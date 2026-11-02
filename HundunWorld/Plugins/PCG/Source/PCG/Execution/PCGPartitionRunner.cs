// PCG 插件 - 分区生成运行器
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §7.3。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Graph;

namespace PCG.Execution
{
    /// <summary>
    /// 运行器要在两次运行之间复用格子所需要的东西。
    /// </summary>
    public readonly struct PCGPartitionCacheRequest
    {
        /// <summary>要读写的缓存。为 null 就整体关掉复用。</summary>
        public readonly PCGPartitionCache Cache;

        /// <summary>已编译图的结构哈希。</summary>
        public readonly int StructuralHash;

        /// <summary>图的已暴露参数发生变化时递增，好让覆盖值不被忽略。</summary>
        public readonly int ParameterVersion;

        /// <summary>
        /// 调用方维护的版本号，管的是图看不见的那些输入：一次地形编辑、一个被挪动的宿主 actor。
        /// 插件观察不到那些，所以体积把这个旋钮交出去，而不是假装它知道。
        /// </summary>
        public readonly int WorldVersion;

        /// <summary>这次运行规划器真正施加的 padding，它是每个格子所见内容的一部分。</summary>
        public readonly float Padding;

        public PCGPartitionCacheRequest(
            PCGPartitionCache cache, int structuralHash, int parameterVersion, int worldVersion, float padding)
        {
            Cache = cache;
            StructuralHash = structuralHash;
            ParameterVersion = parameterVersion;
            WorldVersion = worldVersion;
            Padding = padding;
        }

        /// <summary>复用打开时为真。</summary>
        public bool IsEnabled => Cache != null;

        internal PCGCellSignature SignatureFor(in PCGPartitionCell cell, int seed) =>
            new PCGCellSignature(StructuralHash, seed, ParameterVersion, WorldVersion, cell, Padding);
    }

