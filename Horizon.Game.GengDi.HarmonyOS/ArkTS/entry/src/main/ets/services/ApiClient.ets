import http from "@ohos.net.http";
import hilog from "@ohos.hilog";
import util from "@ohos.util";
import fs from '@ohos.file.fs';
import { AppContext, LoginSession } from "../model/AppContext";
/**
 * 强类型 WebAPI 客户端（单例）。
 * 统一 ResultVM<T> 解包与错误处理，供原生页面 ViewModel 使用。
 * JSON 为 camelCase（ASP.NET Core 默认）。
 */
const TAG = 'GengDi.ApiClient';
const TIMEOUT = 15000;
/** 支付/下单长超时：服务端订单校验含多次DB操作（库存/归属/金额），首请求需更长处理时间 */
const PAY_TIMEOUT = 45000;
/** 后端统一响应包装（Horizon.Share ResultVM） */
interface ResultVM<T> {
    code: number;
    errorMessage: string | null;
    isSuccess: boolean;
    data: T | null;
}
/** 客户端设备上下文（对齐 C# RegisterRequest.GameContext） */
interface ClientGameContext {
    GameId: number;
    ServerId: number;
    AreaId: number;
    Ip: string;
    PlatformId: string;
}
// ===== DTO（与 Horizon.WebApi 控制器对齐）=====
export class FlowerPriceSnapshot {
    speciesId: number = 0;
    marketId: number = 0;
    avgPrice: number = 0;
    minPrice: number = 0;
    maxPrice: number = 0;
    volume: number = 0;
    tradeCount: number = 0;
    snapshotTime: string = '';
}
export class FlowerMarketOverview {
    avgPrice: number = 0;
    priceChange: number = 0;
    alertCount: number = 0;
    activeSpeciesCount: number = 0;
    lastUpdateTime: string = '';
    snapshots: FlowerPriceSnapshot[] = [];
    items: MarketSpeciesItem[] = [];
}
/** 市场品种条目（后端 FlowerMarketSpeciesItemDto：含目录元数据与涨跌幅）*/
export class MarketSpeciesItem {
    speciesId: number = 0;
    name: string = '';
    category: string = '';
    origin: string = '';
    grade: string = '';
    avgPrice: number = 0;
    minPrice: number = 0;
    maxPrice: number = 0;
    volume: number = 0;
    tradeCount: number = 0;
    snapshotTime: string = '';
    changePercent: number = 0;
}
export class FlowerSpeciesListItem {
    id: number = 0;
    name: string = '';
}
export class DashboardStats {
    todayTradeAmount: number = 0;
    tradeCount: number = 0;
    activeSpeciesCount: number = 0;
    onlineMerchantCount: number = 0;
}
export class RegionalTradeData {
    regionName: string = '';
    demandIndex: number = 0;
}
export class SupplyDemandData {
    speciesName: string = '';
    supply: number = 0;
    demand: number = 0;
    supplyDemandRatio: number = 0;
}
export class RecentTransaction {
    tradeTime: string = '';
    speciesName: string = '';
    price: number = 0;
    quantity: number = 0;
    market: string = '';
}
export class PredictedPricePoint {
    date: string = '';
    predictedPrice: number = 0;
    lowerBound: number = 0;
    upperBound: number = 0;
}
export class FlowerPriceForecast {
    speciesId: number = 0;
    marketId: number = 0;
    predictedPrices: PredictedPricePoint[] = [];
    timeScale: number = 0;
    modelVersion: string = '';
    confidence: number = 0;
    generatedAt: string = '';
}
export class WeatherNow {
    city: string = '';
    temperature: number = 0;
    description: string = '';
    humidity: number = 0;
    windSpeed: number = 0;
}
export class WeatherDay {
    date: string = '';
    dayWeather: string = '';
    nightWeather: string = '';
    highTemp: number = 0;
    lowTemp: number = 0;
}
/** 完整当前天气（后端 WeatherCurrentDto）*/
export class WeatherCurrentFull {
    city: string = '';
    province: string = '';
    temperature: number = 0;
    apparentTemperature: number = 0;
    humidity: number = 0;
    windSpeed: number = 0;
    windDirection: string = '';
    conditionText: string = '';
    wmoCode: number = 0;
    pressure: number = 0;
    cloudCover: number = 0;
    uvIndex: number = 0;
    isDay: boolean = true;
    updateTime: string = '';
}
/** 逐小时项（后端 WeatherHourDto）*/
export class WeatherHourItem {
    time: string = '';
    hour: number = 0;
    temperature: number = 0;
    condition: string = '';
    wmoCode: number = 0;
    humidity: number = 0;
    windSpeed: number = 0;
    precipitation: number = 0;
}
/** 每日项（后端 WeatherDayDto）*/
export class WeatherDailyItem {
    date: string = '';
    weekDay: string = '';
    condition: string = '';
    wmoCode: number = 0;
    temperatureMax: number = 0;
    temperatureMin: number = 0;
    precipitation: number = 0;
    windSpeedMax: number = 0;
    uvIndex: number = 0;
    sunrise: string = '';
    sunset: string = '';
}
/** 完整天气预报（后端 WeatherForecastDto）*/
export class WeatherForecastFull {
    city: string = '';
    province: string = '';
    current: WeatherCurrentFull = new WeatherCurrentFull();
    daily: WeatherDailyItem[] = [];
    hourly: WeatherHourItem[] = [];
    updateTime: string = '';
}
/** 农事提醒项（后端 AgriAlertDto）*/
export class AgriAlertItem {
    type: string = '';
    level: string = '';
    title: string = '';
    description: string = '';
}
/** 地点搜索结果（后端 WeatherPlaceDto：城市/区县/乡镇/自然村）*/
export class WeatherPlace {
    name: string = '';
    province: string = '';
    city: string = '';
    lat: number = 0;
    lon: number = 0;
    /** city/district/village/place/cache */
    kind: string = '';
}
/** 解析后端 WeatherCurrentDto（camelCase JSON）*/
function parseWeatherCurrent(r: Record<string, Object>): WeatherCurrentFull {
    const c = new WeatherCurrentFull();
    c.city = (r['city'] as string) ?? '';
    c.province = (r['province'] as string) ?? '';
    c.temperature = Number(r['temperature'] ?? 0);
    c.apparentTemperature = Number(r['apparentTemperature'] ?? 0);
    c.humidity = Number(r['humidity'] ?? 0);
    c.windSpeed = Number(r['windSpeed'] ?? 0);
    c.windDirection = (r['windDirection'] as string) ?? '';
    c.conditionText = (r['conditionText'] as string) ?? '';
    c.wmoCode = Number(r['wmoCode'] ?? 0);
    c.pressure = Number(r['pressure'] ?? 0);
    c.cloudCover = Number(r['cloudCover'] ?? 0);
    c.uvIndex = Number(r['uvIndex'] ?? 0);
    c.isDay = Boolean(r['isDay']);
    c.updateTime = (r['updateTime'] as string) ?? '';
    return c;
}
export class ApiError extends Error {
    code: number;
    constructor(code: number, message: string) {
        super(message);
        this.code = code;
    }
}
export class ApiClient {
    private static instance: ApiClient | null = null;
    static getInstance(): ApiClient {
        if (ApiClient.instance === null) {
            ApiClient.instance = new ApiClient();
        }
        return ApiClient.instance;
    }
    /** https 请求注入内置自签证书（caPath）；http 请求原样返回 */
    private async withCommonOptions(url: string, options: http.HttpRequestOptions): Promise<http.HttpRequestOptions> {
        // 阻塞等待 AppContext 就绪，保证证书已落地（避免初始化竞态导致裸 https 请求）
        await AppContext.getInstance().whenReady();
        if (url.startsWith('https')) {
            const certPath = await AppContext.getInstance().ensureServerCert();
            if (certPath.length > 0) {
                options.caPath = certPath;
            }
        }
        return options;
    }
    private async getAsync<T>(path: string, fromJson: (raw: object) => T | null): Promise<T> {
        const app = AppContext.getInstance();
        const request = http.createHttp();
        try {
            const url = app.baseUrl + path;
            const headers: Record<string, string> = {
                'Content-Type': 'application/json',
                'Accept': 'application/json'
            };
            if (app.authToken.length > 0) {
                headers['Authorization'] = `Bearer ${app.authToken}`;
            }
            const resp = await request.request(url, await this.withCommonOptions(url, {
                method: http.RequestMethod.GET,
                header: headers,
                expectDataType: http.HttpDataType.STRING,
                connectTimeout: TIMEOUT,
                readTimeout: TIMEOUT,
                usingCache: false
            }));
            if (resp.responseCode !== 200) {
                throw new ApiError(resp.responseCode, `HTTP ${resp.responseCode}`);
            }
            const body = resp.result as string;
            const vm = JSON.parse(body) as ResultVM<T>;
            if (!vm.isSuccess) {
                throw new ApiError(vm.code, vm.errorMessage ?? '请求失败');
            }
            if (vm.data === null) {
                throw new ApiError(vm.code, '响应无数据');
            }
            const typed = fromJson(vm.data as object);
            if (typed === null) {
                throw new ApiError(-1, '响应结构解析失败');
            }
            return typed;
        }
        catch (e) {
            hilog.warn(0x0001, TAG, 'GET %{public}s 失败: %{public}s', path, (e as Error).message);
            throw e as Error;
        }
        finally {
            request.destroy();
        }
    }
    async getMarketOverview(): Promise<FlowerMarketOverview> {
        // 显式解析 items 数组（后端 FlowerMarketSpeciesItemDto），避免直接断言遗漏嵌套字段
        return await this.getAsync<FlowerMarketOverview>('FlowerMarket/overview', (raw: object) => {
            const r = raw as Record<string, Object>;
            const o = new FlowerMarketOverview();
            o.avgPrice = Number(r['avgPrice'] ?? 0);
            o.priceChange = Number(r['priceChange'] ?? 0);
            o.alertCount = Number(r['alertCount'] ?? 0);
            o.activeSpeciesCount = Number(r['activeSpeciesCount'] ?? 0);
            o.lastUpdateTime = (r['lastUpdateTime'] as string) ?? '';
            const items = r['items'];
            if (Array.isArray(items)) {
                const arr = items as Array<Record<string, Object>>;
                for (const item of arr) {
                    if (item === null || typeof item !== 'object') {
                        continue;
                    }
                    const m = new MarketSpeciesItem();
                    m.speciesId = Number(item['speciesId'] ?? 0);
                    m.name = (item['name'] as string) ?? '';
                    m.category = (item['category'] as string) ?? '';
                    m.origin = (item['origin'] as string) ?? '';
                    m.grade = (item['grade'] as string) ?? '';
                    m.avgPrice = Number(item['avgPrice'] ?? 0);
                    m.minPrice = Number(item['minPrice'] ?? 0);
                    m.maxPrice = Number(item['maxPrice'] ?? 0);
                    m.volume = Number(item['volume'] ?? 0);
                    m.tradeCount = Number(item['tradeCount'] ?? 0);
                    m.snapshotTime = (item['snapshotTime'] as string) ?? '';
                    m.changePercent = Number(item['changePercent'] ?? 0);
                    o.items.push(m);
                }
            }
            return o;
        });
    }
    async getSpeciesList(): Promise<FlowerSpeciesListItem[]> {
        return await this.getAsync<FlowerSpeciesListItem[]>('FlowerSpecies/list', (raw: object) => {
            return raw as FlowerSpeciesListItem[];
        });
    }
    async getDashboardStats(): Promise<DashboardStats> {
        return await this.getAsync<DashboardStats>('FlowerDashboard/stats', (raw: object) => {
            return raw as DashboardStats;
        });
    }
    async getRegionalTrade(): Promise<RegionalTradeData[]> {
        return await this.getAsync<RegionalTradeData[]>('FlowerDashboard/regional', (raw: object) => {
            return raw as RegionalTradeData[];
        });
    }
    async getSupplyDemand(): Promise<SupplyDemandData[]> {
        return await this.getAsync<SupplyDemandData[]>('FlowerDashboard/supply-demand', (raw: object) => {
            return raw as SupplyDemandData[];
        });
    }
    async getRecentTransactions(): Promise<RecentTransaction[]> {
        return await this.getAsync<RecentTransaction[]>('FlowerDashboard/transactions', (raw: object) => {
            return raw as RecentTransaction[];
        });
    }
    async getPriceForecast(speciesId: number, horizonDays: number): Promise<FlowerPriceForecast> {
        return await this.getAsync<FlowerPriceForecast>(`FlowerMarket/species/${speciesId}/forecast?horizonDays=${horizonDays}`, (raw: object) => {
            return raw as FlowerPriceForecast;
        });
    }
    async getPriceHistory(speciesId: number, days: number): Promise<FlowerPriceSnapshot[]> {
        const start = new Date(Date.now() - days * 86400000).toISOString();
        const end = new Date().toISOString();
        return await this.getAsync<FlowerPriceSnapshot[]>(`FlowerMarket/species/${speciesId}/price-history?startTime=${encodeURIComponent(start)}&endTime=${encodeURIComponent(end)}`, (raw: object) => {
            return raw as FlowerPriceSnapshot[];
        });
    }
    async getCurrentWeather(city: string): Promise<WeatherNow> {
        return await this.getAsync<WeatherNow>(`Weather/current?city=${encodeURIComponent(city)}`, (raw: object) => {
            // 后端 WeatherCurrentDto 序列化为 camelCase，其中描述字段为 conditionText
            const r = raw as Record<string, Object>;
            const now = new WeatherNow();
            now.city = (r['city'] as string) ?? '';
            now.temperature = Number(r['temperature'] ?? 0);
            now.description = (r['conditionText'] as string) ?? '';
            now.humidity = Number(r['humidity'] ?? 0);
            now.windSpeed = Number(r['windSpeed'] ?? 0);
            return now;
        });
    }
    async getWeatherForecast(city: string, days: number): Promise<WeatherDay[]> {
        // 后端 Weather/forecast 返回 ResultVM<WeatherForecastDto>（data.daily 数组），
        // 不能把整个 data 当数组断言，否则 UI ForEach 遍历对象会触发
        // "undefined is not callable" 崩溃。此处解析 data.daily 并映射为 WeatherDay。
        return await this.getAsync<WeatherDay[]>(`Weather/forecast?city=${encodeURIComponent(city)}&days=${days}`, (raw: object) => {
            const root = raw as Record<string, Object>;
            const daily = root['daily'] as Array<Record<string, Object>>;
            if (!Array.isArray(daily)) {
                return [];
            }
            const result: WeatherDay[] = [];
            for (const item of daily) {
                if (item === null || typeof item !== 'object') continue;
                const day = new WeatherDay();
                day.date = (item['date'] as string) ?? '';
                day.dayWeather = (item['condition'] as string) ?? '';
                day.nightWeather = '';
                day.highTemp = Number(item['temperatureMax'] ?? 0);
                day.lowTemp = Number(item['temperatureMin'] ?? 0);
                result.push(day);
            }
            return result;
        });
    }
    /** 完整当前天气（含气压/云量/紫外线/体感等）；place 非空时按坐标直查（自然村）*/
    async getCurrentWeatherFull(city: string, place: WeatherPlace | null = null): Promise<WeatherCurrentFull> {
        let url = `Weather/current?city=${encodeURIComponent(city)}`;
        if (place !== null) {
            url = `Weather/current?lat=${place.lat}&lon=${place.lon}&name=${encodeURIComponent(place.name)}`;
        }
        return await this.getAsync<WeatherCurrentFull>(url, (raw: object) => {
            return parseWeatherCurrent(raw as Record<string, Object>);
        });
    }
    /** 完整天气预报（含 current + daily + hourly）；place 非空时按坐标直查（自然村）*/
    async getWeatherForecastFull(city: string, days: number, place: WeatherPlace | null = null): Promise<WeatherForecastFull> {
        let url = `Weather/forecast?city=${encodeURIComponent(city)}&days=${days}`;
        if (place !== null) {
            url = `Weather/forecast?lat=${place.lat}&lon=${place.lon}&name=${encodeURIComponent(place.name)}&days=${days}`;
        }
        return await this.getAsync<WeatherForecastFull>(url, (raw: object) => {
            const root = raw as Record<string, Object>;
            const f = new WeatherForecastFull();
            f.city = (root['city'] as string) ?? '';
            f.province = (root['province'] as string) ?? '';
            const cur = root['current'];
            if (cur !== null && cur !== undefined && typeof cur === 'object') {
                f.current = parseWeatherCurrent(cur as Record<string, Object>);
            }
            const daily = root['daily'];
            if (Array.isArray(daily)) {
                const arr = daily as Array<Record<string, Object>>;
                for (const item of arr) {
                    if (item === null || typeof item !== 'object') {
                        continue;
                    }
                    const d = new WeatherDailyItem();
                    d.date = (item['date'] as string) ?? '';
                    d.weekDay = (item['weekDay'] as string) ?? '';
                    d.condition = (item['condition'] as string) ?? '';
                    d.wmoCode = Number(item['wmoCode'] ?? 0);
                    d.temperatureMax = Number(item['temperatureMax'] ?? 0);
                    d.temperatureMin = Number(item['temperatureMin'] ?? 0);
                    d.precipitation = Number(item['precipitation'] ?? 0);
                    d.windSpeedMax = Number(item['windSpeedMax'] ?? 0);
                    d.uvIndex = Number(item['uvIndex'] ?? 0);
                    d.sunrise = (item['sunrise'] as string) ?? '';
                    d.sunset = (item['sunset'] as string) ?? '';
                    f.daily.push(d);
                }
            }
            const hourly = root['hourly'];
            if (Array.isArray(hourly)) {
                const arrH = hourly as Array<Record<string, Object>>;
                for (const item of arrH) {
                    if (item === null || typeof item !== 'object') {
                        continue;
                    }
                    const h = new WeatherHourItem();
                    h.time = (item['time'] as string) ?? '';
                    h.hour = Number(item['hour'] ?? 0);
                    h.temperature = Number(item['temperature'] ?? 0);
                    h.condition = (item['condition'] as string) ?? '';
                    h.wmoCode = Number(item['wmoCode'] ?? 0);
                    h.humidity = Number(item['humidity'] ?? 0);
                    h.windSpeed = Number(item['windSpeed'] ?? 0);
                    h.precipitation = Number(item['precipitation'] ?? 0);
                    f.hourly.push(h);
                }
            }
            f.updateTime = (root['updateTime'] as string) ?? '';
            return f;
        });
    }
    /** 农事提醒；place 非空时按坐标直查（自然村）*/
    async getAgriAlerts(city: string, place: WeatherPlace | null = null): Promise<AgriAlertItem[]> {
        let url = `Weather/agricultural-alerts?city=${encodeURIComponent(city)}`;
        if (place !== null) {
            url = `Weather/agricultural-alerts?lat=${place.lat}&lon=${place.lon}&name=${encodeURIComponent(place.name)}`;
        }
        return await this.getAsync<AgriAlertItem[]>(url, (raw: object) => {
            const root = raw as Record<string, Object>;
            const result: AgriAlertItem[] = [];
            const list = root['alerts'];
            if (Array.isArray(list)) {
                const arr = list as Array<Record<string, Object>>;
                for (const item of arr) {
                    if (item === null || typeof item !== 'object') {
                        continue;
                    }
                    const a = new AgriAlertItem();
                    a.type = (item['type'] as string) ?? '';
                    a.level = (item['level'] as string) ?? '';
                    a.title = (item['title'] as string) ?? '';
                    a.description = (item['description'] as string) ?? '';
                    result.push(a);
                }
            }
            return result;
        });
    }
    /** 支持的城市列表（失败时前端用内置兜底）*/
    async getWeatherCities(): Promise<string[]> {
        return await this.getAsync<string[]>('Weather/cities', (raw: object) => {
            const result: string[] = [];
            if (Array.isArray(raw)) {
                const arr = raw as Array<Object>;
                for (const item of arr) {
                    result.push(String(item));
                }
            }
            return result;
        });
    }
    /** 地点搜索（城市/区县/乡镇/自然村，后端合并本地表 + GeoNames）*/
    async searchWeatherPlaces(keyword: string): Promise<WeatherPlace[]> {
        return await this.getAsync<WeatherPlace[]>(`Weather/search?keyword=${encodeURIComponent(keyword)}`, (raw: object) => {
            const result: WeatherPlace[] = [];
            if (Array.isArray(raw)) {
                const arr = raw as Array<Record<string, Object>>;
                for (const item of arr) {
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
                    result.push(p);
                }
            }
            return result;
        });
    }
    /**
     * 刷新令牌（POST Account/GetRefreshTokenAsync）。
     * 携带已过期的 ImAuthToken：服务端解密提取 PassportId 后复用/重签通行证级共享 IM 令牌，
     * 解决本地缓存的 IM 令牌超过服务端有效期后被网关拒绝（"鉴权令牌已过期"）的问题。
     */
    async refreshTokenAsync(refreshToken: string, expiredImAuthToken: string): Promise<LoginSession> {
        const app = AppContext.getInstance();
        const request = http.createHttp();
        try {
            let url = app.baseUrl + 'Account/GetRefreshTokenAsync';
            if (expiredImAuthToken.length > 0) {
                url += `?expiredImAuthToken=${encodeURIComponent(expiredImAuthToken)}`;
            }
            // [FromBody] string 绑定要求 JSON 字符串（带引号）
            const resp = await request.request(url, await this.withCommonOptions(url, {
                method: http.RequestMethod.POST,
                header: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                extraData: JSON.stringify(refreshToken),
                expectDataType: http.HttpDataType.STRING,
                connectTimeout: TIMEOUT,
                readTimeout: TIMEOUT,
                usingCache: false
            }));
            if (resp.responseCode !== 200) {
                throw new ApiError(resp.responseCode, `HTTP ${resp.responseCode}`);
            }
            const vm = JSON.parse(resp.result as string) as ResultVM<object>;
            if (!vm.isSuccess || vm.data === null) {
                throw new ApiError(vm.code, vm.errorMessage ?? '刷新令牌失败');
            }
            const raw = vm.data as Record<string, Object>;
            const session = new LoginSession();
            session.accessToken = (raw['accessToken'] as string) ?? '';
            session.refreshToken = (raw['refreshToken'] as string) ?? '';
            session.expiresIn = Number(raw['expiresIn'] ?? 0);
            session.imAuthToken = (raw['imAuthToken'] as string) ?? '';
            // 刷新接口不回传 userId：沿用当前会话身份
            session.userId = app.user.userId;
            session.passportId = app.passportId;
            session.nickname = app.user.nickname;
            if (session.accessToken.length === 0) {
                throw new ApiError(vm.code, '服务端未返回有效刷新结果');
            }
            return session;
        }
        catch (e) {
            hilog.warn(0x0001, TAG, '刷新令牌失败: %{public}s', (e as Error).message);
            throw e as Error;
        }
        finally {
            request.destroy();
        }
    }
    /** 通行证登录（POST Account/signin，参数对齐 C# AccountService.LoginAsync） */
    async signInAsync(passportId: string, password: string, machineId: string): Promise<LoginSession> {
        const app = AppContext.getInstance();
        const request = http.createHttp();
        try {
            const body: Record<string, Object> = {
                'PassportId': passportId,
                'Password': password,
                'VerifyCode': '',
                'Phone': '',
                'Email': '',
                'AppId': 1,
                'AppType': 0,
                'PassportType': 0,
                'MachineId': machineId
            };
            const url = app.baseUrl + 'Account/signin';
            const resp = await request.request(url, await this.withCommonOptions(url, {
                method: http.RequestMethod.POST,
                header: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                extraData: JSON.stringify(body),
                expectDataType: http.HttpDataType.STRING,
                connectTimeout: TIMEOUT,
                readTimeout: TIMEOUT,
                usingCache: false
            }));
            if (resp.responseCode !== 200) {
                throw new ApiError(resp.responseCode, `HTTP ${resp.responseCode}`);
            }
            const vm = JSON.parse(resp.result as string) as ResultVM<object>;
            if (!vm.isSuccess || vm.data === null) {
                throw new ApiError(vm.code, vm.errorMessage ?? '登录失败');
            }
            const raw = vm.data as Record<string, Object>;
            const session = new LoginSession();
            session.accessToken = (raw['accessToken'] as string) ?? '';
            session.refreshToken = (raw['refreshToken'] as string) ?? '';
            session.expiresIn = Number(raw['expiresIn'] ?? 0);
            session.imAuthToken = (raw['imAuthToken'] as string) ?? '';
            // IM 身份约定：IM UserId 即数字形式的 PassportId（对齐 PC 端 ImIdentity.ResolveUserId）。
            // signin 回传的 userId 依赖服务端 Orleans 查询，可能失败回传 0，此时从通行证号回退。
            const rawUserId = Number(raw['userId'] ?? 0);
            const trimmedPassport = passportId.trim();
            session.userId = rawUserId > 0
                ? rawUserId
                : (/^\d+$/.test(trimmedPassport) ? Number(trimmedPassport) : 0);
            session.passportId = passportId;
            session.nickname = passportId;
            if (session.accessToken.length === 0) {
                throw new ApiError(vm.code, '服务端未返回有效登录信息');
            }
            return session;
        }
        catch (e) {
            hilog.warn(0x0001, TAG, '登录失败: %{public}s', (e as Error).message);
            throw e as Error;
        }
        finally {
            request.destroy();
        }
    }
    /**
     * 通行证注册（POST Account/register，参数对齐 C# AccountService.RegisterAsync）。
     * 注册成功即返回 LoginResultDto（与登录一致，含访问令牌），自动完成登录。
     */
    async registerAsync(username: string, email: string, password: string, machineId: string): Promise<LoginSession> {
        const app = AppContext.getInstance();
        const request = http.createHttp();
        try {
            const displayName = username.trim();
            const normalizedEmail = email.trim();
            // Guid.NewGuid().ToString("N")：32 位十六进制，去掉连字符
            const regId: string = util.generateRandomUUID().replace(/-/g, '');
            // ArkTS 要求对象字面量必须有显式类型，故把 GameContext 单独构造
            const gameContext: ClientGameContext = {
                GameId: 1,
                ServerId: 1,
                AreaId: 1,
                Ip: '127.0.0.1',
                PlatformId: 'HarmonyOS'
            };
            const body: Record<string, Object> = {
                'Password': password,
                'Phone': '',
                'Email': normalizedEmail,
                'AppId': 1,
                'AppType': 0,
                'PassportType': 0,
                'NickName': displayName,
                'GameContext': gameContext,
                'RealName': displayName,
                'ID': regId,
                'MachineId': machineId
            };
            const url = app.baseUrl + 'Account/register';
            const resp = await request.request(url, await this.withCommonOptions(url, {
                method: http.RequestMethod.POST,
                header: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                extraData: JSON.stringify(body),
                expectDataType: http.HttpDataType.STRING,
                connectTimeout: TIMEOUT,
                readTimeout: TIMEOUT,
                usingCache: false
            }));
            if (resp.responseCode !== 200) {
                throw new ApiError(resp.responseCode, `HTTP ${resp.responseCode}`);
            }
            const vm = JSON.parse(resp.result as string) as ResultVM<object>;
            if (!vm.isSuccess || vm.data === null) {
                throw new ApiError(vm.code, vm.errorMessage ?? '注册失败');
            }
            const raw = vm.data as Record<string, Object>;
            const session = new LoginSession();
            session.accessToken = (raw['accessToken'] as string) ?? '';
            session.refreshToken = (raw['refreshToken'] as string) ?? '';
            session.expiresIn = Number(raw['expiresIn'] ?? 0);
            session.imAuthToken = (raw['imAuthToken'] as string) ?? '';
            // 与登录一致：注册回传的 userId 依赖服务端 Orleans 查询，可能回传 0，从通行证号回退
            const rawUserId = Number(raw['userId'] ?? 0);
            session.userId = rawUserId > 0 ? rawUserId : 0;
            // 注册成功后服务端回传真实通行证号（新用户无感知，必须展示给用户以便下次登录）
            const rawPassportId = (raw['passportId'] as string) ?? '';
            session.passportId = rawPassportId.length > 0 ? rawPassportId : displayName;
            session.nickname = displayName;
            if (session.accessToken.length === 0) {
                throw new ApiError(vm.code, '服务端未返回有效注册信息');
            }
            return session;
        }
        catch (e) {
            hilog.warn(0x0001, TAG, '注册失败: %{public}s', (e as Error).message);
            throw e as Error;
        }
        finally {
            request.destroy();
        }
    }

