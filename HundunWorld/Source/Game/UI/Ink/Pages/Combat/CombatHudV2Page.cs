using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.Character;
using HundunWorld.Game.Navigation;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;
using System;
using Game.Character.Attributes;

namespace HundunWorld.Game.UI.Ink.Pages.Combat
{
    /// <summary>
    /// 战斗 HUD V2 页面（沉浸式战斗抬头显示，对应 hundun-yy-ui/pages/combat-hud-v2.html）。
    /// 九大区域（位置/尺寸与设计稿 px 值一一对应）：
    /// <list type="bullet">
    ///   <item>背景层：深墨渐变 + 水墨晕染 + 远景墨山 + 场景占位（游戏场景 · 墨麒麟巢穴）</item>
    ///   <item>左上：小地图（120 圆形罗盘 + 敌我点位 + 视线扇形 + 坐标 昆仑墟 · 深渊）</item>
    ///   <item>顶部中央：目标信息（36 目标头像 + 墨麒麟 + 传说徽章 + Lv. 50 · 首领 + 18m + 血条 + 3 条 meta）</item>
    ///   <item>右上：队伍状态（队伍 (4/4) + 同步徽章 + 3 名成员血/内力条）</item>
    ///   <item>右上（队伍下方）：状态效果（攻击力 +10% / 防御力 +15% / 中毒 · 每秒 -120）</item>
    ///   <item>左中：连击计数器（连击 23 + COMBO + 计时条 + 倍率 ×1.8）</item>
    ///   <item>左下：玩家状态（48 圆形头像 + 江湖过客 Lv.42 剑客 + 血/气条 + 经验条 + 3 个 buff 小图标）</item>
    ///   <item>底部中央：技能栏（8 槽 48x48 剑1 遁2 防3 攻4 轻Q 绝E 药R 食F + 键位提示）</item>
    ///   <item>右下：道具栏（血 ×5 / 气 ×3 / 解 ×2 / 烟 ×1）</item>
    /// </list>
    /// 交互出口：隐藏退出按钮（设计稿 <c>data-dom-id="back-hud"</c>，不可见，仅作为链路终点）。
    /// 通过 <see cref="NavigationRequested"/> 事件向 <see cref="InkPageRouter"/> 暴露导航请求。
    /// </summary>
    public class CombatHudV2Page : ContainerControl, IInkPage, IInkNavigationSource
    {
        // ===================================================================
        // 布局常量（1920x1080 参考分辨率，px 值对齐 combat-hud-v2.html）
        // =======================================================================

        /// <summary>屏幕边缘统一边距（--ink-space-5，四角 24px）</summary>
        private const float Edge = 24f;

        // --- 背景层 ---
        private const float BackdropGradientSteps = 14;
        private const float MountainNearHeight = 220f;
        private const float MountainFarHeight = 180f;
        private const float MountainFadeSteps = 18;

        // --- 左上：小地图 ---
        private const float MinimapDiscSize = 120f;
        private const float MinimapCoordGap = 4f;
        private const float MinimapCoordHeight = 16f;
        private const float VisionConeSize = 70f;
        private const float VisionConeHalfAngle = 30f;

        // --- 顶部中央：目标信息 ---
        private const float TargetWidth = 360f;
        private const float TargetPadX = 16f;
        private const float TargetPadY = 8f;
        private const float TargetAvatarSize = 36f;
        private const float TargetHeaderHeight = 36f;
        private const float TargetHpBarHeight = 10f;
        private const float TargetMetaHeight = 18f;
        private const float TargetHeight = TargetPadY + TargetHeaderHeight + 8f + TargetHpBarHeight + 4f + TargetMetaHeight + TargetPadY;

        // --- 右上：队伍状态 ---
        private const float PartyWidth = 220f;
        private const float PartyHeaderHeight = 20f;
        private const float PartyRowGap = 4f;
        // 没有队伍名册服务：面板只保留头部 + 一行"未组队"，不再为 3 名示例队友留空白高度
        private const float PartyHeight = PartyHeaderHeight + PartyRowGap + 24f;

        // --- 右上（队伍下方）：状态效果 ---
        private const float BuffPanelTop = 256f;
        private const float BuffPanelWidth = 220f;
        private const float BuffHeaderHeight = 20f;
        private const float BuffRowHeight = 34f;
        private const float BuffRowGap = 4f;
        private const float BuffGlyphSize = 24f;
        private const int BuffCount = 3;
        private const float BuffPanelHeight = BuffHeaderHeight + BuffRowGap
            + BuffCount * BuffRowHeight + (BuffCount - 1) * BuffRowGap;

        // --- 左中：连击计数器 ---
        private const float ComboLeft = 32f;
        private const float ComboWidth = 100f;
        private const float ComboHeight = 122f;
        private const float ComboTimerWidth = 60f;
        private const float ComboTimerHeight = 4f;

        // --- 左下：玩家状态 ---
        private const float PlayerAvatarSize = 48f;
        private const float PlayerAvatarGap = 8f;
        private const float PlayerBarWidth = 280f;
        private const float PlayerHpBarHeight = 12f;
        private const float PlayerMpBarHeight = 8f;
        private const float PlayerXpBarHeight = 2f;
        private const float PlayerNameRowHeight = 20f;
        private const float PlayerXpRowHeight = 14f;
        private const float PlayerBuffChipSize = 24f;
        private const float PlayerBuffTimeHeight = 12f;
        private const float PlayerBuffChipGap = 6f;
        private const int PlayerBuffCount = 3;
        private const float PlayerBodyWidth = 32f + PlayerBarWidth;
        private const float PlayerPanelWidth = PlayerAvatarSize + PlayerAvatarGap + PlayerBodyWidth;
        private const float PlayerPanelHeight = PlayerNameRowHeight + 4f + PlayerHpBarHeight + 4f
            + PlayerMpBarHeight + 4f + PlayerXpRowHeight + 4f + PlayerBuffChipSize + PlayerBuffTimeHeight;

        // --- 底部中央：技能栏 ---
        private const float SkillSlotSize = 48f;
        private const float SkillSlotGap = 4f;
        private const float SkillRowPadding = 6f;
        private const int SkillSlotCount = 8;
        private const float SkillRowWidth = SkillRowPadding * 2f + SkillSlotCount * SkillSlotSize
            + (SkillSlotCount - 1) * SkillSlotGap;
        private const float SkillRowHeight = SkillRowPadding * 2f + SkillSlotSize;
        private const float SkillHintHeight = 14f;
        private const float SkillHintGap = 6f;
        private const float SkillBarHeight = SkillRowHeight + SkillHintGap + SkillHintHeight;

        // --- 右下：道具栏 ---
        private const float ItemSlotSize = 36f;
        private const float ItemSlotGap = 4f;
        private const float ItemColumnPadding = 6f;
        private const float ItemHeaderHeight = 18f;
        private const float ItemHeaderGap = 6f;
        private const int ItemSlotCount = 4;
        private const float ItemColumnWidth = ItemColumnPadding * 2f + ItemSlotSize;
        private const float ItemColumnHeight = ItemColumnPadding * 2f + ItemSlotCount * ItemSlotSize
            + (ItemSlotCount - 1) * ItemSlotGap;
        private const float ItemPanelHeight = ItemHeaderHeight + ItemHeaderGap + ItemColumnHeight;

        // ===================================================================
        // 设计稿文案（combat-hud-v2.html 可见文案，作为唯一事实来源）
        // =======================================================================

        // 背景层 .scene-placeholder

        // 小地图坐标 .minimap-coord
        /// <summary>小地图下方坐标占位：没有位姿时显示「—」，由 Update 用真实坐标覆盖。</summary>
        private const string MinimapCoordText = "—";

        // 目标信息 .hud-target
        private const string TargetAvatarGlyph = "麟";
        private const string TargetNameText = "墨麒麟";
        private const string TargetQualityText = "传说";
        private const string TargetLevelText = "Lv. 50 · 首领";
        private const string TargetDistanceText = "18m";
        private const string TargetHpText = "18,500 / 25,000";
        private const float TargetHpRatio = 0.74f;
        private const string TargetMetaWeaknessText = "弱点·雷";
        private const string TargetMetaEnrageText = "狂暴 imminent";
        private const string TargetMetaTimerText = "剩余 03:42";

        // 队伍状态 .hud-party
        private const string PartyTitleText = "队伍 (0/4)";
        private const string PartySyncText = "同步";
        // 状态效果 .hud-buffs
        private const string BuffTitleText = "状态效果";
        private const string BuffAttackText = "攻击力 +10%";
        private const string BuffAttackTime = "5:30";
        private const string BuffDefenseText = "防御力 +15%";
        private const string BuffDefenseTime = "8:45";
        private const string BuffPoisonText = "中毒 · 每秒 -120";
        private const string BuffPoisonTime = "2:15";

        // 连击计数器 .hud-combo
        private const string ComboLabelText = "连击";
        private const string ComboNumberText = "23";
        private const string ComboSubText = "COMBO";
        private const string ComboMultiplierText = "倍率 ×1.8";
        private const float ComboTimerRatio = 0.68f;

        // 玩家状态 .hud-player
        private const string PlayerAvatarGlyph = "客";
        private const string PlayerNameText = "—";
        private const string PlayerLevelText = "Lv.—";
        private const string PlayerClassText = "剑客";
        private const string PlayerHpText = "— / —";
        private const string PlayerMpText = "1,850 / 2,000";
        private const string PlayerXpLabelText = "经验";
        private const float PlayerHpRatio = 1f;
        private const float PlayerMpRatio = 0.925f;
        private const float PlayerXpRatio = 0.54f;

