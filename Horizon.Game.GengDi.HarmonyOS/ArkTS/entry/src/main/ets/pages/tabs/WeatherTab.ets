import { WeatherViewModel, CityTemp, HourShow, LifeIndexItem, WeatherHelper } from "../../viewmodels/FlowerViewModels";
import { ApiClient, WeatherPlace } from "../../services/ApiClient";
import type { WeatherCurrentFull, WeatherDailyItem, AgriAlertItem } from "../../services/ApiClient";

/**
 * 天气页（墨迹天气风格原生重写版）。
 *
 * 对应设计原型 harmony-weather.html：
 * - 深蓝灰渐变背景（#242C3E → #171D2B）+ 青色点缀
 * - 顶部搜索城市 + 常用城市横滑栏（城市 + 实时温度）
 * - 当前天气主面板 / 3×2 指标网格 / 12 项生活服务两行拼块
 * - 逐小时预报（Canvas 温度曲线 + 时间轴横滑）
 * - 未来预报（Canvas 折线/柱状切换 + 列表）
 * - 天气详情（Canvas 风速罗盘 + 2×2 指标）
 * - 农事提醒
 */

/** 天气页配色（显式接口，满足 arkts-no-untyped-obj-literals） */
interface WxColors {
  skyFrom: string;
  skyMid: string;
  skyTo: string;
  accent: string;
  brand: string;
  good: string;
  warn: string;
  bad: string;
  textHi: string;
  textMid: string;
  textLow: string;
  cardBg: string;
  cardBorder: string;
  cellBg: string;
  lifeCellBg: string;
  lifeGap: string;
  activeBg: string;
  todayBg: string;
}

const C: WxColors = {
  skyFrom: '#242c3e',
  skyMid: '#1d2434',
  skyTo: '#171d2b',
  accent: '#2dd4bf',
  brand: '#3d8bff',
  good: '#26b5a0',
  warn: '#ffd54a',
  bad: '#ff6b6b',
  textHi: '#ffffff',
  textMid: 'rgba(255,255,255,0.66)',
  textLow: 'rgba(255,255,255,0.42)',
  cardBg: 'rgba(255,255,255,0.055)',
  cardBorder: 'rgba(255,255,255,0.08)',
  cellBg: 'rgba(255,255,255,0.04)',
  lifeCellBg: '#242c3e',
  lifeGap: 'rgba(10,14,22,0.55)',
  activeBg: 'rgba(45,212,191,0.12)',
  todayBg: 'rgba(45,212,191,0.10)'
};

@Component
export struct WeatherTab {
  @State vm: WeatherViewModel = new WeatherViewModel();
  /** 搜索关键词 */
  @State searchKey: string = '';
  /** 搜索命中地点（城市/区县/乡镇/自然村）*/
  @State searchHits: WeatherPlace[] = [];
  /** 搜索中（远程地点搜索）*/
  @State searching: boolean = false;
  /** 图表模式：line / bar */
  @State chartMode: string = 'line';
  /** 快捷（收藏）地点本地镜像（ForEach 数据源需 @State 数组才可靠 diff，经 onFavChanged 同步）*/
  @State favPlaces: WeatherPlace[] = [];
  /** 搜索防抖句柄 */
  private searchTimer: number = -1;

  private hourlyCtx: CanvasRenderingContext2D =
    new CanvasRenderingContext2D(new RenderingContextSettings(true));
  private tempCtx: CanvasRenderingContext2D =
    new CanvasRenderingContext2D(new RenderingContextSettings(true));
  private compassCtx: CanvasRenderingContext2D =
    new CanvasRenderingContext2D(new RenderingContextSettings(true));
  /** Canvas 就绪标记（避免数据先到、画布未 ready） */
  private hourlyReady: boolean = false;
  private tempReady: boolean = false;
  private compassReady: boolean = false;

  aboutToAppear(): void {
    this.vm.onDataChanged = (): void => {
      this.redrawAll();
    };
    this.vm.onFavChanged = (): void => {
      this.favPlaces = this.vm.getFavPlaces();
    };
    this.favPlaces = this.vm.getFavPlaces();
    this.vm.load();
    this.vm.loadCityTemps();
    this.vm.loadCities();
    // 恢复持久化收藏地点（城市/自然村），完成后自动刷新横滑栏温度
    this.vm.restoreFavorites();
  }

  aboutToDisappear(): void {
    if (this.searchTimer >= 0) {
      clearTimeout(this.searchTimer);
      this.searchTimer = -1;
    }
  }

  // ==================== 搜索（城市/区县/乡镇/自然村，后端异步 + 本地兜底） ====================
  private doSearch(key: string): void {
    this.searchKey = key;
    if (this.searchTimer >= 0) {
      clearTimeout(this.searchTimer);
    }
    if (key.trim().length < 2) {
      this.searchHits = [];
      this.searching = false;
      return;
    }
    // 立即显示本地候选（城市过滤），随后 400ms 防抖发起远程地点搜索
    const localCities = this.vm.searchCities(key);
    const localHits: WeatherPlace[] = [];
    for (const c of localCities) {
      const p = new WeatherPlace();
      p.name = c;
      p.kind = 'city';
      localHits.push(p);
    }
    this.searchHits = localHits;
    this.searching = true;
    this.searchTimer = setTimeout(() => {
      this.searchTimer = -1;
      this.fetchRemotePlaces(key.trim());
    }, 400);
  }

