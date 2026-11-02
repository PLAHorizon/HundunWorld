// PCG 插件 - PCG 体积 Actor
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §7.1 与 §7.2。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;
using PCG.Graph;
using PCG.TerrainSampling;

namespace PCG.Runtime
{
    /// <summary>体积如何应用它的图产出。</summary>
    public enum PCGGenerationMode
    {
        /// <summary>在编辑器里跑，并把生成物留在场景里。最终的生产产物。</summary>
        Bake = 0,

        /// <summary>运行期跑，并按需要持续重新生成。</summary>
        Runtime = 1,

        /// <summary>在编辑器里跑，只画调试图；场景里一个东西都不加。</summary>
        Preview = 2,
    }

    /// <summary>某个图参数的逐实例覆盖。</summary>
    [Serializable]
    public struct PCGParameterOverride
    {
        /// <summary>要覆盖的暴露参数名。</summary>
        public string ParameterName;

        /// <summary>替换成的值。</summary>
        public PCGParameterValue Value;

        /// <inheritdoc />
        public override string ToString() => $"{ParameterName} = {Value}";
    }

    /// <summary>一次图运行的生成物记账，好让重新生成**恰好**只换掉自己那批产出。</summary>
    public sealed class PCGGenerationResult
    {
        /// <summary>这次运行建出来的 Actor。</summary>
        public List<Actor> SpawnedActors = new List<Actor>();

        /// <summary>主输出的点数。</summary>
        public int PointCount;

        /// <summary>这次运行的墙上时钟耗时。</summary>
        public double Milliseconds;

        /// <summary>这次运行里的非致命消息。</summary>
        public string[] Warnings = Array.Empty<string>();

        /// <summary>这次运行的 Unix 时间戳。</summary>
        public long TimestampUtcTicks;

        /// <inheritdoc />
        public override string ToString() =>
            $"PCGGenerationResult(actors={SpawnedActors.Count}, points={PointCount}, ms={Milliseconds:F1})";
    }

    /// <summary>
    /// 在一个盒状体积上跑一张 PCG 图的场景组件。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>体积空间。</b><see cref="Volume"/> 是 Actor 的**局部空间**，所以生成区域
    /// 会跟着 Actor 一起移动或旋转。点是在世界空间产出的，所以 Actor 的
    /// 变换在生成时**一次性**复合进去。生成之后挪动体积不会挪动已有
    /// 产出；要挪就重新生成。这是有意的取舍——每变一次变换就重新生成，
    /// 会让大图上的拖拽完全没法用。
    /// </para>
    /// <para>
    /// <b>产出归属。</b>生成出来的 Actor 挂在这个 Actor 之下，并带固定前缀命名，
    /// 于是 <see cref="Clear"/> 能精确收回这个体积的产出，而不碰手摆的场景
    /// 内容。
    /// </para>
    /// </remarks>
#if FLAX_EDITOR
    /// <summary>
    /// 注册进编辑器的「新建」菜单——这就是"怎么把 PCG 图挂到场景里"缺的那一环。
    /// </summary>
    /// <remarks>
    /// 编辑器只把**带这个属性**的 Actor 类型列进场景树的「新建 / 转换」菜单（见
    /// <c>SceneTreeWindow.ContextMenu</c>：拿不到属性的类型直接 `continue`），Prefab 窗口同理。
    /// 少了它，`PCGVolume` 在界面上无处可造：体积、参数、生成按钮都齐了，却没有入口造出一个来。
    /// 编辑器专有的属性，所以要 `FLAX_EDITOR` 守卫——Game 目标那份程序集里没有它。
    /// </remarks>
    [ActorContextMenu("New/PCG/PCG Volume")]
#endif
    public class PCGVolume : Actor
    {
        /// <summary>这个体积生成的每个 Actor 都会带的前缀。</summary>
        public const string GeneratedNamePrefix = PCGSpawnNaming.GeneratedPrefix;

        /// <summary>
        /// 要执行的图文件，可以写插件图目录里的名字，也可以写路径。null 或空
        /// 表示这个体积是惰性的。
        /// </summary>
        /// <remarks>
        /// 这些文件由插件自己管（见 <see cref="PCGGraphFile"/>）：托管插件注册不了
        /// 资产类型，而引擎也没有把"回写资产"这条路开放给脚本，所以"工具存
        /// 文件、体积读文件"是唯一一条可信通道。裸名会落到
        /// Content/PCG/Graphs，于是一个关卡里只写 "forest_rocks" 就够了。
        /// </remarks>
        [Tooltip("图文件名或路径（裸名字会去 Content/PCG/Graphs 下找）。空着时这个体积什么都不做。")]
        public string GraphPath;

        /// <summary>生成区域，在这个 Actor 的局部空间里。</summary>
        [Tooltip("生成区域，在这个 Actor 的局部空间里。")]
        public BoundingBox Volume = new BoundingBox(new Vector3(-500.0f), new Vector3(500.0f));

        /// <summary>对图的暴露参数的逐实例覆盖。</summary>
        [Tooltip("覆盖图里暴露出来的参数。")]
        public PCGParameterOverride[] ParameterOverrides = Array.Empty<PCGParameterOverride>();

        /// <summary>图级随机种子。</summary>
        [Tooltip("图级随机种子；改它就会重掷一次结果。")]
        public int Seed;

        /// <summary>产出如何应用。</summary>
        [Tooltip("生成结果怎么落到场景里。")]
        public PCGGenerationMode Mode = PCGGenerationMode.Bake;

        /// <summary>
        /// 逐格生成区域，而不是一次跑完。
        /// </summary>
        /// <remarks>
        /// 每一格在采样之前先向外长出一圈 margin，采完再裁回来，于是间距规则
        /// 仍然看得见边界另一侧。布局**不会**与同种子的非分区运行一样——
        /// 采样器的逐点随机数按点下标取，而下标在每格内部重启——
        /// 所以要在一个大到一次跑不完的区域上打开它，而不是为了换个样子。
        /// </remarks>
        [Tooltip("按格生成而不是一次跑完整片；同一个种子下的布局会变，见设计文档。")]
        public bool Partitioned;

        /// <summary>X 与 Z 上的格子边长，世界单位。</summary>
        [Tooltip("单格边长（X 与 Z，世界单位）。")]
        public float PartitionCellSize = 500.0f;

        /// <summary>
        /// 一格在裁剪之前越过自己边界多远。-1 表示由图自己的
        /// 自剪枝间距推出来，而那正是不能猜错的那个值。
        /// </summary>
        [Tooltip("每格外扩多少。-1 = 按图的剪枝间距推；接缝对不上就把它调大。")]
        public float PartitionPadding = -1.0f;

        /// <summary>
        /// 图看不见的东西变了（改了地形、挪了宿主 Actor）就把它加一，
        /// 下一次分区运行会重生成每一格而不是复用存下来的结果。
        /// </summary>
        /// <remarks>
        /// 插件没有"这一格底下的地面现在不一样了"的可观察来源，所以这个旋钮是
        /// **显式**的，而不是去猜——猜错会悄悄交回陈旧的物件。
        /// </remarks>
        [Tooltip("调大它可以强制每格重算（地形改过、宿主挪过）。")]
        public int PartitionWorldVersion = 1;

        /// <summary>
        /// 宿主给的"各格输入"的版本号，与 <see cref="PartitionWorldVersion"/> 合起来用。
        /// </summary>
        /// <remarks>
        /// 游戏知道一些插件看不见的事——地形修订号、Actor 的变换戳、
        /// 存档计数器——就把那个数从这里返回，它一变每格都重生成。
        /// 不设它时手拨字段仍是唯一的旋钮，与以前完全一样。一个返回自己
        /// 说不清的值的来源（随机数、帧号）会让**每次**请求都作废每一格；那是调用者的
        /// 决定，但那个数不是版本号。
        /// </remarks>
        [NoSerialize]
        public Func<int> PartitionVersionSource;

        /// <summary>手拨的分区版本号与宿主给的合在一起，于是任何一边都能让它失效。</summary>
        private int EffectiveWorldVersion()
        {
            return PartitionVersionSource == null
                ? PartitionWorldVersion
                : PCGPartitionCache.CombineWorldVersion(PartitionWorldVersion, PartitionVersionSource());
        }

        /// <summary>
        /// 只生成 <see cref="StreamCenter"/> 附近 <see cref="StreamRadius"/> 以内的格子，并把
        /// 已经离开 <see cref="StreamKeepRadius"/> 的那些交回。需要 <see cref="Partitioned"/>。
        /// </summary>
        /// <remarks>
        /// 这里没有任何东西自己会跳：一次请求是按**调用 <c>Generate</c> 那一刻**体积手里的中心切的，
        /// 所以谁挪中心（传送、重登、一个判断
        /// 窗口漂得够远的跟随脚本），谁就决定什么时候请求。每一步都自动跟随会让格子
        /// 按一个没人做过预算的帧率重生成。
        /// </remarks>
        [Tooltip("只在窗口内生成而不是整片区域（需要先开 Partitioned）。")]
        public bool Streaming;

        /// <summary>流式窗口围绕哪个点切，世界单位。只读 X 与 Z。</summary>
        [Tooltip("流式窗口围绕哪个世界位置切。竖直距离忽略。")]
        public Vector3 StreamCenter;

        /// <summary>离中心多远以内的格子仍然会被生成。</summary>
        [Tooltip("离中心多远以内的格子还会生成。")]
        public float StreamRadius = 1000.0f;

        /// <summary>
        /// 再往外多远，已生成的格子才被收走。-1 表示流式半径再加两格，
        /// 那就是滞回——它挡住"玩家在边界上来回走导致同一格每步都重生成"。
        /// </summary>
        [Tooltip("超出多远就销毁该格的物件并丢掉它的缓存点。-1 = 半径另加两格（滞回）。")]
        public float StreamKeepRadius = -1.0f;

        /// <summary>
        /// 一次请求生成多少格，离中心最近优先。零表示一次调用把整个窗口都拿下，
        /// 那正是一次远距离传送对发起它的那一帧干的事。
        /// </summary>
        /// <remarks>
        /// 超限的格子是**延后**而不是丢掉：它们的物件与缓存点都留着，
        /// 所以下一次请求把它们做完即可，不必重算任何东西。没有任何东西会自己把它们补上——
        /// 设中心的人必须继续请求，这和窗口本身的契约是同一件事。
        /// </remarks>
        [Tooltip("一次请求生成几格，从靠近中心的开始。0 = 一次把整个窗口做完。")]
        public int StreamCellsPerRequest;

