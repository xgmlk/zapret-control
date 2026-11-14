using System;
using System.Collections.Generic;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ZapretControl
{
    // Immutable snapshot: which physical interface the OS uses right now vs which
    // ifIndex is written into the zapret service config (--wf-iface=N).
    public sealed class IfaceStatus
    {
        public bool ActiveFound { get; private set; }
        public string ActiveDisplay { get; private set; }
        public int ActiveIndex { get; private set; }
        public bool ActiveWireless { get; private set; }

        public bool ServiceFound { get; private set; }
        public bool HasConfig { get; private set; }
        public string ConfigDisplay { get; private set; }
        public int ConfigIndex { get; private set; }
        public bool ConfigWireless { get; private set; }

        public bool NeedsChange { get; private set; }

        public IfaceStatus(
            bool activeFound,
            string activeDisplay,
            int activeIndex,
            bool activeWireless,
            bool serviceFound,
            bool hasConfig,
            string configDisplay,
            int configIndex,
            bool configWireless,
            bool needsChange)
        {
            ActiveFound = activeFound;
            ActiveDisplay = activeDisplay;
            ActiveIndex = activeIndex;
            ActiveWireless = activeWireless;
            ServiceFound = serviceFound;
            HasConfig = hasConfig;
            ConfigDisplay = configDisplay;
            ConfigIndex = configIndex;
            ConfigWireless = configWireless;
            NeedsChange = needsChange;
        }
    }

    public sealed class IfaceSyncResult
    {
        public int ActiveIndex { get; private set; }
        public int OldConfigIndex { get; private set; }
        public bool ImagePathChanged { get; private set; }
        public bool StrategyChanged { get; private set; }
        public string StrategyPath { get; private set; }
        public string StrategyError { get; private set; }
        public bool ServiceRestarted { get; private set; }
        public bool NeedsChange { get; private set; }
        public string LogPath { get; private set; }

        public bool Changed
        {
            get { return ImagePathChanged || StrategyChanged; }
        }

        public IfaceSyncResult(
            int activeIndex,
            int oldConfigIndex,
            bool imagePathChanged,
            bool strategyChanged,
            string strategyPath,
            string strategyError,
            bool serviceRestarted,
            bool needsChange,
            string logPath)
        {
            ActiveIndex = activeIndex;
            OldConfigIndex = oldConfigIndex;
            ImagePathChanged = imagePathChanged;
            StrategyChanged = strategyChanged;
            StrategyPath = strategyPath;
            StrategyError = strategyError;
            ServiceRestarted = serviceRestarted;
            NeedsChange = needsChange;
            LogPath = logPath;
        }
    }

    public static class IfaceStatusProvider
    {
        public const string ServiceName = "zapret";
        internal const string StrategyValueName = "zapret-discord-youtube";

        private const string ServiceKeyPath = @"SYSTEM\CurrentControlSet\Services\" + ServiceName;
        private const int ErrorInsufficientBuffer = 122;

        // Keep this list identical to $exclude in tests\run-tests.ps1:
        // the GUI, the CLI and the test must choose the same interface.
        private static readonly string[] ExcludedNameParts = new string[]
        {
            "redflag-tun-v1", "openvpn", "loopback", "teredo", "isatap", "6to4",
            "wan miniport", "bluetooth", "wi-fi direct"
        };

        public static IfaceStatus GetStatus()
        {
            NetworkInterface active = FindActivePhysicalInterface();
            bool activeFound = active != null;
            int activeIndex = activeFound ? GetIndex(active) : -1;
            bool activeWireless = activeFound && IsWireless(active);
            string activeDisplay = activeFound ? DescribeActive(active) : null;

            string imagePath = ReadImagePath();
            bool serviceFound = !string.IsNullOrEmpty(imagePath);

            int configIndex = -1;
            bool hasConfig = serviceFound && TryParseWfIface(imagePath, out configIndex);
            NetworkInterface configAdapter = hasConfig ? FindAdapterByIndex(configIndex) : null;
            string configDisplay = hasConfig ? DescribeConfig(configAdapter, configIndex) : null;
            bool configWireless = hasConfig && IsWireless(configAdapter);

            bool needsChange = activeFound && (!hasConfig || configIndex != activeIndex);

            return new IfaceStatus(
                activeFound,
                activeDisplay,
                activeIndex,
                activeWireless,
                serviceFound,
                hasConfig,
                configDisplay,
                configIndex,
                configWireless,
                needsChange);
        }

        internal static bool TryParseWfIface(string imagePath, out int index)
        {
            index = -1;
            if (string.IsNullOrEmpty(imagePath))
            {
                return false;
            }
            Match match = Regex.Match(
                imagePath, @"--wf-iface=(\d+)(?=\s|$)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }
            return int.TryParse(match.Groups[1].Value, out index);
        }

        // Writes the active interface into the service ImagePath and the strategy
        // .bat, and restarts zapret only when ImagePath changed. Requires admin.
        public static IfaceSyncResult SyncInterface()
        {
            return SyncInterface(0);
        }

        // waitSeconds lets the at-logon task wait for the network to come up.
        public static IfaceSyncResult SyncInterface(int waitSeconds)
        {
            IfaceStatus before = GetStatus();
            if (!before.ServiceFound)
            {
                throw new InvalidOperationException("Служба zapret не установлена.");
            }
            DateTime deadline = DateTime.UtcNow.AddSeconds(Math.Max(0, waitSeconds));
            while (!before.ActiveFound && DateTime.UtcNow < deadline)
            {
                System.Threading.Thread.Sleep(5000);
                before = GetStatus();
            }
            if (!before.ActiveFound)
            {
                throw new InvalidOperationException(
                    "Не удалось определить активный физический интерфейс.");
            }

            string imagePath = ReadImagePath();
            if (string.IsNullOrEmpty(imagePath))
            {
                throw new InvalidOperationException(
                    "Не удалось прочитать ImagePath службы zapret.");
            }

            try
            {
                string newImagePath = BuildImagePath(imagePath, before.ActiveIndex);
                bool imageChanged = !string.Equals(imagePath, newImagePath, StringComparison.Ordinal);
                if (imageChanged)
                {
                    WriteImagePath(newImagePath);
                }

                // A strategy .bat only affects manual starts. A failure there
                // must not hide the already updated service ImagePath.
                string strategyPath = FindStrategyPath(imagePath);
                bool strategyChanged = false;
                string strategyError = null;
                if (!string.IsNullOrEmpty(strategyPath))
                {
                    try
                    {
                        strategyChanged = UpdateStrategyFile(strategyPath, before.ActiveIndex);
                    }
                    catch (Exception ex)
                    {
                        strategyError = ex.GetType().Name + ": " + ex.Message;
                        TryAppendLog("STRATEGY ERROR " + strategyError);
                    }
                }

                // The service reads ImagePath; a strategy .bat is only used for
                // manual starts, so it does not need a service restart.
                bool restarted = false;
                if (imageChanged)
                {
                    restarted = ZapretService.RestartIfRunning();
                }

                IfaceStatus after = GetStatus();
                string summary = string.Format(
                    "active={0} old_config={1} image_changed={2} strategy={3} strategy_changed={4} strategy_error={5} restarted={6} needs_change={7}",
                    before.ActiveIndex,
                    before.HasConfig ? before.ConfigIndex.ToString() : "-",
                    imageChanged ? "yes" : "no",
                    string.IsNullOrEmpty(strategyPath) ? "(not found)" : strategyPath,
                    strategyChanged ? "yes" : "no",
                    strategyError == null ? "(none)" : strategyError,
                    restarted ? "yes" : "no",
                    after.NeedsChange ? "yes" : "no");
                string logPath = TryAppendLog(summary);

                return new IfaceSyncResult(
                    before.ActiveIndex,
                    before.HasConfig ? before.ConfigIndex : -1,
                    imageChanged,
                    strategyChanged,
                    strategyPath,
                    strategyError,
                    restarted,
                    after.NeedsChange,
                    logPath);
            }
            catch (Exception ex)
            {
                TryAppendLog("ERROR " + ex.GetType().Name + ": " + ex.Message);
                throw;
            }
        }

        internal static string BuildImagePath(string imagePath, int index)
        {
            if (string.IsNullOrEmpty(imagePath))
            {
                throw new ArgumentException("ImagePath is empty.", "imagePath");
            }

            if (Regex.IsMatch(imagePath, @"--wf-iface=\d+", RegexOptions.IgnoreCase))
            {
                return Regex.Replace(
                    imagePath,
                    @"--wf-iface=\d+",
                    "--wf-iface=" + index,
                    RegexOptions.IgnoreCase);
            }
            return InsertAfterExecutable(imagePath, "--wf-iface=" + index);
        }

        internal static string BuildStrategyText(string text, int index)
        {
            string updated;
            if (TryBuildStrategyText(text, index, out updated))
            {
                return updated;
            }
            return text;
        }

        internal static bool TryBuildStrategyText(string text, int index, out string updated)
        {
            updated = text;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            Match match = Regex.Match(text, @"--wf-iface=\d+", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                updated = Regex.Replace(
                    text,
                    @"--wf-iface=\d+",
                    "--wf-iface=" + index,
                    RegexOptions.IgnoreCase);
                return true;
            }

            int offset = 0;
            while (offset < text.Length)
            {
                int lineEnd = text.IndexOf('\n', offset);
                if (lineEnd < 0)
                {
                    lineEnd = text.Length;
                }
                string line = text.Substring(offset, lineEnd - offset);
                string trimmed = line.TrimStart();
                bool comment = trimmed.StartsWith("::", StringComparison.Ordinal) ||
                               trimmed.StartsWith("rem ", StringComparison.OrdinalIgnoreCase) ||
                               trimmed.StartsWith("echo", StringComparison.OrdinalIgnoreCase);
                if (!comment)
                {
                    int local = line.IndexOf("winws.exe", StringComparison.OrdinalIgnoreCase);
                    if (local >= 0)
                    {
                        int end = offset + local + "winws.exe".Length;
                        if (end < text.Length && text[end] == '"')
                        {
                            end++;
                        }
                        updated = text.Substring(0, end) +
                                  " --wf-iface=" + index +
                                  text.Substring(end);
                        return true;
                    }
                }
                if (lineEnd >= text.Length)
                {
                    break;
                }
                offset = lineEnd + 1;
            }
            return false;
        }

        private static string InsertAfterExecutable(string commandLine, string argument)
        {
            string value = commandLine;
            int start = 0;
            while (start < value.Length && char.IsWhiteSpace(value[start]))
            {
                start++;
            }
            if (start >= value.Length)
            {
                throw new InvalidOperationException("Некорректный ImagePath службы zapret.");
            }

            int end;
            if (value[start] == '"')
            {
                int closing = value.IndexOf('"', start + 1);
                if (closing < 0)
                {
                    throw new InvalidOperationException("Некорректный ImagePath службы zapret.");
                }
                end = closing + 1;
            }
            else
            {
                int space = value.IndexOfAny(new char[] { ' ', '\t' }, start);
                end = space < 0 ? value.Length : space;
            }

            return value.Substring(0, end) + " " + argument + value.Substring(end);
        }

        private static string ReadImagePath()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(ServiceKeyPath, false))
                {
                    if (key == null)
                    {
                        return null;
                    }
                    return key.GetValue("ImagePath") as string;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string FindStrategyPath(string imagePath)
        {
            try
            {
                string strategy = ReadStrategyName();
                if (string.IsNullOrEmpty(strategy))
                {
                    return null;
                }
                strategy = strategy.Trim();
                if (strategy.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
                {
                    strategy = strategy.Substring(0, strategy.Length - 4);
                }
                if (strategy.Length == 0 || strategy == "." || strategy == ".." ||
                    strategy.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    return null;
                }

                string root = GetZapretRoot(imagePath);
                if (string.IsNullOrEmpty(root))
                {
                    return null;
                }

                string path = Path.GetFullPath(Path.Combine(root, strategy + ".bat"));
                if (!IsUnderRoot(root, path) ||
                    !File.Exists(path) ||
                    IsReparsePoint(path) ||
                    IsReparsePoint(root))
                {
                    return null;
                }
                return path;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string GetZapretRoot(string imagePath)
        {
            string executable = NormalizeExecutablePath(imagePath);
            if (string.IsNullOrEmpty(executable))
            {
                return null;
            }
            string binDirectory = Path.GetDirectoryName(executable);
            if (string.IsNullOrEmpty(binDirectory))
            {
                return null;
            }
            return Path.GetDirectoryName(binDirectory);
        }

        private static bool IsUnderRoot(string root, string path)
        {
            string prefix = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            return Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadStrategyName()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(ServiceKeyPath, false))
                {
                    return key == null ? null : key.GetValue(StrategyValueName) as string;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool UpdateStrategyFile(string path, int index)
        {
            byte[] bytes = File.ReadAllBytes(path);
            bool hasBom = bytes.Length >= 3 &&
                          bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            Encoding encoding;
            string text;
            if (hasBom)
            {
                encoding = new UTF8Encoding(true);
                text = new UTF8Encoding(false, true).GetString(bytes, 3, bytes.Length - 3);
            }
            else
            {
                try
                {
                    text = new UTF8Encoding(false, true).GetString(bytes);
                    encoding = new UTF8Encoding(false);
                }
                catch (DecoderFallbackException)
                {
                    text = Encoding.Default.GetString(bytes);
                    encoding = Encoding.Default;
                }
            }

            string updated;
            if (!TryBuildStrategyText(text, index, out updated) ||
                string.Equals(text, updated, StringComparison.Ordinal))
            {
                return false;
            }

            string backup = CreateBackupPath(path);
            File.Copy(path, backup, false);
            File.WriteAllText(path, updated, encoding);
            return true;
        }

        private static string CreateBackupPath(string path)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string candidate = path + ".bak-iface-" + stamp;
            int suffix = 1;
            while (File.Exists(candidate))
            {
                candidate = path + ".bak-iface-" + stamp + "-" + suffix;
                suffix++;
            }
            return candidate;
        }

        private static void WriteImagePath(string imagePath)
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(ServiceKeyPath, true))
            {
                if (key == null)
                {
                    throw new InvalidOperationException(
                        "Не удалось открыть ключ службы zapret для записи.");
                }
                key.SetValue("ImagePath", imagePath, RegistryValueKind.String);
            }
        }

        private static bool IsReparsePoint(string path)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                return (attributes & FileAttributes.ReparsePoint) != 0;
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static string TryAppendLog(string message)
        {
            string directory = AppPaths.LogDirectory;
            string path = AppPaths.SyncLogPath;
            try
            {
                if (Directory.Exists(directory) && IsReparsePoint(directory))
                {
                    return null;
                }
                if (File.Exists(path) && IsReparsePoint(path))
                {
                    return null;
                }
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                              " " + message + Environment.NewLine;
                File.AppendAllText(path, line, new UTF8Encoding(true));
                return path;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string NormalizeExecutablePath(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath))
            {
                return null;
            }

            string value = imagePath.Trim();
            if (value.StartsWith("\"", StringComparison.Ordinal))
            {
                int closing = value.IndexOf('"', 1);
                if (closing <= 1)
                {
                    return null;
                }
                value = value.Substring(1, closing - 1);
            }
            else
            {
                int space = value.IndexOf(' ');
                if (space > 0)
                {
                    value = value.Substring(0, space);
                }
            }

            if (value.StartsWith(@"\??\", StringComparison.Ordinal))
            {
                value = value.Substring(4);
            }
            if (value.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            {
                value = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    value.Substring(@"\SystemRoot\".Length));
            }
            if (!Path.IsPathRooted(value))
            {
                value = Path.Combine(Environment.SystemDirectory, value);
            }

            return Path.GetFullPath(value);
        }

        // Walks the 0.0.0.0/0 routes in effective-metric order and returns the
        // first physical adapter, using the same rule as SyncInterface() and
        // tests\run-tests.ps1.
        private static NetworkInterface FindActivePhysicalInterface()
        {
            foreach (int index in FindDefaultRouteIndexes())
            {
                NetworkInterface candidate = FindAdapterByIndex(index);
                if (candidate == null || candidate.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }
                if (IsExcluded(candidate))
                {
                    continue;
                }
                return candidate;
            }
            return null;
        }

        private static NetworkInterface FindAdapterByIndex(int index)
        {
            if (index < 0)
            {
                return null;
            }
            foreach (NetworkInterface candidate in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (GetIndex(candidate) == index)
                {
                    return candidate;
                }
            }
            return null;
        }

        private static bool IsExcluded(NetworkInterface candidate)
        {
            string text = (candidate.Name + " " + candidate.Description).ToLowerInvariant();
            foreach (string part in ExcludedNameParts)
            {
                if (text.IndexOf(part, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static int GetIndex(NetworkInterface candidate)
        {
            try
            {
                IPv4InterfaceProperties ipv4 = candidate.GetIPProperties().GetIPv4Properties();
                return ipv4 == null ? -1 : ipv4.Index;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        private static bool IsWireless(NetworkInterface candidate)
        {
            return candidate != null &&
                   candidate.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
        }

        private static string DescribeActive(NetworkInterface candidate)
        {
            return IsWireless(candidate) ? "Wi-Fi (беспроводная)" : "Ethernet (кабель)";
        }

        private static string DescribeConfig(NetworkInterface candidate, int index)
        {
            if (candidate == null)
            {
                return "индекс " + index + " (адаптер не найден)";
            }
            return (IsWireless(candidate) ? "Wi-Fi" : "Ethernet") + " (" + index + ")";
        }

        private static List<int> FindDefaultRouteIndexes()
        {
            List<int> indexes = new List<int>();
            try
            {
                int size = 0;
                uint result = GetIpForwardTable(IntPtr.Zero, ref size, false);
                if (size <= 0 || (result != 0 && result != ErrorInsufficientBuffer))
                {
                    return indexes;
                }

                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    result = GetIpForwardTable(buffer, ref size, false);
                    if (result != 0)
                    {
                        return indexes;
                    }

                    int count = Marshal.ReadInt32(buffer);
                    if (count <= 0 || count > 100000)
                    {
                        return indexes;
                    }

                    int rowSize = Marshal.SizeOf(typeof(MibIpForwardRow));
                    IntPtr row = IntPtr.Add(buffer, 4);
                    List<MibIpForwardRow> routes = new List<MibIpForwardRow>();

                    for (int i = 0; i < count; i++)
                    {
                        MibIpForwardRow entry =
                            (MibIpForwardRow)Marshal.PtrToStructure(row, typeof(MibIpForwardRow));
                        if (entry.Destination == 0 && entry.Mask == 0 && entry.NextHop != 0)
                        {
                            routes.Add(entry);
                        }
                        row = IntPtr.Add(row, rowSize);
                    }

                    routes.Sort(delegate(MibIpForwardRow left, MibIpForwardRow right)
                    {
                        return left.Metric1.CompareTo(right.Metric1);
                    });

                    foreach (MibIpForwardRow entry in routes)
                    {
                        int index = (int)entry.InterfaceIndex;
                        if (!indexes.Contains(index))
                        {
                            indexes.Add(index);
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch (Exception)
            {
                indexes.Clear();
            }
            return indexes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MibIpForwardRow
        {
            public uint Destination;
            public uint Mask;
            public uint Policy;
            public uint NextHop;
            public uint InterfaceIndex;
            public uint Type;
            public uint Protocol;
            public uint Age;
            public uint NextHopAs;
            public uint Metric1;
            public uint Metric2;
            public uint Metric3;
            public uint Metric4;
            public uint Metric5;
        }

        [DllImport("iphlpapi.dll")]
        private static extern uint GetIpForwardTable(IntPtr table, ref int size, bool order);
    }
}
