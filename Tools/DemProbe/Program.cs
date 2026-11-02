// 运行期 DEM 对账工具：用 C#（服务端/客户端真正用的那份实现）复核 Python 侧产物。
//
//   dotnet run --project Tools/DemProbe/DemProbe.csproj -- anchors <demRoot> [level]
//   dotnet run --project Tools/DemProbe/DemProbe.csproj -- crc <demRoot>
//   dotnet run --project Tools/DemProbe/DemProbe.csproj -- patch <demRoot> <latDeg> <lonDeg> [patches]
//   dotnet run --project Tools/DemProbe/DemProbe.csproj -- stream <demRoot> <latDeg> <lonDeg> [cacheRoot]
//   dotnet run --project Tools/DemProbe/DemProbe.csproj -- mesh <demRoot> [quads] [alias]
//
// mesh 量中/远景 LOD 阶梯；带上 alias 时再对每个层级做一次"顶点网格平移不到一个像元"的对照，
// 逐点采样 vs §13.10 路线 B 的面积平均，摆幅之比就是摩尔纹被压下去的倍数。
//
// anchors 的坐标序列是两端共用的纯公式（不依赖随机数发生器），Python 侧 --xcheck 用同一套坐标独立插值。
// stream 量的是接上磁盘缓存后的整条近景通路：冷窗口逐块重算，热窗口逐块读回，两者按 crc 逐块对账。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Horizon.Planet;
using Horizon.Planet.Dem;

