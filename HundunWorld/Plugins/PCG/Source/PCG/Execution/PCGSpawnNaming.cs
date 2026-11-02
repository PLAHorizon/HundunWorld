// PCG 插件 - 生成场景对象的共享命名契约

namespace PCG.Execution
{
    /// <summary>
    /// 生成器节点与“拥有其产物的体积”之间的命名契约。
    /// </summary>
    /// <remarks>
    /// 生成器节点建 Actor 时并不知道这次运行是哪个体积发起的，而体积需要
    /// 精确认出自己的产物，好让重生成是**替换**它而不是再叠一份。
    /// 于是两边约定同一个前缀。它放在这里而不是放在任何一方，是为了让谁都无法
    /// 在对方不知情的情况下改动它。
    /// </remarks>
    public static class PCGSpawnNaming
    {
        /// <summary>
        /// 每个 PCG 生成的 Actor 名字都以此前缀开头。
        /// </summary>
        /// <remarks>
        /// 前缀就是归属标记，逐格归属挂在它上面（见 <see cref="CellToken"/>）。
        /// 这里用不了运行期标签：Flax 的 Tag 是工程标签表里的下标，没法从任意数据
        /// 现场造一个出来，所以“逐格标签”建不出来。而名字是可以枚举的
        /// （遍历宿主的子级即可），且能挺过场景重载——这正是清理所需的。
        /// </remarks>
        public const string GeneratedPrefix = "PCG_Gen_";

        /// <summary>
        /// 标记物件来自哪个分区格子的标记；没有分区时表示整跑。
        /// </summary>
        /// <remarks>
        /// 逐格清理要的就是这份归属记录。运行期标签顶不上：Flax 的 Tag 是
        /// 工程标签表里的下标、运行期无法从任意数据造出；而名字是唯一既有
        /// 挺过场景重载、又不用查询就能比较的逐对象数据。
        /// 生成前缀始终在名字最前面，所以“认出 PCG 产物”从不依赖这个
        /// 标记。
        /// </remarks>
        public static string CellToken(int indexX, int indexZ) => CellToken(0, indexX, indexZ);

        /// <summary>
        /// 带图层编号的标记：分层体积重生成某一层的脏格时，
        /// 同一格里别的图层的物件必须原地不动。
        /// </summary>
        public static string CellToken(int layerIndex, int indexX, int indexZ) =>
            $"_l{layerIndex}_x{indexX}z{indexZ}_";

        /// <summary>没有分区时用的标记：一个隐式格子。</summary>
        public const string WholeRunToken = "_x0z0_";

        /// <summary>单个生成物件的完整名字：前缀、资产标签、它所在的格子、它的点下标。</summary>
        public static string ActorName(string label, int pointIndex, int indexX, int indexZ) =>
            ActorName(label, pointIndex, 0, indexX, indexZ);

        /// <summary>某一层单个生成物件的完整名字：前缀、标签、它的图层与格子、它的点下标。</summary>
        public static string ActorName(string label, int pointIndex, int layerIndex, int indexX, int indexZ) =>
            GeneratedPrefix + label + CellToken(layerIndex, indexX, indexZ) + pointIndex;

        /// <summary>该名字是否为给定格子产生。</summary>
        public static bool BelongsToCell(string name, int indexX, int indexZ) =>
            BelongsToCell(name, 0, indexX, indexZ);

        /// <summary>该名字是否为给定图层与格子产生。</summary>
        /// <remarks>
        /// 按**整个标记**匹配（下划线也算在内）：光写 "x1z2" 会被更长的下标误命中，
        /// 而不带图层的标记会让某一层的清理毁掉另一层放在同一格的
        /// 物件。
        /// </remarks>
        public static bool BelongsToCell(string name, int layerIndex, int indexX, int indexZ)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            return name.Contains(CellToken(layerIndex, indexX, indexZ), System.StringComparison.Ordinal);
        }

        /// <summary>生成名字所指的格子；完全不带标记时为 null。</summary>
        public static bool TryReadCell(string name, out int indexX, out int indexZ)
        {
            indexX = 0;
            indexZ = 0;

            if (string.IsNullOrEmpty(name))
                return false;

            int at = name.IndexOf("_x", System.StringComparison.Ordinal);
            if (at < 0)
                return false;

            int zSep = name.IndexOf('z', at + 2);
            int end = name.IndexOf('_', zSep + 1);
            if (zSep < 0 || end < 0)
                return false;

            bool xOk = int.TryParse(name.Substring(at + 2, zSep - at - 2), out indexX);
            bool zOk = int.TryParse(name.Substring(zSep + 1, end - zSep - 1), out indexZ);

            return xOk && zOk;
        }

        /// <summary>某个 Actor 名字是否标记它是 PCG 生成的。</summary>
        public static bool IsGeneratedName(string name)
        {
            return !string.IsNullOrEmpty(name) &&
                   name.StartsWith(GeneratedPrefix, System.StringComparison.Ordinal);
        }
    }
}
