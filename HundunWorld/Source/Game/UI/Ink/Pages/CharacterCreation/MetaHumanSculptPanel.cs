using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using MetaHuman;
using MetaHuman.Runtime;

namespace HundunWorld.Game.UI.Ink.Pages.CharacterCreation
{
    /// <summary>
    /// MetaHuman 数值捏脸面板：只认 <see cref="MetaHumanCharacter"/> 与 Flax GUI，
    /// 不依赖任何 StyleSystem / Ink 框架类型——这样它能被 Tools/MetaHumanUIGate 单独对着
    /// FlaxEngine.CSharp.dll 编一遍（Source/Game 整体带 Orleans 等网络 DLL，编不进这个门）。
    /// 与 Ink 的接合（找预览 Actor、挂组件、塞进舞台页）留在 CcCreationPages.cs 里，那部分只能人肉复核。
    /// </summary>
    public sealed class MetaHumanSculptPanel : Panel
    {
        // 设计空间 1920x1080，面板由宿主摆到左侧留白区；这里全部按父级真实尺寸在 RefreshLayout 里排布。
        private const float Pad = 12f;
        /// <summary>分区镜头按钮的行高。</summary>
        private const float SectionH = 26f;

        /// <summary>分区镜头：页签内各部位的按钮文案与镜头 id（id 由宿主转给预览组件的 FocusOn）。</summary>
        private static readonly string[][] SectionLabels =
        {
            new[] { "头型", "头发", "眼睛", "脸颊", "下巴", "脖子" },
            new[] { "躯干", "四肢", "三围" },
        };

        /// <summary>
        /// 与 <see cref="SectionLabels"/> 一一对应的镜头 id。
        /// 「头发」目前映射到头部取景：模板里没有头发通道（毛发卡片烘焙路线已判死），
        /// 所以它只动镜头、不动参数 —— 等头发通道落地再给它单列一档。
        /// </summary>
        private static readonly string[][] SectionIds =
        {
            new[] { "head", "head", "eyes", "cheeks", "chin", "neck" },
            new[] { "torso", "limbs", "proportions" },
        };

        private const float TabW = 130f;
        private const float TabH = 34f;
        private const float RowH = 42f;
        private const float BtnH = 30f;
        private const float StepRowH = 26f;
        /// <summary>
        /// 单页行数上限。这个数必须由"分界之后的实测条数"决定，不能凭记忆写：
        /// 旧值 400 的注释写着"头部约 354 条"，而 2026-10-15 把分界交给 <c>PartitionForUI</c> 之后
        /// 头部桶实测 <b>598</b> 条（semantic 120 + advanced 478，见 Tools/MhHeadRowBudgetCensus.py）
        /// ⇒ 专家模式下 400 会**静默截掉 198 根头部滑杆**（只在状态栏留一句"仅显示前 400 项"）。
        /// 滑块区本身是带垂直滚动条的 Panel，超出视口的行可滚到，所以这里不需要用截断来控高度。
        /// 注意：这条只改到"数据能全进界面"，640 行 × 3 个控件的运行期开销没在 PIE 上量过（待目视/实测）。
        /// </summary>
        private const int MaxRowsPerTab = 640;

        private static readonly Color BgColor = new Color(0.10f, 0.10f, 0.13f, 0.82f);
        private static readonly Color Accent = new Color(0.85f, 0.78f, 0.55f);
        private static readonly Color AccentDim = new Color(0.45f, 0.42f, 0.34f);
        private static readonly Color TextColor = new Color(0.92f, 0.92f, 0.95f);
        private static readonly Color WarnColor = new Color(0.95f, 0.55f, 0.45f);

        private MetaHumanCharacter _character;

        private Button _headTab;
        private Button _bodyTab;
        private CheckBox _linkCheck;
        private CheckBox _expertCheck;
        private Label _expertLabel;
        private Label _linkLabel;
        private Panel _sliderScroll;
        private Panel _stepScroll;
        private Button _randomBtn;
        private Button _resetBtn;
        private Button _presetBtn;
        private Button _undoBtn;
        private Button _redoBtn;
        private Button _saveBtn;
        private Button _publishBtn;
        private Label _status;

        private readonly List<MetaHumanParameter> _headParams = new List<MetaHumanParameter>();
        private int _hiddenAdvanced;
        private readonly List<MetaHumanParameter> _bodyParams = new List<MetaHumanParameter>();
        private readonly List<SliderRow> _rows = new List<SliderRow>();

        /// <summary>瞳色色板：把模板参数 eye.iris_color 的 0..1 指数直接做成可点的色点。</summary>
        private CcSwatchRow _eyeSwatch;

        /// <summary>发色色板：把模板参数 hair.color 的 0..1 指数直接做成可点的色点。</summary>
        private CcSwatchRow _hairSwatch;

        /// <summary>发型选择按钮（离散下标 → 模板参数 hair.style）。</summary>
        private Button[] _hairStyleButtons;

        private const float StyleRowH = 28f;

        /// <summary>肤色色板：把模板参数 skin.melanin 的 0..1 指数直接做成可点的色点。</summary>
        private CcSwatchRow _skinSwatch;
        private readonly List<Button> _stepButtons = new List<Button>();
        private List<string> _presetIds = new List<string>();

        private bool _showBody;
        private bool _syncing;       // 程序化回填滑块值时压掉 Adjust/Commit，避免回环
        private bool _confirmPublish;
        private int _randomSeed = 1;

        /// <summary>宿主可读取，用于确认本页有内容可显示。</summary>
        public bool HasContent { get { return _character != null && _character.IsLoaded; } }

        /// <summary>
        /// 写之前让宿主确认"写回目标还活着"的钩子（创角页面把它指到自己的 AttachSculpt）。
        /// <para>
        /// 为什么需要：预览的那颗头会随页面重建，而捏脸会话是跨页留着的 ⇒ 组件还活着、目标已成孤儿。
        /// 2026-10-18 实测：一次 PIE 里 <c>AnimatedModel.GetNodeTransformation</c> 抛原生空引用 35 次，
        /// 整条写回链在第一具死目标上就断掉，症状是"值改了脸不动"。布局只改窗口尺寸时才跑，救不了这个时机，
        /// 所以把重指认挂在"确实要写之前"。
        /// </para>
        /// </summary>
        public Action EnsureWriteTarget;

        public MetaHumanSculptPanel()
        {
            BackgroundColor = BgColor;
            ClipChildren = true;
            AnchorPreset = AnchorPresets.TopLeft;

            BuildChrome();
        }

        private void BuildChrome()
        {
            _headTab = MakeButton("头部", () => SelectTab(false));
            _bodyTab = MakeButton("身体", () => SelectTab(true));

            _linkCheck = new CheckBox
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Checked = true,
                Parent = this,
            };
            _linkCheck.StateChanged += _ => OnLinkToggled();

