using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace ZapretControl
{
    public sealed class SignatureCheck
    {
        public bool IsSigned { get; private set; }
        public bool IsValid { get; private set; }
        public string Thumbprint { get; private set; }
        public string Subject { get; private set; }
        public string Error { get; private set; }

        public SignatureCheck(
            bool isSigned,
            bool isValid,
            string thumbprint,
            string subject,
            string error)
        {
            IsSigned = isSigned;
            IsValid = isValid;
            Thumbprint = thumbprint;
            Subject = subject;
            Error = error;
        }
    }

    // Authenticode verification via WinVerifyTrust; used by the app updater.
    public static class SignatureVerifier
    {
        private static readonly Guid GenericVerifyV2 =
            new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        private const uint WtdUiNone = 2;
        private const uint WtdChoiceFile = 1;
        private const uint WtdStateActionVerify = 1;
        private const uint WtdStateActionClose = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct WinTrustFileInfo
        {
            public uint StructSize;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string FilePath;
            public IntPtr FileHandle;
            public IntPtr KnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WinTrustData
        {
            public uint StructSize;
            public IntPtr PolicyCallbackData;
            public IntPtr SipClientData;
            public uint UiChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr FileInfo;
            public uint StateAction;
            public IntPtr StateData;
            public IntPtr UrlReference;
            public uint ProviderFlags;
            public uint UiContext;
            public IntPtr SignatureSettings;
        }

        [DllImport("wintrust.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(
            IntPtr windowHandle,
            [MarshalAs(UnmanagedType.LPStruct)] Guid actionId,
            IntPtr data);

        public static SignatureCheck Verify(string path, string expectedThumbprint)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            {
                return new SignatureCheck(false, false, null, null, "Файл не найден.");
            }

            string thumbprint = null;
            string subject = null;
            try
            {
                X509Certificate certificate = X509Certificate.CreateFromSignedFile(path);
                thumbprint = certificate.GetCertHashString();
                subject = certificate.Subject;
            }
            catch (Exception)
            {
            }

            int result = VerifyTrust(path);
            if (result != 0)
            {
                if (thumbprint == null)
                {
                    return new SignatureCheck(false, false, null, null, "Файл не подписан.");
                }
                return new SignatureCheck(
                    true, false, thumbprint, subject,
                    "Подпись недействительна (0x" + result.ToString("X8") + ").");
            }

            if (!string.IsNullOrEmpty(expectedThumbprint))
            {
                if (thumbprint == null)
                {
                    return new SignatureCheck(
                        true, false, null, null,
                        "Подпись действительна, но сертификат не извлечён для сверки thumbprint.");
                }
                string expected = expectedThumbprint.Replace(" ", string.Empty).ToUpperInvariant();
                if (!string.Equals(thumbprint, expected, StringComparison.OrdinalIgnoreCase))
                {
                    return new SignatureCheck(
                        true, false, thumbprint, subject,
                        "Подпись принадлежит другому сертификату.");
                }
            }
            return new SignatureCheck(true, true, thumbprint, subject, null);
        }

        private static int VerifyTrust(string path)
        {
            WinTrustFileInfo file = new WinTrustFileInfo();
            file.StructSize = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo));
            file.FilePath = path;

            IntPtr filePointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustFileInfo)));
            IntPtr dataPointer = IntPtr.Zero;
            try
            {
                Marshal.StructureToPtr(file, filePointer, false);
                WinTrustData data = new WinTrustData();
                data.StructSize = (uint)Marshal.SizeOf(typeof(WinTrustData));
                data.UiChoice = WtdUiNone;
                data.RevocationChecks = 0;
                data.UnionChoice = WtdChoiceFile;
                data.FileInfo = filePointer;
                data.StateAction = WtdStateActionVerify;
                data.ProviderFlags = 0;

                dataPointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustData)));
                Marshal.StructureToPtr(data, dataPointer, false);
                int result = WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, dataPointer);

                WinTrustData closeData = (WinTrustData)Marshal.PtrToStructure(
                    dataPointer, typeof(WinTrustData));
                closeData.StateAction = WtdStateActionClose;
                Marshal.StructureToPtr(closeData, dataPointer, false);
                WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, dataPointer);
                return result;
            }
            finally
            {
                if (dataPointer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(dataPointer);
                }
                Marshal.FreeHGlobal(filePointer);
            }
        }
    }
}
