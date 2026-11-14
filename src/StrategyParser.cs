using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ZapretControl
{
    public sealed class GameFilterSettings
    {
        public const string ModeDisabled = "disabled";
        public const string ModeAll = "all";
        public const string ModeTcp = "tcp";
        public const string ModeUdp = "udp";
        public const string DefaultRange = "1024-65535";
        public const string DisabledPort = "12";

        public string Mode { get; set; }
        public string TcpRange { get; set; }
        public string UdpRange { get; set; }

        public GameFilterSettings()
        {
            Mode = ModeDisabled;
            TcpRange = DefaultRange;
            UdpRange = DefaultRange;
        }

        public string EffectiveTcp
        {
            get { return Mode == ModeTcp || Mode == ModeAll ? TcpRange : DisabledPort; }
        }

        public string EffectiveUdp
        {
            get { return Mode == ModeUdp || Mode == ModeAll ? UdpRange : DisabledPort; }
        }

        public string EffectiveAll
        {
            get { return Mode == ModeAll ? TcpRange : DisabledPort; }
        }

        public string Display
        {
            get
            {
                if (Mode == ModeAll)
                {
                    return "включён (TCP и UDP)";
                }
                if (Mode == ModeTcp)
                {
                    return "включён (TCP)";
                }
                if (Mode == ModeUdp)
                {
                    return "включён (UDP)";
                }
                return "выключен";
            }
        }

        public static string GetFlagPath(string payloadRoot)
        {
            return Path.Combine(payloadRoot ?? string.Empty, @"utils\game_filter.enabled");
        }

        public static GameFilterSettings Load(string payloadRoot)
        {
            GameFilterSettings settings = new GameFilterSettings();
            string path = GetFlagPath(payloadRoot);
            try
            {
                if (!File.Exists(path))
                {
                    return settings;
                }
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                string mode = ModeDisabled;
                string tcp = null;
                string udp = null;
                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    int separator = line.IndexOf('=');
                    string key = (separator < 0 ? line : line.Substring(0, separator)).Trim();
                    string value = separator < 0 ? string.Empty : line.Substring(separator + 1).Trim();
                    if (key.Equals("mode", StringComparison.OrdinalIgnoreCase))
                    {
                        mode = value.ToLowerInvariant();
                    }
                    else if (key.Equals("all", StringComparison.OrdinalIgnoreCase))
                    {
                        mode = ModeAll;
                    }
                    else if (key.Equals("tcp", StringComparison.OrdinalIgnoreCase))
                    {
                        if (value.Length == 0)
                        {
                            mode = ModeTcp;
                        }
                        else
                        {
                            tcp = value;
                        }
                    }
                    else if (key.Equals("udp", StringComparison.OrdinalIgnoreCase))
                    {
                        if (value.Length == 0)
                        {
                            mode = ModeUdp;
                        }
                        else
                        {
                            udp = value;
                        }
                    }
                }

                string validatedTcp = NormalizeRange(tcp);
                if (validatedTcp != null)
                {
                    settings.TcpRange = validatedTcp;
                }
                string validatedUdp = NormalizeRange(udp);
                if (validatedUdp != null)
                {
                    settings.UdpRange = validatedUdp;
                }

                if (mode == ModeAll || mode == ModeTcp || mode == ModeUdp)
                {
                    settings.Mode = mode;
                }
                else
                {
                    settings.Mode = ModeDisabled;
                }
                return settings;
            }
            catch (Exception)
            {
                return settings;
            }
        }

        public bool Save(string payloadRoot)
        {
            try
            {
                string path = GetFlagPath(payloadRoot);
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                StringBuilder text = new StringBuilder();
                text.Append("mode=").Append(Mode).Append(Environment.NewLine);
                text.Append("tcp=").Append(TcpRange).Append(Environment.NewLine);
                text.Append("udp=").Append(UdpRange).Append(Environment.NewLine);
                File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Mirrors :validate_game_filter_range from service.bat: comma-separated
        // ports or start-end ranges, 1..65535, no leading zero, start <= end.
        public static string NormalizeRange(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }
            string compact = value.Replace(" ", string.Empty).Replace("\t", string.Empty);
            if (compact.Length == 0)
            {
                return null;
            }
            string[] items = compact.Split(',');
            for (int i = 0; i < items.Length; i++)
            {
                string item = items[i];
                if (!IsValidRangeItem(item))
                {
                    return null;
                }
            }
            return compact;
        }

        private static bool IsValidRangeItem(string item)
        {
            if (string.IsNullOrEmpty(item))
            {
                return false;
            }
            int dash = item.IndexOf('-');
            string startText = dash < 0 ? item : item.Substring(0, dash);
            string endText = dash < 0 ? item : item.Substring(dash + 1);
            if (dash >= 0 && endText.IndexOf('-') >= 0)
            {
                return false;
            }
            int start;
            int end;
            if (!TryPort(startText, out start) || !TryPort(endText, out end))
            {
                return false;
            }
            return start <= end;
        }

        private static bool TryPort(string text, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text) || text[0] == '0')
            {
                return false;
            }
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] < '0' || text[i] > '9')
                {
                    return false;
                }
            }
            if (!int.TryParse(text, out value))
            {
                return false;
            }
            return value >= 1 && value <= 65535;
        }
    }

    public sealed class StrategyInfo
    {
        public string Name { get; private set; }
        public string Path { get; private set; }

        public StrategyInfo(string name, string path)
        {
            Name = name;
            Path = path;
        }

        public override string ToString()
        {
            return Name;
        }
    }

    public sealed class StrategyCommand
    {
        public string StrategyName { get; private set; }
        public string StrategyPath { get; private set; }
        public string WinwsPath { get; private set; }
        public string Arguments { get; private set; }
        public string CommandLine { get; private set; }

        public StrategyCommand(
            string strategyName,
            string strategyPath,
            string winwsPath,
            string arguments,
            string commandLine)
        {
            StrategyName = strategyName;
            StrategyPath = strategyPath;
            WinwsPath = winwsPath;
            Arguments = arguments;
            CommandLine = commandLine;
        }
    }

    public static class StrategyParser
    {
        public static List<StrategyInfo> ListStrategies(string payloadRoot)
        {
            List<StrategyInfo> result = new List<StrategyInfo>();
            if (string.IsNullOrEmpty(payloadRoot) || !Directory.Exists(payloadRoot))
            {
                return result;
            }
            if (AppPaths.IsReparsePoint(payloadRoot))
            {
                return result;
            }
            try
            {
                string[] files = Directory.GetFiles(payloadRoot, "*.bat", SearchOption.TopDirectoryOnly);
                List<string> names = new List<string>();
                foreach (string file in files)
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(file);
                    if (name.StartsWith("service", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (AppPaths.IsReparsePoint(file))
                    {
                        continue;
                    }
                    names.Add(name);
                }
                names.Sort(NaturalCompare);
                foreach (string name in names)
                {
                    result.Add(new StrategyInfo(
                        name, System.IO.Path.Combine(payloadRoot, name + ".bat")));
                }
            }
            catch (Exception)
            {
            }
            return result;
        }

        public static StrategyCommand Build(
            string payloadRoot,
            string strategyPath,
            GameFilterSettings game,
            int ifaceIndex)
        {
            if (string.IsNullOrEmpty(payloadRoot) || !Directory.Exists(payloadRoot))
            {
                throw new InvalidOperationException("Папка zapret не найдена: " + payloadRoot);
            }
            if (AppPaths.IsReparsePoint(payloadRoot))
            {
                throw new InvalidOperationException(
                    "Папка zapret является reparse point: " + payloadRoot);
            }
            if (string.IsNullOrEmpty(strategyPath) || !File.Exists(strategyPath))
            {
                throw new InvalidOperationException("Стратегия не найдена: " + strategyPath);
            }
            string fullStrategy = System.IO.Path.GetFullPath(strategyPath);
            if (!AppPaths.IsUnder(fullStrategy, payloadRoot))
            {
                throw new InvalidOperationException(
                    "Стратегия находится вне папки zapret: " + strategyPath);
            }
            if (AppPaths.IsReparsePoint(fullStrategy))
            {
                throw new InvalidOperationException(
                    "Файл стратегии является reparse point: " + strategyPath);
            }

            string winws = System.IO.Path.Combine(payloadRoot, @"bin\winws.exe");
            if (!File.Exists(winws))
            {
                throw new InvalidOperationException("winws.exe не найден: " + winws);
            }
            if (AppPaths.IsReparsePoint(winws))
            {
                throw new InvalidOperationException("winws.exe является reparse point: " + winws);
            }

            string text = ReadText(fullStrategy);
            string command = FindCommand(text);
            if (command == null)
            {
                throw new InvalidOperationException(
                    "В стратегии не найдена команда winws.exe: " +
                    System.IO.Path.GetFileName(fullStrategy));
            }

            string expanded = ExpandVariables(command, payloadRoot, game ?? new GameFilterSettings());
            expanded = expanded.Trim();
            if (expanded.Length == 0)
            {
                throw new InvalidOperationException(
                    "Стратегия содержит пустую команду winws.exe: " +
                    System.IO.Path.GetFileName(fullStrategy));
            }

            expanded = ApplyIface(expanded, ifaceIndex);
            string commandLine = Quote(winws) + " " + expanded;
            string name = System.IO.Path.GetFileNameWithoutExtension(fullStrategy);
            return new StrategyCommand(name, fullStrategy, winws, expanded, commandLine);
        }

        internal static string ApplyIface(string arguments, int ifaceIndex)
        {
            if (ifaceIndex < 0)
            {
                return arguments;
            }
            string value = "--wf-iface=" + ifaceIndex;
            if (Regex.IsMatch(arguments, @"--wf-iface=\d+", RegexOptions.IgnoreCase))
            {
                return Regex.Replace(
                    arguments, @"--wf-iface=\d+", value, RegexOptions.IgnoreCase);
            }
            if (arguments.StartsWith("--", StringComparison.Ordinal))
            {
                return value + " " + arguments;
            }
            return value + " " + arguments;
        }

        internal static string Quote(string value)
        {
            return "\"" + value + "\"";
        }

        internal static string ReadText(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 &&
                bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return new UTF8Encoding(false, true).GetString(bytes, 3, bytes.Length - 3);
            }
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.Default.GetString(bytes);
            }
        }

        internal static string FindCommand(string text)
        {
            string[] lines = JoinContinuations(text);
            foreach (string raw in lines)
            {
                string line = raw.TrimStart();
                if (line.Length == 0)
                {
                    continue;
                }
                if (line.StartsWith("::", StringComparison.Ordinal) ||
                    line.StartsWith("rem ", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("echo", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("@echo", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                int index = line.IndexOf("winws.exe", StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    continue;
                }
                int end = index + "winws.exe".Length;
                if (end < line.Length && line[end] == '"')
                {
                    end++;
                }
                string command = line.Substring(end).Trim();
                if (command.EndsWith("^", StringComparison.Ordinal))
                {
                    command = command.Substring(0, command.Length - 1).TrimEnd();
                }
                if (command.Length > 0)
                {
                    return command;
                }
            }
            return null;
        }

        internal static string[] JoinContinuations(string text)
        {
            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] physical = normalized.Split('\n');
            List<string> logical = new List<string>();
            StringBuilder current = new StringBuilder();
            for (int i = 0; i < physical.Length; i++)
            {
                string line = physical[i];
                string trimmed = line.TrimEnd();
                if (trimmed.EndsWith("^", StringComparison.Ordinal))
                {
                    string part = trimmed.Substring(0, trimmed.Length - 1);
                    current.Append(part);
                    if (i + 1 < physical.Length &&
                        physical[i + 1].Length > 0 &&
                        !char.IsWhiteSpace(physical[i + 1][0]) &&
                        part.Length > 0 &&
                        !char.IsWhiteSpace(part[part.Length - 1]))
                    {
                        current.Append(' ');
                    }
                    continue;
                }
                current.Append(line);
                logical.Add(current.ToString());
                current.Length = 0;
            }
            if (current.Length > 0)
            {
                logical.Add(current.ToString());
            }
            return logical.ToArray();
        }

        internal static string ExpandVariables(
            string arguments, string payloadRoot, GameFilterSettings game)
        {
            string root = payloadRoot.TrimEnd('\\') + "\\";
            string text = arguments;
            text = ReplaceIgnoreCase(text, "%~dp0", root);
            text = ReplaceIgnoreCase(text, "%BIN%", root + "bin\\");
            text = ReplaceIgnoreCase(text, "%LISTS%", root + "lists\\");
            text = ReplaceIgnoreCase(text, "%GameFilterTCP%", game.EffectiveTcp);
            text = ReplaceIgnoreCase(text, "%GameFilterUDP%", game.EffectiveUdp);
            text = ReplaceIgnoreCase(text, "%GameFilter%", game.EffectiveAll);
            text = Environment.ExpandEnvironmentVariables(text);

            Match leftover = Regex.Match(text, @"%[A-Za-z_][A-Za-z0-9_]*%");
            if (leftover.Success)
            {
                throw new InvalidOperationException(
                    "Стратегия использует неизвестную переменную " + leftover.Value + ".");
            }
            return NormalizeSpaces(text);
        }

        internal static string ReplaceIgnoreCase(string text, string from, string to)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(from))
            {
                return text;
            }
            int index = text.IndexOf(from, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                text = text.Substring(0, index) + to + text.Substring(index + from.Length);
                index = text.IndexOf(from, index + to.Length, StringComparison.OrdinalIgnoreCase);
            }
            return text;
        }

        // Collapses runs of whitespace outside double quotes only.
        internal static string NormalizeSpaces(string text)
        {
            StringBuilder builder = new StringBuilder(text.Length);
            bool quoted = false;
            bool pendingSpace = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"')
                {
                    if (pendingSpace && builder.Length > 0)
                    {
                        builder.Append(' ');
                    }
                    pendingSpace = false;
                    quoted = !quoted;
                    builder.Append(c);
                    continue;
                }
                if (!quoted && (c == ' ' || c == '\t'))
                {
                    pendingSpace = true;
                    continue;
                }
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }
                pendingSpace = false;
                builder.Append(c);
            }
            return builder.ToString().Trim();
        }

        internal static int NaturalCompare(string left, string right)
        {
            int i = 0;
            int j = 0;
            while (i < left.Length && j < right.Length)
            {
                if (char.IsDigit(left[i]) && char.IsDigit(right[j]))
                {
                    int startLeft = i;
                    int startRight = j;
                    while (i < left.Length && char.IsDigit(left[i]))
                    {
                        i++;
                    }
                    while (j < right.Length && char.IsDigit(right[j]))
                    {
                        j++;
                    }
                    string leftNumber = left.Substring(startLeft, i - startLeft).TrimStart('0');
                    string rightNumber = right.Substring(startRight, j - startRight).TrimStart('0');
                    if (leftNumber.Length != rightNumber.Length)
                    {
                        return leftNumber.Length < rightNumber.Length ? -1 : 1;
                    }
                    int numberCompare = string.CompareOrdinal(leftNumber, rightNumber);
                    if (numberCompare != 0)
                    {
                        return numberCompare;
                    }
                    continue;
                }
                int charCompare = char.ToUpperInvariant(left[i]).CompareTo(
                    char.ToUpperInvariant(right[j]));
                if (charCompare != 0)
                {
                    return charCompare;
                }
                i++;
                j++;
            }
            return left.Length.CompareTo(right.Length);
        }
    }
}
