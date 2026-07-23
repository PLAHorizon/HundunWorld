using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using Game.Combat.Skills;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Combat
{
    /// <summary>
    /// 传统模式战斗 HUD 页面（四象限固定式布局，对应 combat-hud-traditional.html）。
    /// 九大区域：
    /// <list type="bullet">
    ///   <item>顶部中央：目标信息（32 圆形血头像 + 8px 血条 + 精英标记）</item>
    ///   <item>左上：角色面板（56 圆形金边头像 + 等级徽章 + 血/气/修三条 + Buff 行）+ 队伍面板</item>
    ///   <item>右上：方形小地图（160 圆形 + 8 个 28px 圆形环绕按钮 + 坐标条）</item>
    ///   <item>右侧：任务追踪（3 任务 + 三色进度条）</item>
    ///   <item>底部中央偏上：17 入口导航栏（50x36，无外层面板）</item>
    ///   <item>底部中央：4 行技能栏（心法切换器 + 12 槽 x4 + 锁定钮）</item>
    ///   <item>左下：聊天框（6 Tab + 频道消息 + 输入行）</item>
    ///   <item>右下：功能按钮（切换沉浸模式 + 背包/角色/技能/设置）</item>
    /// </list>
    /// 边框统一 <c>--ink-border-gold</c>，心法切换器/头像带金辉光。
    /// 通过 <see cref="NavigationRequested"/> 事件向 <see cref="InkPageRouter"/> 暴露导航请求。
    /// </summary>
    public class CombatHudTraditionalPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量（1920x1080 参考分辨率，像素值对齐 combat-hud-traditional.html）
        // =======================================================================

        /// <summary>屏幕边缘统一边距（top/left/right/bottom 均 12px）</summary>
        private const float Edge = 12f;

        // --- 顶部中央：目标信息 ---
        private const float TargetWidth = 300f;
        private const float TargetPaddingX = 12f;
        private const float TargetPaddingY = 8f;
        private const float TargetAvatarSize = 32f;
        private const float TargetHeight = 58f;

        // --- 左上：角色面板 ---
        private const float CharWidth = 300f;
        private const float CharPadding = 10f;
        private const float CharAvatarSize = 56f;
        private const float LevelBadgeSize = 22f;
        private const float CharInfoX = 76f;      // 10 + 56 + 10(gap-2.5)
        private const float CharBarLabelW = 12f;
        private const float CharBarX = 92f;        // 76 + 12 + 4(gap-1)
        private const float CharBarW = 198f;       // 290 - 92
        private const float CharHeight = 104f;     // 74(buffY) + 20 + 10

        // --- 左上：队伍面板 ---
        private const float PartyWidth = 300f;
        private const float PartyPadding = 8f;
        private const float PartyAvatarSize = 24f;
        private const float PartyRowH = 30f;       // 24 + 6(gap-1.5)
        private const float PartyHeight = 150f;    // 28 + 4*30 - 6 + 8

        // --- 右上：小地图 ---
        private const float MinimapContainerSize = 200f;
        private const float MinimapMapSize = 160f;
        private const float MinimapBtnSize = 28f;
        private const float CoordBarHeight = 24f;

        // --- 右侧：任务追踪 ---
        private const float QuestWidth = 220f;
        private const float QuestTop = 260f;
        private const float QuestHeight = 117f;

        // --- 底部：导航栏 ---
        private const float NavBtnW = 50f;
        private const float NavBtnH = 36f;
        private const float NavGap = 2f;
        private const int NavCount = 17;
        private const float NavBarWidth = NavCount * NavBtnW + (NavCount - 1) * NavGap; // 882
        private const float NavBottom = 220f;

        // --- 底部中央：技能栏 ---
        private const float SkillPanelPadding = 4f;
        private const float HeartSize = 36f;
        private const float Slot1Size = 36f;       // 第 1 行槽 36x36
        private const float SlotSubH = 32f;        // 第 2-4 行槽 36x32
        private const float SlotGap = 2f;
        private const float SubRowIndent = 40f;    // margin-left 40px
        private const float LockBtnW = 24f;
        private const float SkillPanelW = 526f;    // 4+36+2+454+2+24+4
        private const float SkillPanelH = 146f;    // 4+36+2+32+2+32+2+32+4
        private const int SlotsPerRow = 12;

        // --- 左下：聊天框 ---
        private const float ChatWidth = 340f;
        private const float ChatContentH = 140f;
        private const float ChatHeight = 195f;     // 3+18+1+140+1+4+24+4

        // --- 右下：功能按钮 ---
        private const float FuncBtnW = 42f;
        private const float FuncBtnH = 38f;
        private const float FuncRowGap = 4f;
        private const float FuncColW = 4 * FuncBtnW + 3 * FuncRowGap; // 180
        private const float ToggleH = 22f;
        private const float FuncColH = ToggleH + 6f + FuncBtnH;       // 66

        // ===================================================================
        // 子控件引用
        // =======================================================================

        private InkPanel _targetPanel;
        private InkBar _targetHpBar;
        private Label _targetHpValueLabel;

        private InkPanel _charPanel;
        private InkCircle _charAvatar;
        private Label _charAvatarGlyph;
        private Label _charNameLabel;
        private Label _levelBadgeLabel;
        private InkBar _hpBar;
        private Label _hpValueLabel;
        private InkBar _mpBar;
        private Label _mpValueLabel;
        private InkBar _expBar;
        private Label _expPercentLabel;

        private InkPanel _partyPanel;

        private ContainerControl _minimapContainer;
        private InkMinimap _minimap;
        private Label _areaLabel;
        private Label _coordLabel;

        private InkPanel _questPanel;

        private ContainerControl _navRow;

        private InkPanel _skillPanel;

        private InkPanel _chatPanel;

        private ContainerControl _funcColumn;

        // ===================================================================
        // mock 数据（对齐 combat-hud-traditional.html）
        // =======================================================================

        private string _playerName = "逍遥客";
        private string _sectName = "武当";
        private int _playerLevel = 60;
        private int _hpCurrent = 12450;
        private int _hpMax = 15000;
        private int _mpCurrent = 800;
        private int _mpMax = 1000;
        private float _expRatio = 0.45f;

        // ===================================================================
        // 屏幕尺寸缓存与数据绑定
        // =======================================================================

        private Float2 _screenSize;
        private CharacterAttributesComponent _boundCharacter;
        private SkillBase[] _boundSkills;
        private float _minimapPlayerYaw;

        /// <summary>
        /// 导航请求事件。参数为目标页面的 dom-id。
        /// 由 <see cref="InkPageRouter"/> 订阅以执行页面跳转。
        /// </summary>
        public event Action<string> NavigationRequested;

        /// <summary>粒子系统（导航点击时触发鎏金迸发反馈）。</summary>
        public InkParticleSystem ParticleSystem { get; set; }

        /// <summary>小地图玩家朝向（角度制，0~360）。</summary>
        public float MinimapPlayerYaw
        {
            get => _minimapPlayerYaw;
            set => _minimapPlayerYaw = ((value % 360f) + 360f) % 360f;
        }

        /// <summary>绑定角色属性组件，驱动血/气条与名称/等级实时刷新。</summary>
        public void BindCharacter(CharacterAttributesComponent component)
        {
            _boundCharacter = component;
            RefreshBoundCharacterDisplay();
        }

        /// <summary>绑定技能槽数组（预留接口，当前技能栏使用 mock 配置）。</summary>
        public void BindSkills(SkillBase[] slots)
        {
            _boundSkills = slots;
        }

        private void RefreshBoundCharacterDisplay()
        {
            if (_boundCharacter == null) return;
            try
            {
                var c = _boundCharacter;
                if (_charNameLabel != null)
                    _charNameLabel.Text = !string.IsNullOrEmpty(c.Nickname) ? c.Nickname : "无名侠";
                if (_charAvatarGlyph != null && !string.IsNullOrEmpty(c.Nickname))
                    _charAvatarGlyph.Text = c.Nickname[0].ToString();
                if (_levelBadgeLabel != null)
                    _levelBadgeLabel.Text = c.Level.ToString();
                if (_hpBar != null && c.MaxHealth > 0f)
                    _hpBar.Value = Mathf.Saturate(c.CurrentHealth / c.MaxHealth);
                if (_hpValueLabel != null)
                    _hpValueLabel.Text = $"{(int)c.CurrentHealth}/{(int)c.MaxHealth}";
                if (_mpBar != null && c.MaxEnergy > 0f)
                    _mpBar.Value = Mathf.Saturate(c.CurrentEnergy / c.MaxEnergy);
                if (_mpValueLabel != null)
                    _mpValueLabel.Text = $"{(int)c.CurrentEnergy}/{(int)c.MaxEnergy}";
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CombatHudTraditionalPage] RefreshBoundCharacterDisplay: {ex.Message}");
            }
        }

        // ===================================================================
        // 构造函数
        // ===================================================================

        /// <summary>
        /// 构造函数：初始化全部九大区域，使用 mock 数据填充。
        /// </summary>
        public CombatHudTraditionalPage()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                BuildTargetInfo();
                BuildCharacterPanel();
                BuildPartyPanel();
                BuildMinimap();
                BuildQuestTracker();
                BuildNavBar();
                BuildSkillBars();
                BuildChatBox();
                BuildFunctionButtons();

                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CombatHudTraditionalPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 嵌套控件：圆形底 + 描边 + 辉光（头像/徽章/小地图按钮/心法切换器）
        // ===================================================================

        /// <summary>
        /// 圆形控件：FillCircle 底色 + 可选叠加色（近似径向渐变）+ 可选外辉光 + 圆形描边。
        /// 子控件（如居中字符 Label）正常绘制。
        /// </summary>
        private class InkCircle : ContainerControl
        {
            /// <summary>点击事件（左键抬起且在控件内触发）</summary>
            public event Action Clicked;

            /// <summary>底色填充</summary>
            public Color FillColor { get; set; } = InkWashTheme.BaseElevated;

            /// <summary>描边颜色（Transparent 表示无描边）</summary>
            public Color BorderColor { get; set; } = Color.Transparent;

            /// <summary>描边厚度（像素）</summary>
            public float BorderThickness { get; set; } = 1f;

            /// <summary>外辉光颜色（Transparent 表示无辉光，对应 --ink-shadow-gold）</summary>
            public Color GlowColor { get; set; } = Color.Transparent;

            /// <summary>叠加色（近似径向/线性渐变的高光层）</summary>
            public Color OverlayColor { get; set; } = Color.Transparent;

            /// <summary>叠加色半径比例（0~1，1=覆盖整圆）</summary>
            public float OverlayRadiusRatio { get; set; } = 1f;

            public InkCircle()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                var center = new Float2(Width * 0.5f, Height * 0.5f);
                float r = Mathf.Min(Width, Height) * 0.5f;
                if (r <= 0f)
                {
                    base.Draw();
                    return;
                }

                // 外辉光（3px 软环）
                if (GlowColor.A > 0f)
                    InkRenderHelper.DrawCircle(center, r + 1.5f, GlowColor, 3f);

                // 底色 + 叠加高光
                InkRenderHelper.FillCircle(center, r, FillColor);
                if (OverlayColor.A > 0f && OverlayRadiusRatio > 0f)
                    InkRenderHelper.FillCircle(center, r * OverlayRadiusRatio, OverlayColor);

                // 子控件（跳过基类背景绘制）
                var savedBg = BackgroundColor;
                BackgroundColor = Color.Transparent;
                base.Draw();
                BackgroundColor = savedBg;

                // 圆形描边
                if (BorderColor.A > 0f && BorderThickness > 0f)
                    InkRenderHelper.DrawCircle(center, r - BorderThickness * 0.5f, BorderColor, BorderThickness);
            }

            /// <inheritdoc />
            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location))
                {
                    Clicked?.Invoke();
                    return true;
                }
                return false;
            }
        }

        // ===================================================================
        // 嵌套控件：圆角矩形底 + 描边 + 悬停叠加（槽位/标签/输入框/功能钮）
        // ===================================================================

        /// <summary>
        /// 圆角矩形单元：FillRoundedRectangle 底色 + 1px 描边 + 可选悬停叠加色。
        /// </summary>
        private class InkRoundCell : ContainerControl
        {
            /// <summary>底色填充</summary>
            public Color FillColor { get; set; } = InkWashTheme.BaseElevated;

            /// <summary>描边颜色（Transparent 表示无描边）</summary>
            public Color BorderColor { get; set; } = Color.Transparent;

            /// <summary>圆角半径（默认 2px = --radius-2）</summary>
            public float Radius { get; set; } = InkWashTheme.RadiusSm;

            /// <summary>悬停叠加色（对应 hover:bg-[var(--ink-bg-hover)]）</summary>
            public Color HoverOverlayColor { get; set; } = Color.Transparent;

            /// <summary>是否处于悬停状态</summary>
            protected bool Hovered;

            public InkRoundCell()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                var bounds = new Rectangle(0, 0, Width, Height);

                if (FillColor.A > 0f)
                    InkRenderHelper.FillRoundedRectangle(bounds, Radius, FillColor);
                if (Hovered && HoverOverlayColor.A > 0f)
                    InkRenderHelper.FillRoundedRectangle(bounds, Radius, HoverOverlayColor);

                var savedBg = BackgroundColor;
                BackgroundColor = Color.Transparent;
                base.Draw();
                BackgroundColor = savedBg;

                if (BorderColor.A > 0f)
                    InkRenderHelper.DrawRoundedRectangle(bounds, Radius, BorderColor, 1f);
            }
        }

        /// <summary>
        /// 导航/功能按钮：圆角矩形底 + 悬停叠加 + 点击事件（携带 dom-id）。
        /// </summary>
        private class InkNavButton : InkRoundCell
        {
            /// <summary>点击事件</summary>
            public event Action<InkNavButton> Clicked;

            /// <summary>目标页面 dom-id</summary>
            public string DomId { get; set; }

            public InkNavButton(float w, float h)
            {
                Size = new Float2(w, h);
                FillColor = InkWashTheme.Panel;
                BorderColor = InkWashTheme.BorderGold;
                HoverOverlayColor = InkWashTheme.BgHover;
            }

            /// <inheritdoc />
            public override void OnMouseEnter(Float2 location)
            {
                Hovered = true;
            }

            /// <inheritdoc />
            public override void OnMouseLeave()
            {
                Hovered = false;
            }

            /// <inheritdoc />
            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location))
                {
                    Clicked?.Invoke(this);
                    return true;
                }
                return false;
            }
        }

        // ===================================================================
        // 辅助构造方法
        // ===================================================================

        /// <summary>创建标签（指定字体角色/字号/颜色/对齐）。</summary>
        private Label MakeLabel(string text, float x, float y, float w, float h,
            Color color, float size, InkWashTheme.FontRole role,
            TextAlignment hAlign = TextAlignment.Near)
        {
            return new Label
            {
                Text = text,
                Location = new Float2(x, y),
                Size = new Float2(w, h),
                TextColor = color,
                Font = InkRenderHelper.GetFontRef(role, size),
                HorizontalAlignment = hAlign,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
        }

        /// <summary>创建进度条（指定槽色/边框色/填充变体）。</summary>
        private InkBar MakeBar(float x, float y, float w, float h, float val,
            InkBarFillVariant variant, Color slotColor, Color borderColor)
        {
            return new InkBar
            {
                Location = new Float2(x, y),
                Size = new Float2(w, h),
                Value = val,
                FillVariant = variant,
                SlotColor = slotColor,
                BorderColor = borderColor,
                AnchorPreset = AnchorPresets.TopLeft,
            };
        }

        /// <summary>创建圆形头像/徽章（底色 + 描边 + 可选辉光/叠加 + 居中字符）。</summary>
        private InkCircle MakeCircle(float x, float y, float size,
            Color fill, Color border, float borderThickness,
            string glyph, Color glyphColor, float glyphSize, InkWashTheme.FontRole glyphRole,
            Color glow = default, Color overlay = default, float overlayRatio = 1f)
        {
            var circle = new InkCircle
            {
                Location = new Float2(x, y),
                Size = new Float2(size, size),
                FillColor = fill,
                BorderColor = border,
                BorderThickness = borderThickness,
                GlowColor = glow,
                OverlayColor = overlay,
                OverlayRadiusRatio = overlayRatio,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            if (glyph != null)
            {
                var lbl = new Label
                {
                    AnchorPreset = AnchorPresets.StretchAll,
                    Text = glyph,
                    TextColor = glyphColor,
                    Font = InkRenderHelper.GetFontRef(glyphRole, glyphSize),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                circle.AddChild(lbl);
            }
            return circle;
        }

        /// <summary>创建圆角矩形单元（底色 + 描边 + 居中字符）。</summary>
        private InkRoundCell MakeCell(float x, float y, float w, float h,
            Color fill, Color border, float radius,
            string glyph, Color glyphColor, float glyphSize, InkWashTheme.FontRole glyphRole)
        {
            var cell = new InkRoundCell
            {
                Location = new Float2(x, y),
                Size = new Float2(w, h),
                FillColor = fill,
                BorderColor = border,
                Radius = radius,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            if (glyph != null)
            {
                var lbl = new Label
                {
                    AnchorPreset = AnchorPresets.StretchAll,
                    Text = glyph,
                    TextColor = glyphColor,
                    Font = InkRenderHelper.GetFontRef(glyphRole, glyphSize),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                cell.AddChild(lbl);
            }
            return cell;
        }

        // ===================================================================
        // 区域构造：顶部中央目标信息
        // ===================================================================

        /// <summary>
        /// 顶部中央目标信息：32 圆形血头像 + 名称/Lv + 8px 血条（金边）+ 数值/精英标记。
        /// </summary>
        private void BuildTargetInfo()
        {
            _targetPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(TargetWidth, TargetHeight),
                Radius = InkWashTheme.RadiusLg,
            };

            // 32 圆形头像（blood-faint 底 + blood-primary 边 + 14px 血亮字）
            var avatar = MakeCircle(TargetPaddingX, TargetPaddingY, TargetAvatarSize,
                InkWashTheme.BloodFaint, InkWashTheme.BloodPrimary, 1f,
                "魁", InkWashTheme.BloodBright, 14f, InkWashTheme.FontRole.Display);
            _targetPanel.AddChild(avatar);

            // 信息列
            float colX = TargetPaddingX + TargetAvatarSize + 8f; // gap-2
            float colW = TargetWidth - TargetPaddingX - colX;

            // 名称 13px 楷书 + Lv.62 11px DIN 血亮
            _targetPanel.AddChild(MakeLabel("黑风寨首领", colX, TargetPaddingY, colW - 60f, 16f,
                InkWashTheme.TextDefault, 13f, InkWashTheme.FontRole.Display));
            _targetPanel.AddChild(MakeLabel("Lv.62", colX + colW - 60f, TargetPaddingY, 60f, 16f,
                InkWashTheme.BloodBright, 11f, InkWashTheme.FontRole.Number, TextAlignment.Far));

            // HP 条 8px（bg-abyss + border-gold + blood 三段渐变，65%）
            float barY = TargetPaddingY + 16f + 4f; // mt-1
            _targetHpBar = MakeBar(colX, barY, colW, 8f, 0.65f,
                InkBarFillVariant.Blood, InkWashTheme.Abyss, InkWashTheme.BorderGold);
            _targetPanel.AddChild(_targetHpBar);

            // 数值行：9750/15000（DIN 9px）+ 精英（9px）
            float valY = barY + 8f + 2f; // mt-0.5
            _targetHpValueLabel = MakeLabel("9750/15000", colX, valY, colW * 0.5f, 12f,
                InkWashTheme.TextTertiary, 9f, InkWashTheme.FontRole.Number);
            _targetPanel.AddChild(_targetHpValueLabel);
            _targetPanel.AddChild(MakeLabel("精英", colX + colW * 0.5f, valY, colW * 0.5f, 12f,
                InkWashTheme.TextTertiary, 9f, InkWashTheme.FontRole.Body, TextAlignment.Far));

            AddChild(_targetPanel);
        }

        // ===================================================================
        // 区域构造：左上角色面板
        // ===================================================================

        /// <summary>
        /// 左上角色面板：56 圆形金边头像（辉光）+ 22 圆形等级徽章 + 名称/门派 +
        /// 血(12px 金边)/气(10px 青边)/修(4px 无边) 三条 + 4 个 Buff 图标。
        /// </summary>
        private void BuildCharacterPanel()
        {
            _charPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(CharWidth, CharHeight),
                Radius = InkWashTheme.RadiusLg,
            };

            // 56 圆形头像（mist→paper 近似：BaseTertiary 底 + BgMist 叠加，2px 金边 + 金辉光）
            _charAvatar = MakeCircle(CharPadding, CharPadding, CharAvatarSize,
                InkWashTheme.BaseTertiary, InkWashTheme.GoldPrimary, 2f,
                null, Color.Transparent, 0f, InkWashTheme.FontRole.Display,
                glow: InkWashTheme.GoldGlow, overlay: InkWashTheme.BgMist, overlayRatio: 1f);
            _charAvatarGlyph = new Label
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Text = _playerName.Length > 0 ? _playerName[0].ToString() : "侠",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 28f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _charAvatar.AddChild(_charAvatarGlyph);
            _charAvatar.Clicked += () => RequestNavigation(InkPageDomIds.NavCharacterPanel);
            _charPanel.AddChild(_charAvatar);

            // 22 圆形等级徽章（右下 -4px 偏移，bg-ink + 金边 + DIN 10 金亮）
            float badgeX = CharPadding + CharAvatarSize - LevelBadgeSize + 4f;
            float badgeY = CharPadding + CharAvatarSize - LevelBadgeSize + 4f;
            var badge = MakeCircle(badgeX, badgeY, LevelBadgeSize,
                InkWashTheme.PanelSolid, InkWashTheme.GoldPrimary, 1f,
                _playerLevel.ToString(), InkWashTheme.GoldBright, 10f, InkWashTheme.FontRole.Number);
            _levelBadgeLabel = (Label)badge.Children[0];
            _charPanel.AddChild(badge);

            // 名称 14px 楷书 + 门派 10px 青亮
            _charNameLabel = MakeLabel(_playerName, CharInfoX, CharPadding, 140f, 18f,
                InkWashTheme.TextDefault, 14f, InkWashTheme.FontRole.Display);
            _charPanel.AddChild(_charNameLabel);
            _charPanel.AddChild(MakeLabel(_sectName, CharInfoX + 140f, CharPadding, 50f, 18f,
                InkWashTheme.JadeBright, 10f, InkWashTheme.FontRole.Body, TextAlignment.Far));

            // 血条行 y=32：标签"血" + 12px 条（金边，数值居中覆盖）
            float hpY = CharPadding + 18f + 4f;
            _charPanel.AddChild(MakeLabel("血", CharInfoX, hpY, CharBarLabelW, 12f,
                InkWashTheme.TextSecondary, 9f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            _hpBar = MakeBar(CharBarX, hpY, CharBarW, 12f, 0.83f,
                InkBarFillVariant.Blood, InkWashTheme.Abyss, InkWashTheme.BorderGold);
            _charPanel.AddChild(_hpBar);
            _hpValueLabel = MakeLabel($"{_hpCurrent}/{_hpMax}", CharBarX, hpY, CharBarW, 12f,
                InkWashTheme.TextDefault, 9f, InkWashTheme.FontRole.Number, TextAlignment.Center);
            _charPanel.AddChild(_hpValueLabel);

            // 气条行 y=48：标签"气" + 10px 条（青边，数值居中覆盖）
            float mpY = hpY + 12f + 4f;
            _charPanel.AddChild(MakeLabel("气", CharInfoX, mpY, CharBarLabelW, 10f,
                InkWashTheme.TextSecondary, 9f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            _mpBar = MakeBar(CharBarX, mpY, CharBarW, 10f, 0.80f,
                InkBarFillVariant.Jade, InkWashTheme.Abyss, InkWashTheme.BorderJade);
            _charPanel.AddChild(_mpBar);
            _mpValueLabel = MakeLabel($"{_mpCurrent}/{_mpMax}", CharBarX, mpY, CharBarW, 10f,
                InkWashTheme.TextDefault, 8f, InkWashTheme.FontRole.Number, TextAlignment.Center);
            _charPanel.AddChild(_mpValueLabel);

            // 修为行 y=62：标签"修" + 4px 条（无边框，金渐变）+ 45% 右侧
            float expY = mpY + 10f + 4f;
            _charPanel.AddChild(MakeLabel("修", CharInfoX, expY, CharBarLabelW, 4f,
                InkWashTheme.TextTertiary, 8f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            float expBarW = CharBarW - 28f; // 预留 4+24 给百分比
            _expBar = MakeBar(CharBarX, expY, expBarW, 4f, _expRatio,
                InkBarFillVariant.Gold, InkWashTheme.Abyss, Color.Transparent);
            _charPanel.AddChild(_expBar);
            _expPercentLabel = MakeLabel($"{(int)(_expRatio * 100f)}%", CharBarX + expBarW + 4f, expY - 2f, 24f, 8f,
                InkWashTheme.TextTertiary, 8f, InkWashTheme.FontRole.Number);
            _charPanel.AddChild(_expPercentLabel);

            // Buff 行 y=74：4 个 20x20 圆角 2px 图标（极金/罡青/风血/+）
            float buffY = CharPadding + CharAvatarSize + 8f; // mt-2
            var buffDefs = new[]
            {
                (glyph: "极", fill: InkWashTheme.GoldTrace, border: InkWashTheme.GoldFaint, color: InkWashTheme.GoldBright),
                (glyph: "罡", fill: InkWashTheme.JadeFaint, border: InkWashTheme.BorderJade, color: InkWashTheme.JadeBright),
                (glyph: "风", fill: InkWashTheme.BloodFaint, border: InkWashTheme.BloodPrimary, color: InkWashTheme.BloodBright),
                (glyph: "+", fill: InkWashTheme.BaseElevated, border: InkWashTheme.BorderFaint, color: InkWashTheme.TextTertiary),
            };
            float buffX = CharPadding;
            foreach (var b in buffDefs)
            {
                _charPanel.AddChild(MakeCell(buffX, buffY, 20f, 20f,
                    b.fill, b.border, InkWashTheme.RadiusSm,
                    b.glyph, b.color, 10f, InkWashTheme.FontRole.Display));
                buffX += 20f + 4f; // gap-1
            }

            AddChild(_charPanel);
        }

        // ===================================================================
        // 区域构造：左上队伍面板
        // ===================================================================

        /// <summary>
        /// 左上队伍面板：标题行（队伍 + 4/5）+ 4 成员行（24 圆形品质边头像 + 名称/Lv + 5px 血条）。
        /// </summary>
        private void BuildPartyPanel()
        {
            _partyPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(PartyWidth, PartyHeight),
                Radius = InkWashTheme.RadiusLg,
            };

            // 标题行：users 图标（以"队"字替代）+ 队伍 + 4/5
            _partyPanel.AddChild(MakeLabel("队", PartyPadding, PartyPadding, 12f, 14f,
                InkWashTheme.GoldPrimary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            _partyPanel.AddChild(MakeLabel("队伍", PartyPadding + 12f + 6f, PartyPadding, 80f, 14f,
                InkWashTheme.TextSecondary, 11f, InkWashTheme.FontRole.Display));
            _partyPanel.AddChild(MakeLabel("4/5", PartyWidth - PartyPadding - 42f, PartyPadding, 42f, 14f,
                InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Number, TextAlignment.Far));

            // 成员行（头像品质边框：青→jade / 红→blood / 醉→gold / 玉→epic）
            var members = new[]
            {
                (glyph: "青", border: InkWashTheme.JadePrimary, color: InkWashTheme.JadeBright, name: "青衣客", level: "Lv.58", hp: 0.85f),
                (glyph: "红", border: InkWashTheme.BloodPrimary, color: InkWashTheme.BloodBright, name: "红袖招", level: "Lv.57", hp: 0.92f),
                (glyph: "醉", border: InkWashTheme.GoldPrimary, color: InkWashTheme.GoldBright, name: "醉道人", level: "Lv.60", hp: 1.0f),
                (glyph: "玉", border: InkWashTheme.QualityEpic, color: InkWashTheme.QualityEpic, name: "玉面狐", level: "Lv.55", hp: 0.70f),
            };

            float rowY = PartyPadding + 14f + 6f; // mb-1.5
            float nameX = PartyPadding + PartyAvatarSize + 6f; // gap-1.5
            float nameW = PartyWidth - PartyPadding - nameX - 42f;
            float barW = PartyWidth - PartyPadding - nameX;

            foreach (var m in members)
            {
                // 24 圆形头像（bg-elevated + 品质边框）
                _partyPanel.AddChild(MakeCircle(PartyPadding, rowY, PartyAvatarSize,
                    InkWashTheme.BaseElevated, m.border, 1f,
                    m.glyph, m.color, 11f, InkWashTheme.FontRole.Display));

                // 名称 10px + Lv 9px
                _partyPanel.AddChild(MakeLabel(m.name, nameX, rowY + 1f, nameW, 12f,
                    InkWashTheme.TextDefault, 10f, InkWashTheme.FontRole.Body));
                _partyPanel.AddChild(MakeLabel(m.level, nameX + nameW, rowY + 1f, 42f, 12f,
                    InkWashTheme.TextTertiary, 9f, InkWashTheme.FontRole.Number, TextAlignment.Far));

                // HP 条 5px（bg-abyss，无边框，blood-deep→primary）
                _partyPanel.AddChild(MakeBar(nameX, rowY + 15f, barW, 5f, m.hp,
                    InkBarFillVariant.Blood, InkWashTheme.Abyss, Color.Transparent));

                rowY += PartyRowH;
            }

            AddChild(_partyPanel);
        }

        // ===================================================================
        // 区域构造：右上方形小地图
        // ===================================================================

        /// <summary>
        /// 右上方形小地图：200x200 容器内 160 圆形地图（20,20）+ 8 个 28px 圆形环绕按钮 + 坐标条。
        /// </summary>
        private void BuildMinimap()
        {
            _minimapContainer = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(MinimapContainerSize, MinimapContainerSize + 4f + CoordBarHeight),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };

            // 160 圆形地图（InkMinimap 内置径向渐变底 + 2px 金边 + 标记点）
            _minimap = new InkMinimap
            {
                Location = new Float2(20f, 20f),
                Size = new Float2(MinimapMapSize, MinimapMapSize),
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _minimap.AddLandmark(0.1f, 0.2f, 0.3f, new Color(InkWashTheme.JadeFaint.R, InkWashTheme.JadeFaint.G, InkWashTheme.JadeFaint.B, 0.5f));
            _minimap.AddLandmark(-0.2f, -0.15f, 0.25f, new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.15f));
            _minimap.AddLandmark(0.4f, -0.3f, 0.15f, new Color(InkWashTheme.JadeFaint.R, InkWashTheme.JadeFaint.G, InkWashTheme.JadeFaint.B, 0.3f));
            _minimap.AddEntity(InkMinimapEntityType.Player, 0f, 0f);
            _minimap.AddEntity(InkMinimapEntityType.Friendly, 0.24f, -0.32f);
            _minimap.AddEntity(InkMinimapEntityType.Enemy, -0.24f, 0.36f);
            _minimap.AddEntity(InkMinimapEntityType.NPC, 0.44f, -0.16f);
            _minimap.AddEntity(InkMinimapEntityType.NPC, -0.1f, 0.5f);
            _minimapContainer.AddChild(_minimap);

            // 8 个 28px 圆形功能按钮环绕（bg-ink + border-gold + 金字）
            var btnDefs = new[]
            {
                (x: 86f, y: 0f, glyph: "图", domId: InkPageDomIds.NavWorldMap),
                (x: 162f, y: 10f, glyph: "+", domId: (string)null),
                (x: 172f, y: 86f, glyph: "−", domId: (string)null),
                (x: 162f, y: 162f, glyph: "任", domId: InkPageDomIds.NavQuestLog),
                (x: 86f, y: 172f, glyph: "队", domId: (string)null),
                (x: 10f, y: 162f, glyph: "邮", domId: InkPageDomIds.NavSocialMail),
                (x: 0f, y: 86f, glyph: "设", domId: InkPageDomIds.NavSettings),
                (x: 10f, y: 10f, glyph: "助", domId: (string)null),
            };

            foreach (var def in btnDefs)
            {
                var btn = MakeCircle(def.x, def.y, MinimapBtnSize,
                    InkWashTheme.PanelSolid, InkWashTheme.BorderGold, 1f,
                    def.glyph, InkWashTheme.GoldPrimary, 12f, InkWashTheme.FontRole.Body);
                if (def.domId != null)
                {
                    string target = def.domId;
                    btn.Clicked += () => RequestNavigation(target);
                }
                _minimapContainer.AddChild(btn);
            }

            // 坐标条（200x24，radius-4，洛阳城 + 坐标）
            var coordBar = new InkPanel
            {
                Location = new Float2(0f, MinimapContainerSize + 4f), // mt-1
                Size = new Float2(MinimapContainerSize, CoordBarHeight),
                Radius = InkWashTheme.RadiusMd,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _areaLabel = MakeLabel("洛阳城", 10f, 4f, 100f, 16f,
                InkWashTheme.GoldBright, 11f, InkWashTheme.FontRole.Display);
            coordBar.AddChild(_areaLabel);
            _coordLabel = MakeLabel("1234, 567", 110f, 4f, 80f, 16f,
                InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Number, TextAlignment.Far);
            coordBar.AddChild(_coordLabel);
            _minimapContainer.AddChild(coordBar);

            AddChild(_minimapContainer);
        }

        // ===================================================================
        // 区域构造：右侧任务追踪
        // ===================================================================

        /// <summary>
        /// 右侧任务追踪：标题行（任务追踪）+ 3 任务（名 + 进度计数 + 3px 三色进度条）。
        /// </summary>
        private void BuildQuestTracker()
        {
            _questPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(QuestWidth, QuestHeight),
                Radius = InkWashTheme.RadiusLg,
            };

            // 标题行：scroll-text 图标（以"任"字替代）+ 任务追踪 12px 金亮
            _questPanel.AddChild(MakeLabel("任", 10f, 8f, 12f, 14f,
                InkWashTheme.GoldPrimary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            _questPanel.AddChild(MakeLabel("任务追踪", 28f, 8f, 120f, 14f,
                InkWashTheme.GoldBright, 12f, InkWashTheme.FontRole.Display));

            // 3 任务：进度条颜色分别为青/金/血
            var quests = new[]
            {
                (name: "黑风寨剿匪", count: "8/10", progress: 0.80f, color: InkWashTheme.JadeBright, variant: InkBarFillVariant.Jade),
                (name: "寻访名剑", count: "3/5", progress: 0.60f, color: InkWashTheme.GoldBright, variant: InkBarFillVariant.Gold),
                (name: "门派日常", count: "1/3", progress: 0.33f, color: InkWashTheme.BloodBright, variant: InkBarFillVariant.Blood),
            };

            float qY = 30f;
            foreach (var q in quests)
            {
                _questPanel.AddChild(MakeLabel(q.name, 10f, qY, 140f, 16f,
                    InkWashTheme.TextDefault, 11f, InkWashTheme.FontRole.Display));
                _questPanel.AddChild(MakeLabel(q.count, 150f, qY, 60f, 16f,
                    q.color, 9f, InkWashTheme.FontRole.Number, TextAlignment.Far));
                _questPanel.AddChild(MakeBar(10f, qY + 18f, 200f, 3f, q.progress,
                    q.variant, InkWashTheme.Abyss, Color.Transparent));
                qY += 29f; // 21 + gap-2(8)
            }

            AddChild(_questPanel);
        }

        // ===================================================================
        // 区域构造：底部导航栏（17 入口）
        // ===================================================================

        /// <summary>
        /// 底部导航栏：17 个 50x36 按钮（bg-panel + border-gold + radius-2，图标 13px + 标签 9px），无外层面板。
        /// </summary>
        private void BuildNavBar()
        {
            _navRow = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(NavBarWidth, NavBtnH),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };

            var entries = new[]
            {
                (glyph: "人", label: "角色", domId: InkPageDomIds.NavCharacterPanel),
                (glyph: "技", label: "技能", domId: InkPageDomIds.NavSkillPanel),
                (glyph: "包", label: "背包", domId: InkPageDomIds.NavInventory),
                (glyph: "任", label: "任务", domId: InkPageDomIds.NavQuestLog),
                (glyph: "图", label: "地图", domId: InkPageDomIds.NavWorldMap),
                (glyph: "帮", label: "帮会", domId: InkPageDomIds.NavSocialGuild),
                (glyph: "商", label: "商城", domId: InkPageDomIds.NavSocialShop),
                (glyph: "路", label: "寻路", domId: InkPageDomIds.NavCompass),
                (glyph: "强", label: "强化", domId: InkPageDomIds.NavEquipmentEnhance),
                (glyph: "锻", label: "锻造", domId: InkPageDomIds.NavCrafting),
                (glyph: "骑", label: "坐骑", domId: InkPageDomIds.NavMountPet),
                (glyph: "友", label: "好友", domId: InkPageDomIds.NavFriends),
                (glyph: "邮", label: "邮件", domId: InkPageDomIds.NavSocialMail),
                (glyph: "榜", label: "排行", domId: InkPageDomIds.NavLeaderboard),
                (glyph: "师", label: "师门", domId: InkPageDomIds.NavMentor),
                (glyph: "成", label: "成就", domId: InkPageDomIds.NavAchievement),
                (glyph: "境", label: "秘境", domId: InkPageDomIds.NavDungeonEntry),
            };

            float cursorX = 0f;
            foreach (var e in entries)
            {
                var btn = new InkNavButton(NavBtnW, NavBtnH)
                {
                    Location = new Float2(cursorX, 0f),
                    DomId = e.domId,
                    AnchorPreset = AnchorPresets.TopLeft,
                };

                // 图标 13px 金 + 标签 9px（垂直居中分布）
                btn.AddChild(MakeLabel(e.glyph, 0f, 6f, NavBtnW, 14f,
                    InkWashTheme.GoldPrimary, 13f, InkWashTheme.FontRole.Body, TextAlignment.Center));
                btn.AddChild(MakeLabel(e.label, 0f, 21f, NavBtnW, 9f,
                    InkWashTheme.TextSecondary, 9f, InkWashTheme.FontRole.Body, TextAlignment.Center));

                btn.Clicked += b => RequestNavigation(b.DomId);
                _navRow.AddChild(btn);
                cursorX += NavBtnW + NavGap;
            }

            AddChild(_navRow);
        }

        // ===================================================================
        // 区域构造：底部中央 4 行技能栏
        // ===================================================================

        /// <summary>
        /// 底部中央技能栏：第 1 行心法切换器（36 圆形金辉光）+ 12 槽 36x36（品质边框）+ 锁定钮；
        /// 第 2-4 行各 12 槽 36x32（左缩进 40px）。
        /// </summary>
        private void BuildSkillBars()
        {
            _skillPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(SkillPanelW, SkillPanelH),
                Radius = InkWashTheme.RadiusLg,
            };

            // 心法切换器：36 圆形，radial(gold-faint → bg-ink) 近似 + 2px 金边 + 金辉光
            float row1Y = SkillPanelPadding;
            var heart = MakeCircle(SkillPanelPadding, row1Y, HeartSize,
                InkWashTheme.PanelSolid, InkWashTheme.GoldPrimary, 2f,
                "心", InkWashTheme.GoldBright, 14f, InkWashTheme.FontRole.Display,
                glow: InkWashTheme.GoldGlow, overlay: InkWashTheme.GoldFaint, overlayRatio: 0.75f);
            _skillPanel.AddChild(heart);

            // 第 1 行：12 槽 36x36（品质边框 + 16px 字符），槽 1 带快捷键 "1"
            var row1 = new (string glyph, Color color, Color border, string hotkey)[]
            {
                ("剑", InkWashTheme.QualityRare, InkWashTheme.QualityRare, "1"),
                ("气", InkWashTheme.QualityUncommon, InkWashTheme.QualityUncommon, null),
                ("掌", InkWashTheme.QualityEpic, InkWashTheme.QualityEpic, null),
                ("绝", InkWashTheme.QualityLegendary, InkWashTheme.QualityLegendary, null),
                ("步", InkWashTheme.GoldPrimary, InkWashTheme.BorderGold, null),
                ("疗", InkWashTheme.JadeBright, InkWashTheme.JadePrimary, null),
                ("空", InkWashTheme.TextTertiary, InkWashTheme.BorderFaint, null),
                ("空", InkWashTheme.TextTertiary, InkWashTheme.BorderFaint, null),
                ("空", InkWashTheme.TextTertiary, InkWashTheme.BorderFaint, null),
                ("空", InkWashTheme.TextTertiary, InkWashTheme.BorderFaint, null),
                ("空", InkWashTheme.TextTertiary, InkWashTheme.BorderFaint, null),
                ("空", InkWashTheme.TextTertiary, InkWashTheme.BorderFaint, null),
            };

            float slotX = SkillPanelPadding + HeartSize + 2f; // mr-0.5
            for (int i = 0; i < SlotsPerRow; i++)
            {
                var slot = MakeCell(slotX, row1Y, Slot1Size, Slot1Size,
                    InkWashTheme.BaseElevated, row1[i].border, InkWashTheme.RadiusSm,
                    row1[i].glyph, row1[i].color, 16f, InkWashTheme.FontRole.Display);
                if (row1[i].hotkey != null)
                {
                    slot.AddChild(MakeLabel(row1[i].hotkey, Slot1Size - 10f, Slot1Size - 10f, 8f, 8f,
                        InkWashTheme.TextTertiary, 8f, InkWashTheme.FontRole.Number, TextAlignment.Far));
                }
                _skillPanel.AddChild(slot);
                slotX += Slot1Size + SlotGap;
            }

            // 锁定钮 24x36（bg-ink + border-faint）
            _skillPanel.AddChild(MakeCell(slotX + 2f, row1Y, LockBtnW, Slot1Size,
                InkWashTheme.PanelSolid, InkWashTheme.BorderFaint, InkWashTheme.RadiusSm,
                "锁", InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Body));

            // 第 2-4 行：各 12 槽 36x32，左缩进 40px，14px 字符
            var empty = (glyph: "空", color: InkWashTheme.TextTertiary, border: InkWashTheme.BorderFaint);
            var subRows = new (string glyph, Color color, Color border)[][]
            {
                new[]
                {
                    ("刀", InkWashTheme.QualityRare, InkWashTheme.QualityRare),
                    ("盾", InkWashTheme.QualityUncommon, InkWashTheme.QualityUncommon),
                    ("闪", InkWashTheme.GoldPrimary, InkWashTheme.BorderGold),
                    ("破", InkWashTheme.BloodBright, InkWashTheme.BloodPrimary),
                    ("御", InkWashTheme.JadeBright, InkWashTheme.JadePrimary),
                    empty, empty, empty, empty, empty, empty, empty,
                },
                new[]
                {
                    ("丹", InkWashTheme.QualityUncommon, InkWashTheme.QualityUncommon),
                    ("药", InkWashTheme.QualityUncommon, InkWashTheme.QualityUncommon),
                    ("符", InkWashTheme.GoldPrimary, InkWashTheme.BorderGold),
                    empty, empty, empty, empty, empty, empty, empty, empty, empty,
                },
                new[]
                {
                    empty, empty, empty, empty, empty, empty,
                    empty, empty, empty, empty, empty, empty,
                },
            };

            float subY = row1Y + Slot1Size + 2f; // mb-0.5
            foreach (var row in subRows)
            {
                float sx = SkillPanelPadding + SubRowIndent;
                foreach (var s in row)
                {
                    _skillPanel.AddChild(MakeCell(sx, subY, Slot1Size, SlotSubH,
                        InkWashTheme.BaseElevated, s.border, InkWashTheme.RadiusSm,
                        s.glyph, s.color, 14f, InkWashTheme.FontRole.Display));
                    sx += Slot1Size + SlotGap;
                }
                subY += SlotSubH + 2f;
            }

            AddChild(_skillPanel);
        }

        // ===================================================================
        // 区域构造：左下聊天框
        // ===================================================================

        /// <summary>
        /// 左下聊天框：6 频道 Tab（近聊激活态金边）+ 分隔线 + 8 条频道消息（前缀着色）+
        /// 输入行（频道选择 + 输入框 + 表情钮）。
        /// </summary>
        private void BuildChatBox()
        {
            _chatPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(ChatWidth, ChatHeight),
                Radius = InkWashTheme.RadiusLg,
            };

            // Tab 行（padding 3 4 0 4，激活态：bg-elevated + border-gold + 金亮字）
            var tabs = new[] { "近聊", "世界", "门派", "队伍", "密聊", "系统" };
            float tabX = 4f;
            for (int i = 0; i < tabs.Length; i++)
            {
                bool active = i == 0;
                _chatPanel.AddChild(MakeCell(tabX, 3f, 38f, 18f,
                    active ? InkWashTheme.BaseElevated : Color.Transparent,
                    active ? InkWashTheme.BorderGold : Color.Transparent,
                    InkWashTheme.RadiusSm,
                    tabs[i], active ? InkWashTheme.GoldBright : InkWashTheme.TextSecondary,
                    10f, InkWashTheme.FontRole.Body));
                tabX += 40f;
            }

            // Tab 下分隔线
            _chatPanel.AddChild(new ContainerControl
            {
                Location = new Float2(4f, 22f),
                Size = new Float2(ChatWidth - 8f, 1f),
                BackgroundColor = InkWashTheme.BorderFaint,
                AnchorPreset = AnchorPresets.TopLeft,
            });

            // 消息区（高 140，padding 4 8，频道前缀着色：门派青/世界金/系统灰/队伍血）
            var messages = new[]
            {
                (prefix: "[门派]", color: InkWashTheme.JadeBright, text: " 醉道人：今晚帮战几时开？"),
                (prefix: "[世界]", color: InkWashTheme.GoldBright, text: " 剑无痕：出售六十级紫剑，洛阳城东摆摊"),
                (prefix: "[系统]", color: InkWashTheme.TextTertiary, text: " 恭喜侠客逍遥客完成「黑风寨剿匪」"),
                (prefix: "[门派]", color: InkWashTheme.JadeBright, text: " 青衣客：已到，等人齐"),
                (prefix: "[队伍]", color: InkWashTheme.BloodBright, text: " 红袖招：BOSS刷新了，速来"),
                (prefix: "[世界]", color: InkWashTheme.GoldBright, text: " 玉面狐：收四十级蓝装，有的MMM"),
                (prefix: "[系统]", color: InkWashTheme.TextTertiary, text: " 门派日常任务已刷新，请前往领取"),
                (prefix: "[门派]", color: InkWashTheme.JadeBright, text: " 醉道人：逍遥客拉怪稳一点"),
            };

            float msgY = 24f;
            foreach (var msg in messages)
            {
                _chatPanel.AddChild(MakeLabel(msg.prefix, 8f, msgY, 38f, 17f,
                    msg.color, 11f, InkWashTheme.FontRole.Body));
                _chatPanel.AddChild(MakeLabel(msg.text, 46f, msgY, ChatWidth - 8f - 46f, 17f,
                    InkWashTheme.TextSecondary, 11f, InkWashTheme.FontRole.Body));
                msgY += 17f;
            }

            // 输入行上分隔线
            _chatPanel.AddChild(new ContainerControl
            {
                Location = new Float2(6f, 163f),
                Size = new Float2(ChatWidth - 12f, 1f),
                BackgroundColor = InkWashTheme.BorderFaint,
                AnchorPreset = AnchorPresets.TopLeft,
            });

            // 频道选择 48x24（bg-elevated + border-gold）
            _chatPanel.AddChild(MakeCell(6f, 167f, 48f, 24f,
                InkWashTheme.BaseElevated, InkWashTheme.BorderGold, InkWashTheme.RadiusSm,
                "近聊", InkWashTheme.TextSecondary, 10f, InkWashTheme.FontRole.Body));

            // 输入框（bg-abyss + border-faint，占位文字）
            _chatPanel.AddChild(MakeCell(58f, 167f, 248f, 24f,
                InkWashTheme.Abyss, InkWashTheme.BorderFaint, InkWashTheme.RadiusSm,
                null, Color.Transparent, 0f, InkWashTheme.FontRole.Body));
            _chatPanel.AddChild(MakeLabel("输入消息...", 64f, 167f, 236f, 24f,
                InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body));

            // 表情钮 24x24（bg-elevated + border-faint）
            _chatPanel.AddChild(MakeCell(310f, 167f, 24f, 24f,
                InkWashTheme.BaseElevated, InkWashTheme.BorderFaint, InkWashTheme.RadiusSm,
                ":)", InkWashTheme.TextSecondary, 11f, InkWashTheme.FontRole.Body));

            AddChild(_chatPanel);
        }

        // ===================================================================
        // 区域构造：右下功能按钮
        // ===================================================================

        /// <summary>
        /// 右下功能按钮：切换沉浸模式按钮（金边）+ 4 个功能钮（背包 B / 角色 C / 技能 K / 设置 ESC）。
        /// </summary>
        private void BuildFunctionButtons()
        {
            _funcColumn = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(FuncColW, FuncColH),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };

            // 切换沉浸模式（右对齐，padding 4x10，金边金亮字）
            float toggleW = 100f;
            var toggle = new InkNavButton(toggleW, ToggleH)
            {
                Location = new Float2(FuncColW - toggleW, 0f),
                BorderColor = InkWashTheme.GoldPrimary,
                DomId = InkPageDomIds.ToggleTraditional,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            toggle.AddChild(MakeLabel("眼", 10f, 0f, 12f, ToggleH,
                InkWashTheme.GoldBright, 12f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            toggle.AddChild(MakeLabel("切换沉浸模式", 28f, 0f, 62f, ToggleH,
                InkWashTheme.GoldBright, 10f, InkWashTheme.FontRole.Display));
            toggle.Clicked += b => RequestNavigation(b.DomId);
            _funcColumn.AddChild(toggle);

            // 功能钮行（42x38，gap 4px）
            var funcDefs = new[]
            {
                (glyph: "包", label: "背包 B", domId: InkPageDomIds.NavInventory),
                (glyph: "人", label: "角色 C", domId: InkPageDomIds.NavCharacterPanel),
                (glyph: "技", label: "技能 K", domId: InkPageDomIds.NavSkillPanel),
                (glyph: "设", label: "设置 ESC", domId: InkPageDomIds.NavSettings),
            };

            float btnX = 0f;
            float btnY = ToggleH + 6f;
            foreach (var f in funcDefs)
            {
                var btn = new InkNavButton(FuncBtnW, FuncBtnH)
                {
                    Location = new Float2(btnX, btnY),
                    DomId = f.domId,
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                btn.AddChild(MakeLabel(f.glyph, 0f, 5f, FuncBtnW, 16f,
                    InkWashTheme.GoldPrimary, 14f, InkWashTheme.FontRole.Body, TextAlignment.Center));
                btn.AddChild(MakeLabel(f.label, 0f, 23f, FuncBtnW, 8f,
                    InkWashTheme.TextSecondary, 8f, InkWashTheme.FontRole.Body, TextAlignment.Center));
                btn.Clicked += b => RequestNavigation(b.DomId);
                _funcColumn.AddChild(btn);
                btnX += FuncBtnW + FuncRowGap;
            }

            AddChild(_funcColumn);
        }

        // ===================================================================
        // 事件处理
        // ===================================================================

        /// <summary>触发导航请求（附带鎏金迸发粒子反馈）。</summary>
        private void RequestNavigation(string domId)
        {
            try
            {
                EmitGoldAtCenter();
                NavigationRequested?.Invoke(domId);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CombatHudTraditionalPage] NavigationRequested({domId}): {ex.Message}");
            }
        }

        /// <summary>在屏幕中心触发鎏金迸发粒子（导航反馈）。</summary>
        private void EmitGoldAtCenter()
        {
            try
            {
                if (ParticleSystem == null) return;
                var center = new Float2(Width * 0.5f, Height * 0.5f);
                var screenPos = PointToScreen(center);
                var localPos = ParticleSystem.PointFromScreen(screenPos);
                ParticleSystem.EmitGoldBurst(localPos, count: 14, isLarge: false);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[CombatHudTraditionalPage] EmitGoldAtCenter: {ex.Message}");
            }
        }

        // ===================================================================
        // 布局与更新
        // ===================================================================

        /// <summary>
        /// 按当前屏幕尺寸应用九区域布局（边距 12px，底部导航 bottom 220，技能栏/聊天/功能钮 bottom 8）。
        /// </summary>
        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;

            if (_targetPanel != null)
                _targetPanel.Location = new Float2(sw * 0.5f - TargetWidth * 0.5f, Edge);

            if (_charPanel != null)
                _charPanel.Location = new Float2(Edge, Edge);

            if (_partyPanel != null)
                _partyPanel.Location = new Float2(Edge, Edge + CharHeight + 8f); // mt-2

            if (_minimapContainer != null)
                _minimapContainer.Location = new Float2(sw - MinimapContainerSize - Edge, Edge);

            if (_questPanel != null)
                _questPanel.Location = new Float2(sw - QuestWidth - Edge, QuestTop);

            if (_navRow != null)
                _navRow.Location = new Float2(sw * 0.5f - NavBarWidth * 0.5f, sh - NavBottom - NavBtnH);

            if (_skillPanel != null)
                _skillPanel.Location = new Float2(sw * 0.5f - SkillPanelW * 0.5f, sh - 8f - SkillPanelH);

            if (_chatPanel != null)
                _chatPanel.Location = new Float2(Edge, sh - 8f - ChatHeight);

            if (_funcColumn != null)
                _funcColumn.Location = new Float2(sw - FuncColW - Edge, sh - 8f - FuncColH);
        }

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (_boundCharacter != null)
                RefreshBoundCharacterDisplay();
            if (_minimap != null)
                _minimap.PlayerYaw = _minimapPlayerYaw;
        }

        /// <summary>重新计算布局（IInkPage 契约，屏幕尺寸变化时由路由器调用）。</summary>
        public void RefreshLayout()
        {
            try
            {
                _screenSize = new Float2(Width > 0f ? Width : FlaxEngine.Screen.Size.X,
                                         Height > 0f ? Height : FlaxEngine.Screen.Size.Y);
                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CombatHudTraditionalPage] RefreshLayout: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }
    }
}
