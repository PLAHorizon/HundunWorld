using System;
using System.Collections.Generic;
using System.Globalization;
using Horizon.Game.Message.Network;
using Horizon.Planet;

namespace HundunWorld.Game.Planet
{
    /// <summary>
    /// 区域快照的<em>客户端</em>决策规则：什么时候该问、拿到之后该加载什么/放下什么。
    /// <para><b>纯 C#，不含引擎类型</b>——源码被 <c>Horizon.Game.Gateway.Tests</c> 链进去直接编译跑判据，
    /// 而不是复制一份。只在编辑器里"看着对"不算验过。</para>
    /// <para>为什么单独一层：§6.4 的三层 AOI（区域 → 100 m cell → 实体）落到客户端就是
    /// "订阅集变了要增删哪些块"。这件事一旦和 <c>Debug.Log</c>、会话对象混在一起就测不到，
    /// 而它错的方式很安静：多留一块不报错、少留一块就是邻区的人不出现。</para>
    /// </summary>
    public static class PlanetRegionWatchRules
    {
        /// <summary>§6.3 第 1 步：距边界 1 km 时预先请求邻区。这里是线上口径（厘米）。</summary>
        public const double PrefetchMarginCm = 100_000.0;

        /// <summary>两次请求之间的最小间隔。站在边界上抖几厘米就会每帧发一次，把 grain 打满。</summary>
        public const long MinRequestIntervalMs = 1000;

        /// <summary>拿到快照之后该做的处置。</summary>
        public enum WatchAction
        {
            /// <summary>没有回执（超时/丢包）。不等于"周围没人"：保持现有订阅集。</summary>
            Ignore,
            /// <summary>受理了，按差集增删块。</summary>
            Apply,
            /// <summary>服务端说我们还没落位（归属为 0）：先落位再读。</summary>
            Rebind,
            /// <summary>口径不符：停手并报警，重试只会原地打转（同 §13.52 的 RefuseWorld）。</summary>
            RefuseWorld,
        }

        /// <summary>订阅集的差集。<b>数组按内容比，不按引用比</b>：每次 new 一个数组就"变了"，
        /// 那会让每一帧都产生卸载/重载。</summary>
        public readonly struct Diff
        {
            public Diff(long[] addRegions, long[] dropRegions, PlanetCellKey[] addCells, PlanetCellKey[] dropCells)
            {
                AddRegions = addRegions ?? Array.Empty<long>();
                DropRegions = dropRegions ?? Array.Empty<long>();
                AddCells = addCells ?? Array.Empty<PlanetCellKey>();
                DropCells = dropCells ?? Array.Empty<PlanetCellKey>();
            }

            public long[] AddRegions { get; }
            public long[] DropRegions { get; }
            public PlanetCellKey[] AddCells { get; }
            public PlanetCellKey[] DropCells { get; }

            public bool IsEmpty => AddRegions.Length == 0 && DropRegions.Length == 0
                                   && AddCells.Length == 0 && DropCells.Length == 0;

            public override string ToString() => string.Format(CultureInfo.InvariantCulture,
                "diff +regions={0} -regions={1} +cells={2} -cells={3}",
                AddRegions.Length, DropRegions.Length, AddCells.Length, DropCells.Length);
        }

        /// <summary>
        /// 该不该发这一趟快照请求。<b>每个不发的理由都分开给</b>：实机上"没发"和"发了被拒"
        /// 一样难查，而它们的处置不同。
        /// </summary>
        public static bool ShouldRequest(long nowMs, long lastRequestMs, long crossedRegionId,
                                         long lastRequestedForRegionId, bool withinPrefetchMargin,
                                         bool responsePending, out string why)
        {
            why = "";
            if (responsePending)
            {
                why = "上一趟还没回来（在飞一条就够，快照是幂等读）";
                return false;
            }
            if (crossedRegionId != 0 && crossedRegionId != lastRequestedForRegionId)
                return true;                                    // 换过区：必须重取订阅集（§6.3 第 1 步）

            if (!withinPrefetchMargin)
            {
                why = "离边界还远，且区域没变过";
                return false;
            }
            long sinceLast = nowMs - lastRequestMs;
            if (sinceLast >= 0 && sinceLast < MinRequestIntervalMs)
            {
                why = string.Format(CultureInfo.InvariantCulture,
                    "边界附近但节流中：距上次请求 {0} ms < {1} ms", sinceLast, MinRequestIntervalMs);
                return false;
            }
            return true;
        }

