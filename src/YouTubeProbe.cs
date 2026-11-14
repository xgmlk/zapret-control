using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Web.Script.Serialization;

namespace ZapretControl
{
    public sealed class YouTubeProbeResult
    {
        public string VideoId { get; private set; }
        public bool PageOk { get; private set; }
        public bool PlayerOk { get; private set; }
        public bool CdnOk { get; private set; }
        public int Attempts { get; private set; }
        public int Successes { get; private set; }
        public long Bytes { get; private set; }
        public long ElapsedMs { get; private set; }
        public string PageDetail { get; private set; }
        public string PlayerDetail { get; private set; }
        public string CdnDetail { get; private set; }

        public YouTubeProbeResult(string videoId)
        {
            VideoId = videoId ?? string.Empty;
            PageDetail = string.Empty;
            PlayerDetail = string.Empty;
            CdnDetail = string.Empty;
            Attempts = 0;
        }

        public string Detail
        {
            get
            {
                if (!PageOk)
                {
                    return "страница: " + Text(PageDetail);
                }
                if (!PlayerOk)
                {
                    return "плеер: " + Text(PlayerDetail);
                }
                if (!CdnOk)
                {
                    return "CDN видео: " + Text(CdnDetail);
                }
                return "видео " + Successes + " из " + Attempts + ", " +
                       (Bytes / 1024) + " КБ";
            }
        }

        internal void SetPage(bool ok, string detail, long elapsedMs)
        {
            PageOk = ok;
            PageDetail = detail ?? string.Empty;
            ElapsedMs = elapsedMs;
        }

        internal void SetPlayer(bool ok, string detail)
        {
            PlayerOk = ok;
            PlayerDetail = detail ?? string.Empty;
        }

        internal void SetCdn(
            bool ok, int attempts, int successes, long bytes,
            string detail, long elapsedMs)
        {
            CdnOk = ok;
            Attempts = attempts;
            Successes = successes;
            Bytes = bytes;
            CdnDetail = detail ?? string.Empty;
            ElapsedMs = elapsedMs;
        }

        private static string Text(string value)
        {
            return string.IsNullOrEmpty(value) ? "нет данных" : value;
        }
    }

    // Deep YouTube check: watch page, player response and a real media fragment
    // from googlevideo.com. The fragment is downloaded more than once because
    // DPI can allow the page and then throttle or reset the media stream.
    public static class YouTubeProbe
    {
        private static readonly string[] VideoIds = new string[]
        {
            "dQw4w9WgXcQ",
            "aqz-KE-bpKQ"
        };

        public static YouTubeProbeResult Run(int timeoutMs, int attempts)
        {
            YouTubeHttp.EnableTls();
            int value = attempts < 1 ? 1 : (attempts > 3 ? 3 : attempts);
            YouTubeProbeResult best = null;
            for (int i = 0; i < VideoIds.Length; i++)
            {
                YouTubeProbeResult result = ProbeVideo(VideoIds[i], timeoutMs, value);
                if (best == null || Score(result) > Score(best))
                {
                    best = result;
                }
                if (result.PageOk && result.PlayerOk)
                {
                    // A working player is enough to judge the media path. Do not
                    // hide a CDN failure behind a fallback video.
                    return result;
                }
            }
            return best == null ? new YouTubeProbeResult(string.Empty) : best;
        }

