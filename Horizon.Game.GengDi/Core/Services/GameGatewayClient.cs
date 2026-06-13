using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using Horizon.Core.Security;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;

using MemoryPack;

using TouchSocket.Core;
using TouchSocket.Sockets;

using TouchSocketTcpClient = TouchSocket.Sockets.TcpClient;

namespace Horizon.Game.GengDi.Core.Services
{
    /// <summary>
    /// Game Gateway 客户端。
    /// 使用持久化共享连接和请求-响应关联，向 Horizon.Game.Gateway 发送消息并等待响应。
    /// </summary>
    internal sealed class GameGatewayClient : IDisposable
    {
        private const int ConnectTimeoutSeconds = 8;
        private const int RequestTimeoutSeconds = 15;
        private const int MaxRetryAttempts = 2;

        private static readonly GameGatewayClient s_instance = new();

        private readonly SemaphoreSlim _connectionGate = new(1, 1);
        private readonly ConcurrentDictionary<string, PendingRequest> _pendingRequests = new(StringComparer.Ordinal);

        private GameGatewaySharedClient _sharedClient;
        private volatile bool _isConnected;
        private volatile bool _disposed;

        public static GameGatewayClient Instance => s_instance;

        private GameGatewayClient() { }

        /// <summary>
        /// 通过 Game Gateway 为指定通行证用户构建游戏内用户记录。
        /// 返回游戏用户 ID，失败时返回 0。
        /// </summary>
        public async Task<long> BuildGameUserAsync(
            string passportId,
            int gameId,
            int areaId,
            int serverId,
            string authToken,
            CancellationToken cancellationToken = default)
        {
            var request = new BuildGameUserRequest
            {
                PassportId = passportId ?? string.Empty,
                GameId = gameId,
                AreaId = areaId,
                ServerId = serverId,
                PlatformId = Environment.OSVersion.Platform.ToString(),
                ServiceType = ServiceType.Account
            };

            try
            {
                var response = await SendAsync<BuildGameUserResponse>(request, authToken, cancellationToken)
                    .ConfigureAwait(false);

                if (response?.IsSuccess != true)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[GameGatewayClient] BuildGameUser响应失败: {response?.ErrorMessage ?? "unknown"}");
                }

                return response?.IsSuccess == true ? response.GameUserId : 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GameGatewayClient] BuildGameUserAsync失败: {ex}");
                return 0;
            }
        }

        private async Task<TResponse> SendAsync<TResponse>(
            MessageUnion message,
            string authToken,
            CancellationToken cancellationToken)
            where TResponse : MessageUnion
        {
            Exception lastException = null;

            for (var attempt = 0; attempt <= MaxRetryAttempts; attempt++)
            {
                if (attempt > 0)
                {
                    var backoffMs = Math.Min(500 * (1 << (attempt - 1)), 2000);
                    await Task.Delay(backoffMs, cancellationToken).ConfigureAwait(false);
                }

                var packet = CreatePacket(message, authToken);
                var messageId = packet.Header.MessageId;
                var pending = new PendingRequest();

                _pendingRequests[messageId] = pending;
                try
                {
                    var client = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
                    var frame = PackPacket(packet);

                    var timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds);

                    await client.SendAsync(frame).WaitAsync(timeout, cancellationToken).ConfigureAwait(false);

                    var responsePacket = await pending.ResponseSource.Task
                        .WaitAsync(timeout, cancellationToken).ConfigureAwait(false);

                    if (responsePacket.Body is AuthenticationError authError)
                    {
                        throw new InvalidOperationException(
                            string.IsNullOrWhiteSpace(authError.ErrorMessage)
                                ? authError.ErrorDetails
                                : authError.ErrorMessage);
                    }

                    if (responsePacket.Body is ErrorMessage error)
                    {
                        throw new InvalidOperationException(
                            string.IsNullOrWhiteSpace(error.Message)
                                ? error.Details
                                : error.Message);
                    }

                    if (responsePacket.Body is TResponse response)
                    {
                        return response;
                    }

                    throw new InvalidOperationException(
                        $"Game 网关返回了意外的响应类型：{responsePacket.Body?.GetType().Name ?? "unknown"}。");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (ObjectDisposedException)
                {
                    throw;
                }
                catch (TimeoutException ex)
                {
                    lastException = ex;
                    InvalidateSharedClient();
                }
                catch (OperationCanceledException ex)
                {
                    lastException = ex;
                    InvalidateSharedClient();
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    InvalidateSharedClient();
                }
                finally
                {
                    _pendingRequests.TryRemove(messageId, out _);
                }
            }

            if (lastException is TimeoutException || lastException is OperationCanceledException)
            {
                throw new TimeoutException(
                    $"Game 网关请求超时（{RequestTimeoutSeconds}秒），已重试 {MaxRetryAttempts + 1} 次仍未完成。请检查网关服务状态和网络连接。",
                    lastException);
            }

            throw new InvalidOperationException(
                $"Game 网关请求失败，已重试 {MaxRetryAttempts + 1} 次仍无法完成。请检查网关服务状态和网络连接。",
                lastException);
        }

        private async Task<GameGatewaySharedClient> EnsureConnectedAsync(CancellationToken cancellationToken)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(GameGatewayClient));
            }

            var client = _sharedClient;
            if (client != null && _isConnected)
            {
                return client;
            }

            try
            {
                await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                throw new ObjectDisposedException(nameof(GameGatewayClient));
            }

            try
            {
                client = _sharedClient;
                if (client != null && _isConnected)
                {
                    return client;
                }

                var staleClient = _sharedClient;
                _sharedClient = null;
                _isConnected = false;
                if (staleClient != null)
                {
                    staleClient.Closed = null;
                    staleClient.Dispose();
                }

                var newClient = new GameGatewaySharedClient();
                newClient.Received = (_, e) =>
                {
                    OnDataReceived(e);
                    return Task.CompletedTask;
                };
                newClient.Closed = (_, e) =>
                {
                    _isConnected = false;
                    FailAllPendingRequests(
                        string.IsNullOrWhiteSpace(e.Message)
                            ? "Game 网关连接已断开。"
                            : $"Game 网关连接已断开：{e.Message}");
                    return Task.CompletedTask;
                };

                var connectTimeout = TimeSpan.FromSeconds(ConnectTimeoutSeconds);

                var endpoint = ResolveEndpoint();

                try
                {
                    await newClient
                        .SetupAsync(new TouchSocketConfig()
                            .SetRemoteIPHost(endpoint)
                            .SetTcpDataHandlingAdapter(() => new GameGatewayMessageAdapter()))
                        .ConfigureAwait(false);

                    await newClient.ConnectAsync().WaitAsync(connectTimeout, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    newClient.Dispose();
                    throw;
                }
                catch (Exception ex)
                {
                    newClient.Dispose();
                    throw new InvalidOperationException(
                        $"Game 网关不可用（目标: {endpoint}），请确认 Horizon.Game.Gateway 已启动且地址配置正确。",
                        ex);
                }

                _sharedClient = newClient;
                _isConnected = true;
                return newClient;
            }
            finally
            {
                try
                {
                    _connectionGate.Release();
                }
                catch (ObjectDisposedException) { }
            }
        }

        private void OnDataReceived(ReceivedDataEventArgs e)
        {
            if (e.RequestInfo is not GameGatewayMessageInfo requestInfo || requestInfo.Packet == null)
            {
                return;
            }

            var packet = requestInfo.Packet;

            if (packet.Header?.IsResponse == true
                && !string.IsNullOrEmpty(packet.Header.ResponseToMessageId)
                && _pendingRequests.TryRemove(packet.Header.ResponseToMessageId, out var pending))
            {
                pending.ResponseSource.TrySetResult(packet);
            }
        }

        private void InvalidateSharedClient()
        {
            _isConnected = false;
        }

        private void FailAllPendingRequests(string reason)
        {
            foreach (var kvp in _pendingRequests)
            {
                if (_pendingRequests.TryRemove(kvp.Key, out var pending))
                {
                    pending.ResponseSource.TrySetException(new InvalidOperationException(reason));
                }
            }
        }

        private static HorizonMessagePacket CreatePacket(MessageUnion message, string authToken)
        {
            var messageType = ((INetworkMessage)message).Type;

            var header = new MessageHeader
            {
                MessageType = messageType,
                ServiceType = ((INetworkMessage)message).ServiceType,
                IsResponse = false,
                RequireResponse = true,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                GameId = 1,
                ZoneId = 1,
                ServerId = 1,
                AuthToken = authToken ?? string.Empty,
                MachineId = MachineIdentifier.GetMachineGuid()
            };

            var rawData = MemoryPackSerializer.Serialize(message);
            var packet = new HorizonMessagePacket
            {
                Header = header,
                ServiceType = ((INetworkMessage)message).ServiceType,
                Body = message,
                RawData = rawData
            };

            return packet;
        }

        private static byte[] PackPacket(HorizonMessagePacket packet, bool compress = true)
        {
            const int headerLength = 8;

            var packetBytes = MemoryPackSerializer.Serialize(packet);
            byte[] payload = packetBytes;
            bool isCompressed = false;

            if (compress && packetBytes.Length > 256)
            {
                var compressed = K4os.Compression.LZ4.LZ4Pickler.Pickle(packetBytes);
                if (compressed.Length < packetBytes.Length)
                {
                    payload = compressed;
                    isCompressed = true;
                }
            }

            var frame = new byte[headerLength + payload.Length];
            BitConverter.GetBytes(payload.Length).CopyTo(frame, 0);
            frame[4] = (byte)packet.Header.MessageType;
            frame[5] = isCompressed ? (byte)1 : (byte)0;
            BitConverter.GetBytes(CalculateChecksum(payload)).CopyTo(frame, 6);
            Array.Copy(payload, 0, frame, headerLength, payload.Length);
            return frame;
        }

        private static ushort CalculateChecksum(ReadOnlySpan<byte> data)
        {
            uint checksum = 0;
            foreach (var b in data)
            {
                checksum += b;
            }
            return (ushort)(checksum & 0xFFFF);
        }

        private static string ResolveHost()
        {
            var discovered = GatewayDiscoveryService.GameGateway;
            if (discovered != null && !string.IsNullOrWhiteSpace(discovered.Host))
            {
                return discovered.Host;
            }
            return GatewayDiscoveryService.GetGameGatewayHost();
        }

        private static int ResolvePort()
        {
            var discovered = GatewayDiscoveryService.GameGateway;
            if (discovered != null && discovered.Port > 0)
            {
                return discovered.Port;
            }
            return GatewayDiscoveryService.GetGameGatewayPort();
        }

        private static string ResolveEndpoint()
        {
            return $"{ResolveHost()}:{ResolvePort()}";
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            FailAllPendingRequests("客户端已释放。");
            _sharedClient?.Dispose();
            _sharedClient = null;
            _isConnected = false;
            _connectionGate.Dispose();
        }

        // ---- Private inner types ----

        private sealed class GameGatewaySharedClient : TouchSocketTcpClient
        {
        }

        /// <summary>
        /// Game Gateway 消息适配器（客户端侧），解析与服务端 HorizonMessageAdapter 相同的帧格式。
        /// </summary>
        private sealed class GameGatewayMessageAdapter : CustomFixedHeaderDataHandlingAdapter<GameGatewayMessageInfo>
        {
            public override int HeaderLength => 8;

            protected override GameGatewayMessageInfo GetInstance() => new();
        }

        private sealed class GameGatewayMessageInfo : IFixedHeaderRequestInfo
        {
            private bool _isCompressed;
            private ushort _expectedChecksum;

            public int BodyLength { get; set; }
            public byte[] Body { get; set; }
            public HorizonMessagePacket Packet { get; set; }
            public int MaxLength => 1024 * 1024;

            public bool TryBuild(ReadOnlySequence<byte> buffer, int length, out IRequestInfo requestInfo)
            {
                requestInfo = default!;
                try
                {
                    if (buffer.Length < 8)
                    {
                        return false;
                    }

                    var reader = new SequenceReader<byte>(buffer);
                    if (!reader.TryReadLittleEndian(out int payloadLength))
                    {
                        return false;
                    }

                    if (buffer.Length < 8 + payloadLength)
                    {
                        return false;
                    }

                    if (!reader.TryRead(out _) || !reader.TryRead(out byte compressedFlag))
                    {
                        return false;
                    }

                    var isCompressed = compressedFlag != 0;

                    if (!reader.TryReadLittleEndian(out short checksum))
                    {
                        return false;
                    }

                    var payload = buffer.Slice(8, payloadLength).ToArray();
                    var finalPayload = isCompressed
                        ? K4os.Compression.LZ4.LZ4Pickler.Unpickle(payload)
                        : payload;

                    var packet = MemoryPackSerializer.Deserialize<HorizonMessagePacket>(finalPayload);
                    if (packet == null)
                    {
                        return false;
                    }

                    packet.RawData = payload;

                    requestInfo = new GameGatewayMessageInfo
                    {
                        Body = payload,
                        BodyLength = payloadLength,
                        Packet = packet
                    };

                    return true;
                }
                catch
                {
                    return false;
                }
            }

            public bool TryBuild(ReadOnlySequence<byte> buffer, out IRequestInfo requestInfo)
                => TryBuild(buffer, (int)buffer.Length, out requestInfo);

            public bool OnParsingHeader(ReadOnlySpan<byte> header)
            {
                if (header.Length < 8)
                {
                    return false;
                }

                try
                {
                    var bodyLength = BitConverter.ToInt32(header);
                    if (bodyLength <= 0 || bodyLength > MaxLength)
                    {
                        return false;
                    }

                    BodyLength = bodyLength;
                    _isCompressed = header[5] != 0;
                    _expectedChecksum = BitConverter.ToUInt16(header.Slice(6, 2));
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            public bool OnParsingBody(ReadOnlySpan<byte> body)
            {
                if (body.Length != BodyLength)
                {
                    return false;
                }

                try
                {
                    var checksum = GameGatewayClient.CalculateChecksum(body);
                    if (checksum != _expectedChecksum)
                    {
                        return false;
                    }

                    Body = body.ToArray();
                    var finalPayload = _isCompressed
                        ? K4os.Compression.LZ4.LZ4Pickler.Unpickle(body)
                        : Body;

                    Packet = MemoryPackSerializer.Deserialize<HorizonMessagePacket>(finalPayload);
                    if (Packet != null)
                    {
                        Packet.RawData = Body;
                    }

                    return Packet != null;
                }
                catch
                {
                    return false;
                }
            }

            public void Build<TByteBlock>(ref TByteBlock byteBlock) where TByteBlock : IByteBlock
            {
                if (Packet == null)
                {
                    return;
                }

                var body = MemoryPackSerializer.Serialize(Packet);
                byteBlock.Write(BitConverter.GetBytes(body.Length));
                byteBlock.Write(body);
            }
        }

        private sealed class PendingRequest
        {
            public TaskCompletionSource<HorizonMessagePacket> ResponseSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
