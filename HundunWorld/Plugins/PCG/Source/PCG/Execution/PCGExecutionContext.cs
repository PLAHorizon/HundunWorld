// PCG 插件 - 执行上下文与节点接口
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §4.2 与 §4.3。

using System;
using System.Threading;
using FlaxEngine;

namespace PCG.Execution
{
    /// <summary>一次图运行要求的调试输出级别。</summary>
    public enum PCGDebugMode
    {
        /// <summary>不输出调试信息。</summary>
        None = 0,

        /// <summary>画点的位置。</summary>
        Points = 1,

        /// <summary>画点的位置，按密度着色。</summary>
        PointsDensity = 2,

        /// <summary>画点的包围盒。</summary>
        Bounds = 3,

        /// <summary>能画的都画。</summary>
        Full = 4,
    }

    /// <summary>
    /// 一个节点从周围需要的一切：种子、生成范围、世界查询与取消。
    /// </summary>
    /// <remarks>
    /// 每次图执行创建一个实例，从工作线程读它是安全的。节点不得修改它。
    /// </remarks>
    /// <summary>
    /// 一个节点需要知道的、关于它身处的那一次运行的一切。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>在这里加一个字段是"两处"的改动。</b>有**两处**会构造子上下文——子图节点与分区运行器——没被复制过去的字段
    /// 不是"缺失"，而是**静默地错**：底下那次运行会当作那个设置还在默认值上。至今加过的每个字段都至少被忘过一次，
    /// 而且每次症状都不一样（预览却写了场景、网格采样器逐格重新定心、分层运行的各层互相覆盖对方的 Actor、
    /// 分区图在逐节点检视里什么都报不出来）。
    /// </para>
    /// <para>
    /// 所以加字段时的步骤是：在 <c>PCGSubgraphNode</c> 与 <c>PCGPartitionRunner.Run</c> 里各复制一次，并确保有东西
    /// 可观察地覆盖它——逐节点检视是那个能离线断言的东西。
    /// </para>
    /// </remarks>
    public sealed class PCGExecutionContext
    {
        /// <summary>图级随机种子。节点里的随机数都由它加上节点号与点号派生。</summary>
        public int Seed;

        /// <summary>图被允许生成的世界空间区域。</summary>
        public BoundingBox GenerationBounds;

        /// <summary>世界查询（地形、Actor 查找）。</summary>
        public PCGWorldContext World;

        /// <summary>协作式取消。长循环必须轮询它；见 <see cref="ThrowIfCancelled"/>。</summary>
        public CancellationToken CancelToken;

        /// <summary>要求的调试输出。</summary>
        public PCGDebugMode DebugMode;

        /// <summary>发起这次运行的 PCG 体积（如果来自体积）。节点可以读，不可以写。</summary>
        /// <summary>
        /// 整次生成的区域，在它被分区切成格子**之前**的样子。
        /// </summary>
        /// <remarks>
        /// 一个要在世界里**放置**东西（而不是在区域里采样）的节点，需要一个"同一张图逐格跑一次时不会跟着走"的锚点。
        /// <see cref="GenerationBounds"/> 恰恰是那个会走的，所以这里带上调用者完整的区域，于是也没人需要知道调用者是谁。
        /// 不是分区运行时为 null——那种情况下两个盒子本来就是同一个。
        /// </remarks>
        public BoundingBox? SourceBounds;

        /// <summary>请求这次生成的对象，供需要回溯到它的节点使用。</summary>
        public object Source;

        /// <summary>
        /// 可选的记录器：抓下每个节点产出了什么，供运行后检视。
        /// </summary>
        /// <remarks>
        /// 默认为 null：普通的一次生成每个节点只多付一次 null 判断、什么都不留，这正是"可以放心把记录器常挂在
        /// 只走编辑器路径上"的原因。
        /// </remarks>
        public PCGNodeInspection Inspection;

