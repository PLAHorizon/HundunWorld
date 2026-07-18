using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Combat
{
    /// <summary>
    /// 阵亡界面页面。
    /// 全屏半透明遮罩（<see cref="InkWashTheme.Scrim"/>）+ 居中"殒命"标题
    /// （<see cref="InkTextBlock"/> Display 样式，朱红色）+ 损失信息
    /// （mock 经验损失 500 / 铜钱损失 200，<see cref="InkTextBlock"/> Body 样式）。
    /// 底部提供朱红"破招"按钮（<see cref="InkButton"/> <see cref="InkButtonVariant.Vermilion"/> 变体，
    /// 尝试以武学破除死亡）与幽影"返回"按钮（<see cref="InkButton"/> <see cref="InkButtonVariant.Ghost"/> 变体，
    /// 返回复活点）。
    /// 通过 <see cref="ReviveRequested"/> 与 <see cref="ReturnRequested"/> 事件通知外部。
    /// 全部数据为 mock，通过 <see cref="RefreshLayout"/> 支持屏幕尺寸变化。
    /// </summary>
    public class DeathScreenPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>殒命标题宽度（像素）</summary>
        private const float TitleWidth = 400f;

        /// <summary>殒命标题高度（像素）</summary>
        private const float TitleHeight = 80f;

        /// <summary>损失信息文本宽度（像素）</summary>
        private const float LossWidth = 600f;

        /// <summary>损失信息文本高度（像素）</summary>
        private const float LossHeight = 30f;

        /// <summary>损失信息与标题之间的垂直间距（像素）</summary>
        private const float LossMarginTop = 16f;

        /// <summary>按钮宽度（像素）</summary>
        private const float ButtonWidth = 240f;

        /// <summary>按钮高度（像素）</summary>
        private const float ButtonHeight = 48f;

        /// <summary>两个按钮之间的水平间距（像素）</summary>
        private const float ButtonGap = 32f;

        /// <summary>按钮组距离屏幕底部的边距（像素）</summary>
        private const float ButtonMarginBottom = 180f;

        /// <summary>标题垂直位置占屏幕高度的比例（约屏幕上 1/3 处）</summary>
        private const float TitleVerticalRatio = 0.32f;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        /// <summary>殒命标题文本（Display 样式，朱红色）</summary>
        private InkTextBlock _titleText;

        /// <summary>损失信息文本（Body 样式）</summary>
        private InkTextBlock _lossText;

        /// <summary>破招按钮（Vermilion 变体）</summary>
        private InkButton _reviveButton;

        /// <summary>返回按钮（Ghost 变体）</summary>
        private InkButton _returnButton;

        // ===================================================================
        // 屏幕尺寸缓存
        // =======================================================================

        /// <summary>当前屏幕尺寸缓存，用于布局计算</summary>
        private Float2 _screenSize;

        // ===================================================================
        // mock 数据
        // =======================================================================

        /// <summary>mock 经验损失值</summary>
        private int _expLoss = 500;

        /// <summary>mock 铜钱损失值</summary>
        private int _coinLoss = 200;

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化全屏遮罩与阵亡界面，使用 mock 数据填充。
        /// 构造时读取 <see cref="FlaxEngine.Screen.Size"/> 计算布局，
        /// 屏幕尺寸未就绪时使用 1920x1080 兜底。
        /// </summary>
        public DeathScreenPage()
        {
            // 1. 读取屏幕尺寸
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            // 2. 外壳：全屏拉伸 + 半透明深墨黑遮罩 + 不裁剪子控件
            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = InkWashTheme.Scrim;
            ClipChildren = false;
            AutoFocus = true;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                // 3. 构建子控件
                BuildTitleText();
                BuildLossText();
                BuildReviveButton();
                BuildReturnButton();

                // 4. 应用初始布局
                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[DeathScreenPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // SubTask 构造方法
        // =======================================================================

        /// <summary>
        /// SubTask 11.2：殒命标题文本。
        /// <see cref="InkTextBlock"/> Display 样式（毛笔书法字体），文本"殒命"，
        /// 字色覆盖为 <see cref="InkWashTheme.VermilionBright"/>（朱红色），
        /// 位置与尺寸由 <see cref="ApplyLayout"/> 基于屏幕尺寸居中计算。
        /// </summary>
        private void BuildTitleText()
        {
            _titleText = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "殒命",
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(TitleWidth, TitleHeight),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.VermilionBright,
            };
            AddChild(_titleText);
        }

        /// <summary>
        /// SubTask 11.2：损失信息文本。
        /// <see cref="InkTextBlock"/> Body 样式，显示 mock 经验损失与铜钱损失，
        /// 位置与尺寸由 <see cref="ApplyLayout"/> 基于屏幕尺寸居中计算。
        /// </summary>
        private void BuildLossText()
        {
            _lossText = new InkTextBlock(InkTextStyle.Body)
            {
                Text = $"经验损失 {_expLoss} / 铜钱损失 {_coinLoss}",
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(LossWidth, LossHeight),
                HorizontalAlignment = TextAlignment.Center,
            };
            AddChild(_lossText);
        }

        /// <summary>
        /// SubTask 11.3：朱红"破招"按钮。
        /// <see cref="InkButton"/> <see cref="InkButtonVariant.Vermilion"/> 变体，
        /// <see cref="InkButtonSize.Lg"/> 尺寸，文本"破招"，
        /// 尝试以武学破除死亡，点击触发 <see cref="ReviveRequested"/> 事件。
        /// 位置与尺寸由 <see cref="ApplyLayout"/> 基于屏幕尺寸计算。
        /// </summary>
        private void BuildReviveButton()
        {
            _reviveButton = new InkButton
            {
                Variant = InkButtonVariant.Vermilion,
                ButtonSize = InkButtonSize.Lg,
                Text = "破招",
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(ButtonWidth, ButtonHeight),
            };
            _reviveButton.ButtonClicked += OnReviveButtonClicked;
            AddChild(_reviveButton);
        }

        /// <summary>
        /// SubTask 11.3：幽影"返回"按钮。
        /// <see cref="InkButton"/> <see cref="InkButtonVariant.Ghost"/> 变体，
        /// <see cref="InkButtonSize.Lg"/> 尺寸，文本"返回"，
        /// 返回复活点，点击触发 <see cref="ReturnRequested"/> 事件。
        /// 位置与尺寸由 <see cref="ApplyLayout"/> 基于屏幕尺寸计算。
        /// </summary>
        private void BuildReturnButton()
        {
            _returnButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Lg,
                Text = "返回",
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(ButtonWidth, ButtonHeight),
            };
            _returnButton.ButtonClicked += OnReturnButtonClicked;
            AddChild(_returnButton);
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        /// <summary>
        /// 破招（原地复活）请求事件。点击"破招"按钮时触发，由外部订阅后处理复活逻辑。
        /// </summary>
        public event Action ReviveRequested;

        /// <summary>
        /// 返回复活点请求事件。点击"返回"按钮时触发，由外部订阅后处理返回逻辑。
        /// </summary>
        public event Action ReturnRequested;

        /// <summary>
        /// 破招按钮点击处理：触发 <see cref="ReviveRequested"/> 事件。
        /// </summary>
        /// <param name="button">触发事件的按钮（未使用）</param>
        private void OnReviveButtonClicked(Button button)
        {
            try
            {
                ReviveRequested?.Invoke();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError(
                    $"[DeathScreenPage] ReviveRequested 触发失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 返回按钮点击处理：触发 <see cref="ReturnRequested"/> 事件。
        /// </summary>
        /// <param name="button">触发事件的按钮（未使用）</param>
        private void OnReturnButtonClicked(Button button)
        {
            try
            {
                ReturnRequested?.Invoke();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError(
                    $"[DeathScreenPage] ReturnRequested 触发失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 布局计算
        // =======================================================================

        /// <summary>
        /// 根据当前 <see cref="_screenSize"/> 重新计算所有子控件位置：
        /// 标题水平居中、垂直位于屏幕上 1/3 处；损失信息紧随标题下方居中；
        /// 两个按钮在底部水平居中并排。
        /// 由构造函数与 <see cref="RefreshLayout"/> 调用。
        /// </summary>
        private void ApplyLayout()
        {
            float screenWidth = _screenSize.X;
            float screenHeight = _screenSize.Y;

            // 标题：水平居中，垂直位于屏幕上 1/3 处
            if (_titleText != null)
            {
                _titleText.Location = new Float2(
                    (screenWidth - TitleWidth) * 0.5f,
                    screenHeight * TitleVerticalRatio);
            }

            // 损失信息：水平居中，紧随标题下方
            if (_lossText != null && _titleText != null)
            {
                _lossText.Location = new Float2(
                    (screenWidth - LossWidth) * 0.5f,
                    _titleText.Location.Y + TitleHeight + LossMarginTop);
            }

            // 按钮组：底部水平居中并排
            float totalButtonsWidth = ButtonWidth * 2f + ButtonGap;
            float buttonsLeft = (screenWidth - totalButtonsWidth) * 0.5f;
            float buttonY = screenHeight - ButtonMarginBottom;

            if (_reviveButton != null)
            {
                _reviveButton.Location = new Float2(buttonsLeft, buttonY);
            }

            if (_returnButton != null)
            {
                _returnButton.Location = new Float2(
                    buttonsLeft + ButtonWidth + ButtonGap, buttonY);
            }
        }

        /// <summary>
        /// 在屏幕尺寸变化时重新布局遮罩与所有子控件。
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
        }
    }
}