    // ==================== 订单与正式支付（FlowerOrder/FlowerPayment）====================

    /**
     * 服务端创建订单（行情品种直购，POST FlowerOrder/create）。
     * 金额以服务端商品价格核算为准；无在售商品时服务端按行情快照价自动创建直供商品。
     */
    async createServerOrder(items: ServerOrderItemRequest[], shipTo: string, cellPhone: string, address: string): Promise<ServerOrderResult> {
        const app = AppContext.getInstance();
        const request = http.createHttp();
        try {
            const body: Record<string, Object> = {
                'items': items as Object,
                'shipTo': shipTo,
                'cellPhone': cellPhone,
                'address': address,
                'shippingAddress': address
            };
            const url = app.baseUrl + 'FlowerOrder/create';
            const resp = await request.request(url, await this.withCommonOptions(url, {
                method: http.RequestMethod.POST,
                header: await this.authHeaders(),
                extraData: JSON.stringify(body),
                expectDataType: http.HttpDataType.STRING,
                connectTimeout: PAY_TIMEOUT,
                readTimeout: PAY_TIMEOUT,
                usingCache: false
            }));
            const vm = JSON.parse(resp.result as string) as ResultVM<object>;
            if (resp.responseCode !== 200 || !vm.isSuccess) {
                throw new ApiError(resp.responseCode, vm.errorMessage ?? `HTTP ${resp.responseCode}`);
            }
            const raw = vm.data as Record<string, Object>;
            const r = new ServerOrderResult();
            r.orderId = Number(raw['orderId'] ?? 0);
            r.orderNo = String(raw['orderNo'] ?? '');
            r.orderTotalAmount = Number(raw['orderTotalAmount'] ?? 0);
            r.status = Number(raw['status'] ?? 0);
            return r;
        }
        catch (e) {
            hilog.warn(0x0001, TAG, '服务端下单失败: %{public}s', (e as Error).message);
            throw e as Error;
        }
        finally {
            request.destroy();
        }
    }

