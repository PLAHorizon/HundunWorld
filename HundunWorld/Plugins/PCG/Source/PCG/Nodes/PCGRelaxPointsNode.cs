// PCG 插件 - 松弛点（点运算）
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.10。

using System;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 用几次迭代把挤在一起的点推开，让散布看起来是铺开的而不是成团的。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 自剪枝过滤器决定**谁活下来**，本节点决定**活下来的落在哪**。这是两件
    /// 不同的事：从密采样的结果里剪枝，剩下的点仍在采样器偏好的位置上，而一坨
    /// 活下来的**还是一坨**——林缘或碎石场恰恰不该长这样。
    /// 松弛是**不必再跑一遍采样**的最省事的修法。
    /// </para>
    /// <para>
    /// 每次迭代都从上一轮读位置、写进第二个缓冲区，所以结果
    /// **不依赖**点的访问顺序——边扫边移的松弛会因为访问顺序不同而漂移，
    /// 而本插件的整套契约就是“同一张图建出同一个世界”。
    /// </para>
    /// <para>
    /// 位移有两重上限：强度（实际施加的推力占算出来的比例）与**每次迭代的步长**
    /// 上限。对紧密小团做无上限松弛，等于把道具传送出自己的格子、
    /// 脱离采样器为它们选好的地面。
    /// </para>
    /// </remarks>
    public sealed class PCGRelaxPointsNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "RelaxPoints";

        // 参数下标。顺序是已存图格式的一部分；只许追加。
        private const int PIterations = 0;
        private const int PRadius = 1;
        private const int PStrength = 2;
        private const int PMaxStep = 3;
        private const int PPlaneOnly = 4;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 5;

        /// <summary>迭代数上限，免得手填一个数就把图变成卡死。</summary>
        public const int MaxIterations = 32;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>跑多少次松弛。</summary>
        public int Iterations = 4;

        /// <summary>近于这个距离的邻居会互相推开。</summary>
        public float Radius = 100.0f;

        /// <summary>每次迭代施加的推力占算出来的多少。</summary>
        public float Strength = 0.5f;

        /// <summary>一次迭代里一个点最多能移动多远。</summary>
        public float MaxStep = 25.0f;

        /// <summary>打开时点保持高度——地面散布的常态。</summary>
        public bool PlaneOnly = true;

        /// <summary>用反序列化出来的参数建节点。</summary>
        public PCGRelaxPointsNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            Iterations = Math.Min(MaxIterations, Math.Max(0, _params.Get(PIterations, 4)));
            Radius = Math.Max(0.0f, _params.Get(PRadius, 100.0f));
            Strength = Math.Min(1.0f, Math.Max(0.0f, _params.Get(PStrength, 0.5f)));
            MaxStep = Math.Max(0.0f, _params.Get(PMaxStep, 25.0f));
            PlaneOnly = _params.Get(PPlaneOnly, true);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Relax Points";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true, tooltip: "Points to spread out."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "The same points, moved apart but kept in their original order."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"iterations={Iterations}, radius={Radius:F1}, strength={Strength:F2}, " +
            $"maxStep={MaxStep:F1}, planeOnly={PlaneOnly}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs[0];
            if (input == null || input.Count <= 1)
                return input ?? PCGData.Empty;

            if (Iterations == 0 || Strength <= 0.0f || Radius <= 0.0f || MaxStep <= 0.0f)
                return input.Clone();

            var result = input.Clone();
            var points = result.Points;
            var current = new Vector3[points.Length];
            var next = new Vector3[points.Length];

            for (int i = 0; i < points.Length; i++)
                current[i] = points[i].Position;

            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                context.ThrowIfCancelled(iteration);

                // 每一轮都重建网格，因为点已经不在原处了——索引一陈旧就会
                // 去推那些早就挪走的邻居。
                var index = new PCGPointIndex(BuildProxy(points, current), Radius, PlaneOnly);
                float squaredRadius = Radius * Radius;

                for (int i = 0; i < points.Length; i++)
                {
                    var origin = current[i];
                    float pushX = 0.0f, pushY = 0.0f, pushZ = 0.0f;
                    int neighbours = 0;

                    index.ForEachNearby(origin, other =>
                    {
                        if (other == i)
                            return;

                        float dx = origin.X - current[other].X;
                        float dy = origin.Y - current[other].Y;
                        float dz = origin.Z - current[other].Z;
                        float squared = dx * dx + dz * dz + (PlaneOnly ? 0.0f : dy * dy);

                        if (squared > squaredRadius || squared < 1e-8f)
                            return;

                        float distance = MathF.Sqrt(squared);
                        float weight = (Radius - distance) / Radius;
                        neighbours++;
                        pushX += dx / distance * weight;
                        pushZ += dz / distance * weight;

                        if (!PlaneOnly)
                            pushY += dy / distance * weight;
                    });

                    if (neighbours == 0)
                    {
                        next[i] = origin;
                        continue;
                    }

                    var move = new Vector3(
                        pushX / neighbours * Strength,
                        PlaneOnly ? 0.0f : pushY / neighbours * Strength,
                        pushZ / neighbours * Strength);

                    float length = move.Length;

                    if (length > MaxStep)
                        move *= MaxStep / length;

                    next[i] = origin + move;
                }

                Array.Copy(next, current, current.Length);
            }

            for (int i = 0; i < points.Length; i++)
            {
                var point = points[i];
                var half = PCGPointTransform.HalfExtents(point.Bounds);
                point.Position = current[i];
                point.Bounds = PCGPointTransform.MakeBox(point.Position, half);
                points[i] = point;
            }

            return result;
        }

        /// <summary>
        /// 一个承载当前位置的点数组，好让共用的网格能给它们做索引。
        /// </summary>
        /// <remarks>
        /// 每次迭代重建而不是原地改写：网格持着**它建索引时那个数组**的引用，
        /// 一边被查询一边往里写新位置，正是“松弛读到半更新的邻居”
        /// 的由来。
        /// </remarks>
        private static PCGPoint[] BuildProxy(PCGPoint[] points, Vector3[] positions)
        {
            var proxy = new PCGPoint[points.Length];

            for (int i = 0; i < points.Length; i++)
            {
                proxy[i] = points[i];
                proxy[i].Position = positions[i];
            }

            return proxy;
        }
    }
}
