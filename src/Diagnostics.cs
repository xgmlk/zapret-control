using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ZapretControl
{
    public enum DiagnosticState
    {
        Pass,
        Warning,
        Fail,
        Info
    }

    public sealed class DiagnosticItem
    {
        public string Name { get; private set; }
        public DiagnosticState State { get; private set; }
        public string Detail { get; private set; }

        public DiagnosticItem(string name, DiagnosticState state, string detail)
        {
            Name = name;
            State = state;
            Detail = detail;
        }

        public string Symbol
        {
            get
            {
                switch (State)
                {
                    case DiagnosticState.Pass:
                        return "OK";
                    case DiagnosticState.Warning:
                        return "!";
                    case DiagnosticState.Fail:
                        return "X";
                    default:
                        return "i";
                }
            }
        }
    }

    public sealed class DiagnosticReport
    {
        public List<DiagnosticItem> Items { get; private set; }
        public DateTime GeneratedAt { get; private set; }

        public DiagnosticReport()
        {
            Items = new List<DiagnosticItem>();
            GeneratedAt = DateTime.Now;
        }

        public int Count(DiagnosticState state)
        {
            int count = 0;
            foreach (DiagnosticItem item in Items)
            {
                if (item.State == state)
                {
                    count++;
                }
            }
            return count;
        }

        public string ToText()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Zapret Control " + AppInfo.Version +
                               " — диагностика " +
                               GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            foreach (DiagnosticItem item in Items)
            {
                builder.Append("[").Append(item.Symbol).Append("] ")
                    .Append(item.Name).Append(": ").AppendLine(item.Detail);
            }
            return builder.ToString();
        }
    }

    // Native port of the Diagnostics menu in service.bat.
    public static class Diagnostics
    {
        public static DiagnosticReport Run(string payloadRoot)
        {
            DiagnosticReport report = new DiagnosticReport();
            string root = payloadRoot ?? string.Empty;

            Add(report, "Zapret", DiagnosticState.Info, root);

            string validationError;
            if (PayloadManager.ValidatePayloadRoot(root, out validationError))
            {
                Add(report, "Payload", DiagnosticState.Pass,
                    "winws.exe, WinDivert64.sys и стратегии на месте.");
            }
            else
            {
                Add(report, "Payload", DiagnosticState.Fail, validationError);
            }

            string winws = Path.Combine(root, @"bin\winws.exe");
            if (File.Exists(winws))
            {
                Add(report, "winws.exe", DiagnosticState.Pass, winws);
            }
            else
            {
                Add(report, "winws.exe", DiagnosticState.Fail, "Файл не найден: " + winws);
            }

            string sys = Path.Combine(root, @"bin\WinDivert64.sys");
            if (File.Exists(sys))
            {
                Add(report, "WinDivert64.sys", DiagnosticState.Pass, "Файл на месте.");
            }
            else
            {
                Add(report, "WinDivert64.sys", DiagnosticState.Fail,
                    "Файл не найден. Драйвер WinDivert не установится.");
            }

            ZapretStatus service = ZapretService.GetStatus();
            Add(report, "Служба zapret", ServiceState(service), ServiceDetail(service));

            bool winwsRunning = false;
            Process[] winwsProcesses = Process.GetProcessesByName("winws");
            foreach (Process process in winwsProcesses)
            {
                winwsRunning = true;
                process.Dispose();
            }
            if (service.State == ZapretState.Running && !winwsRunning)
            {
                Add(report, "winws.exe процесс", DiagnosticState.Warning,
                    "Служба запущена, но процесс winws.exe не найден.");
            }
            else if (service.State != ZapretState.Running && winwsRunning)
            {
                Add(report, "winws.exe процесс", DiagnosticState.Warning,
                    "winws.exe работает без службы zapret.");
            }
            else
            {
                Add(report, "winws.exe процесс", DiagnosticState.Pass,
                    winwsRunning ? "Процесс работает." : "Процесс не запущен (служба остановлена).");
            }

            AddServiceCheck(report, "Base Filtering Engine", "BFE", true);
            AddProxyCheck(report);
            AddTimestampsCheck(report);
            AddProcessCheck(report, "Adguard", "AdguardSvc",
                "Adguard может мешать Discord.", DiagnosticState.Warning);
            AddServicePattern(report, "Killer", new string[] { "Killer" },
                "Killer конфликтует с zapret.", DiagnosticState.Warning, true);
            AddServicePattern(report, "Intel Connectivity",
                new string[] { "Intel", "Connectivity", "Network" },
                "Intel Connectivity Network Service конфликтует с zapret.",
                DiagnosticState.Warning, true);
            AddServicePattern(report, "Check Point", new string[] { "TracSrvWrapper", "EPWD" },
                "Check Point конфликтует с zapret.", DiagnosticState.Warning, false);
            AddServicePattern(report, "SmartByte", new string[] { "SmartByte" },
                "SmartByte конфликтует с zapret.", DiagnosticState.Warning, true);
            AddServicePattern(report, "VPN", new string[] { "VPN" },
                "VPN-службы могут конфликтовать с zapret.", DiagnosticState.Warning, true);
            AddServicePattern(report, "Конфликтующие обходы",
                new string[] { "GoodbyeDPI", "discordfix_zapret", "winws1", "winws2" },
                "Найдены другие обходы DPI.", DiagnosticState.Warning, false);

            AddPathChecks(report, root);
            AddRegistryChecks(report);
            AddHostsCheck(report);
            AddWinDivertConflict(report, winwsRunning);
            AddIfaceCheck(report);
            AddTaskCheck(report);
            return report;
        }

        private static void Add(
            DiagnosticReport report, string name, DiagnosticState state, string detail)
        {
            report.Items.Add(new DiagnosticItem(name, state, detail));
        }

        private static DiagnosticState ServiceState(ZapretStatus status)
        {
            switch (status.State)
            {
                case ZapretState.Running:
                case ZapretState.Stopped:
                    return DiagnosticState.Pass;
                case ZapretState.NotFound:
                    return DiagnosticState.Fail;
                case ZapretState.AccessDenied:
                    return DiagnosticState.Fail;
                default:
                    return DiagnosticState.Warning;
            }
        }

        private static string ServiceDetail(ZapretStatus status)
        {
            string text = status.State + " (" + status.ServiceStatus + ")";
            if (status.WinwsPid > 0)
            {
                text += ", PID " + status.WinwsPid;
            }
            return text;
        }

        private static void AddServiceCheck(
            DiagnosticReport report, string name, string serviceName, bool required)
        {
            try
            {
                using (ServiceController service = new ServiceController(serviceName))
                {
                    bool running = service.Status == ServiceControllerStatus.Running;
                    Add(report, name, running || !required
                        ? (running ? DiagnosticState.Pass : DiagnosticState.Warning)
                        : DiagnosticState.Fail,
                        "Служба " + serviceName + ": " + service.Status +
                        (running ? string.Empty : ". Для zapret нужна запущенная BFE."));
                }
            }
            catch (InvalidOperationException)
            {
                Add(report, name, required ? DiagnosticState.Fail : DiagnosticState.Warning,
                    "Служба " + serviceName + " не найдена.");
            }
            catch (Exception ex)
            {
                Add(report, name, DiagnosticState.Warning,
                    "Не удалось проверить " + serviceName + ": " + ex.Message);
            }
        }

        private static void AddProxyCheck(DiagnosticReport report)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", false))
                {
                    int enabled = key == null ? 0 : ToInt(key.GetValue("ProxyEnable"));
                    string server = key == null ? null : key.GetValue("ProxyServer") as string;
                    if (enabled == 1)
                    {
                        Add(report, "Системный прокси", DiagnosticState.Warning,
                            "Прокси включён: " + (server ?? "(сервер не указан)") +
                            ". Убедитесь, что он рабочий.");
                    }
                    else
                    {
                        Add(report, "Системный прокси", DiagnosticState.Pass, "Прокси выключен.");
                    }
                }
            }
            catch (Exception ex)
            {
                Add(report, "Системный прокси", DiagnosticState.Warning, ex.Message);
            }
        }

        private static void AddTimestampsCheck(DiagnosticReport report)
        {
            string output = RunHidden("netsh.exe", "interface tcp show global", 10000);
            if (string.IsNullOrEmpty(output))
            {
                Add(report, "TCP timestamps", DiagnosticState.Warning,
                    "Не удалось прочитать настройки TCP.");
                return;
            }
            string[] lines = output.Split(new char[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string text = line.ToLowerInvariant();
                if (text.IndexOf("timestamp", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("отметки времени", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("метки времени", StringComparison.Ordinal) >= 0)
                {
                    bool enabled = text.IndexOf("enabled", StringComparison.Ordinal) >= 0 ||
                                   text.IndexOf("включ", StringComparison.Ordinal) >= 0;
                    if (!enabled && EnableTimestamps())
                    {
                        Add(report, "TCP timestamps", DiagnosticState.Pass,
                            "Были выключены, включены автоматически.");
                    }
                    else
                    {
                        Add(report, "TCP timestamps",
                            enabled ? DiagnosticState.Pass : DiagnosticState.Warning,
                            enabled
                                ? "Включены."
                                : "Выключены. Включите: netsh interface tcp set global timestamps=enabled");
                    }
                    return;
                }
            }
            Add(report, "TCP timestamps", DiagnosticState.Warning,
                "Строка про timestamps не найдена в выводе netsh.");
        }

        private static void AddProcessCheck(
            DiagnosticReport report,
            string label,
            string processName,
            string warning,
            DiagnosticState state)
        {
            try
            {
                Process[] processes = Process.GetProcessesByName(processName);
                bool found = processes.Length > 0;
                foreach (Process process in processes)
                {
                    process.Dispose();
                }
                Add(report, label, found ? state : DiagnosticState.Pass,
                    found ? warning : "Не найден.");
            }
            catch (Exception ex)
            {
                Add(report, label, DiagnosticState.Warning, ex.Message);
            }
        }

        private static void AddServicePattern(
            DiagnosticReport report,
            string label,
            string[] patterns,
            string warning,
            DiagnosticState state,
            bool requireAll)
        {
            try
            {
                List<string> found = new List<string>();
                foreach (ServiceController service in ServiceController.GetServices())
                {
                    string text = service.ServiceName + " " + service.DisplayName;
                    bool match = requireAll;
                    foreach (string pattern in patterns)
                    {
                        bool contains = text.IndexOf(
                            pattern, StringComparison.OrdinalIgnoreCase) >= 0;
                        if (requireAll && !contains)
                        {
                            match = false;
                            break;
                        }
                        if (!requireAll && contains)
                        {
                            match = true;
                            break;
                        }
                    }
                    if (match)
                    {
                        found.Add(service.ServiceName);
                    }
                    service.Dispose();
                }
                if (found.Count > 0)
                {
                    Add(report, label, state, warning + " Найдено: " + string.Join(", ", found.ToArray()));
                }
                else
                {
                    Add(report, label, DiagnosticState.Pass, "Не найдено.");
                }
            }
            catch (Exception ex)
            {
                Add(report, label, DiagnosticState.Warning, ex.Message);
            }
        }

        private static void AddPathChecks(DiagnosticReport report, string root)
        {
            if (Regex.IsMatch(root, "[\\u0400-\\u04FF]"))
            {
                Add(report, "Кириллица в пути", DiagnosticState.Warning,
                    "Путь содержит кириллицу. Если обход не работает, перенесите zapret в C:\\zapret.");
            }
            else
            {
                Add(report, "Кириллица в пути", DiagnosticState.Pass, "Путь без кириллицы.");
            }

            string oneDrive = Environment.GetEnvironmentVariable("OneDrive");
            if (!string.IsNullOrEmpty(oneDrive) &&
                root.StartsWith(oneDrive.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            {
                Add(report, "OneDrive", DiagnosticState.Fail,
                    "zapret установлен в папку OneDrive. Перенесите его в Program Files или C:\\zapret.");
            }
            else
            {
                Add(report, "OneDrive", DiagnosticState.Pass, "Путь вне OneDrive.");
            }
        }

        private static void AddRegistryChecks(DiagnosticReport report)
        {
            try
            {
                bool doh = false;
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters", false))
                {
                    if (key != null)
                    {
                        foreach (string name in key.GetSubKeyNames())
                        {
                            using (RegistryKey child = key.OpenSubKey(name, false))
                            {
                                if (child != null && ToInt(child.GetValue("DohFlags")) > 0)
                                {
                                    doh = true;
                                    break;
                                }
                            }
                        }
                    }
                }
                Add(report, "Secure DNS", doh ? DiagnosticState.Warning : DiagnosticState.Pass,
                    doh
                        ? "Включён DoH. Настройте его в браузере или Windows, если обход не работает."
                        : "DoH не обнаружен.");
            }
            catch (Exception ex)
            {
                Add(report, "Secure DNS", DiagnosticState.Warning, ex.Message);
            }
        }

        private static void AddHostsCheck(DiagnosticReport report)
        {
            try
            {
                string path = HostsManager.HostsPath;
                if (!File.Exists(path))
                {
                    Add(report, "hosts", DiagnosticState.Warning, "Файл hosts не найден.");
                    return;
                }
                bool found = false;
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (line.IndexOf("youtube.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        line.IndexOf("youtu.be", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        found = true;
                        break;
                    }
                }
                Add(report, "hosts", found ? DiagnosticState.Warning : DiagnosticState.Pass,
                    found
                        ? "В hosts есть записи youtube.com/youtu.be. Это может мешать YouTube."
                        : "Записей YouTube в hosts нет.");
            }
            catch (Exception ex)
            {
                Add(report, "hosts", DiagnosticState.Warning, ex.Message);
            }
        }

        private static void AddWinDivertConflict(DiagnosticReport report, bool winwsRunning)
        {
            bool exists = NativeService.Exists("WinDivert") || NativeService.Exists("WinDivert14");
            if (exists && !winwsRunning)
            {
                Add(report, "WinDivert", DiagnosticState.Warning,
                    "Служба WinDivert активна, а winws.exe не запущен. Удалите WinDivert или запустите zapret.");
            }
            else
            {
                Add(report, "WinDivert", DiagnosticState.Pass,
                    exists ? "Служба WinDivert активна вместе с winws.exe." : "Конфликтов нет.");
            }
        }

        private static void AddIfaceCheck(DiagnosticReport report)
        {
            try
            {
                IfaceStatus iface = IfaceStatusProvider.GetStatus();
                if (!iface.ServiceFound)
                {
                    Add(report, "Интерфейс", DiagnosticState.Warning, "Служба zapret не установлена.");
                }
                else if (!iface.ActiveFound)
                {
                    Add(report, "Интерфейс", DiagnosticState.Warning,
                        "Не удалось определить активный физический интерфейс.");
                }
                else if (iface.NeedsChange)
                {
                    Add(report, "Интерфейс", DiagnosticState.Warning,
                        "В конфиге " + (iface.HasConfig ? iface.ConfigDisplay : "не задан") +
                        ", активен " + iface.ActiveDisplay + ". Нажмите «Обновить».");
                }
                else
                {
                    Add(report, "Интерфейс", DiagnosticState.Pass,
                        iface.ActiveDisplay + " совпадает с конфигом.");
                }
            }
            catch (Exception ex)
            {
                Add(report, "Интерфейс", DiagnosticState.Warning, ex.Message);
            }
        }

        private static void AddTaskCheck(DiagnosticReport report)
        {
            try
            {
                bool registered = SyncTask.IsRegistered();
                Add(report, "Задача синхронизации",
                    registered ? DiagnosticState.Pass : DiagnosticState.Warning,
                    registered
                        ? SyncTask.TaskName + " зарегистрирована."
                        : SyncTask.TaskName + " не зарегистрирована.");
            }
            catch (Exception ex)
            {
                Add(report, "Задача синхронизации", DiagnosticState.Warning, ex.Message);
            }
        }

        private static int ToInt(object value)
        {
            if (value == null)
            {
                return 0;
            }
            try
            {
                return Convert.ToInt32(value);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        // Mirrors :tcp_enable from service.bat.
        public static bool EnableTimestamps()
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "netsh.exe");
                info.Arguments = "interface tcp set global timestamps=enabled";
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                using (Process process = Process.Start(info))
                {
                    process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    process.WaitForExit(10000);
                    return process.ExitCode == 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static string RunHidden(string fileName, string arguments, int timeoutMs)
        {            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = fileName;
                info.Arguments = arguments;
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                using (Process process = Process.Start(info))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit(timeoutMs);
                    return (output + Environment.NewLine + error).Trim();
                }
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
