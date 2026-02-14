using FlaxEngine;
using FlaxEngine.GUI;
using System;
using HundunWorld.Game.Combat;
using HundunWorld.Game.Combat.Effects;

namespace HundunWorld.Game.UI.GameMain
{
    /// <summary>
    /// 战斗HUD管理器
    /// 统一管理所有战斗相关的UI组件
    /// </summary>
    public class CombatHUDManager : ContainerControl
    {
        private static CombatHUDManager _instance;
        public static CombatHUDManager Instance => _instance;

        [Header("玩家信息")]
        [Tooltip("玩家实体ID")]
        public ulong PlayerEntityId = 0;

        [Header("UI组件引用")]
        [Tooltip("是否启用Buff栏")]
        public bool EnableBuffBar = true;

        [Tooltip("是否启用战斗日志")]
        public bool EnableCombatLog = true;

        [Tooltip("是否启用DPS计量器")]
        public bool EnableDPSMeter = true;

        [Tooltip("是否启用目标信息面板")]
        public bool EnableTargetInfo = true;

        // UI组件
        private BuffBarUI _playerBuffBar;      // 玩家Buff栏
        private BuffBarUI _targetBuffBar;      // 目标Buff栏
        private CombatLogUI _combatLog;        // 战斗日志
        private DPSMeterUI _dpsMeter;          // DPS计量器
        private Panel _targetInfoPanel;        // 目标信息面板
        private Label _targetNameLabel;        // 目标名称
        private Label _targetHPLabel;          // 目标血量

        // 系统引用
        private TargetSelectionSystem _targetSystem;
        private AOEIndicatorSystem _aoeIndicator;

        // 当前目标
        private Actor _currentTarget;
        private ulong _currentTargetEntityId = 0;

        // 更新计时器
        private float _updateTimer = 0f;
        private const float UpdateInterval = 0.1f;

        public CombatHUDManager()
        {
            _instance = this;

            InitializeSystems();
            InitializeUI();
            SubscribeToEvents();

            Debug.Log("[CombatHUDManager] 初始化完成");
        }

        /// <summary>
        /// 初始化系统引用
        /// </summary>
        private void InitializeSystems()
        {
            // 获取目标选择系统
            _targetSystem = TargetSelectionSystem.Instance;
            if (_targetSystem == null)
            {
                Debug.LogWarning("[CombatHUDManager] 未找到目标选择系统");
            }

            // 获取AOE指示器系统
            _aoeIndicator = AOEIndicatorSystem.Instance;
            if (_aoeIndicator == null)
            {
                Debug.LogWarning("[CombatHUDManager] 未找到AOE指示器系统");
            }
        }

        /// <summary>
        /// 初始化UI组件
        /// </summary>
        private void InitializeUI()
        {
            // 创建玩家Buff栏
            if (EnableBuffBar)
            {
                _playerBuffBar = new BuffBarUI
                {
                    Anchor = AnchorPresets.TopRight,
                    Offsets = new Margin(10, 10, 400, 60),
                    MaxDisplayCount = 10,
                    IconSize = new Float2(40, 40),
                    Parent = this
                };
                Debug.Log("[CombatHUDManager] 玩家Buff栏已创建");
            }

            // 创建目标Buff栏
            if (EnableBuffBar && EnableTargetInfo)
            {
                _targetBuffBar = new BuffBarUI
                {
                    Anchor = AnchorPresets.TopCenter,
                    Offsets = new Margin(-200, 80, 400, 60),
                    MaxDisplayCount = 8,
                    IconSize = new Float2(36, 36),
                    Parent = this
                };
            }

            // 创建战斗日志
            if (EnableCombatLog)
            {
                _combatLog = new CombatLogUI
                {
                    Anchor = AnchorPresets.BottomLeft,
                    Offsets = new Margin(10, -300, 400, 290),
                    MaxLogEntries = 100,
                    ShowTimestamp = true,
                    AutoScroll = true,
                    Parent = this
                };
                Debug.Log("[CombatHUDManager] 战斗日志已创建");
            }

            // 创建DPS计量器
            if (EnableDPSMeter)
            {
                _dpsMeter = new DPSMeterUI
                {
                    Anchor = AnchorPresets.TopLeft,
                    Offsets = new Margin(10, 60, 250, 200),
                    ShowDetailedStats = true,
                    UpdateInterval = 0.5f,
                    PlayerEntityId = PlayerEntityId,
                    Parent = this
                };
                Debug.Log("[CombatHUDManager] DPS计量器已创建");
            }

            // 创建目标信息面板
            if (EnableTargetInfo)
            {
                CreateTargetInfoPanel();
            }
        }

