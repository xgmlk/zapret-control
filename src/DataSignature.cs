using System;
using System.Security.Cryptography;
using System.Text;

namespace ZapretControl
{
    // ECDSA P-256 / SHA-256 over the exact bytes of data.json. Signatures are
    // IEEE P1363 (r||s), which is what ECDsaCng.SignData produces and what the
    // PowerShell signer in tools/build-data.ps1 writes.
    internal static class DataSignature
    {
        internal static bool Verify(
            byte[] data, byte[] signature, out string keyName)
        {
            keyName = null;
            if (data == null || data.Length == 0 || signature == null ||
                signature.Length == 0)
            {
                return false;
            }
            string[] keys = new string[]
            {
                AppInfo.DataPublicKeyBlob,
                AppInfo.DataPublicKeyBlobNext
            };
            for (int i = 0; i < keys.Length; i++)
            {
                if (string.IsNullOrEmpty(keys[i]))
                {
                    continue;
                }
                if (VerifyWithKey(data, signature, keys[i]))
                {
                    keyName = i == 0 ? "key1" : "key2";
                    return true;
                }
            }
            return false;
        }

        internal static bool VerifyWithKey(
            byte[] data, byte[] signature, string publicKeyBase64)
        {
            try
            {
                byte[] blob = Convert.FromBase64String(publicKeyBase64);
                using (CngKey key = CngKey.Import(
                    blob, CngKeyBlobFormat.EccPublicBlob))
                using (ECDsaCng ecdsa = new ECDsaCng(key))
                {
                    return ecdsa.VerifyData(
                        data, signature, HashAlgorithmName.SHA256);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static byte[] DecodeSignature(string base64)
        {
            if (string.IsNullOrEmpty(base64))
            {
                return null;
            }
            try
            {
                StringBuilder cleaned = new StringBuilder(base64.Length);
                for (int i = 0; i < base64.Length; i++)
                {
                    char c = base64[i];
                    if (!char.IsWhiteSpace(c))
                    {
                        cleaned.Append(c);
                    }
                }
                return Convert.FromBase64String(cleaned.ToString());
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
