import { http } from '@kit.NetworkKit';
import { hilog } from '@kit.PerformanceAnalysisKit';

/**
 * 花卉数据服务。
 *
 * 通过 HTTP 请求后端 WebAPI 获取花卉市场、预警、种植等实际业务数据。
 * 通过 WebView 的 javaScriptProxy 暴露给 HTML 页面调用。
 *
 * API 基地址优先级：
 * 1. 通过 setBaseUrl 动态设置
 * 2. 默认 https://10.0.2.2:5101/ （模拟器访问宿主机）
 */

const TAG = 'GengDi.FlowerData';
const DOMAIN = 0x0001;
const DEFAULT_TIMEOUT = 15000;

/** API 响应包装结构 */
interface ApiResult<T> {
  code: number;
  isSuccess: boolean;
  errorMessage: string;
  data: T;
}

/** meting 接口返回的歌曲条目 */
interface MetingSong {
  url?: string;
}

/** meting type=playlist 返回的条目（无独立 id，需从 lrc/url 提取） */
interface MetingPlaylistItem {
  name?: string;
  artist?: string;
  url?: string;
  pic?: string;
  lrc?: string;
}

/** 播放地址响应结构（与既有 HTML 解析兼容） */
interface MusicUrlItem {
  url: string;
}
interface MusicUrlPayload {
  data: MusicUrlItem[];
}

/** 歌手 */
interface SongArtist {
  name: string;
}
/** 专辑 */
interface SongAlbum {
  name: string;
  picUrl: string;
}
/** 歌曲（歌单详情 tracks 元素） */
interface SongTrack {
  id: number;
  name: string;
  ar: SongArtist[];
  al: SongAlbum;
  dt: number;
}
/** 歌单详情结构 {playlist:{tracks:[...]}} */
interface SongPlaylist {
  tracks: SongTrack[];
}
interface PlaylistDetailPayload {
  playlist: SongPlaylist;
}
/** 回退歌单条目 */
interface FallbackPlaylist {
  id: number;
  name: string;
}
interface PlaylistListPayload {
  playlists: FallbackPlaylist[];
}
/** 歌词结构 {lrc:{lyric:...}} */
interface LyricBlock {
  lyric: string;
}
interface LyricPayload {
  lrc: LyricBlock;
}
/** 搜索歌曲条目（与歌单详情 tracks 兼容） */
interface SearchSong {
  id: number;
  name: string;
  artists: Array<Record<string, Object>>;
  album: Record<string, Object>;
  duration: number;
}
interface SearchPayload {
  songs: SongTrack[];
}

export class FlowerDataService {
  private baseUrl: string = 'https://10.0.2.2:5101/';
  private authToken: string = '';

  /** 设置 API 基地址 */
  setBaseUrl(url: string): void {
    if (url && url.length > 0) {
      this.baseUrl = url.endsWith('/') ? url : url + '/';
      hilog.info(DOMAIN, TAG, 'API基地址设置为: %{public}s', this.baseUrl);
    }
  }

  /** 设置认证 Token */
  setAuthToken(token: string): void {
    this.authToken = token;
  }

  /** 获取当前基地址 */
  getBaseUrl(): string {
    return this.baseUrl;
  }

  /**
   * 通用 GET 请求。
   * 返回 JSON 字符串，失败返回空字符串。
   */
  private async getAsync(path: string): Promise<string> {
    const httpRequest = http.createHttp();
    try {
      const url = this.baseUrl + path;
      hilog.info(DOMAIN, TAG, 'GET %{public}s', url);

      const headers: Record<string, string> = {
        'Content-Type': 'application/json',
        'Accept': 'application/json'
      };
      if (this.authToken.length > 0) {
        headers['Authorization'] = `Bearer ${this.authToken}`;
      }

      const response = await httpRequest.request(url, {
        method: http.RequestMethod.GET,
        header: headers,
        expectDataType: http.HttpDataType.STRING,
        connectTimeout: DEFAULT_TIMEOUT,
        readTimeout: DEFAULT_TIMEOUT,
        usingCache: false,
        priority: 0
      });

      if (response.responseCode === 200) {
        const body = response.result as string;
        hilog.info(DOMAIN, TAG, '响应长度: %{public}d', body.length);
        return body;
      } else {
        hilog.error(DOMAIN, TAG, 'HTTP %{public}d', response.responseCode);
        return '';
      }
    } catch (e) {
      hilog.error(DOMAIN, TAG, '请求异常: %{public}s', (e as Error).message);
      return '';
    } finally {
      httpRequest.destroy();
    }
  }

