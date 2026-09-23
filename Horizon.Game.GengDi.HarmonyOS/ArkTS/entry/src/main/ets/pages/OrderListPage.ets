import { AppTheme } from "../theme/AppTheme";
import { OrderViewModel, OrderStatus, FlowerOrder, ORDER_STATUS_TEXT, ORDER_STATUS_COLOR, formatOrderTime, OrderItem, ShipmentTrack } from "../viewmodels/OrderViewModels";
import { EmptyView } from "../components/CommonWidgets";

/** 订单列表页面 */
@Entry
@Component
export struct OrderListPage {
  @State orderVm: OrderViewModel = new OrderViewModel();
  @State currentTab: number = 0;
  /** 0全部 1待支付 2已支付 3已发货 4已完成 5已取消 */
  private readonly TABS: string[] = ['全部', '待支付', '已支付', '已发货', '已完成', '已取消'];

  aboutToAppear(): void {
    this.orderVm.loadOrders();
  }

  private getFilteredOrders(): FlowerOrder[] {
    switch (this.currentTab) {
      case 1: return this.orderVm.getOrdersByStatus(OrderStatus.PENDING_PAYMENT);
      case 2: return this.orderVm.getOrdersByStatus(OrderStatus.PAID);
      case 3: return this.orderVm.getOrdersByStatus(OrderStatus.SHIPPED);
      case 4: return this.orderVm.getOrdersByStatus(OrderStatus.COMPLETED);
      case 5: return this.orderVm.getOrdersByStatus(OrderStatus.CANCELLED);
      default: return this.orderVm.orders;
    }
  }

  private goToDetail(orderId: string): void {
    this.getUIContext().getRouter().pushUrl({
      url: 'pages/OrderDetailPage',
      params: { orderId: orderId }
    });
  }

  private async cancelOrder(orderId: string): Promise<void> {
    await this.orderVm.cancelOrder(orderId);
    this.orderVm.loadOrders();
  }

  private async confirmReceive(orderId: string): Promise<void> {
    await this.orderVm.confirmReceive(orderId);
    this.orderVm.loadOrders();
  }

  private async payOrder(orderId: string): Promise<void> {
    const order = this.orderVm.orders.find((o: FlowerOrder) => o.orderId === orderId);
    if (order) {
      this.getUIContext().getRouter().pushUrl({
        url: 'pages/OrderPayPage',
        params: {
          orderId: order.orderId,
          orderNo: order.orderNo,
          payAmount: String(order.payAmount),
          paymentMethod: order.paymentMethod
        }
      });
    }
  }

