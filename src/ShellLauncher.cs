using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ZapretControl
{
    // Opens a URI (tg://, http://, ...) with the token of the interactive
    // desktop shell, not with the elevated token of this process. A direct
    // ShellExecute from an elevated GUI either starts the handler elevated or
    // (for explorer.exe) drops the URI, which is why the Telegram window did
    // not appear. CreateProcessWithTokenW + the shell token keeps Telegram in
    // the normal user session.
    internal static class ShellLauncher
    {
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint TOKEN_DUPLICATE = 0x0002;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint TOKEN_ALL_ACCESS = 0xF01FF;
        private const int SecurityImpersonation = 2;
        private const int TokenPrimary = 1;
        private const uint LOGON_WITH_PROFILE = 0x00000001;
        private const uint CREATE_NO_WINDOW = 0x08000000;
        private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(
            IntPtr hWnd, out uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(
            uint access, bool inheritHandle, uint processId);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(
            IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool DuplicateTokenEx(
            IntPtr existingToken, uint access, IntPtr attributes,
            int impersonationLevel, int tokenType, out IntPtr newToken);

        [DllImport("advapi32.dll", SetLastError = true,
            CharSet = CharSet.Unicode)]
        private static extern bool CreateProcessWithTokenW(
            IntPtr token, uint logonFlags, string applicationName,
            string commandLine, uint creationFlags, IntPtr environment,
            string currentDirectory, ref STARTUPINFO startupInfo,
            out PROCESS_INFORMATION processInformation);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        internal static bool Open(string uri)
        {
            if (string.IsNullOrEmpty(uri))
            {
                return false;
            }
            IntPtr shell = GetShellWindow();
            if (shell == IntPtr.Zero)
            {
                return false;
            }
            uint shellProcessId;
            GetWindowThreadProcessId(shell, out shellProcessId);
            if (shellProcessId == 0)
            {
                return false;
            }

            IntPtr process = IntPtr.Zero;
            IntPtr token = IntPtr.Zero;
            IntPtr primary = IntPtr.Zero;
            try
            {
                process = OpenProcess(
                    PROCESS_QUERY_LIMITED_INFORMATION, false, shellProcessId);
                if (process == IntPtr.Zero)
                {
                    return false;
                }
                if (!OpenProcessToken(
                        process, TOKEN_DUPLICATE | TOKEN_QUERY, out token))
                {
                    return false;
                }
                if (!DuplicateTokenEx(
                        token, TOKEN_ALL_ACCESS, IntPtr.Zero,
                        SecurityImpersonation, TokenPrimary, out primary))
                {
                    return false;
                }

                string command = "\"" +
                    Path.Combine(Environment.SystemDirectory, "cmd.exe") +
                    "\" /c start \"\" \"" + uri + "\"";
                STARTUPINFO startup = new STARTUPINFO();
                startup.cb = Marshal.SizeOf(typeof(STARTUPINFO));
                PROCESS_INFORMATION info;
                bool ok = CreateProcessWithTokenW(
                    primary, LOGON_WITH_PROFILE, null, command,
                    CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT,
                    IntPtr.Zero, null, ref startup, out info);
                if (ok)
                {
                    CloseHandle(info.hProcess);
                    CloseHandle(info.hThread);
                }
                return ok;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (primary != IntPtr.Zero)
                {
                    CloseHandle(primary);
                }
                if (token != IntPtr.Zero)
                {
                    CloseHandle(token);
                }
                if (process != IntPtr.Zero)
                {
                    CloseHandle(process);
                }
            }
        }
    }
}
