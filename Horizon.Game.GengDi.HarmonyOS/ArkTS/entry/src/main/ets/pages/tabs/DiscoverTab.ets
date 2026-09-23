import { AppTheme } from "../../theme/AppTheme";
import { LoadingView, ErrorView, EmptyView, SectionTitle, StatBadge } from "../../components/CommonWidgets";
import { DiscoverViewModel, DiscoverSupplyItem, DiscoverRegionItem, DiscoverRankItem, DiscoverFlowerCard, DiscoverTxItem } from "../../viewmodels/FlowerViewModels";

/** WebView 页路由参数（对齐 WebViewPage.PageParams 结构）*/
class WebPageParams {
  page: string = '';
  title: string = '';
}

/** 金刚区功能入口定义 */
class FuncEntry {
  title: string = '';
  emoji: string = '';
  gradStart: string = '#3b82f6';
  gradEnd: string = '#1d4ed8';
  target: string = '';

  constructor(title: string, emoji: string, gradStart: string, gradEnd: string, target: string) {
    this.title = title;
    this.emoji = emoji;
    this.gradStart = gradStart;
    this.gradEnd = gradEnd;
    this.target = target;
  }
}

/** 金刚区 2×4：一行四入口，两行共八个 */
const FUNC_ROW1: FuncEntry[] = [
  new FuncEntry('即时消息', '💬', '#3b82f6', '#1d4ed8', 'pages/ChatListPage'),
  new FuncEntry('购物车', '🛒', '#10b981', '#047857', 'pages/CartPage'),
  new FuncEntry('市场预报', '📈', '#7e57c2', '#5e35b1', 'pages/ForecastPage'),
  new FuncEntry('音乐', '🎵', '#ec4899', '#be185d', 'web:music')
];

const FUNC_ROW2: FuncEntry[] = [
  new FuncEntry('联系人', '👥', '#14b8a6', '#0f766e', 'pages/ContactsPage'),
  new FuncEntry('订单', '📋', '#f59e0b', '#b45309', 'pages/OrderListPage'),
  new FuncEntry('地址', '📍', '#f43f5e', '#be123c', 'pages/AddressManagePage'),
  new FuncEntry('商家预警', '⚡', '#6366f1', '#4338ca', 'web:alerts')
];

/** 金刚区入口（渐变图标方块 + 标题）*/
@Component
struct FuncGrid {
  @Prop entry: FuncEntry;
  onTap: (entry: FuncEntry) => void = () => {
  };

  build() {
    Column({ space: 7 }) {
      Stack({ alignContent: Alignment.Center }) {
        Text(this.entry.emoji)
          .fontSize(22)
      }
      .width(52)
      .height(52)
      .borderRadius(17)
      .linearGradient({
        angle: 135,
        colors: [[this.entry.gradStart, 0], [this.entry.gradEnd, 1]]
      })
      .shadow({ radius: 10, color: 'rgba(0,0,0,0.30)', offsetX: 0, offsetY: 4 })

      Text(this.entry.title)
        .fontSize(12)
        .fontColor(AppTheme.text)
    }
    .layoutWeight(1)
    .onClick(() => this.onTap(this.entry))
  }
}

/** 头部概览统计项（标签 + 主值，可选副值）*/
@Component
struct HeaderStat {
  @Prop label: string = '';
  @Prop value: string = '--';
  @Prop sub: string = '';
  @Prop subColor: string = '#ffffff';

  build() {
    Column({ space: 3 }) {
      Text(this.label)
        .fontSize(10)
        .fontColor('rgba(255,255,255,0.50)')
      Row({ space: 4 }) {
        Text(this.value)
          .fontSize(15)
          .fontWeight(FontWeight.Bold)
          .fontColor('#ffffff')
          .fontFamily(AppTheme.fontMono)
        if (this.sub.length > 0) {
          Text(this.sub)
            .fontSize(10)
            .fontWeight(FontWeight.Medium)
            .fontColor(this.subColor)
        }
      }
      .alignItems(VerticalAlign.Bottom)
    }
    .layoutWeight(1)
    .alignItems(HorizontalAlign.Center)
  }
}

