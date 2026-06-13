using FlaxEngine;
using FlaxEngine.GUI;
using System;
using System.Collections.Generic;
using HundunWorld.Game.Combat.Effects;
using Horizon.Game.Message.Enums;

namespace HundunWorld.Game.UI.GameMain
{
    /// <summary>
    /// Buff/Debuff栏UI
    /// 显示角色身上的所有增益和减益效果
    /// </summary>
    public class BuffBarUI : ContainerControl
    {
        [Header("布局设置")]
        [Tooltip("Buff图标大小")]
        public Float2 IconSize = new Float2(40, 40);

        [Tooltip("图标间距")]
        public float IconSpacing = 5f;

        [Tooltip("最大显示数量")]
        public int MaxDisplayCount = 10;

        [Header("样式设置")]
        [Tooltip("Buff边框颜色（绿色）")]
        public Color BuffBorderColor = new Color(0.0f, 1.0f, 0.0f, 1.0f);

        [Tooltip("Debuff边框颜色（红色）")]
        public Color DebuffBorderColor = new Color(1.0f, 0.0f, 0.0f, 1.0f);

        [Tooltip("背景颜色")]
        public new Color BackgroundColor = new Color(0, 0, 0, 0.3f);

        [Header("位置设置")]
        [Tooltip("UI锚点")]
        public AnchorPresets Anchor = AnchorPresets.TopRight;

        [Tooltip("偏移量")]
        public new Margin Offsets = new Margin(10, 10, 300, 60);

        [Tooltip("是否启用调试日志")]
        public bool EnableDebugLog = false;

        // Buff图标列表
        private List<BuffIcon> _buffIcons = new List<BuffIcon>();

        // Buff数据缓存
        private Dictionary<int, BuffData> _activeBuffs = new Dictionary<int, BuffData>();

        // 容器面板
        private Panel _containerPanel;

        public BuffBarUI()
        {
            InitializeBuffBar();

            if (EnableDebugLog)
                Debug.Log("[BuffBarUI] 初始化完成");
        }

        /// <summary>
        /// 初始化Buff栏
        /// </summary>
        private void InitializeBuffBar()
        {
            // 创建容器面板
            _containerPanel = new Panel
            {
                AnchorPreset = Anchor,
                Offsets = Offsets,
                BackgroundColor = BackgroundColor,
                Parent = this
            };

            // 创建Buff图标网格
            for (int i = 0; i < MaxDisplayCount; i++)
            {
                var icon = new BuffIcon
                {
                    Location = new Float2(i * (IconSize.X + IconSpacing), 0),
                    Size = IconSize,
                    Visible = false,
                    Parent = _containerPanel
                };
                
                icon.BuffBorderColor = BuffBorderColor;
                icon.DebuffBorderColor = DebuffBorderColor;
                
                _buffIcons.Add(icon);
            }
        }

        /// <summary>
        /// 更新Buff显示
        /// </summary>
        public void UpdateBuffs(List<ActiveEffect> effects)
        {
            if (effects == null)
            {
                ClearBuffs();
                return;
            }

            // 清空旧数据
            _activeBuffs.Clear();

            // 转换为BuffData
            foreach (var effect in effects)
            {
                if (effect == null || effect.Template == null)
                    continue;

                var buffData = new BuffData
                {
                    Id = effect.Template.Id,
                    Name = effect.Template.Name,
                    Type = effect.Template.Type,
                    RemainingTime = effect.RemainingDuration,
                    Stacks = effect.Stacks,
                    IconPath = GetBuffIconPath(effect.Template.Id)
                };
                _activeBuffs[buffData.Id] = buffData;
            }

            // 刷新UI显示
            RefreshBuffIcons();
        }

        /// <summary>
        /// 刷新Buff图标显示
        /// </summary>
        private void RefreshBuffIcons()
        {
            int index = 0;

            // 先显示Buff（增益）
            foreach (var kvp in _activeBuffs)
            {
                if (kvp.Value.Type == EffectType.Buff && index < MaxDisplayCount)
                {
                    UpdateBuffIcon(_buffIcons[index], kvp.Value);
                    index++;
                }
            }

            // 再显示Debuff（减益）
            foreach (var kvp in _activeBuffs)
            {
                if ((kvp.Value.Type == EffectType.Debuff || kvp.Value.Type == EffectType.DoT) 
                    && index < MaxDisplayCount)
                {
                    UpdateBuffIcon(_buffIcons[index], kvp.Value);
                    index++;
                }
            }

            // 隐藏未使用的图标
            for (int i = index; i < _buffIcons.Count; i++)
            {
                _buffIcons[i].Visible = false;
            }

            if (EnableDebugLog)
                Debug.Log($"[BuffBarUI] 刷新Buff显示: {index} 个效果");
        }

