import preferences from "@ohos.data.preferences";
import { common } from '@kit.AbilityKit';
import hilog from "@ohos.hilog";
import util from "@ohos.util";
import fileIo from "@ohos.file.fs";
import { OrderItem } from "../viewmodels/OrderViewModels";
type Context = common.Context;
/**
 * 全局应用上下文（单例）。
 *
 * 统一持有 API 基地址、认证 Token、当前登录用户与 IM 网关地址，
 * 取代此前每个页面各持一份 FlowerDataService 导致 token 不共享的问题。
 * 持久化使用 @ohos.data.preferences（键值对，轻量配置适合）。
 */
const TAG = 'GengDi.AppContext';
const STORE_NAME = 'gengdi_app_store';
/** 键名 */
const KEY_BASE_URL = 'baseUrl';
const KEY_AUTH_TOKEN = 'authToken';
const KEY_USER_ID = 'userId';
const KEY_NICKNAME = 'nickname';
const KEY_AVATAR = 'avatar';
const KEY_IM_HOST = 'imHost';
const KEY_IM_PORT = 'imPort';
const KEY_IM_AUTH_TOKEN = 'imAuthToken';
/** IM 令牌签发/更新时间（毫秒），用于客户端主动刷新判断 */
const KEY_IM_TOKEN_TS = 'imAuthTokenUpdatedAt';
const KEY_REFRESH_TOKEN = 'refreshToken';
const KEY_EXPIRES_AT = 'expiresAt';
const KEY_MACHINE_ID = 'machineId';
const KEY_PASSPORT_ID = 'passportId';
/** 当前登录用户信息 */
export class CurrentUser {
    userId: number = 0;
    nickname: string = '';
    avatar: string = '';
    get isValid(): boolean {
        return this.userId > 0;
    }
}
export class AppContext {
    private static instance: AppContext | null = null;
    private prefs: preferences.Preferences | null = null;
    private initing: Promise<void> | null = null;
    private hostContext: Context | null = null;
    private certPreparing: Promise<string> | null = null;
    /** API 基地址（直连宿主机 https 端口，配合内置自签证书 caPath 信任） */
    baseUrl: string = 'https://192.168.1.78:5101/';
    /** 内置服务器证书在沙箱中的落地路径（供 @ohos http caPath 使用，空串表示未就绪） */
    serverCertPath: string = '';
    /** WebAPI Bearer Token（AccessToken） */
    authToken: string = '';
    /** IM 网关鉴权令牌（登录时由服务端按 machineId 加密下发） */
    imAuthToken: string = '';
    /** IM 令牌最近一次获取时间（毫秒）：超过阈值须主动刷新，避免网关"鉴权令牌已过期"拒绝服务 */
    imAuthTokenUpdatedAt: number = 0;
    /** 刷新令牌 */
    refreshToken: string = '';
    /** AccessToken 过期时间（毫秒） */
    expiresAt: number = 0;
    /** 设备唯一标识（登录与 IM Header 共用，参与 ImAuthToken 绑定） */
    machineId: string = '';
    /** 登录通行证 ID */
    passportId: string = '';
    /** IM 网关地址（直连宿主机局域网 IP，与 baseUrl 同网段；网关服务端绑定 192.168.1.78 非 0.0.0.0，NAT 环回 10.0.2.2 不可达） */
    imHost: string = '192.168.1.78';
    imPort: number = 31000;
    /** 当前登录用户 */
    user: CurrentUser = new CurrentUser();
    /** 是否已登录（持有未过期 AccessToken 即视为登录；部分通行证 signin 不回传 userId，不能据此判否） */
    get isSignedIn(): boolean {
        return this.authToken.length > 0 && (this.expiresAt <= 0 || this.expiresAt > Date.now());
    }
    /** 待下单商品列表（用于跨页面传递订单数据） */
    pendingOrderItems: OrderItem[] = [];
    /** 订单是否来自购物车（决定是否清空已选） */
    pendingOrderFromCart: boolean = false;
    static getInstance(): AppContext {
        if (AppContext.instance === null) {
            AppContext.instance = new AppContext();
        }
        return AppContext.instance;
    }
    /** UIAbility 上下文（摄像头等系统服务需要；未初始化时为 null）。 */
    get context(): Context | null {
        return this.hostContext;
    }
    /** 等待初始化完成（供请求层阻塞，确保证书/会话就绪）。 */
    async whenReady(): Promise<void> {
        if (this.prefs !== null) {
            return;
        }
        if (this.initing !== null) {
            await this.initing;
        }
    }

