using System;
using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.Components;
using Horizon.Game.Message.Enums;

namespace HundunWorld.Game.UI.GameMain
{
    /// <summary>
    /// 属性条UI组件
    /// 显示生命值、内力/灵力/元力、体力等
    /// 支持平滑过渡、闪烁提示、数值显示
    /// </summary>
    public class AttributeBarsUI : Script
    {
        #region 配置参数

        [Header("属性条配置")]
        [Tooltip("属性条起始X位置")]
        public float StartX = 20f;

        [Tooltip("属性条起始Y位置")]
        public float StartY = 20f;

        [Tooltip("属性条宽度")]
        public float BarWidth = 300f;

        [Tooltip("属性条高度")]
        public float BarHeight = 24f;

        [Tooltip("属性条间距")]
        public float BarSpacing = 8f;

        [Tooltip("显示数值文本")]
        public bool ShowValueText = true;

        [Tooltip("平滑过渡速度")]
        public float SmoothSpeed = 5f;

        #endregion

        #region UI组件

        private Panel _mainPanel;
        
        // 生命条
        private RoundedPanel _healthBarBackground;
        private RoundedPanel _healthBarFill;
        private RoundedPanel _healthBarDamage;  // 伤害延迟显示
        private Label _healthLabel;
        
        // 能量条（内力/灵力/元力）
        private RoundedPanel _energyBarBackground;
        private RoundedPanel _energyBarFill;
        private Label _energyLabel;
        
        // 体力条
        private RoundedPanel _staminaBarBackground;
        private RoundedPanel _staminaBarFill;
        private Label _staminaLabel;

        // 角色属性引用
        private CharacterAttributesComponent _characterAttributes;

        // 平滑过渡值
        private float _displayedHealth;
        private float _displayedEnergy;
        private float _displayedStamina;
        private float _displayedDamage;  // 用于延迟伤害显示

        // 闪烁效果
        private float _healthFlashTimer = 0f;
        private float _energyFlashTimer = 0f;
        private const float FlashDuration = 0.3f;

        #endregion

        #region 生命周期

        public override void OnStart()
        {
            InitializeAttributeBars();
            FindCharacterAttributes();

            // 初始隐藏：属性条仅在游戏世界场景可见
            if (_mainPanel != null)
                _mainPanel.Visible = false;

            // 订阅场景切换事件，控制可见性
            var stateManager = UIStateManager.Instance;
            if (stateManager != null)
            {
                stateManager.SceneChanged += OnSceneChanged;
                // 检查当前场景
                OnSceneChanged(SceneType.Start, stateManager.CurrentScene);
            }

            Debug.Log("[AttributeBarsUI] 属性条UI初始化完成");
        }

        private void OnSceneChanged(SceneType previousScene, SceneType newScene)
        {
            if (_mainPanel == null) return;
            _mainPanel.Visible = (newScene == SceneType.GameWorld);
        }

        public override void OnUpdate()
        {
            if (_characterAttributes == null) return;

            UpdateAttributeValues();
            UpdateFlashEffects();
        }

        public override void OnDestroy()
        {
            var stateManager = UIStateManager.Instance;
            if (stateManager != null)
            {
                stateManager.SceneChanged -= OnSceneChanged;
            }
            CleanupAttributeBars();
        }

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化属性条
        /// </summary>
        private void InitializeAttributeBars()
        {
            // 创建主面板
            _mainPanel = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Offsets = new Margin(StartX, StartY, 0, 0),
                Size = new Float2(BarWidth + 20, (BarHeight + BarSpacing) * 3),
                BackgroundColor = Color.Transparent
            };

            // 添加到GUI
            var canvas = Actor.GetScript<UICanvas>();
            if (canvas?.GUI != null)
            {
                canvas.GUI.AddChild(_mainPanel);
            }
            else
            {
                Debug.LogWarning("[AttributeBarsUI] 未找到UICanvas组件");
                return;
            }

            // 创建生命条
            CreateHealthBar();
            
            // 创建能量条
            CreateEnergyBar();
            
            // 创建体力条
            CreateStaminaBar();
        }