    /**
     * 发起支付（POST FlowerPayment/initiate，scene=App）。
     * 返回交易ID + 拉起支付参数（微信=二次签名参数集 / 支付宝=orderInfo 签名串）。
     */
    async initiatePayment(orderId: number, channel: number, amount: number): Promise<PaymentStateDto> {
        const app = AppContext.getInstance();
        const request = http.createHttp();
        try {
            const body: Record<string, Object> = {
                'orderId': orderId,
                'channel': channel,
                'amount': amount,
                'timestamp': Date.now(),
                'scene': 3 // PaymentScene.App
            };
            const url = app.baseUrl + 'FlowerPayment/initiate';
            const resp = await request.request(url, await this.withCommonOptions(url, {
                method: http.RequestMethod.POST,
                header: await this.authHeaders(),
                extraData: JSON.stringify(body),
                expectDataType: http.HttpDataType.STRING,
                connectTimeout: PAY_TIMEOUT,
                readTimeout: PAY_TIMEOUT,
                usingCache: false
            }));
            const vm = JSON.parse(resp.result as string) as ResultVM<object>;
            if (resp.responseCode !== 200 || !vm.isSuccess || vm.data === null) {
                throw new ApiError(resp.responseCode, vm.errorMessage ?? '发起支付失败');
            }
            const raw = vm.data as Record<string, Object>;
            const dto = new PaymentStateDto();
            dto.transactionId = Number(raw['transactionId'] ?? 0);
            dto.orderId = Number(raw['orderId'] ?? 0);
            dto.transactionNo = String(raw['transactionNo'] ?? '');
            dto.channel = Number(raw['channel'] ?? 0);
            dto.amount = Number(raw['amount'] ?? 0);
            dto.status = Number(raw['status'] ?? 0);
            dto.payParams = String(raw['payParams'] ?? '');
            return dto;
        }
        catch (e) {
            hilog.warn(0x0001, TAG, '发起支付失败: %{public}s', (e as Error).message);
            throw e as Error;
        }
        finally {
            request.destroy();
        }
    }