            // 默认只铺语义滑杆：598 根原始滑杆压到玩家脸上，看起来就像功能没做完
            _expertCheck = new CheckBox
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Checked = false,
                Parent = this,
            };
            _expertCheck.StateChanged += _ => OnExpertToggled();
            _expertLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "专家模式（逐关节细调）",
                Parent = this,
            };

            // Flax 的 CheckBox 不自带文字，配一个 Label 同行显示
            _linkLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Text = "左右联动",
                TextColor = TextColor,
                Parent = this,
            };

            _sliderScroll = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
                ScrollBars = ScrollBars.Vertical,
                Parent = this,
            };

            // 瞳色色板：色板下标与模板参数 eye.iris_color（0..1）一一对应，玩家点一下就能换瞳色。
            _skinSwatch = new CcSwatchRow
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Label = "肤色",
                Options = CcFacePresets.SkinColors,
                Parent = _sliderScroll,
            };
            _skinSwatch.SelectedChanged += OnSkinSwatchChanged;

            _eyeSwatch = new CcSwatchRow
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Label = "瞳色",
                Options = CcFacePresets.EyeColors,
                Parent = _sliderScroll,
            };
            _eyeSwatch.SelectedChanged += OnEyeSwatchChanged;

            _hairSwatch = new CcSwatchRow
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Label = "发色",
                Options = CcFacePresets.HairColors,
                Parent = _sliderScroll,
            };
            _hairSwatch.SelectedChanged += OnHairSwatchChanged;

            // 发型选择：6 个文字按钮，直接写模板参数 hair.style（与滑杆/色板同一条 Adjust/Commit 链）。
            _hairStyleButtons = new Button[HundunWorld.Game.Character.HairStyleCatalog.Count];
            for (int i = 0; i < _hairStyleButtons.Length; i++)
            {
                int index = i;
                var styleButton = new Button
                {
                    Text = HundunWorld.Game.Character.HairStyleCatalog.NameFor(i),
                    AnchorPreset = AnchorPresets.TopLeft,
                    BackgroundColor = AccentDim,
                    TextColor = TextColor,
                    Parent = _sliderScroll,
                };
                styleButton.Clicked += () => OnHairStyleClicked(index);
                _hairStyleButtons[i] = styleButton;
            }

            _randomBtn = MakeButton("随机", OnRandomize);
            _resetBtn = MakeButton("重置", OnReset);
            _presetBtn = MakeButton("预设", OnApplyPreset);
            _undoBtn = MakeButton("撤销", OnUndo);
            _redoBtn = MakeButton("重做", OnRedo);
            _saveBtn = MakeButton("保存", OnSave);
            _publishBtn = MakeButton("发布", OnPublish);

            _stepScroll = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
                ScrollBars = ScrollBars.Vertical,
                Parent = this,
            };

            _status = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                TextColor = TextColor,
                Text = string.Empty,
                Parent = this,
            };
        }

        private Button MakeButton(string text, Action onClick)
        {
            Button button = new Button
            {
                Text = text,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = AccentDim,
                TextColor = TextColor,
                Parent = this,
            };
            button.Clicked += onClick;
            return button;
        }

        // ===================================================================
        // 对外入口
        // =======================================================================

        /// <summary>
        /// 服务端说这份存档已发布 ⇒ 本机 Session 一起封存。
        /// 不接这一步，本机会以为还能改、还能回溯，而每次提交都被服务端拒 —— 玩家看到的是无规律的失败。
        /// <para>由宿主页面去订阅服务事件再调这里：本面板刻意不认识 Ink/服务层类型
        /// （见类注释），否则 <c>Tools/MetaHumanUIGate</c> 那道只编本文件的离屏门就编不动了。</para>
        /// </summary>
        public void OnServerSealedByService()
        {
            if (_character == null || _character.IsSealed || _character.Session == null)
            {
                return;
            }

            _character.Session.History.Seal();
            // 定格要立刻反映到控件上：滑杆、撤销/重做、步骤条都是建好之后才存在的，
            // 只改会话状态不动界面 = 玩家还能拖出一串不会生效的读数。
            RefreshSteps();
            SetStatus("该角色外观已在服务端发布，本机回溯与再编辑已关闭", true);
        }

        /// <summary>绑定角色组件并建立页签内容。未装载时把诊断摊到状态栏，绝不显示空页。</summary>
        public void Bind(MetaHumanCharacter character)
        {
            _character = character;
            _bindCount++;
            _lastBindArgNull = character == null;

            // 一次面板生命周期里 Bind 应该只发生一次、且参数不为 null。
            // 这条回执存在的理由：2026-10-18 实测到"滑杆 120 行俱在、_character 却为 null"这种
            // 按现有代码路径不该出现的组合，只能把每次 Bind 的实例编号与行数记下来才能分清是谁改的。
            Probe(string.Format("[SculptBind] panel=#{0} call=#{1} argNull={2} rowsBefore={3} build={4}",
                _serial, _bindCount, _lastBindArgNull, _rows.Count, BuildStamp));

            // 服务端封存事件由宿主页面订阅后再回调 OnServerSealedByService（本面板不认识服务层类型）。

            _headParams.Clear();
            _bodyParams.Clear();
            _presetIds.Clear();

            if (_character == null)
            {
                SetStatus("未找到可绑定的 MetaHumanCharacter。", true);
                RebuildTab();
                return;
            }

            if (!_character.IsLoaded || _character.Session == null)
            {
                SetStatus("装载失败：" + _character.BuildDiagnosticReport(), true);
                RebuildTab();
                return;
            }

            GatherParameters();
            GatherPresets();

            _linkCheck.Checked = _character.Session.State.LinkSymmetric;
            RebuildTab();
            RebuildSections();
            RefreshSteps();
            SetStatus(BuildReadyLine(), false);
            ApplyIncomingPreset();
        }

        /// <summary>
        /// 宿主在 <see cref="Bind"/> 之前写入：上一步「面容预设」对应的模板预设 id（null/空 = 没选）。
        /// 面板不认识 <c>CcSession</c>/<c>CcFacePresets</c>（那两类在 Ink 侧，本面板要能被
        /// <c>Tools/MetaHumanUIGate</c> 单文件编过），所以名字与 id 都由宿主注入。
        /// </summary>
        public string IncomingPresetId;

        /// <summary>预设的展示名，只用于状态栏文案。</summary>
        public string IncomingPresetName;

        /// <summary>
        /// 把上一步「面容预设」落进本次会话：作为基线之前的第一步应用（可撤销、可回溯）。
        /// <para>
        /// 只在会话还是空的时候应用 —— 会话现在跨页留着，重复进入捏脸页时再应用一次，
        /// 会把玩家自己调好的脸覆盖掉。
        /// </para>
        /// <para>
        /// 模板里没有这个预设的数值时**明说**而不是静默留 0：那说明缺的是内容（预设没配数值），
        /// 不是通路断了。
        /// </para>
        /// </summary>
        private void ApplyIncomingPreset()
        {
            string id = IncomingPresetId;
            if (string.IsNullOrEmpty(id) || _character == null || _character.Session == null)
            {
                return;
            }

            if (_character.HistoryStepCount > 1)
            {
                return;
            }

            string name = string.IsNullOrEmpty(IncomingPresetName) ? id : IncomingPresetName;

            if (_presetIds.IndexOf(id) < 0)
            {
                SetStatus("面容预设「" + name + "」在模板里没有对应数值，脸部不会因此改变（缺的是内容，不是通路）。", true);
                Probe(string.Format("[SculptPreset] miss id={0} templatePresets={1}", id, _presetIds.Count));
                return;
            }

            if (_character.ApplyPreset(id, "面容预设 " + name))
            {
                Probe(string.Format("[SculptPreset] applied id={0}", id));
                SyncSliderValues();
                RefreshSteps();
                SetStatus(BuildReadyLine(), false);
            }
            else
            {
                SetStatus("面容预设「" + name + "」应用失败。", true);
            }
        }

        private void GatherParameters()
        {
            _headParams.Clear();
            _bodyParams.Clear();
            int hidden = 0;

            // 头身分界交给纯核的 PartitionForUI（除 Body 区域一律算头部）。
            // 本页以前自带一份十条的区域清单，Forehead/Scalp/Teeth/Tongue/Hair/Skin 共 154 条头部参数
            // （占脸模板 26%）因此整组不进界面，而面板、状态栏、自检全都一切如常：
            // 少了一批滑杆不会留下任何失败痕迹，所以分界不能由调用方另记一张表。
            List<MetaHumanParameter> head = new List<MetaHumanParameter>();
            List<MetaHumanParameter> body = new List<MetaHumanParameter>();
            if (_character.Session != null)
            {
                _character.Session.Template.PartitionForUI(head, body);
            }

            bool expert = _expertCheck != null && _expertCheck.Checked;
            AppendByGroup(_headParams, head, expert, ref hidden);
            AppendByGroup(_bodyParams, body, expert, ref hidden);
            _hiddenAdvanced = hidden;
        }

        /// <summary>按 Group 分层收参数：advanced 只在专家模式下出现（否则 478 根原始滑杆会淹没语义层）。</summary>
        private void AppendByGroup(List<MetaHumanParameter> target, List<MetaHumanParameter> source, bool expert, ref int hidden)
        {
            for (int i = 0; i < source.Count; i++)
            {
                string group = string.IsNullOrEmpty(source[i].Group) ? "semantic" : source[i].Group;
                if (group == "advanced" && !expert)
                {
                    hidden++;
                    continue;
                }

                target.Add(source[i]);
            }
        }

        private void OnExpertToggled()
        {
            GatherParameters();
            RebuildTab();
            RefreshLayout();
            SetStatus(string.Format("专家模式{0}：头部 {1} 项 / 身体 {2} 项，隐藏原始细调 {3} 项",
                                    _expertCheck.Checked ? "开" : "关",
                                    _headParams.Count, _bodyParams.Count, _hiddenAdvanced), false);
        }

        private void GatherPresets()
        {
            List<MetaHumanPreset> presets = _character.Session.Template.Presets;
            for (int i = 0; i < presets.Count; i++)
            {
                if (!string.IsNullOrEmpty(presets[i].Id))
                {
                    _presetIds.Add(presets[i].Id);
                }
            }

            _presetBtn.Enabled = _presetIds.Count > 0;
        }

        private string BuildReadyLine()
        {
            return string.Format("头部 {0} 项 / 身体 {1} 项 · 步骤 {2}/{3}",
                                 _headParams.Count, _bodyParams.Count,
                                 _character.HistoryStepCount, _character.HistoryCursor);
        }

        /// <summary>
        /// 页签切换时通知宿主（true=身体页）。宿主据此切换预览用哪具身体：
        /// 身体参数只能写在**蒙皮**模型上，而"模板身体"是静态网格、对骨骼/形状参数毫无反应
        /// （2026-10-19：身体页怎么拖都不变，因为看的那具根本不接受写入）。
        /// </summary>
        public Action<bool> ShowBodyMode;

        /// <summary>发色下标变化（预览据此实时换色；世界侧仍走 AppearanceInfo.HairColor）。</summary>
        public Action<int> HairColorChanged;

        /// <summary>点分区按钮时通知宿主（参数是镜头 id，见 <see cref="SectionIds"/>）。</summary>
        public Action<string> ShowSection;

        private readonly List<Button> _sectionButtons = new List<Button>();
        private readonly List<string> _sectionIds = new List<string>();


        private void SelectTab(bool body)
        {
            _showBody = body;
            CancelPublishConfirm();
            RebuildTab();
            RebuildSections();
            if (ShowBodyMode != null)
            {
                ShowBodyMode(body);
            }
        }

        /// <summary>按当前页签重建分区镜头按钮（页签切换时调用）。</summary>
        private void RebuildSections()
        {
            for (int i = 0; i < _sectionButtons.Count; i++)
            {
                _sectionButtons[i].Dispose();
            }

            _sectionButtons.Clear();
            _sectionIds.Clear();

            int tab = _showBody ? 1 : 0;
            string[] labels = SectionLabels[tab];
            string[] ids = SectionIds[tab];
            for (int i = 0; i < labels.Length; i++)
            {
                string id = ids[i];
                Button button = new Button
                {
                    Text = labels[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    BackgroundColor = AccentDim,
                    TextColor = TextColor,
                    Parent = this,
                };
                button.Clicked += () =>
                {
                    if (ShowSection != null)
                    {
                        ShowSection(id);
                    }
                };
                _sectionButtons.Add(button);
                _sectionIds.Add(id);
            }

            RelayoutSections();
        }

        /// <summary>把分区按钮排在页签行下面（一行铺满）。</summary>
        private void RelayoutSections()
        {
            if (_sectionButtons.Count == 0)
            {
                return;
            }

            float w = Width - Pad * 2f;
            float gap = 6f;
            float bw = Mathf.Max(48f, (w - gap * (_sectionButtons.Count - 1)) / _sectionButtons.Count);
            float top = Pad + TabH + 8f;
            for (int i = 0; i < _sectionButtons.Count; i++)
            {
                _sectionButtons[i].Bounds = new Rectangle(Pad + (bw + gap) * i, top, bw, SectionH);
            }
        }

        // ===================================================================
        // 布局：全部依赖父级真实尺寸，宿主摆好 Size 后调用
        // =======================================================================

        public void RefreshLayout()
        {
            if (Width <= 0f || Height <= 0f)
            {
                return;
            }

            float w = Width - Pad * 2f;
            float x = Pad;
            float y = Pad;

            _headTab.Bounds = new Rectangle(x, y, TabW, TabH);
            _bodyTab.Bounds = new Rectangle(x + TabW + 8f, y, TabW, TabH);
            float linkX = x + (TabW * 2f + 8f) + 16f;
            _linkCheck.Bounds = new Rectangle(linkX, y + 6f, 20f, 20f);
            _linkLabel.Bounds = new Rectangle(linkX + 26f, y + 6f, 120f, 20f);
            float expertX = linkX + 26f + 128f;
            _expertCheck.Bounds = new Rectangle(expertX, y + 6f, 20f, 20f);
            _expertLabel.Bounds = new Rectangle(expertX + 26f, y + 6f, 220f, 20f);

            y += TabH + 8f;
            RelayoutSections();
            y += SectionH + 6f;
            _status.Bounds = new Rectangle(x, y, w, 20f);
            y += 22f;

            // 按钮两行：操作 / 历史
            float btnW = (w - 6 * 6f) / 7f;
            float rowTop = y;
            Place(_randomBtn, x, rowTop, btnW);
            Place(_resetBtn, x + (btnW + 6f), rowTop, btnW);
            Place(_presetBtn, x + (btnW + 6f) * 2f, rowTop, btnW);
            Place(_saveBtn, x + (btnW + 6f) * 3f, rowTop, btnW);
            Place(_undoBtn, x + (btnW + 6f) * 4f, rowTop, btnW);
            Place(_redoBtn, x + (btnW + 6f) * 5f, rowTop, btnW);
            Place(_publishBtn, x + (btnW + 6f) * 6f, rowTop, btnW);

            y = rowTop + BtnH + 10f;

            // 步骤条固定占底部一块，滑块区吃掉剩余高度
            float stepH = Mathf.Min(180f, Height * 0.26f);
            float bottomBlock = BtnH + stepH + 24f; // 「回溯步骤」标题 + 按钮行 + 步骤滚动
            float sliderH = Mathf.Max(120f, Height - y - bottomBlock);

            _sliderScroll.Bounds = new Rectangle(x, y, w, sliderH);

            float afterSliders = y + sliderH + 8f;
            _undoBtn.Y = afterSliders; // 撤销/重做/发布在底部另排（见下）；此处仅占位重算
            PlaceBottom(_undoBtn, x, afterSliders, btnW);
            PlaceBottom(_redoBtn, x + (btnW + 6f), afterSliders, btnW);
            PlaceBottom(_publishBtn, x + (btnW + 6f) * 2f, afterSliders, btnW);

            float stepTop = afterSliders + BtnH + 6f;
            _stepScroll.Bounds = new Rectangle(x, stepTop, w, stepH);

            RelayoutRows();
            RelayoutSteps();
        }

        private void Place(Button button, float x, float y, float w)
        {
            button.Bounds = new Rectangle(x, y, w, BtnH);
        }

        private void PlaceBottom(Button button, float x, float y, float w)
        {
            button.Bounds = new Rectangle(x, y, w, BtnH);
        }

        private void RelayoutRows()
        {
            float w = _sliderScroll.Width;
            float labelW = Mathf.Max(160f, w * 0.42f);
            float valueW = 70f;
            float sliderW = Mathf.Max(80f, w - labelW - valueW - 24f);

            // 肤色/发色/瞳色三行色板只在头部页签出现；滑杆整体下移，别让色板压住第一行。
            float offset = 0f;
            if (_eyeSwatch != null)
            {
                bool showSwatch = !_showBody;
                const float swatchH = 34f;
                _eyeSwatch.Visible = showSwatch;
                if (_hairSwatch != null)
                {
                    _hairSwatch.Visible = showSwatch;
                }

                if (_skinSwatch != null)
                {
                    _skinSwatch.Visible = showSwatch;
                }

                if (showSwatch)
                {
                    float top = 0f;
                    if (_skinSwatch != null)
                    {
                        _skinSwatch.Bounds = new Rectangle(0f, top, Mathf.Min(w, _skinSwatch.PreferredWidth), swatchH);
                        top += swatchH + 4f;
                    }

                    if (_hairSwatch != null)
                    {
                        _hairSwatch.Bounds = new Rectangle(0f, top, Mathf.Min(w, _hairSwatch.PreferredWidth), swatchH);
                        top += swatchH + 4f;
                    }

                    _eyeSwatch.Bounds = new Rectangle(0f, top, Mathf.Min(w, _eyeSwatch.PreferredWidth), swatchH);
                    top += swatchH + 4f;

                    if (_hairStyleButtons != null)
                    {
                        float gap = 4f;
                        float styleW = Mathf.Max(56f, (w - gap * (_hairStyleButtons.Length - 1)) / _hairStyleButtons.Length);
                        for (int i = 0; i < _hairStyleButtons.Length; i++)
                        {
                            Button styleButton = _hairStyleButtons[i];
                            styleButton.Visible = showSwatch;
                            if (showSwatch)
                            {
                                styleButton.Bounds = new Rectangle(i * (styleW + gap), top, styleW, StyleRowH);
                            }
                        }

                        if (showSwatch)
                        {
                            top += StyleRowH + 4f;
                        }
                    }

                    offset = top;
                }
            }

            for (int i = 0; i < _rows.Count; i++)
            {
                SliderRow row = _rows[i];
                float top = offset + i * RowH;
                row.Caption.Bounds = new Rectangle(0f, top, labelW, RowH);
                // 滑杆占满整行高度（早先只给 18px）：Flax 的命中判定按 Bounds 来，
                // 行与行之间留 24px 死区的话，玩家拖在"看上去是轨道"的位置上什么也不会发生。
                row.Slider.Bounds = new Rectangle(labelW + 8f, top, sliderW, RowH);
                row.Value.Bounds = new Rectangle(labelW + 8f + sliderW + 8f, top, valueW, RowH);
            }
        }

        private void RelayoutSteps()
        {
            float w = _stepScroll.Width;
            for (int i = 0; i < _stepButtons.Count; i++)
            {
                _stepButtons[i].Bounds = new Rectangle(0f, i * (StepRowH + 2f), w, StepRowH);
            }
        }

        // ===================================================================
        // 滑块列表构建
        // =======================================================================

        private void RebuildTab()
        {
            ClearRows();

            if (_character == null || !_character.IsLoaded)
            {
                return;
            }

            List<MetaHumanParameter> source = _showBody ? _bodyParams : _headParams;
            int count = Mathf.Min(source.Count, MaxRowsPerTab);

            for (int i = 0; i < count; i++)
            {
                CreateRow(source[i]);
            }

            if (source.Count > MaxRowsPerTab)
            {
                SetStatus(string.Format("该页 {0} 项，仅显示前 {1} 项。", source.Count, MaxRowsPerTab), true);
            }

            _headTab.BackgroundColor = _showBody ? AccentDim : Accent;
            _bodyTab.BackgroundColor = _showBody ? Accent : AccentDim;

            // 色板选中项要跟着会话现值走（撤销/重做/预设之后不能停在旧下标上）。
            if (_eyeSwatch != null)
            {
                _eyeSwatch.Visible = !_showBody;
                int last = _eyeSwatch.Options.Length - 1;
                if (last > 0)
                {
                    float iris = _character.Session != null ? _character.Session.State.Get("eye.iris_color") : 0.5f;
                    int index = Mathf.Clamp(Mathf.RoundToInt(iris * last), 0, last);
                    _syncing = true;
                    try
                    {
                        _eyeSwatch.Selected = index;
                        if (_hairSwatch != null)
                        {
                            int hairLast = _hairSwatch.Options.Length - 1;
                            float hair = _character.Session != null ? _character.Session.State.Get("hair.color") : 0.3f;
                            _hairSwatch.Selected = Mathf.Clamp(Mathf.RoundToInt(hair * hairLast), 0, hairLast);
                        }

                        if (_skinSwatch != null)
                        {
                            float melanin = _character.Session != null ? _character.Session.State.Get("skin.melanin") : 0.25f;
                            _skinSwatch.Selected = CcFacePresets.ClosestSkinIndex(melanin);
                        }

                        if (_hairStyleButtons != null)
                        {
                            int styleIndex = _character.Session != null
                                ? Mathf.Clamp(Mathf.RoundToInt(_character.Session.State.Get("hair.style")), 0, _hairStyleButtons.Length - 1)
                                : 0;
                            for (int i = 0; i < _hairStyleButtons.Length; i++)
                            {
                                _hairStyleButtons[i].BackgroundColor = i == styleIndex ? Accent : AccentDim;
                            }
                        }
                    }
                    finally
                    {
                        _syncing = false;
                    }
                }
            }

            // 刚建出来的行还带着 Slider/Label 的默认位置（全都堆在 (0,0)），而换页签之后
            // 不一定会再来一次 RefreshLayout（它只在窗口尺寸变化时跑）——
            // 2026-10-18 实测"切到身体页签后整片 UI 叠在左上角"就是漏了这一步。
            RelayoutRows();
        }

        private void ClearRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                _rows[i].Caption.Dispose();
                _rows[i].Slider.Dispose();
                _rows[i].Value.Dispose();
            }

            _rows.Clear();
        }

        /// <summary>
        /// 归一化档位的显示倍率。头部参数是 ±1（配 0.06cm 的骨骼增益），照原样显示成 0.0232 没法读 ⇒ ×100；
        /// 但身体参数本来就是物理量（<c>body.forearm</c> 量程 [14,50] 是厘米），再乘 100 就编出
        /// "3948" 那种假数（2026-10-18 实测）。所以倍率按**这一项自己的量程**决定，不用全表面板常量。
        /// 只改显示：参数 Id、滑杆实值、写回值仍是原始量纲。
        /// </summary>
        private const float NormalizedScale = 100f;
        private const float NormalizedRangeSpan = 4f;

        private static float DisplayScaleOf(MetaHumanParameter param)
        {
            if (param == null)
            {
                return 1f;
            }

            return param.Max - param.Min <= NormalizedRangeSpan ? NormalizedScale : 1f;
        }

        private static string DisplayText(float raw, float scale)
        {
            return (raw * scale).ToString("0.##");
        }

        private void CreateRow(MetaHumanParameter param)
        {
            float scale = DisplayScaleOf(param);

            Label caption = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                TextColor = TextColor,
                Text = string.Format("{0}  [{1}~{2}]", param.DisplayName,
                    DisplayText(param.Min, scale), DisplayText(param.Max, scale)),
                Parent = _sliderScroll,
            };

            // 会话已封存（本机发布过、或服务端回执告知已发布）时滑杆根本不该能拖：
            // 拖得动就等于允许造出"显示值与模型实值不一致"的那种读数。
            bool locked = _character.IsSealed;

            SculptSlider slider = new SculptSlider
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Minimum = param.Min,
                Maximum = param.Max,
                Value = GetValue(param.Id),
                Enabled = !locked,
                ProbeTag = param.Id,
                Parent = _sliderScroll,
            };

            Label value = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                TextColor = Accent,
                Text = DisplayText(slider.Value, scale),
                Parent = _sliderScroll,
            };

            SliderRow row = new SliderRow { Caption = caption, Slider = slider, Value = value, Param = param, Scale = scale };
            _rows.Add(row);

            slider.ValueChanged += () => OnSliderLive(row);
            // 松手定步：一次拖动只落一步（历史会按标签把连续提交并成一步）
            slider.SlidingEnd += () => OnSliderCommit(row);
            // 基类没接管那次按下时（thumb 纵向只有 16px，命中判定看 x 也看 y）由本类收尾，两条互斥不会重复记步
            slider.DragEnded += () => OnSliderCommit(row);
        }

        /// <summary>
        /// "没有角色"有两种，必须分开：① 压根没绑（托管引用为 null）；② 绑过，但承载它的 Actor 已被销毁。
        /// <para>
        /// Flax 在 <see cref="FlaxEngine.Object"/> 上重载了 <c>==</c>，比的是**原生指针**
        /// （Source/Engine/Scripting/Object.cs:232），所以旧写法 <c>_character == null</c> 把两种压成同一个读数。
        /// 2026-10-18 的"拖了没反应"正是 ② 被当成 ①：滑杆值照改、`Adjust` 一次都不调、界面一句不响，
        /// 而托管的会话状态（IsSealed/HistoryLabels）照样能读 ⇒ 面板状态自检全绿。
        /// </para>
        /// </summary>
        private bool CharacterUnbound
        {
            get { return ReferenceEquals(_character, null); }
        }

        private bool CharacterDetached
        {
            get
            {
                if (CharacterUnbound)
                {
                    return false;
                }

                FlaxEngine.Object native = _character;
                return !native; // 隐式 bool：原生指针为 0 即判"没了"
            }
        }

        private int _detachedReported;

        /// <summary>组件已随承载 Actor 销毁：要说出来，不要再静默退回（用户看到的"占位符"就是这么来的）。</summary>
        private void ReportDetached(SliderRow row)
        {
            if (_detachedReported < 3)
            {
                _detachedReported++;
                Probe(string.Format("[SculptDetached] {0} #{1} panel=#{2} sliderValue={3:0.####}",
                    row.Param.Id, _detachedReported, _serial, row.Slider.Value));
            }

            SetStatus("捏脸组件已随预览角色一起销毁，本次改动不会写进模型（需要重新绑定承载 Actor）。", true);
        }

        /// <summary>
        /// 瞳色色板 → 模板参数 eye.iris_color（0..1 指数）。走与滑杆同一条 Adjust/Commit 链，
        /// 因此封存闸门、历史记步、写回端都自动一致；同时把离散下标写进 CcSession 供外观落库。
        /// </summary>
        private void OnEyeSwatchChanged(int index)
        {
            if (_syncing || CharacterUnbound || CharacterDetached || _character == null || _character.Session == null)
            {
                return;
            }

            if (EnsureWriteTarget != null && !_character.HasLiveWriteTarget)
            {
                EnsureWriteTarget();
            }

            int last = Mathf.Max(1, _eyeSwatch.Options.Length - 1);
            float value = Mathf.Clamp(index, 0, last) / (float)last;
            _character.Session.Adjust("eye.iris_color", value);
            bool recorded = _character.Session.Commit("瞳色");

            CcSession.Current.EyeColorIndex = index;
            RefreshSteps();
            SetStatus(recorded ? BuildReadyLine() : "已发布，本次改动不会生效，也不会生成新步骤。", !recorded);
        }

        /// <summary>发色色板 → 模板参数 hair.color（0..1 指数）；与瞳色同一条 Adjust/Commit 链。</summary>
        private void OnHairSwatchChanged(int index)
        {
            if (_syncing || CharacterUnbound || CharacterDetached || _character == null || _character.Session == null)
            {
                return;
            }

            if (EnsureWriteTarget != null && !_character.HasLiveWriteTarget)
            {
                EnsureWriteTarget();
            }

            // 头发是独立 StaticModel，不在捏脸写回端的实例列表里：这里落外观下标，
            // 预览侧由宿主实时换色（与世界侧 MH_Hair 的 HairColor 同一调色板），发布后仍走 AppearanceInfo.HairColor。
            CcSession.Current.HairColorIndex = Mathf.Clamp(index, 0, _hairSwatch.Options.Length - 1);
            HairColorChanged?.Invoke(CcSession.Current.HairColorIndex);
            SetStatus("发色已选择。", false);
        }

        /// <summary>发型按钮 → 模板参数 hair.style（离散下标）；发布后由预览/世界/远端按值换模。</summary>
        private void OnHairStyleClicked(int index)
        {
            if (_syncing || CharacterUnbound || CharacterDetached || _character == null || _character.Session == null)
            {
                return;
            }

            if (EnsureWriteTarget != null && !_character.HasLiveWriteTarget)
            {
                EnsureWriteTarget();
            }

            _character.Session.Adjust("hair.style", index);
            bool recorded = _character.Session.Commit("发型");
            // 外观副本里也留一份下标（远端回执的 HairColor 走同一路子）；渲染仍以 look 里的值为准。
            CcSession.Current.HairStyleIndex = Mathf.Clamp(index, 0, HundunWorld.Game.Character.HairStyleCatalog.Count - 1);
            RefreshSteps();
            SetStatus(recorded ? BuildReadyLine() : "已发布，本次改动不会生效，也不会生成新步骤。", !recorded);
        }

        /// <summary>肤色色板 → 模板参数 skin.melanin（0..1 指数）；与瞳色同一条 Adjust/Commit 链。</summary>
        private void OnSkinSwatchChanged(int index)
        {
            if (_syncing || CharacterUnbound || CharacterDetached || _character == null || _character.Session == null)
            {
                return;
            }

            if (EnsureWriteTarget != null && !_character.HasLiveWriteTarget)
            {
                EnsureWriteTarget();
            }

            // 肤色板不是按明度排序的：melanin 由色块自身亮度反推，不能拿下标线性换算。
            float value = CcFacePresets.MelaninOf(index);
            _character.Session.Adjust("skin.melanin", value);
            bool recorded = _character.Session.Commit("肤色");

            CcSession.Current.SkinColorIndex = index;
            RefreshSteps();
            SetStatus(recorded ? BuildReadyLine() : "已发布，本次改动不会生效，也不会生成新步骤。", !recorded);
        }

        /// <summary>
        /// 把模板参数现值回填到外观档下标。滑杆、色板、预设、撤销/重做最终都会经过 RefreshSteps；
        /// 少了这一步，拖滑杆改了瞳色/肤色后 `AppearanceInfo` 里仍是旧下标（建角后颜色对不上）。
        /// </summary>
        private void SyncColorIndices()
        {
            if (_character == null || _character.Session == null)
            {
                return;
            }

            var state = _character.Session.State;
            CcSession.Current.EyeColorIndex = QuantizeIndex(state.Get("eye.iris_color"), CcFacePresets.EyeColors.Length);
            CcSession.Current.SkinColorIndex = CcFacePresets.ClosestSkinIndex(state.Get("skin.melanin"));
            CcSession.Current.HairColorIndex = QuantizeIndex(state.Get("hair.color"), CcFacePresets.HairColors.Length);
            CcSession.Current.HairStyleIndex = Mathf.Clamp(
                Mathf.RoundToInt(state.Get("hair.style")), 0, HundunWorld.Game.Character.HairStyleCatalog.Count - 1);
        }

        private static int QuantizeIndex(float t, int count)
        {
            int last = Mathf.Max(1, count - 1);
            return Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp(t, 0f, 1f) * last), 0, last);
        }

        private void OnSliderLive(SliderRow row)
        {
            // 入口回执（闸门之前）：2026-10-18 实测按下已经改了滑杆值（[SculptDown] 里 value=0.0395 这种
            // 由点击位置算出的数），但 [SculptDrag] 一条不响 ⇒ 回调要么没进这个方法，要么被下面这道
            // 全局闸门挡了。这两种情形在日志上长得一模一样，只能在闸门前先分一次。
            LogLiveOnce(row);

            if (_syncing || CharacterUnbound)
            {
                return;
            }

            if (CharacterDetached)
            {
                ReportDetached(row);
                return;
            }

            // 写之前先确认目标还活着（宿主会把写回目标重指到当前渲染的那颗头）。
            if (EnsureWriteTarget != null && !_character.HasLiveWriteTarget)
            {
                EnsureWriteTarget();
            }

            // Adjust 的返回值是"模型上真正生效的那个数"：会话已封存时闸门原样退回冻结值（不等于拖到的值）。
            // 显示必须跟着返回值而不是滑杆自己：否则玩家拖到 0.9、脸上还是 0，界面就成了骗人的读数。
            _adjustWatch.Restart();
            float applied = _character.Adjust(row.Param.Id, row.Slider.Value);
            _adjustWatch.Stop();
            _lastAdjustMs = _adjustWatch.Elapsed.TotalMilliseconds;
            LogDragOnce(row, applied);
            if (applied != row.Slider.Value)
            {
                // 程序化回填要压掉回调，但必须自复原：这个开关是全面板共享的，
                // 一旦异常把它留在 true，120 根滑杆的写回会一起静默关掉（本次症状的候选解释）。
                _syncing = true;
                try
                {
                    row.Slider.Value = applied;
                }
                finally
                {
                    _syncing = false;
                }
            }

            row.Value.Text = DisplayText(applied, row.Scale);
        }

        private int _liveLogged;

        /// <summary>本进程里第几个被构造出来的捏脸面板（0 起自增，见构造函数）。</summary>
        private static int _instancesMade;

        /// <summary>面板实例编号：用来分清"两条回执是不是同一个面板打的"。</summary>
        private readonly int _serial = ++_instancesMade;

        /// <summary>本实例被 Bind 过几次、最后一次拿到的是不是 null。</summary>
        private int _bindCount;

        private bool _lastBindArgNull;

        /// <summary>回调入口自证（最多 8 条）：见 <see cref="OnSliderLive"/> 顶部的说明。</summary>
        private void LogLiveOnce(SliderRow row)
        {
            if (_liveLogged >= 8)
            {
                return;
            }

            _liveLogged++;
            Probe(string.Format(
                "[SculptLive] {0} #{1} syncing={2} character={3} sliderValue={4:0.####} sliderRange=[{5:0.####},{6:0.####}] "
                + "panel=#{7} binds={8} lastBindArgNull={9} rows={10} charHash={11}",
                row.Param.Id, _liveLogged, _syncing, CharacterState, row.Slider.Value,
                row.Slider.Minimum, row.Slider.Maximum, _serial, _bindCount, _lastBindArgNull, _rows.Count,
                ReferenceEquals(_character, null) ? 0 : _character.GetHashCode()));
        }

        private int _dragLogged;

        /// <summary>量"一次拖动事件里 Adjust 总共花了多久"（含纯数据核 Rebuild + 写回 ApplyNow）。</summary>
        private readonly System.Diagnostics.Stopwatch _adjustWatch = new System.Diagnostics.Stopwatch();

        private double _lastAdjustMs;
        private int _commitLogged;

        /// <summary>面板状态自证是否已打（见 <see cref="RefreshSteps"/>）。</summary>
        private bool _stateLogged;

        /// <summary>
        /// 面板容器级点击自证（最多 6 条）：滑杆那层的回执一条不响，就得先分清
        /// "点击没进这块 UI"（容器也不响）与"进了容器但没落到滑杆"（容器响、行号命中与否可判）。
        /// </summary>
        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (_clickProbed < 6)
            {
                _clickProbed++;
                Float2 winPt = PointToWindow(location);
                int hit = -1;
                Float2 row0Win = Float2.Zero;
                Float2 row0Size = Float2.Zero;
                for (int i = 0; i < _rows.Count; i++)
                {
                    Slider s = _rows[i].Slider;
                    Float2 origin = s.PointToWindow(Float2.Zero);
                    Float2 size = new Float2(s.Width, s.Height);
                    if (i == 0)
                    {
                        row0Win = origin;
                        row0Size = size;
                    }

                    if (winPt.X >= origin.X && winPt.X <= origin.X + size.X
                        && winPt.Y >= origin.Y && winPt.Y <= origin.Y + size.Y)
                    {
                        hit = i;
                        break;
                    }
                }

                // 全 ASCII：日志里中文会把紧跟其后的数字一起吞掉（实测 `[SculptClick] #1 …local=(x,y)`
                // 的坐标在文件里读不出来），所以这条自证的标签一律用英文。
                Probe(string.Format(
                    "[SculptClick] #{0} panelDown=({1:F0},{2:F0}) win=({3:F0},{4:F0}) hitRow={5} rows={6} "
                    + "row0Win=({7:F0},{8:F0}) row0Size=({9:F0},{10:F0}) character={11} serial=#{12}",
                    _clickProbed, location.X, location.Y, winPt.X, winPt.Y, hit, _rows.Count,
                    row0Win.X, row0Win.Y, row0Size.X, row0Size.Y, CharacterState, _serial));
            }

            return base.OnMouseDown(location, button);
        }

        /// <summary>
        /// 源码版本戳：只出现在回执里。它的唯一用途是判"现场跑的是哪一版" ——
        /// 2026-10-19 连续两轮拿旧装配取证（回执里还带着源码已删除的 compSkip=），
        /// 分不出"改动无效"和"改动没编进来"。改这一支面板就加一位。
        /// </summary>
        private const string BuildStamp = "2026-10-19d";

        private int _clickProbed;

        /// <summary>
        /// 诊断回执通道：既进引擎日志，也追一份到 <c>%APPDATA%/HundunWorld/sculpt-probe.txt</c>。
        /// <para>
        /// 为什么要第二份：2026-10-18 这一轮引擎磁盘日志写到 2.03MB 触顶后打出
        /// "Trimming log file." 就再没长过（PIE 每 2 秒一条的 [PreviewDiag] 连续 8 秒没让文件变大一个字节），
        /// 于是 <c>Debug.Log</c> 里的回执我完全读不到，只能从运行时 UI 树反推。
        /// </para>
        /// </summary>
        private static void Probe(string line)
        {
            FlaxEngine.Debug.Log(line);
            try
            {
                string dir = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "HundunWorld");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "sculpt-probe.txt"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + Environment.NewLine);
            }
            catch
            {
                // 诊断通道自身不能影响面板：写不进去就算了，日志里还有同一条。
            }
        }

        /// <summary>
        /// 拖动链路自证，最多打 6 条：把"滑杆值 → Adjust 返回值"这一对并排报出来。
        /// 为什么需要：用户实测"拖了没反应、数字一直是 0"。这条链上有三处都能把值吃掉
        /// （参数 Id 在会话里查不到 → 返回 0；量程 Min/Max 与模板不一致 → 被钳回 0；
        /// 封存闸门 → 原样退回冻结值），只看得见最后那个"0"就分不出是哪一处。
        /// </summary>
        private void LogDragOnce(SliderRow row, float applied)
        {
            if (_dragLogged >= 12)
            {
                return;
            }

            _dragLogged++;
            MetaHumanParameter probe = _character.FindParameter(row.Param.Id);
            Probe(string.Format(
                "[SculptDrag] {0} #{1} sliderValue={2:0.####} returned={3:0.####} foundInTemplate={4} "
                + "range=[{5:0.###},{6:0.###}] sealed={7} adjustMs={8:0.0} applyMs={9:0.0} "
                + "bones={10} poseWrites={11} morphSkips={12} replaySkipsTotal={13} hookPoseWritesTotal={14} appliers={15}",
                row.Param.Id, _dragLogged, row.Slider.Value, applied, probe != null,
                probe != null ? probe.Min : row.Param.Min, probe != null ? probe.Max : row.Param.Max,
                _character.IsSealed, _lastAdjustMs, _character.LastApplyMilliseconds,
                _character.LastApplyBoneWrites, _character.LastApplyPoseWrites, _character.LastApplyMorphSkips,
                _character.TotalBoneReplaySkips, _character.TotalHookPoseWrites,
                _character.AppliersDiag()));
        }

        /// <summary>把"没绑 / 绑了但承载 Actor 已销毁 / 正常"三种状态压成一个词，供回执直接读。</summary>
        private string CharacterState
        {
            get
            {
                if (CharacterUnbound)
                {
                    return "unbound";
                }

                return CharacterDetached ? "detached" : "ok";
            }
        }

        private void OnSliderCommit(SliderRow row)
        {
            // 松手半条链的入口回执：[SculptLive]/[SculptDrag] 只覆盖"按下改值"，
            // 记步走的是这里，两道闸门（_syncing / 封存）任一误判都表现为"拖了但步骤数不涨"。
            if (_commitLogged < 6)
            {
                _commitLogged++;
                Probe(string.Format("[SculptCommit] {0} #{1} syncing={2} character={3} sealed={4} panel=#{5} "
                    + "replaySkipsTotal={6}",
                    row.Param.Id, _commitLogged, _syncing, CharacterState,
                    CharacterUnbound || CharacterDetached ? "n/a" : (_character.IsSealed ? "sealed" : "open"), _serial,
                    CharacterUnbound || CharacterDetached ? -1 : _character.TotalBoneReplaySkips));
            }

            if (_syncing || CharacterUnbound)
            {
                return;
            }

            if (CharacterDetached)
            {
                ReportDetached(row);
                return;
            }

            bool recorded = _character.Commit(row.Param.DisplayName);
            RefreshSteps();

            if (!recorded)
            {
                // 已封存：Adjust 那一步就被闸门退回原值（模型没变），Commit 也只是确认"不再记步"。
                // 文案要说"没生效"而不是"不会生成新步骤"——后者会让人以为改动已经写进脸上了。
                SetStatus("已发布，本次改动不会生效，也不会生成新步骤。", true);
            }
            else
            {
                SetStatus(BuildReadyLine(), false);
            }
        }

        // ===================================================================
        // 步骤条 / 撤销 / 重做
        // =======================================================================

        private void RefreshSteps()
        {
            for (int i = 0; i < _stepButtons.Count; i++)
            {
                _stepButtons[i].Dispose();
            }

            _stepButtons.Clear();
            if (_character == null)
            {
                return;
            }

            List<string> labels = _character.HistoryLabels();
            int cursor = _character.HistoryCursor;
            SyncColorIndices();
            // 打开面板/换角色时把当前发色刷到预览（否则要等玩家先点一次发色才看得到头发颜色）。
            HairColorChanged?.Invoke(CcSession.Current.HairColorIndex);

            // 一次性面板状态自证（**不需要点击就会打**）：
            // "滑杆拖不动"有两种完全不同的成因 —— ① 会话已封存 ⇒ 建行时 `Enabled = !IsSealed` 把滑杆整个禁掉；
            // ② 点击压根到不了控件。这两种在画面上长得一模一样，只能把状态读出来分。
            if (!_stateLogged)
            {
                _stateLogged = true;
                bool firstEnabled = _rows.Count > 0 && _rows[0].Slider.Enabled;
                Probe(string.Format(
                        "[SculptState] sealed={0} canUndo={1} steps={2} cursor={3} rows={4} row0SliderEnabled={5} "
                        + "row0Range=[{6:0.###},{7:0.###}] row0Value={8:0.###} role={9} panel=#{10}",
                        _character.IsSealed, _character.CanUndo, labels.Count, cursor, _rows.Count, firstEnabled,
                        _rows.Count > 0 ? _rows[0].Slider.Minimum : 0f,
                        _rows.Count > 0 ? _rows[0].Slider.Maximum : 0f,
                        _rows.Count > 0 ? _rows[0].Slider.Value : 0f,
                        _character.CharacterRole, _serial));
            }

            for (int i = 0; i < labels.Count; i++)
            {
                int index = i;
                Button step = new Button
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Text = string.Format("{0}. {1}", i, labels[i]),
                    TextColor = TextColor,
                    BackgroundColor = i == cursor ? Accent : AccentDim,
                    Parent = _stepScroll,
                };
                step.Clicked += () => OnJumpToStep(index);
                _stepButtons.Add(step);
            }

            // 发布后回溯通道关闭：整块步骤条与撤销/重做/跳步一律禁用。
            bool sealedNow = _character.IsSealed;
            _undoBtn.Enabled = _character.CanUndo && !sealedNow;
            _redoBtn.Enabled = _character.CanRedo && !sealedNow;
            for (int i = 0; i < _stepButtons.Count; i++)
            {
                _stepButtons[i].Enabled = !sealedNow;
            }

            // 服务端回执把本机会话定格时，滑杆是在之前建好的 ⇒ 这里必须一起关掉，
            // 不能只靠建条时那一次判据（否则"发布"落地后玩家还能一路拖出与实际不符的读数）。
            for (int i = 0; i < _rows.Count; i++)
            {
                _rows[i].Slider.Enabled = !sealedNow;
            }

            RelayoutSteps();
        }

        private void OnJumpToStep(int index)
        {
            if (_character == null || _character.IsSealed)
            {
                return;
            }

            CancelPublishConfirm();
            if (_character.JumpToStep(index))
            {
                SyncSliderValues();
                RefreshSteps();
                SetStatus(string.Format("已回溯到第 {0} 步。", index), false);
            }
        }

        private void OnUndo()
        {
            if (_character == null || !_character.CanUndo || _character.IsSealed)
            {
                return;
            }

            CancelPublishConfirm();
            _character.Undo();
            SyncSliderValues();
            RefreshSteps();
        }

        private void OnRedo()
        {
            if (_character == null || !_character.CanRedo || _character.IsSealed)
            {
                return;
            }

            CancelPublishConfirm();
            _character.Redo();
            SyncSliderValues();
            RefreshSteps();
        }

        /// <summary>撤销 / 跳步 / 随机后，把整表滑块值回填成当前会话状态。</summary>
        private void SyncSliderValues()
        {
            _syncing = true;
            try
            {
                for (int i = 0; i < _rows.Count; i++)
                {
                    SliderRow row = _rows[i];
                    float value = GetValue(row.Param.Id);
                    row.Slider.Value = value;
                    row.Value.Text = DisplayText(value, row.Scale);
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        // ===================================================================
        // 操作按钮
        // =======================================================================

        private void OnLinkToggled()
        {
            if (_character == null || _character.Session == null || _syncing)
            {
                return;
            }

            _character.Session.SetLinkSymmetric(_linkCheck.Checked, "左右联动");
        }

        private void OnRandomize()
        {
            if (_character == null || !_character.IsLoaded)
            {
                return;
            }

            CancelPublishConfirm();
            int seed = _randomSeed++;
            _character.Randomize(seed, "随机 " + seed);
            SyncSliderValues();
            RefreshSteps();
            SetStatus(BuildReadyLine(), false);
        }

        private void OnReset()
        {
            if (_character == null || !_character.IsLoaded || _character.Session == null)
            {
                return;
            }

            CancelPublishConfirm();

            // 一次调用做完：全部参数回默认 + 重叠加本次选中的面容预设 + 只重算一次 + 记一步。
            // 早先这里是"逐条 Adjust 598 个参数再 Commit"，而每次 Adjust 都会 Rebuild（连带一次
            // 全量骨骼求值）⇒ 按一次「重置」主线程冻好几秒；而且只归零不回预设，等于把玩家
            // 从预设页带进来的那张脸弄丢了（2026-10-19 两条实测缺陷）。
            if (!_character.Session.ResetToBaseline("重置"))
            {
                SetStatus("已发布，重置不生成新步骤。", true);
            }

            SyncSliderValues();
            RefreshSteps();
            SetStatus(BuildReadyLine(), false);
        }

        private void OnApplyPreset()
        {
            if (_character == null || _presetIds.Count == 0)
            {
                return;
            }

            CancelPublishConfirm();
            // 目前模板预设极少，逐个循环应用；每次记一步，可回溯。
            int pick = _presetCursor % _presetIds.Count;
            _presetCursor++;
            string presetId = _presetIds[pick];
            if (_character.ApplyPreset(presetId, "预设 " + presetId))
            {
                SyncSliderValues();
                RefreshSteps();
                SetStatus("已应用预设 " + presetId + "。", false);
            }
        }

        private int _presetCursor;

        private void OnSave()
        {
            if (_character == null || !_character.IsLoaded)
            {
                return;
            }

            CancelPublishConfirm();
            MetaHumanLook look = _character.Save();
            string json = _character.ExportLookJson();
            if (look == null || json == null)
            {
                SetStatus("保存失败。", true);
                return;
            }

            SetStatus(string.Format("已保存 {0} 字节，校验和 {1:X8}。", json.Length, look.Checksum), false);
        }

        private void OnPublish()
        {
            if (_character == null || !_character.IsLoaded)
            {
                return;
            }

            // 二次确认：首点只把按钮改成「确认发布?」，再点才真正封存，避免误触。
            if (!_confirmPublish)
            {
                _confirmPublish = true;
                _publishBtn.Text = "确认发布?";
                SetStatus("发布后回溯通道永久关闭，再点一次确认。", true);
                return;
            }

            _confirmPublish = false;
            _publishBtn.Text = "发布";

            var publishedLook = _character.Publish();
            RecordPendingLookMeta(publishedLook);
            RefreshSteps();
            // 文案要与闸门一致：会话封存后 Adjust 就被挡回原值，"还能继续预览改动"那句是老口径，
            // 留在界面上等于告诉玩家改动会生效、而他看到的一直不变。
            SetStatus("已发布：回溯通道与再编辑均已关闭，滑杆不再改变模型。", true);
        }

        /// <summary>
        /// 把当前捏脸快照交给创建流程，但**不封存**：给"下一步→命名→建号"这条常规路径用。
        /// 没有它时，未点过「发布」的捏脸值在建号时会被丢弃（世界角色回默认脸，2026-10-22 实测）。
        /// </summary>
        public void CommitPendingLook()
        {
            if (_character == null || !_character.IsLoaded)
            {
                return;
            }

            MetaHumanLook look = _character.Save();
            if (look == null)
            {
                return;
            }

            MetaHuman.Runtime.MetaHumanLookStore.SetPendingLook(look);
            RecordPendingLookMeta(look);
        }

        /// <summary>
        /// 把这次发布所依据的模板与差量条目数记下来。
        /// 服务端要按槽位数与模板 Id 判定旧存档能不能沿用（错位解释最阴），而这些信息只有此刻齐全。
        /// </summary>
        private void RecordPendingLookMeta(MetaHuman.MetaHumanLook look)
        {
            if (look == null || _character == null || _character.Session == null)
            {
                return;
            }

            MetaHuman.MetaHumanTemplate template = _character.Session.Template;
            if (template == null)
            {
                return;
            }

            int entries = 0;
            if (_character.Session.State != null)
            {
                foreach (var pair in _character.Session.State.ModifiedValues())
                {
                    entries++;
                }
            }

            MetaHuman.Runtime.MetaHumanLookStore.SetPendingLookMeta(
                new MetaHuman.Runtime.MetaHumanLookStore.PendingLookMeta
                {
                    TemplateId = template.TemplateId,
                    TemplateVersion = template.Version.Major + "." + template.Version.Minor + "." + template.Version.Revision,
                    TemplateParamCount = template.Parameters.Count,
                    EntryCount = entries,
                });
        }

        private void CancelPublishConfirm()
        {
            if (_confirmPublish)
            {
                _confirmPublish = false;
                _publishBtn.Text = "发布";
            }
        }

        // ===================================================================
        // 取值 / 状态
        // =======================================================================

        private float GetValue(string paramId)
        {
            if (_character != null && _character.Session != null)
            {
                return _character.Session.State.Get(paramId);
            }

            MetaHumanParameter p = _character != null ? _character.FindParameter(paramId) : null;
            return p != null ? p.Default : 0f;
        }

        private void SetStatus(string text, bool warn)
        {
            _status.Text = text;
            _status.TextColor = warn ? WarnColor : TextColor;
        }

        /// <summary>
        /// 捏脸滑杆：把"按下"改成 UE 的口径——轨道上任意一点按下即取该点定值，随后连续拖动更新。
        /// <para>
        /// 为什么要自己写（两条都是 2026-10-18 在 PIE 里量出来的，不是猜的）：
        /// </para>
        /// <list type="bullet">
        /// <item><description>Flax 的 <see cref="Slider"/> 只在按下命中 thumb 时才进入拖动，其余落点走
        /// <c>Value ± 10</c> 的步进（本面板量程 ±1 ⇒ 一指撞满）；而命中 thumb 但手指不动时一个
        /// <c>ValueChanged</c> 都不发。量程居中 ⇒ thumb 就在轨道正中，玩家"点中间"必然命中它。</description></item>
        /// <item><description>命中判定是 <c>_thumbRect.Contains(整两点)</c>，thumb 纵向只有 16px，
        /// 在 42px 的行高里占 y∈[13,29]。实测 12 次按下有 3 次因为 y=29/7/5 落在带外而退回 ±10 步进
        /// （回执里表现为 <c>value=1 handled=False</c>）。所以 x 搬到指针下还不够，拖动得自己接管。</description></item>
        /// </list>
        /// </summary>
        private sealed class SculptSlider : Slider
        {
            /// <summary>仅用于自证日志的参数 Id。</summary>
            public string ProbeTag;

            /// <summary>基类没接管这次按下时，由本类自己收尾的"松手定步"回调。</summary>
            public Action DragEnded;

            private bool _dragging;
            private int _downLogged;

            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button != MouseButton.Left)
                {
                    return base.OnMouseDown(location, button);
                }

                ApplyFromX(location.X);

                // 只在"这次按下确实会命中 thumb"时才交给基类。基类的另一条分支是
                // `Value += ±10`（轨道点击步进），对本面板 ±1 的量程等于直接撞满 ——
                // 它会在我们刚按位置定好值之后把值改掉（实测 [SculptDown] value=1 handled=False）。
                // thumb 纵向只有 ThumbSize.Y 高，所以按 y 是否在带内判断即可（x 已由 ApplyFromX 对齐）。
                bool onThumbBand = Mathf.Abs(location.Y - Height * 0.5f) <= ThumbSize.Y * 0.5f;
                bool handled = onThumbBand && base.OnMouseDown(location, button);
                if (!handled)
                {
                    _dragging = true;
                    StartMouseCapture();
                }

                // 全 ASCII：中文标签会把紧跟其后的数字一起吞掉（见 [SculptClick] 的同一处教训）。
                // 每根滑杆最多 2 条：120 根各放 6 条会在日志里把自己淹掉，而这轮只要问"按下到没到滑杆"。
                if (_downLogged < 2)
                {
                    _downLogged++;
                    Probe(string.Format(
                        "[SculptDown] {0} #{1} local=({2:F0},{3:F0}) size=({4:F0},{5:F0}) thumbCenter={6:F0} "
                        + "value={7:0.####} range=[{8:0.####},{9:0.####}] handled={10}",
                        ProbeTag, _downLogged, location.X, location.Y, Width, Height, ThumbCenter.X, Value,
                        Minimum, Maximum, handled));
                }

                // 轨道任意处按下都是对本滑杆的一次赋值，不该再往上冒给容器（否则同一次点击可能被兄弟控件二次消费）。
                return true;
            }

            public override void OnMouseMove(Float2 location)
            {
                if (!_dragging)
                {
                    base.OnMouseMove(location);
                    return;
                }

                Float2 offset = Root != null ? Root.TrackingMouseOffset : Float2.Zero;
                ApplyFromX(location.X + offset.X);
            }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (_dragging && button == MouseButton.Left)
                {
                    _dragging = false;
                    EndMouseCapture();
                    if (DragEnded != null)
                    {
                        DragEnded();
                    }

                    return true;
                }

                return base.OnMouseUp(location, button);
            }

            public override void OnEndMouseCapture()
            {
                _dragging = false;
                base.OnEndMouseCapture();
            }

            /// <summary>
            /// 按轨道上的横向位置定值。映射与基类的 <c>UpdateThumb</c> 写成同一个式子
            /// （thumb 左沿 = perc × (Width − ThumbSize.X)），所以赋值后 thumb 正好落在指针的 x 下。
            /// </summary>
            private void ApplyFromX(float x)
            {
                float half = ThumbSize.X * 0.5f;
                float span = Mathf.Max(1f, Width - ThumbSize.X);
                float perc = Mathf.Clamp((x - half) / span, 0f, 1f);
                Value = Minimum + (Maximum - Minimum) * perc;
            }
        }

        private sealed class SliderRow
        {
            public MetaHumanParameter Param;
            public Label Caption;
            public Slider Slider;
            public Label Value;

            /// <summary>这一项的显示倍率（见 <see cref="DisplayScaleOf"/>）；只影响读数，不影响写回值。</summary>
            public float Scale;
        }
    }
}
