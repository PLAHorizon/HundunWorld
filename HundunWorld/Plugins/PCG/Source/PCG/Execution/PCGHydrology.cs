// PCG 插件 - 水文：填洼、流向、汇流累积，以及由此得到的河网与湖泊
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.36。

using System;
using System.Collections.Generic;
using FlaxEngine;

namespace PCG.Execution
{
    /// <summary>一处湖泊：由填洼算出的水面高与它覆盖的格子集合。</summary>
    public struct PCGLake
    {
        /// <summary>水面高度（填洼后的高度）。</summary>
        public float WaterLevel;

        /// <summary>湖面覆盖的格子数。</summary>
        public int CellCount;

        /// <summary>湖面中心（世界坐标，Y 即水面高度）。</summary>
        public Vector3 Center;

        /// <summary>沿流向最终出湖的那个格子（世界坐标）；湖是封闭的则为中心。</summary>
        public Vector3 Outlet;
    }

    /// <summary>一条河：从源头到出口的格子中心线。</summary>
    public struct PCGRiver
    {
        /// <summary>河心线，从上游到下游。</summary>
        public Vector3[] Centerline;

        /// <summary>沿途最大的汇流累积（格数）。</summary>
        public int PeakFlow;
    }

    /// <summary>
    /// 地形水文：把高度场变成"水会在哪里"。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 湖泊与江河不是可以随手撒点撒出来的东西——它们是地形本身的结果：水往低处走，走不掉的地方就积成湖，
    /// 汇到一起的地方就成了河。所以这里算的是经典的三步：**填洼**（把封闭的坑抬到能溢出的高度，抬起来的部分
    /// 就是湖面）、**流向**（每个格子朝最陡的下坡邻居）、**汇流累积**（把上游面积累加起来，累积大的地方是河）。
    /// </para>
    /// <para>
    /// 高度场是注入的，所以整套规则能对着手算的地形断言：一条 V 形山谷必须把河引到谷底线上，一个凹坑必须变成
    /// 恰好一处湖且水面高等于坑沿高。这比对着一张地形图目视判断可靠得多。
    /// </para>
    /// <para>
    /// 全部是确定性的：填洼用优先队列但并列时按下标决胜，流向并列时按固定方向顺序决胜，所以同一片地形跑两次
    /// 得到同一条河——分区世界需要这个性质。
    /// </para>
    /// </remarks>
    public static class PCGHydrology
    {
        /// <summary>八向邻居的固定顺序（先四正后四斜），并列时按它决胜。</summary>
        private static readonly int[] OffsetX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] OffsetZ = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>水面与地面相差小于这个值就不算湖（避免把浮点噪声当水）。</summary>
        public const float DepthEpsilon = 0.05f;