  /**
   * 通用 POST 请求。
   */
  private async postAsync(path: string, body: string): Promise<string> {
    const httpRequest = http.createHttp();
    try {
      const url = this.baseUrl + path;
      hilog.info(DOMAIN, TAG, 'POST %{public}s', url);

      const headers: Record<string, string> = {
        'Content-Type': 'application/json',
        'Accept': 'application/json'
      };
      if (this.authToken.length > 0) {
        headers['Authorization'] = `Bearer ${this.authToken}`;
      }

      const response = await httpRequest.request(url, {
        method: http.RequestMethod.POST,
        header: headers,
        extraData: body,
        expectDataType: http.HttpDataType.STRING,
        connectTimeout: DEFAULT_TIMEOUT,
        readTimeout: DEFAULT_TIMEOUT,
        usingCache: false,
        priority: 0
      });

      if (response.responseCode === 200) {
        return response.result as string;
      } else {
        hilog.error(DOMAIN, TAG, 'HTTP %{public}d', response.responseCode);
        return '';
      }
    } catch (e) {
      hilog.error(DOMAIN, TAG, '请求异常: %{public}s', (e as Error).message);
      return '';
    } finally {
      httpRequest.destroy();
    }
  }

  // ========== 市场预报 API ==========

  /**
   * 获取市场概览（均价、涨跌幅、预警数、品种快照列表）。
   * 对应后端: GET FlowerMarket/overview
   */
  async getMarketOverview(): Promise<string> {
    return await this.getAsync('FlowerMarket/overview');
  }

  /**
   * 获取品种价格预测。
   * 对应后端: GET FlowerMarket/species/{speciesId}/forecast?horizonDays={days}
   */
  async getPriceForecast(speciesId: number, horizonDays: number): Promise<string> {
    return await this.getAsync(`FlowerMarket/species/${speciesId}/forecast?horizonDays=${horizonDays}`);
  }

  /**
   * 获取品种价格历史。
   * 对应后端: GET FlowerMarket/species/{speciesId}/price-history
   */
  async getPriceHistory(speciesId: number, days: number): Promise<string> {
    const now = new Date();
    const start = new Date(now.getTime() - days * 24 * 60 * 60 * 1000);
    const startStr = start.toISOString();
    const endStr = now.toISOString();
    return await this.getAsync(
      `FlowerMarket/species/${speciesId}/price-history?startTime=${encodeURIComponent(startStr)}&endTime=${encodeURIComponent(endStr)}`
    );
  }

  /**
   * 获取品种列表。
   * 对应后端: GET FlowerSpecies/list
   */
  async getSpeciesList(): Promise<string> {
    return await this.getAsync('FlowerSpecies/list');
  }

  /**
   * 获取仪表盘统计数据。
   * 对应后端: GET FlowerDashboard/stats
   */
  async getDashboardStats(): Promise<string> {
    return await this.getAsync('FlowerDashboard/stats');
  }

  /**
   * 获取区域交易数据。
   * 对应后端: GET FlowerDashboard/regional
   */
  async getRegionalTradeData(): Promise<string> {
    return await this.getAsync('FlowerDashboard/regional');
  }

