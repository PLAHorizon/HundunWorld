// PCG 插件 —— 布尔点运算用的点索引
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.4。

using System;
using System.Collections.Generic;
using FlaxEngine;

namespace PCG.Execution
{
    /// <summary>
    /// 盖在一份点集上的均匀网格，用来回答"这儿有没有东西"，不必做 O(n·m) 的扫描。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 布尔运算（差集、交集）对每个输入点只问一句：另一份集合里有没有点能够到它？
    /// 直白地做就是 n·m 次比较 —— 两层各 2 万点的散布就是四亿次 AABB 测试，
    /// 这正是"图能交互"与"没人愿意等"之间的差别。把被查的那一份先分一次桶，
    /// 每句问题就只剩几个候选。
    /// </para>
    /// <para>
    /// 格子边长由调用方取成"它要测的最大范围的两倍"，于是任何能够到某个格子的盒子，
    /// 要么存在那个格子里、要么存在它的邻格里 —— 与自剪枝过滤器那张网格用的是同一个论证。
    /// 一份点集如果占地远多于它的点数（整张地图上摆几件道具），用不了稠密网格，
    /// 于是退回一张平铺列表：更慢，但依然正确；而 harness 把两条路径都对着暴力参照钉过一遍，
    /// 不是信哪一条。
    /// </para>
    /// </remarks>
    public sealed class PCGPointIndex
    {
        private const long MaxDenseCells = 1L << 20;
        private const int DenseCellBudget = 8;

        private readonly PCGPoint[] _points;
        private readonly float _cell;
        private readonly bool _planeOnly;

        // 稠密布局。
        private readonly int[] _head;
        private readonly int[] _next;
        private readonly int _minX;
        private readonly int _minZ;
        private readonly int _width;
        private readonly int _depth;

        // 稀疏回退。
        private readonly Dictionary<long, List<int>> _buckets;

        /// <summary>索引不得不退回哈希桶时为真。</summary>
        public bool IsSparse { get; }

        /// <summary>被索引的点数。</summary>
        public int Count { get; }

        /// <param name="points">要索引的那一份点。</param>
        /// <param name="cell">能覆盖任何测试体积的边长；必须为正。</param>
        /// <param name="planeOnly">把竖直轴从键里折掉，用于不看高度的测试。</param>
        public PCGPointIndex(PCGPoint[] points, float cell, bool planeOnly)
        {
            _points = points ?? Array.Empty<PCGPoint>();
            _cell = cell > 1e-4f ? cell : 1e-4f;
            _planeOnly = planeOnly;
            Count = _points.Length;

            if (Count == 0)
            {
                _head = Array.Empty<int>();
                return;
            }

            _next = new int[Count];
            Array.Fill(_next, -1);

            int minX = int.MaxValue, maxX = int.MinValue;
            int minZ = int.MaxValue, maxZ = int.MinValue;

            for (int i = 0; i < Count; i++)
            {
                var position = _points[i].Position;
                int cx = (int)MathF.Floor(position.X / _cell);
                int cz = (int)MathF.Floor(position.Z / _cell);

                if (cx < minX) minX = cx;
                if (cx > maxX) maxX = cx;
                if (cz < minZ) minZ = cz;
                if (cz > maxZ) maxZ = cz;
            }

            long width = (long)maxX - minX + 1;
            long depth = (long)maxZ - minZ + 1;
            long cells = _planeOnly ? width * depth : long.MaxValue;

            bool dense = _planeOnly && cells <= MaxDenseCells && cells <= (long)Count * DenseCellBudget;
            IsSparse = !dense;

            if (dense)
            {
                _minX = minX;
                _minZ = minZ;
                _width = (int)width;
                _depth = (int)depth;
                _head = new int[(int)cells];

                // 每条链的终止符。不填的话格子默认是 0，于是空格子指向 0 号点，
                // 走链就永远走不到头 —— 那是卡死而不是答错，而且只在第一次真查询时暴露，
                // 构造阶段看不出来。
                Array.Fill(_head, -1);
            }
            else
            {
                _head = Array.Empty<int>();
                _buckets = new Dictionary<long, List<int>>(Count / 2 + 1);
            }

            for (int i = 0; i < Count; i++)
                Add(i);
        }

        private void Add(int index)
        {
            var position = _points[index].Position;

            if (!IsSparse)
            {
                int col = (int)MathF.Floor(position.X / _cell) - _minX;
                int row = (int)MathF.Floor(position.Z / _cell) - _minZ;
                int slot = row * _width + col;

                _next[index] = _head[slot];
                _head[slot] = index;
                return;
            }

            long key = KeyAt(position, 0, 0, 0);
            if (!_buckets.TryGetValue(key, out var bucket))
            {
                bucket = new List<int>(4);
                _buckets[key] = bucket;
            }

            bucket.Add(index);
        }

        /// <summary>
        /// 只要有一个被索引的点在 <paramref name="position"/> 附近满足 <paramref name="test"/> 就返回真。
        /// </summary>
        /// <param name="position">查询位置，与被索引的点同一空间。</param>
        /// <param name="test">对每个候选问一次；返回真即停止搜索。</param>
        public bool AnyNearby(in Vector3 position, Func<int, bool> test)
        {
            if (test == null)
                throw new ArgumentNullException(nameof(test));

            if (Count == 0 || _next == null)
                return false;

            if (!IsSparse)
            {
                int cx = (int)MathF.Floor(position.X / _cell) - _minX;
                int cz = (int)MathF.Floor(position.Z / _cell) - _minZ;

                for (int dz = -1; dz <= 1; dz++)
                {
                    int row = cz + dz;
                    if (row < 0 || row >= _depth)
                        continue;

                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int col = cx + dx;
                        if (col < 0 || col >= _width)
                            continue;

                        for (int other = _head[row * _width + col]; other >= 0; other = _next[other])
                        {
                            if (test(other))
                                return true;
                        }
                    }
                }

                return false;
            }

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (!_buckets.TryGetValue(KeyAt(position, dx, dy, dz), out var bucket))
                            continue;

                        for (int b = 0; b < bucket.Count; b++)
                        {
                            if (test(bucket[b]))
                                return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>访问某个位置附近的所有候选，不提前退出。</summary>
        public void ForEachNearby(in Vector3 position, Action<int> visit)
        {
            if (visit == null)
                throw new ArgumentNullException(nameof(visit));

            AnyNearby(position, index =>
            {
                visit(index);
                return false;
            });
        }

        private long KeyAt(in Vector3 position, int dx, int dy, int dz)
        {
            int cx = (int)MathF.Floor(position.X / _cell) + dx;
            int cy = (_planeOnly ? 0 : (int)MathF.Floor(position.Y / _cell)) + dy;
            int cz = (int)MathF.Floor(position.Z / _cell) + dz;

            unchecked
            {
                return ((long)cx * 73856093L) ^ ((long)cy * 19349663L) ^ ((long)cz * 83492791L);
            }
        }
    }
}
