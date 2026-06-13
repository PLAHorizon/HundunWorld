using FlaxEngine;
using FlaxEngine.GUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HundunWorld.Game.UI.Components;
using HundunWorld.Game.UI.StyleSystem;
using Horizon.Game.Message.Enums;

namespace HundunWorld.Game.UI
{
    /// <summary>
    /// UI辅助工具类
    /// 提供常用的UI创建和样式设置功能
    /// </summary>
    public static class UIHelper
    {
        // 默认样式配置 - 使用中国古典风格
        public static readonly Color PrimaryColor = ChineseClassicalTheme.SecondaryColor; // 古典金
        public static readonly Color SecondaryColor = ChineseClassicalTheme.PrimaryColor; // 墨青色
        public static readonly Color DangerColor = ChineseClassicalTheme.AccentColor; // 朱砂红
        public static readonly Color InfoColor = new Color(0.2f, 0.4f, 0.8f);
        public static readonly Color BackgroundColor = ChineseClassicalTheme.BackgroundColor; // 雅致灰
        public static readonly Color PanelColor = ChineseClassicalTheme.PanelColor; // 青石色
        public static readonly Color InputColor = ChineseClassicalTheme.InputColor; // 深青色

        // 字体配置
        public static FontReference TitleFont => SetFont("Content/Fonts/Source_Han_Serif_SC_Light_Light.flax", 20);
        public static FontReference DefaultFont => SetFont("Content/Fonts/Source_Han_Serif_SC_Light_Light.flax", 12);

        /// <summary>
        /// 设置字体
        /// </summary>
        /// <param name="fontPath">字体全名称相对路径</param>
        /// <param name="size">字体大小</param>
        /// <returns></returns>
        public static FontReference SetFont(string fontPath = "Content/Fonts/Source_Han_Serif_SC_Light_Light.flax", float size = 12)
        {
            return new FontReference(Content.LoadAsync<FontAsset>(fontPath), size);
        }

        /// <summary>
        /// 创建标准按钮 - 应用中国古典风格
        /// </summary>
        public static Button CreateButton(string text, Color? backgroundColor = null, Color? textColor = null)
        {
            var button = new Button
            {
                Text = text,
                BackgroundColor = backgroundColor ?? PrimaryColor,
                TextColor = textColor ?? ChineseClassicalTheme.TextColor,
                Size = ChineseClassicalTheme.GoldenRatioLayout.CalculateButtonSize(ButtonType.Primary),
                Font = DefaultFont
            };

            // 应用中式样式
            ChineseClassicalTheme.ApplyVisualHierarchy(button, VisualHierarchy.Primary);
            return button;
        }

        /// <summary>
        /// 创建标准按钮 - 支持自定义尺寸
        /// </summary>
        public static Button CreateButton(string text, Color? backgroundColor, Color? textColor, Float2 size)
        {
            var button = new Button
            {
                Text = text,
                BackgroundColor = backgroundColor ?? PrimaryColor,
                TextColor = textColor ?? ChineseClassicalTheme.TextColor,
                Size = size,
                Font = DefaultFont
            };

            // 应用中式样式
            ChineseClassicalTheme.ApplyVisualHierarchy(button, VisualHierarchy.Primary);
            return button;
        }

        /// <summary>
        /// 创建主要按钮 - 使用古典金色
        /// </summary>
        public static Button CreatePrimaryButton(string text)
        {
            var button = CreateButton(text, ChineseClassicalTheme.SecondaryColor, Color.Black);
            ChineseClassicalTheme.ApplyVisualHierarchy(button, VisualHierarchy.Primary);
            return button;
        }

        /// <summary>
        /// 创建次要按钮 - 使用墨青色
        /// </summary>
        public static Button CreateSecondaryButton(string text)
        {
            var button = CreateButton(text, ChineseClassicalTheme.PrimaryColor, ChineseClassicalTheme.TextColor);
            ChineseClassicalTheme.ApplyVisualHierarchy(button, VisualHierarchy.Secondary);
            return button;
        }

        /// <summary>
        /// 创建危险按钮 - 使用朱砂红
        /// </summary>
        public static Button CreateDangerButton(string text)
        {
            var button = CreateButton(text, ChineseClassicalTheme.AccentColor, ChineseClassicalTheme.TextColor);
            ChineseClassicalTheme.ApplyVisualHierarchy(button, VisualHierarchy.Primary);
            return button;
        }

