using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using Game.Combat.Skills;
using Game.Character.Attributes;

namespace HundunWorld.Game.UI.GameMain
{
    /// <summary>
    /// 技能栏UI组件
    /// 显示技能图标、冷却时间、快捷键提示
    /// 支持拖拽、技能切换、冷却显示
    /// </summary>
    public class SkillBarUI : Script
    {
        #region 配置参数

        [Header("技能栏配置")]
        [Tooltip("技能栏槽位数量")]
        public int SkillSlotCount = 8;

        [Tooltip("技能栏起始X位置")]
        public float StartX = 100f;

        [Tooltip("技能栏起始Y位置（从屏幕底部）")]
        public float BottomOffset = 80f;

        [Tooltip("技能槽位大小")]
        public float SlotSize = 60f;

        [Tooltip("技能槽位间距")]
        public float SlotSpacing = 10f;

        [Tooltip("显示快捷键提示")]
        public bool ShowHotkeys = true;

        #endregion

        #region UI组件

        private Panel _skillBarPanel;
        private List<SkillSlot> _skillSlots = new List<SkillSlot>();
        private CharacterAttributesComponent _characterAttributes;

        #endregion

        #region 技能槽位类

        /// <summary>
        /// 技能槽位数据
        /// </summary>
        private class SkillSlot
        {
            public Panel SlotPanel;              // 槽位面板
            public Image IconImage;              // 技能图标
            public Panel CooldownOverlay;        // 冷却遮罩
            public Label CooldownText;           // 冷却文本
            public Label HotkeyLabel;            // 快捷键标签
            public Label EnergyCostLabel;        // 能量消耗标签
            public SkillBase BoundSkill;         // 绑定的技能
            public int SlotIndex;                // 槽位索引

            // 视觉状态
            public bool IsReady = true;          // 是否就绪
            public float CooldownProgress = 0f;  // 冷却进度（0-1）
        }

        #endregion

        #region 生命周期

        public override void OnStart()
        {
            InitializeSkillBar();
            FindCharacterAttributes();
            Debug.Log("[SkillBarUI] 技能栏UI初始化完成");
        }

        public override void OnUpdate()
        {
            UpdateSkillSlots();
        }

        public override void OnDestroy()
        {
            CleanupSkillBar();
        }

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化技能栏
        /// </summary>
        private void InitializeSkillBar()
        {
            // 创建技能栏主面板
            _skillBarPanel = new Panel
            {
                AnchorPreset = AnchorPresets.BottomLeft,
                Offsets = new Margin(StartX, -BottomOffset - SlotSize, 0, -BottomOffset),
                BackgroundColor = Color.Transparent
            };

            // 添加到GUI
            var canvas = Actor.GetScript<UICanvas>();
            if (canvas?.GUI != null)
            {
                canvas.GUI.AddChild(_skillBarPanel);
            }
            else
            {
                Debug.LogWarning("[SkillBarUI] 未找到UICanvas组件");
                return;
            }

            // 创建技能槽位
            for (int i = 0; i < SkillSlotCount; i++)
            {
                CreateSkillSlot(i);
            }
        }

        /// <summary>
        /// 创建技能槽位
        /// </summary>
        private void CreateSkillSlot(int index)
        {
            var slot = new SkillSlot
            {
                SlotIndex = index
            };

            float xPos = index * (SlotSize + SlotSpacing);

            // 槽位面板（底板）
            slot.SlotPanel = new Panel
            {
                Bounds = new Rectangle(xPos, 0, SlotSize, SlotSize),
                BackgroundColor = new Color(0.15f, 0.15f, 0.2f, 0.9f)
            };
            _skillBarPanel.AddChild(slot.SlotPanel);

            // 技能图标
            slot.IconImage = new Image
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(2, 2, 2, 2),
                Brush = new TextureBrush(),
                KeepAspectRatio = true,
                Color = Color.White
            };
            slot.SlotPanel.AddChild(slot.IconImage);

            // 冷却遮罩
            slot.CooldownOverlay = new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                BackgroundColor = new Color(0f, 0f, 0f, 0.6f),
                Visible = false
            };
            slot.SlotPanel.AddChild(slot.CooldownOverlay);

