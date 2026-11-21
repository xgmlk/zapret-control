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
        public int Order { get; internal set; }

        public bool DiscordOk { get; internal set; }
        public int DiscordPassed { get; internal set; }
        public int DiscordTotal { get; internal set; }
        public bool YouTubeHostsOk { get; internal set; }
        public int YouTubeHostsPassed { get; internal set; }
        public int YouTubeHostsTotal { get; internal set; }
        public bool YouTubePageOk { get; internal set; }
        public bool YouTubePlayerOk { get; internal set; }
        public bool YouTubeVideoOk { get; internal set; }
        public bool GoogleOk { get; internal set; }
        public int GooglePassed { get; internal set; }
        public int GoogleTotal { get; internal set; }
        public bool CloudflareOk { get; internal set; }
        public int CloudflarePassed { get; internal set; }
        public int CloudflareTotal { get; internal set; }

        public bool FreezeAvailable { get; internal set; }
        public bool FreezeChecked { get; internal set; }
        public bool FreezeOk { get; internal set; }
        public int FreezeBlocked { get; internal set; }
        public int FreezeTargets { get; internal set; }
        public int FreezeOkCount { get; internal set; }
        public int FreezeErrors { get; internal set; }

        public bool Tls12Attempted { get; internal set; }
        public bool Tls12Ok { get; internal set; }
        public bool Tls13Attempted { get; internal set; }
        public bool Tls13Ok { get; internal set; }
        public bool Tls13EnvironmentUnsupported { get; internal set; }

        public int VideoSuccesses { get; internal set; }
        public int VideoAttempts { get; internal set; }
        public long ElapsedMs { get; internal set; }
        public string Detail { get; internal set; }
        public string FreezeDetail { get; internal set; }
        public string VideoDetail { get; internal set; }
        public string HostsDetail { get; internal set; }

        public StrategyProbeResult(string name, string path)
        {
            Name = name ?? string.Empty;
            Path = path ?? string.Empty;
            Detail = string.Empty;
            FreezeDetail = string.Empty;
            VideoDetail = string.Empty;
            HostsDetail = string.Empty;
        }

        public bool YouTubeStageOk
        {
            get { return YouTubeHostsOk && YouTubePageOk; }
        }

        public int Total
        {
            get { return 6; }
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
                if (YouTubeStageOk)
                {
                    count++;
                }
                if (YouTubePlayerOk)
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
                if (FreezeOk)
                {
                    count++;
                }
                return count;
            }
        }

        public bool Ok
        {
            get
            {
                return Passed == Total && FreezeAvailable && FreezeChecked;
            }
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

        public string YouTubeDisplay
        {
            get
            {
                string hosts = Counts(YouTubeHostsPassed, YouTubeHostsTotal);
                if (YouTubeHostsTotal == 0)
                {
                    return "нет";
                }
                return hosts + " · " + (YouTubePageOk ? "OK" : "нет");
            }
        }

        public string FreezeDisplay
        {
            get
            {
                if (!FreezeAvailable)
                {
                    // Old reports and a missing curl.exe both have no targets;
                    // the report header carries the explicit curl warning.
                    return FreezeTargets > 0 ? "нет curl" : "—";
                }
                if (!FreezeChecked)
                {
                    return "н/д";
                }
                if (!FreezeOk)
                {
                    return "блок " + FreezeBlocked;
                }
                return "OK";
            }
        }

        public string Tls12Display
        {
            get
            {
                if (!Tls12Attempted)
                {
                    return "—";
                }
                return Tls12Ok ? "OK" : "нет";
            }
        }

        public string Tls13Display
        {
            get
            {
                if (!Tls13Attempted)
                {
                    return "—";
                }
                if (Tls13EnvironmentUnsupported && !Tls13Ok)
                {
                    return "н/д";
                }
                return Tls13Ok ? "OK" : "нет";
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
                    return FreezeAvailable
                        ? "работает частично"
                        : "частично (DPI не проверен)";
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
                return "Discord " + Counts(DiscordPassed, DiscordTotal) +
                       " · YouTube хосты " + Counts(YouTubeHostsPassed, YouTubeHostsTotal) +
                       ", страница " + YesNo(YouTubePageOk) +
                       ", плеер " + YesNo(YouTubePlayerOk) +
                       ", видео " + VideoDisplay +
                       " · Google " + Counts(GooglePassed, GoogleTotal) +
                       " · DPI " + FreezeDisplay +
                       " · Cloudflare " + Counts(CloudflarePassed, CloudflareTotal);
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

        internal void SetCounts(
            int discordPassed, int discordTotal,
            int youtubeHostsPassed, int youtubeHostsTotal,
            int googlePassed, int googleTotal,
            int cloudflarePassed, int cloudflareTotal)
        {
            DiscordPassed = discordPassed;
            DiscordTotal = discordTotal;
            YouTubeHostsPassed = youtubeHostsPassed;
            YouTubeHostsTotal = youtubeHostsTotal;
            GooglePassed = googlePassed;
            GoogleTotal = googleTotal;
            CloudflarePassed = cloudflarePassed;
            CloudflareTotal = cloudflareTotal;
        }

        internal void SetFreeze(
            bool available, bool checkedValue, bool ok,
            int blocked, int targets, int okCount, int errors, string detail)
        {
            FreezeAvailable = available;
            FreezeChecked = checkedValue;
            FreezeOk = ok;
            FreezeBlocked = blocked;
            FreezeTargets = targets;
            FreezeOkCount = okCount;
            FreezeErrors = errors;
            FreezeDetail = detail ?? string.Empty;
        }

        private static string Counts(int passed, int total)
        {
            return total == 0 ? "—" : passed + "/" + total;
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
        public int GateLimit { get; set; }
        public bool RestoreService { get; set; }
        public string Preferred { get; set; }

        public StrategyAdviceOptions()
        {
            // 0 means the whole .bat pool. The advisor must not stop at the
            // first page-level success anymore.
            MaxCandidates = 0;
            RequestTimeoutMs = 4000;
            GateLimit = 16;
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
        public string IpsetMode { get; private set; }
        public string IpsetOriginal { get; private set; }
        public bool IpsetSwitched { get; private set; }
        public bool IpsetRestored { get; private set; }
        public bool CurlAvailable { get; private set; }
        public bool FreezeSuiteOffline { get; private set; }
        public long ElapsedMs { get; private set; }
        public DateTime CheckedAtUtc { get; private set; }

        public StrategyAdviceReport()
        {
            Results = new List<StrategyProbeResult>();
            RecommendedName = string.Empty;
            RecommendedPath = string.Empty;
            BestName = string.Empty;
            Error = string.Empty;
            IpsetMode = string.Empty;
            IpsetOriginal = string.Empty;
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

        internal void AppendError(string error)
        {
            if (string.IsNullOrEmpty(error))
            {
                return;
            }
            Error = string.IsNullOrEmpty(Error) ? error : Error + " " + error;
        }

        internal void SetServiceWasRunning(bool wasRunning)
        {
            ServiceWasRunning = wasRunning;
        }

        internal void SetServiceRestored(bool restored)
        {
            ServiceRestored = restored;
        }

        internal void SetIpset(string original, string mode)
        {
            IpsetOriginal = original ?? string.Empty;
            IpsetMode = mode ?? string.Empty;
        }

        internal void SetIpsetSwitched(bool switched)
        {
            IpsetSwitched = switched;
        }

        internal void SetIpsetRestored(bool restored)
        {
            IpsetRestored = restored;
        }

        internal void SetCurlAvailable(bool available)
        {
            CurlAvailable = available;
        }

        internal void SetFreezeSuiteOffline(bool offline)
        {
            FreezeSuiteOffline = offline;
        }

        internal void SetElapsedMs(long elapsedMs)
        {
            ElapsedMs = elapsedMs;
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
            if (!string.IsNullOrEmpty(IpsetMode))
            {
                builder.AppendLine("IPSet: " + IpsetMode + " (исходное " +
                    (string.IsNullOrEmpty(IpsetOriginal) ? "?" : IpsetOriginal) +
                    ", восстановлен: " + (IpsetRestored ? "да" : "нет") + ").");
            }
            builder.AppendLine("curl.exe: " + (CurlAvailable ? "есть" : "нет") +
                " · список suite: " +
                (FreezeSuiteOffline ? "оффлайн" : "онлайн") + ".");
            builder.AppendLine("Время: " +
                (ElapsedMs / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) +
                " с.");
            if (!string.IsNullOrEmpty(Error))
            {
                builder.AppendLine("Ошибка: " + Error);
            }
            return builder.ToString();
        }
    }

    // Tests candidate .bat strategies by starting winws.exe directly. Inside
    // one strategy all independent checks run in parallel behind a 16-slot
    // gate; strategies stay sequential because they share WinDivert filters.
    // The service and IPSet are restored in finally.
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

        // A named mutex prevents two processes (GUI, CLI, Setup) from stopping
        // the service and switching IPSet at the same time.
        public static StrategyAdviceReport Recommend(
            string payloadRoot,
            StrategyAdviceOptions options,
            Action<string> log)
        {
            Mutex mutex = null;
            bool acquired = false;
            try
            {
                try
                {
                    mutex = new Mutex(false, @"Global\ZapretControl.Advisor");
                }
                catch (Exception)
                {
                    try
                    {
                        mutex = new Mutex(false, @"Local\ZapretControl.Advisor");
                    }
                    catch (Exception)
                    {
                        mutex = null;
                    }
                }
                if (mutex == null)
                {
                    acquired = true;
                }
                else
                {
                    try
                    {
                        acquired = mutex.WaitOne(0, false);
                    }
                    catch (AbandonedMutexException)
                    {
                        acquired = true;
                    }
                    catch (Exception)
                    {
                        // A restricted environment must not block the advisor.
                        acquired = true;
                    }
                }
                if (!acquired)
                {
                    StrategyAdviceReport busy = new StrategyAdviceReport();
                    busy.SetError("Другой подбор стратегии уже выполняется.");
                    return busy;
                }
                return RecommendCore(payloadRoot, options, log);
            }
            finally
            {
                if (acquired && mutex != null)
                {
                    try
                    {
                        mutex.ReleaseMutex();
                    }
                    catch (Exception)
                    {
                    }
                }
                if (mutex != null)
                {
                    try
                    {
                        mutex.Dispose();
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        private static StrategyAdviceReport RecommendCore(
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

            Stopwatch watch = Stopwatch.StartNew();
            GameFilterSettings game = GameFilterSettings.Load(payloadRoot);
            int iface = ResolveIface();
            string curl = FreezeProbe.FindCurl();
            report.SetCurlAvailable(curl != null);
            bool suiteOffline = false;
            List<FreezeTarget> suite = new List<FreezeTarget>();
            if (curl != null)
            {
                suite = FreezeProbe.LoadSuite(
                    value.RequestTimeoutMs, out suiteOffline);
                report.SetFreezeSuiteOffline(suiteOffline);
                if (suiteOffline)
                {
                    Log(log, "Список suite.v2.json недоступен: используется встроенный.");
                }
            }
            else
            {
                Log(log, "curl.exe не найден: DPI-заморозка не будет проверена.");
            }

            ZapretStatus before = ZapretService.GetStatus();
            bool wasRunning = before.State == ZapretState.Running ||
                              before.State == ZapretState.Starting;
            report.SetServiceWasRunning(wasRunning);

            string originalIpset = IpsetManager.GetState(payloadRoot);
            report.SetIpset(originalIpset, originalIpset);
            bool serviceStopped = false;
            bool ipsetSwitched = false;
            bool ipsetFinalized = false;
            string payloadFile = null;

            try
            {
                if (wasRunning)
                {
                    Log(log, "Останавливаю службу для проверки стратегий…");
                    serviceStopped = true;
                    ServiceManager.StopServiceOnly(AppInfo.ServiceName);
                }
                bool checkFailed;
                if (!WaitForWinwsExit(5000, out checkFailed))
                {
                    report.SetError(checkFailed
                        ? "Не удалось проверить winws.exe. Подбор не начат."
                        : "winws.exe уже запущен вне службы. Остановите его и повторите.");
                    return report;
                }

                if (originalIpset != IpsetManager.StateAny)
                {
                    Log(log, "Переключаю IPSet в «любой» на время проверки…");
                    try
                    {
                        ipsetSwitched = IpsetSwitch.Begin(
                            payloadRoot, wasRunning, out originalIpset);
                    }
                    catch (Exception ex)
                    {
                        string restoreError = IpsetSwitch.Restore(
                            payloadRoot, originalIpset);
                        ipsetFinalized = true;
                        report.SetIpsetRestored(restoreError == null);
                        report.SetError("Не удалось переключить IPSet: " +
                            ex.Message +
                            (restoreError == null ? string.Empty : " " + restoreError));
                        return report;
                    }
                    if (ipsetSwitched)
                    {
                        report.SetIpset(
                            originalIpset, IpsetManager.StateAny);
                        report.SetIpsetSwitched(true);
                    }
                }

                if (curl != null)
                {
                    try
                    {
                        payloadFile = FreezeProbe.CreatePayloadFile();
                    }
                    catch (Exception ex)
                    {
                        // Keep curl available: the real reason belongs to the
                        // FreezeProbe detail, not to a false "curl missing".
                        Log(log, "Не удалось создать файл для DPI-проверки: " +
                                 ex.Message);
                        payloadFile = null;
                    }
                }

                UnifiedCheckOptions checkOptions = new UnifiedCheckOptions();
                checkOptions.RequestTimeoutMs = value.RequestTimeoutMs;
                checkOptions.FreezeTimeoutSeconds = 5;
                checkOptions.GateLimit = value.GateLimit < 1
                    ? 1
                    : (value.GateLimit > 16 ? 16 : value.GateLimit);

                Log(log, "Проверяю " + candidates.Count + " стратегий…");
                for (int i = 0; i < candidates.Count; i++)
                {
                    StrategyInfo info = candidates[i];
                    Log(log, "Проверяю " + info.Name + " (" + (i + 1) +
                             " из " + candidates.Count + ")…");
                    bool stopped;
                    StrategyProbeResult result = Probe(
                        payloadRoot, info, game, iface, suite, suiteOffline,
                        curl, payloadFile, checkOptions, log, out stopped);
                    result.Order = i;
                    report.Results.Add(result);
                    Log(log, result.Display);
                    if (!result.Ok)
                    {
                        Log(log, "  " + result.Detail);
                    }
                    if (!stopped)
                    {
                        report.SetError(
                            "winws.exe не остановился после проверки. Подбор прерван.");
                        Log(log, report.Error);
                        break;
                    }
                }

                for (int i = 0; i < report.Results.Count; i++)
                {
                    if (report.Results[i].Ok)
                    {
                        report.SetRecommended(report.Results[i]);
                        break;
                    }
                }
                SortResults(report.Results);
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
                    if (string.IsNullOrEmpty(report.Error))
                    {
                        report.SetError("Ни одна стратегия не прошла проверку.");
                    }
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
                bool ipsetOk = true;
                if (ipsetSwitched)
                {
                    // Keep the flag until the service is restored as well.
                    string restoreError = IpsetSwitch.Restore(
                        payloadRoot, originalIpset, true);
                    ipsetOk = restoreError == null;
                    report.SetIpsetRestored(ipsetOk);
                    ipsetFinalized = true;
                    if (restoreError != null)
                    {
                        report.AppendError(restoreError);
                        Log(log, restoreError);
                    }
                }
                if (!ipsetFinalized)
                {
                    report.SetIpsetRestored(true);
                }
                FreezeProbe.TryDelete(payloadFile);

                bool serviceOk;
                if (wasRunning && serviceStopped && value.RestoreService)
                {
                    if (!ipsetOk)
                    {
                        serviceOk = false;
                        report.SetServiceRestored(false);
                        report.AppendError(
                            "Служба не запущена: IPSet не восстановлен.");
                    }
                    else
                    {
                        serviceOk = false;
                        try
                        {
                            ZapretService.Start();
                            serviceOk = ZapretService.GetStatus().State ==
                                        ZapretState.Running;
                            report.SetServiceRestored(serviceOk);
                            if (!serviceOk)
                            {
                                report.AppendError(
                                    "Служба не вернулась после проверки.");
                            }
                        }
                        catch (Exception ex)
                        {
                            serviceOk = false;
                            report.SetServiceRestored(false);
                            report.AppendError(
                                "Служба не вернулась после проверки: " + ex.Message);
                            Log(log, "Не удалось вернуть службу: " + ex.Message);
                        }
                    }
                }
                else
                {
                    serviceOk = !wasRunning || !serviceStopped;
                    report.SetServiceRestored(serviceOk);
                }
                if (ipsetSwitched && ipsetOk && serviceOk)
                {
                    IpsetSwitch.ClearFlag();
                }
                watch.Stop();
                report.SetElapsedMs(watch.ElapsedMilliseconds);
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

        // Drained while winws runs: an unread anonymous pipe (about 4 KB) can
        // block the tested process and make a working strategy look dead.
        private sealed class ErrorCollector
        {
            private readonly StringBuilder _text = new StringBuilder();
            private readonly object _sync = new object();

            internal void Attach(Process process)
            {
                try
                {
                    process.ErrorDataReceived += OnData;
                    process.BeginErrorReadLine();
                }
                catch (Exception)
                {
                }
            }

            internal string Text
            {
                get
                {
                    lock (_sync)
                    {
                        return Short(_text.ToString().Trim());
                    }
                }
            }

            private void OnData(object sender, DataReceivedEventArgs e)
            {
                if (e.Data == null)
                {
                    return;
                }
                lock (_sync)
                {
                    if (_text.Length < 8192)
                    {
                        _text.AppendLine(e.Data);
                    }
                }
            }
        }

        private static StrategyProbeResult Probe(
            string payloadRoot,
            StrategyInfo info,
            GameFilterSettings game,
            int iface,
            List<FreezeTarget> suite,
            bool suiteOffline,
            string curlPath,
            string payloadFile,
            UnifiedCheckOptions options,
            Action<string> log,
            out bool stopped)
        {
            stopped = true;
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
            ErrorCollector errors = new ErrorCollector();
            try
            {
                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = command.WinwsPath;
                start.Arguments = command.Arguments;
                start.WorkingDirectory = System.IO.Path.GetDirectoryName(
                    command.WinwsPath);
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                start.RedirectStandardError = true;
                Stopwatch watch = Stopwatch.StartNew();
                process = Process.Start(start);
                if (process == null)
                {
                    probeResult = Failed(info, 0, "winws.exe не запустился");
                }
                else
                {
                    errors.Attach(process);
                    string readyDetail;
                    if (!WaitReady(process, errors, out readyDetail))
                    {
                        probeResult = Failed(
                            info, watch.ElapsedMilliseconds, readyDetail);
                    }
                    else
                    {
                        Thread.Sleep(500);
                        if (process.HasExited)
                        {
                            string earlyOutput = errors.Text;
                            probeResult = Failed(
                                info, watch.ElapsedMilliseconds,
                                "winws.exe завершился до проверки (код " +
                                process.ExitCode + ")" +
                                (string.IsNullOrEmpty(earlyOutput)
                                    ? string.Empty
                                    : ": " + earlyOutput));
                        }
                        else
                        {
                            StrategyProbeResult checkedResult = UnifiedCheck.Run(
                                info.Name, info.Path, suite, suiteOffline,
                                curlPath, payloadFile, options, log);
                            if (process.HasExited)
                            {
                                string output = errors.Text;
                                probeResult = Failed(
                                    info, watch.ElapsedMilliseconds,
                                    "winws.exe завершился во время проверки (код " +
                                    process.ExitCode + ")" +
                                    (string.IsNullOrEmpty(output)
                                        ? string.Empty
                                        : ": " + output));
                            }
                            else
                            {
                                checkedResult.ElapsedMs = watch.ElapsedMilliseconds;
                                probeResult = checkedResult;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                probeResult = Failed(info, 0, Short(ex.Message));
            }
            finally
            {
                stopped = StopProcess(process, 2000);
            }
            if (!stopped)
            {
                return Failed(
                    info,
                    probeResult == null ? 0 : probeResult.ElapsedMs,
                    "winws.exe не остановился");
            }
            return probeResult ?? Failed(info, 0, "проверка не выполнена");
        }

        private static bool WaitReady(
            Process process, ErrorCollector errors, out string detail)
        {
            detail = string.Empty;
            // Poll every 200 ms until the process has stayed alive for 500 ms
            // (3 s is the safety cap). The caller then adds its 500 ms pause.
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 3000)
            {
                if (process.HasExited)
                {
                    string output = errors.Text;
                    detail = "winws.exe завершился (код " + process.ExitCode + ")" +
                             (string.IsNullOrEmpty(output)
                                 ? string.Empty
                                 : ": " + output);
                    return false;
                }
                if (watch.ElapsedMilliseconds >= 500)
                {
                    return true;
                }
                Thread.Sleep(200);
            }
            return !process.HasExited;
        }

        private static StrategyProbeResult Failed(
            StrategyInfo info, long elapsedMs, string detail)
        {
            StrategyProbeResult result = new StrategyProbeResult(
                info.Name, info.Path);
            result.ElapsedMs = elapsedMs;
            result.Detail = detail ?? string.Empty;
            return result;
        }

        private static bool StopProcess(Process process, int timeoutMs)
        {
            if (process == null)
            {
                return true;
            }
            try
            {
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                        // The process exited between HasExited and Kill.
                        return true;
                    }
                    process.WaitForExit(timeoutMs);
                }
                return process.HasExited;
            }
            catch (Exception)
            {
                try
                {
                    return process.HasExited;
                }
                catch (Exception)
                {
                    return false;
                }
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

        private static bool WaitForWinwsExit(int timeoutMs, out bool checkFailed)
        {
            checkFailed = false;
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                bool failed;
                if (!HasWinws(out failed))
                {
                    checkFailed = failed;
                    return !failed;
                }
                if (failed)
                {
                    checkFailed = true;
                    return false;
                }
                Thread.Sleep(250);
            }
            bool finalFailed;
            bool found = HasWinws(out finalFailed);
            checkFailed = finalFailed;
            return !found && !finalFailed;
        }

        private static bool HasWinws(out bool checkFailed)
        {
            checkFailed = false;
            try
            {
                Process[] processes = Process.GetProcessesByName(
                    ZapretService.ProcessName);
                bool found = processes.Length > 0;
                foreach (Process process in processes)
                {
                    process.Dispose();
                }
                return found;
            }
            catch (Exception)
            {
                // Fail closed: an unreadable process list must not start a
                // second winws next to a possibly running one.
                checkFailed = true;
                return true;
            }
        }

        private static string Short(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "нет подробностей";
            }
            string value = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return value.Length > 200 ? value.Substring(0, 200) : value;
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
