using System;
using System.Text;

namespace ramm
{
    public static class SafeBase64Utility
    {
        public static byte[] SafeFromBase64String(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return Array.Empty<byte>();

            // Clean standard URL-safe variations or whitespace/newlines introduced by traces
            string sanitized = input.Trim()
                .Replace('-', '+')
                .Replace('_', '/')
                .Replace(" ", "+");

            // Fix missing padding characters if length isn't a multiple of 4
            switch (sanitized.Length % 4)
            {
                case 2: sanitized += "=="; break;
                case 3: sanitized += "="; break;
            }

            try
            {
                return Convert.FromBase64String(sanitized);
            }
            catch (FormatException)
            {
                // Fallback or return empty array to prevent application crash on corrupted artifact traces
                return Array.Empty<byte>();
            }
        }
    }
}