  private fetchRemotePlaces(key: string): void {
    const api = ApiClient.getInstance();
    api.searchWeatherPlaces(key).then((places: WeatherPlace[]) => {
      // 关键词未变才应用结果（避免竞态覆盖新输入）
      if (this.searchKey.trim() !== key) {
        return;
      }
      this.searchHits = places;
      this.searching = false;
    }).catch((_e: Error) => {
      if (this.searchKey.trim() === key) {
        this.searching = false;
      }
    });
  }

  private pickCity(place: WeatherPlace): void {
    this.searchKey = '';
    this.searchHits = [];
    if (place.lat !== 0 && place.lon !== 0) {
      // 自然村/区县/远程地点：坐标直查
      this.vm.switchPlace(place);
    } else {
      this.vm.switchCity(place.name);
    }
  }

  /** 地点类型图标：城市/区县/村镇/远程/缓存 */
  private placeIcon(kind: string): string {
    if (kind === 'city') {
      return '🏙️';
    }
    if (kind === 'district') {
      return '🏘️';
    }
    if (kind === 'village') {
      return '🏡';
    }
    return '📍';
  }

  /** 地点上下文（省 市 区）*/
  private placeContext(place: WeatherPlace): string {
    const parts: string[] = [];
    if (place.province.length > 0) {
      parts.push(place.province);
    }
    if (place.city.length > 0 && place.city !== place.province) {
      parts.push(place.city);
    }
    return parts.join(' · ');
  }

  // ==================== Canvas 重绘 ====================
  private redrawAll(): void {
    this.drawHourlyChart();
    this.drawTempChart();
    this.drawCompass();
  }

  /** 逐小时温度曲线（橙色平滑曲线 + 渐变填充） */
  private drawHourlyChart(): void {
    if (!this.hourlyReady) {
      return;
    }
    const ctx = this.hourlyCtx;
    const w = ctx.width;
    const h = ctx.height;
    ctx.clearRect(0, 0, w, h);
    const data = this.vm.hourlyTemps;
    if (w <= 0 || h <= 0 || data.length < 2) {
      return;
    }
    let min = data[0];
    let max = data[0];
    for (const t of data) {
      if (t < min) {
        min = t;
      }
      if (t > max) {
        max = t;
      }
    }
    if (max - min < 1) {
      max = min + 1;
    }
    const padX = 12;
    const padTop = 18;
    const padBottom = 10;
    const stepX = (w - padX * 2) / (data.length - 1);
    const yOf = (t: number): number => {
      return padTop + (1 - (t - min) / (max - min)) * (h - padTop - padBottom);
    };
    const xOf = (i: number): number => {
      return padX + i * stepX;
    };

    // 平滑曲线（中点二次贝塞尔）
    const path = (): void => {
      ctx.moveTo(xOf(0), yOf(data[0]));
      for (let i = 1; i < data.length; i++) {
        const cx = (xOf(i - 1) + xOf(i)) / 2;
        ctx.quadraticCurveTo(cx, yOf(data[i - 1]), xOf(i), yOf(data[i]));
      }
    };

    // 渐变填充
    ctx.beginPath();
    path();
    ctx.lineTo(xOf(data.length - 1), h);
    ctx.lineTo(xOf(0), h);
    ctx.closePath();
    const grad = ctx.createLinearGradient(0, 0, 0, h);
    grad.addColorStop(0, 'rgba(45,212,191,0.28)');
    grad.addColorStop(1, 'rgba(45,212,191,0)');
    ctx.fillStyle = grad;
    ctx.fill();

    // 曲线
    ctx.beginPath();
    path();
    ctx.strokeStyle = C.accent;
    ctx.lineWidth = 2;
    ctx.stroke();

    // 首末温度标注
    ctx.font = 'bold 11px sans-serif';
    ctx.textAlign = 'left';
    ctx.fillStyle = C.accent;
    ctx.fillText(`${data[0]}°`, xOf(0) - 2, yOf(data[0]) - 8);
    ctx.textAlign = 'right';
    ctx.fillText(`${data[data.length - 1]}°`, xOf(data.length - 1) + 2, yOf(data[data.length - 1]) - 8);
    ctx.textAlign = 'left';
  }