        /// <summary>
        /// 创建生命条
        /// </summary>
        private void CreateHealthBar()
        {
            float yPos = 0;

            // 背景
            _healthBarBackground = new RoundedPanel
            {
                Bounds = new Rectangle(0, yPos, BarWidth, BarHeight),
                BackgroundColor = new Color(0.2f, 0.0f, 0.0f, 0.8f),
                CornerRadius = 8f
            };
            _mainPanel.AddChild(_healthBarBackground);

            // 伤害延迟显示（红色→黄色过渡）
            _healthBarDamage = new RoundedPanel
            {
                Bounds = new Rectangle(0, 0, BarWidth, BarHeight),
                BackgroundColor = new Color(0.8f, 0.4f, 0.0f, 0.6f),
                Parent = _healthBarBackground,
                CornerRadius = 8f
            };

            // 填充（生命值）
            _healthBarFill = new RoundedPanel
            {
                Bounds = new Rectangle(0, 0, BarWidth, BarHeight),
                BackgroundColor = new Color(0.2f, 0.8f, 0.2f, 0.9f),  // 绿色
                Parent = _healthBarBackground,
                CornerRadius = 8f
            };

            // 文本标签
            if (ShowValueText)
            {
                _healthLabel = new Label
                {
                    AnchorPreset = AnchorPresets.StretchAll,
                    Offsets = new Margin(5, 0, 5, 0),
                    Text = "生命: 100/100",
                    TextColor = Color.White,
                    TextColorHighlighted = Color.White,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center
                };
                _healthBarBackground.AddChild(_healthLabel);
            }
        }

        /// <summary>
        /// 创建能量条
        /// </summary>
        private void CreateEnergyBar()
        {
            float yPos = BarHeight + BarSpacing;

            // 背景
            _energyBarBackground = new RoundedPanel
            {
                Bounds = new Rectangle(0, yPos, BarWidth, BarHeight),
                BackgroundColor = new Color(0.0f, 0.0f, 0.2f, 0.8f),
                CornerRadius = 8f
            };
            _mainPanel.AddChild(_energyBarBackground);

            // 填充（能量值）
            _energyBarFill = new RoundedPanel
            {
                Bounds = new Rectangle(0, 0, BarWidth, BarHeight),
                BackgroundColor = new Color(0.2f, 0.4f, 1.0f, 0.9f),  // 蓝色
                Parent = _energyBarBackground,
                CornerRadius = 8f
            };

            // 文本标签
            if (ShowValueText)
            {
                _energyLabel = new Label
                {
                    AnchorPreset = AnchorPresets.StretchAll,
                    Offsets = new Margin(5, 0, 5, 0),
                    Text = "内力: 100/100",
                    TextColor = Color.White,
                    TextColorHighlighted = Color.White,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center
                };
                _energyBarBackground.AddChild(_energyLabel);
            }
        }

        /// <summary>
        /// 创建体力条
        /// </summary>
        private void CreateStaminaBar()
        {
            float yPos = (BarHeight + BarSpacing) * 2;

            // 背景
            _staminaBarBackground = new RoundedPanel
            {
                Bounds = new Rectangle(0, yPos, BarWidth, BarHeight),
                BackgroundColor = new Color(0.2f, 0.2f, 0.0f, 0.8f),
                CornerRadius = 8f
            };
            _mainPanel.AddChild(_staminaBarBackground);

            // 填充（体力值）
            _staminaBarFill = new RoundedPanel
            {
                Bounds = new Rectangle(0, 0, BarWidth, BarHeight),
                BackgroundColor = new Color(1.0f, 0.9f, 0.2f, 0.9f),  // 黄色
                Parent = _staminaBarBackground,
                CornerRadius = 8f
            };

            // 文本标签
            if (ShowValueText)
            {
                _staminaLabel = new Label
                {
                    AnchorPreset = AnchorPresets.StretchAll,
                    Offsets = new Margin(5, 0, 5, 0),
                    Text = "体力: 100/100",
                    TextColor = Color.White,
                    TextColorHighlighted = Color.White,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center
                };
                _staminaBarBackground.AddChild(_staminaLabel);
            }
        }

        /// <summary>
        /// 查找角色属性组件
        /// </summary>
        private void FindCharacterAttributes()
        {
            // 在场景中查找玩家角色
            var player = Scene.FindActor("Player");
            if (player != null)
            {
                _characterAttributes = player.GetScript<CharacterAttributesComponent>();
                if (_characterAttributes != null)
                {
                    // 初始化显示值
                    _displayedHealth = _characterAttributes.CurrentHealth;
                    _displayedEnergy = _characterAttributes.CurrentEnergy;
                    _displayedStamina = _characterAttributes.CurrentStamina;
                    _displayedDamage = _displayedHealth;
                }
                else
                {
                    Debug.LogWarning("[AttributeBarsUI] 玩家角色未找到CharacterAttributesComponent");
                }
            }
            else
            {
                Debug.LogWarning("[AttributeBarsUI] 未找到玩家角色Actor");
            }
        }

