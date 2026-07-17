using System;
using System.Collections.Generic;

namespace YamaTro.InertialLink.Core
{
    public static class PairingKey
    {
        public static bool TryParseHex(string text, out byte[] key)
        {
            key = null;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var digits = new List<char>(ProtocolConstants.PairingKeyLength * 2);
            foreach (var c in text)
            {
                if (IsHex(c)) digits.Add(c);
                else if (!IsAsciiSeparator(c)) return false;
            }

            if (digits.Count != ProtocolConstants.PairingKeyLength * 2) return false;
            var parsed = new byte[ProtocolConstants.PairingKeyLength];
            for (var i = 0; i < parsed.Length; i++)
            {
                parsed[i] = (byte)((Nibble(digits[i * 2]) << 4) | Nibble(digits[(i * 2) + 1]));
            }

            key = parsed;
            return true;
        }

        private static bool IsHex(char c) { return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'); }
        private static bool IsAsciiSeparator(char c) { return c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '-'; }
        private static int Nibble(char c) { return c <= '9' ? c - '0' : (char.ToLowerInvariant(c) - 'a') + 10; }
    }
}