        /// <summary>
        /// 置位时，本来要写场景的节点改为把输入原样传下去。
        /// </summary>
        /// <remarks>
        /// 它才是让预览模式名副其实的东西：体积那边的注释写着"绝不能碰场景"，但此前没有任何东西在强制它——预览
        /// 图里的生成器照样建自己的 Actor。它同时也让一次运行可以纯粹当**判定**用：层级生成的粗层问"这里有没有
        /// 东西"而不把它建出来，只有细层才落笔。没有这个开关的话，问这一句的唯一办法是先把东西建出来再删掉，
        /// 那既更慢，也看得见。
        /// </remarks>
        public bool DryRun;

        /// <summary>
        /// 这份上下文的一份副本，什么都不建，供"只负责判定"的那一遍使用。
        /// </summary>
        /// <remarks>
        /// 用副本而不是改原对象：判定那一遍跑在建东西那一遍**内部**，把共享上下文上的标记翻掉会把建东西的那一遍
        /// 也变成判定的。这里逐字段抄一份，于是以后新加的字段会自动被带上——这是字段必须存在的**第三个地方**，
        /// 也是最容易忘的那个。
        /// </remarks>
        public PCGExecutionContext AsDryRun() => new PCGExecutionContext
        {
            Seed = Seed,
            GenerationBounds = GenerationBounds,
            ClipCell = ClipCell,
            ClipRegion = ClipRegion,
            World = World,
            CancelToken = CancelToken,
            DebugMode = DebugMode,
            Source = Source,
            SubgraphLoader = SubgraphLoader,
            GraphName = GraphName,
            SubgraphDepth = SubgraphDepth,
            CurrentNodeId = CurrentNodeId,
            LayerIndex = LayerIndex,
            SourceBounds = SourceBounds,
            Inspection = Inspection,
            DryRun = true,
        };

        /// <summary>当前正在执行的节点标识，供日志与诊断使用。</summary>
        public int CurrentNodeId;

        /// <summary>
        /// 按名字为子图节点加载子图。由发起运行的一侧设置（体积从磁盘解析、编辑器从打开着的缓冲解析、测试从字典
        /// 解析），于是节点永远不会去够全局状态，离屏执行也因此仍然可行。
        /// </summary>
        public Func<string, Graph.PCGGraph> SubgraphLoader;

        /// <summary>
        /// 正在跑的图的名字，用来对照引用链报告子图环。
        /// </summary>
        public string GraphName;

        /// <summary>
        /// 当前这次运行处在第几层子图。由 <c>PCGSubgraphNode</c> 维护，于是即便加载器交回了没人检查过的图，
        /// 递归也有一个硬上限。
        /// </summary>
        public int SubgraphDepth;

        /// <summary>
        /// 这次运行属于第几个生成层；没有分层的体积为 0。
        /// </summary>
        /// <remarks>
        /// 它是每个生成出来的物件名字的一部分，因为两层可以把物件放进同一个格子，而"按格清理"若忽略层号，
        /// 就会把邻层的物件销毁掉、同时让自己的旧物件留着。
        /// </remarks>
        public int LayerIndex;

        /// <summary>
        /// 这次运行是分区生成里的某一格时置位。往场景里加东西的节点（生成器）必须遵守它：图是在一个带 padding
        /// 的区域上跑的（一个点的命运要靠跨边界的邻居来判定），而只裁剪结果数据会让 padding 那些 Actor 照样
        /// 留在场景里。
        /// </summary>
        /// <remarks>
        /// 存的是**格子**而不只是它的盒子，因为哪几条边界被包含进来是"这个格子在区域里的位置"的属性——
        /// 见 <see cref="PCGPartitionPlanner.IsInsideCell"/>。
        /// </remarks>
        public PCGPartitionCell? ClipCell;

        /// <summary>
        /// 这次运行的**存活区域**：体积自己的世界盒。分区运行时格子优先（<see cref="ClipCell"/> 按格判定），
        /// 整块运行时就看这里。
        /// </summary>
        /// <remarks>
        /// 为什么整块运行也要裁剪：体积是"在这儿生成"的契约，而有些节点（洞穴的巷道、绕远的路）会走到盒子外面去。
        /// 不裁的话，"开不开分区"就变成了两套内容——实测同一个洞、同一个种子、同一个盒子，整块出 12 件、分区出
        /// 9 件（差的那 3 件在盒外）。两边的边界取法必须一致：格子那片区域的**并集**是闭区间（最远那格的
        /// <c>IsLastX/IsLastZ</c> 把上界也包进来，见 <see cref="PCGPartitionPlanner.IsInsideCell"/>），
        /// 所以这里的上下界都按闭区间判。
        /// </remarks>
        public BoundingBox? ClipRegion;