    /** 支付状态查询（GET FlowerPayment/{id}/status）。status: 0=待支付 1=已支付 */
    async queryPaymentStatus(transactionId: number): Promise<PaymentStateDto> {
        return await this.getAsync<PaymentStateDto>(`FlowerPayment/${transactionId}/status`, (raw: object) => {
            const r = raw as Record<string, Object>;
            const dto = new PaymentStateDto();
            dto.transactionId = Number(r['transactionId'] ?? 0);
            dto.orderId = Number(r['orderId'] ?? 0);
            dto.transactionNo = String(r['transactionNo'] ?? '');
            dto.channel = Number(r['channel'] ?? 0);
            dto.amount = Number(r['amount'] ?? 0);
            dto.status = Number(r['status'] ?? 0);
            dto.payParams = String(r['payParams'] ?? '');
            return dto;
        });
    }

    /** 带认证令牌的请求头 */
    private async authHeaders(): Promise<Record<string, string>> {
        const app = AppContext.getInstance();
        const headers: Record<string, string> = {
            'Content-Type': 'application/json',
            'Accept': 'application/json'
        };
        if (app.authToken.length > 0) {
            headers['Authorization'] = `Bearer ${app.authToken}`;
        }
        return headers;
    }

    // ==================== IM 聊天媒体（图片/语音上传 / 缓存下载）====================

