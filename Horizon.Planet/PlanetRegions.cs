using System;
using System.Collections.Generic;
using System.Globalization;
using Horizon.Planet.Wire;

namespace Horizon.Planet;

/// <summary>区域在自己面上的四条边（面局部轴向，不是地理方位）。</summary>
public enum RegionEdge
{
    UMinus = 0,
    UPlus = 1,
    VMinus = 2,
    VPlus = 3,
}

/// <summary>一次迁移请求的判定结果，带着"为什么"与反查到的事实，供服务端写日志。</summary>
public readonly struct TransferDecision
{
    public TransferRejectReason Reason { get; init; }
    public bool Accepted => Reason == TransferRejectReason.None;

    /// <summary>请求里的全局点实际落在哪个区域（协议说 to 必须等于它）。</summary>
    public RegionKey RegionOfClaimedPoint { get; init; }
    public RegionKey From { get; init; }
    public RegionKey To { get; init; }
    public double RadiusCm { get; init; }

    /// <summary>点到球面的偏离（厘米）：允许在大气里飞，但不许是天外的坐标。</summary>
    public double OffSurfaceCm { get; init; }

    public override string ToString() => string.Format(CultureInfo.InvariantCulture,
        "transfer {0}->{1} reason={2} claimed={3} radius={4:F1} km off={5:F1} km",
        From, To, Reason, RegionOfClaimedPoint, RadiusCm / 100000.0, OffSurfaceCm / 100000.0);
}

public enum TransferRejectReason
{
    None = 0,
    MalformedRegion,
    MalformedPoint,
    LevelMismatch,
    PointNotInTarget,
    SameRegion,
    NotAdjacent,
    TooFarFromSurface,
}

/// <summary>
/// 区域相邻关系（cube-sphere 跨面）。
/// <para><see cref="RegionKey.TryNeighbour"/> 只在同面内移 x/y，面边缘一律返回 false——那是给"同面索引算术"
/// 用的，不是相邻关系：立方体表面是闭合的，<b>每一块区域都恰有 4 个共边邻居</b>，跨面那几块不能被判成"没有邻居"。</para>
/// <para>跨面的求法刻意避开两个歧义点：① 不采"边的中点"——那条点正好落在两个面的公共棱上，
/// <see cref="CubeSphere.FaceOf"/> 只能靠并列时的先后规则硬选一个面；② 不采"角点"——三个区域在立方体角上相遇，
/// 任何角点采样都是赌运气。所以沿边法线向外推出<b>半个格子</b>再反查：那点已经进到邻面内部，唯一确定。</para>
/// </summary>
public static class PlanetRegions
{
    /// <summary>对角邻接（3 块区域共一个立方体角）留到有需求的时候再定：必须由两次跨边走位复合出来，
    /// 不能靠角点采样。这里先把这个缺口写成可断言的常量，免得被当成"忘了实现"。</summary>
    public const int EdgeNeighbourCount = 4;

    public static RegionEdge Opposite(RegionEdge edge) => edge switch
    {
        RegionEdge.UMinus => RegionEdge.UPlus,
        RegionEdge.UPlus => RegionEdge.UMinus,
        RegionEdge.VMinus => RegionEdge.VPlus,
        _ => RegionEdge.VMinus,
    };

    /// <summary>共边邻居。跨面时结果会在另一个面上；level 用入参区域的 level。</summary>
    public static bool TryNeighbour(RegionKey key, RegionEdge edge, out RegionKey neighbour)
    {
        neighbour = default;
        var (u0, u1, v0, v1) = key.UvBounds();
        double step = u1 - u0;                              // 一格的角度宽（cube 面坐标，不是球面角）
        double midU = (u0 + u1) * 0.5, midV = (v0 + v1) * 0.5;

        // 推出半格：u=±1 那条棱本身属于两个面，正好落在棱上的采样是歧义的
        double u = edge switch
        {
            RegionEdge.UMinus => u0 - step * 0.5,
            RegionEdge.UPlus => u1 + step * 0.5,
            _ => midU,
        };
        double v = edge switch
        {
            RegionEdge.VMinus => v0 - step * 0.5,
            RegionEdge.VPlus => v1 + step * 0.5,
            _ => midV,
        };

        var candidate = RegionKey.FromDirection(CubeSphere.ToDirection(key.Face, u, v), key.Level);
        if (candidate == key) return false;                // 没跨出去 = 采样点算错了，宁可报失败
        neighbour = candidate;
        return true;
    }

    /// <summary>四个共边邻居。返回值恒为该长度：闭合面上不存在"少一个邻居"的区域。</summary>
    public static List<RegionKey> EdgeNeighbours(RegionKey key)
    {
        var list = new List<RegionKey>(EdgeNeighbourCount);
        foreach (RegionEdge edge in Edges)
        {
            if (!TryNeighbour(key, edge, out var neighbour))
                throw new InvalidOperationException($"{key} 沿 {edge} 没有邻居，闭合球面被算破了");
            list.Add(neighbour);
        }
        return list;
    }

    private static readonly RegionEdge[] Edges =
    {
        RegionEdge.UMinus, RegionEdge.UPlus, RegionEdge.VMinus, RegionEdge.VPlus,
    };

