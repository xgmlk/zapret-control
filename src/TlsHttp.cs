using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

namespace ZapretControl
{
    // Minimal HTTPS client with a pinned TLS version. HttpWebRequest cannot
    // choose TLS 1.2/1.3 per request on .NET Framework, and changing the
    // process-wide SecurityProtocol would race with parallel probes.
    internal static class TlsHttp
    {
        private const int Tls13Value = 12288;
        private const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/124.0.0.0 Safari/537.36";

        internal static SslProtocols Tls12
        {
            get { return SslProtocols.Tls12; }
        }

        internal static SslProtocols Tls13
        {
            get { return (SslProtocols)Tls13Value; }
        }

        internal static string Label(SslProtocols protocols)
        {
            if ((int)protocols == Tls13Value)
            {
                return "1.3";
            }
            if (protocols == SslProtocols.Tls12)
            {
                return "1.2";
            }
            return protocols.ToString();
        }

        internal static bool GetStatus(
            string host,
            SslProtocols protocols,
            int timeoutMs,
            out int statusCode,
            out string detail)
        {
            statusCode = 0;
            detail = string.Empty;
            TcpClient client = null;
            SslStream ssl = null;
            Stopwatch total = Stopwatch.StartNew();
            try
            {
                string connectError;
                client = Connect(
                    host, 443, Remaining(total, timeoutMs), out connectError);
                if (client == null)
                {
                    detail = connectError;
                    return false;
                }
                string authError;
                ssl = Authenticate(
                    client, host, protocols,
                    Remaining(total, timeoutMs), out authError);
                if (ssl == null)
                {
                    detail = authError;
                    return false;
                }
                string request = "GET / HTTP/1.1\r\n" +
                                 "Host: " + host + "\r\n" +
                                 "User-Agent: " + UserAgent + "\r\n" +
                                 "Accept: */*\r\n" +
                                 "Connection: close\r\n\r\n";
                byte[] data = Encoding.ASCII.GetBytes(request);
                ssl.Write(data, 0, data.Length);
                ssl.Flush();
                string statusLine = ReadLine(ssl, 512, total, timeoutMs);
                if (!TryParseStatus(statusLine, out statusCode))
                {
                    detail = string.IsNullOrEmpty(statusLine)
                        ? "пустой ответ"
                        : "не HTTP-ответ";
                    return false;
                }
                detail = "HTTP " + statusCode + ", TLS " + Label(protocols);
                return true;
            }
            catch (Exception ex)
            {
                detail = Short(ex.GetType().Name + ": " + ex.Message);
                return false;
            }
            finally
            {
                Close(ssl, client);
            }
        }