    /** 媒体扩展名 → MIME（上传请求头用；图片与音频） */
    private static mediaMimeOf(ext: string): string {
        switch (ext.toLowerCase()) {
            case '.png':
                return 'image/png';
            case '.webp':
                return 'image/webp';
            case '.gif':
                return 'image/gif';
            case '.m4a':
            case '.aac':
                return 'audio/mp4';
            case '.mp3':
                return 'audio/mpeg';
            case '.wav':
                return 'audio/wav';
            case '.amr':
                return 'audio/amr';
            default:
                return 'image/jpeg';
        }
    }

    /**
     * 上传聊天媒体（POST ImMedia/upload，原始二进制体；fileName 用于服务端扩展名校验）。
     * 图片/语音通用；返回可直接访问的完整 URL（baseUrl + 服务端相对路径），作为消息 mediaUrl 下发。
     */
    async uploadImMediaAsync(sandboxPath: string, fileName: string): Promise<string> {
        const app = AppContext.getInstance();
        const request = http.createHttp();
        try {
            // 读文件字节（相册/相机/录音器产出的沙箱文件）
            const file = fs.openSync(sandboxPath, fs.OpenMode.READ_ONLY);
            let bytes: ArrayBuffer;
            try {
                const stat = fs.statSync(file.fd);
                bytes = new ArrayBuffer(stat.size);
                fs.readSync(file.fd, bytes);
            } finally {
                fs.closeSync(file);
            }
            const dot = fileName.lastIndexOf('.');
            const ext = dot >= 0 ? fileName.slice(dot) : '.jpg';
            const url = app.baseUrl + `ImMedia/upload?fileName=${encodeURIComponent(fileName)}`;
            const headers = await this.authHeaders();
            headers['Content-Type'] = ApiClient.mediaMimeOf(ext);
            headers['Accept'] = 'application/json';
            const resp = await request.request(url, await this.withCommonOptions(url, {
                method: http.RequestMethod.POST,
                header: headers,
                extraData: bytes,
                expectDataType: http.HttpDataType.STRING,
                connectTimeout: PAY_TIMEOUT,
                readTimeout: PAY_TIMEOUT,
                usingCache: false
            }));
            if (resp.responseCode !== 200) {
                throw new ApiError(resp.responseCode, `HTTP ${resp.responseCode}`);
            }
            const vm = JSON.parse(resp.result as string) as ResultVM<string>;
            if (!vm.isSuccess || vm.data === null) {
                throw new ApiError(vm.code, vm.errorMessage ?? '媒体上传失败');
            }
            const base = app.baseUrl.endsWith('/') ? app.baseUrl.slice(0, app.baseUrl.length - 1) : app.baseUrl;
            return base + vm.data;
        }
        catch (e) {
            hilog.warn(0x0001, TAG, 'IM 媒体上传失败: %{public}s', (e as Error).message);
            throw e as Error;
        }
        finally {
            request.destroy();
        }
    }

