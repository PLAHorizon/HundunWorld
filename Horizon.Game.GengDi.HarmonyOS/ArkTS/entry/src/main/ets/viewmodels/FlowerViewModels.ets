import { ApiClient, DashboardStats, FlowerMarketOverview } from "../services/ApiClient";
import type { FlowerPriceForecast, FlowerPriceSnapshot, FlowerSpeciesListItem, MarketSpeciesItem, RecentTransaction, RegionalTradeData, SupplyDemandData, WeatherCurrentFull, WeatherForecastFull, WeatherDailyItem, AgriAlertItem } from "../services/ApiClient";
import { WeatherPlace } from "../services/ApiClient";
import { AppContext } from "../model/AppContext";
import hilog from "@ohos.hilog";
/**
 * 花卉业务页面 ViewModel 集（MVVM）。
 * 页面只做绑定，加载/刷新/错误状态集中在这里。
 */
/** 首页热点品种卡（所有展示字段预计算到第一层——避免 @Builder 传参不联动）*/
export class HomeTrendItem {
    speciesId: number = 0;
    name: string = '';
    originGrade: string = '';
    priceText: string = '';
    changePercent: number = 0;
    changeText: string = '--';
    changeUp: boolean = true;
    emoji: string = '🌸';
    gradStart: string = '#5a90ff';
    gradEnd: string = '#163380';
}

/** 首页最近交易行（预计算展示字段）*/
export class HomeTxItem {
    name: string = '';
    market: string = '';
    timeText: string = '';
    priceText: string = '';
    qtyText: string = '';
    emoji: string = '🌸';
    gradStart: string = '#3a78ff';
    gradEnd: string = '#1f4fd6';
}

/** 首页区域需求行（ratio 为 0~1 条宽比例）*/
export class HomeRegionItem {
    regionName: string = '';
    indexText: string = '';
    ratio: number = 0;
    hot: boolean = false;
}

/** 问候语（按小时段）*/
function homeGreeting(hour: number): string {
    if (hour >= 5 && hour < 11) {
        return '早上好';
    }
    if (hour >= 11 && hour < 13) {
        return '中午好';
    }
    if (hour >= 13 && hour < 18) {
        return '下午好';
    }
    if (hour >= 18 && hour < 23) {
        return '晚上好';
    }
    return '夜深了';
}

/** "8月23日 周六" */
function homeDateText(d: Date): string {
    const week: string[] = ['周日', '周一', '周二', '周三', '周四', '周五', '周六'];
    return `${d.getMonth() + 1}月${d.getDate()}日 ${week[d.getDay()]}`;
}

@Observed
export class HomeViewModel {
    loading: boolean = false;
    errorText: string = '';
    stats: DashboardStats = new DashboardStats();
    transactions: RecentTransaction[] = [];
    regional: RegionalTradeData[] = [];

    // ===== 头部问候与日期 =====
    greetingText: string = '';
    titleName: string = '';
    dateText: string = '';

    // ===== 市场脉搏（行情概览镜像）=====
    tradeAmountText: string = '¥0.00';
    avgPriceText: string = '--';
    priceChangeText: string = '--';
    priceChangeUp: boolean = true;
    activeCountText: string = '0';
    alertCountText: string = '0';
    lastUpdateText: string = '';

    // ===== 预计算视图数据 =====
    trends: HomeTrendItem[] = [];
    txRows: HomeTxItem[] = [];
    regionRows: HomeRegionItem[] = [];

    constructor() {
        const now = new Date();
        this.greetingText = homeGreeting(now.getHours());
        this.dateText = homeDateText(now);
        const app = AppContext.getInstance();
        const nick = app.user.nickname.length > 0 ? app.user.nickname : app.passportId;
        this.titleName = nick.length > 0 ? nick : '花商';
    }

    async load(): Promise<void> {
        this.loading = true;
        this.errorText = '';
        const api = ApiClient.getInstance();
        try {
            // 行情概览失败不阻断仪表盘统计（概览已匿名开放）
            const results = await Promise.all([
                api.getDashboardStats(),
                api.getRecentTransactions().catch((_e: Error) => [] as RecentTransaction[]),
                api.getRegionalTrade().catch((_e: Error) => [] as RegionalTradeData[]),
                api.getMarketOverview().catch((_e: Error) => new FlowerMarketOverview())
            ]);
            this.stats = results[0];
            this.transactions = results[1];
            this.regional = results[2];
            const overview = results[3];

            this.tradeAmountText = HomeViewModel.fmtAmount(this.stats.todayTradeAmount);
            this.avgPriceText = `¥${overview.avgPrice.toFixed(2)}`;
            this.priceChangeText = `${overview.priceChange >= 0 ? '+' : ''}${overview.priceChange.toFixed(1)}%`;
            this.priceChangeUp = overview.priceChange >= 0;
            this.activeCountText = `${overview.activeSpeciesCount}`;
            this.alertCountText = `${overview.alertCount}`;
            this.lastUpdateText = this.fmtUpdate(overview.lastUpdateTime);

            this.buildTrends(overview);
            this.buildTxRows(overview);
            this.buildRegionRows();
        }
        catch (e) {
            this.errorText = (e as Error).message;
        }
        finally {
            this.loading = false;
        }
    }

    /** 金额友好格式（万/亿）*/
    static fmtAmount(v: number): string {
        if (v >= 100000000) {
            return `¥${(v / 100000000).toFixed(2)}亿`;
        }
        if (v >= 10000) {
            return `¥${(v / 10000).toFixed(2)}万`;
        }
        return `¥${v.toFixed(2)}`;
    }

    /** 热点品种：按涨跌幅绝对值优先，其次单价（周度数据波动为 0 时展示高价品种）*/
    private buildTrends(overview: FlowerMarketOverview): void {
        const sorted = overview.items.slice().sort((a: MarketSpeciesItem, b: MarketSpeciesItem) => {
            const da = Math.abs(a.changePercent);
            const db = Math.abs(b.changePercent);
            if (da !== db) {
                return db - da;
            }
            return b.avgPrice - a.avgPrice;
        });
        const trends: HomeTrendItem[] = [];
        const count = Math.min(6, sorted.length);
        for (let i = 0; i < count; i++) {
            const it = sorted[i];
            const visual = SPECIES_VISUALS[it.speciesId];
            const t = new HomeTrendItem();
            t.speciesId = it.speciesId;
            t.name = it.name;
            t.originGrade = `${it.origin} · ${it.grade}`;
            t.priceText = `¥${it.avgPrice.toFixed(2)}`;
            t.changePercent = it.changePercent;
            t.changeText = `${it.changePercent >= 0 ? '+' : ''}${it.changePercent.toFixed(1)}%`;
            t.changeUp = it.changePercent >= 0;
            t.emoji = visual ? visual.emoji : '🌸';
            t.gradStart = visual ? visual.start : '#5a90ff';
            t.gradEnd = visual ? visual.end : '#163380';
            trends.push(t);
        }
        this.trends = trends;
    }

    /** 最近交易行：按品种名匹配行情目录补视觉（emoji/渐变）*/
    private buildTxRows(overview: FlowerMarketOverview): void {
        const rows: HomeTxItem[] = [];
        for (const t of this.transactions) {
            const r = new HomeTxItem();
            r.name = t.speciesName;
            r.market = t.market;
            r.timeText = this.fmtTradeTime(t.tradeTime);
            r.priceText = `¥${t.price.toFixed(2)}`;
            r.qtyText = `×${t.quantity}`;
            const v = HomeViewModel.visualOfName(overview, t.speciesName);
            r.emoji = v ? v.emoji : '🌸';
            r.gradStart = v ? v.start : '#3a78ff';
            r.gradEnd = v ? v.end : '#1f4fd6';
            rows.push(r);
        }
        this.txRows = rows;
    }

