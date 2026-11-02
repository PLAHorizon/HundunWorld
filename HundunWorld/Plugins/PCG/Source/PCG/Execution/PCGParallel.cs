// // PCG 插件 - 节点内采样用的有界并行循环
// // 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §4.4 与 §10（RayCast 线程安全）。

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PCG.Execution
{
    /// <summary>
    /// 把某个节点自己的点循环摊到多个工作线程上，只给“逐点工作互不相干”的节点用。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么有它、它不是什么。</b>执行器始终留在场景线程上，因为生成器
    /// 要建 Actor，而建 Actor 不是线程安全的。文档里那条并行点因此
    /// 落在标了 <c>IsThreadSafe</c> 的节点内部：那些“点 <c>i</c> 的输出
    /// 只取决于 <c>i</c>”的采样器与过滤器——这恰恰是按下标取随机流
    /// 要给出的那条性质。它不是通用图调度器，也不试图让两个节点重叠；
    /// 重叠会让节点的输入缓存变成共享可变结构，而比起这个循环换不来额外收益。
    /// </para>
    /// <para>
    /// <b>结果逐位相同，而且是构造出来的。</b>分块是连续的、每个工作线程只写自己那一段，
    /// 而随机数按 <c>(种子, 节点 id, 点下标)</c> 取，不是从一个共享序列里拉——
    /// 于是并行跑出来的点与串行**同序同值**，
    /// 这一点由 harness 直接断言，而不是靠这段推理让人相信。
    /// </para>
    /// <para>
    /// <b>量小就留在调用线程上。</b>低于 <see cref="MinItemsPerWorker"/> 时循环内联跑，
    /// 因为把几百个点交给线程池的代价比省下来的还多，而且拖动预览
    /// 必须保持跟手。
    /// </para>
    /// </remarks>
    public static class PCGParallel
    {
        /// <summary>
        /// 总开关。关掉意味着所有节点循环都内联跑——也就是在有它之前
        /// “这个插件是单线程的”那句话的意思。
        /// </summary>
        public static bool Enabled = true;

        /// <summary>
        /// 一个工作线程至少要摊到多少元素，循环才会真的拆开。
        /// </summary>
        /// <remarks>
        /// 这个数是从基准里量出来的、不是拍的：逐元素的工作只是几次浮点运算，
        /// 所以一个工作线程要拿到上千个，调度开销才不再占主导。
        /// </remarks>
        public static int MinItemsPerWorker = 4096;

        /// <summary>工作线程数上限，免得一台已被编辑器占满的机器还被要求每核开一个。</summary>
        public static int MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount - 1);

        /// <summary>这个长度的循环在当前设置下会不会被拆开。</summary>
        public static bool WillParallelise(int count) =>
            Enabled && count >= MinItemsPerWorker * 2 && MaxDegreeOfParallelism > 1;

        /// <summary>
        /// 对 <c>[0, count)</c> 里的每个下标跑一遍 <paramref name="body"/>。
        /// </summary>
        /// <param name="count">元素个数。</param>
        /// <param name="body">单个元素的工作。只许碰它自己那个槽位。</param>
        /// <exception cref="OperationCanceledException"><paramref name="body"/> 取消时抛出，无论在哪个工作线程上跑的。</exception>
        /// <remarks>
        /// 异常会被拆到“body 真正抛出的第一个”，因为执行器专门捕获
        /// <c>OperationCanceledException</c> 与 <c>PCGNodeExecutionException</c>，而
        /// <c>Parallel.For</c> 会把两者都裹进一个 <c>AggregateException</c>——那样一个都匹配不上，
        /// 于是一次被取消的烘焙会看起来像节点崩了，而节点崩了会看起来像
        /// <c>PCGVolume.Generate</c> 顶层冒出来的意外错误。
        /// </remarks>
        public static void For(int count, Action<int> body)
        {
            if (body == null)
                throw new ArgumentNullException(nameof(body));

            if (count <= 0)
                return;

            if (!WillParallelise(count))
            {
                for (int i = 0; i < count; i++)
                    body(i);
                return;
            }

            int workers = Math.Min(MaxDegreeOfParallelism, count / MinItemsPerWorker);
            if (workers < 2)
            {
                for (int i = 0; i < count; i++)
                    body(i);
                return;
            }

            // // 区间连续，于是同一工作线程的元素在内存里、也在随机流里都是相邻的。
            int chunk = (count + workers - 1) / workers;

            try
            {
                Parallel.For(0, workers, worker =>
                {
                    int start = worker * chunk;
                    int end = Math.Min(start + chunk, count);

                    for (int i = start; i < end; i++)
                        body(i);
                });
            }
            catch (AggregateException aggregate)
            {
                throw Flatten(aggregate);
            }
        }

        /// <summary>循环体真正抛出的那个异常；取消优先于其它任何异常。</summary>
        private static Exception Flatten(AggregateException aggregate)
        {
            var inner = new List<Exception>();
            aggregate.Flatten().Handle(ex =>
            {
                inner.Add(ex);
                return true;
            });

            if (inner.Count == 0)
                return aggregate;

            for (int i = 0; i < inner.Count; i++)
            {
                if (inner[i] is OperationCanceledException)
                    return inner[i];
            }

            return inner[0];
        }
    }
}
