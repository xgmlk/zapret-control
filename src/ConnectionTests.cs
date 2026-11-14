using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

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
        public DateTime StartedAt { get; private set; }

        public ConnectionTestReport(string mode)
        {
            Mode = mode;
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
                    .Append(result.Name).Append(" — ").Append(result.Detail)
                    .Append(" (").Append(result.ElapsedMs).AppendLine(" ms)");
            }
            builder.AppendLine("Итог: " + Passed + " из " + Results.Count + " успешно.");
            return builder.ToString();
        }
    }

    // Native replacement for "Run Tests": connectivity and DPI checks without
    // invoking utils\test zapret.ps1.
    public static class ConnectionTests
    {
        private static readonly string[] StandardHosts = new string[]
        {
            "discord.com", "www.youtube.com", "www.google.com",
            "cloudflare.com", "github.com"
        };

        private static readonly string[] DpiUrls = new string[]
        {
            "https://discord.com/",
            "https://www.youtube.com/",
            "https://www.google.com/",
            "https://www.instagram.com/",
            "https://x.com/"
        };

        public static ConnectionTestReport Run(string mode)
        {
            string normalized = (mode ?? string.Empty).Trim().ToLowerInvariant();
            ConnectionTestReport report = new ConnectionTestReport(
                normalized == "dpi" ? "DPI" : "стандартные");
            if (normalized == "dpi")
            {
                RunDpi(report);
            }
            else
            {
                RunStandard(report);
            }
            return report;
        }

        private static void RunStandard(ConnectionTestReport report)
        {
            foreach (string host in StandardHosts)
            {
                Stopwatch watch = Stopwatch.StartNew();
                try
                {
                    IPAddress[] addresses = Dns.GetHostAddresses(host);
                    if (addresses.Length == 0)
                    {
                        watch.Stop();
                        report.Results.Add(new ConnectionTestResult(
                            host, false, "DNS не вернул адреса", watch.ElapsedMilliseconds));
                        continue;
                    }
                    bool connected = false;
                    foreach (IPAddress address in addresses)
                    {
                        if (TryConnect(address, 443, 5000))
                        {
                            connected = true;
                            break;
                        }
                    }
                    watch.Stop();
                    report.Results.Add(new ConnectionTestResult(
                        host,
                        connected,
                        connected
                            ? "DNS и TCP 443 работают (" + addresses[0] + ")"
                            : "TCP 443 не отвечает (" + addresses[0] + ")",
                        watch.ElapsedMilliseconds));
                }
                catch (Exception ex)
                {
                    watch.Stop();
                    report.Results.Add(new ConnectionTestResult(
                        host, false, ex.GetType().Name + ": " + ex.Message,
                        watch.ElapsedMilliseconds));
                }
            }
        }

        private static void RunDpi(ConnectionTestReport report)
        {
            try
            {
                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            }
            catch (Exception)
            {
            }
            ZapretStatus status = ZapretService.GetStatus();
            foreach (string url in DpiUrls)
            {
                Stopwatch watch = Stopwatch.StartNew();
                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                    request.Method = "GET";
                    request.UserAgent = "ZapretControl/" + AppInfo.Version;
                    request.Proxy = null;
                    request.Timeout = 15000;
                    request.ReadWriteTimeout = 15000;
                    request.KeepAlive = false;
                    request.AllowAutoRedirect = true;
                    request.MaximumAutomaticRedirections = 5;
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        int code = (int)response.StatusCode;
                        watch.Stop();
                        report.Results.Add(new ConnectionTestResult(
                            url,
                            code >= 200 && code < 500,
                            "HTTP " + code + ", bypass " + status.State,
                            watch.ElapsedMilliseconds));
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
                        report.Results.Add(new ConnectionTestResult(
                            url,
                            code >= 200 && code < 500,
                            "HTTP " + code + " (WebException), bypass " + status.State,
                            watch.ElapsedMilliseconds));
                    }
                    else
                    {
                        report.Results.Add(new ConnectionTestResult(
                            url, false, "Ошибка: " + ex.Status,
                            watch.ElapsedMilliseconds));
                    }
                }
                catch (Exception ex)
                {
                    watch.Stop();
                    report.Results.Add(new ConnectionTestResult(
                        url, false, ex.GetType().Name + ": " + ex.Message,
                        watch.ElapsedMilliseconds));
                }
            }
        }

        private static bool TryConnect(IPAddress address, int port, int timeoutMs)
        {
            try
            {
                using (TcpClient client = new TcpClient(address.AddressFamily))
                {
                    Task task = client.ConnectAsync(address, port);
                    if (!task.Wait(timeoutMs))
                    {
                        return false;
                    }
                    return client.Connected;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
