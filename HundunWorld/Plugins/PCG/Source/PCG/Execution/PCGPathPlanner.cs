// PCG Plugin - Path planning over a height field
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.35.

using System;
using System.Collections.Generic;
using FlaxEngine;

namespace PCG.Execution
{
    /// <summary>规划出来的路径上的一个点，连同规划器当时知道的那些信息。</summary>
    public struct PCGPathPoint
    {
        /// <summary>世界坐标，Y 取自高度场。</summary>
        public Vector3 Position;

        /// <summary>从路径起点算起的里程。</summary>
        public float Distance;

        /// <summary>到达此点的那一段的最陡坡度，单位为度。</summary>
        public float SlopeDegrees;
    }

    /// <summary>
    /// 一条路径被允许怎么走。
    /// </summary>
    public struct PCGPathOptions
    {
        /// <summary>规划网格的边长。</summary>
        public float CellSize;

        /// <summary>一个格子可通行所允许的最大坡度，单位为度。</summary>
        public float SlopeLimit;

        /// <summary>每多一度坡度，相对走平地额外增加的代价。</summary>
        public float SlopeCost;

        /// <summary>低于这个高度的地面不可通行——也就是湖泊或江河。</summary>
        public float WaterLevel;

        /// <summary>路径相对两个途经点之间直线允许偏离的最大距离。</summary>
        public float MaxDetour;

        /// <summary>对原始网格路线做几轮切角平滑。</summary>
        public int Smoothing;

        /// <summary>成品路径上输出点的间距。</summary>
        public float SampleSpacing;

        /// <summary>搜索最多展开多少个格子，免得一个孤立的途经点把编辑器挂住。</summary>
        public int MaxCells;

        /// <summary>
        /// 区域矩形（世界单位，XZ）。<see cref="RegionMaxX"/> 不大于 <see cref="RegionMinX"/> 时这一档不存在。
        /// </summary>
        /// <remarks>
        /// 路与洞穴/水系不同：路是**走法**，一块不许经过的地理所当然地绕过去（代价搜索本来就干这个），
        /// 所以这里能表达 <c>outside</c>（挖掉一块），而洞穴那种"截面序列"删一格就断，只能截断不能绕。
        /// </remarks>
        public float RegionMinX;

        /// <summary><see cref="RegionMinX"/> 那片矩形的 Z 下界。</summary>
        public float RegionMinZ;

        /// <summary><see cref="RegionMinX"/> 那片矩形的 X 上界。</summary>
        public float RegionMaxX;

        /// <summary><see cref="RegionMinX"/> 那片矩形的 Z 上界。</summary>
        public float RegionMaxZ;

        /// <summary>true 表示这块矩形**不许走**（绕开它）；false 表示只准在矩形里走。</summary>
        public bool RegionInvert;
    }

    /// <summary>
    /// 在高度场上找一条"真能修路"的路径。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 路既不是两地之间的直线，也不是一条随便的曲线：它走地面允许的地方，并偏好更缓的那条绕行。这是一张
    /// 代价由地形决定的网格上的最小代价路线，所以这里是"一个带高度委托的函数"而不是一个自己去读场景的节点——
    /// 值得较真的是这段算术，而它可以对着手算出来的高度场核对。
    /// </para>
    /// <para>
    /// 三个决定塑造了结果。超过坡度上限的地方是**不可通行**而不是"代价更高"——一个能靠代价买通的限制不算限制。
    /// 低于水位的地面同样不可通行，于是没有桥的路就不会穿过湖面。搜索范围被 <see cref="PCGPathOptions.MaxDetour"/>
    /// 限制在直线附近，因此一个在孤岛上的途经点不会让求解器把整片地形走一遍。
    /// </para>
    /// <para>
    /// 这里一切都是确定性的：并列时按格子位置决胜而不是按哈希顺序，所以同一张图在同一片地形上跑两次得到同一条路——
    /// 分区世界正需要这个性质。
    /// </para>
    /// </remarks>
    public static class PCGPathPlanner
    {
        /// <summary>斜向步长相对直向步长的倍数。</summary>
        private static readonly float Diagonal = 1.41421356f;

