// // PCG 插件 - 图的效果分类
// // 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §6.2。

using System;
using System.Collections.Generic;
using PCG.Graph;
using PCG.Nodes;

namespace PCG.Execution
{
    /// <summary>
    /// 判定“跑一张图会不会把物件放进世界里”——也就是“我能看的结果”与
    /// “我收不回来的副作用”之间的区别。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 插件里大量是只读阶段——采样、裁剪、过滤、变换——只有一小撮会写：
    /// 各生成器，以及**能够抵达生成器**的东西（子图节点的内容这份名单看不见）。
    /// 只要一张图是在体积自己的“生成/清空”之外被跑起来的，这条区分就吃重：
    /// 那种场合下没人拥有被造出来的物件，也没有哪个 <c>Clear()</c> 能把它们
    /// 收回去。
    /// </para>
    /// <para>
    /// 不认识的类型按“会写”算。这份名单是**小且危险**的那一侧，所以“漏登记一个节点”的失效方式
    /// 是“干跑被拒”，而不是“没人要求填充的场景里冒出一堆 Actor”——前者设计师能绕开，
    /// 后者绕不开。离屏测试把这份名单与注册表对钉，于是“新写了一个生成器却没登记”
    /// 会把一条断言打红，而不是被静悄悄地
    /// 归进“安全”那一类。
    /// </para>
    /// </remarks>
    public static class PCGGraphEffects
    {
        private static readonly HashSet<string> SceneWritingIds = new HashSet<string>(StringComparer.Ordinal)
        {
            PCGSpawnStaticModelNode.Id,
            PCGSpawnFoliageNode.Id,
            PCGSpawnActorNode.Id,
            PCGSpawnDecalNode.Id,
            PCGSpawnSplineMeshNode.Id,
            PCGSpawnGeometryNode.Id,

            // // 子图在有东西把它解开之前是不透明的：见 GraphWritesScene(PCGGraph, ...)。
            PCGSubgraphNode.Id,
        };

        /// <summary>
        /// 这个类型往世界里放东西时为真；类型根本没注册、因而无法检查它的行为时，
        /// 也为真。
        /// </summary>
        public static bool IsSceneWriting(string typeId)
        {
            if (string.IsNullOrEmpty(typeId))
                return true;

            if (!PCGNodeRegistry.Contains(typeId))
                return true;

            return SceneWritingIds.Contains(typeId);
        }

        /// <summary>编译后的图里有任何一个节点写场景时为真。</summary>
        public static bool GraphWritesScene(PCGNodeBase[] nodes)
        {
            if (nodes == null)
                return true;

            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                if (node != null && IsSceneWriting(node.TypeId))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 一张图——或它沿子图引用能抵达的任何东西——写场景时为真。
        /// </summary>
        /// <remarks>
        /// 在 <see cref="GraphWritesScene(PCGNodeBase[])"/> 里子图节点按“会写”算，因为光看那个节点
        /// 说明不了它的内容。这个重载会真的去看，用的还是体积在运行时用的那一类加载器，
        /// 于是“由纯数据子图拼出来的图”能被放行，而不是因为用了这个特性
        /// 就被一律拒掉。
        /// <para>
        /// 走查证明不了的一律按“会写”算：没有加载器、名字解析不出来、子图回来是空的、
        /// 引用又指回它已经在里面的那条链（环没有有限的内容可查）、
        /// 或者超过了它肯往下走的深度。这些拒绝在**要紧的那个方向**上是安全的——
        /// 这里答错的代价是“场景里多出一批谁都不会清理的 Actor”。
        /// </para>
        /// </remarks>
        /// <param name="graph">要检查的图。</param>
        /// <param name="loader">把子图名字解析成图；解析不了时返回 null。</param>
        /// <param name="maxDepth">往下看几层子图；更深的拒绝而不是假定。</param>
        public static bool GraphWritesScene(
            PCGGraph graph, Func<string, PCGGraph> loader, int maxDepth = DefaultSubgraphCheckDepth)
        {
            if (graph == null)
                return true;

            return WalksSceneWriting(graph, loader, null, 0, maxDepth);
        }

        /// <summary>子图走查在“拒绝猜”之前往下看多深。</summary>
        public const int DefaultSubgraphCheckDepth = 4;

        private static bool WalksSceneWriting(
            PCGGraph graph, Func<string, PCGGraph> loader, List<string> chain, int depth, int maxDepth)
        {
            var nodes = graph.Nodes;
            if (nodes == null)
                return true;

            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                if (node == null || !IsSceneWriting(node.TypeId))
                    continue;

                if (node.TypeId != PCGSubgraphNode.Id)
                    return true;

                if (loader == null || depth >= maxDepth)
                    return true;

                string name = PCGSubgraphInspector.ChildNameOf(node);
                if (string.IsNullOrEmpty(name))
                    return true;

                if (chain != null && chain.Contains(name))
                    return true;

                var child = loader(name);
                if (child == null)
                    return true;

                var childChain = chain == null ? new List<string>() : new List<string>(chain);
                childChain.Add(name);

                if (WalksSceneWriting(child, loader, childChain, depth + 1, maxDepth))
                    return true;
            }

            return false;
        }

        /// <summary>每一个“名字里自称生成器”的已注册类型都在这份写场景名单上时为真。</summary>
        /// <remarks>
        /// 这是按命名约定拉的绊线，不是真能力标志，而且它老老实实承认这一点：
        /// 它抓得住真正会发生的那种（新注册了一个 <c>SpawnXxx</c> 节点却没写进名单），
        /// 抓不住“改了个名字的生成器”。测试里第二条断言靠**数名单条数**补上这一半，
        /// 于是从名单里删掉一个也会红。
        /// </remarks>
        public static bool EverySpawnerIsListed(out string missing)
        {
            missing = null;

            foreach (var info in PCGNodeRegistry.All)
            {
                if (info?.TypeId == null || !info.TypeId.StartsWith("Spawn", StringComparison.Ordinal))
                    continue;

                if (SceneWritingIds.Contains(info.TypeId))
                    continue;

                missing = (missing == null ? string.Empty : missing + ", ") + info.TypeId;
            }

            return missing == null;
        }

        /// <summary>这份名单把多少个节点类型算作“写场景”。</summary>
        public static int SceneWritingCount => SceneWritingIds.Count;
    }
}
