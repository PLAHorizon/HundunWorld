using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Game.GengDi.Core.Services.Call
{
    /// <summary>
    /// 通话媒体 UDP 传输层。
    /// 媒体流不经过服务端：双方通过信令交换各自的 UDP 端点后直连收发。
    /// 数据包格式（小端）：
    ///   [0..4)  魔数 0x4844434C ("HDCL")
    ///   [4..12) 通话会话ID的64位哈希（用于丢弃其他会话的串扰包）
    ///   [12]    类型：1=音频帧，2=视频分片
    ///   音频帧：[13..)=PCM16 16kHz 单声道数据
    ///   视频分片：[13..17)帧ID，[17..19)分片索引，[19..21)分片总数，[21..)=分片数据
    /// </summary>
    public sealed class CallMediaTransport : IDisposable
    {
        private const uint Magic = 0x4844434C;
        private const byte KindAudio = 1;
        private const byte KindVideoFragment = 2;
        private const int MaxDatagramSize = 1200;
        private const int HeaderSize = 13;
        private const int VideoFragHeaderSize = 21;
        private const int VideoFragmentPayload = MaxDatagramSize - VideoFragHeaderSize;
        private const int MaxPendingFrames = 8;
        private const long PendingFrameTimeoutMs = 3000;

        private readonly UdpClient _udpClient;
        private readonly ulong _callIdHash;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _receiveTask;
        private readonly Dictionary<uint, PendingVideoFrame> _pendingFrames = new();
        private IPEndPoint _remoteEndpoint;
        private uint _nextFrameId;
        private bool _disposed;

        /// <summary>收到远端音频数据（PCM16 16kHz 单声道）。</summary>
        public event Action<byte[]> AudioChunkReceived;

        /// <summary>收到远端完整视频帧（JPEG 数据）。</summary>
        public event Action<byte[]> VideoFrameReceived;

        /// <summary>收到远端任意媒体包（用于连接存活检测）。</summary>
        public event Action MediaActivityDetected;

        /// <summary>传输层发生错误（如套接字异常）。</summary>
        public event Action<string> TransportError;

        /// <summary>最近一次收到远端数据包的时间戳（毫秒），0 表示从未收到。</summary>
        public long LastPacketReceivedMs;

        public CallMediaTransport(string callId)
        {
            if (string.IsNullOrEmpty(callId))
            {
                throw new ArgumentException("通话ID不能为空。", nameof(callId));
            }

            _callIdHash = ComputeCallIdHash(callId);

            // 绑定任意可用端口；端口耗尽等异常由调用方（CallService）捕获并提示设备/网络异常
            _udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            var boundPort = ((IPEndPoint)_udpClient.Client.LocalEndPoint).Port;
            // 对外暴露的端点使用解析后的真实 IP（绑定到 Any 时 LocalEndPoint 为 0.0.0.0，不可直连）
            LocalEndpoint = new IPEndPoint(ResolveLocalMediaAddress(), boundPort);

            _receiveTask = Task.Run(ReceiveLoopAsync);
        }

        /// <summary>本地媒体端点（供 MediaReady 信令携带发送给对端）。</summary>
        public IPEndPoint LocalEndpoint { get; }

        /// <summary>是否已配置远端媒体端点。</summary>
        public bool HasRemoteEndpoint => _remoteEndpoint != null;

        public void SetRemoteEndpoint(IPEndPoint remote)
        {
            _remoteEndpoint = remote ?? throw new ArgumentNullException(nameof(remote));
        }

        /// <summary>
        /// 解析本机用于媒体直连的 IPv4 地址（优先选择处于 Up 状态的非回环地址）。
        /// </summary>
        public static IPAddress ResolveLocalMediaAddress()
        {
            try
            {
                IPAddress fallback = null;
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up)
                    {
                        continue;
                    }

                    foreach (var address in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (address.Address.AddressFamily != AddressFamily.InterNetwork
                            || IPAddress.IsLoopback(address.Address))
                        {
                            continue;
                        }

                        if (IsIPv4LinkLocal(address.Address))
                        {
                            fallback ??= address.Address;
                            continue;
                        }

                        return address.Address;
                    }
                }

                return fallback ?? IPAddress.Loopback;
            }
            catch
            {
                return IPAddress.Loopback;
            }
        }

        /// <summary>发送一段音频数据（PCM16 16kHz 单声道）。静音/未配置远端时静默跳过。</summary>
        public void SendAudio(ReadOnlySpan<byte> pcmData)
        {
            var remote = _remoteEndpoint;
            if (_disposed || remote == null || pcmData.IsEmpty)
            {
                return;
            }

            var packet = new byte[HeaderSize + pcmData.Length];
            WriteHeader(packet, KindAudio);
            pcmData.CopyTo(packet.AsSpan(HeaderSize));
            TrySend(packet, remote);
        }

        /// <summary>发送完整视频帧（自动分片）。返回 false 表示未配置远端端点。</summary>
        public bool SendVideoFrame(ReadOnlySpan<byte> jpegData)
        {
            var remote = _remoteEndpoint;
            if (_disposed || remote == null || jpegData.IsEmpty)
            {
                return false;
            }

            var frameId = unchecked(_nextFrameId++);
            var fragmentCount = (jpegData.Length + VideoFragmentPayload - 1) / VideoFragmentPayload;
            if (fragmentCount > ushort.MaxValue)
            {
                return false;
            }

            for (ushort index = 0; index < fragmentCount; index++)
            {
                var offset = index * VideoFragmentPayload;
                var length = Math.Min(VideoFragmentPayload, jpegData.Length - offset);

                var packet = new byte[VideoFragHeaderSize + length];
                WriteHeader(packet, KindVideoFragment);
                BitConverter.TryWriteBytes(packet.AsSpan(13, 4), frameId);
                BitConverter.TryWriteBytes(packet.AsSpan(17, 2), index);
                BitConverter.TryWriteBytes(packet.AsSpan(19, 2), (ushort)fragmentCount);
                jpegData.Slice(offset, length).CopyTo(packet.AsSpan(VideoFragHeaderSize));
                TrySend(packet, remote);
            }

            return true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                _cts.Cancel();
            }
            catch
            {
            }

            try
            {
                _udpClient.Dispose();
            }
            catch
            {
            }

            try
            {
                _receiveTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
            }

            _cts.Dispose();
        }

        private static bool IsIPv4LinkLocal(IPAddress address)
        {
            var bytes = address.GetAddressBytes();
            return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
        }

        private void TrySend(byte[] packet, IPEndPoint remote)
        {
            try
            {
                _udpClient.Send(packet, packet.Length, remote);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                TransportError?.Invoke($"媒体数据发送失败：{ex.Message}");
            }
        }

        private void WriteHeader(byte[] packet, byte kind)
        {
            BitConverter.TryWriteBytes(packet.AsSpan(0, 4), Magic);
            BitConverter.TryWriteBytes(packet.AsSpan(4, 8), _callIdHash);
            packet[12] = kind;
        }

        private async Task ReceiveLoopAsync()
        {
            var token = _cts.Token;

            while (!token.IsCancellationRequested && !_disposed)
            {
                UdpReceiveResult result;
                try
                {
                    result = await _udpClient.ReceiveAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!_disposed)
                    {
                        TransportError?.Invoke($"媒体数据接收异常：{ex.Message}");
                    }
                    break;
                }

                try
                {
                    HandleDatagram(result.Buffer);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CallMediaTransport] 处理媒体包异常：{ex.Message}");
                }
            }
        }

        private void HandleDatagram(byte[] buffer)
        {
            if (buffer == null || buffer.Length < HeaderSize)
            {
                return;
            }

            var magic = BitConverter.ToUInt32(buffer, 0);
            var callHash = BitConverter.ToUInt64(buffer, 4);
            if (magic != Magic || callHash != _callIdHash)
            {
                return;
            }

            Interlocked.Exchange(ref LastPacketReceivedMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            MediaActivityDetected?.Invoke();

            var kind = buffer[12];
            if (kind == KindAudio)
            {
                var pcm = new byte[buffer.Length - HeaderSize];
                Array.Copy(buffer, HeaderSize, pcm, 0, pcm.Length);
                AudioChunkReceived?.Invoke(pcm);
            }
            else if (kind == KindVideoFragment && buffer.Length > VideoFragHeaderSize)
            {
                HandleVideoFragment(buffer);
            }
        }

        private void HandleVideoFragment(byte[] buffer)
        {
            var frameId = BitConverter.ToUInt32(buffer, 13);
            var fragmentIndex = BitConverter.ToUInt16(buffer, 17);
            var fragmentCount = BitConverter.ToUInt16(buffer, 19);
            if (fragmentCount == 0 || fragmentIndex >= fragmentCount)
            {
                return;
            }

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // 清理超时的未完成帧，防止内存泄漏
            if (_pendingFrames.Count > 0)
            {
                List<uint> expired = null;
                foreach (var kv in _pendingFrames)
                {
                    if (nowMs - kv.Value.LastActivityMs > PendingFrameTimeoutMs)
                    {
                        expired ??= new List<uint>();
                        expired.Add(kv.Key);
                    }
                }

                if (expired != null)
                {
                    foreach (var key in expired)
                    {
                        _pendingFrames.Remove(key);
                    }
                }
            }

            if (!_pendingFrames.TryGetValue(frameId, out var frame))
            {
                if (_pendingFrames.Count >= MaxPendingFrames)
                {
                    return;
                }

                frame = new PendingVideoFrame(fragmentCount);
                _pendingFrames[frameId] = frame;
            }

            if (frame.FragmentCount != fragmentCount || frame.IsComplete)
            {
                return;
            }

            frame.LastActivityMs = nowMs;
            var payloadLength = buffer.Length - VideoFragHeaderSize;
            if (frame.Fragments[fragmentIndex] != null)
            {
                return;
            }

            var fragment = new byte[payloadLength];
            Array.Copy(buffer, VideoFragHeaderSize, fragment, 0, payloadLength);
            frame.Fragments[fragmentIndex] = fragment;
            frame.ReceivedCount++;

            if (frame.ReceivedCount < fragmentCount)
            {
                return;
            }

            _pendingFrames.Remove(frameId);

            var totalLength = 0;
            foreach (var fragmentData in frame.Fragments)
            {
                if (fragmentData == null)
                {
                    return;
                }

                totalLength += fragmentData.Length;
            }

            var jpeg = new byte[totalLength];
            var offset = 0;
            foreach (var fragmentData in frame.Fragments)
            {
                Array.Copy(fragmentData, 0, jpeg, offset, fragmentData.Length);
                offset += fragmentData.Length;
            }

            VideoFrameReceived?.Invoke(jpeg);
        }

        /// <summary>使用 FNV-1a 64 位哈希将会话ID映射为包过滤键。</summary>
        public static ulong ComputeCallIdHash(string callId)
        {
            unchecked
            {
                var hash = 14695981039346656037UL;
                foreach (var ch in callId)
                {
                    hash ^= ch;
                    hash *= 1099511628211UL;
                }

                return hash;
            }
        }

        private sealed class PendingVideoFrame
        {
            public PendingVideoFrame(int fragmentCount)
            {
                FragmentCount = fragmentCount;
                Fragments = new byte[fragmentCount][];
                LastActivityMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }

            public int FragmentCount { get; }

            public byte[][] Fragments { get; }

            public int ReceivedCount { get; set; }

            public long LastActivityMs { get; set; }

            public bool IsComplete => ReceivedCount >= FragmentCount;
        }
    }
}
