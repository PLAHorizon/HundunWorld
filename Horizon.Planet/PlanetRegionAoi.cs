using System;
using System.Collections.Generic;
using System.Globalization;
using Horizon.Planet.Authority;

namespace Horizon.Planet;

/// <summary>
/// 区域 AOI 的第二层（§6.4：区域 → 100 m cell → 实体）。
/// <para><b>这一层刻意不改变"看得见"的下界</b>：同一区域内一律可见，跨到邻区的实体也仍然可见
/// （P1 那条"双客户端跨区不丢实体"的验收判据靠的就是这个下界）。这一层给的是
/// <em>要预载/要模拟哪些 cell</em> 的筛子——邻区只取靠着我们这一侧的那几列，
/// 而不是整块 6 km 见方的区域。把筛子当成可见性规则会把"三公里外的玩家"直接从视野里删掉，
/// 那是玩法决定，不是这一层能替它做的。</para>
/// <para>几何一律<em>量</em>出来而不是按面号推：局部 X 是"东"、局部 Z 是"南"，
/// 而六个面的 uv 轴向各不相同（§13.15 那次实机事故就是把"局部 +X"当成"东=UPlus"用到了所有面上）。
/// 所以"某条边在局部帧里朝哪个轴、哪个符号"必须由邻居中心在该帧里的实际位置决定。</para>
/// </summary>
public static class PlanetRegionAoi
{
    /// <summary>局部轴：0 = X（面内 u 向；四个非极地面上就是地理东），2 = Z（沿边那一维）。Y 是"上"，AOI 用不到。</summary>
    public const int AxisX = 0;
    public const int AxisZ = 2;

    /// <summary>
    /// 既有平面世界的网络视野半径：28 个 chunk × 16 m = 448 m
    /// （<c>DsWorldSession.AoiRadiusChunks</c> 与 <c>SyncWorldHandler.AoiRadiusChunks</c> 都是 28，
    /// 两处注释互相要求一致）。星球这一层取它作**下界之一**，不能比现在还没上线时更看不见人。
    /// </summary>
    public const long LegacyViewRadiusCm = 28 * 1600;

    /// <summary>
    /// 近景地形的加载半跨：窗口是 5×5 块 patch，每块 508 m，所以半跨 = 1270 m。
    /// <para>这是余量必须<em>至少</em>这么大的一条硬理由：客户端会把地形加载到 1270 m 外，
    /// 而 AOI 只订阅到 448 m 的话，"地上有人但服务端没把他发下来"就必然发生。</para>
    /// </summary>
    public const long NearFieldLoadHalfSpanCm =
        PlanetConstants.NearPatchWindowEdge * (long)PlanetConstants.TerrainPatchSizeCm / 2;

    /// <summary>
    /// 默认余量：两个锚里取大的，再向上取整到整格 → 1300 m。
    /// <para>为什么不是"越大越好"：邻区在 L14 的半跨实测约 3.05 km，余量一旦接近或超过它，
    /// "只订阅边界几列"就退化回"订阅整块邻区"，这一层就白做（判据 <c>默认余量真的在筛</c> 钉住这一点）。</para>
    /// </summary>
    public const long DefaultMarginCm = 13 * PlanetRegionAuthority.CellSizeCm;

    /// <summary>
    /// 订阅集里除了本区之外的那些区域，各自要加载哪几列 cell（§6.4 的第二层）。
    /// <para>本区不在返回值里——它整块都订阅，列出来只会把账撑大。</para>
    /// </summary>
    public static List<CellKey> BoundaryCellSubscriptions(RegionKey own, IReadOnlyList<RegionKey> subscribedRegions,
                                                          long marginCm = DefaultMarginCm,
                                                          long radiusCm = PlanetConstants.PlanetRadiusCm)
    {
        if (subscribedRegions is null) throw new ArgumentNullException(nameof(subscribedRegions));
        var list = new List<CellKey>();
        foreach (var region in subscribedRegions)
        {
            if (region == own) continue;                       // 本区整块订阅，不列边界格
            // 从本区看过去的那条边：BoundaryCells 返回的是"邻居那一侧靠边的几列"
            if (!PlanetRegions.TrySharedEdge(own, region, out var edgeFromOwn, out _))
                continue;                                      // 不相邻的区域不订阅（重叠旧区按构造总是相邻）
            list.AddRange(BoundaryCells(own, edgeFromOwn, marginCm, radiusCm));
        }
        return list;
    }

