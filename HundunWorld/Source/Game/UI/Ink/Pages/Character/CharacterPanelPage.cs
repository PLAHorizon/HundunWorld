using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.Inventory;
using HundunWorld.Game.Services;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;
using ClientSlot = HundunWorld.Game.Equipment.EquipmentSlot;

namespace HundunWorld.Game.UI.Ink.Pages.Character
{
    /// <summary>
    /// 角色面板页面 — 对应设计方案 character-panel.html。
    /// 顶部标题栏 + 左侧 3D 模型展示区（13 个浮动装备槽）+ 右侧可滚动属性面板（8 个卡片）。
    /// 严格遵循水墨主题 Token，禁止硬编码色值。
    /// </summary>
    public class CharacterPanelPage : ContainerControl, IInkPage
    {
        // ===== 布局常量（对应设计方案像素值，1920x1080 参考） =====
        private const float TopBarHeight = 52f;
        private const float ContentPad = 12f;      // p-3
        private const float ColumnGap = 12f;       // gap-3
        private const float LeftRatio = 0.48f;
        private const float SlotSize = 40f;
        private const float CloseBtnSize = 32f;
        private const float CardGap = 12f;
        private const float CardPad = 14f;
        private const float CardPadSm = 12f;

        public event Action<string> NavigationRequested;

        private InkParticleSystem _particleSystem;
        public InkParticleSystem ParticleSystem
        {
            get => _particleSystem;
            set => _particleSystem = value;
        }

        private CharacterAttributesComponent _boundCharacter;

        // 顶栏
        private ContainerControl _topBar;
        private Label _nameLabel;
        private Label _levelLabel;
        private InkButton _closeButton;
        private ContainerControl _topDivider;

        // 左列
        private ContainerControl _leftPanel;
        private ModelStage _modelStage;
        private Label _modelPlaceholder;
        private CharacterPreview3D _preview3D;
        private InkEquipSlot[] _slots;
        private string[] _slotBaseGlyphs;
        private float[] _slotXFrac, _slotXPx, _slotYFrac, _slotYPx;
        private bool[] _slotXRight, _slotYBottom;
        private ContainerControl _previewBadge;
        private ContainerControl _rotateHint;
        private ContainerControl _appTabBar;
        private AppearanceTab[] _appTabs;

        // 右列
        private InkScrollArea _rightPanel;
        private ContainerControl _rightScroll;
        private readonly List<ContainerControl> _cards = new List<ContainerControl>();
        // 物品详情弹窗（浮层 + 遮罩；不固定在界面内，点击行囊物品时弹出）
        private ContainerControl _itemDetailCard;
        private ContainerControl _detailOverlay;
        private readonly List<Label> _headerSubtitles = new List<Label>();
        private readonly List<Control> _centeredControls = new List<Control>();
        private readonly List<ContainerControl> _fullWidthHairlines = new List<ContainerControl>();
        private readonly List<Label> _rightCountLabels = new List<Label>();
        private readonly List<Control> _stretchRows = new List<Control>();

        // ===== 背包区块（合并自 InventoryPage） =====
        private ContainerControl _invGridBox;
        private InvCell[] _invCells;
        private CellData[] _invCellData;
        private int _invSelected = -1;
        private int _invFilterIndex;              // 0=全部 1=武器 2=防具 3=丹药 4=材料 5=任务
        private InvFilterChip[] _invFilterChips;
        private ContainerControl _invCapFill;
        private Label _invCountLabel;
        // 物品详情
        private DetailIconBox _invDetailIcon;
        private Label _invDetailName;
        private InvTagBox _invTagQuality;
        private InvTagBox _invTagType;
        private InvTagBox _invTagQty;
        private Label _invMetaEnhance;
        private Label _invMetaElement;
        private Label _invMetaBind;
        private Label _invMetaDura;
        private ContainerControl _invDuraFill;
        private ContainerControl _invBaseAttrHost;
        private ContainerControl _invDescHost;
        private Label _invSetLabel;
        private Label _invSetDescLabel;
        private InkButton _invUseBtn;
        private InkButton _invEquipBtn;
        private InkButton _invDropBtn;
        private Label _invStatusLabel;
        private ContainerControl _invGemTitle;
        private ContainerControl _invGemRowHost;

        // 宝石选择窗（点宝石槽弹出；列出背包里全部宝石 + 空槽时可选"卸下"）
        private ContainerControl _gemPickerOverlay;
        private ContainerControl _gemPickerPanel;
        private Label _gemPickerTitle;
        private ContainerControl _gemPickerRows;
        private long _gemPickerEquipItemId;
        private int _gemPickerSlot = -1;
        private int _gemPickerCurrentTemplate;

        // ===== 装备槽 Tooltip =====
        private InkItemTooltip _slotTooltip;

        // ===== 属性卡绑定（组件值 = 基值 + 装备加成） =====
        private Label[] _baseStatValueLabels;      // 攻/防/暴击率/暴伤/命中/闪避
        private Label _combatPowerLabel;
        private Label[] _combatValueLabels;        // 境界/门派/阵营/侠义/恶名/经验
        private float[] _wuxingRadarValues;        // 五行雷达（0..100 归一，InkRadarChart 每帧读数组）
        private string[] _wuxingRadarTexts;        // 五行雷达顶点对应的真实亲和数值文本
        private float[] _fiveDimValues;            // 五维雷达（体/御/敏/势/劲，0..100 归一）
        private string[] _fiveDimTexts;            // 五维雷达顶点显示文本

        // 属性加点（合并卡的右栏；左栏为基础属性）。pending = 本次未提交的预览点数。
        // 九行分两列；所有子控件一律 TopLeft 锚（非原点锚点在构造期会按未定的父尺寸固化位置，
        // 曾导致 －/＋/确认 整体不渲染），位置全部由 LayoutCards 在父尺寸已知后显式重排。
        private ContainerControl _attrLeftCol;
        private ContainerControl _attrRightCol;
        private ContainerControl _allocColLeft;
        private ContainerControl _allocColRight;
        private readonly List<ContainerControl> _allocRows = new List<ContainerControl>();
        private readonly List<Control> _allocMinusButtons = new List<Control>();
        private readonly List<Control> _allocPlusButtons = new List<Control>();
        private Label _allocSummaryLabel;
        private Label[] _allocSpentLabels;
        private Label[] _allocPendingLabels;
        private readonly int[] _allocPending = new int[9];
        /// <summary>已把期望终态发给服务端、回执未回：期间锁住预览与提交，避免重复发单。</summary>
        private bool _allocSubmitting;
        private Control _allocConfirmBtn;
        private Control _allocResetBtn;
        private InkAttributeTooltip _allocTooltip;

        // 13 个浮动槽对应的真实装备槽位（自上而下、左右成对；暂不展示肩部与腰部）。
        private static readonly ClientSlot[] SlotClientIds =
        {
            ClientSlot.Neck, ClientSlot.Head, ClientSlot.Face,
            ClientSlot.RightHand, ClientSlot.LeftHand,
            ClientSlot.RightWrist, ClientSlot.LeftWrist,
            ClientSlot.RightRing, ClientSlot.LeftRing,
            ClientSlot.Back, ClientSlot.Body, ClientSlot.Legs, ClientSlot.Feet,
        };

        public CharacterPanelPage()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            Offsets = Margin.Zero;
            BackgroundColor = InkWashTheme.Scrim;
            ClipChildren = false;
            AutoFocus = false;

            BuildTopBar();
            BuildLeftColumn();
            BuildRightColumn();
            BuildItemDetailPopup();   // 物品详情弹窗浮层（根节点上层，独立于卡片流）
            if (_slotTooltip != null) AddChild(_slotTooltip);   // Tooltip 挂页面根节点（最上层绘制，不被左右栏裁剪）
            BuildGemPickerPopup();    // 宝石选择窗（最顶层，压在详情弹窗之上）

            // 权威背包/装备快照驱动 UI：页面由路由器缓存、进程内单实例，构造期订阅一次即可。
            PlayerInventoryService.Instance.Changed += RefreshInventoryView;
            PlayerInventoryService.Instance.OperationFinished += OnItemOperationFinished;
            if (!PlayerInventoryService.Instance.HasServerData)
                PlayerInventoryService.Instance.RequestFullRefresh();

            // 属性加点同样走服务端权威：回执只订阅一次，页面销毁时退订。
            Services.AttributeAllocationService.Instance.OperationFinished += OnAllocationOperationFinished;
        }

        public override void OnDestroy()
        {
            PlayerInventoryService.Instance.Changed -= RefreshInventoryView;
            PlayerInventoryService.Instance.OperationFinished -= OnItemOperationFinished;
            Services.AttributeAllocationService.Instance.OperationFinished -= OnAllocationOperationFinished;
            if (_boundCharacter != null) _boundCharacter.AttributesChanged -= OnAttributesChanged;
            base.OnDestroy();
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.R, c.G, c.B, a);

        public void BindCharacter(CharacterAttributesComponent component)
        {
            // 换绑时退订旧组件，避免重复订阅与访问已销毁对象。
            if (!ReferenceEquals(_boundCharacter, component))
            {
                if (_boundCharacter != null) _boundCharacter.AttributesChanged -= OnAttributesChanged;
            }
            _boundCharacter = component;
            if (component != null)
            {
                if (_nameLabel != null) _nameLabel.Text = component.Nickname;
                if (_levelLabel != null) _levelLabel.Text = "Lv." + component.Level.ToString();
                component.AttributesChanged += OnAttributesChanged;
            }
            // 绑定实时 3D 角色模型（场景未就绪时组件内部会暂存并重放）
            _preview3D?.SetCharacter(component);
            // 换绑角色时丢弃未提交的预览点数，并把属性卡/五行雷达按当前（含装备加成）真值刷新一次。
            System.Array.Clear(_allocPending, 0, _allocPending.Length);
            RefreshAttributeCards();
        }

        private void OnAttributesChanged()
        {
            RefreshAttributeCards();
        }