  build() {
    Column() {
      // 标题栏
      Row() {
        Text('我的订单')
          .fontSize(18)
          .fontWeight(FontWeight.Bold)
          .fontColor(AppTheme.text)
      }
      .width('100%')
      .height(56)
      .padding({ left: 16, right: 16 })
      .backgroundColor(AppTheme.card)
      .alignItems(VerticalAlign.Center)

      // Tab栏
      Scroll() {
        Row({ space: 4 }) {
          ForEach(this.TABS, (tab: string, idx: number) => {
            Text(tab)
              .fontSize(14)
              .fontColor(idx === this.currentTab ? AppTheme.primary : AppTheme.textSecondary)
              .fontWeight(idx === this.currentTab ? FontWeight.Medium : FontWeight.Normal)
              .padding({ left: 16, right: 16, top: 10, bottom: 10 })
              .borderRadius(999)
              .backgroundColor(idx === this.currentTab ? 'rgba(59,130,246,0.12)' : Color.Transparent)
              .onClick(() => { this.currentTab = idx; })
          }, (tab: string) => tab)
        }
        .padding({ left: 12, right: 12 })
      }
      .scrollable(ScrollDirection.Horizontal)
      .scrollBar(BarState.Off)
      .width('100%')
      .backgroundColor(AppTheme.card)
      .margin({ bottom: 8 })

      if (this.getFilteredOrders().length === 0) {
        Column() {
          EmptyView({ message: '暂无订单' })
        }
        .layoutWeight(1)
        .width('100%')
        .justifyContent(FlexAlign.Center)
      }
      else {
        Scroll() {
          Column({ space: 12 }) {
            ForEach(this.getFilteredOrders(), (order: FlowerOrder) => {
              Column() {
                // 订单头部
                Row() {
                  Text(`订单号：${order.orderNo}`)
                    .fontSize(12)
                    .fontColor(AppTheme.textSecondary)
                    .fontFamily(AppTheme.fontMono)
                  Blank()
                  Text(order.statusText)
                    .fontSize(12)
                    .fontColor(ORDER_STATUS_COLOR[order.status])
                    .fontWeight(FontWeight.Medium)
                }
                .width('100%')
                .margin({ bottom: 12 })

                // 商品列表
                ForEach(order.items.slice(0, 2), (item: OrderItem) => {
                  Row({ space: 10 }) {
                    Text(item.emoji)
                      .fontSize(20)
                    Column({ space: 2 }) {
                      Text(item.name)
                        .fontSize(14)
                        .fontColor(AppTheme.text)
                        .maxLines(1)
                        .textOverflow({ overflow: TextOverflow.Ellipsis })
                      Text(`¥${item.unitPrice.toFixed(2)} × ${item.quantity}扎`)
                        .fontSize(12)
                        .fontColor(AppTheme.textSecondary)
                    }
                    .layoutWeight(1)
                    .alignItems(HorizontalAlign.Start)
                    Text(`¥${item.subtotal.toFixed(2)}`)
                      .fontSize(14)
                      .fontWeight(FontWeight.Medium)
                      .fontColor(AppTheme.text)
                  }
                  .width('100%')
                  .margin({ bottom: 6 })
                }, (item: OrderItem) => `${item.speciesId}-${item.quantity}`)

                if (order.items.length > 2) {
                  Text(`还有${order.items.length - 2}件商品`)
                    .fontSize(12)
                    .fontColor(AppTheme.textSecondary)
                    .margin({ bottom: 6 })
                }

                // 底部：金额和操作
                Row() {
                  Column({ space: 4 }) {
                    Text(`共${order.items.length}件商品 合计：`)
                      .fontSize(12)
                      .fontColor(AppTheme.textSecondary)
                    Text(`¥${order.totalAmount.toFixed(2)}`)
                      .fontSize(16)
                      .fontWeight(FontWeight.Bold)
                      .fontColor(AppTheme.text)
                      .fontFamily(AppTheme.fontMono)
                  }
                  .alignItems(HorizontalAlign.Center)

                  Blank()

                  // 操作按钮
                  Row({ space: 8 }) {
                    if (order.status === OrderStatus.PENDING_PAYMENT) {
                      Button('取消订单')
                        .type(ButtonType.Normal)
                        .fontSize(13)
                        .height(32)
                        .padding({ left: 12, right: 12 })
                        .borderRadius(999)
                        .backgroundColor(AppTheme.sheet)
                        .fontColor(AppTheme.textSecondary)
                        .onClick(() => this.cancelOrder(order.orderId))
                      Button('立即支付')
                        .type(ButtonType.Normal)
                        .fontSize(13)
                        .height(32)
                        .padding({ left: 12, right: 12 })
                        .borderRadius(999)
                        .backgroundColor(AppTheme.primary)
                        .fontColor(Color.White)
                        .onClick(() => this.payOrder(order.orderId))
                    }
                    else if (order.status === OrderStatus.SHIPPED) {
                      Button('确认收货')
                        .type(ButtonType.Normal)
                        .fontSize(13)
                        .height(32)
                        .padding({ left: 12, right: 12 })
                        .borderRadius(999)
                        .backgroundColor(AppTheme.primary)
                        .fontColor(Color.White)
                        .onClick(() => this.confirmReceive(order.orderId))
                    }
                    else {
                      Button('查看详情')
                        .type(ButtonType.Normal)
                        .fontSize(13)
                        .height(32)
                        .padding({ left: 12, right: 12 })
                        .borderRadius(999)
                        .backgroundColor(AppTheme.sheet)
                        .fontColor(AppTheme.text)
                        .onClick(() => this.goToDetail(order.orderId))
                    }
                  }
                }
                .width('100%')
                .margin({ top: 10 })
              }
              .width('100%')
              .padding(16)
              .backgroundColor(AppTheme.card)
              .borderRadius(16)
              .border({ width: 1, color: AppTheme.divider })
              .onClick(() => this.goToDetail(order.orderId))
            }, (order: FlowerOrder) => order.orderId)
          }
          .padding(16)
        }
        .layoutWeight(1)
        .width('100%')
        .scrollBar(BarState.Off)
      }
    }
    .width('100%')
    .height('100%')
    .backgroundColor(AppTheme.bg)
  }
}