  /**
   * 获取供需数据。
   * 对应后端: GET FlowerDashboard/supply-demand
   */
  async getSupplyDemandData(): Promise<string> {
    return await this.getAsync('FlowerDashboard/supply-demand');
  }

  /**
   * 获取最近交易记录。
   * 对应后端: GET FlowerDashboard/transactions
   */
  async getRecentTransactions(): Promise<string> {
    return await this.getAsync('FlowerDashboard/transactions');
  }

  // ========== 预警 API ==========

  /**
   * 获取预警列表。
   * 对应后端: GET FlowerAlert/species/{speciesId}?skip={skip}&take={take}
   * speciesId = 0 表示全部品种
   */
  async getAlerts(speciesId: number, skip: number, take: number): Promise<string> {
    return await this.getAsync(`FlowerAlert/species/${speciesId}?skip=${skip}&take=${take}`);
  }

  // ========== 种植 IoT API ==========

  /**
   * 获取种植批次列表。
   * 对应后端: GET FlowerIoT/batches/{region}
   */
  async getPlantingBatches(region: string): Promise<string> {
    return await this.getAsync(`FlowerIoT/batches/${region || 'default'}`);
  }

  /**
   * 获取 IoT 设备传感器数据。
   * 对应后端: GET FlowerIoT/devices/{batchId}/sensors
   */
  async getSensorData(batchId: number): Promise<string> {
    return await this.getAsync(`FlowerIoT/devices/${batchId}/sensors`);
  }

  // ========== AI 助手 API ==========

  /**
   * 获取种植建议列表。
   * 对应后端: GET FlowerAI/advice/{batchId}
   */
  async getActiveAdvice(batchId: number): Promise<string> {
    return await this.getAsync(`FlowerAI/advice/${batchId}`);
  }

  // ========== 商家 API ==========

  /**
   * 获取商家仪表盘统计。
   * 对应后端: GET FlowerMerchant/dashboard/{merchantId}
   */
  async getMerchantDashboard(merchantId: number): Promise<string> {
    return await this.getAsync(`FlowerMerchant/dashboard/${merchantId}`);
  }

  /**
   * 获取商家订单列表。
   * 对应后端: GET FlowerMerchant/orders/{merchantId}?skip={skip}&take={take}
   */
  async getMerchantOrders(merchantId: number, skip: number, take: number): Promise<string> {
    return await this.getAsync(`FlowerMerchant/orders/${merchantId}?skip=${skip}&take=${take}`);
  }

  // ========== 天气 API ==========

  /**
   * 获取当前天气数据。
   * 对应后端: GET Weather/current?city={city}
   */
  async getCurrentWeather(city: string): Promise<string> {
    return await this.getAsync(`Weather/current?city=${encodeURIComponent(city || '昆明')}`);
  }

  // ========== 音乐 API（网易云音乐，外部 API）==========

  /** 网易云音乐 API 端点列表（多端点容错，首选动态热门歌单） */
  private musicApiEndpoints: string[] = [
    'https://netease-cloud-music-api.fe-mm.com',
    'https://neteasecloudmusicapi.vercel.app',
    'https://netease-cloud-music-api-five-roan-59.vercel.app'
  ];
  /** meting API 基地址（模拟器已验证可达；song/url/playlist/lrc 均支持） */
  private readonly metingBase: string = 'https://api.injahow.cn/meting/';
  /** fe-mm.com 全链路不可达时回退的真实网易云歌单 ID */
  private readonly fallbackPlaylistId: number = 3778678;
  private musicApiBase: string = '';
  private musicApiTested: boolean = false;
  /**
   * 音乐桥接结果缓存。
   * ArkWeb javaScriptProxy 对 asyncMethodList 的 Promise 返回并不可靠（实测 HTTP 未完成即返回空值），
   * 因此改用"异步写入 + 同步轮询"模式：loadMusicData 后台执行并写入此表，
   * 前端反复调用同步的 getMusicData 直到取到非空结果。
   */
  private musicResult: Map<string, string> = new Map<string, string>();

