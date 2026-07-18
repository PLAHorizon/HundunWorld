using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Social
{
    /// <summary>
    /// NPC 对话确认页面。
    /// 全屏透明覆盖层 + 底部居中纸色卷轴对话框（<see cref="InkPaperPanel"/>），
    /// 内含 NPC 头像占位（<see cref="InkCell"/> 64x64）、竖排 NPC 名称（<see cref="InkVerticalTitle"/>）、
    /// 对话内容文本（<see cref="InkTextBlock"/> Body 样式 + <see cref="TextWrapping.WrapWords"/>）
    /// 与三个选项按钮（接受/拒绝/询问），右上角放置跳过按钮。
    /// 通过 <see cref="SetDialogue"/> 设置 NPC 名与对话内容，通过 <see cref="DialogueConfirmed"/> 事件
    /// 通知外部选择结果（0=接受/1=拒绝/2=询问），跳过按钮触发 <see cref="DialogueSkipped"/> 事件。
    /// 全部数据为 mock，通过 <see cref="RefreshLayout"/> 支持屏幕尺寸变化。
    /// </summary>
    public class DialogueConfirmPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>底部纸色卷轴对话框宽度（像素）</summary>
        private const float PanelWidth = 720f;

        /// <summary>底部纸色卷轴对话框高度（像素）</summary>
        private const float PanelHeight = 360f;

        /// <summary>对话框距屏幕底部的边距（像素）</summary>
        private const float PanelBottomMargin = 80f;

        /// <summary>对话框内边距（像素）</summary>
        private const float PanelPadding = 32f;

        /// <summary>NPC 头像尺寸（正方形，像素）</summary>
        private const float AvatarSize = 64f;

        /// <summary>NPC 头像 X 坐标（面板内，等于内边距）</summary>
        private const float AvatarX = PanelPadding;

        /// <summary>NPC 头像 Y 坐标（面板内）</summary>
        private const float AvatarY = PanelPadding;

        /// <summary>竖排 NPC 名称 X 坐标（头像右侧 + 16px 间距）</summary>
        private const float NameX = AvatarX + AvatarSize + 16f;

        /// <summary>竖排 NPC 名称 Y 坐标（面板内）</summary>
        private const float NameY = PanelPadding;

        /// <summary>竖排 NPC 名称宽度</summary>
        private const float NameWidth = 40f;

        /// <summary>竖排 NPC 名称高度</summary>
        private const float NameHeight = 96f;

        /// <summary>对话内容 X 坐标（名称右侧 + 16px 间距）</summary>
        private const float ContentX = NameX + NameWidth + 16f;

        /// <summary>对话内容 Y 坐标（面板内）</summary>
        private const float ContentY = PanelPadding + 8f;

        /// <summary>对话内容宽度（面板宽度 - 内容 X - 右内边距）</summary>
        private const float ContentWidth = PanelWidth - ContentX - PanelPadding;

        /// <summary>对话内容高度</summary>
        private const float ContentHeight = 80f;

        /// <summary>分隔线 X 坐标（面板内，等于内边距）</summary>
        private const float DividerX = PanelPadding;

        /// <summary>分隔线 Y 坐标（面板内）</summary>
        private const float DividerY = 140f;

        /// <summary>分隔线宽度（面板宽度 - 2 倍内边距）</summary>
        private const float DividerWidth = PanelWidth - 2f * PanelPadding;

        /// <summary>选项按钮 X 坐标（面板内，等于内边距）</summary>
        private const float OptionX = PanelPadding;

        /// <summary>选项按钮起始 Y 坐标（面板内）</summary>
        private const float OptionY = 168f;

        /// <summary>选项按钮宽度（面板宽度 - 2 倍内边距）</summary>
        private const float OptionWidth = PanelWidth - 2f * PanelPadding;

        /// <summary>选项按钮高度</summary>
        private const float OptionHeight = 44f;

        /// <summary>选项按钮垂直间距</summary>
        private const float OptionGap = 8f;

        /// <summary>跳过按钮宽度</summary>
        private const float SkipButtonWidth = 80f;

        /// <summary>跳过按钮高度</summary>
        private const float SkipButtonHeight = 28f;

        /// <summary>跳过按钮距屏幕顶部的边距</summary>
        private const float SkipButtonTopMargin = 32f;

        /// <summary>跳过按钮距屏幕右侧的边距</summary>
        private const float SkipButtonRightMargin = 48f;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        /// <summary>底部纸色卷轴对话框面板</summary>
        private InkPaperPanel _panel;

        /// <summary>NPC 头像占位格子</summary>
        private InkCell _avatarCell;

        /// <summary>竖排 NPC 名称</summary>
        private InkVerticalTitle _nameTitle;

        /// <summary>对话内容文本</summary>
        private InkTextBlock _contentText;

        /// <summary>对话内容与选项之间的水墨分隔线</summary>
        private InkDivider _divider;

        /// <summary>"接受"选项按钮（Primary 变体）</summary>
        private InkButton _acceptButton;

        /// <summary>"拒绝"选项按钮（Vermilion 变体）</summary>
        private InkButton _declineButton;

        /// <summary>"询问"选项按钮（Ghost 变体）</summary>
        private InkButton _inquireButton;

        /// <summary>右上角"跳过"按钮（Ghost Sm 变体）</summary>
        private InkButton _skipButton;

        // ===================================================================
        // 屏幕尺寸缓存
        // =======================================================================

        /// <summary>当前屏幕尺寸缓存，用于布局计算</summary>
        private Float2 _screenSize;

        // ===================================================================
        // mock 数据
        // =======================================================================

        /// <summary>mock NPC 名称</summary>
        private string _npcName = "老翁";

        /// <summary>mock 对话内容</summary>
        private string _content = "少年人，你可愿随老朽走一趟？此处有大机缘等你";

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化全屏透明覆盖层与底部对话面板，使用 mock 数据填充。
        /// 构造时读取 <see cref="FlaxEngine.Screen.Size"/> 计算布局，
        /// 屏幕尺寸未就绪时使用 1920x1080 兜底。
        /// </summary>
        public DialogueConfirmPage()
        {
            // 1. 读取屏幕尺寸
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            // 2. 外壳：全屏拉伸 + 透明背景（透出底层游戏场景）+ 不裁剪子控件
            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                // 3. 底部纸色卷轴对话框面板（尺寸固定，位置由 ApplyLayout 计算）
                _panel = new InkPaperPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Size = new Float2(PanelWidth, PanelHeight),
                };
                AddChild(_panel);

                // 4. 面板内子控件
                BuildAvatar();
                BuildNameTitle();
                BuildContentText();
                BuildDivider();
                BuildAcceptButton();
                BuildDeclineButton();
                BuildInquireButton();

                // 5. 右上角跳过按钮（直接挂在根容器上，不属于对话面板）
                BuildSkipButton();

                // 6. 应用初始布局（基于屏幕尺寸定位面板与跳过按钮）
                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[DialogueConfirmPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // SubTask 构造方法
        // =======================================================================

        /// <summary>
        /// SubTask 12.2：NPC 头像占位。
        /// <see cref="InkCell"/> 尺寸 64x64，位置 (32, 32)，
        /// 默认 <see cref="InkWashTheme.InkQuality.Common"/> 品质（中性灰边框）。
        /// </summary>
        private void BuildAvatar()
        {
            _avatarCell = new InkCell
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(AvatarX, AvatarY),
                Size = new Float2(AvatarSize, AvatarSize),
                Quality = InkWashTheme.InkQuality.Common,
            };
            _panel.AddChild(_avatarCell);
        }

        /// <summary>
        /// SubTask 12.2：竖排 NPC 名称。
        /// <see cref="InkVerticalTitle"/> 字号 22px，位置 (112, 32)，尺寸 (40, 96)，
        /// 文本取自 mock 字段 <see cref="_npcName"/>。
        /// </summary>
        private void BuildNameTitle()
        {
            _nameTitle = new InkVerticalTitle
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(NameX, NameY),
                Size = new Float2(NameWidth, NameHeight),
                Text = _npcName,
                FontSize = 22f,
            };
            _panel.AddChild(_nameTitle);
        }

        /// <summary>
        /// SubTask 12.3：对话内容文本。
        /// <see cref="InkTextBlock"/> Body 样式，位置 (168, 40)，尺寸 (520, 80)，
        /// 字色 <see cref="InkWashTheme.TextOnPaper"/>（纸色背景上可读），
        /// 左上对齐 + <see cref="TextWrapping.WrapWords"/> 支持多行换行。
        /// 文本取自 mock 字段 <see cref="_content"/>。
        /// </summary>
        private void BuildContentText()
        {
            _contentText = new InkTextBlock(InkTextStyle.Body)
            {
                Text = _content,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ContentX, ContentY),
                Size = new Float2(ContentWidth, ContentHeight),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
                Wrapping = TextWrapping.WrapWords,
                TextColor = InkWashTheme.TextOnPaper,
            };
            _panel.AddChild(_contentText);
        }

        /// <summary>
        /// SubTask 12.3：对话内容与选项之间的水墨分隔线。
        /// <see cref="InkDivider"/> 位置 (32, 140)，宽度 656，高度 1px。
        /// </summary>
        private void BuildDivider()
        {
            _divider = new InkDivider
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(DividerX, DividerY),
                Size = new Float2(DividerWidth, 1f),
            };
            _panel.AddChild(_divider);
        }

        /// <summary>
        /// SubTask 12.3："接受"选项按钮。
        /// <see cref="InkButton"/> Primary Lg，位置 (32, 168)，尺寸 (656, 44)，
        /// 点击触发 <see cref="DialogueConfirmed"/> 事件（参数 0）。
        /// </summary>
        private void BuildAcceptButton()
        {
            _acceptButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Lg,
                Text = "欣然前往",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(OptionX, OptionY),
                Size = new Float2(OptionWidth, OptionHeight),
            };
            _acceptButton.ButtonClicked += OnAcceptClicked;
            _panel.AddChild(_acceptButton);
        }

        /// <summary>
        /// SubTask 12.3："拒绝"选项按钮。
        /// <see cref="InkButton"/> Vermilion Lg，位置 (32, 220)，尺寸 (656, 44)，
        /// 点击触发 <see cref="DialogueConfirmed"/> 事件（参数 1）。
        /// </summary>
        private void BuildDeclineButton()
        {
            _declineButton = new InkButton
            {
                Variant = InkButtonVariant.Vermilion,
                ButtonSize = InkButtonSize.Lg,
                Text = "婉言谢绝",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(OptionX, OptionY + OptionHeight + OptionGap),
                Size = new Float2(OptionWidth, OptionHeight),
            };
            _declineButton.ButtonClicked += OnDeclineClicked;
            _panel.AddChild(_declineButton);
        }

        /// <summary>
        /// SubTask 12.3："询问"选项按钮。
        /// <see cref="InkButton"/> Ghost Lg，位置 (32, 272)，尺寸 (656, 44)，
        /// 字色 <see cref="InkWashTheme.TextOnPaper"/>（纸色背景上保持可读），
        /// 点击触发 <see cref="DialogueConfirmed"/> 事件（参数 2）。
        /// </summary>
        private void BuildInquireButton()
        {
            _inquireButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Lg,
                Text = "询问详情",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(OptionX, OptionY + 2f * (OptionHeight + OptionGap)),
                Size = new Float2(OptionWidth, OptionHeight),
                TextColor = InkWashTheme.TextOnPaper,
            };
            _inquireButton.ButtonClicked += OnInquireClicked;
            _panel.AddChild(_inquireButton);
        }

        /// <summary>
        /// SubTask 12.3：右上角"跳过"按钮。
        /// <see cref="InkButton"/> Ghost Sm，尺寸 (80, 28)，
        /// 位置由 <see cref="ApplyLayout"/> 基于屏幕尺寸计算（右上角，距顶 32、距右 48），
        /// 点击触发 <see cref="DialogueSkipped"/> 事件。
        /// </summary>
        private void BuildSkipButton()
        {
            _skipButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "跳过",
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(SkipButtonWidth, SkipButtonHeight),
            };
            _skipButton.ButtonClicked += OnSkipClicked;
            AddChild(_skipButton);
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        /// <summary>
        /// 对话确认事件。点击"接受/拒绝/询问"按钮时触发，
        /// 参数 0=接受、1=拒绝、2=询问，由外部订阅后处理对应分支。
        /// </summary>
        public event Action<int> DialogueConfirmed;

        /// <summary>
        /// 对话跳过事件。点击右上角"跳过"按钮时触发，由外部订阅后关闭对话页面。
        /// </summary>
        public event Action DialogueSkipped;

        /// <summary>
        /// "接受"按钮点击处理：触发 <see cref="DialogueConfirmed"/> 事件（参数 0）。
        /// </summary>
        /// <param name="button">触发事件的按钮（未使用）</param>
        private void OnAcceptClicked(Button button)
        {
            try
            {
                DialogueConfirmed?.Invoke(0);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError(
                    $"[DialogueConfirmPage] DialogueConfirmed(0) 触发失败: {ex.Message}");
            }
        }

        /// <summary>
        /// "拒绝"按钮点击处理：触发 <see cref="DialogueConfirmed"/> 事件（参数 1）。
        /// </summary>
        /// <param name="button">触发事件的按钮（未使用）</param>
        private void OnDeclineClicked(Button button)
        {
            try
            {
                DialogueConfirmed?.Invoke(1);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError(
                    $"[DialogueConfirmPage] DialogueConfirmed(1) 触发失败: {ex.Message}");
            }
        }

        /// <summary>
        /// "询问"按钮点击处理：触发 <see cref="DialogueConfirmed"/> 事件（参数 2）。
        /// </summary>
        /// <param name="button">触发事件的按钮（未使用）</param>
        private void OnInquireClicked(Button button)
        {
            try
            {
                DialogueConfirmed?.Invoke(2);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError(
                    $"[DialogueConfirmPage] DialogueConfirmed(2) 触发失败: {ex.Message}");
            }
        }

        /// <summary>
        /// "跳过"按钮点击处理：触发 <see cref="DialogueSkipped"/> 事件。
        /// </summary>
        /// <param name="button">触发事件的按钮（未使用）</param>
        private void OnSkipClicked(Button button)
        {
            try
            {
                DialogueSkipped?.Invoke();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError(
                    $"[DialogueConfirmPage] DialogueSkipped 触发失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 公共 API
        // =======================================================================

        /// <summary>
        /// 设置 NPC 名称与对话内容。
        /// </summary>
        /// <param name="npcName">NPC 名称（竖排显示）</param>
        /// <param name="content">对话内容（支持多行换行）</param>
        public void SetDialogue(string npcName, string content)
        {
            _npcName = npcName ?? string.Empty;
            _content = content ?? string.Empty;

            if (_nameTitle != null)
                _nameTitle.Text = _npcName;

            if (_contentText != null)
                _contentText.Text = _content;
        }

        // ===================================================================
        // 布局计算
        // =======================================================================

        /// <summary>
        /// 根据当前 <see cref="_screenSize"/> 重新计算对话面板与跳过按钮位置（保持面板尺寸不变）。
        /// 面板水平居中、垂直贴近底部（距底 <see cref="PanelBottomMargin"/>），
        /// 跳过按钮固定在右上角。由构造函数与 <see cref="RefreshLayout"/> 调用。
        /// </summary>
        private void ApplyLayout()
        {
            if (_panel != null)
            {
                _panel.Location = new Float2(
                    (_screenSize.X - PanelWidth) * 0.5f,
                    _screenSize.Y - PanelBottomMargin - PanelHeight);
            }

            if (_skipButton != null)
            {
                _skipButton.Location = new Float2(
                    _screenSize.X - SkipButtonRightMargin - SkipButtonWidth,
                    SkipButtonTopMargin);
            }
        }

        /// <summary>
        /// 在屏幕尺寸变化时重新布局覆盖层与对话面板。
        /// 外部（如屏幕大小变更监听器）应调用此方法。
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
