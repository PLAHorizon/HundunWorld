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
    public class CombatHudPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>中央引导按钮尺寸（正方形），平铺于屏幕中央</summary>
        private const float GuideSize = 64f;

        /// <summary>顶部任务条距离屏幕顶部的边距</summary>
        private const float TopMargin = 20f;

        /// <summary>顶部导航条尺寸</summary>
        private static readonly Float2 NavStripSize = new Float2(440f, 30f);

        /// <summary>右上角小地图尺寸（正方形），对齐 design combat-hud.html 160x160</summary>
        private const float MinimapSize = 160f;

        /// <summary>左上角角色信息面板尺寸（对齐 boss 目标面板紧凑风格）</summary>
        private static readonly Float2 LeftBottomSize = new Float2(360f, 130f);

        /// <summary>右下角容器尺寸（容纳 5 个技能槽 + 间隔 + 1 个奇术槽）</summary>
        private static readonly Float2 RightBottomSize = new Float2(420f, 84f);

        /// <summary>buff/debuff 图标条尺寸（对齐 design 横向图标条，位于左上角角色面板下方）</summary>
        private static readonly Float2 BuffBarSize = new Float2(360f, 42f);

        /// <summary>系统主导航栏尺寸（第一行：9 个主导航按钮 + 分隔符 + 传统模式切换）</summary>
        private static readonly Float2 SysNavSize = new Float2(760f, 36f);

        /// <summary>系统扩展导航栏尺寸（第二行：9 个扩展导航按钮）</summary>
        private static readonly Float2 SysNavExtendedSize = new Float2(760f, 36f);

        /// <summary>主导航栏与扩展导航栏的垂直间距</summary>
        private const float SysNavRowGap = 4f;

        /// <summary>传统模式切换按钮的金色强调背景（rgba(200,168,88,0.12)）</summary>
        private static readonly Color TraditionalToggleBg = new Color(
            InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
            InkWashTheme.GoldPrimary.B, 0.12f);

        /// <summary>主导航栏分隔符宽度（竖线 + 两侧 margin）</summary>
        private const float SysNavDividerWidth = 8f;

        /// <summary>技能槽尺寸（正方形）</summary>
        private const float SkillSlotSize = 58f;

        /// <summary>技能槽间距</summary>
        private const float SkillSlotGap = 10f;

        /// <summary>奇术槽尺寸（正方形，大于技能槽）</summary>
        private const float QishuSlotSize = 76f;

        /// <summary>奇术槽与最后一个技能槽的间距</summary>
        private const float QishuSlotGap = 18f;

        /// <summary>buff cell 尺寸（正方形）</summary>
        private const float BuffCellSize = 34f;

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

        // 顶部中央导航条（方位 + 目标距离）
        private InkNavigationStrip _navStrip;

        // SubTask 5.4 水墨小地图（带地形快照、NPC、玩家图标）
        private InkMinimap _minimap;

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
        /// </summary>
        public float MinimapPlayerYaw
        {
            get => _minimap?.PlayerYaw ?? 0f;
            set
            {
                if (_minimap != null)
                    _minimap.PlayerYaw = value;
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
                BuildGuideButton();
                BuildNavigationStrip();
                BuildMinimap();
                BuildLeftBottom();
                BuildRightBottom();
                BuildBuffBar();
                BuildSystemNav();

                // 美化扩展：对齐 combat-hud-v2.html 设计补全目标/队伍/连击/道具 4 个区域
                BuildTargetInfo();
                BuildPartySection();
                BuildComboCounter();
                BuildItemBar();

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
            // 三个 splash 显式降低不透明度（0.15f），使装饰更轻盈，不依赖 InkSplash 默认值
            _splash1 = new InkSplash
            {
                Variant = InkSplashVariant.Normal,
                Opacity = 0.15f,
                AutoFocus = false,
            };
            _splash2 = new InkSplash
            {
                Variant = InkSplashVariant.Vermilion,
                Opacity = 0.15f,
                AutoFocus = false,
            };
            _splash3 = new InkSplash
            {
                Variant = InkSplashVariant.Elevated,
                Opacity = 0.15f,
                AutoFocus = false,
            };
            AddChild(_splash1);
            AddChild(_splash2);
            AddChild(_splash3);
        }

        /// <summary>
        /// SubTask 5.2：屏幕中央引导/目标按钮。
        /// 采用平铺于屏幕中央的设计，尺寸较大（64x64），文本"引"用于显示当前目标/引导提示。
        /// 点击时记录日志"引导功能待落地"。
        /// </summary>
        private void BuildGuideButton()
        {
            _guideButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Lg,
                Text = "引",
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(GuideSize, GuideSize),
            };
            _guideButton.ButtonClicked += OnGuideButtonClicked;
            AddChild(_guideButton);
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
        /// SubTask 5.4：右上角水墨小地图。
        /// 替换原指南针，使用 <see cref="InkMinimap"/> 展示地形快照、NPC、玩家图标与方向。
        /// 初始化时填充 mock 地形/实体数据，实际运行时可由外部刷新。
        /// </summary>
        private void BuildMinimap()
        {
            _minimap = new InkMinimap
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(MinimapSize, MinimapSize),
                PlayerYaw = 0f,
            };

            // 模拟地形快照：水域（青黛）、山地（古铜）、林地（翡翠）
            _minimap.AddLandmark(-0.35f, -0.42f, 0.22f, new Color(70f / 255f, 110f / 255f, 120f / 255f, 0.45f)); // 水域
            _minimap.AddLandmark(0.45f, -0.20f, 0.18f, new Color(120f / 255f, 90f / 255f, 60f / 255f, 0.40f));  // 山地
            _minimap.AddLandmark(-0.20f, 0.38f, 0.28f, new Color(60f / 255f, 130f / 255f, 90f / 255f, 0.35f));  // 林地
            _minimap.AddLandmark(0.30f, 0.45f, 0.15f, new Color(160f / 255f, 140f / 255f, 90f / 255f, 0.38f));  // 建筑群

            // 模拟实体点位：玩家（中心）+ 友方 + 敌方 + NPC
            _minimap.AddEntity(InkMinimapEntityType.Player, 0f, 0f);
            _minimap.AddEntity(InkMinimapEntityType.Friendly, -0.30f, 0.25f);
            _minimap.AddEntity(InkMinimapEntityType.Friendly, 0.15f, -0.35f);
            _minimap.AddEntity(InkMinimapEntityType.Enemy, 0.50f, 0.10f);
            _minimap.AddEntity(InkMinimapEntityType.Enemy, -0.40f, -0.30f);
            _minimap.AddEntity(InkMinimapEntityType.NPC, 0.25f, 0.35f);

            AddChild(_minimap);

            // 小地图下方坐标标签（对齐设计 .minimap-coord，显示当前位置名）
            // 使用 Display 字体字号 11，字色 PaperAged，与设计 HTML 一致
            _minimapCoordLabel = new Label
            {
                Text = _locationName,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 11f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(MinimapSize, MinimapCoordHeight),
                BackgroundColor = Color.Transparent,
            };
            AddChild(_minimapCoordLabel);
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
        ///   <item>角色与背包：角色(nav-character-panel) / 武学(nav-skill-panel) / 背包(nav-inventory)</item>
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
            // ========== 第一行：主导航栏 ==========
            _sysNav = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = SysNavSize,
            };

            // 主导航按钮：标签 + dom-id + 是否传统模式切换（特殊样式）
            var mainEntries = new[]
            {
                (label: "角色", domId: InkPageDomIds.NavCharacterPanel),
                (label: "武学", domId: InkPageDomIds.NavSkillPanel),
                (label: "背包", domId: InkPageDomIds.NavInventory),
                (label: "任务", domId: InkPageDomIds.NavQuests),
                (label: "地图", domId: InkPageDomIds.NavWorldMap),
                (label: "社交", domId: InkPageDomIds.NavFriends),
                (label: "商城", domId: InkPageDomIds.NavShop),
                (label: "罗盘", domId: InkPageDomIds.NavCompass),
            };

            // 主导航按钮宽度：8 个按钮 + 1 个分隔符 + 1 个切换按钮 + 9 个间距
            // 总宽 760，分隔符 8px，切换按钮略宽 80px，其余 8 个按钮均分剩余
            float mainBtnGap = SysNavBtnGap;
            float toggleBtnWidth = 80f;
            float dividerWidth = SysNavDividerWidth;
            // 8 个主按钮宽度 = (760 - 9*gap - divider - toggle) / 8
            float mainBtnWidth = (SysNavSize.X - mainBtnGap * 9 - dividerWidth - toggleBtnWidth) / 8f;
            float mainBtnHeight = SysNavSize.Y - 4f;
            float mainBtnY = (SysNavSize.Y - mainBtnHeight) * 0.5f;

            float cursorX = 0f;
            for (int i = 0; i < mainEntries.Length; i++)
            {
                var entry = mainEntries[i];
                var btn = new InkButton
                {
                    Variant = InkButtonVariant.Default,
                    ButtonSize = InkButtonSize.Md,
                    Text = entry.label,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(cursorX, mainBtnY),
                    Size = new Float2(mainBtnWidth, mainBtnHeight),
                };

                string domId = entry.domId;
                btn.ButtonClicked += (b) => OnSystemNavButtonClicked(domId, b);

                _sysNav.AddChild(btn);
                cursorX += mainBtnWidth + mainBtnGap;
            }

            // 分隔符：竖线（用窄长方形 InkPanel 模拟）
            var divider = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cursorX, (SysNavSize.Y - 20f) * 0.5f),
                Size = new Float2(1f, 20f),
                BackgroundColor = InkWashTheme.BorderNeutralL3,
            };
            _sysNav.AddChild(divider);
            cursorX += dividerWidth + mainBtnGap;

            // 传统模式切换按钮：金色强调背景
            var toggleBtn = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "传统模式",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cursorX, mainBtnY),
                Size = new Float2(toggleBtnWidth, mainBtnHeight),
                BackgroundColor = TraditionalToggleBg,
            };
            toggleBtn.ButtonClicked += (b) => OnSystemNavButtonClicked(InkPageDomIds.CombatHudTraditional, b);
            _sysNav.AddChild(toggleBtn);

            AddChild(_sysNav);

            // ========== 第二行：扩展导航栏 ==========
            _sysNavExtended = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = SysNavExtendedSize,
            };

            var extendedEntries = new[]
            {
                (label: "强化", domId: InkPageDomIds.NavEquipmentEnhance),
                (label: "制造", domId: InkPageDomIds.NavCrafting),
                (label: "坐骑", domId: InkPageDomIds.NavMountPet),
                (label: "好友", domId: InkPageDomIds.NavFriends),
                (label: "邮件", domId: InkPageDomIds.NavMail),
                (label: "排行", domId: InkPageDomIds.NavLeaderboard),
                (label: "师徒", domId: InkPageDomIds.NavMentor),
                (label: "成就", domId: InkPageDomIds.NavAchievement),
                (label: "副本", domId: InkPageDomIds.NavDungeonEntry),
            };

            // 扩展导航按钮宽度：9 个按钮 + 8 个间距
            float extBtnWidth = (SysNavExtendedSize.X - SysNavBtnGap * (extendedEntries.Length - 1)) / extendedEntries.Length;
            float extBtnHeight = SysNavExtendedSize.Y - 4f;
            float extBtnY = (SysNavExtendedSize.Y - extBtnHeight) * 0.5f;

            float cursorX2 = 0f;
            for (int i = 0; i < extendedEntries.Length; i++)
            {
                var entry = extendedEntries[i];
                var btn = new InkButton
                {
                    Variant = InkButtonVariant.Default,
                    ButtonSize = InkButtonSize.Md,
                    Text = entry.label,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(cursorX2, extBtnY),
                    Size = new Float2(extBtnWidth, extBtnHeight),
                };

                string domId = entry.domId;
                btn.ButtonClicked += (b) => OnSystemNavButtonClicked(domId, b);

                _sysNavExtended.AddChild(btn);
                cursorX2 += extBtnWidth + SysNavBtnGap;
            }

            AddChild(_sysNavExtended);
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

            const float screenEdge = 24f;   // 屏幕边缘统一边距
            const float bottomSafe = 96f;   // 底部安全区高度（容纳导航栏 + buff 条）

            // SubTask 5.1 水墨晕染装饰：分散在屏幕四角与中右
            // splash1（Normal 300x300）：左上角，部分溢出
            if (_splash1 != null)
            {
                _splash1.Location = new Float2(-60f, -80f);
            }
            // splash2（Vermilion 250x250）：右下角
            if (_splash2 != null)
            {
                _splash2.Location = new Float2(sw - 210f, sh - 210f);
            }
            // splash3（Elevated 200x200）：中右偏下
            if (_splash3 != null)
            {
                _splash3.Location = new Float2(sw * 0.72f, sh * 0.58f);
            }

            // SubTask 5.2 引导按钮：平铺于屏幕中央，尺寸较大
            if (_guideButton != null)
            {
                _guideButton.Location = new Float2(sw * 0.5f - GuideSize * 0.5f, sh * 0.5f - GuideSize * 0.5f);
            }

            // 导航条：顶部居中，距离顶部 28px
            if (_navStrip != null)
            {
                _navStrip.Location = new Float2(sw * 0.5f - NavStripSize.X * 0.5f, 28f);
            }

            // SubTask 5.4 小地图：右上角，右侧边距 24px，顶部 24px
            if (_minimap != null)
            {
                _minimap.Location = new Float2(sw - MinimapSize - screenEdge, screenEdge);
            }

            // 小地图下方坐标标签：紧贴小地图底部，居中对齐（对齐设计 .minimap-coord）
            if (_minimapCoordLabel != null)
            {
                _minimapCoordLabel.Location = new Float2(
                    sw - MinimapSize - screenEdge,
                    screenEdge + MinimapSize + MinimapCoordGap);
            }

            // SubTask 5.5 左上角角色面板：左侧边距 24px，顶部对齐（对齐 design top:16px left:16px）
            if (_leftBottom != null)
            {
                _leftBottom.Location = new Float2(screenEdge, screenEdge + 4f);
            }

            // SubTask 5.6 右下角容器：右侧贴边，底部对齐安全区
            if (_rightBottom != null)
            {
                _rightBottom.Location = new Float2(sw - RightBottomSize.X - screenEdge, sh - bottomSafe - RightBottomSize.Y + 10f);
            }

            // SubTask 5.7 buff 图标条：左上角角色面板下方，横向排列
            if (_buffBar != null)
            {
                _buffBar.Location = new Float2(screenEdge, screenEdge + 4f + LeftBottomSize.Y + 6f);
            }

            // SubTask 5.8 系统导航栏（双行）：底部居中，紧贴屏幕底部
            // 第一行（主导航）位于最底部，第二行（扩展导航）位于其上方
            // 布局自下而上：扩展行 y = sh - 50 - SysNavExtendedSize.Y - SysNavRowGap
            //              主行 y = sh - 50
            if (_sysNav != null)
            {
                _sysNav.Location = new Float2(sw * 0.5f - SysNavSize.X * 0.5f, sh - 50f);
            }
            if (_sysNavExtended != null)
            {
                _sysNavExtended.Location = new Float2(
                    sw * 0.5f - SysNavExtendedSize.X * 0.5f,
                    sh - 50f - SysNavExtendedSize.Y - SysNavRowGap);
            }

            // ---------- 美化扩展定位（对齐 combat-hud-v2.html 设计） ----------

            // 目标信息面板：顶部居中，紧贴任务提示条下方
            // 任务条 y=28 高 36，目标面板 y=72 起，避免遮挡
            if (_targetFrame != null)
            {
                _targetFrame.Location = new Float2(sw * 0.5f - TargetFrameSize.X * 0.5f, 72f);
            }

            // 队伍成员状态卡：右上角，紧贴小地图坐标标签下方
            // 小地图 y=24 高 140 + 坐标标签 y=168 高 16 + 间距 6 = 队伍容器 y=190 起
            if (_partyContainer != null)
            {
                _partyContainer.Location = new Float2(
                    sw - PartySectionWidth - screenEdge,
                    screenEdge + MinimapSize + MinimapCoordGap + MinimapCoordHeight + 6f);
            }

            // 连击计数器：左侧中部，避开左下角玩家面板与左上角水墨晕染
            // 垂直居中略偏上，x 距屏幕左缘 30px
            if (_comboCounter != null)
            {
                _comboCounter.Location = new Float2(screenEdge + 30f, sh * 0.5f - ComboCounterSize.Y * 0.5f - 40f);
            }

            // 道具栏：右下角，水平 10 格，位于技能槽上方
            if (_itemBar != null)
            {
                float barW = ItemSlotCount * ItemCellSize + (ItemSlotCount - 1) * ItemCellGap;
                float skillTop = sh - bottomSafe - RightBottomSize.Y + 10f;
                _itemBar.Location = new Float2(sw - barW - screenEdge, skillTop - 16f - ItemCellSize);
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
            EmitGoldAtButton(button);
            FlaxEngine.Debug.Log("[CombatHudPage] 引导功能待落地");
        }

        /// <summary>
        /// 头像按钮点击处理：触发 <see cref="NavigationRequested"/>("nav-character-v2")，
        /// 由 <see cref="InkPageRouter"/> 订阅后跳转角色属性页 V2。
        /// 同时在按钮中心位置触发金粉爆发反馈。
        /// </summary>
        /// <param name="button">触发事件的按钮</param>
        private void OnAvatarButtonClicked(Button button)
        {
            try
            {
                EmitGoldAtButton(button);
                NavigationRequested?.Invoke("nav-character-v2");
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

            // 3. 内圈装饰线（弱金）
            DrawCircleRing(center, radius - 4f, InkWashTheme.BorderNeutralL3, 1f);

            // 4. 四方位字"东南西北"，字号 12，字色 TextBrand
            var fontRef = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 12f);
            float labelOffset = radius - 12f;
            DrawDirectionLabel(fontRef, center, new Float2(0f, -labelOffset), "北", InkWashTheme.TextBrand);
            DrawDirectionLabel(fontRef, center, new Float2(0f, labelOffset), "南", InkWashTheme.TextBrand);
            DrawDirectionLabel(fontRef, center, new Float2(labelOffset, 0f), "东", InkWashTheme.TextBrand);
            DrawDirectionLabel(fontRef, center, new Float2(-labelOffset, 0f), "西", InkWashTheme.TextBrand);

            // 5. 朱红指针：从中心向"北"方向绘制三角形
            // 实际角度 = Yaw + 摆动偏移（正弦波 ±5°）
            float sway = Mathf.Sin(_animTime * (Mathf.TwoPi / SwayPeriod)) * SwayAmplitude;
            float angleDeg = _yaw + sway;
            float angleRad = Mathf.DegreesToRadians * angleDeg;
            DrawNeedle(center, radius, angleRad);
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
        /// 绘制朱红指针三角形。
        /// 指针从中心出发，沿 <paramref name="angleRad"/> 方向延伸
        /// <see cref="NeedleLengthRatio"/>*<paramref name="radius"/> 长度。
        /// </summary>
        /// <param name="center">指针基点（指南针中心）</param>
        /// <param name="radius">指南针半径</param>
        /// <param name="angleRad">指针角度（弧度，0 = 正北，顺时针）</param>
        private void DrawNeedle(Float2 center, float radius, float angleRad)
        {
            // 在屏幕坐标系中，角度 0 = 正北（向上），顺时针增加
            // 方向向量：(sin(a), -cos(a))
            float dirX = Mathf.Sin(angleRad);
            float dirY = -Mathf.Cos(angleRad);

            // 指针尖端
            float needleLen = radius * NeedleLengthRatio;
            var tip = center + new Float2(dirX * needleLen, dirY * needleLen);

            // 指针底部两侧（垂直于方向）
            float baseHalf = radius * NeedleBaseRatio;
            float perpX = dirY;
            float perpY = -dirX;
            var base1 = center + new Float2(perpX * baseHalf, perpY * baseHalf);
            var base2 = center - new Float2(perpX * baseHalf, perpY * baseHalf);

            // 朱红三角形：使用 FillTriangles 绘制
            // Render2D.FillTriangles 接收 Float2[] 顶点数组（每 3 个为一组三角形）
            var vertices = new Float2[3];
            vertices[0] = tip;
            vertices[1] = base1;
            vertices[2] = base2;
            Render2D.FillTriangles(vertices, InkWashTheme.VermilionPrimary);

            // 中心金圆点
            FillCircle(center, 3f, InkWashTheme.GoldPrimary);
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
    /// 方形技能槽控件。
    /// 方形墨色背景 + 金线边框（对齐 design .hud-skill-slot 48x48）+
    /// 中央字形 + 冷却遮罩（黑色半透明从右侧覆盖）+
    /// 右下角快捷键标签。
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

            var rect = new Rectangle(0f, 0f, Width, Height);

            // 1. 方形墨色背景
            Render2D.FillRectangle(rect, SlotBgColor);

            // 2. 金线边框（1px）
            float b = 1f;
            Color bColor = InkWashTheme.BorderGold;
            Render2D.DrawLine(new Float2(0f, 0f), new Float2(Width, 0f), bColor, b);
            Render2D.DrawLine(new Float2(Width, 0f), new Float2(Width, Height), bColor, b);
            Render2D.DrawLine(new Float2(Width, Height), new Float2(0f, Height), bColor, b);
            Render2D.DrawLine(new Float2(0f, Height), new Float2(0f, 0f), bColor, b);

            // 3. 中央字形
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

            // 4. 冷却遮罩（从右侧向左覆盖 cooldown 比例）
            if (_cooldown > 0f)
            {
                float fillW = Width * _cooldown;
                var overlayRect = new Rectangle(Width - fillW, 0f, fillW, Height);
                Render2D.FillRectangle(overlayRect, CooldownMaskColor);
            }

            // 5. 快捷键标签（右下角）
            if (!string.IsNullOrEmpty(_hotkey))
            {
                var fontRef = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 9f);
                var font = fontRef.GetFont();
                if (font != null)
                {
                    var hotkeyRect = new Rectangle(
                        Width - 14f, Height - 12f,
                        12f, 10f);
                    Render2D.DrawText(font, _hotkey, hotkeyRect, InkWashTheme.TextTertiary,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }
            }
        }
    }

    // =======================================================================
    // SubTask 5.6 辅助控件：奇术槽
    // =======================================================================

    /// <summary>
    /// 奇术（终极）槽控件。
    /// 方形墨色背景 + 强金边框（2px，对齐 design .hud-ultimate）+
    /// 脉冲动画（就绪时 alpha 0.5-1.0）+
    /// 底部充能进度条 + 中心"奇"字。
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

            var rect = new Rectangle(0f, 0f, Width, Height);

            // 1. 方形墨色背景
            Render2D.FillRectangle(rect, SlotBgColor);

            // 微弱金色辉光
            var goldTint = new Color(
                InkWashTheme.GoldPrimary.R,
                InkWashTheme.GoldPrimary.G,
                InkWashTheme.GoldPrimary.B,
                0.10f);
            var innerRect = new Rectangle(4f, 4f, Width - 8f, Height - 8f);
            Render2D.FillRectangle(innerRect, goldTint);

            // 2. 金色边框（脉冲 2px）
            Color borderColor = InkWashTheme.BorderGoldStrong;
            if (_ready)
            {
                float t = (_animTime / PulsePeriod) * Mathf.TwoPi;
                float alpha = Mathf.Lerp(PulseAlphaMin, PulseAlphaMax,
                    (Mathf.Sin(t) + 1f) * 0.5f);
                borderColor = new Color(
                    InkWashTheme.BorderGoldStrong.R,
                    InkWashTheme.BorderGoldStrong.G,
                    InkWashTheme.BorderGoldStrong.B,
                    InkWashTheme.BorderGoldStrong.A * alpha);
            }

            float b = 2f;
            Render2D.DrawLine(new Float2(0f, 0f), new Float2(Width, 0f), borderColor, b);
            Render2D.DrawLine(new Float2(Width, 0f), new Float2(Width, Height), borderColor, b);
            Render2D.DrawLine(new Float2(Width, Height), new Float2(0f, Height), borderColor, b);
            Render2D.DrawLine(new Float2(0f, Height), new Float2(0f, 0f), borderColor, b);

            // 3. 中心"奇"字（金色）
            var fontRef = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 22f);
            var font = fontRef.GetFont();
            if (font != null)
            {
                Render2D.DrawText(font, "奇", rect, InkWashTheme.TextBrand,
                    TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            // 4. 底部充能进度条（对齐 design .hud-ultimate charge bar）
            float chargeBarY = Height - ChargeBarHeight;
            var chargeBg = new Rectangle(0f, chargeBarY, Width, ChargeBarHeight);
            Render2D.FillRectangle(chargeBg, new Color(0f, 0f, 0f, 0.6f));

            if (_chargeProgress > 0f)
            {
                float fillW = Width * _chargeProgress;
                var chargeFill = new Rectangle(0f, chargeBarY, fillW, ChargeBarHeight);
                Render2D.FillRectangle(chargeFill, InkWashTheme.GoldBright);
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