        /// <summary>
        /// 按给定顺序穿过每一个途经点，规划出一条路径。
        /// </summary>
        /// <param name="waypoints">要按顺序经过的 XZ 位置；至少两个才成其为路。</param>
        /// <param name="height">给定 XZ 处的地面高度；该处未知时返回 null。</param>
        /// <param name="options">各种限制与代价。</param>
        /// <param name="path">接收成品路径，从第一个途经点开始。</param>
        /// <param name="error">没能产出路径的原因。</param>
        /// <returns>穿过全部途经点的完整路径找到时为 true。</returns>
        public static bool TryPlan(
            IReadOnlyList<Vector2> waypoints, Func<float, float, float?> height, in PCGPathOptions options,
            List<PCGPathPoint> path, out string error)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            path.Clear();
            error = null;

            if (waypoints == null || waypoints.Count < 2)
            {
                error = "a path needs at least two waypoints";
                return false;
            }

            if (height == null)
            {
                error = "no height field was supplied";
                return false;
            }

            float cell = options.CellSize > 0.0f ? options.CellSize : 50.0f;
            float detour = Math.Max(options.MaxDetour, cell);
            int budget = options.MaxCells > 0 ? options.MaxCells : 200000;

            // 网格只覆盖"途经点包围盒 + 允许的绕行范围"，不多一分：把整片地形都铺进来的搜索，
            // 会让一条路变成一次全地形洪泛。
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;

            for (int i = 0; i < waypoints.Count; i++)
            {
                minX = Math.Min(minX, waypoints[i].X);
                maxX = Math.Max(maxX, waypoints[i].X);
                minZ = Math.Min(minZ, waypoints[i].Y);
                maxZ = Math.Max(maxZ, waypoints[i].Y);
            }

            var grid = new Grid(
                minX - detour, maxX + detour, minZ - detour, maxZ + detour, cell, options.SlopeLimit,
                options.WaterLevel, height);

            grid.ApplyRegion(options.RegionMinX, options.RegionMinZ,
                options.RegionMaxX, options.RegionMaxZ, options.RegionInvert);

            var route = new List<(int X, int Z)>();

            for (int i = 1; i < waypoints.Count; i++)
            {
                var from = grid.CellOf(waypoints[i - 1]);
                var to = grid.CellOf(waypoints[i]);

                if (!grid.IsPassable(from) || !grid.IsPassable(to))
                {
                    error = $"waypoint {i} sits on ground a road may not use (too steep, or under water)";
                    return false;
                }

                if (!TrySearch(grid, from, to, options.SlopeCost, budget, out var leg, out string searchError))
                {
                    error = $"no route between waypoint {i - 1} and {i}: {searchError}";
                    return false;
                }

                // 除第一段外，每一段的首格就是上一段的末格，拼接时要跳过重复的那一格。
                for (int c = route.Count == 0 ? 0 : 1; c < leg.Count; c++)
                    route.Add(leg[c]);
            }

            if (route.Count < 2)
            {
                error = "the waypoints are the same cell, so there is no path to walk";
                return false;
            }

            Simplify(route);
            Smooth(grid, route, options.Smoothing);

            Resample(grid, route, options.SampleSpacing, path);
            return true;
        }

