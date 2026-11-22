using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

namespace ZapretControl
{
    internal static class TgClock
    {
        private static readonly Stopwatch Watch = Stopwatch.StartNew();

        internal static long NowMs
        {
            get { return Watch.ElapsedMilliseconds; }
        }
    }

    // Permission to try the direct data-centre IP. Known: a recent probe
    // succeeded. Probe: this connection owns the test. Skip: cooling down, or
    // another connection is already probing.
    internal enum TgDirectTicket
    {
        Skip,
        Known,
        Probe
    }

    // Cloudflare-fronted domain pool used to reach Telegram data centres when
    // the direct IP is blocked. The list is decoded from Flowseal/tg-ws-proxy
    // (MIT) via Zapret2UI (MIT); the original stores it obfuscated to keep
    // static scanners from enumerating it.
    internal sealed class TgFronts
    {
        internal const string DirectKey = "direct";

        private const int DirectGoodMs = 60000;

        private static readonly string[] Domains = new string[]
        {
            "pclead.co.uk",
            "offshor.co.uk",
            "cakeisalie.co.uk",
            "noskomnadzor.co.uk",
            "lovetrue.co.uk",
            "sorokdva.co.uk",
            "pyatdesyatdva.co.uk",
            "kartoshka.co.uk",
            "sorokodin.co.uk",
            "pyatdesyatodin.co.uk",
            "notelega.co.uk",
            "ebally.co.uk",
            "nebally.co.uk",
            "havegreatday.co.uk",
            "pomogite.co.uk",
            "fixtelega.co.uk",
            "sadnews.co.uk",
            "onedaychamp.co.uk",
            "stopblocking.co.uk",
            "nothingthere.co.uk"
        };

        private readonly ConcurrentDictionary<string, string> _sticky =
            new ConcurrentDictionary<string, string>();
        private readonly ConcurrentDictionary<string, long> _bad =
            new ConcurrentDictionary<string, long>();
        private readonly ConcurrentDictionary<int, long> _directGoodUntil =
            new ConcurrentDictionary<int, long>();
        private readonly ConcurrentDictionary<int, byte> _directProbe =
            new ConcurrentDictionary<int, byte>();
        private int _mediaTurn;

        internal static string[] AllBaseDomains
        {
            get { return (string[])Domains.Clone(); }
        }

        internal static string Lane(bool isMedia)
        {
            return isMedia ? "m" : "a";
        }

        // Fronts to try for this data centre, best first. Chat keeps a sticky
        // front per DC; media starts one front further on every call so
        // parallel transfers spread over several Cloudflare workers. Recently
        // failed fronts are skipped per lane, but a total blackout falls back
        // to the whole list: cooldowns are a preference, never a hard ban.
        internal IEnumerable<string> DomainsForDc(int dc, bool isMedia)
        {
            string lane = Lane(isMedia);
            List<string> fresh = new List<string>();
            for (int i = 0; i < Domains.Length; i++)
            {
                if (!IsBad(dc, lane, Domains[i]))
                {
                    fresh.Add(Domains[i]);
                }
            }

            if (fresh.Count == 0)
            {
                List<string> all = new List<string>(Domains);
                Shuffle(all);
                for (int i = 0; i < all.Count; i++)
                {
                    yield return all[i];
                }
                yield break;
            }

            if (isMedia)
            {
                int start = (int)((uint)System.Threading.Interlocked.Increment(
                    ref _mediaTurn) % (uint)fresh.Count);
                for (int i = 0; i < fresh.Count; i++)
                {
                    yield return fresh[(start + i) % fresh.Count];
                }
                yield break;
            }

            string key = dc + "|" + lane;
            string sticky;
            if (!_sticky.TryGetValue(key, out sticky) || sticky == null)
            {
                sticky = Domains[TgProtocol.RandomInt(Domains.Length)];
                _sticky[key] = sticky;
            }
            bool hasSticky = false;
            for (int i = 0; i < fresh.Count; i++)
            {
                if (fresh[i] == sticky)
                {
                    hasSticky = true;
                    break;
                }
            }
            if (hasSticky)
            {
                yield return sticky;
            }
            List<string> rest = new List<string>();
            for (int i = 0; i < fresh.Count; i++)
            {
                if (fresh[i] != sticky)
                {
                    rest.Add(fresh[i]);
                }
            }
            Shuffle(rest);
            for (int i = 0; i < rest.Count; i++)
            {
                yield return rest[i];
            }
        }

        internal bool IsCooling(int dc, string key, bool isMedia)
        {
            return IsBad(dc, Lane(isMedia), key);
        }

        internal void MarkBad(
            int dc, string baseDomain, int cooldownMs, bool isMedia)
        {
            string key = dc + "|" + Lane(isMedia) + "|" + baseDomain;
            _bad[key] = TgClock.NowMs + cooldownMs;
        }

        private bool IsBad(int dc, string lane, string baseDomain)
        {
            string key = dc + "|" + lane + "|" + baseDomain;
            long expires;
            return _bad.TryGetValue(key, out expires) &&
                   expires > TgClock.NowMs;
        }

        internal TgDirectTicket TryBeginDirect(int dc)
        {
            if (IsCooling(dc, DirectKey, false))
            {
                return TgDirectTicket.Skip;
            }
            long goodUntil;
            if (_directGoodUntil.TryGetValue(dc, out goodUntil) &&
                goodUntil > TgClock.NowMs)
            {
                return TgDirectTicket.Known;
            }
            return _directProbe.TryAdd(dc, 0)
                ? TgDirectTicket.Probe
                : TgDirectTicket.Skip;
        }

        internal void EndDirect(int dc, TgDirectTicket ticket, bool ok)
        {
            if (ok)
            {
                _directGoodUntil[dc] = TgClock.NowMs + DirectGoodMs;
            }
            else
            {
                long ignored;
                _directGoodUntil.TryRemove(dc, out ignored);
            }
            if (ticket == TgDirectTicket.Probe)
            {
                byte removed;
                _directProbe.TryRemove(dc, out removed);
            }
        }

        private static void Shuffle(List<string> values)
        {
            for (int i = values.Count - 1; i > 0; i--)
            {
                int j = TgProtocol.RandomInt(i + 1);
                string temp = values[i];
                values[i] = values[j];
                values[j] = temp;
            }
        }
    }
}
