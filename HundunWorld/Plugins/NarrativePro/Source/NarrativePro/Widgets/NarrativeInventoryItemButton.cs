using System;
using System.Collections.Generic;
using System.Text;
using FlaxEngine;
using FlaxEngine.GUI;
using NarrativePro.CommonUI.Widgets;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Widgets
{
    /// <summary>
    /// 背包格子按钮：一个物品槽位的可视化与交互入口。
    /// 移植自 UE5 NarrativeArsenal: <c>Items/Widgets/NarrativeInventoryItemButton.h</c>
    /// （<c>UNarrativeInventoryItemButton : UNarrativeCommonButtonBase</c>）。
    /// <para>
    /// 模块关系：本类是 <see cref="NarrativeInventoryWidget"/> 网格里的唯一子控件类型，
    /// 视觉状态机（Normal/Hovered/Pressed/Selected/Disabled）全部复用
    /// <see cref="NarrativeCommonButtonBase"/>；数据来自 <see cref="NarrativeItem"/>；
    /// 点击/悬浮不自己决策，一律回调给 <see cref="OwnerWidget"/>（UE5 的 OwnerWidget 反向指针），
    /// 由背包控件决定"选中 / 使用 / 两件物品互用"。
    /// </para>
    /// <para>
    /// UE5 依据（只读参考工程 <c>C:\Works\GitHubProjects\20260801\HundunWorld\Plugins\NarrativePro\Source\NarrativeArsenal</c>）：
    /// <list type="bullet">
    /// <item><description><c>Public/Items/Widgets/NarrativeInventoryItemButton.h:16</c> 类声明继承
    /// <c>UNarrativeCommonButtonBase</c>；<c>:22-24</c> <c>Item</c>（ExposeOnSpawn，本按钮服务的那个物品）、
    /// <c>:26-28</c> <c>OwnerWidget</c>（"The inventory widget that we're a child of"）、
    /// <c>:32-34</c> <c>TextBlock_Quantity</c>、<c>:36-38</c> <c>TextBlock_Weight</c>（均 BindWidgetOptional）。</description></item>
    /// <item><description><c>Private/Items/Widgets/NarrativeInventoryItemButton.cpp:1-6</c> 只有 include，
    /// 没有任何函数：UE5 把"填什么、点了做什么"留在蓝图与基类里，因此本端口按头文件字段 +
    /// 物品数据面把行为补齐（见下）。</description></item>
    /// <item><description>驱动槽位的数据取自 <c>Public/Items/NarrativeItem.h</c>：
    /// <c>:193-195</c> Thumbnail、<c>:201-203</c> DisplayName、<c>:266-268</c> bIsBusy、
    /// <c>:270-272</c> bFavourite、<c>:330-334</c> GetQuantity、<c>:341-343</c> ShowActiveInUI
    /// （"show a tick in the inventory screen"）、<c>:354-355</c> GetStackWeight、
    /// <c>:357-361</c> GetMaxStackSize；<c>:320-323</c> SetBusy 明确"busy 时显示读条图标并禁止使用"。</description></item>
    /// <item><description>提示框取自 <c>Public/Items/ItemTooltip.h:21-23</c>（<c>Item</c> 同为 ExposeOnSpawn），
    /// 由 <see cref="InventoryFunctionLibrary.CreateItemTooltip"/> 生成视图模型，本类渲染成悬浮提示。</description></item>
    /// <item><description><c>Public/Items/NarrativeItem.h:226-228</c> <c>ItemWidgetOverride</c>
    /// 允许每类物品指定自己的按钮类 —— Flax 侧的等价物见
    /// <see cref="NarrativeInventoryWidget.CreateItemButton"/>。</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// 端口差异：
    /// <list type="bullet">
    /// <item><description>UE5 的 <c>BindWidgetOptional</c> 文本块由 WBP 提供，蓝图缺失时字段为空也能运行；
    /// Flax 无 UMG/预制体，这里在构造函数里用真实 <see cref="NarrativeCommonTextBlock"/> /
    /// <see cref="Image"/> 子控件自建同名语义的槽位（Quantity/Weight 字段保留 UE5 命名，
    /// 便于对照），控件在运行期构建，不依赖任何编辑器资产。</description></item>
    /// <item><description>UE5 的 <c>TSoftObjectPtr&lt;UTexture2D&gt;</c> 缩略图在 Flax 侧是
    /// <see cref="NarrativeItem.ThumbnailPath"/> 字符串，按路径 <c>Content.Load</c> 成
    /// <see cref="Texture"/>（1.12 托管层没有 Texture2D 类型）后包成 <see cref="TextureBrush"/>；
    /// 资产缺失只降级为无图标，不影响槽位。</description></item>
    /// <item><description>UE5 的 <c>FText</c> 本地化在 Flax GUI 里就是 <see cref="Control.TooltipText"/>，
    /// 悬浮出现/消失由引擎负责，因此不需要 UE5 的 tooltip widget 创建与销毁代码。</description></item>
    /// <item><description>UE5 busy 态靠样式表切换，这里映射为 <see cref="Control.Enabled"/>=false，
    /// 从而复用基类的 Disabled 相位（<c>!Enabled</c> 即 Disabled，且不再接收点击）。</description></item>
    /// </list>
    /// </para>
    /// </summary>
    public class NarrativeInventoryItemButton : NarrativeCommonButtonBase
    {
        /// <summary>本按钮所服务的物品。对应 UE5 Item（ExposeOnSpawn）。</summary>
        public NarrativeItem Item { get; private set; }

        /// <summary>拥有本按钮的背包控件。对应 UE5 OwnerWidget。</summary>
        public NarrativeInventoryWidget OwnerWidget;

        /// <summary>格子在网格里的序号（键盘导航用）。UE5 由布局隐式决定，Flax 需显式记账。</summary>
        public int SlotIndex = -1;

        /// <summary>数量文本块。对应 UE5 TextBlock_Quantity。</summary>
        public NarrativeCommonTextBlock TextBlock_Quantity;

        /// <summary>重量文本块。对应 UE5 TextBlock_Weight。</summary>
        public NarrativeCommonTextBlock TextBlock_Weight;

        /// <summary>物品名字文本块（UE5 由基类 ButtonTextBlock 承担）。</summary>
        public NarrativeCommonTextBlock TextBlock_Name;

        /// <summary>缩略图。对应 UE5 <c>NarrativeItem.h:193-195</c> 的 Thumbnail。</summary>
        public Image ThumbnailImage;

        /// <summary>busy 遮罩。对应 UE5 <c>NarrativeItem.h:320-323</c> 的读条图标。</summary>
        public Image BusyOverlay;

        /// <summary>激活标记。对应 UE5 <c>NarrativeItem.h:341-343</c> ShowActiveInUI 的勾。</summary>
        public NarrativeCommonTextBlock ActiveMark;

        /// <summary>收藏标记。对应 UE5 <c>NarrativeItem.h:270-272</c> bFavourite。</summary>
        public NarrativeCommonTextBlock FavouriteMark;

        /// <summary>缩略图边长（像素）。</summary>
        public float IconSize = 30f;

        /// <summary>格子内四周留白（像素）。</summary>
        public float SlotPadding = 5f;

        private int _signatureQuantity = -1;
        private bool _signatureBusy;
        private bool _signatureActive;
        private bool _signatureFavourite;
        private string _signatureName = string.Empty;
        private string _signatureThumbnail = string.Empty;
        private bool _bTargetable = true;

        // 缩略图按路径缓存（含"加载失败"的 null 记录，避免每帧重试缺失资产）
        private static readonly Dictionary<string, Texture> _thumbnailCache = new Dictionary<string, Texture>();

        public NarrativeInventoryItemButton()
        {
            // 选中相由背包控件统一下发（对应 UE5 里容器负责 SetSelectedInternal）
            bToggleSelectionOnClick = false;
            AnchorPreset = AnchorPresets.TopLeft;
            Size = new Float2(170f, 52f);

            // 名字/数量/重量都由子文本块绘制，基类自身文本清空避免双份
            SetButtonText(string.Empty);

            ThumbnailImage = new Image
            {
                AnchorPreset = AnchorPresets.TopLeft,
                KeepAspectRatio = true,
                Visible = false,
            };
            AddChild(ThumbnailImage);

            TextBlock_Name = CreateLabel("Small");
            AddChild(TextBlock_Name);

            TextBlock_Quantity = CreateLabel("Small");
            TextBlock_Quantity.SetJustification(ETextJustify.Right);
            AddChild(TextBlock_Quantity);

            TextBlock_Weight = CreateLabel("Small");
            AddChild(TextBlock_Weight);

            ActiveMark = CreateLabel("Small");
            ActiveMark.SetText("√");
            ActiveMark.SetJustification(ETextJustify.Center);
            ActiveMark.TextColor = new Color(0.35f, 1f, 0.45f, 1f);
            ActiveMark.Visible = false;
            AddChild(ActiveMark);

            FavouriteMark = CreateLabel("Small");
            FavouriteMark.SetText("★");
            FavouriteMark.SetJustification(ETextJustify.Center);
            FavouriteMark.TextColor = new Color(1f, 0.85f, 0.30f, 1f);
            FavouriteMark.Visible = false;
            AddChild(FavouriteMark);

            BusyOverlay = new Image
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Brush = new SolidColorBrush(new Color(0f, 0f, 0f, 0.55f)),
                Visible = false,
            };
            AddChild(BusyOverlay);
        }

        /// <summary>
        /// 装入物品并接管该物品的修改通知。对应 UE5 蓝图创建按钮时设置 Item / OwnerWidget（ExposeOnSpawn）。
        /// </summary>
        public virtual void SetItem(NarrativeItem item, NarrativeInventoryWidget ownerWidget, int slotIndex)
        {
            if (Item != null && !ReferenceEquals(Item, item))
            {
                Item.OnItemModified -= HandleItemModified;
            }

            Item = item;
            OwnerWidget = ownerWidget;
            SlotIndex = slotIndex;

            if (Item != null)
            {
                Item.OnItemModified += HandleItemModified;
            }

            _signatureQuantity = -1;
            RefreshFromItem();
        }

        /// <summary>取物品数据刷新显示。数量/重量对应 UE5 TextBlock_Quantity / TextBlock_Weight 的数据源。</summary>
        public void RefreshFromItem()
        {
            if (Item == null)
            {
                SetButtonText(string.Empty);
                TextBlock_Name.SetText(string.Empty);
                TextBlock_Quantity.SetText(string.Empty);
                TextBlock_Weight.SetText(string.Empty);
                TooltipText = string.Empty;
                ThumbnailImage.Brush = null;
                ThumbnailImage.Visible = false;
                BusyOverlay.Visible = false;
                ActiveMark.Visible = false;
                FavouriteMark.Visible = false;
                Enabled = true;
                return;
            }

            int quantity = Item.GetQuantity();
            bool busy = Item.bIsBusy;
            bool active = Item.ShowActiveInUI() && Item.IsActive();
            bool favourite = Item.bFavourite;
            string name = Item.DisplayName ?? string.Empty;
            string thumbnailPath = Item.ThumbnailPath ?? string.Empty;

            bool changed = _signatureQuantity != quantity || _signatureBusy != busy ||
                           _signatureActive != active || _signatureFavourite != favourite ||
                           _signatureName != name || _signatureThumbnail != thumbnailPath;
            if (!changed) return;

            _signatureQuantity = quantity;
            _signatureBusy = busy;
            _signatureActive = active;
            _signatureFavourite = favourite;
            _signatureName = name;
            _signatureThumbnail = thumbnailPath;

            TextBlock_Name.SetText(name);

            // 只在"可能是堆叠"时显示数量（对应 UE5 NarrativeItem.h:357-361 的最大堆叠语义）
            int maxStack = Item.GetMaxStackSize();
            TextBlock_Quantity.SetText(maxStack > 1 || quantity > 1 ? quantity.ToString() : string.Empty);
            TextBlock_Weight.SetText($"{Item.GetStackWeight():F1}");

            ApplyThumbnail(thumbnailPath);
            TooltipText = BuildTooltipText(Item);

            BusyOverlay.Visible = busy;
            ActiveMark.Visible = active;
            FavouriteMark.Visible = favourite;

            // busy 即禁用（UE5 SetBusy 文档："show a loading icon and prevent the item from being used"）
            ApplyEnabled();
            PerformSlotLayout();
        }

        /// <summary>背包控件下发选中相。对应 UE5 容器调用 ForceSetIsSelected。</summary>
        public void SetSlotSelected(bool bSelected)
        {
            ForceSetIsSelected(bSelected, false, false);

            var textColor = bSelected ? new Color(0f, 0f, 0f, 1f) : Color.White;
            if (TextBlock_Name != null) TextBlock_Name.TextColor = textColor;
            if (TextBlock_Quantity != null) TextBlock_Quantity.TextColor = textColor;
            if (TextBlock_Weight != null) TextBlock_Weight.TextColor = textColor;
        }

        /// <summary>
        /// 两件物品互用的可选目标标记。对应 UE5 <c>NarrativeItem.h:239-241</c>：
        /// 提示里只列出通过 <c>CanUseItemWith()</c> 的物品。
        /// </summary>
        public void SetTargetable(bool bTargetable)
        {
            _bTargetable = bTargetable;
            ApplyEnabled();
        }

        /// <summary>格子内容按当前尺寸重排。一律 TopLeft 锚 + 显式 Location（仓库既有约定）。</summary>
        public void PerformSlotLayout()
        {
            float w = Size.X;
            float h = Size.Y;
            float p = SlotPadding;
            float icon = Math.Min(IconSize, h - p * 2f);
            float textX = p + icon + 6f;
            float textWidth = Math.Max(20f, w - textX - p);

            if (ThumbnailImage != null)
            {
                ThumbnailImage.Location = new Float2(p, (h - icon) * 0.5f);
                ThumbnailImage.Size = new Float2(icon, icon);
            }

            if (TextBlock_Name != null)
            {
                TextBlock_Name.Location = new Float2(textX, p + 1f);
                TextBlock_Name.Size = new Float2(textWidth, h * 0.5f);
            }

            if (TextBlock_Weight != null)
            {
                TextBlock_Weight.Location = new Float2(textX, h - p - 15f);
                TextBlock_Weight.Size = new Float2(textWidth * 0.5f, 15f);
            }

            if (TextBlock_Quantity != null)
            {
                TextBlock_Quantity.Location = new Float2(textX + textWidth * 0.5f, h - p - 15f);
                TextBlock_Quantity.Size = new Float2(textWidth * 0.5f, 15f);
            }

            if (ActiveMark != null)
            {
                ActiveMark.Location = new Float2(w - p - 16f, p - 1f);
                ActiveMark.Size = new Float2(16f, 16f);
            }

            if (FavouriteMark != null)
            {
                FavouriteMark.Location = new Float2(w - p - 32f, p - 1f);
                FavouriteMark.Size = new Float2(16f, 16f);
            }

            if (BusyOverlay != null)
            {
                BusyOverlay.Location = Float2.Zero;
                BusyOverlay.Size = Size;
            }
        }

        /// <summary>加入父控件后按父给的尺寸排一次内容（构造期父尺寸尚不可用）。</summary>
        protected override void OnParentChangedInternal()
        {
            base.OnParentChangedInternal();
            PerformSlotLayout();
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            PerformSlotLayout();
        }

        /// <summary>悬浮：把"当前悬浮物品"回报背包控件。UE5 由蓝图 OnHovered 事件承担。</summary>
        public override void OnMouseEnter(Float2 location)
        {
            base.OnMouseEnter(location);
            OwnerWidget?.HandleSlotHovered(this);
        }

        public override void OnMouseLeave()
        {
            base.OnMouseLeave();
            OwnerWidget?.HandleSlotUnhovered(this);
        }

        /// <summary>
        /// 右键 = 把本物品作为"另一件物品"提交（对应 UE5 <c>NarrativeItem.h:389-391</c>
        /// 中 <c>Use(OtherItem)</c> 的拖拽目标语义，Flax 无 UMG 拖拽故用右键替代，见端口差异）。
        /// </summary>
        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if ((button & MouseButton.Right) == MouseButton.Right && Item != null)
            {
                if (OwnerWidget != null && OwnerWidget.HandleSlotSecondaryClicked(this))
                {
                    return true;
                }
            }

            return base.OnMouseDown(location, button);
        }

        /// <summary>轮询物品状态（数量/激活/busy 可能被外部直接改动），保证格子显示与数据一致。</summary>
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            RefreshFromItem();
        }

        /// <summary>销毁时断开物品事件，避免残留回调。</summary>
        public override void OnDestroy()
        {
            base.OnDestroy();

            if (Item != null)
            {
                Item.OnItemModified -= HandleItemModified;
                Item = null;
            }
        }

        private void HandleItemModified(NarrativeItem item)
        {
            if (!ReferenceEquals(item, Item)) return;
            RefreshFromItem();
        }

        private void ApplyEnabled()
        {
            bool bBusy = Item != null && Item.bIsBusy;
            Enabled = _bTargetable && !bBusy;
        }

        private void ApplyThumbnail(string thumbnailPath)
        {
            var texture = ResolveThumbnail(thumbnailPath);
            if (texture == null)
            {
                ThumbnailImage.Brush = null;
                ThumbnailImage.Visible = false;
                return;
            }

            ThumbnailImage.Brush = new TextureBrush(texture);
            ThumbnailImage.Visible = true;
        }

        /// <summary>
        /// 拼提示框文本。数据面复用 <see cref="InventoryFunctionLibrary.CreateItemTooltip"/>
        /// （对应 UE5 ItemTooltip 的 SetItem + GetParsedDescription）。
        /// </summary>
        private static string BuildTooltipText(NarrativeItem item)
        {
            var tooltip = InventoryFunctionLibrary.CreateItemTooltip(item);
            if (tooltip == null || !tooltip.bIsValid) return item.DisplayName ?? string.Empty;

            var sb = new StringBuilder();
            sb.Append(tooltip.Title);
            if (!string.IsNullOrEmpty(tooltip.Description)) sb.Append('\n').Append(tooltip.Description);
            if (!string.IsNullOrEmpty(tooltip.StatLines)) sb.Append('\n').Append(tooltip.StatLines);
            sb.Append('\n').Append("重量 ").Append(tooltip.WeightText)
              .Append("  数量 ").Append(tooltip.QuantityText);

            var actions = item.GetItemUseActions();
            if (actions != null)
            {
                for (int i = 0; i < actions.Count; i++)
                {
                    var action = actions[i];
                    if (action == null || !action.IsEnabled()) continue;
                    sb.Append('\n').Append("使用：").Append(action.GetActionDisplayName(item));
                    break;
                }
            }

            if (item.bIsBusy) sb.Append('\n').Append("加载中，暂不可用");
            else if (!item.CanUse()) sb.Append('\n').Append("当前无法使用");

            return sb.ToString();
        }

        private static NarrativeCommonTextBlock CreateLabel(string style)
        {
            var label = new NarrativeCommonTextBlock
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Style = style,
                Wrapping = TextWrapping.NoWrap,
                VerticalAlignment = TextAlignment.Center,
                bFollowThemeColor = false,
                TextColor = Color.White,
            };
            label.SetStyle(style);
            return label;
        }

        private static Texture ResolveThumbnail(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            if (_thumbnailCache.TryGetValue(path, out var cached)) return cached;

            Texture texture = null;
            try
            {
                texture = FlaxEngine.Content.Load<Texture>(path);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning("NarrativeInventoryItemButton 加载缩略图失败（" + path + "）：" + ex.Message);
            }

            _thumbnailCache[path] = texture;
            return texture;
        }
    }
}
