using System;
using System.Buffers.Binary;

namespace Horizon.Planet.Wire;

/// <summary>解线结果。拒绝分支也必须回同一套对账字段，别只回一个 false。</summary>
public enum PlanetWireStatus
{
    Ok = 0,
    Truncated = 1,
    TrailingBytes = 2,
    UnsupportedProtocol = 3,
    InvalidRegion = 4,
}

/// <summary>握手：星球口径 + 地形口径。任一项不一致都不许进世界，否则两侧地面高度不同。</summary>
public struct PlanetHandshake
{
    public const ushort ProtocolVersion = 1;

    /// <summary>u16 + i64 + u8 + i64 + u32 + u32 + i64</summary>
    public const int Size = 2 + 8 + 1 + 8 + 4 + 4 + 8;

    public ushort WireProtocol;
    public long PlanetRadiusCm;
    public byte RegionLevel;
    public long TerrainSeed;
    public uint TerrainVersion;
    public uint BasefieldVersion;
    public long BasefieldHashHi;

    public static bool TryWrite(Span<byte> buffer, in PlanetHandshake message, out int written)
    {
        written = 0;
        if (buffer.Length < Size) return false;
        var span = buffer.Slice(0, Size);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(0), message.WireProtocol);
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(2), message.PlanetRadiusCm);
        span[10] = message.RegionLevel;
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(11), message.TerrainSeed);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(19), message.TerrainVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(23), message.BasefieldVersion);
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(27), message.BasefieldHashHi);
        written = Size;
        return true;
    }

    public static bool TryRead(ReadOnlySpan<byte> buffer, out PlanetHandshake message,
                               out PlanetWireStatus status, out int consumed)
    {
        message = default;
        consumed = 0;
        if (buffer.Length < Size) { status = PlanetWireStatus.Truncated; return false; }
        status = buffer.Length > Size ? PlanetWireStatus.TrailingBytes : PlanetWireStatus.Ok;
        var span = buffer.Slice(0, Size);
        consumed = Size;
        message.WireProtocol = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(0));
        message.PlanetRadiusCm = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(2));
        message.RegionLevel = span[10];
        message.TerrainSeed = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(11));
        message.TerrainVersion = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(19));
        message.BasefieldVersion = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(23));
        message.BasefieldHashHi = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(27));

        if (message.WireProtocol != ProtocolVersion)
            status = PlanetWireStatus.UnsupportedProtocol;
        return status == PlanetWireStatus.Ok;
    }

    /// <summary>两边口径是否一致；不一致时要能指出差在哪一项。</summary>
    public static string DescribeMismatch(in PlanetHandshake expected, in PlanetHandshake actual)
    {
        if (actual.WireProtocol != expected.WireProtocol) return $"wire {actual.WireProtocol} != {expected.WireProtocol}";
        if (actual.PlanetRadiusCm != expected.PlanetRadiusCm) return $"radius {actual.PlanetRadiusCm} != {expected.PlanetRadiusCm}";
        if (actual.RegionLevel != expected.RegionLevel) return $"regionLevel {actual.RegionLevel} != {expected.RegionLevel}";
        if (actual.TerrainSeed != expected.TerrainSeed) return $"terrainSeed {actual.TerrainSeed:x} != {expected.TerrainSeed:x}";
        if (actual.TerrainVersion != expected.TerrainVersion) return $"terrainVersion {actual.TerrainVersion} != {expected.TerrainVersion}";
        if (actual.BasefieldVersion != expected.BasefieldVersion) return $"basefieldVersion {actual.BasefieldVersion} != {expected.BasefieldVersion}";
        if (actual.BasefieldHashHi != expected.BasefieldHashHi) return $"basefieldHash {actual.BasefieldHashHi:x} != {expected.BasefieldHashHi:x}";
        return "";
    }
}

/// <summary>每 tick 的玩家状态：区域 + 区域局部厘米。绝不上全局 64 位坐标。</summary>
public struct PlayerStatePacket
{
    /// <summary>i64 tick + i64 region + 3×i32 位置 + 3×i32 速度 + 2×u16 朝向 + u16 标志</summary>
    public const int Size = 8 + 8 + 3 * 4 + 3 * 4 + 2 + 2 + 2;

    /// <summary>局部坐标的可表示半跨（厘米）：区域半宽 5.5 km，留一个数量级余量。</summary>
    public const int MaxAbsLocalCm = 10_000_000;

