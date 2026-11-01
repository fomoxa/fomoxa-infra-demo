using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Fomoxa.Networking.Transports;

namespace MyGame.Network
{
    /// <summary>
    /// Kiểm tra chứng chỉ TLS của backend bằng vân tay SHA-256 của chứng chỉ lá (certificate pinning).
    /// Dùng cho chứng chỉ tự ký khi dev / mạng nội bộ; chứng chỉ do CA công cộng cấp thì không cần pin
    /// (để trống pin, RoomJoiner dùng bộ gốc Mozilla có sẵn của thư viện).
    /// </summary>
    public sealed class PinnedCertificateCheck : ITlsCertificateCheck
    {
        private readonly byte[] pin;

        public PinnedCertificateCheck(string sha256Hex)
        {
            pin = Parse(sha256Hex);
        }

        public static bool IsValidPin(string value)
        {
            try
            {
                return Parse(value).Length == 32;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public bool Accepts(string host, IReadOnlyList<byte[]> chain, out string rejection)
        {
            if (chain == null || chain.Count == 0)
            {
                rejection = "server không gửi chứng chỉ";
                return false;
            }

            using (SHA256 sha = SHA256.Create())
            {
                byte[] actual = sha.ComputeHash(chain[0]);
                if (CryptographicOperations.FixedTimeEquals(actual, pin))
                {
                    rejection = null;
                    return true;
                }
            }

            rejection = "vân tay chứng chỉ của server không khớp với pin đã cấu hình";
            return false;
        }

        private static byte[] Parse(string hex)
        {
            string clean = (hex ?? "").Replace(":", "").Replace(" ", "").Trim();
            if (clean.Length % 2 != 0)
            {
                throw new FormatException("pin phải là chuỗi hex");
            }

            var bytes = new byte[clean.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(clean.Substring(i * 2, 2), 16);
            }

            return bytes;
        }
    }
}
