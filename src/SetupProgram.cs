using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ZapretControl
{
    internal static class SetupProgram
    {
        private const int AttachParentProcess = -1;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int processId);

        [STAThread]
        private static int Main(string[] args)
        {
            TryAttachConsole();
            SetupOptions options;
            string mode;
            string error;
            if (!SetupArguments.Parse(args, out options, out mode, out error))
            {
                Output("error=" + error);
                return 64;
            }

            if (mode == "help")
            {
                ShowHelp();
                return 0;
            }
            if (mode == "version")
            {
                Output("version=" + AppInfo.Version);
                return 0;
            }
            if (mode == "selftest")
            {
                try
                {
                    List<string> messages = SetupEngine.SelfTest();
                    foreach (string message in messages)
                    {
                        Output(message);
                    }
                    Output("selftest=OK");
                    return 0;
                }
                catch (Exception ex)
                {
                    Output("error=" + ex.Message);
                    return 1;
                }
            }

            bool elevated = AdminCheck.IsAdministrator();
            if (!elevated)
            {
                if (options.Silent)
                {
                    return RelaunchElevated(args);
                }
                if (!RelaunchElevatedInteractive(args))
                {
                    MessageBox.Show(
                        "Для установки нужны права администратора.",
                        "Zapret Control",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return 3;
                }
                return 0;
            }

            IpsetSwitch.RecoverStaleSafe();

            try
            {
                if (options.Silent || mode == "update-silent" || mode == "rollback")
                {
                    RunSilent(options, mode);
                    return 0;
                }
                using (SetupForm form = new SetupForm(options, mode))
                {
                    return form.ShowDialog() == DialogResult.OK ? 0 : 2;
                }
            }
            catch (Exception ex)
            {
                SafeLog("ERROR " + ex.GetType().Name + ": " + ex.Message);
                if (options.Silent)
                {
                    Output("error=" + ex.Message);
                }
                else
                {
                    MessageBox.Show(
                        ex.Message,
                        "Zapret Control",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                return 1;
            }
        }

        private static void RunSilent(SetupOptions options, string mode)
        {
            Action<string> log = delegate(string message)
            {
                Output(message);
                SafeLog(message);
            };
            if (mode == "rollback")
            {
                SetupEngine.Rollback(options, log);
            }
            else if (mode == "uninstall")
            {
                SetupEngine.Uninstall(options, log);
            }
            else if (mode == "update" || mode == "update-silent")
            {
                SetupEngine.Update(options, log);
            }
            else if (mode == "repair")
            {
                SetupEngine.Repair(options, log);
            }
            else
            {
                SetupEngine.Install(options, log);
            }
        }

        private static int RelaunchElevated(string[] args)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = Application.ExecutablePath;
                info.Arguments = QuoteArguments(NormalizeRelativePaths(args));
                info.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
                info.UseShellExecute = true;
                info.Verb = "runas";
                using (Process process = Process.Start(info))
                {
                    process.WaitForExit();
                    return process.ExitCode;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return 3;
            }
        }

        private static bool RelaunchElevatedInteractive(string[] args)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = Application.ExecutablePath;
                info.Arguments = QuoteArguments(NormalizeRelativePaths(args));
                info.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
                info.UseShellExecute = true;
                info.Verb = "runas";
                Process.Start(info);
                return true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }

        // The elevated child starts in System32 for DLL safety, so a relative
        // /payload=... must be resolved in the original working directory.
        private static string[] NormalizeRelativePaths(string[] args)
        {
            string[] result = new string[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                string argument = args[i] ?? string.Empty;
                string prefix = null;
                if (argument.StartsWith("/payload=", StringComparison.OrdinalIgnoreCase))
                {
                    prefix = argument.Substring(0, "/payload=".Length);
                }
                else if (argument.StartsWith("--payload=", StringComparison.OrdinalIgnoreCase))
                {
                    prefix = argument.Substring(0, "--payload=".Length);
                }
                if (prefix != null)
                {
                    string value = argument.Substring(prefix.Length);
                    if (value.Length > 0 && !Path.IsPathRooted(value))
                    {
                        try
                        {
                            value = Path.GetFullPath(value);
                        }
                        catch (Exception)
                        {
                        }
                    }
                    result[i] = prefix + value;
                }
                else
                {
                    result[i] = argument;
                }
            }
            return result;
        }

        private static string QuoteArguments(string[] args)
        {
            StringBuilder builder = new StringBuilder();
            foreach (string argument in args)
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }
                builder.Append('"').Append(argument.Replace("\"", "\\\"")).Append('"');
            }
            return builder.ToString();
        }

        private static void ShowHelp()
        {
            MessageBox.Show(
                "Zapret Control Setup\r\n\r\n" +
                "ZapretControl.Setup.exe install             установить (окно)\r\n" +
                "ZapretControl.Setup.exe /S                  тихая установка\r\n" +
                "ZapretControl.Setup.exe /update [/S]        обновить приложение\r\n" +
                "ZapretControl.Setup.exe /uninstall [/S]     удалить приложение\r\n" +
                "ZapretControl.Setup.exe /repair             переустановить файлы и службу\r\n" +
                "ZapretControl.Setup.exe /rollback           откатить payload\r\n" +
                "ZapretControl.Setup.exe --selftest          самопроверка без установки\r\n" +
                "ZapretControl.Setup.exe --version           версия\r\n\r\n" +
                "Параметры: /payload=<zip|папка> /payload-sha256=<hash> /strategy=<имя.bat>\r\n" +
                "/wait-pid=<pid> /no-launch /keep-data /remove-data\r\n" +
                "/no-service /no-task /no-shortcuts /force",
                "Zapret Control",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private static void TryAttachConsole()
        {
            try
            {
                if (AttachConsole(AttachParentProcess))
                {
                    StreamWriter writer = new StreamWriter(
                        Console.OpenStandardOutput(), new UTF8Encoding(false));
                    writer.AutoFlush = true;
                    Console.SetOut(writer);
                    StreamWriter errorWriter = new StreamWriter(
                        Console.OpenStandardError(), new UTF8Encoding(false));
                    errorWriter.AutoFlush = true;
                    Console.SetError(errorWriter);
                }
            }
            catch (Exception)
            {
            }
        }

        private static void Output(string message)
        {
            try
            {
                Console.WriteLine(message);
            }
            catch (Exception)
            {
            }
        }

        private static void SafeLog(string message)
        {
            try
            {
                AppPaths.EnsureDirectory(AppPaths.LogDirectory);
                File.AppendAllText(
                    AppPaths.SetupLogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + message + Environment.NewLine,
                    new UTF8Encoding(true));
            }
            catch (Exception)
            {
            }
        }
    }

    internal static class SetupArguments
    {
        public static bool Parse(
            string[] args,
            out SetupOptions options,
            out string mode,
            out string error)
        {
            options = new SetupOptions();
            mode = "install";
            error = null;
            if (args == null)
            {
                args = new string[0];
            }
            bool explicitSilent = false;
            bool modeSet = false;

            for (int i = 0; i < args.Length; i++)
            {
                string raw = args[i] ?? string.Empty;
                string argument = raw.Trim();
                string lower = argument.ToLowerInvariant();
                if (lower == "/s" || lower == "/silent" || lower == "--silent")
                {
                    explicitSilent = true;
                    options.Silent = true;
                    continue;
                }
                if (lower == "install" || lower == "/install")
                {
                    SetMode(ref mode, ref modeSet, "install", out error);
                    if (error != null) { return false; }
                    continue;
                }
                if (lower == "update" || lower == "/update")
                {
                    SetMode(ref mode, ref modeSet, "update", out error);
                    if (error != null) { return false; }
                    continue;
                }
                if (lower == "uninstall" || lower == "/uninstall")
                {
                    SetMode(ref mode, ref modeSet, "uninstall", out error);
                    if (error != null) { return false; }
                    continue;
                }
                if (lower == "repair" || lower == "/repair")
                {
                    SetMode(ref mode, ref modeSet, "repair", out error);
                    if (error != null) { return false; }
                    continue;
                }
                if (lower == "rollback" || lower == "/rollback")
                {
                    SetMode(ref mode, ref modeSet, "rollback", out error);
                    if (error != null) { return false; }
                    continue;
                }
                if (lower == "--selftest" || lower == "/selftest")
                {
                    mode = "selftest";
                    return true;
                }
                if (lower == "--version" || lower == "/version")
                {
                    mode = "version";
                    return true;
                }
                if (lower == "--help" || lower == "/help" || lower == "/?" || lower == "-h")
                {
                    mode = "help";
                    return true;
                }
                if (lower == "/no-launch" || lower == "--no-launch")
                {
                    options.NoLaunch = true;
                    continue;
                }
                if (lower == "/keep-data" || lower == "--keep-data")
                {
                    options.KeepData = true;
                    continue;
                }
                if (lower == "/remove-data" || lower == "--remove-data")
                {
                    options.KeepData = false;
                    continue;
                }
                if (lower == "/no-service" || lower == "--no-service")
                {
                    options.InstallService = false;
                    continue;
                }
                if (lower == "/no-task" || lower == "--no-task")
                {
                    options.RegisterTask = false;
                    continue;
                }
                if (lower == "/no-shortcuts" || lower == "--no-shortcuts")
                {
                    options.CreateShortcuts = false;
                    continue;
                }
                if (lower == "/force" || lower == "--force")
                {
                    options.Force = true;
                    continue;
                }
                string value;
                if (TryValue(argument, "/payload", out value) ||
                    TryValue(argument, "--payload", out value))
                {
                    options.PayloadZip = value;
                    continue;
                }
                if (TryValue(argument, "/strategy", out value) ||
                    TryValue(argument, "--strategy", out value))
                {
                    options.Strategy = value;
                    continue;
                }
                if (TryValue(argument, "/wait-pid", out value) ||
                    TryValue(argument, "--wait-pid", out value))
                {
                    int pid;
                    if (!int.TryParse(value, out pid) || pid < 0)
                    {
                        error = "invalid_wait_pid";
                        return false;
                    }
                    options.WaitPid = pid;
                    continue;
                }
                if (TryValue(argument, "/payload-sha256", out value) ||
                    TryValue(argument, "--payload-sha256", out value))
                {
                    options.PayloadSha256 = value;
                    continue;
                }
                error = "unknown_argument:" + argument;
                return false;
            }

            if (Directory.Exists(options.PayloadZip))
            {
                options.PayloadDirectory = options.PayloadZip;
                options.PayloadZip = string.Empty;
            }
            options.Silent = explicitSilent;
            if (!modeSet && explicitSilent)
            {
                mode = "install";
            }
            return true;
        }

        private static void SetMode(
            ref string mode, ref bool modeSet, string value, out string error)
        {
            error = null;
            if (modeSet)
            {
                error = "multiple_modes";
                return;
            }
            mode = value;
            modeSet = true;
        }

        private static bool TryValue(string argument, string name, out string value)
        {
            value = null;
            if (argument.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            {
                value = argument.Substring(name.Length + 1).Trim();
                return value.Length > 0;
            }
            return false;
        }
    }
}