    /// <summary>
    /// 邻接是对称的：从 a 跨 edge 到 b，则 b 必有<b>唯一</b>一条边跨回 a。
    /// 返回那条边；不邻接时返回 false。唯一性也判——如果两条边都能回到同一个 a，说明网格自指，
    /// 迁移校验会因此放行错误的邻居。
    /// </summary>
    public static bool TrySharedEdge(RegionKey a, RegionKey b, out RegionEdge edgeFromA, out RegionEdge edgeFromB)
    {
        edgeFromA = default;
        edgeFromB = default;
        if (a == b || a.Level != b.Level) return false;

        int hits = 0;
        foreach (RegionEdge edge in Edges)
        {
            if (!TryNeighbour(a, edge, out var n)) continue;
            if (n != b) continue;
            int back = 0;
            RegionEdge backEdge = default;
            foreach (RegionEdge other in Edges)
            {
                if (TryNeighbour(b, other, out var m) && m == a)
                {
                    back++;
                    backEdge = other;
                }
            }
            if (back != 1) return false;                   // 0 或 ≥2 个回程边：邻接关系不干净
            if (hits == 0)
            {
                edgeFromA = edge;
                edgeFromB = backEdge;
            }
            hits++;
        }
        return hits == 1;                                  // 恰好一条 a→b 的跨边
    }

    public static bool AreEdgeAdjacent(RegionKey a, RegionKey b) =>
        TrySharedEdge(a, b, out _, out _);

    /// <summary>
    /// 服务端对一次区域迁移请求的判定。<b>不</b>信任客户端报的 to：先用全局点反查真正的区域，
    /// 再要求它与 from 共边。<see cref="TransferDecision.ToString"/> 带齐反查结果，日志能直接答"为什么拒"。
    /// </summary>
    public static TransferDecision ValidateTransfer(in RegionTransferRequest request,
                                                    long radiusCm = PlanetConstants.PlanetRadiusCm,
                                                    int level = PlanetConstants.DefaultRegionLevel,
                                                    double maxOffSurfaceCm = MaxTransferAltitudeCm)
    {
        // regionId 是客户端送来的位打包值：解不开就是坏包，不能让异常穿过服务端处理逻辑
        RegionKey from, to;
        try
        {
            from = RegionKey.FromId(request.FromRegionId);
            to = RegionKey.FromId(request.ToRegionId);
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException || e is ArithmeticException)
        {
            return new TransferDecision { Reason = TransferRejectReason.MalformedRegion,
                                          OffSurfaceCm = double.NaN };
        }
        var p = request.GlobalCm.ToVector();
        double len = p.Length;
        var malformed = new TransferDecision
        {
            Reason = TransferRejectReason.MalformedPoint,
            From = from,
            To = to,
            RadiusCm = double.IsFinite(len) ? len : 0.0,
            OffSurfaceCm = double.NaN,
        };
        if (!double.IsFinite(len) || len <= 1e-300) return malformed;

        double off = len - radiusCm;
        var dir = p / len;
        var claimed = RegionKey.FromDirection(dir, level);

        if (from.Level != level || to.Level != level)
            return new TransferDecision { Reason = TransferRejectReason.LevelMismatch, From = from, To = to,
                                          RegionOfClaimedPoint = claimed, RadiusCm = len, OffSurfaceCm = off };
        // 允许在大气里飞/跳，但不许拿一个天外（或地心）的坐标来骗落点
        if (Math.Abs(off) > maxOffSurfaceCm)
            return new TransferDecision { Reason = TransferRejectReason.TooFarFromSurface, From = from, To = to,
                                          RegionOfClaimedPoint = claimed, RadiusCm = len, OffSurfaceCm = off };
        // 客户端算错区域（含把 from 直接抄进 to）：以反查结果为准
        if (claimed != to)
            return new TransferDecision { Reason = TransferRejectReason.PointNotInTarget, From = from, To = to,
                                          RegionOfClaimedPoint = claimed, RadiusCm = len, OffSurfaceCm = off };
        if (from == to)
            return new TransferDecision { Reason = TransferRejectReason.SameRegion, From = from, To = to,
                                          RegionOfClaimedPoint = claimed, RadiusCm = len, OffSurfaceCm = off };
        if (!AreEdgeAdjacent(from, to))
            return new TransferDecision { Reason = TransferRejectReason.NotAdjacent, From = from, To = to,
                                          RegionOfClaimedPoint = claimed, RadiusCm = len, OffSurfaceCm = off };

        return new TransferDecision { Reason = TransferRejectReason.None, From = from, To = to,
                                      RegionOfClaimedPoint = claimed, RadiusCm = len, OffSurfaceCm = off };
    }
    /// <summary>迁移请求允许离海面最远多少厘米：覆盖"飞行/高空"，同时挡住天外坐标。</summary>
    public const double MaxTransferAltitudeCm = 200_000_000;      // 2000 km，仍远小于星球半径

    /// <summary>给日志用的一行相邻关系摘要。</summary>
    public static string DescribeNeighbours(RegionKey key)
    {
        var parts = new List<string>();
        foreach (RegionEdge edge in Edges)
        {
            parts.Add(TryNeighbour(key, edge, out var n) ? $"{edge}={n}" : $"{edge}=none");
        }
        return string.Format(CultureInfo.InvariantCulture, "neighbours({0}) {1}", key, string.Join(" ", parts));
    }
}
