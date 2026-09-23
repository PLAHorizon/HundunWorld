import { AppTheme } from "../../theme/AppTheme";
import { LoadingView, ErrorView, EmptyView } from "../../components/CommonWidgets";
import { MarketViewModel, MarketItem, MarketAlert, MarketOrder } from "../../viewmodels/FlowerViewModels";
import { CartViewModel, OrderItem, marketItemToOrderItem } from "../../viewmodels/OrderViewModels";
import { AppContext } from "../../model/AppContext";

/** 价格区间菜单文案（index 与 vm.priceRange 对应）*/
const PRICE_LABELS: string[] = ['价格', '¥3以下', '¥3-5', '¥5-8', '¥8以上'];
/** 排序菜单文案（index 与 vm.sortMode 对应）*/
const SORT_LABELS: string[] = ['排序', '涨幅优先', '跌幅优先', '价格最高', '成交量'];

/** 商品卡（@Prop 绑定第一层属性，数组替换触发重建）*/
@Component
struct GoodsCard {
  @Prop item: MarketItem;
  onOpen: (item: MarketItem) => void = () => {
  };
  onOrder: (item: MarketItem) => void = () => {
  };

  build() {
    Column() {
      Stack({ alignContent: Alignment.TopStart }) {
        Column() {
          Text(this.item.emoji)
            .fontSize(44)
            .opacity(0.9)
        }
        .width('100%')
        .aspectRatio(1.25)
        .linearGradient({
          angle: 135,
          colors: [[this.item.gradStart, 0], [this.item.gradEnd, 1]]
        })
        .alignItems(HorizontalAlign.Center)
        .justifyContent(FlexAlign.Center)

        Text(this.item.inStock ? '现货' : '预订')
          .fontSize(10)
          .fontWeight(FontWeight.Medium)
          .fontColor(this.item.inStock ? AppTheme.up : AppTheme.warn)
          .backgroundColor(this.item.inStock ? 'rgba(38,181,160,0.15)' : 'rgba(255,179,0,0.18)')
          .borderRadius(999)
          .padding({ left: 8, right: 8, top: 3, bottom: 3 })
          .margin(10)
      }
      .width('100%')

      Column({ space: 5 }) {
        Text(this.item.name)
          .fontSize(14)
          .fontWeight(FontWeight.Bold)
          .fontColor(AppTheme.text)
          .maxLines(1)
          .textOverflow({ overflow: TextOverflow.Ellipsis })
          .width('100%')

        Text(this.item.originGrade)
          .fontSize(11)
          .fontColor(AppTheme.textSecondary)
          .width('100%')

        Row() {
          Text(this.item.priceText)
            .fontSize(18)
            .fontWeight(FontWeight.Bold)
            .fontColor(AppTheme.text)
            .fontFamily(AppTheme.fontMono)
          Text(' /扎')
            .fontSize(10)
            .fontColor(AppTheme.textSecondary)
          Blank()
          Text(this.item.changeText)
            .fontSize(12)
            .fontFamily(AppTheme.fontMono)
            .fontColor(this.item.changeUp ? AppTheme.up : AppTheme.down)
        }
        .width('100%')
        .alignItems(VerticalAlign.Bottom)

        Button() {
          Row({ space: 5 }) {
            Text('🛒').fontSize(14)
            Text('立即购买').fontSize(13).fontWeight(FontWeight.Medium)
          }
        }
        .type(ButtonType.Normal)
        .width('100%')
        .height(40)
        .borderRadius(999)
        .backgroundColor(AppTheme.primary)
        .margin({ top: 4 })
        .onClick(() => this.onOrder(this.item))
      }
      .padding(12)
      .alignItems(HorizontalAlign.Start)
    }
    .borderRadius(24)
    .backgroundColor(AppTheme.card)
    .border({ width: 1, color: AppTheme.divider })
    .clip(true)
    .onClick(() => this.onOpen(this.item))
  }
}

/** 库存预警卡 */
@Component
struct AlertCard {
  @Prop alert: MarketAlert;
  onRestock: (alert: MarketAlert) => void = () => {
  };