        /// <summary>
        /// 世界的分块网格边长，世界单位；零表示用径向窗口。
        /// </summary>
        /// <remarks>
        /// 大于零时窗口变成**整块**：观察者所在的那一块，每向各外扩
        /// <c>ceil(StreamRadius / StreamChunkSize)</c> 块。于是图与
        /// 一个按块流式的世界对得上——生成的集合就是世界系统认为可见的那些块，而
        /// 边界不会把一块切成两半。又因为窗口跟的是观察者的 <i>块</i> 而不是
        /// 观察者的位置，在一块之内走动完全不重生成。
        /// </remarks>
        [Tooltip("世界 chunk 边长。>0 时窗口按这个网格对齐成整块；0 = 圆形窗口。")]
        public float StreamChunkSize;

        /// <summary>
        /// 流式窗口跟随其位置的 Actor。为 null 时保持 <see cref="StreamCenter"/> 手拨，
        /// 也就是它一直以来的样子：体积只在别人交给它的中心上切一个窗口。
        /// </summary>
        /// <remarks>
        /// 跟随每帧只花一次位置读取与两次整数比较；只有
        /// 被跟随的点跨了格、或者上一次请求还留下延后的格子没做完时，
        /// 才会真发一次请求。它不是定时器，而且一旦"再问一次也不会
        /// 让积压变少"就立刻停下——见 <see cref="PCGStreamTrigger.ShouldRequest"/>。
        /// </remarks>
        [Tooltip("窗口跟随哪个 Actor。为空表示由调用方自己设 StreamCenter。")]
        public Actor StreamFollowTarget;

        /// <summary>
        /// 被跟随的 Actor 要走多远才算进了新的邻里。-1 表示用
        /// 体积生成时**最细**的那一档格子尺寸——它自己的 <see cref="PartitionCellSize"/>，或者
        /// 启用层里最小的那个，那是唯一一个"所有层的窗口都是当下"的步长。
        /// </summary>
        [Tooltip("跟随时要走多远才算换了地方（世界单位）。-1 = 用最细那层的格子尺寸。")]
        public float StreamFollowStep = -1.0f;

        /// <summary>
        /// 窗口跟随的那些 Actor，供世界里不止一个玩家时使用。
        /// </summary>
        /// <remarks>
        /// 非空时优先于 <see cref="StreamFollowTarget"/>：窗口变成"它们每个人周围一个半径"的并集——
        /// 一个格子只要靠近 <i>任一</i> 观察者就被生成，而只有在它
        /// 离开 <i>每一个</i> 观察者之后才被交回。于是两个玩家走开时各自脚下都还有地，而他们之间的
        /// 那片空间里的物件不会因为"能画一条穿过它的线"就被生成。
        /// </remarks>
        [Tooltip("窗口跟随的多个 Actor，取它们邻域的并集（优先于单个跟随目标）。")]
        public Actor[] StreamFollowTargets;

        /// <summary>
        /// 一次请求围绕其中多少个观察者切，保留那些分散开的。
        /// </summary>
        /// <remarks>
        /// 并集就是被生成的量，所以观察者数量就是请求的规模：四十个玩家
        /// 或四十个已加载的分块会一次要求整张地图。超过上限之后，留下谁靠
        /// **最远点采样**而不是列表顺序挑，因为挤在头几条里的
        /// 一群人会把整个预算花在同一个邻里上。
        /// </remarks>
        [Tooltip("一次请求覆盖几个视角，会优先保留分散开的那些。0 = 不设上限。")]
        public int StreamMaxViewers = PCGStreamViewers.DefaultMaxViewers;

        /// <summary>
        /// 用一串网格级联生成：每一层判定下一层哪里值得生成，只有
        /// 最细那层的格子真正建东西。
        /// </summary>
        /// <remarks>
        /// 判定的那几遍什么都不建，于是一片空区域只花几次便宜的运行
        /// 而不是几百次细格的运行——而且每多一层都把工作收得更窄。需要
        /// <see cref="Partitioned"/>；最细那层用 <see cref="PartitionCellSize"/>。两层是
        /// 最常见的用法，行为与两级层级一直以来的表现完全一致。
        /// </remarks>
        [Tooltip("级联式多层生成：粗层只判定，最细那层才真生成。")]
        public bool Hierarchical;

        /// <summary>最粗那层判定网格的边长。-1 表示用四倍的 <see cref="PartitionCellSize"/>。</summary>
        [Tooltip("最粗一层的格子边长。-1 = 分区格子尺寸的四倍。")]
        public float HierarchicalCoarseSize = -1.0f;

        /// <summary>
        /// 级联有多少层，含真正生成的那一层。
        /// </summary>
        /// <remarks>
        /// 最粗层与 <see cref="PartitionCellSize"/> 之间的尺寸按**等比**插值，
        /// 于是每一层都以同一个倍率细分它的父层。写 2 就是原来的两级行为；
        /// 更多层在每一步都收窄工作，代价是每个活格每层多一次干跑。
        /// </remarks>
        [Tooltip("级联几层（含真正生成的那层）。2 = 经典的粗/细两级。")]
        public int HierarchicalLevels = 2;

        /// <summary>
        /// 一个判定格要产出多少点，它下面的下一层才被考虑。
        /// </summary>
        /// <remarks>
        /// 默认是一，也就是"这里有东西"。调高它是设计师在说"只在
        /// 密的地方费劲"，这与 UE5 的密度阈值是同一个旋钮。它作用于每一个判定层，
        /// 因为每一层问的问题都一样。
        /// </remarks>
        [Tooltip("一个判定格至少出几个点，它下面那层才值得生成。")]
        public int HierarchicalMinCoarsePoints = 1;

        /// <summary>
        /// 下一次生成写进去的记录器，于是调用者能看到每个节点产出了什么。
        /// </summary>
        /// <remarks>
        /// Set by whoever wants the report - the editor's试跑 path, or a self-test measuring how many cell runs a
        /// 层级生成到底跑了几次格子。正常使用时留 null，此时它每个节点只多付一次 null 判断、
        /// 什么都不留。
        /// </remarks>
        [NoSerialize]
        public PCGNodeInspection NodeInspection;

        /// <summary>
        /// 宿主提供的观察者，供"窗口由世界驱动而不是由手摆的 Actor 驱动"使用。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 游戏把它认为被观看的东西（玩家、已加载的分块、可见块）填进列表，
        /// 并返回写了多少条；插件从来不需要知道那到底是哪种。这个钩子让
        /// 分块流式器能驱动生成，而**不必**让插件依赖游戏的世界
        /// 模块——那个依赖方向是错的。
        /// </para>
        /// <para>
        /// 它优先于被跟随的 Actor：带 source 的体积不理会它们，于是一个
        /// 场景可以只改一个字段就从"跟随这些 Actor"切成"跟随世界说的那些"。
        /// 一个什么都不返回的 source 就是空窗口，与"没有观察者"完全一样。
        /// </para>
        /// </remarks>
        [NoSerialize]
        public Func<List<Vector3>, int> StreamViewerSource;

        /// <summary>
        /// 世界自己的可见分块清单；设上它之后它取代推导出来的窗口。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 一个按块流式的世界本来就知道自己加载了哪些块；再从玩家
        /// 位置推一遍只会与它不一致，而不一致的表现就是"地面还不存在却已经生成了"
        /// 内容，或者半块。世界把它认为可见的块坐标写进
        /// 列表并返回写了多少——形状与 <see cref="StreamViewerSource"/> 一样，包括
        /// 那个钳制：返回条数大于实际写入条数会被钳掉，因为缓冲在请求之间
        /// 是复用的，信了它就会把上一次请求的块重放一遍。
        /// </para>
        /// <para>
        /// 需要 <see cref="StreamChunkSize"/>，因为正是它把块坐标换算成世界单位。
        /// 不设这个字段时窗口就还是按半径推出来的那个。
        /// </para>
        /// </remarks>
        [NoSerialize]
        public Func<List<Int2>, int> StreamChunkSource;

        /// <summary>
        /// 在可见集合之外多列几块，这个模式就是靠它表达滞回。
        /// </summary>
        [Tooltip("在可见集合之外多留几圈 chunk，相当于按 chunk 计的保活半径。")]
        public int StreamChunkBorder;

        /// <summary>在视口里画生成出来的点。</summary>
        [Tooltip("在视口里画出生成出来的点。")]
        public bool DebugDrawPoints = true;
        /// <summary>
        /// 打印生成时间都花在哪些节点上，最慢的在前。
        /// </summary>
        /// <remarks>
        /// 默认关：这一行是给"某次生成意外地慢"那一刻用的，而执行器其实一直
        /// 都在量这份分解，只是没人看得见。
        /// </remarks>
        [Tooltip("生成后把逐节点耗时打出来（从慢到快）。")]
        public bool LogNodeTimings;

        /// <summary>调试绘制时跳过密度低于这个值的点。</summary>
        [Range(0.0f, 1.0f)]
        [Tooltip("密度低于这个值的调试点不画。")]
        public float DebugDrawMinDensity;

        /// <summary>调试绘制最多画多少个点，免得编辑器卡住。</summary>
        [Tooltip("调试绘制最多画几个点，按密度着色。值越大每帧越贵。")]
        public int DebugDrawMaxPoints = 20000;

        [NoSerialize]  // 运行时状态：存进场景只会存进一份过期、还可能成环的对象图
        private string _overrideSignature;
        [NoSerialize]  // 运行时状态：存进场景只会存进一份过期、还可能成环的对象图
        private int _overrideVersion;

        // 上一次请求时各被跟随 Actor 在哪，按观察者记。MinValue 表示"从没请求过"，
        // 于是设上目标之后第一帧一定会请求；观察者数量变了也算
        // 一次跨越，理由相同。
        [NoSerialize]  // 运行时缓冲：每帧/每次请求重填，不该进场景
        private readonly List<Vector3> _viewerBuffer = new List<Vector3>(4);
        [NoSerialize]  // 运行时缓冲：每帧/每次请求重填，不该进场景
        private readonly List<Vector3> _reducedViewers = new List<Vector3>(8);
        [NoSerialize]  // 运行时缓冲：每帧/每次请求重填，不该进场景
        private int[] _viewerCellsX = Array.Empty<int>();
        [NoSerialize]  // 运行时缓冲：每帧/每次请求重填，不该进场景
        private int[] _viewerCellsZ = Array.Empty<int>();
        [NoSerialize]  // 运行时状态：存进场景只会存进一份过期、还可能成环的对象图
        private int _deferredAtLastRequest = -1;
        [NoSerialize]  // 运行时状态：存进场景只会存进一份过期、还可能成环的对象图
        private PCGGenerationResult _result;
        [NoSerialize]  // 运行时状态：存进场景只会存进一份过期、还可能成环的对象图
        private PCGData _lastOutput;
        [NoSerialize]  // 运行时状态：存进场景只会存进一份过期、还可能成环的对象图
        private bool _isGenerating;

        /// <summary>最近一次生成的结果，或 null。</summary>
        public PCGGenerationResult LastResult => _result;

