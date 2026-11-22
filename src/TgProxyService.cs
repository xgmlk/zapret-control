using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ZapretControl
{
    // Status written by the GUI so the CLI can report a running proxy that
    // lives in another process.
    internal sealed class TgProxyStatusInfo
    {
        internal bool Running;
        internal int Port;
        internal int Pid;
        internal string Secret;
        internal string StartedUtc;
    }

    // Local Telegram MTProto -> WebSocket proxy. Telegram Desktop connects to
    // 127.0.0.1 as to an MTProto proxy; the service relays each connection to
    // a Telegram data centre over WebSocket-TLS through Cloudflare-fronted
    // domains, which survives IP-level blocking that packet desync cannot fix.
    // No administrator rights are required: a loopback listener plus outbound
    // TLS. Ported from Zapret2UI (MIT) and Flowseal/tg-ws-proxy (MIT).
    internal sealed class TgProxyService : IDisposable
    {
        private const int MaxUpstreamAttempts = 6;
        private const int FallbackAttempts = 2;
        private const int IdlePollMs = 30000;
        private const int IdleTimeoutMs = 360000;
        private const int FirstResponseMs = 10000;
        private const int FlakyDeathMs = 6000;
        private const int DirectFailCooldownMs = 60000;
        private const int FrontFailCooldownMs = 30000;
        private const int ConnectTimeoutMs = 8000;
        private const int DohTimeoutMs = 4000;

        private enum BridgeOutcome
        {
            Ok,
            DeadFront,
            FlakyDeath
        }

        private sealed class AttemptState
        {
            internal int Attempts;
            internal readonly List<string> Failures = new List<string>();
        }

        private sealed class Upstream
        {
            internal TgWebSocket Socket;
            internal string FrontId;
        }

        private readonly TgFronts _fronts = new TgFronts();
        private readonly TgDohResolver _doh = new TgDohResolver();
        private readonly ConcurrentDictionary<int, byte> _loggedOddDc =
            new ConcurrentDictionary<int, byte>();

        private byte[] _secret = TgProtocol.RandomBytes(16);
        private TcpListener _listener;
        private CancellationTokenSource _cancellation;
        private Task _acceptTask;
        private bool _loggedFirstSuccess;
        private int _connectionSequence;
        private int _loggedFragmentation;

        internal TgProxyService()
        {
            Host = "127.0.0.1";
            Port = 1443;
            PublishStatus = true;
        }

        internal string Host { get; private set; }

        internal int Port { get; private set; }

        // The CLI self-test uses an isolated instance; it must not overwrite
        // or delete the status file of a proxy running in the GUI.
        internal bool PublishStatus { get; set; }

        internal string SecretHex
        {
            get { return TgProtocol.ToHex(_secret); }
        }

        internal string ProxyLink
        {
            get
            {
                return "tg://proxy?server=" + Host + "&port=" + Port +
                       "&secret=dd" + SecretHex;
            }
        }

        internal bool IsRunning { get; private set; }

        internal string StartError { get; private set; }

        internal bool Verbose { get; set; }

        internal event Action<string> LogLine;

        internal event Action StateChanged;

        internal void Configure(int port, string secretHex)
        {
            if (port >= 0 && port <= 65535)
            {
                Port = port;
            }
            if (!string.IsNullOrEmpty(secretHex) && secretHex.Length == 32)
            {
                byte[] parsed = TgProtocol.FromHex(secretHex);
                if (parsed != null && parsed.Length == 16)
                {
                    _secret = parsed;
                }
            }
        }

        internal bool Start()
        {
            if (IsRunning)
            {
                return true;
            }
            StartError = null;

            TcpListener listener = null;
            int desired = Port >= 0 ? Port : 1443;
            int last = desired == 0 ? 0 : Math.Min(desired + 9, 65535);
            for (int candidate = desired; candidate <= last; candidate++)
            {
                try
                {
                    listener = new TcpListener(IPAddress.Loopback, candidate);
                    listener.Start();
                    IPEndPoint endpoint = listener.LocalEndpoint as IPEndPoint;
                    Port = endpoint != null ? endpoint.Port : candidate;
                    break;
                }
                catch (SocketException)
                {
                    listener = null;
                }
            }
            if (listener == null)
            {
                StartError = "Порт " + desired + " занят (проверены " +
                    desired + "–" + last + "). Закройте программу, занявшую " +
                    "порт, или укажите другой в настройках.";
                Log("[tg-proxy] " + StartError);
                OnStateChanged();
                return false;
            }
            if (desired != 0 && Port != desired)
            {
                Log("[tg-proxy] порт " + desired + " занят — взят " + Port);
            }

            _listener = listener;
            _cancellation = new CancellationTokenSource();
            _loggedFirstSuccess = false;
            IsRunning = true;
            Log("[tg-proxy] запущен на " + Host + ":" + Port +
                " (секрет dd" + SecretHex + ")");
            if (PublishStatus)
            {
                WriteStatusFile();
            }
            OnStateChanged();
            _acceptTask = AcceptLoopAsync(listener, _cancellation.Token);
            return true;
        }

        internal void Stop()
        {
            if (!IsRunning)
            {
                return;
            }
            IsRunning = false;
            if (_cancellation != null)
            {
                try
                {
                    _cancellation.Cancel();
                }
                catch (Exception)
                {
                }
            }
            if (_listener != null)
            {
                try
                {
                    _listener.Stop();
                }
                catch (Exception)
                {
                }
            }
            _listener = null;
            if (PublishStatus)
            {
                DeleteStatusFile();
            }
            Log("[tg-proxy] остановлен");
            OnStateChanged();
        }

        private async Task AcceptLoopAsync(
            TcpListener listener, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync()
                        .ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }
                Task accepted = HandleClientAsync(client, ct);
                TgAsync.Observe(accepted);
            }
        }

        private async Task HandleClientAsync(
            TcpClient client, CancellationToken ct)
        {
            TgCryptoContext context = null;
            TgWebSocket socket = null;
            TgMsgSplitter splitter = null;
            try
            {
                client.NoDelay = true;
                NetworkStream stream = client.GetStream();
                byte[] handshake = await ReadExactAsync(
                    stream, TgProtocol.HandshakeLen, ct).ConfigureAwait(false);
                TgHandshake decoded = TgProtocol.TryHandshake(handshake, _secret);
                if (decoded == null)
                {
                    return;
                }
                if (TgProtocol.IsTestDc(decoded.Dc))
                {
                    Log("[tg-proxy] клиент просит тестовый дата-центр (DC" +
                        (decoded.Dc - TgProtocol.TestDcOffset) +
                        " тестовой сети Telegram) — прокси работает только с " +
                        "обычным Telegram. Отключите тестовый режим в клиенте.");
                    return;
                }

                int dcIndex = decoded.IsMedia ? -decoded.Dc : decoded.Dc;
                uint protocol = TgProtocol.ProtoInt(decoded.ProtoTag);
                int dcKey = TgProtocol.NormalizeDc(decoded.Dc);

                if (dcKey != decoded.Dc && _loggedOddDc.TryAdd(decoded.Dc, 0))
                {
                    Log("[tg-proxy] клиент просит DC" + decoded.Dc +
                        " — такого узла у прокси нет, вместо него DC" + dcKey +
                        "; файлы именно с этого узла могут не загрузиться");
                }

                byte[] relayInit = TgProtocol.GenerateRelayInit(
                    decoded.ProtoTag, dcIndex);
                context = TgProtocol.BuildCryptoContext(
                    decoded.PrekeyIv, _secret, relayInit);

                Upstream upstream = await ConnectUpstreamAsync(
                    dcKey, decoded.IsMedia, ct).ConfigureAwait(false);
                if (upstream == null)
                {
                    return;
                }
                socket = upstream.Socket;

                splitter = new TgMsgSplitter(relayInit, protocol);
                await socket.SendAsync(relayInit, ct).ConfigureAwait(false);

                int id = Interlocked.Increment(ref _connectionSequence);
                LogDetail("[tg-proxy] #" + id + " DC" + dcKey + " " +
                    LaneName(decoded.IsMedia) + ": открыто через " +
                    (upstream.FrontId == null ? "прямой IP" : upstream.FrontId));

                BridgeOutcome outcome = await BridgeAsync(
                    stream, socket, context, splitter,
                    dcKey, id, decoded.IsMedia, ct).ConfigureAwait(false);

                if (upstream.FrontId != null)
                {
                    if (outcome == BridgeOutcome.DeadFront)
                    {
                        _fronts.MarkBad(dcKey, upstream.FrontId,
                            120000, decoded.IsMedia);
                        _loggedFirstSuccess = false;
                        Log("[tg-proxy] DC" + dcKey + ": " +
                            upstream.FrontId +
                            " не доводит трафик до Telegram — исключён на время");
                    }
                    else if (outcome == BridgeOutcome.FlakyDeath)
                    {
                        _fronts.MarkBad(dcKey, upstream.FrontId,
                            30000, decoded.IsMedia);
                        _loggedFirstSuccess = false;
                        Log("[tg-proxy] DC" + dcKey + ": " +
                            upstream.FrontId +
                            " рвёт соединение сразу — берётся другой фронт");
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
            catch (EndOfStreamException)
            {
            }
            catch (IOException)
            {
            }
            catch (Exception ex)
            {
                Log("[tg-proxy] ошибка соединения: " + ex.Message);
            }
            finally
            {
                if (socket != null)
                {
                    socket.Dispose();
                }
                if (splitter != null)
                {
                    splitter.Dispose();
                }
                if (context != null)
                {
                    context.Dispose();
                }
                try
                {
                    client.Close();
                }
                catch (Exception)
                {
                }
            }
        }

        private async Task<Upstream> ConnectUpstreamAsync(
            int dc, bool isMedia, CancellationToken ct)
        {
            AttemptState state = new AttemptState();
            Upstream upstream;
            if (isMedia)
            {
                upstream = await TryFrontsAsync(
                    dc, true, MaxUpstreamAttempts - FallbackAttempts,
                    state, ct).ConfigureAwait(false);
                if (upstream != null)
                {
                    return upstream;
                }
                int remaining = MaxUpstreamAttempts - state.Attempts;
                if (remaining < FallbackAttempts)
                {
                    remaining = FallbackAttempts;
                }
                upstream = await TryDirectAsync(
                    dc, true, remaining, state, ct).ConfigureAwait(false);
                if (upstream != null)
                {
                    return upstream;
                }
            }
            else
            {
                upstream = await TryDirectAsync(
                    dc, false, MaxUpstreamAttempts - FallbackAttempts,
                    state, ct).ConfigureAwait(false);
                if (upstream != null)
                {
                    return upstream;
                }
                int remaining = MaxUpstreamAttempts - state.Attempts;
                if (remaining < FallbackAttempts)
                {
                    remaining = FallbackAttempts;
                }
                upstream = await TryFrontsAsync(
                    dc, false, remaining, state, ct).ConfigureAwait(false);
                if (upstream != null)
                {
                    return upstream;
                }
            }

            string summary = state.Failures.Count == 0
                ? "нет доступных адресов"
                : JoinFailures(state.Failures, 4);
            Log("[tg-proxy] DC" + dc +
                ": не удалось подключиться к Telegram — " + summary);
            return null;
        }

        private async Task<Upstream> TryDirectAsync(
            int dc, bool isMedia, int budget, AttemptState state,
            CancellationToken ct)
        {
            string address;
            if (!TgProtocol.TryDirectTarget(dc, out address))
            {
                return null;
            }
            TgDirectTicket ticket = _fronts.TryBeginDirect(dc);
            if (ticket == TgDirectTicket.Skip)
            {
                return null;
            }
            try
            {
                int failuresBefore = state.Failures.Count;
                string[] domains = TgProtocol.WsDomains(dc, isMedia);
                for (int i = 0; i < domains.Length; i++)
                {
                    if (budget-- <= 0)
                    {
                        break;
                    }
                    state.Attempts++;
                    WsResult result = await TgWebSocket.ConnectAsync(
                        address, domains[i], ConnectTimeoutMs,
                        domains[i], ct).ConfigureAwait(false);
                    if (result.Ok)
                    {
                        _fronts.EndDirect(dc, ticket, true);
                        return LogSuccess(
                            dc, address + " (" + domains[i] + ")",
                            result.Socket, null);
                    }
                    state.Failures.Add(domains[i] + ": " + StageText(result));
                }
                if (state.Failures.Count > failuresBefore)
                {
                    _fronts.MarkBad(
                        dc, TgFronts.DirectKey, DirectFailCooldownMs, false);
                }
                _fronts.EndDirect(dc, ticket, false);
                return null;
            }
            catch (Exception)
            {
                _fronts.EndDirect(dc, ticket, false);
                throw;
            }
        }

        private async Task<Upstream> TryFrontsAsync(
            int dc, bool isMedia, int budget, AttemptState state,
            CancellationToken ct)
        {
            foreach (string baseDomain in _fronts.DomainsForDc(dc, isMedia))
            {
                if (budget-- <= 0)
                {
                    break;
                }
                state.Attempts++;
                // The fronts expose one hostname per data centre; media and
                // chat split by worker through the balancer's ordering.
                string domain = "kws" + dc + "." + baseDomain;
                string host;
                string via;
                string[] dohAddresses = await Task.Run(delegate()
                {
                    return _doh.Resolve(domain, DohTimeoutMs);
                }).ConfigureAwait(false);
                if (dohAddresses.Length > 0)
                {
                    host = dohAddresses[0];
                    via = "DoH " + host;
                }
                else
                {
                    string osAddress = TryOsResolve(domain);
                    if (osAddress == null)
                    {
                        state.Failures.Add(domain + ": DNS не резолвится");
                        continue;
                    }
                    host = osAddress;
                    via = "DNS " + host;
                }

                WsResult result = await TgWebSocket.ConnectAsync(
                    host, domain, ConnectTimeoutMs, domain, ct)
                    .ConfigureAwait(false);
                if (result.Ok)
                {
                    return LogSuccess(
                        dc, domain + " (" + via + ")",
                        result.Socket, baseDomain);
                }
                _fronts.MarkBad(
                    dc, baseDomain,
                    result.Status == 429 ? 45000 : FrontFailCooldownMs,
                    isMedia);
                state.Failures.Add(
                    domain + " (" + via + "): " + StageText(result));
            }
            return null;
        }

        private Upstream LogSuccess(
            int dc, string via, TgWebSocket socket, string frontId)
        {
            if (!_loggedFirstSuccess)
            {
                _loggedFirstSuccess = true;
                // A WebSocket 101 is not proof that Telegram's traffic flows;
                // the bridge logs "трафик пошёл" on the first real answer.
                Log("[tg-proxy] канал до Telegram открыт (DC" + dc +
                    " через " + via + ") — проверка потока");
            }
            Upstream upstream = new Upstream();
            upstream.Socket = socket;
            upstream.FrontId = frontId;
            return upstream;
        }

        private static string StageText(WsResult result)
        {
            if (result.Stage == WsStage.Tcp)
            {
                return "TCP не открылся (таймаут / блок IP)";
            }
            if (result.Stage == WsStage.Tls)
            {
                return "TLS оборван (DPI по SNI?)";
            }
            if (result.Stage == WsStage.Upgrade)
            {
                return "WebSocket-ответ " + result.Status + ", не 101";
            }
            return "ошибка";
        }

        private async Task<BridgeOutcome> BridgeAsync(
            NetworkStream client,
            TgWebSocket socket,
            TgCryptoContext context,
            TgMsgSplitter splitter,
            int dc,
            int id,
            bool isMedia,
            CancellationToken outerToken)
        {
            CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(outerToken);
            CancellationToken ct = linked.Token;
            long lastActivity = TgClock.NowMs;
            long[] clientSpokeAt = new long[1];
            int[] telegramResponded = new int[1];
            int[] clientClosed = new int[1];
            bool[] deadFront = new bool[1];
            long startTick = TgClock.NowMs;
            string[] endCause = new string[1];

            Action<string> endedBy = delegate(string cause)
            {
                if (endCause[0] == null)
                {
                    endCause[0] = cause;
                }
            };

            Func<Task> clientToTelegram = async delegate()
            {
                byte[] buffer = new byte[65536];
                try
                {
                    while (true)
                    {
                        int read = await client.ReadAsync(
                            buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                        Interlocked.Exchange(
                            ref lastActivity, TgClock.NowMs);
                        if (read == 0)
                        {
                            Interlocked.Exchange(ref clientClosed[0], 1);
                            endedBy("Telegram Desktop закрыл соединение");
                            List<byte[]> tail = splitter.Flush();
                            if (tail.Count > 0)
                            {
                                await socket.SendAsync(tail[0], ct)
                                    .ConfigureAwait(false);
                            }
                            break;
                        }
                        if (Interlocked.Read(ref clientSpokeAt[0]) == 0)
                        {
                            Interlocked.Exchange(
                                ref clientSpokeAt[0], TgClock.NowMs);
                        }
                        byte[] plain = context.ClientDecrypt.Update(
                            TgProtocol.Slice(buffer, 0, read));
                        byte[] encrypted = context.TelegramEncrypt.Update(plain);
                        List<byte[]> parts = splitter.Split(encrypted);
                        if (parts.Count == 0)
                        {
                            continue;
                        }
                        if (parts.Count > 1)
                        {
                            await socket.SendBatchAsync(parts, ct)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            await socket.SendAsync(parts[0], ct)
                                .ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception)
                {
                    endedBy("обрыв на стороне Telegram Desktop");
                }
                finally
                {
                    CancelQuietly(linked);
                }
            };

            Func<Task> telegramToClient = async delegate()
            {
                try
                {
                    while (true)
                    {
                        byte[] data = await socket.RecvAsync(ct)
                            .ConfigureAwait(false);
                        if (data == null)
                        {
                            endedBy("Telegram закрыл канал");
                            break;
                        }
                        if (Interlocked.Exchange(
                                ref telegramResponded[0], 1) == 0)
                        {
                            LogDetail("[tg-proxy] #" + id + " DC" + dc +
                                ": трафик пошёл");
                        }
                        Interlocked.Exchange(
                            ref lastActivity, TgClock.NowMs);
                        byte[] plain = context.TelegramDecrypt.Update(data);
                        byte[] encrypted = context.ClientEncrypt.Update(plain);
                        await client.WriteAsync(
                            encrypted, 0, encrypted.Length, ct)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception)
                {
                    endedBy("обрыв канала до Telegram");
                }
                finally
                {
                    CancelQuietly(linked);
                }
            };

            Func<Task> idleWatch = async delegate()
            {
                try
                {
                    while (TgClock.NowMs -
                           Interlocked.Read(ref lastActivity) <= IdleTimeoutMs)
                    {
                        await Task.Delay(IdlePollMs, ct).ConfigureAwait(false);
                    }
                    endedBy("молчало " + (IdleTimeoutMs / 60000) + " мин");
                }
                catch (Exception)
                {
                }
                finally
                {
                    CancelQuietly(linked);
                }
            };

            Func<Task> firstResponseWatch = async delegate()
            {
                try
                {
                    while (Interlocked.CompareExchange(
                               ref telegramResponded[0], 0, 0) == 0)
                    {
                        await Task.Delay(1500, ct).ConfigureAwait(false);
                        if (Interlocked.CompareExchange(
                                ref telegramResponded[0], 0, 0) != 0)
                        {
                            return;
                        }
                        long spoke = Interlocked.Read(ref clientSpokeAt[0]);
                        if (spoke != 0 &&
                            TgClock.NowMs - spoke > FirstResponseMs)
                        {
                            deadFront[0] = true;
                            endedBy("фронт не довёл трафик до Telegram");
                            CancelQuietly(linked);
                            return;
                        }
                    }
                }
                catch (Exception)
                {
                }
            };

            await Task.WhenAll(
                clientToTelegram(), telegramToClient(),
                idleWatch(), firstResponseWatch()).ConfigureAwait(false);
            try
            {
                linked.Dispose();
            }
            catch (Exception)
            {
            }

            long lifetime = TgClock.NowMs - startTick;
            bool relayed =
                Interlocked.CompareExchange(
                    ref telegramResponded[0], 0, 0) != 0;
            bool stopping = outerToken.IsCancellationRequested;
            if (relayed)
            {
                string cause = stopping
                    ? "остановка прокси"
                    : (endCause[0] == null ? "разрыв" : endCause[0]);
                LogDetail("[tg-proxy] #" + id + " DC" + dc + " " +
                    LaneName(isMedia) + ": закрыто через " +
                    (lifetime / 1000.0).ToString("0.0") + " с — " + cause +
                    "; принято " + HumanBytes(socket.BytesIn) +
                    ", отправлено " + HumanBytes(socket.BytesOut));
            }
            if (socket.AssembledMessages > 0 &&
                Interlocked.Exchange(ref _loggedFragmentation, 1) == 0)
            {
                Log("[tg-proxy] канал дробит крупные сообщения — они " +
                    "собираются обратно (первых собрано: " +
                    socket.AssembledMessages + ")");
            }

            if (stopping)
            {
                return BridgeOutcome.Ok;
            }
            if (deadFront[0])
            {
                return BridgeOutcome.DeadFront;
            }
            if (relayed && Interlocked.CompareExchange(
                    ref clientClosed[0], 0, 0) == 0 &&
                lifetime < FlakyDeathMs)
            {
                return BridgeOutcome.FlakyDeath;
            }
            return BridgeOutcome.Ok;
        }

        // Probes the upstream paths (DoH, system DNS, direct IP, Cloudflare
        // fronts) and returns a short verdict. Does not need the listener.
        internal async Task<string> SelfTestAsync(CancellationToken ct)
        {
            Log("[tg-proxy] ── проверка соединения с Telegram ──");
            const int dc = 2;
            bool anyOk = false;
            string okVia = string.Empty;

            string[] baseDomains = TgFronts.AllBaseDomains;
            string probe = "kws" + dc + "." + baseDomains[0];
            string[] dohAddresses = await Task.Run(delegate()
            {
                return _doh.Resolve(probe, DohTimeoutMs);
            }).ConfigureAwait(false);
            Log(dohAddresses.Length > 0
                ? "[tg-proxy] DoH (1.1.1.1 / 8.8.8.8): доступен (" + probe +
                  " → " + dohAddresses[0] + ")"
                : "[tg-proxy] DoH (1.1.1.1 / 8.8.8.8): недоступен — сеть, " +
                  "похоже, режет и его");
            string osAddress = TryOsResolve(probe);
            if (osAddress == null && dohAddresses.Length > 0)
            {
                Log("[tg-proxy] системный DNS: фронт-домен не резолвится — " +
                    "вероятно отравление DNS у провайдера (DoH это обходит)");
            }
            else if (osAddress != null)
            {
                Log("[tg-proxy] системный DNS: работает (" + probe + " → " +
                    osAddress + ")");
            }

            string directAddress;
            if (TgProtocol.TryDirectTarget(dc, out directAddress))
            {
                string domain = TgProtocol.WsDomains(dc, false)[0];
                WsResult result = await TgWebSocket.ConnectAsync(
                    directAddress, domain, ConnectTimeoutMs, domain, ct)
                    .ConfigureAwait(false);
                if (!result.Ok)
                {
                    Log("[tg-proxy] прямой IP " + directAddress + " (SNI " +
                        domain + "): " + StageText(result));
                    // Warm the balancer: the bridge self-test and the first
                    // real connections should not pay the blocked direct path
                    // again when a working front is available.
                    _fronts.MarkBad(
                        dc, TgFronts.DirectKey, DirectFailCooldownMs, false);
                }
                else
                {
                    bool relays = await TgProtocol.ProbeRelayAsync(
                        result.Socket, dc, 5000, ct).ConfigureAwait(false);
                    result.Socket.Dispose();
                    Log(relays
                        ? "[tg-proxy] прямой IP " + directAddress +
                          ": OK (Telegram отвечает)"
                        : "[tg-proxy] прямой IP " + directAddress +
                          ": WS есть, но Telegram молчит " +
                          "(путь не доводит до DC)");
                    if (relays)
                    {
                        anyOk = true;
                        okVia = "прямой IP " + directAddress;
                    }
                    else
                    {
                        _fronts.MarkBad(
                            dc, TgFronts.DirectKey,
                            DirectFailCooldownMs, false);
                    }
                }
            }

            for (int i = 0; i < baseDomains.Length && i < 3; i++)
            {
                string domain = "kws" + dc + "." + baseDomains[i];
                string[] addresses = await Task.Run(delegate()
                {
                    return _doh.Resolve(domain, DohTimeoutMs);
                }).ConfigureAwait(false);
                string host = addresses.Length > 0
                    ? addresses[0]
                    : (TryOsResolve(domain) ?? domain);
                WsResult result = await TgWebSocket.ConnectAsync(
                    host, domain, ConnectTimeoutMs, domain, ct)
                    .ConfigureAwait(false);
                if (!result.Ok)
                {
                    Log("[tg-proxy] фронт " + domain + ": " +
                        StageText(result));
                    _fronts.MarkBad(
                        dc, baseDomains[i], FrontFailCooldownMs, false);
                    continue;
                }
                bool relays = await TgProtocol.ProbeRelayAsync(
                    result.Socket, dc, 5000, ct).ConfigureAwait(false);
                result.Socket.Dispose();
                Log(relays
                    ? "[tg-proxy] фронт " + domain +
                      ": OK (Telegram отвечает)"
                    : "[tg-proxy] фронт " + domain +
                      ": WS есть, но Telegram молчит (мёртвый фронт)");
                if (relays)
                {
                    if (!anyOk)
                    {
                        anyOk = true;
                        okVia = domain;
                    }
                    break;
                }
            }

            string verdict = anyOk
                ? "РАБОТАЕТ: найден путь до Telegram (" + okVia +
                  "). Если Telegram всё равно не грузит — переподключите " +
                  "прокси в приложении Telegram."
                : "НЕ РАБОТАЕТ: до Telegram не достучаться на этой сети. " +
                  "Подробности — в журнале (DNS → поможет DoH; TLS/IP → " +
                  "провайдер режет Cloudflare).";
            Log("[tg-proxy] итог: " + verdict);
            return verdict;
        }

        // Drives the real bridge from a loopback client: sends req_pq through
        // the proxy and checks that Telegram's resPQ survives the round trip
        // decodable. This separates a bridge bug from a network block.
        internal async Task<string> BridgeSelfTestAsync(
            int dcIndex, CancellationToken ct)
        {
            if (!IsRunning)
            {
                return "тест моста: прокси не запущен";
            }
            return await ProbeBridgeAsync(
                Host, Port, _secret, dcIndex, ct, Log).ConfigureAwait(false);
        }

        // Same loopback probe against any local proxy: used by the GUI test
        // and by `--tg-bridge`, which verifies the proxy already running in
        // another process without touching Telegram Desktop.
        internal static async Task<string> ProbeBridgeAsync(
            string host, int port, byte[] secret, int dcIndex,
            CancellationToken ct, Action<string> log)
        {
            LogTo(log, "[tg-proxy] ── тест моста, полоса «" +
                LaneName(dcIndex < 0) + "» (loopback-клиент через мост) ──");
            TcpClient client = new TcpClient();
            try
            {
                client.NoDelay = true;
                Task connect = client.ConnectAsync(host, port);
                if (!await TgAsync.WaitAsync(
                        connect, 5000, ct).ConfigureAwait(false))
                {
                    return "тест моста: нет подключения к " + host + ":" + port;
                }
                NetworkStream stream = client.GetStream();

                byte[] protoTag = TgProtocol.ProtoTagSecure;
                byte[] init = TgProtocol.GenerateClientInit(
                    protoTag, dcIndex, secret);
                byte[] prekeyIv = TgProtocol.Slice(
                    init, TgProtocol.SkipLen, TgProtocol.PrekeyLen +
                    TgProtocol.IvLen);
                byte[] reversed = TgProtocol.Reversed(prekeyIv);
                TgAesCtr send = new TgAesCtr(
                    TgProtocol.DeriveKey(
                        TgProtocol.Slice(prekeyIv, 0, TgProtocol.PrekeyLen),
                        secret),
                    TgProtocol.Slice(
                        prekeyIv, TgProtocol.PrekeyLen, TgProtocol.IvLen));
                TgAesCtr receive = new TgAesCtr(
                    TgProtocol.DeriveKey(
                        TgProtocol.Slice(reversed, 0, TgProtocol.PrekeyLen),
                        secret),
                    TgProtocol.Slice(
                        reversed, TgProtocol.PrekeyLen, TgProtocol.IvLen));
                send.Update(new byte[TgProtocol.HandshakeLen]);

                await stream.WriteAsync(init, 0, init.Length, ct)
                    .ConfigureAwait(false);

                byte[] message = new byte[8 + 8 + 4 + 20];
                long messageId = TgProtocol.CurrentTelegramMessageId();
                TgProtocol.WriteInt64LittleEndian(message, 8, messageId);
                TgProtocol.WriteInt32LittleEndian(message, 16, 20);
                TgProtocol.WriteUInt32LittleEndian(message, 20, 0xbe7e8ef1);
                Buffer.BlockCopy(
                    TgProtocol.RandomBytes(16), 0, message, 24, 16);
                byte[] framed = new byte[4 + message.Length];
                TgProtocol.WriteInt32LittleEndian(framed, 0, message.Length);
                Buffer.BlockCopy(message, 0, framed, 4, message.Length);
                byte[] encrypted = send.Update(framed);
                await stream.WriteAsync(encrypted, 0, encrypted.Length, ct)
                    .ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);

                List<byte> plain = new List<byte>();
                byte[] buffer = new byte[2048];
                System.Diagnostics.Stopwatch watch =
                    System.Diagnostics.Stopwatch.StartNew();
                while (plain.Count < 28 && watch.ElapsedMilliseconds < 9000)
                {
                    Task<int> read = stream.ReadAsync(buffer, 0, buffer.Length, ct);
                    if (!await TgAsync.WaitAsync(
                            read, 9000 - (int)watch.ElapsedMilliseconds, ct)
                            .ConfigureAwait(false))
                    {
                        break;
                    }
                    int count = await read.ConfigureAwait(false);
                    if (count <= 0)
                    {
                        break;
                    }
                    plain.AddRange(receive.Update(
                        TgProtocol.Slice(buffer, 0, count)));
                }

                string verdict;
                if (plain.Count < 28)
                {
                    verdict = "МОСТ: resPQ не дошёл (" + plain.Count +
                        "б) — мост не донёс ответ Telegram до клиента";
                }
                else
                {
                    byte[] bytes = plain.ToArray();
                    uint constructor = (uint)TgProtocol.ReadInt32LittleEndian(
                        bytes, 24);
                    verdict = constructor == 0x05162463
                        ? "МОСТ OK: resPQ прошёл через мост неповреждённым — " +
                          "re-encryption и splitter исправны"
                        : "МОСТ ПОВРЕЖДЁН: ждал resPQ 0x05162463, пришло 0x" +
                          constructor.ToString("x8") +
                          " — баг в мосте (не сеть!)";
                }
                LogTo(log, "[tg-proxy] " + verdict);
                return verdict;
            }
            catch (Exception ex)
            {
                return "тест моста: ошибка " + ex.Message;
            }
            finally
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

        private static void LogTo(Action<string> log, string message)
        {
            if (log != null)
            {
                log(message);
            }
        }

        private static string TryOsResolve(string host)
        {
            try
            {
                IPAddress[] addresses = Dns.GetHostAddresses(host);
                for (int i = 0; i < addresses.Length; i++)
                {
                    if (addresses[i].AddressFamily ==
                        AddressFamily.InterNetwork)
                    {
                        return addresses[i].ToString();
                    }
                }
                return addresses.Length > 0 ? addresses[0].ToString() : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static async Task<byte[]> ReadExactAsync(
            Stream stream, int length, CancellationToken ct)
        {
            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = await stream.ReadAsync(
                    buffer, offset, length - offset, ct).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new EndOfStreamException();
                }
                offset += read;
            }
            return buffer;
        }

        private static string LaneName(bool isMedia)
        {
            return isMedia ? "медиа" : "переписка";
        }

        private static string HumanBytes(long bytes)
        {
            if (bytes >= 1024L * 1024)
            {
                return (bytes / 1024.0 / 1024.0).ToString("0.0") + " МБ";
            }
            if (bytes >= 1024)
            {
                return (bytes / 1024.0).ToString("0") + " КБ";
            }
            return bytes + " Б";
        }

        private static string JoinFailures(List<string> failures, int limit)
        {
            StringBuilder builder = new StringBuilder();
            int count = failures.Count < limit ? failures.Count : limit;
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    builder.Append("; ");
                }
                builder.Append(failures[i]);
            }
            return builder.ToString();
        }

        private static void CancelQuietly(CancellationTokenSource source)
        {
            try
            {
                source.Cancel();
            }
            catch (Exception)
            {
            }
        }

        private void Log(string message)
        {
            Action<string> handler = LogLine;
            if (handler != null)
            {
                handler(message);
            }
        }

        private void LogDetail(string message)
        {
            if (Verbose)
            {
                Log(message);
            }
        }

        private void OnStateChanged()
        {
            Action handler = StateChanged;
            if (handler != null)
            {
                handler();
            }
        }

        internal static string StatusDirectory
        {
            get { return Path.Combine(AppPaths.DataDirectory, "proxy"); }
        }

        // Status is per process: a second instance (or a CLI self-test) must
        // not overwrite or delete the status of a proxy already running in the
        // GUI. The old single file is still read for compatibility.
        internal static string LegacyStatusPath
        {
            get { return Path.Combine(AppPaths.DataDirectory, "tg-proxy.json"); }
        }

        private static string OwnStatusPath()
        {
            return Path.Combine(
                StatusDirectory,
                "tg-proxy-" + Process.GetCurrentProcess().Id + ".json");
        }

        private void WriteStatusFile()
        {
            try
            {
                AppPaths.EnsureDirectory(StatusDirectory);
                Dictionary<string, object> data =
                    new Dictionary<string, object>();
                data["port"] = Port;
                data["pid"] = Process.GetCurrentProcess().Id;
                data["secret"] = "dd" + SecretHex;
                data["started_utc"] = DateTime.UtcNow.ToString("o");
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                AtomicFile.WriteAllText(
                    OwnStatusPath(), serializer.Serialize(data));
            }
            catch (Exception)
            {
            }
        }

        private void DeleteStatusFile()
        {
            try
            {
                string path = OwnStatusPath();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
            }
        }

        internal static TgProxyStatusInfo ReadStatusFile()
        {
            TgProxyStatusInfo best = null;
            string bestStarted = null;
            try
            {
                if (Directory.Exists(StatusDirectory))
                {
                    string[] files = Directory.GetFiles(
                        StatusDirectory, "tg-proxy-*.json");
                    for (int i = 0; i < files.Length; i++)
                    {
                        TgProxyStatusInfo info = ParseStatusFile(files[i]);
                        if (!info.Running)
                        {
                            continue;
                        }
                        if (best == null ||
                            string.CompareOrdinal(
                                info.StartedUtc, bestStarted) > 0)
                        {
                            best = info;
                            bestStarted = info.StartedUtc;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            if (best != null)
            {
                return best;
            }
            TgProxyStatusInfo legacy = ParseStatusFile(LegacyStatusPath);
            return legacy.Running ? legacy : new TgProxyStatusInfo();
        }

        private static TgProxyStatusInfo ParseStatusFile(string path)
        {
            TgProxyStatusInfo info = new TgProxyStatusInfo();
            try
            {
                if (!File.Exists(path))
                {
                    return info;
                }
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                Dictionary<string, object> data =
                    serializer.DeserializeObject(
                        File.ReadAllText(path, Encoding.UTF8))
                        as Dictionary<string, object>;
                if (data == null)
                {
                    return info;
                }
                object value;
                if (data.TryGetValue("port", out value))
                {
                    info.Port = Convert.ToInt32(value);
                }
                if (data.TryGetValue("pid", out value))
                {
                    info.Pid = Convert.ToInt32(value);
                }
                if (data.TryGetValue("secret", out value))
                {
                    info.Secret = value as string;
                }
                if (data.TryGetValue("started_utc", out value))
                {
                    info.StartedUtc = value as string;
                }
                info.StartedUtc = info.StartedUtc ?? string.Empty;
                if (info.Port > 0 && info.Port <= 65535)
                {
                    info.Running = IsListening(info.Port);
                }
                return info;
            }
            catch (Exception)
            {
                return info;
            }
        }

        private static bool IsListening(int port)
        {
            TcpClient probe = new TcpClient();
            try
            {
                IAsyncResult result = probe.BeginConnect(
                    IPAddress.Loopback, port, null, null);
                if (!result.AsyncWaitHandle.WaitOne(500))
                {
                    return false;
                }
                probe.EndConnect(result);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                try
                {
                    probe.Close();
                }
                catch (Exception)
                {
                }
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
