using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZapretControl
{
    internal enum WsStage
    {
        Tcp,
        Tls,
        Upgrade,
        Ok
    }

    // Outcome of an upstream WebSocket connect: the socket on success, else
    // the phase that failed and (for an upgrade failure) the HTTP status.
    internal sealed class WsResult
    {
        internal TgWebSocket Socket;
        internal WsStage Stage;
        internal int Status;

        internal WsResult(TgWebSocket socket, WsStage stage, int status)
        {
            Socket = socket;
            Stage = stage;
            Status = status;
        }

        internal bool Ok
        {
            get { return Socket != null; }
        }
    }

    internal static class TgAsync
    {
        // Awaits a task with a deadline. Returns false on timeout instead of
        // throwing, and observes the abandoned task so its failure does not
        // surface later as an unobserved exception.
        internal static async Task<bool> WaitAsync(
            Task task, int timeoutMs, CancellationToken ct)
        {
            if (task == null)
            {
                return false;
            }
            if (task.IsCompleted)
            {
                await task.ConfigureAwait(false);
                return true;
            }
            Task delay = Task.Delay(timeoutMs, ct);
            Task finished = await Task.WhenAny(task, delay).ConfigureAwait(false);
            if (finished != task)
            {
                Observe(task);
                return false;
            }
            await task.ConfigureAwait(false);
            return true;
        }

        internal static void Observe(Task task)
        {
            if (task == null)
            {
                return;
            }
            task.ContinueWith(delegate(Task completed)
            {
                Exception ignored = completed.Exception;
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    // Minimal client WebSocket over a raw SslStream: connects to an IP with an
    // independent SNI/Host and speaks masked binary frames, mirroring the raw
    // WebSocket client of Flowseal/tg-ws-proxy. Certificate validation is
    // skipped on purpose: the transport carries its own MTProto encryption.
    internal sealed class TgWebSocket : IDisposable
    {
        private const byte OpContinuation = 0x0;
        private const byte OpText = 0x1;
        private const byte OpBinary = 0x2;
        private const byte OpClose = 0x8;
        private const byte OpPing = 0x9;
        private const byte OpPong = 0xA;

        private const int MaxFrameLen = 16 * 1024 * 1024;

        private readonly TcpClient _tcp;
        private readonly Stream _stream;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private volatile bool _closed;

        private long _bytesIn;
        private long _bytesOut;
        private int _assembled;

        internal long BytesIn
        {
            get { return Interlocked.Read(ref _bytesIn); }
        }

        internal long BytesOut
        {
            get { return Interlocked.Read(ref _bytesOut); }
        }

        internal int AssembledMessages
        {
            get { return Volatile.Read(ref _assembled); }
        }

        private TgWebSocket(TcpClient tcp, Stream stream)
        {
            _tcp = tcp;
            _stream = stream;
        }

        internal static async Task<WsResult> ConnectAsync(
            string host, string domain, int timeoutMs,
            string sni, CancellationToken ct)
        {
            string targetName = string.IsNullOrEmpty(sni) ? domain : sni;
            TcpClient tcp = new TcpClient();
            SslStream ssl = null;
            WsStage stage = WsStage.Tcp;
            try
            {
                Task connect = tcp.ConnectAsync(host, 443);
                if (!await TgAsync.WaitAsync(connect, timeoutMs, ct)
                        .ConfigureAwait(false))
                {
                    CloseQuietly(null, tcp);
                    return new WsResult(null, WsStage.Tcp, 0);
                }
                tcp.NoDelay = true;

                stage = WsStage.Tls;
                ssl = new SslStream(
                    tcp.GetStream(), false, AcceptAnyCertificate);
                if (!await AuthenticateAsync(
                        ssl, targetName, timeoutMs, ct).ConfigureAwait(false))
                {
                    CloseQuietly(ssl, tcp);
                    return new WsResult(null, WsStage.Tls, 0);
                }

                stage = WsStage.Upgrade;
                string key = Convert.ToBase64String(TgProtocol.RandomBytes(16));
                string request =
                    "GET /apiws HTTP/1.1\r\n" +
                    "Host: " + domain + "\r\n" +
                    "Upgrade: websocket\r\n" +
                    "Connection: Upgrade\r\n" +
                    "Sec-WebSocket-Key: " + key + "\r\n" +
                    "Sec-WebSocket-Version: 13\r\n" +
                    "Sec-WebSocket-Protocol: binary\r\n\r\n";
                byte[] bytes = Encoding.ASCII.GetBytes(request);
                await ssl.WriteAsync(bytes, 0, bytes.Length, ct)
                    .ConfigureAwait(false);
                await ssl.FlushAsync(ct).ConfigureAwait(false);

                int status = await ReadStatusAsync(
                    ssl, timeoutMs, ct).ConfigureAwait(false);
                if (status == 101)
                {
                    return new WsResult(
                        new TgWebSocket(tcp, ssl), WsStage.Ok, status);
                }
                CloseQuietly(ssl, tcp);
                return new WsResult(null, WsStage.Upgrade, status);
            }
            catch (Exception)
            {
                CloseQuietly(ssl, tcp);
                return new WsResult(null, stage, 0);
            }
        }

        private static async Task<bool> AuthenticateAsync(
            SslStream ssl, string targetName, int timeoutMs, CancellationToken ct)
        {
            // Offer TLS 1.3 only when the runtime knows the flag (it appeared
            // in .NET Framework 4.8). Older runtimes reject the value, and a
            // rejected value fails the whole handshake instead of falling
            // back, so the feature is detected first.
            SslProtocols protocols = SslProtocols.Tls12;
            if (Enum.IsDefined(typeof(SslProtocols), 12288))
            {
                protocols |= (SslProtocols)12288;
            }
            Task authenticate = ssl.AuthenticateAsClientAsync(
                targetName, null, protocols, false);
            return await TgAsync.WaitAsync(
                authenticate, timeoutMs, ct).ConfigureAwait(false);
        }

        private static bool AcceptAnyCertificate(
            object sender,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors errors)
        {
            return true;
        }

        private static async Task<int> ReadStatusAsync(
            SslStream ssl, int timeoutMs, CancellationToken ct)
        {
            StringBuilder builder = new StringBuilder();
            byte[] one = new byte[1];
            int status = 0;
            bool firstLineDone = false;
            Stopwatch watch = Stopwatch.StartNew();
            while (true)
            {
                int remaining = timeoutMs - (int)watch.ElapsedMilliseconds;
                if (remaining <= 0)
                {
                    return 0;
                }
                Task<int> read = ssl.ReadAsync(one, 0, 1, ct);
                Task finished = await Task.WhenAny(
                    read, Task.Delay(remaining, ct)).ConfigureAwait(false);
                if (finished != read)
                {
                    TgAsync.Observe(read);
                    return 0;
                }
                int count = await read.ConfigureAwait(false);
                if (count == 0)
                {
                    return 0;
                }
                builder.Append((char)one[0]);
                int length = builder.Length;
                if (length > 16384)
                {
                    return 0;
                }
                if (!firstLineDone && length >= 2 &&
                    builder[length - 2] == '\r' && builder[length - 1] == '\n')
                {
                    string first = builder.ToString(0, length - 2);
                    string[] parts = first.Split(' ');
                    if (parts.Length >= 2)
                    {
                        int.TryParse(parts[1], out status);
                    }
                    firstLineDone = true;
                }
                if (length >= 4 &&
                    builder[length - 4] == '\r' && builder[length - 3] == '\n' &&
                    builder[length - 2] == '\r' && builder[length - 1] == '\n')
                {
                    return status;
                }
            }
        }

        internal async Task SendAsync(byte[] data, CancellationToken ct)
        {
            if (_closed)
            {
                throw new IOException("WebSocket closed");
            }
            await WriteLockedAsync(
                BuildFrame(OpBinary, data), ct).ConfigureAwait(false);
            Interlocked.Add(ref _bytesOut, data.Length);
        }

        internal async Task SendBatchAsync(
            List<byte[]> parts, CancellationToken ct)
        {
            if (_closed)
            {
                throw new IOException("WebSocket closed");
            }
            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    byte[] frame = BuildFrame(OpBinary, parts[i]);
                    await _stream.WriteAsync(
                        frame, 0, frame.Length, ct).ConfigureAwait(false);
                    Interlocked.Add(ref _bytesOut, parts[i].Length);
                }
                await _stream.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private async Task WriteLockedAsync(byte[] frame, CancellationToken ct)
        {
            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _stream.WriteAsync(
                    frame, 0, frame.Length, ct).ConfigureAwait(false);
                await _stream.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private sealed class WsFrame
        {
            internal byte Opcode;
            internal bool Final;
            internal byte[] Payload;
        }

        // Reads the next application message, answering pings and joining
        // fragmented messages (a large transfer arrives as a FIN=0 frame plus
        // continuation frames; dropping them would desynchronise AES-CTR).
        internal async Task<byte[]> RecvAsync(CancellationToken ct)
        {
            List<byte> partial = null;
            while (!_closed)
            {
                WsFrame frame = await ReadFrameAsync(ct).ConfigureAwait(false);
                byte opcode = frame.Opcode;
                bool final = frame.Final;
                byte[] payload = frame.Payload;

                if (opcode == OpClose)
                {
                    _closed = true;
                    try
                    {
                        await WriteLockedAsync(
                            BuildFrame(OpClose, new byte[0]), ct)
                            .ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                    }
                    return null;
                }
                if (opcode == OpPing)
                {
                    try
                    {
                        await WriteLockedAsync(
                            BuildFrame(OpPong, payload), ct)
                            .ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                    }
                    continue;
                }
                if (opcode == OpPong)
                {
                    continue;
                }
                if (opcode == OpContinuation)
                {
                    if (partial == null)
                    {
                        continue;
                    }
                    partial.AddRange(payload);
                    if (partial.Count > MaxFrameLen)
                    {
                        throw new IOException(
                            "WS message too large (" + partial.Count + ")");
                    }
                    if (!final)
                    {
                        continue;
                    }
                    Interlocked.Increment(ref _assembled);
                    return partial.ToArray();
                }
                if (opcode == OpText || opcode == OpBinary)
                {
                    if (final)
                    {
                        return payload;
                    }
                    partial = new List<byte>(payload);
                    continue;
                }
            }
            return null;
        }

        private async Task<WsFrame> ReadFrameAsync(CancellationToken ct)
        {
            byte[] header = await ReadExactAsync(2, ct).ConfigureAwait(false);
            WsFrame frame = new WsFrame();
            frame.Final = (header[0] & 0x80) != 0;
            frame.Opcode = (byte)(header[0] & 0x0F);
            long length = header[1] & 0x7F;
            if (length == 126)
            {
                byte[] extended = await ReadExactAsync(2, ct)
                    .ConfigureAwait(false);
                length = (extended[0] << 8) | extended[1];
            }
            else if (length == 127)
            {
                byte[] extended = await ReadExactAsync(8, ct)
                    .ConfigureAwait(false);
                length = TgProtocol.ReadInt64BigEndian(extended, 0);
            }
            if (length < 0 || length > MaxFrameLen)
            {
                throw new IOException("WS frame too large (" + length + ")");
            }

            byte[] mask = null;
            if ((header[1] & 0x80) != 0)
            {
                mask = await ReadExactAsync(4, ct).ConfigureAwait(false);
            }

            frame.Payload = await ReadExactAsync((int)length, ct)
                .ConfigureAwait(false);
            if (mask != null)
            {
                for (int i = 0; i < frame.Payload.Length; i++)
                {
                    frame.Payload[i] ^= mask[i & 3];
                }
            }
            if (frame.Opcode == OpContinuation || frame.Opcode == OpText ||
                frame.Opcode == OpBinary)
            {
                Interlocked.Add(ref _bytesIn, frame.Payload.Length);
            }
            return frame;
        }

        private async Task<byte[]> ReadExactAsync(
            int length, CancellationToken ct)
        {
            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = await _stream.ReadAsync(
                    buffer, offset, length - offset, ct).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new EndOfStreamException();
                }
                offset += read;
            }
            return buffer;
        }

        private static byte[] BuildFrame(byte opcode, byte[] data)
        {
            byte[] mask = TgProtocol.RandomBytes(4);
            int length = data.Length;
            int headerLength = length < 126 ? 2 : (length < 65536 ? 4 : 10);
            byte[] frame = new byte[headerLength + 4 + length];
            frame[0] = (byte)(0x80 | opcode);
            int position;
            if (length < 126)
            {
                frame[1] = (byte)(0x80 | length);
                position = 2;
            }
            else if (length < 65536)
            {
                frame[1] = 0x80 | 126;
                frame[2] = (byte)(length >> 8);
                frame[3] = (byte)length;
                position = 4;
            }
            else
            {
                frame[1] = 0x80 | 127;
                TgProtocol.WriteInt64BigEndian(frame, 2, length);
                position = 10;
            }
            Buffer.BlockCopy(mask, 0, frame, position, 4);
            position += 4;
            for (int i = 0; i < length; i++)
            {
                frame[position + i] = (byte)(data[i] ^ mask[i & 3]);
            }
            return frame;
        }

        internal async Task CloseAsync()
        {
            if (_closed)
            {
                return;
            }
            _closed = true;
            try
            {
                await WriteLockedAsync(
                    BuildFrame(OpClose, new byte[0]),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
            Dispose();
        }

        public void Dispose()
        {
            _closed = true;
            try
            {
                _stream.Dispose();
            }
            catch (Exception)
            {
            }
            if (_tcp != null)
            {
                try
                {
                    _tcp.Close();
                }
                catch (Exception)
                {
                }
            }
            try
            {
                _writeLock.Dispose();
            }
            catch (Exception)
            {
            }
        }

        private static void CloseQuietly(SslStream ssl, TcpClient tcp)
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
            if (tcp != null)
            {
                try
                {
                    tcp.Close();
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
