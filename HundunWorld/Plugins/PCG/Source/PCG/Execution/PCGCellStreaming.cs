// PCG 插件 - 流式格子选择
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §7.3。

using System;
using System.Collections.Generic;
using FlaxEngine;

namespace PCG.Execution
{
    /// <summary>
    /// 把一个已规划区域的格子分成三类：一次流式请求要生成的、不碰的、
    /// 以及交回来的。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 分区已经让"每次格子"的生成有界；流式让的是"每次请求"有界，于是一片
    /// 4 平方公里的世界是以移动窗口生成的，而不是同时全生成。需要两个半径而不是一个，
    /// 是因为一个半径会产生抖动：在单条边界上，玩家来回一步就会
    /// 每一步都重建并销毁同一个格子。所以外半径说哪些格子要生成，而更大的
    /// 保持半径说一个格子可以漂多远才被清空——两者之间那一圈刻意不动，
    /// 站着且仍缓存在那里，随时能回到窗口里而不需要重算。
    /// </para>
    /// <para>
    /// 这里不追踪"上次哪些格子是活的"。规则纯粹是几何的——一个在保持半径之外的格子
    /// 会被驱逐，无论它当初有没有生成过——这让选择保持无状态且
    /// 幂等，并让体积自己的清理成为唯一需要对真实 actor 做对的事情。
    /// </para>
    /// <para>
    /// 垂直轴是刻意不进这个判定的：道具按水平距离流式，而一个
    /// 在玩家上方 300 单位的格子仍然是玩家会走进去的格子。
    /// </para>
    /// <para>
    /// 每请求的格子预算防的是一次传送变成一个帧尖峰：它给一次请求能生成
    /// 多少个格子设上限，近的优先。在这场竞争里输掉的格子会同时*不*进两份列表，
    /// 因为驱逐它会销毁它可能已经持有的道具——而被延后的格子存下的点仍留在
    /// 缓存里，所以下一次请求会接着做它，不需要重算任何已经做完的东西。
    /// </para>
    /// <para>
    /// <b>分块对齐的窗口。</b>按 chunk 流式的世界不想要一个圆盘：它想要
    /// 它自己认为是可见的那些整 chunk，于是生成的集合与世界系统加载的东西对齐，而
    /// 边界不会把一个 chunk 切成两半。传入正的 <c>chunkSize</c> 会让每个观察者的窗口变成
    /// 观察者自己的 chunk 朝四周各长出 <c>ceil(radius / chunkSize)</c> 个 chunk，
    /// 而选择就变成了矩形对矩形。有两个值得点名的后果：窗口只能变大（每个矩形
    /// 都覆盖半径本来会用的那个圆盘），而观察者在自己的 chunk <i>里面</i>走到任何位置
    /// 都产出完全相同的窗口——这正是让一个沿 chunk 接缝来回走玩家
    /// 不会每步都重生成整个世界的原因。
    /// </para>
    /// </remarks>
    public static class PCGCellStreaming
    {
        /// <summary>
        /// 把一个区域的格子排进活动窗口与驱逐集合。
        /// </summary>
        /// <param name="cells">来自 <see cref="PCGPartitionPlanner.Plan"/> 的格子。</param>
        /// <param name="center">窗口围绕切的那个点，世界单位；只读 X 与 Z。</param>
        /// <param name="radius">格子从这个距离开始被生成。</param>
        /// <param name="keepRadius">
        /// 格子越过这个距离就被清空。低于 <paramref name="radius"/> 时它被抬到半径，因为
        /// 驱逐一次请求正要生成的那个格子，会销毁它刚刚做出来的道具。
        /// </param>
        /// <param name="active">接收本次请求要跑的格子；跨调用可以是同一个列表。</param>
        /// <param name="evicted">接收要清空的格子；跨调用可以是同一个列表。</param>
        /// <param name="maxActiveCells">
        /// 一次请求允许*干活*的格子数，按离 <paramref name="center"/> 的远近排。零或负数
        /// 表示一次请求做完整个窗口。超出上限的格子被延后——既不跑也不驱逐——
        /// 而已经在保持半径之内的格子保有自己的道具和缓存的点，于是一次
        /// 后来的请求把它们做完而不需要重算。
        /// </param>
        /// <param name="needsWork">
        /// 窗口的格子里，一次请求真正必须计算的是哪些。对它答 false 的那些是免费的
        /// ——缓存已经持有它们——所以无论预算怎么写它们都保留，而只有其余的
        /// 才计入 <paramref name="maxActiveCells"/>。
        /// </param>
        /// <remarks>
        /// <para>
        /// <paramref name="needsWork"/> 防的是预算把外层窗口饿死。只按距离排序
        /// 会把同样最近的 K 个格子交给每一次请求，而它们之外的格子永远
        /// 轮不上——一次传送会填满玩家脚下的地面，让其余地平线永远空着，
        /// 因为下一次从同一处发出的请求又会把同样那 K 个赢回去。免费的格子不
        /// 消耗预算，所以第二次请求会把预算花在下一圈上，而缓存（调用方
        /// 本来就持有）是唯一决定什么算免费的东西。
        /// </para>
        /// </remarks>
        /// <returns>
        /// 预算在这次请求里留下了多少个格子。整个窗口装得下时为零，而那也正是
        /// 无预算的请求永远报的数；调用方要把它打进日志，因为否则"窗口有 40 格而这次
        /// 跑了 6 格"与"窗口只有 6 格"根本分不出来。
        /// </returns>
        public static int Select(
            IReadOnlyList<PCGPartitionCell> cells,
            in Vector3 center,
            float radius,
            float keepRadius,
            List<PCGPartitionCell> active,
            List<PCGPartitionCell> evicted,
            int maxActiveCells = 0,
            Predicate<PCGPartitionCell> needsWork = null,
            float chunkSize = 0.0f)
        {
            // 单个观察者是所有更早调用方用的那个形状，它仍然是那个顺手的入口；下面的
            // 并集情形才是原语。那个单元素数组是每次请求建一次，不是每个格子建一次。
            return SelectAround(
                cells, new[] { center }, radius, keepRadius, active, evicted, maxActiveCells, needsWork, chunkSize);
        }