        internal static YouTubeProbeResult ProbeVideo(
            string videoId, int timeoutMs, int attempts)
        {
            Stopwatch watch = Stopwatch.StartNew();
            YouTubeProbeResult result = new YouTubeProbeResult(videoId);
            string url = "https://www.youtube.com/watch?v=" + videoId +
                         "&hl=en&gl=US&has_verified=1&bpctr=9999999999";
            string html;
            int code;
            string error;
            if (!YouTubeHttp.GetText(url, timeoutMs, out html, out code, out error))
            {
                watch.Stop();
                result.SetPage(false, error, watch.ElapsedMilliseconds);
                return result;
            }
            watch.Stop();
            if (code != 200 || string.IsNullOrEmpty(html))
            {
                result.SetPage(false, "HTTP " + code, watch.ElapsedMilliseconds);
                return result;
            }
            result.SetPage(
                true,
                "HTTP 200, " + html.Length + " символов",
                watch.ElapsedMilliseconds);

            string json = ExtractPlayerResponse(html);
            if (string.IsNullOrEmpty(json))
            {
                result.SetPlayer(false, "player response не найден");
                return result;
            }

            object root;
            string parseError;
            if (!TryParseJson(json, out root, out parseError))
            {
                result.SetPlayer(
                    false, "player response не разобран: " + parseError);
                return result;
            }

            string status = GetString(Get(root, "playabilityStatus", "status"));
            string streamUrl = string.Equals(
                status, "OK", StringComparison.OrdinalIgnoreCase)
                ? FindStreamUrl(root)
                : null;
            string apiNote = string.Empty;
            if (string.IsNullOrEmpty(streamUrl))
            {
                // The watch page often returns streamingData without direct
                // googlevideo URLs (signatureCipher only), and some IPs get a
                // LOGIN_REQUIRED page. The Android VR innertube client returns
                // direct URLs and needs no decipher.
                string apiError;
                string apiJson = YouTubeHttp.RequestPlayerJson(
                    videoId, GetApiKey(html), timeoutMs, out apiError);
                if (string.IsNullOrEmpty(apiJson))
                {
                    apiNote = "API: " + Text(apiError);
                }
                else
                {
                    object apiRoot;
                    string apiParseError;
                    if (TryParseJson(apiJson, out apiRoot, out apiParseError))
                    {
                        string apiStatus = GetString(
                            Get(apiRoot, "playabilityStatus", "status"));
                        string apiUrl = string.Equals(
                            apiStatus, "OK", StringComparison.OrdinalIgnoreCase)
                            ? FindStreamUrl(apiRoot)
                            : null;
                        if (!string.IsNullOrEmpty(apiUrl))
                        {
                            root = apiRoot;
                            status = apiStatus;
                            streamUrl = apiUrl;
                        }
                        else
                        {
                            apiNote = "API: " + Text(apiStatus);
                        }
                    }
                    else
                    {
                        apiNote = "API JSON: " + apiParseError;
                    }
                }
            }
            if (!string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase))
            {
                string reason = GetString(Get(root, "playabilityStatus", "reason"));
                result.SetPlayer(
                    false,
                    "playabilityStatus=" + Text(status) +
                    (string.IsNullOrEmpty(reason) ? string.Empty : ", " + reason));
                return result;
            }
            if (string.IsNullOrEmpty(streamUrl))
            {
                result.SetPlayer(
                    false,
                    "в streamingData нет URL googlevideo" +
                    (string.IsNullOrEmpty(apiNote) ? string.Empty : " (" + apiNote + ")"));
                return result;
            }
            result.SetPlayer(true, "streamingData получен");

            Stopwatch cdnWatch = Stopwatch.StartNew();
            int successes = 0;
            long bytes = 0;
            string lastDetail = string.Empty;
            string failureDetail = string.Empty;
            for (int i = 0; i < attempts; i++)
            {
                long start = (long)i * YouTubeHttp.FragmentBytes;
                int received;
                string detail;
                bool ok = YouTubeHttp.DownloadRange(
                    streamUrl, start, YouTubeHttp.FragmentBytes, timeoutMs,
                    out received, out detail);
                lastDetail = detail;
                if (ok)
                {
                    successes++;
                    bytes += received;
                }
                else if (string.IsNullOrEmpty(failureDetail))
                {
                    failureDetail = detail;
                }
            }
            cdnWatch.Stop();
            bool cdnOk = successes >= (attempts == 1 ? 1 : 2);
            string summary = successes + " из " + attempts + " фрагментов, " +
                             (bytes / 1024) + " КБ";
            if (!cdnOk)
            {
                summary += " (" + (string.IsNullOrEmpty(failureDetail)
                    ? lastDetail
                    : failureDetail) + ")";
            }
            result.SetCdn(
                cdnOk, attempts, successes, bytes, summary,
                result.ElapsedMs + cdnWatch.ElapsedMilliseconds);
            return result;
        }

