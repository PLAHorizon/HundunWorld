using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.Equipment;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages
{
    /// <summary>
    /// 获得物品弹窗。
    /// 全屏半透明遮罩（<see cref="InkWashTheme.Scrim"/>）+ 居中 <see cref="InkPanelElevated"/>（360x280），
    /// 内含品质光晕格子、物品名、数量与"确认"按钮。
    /// 通过 <see cref="Confirmed"/> 事件通知外部（Router 订阅后关闭弹窗）。
    /// 全部数据为 mock，通过 <see cref="RefreshLayout"/> 支持屏幕尺寸变化。
    /// </summary>
    public class PopupItemAcquired : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>居中抬升面板宽度（像素）</summary>
        private const float PanelWidth = 360f;

        /// <summary>居中抬升面板高度（像素）</summary>
        private const float PanelHeight = 280f;

        /// <summary>品质光晕格子尺寸（正方形，像素）</summary>
        private const float CellSize = 80f;

        /// <summary>品质光晕格子 X 坐标（面板内水平居中：(360-80)/2 = 140）</summary>
        private const float CellX = 140f;

        /// <summary>品质光晕格子 Y 坐标</summary>
        private const float CellY = 30f;

        /// <summary>物品名文本 X 坐标（面板内水平居中：(360-280)/2 = 40）</summary>
        private const float NameX = 40f;

        /// <summary>物品名文本 Y 坐标</summary>
        private const float NameY = 130f;

        /// <summary>物品名文本宽度</summary>
        private const float NameWidth = 280f;

        /// <summary>物品名文本高度</summary>
        private const float NameHeight = 28f;

        /// <summary>数量文本 X 坐标（与物品名同列）</summary>
        private const float CountX = 40f;

        /// <summary>数量文本 Y 坐标</summary>
        private const float CountY = 165f;

        /// <summary>数量文本宽度</summary>
        private const float CountWidth = 280f;

        /// <summary>数量文本高度</summary>
        private const float CountHeight = 24f;

        /// <summary>确认按钮 X 坐标（面板内水平居中：(360-100)/2 = 130）</summary>
        private const float ConfirmX = 130f;

        /// <summary>确认按钮 Y 坐标</summary>
        private const float ConfirmY = 220f;

        /// <summary>确认按钮宽度</summary>
        private const float ConfirmWidth = 100f;

        /// <summary>确认按钮高度</summary>
        private const float ConfirmHeight = 36f;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        /// <summary>居中抬升面板</summary>
        private InkPanelElevated _panel;

        /// <summary>品质光晕格子</summary>
        private InkCell _qualityCell;

        /// <summary>物品名文本</summary>
        private InkTextBlock _nameText;

        /// <summary>数量文本</summary>
        private InkTextBlock _countText;

        /// <summary>确认按钮</summary>
        private InkButton _confirmButton;

        // ===================================================================
        // 屏幕尺寸缓存
        // =======================================================================

        /// <summary>当前屏幕尺寸缓存，用于布局计算</summary>
        private Float2 _screenSize;

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化全屏遮罩与居中面板，使用 mock 数据填充。
        /// 构造时读取 <see cref="FlaxEngine.Screen.Size"/> 计算布局，
        /// 屏幕尺寸未就绪时使用 1920x1080 兜底。
        /// </summary>
        public PopupItemAcquired()
        {
            // 1. 读取屏幕尺寸
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            // 2. 外壳：全屏拉伸 + 半透明遮罩 + 不裁剪子控件
            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = InkWashTheme.Scrim;
            ClipChildren = false;
            AutoFocus = true;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                // 3. 居中抬升面板（尺寸固定，位置由 ApplyLayout 居中计算）
                _panel = new InkPanelElevated
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Size = new Float2(PanelWidth, PanelHeight),
                };
                AddChild(_panel);

                // 4. 面板内子控件
                BuildCell();
                BuildNameText();
                BuildCountText();
                BuildConfirmButton();

                // 5. 应用初始布局（基于屏幕尺寸居中面板）
                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[PopupItemAcquired] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // SubTask 构造方法
        // =======================================================================

        /// <summary>
        /// SubTask 10.1：品质光晕格子。
        /// <see cref="InkCell"/> 尺寸 80x80，位置 (140, 30)，
        /// 默认 <see cref="InkWashTheme.InkQuality.Legendary"/>（朱红品质色边框，对应"朱红光晕"）。
        /// </summary>
        private void BuildCell()
        {
            _qualityCell = new InkCell
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(CellX, CellY),
                Size = new Float2(CellSize, CellSize),
                Quality = InkWashTheme.InkQuality.Legendary,
            };
            _panel.AddChild(_qualityCell);
        }

        /// <summary>
        /// SubTask 10.1：物品名文本。
        /// <see cref="InkTextBlock"/> Heading 样式，文本"玄铁剑"，
        /// 位置 (40, 130)，宽度 280，水平居中。
        /// </summary>
        private void BuildNameText()
        {
            _nameText = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = "玄铁剑",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(NameX, NameY),
                Size = new Float2(NameWidth, NameHeight),
                HorizontalAlignment = TextAlignment.Center,
            };
            _panel.AddChild(_nameText);
        }

        /// <summary>
        /// SubTask 10.1：数量文本。
        /// <see cref="InkTextBlock"/> Body 样式，文本"×1"，
        /// 位置 (40, 165)，宽度 280，水平居中，字色 <see cref="InkWashTheme.TextSecondary"/>。
        /// </summary>
        private void BuildCountText()
        {
            _countText = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "×1",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(CountX, CountY),
                Size = new Float2(CountWidth, CountHeight),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.TextSecondary,
            };
            _panel.AddChild(_countText);
        }

        /// <summary>
        /// SubTask 10.1："确认"按钮。
        /// <see cref="InkButton"/> Primary Md，位置 (130, 220)，尺寸 (100, 36)，
        /// 点击触发 <see cref="Confirmed"/> 事件。
        /// </summary>
        private void BuildConfirmButton()
        {
            _confirmButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Md,
                Text = "确认",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ConfirmX, ConfirmY),
                Size = new Float2(ConfirmWidth, ConfirmHeight),
            };
            _confirmButton.ButtonClicked += OnConfirmButtonClicked;
            _panel.AddChild(_confirmButton);
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        /// <summary>
        /// 确认事件。点击"确认"按钮时触发，由外部（Router）订阅后关闭弹窗。
        /// </summary>
        public event Action Confirmed;

        /// <summary>
        /// 确认按钮点击处理：触发 <see cref="Confirmed"/> 事件。
        /// </summary>
        /// <param name="button">触发事件的按钮（未使用）</param>
        private void OnConfirmButtonClicked(Button button)
        {
            try
            {
                Confirmed?.Invoke();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError(
                    $"[PopupItemAcquired] Confirmed 触发失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 公共 API
        // =======================================================================

        /// <summary>
        /// 动态设置物品名、数量与品质。
        /// </summary>
        /// <param name="name">物品名</param>
        /// <param name="count">数量（将渲染为"×N"）</param>
        /// <param name="quality">品质等级（决定光晕格子边框色）</param>
        public void SetItem(string name, int count, InkWashTheme.InkQuality quality)
        {
            if (_nameText != null)
                _nameText.Text = name ?? string.Empty;

            if (_countText != null)
                _countText.Text = $"×{count}";

            if (_qualityCell != null)
                _qualityCell.Quality = quality;
        }

        /// <summary>
        /// 根据装备 ID 查询 <see cref="EquipmentDatabase"/> 并填充弹窗。
        /// 装备品质（0-5）会被钳制到 <see cref="InkWashTheme.InkQuality"/>（0-4）。
        /// 查询失败时显示"未知物品"与 Common 品质。
        /// </summary>
        /// <param name="itemId">装备 ID（对应 <see cref="EquipmentData.Id"/>）</param>
        /// <param name="count">数量（将渲染为"×N"）</param>
        public void ShowItem(ulong itemId, int count)
        {
            string name = "未知物品";
            InkWashTheme.InkQuality quality = InkWashTheme.InkQuality.Common;

            try
            {
                var data = EquipmentDatabase.GetEquipment((int)itemId);
                if (data != null)
                {
                    name = data.Name ?? "未知物品";
                    quality = (InkWashTheme.InkQuality)Mathf.Clamp(data.Quality, 0, 4);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError(
                    $"[PopupItemAcquired] 查询装备 {itemId} 失败: {ex.Message}");
            }

            SetItem(name, count, quality);
        }

        // ===================================================================
        // 布局计算
        // =======================================================================

        /// <summary>
        /// 根据当前 <see cref="_screenSize"/> 重新计算居中面板位置（保持面板尺寸不变）。
        /// 由构造函数与 <see cref="RefreshLayout"/> 调用。
        /// </summary>
        private void ApplyLayout()
        {
            if (_panel != null)
            {
                _panel.Location = new Float2(
                    (_screenSize.X - PanelWidth) * 0.5f,
                    (_screenSize.Y - PanelHeight) * 0.5f);
            }
        }

        /// <summary>
        /// 在屏幕尺寸变化时重新布局遮罩与居中面板。
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

    // ===================================================================
    // PopupMessage
    // =======================================================================

    /// <summary>
    /// 江湖来信留言弹窗。
    /// 全屏半透明遮罩（<see cref="InkWashTheme.Scrim"/>）+ 居中 <see cref="InkPaperPanel"/>（400x320，信笺样式），
    /// 内含标题、留言正文与"关闭"按钮。
    /// 通过 <see cref="Closed"/> 事件通知外部关闭弹窗。
    /// 全部数据为 mock，通过 <see cref="RefreshLayout"/> 支持屏幕尺寸变化。
    /// </summary>
    public class PopupMessage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>居中信笺面板宽度（像素）</summary>
        private const float PanelWidth = 400f;

        /// <summary>居中信笺面板高度（像素）</summary>
        private const float PanelHeight = 320f;

        /// <summary>标题文本 X 坐标（面板内水平居中：(400-320)/2 = 40）</summary>
        private const float TitleX = 40f;

        /// <summary>标题文本 Y 坐标</summary>
        private const float TitleY = 30f;

        /// <summary>标题文本宽度</summary>
        private const float TitleWidth = 320f;

        /// <summary>标题文本高度</summary>
        private const float TitleHeight = 32f;

        /// <summary>留言正文 X 坐标（与标题同列）</summary>
        private const float ContentX = 40f;

        /// <summary>留言正文 Y 坐标</summary>
        private const float ContentY = 80f;

        /// <summary>留言正文宽度</summary>
        private const float ContentWidth = 320f;

        /// <summary>留言正文高度</summary>
        private const float ContentHeight = 180f;

        /// <summary>关闭按钮 X 坐标（面板内水平居中：(400-80)/2 = 160）</summary>
        private const float CloseX = 160f;

        /// <summary>关闭按钮 Y 坐标</summary>
        private const float CloseY = 270f;

        /// <summary>关闭按钮宽度</summary>
        private const float CloseWidth = 80f;

        /// <summary>关闭按钮高度</summary>
        private const float CloseHeight = 36f;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        /// <summary>居中信笺面板</summary>
        private InkPaperPanel _panel;

        /// <summary>标题文本</summary>
        private InkTextBlock _titleText;

        /// <summary>留言正文文本</summary>
        private InkTextBlock _contentText;

        /// <summary>关闭按钮</summary>
        private InkButton _closeButton;

        // ===================================================================
        // 屏幕尺寸缓存
        // =======================================================================

        /// <summary>当前屏幕尺寸缓存，用于布局计算</summary>
        private Float2 _screenSize;

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化全屏遮罩与居中信笺面板，使用 mock 数据填充。
        /// 构造时读取 <see cref="FlaxEngine.Screen.Size"/> 计算布局，
        /// 屏幕尺寸未就绪时使用 1920x1080 兜底。
        /// </summary>
        public PopupMessage()
        {
            // 1. 读取屏幕尺寸
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            // 2. 外壳：全屏拉伸 + 半透明遮罩 + 不裁剪子控件
            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = InkWashTheme.Scrim;
            ClipChildren = false;
            AutoFocus = true;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                // 3. 居中信笺面板（尺寸固定，位置由 ApplyLayout 居中计算）
                _panel = new InkPaperPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Size = new Float2(PanelWidth, PanelHeight),
                };
                AddChild(_panel);

                // 4. 面板内子控件
                BuildTitleText();
                BuildContentText();
                BuildCloseButton();

                // 5. 应用初始布局（基于屏幕尺寸居中面板）
                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[PopupMessage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // SubTask 构造方法
        // =======================================================================

        /// <summary>
        /// SubTask 10.2：标题文本。
        /// <see cref="InkTextBlock"/> Heading 样式，文本"江湖来信"，
        /// 位置 (40, 30)，宽度 320，水平居中，字色 <see cref="InkWashTheme.TextOnPaper"/>。
        /// </summary>
        private void BuildTitleText()
        {
            _titleText = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = "江湖来信",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(TitleX, TitleY),
                Size = new Float2(TitleWidth, TitleHeight),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.TextOnPaper,
            };
            _panel.AddChild(_titleText);
        }

        /// <summary>
        /// SubTask 10.2：留言正文。
        /// <see cref="InkTextBlock"/> Body 样式，文本"少侠亲启：江湖路远，望多保重。他日有缘，再会于洛阳。"，
        /// 位置 (40, 80)，尺寸 (320, 180)，字色 <see cref="InkWashTheme.TextOnPaper"/>，
        /// 左上对齐 + <see cref="TextWrapping.WrapWords"/> 支持多行换行。
        /// </summary>
        private void BuildContentText()
        {
            _contentText = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "少侠亲启：江湖路远，望多保重。他日有缘，再会于洛阳。",
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
        /// SubTask 10.2："关闭"按钮。
        /// <see cref="InkButton"/> Ghost Md，位置 (160, 270)，尺寸 (80, 36)，
        /// 字色 <see cref="InkWashTheme.TextOnPaper"/>（在纸色背景上保持可读），
        /// 点击触发 <see cref="Closed"/> 事件。
        /// </summary>
        private void BuildCloseButton()
        {
            _closeButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Text = "关闭",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(CloseX, CloseY),
                Size = new Float2(CloseWidth, CloseHeight),
                TextColor = InkWashTheme.TextOnPaper,
            };
            _closeButton.ButtonClicked += OnCloseButtonClicked;
            _panel.AddChild(_closeButton);
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        /// <summary>
        /// 关闭事件。点击"关闭"按钮时触发，由外部订阅后关闭弹窗。
        /// </summary>
        public event Action Closed;

        /// <summary>
        /// 关闭按钮点击处理：触发 <see cref="Closed"/> 事件。
        /// </summary>
        /// <param name="button">触发事件的按钮（未使用）</param>
        private void OnCloseButtonClicked(Button button)
        {
            try
            {
                Closed?.Invoke();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError(
                    $"[PopupMessage] Closed 触发失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 公共 API
        // =======================================================================

        /// <summary>
        /// 动态设置标题与留言内容。
        /// </summary>
        /// <param name="title">标题文本</param>
        /// <param name="content">留言正文（支持多行换行）</param>
        public void SetMessage(string title, string content)
        {
            if (_titleText != null)
                _titleText.Text = title ?? string.Empty;

            if (_contentText != null)
                _contentText.Text = content ?? string.Empty;
        }

        /// <summary>
        /// 外部调用入口：设置标题与留言内容（与 <see cref="SetMessage"/> 等价，
        /// 提供符合"Show"语义的调用接口供 Router/外部系统使用）。
        /// </summary>
        /// <param name="title">标题文本</param>
        /// <param name="content">留言正文（支持多行换行）</param>
        public void ShowMessage(string title, string content)
        {
            SetMessage(title, content);
        }

        // ===================================================================
        // 布局计算
        // =======================================================================

        /// <summary>
        /// 根据当前 <see cref="_screenSize"/> 重新计算居中面板位置（保持面板尺寸不变）。
        /// 由构造函数与 <see cref="RefreshLayout"/> 调用。
        /// </summary>
        private void ApplyLayout()
        {
            if (_panel != null)
            {
                _panel.Location = new Float2(
                    (_screenSize.X - PanelWidth) * 0.5f,
                    (_screenSize.Y - PanelHeight) * 0.5f);
            }
        }

        /// <summary>
        /// 在屏幕尺寸变化时重新布局遮罩与居中面板。
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