    /// <summary>
    /// 把一张已编译的图按分区格子每格跑一次，再把结果缝成一份产出。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 每个格子在自己的带 padding 的区域上跑图，于是依赖邻域的那些阶段——
    /// 尤其是自剪枝过滤器——能看见属于相邻格子的那些点，然后只保留格子
    /// 内部的点。padding 存在的全部理由就在这儿：没有它，每条格子边界都会是一条
    /// 道具消失或成双的线，而在流式世界里那是玩家第一个注意到的东西。
    /// </para>
    /// <para>
    /// seed 是跨格子共享的，而不是按格子推导的，因为插件的随机流
    /// 以 <c>(seed, 节点 id, 点下标)</c> 为键：按格子的 seed 会让一个格子的布局取决于它
    /// 是哪个格子，于是格子之间会看得出风格差异。真正按格子变化的是点的下标编号，
    /// 也就是记在 <see cref="PCGPartitionPlanner"/> 上的那条注意事项。
    /// </para>
    /// <para>
    /// 一个执行器被所有格子复用。因此图只编译一次，每格的开销只剩
    /// 求值本身；而由于执行器在每次运行开头清自己的缓存，一个格子永远读不到
    /// 邻居格子的缓存数据——格子之间的独立是构造出来的，不是碰巧。
    /// </para>
    /// <para>
    /// <b>格子级复用</b>叠在同一趟循环上：<see cref="PCGCellSignature"/> 仍然
    /// 匹配的格子交出它存下的点，根本不求值。它的正当性和 padding 是同一个理由——
    /// 一个格子读的是自己那块带 padding 的区域，而不是邻居的输出。
    /// 两种"宁可不复用也不猜"的形状见 <see cref="PCGPartitionCache"/>。
    /// </para>
    /// </remarks>
    public static class PCGPartitionRunner
    {
        /// <summary>
        /// 每格执行一次运行。
        /// </summary>
        /// <param name="executor">只建一次的执行器，带着本次运行的参数计划，按格复用。</param>
        /// <param name="baseContext">持有 seed、世界服务、取消令牌与加载器的上下文。</param>
        /// <param name="cells">来自 <see cref="PCGPartitionPlanner.Plan"/> 的格子。</param>
        /// <param name="maxTotalPoints">
        /// 合并产出的上限。执行器按每格运行施加图自己的预算，而对分区的生成
        /// 那会乘算成 格子数 x 预算，所以总量上限必须在这里查。
        /// 零或负数表示没有总量上限。
        /// </param>
        /// <param name="cacheRequest">可选的格子复用；见 <see cref="PCGPartitionCacheRequest"/>。</param>
        /// <returns>一份形状像单次运行的结果，带着合并后、裁剪过的点。</returns>
        public static PCGExecutionResult Run(
            PCGGraphExecutor executor,
            PCGExecutionContext baseContext,
            IReadOnlyList<PCGPartitionCell> cells,
            int maxTotalPoints = 0,
            PCGPartitionCacheRequest cacheRequest = default)
        {
            if (executor == null)
                throw new ArgumentNullException(nameof(executor));
            if (baseContext == null)
                throw new ArgumentNullException(nameof(baseContext));

            var result = new PCGExecutionResult();

            if (cells == null || cells.Count == 0)
            {
                result.Outputs = Array.Empty<PCGData>();
                result.OutputNodeIndices = Array.Empty<int>();
                result.Warnings = Array.Empty<string>();
                return result;
            }

            var warnings = new List<string>();
            var stats = new List<PCGNodeRunStats>();

            // 汇点下标 -> 该汇点裁剪后的点，按格子分别保存。按汇点拆分，才让一张
            // 有两个产出的图（比如一组石头加一组树）最后仍然出来是两份数据。
            List<PCGData>[] perSink = null;
            int[] outputNodeIndices = null;

            // 这次运行挣到的东西，要等它证明自己覆盖了所有本来要覆盖的格子之后才提交。
            var earned = new List<EarnedCell>();

            int kept = 0;
            int reusedCells = 0;
            int executedCells = 0;
            bool cancelled = false;
            bool truncated = false;
            double milliseconds = 0.0;

            for (int i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                var signature = cacheRequest.SignatureFor(cell, baseContext.Seed);

                PCGData[] parts;

                if (cacheRequest.IsEnabled && cacheRequest.Cache.TryGet(cell, signature, out parts))
                {
                    // 一个被复用的格子交出的正是它自己的运行上次交出的东西——已经裁剪
                    // 到格子内——所以下面的合并分不出复用与执行。这就是要点：
                    // 一次缓存的运行不许和完整的那次看上去略有不同。
                    reusedCells++;
                    outputNodeIndices = outputNodeIndices ?? cacheRequest.Cache.KnownOutputNodeIndices;
                }
                else
                {
                    var context = new PCGExecutionContext
                    {
                        Seed = baseContext.Seed,
                        GenerationBounds = cell.GenerationBounds,
                        ClipCell = cell,
                        World = baseContext.World,
                        CancelToken = baseContext.CancelToken,
                        DebugMode = baseContext.DebugMode,
                        Source = baseContext.Source,
                        SubgraphLoader = baseContext.SubgraphLoader,
                        GraphName = baseContext.GraphName,
                        // 递归深度要延续下去，否则嵌在子图里的一次分区运行会从头计数、
                        // 把递归上限废掉。
                        SubgraphDepth = baseContext.SubgraphDepth,

                        // 上下文的其余部分也一并带过去，而这些里每一个在不带的时候都是一处静默缺陷：
                        // 层索引是每个生成 actor 名字的一部分（丢掉，各层会互相覆盖彼此的道具）、
                        // SourceBounds 是节点的世界锚点（丢掉，网格采样器会按格子重新居中自己）、
                        // DryRun 是预览兑现承诺的方式（丢掉，一次被预览的分区生成会真的建 actor），
                        // 而检视器正是一次运行向人解释自己的途径（丢掉，一次分区运行
                        // 就什么都报不出来）。
                        LayerIndex = baseContext.LayerIndex,
                        SourceBounds = baseContext.SourceBounds,

                        // 体积盒也随行：格子优先，但"这次运行的存活区域"在格子之外还该有个值，
                        // 否则子图/嵌套的运行会看到 null，等于整块与分区两套规则。
                        ClipRegion = baseContext.ClipRegion,
                        DryRun = baseContext.DryRun,
                        Inspection = baseContext.Inspection,
                    };

                    PCGExecutionResult cellResult;
                    try
                    {
                        cellResult = executor.Execute(context);
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        // 一个坏格子不该把已经成功的格子一起扔掉；调用方看得见是哪些。
                        // 失败的格子从不被存下，于是一次后来的运行会再试它一次。
                        warnings.Add($"{cell.Label}: {ex.GetType().Name}: {ex.Message}");
                        continue;
                    }

                    if (cellResult.Cancelled)
                    {
                        cancelled = true;
                        break;
                    }

                    executedCells++;
                    milliseconds += cellResult.Milliseconds;

                    if (cellResult.Warnings != null)
                    {
                        for (int w = 0; w < cellResult.Warnings.Length; w++)
                            warnings.Add($"{cell.Label}: {cellResult.Warnings[w]}");
                    }

                    if (cellResult.Stats != null)
                        stats.AddRange(cellResult.Stats);

                    parts = ClipOutputs(cellResult.Outputs, cell, context);

                    // 汇点集合对每个格子都一样——那是图的拓扑而不是它的数据——所以
                    // 第一个被执行的格子就描述了合并后的结果。
                    outputNodeIndices = outputNodeIndices ?? cellResult.OutputNodeIndices ?? Array.Empty<int>();

                    if (parts != null && parts.Length > 0)
                    {
                        earned.Add(new EarnedCell
                        {
                            Cell = cell,
                            Signature = signature,
                            Parts = parts,
                            OutputNodeIndices = cellResult.OutputNodeIndices,
                        });
                    }
                }

                if (parts == null || parts.Length == 0)
                    continue;

                if (perSink == null)
                {
                    perSink = new List<PCGData>[parts.Length];
                    for (int s = 0; s < perSink.Length; s++)
                        perSink[s] = new List<PCGData>();
                }

                int sinkCount = Math.Min(parts.Length, perSink.Length);
                for (int o = 0; o < sinkCount; o++)
                {
                    var clipped = parts[o];
                    if (clipped == null || clipped.Count == 0)
                        continue;

                    if (maxTotalPoints > 0 && kept + clipped.Count > maxTotalPoints)
                    {
                        // 上限要真的守住，而不是只报告它：一次分区的运行整体超出一整个
                        // 格子的预算不叫上限，而那会是场景自己来告诉你。这个格子超出的
                        // 那部分被丢掉，这和单次运行做的是同一刀裁剪。
                        int room = maxTotalPoints - kept;
                        if (room > 0)
                        {
                            var indices = new int[room];
                            for (int p = 0; p < room; p++)
                                indices[p] = p;

                            perSink[o].Add(PCGData.Gather(clipped, indices, room));
                            kept += room;
                        }

                        truncated = true;
                        break;
                    }

                    kept += clipped.Count;
                    perSink[o].Add(clipped);
                }

                if (truncated)
                {
                    warnings.Add(
                        $"Partitioned generation reached the combined point ceiling of {maxTotalPoints}; " +
                        "points and cells past it were dropped. Raise the graph's point budget, use larger " +
                        "cells, or shrink the volume.");
                    break;
                }
            }

            if (cacheRequest.IsEnabled)
            {
                if (truncated)
                {
                    // 一次在上限处停下的运行，对它没走到的那些格子什么都没说，所以把
                    // 它走到的那些存下来，会让后一次运行把上限前后两种结果混在一起。
                    cacheRequest.Cache.InvalidateAll();
                    cacheRequest.Cache.LastRefusalReason =
                        "a partitioned run hit its combined point ceiling, so no cell results are trusted";
                }
                else if (!cancelled)
                {
                    for (int i = 0; i < earned.Count; i++)
                        cacheRequest.Cache.Store(earned[i].Cell, earned[i].Signature, earned[i].Parts,
                            earned[i].OutputNodeIndices);
                }

                cacheRequest.Cache.LastRunReusedCells = reusedCells;
                cacheRequest.Cache.LastRunExecutedCells = executedCells;
            }

            if (perSink == null)
            {
                result.Outputs = Array.Empty<PCGData>();
                result.OutputNodeIndices = Array.Empty<int>();
            }
            else
            {
                var merged = new PCGData[perSink.Length];
                for (int o = 0; o < perSink.Length; o++)
                {
                    var data = PCGData.Merge(perSink[o].ToArray(), out string mergeError);
                    if (mergeError != null)
                        warnings.Add($"cell merge for output {o}: {mergeError}");

                    merged[o] = data ?? PCGData.Empty;
                }

                result.Outputs = merged;
                result.OutputNodeIndices = outputNodeIndices;
                result.TotalPointsProduced = kept;
            }

            result.Milliseconds = milliseconds;
            result.Cancelled = cancelled;
            result.Stats = stats.ToArray();
            result.Warnings = warnings.ToArray();

            return result;
        }