        /// <summary>最近一次生成产出的点，或 null。</summary>
        public PCGData LastOutput => _lastOutput;

        /// <summary>
        /// 最近一次 <see cref="Generate"/> 的逐层结果，按层顺序；没有
        /// <see cref="Layers"/> 的体积只有一项。
        /// </summary>
        /// <remarks>
        /// 分层从外面本来是看不见的：<see cref="LastResult"/> 是个总数，而"第二
        /// 层什么都没产出"与"两层各产出一半"读起来一模一样。诊断与自检
        /// 读的是这个，而不是去解析日志。
        /// </remarks>
        public PCGGenerationResult[] LayerResults
        {
            get
            {
                var results = new List<PCGGenerationResult>();
                for (int i = 0; i < _runs.Count; i++)
                {
                    if (_runs[i].Result != null)
                        results.Add(_runs[i].Result);
                }

                return results.ToArray();
            }
        }

        /// <summary><see cref="LayerResults"/> 里那些运行的标签，顺序相同。</summary>
        public string[] LayerLabels
        {
            get
            {
                var labels = new List<string>(_runs.Count);
                for (int i = 0; i < _runs.Count; i++)
                    labels.Add(_runs[i].Label);

                return labels.ToArray();
            }
        }

        /// <summary>当前是否正在生成。</summary>
        public bool IsGenerating => _isGenerating;

