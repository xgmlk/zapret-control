using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Security.Authentication;
using System.Text;

namespace ZapretControl
{
    public sealed class HostProbeResult
    {
        public string Host { get; private set; }
        public string Group { get; private set; }
        public string Variant { get; private set; }
        public bool Success { get; private set; }
        public string Detail { get; private set; }
        public long ElapsedMs { get; private set; }

        public HostProbeResult(
            string host,
            string group,
            string variant,
            bool success,
            string detail,
            long elapsedMs)
        {
            Host = host ?? string.Empty;
            Group = group ?? string.Empty;
            Variant = variant ?? string.Empty;
            Success = success;
            Detail = detail ?? string.Empty;
            ElapsedMs = elapsedMs;
        }
    }

    public sealed class StandardProbeReport
    {
        internal object Sync = new object();

        public List<HostProbeResult> Results { get; private set; }
        public List<string> Errors { get; private set; }
        // Windows 10 without Schannel TLS 1.3 support fails every pinned 1.3
        // probe. That is an environment limit, not a strategy failure.
        public bool Tls13Unsupported { get; internal set; }

        public StandardProbeReport()
        {
            Results = new List<HostProbeResult>();
            Errors = new List<string>();
        }

        public int Total(string group)
        {
            int count = 0;
            foreach (HostProbeResult result in Results)
            {
                if (result.Group.Equals(group, StringComparison.Ordinal))
                {
                    count++;
                }
            }
            return count;
        }

        public int Passed(string group)
        {
            int count = 0;
            foreach (HostProbeResult result in Results)
            {
                if (!result.Group.Equals(group, StringComparison.Ordinal))
                {
                    continue;
                }
                if (result.Success ||
                    (Tls13Unsupported &&
                     result.Variant.Equals("TLS1.3", StringComparison.Ordinal)))
                {
                    count++;
                }
            }
            return count;
        }

        public bool GroupOk(string group)
        {
            int expected = StandardProbe.ExpectedTotal(group);
            return expected > 0 &&
                   Total(group) == expected &&
                   Passed(group) == expected;
        }

        public bool DiscordOk
        {
            get { return GroupOk("Discord"); }
        }

        public bool YouTubeOk
        {
            get { return GroupOk("YouTube"); }
        }

        public bool GoogleOk
        {
            get { return GroupOk("Google"); }
        }

        public bool CloudflareOk
        {
            get { return GroupOk("Cloudflare"); }
        }

        public string Summary
        {
            get
            {
                return "Discord " + Passed("Discord") + "/" + Total("Discord") +
                       " · YouTube " + Passed("YouTube") + "/" + Total("YouTube") +
                       " · Google " + Passed("Google") + "/" + Total("Google") +
                       " · Cloudflare " + Passed("Cloudflare") + "/" + Total("Cloudflare") +
                       (Tls13Unsupported ? " · TLS1.3 н/д" : string.Empty);
            }
        }

        public string FailuresText(int limit)
        {
            StringBuilder builder = new StringBuilder();
            int count = 0;
            foreach (HostProbeResult result in Results)
            {
                if (result.Success)
                {
                    continue;
                }
                if (Tls13Unsupported &&
                    result.Variant.Equals("TLS1.3", StringComparison.Ordinal))
                {
                    continue;
                }
                if (count >= limit)
                {
                    builder.Append("; …");
                    break;
                }
                if (builder.Length > 0)
                {
                    builder.Append("; ");
                }
                builder.Append(result.Host).Append(' ').Append(result.Variant)
                    .Append(": ").Append(result.Detail);
                count++;
            }
            return builder.ToString();
        }

        internal void Sort()
        {
            Results.Sort(Compare);
        }

        private static int Compare(HostProbeResult left, HostProbeResult right)
        {
            int group = string.Compare(
                left.Group, right.Group, StringComparison.Ordinal);
            if (group != 0)
            {
                return group;
            }
            int host = string.Compare(
                left.Host, right.Host, StringComparison.OrdinalIgnoreCase);
            if (host != 0)
            {
                return host;
            }
            return string.Compare(
                left.Variant, right.Variant, StringComparison.Ordinal);
        }
    }

    // Standard connectivity stage: every host is checked over HTTPS with
    // HTTP/1.1, TLS 1.2 and TLS 1.3. Any HTTP response proves that the path
    // works; a timeout or TLS failure does not.
    internal static class StandardProbe
    {
        private static readonly string[] Hosts = new string[]
        {
            "discord.com",
            "gateway.discord.gg",
            "cdn.discordapp.com",
            "updates.discord.com",
            "www.youtube.com",
            "youtu.be",
            "i.ytimg.com",
            "redirector.googlevideo.com",
            "www.google.com",
            "www.gstatic.com",
            "www.cloudflare.com",
            "cdnjs.cloudflare.com"
        };

