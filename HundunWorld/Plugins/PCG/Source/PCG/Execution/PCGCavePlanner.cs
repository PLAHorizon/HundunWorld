// PCG 插件 - 洞穴隧道网络：从入口往下钻、带分叉的确定性行走
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.38。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Nodes;

namespace PCG.Execution
{
    /// <summary>一条隧道：中心线、半径，以及它从哪条隧道分出来（-1 表示总入口那条）。</summary>
    public struct PCGTunnel
    {
        /// <summary>中心线，从入口往下游走。</summary>
        public Vector3[] Centerline;

        /// <summary>洞道半径。</summary>
        public float Radius;

        /// <summary>父隧道下标；-1 表示这是从入口直接下去的那条。</summary>
        public int Parent;

        /// <summary>分叉点在哪一段（父隧道中心线上的下标）。</summary>
        public int ParentSegment;
    }

    /// <summary>挖洞的形状参数。</summary>
    public struct PCGCaveOptions
    {
        /// <summary>一段的长度。</summary>
        public float SegmentLength;

        /// <summary>段长抖动比例。</summary>
        public float SegmentJitter;

        /// <summary>每一段之后分叉的概率。</summary>
        public float BranchChance;

        /// <summary>单次转向的最大角度（度）。</summary>
        public float MaxTurnDegrees;

        /// <summary>最深钻到入口以下多少。</summary>
        public float MaxDepth;

        /// <summary>一条隧道最多多少段。</summary>
        public int MaxSegments;

        /// <summary>洞道半径。</summary>
        public float Radius;

        /// <summary>半径抖动比例。</summary>
        public float RadiusJitter;

        /// <summary>整个洞穴最多几条隧道。</summary>
        public int MaxTunnels;

        /// <summary>
        /// 巷道不许走出去的盒子（体积自己的世界盒）；不填就不限。
        /// </summary>
        /// <remarks>
        /// 这是"洞穴挖在**这个体积里**"的那句话。少了它，一张深度 500、28 段的洞网能伸到 3 公里外——实测
        /// 同一个洞整块运行出 12 件、分区运行出 9 件（差的那 3 件在盒外），而整块那份还会把洞壁画到山体外面去。
        /// 约束靠**截短步长**实现，与深度上限同一套规矩（见 <see cref="PCGCavePlanner"/> 的类注释）。
        /// 取体积盒而不是格子盒：分区运行时每格各取自己的盒子，16 格就会挖出 16 张不同的网。
        /// </remarks>
        public BoundingBox? Bounds;

        /// <summary>
        /// 区域盒子：洞道**只准在这块里挖**。与 <see cref="Bounds"/> 同一种约束、同一种落地方式
        /// （走到边界前把步长截短，绝不事后搬落点、也绝不改方向），区别只是它可以比体积更小。
        /// 之所以不做"事后删点"：洞穴的点是**洞道的截面序列**，删一个点就等于在管壁上开一个洞，
        /// 河与洞道这类连续体被散点筛子切过就是瑞士奶酪。
        /// </summary>
        public BoundingBox? Region;
    }

    /// <summary>
    /// 从一个入口往下钻出一套带分叉的隧道。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这里**不模拟地质**：插件手里没有体素岩体，硬说"这是溶洞成因"是编故事。它做的是**程序化巷道网络**——像矿道
    /// 或人工洞窟那样，从入口按段往下走，走到深度上限为止，沿途以一定概率分叉。这件事的价值在于它是**可用的结构**：
    /// 洞道路径能喂样条生成器铺支撑、喂几何生成器出洞壁、喂点云摆矿脉与灯。
    /// </para>
    /// <para>
    /// 四条硬约束写进代码而不是交给运气：**绝不比入口更深超过 <see cref="PCGCaveOptions.MaxDepth"/>**、**单步不掉头**、
    /// **段长与半径始终落在参数范围内**，以及**巷道不走出 <see cref="PCGCaveOptions.Bounds"/>**。第一条与第四条都靠
    /// "截短步长"兜底（不是把落点单独搬到边界上——那会让行进方向与实际走出来的那一段分家，下一步就会在洞底/边上
    /// 掉头，管壁随之反法线）；第二条是洞管不自重叠的前提；第三条是"参数真的是参数"的前提。
    /// </para>
    /// <para>
    /// 方向是**算**出来的（水平面向 + 下潜俯角），不是一轮一轮叠旋转叠出来的：俯角在接近深度上限时按比例收平，
    /// 所以洞底是一条平巷，竖直段与水平段之间没有断崖。
    /// </para>
    /// <para>
    /// 全程确定性：每一步的随机数由（种子，隧道下标，段下标）唯一确定，所以同一个入口、同一个种子永远挖出同一个洞——
    /// 分区世界重跑同样的格子必须得到同样的结果。
    /// </para>
    /// </remarks>
    public static class PCGCavePlanner
    {
        /// <summary>最多允许的隧道条数上限，防止参数写错把预算吃光。</summary>
        public const int MaxTunnelsLimit = 64;

