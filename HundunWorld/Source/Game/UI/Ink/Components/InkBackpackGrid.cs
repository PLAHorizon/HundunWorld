using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.ECS.Components;
using HundunWorld.Game.Equipment;
using HundunWorld.Game.UI;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink.Components
{
    /// <summary>
    /// 水墨风格背包格子网格控件（魔兽世界式）。
    /// 按 N 行 M 列布局渲染 <see cref="InkBackpackCell"/>，支持双击与悬停事件、
    /// 垂直滚动、容量统计。顶部含 4 个 <see cref="InkBagSlot"/> 背包槽位，
    /// 装备扩展背包后追加扩展格子（与默认 36 格视觉区分）。
    /// 继承 <see cref="Panel"/> 以利用内置垂直滚动条：
    /// 当格子总数超过可视区域时自动出现垂直滚动条。
    /// 对应 HTML 原型中的背包网格区域，<see cref="InkBackpackCell"/> 复用
    /// <see cref="InkCell"/> 的视觉规范（品质色边框 + 图标 + 数量徽章）。
    /// </summary>
    public class InkBackpackGrid : Panel
    {
        // ===================================================================
        // 默认值常量
        // =======================================================================

        /// <summary>默认列数（6×6 = 36 格）</summary>
        private const int DefaultColumns = 6;

        /// <summary>默认格子尺寸（像素）</summary>
        private const float DefaultCellSize = 56f;

        /// <summary>默认格子间距（像素）</summary>
        private const float DefaultCellGap = 6f;

        /// <summary>默认背包容量（6×6 = 36 格）</summary>
        private const int DefaultCapacity = 36;

        /// <summary>默认背包槽位数量（顶部 4 个扩展背包槽）</summary>
        private const int DefaultBagSlotCount = 4;

        /// <summary>背包槽行与普通格子行之间的垂直间距（像素）</summary>
        private const float BagSlotRowGap = 10f;

        // ===================================================================
        // 布局字段
        // =======================================================================

        /// <summary>列数</summary>
        private int _columns = DefaultColumns;

        /// <summary>格子尺寸（正方形边长）</summary>
        private float _cellSize = DefaultCellSize;

        /// <summary>格子间距</summary>
        private float _cellGap = DefaultCellGap;

        /// <summary>背包容量（默认 36 + 扩展背包提供的额外格子数）</summary>
        private int _capacity = DefaultCapacity;

        // ===================================================================
        // 子控件集合
        // =======================================================================

        /// <summary>当前所有普通格子（含空格，含扩展格）</summary>
        private readonly List<InkBackpackCell> _cells = new List<InkBackpackCell>();

        /// <summary>顶部 4 个背包槽位</summary>
        private readonly List<InkBagSlot> _bagSlots = new List<InkBagSlot>();

        // ===================================================================
        // 公共事件
        // =======================================================================

        /// <summary>
        /// 格子悬停事件。参数：格子索引、鼠标屏幕坐标（<see cref="FlaxEngine.Input.MouseScreenPosition"/>）。
        /// 由 <see cref="InkBackpackCell.Hovered"/> 冒泡触发。
        /// </summary>
        public event Action<int, Float2> CellHovered;

        /// <summary>
        /// 格子悬停结束事件（鼠标离开格子时触发）。
        /// 由 <see cref="InkBackpackCell.HoverEnded"/> 冒泡触发。
        /// </summary>
        public event Action CellHoverEnded;

        /// <summary>
        /// 格子双击事件。参数：格子索引。
        /// 由 <see cref="InkBackpackCell.DoubleClicked"/> 冒泡触发。
        /// </summary>
        public event Action<int> CellDoubleClicked;

        /// <summary>
        /// 背包槽双击事件。参数：槽位索引（0-3）。
        /// 由 <see cref="InkBagSlot.DoubleClicked"/> 冒泡触发。
        /// </summary>
        public event Action<int> BagSlotDoubleClicked;

        /// <summary>
        /// 背包槽悬停事件。
        /// 参数：鼠标屏幕坐标、槽位索引（0-3）、已装备背包信息（null 表示空槽）。
        /// 由 <see cref="InkBagSlot.Hovered"/> 冒泡触发。
        /// </summary>
        public event Action<Float2, int, EquippedBag?> BagSlotHovered;

        /// <summary>
        /// 背包槽悬停结束事件。由 <see cref="InkBagSlot.HoverEnded"/> 冒泡触发。
        /// </summary>
        public event Action BagSlotHoverEnded;

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化垂直滚动面板，并创建顶部 4 个背包槽。
        /// 设置 <see cref="Panel.ScrollBars"/> 为 <see cref="ScrollBars.Vertical"/>，
        /// 并裁剪超出可视区域的子控件。
        /// </summary>
        public InkBackpackGrid()
        {
            BackgroundColor = InkWashTheme.BaseSecondary;
            ClipChildren = true;
            ScrollBars = ScrollBars.Vertical;
            AutoFocus = false;

            // 创建 4 个背包槽并订阅事件
            for (int i = 0; i < DefaultBagSlotCount; i++)
            {
                var slot = new InkBagSlot
                {
                    BagSlotIndex = i,
                    Size = new Float2(_cellSize, _cellSize),
                };

                // 闭包捕获：每个槽位的事件转发需要携带自身索引与装备状态
                int capturedIndex = i;
                InkBagSlot capturedSlot = slot;

                slot.DoubleClicked += idx =>
                {
                    try { BagSlotDoubleClicked?.Invoke(idx); }
                    catch (Exception ex)
                    {
                        FlaxEngine.Debug.LogError(
                            $"[InkBackpackGrid] BagSlotDoubleClicked 触发失败: {ex.Message}");
                    }
                };
                slot.Hovered += pos =>
                {
                    try { BagSlotHovered?.Invoke(pos, capturedIndex, capturedSlot.EquippedBag); }
                    catch (Exception ex)
                    {
                        FlaxEngine.Debug.LogError(
                            $"[InkBackpackGrid] BagSlotHovered 触发失败: {ex.Message}");
                    }
                };
                slot.HoverEnded += () =>
                {
                    try { BagSlotHoverEnded?.Invoke(); }
                    catch (Exception ex)
                    {
                        FlaxEngine.Debug.LogError(
                            $"[InkBackpackGrid] BagSlotHoverEnded 触发失败: {ex.Message}");
                    }
                };

                _bagSlots.Add(slot);
                AddChild(slot);
            }
        }

        // ===================================================================
        // 公共属性
        // =======================================================================

        /// <summary>
        /// 列数（默认 6）。设置时钳制为不小于 1。
        /// </summary>
        public int Columns
        {
            get => _columns;
            set => _columns = Math.Max(1, value);
        }

        /// <summary>
        /// 格子尺寸（默认 56x56）。设置时钳制为不小于 1。
        /// </summary>
        public float CellSize
        {
            get => _cellSize;
            set => _cellSize = Math.Max(1f, value);
        }

        /// <summary>
        /// 格子间距（默认 6px）。设置时钳制为不小于 0。
        /// </summary>
        public float CellGap
        {
            get => _cellGap;
            set => _cellGap = Math.Max(0f, value);
        }

        /// <summary>
        /// 背包容量（默认 36 + 扩展背包提供的额外格子数），用于判断是否已满。
        /// </summary>
        public int Capacity
        {
            get => _capacity;
            set => _capacity = Math.Max(0, value);
        }

        /// <summary>
        /// 已使用格子数（装备非空的格子数）。
        /// </summary>
        public int UsedCount
        {
            get
            {
                if (_cells == null || _cells.Count == 0)
                    return 0;
                int count = 0;
                for (int i = 0; i < _cells.Count; i++)
                {
                    if (_cells[i] != null && _cells[i].Equipment != null)
                        count++;
                }
                return count;
            }
        }

        /// <summary>
        /// 背包是否已满。
        /// </summary>
        public bool IsFull => UsedCount >= Capacity;

        // ===================================================================
        // 公共方法
        // =======================================================================

        /// <summary>
        /// 填充格子（向后兼容重载）。等价于 <see cref="Populate(List{EquipmentData}, List{EquippedBag})"/>
        /// 传入 null 背包列表。
        /// </summary>
        /// <param name="items">装备列表，可为 null（视为空列表）</param>
        public void Populate(List<EquipmentData> items)
        {
            Populate(items, null);
        }

        /// <summary>
        /// 填充格子（魔兽世界式）。先清空旧普通格子，再根据 items 创建新 <see cref="InkBackpackCell"/>。
        /// 空格子也会创建（用于显示背包剩余容量）：当 items 数量小于 <see cref="Capacity"/> 时，
        /// 补齐空格至 <see cref="Capacity"/>；当 items 数量大于 <see cref="Capacity"/> 时，
        /// 按 items 实际数量创建。
        /// 根据 equippedBags 更新 4 个 <see cref="InkBagSlot"/> 的显示状态，
        /// 并计算 TotalCapacity = 36 + Σ(ExtraSlots)，扩展格（索引 ≥ 36）与默认格视觉区分。
        /// 每个格子的图标通过 <see cref="Content.LoadAsync{T}"/> 异步加载，
        /// 加载失败时记录日志但不影响格子创建。
        /// </summary>
        /// <param name="items">装备列表，可为 null（视为空列表）</param>
        /// <param name="equippedBags">已装备扩展背包列表，可为 null（视为无扩展背包）</param>
        public void Populate(List<EquipmentData> items, List<EquippedBag> equippedBags)
        {
            try
            {
                ClearCells();

                // 1. 更新 4 个背包槽的显示状态
                UpdateBagSlots(equippedBags);

                // 2. 计算总容量 = 默认 36 + Σ(扩展背包 ExtraSlots)
                int extraSlots = 0;
                if (equippedBags != null)
                {
                    for (int i = 0; i < equippedBags.Count; i++)
                    {
                        extraSlots += equippedBags[i].ExtraSlots;
                    }
                }
                _capacity = DefaultCapacity + extraSlots;

                // 3. 创建普通格子（含扩展格）
                int itemCount = items != null ? items.Count : 0;
                int totalCells = Math.Max(itemCount, _capacity);
                if (totalCells <= 0)
                {
                    ApplyLayout();
                    return;
                }

                for (int i = 0; i < totalCells; i++)
                {
                    var cell = new InkBackpackCell
                    {
                        Index = i,
                        Size = new Float2(_cellSize, _cellSize),
                        // 索引 ≥ 默认容量视为扩展格（视觉区分）
                        IsExtraSlot = i >= DefaultCapacity,
                    };

                    // 绑定装备数据与图标
                    if (i < itemCount && items[i] != null)
                    {
                        cell.Equipment = items[i];
                        cell.Count = 1;
                        try
                        {
                            if (!string.IsNullOrEmpty(items[i].IconPath))
                            {
                                cell.Icon = Content.LoadAsync<Texture>(items[i].IconPath);
                            }
                        }
                        catch (Exception ex)
                        {
                            FlaxEngine.Debug.LogWarning(
                                $"[InkBackpackGrid] 加载装备图标失败: {items[i].IconPath} — {ex.Message}");
                        }
                    }

                    // 绑定事件（事件参数 idx 即格子索引，直接冒泡到网格事件）
                    cell.Hovered += (idx, pos) =>
                    {
                        try { CellHovered?.Invoke(idx, pos); }
                        catch (Exception ex)
                        {
                            FlaxEngine.Debug.LogError(
                                $"[InkBackpackGrid] CellHovered 触发失败: {ex.Message}");
                        }
                    };
                    cell.HoverEnded += () =>
                    {
                        try { CellHoverEnded?.Invoke(); }
                        catch (Exception ex)
                        {
                            FlaxEngine.Debug.LogError(
                                $"[InkBackpackGrid] CellHoverEnded 触发失败: {ex.Message}");
                        }
                    };
                    cell.DoubleClicked += idx =>
                    {
                        try { CellDoubleClicked?.Invoke(idx); }
                        catch (Exception ex)
                        {
                            FlaxEngine.Debug.LogError(
                                $"[InkBackpackGrid] CellDoubleClicked 触发失败: {ex.Message}");
                        }
                    };

                    AddChild(cell);
                    _cells.Add(cell);
                }

                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkBackpackGrid] Populate 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 清空所有普通格子与背包槽装备状态。移除子控件并重置滚动偏移。
        /// 背包槽控件本身保留（仅清空装备数据）。
        /// </summary>
        public void Clear()
        {
            try
            {
                ClearCells();

                // 清空背包槽装备状态
                for (int i = 0; i < _bagSlots.Count; i++)
                {
                    if (_bagSlots[i] != null)
                        _bagSlots[i].EquippedBag = null;
                }

                _capacity = DefaultCapacity;
                ViewOffset = Float2.Zero;
                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkBackpackGrid] Clear 失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 私有辅助方法
        // =======================================================================

        /// <summary>
        /// 仅清空普通格子子控件（不动背包槽）。
        /// </summary>
        private void ClearCells()
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                if (_cells[i] != null)
                {
                    RemoveChild(_cells[i]);
                }
            }
            _cells.Clear();
        }

        /// <summary>
        /// 根据 equippedBags 更新 4 个 <see cref="InkBagSlot"/> 的装备状态。
        /// 先清空所有背包槽，再按 BagSlotIndex 匹配填入。
        /// </summary>
        /// <param name="equippedBags">已装备扩展背包列表，可为 null</param>
        private void UpdateBagSlots(List<EquippedBag> equippedBags)
        {
            // 先清空所有背包槽
            for (int i = 0; i < _bagSlots.Count; i++)
            {
                if (_bagSlots[i] != null)
                    _bagSlots[i].EquippedBag = null;
            }

            if (equippedBags == null || equippedBags.Count == 0)
                return;

            // 按 BagSlotIndex 匹配填入
            for (int i = 0; i < equippedBags.Count; i++)
            {
                int slotIndex = equippedBags[i].BagSlotIndex;
                if (slotIndex >= 0 && slotIndex < _bagSlots.Count && _bagSlots[slotIndex] != null)
                {
                    _bagSlots[slotIndex].EquippedBag = equippedBags[i];
                }
            }
        }

        // ===================================================================
        // 布局计算
        // =======================================================================

        /// <summary>
        /// 根据当前布局参数重新计算所有子控件的位置与尺寸。
        /// 顶部一行布局 4 个背包槽（左对齐，与普通格子同尺寸），
        /// 下方按 6 列网格布局普通格子（含扩展格）。
        /// 布局公式：
        /// <c>x = colIndex * (CellSize + CellGap)</c>，
        /// <c>y = bagRowHeight + BagSlotRowGap + rowIndex * (CellSize + CellGap)</c>。
        /// 控件内容总高度 = <c>背包槽行高 + 间距 + 行数 * (CellSize + CellGap)</c>，
        /// 超出可视区域时由 <see cref="Panel"/> 垂直滚动条处理。
        /// </summary>
        private void ApplyLayout()
        {
            try
            {
                // 1. 顶部布局 4 个背包槽（一行，左对齐）
                for (int i = 0; i < _bagSlots.Count; i++)
                {
                    if (_bagSlots[i] == null)
                        continue;
                    float x = i * (_cellSize + _cellGap);
                    _bagSlots[i].Location = new Float2(x, 0f);
                    _bagSlots[i].Size = new Float2(_cellSize, _cellSize);
                }

                // 2. 下方布局普通格子（6 列网格）
                float cellsStartY = _cellSize + BagSlotRowGap;
                for (int i = 0; i < _cells.Count; i++)
                {
                    if (_cells[i] == null)
                        continue;
                    int col = i % _columns;
                    int row = i / _columns;
                    float x = col * (_cellSize + _cellGap);
                    float y = cellsStartY + row * (_cellSize + _cellGap);
                    _cells[i].Location = new Float2(x, y);
                    _cells[i].Size = new Float2(_cellSize, _cellSize);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkBackpackGrid] ApplyLayout 失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 内部类：背包格子
        // =======================================================================

        /// <summary>
        /// 背包格子控件。
        /// 继承 <see cref="ContainerControl"/>，通过 <see cref="Draw"/> 绘制：
        /// <list type="bullet">
        ///   <item>有装备：图标 + 品质色边框 + 右下角数量标签（<see cref="Count"/> &gt; 1 时）</item>
        ///   <item>默认空格（<see cref="IsExtraSlot"/>=false）：暗色边框（<see cref="InkWashTheme.BorderGold"/>）</item>
        ///   <item>扩展空格（<see cref="IsExtraSlot"/>=true）：稍亮背景（<see cref="InkWashTheme.BaseElevated"/>）+ 金色强边框</item>
        /// </list>
        /// 覆写 <see cref="OnMouseDown"/>/<see cref="OnMouseUp"/> 检测双击
        /// （两次点击间隔 &lt; 500ms），覆写 <see cref="OnMouseEnter"/>/<see cref="OnMouseLeave"/>
        /// 触发悬停事件。双击检测使用 <see cref="Time.UnscaledGameTime"/> 记录上次点击时间。
        /// </summary>
        private class InkBackpackCell : ContainerControl
        {
            /// <summary>双击判定阈值（秒），两次点击间隔小于此值视为双击</summary>
            private const float DoubleClickThreshold = 0.5f;

            /// <summary>默认格子背景色（rgba(0,0,0,0.35)）</summary>
            private static readonly Color CellBackground = new Color(0f, 0f, 0f, 0.35f);

            /// <summary>扩展格子背景色（BaseElevated 半透明，与默认格视觉区分）</summary>
            private static readonly Color ExtraCellBackground = new Color(
                InkWashTheme.BaseElevated.R, InkWashTheme.BaseElevated.G,
                InkWashTheme.BaseElevated.B, 0.5f);

            /// <summary>默认空格子边框色</summary>
            private static readonly Color EmptyBorder = InkWashTheme.BorderGold;

            /// <summary>扩展空格子边框色（金色强边框，视觉强调）</summary>
            private static readonly Color ExtraEmptyBorder = InkWashTheme.BorderGoldStrong;

            /// <summary>鼠标左键是否按下（用于点击释放判定）</summary>
            private bool _isMouseDown;

            /// <summary>上次点击时间（<see cref="Time.UnscaledGameTime"/>），-1 表示未点击或已触发双击</summary>
            private float _lastClickTime = -1f;

            /// <summary>格子索引（对应在背包中的位置）</summary>
            public int Index;

            /// <summary>格子装备，null 表示空格</summary>
            public EquipmentData Equipment;

            /// <summary>图标纹理，null 时不绘制图标</summary>
            public Texture Icon;

            /// <summary>物品数量（默认 1，大于 1 时绘制右下角数量标签）</summary>
            public int Count = 1;

            /// <summary>
            /// 是否为扩展格子（索引 ≥ 默认容量 36）。
            /// true 时空格使用稍亮背景与强边框，与默认格视觉区分。
            /// </summary>
            public bool IsExtraSlot;

            /// <summary>
            /// 双击事件。两次左键点击间隔小于 <see cref="DoubleClickThreshold"/> 时触发。
            /// 参数：格子索引。
            /// </summary>
            public event Action<int> DoubleClicked;

            /// <summary>
            /// 悬停事件。参数：格子索引、鼠标屏幕坐标（<see cref="FlaxEngine.Input.MouseScreenPosition"/>）。
            /// </summary>
            public event Action<int, Float2> Hovered;

            /// <summary>
            /// 悬停结束事件。鼠标离开格子时触发。
            /// </summary>
            public event Action HoverEnded;

            /// <summary>
            /// 构造函数：透明背景，不裁剪子控件。
            /// </summary>
            public InkBackpackCell()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var bounds = new Rectangle(0, 0, Width, Height);

                // 1. 背景（扩展格使用稍亮背景与默认格区分）
                Color bg = IsExtraSlot ? ExtraCellBackground : CellBackground;
                Render2D.FillRectangle(bounds, bg);

                if (Equipment != null)
                {
                    // 2. 图标（居中，占 70%）
                    if (Icon != null && Icon.IsLoaded)
                    {
                        float iconSize = Mathf.Min(Width, Height) * 0.7f;
                        float iconX = (Width - iconSize) * 0.5f;
                        float iconY = (Height - iconSize) * 0.5f;
                        Render2D.DrawTexture(
                            Icon,
                            new Rectangle(iconX, iconY, iconSize, iconSize),
                            Color.White);
                    }

                    // 3. 品质色边框
                    var quality = MapQuality(Equipment.Quality);
                    Color borderColor = InkWashTheme.QualityColor(quality);
                    Render2D.DrawRectangle(bounds, borderColor, 1f);

                    // 4. 右下角数量标签（仅 Count > 1 时绘制）
                    if (Count > 1)
                    {
                        var fontRef = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f);
                        var font = fontRef.GetFont();
                        if (font != null)
                        {
                            var badgeRect = new Rectangle(
                                Width * 0.3f, Height * 0.55f,
                                Width * 0.68f, Height * 0.42f);
                            Render2D.DrawText(
                                font,
                                Count.ToString(),
                                badgeRect,
                                InkWashTheme.GoldBright,
                                TextAlignment.Far,
                                TextAlignment.Near,
                                TextWrapping.NoWrap);
                        }
                    }
                }
                else
                {
                    // 空格子：默认格用暗色边框，扩展格用强边框
                    Color emptyBorder = IsExtraSlot ? ExtraEmptyBorder : EmptyBorder;
                    Render2D.DrawRectangle(bounds, emptyBorder, 1f);
                }
            }

            /// <inheritdoc />
            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                base.OnMouseDown(location, button);
                if (button == MouseButton.Left)
                    _isMouseDown = true;
                return true;
            }

            /// <inheritdoc />
            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                base.OnMouseUp(location, button);
                if (button == MouseButton.Left && _isMouseDown)
                {
                    _isMouseDown = false;
                    // 判定点击是否在格子范围内
                    if (location.X >= 0f && location.X <= Width &&
                        location.Y >= 0f && location.Y <= Height)
                    {
                        // 双击检测：使用 UnscaledGameTime 避免受时间缩放影响
                        float now = Time.UnscaledGameTime;
                        if (_lastClickTime > 0f && (now - _lastClickTime) < DoubleClickThreshold)
                        {
                            try
                            {
                                DoubleClicked?.Invoke(Index);
                            }
                            catch (Exception ex)
                            {
                                FlaxEngine.Debug.LogError(
                                    $"[InkBackpackCell] DoubleClicked 触发失败: {ex.Message}");
                            }
                            _lastClickTime = -1f;
                        }
                        else
                        {
                            _lastClickTime = now;
                        }
                    }
                }
                return true;
            }

            /// <inheritdoc />
            public override void OnMouseEnter(Float2 location)
            {
                base.OnMouseEnter(location);
                try
                {
                    // 使用控件本地坐标转换为窗口客户区坐标，与宿主页面 UI 坐标系一致，
                    // 避免 MouseScreenPosition 在编辑器/窗口模式下使用显示器坐标导致的偏移。
                    Float2 screenPos = PointToScreen(location);
                    Hovered?.Invoke(Index, screenPos);
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError(
                        $"[InkBackpackCell] Hovered 触发失败: {ex.Message}");
                }
            }

            /// <inheritdoc />
            public override void OnMouseLeave()
            {
                base.OnMouseLeave();
                try
                {
                    HoverEnded?.Invoke();
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError(
                        $"[InkBackpackCell] HoverEnded 触发失败: {ex.Message}");
                }
            }

            /// <summary>
            /// 将 <see cref="EquipmentData.Quality"/>（0-5）映射到
            /// <see cref="InkWashTheme.InkQuality"/>（0-4），5 钳制为 Legendary。
            /// </summary>
            /// <param name="quality">装备品质（0=白,1=绿,2=蓝,3=紫,4=橙,5=红）</param>
            /// <returns>对应的 <see cref="InkWashTheme.InkQuality"/> 枚举值</returns>
            private static InkWashTheme.InkQuality MapQuality(int quality)
            {
                int clamped = quality < 0 ? 0 : (quality > 4 ? 4 : quality);
                return (InkWashTheme.InkQuality)clamped;
            }
        }

        // ===================================================================
        // 内部类：背包槽（魔兽世界式扩展背包槽位）
        // =======================================================================

        /// <summary>
        /// 背包槽控件（用于装备扩展背包物品）。
        /// 继承 <see cref="ContainerControl"/>，通过 <see cref="Draw"/> 绘制：
        /// <list type="bullet">
        ///   <item>已装备：背包图标 + 品质色边框（2px）</item>
        ///   <item>空槽：暗色边框（1px）+ "背包槽"文字提示居中</item>
        /// </list>
        /// 覆写 <see cref="OnMouseDown"/>/<see cref="OnMouseUp"/> 检测双击
        /// （两次点击间隔 &lt; 500ms），覆写 <see cref="OnMouseEnter"/>/<see cref="OnMouseLeave"/>
        /// 触发悬停事件。实现风格与 <see cref="InkBackpackCell"/> 保持一致。
        /// </summary>
        private class InkBagSlot : ContainerControl
        {
            /// <summary>双击判定阈值（秒），两次点击间隔小于此值视为双击</summary>
            private const float DoubleClickThreshold = 0.5f;

            /// <summary>槽位背景色（rgba(0,0,0,0.35)）</summary>
            private static readonly Color SlotBackground = new Color(0f, 0f, 0f, 0.35f);

            /// <summary>空槽边框色（暗色）</summary>
            private static readonly Color EmptyBorder = InkWashTheme.BorderNeutralL3;

            /// <summary>已装备背包时品质色边框厚度</summary>
            private const float QualityBorderThickness = 2f;

            /// <summary>空槽边框厚度</summary>
            private const float EmptyBorderThickness = 1f;

            /// <summary>空槽提示文字字号</summary>
            private const float EmptyHintFontSize = 10f;

            /// <summary>鼠标左键是否按下（用于点击释放判定）</summary>
            private bool _isMouseDown;

            /// <summary>上次点击时间（<see cref="Time.UnscaledGameTime"/>），-1 表示未点击或已触发双击</summary>
            private float _lastClickTime = -1f;

            /// <summary>槽位索引（0-3，对应背包槽位置）</summary>
            public int BagSlotIndex;

            /// <summary>
            /// 当前已装备的扩展背包，null 表示空槽。
            /// 设置时自动加载对应背包装备的图标与品质。
            /// </summary>
            public EquippedBag? EquippedBag
            {
                get => _equippedBag;
                set
                {
                    _equippedBag = value;
                    if (value.HasValue)
                    {
                        // 通过 TemplateId 查询背包装备数据，加载图标与品质
                        var equipData = EquipmentDatabase.GetEquipment(value.Value.TemplateId);
                        if (equipData != null)
                        {
                            _quality = equipData.Quality;
                            if (!string.IsNullOrEmpty(equipData.IconPath))
                            {
                                try
                                {
                                    Icon = Content.LoadAsync<Texture>(equipData.IconPath);
                                }
                                catch (Exception ex)
                                {
                                    FlaxEngine.Debug.LogWarning(
                                        $"[InkBagSlot] 加载背包图标失败: {equipData.IconPath} — {ex.Message}");
                                    Icon = null;
                                }
                            }
                            else
                            {
                                Icon = null;
                            }
                        }
                        else
                        {
                            _quality = 0;
                            Icon = null;
                        }
                    }
                    else
                    {
                        _quality = 0;
                        Icon = null;
                    }
                }
            }

            /// <summary>当前已装备的扩展背包（私有字段）</summary>
            private EquippedBag? _equippedBag;

            /// <summary>背包图标纹理，null 时不绘制图标</summary>
            public Texture Icon;

            /// <summary>背包装备品质（0-5），用于映射品质色边框</summary>
            private int _quality = 0;

            /// <summary>
            /// 双击事件。两次左键点击间隔小于 <see cref="DoubleClickThreshold"/> 时触发。
            /// 参数：槽位索引。
            /// </summary>
            public event Action<int> DoubleClicked;

            /// <summary>
            /// 悬停事件。参数：鼠标屏幕坐标（<see cref="FlaxEngine.Input.MouseScreenPosition"/>）。
            /// </summary>
            public event Action<Float2> Hovered;

            /// <summary>
            /// 悬停结束事件。鼠标离开槽位时触发。
            /// </summary>
            public event Action HoverEnded;

            /// <summary>
            /// 构造函数：透明背景，不裁剪子控件。
            /// </summary>
            public InkBagSlot()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var bounds = new Rectangle(0, 0, Width, Height);

                // 1. 背景
                Render2D.FillRectangle(bounds, SlotBackground);

                if (_equippedBag.HasValue)
                {
                    // 2. 已装备：绘制背包图标（居中，占 70%）
                    if (Icon != null && Icon.IsLoaded)
                    {
                        float iconSize = Mathf.Min(Width, Height) * 0.7f;
                        float iconX = (Width - iconSize) * 0.5f;
                        float iconY = (Height - iconSize) * 0.5f;
                        Render2D.DrawTexture(
                            Icon,
                            new Rectangle(iconX, iconY, iconSize, iconSize),
                            Color.White);
                    }

                    // 3. 品质色边框（2px）
                    var quality = MapQuality(_quality);
                    Color borderColor = InkWashTheme.QualityColor(quality);
                    Render2D.DrawRectangle(bounds, borderColor, QualityBorderThickness);
                }
                else
                {
                    // 4. 空槽：暗色边框 + "背包槽"文字提示
                    Render2D.DrawRectangle(bounds, EmptyBorder, EmptyBorderThickness);

                    var fontRef = InkRenderHelper.GetFontRef(
                        InkWashTheme.FontRole.Body, EmptyHintFontSize);
                    var font = fontRef.GetFont();
                    if (font != null)
                    {
                        Render2D.DrawText(
                            font,
                            "背包槽",
                            bounds,
                            InkWashTheme.TextTertiary,
                            TextAlignment.Center,
                            TextAlignment.Center,
                            TextWrapping.NoWrap);
                    }
                }
            }

            /// <inheritdoc />
            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                base.OnMouseDown(location, button);
                if (button == MouseButton.Left)
                    _isMouseDown = true;
                return true;
            }

            /// <inheritdoc />
            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                base.OnMouseUp(location, button);
                if (button == MouseButton.Left && _isMouseDown)
                {
                    _isMouseDown = false;
                    // 判定点击是否在槽位范围内
                    if (location.X >= 0f && location.X <= Width &&
                        location.Y >= 0f && location.Y <= Height)
                    {
                        // 双击检测：使用 UnscaledGameTime 避免受时间缩放影响
                        float now = Time.UnscaledGameTime;
                        if (_lastClickTime > 0f && (now - _lastClickTime) < DoubleClickThreshold)
                        {
                            try
                            {
                                DoubleClicked?.Invoke(BagSlotIndex);
                            }
                            catch (Exception ex)
                            {
                                FlaxEngine.Debug.LogError(
                                    $"[InkBagSlot] DoubleClicked 触发失败: {ex.Message}");
                            }
                            _lastClickTime = -1f;
                        }
                        else
                        {
                            _lastClickTime = now;
                        }
                    }
                }
                return true;
            }

            /// <inheritdoc />
            public override void OnMouseEnter(Float2 location)
            {
                base.OnMouseEnter(location);
                try
                {
                    // 使用控件本地坐标转换为窗口客户区坐标，与宿主页面 UI 坐标系一致，
                    // 避免 MouseScreenPosition 在编辑器/窗口模式下使用显示器坐标导致的偏移。
                    Float2 screenPos = PointToScreen(location);
                    Hovered?.Invoke(screenPos);
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError(
                        $"[InkBagSlot] Hovered 触发失败: {ex.Message}");
                }
            }

            /// <inheritdoc />
            public override void OnMouseLeave()
            {
                base.OnMouseLeave();
                try
                {
                    HoverEnded?.Invoke();
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError(
                        $"[InkBagSlot] HoverEnded 触发失败: {ex.Message}");
                }
            }

            /// <summary>
            /// 将 <see cref="EquipmentData.Quality"/>（0-5）映射到
            /// <see cref="InkWashTheme.InkQuality"/>（0-4），5 钳制为 Legendary。
            /// 与 <see cref="InkBackpackCell.MapQuality"/> 保持一致。
            /// </summary>
            /// <param name="quality">装备品质（0=白,1=绿,2=蓝,3=紫,4=橙,5=红）</param>
            /// <returns>对应的 <see cref="InkWashTheme.InkQuality"/> 枚举值</returns>
            private static InkWashTheme.InkQuality MapQuality(int quality)
            {
                int clamped = quality < 0 ? 0 : (quality > 4 ? 4 : quality);
                return (InkWashTheme.InkQuality)clamped;
            }
        }
    }
}