  /** 未来预报图表：折线（最高橙/最低蓝）或柱状 */
  private drawTempChart(): void {
    if (!this.tempReady) {
      return;
    }
    const ctx = this.tempCtx;
    const w = ctx.width;
    const h = ctx.height;
    ctx.clearRect(0, 0, w, h);
    const fc = this.vm.forecast;
    if (w <= 0 || h <= 0 || fc === null || fc.daily.length < 2) {
      return;
    }
    const daily = fc.daily;
    const n = daily.length;
    let min = daily[0].temperatureMin;
    let max = daily[0].temperatureMax;
    for (const d of daily) {
      if (d.temperatureMin < min) {
        min = d.temperatureMin;
      }
      if (d.temperatureMax > max) {
        max = d.temperatureMax;
      }
    }
    if (max - min < 1) {
      max = min + 1;
    }
    const padTop = 12;
    const axisH = 22;
    const chartH = h - padTop - axisH;

    // 横向网格线
    ctx.strokeStyle = 'rgba(255,255,255,0.06)';
    ctx.lineWidth = 1;
    for (let g = 1; g <= 3; g++) {
      const gy = padTop + (chartH / 4) * g;
      ctx.beginPath();
      ctx.moveTo(0, gy);
      ctx.lineTo(w, gy);
      ctx.stroke();
    }

    const yOf = (t: number): number => {
      return padTop + (1 - (t - min) / (max - min)) * chartH;
    };

    if (this.chartMode === 'line') {
      const padX = 16;
      const stepX = (w - padX * 2) / (n - 1);
      const xOf = (i: number): number => {
        return padX + i * stepX;
      };
      const drawLine = (getV: (d: WeatherDailyItem) => number, color: string): void => {
        ctx.beginPath();
        ctx.moveTo(xOf(0), yOf(getV(daily[0])));
        for (let i = 1; i < n; i++) {
          const cx = (xOf(i - 1) + xOf(i)) / 2;
          ctx.quadraticCurveTo(cx, yOf(getV(daily[i - 1])), xOf(i), yOf(getV(daily[i])));
        }
        ctx.strokeStyle = color;
        ctx.lineWidth = 2;
        ctx.stroke();
        for (let i = 0; i < n; i++) {
          ctx.beginPath();
          ctx.arc(xOf(i), yOf(getV(daily[i])), 3, 0, Math.PI * 2);
          ctx.fillStyle = color;
          ctx.fill();
        }
      };
      drawLine((d: WeatherDailyItem): number => {
        return d.temperatureMax;
      }, C.accent);
      drawLine((d: WeatherDailyItem): number => {
        return d.temperatureMin;
      }, C.brand);
      // 图例
      ctx.font = '10px sans-serif';
      ctx.textAlign = 'left';
      ctx.fillStyle = C.accent;
      ctx.beginPath();
      ctx.arc(8, 6, 3, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillText('最高', 14, 9);
      ctx.fillStyle = C.brand;
      ctx.beginPath();
      ctx.arc(44, 6, 3, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillText('最低', 50, 9);
    } else {
      // 柱状
      const slot = w / n;
      const barW = Math.min(14, slot * 0.28);
      for (let i = 0; i < n; i++) {
        const cx = slot * i + slot / 2;
        // 最高柱
        ctx.fillStyle = 'rgba(45,212,191,0.65)';
        const hMax = (daily[i].temperatureMax - min) / (max - min) * chartH;
        this.roundRect(ctx, cx - barW - 2, padTop + chartH - hMax, barW, hMax, 3);
        // 最低柱
        ctx.fillStyle = 'rgba(61,139,255,0.55)';
        const hMin = (daily[i].temperatureMin - min) / (max - min) * chartH;
        this.roundRect(ctx, cx + 2, padTop + chartH - hMin, barW, hMin, 3);
      }
    }

    // x 轴周几标签
    ctx.font = '10px sans-serif';
    ctx.textAlign = 'center';
    ctx.fillStyle = 'rgba(255,255,255,0.5)';
    for (let i = 0; i < n; i++) {
      const x = this.chartMode === 'line' ? 16 + (w - 32) / (n - 1) * i : w / n * i + w / n / 2;
      ctx.fillText(daily[i].weekDay, x, h - 6);
    }
    ctx.textAlign = 'left';
  }

  private roundRect(ctx: CanvasRenderingContext2D, x: number, y: number,
    w: number, h: number, r: number): void {
    if (h <= 0) {
      return;
    }
    const rr = Math.min(r, w / 2, h / 2);
    ctx.beginPath();
    ctx.moveTo(x + rr, y);
    ctx.lineTo(x + w - rr, y);
    ctx.arcTo(x + w, y, x + w, y + rr, rr);
    ctx.lineTo(x + w, y + h);
    ctx.lineTo(x, y + h);
    ctx.lineTo(x, y + rr);
    ctx.arcTo(x, y, x + rr, y, rr);
    ctx.closePath();
    ctx.fill();
  }

  /** 风速罗盘（外圈刻度 + NESW + 橙色风向指针） */
  private drawCompass(): void {
    if (!this.compassReady) {
      return;
    }
    const ctx = this.compassCtx;
    const w = ctx.width;
    const h = ctx.height;
    ctx.clearRect(0, 0, w, h);
    const cx = w / 2;
    const cy = h / 2 - 8;
    const r = Math.min(w, h) / 2 - 4;

    // 外圈
    ctx.beginPath();
    ctx.arc(cx, cy, r, 0, Math.PI * 2);
    ctx.strokeStyle = 'rgba(255,255,255,0.14)';
    ctx.lineWidth = 1.5;
    ctx.stroke();
    // 内圈
    ctx.beginPath();
    ctx.arc(cx, cy, r - 26, 0, Math.PI * 2);
    ctx.strokeStyle = 'rgba(255,255,255,0.08)';
    ctx.lineWidth = 1;
    ctx.stroke();

    // 刻度（每 15°，主刻度每 90°）
    for (let a = 0; a < 360; a += 15) {
      const rad = (a - 90) * Math.PI / 180;
      const isMain = a % 90 === 0;
      const len = isMain ? 8 : 4;
      ctx.beginPath();
      ctx.moveTo(cx + Math.cos(rad) * (r - 2), cy + Math.sin(rad) * (r - 2));
      ctx.lineTo(cx + Math.cos(rad) * (r - 2 - len), cy + Math.sin(rad) * (r - 2 - len));
      ctx.strokeStyle = isMain ? 'rgba(255,255,255,0.45)' : 'rgba(255,255,255,0.18)';
      ctx.lineWidth = isMain ? 2 : 1;
      ctx.stroke();
    }

    // N E S W
    ctx.font = 'bold 11px sans-serif';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    const dirs: string[] = ['N', 'E', 'S', 'W'];
    const angles: number[] = [0, 90, 180, 270];
    for (let i = 0; i < 4; i++) {
      const rad = (angles[i] - 90) * Math.PI / 180;
      const x = cx + Math.cos(rad) * (r - 16);
      const y = cy + Math.sin(rad) * (r - 16);
      ctx.fillStyle = dirs[i] === 'N' ? C.accent : 'rgba(255,255,255,0.55)';
      ctx.fillText(dirs[i], x, y);
    }

    // 风向指针（橙色箭头）
    const windRad = (this.vm.windAngle - 90) * Math.PI / 180;
    const tipX = cx + Math.cos(windRad) * (r - 30);
    const tipY = cy + Math.sin(windRad) * (r - 30);
    const baseX = cx - Math.cos(windRad) * 14;
    const baseY = cy - Math.sin(windRad) * 14;
    const perpX = Math.cos(windRad + Math.PI / 2);
    const perpY = Math.sin(windRad + Math.PI / 2);
    ctx.beginPath();
    ctx.moveTo(tipX, tipY);
    ctx.lineTo(baseX + perpX * 7, baseY + perpY * 7);
    ctx.lineTo(cx, cy);
    ctx.lineTo(baseX - perpX * 7, baseY - perpY * 7);
    ctx.closePath();
    ctx.fillStyle = 'rgba(45,212,191,0.85)';
    ctx.fill();

    // 中心点
    ctx.beginPath();
    ctx.arc(cx, cy, 3.5, 0, Math.PI * 2);
    ctx.fillStyle = '#ffffff';
    ctx.fill();

    // 罗盘下方风力文字
    ctx.fillStyle = '#ffffff';
    ctx.font = 'bold 12px sans-serif';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'alphabetic';
    ctx.fillText(this.vm.windText, cx, h - 6);
    ctx.textAlign = 'left';
  }

  // ==================== 辅助 ====================
  private tempOf(city: string): string {
    for (const ct of this.vm.cityTemps) {
      if (ct.city === city) {
        return ct.temp;
      }
    }
    return '--°';
  }

  private alertColor(level: string): string {
    if (level === 'high') {
      return C.bad;
    }
    if (level === 'good') {
      return C.good;
    }
    return C.accent;
  }

  private alertEmoji(type: string): string {
    const map: Record<string, string> = {
      'sun': '☀️', 'rain': '🌧️', 'dry': '💧', 'humidity': '🌫️',
      'heat': '🥵', 'cold': '🥶', 'wind': '💨', 'ok': '✅'
    };
    const v = map[type];
    return v !== undefined ? v : '🔔';
  }

  @Builder
  SecTitle(title: string) {
    Row({ space: 8 }) {
      Column()
        .width(4)
        .height(16)
        .borderRadius(2)
        .linearGradient({ angle: 180, colors: [[C.accent, 0], ['#0d9488', 1]] })
      Text(title)
        .fontSize(15)
        .fontWeight(FontWeight.Bold)
        .fontColor(C.textHi)
        .letterSpacing(0.5)
    }
    .alignItems(VerticalAlign.Center)
  }

  @Builder
  LifeCell(item: LifeIndexItem) {
    Column({ space: 10 }) {
      Text(item.icon)
        .fontSize(26)
        .lineHeight(30)
      Text(item.tag)
        .fontSize(15)
        .fontWeight(FontWeight.Medium)
        .fontColor(C.textHi)
      Text(item.name)
        .fontSize(11)
        .fontColor('rgba(255,255,255,0.6)')
    }
    .width(108)
    .height(128)
    .justifyContent(FlexAlign.Start)
    .padding({ top: 16, bottom: 14 })
    .backgroundColor(C.lifeCellBg)
  }

  @Builder
  HourCard(item: HourShow, isFirst: boolean) {
    Column({ space: 4 }) {
      Text(item.label)
        .fontSize(11)
        .fontColor(isFirst ? C.accent : C.textMid)
      Text(item.emoji)
        .fontSize(20)
        .lineHeight(24)
      Text(item.temp)
        .fontSize(13)
        .fontWeight(FontWeight.Bold)
        .fontColor(C.textHi)
      Text(item.rainP.length > 0 ? item.rainP : ' ')
        .fontSize(10)
        .fontColor(C.textLow)
    }
    .width(54)
    .padding({ top: 10, bottom: 10 })
    .borderRadius(12)
    .backgroundColor(isFirst ? C.activeBg : C.cellBg)
  }

  @Builder
  DailyRow(d: WeatherDailyItem, isToday: boolean) {
    Row({ space: 8 }) {
      Column({ space: 2 }) {
        Text(d.weekDay)
          .fontSize(12)
          .fontColor(C.textHi)
        Text(d.date.length >= 10 ? d.date.substring(5, 10) : d.date)
          .fontSize(10)
          .fontColor(C.textLow)
      }
      .width(52)
      .alignItems(HorizontalAlign.Start)

      Text(WeatherHelper.weatherEmoji(d.wmoCode, true))
        .fontSize(20)
        .lineHeight(24)

      Row({ space: 4 }) {
        Text(`${d.temperatureMax}°`)
          .fontSize(14)
          .fontWeight(FontWeight.Bold)
          .fontColor(C.accent)
        Text('/')
          .fontSize(11)
          .fontColor(C.textLow)
        Text(`${d.temperatureMin}°`)
          .fontSize(14)
          .fontWeight(FontWeight.Bold)
          .fontColor(C.brand)
      }
      .layoutWeight(1)
      .justifyContent(FlexAlign.Center)

      Text(WeatherHelper.windLevel(d.windSpeedMax))
        .fontSize(11)
        .fontColor(C.textLow)
        .width(64)
        .textAlign(TextAlign.End)
    }
    .width('100%')
    .padding({ left: 10, right: 10, top: 10, bottom: 10 })
    .borderRadius(12)
    .backgroundColor(isToday ? C.todayBg : C.cellBg)
  }

  @Builder
  DetailCell(emoji: string, value: string, label: string) {
    Row({ space: 8 }) {
      Text(emoji)
        .fontSize(16)
      Column({ space: 2 }) {
        Text(value)
          .fontSize(13)
          .fontWeight(FontWeight.Bold)
          .fontColor(C.textHi)
        Text(label)
          .fontSize(10)
          .fontColor(C.textLow)
      }
      .alignItems(HorizontalAlign.Start)
    }
    .layoutWeight(1)
    .padding({ left: 12, right: 12, top: 10, bottom: 10 })
    .borderRadius(12)
    .backgroundColor(C.cellBg)
  }

  @Builder
  AlertRow(a: AgriAlertItem) {
    Row({ space: 10 }) {
      Text(this.alertEmoji(a.type))
        .fontSize(16)
        .width(32)
        .height(32)
        .borderRadius(16)
        .textAlign(TextAlign.Center)
        .backgroundColor(this.alertColor(a.level) + '22')
      Column({ space: 3 }) {
        Text(a.title)
          .fontSize(13)
          .fontWeight(FontWeight.Medium)
          .fontColor(C.textHi)
        Text(a.description)
          .fontSize(11)
          .fontColor(C.textMid)
          .lineHeight(16)
      }
      .layoutWeight(1)
      .alignItems(HorizontalAlign.Start)
    }
    .width('100%')
    .padding(12)
    .borderRadius(12)
    .backgroundColor(C.cellBg)
    .alignItems(VerticalAlign.Top)
  }

  // ==================== 页面 ====================
  build() {
    Column() {
      // ========== 顶部固定区：搜索 + 城市栏 ==========
      Column() {
        // 搜索栏
        Row({ space: 8 }) {
          Text('🔍')
            .fontSize(14)
            .opacity(0.5)
          TextInput({ placeholder: '搜索城市及地址', text: this.searchKey })
            .placeholderColor(C.textLow)
            .placeholderFont({ size: 14 })
            .fontColor(C.textHi)
            .fontSize(14)
            .backgroundColor(Color.Transparent)
            .padding(0)
            .layoutWeight(1)
            .onChange((value: string) => {
              this.doSearch(value);
            })
          if (this.searchKey.length > 0) {
            Text('✕')
              .fontSize(13)
              .fontColor(C.textMid)
              .width(22)
              .height(22)
              .borderRadius(11)
              .textAlign(TextAlign.Center)
              .backgroundColor('rgba(255,255,255,0.12)')
              .onClick(() => {
                this.searchKey = '';
                this.searchHits = [];
              })
          }
        }
        .width('100%')
        .padding({ left: 14, right: 14, top: 10, bottom: 10 })
        .borderRadius(20)
        .backgroundColor('rgba(255,255,255,0.08)')
        .border({ width: 1, color: 'rgba(255,255,255,0.10)' })

        // 搜索结果
        if (this.searchKey.length > 0) {
          Column() {
            Row() {
              Text('搜索结果（城市 / 区县 / 乡镇 / 自然村）')
                .fontSize(12)
                .fontColor(C.textMid)
              Blank()
              if (this.searching) {
                LoadingProgress()
                  .width(14)
                  .height(14)
                  .color(C.textLow)
              } else if (this.searchHits.length > 0) {
                Text(`${this.searchHits.length} 个地点`)
                  .fontSize(11)
                  .fontColor(C.textLow)
              }
            }
            .width('100%')
            .padding({ left: 14, right: 14, top: 10, bottom: 10 })

            if (this.searchHits.length > 0) {
              Scroll() {
                Column() {
                  ForEach(this.searchHits, (place: WeatherPlace) => {
                    Row({ space: 10 }) {
                      Text(this.placeIcon(place.kind))
                        .fontSize(14)
                      Column({ space: 3 }) {
                        Text(place.name)
                          .fontSize(14)
                          .fontColor(C.textHi)
                        if (this.placeContext(place).length > 0) {
                          Text(this.placeContext(place))
                            .fontSize(11)
                            .fontColor(C.textLow)
                            .maxLines(1)
                            .textOverflow({ overflow: TextOverflow.Ellipsis })
                        }
                      }
                      .alignItems(HorizontalAlign.Start)
                      .layoutWeight(1)
                      if (place.name === this.vm.city) {
                        Text('当前')
                          .fontSize(11)
                          .fontColor(C.accent)
                      } else if (this.vm.isFavorite(place.name)) {
                        Text('✓ 已固定')
                          .fontSize(11)
                          .fontColor(C.accent)
                          .padding({ left: 9, right: 9, top: 4, bottom: 4 })
                          .borderRadius(11)
                          .backgroundColor(C.activeBg)
                          .onClick(() => {
                            this.vm.toggleFavorite(place);
                          })
                      } else {
                        Text('＋ 固定')
                          .fontSize(11)
                          .fontColor(C.textMid)
                          .padding({ left: 9, right: 9, top: 4, bottom: 4 })
                          .borderRadius(11)
                          .backgroundColor('rgba(255,255,255,0.10)')
                          .onClick(() => {
                            this.vm.toggleFavorite(place);
                          })
                      }
                    }
                    .width('100%')
                    .padding({ left: 14, right: 14, top: 11, bottom: 11 })
                    .onClick(() => {
                      this.pickCity(place);
                    })
                  }, (place: WeatherPlace, idx: number) => `${idx}_${place.name}_${place.lat}`)
                }
                .width('100%')
              }
              .scrollBar(BarState.Off)
              .constraintSize({ maxHeight: 300 })
              .width('100%')
            } else if (!this.searching) {
              Text('未找到匹配地点')
                .fontSize(13)
                .fontColor(C.textLow)
                .width('100%')
                .textAlign(TextAlign.Center)
                .padding({ top: 24, bottom: 24 })
            }
          }
          .width('100%')
          .margin({ top: 8 })
          .borderRadius(14)
          .backgroundColor('rgba(23,29,43,0.98)')
          .border({ width: 1, color: 'rgba(255,255,255,0.10)' })
          .clip(true)
        }

        // 快捷（收藏）地点横滑栏：城市 + 自然村 + 实时温度；点击切换、长按移除
        Scroll() {
          Row({ space: 8 }) {
            ForEach(this.favPlaces, (place: WeatherPlace) => {
              Row({ space: 6 }) {
                if (place.lat !== 0 && place.lon !== 0) {
                  Text('🏡')
                    .fontSize(11)
                }
                Text(place.name)
                  .fontSize(13)
                  .fontColor(C.textHi)
                Text(this.tempOf(place.name))
                  .fontSize(12)
                  .fontColor(C.accent)
              }
              .padding({ left: 14, right: 14, top: 6, bottom: 6 })
              .borderRadius(20)
              .backgroundColor(place.name === this.vm.city ? 'rgba(45,212,191,0.16)' : 'rgba(255,255,255,0.06)')
              .border({
                width: 1,
                color: place.name === this.vm.city ? 'rgba(45,212,191,0.45)' : 'rgba(255,255,255,0.08)'
              })
              .gesture(
                LongPressGesture({ repeat: false, duration: 500 })
                  .onAction((_e: GestureEvent) => {
                    this.vm.removeFavorite(place.name);
                  })
              )
              .onClick(() => {
                if (place.lat !== 0 && place.lon !== 0) {
                  this.vm.switchPlace(place);
                } else {
                  this.vm.switchCity(place.name);
                }
              })
            }, (place: WeatherPlace) => `${place.name}_${place.kind}`)
          }
          .padding({ top: 2, bottom: 2 })
        }
        .scrollable(ScrollDirection.Horizontal)
        .scrollBar(BarState.Off)
        .width('100%')
        .margin({ top: 10 })
      }
      .width('100%')
      .padding({ left: 16, right: 16, top: 10, bottom: 8 })
      .linearGradient({
        angle: 180,
        colors: [['rgba(23,29,43,0.95)', 0], ['rgba(23,29,43,0.55)', 0.7], ['rgba(23,29,43,0)', 1]]
      })

      // ========== 内容滚动区 ==========
      Scroll() {
        Column({ space: 12 }) {
          if (this.vm.loading && this.vm.current === null) {
            Column({ space: 12 }) {
              LoadingProgress()
                .width(36)
                .height(36)
                .color(C.accent)
              Text('获取天气…')
                .fontSize(13)
                .fontColor(C.textMid)
            }
            .width('100%')
            .padding({ top: 80, bottom: 80 })
            .justifyContent(FlexAlign.Center)
          }
          else if (this.vm.current === null) {
            Column({ space: 12 }) {
              Text('⚠️')
                .fontSize(32)
              Text(this.vm.errorText.length > 0 ? this.vm.errorText : '获取天气数据失败')
                .fontSize(13)
                .fontColor(C.textMid)
              Text('点击重试')
                .fontSize(13)
                .fontColor(C.accent)
                .padding({ left: 20, right: 20, top: 8, bottom: 8 })
                .borderRadius(16)
                .backgroundColor(C.activeBg)
                .onClick(() => {
                  this.vm.load();
                })
            }
            .width('100%')
            .padding({ top: 60, bottom: 60 })
            .alignItems(HorizontalAlign.Center)
          }
          else {
            this.CurrentPanel()
            this.MetricsGrid()
            this.LifeSection()
            this.HourlySection()
            this.DailySection()
            this.DetailSection()
            if (this.vm.alerts.length > 0) {
              this.AlertSection()
            }
          }
        }
        .width('100%')
        .padding({ left: 16, right: 16, bottom: 24 })
        .alignItems(HorizontalAlign.Start)
      }
      .layoutWeight(1)
      .scrollBar(BarState.Off)
      .edgeEffect(EdgeEffect.Spring)
    }
    .width('100%')
    .height('100%')
    .linearGradient({
      angle: 225,
      colors: [[C.skyFrom, 0], [C.skyMid, 0.45], [C.skyTo, 1]]
    })
  }

  // ========== 当前天气主面板 ==========
  @Builder
  CurrentPanel() {
    Stack({ alignContent: Alignment.TopEnd }) {
      // 右上橙光晕
      Circle({ width: 170, height: 170 })
        .fill('rgba(45,212,191,0.16)')
        .blur(45)
        .offset({ x: 40, y: -60 })

      Column() {
        Row({ space: 8 }) {
          Text(this.vm.aqiText)
            .fontSize(11)
            .fontWeight(FontWeight.Medium)
            .fontColor(this.vm.aqiColor)
            .padding({ left: 8, right: 8, top: 3, bottom: 3 })
            .borderRadius(10)
            .backgroundColor(this.vm.aqiColor + '26')
          Text(this.vm.city)
            .fontSize(13)
            .fontWeight(FontWeight.Medium)
            .fontColor(C.textMid)
          Blank()
          Text(`${WeatherHelper.hhmm(this.vm.current?.updateTime ?? '')} 更新`)
            .fontSize(11)
            .fontColor(C.textLow)
        }
        .width('100%')

        Row() {
          Row() {
            Text(`${this.vm.current?.temperature ?? '--'}`)
              .fontSize(76)
              .fontWeight(FontWeight.Lighter)
              .fontColor(C.textHi)
            Text('°')
              .fontSize(24)
              .fontColor(C.textMid)
              .margin({ top: 14 })
          }
          .alignItems(VerticalAlign.Top)

          Blank()

          Column({ space: 6 }) {
            Text(this.vm.curEmoji)
              .fontSize(52)
              .lineHeight(56)
            Text(this.vm.current?.conditionText ?? '--')
              .fontSize(15)
              .fontWeight(FontWeight.Medium)
              .fontColor(C.textHi)
          }
          .alignItems(HorizontalAlign.Center)
        }
        .width('100%')
        .margin({ top: 12 })
        .alignItems(VerticalAlign.Top)

        Row({ space: 12 }) {
          Text(`💨 ${this.vm.windText}`)
            .fontSize(13)
            .fontColor(C.textMid)
          Text('|')
            .fontSize(13)
            .fontColor('rgba(255,255,255,0.2)')
          Text(`💧 ${this.vm.current?.humidity ?? '--'}%`)
            .fontSize(13)
            .fontColor(C.textMid)
          Text('|')
            .fontSize(13)
            .fontColor('rgba(255,255,255,0.2)')
          Text(`🌡️ 体感 ${this.vm.current?.apparentTemperature ?? '--'}°`)
            .fontSize(13)
            .fontColor(C.textMid)
        }
        .width('100%')
        .margin({ top: 10 })
      }
      .width('100%')
    }
    .width('100%')
    .padding(20)
    .borderRadius(16)
    .backgroundColor(C.cardBg)
    .border({ width: 1, color: C.cardBorder })
    .clip(true)
  }

  // ========== 3×2 指标网格 ==========
  @Builder
  MetricsGrid() {
    Grid() {
      GridItem() {
        MetricCellView({ emoji: '💨', value: this.vm.metricWind, label: '风力', cellColor: C.brand })
      }
      GridItem() {
        MetricCellView({ emoji: '🌡️', value: this.vm.metricApparent, label: '体感温度', cellColor: C.brand })
      }
      GridItem() {
        MetricCellView({ emoji: '☀️', value: this.vm.metricUv, label: '紫外线', cellColor: C.accent })
      }
      GridItem() {
        MetricCellView({ emoji: '⏲️', value: this.vm.metricPressure, label: '气压', cellColor: C.brand })
      }
      GridItem() {
        MetricCellView({ emoji: '☁️', value: this.vm.metricCloud, label: '云量', cellColor: C.brand })
      }
      GridItem() {
        MetricCellView({ emoji: '☔', value: this.vm.metricRain, label: '降水概率', cellColor: C.accent })
      }
    }
    .columnsTemplate('1fr 1fr 1fr')
    .columnsGap(10)
    .rowsGap(10)
    .height(178)
    .width('100%')
    .padding(16)
    .borderRadius(16)
    .backgroundColor(C.cardBg)
    .border({ width: 1, color: C.cardBorder })
  }

  // ========== 花卉生活服务（两行横滑拼块） ==========
  @Builder
  LifeSection() {
    Column({ space: 12 }) {
      Row() {
        this.SecTitle('花卉生活服务')
        Blank()
        Row({ space: 2 }) {
          Text(this.vm.lifeScore)
            .fontSize(18)
            .fontWeight(FontWeight.Bold)
            .fontColor(C.good)
          Text('分')
            .fontSize(11)
            .fontColor(C.textLow)
        }
        .alignItems(VerticalAlign.Bottom)
      }
      .width('100%')

      Column({ space: 1 }) {
        // 两行，每行独立横滑（墨迹 .life-index-row）
        ForEach([0, 1], (r: number) => {
          Scroll() {
            Row({ space: 1 }) {
              ForEach(this.lifeRow(r), (item: LifeIndexItem) => {
                this.LifeCell(item)
              }, (item: LifeIndexItem, idx: number) => `${r}_${idx}_${item.name}`)
            }
            .height(128)
          }
          .scrollable(ScrollDirection.Horizontal)
          .scrollBar(BarState.Off)
          .width('100%')
          .height(128)
          .backgroundColor(C.lifeGap)
        }, (r: number) => `row_${r}`)
      }
      .width('100%')
      .borderRadius(10)
      .backgroundColor(C.lifeGap)
      .clip(true)
    }
    .width('100%')
    .padding(16)
    .borderRadius(16)
    .backgroundColor(C.cardBg)
    .border({ width: 1, color: C.cardBorder })
  }

  private lifeRow(row: number): LifeIndexItem[] {
    const half = Math.ceil(this.vm.lifeItems.length / 2);
    const start = row * half;
    const end = Math.min(this.vm.lifeItems.length, start + half);
    return this.vm.lifeItems.slice(start, end);
  }

  // ========== 逐小时预报 ==========
  @Builder
  HourlySection() {
    Column({ space: 12 }) {
      Row() {
        this.SecTitle('逐小时预报')
        Blank()
        Row({ space: 10 }) {
          Text(`🌅 ${this.vm.sunRise}`)
            .fontSize(11)
            .fontColor(C.textLow)
          Text(`🌇 ${this.vm.sunSet}`)
            .fontSize(11)
            .fontColor(C.textLow)
        }
      }
      .width('100%')

      Canvas(this.hourlyCtx)
        .width('100%')
        .height(112)
        .onReady(() => {
          this.hourlyReady = true;
          this.drawHourlyChart();
        })

      Scroll() {
        Row({ space: 8 }) {
          ForEach(this.vm.hourlyShow, (item: HourShow, idx: number) => {
            this.HourCard(item, idx === 0)
          }, (item: HourShow, idx: number) => `${idx}_${item.label}`)
        }
        .padding({ top: 2, bottom: 2 })
      }
      .scrollable(ScrollDirection.Horizontal)
      .scrollBar(BarState.Off)
      .width('100%')
    }
    .width('100%')
    .padding(16)
    .borderRadius(16)
    .backgroundColor(C.cardBg)
    .border({ width: 1, color: C.cardBorder })
  }

  // ========== 未来预报（折线/柱状切换） ==========
  @Builder
  DailySection() {
    Column({ space: 12 }) {
      Row() {
        this.SecTitle('未来预报')
        Blank()
        Row({ space: 2 }) {
          Text('折线')
            .fontSize(11)
            .fontWeight(FontWeight.Medium)
            .fontColor(this.chartMode === 'line' ? '#ffffff' : C.textMid)
            .padding({ left: 12, right: 12, top: 4, bottom: 4 })
            .borderRadius({ topLeft: 12, bottomLeft: 12 })
            .backgroundColor(this.chartMode === 'line' ? C.accent : Color.Transparent)
            .onClick(() => {
              this.chartMode = 'line';
              this.drawTempChart();
            })
          Text('柱状')
            .fontSize(11)
            .fontColor(this.chartMode === 'bar' ? '#ffffff' : C.textMid)
            .padding({ left: 12, right: 12, top: 4, bottom: 4 })
            .borderRadius({ topRight: 12, bottomRight: 12 })
            .backgroundColor(this.chartMode === 'bar' ? C.accent : Color.Transparent)
            .onClick(() => {
              this.chartMode = 'bar';
              this.drawTempChart();
            })
        }
        .borderRadius(12)
        .backgroundColor('rgba(255,255,255,0.08)')
      }
      .width('100%')

      Canvas(this.tempCtx)
        .width('100%')
        .height(160)
        .onReady(() => {
          this.tempReady = true;
          this.drawTempChart();
        })

      if (this.vm.dailyShow.length > 0) {
        Column({ space: 8 }) {
          ForEach(this.vm.dailyShow, (d: WeatherDailyItem, idx: number) => {
            this.DailyRow(d, idx === 0)
          }, (d: WeatherDailyItem, idx: number) => `${idx}_${d.date}`)
        }
        .width('100%')
      }
    }
    .width('100%')
    .padding(16)
    .borderRadius(16)
    .backgroundColor(C.cardBg)
    .border({ width: 1, color: C.cardBorder })
  }

  // ========== 天气详情（罗盘 + 指标） ==========
  @Builder
  DetailSection() {
    Column({ space: 12 }) {
      this.SecTitle('天气详情')
      Row({ space: 16 }) {
        Canvas(this.compassCtx)
          .width(140)
          .height(140)
          .onReady(() => {
            this.compassReady = true;
            this.drawCompass();
          })

        Column({ space: 10 }) {
          Row({ space: 10 }) {
            DetailCellView({ emoji: '💧', value: this.vm.metricHum, label: '湿度' })
            DetailCellView({ emoji: '☀️', value: this.vm.metricUv, label: '紫外线' })
          }
          .width('100%')

          Row({ space: 10 }) {
            DetailCellView({ emoji: '⏲️', value: this.vm.metricPressure, label: '气压' })
            DetailCellView({ emoji: '🍃', value: this.vm.aqiText, label: '空气质量' })
          }
          .width('100%')
        }
        .layoutWeight(1)
      }
      .width('100%')
      .alignItems(VerticalAlign.Center)
    }
    .width('100%')
    .padding(16)
    .borderRadius(16)
    .backgroundColor(C.cardBg)
    .border({ width: 1, color: C.cardBorder })
  }

  // ========== 农事提醒 ==========
  @Builder
  AlertSection() {
    Column({ space: 12 }) {
      this.SecTitle('农事提醒')
      Column({ space: 10 }) {
        ForEach(this.vm.alerts, (a: AgriAlertItem) => {
          this.AlertRow(a)
        }, (a: AgriAlertItem, idx: number) => `${idx}_${a.title}`)
      }
      .width('100%')
    }
    .width('100%')
    .padding(16)
    .borderRadius(16)
    .backgroundColor(C.cardBg)
    .border({ width: 1, color: C.cardBorder })
  }
}

/** 指标格（@Component + @Prop：状态变化经父子同步管道刷新，规避 @Builder 按值传参不联动）*/
@Component
struct MetricCellView {
  @Prop emoji: string = '';
  @Prop value: string = '';
  @Prop label: string = '';
  @Prop cellColor: string = '';

  build() {
    Column({ space: 6 }) {
      Text(this.emoji)
        .fontSize(20)
      Text(this.value)
        .fontSize(14)
        .fontWeight(FontWeight.Bold)
        .fontColor(this.cellColor)
      Text(this.label)
        .fontSize(11)
        .fontColor(C.textLow)
    }
    .width('100%')
    .height('100%')
    .justifyContent(FlexAlign.Center)
    .borderRadius(12)
    .backgroundColor(C.cellBg)
  }
}

/** 详情格（同上，规避 @Builder 按值传参不联动）*/
@Component
struct DetailCellView {
  @Prop emoji: string = '';
  @Prop value: string = '';
  @Prop label: string = '';

  build() {
    Row({ space: 8 }) {
      Text(this.emoji)
        .fontSize(16)
      Column({ space: 2 }) {
        Text(this.value)
          .fontSize(13)
          .fontWeight(FontWeight.Bold)
          .fontColor(C.textHi)
        Text(this.label)
          .fontSize(10)
          .fontColor(C.textLow)
      }
      .alignItems(HorizontalAlign.Start)
    }
    .layoutWeight(1)
    .padding({ left: 12, right: 12, top: 10, bottom: 10 })
    .borderRadius(12)
    .backgroundColor(C.cellBg)
  }
}