  /**
   * （同步桥接）触发一次音乐数据加载，后台完成后写入 musicResult。
   * kind: playlists / detail / lyrics / url
   */
  loadMusicData(kind: string, param: string): void {
    this.musicResult.set(kind, '');
    this.loadMusicDataAsync(kind, param);
  }

  /** （同步桥接）返回指定 kind 的最近一次加载结果。 */
  getMusicData(kind: string): string {
    return this.musicResult.get(kind) ?? '';
  }

  private async loadMusicDataAsync(kind: string, param: string): Promise<void> {
    let value = '';
    try {
      if (kind === 'playlists') {
        value = await this.getMusicPlaylists();
      } else if (kind === 'detail') {
        value = await this.getPlaylistDetail(Number(param));
      } else if (kind === 'lyrics') {
        value = await this.getMusicLyrics(Number(param));
      } else if (kind === 'url') {
        value = await this.getMusicUrl(Number(param));
      } else if (kind === 'search') {
        value = await this.searchMusic(param);
      }
    } catch (e) {
      hilog.warn(DOMAIN, TAG, '音乐桥接 %{public}s 执行异常: %{public}s', kind, (e as Error).message);
      value = '';
    }
    this.musicResult.set(kind, value);
  }

  /**
   * 通用外部 GET 请求（不使用后端 baseUrl，直接请求完整 URL）。
   * 用于调用第三方 API（如网易云音乐），规避 WebView CORS 限制。
   */
  private async getExternalAsync(url: string): Promise<string> {
    const httpRequest = http.createHttp();
    try {
      hilog.info(DOMAIN, TAG, 'EXT GET %{public}s', url);
      const response = await httpRequest.request(url, {
        method: http.RequestMethod.GET,
        header: {
          'Content-Type': 'application/json',
          'Accept': 'application/json',
          'User-Agent': 'Mozilla/5.0 (Linux; Android 12) AppleWebKit/537.36'
        },
        expectDataType: http.HttpDataType.STRING,
        connectTimeout: DEFAULT_TIMEOUT,
        readTimeout: DEFAULT_TIMEOUT,
        usingCache: false,
        priority: 0
      });
      if (response.responseCode === 200) {
        return response.result as string;
      } else {
        hilog.error(DOMAIN, TAG, 'EXT HTTP %{public}d', response.responseCode);
        return '';
      }
    } catch (e) {
      hilog.error(DOMAIN, TAG, 'EXT 请求异常: %{public}s', (e as Error).message);
      return '';
    } finally {
      httpRequest.destroy();
    }
  }

  /**
   * 测试并选择可用的网易云音乐 API 端点。
   * 依次尝试端点列表，返回第一个可用的基地址。
   */
  private async ensureMusicApiBase(): Promise<string> {
    if (this.musicApiTested) return this.musicApiBase;
    this.musicApiTested = true;
    for (const endpoint of this.musicApiEndpoints) {
      try {
        const result = await this.getExternalAsync(endpoint + '/top/playlist?limit=1');
        if (result.length > 0 && result.includes('"playlists"')) {
          this.musicApiBase = endpoint;
          hilog.info(DOMAIN, TAG, '音乐API端点选定: %{public}s', this.musicApiBase);
          return this.musicApiBase;
        }
      } catch (e) {
        hilog.error(DOMAIN, TAG, '音乐端点测试失败: %{public}s', (e as Error).message);
      }
    }
    hilog.error(DOMAIN, TAG, '所有音乐API端点均不可用');
    return '';
  }

