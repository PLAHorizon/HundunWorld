// PCG 插件 - 图表编辑器窗口
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §6.1-§6.3。

using System;
using System.Collections.Generic;
using System.Text;
using FlaxEditor;
using FlaxEditor.Content;
using FlaxEditor.GUI;
using FlaxEditor.Surface;
using FlaxEditor.Surface.Elements;
using FlaxEditor.Windows;
using FlaxEngine;
using FlaxEngine.GUI;
using PCG.Execution;
using PCG.Graph;
using PCG.Runtime;

namespace PCGEditor
{
    /// <summary>
    /// 一个 PCG 图文件对应一个编辑器窗口：画布建在**插件执行的那张图**之上。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 画布是 <see cref="VisjectSurface"/>——粒子图与动画图就建在它上面。
    /// 于是拖拽、连线、框选、缩放、复制粘贴与撤销都来自引擎，而不是
    /// 插件代码。节点类型由运行时注册表生成，于是画布既不可能给出一个
    /// 执行器不认识的节点，也不可能藏起它认识的节点。
    /// </para>
    /// <para>
    /// 这个窗口同时也是画布的上下文：<see cref="IVisjectSurfaceOwner"/> 要求
    /// <see cref="ISurfaceContext"/>，所以画布会向窗口要它的名字、载荷与资产。图
    /// 文件是唯一真相；Visject 的载荷以文本形式装在它里面，于是一个文件同时带着
    /// 可执行的图与产出它的那套布局。
    /// </para>
    /// <para>
    /// 保存是被**有意**看守的。重建画布靠的是 Visject 的载荷，而代码搭出来的图
    /// 还没有载荷；那种状态下提取一张空画布，等于把一张真图用"什么都没有"覆盖掉。
    /// 窗口会检测到并拒绝，而不是悄悄毁掉工作。
    /// </para>
    /// </remarks>
    internal sealed class PCGGraphEditorWindow : EditorWindow, IVisjectSurfaceOwner
    {
        private const float HeaderHeight = 56.0f;
        private const float FooterHeight = 54.0f;

        /// <summary>右侧参数列的宽度；画布拿走剩下的。</summary>
        private const float PanelWidth = 340.0f;

        /// <summary>底部错误行所处的面板高度。</summary>
        private float PanelHeight => Mathf.Max(1.0f, Height - HeaderHeight - FooterHeight);

        private VisjectSurface _surface;
        private TextBox _pathBox;
        private Label _specLabel;
        private TextBox _specBox;
        private Button _specButton;
        private Label _statusLabel;

        // 表头由一个函数按当前宽度整体布局，因为这些控件自己锚不住：
        // 它们的矩形是显式的。没有这一步，按钮就只在构造时被摆过一次——
        // 那时窗口宽度还是零——于是整个右侧按钮簇落在负 x 上、跑出
        // 窗口左边界，而且会一直待在那儿。
        private Label _pathLabel;
        private readonly List<(Button Button, float Left)> _leftButtons = new List<(Button, float)>();
        private readonly List<(Button Button, float FromRight)> _rightButtons = new List<(Button, float)>();

        private const float HeaderButtonY = 7.0f;
        private const float ButtonWidth = 44.0f;
        private const float HeaderButtonHeight = 22.0f;

        /// <summary>窗口打开时用的名字，好让设计师在给任何东西命名之前就有东西可编辑。</summary>
        private const string DefaultGraphName = "新建图";
        private Panel _paramPanel;
        private Label _paramError;

        /// <summary>参数行是按哪个面板宽度摆的；与实际宽度不一致时就重摆一次（见 <see cref="LayoutSurface"/>）。</summary>
        private float _rowsWidth;

        /// <summary>上次同步参数列时的选中签名（个数 + 第一个节点），用来发现"选中变了但事件没响"。</summary>
        private long _selectionSignature = -1;

        /// <summary>面板当前在编辑的节点；什么都没选（或选了多个）时为 null。</summary>
        private SurfaceNode _paramNode;

        /// <summary>每个槽位行对应的活文本框，写回时用来收集设计师敲进去的东西。</summary>
        private readonly List<ParameterRow> _rows = new List<ParameterRow>();

        /// <summary>
        /// 暴露参数，按节点 id 追踪。见 <see cref="PCGParameterText.TrackedExposed"/>：一张图
        /// 按下标存它们，而编辑会让下标平移，于是"保存画布"否则会把它们挪到别的槽位上。
        /// </summary>
        private readonly List<PCGParameterText.TrackedExposed> _exposed = new List<PCGParameterText.TrackedExposed>();

        private PCGGraph _graph = new PCGGraph();

        // 上一次干跑的记录器，于是选中一个节点能说出那个节点产出了什么。跑过之前为 null，
        // 这也是读数沉默而不是空着的原因。
        private PCGNodeInspection _lastInspection;

        private readonly Color _sampleColor = new Color(0.2f, 1.0f, 0.4f, 1.0f);
        private bool _sampleDrawFailed;

        /// <summary>置位时，每个产出过点的节点都被画出来，各用一种颜色。</summary>
        private bool _drawAllSamples;
        private readonly List<PCGNodeInspection.NodeDraw> _drawBuffer = new List<PCGNodeInspection.NodeDraw>();

        /// <summary>What "复制" put on the clipboard; a paste reads it without consuming it.</summary>
        private PCGGraphCopy _clipboard;
        private string _path;
        private bool _dirty;
        private float _lastWidth;
        private float _lastHeight;

        /// <summary>
        /// 从画布自身的连接/断开事件里追踪到的边，按**节点 id** 而不是
        /// 画布位置键控。Box 的连接表不属于脚本 API，而节点 id 依然有效
        /// ——在其他节点被删或移动时——所以删除不会把一条边悄悄挪到
        /// 另一个节点上，而位置下标就会。
        /// </summary>
        private readonly List<TrackedEdge> _edges = new List<TrackedEdge>();

        /// <summary>画布是按节点数据重建出来时为 true——它恢复节点，但没有连线。</summary>
        private bool _wiresNotRestored;

        /// <summary>连着第二次保存才允许丢掉"重建出来的画布"没能恢复的那些连线。</summary>
        private bool _wiresDiscardConfirmed;

        private readonly struct TrackedEdge
        {
            public readonly uint SourceNodeID;
            public readonly int SourcePin;
            public readonly uint TargetNodeID;
            public readonly int TargetPin;

            public TrackedEdge(uint sourceNodeID, int sourcePin, uint targetNodeID, int targetPin)
            {
                SourceNodeID = sourceNodeID;
                SourcePin = sourcePin;
                TargetNodeID = targetNodeID;
                TargetPin = targetPin;
            }
        }

        public PCGGraphEditorWindow(Editor editor) : base(editor, true, ScrollBars.None)
        {
            Title = "PCG 图表编辑器";
            BuildGui();
        }

        /// <summary>为一个图文件打开窗口。</summary>
        public static PCGGraphEditorWindow ShowFor(Editor editor, string graphPath)
        {
            if (editor == null)
                return null;

            var window = new PCGGraphEditorWindow(editor);

            // 先显示、后加载，顺序有意义：载荷里带着画布视图（一个点加一个缩放），而
            // 它要靠 surface 的尺寸换算成控件位置。窗口还没上屏就加载，
            // 等于拿一个不是"保存时那个"的尺寸去量这份视图，于是视图会被丢在屏幕外。
            // nodes outside the visible rectangle - a canvas that reports "已载入 N 个节点" and shows nothing.
            window.Show();

            if (!string.IsNullOrWhiteSpace(graphPath))
                window.LoadGraph(graphPath);

            return window;
        }

        // ---- ISurfaceContext ------------------------------------------------

        /// <inheritdoc />
        public string SurfaceName => string.IsNullOrEmpty(_graph.Name) ? "PCG Graph" : _graph.Name;

        /// <inheritdoc />
        /// <remarks>
        /// PCG 图是插件自己的文件，不是引擎资产，所以没有 <see cref="Asset"/> 可以交给
        /// 画布。PCG 节点集里没有任何东西用这个资产做浏览，所以它可以为 null
        /// 而画布不丢功能。
        /// </remarks>
        public Asset SurfaceAsset => null;

        /// <inheritdoc />
        public VisjectSurfaceContext ParentContext => null;

        /// <inheritdoc />
        /// <remarks>
        /// Visject 产出的字节的 base64——**不是文本**。`VisjectSurfaceContext.Save` 是用
        /// <c>BinaryWriter</c> 把画布写出去的（节点元数据、浮点、整数、带长度前缀的字符串），所以载荷是二进制，
        /// 任何文本编码都会丢信息。按 UTF-8 存它（第一版就是这么干的）会把每一段非法
        /// 字节序列换成 U+FFFD，这正是"每个存过的图重开都是空画布"的原因：回来的
        /// 那份载荷根本解析不了。见 <see cref="PCGGraphJson.MigrateEditorData"/>。
        /// </remarks>
        public byte[] SurfaceData
        {
            get => PCGGraphJson.TryDecodeEditorData(_graph.EditorData, out byte[] bytes, out _)
                ? bytes
                : Array.Empty<byte>();

            set
            {
                _graph.EditorData = PCGGraphJson.EncodeEditorData(value);
                MarkDirty();
            }
        }

        /// <inheritdoc />
        public void OnContextCreated(VisjectSurfaceContext context)
        {
            // 没有东西要播种：布局是经 SurfaceData 来的。
        }

        // ---- IVisjectSurfaceOwner -------------------------------------------

        /// <inheritdoc />
        /// <remarks>
        /// 画布向它的 owner 要撤销栈，给它 null 就等于它没地方记录编辑——而画布自己的
        /// 保存正是**静默**拒绝在这上面的。借给它的应该是编辑器那一个：设计师在这个窗口里
        /// 按 Ctrl+Z，本来就该走那一条栈。
        /// </remarks>
        public Undo Undo => Editor?.Undo;

        /// <inheritdoc />
        public void OnSurfaceClose()
        {
            if (_surface == null)
                return;

            _surface.NodesConnected -= OnNodesConnected;
            _surface.NodesDisconnected -= OnNodesDisconnected;
        }

        /// <inheritdoc />
        public void OnSurfaceEditedChanged() => MarkDirty();

        /// <inheritdoc />
        public void OnSurfaceGraphEdited() => MarkDirty();

        // ---- 窗口 ----------------------------------------------------------