        /// <summary>
        /// 围绕<b>多个</b>观察者做同一次选择，给世界里不止一个玩家的情形。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 一个格子只要在<i>任一</i>观察者的 <paramref name="radius"/> 内就生成，而只有当它在<i>每个</i>
        /// 观察者的 <paramref name="keepRadius"/> 之外时才交回来。正是这条不对称
        /// 构成了它与单观察者情形的全部区别：两个玩家相隔很远时两人都必须有地面，
        /// 而一个离开了某个玩家邻域的格子并没有离开世界——它仍站在
        /// 另一个玩家旁边。这两侧任何一侧写错，表现出来就是玩家踩在虚空里，或者
        /// 两个玩家的道具在他们擦肩而过时互相删掉。
        /// </para>
        /// <para>
        /// 完全没有观察者也是合法状态（这一片世界里没人），而它意味着一个空
        /// 窗口：一切被驱逐，而不是全部被生成。
        /// </para>
        /// </remarks>
        /// <returns>预算在这次请求里留下了多少个格子；见 <see cref="Select"/>。</returns>
        public static int SelectAround(
            IReadOnlyList<PCGPartitionCell> cells,
            IReadOnlyList<Vector3> viewers,
            float radius,
            float keepRadius,
            List<PCGPartitionCell> active,
            List<PCGPartitionCell> evicted,
            int maxActiveCells = 0,
            Predicate<PCGPartitionCell> needsWork = null,
            float chunkSize = 0.0f)
        {
            if (cells == null)
                throw new ArgumentNullException(nameof(cells));
            if (viewers == null)
                throw new ArgumentNullException(nameof(viewers));
            if (active == null)
                throw new ArgumentNullException(nameof(active));
            if (evicted == null)
                throw new ArgumentNullException(nameof(evicted));

            active.Clear();
            evicted.Clear();

            float runSquared = radius * radius;
            float keepSquared = Math.Max(radius, keepRadius);
            keepSquared *= keepSquared;

            bool chunked = chunkSize > 1e-3f;
            float[] runRects = chunked ? ViewerRects(viewers, radius, chunkSize) : null;
            float[] keepRects = chunked ? ViewerRects(viewers, Math.Max(radius, keepRadius), chunkSize) : null;

            for (int i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];

                if (chunked)
                {
                    if (IntersectsAny(runRects, in cell))
                    {
                        active.Add(cell);
                        continue;
                    }

                    if (!IntersectsAny(keepRects, in cell))
                        evicted.Add(cell);

                    continue;
                }

                float nearest = NearestViewerSquared(in cell, viewers);

                if (nearest <= runSquared)
                {
                    active.Add(cell);
                    continue;
                }

                if (nearest > keepSquared)
                    evicted.Add(cell);

                // 两个半径之间的一切——按最近的那个观察者算——保持站着而不
                // 被重跑，这正是设第二个半径的全部意义。
            }

