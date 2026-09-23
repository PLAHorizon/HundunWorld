import { AppTheme } from "../theme/AppTheme";
import { OrderViewModel, PaymentMethod, ORDER_STATUS_TEXT, FlowerOrder } from "../viewmodels/OrderViewModels";
import { PaymentService, PAY_CHANNEL_WECHAT, PAY_CHANNEL_ALIPAY } from "../services/PaymentService";

/** 支付页面 */
@Entry
@Component
export struct OrderPayPage {
  @State orderVm: OrderViewModel = new OrderViewModel();
  @State orderId: string = '';
  @State orderNo: string = '';
  @State payAmount: number = 0;
  @State paymentMethod: PaymentMethod = PaymentMethod.WECHAT;
  @State paying: boolean = false;
  @State paySuccess: boolean = false;
  /** 支付进度提示（预下单/拉起App/等待确认） */
  @State payProgressText: string = '';
  @State countdown: number = 900; // 15分钟倒计时（秒）
  private timerId: number = -1;

  aboutToAppear(): void {
    // 从路由参数获取订单信息
    try {
      const params = this.getUIContext().getRouter().getParams() as Record<string, string>;
      if (params) {
        if (params['orderId'] && typeof params['orderId'] === 'string') {
          this.orderId = params['orderId'];
        }
        if (params['orderNo'] && typeof params['orderNo'] === 'string') {
          this.orderNo = params['orderNo'];
        }
        if (params['payAmount']) {
          const amt = Number(params['payAmount']);
          if (!Number.isNaN(amt) && amt >= 0) {
            this.payAmount = amt;
          }
        }
        if (params['paymentMethod']) {
          const pm = params['paymentMethod'] as PaymentMethod;
          if (pm === PaymentMethod.WECHAT || pm === PaymentMethod.ALIPAY) {
            this.paymentMethod = pm;
          }
        }
      }
    }
    catch (_e) {
      // 忽略参数解析错误
    }
    this.loadOrderInfo();
    this.startCountdown();
  }

  aboutToDisappear(): void {
    this.stopCountdown();
  }

  private loadOrderInfo(): void {
    try {
      this.orderVm.loadOrders().then(() => {
        try {
          const order = this.orderVm.orders.find((o) => o.orderId === this.orderId);
          if (order) {
            this.orderNo = order.orderNo || '';
            this.payAmount = typeof order.payAmount === 'number' ? order.payAmount : 0;
            if (order.paymentMethod === PaymentMethod.WECHAT || order.paymentMethod === PaymentMethod.ALIPAY) {
              this.paymentMethod = order.paymentMethod;
            }
          }
        } catch (_e) {
          // 忽略查找错误
        }
      }).catch(() => {
        // 忽略加载错误
      });
    } catch (_e) {
      // 忽略启动错误
    }
  }

  private startCountdown(): void {
    try {
      this.timerId = setInterval(() => {
        if (this.countdown > 0) {
          this.countdown--;
        }
        else {
          this.stopCountdown();
          this.timeout();
        }
      }, 1000) as number;
    } catch (_e) {
      // 忽略定时器启动错误
    }
  }

  private stopCountdown(): void {
    try {
      if (this.timerId !== -1) {
        clearInterval(this.timerId);
        this.timerId = -1;
      }
    } catch (_e) {
      // 忽略清除错误
      this.timerId = -1;
    }
  }

  private formatCountdown(seconds: number): string {
    const s = Math.max(0, seconds);
    const mins = Math.floor(s / 60);
    const secs = s % 60;
    const pad = (n: number): string => `${n < 10 ? '0' : ''}${n}`;
    return `${pad(mins)}:${pad(secs)}`;
  }

  private timeout(): void {
    try {
      // 支付超时，取消订单
      this.orderVm.cancelOrder(this.orderId).then(() => {
        this.toast('支付超时，订单已取消');
      }).catch(() => {});
      setTimeout(() => {
        try {
          this.getUIContext().getRouter().back();
        } catch (_e) {
          // 忽略返回错误
        }
      }, 1500);
    } catch (_e) {
      // 忽略超时处理错误
    }
  }