        /// <summary>这个体积已生成、尚未清掉的 Actor 个数。</summary>
        /// <remarks>
        /// 有本次运行的账面就报账面（那是逐格/逐层精确的）。**没有账面时要数一遍孩子**：`_result`
        /// 是运行期状态、不进场景，所以重新打开场景（或编辑器重启）之后它是空的，而这个体积之下
        /// 明明还挂着上一次生成的全部产出——照账面报就成了"0 个 Actor"，恰恰又回到当初那个
        /// "点数有、Actor 数是零"的误读（设计师会以为生成没落地）。产出归属本来就是**结构性**的
        /// （谁的孩子就是谁的），所以数孩子与账面是同一个口径；已经交给销毁队列、但帧末才消失的
        /// 那些要排掉，否则清空之后立刻读会报出一批已经不存在的数量。
        /// </remarks>
        public int GeneratedActorCount
        {
            get
            {
                if (_result != null)
                    return _result.SpawnedActors?.Count ?? 0;

                var children = GetChildren<Actor>();
                if (children == null)
                    return 0;

                int count = 0;

                for (int i = 0; i < children.Length; i++)
                {
                    var actor = children[i];

                    if (actor != null &&
                        PCGSpawnNaming.IsGeneratedName(actor.Name) &&
                        !_scheduledDestroy.Contains(actor.ID))
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// 本体积已经交给销毁队列、但还没真正消失的孩子。
        /// </summary>
        /// <remarks>
        /// <see cref="Actor.Destroy"/> 的销毁延迟到帧末，所以刚清过的那一批此刻仍留在子级里。
        /// 再清一次会把它们一起数进去——实测"清空销毁了 4 个"而实际只有 2 个。记下已排队的 id，
        /// 清理时跳过；集合按"还在孩子们里的"修剪，所以不会随场景寿命一直长大。
        /// </remarks>
        [NoSerialize]
        private HashSet<Guid> _scheduledDestroy = new HashSet<Guid>();

        /// <summary>
        /// 可选的额外生成层：每层一张图、一个格子粒度。
        /// </summary>
        /// <remarks>
        /// 空（默认）就沿用体积自己的 <see cref="GraphPath"/>/<see cref="PartitionCellSize"/>
        /// 那条路，多数体积要的就是那个。分层是给"同一片区域既要稀疏的大件、
        /// 又要密集的小件"用的，那种地方一种格子尺寸两头都不合适。
        /// </remarks>
        [Tooltip("额外的生成层：每层一张图、各自的格子尺寸。为空表示只用这个体积自己的图。")]
        public PCGPartitionLayer[] Layers;

        /// <summary>要跑的一次生成：要么体积自己，要么一个声明过的层。</summary>
        private sealed class LayerRun
        {
            internal int Index;
            internal string Label;
            internal string GraphFile;
            internal int Seed;
            internal bool Partitioned;
            internal float CellSize;
            internal float Padding;
            internal PCGCompiledGraph Compiled;
            internal int CompiledHash;
            internal PCGPartitionCache Cache;

            // 当前请求的格子规划。每次 Generate 切一次，清理与执行共用，
            // 因为两边必须**逐格**对"正在被替换的是哪些"达成一致。
            internal List<PCGPartitionCell> Cells;
            internal List<PCGPartitionCell> ActiveCells;
            internal List<PCGPartitionCell> EvictedCells;
            internal int DeferredCells;

            /// <summary>这次运行**拒绝了复用**（图会写场景）——于是格子级的复用计数器没有意义。</summary>
            internal bool ReuseRefused;

            // 只在层级运行时设置：N 级规划，以及每个判定层所覆盖的窗口格子。
            internal PCGPartitionHierarchy Hierarchy;
            internal List<List<PCGPartitionCell>> DecidingWindows;

            internal PCGGenerationResult Result;
        }

        [NoSerialize]  // 运行时缓冲：每帧/每次请求重填，不该进场景
        private readonly List<LayerRun> _runs = new List<LayerRun>();
        [NoSerialize]  // 运行时状态：存进场景只会存进一份过期、还可能成环的对象图
        private LayerRun _run;

        /// <summary>
        /// 跑这个体积拥有的每一次生成，并返回是否有任何一次产出了东西。
        /// </summary>
        /// <remarks>
        /// 重入守卫刻意盖住**整个集合**：否则第 1 层还在跑时的第二次调用
        /// 会重入第 0 层的执行器，而它的逐次运行缓存不是重入安全的。
        /// </remarks>
        public bool Generate()
        {
            if (_isGenerating)
            {
                Debug.LogWarning($"[PCG] {Name}: generation already in progress; ignoring re-entrant call.");
                return false;
            }

            BuildRuns();

            _isGenerating = true;
            bool any = false;
            bool layered = _runs.Count > 1;

            for (int i = 0; i < _runs.Count; i++)
            {
                _run = _runs[i];
                any |= GenerateOne(layered);
            }

            _run = null;
            _isGenerating = false;

            if (layered)
                SummariseLayers();
            else
                _result = _runs.Count > 0 ? _runs[0].Result : null;

            UpdateCellDiagnostics();

            return any;
        }

        /// <summary>
        /// 上一次生成留下的**格子级读数**（活的/真执行的/复用的/延后的），按各层求和。
        /// </summary>
        /// <remarks>
        /// 存在的理由是一次实测：同一个分层+分区体积、同一张图、同一个种子，连跑三次留在场景里的 Actor 是
        /// **120 / 60 / 72**，而点数每次都是 784——"为什么这次少了一半东西"用日志答不上来（磁盘日志还会滞后），
        /// 只能靠这些数回答：格数不变而执行/复用比例变了，就是复用把一部分格子跳过了。
        /// </remarks>
        public int LastActiveCells { get; private set; }

        /// <summary>上一次生成里真的执行了的格子数（各层求和）。</summary>
        public int LastExecutedCells { get; private set; }

        /// <summary>上一次生成里靠缓存复用、没有重新执行的格子数（各层求和）。</summary>
        public int LastReusedCells { get; private set; }

        /// <summary>上一次生成里因为请求预算而延后的格子数（各层求和）。</summary>
        public int LastDeferredCells { get; private set; }

        private void UpdateCellDiagnostics()
        {
            int active = 0, executed = 0, reused = 0, deferred = 0;

            for (int i = 0; i < _runs.Count; i++)
            {
                var run = _runs[i];
                int runActive = run.ActiveCells?.Count ?? run.Cells?.Count ?? 0;

                active += runActive;
                deferred += run.DeferredCells;

                if (run.ReuseRefused)
                {
                    // 写场景的图**拒绝复用**，于是"执行/复用"这两个计数器根本没有意义。
                    // 报 0 会被读成"一个格子都没跑"，那是假象：没有复用就意味着每一格都真跑了。
                    executed += runActive;
                }
                else
                {
                    executed += run.Cache?.LastRunExecutedCells ?? 0;
                    reused += run.Cache?.LastRunReusedCells ?? 0;
                }
            }

            LastActiveCells = active;
            LastExecutedCells = executed;
            LastReusedCells = reused;
            LastDeferredCells = deferred;

            if (Partitioned)
            {
                Debug.Log($"[PCG] {Name}: cells{LayerSuffix()} - {active} active, {executed} executed, " +
                          $"{reused} reused, {deferred} deferred.");
            }
        }

        /// <summary>
        /// 构建运行清单，复用之前那次运行已经填好的已编译图与结果缓存。
        /// </summary>
        private void BuildRuns()
        {
            var layers = Layers;
            int count = layers == null || layers.Length == 0 ? 1 : layers.Length;

            while (_runs.Count < count)
                _runs.Add(new LayerRun { Cache = new PCGPartitionCache() });
            while (_runs.Count > count)
                _runs.RemoveAt(_runs.Count - 1);

            for (int i = 0; i < count; i++)
            {
                var run = _runs[i];
                run.Index = i;

                if (count == 1)
                {
                    run.Label = "main";
                    run.GraphFile = GraphPath;
                    run.Seed = Seed;
                    run.Partitioned = Partitioned;
                    run.CellSize = PartitionCellSize;
                    run.Padding = PartitionPadding;
                    continue;
                }

                var layer = layers[i] ?? new PCGPartitionLayer();
                run.Label = string.IsNullOrEmpty(layer.Name) ? $"layer {i}" : layer.Name;
                run.GraphFile = string.IsNullOrEmpty(layer.GraphPath) ? GraphPath : layer.GraphPath;
                run.Seed = unchecked(Seed + layer.SeedOffset);
                run.Partitioned = true;
                run.CellSize = layer.CellSize;
                run.Padding = layer.Padding;
            }
        }

        /// <summary>一层的生成：加载、编译、清掉即将被替换的东西、跑、收编。</summary>
        private bool GenerateOne(bool layered)
        {
            var run = _run;

            if (string.IsNullOrWhiteSpace(run.GraphFile))
            {
                Debug.LogWarning($"[PCG] {Name}: no graph file is set{LayerSuffix()}.");
                return false;
            }

            if (!PCGGraphFile.TryLoad(run.GraphFile, out var graph, out string loadError))
            {
                Debug.LogError($"[PCG] {Name}: {loadError}");
                return false;
            }

            // 编译之前先走一遍子图引用：跨两个文件的环在各自文件里看着都合法，
            // 而拉取式执行器会一直递归到栈撑不住。
            string graphName = System.IO.Path.GetFileNameWithoutExtension(PCGGraphFile.ResolvePath(run.GraphFile));
            var subgraphIssues = PCGSubgraphInspector.Inspect(
                graph, graphName,
                name => PCGGraphFile.TryLoad(name, out var child, out _) ? child : null);

            if (PCGSubgraphInspector.HasErrors(subgraphIssues))
            {
                for (int i = 0; i < subgraphIssues.Count; i++)
                {
                    if (subgraphIssues[i].Severity == PCGCompileSeverity.Error)
                        Debug.LogError($"[PCG] {Name}: {subgraphIssues[i]}");
                }

                return false;
            }

            try
            {
                // 只在拓扑变了的时候重编，而且是**逐层**的：两层跑同一张图
                // 就共用那份已编译结果，不同的那张各留各的。
                int hash = PCGGraphCompiler.ComputeStructuralHash(graph);
                if (run.Compiled == null || run.CompiledHash != hash)
                {
                    if (!PCGGraphCompiler.TryCompile(graph, out var compiled))
                    {
                        var message = compiled != null && compiled.Diagnostics != null
                            ? string.Join("\n  ", GetErrorMessages(compiled.Diagnostics))
                            : "unknown reason";

                        Debug.LogError($"[PCG] {Name}: graph failed to compile{LayerSuffix()}:\n  {message}");
                        return false;
                    }

                    run.Compiled = compiled;
                    run.CompiledHash = hash;
                    run.Cache?.InvalidateAll();

                    for (int i = 0; i < (run.Compiled.Diagnostics?.Count ?? 0); i++)
                    {
                        var diagnostic = run.Compiled.Diagnostics[i];
                        if (diagnostic.Severity == PCGCompileSeverity.Warning)
                            Debug.LogWarning($"[PCG] {Name}: {diagnostic}");
                    }
                }

                var context = BuildContext();

                // 地形合成（决定 B：只在描述里写了 `terrain` 时才发生）。它必须发生在**任何采样之前**，
                // 而且写下去的那组数要当场交给上下文当高度来源——引擎托管侧没有高度 getter，读不回来，
                // 不喂的话紧随其后的 biome / water / scatter 会拿旧高度（很多场景里那是零平面）判地貌，
                // 于是树浮在新山谷的半空或埋进新山脊里。Preview 不碰场景，与下面 ClearForRun 的守卫同一个理由。
                string terrainNote = null;

                if (Mode != PCGGenerationMode.Preview)
                {
                    if (PCGTerrainWriter.TryApply(graph.Settings?.Terrain, this, TransformVolumeToWorld(), run.Seed,
                            out var heightSource, out string terrainError))
                    {
                        if (heightSource != null)
                            context.World.HeightSource = heightSource;
                    }
                    else
                    {
                        // 地形没写成**不是**这次生成失败：图的其余部分照跑，只是它看到的是旧高度。
                        // 报警告而不是中断，因为"场景里还没有 PCG 自己的地形"是个可恢复的配置问题，
                        // 把整张图一起丢掉会让设计师连其余部分都看不到。
                        // 这句话同时写进结果对象的告警——只打日志的话它会留在 Debug Log 面板里，
                        // 而"跑完只知道点数不对、看不到为什么"正是最难查的一类。
                        terrainNote = $"terrain synthesis skipped: {terrainError}";
                        Debug.LogWarning($"[PCG] {Name}: {terrainNote}");
                    }
                }

                // 格子规划属于**一次请求**而不属于这次运行：流式中心可能自上次 Generate
                // 之后已经挪了，复用昨天的窗口会清掉并重填错误的
                // 那一圈格子。
                run.Cells = null;
                run.ActiveCells = null;
                run.EvictedCells = null;

                // 记账要排除"已经交给销毁队列、但帧末才消失"的那一批：Clear 的销毁**延迟**到帧末，
                // 所以生成完之后上一批还留在子级里，按前缀数会把它们一起数进去（实测 12 报成 24）。
                //
                // 取集合的**时机**是关键：必须在清理**之后**取。清理才把上一批排进销毁队列，而"这一遍新生成的"
                // 正是队列之外的那些；在清理之前取会因为那一批此刻还没被排队而漏掉它们——实测那样会把 2 个报成 4 个。
                if (Mode != PCGGenerationMode.Preview)
                    ClearForRun(run.Compiled, context);

                PruneScheduledDestroy(GetChildren<Actor>());
                var pendingDestruction = new HashSet<Guid>(_scheduledDestroy);

                // 暴露参数是**逐次运行**叠上去的：已编译图与它的结构哈希
                // 一动不动，于是这个体积的取值永远不会逼着共享的图重编。
                var plan = PCGExecutionPlan.Build(run.Compiled, BuildOverrideTable());
                if (layered && plan != null && plan.Warnings != null && plan.Warnings.Length > 0)
                {
                    for (int i = 0; i < plan.Warnings.Length; i++)
                        plan.Warnings[i] = $"{run.Label}: {plan.Warnings[i]}";
                }

                var executor = new PCGGraphExecutor(run.Compiled, plan);
                var result = ExecuteGraph(executor, context, run.Compiled);
                if (result == null)
                    return false;

                if (result.Cancelled)
                {
                    Debug.LogWarning($"[PCG] {Name}: generation cancelled{LayerSuffix()}.");
                    return false;
                }

                _lastOutput = result.PrimaryOutput;

                // 这里原来有一道「孤立分支」警告（点名接不到任何汇、因此一次都不会被执行的节点）。
                // **它已撤：那条判据恒空，是死码。** 编译器的 `SinkNodes` 定义为"没有下游消费者的节点
                // = 要求值的输出"，所以"接不到汇"的节点自己就是汇，任何无环图都构造不出"够不到汇"——
                // 我写好正向测试去证它，结果是它一个都点不出名（那次失败比成功值钱）。
                // 反向语义已经钉在离屏门里（`SpecTests` 那条"反证"断言），别照着这段注释把它加回来。
                // 真要追"产出被丢掉的沉默"，可判定的形式在**上面一层**：某节点产出了点，
                // 但体积最终采纳的那个输出里没有它——不是"够不到汇"。
                var warnings = MergeWarnings(result.Warnings, terrainNote);

                run.Result = new PCGGenerationResult
                {
                    PointCount = _lastOutput?.Count ?? 0,
                    Milliseconds = result.Milliseconds,
                    Warnings = warnings,
                    TimestampUtcTicks = DateTime.UtcNow.Ticks,
                };

                for (int i = 0; i < run.Result.Warnings.Length; i++)
                    Debug.LogWarning($"[PCG] {Name}: {run.Result.Warnings[i]}");

                AdoptGeneratedActors(run.Result, pendingDestruction);

                Debug.Log($"[PCG] {Name}: generated {run.Result.PointCount} point(s) in {result.Milliseconds:F1} ms" +
                          $"{LayerSuffix()} ({run.Result.SpawnedActors.Count} actor(s), mode {Mode}).");

                if (run.Partitioned && run.Cache != null)
                {
                    // 复用必须看得见："它瞬间就完了"与"它什么都没新产出"在一条
                    // 两样都不报的日志里读起来一样。
                    Debug.Log($"[PCG] {Name}: cells{LayerSuffix()} - {run.Cache.LastRunExecutedCells} executed, " +
                              $"{run.Cache.LastRunReusedCells} reused, {run.Cache.CellCount} stored.");
                }

                if (LogNodeTimings)
                    Debug.Log($"[PCG] {Name}: time went to{LayerSuffix()} " +
                              PCGNodeTimingReport.Format(result.Stats, result.Milliseconds));

                return run.Result.PointCount > 0;
            }
            catch (PCGCompilationException ex)
            {
                Debug.LogError($"[PCG] {Name}: {ex.Message}");
                return false;
            }
            catch (PCGNodeExecutionException ex)
            {
                Debug.LogError($"[PCG] {Name}: node execution failed: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PCG] {Name}: generation failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        /// <summary>多层都跑了时体积自己的结果：总数，加上每一层的警告。</summary>
        private void SummariseLayers()
        {
            var total = new PCGGenerationResult { TimestampUtcTicks = DateTime.UtcNow.Ticks };
            int actors = 0;

            for (int i = 0; i < _runs.Count; i++)
            {
                var result = _runs[i].Result;
                if (result == null)
                    continue;

                total.PointCount += result.PointCount;
                total.Milliseconds += result.Milliseconds;
                actors += result.SpawnedActors.Count;

                var merged = new List<string>(total.Warnings ?? Array.Empty<string>());
                for (int w = 0; w < result.Warnings.Length; w++)
                    merged.Add($"{_runs[i].Label}: {result.Warnings[w]}");
                total.Warnings = merged.ToArray();
            }

            for (int i = 0; i < _runs.Count; i++)
                total.SpawnedActors.AddRange(_runs[i].Result?.SpawnedActors ?? new List<Actor>());

            _result = total;

            Debug.Log($"[PCG] {Name}: {_runs.Count} layer(s) generated {total.PointCount} point(s) in " +
                      $"{total.Milliseconds:F1} ms ({actors} actor(s)).");
        }

        /// <summary>把一句额外告警并进这次运行的告警表（没有额外告警时原样返回）。</summary>
        private static string[] MergeWarnings(string[] warnings, string extra)
        {
            if (string.IsNullOrEmpty(extra))
                return warnings ?? Array.Empty<string>();

            var merged = new List<string>(warnings ?? Array.Empty<string>()) { extra };
            return merged.ToArray();
        }

        /// <summary>一行日志来自哪一层；没有分层的体积什么都不加。</summary>
        private string LayerSuffix() =>
            _runs.Count > 1 && _run != null ? $" [{_run.Label}]" : string.Empty;

        /// <summary>
        /// 围绕被跟随的 Actor 重切流式窗口，如果这一帧该请求就生成一次。
        /// 返回是否真跑了一次生成。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 这是个方法而不是 <c>OnUpdate</c> 重写，因为它本来就不是：Flax 把更新
        /// 钩子放在 <see cref="Script"/> 上，而体积是个 <see cref="Actor"/>。所以心跳来自
        /// 外面——<see cref="PCGStreamingDriver"/> 是那个开箱即用的组件，
        /// 而一个本来就在 tick 玩家控制器的游戏可以自己调这里。
        /// </para>
        /// <para>
        /// 空闲帧只花一次位置读取与两次比较；判定本身在
        /// <see cref="PCGStreamTrigger"/>，已离线断言。没有任何东西等定时器：一次请求要的是
        /// "被跟随的点此刻真正所在的那个窗口"，所以站着不动的玩家不生成任何东西；而对延后
        /// 格子的重试循环在"再问一次也不会让积压变少"的那一刻停下。重入的
        /// <c>Generate</c> 会被它自己的守卫拒掉，所以慢的一层没法在这一个后面叠请求。
        /// </para>
        /// </remarks>
        public bool UpdateStreaming()
        {
            if (!Streaming || IsGenerating)
                return false;

            var viewers = CollectViewers();
            if (viewers.Count == 0)
                return false;

            float step = PCGStreamTrigger.FollowStep(StreamFollowStep, PartitionCellSize, Layers);

            // 每个观察者记一格：只要**任一**观察者进了不同的格子就该请求，
            // 而观察者数量变了也算一次，因为它们描述的窗口刚变了。
            if (_viewerCellsX.Length != viewers.Count)
            {
                _viewerCellsX = new int[viewers.Count];
                _viewerCellsZ = new int[viewers.Count];

                for (int i = 0; i < viewers.Count; i++)
                {
                    _viewerCellsX[i] = int.MinValue;
                    _viewerCellsZ[i] = int.MinValue;
                }
            }

            bool crossed = false;

            for (int i = 0; i < viewers.Count; i++)
            {
                var cell = PCGStreamTrigger.CellOf(viewers[i], step);

                if (cell.X != _viewerCellsX[i] || cell.Z != _viewerCellsZ[i])
                    crossed = true;

                _viewerCellsX[i] = cell.X;
                _viewerCellsZ[i] = cell.Z;
            }

            int pending = PendingCells();

            // -1 表示"还没量过"，也就是刚跨完格、以及第一次
            // 带预算的请求刚发出的状态：积压正是那次请求造成的，拿它去和请求之前的数
            // （零）比会被读成**没有**进展，延后的格子就永远填不上了。
            bool madeProgress = _deferredAtLastRequest < 0 || pending < _deferredAtLastRequest;

            if (!PCGStreamTrigger.ShouldRequest(crossed, pending, madeProgress))
                return false;

            _deferredAtLastRequest = crossed ? -1 : pending;

            StreamCenter = viewers[0];
            Generate();
            return true;
        }

        /// <summary>
        /// 窗口围绕哪些位置切：有被跟随的 Actor 时就是它们，否则什么都没有。
        /// </summary>
        /// <remarks>
        /// 复用一个缓冲，因为这里是逐帧、逐请求被调用的；调用方立刻读、
        /// 不留着。
        /// </remarks>
        [NoSerialize]  // 运行时缓冲：每帧/每次请求重填，不该进场景
        private readonly List<Int2> _chunkBuffer = new List<Int2>();
        [NoSerialize]  // 运行时缓冲：每帧/每次请求重填，不该进场景
        private readonly List<Int2> _visibleChunks = new List<Int2>();

        /// <summary>
        /// 跑一次窗口选择，优先用世界自己的可见分块清单，而不是推导出来的窗口。
        /// </summary>
        /// <remarks>
        /// 两个调用点（粗窗口与细窗口）都走这里，于是一个体积不可能
        /// 用**两套规则**分别决定它的两层。
        /// </remarks>
        private int SelectWindow(
            IReadOnlyList<PCGPartitionCell> cells,
            IReadOnlyList<Vector3> viewers,
            float radius,
            float keep,
            List<PCGPartitionCell> active,
            List<PCGPartitionCell> evicted,
            int budget,
            Predicate<PCGPartitionCell> needsWork)
        {
            if (StreamChunkSource == null || !(StreamChunkSize > 0.0f))
            {
                return PCGCellStreaming.SelectAround(
                    cells, viewers, radius, keep, active, evicted, budget, needsWork, StreamChunkSize);
            }

            // 按来源真正写入的量钳制：缓冲在请求之间复用，所以信那个
            // 返回值就会把上一次请求的块重放一遍。这条规则住在 PCGStreamChunks 里，
            // 那里已离线断言；一个什么都没写的来源就是"什么都没加载的世界"，那是一个空
            // 窗口，而不是退回去按半径推。
            PCGStreamChunks.FromSource(StreamChunkSource, _chunkBuffer, _visibleChunks);

            return PCGCellStreaming.SelectChunks(
                cells, _visibleChunks, StreamChunkSize, viewers, active, evicted, budget, needsWork, StreamChunkBorder);
        }

        private List<Vector3> CollectViewers()
        {
            _viewerBuffer.Clear();

            // 世界自己的答案优先：一个知道自己加载了哪些分块的游戏，
            // 不该还得同步维护一份 Actor 列表。
            if (StreamViewerSource != null)
            {
                PCGStreamViewers.FromSource(StreamViewerSource, StreamMaxViewers, _viewerBuffer, _reducedViewers);
                return _reducedViewers;
            }

            var targets = StreamFollowTargets;

            if (targets != null)
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    if (targets[i] != null)
                        _viewerBuffer.Add(targets[i].Position);
                }
            }

            // 单个目标是"只有一个观察者"这个特例，不是第二套机制：一个一直只跟一个
            // 玩家的体积照旧一样工作。
            if (_viewerBuffer.Count == 0 && StreamFollowTarget != null)
                _viewerBuffer.Add(StreamFollowTarget.Position);

            // 上限最后才施加，于是"挑出的那批"是从世界里真正有的所有观察者里挑的，
            // 而不是从先被设上的那几个里挑的。
            PCGStreamViewers.Reduce(_viewerBuffer, StreamMaxViewers, _reducedViewers);

            return _reducedViewers;
        }

        /// <summary>这个体积各层在当前窗口上还欠多少格，按层求和。</summary>
        private int PendingCells()
        {
            int pending = 0;

            for (int i = 0; i < _runs.Count; i++)
                pending += _runs[i].DeferredCells;

            return pending;
        }

        /// <summary>
        /// 销毁这个体积生成的一切，手摆的场景内容一点不碰。
        /// </summary>
        /// <returns>被销毁的 Actor 个数。</returns>
        public int Clear()
        {
            int removed = 0;

            // 归属是**结构性**的：生成出来的物件挂在这个体积之下，所以"清空"就是"销毁我的孩子"。
            // 这一点能扛过场景重载，因为父子关系是**存下来的场景**的一部分，
            // 而不像内存里的追踪表那样随进程消失。
            //
            // 用 Tag 查找是那个一眼看去更顺的替代方案，这里行不通：Level.FindActors 收的是
            // Tag，而 Flax 的 Tag 是项目标签表里的一个下标，没法从任意
            // 字符串在运行期造出来，所以"每体积一个 Tag"这条路走不了。
            var children = GetChildren<Actor>();
            if (children != null)
            {
                PruneScheduledDestroy(children);

                for (int i = children.Length - 1; i >= 0; i--)
                {
                    var actor = children[i];
                    if (actor == null || _scheduledDestroy.Contains(actor.ID))
                        continue;

                    _scheduledDestroy.Add(actor.ID);
                    Actor.Destroy(actor);
                    removed++;
                }
            }

            // 存下来的格子描述的是**曾经**存在于场景里的物件；清空之后再用它们，
            // 会交回一批 Actor 早就不在那儿的点。
            for (int i = 0; i < _runs.Count; i++)
                _runs[i].Cache?.InvalidateAll();

            if (_result?.SpawnedActors != null)
                _result.SpawnedActors.Clear();

            _lastOutput = null;
            _result = null;

            return removed;
        }

        /// <summary>
        /// 丢掉已编译图与存下来的分区结果，强制一次全量重生成。
        /// </summary>
        /// <remarks>
        /// 缓存一并丢掉，因为一个格子的点只对"做出它们的那个图"有效，而这一次
        /// 调用就是调用者在说"那个图已经不是我想要的了"。
        /// </remarks>
        public void InvalidateCompilation()
        {
            for (int i = 0; i < _runs.Count; i++)
            {
                _runs[i].Compiled = null;
                _runs[i].CompiledHash = 0;
                _runs[i].Cache?.InvalidateAll();
            }
        }

        /// <summary>
        /// 跑一次图；<see cref="Partitioned"/> 置位时逐格各跑一次。
        /// </summary>
        /// <returns>运行结果；分区规划不出来时为 null（已记日志）。</returns>
        private PCGExecutionResult ExecuteGraph(PCGGraphExecutor executor, PCGExecutionContext context, PCGCompiledGraph compiled)
        {
            var run = _run;
            if (run == null || !run.Partitioned)
                return executor.Execute(context);

            float padding = run.Padding >= 0.0f
                ? run.Padding
                : PCGPartitionPlanner.SuggestedPaddingForGraph(compiled.Nodes);

            var cells = EnsureCells(compiled, context);
            if (cells == null)
                return null;

            // 图上的预算是按**一次生成**写的限额，所以分区运行把它当作**合计**上限；
            // 若按格各算一次，场景里就会悄悄多出"格数 × 预算"个 Actor。
            int budget = compiled.Source?.Settings?.MaxPointBudget ?? 0;

            if (run.Hierarchy != null)
                return ExecuteHierarchical(executor, context, run, budget,
                                           PartitionRequest(run, compiled, padding));

            // 流式把请求收窄到窗口；不开流式时窗口就是整份规划。
            var window = run.ActiveCells ?? cells;

            return PCGPartitionRunner.Run(executor, context, window, budget, PartitionRequest(run, compiled, padding));
        }

        /// <summary>
        /// 跑那两层：粗层只判定不建东西，细层在判定为活的地方建。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 粗层逐格跑、并且用 <see cref="PCGExecutionContext.AsDryRun"/>，于是它
        /// 产出点、不产 Actor：这正是"空区域很便宜"的原因——几次粗跑
        /// 而不是几百次细跑——也是两层不会互相覆盖的原因，
        /// 因为一个粗格与一个细格可能带相同的网格下标。
        /// </para>
        /// <remarks>
        /// <para>
        /// 级联的走法：第 0 层在它自己的窗口上被问一遍，之后每一层只在上一层
        /// 判定为活的那些格子下面被问，而只有真正生成的那一层把结果交回——
        /// 判定层产出的点是**判定**，不是输出。
        /// </para>
        /// <para>
        /// 生成的格子必须同时满足"父格是活的"与"在窗口内"。第二个条件
        /// 不是走形式：窗口正是 <c>ClearStreamingWindow</c> 已经据以工作的东西，与它求交
        /// 才是"清理与执行逐格一致"的保证。
        /// </para>
        /// </remarks>
        private PCGExecutionResult ExecuteHierarchical(
            PCGGraphExecutor executor, PCGExecutionContext context, LayerRun run, int budget,
            PCGPartitionCacheRequest request)
        {
            var dry = context.AsDryRun();
            var live = new HashSet<(int X, int Z)>();
            var under = new List<PCGPartitionCell>();
            var single = new List<PCGPartitionCell>(1);

            int decidingLevels = run.Hierarchy.LevelCount - 1;
            int asked = 0;
            int decidedPoints = 0;
            var trace = new System.Text.StringBuilder();

            for (int level = 0; level < decidingLevels; level++)
            {
                var levelWindow = run.DecidingWindows[level];

                if (level == 0)
                {
                    under.Clear();
                    under.AddRange(levelWindow);
                }
                else
                {
                    // 这一层里父格活着的那些格子，再按这一层自己的窗口筛一遍。
                    int candidates = run.Hierarchy.CellsUnder(level, live, under);

                    if (candidates > 0 && levelWindow.Count > 0)
                    {
                        for (int i = under.Count - 1; i >= 0; i--)
                        {
                            if (!ContainsCell(levelWindow, under[i]))
                                under.RemoveAt(i);
                        }
                    }
                }

                live = new HashSet<(int X, int Z)>();
                int points = 0;

                for (int i = 0; i < under.Count; i++)
                {
                    single.Clear();
                    single.Add(under[i]);

                    var result = PCGPartitionRunner.Run(executor, dry, single, 0, request);
                    int produced = result.PrimaryOutput?.Count ?? 0;
                    points += produced;

                    if (produced >= HierarchicalMinCoarsePoints)
                        live.Add((under[i].IndexX, under[i].IndexZ));
                }

                asked += under.Count;
                decidedPoints += points;

                if (trace.Length > 0)
                    trace.Append("; ");

                trace.Append($"L{level} {live.Count}/{under.Count} live, {points} point(s)");
            }

            // 最细那层是真的跑，在上一层判定为活的格子下面跑。
            run.Hierarchy.CellsUnder(decidingLevels, live, under);

            var generationWindow = run.ActiveCells;
            var execute = new List<PCGPartitionCell>();

            for (int i = 0; i < under.Count; i++)
            {
                if (generationWindow == null || ContainsCell(generationWindow, under[i]))
                    execute.Add(under[i]);
            }

            Debug.Log($"[PCG] {Name}: hierarchical run over {run.Hierarchy.LevelCount} level(s) - {trace}; " +
                      $"generating {execute.Count} of {under.Count} cell(s) of {run.Hierarchy.Generation.CellSize:0.##} " +
                      $"({asked} deciding run(s), {decidedPoints} point(s)){LayerSuffix()}.");

            // 判定层的点是**判定**而不是输出：它们是对同一张图的粗采样，
            // 把它们交下去等于用错误的分辨率又把物件摆一遍。
            return PCGPartitionRunner.Run(executor, context, execute, budget, request);
        }

        /// <summary>一个格子清单里是否已经有这个格子（按网格下标比）。</summary>
        private static bool ContainsCell(List<PCGPartitionCell> cells, in PCGPartitionCell cell)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].IndexX == cell.IndexX && cells[i].IndexZ == cell.IndexZ)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 这一次运行的复用请求；图不可缓存时是一份"未设置"的。
        /// </summary>
        /// <summary>
        /// 清掉这次运行即将替换的东西：**全部**，但开着格子复用时例外——那时只清
        /// 真正要重生成的那些格子。
        /// </summary>
        /// <remarks>
        /// 复用的两边必须逐格一致。配对一旦陈旧，要么销毁掉运行器马上要从
        /// 缓存交回点的那些物件，要么物件还立着而它的格子又在上面
        /// 重生成了一遍。
        /// </remarks>
        private void ClearForRun(PCGCompiledGraph compiled, PCGExecutionContext context)
        {
            var run = _run;
            if (run == null || !run.Partitioned)
            {
                Clear();
                return;
            }

            // 流式有它自己的清理：一次请求碰一个窗口，而窗口之外的格子、
            // 包括保留的那一圈，必须原样不动。
            if (Streaming)
            {
                ClearStreamingWindow(compiled, context);
                return;
            }

            var request = PartitionRequest(run, compiled, PartitionPaddingValue(context));

            if (!request.IsEnabled)
            {
                Clear();
                return;
            }

            var cells = EnsureCells(compiled, context);
            if (cells == null)
            {
                // 规划器会在这次运行里自己报出问题；把一切都清掉是安全的答法。
                Clear();
                return;
            }

            var dirty = request.Cache.DirtyCells(cells, request, context.Seed);
            if (dirty.Count == cells.Count)
            {
                Clear();
                return;
            }

            int removed = 0;
            for (int i = 0; i < dirty.Count; i++)
                removed += ClearCell(dirty[i], run.Index);

            Debug.Log($"[PCG] {Name}: kept {cells.Count - dirty.Count} fresh cell(s), cleared {removed} actor(s) " +
                      $"from {dirty.Count} dirty cell(s).");
        }

        /// <summary>
        /// 这次请求所在区域的格子：规划一次，开着流式时再切成流式窗口。
        /// </summary>
        /// <remarks>
        /// 清理与执行都读这份清单，而且必须逐格一致——清理那一侧看见
        /// 而运行器看不见的窗口，就是其他每条复用路径都在防的那种陈旧配对。惰性
        /// 规划，于是一次从不碰场景的预览跑也拿得到它的窗口；而逐请求重置，
        /// 因为中心可能在两次 Generate 之间挪过。
        /// </remarks>
        /// <returns>这个区域的格子；规划失败时为 null（已记日志）。</returns>
        private List<PCGPartitionCell> EnsureCells(PCGCompiledGraph compiled, PCGExecutionContext context)
        {
            var run = _run;
            if (run == null)
                return null;

            if (run.Cells != null)
                return run.Cells;

            float padding = run.Padding >= 0.0f
                ? run.Padding
                : PCGPartitionPlanner.SuggestedPaddingForGraph(compiled?.Nodes);

            run.Hierarchy = null;

            if (Hierarchical)
            {
                float coarseSize = HierarchicalCoarseSize > 0.0f
                    ? HierarchicalCoarseSize
                    : run.CellSize * 4.0f;

                int levels = Math.Clamp(HierarchicalLevels, 2, PCGPartitionHierarchyPlanner.MaxLevels);
                var sizes = PCGPartitionHierarchyPlanner.GeometricSizes(coarseSize, run.CellSize, levels);

                if (sizes.Length == 0)
                {
                    // 尺寸是体积自己的那两个旋钮，所以这里是设计师把一个粗格尺寸敲成了
                    // 其实并不比格子尺寸更粗——值得点出来，而不是悄悄钳一下。
                    Debug.LogError($"[PCG] {Name}: hierarchical generation needs the coarsest size ({coarseSize:0.##}) " +
                                   $"to be larger than the cell size ({run.CellSize:0.##}).");
                    return null;
                }

                run.Hierarchy = PCGPartitionHierarchyPlanner.Plan(context.GenerationBounds, sizes, padding,
                    out string hierarchyError);

                if (run.Hierarchy == null)
                {
                    Debug.LogError($"[PCG] {Name}: {hierarchyError}");
                    return null;
                }

                // 这次运行的格子清单就是"生成层"：下游的一切——窗口、缓存键、
                // 清理——本来就逐格工作，而上面那些层只是收窄"哪些格子要跑"。
                run.Cells = run.Hierarchy.Generation.Cells;
            }
            else
            {
                run.Cells = PCGPartitionPlanner.Plan(context.GenerationBounds, run.CellSize, padding, out string planError);
                if (run.Cells == null)
                {
                    Debug.LogError($"[PCG] {Name}: {planError}");
                    return null;
                }
            }

            Debug.Log($"[PCG] {Name}: {run.Cells.Count} cell(s) of {run.CellSize:0.##} with {padding:0.##} " +
                      $"padding{LayerSuffix()}.");

            // 有被跟随的 Actor 时观察者就是它们；一个都没有的体积退回到调用者
            // 手拨的中心，那正是跟随目标出现之前驱动窗口的方式。
            var viewers = CollectViewers();
            if (viewers.Count == 0)
                viewers.Add(StreamCenter);

            if (run.Hierarchy != null)
            {
                // 每个判定层一个窗口，用与最细那层相同的观察者与半径切，于是一个格子是
                // 在玩家附近被判定，而不是横跨整个区域。各层是彼此独立的
                // 网格，所以每层都要各自选一次——粗网格的下标说不清细网格的事。
                run.DecidingWindows = new List<List<PCGPartitionCell>>(run.Hierarchy.LevelCount - 1);

                float keepRadius = StreamKeepRadius >= 0.0f
                    ? StreamKeepRadius
                    : StreamRadius + 2.0f * run.CellSize;

                for (int level = 0; level < run.Hierarchy.LevelCount - 1; level++)
                {
                    var window = new List<PCGPartitionCell>();
                    run.DecidingWindows.Add(window);

                    if (Streaming)
                        SelectWindow(run.Hierarchy.Levels[level].Cells, viewers, StreamRadius, keepRadius,
                                     window, new List<PCGPartitionCell>(), 0, null);
                    else
                        window.AddRange(run.Hierarchy.Levels[level].Cells);
                }
            }

            if (!Streaming)
            {
                run.ActiveCells = run.Cells;
                run.EvictedCells = null;
                run.DeferredCells = 0;
                return run.Cells;
            }

            float keep = StreamKeepRadius >= 0.0f ? StreamKeepRadius : StreamRadius + 2.0f * run.CellSize;

            // 预算只有对着缓存才有意义：点被存下来的格子不花任何工作，所以
            // 它可以免费交给请求，限额于是落到那些真正要生成的格子上。关掉
            // 复用时没有"免费"这回事，预算就退回到"离中心最近的 K 格"。
            var request = PartitionRequest(run, compiled, padding);
            Predicate<PCGPartitionCell> needsWork = null;
            if (StreamCellsPerRequest > 0 && request.IsEnabled)
            {
                var cache = request.Cache;
                needsWork = cell => !cache.IsFresh(cell, request.SignatureFor(cell, context.Seed));
            }

            run.ActiveCells = new List<PCGPartitionCell>();
            run.EvictedCells = new List<PCGPartitionCell>();

            int deferred = SelectWindow(run.Cells, viewers, StreamRadius, keep,
                                        run.ActiveCells, run.EvictedCells,
                                        StreamCellsPerRequest, needsWork);
            run.DeferredCells = deferred;

            Debug.Log($"[PCG] {Name}: streaming window around {viewers.Count} of {_viewerBuffer.Count} viewer(s) - " +
                      $"{run.ActiveCells.Count} of " +
                      $"{run.Cells.Count} cell(s) within {StreamRadius:0.##}, " +
                      $"{run.EvictedCells.Count} past keep radius {keep:0.##}" +
                      (deferred > 0
                          ? $", {deferred} deferred to a later request (budget {StreamCellsPerRequest})"
                          : string.Empty) +
                      ".");

            return run.Cells;
        }

        /// <summary>
        /// 清掉一次流式请求要替换的东西：窗口里那些脏格，加上它交回的格子。
        /// </summary>
        /// <remarks>
        /// "把一切都清掉"是错的，哪怕整个区域确实不是当下：保留圈里的格子
        /// 既不会被重生成（它在窗口外）也不会被驱逐（它在保留半径内），所以
        /// 销毁它的物件会留下一个下一个请求没有理由去填的洞。被驱逐的格子
        /// 的点要跟着它的 Actor 一起走，否则下一个走到它的请求会复用到已经不在的物件。
        /// </remarks>
        private void ClearStreamingWindow(PCGCompiledGraph compiled, PCGExecutionContext context)
        {
            var run = _run;
            var cells = EnsureCells(compiled, context);
            if (cells == null)
            {
                Clear();
                return;
            }

            var request = PartitionRequest(run, compiled, PartitionPaddingValue(context));
            var active = run.ActiveCells;
            var evicted = run.EvictedCells;

            int removed = 0;

            if (!request.IsEnabled)
            {
                // 没有复用，于是窗口里每一格都即将从零重生成。
                for (int i = 0; i < active.Count; i++)
                    removed += ClearCell(active[i], run.Index);
            }
            else
            {
                var dirty = request.Cache.DirtyCells(active, request, context.Seed);
                for (int i = 0; i < dirty.Count; i++)
                    removed += ClearCell(dirty[i], run.Index);
            }

            for (int i = 0; i < evicted.Count; i++)
            {
                removed += ClearCell(evicted[i], run.Index);
                request.Cache?.Evict(evicted[i].IndexX, evicted[i].IndexZ);
            }

            Debug.Log($"[PCG] {Name}: streaming cleared {removed} actor(s) - {active.Count} cell(s) in the " +
                      $"window, {evicted.Count} handed back.");
        }

        /// <summary>销毁这个体积为某个分区格子生成的那些物件。</summary>
        private int ClearCell(in PCGPartitionCell cell, int layerIndex)
        {
            int removed = 0;
            var children = GetChildren<Actor>();
            if (children == null)
                return 0;

            PruneScheduledDestroy(children);

            for (int i = children.Length - 1; i >= 0; i--)
            {
                var actor = children[i];
                if (actor == null || !PCGSpawnNaming.IsGeneratedName(actor.Name))
                    continue;

                if (_scheduledDestroy.Contains(actor.ID))
                    continue;

                bool mine = PCGSpawnNaming.IsGeneratedName(actor.Name)
                    ? PCGSpawnNaming.BelongsToCell(actor.Name, layerIndex, cell.IndexX, cell.IndexZ)
                    : IsCellsFoliageHost(actor.Name, layerIndex, cell.IndexX, cell.IndexZ);
                if (!mine)
                    continue;

                _scheduledDestroy.Add(actor.ID);
                Actor.Destroy(actor);
                removed++;
            }

            return removed;
        }

        /// <summary>
        /// 某个节点为某一格建出来的植被宿主为 true，名字形如 PCG_Foliage_&lt;节点&gt;_&lt;x&gt;x&lt;z&gt;z。
        /// </summary>
        /// <remarks>
        /// 这个 token 里**前导下划线是有用的**：没有它，格子 (1,12) 也会匹配到
        /// 属于 (11,12) 的宿主，于是一个格子的清理会把另一个格子的整片散布销毁掉。
        /// </remarks>
        private static bool IsCellsFoliageHost(string name, int layerIndex, int indexX, int indexZ) =>
            name.StartsWith(PCG.Nodes.PCGSpawnFoliageNode.ActorNamePrefix, System.StringComparison.Ordinal) &&
            name.Contains("_l" + layerIndex + "_", System.StringComparison.Ordinal) &&
            name.Contains("_" + indexX + "x" + indexZ + "z", System.StringComparison.Ordinal);

        private float PartitionPaddingValue(PCGExecutionContext context)
        {
            // 请求只需要规划器将要施加的那个 padding 值；扫一遍节点很便宜、
            // 答案也一样，所以这里是重算一遍而不是从 ExecuteGraph 一路传下来。
            var run = _run;
            float padding = run != null ? run.Padding : PartitionPadding;
            if (padding >= 0.0f)
                return padding;

            // 运行之外没有已编译图就意味着没有节点可扫，而规划器自己的默认值
            // 就是那个调用者的答案——体积不留自己的已编译图，因为分了层之后
            // 每次运行各自持有它编出来的那份。
            return PCGPartitionPlanner.SuggestedPaddingForGraph(run?.Compiled?.Nodes);
        }

        private PCGPartitionCacheRequest PartitionRequest(LayerRun run, PCGCompiledGraph compiled, float padding)
        {
            // **写场景的图不参与缓存复用**。这一条此前只写在 `PCGPartitionCache` 的注释里、没有任何实现
            // （注释说判据是 `IsThreadSafe == false`，而全仓没有一处消费它）——后果实测出来了：同一个
            // 分层+分区体积、同一张图、同一个种子连跑三次，留在场景里的 Actor 是 **120 / 60 / 72**，
            // 而点数每次都是 784。原因是复用把一部分格子跳过了：那些格的物件这一轮**根本没建**，
            // 于是场景里剩什么取决于上一轮的执行/复用比例。
            //
            // 判据用**权威的那一个**：`PCGGraphEffects.GraphWritesScene`（编辑器「试跑」的闸门与生成器
            // 清单都用它，离屏还有 `EverySpawnerIsListed` 盯着漏登记）。另立一份"哪些节点写场景"的
            // 知识迟早会漂——这一处就是这么漂掉一整条规则的。
            if (compiled != null && PCGGraphEffects.GraphWritesScene(compiled.Nodes))
            {
                run.ReuseRefused = true;
                return new PCGPartitionCacheRequest(null, 0, 0, 0, padding);
            }

            run.ReuseRefused = false;

            if (run.Cache == null)
                run.Cache = new PCGPartitionCache();

            return new PCGPartitionCacheRequest(
                run.Cache, run.CompiledHash, OverrideVersion(), EffectiveWorldVersion(), padding);
        }

        /// <summary>
        /// 这个体积覆盖表的一个 token，只在表真的不同时才变。
        /// </summary>
        /// <remarks>
        /// 按规范化字符串比较而不是取哈希：这里一旦碰撞就意味着陈旧参数被
        /// 永远复用下去，而那正是这个版本号存在的目的所在。
        /// </remarks>
        private int OverrideVersion()
        {
            var builder = new System.Text.StringBuilder();
            var overrides = ParameterOverrides;

            for (int i = 0; overrides != null && i < overrides.Length; i++)
            {
                var item = overrides[i];
                builder.Append(item.ParameterName).Append('=').Append(item.Value).Append(';');
            }

            string signature = builder.ToString();
            if (signature != _overrideSignature)
            {
                _overrideSignature = signature;
                _overrideVersion++;
            }

            return _overrideVersion;
        }

        private PCGExecutionContext BuildContext()
        {
            var world = new PCGWorldContext();
            world.CollectTerrainsFromScene();

            world.ActorResolver = id => Level.FindActor(id);
            world.ActorNameResolver = name => Level.FindActor(name);

            // 物理场景是节点唯一无法自己够到的世界服务：Physics.RayCast 需要
            // 引擎的场景，所以离屏运行既没东西可装、也没办法伪造。把这次调用
            // 经上下文递进来，既让查询参数与命中前后的算术可测，也把
            // 引擎调用本身留在唯一被允许做出它的地方。在这个线程上是安全的，因为
            // 执行器在场景自己的线程上跑各格，而射线节点没有标线程安全。
            world.RayCaster = (origin, direction, maxDistance, layerMask, hitTriggers) =>
            {
                return Physics.RayCast(origin, direction, out RayCastHit hit, maxDistance, layerMask, hitTriggers)
                    ? hit
                    : (RayCastHit?)null;
            };

            // 每次运行快照一次，而不是每次调用一次：分区运行会**逐格**问这个来源，而每次都枚举
            // 一遍关卡会把"读一下标记物"变成整张图里最贵的事。这份
            // 快照就在这里取，在场景线程上，那也是唯一被允许枚举的线程。
            var actors = new List<PCGActorInfo>();
            var levelActors = Level.GetActors<Actor>();

            if (levelActors != null)
            {
                for (int i = 0; i < levelActors.Length; i++)
                {
                    var actor = levelActors[i];

                    if (actor == null || !actor.IsActiveInHierarchy)
                        continue;

                    actors.Add(new PCGActorInfo
                    {
                        Id = actor.ID,
                        Name = actor.Name,
                        Tag = actor.Tag,
                        LayerName = actor.LayerName,
                        Position = actor.Position,
                        Rotation = actor.Orientation,
                        Scale = new Vector3(actor.Scale.X, actor.Scale.Y, actor.Scale.Z),
                    });
                }
            }

            world.ActorSource = into =>
            {
                into.Clear();
                into.AddRange(actors);
                return actors.Count;
            };

            // 高度查询：路径规划与水文都要问"这片地多高"，而它们不该各自去读一遍地形。
            // 采样器按地形缓存一次（它会缓存自己走过的地形块），模式取高度场插值——那是最便宜的一条路，
            // 而且对"扫一片区域算水流"这种用法足够；拿不到地形或该处无地形时返回 null，由节点按契约处理。
            var heightSamplers = new Dictionary<Guid, PCGTerrainSampler>();

            world.HeightSource = (x, z) =>
            {
                var terrain = world.FindTerrainAt(new Vector3(x, 0.0f, z));

                if (terrain == null)
                    return null;

                if (!heightSamplers.TryGetValue(terrain.ID, out var sampler))
                {
                    sampler = new PCGTerrainSampler(terrain, PCGTerrainCalibrationStore.Get(terrain));
                    heightSamplers.Add(terrain.ID, sampler);
                }

                // 从地形包围盒顶端往下问：低点也能被问到，而不会被"起点已在地面以下"错过。
                float startY = terrain.Box.Maximum.Y + 100.0f;
                var sample = sampler.Sample(x, z, PCGTerrainSampleMode.HeightfieldInterpolated, startY, true);

                return sample.Hit ? sample.Height : (float?)null;
            };

            return new PCGExecutionContext
            {
                Seed = _run?.Seed ?? Seed,
                GenerationBounds = TransformVolumeToWorld(),
                SourceBounds = TransformVolumeToWorld(),

                // 整块运行也要按体积裁剪：见 PCGExecutionContext.ClipRegion 的注解——不裁的话"开不开分区"
                // 会变成两套内容（实测同一个洞整块 12 件、分区 9 件）。
                ClipRegion = TransformVolumeToWorld(),
                World = world,
                DebugMode = DebugDrawPoints ? PCGDebugMode.PointsDensity : PCGDebugMode.None,

                // 预览是关于场景的一句承诺，而这就是兑现它的东西：生成器看到这个标记就把
                // 点原样传下去，什么都不建。
                DryRun = Mode == PCGGenerationMode.Preview,

                // 正常使用时为 null；想要这次运行逐节点报告的调用者会设置它。
                Inspection = NodeInspection,
                Source = this,
                GraphName = System.IO.Path.GetFileNameWithoutExtension(PCGGraphFile.ResolvePath(_run?.GraphFile ?? GraphPath)),
                LayerIndex = _run?.Index ?? 0,
                SubgraphLoader = name =>
                {
                    // 子图的解析方式与这个体积自己的图完全一样，于是一个叫
                    // "rocks" 的子图，行为等同于把 "rocks" 放到另一个体积上。
                    return PCGGraphFile.TryLoad(name, out var child, out _) ? child : null;
                },
            };
        }

        /// <summary>
        /// 把 <see cref="ParameterOverrides"/> 变成执行计划消费的那张按名字键控的表。
        /// </summary>
        /// <remarks>
        /// 同名时后面的条目压过前面的：检查器里的数组是用户排的顺序，
        /// 而一个重名的条目应当表现得像"我最后敲的那一个"，既不该报错，也不该
        /// 变成字典那随意的插入顺序。
        /// </remarks>
        private Dictionary<string, PCGParameterValue> BuildOverrideTable()
        {
            if (ParameterOverrides == null || ParameterOverrides.Length == 0)
                return null;

            var table = new Dictionary<string, PCGParameterValue>(StringComparer.Ordinal);

            for (int i = 0; i < ParameterOverrides.Length; i++)
            {
                var overrideValue = ParameterOverrides[i];
                if (string.IsNullOrEmpty(overrideValue.ParameterName))
                {
                    Debug.LogWarning($"[PCG] {Name}: parameter override {i} has no name; ignored.");
                    continue;
                }

                table[overrideValue.ParameterName] = overrideValue.Value;
            }

            return table.Count > 0 ? table : null;
        }

        /// <summary>
        /// 把局部空间的 <see cref="Volume"/> 换算成世界空间的盒子。
        /// </summary>
        public BoundingBox TransformVolumeToWorld()
        {
            return BoundingBox.Transform(Volume, Transform);
        }

        /// <summary>
        /// 把这次运行产出的 Actor 记进 <paramref name="result"/>：数本体积之下、名字带产出前缀、
        /// 且不在 <paramref name="pendingDestruction"/> 里的孩子。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 生成器节点把物件**直接挂在体积之下**——它们的父级取自上下文里的宿主，也就是这个体积本身
        /// （见各 <c>Spawn*</c> 节点的 <c>ResolveParent</c>）——所以产出就在自己的子级里。
        /// 早先这里只扫场景根，于是"点数有、Actor 数永远是零"：读数错了，而
        /// <see cref="GeneratedActorCount"/> 与属性面板的那行话都是照着这个数报的。属性面板那行
        /// "0 个 Actor"正是它，而不是生成没发生。
        /// </para>
        /// <para>
        /// <paramref name="pendingDestruction"/> 是这一遍开始前就在的孩子：清理那一侧的销毁要
        /// 等到帧末，所以它们此刻还在子级里，不排掉就会把上一批算成这一批的产出（12 报成 24）。
        /// </para>
        /// <para>
        /// 这里**不再**去场景根上按前缀认领：那是生成器还没有宿主时的老办法，而它有一个坏副作用——
        /// 任何一个体积生成时都会把别人落在根上的产出挂到自己名下，于是数字被算重、而
        /// <see cref="Clear"/> 会把别人的产出也一起销毁。产出归属是结构性的（谁的孩子就是谁的）。
        /// </para>
        /// <para>
        /// 记账写进**这次运行**的结果对象，而不是字段 <c>_result</c>：后者要到整个 <see cref="Generate"/>
        /// 收尾时才指向这次运行的结果，在这里读到的还是上一次那个（首次生成时是 null）。
        /// </para>
        /// </remarks>
        private void AdoptGeneratedActors(PCGGenerationResult result, HashSet<Guid> pendingDestruction)
        {
            if (result == null)
                return;

            var children = GetChildren<Actor>();

            if (children == null)
                return;

            for (int i = 0; i < children.Length; i++)
            {
                var actor = children[i];
                if (actor == null)
                    continue;

                // 只认这个插件产出的 Actor，靠共享前缀辨认：手工挂在体积之下的东西不算产出。
                if (!PCGSpawnNaming.IsGeneratedName(actor.Name))
                    continue;

                if (pendingDestruction != null && pendingDestruction.Contains(actor.ID))
                    continue;

                result.SpawnedActors.Add(actor);
            }
        }

        /// <summary>
        /// 把 <see cref="_scheduledDestroy"/> 修到"只在现在的孩子们里"——真正消失的条目就此丢掉，
        /// 集合于是不会随场景寿命一直长大；留下来的正是那些还没被引擎删掉的（帧末才销毁的）。
        /// </summary>
        private void PruneScheduledDestroy(Actor[] children)
        {
            if (_scheduledDestroy.Count == 0)
                return;

            if (children == null)
            {
                _scheduledDestroy.Clear();
                return;
            }

            var live = new HashSet<Guid>();

            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] != null && _scheduledDestroy.Contains(children[i].ID))
                    live.Add(children[i].ID);
            }