    /** 远程媒体 URL → 沙箱缓存文件路径（内存 + 磁盘两级缓存；并发请求合并） */
    private mediaCache: Map<string, string> = new Map();
    private mediaLoading: Map<string, Promise<string>> = new Map();
    /** 媒体缓存目录（首次访问时创建于 cacheDir/im_media） */
    private mediaCacheDir: string = '';

    private ensureMediaCacheDir(): string {
        if (this.mediaCacheDir.length === 0) {
            const ctx = AppContext.getInstance().context;
            const dir = `${ctx?.cacheDir ?? ''}/im_media`;
            try {
                if (!fs.accessSync(dir)) {
                    fs.mkdirSync(dir);
                }
            } catch (_e) {
                // 目录已存在或创建失败均按现状处理
            }
            this.mediaCacheDir = dir;
        }
        return this.mediaCacheDir;
    }

    /** url → 稳定缓存文件名（djb2 哈希，保留扩展名） */
    private static mediaCacheName(url: string): string {
        let h = 5381;
        for (let i = 0; i < url.length; i++) {
            h = ((h << 5) + h + url.charCodeAt(i)) | 0;
        }
        const dot = url.lastIndexOf('.');
        const slash = url.lastIndexOf('/');
        const ext = dot > slash ? url.slice(dot).toLowerCase() : '.jpg';
        return `img_${(h >>> 0).toString(16)}${ext}`;
    }