            // 冷却文本
            slot.CooldownText = new Label
            {
                AnchorPreset = AnchorPresets.MiddleCenter,
                Offsets = new Margin(-20, -12, -20, -12),
                Size = new Float2(40, 24),
                Text = "",
                TextColor = Color.White,
                TextColorHighlighted = Color.White,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Visible = false
            };
            slot.SlotPanel.AddChild(slot.CooldownText);

            // 快捷键标签
            if (ShowHotkeys)
            {
                slot.HotkeyLabel = new Label
                {
                    Bounds = new Rectangle(2, 2, 20, 16),
                    Text = GetHotkeyText(index),
                    TextColor = Color.Yellow,
                    TextColorHighlighted = Color.Yellow,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Near
                };
                slot.SlotPanel.AddChild(slot.HotkeyLabel);
            }

            // 能量消耗标签
            slot.EnergyCostLabel = new Label
            {
                Bounds = new Rectangle(2, SlotSize - 18, SlotSize - 4, 16),
                Text = "",
                TextColor = new Color(0.3f, 0.6f, 1.0f),
                TextColorHighlighted = new Color(0.3f, 0.6f, 1.0f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Far
            };
            slot.SlotPanel.AddChild(slot.EnergyCostLabel);

            _skillSlots.Add(slot);
        }

