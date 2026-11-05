using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Horizon.Planet;
using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>
/// 整颗星球远景壳的内存账（§9.1 那行"整球网格的显存/内存峰值没量"的补齐）。
/// <para><b>这是按真实上传数组算出来的，不是公式估算</b>：逐块 <c>BuildTile</c> + 逐边 <c>BuildSkirt</c>，
/// 再数实际数组长度。配置刻意取引擎回执那份（quads=8 / maxLevel=5 / allowedPixels=1 / lat30 lon120），
/// 并且把回执里的 105 块 / 8973 顶点 / 裙边占比 5.22% 当判据用——离线量的和实机量的必须是同一组几何，
/// 否则这组数字只是自说自话。</para>
/// <para>字节模型只算 <c>Mesh.UpdateMesh(Float3[] pos, UInt32[] idx, Float3[] nrm)</c> 这三份数组
/// （位置 12 + 法线 12 + 下标 4），所以它是<b>下界</b>：引擎自己那份顶点流布局、UV/切线、
/// 以及 LOD 层级结构还没算进来。写清楚这一点，免得这条判据被当成 profiler 读数用。</para>
/// </summary>
public class PlanetShellMemoryBudgetTests
{
    private const long Radius = PlanetConstants.PlanetRadiusCm;
    private const int Quads = 8;
    private const int MaxLevel = 5;
    private const double AllowedPixels = 1.0;
    private const long BudgetVertsPerFrame = 900;

    // 与 PlanetShellDriver.BuildTile 同一份形状：位置 Float3 + 法线 Float3 + 下标 UInt32
    private const int BytesPerVertex = 12 + 12;
    private const int BytesPerIndex = 4;

    private static ShellPlan ProductionPlan()
    {
        var planner = new PlanetShellPlanner(Radius, Quads, MaxLevel, AllowedPixels, 60.0, 1080.0);
        var dir = Geodetic.DirectionFromLatLon(30.0 * Math.PI / 180.0, 120.0 * Math.PI / 180.0);
        return planner.Build(dir);
    }

    private readonly struct TileBytes
    {
        public TileBytes(long verts, long tris, long skirtVerts, long skirtTris)
        {
            Verts = verts; Tris = tris; SkirtVerts = skirtVerts; SkirtTris = skirtTris;
        }
        public long Verts { get; }
        public long Tris { get; }
        public long SkirtVerts { get; }
        public long SkirtTris { get; }
        public long UploadBytes => (Verts + SkirtVerts) * BytesPerVertex
                                 + (Tris + SkirtTris) * 3 * BytesPerIndex;   // 一个三角形三个下标
    }

    private static List<TileBytes> Materialize(ShellPlan plan)
    {
        // 常数 0 高程：与引擎侧 PlanetShellDriver 的 FlatHeight 同一档（起伏会把裙边深度一起改掉，
        // 那笔账要单独量，不能混进"纯 LOD 的显存账"里）
        var terrain = new PlanetTerrain(Radius, new ConstantHeightSource(0.0));
        var perTile = new List<TileBytes>(plan.Tiles.Count);
        foreach (var tile in plan.Tiles)
        {
            var mesh = PlanetRegionMesh.BuildTile(tile.Key, Quads, terrain);
            long skirtVerts = 0, skirtTris = 0;
            foreach (var edge in tile.Skirts)
            {
                double depth = PlanetRegionSeams.SkirtDepthForEdgeCm(tile.Key, Quads, terrain, edge);
                var s = PlanetRegionSeams.BuildSkirt(tile.Key, Quads, terrain, edge, depth);
                skirtVerts += s.Positions.Length;
                skirtTris += s.Triangles.Length / 3;
            }
            perTile.Add(new TileBytes(mesh.Positions.Length, mesh.Triangles.Length / 3, skirtVerts, skirtTris));
        }
        return perTile;
    }

    [Fact]
    public void 离线复现引擎那组配置的块数与顶点数()
    {
        var plan = ProductionPlan();
        // 这两个数来自实机回执 receipt_SH_static.txt：plan[initial] tiles=105 verts=8973 skirts=26
        Assert.Equal(105, plan.Tiles.Count);
        Assert.Equal(8973, plan.TotalVertices);
        Assert.Equal(26, plan.SkirtCount);
        Assert.False(plan.Truncated);
    }