  build() {
    Column({ space: 8 }) {
      Row() {
        Row({ space: 6 }) {
          Circle({ width: 6, height: 6 })
            .fill(this.alert.severe ? AppTheme.down : AppTheme.warn)
          Text(this.alert.level)
            .fontSize(11)
            .fontColor(AppTheme.textSecondary)
        }
        .alignItems(VerticalAlign.Center)

        Blank()
        Text(this.alert.timeAgo)
          .fontSize(10)
          .fontColor(AppTheme.textSecondary)
      }
      .width('100%')

      Text(this.alert.name)
        .fontSize(14)
        .fontWeight(FontWeight.Bold)
        .fontColor(AppTheme.text)
        .width('100%')

      Row({ space: 6 }) {
        Text(`${this.alert.remain}`)
          .fontSize(24)
          .fontWeight(FontWeight.Bold)
          .fontFamily(AppTheme.fontMono)
          .fontColor(this.alert.severe ? AppTheme.down : AppTheme.warn)
        Text('扎剩余')
          .fontSize(11)
          .fontColor(AppTheme.textSecondary)
      }
      .alignItems(VerticalAlign.Bottom)

      Text(`安全库存 ${this.alert.safe} 扎 · 缺口 ${this.alert.gap} 扎`)
        .fontSize(11)
        .fontColor(AppTheme.textSecondary)
        .width('100%')

      Button() {
        Row({ space: 5 }) {
          Text('🚚').fontSize(13)
          Text(this.alert.severe ? '立即补货' : '一键补货').fontSize(12).fontWeight(FontWeight.Medium)
        }
      }
      .type(ButtonType.Normal)
      .width('100%')
      .height(38)
      .borderRadius(999)
      .backgroundColor(this.alert.severe ? 'rgba(255,107,107,0.14)' : 'rgba(255,179,0,0.16)')
      .fontColor(this.alert.severe ? AppTheme.down : AppTheme.warn)
      .margin({ top: 2 })
      .onClick(() => this.onRestock(this.alert))
    }
    .width(256)
    .borderRadius(24)
    .padding(16)
    .backgroundColor('rgba(19,23,34,0.85)')
    .border({ width: 1, color: AppTheme.divider })
    .alignItems(HorizontalAlign.Start)
  }
}

@Component
export struct MarketTab {
  @State vm: MarketViewModel = new MarketViewModel();
  @State cartVm: CartViewModel = new CartViewModel();
  @State loading: boolean = false;
  @State errorText: string = '';
  @State searchText: string = '';
  @State categories: string[] = ['全部'];
  @State activeCategory: string = '全部';
  @State origins: string[] = ['全部'];
  @State priceRange: number = 0;
  @State originFilter: string = '全部';
  @State sortMode: number = 0;
  @State rows: MarketItem[][] = [];
  @State alerts: MarketAlert[] = [];
  @State totalCountText: string = '';
  @State updateTimeText: string = '';
  // ===== 市场统计概览 =====
  @State avgPriceText: string = '--';
  @State priceChangeText: string = '--';
  @State priceChangeUp: boolean = true;
  @State activeCountText: string = '0';
  @State alertCountText: string = '0';
  // ===== 下拉刷新 =====
  @State refreshing: boolean = false;
  // ===== 下单面板 =====
  @State orderSheetVisible: boolean = false;
  @State orderTarget: MarketItem | null = null;
  @State orderQty: number = 1;
  @State orderNote: string = '';
  // ===== 采购单面板 =====
  @State ordersSheetVisible: boolean = false;
  @State orders: MarketOrder[] = [];

  aboutToAppear(): void {
    if (this.vm.items.length === 0 && !this.vm.loading) {
      this.reload();
    }
    this.vm.loadOrders().then(() => {
      this.orders = this.vm.orders;
    });
  }

  private reload(): void {
    this.loading = true;
    this.errorText = '';
    this.vm.load().then(() => {
      this.syncFromVm();
      this.refreshing = false;
    });
  }

  /** VM 视图数据 → @State 镜像（数组整体替换触发 ForEach 刷新）*/
  private syncFromVm(): void {
    this.loading = this.vm.loading;
    this.errorText = this.vm.errorText;
    this.categories = this.vm.categories;
    this.origins = this.vm.origins;
    this.alerts = this.vm.alerts;
    this.totalCountText = this.vm.totalCountText;
    this.updateTimeText = this.vm.updateTimeText;
    this.avgPriceText = this.vm.avgPriceText;
    this.priceChangeText = this.vm.priceChangeText;
    this.priceChangeUp = this.vm.priceChangeUp;
    this.activeCountText = this.vm.activeCountText;
    this.alertCountText = this.vm.alertCountText;
    this.rebuildRows();
  }

  /** 过滤/排序条件变化后重建两列分组 */
  private rebuildRows(): void {
    const rows: MarketItem[][] = [];
    const list = this.vm.displayItems;
    for (let i = 0; i < list.length; i += 2) {
      rows.push(list.slice(i, i + 2));
    }
    this.rows = rows;
  }

  private applyAndRefresh(): void {
    this.vm.applyFilters();
    this.rebuildRows();
  }

  private toast(message: string): void {
    this.getUIContext().getPromptAction().showToast({ message: message, duration: 2000 });
  }

  private openDetail(item: MarketItem): void {
    this.getUIContext().getRouter().pushUrl({
      url: 'pages/ForecastPage',
      params: {
        speciesId: item.speciesId,
        speciesName: item.name,
        originGrade: item.originGrade,
        priceText: item.priceText,
        changeText: item.changeText,
        changeUp: item.changeUp,
        emoji: item.emoji,
        gradStart: item.gradStart,
        gradEnd: item.gradEnd
      }
    });
  }

