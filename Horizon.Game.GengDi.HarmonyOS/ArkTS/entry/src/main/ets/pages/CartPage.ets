import { AppTheme } from "../theme/AppTheme";
import { CartViewModel, CartItem, OrderItem, cartItemToOrderItem } from "../viewmodels/OrderViewModels";
import { EmptyView } from "../components/CommonWidgets";
import { AppContext } from "../model/AppContext";

/** 购物车页面 */
@Entry
@Component
export struct CartPage {
  @State cartVm: CartViewModel = new CartViewModel();
  @State editing: boolean = false;

  aboutToAppear(): void {
    this.cartVm.loadCart();
  }

  private toggleSelectAll(): void {
    this.cartVm.toggleSelectAll();
  }

  private toggleItemSelect(cartId: string): void {
    const item = this.cartVm.items.find((c: CartItem) => c.cartId === cartId);
    if (item) {
      item.selected = !item.selected;
    }
  }

  private updateQuantity(cartId: string, delta: number): void {
    const item = this.cartVm.items.find((c: CartItem) => c.cartId === cartId);
    if (item) {
      const newQty = item.quantity + delta;
      if (newQty >= 1 && newQty <= 99) {
        this.cartVm.updateQuantity(cartId, newQty);
      }
      else if (newQty <= 0) {
        this.cartVm.removeItem(cartId);
      }
    }
  }

  private removeItem(cartId: string): void {
    this.cartVm.removeItem(cartId);
  }

  private async goToCheckout(): Promise<void> {
    try {
      const selectedItems = this.cartVm.getSelectedItems();
      if (selectedItems.length === 0) {
        this.toast('请选择商品');
        return;
      }

      // 转换为订单项
      const orderItems: OrderItem[] = [];
      for (const ci of selectedItems) {
        const oi = cartItemToOrderItem(ci);
        if (oi && oi.unitPrice > 0 && oi.quantity > 0) {
          orderItems.push(oi);
        }
      }
      if (orderItems.length === 0) {
        this.toast('商品数据异常，请重试');
        return;
      }

      // 使用 AppContext 传递订单数据，避免路由参数编码问题
      const ctx = AppContext.getInstance();
      // 创建新OrderItem实例，避免引用和spread问题
      const safeItems: OrderItem[] = [];
      for (const o of orderItems) {
        const oi = new OrderItem();
        oi.speciesId = o.speciesId;
        oi.name = o.name;
        oi.emoji = o.emoji;
        oi.unitPrice = o.unitPrice;
        oi.quantity = o.quantity;
        oi.subtotal = o.subtotal;
        safeItems.push(oi);
      }
      ctx.pendingOrderItems = safeItems;
      ctx.pendingOrderFromCart = true;

      // 同步获取 router（异步回调中 getUIContext 可能失效）
      const router = this.getUIContext().getRouter();

      // 清空已选购物车项
      this.cartVm.clearSelected();

      // 延迟跳转，确保 UI 状态先更新
      setTimeout(() => {
        try {
          router.pushUrl({
            url: 'pages/OrderConfirmPage'
          });
        } catch (e) {
          console.error(`[CartPage] 跳转订单确认页失败: ${JSON.stringify(e)}`);
        }
      }, 350);
    } catch (e) {
      console.error(`[CartPage] goToCheckout 异常: ${JSON.stringify(e)}`);
      this.toast('结算失败，请重试');
    }
  }

  private toast(message: string): void {
    this.getUIContext().getPromptAction().showToast({ message: message, duration: 2000 });
  }

  // ===== UI 展示辅助方法（禁止在 UI DSL 内写 .toFixed 等非 UI 语法）=====
  /** 购物车项表情（空值保护） */
  private cartItemEmoji(item: CartItem): string {
    return item && typeof item.emoji === 'string' && item.emoji.length > 0 ? item.emoji : '🌸';
  }

  /** 购物车项名称（空值保护） */
  private cartItemName(item: CartItem): string {
    return item && typeof item.name === 'string' && item.name.length > 0 ? item.name : '花卉商品';
  }

