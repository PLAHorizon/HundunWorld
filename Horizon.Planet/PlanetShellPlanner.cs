using System;
using System.Collections.Generic;

namespace Horizon.Planet;

/// <summary>一块要画进壳里的 tile：它的层级，加上它哪几条边需要挂裙边。</summary>
public readonly struct ShellTile
{
    public ShellTile(RegionKey key, int quadsPerEdge, IReadOnlyList<RegionEdge> skirts, double distanceCm)
    {
        Key = key;
        QuadsPerEdge = quadsPerEdge;
        Skirts = skirts;
        DistanceCm = distanceCm;
    }

    public RegionKey Key { get; }
    public int QuadsPerEdge { get; }

    /// <summary>要挂裙边的边。规则只有一条：这条边的同级邻居**没被画出来**（那边归更细的块）。</summary>
    public IReadOnlyList<RegionEdge> Skirts { get; }

    /// <summary>tile 中心到玩家的弧长距离（cm），优先级按它排。</summary>
    public double DistanceCm { get; }

    /// <summary>写进 buffer 的顶点数：网格本身 + 每条裙边 2×(每边 quad 数 + 1)。</summary>
    public int VertexCount
    {
        get
        {
            int side = QuadsPerEdge + 1;
            return side * side + Skirts.Count * 2 * side;
        }
    }

    public override string ToString() =>
        $"{Key} q{QuadsPerEdge} d={DistanceCm / 100000.0:0.#}km skirts={Skirts.Count}";
}

/// <summary>一次规划的结果：画哪些块、多少顶点、按由近到远排好。</summary>
public sealed class ShellPlan
{
    internal ShellPlan(IReadOnlyList<ShellTile> tiles, long totalVertices, int skirtCount, bool truncated)
    {
        Tiles = tiles;
        TotalVertices = totalVertices;
        SkirtCount = skirtCount;
        Truncated = truncated;
    }

    /// <summary>由近到远。流送按这个顺序投，第一帧就该看到玩家脚下那块。</summary>
    public IReadOnlyList<ShellTile> Tiles { get; }

    public long TotalVertices { get; }
    public int SkirtCount { get; }

    /// <summary>撞到 <see cref="PlanetShellPlanner.MaxTiles"/> 停止细分。这时"每块都不超允许张角"不再成立，必须让人看见。</summary>
    public bool Truncated { get; }

    /// <summary>按顺序有几块塞得进这一帧的顶点预算（§13.26：成本 ≈ 顶点数 × 0.36 µs）。</summary>
    public int CountWithinBudget(long vertexBudget)
    {
        if (vertexBudget <= 0) return 0;
        long used = 0;
        int n = 0;
        foreach (var tile in Tiles)
        {
            if (used + tile.VertexCount > vertexBudget) break;
            used += tile.VertexCount;
            n++;
        }
        return n;
    }
}

/// <summary>
/// 远/中景壳的 LOD 规划器：给一个玩家方向，算出该画哪些 cube-sphere tile、哪几条边要挂裙边。
/// <para>选层判据不是"距离分档"这种人为台阶，而是**像素**：一条边的弦高在玩家眼里占几个像素，
/// 由 <see cref="PlanetRegionSeams.AllowedSagCm"/> 把"允许 N 像素"换成允许的厘米数。
/// 一块的弦高超过它所在距离的允许值就细分成 4 个子块，直到达标或到 <see cref="MaxLevel"/>。</para>
/// <para>裙边规则只有一条，而且它是几何事实不是约定：同级邻居被画出来 ⇒ 公共顶点焊接 ⇒ 不用挂；
/// 同级邻居没被画出来 ⇒ 那块面积由更细的块接管 ⇒ 粗块这条边必须挂。§13.36 实测过反例：
/// 洞朝外张开，裙边垒反方向、或墙的可见面朝邻居（被剔背面），两种都等于没挂。</para>
/// <para>规划器不碰引擎、不持状态：同一输入必须给同一份计划——判据逐块对比靠这一点。</para>
/// </summary>
public sealed class PlanetShellPlanner
{
    /// <summary>一次计划最多多少块；撞到它就停止细分并在 <see cref="ShellPlan.Truncated"/> 上认账。</summary>
    public const int DefaultMaxTiles = 4096;

    private static readonly RegionEdge[] AllEdges =
        { RegionEdge.UPlus, RegionEdge.UMinus, RegionEdge.VPlus, RegionEdge.VMinus };