            _scheduledDestroy = live;
        }

#if FLAX_EDITOR
        // 以下调试绘制只在编辑器可用：`Actor.OnDebugDraw` 与 `DebugDraw.*` 的绘制方法均**未导出到非编辑器目标**
        // （仅存在于编辑器程序集）。缺失该守卫会导致 Game 目标构建报 CS0115。

        // 调试绘制的缓存。编辑器每帧都会重画，而几万个点摊成十字就是十几万个顶点，
        // 每帧重新分配会让编辑器一直付 GC 的钱——设计文档 §6.4 注 17 要求的正是
        // "复用缓冲、只在数据变化时重建"，这几份字段就是那条约定的落地。
        [NoSerialize]
        private List<Vector3>[] _debugBandPool;
        [NoSerialize]
        private Float3[][] _debugBandLines = Array.Empty<Float3[]>();
        [NoSerialize]
        private Color[] _debugBandColors = Array.Empty<Color>();
        [NoSerialize]
        private PCGPoint[] _debugBakedPoints;
        [NoSerialize]
        private int _debugBakedCount = -1;
        [NoSerialize]
        private int _debugBakedStep = -1;
        [NoSerialize]
        private int _debugBakedMaxPoints = -1;
        [NoSerialize]
        private float _debugBakedMinDensity = -1.0f;