  /** 购物车项单价文本 */
  private cartItemUnitPriceText(item: CartItem): string {
    const price = item && typeof item.unitPrice === 'number' ? item.unitPrice : 0;
    return `¥${price.toFixed(2)}/扎`;
  }

  /** 购物车项数量文本（空值保护） */
  private cartItemQtyText(item: CartItem): string {
    const qty = item && typeof item.quantity === 'number' ? item.quantity : 0;
    return `${qty}`;
  }

  /** 购物车项小计文本 */
  private cartItemSubtotalText(item: CartItem): string {
    const price = item && typeof item.unitPrice === 'number' ? item.unitPrice : 0;
    const qty = item && typeof item.quantity === 'number' ? item.quantity : 0;
    return `¥${(price * qty).toFixed(2)}`;
  }

  /** 已选件数文本 */
  private selectedCountText(): string {
    return `已选 ${this.cartVm.getSelectedItems().length} 件`;
  }

  /** 合计金额文本 */
  private selectedTotalText(): string {
    return `合计：¥${this.cartVm.getSelectedTotal().toFixed(2)}`;
  }

  /** 去结算按钮是否可用 */
  private checkoutEnabled(): boolean {
    return this.cartVm.getSelectedItems().length > 0;
  }

  /** 全选是否已勾选 */
  private isAllSelected(): boolean {
    return this.cartVm.items.length > 0 && this.cartVm.items.every((c: CartItem) => c.selected);
  }

  /** 购物车项是否已选 */
  private isItemSelected(item: CartItem): boolean {
    return item ? !!item.selected : false;
  }

