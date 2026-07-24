using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

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
        private InvFilterChip[] _invFilterChips;
        private ContainerControl _invCapFill;
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

        // ===== 装备槽 Tooltip =====
        private InkItemTooltip _slotTooltip;
        private InkTooltipData[] _slotTooltipData;

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
            if (_slotTooltip != null) AddChild(_slotTooltip);   // Tooltip 挂页面根节点（最上层绘制，不被左右栏裁剪）
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.R, c.G, c.B, a);

        public void BindCharacter(CharacterAttributesComponent component)
        {
            _boundCharacter = component;
            if (component != null)
            {
                if (_nameLabel != null) _nameLabel.Text = component.Nickname;
                if (_levelLabel != null) _levelLabel.Text = "Lv." + component.Level.ToString();
            }
            // 绑定实时 3D 角色模型（场景未就绪时组件内部会暂存并重放）
            _preview3D?.SetCharacter(component);
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
            // (glyph, xFrac, xPx, xRight, yFrac, yPx, yBottom, state, quality)
            var defs = new (string g, float xf, float xp, bool xr, float yf, float yp, bool yb,
                            InkEquipSlot.SlotState st, InkWashTheme.InkQuality q)[]
            {
                ("环", 0.30f, 0f,  false, -1f,   12f, false, InkEquipSlot.SlotState.Equipped, InkWashTheme.InkQuality.Common),
                ("头", 0.50f, 0f,  false, -1f,   12f, false, InkEquipSlot.SlotState.Equipped, InkWashTheme.InkQuality.Epic),
                ("环", 0.70f, 0f,  false, -1f,   12f, false, InkEquipSlot.SlotState.Empty,    InkWashTheme.InkQuality.Common),
                ("武", -1f,   12f, false, 0.30f, 0f,  false, InkEquipSlot.SlotState.Selected, InkWashTheme.InkQuality.Legendary),
                ("链", -1f,   12f, true,  0.30f, 0f,  false, InkEquipSlot.SlotState.Equipped, InkWashTheme.InkQuality.Rare),
                ("主", -1f,   8f,  false, 0.48f, 0f,  false, InkEquipSlot.SlotState.Equipped, InkWashTheme.InkQuality.Legendary),
                ("副", -1f,   8f,  true,  0.48f, 0f,  false, InkEquipSlot.SlotState.Empty,    InkWashTheme.InkQuality.Common),
                ("戒", -1f,   12f, false, 0.66f, 0f,  false, InkEquipSlot.SlotState.Equipped, InkWashTheme.InkQuality.Epic),
                ("戒", -1f,   12f, true,  0.66f, 0f,  false, InkEquipSlot.SlotState.Empty,    InkWashTheme.InkQuality.Common),
                ("手", 0.18f, 0f,  false, -1f,   64f, true,  InkEquipSlot.SlotState.Equipped, InkWashTheme.InkQuality.Common),
                ("胸", 0.38f, 0f,  false, -1f,   64f, true,  InkEquipSlot.SlotState.Equipped, InkWashTheme.InkQuality.Legendary),
                ("腿", 0.58f, 0f,  false, -1f,   64f, true,  InkEquipSlot.SlotState.Equipped, InkWashTheme.InkQuality.Uncommon),
                ("脚", 0.78f, 0f,  false, -1f,   64f, true,  InkEquipSlot.SlotState.Equipped, InkWashTheme.InkQuality.Uncommon),
            };

            int n = defs.Length;
            _slots = new InkEquipSlot[n];
            _slotXFrac = new float[n]; _slotXPx = new float[n]; _slotXRight = new bool[n];
            _slotYFrac = new float[n]; _slotYPx = new float[n]; _slotYBottom = new bool[n];

            _slotTooltipData = new InkTooltipData[n];
            for (int i = 0; i < n; i++)
            {
                var d = defs[i];
                var slot = new InkEquipSlot(d.st, InkWashTheme.QualityColor(d.q), d.g);
                int captured = i;
                slot.Clicked += () => OnSlotClicked(captured);
                slot.HoverEnter += () => OnSlotHoverEnter(captured);
                slot.HoverLeave += () => OnSlotHoverLeave(captured);
                _slots[i] = slot;
                _modelStage.AddChild(slot);
                _slotTooltipData[i] = MakeSlotTooltipData(d.g, d.st, d.q);

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
                ShowDurability = true,
                ShowAttrs = true,
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

            _cards.Add(BuildBaseStatsCard());      // 1. 基础属性
            _cards.Add(BuildAttrOverviewCard());   // 2. 属性总览（五维/五行并排）
            _cards.Add(BuildCombatCard());         // 3. 战斗属性
            _cards.Add(BuildInventoryCard());      // 4. 行囊
            _cards.Add(BuildItemDetailCard());     // 5. 物品详情（含宝石槽）
            _cards.Add(BuildTiaoLvCard());         // 6. 调律
            _cards.Add(BuildDingYinCard());        // 7. 定音
            _cards.Add(BuildDieYinCard());         // 8. 叠音

            foreach (var card in _cards)
                _rightScroll.AddChild(card);

            SelectInvCell(0);
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
        }

        private void OnSlotHoverEnter(int index)
        {
            if (_slots == null || _slotTooltip == null || _slotTooltipData == null) return;
            if (index < 0 || index >= _slots.Length || index >= _slotTooltipData.Length) return;
            _slotTooltip.Populate(_slotTooltipData[index]);
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

        /// <summary>生成装备槽 Tooltip 演示数据（纯 UI 展示层，后续可替换为真实装备数据绑定）。</summary>
        private static InkTooltipData MakeSlotTooltipData(string glyph, InkEquipSlot.SlotState state, InkWashTheme.InkQuality quality)
        {
            var (name, type) = glyph switch
            {
                "环" => ("翠玉环", "饰品"),
                "头" => ("云纹冠", "头盔"),
                "武" => ("玄铁剑", "武器"),
                "链" => ("璎珞链", "饰品"),
                "主" => ("青霜刃", "武器"),
                "副" => ("玄铁盾", "副手"),
                "戒" => ("素银戒", "饰品"),
                "手" => ("精铁护腕", "护手"),
                "胸" => ("锁子甲", "胸甲"),
                "腿" => ("行军护腿", "护腿"),
                "脚" => ("踏云靴", "靴子"),
                _ => ("未知装备", "装备"),
            };

            if (state == InkEquipSlot.SlotState.Empty)
                return new InkTooltipData { Glyph = glyph, Name = name, Type = type, IsEmpty = true };

            int tier = quality switch
            {
                InkWashTheme.InkQuality.Legendary => 5,
                InkWashTheme.InkQuality.Epic => 4,
                InkWashTheme.InkQuality.Rare => 3,
                InkWashTheme.InkQuality.Uncommon => 2,
                _ => 1,
            };

            // 五行（按字形首字符稳定映射，避免 GetHashCode 随机化）
            var elements = new (string name, Color color)[]
            {
                ("金", InkWashTheme.ElementMetal), ("木", InkWashTheme.ElementWood),
                ("水", InkWashTheme.ElementWater), ("火", InkWashTheme.ElementFire),
                ("土", InkWashTheme.ElementEarth),
            };
            var elem = elements[glyph.Length > 0 ? glyph[0] % 5 : 0];

            bool isWeapon = type == "武器" || type == "副手";
            var attrs = new List<(string, string, bool)>
            {
                (isWeapon ? "攻击力" : "防御力", "+" + (30 * tier), false),
                ("暴击率", "+" + (2 * tier) + "%", true),
            };
            if (tier >= 4) attrs.Add(("会心率", "+" + tier + "%", true));

            return new InkTooltipData
            {
                Glyph = glyph,
                Name = name,
                Type = type,
                Quality = quality,
                Enhance = "+" + (3 * tier),
                Element = elem.name,
                ElementColor = elem.color,
                Bind = tier >= 3 ? "拾取绑定" : "未绑定",
                Durability = (500 + 100 * tier) + "/" + (500 + 100 * tier),
                DurabilityFrac = Mathf.Clamp(0.45f + 0.11f * tier, 0f, 1f),
                Attrs = attrs.ToArray(),
            };
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

        /// <summary>初始化背包格子演示数据（8x5，15 件物品）。</summary>
        private void InitInvCellData()
        {
            const int cols = 8, rows = 5;
            _invCellData = new CellData[cols * rows];
            var items = new (string glyph, string name, string type, InkWashTheme.InkQuality q,
                             string enh, string qty, bool quest)[]
            {
                ("剑", "玄铁剑",   "武器", InkWashTheme.InkQuality.Legendary, "+12", null, false),
                ("刀", "寒铁刀",   "武器", InkWashTheme.InkQuality.Epic,      "+8",  null, false),
                ("腕", "精铁护腕", "防具", InkWashTheme.InkQuality.Rare,      "+5",  null, false),
                ("衣", "布衣",     "防具", InkWashTheme.InkQuality.Common,    null,  null, false),
                ("剑", "铁剑",     "武器", InkWashTheme.InkQuality.Common,    null,  null, false),
                ("甲", "皮甲",     "防具", InkWashTheme.InkQuality.Common,    null,  null, false),
                ("药", "生命药水", "丹药", InkWashTheme.InkQuality.Uncommon,  null,  "9", false),
                ("液", "内力药水", "丹药", InkWashTheme.InkQuality.Uncommon,  null,  "3", false),
                ("丹", "活血丹",   "丹药", InkWashTheme.InkQuality.Uncommon,  null,  "5", false),
                ("矿", "铁矿",     "材料", InkWashTheme.InkQuality.Common,    null,  "3", false),
                ("草", "草药",     "材料", InkWashTheme.InkQuality.Common,    null,  "5", false),
                ("皮", "兽皮",     "材料", InkWashTheme.InkQuality.Common,    null,  "2", false),
                ("矿", "铁矿",     "材料", InkWashTheme.InkQuality.Common,    null,  "4", false),
                ("草", "草药",     "材料", InkWashTheme.InkQuality.Common,    null,  "2", false),
                ("信", "密信",     "任务", InkWashTheme.InkQuality.Common,    null,  null, true),
            };
            int[] slots = { 0, 1, 2, 3, 4, 5, 8, 9, 10, 16, 17, 18, 19, 20, 24 };
            for (int i = 0; i < _invCellData.Length; i++)
                _invCellData[i] = new CellData { Quality = InkWashTheme.InkQuality.Common, HasItem = false };
            for (int k = 0; k < items.Length; k++)
            {
                var it = items[k];
                _invCellData[slots[k]] = new CellData
                {
                    Quality = it.q,
                    Glyph = it.glyph,
                    Enhance = it.enh,
                    Qty = it.qty,
                    IsQuest = it.quest,
                    HasItem = true,
                    Name = it.name,
                    Type = it.type,
                };
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
            footer.AddChild(MakeLabel("15/80", 56f, 0f, 60f, footerH,
                InkWashTheme.PaperBright, 14f, InkWashTheme.FontRole.Number, TextAlignment.Near));
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

        /// <summary>背包属性区块：标题 + 属性行，返回下一段 y。</summary>
        private float AddInvAttrSection(ContainerControl parent, float x, float y, float w,
            string title, (string label, string val)[] rows, Color[] valColors)
        {
            parent.AddChild(MakeInvSectionTitle(title, x, y, w));
            y += 20f + 4f + 8f;
            for (int i = 0; i < rows.Length; i++)
            {
                parent.AddChild(MakeLabel(rows[i].label, x, y, w * 0.5f, 20f,
                    InkWashTheme.TextSecondary, 13f, InkWashTheme.FontRole.Body, TextAlignment.Near));
                parent.AddChild(MakeLabel(rows[i].val, x + w * 0.5f, y, w * 0.5f, 20f,
                    valColors[i], 14f, InkWashTheme.FontRole.Number, TextAlignment.Far));
                y += 20f + 6f;
            }
            y += 8f;
            return y;
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
            duraTrack.AddChild(new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2((innerW - 4f) * 0.85f, 4f),
                BackgroundColor = InkWashTheme.GoldPrimary,
            });
            dy += 4f + 14f;

            // 基础属性 / 附加属性
            dy = AddInvAttrSection(detail, cx, dy, innerW, "基础属性",
                new[] { ("攻击力", "+120"), ("暴击率", "+5%") },
                new[] { InkWashTheme.PaperBright, InkWashTheme.PaperBright });
            dy = AddInvAttrSection(detail, cx, dy, innerW, "附加属性",
                new[] { ("会心率", "+3%") },
                new[] { InkWashTheme.TextJade });

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
            setBox.AddChild(MakeLabel("玄铁套", 30f, 8f, 80f, 18f,
                InkWashTheme.GoldPrimary, 13f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            setBox.AddChild(MakeLabel("2/4", 112f, 8f, 40f, 18f,
                InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Number, TextAlignment.Near));
            setBox.AddChild(MakeLabel("2件套：攻击力 +10%", 12f, 32f, innerW - 24f, 18f,
                InkWashTheme.TextSecondary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            dy += 60f + 12f;

            // 宝石槽（并入自原宝石卡片）：分区标题 + 5 个宝石槽
            detail.AddChild(MakeInvSectionTitle("宝石槽", cx, dy, innerW));
            dy += 20f + 4f + 8f;
            var gemRow = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx, dy),
                Size = new Float2(184f, 32f),
                BackgroundColor = Color.Transparent,
            };
            var gems = new (InkWashTheme.InkQuality q, bool filled)[]
            {
                (InkWashTheme.InkQuality.Rare, true),
                (InkWashTheme.InkQuality.Epic, true),
                (InkWashTheme.InkQuality.Legendary, true),
                (InkWashTheme.InkQuality.Common, false),
                (InkWashTheme.InkQuality.Common, false),
            };
            for (int i = 0; i < gems.Length; i++)
                gemRow.AddChild(new GemSlot(gems[i].q, gems[i].filled) { Location = new Float2(i * 38f, 0f) });
            detail.AddChild(gemRow);
            dy += 32f + 12f;

            // 操作按钮行（使用/装备/丢弃）
            detail.AddChild(MakeFaintHairline(cx, dy, innerW));
            dy += 1f + 12f;
            float btnW = (innerW - 24f) / 3f;
            var useBtn = new InkButton
            {
                Variant = InkButtonVariant.Brand,
                ButtonSize = InkButtonSize.Lg,
                Text = "使用",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx, dy),
                Size = new Float2(btnW, 36f),
            };
            useBtn.ButtonClicked += (b) => EmitGoldAtControl(useBtn);
            detail.AddChild(useBtn);
            var equipBtn = new InkButton
            {
                Variant = InkButtonVariant.Secondary,
                ButtonSize = InkButtonSize.Lg,
                Text = "装备",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx + btnW + 12f, dy),
                Size = new Float2(btnW, 36f),
            };
            equipBtn.ButtonClicked += (b) => EmitGoldAtControl(equipBtn);
            detail.AddChild(equipBtn);
            var dropBtn = new InkButton
            {
                Variant = InkButtonVariant.Danger,
                ButtonSize = InkButtonSize.Lg,
                Text = "丢弃",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cx + (btnW + 12f) * 2f, dy),
                Size = new Float2(btnW, 36f),
            };
            dropBtn.ButtonClicked += (b) => EmitGoldAtControl(dropBtn);
            detail.AddChild(dropBtn);

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
        }

        private void OnInvCellClicked(int index)
        {
            if (_invCellData != null && index >= 0 && index < _invCellData.Length && _invCellData[index].HasItem)
                SelectInvCell(index);
        }

        private void SelectInvCell(int index)
        {
            if (_invCells == null) return;
            for (int i = 0; i < _invCells.Length; i++)
                _invCells[i].IsSelected = (i == index);
            _invSelected = index;
            PopulateInvDetail(index);
            if (index >= 0 && index < _invCells.Length)
                EmitGoldAtControl(_invCells[index]);
        }

        private void PopulateInvDetail(int index)
        {
            if (_invCellData == null || index < 0 || index >= _invCellData.Length || !_invCellData[index].HasItem) return;
            var data = _invCellData[index];
            string qualityName = data.IsQuest ? "任务" : data.Quality switch
            {
                InkWashTheme.InkQuality.Legendary => "传说",
                InkWashTheme.InkQuality.Epic => "史诗",
                InkWashTheme.InkQuality.Rare => "稀有",
                InkWashTheme.InkQuality.Uncommon => "良好",
                _ => "普通",
            };
            Color qColor = data.IsQuest ? InkWashTheme.Warning : InkWashTheme.QualityColor(data.Quality);
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
            if (_invTagQty != null) _invTagQty.SetText("数量 " + (data.Qty ?? "1"));
            if (_invDetailIcon != null) _invDetailIcon.SetGlyph(data.Glyph, qColor);
            if (_invMetaEnhance != null)
            {
                _invMetaEnhance.Text = data.Enhance ?? "-";
                _invMetaEnhance.TextColor = data.Enhance != null ? InkWashTheme.GoldPrimary : InkWashTheme.TextSecondary;
            }
            if (_invMetaDura != null) _invMetaDura.Text = data.Enhance != null ? "850/1000" : "-/-";
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
            var fiveRadar = new InkRadarChart(
                5,
                new[] { 60f, 45f, 80f, 50f, 95f },
                new[] { "体", "御", "敏", "势", "劲" },
                new[] { "60", "45", "80", "50", "95" },
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
            var wuxingRadar = new InkRadarChart(
                5,
                new[] { 75f, 60f, 80f, 65f, 70f },
                new[] { "金", "木", "水", "火", "土" },
                new[] { "75", "60", "80", "65", "70" },
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

        /// <summary>3. 基础属性：3 列网格 6 项（暴击率/暴击伤害金色）。</summary>
        private ContainerControl BuildBaseStatsCard()
        {
            var card = MakeCard(150f);
            AddCardHeader(card, "基础属性", "BASE STATS", CardPad);

            string[] names = { "攻击力", "防御力", "暴击率", "暴击伤害", "命中率", "闪避率" };
            string[] vals = { "1234", "567", "45%", "250%", "95%", "12%" };
            bool[] goldVal = { false, false, true, true, false, false };
            float gy = CardPad + 26f;
            const float rowH = 38f;
            for (int i = 0; i < names.Length; i++)
            {
                int col = i % 3, row = i / 3;
                float x = CardPad + col * 118f;
                float y = gy + row * rowH;
                card.AddChild(MakeLabel(names[i], x, y, 110f, 14f,
                    InkWashTheme.TextSecondary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));
                card.AddChild(MakeLabel(vals[i], x, y + 15f, 110f, 18f,
                    goldVal[i] ? InkWashTheme.GoldBright : InkWashTheme.TextDefault,
                    14f, InkWashTheme.FontRole.Number, TextAlignment.Near));
            }

            var hairline = MakeHairline(CardPad, card.Height - CardPad, 2000f);
            _fullWidthHairlines.Add(hairline);
            card.AddChild(hairline);
            return card;
        }

        /// <summary>4. 战斗属性：战斗力 32px 金色 + 2 列 6 项。</summary>
        private ContainerControl BuildCombatCard()
        {
            var card = MakeCard(204f);
            AddCardHeader(card, "战斗属性", "COMBAT STATS", CardPad);

            // 战斗力（32px DIN 金色）
            card.AddChild(MakeLabel("45,678", CardPad, CardPad + 22f, 200f, 40f,
                InkWashTheme.GoldBright, 32f, InkWashTheme.FontRole.Number, TextAlignment.Near));
            card.AddChild(MakeLabel("战斗力", CardPad + 128f, CardPad + 42f, 80f, 18f,
                InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));

            var hairline = MakeHairline(CardPad, CardPad + 68f, 2000f);
            _fullWidthHairlines.Add(hairline);
            card.AddChild(hairline);

            string[] names = { "境界", "门派", "阵营", "侠义值", "恶名值", "修炼经验" };
            string[] vals = { "筑基后期", "武当派", "正派", "1200", "0", "82%" };
            Color[] vcolors =
            {
                InkWashTheme.TextDefault, InkWashTheme.TextDefault, InkWashTheme.JadeBright,
                InkWashTheme.GoldBright, InkWashTheme.TextDefault, InkWashTheme.TextDefault,
            };
            float gy = CardPad + 78f;
            const float rowH = 30f;
            for (int i = 0; i < names.Length; i++)
            {
                int col = i % 2, row = i / 2;
                float x = CardPad + col * 185f;
                float y = gy + row * rowH;
                card.AddChild(MakeLabel(names[i], x, y, 64f, 16f,
                    InkWashTheme.TextSecondary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));
                card.AddChild(MakeLabel(vals[i], x + 66f, y, 110f, 16f,
                    vcolors[i], 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
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

            private readonly SlotState _state;
            private readonly Color _qualityColor;
            private readonly string _glyph;
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
        internal class GemSlot : Control
        {
            private readonly InkWashTheme.InkQuality _quality;
            private readonly bool _filled;

            public GemSlot(InkWashTheme.InkQuality quality, bool filled)
            {
                _quality = quality;
                _filled = filled;
                Size = new Float2(32f, 32f);
                AutoFocus = false;
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
                        Render2D.DrawText(font, "◆", rect, InkWashTheme.QualityTextColor(_quality),
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
            private readonly CellData _data;
            private bool _isSelected;
            private bool _isHovered;

            public event Action Clicked;

            public bool IsSelected { get => _isSelected; set => _isSelected = value; }

            public InvCell(CellData data)
            {
                _data = data;
                AutoFocus = false;
            }

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
