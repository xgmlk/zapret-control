using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace ZapretControl
{
    // On-disk shape of one advisor run. Plain get/set properties are required
    // by JavaScriptSerializer; the report itself keeps private setters.
    public sealed class StrategyReportFile
    {
        public string CheckedAtUtc { get; set; }
        public string RecommendedName { get; set; }
        public string BestName { get; set; }
        public string Error { get; set; }
        public bool ServiceWasRunning { get; set; }
        public bool ServiceRestored { get; set; }
        public List<StrategyProbeFile> Results { get; set; }

        public StrategyReportFile()
        {
            CheckedAtUtc = string.Empty;
            RecommendedName = string.Empty;
            BestName = string.Empty;
            Error = string.Empty;
            Results = new List<StrategyProbeFile>();
        }
    }

    public sealed class StrategyProbeFile
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public bool DiscordOk { get; set; }
        public bool YouTubePageOk { get; set; }
        public bool YouTubePlayerOk { get; set; }
        public bool YouTubeVideoOk { get; set; }
        public bool GoogleOk { get; set; }
        public int VideoAttempts { get; set; }
        public int VideoSuccesses { get; set; }
        public long ElapsedMs { get; set; }
        public string Detail { get; set; }

        public StrategyProbeFile()
        {
            Name = string.Empty;
            Path = string.Empty;
            Detail = string.Empty;
        }
    }

    // Keeps the last advisor report between GUI runs. The home tab reopens it
    // read-only; saving happens after every real run, including the installer
    // auto-pick and the first-run prompt.
    public static class StrategyReportStore
    {
        public const string FileName = "strategy-report.json";
        private const int MaxResults = 500;
        private static readonly object SecuritySync = new object();
        private static bool _securityPrepared;

        public static string DefaultPath
        {
            get { return Path.Combine(AppPaths.DataDirectory, FileName); }
        }

        public static bool Save(StrategyAdviceReport report)
        {
            if (report == null || report.Results.Count == 0)
            {
                return false;
            }
            lock (SecuritySync)
            {
                if (!_securityPrepared)
                {
                    try
                    {
                        AppPaths.EnsureDirectory(AppPaths.DataDirectory);
                        if (AppPaths.IsReparsePoint(AppPaths.DataDirectory))
                        {
                            TryLog("Каталог данных является reparse point; отчёт не сохранён.");
                            return false;
                        }
                        // The report is written by an elevated process. A
                        // pre-existing user-writable data directory would allow
                        // a planted hardlink or a writable report file, so harden
                        // it once before the first write in this process.
                        AclInstaller.HardenDirectory(AppPaths.DataDirectory);
                    }
                    catch (Exception ex)
                    {
                        TryLog("Не удалось закрепить права на каталог данных: " +
                            ex.Message);
                        return false;
                    }
                }
                if (!SaveToPath(report, DefaultPath))
                {
                    TryLog("Не удалось сохранить отчёт последней проверки стратегий.");
                    return false;
                }
                if (!_securityPrepared)
                {
                    try
                    {
                        AclInstaller.HardenFile(DefaultPath);
                        _securityPrepared = true;
                    }
                    catch (Exception ex)
                    {
                        TryLog("Не удалось закрепить права на strategy-report.json: " +
                            ex.Message);
                        return false;
                    }
                }
                return true;
            }
        }

        public static bool SaveToPath(StrategyAdviceReport report, string path)
        {
            if (report == null || report.Results.Count == 0 ||
                string.IsNullOrEmpty(path))
            {
                return false;
            }
            try
            {
                StrategyReportFile data = ToFile(report);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                return AtomicFile.WriteAllText(path, serializer.Serialize(data));
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static StrategyAdviceReport Load()
        {
            return LoadFromPath(DefaultPath);
        }

        public static StrategyAdviceReport LoadFromPath(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path) ||
                    AppPaths.IsReparsePoint(path))
                {
                    return null;
                }
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) &&
                    AppPaths.IsReparsePoint(directory))
                {
                    return null;
                }
                string json;
                using (FileStream stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Delete))
                {
                    if (stream.Length <= 0 || stream.Length > 1048576)
                    {
                        return null;
                    }
                    using (StreamReader reader = new StreamReader(
                        stream, Encoding.UTF8, true))
                    {
                        json = reader.ReadToEnd();
                    }
                }
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                StrategyReportFile data =
                    serializer.Deserialize<StrategyReportFile>(json);
                if (data == null || data.Results == null)
                {
                    return null;
                }
                StrategyAdviceReport report = new StrategyAdviceReport();
                foreach (StrategyProbeFile item in data.Results)
                {
                    if (report.Results.Count >= MaxResults)
                    {
                        break;
                    }
                    if (item == null)
                    {
                        continue;
                    }
                    string name = CleanText(item.Name, 200, false);
                    if (name.Length == 0)
                    {
                        continue;
                    }
                    // Saved paths are display-only hints. Clearing them keeps a
                    // read-only report from ever becoming an executable input.
                    report.Results.Add(new StrategyProbeResult(
                        name,
                        string.Empty,
                        item.DiscordOk,
                        item.YouTubePageOk,
                        item.YouTubePlayerOk,
                        item.YouTubeVideoOk,
                        item.VideoSuccesses,
                        item.VideoAttempts,
                        item.GoogleOk,
                        item.ElapsedMs,
                        CleanText(item.Detail, 2000, true)));
                }
                if (report.Results.Count == 0)
                {
                    return null;
                }
                DateTime checkedAtUtc;
                if (string.IsNullOrEmpty(data.CheckedAtUtc) ||
                    !DateTime.TryParseExact(
                        data.CheckedAtUtc,
                        "o",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out checkedAtUtc))
                {
                    checkedAtUtc = File.GetLastWriteTimeUtc(path);
                }
                report.SetCheckedAtUtc(checkedAtUtc.ToUniversalTime());
                string error = CleanText(data.Error, 500, true);
                if (error.Length > 0)
                {
                    report.SetError(error);
                }
                report.SetServiceWasRunning(data.ServiceWasRunning);
                report.SetServiceRestored(data.ServiceRestored);

                // Trust the ranked list: only a result that still exists can be
                // recommended, otherwise fall back to the first full/partial one.
                StrategyProbeResult recommended =
                    FindByName(report.Results, data.RecommendedName);
                if (recommended == null || !recommended.Ok)
                {
                    recommended = FindFirst(report.Results, true);
                }
                if (recommended != null)
                {
                    report.SetRecommended(recommended);
                }
                StrategyProbeResult best = FindByName(report.Results, data.BestName);
                if (best == null)
                {
                    best = FindFirstPartial(report.Results);
                }
                if (best != null)
                {
                    report.SetBest(best);
                }
                return report;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static StrategyReportFile ToFile(StrategyAdviceReport report)
        {
            StrategyReportFile data = new StrategyReportFile();
            data.CheckedAtUtc = report.CheckedAtUtc.ToString(
                "o", CultureInfo.InvariantCulture);
            data.RecommendedName = report.RecommendedName;
            data.BestName = report.BestName;
            data.Error = report.Error;
            data.ServiceWasRunning = report.ServiceWasRunning;
            data.ServiceRestored = report.ServiceRestored;
            foreach (StrategyProbeResult result in report.Results)
            {
                StrategyProbeFile item = new StrategyProbeFile();
                item.Name = result.Name;
                item.Path = result.Path;
                item.DiscordOk = result.DiscordOk;
                item.YouTubePageOk = result.YouTubePageOk;
                item.YouTubePlayerOk = result.YouTubePlayerOk;
                item.YouTubeVideoOk = result.YouTubeVideoOk;
                item.GoogleOk = result.GoogleOk;
                item.VideoAttempts = result.VideoAttempts;
                item.VideoSuccesses = result.VideoSuccesses;
                item.ElapsedMs = result.ElapsedMs;
                item.Detail = result.Detail;
                data.Results.Add(item);
            }
            return data;
        }

        private static StrategyProbeResult FindByName(
            List<StrategyProbeResult> results, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            foreach (StrategyProbeResult result in results)
            {
                if (result.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return result;
                }
            }
            return null;
        }

        private static StrategyProbeResult FindFirst(
            List<StrategyProbeResult> results, bool requireFull)
        {
            foreach (StrategyProbeResult result in results)
            {
                if (!requireFull || result.Ok)
                {
                    return result;
                }
            }
            return null;
        }

        private static StrategyProbeResult FindFirstPartial(
            List<StrategyProbeResult> results)
        {
            foreach (StrategyProbeResult result in results)
            {
                if (result.PartiallyOk)
                {
                    return result;
                }
            }
            return null;
        }

        // Report text may come from an old or tampered file; drop control and
        // bidi-format characters and cap the length before it reaches the UI.
        private static string CleanText(string value, int maxLength, bool keepLineBreaks)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            StringBuilder builder = new StringBuilder(
                value.Length < maxLength ? value.Length : maxLength);
            foreach (char c in value)
            {
                if (builder.Length >= maxLength)
                {
                    break;
                }
                UnicodeCategory category = char.GetUnicodeCategory(c);
                if (category == UnicodeCategory.Control)
                {
                    if (keepLineBreaks && (c == '\r' || c == '\n' || c == '\t'))
                    {
                        builder.Append(c);
                    }
                    continue;
                }
                if (category == UnicodeCategory.Format)
                {
                    continue;
                }
                builder.Append(c);
            }
            return builder.ToString();
        }

        private static void TryLog(string message)
        {
            try
            {
                AppPaths.EnsureDirectory(AppPaths.LogDirectory);
                File.AppendAllText(
                    Path.Combine(AppPaths.LogDirectory, "ui-error.log"),
                    "=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===" +
                    Environment.NewLine + message + Environment.NewLine,
                    new UTF8Encoding(true));
            }
            catch (Exception)
            {
            }
        }
    }
}