    private static visualOfName(overview: FlowerMarketOverview, name: string): SpeciesVisual | null {
        for (const it of overview.items) {
            if (it.name === name && SPECIES_VISUALS[it.speciesId]) {
                return SPECIES_VISUALS[it.speciesId];
            }
        }
        return null;
    }

    /** 区域需求条：按最大指数归一化；指数 ≥1 视为高热（红）*/
    private buildRegionRows(): void {
        let maxIdx = 1;
        for (const r of this.regional) {
            if (r.demandIndex > maxIdx) {
                maxIdx = r.demandIndex;
            }
        }
        const rows: HomeRegionItem[] = [];
        for (const r of this.regional) {
            const item = new HomeRegionItem();
            item.regionName = r.regionName;
            item.indexText = r.demandIndex.toFixed(1);
            item.ratio = Math.max(0.08, Math.min(1, r.demandIndex / maxIdx));
            item.hot = r.demandIndex >= 1;
            rows.push(item);
        }
        this.regionRows = rows;
    }

    /** ISO 时间 → "x分钟前/x小时前/x天前" */
    private fmtUpdate(iso: string): string {
        if (iso.length === 0) {
            return '';
        }
        const t = Date.parse(iso);
        if (Number.isNaN(t)) {
            return '';
        }
        const diff = Math.max(0, Date.now() - t) / 60000;
        if (diff < 1) {
            return '刚刚';
        }
        if (diff < 60) {
            return `${Math.floor(diff)}分钟前`;
        }
        if (diff < 1440) {
            return `${Math.floor(diff / 60)}小时前`;
        }
        return `${Math.floor(diff / 1440)}天前`;
    }