    /// <summary>
    /// 该边在区域局部帧里朝哪个轴、哪个符号走才能到邻居。
    /// <para>判据是"邻居中心在本帧水平坐标里的主导轴"，不是面号查表：跨面时 uv 轴会翻转，
    /// 查表那份翻转规则正是最容易写错的地方。</para>
    /// </summary>
    public static (int Axis, int Sign) EdgeDirection(RegionKey region, RegionEdge edge,
                                                    long radiusCm = PlanetConstants.PlanetRadiusCm)
    {
        if (!PlanetRegions.TryNeighbour(region, edge, out var neighbour))
            throw new InvalidOperationException($"{region} 沿 {edge} 没有邻居，闭合球面被算破了");

        var frame = RegionFrame.Build(region, radiusCm);
        var local = frame.LocalFromGlobalCm(neighbour.CenterDirection() * radiusCm);
        double dx = local.X, dz = local.Z;
        if (Math.Abs(dx) < 1e-6 && Math.Abs(dz) < 1e-6)
            throw new InvalidOperationException($"{region} 与 {neighbour} 的水平位置重合，边方向判不出来");

        return Math.Abs(dx) >= Math.Abs(dz)
            ? (AxisX, dx >= 0 ? 1 : -1)
            : (AxisZ, dz >= 0 ? 1 : -1);
    }

    /// <summary>
    /// 区域四角在局部帧里的水平半跨（厘米）。取角点而不是中心跨度，所以是<b>上界</b>：
    /// 筛子宁可多算一列，也不能少算——少算那一列就是"边界上的玩家突然不动了"。
    /// </summary>
    public static (double HalfX, double HalfZ) LocalHalfExtents(RegionKey key,
                                                                long radiusCm = PlanetConstants.PlanetRadiusCm)
    {
        var frame = RegionFrame.Build(key, radiusCm);
        double halfX = 0, halfZ = 0;
        for (int corner = 0; corner < 4; corner++)
        {
            var local = frame.LocalFromGlobalCm(key.CornerDirection(corner) * radiusCm);
            halfX = Math.Max(halfX, Math.Abs(local.X));
            halfZ = Math.Max(halfZ, Math.Abs(local.Z));
        }
        if (halfX <= 0 || halfZ <= 0)
            throw new InvalidOperationException($"{key} 的局部半跨退化：({halfX}, {halfZ})");
        return (halfX, halfZ);
    }

    /// <summary>
    /// 该轴向上 cell 的下标范围（<b>闭区间</b>，两端都按 floor 取，所以覆盖 [-half, +half] 一个不漏）。
    /// <para>上一版用 <c>-ceil(half/S)</c> 起、<c>ceil(2·half/S)</c> 列，在 half 不是整数倍时会漏掉
    /// 靠正方向那一列——而那一列正是共享边所在的列，漏它就是"边界上的玩家突然不动了"。</para>
    /// </summary>
    public static (int First, int Last) CellRange(double halfCm, int cellSizeCm = PlanetRegionAuthority.CellSizeCm)
    {
        if (cellSizeCm <= 0) throw new ArgumentOutOfRangeException(nameof(cellSizeCm), cellSizeCm, "cell 边长必须为正");
        int first = (int)Math.Floor(-halfCm / cellSizeCm);
        int last = (int)Math.Floor(halfCm / cellSizeCm);
        if (last < first) throw new InvalidOperationException($"半跨 {halfCm:0} cm 连一格都装不下");
        return (first, last);
    }

    /// <summary>某区域在某轴向上有多少列 cell。</summary>
    public static int CellColumns(RegionKey key, int axis, long radiusCm = PlanetConstants.PlanetRadiusCm)
    {
        var (halfX, halfZ) = LocalHalfExtents(key, radiusCm);
        var (first, last) = CellRange(AxisOf(axis, halfX, halfZ));
        return last - first + 1;
    }

    /// <summary>
    /// 某个 cell 是否贴着该区域的这条边（离边 ≤ <paramref name="marginCm"/>）。
    /// <para>cell 号 k 覆盖局部区间 <c>[k·S, (k+1)·S)</c>，所以判断用的是**靠近边那一端**的坐标。</para>
    /// </summary>
    public static bool CellNearEdge(int cellIndex, int axis, int sign, double halfCm,
                                    double marginCm, int cellSizeCm = PlanetRegionAuthority.CellSizeCm)
    {
        if (cellSizeCm <= 0) throw new ArgumentOutOfRangeException(nameof(cellSizeCm), cellSizeCm, "cell 边长必须为正");
        if (marginCm < 0) throw new ArgumentOutOfRangeException(nameof(marginCm), marginCm, "余量不能为负");
        if (sign != 1 && sign != -1) throw new ArgumentOutOfRangeException(nameof(sign), sign, "符号只能是 ±1");

        double low = cellIndex * (double)cellSizeCm;
        double high = low + cellSizeCm;
        // 正号那侧的边在 +half，负号那侧在 -half：取靠边的那一端
        double towardEdge = sign > 0 ? high : -low;
        return halfCm - towardEdge <= marginCm;
    }