    public long Tick;
    public long RegionId;
    public int LocalXCm;
    public int LocalYCm;
    public int LocalZCm;
    public int VelocityXCms;
    public int VelocityYCms;
    public int VelocityZCms;
    public ushort YawQuantized;
    public ushort PitchQuantized;
    public ushort Flags;

    public static bool TryWrite(Span<byte> buffer, in PlayerStatePacket message, out int written)
    {
        written = 0;
        if (buffer.Length < Size) return false;
        var span = buffer.Slice(0, Size);
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(0), message.Tick);
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(8), message.RegionId);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(16), message.LocalXCm);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(20), message.LocalYCm);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(24), message.LocalZCm);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(28), message.VelocityXCms);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(32), message.VelocityYCms);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(36), message.VelocityZCms);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(40), message.YawQuantized);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(42), message.PitchQuantized);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(44), message.Flags);
        written = Size;
        return true;
    }

    public static bool TryRead(ReadOnlySpan<byte> buffer, out PlayerStatePacket message,
                               out PlanetWireStatus status, out int consumed)
    {
        message = default;
        consumed = 0;
        if (buffer.Length < Size) { status = PlanetWireStatus.Truncated; return false; }
        status = buffer.Length > Size ? PlanetWireStatus.TrailingBytes : PlanetWireStatus.Ok;
        var span = buffer.Slice(0, Size);
        consumed = Size;
        message.Tick = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(0));
        message.RegionId = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(8));
        message.LocalXCm = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(16));
        message.LocalYCm = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(20));
        message.LocalZCm = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(24));
        message.VelocityXCms = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(28));
        message.VelocityYCms = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(32));
        message.VelocityZCms = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(36));
        message.YawQuantized = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(40));
        message.PitchQuantized = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(42));
        message.Flags = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(44));

        if (Math.Abs((long)message.LocalXCm) > MaxAbsLocalCm ||
            Math.Abs((long)message.LocalYCm) > MaxAbsLocalCm ||
            Math.Abs((long)message.LocalZCm) > MaxAbsLocalCm)
        {
            status = PlanetWireStatus.InvalidRegion;   // 超出区域局部口径 ⇒ 对侧用了别的坐标系
            return false;
        }
        return status == PlanetWireStatus.Ok;
    }

    /// <summary>双精度局部厘米 → 线上 i32；四舍五入，返回真实取整误差便于判据核对。</summary>
    public static int QuantizeLocalCm(double localCm, out double errorCm)
    {
        double rounded = Math.Round(localCm, MidpointRounding.AwayFromZero);
        errorCm = rounded - localCm;
        return (int)Math.Clamp(rounded, -MaxAbsLocalCm, MaxAbsLocalCm);
    }

    public static ushort QuantizeAngle01(double normalizedTurn)
    {
        double t = normalizedTurn - Math.Floor(normalizedTurn);
        int q = (int)Math.Round(t * 65536.0);
        if (q == 65536) q = 0;
        return (ushort)q;
    }

    public static double DecodeAngle01(ushort quantized) => quantized / 65536.0;
}

/// <summary>跨区请求：带全局 ECEF，服务端才有权决定落在哪个区域。</summary>
public struct RegionTransferRequest
{
    /// <summary>from + to + 3×i64 + i64 tick</summary>
    public const int Size = 8 + 8 + 3 * 8 + 8;

    public long FromRegionId;
    public long ToRegionId;
    public EcefCentimeters GlobalCm;
    public long Tick;

    public static bool TryWrite(Span<byte> buffer, in RegionTransferRequest message, out int written)
    {
        written = 0;
        if (buffer.Length < Size) return false;
        var span = buffer.Slice(0, Size);
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(0), message.FromRegionId);
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(8), message.ToRegionId);
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(16), message.GlobalCm.X);
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(24), message.GlobalCm.Y);
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(32), message.GlobalCm.Z);
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(40), message.Tick);
        written = Size;
        return true;
    }

    public static bool TryRead(ReadOnlySpan<byte> buffer, out RegionTransferRequest message,
                               out PlanetWireStatus status, out int consumed)
    {
        message = default;
        consumed = 0;
        if (buffer.Length < Size) { status = PlanetWireStatus.Truncated; return false; }
        status = buffer.Length > Size ? PlanetWireStatus.TrailingBytes : PlanetWireStatus.Ok;
        var span = buffer.Slice(0, Size);
        consumed = Size;
        message.FromRegionId = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(0));
        message.ToRegionId = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(8));
        message.GlobalCm = new EcefCentimeters(
            BinaryPrimitives.ReadInt64LittleEndian(span.Slice(16)),
            BinaryPrimitives.ReadInt64LittleEndian(span.Slice(24)),
            BinaryPrimitives.ReadInt64LittleEndian(span.Slice(32)));
        message.Tick = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(40));
        return status == PlanetWireStatus.Ok;
    }
}