  build() {
    Column() {
      // 标题栏
      Row() {
        Text('购物车')
          .fontSize(18)
          .fontWeight(FontWeight.Bold)
          .fontColor(AppTheme.text)
        Blank()
        Text(this.editing ? '完成' : '管理')
          .fontSize(15)
          .fontColor(AppTheme.primary)
          .onClick(() => { this.editing = !this.editing; })
      }
      .width('100%')
      .height(56)
      .padding({ left: 16, right: 16 })
      .backgroundColor(AppTheme.card)
      .alignItems(VerticalAlign.Center)

      if (this.cartVm.items.length === 0) {
        Column() {
          EmptyView({ message: '购物车还是空的，快去选购吧' })
        }
        .layoutWeight(1)
        .width('100%')
        .justifyContent(FlexAlign.Center)
      }
      else {
        Column() {
          // 全选栏
          Row({ space: 12 }) {
            Column() {
              Circle({ width: 22, height: 22 })
                .fill(this.isAllSelected() ? AppTheme.primary : Color.Transparent)
                .stroke(this.isAllSelected() ? AppTheme.primary : AppTheme.divider)
              if (this.isAllSelected()) {
                Text('✓')
                  .fontSize(14)
                  .fontColor(Color.White)
              }
            }
            .alignItems(HorizontalAlign.Center)
            .justifyContent(FlexAlign.Center)
            .onClick(() => this.toggleSelectAll())

            Text('全选')
              .fontSize(14)
              .fontColor(AppTheme.text)
          }
          .width('100%')
          .padding({ left: 16, right: 16, top: 12, bottom: 12 })
          .backgroundColor(AppTheme.card)
          .margin({ bottom: 4 })

          Scroll() {
            Column({ space: 8 }) {
              ForEach(this.cartVm.items, (item: CartItem) => {
                Row({ space: 12 }) {
                  // 选择框
                  Column() {
                    Circle({ width: 22, height: 22 })
                      .fill(this.isItemSelected(item) ? AppTheme.primary : Color.Transparent)
                      .stroke(this.isItemSelected(item) ? AppTheme.primary : AppTheme.divider)
                    if (this.isItemSelected(item)) {
                      Text('✓')
                        .fontSize(14)
                        .fontColor(Color.White)
                    }
                  }
                  .alignItems(HorizontalAlign.Center)
                  .justifyContent(FlexAlign.Center)
                  .onClick(() => this.toggleItemSelect(item.cartId))

                  // 商品图标
                  Column() {
                    Text(this.cartItemEmoji(item))
                      .fontSize(28)
                  }
                  .width(56)
                  .height(56)
                  .borderRadius(12)
                  .backgroundColor(AppTheme.sheet)
                  .alignItems(HorizontalAlign.Center)
                  .justifyContent(FlexAlign.Center)

                  // 商品信息
                  Column({ space: 6 }) {
                    Text(this.cartItemName(item))
                      .fontSize(14)
                      .fontWeight(FontWeight.Medium)
                      .fontColor(AppTheme.text)
                      .maxLines(1)
                      .textOverflow({ overflow: TextOverflow.Ellipsis })

                    Text(this.cartItemUnitPriceText(item))
                      .fontSize(13)
                      .fontColor(AppTheme.textSecondary)
                  }
                  .layoutWeight(1)
                  .alignItems(HorizontalAlign.Start)

                  // 数量控制
                  if (this.editing) {
                    Text('删除')
                      .fontSize(14)
                      .fontColor(AppTheme.down)
                      .onClick(() => this.removeItem(item.cartId))
                  }
                  else {
                    Column({ space: 8 }) {
                      Row({ space: 0 }) {
                        Text('−')
                          .fontSize(16)
                          .fontColor(AppTheme.text)
                          .width(32)
                          .height(32)
                          .textAlign(TextAlign.Center)
                          .backgroundColor(AppTheme.sheet)
                          .borderRadius({ topLeft: 8, bottomLeft: 8 })
                          .onClick(() => this.updateQuantity(item.cartId, -1))
                        Text(this.cartItemQtyText(item))
                          .fontSize(14)
                          .fontWeight(FontWeight.Medium)
                          .fontColor(AppTheme.text)
                          .width(40)
                          .height(32)
                          .textAlign(TextAlign.Center)
                          .backgroundColor(AppTheme.card)
                          .border({ width: { top: 1, bottom: 1 }, color: AppTheme.divider })
                        Text('+')
                          .fontSize(16)
                          .fontColor(AppTheme.text)
                          .width(32)
                          .height(32)
                          .textAlign(TextAlign.Center)
                          .backgroundColor(AppTheme.sheet)
                          .borderRadius({ topRight: 8, bottomRight: 8 })
                          .onClick(() => this.updateQuantity(item.cartId, 1))
                      }
                      .height(32)

                      Text(this.cartItemSubtotalText(item))
                        .fontSize(14)
                        .fontWeight(FontWeight.Bold)
                        .fontColor(AppTheme.text)
                        .fontFamily(AppTheme.fontMono)
                    }
                  }
                }
                .width('100%')
                .padding(12)
                .backgroundColor(AppTheme.card)
                .borderRadius(12)
              }, (item: CartItem) => item.cartId)
            }
            .padding({ left: 16, right: 16, top: 4, bottom: 100 })
          }
          .layoutWeight(1)
          .width('100%')
          .scrollBar(BarState.Off)

          // 底部结算栏
          Row() {
            Column() {
              Text(this.selectedCountText())
                .fontSize(12)
                .fontColor(AppTheme.textSecondary)
              Text(this.selectedTotalText())
                .fontSize(18)
                .fontWeight(FontWeight.Bold)
                .fontColor(AppTheme.up)
                .fontFamily(AppTheme.fontMono)
            }
            .alignItems(HorizontalAlign.Center)

            Blank()

            Button('去结算')
              .type(ButtonType.Normal)
              .width(120)
              .height(44)
              .borderRadius(999)
              .backgroundColor(this.cartVm.getSelectedItems().length > 0 ? AppTheme.primary : AppTheme.textSecondary)
              .fontSize(15)
              .fontWeight(FontWeight.Medium)
              .onClick(() => this.goToCheckout())
          }
          .width('100%')
          .height(64)
          .padding({ left: 16, right: 16 })
          .backgroundColor(AppTheme.card)
          .border({ width: { top: 1 }, color: AppTheme.divider })
        }
      }
    }
    .width('100%')
    .height('100%')
    .backgroundColor(AppTheme.bg)
  }
}