        /// <summary>
        /// 创建标准进度条 - 支持圆角样式
        /// </summary>
        public static RoundedProgressBar CreateProgressBar(float value = 0, float cornerRadius = 10f)
        {
            var progressBar = new RoundedProgressBar
            {
                Value = value,
                CornerRadius = cornerRadius,
                BackgroundColor = BackgroundColor,
                BarColor = PrimaryColor,
                Size = new Float2(200, 20)
            };
            return progressBar;
        }

        /// <summary>
        /// 创建圆角面板
        /// </summary>
        public static RoundedPanel CreateRoundedPanel(Float2 size, float cornerRadius = 10f, Color? backgroundColor = null)
        {
            var panel = new RoundedPanel
            {
                Size = size,
                CornerRadius = cornerRadius,
                BackgroundColor = backgroundColor ?? ChineseClassicalTheme.PanelColor
            };

            // 应用中式边框装饰
            ChineseClassicalTheme.ApplyChineseBorder(panel, ChineseBorderStyle.Elegant);
            return panel;
        }

        /// <summary>
        /// 创建标准面板 - 应用中式装饰
        /// </summary>
        public static Panel CreatePanel(Float2 size, Color? backgroundColor = null)
        {
            var panel = new Panel
            {
                Size = size,
                BackgroundColor = backgroundColor ?? ChineseClassicalTheme.PanelColor
            };

            // 应用中式边框装饰
            ChineseClassicalTheme.ApplyChineseBorder(panel, ChineseBorderStyle.Elegant);
            return panel;
        }

        /// <summary>
        /// 创建标准输入框 - 应用中式样式
        /// </summary>
        public static TextBox CreateTextBox(string watermark = "", bool isPassword = false)
        {
            var textBox = new TextBox
            {
                WatermarkText = watermark,
                ObfuscateText = isPassword,
                BackgroundColor = ChineseClassicalTheme.InputColor,
                TextColor = ChineseClassicalTheme.TextColor,
                Size = new Float2(200, 30),
                Font = DefaultFont
            };

            ChineseClassicalTheme.ApplyVisualHierarchy(textBox, VisualHierarchy.Tertiary);
            return textBox;
        }

        /// <summary>
        /// 创建标题标签 - 使用中式样式
        /// </summary>
        public static Label CreateTitleLabel(string text, float fontSize = 16)
        {
            var label = new Label
            {
                Text = text,
                Font = SetFont(size: fontSize),
                TextColor = ChineseClassicalTheme.SecondaryColor, // 使用古典金色
                HorizontalAlignment = TextAlignment.Center
            };

            ChineseClassicalTheme.ApplyVisualHierarchy(label, VisualHierarchy.Primary);
            return label;
        }

        /// <summary>
        /// 创建图标 - 使用Texture加载纹理
        /// </summary>
        public static Image CreateIcon(string path, float size = 0, float rotation = 0)
        {
            // 验证路径是否有效
            if (string.IsNullOrEmpty(path))
            {
                FlaxEngine.Debug.LogError("图标路径不能为空");
                return null;
            }

            var image = new Image
            {
                KeepAspectRatio = true,
                Color = Color.White,
                Rotation = rotation,
                Visible = true
            };

            try
            {
                // 尝试加载纹理
                var texture = Content.LoadAsync<Texture>(path);
                if (texture != null && texture.IsLoaded)
                {
                    image.Brush = new TextureBrush(texture); // 直接设置Texture属性
                }
                else
                {
                    // 等待异步加载完成
                    texture.WaitForLoaded();
                    if (texture.IsLoaded)
                    {
                        image.Brush = new TextureBrush(texture);
                    }
                    else
                    {
                        FlaxEngine.Debug.LogWarning($"无法加载纹理: {path}");
                        // 保持默认状态
                    }
                }
                image.Size = size == 0 ? new Float2(texture.Width, texture.Height) : new Float2(size);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"加载纹理时出错: {ex.Message}");
                // 保持默认状态
            }

            return image;
        }

        /// <summary>
        /// 创建标准标签 - 使用中式样式
        /// </summary>
        public static Label CreateLabel(string text, Color? textColor = null)
        {
            var label = new Label
            {
                Text = text,
                TextColor = textColor ?? ChineseClassicalTheme.TextColor,
                HorizontalAlignment = TextAlignment.Near,
                Font = DefaultFont
            };

            ChineseClassicalTheme.ApplyVisualHierarchy(label, VisualHierarchy.Auxiliary);
            return label;
        }

        /// <summary>
        /// 创建加载指示器
        /// </summary>
        public static LoadingIndicator CreateLoadingIndicator()
        {
            return new LoadingIndicator();
        }