        /// <summary>
        /// 创建目标信息面板
        /// </summary>
        private void CreateTargetInfoPanel()
        {
            _targetInfoPanel = new Panel
            {
                AnchorPreset = AnchorPresets.TopCenter,
                Offsets = new Margin(-200, 10, 400, 70),
                BackgroundColor = new Color(0, 0, 0, 0.5f),
                Visible = false,
                Parent = this
            };

            // 目标名称
            _targetNameLabel = new Label
            {
                Text = "目标名称",
                Location = new Float2(5, 5),
                Size = new Float2(390, 25),
                TextColor = Color.Yellow,
                HorizontalAlignment = TextAlignment.Center,
                Parent = _targetInfoPanel
            };

            // 目标血量
            _targetHPLabel = new Label
            {
                Text = "HP: 1000/1000",
                Location = new Float2(5, 30),
                Size = new Float2(390, 20),
                TextColor = Color.White,
                HorizontalAlignment = TextAlignment.Center,
                Parent = _targetInfoPanel
            };

            // 血量条（简单实现）
            var hpBarBg = new Panel
            {
                Location = new Float2(10, 52),
                Size = new Float2(380, 12),
                BackgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.8f),
                Parent = _targetInfoPanel
            };

            Debug.Log("[CombatHUDManager] 目标信息面板已创建");
        }

        /// <summary>
        /// 订阅事件
        /// </summary>
        private void SubscribeToEvents()
        {
            // 订阅目标切换事件
            if (_targetSystem != null)
            {
                _targetSystem.OnTargetChanged += OnTargetChanged;
            }

            // 订阅战斗事件
            var combatManager = CombatSystemManager.Instance;
            if (combatManager != null)
            {
                combatManager.EntityDied += OnEntityDied;
            }
        }

        /// <summary>
        /// 目标切换事件处理
        /// </summary>
        private void OnTargetChanged(Actor newTarget)
        {
            _currentTarget = newTarget;

            if (newTarget != null)
            {
                // 显示目标信息面板
                if (_targetInfoPanel != null)
                {
                    _targetInfoPanel.Visible = true;
                    _targetNameLabel.Text = newTarget.Name;
                }

                // TODO: 获取目标实体ID（需要从Actor获取）
                _currentTargetEntityId = 0; // 临时值

                // 添加战斗日志
                if (_combatLog != null)
                {
                    _combatLog.AddLog(CombatLogType.Info, $"选中目标: {newTarget.Name}");
                }

                Debug.Log($"[CombatHUDManager] 目标切换: {newTarget.Name}");
            }
            else
            {
                // 隐藏目标信息面板
                if (_targetInfoPanel != null)
                {
                    _targetInfoPanel.Visible = false;
                }

                _currentTargetEntityId = 0;
            }
        }

        /// <summary>
        /// 实体死亡事件处理
        /// </summary>
        private void OnEntityDied(ulong entityId, ulong killerId)
        {
            if (_combatLog != null)
            {
                string killerName = killerId == PlayerEntityId ? "你" : $"实体{killerId}";
                _combatLog.AddLog(CombatLogType.Death, $"{killerName} 击杀了目标!");
            }

            // 如果是当前目标死亡，清除选择
            if (entityId == _currentTargetEntityId)
            {
                _currentTarget = null;
                _currentTargetEntityId = 0;
                if (_targetInfoPanel != null)
                {
                    _targetInfoPanel.Visible = false;
                }
            }
        }

        /// <summary>
        /// 更新UI（需要外部定期调用）
        /// </summary>
        public void UpdateHUD()
        {
            _updateTimer += Time.DeltaTime;
            if (_updateTimer >= UpdateInterval)
            {
                _updateTimer = 0f;
                UpdateUI();
            }
        }