        private void BuildGui()
        {

            // 节点在有了参数面板之前没有控件，所以新拖出来的节点保持
            // schema 的默认值，设计师在这里改它们，而不是手改图文件。
            // 第二个参数是画布自己的保存动作：框架在保存时会调它，而给它 null 时
            // null any save the surface initiates throws instead of persisting - which is exactly what "无法保存"
            // 任何由画布发起的保存都会抛——那正是"无法保存"的真身。对 FlaxEditor.Surface.VisjectSurface 的转储给出的签名是：
            // (IVisjectSurfaceOwner owner, Action onSave, Undo undo, SurfaceStyle style, List<GroupArchetype>
            // groups, bool supportsDebugging)。
            _surface = new VisjectSurface(
                this, SaveGraphToDisk, Editor?.Undo, SurfaceStyle.CreateDefault(Editor), PCGNodeArchetypes.Build(), true)
            {
                Parent = this,
            };

            // 画布自己的构造函数要的是 StretchAll，那会在**每一次**尺寸变化时按父控件重算它的矩形——
            // 而"每一次"包括窗口第一次显示时那次，那时构造它时用的边距
            // 已经不再描述我们留给表头的那一行了。
            // TopLeft 让我们赋的矩形成为唯一权威，这也是本项目其余代码驱动 UI
            // 遵循的同一条规则。位置很重要：必须在下面 LayoutSurface 赋矩形**之前**设，
            // 因为锚点预设的 setter 会把位置吸附到锚点上，反过来做会把
            // 矩形整个丢掉。
            _surface.AnchorPreset = AnchorPresets.TopLeft;

            _surface.NodesConnected += OnNodesConnected;
            _surface.NodesDisconnected += OnNodesDisconnected;
            _surface.NodeValuesEdited += OnNodeValuesEdited;
            _surface.NodeSpawned += _ => MarkDirty();
            _surface.NodeDeleted += _ => MarkDirty();
            _surface.SelectionChanged += OnSelectionChanged;

            // 参数列必须创建在画布**之后**：Flax 按创建顺序绘子控件，而画布构造时想要的是 StretchAll，
            // 引擎的布局遍历随时可能把它撑满父控件——撑满之后它画在参数列**上面**，那一列因此既看不见也
            // 点不到（这就是"被遮挡了无法操作"）。表头那一行当初就是按同一条规则放在画布之后建的，
            // 参数列漏了这一步。

            _paramPanel = new Panel
            {
                Parent = this,
                Bounds = new Rectangle(Mathf.Max(0.0f, Width - PanelWidth), HeaderHeight, PanelWidth, Mathf.Max(1.0f, Height - HeaderHeight - FooterHeight)),

                // 比画布亮一点点：这一列在没选中节点时只剩底色，和空画布同色的话，"属性列是空的"与
                // "根本没有属性列"在屏幕上看不出区别。
                BackgroundColor = new Color(0.16f, 0.17f, 0.19f, 0.85f),
            };

            // 和画布同一个坑：非 TopLeft 的锚点预设会在引擎自己的布局遍历里**按父控件重算这个矩形的尺寸**，
            // 于是参数列会比我们算的窄；而列里的输入框是按"面板有多宽"摆的，窄了就把右端露在外面——
            // 露出去的那截既看不见也点不到（"被遮挡了无法操作"就是这么来的）。TopLeft 让我们赋的矩形成为
            // 唯一权威，与画布、与表头那一行同一条规则。
            _paramPanel.AnchorPreset = AnchorPresets.TopLeft;

            // 有意在画布**之后**创建：Flax 按创建顺序画子控件，所以表头那一行才是窗口里
            // 最上层的东西。要是把 surface 放在最后，它无论最终拿到什么矩形——包括一个
            // stale full-window one - would paint over the buttons instead of underneath them, and "保存" would be
            // 过期的整窗矩形——都会盖住按钮，于是"保存"不是位置偏了，而是根本点不到。
            _pathLabel = new Label
            {
                Parent = this,
                Bounds = new Rectangle(8, 8, 46, 20),
                Text = "图表:",
            };

            _pathBox = new TextBox
            {
                Parent = this,
                Bounds = new Rectangle(200, 8, 40, 20),
                Text = string.Empty,
            };

            // 第二行：一段设定描述的文件名 + 生成。放在**第二行**是有意的——第一行已经排满（左边文件动作、
            // 右边运行动作），而 HeaderHeight 本来就留了两行的高度，于是既不动既有布局，也不挤压图表名输入框。
            _specLabel = new Label
            {
                Parent = this,
                Bounds = new Rectangle(8, 31, 46, 20),
                Text = "描述:",
            };

            _specBox = new TextBox
            {
                Parent = this,
                Bounds = new Rectangle(200, 31, 40, 20),
                Text = string.Empty,
            };

            _specButton = new Button
            {
                Parent = this,
                Text = "生成",
                Bounds = new Rectangle(0.0f, 31, ButtonWidth, HeaderButtonHeight),
            };

            _specButton.Clicked += SpecClicked;

            // 图文件动作在路径框左边，图运行动作在右边：两组由它们都指向的
            // 那个字段隔开。
            AddButton("副本", 56, false, DuplicateClicked);
            AddButton("粘贴", 104, false, PasteClicked);
            AddButton("复制", 152, false, CopyClicked);

            AddButton("试跑", 238, true, DryRunClicked);
            AddButton("全绘", 286, true, ToggleAllSamplesClicked);
            AddButton("打开", 190, true, OpenClicked);
            AddButton("新建", 142, true, NewClicked);
            AddButton("保存", 94, true, SaveClicked);
            AddButton("校验", 46, true, ValidateClicked);

            _statusLabel = new Label
            {
                Parent = this,
                Bounds = new Rectangle(8, Height - FooterHeight + 6, Mathf.Max(40.0f, Width - 16), FooterHeight - 10),
                Text = "输入图表文件名（裸名落在 Content/PCG/Graphs）后点「打开」；不存在时「新建」会创建空图表。",
            };

            LayoutHeader();
            LayoutSurface();

            RebuildParameterPanel(null);

            // 打开就处于可编辑状态。背后没有图的画布看起来是**坏了**而不是空的：谁
            // 进来第一件事都是右键点网格，而没有上下文时那一下什么都不做——没有节点菜单、没有
            // explanation. Loading an unnamed graph in memory costs nothing (nothing is written until 保存), makes
            // 在内存里加载一张无名图不要钱（点保存之前什么都不落盘），于是画布立刻是活的，而路径框被填上，保存也就有了去处。
            string initial = _pathBox != null && !string.IsNullOrWhiteSpace(_pathBox.Text)
                ? _pathBox.Text
                : DefaultGraphName;

            if (_pathBox != null)
                _pathBox.Text = initial;

            LoadGraph(initial);
        }

        /// <summary>
        /// 创建一个表头按钮并记住它该在哪儿，好让尺寸变化时能挪它。
        /// </summary>
        /// <param name="text">按钮文字。</param>
        /// <param name="offset">距左边缘的 X；<paramref name="fromRight"/> 为真时是距右边缘。</param>
        /// <param name="fromRight">为真表示这是钉在右边缘的运行动作簇。</param>
        /// <param name="onClick">处理函数。</param>
        private void AddButton(string text, float offset, bool fromRight, Action onClick)
        {
            var button = new Button
            {
                Parent = this,
                Text = text,
                Bounds = new Rectangle(0.0f, HeaderButtonY, ButtonWidth, HeaderButtonHeight),
            };

            button.Clicked += onClick;

            if (fromRight)
                _rightButtons.Add((button, offset));
            else
                _leftButtons.Add((button, offset));
        }

        /// <summary>
        /// 摆放表头那一行：标签、路径字段，以及两个按钮簇。
        /// </summary>
        /// <remarks>
        /// 构造函数与每一次尺寸变化都会调它，而且这些矩形**只在这里**被写——两个地方各算一遍
        /// 同一行，正是它们第一次跑偏的原因。字段至少保留 40
        /// 单位，于是很窄的窗口里按钮会重叠，而不是把控件压成零。
        /// </remarks>
        private void LayoutHeader()
        {
            const float RightCluster = 244.0f;

            if (_pathLabel != null)
            {
                _pathLabel.Bounds = new Rectangle(8.0f, 8.0f, 46.0f, 20.0f);
                _pathLabel.Visible = true;
            }

            if (_pathBox == null)
                return;

            _pathBox.Bounds = new Rectangle(
                200.0f, 8.0f, Mathf.Max(40.0f, Width - 200.0f - RightCluster), 20.0f);
            _pathBox.Visible = true;

            float headerWidth = Mathf.Max(Width, ButtonWidth + RightCluster);

            for (int i = 0; i < _leftButtons.Count; i++)
            {
                var (button, left) = _leftButtons[i];
                button.Bounds = new Rectangle(left, HeaderButtonY, ButtonWidth, HeaderButtonHeight);
                button.Visible = true;
            }

            for (int i = 0; i < _rightButtons.Count; i++)
            {
                var (button, fromRight) = _rightButtons[i];

                // 在左边缘钳一下：窄窗口里按钮簇是重叠，而不是一路走出
                // window, because a button that is off-screen cannot be pressed at all - least of all 保存.
                button.Bounds = new Rectangle(
                    Mathf.Max(0.0f, headerWidth - fromRight - ButtonWidth), HeaderButtonY, ButtonWidth, HeaderButtonHeight);
                button.Visible = true;
            }

            // 第二行：描述名输入框占满到右端（第一行的按钮都在第一行，这一行整条都是空的），生成按钮贴右端。
            if (_specLabel != null)
            {
                _specLabel.Bounds = new Rectangle(8.0f, 31.0f, 46.0f, 20.0f);
                _specLabel.Visible = true;
            }

            if (_specBox != null)
            {
                _specBox.Bounds = new Rectangle(200.0f, 31.0f, Mathf.Max(40.0f, Width - 200.0f - 60.0f), 20.0f);
                _specBox.Visible = true;
            }

            if (_specButton != null)
            {
                _specButton.Bounds = new Rectangle(Mathf.Max(0.0f, Width - 52.0f), 31.0f, ButtonWidth, HeaderButtonHeight);
                _specButton.Visible = true;
            }
        }

        /// <inheritdoc />
        public override void OnUpdate()
        {
            base.OnUpdate();

            // 在任何东西之前画：叠加层与窗口尺寸无关。
            DrawSelectedSamples();
            DrawAllSamples();

            // 在重排把它修回来**之前**检查，于是这里报的是引擎在这一帧对那一行
            // 做了什么，而不是我们马上要请求什么。
            ReportLayoutDrift();

            // 每帧都布局，而不是只在尺寸变化时布局。这些控件有显式矩形、没有锚点，
            // 所以任何把它清零或隐藏的东西——换主题、最大化窗口、编辑器一次布局遍历——
            // 以前会让表头永久消失，因为没有东西会再申明它。那才是顶部那一行"找不到"
            // 而不是"位置偏了"的原因。每帧几十次 Rectangle 赋值
            // 比"不可恢复"便宜得多。
            LayoutHeader();
            LayoutSurface();
            ReportLayoutOnce();

            // 选中变化不只靠画布那个事件：换面板、程序化选中、脚本重载之后事件未必响，而"选中了节点、
            // 参数列却不动"在屏幕上与"没有属性编辑 UI"一模一样。每帧比一次签名，代价是几十次整数比较。
            SyncSelectionPanel();

            // 放在下面的尺寸早退**之前**：视图必须在画布第一次有真实尺寸的那一帧被修正，
            // 而那一帧正是尺寸变化这条路径也会跑的那一帧。
            FrameGraphIfNeeded();

            if (_pathBox == null || (Mathf.Abs(_lastWidth - Width) < 0.5f && Mathf.Abs(_lastHeight - Height) < 0.5f))
                return;

            _lastWidth = Width;
            _lastHeight = Height;

            // 行矩形是按面板高度算出来的，所以尺寸变化会重排它们，而不是把
            // 错误行留在列中间的某个位置。
            RebuildParameterPanel(_paramNode);
        }

        private bool _layoutReported;
        private bool _layoutDriftReported;
        private bool _frameGraphWhenSized;

        /// <summary>
        /// 载入之后把视图落到图上，前提是画布已经有尺寸可以拿来量这份视图。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 视图在载荷里存成一个点加一个缩放，而 <c>ViewCenterPosition</c> 用 surface 的<b>当前尺寸</b>
        /// 把它换回控件位置。在窗口还没稳定下来时载入一张图（恢复的编辑器会话、刚打开的窗口），
        /// 那个存下来的中心就会按一个不是"设计师保存时那个"的尺寸
        /// 施加下去，于是节点被放到
        /// visible rectangle. Nodes in the context, wires tracked, "已载入 N 个节点" reported, empty canvas: the
        /// 载入本身没问题、只有视图错了，所以管线的其他地方都发现不了。
        /// </para>
        /// <para>
        /// 推迟到 surface 有真实尺寸之后，而且只在图完全不在视野里时才施加，
        /// 于是一次合法保存下来的平移与缩放仍然被尊重。这正是引擎自己的 surface 宿主在
        /// 载入之后做的事（<c>ShowWholeGraph</c>）。
        /// </para>
        /// </remarks>
        private void FrameGraphIfNeeded()
        {
            if (!_frameGraphWhenSized || _surface == null)
                return;

            var size = _surface.Size;

            // 还没布局：拿此时的尺寸去施加视图，恰恰就是这里要纠正的那个错误。
            if (size.X <= 2.0f || size.Y <= 2.0f)
                return;

            _frameGraphWhenSized = false;

            if (GraphIsInView())
                return;

            _surface.ShowWholeGraph();

            // 是**追加**而不是替换：已经在那里的那一行说的是载入了什么，而这一行说的是
            // 画布为什么动了。把前一行冲掉，就看不出文件本身其实是好的。
            string previous = _statusLabel?.Text ?? string.Empty;
            string note = "（文件里的视图不在图表上——已自动对准整张图）";

            SetStatus(string.IsNullOrEmpty(previous) ? note.TrimStart('（') : previous + note);
        }

