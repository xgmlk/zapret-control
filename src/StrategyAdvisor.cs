using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace ZapretControl
{
    public sealed class StrategyProbeResult
    {
        public string Name { get; private set; }
        public string Path { get; private set; }
        public int Passed { get; private set; }
        public int Total { get; private set; }
        public string Detail { get; private set; }

        public StrategyProbeResult(
            string name, string path, int passed, int total, string detail)
        {
            Name = name;
            Path = path;
            Passed = passed;
            Total = total;
            Detail = detail;
        }

        public bool Ok
        {
            get { return Total > 0 && Passed == Total; }
        }

        public string Display
        {
            get
            {
                return Name + " — " + Passed + " из " + Total +
                    (Ok ? " (работает)" : " (" + Detail + ")");
            }
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
            MaxCandidates = 6;
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

        public StrategyAdviceReport()
        {
            Results = new List<StrategyProbeResult>();
            RecommendedName = string.Empty;
            RecommendedPath = string.Empty;
            BestName = string.Empty;
            Error = string.Empty;
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
                builder.Append(result.Ok ? "[OK] " : "[FAIL] ")
                    .AppendLine(result.Display);
            }
            if (!string.IsNullOrEmpty(RecommendedName))
            {
                builder.AppendLine("Рекомендация: " + RecommendedName + " (" +
                    RecommendedPassed + " из " + RecommendedTotal + ").");
            }
            else if (!string.IsNullOrEmpty(BestName))
            {
                builder.AppendLine("Лучший результат: " + BestName + " (" +
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
    // the same HTTPS hosts a user cares about. The service is stopped for the
    // duration and restored afterwards, so the check must never run silently.
    public static class StrategyAdvisor
    {
        private static readonly string[] ProbeUrls = new string[]
        {
            "https://discord.com/",
            "https://www.youtube.com/",
            "https://www.google.com/"
        };

        private static readonly string[] PriorityNames = new string[]
        {
            "general",
            "general (FAKE TLS AUTO)",
            "general (ALT9)",
            "general (SIMPLE FAKE)"
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
                    ServiceManager.StopServiceOnly(AppInfo.ServiceName);
                    serviceStopped = true;
                }
                if (!WaitForWinwsExit(5000))
                {
                    report.SetError(
                        "winws.exe уже запущен вне службы. Остановите его и повторите.");
                    return report;
                }

                for (int i = 0; i < candidates.Count; i++)
                {
                    StrategyInfo info = candidates[i];
                    Log(log, "Проверяю " + info.Name + " (" + (i + 1) +
                             " из " + candidates.Count + ")…");
                    StrategyProbeResult result = Probe(
                        payloadRoot, info, game, iface, value.RequestTimeoutMs);
                    report.Results.Add(result);
                    Log(log, result.Display);
                    if (result.Ok)
                    {
                        report.SetRecommended(result);
                        break;
                    }
                }

                if (string.IsNullOrEmpty(report.RecommendedName))
                {
                    StrategyProbeResult best = null;
                    foreach (StrategyProbeResult result in report.Results)
                    {
                        if (best == null || result.Passed > best.Passed)
                        {
                            best = result;
                        }
                    }
                    if (best != null && best.Passed > 0)
                    {
                        report.SetBest(best);
                        Log(log, "Лучший частичный результат: " + best.Display);
                    }
                    else
                    {
                        report.SetError("Ни одна стратегия не прошла проверку.");
                        Log(log, report.Error);
                    }
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
            }
            return report;
        }

        // Order is deterministic so the UI and the installer test the same
        // candidates: current strategy, known good general variants, then the
        // rest in natural .bat order.
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
                return new StrategyProbeResult(
                    info.Name, info.Path, 0, ProbeUrls.Length, Short(ex.Message));
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
                process = Process.Start(start);
                if (process == null)
                {
                    probeResult = new StrategyProbeResult(
                        info.Name, info.Path, 0, ProbeUrls.Length,
                        "winws.exe не запустился");
                }
                else if (process.WaitForExit(1500))
                {
                    probeResult = new StrategyProbeResult(
                        info.Name, info.Path, 0, ProbeUrls.Length,
                        "winws.exe завершился (код " + process.ExitCode + ")");
                }
                else
                {
                    ConnectionTestReport tests = ConnectionTests.RunProbe(
                        ProbeUrls, timeoutMs);
                    string detail = "работает";
                    if (tests.Passed != tests.Results.Count)
                    {
                        ConnectionTestResult firstFailure = null;
                        foreach (ConnectionTestResult result in tests.Results)
                        {
                            if (!result.Success)
                            {
                                firstFailure = result;
                                break;
                            }
                        }
                        detail = firstFailure == null
                            ? "проверка не дала результата"
                            : "не прошло: " + firstFailure.Name + " (" +
                              Short(firstFailure.Detail) + ")";
                    }
                    probeResult = new StrategyProbeResult(
                        info.Name,
                        info.Path,
                        tests.Passed,
                        tests.Results.Count,
                        detail);
                }
            }
            catch (Exception ex)
            {
                probeResult = new StrategyProbeResult(
                    info.Name, info.Path, 0, ProbeUrls.Length, Short(ex.Message));
            }
            finally
            {
                if (!StopProcess(process))
                {
                    probeResult = new StrategyProbeResult(
                        info.Name, info.Path, 0, ProbeUrls.Length,
                        "winws.exe не остановился");
                }
            }
            return probeResult;
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