        /// <summary>
        /// 局部坐标是否进入边界预取带。<b>两个轴都要看</b>：只比 X 会在贴着 Y 边界时完全不预取，
        /// 于是"跨区后才开始加载"——那正是 §13.15 之后要避免的加载空窗。
        /// </summary>
        public static bool WithinPrefetchMargin(double localX, double localZ, double halfExtentX,
                                                double halfExtentZ, double marginCm = PrefetchMarginCm)
        {
            if (!(marginCm >= 0) || !double.IsFinite(marginCm))
                throw new ArgumentOutOfRangeException(nameof(marginCm), marginCm, "预取余量必须是有限非负值");
            if (!double.IsFinite(localX) || !double.IsFinite(localZ)
                || !(halfExtentX > 0) || !(halfExtentZ > 0))
                return false;                                   // 输入不可用时当作"不在带内"：宁可不预取，也不要拿 NaN 去算距离
            return Math.Abs(localX) >= halfExtentX - marginCm
                || Math.Abs(localZ) >= halfExtentZ - marginCm;
        }

        /// <summary>回执处置。</summary>
        public static WatchAction Decide(bool hasResponse, bool accepted, int code, long ownerRegionId)
        {
            if (!hasResponse) return WatchAction.Ignore;
            if (!accepted)
            {
                var reject = (PlanetRegionRejectCode)code;
                if (reject == PlanetRegionRejectCode.CaliberMismatch) return WatchAction.RefuseWorld;
                // 其余拒绝（角色号非法 / 会话绑定 / 还没落位）都指向同一件处置：先落位
                return WatchAction.Rebind;
            }
            if (ownerRegionId == 0) return WatchAction.Rebind;  // 受理但没归属：空订阅集是真的空，但不能当"有位置"用
            return WatchAction.Apply;
        }

        /// <summary>算差集。current/wanted 里出现 null 都按空集处理，客户端不必写两种分支。</summary>
        public static Diff DiffAgainst(long[] currentRegions, long[] wantedRegions,
                                       PlanetCellKey[] currentCells, PlanetCellKey[] wantedCells)
        {
            var cur = new HashSet<long>(currentRegions ?? Array.Empty<long>());
            var want = new HashSet<long>(wantedRegions ?? Array.Empty<long>());
            var add = new List<long>();
            var drop = new List<long>();
            foreach (var id in want) if (!cur.Contains(id)) add.Add(id);
            foreach (var id in cur) if (!want.Contains(id)) drop.Add(id);
            add.Sort();                                          // 排序只是为了差集可逐条对账，不靠集合枚举顺序

            var curCells = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in currentCells ?? Array.Empty<PlanetCellKey>()) curCells.Add(CellKey(c));
            var wantCells = new HashSet<string>(StringComparer.Ordinal);
            var wantLookup = new Dictionary<string, PlanetCellKey>(StringComparer.Ordinal);
            foreach (var c in wantedCells ?? Array.Empty<PlanetCellKey>())
            {
                string key = CellKey(c);
                if (wantCells.Add(key)) wantLookup[key] = c;
            }
            var addCells = new List<PlanetCellKey>();
            var dropCells = new List<PlanetCellKey>();
            foreach (var key in wantCells) if (!curCells.Contains(key)) addCells.Add(wantLookup[key]);
            foreach (var c in currentCells ?? Array.Empty<PlanetCellKey>())
                if (!wantCells.Contains(CellKey(c))) dropCells.Add(c);
            addCells.Sort(static (a, b) =>
                (a.RegionId, a.Cx, a.Cz).CompareTo((b.RegionId, b.Cx, b.Cz)));

            return new Diff(add.ToArray(), drop.ToArray(), addCells.ToArray(), dropCells.ToArray());
        }

        /// <summary>
        /// cell 的身份。<b>三样都进键</b>：区域号是位打包 long，单看 (Cx,Cz) 会把不同面的同一列
        /// 当成同一格——那会卸载掉错误的一块，而且不报错。
        /// </summary>
        private static string CellKey(PlanetCellKey c) =>
            string.Format(CultureInfo.InvariantCulture, "{0:X16}/{1}/{2}", c.RegionId, c.Cx, c.Cz);
    }
}
