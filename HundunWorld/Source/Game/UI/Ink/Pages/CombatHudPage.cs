using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;
using Game.Character.Attributes;
using Game.Combat.Skills;

namespace HundunWorld.Game.UI.Ink.Pages
{
    /// <summary>
    /// 战斗 HUD 页面。
    /// 作为游戏内 UI 枢纽，承载 8 个子区域：
    /// <list type="bullet">
    ///   <item>SubTask 5.1 水墨晕染装饰（3 个 <see cref="InkSplash"/>）</item>
    ///   <item>SubTask 5.2 屏幕中央引导按钮（<see cref="InkButton"/> Ghost Lg）</item>
    ///   <item>SubTask 5.3 顶部中央任务提示条（<see cref="InkPanel"/> + <see cref="Label"/>）</item>
    ///   <item>SubTask 5.4 右上角水墨小地图（<see cref="InkMinimap"/>），带地形快照/NPC/玩家图标</item>
    ///   <item>SubTask 5.5 左上角头像 + 竖排角色名 + 气血/内力/体魄三行条</item>
    ///   <item>SubTask 5.6 右下角技能槽 + 奇术槽</item>
    ///   <item>SubTask 5.7 底部中央 buff/debuff 图标条</item>
    ///   <item>SubTask 5.8 底部系统导航栏</item>
    /// </list>
    /// 战斗 HUD 本身不添加 <see cref="InkBackgroundLayer"/>/<see cref="InkVignette"/>
    /// （由 <see cref="InkPageShell"/> 全局承载），仅添加 splash 装饰。
    /// 通过 <see cref="NavigationRequested"/> 事件向 <see cref="InkPageRouter"/> 暴露导航请求。
    /// </summary>
    public class CombatHudPage : ContainerControl, IInkPage, IInkNavigationSource
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>左上角引导按钮宽度（对齐设计 .hud-nav-btn ≈88px）</summary>
        private const float GuideWidth = 88f;

        /// <summary>引导按钮高度（对齐设计 .hud-nav-btn）</summary>
        private const float GuideHeight = 34f;

        /// <summary>顶部任务条距离屏幕顶部的边距</summary>
        private const float TopMargin = 20f;

        /// <summary>顶部导航条尺寸</summary>
        private static readonly Float2 NavStripSize = new Float2(440f, 30f);

        /// <summary>右上角圆形罗盘尺寸（对齐设计 .hud-compass 148px）</summary>
        private const float CompassSize = 148f;

        /// <summary>左上角角色信息面板尺寸（对齐 boss 目标面板紧凑风格）</summary>
        private static readonly Float2 LeftBottomSize = new Float2(360f, 130f);

        /// <summary>右下角容器尺寸（5 技能槽 60 + 间隔 8 + 分隔 + 奇术槽 72，对齐设计）</summary>
        private static readonly Float2 RightBottomSize = new Float2(440f, 76f);

        /// <summary>buff/debuff 图标条尺寸（对齐 design 横向图标条，位于左上角角色面板下方）</summary>
        private static readonly Float2 BuffBarSize = new Float2(360f, 42f);

        /// <summary>系统导航栏尺寸（对齐设计 .hud-sysnav：底部中央一行 6 项）</summary>
        private static readonly Float2 SysNavSize = new Float2(300f, 58f);

        /// <summary>系统扩展导航栏尺寸（已停用融合扩展行，保留字段避免破坏历史引用）</summary>
        private static readonly Float2 SysNavExtendedSize = new Float2(760f, 36f);

        /// <summary>主导航栏与扩展导航栏的垂直间距</summary>
        private const float SysNavRowGap = 4f;

        /// <summary>传统模式切换按钮的金色强调背景（rgba(200,168,88,0.12)）</summary>
        private static readonly Color TraditionalToggleBg = new Color(
            InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
            InkWashTheme.GoldPrimary.B, 0.12f);

        /// <summary>主导航栏分隔符宽度（竖线 + 两侧 margin）</summary>
        private const float SysNavDividerWidth = 8f;

        /// <summary>技能槽尺寸（对齐设计 .hud-skill 60px 圆形）</summary>
        private const float SkillSlotSize = 60f;

        /// <summary>技能槽间距（对齐设计 gap-2 = 8px）</summary>
        private const float SkillSlotGap = 8f;

        /// <summary>奇术槽尺寸（对齐设计 .hud-skill-special 72px 圆形）</summary>
        private const float QishuSlotSize = 72f;

        /// <summary>奇术槽与最后一个技能槽的间距（含分隔线，对齐设计 gap-3 = 12px）</summary>
        private const float QishuSlotGap = 12f;

        /// <summary>buff cell 尺寸（正方形，对齐设计 .hud-buff 38px）</summary>
        private const float BuffCellSize = 38f;

        /// <summary>顶部中央任务提示条尺寸（对齐设计 .hud-quest）</summary>
        private static readonly Float2 QuestBannerSize = new Float2(340f, 34f);

        /// <summary>系统导航按钮尺寸</summary>
        private static readonly Float2 SysNavBtnSize = new Float2(96f, 30f);

        /// <summary>系统导航按钮间距</summary>
        private const float SysNavBtnGap = 8f;

        // ---------- 美化扩展常量（对齐 combat-hud-v2.html 设计） ----------

        /// <summary>目标信息面板尺寸（顶部中央，含头像/名称/等级/HP 条/标签）</summary>
        private static readonly Float2 TargetFrameSize = new Float2(360f, 78f);

        /// <summary>队伍成员状态卡尺寸（右上角，每张卡）</summary>
        private static readonly Float2 PartyCardSize = new Float2(220f, 60f);

        /// <summary>队伍成员状态卡垂直间距</summary>
        private const float PartyCardGap = 6f;

        /// <summary>队伍容器宽度</summary>
        private const float PartySectionWidth = 220f;

        /// <summary>道具栏格子尺寸（正方形，对齐 design 36px）</summary>
        private const float ItemCellSize = 36f;

        /// <summary>道具栏格子间距</summary>
        private const float ItemCellGap = 4f;

        /// <summary>道具栏槽位数量（对齐 design 10 格，快捷键 1-0）</summary>
        private const int ItemSlotCount = 10;

        /// <summary>连击计数器尺寸</summary>
        private static readonly Float2 ComboCounterSize = new Float2(120f, 110f);

        /// <summary>小地图叠加指南针标签偏移（距小地图边缘）</summary>
        private const float CompassLabelOffset = 4f;

        // ---------- 玩家面板 buff 行常量（对齐 combat-hud-v2.html .player-buffs） ----------

        /// <summary>玩家面板 buff 槽尺寸（正方形，对齐设计 .player-buff 的 24x24）</summary>
        private const float PlayerBuffCellSize = 24f;

        /// <summary>玩家面板 buff 槽间距（对齐设计 .player-buffs gap:6px）</summary>
        private const float PlayerBuffGap = 6f;

        /// <summary>玩家面板 buff 行高度（槽 24 + 时间标签 12）</summary>
        private const float PlayerBuffRowHeight = 36f;

        /// <summary>小地图下方坐标标签高度（对齐设计 .minimap-coord）</summary>
        private const float MinimapCoordHeight = 16f;

        /// <summary>小地图与坐标标签的间距</summary>
        private const float MinimapCoordGap = 4f;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        // SubTask 5.1 水墨晕染装饰
        private InkSplash _splash1;
        private InkSplash _splash2;
        private InkSplash _splash3;

        // SubTask 5.2 引导按钮
        private InkButton _guideButton;

        // 顶部中央导航条（方位 + 目标距离）——已按设计停用（设计为任务提示条，见 _questBanner）
        private InkNavigationStrip _navStrip;

        // 顶部中央任务提示条（对齐设计 .hud-quest）
        private InkPanel _questBanner;

        // SubTask 5.4 水墨小地图（带地形快照、NPC、玩家图标）——已按设计替换为圆形罗盘（见 _compass）
        private InkMinimap _minimap;

        // 右上角圆形水墨罗盘（对齐设计 .hud-compass 148px）
        private InkCompass _compass;

        // SubTask 5.5 左上角角色信息面板（对齐 boss 目标面板布局风格）
        private ContainerControl _leftBottom;
        private InkButton _avatarButton;
        private Label _characterName;
        private Label _characterLevelLabel;
        private InkBar _hpBar;
        private Label _hpLabel;
        private InkBar _mpBar;
        private Label _mpLabel;
        private InkBar _staminaBar;
        private Label _staminaLabel;

        // SubTask 5.6 右下角技能槽 + 奇术槽
        private ContainerControl _rightBottom;
        private SkillSlotControl[] _skillSlots;
        private QishuSlotControl _qishuSlot;

        // SubTask 5.7 buff/debuff 图标条（左上角角色面板下方横向排列）
        private InkPanel _buffBar;

        /// <summary>Buff/Debuff 悬停提示 ToolBar</summary>
        private InkAttributeTooltip _buffTooltip;

        // SubTask 5.8 系统导航栏（主导航行 + 扩展导航行）
        private InkPanel _sysNav;
        private InkPanel _sysNavExtended;

        // ---------- 美化扩展子控件（对齐 combat-hud-v2.html 设计） ----------

        // 顶部中央：目标信息面板
        /// <summary>目标信息面板容器</summary>
        private ContainerControl _targetFrame;

        /// <summary>目标头像字形（"麟"）</summary>
        private Label _targetAvatarLabel;

        /// <summary>目标名称标签</summary>
        private Label _targetNameLabel;

        /// <summary>目标等级/职业标签</summary>
        private Label _targetLevelLabel;

        /// <summary>目标 HP 条</summary>
        private InkBar _targetHpBar;

        /// <summary>目标 HP 数值标签</summary>
        private Label _targetHpLabel;

        /// <summary>目标距离标签</summary>
        private Label _targetDistanceLabel;

        // 右上角：队伍成员状态卡
        /// <summary>队伍容器</summary>
        private ContainerControl _partyContainer;

        /// <summary>3 张队伍成员卡片</summary>
        private InkPanel[] _partyCards;

        /// <summary>3 名队伍成员 HP 条</summary>
        private InkBar[] _partyHpBars;

        /// <summary>3 名队伍成员 MP 条</summary>
        private InkBar[] _partyMpBars;

        // 左中：连击计数器
        /// <summary>连击计数器容器</summary>
        private ContainerControl _comboCounter;

        /// <summary>连击数字标签</summary>
        private Label _comboNumberLabel;

        /// <summary>连击倍率标签</summary>
        private Label _comboHintLabel;

        /// <summary>连击计时条</summary>
        private InkBar _comboTimerBar;

        // 右下角：道具栏（10 格水平，对齐 design 快捷键 1-0）
        /// <summary>道具栏容器</summary>
        private ContainerControl _itemBar;

        /// <summary>10 个道具格</summary>
        private InkPanel[] _itemSlots;

        // ---------- 小地图坐标标签 + 玩家面板 buff 行（对齐设计细节） ----------

        /// <summary>小地图下方坐标标签（显示当前位置名，对齐设计 .minimap-coord）</summary>
        private Label _minimapCoordLabel;