        #endregion

        #region 更新逻辑

        /// <summary>
        /// 更新属性值显示
        /// </summary>
        private void UpdateAttributeValues()
        {
            float deltaTime = Time.DeltaTime;

            // 获取当前真实值
            float targetHealth = _characterAttributes.CurrentHealth;
            float targetEnergy = _characterAttributes.CurrentEnergy;
            float targetStamina = _characterAttributes.CurrentStamina;

            // 检测生命值变化（受伤）
            if (targetHealth < _displayedHealth)
            {
                _healthFlashTimer = FlashDuration;
                _displayedDamage = _displayedHealth;  // 保存旧的生命值用于延迟显示
            }

            // 平滑过渡
            _displayedHealth = Mathf.Lerp(_displayedHealth, targetHealth, SmoothSpeed * deltaTime);
            _displayedEnergy = Mathf.Lerp(_displayedEnergy, targetEnergy, SmoothSpeed * deltaTime);
            _displayedStamina = Mathf.Lerp(_displayedStamina, targetStamina, SmoothSpeed * deltaTime);

            // 伤害延迟显示平滑过渡
            if (_displayedDamage > _displayedHealth)
            {
                _displayedDamage = Mathf.Lerp(_displayedDamage, _displayedHealth, SmoothSpeed * 0.5f * deltaTime);
            }

            // 更新UI显示
            UpdateHealthBar();
            UpdateEnergyBar();
            UpdateStaminaBar();
        }

        /// <summary>
        /// 更新生命条
        /// </summary>
        private void UpdateHealthBar()
        {
            float healthRatio = _characterAttributes.MaxHealth > 0 
                ? _displayedHealth / _characterAttributes.MaxHealth 
                : 0f;
            float damageRatio = _characterAttributes.MaxHealth > 0 
                ? _displayedDamage / _characterAttributes.MaxHealth 
                : 0f;

            // 更新填充宽度
            var fillBounds = _healthBarFill.Bounds;
            fillBounds.Size.X = BarWidth * healthRatio;
            _healthBarFill.Bounds = fillBounds;

            // 更新伤害延迟显示
            var damageBounds = _healthBarDamage.Bounds;
            damageBounds.Size.X = BarWidth * damageRatio;
            _healthBarDamage.Bounds = damageBounds;

            // 根据生命值比例改变颜色
            if (healthRatio > 0.5f)
            {
                _healthBarFill.BackgroundColor = new Color(0.2f, 0.8f, 0.2f, 0.9f);  // 绿色
            }
            else if (healthRatio > 0.25f)
            {
                _healthBarFill.BackgroundColor = new Color(0.9f, 0.9f, 0.2f, 0.9f);  // 黄色
            }
            else
            {
                _healthBarFill.BackgroundColor = new Color(0.9f, 0.2f, 0.2f, 0.9f);  // 红色
            }

            // 更新文本
            if (ShowValueText && _healthLabel != null)
            {
                _healthLabel.Text = $"生命: {(int)_displayedHealth}/{(int)_characterAttributes.MaxHealth}";
            }
        }

        /// <summary>
        /// 更新能量条
        /// </summary>
        private void UpdateEnergyBar()
        {
            float energyRatio = _characterAttributes.MaxEnergy > 0 
                ? _displayedEnergy / _characterAttributes.MaxEnergy 
                : 0f;

            // 更新填充宽度
            var fillBounds = _energyBarFill.Bounds;
            fillBounds.Size.X = BarWidth * energyRatio;
            _energyBarFill.Bounds = fillBounds;

            // 根据成长阶段显示不同名称
            string energyName = GetEnergyName();

            // 更新文本
            if (ShowValueText && _energyLabel != null)
            {
                _energyLabel.Text = $"{energyName}: {(int)_displayedEnergy}/{(int)_characterAttributes.MaxEnergy}";
            }
        }