/** 排名徽章（前三名金银铜）*/
@Component
struct RankBadge {
  @Prop rank: number = 0;

  private badgeColor(): string {
    if (this.rank === 1) {
      return '#fbbf24';
    }
    if (this.rank === 2) {
      return '#cbd5e1';
    }
    if (this.rank === 3) {
      return '#d97706';
    }
    return 'rgba(255,255,255,0.12)';
  }

  private badgeText(): string {
    return `${this.rank}`;
  }

  build() {
    Text(this.badgeText())
      .fontSize(11)
      .fontWeight(FontWeight.Bold)
      .fontColor(this.rank <= 3 ? '#171d2b' : AppTheme.textSecondary)
      .textAlign(TextAlign.Center)
      .width(20)
      .height(20)
      .borderRadius(10)
      .backgroundColor(this.badgeColor())
  }
}

@Component
export struct DiscoverTab {
  @State vm: DiscoverViewModel = new DiscoverViewModel();
  @State refreshing: boolean = false;
  /** Swiper 指示器样式（API24 DotIndicator 无 unselectedColor，仅设置选中色）*/
  private swiperIndicator: DotIndicator = new DotIndicator()
    .selectedColor(AppTheme.primary);

  aboutToAppear(): void {
    if (this.vm.supplyDemand.length === 0 && !this.vm.loading) {
      this.vm.load();
    }
  }

  /** 跨 Tab 跳转（与首页同一 AppStorage 通道）*/
  private jumpTab(index: number): void {
    AppStorage.setOrCreate('currentTab', index);
  }

  /** 品种详情/预测页（参数与 MarketTab.openDetail 对齐）*/
  private openSpecies(speciesId: number, name: string, originGrade: string, priceText: string,
    changeText: string, changeUp: boolean, emoji: string, gradStart: string, gradEnd: string): void {
    try {
      const params: Record<string, Object> = {
        'speciesId': speciesId,
        'speciesName': name,
        'originGrade': originGrade,
        'priceText': priceText,
        'changeText': changeText,
        'changeUp': changeUp,
        'emoji': emoji,
        'gradStart': gradStart,
        'gradEnd': gradEnd
      };
      this.getUIContext().getRouter().pushUrl({ url: 'pages/ForecastPage', params: params });
    }
    catch (_e) {
      // 路由异常保护：吞掉避免 UI 崩溃
    }
  }

  /** 花语卡 → 品种详情 */
  private openFlower(c: DiscoverFlowerCard): void {
    this.openSpecies(c.speciesId, c.name, `${c.origin}`, c.priceText, c.changeText, c.changeUp, c.emoji, c.gradStart, c.gradEnd);
  }

  /** 风向行 → 品种详情 */
  private openRank(r: DiscoverRankItem): void {
    this.openSpecies(r.speciesId, r.name, r.originGrade, r.priceText, r.changeText, r.changeUp, r.emoji, r.gradStart, r.gradEnd);
  }

  /** 金刚区入口跳转 */
  private openFunc(entry: FuncEntry): void {
    try {
      if (entry.target.startsWith('web:')) {
        const params = new WebPageParams();
        params.page = entry.target === 'web:music' ? 'harmony-music.html' : 'harmony-alerts.html';
        params.title = entry.title;
        this.getUIContext().getRouter().pushUrl({ url: 'pages/WebViewPage', params: params });
        return;
      }
      if (entry.target === 'pages/ForecastPage') {
        // 市场预报：带上第一个品种参数（无数据时 ForecastPage 自行兜底）
        if (this.vm.flowerCards.length > 0) {
          this.openFlower(this.vm.flowerCards[0]);
          return;
        }
      }
      this.getUIContext().getRouter().pushUrl({ url: entry.target });
    }
    catch (_e) {
      // 路由异常保护
    }
  }