        /// <summary>玩家面板 buff 行容器（位于 _leftBottom 底部，对齐设计 .player-buffs）</summary>
        private ContainerControl _playerBuffsRow;

        /// <summary>3 个玩家 buff 时间标签（位于 buff 槽下方，对齐设计 .player-buff-time）</summary>
        private Label[] _playerBuffTimeLabels;

        // ===================================================================
        // mock 数据
        // =======================================================================

        /// <summary>5 个技能槽冷却进度（0=就绪，1=完全冷却中），mock 数据</summary>
        private float[] _skillCooldowns = { 0f, 0.3f, 0f, 0.7f, 0f };

        /// <summary>奇术是否就绪，mock 数据</summary>
        private bool _qishuReady = true;

        // ===================================================================
        // 真实数据绑定字段
        // =======================================================================

        /// <summary>绑定的角色属性组件，null 时回退到 mock 数据</summary>
        private CharacterAttributesComponent _boundCharacter;

        /// <summary>绑定的技能数组，null 时回退到 mock 冷却数据</summary>
        private SkillBase[] _boundSkills;

        /// <summary>动态 buff 列表（增强型 mock）</summary>
        private List<(string name, bool isDebuff)> _buffs = new List<(string name, bool isDebuff)>
        {
            ("攻击提升", false),
            ("防御提升", false),
            ("速度提升", false),
            ("中毒", true),
            ("减速", true),
            ("虚弱", true),
        };

        // ---------- 美化扩展 mock 数据（对齐 combat-hud-v2.html 设计） ----------

        /// <summary>目标名称（mock）— 取自设计 HTML 中的"墨麒麟"</summary>
        private string _targetName = "墨麒麟";

        /// <summary>目标头像字形（mock）</summary>
        private string _targetAvatarGlyph = "麟";

        /// <summary>目标等级（mock）</summary>
        private int _targetLevel = 50;

        /// <summary>目标职业/头衔（mock）</summary>
        private string _targetTitle = "首领";

        /// <summary>目标当前 HP（mock）</summary>
        private int _targetHpCurrent = 18500;

        /// <summary>目标最大 HP（mock）</summary>
        private int _targetHpMax = 25000;

        /// <summary>目标距离（米，mock）</summary>
        private int _targetDistance = 18;

        /// <summary>目标相对玩家方向（度，mock），0=正北</summary>
        private float _targetYaw = 135f;

        /// <summary>3 名队伍成员名称（mock）</summary>
        private string[] _partyNames = { "燕归人", "沈莘蕾", "陆孤寒" };

        /// <summary>3 名队伍成员职业（mock）</summary>
        private string[] _partyClasses = { "剑客", "医者", "侠盗" };

        /// <summary>3 名队伍成员头像字形（mock）</summary>
        private string[] _partyAvatarGlyphs = { "燕", "沈", "陆" };

        /// <summary>3 名队伍成员等级（mock）</summary>
        private int[] _partyLevels = { 40, 38, 41 };

        /// <summary>3 名队伍成员 HP 比例（mock，0-1）</summary>
        private float[] _partyHpRatio = { 0.80f, 0.65f, 0.25f };

        /// <summary>3 名队伍成员 MP 比例（mock，0-1）</summary>
        private float[] _partyMpRatio = { 0.92f, 0.70f, 0.48f };

        /// <summary>连击数（mock）</summary>
        private int _comboCount = 23;

        /// <summary>连击倍率（mock）</summary>
        private float _comboMultiplier = 1.8f;

        /// <summary>连击计时进度（mock，0-1，1=满）</summary>
        private float _comboTimerRatio = 0.68f;

        /// <summary>10 个道具格数量徽章（mock，对齐 design 快捷键 1-0）</summary>
        private string[] _itemBadges = { "×5", "×3", "×2", "×1", "", "", "", "", "", "" };

        /// <summary>10 个道具格字形（mock，前 4 格填充，后 6 格空）</summary>
        private string[] _itemGlyphs = { "血", "气", "解", "烟", "", "", "", "", "", "" };

        /// <summary>10 个道具格品质（mock，空槽用 Common）</summary>
        private InkWashTheme.InkQuality[] _itemQualities =
        {
            InkWashTheme.InkQuality.Legendary,
            InkWashTheme.InkQuality.Rare,
            InkWashTheme.InkQuality.Uncommon,
            InkWashTheme.InkQuality.Common,
            InkWashTheme.InkQuality.Common,
            InkWashTheme.InkQuality.Common,
            InkWashTheme.InkQuality.Common,
            InkWashTheme.InkQuality.Common,
            InkWashTheme.InkQuality.Common,
            InkWashTheme.InkQuality.Common,
        };

        // ---------- 小地图坐标 + 玩家面板 buff 行 mock 数据（对齐设计细节） ----------

        /// <summary>当前位置名（mock，对齐设计 .minimap-coord "昆仑墟 · 深渊"）</summary>
        private string _locationName = "昆仑墟 · 深渊";

        /// <summary>3 个玩家面板 buff 字形（mock，对齐设计 .player-buff-glyph 攻/防/轻）</summary>
        private string[] _playerBuffGlyphs = { "攻", "防", "轻" };

        /// <summary>3 个玩家面板 buff 剩余时间（mock，对齐设计 .player-buff-time）</summary>
        private string[] _playerBuffTimes = { "5:30", "8:45", "0:18" };

        /// <summary>3 个玩家面板 buff 是否为负面 debuff（mock，true=朱红边框，false=翡翠边框）</summary>
        private bool[] _playerBuffIsDebuff = { false, false, false };

        // ===================================================================
        // 屏幕尺寸缓存
        // =======================================================================

        /// <summary>当前屏幕尺寸缓存，用于布局计算</summary>
        private Float2 _screenSize;

        // ===================================================================
        // 公共 API：事件与属性
        // =======================================================================

        /// <summary>
        /// 导航请求事件。
        /// 由头像按钮、系统导航按钮等触发，参数为目标页面的 dom-id
        /// （如 <c>"nav-character-v2"</c>、<c>"nav-quests"</c>、<c>"nav-settings"</c>）。
        /// 由 <see cref="InkPageRouter"/> 订阅以执行页面跳转。
        /// </summary>
        public event Action<string> NavigationRequested;

        /// <summary>
        /// 粒子动效系统引用（可选）。
        /// 由 <see cref="MainUIManager"/> 在创建页面后注入，用于在按钮点击位置触发金粉爆发反馈。
        /// 为 null 时按钮点击不触发粒子动效（功能降级，不影响导航）。
        /// </summary>
        public InkParticleSystem ParticleSystem { get; set; }

        /// <summary>
        /// 小地图玩家朝向角（度）。0 = 正北，顺时针增加。默认 0。
        /// （历史名称保留：现驱动右上角圆形罗盘指针 <see cref="InkCompass.Yaw"/>。）
        /// </summary>
        public float MinimapPlayerYaw
        {
            get => _compass?.Yaw ?? 0f;
            set
            {
                if (_compass != null)
                    _compass.Yaw = value;
            }
        }

        /// <summary>
        /// 导航条玩家朝向角（度）。0 = 正北，顺时针增加。
        /// </summary>
        public float NavStripPlayerYaw
        {
            get => _navStrip?.PlayerYaw ?? 0f;
            set
            {
                if (_navStrip != null)
                    _navStrip.PlayerYaw = value;
            }
        }

        /// <summary>
        /// 技能冷却进度数组（5 个 0-1 值）。
        /// 0 = 就绪，1 = 完全冷却中。设置时同步更新各技能槽显示。
        /// </summary>
        public float[] SkillCooldowns
        {
            get => _skillCooldowns;
            set
            {
                _skillCooldowns = value ?? new float[5];
                if (_skillSlots == null)
                    return;
                for (int i = 0; i < _skillSlots.Length && i < _skillCooldowns.Length; i++)
                {
                    _skillSlots[i].Cooldown = _skillCooldowns[i];
                }
            }
        }

        /// <summary>
        /// 奇术是否就绪。true 时奇术槽显示脉冲动画。
        /// </summary>
        public bool QishuReady
        {
            get => _qishuReady;
            set
            {
                _qishuReady = value;
                if (_qishuSlot != null)
                    _qishuSlot.Ready = value;
            }
        }

        // ===================================================================
        // 数据绑定 API
        // =======================================================================

        /// <summary>
        /// 绑定角色属性组件。
        /// 绑定后气血/体魄条每帧从组件读取真实数据，传入 null 解除绑定回退到 mock。
        /// </summary>
        /// <param name="component">角色属性组件，null 解除绑定</param>
        public void BindCharacter(CharacterAttributesComponent component)
        {
            _boundCharacter = component;
            RefreshPlayerIdentity();
        }

        /// <summary>
        /// 从绑定的角色组件刷新身份信息（角色名、等级、头像字形）。
        /// 由 <see cref="BindCharacter"/> 立即调用，并由 <see cref="RefreshBoundData"/> 每帧同步。
        /// 未绑定时保留 mock 数据。
        /// </summary>
        private void RefreshPlayerIdentity()
        {
            if (_boundCharacter == null)
                return;

            if (_characterName != null && !string.IsNullOrEmpty(_boundCharacter.Nickname))
            {
                _characterName.Text = _boundCharacter.Nickname;
                _avatarButton.Text = _boundCharacter.Nickname.Length > 0
                    ? _boundCharacter.Nickname.Substring(0, 1)
                    : string.Empty;
            }

            if (_characterLevelLabel != null)
            {
                string stageName = GetStageName(_boundCharacter.CurrentStage);
                _characterLevelLabel.Text = $"Lv.{_boundCharacter.Level} · {stageName}";
            }
        }

        /// <summary>
        /// 获取角色成长阶段名称
        /// </summary>
        private string GetStageName(CharacterStage stage)
        {
            return stage switch
            {
                CharacterStage.Wuxia => "武侠",
                CharacterStage.Xianxia => "仙侠",
                CharacterStage.Xuanhuan => "玄幻",
                _ => "武侠"
            };
        }

        /// <summary>
        /// 绑定技能数组。
        /// 绑定后技能槽冷却每帧从 <see cref="SkillBase.GetCooldownProgress"/> 读取，传入 null 解除绑定。
        /// 注意：<see cref="SkillBase.GetCooldownProgress"/> 返回 0=刚释放/1=就绪，
        /// 内部自动反转为 <see cref="SkillSlotControl.Cooldown"/> 的 0=就绪/1=冷却中。
        /// </summary>
        /// <param name="slots">技能数组（最多 5 个），null 解除绑定</param>
        public void BindSkills(SkillBase[] slots)
        {
            _boundSkills = slots;
        }

        /// <summary>
        /// 添加一个 buff 到 buff 条（增强型 mock）。
        /// </summary>
        /// <param name="name">buff 名称</param>
        /// <param name="isDebuff">是否为负面 debuff</param>
        public void AddBuff(string name, bool isDebuff)
        {
            _buffs.Add((name, isDebuff));
            RebuildBuffBar();
        }