        /// <summary>
        /// 创建确认对话框
        /// </summary>
        public static ConfirmDialog CreateConfirmDialog(string title, string message, Action onConfirm = null, bool isButton = true)
        {
            var dialog = new ConfirmDialog();
            dialog.ShowAdvanced(title, message, isButton: isButton, action: onConfirm);
            if (onConfirm != null)
                dialog.Confirmed += onConfirm;
            return dialog;
        }

        /// <summary>
        /// 创建渐变边框Panel
        /// </summary>
        /// <param name="size">边框尺寸</param>
        /// <param name="isTopBorder">是否为上边框</param>
        /// <returns>带渐变效果的边框Panel</returns>
        public static Panel CreateGradientBorder(Float2 size, bool isTopBorder)
        {
            var borderPanel = new Panel
            {
                Size = size,
                BackgroundColor = isTopBorder
                    ? ChineseClassicalTheme.PrimaryColor  // 上边框：黛青色
                    : ChineseClassicalTheme.SecondaryColor // 下边框：古典金
            };

            // 应用渐变效果（这里使用半透明模拟渐变）
            var gradientColor = isTopBorder
                ? new Color(ChineseClassicalTheme.PrimaryColor.R, ChineseClassicalTheme.PrimaryColor.G, ChineseClassicalTheme.PrimaryColor.B, 0.8f)
                : new Color(ChineseClassicalTheme.SecondaryColor.R, ChineseClassicalTheme.SecondaryColor.G, ChineseClassicalTheme.SecondaryColor.B, 0.8f);

            borderPanel.BackgroundColor = gradientColor;
            return borderPanel;
        }

        /// <summary>
        /// 应用对话框专用样式
        /// </summary>
        /// <param name="panel">对话框面板</param>
        public static void ApplyDialogStyle(ContainerControl panel)
        {
            panel.BackgroundColor = ChineseClassicalTheme.PanelColor;
            ChineseClassicalTheme.ApplyChineseBorder(panel, ChineseBorderStyle.Elegant);
        }

        /// <summary>
        /// 计算对话框适合尺寸
        /// </summary>
        /// <param name="contentHeight">内容高度</param>
        /// <param name="hasIcon">是否有图标</param>
        /// <param name="itemCount">条目数量</param>
        /// <returns>计算后的对话框尺寸</returns>
        public static Float2 CalculateDialogSize(float contentHeight, bool hasIcon = false, int itemCount = 0)
        {
            float baseHeight = 180; // 基础高度（标题+按钮+边距）
            float totalHeight = baseHeight + contentHeight;

            if (hasIcon)
                totalHeight += 60; // 图标区域高度

            if (itemCount > 0)
                totalHeight += itemCount * 40 + 15; // 条目列表高度

            var width = Math.Min(400, Screen.Size.X * 0.9f); // 最大宽度400px，不超过屏幕90%
            var height = Math.Max(240, totalHeight); // 最小高度240px

            return new Float2(width, height);
        }

        /// <summary>
        /// 创建条目面板
        /// </summary>
        /// <param name="text">条目文本</param>
        /// <param name="icon">条目图标</param>
        /// <param name="width">面板宽度</param>
        /// <returns>条目面板</returns>
        public static Panel CreateItemPanel(string text, SpriteHandle? icon, float width)
        {
            var itemPanel = new Panel
            {
                Size = new Float2(width, 40),
                BackgroundColor = Color.Transparent
            };

            // 图标区域（包含占位符）
            if (icon.HasValue && icon.Value.IsValid)
            {
                var iconImage = new Image
                {
                    Location = new Float2(0, 5),
                    Size = new Float2(30, 30),
                    Brush = new SpriteBrush { Sprite = icon.Value }
                };
                itemPanel.AddChild(iconImage);
            }
            else
            {
                // 创建图标占位符
                var placeholder = new Panel
                {
                    Location = new Float2(0, 5),
                    Size = new Float2(30, 30),
                    BackgroundColor = new Color(ChineseClassicalTheme.SecondaryColor.R, ChineseClassicalTheme.SecondaryColor.G, ChineseClassicalTheme.SecondaryColor.B, 0.3f)
                };
                itemPanel.AddChild(placeholder);
            }

            // 文字标签
            var itemLabel = CreateLabel(text, ChineseClassicalTheme.TextColor);
            itemLabel.Location = new Float2(40, 5);
            itemLabel.Size = new Float2(width - 50, 30);
            itemLabel.HorizontalAlignment = TextAlignment.Near;
            itemPanel.AddChild(itemLabel);

            return itemPanel;
        }

        /// <summary>
        /// Toast管理器（模拟实现）
        /// </summary>
        public static ToastManager ToastManager = new ToastManager();

