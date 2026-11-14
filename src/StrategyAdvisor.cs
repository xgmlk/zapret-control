using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;

namespace ZapretControl
{
    public sealed class StrategyProbeResult
    {
        public string Name { get; private set; }
        public string Path { get; private set; }
        public bool DiscordOk { get; private set; }
        public bool YouTubePageOk { get; private set; }
        public bool YouTubePlayerOk { get; private set; }
        public bool YouTubeVideoOk { get; private set; }
        public bool GoogleOk { get; private set; }
        public int VideoAttempts { get; private set; }
        public int VideoSuccesses { get; private set; }
        public long ElapsedMs { get; private set; }
        public string Detail { get; private set; }

        public StrategyProbeResult(
            string name,
            string path,
            bool discordOk,
            bool youtubePageOk,
            bool youtubePlayerOk,
            bool youtubeVideoOk,
            int videoSuccesses,
            int videoAttempts,
            bool googleOk,
            long elapsedMs,
            string detail)
        {
            Name = name;
            Path = path;
            DiscordOk = discordOk;
            YouTubePageOk = youtubePageOk;
            YouTubePlayerOk = youtubePlayerOk;
            YouTubeVideoOk = youtubeVideoOk;
            VideoSuccesses = videoSuccesses;
            VideoAttempts = videoAttempts;
            GoogleOk = googleOk;
            ElapsedMs = elapsedMs;
            Detail = detail ?? string.Empty;
        }

        public int Total
        {
            get { return 4; }
        }

        public int Passed
        {
            get
            {
                int count = 0;
                if (DiscordOk)
                {
                    count++;
                }
                if (YouTubePageOk)
                {
                    count++;
                }
                if (YouTubeVideoOk)
                {
                    count++;
                }
                if (GoogleOk)
                {
                    count++;
                }
                return count;
            }
        }

        public bool Ok
        {
            get { return Passed == Total; }
        }

        public bool PartiallyOk
        {
            get { return Passed > 0; }
        }

        public string VideoDisplay
        {
            get
            {
                if (!YouTubePlayerOk)
                {
                    return "плеер";
                }
                return VideoSuccesses + "/" + VideoAttempts;
            }
        }

        public string StatusText
        {
            get
            {
                if (Ok)
                {
                    return "работает";
                }
                if (PartiallyOk)
                {
                    return "работает частично";
                }
                return "не работает";
            }
        }

        public string SummaryText
        {
            get { return Passed + "/" + Total + " · " + StatusText; }
        }

        public string StagesText
        {
            get
            {
                return "Discord: " + YesNo(DiscordOk) +
                       " · YouTube: " + YesNo(YouTubePageOk) +
                       " · плеер: " + YesNo(YouTubePlayerOk) +
                       " · видео: " + VideoDisplay +
                       " · Google: " + YesNo(GoogleOk);
            }
        }

        public string Display
        {
            get
            {
                return Name + " — " + Passed + " из " + Total +
                       " (" + StatusText + ")";
            }
        }

        private static string YesNo(bool value)
        {
            return value ? "OK" : "нет";
        }
    }

    public sealed class StrategyAdviceOptions
    {
        public int MaxCandidates { get; set; }
        public int RequestTimeoutMs { get; set; }
        public bool RestoreService { get; set; }
        public string Preferred { get; set; }

        public StrategyAdviceOptions()
        {
            // 0 means the whole .bat pool. The advisor must not stop at the
            // first page-level success anymore.
            MaxCandidates = 0;
            RequestTimeoutMs = 6000;
            RestoreService = true;
            Preferred = string.Empty;
        }
    }

    public sealed class StrategyAdviceReport
    {
        public List<StrategyProbeResult> Results { get; private set; }
        public string RecommendedName { get; private set; }
        public string RecommendedPath { get; private set; }
        public int RecommendedPassed { get; private set; }
        public int RecommendedTotal { get; private set; }
        public string BestName { get; private set; }
        public int BestPassed { get; private set; }
        public int BestTotal { get; private set; }
        public string Error { get; private set; }
        public bool ServiceWasRunning { get; private set; }
        public bool ServiceRestored { get; private set; }
        public DateTime CheckedAtUtc { get; private set; }

        public StrategyAdviceReport()
        {
            Results = new List<StrategyProbeResult>();
            RecommendedName = string.Empty;
            RecommendedPath = string.Empty;
            BestName = string.Empty;
            Error = string.Empty;
            CheckedAtUtc = DateTime.UtcNow;
        }

        // Local, user-facing stamp for the results window and the home button.
        public string CheckedAtDisplay
        {
            get
            {
                if (CheckedAtUtc == DateTime.MinValue)
                {
                    return string.Empty;
                }
                DateTime local = CheckedAtUtc.Kind == DateTimeKind.Utc
                    ? CheckedAtUtc.ToLocalTime()
                    : CheckedAtUtc;
                return local.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
            }
        }