/** 订单详情页面 */
@Component
export struct OrderDetailPage {
  @State orderVm: OrderViewModel = new OrderViewModel();
  @State orderId: string = '';
  @State order: FlowerOrder | null = null;

  aboutToAppear(): void {
    this.loadOrder();
  }

  private loadOrder(): void {
    this.orderVm.loadOrders().then(() => {
      this.order = this.orderVm.orders.find((o: FlowerOrder) => o.orderId === this.orderId) ?? null;
    });
  }

  private async cancelOrder(): Promise<void> {
    if (!this.order) {
      return;
    }
    await this.orderVm.cancelOrder(this.order.orderId);
    this.loadOrder();
  }

  private async payOrder(): Promise<void> {
    if (!this.order) {
      return;
    }
    this.getUIContext().getRouter().pushUrl({
      url: 'pages/OrderPayPage',
      params: {
        orderId: this.order.orderId,
        orderNo: this.order.orderNo,
        payAmount: String(this.order.payAmount),
        paymentMethod: this.order.paymentMethod
      }
    });
  }

  private async confirmReceive(): Promise<void> {
    if (!this.order) {
      return;
    }
    await this.orderVm.confirmReceive(this.order.orderId);
    this.loadOrder();
  }

  private async shipOrder(): Promise<void> {
    if (!this.order) {
      return;
    }
    await this.orderVm.shipOrder(this.order.orderId);
    this.loadOrder();
  }

  /** 获取物流信息（模拟）*/
  private getTrackingInfo(): ShipmentTrack[] {
    if (!this.order) {
      return [];
    }
    const tracks: ShipmentTrack[] = [];
    if (this.order.completeTime) {
      tracks.push({ time: formatOrderTime(this.order.completeTime), description: '交易成功，感谢您的购买' });
    }
    if (this.order.shipTime) {
      tracks.push({ time: formatOrderTime(this.order.shipTime), description: `商品已由${this.order.trackingCompany}揽收，运单号：${this.order.trackingNo}` });
      tracks.push({ time: formatOrderTime(this.order.shipTime), description: '商品正在配送中，请保持手机畅通' });
    }
    if (this.order.payTime) {
      tracks.push({ time: formatOrderTime(this.order.payTime), description: '订单已支付成功，商家正在备货' });
    }
    tracks.push({ time: formatOrderTime(this.order.createTime), description: '订单已创建，等待支付' });
    return tracks;
  }

