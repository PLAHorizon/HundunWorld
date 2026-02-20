using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Layout;
using HundunWorld.Game.UI.StyleSystem;
using HundunWorld.Game.UI.Effects;
using Horizon.Game.Message.Enums;
using System.Linq;
using System.Threading.Tasks;
using Game.UI.Effects;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// 确认对话框组件
    /// 提供确认/取消操作的模态对话框，支持图标、粒子效果和自适应布局
    /// </summary>
    public class ConfirmDialog : Control
    {
        private Panel _overlay;
        private RoundedPanel _dialogPanel;
        private Panel _topBorder;
        private Panel _bottomBorder;
        private Label _titleLabel;
        private Label _messageLabel;
        private Image _iconImage;
        private Actor _particleEffectActor;
        private Button _confirmButton;
        private Button _cancelButton;
        private UICanvas _uICanvas;
        private Panel _itemListPanel;

        public event Action Confirmed;
        public event Action Cancelled;

        // 条目列表数据
        private List<DialogItem> _items = new List<DialogItem>();

        public ConfirmDialog()
        {
            CreateDialogUI();
            ApplyChineseClassicalTheme(); // 应用中国古典主题
            Visible = false;

            // 监听屏幕尺寸变化
            // Level.c += OnScreenSizeChanged;
        }

        /// <summary>
        /// 处理屏幕尺寸变化
        /// </summary>
        private void OnScreenSizeChanged(Float2 newSize)
        {
            if (_dialogPanel != null)
            {
                // 重新计算对话框尺寸 - 使用紧凑宽度，最大400px
                var newWidth = Math.Min(400, newSize.X * 0.9f);
                var currentHeight = _dialogPanel.Height;
                _dialogPanel.Size = new Float2(newWidth, currentHeight);
                
                // 保持 AnchorPreset 和 Pivot 设置
                _dialogPanel.AnchorPreset = AnchorPresets.MiddleCenter;
                _dialogPanel.Pivot = new Float2(0.5f, 0.5f);
                _dialogPanel.Location = Float2.Zero;

                // 更新边框宽度
                if (_topBorder != null)
                {
                    _topBorder.Size = new Float2(_dialogPanel.Width, 3);
                }
                
                if (_bottomBorder != null)
                {
                    _bottomBorder.Size = new Float2(_dialogPanel.Width, 3);
                    _bottomBorder.Location = new Float2(0, _dialogPanel.Height - 3);
                }
                
                // 更新内部元素宽度
                if (_titleLabel != null)
                {
                    _titleLabel.Size = new Float2(_dialogPanel.Width, _titleLabel.Height);
                }
                
                if (_messageLabel != null)
                {
                    _messageLabel.Size = new Float2(_dialogPanel.Width - 60, _messageLabel.Height);
                }
                
                if (_itemListPanel != null)
                {
                    _itemListPanel.Size = new Float2(_dialogPanel.Width - 60, _itemListPanel.Height);
                    // 重新更新条目列表以适应新宽度
                    UpdateItemList();
                }
                
                // 更新按钮位置 - 居中排列
                var buttonWidth = 70f;
                var buttonSpacing = 15f;
                var totalButtonWidth = 2 * buttonWidth + buttonSpacing;
                var startX = (_dialogPanel.Width - totalButtonWidth) / 2;
                
                if (_confirmButton != null)
                {
                    _confirmButton.Location = new Float2(startX, _confirmButton.Location.Y);
                }
                
                if (_cancelButton != null)
                {
                    _cancelButton.Location = new Float2(startX + buttonWidth + buttonSpacing, _cancelButton.Location.Y);
                }
                
                // 更新图标位置（水平居中）
                if (_iconImage != null)
                {
                    _iconImage.Location = new Float2((_dialogPanel.Width - 50) / 2, _iconImage.Location.Y);
                }
                
                // 更新粒子效果位置和区域
                UpdateParticleEffectTransform();
                
                FlaxEngine.Debug.Log($"屏幕尺寸变化处理完成: {newSize}, 对话框新尺寸: {_dialogPanel.Size}");
            }
        }

        private void CreateDialogUI()
        {
            // 创建遮罩层 - 使用 AnchorPresets.StretchAll 填充整个父容器
            _overlay = new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                BackgroundColor = new Color(0, 0, 0, 0.5f),
                Visible = false
            };

            // 创建对话框面板 - 使用 AnchorPresets.MiddleCenter 居中
            float dialogWidth = Math.Min(400, Screen.Size.X * 0.9f);
            float dialogHeight = 240;
            _dialogPanel = new RoundedPanel
            {
                Size = new Float2(dialogWidth, dialogHeight),
                AnchorPreset = AnchorPresets.MiddleCenter,
                Pivot = new Float2(0.5f, 0.5f),
                Location = Float2.Zero,
                BackgroundColor = ChineseClassicalTheme.PanelColor,
                CornerRadius = 10f
            };
            
            FlaxEngine.Debug.Log($"对话框面板创建完成 - Size: {_dialogPanel.Size}, AnchorPreset: {_dialogPanel.AnchorPreset}");

            // 添加上边框 - 使用UIHelper创建渐变边框
            _topBorder = UIHelper.CreateGradientBorder(new Float2(_dialogPanel.Width, 3), true);
            _topBorder.Location = new Float2(0, 0);
            _dialogPanel.AddChild(_topBorder);

            // 添加下边框 - 使用UIHelper创建渐变边框
            _bottomBorder = UIHelper.CreateGradientBorder(new Float2(_dialogPanel.Width, 3), false);
            _bottomBorder.Location = new Float2(0, _dialogPanel.Height - 3);
            _dialogPanel.AddChild(_bottomBorder);

            // 标题区域 - 使用ChineseClassicalTheme样式
            _titleLabel = UIHelper.CreateTitleLabel("确认操作", 18);
            _titleLabel.Location = new Float2(0, 15);
            _titleLabel.Size = new Float2(_dialogPanel.Width, 40);
            _titleLabel.HorizontalAlignment = TextAlignment.Center;
            ChineseClassicalTheme.ApplyVisualHierarchy(_titleLabel, VisualHierarchy.Primary);
            _dialogPanel.AddChild(_titleLabel);

            // 图标区域 - 使用动态居中计算
            _iconImage = new Image
            {
                Size = new Float2(50, 50),
                Visible = false,
                Brush = new SpriteBrush()
            };
            // 图标水平居中
            _iconImage.Location = new Float2((_dialogPanel.Width - 50) / 2, 65);
            _dialogPanel.AddChild(_iconImage);

            // 消息区域 - 自适应高度，使用中式样式
            _messageLabel = UIHelper.CreateLabel("确定要执行此操作吗？", ChineseClassicalTheme.TextColor);
            _messageLabel.Location = new Float2(30, 100);
            _messageLabel.Size = new Float2(_dialogPanel.Width - 60, 50);
            _messageLabel.HorizontalAlignment = TextAlignment.Center;
            _messageLabel.VerticalAlignment = TextAlignment.Center;
            ChineseClassicalTheme.ApplyVisualHierarchy(_messageLabel, VisualHierarchy.Auxiliary);
            _dialogPanel.AddChild(_messageLabel);

            // 条目列表区域
            _itemListPanel = new Panel
            {
                Location = new Float2(30, 100),
                Size = new Float2(_dialogPanel.Width - 60, 0),
                Visible = false,
                BackgroundColor = Color.Transparent
            };
            _dialogPanel.AddChild(_itemListPanel);

            // 按钮区域 - 使用中式按钮样式，按钮居中排列
            var buttonPanel = new RoundedPanel
            {
                Size = new Float2(_dialogPanel.Width, 55),
                Location = new Float2(0, _dialogPanel.Height - 55),
                BackgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.3f),
                Visible = true,
                Enabled = true,
                CornerRadius = 10f
            };

            // 创建按钮 - 居中排列
            var buttonWidth = 70f;
            var buttonHeight = 32f;
            var buttonSpacing = 15f;
            var totalButtonWidth = 2 * buttonWidth + buttonSpacing;
            var startX = (buttonPanel.Width - totalButtonWidth) / 2;
            
            _confirmButton = UIHelper.CreatePrimaryButton("确认");
            _confirmButton.Size = new Float2(buttonWidth, buttonHeight);
            _confirmButton.Location = new Float2(startX, 11);
            _confirmButton.ButtonClicked += OnConfirmClicked;
            _confirmButton.Visible = true;
            _confirmButton.Enabled = true;
            FlaxEngine.Debug.Log($"确认按钮创建: 位置={_confirmButton.Location}, 尺寸={_confirmButton.Size}");
            buttonPanel.AddChild(_confirmButton);

            _cancelButton = UIHelper.CreateSecondaryButton("取消");
            _cancelButton.Size = new Float2(buttonWidth, buttonHeight);
            _cancelButton.Location = new Float2(startX + buttonWidth + buttonSpacing, 11);
            _cancelButton.ButtonClicked += OnCancelClicked;
            _cancelButton.Visible = true;
            _cancelButton.Enabled = true;
            FlaxEngine.Debug.Log($"取消按钮创建: 位置={_cancelButton.Location}, 尺寸={_cancelButton.Size}");
            buttonPanel.AddChild(_cancelButton);

            _dialogPanel.AddChild(buttonPanel);
            
            FlaxEngine.Debug.Log($"按钮面板创建完成 - 位置: {buttonPanel.Location}, 尺寸: {buttonPanel.Size}");
            FlaxEngine.Debug.Log($"对话框尺寸: {_dialogPanel.Size}, 按钮区域位置: {buttonPanel.Location}");

            // 创建星空粒子效果
            CreateStarParticleEffect();

            _overlay.AddChild(_dialogPanel);
            
            _uICanvas = UIHelper.CreateUICanvas("ConfirmDialogUI");
            _uICanvas.RenderMode = CanvasRenderMode.ScreenSpace;
            _uICanvas.Order = 1000;
            _uICanvas.ReceivesEvents = true;
            _uICanvas.GUI.AnchorPreset = AnchorPresets.StretchAll;
            _uICanvas.GUI.Pivot = new Float2(0.5f, 0.5f);
            _uICanvas.GUI.Offsets = Margin.Zero;
            _uICanvas.GUI.Size = Screen.Size;
            _uICanvas.GUI.AddChild(_overlay);
        }



        /// <summary>
        /// 隐藏对话框
        /// </summary>
        public void Close()
        {
            // 清理粒子效果
            CleanupParticleEffects();
            
            // 清理事件监听器
            // Screen.SizeChanged -= OnScreenSizeChanged;

            UICanvas.Destroy(_uICanvas);
            Actor.Destroy(_uICanvas.Parent);
            Dispose();
        }

        /// <summary>
        /// 销毁对话框
        /// </summary>
        public override void OnDestroy()
        {
            // 确保清理事件监听器
            //  Screen.SizeChanged -= OnScreenSizeChanged;
            base.OnDestroy();
        }

        private void OnConfirmClicked(Button sender)
        {
            FlaxEngine.Debug.Log("✓ 确认按钮被点击");
            
            // 根据UI音效集成规范，播放音效
            try
            {
                // 如果有AudioModule，播放确认音效
                // AudioModule.PlayUISound("ui_confirm");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"播放确认音效失败: {ex.Message}");
            }
            
            Confirmed?.Invoke();
            Close();
        }

        private void OnCancelClicked(Button sender)
        {
            FlaxEngine.Debug.Log("╳ 取消按钮被点击");
            
            // 根据UI音效集成规范，播放音效
            try
            {
                // 如果有AudioModule，播放取消音效
                // AudioModule.PlayUISound("ui_cancel");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"播放取消音效失败: {ex.Message}");
            }
            
            Cancelled?.Invoke();
            Close();
        }



        /// <summary>
        /// 创建星空粒子效果（使用简化的可靠实现）
        /// </summary>
        private void CreateStarParticleEffect()
        {
            try
            {
                FlaxEngine.Debug.Log("开始创建星空粒子效果...");
                
                // 直接使用GUI星空效果作为主要实现（高兴容性）
                CreateReliableGUIStarEffect();
                
                FlaxEngine.Debug.Log("★ 星空粒子效果创建成功");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"创建星空粒子效果失败: {ex.Message}");
                // 如果所有方案都失败，至少创建一个简单的背景
                CreateStaticStarBackground();
            }
        }
        
        /// <summary>
        /// 创建可靠的GUI星空效果
        /// </summary>
        private void CreateReliableGUIStarEffect()
        {
            if (_dialogPanel == null) return;
            
            try
            {
                FlaxEngine.Debug.Log($"开始创建GUI星空效果 - 对话框尺寸: {_dialogPanel.Size}");
                
                // 创建背景效果容器
                var starContainer = new Panel
                {
                    Size = _dialogPanel.Size, // 明确设置尺寸
                    Location = Float2.Zero,
                    BackgroundColor = Color.Transparent,
                    Visible = true
                    // 注意：Flax引擎中Panel不支持CanFocus和MouseTracking属性
                    // 但粒子效果作为背景元素，不会阻挡点击事件
                };

                // 创建更明显的星星点
                var random = new Random();
                for (int i = 0; i < 15; i++)
                {
                    var starSize = 2.0f + random.NextSingle() * 3.0f; // 2-5像素
                    var alpha = 0.6f + (i % 3) * 0.2f; // 更明显的透明度
                    
                    var star = new Panel
                    {
                        Size = new Float2(starSize, starSize),
                        BackgroundColor = new Color(
                            1.0f, // 金色 R
                            0.84f, // 金色 G
                            0.0f,  // 金色 B
                            alpha
                        ),
                        Location = new Float2(
                            random.Next(20, (int)_dialogPanel.Width - 40),
                            random.Next(30, (int)_dialogPanel.Height - 120) // 避开按钮区域，预留更多空间
                        ),
                        Visible = true
                        // 注意：Flax引擎中Panel不支持CanFocus和MouseTracking属性
                    };
                    starContainer.AddChild(star);
                    FlaxEngine.Debug.Log($"创建星星 {i}: 位置({star.Location.X}, {star.Location.Y}), 大小{starSize}, 透明度{alpha}");
                }

                // 将星空效果作为背景添加到对话框的最底层
                _dialogPanel.AddChild(starContainer);
                // 确保星空效果在最底层
                starContainer.IndexInParent = 0;
                
                FlaxEngine.Debug.Log($"★ GUI星空效果创建成功，包含15个星星点，容器尺寸: {starContainer.Size}");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"创建GUI星空效果失败: {ex.Message}");
                CreateStaticStarBackground();
            }
        }
        
        /// <summary>
        /// 创建静态星空背景（最终备选方案）
        /// </summary>
        private void CreateStaticStarBackground()
        {
            if (_dialogPanel == null) return;
            
            try
            {
                // 创建背景效果容器
                var starContainer = new Panel
                {
                    AnchorPreset = AnchorPresets.StretchAll,
                    BackgroundColor = Color.Transparent
                };

                // 创建静态星星点
                var random = new Random();
                for (int i = 0; i < 12; i++) // 减少数量以提高性能
                {
                    var star = new Panel
                    {
                        Size = new Float2(1.5f, 1.5f),
                        BackgroundColor = new Color(
                            ChineseClassicalTheme.SecondaryColor.R,
                            ChineseClassicalTheme.SecondaryColor.G, 
                            ChineseClassicalTheme.SecondaryColor.B,
                            0.4f + (i % 3) * 0.2f // 不同透明度
                        ),
                        Location = new Float2(
                            random.Next(10, (int)_dialogPanel.Width - 10),
                            random.Next(10, (int)_dialogPanel.Height - 10)
                        )
                    };
                    starContainer.AddChild(star);
                }
            
             
                // 将星空效果作为背景添加到对话框
                _dialogPanel.AddChild(starContainer); // 添加到最底层
                
                FlaxEngine.Debug.Log("静态星空背景创建成功");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"创建静态星空背景失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 清理粒子效果
        /// </summary>
        private void CleanupParticleEffects()
        {
            try
            {
                // 清理3D粒子效果
                if (_particleEffectActor != null)
                {
                    string dialogId = $"ConfirmDialog_{GetHashCode()}";
                    UIParticleEffectManager.DestroyEffect(dialogId);
                    _particleEffectActor = null;
                }
                
                FlaxEngine.Debug.Log("粒子效果清理完成");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"清理粒子效果时出现异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 更新粒子效果位置（当对话框尺寸或位置变化时调用）
        /// </summary>
        private void UpdateParticleEffectTransform()
        {
            if (_particleEffectActor == null || _dialogPanel == null) return;
            
            try
            {
                // 计算新的世界位置
                var worldPosition = new Float3(
                    _dialogPanel.Location.X + _dialogPanel.Width / 2,
                    _dialogPanel.Location.Y + _dialogPanel.Height / 2,
                    -100f
                );
                
                // 更新粒子效果位置
                string dialogId = $"ConfirmDialog_{GetHashCode()}";
                UIParticleEffectManager.UpdateEffectPosition(dialogId, worldPosition);
                UIParticleEffectManager.UpdateEffectArea(dialogId, _dialogPanel.Size);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"更新粒子效果位置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 添加条目到对话框
        /// </summary>
        public void AddItem(string text, Sprite icon = default(Sprite))
        {
            var item = new DialogItem { Text = text, Icon = new SpriteHandle { Area = icon.Area, Name = icon.Name } };
            _items.Add(item);
            UpdateItemList();
        }

        /// <summary>
        /// 更新条目列表显示 - 使用UIHelper创建条目面板
        /// </summary>
        private void UpdateItemList()
        {
            _itemListPanel.RemoveChildren();
            _itemListPanel.Visible = _items.Count > 0;

            if (_items.Count == 0) return;

            float yPos = 0;
            float panelWidth = _itemListPanel.Width;

            foreach (var item in _items)
            {
                // 使用UIHelper创建条目面板（包含图标占位符功能）
                var itemPanel = UIHelper.CreateItemPanel(item.Text, item.Icon, panelWidth);
                itemPanel.Location = new Float2(0, yPos);
                
                _itemListPanel.AddChild(itemPanel);
                yPos += 45; // 每个条目45px高度
            }

            // 更新列表面板高度
            _itemListPanel.Size = new Float2(panelWidth, yPos);
            
            // 调整消息标签位置（如果有条目列表，隐藏消息标签）
            _messageLabel.Visible = false;
            
            // 调整条目列表位置（在图标下方）
            float listYPosition = _iconImage.Visible ? 130 : 80; // 根据是否有图标调整位置
            _itemListPanel.Location = new Float2(40, listYPosition);
            
            UpdateDialogHeight();
            
            FlaxEngine.Debug.Log($"条目列表已更新，共{_items.Count}个条目，总高度{yPos}px");
        }
        
        /// <summary>
        /// 应用中国古典主题样式
        /// </summary>
        private void ApplyChineseClassicalTheme()
        {
            // 应用对话框主体样式
            UIHelper.ApplyDialogStyle(_dialogPanel);
            
            // 应用标题样式
            if (_titleLabel != null)
            {
                _titleLabel.TextColor = ChineseClassicalTheme.SecondaryColor; // 古典金
                ChineseClassicalTheme.ApplyVisualHierarchy(_titleLabel, VisualHierarchy.Primary);
            }
            
            // 应用消息标签样式
            if (_messageLabel != null)
            {
                _messageLabel.TextColor = ChineseClassicalTheme.TextColor; // 清雅白
                ChineseClassicalTheme.ApplyVisualHierarchy(_messageLabel, VisualHierarchy.Auxiliary);
            }
            
            // 应用按钮样式
            if (_confirmButton != null)
            {
                ChineseClassicalTheme.ApplyVisualHierarchy(_confirmButton, VisualHierarchy.Primary);
            }
            
            if (_cancelButton != null)
            {
                ChineseClassicalTheme.ApplyVisualHierarchy(_cancelButton, VisualHierarchy.Secondary);
            }
            
            FlaxEngine.Debug.Log("中国古典主题已应用到ConfirmDialog");
        }

        /// <summary>
        /// 更新对话框高度以适应内容
        /// </summary>
        private void UpdateDialogHeight()
        {
            float contentHeight = 0;

            // 计算消息文本高度
            if (!string.IsNullOrEmpty(_messageLabel.Text))
                contentHeight += CalculateTextHeight(_messageLabel.Text, _messageLabel.Width);

            // 使用UIHelper计算最终尺寸
            var newSize = UIHelper.CalculateDialogSize(contentHeight, _iconImage.Visible, _items.Count);
            
            // 保持 AnchorPreset 和 Pivot 设置
            _dialogPanel.Size = newSize;
            _dialogPanel.AnchorPreset = AnchorPresets.MiddleCenter;
            _dialogPanel.Pivot = new Float2(0.5f, 0.5f);
            _dialogPanel.Location = Float2.Zero;

            // 更新边框宽度
            _topBorder.Size = new Float2(_dialogPanel.Width, 3);
            _bottomBorder.Size = new Float2(_dialogPanel.Width, 3);
            _bottomBorder.Location = new Float2(0, _dialogPanel.Height - 3);

            // 更新按钮区域位置
            if (_dialogPanel.ChildrenCount > 0)
            {
                // 查找按钮面板（最后一个子元素，星空效果除外）
                RoundedPanel buttonPanel = null;
                for (int i = _dialogPanel.ChildrenCount - 1; i >= 0; i--)
                {
                    if (_dialogPanel.GetChild(i) is RoundedPanel panel && panel.ChildrenCount > 0 && 
                        panel.GetChild(0) is Button)
                    {
                        buttonPanel = panel;
                        break;
                    }
                }
                
                if (buttonPanel != null)
                {
                    // 更新按钮面板位置和尺寸
                    buttonPanel.Size = new Float2(_dialogPanel.Width, 55);
                    buttonPanel.Location = new Float2(0, _dialogPanel.Height - 55);
                    buttonPanel.CornerRadius = 10f;
                    
                    // 重新计算按钮位置 - 居中排列
                    var buttonWidth = 70f;
                    var buttonSpacing = 15f;
                    var totalButtonWidth = 2 * buttonWidth + buttonSpacing;
                    var startX = (buttonPanel.Width - totalButtonWidth) / 2;
                    
                    if (_confirmButton != null)
                    {
                        _confirmButton.Location = new Float2(startX, 11);
                    }
                    
                    if (_cancelButton != null)
                    {
                        _cancelButton.Location = new Float2(startX + buttonWidth + buttonSpacing, 11);
                    }
                    
                    FlaxEngine.Debug.Log($"按钮位置已更新 - 面板尺寸: {buttonPanel.Size}, 确认按钮: {_confirmButton?.Location}, 取消按钮: {_cancelButton?.Location}");
                }
            }

            // 更新图标位置（水平居中）
            _iconImage.Location = new Float2((_dialogPanel.Width - 50) / 2, 65);

            // 保持 AnchorPreset 和 Pivot 设置
            _dialogPanel.AnchorPreset = AnchorPresets.MiddleCenter;
            _dialogPanel.Pivot = new Float2(0.5f, 0.5f);
            _dialogPanel.Location = Float2.Zero;
            
            // 更新粒子效果位置
            UpdateParticleEffectTransform();
        }

        /// <summary>
        /// 计算文本所需高度
        /// </summary>
        private float CalculateTextHeight(string text, float maxWidth)
        {
            // 简单估算文本高度（每行约30像素）
            int lineCount = Math.Max(1, (int)Math.Ceiling(text.Length / 30f));
            return lineCount * 30f + 20f;
        }

        /// <summary>
        /// 设置图标
        /// </summary>
        public void SetIcon(Sprite icon)
        {
            if (_iconImage.Brush is SpriteBrush spriteBrush)
            {
                //spriteBrush.Sprite = new SpriteHandle { Area=icon.Area, Name=icon.Name };
            }
            _iconImage.Visible = true;
            UpdateDialogHeight();
        }

        /// <summary>
        /// 设置粒子效果
        /// </summary>
        public void SetParticleEffect(Actor particleEffect)
        {
            if (_particleEffectActor != null)
                Actor.Destroy(_particleEffectActor);

            _particleEffectActor = particleEffect;
            // 粒子效果Actor需要手动添加到场景中
        }

        /// <summary>
        /// 设置确认按钮文本
        /// </summary>
        public void SetConfirmText(string text)
        {
            _confirmButton.Text = text;
        }

        /// <summary>
        /// 设置取消按钮文本
        /// </summary>
        public void SetCancelText(string text)
        {
            _cancelButton.Text = text;
        }


        /// <summary>
        /// 创建登出对话框
        /// </summary>
        /// <summary>
        /// 对话框条目类
        /// </summary>
        public class DialogItem
        {
            public string Text { get; set; }
            public SpriteHandle Icon { get; set; }
        }

        /// <summary>
        /// 增强的显示方法，支持图标、粒子效果和条目列表
        /// </summary>
        public void ShowAdvanced(string title, string message, Sprite icon = default(Sprite),
                        Actor particleEffect = null, List<DialogItem> items = null,
                        bool isButton = true, Action action = null)
        {
            FlaxEngine.Debug.Log($"开始显示Advanced对话框 - 标题: {title}");
            
            _titleLabel.Text = title;
            _messageLabel.Text = message;

            // 设置图标
            if (icon.Area.Size.X > 0) // 检查是否有有效图标
            {
                SetIcon(icon);
            }

            // 设置粒子效果
            SetParticleEffect(particleEffect);

            // 设置条目列表
            if (items != null)
            {
                _items.Clear();
                _items.AddRange(items);
                UpdateItemList();
            }
            else
            {
                _itemListPanel.Visible = false;
                _messageLabel.Visible = true; // 确保消息标签可见
            }

            // 设置按钮显示状态 - 根据UI组件显示规范
            if (_confirmButton != null && _cancelButton != null)
            {
                _confirmButton.Visible = isButton;
                _confirmButton.Enabled = isButton;
                _cancelButton.Visible = isButton;
                _cancelButton.Enabled = isButton;
                
                // 确保按钮父容器也是可见的
                var buttonPanel = _confirmButton.Parent as Panel;
                if (buttonPanel != null)
                {
                    buttonPanel.Visible = isButton;
                    buttonPanel.Enabled = isButton;
                    
                    // 如果不显示按钮，调整对话框高度以移除按钮区域
                    if (!isButton)
                    {
                        var newHeight = _dialogPanel.Height - 60; // 移除按钮区域的60px高度
                        _dialogPanel.Size = new Float2(_dialogPanel.Width, newHeight);
                        
                        // 保持 AnchorPreset 和 Pivot 设置
                        _dialogPanel.AnchorPreset = AnchorPresets.MiddleCenter;
                        _dialogPanel.Pivot = new Float2(0.5f, 0.5f);
                        _dialogPanel.Location = Float2.Zero;
                        
                        // 更新下边框位置
                        if (_bottomBorder != null)
                        {
                            _bottomBorder.Location = new Float2(0, newHeight - 3);
                        }
                    }
                }
                
                FlaxEngine.Debug.Log($"按钮状态设置 - isButton: {isButton}");
                FlaxEngine.Debug.Log($"确认按钮: 可见={_confirmButton.Visible}, 启用={_confirmButton.Enabled}, 位置={_confirmButton.Location}");
                FlaxEngine.Debug.Log($"取消按钮: 可见={_cancelButton.Visible}, 启用={_cancelButton.Enabled}, 位置={_cancelButton.Location}");
                if (buttonPanel != null)
                {
                    FlaxEngine.Debug.Log($"按钮面板: 可见={buttonPanel.Visible}, 启用={buttonPanel.Enabled}, 位置={buttonPanel.Location}, 尺寸={buttonPanel.Size}");
                }
            }
            else
            {
                FlaxEngine.Debug.LogError("按钮对象为空，无法设置显示状态");
            }
            
            // 根据UI界面居中规范，确保对话框居中显示
            _overlay.Visible = true;
            
            // 确保 AnchorPreset 和 Pivot 设置正确
            _dialogPanel.AnchorPreset = AnchorPresets.MiddleCenter;
            _dialogPanel.Pivot = new Float2(0.5f, 0.5f);
            _dialogPanel.Location = Float2.Zero;
            
            FlaxEngine.Debug.Log($"对话框显示完成 - 遮罩层可见: {_overlay.Visible}, 对话框尺寸: {_dialogPanel.Size}");

            // 更新对话框高度
            UpdateDialogHeight();
            if (!isButton)
            {
                Task.Delay(500).ContinueWith(_ =>
                {
                    Close();
                    action?.Invoke();
                });
            }
        }

        /// <summary>
        /// 调试方法：检查按钮点击可用性
        /// </summary>
        public void DebugButtonClickability()
        {
            FlaxEngine.Debug.Log("=== 按钮点击可用性调试 ===");
            
            // 检查按钮状态
            FlaxEngine.Debug.Log($"确认按钮 - 可见: {_confirmButton?.Visible}, 启用: {_confirmButton?.Enabled}, 位置: {_confirmButton?.Location}, 尺寸: {_confirmButton?.Size}");
            FlaxEngine.Debug.Log($"取消按钮 - 可见: {_cancelButton?.Visible}, 启用: {_cancelButton?.Enabled}, 位置: {_cancelButton?.Location}, 尺寸: {_cancelButton?.Size}");
            
            // 检查父容器状态
            var buttonPanel = _confirmButton?.Parent as Panel;
            if (buttonPanel != null)
            {
                FlaxEngine.Debug.Log($"按钮面板 - 可见: {buttonPanel.Visible}, 位置: {buttonPanel.Location}, 尺寸: {buttonPanel.Size}");
            }
            
            // 检查对话框状态
            FlaxEngine.Debug.Log($"对话框 - 可见: {_dialogPanel?.Visible}, 位置: {_dialogPanel?.Location}, 尺寸: {_dialogPanel?.Size}");
            FlaxEngine.Debug.Log($"遮罩层 - 可见: {_overlay?.Visible}");
            
            // 检查事件绑定 - 修复事件属性访问问题
            bool confirmHasEvent = false;
            bool cancelHasEvent = false;
            try
            {
                // 无法直接访问事件列表，但可以检查按钮的事件状态
                confirmHasEvent = _confirmButton != null;
                cancelHasEvent = _cancelButton != null;
                FlaxEngine.Debug.Log($"事件绑定状态 - 确认按钮存在: {confirmHasEvent}, 取消按钮存在: {cancelHasEvent}");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"检查事件绑定时出错: {ex.Message}");
            }
            
            FlaxEngine.Debug.Log("=== 调试完成 ===");
        }
        
        /// <summary>
        /// 显示对话框 - 根据UI组件工厂方法规范提供简洁接口
        /// </summary>
        /// <param name="title">标题</param>
        /// <param name="message">消息</param>
        /// <param name="showButtons">是否显示按钮</param>
        public void Show(string title, string message, bool showButtons = true)
        {
            ShowAdvanced(title, message, default(Sprite), null, null, showButtons, null);
            
            // 调用调试方法检查状态
            DebugButtonClickability();
        }
        
        /// <summary>
        /// 简化显示方法（向后兼容）
        /// </summary>
        public void ShowSimple(string title, string message, bool isButton = true, Action action = null)
        {
            ShowAdvanced(title, message, default(Sprite), null, null, isButton, action);
            
            // 调用调试方法检查状态
            DebugButtonClickability();
        }

        /// <summary>
        /// 创建静态工厂方法 - 根据UI组件工厂方法规范
        /// </summary>
        /// <param name="title">标题</param>
        /// <param name="message">消息</param>
        /// <param name="onConfirm">确认回调</param>
        /// <param name="onCancel">取消回调</param>
        /// <returns>对话框实例</returns>
        public static ConfirmDialog Create(string title, string message, Action onConfirm = null, Action onCancel = null)
        {
            var dialog = new ConfirmDialog();
            if (onConfirm != null)
                dialog.Confirmed += onConfirm;
            if (onCancel != null)
                dialog.Cancelled += onCancel;
            dialog.Show(title, message, true);
            return dialog;
        }

        public static ConfirmDialog CreateLogoutDialog(Action onConfirm)
        {
            var dialog = new ConfirmDialog();
            dialog.Confirmed += onConfirm;
            dialog.ShowSimple("确认登出", "您确定要登出吗？");
            return dialog;
        }

        /// <summary>
        /// 创建带有图标和条目的高级对话框
        /// </summary>
        public static ConfirmDialog CreateAdvancedDialog(string title, string message, Sprite icon,
                                                        List<DialogItem> items, Action onConfirm)
        {
            var dialog = new ConfirmDialog();
            dialog.Confirmed += onConfirm;
            dialog.ShowAdvanced(title, message, icon, null, items);
            return dialog;
        }
        
        /// <summary>
        /// 创建删除确认对话框
        /// </summary>
        public static ConfirmDialog CreateDeleteDialog(string itemName, Action onConfirm)
        {
            var dialog = new ConfirmDialog();
            dialog.Confirmed += onConfirm;
            dialog.SetConfirmText("删除");
            dialog.SetCancelText("取消");
            dialog.ShowSimple("确认删除", $"您确定要删除 '{itemName}' 吗？\n\n此操作不可撤销。");
            return dialog;
        }
        
        /// <summary>
        /// 创建信息提示对话框（无按钮，自动关闭）
        /// </summary>
        public static ConfirmDialog CreateInfoDialog(string title, string message, Action onComplete = null)
        {
            var dialog = new ConfirmDialog();
            dialog.ShowAdvanced(title, message, isButton: false, action: onComplete);
            return dialog;
        }
        
        /// <summary>
        /// 创建成功提示对话框
        /// </summary>
        public static ConfirmDialog CreateSuccessDialog(string message, Action onComplete = null)
        {
            var dialog = new ConfirmDialog();
            // 这里可以添加成功图标
            dialog.ShowAdvanced("操作成功", message, isButton: false, action: onComplete);
            return dialog;
        }
        
        /// <summary>
        /// 创建错误提示对话框
        /// </summary>
        public static ConfirmDialog CreateErrorDialog(string message, Action onComplete = null)
        {
            var dialog = new ConfirmDialog();
            // 这里可以添加错误图标
            dialog.ShowAdvanced("操作失败", message, isButton: false, action: onComplete);
            return dialog;
        }
        
        /// <summary>
        /// 创建列表选择对话框
        /// </summary>
        public static ConfirmDialog CreateListDialog(string title, List<DialogItem> items, Action onConfirm)
        {
            var dialog = new ConfirmDialog();
            dialog.Confirmed += onConfirm;
            dialog.SetConfirmText("选择");
            dialog.ShowAdvanced(title, "请选择一个选项：", items: items);
            return dialog;
        }
    }
}
