import { AppTheme } from "../../theme/AppTheme";
import { LoadingView, ErrorView, EmptyView, SectionTitle, StatBadge } from "../../components/CommonWidgets";
import { HomeViewModel, HomeTrendItem, HomeTxItem, HomeRegionItem } from "../../viewmodels/FlowerViewModels";

/** 热点品种迷你卡（渐变头图 + 名称 + 价格/涨跌）*/
@Component
struct TrendCard {
  @Prop item: HomeTrendItem;
  onOpen: (item: HomeTrendItem) => void = () => {
  };

  build() {
    Column() {
      Stack({ alignContent: Alignment.Center }) {
        Column()
          .width('100%')
          .height('100%')
          .linearGradient({
            angle: 135,
            colors: [[this.item.gradStart, 0], [this.item.gradEnd, 1]]
          })
        Text(this.item.emoji)
          .fontSize(34)
          .opacity(0.95)
      }
      .width('100%')
      .height(84)

      Column({ space: 4 }) {
        Text(this.item.name)
          .fontSize(13)
          .fontWeight(FontWeight.Bold)
          .fontColor(AppTheme.text)
          .maxLines(1)
          .textOverflow({ overflow: TextOverflow.Ellipsis })
          .width('100%')

        Row() {
          Text(this.item.priceText)
            .fontSize(15)
            .fontWeight(FontWeight.Bold)
            .fontColor(AppTheme.text)
            .fontFamily(AppTheme.fontMono)
          Blank()
          Text(this.item.changeText)
            .fontSize(11)
            .fontFamily(AppTheme.fontMono)
            .fontColor(this.item.changeUp ? AppTheme.up : AppTheme.down)
        }
        .width('100%')
        .alignItems(VerticalAlign.Bottom)
      }
      .padding(10)
      .alignItems(HorizontalAlign.Start)
    }
    .width(128)
    .borderRadius(16)
    .backgroundColor(AppTheme.card)
    .border({ width: 1, color: AppTheme.divider })
    .clip(true)
    .onClick(() => this.onOpen(this.item))
  }
}

/** 核心指标卡（渐变图标圆 + 标签 + 等宽数值）*/
@Component
struct MetricCard {
  @Prop emoji: string = '';
  @Prop label: string = '';
  @Prop value: string = '';
  @Prop gradStart: string = '#3b82f6';
  @Prop gradEnd: string = '#1d4ed8';

  build() {
    Row({ space: 12 }) {
      Stack({ alignContent: Alignment.Center }) {
        Text(this.emoji)
          .fontSize(18)
      }
      .width(40)
      .height(40)
      .borderRadius(20)
      .linearGradient({
        angle: 135,
        colors: [[this.gradStart, 0], [this.gradEnd, 1]]
      })

      Column({ space: 3 }) {
        Text(this.label)
          .fontSize(AppTheme.fontCaption)
          .fontColor(AppTheme.textSecondary)
        Text(this.value)
          .fontSize(19)
          .fontWeight(FontWeight.Bold)
          .fontColor(AppTheme.text)
          .fontFamily(AppTheme.fontMono)
      }
      .alignItems(HorizontalAlign.Start)
      .layoutWeight(1)
    }
    .width('100%')
    .padding(AppTheme.spaceLg)
    .backgroundColor(AppTheme.card)
    .borderRadius(AppTheme.radiusCard)
    .border({ width: 1, color: AppTheme.divider })
    .alignItems(VerticalAlign.Center)
  }
}

@Component
export struct HomeTab {
  @State vm: HomeViewModel = new HomeViewModel();
  @State refreshing: boolean = false;

  aboutToAppear(): void {
    if (this.vm.transactions.length === 0 && this.vm.trends.length === 0) {
      this.vm.load();
    }
  }

  /** 跨 Tab 跳转（Index 的 currentTab 经 @StorageProp 与 AppStorage 单向同步）*/
  private jumpTab(index: number): void {
    AppStorage.setOrCreate('currentTab', index);
  }

