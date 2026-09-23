import { AppTheme } from "../theme/AppTheme";
import { AddressViewModel, OrderViewModel, ShippingAddress, PaymentMethod, OrderItem, FlowerOrder } from "../viewmodels/OrderViewModels";
import { CartItem, cartItemToOrderItem } from "../viewmodels/OrderViewModels";
import { AppContext } from "../model/AppContext";

/** 订单确认页面 */
@Entry
@Component
export struct OrderConfirmPage {
  @State addressVm: AddressViewModel = new AddressViewModel();
  @State orderVm: OrderViewModel = new OrderViewModel();
  @State selectedAddress: ShippingAddress | null = null;
  @State paymentMethod: PaymentMethod = PaymentMethod.WECHAT;
  @State remark: string = '';
  @State orderItems: OrderItem[] = [];
  @State fromCart: boolean = false;
  @State showAddressSheet: boolean = false;
  /** 订单提交服务端中（防重复点击） */
  @State submitting: boolean = false;

  /** 从 AppContext 或路由参数解析订单数据 */
  aboutToAppear(): void {
    try {
      // 优先从 AppContext 读取（避免路由参数编码问题）
      const ctx = AppContext.getInstance();
      if (ctx.pendingOrderItems && ctx.pendingOrderItems.length > 0) {
        // 创建副本，确保数据安全
        this.orderItems = ctx.pendingOrderItems.map((o) => {
          const oi = new OrderItem();
          oi.speciesId = typeof o.speciesId === 'number' ? o.speciesId : 0;
          oi.name = typeof o.name === 'string' ? o.name : '商品';
          oi.emoji = typeof o.emoji === 'string' ? o.emoji : '🌸';
          oi.unitPrice = typeof o.unitPrice === 'number' ? o.unitPrice : 0;
          oi.quantity = typeof o.quantity === 'number' ? o.quantity : 0;
          oi.subtotal = typeof o.subtotal === 'number' ? o.subtotal : (oi.unitPrice * oi.quantity);
          return oi;
        }).filter((o) => o.speciesId > 0 && o.quantity > 0);
        this.fromCart = !!ctx.pendingOrderFromCart;
        // 清空 AppContext 中的待下单数据
        ctx.pendingOrderItems = [];
        ctx.pendingOrderFromCart = false;
      }
      else {
        // 备用：从路由参数读取
        try {
          const params = this.getUIContext().getRouter().getParams() as Record<string, string>;
          if (params) {
            const orderItemsStr = params['orderItems'];
            if (orderItemsStr && typeof orderItemsStr === 'string') {
              const arr = JSON.parse(orderItemsStr) as Array<Record<string, Object>>;
              const items: OrderItem[] = [];
              if (Array.isArray(arr)) {
                for (const it of arr) {
                  const oi = new OrderItem();
                  oi.speciesId = Number(it['speciesId'] ?? 0);
                  oi.name = String(it['name'] ?? '商品');
                  oi.emoji = String(it['emoji'] ?? '🌸');
                  oi.unitPrice = Number(it['unitPrice'] ?? 0);
                  oi.quantity = Number(it['quantity'] ?? 0);
                  oi.subtotal = Number(it['subtotal'] ?? (oi.unitPrice * oi.quantity));
                  if (oi.speciesId > 0 && oi.quantity > 0) {
                    items.push(oi);
                  }
                }
              }
              this.orderItems = items;
            }
            const fromCartStr = params['fromCart'];
            if (fromCartStr === 'true') {
              this.fromCart = true;
            }
          }
        }
        catch (_e) {
          // 忽略参数解析错误
        }
      }
    } catch (e) {
      // 读取失败时保持空数组
      this.orderItems = [];
    }

    // 加载地址，确保错误不影响主流程
    this.addressVm.loadAddresses().then(() => {
      try {
        const defaultAddr = this.addressVm.getDefaultAddress();
        if (defaultAddr) {
          this.selectedAddress = defaultAddr;
        }
      } catch (_e) {
        // 忽略地址加载错误
      }
    }).catch(() => {
      // 忽略Promise rejection
    });
  }