        /// <summary>
        /// 更新UI显示
        /// </summary>
        private void UpdateUI()
        {
            // 更新玩家Buff显示
            if (_playerBuffBar != null && PlayerEntityId > 0)
            {
                try
                {
                    var playerEffects = SkillEffectSystem.Instance.GetActiveEffects(PlayerEntityId);
                    _playerBuffBar.UpdateBuffs(playerEffects);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[CombatHUDManager] 更新玩家Buff失败: {ex.Message}");
                }
            }

            // 更新目标Buff显示
            if (_targetBuffBar != null && _currentTargetEntityId > 0)
            {
                try
                {
                    var targetEffects = SkillEffectSystem.Instance.GetActiveEffects(_currentTargetEntityId);
                    _targetBuffBar.UpdateBuffs(targetEffects);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[CombatHUDManager] 更新目标Buff失败: {ex.Message}");
                }
            }
            else if (_targetBuffBar != null)
            {
                _targetBuffBar.ClearBuffs();
            }

            // 更新目标血量显示
            UpdateTargetHealthDisplay();
        }

        /// <summary>
        /// 更新目标血量显示
        /// </summary>
        private void UpdateTargetHealthDisplay()
        {
            if (_targetHPLabel == null || _currentTargetEntityId == 0)
                return;

            try
            {
                // TODO: 从属性管理器获取目标血量
                // var currentHP = AttributeManager.Instance.GetCurrentHealth(_currentTargetEntityId);
                // var maxHP = AttributeManager.Instance.GetMaxHealth(_currentTargetEntityId);
                // _targetHPLabel.Text = $"HP: {currentHP:F0}/{maxHP:F0}";

                // 临时显示
                _targetHPLabel.Text = "HP: ???/???";
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CombatHUDManager] 更新目标血量失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 添加战斗日志
        /// </summary>
        public void AddCombatLog(CombatLogType type, string message)
        {
            if (_combatLog != null)
            {
                _combatLog.AddLog(type, message);
            }
        }

        /// <summary>
        /// 显示AOE指示器
        /// </summary>
        public void ShowAOEIndicator(AOEIndicatorSystem.IndicatorShape shape, float radius, float angle = 0, float length = 0, float maxRange = 25f)
        {
            if (_aoeIndicator != null)
            {
                _aoeIndicator.ShowIndicator(shape, radius, angle, length, maxRange);
            }
        }

        /// <summary>
        /// 隐藏AOE指示器
        /// </summary>
        public void HideAOEIndicator()
        {
            if (_aoeIndicator != null)
            {
                _aoeIndicator.HideIndicator();
            }
        }

        /// <summary>
        /// 获取AOE指示器位置
        /// </summary>
        public Vector3 GetAOEIndicatorPosition()
        {
            if (_aoeIndicator != null)
            {
                return _aoeIndicator.GetIndicatorPosition();
            }
            return Vector3.Zero;
        }

        /// <summary>
        /// AOE指示器是否在有效范围内
        /// </summary>
        public bool IsAOEInRange()
        {
            if (_aoeIndicator != null)
            {
                return _aoeIndicator.IsInRange;
            }
            return false;
        }

        /// <summary>
        /// 设置玩家实体ID
        /// </summary>
        public void SetPlayerEntityId(ulong entityId)
        {
            PlayerEntityId = entityId;
            if (_dpsMeter != null)
            {
                _dpsMeter.SetPlayerEntityId(entityId);
            }
            Debug.Log($"[CombatHUDManager] 设置玩家实体ID: {entityId}");
        }

        /// <summary>
        /// 重置DPS统计
        /// </summary>
        public void ResetDPS()
        {
            if (_dpsMeter != null)
            {
                _dpsMeter.ResetStats();
            }
        }

        /// <summary>
        /// 获取当前目标
        /// </summary>
        public Actor GetCurrentTarget()
        {
            return _currentTarget;
        }

        /// <summary>
        /// 获取当前目标实体ID
        /// </summary>
        public ulong GetCurrentTargetEntityId()
        {
            return _currentTargetEntityId;
        }

        public override void OnDestroy()
        {
            // 取消订阅事件
            if (_targetSystem != null)
            {
                _targetSystem.OnTargetChanged -= OnTargetChanged;
            }

            var combatManager = CombatSystemManager.Instance;
            if (combatManager != null)
            {
                combatManager.EntityDied -= OnEntityDied;
            }

            if (_instance == this)
                _instance = null;

            base.OnDestroy();
        }
    }
}
