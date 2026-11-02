// PCG 插件 - 场景 Actor 作为图输入
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.32。

using System;
using System.Collections.Generic;
using FlaxEngine;

namespace PCG.Execution
{
    /// <summary>
    /// 一个场景 Actor 能为图提供什么：它在哪里，以及 Actor 身上那三样设计师
    /// 真能叫出名字的元数据。
    /// </summary>
    /// <remarks>
    /// 刻意做得小。Flax 把 Actor 的 <c>Tag</c> 与 <c>LayerName</c> 暴露成字符串、名字也是
    /// 字符串，这些都能在编辑器里写、在运行期比较；而项目级的 <c>Tag</c> 枚举**无法**由任意字符串
    /// 现场造出来，所以任何以它为键的东西在图里都够不着。
    /// 按反射读任意脚本字段考虑过、也放弃了：它慢，字段一改名就会静默失效，
    /// 而且会让一张图依赖上一个图里没有任何地方声明过的类。
    /// </remarks>
    public struct PCGActorInfo
    {
        /// <summary>Actor 的 id，只要这个 Actor 还在就不变。</summary>
        public Guid Id;

        /// <summary>Actor 的名字，也就是设计师在大纲里敲的那个。</summary>
        public string Name;

        /// <summary>Actor 自己的字符串 Tag，没有则为空。</summary>
        public string Tag;

        /// <summary>Actor 所在的碰撞层名字，没有则为空。</summary>
        public string LayerName;

        /// <summary>世界位置。</summary>
        public Vector3 Position;

        /// <summary>世界旋转。</summary>
        public Quaternion Rotation;

        /// <summary>世界缩放。</summary>
        public Vector3 Scale;
    }

    /// <summary>
    /// 读宿主给的一份场景 Actor 清单，契约与流式来源用的那条一样。
    /// </summary>
    /// <remarks>
    /// 来源写进调用方拥有的列表并返回写了多少条，于是逐格调用不分配任何东西；
    /// 而声称的条数大于实际写入时，会按列表里真正有的量钳掉，因为信那个返回值
    /// 会读到列表上一次调用残留的内容——这正是观察者来源与分块来源都在防的
    /// 那个缺陷。
    /// </remarks>
    public static class PCGActors
    {
        /// <summary>
        /// 把一个 Actor 生成到场景里并挂到 <paramref name="parent"/> 下；**成功返回 true**。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 包一层只为一件事：引擎的 `Level.SpawnActor` 返回值是反的。它自己的文档写着
        /// "True if action cannot be done, otherwise false"——**返回 true 表示失败**。
        /// 插件里原先五处调用都当成"true 是成功"，于是每个生成器都在**成功之后**报
        /// "failed to spawn …" 并把自己刚创建的 Actor 丢掉；有一个还把它跟 null 比（那个表达式恒为真）。
        /// 结果整齐地表现为"图跑得出几百个点，场景里 0 个东西"。
        /// </para>
        /// <para>
        /// 反直觉的约定只在这里翻一次，调用方读到的就是人话。各处不要再直接调 `Level.SpawnActor`。
        /// </para>
        /// </remarks>
        public static bool TrySpawn(Actor actor, Actor parent)
        {
            if (actor == null)
                return false;

            return !Level.SpawnActor(actor, parent);
        }

        /// <summary>
        /// 用宿主来源填 <paramref name="into"/>，并把声明的条数**两端**都钳一遍。
        /// </summary>
        /// <param name="source">写进 <paramref name="scratch"/> 并返回写了多少条。</param>
        /// <param name="scratch">来源写入用的工作列表。</param>
        /// <param name="into">接收这些 Actor。</param>
        /// <returns>写入了多少个 Actor。</returns>
        public static int FromSource(Func<List<PCGActorInfo>, int> source, List<PCGActorInfo> scratch,
            List<PCGActorInfo> into)
        {
            if (scratch == null)
                throw new ArgumentNullException(nameof(scratch));
            if (into == null)
                throw new ArgumentNullException(nameof(into));

            into.Clear();

            if (source == null)
                return 0;

            scratch.Clear();
            int claimed = source(scratch);

            // 上下两端都要钳：一个正在被写入的来源完全可能返回 -1 或一个陈旧的条数，
            // 而这两种都不该被读成"越过末尾去读"或"什么都不产出"。
            int count = Math.Min(Math.Max(claimed, 0), scratch.Count);

            if (count < scratch.Count)
                scratch.RemoveRange(count, scratch.Count - count);

            for (int i = 0; i < count; i++)
                into.Add(scratch[i]);

            return into.Count;
        }

        /// <summary>Actor 是否匹配设计师在节点上设的那些过滤条件。</summary>
        /// <remarks>
        /// 纯函数、且与读那一步分开，于是匹配规则不必有场景就能断言：其中要紧的两条是——空过滤条件表示
        /// **无约束**而不是"匹配空字符串"，以及名字是子串匹配而 Tag 与层是精确匹配（按 Tag 给 Actor
        /// 分组是常见用法，那里用子串会顺带拉进一个仅仅
        /// 开头相同的 Tag）。
        /// </remarks>
        public static bool Matches(
            in PCGActorInfo actor, string nameContains, string tag, string layerName, bool skipGenerated)
        {
            if (skipGenerated && PCGSpawnNaming.IsGeneratedName(actor.Name))
                return false;

            if (!string.IsNullOrEmpty(nameContains) &&
                (actor.Name == null ||
                 actor.Name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) < 0))
                return false;

            if (!string.IsNullOrEmpty(tag) && !string.Equals(actor.Tag, tag, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(layerName) &&
                !string.Equals(actor.LayerName, layerName, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }
    }
}
