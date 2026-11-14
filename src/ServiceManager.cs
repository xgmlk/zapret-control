using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using Microsoft.Win32;

namespace ZapretControl
{
    public sealed class ServiceInstallResult
    {
        public string StrategyName { get; private set; }
        public string CommandLine { get; private set; }
        public bool Started { get; private set; }

        public ServiceInstallResult(string strategyName, string commandLine, bool started)
        {
            StrategyName = strategyName;
            CommandLine = commandLine;
            Started = started;
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
            StopAndDelete(AppInfo.ServiceName);
            NativeService.Install(
                AppInfo.ServiceName,
                AppInfo.ServiceName,
                command.CommandLine,
                "Zapret DPI bypass software");
            WriteStrategyName(command.StrategyName);

            bool started = false;
            if (start)
            {
                try
                {
                    ZapretService.Start();
                    started = ZapretService.GetStatus().State == ZapretState.Running;
                }
                catch (Exception ex)
                {
                    Log("start failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            Log("install strategy=" + command.StrategyName +
                " started=" + (started ? "yes" : "no") +
                " root=" + payloadRoot);
            return new ServiceInstallResult(command.StrategyName, command.CommandLine, started);
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