        /// <inheritdoc />
        public override void OnDebugDraw()
        {
            base.OnDebugDraw();

            if (!DebugDrawPoints || _lastOutput == null)
                return;

            var points = _lastOutput.Points;
            if (points == null || points.Length == 0)
                return;

            // 画生成体积，好让这片区域在什么都还没生成之前就看得见。
            DrawWireBox(TransformVolumeToWorld(), new Color(0.2f, 0.8f, 1.0f), 0.0f, false);

            int step = 1;
            if (DebugDrawMaxPoints > 0 && points.Length > DebugDrawMaxPoints)
                step = Mathf.Max(1, points.Length / DebugDrawMaxPoints);

            // 点集和旋钮都没变就沿用上一份顶点；变了一次才重建。
            if (!IsDebugBakeCurrent(points, step))
                RebuildDebugBake(points, step);

            for (int band = 0; band < _debugBandLines.Length; band++)
            {
                var lines = _debugBandLines[band];
                if (lines == null || lines.Length == 0)
                    continue;

                DebugDraw.DrawLines(lines, Matrix.Identity, _debugBandColors[band], 0.0f, false);
            }
        }

        private bool IsDebugBakeCurrent(PCGPoint[] points, int step) =>
            ReferenceEquals(_debugBakedPoints, points) &&
            _debugBakedCount == points.Length &&
            _debugBakedStep == step &&
            _debugBakedMaxPoints == DebugDrawMaxPoints &&
            _debugBakedMinDensity == DebugDrawMinDensity;