/// <summary>跨区确认：新区域 + 新局部坐标 + 新原点与切平面姿态。</summary>
public struct RegionTransferAck
{
    /// <summary>region + 3×i32 局部 + 3×i64 原点 + 4×i16 四元数</summary>
    public const int Size = 8 + 3 * 4 + 3 * 8 + 4 * 2;

    public long RegionId;
    public int LocalXCm;
    public int LocalYCm;
    public int LocalZCm;
    public EcefCentimeters OriginCm;
    public QuaternionQuantized Frame;

    public static bool TryWrite(Span<byte> buffer, in RegionTransferAck message, out int written)
    {
        written = 0;
        if (buffer.Length < Size) return false;
        var span = buffer.Slice(0, Size);
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(0), message.RegionId);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(8), message.LocalXCm);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(12), message.LocalYCm);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(16), message.LocalZCm);
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(20), message.OriginCm.X);
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(28), message.OriginCm.Y);
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(36), message.OriginCm.Z);
        message.Frame.WriteTo(span.Slice(44));
        written = Size;
        return true;
    }

    public static bool TryRead(ReadOnlySpan<byte> buffer, out RegionTransferAck message,
                               out PlanetWireStatus status, out int consumed)
    {
        message = default;
        consumed = 0;
        if (buffer.Length < Size) { status = PlanetWireStatus.Truncated; return false; }
        status = buffer.Length > Size ? PlanetWireStatus.TrailingBytes : PlanetWireStatus.Ok;
        var span = buffer.Slice(0, Size);
        consumed = Size;
        message.RegionId = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(0));
        message.LocalXCm = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(8));
        message.LocalYCm = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(12));
        message.LocalZCm = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(16));
        message.OriginCm = new EcefCentimeters(
            BinaryPrimitives.ReadInt64LittleEndian(span.Slice(20)),
            BinaryPrimitives.ReadInt64LittleEndian(span.Slice(28)),
            BinaryPrimitives.ReadInt64LittleEndian(span.Slice(36)));
        message.Frame = QuaternionQuantized.ReadFrom(span.Slice(44));
        return status == PlanetWireStatus.Ok;
    }
}

/// <summary>四元数 Q15 量化（8 字节）。姿态只用于换帧，误差上界由测试钉住。</summary>
public struct QuaternionQuantized
{
    public const double Scale = 32767.0;
    public short X;
    public short Y;
    public short Z;
    public short W;

    public static QuaternionQuantized From(double x, double y, double z, double w) => new()
    {
        X = Quantize(x), Y = Quantize(y), Z = Quantize(z), W = Quantize(w),
    };

    private static short Quantize(double value)
    {
        // 与 QuantizeLocalCm 同一条取整规则（.5 远离零），跨端可复现；默认的银行家舍入会让 ±0.5 分叉
        double q = Math.Round(value * Scale, MidpointRounding.AwayFromZero);
        if (q > short.MaxValue) q = short.MaxValue;
        if (q < short.MinValue) q = short.MinValue;
        return (short)q;
    }

    public double DecodeX => X / Scale;
    public double DecodeY => Y / Scale;
    public double DecodeZ => Z / Scale;
    public double DecodeW => W / Scale;