  private async doPay(): Promise<void> {
    try {
      if (this.paying) {
        return;
      }

      // 定位本地订单并校验服务端订单号（正式支付前置）
      const order = this.orderVm.orders.find((o: FlowerOrder) => o.orderId === this.orderId);
      if (!order || order.serverOrderId <= 0) {
        this.toast('订单未同步服务端，无法发起支付（请返回重新提交订单）');
        return;
      }

      // 渠道映射：PaymentMethod（字符串）→ 服务端 PaymentChannel（数值）
      const channel = this.paymentMethod === PaymentMethod.ALIPAY ? PAY_CHANNEL_ALIPAY : PAY_CHANNEL_WECHAT;

      this.paying = true;
      // 金额以服务端核算为准（服务端会再按订单实价校验）
      const payAmount = order.serverPayAmount > 0 ? order.serverPayAmount : this.payAmount;

      // 正式支付：服务端预下单 → 拉起微信/支付宝 → 轮询支付结果
      const result = await PaymentService.getInstance().payAsync(order.serverOrderId, channel, payAmount,
        (msg: string) => {
          this.payProgressText = msg;
        });

      this.paying = false;
      if (result.success) {
        // 支付成功：更新本地订单状态
        await this.orderVm.payOrder(this.orderId);
        this.paySuccess = true;
        this.stopCountdown();
      }
      else {
        this.toast(result.message.length > 0 ? result.message : '支付未完成');
      }
    } catch (e) {
      this.paying = false;
      console.error(`[OrderPayPage] doPay 异常: ${JSON.stringify(e)}`);
      this.toast('支付失败，请重试');
    }
  }

  private toast(message: string): void {
    try {
      this.getUIContext().getPromptAction().showToast({ message: message, duration: 2000 });
    } catch (_e) {
      // 忽略 toast 错误
    }
  }

  private goToOrderDetail(): void {
    try {
      this.getUIContext().getRouter().replaceUrl({
        url: 'pages/OrderListPage'
      });
    } catch (_e) {
      // 忽略路由错误
    }
  }

  private backToOrderList(): void {
    try {
      this.getUIContext().getRouter().replaceUrl({
        url: 'pages/OrderListPage'
      });
    } catch (_e) {
      // 忽略路由错误
    }
  }

  // ===== UI 展示辅助方法（禁止在 UI DSL 内写 .toFixed 等非 UI 语法）=====
  /** 支付金额文本 */
  private payAmountText(): string {
    return `¥${this.payAmount.toFixed(2)}`;
  }

  /** 支付金额带前缀文本 */
  private payAmountLabelText(): string {
    return `支付金额：¥${this.payAmount.toFixed(2)}`;
  }

  /** 确认支付按钮文本 */
  private confirmPayBtnText(): string {
    return this.paying ? '支付中...' : `确认支付 ¥${this.payAmount.toFixed(2)}`;
  }

  /** 订单号文本（空值保护） */
  private orderNoText(): string {
    return this.orderNo && this.orderNo.length > 0 ? `订单号：${this.orderNo}` : '订单号：--';
  }

  /** 倒计时文本 */
  private countdownText(): string {
    return this.formatCountdown(this.countdown);
  }

