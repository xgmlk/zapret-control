using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ZapretControl
{
    public static class HashUtil
    {
        public static string ComputeSha256(string path)
        {
            using (FileStream stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(stream);
                return ToHex(hash);
            }
        }

        public static string ComputeSha256(byte[] data)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                return ToHex(algorithm.ComputeHash(data));
            }
        }

        public static string ToHex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("X2"));
            }
            return builder.ToString();
        }

        public static bool Matches(string path, string expectedHash)
        {
            if (string.IsNullOrEmpty(expectedHash) || !File.Exists(path))
            {
                return false;
            }
            string actual = ComputeSha256(path);
            return string.Equals(
                actual, Normalize(expectedHash), StringComparison.OrdinalIgnoreCase);
        }

        public static string Normalize(string hash)
        {
            if (hash == null)
            {
                return string.Empty;
            }
            return hash.Trim().Replace(" ", string.Empty).Replace("-", string.Empty).ToUpperInvariant();
        }
    }
}