        /// <summary>
        /// 图的中点是否落在画布**真正**显示的那片区域里。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 从根控件自己的位置与缩放量出来，而不是从 <c>ViewCenterPosition</c> 量——这个区别
        /// 正是要害：视图中心那个属性会用 surface 的<b>当前</b>尺寸自我重算，
        /// 于是"按陈旧尺寸施加下去的视图"读回来显得是对的。
        /// 节点**真正**是经根控件的变换画出来的，所以问它是唯一能
        /// 看出差别的办法。
        /// </para>
        /// <para>
        /// 看的是中心而不是"有没有重叠"：一张只在边缘挂出几个像素的图，技术上与视图相交，
        /// 但根本没法用，而"我能看见我的图"才是这里真正要问的问题。
        /// </para>
        /// </remarks>
        private bool GraphIsInView()
        {
            if (_surface == null || _surface.Nodes == null || _surface.Nodes.Count == 0)
                return true;

            var root = _surface.SurfaceRoot;

            if (root == null)
                return true;

            float scale = Mathf.Abs(root.Scale.X);

            if (scale < 0.001f)
                return false;

            var size = _surface.Size;
            var location = root.Location;
            var bounds = _surface.AllNodesBounds;
            var center = bounds.Center;

            // 根控件空间里的可见矩形，反解 p_surface = p_root * scale + location。
            float minX = -location.X / scale;
            float maxX = (size.X - location.X) / scale;
            float minY = -location.Y / scale;
            float maxY = (size.Y - location.Y) / scale;

            return center.X >= minX && center.X <= maxX && center.Y >= minY && center.Y <= maxY;
        }

        /// <summary>
        /// 在窗口第一次有尺寸的那一帧，把真实几何写一次日志。
        /// </summary>
        /// <remarks>
        /// 表头那一行不见了的窗口是个**像素**问题，而这里唯一的像素通道是截图。
        /// 这就把它变成文字：控件最终拿到了什么都在编辑器日志里，于是"表头不见了"
        /// 与"表头在 Y=0、被画布盖住"不必靠猜就能分开。
        /// </remarks>
        private void ReportLayoutOnce()
        {
            if (_layoutReported || Width < 1.0f || Height < 1.0f)
                return;

            _layoutReported = true;

            Debug.Log($"[PCG] 图表编辑器布局：窗口 {Width}x{Height}；" +
                      $"画布 {_surface?.Bounds}（AnchorPreset={_surface?.AnchorPreset}）；" +
                      $"参数列 {_paramPanel?.Bounds}（AnchorPreset={_paramPanel?.AnchorPreset}，行按 {_rowsWidth:F0} 摆）；" +
                      $"最后一个输入框 {LastFieldBounds()}；" +
                      $"路径框 {_pathBox?.Bounds}；" +
                      $"保存按钮 {FindButtonBounds("保存")}；" +
                      $"状态栏 {_statusLabel?.Bounds}。" +
                      $"画布载荷格式 v{PCGGraphJson.CurrentFormat}（旧版编辑器只会写 v1，日志里看到 v1 说明 DLL 没更新）。");

            // 与布局一起报出来，因为"画布是空的"有三个长得一样的成因：
            // 没有节点、视图指向别处、或者被压成了零。一行字就能把它们分开。
            if (_surface != null)
            {
                var root = _surface.SurfaceRoot;

                Debug.Log($"[PCG] 画布视图：{_surface.Nodes?.Count ?? 0} 个节点，节点范围 {_surface.AllNodesBounds}，" +
                          $"根控件位置 {root?.Location}、缩放 {root?.Scale.X}，" +
                          $"视图中心 {_surface.ViewCenterPosition}，画布尺寸 {_surface.Size}，" +
                          $"节点在视野内={GraphIsInView()}。");
            }
        }

        /// <summary>第一次发现画布不在表头那一行下面时报告一次。</summary>
        private void ReportLayoutDrift()
        {
            if (_layoutDriftReported || _surface == null)
                return;

            float allowedWidth = Mathf.Max(1.0f, Width - PanelWidth);
            bool movedDown = Mathf.Abs(_surface.Bounds.Y - HeaderHeight) >= 0.5f;

            // 宽度这一项是补查的：画布构造时想要 StretchAll，引擎的布局遍历随时可能把它撑满父控件，
            // 撑满之后它就盖住了右边的参数列（"看不见也点不到"）。参数列已改为在画布之后创建，
            // 所以这条若再次出现，说明还有别的东西在撑画布——而不是我们没接上 z 序。
            bool tooWide = _surface.Bounds.Width > allowedWidth + 0.5f;

            if (!movedDown && !tooWide)
                return;

            _layoutDriftReported = true;

            Debug.LogWarning($"[PCG] 画布被挪出了预留的位置：Bounds={_surface.Bounds}（应为 X:0 Y:{HeaderHeight} Width:{allowedWidth}），" +
                             $"AnchorPreset={_surface.AnchorPreset}。" +
                             (tooWide ? "它比预留的宽，会盖住右边的参数列。" : string.Empty) +
                             "本帧会重新排回，但这条说明有别的东西在改它。");
        }

        private string FindButtonBounds(string text)
        {
            for (int i = 0; i < _rightButtons.Count; i++)
            {
                if (_rightButtons[i].Item1.Text == text)
                    return _rightButtons[i].Item1.Bounds.ToString();
            }

            for (int i = 0; i < _leftButtons.Count; i++)
            {
                if (_leftButtons[i].Item1.Text == text)
                    return _leftButtons[i].Item1.Bounds.ToString();
            }

            return "未创建";
        }

        /// <summary>按当前选中内容同步参数列；只在签名变化时动手。</summary>
        /// <remarks>
        /// 画布的 <c>SelectionChanged</c> 事件照旧接着；这里是第二条路：选中变了而事件没响时（换面板、程序化
        /// 选中、脚本重载之后），参数列只会在下一帧被这条接上。签名只取"个数 + 第一个节点"，够用而且便宜——
        /// 多选时那一列还有自检里的批量形态，不需要在这里分辨选中了哪几个。
        /// </remarks>
        private void SyncSelectionPanel()
        {
            if (_surface == null)
                return;

            var selected = _surface.SelectedNodes;
            int count = selected?.Count ?? 0;
            uint first = count > 0 ? selected[0].ID : 0u;
            long signature = ((long)count << 32) ^ first;

            if (signature == _selectionSignature)
                return;

            _selectionSignature = signature;
            OnSelectionChanged();
        }

        /// <summary>按当前窗口尺寸摆放画布、参数列与状态行。</summary>
        private void LayoutSurface()
        {
            // 参数列再宽也不许超过窗口的一半：窗口可能被拖得很窄，而"列比窗口还宽"就等于把输入框放到窗口外面，
            // 那一截鼠标点不到。窄到下限以下时按下限算——宁可先压扁画布。行列的摆放本来就按**实测**列宽走，
            // 所以这里改了宽度，行会在下面那句判断里自动重摆一次。
            float panelWidth = PanelWidth;

            if (Width > 1.0f)
                panelWidth = Mathf.Min(PanelWidth, Mathf.Max(PCGParameterLayout.MinPanelWidth, Width * 0.5f));

            if (_surface != null)
                _surface.Bounds = new Rectangle(0, HeaderHeight, Mathf.Max(1.0f, Width - panelWidth), Mathf.Max(1.0f, Height - HeaderHeight - FooterHeight));

            if (_paramPanel != null)
            {
                _paramPanel.Bounds = new Rectangle(Mathf.Max(0.0f, Width - panelWidth), HeaderHeight, panelWidth, Mathf.Max(1.0f, Height - HeaderHeight - FooterHeight));

                // 行是按"当时的面板宽度"摆的。面板宽度被引擎改过（锚点重算、换主题、拖动停靠）就重摆一次，
                // 否则输入框的右端会伸出面板：那一截鼠标点不到，而列里又没有滚动条可以把它拉回来。
                if (_paramNode != null && Mathf.Abs(_paramPanel.Width - _rowsWidth) > 0.5f)
                    RebuildParameterPanel(_paramNode);
            }

            if (_statusLabel != null)
                _statusLabel.Bounds = new Rectangle(8, Mathf.Max(0.0f, Height - FooterHeight + 6), Mathf.Max(40.0f, Width - 16), FooterHeight - 10);
        }

        /// <summary>最后一个参数输入框的矩形，供布局日志自证"输入框有没有伸出参数列"。</summary>
        /// <remarks>
        /// 加它是因为参数列此前**不在**那份布局日志里，于是"输入框右端被切、点不到"这件事在日志里一点痕迹都没有，
        /// 只能靠人盯着截图猜。有这一行就能直接比：输入框的右边界应当 ≤ 参数列的右边界。
        /// </remarks>
        private string LastFieldBounds()
        {
            for (int i = _rows.Count - 1; i >= 0; i--)
            {
                var fields = _rows[i].Fields;

                if (fields == null || fields.Length == 0)
                    continue;

                var last = fields[fields.Length - 1];

                if (last != null)
                    return last.Bounds.ToString();

                if (_rows[i].Editor != null)
                    return _rows[i].Editor.Bounds.ToString();
            }

            return "无（没有选中节点或该节点没有参数）";
        }

        /// <inheritdoc />
        public override void OnDestroy()
        {
            OnSurfaceClose();
            base.OnDestroy();
        }

        private void OpenClicked() => LoadGraph(_pathBox?.Text);

        /// <summary>
        /// 就地跑这张图并报出结果，不往场景里放任何东西。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 这是图窗口自己能给出的反馈：编译错误、点数、逐节点
        /// 耗时分解，以及这张图的警告——全都在任何人摆下体积、往关卡里生成之前。
        /// 它**有意**拒绝会写场景的图——生成器，以及子图节点（它的内容它看不见）——
        /// 因为那样建出来的 Actor 没人负责：体积的 Clear 只会收回
        /// 那个体积生成的，而这里根本没有体积。
        /// </para>
        /// <para>
        /// 区域用的是这个窗口自己的固定测试盒，不是任何体积的，所以这里的点数回答的是
        /// "这张图在我设的密度下产出的是不是我要的东西"，而不是"我那 2 平方公里的区会长成什么样"。种子取自
        /// 图的设置，那也是没有种子的体积采用的同一条退路。
        /// </para>
        /// </remarks>
        private void DryRunClicked()
        {
            var graph = ExtractGraph();
            if (graph.NodeCount == 0)
            {
                SetStatus("画布是空的，无从试跑。");
                return;
            }

            if (!PCGGraphCompiler.TryCompile(graph, out var compiled))
            {
                var failure = new StringBuilder("编译失败：");

                foreach (var diagnostic in compiled.Diagnostics ?? new List<PCGCompileDiagnostic>())
                    failure.Append('\n').Append(diagnostic);

                SetStatus(failure.ToString());
                return;
            }

            // 子图节点只是"没人读它"而已：按体积在运行期解析它的**同一条**路子解析，
            // 于是"只含数据子图的图"放行，而"藏着生成器的图"不放行。
            Func<string, PCGGraph> loadChild = name =>
                PCGGraphFile.TryLoad(name, out var child, out _) ? child : null;

            if (PCGGraphEffects.GraphWritesScene(graph, loadChild))
            {
                SetStatus("图里有往场景生成的节点（Spawn*，或某个子图里藏着它们），这里不试跑：这样造出来的 Actor 没人负责清理。" +
                          "请把图挂到场景里的 Volume 上生成。");
                return;
            }

            var context = new PCGExecutionContext
            {
                Seed = graph.Settings?.DefaultSeed ?? 0,
                GenerationBounds = new BoundingBox(
                    new Vector3(-300.0f, -50.0f, -300.0f), new Vector3(300.0f, 50.0f, 300.0f)),
                GraphName = string.IsNullOrEmpty(graph.Name) ? "试跑" : graph.Name,

                // 这次运行挂了检视，于是状态行能说出每个节点传下去了什么："这个剪枝器去掉了
                // 这一层的九成"往往正是设计师要的答案，而从最终输出
                // 反推只是猜。
                Inspection = new PCGNodeInspection(),
            };

            try
            {
                var result = new PCGGraphExecutor(compiled).Execute(context);

                var report = new StringBuilder(
                    $"试跑完成（区域 600×600）：汇出 {result.PrimaryOutput?.Count ?? 0} 点，" +
                    $"共产出 {result.TotalPointsProduced} 点，耗时 {result.Milliseconds:F1} ms。");

                var timings = PCGNodeTimingReport.Format(result.Stats, result.Milliseconds);
                if (!string.IsNullOrEmpty(timings))
                    report.Append('\n').Append(timings);

                // 每个节点传下去了什么，按执行顺序。单独起一段，于是逐节点计数
                // 不用翻日志就能看。
                var inspected = context.Inspection.Format();
                if (!string.IsNullOrEmpty(inspected))
                    report.Append('\n').Append("逐节点产出：").Append(inspected);

                // 留着，于是一次点击某个节点就能回答"**这个**节点传下去了什么"，不必再跑一遍。
                _lastInspection = context.Inspection;

                foreach (var warning in result.Warnings ?? Array.Empty<string>())
                    report.Append('\n').Append("提示：").Append(warning);

                SetStatus(report.ToString());
            }
            catch (Exception ex)
            {
                SetStatus($"试跑失败：{ex.GetType().Name}: {ex.Message}");
            }
        }