        /// <summary>一个已执行的格子，要等运行证明自己走到了结尾才被存下。</summary>
        private sealed class EarnedCell
        {
            internal PCGPartitionCell Cell;
            internal PCGCellSignature Signature;
            internal PCGData[] Parts;
            internal int[] OutputNodeIndices;
        }

        /// <summary>把一个格子产出的每个汇点裁剪到那个格子自己的范围。</summary>
        private static PCGData[] ClipOutputs(PCGData[] outputs, in PCGPartitionCell cell, PCGExecutionContext context)
        {
            if (outputs == null || outputs.Length == 0)
                return null;

            var parts = new PCGData[outputs.Length];
            for (int o = 0; o < outputs.Length; o++)
                parts[o] = ClipToCell(outputs[o], cell, context);

            return parts;
        }

        /// <summary>
        /// 保留一个格子的产出中真正属于该格子自身的那些点。
        /// </summary>
        /// <remarks>
        /// 用的是这次运行里生成器所依据的同一个判定
        /// （<see cref="PCGExecutionContext.IsInsideClip"/>），于是一个变成场景里 actor 的点与一个
        /// 留在返回数据里的点是被同一条规则选出的。padding 的点在这里被丢掉，而不是
        /// 交给下游某个节点去过滤——那样那个节点也得被告知分区的存在。
        /// </remarks>
        private static PCGData ClipToCell(PCGData data, in PCGPartitionCell cell, PCGExecutionContext context)
        {
            if (data == null || data.IsEmpty)
                return null;

            var points = data.Points;
            var indices = new int[points.Length];
            int count = 0;

            for (int p = 0; p < points.Length; p++)
            {
                if (!context.IsInsideClip(points[p].Position))
                    continue;

                indices[count++] = p;
            }

            if (count == 0)
                return null;

            // 一个每个点都存活的格子仍然要拿到一份拷贝：这个格子的数据集归执行器的
            // 缓存所有、并为下一次运行被清空，所以交回原件会让它悬空。
            return PCGData.Gather(data, indices, count);
        }
    }
}