  private async createAndPay(): Promise<void> {
    try {
      if (!this.selectedAddress) {
        this.toast('请选择收货地址');
        return;
      }
      if (this.orderItems.length === 0) {
        this.toast('没有可下单的商品');
        return;
      }

      const order = this.orderVm.createOrder(
        this.orderItems,
        this.selectedAddress,
        this.paymentMethod,
        this.remark.trim()
      );
      if (!order) {
        this.toast('创建订单失败，请重试');
        return;
      }

      await this.orderVm.saveOrder(order);

      // 订单同步服务端（正式支付前置：金额以服务端核算为准）
      this.submitting = true;
      const synced = await this.orderVm.submitToServerAsync(order);
      this.submitting = false;
      if (!synced) {
        this.toast('订单提交服务端失败：请检查网络或登录状态后重试');
        return;
      }

      // 同步获取 router（异步回调中 getUIContext 可能失效）
      const router = this.getUIContext().getRouter();

      // 跳转到支付页面（金额以服务端核算结果为准）
      const params: Record<string, string> = {} as Record<string, string>;
      params['orderId'] = String(order.orderId ?? '');
      params['orderNo'] = String(order.serverOrderNo.length > 0 ? order.serverOrderNo : order.orderNo);
      params['payAmount'] = String(order.serverPayAmount > 0 ? order.serverPayAmount : order.payAmount);
      params['paymentMethod'] = String(this.paymentMethod ?? PaymentMethod.WECHAT);
      setTimeout(() => {
        try {
          router.pushUrl({
            url: 'pages/OrderPayPage',
            params: params
          });
        } catch (e) {
          console.error(`[OrderConfirmPage] 跳转支付页失败: ${JSON.stringify(e)}`);
        }
      }, 350);
    } catch (e) {
      this.submitting = false;
      console.error(`[OrderConfirmPage] createAndPay 异常: ${JSON.stringify(e)}`);
      this.toast('提交订单失败，请重试');
    }
  }

  private selectAddressFromList(): void {
    // 打开地址下拉面板前先刷新地址列表
    this.addressVm.loadAddresses().then(() => {
      this.showAddressSheet = true;
    }).catch(() => {
      this.showAddressSheet = true;
    });
  }

  private onAddressPicked(addr: ShippingAddress): void {
    this.selectedAddress = addr;
    this.showAddressSheet = false;
  }

  /** 跳转地址管理页（mode=manage） */
  private goAddressManage(): void {
    this.showAddressSheet = false;
    setTimeout(() => {
      try {
        this.getUIContext().getRouter().pushUrl({
          url: 'pages/AddressManagePage',
          params: { mode: 'manage' }
        });
      } catch (e) {
        console.error(`[OrderConfirmPage] 跳转地址管理失败: ${JSON.stringify(e)}`);
      }
    }, 100);
  }

  /** 地址管理入口 onAppear 后刷新（返回本页时刷新已选择地址） */
  private refreshSelectedAddress(): void {
    this.addressVm.loadAddresses().then(() => {
      const addr = this.addressVm.addresses.find((a: ShippingAddress) =>
        this.selectedAddress && a.addressId === this.selectedAddress.addressId);
      if (addr) {
        this.selectedAddress = addr;
      }
    }).catch(() => {});
  }

  private toast(message: string): void {
    this.getUIContext().getPromptAction().showToast({ message: message, duration: 2000 });
  }