    /** ISO 时间 → "MM-DD HH:mm" */
    private fmtTradeTime(iso: string): string {
        const t = Date.parse(iso);
        if (Number.isNaN(t)) {
            return iso;
        }
        const d = new Date(t);
        const pad = (n: number): string => `${n < 10 ? '0' : ''}${n}`;
        return `${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
    }
}
/** 市场商品卡（视图模型，所有展示字段预计算到第一层——避免 @Builder 传参不联动）*/
export class MarketItem {
    speciesId: number = 0;
    name: string = '';
    category: string = '';
    origin: string = '';
    /** "云南 · A级" */
    originGrade: string = '';
    /** "¥38.50" */
    priceText: string = '';
    changePercent: number = 0;
    /** "+2.3%" / "-1.2%" */
    changeText: string = '--';
    changeUp: boolean = true;
    volume: number = 0;
    /** 现货（true）/预订（false） */
    inStock: boolean = true;
    /** 头图渐变起止色 */
    gradStart: string = '#e11d48';
    gradEnd: string = '#881337';
    /** 头图中央 emoji */
    emoji: string = '🌸';
}

/** 库存预警卡（从成交量衍生：交易量低=供货紧张）*/
export class MarketAlert {
    speciesId: number = 0;
    name: string = '';
    /** 低库存预警 / 紧急补货 / 严重不足 / 建议补货 */
    level: string = '';
    /** true=红色（严重） false=橙色（一般） */
    severe: boolean = false;
    /** 剩余（扎） */
    remain: number = 0;
    /** 安全库存（扎） */
    safe: number = 0;
    /** 缺口（扎） */
    gap: number = 0;
    /** "5分钟前" */
    timeAgo: string = '';
}

/** 本地采购单（"立即下单/补货"落地，AppContext Preferences 持久化）*/
export class MarketOrder {
    orderId: string = '';
    speciesId: number = 0;
    name: string = '';
    emoji: string = '🌸';
    /** 单价（元/扎）*/
    unitPrice: number = 0;
    /** 数量（扎）*/
    quantity: number = 0;
    /** 总价（元）*/
    totalPrice: number = 0;
    /** 下单时间（ISO）*/
    createTime: string = '';
}

/** 价格走势图数据点（历史=price；预测含置信区间）*/
export interface ChartPoint {
    timeMs: number;
    price: number;
    lower: number;
    upper: number;
}

/** 品种视觉映射（头图渐变 + emoji，按 speciesId）*/
interface SpeciesVisual {
    start: string;
    end: string;
    emoji: string;
}
const SPECIES_VISUALS: Record<number, SpeciesVisual> = {
    2001: { start: '#e2e8f0', end: '#64748b', emoji: '🤍' },
    2002: { start: '#f8fafc', end: '#6b7280', emoji: '�' },
    2003: { start: '#f472b6', end: '#9d174d', emoji: '�' },
    2004: { start: '#fb923c', end: '#c2410c', emoji: '�' },
    2005: { start: '#ef4444', end: '#7f1d1d', emoji: '🫘' },
    2006: { start: '#e11d48', end: '#881337', emoji: '�' },
    2007: { start: '#a78bfa', end: '#5b21b6', emoji: '💠' },
    2008: { start: '#fb7185', end: '#9f1239', emoji: '💐' },
    2009: { start: '#f43f5e', end: '#881337', emoji: '�' },
    2010: { start: '#60a5fa', end: '#1e3a8a', emoji: '💙' },
    2011: { start: '#4ade80', end: '#166534', emoji: '🎋' },
    2012: { start: '#fef9c3', end: '#ca8a04', emoji: '✨' },
    2013: { start: '#fbbf24', end: '#b45309', emoji: '🌟' },
    2014: { start: '#f5f5f4', end: '#a8a29e', emoji: '🪷' },
    2015: { start: '#fda4af', end: '#be123c', emoji: '🌸' },
    2016: { start: '#facc15', end: '#a16207', emoji: '💛' },
    2017: { start: '#818cf8', end: '#3730a3', emoji: '�' },
    2018: { start: '#fcd34d', end: '#ea580c', emoji: '🌻' },
    2019: { start: '#fef9c3', end: '#d97706', emoji: '🌼' },
    2020: { start: '#818cf8', end: '#4338ca', emoji: '🫧' },
    2021: { start: '#fef3c7', end: '#f59e0b', emoji: '🌼' },
    2022: { start: '#c084fc', end: '#6b21a8', emoji: '🪻' }
};

/** 采购单持久化键（与天气收藏同一 Preferences 存储）*/
const KEY_MARKET_ORDERS = 'marketOrders';

/** 库存预警分级阈值（成交量=剩余库存，扎）*/
interface AlertRule {
    maxVolume: number;
    level: string;
    severe: boolean;
    safe: number;
}
const ALERT_RULES: AlertRule[] = [
    { maxVolume: 1000, level: '严重不足', severe: true, safe: 2000 },
    { maxVolume: 1500, level: '紧急补货', severe: true, safe: 3000 },
    { maxVolume: 2000, level: '低库存预警', severe: false, safe: 4000 },
    { maxVolume: 2500, level: '建议补货', severe: false, safe: 5000 }
];

@Observed
export class MarketViewModel {
    loading: boolean = false;
    errorText: string = '';
    overview: FlowerMarketOverview = new FlowerMarketOverview();
    species: FlowerSpeciesListItem[] = [];
    selectedSpeciesId: number = 0;
    forecast: FlowerPriceForecast | null = null;
    forecastLoading: boolean = false;
    forecastError: string = '';
    /** 品种价格历史（详情页用，近 45 天）*/
    history: FlowerPriceSnapshot[] = [];
    historyLoading: boolean = false;
    /** 最近 7 条历史（时间倒序，列表展示用）*/
    historyRecent: FlowerPriceSnapshot[] = [];
    /** 走势图数据（历史升序）*/
    chartHist: ChartPoint[] = [];
    /** 走势图数据（预测升序）*/
    chartForecast: ChartPoint[] = [];

    // ===== 本地采购单（Preferences 持久化）=====
    orders: MarketOrder[] = [];
    ordersLoading: boolean = false;

    // ===== 市场统计概览（页面 @State 镜像）=====
    avgPriceText: string = '--';
    priceChangeText: string = '--';
    priceChangeUp: boolean = true;
    activeCountText: string = '0';
    alertCountText: string = '0';

    // ===== 过滤条件 =====
    searchText: string = '';
    activeCategory: string = '全部';
    /** 0不限 1 ¥3以下 2 ¥3-5 3 ¥5-8 4 ¥8以上 */
    priceRange: number = 0;
    originFilter: string = '全部';
    /** 0默认 1涨幅优先 2跌幅优先 3价格从高到低 4成交量优先 */
    sortMode: number = 0;

    // ===== 视图数据（页面 @State 镜像）=====
    items: MarketItem[] = [];
    alerts: MarketAlert[] = [];
    categories: string[] = ['全部'];
    origins: string[] = ['全部'];
    displayItems: MarketItem[] = [];
    totalCountText: string = '共 0 个品种';
    updateTimeText: string = '';

    async load(): Promise<void> {
        this.loading = true;
        this.errorText = '';
        const api = ApiClient.getInstance();
        try {
            const results = await Promise.all([
                api.getMarketOverview(),
                api.getSpeciesList().catch((_e: Error) => [] as FlowerSpeciesListItem[])
            ]);
            this.overview = results[0];
            this.species = results[1];
            this.buildViewData();
            if (this.selectedSpeciesId === 0 && this.items.length > 0) {
                this.selectedSpeciesId = this.items[0].speciesId;
            }
        }
        catch (e) {
            this.errorText = (e as Error).message;
        }
        finally {
            this.loading = false;
        }
    }

    /** 从 overview.items 生成商品卡/预警卡/分类/产地视图数据 */
    private buildViewData(): void {
        const items: MarketItem[] = [];
        const categories: string[] = ['全部'];
        const origins: string[] = ['全部'];
        for (const it of this.overview.items) {
            const visual = SPECIES_VISUALS[it.speciesId];
            const item = new MarketItem();
            item.speciesId = it.speciesId;
            item.name = it.name;
            item.category = it.category;
            item.origin = it.origin;
            item.originGrade = `${it.origin} · ${it.grade}`;
            item.priceText = `¥${it.avgPrice.toFixed(2)}`;
            item.changePercent = it.changePercent;
            item.changeText = `${it.changePercent >= 0 ? '+' : ''}${it.changePercent.toFixed(1)}%`;
            item.changeUp = it.changePercent >= 0;
            item.volume = it.volume;
            // 成交量充足视为现货，偏低为预订（供货紧张需提前预订）
            item.inStock = it.volume >= 2000;
            item.gradStart = visual ? visual.start : '#5a90ff';
            item.gradEnd = visual ? visual.end : '#163380';
            item.emoji = visual ? visual.emoji : '🌸';
            items.push(item);
            if (!categories.includes(it.category)) {
                categories.push(it.category);
            }
            if (!origins.includes(it.origin)) {
                origins.push(it.origin);
            }
        }
        this.items = items;
        this.categories = categories;
        this.origins = origins;
        this.totalCountText = `共 ${items.length} 个品种`;
        this.updateTimeText = this.formatUpdateTime(this.overview.lastUpdateTime);
        // 市场统计概览（overview 头部字段）
        this.avgPriceText = `¥${this.overview.avgPrice.toFixed(2)}`;
        this.priceChangeText = `${this.overview.priceChange >= 0 ? '+' : ''}${this.overview.priceChange.toFixed(1)}%`;
        this.priceChangeUp = this.overview.priceChange >= 0;
        this.activeCountText = `${this.overview.activeSpeciesCount}`;
        this.alertCountText = `${this.overview.alertCount}`;
        this.buildAlerts();
        this.applyFilters();
    }

    /** 从成交量衍生库存预警（取最低的最多 4 条）*/
    private buildAlerts(): void {
        const candidates = this.items
            .slice()
            .sort((a: MarketItem, b: MarketItem) => a.volume - b.volume)
            .filter((it: MarketItem) => it.volume < 2500)
            .slice(0, 4);
        const alerts: MarketAlert[] = [];
        for (const it of candidates) {
            let rule: AlertRule | null = null;
            for (const r of ALERT_RULES) {
                if (it.volume < r.maxVolume) {
                    rule = r;
                    break;
                }
            }
            if (rule === null) {
                continue;
            }
            const alert = new MarketAlert();
            alert.speciesId = it.speciesId;
            alert.name = it.name;
            alert.level = rule.level;
            alert.severe = rule.severe;
            alert.remain = it.volume;
            alert.safe = rule.safe;
            alert.gap = rule.safe - it.volume;
            alert.timeAgo = this.formatUpdateTime(this.snapshotTimeOf(it.speciesId));
            alerts.push(alert);
        }
        this.alerts = alerts;
    }

    /** 取某品种快照时间（找得到返回字符串，否则空）*/
    private snapshotTimeOf(speciesId: number): string {
        for (const it of this.overview.items) {
            if (it.speciesId === speciesId) {
                return it.snapshotTime;
            }
        }
        return '';
    }

    /** ISO 时间 → "x分钟前/x小时前" */
    private formatUpdateTime(iso: string): string {
        if (iso.length === 0) {
            return '';
        }
        const t = Date.parse(iso);
        if (Number.isNaN(t)) {
            return '';
        }
        const diff = Math.max(0, Date.now() - t) / 60000;
        if (diff < 1) {
            return '刚刚';
        }
        if (diff < 60) {
            return `${Math.floor(diff)}分钟前`;
        }
        if (diff < 1440) {
            return `${Math.floor(diff / 60)}小时前`;
        }
        return `${Math.floor(diff / 1440)}天前`;
    }

    /** 按当前过滤/排序条件生成 displayItems（页面镜像刷新）*/
    applyFilters(): void {
        let list = this.items.slice();
        const keyword = this.searchText.trim();
        if (keyword.length > 0) {
            list = list.filter((it: MarketItem) =>
                it.name.includes(keyword) || it.origin.includes(keyword) || it.category.includes(keyword));
        }
        if (this.activeCategory !== '全部') {
            list = list.filter((it: MarketItem) => it.category === this.activeCategory);
        }
        if (this.originFilter !== '全部') {
            list = list.filter((it: MarketItem) => it.origin === this.originFilter);
        }
        if (this.priceRange === 1) {
            list = list.filter((it: MarketItem) => MarketViewModel.itemPrice(it) < 3);
        }
        else if (this.priceRange === 2) {
            list = list.filter((it: MarketItem) => {
                const p = MarketViewModel.itemPrice(it);
                return p >= 3 && p < 5;
            });
        }
        else if (this.priceRange === 3) {
            list = list.filter((it: MarketItem) => {
                const p = MarketViewModel.itemPrice(it);
                return p >= 5 && p < 8;
            });
        }
        else if (this.priceRange === 4) {
            list = list.filter((it: MarketItem) => MarketViewModel.itemPrice(it) >= 8);
        }
        if (this.sortMode === 1) {
            list.sort((a: MarketItem, b: MarketItem) => b.changePercent - a.changePercent);
        }
        else if (this.sortMode === 2) {
            list.sort((a: MarketItem, b: MarketItem) => a.changePercent - b.changePercent);
        }
        else if (this.sortMode === 3) {
            list.sort((a: MarketItem, b: MarketItem) => MarketViewModel.itemPrice(b) - MarketViewModel.itemPrice(a));
        }
        else if (this.sortMode === 4) {
            list.sort((a: MarketItem, b: MarketItem) => b.volume - a.volume);
        }
        else {
            list.sort((a: MarketItem, b: MarketItem) => a.speciesId - b.speciesId);
        }
        this.displayItems = list;
        this.totalCountText = `共 ${list.length} 个品种`;
    }

    /** 从 priceText 解析数值（过滤/排序用）*/
    private static itemPrice(it: MarketItem): number {
        const raw = Number(it.priceText.replace('¥', ''));
        return Number.isNaN(raw) ? 0 : raw;
    }

    async loadForecast(): Promise<void> {
        if (this.selectedSpeciesId <= 0) {
            return;
        }
        this.forecastLoading = true;
        this.forecastError = '';
        try {
            this.forecast = await ApiClient.getInstance().getPriceForecast(this.selectedSpeciesId, 7);
        }
        catch (e) {
            this.forecastError = (e as Error).message;
            this.forecast = null;
        }
        finally {
            this.forecastLoading = false;
        }
    }

    /** 品种详情（ForecastPage 参数化入口）：并行拉预测 + 近 45 天历史（朝阳数据周度发布，拉长窗口保证走势图有足够点）*/
    async loadDetail(speciesId: number): Promise<void> {
        this.selectedSpeciesId = speciesId;
        this.forecastLoading = true;
        this.historyLoading = true;
        this.forecastError = '';
        const api = ApiClient.getInstance();
        try {
            const results = await Promise.all([
                api.getPriceForecast(speciesId, 7).catch((_e: Error) => null),
                api.getPriceHistory(speciesId, 45).catch((_e: Error) => [] as FlowerPriceSnapshot[])
            ]);
            this.forecast = results[0];
            if (this.forecast === null) {
                this.forecastError = '预测数据加载失败';
            }
            this.history = results[1];
            this.buildDetailViewData();
        }
        finally {
            this.forecastLoading = false;
            this.historyLoading = false;
        }
    }

    /** 详情页视图数据：最近 7 条列表 + 走势图（历史升序 / 预测升序）*/
    private buildDetailViewData(): void {
        const sorted = this.history.slice()
            .sort((a: FlowerPriceSnapshot, b: FlowerPriceSnapshot) => a.snapshotTime.localeCompare(b.snapshotTime));
        this.historyRecent = sorted.slice().reverse().slice(0, 7);
        const hist: ChartPoint[] = [];
        for (const h of sorted) {
            const t = Date.parse(h.snapshotTime);
            if (Number.isNaN(t)) {
                continue;
            }
            const p: ChartPoint = { timeMs: t, price: h.avgPrice, lower: h.avgPrice, upper: h.avgPrice };
            hist.push(p);
        }
        this.chartHist = hist;
        const fc: ChartPoint[] = [];
        const predicted = this.forecast !== null ? this.forecast.predictedPrices : [];
        for (const p of predicted) {
            const t = Date.parse(p.date);
            if (Number.isNaN(t)) {
                continue;
            }
            fc.push({ timeMs: t, price: p.predictedPrice, lower: p.lowerBound, upper: p.upperBound });
        }
        this.chartForecast = fc;
    }

    // ===== 本地采购单 =====

    /** 读取本地采购单（Preferences JSON 数组，最新在前）*/
    async loadOrders(): Promise<void> {
        this.ordersLoading = true;
        try {
            const raw = await AppContext.getInstance().getPrefString(KEY_MARKET_ORDERS, '[]');
            const orders: MarketOrder[] = [];
            try {
                const arr = JSON.parse(raw) as object[];
                if (Array.isArray(arr)) {
                    for (const o of arr) {
                        const r = o as Record<string, Object>;
                        const order = new MarketOrder();
                        order.orderId = String(r['orderId'] ?? '');
                        order.speciesId = Number(r['speciesId'] ?? 0);
                        order.name = String(r['name'] ?? '');
                        order.emoji = String(r['emoji'] ?? '🌸');
                        order.unitPrice = Number(r['unitPrice'] ?? 0);
                        order.quantity = Number(r['quantity'] ?? 0);
                        order.totalPrice = Number(r['totalPrice'] ?? 0);
                        order.createTime = String(r['createTime'] ?? '');
                        orders.push(order);
                    }
                }
            }
            catch (_e) {
                // 损坏数据按空处理
            }
            this.orders = orders;
        }
        finally {
            this.ordersLoading = false;
        }
    }

    /** 新增采购单（最新在前，立即落盘）*/
    async addOrder(item: MarketItem, quantity: number, note: string): Promise<MarketOrder> {
        const order = new MarketOrder();
        order.orderId = `${Date.now()}-${Math.floor(Math.random() * 10000)}`;
        order.speciesId = item.speciesId;
        order.name = note.length > 0 ? `${item.name}（${note}）` : item.name;
        order.emoji = item.emoji;
        order.unitPrice = MarketViewModel.itemPrice(item);
        order.quantity = quantity;
        order.totalPrice = Math.round(order.unitPrice * quantity * 100) / 100;
        order.createTime = new Date().toISOString();
        this.orders = [order, ...this.orders];
        await this.saveOrders();
        return order;
    }

    /** 删除指定采购单 */
    async removeOrder(orderId: string): Promise<void> {
        this.orders = this.orders.filter((o: MarketOrder) => o.orderId !== orderId);
        await this.saveOrders();
    }

    /** 清空采购单 */
    async clearOrders(): Promise<void> {
        this.orders = [];
        await this.saveOrders();
    }

    private async saveOrders(): Promise<void> {
        await AppContext.getInstance().putPrefString(KEY_MARKET_ORDERS, JSON.stringify(this.orders));
    }
}
/** 收藏地点持久化键（AppContext 同一 Preferences 存储）*/
const KEY_FAV_PLACES = 'weatherFavPlaces';
/** 收藏上限（超出移除最旧）*/
const FAV_MAX = 8;

@Observed
export class WeatherViewModel {
    loading: boolean = false;
    errorText: string = '';
    city: string = '昆明';
    /** 快捷（收藏）地点列表：城市项无坐标走城市接口，自然村/区县含坐标走坐标直查 */
    favPlaces: WeatherPlace[] = WeatherViewModel.defaultFavs();
    /** 城市横滑栏温度（全量替换触发刷新）*/
    cityTemps: CityTemp[] = [];
    /** 全部城市（搜索用；动态接口优先，失败用内置兜底）*/
    cities: string[] = [
        '北京', '上海', '天津', '重庆', '昆明', '曲靖', '玉溪', '丽江', '大理',
        '广州', '深圳', '珠海', '佛山', '东莞', '杭州', '宁波', '温州', '南京',
        '苏州', '无锡', '成都', '绵阳', '武汉', '西安', '郑州', '长沙', '合肥',
        '福州', '厦门', '南昌', '济南', '青岛', '沈阳', '大连', '长春', '哈尔滨',
        '石家庄', '太原', '呼和浩特', '南宁', '海口', '三亚', '贵阳', '兰州',
        '西宁', '银川', '乌鲁木齐', '拉萨', '台北', '香港', '澳门'
    ];
    /** 完整当前天气 */
    current: WeatherCurrentFull | null = null;
    /** 完整预报 */
    forecast: WeatherForecastFull | null = null;
    /** 当前自然村/任意地点（非空=坐标直查模式）*/
    activePlace: WeatherPlace | null = null;
    /** 农事提醒 */
    alerts: AgriAlertItem[] = [];
    /** ===== 视图映射（load 完成后生成，页面直接绑定） ===== */
    /** 逐小时时间轴卡片 */
    hourlyShow: HourShow[] = [];
    /** 逐小时温度曲线数据 */
    hourlyTemps: number[] = [];
    /** 生活指数（两行 × 6 项）*/
    lifeItems: LifeIndexItem[] = [];
    /** 花卉培育总分 */
    lifeScore: string = '--';
    /** 日出/日落 */
    sunRise: string = '--:--';
    sunSet: string = '--:--';
    /** AQI 估算文本与颜色 */
    aqiText: string = '--';
    aqiColor: string = '#26b5a0';
    /** 当前天气 emoji 图标 */
    curEmoji: string = '⛅';
    /** 风向罗盘角度 */
    windAngle: number = 0;
    /** 罗盘下方风力文字 */
    windText: string = '--';
    /** 未来预报列表（ForEach 数据源，预计算避免链式绑定不刷新）*/
    dailyShow: WeatherDailyItem[] = [];
    /** 指标格预计算值（@Builder 按值传参不联动，须走 VM 第一层属性）*/
    metricWind: string = '--';
    metricApparent: string = '--°';
    metricUv: string = '--';
    metricPressure: string = '--hPa';
    metricCloud: string = '--%';
    metricRain: string = '--%';
    metricHum: string = '--%';
    /** 数据变化回调（页面重绘 Canvas 图表）*/
    onDataChanged: () => void = () => {
    };
    /** 收藏列表变化回调（页面同步 @State 本地镜像——ForEach 数据源不追踪 @Observed 属性替换）*/
    onFavChanged: () => void = () => {
    };

    async load(): Promise<void> {
        this.loading = true;
        this.errorText = '';
        const api = ApiClient.getInstance();
        try {
            const results = await Promise.all([
                api.getCurrentWeatherFull(this.city, this.activePlace),
                api.getWeatherForecastFull(this.city, 10, this.activePlace).catch((_e: Error) => null),
                api.getAgriAlerts(this.city, this.activePlace).catch((_e: Error) => [] as AgriAlertItem[])
            ]);
            this.current = results[0];
            this.forecast = results[1];
            this.alerts = results[2];
            this.buildViewData();
        }
        catch (e) {
            this.errorText = (e as Error).message;
            this.current = null;
            this.forecast = null;
        }
        finally {
            this.loading = false;
            this.onDataChanged();
        }
    }

    /** 切换城市（自动固定到快捷栏并重新加载；退出自然村坐标模式）*/
    switchCity(city: string): void {
        this.city = city;
        this.activePlace = null;
        this.ensureFavorite(city, null);
        this.load();
        this.loadCityTemps();
    }

    /** 切换到自然村/任意地点（坐标直查模式，自动固定到快捷栏）*/
    switchPlace(place: WeatherPlace): void {
        this.activePlace = place;
        this.city = place.name;
        this.ensureFavorite(place.name, place);
        this.load();
        this.loadCityTemps();
    }

    /** 拉取快捷地点温度（并行，全量重建数组；含坐标的走坐标直查）*/
    loadCityTemps(): void {
        const api = ApiClient.getInstance();
        const list: CityTemp[] = [];
        for (const p of this.favPlaces) {
            const ct = new CityTemp();
            ct.city = p.name;
            ct.temp = this.findTemp(p.name);
            list.push(ct);
        }
        this.cityTemps = list;
        const jobs: Promise<void>[] = [];
        for (const p of this.favPlaces) {
            if (this.findTemp(p.name).length > 0) {
                continue;
            }
            const placeArg: WeatherPlace | null = (p.lat !== 0 && p.lon !== 0) ? p : null;
            jobs.push(api.getCurrentWeatherFull(p.name, placeArg).then((full: WeatherCurrentFull) => {
                const t = new CityTemp();
                t.city = p.name;
                t.temp = `${Math.round(full.temperature)}°`;
                this.updateTemp(t);
            }).catch((_e: Error) => {
            }));
        }
        if (jobs.length > 0) {
            Promise.all(jobs).then(() => {
                this.onDataChanged();
            });
        }
    }

    // ==================== 快捷（收藏）地点管理 ====================

    /** 默认快捷城市（首次启动/无持久化数据时使用）*/
    private static defaultFavs(): WeatherPlace[] {
        const names: string[] = ['昆明', '北京', '上海', '广州'];
        const list: WeatherPlace[] = [];
        for (const n of names) {
            const p = new WeatherPlace();
            p.name = n;
            p.kind = 'city';
            list.push(p);
        }
        return list;
    }

    /** 从本地恢复收藏地点（无数据保持默认），完成后刷新横滑栏温度 */
    async restoreFavorites(): Promise<void> {
        try {
            const raw = await AppContext.getInstance().getPrefString(KEY_FAV_PLACES, '');
            if (raw.length > 0) {
                const parsed = JSON.parse(raw) as Array<Record<string, Object>>;
                if (Array.isArray(parsed)) {
                    const list: WeatherPlace[] = [];
                    for (const item of parsed) {
                        if (item === null || typeof item !== 'object') {
                            continue;
                        }
                        const p = new WeatherPlace();
                        p.name = (item['name'] as string) ?? '';
                        p.province = (item['province'] as string) ?? '';
                        p.city = (item['city'] as string) ?? '';
                        p.lat = Number(item['lat'] ?? 0);
                        p.lon = Number(item['lon'] ?? 0);
                        p.kind = (item['kind'] as string) ?? '';
                        if (p.name.length > 0) {
                            list.push(p);
                        }
                    }
                    if (list.length > 0) {
                        this.favPlaces = list;
                        this.onFavChanged();
                    }
                }
            }
        }
        catch (e) {
            hilog.error(0x0000, 'GengDi.WeatherVM', 'restoreFavorites failed: %{public}s', `${e}`);
        }
        this.loadCityTemps();
    }

    /** 按名称查收藏下标（-1 未收藏）*/
    private indexOfFavorite(name: string): number {
        for (let i = 0; i < this.favPlaces.length; i++) {
            if (this.favPlaces[i].name === name) {
                return i;
            }
        }
        return -1;
    }

    /** 是否已收藏 */
    isFavorite(name: string): boolean {
        return this.indexOfFavorite(name) >= 0;
    }

    /** 确保地点在快捷栏（place 为 null 表示纯城市项，无坐标）*/
    private ensureFavorite(name: string, place: WeatherPlace | null): void {
        if (this.indexOfFavorite(name) >= 0) {
            return;
        }
        const item = place ?? new WeatherPlace();
        if (place === null) {
            item.name = name;
            item.kind = 'city';
        }
        const list: WeatherPlace[] = [];
        for (const p of this.favPlaces) {
            list.push(p);
        }
        list.push(item);
        while (list.length > FAV_MAX) {
            list.shift();
        }
        this.favPlaces = list;
        this.onFavChanged();
        this.saveFavorites();
    }

    /** 切换收藏状态：未收藏则固定（返回 true），已收藏则取消（返回 false）*/
    toggleFavorite(place: WeatherPlace): boolean {
        const idx = this.indexOfFavorite(place.name);
        if (idx >= 0) {
            this.removeFavoriteAt(idx);
            return false;
        }
        this.ensureFavorite(place.name, place);
        this.loadCityTemps();
        return true;
    }

    /** 移除收藏（横滑栏长按触发）*/
    removeFavorite(name: string): void {
        const idx = this.indexOfFavorite(name);
        if (idx >= 0) {
            this.removeFavoriteAt(idx);
        }
    }

    /** 获取收藏列表副本（供页面 @State 镜像绑定 ForEach）*/
    getFavPlaces(): WeatherPlace[] {
        const list: WeatherPlace[] = [];
        for (const p of this.favPlaces) {
            list.push(p);
        }
        return list;
    }

    private removeFavoriteAt(idx: number): void {
        const list: WeatherPlace[] = [];
        for (let i = 0; i < this.favPlaces.length; i++) {
            if (i !== idx) {
                list.push(this.favPlaces[i]);
            }
        }
        this.favPlaces = list;
        this.onFavChanged();
        this.saveFavorites();
        this.loadCityTemps();
    }

    /** 持久化收藏（失败不影响内存态）*/
    private saveFavorites(): void {
        AppContext.getInstance().putPrefString(KEY_FAV_PLACES, JSON.stringify(this.favPlaces))
            .catch((_e: Error) => {
            });
    }

    private findTemp(city: string): string {
        for (const ct of this.cityTemps) {
            if (ct.city === city) {
                return ct.temp;
            }
        }
        return '';
    }

    private updateTemp(item: CityTemp): void {
        const list: CityTemp[] = [];
        for (const p of this.favPlaces) {
            if (p.name === item.city) {
                list.push(item);
            }
            else {
                const ct = new CityTemp();
                ct.city = p.name;
                ct.temp = this.findTemp(p.name);
                list.push(ct);
            }
        }
        this.cityTemps = list;
    }

    /** 动态加载城市列表（失败保持内置兜底）*/
    async loadCities(): Promise<void> {
        try {
            const list = await ApiClient.getInstance().getWeatherCities();
            if (list.length > 0) {
                this.cities = list;
            }
        }
        catch (_e) {
        }
    }

    /** 搜索城市（模糊匹配）*/
    searchCities(keyword: string): string[] {
        const kw = keyword.trim();
        if (kw.length === 0) {
            return [];
        }
        return this.cities.filter((c: string) => c.indexOf(kw) >= 0);
    }

    /** 数据 → 视图映射（emoji/指数计算/日出日落等）*/
    private buildViewData(): void {
        const cur = this.current;
        const fc = this.forecast;
        if (cur === null) {
            return;
        }
        this.curEmoji = WeatherHelper.weatherEmoji(cur.wmoCode, cur.isDay);
        this.windText = `${cur.windDirection} ${WeatherHelper.windLevel(cur.windSpeed)}`;
        this.windAngle = WeatherHelper.windAngle(cur.windDirection);

        // AQI 估算
        const aq = WeatherHelper.estimateAqi(cur.uvIndex, cur.humidity);
        this.aqiText = `${aq <= 50 ? '优' : (aq <= 100 ? '良' : '轻度')} ${aq}`;
        this.aqiColor = aq <= 50 ? '#26b5a0' : (aq <= 100 ? '#ffd54a' : '#ff6b6b');

        // 日出日落
        if (fc !== null && fc.daily.length > 0) {
            this.sunRise = WeatherHelper.hhmm(fc.daily[0].sunrise);
            this.sunSet = WeatherHelper.hhmm(fc.daily[0].sunset);
        }

        // 逐小时：从当前时刻起 24 个
        if (fc !== null && fc.hourly.length > 0) {
            const nowHour = new Date().getHours();
            let first = 0;
            for (let i = 0; i < fc.hourly.length; i++) {
                if (fc.hourly[i].hour >= nowHour) {
                    first = i;
                    break;
                }
            }
            const shows: HourShow[] = [];
            const temps: number[] = [];
            const count = Math.min(24, fc.hourly.length - first);
            for (let j = 0; j < count; j++) {
                const h = fc.hourly[first + j];
                const s = new HourShow();
                s.label = j === 0 ? '现在' : `${h.hour}时`;
                s.emoji = WeatherHelper.weatherEmoji(h.wmoCode, h.hour >= 6 && h.hour < 19);
                s.temp = `${h.temperature}°`;
                s.rainP = h.precipitation > 0 ? `${h.precipitation}%` : '';
                shows.push(s);
                temps.push(h.temperature);
            }
            this.hourlyShow = shows;
            this.hourlyTemps = temps;
        }

        // 生活指数（12 项）
        if (fc !== null) {
            this.buildLifeItems(fc);
        }

        // 未来预报列表（ForEach 数据源）
        if (fc !== null) {
            this.dailyShow = fc.daily.slice(0, 7);
        } else {
            this.dailyShow = [];
        }

        // 指标格预计算（供 @Component 子组件 @Prop 绑定，避免 @Builder 按值传参不刷新）
        this.metricWind = WeatherHelper.windLevel(cur.windSpeed);
        this.metricApparent = `${cur.apparentTemperature}°`;
        this.metricUv = `${Math.round(cur.uvIndex)}`;
        this.metricPressure = `${Math.round(cur.pressure)}hPa`;
        this.metricCloud = `${Math.round(cur.cloudCover)}%`;
        this.metricHum = `${cur.humidity}%`;
        this.metricRain = this.currentRainP(fc);
    }

    /** 当前时刻降水概率（指标格用）*/
    private currentRainP(fc: WeatherForecastFull | null): string {
        if (fc !== null && fc.hourly.length > 0) {
            const nowHour = new Date().getHours();
            for (const h of fc.hourly) {
                if (h.hour >= nowHour) {
                    return `${h.precipitation}%`;
                }
            }
            return `${fc.hourly[0].precipitation}%`;
        }
        return '--%';
    }

    private buildLifeItems(fc: WeatherForecastFull): void {
        const d = fc.current;
        const temp = d.temperature;
        const hum = d.humidity;
        const uv = d.uvIndex;
        const pressure = d.pressure > 0 ? d.pressure : 1010;
        let rainP = 30;
        if (fc.hourly.length > 0) {
            rainP = fc.hourly[0].precipitation;
        }
        const calmWind = d.windSpeed < 20;

        const sTemp = WeatherHelper.scoreTemp(temp);
        const sHum = WeatherHelper.scoreHum(hum);
        const sSun = WeatherHelper.scoreSun(uv);
        const sRain = WeatherHelper.scoreRain(rainP);
        this.lifeScore = `${Math.round((sTemp + sHum + sSun + sRain) / 4)}`;

        const items: LifeIndexItem[] = [];
        // 1 穿衣
        items.push(WeatherHelper.life('👕', '穿衣指数',
            temp >= 28 ? '炎热' : (temp >= 22 ? '舒适' : (temp >= 15 ? '套装' : (temp >= 8 ? '夹衣' : '棉衣')))));
        // 2 运动
        items.push(WeatherHelper.life('🏃', '运动指数',
            (temp >= 15 && temp <= 28 && rainP <= 40) ? '适宜' : '不太适宜'));
        // 3 紫外线
        items.push(WeatherHelper.life('☀️', '紫外线指数',
            uv >= 8 ? '很强' : (uv >= 6 ? '强' : (uv >= 3 ? '中等' : '弱'))));
        // 4 感冒
        items.push(WeatherHelper.life('🤧', '感冒指数',
            (temp <= 10 || hum >= 85) ? '易发' : '少发'));
        // 5 浇花（花卉特色）
        items.push(WeatherHelper.life('💧', '浇花指数',
            (hum < 40 && rainP <= 30 && temp >= 10 && temp <= 32) ? '适宜浇水' : '暂缓'));
        // 6 晾晒
        items.push(WeatherHelper.life('🌬️', '晾晒指数',
            (rainP <= 20 && hum <= 60 && calmWind) ? '适宜晾晒' : '不宜'));
        // 7 洗车
        items.push(WeatherHelper.life('🚗', '洗车指数', rainP <= 20 ? '适宜洗车' : '不宜洗车'));
        // 8 钓鱼
        items.push(WeatherHelper.life('🎣', '钓鱼指数', (pressure >= 1000 && calmWind) ? '较适宜' : '一般'));
        // 9 旅游
        items.push(WeatherHelper.life('✈️', '旅游指数',
            (rainP <= 40 && temp >= 12 && temp <= 30) ? '适宜出游' : '注意天气'));
        // 10 过敏（花卉特色）
        items.push(WeatherHelper.life('🌸', '过敏指数', (hum < 40 || uv >= 7) ? '较易发' : '不易发'));
        // 11 空调开启
        items.push(WeatherHelper.life('❄️', '空调开启指数',
            temp >= 30 ? '长时间开启' : (temp >= 26 ? '部分开启' : '较少开启')));
        // 12 化妆
        items.push(WeatherHelper.life('💄', '化妆指数',
            hum < 40 ? '干燥补水' : (hum > 80 ? '控油' : '滋润')));
        this.lifeItems = items;
    }
}

/** 城市温度对（城市横滑栏）*/
export class CityTemp {
    city: string = '';
    temp: string = '';
}
/** 逐小时时间轴展示项 */
export class HourShow {
    label: string = '';
    emoji: string = '';
    temp: string = '';
    rainP: string = '';
}
/** 生活指数展示项 */
export class LifeIndexItem {
    icon: string = '';
    name: string = '';
    tag: string = '';
}

/** 天气工具：emoji 映射 / 风级 / 指数计算 */
export class WeatherHelper {
    /** WMO 天气码 → emoji 图标 */
    static weatherEmoji(code: number, isDay: boolean): string {
        if (code === 0) {
            return isDay ? '☀️' : '🌙';
        }
        if (code === 1 || code === 2) {
            return isDay ? '🌤️' : '🌙';
        }
        if (code === 3) {
            return '☁️';
        }
        if (code === 45 || code === 48) {
            return '🌫️';
        }
        if (code >= 51 && code <= 57) {
            return '🌦️';
        }
        if (code >= 61 && code <= 67) {
            return '🌧️';
        }
        if (code >= 71 && code <= 77) {
            return '🌨️';
        }
        if (code >= 80 && code <= 82) {
            return '🌧️';
        }
        if (code === 85 || code === 86) {
            return '🌨️';
        }
        if (code >= 95) {
            return '⛈️';
        }
        return '🌤️';
    }

    /** 风速（km/h）→ 风级 */
    static windLevel(speed: number): string {
        if (speed < 1) {
            return '0级';
        }
        if (speed < 6) {
            return '1级';
        }
        if (speed < 12) {
            return '2级';
        }
        if (speed < 20) {
            return '3级';
        }
        if (speed < 29) {
            return '4级';
        }
        if (speed < 39) {
            return '5级';
        }
        if (speed < 50) {
            return '6级';
        }
        if (speed < 62) {
            return '7级';
        }
        if (speed < 75) {
            return '8级';
        }
        return '9级+';
    }

    /** 风向文字 → 罗盘角度 */
    static windAngle(dirText: string): number {
        const map: Record<string, number> = {
            '北风': 0, '东北风': 45, '东风': 90, '东南风': 135,
            '南风': 180, '西南风': 225, '西风': 270, '西北风': 315
        };
        const v = map[dirText];
        return v !== undefined ? v : 0;
    }

    /** ISO 时间 → HH:mm */
    static hhmm(time: string): string {
        if (time.length === 0) {
            return '--:--';
        }
        const t = time.indexOf('T');
        if (t >= 0 && time.length >= t + 6) {
            return time.substring(t + 1, t + 6);
        }
        if (time.length >= 5) {
            return time.substring(0, 5);
        }
        return '--:--';
    }

    /** AQI 估算（无实测 AQI 数据源时按紫外线/湿度估算）*/
    static estimateAqi(uvIndex: number, humidity: number): number {
        let aq = 45;
        if (uvIndex >= 7) {
            aq = 38;
        }
        if (humidity >= 85) {
            aq = 70;
        }
        return aq;
    }

    static scoreTemp(t: number): number {
        if (t >= 15 && t <= 28) {
            return 100;
        }
        if (t < 5 || t > 35) {
            return 30;
        }
        if (t < 10 || t > 30) {
            return 50;
        }
        return 75;
    }

    static scoreHum(h: number): number {
        if (h >= 50 && h <= 80) {
            return 100;
        }
        if (h < 30 || h > 90) {
            return 40;
        }
        return 75;
    }

    static scoreSun(uv: number): number {
        if (uv >= 4 && uv <= 7) {
            return 100;
        }
        if (uv < 2 || uv > 9) {
            return 50;
        }
        return 80;
    }

    static scoreRain(p: number): number {
        if (p <= 30) {
            return 100;
        }
        if (p <= 60) {
            return 70;
        }
        return 40;
    }

    static life(icon: string, name: string, tag: string): LifeIndexItem {
        const it = new LifeIndexItem();
        it.icon = icon;
        it.name = name;
        it.tag = tag;
        return it;
    }
}
/** 发现页供需行（供/需双色条 + 比值状态）*/
export class DiscoverSupplyItem {
    speciesName: string = '';
    supply: number = 0;
    demand: number = 0;
    /** 供给条宽比例（0~1，按行内供需较大者归一化）*/
    supplyBar: number = 0;
    /** 需求条宽比例（0~1）*/
    demandBar: number = 0;
    ratioText: string = '';
    /** 0=供不应求(机会) 1=平衡 2=供过于求(承压) */
    ratioState: number = 1;
}

/** 发现页区域热度行（排名徽章 + 渐变条）*/
export class DiscoverRegionItem {
    rank: number = 0;
    regionName: string = '';
    indexText: string = '';
    barRatio: number = 0;
    hot: boolean = false;
}

/** 发现页涨跌风向行（品种渐变徽章 + 价格 + 涨跌）*/
export class DiscoverRankItem {
    speciesId: number = 0;
    name: string = '';
    originGrade: string = '';
    priceText: string = '';
    changeText: string = '';
    changeUp: boolean = true;
    emoji: string = '🌸';
    gradStart: string = '#3a78ff';
    gradEnd: string = '#1f4fd6';
}

/** 发现页花语卡（轮播）*/
export class DiscoverFlowerCard {
    speciesId: number = 0;
    name: string = '';
    origin: string = '';
    meaning: string = '';
    emoji: string = '🌸';
    gradStart: string = '#ec4899';
    gradEnd: string = '#be185d';
    priceText: string = '--';
    changeText: string = '--';
    changeUp: boolean = true;
}

/** 发现页成交速递行（近期成交流水）*/
export class DiscoverTxItem {
    speciesName: string = '';
    market: string = '';
    timeText: string = '';
    priceText: string = '';
    qtyText: string = '';
    emoji: string = '🌸';
}

/** 品种花语（本地静态知识，按 speciesId 对齐斗南目录 2001~2022）*/
const FLOWER_MEANINGS: Record<number, string> = {
    2001: '高洁 · 真情长存',
    2002: '纯洁天真 · 唯一的爱',
    2003: '初恋 · 铭记于心',
    2004: '互敬互爱 · 不畏艰难',
    2005: '相思 · 心心相印',
    2006: '热恋 · 一往情深',
    2007: '永恒不变的爱',
    2008: '温馨祝福 · 感恩',
    2009: '热烈的爱 · 伟大母爱',
    2010: '互信的心 · 把握幸福',
    2011: '节节高升 · 长青',
    2012: '甘做配角 · 思念纯真',
    2013: '圆满团圆 · 长久',
    2014: '纯洁庄严 · 心想事成',
    2015: '清纯高雅 · 甜蜜',
    2016: '感激快乐 · 早日康复',
    2017: '永恒的记忆 · 不要忘记我',
    2018: '沉默的爱 · 忠诚追随',
    2019: '深藏在心底的爱',
    2020: '希望 · 美满团圆',
    2021: '逆境中的坚强 · 愈挫愈勇',
    2022: '永恒的美与爱 · 质朴'
};

@Observed
export class DiscoverViewModel {
    loading: boolean = false;
    errorText: string = '';
    supplyDemand: SupplyDemandData[] = [];
    regional: RegionalTradeData[] = [];
    transactions: RecentTransaction[] = [];

    // ===== 头部市场概览统计（预计算）=====
    avgPriceText: string = '--';
    priceChangeText: string = '--';
    priceChangeUp: boolean = true;
    speciesCountText: string = '--';
    alertCountText: string = '--';
    updateTimeText: string = '';

    // ===== 预计算视图数据 =====
    supplyRows: DiscoverSupplyItem[] = [];
    regionRows: DiscoverRegionItem[] = [];
    rankRows: DiscoverRankItem[] = [];
    flowerCards: DiscoverFlowerCard[] = [];
    txRows: DiscoverTxItem[] = [];

    /** 行情概览缓存（"换一批"本地重建花语卡用，不发请求）*/
    private overviewCache: FlowerMarketOverview | null = null;
    /** 花语卡轮换偏移（每次"换一批"递增一组）*/
    private cardOffset: number = 0;

    async load(): Promise<void> {
        this.loading = true;
        this.errorText = '';
        const api = ApiClient.getInstance();
        try {
            const results = await Promise.all([
                api.getSupplyDemand().catch((_e: Error) => [] as SupplyDemandData[]),
                api.getRegionalTrade().catch((_e: Error) => [] as RegionalTradeData[]),
                api.getMarketOverview().catch((_e: Error) => new FlowerMarketOverview()),
                api.getRecentTransactions().catch((_e: Error) => [] as RecentTransaction[])
            ]);
            this.supplyDemand = results[0];
            this.regional = results[1];
            const overview = results[2];
            this.overviewCache = overview;
            this.transactions = results[3];

            this.buildOverviewStats(overview);
            this.buildSupplyRows();
            this.buildRegionRows(overview);
            this.buildRankRows(overview);
            this.buildTxRows(overview);
            this.buildFlowerCards(overview);

            if (this.supplyDemand.length === 0 && this.regional.length === 0
                && this.flowerCards.length === 0 && this.txRows.length === 0) {
                this.errorText = '暂无发现内容';
            }
        }
        catch (e) {
            this.errorText = (e as Error).message;
        }
        finally {
            this.loading = false;
        }
    }

    /** 头部概览统计：均价/涨跌/在售品种/预警数 + 更新时间 */
    private buildOverviewStats(overview: FlowerMarketOverview): void {
        this.avgPriceText = `¥${overview.avgPrice.toFixed(2)}`;
        this.priceChangeText = `${overview.priceChange >= 0 ? '+' : ''}${overview.priceChange.toFixed(1)}%`;
        this.priceChangeUp = overview.priceChange >= 0;
        this.speciesCountText = `${overview.activeSpeciesCount}`;
        this.alertCountText = `${overview.alertCount}`;
        const t = Date.parse(overview.lastUpdateTime);
        if (!Number.isNaN(t)) {
            const d = new Date(t);
            const pad = (n: number): string => `${n < 10 ? '0' : ''}${n}`;
            this.updateTimeText = `${pad(d.getHours())}:${pad(d.getMinutes())} 更新`;
        }
        else {
            this.updateTimeText = '';
        }
    }

    /** 成交速递行：前 5 条，按品种名匹配行情目录补视觉 emoji */
    private buildTxRows(overview: FlowerMarketOverview): void {
        const rows: DiscoverTxItem[] = [];
        const count = Math.min(5, this.transactions.length);
        for (let i = 0; i < count; i++) {
            const t = this.transactions[i];
            const r = new DiscoverTxItem();
            r.speciesName = t.speciesName;
            r.market = t.market;
            r.priceText = `¥${t.price.toFixed(2)}`;
            r.qtyText = `×${t.quantity}`;
            const ts = Date.parse(t.tradeTime);
            if (!Number.isNaN(ts)) {
                const d = new Date(ts);
                const pad = (n: number): string => `${n < 10 ? '0' : ''}${n}`;
                r.timeText = `${pad(d.getHours())}:${pad(d.getMinutes())}`;
            }
            else {
                r.timeText = '--:--';
            }
            r.emoji = this.visualEmojiOfName(overview, t.speciesName);
            rows.push(r);
        }
        this.txRows = rows;
    }

    /** 按品种名在行情目录中查视觉 emoji（无匹配回退 🌸）*/
    private visualEmojiOfName(overview: FlowerMarketOverview, name: string): string {
        for (const it of overview.items) {
            if (it.name === name && SPECIES_VISUALS[it.speciesId]) {
                return SPECIES_VISUALS[it.speciesId].emoji;
            }
        }
        return '🌸';
    }

    /** 供需双色条：按行内供/需较大者归一化；比值状态 0机会/1平衡/2承压 */
    private buildSupplyRows(): void {
        const rows: DiscoverSupplyItem[] = [];
        for (const s of this.supplyDemand) {
            const r = new DiscoverSupplyItem();
            r.speciesName = s.speciesName;
            r.supply = s.supply;
            r.demand = s.demand;
            const max = Math.max(s.supply, s.demand, 1);
            r.supplyBar = Math.max(0.04, s.supply / max);
            r.demandBar = Math.max(0.04, s.demand / max);
            r.ratioText = s.supplyDemandRatio.toFixed(2);
            if (s.supplyDemandRatio < 0.8) {
                r.ratioState = 0;
            } else if (s.supplyDemandRatio > 1.2) {
                r.ratioState = 2;
            }
            rows.push(r);
        }
        this.supplyRows = rows;
    }

    /** 区域热度：按需求指数降序排名；条宽按最大指数归一化 */
    private buildRegionRows(_overview: FlowerMarketOverview): void {
        const sorted = this.regional.slice().sort((a: RegionalTradeData, b: RegionalTradeData) => b.demandIndex - a.demandIndex);
        let maxIdx = 1;
        for (const r of sorted) {
            if (r.demandIndex > maxIdx) {
                maxIdx = r.demandIndex;
            }
        }
        const rows: DiscoverRegionItem[] = [];
        for (let i = 0; i < sorted.length; i++) {
            const src = sorted[i];
            const item = new DiscoverRegionItem();
            item.rank = i + 1;
            item.regionName = src.regionName;
            item.indexText = src.demandIndex.toFixed(1);
            item.barRatio = Math.max(0.08, Math.min(1, src.demandIndex / maxIdx));
            item.hot = src.demandIndex >= 1;
            rows.push(item);
        }
        this.regionRows = rows;
    }

    /** 价格风向标：按涨跌幅绝对值取前 5 */
    private buildRankRows(overview: FlowerMarketOverview): void {
        const sorted = overview.items.slice().sort((a: MarketSpeciesItem, b: MarketSpeciesItem) => Math.abs(b.changePercent) - Math.abs(a.changePercent));
        const rows: DiscoverRankItem[] = [];
        const count = Math.min(5, sorted.length);
        for (let i = 0; i < count; i++) {
            const it = sorted[i];
            const visual = SPECIES_VISUALS[it.speciesId];
            const r = new DiscoverRankItem();
            r.speciesId = it.speciesId;
            r.name = it.name;
            r.originGrade = `${it.origin} · ${it.grade}`;
            r.priceText = `¥${it.avgPrice.toFixed(2)}`;
            r.changeText = `${it.changePercent >= 0 ? '+' : ''}${it.changePercent.toFixed(1)}%`;
            r.changeUp = it.changePercent >= 0;
            r.emoji = visual ? visual.emoji : '🌸';
            r.gradStart = visual ? visual.start : '#3a78ff';
            r.gradEnd = visual ? visual.end : '#1f4fd6';
            rows.push(r);
        }
        this.rankRows = rows;
    }

    /** 今日花语：起始位 = 按天自动轮换 + "换一批"手动偏移，取 4 个品种（视觉 + 价格 + 涨跌 + 花语）*/
    private buildFlowerCards(overview: FlowerMarketOverview): void {
        const cards: DiscoverFlowerCard[] = [];
        const total = overview.items.length;
        if (total > 0) {
            // 天数因子实现每日一组；cardOffset 每次"换一批"加一组（×4 跳过整组）
            const start = (Math.floor(Date.now() / 86400000) + this.cardOffset * 4) % total;
            const count = Math.min(4, total);
            for (let i = 0; i < count; i++) {
                const it = overview.items[(start + i) % total];
                const visual = SPECIES_VISUALS[it.speciesId];
                const c = new DiscoverFlowerCard();
                c.speciesId = it.speciesId;
                c.name = it.name;
                c.origin = it.origin;
                c.meaning = FLOWER_MEANINGS[it.speciesId] ?? '美好 · 生机盎然';
                c.emoji = visual ? visual.emoji : '🌸';
                c.gradStart = visual ? visual.start : '#ec4899';
                c.gradEnd = visual ? visual.end : '#be185d';
                c.priceText = `¥${it.avgPrice.toFixed(2)}/扎`;
                c.changeText = `${it.changePercent >= 0 ? '+' : ''}${it.changePercent.toFixed(1)}%`;
                c.changeUp = it.changePercent >= 0;
                cards.push(c);
            }
        }
        this.flowerCards = cards;
    }

    /** "换一批"：本地递增偏移并重建花语卡（复用概览缓存，不发网络请求）*/
    rotateFlowerCards(): void {
        this.cardOffset++;
        if (this.overviewCache !== null) {
            this.buildFlowerCards(this.overviewCache);
        }
    }
}