            return ApplyBudget(active, viewers, maxActiveCells, needsWork);
        }

        /// <summary>
        /// 按一份显式的可见 chunk 坐标集合来选择——世界系统自己的答案，而不是
        /// 从玩家位置推出来的窗口。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 这里其余的都从观察者位置派生窗口；而一个按 chunk 流式的世界本来
        /// 就知道自己加载了哪些 chunk，再从位置反推只能和它不一致——图
        /// 会填满一个世界并没加载的 chunk，或把已加载的那个留一半。所以在这个模式下
        /// 这份清单<i>就是</i>窗口，而且没有保持半径：世界没列出的格子就被交回来。
        /// 留着它就意味着给世界已经判定不存在的地面生成内容。
        /// </para>
        /// <para>
        /// 滞回由调用方通过 <paramref name="border"/> 表达：把可见集合外围的一圈 chunk
        /// 也列进来，是第二个半径的 chunk 形状等价物；它归调用方选，
        /// 因为加载策略是调用方的职责。
        /// </para>
        /// <para>
        /// <paramref name="viewers"/> 只在预算生效时用来给格子排序，所以空列表是
        /// 合法的——没有观察者时每个格子都平手，排序退回到格子的下标，而这
        /// 与按距离那种同样确定。
        /// </para>
        /// </remarks>
        /// <returns>预算在这次请求里留下了多少个格子；见 <see cref="Select"/>。</returns>
        public static int SelectChunks(
            IReadOnlyList<PCGPartitionCell> cells,
            IReadOnlyList<Int2> chunks,
            float chunkSize,
            IReadOnlyList<Vector3> viewers,
            List<PCGPartitionCell> active,
            List<PCGPartitionCell> evicted,
            int maxActiveCells = 0,
            Predicate<PCGPartitionCell> needsWork = null,
            int border = 0)
        {
            if (cells == null)
                throw new ArgumentNullException(nameof(cells));
            if (chunks == null)
                throw new ArgumentNullException(nameof(chunks));
            if (viewers == null)
                throw new ArgumentNullException(nameof(viewers));
            if (active == null)
                throw new ArgumentNullException(nameof(active));
            if (evicted == null)
                throw new ArgumentNullException(nameof(evicted));

            if (!(chunkSize > 1e-3f))
                throw new ArgumentOutOfRangeException(nameof(chunkSize), chunkSize, "Chunk size must be positive.");

            active.Clear();
            evicted.Clear();

            var rects = ChunkRects(chunks, chunkSize, border);

            for (int i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];

                if (IntersectsAny(rects, in cell))
                    active.Add(cell);
                else
                    evicted.Add(cell);
            }

