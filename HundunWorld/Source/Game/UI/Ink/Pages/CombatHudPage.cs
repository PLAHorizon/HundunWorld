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
    ///   <item>SubTask 5.5 左下角头像 + 竖排角色名 + 气血/体魄条</item>
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

        /// <summary>顶部任务条尺寸</summary>
        private static readonly Float2 QuestBarSize = new Float2(400f, 36f);

        /// <summary>右上角小地图尺寸（正方形），较原先指南针更大以展示地形与实体</summary>
        private const float MinimapSize = 140f;

        /// <summary>左下角容器尺寸</summary>
        private static readonly Float2 LeftBottomSize = new Float2(300f, 150f);

        /// <summary>右下角容器尺寸（容纳 5 个技能槽 + 间隔 + 1 个奇术槽）</summary>
        private static readonly Float2 RightBottomSize = new Float2(420f, 84f);

        /// <summary>buff/debuff 图标条尺寸</summary>
        private static readonly Float2 BuffBarSize = new Float2(360f, 42f);

        /// <summary>系统导航栏尺寸</summary>
        private static readonly Float2 SysNavSize = new Float2(640f, 40f);

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

        // ===================================================================
        // 子控件引用
        // =======================================================================

        // SubTask 5.1 水墨晕染装饰
        private InkSplash _splash1;
        private InkSplash _splash2;
        private InkSplash _splash3;

        // SubTask 5.2 引导按钮
        private InkButton _guideButton;

        // SubTask 5.3 任务提示条
        private InkPanel _questBar;
        private Label _questLabel;

        // SubTask 5.4 水墨小地图（带地形快照、NPC、玩家图标）
        private InkMinimap _minimap;

        // SubTask 5.5 左下角头像 + 竖排角色名 + 气血/体魄
        private ContainerControl _leftBottom;
        private InkButton _avatarButton;
        private InkVerticalTitle _characterName;
        private InkBar _hpBar;
        private Label _hpLabel;
        private InkBar _staminaBar;
        private Label _staminaLabel;

        // SubTask 5.6 右下角技能槽 + 奇术槽
        private ContainerControl _rightBottom;
        private SkillSlotControl[] _skillSlots;
        private QishuSlotControl _qishuSlot;

        // SubTask 5.7 buff/debuff 图标条
        private InkPanel _buffBar;

        // SubTask 5.8 系统导航栏
        private InkPanel _sysNav;

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

        /// <summary>任务提示条 mock 数据：任务名</summary>
        private string _questName = "寻访江湖名士";

        /// <summary>任务提示条 mock 数据：当前进度</summary>
        private int _questCurrent = 3;

        /// <summary>任务提示条 mock 数据：目标进度</summary>
        private int _questTarget = 10;

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
        /// 设置任务提示条进度（增强型 mock）。
        /// </summary>
        /// <param name="name">任务名</param>
        /// <param name="current">当前进度</param>
        /// <param name="target">目标进度</param>
        public void SetQuestProgress(string name, int current, int target)
        {
            _questName = name ?? string.Empty;
            _questCurrent = current;
            _questTarget = target;
            UpdateQuestLabel();
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
            // 刷新气血/体魄
            if (_boundCharacter != null)
            {
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
                if (_staminaBar != null)
                    _staminaBar.Value = staminaRatio;
                if (_staminaLabel != null)
                    _staminaLabel.Text = $"{(int)_boundCharacter.CurrentStamina}/{(int)_boundCharacter.MaxStamina}";
            }

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
        /// 更新任务提示条文本。
        /// </summary>
        private void UpdateQuestLabel()
        {
            if (_questLabel != null)
            {
                _questLabel.Text = $"主线任务：{_questName}  ·  {_questCurrent}/{_questTarget}";
            }
        }

        /// <summary>
        /// 重建 buff 条子控件（增强型 mock）。
        /// 根据 <see cref="_buffs"/> 列表动态生成 buff cell 与分割线。
        /// </summary>
        private void RebuildBuffBar()
        {
            if (_buffBar == null)
                return;

            // 移除旧子控件
            _buffBar.DisposeChildren();

            if (_buffs.Count == 0)
                return;

            int count = _buffs.Count;
            float cellY = (BuffBarSize.Y - BuffCellSize) * 0.5f;
            float dividerH = 24f;
            float dividerY = (BuffBarSize.Y - dividerH) * 0.5f;
            float startX = (BuffBarSize.X - count * BuffCellSize - (count - 1) * 1f) * 0.5f;
            if (startX < 0f)
                startX = 0f;

            float cursorX = startX;
            for (int i = 0; i < count; i++)
            {
                var buff = _buffs[i];
                var quality = buff.isDebuff
                    ? InkWashTheme.InkQuality.Legendary
                    : InkWashTheme.InkQuality.Uncommon;

                var cell = new InkCell
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(cursorX, cellY),
                    Size = new Float2(BuffCellSize, BuffCellSize),
                    Quality = quality,
                    Badge = string.Empty,
                };
                _buffBar.AddChild(cell);
                cursorX += BuffCellSize;

                if (i < count - 1)
                {
                    var divider = new InkDividerVertical
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(cursorX, dividerY),
                        Size = new Float2(1f, dividerH),
                    };
                    _buffBar.AddChild(divider);
                    cursorX += 1f;
                }
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
                BuildQuestBar();
                BuildMinimap();
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
            _splash1 = new InkSplash
            {
                Variant = InkSplashVariant.Normal,
                Opacity = 0.18f,
                AutoFocus = false,
            };
            _splash2 = new InkSplash
            {
                Variant = InkSplashVariant.Vermilion,
                Opacity = 0.22f,
                AutoFocus = false,
            };
            _splash3 = new InkSplash
            {
                Variant = InkSplashVariant.Elevated,
                Opacity = 0.20f,
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
        /// SubTask 5.3：顶部中央任务提示条。
        /// <see cref="InkPanel"/> 尺寸 (400, 36)，内含 <see cref="Label"/> 显示
        /// "主线任务：寻访江湖名士 · 3/10"。
        /// 字体 <see cref="InkWashTheme.FontRole.Heading"/>，字号 14，字色 <see cref="InkWashTheme.TextDefault"/>。
        /// </summary>
        private void BuildQuestBar()
        {
            _questBar = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = QuestBarSize,
            };

            _questLabel = new Label
            {
                Text = "主线任务：寻访江湖名士  ·  3/10",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 14f),
                TextColor = InkWashTheme.TextDefault,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Location = Float2.Zero,
                Size = QuestBarSize,
            };
            _questBar.AddChild(_questLabel);
            AddChild(_questBar);
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
        }

        /// <summary>
        /// SubTask 5.5：左下角头像 + 竖排角色名 + 气血条 + 体魄条。
        /// <see cref="ContainerControl"/> 位置 (20, screenHeight-180)，尺寸 (280, 160)。
        /// 内含 <see cref="InkButton"/> 头像 64x64、<see cref="InkVerticalTitle"/> 角色名"李逍遥"、
        /// <see cref="InkBar"/> 朱红气血条（Value=0.85）、<see cref="InkBar"/> 翡翠体魄条（Value=0.6）、
        /// 两个 <see cref="Label"/> 数值标签（DIN 字体）。
        /// 头像点击触发 <see cref="NavigationRequested"/>("nav-character-v2")。
        /// </summary>
        private void BuildLeftBottom()
        {
            _leftBottom = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = LeftBottomSize,
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };

            const float avatarSize = 64f;
            const float nameW = 28f;
            const float barX = avatarSize + nameW + 14f;
            const float barW = 170f;
            const float barH = 14f;

            // 头像按钮：64x64，位置 (0, 4)
            _avatarButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = string.Empty,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 4f),
                Size = new Float2(avatarSize, avatarSize),
            };
            _avatarButton.ButtonClicked += OnAvatarButtonClicked;
            _leftBottom.AddChild(_avatarButton);

            // 竖排角色名："李逍遥"，位置 (74, 0)，尺寸 (28, 140)
            _characterName = new InkVerticalTitle
            {
                Text = "李逍遥",
                FontSize = 20f,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(avatarSize + 10f, 0f),
                Size = new Float2(nameW, 140f),
            };
            _leftBottom.AddChild(_characterName);

            // 气血条：Vermilion 填充，位置 (barX, 22)，尺寸 (barW, barH)，Value=0.85
            _hpBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Vermilion,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(barX, 22f),
                Size = new Float2(barW, barH),
                Value = 0.85f,
            };
            _leftBottom.AddChild(_hpBar);

            // 气血数值标签："8500/10000"，DIN 字体，字号 12，字色 TextBrand
            _hpLabel = new Label
            {
                Text = "8500/10000",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 12f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(barX, 40f),
                Size = new Float2(barW, 18f),
            };
            _leftBottom.AddChild(_hpLabel);

            // 体魄条：Jade 填充，位置 (barX, 70)，尺寸 (barW, barH)，Value=0.6
            _staminaBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Jade,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(barX, 70f),
                Size = new Float2(barW, barH),
                Value = 0.6f,
            };
            _leftBottom.AddChild(_staminaBar);

            // 体魄数值标签："600/1000"，DIN 字体
            _staminaLabel = new Label
            {
                Text = "600/1000",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 12f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(barX, 88f),
                Size = new Float2(barW, 18f),
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
        /// SubTask 5.7：底部中央 buff/debuff 图标条。
        /// <see cref="InkPanel"/> 尺寸 (360, 42)，子控件由 <see cref="RebuildBuffBar"/> 动态生成。
        /// 初始 6 个 mock buff（3 正面翡翠 + 3 负面朱红），支持 <see cref="AddBuff"/>/<see cref="ClearBuffs"/> 动态增减。
        /// </summary>
        private void BuildBuffBar()
        {
            _buffBar = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = BuffBarSize,
            };
            AddChild(_buffBar);
            RebuildBuffBar();
        }

        /// <summary>
        /// SubTask 5.8：底部系统导航栏。
        /// <see cref="InkPanel"/> 尺寸 (640, 40)，内含 6 个 <see cref="InkButton"/> Default Md 变体。
        /// <list type="bullet">
        ///   <item>"任务" 绑定 <c>nav-quests</c></item>
        ///   <item>"装备" 绑定 <c>nav-character-v2</c>（重定向至 V2 角色属性页，装备槽已集成于 V2）</item>
        ///   <item>"战前" 绑定 <c>nav-battle-prep</c></item>
        ///   <item>"点穴" 绑定 <c>acupoint</c></item>
        ///   <item>"成就" 预留占位（点击记录日志）</item>
        ///   <item>"设置" 绑定 <c>nav-settings</c></item>
        /// </list>
        /// 有绑定 dom-id 的按钮点击触发 <see cref="NavigationRequested"/>；占位按钮记录日志。
        /// </summary>
        private void BuildSystemNav()
        {
            _sysNav = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = SysNavSize,
            };

            // 6 个按钮：标签 + dom-id（null 表示占位）
            var entries = new[]
            {
                (label: "任务", domId: "nav-quests"),
                (label: "装备", domId: "nav-equipment"),
                (label: "战前", domId: "nav-battle-prep"),
                (label: "点穴", domId: "acupoint"),
                (label: "成就", domId: (string)null),
                (label: "设置", domId: "nav-settings"),
            };

            // 按钮宽度：6 个按钮 + 5 个间距 = 600，按钮宽 = (600 - 5*6) / 6 = 95
            // 实际用 90 宽 + 间距 6 + 余白分配，避免溢出
            float btnWidth = (SysNavSize.X - SysNavBtnGap * (entries.Length - 1)) / entries.Length;
            float btnHeight = SysNavSize.Y - 4f; // 上下各留 2px 内边距
            float btnY = (SysNavSize.Y - btnHeight) * 0.5f;

            float cursorX = 0f;
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                var btn = new InkButton
                {
                    Variant = InkButtonVariant.Default,
                    ButtonSize = InkButtonSize.Md,
                    Text = entry.label,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(cursorX, btnY),
                    Size = new Float2(btnWidth, btnHeight),
                };

                // 闭包捕获当前 entry，避免循环变量陷阱
                string domId = entry.domId;
                btn.ButtonClicked += (b) => OnSystemNavButtonClicked(domId);

                _sysNav.AddChild(btn);
                cursorX += btnWidth + SysNavBtnGap;
            }

            AddChild(_sysNav);
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

            // SubTask 5.3 任务提示条：顶部居中，距离顶部 28px
            if (_questBar != null)
            {
                _questBar.Location = new Float2(sw * 0.5f - QuestBarSize.X * 0.5f, 28f);
            }

            // SubTask 5.4 小地图：右上角，右侧边距 24px，顶部 24px
            if (_minimap != null)
            {
                _minimap.Location = new Float2(sw - MinimapSize - screenEdge, screenEdge);
            }

            // SubTask 5.5 左下角容器：左侧边距 24px，底部对齐安全区
            if (_leftBottom != null)
            {
                _leftBottom.Location = new Float2(screenEdge, sh - bottomSafe - LeftBottomSize.Y + 6f);
            }

            // SubTask 5.6 右下角容器：右侧贴边，底部对齐安全区
            if (_rightBottom != null)
            {
                _rightBottom.Location = new Float2(sw - RightBottomSize.X - screenEdge, sh - bottomSafe - RightBottomSize.Y + 10f);
            }

            // SubTask 5.7 buff 条：底部居中，紧贴导航栏上方
            if (_buffBar != null)
            {
                _buffBar.Location = new Float2(sw * 0.5f - BuffBarSize.X * 0.5f, sh - 54f);
            }

            // SubTask 5.8 系统导航栏：底部居中，紧贴屏幕底部
            if (_sysNav != null)
            {
                _sysNav.Location = new Float2(sw * 0.5f - SysNavSize.X * 0.5f, sh - 50f);
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
        /// 引导按钮点击处理：记录"引导功能待落地"占位日志。
        /// </summary>
        /// <param name="button">触发事件的按钮（未使用）</param>
        private void OnGuideButtonClicked(Button button)
        {
            FlaxEngine.Debug.Log("[CombatHudPage] 引导功能待落地");
        }

        /// <summary>
        /// 头像按钮点击处理：触发 <see cref="NavigationRequested"/>("nav-character-v2")，
        /// 由 <see cref="InkPageRouter"/> 订阅后跳转角色属性页 V2。
        /// </summary>
        /// <param name="button">触发事件的按钮（未使用）</param>
        private void OnAvatarButtonClicked(Button button)
        {
            try
            {
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
        /// </summary>
        /// <param name="domId">按钮绑定的目标 dom-id，null 表示占位</param>
        private void OnSystemNavButtonClicked(string domId)
        {
            try
            {
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
    /// 圆形技能槽控件。
    /// 圆形墨色背景 + 金色边框（<see cref="InkWashTheme.BorderGold"/>）+
    /// 冷却扇形遮罩（黑色半透明，覆盖角度由 <see cref="Cooldown"/> 决定）+
    /// 底部快捷键标签。
    /// </summary>
    internal class SkillSlotControl : ContainerControl
    {
        /// <summary>圆形分段数</summary>
        private const int CircleSegments = 32;

        /// <summary>快捷键标签距离技能槽底部的偏移</summary>
        private const float HotkeyOffsetY = 4f;

        /// <summary>快捷键标签尺寸</summary>
        private const float HotkeySize = 14f;

        /// <summary>冷却扇形遮罩颜色（黑色半透明）</summary>
        private static readonly Color CooldownMaskColor = new Color(0f, 0f, 0f, 0.55f);

        /// <summary>技能槽背景色（BaseTertiary 半透明）</summary>
        private static readonly Color SlotBgColor = new Color(
            InkWashTheme.BaseTertiary.R,
            InkWashTheme.BaseTertiary.G,
            InkWashTheme.BaseTertiary.B,
            0.85f);

        /// <summary>当前冷却进度（0=就绪，1=完全冷却中）</summary>
        private float _cooldown;

        /// <summary>快捷键标签文本</summary>
        private string _hotkey = string.Empty;

        /// <summary>
        /// 冷却进度（0.0~1.0），自动钳制。
        /// 0 = 就绪（不绘制遮罩），1 = 完全冷却中（整圆遮罩）。
        /// </summary>
        public float Cooldown
        {
            get => _cooldown;
            set => _cooldown = Mathf.Clamp(value, 0f, 1f);
        }

        /// <summary>
        /// 快捷键标签文本（如"1"、"2"）。为空时不绘制标签。
        /// </summary>
        public string Hotkey
        {
            get => _hotkey;
            set => _hotkey = value ?? string.Empty;
        }

        /// <summary>
        /// 构造函数：初始化为透明、不裁剪的技能槽。
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

            // 1. 圆形墨色背景
            FillCircle(center, radius, SlotBgColor);

            // 2. 金色边框
            DrawCircleRing(center, radius, InkWashTheme.BorderGold, 1f);

            // 3. 冷却扇形遮罩（如有冷却进度）
            if (_cooldown > 0f)
            {
                DrawCooldownArc(center, radius, _cooldown);
            }

            // 4. 快捷键标签（底部，字号 10，字色 TextTertiary）
            if (!string.IsNullOrEmpty(_hotkey))
            {
                var fontRef = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 10f);
                var font = fontRef.GetFont();
                if (font != null)
                {
                    var rect = new Rectangle(
                        center.X - HotkeySize * 0.5f,
                        Height + HotkeyOffsetY,
                        HotkeySize, HotkeySize);
                    Render2D.DrawText(font, _hotkey, rect, InkWashTheme.TextTertiary,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }
            }
        }

        /// <summary>
        /// 绘制冷却扇形遮罩。
        /// 从 12 点钟方向（正北）开始顺时针扫描 cooldown*360° 范围。
        /// </summary>
        /// <param name="center">圆心</param>
        /// <param name="radius">半径</param>
        /// <param name="cooldown">冷却进度（0-1）</param>
        private void DrawCooldownArc(Float2 center, float radius, float cooldown)
        {
            // 计算需要绘制多少段三角形（按 cooldown 比例）
            int activeSegments = Mathf.Max(1, Mathf.CeilToInt(CircleSegments * cooldown));
            if (activeSegments <= 0)
                return;

            // 屏幕坐标系：从正北（向上）开始顺时针扫描
            // 起始角度 = -π/2（指向上方），顺时针方向 = 角度递增
            const float startAngle = -Mathf.PiOverTwo;
            float step = Mathf.TwoPi / CircleSegments;

            // 至多绘制 activeSegments 段
            int segCount = Mathf.Min(activeSegments, CircleSegments);
            var vertices = new Float2[segCount * 3];
            for (int i = 0; i < segCount; i++)
            {
                float a1 = startAngle + i * step;
                float a2 = startAngle + (i + 1) * step;
                int idx = i * 3;
                vertices[idx] = center;
                vertices[idx + 1] = center + new Float2(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
                vertices[idx + 2] = center + new Float2(Mathf.Cos(a2) * radius, Mathf.Sin(a2) * radius);
            }
            Render2D.FillTriangles(vertices, CooldownMaskColor);
        }

        /// <summary>
        /// 使用三角形扇填充一个圆形。
        /// </summary>
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
    // SubTask 5.6 辅助控件：奇术槽
    // =======================================================================

    /// <summary>
    /// 奇术槽控件。
    /// 圆形墨色背景 + 强金边框（<see cref="InkWashTheme.BorderGoldStrong"/>）+
    /// <see cref="Ready"/> = true 时边框脉冲动画（alpha 0.5-1.0 循环）。
    /// </summary>
    internal class QishuSlotControl : ContainerControl
    {
        /// <summary>圆形分段数</summary>
        private const int CircleSegments = 32;

        /// <summary>脉冲周期（秒）</summary>
        private const float PulsePeriod = 1.6f;

        /// <summary>脉冲最小 alpha</summary>
        private const float PulseAlphaMin = 0.5f;

        /// <summary>脉冲最大 alpha</summary>
        private const float PulseAlphaMax = 1.0f;

        /// <summary>背景色（带金色微光的 BaseTertiary）</summary>
        private static readonly Color SlotBgColor = new Color(
            InkWashTheme.BaseTertiary.R,
            InkWashTheme.BaseTertiary.G,
            InkWashTheme.BaseTertiary.B,
            0.9f);

        /// <summary>累计动画时间</summary>
        private float _animTime;

        /// <summary>是否就绪（true 时显示脉冲动画）</summary>
        private bool _ready = true;

        /// <summary>
        /// 是否就绪。true 时在 <see cref="Update"/> 中累加时间，
        /// <see cref="Draw"/> 中根据时间使边框 alpha 在 0.5-1.0 之间循环。
        /// </summary>
        public bool Ready
        {
            get => _ready;
            set => _ready = value;
        }

        /// <summary>
        /// 构造函数：初始化为透明、不裁剪的奇术槽。
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

            // 1. 圆形墨色背景（带金色微光叠加）
            FillCircle(center, radius, SlotBgColor);
            // 微弱金色辉光（中心更亮）
            var goldTint = new Color(
                InkWashTheme.GoldPrimary.R,
                InkWashTheme.GoldPrimary.G,
                InkWashTheme.GoldPrimary.B,
                0.12f);
            FillCircle(center, radius * 0.6f, goldTint);

            // 2. 金色边框（脉冲）
            Color borderColor = InkWashTheme.BorderGoldStrong;
            if (_ready)
            {
                // 正弦波在 0.5-1.0 之间循环
                float t = (_animTime / PulsePeriod) * Mathf.TwoPi;
                float alpha = Mathf.Lerp(PulseAlphaMin, PulseAlphaMax,
                    (Mathf.Sin(t) + 1f) * 0.5f);
                borderColor = new Color(
                    InkWashTheme.BorderGoldStrong.R,
                    InkWashTheme.BorderGoldStrong.G,
                    InkWashTheme.BorderGoldStrong.B,
                    InkWashTheme.BorderGoldStrong.A * alpha);
            }
            DrawCircleRing(center, radius, borderColor, 2f);

            // 3. 内圈装饰线（弱金）
            DrawCircleRing(center, radius - 4f, InkWashTheme.BorderNeutralL3, 1f);

            // 4. 中心"奇"字（书法字体，金色）
            var fontRef = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 22f);
            var font = fontRef.GetFont();
            if (font != null)
            {
                var rect = new Rectangle(0f, 0f, Width, Height);
                Render2D.DrawText(font, "奇", rect, InkWashTheme.TextBrand,
                    TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        /// <summary>
        /// 使用三角形扇填充一个圆形。
        /// </summary>
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
}
