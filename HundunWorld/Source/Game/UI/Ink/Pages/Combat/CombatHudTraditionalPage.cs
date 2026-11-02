using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using Game.Combat.Skills;
using HundunWorld.Game.Character;
using HundunWorld.Game.Navigation;
using ChatChannel = Horizon.Game.Message.Network.ChatChannel;
using ChatClient = HundunWorld.Game.Chat.ChatClient;
using ChatLine = HundunWorld.Game.Chat.ChatLine;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink.Pages.Combat
{
    /// <summary>
    /// 传统模式战斗 HUD 页面（四象限固定式布局，对应 combat-hud-traditional.html）。
    /// 区域：
    /// <list type="bullet">
    ///   <item>顶部中央：目标信息（38 圆形血头像 + 10px 血条 + 精英标记，整体放大 20%）</item>
    ///   <item>左上：角色面板（72 圆形金边头像 + 等级徽章 + 血/气/修三条 + Buff 行）+ 队伍面板</item>
    ///   <item>右上：方形小地图（160 圆形 + 8 个 28px 圆形环绕按钮 + 坐标条）</item>
    ///   <item>右侧：任务追踪（3 任务 + 三色进度条）</item>
    ///   <item>底部中央偏上：17 入口导航栏（50x36，无外层面板）</item>
    ///   <item>左下：聊天框（6 可点击频道 Tab 过滤 + 可滚动消息区 + 输入行）</item>
    ///   <item>右下：功能按钮（切换沉浸模式 + 背包/角色/技能/设置）</item>
    /// </list>
    /// 边框统一 <c>--ink-border-gold</c>，头像带金辉光。
    /// 通过 <see cref="NavigationRequested"/> 事件向 <see cref="InkPageRouter"/> 暴露导航请求。
    /// </summary>
    public class CombatHudTraditionalPage : ContainerControl, IInkPage, IInkNavigationSource
    {
        // ===================================================================
        // 布局常量（1920x1080 参考分辨率，像素值对齐 combat-hud-traditional.html）
        // =======================================================================

        /// <summary>屏幕边缘统一边距（top/left/right/bottom 均 12px）</summary>
        private const float Edge = 12f;

        // --- 顶部中央：目标信息（整体放大 20%）---
        private const float TargetWidth = 360f;
        private const float TargetPaddingX = 14f;
        private const float TargetPaddingY = 10f;
        private const float TargetAvatarSize = 38f;
        private const float TargetHeight = 92f;   // 多出的一截给动作行（打断/嘲讽）

        // --- 顶部中央：方位与地图标记指示条（罗盘带） ---
        private const float CompassWidth = 640f;
        private const float CompassHeight = 30f;
        private const float CompassGapBelowTarget = 6f;
        /// <summary>罗盘带距屏幕顶部边距（整体置顶）。</summary>
        private const float CompassTop = 10f;

        // --- 左上：角色面板（放大头像/字号/血条以更醒目）---
        private const float CharWidth = 340f;
        private const float CharPadding = 12f;
        private const float CharAvatarSize = 72f;
        private const float LevelBadgeSize = 28f;
        private const float CharInfoX = 96f;      // 12 + 72 + 12
        private const float CharBarLabelW = 14f;
        private const float CharBarX = 114f;       // 96 + 14 + 4
        private const float CharBarW = 214f;       // 340 - 12 - 114
        private const float CharHeight = 130f;     // 94(buffY) + 24 + 12

        // --- 左上：队伍面板 ---
        private const float PartyWidth = 300f;
        private const float PartyPadding = 8f;
        private const float PartyAvatarSize = 24f;
        private const float PartyRowH = 30f;       // 24 + 6(gap-1.5)
        private const float PartyHeight = 150f;    // 28 + 4*30 - 6 + 8

        // --- 右上：小地图 ---
        /// <summary>小地图整体缩放系数（1.0 = 设计稿：160px 圆形地图 + 200px 容器 + 28px 圆钮）。</summary>
        private const float MinimapScale = 1.2f;
        private const float MinimapContainerSize = 200f * MinimapScale;
        private const float MinimapMapSize = 160f * MinimapScale;
        private const float MinimapBtnSize = 28f * MinimapScale;
        private const float CoordBarHeight = 24f;

        // --- 右侧：任务追踪 ---
        private const float QuestWidth = 220f;
        /// <summary>任务追踪顶边：紧随小地图容器（含坐标条）之下，留 20px 间距。</summary>
        private const float QuestTop = Edge + MinimapContainerSize + 4f + CoordBarHeight + 20f;
        private const float QuestHeight = 117f;
        /// <summary>任务追踪折叠后仅保留标题行的高度。</summary>
        private const float QuestCollapsedHeight = 30f;

        // --- 底部：导航栏 ---
        private const float NavBtnW = 50f;
        private const float NavBtnH = 36f;
        private const float NavGap = 2f;
        private const int NavCount = 18;
        private const float NavBarWidth = NavCount * NavBtnW + (NavCount - 1) * NavGap; // 882
        private const float NavBottom = 220f;

        // --- 左下：聊天框 ---
        private const float ChatWidth = 340f;
        private const float ChatContentH = 140f;
        private const float ChatHeight = 195f;     // 3+18+1+140+1+4+24+4
        /// <summary>聊天消息区从 ChatClient 同步的节流间隔（秒）。</summary>
        private const float ChatSyncInterval = 0.4f;
        /// <summary>聊天消息字号可选范围（设置面板）。</summary>
        private const float ChatFontMin = 10f;
        private const float ChatFontMax = 16f;

        // --- 底部：修为经验条（9 格 + 跨格连续渐变）---
        private const float ExpBarHeight = 6f;
        private const float ExpBarBottom = 4f;
        private const int ExpBarSegments = 9;

        // --- 右下：功能按钮（竖向排列）---
        private const float FuncBtnW = 42f;
        private const float FuncBtnH = 38f;
        private const float FuncRowGap = 4f;
        /// <summary>列宽：需容纳"切换沉浸模式"宽钮（100）。</summary>
        private const float FuncColW = 100f;
        private const float ToggleH = 22f;
        /// <summary>列高：切换钮 + 4 个功能钮竖向堆叠。</summary>
        private const float FuncColH = ToggleH + 6f + 4 * FuncBtnH + 3 * FuncRowGap; // 192
        /// <summary>底部统一外边距（聊天框 / 功能钮组距屏幕底部）。</summary>
        private const float EdgeBottom = 20f;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        /// <summary>HUD 三层宿主：常驻/上下文/瞬时，见 <see cref="InkHudLayerHost"/>。</summary>
        private InkHudLayerHost _hudLayers;

        private ContainerControl _targetPanel;
        private InkBar _targetHpBar;
        private Label _targetHpValueLabel;

        /// <summary>顶部中央目标信息框的实时字段：数据来自世界选中服务，不再是设计稿占位值。</summary>
        private InkCircle _targetAvatar;
        private Label _targetNameLabel;
        private Label _targetLevelLabel;
        private Label _targetKindLabel;

        /// <summary>
        /// 目标框上的两个动作按钮：打断 / 嘲讽。
        /// <para>服务端这两条受理口早就有了（还各自带拒因计数），但客户端一直没有入口——
        /// <c>CombatReporter.ReportInterruptById</c> 与 <c>ReportTauntById</c> 全仓零调用方，
        /// 玩家既踢不断读条也拉不走咬队友的怪。这里与 <c>X</c>/<c>T</c> 两个快捷键调同一对方法，
        /// 判据只有一份（<c>MonsterCombatActionRules</c>），按钮的亮/灰与提示文案也都从它来。</para>
        /// </summary>
        private InkNavButton _interruptBtn;
        private InkNavButton _tauntBtn;
        private Label _targetActionHint;
        private const float TargetActionBtnWidth = 64f;
        private const float TargetActionBtnHeight = 20f;

        /// <summary>目标信息框刷新节流：血量/距离不需要每帧重算，4Hz 足够跟手。</summary>
        private const float TargetRefreshInterval = 0.25f;
        private float _targetRefreshTimer = TargetRefreshInterval;

        /// <summary>
        /// 上一帧的选中引用与"是否还活着"。目标被击杀/离开 AOI 时 Actor 会被销毁，
        /// 这种消失必须**当帧**反映到信息框上（否则 4Hz 节流会让死目标滞留 0.25 秒），
        /// 所以节流只对血量生效，身份与存活变化一律立即刷新。
        /// </summary>
        private object _lastTargetRef;
        private bool _lastTargetAlive;
        private CompassBar _compassBar;

        /// <summary>罗盘带导航标记刷新间隔（秒）——POI 方位与玩家位置均变化缓慢，无需每帧重算。</summary>
        private const float CompassRefreshInterval = 0.4f;

        /// <summary>坐标条刷新间隔（秒）：比罗盘更高频，保证移动时读数跟手</summary>
        private const float CoordRefreshInterval = 0.25f;

        /// <summary>罗盘带上最多绘制的地图标记数（避免拥挤遮挡八方位标签）。</summary>
        private const int CompassMaxMarkers = 8;

        /// <summary>内置演示标记（导航数据不可用时的回退，保持既有观感）。</summary>
        private static readonly (float Bearing, string Glyph, Color Color)[] DemoCompassMarkers =
        {
            (35f, "任", InkWashTheme.GoldBright),
            (120f, "城", InkWashTheme.JadeBright),
            (210f, "肆", InkWashTheme.VermilionBright),
            (305f, "友", InkWashTheme.PaperAged),
        };

        private float _compassRefreshTimer;

        private float _coordRefreshTimer;

        private ContainerControl _charPanel;
        private InkCircle _charAvatar;
        private Label _charAvatarGlyph;
        private Label _charNameLabel;
        private Label _sectLabel;
        private InkCircle _levelBadge;
        private InkBar _hpBar;
        private Label _hpValueLabel;
        private InkBar _mpBar;
        private Label _mpValueLabel;
        private InkBar _expBar;
        private Label _expPercentLabel;

        // 控制状态行（眩晕/沉默/定身…）：真实数据来自 ControlStateManager，替代原先 4 个假 Buff 图标
        private ControlStateChipView[] _controlChips;
        private float _controlChipRefreshTimer;
        private const int ControlChipSlots = 6;
        private const float ControlChipW = 24f;
        private const float ControlChipH = 34f;
        private const float ControlChipGap = 5f;

        private ContainerControl _partyPanel;

        private ContainerControl _minimapContainer;
        private InkMinimap _minimap;
        private Label _areaLabel;
        private Label _coordLabel;

        private ContainerControl _questPanel;
        private readonly List<InkNavButton> _questRows = new();
        private InkNavButton _questToggleBtn;
        private Label _questToggleLabel;
        private bool _questCollapsed;
        private Label _questEmpty;
        private int _questRevision = -1;
        private float _questRequestTimer = 1f;

        private ContainerControl _navRow;

        private InkPanel _chatPanel;
        private InkMessageList _chatMessages;
        private TextBox _chatInput;
        private ChatChannel? _chatFilterChannel;
        private float _chatSyncTimer;
        private InkNavButton _chatToggleBtn;
        private Label _chatToggleLabel;
        private bool _chatCollapsed;
        private ulong _chatWhisperTargetId;
        private string _chatWhisperTargetName;
        private InkNavButton _chatWhisperExitBtn;
        private InkPanel _chatSettingsPanel;
        private float _chatFontSize = 11f;
        private bool _chatHistoryLoading;
        private float _chatHistoryLoadingAt;
        private InkExpBar _expBottomBar;
        private readonly List<(Panel Dot, ChatChannel Channel)> _chatTabDots = new();
        private readonly List<(InkNavButton Button, ChatChannel Channel)> _chatMuteButtons = new();
        private readonly List<(InkNavButton Button, Label Label, ChatChannel Channel)> _chatTabs = new();

        /// <summary>聊天 Tab 顺序（附近/世界/门派/队伍/团队/密聊/系统），与「结伴江湖」页同源。</summary>
        private static readonly ChatChannel[] ChatTabChannels = InkChatChannelView.TabChannels;

        private ContainerControl _funcColumn;

        /// <summary>右下角"退出游戏"圆钮（功能列左侧，底对齐）</summary>
        private InkCircle _exitGameButton;
        private const float ExitBtnSize = 44f;

        // ===================================================================
        // mock 数据（对齐 combat-hud-traditional.html）
        // =======================================================================

        // 构造期中性初值：真实值由 BindCharacter 注入，绑定前显示占位而不是设计稿的 60 级/12450 血。
        private string _playerName = "—";
        /// <summary>门派名。创角不再选门派，未加入前显示「无门派」，由 <see cref="SetSectName"/> 注入真实值。</summary>
        private string _sectName = "无门派";
        private int _playerLevel;
        private int _hpCurrent;
        private int _hpMax;
        private int _mpCurrent;
        private int _mpMax;
        private float _expRatio;

        // ===================================================================
        // 屏幕尺寸缓存与数据绑定
        // =======================================================================

        private Float2 _screenSize;
        private CharacterAttributesComponent _boundCharacter;
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

        /// <summary>
        /// 注入门派名。创角不再选择门派，未加入前传入「无门派」；
        /// 角色在游戏内创建 / 加入门派之后才显示门派名。
        /// </summary>
        public void SetSectName(string sectName)
        {
            _sectName = string.IsNullOrEmpty(sectName) ? "无门派" : sectName;
            if (_sectLabel != null)
                _sectLabel.Text = _sectName;
        }

        private void RefreshBoundCharacterDisplay()
        {
            if (_boundCharacter == null) return;
            try
            {
                var c = _boundCharacter;
                if (_charNameLabel != null)
                    _charNameLabel.Text = !string.IsNullOrEmpty(c.Nickname) ? c.Nickname : "—";
                if (_charAvatarGlyph != null && !string.IsNullOrEmpty(c.Nickname))
                    _charAvatarGlyph.Text = c.Nickname[0].ToString();
                if (_levelBadge != null)
                    _levelBadge.Glyph = c.Level.ToString();
                if (_hpBar != null && c.MaxHealth > 0f)
                    _hpBar.Value = Mathf.Saturate(c.CurrentHealth / c.MaxHealth);
                if (_hpValueLabel != null)
                    _hpValueLabel.Text = $"{(int)c.CurrentHealth}/{(int)c.MaxHealth}";
                if (_mpBar != null && c.MaxEnergy > 0f)
                    _mpBar.Value = Mathf.Saturate(c.CurrentEnergy / c.MaxEnergy);
                if (_mpValueLabel != null)
                    _mpValueLabel.Text = $"{(int)c.CurrentEnergy}/{(int)c.MaxEnergy}";
                if (_expBottomBar != null)
                    _expBottomBar.SetValue(c.Exp, c.MaxExp);
                if (_expBar != null && c.MaxExp > 0)
                    _expBar.Value = Mathf.Saturate((float)c.Exp / c.MaxExp);
                // 百分比文字原来只在构造时写成 0%，经验变化后不跟着变
                if (_expPercentLabel != null)
                    _expPercentLabel.Text = c.MaxExp > 0
                        ? $"{Mathf.Saturate((float)c.Exp / c.MaxExp) * 100f:0}%"
                        : "—";
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CombatHudTraditionalPage] RefreshBoundCharacterDisplay: {ex.Message}");
            }
        }

        /// <summary>
        /// 把本地角色当前的控制状态（眩晕/沉默/定身…）刷到左上角状态行。
        /// 数据唯一来源是 <see cref="ControlStateManager"/>：服务端状态位由
        /// LocalPlayerActorSyncSystem 镜像、本地技能效果由 SkillEffectSystem 施加，两边都不在这里写。
        /// </summary>
        private void RefreshControlChips()
        {
            if (_controlChips == null) return;

            ulong id = HundunWorldGame.Instance?.PlayerId ?? 0UL;
            var states = id == 0UL
                ? null
                : ControlStateManager.Instance.GetActiveControlStates(id);

            if (states != null)
                states.Sort((a, b) => a.State.CompareTo(b.State));

            int count = states?.Count ?? 0;
            for (int i = 0; i < _controlChips.Length; i++)
            {
                var chip = _controlChips[i];
                if (chip == null) continue;

                if (i < count)
                {
                    chip.SetState(states[i].State, states[i].RemainingTime);
                    chip.Visible = true;
                }
                else
                {
                    chip.Visible = false;
                }
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
                _hudLayers = new InkHudLayerHost();
                AddChild(_hudLayers);

                BuildTargetInfo();
                BuildCompassBar();
                BuildCharacterPanel();
                BuildPartyPanel();
                BuildMinimap();
                BuildQuestTracker();
                BuildNavBar();
                BuildChatBox();
                BuildFunctionButtons();
                BuildExpBar();

                ApplyLayout();

                // 其它页面（好友页「私聊」）留下的密聊请求：HUD 重建后在这里消费
                TryConsumePendingWhisper();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CombatHudTraditionalPage] 初始化失败: {ex}");
                // 编辑器磁盘日志有滞后，构造失败时另留一份文件证据，否则只能看到整页空壳
                try
                {
                    var probe = System.IO.Path.Combine(
                        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                        "HundunWorld", "hud-ctor-probe.out");
                    System.IO.File.AppendAllText(probe, "[CombatHudTraditionalPage] " + ex + "\n");
                }
                catch { }
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

            /// <summary>居中字形（用 Render2D 直接绘制，精确居中，避免 Label 对齐偏差）</summary>
            public string Glyph { get; set; }

            /// <summary>字形颜色</summary>
            public Color GlyphColor { get; set; } = Color.White;

            /// <summary>字形字体</summary>
            public FontReference GlyphFont { get; set; }

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

                // 居中字形（Render2D 直接绘制，精确居中）
                if (!string.IsNullOrEmpty(Glyph))
                {
                    var font = GlyphFont.GetFont();
                    if (font != null)
                        Render2D.DrawText(font, Glyph, new Rectangle(0f, 0f, Width, Height),
                            GlyphColor, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }

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

            /// <summary>居中字形（用 Render2D 直接绘制，精确居中，避免 Label 对齐偏差）</summary>
            public string Glyph { get; set; }

            /// <summary>字形颜色</summary>
            public Color GlyphColor { get; set; } = Color.White;

            /// <summary>字形字体</summary>
            public FontReference GlyphFont { get; set; }

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

                // 居中字形（Render2D 直接绘制，精确居中）
                if (!string.IsNullOrEmpty(Glyph))
                {
                    var font = GlyphFont.GetFont();
                    if (font != null)
                        Render2D.DrawText(font, Glyph, bounds, GlyphColor,
                            TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }

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

            /// <summary>长按事件（按下保持超过阈值后抬起触发；触发后不再触发 Clicked）</summary>
            public event Action<InkNavButton> LongPressed;

            /// <summary>目标页面 dom-id</summary>
            public string DomId { get; set; }

            private const float LongPressSeconds = 0.5f;
            private float _pressStart;

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
            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                    _pressStart = Time.TimeSinceStartup;
                return base.OnMouseDown(location, button);
            }

            /// <inheritdoc />
            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location))
                {
                    if (Time.TimeSinceStartup - _pressStart >= LongPressSeconds)
                    {
                        LongPressed?.Invoke(this);
                        return true;
                    }
                    Clicked?.Invoke(this);
                    return true;
                }
                return false;
            }
        }

        // ===================================================================
        // 嵌套控件：可滚动聊天消息列表
        // ===================================================================

        /// <summary>
        /// 聊天消息列表：容器内裁剪，支持鼠标滚轮上下滚动；
        /// 行内容由调用方（页面）按频道过滤后经 <see cref="InkMessageList.SetLines"/> 提供。
        /// </summary>
        private sealed class InkMessageList : ContainerControl
        {
            private const float RowH = 17f;
            private const float PrefixW = 38f;

            private readonly List<(ChatLine Line, string Prefix, Color Color, string Text)> _lines = new();
            private readonly List<ContainerControl> _rows = new();
            private float _contentH;
            private float _scroll;

            /// <summary>点击某行（页面据此发起密聊）。</summary>
            public event Action<ChatLine> LineClicked;

            /// <summary>长按某行（页面据此撤回/举报）。</summary>
            public event Action<ChatLine> LineLongPressed;

            /// <summary>滚轮滚到顶部（页面据此加载更早历史）。</summary>
            public event Action ReachedTop;

            /// <summary>消息字号（设置面板可调）。</summary>
            public float FontSize { get; set; } = 11f;

            public InkMessageList()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = true;
            }

            /// <summary>设置可见行（调用方已完成频道过滤与排序）。</summary>
            public void SetLines(IReadOnlyList<(ChatLine Line, string Prefix, Color Color, string Text)> lines)
            {
                _lines.Clear();
                for (int i = 0; i < lines.Count; i++)
                    _lines.Add(lines[i]);
                Rebuild();
            }

            /// <summary>调整字号并重建。</summary>
            public void SetFontSize(float size)
            {
                FontSize = size;
                Rebuild();
            }

            private void Rebuild()
            {
                // 重建前判断是否已贴底：贴底则继续自动滚到最新，否则保留用户滚动位置。
                float prevMax = Mathf.Max(0f, _contentH - Height);
                bool atBottom = _scroll >= prevMax - 1f;

                foreach (var r in _rows) r.Dispose();
                _rows.Clear();

                foreach (var m in _lines)
                {
                    // 行用透明 InkNavButton：可点击（发起密聊）+ 悬停反馈
                    var row = new InkNavButton(Width, RowH)
                    {
                        Location = Float2.Zero,
                        FillColor = Color.Transparent,
                        BorderColor = Color.Transparent,
                        AnchorPreset = AnchorPresets.TopLeft,
                    };
                    row.AddChild(new Label
                    {
                        Location = new Float2(4f, 0f),
                        Size = new Float2(PrefixW, RowH),
                        Text = m.Prefix,
                        TextColor = m.Color,
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, FontSize),
                        VerticalAlignment = TextAlignment.Center,
                        AnchorPreset = AnchorPresets.TopLeft,
                    });
                    row.AddChild(new Label
                    {
                        Location = new Float2(4f + PrefixW, 0f),
                        Size = new Float2(Mathf.Max(0f, Width - 8f - PrefixW), RowH),
                        Text = m.Text,
                        TextColor = InkWashTheme.TextSecondary,
                        Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, FontSize),
                        VerticalAlignment = TextAlignment.Center,
                        AnchorPreset = AnchorPresets.TopLeft,
                    });

                    ChatLine line = m.Line;
                    row.Clicked += _ => LineClicked?.Invoke(line);
                    row.LongPressed += _ => LineLongPressed?.Invoke(line);

                    AddChild(row);
                    _rows.Add(row);
                }

                _contentH = _rows.Count * RowH;
                float max = Mathf.Max(0f, _contentH - Height);
                _scroll = atBottom ? max : Mathf.Clamp(_scroll, 0f, max);
                ApplyScroll();
            }

            private void ApplyScroll()
            {
                float y = -_scroll;
                foreach (var r in _rows)
                {
                    r.Location = new Float2(0f, y);
                    y += RowH;
                }
            }

            /// <inheritdoc />
            public override bool OnMouseWheel(Float2 location, float delta)
            {
                _scroll -= delta * RowH;
                _scroll = Mathf.Clamp(_scroll, 0f, Mathf.Max(0f, _contentH - Height));
                ApplyScroll();

                // 已在顶部继续上滚 → 通知页面加载更早历史
                if (_scroll <= 0.5f)
                    ReachedTop?.Invoke();
                return true;
            }
        }

        // ===================================================================
        // 嵌套控件：底部修为经验条（9 格 + 跨格连续渐变 + 数值）
        // ===================================================================

        /// <summary>
        /// 修为经验条：整体分 9 格（等宽刻度），填充色为跨全长的连续渐变——
        /// 取 9 个锚点色按 x 位置线性插值，故格与格交界处颜色天然连续；
        /// 叠加居中数值文本（exp/max）。填充比例 = exp / max。
        /// </summary>
        private sealed class InkExpBar : ContainerControl
        {
            public float Ratio;

            private readonly Label _value;

            /// <summary>9 段锚点色（左→右，低阶到高阶）。</summary>
            private static readonly Color[] Anchors =
            {
                new Color(0.45f, 0.72f, 0.45f),   // 松绿
                new Color(0.35f, 0.72f, 0.62f),   // 青
                new Color(0.35f, 0.62f, 0.92f),   // 靛蓝
                new Color(0.55f, 0.50f, 0.92f),   // 紫
                new Color(0.80f, 0.50f, 0.85f),   // 品红
                new Color(0.92f, 0.60f, 0.35f),   // 橙
                new Color(0.92f, 0.45f, 0.30f),   // 朱
                new Color(0.95f, 0.72f, 0.30f),   // 金
                new Color(1.00f, 0.88f, 0.45f),   // 鎏金
            };

            public InkExpBar()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;

                _value = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, -12f),
                    Size = new Float2(0f, 12f),
                    Text = string.Empty,
                    TextColor = InkWashTheme.TextDefault,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 10f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                AddChild(_value);
            }

            /// <summary>设置数值（exp 当前 / max 升级所需）。</summary>
            public void SetValue(long exp, long max)
            {
                Ratio = max > 0 ? Mathf.Saturate((float)exp / max) : 0f;
                _value.Text = max > 0 ? $"{exp}/{max}" : exp.ToString();
                // 条体仅 6px 高，数值显示在条体上方（负 Y，ClipChildren=false 可绘出）
                _value.Bounds = new Rectangle(0f, -12f, Width, 12f);
            }

            /// <summary>按归一化位置 t(0~1) 在 9 个锚点色之间线性插值（连续）。</summary>
            private static Color Sample(float t)
            {
                int n = Anchors.Length;
                float p = Mathf.Saturate(t) * (n - 1);
                int i = (int)p;
                if (i >= n - 1) return Anchors[n - 1];

                var a = Anchors[i];
                var b = Anchors[i + 1];
                float f = p - i;
                return new Color(
                    a.R + (b.R - a.R) * f,
                    a.G + (b.G - a.G) * f,
                    a.B + (b.B - a.B) * f,
                    a.A + (b.A - a.A) * f);
            }

            public override void Draw()
            {
                float w = Width, h = Height;
                if (w <= 0f || h <= 0f)
                {
                    base.Draw();
                    return;
                }

                float r = h * 0.5f;   // 胶囊圆角（平滑）
                var rect = new Rectangle(0f, 0f, w, h);

                // 底色：半透明灰
                InkRenderHelper.FillRoundedRectangle(rect, r, new Color(0.55f, 0.55f, 0.58f, 0.35f));

                // 渐变填充：分带绘制（+1px 重叠防接缝），只画到 比例 处；两端补半圆使其平滑圆角
                float fillW = w * Mathf.Saturate(Ratio);
                if (fillW > 0f)
                {
                    const int bands = 64;
                    float bw = w / bands;
                    for (int i = 0; i < bands; i++)
                    {
                        float x0 = i * bw;
                        if (x0 >= fillW) break;
                        var c = Sample((x0 + bw * 0.5f) / w);
                        float x1 = Mathf.Min(x0 + bw + 1f, fillW);
                        Render2D.FillRectangle(new Rectangle(x0, 0f, x1 - x0, h), c);
                    }
                    InkRenderHelper.FillCircle(new Float2(r, r), r, Sample(0f));
                    InkRenderHelper.FillCircle(new Float2(Mathf.Max(r, fillW - r), r), r, Sample(Mathf.Saturate(fillW / w)));
                }

                // 9 格竖线（灰）
                for (int i = 1; i < ExpBarSegments; i++)
                {
                    float x = w * i / ExpBarSegments;
                    Render2D.DrawLine(new Float2(x, 0f), new Float2(x, h), new Color(0.78f, 0.78f, 0.80f, 0.55f), 1f);
                }

                // 细描边，增强轮廓（浅色，避免抢眼）
                InkRenderHelper.DrawRoundedRectangle(rect, r, new Color(1f, 1f, 1f, 0.12f), 1f);

                // 数值文本在最上层
                base.Draw();
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
                circle.Glyph = glyph;
                circle.GlyphColor = glyphColor;
                circle.GlyphFont = InkRenderHelper.GetFontRef(glyphRole, glyphSize);
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
                cell.Glyph = glyph;
                cell.GlyphColor = glyphColor;
                cell.GlyphFont = InkRenderHelper.GetFontRef(glyphRole, glyphSize);
            }
            return cell;
        }

        // ===================================================================
        // 区域构造：顶部中央目标信息
        // ===================================================================

        /// <summary>
        /// 顶部中央目标信息：32 圆形血头像 + 名称/Lv + 8px 血条（金边）+ 数值/类型标记。
        /// 未选中任何对象时整块隐藏（原实现是写死的"黑风寨首领 Lv.62 / 9750/15000"，
        /// 无论场上有没有目标都常驻显示）。
        /// </summary>
        private void BuildTargetInfo()
        {
            // 无背景、无边框：用透明容器承载（头像/名称/血条直接浮于场景之上）
            _targetPanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(TargetWidth, TargetHeight),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
                Visible = false,
            };

            // 38 圆形头像（blood-faint 底 + blood-primary 边 + 17px 血亮字）
            _targetAvatar = MakeCircle(TargetPaddingX, TargetPaddingY, TargetAvatarSize,
                InkWashTheme.BloodFaint, InkWashTheme.BloodPrimary, 1f,
                "魁", InkWashTheme.BloodBright, 17f, InkWashTheme.FontRole.Display);
            _targetPanel.AddChild(_targetAvatar);

            // 信息列
            float colX = TargetPaddingX + TargetAvatarSize + 10f;
            float colW = TargetWidth - TargetPaddingX - colX;   // 284

            // 名称 16px 楷书 + Lv 13px DIN 血亮
            _targetNameLabel = MakeLabel("目标", colX, TargetPaddingY, colW - 72f, 19f,
                InkWashTheme.TextDefault, 16f, InkWashTheme.FontRole.Display);
            _targetPanel.AddChild(_targetNameLabel);
            _targetLevelLabel = MakeLabel("", colX + colW - 72f, TargetPaddingY, 72f, 19f,
                InkWashTheme.BloodBright, 13f, InkWashTheme.FontRole.Number, TextAlignment.Far);
            _targetPanel.AddChild(_targetLevelLabel);

            // HP 条 10px（bg-abyss + border-gold + blood 三段渐变）
            float barY = TargetPaddingY + 19f + 5f;
            _targetHpBar = MakeBar(colX, barY, colW, 10f, 0f,
                InkBarFillVariant.Blood, InkWashTheme.Abyss, InkWashTheme.BorderGold);
            _targetPanel.AddChild(_targetHpBar);

            // 数值行：当前/最大（DIN 11px）+ 实体类型（11px）
            float valY = barY + 10f + 3f;
            _targetHpValueLabel = MakeLabel("", colX, valY, colW * 0.5f, 14f,
                InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Number);
            _targetPanel.AddChild(_targetHpValueLabel);
            _targetKindLabel = MakeLabel("", colX + colW * 0.5f, valY, colW * 0.5f, 14f,
                InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Far);
            _targetPanel.AddChild(_targetKindLabel);

            // 动作行：打断 / 嘲讽（快捷键 X / T）。
            float actY = valY + 14f + 3f;
            _interruptBtn = MakeActionButton(colX, actY, "打断 X",
                () => HundunWorld.Game.Selection.WorldSelectionService.Instance?.TryInterruptSelected() ?? false);
            _tauntBtn = MakeActionButton(colX + TargetActionBtnWidth + 6f, actY, "嘲讽 T",
                () => HundunWorld.Game.Selection.WorldSelectionService.Instance?.TryTauntSelected() ?? false);
            _targetActionHint = MakeLabel("", colX + 2f * (TargetActionBtnWidth + 6f), actY + 3f,
                colW - 2f * (TargetActionBtnWidth + 6f), 14f,
                InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Body);
            _targetPanel.AddChild(_interruptBtn);
            _targetPanel.AddChild(_tauntBtn);
            _targetPanel.AddChild(_targetActionHint);

            _hudLayers.AddToLayer(_targetPanel, InkHudLayer.Context);
        }

        /// <summary>
        /// 目标框上的一个动作按钮。
        /// <para>灰化只是<b>提示</b>而不是禁用：Flax 的 <c>Control</c>/<c>Button</c> 没有
        /// <c>Interactable</c> 这类属性（实测），而"点下去没反应"是最难归因的表现——
        /// 所以两态都可点，由选中层给出人话原因；服务端那边还有各自的拒因计数。</para>
        /// </summary>
        private InkNavButton MakeActionButton(float x, float y, string text, System.Func<bool> onClick)
        {
            var btn = new InkNavButton(TargetActionBtnWidth, TargetActionBtnHeight)
            {
                Location = new Float2(x, y),
                AnchorPreset = AnchorPresets.TopLeft,
                FillColor = InkWashTheme.Panel,
                BorderColor = InkWashTheme.BorderGold,
            };
            btn.AddChild(MakeLabel(text, 0f, 0f, TargetActionBtnWidth, TargetActionBtnHeight,
                InkWashTheme.TextSecondary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            btn.Clicked += _ => onClick();
            return btn;
        }

        /// <summary>
        /// 用世界选中服务的当前目标刷新顶部信息框。无选中时隐藏。
        /// 血量读目标 Actor 上的 CharacterAttributesComponent——那是 FlaxActorSyncSystem
        /// 每帧从网络状态搬运过来的值，本层不再另开一条取数链路。
        /// </summary>
        private void RefreshTargetInfo()
        {
            if (_targetPanel == null)
                return;

            var selected = HundunWorld.Game.Selection.WorldSelectionService.Instance?.Selected;
            if (selected == null || selected.Actor == null || !selected.Actor.IsActive)
            {
                _targetPanel.Visible = false;
                return;
            }

            _targetPanel.Visible = true;

            var attrs = selected.Actor.GetScript<CharacterAttributesComponent>();
            string name = !string.IsNullOrEmpty(attrs?.Nickname) ? attrs.Nickname
                          : (!string.IsNullOrEmpty(selected.DisplayName) ? selected.DisplayName : selected.Actor.Name);

            _targetNameLabel.Text = name;
            _targetNameLabel.TextColor = selected.Kind == HundunWorld.Game.Selection.SelectableKind.Monster
                ? InkWashTheme.BloodBright
                : InkWashTheme.TextDefault;
            _targetLevelLabel.Text = selected.Level > 0 ? $"Lv.{selected.Level}" : string.Empty;
            _targetKindLabel.Text = TargetKindLabel(selected);
            if (_targetAvatar != null && name.Length > 0)
                _targetAvatar.Glyph = name.Substring(0, 1);

            float maxHp = attrs != null && attrs.MaxHealth > 0f ? attrs.MaxHealth : 0f;
            float curHp = attrs != null ? attrs.CurrentHealth : 0f;
            _targetHpBar.Value = maxHp > 0f ? Mathf.Clamp(curHp / maxHp, 0f, 1f) : 0f;
            _targetHpValueLabel.Text = maxHp > 0f ? $"{(int)curHp}/{(int)maxHp}" : string.Empty;

            RefreshTargetActions(selected);
        }

        /// <summary>
        /// 刷新两个动作按钮的亮/灰与那句人话。判据不在这里重写：
        /// 它住在 <c>MonsterCombatActionRules</c>（离线闸门把那份文件原样编进测试工程逐点真跑），
        /// 快捷键 X/T 走的是选中层同一对方法。
        /// </summary>
        private void RefreshTargetActions(HundunWorld.Game.Selection.SelectableEntityTag selected)
        {
            if (_interruptBtn == null || _tauntBtn == null || _targetActionHint == null)
                return;

            var has = selected.EntityId != 0UL;
            var isMonster = has && selected.Kind == HundunWorld.Game.Selection.SelectableKind.Monster;
            var selectable = has && selected.Selectable;
            var unattackable = has && selected.Unattackable;
            var casting = has && selected.IsCasting;
            var interruptible = has && selected.CastInterruptible;

            var canTaunt = HundunWorld.Game.Combat.MonsterCombatActionRules.CanTaunt(
                has, isMonster, selectable, unattackable);
            var canInterrupt = HundunWorld.Game.Combat.MonsterCombatActionRules.CanInterrupt(
                has, isMonster, selectable, casting, interruptible, unattackable);

            _tauntBtn.BorderColor = canTaunt ? InkWashTheme.BorderGold : InkWashTheme.Panel;
            _interruptBtn.BorderColor = canInterrupt ? InkWashTheme.BorderGold : InkWashTheme.Panel;

            // 只念一句：优先念打断的原因（它对状态最敏感，玩家最常撞上的就是"它没在读条"）；
            // 两个都能用时清空，免得一句常驻提示压在目标框上。
            var hint = HundunWorld.Game.Combat.MonsterCombatActionRules.InterruptHint(
                has, isMonster, selectable, casting, interruptible, unattackable);
            if (hint.Length == 0 && !canTaunt)
            {
                hint = HundunWorld.Game.Combat.MonsterCombatActionRules.TauntHint(
                    has, isMonster, selectable, unattackable);
            }
            _targetActionHint.Text = hint;
        }

        /// <summary>目标类型标签。物品品质名暂无统一表（四套品质枚举并存，M2 落地掉落物时收敛）。</summary>
        private static string TargetKindLabel(HundunWorld.Game.Selection.SelectableEntityTag tag)
        {
            switch (tag.Kind)
            {
                case HundunWorld.Game.Selection.SelectableKind.Monster: return "妖兽";
                case HundunWorld.Game.Selection.SelectableKind.Player: return "修士";
                case HundunWorld.Game.Selection.SelectableKind.Npc:
                    // 姿态来自服务端每快照下发的身份增量（随作息换档）；−1 = 未下发，只显示 NPC。
                    return tag.StandState switch
                    {
                        1 => "NPC · 坐",
                        2 => "NPC · 做工",
                        3 => "NPC · 持械",
                        _ => "NPC",
                    };
                case HundunWorld.Game.Selection.SelectableKind.GatherNode: return "可采集";
                case HundunWorld.Game.Selection.SelectableKind.Item: return "物品";
                default: return string.Empty;
            }
        }

        // ===================================================================
        // 区域构造：顶部中央方位与地图标记指示条（罗盘带）
        // ===================================================================

        /// <summary>
        /// 顶部中央罗盘带：八方位标签 + 度数刻度 + 地图标记，随玩家朝向横向滚动。
        /// 位置：目标信息面板下方居中；朝向由 <see cref="MinimapPlayerYaw"/> 每帧驱动。
        /// </summary>
        private void BuildCompassBar()
        {
            _compassBar = new CompassBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(CompassWidth, CompassHeight),
            };
            // 地图标记：优先取 NarrativePro 导航数据（POI / 传送点的世界方位角），不可用时回退内置演示方位。
            _compassBar.Markers = DemoCompassMarkers;
            _hudLayers.AddToLayer(_compassBar, InkHudLayer.Persistent);

            RefreshCompassMarkers();
        }

        /// <summary>
        /// 从 NarrativePro 导航刷新罗盘带标记。
        /// <para>
        /// 方位角按世界正北（-Z）为 0°、顺时针增加计算，与 <see cref="CompassBar"/> 内部
        /// <c>Delta(bearing, PlayerYaw)</c> 的约定一致（玩家朝北时正北 POI 落在带中央）。
        /// 传送点用金色「界」、已发现兴趣点用青色「景」、未发现用纸色「？」。
        /// </para>
        /// </summary>
        private void RefreshCompassMarkers()
        {
            if (_compassBar == null) return;

            var views = new List<MapMarkerView>();
            if (!PluginNavigationBridge.TryGetMarkers(views))
            {
                _compassBar.Markers = DemoCompassMarkers;
                return;
            }

            if (!PluginNavigationBridge.TryGetPlayerPose(out var playerPos, out _))
            {
                _compassBar.Markers = DemoCompassMarkers;
                return;
            }

            var markers = new List<(float Bearing, string Glyph, Color Color)>(views.Count);

            foreach (var v in views)
            {
                if (markers.Count >= CompassMaxMarkers) break;

                float dx = v.WorldPosition.X - playerPos.X;
                float dz = v.WorldPosition.Z - playerPos.Z;
                if (dx * dx + dz * dz < 1e-4f) continue;   // 与玩家重合，方位无意义

                float bearing = PluginNavigationBridge.NormalizeYaw(
                    Mathf.Atan2(dx, -dz) * Mathf.RadiansToDegrees);

                if (v.SupportsFastTravel)
                    markers.Add((bearing, "界", InkWashTheme.GoldBright));
                else if (!v.Discovered)
                    markers.Add((bearing, "？", InkWashTheme.PaperAged));
                else
                    markers.Add((bearing, "景", InkWashTheme.JadeBright));
            }

            _compassBar.Markers = markers.Count > 0 ? markers.ToArray() : DemoCompassMarkers;
        }

        /// <summary>
        /// 方位与地图标记指示条：按 <see cref="PlayerYaw"/> 绘制的横向罗盘带。
        /// 上带为地图标记（彩色三角 + 字形），中带为八方位标签，底部为度数刻度与中央朝向指针。
        /// </summary>
        private sealed class CompassBar : ContainerControl
        {
            /// <summary>玩家朝向（角度制 0~360，0=北）。</summary>
            public float PlayerYaw;

            /// <summary>地图标记：方位角 + 字形 + 颜色。</summary>
            public (float Bearing, string Glyph, Color Color)[] Markers = System.Array.Empty<(float, string, Color)>();

            /// <summary>可视总跨度（度）。</summary>
            private const float DegSpan = 150f;

            private static readonly (float Angle, string Label, bool Cardinal)[] Dirs =
            {
                (0f, "北", true), (45f, "东北", false), (90f, "东", true), (135f, "东南", false),
                (180f, "南", true), (225f, "西南", false), (270f, "西", true), (315f, "西北", false),
            };

            public CompassBar()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = true;
            }

            /// <summary>将方位角差归一化到 [-180,180)。</summary>
            private static float Delta(float bearing, float yaw)
            {
                return ((bearing - yaw + 540f) % 360f) - 180f;
            }

            public override void Draw()
            {
                if (Width <= 0f || Height <= 0f)
                {
                    base.Draw();
                    return;
                }

                var rect = new Rectangle(0f, 0f, Width, Height);
                // 底色：青金色（青与金混合），透明度 0.3（低底以凸显文字与图标）；无边框。
                // 整块平铺而不用 FillRoundedRectangle——后者按"十字带 + 四角圆"分次叠加绘制，
                // 半透明色在重叠区会被叠两次（0.3 叠成 0.51），四边留下一圈比内部更暗的假边框。
                // 原来用 0.3 透明度的青金浅色底：在雪地/沙漠这类亮场景里等于没有底，
                // 浅色方位字完全糊掉。改成深墨底（0.55），与 HUD 其它浮层口径一致。
                var baseInk = InkWashTheme.BaseDefault;
                var compassChrome = new Color(baseInk.R, baseInk.G, baseInk.B, 0.55f);
                Render2D.FillRectangle(rect, compassChrome);

                float cx = Width * 0.5f;
                float pxPerDeg = Width / DegSpan;
                float half = DegSpan * 0.5f;

                // 底部度数刻度（每 15° 小刻度，45° 大刻度）——统一灰白，去掉金色，避免被看成"金色边框"
                for (float a = 0f; a < 360f; a += 15f)
                {
                    float d = Delta(a, PlayerYaw);
                    if (Mathf.Abs(d) > half) continue;
                    float x = cx + d * pxPerDeg;
                    bool major = a % 45f < 0.01f;
                    float th = major ? 7f : 4f;
                    Render2D.DrawLine(new Float2(x, Height - 3f), new Float2(x, Height - 3f - th),
                        major ? InkWashTheme.TextDefault : InkWashTheme.TextTertiary, 1f);
                }

                // 中带：八方位标签（主方位金亮大字，隅方位纸色小字）
                foreach (var dir in Dirs)
                {
                    float d = Delta(dir.Angle, PlayerYaw);
                    if (Mathf.Abs(d) > half) continue;
                    float x = cx + d * pxPerDeg;
                    float size = dir.Cardinal ? 14f : 11f;
                    var color = dir.Cardinal ? InkWashTheme.GoldBright : InkWashTheme.PaperAged;
                    var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, size).GetFont();
                    if (font != null)
                        Render2D.DrawText(font, dir.Label, new Rectangle(x - 30f, 13f, 60f, 14f),
                            color, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }

                // 上带：地图标记（彩色向下三角 + 字形）
                if (Markers != null)
                {
                    foreach (var m in Markers)
                    {
                        float d = Delta(m.Bearing, PlayerYaw);
                        if (Mathf.Abs(d) > half) continue;
                        float x = cx + d * pxPerDeg;
                        Render2D.DrawLine(new Float2(x - 4f, 1f), new Float2(x, 6f), m.Color, 1.5f);
                        Render2D.DrawLine(new Float2(x, 6f), new Float2(x + 4f, 1f), m.Color, 1.5f);
                        var mf = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f).GetFont();
                        if (mf != null)
                            Render2D.DrawText(mf, m.Glyph, new Rectangle(x - 14f, 4f, 28f, 11f),
                                m.Color, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                    }
                }

                // 中央朝向指针（白灰倒三角，去金色）
                Render2D.DrawLine(new Float2(cx - 5f, Height - 2f), new Float2(cx, Height - 7f), InkWashTheme.TextDefault, 2f);
                Render2D.DrawLine(new Float2(cx, Height - 7f), new Float2(cx + 5f, Height - 2f), InkWashTheme.TextDefault, 2f);

                // 半透明遮罩：水平中心全透明，两端约 0.2 透明度（内容向边缘渐隐）
                const int MaskSlices = 24;
                float sliceW = Width / MaskSlices;
                var veil = InkWashTheme.BaseDefault;
                for (int i = 0; i < MaskSlices; i++)
                {
                    float x0 = i * sliceW;
                    float t = Mathf.Abs(x0 + sliceW * 0.5f - cx) / (Width * 0.5f);
                    float alpha = 0.2f * t;
                    if (alpha <= 0.001f) continue;
                    Render2D.FillRectangle(new Rectangle(x0, 0f, sliceW + 1f, Height), new Color(veil.R, veil.G, veil.B, alpha));
                }

                base.Draw();
            }
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
            // 无背景、无边框：透明容器承载（头像/名称/血条/Buff 直接浮于场景之上）
            _charPanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(CharWidth, CharHeight),
                // HUD 叠在 3D 世界上：亮场景（雪地/沙漠）里"无门派"和血/气/修标签会糊掉，
                // 给一层淡墨底（0.45）保证可读，又不至于挡场景。
                BackgroundColor = new Color(InkWashTheme.BaseDefault.R, InkWashTheme.BaseDefault.G,
                    InkWashTheme.BaseDefault.B, 0.45f),
                ClipChildren = false,
            };

            // 72 圆形头像（BaseTertiary 底 + BgMist 叠加，2px 金边 + 金辉光）
            _charAvatar = MakeCircle(CharPadding, CharPadding, CharAvatarSize,
                InkWashTheme.BaseTertiary, InkWashTheme.GoldPrimary, 2f,
                null, Color.Transparent, 0f, InkWashTheme.FontRole.Display,
                glow: InkWashTheme.GoldGlow, overlay: InkWashTheme.BgMist, overlayRatio: 1f);
            _charAvatarGlyph = new Label
            {
                // 用显式 Location/Size 居中（StretchAll 在此环境不可靠，会让字形偏向一侧）
                Location = Float2.Zero,
                Size = new Float2(CharAvatarSize, CharAvatarSize),
                AnchorPreset = AnchorPresets.TopLeft,
                Text = _playerName.Length > 0 ? _playerName[0].ToString() : "侠",
                TextColor = InkWashTheme.GoldPrimary,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 34f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _charAvatar.AddChild(_charAvatarGlyph);
            _charAvatar.Clicked += () => RequestNavigation(InkPageDomIds.NavCharacterPanel);
            _charPanel.AddChild(_charAvatar);

            // 28 圆形等级徽章（右下 +4px 偏移，bg-ink + 金边 + DIN 12 金亮）
            float badgeX = CharPadding + CharAvatarSize - LevelBadgeSize + 4f;
            float badgeY = CharPadding + CharAvatarSize - LevelBadgeSize + 4f;
            _levelBadge = MakeCircle(badgeX, badgeY, LevelBadgeSize,
                InkWashTheme.PanelSolid, InkWashTheme.GoldPrimary, 1f,
                _playerLevel.ToString(), InkWashTheme.GoldBright, 12f, InkWashTheme.FontRole.Number);
            _charPanel.AddChild(_levelBadge);

            // 名称 18px 楷书 + 门派 12px 青亮
            _charNameLabel = MakeLabel(_playerName, CharInfoX, CharPadding, 150f, 22f,
                InkWashTheme.TextDefault, 18f, InkWashTheme.FontRole.Display);
            _charPanel.AddChild(_charNameLabel);
            _sectLabel = MakeLabel(_sectName, CharInfoX + 150f, CharPadding, 60f, 22f,
                InkWashTheme.JadeBright, 12f, InkWashTheme.FontRole.Body, TextAlignment.Far);
            _charPanel.AddChild(_sectLabel);

            // 血条行：标签"血" + 16px 条（金边，数值居中覆盖）
            float hpY = CharPadding + 22f + 5f;
            _charPanel.AddChild(MakeLabel("血", CharInfoX, hpY, CharBarLabelW, 16f,
                InkWashTheme.TextSecondary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            _hpBar = MakeBar(CharBarX, hpY, CharBarW, 16f, 0.83f,
                InkBarFillVariant.Blood, InkWashTheme.Abyss, InkWashTheme.BorderGold);
            _charPanel.AddChild(_hpBar);
            _hpValueLabel = MakeLabel($"{_hpCurrent}/{_hpMax}", CharBarX, hpY, CharBarW, 16f,
                InkWashTheme.TextDefault, 11f, InkWashTheme.FontRole.Number, TextAlignment.Center);
            _charPanel.AddChild(_hpValueLabel);

            // 气条行：标签"气" + 14px 条（青边，数值居中覆盖）
            float mpY = hpY + 16f + 4f;
            _charPanel.AddChild(MakeLabel("气", CharInfoX, mpY, CharBarLabelW, 14f,
                InkWashTheme.TextSecondary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            _mpBar = MakeBar(CharBarX, mpY, CharBarW, 14f, 0.80f,
                InkBarFillVariant.Jade, InkWashTheme.Abyss, InkWashTheme.BorderJade);
            _charPanel.AddChild(_mpBar);
            _mpValueLabel = MakeLabel($"{_mpCurrent}/{_mpMax}", CharBarX, mpY, CharBarW, 14f,
                InkWashTheme.TextDefault, 10f, InkWashTheme.FontRole.Number, TextAlignment.Center);
            _charPanel.AddChild(_mpValueLabel);

            // 修为行：标签"修" + 5px 条（无边框，金渐变）+ 百分比右侧
            float expY = mpY + 14f + 4f;
            _charPanel.AddChild(MakeLabel("修", CharInfoX, expY - 2f, CharBarLabelW, 9f,
                InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            float expBarW = CharBarW - 34f;
            _expBar = MakeBar(CharBarX, expY, expBarW, 5f, _expRatio,
                InkBarFillVariant.Gold, InkWashTheme.Abyss, Color.Transparent);
            _charPanel.AddChild(_expBar);
            _expPercentLabel = MakeLabel($"{(int)(_expRatio * 100f)}%", CharBarX + expBarW + 4f, expY - 4f, 30f, 12f,
                InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Number);
            _charPanel.AddChild(_expPercentLabel);

            // 控制状态行：眩晕/沉默/定身…由 ControlStateManager 实时驱动；
            // 没有状态时整行留空，不再画 4 个不存在的 Buff。
            float buffY = CharPadding + CharAvatarSize + 10f;
            _controlChips = new ControlStateChipView[ControlChipSlots];
            float buffX = CharPadding;
            for (int i = 0; i < _controlChips.Length; i++)
            {
                var chip = new ControlStateChipView
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(buffX, buffY),
                    Size = new Float2(ControlChipW, ControlChipH),
                    Visible = false,
                };
                _charPanel.AddChild(chip);
                _controlChips[i] = chip;
                buffX += ControlChipW + ControlChipGap;
            }

            _hudLayers.AddToLayer(_charPanel, InkHudLayer.Persistent);
        }

        // ===================================================================
        // 区域构造：左上队伍面板
        // ===================================================================

        /// <summary>
        /// 左上队伍面板：标题行（队伍 + 4/5）+ 4 成员行（24 圆形品质边头像 + 名称/Lv + 5px 血条）。
        /// </summary>
        private void BuildPartyPanel()
        {
            // 无背景、无边框：透明容器承载（成员行直接浮于场景之上）
            _partyPanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(PartyWidth, PartyHeight),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };

            // 标题行：users 图标（以"队"字替代）+ 队伍 + 0/5
            _partyPanel.AddChild(MakeLabel("队", PartyPadding, PartyPadding, 12f, 14f,
                InkWashTheme.GoldPrimary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            _partyPanel.AddChild(MakeLabel("队伍", PartyPadding + 12f + 6f, PartyPadding, 80f, 14f,
                InkWashTheme.TextSecondary, 11f, InkWashTheme.FontRole.Display));
            _partyPanel.AddChild(MakeLabel("0/5", PartyWidth - PartyPadding - 42f, PartyPadding, 42f, 14f,
                InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Number, TextAlignment.Far));

            // 客户端没有队伍名册服务（服务端仅部分 CreateTeam）：不显示假队友，给诚实空态。
            _partyPanel.AddChild(MakeLabel("未组队", PartyPadding, PartyPadding + 14f + 10f,
                PartyWidth - PartyPadding * 2f, 18f,
                InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body));

            _hudLayers.AddToLayer(_partyPanel, InkHudLayer.Persistent);
        }

        // ===================================================================
        // 区域构造：右上方形小地图
        // ===================================================================

        /// <summary>
        /// 右上角小地图：240x240 容器内 192 圆形地图（24,24）+ 圆形功能钮 + 坐标条。
        /// 所有尺寸由 <see cref="MinimapScale"/> 统一缩放。
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

            // 192 圆形地图（InkMinimap 普通圆形：圆内透明 + 细圆环；外框可换成贴图，见 InkMinimap.FrameTexture）
            _minimap = new InkMinimap
            {
                Location = new Float2(20f * MinimapScale, 20f * MinimapScale),
                Size = new Float2(MinimapMapSize, MinimapMapSize),
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _minimap.AddLandmark(0.1f, 0.2f, 0.3f, new Color(InkWashTheme.JadeFaint.R, InkWashTheme.JadeFaint.G, InkWashTheme.JadeFaint.B, 0.5f));
            _minimap.AddLandmark(-0.2f, -0.15f, 0.25f, new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.15f));
            _minimap.AddLandmark(0.4f, -0.3f, 0.15f, new Color(InkWashTheme.JadeFaint.R, InkWashTheme.JadeFaint.G, InkWashTheme.JadeFaint.B, 0.3f));
            // 小地图上的敌人/队友点必须来自真实同步或导航 POI：
            // 原来这几个是写死的「2 敌人 + 3 队友」样例点，导航不可用时会一直挂在地图上骗人。
            // 只保留"自己"这一个真实点，其余由 InkMinimap 每 0.5s 从导航刷新写入。
            _minimap.AddEntity(InkMinimapEntityType.Player, 0f, 0f);
                        // 小地图显示半径（世界单位）：以玩家为圆心，仅显示附近标记；
            // 默认 30000 过大，会让 POI 全部挤在圆心附近而不可见。
            _minimap.NavigationRange = 1200f;
            _minimap.Clicked += () => RequestNavigation(InkPageDomIds.NavWorldMap);
_minimapContainer.AddChild(_minimap);

            // 8 个 28px 圆形功能按钮环绕（bg-ink + border-gold + 金字）
            // 环绕圆钮：仅保留「+」「−」缩放小地图（其余入口移除）；点圆盘本身打开大地图。
            // "+" 钮中心落在小地图圆环右上 45° 斜线上（与 "−" 同样"骑"在边框上），
            // 由半径反推，改 MinimapScale/地图尺寸时自动跟随，不再用魔数。
            float mapCenter = 20f * MinimapScale + MinimapMapSize * 0.5f;
            float ringDiag = MinimapMapSize * 0.5f * 0.7071f; // sin45° = cos45°
            var btnDefs = new[]
            {
                (x: mapCenter + ringDiag - MinimapBtnSize * 0.5f,
                 y: mapCenter - ringDiag - MinimapBtnSize * 0.5f,
                 glyph: "+", domId: (string)null, zoom: 0.7f),
                (x: 172f * MinimapScale, y: 86f * MinimapScale, glyph: "−", domId: (string)null, zoom: 1.5f),
            };

            foreach (var def in btnDefs)
            {
                var btn = MakeCircle(def.x, def.y, MinimapBtnSize,
                    InkWashTheme.PanelSolid, InkWashTheme.BorderGold, 1f,
                    def.glyph, InkWashTheme.GoldPrimary, 12f, InkWashTheme.FontRole.Body);
                if (def.zoom > 0f)
                {
                    float factor = def.zoom;
                    btn.Clicked += () => _minimap.ZoomBy(factor);
                }
                else if (def.domId != null)
                {
                    string target = def.domId;
                    btn.Clicked += () => RequestNavigation(target);
                }
                _minimapContainer.AddChild(btn);
            }

            // 坐标条（200x24，radius-4，洛阳城 + 坐标）
            // 坐标条：无背景、无边框（透明容器承载 区域名 + 坐标）
            var coordBar = new ContainerControl
            {
                Location = new Float2(0f, MinimapContainerSize + 4f), // mt-1
                Size = new Float2(MinimapContainerSize, CoordBarHeight),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _areaLabel = MakeLabel("洛阳城", 10f * MinimapScale, 4f, 100f * MinimapScale, 16f,
                InkWashTheme.GoldBright, 11f, InkWashTheme.FontRole.Display);
            coordBar.AddChild(_areaLabel);
            _coordLabel = MakeLabel("--, --", 110f * MinimapScale, 4f, 80f * MinimapScale, 16f,
                InkWashTheme.TextTertiary, 10f, InkWashTheme.FontRole.Number, TextAlignment.Far);
            coordBar.AddChild(_coordLabel);
            _minimapContainer.AddChild(coordBar);

            _hudLayers.AddToLayer(_minimapContainer, InkHudLayer.Persistent);
        }

        // ===================================================================
        // 区域构造：右侧任务追踪
        // ===================================================================

        /// <summary>
        /// 右侧任务追踪：标题行（任务追踪）+ 3 任务（名 + 进度计数 + 3px 三色进度条）。
        /// </summary>
        private void BuildQuestTracker()
        {
            // 无背景、无边框：透明容器承载（标题/任务名/计数/进度条直接浮于场景之上）
            _questPanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(QuestWidth, QuestHeight),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };

            // 标题行："任"字图标 + 任务追踪 12px 金亮 + 折叠开关
            _questPanel.AddChild(MakeLabel("任", 10f, 8f, 12f, 14f,
                InkWashTheme.GoldPrimary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            _questPanel.AddChild(MakeLabel("任务追踪", 28f, 8f, 120f, 14f,
                InkWashTheme.GoldBright, 12f, InkWashTheme.FontRole.Display));

            _questToggleBtn = new InkNavButton(16f, 14f)
            {
                Location = new Float2(QuestWidth - 12f - 16f, 8f),
                AnchorPreset = AnchorPresets.TopLeft,
                FillColor = Color.Transparent,
                BorderColor = Color.Transparent,
            };
            _questToggleLabel = MakeLabel("-", 0f, 0f, 16f, 14f,
                InkWashTheme.GoldBright, 12f, InkWashTheme.FontRole.Body, TextAlignment.Center);
            _questToggleBtn.AddChild(_questToggleLabel);
            _questToggleBtn.Clicked += _ => ToggleQuestCollapsed();
            _questPanel.AddChild(_questToggleBtn);

            // 3 任务：整行可点击（打开任务日志并定位到该条）；进度条颜色分别为青/金/血。
            // 内容一律来自服务端清单（QuestClientService），这里不再有写死的三行——
            // 写死的样本会让"界面有任务"和"身上有任务"变成两件事。
            RebuildQuestRows();

            _hudLayers.AddToLayer(_questPanel, InkHudLayer.Persistent);
        }

        /// <summary>
        /// 按服务端清单重画追踪行：进行中/可交付的前 3 条。
        /// <para>进度显示的是"已完成目标数 / 目标总数"，由服务端算好后照抄；
        /// 客户端不判断任何完成条件，否则两处规则一旦分叉，玩家会看见自己"做完了"却交不了。</para>
        /// </summary>
        private void RebuildQuestRows()
        {
            if (_questPanel == null) return;

            foreach (var row in _questRows)
                row.Dispose();
            _questRows.Clear();
            _questEmpty?.Dispose();
            _questEmpty = null;

            var all = HundunWorld.Game.Network.QuestClientService.Instance.GetQuests();
            var shown = new List<Horizon.Game.Message.Network.QuestSummary>(3);
            foreach (var quest in all)
            {
                if (quest == null || quest.Status == 2) continue;   // 已完成的不占追踪位
                if (shown.Count >= 3) break;
                shown.Add(quest);
            }

            if (shown.Count == 0)
            {
                _questEmpty = MakeLabel("暂无进行中的任务", 10f, 34f, QuestWidth - 30f, 16f,
                    InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body);
                _questPanel.AddChild(_questEmpty);
                _questEmpty.Visible = !_questCollapsed;
                return;
            }

            // 三色循环只是视觉分层（青/金/血），不承载语义：语义在进度数字里。
            var palette = new (Color color, InkBarFillVariant variant)[]
            {
                (InkWashTheme.JadeBright, InkBarFillVariant.Jade),
                (InkWashTheme.GoldBright, InkBarFillVariant.Gold),
                (InkWashTheme.BloodBright, InkBarFillVariant.Blood),
            };

            float qY = 30f;
            for (var index = 0; index < shown.Count; index++)
            {
                var quest = shown[index];
                var tint = palette[index % palette.Length];
                var questId = quest.QuestId;
                var questName = string.IsNullOrWhiteSpace(quest.QuestName) ? ("任务" + questId) : quest.QuestName;
                var required = quest.TargetProgress > 0 ? quest.TargetProgress : 1;
                var current = quest.CurrentProgress < 0 ? 0 : quest.CurrentProgress;
                var progress = current / (float)required;
                if (progress > 1f) progress = 1f;
                var countText = quest.Status == 1 ? "可交付" : $"{current}/{required}";

                var row = new InkNavButton(QuestWidth - 20f, 21f)
                {
                    Location = new Float2(10f, qY - 1f),
                    AnchorPreset = AnchorPresets.TopLeft,
                    FillColor = Color.Transparent,
                    BorderColor = Color.Transparent,
                };
                row.AddChild(MakeLabel(questName, 0f, 0f, 130f, 16f,
                    InkWashTheme.TextDefault, 11f, InkWashTheme.FontRole.Display));
                row.AddChild(MakeLabel(countText, 140f, 0f, 50f, 16f,
                    tint.color, 9f, InkWashTheme.FontRole.Number, TextAlignment.Far));
                row.AddChild(MakeBar(0f, 17f, 180f, 3f, progress,
                    tint.variant, InkWashTheme.Abyss, Color.Transparent));
                row.Clicked += _ => OnQuestClicked(questId);
                _questPanel.AddChild(row);
                _questRows.Add(row);
                row.Visible = !_questCollapsed;
                qY += 29f; // 21 + gap-2(8)
            }
        }

        /// <summary>折叠/展开任务追踪（折叠后仅保留标题行）。</summary>
        private void ToggleQuestCollapsed()
        {
            _questCollapsed = !_questCollapsed;
            if (_questPanel != null)
                _questPanel.Size = new Float2(QuestWidth, _questCollapsed ? QuestCollapsedHeight : QuestHeight);
            foreach (var row in _questRows)
                row.Visible = !_questCollapsed;
            if (_questEmpty != null)
                _questEmpty.Visible = !_questCollapsed;
            if (_questToggleLabel != null)
                _questToggleLabel.Text = _questCollapsed ? "+" : "-";
        }

        /// <summary>
        /// 点击任务追踪行 → 打开任务日志并定位到这一条。
        /// <para>
        /// 不在这里直接发起"走到任务地点"：<c>QuestSummary</c> 只带名称/描述/进度，协议里没有目标坐标，
        /// 客户端凭空拼一个坐标只会把玩家领到错误方向。真正的目标点要等服务端把任务目标位置下发后
        /// 再接 <c>PluginNavigationBridge</c>/<c>PlayerController.TryStartClickToMove</c>。
        /// </para>
        /// </summary>
        private void OnQuestClicked(int questId)
        {
            if (questId <= 0) return;

            HundunWorld.Game.UI.Ink.Pages.MenuQuestsPage.PendingFocusQuestId = questId;
            RequestNavigation(InkPageDomIds.NavQuests);
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
                (glyph: "装", label: "外观", domId: InkPageDomIds.NavAppearance),
                (glyph: "技", label: "技能", domId: InkPageDomIds.NavSkillTree),
                (glyph: "包", label: "背包", domId: InkPageDomIds.NavInventory),
                (glyph: "任", label: "任务", domId: InkPageDomIds.NavQuests),
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

            _hudLayers.AddToLayer(_navRow, InkHudLayer.Persistent);
        }

        // ===================================================================
        // 区域构造：左下聊天框
        // ===================================================================

        /// <summary>
        /// 左下聊天框：7 频道 Tab（附近/世界/门派/队伍/团队/密聊/系统，点击过滤，再次点击回到全部）+
        /// 可滚动的实时消息区（数据来自 ChatClient）+ 输入框（发送钮/回车）+ 清空钮（清当前频道）。
        /// </summary>
        private void BuildChatBox()
        {
            _chatPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(ChatWidth, ChatHeight),
                Radius = InkWashTheme.RadiusLg,
            };

            // Tab 行：点击切换频道过滤（激活态：bg-elevated + border-gold + 金亮字）；右上角小红点表示未读
            float tabX = 4f;
            foreach (var channel in ChatTabChannels)
            {
                ChatChannel ch = channel;
                var tab = new InkNavButton(36f, 18f)
                {
                    Location = new Float2(tabX, 3f),
                    FillColor = Color.Transparent,
                    BorderColor = Color.Transparent,
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                var lbl = MakeLabel(ChatChannelLabel(ch), 0f, 0f, 36f, 18f,
                    InkWashTheme.TextSecondary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Center);
                tab.AddChild(lbl);

                var dot = new Panel
                {
                    Location = new Float2(30f, 1f),
                    Size = new Float2(6f, 6f),
                    BackgroundColor = new Color(0.9f, 0.25f, 0.25f),
                    Visible = false,
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                tab.AddChild(dot);
                _chatTabDots.Add((dot, ch));

                tab.Clicked += _ => ToggleChatFilter(ch);
                _chatTabs.Add((tab, lbl, ch));
                _chatPanel.AddChild(tab);
                tabX += 38f;
            }

            // Tab 下分隔线
            _chatPanel.AddChild(new ContainerControl
            {
                Location = new Float2(4f, 22f),
                Size = new Float2(ChatWidth - 8f, 1f),
                BackgroundColor = InkWashTheme.BorderFaint,
                AnchorPreset = AnchorPresets.TopLeft,
            });

            // 消息区（裁剪 + 滚轮滚动；内容由 SyncChatFromClient 从 ChatClient 填充）
            _chatMessages = new InkMessageList
            {
                Location = new Float2(4f, 24f),
                Size = new Float2(ChatWidth - 8f, 136f),
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _chatMessages.SetFontSize(_chatFontSize);
            _chatPanel.AddChild(_chatMessages);

            // 输入行上分隔线
            _chatPanel.AddChild(new ContainerControl
            {
                Location = new Float2(6f, 163f),
                Size = new Float2(ChatWidth - 12f, 1f),
                BackgroundColor = InkWashTheme.BorderFaint,
                AnchorPreset = AnchorPresets.TopLeft,
            });

            // 输入框（真实 TextBox：点击聚焦后可输入）
            _chatInput = new TextBox
            {
                Location = new Float2(6f, 167f),
                Size = new Float2(240f, 24f),
                AnchorPreset = AnchorPresets.TopLeft,
                WatermarkText = "输入消息...",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.TextDefault,
                BackgroundColor = InkWashTheme.Abyss,
            };
            _chatPanel.AddChild(_chatInput);

            // 发送钮（点击发送当前输入内容）
            var send = new InkNavButton(48f, 24f)
            {
                Location = new Float2(ChatWidth - 12f - 48f, 167f),
                AnchorPreset = AnchorPresets.TopLeft,
            };
            send.AddChild(MakeLabel("发送", 0f, 0f, 48f, 24f,
                InkWashTheme.TextSecondary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            send.Clicked += _ => SendChatInput();
            _chatPanel.AddChild(send);

            // 退出密聊钮（仅在设置了密聊对象时显示）
            _chatWhisperExitBtn = new InkNavButton(22f, 24f)
            {
                Location = new Float2(250f, 167f),
                AnchorPreset = AnchorPresets.TopLeft,
                Visible = false,
            };
            _chatWhisperExitBtn.AddChild(MakeLabel("×", 0f, 0f, 22f, 24f,
                InkWashTheme.BloodBright, 12f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            _chatWhisperExitBtn.Clicked += _ => ExitWhisper();
            _chatPanel.AddChild(_chatWhisperExitBtn);

            // 清空钮（清空当前过滤频道的消息；未选频道时清空全部）
            var clear = new InkNavButton(26f, 18f)
            {
                Location = new Float2(ChatWidth - 12f - 26f - 4f - 26f, 3f),
                AnchorPreset = AnchorPresets.TopLeft,
            };
            clear.AddChild(MakeLabel("清", 0f, 0f, 26f, 18f,
                InkWashTheme.TextSecondary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            clear.Clicked += _ => ClearChat();
            _chatPanel.AddChild(clear);

            // 设置钮（聊天设置：字体大小 / 频道屏蔽）
            var settings = new InkNavButton(26f, 18f)
            {
                Location = new Float2(ChatWidth - 12f - 26f, 3f),
                AnchorPreset = AnchorPresets.TopLeft,
            };
            settings.AddChild(MakeLabel("设", 0f, 0f, 26f, 18f,
                InkWashTheme.TextSecondary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            settings.Clicked += _ => ToggleChatSettings();
            _chatPanel.AddChild(settings);

            // 点击聊天行（他人发言）→ 发起密聊；长按 → 撤回/举报；滚到顶 → 加载更早历史
            _chatMessages.LineClicked += OnChatLineClicked;
            _chatMessages.LineLongPressed += OnChatLineLongPressed;
            _chatMessages.ReachedTop += OnChatReachedTop;
            ChatClient.Instance.HistoryMerged += OnChatHistoryMerged;

            _hudLayers.AddToLayer(_chatPanel, InkHudLayer.Persistent);
            RefreshChatTabVisuals();

            // 聊天设置弹层（默认隐藏，位于聊天框上方）
            BuildChatSettings();

            // 折叠开关：展开时贴在聊天框右侧，折叠时贴到左边缘（小标签），点击切换显隐。
            _chatToggleBtn = new InkNavButton(22f, 18f)
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _chatToggleLabel = MakeLabel("收", 0f, 0f, 22f, 18f,
                InkWashTheme.GoldBright, 10f, InkWashTheme.FontRole.Body, TextAlignment.Center);
            _chatToggleBtn.AddChild(_chatToggleLabel);
            _chatToggleBtn.Clicked += _ => ToggleChatCollapsed();
            _hudLayers.AddToLayer(_chatToggleBtn, InkHudLayer.Persistent);
            ApplyChatCollapsed();
            UpdateChatInputHint();
        }

        // ===================================================================
        // 密聊交互 + 聊天设置
        // ===================================================================

        /// <summary>点击聊天行（他人发言）→ 进入与该玩家的密聊。</summary>
        private void OnChatLineClicked(ChatLine line)
        {
            var me = HundunWorldGame.Instance?.PlayerId ?? 0;
            if (line.SenderId == 0 || line.SenderId == me) return;
            StartWhisper(line.SenderId, line.SenderName);
        }

        /// <summary>长按聊天行：自己的消息 → 撤回；他人的消息 → 举报。</summary>
        private void OnChatLineLongPressed(ChatLine line)
        {
            if (line.MessageId <= 0) return;

            var me = HundunWorldGame.Instance?.PlayerId ?? 0;
            if (line.SenderId == me)
            {
                ulong receiver = line.Channel == ChatChannel.Private ? line.ReceiverId : 0;
                if (ChatClient.Instance.Recall(line.Channel, line.MessageId, receiver))
                    SyncChatFromClient();
            }
            else
            {
                if (ChatClient.Instance.Report(line.Channel, line.MessageId, "不当言论"))
                    ChatClient.Instance.AddLocalSystem("举报已提交");
            }
        }

        /// <summary>滚到顶部：加载更早历史（需处于具体频道或密聊视图；带节流，避免滚轮每格都发请求）。</summary>
        private void OnChatReachedTop()
        {
            if (_chatCollapsed) return;
            if (_chatHistoryLoading && Time.TimeSinceStartup - _chatHistoryLoadingAt < 3f) return;

            bool sent;
            if (_chatWhisperTargetId != 0)
            {
                long oldest = ChatClient.Instance.GetOldestWhisperId(_chatWhisperTargetId);
                if (oldest <= 0) return;
                sent = ChatClient.Instance.RequestHistory(ChatChannel.Private, _chatWhisperTargetId, oldest, 30);
            }
            else if (_chatFilterChannel != null)
            {
                long oldest = ChatClient.Instance.GetOldestMessageId(_chatFilterChannel.Value);
                if (oldest <= 0) return;
                sent = ChatClient.Instance.RequestHistory(_chatFilterChannel.Value, 0, oldest, 30);
            }
            else
            {
                return;   // 全部频道视图不做分页（游标语义不明确）
            }

            if (sent)
            {
                _chatHistoryLoading = true;
                _chatHistoryLoadingAt = Time.TimeSinceStartup;
            }
        }

        /// <summary>历史合并完成：结束加载态并刷新消息区。</summary>
        private void OnChatHistoryMerged()
        {
            _chatHistoryLoading = false;
            SyncChatFromClient();
        }

        private void TryConsumePendingWhisper()
        {
            if (WhisperRequest.TryConsume(out var id, out var name))
                StartWhisper(id, name);
        }

        /// <summary>进入与指定玩家的密聊会话（消息区仅显示与该玩家的双向消息）。</summary>
        private void StartWhisper(ulong characterId, string name)
        {
            _chatWhisperTargetId = characterId;
            _chatWhisperTargetName = string.IsNullOrEmpty(name) ? characterId.ToString() : name;
            _chatFilterChannel = ChatChannel.Private;
            _chatCollapsed = false;                       // 收到/发起密聊时自动展开
            if (_chatWhisperExitBtn != null) _chatWhisperExitBtn.Visible = true;
            RefreshChatTabVisuals();
            UpdateChatInputHint();
            ApplyChatCollapsed();
        }

        /// <summary>退出密聊，回到普通频道输入。</summary>
        private void ExitWhisper()
        {
            _chatWhisperTargetId = 0;
            _chatWhisperTargetName = null;
            if (_chatWhisperExitBtn != null) _chatWhisperExitBtn.Visible = false;
            UpdateChatInputHint();
            SyncChatFromClient();
        }

        /// <summary>刷新输入框占位提示（密聊时显示对象）。</summary>
        private void UpdateChatInputHint()
        {
            if (_chatInput == null) return;
            _chatInput.WatermarkText = _chatWhisperTargetId != 0
                ? $"密聊 {_chatWhisperTargetName}："
                : "输入消息...";
        }

        /// <summary>显示/隐藏聊天设置弹层。</summary>
        private void ToggleChatSettings()
        {
            if (_chatSettingsPanel == null) return;
            _chatSettingsPanel.Visible = !_chatSettingsPanel.Visible;
            PositionChatSettings();
        }

        /// <summary>设置弹层定位在聊天框上方（随屏高变化）。</summary>
        private void PositionChatSettings()
        {
            if (_chatSettingsPanel == null) return;
            float y = LayoutHeight - EdgeBottom - ChatHeight - _chatSettingsPanel.Height - 4f;
            _chatSettingsPanel.Location = new Float2(Edge, Mathf.Max(Edge, y));
        }

        /// <summary>聊天设置弹层：字体大小（小/中/大）+ 频道屏蔽（点击频道名切换）。</summary>
        private void BuildChatSettings()
        {
            _chatSettingsPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(ChatWidth, 80f),
                Radius = InkWashTheme.RadiusLg,
                Visible = false,
            };

            _chatSettingsPanel.AddChild(MakeLabel("字体", 8f, 6f, 30f, 16f,
                InkWashTheme.TextSecondary, 10f, InkWashTheme.FontRole.Body));

            var sizes = new (string Label, float Size)[]
            {
                ("小", ChatFontMin),
                ("中", 11f),
                ("大", ChatFontMax),
            };
            float sx = 42f;
            foreach (var s in sizes)
            {
                float size = s.Size;
                var b = new InkNavButton(30f, 16f)
                {
                    Location = new Float2(sx, 6f),
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                b.AddChild(MakeLabel(s.Label, 0f, 0f, 30f, 16f,
                    InkWashTheme.TextSecondary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Center));
                b.Clicked += _ => ApplyChatFont(size);
                _chatSettingsPanel.AddChild(b);
                sx += 34f;
            }

            _chatSettingsPanel.AddChild(MakeLabel("屏蔽", 8f, 30f, 30f, 16f,
                InkWashTheme.TextSecondary, 10f, InkWashTheme.FontRole.Body));

            float mx = 42f;
            foreach (var channel in ChatTabChannels)
            {
                ChatChannel ch = channel;
                var b = new InkNavButton(36f, 16f)
                {
                    Location = new Float2(mx, 30f),
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                b.AddChild(MakeLabel(ChatChannelLabel(ch), 0f, 0f, 36f, 16f,
                    InkWashTheme.TextSecondary, 10f, InkWashTheme.FontRole.Body, TextAlignment.Center));
                b.Clicked += _ =>
                {
                    ChatClient.Instance.ToggleMute(ch);
                    RefreshChatMuteButtons();
                    SyncChatFromClient();
                };
                _chatMuteButtons.Add((b, ch));
                _chatSettingsPanel.AddChild(b);
                mx += 38f;
            }

            _chatSettingsPanel.AddChild(MakeLabel("点击频道名屏蔽/取消；点击聊天行可密聊", 8f, 52f, ChatWidth - 16f, 14f,
                InkWashTheme.TextTertiary, 9f, InkWashTheme.FontRole.Body));

            _hudLayers.AddToLayer(_chatSettingsPanel, InkHudLayer.Transient);
            RefreshChatMuteButtons();
            PositionChatSettings();
        }

        /// <summary>应用聊天字号（重建消息行）。</summary>
        private void ApplyChatFont(float size)
        {
            _chatFontSize = size;
            if (_chatMessages != null) _chatMessages.SetFontSize(size);
        }

        /// <summary>刷新屏蔽按钮的激活态。</summary>
        private void RefreshChatMuteButtons()
        {
            foreach (var (button, channel) in _chatMuteButtons)
            {
                bool muted = ChatClient.Instance.IsMuted(channel);
                button.FillColor = muted ? InkWashTheme.BaseElevated : Color.Transparent;
                button.BorderColor = muted ? InkWashTheme.BloodPrimary : Color.Transparent;
            }
        }

        /// <summary>刷新 Tab 未读红点（展开且正在查看的频道标记为已读）。</summary>
        private void RefreshChatTabDots()
        {
            foreach (var (dot, channel) in _chatTabDots)
            {
                if (dot == null) continue;

                bool unread = ChatClient.Instance.GetUnread(channel) > 0;
                bool viewing = !_chatCollapsed &&
                               (_chatFilterChannel == null || _chatFilterChannel == channel);
                if (viewing)
                {
                    ChatClient.Instance.MarkRead(channel);
                    unread = false;
                }
                dot.Visible = unread;
            }
        }

        /// <summary>切换聊天框折叠状态（折叠到左侧隐藏 / 点击标签展开）。</summary>
        private void ToggleChatCollapsed()
        {
            _chatCollapsed = !_chatCollapsed;
            ApplyChatCollapsed();
        }

        /// <summary>应用折叠状态：折叠时隐藏面板并把开关贴到左边缘，展开时显示面板并同步一次消息。</summary>
        private void ApplyChatCollapsed()
        {
            if (_chatPanel != null)
                _chatPanel.Visible = !_chatCollapsed;

            if (_chatToggleBtn != null)
            {
                float y = LayoutHeight - EdgeBottom - ChatHeight + 4f;
                float x = _chatCollapsed ? Edge : Edge + ChatWidth + 2f;
                _chatToggleBtn.Location = new Float2(x, y);
            }
            if (_chatToggleLabel != null)
                _chatToggleLabel.Text = _chatCollapsed ? "聊" : "收";

            // 折叠时一并收起设置弹层；其余情况让其跟随聊天框位置。
            if (_chatSettingsPanel != null)
            {
                if (_chatCollapsed) _chatSettingsPanel.Visible = false;
                PositionChatSettings();
            }

            if (!_chatCollapsed)
                SyncChatFromClient();
        }

        /// <summary>清空当前过滤频道的聊天消息（未选频道时清空全部）。仅清空本地显示，服务端历史保留。</summary>
        private void ClearChat()
        {
            ChatClient.Instance.Clear(_chatFilterChannel);
            SyncChatFromClient();
        }

        private static string ChatChannelLabel(ChatChannel c) => InkChatChannelView.Label(c);

        private static (string Prefix, Color Color) ChatChannelStyle(ChatChannel c) => InkChatChannelView.Style(c);

        /// <summary>切换聊天频道过滤：点击非当前频道→只看该频道；再次点击→显示全部。切换时退出密聊视图。</summary>
        private void ToggleChatFilter(ChatChannel channel)
        {
            if (_chatWhisperTargetId != 0)
            {
                _chatWhisperTargetId = 0;
                _chatWhisperTargetName = null;
                if (_chatWhisperExitBtn != null) _chatWhisperExitBtn.Visible = false;
                UpdateChatInputHint();
            }

            _chatFilterChannel = _chatFilterChannel == channel ? (ChatChannel?)null : channel;
            RefreshChatTabVisuals();
            SyncChatFromClient();
        }

        /// <summary>刷新频道 Tab 的激活态样式。</summary>
        private void RefreshChatTabVisuals()
        {
            foreach (var (button, label, channel) in _chatTabs)
            {
                bool active = _chatFilterChannel == channel;
                button.FillColor = active ? InkWashTheme.BaseElevated : Color.Transparent;
                button.BorderColor = active ? InkWashTheme.BorderGold : Color.Transparent;
                label.TextColor = active ? InkWashTheme.GoldBright : InkWashTheme.TextSecondary;
            }
        }

        /// <summary>发送当前输入框内容（密聊视图发给指定对象；否则按过滤频道，无过滤时默认世界）。</summary>
        private void SendChatInput()
        {
            if (_chatInput == null) return;
            var text = _chatInput.Text;
            if (string.IsNullOrWhiteSpace(text)) return;

            if (_chatWhisperTargetId != 0)
            {
                if (ChatClient.Instance.Send(ChatChannel.Private, text, _chatWhisperTargetId))
                    _chatInput.Text = string.Empty;
                return;
            }

            var channel = _chatFilterChannel ?? ChatChannel.World;
            if (ChatClient.Instance.Send(channel, text))
                _chatInput.Text = string.Empty;
        }

        /// <summary>从 ChatClient 同步可见消息到消息区（节流调用）；密聊视图下只显示与目标的会话。</summary>
        private void SyncChatFromClient()
        {
            if (_chatMessages == null) return;
            ChatClient.Instance.EnsureAttached();

            var lines = new List<(ChatLine Line, string Prefix, Color Color, string Text)>();

            if (_chatWhisperTargetId != 0)
            {
                var wStyle = ChatChannelStyle(ChatChannel.Private);
                foreach (var line in ChatClient.Instance.GetWhisper(_chatWhisperTargetId))
                    lines.Add((line, wStyle.Prefix, wStyle.Color, $" {line.SenderName}：{line.Content}"));
            }
            else
            {
                foreach (var channel in ChatTabChannels)
                {
                    if (_chatFilterChannel != null && channel != _chatFilterChannel) continue;

                    var style = ChatChannelStyle(channel);
                    foreach (var line in ChatClient.Instance.GetMessages(channel))
                        lines.Add((line, style.Prefix, style.Color, $" {line.SenderName}：{line.Content}"));
                }
            }

            const int maxLines = 80;
            if (lines.Count > maxLines)
                lines.RemoveRange(0, lines.Count - maxLines);

            _chatMessages.SetLines(lines);
            RefreshChatTabDots();
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
            // 对齐设计 combat-hud-traditional.html：本页（传统式）的切换按钮 dom-id 为 toggle-immersive（→ 沉浸式 V2）。
            float toggleW = 100f;
            var toggle = new InkNavButton(toggleW, ToggleH)
            {
                Location = new Float2(FuncColW - toggleW, 0f),
                BorderColor = InkWashTheme.GoldPrimary,
                DomId = InkPageDomIds.ToggleImmersive,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            toggle.AddChild(MakeLabel("眼", 10f, 0f, 12f, ToggleH,
                InkWashTheme.GoldBright, 12f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            toggle.AddChild(MakeLabel("切换沉浸模式", 28f, 0f, 62f, ToggleH,
                InkWashTheme.GoldBright, 10f, InkWashTheme.FontRole.Display));
            toggle.Clicked += b => RequestNavigation(b.DomId);
            _funcColumn.AddChild(toggle);

            // 功能钮：竖向排列（42x38，gap 4px），与切换钮右对齐于列宽
            // 键位提示只写真的：C 确实绑在 NavInventory（角色属性=行囊合并入口，见 MainUIManager 的 2c），
            // 而 B/K/ESC 从来没有绑到这几个页面上——挂着假提示等于教玩家一个按了没反应的键。
            // ESC 现在是系统菜单（继续游戏/设置/退出），不在此列。
            var funcDefs = new[]
            {
                (glyph: "包", label: "背包 C", domId: InkPageDomIds.NavInventory),
                (glyph: "人", label: "角色", domId: InkPageDomIds.NavCharacterPanel),
                (glyph: "装", label: "外观", domId: InkPageDomIds.NavAppearance),
                (glyph: "技", label: "技能", domId: InkPageDomIds.NavSkillTree),
                (glyph: "设", label: "设置", domId: InkPageDomIds.NavSettings),
            };

            float btnX = FuncColW - FuncBtnW;   // 右对齐
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
                btnY += FuncBtnH + FuncRowGap;
            }

            _hudLayers.AddToLayer(_funcColumn, InkHudLayer.Persistent);

            // 退出游戏圆钮：功能列左侧、与其底对齐；点击返回角色选择（同断线超时流程）。
            _exitGameButton = MakeCircle(0f, 0f, ExitBtnSize,
                InkWashTheme.PanelSolid, InkWashTheme.BorderGold, 1f,
                "退", InkWashTheme.BloodBright, 14f, InkWashTheme.FontRole.Body);
            _exitGameButton.Clicked += () => ExitGameToCharacterSelection();
            _hudLayers.AddToLayer(_exitGameButton, InkHudLayer.Context);
        }

        /// <summary>
        /// 退出游戏：返回角色选择界面并切换全局状态。
        /// 与 <c>HundunWorldGame</c> 断线超时/重连失败的回退路径保持同一流程；
        /// 服务端随后经断线感知完成 Despawn/离线（位置缓存由 DS 写回/ZoneShard tick 保持新鲜）。
        /// </summary>
        private void ExitGameToCharacterSelection()
        {
            var sceneManager = HundunWorld.Game.UI.GameSceneManager.Instance;
            if (sceneManager != null)
                sceneManager.TransitionTo(Horizon.Game.Message.Enums.SceneType.CharacterSelection);

            var gameState = HundunWorld.Game.Core.GameStateManager.Instance;
            if (gameState != null)
                gameState.ChangeState(HundunWorld.Game.Core.GameState.CharacterSelect);
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
        /// 按当前屏幕尺寸应用布局（边距 12px，底部导航 bottom 220，聊天框/功能钮组距底部 20px，功能钮竖向排列）。
        /// </summary>
        /// <summary>底部修为经验条（9 格 + 跨格连续渐变 + 数值）。</summary>
        private void BuildExpBar()
        {
            _expBottomBar = new InkExpBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(100f, ExpBarHeight),
            };
            _hudLayers.AddToLayer(_expBottomBar, InkHudLayer.Persistent);
        }

        /// <summary>布局实际宽/高：优先页面自身尺寸（StretchAll 后即设计空间尺寸），回退 _screenSize。</summary>
        private float LayoutWidth => Width > 0f ? Width : _screenSize.X;
        private float LayoutHeight => Height > 0f ? Height : _screenSize.Y;

        private void ApplyLayout()
        {
            float sw = LayoutWidth;
            float sh = LayoutHeight;

            if (_compassBar != null)
                _compassBar.Location = new Float2(sw * 0.5f - CompassWidth * 0.5f, CompassTop);

            if (_targetPanel != null)
                _targetPanel.Location = new Float2(sw * 0.5f - TargetWidth * 0.5f, CompassTop + CompassHeight + CompassGapBelowTarget);

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

            if (_chatPanel != null)
                _chatPanel.Location = new Float2(Edge, sh - EdgeBottom - ChatHeight);

            if (_funcColumn != null)
            {
                _funcColumn.Location = new Float2(sw - FuncColW - Edge, sh - EdgeBottom - FuncColH);

                // 退出钮贴功能列左侧、底对齐
                if (_exitGameButton != null)
                    _exitGameButton.Location = new Float2(
                        sw - Edge - FuncColW - 8f - ExitBtnSize,
                        sh - EdgeBottom - ExitBtnSize);
            }

            if (_expBottomBar != null)
            {
                _expBottomBar.Location = new Float2(Edge, sh - ExpBarBottom - ExpBarHeight);
                _expBottomBar.Size = new Float2(Mathf.Max(0f, sw - 2f * Edge), ExpBarHeight);
            }

            // 聊天折叠开关位置随屏幕尺寸更新（Y 依赖屏高，X 依赖折叠状态）。
            ApplyChatCollapsed();
        }

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (_boundCharacter != null)
                RefreshBoundCharacterDisplay();

            _controlChipRefreshTimer += deltaTime;
            if (_controlChipRefreshTimer >= 0.15f)
            {
                _controlChipRefreshTimer = 0f;
                RefreshControlChips();
            }

            // 任务清单是网络回包（连接线程写入），这里只在主线程读一个版本号决定是否重画，
            // 避免在回调线程上直接动 GUI。
            _questRequestTimer -= deltaTime;
            if (_questRequestTimer <= 0f)
            {
                _questRequestTimer = 10f;
                HundunWorld.Game.Network.QuestClientService.Instance.RequestList();
            }

            var questRevision = HundunWorld.Game.Network.QuestClientService.Instance.Revision;
            if (questRevision != _questRevision)
            {
                _questRevision = questRevision;
                RebuildQuestRows();
            }

            _targetRefreshTimer += deltaTime;

            // 用 Flax 的 == 判存活：目标 Actor 被销毁（击杀后 Despawn、离开 AOI）后，
            // 托管引用还在，但原生对象已没了，只有 Flax 运算符能识破。
            var selected = HundunWorld.Game.Selection.WorldSelectionService.Instance?.Selected;
            bool alive = selected != null && selected.Actor != null && selected.Actor.IsActive;
            bool identityChanged = !ReferenceEquals(selected, _lastTargetRef) || alive != _lastTargetAlive;

            if (identityChanged || _targetRefreshTimer >= TargetRefreshInterval)
            {
                _lastTargetRef = selected;
                _lastTargetAlive = alive;
                _targetRefreshTimer = 0f;
                RefreshTargetInfo();
            }
            if (_minimap != null)
                _minimap.PlayerYaw = _minimapPlayerYaw;
            if (_compassBar != null)
            {
                _compassBar.PlayerYaw = _minimapPlayerYaw;

                _compassRefreshTimer += deltaTime;
                if (_compassRefreshTimer >= CompassRefreshInterval)
                {
                    _compassRefreshTimer = 0f;
                    RefreshCompassMarkers();
                }
            }

            // 坐标条：显示角色实际世界坐标（X, Z 水平面），与 minimap 同源（插件导航位姿）
            if (_coordLabel != null)
            {
                _coordRefreshTimer += deltaTime;
                if (_coordRefreshTimer >= CoordRefreshInterval)
                {
                    _coordRefreshTimer = 0f;
                    if (PluginNavigationBridge.TryGetPlayerPose(out var pose, out _))
                        _coordLabel.Text = $"{Mathf.RoundToInt(pose.X)}, {Mathf.RoundToInt(pose.Z)}";
                }
            }

            // 聊天：展开时节流从 ChatClient 同步可见消息；输入框聚焦时回车发送（折叠时跳过）。
            _chatSyncTimer += deltaTime;
            if (!_chatCollapsed && _chatSyncTimer >= ChatSyncInterval)
            {
                _chatSyncTimer = 0f;
                SyncChatFromClient();
            }
            if (!_chatCollapsed && _chatInput != null && _chatInput.IsFocused && FlaxEngine.Input.GetKeyDown(KeyboardKeys.Return))
                SendChatInput();
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