  build() {
    Column() {
      // 标题栏
      Row() {
        Text('订单支付')
          .fontSize(18)
          .fontWeight(FontWeight.Bold)
          .fontColor(AppTheme.text)
      }
      .width('100%')
      .height(56)
      .padding({ left: 16, right: 16 })
      .backgroundColor(AppTheme.card)
      .alignItems(VerticalAlign.Center)

      if (this.paySuccess) {
        // 支付成功页
        Column() {
          Column() {
            Text('✓')
              .fontSize(48)
              .fontColor(AppTheme.up)
          }
          .width(80)
          .height(80)
          .borderRadius(40)
          .backgroundColor('rgba(16,185,129,0.15)')
          .alignItems(HorizontalAlign.Center)
          .justifyContent(FlexAlign.Center)
          .margin({ bottom: 20 })

          Text('支付成功')
            .fontSize(22)
            .fontWeight(FontWeight.Bold)
            .fontColor(AppTheme.text)
            .margin({ bottom: 8 })

          Text(this.orderNoText())
            .fontSize(13)
            .fontColor(AppTheme.textSecondary)
            .margin({ bottom: 4 })

          Text(this.payAmountLabelText())
            .fontSize(20)
            .fontWeight(FontWeight.Bold)
            .fontColor(AppTheme.up)
            .fontFamily(AppTheme.fontMono)
            .margin({ bottom: 32 })

          Row({ space: 12 }) {
            Button('查看订单详情')
              .type(ButtonType.Normal)
              .layoutWeight(1)
              .height(48)
              .borderRadius(999)
              .backgroundColor(AppTheme.primary)
              .fontSize(15)
              .onClick(() => this.goToOrderDetail())

            Button('返回订单列表')
              .type(ButtonType.Normal)
              .layoutWeight(1)
              .height(48)
              .borderRadius(999)
              .backgroundColor(AppTheme.card)
              .fontColor(AppTheme.text)
              .fontSize(15)
              .border({ width: 1, color: AppTheme.divider })
              .onClick(() => this.backToOrderList())
          }
          .width('100%')
          .padding({ left: 16, right: 16 })
        }
        .layoutWeight(1)
        .width('100%')
        .justifyContent(FlexAlign.Center)
        .alignItems(HorizontalAlign.Center)
      }
      else {
        // 支付进行中
        Column() {
          // 订单信息
          Column({ space: 16 }) {
            Text('待支付金额')
              .fontSize(14)
              .fontColor(AppTheme.textSecondary)

            Text(this.payAmountText())
              .fontSize(42)
              .fontWeight(FontWeight.Bold)
              .fontColor(AppTheme.text)
              .fontFamily(AppTheme.fontMono)

            Text(this.orderNoText())
              .fontSize(13)
              .fontColor(AppTheme.textSecondary)
          }
          .width('100%')
          .padding({ top: 32, bottom: 32 })
          .backgroundColor(AppTheme.card)
          .margin({ bottom: 16 })

          // 倒计时提示
          Row() {
            Text('支付剩余时间：')
              .fontSize(13)
              .fontColor(AppTheme.textSecondary)
            Text(this.countdownText())
              .fontSize(13)
              .fontColor(this.countdown < 60 ? AppTheme.down : AppTheme.text)
              .fontFamily(AppTheme.fontMono)
          }
          .width('100%')
          .justifyContent(FlexAlign.Center)
          .margin({ bottom: 24 })

          // 支付方式选择
          Column({ space: 12 }) {
            Text('选择支付方式')
              .fontSize(14)
              .fontWeight(FontWeight.Medium)
              .fontColor(AppTheme.text)
              .width('100%')

            // 微信支付
            Row({ space: 12 }) {
              Text('💚')
                .fontSize(24)
              Column({ space: 4 }) {
                Text('微信支付')
                  .fontSize(15)
                  .fontWeight(FontWeight.Medium)
                  .fontColor(AppTheme.text)
                Text('使用微信安全支付')
                  .fontSize(12)
                  .fontColor(AppTheme.textSecondary)
              }
              .layoutWeight(1)
              .alignItems(HorizontalAlign.Center)
              Text(this.paymentMethod === PaymentMethod.WECHAT ? '✓' : '')
                .fontSize(18)
                .fontColor(AppTheme.primary)
            }
            .width('100%')
            .padding(16)
            .backgroundColor(this.paymentMethod === PaymentMethod.WECHAT ? 'rgba(38,181,160,0.1)' : AppTheme.card)
            .borderRadius(12)
            .border({ width: 1, color: this.paymentMethod === PaymentMethod.WECHAT ? AppTheme.primary : AppTheme.divider })
            .onClick(() => { this.paymentMethod = PaymentMethod.WECHAT; })

            // 支付宝
            Row({ space: 12 }) {
              Text('💙')
                .fontSize(24)
              Column({ space: 4 }) {
                Text('支付宝')
                  .fontSize(15)
                  .fontWeight(FontWeight.Medium)
                  .fontColor(AppTheme.text)
                Text('使用支付宝快捷支付')
                  .fontSize(12)
                  .fontColor(AppTheme.textSecondary)
              }
              .layoutWeight(1)
              .alignItems(HorizontalAlign.Center)
              Text(this.paymentMethod === PaymentMethod.ALIPAY ? '✓' : '')
                .fontSize(18)
                .fontColor(AppTheme.primary)
            }
            .width('100%')
            .padding(16)
            .backgroundColor(this.paymentMethod === PaymentMethod.ALIPAY ? 'rgba(59,130,246,0.1)' : AppTheme.card)
            .borderRadius(12)
            .border({ width: 1, color: this.paymentMethod === PaymentMethod.ALIPAY ? AppTheme.primary : AppTheme.divider })
            .onClick(() => { this.paymentMethod = PaymentMethod.ALIPAY; })
          }
          .width('100%')
          .padding(16)
          .backgroundColor(AppTheme.card)
          .borderRadius(16)
          .margin({ bottom: 32 })

          // 支付进度提示（预下单/拉起App/等待确认）
          if (this.payProgressText.length > 0) {
            Text(this.payProgressText)
              .fontSize(13)
              .fontColor(AppTheme.textSecondary)
              .width('100%')
              .textAlign(TextAlign.Center)
              .margin({ bottom: 12 })
          }

          // 支付按钮
          Button(this.confirmPayBtnText())
            .type(ButtonType.Normal)
            .width('100%')
            .height(52)
            .borderRadius(999)
            .backgroundColor(this.paying ? AppTheme.textSecondary : AppTheme.primary)
            .fontSize(16)
            .fontWeight(FontWeight.Medium)
            .enabled(!this.paying)
            .onClick(() => this.doPay())
        }
        .layoutWeight(1)
        .width('100%')
        .padding(16)
      }
    }
    .width('100%')
    .height('100%')
    .backgroundColor(AppTheme.bg)
  }
}