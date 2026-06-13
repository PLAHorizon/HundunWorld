using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.UI.Authentication;
using K4os.Compression.LZ4;
using MemoryPack;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TouchSocket.Core;
using TouchSocket.Sockets;
using AuthenticationManager = HundunWorld.Game.UI.Authentication.AuthenticationManager;


namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 混沌世界MMORPG网络消息适配器（客户端版本）
    /// 专为武侠游戏的消息传输优化
    /// </summary>
    public class HorizonMessageAdapter : CustomFixedHeaderDataHandlingAdapter<HorizonMessageInfo>
    {
        private readonly AdapterStatistics _statistics = new();
        private readonly object _statsLock = new();

        /// <summary>
        /// 消息头大小（字节）
        /// 4字节长度 + 1字节类型 + 1字节压缩标志 + 2字节校验和 = 8字节
        /// </summary>
        public override int HeaderLength => 8;

        public override bool CanSendRequestInfo { get; } = true;

        protected override HorizonMessageInfo GetInstance() => new HorizonMessageInfo();

        /// <summary>
        /// 生成网络消息包
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="message"></param>
        /// <returns></returns>
        public HorizonMessagePacket CreateHorizonMessage<T>(T message) where T : MessageUnion, INetworkMessage
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message), "网络消息不能为空");
            }

            // 创建默认的消息头
            var networkManager = HundunWorldGame.Instance?.NetworkManager;
            var passport = AuthenticationManager.Instance.Passport;
            var authToken = AuthenticationManager.Instance.AuthToken;
            var header = new MessageHeader
            {
                MessageType = ((INetworkMessage)message).Type,
                IsResponse = false,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                GameId = networkManager?.GameId ?? AuthenticationManager.Instance.GameId,
                ZoneId = networkManager?.ZoneId ?? AuthenticationManager.Instance.ZoneId,
                ServerId = networkManager?.ServerId ?? AuthenticationManager.Instance.ServerId,
                UserId = networkManager?.UserId ?? (passport?.UserId ?? 0),
                AuthToken = !string.IsNullOrEmpty(networkManager?.AuthToken) ? networkManager.AuthToken : authToken,
                MachineId = MachineIdentifier.GetMachineGuid()
            };

            HorizonMessagePacket messagePacket = new HorizonMessagePacket
            {
                Header = header,
                ServiceType = ((INetworkMessage)message).ServiceType,
                Body = message,
                RawData = MemoryPackSerializer.Serialize(message),
            };

            messagePacket.Header.SequenceId = CRC32.Compute(messagePacket.RawData);
            return messagePacket;
        }
        
        /// <summary>
        /// 序列化并打包完整消息包为线路帧，保留调用方已经设置好的头部字段
        /// </summary>
        public byte[] PackPacket(HorizonMessagePacket packet, bool compress = true)
        {
            try
            {
                if (packet == null)
                {
                    throw new ArgumentNullException(nameof(packet), "消息包不能为空");
                }

                if (packet.Header == null)
                {
                    throw new ArgumentException("消息包头不能为空", nameof(packet));
                }

                if (packet.Body != null && (packet.RawData == null || packet.RawData.Length == 0))
                {
                    packet.RawData = MemoryPackSerializer.Serialize(packet.Body);
                }

                if (packet.RawData != null && packet.RawData.Length > 0)
                {
                    packet.Header.SequenceId = CRC32.Compute(packet.RawData);
                }

                var messageData = MemoryPackSerializer.Serialize(packet);
                return WrapPacket(messageData, packet.Header.MessageType, compress);
            }
            catch (Exception ex)
            {
                UpdateErrorStats();
                throw new InvalidOperationException($"消息打包失败: {ex.Message}", ex);
            }
        }

        private byte[] WrapPacket(byte[] messageData, MessageType messageType, bool compress)
        {
            byte[] finalData;
            bool isCompressed = false;

            if (compress && messageData.Length > 256)
            {
                finalData = LZ4Pickler.Pickle(messageData);
                isCompressed = finalData.Length < messageData.Length;
                if (!isCompressed)
                    finalData = messageData;
            }
            else
            {
                finalData = messageData;
            }

            var packet = new byte[HeaderLength + finalData.Length];
            var span = packet.AsSpan();

            BitConverter.TryWriteBytes(span.Slice(0, 4), finalData.Length);
            span[4] = (byte)messageType;
            span[5] = (byte)(isCompressed ? 1 : 0);
            var checksum = CalculateChecksum(finalData);
            BitConverter.TryWriteBytes(span.Slice(6, 2), checksum);
            finalData.AsSpan().CopyTo(span.Slice(HeaderLength));

            return packet;
        }

        /// <summary>
        /// 序列化并打包消息
        /// </summary>
        public byte[] PackMessage<T>(T message, MessageType messageType, bool compress = true) where T : MessageUnion,INetworkMessage
        {
            try
            {
                var data = CreateHorizonMessage(message);
                // 序列化消息
                var messageData = MemoryPackSerializer.Serialize(data);
                
                // 压缩消息（如果需要且大于阈值）
                byte[] finalData;
                bool isCompressed = false;

                if (compress && messageData.Length > 256) // 大于256字节才压缩
                {
                    finalData = LZ4Pickler.Pickle(messageData);
                    isCompressed = finalData.Length < messageData.Length; // 只有压缩有效果才使用
                    if (!isCompressed)
                        finalData = messageData;
                }
                else
                {
                    finalData = messageData;
                }

                // 构建完整消息包
                var packet = new byte[HeaderLength + finalData.Length];
                var span = packet.AsSpan();

                // 写入消息长度
                BitConverter.TryWriteBytes(span.Slice(0, 4), finalData.Length);

                // 写入消息类型
                span[4] = (byte)messageType;

                // 写入压缩标志
                span[5] = (byte)(isCompressed ? 1 : 0);

                // 计算并写入校验和
                var checksum = CalculateChecksum(finalData);
                BitConverter.TryWriteBytes(span.Slice(6, 2), checksum);

                // 写入消息体
                finalData.AsSpan().CopyTo(span.Slice(HeaderLength));

                return packet;
            }
            catch (Exception ex)
            {
                UpdateErrorStats();
                throw new InvalidOperationException($"消息打包失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 解包并反序列化消息
        /// </summary>
        public HorizonMessagePacket UnpackMessage(byte[] data)
        {
            try
            {
                if (data == null)
                {
                    throw new InvalidOperationException("数据为空");
                }
                
                if (data.Length < HeaderLength)
                {
                    throw new InvalidOperationException($"数据长度不足消息头长度: {data.Length} < {HeaderLength}");
                }

                var span = data.AsSpan();

                // 读取消息长度
                var messageLength = BitConverter.ToInt32(span.Slice(0, 4));
                
                // 输出调试信息
                FlaxEngine.Debug.Log($"[HorizonMessageAdapter] 消息头信息 - 长度: {messageLength}, 数据总长度: {data.Length}");

                // 验证数据长度
                if (data.Length < HeaderLength + messageLength)
                {
                    throw new InvalidOperationException($"数据长度不足: {data.Length} < {HeaderLength + messageLength}");
                }

                // 读取消息类型
                var messageType = (MessageType)span[4];

                // 读取压缩标志
                var isCompressed = span[5] != 0;

                // 读取校验和
                var expectedChecksum = BitConverter.ToUInt16(span.Slice(6, 2));
                
                // 输出调试信息
                FlaxEngine.Debug.Log($"[HorizonMessageAdapter] 消息详情 - 类型: {messageType}, 压缩: {isCompressed}, 校验和: {expectedChecksum}");

                // 读取消息体
                var messageBody = span.Slice(HeaderLength, messageLength);

                // 验证校验和
                var actualChecksum = CalculateChecksum(messageBody);
                if (actualChecksum != expectedChecksum)
                {
                    throw new InvalidOperationException($"消息校验和验证失败: 期望 {expectedChecksum}, 实际 {actualChecksum}");
                }

                // 解压缩消息（如果需要）
                byte[] finalData = isCompressed ? LZ4Pickler.Unpickle(messageBody) : messageBody.ToArray();
                
                // 输出调试信息
                FlaxEngine.Debug.Log($"[HorizonMessageAdapter] 消息体处理 - 压缩前长度: {messageBody.Length}, 压缩后长度: {finalData.Length}");

                // 反序列化消息包
                var packet = MemoryPackSerializer.Deserialize<HorizonMessagePacket>(finalData);
                if (packet == null)
                {
                    throw new InvalidOperationException("消息反序列化失败");
                }
                
                // 输出调试信息
                FlaxEngine.Debug.Log($"[HorizonMessageAdapter] 消息反序列化成功 - 类型: {packet.Header?.MessageType}, 服务: {packet.ServiceType}");

                packet.RawData = data;
                UpdateProcessStats(messageType, data.Length);
                return packet;
            }
            catch (Exception ex)
            {
                UpdateErrorStats();
                FlaxEngine.Debug.LogError($"[HorizonMessageAdapter] 消息解包失败: {ex.Message}");
                FlaxEngine.Debug.LogError($"[HorizonMessageAdapter] 堆栈跟踪: {ex.StackTrace}");
                throw new InvalidOperationException($"消息解包失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 计算校验和
        /// </summary>
        private static ushort CalculateChecksum(ReadOnlySpan<byte> data)
        {
            uint checksum = 0;
            foreach (var b in data)
            {
                checksum += b;
            }
            return (ushort)(checksum & 0xFFFF);
        }

        /// <summary>
        /// 更新处理统计信息
        /// </summary>
        private void UpdateProcessStats(MessageType messageType, int length)
        {
            lock (_statsLock)
            {
                _statistics.TotalMessagesProcessed++;
                _statistics.TotalBytesProcessed += length;

                if (!_statistics.MessageTypeStats.ContainsKey(messageType))
                    _statistics.MessageTypeStats[messageType] = 0;

                _statistics.MessageTypeStats[messageType]++;
            }
        }

        /// <summary>
        /// 更新错误统计信息
        /// </summary>
        private void UpdateErrorStats()
        {
            lock (_statsLock)
            {
                _statistics.ErrorCount++;
            }
        }
    }
    /// <summary>
    /// 跨平台机器唯一标识符工具类。
    /// 读取操作系统内置的机器GUID，支持 Windows、Linux 和 macOS。
    /// 结果在进程生命周期内缓存，不会重复读取文件或执行子进程。
    /// </summary>
    public static class MachineIdentifier
    {
        private static readonly Lazy<string> _machineGuid = new(ResolveGuid, isThreadSafe: true);

        /// <summary>
        /// 获取当前机器的唯一标识符（GUID 字符串，小写，不含花括号）。
        /// 在各平台的读取来源：
        /// <list type="bullet">
        ///   <item>Windows：注册表 HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid</item>
        ///   <item>Linux：/etc/machine-id（systemd）或 /var/lib/dbus/machine-id</item>
        ///   <item>macOS：sysctl -n kern.uuid（硬件 UUID）</item>
        ///   <item>其他平台/读取失败：主机名的 SHA-256 哈希值</item>
        /// </list>
        /// </summary>
        public static string GetMachineGuid() => _machineGuid.Value;

        private static string ResolveGuid()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                    return GetWindowsMachineGuid();

                if (OperatingSystem.IsLinux())
                    return GetLinuxMachineId();

                if (OperatingSystem.IsMacOS())
                    return GetMacOsMachineUuid();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MachineIdentifier] 读取系统机器ID失败，将使用主机名回退方案: {ex.Message}");
            }

            return HostnameFallback();
        }

        /// <summary>
        /// Windows：从注册表读取 MachineGuid。
        /// </summary>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static string GetWindowsMachineGuid()
        {
#pragma warning disable CA1416
            using var key = Microsoft.Win32.Registry.LocalMachine
                .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");

            var value = key?.GetValue("MachineGuid")?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
                return Normalize(value);
#pragma warning restore CA1416

            throw new InvalidOperationException("Windows 注册表中未找到 MachineGuid 值");
        }

        /// <summary>
        /// Linux：读取 /etc/machine-id 或 /var/lib/dbus/machine-id。
        /// </summary>
        private static string GetLinuxMachineId()
        {
            foreach (var path in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
            {
                if (!File.Exists(path))
                    continue;

                var content = File.ReadAllText(path).Trim();
                if (!string.IsNullOrWhiteSpace(content))
                    return Normalize(content);
            }

            throw new InvalidOperationException("Linux machine-id 文件不存在或内容为空");
        }

        /// <summary>
        /// macOS：通过 sysctl 读取硬件 UUID。
        /// </summary>
        private static string GetMacOsMachineUuid()
        {
            var output = RunCommand("sysctl", "-n kern.uuid");
            if (!string.IsNullOrWhiteSpace(output))
                return Normalize(output.Trim());

            throw new InvalidOperationException("macOS sysctl kern.uuid 未返回有效值");
        }

        /// <summary>
        /// 降级方案：对主机名和机器域名求 SHA-256，以 GUID 形式表示。
        /// </summary>
        private static string HostnameFallback()
        {
            var seed = $"{Environment.MachineName}|{Environment.UserDomainName}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));

            // 取前16字节生成确定性 GUID
            var guidBytes = new byte[16];
            Array.Copy(hash, guidBytes, 16);
            // 设置版本4（随机）字段以符合 UUID 规范
            guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x40);
            guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
            return new Guid(guidBytes).ToString("D");
        }

        /// <summary>
        /// 规范化 GUID 字符串：去掉花括号、转为小写。
        /// </summary>
        private static string Normalize(string raw)
        {
            return raw.Trim('{', '}', ' ').ToLowerInvariant();
        }

        /// <summary>
        /// 执行外部命令并返回标准输出内容，超时时间 3 秒。
        /// </summary>
        private static string RunCommand(string executable, string arguments)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = executable,
                        Arguments = arguments,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(3000);
                return output;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
    /// <summary>
    /// 适配器统计信息
    /// </summary>
    public class AdapterStatistics
    {
        /// <summary>
        /// 处理的消息总数
        /// </summary>
        public long TotalMessagesProcessed { get; set; }

        /// <summary>
        /// 处理的字节总数
        /// </summary>
        public long TotalBytesProcessed { get; set; }

        /// <summary>
        /// 错误总数
        /// </summary>
        public long ErrorCount { get; set; }

        /// <summary>
        /// 各类型消息统计
        /// </summary>
        public Dictionary<MessageType, long> MessageTypeStats { get; set; } = new();

        /// <summary>
        /// 错误率
        /// </summary>
        public double ErrorRate => TotalMessagesProcessed > 0 ? (double)ErrorCount / TotalMessagesProcessed : 0;

        /// <summary>
        /// 平均消息大小
        /// </summary>
        public double AverageMessageSize => TotalMessagesProcessed > 0 ? (double)TotalBytesProcessed / TotalMessagesProcessed : 0;
    }
}