  // ===== 下单面板 =====

  /** 打开下单面板（数量默认 1 扎）*/
  private openOrderSheet(item: MarketItem, defaultQty: number): void {
    this.orderTarget = item;
    this.orderQty = Math.max(1, defaultQty);
    this.orderNote = '';
    this.orderSheetVisible = true;
  }

  /** 库存预警卡补货：按品种定位商品项，预填缺口数量 */
  private openRestockSheet(alert: MarketAlert): void {
    for (const it of this.vm.items) {
      if (it.speciesId === alert.speciesId) {
        this.openOrderSheet(it, alert.gap);
        return;
      }
    }
    this.toast('未找到对应品种');
  }

  private stepQty(delta: number): void {
    const next = this.orderQty + delta;
    if (next >= 1 && next <= 999) {
      this.orderQty = next;
    }
  }

  /** 单价（元/扎，从 priceText 解析）*/
  private orderUnitPrice(): number {
    if (this.orderTarget === null) {
      return 0;
    }
    const raw = Number(this.orderTarget.priceText.replace('¥', ''));
    return Number.isNaN(raw) ? 0 : raw;
  }

  private orderTotalText(): string {
    return `¥${(this.orderUnitPrice() * this.orderQty).toFixed(2)}`;
  }

  private submitOrder(): void {
    try {
      if (this.orderTarget === null) {
        this.toast('请选择商品');
        return;
      }
      const item = this.orderTarget;
      // 安全检查：确保必要字段存在
      if (!item || typeof item.speciesId !== 'number') {
        this.toast('商品数据异常');
        return;
      }
      const orderItem = marketItemToOrderItem(item, this.orderQty);
      // 二次验证转换结果
      if (!orderItem || orderItem.unitPrice <= 0 || orderItem.quantity <= 0) {
        this.toast('下单数据异常，请重试');
        return;
      }

      // 使用 AppContext 传递订单数据，避免路由参数编码问题
      const ctx = AppContext.getInstance();
      // 创建新OrderItem实例，避免引用和spread问题
      const safeItem = new OrderItem();
      safeItem.speciesId = orderItem.speciesId;
      safeItem.name = orderItem.name;
      safeItem.emoji = orderItem.emoji;
      safeItem.unitPrice = orderItem.unitPrice;
      safeItem.quantity = orderItem.quantity;
      safeItem.subtotal = orderItem.subtotal;
      ctx.pendingOrderItems = [safeItem];
      ctx.pendingOrderFromCart = false;

      // 同步获取 router（异步回调中 getUIContext 可能失效）
      const router = this.getUIContext().getRouter();

      this.orderSheetVisible = false;

      // 延迟 350ms 跳转：必须超过 sheet 关闭 transition 动画时长(220ms)，
      // 否则 sheet 撤场动画与页面转场动画冲突导致渲染崩溃闪退
      setTimeout(() => {
        try {
          router.pushUrl({
            url: 'pages/OrderConfirmPage'
          });
        } catch (e) {
          console.error(`[MarketTab] 跳转订单确认页失败: ${JSON.stringify(e)}`);
        }
      }, 350);
    } catch (e) {
      console.error(`[MarketTab] submitOrder 异常: ${JSON.stringify(e)}`);
      this.toast('下单失败，请重试');
    }
  }

  /** 加入购物车 */
  private addToCart(): void {
    if (this.orderTarget === null) {
      return;
    }
    const item = this.orderTarget;
    this.cartVm.addToCart(item, this.orderQty).then(() => {
      this.orderSheetVisible = false;
      this.toast(`已加入购物车：${item.name} × ${this.orderQty}扎`);
    });
  }

  /** 跳转到购物车 */
  private goToCart(): void {
    this.getUIContext().getRouter().pushUrl({
      url: 'pages/CartPage'
    });
  }

  /** 跳转到订单列表 */
  private goToOrders(): void {
    this.getUIContext().getRouter().pushUrl({
      url: 'pages/OrderListPage'
    });
  }

  // ===== 采购单面板 =====

  private removeOrder(orderId: string): void {
    this.vm.removeOrder(orderId).then(() => {
      this.orders = this.vm.orders;
    });
  }

  private clearAllOrders(): void {
    this.vm.clearOrders().then(() => {
      this.orders = this.vm.orders;
      this.toast('采购单已清空');
    });
  }

  /** 采购单合计金额 */
  private ordersTotalText(): string {
    let total = 0;
    for (const o of this.orders) {
      total += o.totalPrice;
    }
    return `¥${total.toFixed(2)}`;
  }

