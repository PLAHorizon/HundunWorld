using FlaxEngine;
using FlaxEngine.GUI;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.GameMain
{
    /// <summary>
    /// 战斗日志UI
    /// 记录战斗事件并实时显示
    /// </summary>
    public class CombatLogUI : ContainerControl
    {
        [Header("日志设置")]
        [Tooltip("最大日志条数")]
        public int MaxLogEntries = 100;

        [Tooltip("日志自动滚动")]
        public bool AutoScroll = true;

        [Tooltip("显示时间戳")]
        public bool ShowTimestamp = true;

        [Tooltip("是否启用调试日志")]
        public bool EnableDebugLog = false;

        [Header("UI设置")]
        [Tooltip("背景颜色")]
        public new Color BackgroundColor = new Color(0, 0, 0, 0.6f);

        [Tooltip("UI锚点")]
        public AnchorPresets Anchor = AnchorPresets.BottomLeft;

        [Tooltip("偏移量")]
        public new Margin Offsets = new Margin(10, -300, 400, 290);

        // 日志条目列表
        private List<CombatLogEntry> _logEntries = new List<CombatLogEntry>();

        // UI组件
        private Panel _logPanel;
        private Panel _contentPanel;
        private List<Label> _logLabels = new List<Label>();
        private int _scrollOffset = 0;

        public CombatLogUI()
        {
            InitializeCombatLog();
            SubscribeToCombatEvents();

            if (EnableDebugLog)
                Debug.Log("[CombatLogUI] 初始化完成");
        }

        /// <summary>
        /// 初始化战斗日志UI
        /// </summary>
        private void InitializeCombatLog()
        {
            // 创建背景面板
            _logPanel = new Panel
            {
                AnchorPreset = Anchor,
                Offsets = Offsets,
                BackgroundColor = BackgroundColor,
                Parent = this
            };

            // 创建内容面板
            _contentPanel = new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(5, 5, 5, 5),
                Parent = _logPanel
            };

            // 创建标题
            var titleLabel = new Label
            {
                Text = "战斗日志",
                Location = new Float2(5, 5),
                Size = new Float2(380, 20),
                TextColor = Color.Yellow,
                Parent = _contentPanel
            };
        }

        /// <summary>
        /// 订阅战斗事件
        /// </summary>
        private void SubscribeToCombatEvents()
        {
            try
            {
                // 订阅伤害事件
                var combatManager = HundunWorld.Game.Combat.CombatSystemManager.Instance;
                if (combatManager != null)
                {
                    combatManager.EntityDied += OnEntityDied;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CombatLogUI] 订阅战斗事件失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 添加日志条目
        /// </summary>
        public void AddLog(CombatLogEntry entry)
        {
            _logEntries.Add(entry);

            // 限制日志数量
            if (_logEntries.Count > MaxLogEntries)
            {
                _logEntries.RemoveAt(0);
                
                // 移除最旧的Label
                if (_logLabels.Count > 0)
                {
                    var oldLabel = _logLabels[0];
                    oldLabel.Dispose();
                    _logLabels.RemoveAt(0);
                }
            }

            // 创建日志Label
            CreateLogLabel(entry);

            // 自动滚动到底部
            if (AutoScroll)
            {
                _scrollOffset = Math.Max(0, _logLabels.Count - 10);
            }

            if (EnableDebugLog)
                Debug.Log($"[CombatLogUI] 添加日志: {entry.Message}");
        }

        /// <summary>
        /// 添加日志（便捷方法）
        /// </summary>
        public void AddLog(CombatLogType type, string message)
        {
            AddLog(new CombatLogEntry
            {
                Type = type,
                Message = message,
                Timestamp = DateTime.Now
            });
        }

        /// <summary>
        /// 创建日志Label
        /// </summary>
        private void CreateLogLabel(CombatLogEntry entry)
        {
            try
            {
                string text = FormatLogEntry(entry);
                Color color = GetLogColor(entry.Type);

                var label = new Label
                {
                    Text = text,
                    Location = new Float2(5, 30 + _logLabels.Count * 20),
                    Size = new Float2(380, 18),
                    TextColor = color,
                    TextColorHighlighted = color,
                    Parent = _contentPanel
                };

                _logLabels.Add(label);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CombatLogUI] 创建日志Label失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 格式化日志条目
        /// </summary>
        private string FormatLogEntry(CombatLogEntry entry)
        {
            string timestamp = ShowTimestamp ? $"[{entry.Timestamp:HH:mm:ss}] " : "";
            return $"{timestamp}{entry.Message}";
        }

        /// <summary>
        /// 获取日志颜色
        /// </summary>
        private Color GetLogColor(CombatLogType type)
        {
            return type switch
            {
                CombatLogType.Damage => Color.White,
                CombatLogType.Critical => Color.Red,
                CombatLogType.Heal => Color.Green,
                CombatLogType.Buff => Color.Cyan,
                CombatLogType.Debuff => Color.Orange,
                CombatLogType.Death => Color.Yellow,
                CombatLogType.Skill => new Color(0.8f, 0.8f, 1.0f),
                CombatLogType.Info => Color.Gray,
                _ => Color.Gray
            };
        }

        /// <summary>
        /// 实体死亡事件处理
        /// </summary>
        private void OnEntityDied(ulong entityId, ulong killerId)
        {
            AddLog(new CombatLogEntry
            {
                Type = CombatLogType.Death,
                Message = $"实体 {entityId} 被 {killerId} 击杀",
                Timestamp = DateTime.Now
            });
        }

        /// <summary>
        /// 清空日志
        /// </summary>
        public void ClearLog()
        {
            _logEntries.Clear();
            
            foreach (var label in _logLabels)
            {
                label?.Dispose();
            }
            _logLabels.Clear();

            _scrollOffset = 0;

            if (EnableDebugLog)
                Debug.Log("[CombatLogUI] 清空日志");
        }

        /// <summary>
        /// 处理滚动输入（需要外部调用）
        /// </summary>
        public void HandleScrollInput()
        {
            // 使用鼠标滚轮滚动
            if (IsMouseOver && Input.MouseScrollDelta != 0)
            {
                _scrollOffset -= (int)(Input.MouseScrollDelta * 3);
                _scrollOffset = Mathf.Clamp(_scrollOffset, 0, Math.Max(0, _logLabels.Count - 10));
            }
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        public void Cleanup()
        {
            // 取消订阅事件
            try
            {
                var combatManager = HundunWorld.Game.Combat.CombatSystemManager.Instance;
                if (combatManager != null)
                {
                    combatManager.EntityDied -= OnEntityDied;
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// 战斗日志类型
    /// </summary>
    public enum CombatLogType
    {
        Damage,     // 伤害
        Critical,   // 暴击
        Heal,       // 治疗
        Buff,       // 增益
        Debuff,     // 减益
        Death,      // 死亡
        Skill,      // 技能使用
        Info        // 信息
    }

    /// <summary>
    /// 战斗日志条目
    /// </summary>
    public class CombatLogEntry
    {
        public CombatLogType Type { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