        /// <summary>这次运行之后内容还留着的区域；不是分区运行时为 null。</summary>
        public BoundingBox? ClipBounds => ClipCell?.ClipBounds;

        /// <summary>
        /// 一个位置是否落在这次运行的裁剪区域里；既没有格子也没有区域时一律为 true。
        /// </summary>
        /// <remarks>
        /// 只由这一处判定，于是生成器不可能与运行器在"哪些点算属于这一格"上产生分歧。
        /// </remarks>
        public bool IsInsideClip(Vector3 position)
        {
            if (ClipCell.HasValue)
                return PCGPartitionPlanner.IsInsideCell(ClipCell.Value, position);

            if (ClipRegion.HasValue)
            {
                var bounds = ClipRegion.Value;

                return position.X >= bounds.Minimum.X && position.X <= bounds.Maximum.X &&
                       position.Y >= bounds.Minimum.Y && position.Y <= bounds.Maximum.Y &&
                       position.Z >= bounds.Minimum.Z && position.Z <= bounds.Maximum.Z;
            }

            return true;
        }

        /// <summary>创建一份用默认值的上下文。</summary>
        public PCGExecutionContext()
        {
            World = new PCGWorldContext();
        }

        /// <summary>创建一份字段齐全的上下文。</summary>
        public PCGExecutionContext(int seed, BoundingBox bounds, PCGWorldContext world, CancellationToken token)
        {
            Seed = seed;
            GenerationBounds = bounds;
            World = world;
            CancelToken = token;
        }

        /// <summary>
        /// 每 <paramref name="interval"/> 次迭代轮询一次取消标记。
        /// </summary>
        /// <remarks>
        /// 取消标记只在被检查的地方生效。逐点的长循环必须定期调用它，否则"图一改就把上一次运行取消掉"其实
        /// 什么都打断不了，而快速拖动参数会把 CPU 吃满的陈旧运行堆起来。
        /// </remarks>
        public void ThrowIfCancelled(int iteration, int interval = 256)
        {
            if (interval <= 0 || (iteration % interval) == 0)
                CancelToken.ThrowIfCancellationRequested();
        }

        /// <summary>取当前节点某个点的确定性随机数发生器。</summary>
        public PCGDeterministicRng GetRng(int pointIndex)
        {
            return new PCGDeterministicRng(Seed, CurrentNodeId, pointIndex);
        }

        /// <summary>取当前节点整体的确定性随机数发生器。</summary>
        public PCGDeterministicRng GetNodeRng()
        {
            return new PCGDeterministicRng(Seed, CurrentNodeId);
        }
    }

    /// <summary>节点引脚的方向。</summary>
    public enum PCGPinDirection
    {
        /// <summary>消费数据。</summary>
        Input = 0,

        /// <summary>产出数据。</summary>
        Output = 1,
    }

    /// <summary>
    /// 声明节点的一个引脚。**顺序有意义**：它定义了图连线所用的引脚下标。
    /// </summary>
    public sealed class PCGPinDefinition
    {
        /// <summary>显示名，在同一节点内唯一。</summary>
        public string Name;

        /// <summary>方向。</summary>
        public PCGPinDirection Direction;

        /// <summary>这个输入没有数据时节点就不能运行时为 true。</summary>
        public bool IsRequired;

        /// <summary>为 true 时多条连线可以指向这个输入，执行器把它们作为一个数组传进来。</summary>
        public bool AllowMultiple;

        /// <summary>图编辑器里显示给人看的说明。</summary>
        public string Tooltip;

        /// <summary>创建一个引脚声明。</summary>
        public PCGPinDefinition(string name, PCGPinDirection direction, bool isRequired = false,
            bool allowMultiple = false, string tooltip = null)
        {
            Name = name;
            Direction = direction;
            IsRequired = isRequired;
            AllowMultiple = allowMultiple;
            Tooltip = tooltip;
        }

