using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Authentication;
using System.Text;
using System.Web.Script.Serialization;

namespace ZapretControl
{
    public sealed class YouTubeVideoResult
    {
        public string VideoId { get; private set; }
        public bool StreamResolved { get; internal set; }
        public int Attempts { get; internal set; }
        public int Successes { get; internal set; }
        public long Bytes { get; internal set; }
        public bool Tls12Attempted { get; internal set; }
        public bool Tls12Ok { get; internal set; }
        public bool Tls13Attempted { get; internal set; }
        public bool Tls13Ok { get; internal set; }
        public string Detail { get; internal set; }

        public YouTubeVideoResult(string videoId)
        {
            VideoId = videoId ?? string.Empty;
            Detail = string.Empty;
        }

        public bool Ok
        {
            get
            {
                return StreamResolved &&
                       YouTubeProbe.IsVideoOk(Successes, Attempts);
            }
        }

        public string Display
        {
            get
            {
                if (!StreamResolved)
                {
                    return VideoId + ": нет потока";
                }
                return VideoId + ": " + Successes + "/" + Attempts;
            }
        }
    }

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
        public bool Tls12Attempted { get; private set; }
        public bool Tls12Ok { get; private set; }
        public bool Tls13Attempted { get; private set; }
        public bool Tls13Ok { get; private set; }
        public List<YouTubeVideoResult> Videos { get; private set; }

        public YouTubeProbeResult(string videoId)
        {
            VideoId = videoId ?? string.Empty;
            PageDetail = string.Empty;
            PlayerDetail = string.Empty;
            CdnDetail = string.Empty;
            Videos = new List<YouTubeVideoResult>();
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

        internal void SetTls(
            bool tls12Attempted, bool tls12Ok,
            bool tls13Attempted, bool tls13Ok)
        {
            Tls12Attempted = tls12Attempted;
            Tls12Ok = tls12Ok;
            Tls13Attempted = tls13Attempted;
            Tls13Ok = tls13Ok;
        }

        internal void AddVideo(YouTubeVideoResult video)
        {
            if (video != null)
            {
                Videos.Add(video);
            }
        }

        private static string Text(string value)
        {
            return string.IsNullOrEmpty(value) ? "нет данных" : value;
        }
    }

    // Full YouTube pipeline for one strategy: watch page, player response and
    // two videos with three 384 KB fragments each. The player stage starts as
    // soon as the page is parsed, while the parallel host and freeze stages of
    // the caller continue to run behind the shared connection gate.
    public static class YouTubeProbe
    {
        private const int FragmentTotalTimeoutMs = 6000;

        private static readonly string[] VideoIds = new string[]
        {
            "dQw4w9WgXcQ",
            "aqz-KE-bpKQ"
        };

        public static YouTubeProbeResult Run(int timeoutMs, int attempts)
        {
            using (NetworkGate gate = new NetworkGate(16))
            {
                return Run(gate, timeoutMs, attempts);
            }
        }