        /// <summary>
        /// 更新单个Buff图标
        /// </summary>
        private void UpdateBuffIcon(BuffIcon icon, BuffData data)
        {
            icon.Visible = true;
            icon.SetData(data);
        }

        /// <summary>
        /// 清空所有Buff显示
        /// </summary>
        public void ClearBuffs()
        {
            _activeBuffs.Clear();
            foreach (var icon in _buffIcons)
            {
                icon.Visible = false;
            }
        }

        /// <summary>
        /// 获取Buff图标路径
        /// </summary>
        private string GetBuffIconPath(int buffId)
        {
            // TODO: 根据实际资源路径配置
            return $"Game/UI/Icons/Buffs/Buff_{buffId}.png";
        }

        /// <summary>
        /// 更新Buff计时器（需要外部定期调用）
        /// </summary>
        public void UpdateTimers()
        {
            // 更新所有可见图标的倒计时
            foreach (var icon in _buffIcons)
            {
                if (icon.Visible)
                {
                    icon.UpdateTimer();
                }
            }
        }

        /// <summary>
        /// Buff图标控件
        /// </summary>
        private class BuffIcon : ContainerControl
        {
            private Label _stacksLabel;
            private Label _timerLabel;
            private BuffData _data;

            public Color BuffBorderColor { get; set; } = Color.Green;
            public Color DebuffBorderColor { get; set; } = Color.Red;

            public BuffIcon()
            {
                // 图标背景
                BackgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.8f);

                // 层数标签（右下角）
                _stacksLabel = new Label
                {
                    AnchorPreset = AnchorPresets.BottomRight,
                    Offsets = new Margin(-20, -15, 20, 15),
                    TextColor = Color.Yellow,
                    TextColorHighlighted = Color.Yellow,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };

                // 剩余时间标签（底部中央）
                _timerLabel = new Label
                {
                    AnchorPreset = AnchorPresets.BottomCenter,
                    Offsets = new Margin(-20, -15, 20, 15),
                    TextColor = Color.White,
                    TextColorHighlighted = Color.White,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    Parent = this
                };
            }

            public void SetData(BuffData data)
            {
                _data = data;

                // 显示层数
                _stacksLabel.Text = data.Stacks > 1 ? data.Stacks.ToString() : "";

                // 显示剩余时间
                UpdateTimer();
            }

            public void UpdateTimer()
            {
                if (_data != null)
                {
                    _timerLabel.Text = $"{_data.RemainingTime:F0}s";
                    
                    // 更新剩余时间（简单递减，实际应该从SkillEffectSystem获取）
                    _data.RemainingTime -= Time.DeltaTime;
                    
                    // 时间快结束时变红
                    if (_data.RemainingTime <= 3f)
                    {
                        _timerLabel.TextColor = Color.Red;
                    }
                    else
                    {
                        _timerLabel.TextColor = Color.White;
                    }
                }
            }

            public override void Draw()
            {
                base.Draw();

                if (_data == null)
                    return;

                try
                {
                    // 绘制边框
                    Color borderColor = _data.Type == EffectType.Buff ? BuffBorderColor : DebuffBorderColor;
                    Render2D.DrawRectangle(new Rectangle(Float2.Zero, Size), borderColor, 2f);

                    // 绘制Buff名称（中心）
                    var textBounds = new Rectangle(2, 2, Size.X - 4, Size.Y - 20);
                    Render2D.DrawText(
                        FlaxEngine.GUI.Style.Current.FontMedium,
                        _data.Name,
                        textBounds,
                        Color.White,
                        TextAlignment.Center,
                        TextAlignment.Center
                    );
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[BuffIcon] 绘制失败: {ex.Message}");
                }
            }

            public override void OnMouseEnter(Float2 location)
            {
                base.OnMouseEnter(location);

                // 显示Tooltip
                if (_data != null)
                {
                    string tooltip = $"{_data.Name}\n剩余时间: {_data.RemainingTime:F1}秒";
                    if (_data.Stacks > 1)
                        tooltip += $"\n层数: {_data.Stacks}";
                    
                    // TODO: 集成Tooltip系统
                    Debug.Log($"[BuffIcon] Tooltip: {tooltip}");
                }
            }
        }

        /// <summary>
        /// Buff数据结构
        /// </summary>
        private class BuffData
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public EffectType Type { get; set; }
            public float RemainingTime { get; set; }
            public int Stacks { get; set; }
            public string IconPath { get; set; }

        }
    }
}