        // 技能栏 .hud-skillbar
        private const string SkillHintText = "数字键 / Q E R F 释放 · 长按蓄力";

        // 道具栏 .hud-items
        private const string ItemTitleText = "道具";

        /// <summary>技能槽配置：字符、快捷键、冷却剩余、冷却上限、是否大招、悬停说明（对齐设计 aria-label）</summary>
        private static readonly (string Glyph, string Key, float Cooldown, float MaxCooldown, bool Ultimate, string Tooltip)[] SkillConfig =
        {
            ("剑", "1", 0f, 0f, false, "剑招 按键1"),
            ("遁", "2", 3f, 10f, false, "遁术 按键2 冷却中"),
            ("防", "3", 0f, 0f, false, "防御 按键3"),
            ("攻", "4", 0f, 0f, false, "强攻 按键4"),
            ("轻", "Q", 8f, 15f, false, "轻功 按键Q 冷却中"),
            ("绝", "E", 0f, 0f, true, "绝学 按键E 就绪"),
            ("药", "R", 0f, 0f, false, "服药 按键R"),
            ("食", "F", 0f, 0f, false, "进食 按键F"),
        };

        /// <summary>道具槽配置：字符、数量、色调、名称（对齐设计 aria-label）</summary>
        private static readonly (string Glyph, int Count, Color Tone, Color Border, string Name)[] ItemConfig =
        {
            ("血", 5, InkWashTheme.VermilionBright, InkWashTheme.VermilionDeep, "回血丹"),
            ("气", 3, InkWashTheme.GoldBright, InkWashTheme.GoldDeep, "回气丹"),
            ("解", 2, InkWashTheme.JadeBright, InkWashTheme.JadePrimary, "解毒散"),
            ("烟", 1, InkWashTheme.PaperAged, InkWashTheme.BorderNeutralL2, "烟幕弹"),
        };

        // ===================================================================
        // 子控件引用
        // =======================================================================

        // 背景层

        // 左上：小地图
        private InkHudLayerHost _hudLayers;

        private ContainerControl _minimapRoot;
        private InkMinimap _minimap;
        private HudVisionCone _visionCone;
        private Label _minimapCoordLabel;

        // 顶部中央：目标信息
        private HudFrame _targetPanel;
        private InkBar _targetHpBar;
        private Label _targetHpValueLabel;
        private Label _targetAvatarLabel;
        private Label _targetNameLabel;
        private InkTag _targetQualityTag;
        private Label _targetLevelLabel;
        private Label _targetDistanceLabel;
        private readonly System.Collections.Generic.List<HudRoundCell> _targetMetaChips = new System.Collections.Generic.List<HudRoundCell>();

        // 右上：队伍状态
        private ContainerControl _partyPanel;

        // 右上：状态效果
        private ContainerControl _buffPanel;

        // 左中：连击计数器
        private HudFrame _comboPanel;
        private Label _comboNumberLabel;
        private InkBar _comboTimerBar;

        // 左下：玩家状态
        private ContainerControl _playerPanel;
        private HudCircle _playerAvatar;
        private Label _playerAvatarGlyph;
        private Label _playerNameLabel;
        private Label _playerLevelLabel;
        private InkBar _hpBar;
        private Label _hpValueLabel;
        private InkBar _mpBar;
        private Label _mpValueLabel;
        private InkBar _xpBar;

        // 玩家状态行：真实控制状态（眩晕/沉默/定身…），替代原 3 个假 Buff 图标
        private ControlStateChipView[] _controlChips;
        private float _controlChipRefreshTimer;
        private const int ControlChipSlots = 6;

        // 底部中央：技能栏
        private HudFrame _skillBarRow;
        private ContainerControl _skillHintRow;
        private HudSkillSlot[] _skillSlots;

        // 右下：道具栏
        private ContainerControl _itemTitleRow;
        private HudFrame _itemColumn;
        private HudItemSlot[] _itemSlots;
        private Label[] _itemCountLabels;

        // 隐藏退出按钮（设计稿 .hud-exit-hidden）
        private HudExitHotspot _exitHotspot;

        // 底部系统导航栏（共享壳契约：战斗 HUD 底部系统入口，串联各 UI 子系统）
        private InkPanel _sysNav;

        // ===================================================================
        // 运行期状态
        // =======================================================================

        private Float2 _screenSize;
        private CharacterAttributesComponent _boundCharacter;

        /// <summary>
        /// 导航请求事件。参数为目标页面的 dom-id。
        /// 由 <see cref="InkPageRouter"/> 订阅以执行页面跳转。
        /// </summary>
        public event Action<string> NavigationRequested;

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：按设计稿构建九大 HUD 区域，使用设计稿 mock 数据填充。
        /// </summary>
        public CombatHudV2Page()
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