        /// <summary>
        /// 把高度场填成"处处都能流出去"的坡面。
        /// </summary>
        /// <remarks>
        /// 优先队列从边界向内推进（priority-flood）：队列里总是当前最低的格子，它被抬到不低于把它推进来的那个
        /// 格子的高度，于是每个封闭洼地都被抬到它最低的溢出点——抬起来的那部分正是湖面。
        /// </remarks>
        /// <param name="heights">原始高度，按行优先（z * 宽 + x）。</param>
        /// <param name="known">该格是否有高度；未知格不参与。</param>
        /// <param name="width">网格宽。</param>
        /// <param name="height">网格高。</param>
        /// <returns>填洼后的高度，未知处为 <see cref="float.MaxValue"/>。</returns>
        public static float[] Fill(float[] heights, bool[] known, int width, int height)
        {
            var filled = new float[heights.Length];
            var visited = new bool[heights.Length];
            var queue = new MinHeap(heights.Length);

            for (int i = 0; i < filled.Length; i++)
                filled[i] = float.MaxValue;

            // 边界上的已知格是水的出口：它们自身不被抬高。
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool edge = x == 0 || z == 0 || x == width - 1 || z == height - 1;
                    int index = z * width + x;

                    if (!edge || !known[index])
                        continue;

                    filled[index] = heights[index];
                    visited[index] = true;
                    queue.Push(heights[index], index);
                }
            }

            // 整片都未知时没有出口，直接返回未知。
            if (queue.Count == 0)
                return filled;

            while (queue.Count > 0)
            {
                int index = queue.Pop(out float level);
                int x = index % width;
                int z = index / width;

                for (int d = 0; d < OffsetX.Length; d++)
                {
                    int nx = x + OffsetX[d];
                    int nz = z + OffsetZ[d];

                    if (nx < 0 || nz < 0 || nx >= width || nz >= height)
                        continue;

                    int neighbour = nz * width + nx;

                    if (visited[neighbour] || !known[neighbour])
                        continue;

                    visited[neighbour] = true;

                    // 抬到不低于"推进来的那个格子"：这就是填洼，抬起的量就是水深。
                    filled[neighbour] = Math.Max(heights[neighbour], level);
                    queue.Push(filled[neighbour], neighbour);
                }
            }

            return filled;
        }

        /// <summary>
        /// 每个格子的流向：填洼后最陡的下坡邻居，没有下坡则是自己的汇（-1）。
        /// </summary>
        public static int[] FlowDirections(float[] filled, bool[] known, int width, int height)
        {
            var flow = new int[filled.Length];

            for (int i = 0; i < flow.Length; i++)
                flow[i] = -1;

            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = z * width + x;

                    if (!known[index] || filled[index] == float.MaxValue)
                        continue;

                    int best = -1;
                    float bestDrop = 0.0f;

                    for (int d = 0; d < OffsetX.Length; d++)
                    {
                        int nx = x + OffsetX[d];
                        int nz = z + OffsetZ[d];

                        if (nx < 0 || nz < 0 || nx >= width || nz >= height)
                            continue;

                        int neighbour = nz * width + nx;

                        if (!known[neighbour] || filled[neighbour] == float.MaxValue)
                            continue;

                        float run = (OffsetX[d] != 0 && OffsetZ[d] != 0) ? 1.41421356f : 1.0f;
                        float drop = (filled[index] - filled[neighbour]) / run;

                        // 严格大于：并列时先到者胜，而方向顺序是固定的，所以结果可复现。
                        if (drop > bestDrop)
                        {
                            bestDrop = drop;
                            best = neighbour;
                        }
                    }

                    flow[index] = best;
                }
            }

            return flow;
        }

        /// <summary>
        /// 汇流累积：每个格子承接了多少个上游格子（含自己）。
        /// </summary>
        /// <remarks>
        /// 按"高度从高到低"处理（填洼后必定无环，除汇点外每个格子都有下坡），把当前格子的量加到它流向的格子上——
        /// 一次排序加一遍扫描，比逐个格子递归上游快得多，也不需要栈。
        /// </remarks>
        public static int[] Accumulate(float[] filled, int[] flow, bool[] known, int width, int height)
        {
            var order = new List<int>(filled.Length);

            for (int i = 0; i < filled.Length; i++)
            {
                if (known[i] && filled[i] != float.MaxValue)
                    order.Add(i);
            }

            order.Sort((a, b) =>
            {
                int byHeight = filled[b].CompareTo(filled[a]);

                // 等高时按下标决胜，保证顺序（以及由此得到的累积量）可复现。
                return byHeight != 0 ? byHeight : a.CompareTo(b);
            });

            var accumulation = new int[filled.Length];

            for (int i = 0; i < accumulation.Length; i++)
                accumulation[i] = known[i] && filled[i] != float.MaxValue ? 1 : 0;

            for (int i = 0; i < order.Count; i++)
            {
                int index = order[i];
                int downhill = flow[index];

                if (downhill >= 0)
                    accumulation[downhill] += accumulation[index];
            }

            return accumulation;
        }

        /// <summary>
        /// 找出所有湖：填洼抬高了、且抬得超过 <paramref name="minDepth"/> 的连通区域。
        /// </summary>
        /// <param name="heights">原始高度。</param>
        /// <param name="filled">填洼后的高度。</param>
        /// <param name="known">该格是否有高度。</param>
        /// <param name="flow">流向，用于给湖找一个出水口。</param>
        /// <param name="perCell">格子边长，用于算中心与面积。</param>
        /// <param name="minCells">小于这么多格子的洼地不算湖（多半只是浮点噪声）。</param>
        /// <param name="minDepth">水浅于这个值不算湖。</param>
        /// <param name="into">接收结果。</param>
        /// <returns>找到的湖数。</returns>
        public static int FindLakes(
            float[] heights, float[] filled, bool[] known, int[] flow, int width, float perCell, int minCells,
            float minDepth, List<PCGLake> into, Predicate<int> allowOrigin = null)
        {
            if (into == null)
                throw new ArgumentNullException(nameof(into));

            into.Clear();

            if (width <= 0 || heights.Length % width != 0)
                return 0;

            int height = heights.Length / width;
            var seen = new bool[heights.Length];
            var stack = new Stack<int>();

            for (int start = 0; start < heights.Length; start++)
            {
                if (seen[start] || !known[start] || filled[start] == float.MaxValue)
                    continue;

                if (filled[start] - heights[start] <= minDepth)
                    continue;

                // 洪泛连通：同一次填洼抬起来、彼此连通的水面属于同一处湖。
                seen[start] = true;
                stack.Clear();
                stack.Push(start);

                int cells = 0;
                double sumX = 0.0;
                double sumZ = 0.0;
                float level = filled[start];
                int outlet = -1;

                while (stack.Count > 0)
                {
                    int index = stack.Pop();
                    int x = index % width;
                    int z = index / width;

                    cells++;
                    sumX += x;
                    sumZ += z;
                    level = Math.Max(level, filled[index]);

                    if (flow[index] < 0 || !known[flow[index]] ||
                        filled[flow[index]] - heights[flow[index]] <= minDepth)
                    {
                        // 这一格的下坡已经出了水面，它就是出水口。
                        outlet = index;
                    }

                    for (int d = 0; d < OffsetX.Length; d++)
                    {
                        int nx = x + OffsetX[d];
                        int nz = z + OffsetZ[d];

                        if (nx < 0 || nz < 0 || nx >= width || nz >= height)
                            continue;

                        int neighbour = nz * width + nx;

                        if (seen[neighbour] || !known[neighbour] || filled[neighbour] == float.MaxValue)
                            continue;

                        if (filled[neighbour] - heights[neighbour] <= minDepth)
                            continue;

                        seen[neighbour] = true;
                        stack.Push(neighbour);
                    }
                }

                if (cells < minCells)
                    continue;

                var center = new Vector3((float)(sumX / cells), 0.0f, (float)(sumZ / cells));
                var outletCell = outlet >= 0 ? outlet : start;

                // 区域按**湖心**判，不按洪泛的起始格判：起始格取决于扫描顺序，
                // 一片跨在边界上的湖会因为"先扫到哪个格子"而时有时无——那是比越界更难查的错。
                if (allowOrigin != null)
                {
                    int centerCell = (int)Math.Round(center.Z) * width + (int)Math.Round(center.X);

                    if (!allowOrigin(centerCell))
                        continue;
                }

                into.Add(new PCGLake
                {
                    WaterLevel = level,
                    CellCount = cells,
                    Center = center,
                    Outlet = new Vector3(outletCell % width, 0.0f, outletCell / width),
                });
            }

            return into.Count;
        }

        /// <summary>
        /// 从汇流累积大的格子往下游追，得到一条河的心线。
        /// </summary>
        /// <param name="accumulation">汇流累积。</param>
        /// <param name="flow">流向。</param>
        /// <param name="start">源头格子。</param>
        /// <param name="known">该格是否有高度。</param>
        /// <returns>心线（格子下标，从上游到下游）。</returns>
        public static List<int> TraceRiver(int[] accumulation, int[] flow, int start, bool[] known)
        {
            var line = new List<int>(32);
            var guard = accumulation.Length + 1;
            int at = start;

            while (at >= 0 && guard-- > 0)
            {
                line.Add(at);

                int next = flow[at];

                if (next < 0 || !known[next] || accumulation[next] <= accumulation[at])
                    break;

                at = next;
            }

            return line;
        }

        /// <summary>
        /// 找出所有"累积量达到阈值的格子所在的河"。
        /// </summary>
        /// <remarks>
        /// 判据是**源头**：一个格子的上游累积量低于阈值，而它自己达到阈值，它就是一个源头；从每个源头沿流向追到
        /// 下游出口便是一条河。按格子而不是按"水线"去分组，是因为一条河的心线在分叉处才需要分组，而分叉在
        /// D8 地形里本来就是各自成源的——这样既简单又与地形一致。
        /// </remarks>
        /// <returns>找到的河数。</returns>
        public static int FindRivers(
            int[] accumulation, int[] flow, bool[] known, int width, int height, int threshold, int maxRivers,
            List<PCGRiver> into, Predicate<int> allowOrigin = null)
        {
            if (into == null)
                throw new ArgumentNullException(nameof(into));

            into.Clear();

            if (threshold <= 0)
                return 0;

            for (int index = 0; index < accumulation.Length && into.Count < maxRivers; index++)
            {
                if (!known[index] || accumulation[index] < threshold)
                    continue;

                // 区域是**起点的判据**，不是产出之后的删点：河是一条中心线，从中间删一格就断成两截。
                // 于是"河只出现在这片地里"读作"只从这片地里发源"——下游可以流出框（水往低处走，拦它没道理）。
                if (allowOrigin != null && !allowOrigin(index))
                    continue;

                // 只在上游都还没到阈值的地方起头：这样每条河恰好被追一次。
                bool isSource = true;

                for (int d = 0; d < OffsetX.Length && isSource; d++)
                {
                    int x = index % width + OffsetX[d];
                    int z = index / width + OffsetZ[d];

                    if (x < 0 || z < 0 || x >= width || z >= height)
                        continue;

                    int neighbour = z * width + x;

                    if (known[neighbour] && flow[neighbour] == index && accumulation[neighbour] >= threshold)
                        isSource = false;
                }

                if (!isSource)
                    continue;

                var line = TraceRiver(accumulation, flow, index, known);

                if (line.Count < 2)
                    continue;

                var points = new Vector3[line.Count];
                int peak = 0;

                for (int i = 0; i < line.Count; i++)
                {
                    points[i] = new Vector3(line[i] % width, 0.0f, line[i] / width);
                    peak = Math.Max(peak, accumulation[line[i]]);
                }

                into.Add(new PCGRiver { Centerline = points, PeakFlow = peak });
            }

            return into.Count;
        }

        /// <summary>把格子下标换算成世界坐标（格子中心）。</summary>
        public static Vector3 CellToWorld(int index, int width, Vector3 origin, float cellSize, float y) =>
            new Vector3(origin.X + (index % width + 0.5f) * cellSize, y, origin.Z + (index / width + 0.5f) * cellSize);

        /// <summary>简单的二叉最小堆：填洼只需要"取出当前最低"。</summary>
        private sealed class MinHeap
        {
            private float[] _keys;
            private int[] _values;
            private int _count;

            public MinHeap(int capacity)
            {
                _keys = new float[Math.Max(4, capacity)];
                _values = new int[Math.Max(4, capacity)];
            }

            public int Count => _count;

            public void Push(float key, int value)
            {
                if (_count == _keys.Length)
                    Array.Resize(ref _keys, _count * 2);

                if (_count == _values.Length)
                    Array.Resize(ref _values, _count * 2);

                _keys[_count] = key;
                _values[_count] = value;

                int at = _count++;

                while (at > 0)
                {
                    int parent = (at - 1) / 2;

                    // 并列时按下标决胜：填洼的结果不能依赖插入顺序。
                    if (_keys[parent] < _keys[at] ||
                        (_keys[parent] == _keys[at] && _values[parent] <= _values[at]))
                        break;

                    Swap(parent, at);
                    at = parent;
                }
            }

            public int Pop(out float key)
            {
                key = _keys[0];
                int value = _values[0];

                _count--;
                _keys[0] = _keys[_count];
                _values[0] = _values[_count];

                int at = 0;

                while (true)
                {
                    int left = at * 2 + 1;
                    int right = left + 1;

                    if (left >= _count)
                        break;

                    int best = left;

                    if (right < _count &&
                        (_keys[right] < _keys[left] ||
                         (_keys[right] == _keys[left] && _values[right] < _values[left])))
                        best = right;

                    if (_keys[at] < _keys[best] ||
                        (_keys[at] == _keys[best] && _values[at] <= _values[best]))
                        break;

                    Swap(at, best);
                    at = best;
                }

                return value;
            }

            private void Swap(int a, int b)
            {
                (_keys[a], _keys[b]) = (_keys[b], _keys[a]);
                (_values[a], _values[b]) = (_values[b], _values[a]);
            }
        }
    }
}