        private void RebuildDebugBake(PCGPoint[] points, int step)
        {
            // 批处理：逐点调 DrawSphere 会变成一点一次绘制，几千个点就把编辑器卡住了。
            // DrawLines 收的是一份扁平的顶点数组，于是点按密度带累积进
            // 一次调用，每个十字由四个顶点搭出来。
            const int Bands = 4;

            if (_debugBandPool == null || _debugBandPool.Length != Bands)
            {
                _debugBandPool = new List<Vector3>[Bands];
                for (int band = 0; band < Bands; band++)
                    _debugBandPool[band] = new List<Vector3>();
            }

            for (int band = 0; band < Bands; band++)
                _debugBandPool[band].Clear();

            for (int i = 0; i < points.Length; i += step)
            {
                var point = points[i];
                if (point.Density < DebugDrawMinDensity)
                    continue;

                int band = Mathf.Clamp((int)(point.Density * Bands), 0, Bands - 1);
                _debugBandPool[band].Add(point.Position);
            }

            var linesByBand = new Float3[Bands][];
            var colorsByBand = new Color[Bands];

            for (int band = 0; band < Bands; band++)
            {
                var positions = _debugBandPool[band];
                if (positions.Count == 0)
                {
                    linesByBand[band] = Array.Empty<Float3>();
                    continue;
                }

                float size = step > 1 ? 6.0f : 4.0f;

                // 每个点两段，于是每个点四个顶点，按 DrawLines 的要求扁平摆放。
                var lines = new Float3[positions.Count * 4];

                for (int i = 0; i < positions.Count; i++)
                {
                    var p = positions[i];
                    int o = i * 4;

                    lines[o + 0] = new Float3(p.X - size, p.Y, p.Z);
                    lines[o + 1] = new Float3(p.X + size, p.Y, p.Z);

                    // 在倾斜地形上，第二段画竖直比画水平更好认。
                    lines[o + 2] = new Float3(p.X, p.Y - size, p.Z);
                    lines[o + 3] = new Float3(p.X, p.Y + size, p.Z);
                }

                linesByBand[band] = lines;

                // 低密度偏蓝，高密度偏红。
                float t = Bands > 1 ? band / (float)(Bands - 1) : 0.0f;
                colorsByBand[band] = new Color(
                    Mathf.Lerp(0.2f, 1.0f, t), Mathf.Lerp(0.4f, 0.3f, t), Mathf.Lerp(1.0f, 0.2f, t));
            }

            _debugBandLines = linesByBand;
            _debugBandColors = colorsByBand;
            _debugBakedPoints = points;
            _debugBakedCount = points.Length;
            _debugBakedStep = step;
            _debugBakedMaxPoints = DebugDrawMaxPoints;
            _debugBakedMinDensity = DebugDrawMinDensity;
        }

        private void DrawWireBox(BoundingBox box, Color color, float duration, bool depthTest)
        {
            DebugDraw.DrawWireBox(box, color, duration, depthTest);
        }
#endif

        private static IEnumerable<string> GetErrorMessages(IReadOnlyList<PCGCompileDiagnostic> diagnostics)
        {
            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i].Severity == PCGCompileSeverity.Error)
                    yield return diagnostics[i].ToString();
            }
        }
    }
}