        internal void SetCheckedAtUtc(DateTime value)
        {
            CheckedAtUtc = value;
        }

        public bool HasRecommendation
        {
            get { return !string.IsNullOrEmpty(RecommendedName); }
        }

        public bool HasPartial
        {
            get { return !string.IsNullOrEmpty(BestName); }
        }

        public void SetRecommended(StrategyProbeResult result)
        {
            RecommendedName = result.Name;
            RecommendedPath = result.Path;
            RecommendedPassed = result.Passed;
            RecommendedTotal = result.Total;
        }

        public void SetBest(StrategyProbeResult result)
        {
            BestName = result.Name;
            BestPassed = result.Passed;
            BestTotal = result.Total;
        }

        internal void SetError(string error)
        {
            Error = error ?? string.Empty;
        }

        internal void SetServiceWasRunning(bool wasRunning)
        {
            ServiceWasRunning = wasRunning;
        }

        internal void SetServiceRestored(bool restored)
        {
            ServiceRestored = restored;
        }

        public string ToText()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Zapret Control — подбор стратегии");
            foreach (StrategyProbeResult result in Results)
            {
                string mark = result.Ok ? "[OK] " :
                    (result.PartiallyOk ? "[PART] " : "[FAIL] ");
                builder.Append(mark).AppendLine(result.Display);
                builder.Append("  ").AppendLine(result.StagesText);
                if (!result.Ok)
                {
                    builder.Append("  ").AppendLine(result.Detail);
                }
            }
            if (!string.IsNullOrEmpty(RecommendedName))
            {
                builder.AppendLine("Рекомендация: " + RecommendedName + " (" +
                    RecommendedPassed + " из " + RecommendedTotal + ").");
            }
            else if (!string.IsNullOrEmpty(BestName))
            {
                builder.AppendLine("Лучший частичный результат: " + BestName + " (" +
                    BestPassed + " из " + BestTotal + ").");
            }
            if (!string.IsNullOrEmpty(Error))
            {
                builder.AppendLine("Ошибка: " + Error);
            }
            return builder.ToString();
        }
    }

    // Tests candidate .bat strategies by starting winws.exe directly and probing
    // Discord, the YouTube watch page, real YouTube media bytes and Google. All
    // candidates are tested; the result is a ranked list, not the first match.
    // The service is stopped for the duration and restored afterwards.
    public static class StrategyAdvisor
    {
        private static readonly string[] PriorityNames = new string[]
        {
            "general",
            "general (FAKE TLS AUTO)",
            "general (FAKE TLS AUTO ALT)",
            "general (FAKE TLS AUTO ALT2)",
            "general (FAKE TLS AUTO ALT3)",
            "general (SIMPLE FAKE)",
            "general (SIMPLE FAKE ALT)",
            "general (SIMPLE FAKE ALT2)",
            "general (ALT9)",
            "general (ALT)",
            "general (EXP)"
        };

        public static StrategyAdviceReport Recommend(
            string payloadRoot,
            StrategyAdviceOptions options,
            Action<string> log)
        {
            StrategyAdviceOptions value = options ?? new StrategyAdviceOptions();
            StrategyAdviceReport report = new StrategyAdviceReport();
            if (string.IsNullOrEmpty(payloadRoot) || !AppPaths.IsPayloadRoot(payloadRoot))
            {
                report.SetError("Папка zapret не найдена.");
                return report;
            }

            string preferred = value.Preferred;
            if (string.IsNullOrEmpty(preferred))
            {
                preferred = ServiceManager.GetCurrentStrategyName();
            }
            List<StrategyInfo> candidates = OrderCandidates(
                StrategyParser.ListStrategies(payloadRoot), preferred);
            if (candidates.Count == 0)
            {
                report.SetError("В payload нет стратегий .bat.");
                return report;
            }
            int max = value.MaxCandidates;
            if (max > 0 && candidates.Count > max)
            {
                candidates = candidates.GetRange(0, max);
            }

            GameFilterSettings game = GameFilterSettings.Load(payloadRoot);
            int iface = ResolveIface();
            ZapretStatus before = ZapretService.GetStatus();
            bool wasRunning = before.State == ZapretState.Running ||
                              before.State == ZapretState.Starting;
            report.SetServiceWasRunning(wasRunning);
            bool serviceStopped = false;
            try
            {
                if (wasRunning)
                {
                    Log(log, "Останавливаю службу для проверки стратегий…");
                    serviceStopped = true;
                    ServiceManager.StopServiceOnly(AppInfo.ServiceName);
                }
                if (!WaitForWinwsExit(5000))
                {
                    report.SetError(
                        "winws.exe уже запущен вне службы. Остановите его и повторите.");
                    return report;
                }

                Log(log, "Проверяю " + candidates.Count + " стратегий…");
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (i > 0 && !WaitForWinwsExit(5000))
                    {
                        report.SetError(
                            "winws.exe не остановился после предыдущей проверки. " +
                            "Подбор прерван.");
                        Log(log, report.Error);
                        break;
                    }
                    StrategyInfo info = candidates[i];
                    Log(log, "Проверяю " + info.Name + " (" + (i + 1) +
                             " из " + candidates.Count + ")…");
                    StrategyProbeResult result = Probe(
                        payloadRoot, info, game, iface, value.RequestTimeoutMs);
                    report.Results.Add(result);
                    Log(log, result.Display);
                    if (!result.Ok)
                    {
                        Log(log, "  " + result.Detail);
                    }
                }

                SortResults(report.Results);
                foreach (StrategyProbeResult result in report.Results)
                {
                    if (result.Ok && !report.HasRecommendation)
                    {
                        report.SetRecommended(result);
                    }
                }
                if (!report.HasRecommendation)
                {
                    foreach (StrategyProbeResult result in report.Results)
                    {
                        if (result.PartiallyOk)
                        {
                            report.SetBest(result);
                            break;
                        }
                    }
                }
                if (report.HasRecommendation)
                {
                    Log(log, "Рекомендация: " + report.RecommendedName + " (" +
                             report.RecommendedPassed + " из " +
                             report.RecommendedTotal + ").");
                }
                else if (report.HasPartial)
                {
                    Log(log, "Полностью работающих нет. Лучший частичный результат: " +
                             report.BestName + " (" + report.BestPassed + " из " +
                             report.BestTotal + ").");
                }
                else
                {
                    report.SetError("Ни одна стратегия не прошла проверку.");
                    Log(log, report.Error);
                }
            }
            catch (Exception ex)
            {
                report.SetError("Подбор стратегии прерван: " + ex.Message);
                Log(log, report.Error);
            }
            finally
            {
                if (wasRunning && serviceStopped && value.RestoreService)
                {
                    try
                    {
                        ZapretService.Start();
                        report.SetServiceRestored(
                            ZapretService.GetStatus().State == ZapretState.Running);
                    }
                    catch (Exception ex)
                    {
                        Log(log, "Не удалось вернуть службу: " + ex.Message);
                    }
                }
                // The stamp means "when the check finished", not when it began.
                report.SetCheckedAtUtc(DateTime.UtcNow);
            }
            return report;
        }

        // Order is deterministic so the UI and the installer test the same
        // candidates: current strategy, known general variants, then the rest
        // in natural .bat order.
        internal static List<StrategyInfo> OrderCandidates(
            List<StrategyInfo> all, string preferred)
        {
            List<StrategyInfo> ordered = new List<StrategyInfo>();
            if (all == null)
            {
                return ordered;
            }
            AddByName(ordered, all, preferred);
            foreach (string name in PriorityNames)
            {
                AddByName(ordered, all, name);
            }
            foreach (StrategyInfo info in all)
            {
                AddUnique(ordered, info);
            }
            return ordered;
        }

        // A full pass wins. Partial results are ranked by completeness, then
        // by whether the video stage works, then by video stability and speed.
        internal static void SortResults(List<StrategyProbeResult> results)
        {
            results.Sort(CompareResults);
        }

        private static int CompareResults(
            StrategyProbeResult left, StrategyProbeResult right)
        {
            if (left.Ok != right.Ok)
            {
                return left.Ok ? -1 : 1;
            }
            if (left.Passed != right.Passed)
            {
                return right.Passed.CompareTo(left.Passed);
            }
            if (left.YouTubeVideoOk != right.YouTubeVideoOk)
            {
                return left.YouTubeVideoOk ? -1 : 1;
            }
            if (left.VideoSuccesses != right.VideoSuccesses)
            {
                return right.VideoSuccesses.CompareTo(left.VideoSuccesses);
            }
            if (left.ElapsedMs != right.ElapsedMs)
            {
                return left.ElapsedMs.CompareTo(right.ElapsedMs);
            }
            return StrategyParser.NaturalCompare(left.Name, right.Name);
        }

        private static void AddByName(
            List<StrategyInfo> target, List<StrategyInfo> all, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }
            foreach (StrategyInfo info in all)
            {
                if (info.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    AddUnique(target, info);
                    return;
                }
            }
        }

        private static void AddUnique(List<StrategyInfo> target, StrategyInfo info)
        {
            foreach (StrategyInfo existing in target)
            {
                if (existing.Path.Equals(info.Path, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            target.Add(info);
        }

        private static int ResolveIface()
        {
            int iface = AppPaths.GetServiceIfaceIndex();
            IfaceStatus status = IfaceStatusProvider.GetStatus();
            if (status != null && status.ActiveFound)
            {
                iface = status.ActiveIndex;
            }
            return iface;
        }

        private static StrategyProbeResult Probe(
            string payloadRoot,
            StrategyInfo info,
            GameFilterSettings game,
            int iface,
            int timeoutMs)
        {
            StrategyCommand command;
            try
            {
                command = StrategyParser.Build(
                    payloadRoot, info.Path, game, iface);
            }
            catch (Exception ex)
            {
                return Failed(info, 0, Short(ex.Message));
            }

            Process process = null;
            StrategyProbeResult probeResult = null;
            try
            {
                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = command.WinwsPath;
                start.Arguments = command.Arguments;
                start.WorkingDirectory = System.IO.Path.GetDirectoryName(command.WinwsPath);
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                start.RedirectStandardError = true;
                Stopwatch watch = Stopwatch.StartNew();
                process = Process.Start(start);
                if (process == null)
                {
                    probeResult = Failed(info, 0, "winws.exe не запустился");
                }
                else if (process.WaitForExit(1500))
                {
                    string output = ReadError(process);
                    probeResult = Failed(
                        info, 0, "winws.exe завершился (код " + process.ExitCode + ")" +
                        (string.IsNullOrEmpty(output) ? string.Empty : ": " + output));
                }
                else
                {
                    ConnectionTestResult discord = ConnectionTests.ProbeUrl(
                        "https://discord.com/", timeoutMs);
                    YouTubeProbeResult video = YouTubeProbe.Run(timeoutMs, 3);
                    ConnectionTestResult google = ConnectionTests.ProbeUrl(
                        "https://www.google.com/", timeoutMs);
                    watch.Stop();

                    if (process.HasExited)
                    {
                        string output = ReadError(process);
                        probeResult = Failed(
                            info, watch.ElapsedMilliseconds,
                            "winws.exe завершился во время проверки (код " +
                            process.ExitCode + ")" +
                            (string.IsNullOrEmpty(output) ? string.Empty : ": " + output));
                    }
                    else
                    {
                        List<string> failures = new List<string>();
                        if (!discord.Success)
                        {
                            failures.Add("Discord: " + Short(discord.Detail));
                        }
                        if (!video.PageOk)
                        {
                            failures.Add("YouTube страница: " + Short(video.PageDetail));
                        }
                        else if (!video.PlayerOk)
                        {
                            failures.Add("YouTube плеер: " + Short(video.PlayerDetail));
                        }
                        else if (!video.CdnOk)
                        {
                            failures.Add("YouTube видео: " + Short(video.CdnDetail));
                        }
                        if (!google.Success)
                        {
                            failures.Add("Google: " + Short(google.Detail));
                        }
                        string detail = failures.Count == 0
                            ? "все проверки пройдены, видео " + video.Successes +
                              " из " + video.Attempts
                            : "не прошло: " + Join(failures);
                        probeResult = new StrategyProbeResult(
                            info.Name,
                            info.Path,
                            discord.Success,
                            video.PageOk,
                            video.PlayerOk,
                            video.CdnOk,
                            video.Successes,
                            video.Attempts,
                            google.Success,
                            watch.ElapsedMilliseconds,
                            detail);
                    }
                }
            }
            catch (Exception ex)
            {
                probeResult = Failed(info, 0, Short(ex.Message));
            }
            finally
            {
                if (!StopProcess(process) && probeResult != null)
                {
                    probeResult = Failed(info, probeResult.ElapsedMs,
                        "winws.exe не остановился");
                }
            }
            return probeResult;
        }

        private static StrategyProbeResult Failed(
            StrategyInfo info, long elapsedMs, string detail)
        {
            return new StrategyProbeResult(
                info.Name, info.Path, false, false, false, false, 0, 0, false,
                elapsedMs, detail);
        }

        private static string Join(List<string> values)
        {
            StringBuilder builder = new StringBuilder();
            foreach (string value in values)
            {
                if (builder.Length > 0)
                {
                    builder.Append("; ");
                }
                builder.Append(value);
            }
            return builder.ToString();
        }

        private static string ReadError(Process process)
        {
            try
            {
                string text = process.StandardError.ReadToEnd();
                return Short(text == null ? string.Empty : text.Trim());
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static bool StopProcess(Process process)
        {
            if (process == null)
            {
                return true;
            }
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
                return process.HasExited;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                try
                {
                    process.Dispose();
                }
                catch (Exception)
                {
                }
            }
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

        private static string Short(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "нет подробностей";
            }
            return text.Length > 200 ? text.Substring(0, 200) : text;
        }

        private static void Log(Action<string> log, string message)
        {
            if (log != null)
            {
                log(message);
            }
        }
    }
}