        /// <summary>
        /// 获取快捷键文本
        /// </summary>
        private string GetHotkeyText(int index)
        {
            if (index < 9)
                return (index + 1).ToString();
            else if (index == 9)
                return "0";
            else
                return "";
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
                if (_characterAttributes == null)
                {
                    Debug.LogWarning("[SkillBarUI] 玩家角色未找到CharacterAttributesComponent");
                }
            }
            else
            {
                Debug.LogWarning("[SkillBarUI] 未找到玩家角色Actor");
            }
        }

        #endregion

        #region 技能绑定

        /// <summary>
        /// 绑定技能到槽位
        /// </summary>
        public void BindSkillToSlot(int slotIndex, SkillBase skill)
        {
            if (slotIndex < 0 || slotIndex >= _skillSlots.Count)
            {
                Debug.LogWarning($"[SkillBarUI] 无效的槽位索引: {slotIndex}");
                return;
            }

            var slot = _skillSlots[slotIndex];
            slot.BoundSkill = skill;

            if (skill != null && skill.Data != null)
            {
                // 更新技能图标（TODO：加载实际图标纹理）
                slot.IconImage.Color = GetElementColor(skill.Data.Element);
                
                // 更新能量消耗显示
                slot.EnergyCostLabel.Text = skill.Data.EnergyCost.ToString("F0");
                
                Debug.Log($"[SkillBarUI] 技能 {skill.Data.SkillName} 绑定到槽位 {slotIndex}");
            }
            else
            {
                // 清空槽位
                slot.IconImage.Color = Color.Gray;
                slot.EnergyCostLabel.Text = "";
            }
        }

        /// <summary>
        /// 解绑槽位技能
        /// </summary>
        public void UnbindSkill(int slotIndex)
        {
            BindSkillToSlot(slotIndex, null);
        }

        /// <summary>
        /// 获取五行元素颜色
        /// </summary>
        private Color GetElementColor(WuxingElement element)
        {
            switch (element)
            {
                case WuxingElement.Metal: return new Color(0.8f, 0.8f, 0.8f);    // 金：银白色
                case WuxingElement.Wood: return new Color(0.2f, 0.8f, 0.2f);     // 木：绿色
                case WuxingElement.Water: return new Color(0.2f, 0.4f, 1.0f);    // 水：蓝色
                case WuxingElement.Fire: return new Color(1.0f, 0.3f, 0.1f);     // 火：红色
                case WuxingElement.Earth: return new Color(0.8f, 0.6f, 0.2f);    // 土：土黄色
                default: return Color.White;
            }
        }

        #endregion

        #region 更新逻辑

        /// <summary>
        /// 更新技能槽位状态
        /// </summary>
        private void UpdateSkillSlots()
        {
            foreach (var slot in _skillSlots)
            {
                if (slot.BoundSkill == null) continue;

                UpdateSlotCooldown(slot);
                UpdateSlotEnergyState(slot);
            }
        }

        /// <summary>
        /// 更新槽位冷却显示
        /// </summary>
        private void UpdateSlotCooldown(SkillSlot slot)
        {
            if (slot.BoundSkill == null) return;

            float cooldownProgress = slot.BoundSkill.GetCooldownProgress();
            bool isReady = slot.BoundSkill.IsReady();

            // 更新冷却遮罩
            if (!isReady)
            {
                slot.CooldownOverlay.Visible = true;
                slot.CooldownText.Visible = true;

                // 计算剩余冷却时间
                float remainingCooldown = slot.BoundSkill.Data.Cooldown * (1f - cooldownProgress);
                slot.CooldownText.Text = remainingCooldown.ToString("F1");

                // 更新遮罩高度（从上到下）
                float overlayHeight = SlotSize * (1f - cooldownProgress);
                slot.CooldownOverlay.Offsets = new Margin(0, 0, 0, SlotSize - overlayHeight);
            }
            else
            {
                slot.CooldownOverlay.Visible = false;
                slot.CooldownText.Visible = false;
            }

            slot.IsReady = isReady;
            slot.CooldownProgress = cooldownProgress;
        }

        /// <summary>
        /// 更新槽位能量状态
        /// </summary>
        private void UpdateSlotEnergyState(SkillSlot slot)
        {
            if (slot.BoundSkill == null || _characterAttributes == null) return;

            float currentEnergy = _characterAttributes.CurrentEnergy;
            float requiredEnergy = slot.BoundSkill.Data.EnergyCost;

            // 能量不足时变红
            if (currentEnergy < requiredEnergy)
            {
                slot.EnergyCostLabel.TextColor = Color.Red;
                slot.IconImage.Color = slot.IconImage.Color * 0.5f; // 变暗
            }
            else
            {
                slot.EnergyCostLabel.TextColor = new Color(0.3f, 0.6f, 1.0f);
            }
        }

        #endregion

        #region 公共接口

        /// <summary>
        /// 尝试使用技能槽位
        /// </summary>
        public bool TryUseSkill(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _skillSlots.Count)
                return false;

            var slot = _skillSlots[slotIndex];
            if (slot.BoundSkill == null)
            {
                Debug.LogWarning($"[SkillBarUI] 槽位 {slotIndex} 未绑定技能");
                return false;
            }

            if (!slot.IsReady)
            {
                Debug.LogWarning($"[SkillBarUI] 技能冷却中: {slot.BoundSkill.Data.SkillName}");
                return false;
            }

            // 尝试释放技能
            bool success = slot.BoundSkill.TryCast();
            if (success)
            {
                Debug.Log($"[SkillBarUI] 技能释放成功: {slot.BoundSkill.Data.SkillName}");
                
                // 播放技能使用动画（TODO）
                PlaySkillUseAnimation(slot);
            }

            return success;
        }

        /// <summary>
        /// 播放技能使用动画
        /// </summary>
        private void PlaySkillUseAnimation(SkillSlot slot)
        {
            if (slot?.SlotPanel == null) return;

            // 闪烁效果：短暂高亮槽位边框，然后恢复原色
            var originalColor = slot.SlotPanel.BackgroundColor;
            var highlightColor = new Color(1.0f, 0.9f, 0.3f, 0.9f);
            slot.SlotPanel.BackgroundColor = highlightColor;

            // 延迟恢复原色（通过InvokeOnUpdate在下一帧恢复）
            FlaxEngine.Scripting.InvokeOnUpdate(() =>
            {
                if (slot.SlotPanel != null)
                {
                    slot.SlotPanel.BackgroundColor = originalColor;
                }
            });

            Debug.Log($"[SkillBarUI] 播放技能使用动画: {slot.BoundSkill?.Data?.SkillName}");
        }

        /// <summary>
        /// 获取技能槽位数量
        /// </summary>
        public int GetSlotCount()
        {
            return _skillSlots.Count;
        }

        /// <summary>
        /// 获取槽位绑定的技能
        /// </summary>
        public SkillBase GetBoundSkill(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _skillSlots.Count)
                return null;

            return _skillSlots[slotIndex].BoundSkill;
        }

        #endregion

        #region 清理

        /// <summary>
        /// 清理技能栏
        /// </summary>
        private void CleanupSkillBar()
        {
            if (_skillBarPanel != null && _skillBarPanel.Parent != null)
            {
                _skillBarPanel.Parent.RemoveChild(_skillBarPanel);
                _skillBarPanel.Dispose();
            }

            _skillSlots.Clear();
        }

        #endregion
    }
}