        private static readonly string[] Groups = new string[]
        {
            "Discord",
            "Discord",
            "Discord",
            "Discord",
            "YouTube",
            "YouTube",
            "YouTube",
            "YouTube",
            "Google",
            "Google",
            "Cloudflare",
            "Cloudflare"
        };

        internal static int ExpectedTotal(string group)
        {
            int hosts = 0;
            for (int i = 0; i < Groups.Length; i++)
            {
                if (Groups[i].Equals(group, StringComparison.Ordinal))
                {
                    hosts++;
                }
            }
            return hosts * 3;
        }

        internal static StandardProbeReport Run(NetworkGate gate, int timeoutMs)
        {
            // HttpWebRequest uses the process-wide SecurityProtocol. Enable
            // TLS 1.2 before any parallel request starts; the pinned TLS 1.2
            // and 1.3 variants use TlsHttp and do not depend on this value.
            YouTubeHttp.EnableTls();
            StandardProbeReport report = new StandardProbeReport();
            Action[] actions = new Action[Hosts.Length];
            for (int i = 0; i < Hosts.Length; i++)
            {
                string host = Hosts[i];
                string group = Groups[i];
                actions[i] = delegate()
                {
                    List<HostProbeResult> values = new List<HostProbeResult>();
                    values.Add(ProbeHttp11(host, group, gate, timeoutMs));
                    values.Add(ProbePinned(
                        host, group, "TLS1.2", TlsHttp.Tls12, gate, timeoutMs));
                    values.Add(ProbePinned(
                        host, group, "TLS1.3", TlsHttp.Tls13, gate, timeoutMs));
                    lock (report.Sync)
                    {
                        report.Results.AddRange(values);
                    }
                };
            }
            ParallelWork.RunAll(actions, delegate(Exception ex)
            {
                lock (report.Sync)
                {
                    report.Errors.Add(ex.GetType().Name + ": " + ex.Message);
                }
            });
            int tls13Total = 0;
            int tls13Passed = 0;
            foreach (HostProbeResult result in report.Results)
            {
                if (result.Variant.Equals("TLS1.3", StringComparison.Ordinal))
                {
                    tls13Total++;
                    if (result.Success)
                    {
                        tls13Passed++;
                    }
                }
            }
            report.Tls13Unsupported = tls13Total > 0 && tls13Passed == 0;
            report.Sort();
            return report;
        }

        private static HostProbeResult ProbeHttp11(
            string host, string group, NetworkGate gate, int timeoutMs)
        {
            Stopwatch watch = Stopwatch.StartNew();
            gate.Enter();
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(
                    "https://" + host + "/");
                request.Method = "GET";
                request.UserAgent = "ZapretControl/" + AppInfo.Version;
                request.Proxy = null;
                request.Timeout = timeoutMs;
                request.ReadWriteTimeout = timeoutMs;
                request.KeepAlive = false;
                request.AllowAutoRedirect = true;
                request.MaximumAutomaticRedirections = 5;
                using (HttpWebResponse response =
                    (HttpWebResponse)request.GetResponse())
                {
                    int code = (int)response.StatusCode;
                    watch.Stop();
                    return new HostProbeResult(
                        host, group, "HTTP/1.1", true,
                        "HTTP " + code, watch.ElapsedMilliseconds);
                }
            }
            catch (WebException ex)
            {
                watch.Stop();
                HttpWebResponse response = ex.Response as HttpWebResponse;
                if (response != null)
                {
                    int code = (int)response.StatusCode;
                    response.Dispose();
                    return new HostProbeResult(
                        host, group, "HTTP/1.1", true,
                        "HTTP " + code + " (" + ex.Status + ")",
                        watch.ElapsedMilliseconds);
                }
                return new HostProbeResult(
                    host, group, "HTTP/1.1", false,
                    "WebException: " + ex.Status,
                    watch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                watch.Stop();
                return new HostProbeResult(
                    host, group, "HTTP/1.1", false,
                    ex.GetType().Name + ": " + ex.Message,
                    watch.ElapsedMilliseconds);
            }
            finally
            {
                gate.Exit();
            }
        }

        private static HostProbeResult ProbePinned(
            string host,
            string group,
            string variant,
            SslProtocols protocols,
            NetworkGate gate,
            int timeoutMs)
        {
            Stopwatch watch = Stopwatch.StartNew();
            gate.Enter();
            try
            {
                int code;
                string detail;
                bool ok = TlsHttp.GetStatus(
                    host, protocols, timeoutMs, out code, out detail);
                watch.Stop();
                return new HostProbeResult(
                    host, group, variant, ok, detail, watch.ElapsedMilliseconds);
            }
            finally
            {
                gate.Exit();
            }
        }
    }
}