    [Fact]
    public void 整球壳的上传字节是真实数组长度算出来的()
    {
        var plan = ProductionPlan();
        var perTile = Materialize(plan);

        long verts = perTile.Sum(t => t.Verts + t.SkirtVerts);
        long tris = perTile.Sum(t => t.Tris + t.SkirtTris);
        long bytes = perTile.Sum(t => t.UploadBytes);
        long peakTile = perTile.Max(t => t.UploadBytes);

        // 顶点数必须与 plan 的账一致：这条把"数组长度的账"和"规划器的账"接上，
        // 否则下面那句"零点几兆"量的可能是另一组几何
        Assert.Equal(plan.TotalVertices, verts);

        var mb = bytes / (1024.0 * 1024.0);
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "SHELL_MEMORY tiles={0} verts={1} tris={2} upload_bytes={3} ({4:0.###} MB) peak_tile_bytes={5}",
            perTile.Count, verts, tris, bytes, mb, peakTile));

        Assert.True(bytes < 1024 * 1024, $"整颗 10× 星球的远景壳应当远小于 1 MB，实际 {bytes} 字节");
        Assert.True(peakTile < 16 * 1024, $"单块 tile 的上传缓冲应当只有几 KB，实际 {peakTile} 字节");

        // 下界这条也要成立：字节数确实由顶点与下标给，不是随手写的一个数
        Assert.Equal(verts * BytesPerVertex + tris * 3 * BytesPerIndex, bytes);
    }

    [Fact]
    public void 裙边的顶点占比与实机回执一致()
    {
        var plan = ProductionPlan();
        var perTile = Materialize(plan);
        long tileVerts = perTile.Sum(t => t.Verts);
        long skirtVerts = perTile.Sum(t => t.SkirtVerts);
        double share = 100.0 * skirtVerts / (tileVerts + skirtVerts);

        // 回执：tile_verts=8505 skirt_verts=468 skirt_share=5.22%
        Assert.Equal(8505, tileVerts);
        Assert.Equal(468, skirtVerts);
        Assert.True(Math.Abs(share - 5.22) < 0.05, $"裙边占比应当复现实机的 5.22%，实际 {share:0.###}%");
    }

    [Fact]
    public void 每帧顶点预算把瞬时分配也卡住了()
    {
        var plan = ProductionPlan();
        var streamer = new PlanetShellStreamer(BudgetVertsPerFrame);
        streamer.Sync(plan);

        int frames = 0;
        while (!streamer.IsSettled && frames < 1000)
        {
            streamer.Step(out _);
            frames++;
        }
        Assert.True(streamer.IsSettled, $"壳应当在有限帧内铺完，实际 {frames} 帧仍有 {streamer.PendingCount} 块待建");

        // 单帧最多写多少顶点 ⇒ 单帧瞬时上传缓冲的上界。
        // 每字节的换算用**量出来的**比值（整球那组 verts/tris 的真实比例），
        // 不用"三角形数≈顶点数×2"这类拍出来的系数：系数错了这条判据就会跟着错（上一版就错了 3 倍）。
        var perTile = Materialize(ProductionPlan());
        long allVerts = perTile.Sum(t => t.Verts + t.SkirtVerts);
        long allBytes = perTile.Sum(t => t.UploadBytes);
        double bytesPerVertex = (double)allBytes / allVerts;

        long peakVerts = streamer.PeakFrameVertices;
        long peakBytes = (long)Math.Ceiling(peakVerts * bytesPerVertex);
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "SHELL_FRAME frames={0} peak_frame_verts={1} bytes_per_vertex={2:0.##} peak_upload_bytes≈{3}",
            frames, peakVerts, bytesPerVertex, peakBytes));

        Assert.True(peakVerts <= BudgetVertsPerFrame,
            $"单帧顶点数不得越过预算：{peakVerts} > {BudgetVertsPerFrame}");
        Assert.True(peakBytes < 64 * 1024, $"单帧瞬时上传缓冲应当只有几十 KB，实际约 {peakBytes} 字节");
        // 实机回执是 11 帧铺满；离线同配置应当落在同一量级（差 1 帧来自收尾那帧是否空转）
        Assert.InRange(frames, 9, 14);
    }

    [Fact]
    public void 预算收紧时规划器先砍块数而不是超预算()
    {
        var planner = new PlanetShellPlanner(Radius, Quads, MaxLevel, AllowedPixels, 60.0, 1080.0);
        var dir = Geodetic.DirectionFromLatLon(30.0 * Math.PI / 180.0, 120.0 * Math.PI / 180.0);
        var plan = planner.Build(dir);

        foreach (var budget in new long[] { 1_000, 4_000, 8_973, 20_000 })
        {
            int fits = plan.CountWithinBudget(budget);
            Assert.InRange(fits, 0, plan.Tiles.Count);
        }
        // 反向对照：预算小于第一块就一块都放不下，而不是"放一块但超预算"
        Assert.Equal(0, plan.CountWithinBudget(1));
        Assert.Equal(plan.Tiles.Count, plan.CountWithinBudget(plan.TotalVertices));
    }
}