                BuildMinimap();
                BuildTargetPanel();
                BuildPartyPanel();
                BuildBuffPanel();
                BuildComboCounter();
                BuildPlayerPanel();
                BuildSkillBar();
                BuildItemColumn();
                BuildSystemNav();
                BuildHiddenExit();

                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CombatHudV2Page] 初始化失败: {ex.Message}\n{ex.StackTrace}");
                // 编辑器磁盘日志有滞后，构造失败时另留一份文件证据，否则只能看到整页空壳
                try
                {
                    var probe = System.IO.Path.Combine(
                        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                        "HundunWorld", "hud-ctor-probe.out");
                    System.IO.File.AppendAllText(probe, "[CombatHudV2Page] " + ex + "\n");
                }
                catch { }
            }
        }

        // ===================================================================
        // 辅助构造方法
        // =======================================================================

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
        private HudCircle MakeCircle(float x, float y, float size,
            Color fill, Color border, float borderThickness,
            string glyph, Color glyphColor, float glyphSize, InkWashTheme.FontRole glyphRole,
            Color glow = default, Color overlay = default, float overlayRatio = 1f)
        {
            var circle = new HudCircle
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
                circle.AddChild(new Label
                {
                    AnchorPreset = AnchorPresets.StretchAll,
                    Text = glyph,
                    TextColor = glyphColor,
                    Font = InkRenderHelper.GetFontRef(glyphRole, glyphSize),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                });
            }
            return circle;
        }

        /// <summary>创建半透明墨底（面板填充，透明度 0.92 对齐设计 .hud-* 的 opacity）。</summary>
        private static Color InkFill(Color baseColor, float alpha)
        {
            return new Color(baseColor.R, baseColor.G, baseColor.B, alpha);
        }

        // ===================================================================
        // 区域构造：背景层
        // =======================================================================

        /// <summary>
        /// 背景层：深墨渐变 + 4 处水墨晕染 + 远景墨山剪影 + 场景占位文字。
        /// 对齐设计 .hud-background / .ink-splash / .ink-mountain / .scene-placeholder。
        /// </summary>
        // ===================================================================
        // 区域构造：左上小地图
        // =======================================================================

        /// <summary>
        /// 左上小地图：120 圆形罗盘（内置东南西北方位字）+ 敌我点位 + 视线扇形 + 坐标行。
        /// 对齐设计 .hud-minimap / .minimap-disc / .minimap-vision / .minimap-coord。
        /// </summary>
        private void BuildMinimap()
        {
            _minimapRoot = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(MinimapDiscSize, MinimapDiscSize + MinimapCoordGap + MinimapCoordHeight),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };

            // 圆盘裁剪容器（设计 .minimap-disc overflow:hidden）
            var disc = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(MinimapDiscSize, MinimapDiscSize),
                BackgroundColor = Color.Transparent,
                ClipChildren = true,
            };

            _minimap = new InkMinimap
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(MinimapDiscSize, MinimapDiscSize),
                // 小地图显示半径（世界单位）：以玩家为圆心仅显示附近标记；
                // 默认 30000 过大，会让 POI 全部挤在圆心附近而不可见。
                NavigationRange = 1200f,
            };
            // 与「图」入口同一出口：小地图只看附近一圈，点它就是要看大地图
            _minimap.Clicked += () => RequestNavigation(InkPageDomIds.NavWorldMap);

            // 地形快照（墨色山水意象）
            _minimap.AddLandmark(0.10f, 0.25f, 0.32f, InkFill(InkWashTheme.JadeFaint, 0.35f));
            _minimap.AddLandmark(-0.30f, -0.20f, 0.26f, InkFill(InkWashTheme.GoldTrace, 0.80f));
            _minimap.AddLandmark(0.42f, -0.38f, 0.18f, InkFill(InkWashTheme.BaseElevated, 0.90f));
            _minimap.AddLandmark(-0.44f, 0.44f, 0.20f, InkFill(InkWashTheme.BaseElevated, 0.90f));

            // 点位：自己（居中）+ 2 敌人 + 3 队友（对齐设计 map-dot 布局）
            // 小地图上的敌人/队友点必须来自真实同步或导航 POI：
            // 原来这几个是写死的「2 敌人 + 3 队友」样例点，导航不可用时会一直挂在地图上骗人。
            // 只保留"自己"这一个真实点，其余由 InkMinimap 每 0.5s 从导航刷新写入。
            _minimap.AddEntity(InkMinimapEntityType.Player, 0f, 0f);
            disc.AddChild(_minimap);

            // 视线扇形（设计 .minimap-vision：从圆心向上张开 60deg）
            _visionCone = new HudVisionCone
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(VisionConeSize, VisionConeSize),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
                HalfAngleDeg = VisionConeHalfAngle,
            };
            disc.AddChild(_visionCone);

            _minimapRoot.AddChild(disc);

            // 坐标行（图标 + 昆仑墟 · 深渊）
            float coordY = MinimapDiscSize + MinimapCoordGap;
            // 原来这个小字是"向"（配合地名"向昆仑墟·深渊"）；坐标化之后"向 X:414 Z:353"读不通，换成"坐标"
            _minimapRoot.AddChild(MakeLabel("坐标", 18f, coordY + 1f, 22f, MinimapCoordHeight - 2f,
                InkWashTheme.GoldPrimary, 11f, InkWashTheme.FontRole.Display, TextAlignment.Center));
            _minimapCoordLabel = MakeLabel(MinimapCoordText, 42f, coordY, 84f, MinimapCoordHeight,
                InkWashTheme.PaperAged, 11f, InkWashTheme.FontRole.Display);
            _minimapRoot.AddChild(_minimapCoordLabel);

            _hudLayers.AddToLayer(_minimapRoot, InkHudLayer.Persistent);
        }

        // ===================================================================
        // 区域构造：顶部中央目标信息
        // =======================================================================

        /// <summary>
        /// 顶部中央目标信息：36 目标头像 + 墨麒麟/传说 + Lv. 50 · 首领 + 18m +
        /// 10px 朱红血条与数值 + 3 条 meta（弱点·雷 / 狂暴 imminent / 剩余 03:42）。
        /// 对齐设计 .hud-target。
        /// </summary>
        private void BuildTargetPanel()
        {
            _targetPanel = new HudFrame
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(TargetWidth, TargetHeight),
                Radius = InkWashTheme.RadiusLg,
                FillColor = InkFill(InkWashTheme.BaseSecondary, 0.92f),
                BorderColor = InkWashTheme.VermilionDeep,
                BorderThickness = 1f,
                GlowColor = InkFill(InkWashTheme.VermilionPrimary, 0.18f),
            };

            // 目标头像（朱红渐变 + 纸色"麟"字）
            var avatar = new HudRoundCell
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(TargetPadX, TargetPadY),
                Size = new Float2(TargetAvatarSize, TargetAvatarSize),
                Radius = InkWashTheme.RadiusMd,
                FillColor = InkWashTheme.VermilionFaded,
                BorderColor = InkWashTheme.VermilionBright,
                BorderThickness = 1f,
            };
            _targetAvatarLabel = MakeLabel(TargetAvatarGlyph, 0f, 0f, TargetAvatarSize, TargetAvatarSize,
                InkWashTheme.PaperBright, 18f, InkWashTheme.FontRole.Display, TextAlignment.Center);
            avatar.AddChild(_targetAvatarLabel);
            _targetPanel.AddChild(avatar);

            // 名称 + 品质徽章 + 等级行
            float colX = TargetPadX + TargetAvatarSize + 8f;
            _targetNameLabel = MakeLabel(TargetNameText, colX, TargetPadY, 118f, 18f,
                InkWashTheme.VermilionBright, 16f, InkWashTheme.FontRole.Display);
            _targetPanel.AddChild(_targetNameLabel);

            _targetQualityTag = new InkTag
            {
                Text = TargetQualityText,
                TagVariant = InkTagVariant.Vermilion,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(colX + 122f, TargetPadY + 1f),
                Size = new Float2(48f, 16f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
            };
            _targetPanel.AddChild(_targetQualityTag);

            _targetLevelLabel = MakeLabel(TargetLevelText, colX, TargetPadY + 19f, 160f, 14f,
                InkWashTheme.PaperFaded, 12f, InkWashTheme.FontRole.Body);
            _targetPanel.AddChild(_targetLevelLabel);

            // 距离（图标 + 18m）
            _targetPanel.AddChild(MakeLabel("距", TargetWidth - TargetPadX - 54f, TargetPadY + 2f, 14f, 14f,
                InkWashTheme.VermilionBright, 11f, InkWashTheme.FontRole.Display, TextAlignment.Center));
            _targetDistanceLabel = MakeLabel(TargetDistanceText, TargetWidth - TargetPadX - 38f, TargetPadY, 38f, 16f,
                InkWashTheme.PaperAged, 11f, InkWashTheme.FontRole.Number, TextAlignment.Far);
            _targetPanel.AddChild(_targetDistanceLabel);

            // 血条（10px 朱红）+ 数值
            float hpY = TargetPadY + TargetHeaderHeight + 8f;
            float hpTextW = 92f;
            _targetHpBar = MakeBar(TargetPadX, hpY, TargetWidth - TargetPadX * 2f - hpTextW - 8f, TargetHpBarHeight,
                TargetHpRatio, InkBarFillVariant.Vermilion, InkFill(InkWashTheme.Abyss, 0.85f), InkWashTheme.BorderVermilion);
            _targetPanel.AddChild(_targetHpBar);

            _targetHpValueLabel = MakeLabel(TargetHpText, TargetWidth - TargetPadX - hpTextW, hpY - 2f, hpTextW, 14f,
                InkWashTheme.PaperBright, 10f, InkWashTheme.FontRole.Number, TextAlignment.Far);
            _targetPanel.AddChild(_targetHpValueLabel);

            // meta 行（3 条状态标签）
            float metaY = hpY + TargetHpBarHeight + 4f;
            float metaX = TargetPadX;
            metaX = AddTargetMetaTag(metaX, metaY, 68f, "武", TargetMetaWeaknessText,
                InkWashTheme.PaperAged, InkFill(InkWashTheme.GoldPrimary, 0.04f), InkWashTheme.BorderNeutralL1);
            metaX = AddTargetMetaTag(metaX, metaY, 106f, "怒", TargetMetaEnrageText,
                InkWashTheme.VermilionBright, InkFill(InkWashTheme.VermilionPrimary, 0.10f), InkWashTheme.VermilionDeep);
            AddTargetMetaTag(metaX, metaY, 82f, "时", TargetMetaTimerText,
                InkWashTheme.PaperAged, InkFill(InkWashTheme.GoldPrimary, 0.04f), InkWashTheme.BorderNeutralL1);

            _hudLayers.AddToLayer(_targetPanel, InkHudLayer.Context);

            // weakness/enrage/timer 三条 meta 没有数据源，先藏起来；目标本体由 RefreshTargetInfo 驱动显隐
            foreach (var chip in _targetMetaChips)
            {
                if (chip != null) chip.Visible = false;
            }

            _targetPanel.Visible = false;
        }

        /// <summary>
        /// 按世界选中服务刷新顶部目标框；没有选中/目标已失效时整框隐藏。
        /// 字段口径与传统 HUD 的 <c>RefreshTargetInfo</c> 保持一致（同一份 <c>WorldSelectionService</c>）。
        /// </summary>
        public void RefreshTargetInfo()
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

            if (_targetNameLabel != null)
                _targetNameLabel.Text = name;
            if (_targetAvatarLabel != null && !string.IsNullOrEmpty(name))
                _targetAvatarLabel.Text = name.Substring(0, 1);
            if (_targetLevelLabel != null)
                _targetLevelLabel.Text = selected.Level > 0 ? $"Lv. {selected.Level}" : string.Empty;

            if (_targetQualityTag != null)
            {
                string kind = selected.Kind switch
                {
                    HundunWorld.Game.Selection.SelectableKind.Monster => "怪物",
                    HundunWorld.Game.Selection.SelectableKind.Player => "玩家",
                    _ => "NPC"
                };
                _targetQualityTag.Text = kind;
            }

            if (_targetDistanceLabel != null)
            {
                var local = HundunWorldGame.Instance?.LocalPlayerActor;
                _targetDistanceLabel.Text = local != null
                    ? $"{Mathf.RoundToInt(Vector3.Distance(local.Position, selected.Actor.Position))}m"
                    : string.Empty;
            }

            float maxHp = attrs != null && attrs.MaxHealth > 0f ? attrs.MaxHealth : 0f;
            float curHp = attrs != null ? attrs.CurrentHealth : 0f;
            if (_targetHpBar != null)
                _targetHpBar.Value = maxHp > 0f ? Mathf.Clamp(curHp / maxHp, 0f, 1f) : 0f;
            if (_targetHpValueLabel != null)
                _targetHpValueLabel.Text = maxHp > 0f ? $"{(int)curHp}/{(int)maxHp}" : string.Empty;
        }

        /// <summary>添加一条目标 meta 标签（图标 + 文案），返回下一个标签的 X 坐标。</summary>
        private float AddTargetMetaTag(float x, float y, float width, string icon, string text,
            Color textColor, Color fill, Color border)
        {
            var chip = new HudRoundCell
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(width, TargetMetaHeight),
                Radius = InkWashTheme.RadiusSm,
                FillColor = fill,
                BorderColor = border,
                BorderThickness = 1f,
            };
            chip.AddChild(MakeLabel(icon, 5f, 1f, 12f, TargetMetaHeight - 2f,
                textColor, 10f, InkWashTheme.FontRole.Display, TextAlignment.Center));
            chip.AddChild(MakeLabel(text, 18f, 0f, width - 22f, TargetMetaHeight,
                textColor, 10f, InkWashTheme.FontRole.Body));
            _targetPanel.AddChild(chip);
            _targetMetaChips.Add(chip);
            return x + width + 8f;
        }

        // ===================================================================
        // 区域构造：右上队伍状态
        // =======================================================================

        /// <summary>
        /// 右上队伍状态：队伍 (4/4) 标题 + 同步徽章 + 3 名成员卡
        /// （32 圆形头像 + 名称/等级 + 6px 血条 + 内力标签与 3px 气条，低血成员朱红脉冲）。
        /// 对齐设计 .hud-party。
        /// </summary>
        private void BuildPartyPanel()
        {
            _partyPanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(PartyWidth, PartyHeight),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };

            // 头部：侠 + 队伍 (4/4) + 同步
            _partyPanel.AddChild(MakeLabel("侠", 0f, 0f, 14f, PartyHeaderHeight,
                InkWashTheme.GoldBright, 12f, InkWashTheme.FontRole.Display, TextAlignment.Center));
            _partyPanel.AddChild(MakeLabel(PartyTitleText, 16f, 0f, 132f, PartyHeaderHeight,
                InkWashTheme.GoldBright, 13f, InkWashTheme.FontRole.Display));

            var syncTag = new InkTag
            {
                Text = PartySyncText,
                TagVariant = InkTagVariant.Brand,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PartyWidth - 52f, 1f),
                Size = new Float2(52f, 18f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
            };
            _partyPanel.AddChild(syncTag);

            // 客户端没有队伍名册服务（服务端仅部分 CreateTeam），成员是设计稿 mock；
            // 显示假队友会让人以为已经组队，这里改为诚实的空态。
            var empty = MakeLabel("未组队", 0f, PartyHeaderHeight + PartyRowGap, PartyWidth, 24f,
                InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Center);
            empty.VerticalAlignment = TextAlignment.Center;
            _partyPanel.AddChild(empty);

            _hudLayers.AddToLayer(_partyPanel, InkHudLayer.Persistent);
        }

        // ===================================================================
        // 区域构造：右上状态效果（队伍下方）
        // =======================================================================

        /// <summary>
        /// 状态效果列表：状态效果标题 + 3 条 buff（24 字形 + 名称 + 倒计时，
        /// 增益金/青、减益朱红）。对齐设计 .hud-buffs。
        /// </summary>
        private void BuildBuffPanel()
        {
            _buffPanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(BuffPanelWidth, BuffPanelHeight),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };

            _buffPanel.AddChild(MakeLabel("效", 0f, 0f, 14f, BuffHeaderHeight,
                InkWashTheme.GoldBright, 12f, InkWashTheme.FontRole.Display, TextAlignment.Center));
            _buffPanel.AddChild(MakeLabel(BuffTitleText, 16f, 0f, 160f, BuffHeaderHeight,
                InkWashTheme.GoldBright, 13f, InkWashTheme.FontRole.Display));

            float rowY = BuffHeaderHeight + BuffRowGap;
            AddBuffRow(rowY, "攻", BuffAttackText, BuffAttackTime,
                InkWashTheme.GoldBright, InkWashTheme.GoldDeep, InkWashTheme.PaperBright, InkWashTheme.PaperFaded);
            rowY += BuffRowHeight + BuffRowGap;
            AddBuffRow(rowY, "防", BuffDefenseText, BuffDefenseTime,
                InkWashTheme.JadeBright, InkWashTheme.JadePrimary, InkWashTheme.PaperBright, InkWashTheme.PaperFaded);
            rowY += BuffRowHeight + BuffRowGap;
            AddBuffRow(rowY, "毒", BuffPoisonText, BuffPoisonTime,
                InkWashTheme.VermilionBright, InkWashTheme.VermilionDeep,
                InkWashTheme.VermilionBright, InkWashTheme.VermilionBright);

            _hudLayers.AddToLayer(_buffPanel, InkHudLayer.Persistent);

            // 没有 Buff 数据源（服务端只下发控制状态位），这 3 条"攻击力+10%/防御力+15%/中毒"
            // 是设计稿 mock；真实控制状态已在左下玩家面板显示。隐藏以免把假增益当成真。
            _buffPanel.Visible = false;
        }

        /// <summary>构建单条状态效果（24 字形块 + 名称 + 倒计时）。</summary>
        private void AddBuffRow(float y, string glyph, string name, string time,
            Color glyphColor, Color glyphBorder, Color nameColor, Color timeColor)
        {
            var row = new HudRoundCell
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, y),
                Size = new Float2(BuffPanelWidth, BuffRowHeight),
                Radius = InkWashTheme.RadiusMd,
                FillColor = InkFill(InkWashTheme.BaseSecondary, 0.85f),
                BorderColor = InkWashTheme.BorderNeutralL1,
                BorderThickness = 1f,
            };

            var glyphCell = new HudRoundCell
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 5f),
                Size = new Float2(BuffGlyphSize, BuffGlyphSize),
                Radius = InkWashTheme.RadiusSm,
                FillColor = InkFill(glyphBorder, 0.15f),
                BorderColor = glyphBorder,
                BorderThickness = 1f,
            };
            glyphCell.AddChild(MakeLabel(glyph, 0f, 0f, BuffGlyphSize, BuffGlyphSize,
                glyphColor, 13f, InkWashTheme.FontRole.Display, TextAlignment.Center));
            row.AddChild(glyphCell);

            row.AddChild(MakeLabel(name, 40f, 4f, BuffPanelWidth - 48f, 14f,
                nameColor, 11f, InkWashTheme.FontRole.Body));
            row.AddChild(MakeLabel(time, 40f, 18f, 60f, 12f,
                timeColor, 10f, InkWashTheme.FontRole.Number));

            _buffPanel.AddChild(row);
        }

        // ===================================================================
        // 区域构造：左中连击计数器
        // =======================================================================

        /// <summary>
        /// 连击计数器：连击标签 + 大号数字 23 + COMBO 副标题 + 计时条 + 倍率 ×1.8。
        /// 对齐设计 .hud-combo。
        /// </summary>
        private void BuildComboCounter()
        {
            _comboPanel = new HudFrame
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(ComboWidth, ComboHeight),
                Radius = InkWashTheme.RadiusLg,
                FillColor = InkFill(InkWashTheme.BaseSecondary, 0.72f),
                BorderColor = InkWashTheme.BorderGold,
                BorderThickness = 1f,
                GlowColor = InkWashTheme.ShadowGold,
            };

            _comboPanel.AddChild(MakeLabel("焰", 8f, 8f, 14f, 20f,
                InkWashTheme.VermilionBright, 13f, InkWashTheme.FontRole.Display, TextAlignment.Center));
            _comboPanel.AddChild(MakeLabel(ComboLabelText, 24f, 8f, 68f, 20f,
                InkWashTheme.GoldBright, 14f, InkWashTheme.FontRole.Display));

            _comboNumberLabel = MakeLabel(ComboNumberText, 0f, 28f, ComboWidth, 52f,
                InkWashTheme.GoldBright, 48f, InkWashTheme.FontRole.Display, TextAlignment.Center);
            _comboPanel.AddChild(_comboNumberLabel);

            _comboPanel.AddChild(MakeLabel(ComboSubText, 0f, 80f, ComboWidth, 16f,
                InkWashTheme.PaperFaded, 12f, InkWashTheme.FontRole.Body, TextAlignment.Center));

            _comboTimerBar = MakeBar((ComboWidth - ComboTimerWidth) * 0.5f, 97f, ComboTimerWidth, ComboTimerHeight,
                ComboTimerRatio, InkBarFillVariant.Gold, InkFill(InkWashTheme.Abyss, 0.85f), InkWashTheme.BorderNeutralL1);
            _comboPanel.AddChild(_comboTimerBar);

            _comboPanel.AddChild(MakeLabel(ComboMultiplierText, 0f, 102f, ComboWidth, 14f,
                InkWashTheme.VermilionBright, 10f, InkWashTheme.FontRole.Display, TextAlignment.Center));

            _hudLayers.AddToLayer(_comboPanel, InkHudLayer.Transient);
            // 没有连击数据源：先隐藏，别让设计稿的「23 连击」常驻（2026-10-29）
            _comboPanel.Visible = false;
        }

        // ===================================================================
        // 区域构造：左下玩家状态
        // =======================================================================

        /// <summary>
        /// 左下玩家状态：48 圆形金边头像 + 江湖过客 Lv.42 剑客 +
        /// 12px 血条（内嵌数值）/ 8px 气条（内嵌数值）/ 经验细条 + 3 个 buff 小图标。
        /// 对齐设计 .hud-player。
        /// </summary>
        private void BuildPlayerPanel()
        {
            _playerPanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(PlayerPanelWidth, PlayerPanelHeight),
                // HUD 直接叠在 3D 世界上：雪地/沙漠这类亮场景里纯透明会让名称与血条糊成一片，
                // 给一层淡墨底（0.45，比菜单面板淡，避免挡住场景）。
                BackgroundColor = InkFill(InkWashTheme.BaseDefault, 0.45f),
                ClipChildren = false,
            };

            // 玩家头像（金渐变 + 金亮描边 + 2px 光晕，居中"客"字）
            _playerAvatar = MakeCircle(0f, 0f, PlayerAvatarSize,
                InkWashTheme.GoldPrimary, InkWashTheme.GoldBright, 2f,
                null, Color.Transparent, 0f, InkWashTheme.FontRole.Display,
                glow: InkWashTheme.ShadowGold, overlay: InkWashTheme.GoldDeep, overlayRatio: 0.35f);
            _playerAvatarGlyph = MakeLabel(PlayerAvatarGlyph, 0f, 0f, PlayerAvatarSize, PlayerAvatarSize,
                InkWashTheme.TextOnBrand, 22f, InkWashTheme.FontRole.Display, TextAlignment.Center);
            _playerAvatar.AddChild(_playerAvatarGlyph);
            _playerAvatar.TooltipText = PlayerNameText;
            _playerAvatar.Clicked += () => RequestNavigation(InkPageDomIds.NavCharacterPanel);
            _playerPanel.AddChild(_playerAvatar);

            float bodyX = PlayerAvatarSize + PlayerAvatarGap;

            // 名称 / 等级 / 门派
            _playerNameLabel = MakeLabel(PlayerNameText, bodyX, 0f, 120f, PlayerNameRowHeight,
                InkWashTheme.GoldBright, 14f, InkWashTheme.FontRole.Display);
            _playerPanel.AddChild(_playerNameLabel);

            _playerLevelLabel = MakeLabel(PlayerLevelText, bodyX + 124f, 0f, 46f, PlayerNameRowHeight,
                InkWashTheme.PaperFaded, 12f, InkWashTheme.FontRole.Number);
            _playerPanel.AddChild(_playerLevelLabel);

            var classTag = new InkTag
            {
                Text = PlayerClassText,
                TagVariant = InkTagVariant.Brand,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(bodyX + 176f, 1f),
                Size = new Float2(48f, 18f),
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
            };
            _playerPanel.AddChild(classTag);

            // 血条 12px（数值居中覆盖）
            float hpY = PlayerNameRowHeight + 4f;
            _hpBar = MakeBar(bodyX, hpY, PlayerBarWidth, PlayerHpBarHeight, PlayerHpRatio,
                InkBarFillVariant.Vermilion, InkFill(InkWashTheme.Abyss, 0.85f), InkWashTheme.BorderVermilion);
            _playerPanel.AddChild(_hpBar);

            _hpValueLabel = MakeLabel(PlayerHpText, bodyX, hpY, PlayerBarWidth, PlayerHpBarHeight,
                InkWashTheme.PaperBright, 10f, InkWashTheme.FontRole.Number, TextAlignment.Center);
            _playerPanel.AddChild(_hpValueLabel);

            // 气条 8px（数值居中覆盖）
            float mpY = hpY + PlayerHpBarHeight + 4f;
            _mpBar = MakeBar(bodyX, mpY, PlayerBarWidth, PlayerMpBarHeight, PlayerMpRatio,
                InkBarFillVariant.Gold, InkFill(InkWashTheme.Abyss, 0.85f), InkWashTheme.BorderGold);
            _playerPanel.AddChild(_mpBar);

            _mpValueLabel = MakeLabel(PlayerMpText, bodyX, mpY, PlayerBarWidth, PlayerMpBarHeight,
                InkWashTheme.PaperBright, 10f, InkWashTheme.FontRole.Number, TextAlignment.Center);
            _playerPanel.AddChild(_mpValueLabel);

            // 经验行：经验 标签 + 2px 细条
            float xpY = mpY + PlayerMpBarHeight + 4f;
            _playerPanel.AddChild(MakeLabel(PlayerXpLabelText, bodyX, xpY, 28f, PlayerXpRowHeight,
                InkWashTheme.PaperDark, 9f, InkWashTheme.FontRole.Body));
            _xpBar = MakeBar(bodyX + 32f, xpY + 6f, PlayerBarWidth, PlayerXpBarHeight, PlayerXpRatio,
                InkBarFillVariant.Jade, InkFill(InkWashTheme.Abyss, 0.85f), Color.Transparent);
            _playerPanel.AddChild(_xpBar);

            // 控制状态行：眩晕/沉默/定身…由 ControlStateManager 实时驱动；无状态时留空。
            // （原 3 个"攻/防/轻"图标是设计稿 mock，没有数据源。）
            float buffY = PlayerPanelHeight - PlayerBuffChipSize - PlayerBuffTimeHeight;
            _controlChips = new ControlStateChipView[ControlChipSlots];
            for (int i = 0; i < _controlChips.Length; i++)
            {
                var chip = new ControlStateChipView
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(bodyX + i * (PlayerBuffChipSize + PlayerBuffChipGap), buffY),
                    Size = new Float2(PlayerBuffChipSize, PlayerBuffChipSize + PlayerBuffTimeHeight),
                    Visible = false,
                };
                _playerPanel.AddChild(chip);
                _controlChips[i] = chip;
            }

            _hudLayers.AddToLayer(_playerPanel, InkHudLayer.Persistent);
        }

        /// <summary>构建玩家状态栏下方的 buff 小图标（24 方块 + 下方倒计时）。</summary>
        private void AddPlayerBuffChip(float x, float y, string glyph, string time, string tooltip)
        {
            var chip = new HudRoundCell
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(PlayerBuffChipSize, PlayerBuffChipSize),
                Radius = InkWashTheme.RadiusSm,
                FillColor = InkFill(InkWashTheme.Abyss, 0.80f),
                BorderColor = InkWashTheme.JadePrimary,
                BorderThickness = 1f,
                TooltipText = tooltip,
            };
            chip.AddChild(MakeLabel(glyph, 0f, 0f, PlayerBuffChipSize, PlayerBuffChipSize,
                InkWashTheme.JadeBright, 12f, InkWashTheme.FontRole.Display, TextAlignment.Center));
            _playerPanel.AddChild(chip);

            _playerPanel.AddChild(MakeLabel(time, x, y + PlayerBuffChipSize, PlayerBuffChipSize, PlayerBuffTimeHeight,
                InkWashTheme.PaperFaded, 8f, InkWashTheme.FontRole.Number, TextAlignment.Center));
        }

        // ===================================================================
        // 区域构造：底部中央技能栏
        // =======================================================================

        /// <summary>
        /// 底部中央技能栏：8 个 48x48 技能槽（剑1 遁2 防3 攻4 轻Q 绝E 药R 食F），
        /// 冷却槽绘制扇形遮罩与剩余秒数，绝学槽金边脉冲，下方为键位提示行。
        /// 对齐设计 .hud-skillbar。
        /// </summary>
        private void BuildSkillBar()
        {
            // 技能行（设计 .skillbar-row：半透明墨底 + 金线描边 + 6px 内边距）
            _skillBarRow = new HudFrame
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(SkillRowWidth, SkillRowHeight),
                Radius = InkWashTheme.RadiusLg,
                FillColor = InkFill(InkWashTheme.BaseSecondary, 0.78f),
                BorderColor = InkWashTheme.BorderGold,
                BorderThickness = 1f,
                GlowColor = InkWashTheme.ShadowSoft,
            };

            _skillSlots = new HudSkillSlot[SkillSlotCount];
            for (int i = 0; i < SkillSlotCount; i++)
            {
                var (glyph, key, cooldown, maxCooldown, ultimate, tooltip) = SkillConfig[i];
                var slot = new HudSkillSlot
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(SkillRowPadding + i * (SkillSlotSize + SkillSlotGap), SkillRowPadding),
                    Size = new Float2(SkillSlotSize, SkillSlotSize),
                    Radius = InkWashTheme.RadiusMd,
                    Glyph = glyph,
                    Hotkey = key,
                    IsUltimate = ultimate,
                    CooldownSeconds = cooldown,
                    MaxCooldownSeconds = maxCooldown,
                    TooltipText = tooltip,
                };
                slot.Clicked += slot.Activate;
                _skillSlots[i] = slot;
                _skillBarRow.AddChild(slot);
            }

            // 将技能行挂到页面（此前遗漏导致技能栏不渲染）。
            _hudLayers.AddToLayer(_skillBarRow, InkHudLayer.Persistent);

            // 键位提示行（设计 .skillbar-hint）。
            _skillHintRow = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(SkillRowWidth, SkillHintHeight),
                BackgroundColor = Color.Transparent,
            };
            _skillHintRow.AddChild(MakeLabel(SkillHintText, 0f, 0f, SkillRowWidth, SkillHintHeight,
                InkWashTheme.TextTertiary, 11f, InkWashTheme.FontRole.Body, TextAlignment.Center));
            _hudLayers.AddToLayer(_skillHintRow, InkHudLayer.Persistent);

            // 真实技能栏（MainUIManager 的 10×6 滚轮技能栏）常驻挂在 shell 上，正好覆盖这一区域：
            // 这一排 8 格是设计稿 mock，保留可见会从真实技能栏上方露出约 22px 的假槽位与假键位提示。
            // 只隐藏、不删除布局，避免底部系统导航条的位置跟着跳动。
            _skillBarRow.Visible = false;
            _skillHintRow.Visible = false;
        }

        // ===================================================================
        // 区域构造：右下道具栏
        // =======================================================================

        /// <summary>
        /// 右下道具栏：道具标题 + 竖直 4 格 36x36（血 ×5 / 气 ×3 / 解 ×2 / 烟 ×1）。
        /// 点击消耗一格（数量递减，归零后淡出）。对齐设计 .hud-items。
        /// </summary>
        private void BuildItemColumn()
        {
            _itemSlots = new HudItemSlot[ItemSlotCount];
            _itemCountLabels = new Label[ItemSlotCount];

            // 道具标题行（设计 .items-title）。
            _itemTitleRow = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(ItemColumnWidth, ItemHeaderHeight),
                BackgroundColor = Color.Transparent,
            };
            _itemTitleRow.AddChild(MakeLabel(ItemTitleText, 0f, 0f, ItemColumnWidth, ItemHeaderHeight,
                InkWashTheme.GoldBright, 12f, InkWashTheme.FontRole.Heading, TextAlignment.Center));
            _hudLayers.AddToLayer(_itemTitleRow, InkHudLayer.Persistent);

            // 道具竖列容器（半透墨底 + 金线描边，与技能栏一致）。此前 _itemColumn 未初始化导致 NPE。
            _itemColumn = new HudFrame
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(ItemColumnWidth, ItemColumnHeight),
                Radius = InkWashTheme.RadiusLg,
                FillColor = InkFill(InkWashTheme.BaseSecondary, 0.78f),
                BorderColor = InkWashTheme.BorderGold,
                BorderThickness = 1f,
                GlowColor = InkWashTheme.ShadowSoft,
            };
            _hudLayers.AddToLayer(_itemColumn, InkHudLayer.Persistent);

            for (int i = 0; i < ItemSlotCount; i++)
            {
                var (glyph, count, tone, border, name) = ItemConfig[i];
                float y = ItemColumnPadding + i * (ItemSlotSize + ItemSlotGap);

                var slot = new HudItemSlot
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(ItemColumnPadding, y),
                    Size = new Float2(ItemSlotSize, ItemSlotSize),
                    Radius = InkWashTheme.RadiusSm,
                    FillColor = InkFill(InkWashTheme.BaseTertiary, 0.92f),
                    BorderColor = border,
                    BorderThickness = 1f,
                    HoverOverlayColor = InkWashTheme.BgHover,
                    Glyph = glyph,
                    Count = count,
                    Tone = tone,
                    TooltipText = $"{name} ×{count}",
                };

                var glyphLabel = MakeLabel(glyph, 0f, 0f, ItemSlotSize, ItemSlotSize,
                    tone, 15f, InkWashTheme.FontRole.Display, TextAlignment.Center);
                slot.AddChild(glyphLabel);

                var countLabel = MakeLabel("×" + count, ItemSlotSize - 22f, ItemSlotSize - 14f, 20f, 12f,
                    InkWashTheme.PaperBright, 9f, InkWashTheme.FontRole.Number, TextAlignment.Far);
                slot.AddChild(countLabel);

                _itemSlots[i] = slot;
                _itemCountLabels[i] = countLabel;

                HudItemSlot captured = slot;
                Label capturedCount = countLabel;
                Label capturedGlyph = glyphLabel;
                captured.Clicked += () =>
                {
                    captured.Consume();
                    capturedCount.Text = "×" + captured.Count;
                    if (captured.Count <= 0)
                    {
                        capturedGlyph.TextColor = InkWashTheme.TextTertiary;
                        capturedCount.TextColor = InkWashTheme.TextTertiary;
                    }
                };

                _itemColumn.AddChild(slot);
            }

            // 这一列是可点击的"假消耗品"（点击会把 mock 数量减一），而真实消耗品在
            // MainUIManager 的滚轮技能栏/背包里；留着会让玩家以为点了真道具。隐藏而不是删除布局。
            _itemTitleRow.Visible = false;
            _itemColumn.Visible = false;
        }

        // ===================================================================
        // 区域构造：底部系统导航栏
        // =======================================================================

        /// <summary>
        /// 底部系统导航栏：共享壳契约要求的“战斗 HUD 底部系统入口”。
        /// 将各 UI 子系统（角色/背包/武学/任务/商店/邮件/异闻/设置）从 HUD 直接可达，
        /// 点击进入后各菜单页侧栏再串联其余页面。位置：底部中央、技能栏上方。
        /// </summary>
        private void BuildSystemNav()
        {
            var entries = new (string Label, string DomId)[]
            {
                ("角色", InkPageDomIds.NavCharacterV2),
                ("背包", InkPageDomIds.NavInventory),
                ("武学", InkPageDomIds.NavSkillTree),
                ("任务", InkPageDomIds.NavQuests),
                ("商店", InkPageDomIds.NavShop),
                ("邮件", InkPageDomIds.NavMail),
                ("异闻", InkPageDomIds.PopupBestiarySide),
                ("设置", InkPageDomIds.NavSettings),
            };

            const float pad = 8f;
            const float gap = 6f;
            const float btnW = 68f;
            const float btnH = 36f;
            float navW = pad * 2f + entries.Length * btnW + (entries.Length - 1) * gap;
            float navH = pad * 2f + btnH;

            _sysNav = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(navW, navH),
            };

            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                var btn = new InkButton
                {
                    Variant = InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Sm,
                    Text = entry.Label,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(pad + i * (btnW + gap), pad),
                    Size = new Float2(btnW, btnH),
                };
                string domId = entry.DomId;
                btn.ButtonClicked += (b) => RequestNavigation(domId);
                _sysNav.AddChild(btn);
            }

            _hudLayers.AddToLayer(_sysNav, InkHudLayer.Persistent);
        }

        // ===================================================================
        // 区域构造：隐藏退出按钮（链路终点）
        // =======================================================================

        /// <summary>
        /// 隐藏退出按钮：设计稿 <c>.hud-exit-hidden</c>（1x1、不可见、无可见出口），
        /// 作为"返回战斗 HUD"链路终点存在；点击后向路由器请求 <c>back-hud</c>。
        /// </summary>
        private void BuildHiddenExit()
        {
            _exitHotspot = new HudExitHotspot
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(1f, 1f),
                BackgroundColor = Color.Transparent,
                TooltipText = "退出战斗界面",
            };
            _exitHotspot.Clicked += () => RequestNavigation(InkPageDomIds.BackHud);
            _hudLayers.AddToLayer(_exitHotspot, InkHudLayer.Context);
        }

        // ===================================================================
        // 布局计算
        // =======================================================================

        /// <summary>根据当前 <see cref="_screenSize"/> 重新计算所有子控件的位置（对齐设计稿四角定位）。</summary>
        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;



            // 左上：小地图
            if (_minimapRoot != null)
                _minimapRoot.Location = new Float2(Edge, Edge);

            // 顶部中央：目标信息
            if (_targetPanel != null)
                _targetPanel.Location = new Float2((sw - TargetWidth) * 0.5f, Edge);

            // 右上：队伍状态
            if (_partyPanel != null)
                _partyPanel.Location = new Float2(sw - PartyWidth - Edge, Edge);

            // 右上：状态效果（设计稿固定 256px；队伍更高时自动下移避免重叠）
            if (_buffPanel != null)
            {
                float buffTop = Mathf.Max(BuffPanelTop, Edge + PartyHeight + 8f);
                _buffPanel.Location = new Float2(sw - BuffPanelWidth - Edge, buffTop);
            }

            // 左中：连击计数器（垂直居中）
            if (_comboPanel != null)
                _comboPanel.Location = new Float2(ComboLeft, (sh - ComboHeight) * 0.5f);

            // 左下：玩家状态
            if (_playerPanel != null)
                _playerPanel.Location = new Float2(Edge, sh - PlayerPanelHeight - Edge);

            // 底部中央：技能栏（技能行 + 键位提示行）
            if (_skillBarRow != null)
                _skillBarRow.Location = new Float2((sw - SkillRowWidth) * 0.5f, sh - Edge - SkillBarHeight);
            if (_skillHintRow != null)
                _skillHintRow.Location = new Float2((sw - SkillRowWidth) * 0.5f,
                    sh - Edge - SkillBarHeight + SkillRowHeight + SkillHintGap);

            // 右下：道具栏（标题 + 竖列）
            if (_itemTitleRow != null)
                _itemTitleRow.Location = new Float2(sw - Edge - ItemColumnWidth, sh - Edge - ItemPanelHeight);
            if (_itemColumn != null)
                _itemColumn.Location = new Float2(sw - Edge - ItemColumnWidth,
                    sh - Edge - ItemPanelHeight + ItemHeaderHeight + ItemHeaderGap);

            // 底部中央：系统导航栏（位于技能栏上方，避免与技能/道具/玩家区重叠）
            if (_sysNav != null)
                _sysNav.Location = new Float2((sw - _sysNav.Width) * 0.5f,
                    sh - Edge - SkillBarHeight - _sysNav.Height - 12f);
        }

        /// <summary>
        /// 在屏幕尺寸变化时重新布局所有子控件。
        /// 优先使用控件实际尺寸（已由 <see cref="InkPageShell.LoadPage"/> 的 StretchAll 锚点填充父容器）。
        /// </summary>
        public void RefreshLayout()
        {
            float w = Width;
            float h = Height;
            if (w <= 0f || h <= 0f)
            {
                var screen = FlaxEngine.Screen.Size;
                w = screen.X;
                h = screen.Y;
            }
            if (w <= 0f || h <= 0f)
            {
                w = 1920f;
                h = 1080f;
            }
            _screenSize = new Float2(w, h);
            ApplyLayout();
        }

        // ===================================================================
        // 数据绑定 API
        // =======================================================================

        /// <summary>绑定角色属性组件。绑定后血/气/经验条每帧读取真实数据，传入 null 解除绑定回退设计稿 mock。</summary>
        public void BindCharacter(CharacterAttributesComponent component)
        {
            _boundCharacter = component;
            RefreshPlayerIdentity();
        }

        /// <summary>
        /// 小地图玩家朝向角（度）。0 = 正北，顺时针增加。
        /// 由 <c>MainUIManager</c> 在 V2 作为默认战斗 HUD 时动态推送。
        /// </summary>
        public float MinimapPlayerYaw
        {
            get => _minimap?.PlayerYaw ?? 0f;
            set
            {
                if (_minimap != null)
                    _minimap.PlayerYaw = value;
                if (_visionCone != null)
                    _visionCone.Yaw = value;
            }
        }

        /// <summary>当前连击数（外部可推送，默认设计稿值 23）。</summary>
        public int ComboCount
        {
            get => _comboCount;
            set
            {
                _comboCount = Mathf.Max(0, value);
                if (_comboNumberLabel != null)
                    _comboNumberLabel.Text = _comboCount.ToString();
            }
        }

        private int _comboCount = 23;

        // ===================================================================
        // 生命周期
        // =======================================================================

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            RefreshBoundData();

            _controlChipRefreshTimer += deltaTime;
            if (_controlChipRefreshTimer >= 0.15f)
            {
                _controlChipRefreshTimer = 0f;
                RefreshControlChips();
            }

            _minimapCoordTimer += deltaTime;
            if (_minimapCoordTimer >= 0.5f)
            {
                _minimapCoordTimer = 0f;
                RefreshMinimapCoord();
            }
        }

        private float _minimapCoordTimer;

        /// <summary>
        /// 小地图下方那行坐标：原先是设计稿地名「昆仑墟 · 深渊」，看起来像当前区域。
        /// 现在写玩家真实世界坐标（平面轴 X/Z，与地图页口径一致），拿不到位姿就显示「—」。
        /// </summary>
        private void RefreshMinimapCoord()
        {
            if (_minimapCoordLabel == null) return;
            _minimapCoordLabel.Text = PluginNavigationBridge.TryGetPlayerPose(out var pos, out _)
                ? $"X:{pos.X:0} Z:{pos.Z:0}"
                : "—";
        }

        /// <summary>把本地角色当前的控制状态刷到左下玩家面板（与 <see cref="ControlStateManager"/> 同一数据源）。</summary>
        private void RefreshControlChips()
        {
            if (_controlChips == null) return;

            ulong id = HundunWorldGame.Instance?.PlayerId ?? 0UL;
            var states = id == 0UL ? null : ControlStateManager.Instance.GetActiveControlStates(id);
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

        /// <summary>每帧从绑定数据源刷新玩家身份与血/气/经验。未绑定时保持设计稿 mock。</summary>
        private void RefreshBoundData()
        {
            if (_boundCharacter == null)
                return;

            RefreshPlayerIdentity();

            float hpRatio = _boundCharacter.MaxHealth > 0f
                ? Mathf.Clamp(_boundCharacter.CurrentHealth / _boundCharacter.MaxHealth, 0f, 1f) : 0f;
            float mpRatio = _boundCharacter.MaxEnergy > 0f
                ? Mathf.Clamp(_boundCharacter.CurrentEnergy / _boundCharacter.MaxEnergy, 0f, 1f) : 0f;

            if (_hpBar != null)
                _hpBar.Value = hpRatio;
            if (_hpValueLabel != null)
                _hpValueLabel.Text = $"{_boundCharacter.CurrentHealth:N0} / {_boundCharacter.MaxHealth:N0}";
            if (_mpBar != null)
                _mpBar.Value = mpRatio;
            if (_mpValueLabel != null)
                _mpValueLabel.Text = $"{_boundCharacter.CurrentEnergy:N0} / {_boundCharacter.MaxEnergy:N0}";

            // 经验条：同一次刷新里跟血/气一起更新，否则绑定后经验条会永远停在设计稿 54%
            if (_xpBar != null && _boundCharacter.MaxExp > 0)
                _xpBar.Value = Mathf.Clamp((float)_boundCharacter.Exp / _boundCharacter.MaxExp, 0f, 1f);
        }

        /// <summary>刷新玩家身份信息（名称、等级、头像字）。未绑定时保留设计稿 mock。</summary>
        private void RefreshPlayerIdentity()
        {
            if (_boundCharacter == null)
                return;

            if (_playerNameLabel != null && !string.IsNullOrEmpty(_boundCharacter.Nickname))
            {
                _playerNameLabel.Text = _boundCharacter.Nickname;
                if (_playerAvatarGlyph != null)
                    _playerAvatarGlyph.Text = _boundCharacter.Nickname[0].ToString();
            }
            if (_playerLevelLabel != null && _boundCharacter.Level > 0)
                _playerLevelLabel.Text = $"Lv.{_boundCharacter.Level}";
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        /// <summary>触发导航请求（带异常保护）。</summary>
        private void RequestNavigation(string domId)
        {
            try
            {
                NavigationRequested?.Invoke(domId);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CombatHudV2Page] NavigationRequested({domId}) 触发失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 嵌套控件：背景层（深墨渐变 + 水墨晕染 + 远景墨山）
        // =======================================================================

        /// <summary>
        /// HUD 背景层：垂直墨底渐变 + 4 处径向水墨晕染 + 两层墨山剪影 + 底部压暗。
        /// 对应设计 <c>.hud-background</c> / <c>.ink-splash</c> / <c>.ink-mountain</c>。
        /// </summary>
        // ===================================================================
        // 嵌套控件：圆角面板 / 圆角单元 / 圆形头像
        // =======================================================================

        /// <summary>圆角面板：填充 + 外辉光 + 描边，对应设计 .ink-panel / .target-frame。</summary>
        private class HudFrame : ContainerControl
        {
            /// <summary>填充色</summary>
            public Color FillColor { get; set; } = InkWashTheme.Panel;

            /// <summary>描边颜色</summary>
            public Color BorderColor { get; set; } = InkWashTheme.BorderGold;

            /// <summary>描边厚度</summary>
            public float BorderThickness { get; set; } = 1f;

            /// <summary>外辉光颜色（Transparent 表示无辉光）</summary>
            public Color GlowColor { get; set; } = Color.Transparent;

            /// <summary>圆角半径</summary>
            public float Radius { get; set; } = InkWashTheme.RadiusLg;

            public HudFrame()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                var bounds = new Rectangle(0, 0, Width, Height);

                if (GlowColor.A > 0f)
                    InkRenderHelper.DrawRoundedRectangle(new Rectangle(-2f, -2f, Width + 4f, Height + 4f),
                        Radius + 2f, GlowColor, 3f);
                if (FillColor.A > 0f)
                    InkRenderHelper.FillRoundedRectangle(bounds, Radius, FillColor);

                var savedBg = BackgroundColor;
                BackgroundColor = Color.Transparent;
                base.Draw();
                BackgroundColor = savedBg;

                if (BorderColor.A > 0f && BorderThickness > 0f)
                    InkRenderHelper.DrawRoundedRectangle(bounds, Radius, BorderColor, BorderThickness);
            }
        }

        /// <summary>
        /// 圆角单元：填充 + 悬停叠加 + 悬停描边 + 点击事件。
        /// 用于技能槽/道具格/状态标签/buff 条目（对应设计 .skill-slot / .item-slot / .target-tag / .buff-item）。
        /// </summary>
        private class HudRoundCell : ContainerControl
        {
            /// <summary>点击事件</summary>
            public event Action Clicked;

            /// <summary>填充色</summary>
            public Color FillColor { get; set; } = InkWashTheme.BaseTertiary;

            /// <summary>描边颜色</summary>
            public Color BorderColor { get; set; } = Color.Transparent;

            /// <summary>描边厚度</summary>
            public float BorderThickness { get; set; } = 1f;

            /// <summary>圆角半径</summary>
            public float Radius { get; set; } = InkWashTheme.RadiusMd;

            /// <summary>悬停叠加色</summary>
            public Color HoverOverlayColor { get; set; } = Color.Transparent;

            /// <summary>悬停描边色</summary>
            public Color HoverBorderColor { get; set; } = InkWashTheme.GoldBright;

            /// <summary>是否处于悬停状态</summary>
            protected bool Hovered;

            public HudRoundCell()
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

                Color border = Hovered && HoverBorderColor.A > 0f ? HoverBorderColor : BorderColor;
                if (border.A > 0f && BorderThickness > 0f)
                    InkRenderHelper.DrawRoundedRectangle(bounds, Radius, border, BorderThickness);
            }

            /// <inheritdoc />
            public override void OnMouseEnter(Float2 location)
            {
                Hovered = true;
                base.OnMouseEnter(location);
            }

            /// <inheritdoc />
            public override void OnMouseLeave()
            {
                Hovered = false;
                base.OnMouseLeave();
            }

            /// <inheritdoc />
            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location))
                {
                    Clicked?.Invoke();
                    return true;
                }
                return base.OnMouseUp(location, button);
            }
        }

        /// <summary>圆形头像：圆形填充 + 可选叠加高光 + 外辉光 + 圆形描边，可选点击事件。</summary>
        private class HudCircle : ContainerControl
        {
            /// <summary>点击事件</summary>
            public event Action Clicked;

            /// <summary>底色填充</summary>
            public Color FillColor { get; set; } = InkWashTheme.BaseElevated;

            /// <summary>描边颜色</summary>
            public Color BorderColor { get; set; } = Color.Transparent;

            /// <summary>描边厚度</summary>
            public float BorderThickness { get; set; } = 1f;

            /// <summary>外辉光颜色</summary>
            public Color GlowColor { get; set; } = Color.Transparent;

            /// <summary>叠加色（近似渐变高光层）</summary>
            public Color OverlayColor { get; set; } = Color.Transparent;

            /// <summary>叠加色半径比例</summary>
            public float OverlayRadiusRatio { get; set; } = 1f;

            public HudCircle()
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

                if (GlowColor.A > 0f)
                    InkRenderHelper.DrawCircle(center, r + 1.5f, GlowColor, 3f);

                InkRenderHelper.FillCircle(center, r, FillColor);
                if (OverlayColor.A > 0f && OverlayRadiusRatio > 0f)
                    InkRenderHelper.FillCircle(center, r * OverlayRadiusRatio, OverlayColor);

                var savedBg = BackgroundColor;
                BackgroundColor = Color.Transparent;
                base.Draw();
                BackgroundColor = savedBg;

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
                return base.OnMouseUp(location, button);
            }
        }

        // ===================================================================
        // 嵌套控件：队伍成员卡（低血脉冲）
        // =======================================================================

        // ===================================================================
        // 嵌套控件：技能槽
        // =======================================================================

        /// <summary>
        /// HUD 技能槽（48x48）：技能字 + 右下快捷键；冷却中绘制扇形遮罩与剩余秒数并减淡；
        /// 绝学槽金亮 2px 边框 + 呼吸辉光；点击就绪技能开始冷却（冷却中点击无效）。
        /// 对应设计 .skill-slot / .skill-ready / .skill-cooldown / .skill-ultimate / .cooldown-overlay。
        /// </summary>
        private sealed class HudSkillSlot : HudRoundCell
        {
            private const int SectorSegments = 32;

            /// <summary>技能字符</summary>
            public string Glyph { get; set; } = "";

            /// <summary>快捷键标签（空则不显示）</summary>
            public string Hotkey { get; set; } = "";

            /// <summary>是否绝学槽（金亮描边 + 呼吸辉光）</summary>
            public bool IsUltimate { get; set; }

            /// <summary>当前冷却剩余秒数</summary>
            public float CooldownSeconds { get; set; }

            /// <summary>冷却总时长（秒），为 0 表示该技能无冷却</summary>
            public float MaxCooldownSeconds { get; set; }

            /// <summary>是否处于冷却中</summary>
            public bool OnCooldown => CooldownSeconds > 0f;

            private float _pulseTime;
            private float _flashTime;

            public HudSkillSlot()
            {
                FillColor = new Color(InkWashTheme.BaseTertiary.R, InkWashTheme.BaseTertiary.G,
                    InkWashTheme.BaseTertiary.B, 0.92f);
                BorderColor = InkWashTheme.GoldDeep;
                HoverOverlayColor = InkWashTheme.BgHover;
            }

            /// <summary>释放技能：冷却中忽略；就绪则进入冷却并给出一次金色闪动反馈。</summary>
            public void Activate()
            {
                _flashTime = 0.25f;
                if (OnCooldown)
                    return;
                if (MaxCooldownSeconds > 0f)
                    CooldownSeconds = MaxCooldownSeconds;
            }

            /// <inheritdoc />
            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                if (CooldownSeconds > 0f)
                    CooldownSeconds = Mathf.Max(0f, CooldownSeconds - deltaTime);
                if (IsUltimate)
                    _pulseTime += deltaTime;
                if (_flashTime > 0f)
                    _flashTime = Mathf.Max(0f, _flashTime - deltaTime);
            }

            /// <inheritdoc />
            public override void Draw()
            {
                var bounds = new Rectangle(0, 0, Width, Height);

                // 绝学呼吸辉光
                if (IsUltimate)
                {
                    float pulse = 0.35f + 0.35f * (0.5f + 0.5f * Mathf.Sin(_pulseTime * 3.15f));
                    InkRenderHelper.DrawRoundedRectangle(new Rectangle(-2f, -2f, Width + 4f, Height + 4f),
                        Radius + 2f, new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                            InkWashTheme.GoldPrimary.B, pulse), 3f);
                }

                base.Draw();

                // 冷却中整体减淡（设计 opacity .75）
                float dim = OnCooldown ? 0.75f : 1f;
                bool highlight = IsUltimate || _flashTime > 0f;
                Color glyphColor = highlight ? InkWashTheme.GoldBright : InkWashTheme.PaperBright;

                var glyphFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 20f).GetFont();
                if (glyphFont != null && !string.IsNullOrEmpty(Glyph))
                {
                    Render2D.DrawText(glyphFont, Glyph, bounds,
                        new Color(glyphColor.R, glyphColor.G, glyphColor.B, glyphColor.A * dim),
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }

                // 冷却扇形遮罩 + 剩余秒数（设计 .cooldown-overlay / .cooldown-number）
                if (OnCooldown && MaxCooldownSeconds > 0f)
                {
                    float ratio = Mathf.Clamp(CooldownSeconds / MaxCooldownSeconds, 0f, 1f);
                    DrawCooldownSector(new Float2(Width * 0.5f, Height * 0.5f), Width * 0.5f, ratio,
                        new Color(InkWashTheme.BaseDefault.R, InkWashTheme.BaseDefault.G,
                            InkWashTheme.BaseDefault.B, 0.82f));

                    var numberFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f).GetFont();
                    if (numberFont != null)
                    {
                        Render2D.DrawText(numberFont, Mathf.CeilToInt(CooldownSeconds).ToString(), bounds,
                            InkWashTheme.PaperBright, TextAlignment.Center, TextAlignment.Center,
                            TextWrapping.NoWrap);
                    }
                }

                // 快捷键（右下角）
                if (!string.IsNullOrEmpty(Hotkey))
                {
                    var keyFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 10f).GetFont();
                    if (keyFont != null)
                    {
                        Color keyColor = OnCooldown ? InkWashTheme.PaperFaded : InkWashTheme.PaperBright;
                        Render2D.DrawText(keyFont, Hotkey,
                            new Rectangle(0f, 0f, Width - 3f, Height - 2f), keyColor,
                            TextAlignment.Far, TextAlignment.Far, TextWrapping.NoWrap);
                    }
                }
            }

            /// <summary>绘制冷却扇形遮罩（自正上方顺时针）。</summary>
            private static void DrawCooldownSector(Float2 center, float radius, float progress, Color color)
            {
                if (progress <= 0f || radius <= 0f)
                    return;
                if (progress >= 1f)
                {
                    InkRenderHelper.FillCircle(center, radius, color);
                    return;
                }

                int segments = Mathf.Max(1, Mathf.CeilToInt(progress * SectorSegments));
                float startAngle = -Mathf.Pi * 0.5f;
                float totalAngle = progress * Mathf.TwoPi;
                var vertices = new Float2[segments * 3];
                for (int i = 0; i < segments; i++)
                {
                    float a1 = startAngle + (float)i / segments * totalAngle;
                    float a2 = startAngle + (float)(i + 1) / segments * totalAngle;
                    vertices[i * 3] = center;
                    vertices[i * 3 + 1] = center + new Float2(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
                    vertices[i * 3 + 2] = center + new Float2(Mathf.Cos(a2) * radius, Mathf.Sin(a2) * radius);
                }
                Render2D.FillTriangles(vertices, color);
            }
        }

        // ===================================================================
        // 嵌套控件：道具格 / 小地图视线扇形 / 隐藏退出热区
        // =======================================================================

        /// <summary>道具格：字符 + 数量，点击消耗一个（对应设计 .item-slot 的 cursor:pointer）。</summary>
        private sealed class HudItemSlot : HudRoundCell
        {
            /// <summary>道具字（血/气/解/烟）</summary>
            public string Glyph { get; set; } = "";

            /// <summary>剩余数量</summary>
            public int Count { get; set; }

            /// <summary>色调（图标与文字）</summary>
            public Color Tone { get; set; } = InkWashTheme.PaperAged;

            /// <summary>消耗一个（数量归零后不再响应）。</summary>
            public void Consume()
            {
                if (Count <= 0)
                    return;
                Count--;
            }
        }

        /// <summary>小地图视线扇形（对应设计 .minimap-vision 的 60deg 锥形渐变，随朝向旋转）。</summary>
        private sealed class HudVisionCone : ContainerControl
        {
            /// <summary>视线半角（度）</summary>
            public float HalfAngleDeg { get; set; } = 30f;

            /// <summary>朝向角（度，0=正北，顺时针）</summary>
            public float Yaw { get; set; }

            public HudVisionCone()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (Width <= 0f || Height <= 0f)
                    return;

                // 锥顶位于底边中心（设计 transform-origin: bottom center）
                var apex = new Float2(Width * 0.5f, Height);
                float radius = Height;
                float centerDeg = -90f + Yaw;
                float half = Mathf.Clamp(HalfAngleDeg, 1f, 89f);

                var vertices = new Float2[3];
                vertices[0] = apex;
                vertices[1] = apex + PolarOffset(radius, centerDeg - half);
                vertices[2] = apex + PolarOffset(radius, centerDeg + half);

                Render2D.FillTriangles(vertices,
                    new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                        InkWashTheme.GoldPrimary.B, 0.18f));

                base.Draw();
            }

            /// <summary>按屏幕坐标系（0 度=正上，顺时针）计算半径端点。</summary>
            private static Float2 PolarOffset(float radius, float degrees)
            {
                float rad = Mathf.DegreesToRadians * degrees;
                return new Float2(Mathf.Cos(rad) * radius, Mathf.Sin(rad) * radius);
            }
        }

        /// <summary>隐藏退出热区（1x1，不可见）：设计 .hud-exit-hidden 的链路终点。</summary>
        private sealed class HudExitHotspot : ContainerControl
        {
            /// <summary>点击事件</summary>
            public event Action Clicked;

            public HudExitHotspot()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
            }

            /// <inheritdoc />
            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && ContainsPoint(ref location))
                {
                    Clicked?.Invoke();
                    return true;
                }
                return base.OnMouseUp(location, button);
            }
        }
    }
}