        internal static bool DownloadRange(
            string url,
            long start,
            int length,
            SslProtocols protocols,
            int headerTimeoutMs,
            int totalTimeoutMs,
            out int received,
            out string detail)
        {
            received = 0;
            detail = string.Empty;
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase))
            {
                detail = "не HTTPS URL";
                return false;
            }
            Stopwatch watch = Stopwatch.StartNew();
            TcpClient client = null;
            SslStream ssl = null;
            try
            {
                string connectError;
                client = Connect(uri.Host, uri.Port > 0 ? uri.Port : 443,
                    Remaining(watch, headerTimeoutMs), out connectError);
                if (client == null)
                {
                    detail = connectError;
                    return false;
                }
                string authError;
                ssl = Authenticate(
                    client, uri.Host, protocols,
                    Remaining(watch, headerTimeoutMs), out authError);
                if (ssl == null)
                {
                    detail = authError;
                    return false;
                }
                string request = "GET " + uri.PathAndQuery + " HTTP/1.1\r\n" +
                                 "Host: " + uri.Host + "\r\n" +
                                 "User-Agent: " + UserAgent + "\r\n" +
                                 "Accept: */*\r\n" +
                                 "Referer: https://www.youtube.com/\r\n" +
                                 "Range: bytes=" + start + "-" + (start + length - 1) + "\r\n" +
                                 "Connection: close\r\n\r\n";
                byte[] data = Encoding.ASCII.GetBytes(request);
                ssl.Write(data, 0, data.Length);
                ssl.Flush();

                int statusCode;
                string statusLine = ReadLine(
                    ssl, 512, watch, headerTimeoutMs);
                if (!TryParseStatus(statusLine, out statusCode))
                {
                    detail = string.IsNullOrEmpty(statusLine)
                        ? "пустой ответ"
                        : "не HTTP-ответ";
                    return false;
                }
                if (statusCode != 200 && statusCode != 206)
                {
                    detail = "HTTP " + statusCode;
                    return false;
                }

                long contentLength = -1;
                bool chunked = false;
                int headerLines = 0;
                while (true)
                {
                    if (headerLines++ >= 100)
                    {
                        detail = "слишком много заголовков";
                        return false;
                    }
                    string line = ReadLine(
                        ssl, 1024, watch, headerTimeoutMs);
                    if (line.Length == 0)
                    {
                        break;
                    }
                    int colon = line.IndexOf(':');
                    if (colon <= 0)
                    {
                        continue;
                    }
                    string name = line.Substring(0, colon).Trim();
                    string value = line.Substring(colon + 1).Trim();
                    if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    {
                        long.TryParse(value, NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out contentLength);
                    }
                    else if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) &&
                             value.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        chunked = true;
                    }
                }

                ssl.ReadTimeout = Remaining(watch, totalTimeoutMs);
                bool timedOut;
                if (chunked)
                {
                    received = ReadChunked(ssl, length, totalTimeoutMs, watch, out timedOut);
                }
                else
                {
                    int wanted = contentLength > 0 && contentLength < length
                        ? (int)contentLength
                        : length;
                    received = ReadFixed(ssl, wanted, totalTimeoutMs, watch, out timedOut);
                }
                detail = "HTTP " + statusCode + ", " + received + " байт";
                if (timedOut)
                {
                    detail += ", таймаут";
                    return false;
                }
                return received > 0;
            }
            catch (Exception ex)
            {
                detail = Short(ex.GetType().Name + ": " + ex.Message);
                return false;
            }
            finally
            {
                Close(ssl, client);
            }
        }

        private static int ReadFixed(
            Stream stream,
            int length,
            int totalTimeoutMs,
            Stopwatch watch,
            out bool timedOut)
        {
            timedOut = false;
            byte[] buffer = new byte[16384];
            int total = 0;
            while (total < length)
            {
                int remaining = totalTimeoutMs - (int)watch.ElapsedMilliseconds;
                if (remaining <= 0)
                {
                    timedOut = true;
                    break;
                }
                try
                {
                    stream.ReadTimeout = remaining;
                }
                catch (Exception)
                {
                }
                int read;
                try
                {
                    read = stream.Read(buffer, 0, Math.Min(buffer.Length, length - total));
                }
                catch (IOException)
                {
                    timedOut = watch.ElapsedMilliseconds >= totalTimeoutMs;
                    break;
                }
                if (read <= 0)
                {
                    break;
                }
                total += read;
            }
            return total;
        }

        private static int ReadChunked(
            Stream stream,
            int length,
            int totalTimeoutMs,
            Stopwatch watch,
            out bool timedOut)
        {
            timedOut = false;
            byte[] buffer = new byte[16384];
            int total = 0;
            while (total < length)
            {
                if (watch.ElapsedMilliseconds > totalTimeoutMs)
                {
                    timedOut = true;
                    break;
                }
                string line = ReadLine(
                    stream, 64, watch, totalTimeoutMs);
                if (line.Length == 0)
                {
                    break;
                }
                int semicolon = line.IndexOf(';');
                if (semicolon >= 0)
                {
                    line = line.Substring(0, semicolon);
                }
                int size;
                if (!int.TryParse(line.Trim(), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out size) || size <= 0)
                {
                    break;
                }
                int remaining = size;
                while (remaining > 0 && total < length)
                {
                    int budget = totalTimeoutMs - (int)watch.ElapsedMilliseconds;
                    if (budget <= 0)
                    {
                        timedOut = true;
                        break;
                    }
                    try
                    {
                        stream.ReadTimeout = budget;
                    }
                    catch (Exception)
                    {
                    }
                    int read = stream.Read(buffer, 0,
                        Math.Min(buffer.Length, remaining));
                    if (read <= 0)
                    {
                        remaining = 0;
                        break;
                    }
                    if (total < length)
                    {
                        total += Math.Min(read, length - total);
                    }
                    remaining -= read;
                }
                if (total >= length || timedOut)
                {
                    break;
                }
                ReadLine(stream, 8, watch, totalTimeoutMs);
                if (timedOut)
                {
                    break;
                }
            }
            return total;
        }

        private static TcpClient Connect(
            string host,
            int port,
            int timeoutMs,
            out string error)
        {
            error = string.Empty;
            Stopwatch budget = Stopwatch.StartNew();
            IPAddress[] addresses = Resolve(host, timeoutMs, out error);
            if (addresses == null)
            {
                return null;
            }
            // Prefer IPv4: a blackholed IPv6 route would consume the whole
            // timeout before the working IPv4 address is tried.
            List<IPAddress> ordered = new List<IPAddress>();
            foreach (IPAddress address in addresses)
            {
                if (address.AddressFamily == AddressFamily.InterNetwork)
                {
                    ordered.Add(address);
                }
            }
            foreach (IPAddress address in addresses)
            {
                if (address.AddressFamily != AddressFamily.InterNetwork)
                {
                    ordered.Add(address);
                }
            }
            string lastError = string.Empty;
            foreach (IPAddress address in ordered)
            {
                int remaining = timeoutMs - (int)budget.ElapsedMilliseconds;
                if (remaining <= 0)
                {
                    lastError = "таймаут подключения";
                    break;
                }
                TcpClient client = new TcpClient(address.AddressFamily);
                IAsyncResult result = null;
                try
                {
                    result = client.BeginConnect(address, port, null, null);
                    if (!result.AsyncWaitHandle.WaitOne(remaining))
                    {
                        lastError = "таймаут подключения";
                        client.Close();
                        continue;
                    }
                    client.EndConnect(result);
                    client.NoDelay = true;
                    return client;
                }
                catch (Exception ex)
                {
                    lastError = Short(ex.Message);
                    try
                    {
                        client.Close();
                    }
                    catch (Exception)
                    {
                    }
                }
                finally
                {
                    if (result != null)
                    {
                        try
                        {
                            result.AsyncWaitHandle.Close();
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
            }
            error = string.IsNullOrEmpty(lastError)
                ? "нет соединения"
                : lastError;
            return null;
        }

        private static IPAddress[] Resolve(
            string host, int timeoutMs, out string error)
        {
            error = string.Empty;
            IAsyncResult result = null;
            try
            {
                result = Dns.BeginGetHostAddresses(host, null, null);
                if (!result.AsyncWaitHandle.WaitOne(timeoutMs))
                {
                    error = "DNS: таймаут";
                    return null;
                }
                IPAddress[] addresses = Dns.EndGetHostAddresses(result);
                if (addresses == null || addresses.Length == 0)
                {
                    error = "DNS не вернул адреса";
                    return null;
                }
                return addresses;
            }
            catch (Exception ex)
            {
                error = "DNS: " + Short(ex.Message);
                return null;
            }
            finally
            {
                if (result != null)
                {
                    try
                    {
                        result.AsyncWaitHandle.Close();
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        private static SslStream Authenticate(
            TcpClient client,
            string host,
            SslProtocols protocols,
            int timeoutMs,
            out string error)
        {
            error = string.Empty;
            NetworkStream network = client.GetStream();
            network.ReadTimeout = timeoutMs;
            network.WriteTimeout = timeoutMs;
            SslStream ssl = new SslStream(network, false);
            ssl.ReadTimeout = timeoutMs;
            ssl.WriteTimeout = timeoutMs;
            try
            {
                ssl.AuthenticateAsClient(host, null, protocols, false);
                return ssl;
            }
            catch (Exception ex)
            {
                error = "TLS " + Label(protocols) + ": " + Short(ex.Message);
                try
                {
                    ssl.Dispose();
                }
                catch (Exception)
                {
                }
                return null;
            }
        }

        private static string ReadLine(
            Stream stream, int maxLength, Stopwatch budget, int timeoutMs)
        {
            byte[] single = new byte[1];
            StringBuilder builder = new StringBuilder();
            while (builder.Length < maxLength)
            {
                int remaining = timeoutMs - (int)budget.ElapsedMilliseconds;
                if (remaining <= 0)
                {
                    break;
                }
                try
                {
                    stream.ReadTimeout = remaining;
                }
                catch (Exception)
                {
                }
                int read;
                try
                {
                    read = stream.Read(single, 0, 1);
                }
                catch (IOException)
                {
                    break;
                }
                if (read <= 0)
                {
                    break;
                }
                if (single[0] == (byte)'\n')
                {
                    break;
                }
                if (single[0] != (byte)'\r')
                {
                    builder.Append((char)single[0]);
                }
            }
            return builder.ToString();
        }

        private static bool TryParseStatus(string line, out int code)
        {
            code = 0;
            if (string.IsNullOrEmpty(line) ||
                !line.StartsWith("HTTP/", StringComparison.Ordinal))
            {
                return false;
            }
            int space = line.IndexOf(' ');
            if (space <= 0 || space + 4 > line.Length)
            {
                return false;
            }
            return int.TryParse(
                line.Substring(space + 1, 3),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out code) && code >= 100 && code <= 599;
        }

        private static void Close(Stream ssl, TcpClient client)
        {
            if (ssl != null)
            {
                try
                {
                    ssl.Dispose();
                }
                catch (Exception)
                {
                }
            }
            if (client != null)
            {
                try
                {
                    client.Close();
                }
                catch (Exception)
                {
                }
            }
        }

        private static int Remaining(Stopwatch watch, int budgetMs)
        {
            int value = budgetMs - (int)watch.ElapsedMilliseconds;
            return value < 1 ? 1 : value;
        }

        private static string Short(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "нет подробностей";
            }
            string value = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return value.Length > 120 ? value.Substring(0, 120) : value;
        }
    }
}
