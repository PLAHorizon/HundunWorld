using FlaxEngine;
using FlaxEngine.GUI;
using System;
using HundunWorld.Game.Combat;

namespace HundunWorld.Game.UI.GameMain
{
    /// <summary>
    /// DPS计量器UI
    /// 实时显示玩家的DPS、暴击率等战斗统计数据
    /// </summary>
    public class DPSMeterUI : ContainerControl
    {
        [Header("显示设置")]
        [Tooltip("玩家实体ID")]
        public ulong PlayerEntityId = 0;

        [Tooltip("更新间隔（秒）")]
        public float UpdateInterval = 0.5f;

        [Tooltip("是否显示详细统计")]
        public bool ShowDetailedStats = true;

        [Header("UI设置")]
        [Tooltip("背景颜色")]
        public Color BackgroundColor = new Color(0, 0, 0, 0.5f);

        [Tooltip("UI锚点")]
        public AnchorPresets Anchor = AnchorPresets.TopLeft;

        [Tooltip("偏移量")]
        public Margin Offsets = new Margin(10, 60, 250, 200);

        // UI组件
        private Panel _containerPanel;
        private Label _titleLabel;
        private Label _dpsLabel;
        private Label _instantDpsLabel;
        private Label _critRateLabel;
        private Label _maxHitLabel;
        private Label _avgDamageLabel;
        private Label _hitCountLabel;

        // 更新计时器
        private float _updateTimer = 0f;

        public DPSMeterUI()
        {
            InitializeDPSMeter();
        }

        /// <summary>
        /// 初始化DPS计量器UI
        /// </summary>
        private void InitializeDPSMeter()
        {
            // 创建容器面板
            _containerPanel = new Panel
            {
                AnchorPreset = Anchor,
                Offsets = Offsets,
                BackgroundColor = BackgroundColor,
                Parent = this
            };

            float yOffset = 5;
            float lineHeight = 20;

            // 标题
            _titleLabel = CreateLabel("战斗统计", yOffset, Color.Yellow);
            yOffset += lineHeight;

            // DPS
            _dpsLabel = CreateLabel("DPS: 0.0", yOffset, Color.White);
            yOffset += lineHeight;

            // 瞬时DPS
            _instantDpsLabel = CreateLabel("瞬时DPS: 0.0", yOffset, new Color(0.8f, 0.8f, 1.0f));
            yOffset += lineHeight;

            if (ShowDetailedStats)
            {
                // 暴击率
                _critRateLabel = CreateLabel("暴击率: 0%", yOffset, Color.Red);
                yOffset += lineHeight;

                // 最高伤害
                _maxHitLabel = CreateLabel("最高: 0", yOffset, Color.Orange);
                yOffset += lineHeight;

                // 平均伤害
                _avgDamageLabel = CreateLabel("平均: 0", yOffset, Color.Gray);
                yOffset += lineHeight;

                // 命中次数
                _hitCountLabel = CreateLabel("命中: 0", yOffset, Color.Cyan);
            }
        }

        /// <summary>
        /// 创建标签
        /// </summary>
        private Label CreateLabel(string text, float yOffset, Color color)
        {
            return new Label
            {
                Text = text,
                Location = new Float2(5, yOffset),
                Size = new Float2(230, 18),
                TextColor = color,
                TextColorHighlighted = color,
                Parent = _containerPanel
            };
        }

        /// <summary>
        /// 更新显示（需要外部定期调用）
        /// </summary>
        public void UpdateUI()
        {
            // 定时更新显示
            _updateTimer += Time.DeltaTime;
            if (_updateTimer >= UpdateInterval)
            {
                _updateTimer = 0f;
                UpdateDisplay();
            }
        }

        /// <summary>
        /// 更新显示
        /// </summary>
        private void UpdateDisplay()
        {
            if (PlayerEntityId == 0)
                return;

            try
            {
                var stats = DamageMeter.Instance.GetStatistics(PlayerEntityId);

                // 更新DPS
                _dpsLabel.Text = $"DPS: {stats.DPS:F1}";

                // 更新瞬时DPS
                _instantDpsLabel.Text = $"瞬时DPS: {stats.InstantDPS:F0}";

                if (ShowDetailedStats)
                {
                    // 更新暴击率
                    _critRateLabel.Text = $"暴击率: {stats.CriticalRate:F1}%";
                    
                    // 更新最高伤害
                    _maxHitLabel.Text = $"最高: {stats.MaxHit:F0}";
                    
                    // 更新平均伤害
                    _avgDamageLabel.Text = $"平均: {stats.AverageDamage:F0}";
                    
                    // 更新命中次数
                    _hitCountLabel.Text = $"命中: {stats.HitCount} ({stats.RecentHitCount} 最近)";
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DPSMeterUI] 更新显示失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置玩家实体ID
        /// </summary>
        public void SetPlayerEntityId(ulong entityId)
        {
            PlayerEntityId = entityId;
        }

        /// <summary>
        /// 重置统计数据
        /// </summary>
        public void ResetStats()
        {
            if (PlayerEntityId != 0)
            {
                DamageMeter.Instance.ResetEntity(PlayerEntityId);
            }
        }
    }
}