        /// <summary>
        /// 清空所有 buff（增强型 mock）。
        /// </summary>
        public void ClearBuffs()
        {
            _buffs.Clear();
            RebuildBuffBar();
        }

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            RefreshBoundData();
        }

        /// <summary>
        /// 每帧从绑定的数据源刷新气血/体魄/技能冷却。
        /// 未绑定时保持 mock 数据不变。
        /// </summary>
        private void RefreshBoundData()
        {
            if (_boundCharacter == null)
                return;

            // 每帧同步身份信息（防止运行时改名/升级）
            RefreshPlayerIdentity();

            // 刷新气血
            float hpRatio = _boundCharacter.MaxHealth > 0f
                ? Mathf.Clamp(_boundCharacter.CurrentHealth / _boundCharacter.MaxHealth, 0f, 1f)
                : 0f;
            float staminaRatio = _boundCharacter.MaxStamina > 0f
                ? Mathf.Clamp(_boundCharacter.CurrentStamina / _boundCharacter.MaxStamina, 0f, 1f)
                : 0f;

            if (_hpBar != null)
                _hpBar.Value = hpRatio;
            if (_hpLabel != null)
                _hpLabel.Text = $"{(int)_boundCharacter.CurrentHealth}/{(int)_boundCharacter.MaxHealth}";

            // 刷新体魄
            if (_staminaBar != null)
                _staminaBar.Value = staminaRatio;
            if (_staminaLabel != null)
                _staminaLabel.Text = $"{(int)_boundCharacter.CurrentStamina}/{(int)_boundCharacter.MaxStamina}";

            // 刷新内力（气）
            float mpRatio = _boundCharacter.MaxEnergy > 0f
                ? Mathf.Clamp(_boundCharacter.CurrentEnergy / _boundCharacter.MaxEnergy, 0f, 1f)
                : 0f;
            if (_mpBar != null)
                _mpBar.Value = mpRatio;
            if (_mpLabel != null)
                _mpLabel.Text = $"{(int)_boundCharacter.CurrentEnergy}/{(int)_boundCharacter.MaxEnergy}";

            // 刷新技能冷却
            if (_boundSkills != null && _skillSlots != null)
            {
                for (int i = 0; i < _skillSlots.Length && i < _boundSkills.Length; i++)
                {
                    var skill = _boundSkills[i];
                    if (skill != null && skill.Data != null)
                    {
                        // SkillBase.GetCooldownProgress(): 0=刚释放, 1=就绪
                        // SkillSlotControl.Cooldown: 0=就绪, 1=冷却中
                        // 需反转：cooldown = 1 - progress
                        _skillSlots[i].Cooldown = 1f - skill.GetCooldownProgress();
                    }
                }
            }
        }

        /// <summary>
        /// 重建 buff 横向图标条（位于左上角角色面板下方）。
        /// 每项为 34x34 图标单元（色调背景+字形），正面 buff 用翡翠色，负面 debuff 用朱红色。
        /// 鼠标悬停时弹出 <see cref="InkAttributeTooltip"/> 显示完整信息。
        /// </summary>
        private void RebuildBuffBar()
        {
            if (_buffBar == null)
                return;

            _buffBar.DisposeChildren();

            if (_buffs.Count == 0)
                return;

            const float cellSize = BuffCellSize;
            const float gap = 6f;
            const float startX = 0f;
            float cellX = startX;

            for (int i = 0; i < _buffs.Count; i++)
            {
                var buff = _buffs[i];
                bool isDebuff = buff.isDebuff;
                Color accentColor = isDebuff ? InkWashTheme.VermilionBright : InkWashTheme.JadeBright;
                Color iconBg = isDebuff
                    ? new Color(184f / 255f, 84f / 255f, 80f / 255f, 0.25f)
                    : new Color(126f / 255f, 171f / 255f, 158f / 255f, 0.25f);

                var cell = new BuffIconCell(
                    buff.name,
                    isDebuff,
                    accentColor,
                    iconBg,
                    _buffTooltip)
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(cellX, 4f),
                    Size = new Float2(cellSize, cellSize),
                    BackgroundColor = Color.Transparent,
                    ClipChildren = false,
                };

                _buffBar.AddChild(cell);
                cellX += cellSize + gap;
            }
        }

        /// <summary>
        /// Buff/Debuff 图标单元，支持鼠标悬停显示 ToolBar 提示。
        /// </summary>
        private class BuffIconCell : ContainerControl
        {
            private readonly string _buffName;
            private readonly bool _isDebuff;
            private readonly InkAttributeTooltip _tooltip;

            public BuffIconCell(string buffName, bool isDebuff, Color accentColor, Color iconBg, InkAttributeTooltip tooltip)
            {
                _buffName = buffName;
                _isDebuff = isDebuff;
                _tooltip = tooltip;

                BackgroundColor = iconBg;

                var glyph = new Label
                {
                    Text = buffName.Length > 0 ? buffName.Substring(0, 1) : "?",
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 14f),
                    TextColor = accentColor,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.StretchAll,
                };
                AddChild(glyph);
            }

            public override void OnMouseEnter(Float2 location)
            {
                base.OnMouseEnter(location);

                if (_tooltip == null)
                    return;

                string typeText = _isDebuff ? "减益效果" : "增益效果";
                string coreInfo = $"类型：{typeText}\n剩余时间：5:00\n层数：1";
                string additionalInfo = _isDebuff
                    ? "该效果会降低角色战斗能力，建议尽快驱散。"
                    : "该效果会提升角色战斗能力。";

                _tooltip.SetData(
                    null,
                    _buffName,
                    coreInfo,
                    additionalInfo,
                    null);

                _tooltip.Show(PointToScreen(location));
            }

            public override void OnMouseLeave()
            {
                base.OnMouseLeave();

                if (_tooltip == null)
                    return;

                _tooltip.Hide();
                _tooltip.SetData(null, string.Empty, string.Empty, string.Empty, null);
            }
        }

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化全部 8 个子区域，使用 mock 数据填充。
        /// 构造时读取 <see cref="FlaxEngine.Screen.Size"/> 计算布局，
        /// 屏幕尺寸未就绪时使用 1920x1080 兜底。
        /// </summary>
        public CombatHudPage()
        {
            // 1. 读取屏幕尺寸
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                // 屏幕尺寸尚未就绪时使用 1920x1080 兜底
                _screenSize = new Float2(1920f, 1080f);
            }

            // 2. 外壳本身：全屏拉伸 + 透明背景 + 不裁剪子控件
            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                BuildSplashDecorations();
                // 引导入口已按需求取消（引导三页保留注册，可经 F9 画廊访问）。
                // BuildGuideButton();
                // 设计 combat-hud.html：顶部中央为任务提示条（原"方位导航条"已停用，
                // 方向信息由右上角罗盘承担；目标/队伍/连击/道具栏为 combat-hud-v2 元素，本页不再构建）。
                BuildQuestBanner();
                BuildCompass();
                BuildLeftBottom();
                BuildRightBottom();
                BuildBuffBar();
                BuildSystemNav();

                // 应用初始布局（基于屏幕尺寸计算所有子控件位置）
                ApplyLayout();

                FlaxEngine.Debug.Log($"[CombatHudPage] 构造完成: Size={Size}, ChildrenCount={ChildrenCount}, _screenSize={_screenSize}");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CombatHudPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // SubTask 构造方法
        // =======================================================================

        /// <summary>
        /// SubTask 5.1：添加 3 个 <see cref="InkSplash"/> 水墨晕染装饰。
        /// 不同位置与变体（Normal/Vermilion/Elevated），不接收鼠标事件。
        /// </summary>
        private void BuildSplashDecorations()
        {
            // 对齐设计 combat-hud.html：共 2 个水墨晕染（左上 Normal 16%、右下 Elevated 14%），
            // 原第三个朱红晕染为融合扩展，已停用（_splash2 保持 null，ApplyLayout 守卫跳过）。
            _splash1 = new InkSplash
            {
                Variant = InkSplashVariant.Normal,
                Opacity = 0.16f,
                AutoFocus = false,
            };
            _splash3 = new InkSplash
            {
                Variant = InkSplashVariant.Elevated,
                Opacity = 0.14f,
                AutoFocus = false,
            };
            AddChild(_splash1);
            AddChild(_splash3);
        }

        /// <summary>
        /// 左上角引导按钮（对齐设计 combat-hud.html .hud-nav-btn）：
        /// 墨底金边圆角胶囊，文本"引导"，点击进入引导入口。
        /// </summary>
        private void BuildGuideButton()
        {
            _guideButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "引导",
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(GuideWidth, GuideHeight),
            };
            _guideButton.ButtonClicked += OnGuideButtonClicked;
            AddChild(_guideButton);
        }

        /// <summary>
        /// 顶部中央任务提示条（对齐设计 combat-hud.html .hud-quest）：
        /// 卷轴图标字形（鎏金）+ 主线标题（纸白 13px）+ 进度计数（DIN 数字体）。
        /// </summary>
        private void BuildQuestBanner()
        {
            _questBanner = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = QuestBannerSize,
            };

            var glyph = new Label
            {
                Text = "卷",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 15f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 0f),
                Size = new Float2(18f, QuestBannerSize.Y),
            };
            _questBanner.AddChild(glyph);

            var title = new Label
            {
                Text = "主线 · 前往藏经阁探查异动",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 13f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(38f, 0f),
                Size = new Float2(QuestBannerSize.X - 110f, QuestBannerSize.Y),
            };
            _questBanner.AddChild(title);

            var count = new Label
            {
                Text = "3 / 5",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(QuestBannerSize.X - 62f, 0f),
                Size = new Float2(50f, QuestBannerSize.Y),
            };
            _questBanner.AddChild(count);

            AddChild(_questBanner);
        }

        /// <summary>
        /// 顶部中央方条型导航条。
        /// 显示东南西北方位、目标方向标记、刻度和以米为单位的距离。
        /// </summary>
        private void BuildNavigationStrip()
        {
            _navStrip = new InkNavigationStrip
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = NavStripSize,
                PlayerYaw = 0f,
                TargetYaw = _targetYaw,
                TargetDistance = _targetDistance,
                HasTarget = true,
            };
            AddChild(_navStrip);
        }
        /// <summary>
        /// 右上角圆形水墨罗盘（对齐设计 combat-hud.html .hud-compass 148px）：
        /// 圆形墨底 + 金线外环 + 内圈装饰环 + 四方位字（北=朱红亮）+ 双色指针 + 中心金点。
        /// 朝向由 <see cref="MinimapPlayerYaw"/> 属性驱动（0=正北，顺时针）。
        /// </summary>
        private void BuildCompass()
        {
            _compass = new InkCompass
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(CompassSize, CompassSize),
                Yaw = 0f,
            };
            AddChild(_compass);
        }

        /// <summary>
        /// SubTask 5.5：左上角角色信息面板（对齐 boss 目标面板布局风格）。
        /// 紧凑水平布局：头像 + 角色名 + 等级一行，气血/内力/体魄三条垂直排列。
        /// 头像点击触发 <see cref="NavigationRequested"/>("nav-character-v2")。
        /// </summary>
        private void BuildLeftBottom()
        {
            _leftBottom = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = LeftBottomSize,
                BackgroundColor = new Color(0.11f, 0.12f, 0.16f, 0.92f),
                ClipChildren = false,
            };

            const float avatarSize = 36f;
            const float avatarX = 8f;
            const float avatarY = 8f;
            float textX = avatarX + avatarSize + 8f;
            const float barH = 10f;
            const float barLabelW = 80f;

            // 头像按钮：36x36，紧凑方形（对齐 boss 头像风格）
            _avatarButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = string.Empty,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(avatarX, avatarY),
                Size = new Float2(avatarSize, avatarSize),
                BackgroundColor = InkWashTheme.GoldPrimary,
            };
            _avatarButton.ButtonClicked += OnAvatarButtonClicked;
            _leftBottom.AddChild(_avatarButton);

            // 角色名（水平，Display 字体，鎏金亮色，对齐 boss 名称风格）
            _characterName = new Label
            {
                Text = "慕容凌霄",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 16f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(textX, 6f),
                Size = new Float2(140f, 20f),
            };
            _leftBottom.AddChild(_characterName);

            // 等级标签（对齐 boss 等级风格）
            _characterLevelLabel = new Label
            {
                Text = "Lv.1 · 剑客",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                TextColor = InkWashTheme.PaperFaded,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(textX, 26f),
                Size = new Float2(120f, 16f),
            };
            _leftBottom.AddChild(_characterLevelLabel);

            float barW = LeftBottomSize.X - barLabelW - 20f;

            // 气血条：Blood 填充（对齐 boss HP 条风格），紧凑高度
            _hpBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Blood,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(avatarX, 48f),
                Size = new Float2(barW, barH),
                Value = 0.85f,
            };
            _leftBottom.AddChild(_hpBar);

            // 气血数值标签
            _hpLabel = new Label
            {
                Text = "8500 / 10000",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 10f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftBottomSize.X - barLabelW - 8f, 44f),
                Size = new Float2(barLabelW, 16f),
            };
            _leftBottom.AddChild(_hpLabel);

            // 内力条（气）：Jade 填充，紧凑
            _mpBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Jade,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(avatarX, 64f),
                Size = new Float2(barW, 6f),
                Value = 0.75f,
            };
            _leftBottom.AddChild(_mpBar);

            // 内力数值标签
            _mpLabel = new Label
            {
                Text = "800 / 1000",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 9f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftBottomSize.X - barLabelW - 8f, 62f),
                Size = new Float2(barLabelW, 12f),
            };
            _leftBottom.AddChild(_mpLabel);

            // 体魄条：Gold 填充，最矮
            _staminaBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Gold,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(avatarX, 76f),
                Size = new Float2(barW, 4f),
                Value = 0.6f,
            };
            _leftBottom.AddChild(_staminaBar);

            // 体魄数值标签
            _staminaLabel = new Label
            {
                Text = "60 / 100",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 9f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LeftBottomSize.X - barLabelW - 8f, 74f),
                Size = new Float2(barLabelW, 10f),
            };
            _leftBottom.AddChild(_staminaLabel);

            AddChild(_leftBottom);
        }

        /// <summary>
        /// SubTask 5.6：右下角技能槽 + 奇术槽。
        /// <see cref="ContainerControl"/> 尺寸 (420, 84)。
        /// 5 个 <see cref="SkillSlotControl"/> 圆形技能槽 58x58，间距 10px，从左到右排列；
        /// 每个技能槽底部有快捷键标签"1"~"5"。
        /// 1 个 <see cref="QishuSlotControl"/> 奇术槽 76x76，金边（<see cref="InkWashTheme.BorderGoldStrong"/>），
        /// 位于最右侧，<see cref="QishuSlotControl.Ready"/> = true 时显示脉冲动画。
        /// </summary>
        private void BuildRightBottom()
        {
            _rightBottom = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = RightBottomSize,
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };

            string[] skillGlyphs = { "斩", "疾", "焰", "阵", "雷" };
            _skillSlots = new SkillSlotControl[5];
            float skillX = 0f;
            for (int i = 0; i < 5; i++)
            {
                var slot = new SkillSlotControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(skillX, (RightBottomSize.Y - SkillSlotSize) * 0.5f),
                    Size = new Float2(SkillSlotSize, SkillSlotSize),
                    Hotkey = (i + 1).ToString(),
                    Cooldown = i < _skillCooldowns.Length ? _skillCooldowns[i] : 0f,
                    Glyph = skillGlyphs[i],
                };
                _skillSlots[i] = slot;
                _rightBottom.AddChild(slot);
                skillX += SkillSlotSize + SkillSlotGap;
            }

            // 奇术槽：76x76，与最后一个技能槽间隔 18px，垂直居中对齐
            float qishuX = skillX - SkillSlotGap + QishuSlotGap;
            float qishuY = (RightBottomSize.Y - QishuSlotSize) * 0.5f;
            _qishuSlot = new QishuSlotControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(qishuX, qishuY),
                Size = new Float2(QishuSlotSize, QishuSlotSize),
                Ready = _qishuReady,
            };
            _rightBottom.AddChild(_qishuSlot);

            AddChild(_rightBottom);
        }

        /// <summary>
        /// SubTask 5.7：左上角角色面板下方 buff/debuff 横向图标条。
        /// <see cref="InkPanel"/> 尺寸 (360, 42)，子控件由 <see cref="RebuildBuffBar"/> 动态生成。
        /// 初始 6 个 mock buff（3 正面翡翠 + 3 负面朱红），支持 <see cref="AddBuff"/>/<see cref="ClearBuffs"/> 动态增减。
        /// 鼠标悬停图标时通过 <see cref="_buffTooltip"/> 弹出完整信息 ToolBar。
        /// </summary>
        private void BuildBuffBar()
        {
            _buffBar = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = BuffBarSize,
                ClipChildren = false,
            };
            AddChild(_buffBar);

            _buffTooltip = new InkAttributeTooltip
            {
                Visible = false,
            };
            AddChild(_buffTooltip);

            RebuildBuffBar();
        }

        /// <summary>
        /// SubTask 5.8：底部系统导航栏（双行布局，对齐 combat-hud.html 设计）。
        /// <para>
        /// 第一行（<see cref="_sysNav"/>，760x36）：9 个主导航按钮 + 分隔符 + 传统模式切换按钮，按 4 大子系统分组：
        /// <list type="bullet">
        ///   <item>角色与背包：角色(nav-character-panel) / 武学(nav-skill-tree) / 背包(nav-inventory)</item>
        ///   <item>任务与技能：任务(nav-quests) / 地图(nav-world-map) / 罗盘(nav-compass)</item>
        ///   <item>社交与商城：社交(nav-friends) / 商城(nav-shop)</item>
        ///   <item>战斗模式切换：传统模式(toggle-traditional)，金色强调背景</item>
        /// </list>
        /// 第二行（<see cref="_sysNavExtended"/>，760x36）：9 个扩展导航按钮：
        /// <list type="bullet">
        ///   <item>角色与背包：强化(nav-equipment-enhance) / 制造(nav-crafting) / 坐骑(nav-mount-pet)</item>
        ///   <item>社交与商城：好友(nav-friends) / 邮件(nav-mail) / 排行(nav-leaderboard) / 师徒(nav-mentor) / 成就(nav-achievement)</item>
        ///   <item>任务与技能：副本(nav-dungeon-entry)</item>
        /// </list>
        /// 所有按钮点击触发 <see cref="OnSystemNavButtonClicked"/>，发射金粉粒子并请求导航。
        /// </para>
        /// </summary>
        private void BuildSystemNav()
        {
            // 对齐设计 combat-hud.html .hud-sysnav：底部中央一行 6 项
            //（任务/闲趣/异闻/成就/留影/设置）。原双行 18 项导航与传统模式切换为融合扩展，已停用。
            _sysNav = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = SysNavSize,
            };

            var entries = new[]
            {
                (label: "任务", domId: InkPageDomIds.NavQuests),
                (label: "闲趣", domId: InkPageDomIds.NavCasualMode),
                (label: "异闻", domId: InkPageDomIds.PopupBestiarySide),
                // 「成就」应打开成就列表；RewardAchievement 是"成就解锁"庆祝页，
                // 里头是设计稿写死的「初心者称号 / 已解锁传送功能」，点进来就是在展示假成就。
                (label: "成就", domId: InkPageDomIds.NavAchievement),
                (label: "留影", domId: InkPageDomIds.NavPhotoMode),
                (label: "设置", domId: InkPageDomIds.NavSettings),
            };

            const float pad = 6f;
            const float gap = 4f;
            float btnWidth = (SysNavSize.X - pad * 2f - gap * (entries.Length - 1)) / entries.Length;
            float btnHeight = SysNavSize.Y - pad * 2f;

            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                var btn = new InkButton
                {
                    Variant = InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Sm,
                    Text = entry.label,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(pad + i * (btnWidth + gap), pad),
                    Size = new Float2(btnWidth, btnHeight),
                };

                string domId = entry.domId;
                btn.ButtonClicked += (b) => OnSystemNavButtonClicked(domId, b);

                _sysNav.AddChild(btn);
            }

            AddChild(_sysNav);
        }

        // ===================================================================
        // 美化扩展 Build 方法（对齐 combat-hud-v2.html 设计）
        // =======================================================================

        /// <summary>
        /// 顶部中央目标信息面板。
        /// 对齐设计 HTML 中 .hud-target：目标头像（朱红方形 + 字形"麟"）、
        /// 目标名称（书法字体，朱红亮色）、Lv.X 头衔、距离标签、HP 条（朱红）+ 数值标签。
        /// 使用 <see cref="ContainerControl"/> 作为容器，手动绘制朱红边框与噪声背景。
        /// </summary>
        private void BuildTargetInfo()
        {
            _targetFrame = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = TargetFrameSize,
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };

            // 目标头像：36x36 方形，朱红渐变背景 + 字形"麟"
            _targetAvatarLabel = new Label
            {
                Text = _targetAvatarGlyph,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 18f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 8f),
                Size = new Float2(36f, 36f),
                BackgroundColor = Color.Transparent,
            };
            _targetFrame.AddChild(_targetAvatarLabel);

            // 目标名称：书法字体，朱红亮色
            _targetNameLabel = new Label
            {
                Text = _targetName,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 16f),
                TextColor = InkWashTheme.VermilionBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(52f, 6f),
                Size = new Float2(140f, 20f),
            };
            _targetFrame.AddChild(_targetNameLabel);

            // 目标等级/头衔
            _targetLevelLabel = new Label
            {
                Text = $"Lv. {_targetLevel} · {_targetTitle}",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                TextColor = InkWashTheme.PaperFaded,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(52f, 26f),
                Size = new Float2(140f, 16f),
            };
            _targetFrame.AddChild(_targetLevelLabel);

            // 目标距离标签（右上）
            _targetDistanceLabel = new Label
            {
                Text = $"{_targetDistance}m",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(TargetFrameSize.X - 64f, 14f),
                Size = new Float2(56f, 16f),
            };
            _targetFrame.AddChild(_targetDistanceLabel);

            // 目标 HP 条（设计方案 §3.1 HP=blood-primary，敌对目标用血色）
            float hpRatio = _targetHpMax > 0 ? Mathf.Clamp((float)_targetHpCurrent / _targetHpMax, 0f, 1f) : 0f;
            _targetHpBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Blood,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 48f),
                Size = new Float2(TargetFrameSize.X - 80f, 10f),
                Value = hpRatio,
            };
            _targetFrame.AddChild(_targetHpBar);

            // 目标 HP 数值标签
            _targetHpLabel = new Label
            {
                Text = $"{_targetHpCurrent:N0} / {_targetHpMax:N0}",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 10f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(TargetFrameSize.X - 70f, 44f),
                Size = new Float2(62f, 16f),
            };
            _targetFrame.AddChild(_targetHpLabel);

            AddChild(_targetFrame);
        }

        /// <summary>
        /// 右上角队伍成员状态卡（3 名 mock 成员）。
        /// 对齐设计 HTML 中 .hud-party：每张卡片含头像（圆形 + 字形）、
        /// 名称、等级、HP 条（朱红/翡翠按比例切换）+ MP 条（鎏金）。
        /// 低 HP 成员卡片使用朱红左边框并触发脉冲动画。
        /// </summary>
        private void BuildPartySection()
        {
            float sectionH = 3f * PartyCardSize.Y + 2f * PartyCardGap;
            _partyContainer = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(PartySectionWidth, sectionH),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };

            _partyCards = new InkPanel[3];
            _partyHpBars = new InkBar[3];
            _partyMpBars = new InkBar[3];

            for (int i = 0; i < 3; i++)
            {
                var card = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, i * (PartyCardSize.Y + PartyCardGap)),
                    Size = PartyCardSize,
                };

                // 头像字形标签
                var avatarLabel = new Label
                {
                    Text = _partyAvatarGlyphs[i],
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 13f),
                    TextColor = InkWashTheme.PaperBright,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 14f),
                    Size = new Float2(32f, 32f),
                    BackgroundColor = Color.Transparent,
                };
                card.AddChild(avatarLabel);

                // 名称标签
                var nameLabel = new Label
                {
                    Text = _partyNames[i],
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 12f),
                    TextColor = InkWashTheme.PaperBright,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(48f, 6f),
                    Size = new Float2(120f, 16f),
                };
                card.AddChild(nameLabel);

                // 等级标签
                var levelLabel = new Label
                {
                    Text = $"Lv.{_partyLevels[i]}",
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 10f),
                    TextColor = InkWashTheme.PaperFaded,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(PartyCardSize.X - 56f, 6f),
                    Size = new Float2(48f, 16f),
                };
                card.AddChild(levelLabel);

                // HP 条（设计方案 §3.1 HP=blood-primary；低 HP 升级为朱红示警）
                var hpVariant = _partyHpRatio[i] < 0.3f
                    ? InkBarFillVariant.Vermilion
                    : InkBarFillVariant.Blood;
                var hpBar = new InkBar
                {
                    FillVariant = hpVariant,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(48f, 26f),
                    Size = new Float2(PartyCardSize.X - 56f, 6f),
                    Value = _partyHpRatio[i],
                };
                card.AddChild(hpBar);
                _partyHpBars[i] = hpBar;

                // MP 条（设计方案 §3.1 MP=jade-primary，更细）
                var mpBar = new InkBar
                {
                    FillVariant = InkBarFillVariant.Jade,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(48f, 36f),
                    Size = new Float2(PartyCardSize.X - 56f, 4f),
                    Value = _partyMpRatio[i],
                };
                card.AddChild(mpBar);
                _partyMpBars[i] = mpBar;

                _partyCards[i] = card;
                _partyContainer.AddChild(card);
            }

            AddChild(_partyContainer);
        }

        /// <summary>
        /// 左中连击计数器。
        /// 对齐设计 HTML 中 .hud-combo：连击数（大号书法字体，鎏金亮色 + 辉光）、
        /// "COMBO" 副标题、计时条、倍率提示。
        /// </summary>
        private void BuildComboCounter()
        {
            _comboCounter = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = ComboCounterSize,
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };

            // 连击数标签（大号）
            _comboNumberLabel = new Label
            {
                Text = _comboCount.ToString(),
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 42f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 18f),
                Size = new Float2(ComboCounterSize.X, 48f),
            };
            _comboCounter.AddChild(_comboNumberLabel);

            // "连击" 副标题
            var comboLabel = new Label
            {
                Text = "连击",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 13f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 2f),
                Size = new Float2(ComboCounterSize.X, 16f),
            };
            _comboCounter.AddChild(comboLabel);

            // "COMBO" 英文副标
            var comboSub = new Label
            {
                Text = "COMBO",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 10f),
                TextColor = InkWashTheme.PaperFaded,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 66f),
                Size = new Float2(ComboCounterSize.X, 14f),
            };
            _comboCounter.AddChild(comboSub);

            // 连击计时条
            _comboTimerBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Gold,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ComboCounterSize.X * 0.5f - 30f, 84f),
                Size = new Float2(60f, 4f),
                Value = _comboTimerRatio,
            };
            _comboCounter.AddChild(_comboTimerBar);

            // 倍率提示
            _comboHintLabel = new Label
            {
                Text = $"倍率 ×{_comboMultiplier:F1}",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 10f),
                TextColor = InkWashTheme.VermilionBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 92f),
                Size = new Float2(ComboCounterSize.X, 14f),
            };
            _comboCounter.AddChild(_comboHintLabel);

            AddChild(_comboCounter);
        }

        /// <summary>
        /// 右下角道具栏（10 格水平，对齐 design .hud-item-bar）。
        /// 每格含快捷键标签（1-0）、字形（书法字体）、数量徽章。
        /// 填充格有色调背景，空格暗色低透明度。
        /// </summary>
        private void BuildItemBar()
        {
            float barW = ItemSlotCount * ItemCellSize + (ItemSlotCount - 1) * ItemCellGap;
            _itemBar = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(barW, ItemCellSize),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };

            _itemSlots = new InkPanel[ItemSlotCount];
            for (int i = 0; i < ItemSlotCount; i++)
            {
                float x = i * (ItemCellSize + ItemCellGap);
                bool isFilled = i < 4 && !string.IsNullOrEmpty(_itemGlyphs[i]);

                var slot = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(x, 0f),
                    Size = new Float2(ItemCellSize, ItemCellSize),
                    BackgroundColor = GetItemSlotBgColor(i, isFilled),
                };

                // 快捷键标签（1-0），左上角
                var keyLabel = new Label
                {
                    Text = (i + 1).ToString(),
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 8f),
                    TextColor = InkWashTheme.PaperAged,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(2f, 1f),
                    Size = new Float2(10f, 10f),
                    BackgroundColor = Color.Transparent,
                };
                slot.AddChild(keyLabel);

                if (isFilled)
                {
                    // 字形（中央）
                    var glyphLabel = new Label
                    {
                        Text = _itemGlyphs[i],
                        Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 16f),
                        TextColor = InkWashTheme.QualityTextColor(_itemQualities[i]),
                        HorizontalAlignment = TextAlignment.Center,
                        VerticalAlignment = TextAlignment.Center,
                        AnchorPreset = AnchorPresets.StretchAll,
                        Location = Float2.Zero,
                        Size = new Float2(ItemCellSize, ItemCellSize),
                        BackgroundColor = Color.Transparent,
                    };
                    slot.AddChild(glyphLabel);

                    // 数量徽章（右下角）
                    if (!string.IsNullOrEmpty(_itemBadges[i]))
                    {
                        var badgeLabel = new Label
                        {
                            Text = _itemBadges[i],
                            Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 8f),
                            TextColor = InkWashTheme.PaperBright,
                            HorizontalAlignment = TextAlignment.Center,
                            VerticalAlignment = TextAlignment.Center,
                            AnchorPreset = AnchorPresets.TopLeft,
                            Location = new Float2(ItemCellSize - 24f, ItemCellSize - 12f),
                            Size = new Float2(22f, 11f),
                            BackgroundColor = Color.Transparent,
                        };
                        slot.AddChild(badgeLabel);
                    }
                }

                _itemSlots[i] = slot;
                _itemBar.AddChild(slot);
            }

            AddChild(_itemBar);
        }

        /// <summary>
        /// 根据道具格索引与是否填充返回背景色调色。
        /// 对齐 design：血→红、气→碧、解→琥珀、烟→金、空→暗。
        /// </summary>
        private Color GetItemSlotBgColor(int index, bool isFilled)
        {
            if (!isFilled)
                return new Color(0f, 0f, 0f, 0.25f);
            switch (_itemGlyphs[index])
            {
                case "血": return new Color(184f / 255f, 84f / 255f, 80f / 255f, 0.15f);
                case "气": return new Color(126f / 255f, 171f / 255f, 158f / 255f, 0.15f);
                case "解": return new Color(196f / 255f, 123f / 255f, 62f / 255f, 0.15f);
                case "烟": return new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.15f);
                default: return new Color(0f, 0f, 0f, 0.15f);
            }
        }

        // ===================================================================
        // 布局计算
        // =======================================================================

        /// <summary>
        /// 根据当前 <see cref="_screenSize"/> 重新计算所有子控件的位置。
        /// 由构造函数与 <see cref="RefreshLayout"/> 调用。
        /// </summary>
        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;

            // 对齐设计 combat-hud.html 定位：
            // top-6/left-6/right-6 = 24px；bottom-24 = 96px（左下/右下/底部中央组）；
            // bottom-4 = 16px（系统导航）；左右组 left-8/right-8 = 32px。

            // 水墨晕染装饰（设计 .ink-splash：左上 (-60,-80) 16%；右下 18% 处 14%）
            if (_splash1 != null)
            {
                _splash1.Location = new Float2(-60f, -80f);
            }
            if (_splash3 != null)
            {
                _splash3.Location = new Float2(sw * 0.82f - 200f, sh - 160f);
            }

            // 左上角引导按钮（设计 top-6 left-6）
            if (_guideButton != null)
            {
                _guideButton.Location = new Float2(24f, 24f);
            }

            // 顶部中央任务提示条（设计 top-6 居中）
            if (_questBanner != null)
            {
                _questBanner.Location = new Float2(sw * 0.5f - QuestBannerSize.X * 0.5f, 24f);
            }

            // 右上角圆形罗盘（设计 top-6 right-6）
            if (_compass != null)
            {
                _compass.Location = new Float2(sw - CompassSize - 24f, 24f);
            }

            // 左下角角色信息组（设计 bottom-24 left-8=32）
            if (_leftBottom != null)
            {
                _leftBottom.Location = new Float2(32f, sh - 96f - LeftBottomSize.Y);
            }

            // 右下角技能组（设计 bottom-24 right-8=32）
            if (_rightBottom != null)
            {
                _rightBottom.Location = new Float2(sw - RightBottomSize.X - 32f, sh - 96f - RightBottomSize.Y + 10f);
            }

            // 底部中央 buff/debuff 图标条（设计 bottom-24 居中）
            if (_buffBar != null)
            {
                _buffBar.Location = new Float2(sw * 0.5f - BuffBarSize.X * 0.5f, sh - 96f - BuffBarSize.Y);
            }

            // 底部系统导航栏（设计 bottom-4=16 居中，单行 6 项）
            if (_sysNav != null)
            {
                _sysNav.Location = new Float2(sw * 0.5f - SysNavSize.X * 0.5f, sh - 16f - SysNavSize.Y);
            }
        }

        /// <summary>
        /// 在屏幕尺寸变化时重新布局所有子控件。
        /// 外部（如 <see cref="InkPageShell"/> 或屏幕大小变更监听器）应调用此方法。
        /// </summary>
        public void RefreshLayout()
        {
            // 优先使用控件实际尺寸（已由 InkPageShell.LoadPage 的 StretchAll 锚点填充父容器）
            float w = Width;
            float h = Height;
            if (w <= 0f || h <= 0f)
            {
                // 控件尚未布局，回退到屏幕尺寸
                var screen = FlaxEngine.Screen.Size;
                w = screen.X;
                h = screen.Y;
            }
            if (w <= 0f || h <= 0f)
            {
                // 仍然为 0，使用 1920x1080 兜底
                w = 1920f;
                h = 1080f;
            }
            _screenSize = new Float2(w, h);
            ApplyLayout();
            FlaxEngine.Debug.Log($"[CombatHudPage] RefreshLayout: Width={Width}, Height={Height}, _screenSize={_screenSize}");
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        /// <summary>
        /// 引导按钮点击处理：记录"引导功能待落地"占位日志，并触发金粉反馈。
        /// </summary>
        /// <param name="button">触发事件的按钮</param>
        private void OnGuideButtonClicked(Button button)
        {
            try
            {
                EmitGoldAtButton(button);
                // 设计 combat-hud.html：link-guide-hud → 引导 HUD 页（已注册路由）。
                NavigationRequested?.Invoke(InkPageDomIds.GuideHud);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CombatHudPage] 引导按钮导航失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 头像按钮点击处理：触发 <see cref="NavigationRequested"/>（角色属性合并入口 → 角色信息面板，含行囊），
        /// 由 <see cref="InkPageRouter"/> 订阅后执行跳转。
        /// 同时在按钮中心位置触发金粉爆发反馈。
        /// </summary>
        /// <param name="button">触发事件的按钮</param>
        private void OnAvatarButtonClicked(Button button)
        {
            try
            {
                EmitGoldAtButton(button);
                NavigationRequested?.Invoke(InkPageDomIds.NavInventory); // 角色属性合并入口：角色信息面板（含行囊）
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CombatHudPage] NavigationRequested(nav-character-v2) 触发失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 系统导航按钮点击处理。
        /// 若 <paramref name="domId"/> 非空，触发 <see cref="NavigationRequested"/>；
        /// 否则记录"功能待落地"日志。
        /// 无论是否导航，均在按钮中心位置触发金粉爆发反馈。
        /// </summary>
        /// <param name="domId">按钮绑定的目标 dom-id，null 表示占位</param>
        /// <param name="sourceButton">触发事件的按钮，用于定位金粉爆发中心</param>
        private void OnSystemNavButtonClicked(string domId, Button sourceButton)
        {
            try
            {
                EmitGoldAtButton(sourceButton);
                if (string.IsNullOrEmpty(domId))
                {
                    FlaxEngine.Debug.Log("[CombatHudPage] 功能待落地");
                    return;
                }

                NavigationRequested?.Invoke(domId);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CombatHudPage] NavigationRequested({domId}) 触发失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 在指定按钮中心位置触发金粉爆发粒子反馈。
        /// <para>
        /// 将按钮中心点转换为粒子系统局部坐标后调用
        /// <see cref="InkParticleSystem.EmitGoldBurst"/>，对应 ink-particles.css
        /// 的 gold-burst 动画（按钮点击 800ms 金粉扩散）。
        /// 若 <see cref="ParticleSystem"/> 为 null 则静默跳过（功能降级）。
        /// </para>
        /// </summary>
        /// <param name="button">触发按钮，用于计算屏幕坐标</param>
        private void EmitGoldAtButton(Button button)
        {
            try
            {
                if (ParticleSystem == null || button == null)
                    return;

                // 按钮中心点（按钮局部坐标）
                var buttonCenter = new Float2(button.Width * 0.5f, button.Height * 0.5f);
                // 转换为屏幕坐标
                var screenPos = button.PointToScreen(buttonCenter);
                // 转换为粒子系统局部坐标
                var localPos = ParticleSystem.PointFromScreen(screenPos);
                // 发射 14 颗金粉粒子（略多于默认 12，强化按钮反馈）
                ParticleSystem.EmitGoldBurst(localPos, count: 14, isLarge: false);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[CombatHudPage] EmitGoldAtButton 失败（不影响导航）: {ex.Message}");
            }
        }
    }

    // =======================================================================
    // SubTask 5.4 辅助控件：水墨指南针
    // =======================================================================

    /// <summary>
    /// 水墨指南针控件。
    /// 圆形墨色背景 + 金色边框 + 四方位字（东南西北）+ 朱红指针。
    /// 通过 <see cref="Yaw"/> 属性控制指针角度（0=正北，顺时针），
    /// 内部 <see cref="Update"/> 实现 ±5° 缓慢摆动动画。
    /// </summary>
    internal class InkCompass : ContainerControl
    {
        /// <summary>指针摆动幅度（度）</summary>
        private const float SwayAmplitude = 5f;

        /// <summary>指针摆动周期（秒）</summary>
        private const float SwayPeriod = 4f;

        /// <summary>圆形三角形扇分段数</summary>
        private const int CircleSegments = 32;

        /// <summary>指针长度（占半径的比例）</summary>
        private const float NeedleLengthRatio = 0.78f;

        /// <summary>指针底部宽度（占半径的比例）</summary>
        private const float NeedleBaseRatio = 0.12f;

        /// <summary>累计动画时间</summary>
        private float _animTime;

        /// <summary>指针偏航角（度），默认 0</summary>
        private float _yaw;

        /// <summary>
        /// 指针偏航角（度）。0 = 正北，顺时针增加。
        /// 实际绘制角度 = <see cref="Yaw"/> + 摆动动画偏移。
        /// </summary>
        public float Yaw
        {
            get => _yaw;
            set => _yaw = value;
        }

        /// <summary>
        /// 构造函数：初始化为透明、不裁剪的指南针控件。
        /// </summary>
        public InkCompass()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
        }

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            _animTime += deltaTime;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            var center = new Float2(Width * 0.5f, Height * 0.5f);
            float radius = Mathf.Min(Width, Height) * 0.5f;

            // 1. 圆形墨色背景（BaseTertiary 半透明）
            var bgColor = new Color(
                InkWashTheme.BaseTertiary.R,
                InkWashTheme.BaseTertiary.G,
                InkWashTheme.BaseTertiary.B,
                0.85f);
            FillCircle(center, radius, bgColor);

            // 2. 金色边框
            DrawCircleRing(center, radius, InkWashTheme.BorderGold, 1f);

            // 3. 内圈装饰线（对齐设计 .hud-compass-ring inset 8px，金色 18%）
            DrawCircleRing(center, radius - 8f,
                new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.18f), 1f);

            // 4. 四方位字"东南西北"（对齐设计：北=朱红亮书法体 13px，南/东/西=旧纸色）
            var fontRef = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 13f);
            float labelOffset = radius - 13f;
            DrawDirectionLabel(fontRef, center, new Float2(0f, -labelOffset), "北", InkWashTheme.VermilionBright);
            DrawDirectionLabel(fontRef, center, new Float2(0f, labelOffset), "南", InkWashTheme.PaperAged);
            DrawDirectionLabel(fontRef, center, new Float2(labelOffset, 0f), "东", InkWashTheme.PaperAged);
            DrawDirectionLabel(fontRef, center, new Float2(-labelOffset, 0f), "西", InkWashTheme.PaperAged);

            // 5. 双色指针（对齐设计 .hud-compass-needle：3px 线，上半朱红、下半暗灰）
            // 实际角度 = Yaw + 摆动偏移（正弦波 ±5°）
            float sway = Mathf.Sin(_animTime * (Mathf.TwoPi / SwayPeriod)) * SwayAmplitude;
            float angleDeg = _yaw + sway;
            float angleRad = Mathf.DegreesToRadians * angleDeg;
            DrawTwoToneNeedle(center, radius, angleRad);

            // 6. 中心金点（对齐设计 .hud-compass-center 8px）
            FillCircle(center, 4f, InkWashTheme.GoldPrimary);
        }

        /// <summary>
        /// 在指定中心位置绘制一个文本方位字。
        /// </summary>
        /// <param name="fontRef">字体引用</param>
        /// <param name="center">指南针中心</param>
        /// <param name="offset">从中心到标签的偏移</param>
        /// <param name="text">方位字</param>
        /// <param name="color">文字色</param>
        private void DrawDirectionLabel(FontReference fontRef, Float2 center, Float2 offset, string text, Color color)
        {
            float size = 14f;
            var rect = new Rectangle(
                center.X + offset.X - size * 0.5f,
                center.Y + offset.Y - size * 0.5f,
                size, size);
            var font = fontRef.GetFont();
            if (font != null)
            {
                Render2D.DrawText(font, text, rect, color,
                    TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>
        /// 绘制双色指针（对齐设计 .hud-compass-needle）：3px 线，
        /// 上半段朱红（中心→48%）、下半段暗灰（52%→尖端），延伸至 <see cref="NeedleLengthRatio"/> 半径。
        /// </summary>
        /// <param name="center">指针基点（指南针中心）</param>
        /// <param name="radius">指南针半径</param>
        /// <param name="angleRad">指针角度（弧度，0 = 正北，顺时针）</param>
        private void DrawTwoToneNeedle(Float2 center, float radius, float angleRad)
        {
            // 屏幕坐标系：角度 0 = 正北（向上），顺时针增加；方向向量 (sin a, -cos a)
            float dirX = Mathf.Sin(angleRad);
            float dirY = -Mathf.Cos(angleRad);
            float len = radius * NeedleLengthRatio;

            var tip = center + new Float2(dirX * len, dirY * len);
            var upperEnd = center + new Float2(dirX * len * 0.48f, dirY * len * 0.48f);
            var lowerStart = center + new Float2(dirX * len * 0.52f, dirY * len * 0.52f);

            const float thickness = 3f;
            Render2D.DrawLine(center, upperEnd, InkWashTheme.VermilionPrimary, thickness);
            Render2D.DrawLine(lowerStart, tip, InkWashTheme.TextTertiary, thickness);
        }

        /// <summary>
        /// 使用三角形扇填充一个圆形。
        /// </summary>
        /// <param name="center">圆心</param>
        /// <param name="radius">半径</param>
        /// <param name="color">填充颜色</param>
        private static void FillCircle(Float2 center, float radius, Color color)
        {
            if (radius <= 0f)
                return;

            var vertices = new Float2[CircleSegments * 3];
            for (int i = 0; i < CircleSegments; i++)
            {
                float a1 = (i / (float)CircleSegments) * Mathf.TwoPi;
                float a2 = ((i + 1) / (float)CircleSegments) * Mathf.TwoPi;
                int idx = i * 3;
                vertices[idx] = center;
                vertices[idx + 1] = center + new Float2(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
                vertices[idx + 2] = center + new Float2(Mathf.Cos(a2) * radius, Mathf.Sin(a2) * radius);
            }
            Render2D.FillTriangles(vertices, color);
        }

        /// <summary>
        /// 绘制圆环描边（用线段近似）。
        /// </summary>
        /// <param name="center">圆心</param>
        /// <param name="radius">半径</param>
        /// <param name="color">描边颜色</param>
        /// <param name="thickness">线宽</param>
        private static void DrawCircleRing(Float2 center, float radius, Color color, float thickness)
        {
            if (radius <= 0f)
                return;

            for (int i = 0; i < CircleSegments; i++)
            {
                float a1 = (i / (float)CircleSegments) * Mathf.TwoPi;
                float a2 = ((i + 1) / (float)CircleSegments) * Mathf.TwoPi;
                var p1 = center + new Float2(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
                var p2 = center + new Float2(Mathf.Cos(a2) * radius, Mathf.Sin(a2) * radius);
                Render2D.DrawLine(p1, p2, color, thickness);
            }
        }
    }

    // =======================================================================
    // SubTask 5.6 辅助控件：技能槽
    // =======================================================================

    /// <summary>
    /// 圆形技能槽控件（对齐设计 .hud-skill 60px 圆形）：
    /// 圆形墨底 + 金线圆环 + 冷却饼图遮罩（自顶部顺时针）+ 中央字形 + 底部快捷键标签。
    /// </summary>
    internal class SkillSlotControl : ContainerControl
    {
        /// <summary>冷却遮罩颜色（黑色半透明）</summary>
        private static readonly Color CooldownMaskColor = new Color(0f, 0f, 0f, 0.55f);

        /// <summary>技能槽背景色（Paper 半透明，对齐 design）</summary>
        private static readonly Color SlotBgColor = new Color(
            InkWashTheme.BaseTertiary.R,
            InkWashTheme.BaseTertiary.G,
            InkWashTheme.BaseTertiary.B,
            0.70f);

        /// <summary>当前冷却进度（0=就绪，1=完全冷却中）</summary>
        private float _cooldown;

        /// <summary>快捷键标签文本</summary>
        private string _hotkey = string.Empty;

        /// <summary>中央字形（如"斩""疾""焰"等）</summary>
        private string _glyph = string.Empty;

        /// <summary>
        /// 冷却进度（0.0~1.0），自动钳制。
        /// </summary>
        public float Cooldown
        {
            get => _cooldown;
            set => _cooldown = Mathf.Clamp(value, 0f, 1f);
        }

        /// <summary>
        /// 快捷键标签文本（如"1"、"2"）。
        /// </summary>
        public string Hotkey
        {
            get => _hotkey;
            set => _hotkey = value ?? string.Empty;
        }

        /// <summary>
        /// 中央字形（如"斩""疾""焰"）。
        /// </summary>
        public string Glyph
        {
            get => _glyph;
            set => _glyph = value ?? string.Empty;
        }

        /// <summary>
        /// 构造函数。
        /// </summary>
        public SkillSlotControl()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            var center = new Float2(Width * 0.5f, Height * 0.5f);
            float radius = Mathf.Min(Width, Height) * 0.5f;
            var rect = new Rectangle(0f, 0f, Width, Height);

            // 1. 圆形墨色背景（对齐设计 .hud-skill 圆形）
            FillCircle(center, radius, SlotBgColor);

            // 2. 金线圆环边框
            DrawCircleRing(center, radius, InkWashTheme.BorderGold, 1f);

            // 3. 冷却饼图遮罩（对齐设计 conic-gradient：自顶部顺时针扫过）
            if (_cooldown > 0f)
            {
                FillPie(center, radius, _cooldown, CooldownMaskColor);
            }

            // 4. 中央字形
            if (!string.IsNullOrEmpty(_glyph))
            {
                var glyphFontRef = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 20f);
                var glyphFont = glyphFontRef.GetFont();
                if (glyphFont != null)
                {
                    Render2D.DrawText(glyphFont, _glyph, rect, InkWashTheme.TextDefault,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }
            }

            // 5. 快捷键标签（圆内下缘，金色；设计为圆下 -8px 外置，此处内聚呈现）
            if (!string.IsNullOrEmpty(_hotkey))
            {
                var fontRef = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 10f);
                var font = fontRef.GetFont();
                if (font != null)
                {
                    var hotkeyRect = new Rectangle(Width * 0.5f - 16f, Height - 18f, 32f, 14f);
                    Render2D.DrawText(font, _hotkey, hotkeyRect, InkWashTheme.GoldPrimary,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }
            }
        }

        /// <summary>用三角形扇填充一个圆形。</summary>
        private static void FillCircle(Float2 center, float radius, Color color)
        {
            if (radius <= 0f)
                return;
            const int segments = 32;
            var vertices = new Float2[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                float a1 = (i / (float)segments) * Mathf.TwoPi;
                float a2 = ((i + 1) / (float)segments) * Mathf.TwoPi;
                int idx = i * 3;
                vertices[idx] = center;
                vertices[idx + 1] = center + new Float2(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
                vertices[idx + 2] = center + new Float2(Mathf.Cos(a2) * radius, Mathf.Sin(a2) * radius);
            }
            Render2D.FillTriangles(vertices, color);
        }

        /// <summary>用线段近似绘制圆环描边。</summary>
        private static void DrawCircleRing(Float2 center, float radius, Color color, float thickness)
        {
            if (radius <= 0f)
                return;
            const int segments = 32;
            for (int i = 0; i < segments; i++)
            {
                float a1 = (i / (float)segments) * Mathf.TwoPi;
                float a2 = ((i + 1) / (float)segments) * Mathf.TwoPi;
                var p1 = center + new Float2(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
                var p2 = center + new Float2(Mathf.Cos(a2) * radius, Mathf.Sin(a2) * radius);
                Render2D.DrawLine(p1, p2, color, thickness);
            }
        }

        /// <summary>自顶部（12 点）顺时针填充扇形，用作冷却遮罩。</summary>
        private static void FillPie(Float2 center, float radius, float ratio, Color color)
        {
            if (ratio <= 0f || radius <= 0f)
                return;
            float sweep = Mathf.Clamp(ratio, 0f, 1f) * Mathf.TwoPi;
            const int maxSegments = 40;
            // 注意：Flax 的 Mathf 无 Ceiling/Max(int,int)，此处用纯整数运算（向上取整 + 保底两段）。
            int segments = (int)(sweep / Mathf.TwoPi * maxSegments) + 1;
            if (segments < 2)
                segments = 2;
            var vertices = new Float2[segments * 3];
            float start = -Mathf.Pi * 0.5f; // 屏幕坐标系：12 点 = -90°，顺时针为正（Y 向下）
            for (int i = 0; i < segments; i++)
            {
                float a1 = start + sweep * (i / (float)segments);
                float a2 = start + sweep * ((i + 1) / (float)segments);
                int idx = i * 3;
                vertices[idx] = center;
                vertices[idx + 1] = center + new Float2(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
                vertices[idx + 2] = center + new Float2(Mathf.Cos(a2) * radius, Mathf.Sin(a2) * radius);
            }
            Render2D.FillTriangles(vertices, color);
        }
    }

    // =======================================================================
    // SubTask 5.6 辅助控件：奇术槽
    // =======================================================================

    /// <summary>
    /// 奇术（终极）槽控件（对齐设计 .hud-skill-special 72px 圆形）：
    /// 圆形墨底 + 内圈金晕 + 2px 金色圆环边框 + 就绪脉冲环（外扩渐隐）+ 中心"奇"字。
    /// </summary>
    internal class QishuSlotControl : ContainerControl
    {
        /// <summary>脉冲周期（秒）</summary>
        private const float PulsePeriod = 1.6f;

        /// <summary>脉冲最小 alpha</summary>
        private const float PulseAlphaMin = 0.5f;

        /// <summary>脉冲最大 alpha</summary>
        private const float PulseAlphaMax = 1.0f;

        /// <summary>充能条高度</summary>
        private const float ChargeBarHeight = 3f;

        /// <summary>背景色</summary>
        private static readonly Color SlotBgColor = new Color(
            InkWashTheme.BaseTertiary.R,
            InkWashTheme.BaseTertiary.G,
            InkWashTheme.BaseTertiary.B,
            0.9f);

        /// <summary>累计动画时间</summary>
        private float _animTime;

        /// <summary>是否就绪（true 时显示脉冲动画）</summary>
        private bool _ready = true;

        /// <summary>充能进度（0-1，对齐 design ultimate 70%）</summary>
        private float _chargeProgress = 0.7f;

        /// <summary>
        /// 是否就绪。
        /// </summary>
        public bool Ready
        {
            get => _ready;
            set => _ready = value;
        }

        /// <summary>
        /// 充能进度（0.0~1.0），自动钳制。
        /// </summary>
        public float ChargeProgress
        {
            get => _chargeProgress;
            set => _chargeProgress = Mathf.Clamp(value, 0f, 1f);
        }

        /// <summary>
        /// 构造函数。
        /// </summary>
        public QishuSlotControl()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
        }

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (_ready)
                _animTime += deltaTime;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            var center = new Float2(Width * 0.5f, Height * 0.5f);
            float radius = Mathf.Min(Width, Height) * 0.5f;
            var rect = new Rectangle(0f, 0f, Width, Height);

            // 1. 圆形墨底 + 内圈金晕（近似设计 radial-gradient）
            FillCircle(center, radius, SlotBgColor);
            var goldTint = new Color(
                InkWashTheme.GoldPrimary.R,
                InkWashTheme.GoldPrimary.G,
                InkWashTheme.GoldPrimary.B,
                0.12f);
            FillCircle(center, radius * 0.78f, goldTint);

            // 2. 金色圆环边框（2px，对齐设计 .hud-skill-special）
            DrawCircleRing(center, radius, InkWashTheme.BorderGoldStrong, 2f);

            // 3. 就绪脉冲环（对齐设计 .hud-skill-ready readyPulse：外扩渐隐）
            if (_ready)
            {
                float phase = (_animTime / PulsePeriod) % 1f;
                float ringRadius = radius + 2f + phase * 10f;
                float alpha = (1f - phase) * 0.6f;
                var ringColor = new Color(
                    InkWashTheme.GoldPrimary.R,
                    InkWashTheme.GoldPrimary.G,
                    InkWashTheme.GoldPrimary.B,
                    InkWashTheme.GoldPrimary.A * alpha);
                DrawCircleRing(center, ringRadius, ringColor, 1f);
            }

            // 4. 中心"奇"字（金色）
            var fontRef = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 24f);
            var font = fontRef.GetFont();
            if (font != null)
            {
                Render2D.DrawText(font, "奇", rect, InkWashTheme.TextBrand,
                    TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>用三角形扇填充一个圆形。</summary>
        private static void FillCircle(Float2 center, float radius, Color color)
        {
            if (radius <= 0f)
                return;
            const int segments = 32;
            var vertices = new Float2[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                float a1 = (i / (float)segments) * Mathf.TwoPi;
                float a2 = ((i + 1) / (float)segments) * Mathf.TwoPi;
                int idx = i * 3;
                vertices[idx] = center;
                vertices[idx + 1] = center + new Float2(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
                vertices[idx + 2] = center + new Float2(Mathf.Cos(a2) * radius, Mathf.Sin(a2) * radius);
            }
            Render2D.FillTriangles(vertices, color);
        }

        /// <summary>用线段近似绘制圆环描边。</summary>
        private static void DrawCircleRing(Float2 center, float radius, Color color, float thickness)
        {
            if (radius <= 0f)
                return;
            const int segments = 32;
            for (int i = 0; i < segments; i++)
            {
                float a1 = (i / (float)segments) * Mathf.TwoPi;
                float a2 = ((i + 1) / (float)segments) * Mathf.TwoPi;
                var p1 = center + new Float2(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
                var p2 = center + new Float2(Mathf.Cos(a2) * radius, Mathf.Sin(a2) * radius);
                Render2D.DrawLine(p1, p2, color, thickness);
            }
        }
    }

    // =======================================================================
    // SubTask 5.9 辅助控件：水墨方条导航
    // =======================================================================

    /// <summary>
    /// 方条型导航控件。
    /// 顶部中央水平条，显示玩家前方 180° 范围内的方位、刻度和目标指示。
    /// 包含东南西北中文标记、刻度线、玩家朝向三角指针和目标菱形标记+距离。
    /// 风格对齐水墨主题：墨色半透明底 + 金线边框 + 鎏金/朱红主色。
    /// </summary>
    internal class InkNavigationStrip : ContainerControl
    {
        /// <summary>视野范围（度），左右各 90°</summary>
        private const float FovDegrees = 180f;

        /// <summary>小刻度间隔（度）</summary>
        private const float TickMinorSpacing = 15f;

        /// <summary>中刻度间隔（度），带稍长线</summary>
        private const float TickMidSpacing = 45f;

        /// <summary>大刻度间隔（度），带方位标签</summary>
        private const float TickMajorSpacing = 90f;

        /// <summary>小刻度线高</summary>
        private const float TickMinorHeight = 6f;

        /// <summary>中刻度线高</summary>
        private const float TickMidHeight = 10f;

        /// <summary>大刻度线高</summary>
        private const float TickMajorHeight = 14f;

        /// <summary>导航条背景色（BaseTertiary 半透明）</summary>
        private static readonly Color StripBgColor = new Color(
            InkWashTheme.BaseTertiary.R,
            InkWashTheme.BaseTertiary.G,
            InkWashTheme.BaseTertiary.B,
            0.75f);

        /// <summary>方位角度 → 中文字典</summary>
        private static readonly (string label, float angle)[] Directions = new[]
        {
            ("北", 0f),
            ("东", 90f),
            ("南", 180f),
            ("西", 270f),
        };

        /// <summary>玩家偏航角（度），0 = 正北，顺时针增加</summary>
        public float PlayerYaw { get; set; } = 0f;

        /// <summary>目标方向（度），0 = 正北，顺时针增加</summary>
        public float TargetYaw { get; set; } = 0f;

        /// <summary>目标距离（米）</summary>
        public int TargetDistance { get; set; } = 0;

        /// <summary>是否有活跃目标</summary>
        public bool HasTarget { get; set; } = false;

        /// <summary>
        /// 构造函数。
        /// </summary>
        public InkNavigationStrip()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            // 1. 背景填充
            var bgRect = new Rectangle(0f, 0f, Width, Height);
            Render2D.FillRectangle(bgRect, StripBgColor);

            // 2. 上下金线边框
            Render2D.DrawLine(new Float2(0f, 0f), new Float2(Width, 0f),
                InkWashTheme.BorderGold, 1f);
            Render2D.DrawLine(new Float2(0f, Height), new Float2(Width, Height),
                InkWashTheme.BorderGold, 1f);

            // 3. 刻度线
            DrawTicks();

            // 4. 方位标签
            DrawDirectionLabels();

            // 5. 玩家朝向三角指针（居中，从顶部向下指）
            float cx = Width * 0.5f;
            var triTip = new Float2(cx, Height * 0.35f);
            var triB1 = new Float2(cx - 5f, 2f);
            var triB2 = new Float2(cx + 5f, 2f);
            Render2D.FillTriangles(new[] { triTip, triB1, triB2 },
                InkWashTheme.GoldPrimary);
            // 三角下方小圆点
            FillCircle(new Float2(cx, Height * 0.55f), 2f,
                InkWashTheme.GoldPrimary);

            // 6. 目标标记
            if (HasTarget)
            {
                DrawTargetMarker();
            }
        }

        /// <summary>
        /// 将世界角度转换为导航条上的 X 坐标。
        /// 条左侧 = 玩家左方（相对角 -90°），条右侧 = 玩家右方（相对角 +90°），
        /// 条中央 = 玩家正前方（相对角 0°）。
        /// </summary>
        private float AngleToStripX(float worldAngle)
        {
            float rel = ((worldAngle - PlayerYaw) % 360f + 360f) % 360f;
            if (rel > 180f) rel -= 360f;
            float x = (rel + FovDegrees * 0.5f) / FovDegrees * Width;
            return Mathf.Clamp(x, 0f, Width);
        }

        /// <summary>
        /// 判断世界角度是否在当前视野范围内。
        /// </summary>
        private bool IsAngleVisible(float worldAngle)
        {
            float rel = ((worldAngle - PlayerYaw) % 360f + 360f) % 360f;
            if (rel > 180f) rel -= 360f;
            return rel >= -FovDegrees * 0.5f && rel <= FovDegrees * 0.5f;
        }

        /// <summary>
        /// 绘制刻度线。
        /// 小刻度 15° 间隔 6px 高，中刻度 45° 间隔 10px 高，大刻度 90° 间隔 14px 高。
        /// </summary>
        private void DrawTicks()
        {
            for (float angle = 0f; angle < 360f; angle += TickMinorSpacing)
            {
                if (!IsAngleVisible(angle)) continue;

                float x = AngleToStripX(angle);
                bool isMajor = Mathf.Abs(angle % TickMajorSpacing) < 0.1f;
                bool isMid = Mathf.Abs(angle % TickMidSpacing) < 0.1f && !isMajor;

                float tickH = isMajor ? TickMajorHeight
                         : isMid ? TickMidHeight
                         : TickMinorHeight;

                float y0 = Height - tickH;
                Color tickColor = isMajor
                    ? InkWashTheme.BorderGold
                    : InkWashTheme.BorderNeutralL3;

                Render2D.DrawLine(
                    new Float2(x, y0),
                    new Float2(x, Height),
                    tickColor, isMajor ? 1.5f : 1f);
            }
        }

        /// <summary>
        /// 绘制东南西北中文方位标签。
        /// 北使用鎏金亮色突出，其他使用纸色淡显。
        /// 标签矩形放大确保字型完整显示，并对左右边缘做裁剪保护。
        /// </summary>
        private void DrawDirectionLabels()
        {
            var headingFontAsset = InkWashTheme.GetFont(InkWashTheme.FontRole.Heading);
            if (headingFontAsset == null) return;
            var headingFontRef = new FontReference(headingFontAsset, 12f);
            var font = headingFontRef.GetFont();
            if (font == null) return;

            float labelSize = 18f;
            for (int i = 0; i < Directions.Length; i++)
            {
                var (label, angle) = Directions[i];
                if (!IsAngleVisible(angle)) continue;

                float x = AngleToStripX(angle);
                bool isPrimary = label == "北";
                Color color = isPrimary
                    ? InkWashTheme.GoldBright
                    : InkWashTheme.PaperFaded;

                float rx = Mathf.Clamp(x - labelSize * 0.5f, 0f, Width - labelSize);
                var rect = new Rectangle(
                    rx,
                    0f,
                    labelSize, labelSize);
                Render2D.DrawText(font, label, rect, color,
                    TextAlignment.Center, TextAlignment.Center,
                    TextWrapping.NoWrap);
            }
        }

        /// <summary>
        /// 绘制目标菱形标记 + 距离文本。
        /// 菱形使用朱红亮色，距离文本使用 Number 字体。
        /// </summary>
        private void DrawTargetMarker()
        {
            if (!IsAngleVisible(TargetYaw)) return;

            float tx = AngleToStripX(TargetYaw);
            float diamondSize = 5f;
            float cy = Height * 0.6f;

            // 菱形顶点：2个三角形（上-右-下、上-下-左）
            var diamond = new Float2[]
            {
                new Float2(tx, cy - diamondSize),
                new Float2(tx + diamondSize * 0.7f, cy),
                new Float2(tx, cy + diamondSize),
                new Float2(tx, cy - diamondSize),
                new Float2(tx, cy + diamondSize),
                new Float2(tx - diamondSize * 0.7f, cy),
            };
            Render2D.FillTriangles(diamond, InkWashTheme.VermilionBright);

            // 距离文本
            var numFontAsset = InkWashTheme.GetFont(InkWashTheme.FontRole.Number);
            if (numFontAsset == null) return;
            var numFontRef = new FontReference(numFontAsset, 10f);
            var numFont = numFontRef.GetFont();
            if (numFont != null)
            {
                string distText = $"{TargetDistance}m";
                float textW = 40f;
                float textH = 12f;
                var textRect = new Rectangle(
                    tx - textW * 0.5f,
                    Height - 13f,
                    textW, textH);
                Render2D.DrawText(numFont, distText, textRect,
                    InkWashTheme.VermilionBright,
                    TextAlignment.Center, TextAlignment.Center,
                    TextWrapping.NoWrap);
            }
        }

        /// <summary>
        /// 使用三角形扇填充一个圆形。
        /// </summary>
        private static void FillCircle(Float2 center, float radius, Color color)
        {
            if (radius <= 0f) return;

            const int segs = 16;
            var vertices = new Float2[segs * 3];
            for (int i = 0; i < segs; i++)
            {
                float a1 = (i / (float)segs) * Mathf.TwoPi;
                float a2 = ((i + 1) / (float)segs) * Mathf.TwoPi;
                int idx = i * 3;
                vertices[idx] = center;
                vertices[idx + 1] = center + new Float2(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
                vertices[idx + 2] = center + new Float2(Mathf.Cos(a2) * radius, Mathf.Sin(a2) * radius);
            }
            Render2D.FillTriangles(vertices, color);
        }
    }
}