  /**
   * 获取网易云音乐热门歌单列表。
   * 返回 JSON 字符串，包含 playlists 数组。
   * fe-mm.com 全链路不可达时回退到固定真实歌单（meting 通道），避免页面停留在模拟数据。
   */
  async getMusicPlaylists(): Promise<string> {
    try {
      const base = await this.ensureMusicApiBase();
      if (base) {
        const raw = await this.getExternalAsync(base + '/top/playlist?limit=5&order=hot');
        if (raw.length > 0 && raw.includes('"playlists"')) {
          // fe-mm 原始响应含大量 description/creator/tags 字段，体积 10KB+。
          // ArkWeb javaScriptProxy 超长返回会被截断导致 JS 解析为空，故此处精简为
          // 仅 {id,name} 的紧凑结构后再返回 HTML。
          const compact = this.compactPlaylistList(raw);
          if (compact.length > 0) {
            const res: PlaylistListPayload = { playlists: compact };
            return JSON.stringify(res);
          }
        }
        hilog.warn(DOMAIN, TAG, '音乐歌单列表精简失败，回退 meting 固定歌单');
      }
    } catch (e) {
      hilog.warn(DOMAIN, TAG, '音乐歌单列表处理异常: %{public}s', (e as Error).message);
    }
    const fallbackPlaylists: FallbackPlaylist[] = [{ id: this.fallbackPlaylistId, name: '热门网络歌单' }];
    const fallbackRes: PlaylistListPayload = { playlists: fallbackPlaylists };
    return JSON.stringify(fallbackRes);
  }

  /**
   * 获取歌单详情（含歌曲列表）。
   * @param playlistId 歌单 ID
   */
  async getPlaylistDetail(playlistId: number): Promise<string> {
    try {
      const base = await this.ensureMusicApiBase();
      if (base) {
        const raw = await this.getExternalAsync(base + '/playlist/detail?id=' + playlistId);
        if (raw.length > 0 && raw.includes('"playlist"')) {
          // 同样精简为紧凑 tracks 结构，避免超长返回被 javaScriptProxy 截断。
          const compact = this.compactPlaylistDetail(raw);
          if (compact.length > 0) return compact;
        }
        hilog.warn(DOMAIN, TAG, 'fe-mm 歌单详情精简失败，改用 meting type=playlist');
      }
    } catch (e) {
      hilog.warn(DOMAIN, TAG, '歌单详情处理异常: %{public}s', (e as Error).message);
    }
    // 回退：meting type=playlist，转换为 HTML 期望的 {playlist:{tracks:[...]}} 结构
    const meting = await this.getExternalAsync(`${this.metingBase}?server=netease&type=playlist&id=${playlistId}`);
    if (!meting) return '';
    try {
      const arr = JSON.parse(meting) as MetingPlaylistItem[];
      if (!Array.isArray(arr) || arr.length === 0) return '';
      const tracks: SongTrack[] = arr.slice(0, 20).map((item, index) => {
        const id = this.extractMetingId(item.lrc || item.url || '');
        const artistName = (item.artist || '未知艺术家').split('/');
        const ar: SongArtist[] = artistName.map((n: string): SongArtist => ({ name: n }));
        const al: SongAlbum = { name: '网络音乐', picUrl: item.pic || '' };
        const track: SongTrack = {
          id: id > 0 ? id : -(index + 1),
          name: item.name || '未知歌曲',
          ar: ar,
          al: al,
          dt: 0
        };
        return track;
      });
      const detailRes: PlaylistDetailPayload = { playlist: { tracks: tracks } };
      return JSON.stringify(detailRes);
    } catch (e) {
      hilog.error(DOMAIN, TAG, 'meting 歌单详情解析失败: %{public}s', (e as Error).message);
      return '';
    }
  }

  /**
   * 精简 fe-mm /top/playlist 响应为 {id,name} 数组（仅取前 5 个）。
   */
  private compactPlaylistList(raw: string): FallbackPlaylist[] {
    const obj = JSON.parse(raw) as Record<string, Object>;
    const arr = obj['playlists'] as Array<Record<string, Object>>;
    if (!Array.isArray(arr)) return [];
    const result: FallbackPlaylist[] = [];
    for (const p of arr) {
      if (p === null || typeof p !== 'object') continue;
      const id = Number((p as Record<string, Object>)['id'] ?? 0);
      const name = ((p as Record<string, Object>)['name'] as string) ?? '';
      if (id > 0 && name) result.push({ id: id, name: name });
      if (result.length >= 5) break;
    }
    return result;
  }

