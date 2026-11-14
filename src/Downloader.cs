using System;
using System.IO;
using System.Net;
using System.Text;

namespace ZapretControl
{
    // Small HTTPS client used by the bootstrap and both update channels.
    // Downloads always go to a temporary file first and are size-limited.
    public static class Downloader
    {
        public const int DefaultTimeoutMs = 30000;
        public const long DefaultMaxBytes = 104857600; // 100 MiB

        public delegate void ProgressHandler(long received, long total);

        private static readonly string UserAgent = "ZapretControl/" + AppInfo.Version;

        public static string GetString(string url)
        {
            return GetString(url, DefaultTimeoutMs, 2097152);
        }

        public static string GetString(string url, int timeoutMs, int maxBytes)
        {
            byte[] data = GetBytes(url, timeoutMs, maxBytes, null);
            return Encoding.UTF8.GetString(data).Trim();
        }

        public static byte[] GetBytes(
            string url,
            int timeoutMs,
            long maxBytes,
            ProgressHandler progress)
        {
            using (HttpWebResponse response = CreateResponse(url, timeoutMs))
            using (Stream stream = response.GetResponseStream())
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("Сервер вернул пустой ответ.");
                }
                long total = response.ContentLength;
                MemoryStream memory = new MemoryStream();
                try
                {
                    CopyLimited(stream, memory, maxBytes, total, progress);
                    return memory.ToArray();
                }
                finally
                {
                    memory.Dispose();
                }
            }
        }

        public static void DownloadFile(string url, string destination, ProgressHandler progress)
        {
            DownloadFile(url, destination, DefaultTimeoutMs, DefaultMaxBytes, progress);
        }

        public static void DownloadFile(
            string url,
            string destination,
            int timeoutMs,
            long maxBytes,
            ProgressHandler progress)
        {
            if (string.IsNullOrEmpty(destination))
            {
                throw new ArgumentException("Не задан путь для загрузки.", "destination");
            }
            string directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporary = destination + ".part-" + Guid.NewGuid().ToString("N");
            try
            {
                using (HttpWebResponse response = CreateResponse(url, timeoutMs))
                using (Stream stream = response.GetResponseStream())
                using (FileStream file = new FileStream(
                    temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    if (stream == null)
                    {
                        throw new InvalidOperationException("Сервер вернул пустой ответ.");
                    }
                    CopyLimited(stream, file, maxBytes, response.ContentLength, progress);
                    file.Flush();
                }

                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
                File.Move(temporary, destination);
            }
            finally
            {
                TryDelete(temporary);
            }
        }

        private static HttpWebResponse CreateResponse(string url, int timeoutMs)
        {
            if (string.IsNullOrEmpty(url) ||
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Разрешены только HTTPS-адреса.");
            }

            try
            {
                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            }
            catch (Exception)
            {
                // Older runtime: keep the system default.
            }

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = UserAgent;
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            request.AllowAutoRedirect = true;
            request.MaximumAutomaticRedirections = 10;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.CachePolicy = new System.Net.Cache.RequestCachePolicy(
                System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            request.Headers["Cache-Control"] = "no-cache";

            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            if (!string.Equals(
                response.ResponseUri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                response.Dispose();
                throw new InvalidOperationException("Редирект на не-HTTPS адрес запрещён.");
            }
            int status = (int)response.StatusCode;
            if (status < 200 || status >= 300)
            {
                response.Dispose();
                throw new InvalidOperationException("HTTP " + status + " для " + url);
            }
            return response;
        }

        private static void CopyLimited(
            Stream source,
            Stream destination,
            long maxBytes,
            long total,
            ProgressHandler progress)
        {
            byte[] buffer = new byte[65536];
            long received = 0;
            while (true)
            {
                int read = source.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }
                received += read;
                if (maxBytes > 0 && received > maxBytes)
                {
                    throw new InvalidOperationException("Загрузка превысила допустимый размер.");
                }
                destination.Write(buffer, 0, read);
                if (progress != null)
                {
                    progress(received, total);
                }
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
