// PCG 插件 - 子图节点
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §5.1.4。

using System;
using FlaxEngine;
using PCG.Execution;
using PCG.Graph;

namespace PCG.Nodes
{
    /// <summary>
    /// 跑另一张图，并把它的产出交给本图的其余部分。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 子图是"让一套撒点模式可复用"的做法：山上落石那张图只编一次，
    /// 每个地貌图都引它，而不是各抄一份然后各自漂移。子图运行时拿的是父图的生成范围、
    /// 世界服务与取消令牌，所以它不会活过启动它的那一次运行；种子流则是它自己的，
    /// 于是同一张子图在两个不同的父图里，
    /// 只有种子真的对上时才会产出同一套布局。
    /// </para>
    /// <para>
    /// 跨文件的环（A 引 B、B 又引 A）单张图的编译器看不见，
    /// 所以由 <see cref="PCGSubgraphInspector"/> 在运行之前拦下。这里这道深度上限是最后一道防线，
    /// 防的是加载器把检查器从没见过的图交回来那种情况：递归被**显式**限住，
    /// 而不是听任它把栈撑爆——栈一爆，在编辑器里就把整个进程一起带走了。
    /// </para>
    /// </remarks>
    public sealed class PCGSubgraphNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "Subgraph";

        /// <summary>运行期强制的嵌套上限，与编译期那次遍历各自独立。</summary>
        public const int MaxRuntimeDepth = 16;

        // 参数下标。顺序是存档图格式的一部分，只能往后追加。
        private const int PChildGraph = 0;
        private const int PMergeSinks = 1;
        private const int PSeedOffset = 2;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 3;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>要运行的图文件名。</summary>
        public string ChildGraph;

        /// <summary>
        /// 置位时子图的每个汇都把自己的点交出来；否则只交第一个。
        /// </summary>
        public bool MergeSinks;

        /// <summary>加在父图的种子上，让两个跑同一张子图的子图节点不必想到一起。</summary>
        public int SeedOffset;

        /// <summary>用序列化参数构造节点。</summary>
        public PCGSubgraphNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            ChildGraph = _params.Get(PChildGraph, (string)null);
            MergeSinks = _params.Get(PMergeSinks, false);
            SeedOffset = _params.Get(PSeedOffset, 0);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Sub-graph";

        /// <inheritdoc />
        /// <remarks>
        /// 跑的是嵌套执行器，它可能创建 Actor：那些只能主线程做。
        /// </remarks>
        public override bool IsThreadSafe => false;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => Array.Empty<PCGPinDefinition>();

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output, tooltip: "The child graph's output points."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"child={ChildGraph}, mergeSinks={MergeSinks}, seedOffset={SeedOffset}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            if (string.IsNullOrWhiteSpace(ChildGraph))
            {
                LastError = "Sub-graph node has no child graph name.";
                return PCGData.Empty;
            }

            if (context?.SubgraphLoader == null)
            {
                LastError = "No sub-graph loader is available for this run.";
                return PCGData.Empty;
            }

            if (context.SubgraphDepth >= MaxRuntimeDepth)
            {
                LastError = $"Sub-graph nesting passes the runtime limit of {MaxRuntimeDepth} levels at '{ChildGraph}'; " +
                            "stopping there rather than overflowing the stack.";
                return PCGData.Empty;
            }

            PCGGraph child;
            try
            {
                child = context.SubgraphLoader.Invoke(ChildGraph);
            }
            catch (Exception ex)
            {
                LastError = $"Sub-graph '{ChildGraph}' could not be loaded: {ex.Message}";
                return PCGData.Empty;
            }

            if (child == null)
            {
                LastError = $"Sub-graph '{ChildGraph}' was not found.";
                return PCGData.Empty;
            }

            if (!PCGGraphCompiler.TryCompile(child, out var compiled))
            {
                LastError = $"Sub-graph '{ChildGraph}' failed to compile: {FirstError(compiled)}";
                return PCGData.Empty;
            }

            var childContext = new PCGExecutionContext
            {
                Seed = context.Seed + SeedOffset,
                GenerationBounds = context.GenerationBounds,
                World = context.World,
                CancelToken = context.CancelToken,
                DebugMode = context.DebugMode,
                Source = context.Source,
                SubgraphLoader = context.SubgraphLoader,
                SubgraphDepth = context.SubgraphDepth + 1,
                GraphName = ChildGraph,

                // 一切"标识或约束这次运行"的东西都随子图一起走：少了层号，子图里生成的物件会被当成第 0 层的；
                // 少了世界锚点，子图里的网格采样器就丢了定位依据；少了 DryRun，预览会从子图里建出物件；
                // 少了裁剪（格子与体积盒），子图里的生成器就不认边界——分区运行会把 padding 里的物件也建一遍、
                // 整块运行会把内容落到体积外面去。这两条都是"看着跑通了"的那一类：点数与件数都不报错。
                LayerIndex = context.LayerIndex,
                SourceBounds = context.SourceBounds,
                ClipCell = context.ClipCell,
                ClipRegion = context.ClipRegion,
                DryRun = context.DryRun,
                Inspection = context.Inspection,
            };

            PCGExecutionResult result;
            try
            {
                result = new PCGGraphExecutor(compiled).Execute(childContext);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LastError = $"Sub-graph '{ChildGraph}' failed: {ex.Message}";
                return PCGData.Empty;
            }

            if (result.Cancelled)
            {
                LastError = $"Sub-graph '{ChildGraph}' was cancelled.";
                return PCGData.Empty;
            }

            // 子图的问题对父图的运行是隐形的，除非一路带上来：嵌套执行器的结果
            // 在这儿被丢掉，于是它的提醒也跟着消失，设计者只会看到一片空场景，
            // 而没有任何线索指向闯祸的那张图。
            if (result.Warnings != null && result.Warnings.Length > 0)
                LastError = string.Join("; ", result.Warnings);

            // 节点 id 0 是子图自己的空间；父图的 CurrentNodeId 到不了那里，所以无论子图被引到
            // 哪一处，子图的随机流都是稳定的。
            var outputs = result.Outputs;

            if (outputs == null || outputs.Length == 0)
                return PCGData.Empty;

            if (outputs.Length == 1 || !MergeSinks)
                return outputs[0] ?? PCGData.Empty;

            var merged = PCGData.Merge(outputs, out string mergeError);
            if (mergeError != null)
            {
                // 子图各个汇对同一个属性的类型不一致。回退到第一个输出，
                // 而不是让一个属性就把整张父图的运行判死。
                LastError = $"Sub-graph '{ChildGraph}': {mergeError} Using the first output only.";
                return outputs[0] ?? PCGData.Empty;
            }

            return merged;
        }

        private static string FirstError(PCGCompiledGraph compiled)
        {
            if (compiled?.Diagnostics == null)
                return "no diagnostics were produced";

            foreach (var diagnostic in compiled.Diagnostics)
            {
                if (diagnostic.Severity == PCGCompileSeverity.Error)
                    return diagnostic.Message;
            }

            return "no error diagnostic was reported";
        }
    }
}