    /**
     * 下载远程媒体到沙箱缓存并返回本地路径（服务端为自签 https，
     * Image 组件无法直接加载，须经本方法走 caPath 校验后落盘转本地渲染）。
     * 命中内存/磁盘缓存直接返回；并发同 URL 请求合并为同一次下载。
     */
    async downloadImMediaAsync(url: string): Promise<string> {
        if (url.length === 0) {
            return '';
        }
        const cached = this.mediaCache.get(url);
        if (cached !== undefined) {
            return cached;
        }
        const inflight = this.mediaLoading.get(url);
        if (inflight !== undefined) {
            return inflight;
        }
        const task = this.downloadImMediaInternal(url);
        this.mediaLoading.set(url, task);
        try {
            return await task;
        } finally {
            this.mediaLoading.delete(url);
        }
    }

    /**
     * 下载共享文件到沙箱缓存并返回本地路径（保留原始文件名与扩展名，
     * 供系统 filePreview 按扩展名匹配打开方式；fileName 为空时回退 URL 末段）。
     * 按 URL 哈希 + 文件名去重缓存；并发同 URL 合并为一次下载。
     */
    async downloadSharedFileAsync(url: string, fileName: string): Promise<string> {
        if (url.length === 0) {
            return '';
        }
        const cacheKey = `file|${url}|${fileName}`;
        const cached = this.mediaCache.get(cacheKey);
        if (cached !== undefined) {
            return cached;
        }
        const inflight = this.mediaLoading.get(cacheKey);
        if (inflight !== undefined) {
            return inflight;
        }
        const task = this.downloadSharedFileInternal(url, fileName);
        this.mediaLoading.set(cacheKey, task);
        try {
            return await task;
        } finally {
            this.mediaLoading.delete(cacheKey);
        }
    }