    /// <summary>
    /// 由局部轴在 ECEF 里的指向造出姿态：列 j 是局部第 j 轴（X=东、Y=上、Z=南）的全局指向。
    /// <para>这是"服务端发 ack 姿态"那一侧的唯一算法；<see cref="Rotate"/> 是消费侧，两边配对才有判据可写。</para>
    /// </summary>
    public static QuaternionQuantized FromBasis(in PlanetVector3 east, in PlanetVector3 up, in PlanetVector3 south)
    {
        // 必须是右手正交基，否则转出来不是旋转：位置会被无声地歪掉，比抛异常难查得多
        CheckUnit(east, nameof(east));
        CheckUnit(up, nameof(up));
        CheckUnit(south, nameof(south));
        RequireSmall(PlanetVector3.Dot(east, up), 1e-9, "东·上");
        RequireSmall(PlanetVector3.Dot(east, south), 1e-9, "东·南");
        RequireSmall(PlanetVector3.Dot(up, south), 1e-9, "上·南");
        RequireSmall(PlanetVector3.Dot(PlanetVector3.Cross(east, up) - south, south), 1e-6,
                     "Cross(东,上) 与南不重合（左手系）");

        double m00 = east.X, m01 = up.X, m02 = south.X;
        double m10 = east.Y, m11 = up.Y, m12 = south.Y;
        double m20 = east.Z, m21 = up.Z, m22 = south.Z;
        double trace = m00 + m11 + m22;
        double w, x, y, z;
        if (trace > 0)
        {
            double s = Math.Sqrt(trace + 1.0) * 2;
            w = 0.25 * s;
            x = (m21 - m12) / s; y = (m02 - m20) / s; z = (m10 - m01) / s;
        }
        else if (m00 > m11 && m00 > m22)
        {
            double s = Math.Sqrt(1.0 + m00 - m11 - m22) * 2;
            w = (m21 - m12) / s;
            x = 0.25 * s; y = (m01 + m10) / s; z = (m02 + m20) / s;
        }
        else if (m11 > m22)
        {
            double s = Math.Sqrt(1.0 + m11 - m00 - m22) * 2;
            w = (m02 - m20) / s;
            x = (m01 + m10) / s; y = 0.25 * s; z = (m12 + m21) / s;
        }
        else
        {
            double s = Math.Sqrt(1.0 + m22 - m00 - m11) * 2;
            w = (m10 - m01) / s;
            x = (m02 + m20) / s; y = (m12 + m21) / s; z = 0.25 * s;
        }
        double norm = Math.Sqrt(w * w + x * x + y * y + z * z);
        if (norm <= 1e-300) throw new ArgumentException("基退化成零旋转，造不出姿态");
        return From(x / norm, y / norm, z / norm, w / norm);
    }

    private static void CheckUnit(in PlanetVector3 v, string name)
    {
        double deviation = Math.Abs(v.LengthSquared - 1.0);
        RequireSmall(deviation, 1e-9, name + " 不是单位向量");
    }

    private static void RequireSmall(double value, double tolerance, string what)
    {
        if (Math.Abs(value) > tolerance)
            throw new ArgumentException($"{what} 超出 {tolerance:E0}：实际 {value:E3}");
    }

    /// <summary>
    /// 局部向量 → ECEF：q·v·q⁻¹ 的闭式。
    /// <para>第一项的系数是 (2w²−1) 而不是 w²：用单位四元数恒等式 |vec|² = 1−w² 换下来的，
    /// 漏掉这一步在 w=0（180° 姿态）时会把向量直接归零。</para>
    /// <para><b>先归一再套公式</b>。这条闭式只在 |q|=1 时成立（(2w²−1) 那一项就是用
    /// |vec|² = 1−w² 换出来的），而 Q15 解出来的模长不是正好 1：实测 1−|q| 到 1.5e-5。
    /// 不补这一步，旋转<em>不再保长度</em>，向量被按 (1−1.5e-5) 缩放——在离地 50 km 那一档
    /// 就是约 0.75 m 的径向错，而它不会报错，只会让人站得"稍微不对"。
    /// 归一之后判据量到的基向量残差从 5.79e-5 降到 4.01e-5（剩下的就是纯量化）。</para>
    /// </summary>
    public PlanetVector3 Rotate(in PlanetVector3 local)
    {
        double x = DecodeX, y = DecodeY, z = DecodeZ, w = DecodeW;
        double norm = Math.Sqrt(x * x + y * y + z * z + w * w);
        if (norm <= 1e-12)
            throw new ArgumentException("四元数退化成零：那不是一个旋转，硬转出来是 -v 而不是任何姿态");
        x /= norm; y /= norm; z /= norm; w /= norm;

        PlanetVector3 q = new(x, y, z);
        double dot = PlanetVector3.Dot(local, q);
        return local * (2 * w * w - 1)
             + q * (2 * dot)
             + PlanetVector3.Cross(q, local) * (2 * w);
    }


    public void WriteTo(Span<byte> span)
    {
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(0), X);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(2), Y);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(4), Z);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(6), W);
    }

    public static QuaternionQuantized ReadFrom(ReadOnlySpan<byte> span) => new()
    {
        X = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(0)),
        Y = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(2)),
        Z = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(4)),
        W = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(6)),
    };
}