  /** ISO 时间 → "MM-DD HH:mm" */
  private formatOrderTime(iso: string): string {
    const t = Date.parse(iso);
    if (Number.isNaN(t)) {
      return '';
    }
    const d = new Date(t);
    const pad = (n: number): string => `${n < 10 ? '0' : ''}${n}`;
    return `${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
  }

  /** 采购单项明细文本（空值保护，UI DSL 内禁止 toFixed） */
  private orderRowDetailText(o: MarketOrder): string {
    const price = o && typeof o.unitPrice === 'number' ? o.unitPrice : 0;
    const qty = o && typeof o.quantity === 'number' ? o.quantity : 0;
    const time = o ? this.formatOrderTime(o.createTime) : '';
    return `¥${price.toFixed(2)} × ${qty} 扎 · ${time}`;
  }

  /** 采购单项总额文本（空值保护） */
  private orderRowTotalText(o: MarketOrder): string {
    const total = o && typeof o.totalPrice === 'number' ? o.totalPrice : 0;
    return `¥${total.toFixed(2)}`;
  }

  /** 采购单项表情（空值保护） */
  private orderRowEmoji(o: MarketOrder): string {
    return o && typeof o.emoji === 'string' && o.emoji.length > 0 ? o.emoji : '🌸';
  }

  /** 采购单项名称（空值保护） */
  private orderRowName(o: MarketOrder): string {
    return o && typeof o.name === 'string' && o.name.length > 0 ? o.name : '花卉商品';
  }

  /** 筛选条胶囊按钮 */
  @Builder
  FilterChip(label: string, menu: MenuElement[]) {
    Row({ space: 5 }) {
      Text(label)
        .fontSize(13)
        .fontColor(AppTheme.text)
        .maxLines(1)
        .textOverflow({ overflow: TextOverflow.Ellipsis })
      Text('▾')
        .fontSize(12)
        .fontColor(AppTheme.textSecondary)
    }
    .justifyContent(FlexAlign.Center)
    .layoutWeight(1)
    .height(44)
    .borderRadius(16)
    .backgroundColor(AppTheme.card)
    .border({ width: 1, color: AppTheme.divider })
    .bindMenu(menu)
  }

  build() {
    Stack({ alignContent: Alignment.Bottom }) {
      Column() {
      // ===== 顶部搜索栏（磨砂玻璃）=====
      Row({ space: 10 }) {
        Row({ space: 8 }) {
          Text('🔍').fontSize(14)
          TextInput({ placeholder: '搜索花卉品种、产地...', text: this.searchText })
            .placeholderColor(AppTheme.textSecondary)
            .placeholderFont({ size: 13 })
            .fontSize(13)
            .fontColor(AppTheme.text)
            .backgroundColor(Color.Transparent)
            .padding(0)
            .layoutWeight(1)
            .onChange((value: string) => {
              this.searchText = value;
              this.vm.searchText = value;
              this.applyAndRefresh();
            })
        }
        .layoutWeight(1)
        .height(44)
        .borderRadius(999)
        .backgroundColor(AppTheme.sheet)
        .padding({ left: 14, right: 14 })
        .alignItems(VerticalAlign.Center)

        // 购物车入口（数量徽标）
        Stack({ alignContent: Alignment.TopEnd }) {
          Button('🛒')
            .type(ButtonType.Normal)
            .width(44)
            .height(44)
            .borderRadius(999)
            .backgroundColor(AppTheme.sheet)
            .fontSize(17)
            .onClick(() => this.goToCart())
          if (this.cartVm.totalCount > 0) {
            Text(`${this.cartVm.totalCount > 99 ? '99+' : this.cartVm.totalCount}`)
              .fontSize(9)
              .fontColor(Color.White)
              .fontWeight(FontWeight.Medium)
              .fontFamily(AppTheme.fontMono)
              .backgroundColor(AppTheme.badge)
              .borderRadius(999)
              .constraintSize({ minWidth: 15 })
              .padding({ left: 4, right: 4, top: 1, bottom: 1 })
              .margin({ top: 4, right: 2 })
          }
        }

        // 订单入口
        Stack({ alignContent: Alignment.TopEnd }) {
          Button('📋')
            .type(ButtonType.Normal)
            .width(44)
            .height(44)
            .borderRadius(999)
            .backgroundColor(AppTheme.sheet)
            .fontSize(17)
            .onClick(() => this.goToOrders())
        }

        Stack({ alignContent: Alignment.TopEnd }) {
          Button('🔔')
            .type(ButtonType.Normal)
            .width(44)
            .height(44)
            .borderRadius(999)
            .backgroundColor(AppTheme.sheet)
            .fontSize(17)
            .onClick(() => this.toast(this.alerts.length > 0
              ? `当前 ${this.alerts.length} 条库存预警` : '暂无新通知'))
          if (this.alerts.length > 0) {
            Circle({ width: 8, height: 8 })
              .fill(AppTheme.badge)
              .margin({ top: 8, right: 8 })
          }
        }
      }
      .width('100%')
      .padding({ left: 16, right: 16, top: 12, bottom: 12 })
      .backgroundColor('rgba(19,23,34,0.72)')
      .backdropBlur(20)

      Refresh({ refreshing: $$this.refreshing }) {
        Scroll() {
          Column() {
          // ===== 市场统计概览（四宫格）=====
          Row({ space: 8 }) {
            Column({ space: 4 }) {
              Text('市场均价')
                .fontSize(10)
                .fontColor(AppTheme.textSecondary)
              Text(this.avgPriceText)
                .fontSize(15)
                .fontWeight(FontWeight.Bold)
                .fontColor(AppTheme.text)
                .fontFamily(AppTheme.fontMono)
            }
            .layoutWeight(1)
            .alignItems(HorizontalAlign.Start)

            Column({ space: 4 }) {
              Text('整体涨跌')
                .fontSize(10)
                .fontColor(AppTheme.textSecondary)
              Text(this.priceChangeText)
                .fontSize(15)
                .fontWeight(FontWeight.Bold)
                .fontColor(this.priceChangeUp ? AppTheme.up : AppTheme.down)
                .fontFamily(AppTheme.fontMono)
            }
            .layoutWeight(1)
            .alignItems(HorizontalAlign.Start)

            Column({ space: 4 }) {
              Text('在售品种')
                .fontSize(10)
                .fontColor(AppTheme.textSecondary)
              Text(this.activeCountText)
                .fontSize(15)
                .fontWeight(FontWeight.Bold)
                .fontColor(AppTheme.text)
                .fontFamily(AppTheme.fontMono)
            }
            .layoutWeight(1)
            .alignItems(HorizontalAlign.Start)

            Column({ space: 4 }) {
              Text('价格预警')
                .fontSize(10)
                .fontColor(AppTheme.textSecondary)
              Text(this.alertCountText)
                .fontSize(15)
                .fontWeight(FontWeight.Bold)
                .fontColor(Number(this.alertCountText) > 0 ? AppTheme.warn : AppTheme.text)
                .fontFamily(AppTheme.fontMono)
            }
            .layoutWeight(1)
            .alignItems(HorizontalAlign.Start)
          }
          .width('100%')
          .padding({ left: 14, right: 14, top: 12, bottom: 12 })
          .borderRadius(20)
          .backgroundColor(AppTheme.card)
          .border({ width: 1, color: AppTheme.divider })
          .margin({ left: 16, right: 16, top: 12 })

          // ===== 分类胶囊（横向滚动）=====
          Scroll() {
            Row({ space: 8 }) {
              ForEach(this.categories, (c: string) => {
                Text(c)
                  .fontSize(13)
                  .fontColor(c === this.activeCategory ? Color.White : AppTheme.textSecondary)
                  .fontWeight(c === this.activeCategory ? FontWeight.Medium : FontWeight.Normal)
                  .height(36)
                  .padding({ left: 16, right: 16 })
                  .borderRadius(999)
                  .backgroundColor(c === this.activeCategory ? AppTheme.primary : AppTheme.sheet)
                  .onClick(() => {
                    this.activeCategory = c;
                    this.vm.activeCategory = c;
                    this.applyAndRefresh();
                  })
              }, (c: string) => c)
            }
            .padding({ left: 16, right: 16 })
          }
          .scrollable(ScrollDirection.Horizontal)
          .scrollBar(BarState.Off)
          .width('100%')
          .margin({ top: 12, bottom: 12 })

          // ===== 筛选条：品种/价格/产地/排序 =====
          Row({ space: 8 }) {
            this.FilterChip(
              this.activeCategory === '全部' ? '品种' : this.activeCategory,
              this.categories.map((c: string): MenuElement => ({
                value: c,
                action: () => {
                  this.activeCategory = c;
                  this.vm.activeCategory = c;
                  this.applyAndRefresh();
                }
              })))

            this.FilterChip(PRICE_LABELS[this.priceRange],
              PRICE_LABELS.map((label: string, idx: number): MenuElement => ({
                value: idx === 0 ? '不限' : label,
                action: () => {
                  this.priceRange = idx;
                  this.vm.priceRange = idx;
                  this.applyAndRefresh();
                }
              })))

            this.FilterChip(
              this.originFilter === '全部' ? '产地' : this.originFilter,
              this.origins.map((o: string): MenuElement => ({
                value: o,
                action: () => {
                  this.originFilter = o;
                  this.vm.originFilter = o;
                  this.applyAndRefresh();
                }
              })))

            this.FilterChip(SORT_LABELS[this.sortMode],
              SORT_LABELS.map((label: string, idx: number): MenuElement => ({
                value: idx === 0 ? '默认' : label,
                action: () => {
                  this.sortMode = idx;
                  this.vm.sortMode = idx;
                  this.applyAndRefresh();
                }
              })))
          }
          .width('100%')
          .padding({ left: 16, right: 16 })

          // ===== 内容区 =====
          if (this.loading) {
            LoadingView({ message: '加载市场数据…' })
              .margin({ top: 120 })
          }
          else if (this.errorText.length > 0) {
            ErrorView({
              message: this.errorText,
              onRetry: () => {
                this.reload();
              }
            })
              .margin({ top: 120 })
          }
          else {
            // ===== 实时库存提醒（横滑卡片）=====
            if (this.alerts.length > 0) {
              Column() {
                Row() {
                  Row({ space: 7 }) {
                    Text('🔔').fontSize(14)
                    Text('实时库存提醒')
                      .fontSize(15)
                      .fontWeight(FontWeight.Bold)
                      .fontColor(AppTheme.text)
                  }
                  .alignItems(VerticalAlign.Center)

                  Blank()
                  Row({ space: 2 }) {
                    Text('全部')
                      .fontSize(11)
                      .fontColor(AppTheme.textSecondary)
                    Text('›')
                      .fontSize(13)
                      .fontColor(AppTheme.textSecondary)
                  }
                  .alignItems(VerticalAlign.Center)
                  .onClick(() => this.toast(`共 ${this.alerts.length} 条预警`))
                }
                .width('100%')
                .padding({ left: 16, right: 16, top: 16, bottom: 10 })

                Scroll() {
                  Row({ space: 12 }) {
                    ForEach(this.alerts, (a: MarketAlert) => {
                      AlertCard({
                        alert: a,
                        onRestock: (alert: MarketAlert) => {
                          this.openRestockSheet(alert);
                        }
                      })
                    }, (a: MarketAlert) => `${a.speciesId}-${a.level}`)
                  }
                  .padding({ left: 16, right: 16 })
                }
                .scrollable(ScrollDirection.Horizontal)
                .scrollBar(BarState.Off)
                .width('100%')
              }
              .width('100%')
            }

            // ===== 热门花卉（两列商品网格）=====
            Column() {
              Row() {
                Text('热门花卉')
                  .fontSize(15)
                  .fontWeight(FontWeight.Bold)
                  .fontColor(AppTheme.text)
                Blank()
                Text(this.updateTimeText.length > 0
                  ? `${this.totalCountText} · ${this.updateTimeText}` : this.totalCountText)
                  .fontSize(11)
                  .fontColor(AppTheme.textSecondary)
              }
              .width('100%')
              .padding({ left: 16, right: 16, top: 18, bottom: 10 })

              if (this.rows.length === 0) {
                EmptyView({ message: '没有符合条件的品种' })
                  .margin({ top: 60, bottom: 60 })
              }
              else {
                Column({ space: 12 }) {
                  ForEach(this.rows, (row: MarketItem[]) => {
                    Row({ space: 12 }) {
                      ForEach(row, (item: MarketItem) => {
                        GoodsCard({
                          item: item,
                          onOpen: (it: MarketItem) => {
                            this.openDetail(it);
                          },
                          onOrder: (it: MarketItem) => {
                            this.openOrderSheet(it, 1);
                          }
                        })
                          .layoutWeight(1)
                      }, (item: MarketItem) => `g-${item.speciesId}`)
                    }
                    .width('100%')
                    .alignItems(VerticalAlign.Top)
                  }, (row: MarketItem[], idx: number) => `row-${idx}`)
                }
                .padding({ left: 16, right: 16, bottom: 24 })
              }
            }
            .width('100%')
          }
        }
        .width('100%')
        .alignItems(HorizontalAlign.Start)
        }
        .width('100%')
        .height('100%')
        .align(Alignment.Top)
        .scrollBar(BarState.Off)
        .linearGradient({ angle: AppTheme.skyAngle, colors: AppTheme.skyColors })
        .edgeEffect(EdgeEffect.Spring)
      }
      .layoutWeight(1)
      .width('100%')
      .onRefreshing(() => {
        this.reload();
      })
      }
      .width('100%')
      .height('100%')
      .linearGradient({ angle: AppTheme.skyAngle, colors: AppTheme.skyColors })

      // ===== 下单面板覆盖层（TabContent 内 bindSheet 不弹出，改用 Stack 底部面板）=====
      if (this.orderSheetVisible) {
        Stack({ alignContent: Alignment.Bottom }) {
          Column()
            .width('100%')
            .height('100%')
            .backgroundColor('rgba(0,0,0,0.55)')
            .onClick(() => {
              this.orderSheetVisible = false;
            })
          Column() {
            this.orderSheetBuilder()
          }
          .width('100%')
          .backgroundColor(AppTheme.sheet)
          .border({ width: { top: 1 }, color: AppTheme.cardBorder })
          .borderRadius({ topLeft: 24, topRight: 24 })
          .padding({ bottom: 72 })
          .transition(TransitionEffect.translate({ y: 60 }).animation({ duration: 220 }))
        }
        .width('100%')
        .height('100%')
      }

      // ===== 采购单面板覆盖层 =====
      if (this.ordersSheetVisible) {
        Stack({ alignContent: Alignment.Bottom }) {
          Column()
            .width('100%')
            .height('100%')
            .backgroundColor('rgba(0,0,0,0.55)')
            .onClick(() => {
              this.ordersSheetVisible = false;
            })
          Column() {
            this.ordersSheetBuilder()
          }
          .width('100%')
          .height('78%')
          .backgroundColor(AppTheme.sheet)
          .border({ width: { top: 1 }, color: AppTheme.cardBorder })
          .borderRadius({ topLeft: 24, topRight: 24 })
          .transition(TransitionEffect.translate({ y: 60 }).animation({ duration: 220 }))
        }
        .width('100%')
        .height('100%')
      }
    }
    .width('100%')
    .height('100%')
  }

  // ===== 下单面板（bindSheet 内容）=====
  @Builder
  orderSheetBuilder() {
    Column({ space: 0 }) {
      Column()
        .width(36)
        .height(4)
        .borderRadius(2)
        .backgroundColor(AppTheme.divider)
        .margin({ top: 10, bottom: 14 })

      if (this.orderTarget !== null) {
        // 品种摘要
        Row({ space: 12 }) {
          Column() {
            Text(this.orderTarget.emoji)
              .fontSize(30)
          }
          .width(56)
          .height(56)
          .borderRadius(16)
          .linearGradient({
            angle: 135,
            colors: [[this.orderTarget.gradStart, 0], [this.orderTarget.gradEnd, 1]]
          })
          .alignItems(HorizontalAlign.Center)
          .justifyContent(FlexAlign.Center)

          Column({ space: 3 }) {
            Text(this.orderTarget.name)
              .fontSize(16)
              .fontWeight(FontWeight.Bold)
              .fontColor(AppTheme.text)
            Text(this.orderTarget.originGrade)
              .fontSize(11)
              .fontColor(AppTheme.textSecondary)
          }
          .alignItems(HorizontalAlign.Start)
          .layoutWeight(1)

          Column({ space: 2 }) {
            Text(this.orderTarget.priceText)
              .fontSize(18)
              .fontWeight(FontWeight.Bold)
              .fontColor(AppTheme.text)
              .fontFamily(AppTheme.fontMono)
            Text('元/扎')
              .fontSize(10)
              .fontColor(AppTheme.textSecondary)
          }
          .alignItems(HorizontalAlign.End)
        }
        .width('100%')
        .padding({ left: 20, right: 20 })
        .margin({ bottom: 16 })

        // 数量步进
        Row() {
          Text('采购数量（扎）')
            .fontSize(14)
            .fontColor(AppTheme.text)
          Blank()
          Row({ space: 0 }) {
            Text('−')
              .fontSize(20)
              .fontColor(AppTheme.text)
              .width(42)
              .height(42)
              .textAlign(TextAlign.Center)
              .backgroundColor(AppTheme.sheet)
              .borderRadius({ topLeft: 12, bottomLeft: 12 })
              .onClick(() => this.stepQty(-1))
            Text(`${this.orderQty}`)
              .fontSize(16)
              .fontWeight(FontWeight.Bold)
              .fontColor(AppTheme.text)
              .fontFamily(AppTheme.fontMono)
              .width(84)
              .height(42)
              .textAlign(TextAlign.Center)
              .backgroundColor(AppTheme.card)
              .border({ width: { top: 1, bottom: 1 }, color: AppTheme.divider })
            Text('+')
              .fontSize(20)
              .fontColor(AppTheme.text)
              .width(42)
              .height(42)
              .textAlign(TextAlign.Center)
              .backgroundColor(AppTheme.sheet)
              .borderRadius({ topRight: 12, bottomRight: 12 })
              .onClick(() => this.stepQty(1))
          }
          .height(42)
        }
        .width('100%')
        .padding({ left: 20, right: 20 })
        .margin({ bottom: 14 })

        // 备注
        TextInput({ placeholder: '备注（选填，如：明日送达）', text: this.orderNote })
          .placeholderColor(AppTheme.textSecondary)
          .placeholderFont({ size: 13 })
          .fontSize(13)
          .fontColor(AppTheme.text)
          .backgroundColor(AppTheme.card)
          .borderRadius(12)
          .height(44)
          .margin({ left: 20, right: 20, bottom: 14 })
          .onChange((value: string) => {
            this.orderNote = value;
          })

        // 合计
        Row() {
          Text('合计金额')
            .fontSize(13)
            .fontColor(AppTheme.textSecondary)
          Blank()
          Text(this.orderTotalText())
            .fontSize(24)
            .fontWeight(FontWeight.Bold)
            .fontColor(AppTheme.text)
            .fontFamily(AppTheme.fontMono)
        }
        .width('100%')
        .padding({ left: 20, right: 20 })
        .margin({ bottom: 18 })

        // 操作按钮：加入购物车 + 立即购买
        Row({ space: 12 }) {
          Button('加入购物车')
            .type(ButtonType.Normal)
            .layoutWeight(1)
            .height(48)
            .borderRadius(999)
            .backgroundColor(AppTheme.card)
            .fontColor(AppTheme.text)
            .fontSize(15)
            .fontWeight(FontWeight.Medium)
            .border({ width: 1, color: AppTheme.divider })
            .onClick(() => this.addToCart())

          Button('立即购买')
            .type(ButtonType.Normal)
            .layoutWeight(1)
            .height(48)
            .borderRadius(999)
            .backgroundColor(AppTheme.primary)
            .fontColor(Color.White)
            .fontSize(15)
            .fontWeight(FontWeight.Medium)
            .onClick(() => this.submitOrder())
        }
        .width('100%')
        .padding({ left: 20, right: 20, bottom: 20 })
      }
    }
    .width('100%')
    .alignItems(HorizontalAlign.Start)
  }

  // ===== 采购单面板（bindSheet 内容）=====
  @Builder
  ordersSheetBuilder() {
    Column() {
      Column()
        .width(36)
        .height(4)
        .borderRadius(2)
        .backgroundColor(AppTheme.divider)
        .margin({ top: 10, bottom: 14 })

      Row() {
        Text(`我的采购单（${this.orders.length}）`)
          .fontSize(16)
          .fontWeight(FontWeight.Bold)
          .fontColor(AppTheme.text)
        Blank()
        if (this.orders.length > 0) {
          Text('清空')
            .fontSize(13)
            .fontColor(AppTheme.down)
            .padding({ left: 8, right: 8, top: 4, bottom: 4 })
            .onClick(() => this.clearAllOrders())
        }
      }
      .width('100%')
      .padding({ left: 20, right: 20 })
      .margin({ bottom: 12 })

      if (this.orders.length === 0) {
        EmptyView({ message: '暂无采购单，去市场下单一笔吧' })
          .margin({ top: 40, bottom: 60 })
      }
      else {
        Scroll() {
          Column({ space: 10 }) {
            ForEach(this.orders, (o: MarketOrder) => {
              Row({ space: 12 }) {
                Column() {
                  Text(this.orderRowEmoji(o))
                    .fontSize(22)
                }
                .width(44)
                .height(44)
                .borderRadius(12)
                .backgroundColor(AppTheme.sheet)
                .alignItems(HorizontalAlign.Center)
                .justifyContent(FlexAlign.Center)

                Column({ space: 3 }) {
                  Text(this.orderRowName(o))
                    .fontSize(14)
                    .fontWeight(FontWeight.Medium)
                    .fontColor(AppTheme.text)
                    .maxLines(1)
                    .textOverflow({ overflow: TextOverflow.Ellipsis })
                  Text(this.orderRowDetailText(o))
                    .fontSize(11)
                    .fontColor(AppTheme.textSecondary)
                    .fontFamily(AppTheme.fontMono)
                }
                .alignItems(HorizontalAlign.Start)
                .layoutWeight(1)

                Column({ space: 4 }) {
                  Text(this.orderRowTotalText(o))
                    .fontSize(15)
                    .fontWeight(FontWeight.Bold)
                    .fontColor(AppTheme.text)
                    .fontFamily(AppTheme.fontMono)
                  Text('✕')
                    .fontSize(13)
                    .fontColor(AppTheme.textSecondary)
                    .onClick(() => this.removeOrder(o.orderId))
                }
                .alignItems(HorizontalAlign.End)
              }
              .width('100%')
              .padding(12)
              .borderRadius(16)
              .backgroundColor(AppTheme.card)
              .border({ width: 1, color: AppTheme.divider })
            }, (o: MarketOrder) => o.orderId)
          }
          .padding({ left: 20, right: 20, bottom: 12 })
        }
        .layoutWeight(1)
        .scrollBar(BarState.Off)
        .width('100%')

        // 合计栏
        Row() {
          Text('合计')
            .fontSize(13)
            .fontColor(AppTheme.textSecondary)
          Blank()
          Text(this.ordersTotalText())
            .fontSize(20)
            .fontWeight(FontWeight.Bold)
            .fontColor(AppTheme.text)
            .fontFamily(AppTheme.fontMono)
        }
        .width('100%')
        .padding({ left: 20, right: 20, top: 12, bottom: 24 })
      }
    }
    .width('100%')
    .height('100%')
  }
}
