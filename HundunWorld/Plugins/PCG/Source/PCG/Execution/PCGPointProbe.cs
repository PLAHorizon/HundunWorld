// PCG 插件 - 布尔点运算用的点包含探测
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §5.1.4。

using System;
using FlaxEngine;
using PCG.Nodes;

namespace PCG.Execution
{
    /// <summary>
    /// 回答"这个位置在不在另一份集合里"，口径与 UE5 的 Difference 与 Intersection 一致：
    /// 要么对照另一份集合的点包围盒判包含，要么按到它中心的距离判定。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 两个节点问的是同一个问题，只在对答案时留哪一侧上不同，所以这条判定只写一次、放在这里。
    /// "在内部"若落成两份实现各自漂移，difference 与 intersection 就会停止互为补集——
    /// harness 靠这条检查钉住该性质：每个输入点最终恰好落在两份产出之一里，
    /// 也就是不会同时进两份、也不会两份都不进。
    /// </para>
    /// <para>
    /// 包围盒模式下，查询点对照的是被 padding 放大过的已索引盒，这正是从散点里挖出一条路廊的做法：
    /// 只调被裁那一层的内缩或外扩，不必逐棵树去改。
    /// 平面模式把 Y 从分桶与盒判定里一起折掉，
    /// 与插件其余部分"落地散点无视高度"的口径一致。
    /// </para>
    /// </remarks>
    public readonly struct PCGPointProbe
    {
        private readonly PCGPoint[] _set;
        private readonly PCGPointIndex _index;
        private readonly bool _distanceMode;
        private readonly float _distanceSquared;
        private readonly bool _planeOnly;
        private readonly float _padding;

        /// <summary>已索引集合被迫退回哈希分桶时为真。</summary>
        public bool IsSparse => _index?.IsSparse ?? false;

        /// <summary>探测对照了多少个点。</summary>
        public int Count => _set?.Length ?? 0;

        /// <param name="set">被裁的那份集合（B）。</param>
        /// <param name="distanceMode">按到中心的距离判定，而不是在包围盒内判包含。</param>
        /// <param name="maxDistance">距离模式下的可达半径；其余模式下忽略。</param>
        /// <param name="planeOnly">两种模式下都忽略垂直轴。</param>
        /// <param name="padding">把盒（或可达半径）撑大，从而能加宽或内缩一条廊道。</param>
        public PCGPointProbe(PCGPoint[] set, bool distanceMode, float maxDistance, bool planeOnly, float padding)
        {
            _set = set ?? Array.Empty<PCGPoint>();
            _distanceMode = distanceMode;
            _planeOnly = planeOnly;
            _padding = Math.Max(0.0f, padding);
            _distanceSquared = 0.0f;

            float cell;

            if (distanceMode)
            {
                float reach = Math.Max(0.0f, maxDistance) + _padding;
                _distanceSquared = reach * reach;
                cell = Math.Max(reach, 1e-4f);
            }
            else
            {
                float largest = 0.0f;

                for (int i = 0; i < _set.Length; i++)
                {
                    var half = PCGPointTransform.HalfExtents(_set[i].Bounds);
                    largest = planeOnly
                        ? Math.Max(largest, Math.Max(half.X, half.Z))
                        : Math.Max(largest, Math.Max(half.X, Math.Max(half.Y, half.Z)));
                }

                // 一个盒只有当它的半尺寸加上 padding 够得着时，才可能包含本格之外的点，
                // 因此取两倍便是能让真实命中落在相邻一圈之内的最小格子。
                cell = Math.Max(largest * 2.0f + _padding * 2.0f, 1e-4f);
            }

            _index = _set.Length > 0 ? new PCGPointIndex(_set, cell, planeOnly) : null;
        }

        /// <summary>当 <paramref name="position"/> 落在被裁集合内部时为真。</summary>
        /// <remarks>
        /// 搜索要用的东西先都拷成局部变量：结构体内的 lambda 既不能捕获结构体自身的成员，
        /// 也不能捕获一个 <c>in</c> 参数，所以回调里直接读字段根本编译不过。
        /// 这些拷贝同时也让回调在运行期间看到的探测状态保持固定。
        /// </remarks>
        public bool IsInside(in Vector3 position)
        {
            var index = _index;
            if (index == null)
                return false;

            var set = _set;
            var query = position;

            if (_distanceMode)
            {
                float limitSquared = _distanceSquared;
                bool planeOnly = _planeOnly;

                return index.AnyNearby(query, other =>
                {
                    float dx = set[other].Position.X - query.X;
                    float dz = set[other].Position.Z - query.Z;
                    float squared = dx * dx + dz * dz;

                    if (!planeOnly)
                    {
                        float dy = set[other].Position.Y - query.Y;
                        squared += dy * dy;
                    }

                    return squared <= limitSquared;
                });
            }

            float padding = _padding;
            bool ignoreHeight = _planeOnly;

            return index.AnyNearby(query, other =>
            {
                var bounds = set[other].Bounds;

                if (query.X < bounds.Minimum.X - padding || query.X > bounds.Maximum.X + padding)
                    return false;

                if (query.Z < bounds.Minimum.Z - padding || query.Z > bounds.Maximum.Z + padding)
                    return false;

                if (ignoreHeight)
                    return true;

                return query.Y >= bounds.Minimum.Y - padding && query.Y <= bounds.Maximum.Y + padding;
            });
        }
    }
}
