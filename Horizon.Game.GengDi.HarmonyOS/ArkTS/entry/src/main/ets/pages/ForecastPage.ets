import { AppTheme } from "../theme/AppTheme";
import { SectionCard, LoadingView, ErrorView, EmptyView } from "../components/CommonWidgets";
import { MarketViewModel, ChartPoint } from "../viewmodels/FlowerViewModels";
import type { PredictedPricePoint, FlowerPriceSnapshot } from '../services/ApiClient';

/** 价格走势图（Canvas 自绘）：历史实线（青）+ 预测虚线（蓝）+ 置信区间带 + 今日分隔 */
@Component
struct PriceChart {
  @Prop @Watch('onDataChange') hist: ChartPoint[] = [];
  @Prop @Watch('onDataChange') forecast: ChartPoint[] = [];
  private settings: RenderingContextSettings = new RenderingContextSettings(true);
  private ctx: CanvasRenderingContext2D = new CanvasRenderingContext2D(this.settings);

  private onDataChange(): void {
    this.draw();
  }

  build() {
    Canvas(this.ctx)
      .width('100%')
      .height(220)
      .onReady(() => {
        this.draw();
      })
  }

  /** 时间戳 → "MM-DD" */
  private fmtDate(t: number): string {
    const d = new Date(t);
    const pad = (n: number): string => `${n < 10 ? '0' : ''}${n}`;
    return `${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  }

  /** 两点间虚线段（该 SDK Canvas 无 lineDash，手动按 dash/gap 切段）*/
  private dashSeg(x1: number, y1: number, x2: number, y2: number, dash: number, gap: number): void {
    const dx = x2 - x1;
    const dy = y2 - y1;
    const len = Math.sqrt(dx * dx + dy * dy);
    if (len <= 0) {
      return;
    }
    const ux = dx / len;
    const uy = dy / len;
    let t = 0;
    while (t < len) {
      const t2 = Math.min(t + dash, len);
      this.ctx.moveTo(x1 + ux * t, y1 + uy * t);
      this.ctx.lineTo(x1 + ux * t2, y1 + uy * t2);
      t = t2 + gap;
    }
  }

  private draw(): void {
    const w = this.ctx.width;
    const h = this.ctx.height;
    if (w <= 0 || h <= 0) {
      return;
    }
    this.ctx.clearRect(0, 0, w, h);
    const hist = this.hist;
    const fc = this.forecast;
    if (hist.length + fc.length === 0) {
      return;
    }
    const padL = 10;
    const padR = 56;
    const padT = 14;
    const padB = 22;
    const plotW = w - padL - padR;
    const plotH = h - padT - padB;

    // Y 范围（含置信区间）、X 时间范围
    let vMin = Number.MAX_VALUE;
    let vMax = -Number.MAX_VALUE;
    let tMin = Number.MAX_VALUE;
    let tMax = -Number.MAX_VALUE;
    const all: ChartPoint[] = hist.concat(fc);
    for (const p of all) {
      if (p.lower < vMin) {
        vMin = p.lower;
      }
      if (p.upper > vMax) {
        vMax = p.upper;
      }
      if (p.timeMs < tMin) {
        tMin = p.timeMs;
      }
      if (p.timeMs > tMax) {
        tMax = p.timeMs;
      }
    }
    let range = vMax - vMin;
    if (range <= 0) {
      const expand = Math.max(Math.abs(vMax) * 0.1, 0.5);
      vMin -= expand;
      vMax += expand;
    }
    else {
      vMin -= range * 0.08;
      vMax += range * 0.08;
    }
    range = vMax - vMin;
    const tRange = Math.max(1, tMax - tMin);
    const xOf = (t: number): number => padL + (t - tMin) / tRange * plotW;
    const yOf = (v: number): number => padT + (1 - (v - vMin) / range) * plotH;

    // 水平网格 + 右侧价格刻度
    this.ctx.font = '10px HarmonyOS Sans Mono';
    this.ctx.textAlign = 'left';
    this.ctx.textBaseline = 'middle';
    for (let i = 0; i <= 4; i++) {
      const v = vMin + range * i / 4;
      const y = yOf(v);
      this.ctx.strokeStyle = 'rgba(42,46,57,0.9)';
      this.ctx.lineWidth = 1;
      this.ctx.beginPath();
      this.ctx.moveTo(padL, y);
      this.ctx.lineTo(w - padR, y);
      this.ctx.stroke();
      this.ctx.fillStyle = 'rgba(255,255,255,0.42)';
      this.ctx.fillText(v.toFixed(1), w - padR + 8, y);
    }

    // 置信区间带（预测上下界围合）
    if (fc.length >= 2) {
      this.ctx.fillStyle = 'rgba(45,212,191,0.10)';
      this.ctx.beginPath();
      for (let i = 0; i < fc.length; i++) {
        const p = fc[i];
        if (i === 0) {
          this.ctx.moveTo(xOf(p.timeMs), yOf(p.upper));
        }
        else {
          this.ctx.lineTo(xOf(p.timeMs), yOf(p.upper));
        }
      }
      for (let i = fc.length - 1; i >= 0; i--) {
        const p = fc[i];
        this.ctx.lineTo(xOf(p.timeMs), yOf(p.lower));
      }
      this.ctx.closePath();
      this.ctx.fill();
    }

    // 历史实线
    if (hist.length >= 2) {
      this.ctx.strokeStyle = '#26b5a0';
      this.ctx.lineWidth = 2;
      this.ctx.beginPath();
      for (let i = 0; i < hist.length; i++) {
        const p = hist[i];
        if (i === 0) {
          this.ctx.moveTo(xOf(p.timeMs), yOf(p.price));
        }
        else {
          this.ctx.lineTo(xOf(p.timeMs), yOf(p.price));
        }
      }
      this.ctx.stroke();
    }

    // 预测虚线（从历史末点衔接，手动 dash 切段）
    if (fc.length >= 1) {
      this.ctx.strokeStyle = '#2dd4bf';
      this.ctx.lineWidth = 2;
      this.ctx.beginPath();
      const segs: ChartPoint[] = [];
      if (hist.length > 0) {
        segs.push(hist[hist.length - 1]);
      }
      for (const p of fc) {
        segs.push(p);
      }
      for (let i = 0; i < segs.length - 1; i++) {
        const a = segs[i];
        const b = segs[i + 1];
        this.dashSeg(xOf(a.timeMs), yOf(a.price), xOf(b.timeMs), yOf(b.price), 6, 5);
      }
      this.ctx.stroke();
    }

    // 今日分隔虚线 + 最新价圆点
    if (hist.length > 0 && fc.length > 0) {
      const last = hist[hist.length - 1];
      const x = xOf(last.timeMs);
      this.ctx.strokeStyle = 'rgba(120,123,134,0.45)';
      this.ctx.lineWidth = 1;
      this.ctx.beginPath();
      this.dashSeg(x, padT, x, h - padB, 4, 4);
      this.ctx.stroke();
      const y = yOf(last.price);
      this.ctx.fillStyle = '#171d2b';
      this.ctx.strokeStyle = '#26b5a0';
      this.ctx.lineWidth = 2;
      this.ctx.beginPath();
      this.ctx.arc(x, y, 4, 0, Math.PI * 2);
      this.ctx.fill();
      this.ctx.stroke();
    }

    // X 轴日期（首点/今日/预测末点）
    this.ctx.fillStyle = 'rgba(255,255,255,0.42)';
    this.ctx.font = '10px HarmonyOS Sans Mono';
    this.ctx.textBaseline = 'top';
    this.ctx.textAlign = 'left';
    if (hist.length > 0) {
      this.ctx.fillText(this.fmtDate(hist[0].timeMs), padL, h - padB + 6);
    }
    this.ctx.textAlign = 'center';
    if (fc.length > 0) {
      const lastFc = fc[fc.length - 1];
      const xf = xOf(lastFc.timeMs);
      this.ctx.fillText(this.fmtDate(lastFc.timeMs), Math.min(xf, w - padR - 16), h - padB + 6);
    }
    if (hist.length > 0) {
      const last = hist[hist.length - 1];
      this.ctx.fillText(this.fmtDate(last.timeMs), xOf(last.timeMs), h - padB + 6);
    }
  }
}

/** 品种详情页（市场页跳转）：头部摘要卡 + 价格走势图 + 近 7 天历史 + 未来 7 天预测 */
@Entry
@Component
struct ForecastPage {
  @State vm: MarketViewModel = new MarketViewModel();
  /** 路由传入的品种摘要 */
  @State speciesId: number = 0;
  @State speciesName: string = '品种详情';
  @State originGrade: string = '';
  @State priceText: string = '';
  @State changeText: string = '--';
  @State changeUp: boolean = true;
  @State emoji: string = '🌸';
  @State gradStart: string = '#e11d48';
  @State gradEnd: string = '#881337';
  @State history: FlowerPriceSnapshot[] = [];
  /** 走势图数据 */
  @State chartHist: ChartPoint[] = [];
  @State chartForecast: ChartPoint[] = [];

  aboutToAppear(): void {
    const params = this.getUIContext().getRouter().getParams() as Record<string, Object>;
    if (params !== undefined && params !== null) {
      this.speciesId = Number(params['speciesId'] ?? 0);
      this.speciesName = (params['speciesName'] as string) ?? '品种详情';
      this.originGrade = (params['originGrade'] as string) ?? '';
      this.priceText = (params['priceText'] as string) ?? '';
      this.changeText = (params['changeText'] as string) ?? '--';
      this.changeUp = (params['changeUp'] as boolean) ?? true;
      this.emoji = (params['emoji'] as string) ?? '🌸';
      this.gradStart = (params['gradStart'] as string) ?? '#e11d48';
      this.gradEnd = (params['gradEnd'] as string) ?? '#881337';
    }
    if (this.speciesId > 0) {
      this.loadDetail();
    }
  }

  private loadDetail(): void {
    this.vm.loadDetail(this.speciesId).then(() => {
      this.history = this.vm.historyRecent;
      this.chartHist = this.vm.chartHist;
      this.chartForecast = this.vm.chartForecast;
    });
  }

  /** ISO 时间 → "MM-DD" */
  private shortDate(iso: string): string {
    if (iso.length >= 10) {
      return iso.slice(5, 10);
    }
    return iso;
  }

  @Builder
  HeaderCard() {
    Column() {
      Stack({ alignContent: Alignment.TopStart }) {
        Column() {
          Text(this.emoji)
            .fontSize(56)
            .opacity(0.95)
        }
        .width('100%')
        .height(140)
        .linearGradient({
          angle: 135,
          colors: [[this.gradStart, 0], [this.gradEnd, 1]]
        })
        .alignItems(HorizontalAlign.Center)
        .justifyContent(FlexAlign.Center)

        if (this.originGrade.length > 0) {
          Text(this.originGrade)
            .fontSize(11)
            .fontColor(Color.White)
            .backgroundColor('rgba(0,0,0,0.35)')
            .borderRadius(999)
            .padding({ left: 10, right: 10, top: 3, bottom: 3 })
            .margin(12)
        }
      }
      .width('100%')

      Row({ space: 10 }) {
        Text(this.priceText.length > 0 ? this.priceText : '--')
          .fontSize(28)
          .fontWeight(FontWeight.Bold)
          .fontColor(AppTheme.text)
          .fontFamily(AppTheme.fontMono)
        Text(' /扎')
          .fontSize(11)
          .fontColor(AppTheme.textSecondary)
        Blank()
        if (this.priceText.length > 0) {
          Text(this.changeText)
            .fontSize(14)
            .fontWeight(FontWeight.Bold)
            .fontFamily(AppTheme.fontMono)
            .fontColor(this.changeUp ? AppTheme.up : AppTheme.down)
        }
      }
      .width('100%')
      .padding({ left: 16, right: 16, top: 12, bottom: 16 })
      .alignItems(VerticalAlign.Bottom)
    }
    .borderRadius(24)
    .backgroundColor(AppTheme.card)
    .border({ width: 1, color: AppTheme.divider })
    .clip(true)
  }

  build() {
    Column() {
      Row() {
        Text('‹')
          .fontSize(28)
          .fontColor(AppTheme.text)
          .width(32)
          .onClick(() => {
            this.getUIContext().getRouter().back();
          })
        Text(this.speciesName)
          .fontSize(AppTheme.fontTitle)
          .fontColor(AppTheme.text)
          .fontWeight(FontWeight.Medium)
          .maxLines(1)
          .textOverflow({ overflow: TextOverflow.Ellipsis })
          .layoutWeight(1)
      }
        .width('100%')
        .height(52)
        .padding({ left: AppTheme.spaceMd, right: AppTheme.spaceLg })
        .backgroundColor(AppTheme.card)
      Scroll() {
        Column({ space: AppTheme.spaceMd }) {
          this.HeaderCard()

          if (this.vm.forecastLoading) {
            LoadingView({ message: '计算预测…' })
              .margin({ top: 40 })
          }
          else if (this.vm.forecastError.length > 0) {
            ErrorView({
              message: this.vm.forecastError,
              onRetry: () => {
                this.loadDetail();
              }
            })
              .margin({ top: 40 })
          }
          else if (this.vm.forecast === null || this.vm.forecast.predictedPrices.length === 0) {
            EmptyView({ message: '暂无预测数据' })
              .margin({ top: 40 })
          }
          else {
            // ===== 价格走势图（近 45 天历史 + 未来 7 天预测）=====
            if (this.chartHist.length + this.chartForecast.length >= 2) {
              SectionCard({ title: '价格走势（近 45 天）' }) {
                Column() {
                  PriceChart({ hist: this.chartHist, forecast: this.chartForecast })
                  Row({ space: 14 }) {
                    Row({ space: 5 }) {
                      Column()
                        .width(14)
                        .height(3)
                        .borderRadius(2)
                        .backgroundColor('#26b5a0')
                      Text('历史')
                        .fontSize(10)
                        .fontColor(AppTheme.textSecondary)
                    }
                    .alignItems(VerticalAlign.Center)

                    Row({ space: 5 }) {
                      Column()
                        .width(14)
                        .height(0)
                        .border({ width: { top: 2 }, color: '#2dd4bf', style: BorderStyle.Dashed })
                      Text('预测')
                        .fontSize(10)
                        .fontColor(AppTheme.textSecondary)
                    }
                    .alignItems(VerticalAlign.Center)
                  }
                  .width('100%')
                  .justifyContent(FlexAlign.End)
                  .margin({ top: 4 })
                }
                  .alignItems(HorizontalAlign.Start)
              }
            }

            // ===== 近 7 天成交价 =====
            if (this.history.length > 0) {
              SectionCard({ title: '近 7 天成交价' }) {
                Column() {
                  ForEach(this.history, (h: FlowerPriceSnapshot) => {
                    Row() {
                      Text(this.shortDate(h.snapshotTime))
                        .fontSize(AppTheme.fontBody)
                        .fontColor(AppTheme.textSecondary)
                        .layoutWeight(1)
                      Text(`¥${h.avgPrice.toFixed(2)}`)
                        .fontSize(AppTheme.fontSub)
                        .fontColor(AppTheme.text)
                        .fontWeight(FontWeight.Medium)
                        .fontFamily(AppTheme.fontMono)
                      if (h.volume > 0) {
                        Text(`  ${h.volume} 扎`)
                          .fontSize(AppTheme.fontCaption)
                          .fontColor(AppTheme.textSecondary)
                      }
                    }
                      .width('100%')
                      .padding({ top: 6, bottom: 6 })
                  }, (h: FlowerPriceSnapshot) => `${h.speciesId}-${h.snapshotTime}`)
                }
                  .alignItems(HorizontalAlign.Start)
              }
            }

            // ===== 未来 7 天价格预测 =====
            SectionCard({ title: '未来 7 天预测' }) {
              Column() {
                ForEach(this.vm.forecast.predictedPrices, (p: PredictedPricePoint) => {
                  Row() {
                    Text(p.date.slice(5, 10))
                      .fontSize(AppTheme.fontBody)
                      .fontColor(AppTheme.textSecondary)
                      .layoutWeight(1)
                    Text(`¥${p.predictedPrice.toFixed(2)}`)
                      .fontSize(AppTheme.fontSub)
                      .fontColor(AppTheme.text)
                      .fontWeight(FontWeight.Medium)
                      .fontFamily(AppTheme.fontMono)
                    Text(`  [${p.lowerBound.toFixed(2)} ~ ${p.upperBound.toFixed(2)}]`)
                      .fontSize(AppTheme.fontCaption)
                      .fontColor(AppTheme.textSecondary)
                  }
                    .width('100%')
                    .padding({ top: 6, bottom: 6 })
                }, (p: PredictedPricePoint) => p.date)
              }
                .alignItems(HorizontalAlign.Start)
            }
          }
        }
          .padding({ left: AppTheme.spaceLg, right: AppTheme.spaceLg, top: AppTheme.spaceMd, bottom: 20 })
          .alignItems(HorizontalAlign.Start)
      }
        .layoutWeight(1)
        .scrollBar(BarState.Off)
        .backgroundColor(AppTheme.bg)
    }
      .width('100%')
      .height('100%')
      .backgroundColor(AppTheme.bg)
  }
}