        internal static YouTubeProbeResult Run(
            NetworkGate gate, int requestTimeoutMs, int fragmentsPerVideo)
        {
            YouTubeHttp.EnableTls();
            int fragments = fragmentsPerVideo < 1
                ? 1
                : (fragmentsPerVideo > 3 ? 3 : fragmentsPerVideo);
            Stopwatch watch = Stopwatch.StartNew();
            YouTubeProbeResult result = new YouTubeProbeResult(VideoIds[0]);

            string html;
            int code;
            string error;
            if (!FetchPage(gate, VideoIds[0], requestTimeoutMs, out html, out code, out error))
            {
                watch.Stop();
                result.SetPage(false, error, watch.ElapsedMilliseconds);
                return result;
            }
            if (code != 200 || string.IsNullOrEmpty(html))
            {
                watch.Stop();
                result.SetPage(false, "HTTP " + code, watch.ElapsedMilliseconds);
                return result;
            }
            result.SetPage(
                true,
                "HTTP 200, " + html.Length + " символов",
                watch.ElapsedMilliseconds);

            object pageRoot = null;
            string pageJson = ExtractPlayerResponse(html);
            if (!string.IsNullOrEmpty(pageJson))
            {
                object parsed;
                string parseError;
                if (TryParseJson(pageJson, out parsed, out parseError))
                {
                    pageRoot = parsed;
                }
            }
            string apiKey = GetApiKey(html);

            string[] urls = new string[VideoIds.Length];
            string[] notes = new string[VideoIds.Length];
            Action[] resolve = new Action[VideoIds.Length];
            for (int i = 0; i < VideoIds.Length; i++)
            {
                int index = i;
                resolve[i] = delegate()
                {
                    try
                    {
                        string note;
                        urls[index] = ResolveStreamUrl(
                            VideoIds[index],
                            index == 0 ? pageRoot : null,
                            apiKey,
                            gate,
                            requestTimeoutMs,
                            out note);
                        notes[index] = note;
                    }
                    catch (Exception ex)
                    {
                        urls[index] = null;
                        notes[index] = ex.GetType().Name + ": " + ex.Message;
                    }
                };
            }
            ParallelWork.RunAll(resolve, null);

            int resolved = 0;
            StringBuilder playerNotes = new StringBuilder();
            for (int i = 0; i < urls.Length; i++)
            {
                if (!string.IsNullOrEmpty(urls[i]))
                {
                    resolved++;
                }
                if (playerNotes.Length > 0)
                {
                    playerNotes.Append("; ");
                }
                playerNotes.Append(VideoIds[i]).Append(": ")
                    .Append(string.IsNullOrEmpty(urls[i])
                        ? Text(notes[i])
                        : "поток есть");
            }
            if (resolved == 0)
            {
                watch.Stop();
                result.SetPlayer(false, "нет URL googlevideo: " + playerNotes);
                return result;
            }
            result.SetPlayer(true, "потоки: " + resolved + " из " + VideoIds.Length);

            YouTubeVideoResult[] videos = new YouTubeVideoResult[VideoIds.Length];
            List<Action> actions = new List<Action>();
            for (int i = 0; i < VideoIds.Length; i++)
            {
                YouTubeVideoResult video = new YouTubeVideoResult(VideoIds[i]);
                videos[i] = video;
                if (string.IsNullOrEmpty(urls[i]))
                {
                    video.Detail = Text(notes[i]);
                    result.AddVideo(video);
                    continue;
                }
                video.StreamResolved = true;
                video.Attempts = fragments;
                string url = urls[i];
                for (int fragment = 0; fragment < fragments; fragment++)
                {
                    int fragmentIndex = fragment;
                    actions.Add(delegate()
                    {
                        try
                        {
                            DownloadFragment(
                                video, url, fragmentIndex, fragments,
                                gate, requestTimeoutMs);
                        }
                        catch (Exception ex)
                        {
                            lock (video)
                            {
                                if (video.Detail.Length == 0)
                                {
                                    video.Detail = ex.GetType().Name +
                                                   ": " + ex.Message;
                                }
                            }
                        }
                    });
                }
                result.AddVideo(video);
            }
            ParallelWork.RunAll(actions, null);

            int totalSuccesses = 0;
            int totalAttempts = 0;
            long totalBytes = 0;
            bool anyVideoOk = false;
            bool tls12Attempted = false;
            bool tls12Ok = true;
            bool tls13Attempted = false;
            bool tls13Ok = true;
            StringBuilder cdnDetail = new StringBuilder();
            foreach (YouTubeVideoResult video in videos)
            {
                totalSuccesses += video.Successes;
                totalAttempts += video.Attempts;
                totalBytes += video.Bytes;
                anyVideoOk = anyVideoOk || video.Ok;
                if (video.Tls12Attempted)
                {
                    tls12Attempted = true;
                    tls12Ok = tls12Ok && video.Tls12Ok;
                }
                if (video.Tls13Attempted)
                {
                    tls13Attempted = true;
                    tls13Ok = tls13Ok && video.Tls13Ok;
                }
                if (cdnDetail.Length > 0)
                {
                    cdnDetail.Append("; ");
                }
                cdnDetail.Append(video.Display);
            }
            watch.Stop();
            result.SetTls(tls12Attempted, tls12Ok, tls13Attempted, tls13Ok);
            result.SetCdn(
                anyVideoOk,
                totalAttempts,
                totalSuccesses,
                totalBytes,
                cdnDetail.ToString(),
                watch.ElapsedMilliseconds);
            return result;
        }

        // A video passes with 2 of 3 fragments; with a shorter attempt list it
        // needs one fragment.
        internal static bool IsVideoOk(int successes, int attempts)
        {
            if (attempts < 1)
            {
                return false;
            }
            int required = attempts >= 2 ? 2 : 1;
            return successes >= required;
        }

        // Regression helper for the selftest: one working video is enough even
        // when the second one fails completely.
        internal static bool CombineVideos(int[] successes, int attemptsPerVideo)
        {
            if (successes == null || attemptsPerVideo < 1)
            {
                return false;
            }
            foreach (int value in successes)
            {
                if (IsVideoOk(value, attemptsPerVideo))
                {
                    return true;
                }
            }
            return false;
        }

        internal static YouTubeProbeResult ProbeVideo(
            string videoId, int timeoutMs, int attempts)
        {
            NetworkGate gate = new NetworkGate(16);
            try
            {
                return Run(gate, timeoutMs, attempts);
            }
            finally
            {
                gate.Dispose();
            }
        }

