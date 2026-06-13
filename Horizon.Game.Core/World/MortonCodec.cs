using System;
using System.Runtime.CompilerServices;

namespace Horizon.Game.Core.World;

/// <summary>
/// 3D Morton（Z-order）编解码（P2-a）。<br/>
/// 把 (X, Y, Z) 三个 21 位有符号块坐标打包到一个 <see cref="ulong"/> 的低 63 位中；
/// 最高位保留作符号位。编码后的键满足空间局部性：临近 chunk 在 Morton 空间也临近，利于 Redis key 扫描。
/// </summary>
/// <remarks>
/// 约束：
/// <list type="bullet">
///   <item>每个轴的块坐标范围 <c>[-2^20, 2^20-1]</c>，即 <c>[-1048576, 1048575]</c>；
///   对应 <c>ChunkCell = 16m</c> 时世界半径 ≈ 16 km × 1M ≈ 16 777 km，远大于单集群规划上限。</item>
///   <item>编码先做"无符号偏移"（加 2^20），再做位交错。解码反过来。</item>
///   <item>实现使用"魔法位运算"展开至 21 位（每轴贡献 21 位 → 合计 63 位）。</item>
/// </list>
/// </remarks>
public static class MortonCodec
{
    /// <summary>每轴有符号位宽（必须 ≤ 21）。</summary>
    public const int AxisBits = 21;

    /// <summary>每轴取值范围上界（含），即 <c>2^20 - 1</c>。</summary>
    public const int AxisMax = (1 << (AxisBits - 1)) - 1;

    /// <summary>每轴取值范围下界（含），即 <c>-2^20</c>。</summary>
    public const int AxisMin = -(1 << (AxisBits - 1));

    /// <summary>无符号偏移量：把 [AxisMin, AxisMax] 平移到 [0, 2^AxisBits - 1]。</summary>
    public const int AxisBias = 1 << (AxisBits - 1);

    /// <summary>每轴无符号范围（不含）上界。</summary>
    public const ulong AxisUnsignedLimit = 1UL << AxisBits;

    /// <summary>
    /// 把 (x, y, z) 块坐标编码成 Morton 键。超范围抛 <see cref="ArgumentOutOfRangeException"/>。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Encode3D(int x, int y, int z)
    {
        if (x < AxisMin || x > AxisMax)
            throw new ArgumentOutOfRangeException(nameof(x), x, $"X 超出 [{AxisMin},{AxisMax}]。");
        if (y < AxisMin || y > AxisMax)
            throw new ArgumentOutOfRangeException(nameof(y), y, $"Y 超出 [{AxisMin},{AxisMax}]。");
        if (z < AxisMin || z > AxisMax)
            throw new ArgumentOutOfRangeException(nameof(z), z, $"Z 超出 [{AxisMin},{AxisMax}]。");

        var ux = (ulong)(x + AxisBias);
        var uy = (ulong)(y + AxisBias);
        var uz = (ulong)(z + AxisBias);
        return Part1By2(ux) | (Part1By2(uy) << 1) | (Part1By2(uz) << 2);
    }

    /// <summary>
    /// 解码 Morton 键回 (x, y, z) 块坐标。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (int X, int Y, int Z) Decode3D(ulong key)
    {
        var ux = Compact1By2(key);
        var uy = Compact1By2(key >> 1);
        var uz = Compact1By2(key >> 2);
        return (
            (int)ux - AxisBias,
            (int)uy - AxisBias,
            (int)uz - AxisBias);
    }

    /// <summary>
    /// 判定给定 (x, y, z) 是否落在合法块坐标范围内。
    /// </summary>
    public static bool IsInRange(int x, int y, int z) =>
        x >= AxisMin && x <= AxisMax &&
        y >= AxisMin && y <= AxisMax &&
        z >= AxisMin && z <= AxisMax;

    // -----------------------------------------------------------------------
    // "Part1By2 / Compact1By2" 21-bit 版：参考 Fabian Giesen's "morton-nd" 思路，
    // 把低 21 位交错成间隔 3 的位布局，便于与其它轴做 OR 合并。
    // -----------------------------------------------------------------------

    /// <summary>在 21 位输入 <c>n</c> 的每个位中间插入 2 个 0（n 的位 i → 输出的位 3i）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Part1By2(ulong n)
    {
        n &= 0x1FFFFFUL;                                  // 保留低 21 位
        n = (n | (n << 32)) & 0x1F00000000FFFFUL;         // 0000_0000_0001_1111_0000_0000_0000_0000_0000_0000_1111_1111_1111_1111
        n = (n | (n << 16)) & 0x1F0000FF0000FFUL;
        n = (n | (n << 8))  & 0x100F00F00F00F00FUL;
        n = (n | (n << 4))  & 0x10C30C30C30C30C3UL;
        n = (n | (n << 2))  & 0x1249249249249249UL;
        return n;
    }

    /// <summary>反向收集 Part1By2 的输出，把间隔 3 的位压回低 21 位。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Compact1By2(ulong n)
    {
        n &= 0x1249249249249249UL;
        n = (n ^ (n >> 2))  & 0x10C30C30C30C30C3UL;
        n = (n ^ (n >> 4))  & 0x100F00F00F00F00FUL;
        n = (n ^ (n >> 8))  & 0x1F0000FF0000FFUL;
        n = (n ^ (n >> 16)) & 0x1F00000000FFFFUL;
        n = (n ^ (n >> 32)) & 0x1FFFFFUL;
        return n;
    }
}
