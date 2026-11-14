using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ZapretControl
{
    public sealed class HostsCheckResult
    {
        public bool NeedsUpdate { get; private set; }
        public bool ManagedBlockFound { get; private set; }
        public string UpstreamText { get; private set; }
        public string Preview { get; private set; }
        public string Error { get; private set; }

        public HostsCheckResult(
            bool needsUpdate,
            bool managedBlockFound,
            string upstreamText,
            string preview,
            string error)
        {
            NeedsUpdate = needsUpdate;
            ManagedBlockFound = managedBlockFound;
            UpstreamText = upstreamText;
            Preview = preview;
            Error = error;
        }
    }

    public sealed class HostsApplyResult
    {
        public string BackupPath { get; private set; }
        public int Lines { get; private set; }

        public HostsApplyResult(string backupPath, int lines)
        {
            BackupPath = backupPath;
            Lines = lines;
        }
    }

    // Native replacement for "Update Hosts File": keeps the upstream content in a
    // marked managed block instead of asking the user to copy it manually.
    public static class HostsManager
    {
        public const string StartMarker = "# >>> Zapret Control (managed) >>>";
        public const string EndMarker = "# <<< Zapret Control (managed) <<<";

        private static readonly Regex HostnamePattern = new Regex(
            @"^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.Compiled);

        public static string HostsPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    @"drivers\etc\hosts");
            }
        }

        public static HostsCheckResult Check()
        {
            try
            {
                string upstream = DownloadHosts();
                if (string.IsNullOrEmpty(upstream))
                {
                    return new HostsCheckResult(
                        false, false, null, null, "Не удалось скачать hosts из репозитория.");
                }
                if (!IsValidHostsText(upstream))
                {
                    return new HostsCheckResult(
                        false, false, null, null,
                        "hosts из репозитория не похож на файл hosts.");
                }
                string hosts = ReadHosts();
                string block;
                bool managed = TryExtractBlock(hosts, out block);
                if (managed)
                {
                    bool same = NormalizeLines(block) == NormalizeLines(upstream);
                    return new HostsCheckResult(
                        !same, true, upstream, BuildPreview(upstream), null);
                }

                string[] upstreamLines = SplitLines(upstream);
                string first = upstreamLines.Length > 0 ? upstreamLines[0].Trim() : string.Empty;
                string last = upstreamLines.Length > 0
                    ? upstreamLines[upstreamLines.Length - 1].Trim()
                    : string.Empty;
                bool firstFound = first.Length > 0 &&
                    hosts.IndexOf(first, StringComparison.OrdinalIgnoreCase) >= 0;
                bool lastFound = last.Length > 0 &&
                    hosts.IndexOf(last, StringComparison.OrdinalIgnoreCase) >= 0;
                return new HostsCheckResult(
                    !(firstFound && lastFound), false, upstream, BuildPreview(upstream), null);
            }
            catch (Exception ex)
            {
                return new HostsCheckResult(
                    false, false, null, null, ex.Message);
            }
        }

        public static HostsApplyResult Apply(string upstreamText)
        {
            if (string.IsNullOrEmpty(upstreamText))
            {
                throw new InvalidOperationException("Нет данных hosts для записи.");
            }
            if (!IsValidHostsText(upstreamText))
            {
                throw new InvalidOperationException(
                    "Данные hosts не прошли проверку формата.");
            }
            string hosts = ReadHosts();
            string backup = null;
            if (File.Exists(HostsPath))
            {
                AppPaths.EnsureDirectory(AppPaths.BackupDirectory);
                backup = Path.Combine(
                    AppPaths.BackupDirectory,
                    "hosts.bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                File.Copy(HostsPath, backup, true);
            }

            string cleaned = RemoveManagedBlock(hosts);
            string block = StartMarker + Environment.NewLine +
                           NormalizeToCrlf(upstreamText).Trim() + Environment.NewLine +
                           EndMarker + Environment.NewLine;
            string result = cleaned.TrimEnd() + Environment.NewLine + Environment.NewLine + block;
            File.WriteAllText(HostsPath, result, new UTF8Encoding(false));
            FlushDns();

            int lines = 0;
            foreach (string line in SplitLines(upstreamText))
            {
                if (line.Trim().Length > 0 && !line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                {
                    lines++;
                }
            }
            return new HostsApplyResult(backup, lines);
        }

        // Accept only "IP hostname..." lines (IPv4 or IPv6, comments allowed).
        // The upstream file is remote data, so it must not be able to inject
        // arbitrary content into the system hosts file.
        public static bool IsValidHostsText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            int entries = 0;
            foreach (string raw in SplitLines(text))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }
                if (line.Length > 512 || !IsValidHostsLine(line))
                {
                    return false;
                }
                entries++;
                if (entries > 10000)
                {
                    return false;
                }
            }
            return entries > 0;
        }

        private static bool IsValidHostsLine(string line)
        {
            string[] parts = line.Split(
                new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return false;
            }
            IPAddress address;
            if (!IPAddress.TryParse(parts[0], out address))
            {
                return false;
            }
            int names = 0;
            for (int i = 1; i < parts.Length; i++)
            {
                if (parts[i].StartsWith("#", StringComparison.Ordinal))
                {
                    break;
                }
                if (!HostnamePattern.IsMatch(parts[i]))
                {
                    return false;
                }
                names++;
            }
            return names > 0;
        }

        private static string DownloadHosts()
        {
            string url = AppInfo.FlowsealHostsUrl + "?t=" +
                         DateTime.UtcNow.Ticks.ToString();
            return Downloader.GetString(url, 20000, 2097152);
        }

        private static string ReadHosts()
        {
            string path = HostsPath;
            if (!File.Exists(path))
            {
                return string.Empty;
            }
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

        private static bool TryExtractBlock(string hosts, out string block)
        {
            block = null;
            int start = hosts.IndexOf(StartMarker, StringComparison.Ordinal);
            if (start < 0)
            {
                return false;
            }
            int contentStart = start + StartMarker.Length;
            int end = hosts.IndexOf(EndMarker, contentStart, StringComparison.Ordinal);
            if (end < 0)
            {
                return false;
            }
            block = hosts.Substring(contentStart, end - contentStart).Trim();
            return true;
        }

        private static string RemoveManagedBlock(string hosts)
        {
            List<string> lines = new List<string>(SplitLines(hosts));
            StringBuilder builder = new StringBuilder();
            bool inBlock = false;
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed == StartMarker)
                {
                    inBlock = true;
                    continue;
                }
                if (trimmed == EndMarker)
                {
                    inBlock = false;
                    continue;
                }
                if (!inBlock)
                {
                    builder.Append(line).Append(Environment.NewLine);
                }
            }
            return builder.ToString();
        }

        private static string NormalizeLines(string text)
        {
            StringBuilder builder = new StringBuilder();
            foreach (string raw in SplitLines(text))
            {
                string line = raw.Trim();
                if (line.Length > 0)
                {
                    builder.Append(line).Append('\n');
                }
            }
            return builder.ToString();
        }

        private static string NormalizeToCrlf(string text)
        {
            string[] lines = SplitLines(text);
            StringBuilder builder = new StringBuilder();
            foreach (string line in lines)
            {
                builder.Append(line.TrimEnd()).Append("\r\n");
            }
            return builder.ToString();
        }

        private static string BuildPreview(string text)
        {
            string[] lines = SplitLines(text);
            StringBuilder builder = new StringBuilder();
            int count = 0;
            foreach (string line in lines)
            {
                if (line.Trim().Length == 0)
                {
                    continue;
                }
                builder.Append(line.TrimEnd()).Append(Environment.NewLine);
                count++;
                if (count >= 5)
                {
                    break;
                }
            }
            return builder.ToString().TrimEnd();
        }

        private static string[] SplitLines(string text)
        {
            return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }

        private static void FlushDns()
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "ipconfig.exe");
                info.Arguments = "/flushdns";
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                using (Process process = Process.Start(info))
                {
                    process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    process.WaitForExit(10000);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