        private static void DownloadFragment(
            YouTubeVideoResult video,
            string url,
            int index,
            int fragments,
            NetworkGate gate,
            int requestTimeoutMs)
        {
            SslProtocols protocols = SslProtocols.Tls12;
            bool pinned = false;
            string label = "по умолчанию";
            if (fragments >= 2 && index == 1)
            {
                pinned = true;
                protocols = SslProtocols.Tls12;
                label = "TLS1.2";
            }
            else if (fragments >= 3 && index == 2)
            {
                pinned = true;
                protocols = TlsHttp.Tls13;
                label = "TLS1.3";
            }

            long start = (long)index * YouTubeHttp.FragmentBytes;
            int received = 0;
            string detail = string.Empty;
            bool ok;
            gate.Enter();
            // Start the 6 s budget after the gate: queue wait is not network
            // time and must not turn a fast fragment into a failure.
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                if (pinned)
                {
                    ok = TlsHttp.DownloadRange(
                        url, start, YouTubeHttp.FragmentBytes, protocols,
                        requestTimeoutMs, FragmentTotalTimeoutMs,
                        out received, out detail);
                }
                else
                {
                    ok = YouTubeHttp.DownloadRange(
                        url, start, YouTubeHttp.FragmentBytes,
                        FragmentTotalTimeoutMs, out received, out detail);
                }
            }
            finally
            {
                gate.Exit();
            }
            watch.Stop();
            bool success = ok &&
                           received >= YouTubeHttp.MinFragmentBytes &&
                           watch.ElapsedMilliseconds <= FragmentTotalTimeoutMs;
            lock (video)
            {
                if (index == 1)
                {
                    video.Tls12Attempted = true;
                    video.Tls12Ok = success;
                }
                else if (index == 2)
                {
                    video.Tls13Attempted = true;
                    video.Tls13Ok = success;
                }
                if (success)
                {
                    video.Successes++;
                    video.Bytes += received;
                }
                if (video.Detail.Length == 0 && !success)
                {
                    video.Detail = label + ": " + Text(detail);
                }
            }
        }

        private static bool FetchPage(
            NetworkGate gate,
            string videoId,
            int timeoutMs,
            out string html,
            out int code,
            out string error)
        {
            string url = "https://www.youtube.com/watch?v=" + videoId +
                         "&hl=en&gl=US&has_verified=1&bpctr=9999999999";
            gate.Enter();
            try
            {
                return YouTubeHttp.GetText(url, timeoutMs, out html, out code, out error);
            }
            finally
            {
                gate.Exit();
            }
        }

        private static string ResolveStreamUrl(
            string videoId,
            object pageRoot,
            string apiKey,
            NetworkGate gate,
            int timeoutMs,
            out string note)
        {
            note = string.Empty;
            if (pageRoot != null)
            {
                string status = GetString(Get(pageRoot, "playabilityStatus", "status"));
                if (string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase))
                {
                    string pageUrl = FindStreamUrl(pageRoot);
                    if (!string.IsNullOrEmpty(pageUrl))
                    {
                        note = "страница";
                        return pageUrl;
                    }
                    note = "страница без прямого URL";
                }
                else
                {
                    note = "playabilityStatus=" + Text(status);
                }
            }

            string apiError;
            string apiJson;
            gate.Enter();
            try
            {
                apiJson = YouTubeHttp.RequestPlayerJson(
                    videoId, apiKey, timeoutMs, out apiError);
            }
            finally
            {
                gate.Exit();
            }
            if (string.IsNullOrEmpty(apiJson))
            {
                note = AppendNote(note, "API: " + Text(apiError));
                return null;
            }
            object apiRoot;
            string apiParseError;
            if (!TryParseJson(apiJson, out apiRoot, out apiParseError))
            {
                note = AppendNote(note, "API JSON: " + Text(apiParseError));
                return null;
            }
            string apiStatus = GetString(
                Get(apiRoot, "playabilityStatus", "status"));
            if (!string.Equals(apiStatus, "OK", StringComparison.OrdinalIgnoreCase))
            {
                note = AppendNote(note, "API status=" + Text(apiStatus));
                return null;
            }
            string apiUrl = FindStreamUrl(apiRoot);
            if (string.IsNullOrEmpty(apiUrl))
            {
                note = AppendNote(note, "API без URL googlevideo");
                return null;
            }
            note = AppendNote(note, "API");
            return apiUrl;
        }

        private static string AppendNote(string current, string next)
        {
            if (string.IsNullOrEmpty(current))
            {
                return next;
            }
            return current + "; " + next;
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
    }
}