    private async downloadSharedFileInternal(url: string, fileName: string): Promise<string> {
        const dir = this.ensureMediaCacheDir();
        let raw = fileName && fileName.length > 0 ? fileName : url.slice(url.lastIndexOf('/') + 1);
        if (raw.length === 0) {
            raw = `file_${Date.now()}`;
        }
        // 清洗非法字符；无扩展名时从 URL 末段补全（系统按扩展名匹配打开方式）
        raw = raw.replace(/[\\/:*?"<>|]/g, '_');
        if (raw.lastIndexOf('.') < 0) {
            const dot = url.lastIndexOf('.');
            const slash = url.lastIndexOf('/');
            if (dot > slash) {
                raw += url.slice(dot).toLowerCase();
            }
        }
        let h = 5381;
        for (let i = 0; i < url.length; i++) {
            h = ((h << 5) + h + url.charCodeAt(i)) | 0;
        }
        const path = `${dir}/file_${(h >>> 0).toString(16)}_${raw}`;
        try {
            if (fs.accessSync(path)) {
                this.mediaCache.set(`file|${url}|${fileName}`, path);
                return path;
            }
        } catch (_e) {
            // access 异常按未命中处理
        }
        const request = http.createHttp();
        try {
            const resp = await request.request(url, await this.withCommonOptions(url, {
                method: http.RequestMethod.GET,
                expectDataType: http.HttpDataType.ARRAY_BUFFER,
                connectTimeout: TIMEOUT,
                readTimeout: PAY_TIMEOUT,
                usingCache: false
            }));
            if (resp.responseCode !== 200) {
                throw new ApiError(resp.responseCode, `HTTP ${resp.responseCode}`);
            }
            const bytes = resp.result as ArrayBuffer;
            if (bytes.byteLength === 0) {
                throw new ApiError(-1, '文件内容为空');
            }
            const file = fs.openSync(path, fs.OpenMode.READ_WRITE | fs.OpenMode.CREATE | fs.OpenMode.TRUNC);
            try {
                fs.writeSync(file.fd, bytes);
            } finally {
                fs.closeSync(file);
            }
            this.mediaCache.set(`file|${url}|${fileName}`, path);
            return path;
        }
        catch (e) {
            hilog.warn(0x0001, TAG, '共享文件下载失败: %{public}s %{public}s', url, (e as Error).message);
            try {
                if (fs.accessSync(path)) {
                    fs.unlinkSync(path);
                }
            } catch (_e) {
                // 忽略清理失败
            }
            throw e as Error;
        }
        finally {
            request.destroy();
        }
    }

    private async downloadImMediaInternal(url: string): Promise<string> {
        const dir = this.ensureMediaCacheDir();
        const path = `${dir}/${ApiClient.mediaCacheName(url)}`;
        // 磁盘缓存命中
        try {
            if (fs.accessSync(path)) {
                this.mediaCache.set(url, path);
                return path;
            }
        } catch (_e) {
            // access 异常按未命中处理
        }
        const request = http.createHttp();
        try {
            const resp = await request.request(url, await this.withCommonOptions(url, {
                method: http.RequestMethod.GET,
                expectDataType: http.HttpDataType.ARRAY_BUFFER,
                connectTimeout: TIMEOUT,
                readTimeout: PAY_TIMEOUT,
                usingCache: false
            }));
            if (resp.responseCode !== 200) {
                throw new ApiError(resp.responseCode, `HTTP ${resp.responseCode}`);
            }
            const bytes = resp.result as ArrayBuffer;
            if (bytes.byteLength === 0) {
                throw new ApiError(-1, '媒体内容为空');
            }
            const file = fs.openSync(path, fs.OpenMode.READ_WRITE | fs.OpenMode.CREATE | fs.OpenMode.TRUNC);
            try {
                fs.writeSync(file.fd, bytes);
            } finally {
                fs.closeSync(file);
            }
            this.mediaCache.set(url, path);
            return path;
        }
        catch (e) {
            hilog.warn(0x0001, TAG, 'IM 媒体下载失败: %{public}s %{public}s', url, (e as Error).message);
            // 下载失败不落残留半截文件
            try {
                if (fs.accessSync(path)) {
                    fs.unlinkSync(path);
                }
            } catch (_e) {
                // 忽略清理失败
            }
            throw e as Error;
        }
        finally {
            request.destroy();
        }
    }
}

/** 服务端下单商品项（productId 优先，否则 speciesId 品种直购） */
export class ServerOrderItemRequest {
    productId: number = 0;
    speciesId: number = 0;
    productName: string = '';
    quantity: number = 0;
}

/** 服务端下单结果（金额以服务端核算为准） */
export class ServerOrderResult {
    orderId: number = 0;
    orderNo: string = '';
    orderTotalAmount: number = 0;
    status: number = 0;
}

/** 支付交易状态（channel: 0=微信 1=支付宝；status: 0=待支付 1=已支付 2=已关闭） */
export class PaymentStateDto {
    transactionId: number = 0;
    orderId: number = 0;
    transactionNo: string = '';
    channel: number = 0;
    amount: number = 0;
    status: number = 0;
    payParams: string = '';
}