  /** 计算商品总额（禁止用 getter：ArkUI 状态代理在 initialRender 中不包装 get 访问器，会返回 undefined 导致崩溃）*/
  private getGoodsTotal(): number {
    try {
      if (!this.orderItems || this.orderItems.length === 0) {
        return 0;
      }
      let total = 0;
      for (const item of this.orderItems) {
        if (item && typeof item.subtotal === 'number') {
          total += item.subtotal;
        }
      }
      return Math.round(total * 100) / 100;
    } catch (e) {
      return 0;
    }
  }

  /** 计算运费 */
  private getShippingFee(): number {
    return this.getGoodsTotal() >= 100 ? 0 : 8;
  }

  /** 计算应付金额 */
  private getPayAmount(): number {
    return Math.round((this.getGoodsTotal() + this.getShippingFee()) * 100) / 100;
  }

  // ===== UI 展示辅助方法（禁止在 UI DSL 内写 .toFixed 等非 UI 语法）=====
  /** 商品总额格式化文本 */
  private goodsTotalText(): string {
    return `¥${this.getGoodsTotal().toFixed(2)}`;
  }

  /** 运费格式化文本 */
  private shippingFeeText(): string {
    const fee = this.getShippingFee();
    return fee === 0 ? '免运费' : `¥${fee.toFixed(2)}`;
  }

  /** 应付金额格式化文本 */
  private payAmountText(): string {
    return `¥${this.getPayAmount().toFixed(2)}`;
  }

  /** 订单项单价文本 */
  private orderItemPriceText(item: OrderItem): string {
    const price = item && typeof item.unitPrice === 'number' ? item.unitPrice : 0;
    const qty = item && typeof item.quantity === 'number' ? item.quantity : 0;
    return `¥${price.toFixed(2)} × ${qty}扎`;
  }

  /** 订单项小计文本 */
  private orderItemSubtotalText(item: OrderItem): string {
    const sub = item && typeof item.subtotal === 'number' ? item.subtotal : 0;
    return `¥${sub.toFixed(2)}`;
  }

  /** 订单项名称（含空值保护） */
  private orderItemName(item: OrderItem): string {
    return item && typeof item.name === 'string' && item.name.length > 0 ? item.name : '花卉商品';
  }

  /** 订单项表情（含空值保护） */
  private orderItemEmoji(item: OrderItem): string {
    return item && typeof item.emoji === 'string' && item.emoji.length > 0 ? item.emoji : '🌸';
  }

  /** 商品件数文本 */
  private itemsCountText(): string {
    return `共${this.orderItems.length}件商品`;
  }

  /** 地址收件人（含空值保护） */
  private addressReceiver(): string {
    return this.selectedAddress && this.selectedAddress.receiverName ? this.selectedAddress.receiverName : '';
  }

  /** 地址电话（含空值保护） */
  private addressPhone(): string {
    return this.selectedAddress && this.selectedAddress.phone ? this.selectedAddress.phone : '';
  }

  /** 完整地址（含空值保护） */
  private addressFull(): string {
    return this.selectedAddress && this.selectedAddress.fullAddress ? this.selectedAddress.fullAddress : '';
  }

  /** 是否默认地址（含空值保护） */
  private isAddressDefault(): boolean {
    return this.selectedAddress ? !!this.selectedAddress.isDefault : false;
  }

