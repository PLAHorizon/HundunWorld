// PCG Plugin - Path (Structure)
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.35.

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 把输入的这些点用道路连起来：一条贴着地面、真能修出路的**最小代价**路线。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 插件此前已经能"摆放"，有了几何节点之后还能"建造"，但不会**连接**。山间道路、田间小道、两个村庄之间的通路，
    /// 都是一条尊重地面的路线；而两个标记之间的直线不是：它从崖上走下去、穿过湖面、专挑最陡的方向抵达。
    /// 这个节点就是"把标记变成路线"。
    /// </para>
    /// <para>
    /// 输入点按顺序作为途经点，所以上游通常是关卡里的标记（Actor Sampler）、聚居地语法算出的中心，或手工摆的一排点；
    /// 输出是 <see cref="PCGData.Splines"/> 上的一条曲线（已有的样条网格生成器就能沿它铺路），
    /// 外加沿路的点，带里程与所在段的坡度。入口与出口都是插件已有的词汇，所以"上一条路"不需要新写生成器。
    /// </para>
    /// <para>
    /// 规划本身在 <see cref="PCGPathPlanner"/>：注入高度场之上的纯算术，因此真正要紧的几条规则——超限坡地是
    /// **不可通行**而不是"更贵"、回避水域、搜索不离开直线太远、平滑绝不切角穿过路线特意绕开的崖体——
    /// 是对着手算出来的地面断言的，而不是对着一个场景。
    /// </para>
    /// </remarks>
    public sealed class PCGPathNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "Path";

        // 参数下标。顺序是存档格式的一部分，只允许追加。
        private const int PCellSize = 0;
        private const int PSlopeLimit = 1;
        private const int PSlopeCost = 2;
        private const int PWaterLevel = 3;
        private const int PMaxDetour = 4;
        private const int PSmoothing = 5;
        private const int PSampleSpacing = 6;
        private const int PWriteSlope = 7;

        /// <summary>区域矩形（世界单位，XZ）与判法：只准在框里走，或绕开这块地。</summary>
        private const int PRegionMin = 8;
        private const int PRegionMax = 9;
        private const int PRegionInvert = 10;

        /// <summary>区域下限；与上限写平（或写反）时这一档不存在。</summary>
        public Vector3 RegionMin;

        /// <summary><see cref="RegionMin"/> 的对角。</summary>
        public Vector3 RegionMax;

        /// <summary>true 表示这块矩形**不许经过**（路会绕开它）；false 表示只准在矩形里走。</summary>
        public bool RegionInvert;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 11;

        /// <summary>记录每个点位于路线多远处的列名。</summary>
        public const string DistanceColumn = "PathDistance";

        /// <summary>参数没给出可用值时的默认格子边长。</summary>
        public const float DefaultCellSize = 50.0f;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>规划网格的边长。</summary>
        public float CellSize = DefaultCellSize;

        /// <summary>道路允许的最大坡度（度）。</summary>
        public float SlopeLimit = 25.0f;

        /// <summary>每多一度坡度，相对走平地额外增加的代价。</summary>
        public float SlopeCost = 3.0f;

        /// <summary>低于此高度不可通行。默认极低，因此只有真去设置它时水才开始起作用。</summary>
        public float WaterLevel = -1000000.0f;

        /// <summary>路线相对两个途经点之间直线允许偏离的最大距离。</summary>
        public float MaxDetour = 500.0f;

        /// <summary>对原始路线做几轮切角平滑。</summary>
        public int Smoothing = 3;

        /// <summary>沿路线输出点的间距。</summary>
        public float SampleSpacing = 100.0f;

        /// <summary>开启时，每个点还会带上它所在那一段的坡度。</summary>
        public bool WriteSlope = true;

        /// <summary>上一次路线的总长（世界单位）。</summary>
        public float LastLength { get; private set; }

        /// <summary>上一次路线的最陡坡度（度）。</summary>
        public float LastMaxSlope { get; private set; }

        /// <summary>由序列化参数构造节点。</summary>
        public PCGPathNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            CellSize = Math.Max(0.01f, _params.Get(PCellSize, DefaultCellSize));
            SlopeLimit = _params.Get(PSlopeLimit, 25.0f);
            SlopeCost = Math.Max(0.0f, _params.Get(PSlopeCost, 3.0f));
            WaterLevel = _params.Get(PWaterLevel, -1000000.0f);
            MaxDetour = Math.Max(CellSize, _params.Get(PMaxDetour, 500.0f));
            Smoothing = Math.Max(0, _params.Get(PSmoothing, 3));
            SampleSpacing = Math.Max(1.0f, _params.Get(PSampleSpacing, 100.0f));
            WriteSlope = _params.Get(PWriteSlope, true);
            RegionMin = _params.Get(PRegionMin, Vector3.Zero);
            RegionMax = _params.Get(PRegionMax, Vector3.Zero);
            RegionInvert = _params.Get(PRegionInvert, false);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Path";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Waypoints, visited in order."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "Points along the route, plus the route itself as a spline."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"cell {CellSize:F0}, slope <= {SlopeLimit:F0} deg (cost {SlopeCost:F1}), detour {MaxDetour:F0}, " +
            $"smoothing {Smoothing}, spacing {SampleSpacing:F0}" +
            (WaterLevel > -100000.0f ? $", water at {WaterLevel:F0}" : string.Empty);

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            LastLength = 0.0f;
            LastMaxSlope = 0.0f;
            LastError = null;

            var input = inputs != null && inputs.Length > 0 ? inputs[0] : null;

            if (input == null || input.Count < 2)
            {
                LastError = "Path needs at least two points to connect.";
                return input ?? PCGData.Empty;
            }

            var world = context.World;

            if (world == null || !world.HasHeightSource)
            {
                // 本节点会被离屏执行，所以只经 LastError 报告（执行器会把它并进本轮告警），
                // 不调用 Debug.*：离线路径上那会直接以 DllNotFound 崩掉。
                LastError = "Path found no terrain in the execution context. The layer passes through unchanged.";
                return input;
            }

            var waypoints = new List<Vector2>(input.Count);

            for (int i = 0; i < input.Count; i++)
            {
                var position = input.Points[i].Position;
                waypoints.Add(new Vector2(position.X, position.Z));
            }

            var options = new PCGPathOptions
            {
                CellSize = CellSize,
                SlopeLimit = SlopeLimit,
                SlopeCost = SlopeCost,

                // 区域进可通行性：绕开或限定都由代价搜索自己处理，节点不搬落点。
                RegionMinX = RegionMin.X,
                RegionMinZ = RegionMin.Z,
                RegionMaxX = RegionMax.X,
                RegionMaxZ = RegionMax.Z,
                RegionInvert = RegionInvert,
                WaterLevel = WaterLevel,
                MaxDetour = MaxDetour,
                Smoothing = Smoothing,
                SampleSpacing = SampleSpacing,
            };

            var route = new List<PCGPathPoint>();

            if (!PCGPathPlanner.TryPlan(waypoints, world.HeightSource, options, route, out string planError))
            {
                LastError = $"Path could not route between the waypoints: {planError}.";
                return input;
            }

            var result = PCGData.Create(route.Count);
            var points = result.Points;
            var distances = PCGCellSplit.DeclareColumn(result, DistanceColumn);
            var slopes = WriteSlope ? PCGCellSplit.DeclareColumn(result, PCGAttributeSet.SlopeName) : null;

            float maxSlope = 0.0f;

            for (int i = 0; i < route.Count; i++)
            {
                var step = route[i];

                points[i] = PCGPoint.Create(step.Position, 1.0f, unchecked(context.Seed + i));

                // 朝向：偏航取前进方位、俯仰取纵坡。不写它的后果不是难看而是**下游每一片都水平摆**——
                // 这份纵坡 `Resample` 明明逐点算过，却只进了一个属性列，于是坡上的路面片之间露出台阶。
                // 中心差分而不是单侧：单侧会让每颗点的朝向比它所在的坡段错开半格。
                var ahead = route[i < route.Count - 1 ? i + 1 : i];
                var behind = route[i > 0 ? i - 1 : i];
                var delta = ahead.Position - behind.Position;
                float run = (float)Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z);

                if (run > 1e-4f || Math.Abs(delta.Y) > 1e-4f)
                {
                    // 57.29578f = 180/π，与路径规划器里算坡度用的是同一个换算。
                    float pitchDegrees = -(float)Math.Atan2(delta.Y, run) * 57.29578f;
                    float yawDegrees = (float)Math.Atan2(delta.X, delta.Z) * 57.29578f;

                    // 先偏航、再绕**局部右轴**俯仰。合成成 `Euler(pitch, yaw, 0)` 会把俯仰绕世界 X 轴施加，
                    // 于是坡向 +X 的路被拧出一个 Z 分量——判据量的是几何关系（垂直 + 水平投影平行），
                    // 它把这个顺序错误当场量了出来。
                    points[i].Rotation = Quaternion.Euler(0.0f, yawDegrees, 0.0f) *
                                         Quaternion.Euler(pitchDegrees, 0.0f, 0.0f);
                }
                points[i].UpdateBounds(new Vector3(0.5f, 0.5f, 0.5f));

                if (distances != null)
                    distances[i] = step.Distance;

                if (slopes != null)
                    slopes[i] = step.SlopeDegrees;

                maxSlope = Math.Max(maxSlope, step.SlopeDegrees);
            }

            // 路线作为一条曲线，追加在本层原有的曲线之后：保留输入的曲线，与 Create Spline 遵循的是同一条规则。
            var splinePoints = new Vector3[route.Count];

            for (int i = 0; i < route.Count; i++)
                splinePoints[i] = route[i].Position;

            var splines = new List<PCGSpline>(1) { new PCGSpline(splinePoints, false) };

            if (input.Splines != null)
                splines.InsertRange(0, input.Splines);

            result.Splines = splines.ToArray();

            LastLength = route[route.Count - 1].Distance;
            LastMaxSlope = maxSlope;

            // 路线概况放在节点字段里（LastLength / LastMaxSlope），由调用方按需读取；
            // 这里不写日志，理由同上：离线执行路径不允许碰 Debug.*。
            return result;
        }
    }
}