        /// <summary>
        /// 显示成功消息
        /// </summary>
        public static void ShowSuccess(string message)
        {
            FlaxEngine.Debug.Log($"[成功] {message}");
            // 在实际项目中，这里应该显示UI提示
            ToastManager.ShowSuccess(message);
        }

        /// <summary>
        /// 显示错误消息
        /// </summary>
        public static void ShowError(string message)
        {
            FlaxEngine.Debug.LogError($"[错误] {message}");
            // 在实际项目中，这里应该显示UI提示
            ToastManager.ShowError(message);
        }

        /// <summary>
        /// 显示信息消息
        /// </summary>
        public static void ShowInfo(string message)
        {
            FlaxEngine.Debug.Log($"[信息] {message}");
            // 在实际项目中，这里应该显示UI提示
            ToastManager.ShowInfo(message);
        }

        /// <summary>
        /// 显示Toast消息
        /// </summary>
        public static void ShowToast(string message, ToastType toastType = ToastType.Info)
        {
            ToastManager.ShowToast(message, toastType);
        }


        /// <summary>
        /// 设置控件的标准样式
        /// </summary>
        public static void ApplyStandardStyle(Control control)
        {
            if (control is Button button)
            {
                button.BackgroundColor = PrimaryColor;
                button.TextColor = Color.White;
            }
            else if (control is TextBox textBox)
            {
                textBox.BackgroundColor = InputColor;
                textBox.TextColor = Color.White;
            }
            else if (control is Panel panel)
            {
                panel.BackgroundColor = PanelColor;
            }
            else if (control is Label label)
            {
                label.TextColor = Color.White;
            }
        }

        /// <summary>
        /// 批量应用标准样式
        /// </summary>
        public static void ApplyStandardStyles(params Control[] controls)
        {
            foreach (var control in controls)
            {
                ApplyStandardStyle(control);
            }
        }
        /// <summary>
        /// 为Panel应用边框样式（通过表演实现）
        /// </summary>
        public static void ApplyPanelBorder(Panel panel, Color borderColor)
        {
            // 由于Flax引擎的Panel不支持BorderColor，这里用BackgroundColor来模拟
            // 在实际项目中可以使用其他方式实现边框效果
            var currentBg = panel.BackgroundColor;
            panel.BackgroundColor = new Color(
                Mathf.Lerp(currentBg.R, borderColor.R, 0.1f),
                Mathf.Lerp(currentBg.G, borderColor.G, 0.1f),
                Mathf.Lerp(currentBg.B, borderColor.B, 0.1f),
                currentBg.A
            );
        }

        /// <summary>
        /// 为Label应用边框样式（通过表演实现）
        /// </summary>
        public static void ApplyLabelBorder(Label label, Color borderColor)
        {
            // 由于Flax引擎的Label不支持BorderColor，这里用BackgroundColor来模拟
            var currentBg = label.BackgroundColor;
            label.BackgroundColor = new Color(
                Mathf.Lerp(currentBg.R, borderColor.R, 0.1f),
                Mathf.Lerp(currentBg.G, borderColor.G, 0.1f),
                Mathf.Lerp(currentBg.B, borderColor.B, 0.1f),
                currentBg.A
            );
        }
        public static UICanvas CreateUICanvas(string name)
        {
            Actor Actor = new EmptyActor();
            Actor.Name = name;
            Level.SpawnActor(Actor);
            // 方法1：从当前Actor查找
            var canvas = Actor.AddChild<UICanvas>();

            return canvas;

        }


    }

    /// <summary>
    /// Toast管理器（简单实现）
    /// </summary>
    public class ToastManager
    {
        public void ShowSuccess(string message)
        {
            FlaxEngine.Debug.Log($"[Toast成功] {message}");
            new ConfirmDialog().ShowAdvanced("成功", message);

        }

        public void ShowError(string message)
        {
            FlaxEngine.Debug.LogError($"[Toast错误] {message}");
            new ConfirmDialog().ShowAdvanced("失败", message);
        }

        public void ShowInfo(string message)
        {
            FlaxEngine.Debug.Log($"[Toast信息] {message}");
            new ConfirmDialog().ShowAdvanced("信息", message);
        }

        public void ShowToast(string message, ToastType toastType = ToastType.Info)
        {
            switch (toastType)
            {
                case ToastType.Success:
                    ShowSuccess(message);
                    break;
                case ToastType.Error:
                    ShowError(message);
                    break;
                default:
                    ShowInfo(message);
                    break;
            }
        }

        // 支持 3 个参数的重载
        public void ShowToast(string message, ToastType toastType, float additionalData)
        {
            ShowToast(message, toastType);
        }
    }
}