  @Builder HeaderBanner() {
    Stack({ alignContent: Alignment.TopStart }) {
      // 春色/紫罗兰光晕装饰
      Circle({ width: 170, height: 170 })
        .fill('rgba(196,181,253,0.12)')
        .blur(26)
        .position({ x: 210, y: -60 })
      Circle({ width: 130, height: 130 })
        .fill('rgba(134,239,172,0.10)')
        .blur(22)
        .position({ x: -40, y: 60 })

      Column({ space: 12 }) {
        Row() {
          Column({ space: 3 }) {
            Text('发现')
              .fontSize(22)
              .fontWeight(FontWeight.Bold)
              .fontColor('#ffffff')
            Text('花卉世界 · 供需风向 · 每日花语')
              .fontSize(12)
              .fontColor('rgba(255,255,255,0.60)')
          }
          .alignItems(HorizontalAlign.Start)
          .layoutWeight(1)

          // 更新时间胶囊
          if (this.vm.updateTimeText.length > 0) {
            Text(this.vm.updateTimeText)
              .fontSize(10)
              .fontColor('rgba(255,255,255,0.65)')
              .padding({ left: 8, right: 8, top: 4, bottom: 4 })
              .borderRadius(999)
              .backgroundColor('rgba(255,255,255,0.12)')
          }
        }
        .width('100%')

        // 搜索条（点击跳市场 Tab 聚焦搜索）
        Row({ space: 8 }) {
          Text('🔍')
            .fontSize(14)
          Text('搜索花卉品种 / 产地')
            .fontSize(13)
            .fontColor('rgba(255,255,255,0.45)')
            .layoutWeight(1)
        }
        .width('100%')
        .padding({ left: 14, right: 14, top: 10, bottom: 10 })
        .borderRadius(999)
        .backgroundColor('rgba(255,255,255,0.10)')
        .border({ width: 1, color: 'rgba(255,255,255,0.12)' })
        .onClick(() => this.jumpTab(2))

        // 市场概览统计条（均价/品种/预警/涨跌）
        Row() {
          HeaderStat({
            label: '市场均价',
            value: this.vm.avgPriceText,
            sub: this.vm.priceChangeText,
            subColor: this.vm.priceChangeUp ? '#5eead4' : '#fda4af'
          })
          Row()
            .width(1)
            .height(26)
            .backgroundColor('rgba(255,255,255,0.12)')
          HeaderStat({
            label: '在售品种',
            value: this.vm.speciesCountText
          })
          Row()
            .width(1)
            .height(26)
            .backgroundColor('rgba(255,255,255,0.12)')
          HeaderStat({
            label: '今日预警',
            value: this.vm.alertCountText
          })
        }
        .width('100%')
        .padding({ top: 12, bottom: 12 })
        .borderRadius(16)
        .backgroundColor('rgba(255,255,255,0.08)')
        .border({ width: 1, color: 'rgba(255,255,255,0.06)' })
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
      colors: [['#242c3e', 0], ['#3b2f63', 0.55], ['#171d2b', 1]]
    })
    .border({ width: 1, color: 'rgba(255,255,255,0.08)' })
  }

  /** 供需比徽章文案/配色（0机会/1平衡/2承压）*/
  private ratioBadgeText(state: number, ratioText: string): string {
    if (state === 0) {
      return `缺货 ${ratioText}`;
    }
    if (state === 2) {
      return `过剩 ${ratioText}`;
    }
    return `平衡 ${ratioText}`;
  }

  /** 供需图例行（绿点=供给 金点=需求 + 判读说明）*/
  @Builder SupplyLegend() {
    Row({ space: 12 }) {
      Row({ space: 5 }) {
        Row()
          .width(8)
          .height(8)
          .borderRadius(4)
          .backgroundColor('#34d399')
        Text('供给量')
          .fontSize(10)
          .fontColor(AppTheme.textSecondary)
      }
      Row({ space: 5 }) {
        Row()
          .width(8)
          .height(8)
          .borderRadius(4)
          .backgroundColor('#f59e0b')
        Text('需求量')
          .fontSize(10)
          .fontColor(AppTheme.textSecondary)
      }
      Text('比值 <0.8 缺货 · >1.2 过剩')
        .fontSize(10)
        .fontColor('rgba(120,123,134,0.75)')
        .layoutWeight(1)
        .textAlign(TextAlign.End)
        .maxLines(1)
        .textOverflow({ overflow: TextOverflow.Ellipsis })
    }
    .width('100%')
    .padding({ top: 10, bottom: 2 })
    .alignItems(VerticalAlign.Center)
  }

