// TARGET:dummy.exe
// START_IN:
using LoginPI.Engine.ScriptBase;
using System;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Linq; // only for ToUpperInvariant()

namespace LoginEnterprise.Samples
{
    public class OtpInlineDemo : ScriptBase
    {
        void Execute()
        {
            // === Configure your OTP ===
            const string Base32Secret = "JBSWY3DPEHPK3PXP"; // replace with your Base32 secret
            const int Digits = 6;
            const int PeriodSeconds = 30;
            var Algo = Totp.HashAlgo.SHA1; // SHA1 | SHA256 | SHA512

            string code = Totp.Generate(Base32Secret, Digits, PeriodSeconds, Algo);
            Log("[TOTP] " + code);

            // Example usage:
            // START();
            // MainWindow.FindControl(title: "One-time code", timeout: 10).Type(code);
            // MainWindow.Type("{ENTER}");
        }
    }

    internal static class Totp
    {
        internal enum HashAlgo { SHA1, SHA256, SHA512 }

        internal static string Generate(
            string base32Secret,
            int digits,
            int periodSeconds,
            HashAlgo algo)
        {
            if (string.IsNullOrEmpty(base32Secret)) throw new ArgumentException("Secret required.", "base32Secret");
            if (digits < 6 || digits > 10) throw new ArgumentOutOfRangeException("digits", "Digits must be between 6 and 10.");
            if (periodSeconds <= 0) throw new ArgumentOutOfRangeException("periodSeconds");

            byte[] key = Base32Decode(base32Secret);
            long timestep = GetTimeStep(DateTime.UtcNow, periodSeconds);
            return ComputeOtp(key, timestep, digits, algo);
        }

        private static long GetTimeStep(DateTime utcNow, int periodSeconds)
        {
            // No DateTime.UnixEpoch in older frameworks—do it the classic way.
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            long unix = (long)Math.Floor((utcNow - epoch).TotalSeconds);
            return unix / periodSeconds;
        }

        private static string ComputeOtp(byte[] key, long timestep, int digits, HashAlgo algo)
        {
            // 8-byte big-endian counter
            byte[] msg = BitConverter.GetBytes(timestep);
            if (BitConverter.IsLittleEndian) Array.Reverse(msg);

            HMAC hmac;
            // No switch-expression shenanigans here.
            switch (algo)
            {
                case HashAlgo.SHA1:   hmac = new HMACSHA1(key);   break;
                case HashAlgo.SHA256: hmac = new HMACSHA256(key); break;
                case HashAlgo.SHA512: hmac = new HMACSHA512(key); break;
                default: throw new NotSupportedException("Unsupported algo");
            }

            byte[] hash;
            using (hmac) { hash = hmac.ComputeHash(msg); }

            // Dynamic truncation (no System.Index / ^ operator)
            int offset = hash[hash.Length - 1] & 0x0F;
            int binary =
                  ((hash[offset]     & 0x7F) << 24)
                | ((hash[offset + 1] & 0xFF) << 16)
                | ((hash[offset + 2] & 0xFF) << 8)
                |  (hash[offset + 3] & 0xFF);

            int modulo = Pow10(digits);
            int otp = binary % modulo;
            return otp.ToString().PadLeft(digits, '0');
        }

        private static int Pow10(int n)
        {
            int v = 1;
            for (int i = 0; i < n; i++) v *= 10;
            return v;
        }

        // RFC 4648 Base32 decode (case-insensitive, ignores '=' and whitespace)
        private static byte[] Base32Decode(string input)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            // Clean input without LINQ-heavy stuff; keep compatibility simple
            var sb = new System.Text.StringBuilder(input.Length);
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (c != '=' && !char.IsWhiteSpace(c)) sb.Append(c);
            }
            string clean = sb.ToString().ToUpperInvariant();

            int buffer = 0, bitsLeft = 0;
            var bytes = new List<byte>(clean.Length * 5 / 8 + 1);

            for (int i = 0; i < clean.Length; i++)
            {
                int val = alphabet.IndexOf(clean[i]);
                if (val < 0) throw new FormatException("Invalid Base32 char: " + clean[i]);

                buffer = (buffer << 5) | val;
                bitsLeft += 5;

                if (bitsLeft >= 8)
                {
                    bitsLeft -= 8;
                    bytes.Add((byte)((buffer >> bitsLeft) & 0xFF));
                }
            }
            return bytes.ToArray();
        }
    }
}