        /// <summary>一条隧道最多多少段。</summary>
        public const int MaxSegmentsLimit = 512;

        private const float Deg2Rad = (float)(Math.PI / 180.0);
        private const float Rad2Deg = (float)(180.0 / Math.PI);

        /// <summary>取一个方向的水平分量并归一化；方向竖直时把"退化"如实交给调用方。</summary>
        private static Vector3 HorizontalOf(Vector3 direction, out bool degenerate)
        {
            var flat = new Vector3(direction.X, 0.0f, direction.Z);
            degenerate = flat.Length <= 1e-4f;

            return degenerate ? Vector3.Forward : flat / flat.Length;
        }

        /// <summary>
        /// 从 <paramref name="entrance"/> 沿 <paramref name="direction"/> 挖一套隧道。
        /// </summary>
        /// <param name="entrance">入口位置。</param>
        /// <param name="direction">初始方向；零向量按正下方处理。</param>
        /// <param name="seed">随机种子。</param>
        /// <param name="options">形状参数。</param>
        /// <param name="into">接收隧道。</param>
        /// <returns>挖出的隧道数。</returns>
        public static int Plan(
            Vector3 entrance, Vector3 direction, int seed, in PCGCaveOptions options, List<PCGTunnel> into)
        {
            if (into == null)
                throw new ArgumentNullException(nameof(into));

            into.Clear();

            float segment = Math.Max(1.0f, options.SegmentLength);
            int maxSegments = Math.Max(1, Math.Min(MaxSegmentsLimit, options.MaxSegments));
            int maxTunnels = Math.Max(1, Math.Min(MaxTunnelsLimit, options.MaxTunnels));
            float depthLimit = Math.Max(0.0f, options.MaxDepth);
            float radiusBase = Math.Max(0.5f, options.Radius);

            // 区域盒子取一次就好（它是常量约束，不随隧道变）。
            var region = options.Region;

            var start = direction.Length > 1e-5f ? direction.Normalized : new Vector3(0, -1, 0);

            // 待挖的工作项：从哪出发、朝哪走、父隧道是谁、从父隧道的第几段分出来（-1 表示总入口那条）。
            var pending = new Queue<(Vector3 Position, Vector3 Direction, int Parent, int ParentSegment)>();
            pending.Enqueue((entrance, start, -1, -1));

            while (pending.Count > 0 && into.Count < maxTunnels)
            {
                var (position, initialDirection, parent, parentSegment) = pending.Dequeue();

                // 入口/分叉点本身就在区域外：这条洞**整条都不该存在**，连占位都不留。
                // 放在 `into.Add` 之前是有意的——晚一步就留下一条只有入口点的空隧道，
                // 而它在洞壁那一侧只是一份被下游回退掉的空网格，白占一个隧道名额。
                if (region.HasValue && !RegionContains(region.Value, position))
                    continue;

                int tunnelIndex = into.Count;

                // 先占位再走：分叉要在循环里引用本隧道的下标，而本隧道此时还没走完。
                into.Add(new PCGTunnel());

                var rng = new PCGDeterministicRng(seed, tunnelIndex, 0);
                float radius = radiusBase * (1.0f + options.RadiusJitter * rng.NextRange(-1.0f, 1.0f));

                var diveFrom = initialDirection.Length > 1e-5f ? initialDirection.Normalized : new Vector3(0, -1, 0);

                // 巷道用"面向 + 俯角"走，而不是把旋转一轮一轮地叠到方向上。
                // 面向是水平方向的随机行走（钉在起点面向的 ±90 度内，所以永远不掉头），俯角是下潜的角度，
                // 每步走一点、并按剩余深度收平。方向由这两者**算**出来：于是到洞底时它已经是缓的，
                // 不存在"最后一步被硬掰成直角"——那种不连续正是让洞管自重叠、管壁反法线的源头。
                float facing = 0.0f;
                float dip = Mathf.Clamp((float)(Math.Asin(Mathf.Clamp(-diveFrom.Y, -1.0f, 1.0f)) * Rad2Deg), 0.0f, 90.0f);

                var level = HorizontalOf(diveFrom, out bool degenerate);
                var facing0 = degenerate
                    ? new PCGDeterministicRng(seed, tunnelIndex, 7919).NextRange(0.0f, 360.0f)
                    : 0.0f;
                var basis = Vector3.Transform(level, Quaternion.RotationAxis(Vector3.Up, facing0 * Deg2Rad));

                var heading = diveFrom;
                var line = new List<Vector3>(maxSegments + 1) { position };

                for (int step = 0; step < maxSegments; step++)
                {
                    var stepRng = new PCGDeterministicRng(seed, tunnelIndex, step + 1);

                    facing = Mathf.Clamp(
                        facing + stepRng.NextRange(-options.MaxTurnDegrees, options.MaxTurnDegrees),
                        -90.0f, 90.0f);
                    dip = Mathf.Clamp(
                        dip + stepRng.NextRange(-options.MaxTurnDegrees, options.MaxTurnDegrees), 0.0f, 90.0f);

                    float length = segment * (1.0f + options.SegmentJitter * stepRng.NextRange(-1.0f, 1.0f));
                    float planned = length;

                    // 剩余还能往下多少。离上限还有好几段时俯角不受限；只剩不到四段时给一个连续变小的目标角，
                    // 于是"收平"是渐近的——洞底自然变成一条平巷，而不是在最后一刻被掰成直角。
                    float clearance = Math.Max(0.0f, position.Y - (entrance.Y - depthLimit));
                    float target = (float)(Math.Asin(
                        Mathf.Clamp(clearance / Math.Max(length * 4.0f, 1e-3f), 0.0f, 1.0f)) * Rad2Deg);

                    // 收平也要一步一步来：单步最多减一个转弯量，免得竖直段接水平段之间出现断崖。
                    dip = Math.Min(dip, Math.Max(target, dip - Math.Max(options.MaxTurnDegrees, 5.0f)));

                    var around = Vector3.Transform(basis, Quaternion.RotationAxis(Vector3.Up, facing * Deg2Rad));
                    var candidate = (around * Mathf.Cos(dip * Deg2Rad) - Vector3.Up * Mathf.Sin(dip * Deg2Rad)).Normalized;

                    // 掉头守卫：一步之内不许转过六十度（掉头意味着洞管叠回自己身上，管壁在折返处翻面）。
                    // 超了就折半逼近，而不是把方向冻住——冻住会让方向卡死在竖直上，下一步只能靠硬掰。
                    for (int guard = 0; guard < 4 && Vector3.Dot(candidate, heading) < 0.5f; guard++)
                        candidate = Vector3.Lerp(heading, candidate, 0.5f).Normalized;

                    // 深度上限只靠截短步长兜底，方向一个字都不改——方向一改，下一步就接着错。
                    if (candidate.Y < -1e-6f)
                        length = Math.Min(length, clearance / -candidate.Y);

                    // 体积边界是同一套规矩的第二条：到达盒子面向前还能走多远，就把步长截到那么长。
                    // 不搬落点、不改方向——搬了就与算出来的朝向分家，下一步必在边界上掉头，管壁随之反法线。
                    if (options.Bounds.HasValue)
                    {
                        float wall = MarchToBounds(position, candidate, options.Bounds.Value);

                        // 残桩不留：截完只剩一小截就整段作废。几厘米的段落算不出可信的切向，管道那一侧的
                        // 平行输运会拿到一个乱转的基准框，洞壁法线就在那一段翻面（实测：体积边界一开，
                        // 12 根管子里总有一根的最差三角形余弦变成 -0.88）。
                        if (wall < planned * 0.1f)
                            break;

                        length = Math.Min(length, wall);
                    }

                    // 区域盒子是同一条规矩的第三条：只准在这块里挖，出界前把步长截短——不搬落点、不改方向。
                    if (region.HasValue)
                    {
                        var box = region.Value;

                        // 人已经在区域外：这条洞到此为止。让 MarchToBounds 去处理"从盒子外面往里走"
                        // 会把洞挖到边界上停住，那是另一种错法——区域外的洞段本来就不该存在。
                        if (!RegionContains(box, position))
                            break;

                        float wall = MarchToBounds(position, candidate, box);

                        // 残桩的害处与体积那条一样：几厘米的段算不出可信切向，洞壁法线会在那一段翻面。
                        if (wall < planned * 0.1f)
                            break;

                        length = Math.Min(length, wall);
                    }

                    if (length <= 1e-3f)
                        break;   // 深度用尽或已顶到体积边界：巷道到此为止，不再往前。

                    var next = position + candidate * length;

                    line.Add(next);

                    // 分叉：从当前点换一个横摆方向另起一条，父指针指回本条、记下分叉段号。只有预算还有余量时才排。
                    if (step > 1 && stepRng.NextFloat() < options.BranchChance &&
                        into.Count + pending.Count < maxTunnels)
                    {
                        var branchRng = new PCGDeterministicRng(seed, tunnelIndex, 1000 + step);
                        float branchYaw = branchRng.NextRange(-90.0f, 90.0f);
                        var branchHeading = Vector3.Transform(
                            candidate, PCGPointTransform.EulerDegrees(new Vector3(0.0f, branchYaw, 0.0f))).Normalized;

                        pending.Enqueue((next, branchHeading, tunnelIndex, line.Count - 1));
                    }

                    heading = candidate;
                    position = next;
                }

                into[tunnelIndex] = new PCGTunnel
                {
                    Centerline = line.ToArray(),
                    Radius = radius,
                    Parent = parent,
                    ParentSegment = parentSegment,
                };
            }

            return into.Count;
        }