  /**
   * 精简 fe-mm /playlist/detail 响应为紧凑 tracks 结构（仅保留 id/name/ar/al/dt）。
   */
  private compactPlaylistDetail(raw: string): string {
    const root = JSON.parse(raw) as Record<string, Object>;
    const playlist = root['playlist'] as Record<string, Object>;
    if (playlist === null || typeof playlist !== 'object') return '';
    const tracks = playlist['tracks'] as Array<Record<string, Object>>;
    if (!Array.isArray(tracks)) return '';
    const compactTracks: SongTrack[] = [];
    for (const t of tracks) {
      if (t === null || typeof t !== 'object') continue;
      const id = Number((t as Record<string, Object>)['id'] ?? 0);
      if (id <= 0) continue;
      const arRaw = t['ar'] as Array<Record<string, Object>>;
      const ar: SongArtist[] = [];
      if (Array.isArray(arRaw)) {
        for (const a of arRaw) {
          if (a !== null && typeof a === 'object') {
            ar.push({ name: (a['name'] as string) ?? '' });
          }
          if (ar.length >= 3) break;
        }
      }
      const alRaw = t['al'] as Record<string, Object>;
      const alName = (alRaw != null && alRaw['name']) ? (alRaw['name'] as string) : '未知专辑';
      const alPic = (alRaw != null && alRaw['picUrl']) ? (alRaw['picUrl'] as string) : '';
      compactTracks.push({
        id: id,
        name: (t['name'] as string) ?? '未知歌曲',
        ar: ar,
        al: { name: alName, picUrl: alPic },
        dt: Number(t['dt'] ?? 0)
      });
      if (compactTracks.length >= 20) break;
    }
    if (compactTracks.length === 0) return '';
    const payload: PlaylistDetailPayload = { playlist: { tracks: compactTracks } };
    return JSON.stringify(payload);
  }

  /** 从 meting 的 lrc/url 字段（...type=lrc&id=xxx）中提取真实歌曲 id */
  private extractMetingId(input: string): number {
    const match = /id=(\d+)/.exec(input || '');
    return match ? parseInt(match[1]) : 0;
  }

  /**
   * 获取歌词。
   * @param songId 网易云歌曲 ID
   */
  async getMusicLyrics(songId: number): Promise<string> {
    const base = await this.ensureMusicApiBase();
    if (base) {
      const raw = await this.getExternalAsync(base + '/lyric?id=' + songId);
      if (raw.length > 0 && raw.includes('"lrc"')) {
        return raw;
      }
      hilog.warn(DOMAIN, TAG, 'fe-mm 歌词获取失败，改用 meting type=lrc');
    }
    // 回退：meting type=lrc 返回纯 LRC 文本，包装为 HTML 期望的 {lrc:{lyric}}
    const lrc = await this.getExternalAsync(`${this.metingBase}?server=netease&type=lrc&id=${songId}`);
    if (!lrc || lrc.length === 0) return '';
    const lrcRes: LyricPayload = { lrc: { lyric: lrc } };
    return JSON.stringify(lrcRes);
  }