        internal static string ExtractPlayerResponse(string html)
        {
            if (string.IsNullOrEmpty(html))
            {
                return null;
            }
            const string marker = "ytInitialPlayerResponse";
            int search = 0;
            while (search < html.Length)
            {
                int found = html.IndexOf(marker, search, StringComparison.Ordinal);
                if (found < 0)
                {
                    return null;
                }
                int equals = html.IndexOf('=', found + marker.Length);
                if (equals < 0)
                {
                    return null;
                }
                int start = html.IndexOf('{', equals + 1);
                int semicolon = html.IndexOf(';', equals + 1);
                if (start < 0 || (semicolon >= 0 && semicolon < start))
                {
                    search = found + marker.Length;
                    continue;
                }
                int end = FindMatchingBrace(html, start);
                if (end > start)
                {
                    return html.Substring(start, end - start + 1);
                }
                search = found + marker.Length;
            }
            return null;
        }

        internal static string FindStreamUrl(object root)
        {
            object streaming = Get(root, "streamingData");
            string url = FindUrlInFormats(Get(streaming, "formats"));
            if (string.IsNullOrEmpty(url))
            {
                url = FindUrlInFormats(Get(streaming, "adaptiveFormats"));
            }
            return url;
        }

        private static string FindUrlInFormats(object formats)
        {
            object[] items = formats as object[];
            if (items == null)
            {
                return null;
            }
            string fallback = null;
            foreach (object item in items)
            {
                Dictionary<string, object> format =
                    item as Dictionary<string, object>;
                if (format == null)
                {
                    continue;
                }
                string url = GetString(Get(format, "url"));
                if (string.IsNullOrEmpty(url) || !IsGooglevideoUrl(url))
                {
                    continue;
                }
                string mime = GetString(Get(format, "mimeType"));
                if (string.IsNullOrEmpty(mime) ||
                    mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
                {
                    if (fallback == null)
                    {
                        fallback = url;
                    }
                    if (mime != null &&
                        mime.StartsWith("video/mp4", StringComparison.OrdinalIgnoreCase))
                    {
                        return url;
                    }
                }
            }
            return fallback;
        }

        private static bool IsGooglevideoUrl(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                return false;
            }
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            string host = uri.Host.ToLowerInvariant();
            return host == "googlevideo.com" || host.EndsWith(
                ".googlevideo.com", StringComparison.Ordinal);
        }

        private static bool TryParseJson(
            string json, out object root, out string error)
        {
            root = null;
            error = string.Empty;
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = 4 * 1024 * 1024;
                serializer.RecursionLimit = 100;
                root = serializer.DeserializeObject(json);
                return root != null;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name;
                return false;
            }
        }

        private static string GetApiKey(string html)
        {
            if (string.IsNullOrEmpty(html))
            {
                return string.Empty;
            }
            const string marker = "\"INNERTUBE_API_KEY\"";
            int found = html.IndexOf(marker, StringComparison.Ordinal);
            if (found < 0)
            {
                return string.Empty;
            }
            int colon = html.IndexOf(':', found + marker.Length);
            if (colon < 0)
            {
                return string.Empty;
            }
            int start = colon + 1;
            while (start < html.Length && char.IsWhiteSpace(html[start]))
            {
                start++;
            }
            if (start >= html.Length || html[start] != '"')
            {
                return string.Empty;
            }
            start++;
            int end = html.IndexOf('"', start);
            return end > start ? html.Substring(start, end - start) : string.Empty;
        }

        private static int FindMatchingBrace(string text, int start)
        {
            int depth = 0;
            bool quoted = false;
            bool escaped = false;
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (c == '\\')
                    {
                        escaped = true;
                    }
                    else if (c == '"')
                    {
                        quoted = false;
                    }
                    continue;
                }
                if (c == '"')
                {
                    quoted = true;
                }
                else if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        private static object Get(object root, params string[] path)
        {
            object current = root;
            foreach (string key in path)
            {
                Dictionary<string, object> dictionary =
                    current as Dictionary<string, object>;
                if (dictionary == null)
                {
                    return null;
                }
                object value;
                if (!dictionary.TryGetValue(key, out value))
                {
                    return null;
                }
                current = value;
            }
            return current;
        }

        private static string GetString(object value)
        {
            return value as string;
        }

        private static string Text(string value)
        {
            return string.IsNullOrEmpty(value) ? "нет данных" : value;
        }

        private static int Score(YouTubeProbeResult result)
        {
            if (result == null)
            {
                return 0;
            }
            int score = 0;
            if (result.PageOk)
            {
                score += 1;
            }
            if (result.PlayerOk)
            {
                score += 2;
            }
            if (result.CdnOk)
            {
                score += 4;
            }
            return score;
        }
    }
}
