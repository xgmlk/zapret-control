using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ZapretControl
{
    internal sealed class UnifiedCheckOptions
    {
        public int RequestTimeoutMs { get; set; }
        public int FreezeTimeoutSeconds { get; set; }
        public int GateLimit { get; set; }

        internal UnifiedCheckOptions()
        {
            RequestTimeoutMs = 4000;
            FreezeTimeoutSeconds = 5;
            GateLimit = 16;
        }
    }

    // One full strategy check. Standard hosts, DPI freeze and the YouTube
    // pipeline start together; the YouTube thread continues with the player and
    // six media fragments while the other two stages are still running.
    internal static class UnifiedCheck
    {
        internal static StrategyProbeResult Run(
            string name,
            string path,
            List<FreezeTarget> suite,
            bool suiteOffline,
            string curlPath,
            string payloadFile,
            UnifiedCheckOptions options,
            Action<string> log)
        {
            UnifiedCheckOptions value = options ?? new UnifiedCheckOptions();
            Stopwatch watch = Stopwatch.StartNew();
            StrategyProbeResult result = new StrategyProbeResult(name, path);
            StandardProbeReport standard = null;
            FreezeReport freeze = null;
            YouTubeProbeResult youtube = null;

            using (NetworkGate gate = new NetworkGate(value.GateLimit))
            {
                Action[] actions = new Action[]
                {
                    delegate()
                    {
                        youtube = YouTubeProbe.Run(
                            gate, value.RequestTimeoutMs, 3);
                    },
                    delegate()
                    {
                        standard = StandardProbe.Run(
                            gate, value.RequestTimeoutMs);
                    },
                    delegate()
                    {
                        freeze = FreezeProbe.Run(
                            suite, curlPath, payloadFile, gate,
                            value.FreezeTimeoutSeconds, suiteOffline);
                    }
                };
                ParallelWork.RunAll(actions, delegate(Exception ex)
                {
                    Log(log, "Проверка прервана: " + ex.GetType().Name + ": " + ex.Message);
                });
            }

            if (standard != null)
            {
                result.DiscordOk = standard.DiscordOk;
                result.DiscordPassed = standard.Passed("Discord");
                result.DiscordTotal = standard.Total("Discord");
                result.YouTubeHostsOk = standard.YouTubeOk;
                result.YouTubeHostsPassed = standard.Passed("YouTube");
                result.YouTubeHostsTotal = standard.Total("YouTube");
                result.GoogleOk = standard.GoogleOk;
                result.GooglePassed = standard.Passed("Google");
                result.GoogleTotal = standard.Total("Google");
                result.CloudflareOk = standard.CloudflareOk;
                result.CloudflarePassed = standard.Passed("Cloudflare");
                result.CloudflareTotal = standard.Total("Cloudflare");
                result.Tls13EnvironmentUnsupported = standard.Tls13Unsupported;
                string failures = standard.FailuresText(6);
                result.HostsDetail = standard.Summary +
                    (string.IsNullOrEmpty(failures)
                        ? string.Empty
                        : " · сбои: " + failures);
            }

            if (youtube != null)
            {
                result.YouTubePageOk = youtube.PageOk;
                result.YouTubePlayerOk = youtube.PlayerOk;
                result.YouTubeVideoOk = youtube.CdnOk;
                result.VideoSuccesses = youtube.Successes;
                result.VideoAttempts = youtube.Attempts;
                result.VideoDetail = string.IsNullOrEmpty(youtube.CdnDetail)
                    ? youtube.Detail
                    : youtube.Detail + " · " + youtube.CdnDetail;
                result.Tls12Attempted = youtube.Tls12Attempted;
                result.Tls12Ok = youtube.Tls12Ok;
                result.Tls13Attempted = youtube.Tls13Attempted;
                result.Tls13Ok = youtube.Tls13Ok;
            }
            else
            {
                result.VideoDetail = "YouTube не проверен";
            }

            if (freeze != null)
            {
                result.FreezeAvailable = freeze.CurlAvailable;
                result.FreezeChecked = freeze.Checked;
                result.FreezeOk = freeze.Ok;
                result.FreezeBlocked = freeze.BlockedCount;
                result.FreezeTargets = freeze.Targets.Count;
                result.FreezeOkCount = freeze.OkCount;
                result.FreezeErrors = freeze.FailCount;
                result.FreezeDetail = freeze.Detail;
            }
            else
            {
                result.FreezeDetail = "DPI-заморозка не проверена";
            }

            result.Detail = BuildDetail(result, standard, youtube, freeze);
            watch.Stop();
            result.ElapsedMs = watch.ElapsedMilliseconds;
            return result;
        }

        private static string BuildDetail(
            StrategyProbeResult result,
            StandardProbeReport standard,
            YouTubeProbeResult youtube,
            FreezeReport freeze)
        {
            List<string> failures = new List<string>();
            if (standard == null)
            {
                failures.Add("стандартные хосты не проверены");
            }
            else
            {
                if (!result.DiscordOk)
                {
                    failures.Add("Discord " + result.DiscordPassed +
                                 "/" + result.DiscordTotal);
                }
                if (!result.YouTubeHostsOk)
                {
                    failures.Add("YouTube хосты " + result.YouTubeHostsPassed +
                                 "/" + result.YouTubeHostsTotal);
                }
                if (!result.GoogleOk)
                {
                    failures.Add("Google " + result.GooglePassed +
                                 "/" + result.GoogleTotal);
                }
            }
            if (youtube == null)
            {
                failures.Add("YouTube не проверен");
            }
            else
            {
                if (!result.YouTubePageOk)
                {
                    failures.Add("YouTube страница: " + Short(youtube.PageDetail));
                }
                else if (!result.YouTubePlayerOk)
                {
                    failures.Add("YouTube плеер: " + Short(youtube.PlayerDetail));
                }
                else if (!result.YouTubeVideoOk)
                {
                    failures.Add("YouTube видео: " + Short(youtube.CdnDetail));
                }
            }
            if (freeze == null)
            {
                failures.Add("DPI: этап не выполнен");
            }
            else if (!result.FreezeAvailable)
            {
                failures.Add("DPI: curl.exe не найден");
            }
            else if (!result.FreezeChecked)
            {
                failures.Add("DPI: этап не выполнен");
            }
            else if (!result.FreezeOk)
            {
                failures.Add("DPI: блокировка " + result.FreezeBlocked +
                             " из " + result.FreezeTargets);
            }
            if (failures.Count == 0)
            {
                StringBuilder notes = new StringBuilder();
                if (result.CloudflareTotal > 0 && !result.CloudflareOk)
                {
                    notes.Append(" · Cloudflare ").Append(result.CloudflarePassed)
                        .Append("/").Append(result.CloudflareTotal);
                }
                if (standard != null && standard.Errors.Count > 0)
                {
                    notes.Append(" · сбои проверок: ");
                    int count = standard.Errors.Count > 2 ? 2 : standard.Errors.Count;
                    for (int i = 0; i < count; i++)
                    {
                        if (i > 0)
                        {
                            notes.Append("; ");
                        }
                        notes.Append(Short(standard.Errors[i]));
                    }
                }
                return "все проверки пройдены" + notes;
            }
            return "не прошло: " + Join(failures);
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

        private static string Short(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "нет данных";
            }
            return text.Length > 160 ? text.Substring(0, 160) : text;
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