        /// <summary>
        /// 在可通行格子上跑 A*，启发函数用平距。
        /// </summary>
        /// <remarks>
        /// 启发函数取到目标的八向距离，它不会高估最便宜路线的代价（代价至少包含走过的距离），
        /// 所以第一次弹出目标时得到的路线，对当前这个代价函数而言就是最优的。
        /// </remarks>
        private static bool TrySearch(
            Grid grid, (int X, int Z) from, (int X, int Z) to, float slopeCost, int budget,
            out List<(int X, int Z)> route, out string error)
        {
            route = null;
            error = null;

            int width = grid.Width;
            int height = grid.Height;
            var open = new List<int>();
            var cost = new float[width * height];
            var cameFrom = new int[width * height];
            var closed = new bool[width * height];

            for (int i = 0; i < cost.Length; i++)
            {
                cost[i] = float.MaxValue;
                cameFrom[i] = -1;
            }

            int start = grid.Index(from.X, from.Z);
            int goal = grid.Index(to.X, to.Z);

            cost[start] = 0.0f;
            open.Add(start);

            int expanded = 0;

            while (open.Count > 0)
            {
                if (++expanded > budget)
                {
                    error = $"the search gave up after {budget} cells";
                    return false;
                }

                // 用（代价 + 启发，再按下标）选出下一个，而不是用堆：网格本来就不大，
                // 而这条决胜规则才是结果可复现的原因。
                int best = 0;
                float bestScore = float.MaxValue;

                for (int i = 0; i < open.Count; i++)
                {
                    int candidate = open[i];
                    float score = cost[candidate] + Heuristic(grid, candidate, goal);

                    if (score < bestScore || (score == bestScore && candidate < open[best]))
                    {
                        bestScore = score;
                        best = i;
                    }
                }

                int current = open[best];
                open.RemoveAt(best);

                if (current == goal)
                    break;

                if (closed[current])
                    continue;

                closed[current] = true;

                int cx = current % width;
                int cz = current / width;

                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0)
                            continue;

                        int nx = cx + dx;
                        int nz = cz + dz;

                        if (nx < 0 || nz < 0 || nx >= width || nz >= height)
                            continue;

                        int neighbour = grid.Index(nx, nz);

                        if (closed[neighbour] || !grid.IsPassable(nx, nz))
                            continue;

                        // 只有两个正交邻格都可通行时才允许走斜线：从崖体上切角，正是路绝不能做的事。
                        if (dx != 0 && dz != 0 && (!grid.IsPassable(nx, cz) || !grid.IsPassable(cx, nz)))
                            continue;

                        float slope = grid.SlopeBetween(cx, cz, nx, nz);

                        // 超过上限的一步**直接不走**，而不是"更贵"：一个能靠代价买通的限制不算限制
                        // （此前这里只加了代价，于是路会笔直穿过它本该绕开的陡墙——注释与实现当时并不一致）。
                        if (slope > grid.SlopeLimit)
                            continue;

                        float step = dx != 0 && dz != 0 ? Diagonal : 1.0f;
                        float candidate = cost[current] + step * (1.0f + slopeCost * slope);

                        if (candidate >= cost[neighbour])
                            continue;

                        cost[neighbour] = candidate;
                        cameFrom[neighbour] = current;

                        if (!open.Contains(neighbour))
                            open.Add(neighbour);
                    }
                }
            }

            if (cameFrom[goal] < 0 && goal != start)
            {
                error = "every route is blocked (a slope limit or water that cannot be crossed)";
                return false;
            }

            route = new List<(int X, int Z)>();

            for (int at = goal; at >= 0; at = cameFrom[at])
            {
                route.Add((at % width, at / width));

                if (at == start)
                    break;
            }

            route.Reverse();
            return true;
        }

        private static float Heuristic(Grid grid, int cell, int goal)
        {
            int dx = Math.Abs(cell % grid.Width - goal % grid.Width);
            int dz = Math.Abs(cell / grid.Width - goal / grid.Width);

            // Octile: the straight steps plus the extra a diagonal costs.
            return Math.Max(dx, dz) + (Diagonal - 1.0f) * Math.Min(dx, dz);
        }

        /// <summary>去掉落在前后邻格直线上的那些格子。</summary>
        private static void Simplify(List<(int X, int Z)> route)
        {
            for (int i = route.Count - 2; i >= 1; i--)
            {
                var previous = route[i - 1];
                var current = route[i];
                var next = route[i + 1];

                if ((current.X - previous.X) * (next.Z - current.Z) ==
                    (current.Z - previous.Z) * (next.X - current.X))
                {
                    route.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 对网格路线切角，但只保留仍然落在可通行地面上的那些切法。
        /// </summary>
        /// <remarks>
        /// 原始 A* 路线像楼梯，路不像。每一轮把每个内部点朝其前后邻点的中点挪四分之一，
        /// 并且只有当它新形成的两段仍然可通行时才采纳这次挪动——不看地形的平滑，正是让一条路反过来横穿
        /// 它当初特意绕开的崖体的原因。
        /// </remarks>
        private static void Smooth(Grid grid, List<(int X, int Z)> route, int passes)
        {
            for (int pass = 0; pass < passes; pass++)
            {
                for (int i = 1; i < route.Count - 1; i++)
                {
                    var previous = route[i - 1];
                    var current = route[i];
                    var next = route[i + 1];

                    int x = (int)Math.Round(current.X * 0.5f + (previous.X + next.X) * 0.25f);
                    int z = (int)Math.Round(current.Z * 0.5f + (previous.Z + next.Z) * 0.25f);

                    if (x == current.X && z == current.Z)
                        continue;

                    if (!grid.IsPassable(x, z) || !grid.IsWalkableLine(previous, (x, z)) ||
                        !grid.IsWalkableLine((x, z), next))
                        continue;

                    route[i] = (x, z);
                }
            }
        }

        /// <summary>
        /// 沿成品路线按 <paramref name="spacing"/> 的间距输出点。
        /// </summary>
        private static void Resample(Grid grid, List<(int X, int Z)> route, float spacing, List<PCGPathPoint> path)
        {
            float step = spacing > 0.0f ? spacing : grid.CellSize;

            // 起点上面已经单独摆了一颗（`Distance = 0`），所以**下一颗**要落在 `step` 处：
            // 这里从 0 起会让第一段第一次循环就原地再摆一颗，与起点位置与 Distance 全都相同。
            // 后果不是难看而是**共面**：路上每一层几何都吃这个点，于是起点处两件完全重合的
            // 路面片叠在一起（z-fighting），而 Actor 清单上只是"多了一件"。
            float carried = step;

            var previous = grid.PositionOf(route[0]);
            float carriedHeight = grid.HeightAt(previous) ?? 0.0f;

            path.Add(new PCGPathPoint
            {
                Position = new Vector3(previous.X, carriedHeight, previous.Y),
                Distance = 0.0f,
                SlopeDegrees = 0.0f,
            });

            float distance = 0.0f;

            for (int i = 1; i < route.Count; i++)
            {
                var from = grid.PositionOf(route[i - 1]);
                var to = grid.PositionOf(route[i]);

                // 没有高度的格子沿用前一个格子的高度，路径不会因为高度场里的空洞而掉到 0；
                // 这与可通行判定同源——那里未知地面本来就算不可通行。
                float fromHeight = grid.HeightAt(from) ?? carriedHeight;
                float toHeight = grid.HeightAt(to) ?? fromHeight;

                float length = (to - from).Length;

                if (length <= 1e-4f)
                    continue;

                var direction = (to - from) / length;
                float slope = Grid.Degrees(Math.Abs(toHeight - fromHeight), length);
                float at = carried;

                while (at <= length)
                {
                    var position = from + direction * at;
                    float total = distance + at;

                    path.Add(new PCGPathPoint
                    {
                        Position = new Vector3(position.X, fromHeight + (toHeight - fromHeight) * (at / length), position.Y),
                        Distance = total,
                        SlopeDegrees = slope,
                    });

                    at += step;
                }

                carried = at - length;
                distance += length;
            }

            // 无论间距余下多少，最后一个途经点都必须落在路上。
            var last = grid.PositionOf(route[route.Count - 1]);
            var lastPoint = path[path.Count - 1];

            if ((lastPoint.Position - new Vector3(last.X, lastPoint.Position.Y, last.Y)).Length > grid.CellSize * 0.25f)
            {
                path.Add(new PCGPathPoint
                {
                    Position = new Vector3(last.X, grid.HeightAt(last) ?? lastPoint.Position.Y, last.Y),
                    Distance = distance,
                    SlopeDegrees = lastPoint.SlopeDegrees,
                });
            }

            carriedHeight = lastPoint.Position.Y;
        }

        /// <summary>代价网格：高度、可通行性，以及相邻格之间的距离。</summary>
        private sealed class Grid
        {
            private readonly float _minX;
            private readonly float _minZ;
            private readonly float[] _heights;
            private readonly bool[] _known;
            private readonly float _slopeLimit;
            private readonly float _waterLevel;

            public readonly int Width;
            public readonly int Height;
            public readonly float CellSize;

            public Grid(
                float minX, float maxX, float minZ, float maxZ, float cell, float slopeLimit, float waterLevel,
                Func<float, float, float?> height)
            {
                CellSize = cell;
                _minX = minX;
                _minZ = minZ;
                _slopeLimit = slopeLimit;
                _waterLevel = waterLevel;

                Width = Math.Max(2, (int)Math.Ceiling((maxX - minX) / cell) + 1);
                Height = Math.Max(2, (int)Math.Ceiling((maxZ - minZ) / cell) + 1);

                _heights = new float[Width * Height];
                _known = new bool[Width * Height];

                for (int z = 0; z < Height; z++)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        var sample = height(minX + x * cell, minZ + z * cell);

                        if (sample.HasValue)
                        {
                            _heights[z * Width + x] = sample.Value;
                            _known[z * Width + x] = true;
                        }
                    }
                }
            }

            /// <summary>
            /// 区域矩形（世界单位）。由参数带进来，不改构造签名——那个签名被几处测试共用。
            /// 两个角写平（上界不大于下界）时这一档**不存在**，可通行性与改动前逐格相同。
            /// </summary>
            public void ApplyRegion(float minX, float minZ, float maxX, float maxZ, bool invert)
            {
                if (maxX <= minX || maxZ <= minZ)
                {
                    _hasRegion = false;
                    return;
                }

                _regionMinX = minX;
                _regionMinZ = minZ;
                _regionMaxX = maxX;
                _regionMaxZ = maxZ;
                _regionInvert = invert;
                _hasRegion = true;
            }

            private float _regionMinX;
            private float _regionMinZ;
            private float _regionMaxX;
            private float _regionMaxZ;
            private bool _regionInvert;
            private bool _hasRegion;

            public int Index(int x, int z) => z * Width + x;

            public (int X, int Z) CellOf(Vector2 world) => (
                Math.Min(Math.Max((int)Math.Round((world.X - _minX) / CellSize), 0), Width - 1),
                Math.Min(Math.Max((int)Math.Round((world.Y - _minZ) / CellSize), 0), Height - 1));

            public Vector2 PositionOf((int X, int Z) cell) =>
                new Vector2(_minX + cell.X * CellSize, _minZ + cell.Z * CellSize);

            public float? HeightAt(Vector2 world)
            {
                var cell = CellOf(world);
                int index = Index(cell.X, cell.Z);

                return _known[index] ? _heights[index] : (float?)null;
            }

            public bool IsPassable(int x, int z)
            {
                if (x < 0 || z < 0 || x >= Width || z >= Height)
                    return false;

                int index = Index(x, z);

                if (!_known[index])
                    return false;

                // 先判水：不管岸边是什么样，湖面就是不可通行。
                if (_heights[index] <= _waterLevel)
                    return false;

                // 区域是**可通行性**的一部分，不是事后把落点搬开：代价搜索会绕开不可走的格子，
                // 绕不过去时它自己会报"每条路都被挡住"——那才是诚实的失败，而不是一条穿湖的路。
                if (_hasRegion)
                {
                    // `PositionOf` 返回的是 Vector2：它的 X 是世界 X、它的 Y 就是世界 Z（规划器一直是这个约定）。
                    var world = PositionOf((x, z));
                    bool inside = world.X >= _regionMinX && world.X <= _regionMaxX &&
                                  world.Y >= _regionMinZ && world.Y <= _regionMaxZ;

                    if (inside == _regionInvert)
                        return false;
                }

                return true;
            }

            public bool IsPassable((int X, int Z) cell) => IsPassable(cell.X, cell.Z);

            /// <summary>坡度上限（度）。超过它的一步不可通行。</summary>
            public float SlopeLimit => _slopeLimit;

            /// <summary>Slope of the step from one cell to a neighbour, in degrees.</summary>
            public float SlopeBetween(int x0, int z0, int x1, int z1)
            {
                int a = Index(x0, z0);
                int b = Index(x1, z1);

                if (!_known[a] || !_known[b])
                    return 90.0f;

                float run = (float)Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0)) * CellSize;

                if (run <= 1e-4f)
                    return 0.0f;

                return Degrees(Math.Abs(_heights[b] - _heights[a]), run);
            }

            /// <summary>给定升高与水平距离，算出坡度（度）；代价计算与日志都走它。</summary>
            public static float Degrees(float rise, float run) =>
                run <= 1e-4f ? 90.0f : (float)Math.Atan2(rise, run) * 57.29578f;

            /// <summary>两格之间的直线步进是否全程可通行。</summary>
            public bool IsWalkableLine((int X, int Z) from, (int X, int Z) to)
            {
                int steps = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Z - from.Z));

                if (steps == 0)
                    return IsPassable(from);

                int previousX = from.X;
                int previousZ = from.Z;
                bool passable = true;

                for (int i = 1; i <= steps && passable; i++)
                {
                    int x = from.X + (to.X - from.X) * i / steps;
                    int z = from.Z + (to.Z - from.Z) * i / steps;

                    passable = IsPassable(x, z) && SlopeBetween(previousX, previousZ, x, z) <= _slopeLimit;

                    if (passable && x != previousX && z != previousZ &&
                        (!IsPassable(x, previousZ) || !IsPassable(previousX, z)))
                    {
                        // 这一步斜着切了角，与搜索里用的是同一条规则。
                        passable = false;
                    }

                    previousX = x;
                    previousZ = z;
                }

                return passable;
            }
        }
    }
}
