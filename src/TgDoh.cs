using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace ZapretControl
{
    // Minimal DNS-over-HTTPS resolver. Endpoints are addressed by IP literal
    // (their certificates carry the IP as a SAN), so it keeps working when the
    // provider poisons or blocks ordinary DNS. Results are cached for five
    // minutes; a fully unreachable DoH is skipped for 30 seconds so failures
    // stay fast and the caller can fall back to the system resolver.
    internal sealed class TgDohResolver
    {
        private const int CacheMs = 300000;
        private const int DownMs = 30000;

        private sealed class Entry
        {
            internal string[] Addresses;
            internal long ExpiresMs;
        }

        private readonly ConcurrentDictionary<string, Entry> _cache =
            new ConcurrentDictionary<string, Entry>();
        private long _downUntil;

        internal string[] Resolve(string host, int timeoutMs)
        {
            if (string.IsNullOrEmpty(host))
            {
                return new string[0];
            }
            long now = TgClock.NowMs;
            Entry cached;
            if (_cache.TryGetValue(host, out cached) && cached.ExpiresMs > now)
            {
                return cached.Addresses;
            }
            if (System.Threading.Volatile.Read(ref _downUntil) > now)
            {
                return new string[0];
            }

            bool reachable = false;
            string[] endpoints = DataStore.DohEndpoints;
            for (int i = 0; i < endpoints.Length; i++)
            {
                string url = endpoints[i] + "?name=" +
                    Uri.EscapeDataString(host) + "&type=A";
                int status;
                string body;
                string detail;
                if (!TlsHttp.GetText(
                        url, timeoutMs, out status, out body, out detail))
                {
                    continue;
                }
                reachable = true;
                List<string> addresses = ParseAnswer(body);
                if (addresses.Count > 0)
                {
                    Entry entry = new Entry();
                    entry.Addresses = addresses.ToArray();
                    entry.ExpiresMs = TgClock.NowMs + CacheMs;
                    _cache[host] = entry;
                    return entry.Addresses;
                }
            }

            if (!reachable)
            {
                System.Threading.Volatile.Write(
                    ref _downUntil, TgClock.NowMs + DownMs);
            }
            return new string[0];
        }

        private static List<string> ParseAnswer(string json)
        {
            List<string> addresses = new List<string>();
            if (string.IsNullOrEmpty(json))
            {
                return addresses;
            }
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                Dictionary<string, object> root =
                    serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null)
                {
                    return addresses;
                }
                object answerValue;
                if (!root.TryGetValue("Answer", out answerValue))
                {
                    return addresses;
                }
                object[] answers = answerValue as object[];
                if (answers == null)
                {
                    return addresses;
                }
                for (int i = 0; i < answers.Length; i++)
                {
                    Dictionary<string, object> record =
                        answers[i] as Dictionary<string, object>;
                    if (record == null)
                    {
                        continue;
                    }
                    object typeValue;
                    object dataValue;
                    if (!record.TryGetValue("type", out typeValue) ||
                        !record.TryGetValue("data", out dataValue))
                    {
                        continue;
                    }
                    int type;
                    try
                    {
                        type = Convert.ToInt32(typeValue);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    string data = dataValue as string;
                    if (type == 1 && !string.IsNullOrEmpty(data))
                    {
                        addresses.Add(data);
                    }
                }
            }
            catch (Exception)
            {
                addresses.Clear();
            }
            return addresses;
        }
    }
}