        /// <summary>把基础属性卡、战斗属性卡的攻/防与五行雷达刷新为组件真值（基值+装备加成）。</summary>
        private void RefreshAttributeCards()
        {
            try
            {
                var c = _boundCharacter;
                if (c == null) return;

                // 基础属性卡：攻击力=物攻+法攻，防御力=物防+法防；暴击率/暴伤接加点衍生真值；
                // 命中/闪避无数据来源，保持"-"。
                if (_baseStatValueLabels != null && _baseStatValueLabels.Length >= 4)
                {
                    _baseStatValueLabels[0].Text = FormatStat(c.PhysicalAttack + c.MagicAttack);
                    _baseStatValueLabels[1].Text = FormatStat(c.PhysicalDefense + c.MagicDefense);
                    _baseStatValueLabels[2].Text = $"{c.CritRatePercent:0.##}%";
                    _baseStatValueLabels[3].Text = $"+{c.CritDamageBonusPercent:0.##}%";
                }

                // 战斗属性卡：境界=成长阶段真值，修炼经验=Exp/MaxExp（其余项暂无数据源，保持"-"）。
                if (_combatValueLabels != null)
                {
                    if (_combatValueLabels.Length > CombatIdxStage)
                        _combatValueLabels[CombatIdxStage].Text = StageName(c.CurrentStage);
                    if (_combatValueLabels.Length > CombatIdxExp)
                    {
                        double pct = c.MaxExp > 0 ? Mathf.Clamp((double)c.Exp * 100.0 / c.MaxExp, 0.0, 100.0) : 0.0;
                        _combatValueLabels[CombatIdxExp].Text = $"{pct:0}%";
                    }
                }

                // 战斗力（32px 主数值）：攻防生命合成的占位口径。
                if (_combatPowerLabel != null)
                {
                    float power = (c.PhysicalAttack + c.MagicAttack) * 2f
                                  + (c.PhysicalDefense + c.MagicDefense) * 1.5f
                                  + c.MaxHealth * 0.2f;
                    _combatPowerLabel.Text = System.Math.Round(power).ToString("N0");
                }

                RefreshWuxingRadar(c);
                RefreshFiveDimRadar(c);
                RefreshAllocationRows();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CharacterPanelPage] RefreshAttributeCards: {ex.Message}");
            }
        }

        private static string FormatStat(float v)
            => v >= 1000f ? v.ToString("N0") : v.ToString("0.#");

        // 战斗属性卡行索引（与 BuildCombatCard 的 names 数组一致）。
        private const int CombatIdxStage = 0;
        private const int CombatIdxExp = 5;

        private static string StageName(CharacterStage stage) => stage switch
        {
            CharacterStage.Wuxia => "武侠",
            CharacterStage.Xianxia => "仙侠",
            CharacterStage.Xuanhuan => "玄幻",
            _ => stage.ToString(),
        };

        /// <summary>五行雷达图接组件亲和真值：数值文本=亲和值，多边形长度按 max(100, 最高亲和) 归一。</summary>
        private void RefreshWuxingRadar(CharacterAttributesComponent c)        {
            if (_wuxingRadarValues == null || _wuxingRadarTexts == null) return;

            int[] aff = { c.MetalAffinity, c.WoodAffinity, c.WaterAffinity, c.FireAffinity, c.EarthAffinity };
            float maxV = 100f;
            for (int i = 0; i < aff.Length; i++)
                if (aff[i] > maxV) maxV = aff[i];

            for (int i = 0; i < aff.Length; i++)
            {
                _wuxingRadarValues[i] = aff[i] * 100f / maxV;
                _wuxingRadarTexts[i] = aff[i].ToString();
            }
        }

        // 五维归一口径（页内展示用标尺，非服务端定义）：体=生命上限/2000、御=物防+法防/500、
        // 劲=物攻+法攻/500，映射到 0..100；敏/势无权威数据源，顶点留空。穿戴装备后三轴实时增长。
        private const float FiveDimBodyCap = 2000f;
        private const float FiveDimDefenseCap = 500f;
        private const float FiveDimAttackCap = 500f;

        private void RefreshFiveDimRadar(CharacterAttributesComponent c)
        {
            if (_fiveDimValues == null || _fiveDimTexts == null) return;

            float body = c.MaxHealth;
            float defense = c.PhysicalDefense + c.MagicDefense;
            float attack = c.PhysicalAttack + c.MagicAttack;

            _fiveDimValues[0] = Mathf.Clamp(body / FiveDimBodyCap * 100f, 0f, 100f);
            _fiveDimValues[1] = Mathf.Clamp(defense / FiveDimDefenseCap * 100f, 0f, 100f);
            _fiveDimValues[2] = 0f;                       // 敏：无数据源
            _fiveDimValues[3] = 0f;                       // 势：无数据源
            _fiveDimValues[4] = Mathf.Clamp(attack / FiveDimAttackCap * 100f, 0f, 100f);

            _fiveDimTexts[0] = FormatStat(body);
            _fiveDimTexts[1] = FormatStat(defense);
            _fiveDimTexts[2] = "-";
            _fiveDimTexts[3] = "-";
            _fiveDimTexts[4] = FormatStat(attack);
        }

        // ===================================================================
        // 属性加点（基值 + 装备 + 加点 三层合一；换算常量以组件为单一来源）
        // ===================================================================

        private static readonly (string Label, CharacterAttributesComponent.AllocationKind Kind, string Desc)[] AllocationRowDefs =
        {
            ("力量", CharacterAttributesComponent.AllocationKind.Strength, "每点：气血上限+20\n每点：攻击力+10"),
            ("敏捷", CharacterAttributesComponent.AllocationKind.Agility, "每点：防御+20\n每点：速度+3"),
            ("体质", CharacterAttributesComponent.AllocationKind.Health, "每点：气血上限+100"),
            ("内力", CharacterAttributesComponent.AllocationKind.Energy, "每点：内力上限+10"),
            ("金",   CharacterAttributesComponent.AllocationKind.Metal, "每点：攻击+50、金伤+30、金防+5\n破木防+30、暴击率+0.1%、暴伤+3%\n另：金亲和+10"),
            ("木",   CharacterAttributesComponent.AllocationKind.Wood, "每点：气血+300、木伤+20、木防+5\n破土防+30、回血+15/秒\n毒伤几率+0.1%、治疗量+0.2%\n另：木亲和+10"),
            ("水",   CharacterAttributesComponent.AllocationKind.Water, "每点：内力+200、水伤+30、水防+5\n破火防+30、冰冻几率+0.1%\n减速+0.2%（上限70%）\n另：水亲和+10"),
            ("火",   CharacterAttributesComponent.AllocationKind.Fire, "每点：攻速+2、火伤+30、破金防+30\n移速+5、极速攻击几率+0.1%\n（触发时3倍攻速）\n另：火亲和+10"),
            ("土",   CharacterAttributesComponent.AllocationKind.Earth, "每点：防御+50、土伤+30、土防+5\n破水防+30、反伤几率+0.1%\n（反弹目标实际攻击的10%）\n另：土亲和+10"),
        };

        private static int AllocationSpentOf(in CharacterAttributesComponent.AttributeAllocation a,
            CharacterAttributesComponent.AllocationKind kind)
        {
            switch (kind)
            {
                case CharacterAttributesComponent.AllocationKind.Strength: return a.Strength;
                case CharacterAttributesComponent.AllocationKind.Agility: return a.Agility;
                case CharacterAttributesComponent.AllocationKind.Health: return a.Health;
                case CharacterAttributesComponent.AllocationKind.Energy: return a.Energy;
                case CharacterAttributesComponent.AllocationKind.Metal: return a.Metal;
                case CharacterAttributesComponent.AllocationKind.Wood: return a.Wood;
                case CharacterAttributesComponent.AllocationKind.Water: return a.Water;
                case CharacterAttributesComponent.AllocationKind.Fire: return a.Fire;
                default: return a.Earth;
            }
        }

        private int PendingTotal()
        {
            int t = 0;
            for (int i = 0; i < _allocPending.Length; i++) t += _allocPending[i];
            return t;
        }

        /// <summary>＋：往预览池加一点（受 可用点 − 已预览总数 约束），不直接改属性。</summary>
        private void OnAllocPlus(int row)
        {
            var c = _boundCharacter;
            if (c == null) { UIHelper.ShowToast("角色数据未就绪", ToastType.Info); return; }
            if (_allocSubmitting) return;
            if (_allocPending[row] + PendingTotal() > 0 || c.AllocationPointsAvailable > PendingTotal())
                _allocPending[row]++;
            else
                UIHelper.ShowToast("没有可用的自由点", ToastType.Warning);
            RefreshAllocationRows();
        }

        /// <summary>－：撤回预览点（仅限本次预览量，不动已提交加点）。</summary>
        private void OnAllocMinus(int row)
        {
            if (_allocSubmitting) return;
            if (_allocPending[row] > 0) _allocPending[row]--;
            RefreshAllocationRows();
        }

        /// <summary>
        /// 确认：把"当前权威分配 + 预览增量"作为期望终态提交给服务端（服务端校验点池后缓存 + 落库）。
        /// 本地不预先改组件——属性变化一律由回执驱动，避免客户端算出一套、服务端算出另一套。
        /// </summary>
        private void OnConfirmAllocation()
        {
            var c = _boundCharacter;
            if (c == null) { UIHelper.ShowToast("角色数据未就绪", ToastType.Info); return; }
            if (_allocSubmitting) return;
            int pending = PendingTotal();
            if (pending == 0) { UIHelper.ShowToast("没有待确认的加点", ToastType.Info); return; }

            var alloc = c.Allocation;
            var desired = new int[AllocationRowDefs.Length];
            for (int i = 0; i < AllocationRowDefs.Length; i++)
                desired[i] = AllocationSpentOf(in alloc, AllocationRowDefs[i].Kind) + _allocPending[i];

            _allocSubmitting = true;
            _ = Services.AttributeAllocationService.Instance.RequestAllocateAsync(
                Services.AttributeAllocationService.FromRowValues(desired));
            RefreshAllocationRows();
        }

        /// <summary>重置：提交全零终态（服务端据此返还全部点数），预览同时清空。</summary>
        private void OnResetAllocation()
        {
            var c = _boundCharacter;
            System.Array.Clear(_allocPending, 0, _allocPending.Length);
            if (c == null || c.Allocation.Spent == 0 || _allocSubmitting)
            {
                RefreshAllocationRows();
                return;
            }

            _allocSubmitting = true;
            _ = Services.AttributeAllocationService.Instance.RequestAllocateAsync(
                Services.AttributeAllocationService.FromRowValues(new int[AllocationRowDefs.Length]));
            RefreshAllocationRows();
        }

        /// <summary>
        /// 加点回执：服务端已把权威分配灌进组件（AttributesChanged 会带动本卡刷新），
        /// 这里只负责清预览、解除提交中状态并提示结果。
        /// </summary>
        private void OnAllocationOperationFinished(bool success, string message)
        {
            _allocSubmitting = false;
            System.Array.Clear(_allocPending, 0, _allocPending.Length);
            UIHelper.ShowToast(
                success ? (string.IsNullOrEmpty(message) ? "加点已生效" : message)
                        : (string.IsNullOrEmpty(message) ? "加点失败" : message),
                success ? ToastType.Success : ToastType.Warning);
            RefreshAllocationRows();
        }

        /// <summary>刷新加点行：可用点数（扣预览）、各行预览增量与已提交量；无预览或提交中时禁用确认。</summary>
        private void RefreshAllocationRows()
        {
            var c = _boundCharacter;
            int pending = PendingTotal();
            if (_allocSummaryLabel != null)
                _allocSummaryLabel.Text = c == null ? "-"
                    : $"{c.AllocationPointsAvailable - pending} / {c.AllocationPointsTotal}";
            if (_allocSpentLabels == null || c == null) return;

            var alloc = c.Allocation;
            for (int i = 0; i < _allocSpentLabels.Length && i < AllocationRowDefs.Length; i++)
            {
                int spent = AllocationSpentOf(in alloc, AllocationRowDefs[i].Kind);
                _allocSpentLabels[i].Text = spent == 0 ? "未分配" : $"已分配 {spent}";
                if (_allocPendingLabels != null && i < _allocPendingLabels.Length)
                {
                    _allocPendingLabels[i].Text = _allocPending[i] > 0 ? $"+{_allocPending[i]}" : "·";
                    _allocPendingLabels[i].TextColor = _allocPending[i] > 0
                        ? InkWashTheme.GoldBright : InkWashTheme.TextTertiary;
                }
            }
            if (_allocConfirmBtn != null)
            {
                // Control.Enabled=false 只屏蔽交互、不影响渲染（Flax GUI 语义）
                _allocConfirmBtn.Enabled = pending > 0 && !_allocSubmitting;
            }
        }

        public void RefreshLayout()
        {
            try
            {
                float w = Width;
                float h = Height;
                if (w <= 0f || h <= 0f) return;

                if (_topBar != null)
                    _topBar.Size = new Float2(w, TopBarHeight);

                // 顶栏右侧元素：关闭按钮 / 等级 / 名称 / 分隔线
                if (_closeButton != null)
                    _closeButton.Location = new Float2(w - ContentPad - CloseBtnSize, (TopBarHeight - CloseBtnSize) * 0.5f);
                if (_levelLabel != null && _closeButton != null)
                    _levelLabel.Location = new Float2(_closeButton.Left - 8f - 70f, 0f);
                if (_topDivider != null && _levelLabel != null)
                    _topDivider.Location = new Float2(_levelLabel.Left - 8f - 1f, (TopBarHeight - 20f) * 0.5f);
                if (_nameLabel != null && _topDivider != null)
                    _nameLabel.Location = new Float2(_topDivider.Left - 8f - 90f, 0f);

                // 两列内容区
                float contentTop = TopBarHeight + ContentPad;
                float contentH = h - contentTop - ContentPad;
                float availW = w - ContentPad * 2f - ColumnGap;
                float leftW = availW * LeftRatio;
                float rightW = availW - leftW;

                if (_leftPanel != null)
                {
                    _leftPanel.Size = new Float2(leftW, contentH);
                    _leftPanel.Location = new Float2(ContentPad, contentTop);
                }
                if (_rightPanel != null)
                {
                    _rightPanel.Size = new Float2(rightW, contentH);
                    _rightPanel.Location = new Float2(ContentPad + leftW + ColumnGap, contentTop);
                }
                if (_rightScroll != null && _rightPanel != null)
                    _rightScroll.Size = new Float2(_rightPanel.Width, _rightScroll.Height);

                LayoutModelStage();
                LayoutCards();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CharacterPanelPage] RefreshLayout: {ex.Message}");
            }
        }

        private void LayoutModelStage()
        {
            if (_modelStage == null) return;
            float mw = _modelStage.Width;
            float mh = _modelStage.Height;

            // 模型占位居中
            if (_modelPlaceholder != null)
                _modelPlaceholder.Location = new Float2((mw - _modelPlaceholder.Width) * 0.5f, (mh - _modelPlaceholder.Height) * 0.5f);

            // 3D 预览铺满展示区并刷新渲染管线；就绪后隐藏 2D 占位提示
            if (_preview3D != null)
            {
                _preview3D.Size = new Float2(mw, mh);
                _preview3D.RefreshLayout();
                if (_modelPlaceholder != null && _preview3D.IsReady)
                    _modelPlaceholder.Visible = false;
            }

            // 浮动装备槽
            if (_slots != null)
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    float cx = _slotXFrac[i] >= 0f
                        ? _slotXFrac[i] * mw
                        : (_slotXRight[i] ? mw - _slotXPx[i] - SlotSize * 0.5f : _slotXPx[i] + SlotSize * 0.5f);
                    float cy = _slotYFrac[i] >= 0f
                        ? _slotYFrac[i] * mh
                        : (_slotYBottom[i] ? mh - _slotYPx[i] - SlotSize * 0.5f : _slotYPx[i] + SlotSize * 0.5f);
                    _slots[i].Location = new Float2(cx - SlotSize * 0.5f, cy - SlotSize * 0.5f);
                }
            }

            // 拖拽旋转标签（右上 14px）
            if (_rotateHint != null)
                _rotateHint.Location = new Float2(mw - 14f - _rotateHint.Width, 14f);

            // 外观切换 Tab 栏（底部 12px）
            if (_appTabBar != null)
            {
                float barW = mw - 24f;
                _appTabBar.Size = new Float2(barW, _appTabBar.Height);
                _appTabBar.Location = new Float2(12f, mh - 12f - _appTabBar.Height);
                if (_appTabs != null)
                {
                    float tabW = (barW - 12f - 8f) / _appTabs.Length;
                    for (int i = 0; i < _appTabs.Length; i++)
                    {
                        _appTabs[i].Location = new Float2(6f + i * (tabW + 4f), 6f);
                        _appTabs[i].Size = new Float2(tabW, _appTabBar.Height - 12f);
                    }
                }
            }
        }

        private void LayoutCards()
        {
            if (_rightScroll == null) return;
            float cw = _rightScroll.Width - 6f;
            float cy = 0f;
            foreach (var card in _cards)
            {
                card.Size = new Float2(cw, card.Height);
                card.Location = new Float2(0f, cy);
                cy += card.Height + CardGap;
            }
            // 标题行英文副标题右对齐（宽度随卡片）
            foreach (var sub in _headerSubtitles)
            {
                if (sub.Parent != null)
                {
                    float pad = 14f;
                    sub.Size = new Float2(sub.Parent.Width - pad * 2f, sub.Height);
                    sub.Location = new Float2(pad, sub.Location.Y);
                }
            }
            // 雷达图 / 图例水平居中
            foreach (var ctl in _centeredControls)
            {
                if (ctl.Parent != null)
                    ctl.Location = new Float2((ctl.Parent.Width - ctl.Width) * 0.5f, ctl.Location.Y);
            }
            // 全宽发丝线：宽度 = 卡片宽 - 2*pad
            foreach (var hl in _fullWidthHairlines)
            {
                if (hl.Parent != null)
                    hl.Size = new Float2(hl.Parent.Width - 28f, 1f);
            }
            // 右对齐计数标签（x = 卡片宽 - pad - 标签宽）
            foreach (var lb in _rightCountLabels)
            {
                if (lb.Parent != null)
                    lb.Location = new Float2(lb.Parent.Width - 12f - lb.Width, lb.Location.Y);
            }
            // 拉伸行：宽度 = 卡片宽 - 2*pad
            foreach (var row in _stretchRows)
            {
                if (row.Parent != null)
                    row.Size = new Float2(row.Parent.Width - 24f, row.Height);
            }
            // 属性合并卡双栏：右栏（属性加点）随卡片宽度贴右缘；两子列与行尾控件在父尺寸已知后显式重排
            if (_attrRightCol != null && _attrRightCol.Parent != null)
            {
                float rw = _attrRightCol.Parent.Width - 320f - CardPad * 2f;
                if (rw < 470f) rw = 470f;
                _attrRightCol.Location = new Float2(_attrRightCol.Parent.Width - CardPad - rw, _attrRightCol.Location.Y);
                _attrRightCol.Size = new Float2(rw, _attrRightCol.Height);

                float colW = (rw - 10f) * 0.5f;
                if (_allocColLeft != null) _allocColLeft.Size = new Float2(colW, _allocColLeft.Height);
                if (_allocColRight != null)
                {
                    _allocColRight.Location = new Float2(colW + 10f, _allocColRight.Location.Y);
                    _allocColRight.Size = new Float2(rw - colW - 10f, _allocColRight.Height);
                }

                // 行容器铺满列宽；行尾 －/预览/＋ 随列宽贴右缘
                for (int i = 0; i < _allocRows.Count && i < AllocationRowDefs.Length; i++)
                {
                    var rowCtl = _allocRows[i];
                    float w = i < 5 ? _allocColLeft.Width : _allocColRight.Width;
                    rowCtl.Size = new Float2(w, rowCtl.Height);
                    _allocPlusButtons[i].Location = new Float2(w - 26f, 2f);
                    if (_allocPendingLabels[i] != null) _allocPendingLabels[i].Location = new Float2(w - 58f, 3f);
                    _allocMinusButtons[i].Location = new Float2(w - 86f, 2f);
                }

                if (_allocConfirmBtn != null)
                    _allocConfirmBtn.Location = new Float2(rw - 100f, _allocConfirmBtn.Location.Y);
            }
            // 滚动内容高度 = 卡片总高，并夹取滚动位置
            _rightScroll.Size = new Float2(_rightScroll.Width, cy);
            if (_rightPanel != null)
            {
                float minY = Mathf.Min(0f, _rightPanel.Height - cy);
                float sy = Mathf.Clamp(_rightScroll.Location.Y, minY, 0f);
                _rightScroll.Location = new Float2(_rightScroll.Location.X, sy);
            }
        }

        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }

        // ===================================================================
        // 顶栏
        // ===================================================================

        private void BuildTopBar()
        {
            _topBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(Width, TopBarHeight),
                BackgroundColor = new Color(InkWashTheme.BaseSecondary.R, InkWashTheme.BaseSecondary.G, InkWashTheme.BaseSecondary.B, 0.9f),
            };
            AddChild(_topBar);

            _topBar.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, TopBarHeight - 1f),
                Size = new Float2(4000f, 1f),
                BackgroundColor = InkWashTheme.BorderGoldSubtle,
            });

            // user 图标（以"人"字符替代 lucide user）
            _topBar.AddChild(MakeLabel("人", ContentPad, 0f, 18f, TopBarHeight,
                InkWashTheme.GoldPrimary, 14f, InkWashTheme.FontRole.Body, TextAlignment.Center));

            // 标题：角色信息（楷书 18px 金色）
            _topBar.AddChild(MakeLabel("角色信息", ContentPad + 30f, 0f, 110f, TopBarHeight,
                InkWashTheme.GoldPrimary, 18f, InkWashTheme.FontRole.Display, TextAlignment.Near));

            // 副标题：CHARACTER
            _topBar.AddChild(MakeLabel("CHARACTER", ContentPad + 30f + 118f, 0f, 100f, TopBarHeight,
                InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));

            // 关闭按钮 32x32
            _closeButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "X",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Width > 0f ? Width - ContentPad - CloseBtnSize : 900f, (TopBarHeight - CloseBtnSize) * 0.5f),
                Size = new Float2(CloseBtnSize, CloseBtnSize),
                BorderColor = InkWashTheme.BorderGoldSubtle,
                BorderThickness = 1f,
            };
            _closeButton.Clicked += OnCloseClicked;
            _topBar.AddChild(_closeButton);

            // 等级（DIN 14px 金色）
            _levelLabel = MakeLabel("Lv.60", 0f, 0f, 70f, TopBarHeight,
                InkWashTheme.GoldPrimary, 14f, InkWashTheme.FontRole.Number, TextAlignment.Far);
            _topBar.AddChild(_levelLabel);

            // 名称与等级之间的分隔线 1x20
            _topDivider = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, (TopBarHeight - 20f) * 0.5f),
                Size = new Float2(1f, 20f),
                BackgroundColor = InkWashTheme.BorderGoldSubtle,
            };
            _topBar.AddChild(_topDivider);

            // 名称（楷书 14px 亮色）
            _nameLabel = MakeLabel("逍遥客", 0f, 0f, 90f, TopBarHeight,
                InkWashTheme.TextDefault, 14f, InkWashTheme.FontRole.Display, TextAlignment.Far);
            _topBar.AddChild(_nameLabel);
        }

        // ===================================================================
        // 左列 — 3D 模型展示区
        // ===================================================================

        private void BuildLeftColumn()
        {
            _leftPanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                ClipChildren = false,
            };
            AddChild(_leftPanel);

            _modelStage = new ModelStage
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
            };
            _leftPanel.AddChild(_modelStage);

            _modelPlaceholder = MakeLabel("3D 角色模型", 0f, 0f, 200f, 40f,
                InkWashTheme.TextTertiary, 16f, InkWashTheme.FontRole.Display, TextAlignment.Center);
            _modelStage.AddChild(_modelPlaceholder);

            // 实时 3D 角色预览（透明背景嵌入，由 ModelStage 提供渐变底色与金边）
            // 注意：须用 TopLeft 锚点。Location 在构造期设置时父级尺寸未定，
            // BottomCenter 等锚点会基于错误尺寸固化偏移导致错位；TopLeft 偏移恒为(0,0)，
            // 配合 LayoutModelStage 的 Size=(mw,mh) 可稳定铺满展示区。
            _preview3D = new CharacterPreview3D
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                TransparentBackground = true,
            };
            _modelStage.AddChild(_preview3D);

            BuildFloatingSlots();
            BuildStageBadges();
            BuildAppearanceTabs();
        }

        /// <summary>
        /// 13 个浮动装备槽（对应设计方案 cp-slot--float 三态）。
        /// 位置编码：百分比居中 或 边缘像素偏移。
        /// </summary>
        private void BuildFloatingSlots()
        {
            // (glyph, xFrac, xPx, xRight, yFrac, yPx, yBottom)；状态/品质由服务端装备快照驱动。
            var defs = new (string g, float xf, float xp, bool xr, float yf, float yp, bool yb)[]
            {
                ("链", 0.30f, 0f,  false, -1f,   12f, false),
                ("头", 0.50f, 0f,  false, -1f,   12f, false),
                ("面", 0.70f, 0f,  false, -1f,   12f, false),
                ("武", -1f,   12f, false, 0.30f, 0f,  false),
                ("副", -1f,   12f, true,  0.30f, 0f,  false),
                ("腕", -1f,   8f,  false, 0.48f, 0f,  false),
                ("腕", -1f,   8f,  true,  0.48f, 0f,  false),
                ("戒", -1f,   12f, false, 0.66f, 0f,  false),
                ("戒", -1f,   12f, true,  0.66f, 0f,  false),
                ("披", 0.18f, 0f,  false, -1f,   64f, true),
                ("衣", 0.38f, 0f,  false, -1f,   64f, true),
                ("腿", 0.58f, 0f,  false, -1f,   64f, true),
                ("脚", 0.78f, 0f,  false, -1f,   64f, true),
            };

            int n = defs.Length;
            _slots = new InkEquipSlot[n];
            _slotBaseGlyphs = new string[n];
            _slotXFrac = new float[n]; _slotXPx = new float[n]; _slotXRight = new bool[n];
            _slotYFrac = new float[n]; _slotYPx = new float[n]; _slotYBottom = new bool[n];

            for (int i = 0; i < n; i++)
            {
                var d = defs[i];
                var slot = new InkEquipSlot(InkEquipSlot.SlotState.Empty,
                    InkWashTheme.QualityColor(InkWashTheme.InkQuality.Common), d.g);
                int captured = i;
                slot.Clicked += () => OnSlotClicked(captured);
                slot.HoverEnter += () => OnSlotHoverEnter(captured);
                slot.HoverLeave += () => OnSlotHoverLeave(captured);
                _slots[i] = slot;
                _slotBaseGlyphs[i] = d.g;
                _modelStage.AddChild(slot);

                _slotXFrac[i] = d.xf; _slotXPx[i] = d.xp; _slotXRight[i] = d.xr;
                _slotYFrac[i] = d.yf; _slotYPx[i] = d.yp; _slotYBottom[i] = d.yb;
            }

            // 装备槽 Tooltip（可复用组件，布局与物品详情卡片同构）
            _slotTooltip = new InkItemTooltip(new InkTooltipOptions
            {
                Width = 300f,
                ShowIcon = true,
                ShowTags = true,
                ShowMeta = true,
                ShowDurability = false,
                ShowAttrs = true,
                ShowGems = true,
                ShowDesc = true,
                ShowSetEffect = false,
                ShowButtons = false,
            });
        }

        /// <summary>3D预览（左上）与拖拽旋转（右上）标签。</summary>
        private void BuildStageBadges()
        {
            _previewBadge = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(14f, 14f),
                Size = new Float2(84f, 24f),
                BackgroundColor = WithAlpha(InkWashTheme.Void, 0.7f),
            };
            _previewBadge.AddChild(MakeLabel("◻", 6f, 0f, 14f, 24f,
                InkWashTheme.GoldPrimary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            _previewBadge.AddChild(MakeLabel("3D预览", 22f, 0f, 58f, 24f,
                InkWashTheme.GoldPrimary, 11f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            _modelStage.AddChild(_previewBadge);

            _rotateHint = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 14f),
                Size = new Float2(84f, 24f),
                BackgroundColor = WithAlpha(InkWashTheme.Void, 0.7f),
            };
            _rotateHint.AddChild(MakeLabel("↻", 6f, 0f, 14f, 24f,
                InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            _rotateHint.AddChild(MakeLabel("拖拽旋转", 22f, 0f, 58f, 24f,
                InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            _modelStage.AddChild(_rotateHint);
        }

        /// <summary>外观切换 Tab（发型/脸型/肤色），底部 12px。</summary>
        private void BuildAppearanceTabs()
        {
            _appTabBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(300f, 42f),
                BackgroundColor = WithAlpha(InkWashTheme.BaseSecondary, 0.8f),
            };
            _modelStage.AddChild(_appTabBar);

            string[] tabNames = { "发型", "脸型", "肤色" };
            _appTabs = new AppearanceTab[tabNames.Length];
            for (int i = 0; i < tabNames.Length; i++)
            {
                var tab = new AppearanceTab(tabNames[i]) { IsActive = (i == 0) };
                int captured = i;
                tab.Clicked += () => OnAppearanceTabClicked(captured);
                _appTabs[i] = tab;
                _appTabBar.AddChild(tab);
            }
        }

        // ===================================================================
        // 右列 — 可滚动属性面板（8 个卡片）
        // ===================================================================

        private void BuildRightColumn()
        {
            _rightScroll = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };
            _rightPanel = new InkScrollArea(_rightScroll)
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _rightPanel.AddChild(_rightScroll);
            AddChild(_rightPanel);

            _cards.Add(BuildAttrOverviewCard());   // 1. 属性总览（五维/五行并排）
            _cards.Add(BuildAttributeCard());      // 2. 属性：左基础属性 + 右属性加点（双栏合一卡）
            _cards.Add(BuildCombatCard());         // 3. 战斗属性
            _cards.Add(BuildInventoryCard());      // 5. 行囊
            _cards.Add(BuildTiaoLvCard());         // 5. 调律
            _cards.Add(BuildDingYinCard());        // 6. 定音
            _cards.Add(BuildDieYinCard());         // 7. 叠音

            foreach (var card in _cards)
                _rightScroll.AddChild(card);

            // 物品详情：改为浮层弹窗承载（不占卡片流；点击行囊物品时弹出，见 BuildItemDetailPopup）
            _itemDetailCard = BuildItemDetailCard();

            // 初始化：有真实数据才选中首格（快照晚到时由 Changed 事件补刷新）
            if (_invCellData != null && _invCellData.Length > 0 && _invCellData[0].HasItem)
                ApplyInvSelection(0, emitGold: false);
            else
                UpdateDetailButtons(null);
            RefreshEquipSlots();
        }

        // ===================================================================
        // 卡片与标签辅助方法
        // ===================================================================

        /// <summary>创建 ds-card 风格卡片容器（圆角背景 + 边框）。</summary>
        private static InkCard MakeCard(float height)
        {
            return new InkCard { Height = height };
        }

        /// <summary>创建卡片标题行：中文标题（金色楷书 14px）+ 英文副标题（弱色 10px 右对齐）。</summary>
        private void AddCardHeader(ContainerControl card, string title, string subtitle, float pad)
        {
            card.AddChild(MakeLabel(title, pad, pad, 140f, 18f,
                InkWashTheme.GoldPrimary, 14f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            var sub = MakeLabel(subtitle, pad, pad + 4f, 140f, 14f,
                InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Far);
            _headerSubtitles.Add(sub);
            card.AddChild(sub);
        }

        /// <summary>创建标签控件。</summary>
        private static Label MakeLabel(string text, float x, float y, float w, float h,
            Color color, float fontSize, InkWashTheme.FontRole role, TextAlignment hAlign)
        {
            return new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(w, h),
                Text = text,
                TextColor = color,
                Font = InkRenderHelper.GetFontRef(role, fontSize),
                HorizontalAlignment = hAlign,
                VerticalAlignment = TextAlignment.Center,
                AutoFocus = false,
            };
        }

        /// <summary>创建金色发丝分隔线（gold 0.06）。</summary>
        private static ContainerControl MakeHairline(float x, float y, float w)
        {
            return new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(w, 1f),
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.06f),
            };
        }

        // ===================================================================
        // 事件处理
        // ===================================================================

        private void OnCloseClicked()
        {
            NavigationRequested?.Invoke(InkPageDomIds.CombatHud);
        }

        private void OnSlotClicked(int index)
        {
            if (_slots == null || index < 0 || index >= _slots.Length) return;
            // 选中态视觉反馈由装备槽控件自身处理；此处触发金粉粒子
            if (_particleSystem != null)
            {
                var center = new Float2(_slots[index].Width * 0.5f, _slots[index].Height * 0.5f);
                var screenPos = _slots[index].PointToScreen(center);
                var localPos = _particleSystem.PointFromScreen(screenPos);
                _particleSystem.EmitGoldBurst(localPos, count: 8, isLarge: false);
            }

            // 已装备的槽位：点击 = 向服务端发起卸下（成功回执后快照刷新，物品回到行囊、3D 外观同步更新）。
            var slot = SlotClientIds[index];
            if (PlayerInventoryService.Instance.TryGetEquipped(slot, out _))
            {
                _ = PlayerInventoryService.Instance.RequestUnequipAsync(slot);
                SetInvStatus("卸下中…", false);
            }
        }

        private void OnSlotHoverEnter(int index)
        {
            if (_slots == null || _slotTooltip == null) return;
            if (index < 0 || index >= _slots.Length) return;
            _slotTooltip.Populate(BuildSlotTooltip(index));
            PositionSlotTooltip(_slots[index]);
            _slotTooltip.Visible = true;
        }

        private void OnSlotHoverLeave(int index)
        {
            if (_slotTooltip != null) _slotTooltip.Visible = false;
        }

        /// <summary>将 Tooltip 定位到装备槽侧旁（右侧优先、空间不足自动翻转到左侧，并做上下边界夹取）。</summary>
        private void PositionSlotTooltip(Control slot)
        {
            const float gap = 10f;
            const float edgePad = 8f;
            // 插槽屏幕坐标 → 页面局部坐标（Tooltip 挂在页面根节点）
            var topLeft = PointFromScreen(slot.PointToScreen(Float2.Zero));
            var topRight = PointFromScreen(slot.PointToScreen(new Float2(slot.Width, 0f)));
            float midY = PointFromScreen(slot.PointToScreen(new Float2(slot.Width * 0.5f, slot.Height * 0.5f))).Y;

            float tw = _slotTooltip.Width;
            float th = _slotTooltip.Height;
            float x = topRight.X + gap + tw <= Width
                ? topRight.X + gap
                : topLeft.X - gap - tw;
            float y = Mathf.Clamp(midY - th * 0.5f, edgePad, Mathf.Max(edgePad, Height - th - edgePad));
            _slotTooltip.Location = new Float2(x, y);
        }

        /// <summary>把 0..4 品质值映射到 InkQuality 序数（越界夹取）。</summary>
        private static InkWashTheme.InkQuality ToInkQuality(int quality)
            => (InkWashTheme.InkQuality)Mathf.Clamp(quality, 0, 4);

        /// <summary>
        /// 依据服务端装备快照构建第 <paramref name="index"/> 个浮动槽的 Tooltip 数据。
        /// 空槽显示占位名；已装备则展示真实定义（名称/类型/品质/五行/属性）。
        /// </summary>
        private InkTooltipData BuildSlotTooltip(int index)
        {
            var slot = SlotClientIds[index];
            if (!PlayerInventoryService.Instance.TryGetEquipped(slot, out var entry) || entry.TemplateId <= 0)
                return new InkTooltipData
                {
                    Glyph = _slotBaseGlyphs != null && index < _slotBaseGlyphs.Length ? _slotBaseGlyphs[index] : "",
                    Name = ItemCatalog.SlotDisplayName(slot),
                    Type = "装备",
                    IsEmpty = true,
                };

            var def = ItemCatalog.Resolve(entry.TemplateId);
            var attrs = new (string, string, bool)[def.Stats.Count];
            for (int i = 0; i < attrs.Length; i++)
                attrs[i] = (def.Stats[i].Label, def.Stats[i].Value, false);

            // 宝石槽：孔数取装备定义，内容取服务端权威 entry.Gems（-1=空槽）。
            string[] gemLines = null;
            string gemSummary = null;
            if (def.GemSlots > 0)
            {
                gemLines = new string[def.GemSlots];
                int filled = 0;
                for (int i = 0; i < def.GemSlots; i++)
                {
                    int gemTemplate = entry.Gems != null && i < entry.Gems.Count ? entry.Gems[i] : -1;
                    if (gemTemplate <= 0)
                    {
                        gemLines[i] = "空槽";
                        continue;
                    }
                    var gem = ItemCatalog.Resolve(gemTemplate);
                    filled++;
                    gemLines[i] = $"{gem.Name} · {gem.Element}亲和 +{gem.GemAffinityBonus}";
                }
                gemSummary = $"{filled}/{def.GemSlots}";
            }

            return new InkTooltipData
            {
                Glyph = def.Glyph,
                Name = def.Name,
                Type = def.TypeName,
                Quality = ToInkQuality(def.Quality),
                Element = def.Element,
                ElementColor = def.ElementColor,
                // 绑定/强化/耐久服务端暂无权威字段，留空由组件显示 "-"，不编造值。
                Attrs = attrs,
                SlotName = ItemCatalog.SlotDisplayName(slot),
                GemLines = gemLines,
                GemSummary = gemSummary,
                Desc = string.IsNullOrEmpty(def.Description) ? null : def.Description,
            };
        }

        /// <summary>按服务端权威装备快照刷新 13 个浮动槽的三态与品质色。</summary>
        private void RefreshEquipSlots()
        {
            if (_slots == null) return;
            var service = PlayerInventoryService.Instance;
            for (int i = 0; i < _slots.Length; i++)
            {
                var slot = SlotClientIds[i];
                if (service.TryGetEquipped(slot, out var entry) && entry.TemplateId > 0)
                {
                    var def = ItemCatalog.Resolve(entry.TemplateId);
                    _slots[i].SetContent(InkEquipSlot.SlotState.Equipped,
                        InkWashTheme.QualityColor(ToInkQuality(def.Quality)), def.Glyph);
                }
                else
                {
                    _slots[i].SetContent(InkEquipSlot.SlotState.Empty,
                        InkWashTheme.QualityColor(InkWashTheme.InkQuality.Common), _slotBaseGlyphs[i]);
                }
            }
        }

        private void OnAppearanceTabClicked(int index)
        {
            if (_appTabs == null) return;
            for (int i = 0; i < _appTabs.Length; i++)
                _appTabs[i].IsActive = (i == index);
        }

        // ===================================================================
        // 背包区块（合并自 InventoryPage）
        // ===================================================================

        /// <summary>
        /// 从服务端权威背包快照构建 8x5 格子数据（超过 40 件先截断展示；筛选按物品大类）。
        /// </summary>
        private void InitInvCellData()
        {
            const int cols = 8, rows = 5;
            _invCellData = new CellData[cols * rows];
            for (int i = 0; i < _invCellData.Length; i++)
                _invCellData[i] = new CellData { Quality = InkWashTheme.InkQuality.Common, HasItem = false };

            var service = PlayerInventoryService.Instance;
            int placed = 0;
            foreach (var item in service.Items)
            {
                if (placed >= _invCellData.Length) break;
                if (item.TemplateId <= 0 || item.Count <= 0) continue;
                if (!PassesFilter(item.TemplateId)) continue;

                var def = ItemCatalog.Resolve(item.TemplateId);
                string name = !string.IsNullOrEmpty(item.Name) ? item.Name : def.Name;
                // 可堆叠类（丹药/材料）始终显示数量角标——包括 1：否则 2→1 时角标消失，
                // 看起来像"使用没扣数"。装备单件不显示。
                bool stackable = def.Kind == ItemKind.Consumable || def.Kind == ItemKind.Material;
                _invCellData[placed++] = new CellData
                {
                    Quality = ToInkQuality(def.Quality),
                    Glyph = def.Glyph,
                    Enhance = null,
                    Qty = (stackable || item.Count > 1) ? item.Count.ToString() : null,
                    IsQuest = def.Kind == ItemKind.Quest,
                    HasItem = true,
                    Name = name,
                    Type = def.TypeName,
                    TemplateId = item.TemplateId,
                    ServerItemId = item.ItemId,
                    Kind = def.Kind,
                };
            }
        }

        /// <summary>当前筛选片是否放行该模板物品（索引对应 BuildInventoryCard 的 filters 数组）。</summary>
        private bool PassesFilter(int templateId)
        {
            if (_invFilterIndex == 0) return true;
            var def = ItemCatalog.Resolve(templateId);
            bool handWeapon = def.Kind == ItemKind.Equipment &&
                              (def.EquipSlot == ClientSlot.RightHand || def.EquipSlot == ClientSlot.LeftHand);
            switch (_invFilterIndex)
            {
                case 1: return handWeapon;
                case 2: return def.Kind == ItemKind.Equipment && !handWeapon;
                case 3: return def.Kind == ItemKind.Consumable;
                case 4: return def.Kind == ItemKind.Material;
                case 5: return def.Kind == ItemKind.Quest;
                default: return true;
            }
        }

        /// <summary>行囊卡片：搜索/分类筛选 + 8x5 物品网格（品质六态）+ 容量页脚 + 货币栏。</summary>
        private ContainerControl BuildInventoryCard()
        {
            const float cellSize = 64f;
            const float cellGap = 12f;
            const int cols = 8;
            const int rows = 5;
            float gridW = cellSize * cols + cellGap * (cols - 1);   // 596
            float gridH = cellSize * rows + cellGap * (rows - 1);   // 368
            const float footerH = 44f;
            const float currencyH = 40f;

            InitInvCellData();

            float rowY = CardPad + 26f;
            float gridBoxH = gridH + 12f + footerH;
            float currencyY = rowY + 30f + 12f + gridBoxH + 12f;
            float cardH = currencyY + currencyH + CardPad;
            var card = MakeCard(cardH);
            AddCardHeader(card, "行囊", "INVENTORY", CardPad);

            // 搜索框 + 分类筛选片（整行随卡片拉伸）
            var filterRow = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(CardPad, rowY),
                Size = new Float2(600f, 30f),
                BackgroundColor = Color.Transparent,
            };
            _stretchRows.Add(filterRow);
            card.AddChild(filterRow);

            var searchBox = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 1f),
                Size = new Float2(220f, 28f),
                BackgroundColor = Color.Transparent,
            };
            filterRow.AddChild(searchBox);
            searchBox.AddChild(new InvRoundedBox(InkWashTheme.BaseTertiary, InkWashTheme.BorderNeutralL2, 4f));
            searchBox.AddChild(MakeLabel("🔍 搜索物品...", 10f, 0f, 200f, 28f,
                InkWashTheme.TextTertiary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));

            string[] filters = { "全部", "武器", "防具", "丹药", "材料", "任务" };
            _invFilterChips = new InvFilterChip[filters.Length];
            float chipX = 220f + 16f;
            for (int i = 0; i < filters.Length; i++)
            {
                var chip = new InvFilterChip(filters[i], i == 0)
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(chipX, 3f),
                    Size = new Float2(52f, 24f),
                };
                int captured = i;
                chip.Clicked += () => OnInvFilterClicked(captured);
                _invFilterChips[i] = chip;
                filterRow.AddChild(chip);
                chipX += 52f + 8f;
            }

            // 网格容器（596 宽，水平居中）：8x5 格子 + 容量页脚
            _invGridBox = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(CardPad, rowY + 30f + 12f),
                Size = new Float2(gridW, gridBoxH),
                BackgroundColor = Color.Transparent,
            };
            _centeredControls.Add(_invGridBox);
            card.AddChild(_invGridBox);

            _invCells = new InvCell[cols * rows];
            for (int i = 0; i < _invCells.Length; i++)
            {
                int col = i % cols;
                int row = i / cols;
                var cell = new InvCell(_invCellData[i])
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(col * (cellSize + cellGap), row * (cellSize + cellGap)),
                    Size = new Float2(cellSize, cellSize),
                };
                int captured = i;
                cell.Clicked += () => OnInvCellClicked(captured);
                _invCells[i] = cell;
                _invGridBox.AddChild(cell);
            }

            // 容量页脚：格子 + 15/80 + 容量条
            float footerY = gridH + 12f;
            var footer = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, footerY),
                Size = new Float2(gridW, footerH),
                BackgroundColor = Color.Transparent,
            };
            _invGridBox.AddChild(footer);
            footer.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(gridW, 1f),
                BackgroundColor = InkWashTheme.BorderFaint,
            });
            footer.AddChild(MakeLabel("格子", 12f, 0f, 40f, footerH,
                InkWashTheme.TextSecondary, 13f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            _invCountLabel = MakeLabel("0/50", 56f, 0f, 60f, footerH,
                InkWashTheme.PaperBright, 14f, InkWashTheme.FontRole.Number, TextAlignment.Near);
            footer.AddChild(_invCountLabel);
            var capTrack = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(126f, (footerH - 4f) * 0.5f),
                Size = new Float2(gridW - 126f - 12f, 4f),
                BackgroundColor = InkWashTheme.BorderFaint,
            };
            footer.AddChild(capTrack);
            _invCapFill = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2((gridW - 138f) * 0.1875f, 4f),
                BackgroundColor = InkWashTheme.GoldPrimary,
            };
            capTrack.AddChild(_invCapFill);

            // 货币栏：铜币 / 银两 / 金锭 / 元宝（与网格等宽、居中）
            var currencyRow = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(CardPad, currencyY),
                Size = new Float2(gridW, currencyH),
                BackgroundColor = Color.Transparent,
            };
            _centeredControls.Add(currencyRow);
            card.AddChild(currencyRow);
            currencyRow.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(gridW, 1f),
                BackgroundColor = InkWashTheme.BorderGoldSubtle,
            });
            var currDefs = new (string glyph, string label, string val, Color iconColor)[]
            {
                ("铜", "铜币", "12,450", InkWashTheme.Alert),
                ("银", "银两", "3,200",  InkWashTheme.TextSecondary),
                ("金", "金锭", "85",     InkWashTheme.GoldPrimary),
                ("宝", "元宝", "12",     InkWashTheme.GoldBright),
            };
            float itemW = (gridW - 3f) / 4f;
            float currX = 0f;
            for (int i = 0; i < currDefs.Length; i++)
            {
                var d = currDefs[i];
                currencyRow.AddChild(MakeLabel(d.glyph, currX, 0f, 20f, currencyH,
                    d.iconColor, 15f, InkWashTheme.FontRole.Body, TextAlignment.Center));
                currencyRow.AddChild(MakeLabel(d.label, currX + 24f, 0f, 40f, currencyH,
                    InkWashTheme.TextSecondary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));
                currencyRow.AddChild(MakeLabel(d.val, currX + 66f, 0f, itemW - 70f, currencyH,
                    InkWashTheme.PaperBright, 14f, InkWashTheme.FontRole.Number, TextAlignment.Far));
                currX += itemW;
                if (i < currDefs.Length - 1)
                {
                    currencyRow.AddChild(new ContainerControl
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(currX, (currencyH - 24f) * 0.5f),
                        Size = new Float2(1f, 24f),
                        BackgroundColor = WithAlpha(InkWashTheme.GoldPrimary, 0.12f),
                    });
                    currX += 1f;
                }
            }

            return card;
        }

        /// <summary>发丝线（BorderFaint）。</summary>
        private static ContainerControl MakeFaintHairline(float x, float y, float w)
        {
            return new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(w, 1f),
                BackgroundColor = InkWashTheme.BorderFaint,
            };
        }

        /// <summary>背包元信息行：标签（12px 弱色）+ 值（13px 数字）。</summary>
        private void AddInvMetaRow(ContainerControl parent, float x, float y, float w, string label, out Label val)
        {
            parent.AddChild(MakeLabel(label, x, y, w * 0.4f, 22f,
                InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            val = MakeLabel("", x + w * 0.4f, y, w * 0.6f, 22f,
                InkWashTheme.PaperBright, 13f, InkWashTheme.FontRole.Number, TextAlignment.Far);
            parent.AddChild(val);
        }

        /// <summary>背包区块标题（14px 楷书金色 + 下划发丝线）。</summary>
        private ContainerControl MakeInvSectionTitle(string title, float x, float y, float w)
        {
            var c = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(w, 24f),
                BackgroundColor = Color.Transparent,
            };
            c.AddChild(MakeLabel(title, 0f, 0f, 120f, 20f,
                InkWashTheme.GoldPrimary, 14f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            c.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 23f),
                Size = new Float2(w, 1f),
                BackgroundColor = InkWashTheme.BorderFaint,
            });
            return c;
        }

        /// <summary>物品详情卡片（合并自 InventoryPage）：大图标 + 名称标签 + 元信息 + 属性 + 套装 + 宝石槽 + 操作。</summary>
        private ContainerControl BuildItemDetailCard()
        {
            const float detailW = 460f;
            const float pad = 20f;
            float innerW = detailW - pad * 2f;  // 420
            float cx = pad;
            const float contentH = 744f;
            float cardH = CardPad + 26f + contentH + CardPad;
            var card = MakeCard(cardH);
            AddCardHeader(card, "物品详情", "ITEM DETAIL", CardPad);

            var detail = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(CardPad, CardPad + 26f),
                Size = new Float2(detailW, contentH),
                BackgroundColor = Color.Transparent,
            };
            _centeredControls.Add(detail);
            card.AddChild(detail);
            detail.AddChild(new InvRoundedBox(
                WithAlpha(InkWashTheme.Void, 0.6f),
                WithAlpha(InkWashTheme.GoldPrimary, 0.12f),
                8f));

            float dy = pad;
            // 大图标 120x120（径向渐变金辉 + 金边）
            _invDetailIcon = new DetailIconBox("剑", InkWashTheme.QualityLegendary)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx + (innerW - 120f) * 0.5f, dy),
                Size = new Float2(120f, 120f),
            };
            detail.AddChild(_invDetailIcon);
            dy += 120f + 2f + 14f;

            // 名称 24px 楷书（品质色）
            _invDetailName = MakeLabel("玄铁剑", cx, dy, innerW, 30f,
                InkWashTheme.QualityLegendary, 24f, InkWashTheme.FontRole.Display, TextAlignment.Center);
            detail.AddChild(_invDetailName);
            dy += 30f + 8f;

            // 标签行：传说 / 武器 / 数量
            float tagW = 56f;
            float qtyW = 72f;
            float tagsTotal = tagW * 2f + qtyW + 6f * 2f;
            float tagX = cx + (innerW - tagsTotal) * 0.5f;
            _invTagQuality = new InvTagBox("传说",
                WithAlpha(InkWashTheme.GoldPrimary, 0.12f), InkWashTheme.QualityLegendary,
                WithAlpha(InkWashTheme.GoldPrimary, 0.25f))
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(tagX, dy),
                Size = new Float2(tagW, 22f),
            };
            detail.AddChild(_invTagQuality);
            _invTagType = new InvTagBox("武器",
                WithAlpha(InkWashTheme.GoldPrimary, 0.08f), InkWashTheme.TextSecondary,
                WithAlpha(InkWashTheme.GoldPrimary, 0.15f))
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(tagX + tagW + 6f, dy),
                Size = new Float2(tagW, 22f),
            };
            detail.AddChild(_invTagType);
            _invTagQty = new InvTagBox("数量 1",
                WithAlpha(InkWashTheme.GoldPrimary, 0.08f), InkWashTheme.TextSecondary,
                WithAlpha(InkWashTheme.GoldPrimary, 0.15f))
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(tagX + (tagW + 6f) * 2f, dy),
                Size = new Float2(qtyW, 22f),
            };
            detail.AddChild(_invTagQty);
            dy += 22f + 14f;

            // 元信息 2x2（强化/五行/绑定/耐久），上下发丝线
            detail.AddChild(MakeFaintHairline(cx, dy, innerW));
            dy += 1f + 12f;
            float halfW = innerW * 0.5f - 10f;
            AddInvMetaRow(detail, cx, dy, halfW, "强化", out _invMetaEnhance);
            _invMetaEnhance.Text = "+12";
            _invMetaEnhance.TextColor = InkWashTheme.GoldPrimary;
            AddInvMetaRow(detail, cx + halfW + 20f, dy, halfW, "五行", out _invMetaElement);
            _invMetaElement.Text = "金";
            _invMetaElement.TextColor = InkWashTheme.ElementMetal;
            dy += 22f + 8f;
            AddInvMetaRow(detail, cx, dy, halfW, "绑定", out _invMetaBind);
            _invMetaBind.Text = "拾取绑定";
            AddInvMetaRow(detail, cx + halfW + 20f, dy, halfW, "耐久", out _invMetaDura);
            _invMetaDura.Text = "850/1000";
            dy += 22f + 12f;
            detail.AddChild(MakeFaintHairline(cx, dy, innerW));
            dy += 1f + 10f;

            // 耐久度条 4px（85%）
            var duraTrack = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx + 2f, dy),
                Size = new Float2(innerW - 4f, 4f),
                BackgroundColor = InkWashTheme.BorderFaint,
            };
            detail.AddChild(duraTrack);
            _invDuraFill = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(0f, 4f),
                BackgroundColor = InkWashTheme.GoldPrimary,
            };
            duraTrack.AddChild(_invDuraFill);
            dy += 4f + 14f;

            // 基础属性 / 说明（动态内容，切换选中物品时由 PopulateInvDetail 重建）
            detail.AddChild(MakeInvSectionTitle("基础属性", cx, dy, innerW));
            dy += 20f + 4f + 8f;
            _invBaseAttrHost = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx, dy),
                Size = new Float2(innerW, 2 * 26f + 8f),
                BackgroundColor = Color.Transparent,
            };
            detail.AddChild(_invBaseAttrHost);
            dy += _invBaseAttrHost.Height;

            detail.AddChild(MakeInvSectionTitle("说明", cx, dy, innerW));
            dy += 20f + 4f + 8f;
            _invDescHost = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx, dy),
                Size = new Float2(innerW, 44f),
                BackgroundColor = Color.Transparent,
            };
            detail.AddChild(_invDescHost);
            dy += 44f + 8f;

            // 套装效果
            detail.AddChild(MakeInvSectionTitle("套装效果", cx, dy, innerW));
            dy += 20f + 4f + 8f;
            var setBox = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx, dy),
                Size = new Float2(innerW, 60f),
                BackgroundColor = Color.Transparent,
            };
            detail.AddChild(setBox);
            setBox.AddChild(new InvRoundedBox(
                WithAlpha(InkWashTheme.GoldPrimary, 0.06f),
                WithAlpha(InkWashTheme.GoldPrimary, 0.12f),
                4f));
            setBox.AddChild(MakeLabel("◆", 12f, 8f, 14f, 18f,
                InkWashTheme.GoldPrimary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            _invSetLabel = MakeLabel("暂无套装", 30f, 8f, innerW - 42f, 18f,
                InkWashTheme.GoldPrimary, 13f, InkWashTheme.FontRole.Body, TextAlignment.Near);
            setBox.AddChild(_invSetLabel);
            _invSetDescLabel = MakeLabel("", 12f, 32f, innerW - 24f, 18f,
                InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near);
            setBox.AddChild(_invSetDescLabel);
            dy += 60f + 12f;

            // 宝石槽（动态：孔数=装备品质规则，内容=服务端权威 ItemInfo.Gems）
            _invGemTitle = MakeInvSectionTitle("宝石槽", cx, dy, innerW);
            detail.AddChild(_invGemTitle);
            dy += 20f + 4f + 8f;
            _invGemRowHost = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx, dy),
                Size = new Float2(200f, 32f),
                BackgroundColor = Color.Transparent,
            };
            detail.AddChild(_invGemRowHost);
            dy += 32f + 12f;

            // 操作按钮行（使用/装备/丢弃）
            detail.AddChild(MakeFaintHairline(cx, dy, innerW));
            dy += 1f + 12f;
            float btnW = (innerW - 24f) / 3f;
            _invUseBtn = new InkButton
            {
                Variant = InkButtonVariant.Brand,
                ButtonSize = InkButtonSize.Lg,
                Text = "使用",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx, dy),
                Size = new Float2(btnW, 36f),
                Enabled = false,
            };
            _invUseBtn.ButtonClicked += (b) => OnUseSelected();
            detail.AddChild(_invUseBtn);
            _invEquipBtn = new InkButton
            {
                Variant = InkButtonVariant.Secondary,
                ButtonSize = InkButtonSize.Lg,
                Text = "装备",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx + btnW + 12f, dy),
                Size = new Float2(btnW, 36f),
                Enabled = false,
            };
            _invEquipBtn.ButtonClicked += (b) => OnEquipSelected();
            detail.AddChild(_invEquipBtn);
            _invDropBtn = new InkButton
            {
                Variant = InkButtonVariant.Danger,
                ButtonSize = InkButtonSize.Lg,
                Text = "丢弃",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx + (btnW + 12f) * 2f, dy),
                Size = new Float2(btnW, 36f),
                Enabled = false,
            };
            // 丢弃需要网关新增 ItemDiscard 路由（服务端消息已定义但未接入），暂不开放。
            _invDropBtn.ButtonClicked += (b) => SetInvStatus("丢弃功能暂未开放", true);
            detail.AddChild(_invDropBtn);

            // 操作状态行（使用/装备/卸下的服务端回执显示在这里）
            _invStatusLabel = MakeLabel("", cx, dy + 36f + 4f, innerW, 18f,
                InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Center);
            detail.AddChild(_invStatusLabel);

            return card;
        }

        // ===================================================================
        // 背包交互
        // ===================================================================

        private void OnInvFilterClicked(int index)
        {
            if (_invFilterChips == null) return;
            for (int i = 0; i < _invFilterChips.Length; i++)
                _invFilterChips[i].IsActive = (i == index);
            _invFilterIndex = index;
            _invSelected = -1;
            RefreshInventoryView();
        }

        /// <summary>构建物品详情弹窗浮层：全屏半透明遮罩（点击关闭）+ 详情卡 + 右上角关闭钮。</summary>
        private void BuildItemDetailPopup()
        {
            _detailOverlay = new ContainerControl
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                Visible = false,
                AutoFocus = false,
            };
            AddChild(_detailOverlay);

            var backdrop = new DetailBackdrop();
            backdrop.AnchorPreset = AnchorPresets.StretchAll;
            backdrop.Offsets = Margin.Zero;
            backdrop.Clicked += HideItemDetailPopup;
            _detailOverlay.AddChild(backdrop);

            if (_itemDetailCard != null)
            {
                // 弹窗宽度固定（不再随右栏），位置由 ShowItemDetailPopup 居中计算
                _itemDetailCard.Size = new Float2(520f, _itemDetailCard.Height);

                // 右上角关闭按钮（TopRight 锚点，与卡片宽度解耦）
                var closeButton = new InkButton
                {
                    Variant = InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Sm,
                    Text = "×",
                    AnchorPreset = AnchorPresets.TopRight,
                    Location = new Float2(-12f, 10f),
                    Size = new Float2(30f, 30f),
                };
                closeButton.Clicked += HideItemDetailPopup;
                _itemDetailCard.AddChild(closeButton);

                _detailOverlay.AddChild(_itemDetailCard);
            }
        }

        /// <summary>显示物品详情弹窗并居中（以当前页面尺寸计算）。</summary>
        private void ShowItemDetailPopup()
        {
            if (_detailOverlay == null || _itemDetailCard == null) return;
            _detailOverlay.Visible = true;

            float pw = Width > 0f ? Width : 1920f;
            float ph = Height > 0f ? Height : 1080f;
            _itemDetailCard.Location = new Float2(
                (pw - _itemDetailCard.Width) * 0.5f,
                Mathf.Max((ph - _itemDetailCard.Height) * 0.5f, 12f));
        }

        /// <summary>隐藏物品详情弹窗。</summary>
        private void HideItemDetailPopup()
        {
            if (_detailOverlay != null)
                _detailOverlay.Visible = false;
        }

        /// <summary>弹窗遮罩：半透明暗底，点击任意处关闭。</summary>
        private class DetailBackdrop : ContainerControl
        {
            public event Action Clicked;

            public DetailBackdrop()
            {
                BackgroundColor = WithAlpha(InkWashTheme.Void, 0.55f);
                ClipChildren = false;
            }

            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                try
                {
                    Clicked?.Invoke();
                }
                catch { }
                return true;
            }
        }

        private void OnInvCellClicked(int index)
        {
            if (_invCellData != null && index >= 0 && index < _invCellData.Length && _invCellData[index].HasItem)
                SelectInvCell(index);
        }

        private void SelectInvCell(int index)
        {
            ApplyInvSelection(index);
            // 物品详情以弹窗呈现（不固定在界面中）
            ShowItemDetailPopup();
        }

        /// <summary>仅应用选中高亮与详情数据填充（不弹出弹窗，供初始化使用）。</summary>
        private void ApplyInvSelection(int index, bool emitGold = true)
        {
            if (_invCells == null) return;
            for (int i = 0; i < _invCells.Length; i++)
                _invCells[i].IsSelected = (i == index);
            _invSelected = index;
            PopulateInvDetail(index);
            if (emitGold && index >= 0 && index < _invCells.Length)
                EmitGoldAtControl(_invCells[index]);
        }

        private void PopulateInvDetail(int index)
        {
            if (_invCellData == null || index < 0 || index >= _invCellData.Length || !_invCellData[index].HasItem)
            {
                UpdateDetailButtons(null);
                return;
            }
            var data = _invCellData[index];
            var def = ItemCatalog.Resolve(data.TemplateId);
            PlayerInventoryService.Instance.TryGetItemByServerId(data.ServerItemId, out var info);

            bool isQuest = data.Kind == ItemKind.Quest;
            string qualityName = isQuest ? "任务" : def.Quality switch
            {
                4 => "传说",
                3 => "史诗",
                2 => "稀有",
                1 => "良好",
                _ => "普通",
            };
            Color qColor = isQuest ? InkWashTheme.Warning : InkWashTheme.QualityColor(data.Quality);
            if (_invDetailName != null)
            {
                _invDetailName.Text = data.Name;
                _invDetailName.TextColor = qColor;
            }
            if (_invTagQuality != null)
            {
                _invTagQuality.SetText(qualityName);
                _invTagQuality.SetColors(WithAlpha(qColor, 0.12f), qColor, WithAlpha(qColor, 0.25f));
            }
            if (_invTagType != null) _invTagType.SetText(data.Type);
            if (_invTagQty != null) _invTagQty.SetText("数量 " + (info != null ? info.Count : 1));
            if (_invDetailIcon != null) _invDetailIcon.SetGlyph(data.Glyph, qColor);
            if (_invMetaEnhance != null)
            {
                _invMetaEnhance.Text = data.Enhance ?? "-";
                _invMetaEnhance.TextColor = data.Enhance != null ? InkWashTheme.GoldPrimary : InkWashTheme.TextSecondary;
            }
            if (_invMetaElement != null)
            {
                _invMetaElement.Text = def.Element ?? "-";
                _invMetaElement.TextColor = def.Element != null ? def.ElementColor : InkWashTheme.TextSecondary;
            }
            if (_invMetaBind != null) _invMetaBind.Text = "未绑定";
            if (_invMetaDura != null) _invMetaDura.Text = "-/-";
            if (_invDuraFill != null) _invDuraFill.Size = new Float2(0f, 4f);
            if (_invSetLabel != null) _invSetLabel.Text = "暂无套装";
            if (_invSetDescLabel != null) _invSetDescLabel.Text = "";

            PopulateInvAttrRows(def);
            PopulateInvDescription(def);
            RebuildGemRow(data, def, info);
            UpdateDetailButtons(def);
            SetInvStatus("", false);
        }

        /// <summary>详情"基础属性"区：按物品定义重建属性行。</summary>
        private void PopulateInvAttrRows(ItemDefinition def)
        {
            if (_invBaseAttrHost == null) return;
            _invBaseAttrHost.DisposeChildren();
            float y = 0f;
            int shown = 0;
            foreach (var stat in def.Stats)
            {
                if (shown >= 2) break;   // 高度按两行预留，多余属性折叠
                _invBaseAttrHost.AddChild(MakeLabel(stat.Label, 0f, y, 140f, 20f,
                    InkWashTheme.TextSecondary, 13f, InkWashTheme.FontRole.Body, TextAlignment.Near));
                _invBaseAttrHost.AddChild(MakeLabel(stat.Value, 140f, y, _invBaseAttrHost.Width - 140f, 20f,
                    InkWashTheme.PaperBright, 14f, InkWashTheme.FontRole.Number, TextAlignment.Far));
                y += 26f;
                shown++;
            }
            if (shown == 0)
                _invBaseAttrHost.AddChild(MakeLabel("无属性加成", 0f, 0f, _invBaseAttrHost.Width, 20f,
                    InkWashTheme.TextTertiary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
        }

        /// <summary>详情"说明"区：物品描述文本（两行截断）。</summary>
        private void PopulateInvDescription(ItemDefinition def)
        {
            if (_invDescHost == null) return;
            _invDescHost.DisposeChildren();
            _invDescHost.AddChild(MakeLabel(
                string.IsNullOrEmpty(def.Description) ? "暂无说明" : def.Description,
                0f, 0f, _invDescHost.Width, 44f,
                InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
        }

        /// <summary>按物品定义启用对应的操作按钮；null（未选中）时全部禁用。</summary>
        private void UpdateDetailButtons(ItemDefinition def)
        {
            if (_invUseBtn != null) _invUseBtn.Enabled = def != null && def.IsUsable;
            if (_invEquipBtn != null) _invEquipBtn.Enabled = def != null && def.IsEquippable;
            if (_invDropBtn != null) _invDropBtn.Enabled = false;   // 丢弃路由未开通
        }

        private void OnUseSelected()
        {
            var item = GetSelectedServerItem();
            if (item == null) return;
            EmitGoldAtControl(_invUseBtn);
            SetInvStatus("使用中…", false);
            _ = PlayerInventoryService.Instance.RequestUseItemAsync(item);
        }

        private void OnEquipSelected()
        {
            var item = GetSelectedServerItem();
            if (item == null) return;
            EmitGoldAtControl(_invEquipBtn);
            SetInvStatus("穿戴中…", false);
            _ = PlayerInventoryService.Instance.RequestEquipAsync(item);
        }

        /// <summary>取当前选中格对应的服务端权威物品条目（快照已变化时给出提示并返回 null）。</summary>
        private Horizon.Game.Message.Network.ItemInfo GetSelectedServerItem()
        {
            if (_invCellData == null || _invSelected < 0 || _invSelected >= _invCellData.Length
                || !_invCellData[_invSelected].HasItem)
                return null;
            var cell = _invCellData[_invSelected];
            if (PlayerInventoryService.Instance.TryGetItemByServerId(cell.ServerItemId, out var info))
                return info;
            SetInvStatus("物品数据已更新，请重新选择", true);
            return null;
        }

        /// <summary>服务端回执：把结果写到详情弹窗状态行（弹窗关闭时仅记日志）。</summary>
        private void OnItemOperationFinished(ItemOperationResult result)
        {
            string text;
            if (result.Operation == ItemOperationKind.GemSocket)
            {
                // 服务端对宝石操作的话术已含语义（宝石已镶嵌/已交换/已卸下/失败原因），直接呈现。
                text = string.IsNullOrEmpty(result.Message) ? "宝石操作完成" : result.Message;
            }
            else
            {
                string verb = result.Operation switch
                {
                    ItemOperationKind.Use => "使用",
                    ItemOperationKind.Equip => "穿戴",
                    _ => "卸下",
                };
                text = result.Success ? verb + "成功" : verb + "失败：" + result.Message;
            }
            FlaxEngine.Debug.Log($"[CharacterPanelPage] {text}");
            if (_detailOverlay != null && _detailOverlay.Visible)
                SetInvStatus(text, !result.Success);
        }

        private void SetInvStatus(string text, bool failed)
        {
            if (_invStatusLabel == null) return;
            _invStatusLabel.Text = text;
            _invStatusLabel.TextColor = failed ? InkWashTheme.Warning : InkWashTheme.TextJade;
        }

        // ===================================================================
        // 宝石镶嵌：槽位渲染 / 选择窗 / 请求
        // ===================================================================

        /// <summary>按选中物品重建宝石行：装备按目录孔数规则显示几颗，内容取服务端权威 <c>ItemInfo.Gems</c>。</summary>
        private void RebuildGemRow(CellData data, ItemDefinition def, Horizon.Game.Message.Network.ItemInfo info)
        {
            if (_invGemRowHost == null) return;
            bool show = data.HasItem && def != null && def.Kind == ItemKind.Equipment && def.GemSlots > 0;
            if (_invGemTitle != null) _invGemTitle.Visible = show;
            _invGemRowHost.Visible = show;
            if (!show) return;

            _invGemRowHost.DisposeChildren();
            var gems = info?.Gems;
            for (int i = 0; i < def.GemSlots; i++)
            {
                int gemTemplate = gems != null && i < gems.Count ? gems[i] : ItemCatalog.EmptyGemSlot;
                bool filled = gemTemplate > ItemCatalog.EmptyGemSlot;
                var gemDef = filled ? ItemCatalog.Resolve(gemTemplate) : null;
                var slot = new GemSlot(
                    filled ? ToInkQuality(gemDef.Quality) : InkWashTheme.InkQuality.Common,
                    filled,
                    filled ? gemDef.Glyph : null)
                {
                    Location = new Float2(i * 38f, 0f),
                };
                int captured = i;
                var capturedCell = data;
                slot.Clicked += () => OnGemSlotClicked(captured, capturedCell);
                _invGemRowHost.AddChild(slot);
            }
        }

        private void OnGemSlotClicked(int slotIndex, CellData cell)
        {
            if (!PlayerInventoryService.Instance.TryGetItemByServerId(cell.ServerItemId, out var info))
            {
                SetInvStatus("物品数据已更新，请重新选择", true);
                return;
            }
            var gems = info.Gems;
            int current = gems != null && slotIndex >= 0 && slotIndex < gems.Count
                ? gems[slotIndex] : ItemCatalog.EmptyGemSlot;
            OpenGemPicker(info.ItemId, slotIndex, current);
        }

        private void BuildGemPickerPopup()
        {
            _gemPickerOverlay = new ContainerControl
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                Visible = false,
                AutoFocus = false,
            };
            AddChild(_gemPickerOverlay);

            var backdrop = new DetailBackdrop();   // 空白处点击 = 关选择窗（不关详情弹窗）
            backdrop.AnchorPreset = AnchorPresets.StretchAll;
            backdrop.Offsets = Margin.Zero;
            backdrop.Clicked += CloseGemPicker;
            _gemPickerOverlay.AddChild(backdrop);

            _gemPickerPanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(360f, 380f),
            };
            _gemPickerPanel.AddChild(new InvRoundedBox(
                WithAlpha(InkWashTheme.Void, 0.94f), InkWashTheme.BorderGoldSubtle, 6f));
            _gemPickerTitle = MakeLabel("选择宝石", 16f, 10f, 280f, 26f,
                InkWashTheme.GoldPrimary, 15f, InkWashTheme.FontRole.Display, TextAlignment.Near);
            _gemPickerPanel.AddChild(_gemPickerTitle);

            var closeBtn = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "×",
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(-40f, 8f),
                Size = new Float2(30f, 30f),
            };
            closeBtn.Clicked += CloseGemPicker;
            _gemPickerPanel.AddChild(closeBtn);

            _gemPickerRows = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 46f),
                Size = new Float2(328f, 282f),
                BackgroundColor = Color.Transparent,
            };
            _gemPickerPanel.AddChild(_gemPickerRows);

            var cancelBtn = new InkButton
            {
                Variant = InkButtonVariant.Secondary,
                ButtonSize = InkButtonSize.Md,
                Text = "取消",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((360f - 120f) * 0.5f, 380f - 44f),
                Size = new Float2(120f, 32f),
            };
            cancelBtn.Clicked += CloseGemPicker;
            _gemPickerPanel.AddChild(cancelBtn);

            _gemPickerOverlay.AddChild(_gemPickerPanel);
        }

        /// <summary>打开宝石选择窗：列出背包内全部宝石（选新宝石=交换，旧宝石回背包可再用），槽内已有宝石时提供"卸下"。</summary>
        private void OpenGemPicker(long equipItemId, int slotIndex, int currentTemplate)
        {
            if (_gemPickerRows == null) return;
            _gemPickerEquipItemId = equipItemId;
            _gemPickerSlot = slotIndex;
            _gemPickerCurrentTemplate = currentTemplate;

            _gemPickerRows.DisposeChildren();
            const float rowH = 40f;
            const float rowGap = 6f;
            float y = 0f;

            var counts = new Dictionary<int, int>();
            foreach (var item in PlayerInventoryService.Instance.Items)
            {
                var d = ItemCatalog.Resolve(item.TemplateId);
                if (d.Kind != ItemKind.Gem || item.Count <= 0) continue;
                counts.TryGetValue(item.TemplateId, out var c);
                counts[item.TemplateId] = c + item.Count;
            }

            if (counts.Count == 0)
            {
                _gemPickerRows.AddChild(MakeLabel("背包中没有宝石", 4f, y, 320f, rowH,
                    InkWashTheme.TextTertiary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Center));
                y += rowH + rowGap;
            }
            else
            {
                foreach (var kv in counts)
                {
                    int templateId = kv.Key;
                    var d = ItemCatalog.Resolve(templateId);
                    var btn = new InkButton
                    {
                        Variant = templateId == currentTemplate ? InkButtonVariant.Brand : InkButtonVariant.Secondary,
                        ButtonSize = InkButtonSize.Md,
                        Text = $"{d.Glyph} {d.Name} ×{kv.Value}",
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(0f, y),
                        Size = new Float2(328f, rowH),
                    };
                    int captured = templateId;
                    btn.ButtonClicked += (b) => RequestGemSocket(captured);
                    _gemPickerRows.AddChild(btn);
                    y += rowH + rowGap;
                }
            }

            if (currentTemplate > ItemCatalog.EmptyGemSlot)
            {
                var cur = ItemCatalog.Resolve(currentTemplate);
                var removeBtn = new InkButton
                {
                    Variant = InkButtonVariant.Danger,
                    ButtonSize = InkButtonSize.Md,
                    Text = $"卸下 {cur.Name}",
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, y),
                    Size = new Float2(328f, rowH),
                };
                removeBtn.ButtonClicked += (b) => RequestGemSocket(0);   // 协议约定 0 = 卸下该槽
                _gemPickerRows.AddChild(removeBtn);
            }

            CenterGemPicker();
            _gemPickerOverlay.Visible = true;
        }

        private void RequestGemSocket(int gemTemplateId)
        {
            CloseGemPicker();
            SetInvStatus(gemTemplateId == 0 ? "卸下中…" : "镶嵌中…", false);
            _ = PlayerInventoryService.Instance.RequestSocketGemAsync(_gemPickerEquipItemId, _gemPickerSlot, gemTemplateId);
        }

        private void CenterGemPicker()
        {
            if (_gemPickerPanel == null) return;
            float pw = Width > 0f ? Width : 1920f;
            float ph = Height > 0f ? Height : 1080f;
            _gemPickerPanel.Location = new Float2(
                (pw - _gemPickerPanel.Width) * 0.5f,
                Mathf.Max((ph - _gemPickerPanel.Height) * 0.5f, 12f));
        }

        private void CloseGemPicker()
        {
            if (_gemPickerOverlay != null) _gemPickerOverlay.Visible = false;
        }

        /// <summary>
        /// 权威快照变化后的整体刷新：重建格子数据→同步到控件→容量/装备槽→保持选中。
        /// 由 <c>PlayerInventoryService.Changed</c> 事件驱动（已在主线程）。
        /// </summary>
        private void RefreshInventoryView()
        {
            try
            {
                if (_invCells == null || _invCellData == null) return;

                long previousSelectedId = _invSelected >= 0 && _invSelected < _invCellData.Length
                    ? _invCellData[_invSelected].ServerItemId : 0;

                InitInvCellData();
                for (int i = 0; i < _invCells.Length; i++)
                    _invCells[i].SetData(_invCellData[i]);
                UpdateCapacityFooter();
                RefreshEquipSlots();
                // 穿戴快照驱动的 Changed 事件里，装备加成已由桥写入组件（ApplyEquipmentSnapshot
                // 先重算后发事件），此处把属性卡/五行雷达同步到新值。
                RefreshAttributeCards();

                int keep = -1;
                if (previousSelectedId != 0)
                {
                    for (int i = 0; i < _invCellData.Length; i++)
                    {
                        if (_invCellData[i].HasItem && _invCellData[i].ServerItemId == previousSelectedId)
                        {
                            keep = i;
                            break;
                        }
                    }
                }
                if (keep >= 0)
                    ApplyInvSelection(keep, emitGold: false);
                else
                {
                    for (int i = 0; i < _invCells.Length; i++)
                        _invCells[i].IsSelected = false;
                    _invSelected = -1;
                    UpdateDetailButtons(null);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CharacterPanelPage] RefreshInventoryView: {ex.Message}");
            }
        }

        private void UpdateCapacityFooter()
        {
            var service = PlayerInventoryService.Instance;
            int count = service.Items.Count;
            if (_invCountLabel != null)
                _invCountLabel.Text = $"{count}/{service.Capacity}";
            if (_invCapFill != null && _invCapFill.Parent != null)
            {
                float frac = service.Capacity > 0 ? Mathf.Clamp((float)count / service.Capacity, 0f, 1f) : 0f;
                _invCapFill.Size = new Float2(_invCapFill.Parent.Width * frac, 4f);
            }
        }

        /// <summary>在指定控件中心发射金粉粒子（屏幕坐标转换）。</summary>
        private void EmitGoldAtControl(Control control)
        {
            if (_particleSystem == null || control == null) return;
            try
            {
                var center = new Float2(control.Width * 0.5f, control.Height * 0.5f);
                var screenPos = control.PointToScreen(center);
                var localPos = _particleSystem.PointFromScreen(screenPos);
                _particleSystem.EmitGoldBurst(localPos, count: 8, isLarge: false);
            }
            catch { }
        }

        // ===================================================================
        // 卡片构建（8 个，对应设计方案右侧属性面板）
        // ===================================================================

        /// <summary>1. 属性总览：五维属性五边形雷达图 + 五行属性五边形雷达图与元素图例（左右并排）。</summary>
        private ContainerControl BuildAttrOverviewCard()
        {
            // 并排布局：左列五维 / 右列五行，各 200 宽雷达 + 30 间距 = 430 宽内容行，整体水平居中
            const float radarS = 200f;
            const float colGap = 30f;
            const float col2X = radarS + colGap;          // 230
            const float rowW = col2X + radarS;            // 430
            const float rowH = 256f;                      // 标题24 + 雷达200 + 间距10 + 图例16 + 余量6
            float secY = CardPad + 26f;                   // 40
            var card = MakeCard(secY + rowH + CardPad);   // 40 + 256 + 14 = 310
            AddCardHeader(card, "属性总览", "ATTRIBUTES", CardPad);

            var row = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, secY),
                Size = new Float2(rowW, rowH),
                BackgroundColor = Color.Transparent,
            };
            _centeredControls.Add(row);
            card.AddChild(row);

            // ── 左列：五维属性（五边形雷达图） ──
            row.AddChild(MakeLabel("五维属性", 0f, 0f, 120f, 20f,
                InkWashTheme.GoldPrimary, 14f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            row.AddChild(MakeFaintHairline(0f, 23f, radarS));
            // 五维雷达：体/御/劲接组件真实值（RefreshFiveDimRadar 归一），敏/势暂无权威数据源标"-"。
            // 数组引用常驻，InkRadarChart.DrawChart 每帧读取 → 穿戴后无需重建控件。
            _fiveDimValues = new float[] { 0f, 0f, 0f, 0f, 0f };
            _fiveDimTexts = new string[] { "-", "-", "-", "-", "-" };
            var fiveRadar = new InkRadarChart(
                5,
                _fiveDimValues,
                new[] { "体", "御", "敏", "势", "劲" },
                _fiveDimTexts,
                new[]
                {
                    InkWashTheme.GoldBright, InkWashTheme.JadeBright, InkWashTheme.GoldBright,
                    InkWashTheme.JadeBright, InkWashTheme.GoldBright,
                },
                hexMode: false, taiji: false, nameSize: 14f, pad: 32f)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 30f),
                Size = new Float2(radarS, radarS),
            };
            row.AddChild(fiveRadar);

            // ── 右列：五行属性（五边形雷达图） ──
            row.AddChild(MakeLabel("五行属性", col2X, 0f, 120f, 20f,
                InkWashTheme.GoldPrimary, 14f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            row.AddChild(MakeFaintHairline(col2X, 23f, radarS));
            // 五行雷达接组件真值（数组引用常驻，DrawChart 每帧读取 → 穿戴后无需重建控件）。
            _wuxingRadarValues = new float[] { 0f, 0f, 0f, 0f, 0f };
            _wuxingRadarTexts = new string[] { "0", "0", "0", "0", "0" };
            var wuxingRadar = new InkRadarChart(
                5,
                _wuxingRadarValues,
                new[] { "金", "木", "水", "火", "土" },
                _wuxingRadarTexts,
                new[]
                {
                    InkWashTheme.GoldBright, InkWashTheme.GoldBright, InkWashTheme.GoldBright,
                    InkWashTheme.GoldBright, InkWashTheme.GoldBright,
                },
                hexMode: false, taiji: true, nameSize: 14f, pad: 32f)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(col2X, 30f),
                Size = new Float2(radarS, radarS),
            };
            row.AddChild(wuxingRadar);

            // 元素图例：8px 圆点 + 10px 文字，5 项（金木水火土），整行水平居中
            string[] legendNames = { "金", "木", "水", "火", "土" };
            Color[] legendColors =
            {
                InkWashTheme.ElementColor(InkWashTheme.InkElement.Metal),
                InkWashTheme.ElementColor(InkWashTheme.InkElement.Wood),
                InkWashTheme.ElementColor(InkWashTheme.InkElement.Water),
                InkWashTheme.ElementColor(InkWashTheme.InkElement.Fire),
                InkWashTheme.ElementColor(InkWashTheme.InkElement.Earth),
            };
            var legend = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((rowW - 202f) * 0.5f, 30f + radarS + 10f),
                Size = new Float2(202f, 16f),
                BackgroundColor = Color.Transparent,
            };
            float lx = 0f;
            for (int i = 0; i < legendNames.Length; i++)
            {
                legend.AddChild(new InkDot(legendColors[i], 4f) { Location = new Float2(lx, 4f) });
                legend.AddChild(MakeLabel(legendNames[i], lx + 12f, 0f, 22f, 16f,
                    InkWashTheme.TextSecondary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Near));
                lx += 12f + 22f + 8f;
            }
            row.AddChild(legend);
            return card;
        }

        /// <summary>
        /// "属性"合并卡：左栏基础属性（攻/防/暴击率/暴击伤害接组件真值，命中/闪避无源"-"），
        /// 右栏属性加点（力量/敏捷/体质/内力 + 五行九行，每行带完整每点效果说明）。
        /// 属性口径 = 基值 + 装备 + 加点，任何一层变化后由 <see cref="RefreshAttributeCards"/> 统一刷新。
        /// </summary>
        private ContainerControl BuildAttributeCard()
        {
            const float leftRowH = 24f;
            float secY = CardPad + 26f;

            float leftH = 26f + leftRowH * 6f;
            // 右栏两列：标题26 + 汇总24 + 发丝线 + 5行×30 + 操作区38
            float rightH = 26f + 30f + 30f * 5 + 10f + 28f + 6f;
            var card = MakeCard(secY + Mathf.Max(leftH, rightH) + CardPad);
            AddCardHeader(card, "属性", "ATTRIBUTES", CardPad);

            // ── 左栏：基础属性 ──
            _attrLeftCol = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(CardPad, secY),
                Size = new Float2(300f, leftH),
                BackgroundColor = Color.Transparent,
            };
            card.AddChild(_attrLeftCol);
            _attrLeftCol.AddChild(MakeInvSectionTitle("基础属性", 0f, 0f, 290f));

            string[] names = { "攻击力", "防御力", "暴击率", "暴击伤害", "命中率", "闪避率" };
            bool[] goldVal = { false, false, true, true, false, false };
            _baseStatValueLabels = new Label[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                float y = 26f + i * leftRowH;
                _attrLeftCol.AddChild(MakeLabel(names[i], 0f, y, 74f, leftRowH,
                    InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
                _baseStatValueLabels[i] = MakeLabel("-", 78f, y, 210f, leftRowH,
                    goldVal[i] ? InkWashTheme.GoldBright : InkWashTheme.TextDefault,
                    13f, InkWashTheme.FontRole.Number, TextAlignment.Near);
                _attrLeftCol.AddChild(_baseStatValueLabels[i]);
            }

            // ── 右栏：属性加点（两列排布；每点说明收进"？"悬停 Tooltip） ──
            // 全部子控件 TopLeft 锚；位置由 LayoutCards 在父尺寸已知后显式重排。
            _attrRightCol = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(320f, secY),
                Size = new Float2(500f, rightH),
                BackgroundColor = Color.Transparent,
            };
            card.AddChild(_attrRightCol);
            _attrRightCol.AddChild(MakeInvSectionTitle("属性加点", 0f, 0f, 480f));

            _attrRightCol.AddChild(MakeLabel("自由可用点", 0f, 26f, 80f, 22f,
                InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            _allocSummaryLabel = MakeLabel("-", 84f, 26f, 300f, 22f,
                InkWashTheme.GoldPrimary, 13f, InkWashTheme.FontRole.Number, TextAlignment.Near);
            _attrRightCol.AddChild(_allocSummaryLabel);
            _attrRightCol.AddChild(MakeFaintHairline(0f, 50f, 480f));

            const float rowsY = 56f;
            const float rowPitch = 30f;
            const int leftRows = 5;   // 左列：力量/敏捷/体质/内力/金；右列：木/水/火/土

            _allocSpentLabels = new Label[AllocationRowDefs.Length];
            _allocPendingLabels = new Label[AllocationRowDefs.Length];

            _allocColLeft = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, rowsY),
                Size = new Float2(240f, rowPitch * leftRows),
                BackgroundColor = Color.Transparent,
            };
            _attrRightCol.AddChild(_allocColLeft);
            _allocColRight = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(250f, rowsY),
                Size = new Float2(240f, rowPitch * leftRows),
                BackgroundColor = Color.Transparent,
            };
            _attrRightCol.AddChild(_allocColRight);

            for (int i = 0; i < AllocationRowDefs.Length; i++)
            {
                var (label, kind, desc) = AllocationRowDefs[i];
                var parentCol = i < leftRows ? _allocColLeft : _allocColRight;
                float y = (i < leftRows ? i : i - leftRows) * rowPitch;
                BuildAllocRow(parentCol, y, i, label, kind);
            }

            float actionY = rowsY + rowPitch * leftRows + 10f;
            var resetBtn = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                Text = "重置",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, actionY),
                BorderColor = InkWashTheme.BorderGoldSubtle,
                BorderThickness = 1f,
            };
            resetBtn.Height = 28f;
            resetBtn.Width = 72f;
            resetBtn.Clicked += OnResetAllocation;
            _attrRightCol.AddChild(resetBtn);
            _allocResetBtn = resetBtn;

            var confirmBtn = new InkButton
            {
                Variant = InkButtonVariant.Brand,
                Text = "确认加点",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(400f, actionY),
            };
            confirmBtn.Height = 28f;
            confirmBtn.Width = 100f;
            confirmBtn.Clicked += OnConfirmAllocation;
            _attrRightCol.AddChild(confirmBtn);
            _allocConfirmBtn = confirmBtn;

            _attrRightCol.AddChild(MakeLabel($"点池 = 等级 × {CharacterAttributesComponent.AllocationPointsPerLevel}，重置全额返还",
                82f, actionY + 6f, 240f, 18f, InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Near));

            RefreshAllocationRows();
            return card;
        }

        /// <summary>
        /// 单行加点控件：名称 + 已分配 + "？"(悬停说明) ｜ 行尾 － [预览+n] ＋。
        /// 全部 TopLeft 锚 + 设计宽(246)定位；列变宽时由 LayoutCards 把行尾三件套重排到右缘。
        /// </summary>
        private void BuildAllocRow(ContainerControl col, float y, int row, string label,
            CharacterAttributesComponent.AllocationKind kind)
        {
            var rowCtl = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, y),
                Size = new Float2(246f, 28f),
                BackgroundColor = Color.Transparent,
            };
            col.AddChild(rowCtl);
            _allocRows.Add(rowCtl);

            // 行底：极淡墨条（五行行再叠一层元素色 6% 底，增强辨识度）
            rowCtl.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(246f, 28f),
                BackgroundColor = kind >= CharacterAttributesComponent.AllocationKind.Metal
                    ? WithAlpha(InkWashTheme.ElementColor(ElementOf(kind)), 0.05f)
                    : new Color(1f, 1f, 1f, 0.02f),
            });

            rowCtl.AddChild(MakeLabel(label, 8f, 2f, 40f, 18f,
                InkWashTheme.TextDefault, 12f, InkWashTheme.FontRole.Display, TextAlignment.Near));

            _allocSpentLabels[row] = MakeLabel("未分配", 52f, 3f, 82f, 18f,
                InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near);
            rowCtl.AddChild(_allocSpentLabels[row]);

            AllocHelpDot help = null;
            help = new AllocHelpDot(row, r => ShowAllocTooltip(help, r), HideAllocTooltip)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(134f, 6f),
            };
            rowCtl.AddChild(help);

            var minus = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                Text = "－",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(160f, 2f),
                BorderColor = InkWashTheme.BorderGoldSubtle,
                BorderThickness = 1f,
            };
            minus.Height = 22f;
            minus.Width = 24f;
            minus.Clicked += () => OnAllocMinus(row);
            rowCtl.AddChild(minus);
            _allocMinusButtons.Add(minus);

            _allocPendingLabels[row] = MakeLabel("·", 188f, 3f, 30f, 20f,
                InkWashTheme.TextTertiary, 12f, InkWashTheme.FontRole.Number, TextAlignment.Center);
            rowCtl.AddChild(_allocPendingLabels[row]);

            var plus = new InkButton
            {
                Variant = InkButtonVariant.Secondary,
                Text = "＋",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(220f, 2f),
            };
            plus.Height = 22f;
            plus.Width = 24f;
            plus.Clicked += () => OnAllocPlus(row);
            rowCtl.AddChild(plus);
            _allocPlusButtons.Add(plus);
        }

        /// <summary>五行加点行 → 元素色（力量/敏捷等基础行不参与）。</summary>
        private static InkWashTheme.InkElement ElementOf(CharacterAttributesComponent.AllocationKind kind)
        {
            switch (kind)
            {
                case CharacterAttributesComponent.AllocationKind.Metal: return InkWashTheme.InkElement.Metal;
                case CharacterAttributesComponent.AllocationKind.Wood: return InkWashTheme.InkElement.Wood;
                case CharacterAttributesComponent.AllocationKind.Water: return InkWashTheme.InkElement.Water;
                case CharacterAttributesComponent.AllocationKind.Fire: return InkWashTheme.InkElement.Fire;
                default: return InkWashTheme.InkElement.Earth;
            }
        }

        /// <summary>"？"悬停：弹出该属性的每点效果说明（Tooltip 挂页面根，最上层绘制）。</summary>
        private void ShowAllocTooltip(Control anchor, int row)
        {
            if (_allocTooltip == null)
            {
                // 深墨配色：与角色面板暗色底一致（组件默认是纸色 popup 风格）
                _allocTooltip = new InkAttributeTooltip { AnchorPreset = AnchorPresets.TopLeft, DarkTheme = true };
                AddChild(_allocTooltip);
            }
            var def = AllocationRowDefs[row];
            int spent = 0;
            if (_boundCharacter != null)
            {
                var alloc = _boundCharacter.Allocation;
                spent = AllocationSpentOf(in alloc, def.Kind);
            }
            // 未分配时不显示附加行（"当前未分配"是噪声）；已分配才补一行小字。
            string spentText = spent > 0 ? $"已分配 {spent} 点" : string.Empty;
            _allocTooltip.SetData(null, def.Label, def.Desc, spentText, null);
            _allocTooltip.SetQualityBorderColor(null);
            _allocTooltip.Show(anchor.PointToScreen(new Float2(anchor.Width * 0.5f, anchor.Height)));
        }

        private void HideAllocTooltip()
        {
            if (_allocTooltip != null) _allocTooltip.Visible = false;
        }

        /// <summary>4. 战斗属性：战斗力 32px 金色 + 2 列 6 项。</summary>
        private ContainerControl BuildCombatCard()
        {
            var card = MakeCard(204f);
            AddCardHeader(card, "战斗属性", "COMBAT STATS", CardPad);

            // 战斗力（32px DIN 金色）——由 RefreshAttributeCards 按属性合成刷新。
            _combatPowerLabel = MakeLabel("-", CardPad, CardPad + 22f, 200f, 40f,
                InkWashTheme.GoldBright, 32f, InkWashTheme.FontRole.Number, TextAlignment.Near);
            card.AddChild(_combatPowerLabel);
            card.AddChild(MakeLabel("战斗力", CardPad + 128f, CardPad + 42f, 80f, 18f,
                InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));

            var hairline = MakeHairline(CardPad, CardPad + 68f, 2000f);
            _fullWidthHairlines.Add(hairline);
            card.AddChild(hairline);

            string[] names = { "境界", "门派", "阵营", "侠义值", "恶名值", "修炼经验" };
            // 门派取自真实角色数据：创角不再选门派，未加入前显示「无门派」。
            // 境界/修炼经验由 RefreshAttributeCards 写组件真值；阵营/侠义/恶名无数据来源，显示 "-"。
            var ownerRole = global::Game.UI.Character.CharacterManager.Instance?.SelectedCharacter;
            string ownerSect = global::HundunWorld.Game.UI.Ink.Pages.CharacterCreation.CcSectNames.Get(
                ownerRole?.Profession ?? Horizon.Game.Message.Enums.Profession.None);
            string[] vals = { "-", string.IsNullOrEmpty(ownerSect) ? "无门派" : ownerSect, "-", "-", "-", "-" };
            Color[] vcolors =
            {
                InkWashTheme.TextDefault, InkWashTheme.TextDefault, InkWashTheme.JadeBright,
                InkWashTheme.GoldBright, InkWashTheme.TextDefault, InkWashTheme.TextDefault,
            };
            float gy = CardPad + 78f;
            const float rowH = 30f;
            _combatValueLabels = new Label[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int col = i % 2, row = i / 2;
                float x = CardPad + col * 185f;
                float y = gy + row * rowH;
                card.AddChild(MakeLabel(names[i], x, y, 64f, 16f,
                    InkWashTheme.TextSecondary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));
                _combatValueLabels[i] = MakeLabel(vals[i], x + 66f, y, 110f, 16f,
                    vcolors[i], 12f, InkWashTheme.FontRole.Body, TextAlignment.Near);
                card.AddChild(_combatValueLabels[i]);
            }
            return card;
        }

        /// <summary>6. 调律：无相攻击 +120（金色左边框条目）。</summary>
        private ContainerControl BuildTiaoLvCard()
        {
            var card = MakeCard(86f);
            AddCardHeader(card, "调律", "TUNING", CardPadSm);

            var entry = new StatEntry(InkWashTheme.GoldPrimary, "无相攻击", InkWashTheme.TextSecondary, "+120", InkWashTheme.GoldBright, false)
            {
                Location = new Float2(CardPadSm, CardPadSm + 26f),
                Size = new Float2(300f, 34f),
            };
            _stretchRows.Add(entry);
            card.AddChild(entry);
            return card;
        }

        /// <summary>7. 定音：2/2 激活（会心率 +3% / 最大外功 +45，青玉左边框 + 激活标签）。</summary>
        private ContainerControl BuildDingYinCard()
        {
            var card = MakeCard(118f);
            AddCardHeader(card, "定音", "DING YIN", CardPadSm);

            var count = MakeLabel("2/2 激活", 0f, CardPadSm + 2f, 70f, 14f,
                InkWashTheme.JadeBright, 10f, InkWashTheme.FontRole.Number, TextAlignment.Far);
            _rightCountLabels.Add(count);
            card.AddChild(count);

            float ry = CardPadSm + 26f;
            var e1 = new StatEntry(InkWashTheme.JadePrimary, "会心率", InkWashTheme.TextSecondary, "+3%", InkWashTheme.TextDefault, true)
            {
                Location = new Float2(CardPadSm, ry),
                Size = new Float2(300f, 30f),
            };
            _stretchRows.Add(e1);
            card.AddChild(e1);

            var e2 = new StatEntry(InkWashTheme.JadePrimary, "最大外功", InkWashTheme.TextSecondary, "+45", InkWashTheme.TextDefault, true)
            {
                Location = new Float2(CardPadSm, ry + 36f),
                Size = new Float2(300f, 30f),
            };
            _stretchRows.Add(e2);
            card.AddChild(e2);
            return card;
        }

        /// <summary>8. 叠音：3/5 层 + 5 个层进点 + 共鸣效果行。</summary>
        private ContainerControl BuildDieYinCard()
        {
            var card = MakeCard(112f);
            AddCardHeader(card, "叠音", "DIE YIN", CardPadSm);

            var count = MakeLabel("3/5 层", 0f, CardPadSm + 2f, 70f, 14f,
                InkWashTheme.GoldBright, 10f, InkWashTheme.FontRole.Number, TextAlignment.Far);
            _rightCountLabels.Add(count);
            card.AddChild(count);

            // 5 个层进点（3 个激活）
            float dy = CardPadSm + 30f;
            var dotsRow = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(CardPadSm, dy),
                Size = new Float2(140f, 6f),
                BackgroundColor = Color.Transparent,
            };
            for (int i = 0; i < 5; i++)
                dotsRow.AddChild(new LayerDot(i < 3) { Location = new Float2(i * 29f, 0f) });
            card.AddChild(dotsRow);

            // 共鸣效果行（gold-deep 左边框，金色文字）
            var resonance = new StatEntry(InkWashTheme.GoldDeep, "2件套：攻击 +10%", InkWashTheme.GoldBright, "", InkWashTheme.GoldBright, false)
            {
                Location = new Float2(CardPadSm, dy + 16f),
                Size = new Float2(300f, 28f),
            };
            _stretchRows.Add(resonance);
            card.AddChild(resonance);
            return card;
        }

        // ===================================================================
        // 嵌套控件：滚轮滚动区域
        // ===================================================================

        /// <summary>鼠标滚轮驱动的滚动容器：裁剪子内容，滚轮上下滚动。</summary>
        internal class InkScrollArea : ContainerControl
        {
            private readonly ContainerControl _content;

            public InkScrollArea(ContainerControl content)
            {
                _content = content;
                ClipChildren = true;
                BackgroundColor = Color.Transparent;
                AutoFocus = false;
            }

            public override bool OnMouseWheel(Float2 location, float delta)
            {
                float minY = Mathf.Min(0f, Height - _content.Height);
                float newY = Mathf.Clamp(_content.Location.Y + delta * 48f, minY, 0f);
                _content.Location = new Float2(_content.Location.X, newY);
                return true;
            }
        }

        // ===================================================================
        // 嵌套控件：圆角卡片
        // ===================================================================

        /// <summary>ds-card 风格圆角卡片容器（自绘圆角背景 + 边框）。</summary>
        internal class InkCard : ContainerControl
        {
            public InkCard()
            {
                AnchorPreset = AnchorPresets.TopLeft;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;
            }

            public override void Draw()
            {
                var rect = new Rectangle(Float2.Zero, Size);
                InkRenderHelper.FillRoundedRectangle(rect, 8f,
                    new Color(InkWashTheme.BaseSecondary.R, InkWashTheme.BaseSecondary.G, InkWashTheme.BaseSecondary.B, 0.85f));
                InkRenderHelper.DrawRoundedRectangle(rect, 8f, InkWashTheme.BorderNeutralL1, 1f);
                base.Draw();
            }
        }

        // ===================================================================
        // 嵌套控件：自绘雷达图（五边形 / 六边形）
        // ===================================================================

        /// <summary>
        /// 水墨风格雷达图。网格 5 层 + 轴线 + 数据多边形（青玉填充 + 鎏金描边）+ 顶点 + 标签。
        /// hexMode=true 时仅顶部顶点数值在名称上方，其余在下方；否则数值总在名称上方。
        /// </summary>
        internal class InkRadarChart : Control
        {
            private readonly int _sides;
            private readonly float[] _values;
            private readonly string[] _names;
            private readonly string[] _valTexts;
            private readonly Color[] _valColors;
            private readonly bool _hexMode;
            private readonly bool _taiji;
            private readonly float _nameSize;
            private readonly float _pad;

            public InkRadarChart(int sides, float[] values, string[] names, string[] valTexts,
                Color[] valColors, bool hexMode, bool taiji, float nameSize, float pad)
            {
                _sides = sides;
                _values = values;
                _names = names;
                _valTexts = valTexts;
                _valColors = valColors;
                _hexMode = hexMode;
                _taiji = taiji;
                _nameSize = nameSize;
                _pad = pad;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                try
                {
                    DrawChart();
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[InkRadarChart] Draw: {ex.Message}");
                }
            }

            private void DrawChart()
            {
                Float2 c = new Float2(Width * 0.5f, Height * 0.5f);
                float r = Mathf.Min(Width, Height) * 0.5f - _pad;
                if (r <= 0f) return;
                Color gold = InkWashTheme.GoldPrimary;

                // 网格 5 层
                float[] ga = { 0.12f, 0.14f, 0.16f, 0.18f, 0.25f };
                for (int lv = 1; lv <= 5; lv++)
                {
                    Color gc = new Color(gold.R, gold.G, gold.B, ga[lv - 1]);
                    Float2[] ring = Ring(c, r * lv / 5f);
                    for (int i = 0; i < _sides; i++)
                        Render2D.DrawLine(ring[i], ring[(i + 1) % _sides], gc, 1f);
                }

                // 轴线
                Color ax = new Color(gold.R, gold.G, gold.B, 0.15f);
                Float2[] outer = Ring(c, r);
                for (int i = 0; i < _sides; i++)
                    Render2D.DrawLine(c, outer[i], ax, 1f);

                // 数据多边形
                Float2[] data = new Float2[_sides];
                for (int i = 0; i < _sides; i++)
                {
                    float a = Ang(i);
                    float ratio = Mathf.Clamp(_values[i] / 100f, 0f, 1f);
                    data[i] = c + new Float2(Mathf.Cos(a), Mathf.Sin(a)) * (r * ratio);
                }
                Color fill = new Color(InkWashTheme.JadePrimary.R, InkWashTheme.JadePrimary.G, InkWashTheme.JadePrimary.B, 0.25f);
                for (int i = 0; i < _sides; i++)
                    Render2D.FillTriangle(c, data[i], data[(i + 1) % _sides], fill);
                for (int i = 0; i < _sides; i++)
                    Render2D.DrawLine(data[i], data[(i + 1) % _sides], gold, 2f);
                for (int i = 0; i < _sides; i++)
                    InkRenderHelper.FillCircle(data[i], 3f, gold);

                // 中心太极（六边形图）
                if (_taiji)
                {
                    InkRenderHelper.DrawCircle(c, 8f, new Color(gold.R, gold.G, gold.B, 0.4f), 1f);
                    InkRenderHelper.FillCircle(c, 3f, new Color(gold.R, gold.G, gold.B, 0.3f));
                }

                // 标签：名称（楷书）+ 数值（DIN）
                Font nf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, _nameSize).GetFont();
                Font vf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f).GetFont();
                if (nf == null || vf == null) return;
                const float lw = 64f, nh = 18f, vh = 14f;
                for (int i = 0; i < _sides; i++)
                {
                    float a = Ang(i);
                    float cos = Mathf.Cos(a);
                    Float2 anchor = c + new Float2(cos, Mathf.Sin(a)) * (r + 13f);
                    TextAlignment ha = cos > 0.3f ? TextAlignment.Near : (cos < -0.3f ? TextAlignment.Far : TextAlignment.Center);
                    float rx = ha == TextAlignment.Near ? anchor.X : (ha == TextAlignment.Far ? anchor.X - lw : anchor.X - lw * 0.5f);
                    bool above = _hexMode ? (i == 0) : true;
                    float ny = anchor.Y;
                    float vy = above ? anchor.Y - 14f : anchor.Y + 14f;
                    Render2D.DrawText(nf, _names[i], new Rectangle(rx, ny - nh * 0.5f, lw, nh), gold, ha, TextAlignment.Center, TextWrapping.NoWrap);
                    Render2D.DrawText(vf, _valTexts[i], new Rectangle(rx, vy - vh * 0.5f, lw, vh), _valColors[i], ha, TextAlignment.Center, TextWrapping.NoWrap);
                }
            }

            private float Ang(int i) => -Mathf.PiOverTwo + i * (Mathf.TwoPi / _sides);

            private Float2[] Ring(Float2 c, float r)
            {
                var p = new Float2[_sides];
                for (int i = 0; i < _sides; i++)
                {
                    float a = Ang(i);
                    p[i] = c + new Float2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                }
                return p;
            }
        }

        // ===================================================================
        // 嵌套控件：装备槽（三态）
        // ===================================================================

        /// <summary>浮动装备槽：equipped（品质色边框+辉光）/ selected（金边+脉冲环）/ empty（虚化暗底）。</summary>
        internal class InkEquipSlot : Control
        {
            public enum SlotState { Equipped, Selected, Empty }

            private SlotState _state;
            private Color _qualityColor;
            private string _glyph;
            private bool _isHovered;
            private bool _isPressed;
            private float _pulseTime;

            public event Action Clicked;
            public event Action HoverEnter;
            public event Action HoverLeave;

            public InkEquipSlot(SlotState state, Color qualityColor, string glyph)
            {
                _state = state;
                _qualityColor = qualityColor;
                _glyph = glyph;
                Size = new Float2(SlotSize, SlotSize);
                AutoFocus = false;
            }

            /// <summary>穿戴/卸下快照刷新时更新三态、品质色与图标字。</summary>
            public void SetContent(SlotState state, Color qualityColor, string glyph)
            {
                _state = state;
                _qualityColor = qualityColor;
                _glyph = glyph;
                if (_state != SlotState.Selected) _pulseTime = 0f;
            }

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                if (_state == SlotState.Selected)
                    _pulseTime += deltaTime;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;

                var rect = new Rectangle(Float2.Zero, Size);
                Color dark = WithAlpha(InkWashTheme.Void, 0.7f);
                Color bg, border, text;
                float thick = 1f;

                switch (_state)
                {
                    case SlotState.Equipped:
                        bg = Color.Lerp(dark, _qualityColor, 0.12f);
                        border = _qualityColor;
                        text = _qualityColor;
                        break;
                    case SlotState.Selected:
                        bg = Color.Lerp(dark, _qualityColor, 0.18f);
                        border = InkWashTheme.GoldPrimary;
                        thick = 2f;
                        text = InkWashTheme.GoldBright;
                        break;
                    default:
                        bg = WithAlpha(InkWashTheme.Void, 0.4f);
                        border = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.1f);
                        text = InkWashTheme.TextTertiary;
                        break;
                }
                if (_isHovered)
                    bg = Color.Lerp(bg, InkWashTheme.GoldPrimary, 0.12f);

                // 外辉光
                if (_state == SlotState.Equipped)
                    InkRenderHelper.FillRoundedRectangle(new Rectangle(-3f, -3f, Width + 6f, Height + 6f), 6f,
                        new Color(_qualityColor.R, _qualityColor.G, _qualityColor.B, 0.12f));
                else if (_state == SlotState.Selected)
                    InkRenderHelper.FillRoundedRectangle(new Rectangle(-4f, -4f, Width + 8f, Height + 8f), 7f,
                        new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.18f));

                InkRenderHelper.FillRoundedRectangle(rect, 4f, bg);
                InkRenderHelper.DrawRoundedRectangle(rect, 4f, border, thick);

                // 选中脉冲环
                if (_state == SlotState.Selected)
                {
                    float pulse = 0.3f + 0.2f * Mathf.Sin(_pulseTime * Mathf.Pi);
                    InkRenderHelper.DrawRoundedRectangle(new Rectangle(-3f, -3f, Width + 6f, Height + 6f), 5f,
                        new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, pulse), 1f);
                }

                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 11f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _glyph, rect, text, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            public override void OnMouseEnter(Float2 location) { _isHovered = true; HoverEnter?.Invoke(); base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _isHovered = false; HoverLeave?.Invoke(); base.OnMouseLeave(); }

            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left) _isPressed = true;
                return base.OnMouseDown(location, button);
            }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && _isPressed)
                {
                    _isPressed = false;
                    if (ContainsPoint(ref location)) Clicked?.Invoke();
                }
                return base.OnMouseUp(location, button);
            }
        }

        // ===================================================================
        // 嵌套控件：模型舞台（渐变背景 + 金色角饰 + 边框）
        // ===================================================================

        /// <summary>3D 模型展示舞台：水墨青带紫三段渐变（青墨→青紫过渡→深紫）+ 1px 金边 + 四角 L 形金饰。</summary>
        internal class ModelStage : ContainerControl
        {
            // ── 水墨青带紫背景色板（水墨风：青为主、紫为衬，低饱和深色调） ──

            /// <summary>顶部青墨</summary>
            private static readonly Color InkCyanTop = new Color(0.11f, 0.22f, 0.30f, 1f);

            /// <summary>中部深青</summary>
            private static readonly Color InkCyanMid = new Color(0.07f, 0.15f, 0.24f, 1f);

            /// <summary>中部紫墨</summary>
            private static readonly Color InkPurpleMid = new Color(0.14f, 0.11f, 0.26f, 1f);

            /// <summary>底部深紫</summary>
            private static readonly Color InkPurpleBottom = new Color(0.08f, 0.06f, 0.16f, 1f);

            public ModelStage()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;
            }

            public override void Draw()
            {
                DrawGradientBg();
                base.Draw();
                DrawGoldCorners();
                InkRenderHelper.DrawRoundedRectangle(new Rectangle(Float2.Zero, Size), 4f, InkWashTheme.BorderGold, 1f);
            }

            private void DrawGradientBg()
            {
                const int steps = 40;
                for (int i = 0; i < steps; i++)
                {
                    float t0 = i / (float)steps;
                    float t1 = (i + 1) / (float)steps;
                    Color c = SampleGradient((t0 + t1) * 0.5f);
                    Render2D.FillRectangle(new Rectangle(0f, Height * t0, Width, Height * (t1 - t0) + 1f), c);
                }
            }

            private static Color SampleGradient(float t)
            {
                // 青→紫三段渐变：顶部青墨(0~0.35) → 青紫过渡(0.35~0.7) → 底部深紫(0.7~1)
                if (t < 0.35f) return Color.Lerp(InkCyanTop, InkCyanMid, t / 0.35f);
                if (t < 0.7f) return Color.Lerp(InkCyanMid, InkPurpleMid, (t - 0.35f) / 0.35f);
                return Color.Lerp(InkPurpleMid, InkPurpleBottom, (t - 0.7f) / 0.3f);
            }

            private void DrawGoldCorners()
            {
                Color gold = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.5f);
                const float s = 20f, th = 2f, ins = 6f;
                float w = Width, h = Height;
                Render2D.DrawLine(new Float2(ins, ins), new Float2(ins + s, ins), gold, th);
                Render2D.DrawLine(new Float2(ins, ins), new Float2(ins, ins + s), gold, th);
                Render2D.DrawLine(new Float2(w - ins, ins), new Float2(w - ins - s, ins), gold, th);
                Render2D.DrawLine(new Float2(w - ins, ins), new Float2(w - ins, ins + s), gold, th);
                Render2D.DrawLine(new Float2(ins, h - ins), new Float2(ins + s, h - ins), gold, th);
                Render2D.DrawLine(new Float2(ins, h - ins), new Float2(ins, h - ins - s), gold, th);
                Render2D.DrawLine(new Float2(w - ins, h - ins), new Float2(w - ins - s, h - ins), gold, th);
                Render2D.DrawLine(new Float2(w - ins, h - ins), new Float2(w - ins, h - ins - s), gold, th);
            }
        }

        // ===================================================================
        // 嵌套控件：外观切换 Tab
        // ===================================================================

        /// <summary>外观切换标签（发型/脸型/肤色）：激活态金底金边金字。</summary>
        internal class AppearanceTab : Control
        {
            private readonly string _text;
            private bool _isActive;
            private bool _isHovered;
            private bool _isPressed;

            public event Action Clicked;

            public bool IsActive
            {
                get => _isActive;
                set => _isActive = value;
            }

            public AppearanceTab(string text)
            {
                _text = text;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                Color gold = InkWashTheme.GoldPrimary;

                if (_isActive)
                {
                    InkRenderHelper.FillRoundedRectangle(rect, 4f, new Color(gold.R, gold.G, gold.B, 0.1f));
                    InkRenderHelper.DrawRoundedRectangle(rect, 4f, InkWashTheme.BorderGoldSubtle, 1f);
                }
                else if (_isHovered)
                {
                    InkRenderHelper.FillRoundedRectangle(rect, 4f, new Color(gold.R, gold.G, gold.B, 0.06f));
                }

                Color text = _isActive ? InkWashTheme.GoldPrimary : InkWashTheme.TextSecondary;
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, rect, text, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            public override void OnMouseEnter(Float2 location) { _isHovered = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _isHovered = false; base.OnMouseLeave(); }

            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left) _isPressed = true;
                return base.OnMouseDown(location, button);
            }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && _isPressed)
                {
                    _isPressed = false;
                    if (ContainsPoint(ref location)) Clicked?.Invoke();
                }
                return base.OnMouseUp(location, button);
            }
        }

        // ===================================================================
        // 嵌套控件：卡片内小组件
        // ===================================================================

        /// <summary>小圆点（用于元素图例）。</summary>
        internal class InkDot : Control
        {
            private readonly Color _color;
            private readonly float _radius;

            public InkDot(Color color, float radius)
            {
                _color = color;
                _radius = radius;
                Size = new Float2(radius * 2f, radius * 2f);
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                InkRenderHelper.FillCircle(new Float2(_radius, _radius), _radius, _color);
            }
        }

        /// <summary>加点说明入口"？"圆钮：悬停触发 OnShow（弹出每点效果说明），离开触发 OnHide。</summary>
        internal class AllocHelpDot : Control
        {
            private const float Radius = 8f;
            private readonly int _row;
            private readonly Action<int> _onShow;
            private readonly Action _onHide;
            private bool _hovered;

            public AllocHelpDot(int row, Action<int> onShow, Action onHide)
            {
                _row = row;
                _onShow = onShow;
                _onHide = onHide;
                Size = new Float2(Radius * 2f, Radius * 2f);
                BackgroundColor = Color.Transparent;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var c = new Float2(Radius, Radius);
                InkRenderHelper.FillCircle(c, Radius, _hovered ? InkWashTheme.GoldPrimary : InkWashTheme.BorderGoldSubtle);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 11f).GetFont();
                Render2D.DrawText(font, "?", new Rectangle(0f, -1f, Radius * 2f, Radius * 2f),
                    _hovered ? InkWashTheme.PaperDark : InkWashTheme.TextSecondary,
                    TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            public override void OnMouseEnter(Float2 location)
            {
                base.OnMouseEnter(location);
                _hovered = true;
                _onShow?.Invoke(_row);
            }

            public override void OnMouseLeave()
            {
                base.OnMouseLeave();
                _hovered = false;
                _onHide?.Invoke();
            }
        }

        /// <summary>左边框条目：2px 左边框 + 浅色底 + 名称 + 数值（可选激活标签）。宽度随父容器拉伸。</summary>
        internal class StatEntry : Control
        {
            private readonly Color _accent;
            private readonly string _name;
            private readonly Color _nameColor;
            private readonly string _value;
            private readonly Color _valueColor;
            private readonly bool _showActiveTag;

            public StatEntry(Color accent, string name, Color nameColor, string value, Color valueColor, bool showActiveTag)
            {
                _accent = accent;
                _name = name;
                _nameColor = nameColor;
                _value = value;
                _valueColor = valueColor;
                _showActiveTag = showActiveTag;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                float w = Width;
                var rect = new Rectangle(0f, 0f, w, Height);
                InkRenderHelper.FillRoundedRectangle(rect, 2f, new Color(_accent.R, _accent.G, _accent.B, 0.06f));
                Render2D.FillRectangle(new Rectangle(0f, 0f, 2f, Height), _accent);

                var nf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f).GetFont();
                if (nf != null)
                    Render2D.DrawText(nf, _name, new Rectangle(12f, 0f, w - 24f, Height), _nameColor,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);

                if (_showActiveTag)
                {
                    var tagRect = new Rectangle(w - 52f, Height * 0.5f - 8f, 42f, 16f);
                    InkRenderHelper.FillRoundedRectangle(tagRect, 2f,
                        new Color(InkWashTheme.JadePrimary.R, InkWashTheme.JadePrimary.G, InkWashTheme.JadePrimary.B, 0.12f));
                    var tf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 10f).GetFont();
                    if (tf != null)
                        Render2D.DrawText(tf, "激活", tagRect, InkWashTheme.JadeBright,
                            TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }
                else if (!string.IsNullOrEmpty(_value))
                {
                    var vf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 14f).GetFont();
                    if (vf != null)
                        Render2D.DrawText(vf, _value, new Rectangle(12f, 0f, w - 24f, Height), _valueColor,
                            TextAlignment.Far, TextAlignment.Center, TextWrapping.NoWrap);
                }
            }
        }

        /// <summary>叠音层进点：24x6，激活态金色渐变 + 辉光。</summary>
        internal class LayerDot : Control
        {
            private readonly bool _active;

            public LayerDot(bool active)
            {
                _active = active;
                Size = new Float2(24f, 6f);
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                Color gold = InkWashTheme.GoldPrimary;
                if (_active)
                {
                    InkRenderHelper.FillRoundedRectangle(new Rectangle(-1f, -1f, 26f, 8f), 4f,
                        new Color(gold.R, gold.G, gold.B, 0.25f));
                    InkRenderHelper.FillRoundedRectangle(rect, 3f, gold);
                    InkRenderHelper.FillRoundedRectangle(new Rectangle(0f, 0f, 24f, 3f), 3f, InkWashTheme.GoldBright);
                }
                else
                {
                    InkRenderHelper.FillRoundedRectangle(rect, 3f, new Color(gold.R, gold.G, gold.B, 0.08f));
                }
            }
        }

        /// <summary>宝石槽：32x32，已镶嵌（品质色边框 + 辉光 + 宝石字符）/ 空槽（虚化暗底 + 加号）。</summary>
        /// <summary>宝石槽（可点击开宝石选择窗；吞掉鼠标事件避免冒泡关闭详情弹窗）。</summary>
        internal class GemSlot : Control
        {
            private InkWashTheme.InkQuality _quality;
            private bool _filled;
            private string _glyph;
            private bool _isPressed;

            public event Action Clicked;

            public GemSlot(InkWashTheme.InkQuality quality, bool filled, string glyph = null)
            {
                _quality = quality;
                _filled = filled;
                _glyph = glyph;
                Size = new Float2(32f, 32f);
                AutoFocus = false;
            }

            /// <summary>快照刷新时更新槽内容（孔数不变时可复用控件，避免重建）。</summary>
            public void SetContent(InkWashTheme.InkQuality quality, bool filled, string glyph)
            {
                _quality = quality;
                _filled = filled;
                _glyph = glyph;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                if (_filled)
                {
                    Color qc = InkWashTheme.QualityColor(_quality);
                    InkRenderHelper.FillRoundedRectangle(new Rectangle(-2f, -2f, 36f, 36f), 5f,
                        new Color(qc.R, qc.G, qc.B, 0.15f));
                    InkRenderHelper.FillRoundedRectangle(rect, 4f, Color.Lerp(InkWashTheme.BaseDefault, qc, 0.1f));
                    InkRenderHelper.DrawRoundedRectangle(rect, 4f, qc, 1f);
                    var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 14f).GetFont();
                    if (font != null)
                        Render2D.DrawText(font, string.IsNullOrEmpty(_glyph) ? "◆" : _glyph, rect, InkWashTheme.QualityTextColor(_quality),
                            TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }
                else
                {
                    InkRenderHelper.FillRoundedRectangle(rect, 4f, WithAlpha(InkWashTheme.Void, 0.4f));
                    InkRenderHelper.DrawRoundedRectangle(rect, 4f,
                        new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.1f), 1f);
                    var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f).GetFont();
                    if (font != null)
                        Render2D.DrawText(font, "+", rect, InkWashTheme.TextTertiary,
                            TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }
            }

            // 关键：宝石槽必须"吃掉"鼠标事件。此前 GemSlot 无输入处理，按下事件向上冒泡
            // 触发详情弹窗遮罩的"点击任意处关闭"，点宝石槽会把整个弹窗关掉。
            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location))
                {
                    _isPressed = true;
                    return true;
                }
                return true;   // 任何落在槽上的按下都不冒泡
            }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && _isPressed && ContainsPoint(ref location))
                    Clicked?.Invoke();
                _isPressed = false;
                return true;
            }
        }

        // ===================================================================
        // 嵌套控件：背包（合并自 InventoryPage）
        // ===================================================================

        /// <summary>背包格子数据。</summary>
        internal struct CellData
        {
            public InkWashTheme.InkQuality Quality;
            public string Glyph;
            public string Enhance;
            public string Qty;
            public bool IsQuest;
            public bool HasItem;
            public string Name;
            public string Type;
            /// <summary>客户端定义表 ID（= 服务端 TemplateId），穿戴/使用按它解析。</summary>
            public int TemplateId;
            /// <summary>服务端背包实例 ID（请求携带；快照重拉后用于保持选中）。</summary>
            public long ServerItemId;
            public ItemKind Kind;
        }

        /// <summary>自绘圆角背景 + 边框（StretchAll 填充父容器）。</summary>
        internal class InvRoundedBox : Control
        {
            private readonly Color _bg;
            private readonly Color _border;
            private readonly float _radius;

            public InvRoundedBox(Color bg, Color border, float radius)
            {
                _bg = bg;
                _border = border;
                _radius = radius;
                AutoFocus = false;
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                if (_bg.A > 0f)
                    InkRenderHelper.FillRoundedRectangle(rect, _radius, _bg);
                if (_border.A > 0f)
                    InkRenderHelper.DrawRoundedRectangle(rect, _radius, _border, 1f);
            }
        }

        /// <summary>分类筛选片（激活金底金字 + 金边，未激活弱色边框）。</summary>
        internal class InvFilterChip : Control
        {
            private readonly string _text;
            private bool _isActive;

            public event Action Clicked;

            public bool IsActive { get => _isActive; set => _isActive = value; }

            public InvFilterChip(string text, bool active)
            {
                _text = text;
                _isActive = active;
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                Color gold = InkWashTheme.GoldPrimary;
                if (_isActive)
                {
                    InkRenderHelper.FillRoundedRectangle(rect, 3f, new Color(gold.R, gold.G, gold.B, 0.12f));
                    InkRenderHelper.DrawRoundedRectangle(rect, 3f, new Color(gold.R, gold.G, gold.B, 0.25f), 1f);
                }
                else
                {
                    InkRenderHelper.DrawRoundedRectangle(rect, 3f, InkWashTheme.BorderFaint, 1f);
                }
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, rect, _isActive ? gold : InkWashTheme.TextSecondary,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location))
                    Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }
        }

        /// <summary>背包格子（品质边框六态 + 图标字 + 强化/数量角标 + 选中金色辉光）。</summary>
        internal class InvCell : Control
        {
            private CellData _data;
            private bool _isSelected;
            private bool _isHovered;

            public event Action Clicked;

            public bool IsSelected { get => _isSelected; set => _isSelected = value; }

            public InvCell(CellData data)
            {
                _data = data;
                AutoFocus = false;
            }

            /// <summary>快照刷新时整体替换格子数据（Draw 每帧读取最新值）。</summary>
            public void SetData(CellData data) => _data = data;

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                Color gold = InkWashTheme.GoldPrimary;
                Color baseBg = InkWashTheme.BaseTertiary;

                // 背景：选中 gold0.06 / 悬停 BaseTertiary0.8 / 默认 BaseTertiary0.5
                Color bg;
                if (_isSelected)
                    bg = new Color(gold.R, gold.G, gold.B, 0.06f);
                else if (_isHovered && _data.HasItem)
                    bg = new Color(baseBg.R, baseBg.G, baseBg.B, 0.8f);
                else
                    bg = new Color(baseBg.R, baseBg.G, baseBg.B, 0.5f);
                InkRenderHelper.FillRoundedRectangle(rect, 4f, bg);

                // 边框：选中金 / 空格弱色 / 任务预警色 / 品质色（普通 0.35）
                Color border;
                if (_isSelected)
                    border = gold;
                else if (!_data.HasItem)
                    border = InkWashTheme.BorderFaint;
                else if (_data.IsQuest)
                    border = InkWashTheme.Warning;
                else if (_data.Quality == InkWashTheme.InkQuality.Common)
                    border = new Color(InkWashTheme.QualityCommon.R, InkWashTheme.QualityCommon.G, InkWashTheme.QualityCommon.B, 0.35f);
                else
                    border = InkWashTheme.QualityColor(_data.Quality);
                InkRenderHelper.DrawRoundedRectangle(rect, 4f, border, 1f);

                // 辉光：选中 gold0.25 / 传说 gold0.15
                if (_isSelected)
                {
                    var glowRect = new Rectangle(-2f, -2f, Size.X + 4f, Size.Y + 4f);
                    InkRenderHelper.DrawRoundedRectangle(glowRect, 6f, new Color(gold.R, gold.G, gold.B, 0.25f), 3f);
                }
                else if (_data.HasItem && _data.Quality == InkWashTheme.InkQuality.Legendary)
                {
                    var glowRect = new Rectangle(-1.5f, -1.5f, Size.X + 3f, Size.Y + 3f);
                    InkRenderHelper.DrawRoundedRectangle(glowRect, 5.5f, new Color(gold.R, gold.G, gold.B, 0.15f), 2.5f);
                }

                if (!_data.HasItem) return;

                // 图标字（居中，品质色）
                Color iconColor = _data.IsQuest ? InkWashTheme.Warning : InkWashTheme.QualityColor(_data.Quality);
                var iconFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 26f).GetFont();
                if (iconFont != null)
                    Render2D.DrawText(iconFont, _data.Glyph, rect, iconColor,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);

                // 强化角标（左上，金色 11px）
                if (!string.IsNullOrEmpty(_data.Enhance))
                {
                    var ef = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f).GetFont();
                    if (ef != null)
                        Render2D.DrawText(ef, _data.Enhance, new Rectangle(5f, 3f, 30f, 12f), gold,
                            TextAlignment.Near, TextAlignment.Near, TextWrapping.NoWrap);
                }

                // 数量角标（右下，亮白 12px）
                if (!string.IsNullOrEmpty(_data.Qty))
                {
                    var qf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 12f).GetFont();
                    if (qf != null)
                        Render2D.DrawText(qf, _data.Qty, new Rectangle(Size.X - 35f, Size.Y - 16f, 30f, 13f),
                            InkWashTheme.PaperBright, TextAlignment.Far, TextAlignment.Far, TextWrapping.NoWrap);
                }

                // 锁标记（任务物品，右下，预警色）
                if (_data.IsQuest)
                {
                    var lf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f).GetFont();
                    if (lf != null)
                        Render2D.DrawText(lf, "锁", new Rectangle(Size.X - 19f, Size.Y - 16f, 14f, 13f),
                            InkWashTheme.Warning, TextAlignment.Far, TextAlignment.Far, TextWrapping.NoWrap);
                }
            }

            public override void OnMouseEnter(Float2 location) { _isHovered = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _isHovered = false; base.OnMouseLeave(); }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && _data.HasItem && ContainsPoint(ref location))
                    Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }
        }

        /// <summary>详情大图标（径向渐变金辉 + 金边 + 辉光 + 图标字）。</summary>
        internal class DetailIconBox : Control
        {
            private string _glyph;
            private Color _glyphColor;

            public DetailIconBox(string glyph, Color color)
            {
                _glyph = glyph;
                _glyphColor = color;
                AutoFocus = false;
            }

            public void SetGlyph(string glyph, Color color)
            {
                _glyph = glyph;
                _glyphColor = color;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                Color gold = InkWashTheme.GoldPrimary;

                InkRenderHelper.FillRadialGradient(new Float2(Width * 0.5f, Height * 0.5f), Width * 0.5f,
                    new Color(gold.R, gold.G, gold.B, 0.15f), Color.Transparent);
                var glowRect = new Rectangle(-2f, -2f, Size.X + 4f, Size.Y + 4f);
                InkRenderHelper.DrawRoundedRectangle(glowRect, 10f, new Color(gold.R, gold.G, gold.B, 0.2f), 3f);
                InkRenderHelper.DrawRoundedRectangle(rect, 8f, new Color(gold.R, gold.G, gold.B, 0.3f), 1f);

                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 56f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _glyph, rect, _glyphColor,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>详情标签（背景 + 边框 + 文字，radius 2）。</summary>
        internal class InvTagBox : Control
        {
            private string _text;
            private Color _bg;
            private Color _textColor;
            private Color _border;

            public InvTagBox(string text, Color bg, Color textColor, Color border)
            {
                _text = text;
                _bg = bg;
                _textColor = textColor;
                _border = border;
                AutoFocus = false;
            }

            public void SetText(string text) { _text = text; }

            public void SetColors(Color bg, Color textColor, Color border)
            {
                _bg = bg;
                _textColor = textColor;
                _border = border;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                InkRenderHelper.FillRoundedRectangle(rect, 2f, _bg);
                InkRenderHelper.DrawRoundedRectangle(rect, 2f, _border, 1f);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, rect, _textColor,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        // ===================================================================
        // 装备槽 Tooltip（独立可复用组件）
        // ===================================================================

        /// <summary>Tooltip 弹出侧（Auto = 由调用方按可用空间自动决定）。</summary>
        internal enum InkTooltipSide { Auto, Left, Right, Top, Bottom }

        /// <summary>
        /// 物品信息 Tooltip 配置（各项均可选，带合理默认值）。
        /// 可复用于装备槽、背包格子等任意悬停展示场景。
        /// </summary>
        internal class InkTooltipOptions
        {
            /// <summary>Tooltip 宽度（默认 300）。</summary>
            public float Width = 300f;
            /// <summary>图标区（默认开）。</summary>
            public bool ShowIcon = true;
            /// <summary>图标边长（默认 72）。</summary>
            public float IconSize = 72f;
            /// <summary>标签行：品质/类型（默认开）。</summary>
            public bool ShowTags = true;
            /// <summary>元信息 2x2：强化/五行/绑定/耐久（默认开）。</summary>
            public bool ShowMeta = true;
            /// <summary>耐久度条（默认关）。</summary>
            public bool ShowDurability = false;
            /// <summary>属性列表（默认开）。</summary>
            public bool ShowAttrs = true;
            /// <summary>套装效果（默认关）。</summary>
            public bool ShowSetEffect = false;
            /// <summary>宝石槽明细（默认关；装备槽悬停开启）。</summary>
            public bool ShowGems = false;
            /// <summary>说明文本段（默认关；装备槽悬停开启）。</summary>
            public bool ShowDesc = false;
            /// <summary>操作按钮行（默认关，悬停场景不宜放按钮）。</summary>
            public bool ShowButtons = false;
            /// <summary>可选标题文本（null = 不显示标题栏）。</summary>
            public string Title = null;
            /// <summary>首选弹出侧（Auto = 由调用方按空间决定）。</summary>
            public InkTooltipSide PreferredSide = InkTooltipSide.Auto;
        }

        /// <summary>
        /// 物品信息 Tooltip 数据（纯 UI 展示模型，由调用方填充；组件自身不含任何业务数据）。
        /// </summary>
        internal class InkTooltipData
        {
            public string Glyph = "";
            public string Name = "";
            public string Type = "";
            public InkWashTheme.InkQuality Quality = InkWashTheme.InkQuality.Common;
            public bool IsQuest = false;
            public string Enhance = null;       // 如 "+12"；null 显示 "-"
            public string Element = null;       // 如 "金"；null 显示 "-"
            public Color ElementColor = InkWashTheme.ElementMetal;
            public string Bind = null;          // 如 "拾取绑定"
            public string Durability = null;    // 如 "850/1000"
            public float DurabilityFrac = 0f;   // 0..1
            public (string label, string val, bool bonus)[] Attrs = null;
            public string SetName = null;
            public string SetCount = null;
            public string SetDesc = null;
            /// <summary>装备部位（如 "武器 · 主手"）；非装备为 null。</summary>
            public string SlotName = null;
            /// <summary>宝石槽明细行（每行一颗：已镶=宝石名+五行+亲和，空槽=空槽）。</summary>
            public string[] GemLines = null;
            /// <summary>已镶/总孔摘要（如 "2/3"），用于元信息行。</summary>
            public string GemSummary = null;
            /// <summary>物品说明文本。</summary>
            public string Desc = null;
            /// <summary>空槽位 / 未装备占位标记。</summary>
            public bool IsEmpty = false;
        }

        /// <summary>
        /// 物品信息 Tooltip（独立可复用组件）。
        /// 布局与物品详情卡片 <c>BuildItemDetailCard</c> 同构：图标 + 名称 + 标签 + 元信息 + 属性，
        /// 各分区可由 <see cref="InkTooltipOptions"/> 开关；通过 <see cref="Populate"/> 填充内容并自动计算高度。
        /// 色值严格引用 InkWashTheme / UIStyleTokens，无硬编码。
        /// </summary>
        internal class InkItemTooltip : ContainerControl
        {
            private const float Pad = 14f;
            private readonly InkTooltipOptions _opts;

            public InkItemTooltip(InkTooltipOptions opts = null)
            {
                _opts = opts ?? new InkTooltipOptions();
                AutoFocus = false;
                ClipChildren = false;
                Visible = false;
                Size = new Float2(_opts.Width, 10f);
            }

            /// <summary>填充 Tooltip 内容并重新计算布局高度（每次调用重建，分区由配置驱动）。</summary>
            public void Populate(InkTooltipData d)
            {
                RemoveChildren();
                float w = _opts.Width;
                float innerW = w - Pad * 2f;
                float cx = Pad;
                float y = Pad;

                // 背景面板（最先添加 → 绘制在最底层；StretchAll 自动填满最终尺寸）
                AddChild(new InvRoundedBox(
                    WithAlpha(InkWashTheme.Void, 0.94f),
                    InkWashTheme.BorderGoldSubtle,
                    6f));

                Color qColor = d.IsQuest ? InkWashTheme.Warning : InkWashTheme.QualityColor(d.Quality);

                // 可选标题栏
                if (!string.IsNullOrEmpty(_opts.Title))
                {
                    AddChild(MakeLabel(_opts.Title, cx, y, innerW, 20f,
                        InkWashTheme.GoldPrimary, 14f, InkWashTheme.FontRole.Display, TextAlignment.Near));
                    y += 20f + 4f;
                    AddChild(MakeFaintHairline(cx, y, innerW));
                    y += 1f + 8f;
                }

                // 空槽位缺省：插槽名 + 占位提示
                if (d.IsEmpty)
                {
                    AddChild(MakeLabel(d.Name, cx, y, innerW, 22f,
                        InkWashTheme.TextSecondary, 15f, InkWashTheme.FontRole.Display, TextAlignment.Center));
                    y += 22f + 6f;
                    AddChild(MakeLabel("未装备 · 空槽位", cx, y, innerW, 18f,
                        InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Center));
                    y += 18f + Pad;
                    Size = new Float2(w, y);
                    return;
                }

                // 图标（径向渐变金辉 + 品质色图标字）
                if (_opts.ShowIcon)
                {
                    float isz = _opts.IconSize;
                    AddChild(new DetailIconBox(d.Glyph, qColor)
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(cx + (innerW - isz) * 0.5f, y),
                        Size = new Float2(isz, isz),
                    });
                    y += isz + 2f + 10f;
                }

                // 名称（品质色，楷书 18px）
                AddChild(MakeLabel(d.Name, cx, y, innerW, 24f,
                    qColor, 18f, InkWashTheme.FontRole.Display, TextAlignment.Center));
                y += 24f + 6f;

                // 标签行：品质 + 类型
                if (_opts.ShowTags)
                {
                    string qualityName = d.IsQuest ? "任务" : QualityName(d.Quality);
                    float tagW = 56f, tagGap = 6f;
                    float total = tagW * 2f + tagGap;
                    float tx = cx + (innerW - total) * 0.5f;
                    AddChild(new InvTagBox(qualityName,
                        WithAlpha(qColor, 0.12f), qColor, WithAlpha(qColor, 0.25f))
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(tx, y),
                        Size = new Float2(tagW, 20f),
                    });
                    AddChild(new InvTagBox(d.Type,
                        WithAlpha(InkWashTheme.GoldPrimary, 0.08f), InkWashTheme.TextSecondary,
                        WithAlpha(InkWashTheme.GoldPrimary, 0.15f))
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(tx + tagW + tagGap, y),
                        Size = new Float2(tagW, 20f),
                    });
                    y += 20f + 10f;
                }

                // 元信息 2x2（强化/五行/绑定/耐久），上下发丝线
                if (_opts.ShowMeta)
                {
                    AddChild(MakeFaintHairline(cx, y, innerW));
                    y += 1f + 8f;
                    float halfW = innerW * 0.5f - 8f;
                    float col2X = cx + halfW + 16f;
                    y = AddMetaPair(y, cx, halfW, "强化", d.Enhance ?? "-",
                        d.Enhance != null ? InkWashTheme.GoldPrimary : InkWashTheme.TextSecondary,
                        col2X, halfW, "五行", d.Element ?? "-",
                        d.Element != null ? d.ElementColor : InkWashTheme.TextSecondary);
                    y = AddMetaPair(y, cx, halfW, "绑定", d.Bind ?? "-", InkWashTheme.PaperBright,
                        col2X, halfW, "耐久", d.Durability ?? "-", InkWashTheme.PaperBright);
                    if (!string.IsNullOrEmpty(d.SlotName) || !string.IsNullOrEmpty(d.GemSummary))
                    {
                        y = AddMetaPair(y, cx, halfW, "部位", d.SlotName ?? "-", InkWashTheme.PaperBright,
                            col2X, halfW, "宝石", d.GemSummary ?? "-", InkWashTheme.PaperBright);
                    }
                    AddChild(MakeFaintHairline(cx, y, innerW));
                    y += 1f + 8f;
                }

                // 耐久度条 4px
                if (_opts.ShowDurability)
                {
                    var track = new ContainerControl
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(cx + 2f, y),
                        Size = new Float2(innerW - 4f, 4f),
                        BackgroundColor = InkWashTheme.BorderFaint,
                    };
                    AddChild(track);
                    track.AddChild(new ContainerControl
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = Float2.Zero,
                        Size = new Float2((innerW - 4f) * Mathf.Clamp(d.DurabilityFrac, 0f, 1f), 4f),
                        BackgroundColor = InkWashTheme.GoldPrimary,
                    });
                    y += 4f + 10f;
                }

                // 属性列表（分区标题 + 属性行，加成属性青色）
                if (_opts.ShowAttrs && d.Attrs != null && d.Attrs.Length > 0)
                {
                    AddChild(MakeLabel("属性", cx, y, 120f, 20f,
                        InkWashTheme.GoldPrimary, 14f, InkWashTheme.FontRole.Display, TextAlignment.Near));
                    y += 20f + 4f;
                    AddChild(MakeFaintHairline(cx, y, innerW));
                    y += 1f + 8f;
                    for (int i = 0; i < d.Attrs.Length; i++)
                    {
                        var a = d.Attrs[i];
                        AddChild(MakeLabel(a.label, cx, y, innerW * 0.5f, 20f,
                            InkWashTheme.TextSecondary, 13f, InkWashTheme.FontRole.Body, TextAlignment.Near));
                        AddChild(MakeLabel(a.val, cx + innerW * 0.5f, y, innerW * 0.5f, 20f,
                            a.bonus ? InkWashTheme.TextJade : InkWashTheme.PaperBright,
                            14f, InkWashTheme.FontRole.Number, TextAlignment.Far));
                        y += 20f + 4f;
                    }
                    y += 4f;
                }

                // 宝石槽明细（每孔一行；空槽弱化显示）
                if (_opts.ShowGems && d.GemLines != null && d.GemLines.Length > 0)
                {
                    AddChild(MakeLabel("宝石槽", cx, y, 120f, 20f,
                        InkWashTheme.GoldPrimary, 14f, InkWashTheme.FontRole.Display, TextAlignment.Near));
                    y += 20f + 4f;
                    AddChild(MakeFaintHairline(cx, y, innerW));
                    y += 1f + 8f;
                    for (int i = 0; i < d.GemLines.Length; i++)
                    {
                        string line = d.GemLines[i] ?? "";
                        bool empty = line == "空槽";
                        AddChild(MakeLabel(line, cx, y, innerW, 20f,
                            empty ? InkWashTheme.TextTertiary : InkWashTheme.PaperBright,
                            12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
                        y += 20f + 2f;
                    }
                    y += 6f;
                }

                // 说明（详情卡片同源的描述文本，最多 3 行）
                if (_opts.ShowDesc && !string.IsNullOrEmpty(d.Desc))
                {
                    AddChild(MakeLabel("说明", cx, y, 120f, 20f,
                        InkWashTheme.GoldPrimary, 14f, InkWashTheme.FontRole.Display, TextAlignment.Near));
                    y += 20f + 4f;
                    AddChild(MakeFaintHairline(cx, y, innerW));
                    y += 1f + 8f;
                    // 说明按字数估行（CJK 12px ≈ 22 字/行，最多 4 行），保证不溢出也不留空腔
                    int descLines = Mathf.Clamp((d.Desc.Length + 21) / 22, 1, 4);
                    float descH = descLines * 18f;
                    var descLabel = MakeLabel(d.Desc, cx, y, innerW, descH,
                        InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near);
                    descLabel.Wrapping = TextWrapping.WrapWords;
                    AddChild(descLabel);
                    y += descH + 4f;
                }

                // 套装效果（可选）
                if (_opts.ShowSetEffect && !string.IsNullOrEmpty(d.SetName))
                {
                    AddChild(MakeLabel("套装效果", cx, y, 120f, 20f,
                        InkWashTheme.GoldPrimary, 14f, InkWashTheme.FontRole.Display, TextAlignment.Near));
                    y += 20f + 4f;
                    AddChild(MakeFaintHairline(cx, y, innerW));
                    y += 1f + 8f;
                    AddChild(MakeLabel("◆ " + d.SetName + "  " + (d.SetCount ?? ""), cx, y, innerW, 18f,
                        InkWashTheme.GoldPrimary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
                    y += 18f + 2f;
                    if (!string.IsNullOrEmpty(d.SetDesc))
                    {
                        AddChild(MakeLabel(d.SetDesc, cx, y, innerW, 18f,
                            InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
                        y += 18f + 2f;
                    }
                    y += 4f;
                }

                // 操作按钮行（可选；悬停场景默认关闭）
                if (_opts.ShowButtons)
                {
                    AddChild(MakeFaintHairline(cx, y, innerW));
                    y += 1f + 10f;
                    float btnW = (innerW - 12f) * 0.5f;
                    AddChild(new InkButton
                    {
                        Variant = InkButtonVariant.Brand,
                        ButtonSize = InkButtonSize.Md,
                        Text = "装备",
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(cx, y),
                        Size = new Float2(btnW, 30f),
                    });
                    AddChild(new InkButton
                    {
                        Variant = InkButtonVariant.Secondary,
                        ButtonSize = InkButtonSize.Md,
                        Text = "关闭",
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(cx + btnW + 12f, y),
                        Size = new Float2(btnW, 30f),
                    });
                    y += 30f + 4f;
                }

                y += Pad;
                Size = new Float2(w, y);
            }

            /// <summary>元信息行：同一行两组 标签+值（与详情卡片 AddInvMetaRow 同构），返回下一段 y。</summary>
            private float AddMetaPair(float y,
                float x1, float w1, string l1, string v1, Color vc1,
                float x2, float w2, string l2, string v2, Color vc2)
            {
                AddChild(MakeLabel(l1, x1, y, w1 * 0.4f, 20f,
                    InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
                AddChild(MakeLabel(v1, x1 + w1 * 0.4f, y, w1 * 0.6f, 20f,
                    vc1, 13f, InkWashTheme.FontRole.Number, TextAlignment.Far));
                AddChild(MakeLabel(l2, x2, y, w2 * 0.4f, 20f,
                    InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
                AddChild(MakeLabel(v2, x2 + w2 * 0.4f, y, w2 * 0.6f, 20f,
                    vc2, 13f, InkWashTheme.FontRole.Number, TextAlignment.Far));
                return y + 20f + 6f;
            }

            private static string QualityName(InkWashTheme.InkQuality q) => q switch
            {
                InkWashTheme.InkQuality.Legendary => "传说",
                InkWashTheme.InkQuality.Epic => "史诗",
                InkWashTheme.InkQuality.Rare => "稀有",
                InkWashTheme.InkQuality.Uncommon => "良好",
                _ => "普通",
            };
        }
    }
}