        /// <summary>
        /// 更新体力条
        /// </summary>
        private void UpdateStaminaBar()
        {
            float staminaRatio = _characterAttributes.MaxStamina > 0 
                ? _displayedStamina / _characterAttributes.MaxStamina 
                : 0f;

            // 更新填充宽度
            var fillBounds = _staminaBarFill.Bounds;
            fillBounds.Size.X = BarWidth * staminaRatio;
            _staminaBarFill.Bounds = fillBounds;

            // 体力不足时改变颜色
            if (staminaRatio < 0.3f)
            {
                _staminaBarFill.BackgroundColor = new Color(0.9f, 0.5f, 0.1f, 0.9f);  // 橙色警告
            }
            else
            {
                _staminaBarFill.BackgroundColor = new Color(1.0f, 0.9f, 0.2f, 0.9f);  // 黄色
            }

            // 更新文本
            if (ShowValueText && _staminaLabel != null)
            {
                _staminaLabel.Text = $"体力: {(int)_displayedStamina}/{(int)_characterAttributes.MaxStamina}";
            }
        }

        /// <summary>
        /// 获取能量名称（根据成长阶段）
        /// </summary>
        private string GetEnergyName()
        {
            switch (_characterAttributes.CurrentStage)
            {
                case CharacterStage.Wuxia:
                    return "内力";
                case CharacterStage.Xianxia:
                    return "灵力";
                case CharacterStage.Xuanhuan:
                    return "元力";
                default:
                    return "能量";
            }
        }

        /// <summary>
        /// 更新闪烁效果
        /// </summary>
        private void UpdateFlashEffects()
        {
            float deltaTime = Time.DeltaTime;

            // 生命值闪烁
            if (_healthFlashTimer > 0)
            {
                _healthFlashTimer -= deltaTime;
                
                // 闪烁效果（0.5秒内快速闪烁）
                float flashAlpha = Mathf.Sin(_healthFlashTimer * 20f) * 0.5f + 0.5f;
                var color = _healthBarBackground.BackgroundColor;
                color.A = 0.8f + flashAlpha * 0.2f;
                _healthBarBackground.BackgroundColor = color;
            }

            // 能量值闪烁（能量不足时）
            if (_characterAttributes.CurrentEnergy < _characterAttributes.MaxEnergy * 0.2f)
            {
                _energyFlashTimer += deltaTime;
                float flashAlpha = Mathf.Sin(_energyFlashTimer * 3f) * 0.3f + 0.7f;
                var color = _energyBarFill.BackgroundColor;
                color.A = flashAlpha;
                _energyBarFill.BackgroundColor = color;
            }
        }

        #endregion

        #region 公共接口

        /// <summary>
        /// 设置角色属性组件（手动设置）
        /// </summary>
        public void SetCharacterAttributes(CharacterAttributesComponent attributes)
        {
            _characterAttributes = attributes;
            if (_characterAttributes != null)
            {
                _displayedHealth = _characterAttributes.CurrentHealth;
                _displayedEnergy = _characterAttributes.CurrentEnergy;
                _displayedStamina = _characterAttributes.CurrentStamina;
                _displayedDamage = _displayedHealth;
            }
        }

        /// <summary>
        /// 显示伤害数字
        /// </summary>
        public void ShowDamageNumber(float damage, Vector3 worldPosition)
        {
            try
            {
                var damageSystem = global::Game.Combat.Effects.DamageNumberSystem.Instance;
                if (damageSystem != null)
                {
                    damageSystem.ShowDamageNumber(damage, worldPosition);
                }
                else
                {
                    Debug.Log($"[AttributeBarsUI] 显示伤害: {damage} 在位置 {worldPosition}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AttributeBarsUI] 显示伤害数字失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 显示治疗数字
        /// </summary>
        public void ShowHealNumber(float heal, Vector3 worldPosition)
        {
            try
            {
                var damageSystem = global::Game.Combat.Effects.DamageNumberSystem.Instance;
                if (damageSystem != null)
                {
                    var healText = $"+{(int)heal}";
                    damageSystem.ShowText(healText, worldPosition, Color.Green, 1.5f);
                }
                else
                {
                    Debug.Log($"[AttributeBarsUI] 显示治疗: {heal} 在位置 {worldPosition}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AttributeBarsUI] 显示治疗数字失败: {ex.Message}");
            }
        }

        #endregion

        #region 清理

        /// <summary>
        /// 清理属性条
        /// </summary>
        private void CleanupAttributeBars()
        {
            if (_mainPanel != null && _mainPanel.Parent != null)
            {
                _mainPanel.Parent.RemoveChild(_mainPanel);
                _mainPanel.Dispose();
            }
        }

        #endregion
    }
}
