using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

namespace ZapretControl
{
    public sealed class TelegramProbeItem
    {
        public string Name { get; private set; }
        public bool Success { get; private set; }
        public string Detail { get; private set; }
        public long ElapsedMs { get; private set; }
        public bool Informational { get; private set; }

        internal TelegramProbeItem(
            string name, bool success, string detail, long elapsedMs,
            bool informational)
        {
            Name = name;
            Success = success;
            Detail = detail ?? string.Empty;
            ElapsedMs = elapsedMs;
            Informational = informational;
        }
    }

    public sealed class TelegramProbeReport
    {
        internal object Sync = new object();

        public List<TelegramProbeItem> Items { get; private set; }
        public int DcPassed { get; internal set; }
        public int DcTotal { get; internal set; }
        public bool WebOk { get; internal set; }
        public bool DohOk { get; internal set; }
        public bool Checked { get; internal set; }

        public TelegramProbeReport()
        {
            Items = new List<TelegramProbeItem>();
        }

        // A working Telegram needs reachable data centres and the web login
        // front. DoH is reported but does not decide the verdict: a blocked
        // DoH endpoint says nothing about the MTProto path.
        public bool Ok
        {
            get
            {
                return Checked && DcTotal > 0 &&
                       DcPassed == DcTotal && WebOk;
            }
        }

        public int Passed
        {
            get
            {
                int count = 0;
                foreach (TelegramProbeItem item in Items)
                {
                    if (!item.Informational && item.Success)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        public int Total
        {
            get
            {
                int count = 0;
                foreach (TelegramProbeItem item in Items)
                {
                    if (!item.Informational)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        public string Summary
        {
            get
            {
                if (!Checked)
                {
                    return "Telegram не проверен";
                }
                return "DC " + DcPassed + "/" + DcTotal +
                       " · web " + (WebOk ? "OK" : "нет");
            }
        }

        public string Detail
        {
            get
            {
                StringBuilder builder = new StringBuilder(Summary);
                foreach (TelegramProbeItem item in Items)
                {
                    if (item.Success)
                    {
                        continue;
                    }
                    builder.Append(" · ").Append(item.Name).Append(": ")
                        .Append(Short(item.Detail));
                }
                return builder.ToString();
            }
        }

        private static string Short(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "нет данных";
            }
            return text.Length > 80 ? text.Substring(0, 80) : text;
        }
    }

    // Informational Telegram reachability check: production data-centre TCP
    // endpoints and the HTTPS front, plus a DoH lookup. It runs inside the
    // unified check but never changes the strategy verdict: several strategies
    // can be perfectly good for Discord/YouTube while Telegram is blocked by
    // IP, which only the local MTProto proxy solves.
    internal static class TelegramProbe
    {
        private const int DcCount = 5;

        internal static TelegramProbeReport Run(
            NetworkGate gate, int timeoutMs)
        {
            TelegramProbeReport report = new TelegramProbeReport();
            report.Checked = true;
            report.DcTotal = DcCount;

            List<Action> actions = new List<Action>();
            for (int dc = 1; dc <= DcCount; dc++)
            {
                int index = dc;
                actions.Add(delegate
                {
                    TelegramProbeItem item = ProbeTcp(
                        "dc" + index,
                        TgProtocol.DcAddress(index),
                        true,
                        gate, timeoutMs);
                    lock (report.Sync)
                    {
                        report.Items.Add(item);
                        if (item.Success)
                        {
                            report.DcPassed++;
                        }
                    }
                });
            }

            actions.Add(delegate
            {
                TelegramProbeItem item = ProbeWeb(gate, timeoutMs);
                lock (report.Sync)
                {
                    report.Items.Add(item);
                    report.WebOk = item.Success;
                }
            });

            actions.Add(delegate
            {
                TelegramProbeItem item = ProbeDoh(timeoutMs);
                lock (report.Sync)
                {
                    report.Items.Add(item);
                    report.DohOk = item.Success;
                }
            });

            ParallelWork.RunAll(actions, delegate(Exception ex)
            {
                lock (report.Sync)
                {
                    report.Items.Add(new TelegramProbeItem(
                        "probe", false,
                        ex.GetType().Name + ": " + ex.Message, 0, true));
                }
            });
            return report;
        }

        private static TelegramProbeItem ProbeTcp(
            string name, string address, bool counted, NetworkGate gate,
            int timeoutMs)
        {
            if (string.IsNullOrEmpty(address))
            {
                return new TelegramProbeItem(
                    name, false, "адрес неизвестен", 0, !counted);
            }
            Stopwatch watch = Stopwatch.StartNew();
            gate.Enter();
            TcpClient client = new TcpClient();
            try
            {
                IAsyncResult result = client.BeginConnect(
                    address, 443, null, null);
                if (!result.AsyncWaitHandle.WaitOne(timeoutMs))
                {
                    return new TelegramProbeItem(
                        name, false,
                        "TCP " + address + ": таймаут (блок IP?)",
                        watch.ElapsedMilliseconds, !counted);
                }
                client.EndConnect(result);
                return new TelegramProbeItem(
                    name, true, "TCP " + address + ": OK",
                    watch.ElapsedMilliseconds, !counted);
            }
            catch (Exception ex)
            {
                return new TelegramProbeItem(
                    name, false,
                    "TCP " + address + ": " + ex.GetType().Name,
                    watch.ElapsedMilliseconds, !counted);
            }
            finally
            {
                gate.Exit();
                try
                {
                    client.Close();
                }
                catch (Exception)
                {
                }
            }
        }

        private static TelegramProbeItem ProbeWeb(
            NetworkGate gate, int timeoutMs)
        {
            Stopwatch watch = Stopwatch.StartNew();
            gate.Enter();
            try
            {
                int status;
                string detail;
                bool ok = TlsHttp.GetStatus(
                    "web.telegram.org", SslProtocols.Tls12,
                    timeoutMs, out status, out detail);
                return new TelegramProbeItem(
                    "web.telegram.org", ok, detail,
                    watch.ElapsedMilliseconds, false);
            }
            finally
            {
                gate.Exit();
            }
        }

        private static TelegramProbeItem ProbeDoh(int timeoutMs)
        {
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                TgDohResolver resolver = new TgDohResolver();
                string[] addresses = resolver.Resolve(
                    "kws2.web.telegram.org", timeoutMs);
                if (addresses.Length == 0)
                {
                    return new TelegramProbeItem(
                        "doh", false, "DoH не ответил",
                        watch.ElapsedMilliseconds, true);
                }
                return new TelegramProbeItem(
                    "doh", true, "DoH: " + addresses[0],
                    watch.ElapsedMilliseconds, true);
            }
            catch (Exception ex)
            {
                return new TelegramProbeItem(
                    "doh", false, ex.GetType().Name,
                    watch.ElapsedMilliseconds, true);
            }
        }
    }
}