    /** 读取字符串偏好（等待初始化完成；未初始化或键不存在时返回默认值，供业务模块复用同一存储） */
    async getPrefString(key: string, def: string): Promise<string> {
        await this.whenReady();
        if (this.prefs === null) {
            return def;
        }
        return await this.prefs.get(key, def) as string;
    }

    /** 写入字符串偏好并落盘（等待初始化完成） */
    async putPrefString(key: string, value: string): Promise<void> {
        await this.whenReady();
        if (this.prefs === null) {
            return;
        }
        await this.prefs.put(key, value);
        await this.prefs.flush();
    }
    /** 使用 UIAbility 上下文初始化并从本地恢复配置。幂等。 */
    async initialize(context: Context): Promise<void> {
        this.hostContext = context;
        if (this.prefs !== null) {
            return;
        }
        if (this.initing !== null) {
            await this.initing;
            return;
        }
        this.initing = (async () => {
            try {
                this.prefs = await preferences.getPreferences(context, STORE_NAME);
                this.baseUrl = await this.prefs.get(KEY_BASE_URL, this.baseUrl) as string;
                this.authToken = await this.prefs.get(KEY_AUTH_TOKEN, '') as string;
                this.imHost = await this.prefs.get(KEY_IM_HOST, this.imHost) as string;
                this.imPort = await this.prefs.get(KEY_IM_PORT, this.imPort) as number;
                this.user.userId = await this.prefs.get(KEY_USER_ID, 0) as number;
                this.user.nickname = await this.prefs.get(KEY_NICKNAME, '') as string;
                this.user.avatar = await this.prefs.get(KEY_AVATAR, '') as string;
                this.imAuthToken = await this.prefs.get(KEY_IM_AUTH_TOKEN, '') as string;
                this.imAuthTokenUpdatedAt = await this.prefs.get(KEY_IM_TOKEN_TS, 0) as number;
                this.refreshToken = await this.prefs.get(KEY_REFRESH_TOKEN, '') as string;
                this.expiresAt = await this.prefs.get(KEY_EXPIRES_AT, 0) as number;
                this.machineId = await this.prefs.get(KEY_MACHINE_ID, '') as string;
                this.passportId = await this.prefs.get(KEY_PASSPORT_ID, '') as string;
                if (this.machineId.length === 0) {
                    this.machineId = util.generateRandomUUID(false);
                    await this.prefs.put(KEY_MACHINE_ID, this.machineId);
                    await this.prefs.flush();
                }
                // 旧默认地址（http 模拟器通道会被服务端 307 到自签 https）迁移到证书信任的直连地址
                if (this.baseUrl.includes('10.0.2.2')) {
                    this.baseUrl = 'https://192.168.1.78:5101/';
                    await this.prefs.put(KEY_BASE_URL, this.baseUrl);
                    await this.prefs.flush();
                }
                // IM 网关同样迁移：服务端监听 192.168.1.78:31000（非 0.0.0.0），模拟器 NAT 环回 10.0.2.2 不可达
                if (this.imHost.includes('10.0.2.2')) {
                    this.imHost = '192.168.1.78';
                    await this.prefs.put(KEY_IM_HOST, this.imHost);
                    await this.prefs.flush();
                }
            }
            catch (e) {
                hilog.error(0x0001, TAG, '初始化偏好失败: %{public}s', (e as Error).message);
            }
        })();
        await this.initing;
        this.initing = null;
    }
    /**
     * 把 rawfile 中的服务器自签证书复制到沙箱文件，返回路径（供 http caPath）。
     * 幂等：已存在则直接返回。
     */
    async ensureServerCert(): Promise<string> {
        if (this.serverCertPath.length > 0) {
            return this.serverCertPath;
        }
        if (this.certPreparing !== null) {
            return await this.certPreparing;
        }
        this.certPreparing = this.prepareServerCertInternal();
        const path = await this.certPreparing;
        this.certPreparing = null;
        return path;
    }
    private async prepareServerCertInternal(): Promise<string> {
        if (this.hostContext === null) {
            hilog.error(0x0001, TAG, '服务器证书落地失败: hostContext 未初始化');
            return '';
        }
        const target = `${this.hostContext.filesDir}/server_cert.pem`;
        try {
            // 注意：fileIo.access 在文件不存在时抛 13900002 而非返回 false，须以 stat 探测
            let exists = false;
            try {
                fileIo.statSync(target);
                exists = true;
            }
            catch (_e) {
                exists = false;
            }
            if (!exists) {
                const content: Uint8Array = await this.hostContext.resourceManager.getRawFileContent('server_cert.pem');
                const file = fileIo.openSync(target, fileIo.OpenMode.READ_WRITE | fileIo.OpenMode.CREATE | fileIo.OpenMode.TRUNC);
                fileIo.writeSync(file.fd, content.buffer.slice(content.byteOffset, content.byteOffset + content.byteLength) as ArrayBuffer);
                fileIo.closeSync(file);
            }
            this.serverCertPath = target;
            hilog.info(0x0001, TAG, '服务器证书就绪: %{public}s', target);
            return target;
        }
        catch (e) {
            hilog.error(0x0001, TAG, '服务器证书落地失败: %{public}s', (e as Error).message);
            return '';
        }
    }
    async setBaseUrl(url: string): Promise<void> {
        if (url && url.length > 0) {
            this.baseUrl = url.endsWith('/') ? url : url + '/';
            await this.prefs?.put(KEY_BASE_URL, this.baseUrl);
            await this.prefs?.flush();
        }
    }
    async setAuthToken(token: string): Promise<void> {
        this.authToken = token;
        await this.prefs?.put(KEY_AUTH_TOKEN, token);
        await this.prefs?.flush();
    }
    async setImEndpoint(host: string, port: number): Promise<void> {
        this.imHost = host;
        this.imPort = port;
        await this.prefs?.put(KEY_IM_HOST, host);
        await this.prefs?.put(KEY_IM_PORT, port);
        await this.prefs?.flush();
    }
    async setCurrentUser(userId: number, nickname: string, avatar: string): Promise<void> {
        this.user.userId = userId;
        this.user.nickname = nickname;
        this.user.avatar = avatar;
        await this.prefs?.put(KEY_USER_ID, userId);
        await this.prefs?.put(KEY_NICKNAME, nickname);
        await this.prefs?.put(KEY_AVATAR, avatar);
        await this.prefs?.flush();
    }
    /** 登录成功后写入完整会话（对齐 C# AccountService.SetSession） */
    async setLoginSession(s: LoginSession): Promise<void> {
        this.authToken = s.accessToken;
        this.imAuthToken = s.imAuthToken;
        this.imAuthTokenUpdatedAt = Date.now();
        this.refreshToken = s.refreshToken;
        this.expiresAt = s.expiresAt > 0 ? s.expiresAt : Date.now() + Math.max(s.expiresIn, 60) * 1000;
        this.passportId = s.passportId;
        await this.prefs?.put(KEY_AUTH_TOKEN, this.authToken);
        await this.prefs?.put(KEY_IM_AUTH_TOKEN, this.imAuthToken);
        await this.prefs?.put(KEY_IM_TOKEN_TS, this.imAuthTokenUpdatedAt);
        await this.prefs?.put(KEY_REFRESH_TOKEN, this.refreshToken);
        await this.prefs?.put(KEY_EXPIRES_AT, this.expiresAt);
        await this.prefs?.put(KEY_PASSPORT_ID, this.passportId);
        await this.prefs?.flush();
    }
    /** 退出登录：清空会话，但保留记住的通行证号与服务器配置/machineId，下次登录自动带出 */
    async signOut(): Promise<void> {
        this.authToken = '';
        this.imAuthToken = '';
        this.imAuthTokenUpdatedAt = 0;
        this.refreshToken = '';
        this.expiresAt = 0;
        this.user = new CurrentUser();
        await this.prefs?.delete(KEY_AUTH_TOKEN);
        await this.prefs?.delete(KEY_IM_AUTH_TOKEN);
        await this.prefs?.delete(KEY_IM_TOKEN_TS);
        await this.prefs?.delete(KEY_REFRESH_TOKEN);
        await this.prefs?.delete(KEY_EXPIRES_AT);
        await this.prefs?.delete(KEY_USER_ID);
        await this.prefs?.delete(KEY_NICKNAME);
        await this.prefs?.delete(KEY_AVATAR);
        await this.prefs?.flush();
    }
}
/** 登录结果（Account/signin 返回 LoginResultDto，camelCase） */
export class LoginSession {
    accessToken: string = '';
    refreshToken: string = '';
    expiresIn: number = 0;
    expiresAt: number = 0;
    imAuthToken: string = '';
    userId: number = 0;
    passportId: string = '';
    nickname: string = '';
}
