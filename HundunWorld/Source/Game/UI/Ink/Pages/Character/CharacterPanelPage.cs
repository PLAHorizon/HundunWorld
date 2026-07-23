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
    /// 顶部标题栏 + 左侧 3D 模型展示区（13 个浮动装备槽）+ 右侧可滚动属性面板（10 个卡片）。
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

            for (int i = 0; i < n; i++)
            {
                var d = defs[i];
                var slot = new InkEquipSlot(d.st, InkWashTheme.QualityColor(d.q), d.g);
                int captured = i;
                slot.Clicked += () => OnSlotClicked(captured);
                _slots[i] = slot;
                _modelStage.AddChild(slot);

                _slotXFrac[i] = d.xf; _slotXPx[i] = d.xp; _slotXRight[i] = d.xr;
                _slotYFrac[i] = d.yf; _slotYPx[i] = d.yp; _slotYBottom[i] = d.yb;
            }
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
        // 右列 — 可滚动属性面板（10 个卡片）
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

            _cards.Add(BuildFiveAttrCard());
            _cards.Add(BuildWuxingCard());
            _cards.Add(BuildBaseStatsCard());
            _cards.Add(BuildCombatCard());
            _cards.Add(BuildEquipDetailCard());
            _cards.Add(BuildTiaoLvCard());
            _cards.Add(BuildDingYinCard());
            _cards.Add(BuildDieYinCard());
            _cards.Add(BuildSetEffectCard());
            _cards.Add(BuildGemSlotCard());

            foreach (var card in _cards)
                _rightScroll.AddChild(card);
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

        private void OnAppearanceTabClicked(int index)
        {
            if (_appTabs == null) return;
            for (int i = 0; i < _appTabs.Length; i++)
                _appTabs[i].IsActive = (i == index);
        }

        // ===================================================================
        // 卡片构建（10 个，对应设计方案右侧属性面板）
        // ===================================================================

        /// <summary>1. 五维属性：五边形雷达图（体60/御45/敏80/势50/劲95）。</summary>
        private ContainerControl BuildFiveAttrCard()
        {
            var card = MakeCard(276f);
            AddCardHeader(card, "五维属性", "FIVE ATTRIBUTES", CardPad);

            var radar = new InkRadarChart(
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
                Location = new Float2(0f, CardPad + 26f),
                Size = new Float2(220f, 220f),
            };
            _centeredControls.Add(radar);
            card.AddChild(radar);
            return card;
        }

        /// <summary>2. 五行体质：六边形雷达图 + 中心太极 + 元素图例。</summary>
        private ContainerControl BuildWuxingCard()
        {
            var card = MakeCard(346f);
            AddCardHeader(card, "五行体质", "FIVE ELEMENTS", CardPad);

            var radar = new InkRadarChart(
                6,
                new[] { 75f, 60f, 80f, 65f, 70f, 85f },
                new[] { "金", "木", "水", "火", "土", "体质" },
                new[] { "75", "60", "80", "65", "70", "85" },
                new[]
                {
                    InkWashTheme.GoldBright, InkWashTheme.GoldBright, InkWashTheme.GoldBright,
                    InkWashTheme.GoldBright, InkWashTheme.GoldBright, InkWashTheme.GoldBright,
                },
                hexMode: true, taiji: true, nameSize: 15f, pad: 36f)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, CardPad + 26f),
                Size = new Float2(260f, 260f),
            };
            _centeredControls.Add(radar);
            card.AddChild(radar);

            // 元素图例：8px 圆点 + 10px 文字，6 项（金木水火土 + 体质）
            float ly = CardPad + 26f + 260f + 10f;
            string[] legendNames = { "金", "木", "水", "火", "土", "体质" };
            Color[] legendColors =
            {
                InkWashTheme.ElementColor(InkWashTheme.InkElement.Metal),
                InkWashTheme.ElementColor(InkWashTheme.InkElement.Wood),
                InkWashTheme.ElementColor(InkWashTheme.InkElement.Water),
                InkWashTheme.ElementColor(InkWashTheme.InkElement.Fire),
                InkWashTheme.ElementColor(InkWashTheme.InkElement.Earth),
                InkWashTheme.JadePrimary,
            };
            var legend = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, ly),
                Size = new Float2(252f, 16f),
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
            _centeredControls.Add(legend);
            card.AddChild(legend);
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

        /// <summary>5. 装备详情：传说武器图标 + 名称/品质标签 + 五行/类型 + 耐久条。</summary>
        private ContainerControl BuildEquipDetailCard()
        {
            var card = MakeCard(108f);
            AddCardHeader(card, "装备详情", "EQUIPMENT", CardPadSm);

            float iy = CardPadSm + 26f;
            // 52x52 传说品质图标（+12 强化角标）
            card.AddChild(new EquipIcon(InkWashTheme.InkQuality.Legendary, "剑", "+12")
            {
                Location = new Float2(CardPadSm, iy),
            });

            // 名称（15px 传说品质色）+ 传说标签
            card.AddChild(MakeLabel("玄铁剑", CardPadSm + 62f, iy, 80f, 20f,
                InkWashTheme.QualityTextColor(InkWashTheme.InkQuality.Legendary),
                15f, InkWashTheme.FontRole.Display, TextAlignment.Near));
            card.AddChild(new QualityTag(InkWashTheme.InkQuality.Legendary, "传说")
            {
                Location = new Float2(CardPadSm + 62f + 72f, iy + 3f),
            });

            // 五行·金 / 类型·长剑
            card.AddChild(MakeLabel("五行·金  类型·长剑", CardPadSm + 62f, iy + 22f, 200f, 14f,
                InkWashTheme.TextSecondary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Near));

            // 耐久条 6px（青玉渐变 85%）+ 数值
            float by = iy + 44f;
            card.AddChild(new DurabilityBar(0.85f)
            {
                Location = new Float2(CardPadSm + 62f, by),
                Size = new Float2(190f, 6f),
            });
            card.AddChild(MakeLabel("850/1000", CardPadSm + 62f + 196f, by - 4f, 70f, 14f,
                InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Number, TextAlignment.Near));
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

        /// <summary>9. 套装效果：2/4，玄铁套（2件激活青 / 4件未激活灰）。</summary>
        private ContainerControl BuildSetEffectCard()
        {
            var card = MakeCard(124f);
            AddCardHeader(card, "套装效果", "SET EFFECTS", CardPadSm);

            var count = MakeLabel("2/4", 0f, CardPadSm + 2f, 70f, 14f,
                InkWashTheme.GoldBright, 10f, InkWashTheme.FontRole.Number, TextAlignment.Far);
            _rightCountLabels.Add(count);
            card.AddChild(count);

            // 套装名（传说品质色）
            card.AddChild(MakeLabel("玄铁套", CardPadSm, CardPadSm + 26f, 120f, 18f,
                InkWashTheme.QualityTextColor(InkWashTheme.InkQuality.Legendary),
                13f, InkWashTheme.FontRole.Display, TextAlignment.Near));

            // 2件套：攻击 +10%（激活，青玉）
            float ry = CardPadSm + 50f;
            card.AddChild(new SetBadge(true) { Location = new Float2(CardPadSm, ry + 2f) });
            card.AddChild(MakeLabel("2件套：攻击 +10%", CardPadSm + 26f, ry, 220f, 22f,
                InkWashTheme.JadeBright, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));

            // 4件套：暴击率 +15%（未激活，弱色）
            card.AddChild(new SetBadge(false) { Location = new Float2(CardPadSm, ry + 28f) });
            card.AddChild(MakeLabel("4件套：暴击率 +15%", CardPadSm + 26f, ry + 26f, 220f, 22f,
                InkWashTheme.TextTertiary, 12f, InkWashTheme.FontRole.Body, TextAlignment.Near));
            return card;
        }

        /// <summary>10. 宝石槽：3/5（稀有/史诗/传说已镶嵌 + 2 个空槽）。</summary>
        private ContainerControl BuildGemSlotCard()
        {
            var card = MakeCard(86f);
            AddCardHeader(card, "宝石槽", "GEM SLOTS", CardPadSm);

            var count = MakeLabel("3/5", 0f, CardPadSm + 2f, 70f, 14f,
                InkWashTheme.GoldBright, 10f, InkWashTheme.FontRole.Number, TextAlignment.Far);
            _rightCountLabels.Add(count);
            card.AddChild(count);

            float gy = CardPadSm + 28f;
            var row = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(CardPadSm, gy),
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
                row.AddChild(new GemSlot(gems[i].q, gems[i].filled) { Location = new Float2(i * 38f, 0f) });
            card.AddChild(row);
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
        // 嵌套控件：模型舞台（渐变背景 + 金色角饰 + 边框）
        // ===================================================================

        /// <summary>3D 模型展示舞台：垂直三段渐变（mist→void→abyss）+ 1px 金边 + 四角 L 形金饰。</summary>
        internal class ModelStage : ContainerControl
        {
            private static readonly Color TopTint = Color.Lerp(InkWashTheme.BaseDefault, InkWashTheme.GoldPrimary, 0.07f);

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
                if (t < 0.7f) return Color.Lerp(TopTint, InkWashTheme.Void, t / 0.7f);
                return Color.Lerp(InkWashTheme.Void, InkWashTheme.Abyss, (t - 0.7f) / 0.3f);
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

        /// <summary>装备图标：品质色边框 + 12% 背景 + 辉光 + 字符 + 强化角标。</summary>
        internal class EquipIcon : Control
        {
            private readonly InkWashTheme.InkQuality _quality;
            private readonly string _glyph;
            private readonly string _enhance;

            public EquipIcon(InkWashTheme.InkQuality quality, string glyph, string enhance)
            {
                _quality = quality;
                _glyph = glyph;
                _enhance = enhance;
                Size = new Float2(52f, 52f);
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                Color qc = InkWashTheme.QualityColor(_quality);
                var rect = new Rectangle(Float2.Zero, Size);
                InkRenderHelper.FillRoundedRectangle(new Rectangle(-3f, -3f, 58f, 58f), 7f,
                    new Color(qc.R, qc.G, qc.B, 0.2f));
                InkRenderHelper.FillRoundedRectangle(rect, 4f, Color.Lerp(InkWashTheme.BaseDefault, qc, 0.12f));
                InkRenderHelper.DrawRoundedRectangle(rect, 4f, qc, 2f);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 24f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _glyph, rect, InkWashTheme.QualityTextColor(_quality),
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                if (!string.IsNullOrEmpty(_enhance))
                {
                    var bf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 10f).GetFont();
                    if (bf != null)
                        Render2D.DrawText(bf, _enhance, new Rectangle(24f, 38f, 26f, 12f), InkWashTheme.GoldBright,
                            TextAlignment.Far, TextAlignment.Center, TextWrapping.NoWrap);
                }
            }
        }

        /// <summary>品质标签（带边框小标签）。</summary>
        internal class QualityTag : Control
        {
            private readonly InkWashTheme.InkQuality _quality;
            private readonly string _text;

            public QualityTag(InkWashTheme.InkQuality quality, string text)
            {
                _quality = quality;
                _text = text;
                Size = new Float2(36f, 15f);
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                Color qc = InkWashTheme.QualityColor(_quality);
                var rect = new Rectangle(Float2.Zero, Size);
                InkRenderHelper.FillRoundedRectangle(rect, 2f, new Color(qc.R, qc.G, qc.B, 0.12f));
                InkRenderHelper.DrawRoundedRectangle(rect, 2f, new Color(qc.R, qc.G, qc.B, 0.5f), 1f);
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 10f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, rect, InkWashTheme.QualityTextColor(_quality),
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>耐久条：6px 高，青玉渐变填充。</summary>
        internal class DurabilityBar : Control
        {
            private readonly float _ratio;

            public DurabilityBar(float ratio)
            {
                _ratio = Mathf.Clamp(ratio, 0f, 1f);
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                InkRenderHelper.FillRoundedRectangle(rect, 3f,
                    new Color(InkWashTheme.JadePrimary.R, InkWashTheme.JadePrimary.G, InkWashTheme.JadePrimary.B, 0.12f));
                if (_ratio > 0f)
                {
                    InkRenderHelper.FillRoundedRectangle(new Rectangle(0f, 0f, Size.X * _ratio, Size.Y), 3f, InkWashTheme.JadePrimary);
                    InkRenderHelper.FillRoundedRectangle(new Rectangle(0f, 0f, Size.X * _ratio, Size.Y * 0.5f), 3f, InkWashTheme.JadeBright);
                }
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

        /// <summary>套装效果徽章：18x18，激活态青玉 + 对勾 / 未激活灰。</summary>
        internal class SetBadge : Control
        {
            private readonly bool _active;

            public SetBadge(bool active)
            {
                _active = active;
                Size = new Float2(18f, 18f);
                AutoFocus = false;
            }

            public override void Draw()
            {
                base.Draw();
                if (!Visible) return;
                var rect = new Rectangle(Float2.Zero, Size);
                Color c = _active ? InkWashTheme.JadePrimary : InkWashTheme.TextTertiary;
                InkRenderHelper.FillRoundedRectangle(rect, 3f, new Color(c.R, c.G, c.B, _active ? 0.15f : 0.06f));
                InkRenderHelper.DrawRoundedRectangle(rect, 3f, new Color(c.R, c.G, c.B, _active ? 0.6f : 0.2f), 1f);
                if (_active)
                {
                    var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f).GetFont();
                    if (font != null)
                        Render2D.DrawText(font, "✓", rect, c, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
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
    }
}