  build() {
    Column() {
      // 标题栏
      Row() {
        Text('订单详情')
          .fontSize(18)
          .fontWeight(FontWeight.Bold)
          .fontColor(AppTheme.text)
      }
      .width('100%')
      .height(56)
      .padding({ left: 16, right: 16 })
      .backgroundColor(AppTheme.card)
      .alignItems(VerticalAlign.Center)

      if (!this.order) {
        Column() {
          Text('订单不存在或已删除')
            .fontSize(14)
            .fontColor(AppTheme.textSecondary)
        }
        .layoutWeight(1)
        .width('100%')
        .justifyContent(FlexAlign.Center)
      }
      else {
        Scroll() {
          Column({ space: 12 }) {
            // 状态横幅
            Column() {
              Row({ space: 12 }) {
                Text(this.order.status === OrderStatus.PENDING_PAYMENT ? '⏰' :
                     this.order.status === OrderStatus.SHIPPED ? '📦' :
                     this.order.status === OrderStatus.COMPLETED ? '✓' :
                     this.order.status === OrderStatus.CANCELLED ? '✕' : '📝')
                  .fontSize(28)
                Column({ space: 4 }) {
                  Text(this.order.statusText)
                    .fontSize(18)
                    .fontWeight(FontWeight.Bold)
                    .fontColor(ORDER_STATUS_COLOR[this.order.status])
                  Text(this.order.status === OrderStatus.PENDING_PAYMENT ? '请尽快完成支付' :
                       this.order.status === OrderStatus.SHIPPED ? '商品已发出，请注意查收' :
                       this.order.status === OrderStatus.COMPLETED ? '感谢您的购买' :
                       this.order.status === OrderStatus.CANCELLED ? '订单已取消' :
                       '订单处理中')
                    .fontSize(13)
                    .fontColor(AppTheme.textSecondary)
                }
                .alignItems(HorizontalAlign.Center)
              }
              .width('100%')
            }
            .width('100%')
            .padding(20)
            .backgroundColor(AppTheme.card)
            .borderRadius(16)

            // 收货地址
            if (this.order.addressSnapshot) {
              Column() {
                Text('收货信息')
                  .fontSize(14)
                  .fontWeight(FontWeight.Medium)
                  .fontColor(AppTheme.text)
                  .width('100%')
                  .margin({ bottom: 10 })

                Row({ space: 12 }) {
                  Text('📍')
                    .fontSize(16)
                  Column({ space: 4 }) {
                    Row({ space: 8 }) {
                      Text(this.order.addressSnapshot.receiverName)
                        .fontSize(15)
                        .fontWeight(FontWeight.Medium)
                        .fontColor(AppTheme.text)
                      Text(this.order.addressSnapshot.phone)
                        .fontSize(13)
                        .fontColor(AppTheme.textSecondary)
                    }
                    Text(this.order.addressSnapshot.fullAddress)
                      .fontSize(14)
                      .fontColor(AppTheme.text)
                  }
                  .layoutWeight(1)
                  .alignItems(HorizontalAlign.Start)
                }
                .width('100%')
              }
              .width('100%')
              .padding(16)
              .backgroundColor(AppTheme.card)
              .borderRadius(16)
            }

            // 商品清单
            Column() {
              Text('商品清单')
                .fontSize(14)
                .fontWeight(FontWeight.Medium)
                .fontColor(AppTheme.text)
                .width('100%')
                .margin({ bottom: 12 })

              ForEach(this.order.items, (item: OrderItem) => {
                Row({ space: 12 }) {
                  Text(item.emoji)
                    .fontSize(22)
                  Column({ space: 4 }) {
                    Text(item.name)
                      .fontSize(14)
                      .fontWeight(FontWeight.Medium)
                      .fontColor(AppTheme.text)
                    Text(`¥${item.unitPrice.toFixed(2)} × ${item.quantity}扎`)
                      .fontSize(12)
                      .fontColor(AppTheme.textSecondary)
                  }
                  .layoutWeight(1)
                  .alignItems(HorizontalAlign.Start)
                  Text(`¥${item.subtotal.toFixed(2)}`)
                    .fontSize(15)
                    .fontWeight(FontWeight.Medium)
                    .fontColor(AppTheme.text)
                }
                .width('100%')
                .padding({ bottom: 12 })
              }, (item: OrderItem) => `${item.speciesId}-${item.quantity}`)

            }
            .width('100%')
            .padding(16)
            .backgroundColor(AppTheme.card)
            .borderRadius(16)

            // 物流信息
            if (this.order.status === OrderStatus.SHIPPED || this.order.status === OrderStatus.COMPLETED) {
              Column() {
                Text('物流信息')
                  .fontSize(14)
                  .fontWeight(FontWeight.Medium)
                  .fontColor(AppTheme.text)
                  .width('100%')
                  .margin({ bottom: 12 })

                Column({ space: 0 }) {
                  ForEach(this.getTrackingInfo(), (track: ShipmentTrack, idx: number) => {
                    Row({ space: 12 }) {
                      Column() {
                        Circle({ width: 10, height: 10 })
                          .fill(idx === 0 ? AppTheme.primary : AppTheme.textSecondary)
                          .margin({ top: 4 })
                        if (idx < this.getTrackingInfo().length - 1) {
                          Column()
                            .width(2)
                            .layoutWeight(1)
                            .backgroundColor(AppTheme.divider)
                            .margin({ top: 4, bottom: 4 })
                        }
                      }
                      .width(20)
                      .height(idx < this.getTrackingInfo().length - 1 ? 60 : 18)
                      .alignItems(HorizontalAlign.Center)

                      Column({ space: 4 }) {
                        Text(track.description)
                          .fontSize(13)
                          .fontColor(idx === 0 ? AppTheme.text : AppTheme.textSecondary)
                        Text(track.time)
                          .fontSize(11)
                          .fontColor(AppTheme.textSecondary)
                      }
                      .layoutWeight(1)
                      .alignItems(HorizontalAlign.Start)
                    }
                    .width('100%')
                  }, (track: ShipmentTrack, idx: number) => `${idx}`)
                }
              }
              .width('100%')
              .padding(16)
              .backgroundColor(AppTheme.card)
              .borderRadius(16)
            }

            // 费用明细
            Column() {
              Text('费用明细')
                .fontSize(14)
                .fontWeight(FontWeight.Medium)
                .fontColor(AppTheme.text)
                .width('100%')
                .margin({ bottom: 12 })

              Row() {
                Text('商品总额')
                  .fontSize(13)
                  .fontColor(AppTheme.textSecondary)
                Blank()
                Text(`¥${this.order.goodsTotal.toFixed(2)}`)
                  .fontSize(14)
                  .fontColor(AppTheme.text)
              }
              .width('100%')
              .margin({ bottom: 8 })

              Row() {
                Text('运费')
                  .fontSize(13)
                  .fontColor(AppTheme.textSecondary)
                Blank()
                Text(this.order.shippingFee === 0 ? '免运费' : `¥${this.order.shippingFee.toFixed(2)}`)
                  .fontSize(14)
                  .fontColor(this.order.shippingFee === 0 ? AppTheme.up : AppTheme.text)
              }
              .width('100%')
              .margin({ bottom: 8 })

              if (this.order.discount > 0) {
                Row() {
                  Text('优惠')
                    .fontSize(13)
                    .fontColor(AppTheme.textSecondary)
                  Blank()
                  Text(`-¥${this.order.discount.toFixed(2)}`)
                    .fontSize(14)
                    .fontColor(AppTheme.up)
                }
                .width('100%')
                .margin({ bottom: 8 })
              }

              Row() {
                Text('实付金额')
                  .fontSize(14)
                  .fontColor(AppTheme.text)
                  .fontWeight(FontWeight.Medium)
                Blank()
                Text(`¥${this.order.payAmount.toFixed(2)}`)
                  .fontSize(18)
                  .fontWeight(FontWeight.Bold)
                  .fontColor(AppTheme.up)
                  .fontFamily(AppTheme.fontMono)
              }
              .width('100%')
            }
            .width('100%')
            .padding(16)
            .backgroundColor(AppTheme.card)
            .borderRadius(16)

            // 订单信息
            Column() {
              Text('订单信息')
                .fontSize(14)
                .fontWeight(FontWeight.Medium)
                .fontColor(AppTheme.text)
                .width('100%')
                .margin({ bottom: 12 })

              Row() {
                Text('订单编号')
                  .fontSize(13)
                  .fontColor(AppTheme.textSecondary)
                Blank()
                Text(this.order.orderNo)
                  .fontSize(13)
                  .fontColor(AppTheme.text)
                  .fontFamily(AppTheme.fontMono)
              }
              .width('100%')
              .margin({ bottom: 8 })

              Row() {
                Text('创建时间')
                  .fontSize(13)
                  .fontColor(AppTheme.textSecondary)
                Blank()
                Text(formatOrderTime(this.order.createTime))
                  .fontSize(13)
                  .fontColor(AppTheme.text)
              }
              .width('100%')
              .margin({ bottom: 8 })

              if (this.order.payTime) {
                Row() {
                  Text('支付时间')
                    .fontSize(13)
                    .fontColor(AppTheme.textSecondary)
                  Blank()
                  Text(formatOrderTime(this.order.payTime))
                    .fontSize(13)
                    .fontColor(AppTheme.text)
                }
                .width('100%')
                .margin({ bottom: 8 })
              }

              if (this.order.shipTime) {
                Row() {
                  Text('发货时间')
                    .fontSize(13)
                    .fontColor(AppTheme.textSecondary)
                  Blank()
                  Text(formatOrderTime(this.order.shipTime))
                    .fontSize(13)
                    .fontColor(AppTheme.text)
                }
                .width('100%')
                .margin({ bottom: 8 })
              }

              if (this.order.completeTime) {
                Row() {
                  Text('完成时间')
                    .fontSize(13)
                    .fontColor(AppTheme.textSecondary)
                  Blank()
                  Text(formatOrderTime(this.order.completeTime))
                    .fontSize(13)
                    .fontColor(AppTheme.text)
                }
                .width('100%')
              }

              if (this.order.remark) {
                Row() {
                  Text('备注')
                    .fontSize(13)
                    .fontColor(AppTheme.textSecondary)
                  Blank()
                  Text(this.order.remark)
                    .fontSize(13)
                    .fontColor(AppTheme.text)
                }
                .width('100%')
                .margin({ top: 8 })
              }
            }
            .width('100%')
            .padding(16)
            .backgroundColor(AppTheme.card)
            .borderRadius(16)
          }
          .padding(16)
        }
        .layoutWeight(1)
        .width('100%')
        .scrollBar(BarState.Off)

        // 底部操作栏
        Row() {
          if (this.order.status === OrderStatus.PENDING_PAYMENT) {
            Button('取消订单')
              .type(ButtonType.Normal)
              .layoutWeight(1)
              .height(44)
              .borderRadius(999)
              .backgroundColor(AppTheme.sheet)
              .fontColor(AppTheme.text)
              .onClick(() => this.cancelOrder())
            Button('立即支付')
              .type(ButtonType.Normal)
              .layoutWeight(1)
              .height(44)
              .borderRadius(999)
              .backgroundColor(AppTheme.primary)
              .fontColor(Color.White)
              .onClick(() => this.payOrder())
          }
          else if (this.order.status === OrderStatus.PAID) {
            Button('模拟发货')
              .type(ButtonType.Normal)
              .layoutWeight(1)
              .height(44)
              .borderRadius(999)
              .backgroundColor(AppTheme.primary)
              .fontColor(Color.White)
              .onClick(() => this.shipOrder())
          }
          else if (this.order.status === OrderStatus.SHIPPED) {
            Button('确认收货')
              .type(ButtonType.Normal)
              .width('100%')
              .height(44)
              .borderRadius(999)
              .backgroundColor(AppTheme.primary)
              .fontColor(Color.White)
              .onClick(() => this.confirmReceive())
          }
        }
        .width('100%')
        .height(72)
        .padding({ left: 16, right: 16 })
        .backgroundColor(AppTheme.card)
        .border({ width: { top: 1 }, color: AppTheme.divider })
      }
    }
    .width('100%')
    .height('100%')
    .backgroundColor(AppTheme.bg)
  }
}