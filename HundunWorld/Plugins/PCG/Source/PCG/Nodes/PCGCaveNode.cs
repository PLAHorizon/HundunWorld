// PCG 插件 - 洞穴（结构）
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.38。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 从每个入口往下挖一套带分叉的隧道：中心线点、每条隧道一条样条，以及可选的内向管道网格。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 洞穴在地形生成的链条里是"从地表往下"的那一半，所以它的入口就是上游给的点——地貌底图里筛出来的山地、
    /// 道路旁边、聚居地附近，都可以当入口。出口有三样，都是插件已有的词汇：**中心线点**（带隧道号、里程、深度、
    /// 半径四列，可以拿来摆支撑/矿脉/灯）、**每条隧道一条样条**（样条网格生成器可直接沿它铺）、以及**内向的管道
    /// 网格**（<see cref="PCGData.Geometry"/> 的独立几何，一条隧道一块、自带摆放，交给 `Spawn Geometry` 就是
    /// 看得见的洞壁）。
    /// </para>
    /// <para>
    /// "内向"是给洞穴用的关键细节：站在隧道里看到的是洞壁内侧，所以管道法线朝内、绕序同步翻转。法线与绕序一致
    /// 是这个几何核心一直守住的性质，翻转之后有向体积变成负值——那正是"翻转确实生效"的断言依据。
    /// </para>
    /// <para>
    /// 挖洞本身不模拟地质（插件手里没有体素岩体），它是**程序化巷道**：按段往下走、到深度上限转平、沿途分叉。
    /// 这条边界写在 <see cref="PCGCavePlanner"/> 的注释里，免得被当成地形学仿真。本节点会被离屏执行，因此只经
    /// <see cref="PCGNodeBase.LastError"/> 报告，不调用 <c>Debug.*</c>。
    /// </para>
    /// </remarks>
    public sealed class PCGCaveNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "Cave";

        // 参数下标。顺序是存档格式的一部分，只允许追加。
        private const int PSegmentLength = 0;
        private const int PSegmentJitter = 1;
        private const int PBranchChance = 2;
        private const int PMaxTurn = 3;
        private const int PMaxDepth = 4;
        private const int PSegments = 5;
        private const int PRadius = 6;
        private const int PRadiusJitter = 7;
        private const int PMaxTunnels = 8;
        private const int PTubeSides = 9;
        private const int PMakeMeshes = 10;
        private const int PWriteDepth = 11;

        /// <summary>区域盒子的两个角（只框水平面；Y 由节点自己撑到足够宽）。</summary>
        private const int PRegionMin = 12;
        private const int PRegionMax = 13;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 14;

        /// <summary>隧道号所在的列名。</summary>
        public const string TunnelColumn = "Tunnel";

        /// <summary>沿隧道里程所在的列名。</summary>
        public const string DistanceColumn = "TunnelDistance";

        /// <summary>相对入口的深度所在的列名。</summary>
        public const string DepthColumn = "TunnelDepth";

        /// <summary>洞道半径所在的列名。</summary>
        public const string RadiusColumn = "TunnelRadius";

        /// <summary>一次运行最多输出多少点，防止参数写错把预算吃光。</summary>
        public const int MaxPoints = 100000;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>挖洞的形状参数。</summary>
        public PCGCaveOptions Options = new PCGCaveOptions
        {
            SegmentLength = 120.0f,
            SegmentJitter = 0.4f,
            BranchChance = 0.15f,
            MaxTurnDegrees = 35.0f,
            MaxDepth = 600.0f,
            MaxSegments = 24,
            Radius = 60.0f,
            RadiusJitter = 0.3f,
            MaxTunnels = 12,
        };

        /// <summary>管道网格一圈多少边。</summary>
        public int TubeSides = 8;

        /// <summary>是否输出内向管道网格（洞壁）。</summary>
        public bool MakeMeshes = true;

        /// <summary>是否写深度列。</summary>
        public bool WriteDepth = true;

        /// <summary>上次挖出的隧道条数（所有入口合计）。</summary>
        public int LastTunnelCount { get; private set; }

        /// <summary>由序列化参数构造节点。</summary>
        public PCGCaveNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            Options.SegmentLength = Math.Max(1.0f, _params.Get(PSegmentLength, 120.0f));
            Options.SegmentJitter = Mathf.Saturate(Math.Abs(_params.Get(PSegmentJitter, 0.4f)));
            Options.BranchChance = Mathf.Saturate(_params.Get(PBranchChance, 0.15f));
            Options.MaxTurnDegrees = Math.Max(0.0f, _params.Get(PMaxTurn, 35.0f));
            Options.MaxDepth = Math.Max(0.0f, _params.Get(PMaxDepth, 600.0f));
            Options.MaxSegments = Math.Max(1, _params.Get(PSegments, 24));
            Options.Radius = Math.Max(0.5f, _params.Get(PRadius, 60.0f));
            Options.RadiusJitter = Mathf.Saturate(Math.Abs(_params.Get(PRadiusJitter, 0.3f)));
            Options.MaxTunnels = Math.Max(1, _params.Get(PMaxTunnels, 12));

            // 区域：两个角真框出一块地时才成立，否则这一档**完全不存在**（不改变任何既有行为）。
            // Y 撑到 ±100000 是因为洞穴的深度本来就以几百计——这一档要管的是"在哪个平面范围内挖"，
            // 竖直方向该由 `Max Depth` 与体积边界管，别在这里投第二票。
            var regionMin = _params.Get(PRegionMin, Vector3.Zero);
            var regionMax = _params.Get(PRegionMax, Vector3.Zero);

            Options.Region = regionMax.X > regionMin.X && regionMax.Z > regionMin.Z
                ? new BoundingBox(
                    new Vector3(regionMin.X, -100000.0f, regionMin.Z),
                    new Vector3(regionMax.X, 100000.0f, regionMax.Z))
                : (BoundingBox?)null;

            TubeSides = Math.Max(3, _params.Get(PTubeSides, 8));
            MakeMeshes = _params.Get(PMakeMeshes, true);
            WriteDepth = _params.Get(PWriteDepth, true);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Cave";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input,
                tooltip: "入口点。不接时在生成范围中心取一个入口。"),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "中心线点、每条隧道一条样条，以及可选的内向管道网格。"),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"segment {Options.SegmentLength:F0}±{Options.SegmentJitter:P0}, {Options.MaxSegments} segments, " +
            $"depth <= {Options.MaxDepth:F0}, turn <= {Options.MaxTurnDegrees:F0} deg, " +
            $"branch {Options.BranchChance:P0}, radius {Options.Radius:F0}±{Options.RadiusJitter:P0}, " +
            $"<= {Options.MaxTunnels} tunnel(s), tube {TubeSides} side(s)";

        /// <summary>
        /// 把入口压到**地表**上。
        /// </summary>
        /// <remarks>
        /// 生成范围的包围盒在竖直方向常常是跨过地面的（例如 ±500），拿它的几何中心的 Y 当入口，
        /// 会把整条隧道埋在地面以下——平地上两者都贴着 0，看不出差别；地形一旦有起伏，
        /// 表现就是"洞挖在山里、但找不到洞口"。上游给的点本来就落在地表时，这一步是恒等的，
        /// 所以对"接入采样器点集"的用法没有副作用。没有高度来源时原样返回（不猜）。
        /// </remarks>
        private static Vector3 OnGround(PCGExecutionContext context, Vector3 position)
        {
            var world = context.World;

            if (world == null || !world.HasHeightSource)
                return position;

            var height = world.HeightSource(position.X, position.Z);

            return height.HasValue ? new Vector3(position.X, height.Value, position.Z) : position;
        }

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            LastTunnelCount = 0;
            LastError = null;

            var input = inputs != null && inputs.Length > 0 ? inputs[0] : null;

            // 体积级的盒子：默认入口从它的中心起，巷道也不许走出去。取体积盒而不是"这次运行的范围"——
            // 分区运行时一次只覆盖一格（带余量）的范围，而 16 格各取自己的盒子就会挖出 16 张不同的网。
            // `SourceBounds` 是体积级的、随上下文一路传到每格，与网格采样器那条"世界锚点优先"是同一条规则。
            var world = context.SourceBounds ?? context.GenerationBounds;

            // 退化的盒子（离屏夹具常常不设范围）当"不限"：拿它当边界会把巷道钉死在一个点上。
            Options.Bounds = world.Size.Length > 0.0f ? world : (BoundingBox?)null;

            var entrances = new List<Vector3>(4);

            if (input != null && input.Count > 0)
            {
                for (int i = 0; i < input.Count; i++)
                    entrances.Add(OnGround(context, input.Points[i].Position));
            }
            else
            {
                // 没有入口就在体积自己的范围中心起一个（实测过教训：取"这次运行的范围中心"，分区后会长出
                // 16 张迷你洞网——同一个体积分区出 48 件、整块出 12 件，而它们本该是同一张网按格裁开）。
                entrances.Add(OnGround(context, world.Center));
            }

            var tunnels = new List<PCGTunnel>();
            var scratch = new List<PCGTunnel>();
            int entranceTunnels = 0;
            int totalPoints = 0;
            int totalMeshes = 0;

            for (int e = 0; e < entrances.Count; e++)
            {
                context.ThrowIfCancelled(e);

                // 每个入口各自一套网络；种子按入口下标错开，保证两个入口不会挖出一模一样的洞。
                PCGCavePlanner.Plan(entrances[e], new Vector3(0, -1, 0),
                    unchecked(context.Seed + e * 7919), Options, scratch);

                entranceTunnels = scratch.Count;

                for (int t = 0; t < scratch.Count; t++)
                {
                    totalPoints += scratch[t].Centerline.Length;
                    tunnels.Add(scratch[t]);
                    totalMeshes++;
                }

                LastTunnelCount += entranceTunnels;

                if (totalPoints >= MaxPoints)
                    break;
            }

            if (tunnels.Count == 0)
            {
                LastError = "Cave planned no tunnels.";
                return input ?? PCGData.Empty;
            }

            int points = Math.Min(MaxPoints, totalPoints);
            var result = PCGData.Create(points);
            var tunnelsColumn = PCGCellSplit.DeclareColumn(result, TunnelColumn);
            var distances = PCGCellSplit.DeclareColumn(result, DistanceColumn);
            var depths = WriteDepth ? PCGCellSplit.DeclareColumn(result, DepthColumn) : null;
            var radii = PCGCellSplit.DeclareColumn(result, RadiusColumn);
            var splines = new List<PCGSpline>(tunnels.Count);
            var geometry = MakeMeshes ? new List<PCGGeometryItem>(tunnels.Count) : null;
            int write = 0;

            for (int t = 0; t < tunnels.Count && write < points; t++)
            {
                var tunnel = tunnels[t];
                var line = tunnel.Centerline;
                float entranceY = line[0].Y;
                float distance = 0.0f;

                for (int i = 0; i < line.Length && write < points; i++)
                {
                    if (i > 0)
                        distance += (line[i] - line[i - 1]).Length;

                    // 点就落在中心线上：半径写进列，由下游决定洞壁、灯光还是矿脉。
                    result.Points[write] = PCGPoint.Create(line[i], 1.0f, unchecked(context.Seed + write));
                    result.Points[write].UpdateBounds(new Vector3(0.5f, 0.5f, 0.5f));

                    tunnelsColumn[write] = t;
                    distances[write] = distance;

                    if (depths != null)
                        depths[write] = entranceY - line[i].Y;

                    radii[write] = tunnel.Radius;
                    write++;
                }

                if (line.Length > 1)
                    splines.Add(new PCGSpline(line, false));

                if (geometry != null && line.Length > 1)
                {
                    // 内向管道：法线朝内、绕序同步翻转，于是站在隧道里看到的是洞壁。
                    // 承载走**独立几何**（网格 + 自己的摆放），不是 `Meshes`：后者按点下标配对，
                    // 而这里一条隧道只有一个网格、却有很多点（见 PlaceTube）。
                    geometry.Add(PlaceTube(PCGMeshBuilder.Tube(line, tunnel.Radius, TubeSides, true, true)));
                }
            }

            // 点自己也要指向这套属性存储：下游有节点通过 point.Attributes 读列，不接上就只看得见一份空的。
            for (int i = 0; i < result.Count; i++)
                result.Points[i].Attributes = result.SharedAttributes;

            if (write < result.Count)
            {
                var trimmed = PCGData.Create(write);

                for (int i = 0; i < write; i++)
                    trimmed.Points[i] = result.Points[i];

                trimmed.SharedAttributes = result.SharedAttributes;
                trimmed.Splines = splines.Count > 0 ? splines.ToArray() : null;
                trimmed.Geometry = geometry?.ToArray();
                return trimmed;
            }

            result.Splines = splines.Count > 0 ? splines.ToArray() : null;
            result.Geometry = geometry?.ToArray();
            return result;
        }

        /// <summary>
        /// 把 <see cref="PCGMeshBuilder.Tube"/> 出的**世界坐标**管子搬回它自己的局部原点，并把它原来在哪
        /// 当作摆放交回去。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 这一步不是可选的美化，是<b>承载契约</b>：<see cref="PCGGeometryItem"/> 明说"网格建在自己的局部原点
        /// 上、摆放在 <c>Placement</c> 里"，而 <c>Tube</c> 写的是 <c>path[ring] + offset</c>（世界坐标）。
        /// 不搬就得把摆放写成单位变换——那时"这根管子在哪"只剩顶点坐标里有，下游按摆放做的裁剪、归属、实例化
        /// 全都会把整根管子当成在原点（实例化尤其明显：N 个实例共用一个网格，不居中就全叠在原点）。
        /// </para>
        /// <para>
        /// 为什么不能记在 <c>Meshes</c> 上：那条通道的契约是"第 i 个网格配第 i 个点"（供"每点一块"的图用），
        /// 而一条隧道是**一个网格、很多点**。硬凑下标的结果实测过：12 根管子全被摆到"隧道 0 的前 12 个点"
        /// 上，而且每根又被平移了一次点的位置——洞壁整体飞到了体积外两倍远的地方。
        /// </para>
        /// </remarks>
        private static PCGGeometryItem PlaceTube(PCGMesh tube)
        {
            var centre = PCGMeshBuilder.Bounds(tube).Center;

            for (int i = 0; i < tube.VertexCount; i++)
                tube.Vertices[i] -= centre;

            return PCGGeometryItem.At(tube, centre);
        }
    }
}