        /// <inheritdoc />
        public override string ToString() => $"{(Direction == PCGPinDirection.Input ? "In" : "Out")} {Name}";
    }

    /// <summary>
    /// 每个 PCG 节点的基类。
    /// </summary>
    /// <remarks>
    /// 实现除了自己那些序列化参数之外**必须无状态**：执行器每张图的每个节点只缓存一个实例，可能反复调用
    /// <see cref="OnExecute"/>，在编辑器里还会跨脚本重载。绝不要在两次运行之间缓存 <see cref="PCGData"/>。
    /// </remarks>
    public abstract class PCGNodeBase
    {
        /// <summary>
        /// 稳定的类型标识。必须与注册在 <see cref="PCGNodeRegistry"/> 里的 id 一致，有了图编辑器之后还要与
        /// <c>NodeArchetype.TypeID</c> 一致。
        /// </summary>
        public abstract string TypeId { get; }

        /// <summary>供日志与编辑器显示的名字。</summary>
        public abstract string DisplayName { get; }

        /// <summary>输入引脚，按顺序。</summary>
        public abstract PCGPinDefinition[] InputPins { get; }

        /// <summary>输出引脚，按顺序。</summary>
        public abstract PCGPinDefinition[] OutputPins { get; }

        /// <summary>
        /// <see cref="OnExecute"/> 是否可以在工作线程上跑。默认 false。
        /// </summary>
        /// <remarks>
        /// 只有读不可变输入、又不碰任何场景状态的采样类节点够格。任何建 Actor、写资产或调用物理场景的东西
        /// 都必须保持 false。
        /// </remarks>
        public virtual bool IsThreadSafe => false;

        /// <summary>
        /// 运行这个节点。
        /// </summary>
        /// <param name="context">执行上下文。</param>
        /// <param name="inputs">
        /// 按声明顺序，每个输入引脚一项。没接线的可选引脚为 null。对于带
        /// <see cref="PCGPinDefinition.AllowMultiple"/> 的引脚，这一项是**按连线顺序**收上来的所有数据集组成的
        /// 数组，且永不为 null（可以是空数组）。
        /// </param>
        /// <returns>0 号引脚的输出数据；节点没有输出引脚时返回 null。</returns>
        public abstract PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs);

        /// <summary>
        /// 运行这个节点，并校验执行器给的引脚与节点声明的一致。
        /// </summary>
        internal PCGData Execute(PCGExecutionContext context, PCGData[] inputs)
        {
            var declared = InputPins;
            if (inputs == null || inputs.Length != declared.Length)
            {
                throw new PCGNodeExecutionException(
                    $"Node '{TypeId}' declares {declared.Length} input pin(s) but received {inputs?.Length ?? 0}.");
            }

            return OnExecute(context, inputs);
        }

        /// <summary>
        /// 给出一句关于本节点当前参数的摘要，供图日志使用。
        /// </summary>
        public virtual string DescribeParameters() => string.Empty;

        /// <summary>
        /// 本节点最近一次运行为什么产出少于应有量，或 null。
        /// </summary>
        /// <remarks>
        /// 放在基类上是因为**该把它报出来的是执行器，不是每个节点**：一个只用 <c>Debug.LogWarning</c> 报告问题的
        /// 采样器，留给设计师的是空场景加上一句指不到图里任何地方的话；而只返回空数据集的节点更糟。setter 是
        /// protected 的，于是只有节点自己会写关于自己的话；执行器读并清空。
        /// </remarks>
        public string LastError { get; protected set; }

        /// <summary>由执行器在错误已被收进本次运行的警告之后调用。</summary>
        internal void ClearError() => LastError = null;

        /// <inheritdoc />
        public override string ToString() => $"{DisplayName} ({TypeId})";
    }

    /// <summary>节点在运行期失败时抛出。</summary>
    public sealed class PCGNodeExecutionException : Exception
    {
        /// <summary>创建异常。</summary>
        public PCGNodeExecutionException(string message) : base(message)
        {
        }

        /// <summary>带内因创建异常。</summary>
        public PCGNodeExecutionException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