  /** 热点品种 → 品种详情/预测页（参数与 MarketTab.openDetail 对齐）*/
  private openTrend(item: HomeTrendItem): void {
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

  @Builder HeroBanner() {
    Stack({ alignContent: Alignment.TopStart }) {
      // 春色光晕 + 金色微光（低透明度装饰圆）
      Circle({ width: 190, height: 190 })
        .fill('rgba(134,239,172,0.10)')
        .blur(28)
        .position({ x: 200, y: -70 })
      Circle({ width: 160, height: 160 })
        .fill('rgba(255,206,84,0.12)')
        .blur(26)
        .position({ x: -50, y: 100 })
      Circle({ width: 90, height: 90 })
        .fill('rgba(125,211,252,0.10)')
        .blur(20)
        .position({ x: 300, y: 130 })

      Column() {
        Row() {
          Column({ space: 3 }) {
            Text(`${this.vm.greetingText}，${this.vm.titleName}`)
              .fontSize(12)
              .fontColor('rgba(255,255,255,0.65)')
            Text('耕地 · 斗南鲜花行情')
              .fontSize(21)
              .fontWeight(FontWeight.Bold)
              .fontColor('#ffffff')
          }
          .alignItems(HorizontalAlign.Start)
          .layoutWeight(1)

          Text(this.vm.dateText)
            .fontSize(11)
            .fontColor('rgba(255,255,255,0.60)')
            .padding({ left: 10, right: 10, top: 5, bottom: 5 })
            .borderRadius(999)
            .backgroundColor('rgba(255,255,255,0.10)')
        }
        .width('100%')
        .alignItems(VerticalAlign.Top)

        Row() {
          Column({ space: 4 }) {
            Text('今日鲜花均价')
              .fontSize(11)
              .fontColor('rgba(255,255,255,0.60)')
            Row({ space: 8 }) {
              Text(this.vm.avgPriceText)
                .fontSize(30)
                .fontWeight(FontWeight.Bold)
                .fontColor('#ffffff')
                .fontFamily(AppTheme.fontMono)
              Text(this.vm.priceChangeText)
                .fontSize(12)
                .fontWeight(FontWeight.Medium)
                .fontFamily(AppTheme.fontMono)
                .fontColor(this.vm.priceChangeUp ? AppTheme.up : AppTheme.down)
                .padding({ left: 7, right: 7, top: 3, bottom: 3 })
                .borderRadius(6)
                .backgroundColor(this.vm.priceChangeUp ? 'rgba(38,181,160,0.22)' : 'rgba(255,107,107,0.22)')
            }
            .alignItems(VerticalAlign.Bottom)
          }
          .alignItems(HorizontalAlign.Start)

          Blank()

          Column({ space: 4 }) {
            Text('在售品种')
              .fontSize(10)
              .fontColor('rgba(255,255,255,0.55)')
            Row({ space: 3 }) {
              Text(this.vm.activeCountText)
                .fontSize(20)
                .fontWeight(FontWeight.Bold)
                .fontColor('#ffffff')
                .fontFamily(AppTheme.fontMono)
              Text('种')
                .fontSize(11)
                .fontColor('rgba(255,255,255,0.55)')
            }
            .alignItems(VerticalAlign.Bottom)
          }
          .alignItems(HorizontalAlign.End)
        }
        .width('100%')
        .margin({ top: 20 })
        .alignItems(VerticalAlign.Bottom)

        Text(`${this.vm.alertCountText} 个品种价格波动预警 · 数据更新于 ${this.vm.lastUpdateText.length > 0 ? this.vm.lastUpdateText : '—'}`)
          .fontSize(11)
          .fontColor('rgba(255,255,255,0.55)')
          .margin({ top: 12 })

        Row({ space: 10 }) {
          Row({ space: 6 }) {
            Text('🛒').fontSize(14)
            Text('去采购').fontSize(13).fontWeight(FontWeight.Medium).fontColor('#ffffff')
          }
          .padding({ left: 18, right: 18, top: 9, bottom: 9 })
          .borderRadius(999)
          .backgroundColor(AppTheme.primary)
          .onClick(() => this.jumpTab(2))

          Row({ space: 6 }) {
            Text('🌤').fontSize(14)
            Text('看天气').fontSize(13).fontWeight(FontWeight.Medium).fontColor(AppTheme.text)
          }
          .padding({ left: 18, right: 18, top: 9, bottom: 9 })
          .borderRadius(999)
          .border({ width: 1, color: 'rgba(255,255,255,0.25)' })
          .onClick(() => this.jumpTab(1))
        }
        .margin({ top: 16 })
      }
      .width('100%')
      .padding(20)
      .alignItems(HorizontalAlign.Start)
    }
    .width('100%')
    .borderRadius(24)
    .clip(true)
    .linearGradient({
      angle: 140,
      colors: [['#242c3e', 0], ['#1d2434', 0.55], ['#171d2b', 1]]
    })
    .border({ width: 1, color: 'rgba(255,255,255,0.08)' })
  }

  build() {
    Refresh({ refreshing: $$this.refreshing }) {
      Scroll() {
        Column({ space: AppTheme.spaceMd }) {
          this.HeroBanner()

          if (this.vm.loading) {
            LoadingView({ message: '加载行情数据…' })
          }
          else if (this.vm.errorText.length > 0) {
            ErrorView({
              message: this.vm.errorText,
              onRetry: () => {
                this.vm.load();
              }
            })
          }
          else {
            // ===== 核心指标 2×2 =====
            Row({ space: AppTheme.spaceMd }) {
              MetricCard({
                emoji: '💰',
                label: '今日交易额',
                value: this.vm.tradeAmountText,
                gradStart: '#f59e0b',
                gradEnd: '#b45309'
              })
              MetricCard({
                emoji: '🧾',
                label: '交易笔数',
                value: `${this.vm.stats.tradeCount}`,
                gradStart: '#3b82f6',
                gradEnd: '#1d4ed8'
              })
            }
            .width('100%')

            Row({ space: AppTheme.spaceMd }) {
              MetricCard({
                emoji: '🌸',
                label: '活跃品种',
                value: `${this.vm.stats.activeSpeciesCount}`,
                gradStart: '#ec4899',
                gradEnd: '#be185d'
              })
              MetricCard({
                emoji: '🏪',
                label: '在线商家',
                value: `${this.vm.stats.onlineMerchantCount}`,
                gradStart: '#14b8a6',
                gradEnd: '#0f766e'
              })
            }
            .width('100%')

            // ===== 今日热点品种（横向滚动）=====
            if (this.vm.trends.length > 0) {
              Column({ space: AppTheme.spaceMd }) {
                SectionTitle({
                  title: '今日热点品种',
                  actionText: '更多行情 ›',
                  onAction: () => this.jumpTab(2)
                })
                Scroll() {
                  Row({ space: AppTheme.spaceMd }) {
                    ForEach(this.vm.trends, (t: HomeTrendItem) => {
                      TrendCard({
                        item: t,
                        onOpen: (it: HomeTrendItem) => this.openTrend(it)
                      })
                    }, (t: HomeTrendItem) => `${t.speciesId}`)
                  }
                }
                .scrollable(ScrollDirection.Horizontal)
                .scrollBar(BarState.Off)
                .edgeEffect(EdgeEffect.Spring)
              }
              .width('100%')
              .alignItems(HorizontalAlign.Start)
            }

            // ===== 最近交易 =====
            SectionTitle({ title: '最近交易' })
            Column() {
              if (this.vm.txRows.length === 0) {
                EmptyView({ message: '暂无交易记录' })
              }
              ForEach(this.vm.txRows, (r: HomeTxItem, idx: number) => {
                Column() {
                  Row({ space: 12 }) {
                    Stack({ alignContent: Alignment.Center }) {
                      Text(r.emoji)
                        .fontSize(16)
                    }
                    .width(36)
                    .height(36)
                    .borderRadius(18)
                    .linearGradient({
                      angle: 135,
                      colors: [[r.gradStart, 0], [r.gradEnd, 1]]
                    })

                    Column({ space: 3 }) {
                      Text(r.name)
                        .fontSize(14)
                        .fontWeight(FontWeight.Medium)
                        .fontColor(AppTheme.text)
                      Text(`${r.market} · ${r.timeText}`)
                        .fontSize(11)
                        .fontColor(AppTheme.textSecondary)
                    }
                    .alignItems(HorizontalAlign.Start)
                    .layoutWeight(1)

                    Column({ space: 2 }) {
                      Text(r.priceText)
                        .fontSize(14)
                        .fontWeight(FontWeight.Bold)
                        .fontColor(AppTheme.text)
                        .fontFamily(AppTheme.fontMono)
                      Text(r.qtyText)
                        .fontSize(11)
                        .fontColor(AppTheme.textSecondary)
                        .fontFamily(AppTheme.fontMono)
                    }
                    .alignItems(HorizontalAlign.End)
                  }
                  .width('100%')
                  .padding({ top: 10, bottom: 10 })
                  .alignItems(VerticalAlign.Center)

                  if (idx < this.vm.txRows.length - 1) {
                    Divider()
                      .color(AppTheme.divider)
                      .margin({ left: 48 })
                  }
                }
              }, (r: HomeTxItem) => `${r.name}-${r.timeText}-${r.priceText}`)
            }
            .width('100%')
            .padding({ left: AppTheme.spaceLg, right: AppTheme.spaceLg, top: 6, bottom: 6 })
            .backgroundColor(AppTheme.card)
            .borderRadius(AppTheme.radiusCard)
            .border({ width: 1, color: AppTheme.divider })
            .alignItems(HorizontalAlign.Start)

            // ===== 区域需求指数 =====
            SectionTitle({ title: '区域需求指数' })
            Column() {
              if (this.vm.regionRows.length === 0) {
                EmptyView({ message: '暂无区域数据' })
              }
              ForEach(this.vm.regionRows, (r: HomeRegionItem, idx: number) => {
                Column() {
                  Row({ space: 10 }) {
                    Text(r.regionName)
                      .fontSize(13)
                      .fontColor(AppTheme.text)
                      .width(64)

                    Row() {
                      Row()
                        .width(`${Math.round(r.ratio * 100)}%`)
                        .height(6)
                        .borderRadius(3)
                        .linearGradient({
                          angle: 90,
                          colors: r.hot
                            ? [['#ff7043', 0], ['#ff6b6b', 1]]
                            : [[AppTheme.primary, 0], ['#26b5a0', 1]]
                        })
                    }
                    .layoutWeight(1)
                    .height(6)
                    .borderRadius(3)
                    .backgroundColor('rgba(255,255,255,0.06)')

                    StatBadge({ text: r.indexText, positive: !r.hot })
                  }
                  .width('100%')
                  .padding({ top: 9, bottom: 9 })
                  .alignItems(VerticalAlign.Center)

                  if (idx < this.vm.regionRows.length - 1) {
                    Divider()
                      .color(AppTheme.divider)
                      .margin({ left: 74 })
                  }
                }
              }, (r: HomeRegionItem) => r.regionName)
            }
            .width('100%')
            .padding({ left: AppTheme.spaceLg, right: AppTheme.spaceLg, top: 6, bottom: 6 })
            .backgroundColor(AppTheme.card)
            .borderRadius(AppTheme.radiusCard)
            .border({ width: 1, color: AppTheme.divider })
            .alignItems(HorizontalAlign.Start)
          }
        }
        .padding({ left: AppTheme.spaceLg, right: AppTheme.spaceLg, bottom: 76 })
        .alignItems(HorizontalAlign.Start)
      }
      .layoutWeight(1)
      .width('100%')
      .align(Alignment.Top)
      .scrollBar(BarState.Off)
      .linearGradient({ angle: AppTheme.skyAngle, colors: AppTheme.skyColors })
      .edgeEffect(EdgeEffect.Spring)
    }
    .onRefreshing(() => {
      this.vm.load().then(() => {
        this.refreshing = false;
      }).catch((_e: Error) => {
        this.refreshing = false;
      });
    })
    .layoutWeight(1)
    .width('100%')
    .linearGradient({ angle: AppTheme.skyAngle, colors: AppTheme.skyColors })
  }
}