        private void NewClicked()
        {
            string path = _pathBox?.Text;

            if (string.IsNullOrWhiteSpace(path))
            {
                SetStatus("先在上面输入图表文件名，再点「新建」。");
                return;
            }

            var graph = new PCGGraph { Name = System.IO.Path.GetFileNameWithoutExtension(path) };

            if (!PCGGraphFile.TrySave(path, graph, out string error))
            {
                SetStatus("创建失败：" + error);
                return;
            }

            LoadGraph(path);
            SetStatus($"已创建空图表：{PCGGraphFile.ResolvePath(path)}。右键画布添加节点。");
        }

        /// <summary>
        /// 把一张**内存里**的图装进窗口：画布按数据重建，图还没落盘（点「保存」才写）。
        /// </summary>
        /// <remarks>
        /// 与 <see cref="LoadGraph"/> 的区别只有一个：不读文件。单独抽出来是因为"按描述生成"这条路根本没有图文件——
        /// 描述是输入，图是产物。目标文件仍是图表名那一栏，所以生成完点保存就落到那里。
        /// </remarks>
        public void InstallGraph(PCGGraph graph, string note = null)
        {
            if (graph == null)
            {
                SetStatus("没有可装入的图表。");
                return;
            }

            _graph = graph;
            _dirty = true;
            _edges.Clear();
            _exposed.Clear();
            _wiresDiscardConfirmed = false;
            _wiresNotRestored = false;

            _surface.ChangeContext(this);

            string rebuilt = RebuildCanvasFromGraph();
            _frameGraphWhenSized = true;

            SetStatus(string.IsNullOrEmpty(note) ? rebuilt : $"{note} {rebuilt}");
        }