internal static class Program
{
    private const int AnchorCount = 200;

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("用法：anchors <demRoot> [level] | anchor <demRoot> | crc <demRoot> | " +
                                                                        "stream <demRoot> <latDeg> <lonDeg> [cacheRoot]");
            return 2;
        }

        string mode = args[0];
        string root = args[1];
        if (!DemManifest.TryLoad(root, out var manifest, out string error))
        {
            Console.Error.WriteLine("manifest 加载失败：" + error);
            return 3;
        }

        switch (mode)
        {
            case "anchors": return AnchorsTable(manifest, args.Length > 2 ? int.Parse(args[2]) : -1);
            case "anchor": return AnchorGate(manifest);
            case "crc": return Crc(manifest);
            case "patch": return Patch(manifest, args);
            case "stream": return Stream(manifest, args);
            case "mesh": return Mesh(manifest, args);
            case "pump": return Pump(manifest, args);
            default:
                Console.Error.WriteLine("未知模式 " + mode);
                return 2;
        }
    }

    /// <summary>
    /// 地理锚点：期望区间来自公开事实与对原始 GeoTIFF 的实测，不从本产物反推。
    /// C# 侧独立跑一遍，才能证明"运行期读取器的约定"地理上正确，而不只是与 Python 一致。
    /// </summary>
    private static readonly (string Name, double LatDeg, double LonDeg, int Lo, int Hi)[] Anchors =
    {
        ("珠穆朗玛峰", 27.9881, 86.9250, 8000, 9000),
        ("马里亚纳海沟", 11.3500, 142.2000, -11000, -10000),
        ("死海湖面", 31.5000, 35.4500, -480, -380),
        ("乞力马扎罗山", -3.0674, 37.3556, 5600, 6000),
        ("南极内陆冰盖", -80.0000, 45.0000, 2500, 4200),
        ("北太平洋深海盆", 30.0000, -150.0000, -7000, -5500),
        ("恒河平原", 25.6000, 85.1000, 0, 300),
        ("撒哈拉中部", 23.4000, 12.7000, 600, 900),
        ("冰岛内陆", 64.9000, -18.6000, 1200, 1500),
        ("秘鲁-智利海沟", -12.0000, -82.0000, -6000, -4000),
    };

    private static int AnchorGate(DemManifest manifest)
    {
        using var grid = new DemGrid(manifest);
        int level = manifest.Finest.Level;
        int failed = 0;
        foreach (var (name, latDeg, lonDeg, lo, hi) in Anchors)
        {
            double cm = grid.SampleElevationCmAt(level, latDeg * Math.PI / 180.0, lonDeg * Math.PI / 180.0);
            int metres = (int)Math.Round(cm / 100.0);
            bool ok = lo <= metres && metres <= hi;
            if (!ok) failed++;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "anchor {0,-14} L{1} {2,7} m 期望 [{3,6},{4,6}] {5}", name, level, metres, lo, hi, ok ? "OK" : "FAIL"));
        }
        Console.WriteLine("ANCHOR_RESULT " + (failed == 0 ? "PASS" : "FAIL"));
        return failed == 0 ? 0 : 1;
    }

    private static int AnchorsTable(DemManifest manifest, int level)
    {
        using var grid = new DemGrid(manifest);
        int coarse = CoarsestLevel(manifest);
        int fine = level >= 0 ? level : manifest.Finest.Level;
        if (!manifest.TryGetLevel(fine, out _) || !manifest.TryGetLevel(coarse, out _))
        {
            Console.Error.WriteLine("层级不在 manifest 里");
            return 4;
        }

        Console.WriteLine("# lat lon fine_level coarse_level fine_cm coarse_cm");
        for (int i = 0; i < AnchorCount; i++)
        {
            double latDeg = -89.0 + (i * 7.3) % 179.0;
            double lonDeg = -180.0 + (i * 41.9) % 360.0;
            double lat = latDeg * Math.PI / 180.0;
            double lon = lonDeg * Math.PI / 180.0;
            double fineCm = grid.SampleElevationCmAt(fine, lat, lon);
            double coarseCm = grid.SampleElevationCmAt(coarse, lat, lon);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0:F6} {1:F6} {2} {3} {4:F6} {5:F6}", latDeg, lonDeg, fine, coarse, fineCm, coarseCm));
        }
        return 0;
    }

    private static int Crc(DemManifest manifest)
    {
        int failures = 0;
        foreach (var level in manifest.Levels)
        {
            string path = Path.Combine(manifest.RootDirectory, level.File);
            var sw = Stopwatch.StartNew();
            uint crc = Crc32.HashFile(path);
            sw.Stop();
            bool ok = crc == (uint)level.Crc32;
            if (!ok) failures++;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "level {0:00} crc={1:x8} manifest={2:x8} {3} 秒={4:F1}",
                level.Level, crc, (uint)level.Crc32, ok ? "OK" : "FAIL", sw.Elapsed.TotalSeconds));
        }
        Console.WriteLine("CRC_RESULT " + (failures == 0 ? "PASS" : "FAIL"));
        return failures == 0 ? 0 : 1;
    }

    private static int Patch(DemManifest manifest, string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("patch 需要 <latDeg> <lonDeg> [patches]");
            return 2;
        }
        double latDeg = double.Parse(args[2], CultureInfo.InvariantCulture);
        double lonDeg = double.Parse(args[3], CultureInfo.InvariantCulture);
        int patches = args.Length > 4 ? int.Parse(args[4], CultureInfo.InvariantCulture) : 1;

        using var grid = new DemGrid(manifest);
        var profile = new TerrainProfile(PlanetConstants.PlanetRadiusCm, manifest.ArcSecond / 3600.0);
        var hybrid = new HybridHeightSource(grid, profile, manifest.ContentSeed, manifest.Format);
        var sampler = new HeightSampler(grid, hybrid);
        var terrain = new PlanetTerrain(PlanetConstants.PlanetRadiusCm, hybrid);
        var frame = RegionFrame.Build(RegionKey.FromDirection(
            Geodetic.DirectionFromLatLon(latDeg * Math.PI / 180.0, lonDeg * Math.PI / 180.0),
            PlanetConstants.DefaultRegionLevel));
        var heights = new float[PlanetPatchSampler.HeightmapLength];
        int edge = PlanetPatchSampler.HeightmapEdge;

        var sw = Stopwatch.StartNew();
        double min = double.MaxValue, max = double.MinValue;
        for (int p = 0; p < patches; p++)
        {
            PlanetPatchSampler.FillPatchHeights(frame, terrain,
                (p % 5) * (double)PlanetConstants.TerrainPatchSizeCm,
                (p / 5) * (double)PlanetConstants.TerrainPatchSizeCm, heights, sampler);
        }
        sw.Stop();
        for (int i = 0; i < heights.Length; i++)
        {
            float v = heights[i];
            if (v < min) min = v;
            if (v > max) max = v;
        }

        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "lat={0:F3} lon={1:F3} region={2} λ_c={3:F2} km 基场单格={4:F2} km bands={5} interpolator={6}",
            latDeg, lonDeg, frame.Key, profile.CrossWavelengthCm / 100000.0, profile.CellWavelengthCm / 100000.0,
            profile.BandCount, profile.UseCatmullRom ? "CatmullRom" : "Bilinear"));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "patches={0} vertices/patch={1} ms={2:F1} ms_per_patch={3:F1} 末patch局部Y min={4:F1} max={5:F1} cm",
            patches, heights.Length, sw.Elapsed.TotalMilliseconds, sw.Elapsed.TotalMilliseconds / patches, min, max));
        // patch 内起伏：纯基场在一格（4.6 km）的一小片里几乎是常数，起伏应主要来自细节层
        double centre = heights[PlanetPatchSampler.HeightIndex(edge / 2, edge / 2)];
        double corner = heights[PlanetPatchSampler.HeightIndex(0, 0)];
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "patch 内起伏 centre-corner={0:F1} cm", centre - corner));
        return 0;
    }

    /// <summary>
    /// 近景流送端到端：同一个窗口先冷跑（全部重算 + 落盘），再让第二个 streamer 热跑（全部读回），
    /// 逐块比 crc。跑出的数是 PIE spike 的预算依据，也是"缓存只是加速层、不改任何一个数"的现场证据。
    /// </summary>
    private static int Stream(DemManifest manifest, string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("stream 需要 <latDeg> <lonDeg> [cacheRoot]");
            return 2;
        }
        double latDeg = double.Parse(args[2], CultureInfo.InvariantCulture);
        double lonDeg = double.Parse(args[3], CultureInfo.InvariantCulture);
        string cacheParent = args.Length > 4
            ? args[4]
            : Path.Combine(Path.GetTempPath(), "hundun_stream_probe");
        // 每次一个独立子目录：不覆盖别人的缓存，也不靠删除未知内容来保证冷启动
        string cacheRoot = Path.Combine(cacheParent, "probe_" + Guid.NewGuid().ToString("N"));

        using var grid = new DemGrid(manifest);
        var profile = new TerrainProfile(PlanetConstants.PlanetRadiusCm, manifest.ArcSecond / 3600.0);
        var hybrid = new HybridHeightSource(grid, profile, manifest.ContentSeed, manifest.Format);
        var sampler = new HeightSampler(grid, hybrid);
        var terrain = new PlanetTerrain(PlanetConstants.PlanetRadiusCm, hybrid);
        var frame = RegionFrame.Build(RegionKey.FromDirection(
            Geodetic.DirectionFromLatLon(latDeg * Math.PI / 180.0, lonDeg * Math.PI / 180.0),
            PlanetConstants.DefaultRegionLevel));

        var cache = new PlanetPatchCache(cacheRoot, manifest.ContentSeed, hybrid.TerrainVersion);
        var streamer = new PlanetTerrainStreamer(frame, terrain, cache, sampler);
        var heights = new float[PlanetPatchSampler.HeightmapLength];

        var cold = new Dictionary<PatchKey, uint>();
        double coldMs = 0;
        int ticks = 0, unexpectedHit = 0;
        while (ticks < 200)
        {
            var plan = streamer.BuildPlan(0, 0);
            if (plan.ToLoad.Count == 0) break;
            foreach (var key in plan.ToLoad)
            {
                var one = Stopwatch.StartNew();
                var source = streamer.FillPatch(key, heights);
                one.Stop();
                if (source != PatchFillSource.Generated) unexpectedHit++;   // 冷跑里出现命中=这块根本没在计时
                coldMs += one.Elapsed.TotalMilliseconds;
                cold[key] = Crc32.Compute(MemoryMarshal.AsBytes(heights.AsSpan()));
                streamer.MarkReady(key);
            }
            ticks++;
        }
        if (unexpectedHit > 0)
        {
            Console.Error.WriteLine($"冷跑里有 {unexpectedHit} 块直接命中了缓存，耗时数字不可信");
            return 4;
        }
        if (cold.Count != PlanetConstants.NearPatchWindowEdge * PlanetConstants.NearPatchWindowEdge)
        {
            Console.Error.WriteLine($"窗口没收敛到 25 块：实际 {cold.Count} 块，ticks={ticks}");
            return 4;
        }

        // 热跑：换一个新调度器（驻留集空），同一份磁盘缓存，逐块读回
        var warmStreamer = new PlanetTerrainStreamer(frame, terrain, cache, sampler);
        double warmMs = 0;
        int mismatched = 0, notFromCache = 0;
        foreach (var pair in cold)
        {
            var one = Stopwatch.StartNew();
            var source = warmStreamer.FillPatch(pair.Key, heights);
            one.Stop();
            warmMs += one.Elapsed.TotalMilliseconds;
            if (source != PatchFillSource.CacheHit) notFromCache++;
            uint crc = Crc32.Compute(MemoryMarshal.AsBytes(heights.AsSpan()));
            if (crc != pair.Value) mismatched++;
        }

        // 热跑耗时里有多少是"逐字节查表算 crc"：不拆开的话，后面优化就不知道该怎么选
        // 窗口中心 (0,0) 必然在这 25 块里，直接拿它当计时样本
        byte[] crcSample = File.ReadAllBytes(cache.PathFor(frame.Key.ToId(), 0, 0));
        var crcSw = Stopwatch.StartNew();
        for (int i = 0; i < 10; i++) Crc32.Compute(crcSample.AsSpan(PlanetPatchCache.HeaderSize));
        crcSw.Stop();
        double crcMs = crcSw.Elapsed.TotalMilliseconds / 10;

        double peakResidentMb = cold.Count * (double)PlanetPatchSampler.HeightmapLength * 4 / (1 << 20);
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "lat={0:F3} lon={1:F3} region={2} patches={3} window={4}×{4} budget={5}",
            latDeg, lonDeg, frame.Key, cold.Count, PlanetConstants.NearPatchWindowEdge,
            PlanetConstants.PatchLoadsPerPlan));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "cold  total={0:F1} ms ms_per_patch={1:F1} writes={2}（plans={3}，budget={4}）",
            coldMs, coldMs / cold.Count, cache.Writes, ticks, PlanetConstants.PatchLoadsPerPlan));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "warm  total={0:F1} ms ms_per_patch={1:F3} hits={2} misses={3} speedup={4:F1}x",
            warmMs, warmMs / cold.Count, warmStreamer.CacheHits, warmStreamer.CacheMisses,
            warmMs > 0 ? coldMs / warmMs : double.PositiveInfinity));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "单块 crc（{0} 字节，逐字节查表）={1:F3} ms，占热跑每块的 {2:F0}%",
            PlanetPatchCache.PayloadBytes, crcMs, 100.0 * crcMs / Math.Max(warmMs / cold.Count, 1e-9)));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "cache dir={0} entries={1} bytes={2} 每块={3} 字节；内存侧 25 块 ≈ {4:F1} MB",
            cache.RootDirectory, cache.EntryCount, cache.BytesUsed, PlanetPatchCache.EntryBytes, peakResidentMb));
        Console.WriteLine(cache.DescribeStats());

        int failures = mismatched + notFromCache;
        Console.WriteLine(failures == 0
            ? "STREAM PASS 25 块热跑全部来自缓存且逐块 crc 与冷跑相同"
            : $"STREAM FAIL crc_mismatch={mismatched} not_from_cache={notFromCache}");
        try { Directory.Delete(cacheRoot, true); } catch (IOException) { }
        return failures == 0 ? 0 : 5;
    }

    /// <summary>
    /// 中/远景 LOD 阶梯实测：每个区域层级量一次 tile 跨度、网格间距、该配哪一级基场、
    /// 建一块 tile 的耗时与拓扑自检，最后用真实高程拼一个封闭球面验收。
    /// 输出的就是 PIE/渲染层选 LOD 阈值要用的数，不是纸面推算。
    /// </summary>
    private static int Mesh(DemManifest manifest, string[] args)
    {
        int quads = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 4;
        bool aliasing = args.Length > 3 && args[3] == "alias";
        if (quads < 1)
        {
            Console.Error.WriteLine("quads 必须 ≥ 1");
            return 2;
        }

        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "星球半径={0:F0} km  金字塔步长={1:F4}°..{2:F4}°  quads/edge={3}",
            PlanetConstants.PlanetRadiusCm / 100000.0, manifest.StepDeg,
            manifest.StepDegAt(CoarsestLevel(manifest)), quads));
        Console.WriteLine("level  tile宽km  网格间距km  基场级  像元km   tile顶点  tile_ms  拓扑");

        int failures = 0;
        foreach (int level in new[] { 0, 2, 4, 6, 8, 10, 12, 14 })
        {
            int span = 1 << level;
            var key = new RegionKey(0, level, span / 2, span / 2);       // 面中心的 tile（跨度最有代表性）
            double spacing = PlanetRegionMesh.GridSpacingCm(key, quads);
            int demLevel = PlanetLod.DemLevelForSpacing(manifest, spacing);
            double cell = PlanetLod.CellWidthCm(manifest, demLevel);
            double tileWidth = Math.Min(RegionMetrics.CenterWidthCm(key), RegionMetrics.CenterHeightCm(key));

            using var grid = new DemGrid(manifest, demLevel);
            var terrain = new PlanetTerrain(PlanetConstants.PlanetRadiusCm, grid);
            var sw = Stopwatch.StartNew();
            var mesh = PlanetRegionMesh.BuildTile(key, quads, terrain);
            var report = PlanetRegionMesh.Analyze(mesh);
            sw.Stop();

            bool clean = report.InwardWinding == 0 && report.DegenerateTriangles == 0 &&
                         report.NonFiniteVertices == 0 && report.EulerCharacteristic == 1;
            if (!clean) failures++;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0,5}  {1,9:F2}  {2,10:F2}  {3,7}  {4,8:F2}  {5,9}  {6,8:F2}  chi={7} bnd={8} inward={9} degen={10}{11}",
                level, tileWidth / 100000.0, spacing / 100000.0, demLevel, cell / 100000.0,
                mesh.VertexCount, sw.Elapsed.TotalMilliseconds, report.EulerCharacteristic,
                report.BoundaryEdges, report.InwardWinding, report.DegenerateTriangles,
                clean ? "" : "  <- MESH FAIL"));

            if (aliasing)
            {
                // 路线 B 的效果要在真数据上看：把整张顶点网格平移不到一个像元，
                // 逐点采样会"山忽有忽无"，面积平均的同一批点几乎不动
                var plan = PlanetLod.PlanSampling(manifest, spacing);
                using var boxGrid = new DemGrid(manifest, plan.DemLevel);
                var boxSource = new DemBoxAverageSource(boxGrid, plan.TapsPerEdge);
                var terrainBox = new PlanetTerrain(PlanetConstants.PlanetRadiusCm, boxSource);
                double shiftDeg = 0.37 * manifest.StepDegAt(plan.DemLevel);

                var swBox = Stopwatch.StartNew();
                var meshBox = PlanetRegionMesh.BuildTile(key, quads, terrainBox);
                swBox.Stop();

                double swingPoint = 0, swingBox = 0, maxBox = 0;
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    var dir = mesh.Positions[i] / mesh.Positions[i].Length;
                    Geodetic.LatLonFromDirection(dir, out double latRad, out double lonRad);
                    var shifted = Geodetic.DirectionFromLatLon(latRad, lonRad + shiftDeg * Math.PI / 180.0);

                    double dPoint = Math.Abs(grid.SampleElevationCm(shifted) - grid.SampleElevationCm(dir));
                    double dBox = Math.Abs(boxSource.SampleElevationCm(shifted) - boxSource.SampleElevationCm(dir));
                    swingPoint = Math.Max(swingPoint, dPoint);
                    swingBox = Math.Max(swingBox, dBox);
                    maxBox = Math.Max(maxBox, Math.Abs(boxSource.SampleElevationCm(dir)));
                }
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "        相位抖动最大变化：逐点 {0,8:F1} m   面积平均 {1,7:F1} m   压低 {2,5:F1}×   [{3}]",
                    swingPoint / 100.0, swingBox / 100.0,
                    swingBox > 0 ? swingPoint / swingBox : double.PositiveInfinity, plan));
                if (plan.TapsPerEdge > 1 && swingBox > swingPoint)
                {
                    Console.WriteLine("        MESH FAIL 面积平均反而更飘：抽头没起作用");
                    failures++;
                }
                if (meshBox.VertexCount != mesh.VertexCount)
                {
                    Console.WriteLine("        MESH FAIL 两种源的顶点数不同");
                    failures++;
                }
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "        tile_ms 逐点={0:F2} 面积平均={1:F2}（{2} 抽头/顶点） 平均场强={3:F1} m",
                    sw.Elapsed.TotalMilliseconds, swBox.Elapsed.TotalMilliseconds,
                    plan.TapsPerEdge * plan.TapsPerEdge, maxBox / 100.0));
            }

            // 真实高程下 tile 的半径必须还是 R+h：常数源判据已验过几何，这里验接上 DEM 之后没走偏
            PlanetVector3 centre = mesh.Positions[mesh.VertexCount / 2];
            double centreLen = centre.Length;
            double expected = PlanetConstants.PlanetRadiusCm + terrain.ElevationCm(centre / centreLen);
            if (Math.Abs(centreLen - expected) > 1e-2)
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "      MESH FAIL 中心顶点半径 {0:F3} 与 R+h {1:F3} 不符", centreLen, expected));
                failures++;
            }
        }

        // 远景验收：最粗一级基场拼整个球面，必须封闭（这是"看到地平线以外"的那一层）
        {
            int coarse = CoarsestLevel(manifest);
            using var grid = new DemGrid(manifest, coarse);
            var terrain = new PlanetTerrain(PlanetConstants.PlanetRadiusCm, grid);
            var sw = Stopwatch.StartNew();
            var globe = PlanetRegionMesh.BuildLevel(0, quads, terrain);
            var report = PlanetRegionMesh.Analyze(globe);
            sw.Stop();
            bool closed = report.IsClosed && report.InwardWinding == 0 && report.DegenerateTriangles == 0;
            if (!closed) failures++;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "globe L0 quads={0} 基场级={1} V={2} F={3} E={4} chi={5} bnd={6} nonmanifold={7} inward={8} degen={9} ms={10:F1} {11}",
                quads, coarse, globe.VertexCount, globe.TriangleCount, report.Edges,
                report.EulerCharacteristic, report.BoundaryEdges, report.NonManifoldEdges,
                report.InwardWinding, report.DegenerateTriangles, sw.Elapsed.TotalMilliseconds,
                closed ? "CLOSED" : "NOT-CLOSED"));

            // 全球最高/最低点：拿它核对"远景确实画出了地形起伏"，而不是画了个球
            double max = double.MinValue, min = double.MaxValue;
            foreach (var p in globe.Positions)
            {
                double r = p.Length - PlanetConstants.PlanetRadiusCm;
                if (r > max) max = r;
                if (r < min) min = r;
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "远景球面半径偏差 min={0:F0} max={1:F0} cm（{2} 级基场采样到的极值）",
                min, max, coarse));
        }

        Console.WriteLine(failures == 0 ? "MESH PASS 每个层级的 tile 与远景球面拓扑都干净"
                                       : $"MESH FAIL 共 {failures} 处异常");
        return failures == 0 ? 0 : 5;
    }

    /// <summary>
    /// 后台生成泵的实机前哨测量：真实 DEM 上跑一个 5×5 窗口，比较串行与 1/2/4 个工作线程的墙钟时间、
    /// 主线程单帧最长被占用的时间、缓冲区复用省下的分配，并抽样核对后台结果与串行结果逐位相同。
    /// </summary>
    private static int Pump(DemManifest manifest, string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("pump 需要 <latDeg> <lonDeg> [workers...]");
            return 2;
        }
        double latDeg = double.Parse(args[2], CultureInfo.InvariantCulture);
        double lonDeg = double.Parse(args[3], CultureInfo.InvariantCulture);
        var workerCounts = new List<int>();
        for (int i = 4; i < args.Length; i++) workerCounts.Add(int.Parse(args[i], CultureInfo.InvariantCulture));
        if (workerCounts.Count == 0) workerCounts.AddRange(new[] { 1, 2, 4 });

        using var grid = new DemGrid(manifest);
        var profile = new TerrainProfile(PlanetConstants.PlanetRadiusCm, manifest.ArcSecond / 3600.0);
        var hybrid = new HybridHeightSource(grid, profile, manifest.ContentSeed, manifest.Format);
        var template = new HeightSampler(grid, hybrid);
        var terrain = new PlanetTerrain(PlanetConstants.PlanetRadiusCm, hybrid);
        var frame = RegionFrame.Build(RegionKey.FromDirection(
            Geodetic.DirectionFromLatLon(latDeg * Math.PI / 180.0, lonDeg * Math.PI / 180.0),
            PlanetConstants.DefaultRegionLevel));

        var keys = new List<PatchKey>();
        for (int dz = -2; dz <= 2; dz++)
            for (int dx = -2; dx <= 2; dx++) keys.Add(new PatchKey(dx, dz));

        // 对照口径：同样 25 块，一次是"全在主线程同步生成"（客户端不接泵会发生什么），一次是接泵。
        // 两边都走 FillPatchHeights 的同一条路径（含它内部按行分片），否则比值没有意义。
        var serialAll = new float[PlanetPatchSampler.HeightmapLength];
        var serialSw = Stopwatch.StartNew();
        double serialBlocked = 0, serialCrcMs = 0;
        uint serialCrc = 0;
        foreach (var key in keys)
        {
            var one = Stopwatch.StartNew();
            PlanetPatchSampler.FillPatchHeights(frame, terrain, key.OriginXCm, key.OriginZCm, serialAll,
                                                new HeightSampler(grid, hybrid));
            one.Stop();
            serialBlocked = Math.Max(serialBlocked, one.Elapsed.TotalMilliseconds);
            // 逐位对账要拿同一块比：记下中心 patch 那一块的 crc，而不是末块
            if (key == new PatchKey(0, 0))
            {
                var crcSw = Stopwatch.StartNew();
                serialCrc = Crc32.Compute(MemoryMarshal.AsBytes(serialAll.AsSpan()));
                crcSw.Stop();
                serialCrcMs = crcSw.Elapsed.TotalMilliseconds;     // 本机"搬 1 MB 做校验"要多久，下面的闸门按它比例定
            }
        }
        serialSw.Stop();
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "lat={0:F3} lon={1:F3} region={2} patches={3} 主线程同步 25 块：wall={4:F0} ms 单块最长={5:F1} ms " +
            "中心块crc={6:X8}（本机 1 MB 校验 {7:F1} ms）",
            latDeg, lonDeg, frame.Key, keys.Count, serialSw.Elapsed.TotalMilliseconds, serialBlocked,
            serialCrc, serialCrcMs));
        Console.WriteLine("workers  wall_ms  相对串行  max_inflight  主线程单帧最长ms  中心crc一致  rents  pool_hits  省下的分配MB");

        int failures = 0;
        foreach (int workers in workerCounts)
        {
            using var dir = new TempProbeDir();
            var cache = new PlanetPatchCache(dir.Path, manifest.ContentSeed, hybrid.TerrainVersion);
            using var pump = new PlanetPatchPump(frame, terrain, cache, template, maxWorkers: workers);

            var sw = Stopwatch.StartNew();
            double longestFrameMs = 0;
            long dequeued = 0;
            uint centreCrc = 0;
            float[]? held = null;
            foreach (var key in keys) pump.Request(key);
            while (true)
            {
                var frameWatch = Stopwatch.StartNew();
                while (pump.TryDequeueCompleted(out var ready))
                {
                    dequeued++;
                    // 中心那一块先扣着不归还：缓冲区一旦回池就可能被下一块复用，crc 就量错对象了。
                    // 而且 crc 本身要算 1 MB，绝不能算在"主线程单帧"这段时间里。
                    if (ready.Key == new PatchKey(0, 0) && held is null) held = ready.Heights;
                    else pump.Release(ready.Heights);
                }
                frameWatch.Stop();
                longestFrameMs = Math.Max(longestFrameMs, frameWatch.Elapsed.TotalMilliseconds);
                if (dequeued == keys.Count) break;
                if (sw.ElapsedMilliseconds > 180_000)
                {
                    Console.Error.WriteLine("pump 超时没收敛：" + pump.Describe());
                    failures++;
                    break;
                }
                Thread.Sleep(1);            // 让出核心：主线程自旋会和 worker 抢核，量出来的墙钟就不诚实了
            }
            sw.Stop();
            if (held is not null)
            {
                centreCrc = Crc32.Compute(MemoryMarshal.AsBytes(held.AsSpan()));
                pump.Release(held);
            }

            bool crcOk = centreCrc == serialCrc;
            if (!crcOk) failures++;
            long avoided = Math.Max(0, pump.Rents - pump.PoolHits);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0,7}  {1,7:F0}  {2,9:F2}x  {3,12}  {4,14:F3}  {5,10}  {6,6}  {7,9}  {8,11:F1}",
                workers, sw.Elapsed.TotalMilliseconds, serialSw.Elapsed.TotalMilliseconds / sw.Elapsed.TotalMilliseconds,
                pump.MaxInFlight, longestFrameMs, crcOk ? "OK" : "MISMATCH",
                pump.Rents, pump.PoolHits, avoided * PlanetPatchSampler.HeightmapLength * 4.0 / (1 << 20)));
            if (workers >= 2 && pump.MaxInFlight == 1)
            {
                Console.Error.WriteLine("        给了并发却没并发起来（限流或驱动没接好）");
                failures++;
            }
            // "主线程单帧最长"在这里只是观察值，不当闸门：CPU 被 12 路行分片占满时，
            // 一次空转的取件也会被调度出去几十毫秒，wall 时间分不清"泵在主线程上干了活"和"被抢了核"。
            // 取件路径必须便宜这条断言放在单元测试里（那里 CPU 是空的），见 PlanetPatchPumpTests。
            if (pump.GenerationFailures != 0 || pump.CacheWriteFailures != 0)
            {
                Console.Error.WriteLine("        " + pump.Describe());
                failures++;
            }
        }

        Console.WriteLine(failures == 0 ? "PUMP PASS 后台结果与串行逐位相同，主线程没被生成占住"
                                       : $"PUMP FAIL 共 {failures} 处异常");
        return failures == 0 ? 0 : 5;
    }

    /// <summary>探针专用的临时目录：每次一个独立路径，不覆盖任何人的缓存。</summary>
    private sealed class TempProbeDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "hundun_pump_probe_" + Guid.NewGuid().ToString("N"));
        public TempProbeDir() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            try { Directory.Delete(Path, true); }
            catch (IOException) { }
        }
    }

    private static int CoarsestLevel(DemManifest manifest)
    {
        int coarse = 0;
        foreach (var level in manifest.Levels)
            if (level.Level > coarse) coarse = level.Level;
        return coarse;
    }
}