    /// <summary>
    /// 邻区里"靠着我们这一侧"的 cell 集合：从共享边往里 <paramref name="marginCm"/> 这几列。
    /// <para>返回的 <see cref="CellKey"/> 用的是<em>邻区自己</em>的局部帧——订阅侧要的就是
    /// "在邻居那一侧加载哪几列"，而不是在我们这侧看过去的投影。</para>
    /// </summary>
    public static List<CellKey> BoundaryCells(RegionKey region, RegionEdge towardsNeighbour,
                                              double marginCm, long radiusCm = PlanetConstants.PlanetRadiusCm)
    {
        if (marginCm <= 0)
            throw new ArgumentOutOfRangeException(nameof(marginCm), marginCm, "余量必须为正");

        var neighbour = PlanetRegions.TryNeighbour(region, towardsNeighbour, out var n)
            ? n
            : throw new InvalidOperationException($"{region} 沿 {towardsNeighbour} 没有邻居");

        // 邻居那一侧的边方向：用共边关系拿到"从邻居回到本区"的那条边（§13.13：它不一定等于对边）
        if (!PlanetRegions.TrySharedEdge(region, neighbour, out _, out var edgeBack))
            throw new InvalidOperationException($"{region} 与 {neighbour} 不共边");

        var (axis, sign) = EdgeDirection(neighbour, edgeBack, radiusCm);
        var (halfX, halfZ) = LocalHalfExtents(neighbour, radiusCm);
        double half = AxisOf(axis, halfX, halfZ);
        int columns = CellColumns(neighbour, axis, radiusCm);

        var list = new List<CellKey>();
        int S = PlanetRegionAuthority.CellSizeCm;
        var (first, last) = CellRange(half, S);
        var otherHalf = AxisOf(axis == AxisX ? AxisZ : AxisX,
                               LocalHalfExtents(neighbour, radiusCm).HalfX,
                               LocalHalfExtents(neighbour, radiusCm).HalfZ);
        var (otherFirst, otherLast) = CellRange(otherHalf, S);
        for (int index = first; index <= last; index++)
        {
            if (!CellNearEdge(index, axis, sign, half, marginCm, S)) continue;
            // 沿边那一维要铺满整条共享边：否则斜着走过来的人只在角上一格订阅，会突然看不见
            for (int other = otherFirst; other <= otherLast; other++)
            {
                int cx = axis == AxisX ? index : other;
                int cz = axis == AxisX ? other : index;
                list.Add(new CellKey(neighbour, cx, cz));
            }
        }
        return list;
    }

    /// <summary>
    /// 这一对 cell 是否落在彼此的边界订阅里。<b>对称性由构造保证</b>：
    /// 两侧都要求"自己贴着共享边"，所以互换观察者与被观察者不会改变结论。
    /// <para>同一区域内直接返回 true——这一层不裁区域内的可见性（见类型注释）。</para>
    /// </summary>
    public static bool Subscribes(CellKey viewer, CellKey target, double marginCm,
                                  long radiusCm = PlanetConstants.PlanetRadiusCm)
    {
        if (marginCm <= 0) throw new ArgumentOutOfRangeException(nameof(marginCm), marginCm, "余量必须为正");
        if (viewer.Region == target.Region) return true;
        if (viewer.Region.Level != target.Region.Level)
            throw new ArgumentException($"AOI 只在同层级区域之间比较：{viewer.Region} vs {target.Region}",
                                        nameof(target));
        if (!PlanetRegions.TrySharedEdge(viewer.Region, target.Region, out var edgeFromViewer, out var edgeFromTarget))
            return false;

        var (halfX, halfZ) = LocalHalfExtents(target.Region, radiusCm);
        var (axis, sign) = EdgeDirection(target.Region, edgeFromTarget, radiusCm);
        int index = axis == AxisX ? target.Cx : target.Cz;
        if (!CellNearEdge(index, axis, sign, AxisOf(axis, halfX, halfZ), marginCm)) return false;

        var viewerFrame = LocalHalfExtents(viewer.Region, radiusCm);
        var (vAxis, vSign) = EdgeDirection(viewer.Region, edgeFromViewer, radiusCm);
        int vIndex = vAxis == AxisX ? viewer.Cx : viewer.Cz;
        return CellNearEdge(vIndex, vAxis, vSign, AxisOf(vAxis, viewerFrame.HalfX, viewerFrame.HalfZ), marginCm);
    }

    private static double AxisOf(int axis, double halfX, double halfZ) => axis switch
    {
        AxisX => halfX,
        AxisZ => halfZ,
        _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, "局部水平轴只有 X 与 Z"),
    };

    /// <summary>一行摘要，给判据与实机日志用。</summary>
    public static string Describe(RegionKey region, RegionEdge edge, double marginCm,
                                  long radiusCm = PlanetConstants.PlanetRadiusCm)
    {
        var (axis, sign) = EdgeDirection(region, edge, radiusCm);
        var cells = BoundaryCells(region, edge, marginCm, radiusCm);
        return string.Format(CultureInfo.InvariantCulture,
            "aoi {0} edge={1} axis={2} sign={3} margin={4:0} m cells={5}",
            region, edge, axis, sign, marginCm / 100.0, cells.Count);
    }
}