    private readonly double _radiusCm;
    private readonly int _quadsPerEdge;
    private readonly int _maxLevel;
    private readonly double _allowedPixels;
    private readonly double _verticalFovDeg;
    private readonly double _screenHeightPx;
    private readonly int _maxTiles;

    public PlanetShellPlanner(double radiusCm, int quadsPerEdge, int maxLevel,
                              double allowedPixels = 1.0, double verticalFovDeg = 60.0,
                              double screenHeightPx = 1080.0, int maxTiles = DefaultMaxTiles)
    {
        if (radiusCm <= 0) throw new ArgumentOutOfRangeException(nameof(radiusCm), radiusCm, "半径必须为正");
        if (quadsPerEdge < 1) throw new ArgumentOutOfRangeException(nameof(quadsPerEdge), quadsPerEdge, "每边至少 1 个 quad");
        if (maxLevel < 0) throw new ArgumentOutOfRangeException(nameof(maxLevel), maxLevel, "层级不能为负");
        if (allowedPixels <= 0) throw new ArgumentOutOfRangeException(nameof(allowedPixels), allowedPixels, "允许张角必须为正");
        if (maxTiles < CubeSphere.FaceCount) throw new ArgumentOutOfRangeException(nameof(maxTiles), maxTiles, "至少得放得下 6 个面");
        _radiusCm = radiusCm;
        _quadsPerEdge = quadsPerEdge;
        _maxLevel = maxLevel;
        _allowedPixels = allowedPixels;
        _verticalFovDeg = verticalFovDeg;
        _screenHeightPx = screenHeightPx;
        _maxTiles = maxTiles;
    }

    public int MaxLevel => _maxLevel;
    public int MaxTiles => _maxTiles;
    public double AllowedPixels => _allowedPixels;

    /// <summary>
    /// 距离 <paramref name="distanceCm"/> 上"允许 N 像素"对应允许的最大层级：弦高随层级 4 倍 4 倍地掉，
    /// 所以这是单调的。超过 <see cref="MaxLevel"/> 就报 MaxLevel（近景该交给 Terrain，不是这里的活）。
    /// </summary>
    public int LevelAllowedAt(double distanceCm)
    {
        double allowed = PlanetRegionSeams.AllowedSagCm(distanceCm, _allowedPixels, _verticalFovDeg, _screenHeightPx);
        for (int level = 0; level <= _maxLevel; level++)
        {
            var probe = new RegionKey(0, level, 0, 0);
            if (PlanetRegionSeams.ChordSagCm(probe, _quadsPerEdge, _radiusCm) <= allowed) return level;
        }
        return _maxLevel;
    }

    /// <summary>从 6 个面出发递归细分。<paramref name="playerDirection"/> 必须非零（内部会归一）。</summary>
    public ShellPlan Build(in PlanetVector3 playerDirection)
    {
        double unit = playerDirection.Length;
        if (unit <= 0) throw new ArgumentException("玩家方向不能是零向量", nameof(playerDirection));
        var player = playerDirection * (1.0 / unit);

        // 第一趟只决定"画哪些块"。裙边必须等集合齐了再判：深度优先时同级邻居可能还没生成，
        // 当场查会把它误判成"没画"，于是给一条本来焊接得好好的边挂上裙边（顶点白花，且判据抓不到原因）。
        var drawn = new List<(RegionKey Key, double Distance)>();
        bool truncated = false;
        for (int face = 0; face < CubeSphere.FaceCount; face++)
            Descend(new RegionKey(face, 0, 0, 0), player, drawn, ref truncated);
        if (drawn.Count > _maxTiles) truncated = true;

        var set = new HashSet<RegionKey>();
        foreach (var (key, _) in drawn) set.Add(key);

        var tiles = new List<ShellTile>(drawn.Count);
        foreach (var (key, distance) in drawn)
        {
            var skirts = new List<RegionEdge>(2);
            foreach (var edge in AllEdges)
            {
                // 同级邻居画出来了 ⇒ 公共顶点焊接 ⇒ 不用挂。
                // 没画出来还分两种：那边被更细的块接管 ⇒ 本块这条边必须挂；
                // 那边被更粗的块接管 ⇒ 该挂的是粗块那一侧，本块挂上就是白花顶点（判据抓到过）。
                if (!PlanetRegions.TryNeighbour(key, edge, out var neighbour)) continue;
                if (set.Contains(neighbour)) continue;
                if (!HasDrawnAncestor(neighbour, set)) skirts.Add(edge);
            }
            tiles.Add(new ShellTile(key, _quadsPerEdge, skirts, distance));
        }

        tiles.Sort((a, b) => a.DistanceCm.CompareTo(b.DistanceCm));
        long verts = 0;
        int skirts2 = 0;
        foreach (var t in tiles) { verts += t.VertexCount; skirts2 += t.Skirts.Count; }
        return new ShellPlan(tiles, verts, skirts2, truncated);
    }

