using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace ZapretControl
{
    // Registers the at-logon interface sync. The task runs the installed CLI as
    // SYSTEM, so --wf-iface follows the physical adapter even when the GUI is
    // closed. The name is kept from the old script-based task on purpose:
    // /Create /F replaces it during migration.
    internal static class SyncTask
    {
        public const string TaskName = "Zapret-IfIndex-Sync";
        private const string LogonDelay = "0000:20";
        private const int NetworkWaitSeconds = 120;

        public static void Register()
        {
            if (!IsInstalledCopy())
            {
                throw new InvalidOperationException(
                    "Задачу можно зарегистрировать только из установленной копии в Program Files.");
            }

            string executable = GetExecutablePath();
            string arguments = BuildCreateArguments(executable);

            // Stop a legacy script task before replacing it; failures here only
            // mean the task is not running or does not exist yet.
            string ignored;
            RunSchtasks("/End /TN \"" + TaskName + "\"", out ignored);

            string output;
            int exitCode = RunSchtasks(arguments, out output);
            if (exitCode != 0)
            {
                throw new InvalidOperationException(
                    "Не удалось зарегистрировать задачу " + TaskName +
                    " (код " + exitCode + "). " + FirstLine(output));
            }
            if (!IsRegistered())
            {
                throw new InvalidOperationException(
                    "Задача " + TaskName + " не появилась после регистрации.");
            }
        }

        public static void Unregister()
        {
            string output;
            if (RunSchtasks("/Query /TN \"" + TaskName + "\"", out output) != 0)
            {
                return;
            }
            RunSchtasks("/End /TN \"" + TaskName + "\"", out output);
            int exitCode = RunSchtasks("/Delete /TN \"" + TaskName + "\" /F", out output);
            if (exitCode != 0)
            {
                throw new InvalidOperationException(
                    "Не удалось удалить задачу " + TaskName +
                    " (код " + exitCode + "). " + FirstLine(output));
            }
        }

        public static bool IsRegistered()
        {
            string output;
            return RunSchtasks("/Query /TN \"" + TaskName + "\"", out output) == 0;
        }

        internal static string BuildCreateArguments(string executable)
        {
            return "/Create /TN \"" + TaskName +
                "\" /TR \"\\\"" + executable + "\\\" --sync-iface --wait=" +
                NetworkWaitSeconds + "\"" +
                " /SC ONLOGON /DELAY " + LogonDelay +
                " /RU SYSTEM /RL HIGHEST /F";
        }

        public static bool IsInstalledCopy()
        {
            string executable = GetExecutablePath();
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            return IsUnder(executable, programFiles) ||
                   (!string.IsNullOrEmpty(programFilesX86) && IsUnder(executable, programFilesX86));
        }

        private static bool IsUnder(string path, string root)
        {
            if (string.IsNullOrEmpty(root))
            {
                return false;
            }
            string prefix = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            return Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetExecutablePath()
        {
            Assembly assembly = Assembly.GetEntryAssembly();
            if (assembly != null && !string.IsNullOrEmpty(assembly.Location))
            {
                return Path.GetFullPath(assembly.Location);
            }
            return Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);
        }

        private static int RunSchtasks(string arguments, out string output)
        {
            string schtasks = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "schtasks.exe");
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = schtasks;
            info.Arguments = arguments;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;

            using (Process process = new Process())
            {
                process.StartInfo = info;
                process.Start();
                string standardOutput = process.StandardOutput.ReadToEnd();
                string standardError = process.StandardError.ReadToEnd();
                process.WaitForExit();
                output = (standardOutput + " " + standardError).Trim();
                return process.ExitCode;
            }
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            int newline = text.IndexOfAny(new char[] { '\r', '\n' });
            string line = newline < 0 ? text : text.Substring(0, newline);
            if (line.Length > 200)
            {
                line = line.Substring(0, 200);
            }
            return line.Trim();
        }
    }
}