        /// <summary>
        /// 按描述生成：读描述文件 → 编译成图 → 装进画布。
        /// </summary>
        /// <remarks>
        /// 整个处理体包在 try 里：编辑器里抛出去的异常不是"报错"，是窗口坏掉，而这条路上有三处外部输入
        /// （文件名、文件内容、资产库），任何一处都不值得用崩溃来报告。
        /// </remarks>
        private void SpecClicked()
        {
            try
            {
                string name = _specBox?.Text;

                if (string.IsNullOrWhiteSpace(name))
                {
                    SetStatus($"先输入描述文件名（裸名落在 {PCGSpecFile.SpecsFolder}）再点「生成」。" +
                              $"例如：world 温带山谷 / scatter forest per-block 1.5");
                    return;
                }

                var result = PCGSpecFile.TryLoad(name, ResolveAssetId);

                if (!result.Ok)
                {
                    SetStatus($"按描述生成失败：{result.Error}");
                    return;
                }

                string warnings = result.Warnings.Count > 0
                    ? " 注意：" + string.Join(" ", result.Warnings)
                    : string.Empty;

                InstallGraph(result.Graph,
                    $"已按描述 {System.IO.Path.GetFileName(PCGSpecFile.ResolvePath(name))} 生成 " +
                    $"{result.Graph.NodeCount} 个节点、{result.Graph.EdgeCount} 条连线。" +
                    $"确认无误后点「保存」落到 {_pathBox?.Text}。" + warnings);
            }
            catch (Exception ex)
            {
                SetStatus($"按描述生成时发生异常：{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>把资产路径解析成资产 id；查不到就给空，让生成器在运行期如实报"没有指定模型"。</summary>
        private Guid ResolveAssetId(string assetPath)
        {
            try
            {
                return (Editor?.ContentDatabase?.Find(assetPath) as AssetItem)?.ID ?? Guid.Empty;
            }
            catch (Exception)
            {
                return Guid.Empty;
            }
        }

        /// <summary>载入一个图文件，并按存在里面的载荷重建画布。</summary>
        public void LoadGraph(string graphPath)
        {
            if (string.IsNullOrWhiteSpace(graphPath))
            {
                SetStatus("没有输入图表文件名。");
                return;
            }

            if (!PCGGraphFile.TryLoad(graphPath, out var graph, out string loadError))
            {
                // 名字还不存在是设计师正在敲一个新名字时的常态。
                if (PCGGraphFile.Exists(graphPath))
                {
                    SetStatus(loadError);
                    return;
                }

                graph = new PCGGraph { Name = System.IO.Path.GetFileNameWithoutExtension(graphPath) };
            }

            _path = graphPath;
            _graph = graph;
            _dirty = false;
            _edges.Clear();
            _exposed.Clear();
            _wiresDiscardConfirmed = false;

            // 有载荷时画布才是连线的权威；没有载荷时按节点数据重建，
            // 而那条路回不到连接上——见 RebuildCanvasFromGraph 与保存侧的那道守卫。
            _wiresNotRestored = false;

            Title = $"PCG 图表编辑器 - {System.IO.Path.GetFileNameWithoutExtension(PCGGraphFile.ResolvePath(graphPath))}";

            if (_pathBox != null && !string.Equals(_pathBox.Text, graphPath, StringComparison.Ordinal))
                _pathBox.Text = graphPath;

            _surface.ChangeContext(this);

            int nodeCount = _graph.Nodes?.Length ?? 0;
            bool hasPayload = !string.IsNullOrEmpty(_graph.EditorData);
            string payloadNote = _graph.EditorDataNote;

            // 存下来的载荷若不是合法 base64，会让 Visject 在它的读取器里某处抛异常；在这里先查，
            // 于是设计师拿到的是真原因，而不是"画布载入失败"。
            if (hasPayload && !PCGGraphJson.TryDecodeEditorData(_graph.EditorData, out _, out string payloadError))
            {
                hasPayload = false;
                payloadNote = $"{payloadError}——节点与连线完好，已按数据重建画布。";
            }

            if (hasPayload)
            {
                // Load() 用 **true** 报告**失败**——VisjectSurfaceContext.Load 自己吞掉异常并
                // 返回 true，false 才表示"已载入"。把这个值读反既掩盖了真失败，
                // 更糟的是：一次成功载入会在连线和暴露参数被登记**之前**就提前返回，
                // 于是画布看着是对的，而下一次保存把它写成一条连线都没有的图。
                if (_surface.Load())
                {
                    SetStatus($"图表里有画布布局，但 surface 没能反序列化它（Load() 报告失败）。" +
                              $"这通常意味着文件里的布局与当前节点表不匹配。文件：{PCGGraphFile.ResolvePath(graphPath)}" +
                              "（编辑器日志里有 Visject 抛出的异常原文）");
                    return;
                }

                // 载荷是按"画布打包节点时用的那个 id"还原节点的，所以来自旧节点表的 id
                // 解析不出任何东西。Visject 不会丢掉这种节点：它在原位放一个"Missing Node"框，
                // 这意味着**数量仍然对得上**，只数数量的检查看不出任何问题。真正错的地方是
                // 这个节点没有类型，所以这里找的是那个。
                int restored = _surface.Nodes?.Count ?? 0;
                int unrecognised = CountUnrecognisedNodes();

                if (unrecognised > 0 || (nodeCount > 0 && restored < nodeCount))
                {
                    string recovered = RebuildCanvasFromGraph();

                    SetStatus(unrecognised > 0
                        ? $"文件里有 {nodeCount} 个节点，但画布上有 {unrecognised} 个认不出类型" +
                          "（画布数据里的节点编号与当前节点表对不上，多半是旧版本写的）。" +
                          $"{recovered} 画布布局已丢失、节点与连线按图表数据重建；点一次「保存」即可写成新格式。"
                        : $"文件里有 {nodeCount} 个节点，但画布只还原出 {restored} 个。" +
                          $"{recovered} 画布布局已丢失、节点与连线按图表数据重建；点一次「保存」即可写成新格式。");

                    _frameGraphWhenSized = true;
                    return;
                }

                TrackEdgesFromGraph();
                TrackExposedFromGraph();

                // 载荷自带一份视图，而它只有在画布有尺寸可以拿来量之后才可用
                // ——见 FrameGraphIfNeeded。
                _frameGraphWhenSized = true;

                SetStatus($"已载入 {nodeCount} 个节点、{_edges.Count} 条连线、{_exposed.Count} 个暴露参数。" +
                          (string.IsNullOrEmpty(payloadNote) ? string.Empty : " " + payloadNote));
                return;
            }

            if (nodeCount == 0)
            {
                SetStatus(string.IsNullOrEmpty(payloadNote)
                    ? $"空白图表（{PCGGraphFile.ResolvePath(graphPath)}）。右键画布添加节点，连线后点「保存」。"
                    : $"{payloadNote} 文件：{PCGGraphFile.ResolvePath(graphPath)}");
                return;
            }

            // 载荷不可用但盘上有节点：按数据模型重建画布，节点与连线
            // 一视同仁，于是图仍然可编辑、保存也能整份写回。读那一侧留的说明
            // 放在前面，因为它解释了为什么压根没用载荷。
            string rebuilt = RebuildCanvasFromGraph();

            SetStatus(string.IsNullOrEmpty(payloadNote) ? rebuilt : $"{payloadNote} {rebuilt}");
        }

        /// <summary>
        /// 为一张还没有 Visject 载荷的图生成画布节点。
        /// </summary>
        /// <returns>一句话描述结果，供调用方写状态行。</returns>
        /// <remarks>
        /// 用引擎自己的节点工厂，于是 id、框与上下文注册走的是与手工摆节点
        /// 同一条路；然后用 <c>Box.CreateConnection</c> 把文档里的连线接回去——
        /// 那正是引擎自己的"插入类型转换"路径用的同一个原语。从脚本里读一个框的连接仍然做不到，
        /// 但这从来不是这里需要的：图文档本来就说了哪两个引脚相连，
        /// 所以连线是**按文档重建**的，而不是从画布上读回来。这里画出来的线会触发
        /// <c>NodesConnected</c>，而边的追踪器也正是靠它学到这些边的。
        /// </remarks>
        private string RebuildCanvasFromGraph()
        {
            var context = _surface?.Context;

            if (context == null)
                return "画布尚未就绪，无法从图表重建。请重试。";

            // 先清空。工厂是**追加**的，不清的话画布会留着上一张图
            // 留下的东西：打开第二个文件会把它的节点加到第一个文件的节点上，一次粘贴/副本会在屏上摆出
            // 整份第二份图，而之后每一次读画布——校验，以及把文件写出去的那次保存——
            // 就与设计师眼前看的东西不一致了。
            context.Clear();

            int built = 0;
            int total = _graph.Nodes?.Length ?? 0;

            // 按图里的下标保存，于是连线那一遍能找到某条存下来的边所指的画布节点，
            // 也于是引脚算术知道每个节点有几个输入——那一项正是把框 id 换成引脚下标的算式。
            var spawned = new SurfaceNode[total];
            var inputCounts = new int[total];

            for (int i = 0; i < total; i++)
            {
                var node = _graph.Nodes[i];

                if (node == null ||
                    !PCGNodeArchetypes.TryGetArchetype(node.TypeId, out var group, out var archetype))
                    continue;

                var spawnedNode = NodeFactory.CreateNode(
                    ((uint)group.GroupID << 16) | archetype.TypeID, context, group, archetype);

                if (spawnedNode == null)
                    continue;

                if (node.Values != null && spawnedNode.Values != null)
                {
                    int copy = Math.Min(node.Values.Length, spawnedNode.Values.Length);
                    Array.Copy(node.Values, 0, spawnedNode.Values, 0, copy);
                }

                spawnedNode.Location = new Float2(node.EditorPosition.X, node.EditorPosition.Y);

                spawned[i] = spawnedNode;
                inputCounts[i] = CreatePrototype(node.TypeId)?.InputPins?.Length ?? 0;
                built++;
            }

            int documentEdges = _graph.Edges?.Length ?? 0;
            int restored = RestoreWires(spawned, inputCounts, total);

            // 这条路径上没有存下来的视图——节点位置来自文件，而视图还在上一张图
            // 留下的地方，所以它很可能正看着一片空。
            if (built > 0)
                _frameGraphWhenSized = true;

            if (built > 0)
            {
                // 节点是按图里的顺序追加的，所以暴露参数仍然指着对的那几个。
                TrackExposedFromGraph();
            }

            // 只有真的没能接回去的线才算损失，所以保存侧的守卫只在确有东西缺失时
            // 才去问设计师（一个没注册的类型、一个节点没有的引脚下标）。
            _wiresNotRestored = restored < documentEdges;

            if (built == 0)
                return $"图表里有 {total} 个节点，但画布无法重建。请勿点「保存」，以免覆盖图表。";

            return restored == documentEdges
                ? $"从图表重建了 {built}/{total} 个节点、{restored} 条连线。保存一次就会写入画布数据。"
                : $"从图表重建了 {built}/{total} 个节点、{restored}/{documentEdges} 条连线（其余连线的端点解析不到）。" +
                  "请在画布上补齐后保存。";
        }

        /// <summary>
        /// 在刚刚重建出来的画布上，把文档里的连线接回去。
        /// </summary>
        /// <returns>恢复了多少条线。</returns>
        /// <remarks>
        /// 文档里的引脚下标用保存侧那同一个助手换回框 id，
        /// 于是一条从某个框记录下来的线会回到那个框上。每个端点都是防御式解析的，
        /// 缺一个就跳过而不是抛异常：这段跑在设计师正在打开文件的时候，一张
        /// 半恢复、他还能自己修的画布，比一个拒绝打开的窗口好。
        /// </remarks>
        private int RestoreWires(SurfaceNode[] spawned, int[] inputCounts, int total)
        {
            var edges = _graph.Edges;

            if (edges == null || edges.Length == 0)
                return 0;

            int restored = 0;

            for (int e = 0; e < edges.Length; e++)
            {
                var edge = edges[e];

                if (edge == null ||
                    edge.OutputNodeIndex < 0 || edge.OutputNodeIndex >= total ||
                    edge.InputNodeIndex < 0 || edge.InputNodeIndex >= total)
                    continue;

                var source = spawned[edge.OutputNodeIndex];
                var target = spawned[edge.InputNodeIndex];

                if (source == null || target == null)
                    continue;

                int inputs = inputCounts[edge.OutputNodeIndex];
                var outputBox = source.GetBox(PCGNodePinSlots.BoxId(true, edge.OutputPinIndex, inputs));
                var inputBox = target.GetBox(PCGNodePinSlots.BoxId(false, edge.InputPinIndex, inputs));

                if (outputBox == null || inputBox == null)
                    continue;

                // 在输入那一侧调用、把输出当参数传进去，引擎自己的连接路径
                // 就是这么调的；CreateConnection 无条件加连接（Connect 会做切换）。
                inputBox.CreateConnection(outputBox);
                restored++;
            }

            return restored;
        }

        private void TrackEdgesFromGraph()
        {
            var nodes = _surface?.Nodes;
            if (nodes == null || _graph.Edges == null)
                return;

            // 边是按画布位置存的；追踪器需要稳定的节点 id。
            for (int i = 0; i < _graph.Edges.Length; i++)
            {
                var edge = _graph.Edges[i];
                if (edge == null)
                    continue;

                if (edge.OutputNodeIndex < 0 || edge.OutputNodeIndex >= nodes.Count ||
                    edge.InputNodeIndex < 0 || edge.InputNodeIndex >= nodes.Count)
                    continue;

                var source = nodes[edge.OutputNodeIndex];
                var target = nodes[edge.InputNodeIndex];
                if (source == null || target == null)
                    continue;

                _edges.Add(new TrackedEdge(source.ID, edge.OutputPinIndex, target.ID, edge.InputPinIndex));
            }
        }

        /// <summary>
        /// 把图的暴露参数记到"为它们载入的那些画布节点"上。
        /// </summary>
        /// <remarks>
        /// 载荷按保存时的顺序还原画布，而保存也按画布顺序写节点，
        /// 所以这里"下标对应节点"是成立的。任何解析不出来的都**略过**而不是猜；
        /// 于是体积上的覆盖列表会把那个参数报成缺失，这正是事实。
        /// </remarks>
        private void TrackExposedFromGraph()
        {
            var nodes = _surface?.Nodes;

            if (nodes == null || _graph?.ExposedParameters == null)
                return;

            foreach (var definition in _graph.ExposedParameters)
            {
                if (definition == null || definition.NodeIndex < 0 || definition.NodeIndex >= nodes.Count)
                    continue;

                var node = nodes[definition.NodeIndex];
                if (node == null)
                    continue;

                _exposed.Add(new PCGParameterText.TrackedExposed(
                    node.ID,
                    definition.ValueIndex,
                    definition.Name,
                    definition.DisplayName,
                    definition.Type,
                    definition.DefaultValue.ToObject(definition.Type)));
            }
        }

        private int FindExposed(uint nodeID, int slot)
        {
            for (int i = 0; i < _exposed.Count; i++)
            {
                if (_exposed[i].NodeID == nodeID && _exposed[i].ValueIndex == slot)
                    return i;
            }

            return -1;
        }

        private void OnExposeToggled(ParameterRow row, bool expose)
        {
            if (_paramNode == null || row?.Parameter == null)
                return;

            int existing = FindExposed(_paramNode.ID, row.Slot);

            if (!expose)
            {
                if (existing >= 0)
                    _exposed.RemoveAt(existing);

                MarkDirty();
                return;
            }

            if (existing >= 0)
                return;

            // 把当前的值抓下来，于是一个覆盖了该参数的体积是从"设计师真正看到的那个值"
            // 起步，而不是从该类型的零值起步。
            object value = row.Parameter.Default;
            var texts = new string[row.Fields?.Length ?? 0];
            for (int i = 0; i < texts.Length; i++)
                texts[i] = row.Fields[i]?.Text;

            if (PCGParameterText.TryParse(row.Parameter.Kind, texts, out var parsed, out _))
                value = parsed;

            _exposed.Add(new PCGParameterText.TrackedExposed(
                _paramNode.ID,
                row.Slot,
                PCGParameterText.ExposedName(ResolveTypeId(_paramNode), row.Slot, row.Parameter),
                row.Parameter.Name,
                row.Parameter.Kind,
                value));

            MarkDirty();
        }

        /// <summary>保存按钮：与画布自己的保存动作做的事完全一样。</summary>
        private void SaveClicked() => SaveGraphToDisk();

        /// <summary>
        /// 把画布的图文档写到 <see cref="_path"/>。
        /// </summary>
        /// <remarks>
        /// Shared by the 保存 button and by the surface's own save callback, so the two cannot disagree about what
        /// 这才是"保存"的意思。重入守卫要紧，因为那个回调是从画布保存**内部**触发的，
        /// 而按钮那条路已经驱动过一次——没有它，一次点击会把同一个文件写两遍。
        /// </remarks>
        private void SaveGraphToDisk()
        {
            if (_saving)
                return;

            _saving = true;

            try
            {
                SaveGraphToDiskCore();
            }
            catch (Exception ex)
            {
                // 保存链里任何一处抛异常，以前都意味着"点了没反应"——异常
                // 进了日志，而状态行还留着上一句话。在这里报出来
                // turns "无法保存" into a named cause.
                SetStatus($"保存时发生异常：{ex.GetType().Name}: {ex.Message}");
                Debug.LogError($"[PCG] 图表保存失败（{_path}）：{ex}");
            }
            finally
            {
                _saving = false;
            }
        }

        private bool _saving;

        private void SaveGraphToDiskCore()
        {
            if (string.IsNullOrWhiteSpace(_path))
            {
                SetStatus("没有打开图表，无从保存。");
                return;
            }

            // 在构建图文档**之前**先让画布把自己序列化出来。画布载荷只有
            // 一个写入者——SurfaceData 的 setter，Visject 在保存时会调它——所以没有这一步，
            // 文件只记下图的节点，不记它们在哪、怎么连的，于是下一次"打开"
            // 会在一张非空的图上显示空画布。载入那条路一直做着这一步的镜像
            // （ChangeContext 之后 _surface.Load()）；保存这条路缺了自己那一半。
            // 包在 try 里，因为这里的失败不能把整次保存带下去：图数据仍然
            // 值得写，而那段异常文本是关于"画布到底想要哪个 API"的唯一线索。
            string payloadError = null;

            try
            {
                // Save() 用 true 报告**失败**（VisjectSurfaceContext.Save 捕获并返回 true；false
                // 表示字节已经进了 SurfaceData）。把这个值反过来读，会让每一次成功的保存
                // announce itself as a layout failure - which is precisely the "点了保存却说保存失败" that sent this
                // 都报错（于是他往错的方向找），而且还会把窗口永久留在"脏"状态上。
                if (_surface != null && _surface.Save())
                    payloadError = "surface.Save() 报告失败（画布未被序列化，详见编辑器日志里的 Visject 警告）";
            }
            catch (Exception ex)
            {
                payloadError = $"{ex.GetType().Name}: {ex.Message}";
            }

            var graph = ExtractGraph();

            // 那个破坏性的情形：一张空画布盖在一张非空的图上，而且没有载荷可以还原。
            if (graph.NodeCount == 0 && _graph.NodeCount > 0 && string.IsNullOrEmpty(_graph.EditorData))
            {
                SetStatus($"画布是空的，但图表里已有 {_graph.NodeCount} 个节点。已拒绝保存，否则会把它们清掉。");
                return;
            }

            // 另一个破坏性情形，也是更安静的那个：节点以"Missing Node"
            // 占位框回来的画布会被写成空类型 id，于是一张能读的图变成一张谁
            // 都读不了的图。拒写而不是写出去，因为文件里还留着真图。
            int unrecognised = CountUnrecognisedNodes();

            if (unrecognised > 0)
            {
                _dirty = true;
                SetStatus($"画布上有 {unrecognised} 个节点认不出类型（显示为 Missing Node），现在保存会把它们写成空类型、" +
                          "整个图表就毁了。已拒绝保存。请关掉这张图再重新打开，让它按图表数据重建后再保存。");
                return;
            }

            int documentEdges = _graph.Edges?.Length ?? 0;

            // 按节点数据重建出来的画布上没有连线——只有节点回来了。从这样一张画布保存
            // 会把空的连线表写到一份有连线的文档上，那是一种静默
            // delete of the designer's wiring, and the status line would cheerfully report "0 条连线". Refused
            // 只问一次，第二次点击就照办：图编辑器没有确认对话框，而这是
            // 没有对话框时最省的问法。
            if (_wiresNotRestored && graph.EdgeCount < documentEdges)
            {
                if (!_wiresDiscardConfirmed)
                {
                    _wiresDiscardConfirmed = true;
                    SetStatus($"这份画布是重建出来的，其中有连线没能恢复：文件里 {documentEdges} 条，画布上只有 {graph.EdgeCount} 条，" +
                              "现在保存会把缺的那些从文件里删掉。请在画布上补齐；若确定要丢弃，再点一次「保存」。");
                    return;
                }
            }

            if (!PCGGraphFile.TrySave(_path, graph, out string error))
            {
                SetStatus("保存失败：" + error);
                return;
            }

            _graph = graph;

            if (payloadError != null)
            {
                // 文件现在装着图却不装它的布局，所以它不是画布的一份完整记录。
                _dirty = true;
                SetStatus($"节点数据已保存，但画布布局序列化失败（{payloadError}）。重开只会看到图数据、看不到画布——请把括号里这条报给开发者。");
                return;
            }

            _dirty = false;

            // 一次"带着节点却没有载荷"的保存，说明画布从没把自己序列化过：文件里有
            // 图数据，但重开它会显示一张空画布。在这里报出来，就把一次静默的布局丢失
            // 变成一行设计师能据以行动的话，而它也正是"这半条往返路
            // 真的能走通"的直接信号。
            if (graph.NodeCount > 0 && string.IsNullOrEmpty(graph.EditorData))
            {
                SetStatus($"已保存 {graph.NodeCount} 个节点，但画布布局是空的——重新打开会看不到它们。请把这条信息报给开发者。");
                return;
            }

            SetStatus($"已保存 {graph.NodeCount} 个节点、{graph.EdgeCount} 条连线到 {PCGGraphFile.ResolvePath(_path)}" +
                      $"（画布载荷 v{PCGGraphJson.CurrentFormat}）。" +
                      (SkippedEdges(graph) > 0
                          ? $"（另有 {SkippedEdges(graph)} 条追踪到的连线因端点节点不在画布上被跳过——图表可能已经不完整）"
                          : string.Empty));
        }

        /// <summary>
        /// 有多少画布节点没有可识别的类型——Visject 把这种画成"Missing Node"框。
        /// </summary>
        /// <remarks>
        /// 打包 id 解析不出来的节点不是**不在**，而是**在但没有类型**，所以只数节点
        /// 是看不见它的。文件的两端都值得找它：载入时它意味着画布得
        /// 按图数据重建；保存时把它写出去，等于把一张真图换成一张
        /// 全是 null 的图。
        /// </remarks>
        private int CountUnrecognisedNodes()
        {
            var nodes = _surface?.Nodes;

            if (nodes == null)
                return 0;

            int count = 0;

            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] != null && string.IsNullOrEmpty(ResolveTypeId(nodes[i])))
                    count++;
            }

            return count;
        }

