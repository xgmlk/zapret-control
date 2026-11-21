using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace ZapretControl
{
    // HTTP transport for the YouTube probe. Kept separate from parsing and
    // probing so the fragile player-response code stays small and testable.
    internal static class YouTubeHttp
    {
        internal const int FragmentBytes = 393216;
        // 288 KB: a fragment smaller than this does not prove that media
        // traffic survives DPI, even when HTTP 200/206 arrives.
        internal const int MinFragmentBytes = 288 * 1024;

        internal static void EnableTls()
        {
            try
            {
                // Add TLS 1.2 on .NET 4.x; do not remove newer protocols or
                // downgrade the process-wide default for update downloads.
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                ServicePointManager.Expect100Continue = false;
            }
            catch (Exception)
            {
            }
        }

        internal static string RequestPlayerJson(
            string videoId,
            string apiKey,
            int timeoutMs,
            out string error)
        {
            error = string.Empty;
            if (string.IsNullOrEmpty(apiKey))
            {
                error = "INNERTUBE_API_KEY не найден";
                return null;
            }
            try
            {
                Dictionary<string, object> client = new Dictionary<string, object>();
                client["clientName"] = "ANDROID_VR";
                client["clientVersion"] = "1.60.19";
                client["deviceMake"] = "Oculus";
                client["deviceModel"] = "Quest 3";
                client["osName"] = "Android";
                client["osVersion"] = "12L";
                client["androidSdkVersion"] = 32;
                client["hl"] = "en";
                client["gl"] = "US";

                Dictionary<string, object> context = new Dictionary<string, object>();
                context["client"] = client;

                Dictionary<string, object> body = new Dictionary<string, object>();
                body["context"] = context;
                body["videoId"] = videoId;
                body["contentCheckOk"] = true;
                body["racyCheckOk"] = true;

                JavaScriptSerializer serializer = new JavaScriptSerializer();
                string payload = serializer.Serialize(body);
                byte[] data = Encoding.UTF8.GetBytes(payload);

                string url = "https://www.youtube.com/youtubei/v1/player?key=" +
                             Uri.EscapeDataString(apiKey);
                HttpWebRequest request = CreateRequest(url, timeoutMs);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Accept = "application/json";
                request.ContentLength = data.Length;
                request.ServicePoint.Expect100Continue = false;
                using (Stream stream = request.GetRequestStream())
                {
                    stream.Write(data, 0, data.Length);
                }
                using (HttpWebResponse response =
                    (HttpWebResponse)request.GetResponse())
                {
                    using (Stream stream = response.GetResponseStream())
                    {
                        if (stream == null)
                        {
                            error = "пустой ответ Innertube";
                            return null;
                        }
                        using (StreamReader reader = new StreamReader(
                            stream, Encoding.UTF8, true))
                        {
                            bool truncated;
                            string jsonText = ReadLimited(reader, out truncated);
                            if (truncated)
                            {
                                error = "ответ больше 4 МБ";
                                return null;
                            }
                            return jsonText;
                        }
                    }
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                if (response != null)
                {
                    error = "HTTP " + (int)response.StatusCode + " (" + ex.Status + ")";
                    response.Dispose();
                }
                else
                {
                    error = "Ошибка: " + ex.Status;
                }
                return null;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }

        internal static bool DownloadRange(
            string url,
            long start,
            int length,
            int timeoutMs,
            out int received,
            out string detail)
        {
            received = 0;
            detail = string.Empty;
            try
            {
                HttpWebRequest request = CreateRequest(url, timeoutMs);
                request.Accept = "*/*";
                request.Referer = "https://www.youtube.com/";
                request.AddRange(start, start + length - 1);
                using (HttpWebResponse response =
                    (HttpWebResponse)request.GetResponse())
                {
                    int code = (int)response.StatusCode;
                    if (code != 200 && code != 206)
                    {
                        detail = "HTTP " + code;
                        return false;
                    }
                    using (Stream stream = response.GetResponseStream())
                    {
                        if (stream == null)
                        {
                            detail = "HTTP " + code + ", пустой ответ";
                            return false;
                        }
                        byte[] buffer = new byte[16384];
                        int total = 0;
                        while (total < length)
                        {
                            int read = stream.Read(
                                buffer, 0, Math.Min(buffer.Length, length - total));
                            if (read <= 0)
                            {
                                break;
                            }
                            total += read;
                        }
                        received = total;
                        if (total < MinFragmentBytes)
                        {
                            detail = "HTTP " + code + ", мало данных: " +
                                     total + " байт";
                            return false;
                        }
                        detail = "HTTP " + code + ", " + total + " байт";
                        return true;
                    }
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                if (response != null)
                {
                    detail = "HTTP " + (int)response.StatusCode + " (" + ex.Status + ")";
                    response.Dispose();
                }
                else
                {
                    detail = "Ошибка: " + ex.Status;
                }
                return false;
            }
            catch (Exception ex)
            {
                detail = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        internal static bool GetText(
            string url, int timeoutMs, out string text, out int code, out string error)
        {
            text = string.Empty;
            code = 0;
            error = string.Empty;
            try
            {
                HttpWebRequest request = CreateRequest(url, timeoutMs);
                request.Accept = "text/html,application/xhtml+xml";
                using (HttpWebResponse response =
                    (HttpWebResponse)request.GetResponse())
                {
                    code = (int)response.StatusCode;
                    using (Stream stream = response.GetResponseStream())
                    {
                        if (stream == null)
                        {
                            error = "HTTP " + code + ", пустой ответ";
                            return false;
                        }
                        using (StreamReader reader = new StreamReader(
                            stream, Encoding.UTF8, true))
                        {
                            bool truncated;
                            text = ReadLimited(reader, out truncated);
                            if (truncated)
                            {
                                error = "ответ больше 4 МБ";
                                return false;
                            }
                        }
                    }
                }
                if (code != 200)
                {
                    error = "HTTP " + code;
                    return false;
                }
                return true;
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                if (response != null)
                {
                    code = (int)response.StatusCode;
                    error = "HTTP " + code + " (" + ex.Status + ")";
                    response.Dispose();
                }
                else
                {
                    error = "Ошибка: " + ex.Status;
                }
                return false;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static string ReadLimited(StreamReader reader, out bool truncated)
        {
            const int maxChars = 4 * 1024 * 1024;
            StringBuilder builder = new StringBuilder();
            char[] buffer = new char[8192];
            truncated = false;
            while (true)
            {
                int remaining = maxChars - builder.Length;
                if (remaining <= 0)
                {
                    truncated = reader.Peek() >= 0;
                    break;
                }
                int read = reader.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                if (read <= 0)
                {
                    break;
                }
                builder.Append(buffer, 0, read);
            }
            return builder.ToString();
        }

        private static HttpWebRequest CreateRequest(string url, int timeoutMs)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.UserAgent =
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/124.0.0.0 Safari/537.36";
            request.Proxy = null;
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            request.KeepAlive = false;
            request.AllowAutoRedirect = true;
            request.MaximumAutomaticRedirections = 5;
            request.AutomaticDecompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate;
            return request;
        }
    }
}