  /**
   * 获取歌曲播放 URL。
   * 通过 meting 接口获取真实可播放地址（302 重定向到网易云 CDN），
   * 规避第三方 /song/url 接口因无 Cookie 授权返回 url=null 的问题。
   * 返回与原有解析兼容的结构 {data:[{url:...}]}。
   * @param songId 网易云歌曲 ID
   */
  async getMusicUrl(songId: number): Promise<string> {
    try {
      const url = `${this.metingBase}?server=netease&type=song&id=${songId}`;
      const raw = await this.getExternalAsync(url);
      if (!raw) return '';
      const arr = JSON.parse(raw) as MetingSong[];
      if (Array.isArray(arr) && arr.length > 0 && arr[0].url && arr[0].url.length > 0) {
        const payload: MusicUrlPayload = { data: [{ url: arr[0].url }] };
        return JSON.stringify(payload);
      }
    } catch (e) {
      hilog.error(DOMAIN, TAG, 'meting 播放地址解析失败: %{public}s', (e as Error).message);
    }
    return '';
  }

  /**
   * 搜索网易云音乐（按关键字）。
   * 走 fe-mm 原生 /search 接口，精简为紧凑 tracks 结构返回 HTML。
   * 返回 {songs:[{id,name,ar,al,dt}]}，与歌单详情 tracks 结构兼容。
   * @param keyword 搜索关键字
   */
  async searchMusic(keyword: string): Promise<string> {
    const kw = (keyword || '').trim();
    if (kw.length === 0) return '';
    try {
      const base = await this.ensureMusicApiBase();
      if (!base) return '';
      const raw = await this.getExternalAsync(base + '/search?keywords=' + encodeURIComponent(kw) + '&limit=20');
      if (raw.length === 0) return '';
      const root = JSON.parse(raw) as Record<string, Object>;
      const result = root['result'] as Record<string, Object>;
      if (result === null || typeof result !== 'object') return '';
      const songs = result['songs'] as Array<Record<string, Object>>;
      if (!Array.isArray(songs) || songs.length === 0) return '';
      const compactSongs: SongTrack[] = [];
      for (const s of songs) {
        if (s === null || typeof s !== 'object') continue;
        const id = Number((s as Record<string, Object>)['id'] ?? 0);
        if (id <= 0) continue;
        const arRaw = s['artists'] as Array<Record<string, Object>>;
        const ar: SongArtist[] = [];
        if (Array.isArray(arRaw)) {
          for (const a of arRaw) {
            if (a !== null && typeof a === 'object') {
              ar.push({ name: (a['name'] as string) ?? '' });
            }
            if (ar.length >= 3) break;
          }
        }
        const alRaw = s['album'] as Record<string, Object>;
        const alName = (alRaw != null && alRaw['name']) ? (alRaw['name'] as string) : '未知专辑';
        const alPic = (alRaw != null && alRaw['picUrl']) ? (alRaw['picUrl'] as string) : '';
        compactSongs.push({
          id: id,
          name: (s['name'] as string) ?? '未知歌曲',
          ar: ar,
          al: { name: alName, picUrl: alPic },
          dt: Number(s['duration'] ?? 0)
        });
        if (compactSongs.length >= 20) break;
      }
      if (compactSongs.length === 0) return '';
      const payload: SearchPayload = { songs: compactSongs };
      return JSON.stringify(payload);
    } catch (e) {
      hilog.error(DOMAIN, TAG, '音乐搜索失败: %{public}s', (e as Error).message);
      return '';
    }
  }

  /**
   * 获取天气预报。
   * 对应后端: GET Weather/forecast?city={city}&days={days}
   */
  async getWeatherForecast(city: string, days: number): Promise<string> {
    return await this.getAsync(`Weather/forecast?city=${encodeURIComponent(city || '昆明')}&days=${days}`);
  }

  /**
   * 获取农事提醒。
   * 对应后端: GET Weather/agricultural-alerts?city={city}
   */
  async getAgriAlerts(city: string): Promise<string> {
    return await this.getAsync(`Weather/agricultural-alerts?city=${encodeURIComponent(city || '昆明')}`);
  }

  /**
   * 获取支持的城市列表（用于前端搜索切换城市）。
   * 对应后端: GET Weather/cities
   */
  async getWeatherCities(): Promise<string> {
    return await this.getAsync('Weather/cities');
  }
}
