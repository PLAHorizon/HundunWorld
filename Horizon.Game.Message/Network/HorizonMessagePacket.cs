using Horizon.Game.Message.Enums;
// 安全审查 2026-08：改用本项目带解压上限防护的 LZ4Pickler 副本（K4os 库版无长度上限，存在解压炸弹风险）。
using LZ4Pickler = Horizon.Game.Message.Utilities.LZ4Pickler;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using TouchSocket.Core;

namespace Horizon.Game.Message.Network
{
    /// <summary>
    /// 游戏消息包装类
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class HorizonMessagePacket 
    {
        /// <summary>
        /// 服务类型
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public ServiceType ServiceType { get; set; }
       
        /// <summary>
        /// 消息头
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public MessageHeader Header { get; set; }

        /// <summary>
        /// 消息体
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public MessageUnion Body { get; set; }

        /// <summary>
        /// 原始数据
        /// </summary>
        [MemoryPackIgnore]
        [Id(3)] public byte[]? RawData { get; set; }

        /// <summary>
        /// 数据长度
        /// </summary>
        [MemoryPackIgnore]
        [Id(4)] public int Length { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        [MemoryPackConstructor]
        public HorizonMessagePacket()
        {
            Header = new MessageHeader();
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="body">消息体</param>
        public HorizonMessagePacket(MessageUnion body)
        {
            Header = new MessageHeader();
            Body = body;
        }
                
    }

    /// <summary>
    /// 包括消息体、长度和关联的数据包。
    /// </summary>
    /// <remarks>此类提供构造、序列化和反序列化地平线消息的功能。
    /// 它支持从缓冲区构建请求信息、将消息转换为字节数组、解析消息头和消息体等操作。
    /// 此类的实例封装了地平线消息的原始数据和元数据。</remarks>
    [MemoryPackable(SerializeLayout.Explicit)]
    public partial class HorizonMessageInfo : IFixedHeaderRequestInfo
    {
        // 协议头固定为 8 字节：4字节载荷长度 + 1字节消息类型 + 1字节压缩标志 + 2字节校验和
        private bool _isCompressed;
        private ushort _expectedChecksum;

        /// <summary>
        /// 最近一次成功解析的原始 8 字节头（按小端读入 ulong）与有效标志。
        /// 仅用于协议不匹配时打印十六进制，便于判断对端帧格式是否与本端一致；
        /// 存为值类型，热路径不产生任何分配。
        /// </summary>
        private ulong _rawHeader;
        private bool _hasRawHeader;

        /// <summary>
        /// 消息体长度（等于载荷字节数，不含8字节头）
        /// </summary>
        [MemoryPackOrder(0)]
        public int BodyLength { get; set; }

        /// <summary>
        /// 消息数据
        /// </summary>
        [MemoryPackOrder(1)]
        public byte[]? Body { get; set; }

        /// <summary>
        /// 消息包对象
        /// </summary>
        [MemoryPackIgnore]
        public HorizonMessagePacket? Packet { get; set; }

        /// <summary>
        /// 最大长度
        /// </summary>
        [MemoryPackIgnore]
        public int MaxLength => 1024 * 1024;

        /// <summary>
        /// 阶段 5.4.1 — 注入的日志器（可选，静态）。由宿主进程（网关/客户端）启动时设置，
        /// 类别建议 <c>"HorizonMessageInfo"</c>；null 时不输出任何日志（零开销）。<br/>
        /// 替换协议头/体解析失败路径的原 <c>Console.WriteLine</c> 直接打印
        /// （解析失败属协议异常，按 Error 级输出，可接入统一日志管线与监控告警）。
        /// </summary>
        [MemoryPackIgnore]
        public static ILogger? Log { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        [MemoryPackConstructor]
        public HorizonMessageInfo()
        {
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="packet">消息包</param>
       
        public HorizonMessageInfo(HorizonMessagePacket packet)
        {
            Packet = packet;
            if (packet?.RawData != null)
            {
                Body = packet.RawData;
                BodyLength = packet.RawData.Length;
            }
        }

        // 已删除 TryBuild()/ToBytes()/FromBytes()：它们编码的是「4 字节长度前缀 + MemoryPack 体」的
        // 旧线格式（无消息类型/压缩标志/校验和），与规范 8 字节头格式（见 OnParsingHeader/
        // OnParsingBody 与 HorizonMessageAdapter.WrapPacket）冲突。全仓无调用方，属死代码。

        public bool OnParsingHeader(ReadOnlySpan<byte> header)
        {
            // 每帧重置：确保后续诊断打印的是"本帧"的头，而不是上一帧的残留。
            _hasRawHeader = false;

            // 完整协议头为 8 字节：[0-3] 载荷长度, [4] 消息类型, [5] 压缩标志, [6-7] 校验和
            if (header.Length < 8)
            {
                Log?.LogError($"HorizonMessageInfo.OnParsingHeader: header too short, length={header.Length}");
                return false;
            }

            try
            {
                // 先用临时变量读取，验证通过后再赋值给 BodyLength，
                // 避免将非法大值写入属性后被 TouchSocket 框架读取并触发越界异常。
                var bodyLength = BitConverter.ToInt32(header);
                if (bodyLength <= 0 || bodyLength > MaxLength)
                {
                    Log?.LogError($"HorizonMessageInfo.OnParsingHeader: invalid bodyLength={bodyLength}, MaxLength={MaxLength}");
                    return false;
                }

                _rawHeader = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(header);
                _hasRawHeader = true;
                BodyLength = bodyLength;
                _isCompressed = header[5] != 0;
                _expectedChecksum = BitConverter.ToUInt16(header.Slice(6, 2));
                return true;
            }
            catch (Exception ex)
            {
                Log?.LogError($"HorizonMessageInfo.OnParsingHeader: exception={ex.Message}");
                return false;
            }
        }

        public bool OnParsingBody(ReadOnlySpan<byte> body)
        {
            if (body.Length != BodyLength)
            {
                Log?.LogError($"HorizonMessageInfo.OnParsingBody: body length mismatch, expected={BodyLength}, actual={body.Length}");
                return false;
            }

            try
            {
                // 校验和验证
                var actualChecksum = CalculateChecksum(body);
                if (actualChecksum != _expectedChecksum)
                {
                    // 附带原始头与载荷头部十六进制：若对端按非 8 字节头（如旧版 4 字节长度前缀）成帧，
                    // 这里能直接看出 [4]/[5]/[6-7] 位置上放的是载荷字节而非 类型/压缩标志/校验和，
                    // 从而把"checksum mismatch"一步定位到成帧格式不一致，而不是靠猜。
                    Log?.LogError(
                        $"HorizonMessageInfo.OnParsingBody: checksum mismatch, expected={_expectedChecksum}, actual={actualChecksum}, " +
                        $"bodyLength={body.Length}, compressed={_isCompressed}, header=[{FormatHeaderHex()}], bodyHead=[{FormatHex(body, 32)}]");
                    return false;
                }

                Body = body.ToArray();

                var finalBody = _isCompressed ? LZ4Pickler.Unpickle(body) : Body;

                // Unpickle 对畸形/超限输入返回 null（而非抛异常），必须显式判空，
                // 否则 NRE 会掩盖真实的协议违规分类（解压失败/超限 vs 反序列化失败）。
                if (finalBody is null || finalBody.Length == 0)
                {
                    Log?.LogError($"HorizonMessageInfo.OnParsingBody: decompression failed (malformed or exceeds {LZ4Pickler.MaxDecompressedLength} bytes), compressed={_isCompressed}, wireSize={body.Length}");
                    return false;
                }

                if (finalBody.Length > MaxLength)
                {
                    Log?.LogError($"HorizonMessageInfo.OnParsingBody: decompressed size exceeds limit, size={finalBody.Length}, MaxLength={MaxLength}");
                    return false;
                }

                Packet = MemoryPackSerializer.Deserialize<HorizonMessagePacket>(finalBody);
                if (Packet == null)
                {
                    Log?.LogError("HorizonMessageInfo.OnParsingBody: MemoryPack deserialization returned null");
                    return false;
                }

                Packet.RawData = Body;
                return true;
            }
            catch (Exception ex)
            {
                Log?.LogError($"HorizonMessageInfo.OnParsingBody: exception={ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 与 MessageAdapter / HorizonMessageAdapter 保持一致的轻量校验和算法。
        /// </summary>
        private static ushort CalculateChecksum(ReadOnlySpan<byte> data) =>
            HorizonProtocol.CalculateChecksum(data);

        /// <summary>
        /// 把缓冲区前 <paramref name="maxBytes"/> 字节格式化为十六进制串（仅故障诊断路径调用，热路径零开销）。
        /// </summary>
        private static string FormatHex(ReadOnlySpan<byte> data, int maxBytes)
        {
            var count = Math.Min(maxBytes, data.Length);
            if (count <= 0)
            {
                return "(empty)";
            }

            const string digits = "0123456789abcdef";
            var chars = new char[count * 2];
            for (var i = 0; i < count; i++)
            {
                chars[i * 2] = digits[data[i] >> 4];
                chars[i * 2 + 1] = digits[data[i] & 0x0F];
            }
            return new string(chars);
        }

        /// <summary>
        /// 本帧原始 8 字节头的小端字节序十六进制表示。
        /// </summary>
        private string FormatHeaderHex()
        {
            if (!_hasRawHeader)
            {
                return "(none)";
            }

            Span<byte> bytes = stackalloc byte[HorizonProtocol.HeaderLength];
            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)(_rawHeader >> (8 * i));
            }
            return FormatHex(bytes, bytes.Length);
        }

        /// <summary>
        /// 以规范 8 字节线路头写出本帧：4 字节载荷长度 + 1 字节消息类型 + 1 字节压缩标志 + 2 字节校验和。
        /// </summary>
        /// <remarks>
        /// 修复：原实现写的是「4 字节长度前缀 + MemoryPack 体」的旧线格式（无消息类型/压缩标志/校验和），
        /// 与 <see cref="OnParsingHeader"/> / <see cref="HorizonMessageAdapter.WrapPacket"/> 的 8 字节头规范冲突。
        /// <para>
        /// 该类通过 <c>CustomFixedHeaderDataHandlingAdapter</c> 注册为<b>可发送</b>的请求信息
        /// （客户端适配器 <c>CanSendRequestInfo = true</c>），一旦走 TouchSocket 强类型发送路径，
        /// 对端按 8 字节头解析时会把载荷前几字节误当作 类型/压缩标志/校验和，
        /// 表现为「expected 恒定、actual 随载荷变化」的 checksum mismatch —— 与本次线上现象一致。
        /// </para>
        /// 现在与 <c>HorizonMessageAdapter.WrapPacket</c> / <c>SyncHandshakeWire.Wrap</c> 逐字节同构。
        /// </remarks>
        public void Build<TByteBlock>(ref TByteBlock byteBlock) where TByteBlock : IByteBlock
        {
            if (Packet?.Header == null)
            {
                return;
            }

            try
            {
                var bodyData = MemoryPackSerializer.Serialize(Packet);
                var frame = new byte[HorizonProtocol.HeaderLength + bodyData.Length];
                var span = frame.AsSpan();

                BitConverter.TryWriteBytes(span.Slice(0, 4), bodyData.Length);
                // MessageType 可 > byte 上限，按低位截断（与各写入方 span[4] = (byte)type 一致）。
                span[4] = unchecked((byte)Packet.Header.MessageType);
                span[5] = 0; // Build 无压缩入参，固定发未压缩帧（压缩标志 0 语义明确）
                BitConverter.TryWriteBytes(span.Slice(6, 2), HorizonProtocol.CalculateChecksum(bodyData));
                bodyData.AsSpan().CopyTo(span.Slice(HorizonProtocol.HeaderLength));

                byteBlock.Write(frame);
            }
            catch
            {
                // 构建失败时不写入任何数据
            }
        }
    }

    /// <summary>
    /// 地平线协议工具类：提供跨程序集共享的协议常量与算法。
    /// </summary>
    public static class HorizonProtocol
    {
        /// <summary>协议固定头长度（字节）：4字节载荷长度 + 1字节消息类型 + 1字节压缩标志 + 2字节校验和。</summary>
        public const int HeaderLength = 8;

        /// <summary>
        /// 轻量校验和：将载荷各字节累加后截断为 ushort。
        /// 与 MessageAdapter（客户端）和 HorizonMessageAdapter（服务端）的实现保持完全一致。
        /// </summary>
        public static ushort CalculateChecksum(ReadOnlySpan<byte> data)
        {
            uint checksum = 0;
            foreach (var b in data)
            {
                checksum += b;
            }
            return (ushort)(checksum & 0xFFFF);
        }
    }
}