  build() {
    Stack({ alignContent: Alignment.Bottom }) {
    Column() {
      // 标题栏
      Row() {
        Text('确认订单')
          .fontSize(18)
          .fontWeight(FontWeight.Bold)
          .fontColor(AppTheme.text)
      }
      .width('100%')
      .height(56)
      .padding({ left: 16, right: 16 })
      .backgroundColor(AppTheme.card)
      .alignItems(VerticalAlign.Center)

      Scroll() {
        Column({ space: 12 }) {
          // 收货地址卡片
          Column() {
            Row({ space: 12 }) {
              Text('📍')
                .fontSize(18)
              Column({ space: 4 }) {
                if (this.selectedAddress) {
                  Row({ space: 8 }) {
                    Text(this.addressReceiver())
                      .fontSize(15)
                      .fontWeight(FontWeight.Medium)
                      .fontColor(AppTheme.text)
                    Text(this.addressPhone())
                      .fontSize(13)
                      .fontColor(AppTheme.textSecondary)
                  }
                  Text(this.addressFull())
                    .fontSize(14)
                    .fontColor(AppTheme.text)
                    .maxLines(2)
                    .textOverflow({ overflow: TextOverflow.Ellipsis })
                  if (this.isAddressDefault()) {
                    Text('默认')
                      .fontSize(10)
                      .fontColor(AppTheme.primary)
                  }
                }
                else {
                  Text('请选择收货地址')
                    .fontSize(14)
                    .fontColor(AppTheme.textSecondary)
                }
              }
              .layoutWeight(1)
              .alignItems(HorizontalAlign.Start)
              Text('›')
                .fontSize(18)
                .fontColor(AppTheme.textSecondary)
            }
            .width('100%')
          }
          .width('100%')
          .padding(16)
          .backgroundColor(AppTheme.card)
          .borderRadius(16)
          .border({ width: 1, color: AppTheme.divider })
          .onClick(() => this.selectAddressFromList())

          // 商品列表
          Column() {
            Row() {
              Text('商品清单')
                .fontSize(14)
                .fontWeight(FontWeight.Medium)
                .fontColor(AppTheme.text)
              Text(this.itemsCountText())
                .fontSize(12)
                .fontColor(AppTheme.textSecondary)
            }
            .width('100%')
            .margin({ bottom: 12 })

            ForEach(this.orderItems, (item: OrderItem) => {
              Row({ space: 12 }) {
                Column() {
                  Text(this.orderItemEmoji(item))
                    .fontSize(24)
                }
                .width(48)
                .height(48)
                .borderRadius(12)
                .backgroundColor(AppTheme.sheet)
                .alignItems(HorizontalAlign.Center)
                .justifyContent(FlexAlign.Center)

                Column({ space: 4 }) {
                  Text(this.orderItemName(item))
                    .fontSize(14)
                    .fontWeight(FontWeight.Medium)
                    .fontColor(AppTheme.text)
                    .maxLines(1)
                    .textOverflow({ overflow: TextOverflow.Ellipsis })
                  Text(this.orderItemPriceText(item))
                    .fontSize(12)
                    .fontColor(AppTheme.textSecondary)
                }
                .layoutWeight(1)
                .alignItems(HorizontalAlign.Start)

                Text(this.orderItemSubtotalText(item))
                  .fontSize(14)
                  .fontWeight(FontWeight.Bold)
                  .fontColor(AppTheme.text)
                  .fontFamily(AppTheme.fontMono)
              }
              .width('100%')
              .padding(12)
              .backgroundColor(AppTheme.sheet)
              .borderRadius(12)
            }, (item: OrderItem) => `${item.speciesId}-${item.quantity}`)
          }
          .width('100%')
          .padding(16)
          .backgroundColor(AppTheme.card)
          .borderRadius(16)
          .border({ width: 1, color: AppTheme.divider })

          // 支付方式
          Column() {
            Text('支付方式')
              .fontSize(14)
              .fontWeight(FontWeight.Medium)
              .fontColor(AppTheme.text)
              .margin({ bottom: 12 })

            Row({ space: 12 }) {
              // 微信支付
              Row({ space: 10 }) {
                Text('💚')
                  .fontSize(20)
                Column({ space: 2 }) {
                  Text('微信支付')
                    .fontSize(14)
                    .fontColor(AppTheme.text)
                  Text('推荐使用')
                    .fontSize(11)
                    .fontColor(AppTheme.textSecondary)
                }
                .alignItems(HorizontalAlign.Center)
              }
              .layoutWeight(1)
              .padding(12)
              .borderRadius(12)
              .backgroundColor(this.paymentMethod === PaymentMethod.WECHAT ? 'rgba(38,181,160,0.15)' : AppTheme.sheet)
              .border({ width: this.paymentMethod === PaymentMethod.WECHAT ? 2 : 1, color: this.paymentMethod === PaymentMethod.WECHAT ? AppTheme.primary : AppTheme.divider })
              .onClick(() => { this.paymentMethod = PaymentMethod.WECHAT; })

              // 支付宝
              Row({ space: 10 }) {
                Text('💙')
                  .fontSize(20)
                Column({ space: 2 }) {
                  Text('支付宝')
                    .fontSize(14)
                    .fontColor(AppTheme.text)
                  Text('快捷支付')
                    .fontSize(11)
                    .fontColor(AppTheme.textSecondary)
                }
                .alignItems(HorizontalAlign.Center)
              }
              .layoutWeight(1)
              .padding(12)
              .borderRadius(12)
              .backgroundColor(this.paymentMethod === PaymentMethod.ALIPAY ? 'rgba(59,130,246,0.15)' : AppTheme.sheet)
              .border({ width: this.paymentMethod === PaymentMethod.ALIPAY ? 2 : 1, color: this.paymentMethod === PaymentMethod.ALIPAY ? AppTheme.primary : AppTheme.divider })
              .onClick(() => { this.paymentMethod = PaymentMethod.ALIPAY; })
            }
          }
          .width('100%')
          .padding(16)
          .backgroundColor(AppTheme.card)
          .borderRadius(16)
          .border({ width: 1, color: AppTheme.divider })

          // 备注
          Column({ space: 8 }) {
            Text('订单备注')
              .fontSize(14)
              .fontWeight(FontWeight.Medium)
              .fontColor(AppTheme.text)
            TextArea({ placeholder: '选填，请输入备注信息', text: this.remark })
              .width('100%')
              .height(80)
              .fontSize(14)
              .fontColor(AppTheme.text)
              .placeholderColor(AppTheme.textSecondary)
              .backgroundColor(AppTheme.sheet)
              .borderRadius(12)
              .padding(12)
              .onChange((v: string) => { this.remark = v; })
          }
          .width('100%')
          .padding(16)
          .backgroundColor(AppTheme.card)
          .borderRadius(16)
          .border({ width: 1, color: AppTheme.divider })

          // 金额明细
          Column() {
            Text('费用明细')
              .fontSize(14)
              .fontWeight(FontWeight.Medium)
              .fontColor(AppTheme.text)
              .margin({ bottom: 12 })

            Row() {
              Text('商品总额')
                .fontSize(13)
                .fontColor(AppTheme.textSecondary)
              Blank()
              Text(this.goodsTotalText())
                .fontSize(14)
                .fontColor(AppTheme.text)
                .fontFamily(AppTheme.fontMono)
            }
            .width('100%')
            .margin({ bottom: 8 })

            Row() {
              Text('运费')
                .fontSize(13)
                .fontColor(AppTheme.textSecondary)
              Blank()
              Text(this.shippingFeeText())
                .fontSize(14)
                .fontColor(this.getShippingFee() === 0 ? AppTheme.up : AppTheme.text)
            }
            .width('100%')
          }
          .width('100%')
          .padding(16)
          .backgroundColor(AppTheme.card)
          .borderRadius(16)
          .border({ width: 1, color: AppTheme.divider })
        }
        .padding(16)
      }
      .layoutWeight(1)
      .width('100%')
      .scrollBar(BarState.Off)

      // 底部结算栏
      Row() {
        Column() {
          Text('合计')
            .fontSize(12)
            .fontColor(AppTheme.textSecondary)
          Text(this.payAmountText())
            .fontSize(20)
            .fontWeight(FontWeight.Bold)
            .fontColor(AppTheme.up)
            .fontFamily(AppTheme.fontMono)
        }
        .alignItems(HorizontalAlign.Center)

        Blank()

        Button(this.submitting ? '提交中...' : '提交订单')
          .type(ButtonType.Normal)
          .width(140)
          .height(48)
          .borderRadius(999)
          .backgroundColor(this.submitting ? AppTheme.textSecondary : AppTheme.primary)
          .fontSize(15)
          .fontWeight(FontWeight.Medium)
          .enabled(!this.submitting)
          .onClick(() => this.createAndPay())
      }
      .width('100%')
      .height(72)
      .padding({ left: 16, right: 16 })
      .backgroundColor(AppTheme.card)
      .border({ width: { top: 1 }, color: AppTheme.divider })
    }
    .width('100%')
    .height('100%')
    .backgroundColor(AppTheme.bg)

      // 地址选择覆盖层（半透明遮罩 + 底部面板）
      if (this.showAddressSheet) {
        // 遮罩
        Column()
          .width('100%')
          .height('100%')
          .backgroundColor('rgba(0,0,0,0.45)')
          .onClick(() => { this.showAddressSheet = false; })
        // 底部地址面板
        Column() {
          Row() {
            Text('选择收货地址')
              .fontSize(16)
              .fontWeight(FontWeight.Bold)
              .fontColor(AppTheme.text)
            Blank()
            Text('管理地址')
              .fontSize(14)
              .fontColor(AppTheme.primary)
              .onClick(() => this.goAddressManage())
          }
          .width('100%')
          .padding({ left: 16, right: 16, bottom: 8 })

          if (this.addressVm.addresses.length === 0) {
            Column() {
              Text('暂无收货地址')
                .fontSize(14)
                .fontColor(AppTheme.textSecondary)
              Button('添加收货地址')
                .type(ButtonType.Normal)
                .fontSize(14)
                .backgroundColor(AppTheme.primary)
                .margin({ top: 16 })
                .onClick(() => this.goAddressManage())
            }
            .height(140)
            .width('100%')
            .justifyContent(FlexAlign.Center)
          }
          else {
            List({ space: 4 }) {
              ForEach(this.addressVm.addresses, (addr: ShippingAddress) => {
                ListItem() {
                  Column() {
                    Row({ space: 8 }) {
                      Text(addr.receiverName)
                        .fontSize(15)
                        .fontWeight(FontWeight.Medium)
                        .fontColor(AppTheme.text)
                      Text(addr.phone)
                        .fontSize(13)
                        .fontColor(AppTheme.textSecondary)
                      if (addr.isDefault) {
                        Text('默认')
                          .fontSize(10)
                          .fontColor(AppTheme.primary)
                          .backgroundColor('rgba(59,130,246,0.15)')
                          .padding({ left: 6, right: 6, top: 2, bottom: 2 })
                          .borderRadius(4)
                      }
                    }
                    Text(addr.fullAddress)
                      .fontSize(13)
                      .fontColor(AppTheme.text)
                      .maxLines(2)
                      .textOverflow({ overflow: TextOverflow.Ellipsis })
                  }
                  .width('100%')
                  .padding(14)
                  .backgroundColor(this.selectedAddress && this.selectedAddress.addressId === addr.addressId ? 'rgba(59,130,246,0.12)' : AppTheme.sheet)
                  .borderRadius(12)
                  .border({ width: this.selectedAddress && this.selectedAddress.addressId === addr.addressId ? 1 : 0, color: AppTheme.primary })
                  .onClick(() => this.onAddressPicked(addr))
                }
              }, (addr: ShippingAddress) => addr.addressId)
            }
            .layoutWeight(1)
            .width('100%')
            .scrollBar(BarState.Off)
          }
        }
        .width('100%')
        .height('45%')
        .padding({ left: 12, right: 12, top: 12, bottom: 12 })
        .backgroundColor(AppTheme.bg)
        .borderRadius({ topLeft: 24, topRight: 24 })
      }
    }
    .width('100%')
    .height('100%')
    }
}