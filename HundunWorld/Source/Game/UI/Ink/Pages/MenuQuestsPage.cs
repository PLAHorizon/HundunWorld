using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages
{
    /// <summary>
    /// 任务菜单页面。
    /// 承载 3 个子区域：
    /// <list type="bullet">
    ///   <item>SubTask 8.1 顶部标题栏（<see cref="InkPanelTitle"/>，文本"任务"）</item>
    ///   <item>SubTask 8.2 左侧分类侧边栏（3 个 <see cref="InkListItem"/>：主线/支线/日常）</item>
    ///   <item>SubTask 8.3 右侧任务列表（5 个 <see cref="InkListItem"/> + <see cref="InkBar"/> 进度条）</item>
    /// </list>
    /// 返回按钮由 <see cref="InkPageShell"/> 自动添加的 InkBackButton 承载，本页面不自建。
    /// 全部数据为 mock，通过 <see cref="RefreshLayout"/> 支持屏幕尺寸变化。
    /// </summary>
    public class MenuQuestsPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>顶部标题栏高度（像素）</summary>
        private const float TitleHeight = 48f;

        /// <summary>内容区顶部 Y 坐标（标题栏下方留白）</summary>
        private const float ContentTop = 80f;

        /// <summary>内容区底部留白（像素）</summary>
        private const float ContentBottomMargin = 80f;

        /// <summary>左侧分类面板 X 坐标</summary>
        private const float LeftPanelX = 40f;

        /// <summary>左侧分类面板宽度</summary>
        private const float LeftPanelWidth = 200f;

        /// <summary>右侧任务列表面板 X 坐标</summary>
        private const float RightPanelX = 260f;

        /// <summary>右侧任务列表面板宽度公式中扣除的常量（= X 偏移 260 + 右边距 40 = 300）</summary>
        private const float RightPanelWidthReserve = 300f;

        /// <summary>分类列表项高度（像素）</summary>
        private const float CategoryItemHeight = 48f;

        /// <summary>分类项 Label 左边距</summary>
        private const float CategoryLabelLeftMargin = 16f;

        /// <summary>任务列表项高度（像素）</summary>
        private const float QuestItemHeight = 96f;

        /// <summary>任务列表项垂直间距（像素）</summary>
        private const float QuestItemGap = 8f;

        /// <summary>任务项内容左右内边距</summary>
        private const float QuestItemHPadding = 20f;

        /// <summary>任务名 Label Y 坐标</summary>
        private const float QuestNameY = 8f;

        /// <summary>任务名 Label 高度</summary>
        private const float QuestNameHeight = 22f;

        /// <summary>任务描述 Label Y 坐标</summary>
        private const float QuestDescY = 30f;

        /// <summary>任务描述 Label 高度</summary>
        private const float QuestDescHeight = 18f;

        /// <summary>进度行 Y 坐标（InkBar 与数值 Label 共用基准）</summary>
        private const float QuestProgressY = 56f;

        /// <summary>进度行高度</summary>
        private const float QuestProgressHeight = 14f;

        /// <summary>InkBar 高度（像素）</summary>
        private const float QuestBarHeight = 6f;

        /// <summary>进度数值 Label 宽度</summary>
        private const float QuestProgressValueLabelWidth = 48f;

        /// <summary>InkBar 与数值 Label 间距</summary>
        private const float QuestBarToValueGap = 8f;

        /// <summary>奖励 Label Y 坐标</summary>
        private const float QuestRewardY = 74f;

        /// <summary>奖励 Label 高度</summary>
        private const float QuestRewardHeight = 16f;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        /// <summary>顶部标题栏</summary>
        private InkPanelTitle _title;

        /// <summary>左侧分类面板</summary>
        private InkPanel _leftPanel;

        /// <summary>右侧任务列表面板</summary>
        private InkPanel _rightPanel;

        /// <summary>分类项数组（用于点击切换 active 态）</summary>
        private CategoryItem[] _categoryItems = new CategoryItem[3];

        /// <summary>任务项数组（用于布局刷新）</summary>
        private QuestEntry[] _questEntries = new QuestEntry[5];

        // ===================================================================
        // 屏幕尺寸缓存
        // =======================================================================

        /// <summary>当前屏幕尺寸缓存，用于布局计算</summary>
        private Float2 _screenSize;

        /// <summary>当前选中的分类索引（0=主线，1=支线，2=日常），用于过滤任务列表</summary>
        private int _selectedCategory = 0;

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化全部 3 个子区域，使用 mock 数据填充。
        /// 构造时读取 <see cref="FlaxEngine.Screen.Size"/> 计算布局，
        /// 屏幕尺寸未就绪时使用 1920x1080 兜底。
        /// </summary>
        public MenuQuestsPage()
        {
            // 1. 读取屏幕尺寸
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            // 2. 外壳：全屏拉伸 + 透明背景 + 不裁剪子控件
            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                BuildTitle();
                BuildLeftCategories();
                BuildRightQuestList();

                // 应用初始布局（基于屏幕尺寸计算所有子控件位置与尺寸）
                ApplyLayout();

                // 应用初始分类过滤（默认仅显示主线任务）
                FilterQuestsByCategory(_selectedCategory);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuQuestsPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // SubTask 构造方法
        // =======================================================================

        /// <summary>
        /// SubTask 8.1：顶部标题栏。
        /// <see cref="InkPanelTitle"/> 文本"任务"，位置 (0, 0)，宽度铺满，高度 48。
        /// 返回按钮由 <see cref="InkPageShell"/> 自动添加，本页面不自建。
        /// </summary>
        private void BuildTitle()
        {
            _title = new InkPanelTitle
            {
                Title = "任务",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Height = TitleHeight,
            };
            AddChild(_title);
        }

        /// <summary>
        /// SubTask 8.2：左侧分类侧边栏。
        /// <see cref="InkPanel"/> 容器位置 (40, 80)，尺寸 (200, screenHeight - 160)。
        /// 内含 3 个 <see cref="InkListItem"/>（垂直排列，每项高度 48）：
        /// 主线（默认 active）/支线/日常。
        /// 每项点击切换 active 态（mock 行为：点击时设置该项 active，其他项取消 active）。
        /// </summary>
        private void BuildLeftCategories()
        {
            _leftPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_leftPanel);

            // mock 数据：分类名
            string[] categories = { "主线", "支线", "日常" };

            for (int i = 0; i < categories.Length; i++)
            {
                var item = new CategoryItem
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, i * CategoryItemHeight),
                    Size = new Float2(LeftPanelWidth, CategoryItemHeight),
                    Active = (i == 0),
                };

                // 分类名 Label：Heading 字体 16px
                var label = new Label
                {
                    Text = categories[i],
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 16f),
                    TextColor = InkWashTheme.TextDefault,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(CategoryLabelLeftMargin, 0f),
                    Size = new Float2(LeftPanelWidth - CategoryLabelLeftMargin, CategoryItemHeight),
                };
                item.AddChild(label);

                item.OnClicked = OnCategoryClicked;
                _categoryItems[i] = item;
                _leftPanel.AddChild(item);
            }
        }

        /// <summary>
        /// SubTask 8.3：右侧任务列表。
        /// <see cref="InkPanel"/> 容器位置 (260, 80)，尺寸 (screenWidth - 300, screenHeight - 160)。
        /// 内含 5 个 <see cref="InkListItem"/>（垂直排列，每项高度 96，间距 8）。
        /// 每项包含：任务名 Label（Heading 16px）、任务描述 Label（Body 12px）、
        /// <see cref="InkBar"/> Gold 进度条（高 6）+ 进度数值 Label（DIN 11px）、
        /// 奖励 Label（Caption 11px，字色 <see cref="InkWashTheme.TextBrand"/>）。
        /// </summary>
        private void BuildRightQuestList()
        {
            _rightPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_rightPanel);

            // mock 任务数据：任务名、描述、当前进度/目标、奖励、分类
            var quests = new[]
            {
                (name: "寻访江湖名士", desc: "前往洛阳城寻访名士张三丰", current: 3, target: 10, reward: "奖励：经验+100 铜钱+50", category: "主线"),
                (name: "破阵试炼", desc: "挑战破阵塔第三层", current: 6, target: 10, reward: "奖励：经验+200 装备×1", category: "主线"),
                (name: "书信往来", desc: "将密信送至黑风寨", current: 9, target: 10, reward: "奖励：经验+150 银两+10", category: "支线"),
                (name: "采药修行", desc: "采集 10 株灵芝", current: 4, target: 10, reward: "奖励：经验+80 丹药×3", category: "支线"),
                (name: "江湖恩怨", desc: "化解洛阳城纷争", current: 1, target: 10, reward: "奖励：经验+300 声望+20", category: "日常"),
            };

            for (int i = 0; i < quests.Length; i++)
            {
                var quest = quests[i];
                float y = i * (QuestItemHeight + QuestItemGap);

                var entry = new QuestEntry(quest.name, quest.desc, quest.current, quest.target, quest.reward, quest.category)
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, y),
                };
                _questEntries[i] = entry;
                _rightPanel.AddChild(entry);
            }
        }

        // ===================================================================
        // 分类点击处理
        // =======================================================================

        /// <summary>
        /// 分类项点击回调：设置被点击项为 active，其余取消 active，并按分类过滤任务列表。
        /// </summary>
        /// <param name="selected">被点击的分类项</param>
        private void OnCategoryClicked(CategoryItem selected)
        {
            int selectedIndex = 0;
            for (int i = 0; i < _categoryItems.Length; i++)
            {
                if (_categoryItems[i] != null)
                {
                    _categoryItems[i].Active = (_categoryItems[i] == selected);
                    if (_categoryItems[i] == selected)
                        selectedIndex = i;
                }
            }
            _selectedCategory = selectedIndex;
            FilterQuestsByCategory(selectedIndex);
        }

        /// <summary>
        /// 按分类过滤任务列表：隐藏不匹配项，将匹配项重新紧凑排列在顶部。
        /// </summary>
        /// <param name="categoryIndex">分类索引（0=主线，1=支线，2=日常）</param>
        private void FilterQuestsByCategory(int categoryIndex)
        {
            string[] categoryNames = { "主线", "支线", "日常" };
            if (categoryIndex < 0 || categoryIndex >= categoryNames.Length)
                return;
            string selectedName = categoryNames[categoryIndex];

            int visibleIndex = 0;
            foreach (var entry in _questEntries)
            {
                if (entry == null)
                    continue;
                bool match = entry.Category == selectedName;
                entry.Visible = match;
                if (match)
                {
                    entry.Location = new Float2(0f, visibleIndex * (QuestItemHeight + QuestItemGap));
                    visibleIndex++;
                }
            }
        }

        // ===================================================================
        // 布局计算
        // =======================================================================

        /// <summary>
        /// 根据当前 <see cref="_screenSize"/> 重新计算所有子控件的位置与尺寸。
        /// 由构造函数与 <see cref="RefreshLayout"/> 调用。
        /// </summary>
        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;

            // SubTask 8.1 标题栏：位置 (0, 0)，宽度铺满，高度 48
            if (_title != null)
            {
                _title.Location = Float2.Zero;
                _title.Size = new Float2(sw, TitleHeight);
            }

            // SubTask 8.2 左侧分类面板：(40, 80)，尺寸 (200, sh - 160)
            if (_leftPanel != null)
            {
                _leftPanel.Location = new Float2(LeftPanelX, ContentTop);
                _leftPanel.Size = new Float2(LeftPanelWidth, sh - ContentTop - ContentBottomMargin);
            }

            // SubTask 8.3 右侧任务列表面板：(260, 80)，尺寸 (sw - 300, sh - 160)
            if (_rightPanel != null)
            {
                float rightWidth = sw - RightPanelWidthReserve;
                _rightPanel.Location = new Float2(RightPanelX, ContentTop);
                _rightPanel.Size = new Float2(rightWidth, sh - ContentTop - ContentBottomMargin);

                // 同步任务项宽度（随右侧面板宽度变化）
                foreach (var entry in _questEntries)
                {
                    entry?.UpdateLayout(rightWidth);
                }
            }
        }

        /// <summary>
        /// 在屏幕尺寸变化时重新布局所有子控件。
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

        // ===================================================================
        // 嵌套类
        // =======================================================================

        /// <summary>
        /// 分类列表项（支持点击切换 active 态）。
        /// 继承 <see cref="InkListItem"/>，重写 <see cref="OnMouseDown"/> 触发点击回调。
        /// </summary>
        private class CategoryItem : InkListItem
        {
            /// <summary>点击回调（参数为本项自身）</summary>
            public Action<CategoryItem> OnClicked;

            /// <inheritdoc />
            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                {
                    OnClicked?.Invoke(this);
                }
                return base.OnMouseDown(location, button);
            }
        }

        /// <summary>
        /// 任务列表项（封装任务名、描述、进度条、数值、奖励 5 个子控件）。
        /// 继承 <see cref="InkListItem"/>，通过 <see cref="UpdateLayout"/> 在面板宽度变化时同步子控件尺寸。
        /// 点击任务项时进度 +1，进度满标记"已完成"并将奖励文本高亮为金色。
        /// </summary>
        private class QuestEntry : InkListItem
        {
            /// <summary>任务名 Label（Heading 16px，字色 TextDefault）</summary>
            private Label _nameLabel;

            /// <summary>任务描述 Label（Body 12px，字色 TextSecondary）</summary>
            private Label _descLabel;

            /// <summary>进度条（Gold 变体）</summary>
            private InkBar _bar;

            /// <summary>进度数值 Label（DIN 11px）</summary>
            private Label _valueLabel;

            /// <summary>奖励 Label（Caption 11px，字色 TextBrand）</summary>
            private Label _rewardLabel;

            /// <summary>原始任务名（完成后追加" [已完成]"）</summary>
            private string _originalName;

            /// <summary>当前进度计数</summary>
            private int _current;

            /// <summary>目标进度计数</summary>
            private int _target;

            /// <summary>是否已完成</summary>
            private bool _completed;

            /// <summary>任务分类（主线/支线/日常），用于过滤</summary>
            public string Category { get; }

            /// <summary>是否已完成</summary>
            public bool IsComplete => _completed;

            /// <summary>
            /// 构造函数：创建 5 个子控件并填充 mock 数据。
            /// </summary>
            /// <param name="name">任务名</param>
            /// <param name="desc">任务描述</param>
            /// <param name="current">当前进度计数</param>
            /// <param name="target">目标进度计数</param>
            /// <param name="reward">奖励文本</param>
            /// <param name="category">任务分类（主线/支线/日常）</param>
            public QuestEntry(string name, string desc, int current, int target, string reward, string category)
            {
                _originalName = name;
                _current = current;
                _target = target > 0 ? target : 1;
                Category = category ?? string.Empty;
                _completed = _current >= _target;

                Height = QuestItemHeight;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;

                float progress = (float)_current / _target;
                float barY = QuestProgressY + (QuestProgressHeight - QuestBarHeight) * 0.5f;

                // 任务名 Label：Heading 字体 16px，字色 TextDefault
                _nameLabel = new Label
                {
                    Text = _completed ? $"{name} [已完成]" : name,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 16f),
                    TextColor = InkWashTheme.TextDefault,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(QuestItemHPadding, QuestNameY),
                    Size = new Float2(0f, QuestNameHeight),
                };
                AddChild(_nameLabel);

                // 任务描述 Label：Body 字体 12px，字色 TextSecondary
                _descLabel = new Label
                {
                    Text = desc,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                    TextColor = InkWashTheme.TextSecondary,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(QuestItemHPadding, QuestDescY),
                    Size = new Float2(0f, QuestDescHeight),
                };
                AddChild(_descLabel);

                // InkBar：Gold 变体，高 6
                _bar = new InkBar
                {
                    FillVariant = InkBarFillVariant.Gold,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(QuestItemHPadding, barY),
                    Size = new Float2(0f, QuestBarHeight),
                    Value = progress,
                };
                AddChild(_bar);

                // 进度数值 Label：DIN 字体 11px
                int percent = (int)(progress * 100f + 0.5f);
                _valueLabel = new Label
                {
                    Text = $"{percent}%",
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                    TextColor = InkWashTheme.TextBrand,
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, QuestProgressY),
                    Size = new Float2(QuestProgressValueLabelWidth, QuestProgressHeight),
                };
                AddChild(_valueLabel);

                // 奖励 Label：Caption 字体 11px（Body 11px），字色 TextBrand（完成后改为金色）
                _rewardLabel = new Label
                {
                    Text = reward,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                    TextColor = _completed ? InkWashTheme.GoldPrimary : InkWashTheme.TextBrand,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(QuestItemHPadding, QuestRewardY),
                    Size = new Float2(0f, QuestRewardHeight),
                };
                AddChild(_rewardLabel);
            }

            /// <summary>
            /// 点击任务项：进度 +1，满则标记"已完成"并高亮奖励文本为金色。
            /// </summary>
            public void IncrementProgress()
            {
                if (_completed)
                    return;
                _current = Mathf.Min(_current + 1, _target);
                float progress = (float)_current / _target;

                if (_bar != null)
                    _bar.Value = progress;

                if (_valueLabel != null)
                {
                    int percent = (int)(progress * 100f + 0.5f);
                    _valueLabel.Text = $"{percent}%";
                }

                if (_current >= _target)
                {
                    _completed = true;
                    if (_nameLabel != null)
                        _nameLabel.Text = $"{_originalName} [已完成]";
                    if (_rewardLabel != null)
                        _rewardLabel.TextColor = InkWashTheme.GoldPrimary;
                }
            }

            /// <inheritdoc />
            /// <remarks>点击任务项时调用 <see cref="IncrementProgress"/> 推进进度。</remarks>
            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                {
                    IncrementProgress();
                }
                return base.OnMouseDown(location, button);
            }

            /// <summary>
            /// 根据任务项宽度同步更新所有子控件的宽度与水平位置。
            /// 由 <see cref="MenuQuestsPage.ApplyLayout"/> 在面板尺寸变化时调用。
            /// </summary>
            /// <param name="itemWidth">任务项宽度（= 右侧面板宽度）</param>
            public void UpdateLayout(float itemWidth)
            {
                Width = itemWidth;

                float contentWidth = itemWidth - 2f * QuestItemHPadding;

                // 任务名、描述、奖励 Label：宽度铺满内容区
                _nameLabel.Size = new Float2(contentWidth, QuestNameHeight);
                _descLabel.Size = new Float2(contentWidth, QuestDescHeight);
                _rewardLabel.Size = new Float2(contentWidth, QuestRewardHeight);

                // InkBar：宽度 = 内容宽度 - 数值 Label 宽度 - 间距
                float barWidth = contentWidth - QuestProgressValueLabelWidth - QuestBarToValueGap;
                if (barWidth < 0f) barWidth = 0f;
                _bar.Size = new Float2(barWidth, QuestBarHeight);

                // 数值 Label：右对齐，X = 项宽度 - 右内边距 - Label 宽度
                _valueLabel.Location = new Float2(
                    itemWidth - QuestItemHPadding - QuestProgressValueLabelWidth,
                    QuestProgressY);
            }
        }
    }
}