    /// <summary>
    /// 找出方向 <paramref name="direction"/> 落在哪一块上（用该块自己层级的格子归属判，不新增投影 API）。
    /// 裙边的两条几何判据靠它绕开"同级邻居"这个说法，直接问：这个点归哪块、那块多细。
    /// </summary>
    public ShellTile? TileCovering(ShellPlan plan, in PlanetVector3 direction)
    {
        foreach (var tile in plan.Tiles)
        {
            var probe = RegionKey.FromDirection(direction, tile.Key.Level);
            if (probe.Face == tile.Key.Face && probe.X == tile.Key.X && probe.Y == tile.Key.Y)
                return tile;
        }
        return null;
    }

    private void Descend(RegionKey key, in PlanetVector3 player, List<(RegionKey Key, double Distance)> drawn, ref bool truncated)
    {
        if (drawn.Count >= _maxTiles)
        {
            truncated = true;
            drawn.Add((key, ArcToCenterCm(key, player)));
            return;
        }

        double distance = ViewDistanceCm(key, player);
        double sag = PlanetRegionSeams.ChordSagCm(key, _quadsPerEdge, _radiusCm);
        double allowed = PlanetRegionSeams.AllowedSagCm(distance, _allowedPixels, _verticalFovDeg, _screenHeightPx);
        // 弦高只随层级变，允许值随距离变：越远的块允许的弦高越大，所以远处自然粗。
        if (key.Level >= _maxLevel || sag <= allowed)
        {
            drawn.Add((key, ArcToCenterCm(key, player)));
            return;
        }

        for (int dy = 0; dy < 2; dy++)
            for (int dx = 0; dx < 2; dx++)
                Descend(new RegionKey(key.Face, key.Level + 1, key.X * 2 + dx, key.Y * 2 + dy),
                        player, drawn, ref truncated);
    }

    /// <summary>那一格自己没被画，但它的某个祖先被画了 ⇒ 那片面积归更粗的块，本块不需要挂裙边。</summary>
    private static bool HasDrawnAncestor(RegionKey key, HashSet<RegionKey> drawn)
    {
        int x = key.X, y = key.Y;
        for (int level = key.Level - 1; level >= 0; level--)
        {
            x >>= 1; y >>= 1;
            if (drawn.Contains(new RegionKey(key.Face, level, x, y))) return true;
        }
        return false;
    }

    /// <summary>tile 中心到玩家的弧长（cm），用于优先级排序。</summary>
    private double ArcToCenterCm(RegionKey key, in PlanetVector3 player)
    {
        var (u0, u1, v0, v1) = key.UvBounds();
        var dir = CubeSphere.ToDirection(key.Face, (u0 + u1) * 0.5, (v0 + v1) * 0.5);
        double dot = PlanetVector3.Dot(player, dir);
        if (dot > 1.0) dot = 1.0;
        if (dot < -1.0) dot = -1.0;
        return _radiusCm * Math.Acos(dot);
    }

    /// <summary>
    /// 算允许张角时用的视距：<c>max(到块中心的弧长, 本块边长的一半)</c>。
    /// <para>为什么必须有下半个：玩家正下方那块到中心的弧长≈0，"1 个像素"换出来的允许弦高也就≈0，
    /// 于是规划器会一路把它细分到 MaxLevel 且每层都还"不达标"——判据在这种距离下退化成了空转。
    /// 而这块的缝本来就不在脚下那个点上，它在块的另一头，离玩家至少半个块远。
    /// 有了这个下限，"允许 N 像素"自己就把最深层级卡住（弦高随 4 的倍数掉，下限随 2 的倍数掉），
    /// 不再需要靠 MaxLevel 兜底。</para>
    /// </summary>
    private double ViewDistanceCm(RegionKey key, in PlanetVector3 player)
    {
        var (u0, u1, v0, v1) = key.UvBounds();
        double cellArc = _radiusCm * (u1 - u0) * (Math.PI / 4.0);   // uv 跨度 2 ↔ 一个面 90°
        return Math.Max(ArcToCenterCm(key, player), cellArc * 0.5);
    }
}