  build() {
    Refresh({ refreshing: $$this.refreshing }) {
      Scroll() {
        Column({ space: AppTheme.spaceMd }) {
          this.HeaderBanner()

          if (this.vm.loading) {
            LoadingView()
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
            // ===== 金刚区（2×4 卡片）=====
            Column({ space: AppTheme.spaceMd }) {
              Row({ space: AppTheme.spaceSm }) {
                ForEach(FUNC_ROW1, (entry: FuncEntry) => {
                  FuncGrid({
                    entry: entry,
                    onTap: (e: FuncEntry) => this.openFunc(e)
                  })
                }, (entry: FuncEntry) => entry.title)
              }
              .width('100%')

              Row({ space: AppTheme.spaceSm }) {
                ForEach(FUNC_ROW2, (entry: FuncEntry) => {
                  FuncGrid({
                    entry: entry,
                    onTap: (e: FuncEntry) => this.openFunc(e)
                  })
                }, (entry: FuncEntry) => entry.title)
              }
              .width('100%')
            }
            .width('100%')
            .padding({ left: AppTheme.spaceLg, right: AppTheme.spaceLg, top: AppTheme.spaceMd, bottom: AppTheme.spaceMd })
            .backgroundColor(AppTheme.card)
            .borderRadius(AppTheme.radiusCard)
            .border({ width: 1, color: AppTheme.divider })

            // ===== 今日花语（轮播）=====
            if (this.vm.flowerCards.length > 0) {
              Column({ space: AppTheme.spaceMd }) {
                SectionTitle({ title: '今日花语', actionText: '换一批 ›', onAction: () => {
                  this.vm.rotateFlowerCards();
                } })
                Swiper() {
                  ForEach(this.vm.flowerCards, (c: DiscoverFlowerCard) => {
                    Row({ space: 14 }) {
                      Stack({ alignContent: Alignment.Center }) {
                        // 底层白色柔光，托起 emoji
                        Circle({ width: 56, height: 56 })
                          .fill('rgba(255,255,255,0.20)')
                        Text(c.emoji)
                          .fontSize(30)
                      }
                      .width(76)
                      .height(76)
                      .borderRadius(18)

                      Column({ space: 5 }) {
                        Row({ space: 8 }) {
                          Text(c.name)
                            .fontSize(16)
                            .fontWeight(FontWeight.Bold)
                            .fontColor('#ffffff')
                            .maxLines(1)
                            .textOverflow({ overflow: TextOverflow.Ellipsis })
                            .layoutWeight(1)
                          Text(`${c.changeUp ? '▲' : '▼'}${c.changeText}`)
                            .fontSize(10)
                            .fontWeight(FontWeight.Bold)
                            .fontColor('#ffffff')
                            .padding({ left: 6, right: 6, top: 2, bottom: 2 })
                            .borderRadius(6)
                            .backgroundColor(c.changeUp ? 'rgba(94,234,212,0.30)' : 'rgba(253,164,175,0.30)')
                        }
                        .width('100%')
                        .alignItems(VerticalAlign.Center)

                        Text(c.meaning)
                          .fontSize(12)
                          .fontColor('rgba(255,255,255,0.75)')
                        Row({ space: 6 }) {
                          Text(c.priceText)
                            .fontSize(11)
                            .fontFamily(AppTheme.fontMono)
                            .fontColor('rgba(255,255,255,0.65)')
                          Text('查看行情 ›')
                            .fontSize(11)
                            .fontColor('#93c5fd')
                        }
                      }
                      .alignItems(HorizontalAlign.Start)
                      .layoutWeight(1)
                    }
                    .width('100%')
                    .padding(16)
                    .borderRadius(20)
                    .linearGradient({
                      angle: 120,
                      colors: [[c.gradStart, 0], [c.gradEnd, 1]]
                    })
                    .onClick(() => this.openFlower(c))
                  }, (c: DiscoverFlowerCard) => `${c.speciesId}`)
                }
                .autoPlay(true)
                .interval(4200)
                .loop(true)
                .indicator(this.swiperIndicator)
                .height(116)
              }
              .width('100%')
              .alignItems(HorizontalAlign.Start)
            }

            // ===== 成交速递 =====
            if (this.vm.txRows.length > 0) {
              Column({ space: AppTheme.spaceMd }) {
                SectionTitle({ title: '成交速递', badgeText: '实时' })
                Column() {
                  ForEach(this.vm.txRows, (t: DiscoverTxItem, idx: number) => {
                    Column() {
                      Row({ space: 12 }) {
                        Stack({ alignContent: Alignment.Center }) {
                          Text(t.emoji)
                            .fontSize(16)
                        }
                        .width(36)
                        .height(36)
                        .borderRadius(12)
                        .backgroundColor('rgba(45,212,191,0.10)')
                        .border({ width: 1, color: 'rgba(45,212,191,0.16)' })

                        Column({ space: 3 }) {
                          Text(t.speciesName)
                            .fontSize(13)
                            .fontWeight(FontWeight.Medium)
                            .fontColor(AppTheme.text)
                            .maxLines(1)
                            .textOverflow({ overflow: TextOverflow.Ellipsis })
                          Row({ space: 6 }) {
                            Text(t.market)
                              .fontSize(10)
                              .fontColor(AppTheme.textSecondary)
                              .maxLines(1)
                              .textOverflow({ overflow: TextOverflow.Ellipsis })
                            Text(t.timeText)
                              .fontSize(10)
                              .fontColor(AppTheme.textSecondary)
                              .fontFamily(AppTheme.fontMono)
                          }
                          .alignItems(VerticalAlign.Center)
                        }
                        .alignItems(HorizontalAlign.Start)
                        .layoutWeight(1)

                        Column({ space: 2 }) {
                          Text(t.priceText)
                            .fontSize(13)
                            .fontWeight(FontWeight.Bold)
                            .fontColor(AppTheme.up)
                            .fontFamily(AppTheme.fontMono)
                          Text(t.qtyText)
                            .fontSize(10)
                            .fontColor(AppTheme.textSecondary)
                            .fontFamily(AppTheme.fontMono)
                        }
                        .alignItems(HorizontalAlign.End)
                      }
                      .width('100%')
                      .alignItems(VerticalAlign.Center)

                      if (idx < this.vm.txRows.length - 1) {
                        Divider()
                          .color(AppTheme.divider)
                          .margin({ top: 10, bottom: 10 })
                      }
                    }
                    .width('100%')
                  }, (t: DiscoverTxItem) => `${t.speciesName}-${t.timeText}-${t.priceText}`)
                }
                .width('100%')
                .padding({ left: AppTheme.spaceLg, right: AppTheme.spaceLg, top: 6, bottom: 10 })
                .backgroundColor(AppTheme.card)
                .borderRadius(AppTheme.radiusCard)
                .border({ width: 1, color: AppTheme.divider })
                .alignItems(HorizontalAlign.Start)
              }
              .width('100%')
              .alignItems(HorizontalAlign.Start)
            }

            // ===== 品种供需（双色条 + 图例）=====
            SectionTitle({ title: '品种供需' })
            Column() {
              this.SupplyLegend()
              if (this.vm.supplyRows.length === 0) {
                EmptyView({ message: '暂无供需数据' })
              }
              ForEach(this.vm.supplyRows, (s: DiscoverSupplyItem, idx: number) => {
                Column() {
                  Row({ space: 8 }) {
                    Text(s.speciesName)
                      .fontSize(13)
                      .fontWeight(FontWeight.Medium)
                      .fontColor(AppTheme.text)
                      .layoutWeight(1)
                      .maxLines(1)
                      .textOverflow({ overflow: TextOverflow.Ellipsis })

                    StatBadge({
                      text: this.ratioBadgeText(s.ratioState, s.ratioText),
                      positive: s.ratioState !== 2
                    })
                  }
                  .width('100%')

                  // 供给条（绿）
                  Row({ space: 8 }) {
                    Text('供')
                      .fontSize(10)
                      .fontColor(AppTheme.textSecondary)
                      .width(14)
                    Row() {
                      Row()
                        .width(`${Math.round(s.supplyBar * 100)}%`)
                        .height(5)
                        .borderRadius(3)
                        .linearGradient({
                          angle: 90,
                          colors: [['#34d399', 0], ['#10b981', 1]]
                        })
                    }
                    .layoutWeight(1)
                    .height(5)
                    .borderRadius(3)
                    .backgroundColor('rgba(52,211,153,0.10)')

                    Text(`${s.supply}`)
                      .fontSize(10)
                      .fontColor(AppTheme.up)
                      .fontFamily(AppTheme.fontMono)
                      .width(38)
                      .textAlign(TextAlign.End)
                  }
                  .width('100%')
                  .margin({ top: 8 })

                  // 需求条（金）
                  Row({ space: 8 }) {
                    Text('需')
                      .fontSize(10)
                      .fontColor(AppTheme.textSecondary)
                      .width(14)
                    Row() {
                      Row()
                        .width(`${Math.round(s.demandBar * 100)}%`)
                        .height(5)
                        .borderRadius(3)
                        .linearGradient({
                          angle: 90,
                          colors: [['#fcd34d', 0], ['#f59e0b', 1]]
                        })
                    }
                    .layoutWeight(1)
                    .height(5)
                    .borderRadius(3)
                    .backgroundColor('rgba(252,211,77,0.10)')

                    Text(`${s.demand}`)
                      .fontSize(10)
                      .fontColor(AppTheme.warn)
                      .fontFamily(AppTheme.fontMono)
                      .width(38)
                      .textAlign(TextAlign.End)
                  }
                  .width('100%')
                  .margin({ top: 5 })

                  if (idx < this.vm.supplyRows.length - 1) {
                    Divider()
                      .color(AppTheme.divider)
                      .margin({ top: 10, bottom: 10 })
                  }
                }
                .width('100%')
                .padding({ top: 8, bottom: 8 })
              }, (s: DiscoverSupplyItem) => `${s.speciesName}-${s.ratioText}`)
            }
            .width('100%')
            .padding({ left: AppTheme.spaceLg, right: AppTheme.spaceLg, top: 6, bottom: 10 })
            .backgroundColor(AppTheme.card)
            .borderRadius(AppTheme.radiusCard)
            .border({ width: 1, color: AppTheme.divider })
            .alignItems(HorizontalAlign.Start)

            // ===== 区域热度榜 =====
            SectionTitle({ title: '区域热度榜' })
            Column() {
              if (this.vm.regionRows.length === 0) {
                EmptyView({ message: '暂无区域数据' })
              }
              ForEach(this.vm.regionRows, (r: DiscoverRegionItem, idx: number) => {
                Column() {
                  Row({ space: 10 }) {
                    RankBadge({ rank: r.rank })

                    Row({ space: 6 }) {
                      Text(r.regionName)
                        .fontSize(13)
                        .fontColor(AppTheme.text)
                        .maxLines(1)
                        .textOverflow({ overflow: TextOverflow.Ellipsis })
                      if (r.hot) {
                        Text('🔥热')
                          .fontSize(9)
                          .fontWeight(FontWeight.Bold)
                          .fontColor('#ffd54f')
                          .padding({ left: 5, right: 5, top: 1, bottom: 1 })
                          .borderRadius(6)
                          .backgroundColor('rgba(255,112,67,0.16)')
                      }
                    }
                    .width(88)

                    Row() {
                      Row()
                        .width(`${Math.round(r.barRatio * 100)}%`)
                        .height(6)
                        .borderRadius(3)
                        .linearGradient({
                          angle: 90,
                          colors: r.hot
                            ? [['#ff7043', 0], ['#ff6b6b', 1]]
                            : [[AppTheme.primary, 0], ['#7e57c2', 1]]
                        })
                    }
                    .layoutWeight(1)
                    .height(6)
                    .borderRadius(3)
                    .backgroundColor('rgba(255,255,255,0.06)')

                    StatBadge({ text: r.indexText, positive: !r.hot })
                  }
                  .width('100%')
                  .alignItems(VerticalAlign.Center)

                  if (idx < this.vm.regionRows.length - 1) {
                    Divider()
                      .color(AppTheme.divider)
                      .margin({ top: 10, bottom: 10 })
                  }
                }
                .width('100%')
              }, (r: DiscoverRegionItem) => r.regionName)
            }
            .width('100%')
            .padding({ left: AppTheme.spaceLg, right: AppTheme.spaceLg, top: 10, bottom: 10 })
            .backgroundColor(AppTheme.card)
            .borderRadius(AppTheme.radiusCard)
            .border({ width: 1, color: AppTheme.divider })
            .alignItems(HorizontalAlign.Start)

            // ===== 价格风向标（更多 → 市场 Tab）=====
            SectionTitle({
              title: '价格风向标',
              actionText: '更多 ›',
              onAction: () => this.jumpTab(2)
            })
            Column() {
              if (this.vm.rankRows.length === 0) {
                EmptyView({ message: '暂无行情数据' })
              }
              ForEach(this.vm.rankRows, (r: DiscoverRankItem, idx: number) => {
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
                      Text(r.originGrade)
                        .fontSize(11)
                        .fontColor(AppTheme.textSecondary)
                        .maxLines(1)
                        .textOverflow({ overflow: TextOverflow.Ellipsis })
                    }
                    .alignItems(HorizontalAlign.Start)
                    .layoutWeight(1)

                    Text(r.priceText)
                      .fontSize(14)
                      .fontWeight(FontWeight.Bold)
                      .fontColor(AppTheme.text)
                      .fontFamily(AppTheme.fontMono)

                    Text(r.changeText)
                      .fontSize(11)
                      .fontWeight(FontWeight.Medium)
                      .fontFamily(AppTheme.fontMono)
                      .fontColor(r.changeUp ? AppTheme.up : AppTheme.down)
                      .padding({ left: 7, right: 7, top: 3, bottom: 3 })
                      .borderRadius(6)
                      .backgroundColor(r.changeUp ? 'rgba(38,181,160,0.14)' : 'rgba(255,107,107,0.14)')
                  }
                  .width('100%')
                  .alignItems(VerticalAlign.Center)

                  if (idx < this.vm.rankRows.length - 1) {
                    Divider()
                      .color(AppTheme.divider)
                      .margin({ top: 10, bottom: 10 })
                  }
                }
                .width('100%')
                .onClick(() => this.openRank(r))
              }, (r: DiscoverRankItem) => `${r.speciesId}-${r.changeText}`)
            }
            .width('100%')
            .padding({ left: AppTheme.spaceLg, right: AppTheme.spaceLg, top: 10, bottom: 10 })
            .backgroundColor(AppTheme.card)
            .borderRadius(AppTheme.radiusCard)
            .border({ width: 1, color: AppTheme.divider })
            .alignItems(HorizontalAlign.Start)

            // ===== 底部 footer =====
            Column({ space: 6 }) {
              Row({ space: 8 }) {
                Row()
                  .width(24)
                  .height(1)
                  .backgroundColor('rgba(120,123,134,0.35)')
                Text('✿')
                  .fontSize(11)
                  .fontColor('rgba(120,123,134,0.6)')
                Row()
                  .width(24)
                  .height(1)
                  .backgroundColor('rgba(120,123,134,0.35)')
              }
              .alignItems(VerticalAlign.Center)

              Text('斗南花卉 · 供需风向 · 数据仅供参考')
                .fontSize(10)
                .fontColor('rgba(120,123,134,0.75)')
              Text('行情实时更新 · 下拉刷新获取最新数据')
                .fontSize(10)
                .fontColor('rgba(120,123,134,0.55)')
            }
            .width('100%')
            .padding({ top: AppTheme.spaceMd, bottom: AppTheme.spaceSm })
            .alignItems(HorizontalAlign.Center)
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