        /// <summary>追踪器手里那些没能进文档的连线：它们的某个端点已经没了。</summary>
        private int SkippedEdges(PCGGraph graph) => Math.Max(0, _edges.Count - (graph?.EdgeCount ?? 0));

        private void ValidateClicked()
        {
            if (string.IsNullOrWhiteSpace(_path))
            {
                SetStatus("没有打开图表，无从校验。");
                return;
            }

            var graph = ExtractGraph();

            // 子图引用跨文件，所以下面那个"单文件编译器"看不见两个文件之间的环；
            // 这一遍走查用的是体积将要用的同一个加载器来解析子图。
            var subgraphIssues = PCGSubgraphInspector.Inspect(
                graph, System.IO.Path.GetFileNameWithoutExtension(PCGGraphFile.ResolvePath(_path)),
                name => PCGGraphFile.TryLoad(name, out var child, out _) ? child : null);

            if (PCGSubgraphInspector.HasErrors(subgraphIssues))
            {
                var cycleReport = new StringBuilder("子图引用有问题：");

                foreach (var diagnostic in subgraphIssues)
                {
                    if (diagnostic.Severity == PCGCompileSeverity.Error)
                        cycleReport.Append('\n').Append(diagnostic);
                }

                SetStatus(cycleReport.ToString());
                return;
            }

            if (!PCGGraphCompiler.TryCompile(graph, out var compiled))
            {
                var builder = new StringBuilder("编译失败：");

                if (compiled?.Diagnostics != null)
                {
                    for (int i = 0; i < compiled.Diagnostics.Count; i++)
                        builder.Append('\n').Append(compiled.Diagnostics[i]);
                }

                SetStatus(builder.ToString());
                return;
            }

            int errors = 0;
            var notes = new StringBuilder();

            if (compiled.Diagnostics != null)
            {
                foreach (var diagnostic in compiled.Diagnostics)
                {
                    if (diagnostic.Severity == PCGCompileSeverity.Error)
                        errors++;

                    notes.Append(' ').Append(diagnostic);
                }
            }

            SetStatus(errors == 0
                ? $"编译通过：{compiled.NodeCount} 个节点，输出节点 {compiled.SinkNodes?.Length ?? 0} 个。{(notes.Length > 0 ? "提示：" + notes : string.Empty)}"
                : $"编译有 {errors} 个错误：{notes}");
        }

        /// <summary>把画布读回成一张可执行的图。</summary>
        private PCGGraph ExtractGraph()
        {
            var nodes = _surface?.Nodes;
            var graph = new PCGGraph
            {
                Name = _graph.Name,
                EditorData = _graph.EditorData,
            };

            if (nodes == null || nodes.Count == 0)
            {
                graph.Nodes = Array.Empty<PCGNode>();
                graph.Edges = Array.Empty<PCGEdge>();
                return graph;
            }

            var idsToIndex = new Dictionary<uint, int>((int)(nodes.Count * 1.3f));
            var idsByIndex = new uint[nodes.Count];
            graph.Nodes = new PCGNode[nodes.Count];

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node == null)
                {
                    graph.Nodes[i] = null;
                    continue;
                }

                idsByIndex[i] = node.ID;
                idsToIndex[node.ID] = i;

                graph.Nodes[i] = new PCGNode
                {
                    TypeId = ResolveTypeId(node),
                    EditorPosition = new Vector2(node.Location.X, node.Location.Y),
                    Values = node.Values == null ? Array.Empty<object>() : (object[])node.Values.Clone(),
                };
            }

            var edges = new List<PCGEdge>(_edges.Count);

            foreach (var tracked in _edges)
            {
                if (!idsToIndex.TryGetValue(tracked.SourceNodeID, out int sourceIndex) ||
                    !idsToIndex.TryGetValue(tracked.TargetNodeID, out int targetIndex))
                    continue;

                edges.Add(new PCGEdge
                {
                    OutputNodeIndex = sourceIndex,
                    OutputPinIndex = tracked.SourcePin,
                    InputNodeIndex = targetIndex,
                    InputPinIndex = tracked.TargetPin,
                });
            }

            graph.Edges = edges.ToArray();
            graph.ExposedParameters = PCGParameterText.MaterializeExposed(_exposed, idsByIndex);

