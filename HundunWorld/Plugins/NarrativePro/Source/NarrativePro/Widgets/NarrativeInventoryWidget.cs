using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using NarrativePro.CommonUI;
using NarrativePro.CommonUI.Widgets;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Widgets
{
    /// <summary>
    /// 背包网格控件：把一件背包（<see cref="NarrativeInventoryComponent"/>）显示成可点击的物品格子网格。
    /// 移植自 UE5 NarrativeArsenal: <c>Items/Widgets/InventoryWidget.h</c>
    /// （<c>UInventoryWidget : UNarrativeActivatableWidget</c>），子控件为
    /// <see cref="NarrativeInventoryItemButton"/>。
    /// <para>
    /// 模块关系：向上挂在 <see cref="NarrativeActivatableWidgetContainer"/>（HUD 的层激活栈）上，
    /// 激活/停用与输入模式由基类 <see cref="NarrativeActivatableWidget"/> 经
    /// <see cref="NarrativeInputRouter"/> 决定（本控件按 Menu 模式打开，鼠标交还 UI）；
    /// 向下绑定 <see cref="NarrativeInventoryComponent"/> 作为唯一数据源，订阅其
    /// <c>OnInventoryUpdated</c> 做整格重建，并订阅每个 <see cref="NarrativeItem.OnItemModified"/>
    /// 做单格刷新；格子自身不决策，点击一律回调到本控件（UE5 的 OwnerWidget 反向指针）。
    /// </para>
    /// <para>
    /// UE5 依据（只读参考工程 <c>C:\Works\GitHubProjects\20260801\HundunWorld\Plugins\NarrativePro\Source\NarrativeArsenal</c>）：
    /// <list type="bullet">
    /// <item><description><c>Public/Items/Widgets/InventoryWidget.h:9-17</c>：
    /// <c>UInventoryWidget : UNarrativeActivatableWidget</c>，注释 "Inventory widgets use this as a base class -
    /// the looting menu, player inventory, etc."，类体为空；
    /// <c>Private/Items/Widgets/InventoryWidget.cpp:1-4</c> 只有 include ——
    /// 即 UE5 的网格布局、刷新与点击逻辑全在 WBP 蓝图里，C++ 只给出"这是一个可激活的背包类控件"的契约。</description></item>
    /// <item><description>格子数据面取自 <c>Public/Items/InventoryComponent.h</c>：<c>:449</c> <c>GetItems()</c>
    /// （网格条目）、<c>:479</c> <c>OnInventoryUpdated</c>（变化刷新）、<c>:416</c> <c>GetItemsUsableWith()</c>
    /// （两件物品互用的候选集）。</description></item>
    /// <item><description>点击决策取自 <c>Public/Items/NarrativeItem.h</c>：<c>:239-241</c>
    /// <c>bUsedWithOtherItem</c>（"using this item will open a prompt asking which other item you'd like to use it on.
    /// All items passing CanUseItemWith() will be shown"）、<c>:389-391</c> <c>Use(OtherItem)</c>
    /// （注释写明 OtherItem 是"拖到的目标物品"）、<c>:377-379</c> <c>GetItemUseActions()</c>、
    /// <c>:472-474</c> <c>ShouldShowInInventory()</c>（是否进网格）、<c>:226-228</c> <c>ItemWidgetOverride</c>
    /// （每类物品可用不同按钮类）。</description></item>
    /// <item><description><c>Public/Items/Widgets/NarrativeInventoryItemButton.h:26-28</c>
    /// 的 <c>OwnerWidget</c> 指明格子必须回调背包控件，本端口据此把使用/选中决策收回控件侧。</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// 端口差异：
    /// <list type="bullet">
    /// <item><description>UE5 的网格是 <c>UniformGridPanel</c> / <c>WrapBox</c> + <c>ScrollBox</c> 蓝图布局，
    /// Flax 无 UMG 也不引入预制体资产：这里用真实 <see cref="Panel"/>（带
    /// <see cref="FlaxEngine.GUI.ScrollBars.Vertical"/>，条目超出高度即滚动）+ 代码排格子，
    /// 列数按父容器可用宽度回退（<see cref="PerformLayout"/>），列宽/行高由
    /// <see cref="SlotSize"/>、<see cref="SlotSpacing"/> 决定。</description></item>
    /// <item><description>UE5 的一个 UMG slot 对应 Flax 的一个子控件：条目按钮 <see cref="NarrativeItem"/> →
    /// <see cref="NarrativeInventoryItemButton"/> 子控件，"选中"用基类的 Selected 相
    /// （<c>ForceSetIsSelected</c>）而不是 UE5 的 Slate 焦点。</description></item>
    /// <item><description>UE5 的 <c>TSubclassOf&lt;UNarrativeInventoryItemButton&gt; ItemWidgetOverride</c>
    /// 在 Flax 侧没有物品字段可用（本地 <see cref="NarrativeItem"/> 未带该属性），因此改成
    /// <see cref="ItemButtonFactory"/> / <see cref="ItemButtonType"/> / <see cref="CreateItemButton"/>
    /// 三级覆盖点，宿主可按物品返回不同按钮类。</description></item>
    /// <item><description>UE5 的拖拽（Slate DragDrop，配件装到武器）在 Flax GUI 里没有等价物，
    /// 因此把 <c>Use(OtherItem)</c> 的两个操作数改成"两段式点选"：先选中源物品进入待选态
    /// （<see cref="PendingUseItem"/>，只有 <c>GetItemsUsableWith</c> 通过的格子保持可点），
    /// 再点目标格子提交；右键为源物品的快捷入口。</description></item>
    /// <item><description>键盘导航不依赖 Flax 的输入动作表（项目未内置背包动作映射），
    /// 改为 <see cref="Update"/> 里轮询方向键/回车/Esc 边沿，避免虚构输入配置依赖。</description></item>
    /// </list>
    /// </para>
    /// </summary>
    public class NarrativeInventoryWidget : NarrativeActivatableWidget
    {
        /// <summary>网格列数上限（父容器宽度不足时自动减列）。</summary>
        public int Columns = 6;

        /// <summary>单格尺寸（像素）。</summary>
        public Float2 SlotSize = new Float2(170f, 52f);

        /// <summary>格子间距（像素）。</summary>
        public float SlotSpacing = 6f;

        /// <summary>面板内边距（像素）。</summary>
        public float PanelPadding = 12f;

        /// <summary>标题区高度（像素）。</summary>
        public float HeaderHeight = 52f;

        /// <summary>底部提示区高度（像素）。</summary>
        public float FooterHeight = 30f;

        /// <summary>网格可视区最大高度，超过则走滚动（对应 UE5 ScrollBox 的 HeightBox 上限）。</summary>
        public float MaxGridHeight = 300f;

        /// <summary>面板与父容器边缘保留的最小间距（像素）。</summary>
        public float ScreenMargin = 32f;

        /// <summary>左键点击是否立即使用物品。为 false 时左键只选中，需按回车使用。</summary>
        public bool bClickUsesImmediately = true;

        /// <summary>是否按显示名排序条目（对应 <see cref="InventoryFunctionLibrary.SortItemArrayAlphabetical"/>）。</summary>
        public bool bSortAlphabetically = false;

        /// <summary>是否显示重量/容量/货币表头（对应 UE5 背包菜单的头部信息条）。</summary>
        public bool bShowHeader = true;

        /// <summary>按钮类型覆盖。对应 UE5 <c>NarrativeItem.h:226-228</c> ItemWidgetOverride 的控件侧默认。</summary>
        public Type ItemButtonType = typeof(NarrativeInventoryItemButton);

        /// <summary>按钮工厂（返回 null 时回退 <see cref="ItemButtonType"/>）。逐物品定制入口。</summary>
        public Func<NarrativeItem, NarrativeInventoryItemButton> ItemButtonFactory;

        /// <summary>当前绑定的背包（数据源）。对应 UE5 蓝图里对 InventoryComponent 的引用。</summary>
        public NarrativeInventoryComponent SourceInventory { get; private set; }

        /// <summary>当前选中的格子（null 表示无选中）。</summary>
        public NarrativeInventoryItemButton SelectedSlot { get; private set; }

        /// <summary>待选第二件物品的源物品。对应 UE5 <c>NarrativeItem.h:239-241</c> bUsedWithOtherItem 的提问态。</summary>
        public NarrativeItem PendingUseItem => _pendingUseItem;

        /// <summary>当前悬浮的格子物品（供宿主接自定义提示框）。</summary>
        public NarrativeItem HoveredItem { get; private set; }

        /// <summary>格子条目（可见顺序即网格顺序）。</summary>
        public IReadOnlyList<NarrativeInventoryItemButton> Slots => _slots;

        /// <summary>选中变化事件。</summary>
        public event Action<NarrativeItem> OnSlotSelected;

        /// <summary>物品被使用（含两件物品互用）事件。</summary>
        public event Action<NarrativeItem> OnSlotUsed;

        /// <summary>悬浮变化事件。</summary>
        public event Action<NarrativeItem> OnSlotHovered;

        /// <summary>背景板。用 <see cref="Image"/> + 画刷（Panel 底色在画刷为空时不落色）。</summary>
        public Image Backdrop { get; private set; }

        /// <summary>滚动网格容器（条目父控件）。</summary>
        public Panel ScrollPanel { get; private set; }

        /// <summary>标题（背包友好名）。对应 <see cref="NarrativeInventoryComponent.InventoryFriendlyName"/>。</summary>
        public NarrativeCommonTextBlock TitleLabel { get; private set; }

        /// <summary>重量/容量/货币摘要。</summary>
        public NarrativeCommonTextBlock SummaryLabel { get; private set; }

        /// <summary>底部操作提示。</summary>
        public NarrativeCommonTextBlock HintLabel { get; private set; }

        private readonly List<NarrativeInventoryItemButton> _slots = new List<NarrativeInventoryItemButton>();
        private readonly List<NarrativeItem> _pendingTargets = new List<NarrativeItem>();
        private NarrativeItem _pendingUseItem;
        private Guid _selectedGuid = Guid.Empty;

        private int _effectiveColumns = 1;
        private int _selectedIndex = -1;
        private bool _bNeedsRebuild = true;

        public NarrativeInventoryWidget()
        {
            OptionalNameID = "inventory";
            OptionalDisplayName = "背包";

            // 鼠标要点得到格子，因此按 Menu 模式打开（基类经 NarrativeInputRouter 屏蔽游戏输入）
            InputConfig = ENarrativeWidgetInputMode.Menu;
            bDeactivateOnBack = true;

            AnchorPreset = AnchorPresets.TopLeft;

            Backdrop = new Image
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Brush = new SolidColorBrush(new Color(0f, 0f, 0f, 0.72f)),
            };
            AddChild(Backdrop);

            TitleLabel = CreateLabel("H2");
            AddChild(TitleLabel);

            SummaryLabel = CreateLabel("Small");
            AddChild(SummaryLabel);

            ScrollPanel = new Panel(ScrollBars.Vertical, false)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                AlwaysShowScrollbars = false,
                ClipChildren = true,
            };
            AddChild(ScrollPanel);

            HintLabel = CreateLabel("Small");
            HintLabel.SetJustification(ETextJustify.Center);
            AddChild(HintLabel);
        }

        // =================================================================
        // 背包绑定（对应 UE5 蓝图把 InventoryComponent 引用传给 WBP）
        // =================================================================

        /// <summary>绑定数据源并立即重建网格。传 null 等价 <see cref="UnbindInventory"/>。</summary>
        public virtual void BindInventory(NarrativeInventoryComponent inventory)
        {
            if (ReferenceEquals(SourceInventory, inventory))
            {
                _bNeedsRebuild = true;
                return;
            }

            UnbindInventory();

            SourceInventory = inventory;
            if (SourceInventory != null)
            {
                SourceInventory.OnInventoryUpdated += HandleInventoryUpdated;
            }

            _bNeedsRebuild = true;
        }

        /// <summary>解绑数据源并清空格子。</summary>
        public virtual void UnbindInventory()
        {
            if (SourceInventory != null)
            {
                SourceInventory.OnInventoryUpdated -= HandleInventoryUpdated;
                SourceInventory = null;
            }

            _bNeedsRebuild = false;
            ClearSlots();
            UpdateHeader();
            UpdateHint();
        }

        /// <summary>背包变化：只置脏，真正重建放在 <see cref="Update"/>，避免在事件遍历中改控件树。</summary>
        private void HandleInventoryUpdated(NarrativeInventoryComponent inventory)
        {
            if (!ReferenceEquals(inventory, SourceInventory)) return;
            _bNeedsRebuild = true;
        }

        // =================================================================
        // 网格重建与布局
        // =================================================================

        /// <summary>按当前背包条目重建格子。</summary>
        public void RebuildGrid()
        {
            _bNeedsRebuild = false;
            ClearSlots();

            var items = CollectItems();
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var slot = CreateItemButton(item);
                if (slot == null) continue;

                slot.SlotIndex = _slots.Count;
                slot.ButtonClickedByUser += HandleSlotButtonClicked;
                ScrollPanel.AddChild(slot);
                slot.SetItem(item, this, _slots.Count);
                _slots.Add(slot);
            }

            _selectedIndex = -1;
            SelectedSlot = null;

            // 重建后按 GUID 找回原选中项（UE5 蓝图列表刷新同样要保住当前选择）
            if (_selectedGuid != Guid.Empty)
            {
                for (int i = 0; i < _slots.Count; i++)
                {
                    var slot = _slots[i];
                    if (slot?.Item == null) continue;
                    if (slot.Item.ItemGUID != _selectedGuid) continue;
                    SelectSlot(slot, false);
                    break;
                }
            }

            RefreshSlotTargetability();
            PerformLayout();
            UpdateHeader();
            UpdateHint();
        }

        /// <summary>
        /// 网格条目集合。对应 UE5 蓝图对 <c>GetItems()</c> 的过滤：
        /// 隐藏物品（<c>ShouldShowInInventory()</c>）不进网格。
        /// </summary>
        private List<NarrativeItem> CollectItems()
        {
            var result = new List<NarrativeItem>();
            if (SourceInventory == null) return result;

            var source = SourceInventory.Items;
            if (source == null) return result;

            for (int i = 0; i < source.Count; i++)
            {
                var item = source[i];
                if (item == null) continue;
                if (!item.ShouldShowInInventory()) continue;
                result.Add(item);
            }

            if (bSortAlphabetically)
            {
                var sorted = InventoryFunctionLibrary.SortItemArrayAlphabetical(result, false);
                if (sorted != null) return sorted;
            }

            return result;
        }

        /// <summary>为一个物品创建格子按钮（UE5 ItemWidgetOverride 的 Flax 等价落点）。</summary>
        protected virtual NarrativeInventoryItemButton CreateItemButton(NarrativeItem item)
        {
            if (ItemButtonFactory != null)
            {
                try
                {
                    var custom = ItemButtonFactory.Invoke(item);
                    if (custom != null) return custom;
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogWarning("NarrativeInventoryWidget 物品按钮工厂异常：" + ex.Message);
                }
            }

            var type = ItemButtonType ?? typeof(NarrativeInventoryItemButton);
            try
            {
                return Activator.CreateInstance(type) as NarrativeInventoryItemButton;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning("NarrativeInventoryWidget 创建物品按钮（" + type.Name + "）失败：" + ex.Message);
                return new NarrativeInventoryItemButton();
            }
        }

        private void ClearSlots()
        {
            for (int i = _slots.Count - 1; i >= 0; i--)
            {
                var slot = _slots[i];
                if (slot == null) continue;
                slot.ButtonClickedByUser -= HandleSlotButtonClicked;
                ScrollPanel?.RemoveChild(slot);
                slot.Dispose();
            }
            _slots.Clear();

            SelectedSlot = null;
            _selectedIndex = -1;
        }

        /// <summary>
        /// 重排：面板尺寸/列数按父容器可用宽度决定，条目超出可视高度即由 Panel 滚动。
        /// 一律 TopLeft 锚 + 显式 Location（非 TopLeft 锚在构造期按父尺寸 0 计算会错位）。
        /// </summary>
        public void PerformLayout()
        {
            Float2 parentSize = Parent != null ? Parent.Size : Float2.Zero;
            if (parentSize.X <= 0f || parentSize.Y <= 0f) return;

            AnchorMin = Float2.Zero;
            AnchorMax = Float2.Zero;
            Location = Float2.Zero;
            Size = parentSize;

            float availableWidth = Math.Max(SlotSize.X + PanelPadding * 2f + ScrollbarReserve,
                                            parentSize.X - ScreenMargin * 2f);

            // 列数随可用宽度回退（对应 UE5 WrapBox 的自动换行效果）
            int columns = Math.Max(1, Columns);
            float widthForColumns = availableWidth - PanelPadding * 2f - ScrollbarReserve + SlotSpacing;
            int fitColumns = (int)(widthForColumns / (SlotSize.X + SlotSpacing));
            if (fitColumns >= 1) columns = Math.Min(columns, fitColumns);
            _effectiveColumns = columns;

            int rows = (_slots.Count + columns - 1) / columns;
            float gridWidth = columns * SlotSize.X + (columns - 1) * SlotSpacing;
            float gridHeight = rows > 0 ? rows * SlotSize.Y + (rows - 1) * SlotSpacing : 0f;

            float panelWidth = Math.Min(availableWidth, gridWidth + PanelPadding * 2f + ScrollbarReserve);
            panelWidth = Math.Max(panelWidth, 260f);

            float viewportHeight = Math.Min(Math.Max(gridHeight + PanelPadding * 2f, PanelPadding * 2f), MaxGridHeight);
            if (rows == 0) viewportHeight = PanelPadding * 2f;

            float headerHeight = bShowHeader ? HeaderHeight : PanelPadding;
            float panelHeight = headerHeight + viewportHeight + FooterHeight + PanelPadding;
            float panelX = Math.Max(ScreenMargin, (parentSize.X - panelWidth) * 0.5f);
            float panelY = Math.Max(ScreenMargin * 0.5f, (parentSize.Y - panelHeight) * 0.5f);

            if (Backdrop != null)
            {
                Backdrop.Location = new Float2(panelX, panelY);
                Backdrop.Size = new Float2(panelWidth, panelHeight);
            }

            if (bShowHeader && TitleLabel != null)
            {
                TitleLabel.Location = new Float2(panelX + PanelPadding, panelY + 8f);
                TitleLabel.Size = new Float2(panelWidth - PanelPadding * 2f, 24f);
            }
            else if (TitleLabel != null)
            {
                TitleLabel.Size = Float2.Zero;
                TitleLabel.Visible = false;
            }

            if (bShowHeader && SummaryLabel != null)
            {
                SummaryLabel.Location = new Float2(panelX + PanelPadding, panelY + 30f);
                SummaryLabel.Size = new Float2(panelWidth - PanelPadding * 2f, 18f);
            }
            else if (SummaryLabel != null)
            {
                SummaryLabel.Size = Float2.Zero;
                SummaryLabel.Visible = false;
            }

            float gridTop = panelY + headerHeight;
            if (ScrollPanel != null)
            {
                ScrollPanel.Location = new Float2(panelX, gridTop);
                ScrollPanel.Size = new Float2(panelWidth, viewportHeight);
            }

            LayoutSlots(gridTop);

            if (HintLabel != null)
            {
                HintLabel.Location = new Float2(panelX, panelY + panelHeight - FooterHeight + 4f);
                HintLabel.Size = new Float2(panelWidth, FooterHeight - 8f);
            }
        }

        /// <summary>滚动条预留宽度（Panel 的 ScrollBarsSize 由引擎控制，这里给布局留出空间）。</summary>
        private const float ScrollbarReserve = 14f;

        private void LayoutSlots(float gridTop)
        {
            if (ScrollPanel == null) return;

            float x0 = PanelPadding;
            float y0 = PanelPadding;

            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (slot == null) continue;

                int column = i % _effectiveColumns;
                int row = i / _effectiveColumns;

                slot.AnchorMin = Float2.Zero;
                slot.AnchorMax = Float2.Zero;
                slot.Size = SlotSize;
                slot.Location = new Float2(
                    x0 + column * (SlotSize.X + SlotSpacing),
                    y0 + row * (SlotSize.Y + SlotSpacing));
                slot.PerformSlotLayout();
            }
        }

        private void UpdateHeader()
        {
            if (SourceInventory == null)
            {
                TitleLabel?.SetText("背包");
                SummaryLabel?.SetText("未绑定背包");
                return;
            }

            TitleLabel?.SetText(SourceInventory.InventoryFriendlyName ?? "背包");
            SummaryLabel?.SetText(
                $"重量 {SourceInventory.GetCurrentWeight():F1}/{SourceInventory.GetWeightCapacity():F1}" +
                $"  物品 {SourceInventory.GetItemCount()}/{SourceInventory.GetCapacity()}" +
                $"  金币 {SourceInventory.GetCurrency()}");
        }

        private void UpdateHint()
        {
            if (HintLabel == null) return;

            if (_pendingUseItem != null)
            {
                HintLabel.SetText($"为「{_pendingUseItem.DisplayName}」选择目标物品 · 右键/Esc 取消");
                return;
            }

            if (HoveredItem != null)
            {
                HintLabel.SetText(BuildActionLine(HoveredItem));
                return;
            }

            HintLabel.SetText(bClickUsesImmediately
                ? "左键使用 · 右键选目标 · 方向键导航 · Enter 使用 · Esc 关闭"
                : "左键选中 · 右键选目标 · 方向键导航 · Enter 使用 · Esc 关闭");
        }

        /// <summary>取物品首个可用使用动作的显示文案（对应 UE5 GetItemUseActions / GetActionDisplayName）。</summary>
        private static string BuildActionLine(NarrativeItem item)
        {
            var actions = item.GetItemUseActions();
            if (actions != null)
            {
                for (int i = 0; i < actions.Count; i++)
                {
                    var action = actions[i];
                    if (action == null || !action.IsEnabled()) continue;
                    return $"{item.DisplayName} · {action.GetActionDisplayName(item)}";
                }
            }

            return item.DisplayName ?? string.Empty;
        }

        // =================================================================
        // 点击决策（UE5 由 OwnerWidget 承担的部分）
        // =================================================================

        private void HandleSlotButtonClicked(NarrativeCommonButtonBase button)
        {
            HandleSlotClicked(button as NarrativeInventoryItemButton);
        }

        /// <summary>
        /// 左键点击格子：待选态下提交目标，否则选中并按 UE5 语义决定"提问 / 直接使用"。
        /// </summary>
        public virtual void HandleSlotClicked(NarrativeInventoryItemButton slot)
        {
            if (slot == null || slot.Item == null) return;

            if (_pendingUseItem != null)
            {
                if (ReferenceEquals(slot.Item, _pendingUseItem))
                {
                    CancelPendingUse();
                    return;
                }

                TryCompleteMultiItemUse(slot.Item);
                return;
            }

            SelectSlot(slot, true);

            // 需要第二件物品的物品：进入提问态，等待目标格子
            if (slot.Item.bUsedWithOtherItem)
            {
                BeginMultiItemUse(slot.Item);
                return;
            }

            if (bClickUsesImmediately) UseSelected();
        }

        /// <summary>
        /// 右键点击格子：有待选源时当作目标提交，否则把本物品设为源（等价 UE5 的拖起配件）。
        /// 返回 true 表示事件已被消费。
        /// </summary>
        public virtual bool HandleSlotSecondaryClicked(NarrativeInventoryItemButton slot)
        {
            if (slot == null || slot.Item == null) return false;

            if (_pendingUseItem != null)
            {
                if (ReferenceEquals(slot.Item, _pendingUseItem))
                {
                    CancelPendingUse();
                    return true;
                }

                return TryCompleteMultiItemUse(slot.Item);
            }

            SelectSlot(slot, true);
            BeginMultiItemUse(slot.Item);
            return true;
        }

        /// <summary>使用选中的物品（对应 UE5 走物品使用动作 → <c>TryUse</c>）。</summary>
        public virtual bool UseSelected()
        {
            var item = SelectedSlot != null ? SelectedSlot.Item : null;
            if (item == null || SourceInventory == null) return false;

            bool bUsed = SourceInventory.UseItem(item);
            if (bUsed) OnSlotUsed?.Invoke(item);
            _bNeedsRebuild = true;
            return bUsed;
        }

        /// <summary>进入两件物品互用的提问态。对应 UE5 <c>bUsedWithOtherItem</c> 的提示选择。</summary>
        public virtual void BeginMultiItemUse(NarrativeItem source)
        {
            if (source == null) return;

            _pendingUseItem = source;
            _pendingTargets.Clear();
            SourceInventory?.GetItemsUsableWith(source, _pendingTargets, true);

            RefreshSlotTargetability();
            UpdateHint();
        }

        /// <summary>提交目标物品完成两件物品互用。对应 UE5 <c>Use(OtherItem)</c>。</summary>
        public virtual bool TryCompleteMultiItemUse(NarrativeItem target)
        {
            if (_pendingUseItem == null || target == null) return false;

            if (_pendingTargets.Count > 0 && !_pendingTargets.Contains(target))
            {
                CancelPendingUse();
                return false;
            }

            NarrativeItem source = _pendingUseItem;
            CancelPendingUse();

            bool bUsed = source.TryUse(target);
            if (bUsed) OnSlotUsed?.Invoke(source);
            _bNeedsRebuild = true;
            return bUsed;
        }

        /// <summary>退出提问态，恢复所有格子的可点状态。</summary>
        public virtual void CancelPendingUse()
        {
            if (_pendingUseItem == null && _pendingTargets.Count == 0) return;

            _pendingUseItem = null;
            _pendingTargets.Clear();
            RefreshSlotTargetability();
            UpdateHint();
        }

        private void RefreshSlotTargetability()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (slot == null) continue;

                bool bTargetable = true;
                if (_pendingUseItem != null)
                {
                    bTargetable = ReferenceEquals(slot.Item, _pendingUseItem) ||
                                  _pendingTargets.Contains(slot.Item);
                }

                slot.SetTargetable(bTargetable);
            }
        }

        /// <summary>选中一个格子（同时把选中相下发给按钮）。</summary>
        public virtual void SelectSlot(NarrativeInventoryItemButton slot, bool bBroadcast)
        {
            if (slot == null) return;

            for (int i = 0; i < _slots.Count; i++)
            {
                var other = _slots[i];
                if (other == null || ReferenceEquals(other, slot)) continue;
                other.SetSlotSelected(false);
            }

            SelectedSlot = slot;
            _selectedIndex = slot.SlotIndex;
            _selectedGuid = slot.Item != null ? slot.Item.ItemGUID : Guid.Empty;
            slot.SetSlotSelected(true);

            slot.Focus();

            if (bBroadcast) OnSlotSelected?.Invoke(slot.Item);
        }

        /// <summary>格子悬浮回调（UE5 由格子的 OnHovered 事件通知 OwnerWidget）。</summary>
        public void HandleSlotHovered(NarrativeInventoryItemButton slot)
        {
            HoveredItem = slot != null ? slot.Item : null;
            OnSlotHovered?.Invoke(HoveredItem);
            UpdateHint();
        }

        public void HandleSlotUnhovered(NarrativeInventoryItemButton slot)
        {
            if (slot != null && !ReferenceEquals(slot.Item, HoveredItem)) return;

            HoveredItem = null;
            OnSlotHovered?.Invoke(null);
            UpdateHint();
        }

        // =================================================================
        // 键盘导航
        // =================================================================

        /// <summary>按列移动选中位（越界回绕，与 UE5 列表导航一致）。</summary>
        public void MoveSelection(int delta)
        {
            if (_slots.Count == 0) return;

            int index = _selectedIndex < 0 ? 0 : _selectedIndex + delta;
            if (index < 0) index = _slots.Count - 1;
            if (index >= _slots.Count) index = 0;

            var slot = _slots[index];
            if (slot == null) return;
            SelectSlot(slot, true);
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (!IsActivated) return;

            if (_bNeedsRebuild) RebuildGrid();

            PollNavigationKeys();
        }

        private void PollNavigationKeys()
        {
            if (Input.GetKeyDown(KeyboardKeys.Escape))
            {
                if (_pendingUseItem != null)
                {
                    CancelPendingUse();
                    return;
                }

                Close();
                return;
            }

            if (Input.GetKeyDown(KeyboardKeys.Return))
            {
                if (SelectedSlot != null) HandleSlotClicked(SelectedSlot);
                return;
            }

            if (Input.GetKeyDown(KeyboardKeys.ArrowLeft)) MoveSelection(-1);
            else if (Input.GetKeyDown(KeyboardKeys.ArrowRight)) MoveSelection(1);
            else if (Input.GetKeyDown(KeyboardKeys.ArrowUp)) MoveSelection(-_effectiveColumns);
            else if (Input.GetKeyDown(KeyboardKeys.ArrowDown)) MoveSelection(_effectiveColumns);
        }

        /// <summary>
        /// 关闭背包：从所在激活栈弹出（无栈时只停用）。对应 UE5 蓝图里
        /// <c>ContainerToUse-&gt;RemoveWidget(Widget)</c>。
        /// </summary>
        public virtual void Close()
        {
            var container = Parent as NarrativeActivatableWidgetContainer;
            if (container != null)
            {
                container.RemoveWidget(this);
                return;
            }

            Deactivate();
        }

        // =================================================================
        // 生命周期
        // =================================================================

        /// <summary>压上激活栈时重建网格并铺好布局。对应 UE5 背包 WBP 的 NativeConstruct。</summary>
        public override void NativeOnActivated()
        {
            _bNeedsRebuild = true;
            base.NativeOnActivated();
            PerformLayout();
        }

        /// <summary>从激活栈退出时清理提问态与悬浮态。</summary>
        public override void NativeOnDeactivated()
        {
            CancelPendingUse();
            HoveredItem = null;
            base.NativeOnDeactivated();
        }

        /// <summary>焦点目标：首个格子（对应 UE5 bFocusDesiredTargetOnActivate 的 GetDesiredFocusTarget）。</summary>
        protected override Control NativeGetDesiredFocusTarget()
        {
            if (_bNeedsRebuild) RebuildGrid();
            return _slots.Count > 0 ? _slots[0] : base.NativeGetDesiredFocusTarget();
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            PerformLayout();
        }

        public override void OnParentResized()
        {
            base.OnParentResized();
            PerformLayout();
        }

        /// <summary>销毁时断开背包与物品事件。</summary>
        public override void OnDestroy()
        {
            base.OnDestroy();
            UnbindInventory();
        }

        private static NarrativeCommonTextBlock CreateLabel(string style)
        {
            var label = new NarrativeCommonTextBlock
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Style = style,
                Wrapping = TextWrapping.NoWrap,
                VerticalAlignment = TextAlignment.Near,
            };
            label.SetStyle(style);
            return label;
        }

        // =================================================================
        // 宿主接线
        // =================================================================

        /// <summary>
        /// 把背包控件压到 HUD 的某个层上并绑定数据源（宿主 UI 的一行式入口）。
        /// 层为空时取菜单层，与 <see cref="NarrativeGameplayHUD"/> 已有的层注册一致。
        /// </summary>
        public static NarrativeInventoryWidget Open(NarrativeGameplayHUD hud,
                                                   NarrativeInventoryComponent inventory,
                                                   GameplayTag layerTag = default)
        {
            if (hud == null) return null;

            GameplayTag tag = layerTag.IsValid() ? layerTag : NarrativePro.Core.NarrativeGameplayTags.UI_Layer_Menu;
            var container = hud.GetOrAddLayerContainer(tag);
            if (container == null) return null;

            var widget = new NarrativeInventoryWidget();
            widget.BindInventory(inventory);
            container.AddWidget(widget, true);
            return widget;
        }
    }
}
