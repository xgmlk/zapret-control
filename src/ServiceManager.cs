using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace ZapretControl
{
    public sealed class ServiceInstallResult
    {
        public string StrategyName { get; private set; }
        public string CommandLine { get; private set; }
        public bool StartRequested { get; private set; }
        public bool Started { get; private set; }
        public bool ServiceRunning { get; private set; }
        public bool RolledBack { get; private set; }
        public bool PreviousRunning { get; private set; }
        public string PreviousStrategyName { get; private set; }
        public string Error { get; private set; }
        public string StatusText { get; private set; }
        public int WinwsPid { get; private set; }

        public ServiceInstallResult(
            string strategyName,
            string commandLine,
            bool startRequested,
            bool started,
            bool serviceRunning,
            bool rolledBack,
            bool previousRunning,
            string previousStrategyName,
            string error,
            string statusText,
            int winwsPid)
        {
            StrategyName = strategyName;
            CommandLine = commandLine;
            StartRequested = startRequested;
            Started = started;
            ServiceRunning = serviceRunning;
            RolledBack = rolledBack;
            PreviousRunning = previousRunning;
            PreviousStrategyName = previousStrategyName ?? string.Empty;
            Error = error ?? string.Empty;
            StatusText = statusText ?? string.Empty;
            WinwsPid = winwsPid;
        }
    }

    // Native replacement for service.bat: install/remove/strategy selection.
    public static class ServiceManager
    {
        private const string ServiceKeyPath =
            @"SYSTEM\CurrentControlSet\Services\" + AppInfo.ServiceName;
        private const string StrategyValueName = "zapret-discord-youtube";

        public static ServiceInstallResult Install(
            string payloadRoot,
            string strategyPath,
            bool start,
            int ifaceIndex)
        {
            GameFilterSettings game = GameFilterSettings.Load(payloadRoot);
            StrategyCommand command = StrategyParser.Build(
                payloadRoot, strategyPath, game, ifaceIndex);
            return InstallCommand(payloadRoot, command, start);
        }

        public static ServiceInstallResult InstallCommand(
            string payloadRoot,
            StrategyCommand command,
            bool start)
        {
            if (command == null)
            {
                throw new ArgumentNullException("command");
            }

            Diagnostics.EnableTimestamps();
            bool hadService = NativeService.Exists(AppInfo.ServiceName);
            string previousImagePath = hadService ? AppPaths.GetServiceImagePath() : null;
            string previousStrategy = hadService ? GetCurrentStrategyName() : string.Empty;
            ZapretStatus previousStatus = hadService ? ZapretService.GetStatus() : null;
            bool previousRunning = previousStatus != null &&
                                   previousStatus.State == ZapretState.Running;
            int previousPid = previousStatus == null ? 0 : previousStatus.WinwsPid;
            bool preserveManualStart = hadService &&
                                       !NativeService.IsAutoStart(AppInfo.ServiceName);
            bool started = false;
            bool serviceRunning = false;
            bool rolledBack = false;
            bool previousRestoredRunning = false;
            string statusText = string.Empty;
            string failure = string.Empty;
            int winwsPid = 0;
            try
            {
                StopAndDelete(AppInfo.ServiceName);
                // The old service can report Stopped before its winws.exe
                // releases WinDivert/ports. A stale process makes the new
                // service exit immediately with code 1.
                if (!WaitForWinwsExit(5000))
                {
                    if (!hadService)
                    {
                        throw new InvalidOperationException(
                            "winws.exe уже запущен вне службы. Остановите его и повторите.");
                    }
                    Log("stale winws.exe still running; stopping it before install");
                    if (previousPid > 0)
                    {
                        KillWinws(previousPid);
                    }
                    else
                    {
                        KillWinws();
                    }
                    if (!WaitForWinwsExit(5000))
                    {
                        Log("winws.exe is still running after cleanup");
                    }
                }
                NativeService.Install(
                    AppInfo.ServiceName,
                    AppInfo.ServiceName,
                    command.CommandLine,
                    "Zapret DPI bypass software");
                if (preserveManualStart)
                {
                    try
                    {
                        NativeService.SetAutoStart(AppInfo.ServiceName, false);
                    }
                    catch (Exception ex)
                    {
                        Log("autostart restore failed: " +
                            ex.GetType().Name + ": " + ex.Message);
                    }
                }
                WriteStrategyName(command.StrategyName);

                if (start)
                {
                    StartAttempt attempt = TryStartService();
                    started = attempt.Running;
                    serviceRunning = attempt.Running;
                    statusText = attempt.StatusText;
                    winwsPid = attempt.WinwsPid;
                    if (!started)
                    {
                        failure = attempt.Error;
                    }
                }
                else
                {
                    ZapretStatus status = ZapretService.GetStatus();
                    serviceRunning = status.State == ZapretState.Running;
                    statusText = status.State + " (" + status.ServiceStatus + ")";
                    winwsPid = status.WinwsPid;
                }
            }
            catch (Exception ex)
            {
                failure = "Не удалось установить службу: " + DescribeException(ex);
                try
                {
                    ZapretStatus status = ZapretService.GetStatus();
                    serviceRunning = status.State == ZapretState.Running;
                    statusText = status.State + " (" + status.ServiceStatus + ")";
                    winwsPid = status.WinwsPid;
                }
                catch (Exception)
                {
                }
            }

            string error = failure;
            if (!string.IsNullOrEmpty(failure) && CanRollback(hadService, previousImagePath))
            {
                Log("rollback requested: " + OneLine(failure));
                string restoreError;
                bool restored = TryRestorePrevious(
                    previousImagePath,
                    previousStrategy,
                    preserveManualStart,
                    previousRunning,
                    out restoreError);
                rolledBack = restored;
                if (restored)
                {
                    previousRestoredRunning = previousRunning;
                    ZapretStatus restoredStatus = ZapretService.GetStatus();
                    serviceRunning = restoredStatus.State == ZapretState.Running;
                    statusText = restoredStatus.State + " (" +
                                 restoredStatus.ServiceStatus + ")";
                    winwsPid = restoredStatus.WinwsPid;
                    error += "\nВосстановлена предыдущая стратегия " +
                             (string.IsNullOrEmpty(previousStrategy)
                                 ? "(без имени)"
                                 : previousStrategy) + ".";
                    if (previousRunning && !serviceRunning)
                    {
                        error += " Но она тоже не запустилась: " +
                                 restoredStatus.State + ".";
                    }
                }
                else
                {
                    error += "\nВосстановить предыдущую стратегию не удалось: " +
                             restoreError;
                }
            }
            else if (!string.IsNullOrEmpty(failure) && hadService &&
                     string.IsNullOrEmpty(previousImagePath))
            {
                Log("rollback skipped: previous ImagePath is unavailable");
                error += "\nВосстановление невозможно: не удалось прочитать " +
                         "предыдущий ImagePath службы.";
            }

            string actualStrategy = GetCurrentStrategyName();
            if (string.IsNullOrEmpty(actualStrategy))
            {
                actualStrategy = rolledBack ? previousStrategy : command.StrategyName;
            }
            Log("install strategy=" + command.StrategyName +
                " started=" + (started ? "yes" : "no") +
                " running=" + (serviceRunning ? "yes" : "no") +
                " rolled_back=" + (rolledBack ? "yes" : "no") +
                " root=" + payloadRoot);
            if (!string.IsNullOrEmpty(error))
            {
                Log("start failure: " + OneLine(error));
            }
            return new ServiceInstallResult(
                string.IsNullOrEmpty(actualStrategy)
                    ? command.StrategyName
                    : actualStrategy,
                command.CommandLine,
                start,
                started,
                serviceRunning,
                rolledBack,
                previousRestoredRunning,
                previousStrategy,
                error,
                statusText,
                winwsPid);
        }

        private sealed class StartAttempt
        {
            public bool Running { get; private set; }
            public string StatusText { get; private set; }
            public string Error { get; private set; }
            public int WinwsPid { get; private set; }

            public StartAttempt(
                bool running, string statusText, string error, int winwsPid)
            {
                Running = running;
                StatusText = statusText ?? string.Empty;
                Error = error ?? string.Empty;
                WinwsPid = winwsPid;
            }
        }

        private static StartAttempt TryStartService()
        {
            string error = string.Empty;
            bool timedOut = false;
            try
            {
                ZapretService.Start();
            }
            catch (System.ServiceProcess.TimeoutException)
            {
                timedOut = true;
            }
            catch (Exception ex)
            {
                error = DescribeException(ex);
            }

            // A service can reach Running and exit immediately (bad winws
            // arguments, missing driver). Poll for a stable PID before
            // reporting success to the user.
            ZapretStatus status = ZapretService.GetStatus();
            bool running = status.State == ZapretState.Running;
            if (running && !timedOut && string.IsNullOrEmpty(error))
            {
                int stablePid = status.WinwsPid;
                for (int i = 0; i < 3; i++)
                {
                    Thread.Sleep(750);
                    status = ZapretService.GetStatus();
                    if (status.State != ZapretState.Running ||
                        status.WinwsPid <= 0 ||
                        (stablePid > 0 && status.WinwsPid != stablePid))
                    {
                        running = false;
                        break;
                    }
                    stablePid = status.WinwsPid;
                }
            }
            List<string> lines = new List<string>();
            lines.Add("Служба не перешла в состояние Running.");
            lines.Add("Статус: " + status.State + " (" + status.ServiceStatus + ").");
            if (timedOut)
            {
                lines.Add("Ожидание Running истекло (25 с): возможен StartPending.");
            }
            if (!string.IsNullOrEmpty(error))
            {
                lines.Add("Ошибка: " + error + ".");
            }
            lines.Add(status.WinwsPid > 0
                ? "winws.exe PID " + status.WinwsPid + "."
                : "winws.exe не найден.");
            string log = LastLogLines("service.log", 3);
            if (!string.IsNullOrEmpty(log))
            {
                lines.Add("Последние строки logs\\service.log:");
                lines.Add(log);
            }
            return new StartAttempt(
                running,
                status.State + " (" + status.ServiceStatus + ")",
                running ? string.Empty : Join(lines, "\n"),
                status.WinwsPid);
        }

        private static bool TryRestorePrevious(
            string imagePath,
            string strategyName,
            bool manualStart,
            bool start,
            out string error)
        {
            error = string.Empty;
            try
            {
                int currentPid = ZapretService.GetStatus().WinwsPid;
                StopAndDelete(AppInfo.ServiceName);
                if (!WaitForWinwsExit(5000))
                {
                    if (currentPid > 0)
                    {
                        KillWinws(currentPid);
                    }
                    else
                    {
                        KillWinws();
                    }
                    WaitForWinwsExit(5000);
                }
                NativeService.Install(
                    AppInfo.ServiceName,
                    AppInfo.ServiceName,
                    imagePath,
                    "Zapret DPI bypass software");
                if (manualStart)
                {
                    NativeService.SetAutoStart(AppInfo.ServiceName, false);
                }
                if (!string.IsNullOrEmpty(strategyName))
                {
                    WriteStrategyName(strategyName);
                }
                else
                {
                    ClearStrategyName();
                }
                if (start)
                {
                    StartAttempt attempt = TryStartService();
                    if (!attempt.Running)
                    {
                        // The old service and strategy are back, but it did not
                        // reach Running. Report the configuration as restored
                        // so the caller can show the real state instead of
                        // claiming that rollback itself failed.
                        error = attempt.Error;
                        return true;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = DescribeException(ex);
                Log("rollback failed: " + OneLine(error));
                return false;
            }
        }

        internal static bool CanRollback(bool hadService, string previousImagePath)
        {
            return hadService && !string.IsNullOrEmpty(previousImagePath);
        }

        private static bool WaitForWinwsExit(int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (!HasWinws())
                {
                    return true;
                }
                Thread.Sleep(250);
            }
            return !HasWinws();
        }

        private static bool HasWinws()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName(ZapretService.ProcessName);
                bool found = processes.Length > 0;
                foreach (Process process in processes)
                {
                    process.Dispose();
                }
                return found;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void Remove()
        {
            StopAndDelete(AppInfo.ServiceName);
            KillWinws();
            RemoveForeignService("WinDivert");
            RemoveForeignService("WinDivert14");
            RemoveForeignService("GoodbyeDPI");
            Log("remove service=" + AppInfo.ServiceName);
        }

        public static string GetCurrentStrategyName()
        {
            string value = AppPaths.GetServiceStrategy();
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            value = value.Trim();
            if (value.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(0, value.Length - 4);
            }
            return value;
        }

        public static string FindStrategyPath(string payloadRoot, string name)
        {
            if (string.IsNullOrEmpty(payloadRoot) || string.IsNullOrEmpty(name))
            {
                return null;
            }
            string clean = name.Trim();
            if (clean.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                clean = clean.Substring(0, clean.Length - 4);
            }
            if (clean.Length == 0 || clean.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                clean.IndexOf('\\') >= 0 || clean.IndexOf('/') >= 0)
            {
                return null;
            }
            try
            {
                foreach (string file in Directory.GetFiles(
                    payloadRoot, "*.bat", SearchOption.TopDirectoryOnly))
                {
                    if (Path.GetFileNameWithoutExtension(file).Equals(
                            clean, StringComparison.OrdinalIgnoreCase) &&
                        !AppPaths.IsReparsePoint(file))
                    {
                        return file;
                    }
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        public static string ChooseDefaultStrategy(string payloadRoot, string preferred)
        {
            string path = FindStrategyPath(payloadRoot, preferred);
            if (!string.IsNullOrEmpty(path))
            {
                return path;
            }
            System.Collections.Generic.List<StrategyInfo> strategies =
                StrategyParser.ListStrategies(payloadRoot);
            if (strategies.Count == 0)
            {
                return null;
            }
            foreach (StrategyInfo info in strategies)
            {
                if (info.Name.Equals("general", StringComparison.OrdinalIgnoreCase))
                {
                    return info.Path;
                }
            }
            return strategies[0].Path;
        }

        public static void StopAndDelete(string serviceName)
        {
            if (!NativeService.Exists(serviceName))
            {
                return;
            }
            StopServiceOnly(serviceName);
            NativeService.Delete(serviceName);
        }

        // Stops the service without deleting it. Used when a payload updates but
        // the caller asked to keep the existing service (/no-service).
        public static void StopServiceOnly(string serviceName)
        {
            if (!NativeService.Exists(serviceName))
            {
                return;
            }
            try
            {
                using (ServiceController controller = new ServiceController(serviceName))
                {
                    if (controller.Status == ServiceControllerStatus.StartPending)
                    {
                        TryWait(controller, ServiceControllerStatus.Running, 20);
                    }
                    if (controller.Status != ServiceControllerStatus.Stopped)
                    {
                        controller.Stop();
                        TryWait(controller, ServiceControllerStatus.Stopped, 30);
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }
            catch (System.ServiceProcess.TimeoutException)
            {
                // DeleteService reports the real error if the service is stuck.
            }
        }

        public static void RemoveForeignService(string serviceName)
        {
            if (!NativeService.Exists(serviceName))
            {
                return;
            }
            try
            {
                using (ServiceController controller = new ServiceController(serviceName))
                {
                    if (controller.Status != ServiceControllerStatus.Stopped)
                    {
                        controller.Stop();
                        TryWait(controller, ServiceControllerStatus.Stopped, 15);
                    }
                }
            }
            catch (Exception)
            {
            }
            try
            {
                NativeService.Delete(serviceName);
            }
            catch (Exception)
            {
            }
        }

        public static void KillWinws()
        {
            try
            {
                foreach (Process process in Process.GetProcessesByName("winws"))
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                    catch (Exception)
                    {
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private static void KillWinws(int pid)
        {
            try
            {
                using (Process process = Process.GetProcessById(pid))
                {
                    if (!process.ProcessName.Equals(
                            ZapretService.ProcessName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        public static void WriteStrategyName(string strategyName)
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(ServiceKeyPath, true))
            {
                if (key == null)
                {
                    throw new InvalidOperationException(
                        "Не удалось открыть ключ службы zapret для записи.");
                }
                key.SetValue(StrategyValueName, strategyName, RegistryValueKind.String);
            }
        }

        private static void ClearStrategyName()
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(ServiceKeyPath, true))
            {
                if (key == null)
                {
                    return;
                }
                try
                {
                    key.DeleteValue(StrategyValueName, false);
                }
                catch (Exception)
                {
                }
            }
        }

        private static void TryWait(
            ServiceController controller,
            ServiceControllerStatus status,
            int seconds)
        {
            try
            {
                controller.WaitForStatus(status, TimeSpan.FromSeconds(seconds));
            }
            catch (System.ServiceProcess.TimeoutException)
            {
            }
        }

        private static string LastLogLines(string fileName, int count)
        {
            try
            {
                string path = Path.Combine(AppPaths.LogDirectory, fileName);
                if (!File.Exists(path))
                {
                    return string.Empty;
                }
                string[] lines = File.ReadAllLines(path);
                int start = Math.Max(0, lines.Length - count);
                StringBuilder builder = new StringBuilder();
                for (int i = start; i < lines.Length; i++)
                {
                    string line = lines[i].TrimEnd();
                    if (line.Length == 0)
                    {
                        continue;
                    }
                    if (builder.Length > 0)
                    {
                        builder.Append('\n');
                    }
                    builder.Append(line.Length > 200 ? line.Substring(0, 200) : line);
                }
                return builder.ToString();
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string Join(List<string> values, string separator)
        {
            StringBuilder builder = new StringBuilder();
            foreach (string value in values)
            {
                if (builder.Length > 0)
                {
                    builder.Append(separator);
                }
                builder.Append(value);
            }
            return builder.ToString();
        }

        private static string OneLine(string value)
        {
            return (value ?? string.Empty)
                .Replace("\r\n", " | ")
                .Replace('\n', ' ')
                .Replace('\r', ' ');
        }

        private static string DescribeException(Exception ex)
        {
            string text = ex.GetType().Name + ": " + (ex.Message ?? string.Empty);
            Win32Exception win32 = ex as Win32Exception;
            if (win32 == null)
            {
                win32 = ex.InnerException as Win32Exception;
            }
            if (win32 != null && win32.NativeErrorCode != 0)
            {
                text += " (код " + win32.NativeErrorCode + ")";
            }
            return text;
        }

        private static void Log(string message)
        {
            try
            {
                AppPaths.EnsureDirectory(AppPaths.LogDirectory);
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                              " " + message + Environment.NewLine;
                File.AppendAllText(
                    Path.Combine(AppPaths.LogDirectory, "service.log"),
                    line,
                    new System.Text.UTF8Encoding(true));
            }
            catch (Exception)
            {
            }
        }
    }
}