            return graph;
        }

        /// <summary>
        /// <c>_graph.Nodes</c> 里对应当前在画布上选中的那些节点。
        /// </summary>
        /// <remarks>
        /// 画布交回来的是画布节点，而剪贴板按图的下标工作，
        /// 而两边都原样携带的字段只有**画布位置**：载入是把图的值数组**复制**进画布节点
        /// 而不是共享它，所以引用相等算不上一条联系。两个节点叠在同一处时
        /// 因此只会解析到先找到的那一个——结果会去重，而状态行报的是
        /// 真正动手的条数，于是一次含糊的选择不可能悄悄复制错节点。
        /// </remarks>
        private List<int> SelectedGraphIndices()
        {
            var indices = new List<int>();
            var selected = _surface?.SelectedNodes;
            if (selected == null || selected.Count == 0 || _graph?.Nodes == null)
                return indices;

            foreach (var node in selected)
            {
                if (node == null)
                    continue;

                var location = node.Location;

                for (int i = 0; i < _graph.Nodes.Length; i++)
                {
                    var graphNode = _graph.Nodes[i];
                    if (graphNode == null)
                        continue;

                    if (Math.Abs(graphNode.EditorPosition.X - location.X) > 0.5f ||
                        Math.Abs(graphNode.EditorPosition.Y - location.Y) > 0.5f)
                    {
                        continue;
                    }

                    if (!indices.Contains(i))
                        indices.Add(i);

                    break;
                }
            }

            indices.Sort();
            return indices;
        }

        private void CopyClicked()
        {
            var indices = SelectedGraphIndices();
            if (indices.Count == 0)
            {
                SetStatus("先在画布上选中要复制的节点。");
                return;
            }

            _clipboard = PCGGraphClipboard.Copy(_graph, indices);

            // 说清认出了几个才是要点：一次没能解析出来的选择
            // 否则看起来就像"成功复制了一小批"。
            int requested = _surface?.SelectedNodes?.Count ?? 0;
            var note = _clipboard.NodeCount < requested
                ? $"（选中 {requested} 个，只识别出 {_clipboard.NodeCount} 个：画布上有节点位置重叠或不在图表里）"
                : string.Empty;

            SetStatus($"已复制 {_clipboard.NodeCount} 个节点{note}" +
                      (_clipboard.DroppedEdgeCount > 0
                          ? $"，其中 {_clipboard.DroppedEdgeCount} 条连线因为另一端没被选中而没有带上。"
                          : "。"));
        }

        private void PasteClicked()
        {
            if (_clipboard == null || _clipboard.IsEmpty)
            {
                SetStatus("剪贴板是空的：先复制一组节点。");
                return;
            }

            // 粘贴是先写图数据、再按它重建画布，所以画出来的东西永远不会
            // 与将要保存的东西不一致。
            var pasted = PCGGraphClipboard.Paste(_graph, _clipboard, PCGGraphClipboard.DefaultPasteOffset,
                out int[] newIndices, out string error);

            if (!pasted)
            {
                SetStatus(error);
                return;
            }

            RebuildCanvasFromGraph();
            SetStatus($"已粘贴 {newIndices.Length} 个节点。确认无误后点“保存”。");
        }

        private void DuplicateClicked()
        {
            var indices = SelectedGraphIndices();
            if (indices.Count == 0)
            {
                SetStatus("先在画布上选中要复制副本的节点。");
                return;
            }

            var duplicated = PCGGraphClipboard.Duplicate(_graph, indices, PCGGraphClipboard.DefaultPasteOffset,
                out int[] newIndices, out string error);

            if (!duplicated)
            {
                SetStatus(error);
                return;
            }

            RebuildCanvasFromGraph();
            SetStatus($"已生成 {newIndices.Length} 个副本节点。确认无误后点“保存”。");
        }

        private static string ResolveTypeId(SurfaceNode node)
        {
            var archetype = node?.Archetype;
            if (archetype == null)
                return null;

            // 签名带着注册表里的类型 id，那是建原型表时分配的。
            if (!string.IsNullOrEmpty(archetype.Signature))
                return archetype.Signature;

            return PCGNodeArchetypes.TryResolve(archetype.TypeID, out var name) ? name : null;
        }

        private void OnNodesConnected(IConnectionInstigator from, IConnectionInstigator to)
        {
            if (!TryConnectionPair(from, to, out var edge))
                return;

            for (int i = 0; i < _edges.Count; i++)
            {
                var existing = _edges[i];
                if (existing.SourceNodeID == edge.SourceNodeID && existing.SourcePin == edge.SourcePin &&
                    existing.TargetNodeID == edge.TargetNodeID && existing.TargetPin == edge.TargetPin)
                    return;
            }

            _edges.Add(edge);
            MarkDirty();
        }

        private void OnNodesDisconnected(IConnectionInstigator from, IConnectionInstigator to)
        {
            if (!TryConnectionPair(from, to, out var edge))
                return;

            for (int i = _edges.Count - 1; i >= 0; i--)
            {
                var existing = _edges[i];
                if (existing.SourceNodeID == edge.SourceNodeID && existing.SourcePin == edge.SourcePin &&
                    existing.TargetNodeID == edge.TargetNodeID && existing.TargetPin == edge.TargetPin)
                    {
                        _edges.RemoveAt(i);
                    }
            }

            MarkDirty();
        }

        private bool TryConnectionPair(IConnectionInstigator a, IConnectionInstigator b, out TrackedEdge edge)
        {
            edge = default;

            var boxA = a as Box;
            var boxB = b as Box;
            if (boxA == null || boxB == null)
                return false;

            var nodeA = boxA.ParentNode;
            var nodeB = boxB.ParentNode;
            if (nodeA == null || nodeB == null)
                return false;

            // 事件不说是哪一侧是输出；框自己知道。
            var outputBox = boxA.IsOutput ? boxA : boxB;
            var inputBox = boxA.IsOutput ? boxB : boxA;

            edge = new TrackedEdge(
                outputBox.ParentNode.ID, PinSlot(outputBox),
                inputBox.ParentNode.ID, PinSlot(inputBox));

            return true;
        }

        /// <summary>
        /// 把一个框 id 映射回它在该节点上的引脚下标。
        /// </summary>
        /// <remarks>
        /// 框在一个节点内是**先输入后输出**编号的，所以一个输出框的引脚下标是它的 id 减去
        /// 输入个数。节点类型解析不出来时退回用原始 id：一个错的引脚下标会把图连到
        /// 另一个引脚上，而原始 id 会让一张陈旧的图仍可载入、并在编译器的
        /// 引脚越界诊断里**显眼地**报错，而不是悄悄连错。
        /// </remarks>
        private static int PinSlot(Box box)
        {
            int id = box.ID;
            var prototype = CreatePrototype(ResolveTypeId(box.ParentNode));

            if (prototype == null)
                return id;

            return PCGNodePinSlots.PinIndex(box.IsOutput, id, prototype.InputPins?.Length ?? 0);
        }

        private static PCGNodeBase CreatePrototype(string typeId)
        {
            if (string.IsNullOrEmpty(typeId) ||
                !PCGNodeRegistry.TryGet(typeId, out var info) || info?.Factory == null)
                return null;

            try
            {
                return info.Factory(new PCGNodeRegistry.PCGNodeSerializationData(typeId, null, 0));
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ---- 参数面板 ---------------------------------------------------------

        /// <summary>一行可编辑的槽位：它的下标、种类、schema 条目，以及显示各部分值的那些控件。</summary>
        private sealed class ParameterRow
        {
            public int Slot;
            public PCGParameterType Kind;
            public PCGNodeParameterInfo Parameter;
            public TextBox[] Fields;

            /// <summary>资产/枚举槽位用的控件（选择器或下拉框）；文本框槽位为 null。</summary>
            public Control Editor;

            public CheckBox ExposeBox;
        }

        private void OnNodeValuesEdited(SurfaceNode node)
        {
            MarkDirty();

            // 经控件写一个值会再次触发这个事件。只在换了节点时重建，于是
            // 设计师敲到一半的数字永远不会被他手下的动作替换掉。
            if (node != null && !ReferenceEquals(node, _paramNode))
                RebuildParameterPanel(node);
        }

        /// <summary>
        /// 把选中节点的采样位置标到视口里，前提是干跑已经产出过采样。
        /// </summary>
        /// <remarks>
        /// 只对单选、且只对真的存在的采样：给每个选中节点的每个点画一个框
        /// 是一片标记墙而不是一个答案。这段绘制包在 try 里，因为这是窗口
        /// 与视口说话的唯一地方——当前编辑器状态下拿不到即时模式绘制时，
        /// 窗口应该说一次然后继续，而不是每帧都抛。
        /// </remarks>
        private void DrawSelectedSamples()
        {
            if (_lastInspection == null || _surface?.SelectedNodes == null || _surface.SelectedNodes.Count != 1)
                return;

            var indices = SelectedGraphIndices();
            if (indices.Count != 1)
                return;

            var summary = _lastInspection.Summarize(indices[0]);

            if (!summary.Ran || summary.Samples == null || summary.Samples.Length == 0)
                return;

            try
            {
                for (int i = 0; i < summary.Samples.Length; i++)
                {
                    var centre = summary.Samples[i];
                    var box = new BoundingBox(centre - new Vector3(4.0f), centre + new Vector3(4.0f));

                    DebugDraw.DrawWireBox(box, _sampleColor, 0.0f, false);
                }
            }
            catch (Exception ex)
            {
                if (_sampleDrawFailed)
                    return;

                _sampleDrawFailed = true;
                SetStatus($"视口绘制节点采样不可用：{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// 打开或关闭整图采样叠加。
        /// </summary>
        private void ToggleAllSamplesClicked()
        {
            _drawAllSamples = !_drawAllSamples;

            SetStatus(_drawAllSamples
                ? "视口叠加已打开：试跑后每个出点的节点各用一种颜色画出它的采样（仍可选中单个节点单独看）。"
                : "视口叠加已关闭。");
        }

        /// <summary>
        /// 每个产出过点的节点画一个框、按节点着色，于是整张图一次看完。
        /// </summary>
        /// <remarks>
        /// 一次看一个节点回答的是"这个节点传下去了什么"；全看回答的是"这张图把东西
        /// 放在哪儿了、那一片属于哪个节点"——那是设计师发现某一层落在意外之处时问的问题。
        /// 一个什么都没产出的节点就是**不在**这里；"它跑了但没有产出"由状态行里
        /// 的逐节点读数负责，在这里画一个标记就是撒谎。
        /// </remarks>
        private void DrawAllSamples()
        {
            if (!_drawAllSamples || _lastInspection == null)
                return;

            try
            {
                int count = _lastInspection.CollectDraws(_drawBuffer);

                for (int n = 0; n < count; n++)
                {
                    var draw = _drawBuffer[n];

                    for (int i = 0; i < draw.Samples.Length; i++)
                    {
                        var centre = draw.Samples[i];
                        var box = new BoundingBox(centre - new Vector3(4.0f), centre + new Vector3(4.0f));

                        DebugDraw.DrawWireBox(box, draw.Color, 0.0f, false);
                    }
                }
            }
            catch (Exception ex)
            {
                if (_sampleDrawFailed)
                    return;

                _sampleDrawFailed = true;
                SetStatus($"视口绘制节点采样不可用：{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// 上一次干跑里某个节点产出了什么；没什么可报时是 null。
        /// </summary>
        /// <remarks>
        /// "没跑"与"跑了但什么都没产出"是两个不同的答案、而且都有用——前者
        /// 通常意味着这个节点没接进 sink 那条路，后者意味着它的过滤条件一个都没匹配上——
        /// 所以两者的措辞是分开的，而不是都显示成零。
        /// </remarks>
        private string InspectionReadout(int nodeIndex)
        {
            if (_lastInspection == null || nodeIndex < 0)
                return null;

            var summary = _lastInspection.Summarize(nodeIndex);

            return summary.Ran
                ? $"节点 {nodeIndex} 上次试跑产出：{summary}"
                : $"节点 {nodeIndex} 上次试跑没有运行（未接在图里、或没被求值到）。";
        }

        private void OnSelectionChanged()
        {            var selected = _surface?.SelectedNodes;
            if (selected == null || selected.Count == 0)
            {
                RebuildParameterPanel(null);
                return;
            }

            if (selected.Count == 1)
            {
                RebuildParameterPanel(selected[0]);

                // 这个节点上次跑出来什么："哪个剪枝器吃掉了我的层"在干跑过一次之后就只是一次点击。
                // 离屏门够不到这里——它需要一个窗口——所以它被写成
                // 没什么可说时保持沉默，而不是显示一个会误导的零。
                var indices = SelectedGraphIndices();

                if (indices.Count == 1)
                {
                    var readout = InspectionReadout(indices[0]);

                    if (readout != null)
                        SetStatus(readout);
                }

                return;
            }

            // 多于一个节点：单节点那一列只描述得了最后一次点击，于是面板
            // 换成批量形态——整个选择**真正共有**的每个参数一行。
            RebuildMultiSelectPanel();
        }

        /// <summary>多选面板的行：哪个参数，以及它的控件从哪儿开始。</summary>
        private readonly List<TextBox> _batchFields = new List<TextBox>();

        private readonly List<int[]> _batchLayout = new List<int[]>();

        /// <summary>
        /// 显示整个选择共有的那些参数，每个参数一组控件加一个应用按钮。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 只提供**交集**：为一个某个选中节点没有的参数放一个控件，要么
        /// 悄悄跳过那些节点，要么写进一个在那里别有含义的槽位。文本经
        /// 单节点面板用的同一个 schema 转换，于是一次批量写入不会落成一个类型错的值、
        /// 而被图文件从此一直带着。
        /// </para>
        /// <para>
        /// 写的是图数据，也就是保存所序列化的东西。没有任何东西会自己持久化——一次批量
        /// edit that quietly rewrote a graph file would be worse than one needing a click on 保存.
        /// </para>
        /// </remarks>
        private void RebuildMultiSelectPanel()
        {
            _paramNode = null;
            _rows.Clear();

            if (_paramPanel == null)
                return;

            _paramPanel.DisposeChildren();
            _batchFields.Clear();
            _batchLayout.Clear();

            var indices = SelectedGraphIndices();
            var names = PCGGraphClipboard.CommonParameterNames(_graph, indices);

            _paramError = new Label
            {
                Parent = _paramPanel,
                Bounds = new Rectangle(6, Mathf.Max(30.0f, PanelHeight - 34.0f), PanelWidth - 12, 28.0f),
                Text = string.Empty,
            };

            int selectedCount = _surface?.SelectedNodes?.Count ?? 0;

            var title = new Label
            {
                Parent = _paramPanel,
                Bounds = new Rectangle(6, 4, PanelWidth - 12, 18),
                Text = $"多选：识别出 {indices.Count} / {selectedCount} 个节点，共同参数 {names.Count} 项",
            };

            if (indices.Count < selectedCount)
            {
                var warning = new Label
                {
                    Parent = _paramPanel,
                    Bounds = new Rectangle(6, 24, PanelWidth - 12, 18),
                    Text = "画布上有节点位置重叠或不在图表数据里，批量编辑只作用于识别出的节点。",
                };
            }

            if (names.Count == 0)
            {
                var none = new Label
                {
                    Parent = _paramPanel,
                    Bounds = new Rectangle(6, 46, PanelWidth - 12, 18),
                    Text = "选中的节点没有共同参数，或其中某个类型未注册。",
                };
                return;
            }

            float y = 46.0f;

            for (int i = 0; i < names.Count && y < PanelHeight - 90.0f; i++)
            {
                string name = names[i];
                bool knownKind = PCGGraphClipboard.TryGetParameterKind(_graph, indices, name, out var kind);
                int components = knownKind ? PCGParameterText.ComponentCount(kind) : 1;

                var label = new Label
                {
                    Parent = _paramPanel,
                    Bounds = new Rectangle(6, y, PanelWidth - 70, 16),
                    Text = name,
                };

                _batchLayout.Add(new[] { i, _batchFields.Count, components });

                float fieldWidth = Mathf.Max(28.0f, (PanelWidth - 74.0f) / components);

                for (int c = 0; c < components; c++)
                {
                    var field = new TextBox
                    {
                        Parent = _paramPanel,
                        Bounds = new Rectangle(6 + c * fieldWidth, y + 17, fieldWidth - 2.0f, 18.0f),
                        Text = string.Empty,
                    };

                    _batchFields.Add(field);
                }

                int rowIndex = i;
                var apply = new Button
                {
                    Parent = _paramPanel,
                    Bounds = new Rectangle(PanelWidth - 60, y + 17, 54, 18),
                    Text = "应用",
                };
                apply.Clicked += () => ApplyBatchEdit(rowIndex);

                y += 40.0f;
            }
        }

        private void ApplyBatchEdit(int rowIndex)
        {
            var indices = SelectedGraphIndices();
            var names = PCGGraphClipboard.CommonParameterNames(_graph, indices);

            if (rowIndex < 0 || rowIndex >= names.Count)
            {
                SetStatus("选中节点已经变了，请重新选择后再批量编辑。");
                return;
            }

            string name = names[rowIndex];

            int layoutIndex = -1;
            for (int i = 0; i < _batchLayout.Count; i++)
            {
                if (_batchLayout[i][0] == rowIndex)
                {
                    layoutIndex = i;
                    break;
                }
            }

            if (layoutIndex < 0)
            {
                SetStatus("面板已过时，请先重新选择节点。");
                return;
            }

            int firstField = _batchLayout[layoutIndex][1];
            int components = _batchLayout[layoutIndex][2];

            if (firstField + components > _batchFields.Count)
            {
                SetStatus("输入框已失效，请重新选择节点。");
                return;
            }

            var texts = new string[components];
            for (int c = 0; c < components; c++)
                texts[c] = _batchFields[firstField + c]?.Text ?? string.Empty;

            if (!PCGGraphClipboard.TryGetParameterKind(_graph, indices, name, out var kind))
            {
                SetStatus($"无法确定“{name}”的参数类型。");
                return;
            }

            if (!PCGParameterText.TryParse(kind, texts, out object value, out string parseError))
            {
                SetStatus(parseError);
                return;
            }

            if (!PCGGraphClipboard.SetParameterOnNodes(_graph, indices, name, value,
                    out int changed, out List<string> skipped, out string error))
            {
                SetStatus(error);
                return;
            }

            SetStatus($"已把“{name}”写到 {changed} 个节点" +
                      (skipped.Count > 0 ? $"，跳过 {skipped.Count} 个：{skipped[0]}" : "") +
                      "。改动只在图表数据里，记得点“保存”。");

            RebuildMultiSelectPanel();
        }

        /// <summary>按给定节点重建参数列；没有可用节点时显示一句提示。</summary>
        private void RebuildParameterPanel(SurfaceNode node)
        {
            _paramNode = node;
            _rows.Clear();

            if (_paramPanel == null)
                return;

            _paramPanel.DisposeChildren();

            // 与行一起重建：面板的子控件每次重建都被丢掉，所以一个
            // 能活下来的标签无论如何都得重新挂父。
            _paramError = new Label
            {
                Parent = _paramPanel,
                Bounds = new Rectangle(6, Mathf.Max(30.0f, PanelHeight - 34.0f), PanelWidth - 12, 28.0f),
                Text = string.Empty,
            };

            // 面板宽度先量一次并**无条件**记下来：摆行的那段代码若在早退路径上漏记，就会让 LayoutSurface 里那条
            // "宽度变了就重摆"每帧都成立，于是每一帧都重建一遍控件。
            float panel = _paramPanel != null && _paramPanel.Width > 1.0f ? _paramPanel.Width : PanelWidth;
            _rowsWidth = panel;

            if (node == null)
            {
                AddPanelHeader("参数", panel);

                new Label
                {
                    Parent = _paramPanel,
                    Bounds = PCGParameterLayout.Hint(panel),
                    Text = "单选一个节点后即可编辑它的参数。",
                };

                return;
            }

            string typeId = ResolveTypeId(node);
            PCGNodeRegistry.TryGet(typeId ?? string.Empty, out var info);
            var schema = info?.Parameters;

            AddPanelHeader(info?.DisplayName ?? typeId ?? "节点", panel);

            if (schema == null || schema.Length == 0)
            {
                new Label
                {
                    Parent = _paramPanel,
                    Bounds = PCGParameterLayout.Hint(panel),
                    Text = "该节点没有参数。",
                };

                return;
            }

            float y = 28.0f;

            for (int i = 0; i < schema.Length; i++)
            {
                var parameter = schema[i];
                if (parameter == null)
                    continue;

                int components = PCGParameterText.ComponentCount(parameter.Kind);
                var texts = PCGParameterText.Components(parameter.Kind, ValueAt(node.Values, i));

                // 摆放规则住在 PCGParameterLayout（纯函数，离屏断言钉住"每个矩形都在面板之内"）。
                // 这里只把算出来的矩形赋给控件，不再自己抄一遍公式——抄两遍的公式迟早会走偏，
                // 而"输入框伸出去了"这类缺陷只会在屏幕上显形。
                var rowLayout = PCGParameterLayout.Row(panel, components);

                new Label
                {
                    Parent = _paramPanel,
                    Bounds = Shift(rowLayout.Label, y),
                    Text = PCGParameterText.LabelFor(parameter),
                };

                var row = new ParameterRow
                {
                    Slot = i,
                    Kind = parameter.Kind,
                    Parameter = parameter,
                    Fields = new TextBox[components],
                };

                // 暴露勾选框与槽位名同一行：一列只能靠
                // 改尺寸来滚动的面板没有余量给"一个勾选框一行"。
                var exposeLabel = new Label
                {
                    Parent = _paramPanel,
                    Bounds = Shift(rowLayout.ExposeLabel, y),
                    Text = "暴露",
                };

                var exposeBox = new CheckBox
                {
                    Parent = _paramPanel,
                    Bounds = Shift(rowLayout.ExposeBox, y),
                    Checked = FindExposed(node.ID, i) >= 0,
                };

                var capturedRow = row;
                exposeBox.StateChanged += box => OnExposeToggled(capturedRow, box.Checked);

                row.ExposeBox = exposeBox;

                // 控件按种类分派：资产给**选择器**（可筛类型、带搜索），枚举给**下拉框**（从写好的选项里挑）。
                // 两者都只有一份值，所以只在 components == 1 时替换文本框；其余仍是文本框。
                var editorKind = components == 1
                    ? PCGParameterText.EditorKindOf(parameter.Name, parameter.Kind)
                    : PCGParameterText.PCGParameterEditorKind.Text;

                if (editorKind == PCGParameterText.PCGParameterEditorKind.PathPicker)
                {
                    // 路径槽位与资产槽位用同一个选择器：可筛、带搜索弹窗；区别只在写回的东西——
                    // 这里写的是**工程相对路径**（引擎的 `SelectedPath` 就是这个形式），
                    // 因为生成器在运行期按路径解析资产，而不是存一个 id。
                    var filePicker = new AssetPicker(FlaxEditor.Scripting.ScriptType.Null, Float2.Zero)
                    {
                        Parent = _paramPanel,
                        Bounds = Shift(rowLayout.FieldRect(0), y),
                    };

                    bool isGraph = string.Equals(parameter.Name, "Child Graph", StringComparison.Ordinal);
                    filePicker.Validator.FileExtension = isGraph ? ".json" : ".flax";

                    // 有值就回填；解析不到时留空（选择器的 setter 自己会去 ContentDatabase 找）。
                    if (ValueAt(node.Values, i) is string path && !string.IsNullOrEmpty(path))
                        filePicker.Validator.SelectedPath = path;

                    filePicker.SelectedItemChanged += () => OnPathPicked(capturedRow, filePicker);
                    row.Editor = filePicker;
                }
                else if (editorKind == PCGParameterText.PCGParameterEditorKind.Asset)
                {
                    var picker = new AssetPicker(AssetTypeFor(parameter.Name), Float2.Zero)
                    {
                        Parent = _paramPanel,
                        Bounds = Shift(rowLayout.FieldRect(0), y),
                    };

                    // 有值才回填：空引用时留空，免得把 Guid.Empty 显示成一个"已选中"的资产。
                    if (ValueAt(node.Values, i) is Guid current && current != Guid.Empty)
                        picker.Validator.SelectedID = current;

                    picker.SelectedItemChanged += () => OnAssetPicked(capturedRow, picker);
                    row.Editor = picker;
                }
                else if (editorKind == PCGParameterText.PCGParameterEditorKind.Combo)
                {
                    var combo = new ComboBox(6.0f, 0.0f, Mathf.Max(24.0f, rowLayout.FieldWidth))
                    {
                        Parent = _paramPanel,
                        Bounds = Shift(rowLayout.FieldRect(0), y),
                    };

                    // 选项按中文标签填，但**值仍是序号**——写回的是 SelectedIndex，与节点的读法一致。
                    if (parameter.Options != null)
                    {
                        foreach (var option in parameter.Options)
                            combo.AddItem(PCGParameterLabels.Label(option));
                    }

                    int current = texts.Length > 0 && int.TryParse(texts[0], out int parsed) ? parsed : 0;
                    combo.SelectedIndex = Mathf.Clamp(current, 0, Mathf.Max(0, (parameter.Options?.Length ?? 1) - 1));
                    combo.SelectedIndexChanged += picked => OnEnumPicked(capturedRow, picked);
                    row.Editor = combo;
                }
                else
                {
                    for (int c = 0; c < components; c++)
                    {
                        var field = new TextBox
                        {
                            Parent = _paramPanel,
                            Bounds = Shift(rowLayout.FieldRect(c), y),
                            Text = c < texts.Length ? texts[c] : string.Empty,
                        };

                        field.TextChanged += () => OnParameterFieldChanged(capturedRow);
                        row.Fields[c] = field;
                    }
                }

                y += rowLayout.RowAdvance;
                _rows.Add(row);
            }

            // 参数列每次都自报一句：有了它，"选中了节点但列里什么都没有"就能一步分成三种——
            // 选中没传到（没有这行日志）、节点查不到 schema（行数 0 且说了"没有参数"）、
            // 或者行建出来了却没显示（行数 > 0 却看不见 → 那是绘制/尺寸问题，不是数据问题）。
            Debug.Log($"[PCG] 参数列：{info?.DisplayName ?? typeId ?? "节点"} → {_rows.Count} 行（列宽 {panel:F0}）");
        }

        /// <summary>资产槽位按名字挑可接受的资产类型；认不出来就给"任意资产"。</summary>
        /// <remarks>
        /// 选择器带着类型走，于是挑资产时列表里只出现对得上的东西——"给植被槽位挑了一张贴花"这种事
        /// 在选的时候就没了，而不是等到运行期报"没有指定模型"。
        /// </remarks>
        private static FlaxEditor.Scripting.ScriptType AssetTypeFor(string slotName)
        {
            switch (slotName)
            {
            case "Model": return new FlaxEditor.Scripting.ScriptType(typeof(Model));
            case "Prefab": return new FlaxEditor.Scripting.ScriptType(typeof(Prefab));
            case "Material": return new FlaxEditor.Scripting.ScriptType(typeof(MaterialBase));
            case "Actor":
            case "Spline Actor": return new FlaxEditor.Scripting.ScriptType(typeof(Actor));
            default: return new FlaxEditor.Scripting.ScriptType(typeof(Asset));
            }
        }

        /// <summary>路径选择器挑完：把工程相对路径写进那一槽（规整成统一的正斜杠）。</summary>
        private void OnPathPicked(ParameterRow row, AssetPicker picker)
        {
            WriteParameter(row, PCGParameterText.NormalizeContentPath(picker?.Validator?.SelectedPath));
        }

        /// <summary>资产选择器挑完：把资产 id 写进那一槽，与文本框走同一条写回路径。</summary>
        private void OnAssetPicked(ParameterRow row, AssetPicker picker)
        {
            WriteParameter(row, picker?.Validator?.SelectedID ?? Guid.Empty);
        }

        /// <summary>下拉框挑完：写回选项序号（节点读的就是这个序号）。</summary>
        private void OnEnumPicked(ParameterRow row, ComboBox combo)
        {
            WriteParameter(row, combo?.SelectedIndex ?? 0);
        }

        /// <summary>把一个值写进这一行对应的槽位，并让画布知道图变了。</summary>
        private void WriteParameter(ParameterRow row, object value)
        {
            if (_paramNode == null || row == null)
                return;

            _paramNode.Values = PCGParameterText.WithValue(_paramNode.Values, row.Slot, value);
            _surface?.OnNodeValuesEdited(_paramNode);
            MarkDirty();
        }

        /// <summary>把 <see cref="PCGParameterLayout"/> 算出来的行内矩形平移到当前行的高度上。</summary>
        private static Rectangle Shift(Rectangle rect, float y) =>
            new Rectangle(rect.X, y + rect.Y, rect.Width, rect.Height);

        /// <summary>
        /// 参数列的标题条：一条底色加一行字。
        /// </summary>
        /// <remarks>
        /// 没有它，这一列在没选中节点时只剩半透明底色——和空画布几乎同色，于是"属性列是空的"看上去就是
        /// "这个编辑器没有属性编辑功能"（本轮反馈的原话）。标题条把它变成一块**看得见的面板**。
        /// </remarks>
        private void AddPanelHeader(string text, float panel)
        {
            new Panel
            {
                Parent = _paramPanel,
                Bounds = PCGParameterLayout.Header(panel),
                BackgroundColor = new Color(0.24f, 0.25f, 0.28f, 1.0f),
            };

            new Label
            {
                Parent = _paramPanel,
                Bounds = PCGParameterLayout.Header(panel),
                Text = text,
                Bold = true,
            };
        }

        private void OnParameterFieldChanged(ParameterRow row)
        {
            if (_paramNode == null || row?.Fields == null)
                return;

            var texts = new string[row.Fields.Length];
            for (int i = 0; i < texts.Length; i++)
                texts[i] = row.Fields[i]?.Text;

            if (!PCGParameterText.TryParse(row.Kind, texts, out var value, out string error))
            {
                // 报错并保留上一个值：写进一个敲到一半的数字会改变图
                // 跑出来的东西，而拒绝则让运行结果与最后一次合法编辑保持一致。
                if (_paramError != null)
                    _paramError.Text = error;
                return;
            }

            if (_paramError != null)
                _paramError.Text = string.Empty;

            _paramNode.Values = PCGParameterText.WithValue(_paramNode.Values, row.Slot, value);
            _surface?.OnNodeValuesEdited(_paramNode);
            MarkDirty();
        }

        private static object ValueAt(object[] values, int index)
        {
            if (values == null || index < 0 || index >= values.Length)
                return null;

            return values[index];
        }

        private void MarkDirty() => _dirty = true;

        private void SetStatus(string message)
        {
            if (_statusLabel != null)
                _statusLabel.Text = message;
        }
    }
}