            return ApplyBudget(active, viewers, maxActiveCells, needsWork);
        }

        /// <summary>
        /// 清单里那些 chunk 的矩形，每个四个浮点数，各边按 <paramref name="border"/> 个 chunk 外扩。
        /// </summary>
        private static float[] ChunkRects(IReadOnlyList<Int2> chunks, float chunkSize, int border)
        {
            int grow = border > 0 ? border : 0;
            var rects = new float[chunks.Count * 4];

            for (int i = 0; i < chunks.Count; i++)
            {
                rects[i * 4 + 0] = (chunks[i].X - grow) * chunkSize;
                rects[i * 4 + 1] = (chunks[i].Y - grow) * chunkSize;
                rects[i * 4 + 2] = (chunks[i].X + grow + 1) * chunkSize;
                rects[i * 4 + 3] = (chunks[i].Y + grow + 1) * chunkSize;
            }

            return rects;
        }

        /// <summary>
        /// 把每请求的预算施加到已经选好的窗口上，返回它延后了多少个格子。
        /// </summary>
        /// <remarks>
        /// 刻意让所有选择模式共用：那条"缓存里的格子不花钱、因此不许计入预算"的规则
        /// 微妙到两份副本一定会各自漂移，而漂移会表现为
        /// 一圈永远填不满的外层窗口。
        /// </remarks>
        private static int ApplyBudget(
            List<PCGPartitionCell> active,
            IReadOnlyList<Vector3> viewers,
            int maxActiveCells,
            Predicate<PCGPartitionCell> needsWork)
        {
            if (maxActiveCells <= 0)
                return 0;

            if (needsWork == null)
            {
                if (active.Count <= maxActiveCells)
                    return 0;

                int window = active.Count;
                TrimToNearest(active, viewers, maxActiveCells);
                return window - maxActiveCells;
            }

            // 缓存已经持有的格子不花钱，所以无论预算怎么写它都保留，而只有
            // 需要干活的那些才被排序和裁切。没有这道拆分，预算每一次都会花在同样
            // 最近的 K 个格子上，而外层窗口永远填不满。
            var free = new List<PCGPartitionCell>();
            var paid = new List<PCGPartitionCell>();

            for (int i = 0; i < active.Count; i++)
            {
                if (needsWork(active[i]))
                    paid.Add(active[i]);
                else
                    free.Add(active[i]);
            }

            if (paid.Count <= maxActiveCells)
                return 0;

            int deferred = paid.Count - maxActiveCells;
            TrimToNearest(paid, viewers, maxActiveCells);

            // 要干活的排在前（按本次请求的格子顺序），免费的在后面：这个顺序只决定
            // 哪些格子先撞上那张图的合并点数上限，而先撞上的应该是玩家
            // 最近的那些，不是不花钱的那些。
            active.Clear();
            active.AddRange(paid);
            active.AddRange(free);

            return deferred;
        }

        /// <summary>
        /// 保留窗口里最近的 <paramref name="limit"/> 个格子，按近的在前排，其余丢掉。
        /// </summary>
        /// <remarks>
        /// 距离对每个格子只算一次，排序以那个值为键、以格子自己的下标做
        /// 打破平手，所以这场预算竞争的胜者在每台机器、每一次运行上都一样：对称的
        /// 网格会产出精确的距离平手（中心周围的四个格子同为零），而一个
        /// 停在"相等"就结束的比较，会让列表的主元顺序决定玩家先看到哪一格被填上。
        /// 把输掉的格子排除在 <see cref="Select"/> 的驱逐列表之外，正是"延后"整体安全的前提——
        /// 它们保有自己的道具和缓存，于是一次后来的请求把它们做完而不需要重算。
        /// </remarks>
        private static void TrimToNearest(List<PCGPartitionCell> active, IReadOnlyList<Vector3> viewers, int limit)
        {
            int count = active.Count;
            var rank = new int[count];
            var distances = new float[count];

            for (int i = 0; i < count; i++)
            {
                rank[i] = i;

                // 要紧的距离是到最近观察者的：世界里有两个玩家时，紧挨着任何一人的格子
                // 确实是"近的"，应当在预算竞争里赢过两人都不挨着的那个
                // 格子。
                // 这里不用 in：List&lt;T&gt; 的索引器是个属性，所以没有可按引用传的东西——
                // 实参被编译器为 readonly 参数造的那个隐式临时量拷贝了一份。
                distances[i] = NearestViewerSquared(active[i], viewers);
            }

            Array.Sort(rank, (a, b) =>
            {
                int byDistance = distances[a].CompareTo(distances[b]);
                if (byDistance != 0)
                    return byDistance;

                int byRow = active[a].IndexZ.CompareTo(active[b].IndexZ);
                return byRow != 0 ? byRow : active[a].IndexX.CompareTo(active[b].IndexX);
            });

            var nearest = new PCGPartitionCell[limit];
            for (int i = 0; i < limit; i++)
                nearest[i] = active[rank[i]];

            active.Clear();
            active.AddRange(nearest);
        }

        /// <summary>
        /// 每个观察者在 chunk 网格上的窗口矩形，各四个浮点数：minX、minZ、maxX、maxZ。
        /// </summary>
        /// <remarks>
        /// 这个矩形是观察者<b>自己的 chunk</b> 朝每边长出 <c>ceil(radius / chunkSize)</c> 个 chunk——
        /// 不是把圆盘逐角向外取整。区别要紧：这一形态只取决于
        /// 观察者站在哪个 chunk 里，所以玩家在一个 chunk <i>里面</i>走动时，
        /// 每帧产出的窗口完全相同、什么都不重生成。改用圆盘取整会让边界随着
        /// 观察者漂移而移动，而那正是整套滞回机制要防的抖动。
        /// 它仍然覆盖请求的半径：观察者离自己 chunk 的边最多一个 chunk，而
        /// <c>ceil(radius / chunkSize) * chunkSize >= radius</c>。
        /// </remarks>
        private static float[] ViewerRects(IReadOnlyList<Vector3> viewers, float radius, float chunkSize)
        {
            var rects = new float[viewers.Count * 4];
            int steps = (int)MathF.Ceiling(radius / chunkSize);

            for (int i = 0; i < viewers.Count; i++)
            {
                var viewer = viewers[i];
                int chunkX = (int)MathF.Floor(viewer.X / chunkSize);
                int chunkZ = (int)MathF.Floor(viewer.Z / chunkSize);

                rects[i * 4 + 0] = (chunkX - steps) * chunkSize;
                rects[i * 4 + 1] = (chunkZ - steps) * chunkSize;
                rects[i * 4 + 2] = (chunkX + steps + 1) * chunkSize;
                rects[i * 4 + 3] = (chunkZ + steps + 1) * chunkSize;
            }

            return rects;
        }

        /// <summary>
        /// 当格子的裁剪矩形与任一带入的窗口矩形<b>重叠</b>时为真。
        /// </summary>
        /// <remarks>
        /// 是重叠，不是相接。圆形那条路把仅仅擦到窗口的格子算进来，因为"到矩形的
        /// 距离为零"就是半径的含义——零半径的夹具正是为此刻意保留在中心相接的那四个
        /// 格子。而对齐到 chunk 的窗口问的是另一个问题——它覆盖哪些 chunk——所以只与它共一条边的
        /// chunk 并未被覆盖，于是让它保持原样，
        /// 而不是把整整多出来的一圈生成拉进来。
        /// </remarks>
        private static bool IntersectsAny(float[] rects, in PCGPartitionCell cell)
        {
            var min = cell.ClipBounds.Minimum;
            var max = cell.ClipBounds.Maximum;

            for (int i = 0; i < rects.Length; i += 4)
            {
                if (max.X <= rects[i + 0] || min.X >= rects[i + 2])
                    continue;

                if (max.Z <= rects[i + 1] || min.Z >= rects[i + 3])
                    continue;

                return true;
            }

            return false;
        }

        /// <summary>
        /// 到最近观察者的 XZ 距离平方；没有观察者时为无穷大。
        /// </summary>
        /// <remarks>
        /// 空的情形用无穷大而不是零："没有观察者靠近这个格子"必须排在最远并被驱逐，
        /// 而写零会给出完全相反的结果。
        /// </remarks>
        private static float NearestViewerSquared(in PCGPartitionCell cell, IReadOnlyList<Vector3> viewers)
        {
            if (viewers.Count == 0)
                return float.PositiveInfinity;

            float nearest = float.MaxValue;

            for (int i = 0; i < viewers.Count; i++)
            {
                float distance = DistanceToCellSquared(in cell, viewers[i]);

                if (distance < nearest)
                    nearest = distance;
            }

            return nearest;
        }

        /// <summary>
        /// 从一个点到格子裁剪矩形的 XZ 距离平方；点在矩形内时为零。
        /// </summary>
        private static float DistanceToCellSquared(in PCGPartitionCell cell, in Vector3 center)
        {
            var min = cell.ClipBounds.Minimum;
            var max = cell.ClipBounds.Maximum;

            // 钳位到最近点：经典的盒子对圆检验，出于上面所说的那个原因把 Y 折掉。
            float nearestX = center.X < min.X ? min.X : (center.X > max.X ? max.X : center.X);
            float nearestZ = center.Z < min.Z ? min.Z : (center.Z > max.Z ? max.Z : center.Z);

            float dx = center.X - nearestX;
            float dz = center.Z - nearestZ;

            return dx * dx + dz * dz;
        }
    }
}
