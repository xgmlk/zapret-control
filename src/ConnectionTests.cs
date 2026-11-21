using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ZapretControl
{
    public sealed class ConnectionTestResult
    {
        public string Name { get; private set; }
        public bool Success { get; private set; }
        public string Detail { get; private set; }
        public long ElapsedMs { get; private set; }

        public ConnectionTestResult(string name, bool success, string detail, long elapsedMs)
        {
            Name = name;
            Success = success;
            Detail = detail;
            ElapsedMs = elapsedMs;
        }
    }

    public sealed class ConnectionTestReport
    {
        public List<ConnectionTestResult> Results { get; private set; }
        public string Mode { get; private set; }
        public string Notes { get; set; }
        public StrategyProbeResult StrategyResult { get; internal set; }
        public DateTime StartedAt { get; private set; }

        public ConnectionTestReport(string mode)
        {
            Mode = mode;
            Notes = string.Empty;
            Results = new List<ConnectionTestResult>();
            StartedAt = DateTime.Now;
        }

        public int Passed
        {
            get
            {
                int count = 0;
                foreach (ConnectionTestResult result in Results)
                {
                    if (result.Success)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        public string ToText()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Zapret Control — тесты (" + Mode + "), " +
                               StartedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            foreach (ConnectionTestResult result in Results)
            {
                builder.Append(result.Success ? "[OK] " : "[FAIL] ")
                    .Append(result.Name).Append(" — ").Append(result.Detail);
                if (result.ElapsedMs > 0)
                {
                    builder.Append(" (").Append(result.ElapsedMs).Append(" ms)");
                }
                builder.AppendLine();
            }
            if (!string.IsNullOrEmpty(Notes))
            {
                builder.AppendLine(Notes);
            }
            builder.AppendLine("Итог: " + Passed + " из " + Results.Count + " успешно.");
            return builder.ToString();
        }
    }

    // Native replacement for "Run Tests": one full check against the currently
    // running strategy. The legacy "standard" and "dpi" names map here because
    // there is now a single unified mode.
    public static class ConnectionTests
    {
        public static ConnectionTestReport Run(string mode)
        {
            return RunSingle();
        }

        public static ConnectionTestReport RunSingle()
        {
            ConnectionTestReport report = new ConnectionTestReport("единый");
            string root = AppPaths.ResolvePayloadRoot();
            string strategy = ServiceManager.GetCurrentStrategyName();
            string curl = FreezeProbe.FindCurl();
            bool suiteOffline = false;
            List<FreezeTarget> suite = new List<FreezeTarget>();
            if (curl != null)
            {
                suite = FreezeProbe.LoadSuite(4000, out suiteOffline);
            }
            string payloadFile = null;
            try
            {
                if (curl != null)
                {
                    try
                    {
                        payloadFile = FreezeProbe.CreatePayloadFile();
                    }
                    catch (Exception)
                    {
                        // FreezeProbe will retry and report the real reason.
                        payloadFile = null;
                    }
                }
                UnifiedCheckOptions options = new UnifiedCheckOptions();
                StrategyProbeResult result = UnifiedCheck.Run(
                    string.IsNullOrEmpty(strategy) ? "текущая стратегия" : strategy,
                    string.Empty,
                    suite,
                    suiteOffline,
                    curl,
                    payloadFile,
                    options,
                    null);
                report.StrategyResult = result;
                report.Results.Add(new ConnectionTestResult(
                    "Discord",
                    result.DiscordOk,
                    StageDetail(
                        result.DiscordPassed + " из " + result.DiscordTotal +
                        " проверок (HTTP/1.1, TLS 1.2, TLS 1.3)",
                        result.DiscordOk,
                        result.HostsDetail),
                    0));
                report.Results.Add(new ConnectionTestResult(
                    "YouTube хосты и страница",
                    result.YouTubeStageOk,
                    StageDetail(
                        "хосты " + result.YouTubeHostsPassed + " из " +
                        result.YouTubeHostsTotal + ", страница " +
                        (result.YouTubePageOk ? "OK" : "нет"),
                        result.YouTubeStageOk,
                        result.HostsDetail),
                    0));
                report.Results.Add(new ConnectionTestResult(
                    "YouTube плеер",
                    result.YouTubePlayerOk,
                    result.VideoDetail,
                    0));
                report.Results.Add(new ConnectionTestResult(
                    "YouTube видео",
                    result.YouTubeVideoOk,
                    result.VideoDetail,
                    0));
                report.Results.Add(new ConnectionTestResult(
                    "Google",
                    result.GoogleOk,
                    StageDetail(
                        result.GooglePassed + " из " + result.GoogleTotal +
                        " проверок (HTTP/1.1, TLS 1.2, TLS 1.3)",
                        result.GoogleOk,
                        result.HostsDetail),
                    0));
                report.Results.Add(new ConnectionTestResult(
                    "DPI-заморозка",
                    result.FreezeOk,
                    result.FreezeDetail,
                    0));
                StringBuilder notes = new StringBuilder();
                notes.Append("IPSet: " + IpsetManager.GetState(root) +
                             " · TLS видео: 1.2 " + result.Tls12Display +
                             ", 1.3 " + result.Tls13Display);
                notes.Append(" · Cloudflare " + result.CloudflarePassed +
                             " из " + result.CloudflareTotal);
                if (suiteOffline)
                {
                    notes.Append(" · список suite оффлайн");
                }
                if (curl == null)
                {
                    notes.Append(" · curl.exe не найден");
                }
                report.Notes = notes.ToString();
                return report;
            }
            finally
            {
                FreezeProbe.TryDelete(payloadFile);
            }
        }

        private static string StageDetail(
            string text, bool ok, string hostsDetail)
        {
            if (ok || string.IsNullOrEmpty(hostsDetail))
            {
                return text;
            }
            return text + " · " + hostsDetail;
        }
    }
}