        /// <summary>
        /// 沿 <paramref name="direction"/> 从 <paramref name="from"/> 出发，还能走多远才碰到盒子的任一面。
        /// </summary>
        /// <remarks>
        /// 只回答"还有多远"，绝不回答"该往哪走"——调用方拿它**截短步长**。方向不动是这条约束的全部要点：
        /// 改了方向，实际走出来的那一段就与算出来的朝向分家，下一步必在边界上掉头，而掉头正是洞管自重叠、
        /// 管壁反法线的源头。起点已经在盒子外面时返回 0（一步都不许走）。
        /// </remarks>
        /// <summary>
        /// 这个水平位置落在区域框里吗。只判 X/Z：竖直方向由深度上限与体积边界管，这里不投第二票。
        /// </summary>
        private static bool RegionContains(BoundingBox region, Vector3 position) =>
            position.X >= region.Minimum.X && position.X <= region.Maximum.X &&
            position.Z >= region.Minimum.Z && position.Z <= region.Maximum.Z;

        private static float MarchToBounds(Vector3 from, Vector3 direction, BoundingBox bounds)
        {
            float limit = float.MaxValue;

            if (!AxisMarch(from.X, direction.X, bounds.Minimum.X, bounds.Maximum.X, ref limit) ||
                !AxisMarch(from.Y, direction.Y, bounds.Minimum.Y, bounds.Maximum.Y, ref limit) ||
                !AxisMarch(from.Z, direction.Z, bounds.Minimum.Z, bounds.Maximum.Z, ref limit))
                return 0.0f;

            return limit;
        }

        /// <summary>单轴：从这个坐标沿这个分量走到该轴的两个面要多久；已经在轴外则返回 false。</summary>
        private static bool AxisMarch(float from, float delta, float minimum, float maximum, ref float limit)
        {
            if (from < minimum || from > maximum)
                return false;

            if (Math.Abs(delta) < 1e-9f)
                return true;   // 这一轴不动：永远碰不到它的面，交给别的轴拦。

            float distance = delta > 0.0f ? (maximum - from) / delta : (minimum - from) / delta;

            if (distance < limit)
                limit = distance;

            return true;
        }
    }
}
