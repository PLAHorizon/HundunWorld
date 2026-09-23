import socket from "@ohos.net.socket";
import hilog from "@ohos.hilog";
import util from "@ohos.util";
import { MemoryPackReader, MemoryPackWriter } from "../../services/im/codec/MemoryPackCodec";
import { FrameCodec } from "../../services/im/codec/FrameCodec";
import type { ImFrame, UnpackResult } from "../../services/im/codec/FrameCodec";
import { Lz4Block } from "../../services/im/codec/Lz4Block";
import { ImPacket, ImPacketBody, ImHeader, ImUnionTag, ImServiceType, ImPrivateChatSend, ImPrivateChatNotify, ImGroupChatSend, ImGroupChatNotify, ImChatAck, ImChatRecallMessage, ImChatReadReceipt, ImTypingIndicator, ImContactListRequest, ImContactAddRequest, ImContactAddResponse, ImContactRemoveRequest, ImContactRemoveResponse, ImContactRequestHandleRequest, ImContactRequestHandleResponse, ImPendingContactRequestListRequest, ImPendingContactRequestListResponse, ImContactOnlineStatus, ImConversationListRequest, ImConversationDeleteMessage, ImConversationPinMessage, ImConversationMuteMessage, ImChatHistoryQueryRequest, ImHeartbeatMessage, ImSystemNotification, ImCallSignal, ImCallSignalAck } from "../../services/im/ImMessages";
import type { ImContactListResponse, ImConversationListResponse, ImChatHistoryQueryResponse, ImErrorMessage } from "../../services/im/ImMessages";
import { AppContext } from "../../model/AppContext";
import { ApiClient } from "../ApiClient";
/**
 * IM 会话层：持久 TCP 连接 + 心跳 + 请求-响应关联 + 推送分发。
 * 语义对齐 C# ImGatewayContactClient（超时 15s、重试 2 次指数退避）。
 */
const TAG = 'GengDi.ImSession';
const DOMAIN = 0x0001;
const REQUEST_TIMEOUT_MS = 15000;
const MAX_RETRY = 2;
/** 心跳间隔（对齐 C# 客户端 15s；网关空闲超时 45s） */
const HEARTBEAT_INTERVAL_MS = 15000;
/** IM 令牌主动刷新阈值：超过该时长未刷新即经 refresh-token 接口换新（服务端共享令牌 168h，兼容旧版 24h 令牌） */
const IM_TOKEN_PROACTIVE_REFRESH_MS = 20 * 3600 * 1000;
/** 刷新接口最小调用间隔（防抖，避免失败风暴） */
const IM_TOKEN_REFRESH_MIN_INTERVAL_MS = 30 * 1000;
type PacketCallback = (packet: ImPacket) => void;
class PendingEntry {
    messageId: string = '';
    resolve: (p: ImPacket) => void = (_p: ImPacket) => {
    };
    reject: (e: Error) => void = (_e: Error) => {
    };
    timer: number = -1;
}
export class ImSession {
    private static instance: ImSession | null = null;
    static getInstance(): ImSession {
        if (ImSession.instance === null) {
            ImSession.instance = new ImSession();
        }
        return ImSession.instance;
    }
    private tcp: socket.TCPSocket | null = null;
    private connected: boolean = false;
    private connecting: Promise<boolean> | null = null;
    private recvBuffer: Uint8Array = new Uint8Array(0);
    private pending: Map<string, PendingEntry> = new Map();
    private heartbeatTimer: number = -1;
    private reconnectAttempts: number = 0;
    private closedByUser: boolean = false;
    private sequenceSeed: number = 1;
    private refreshingToken: Promise<boolean> | null = null;
    private lastRefreshAttempt: number = 0;
    /** 推送回调（按需订阅） */
    onPrivateChatReceived: ((m: ImPrivateChatNotify) => void) | null = null;
    onGroupChatReceived: ((m: ImGroupChatNotify) => void) | null = null;
    onChatAckReceived: ((m: ImChatAck) => void) | null = null;
    /** 撤回通知推送（自己/对方撤回均会推送；多端同步） */
    onRecallReceived: ((m: ImChatRecallMessage) => void) | null = null;
    onReadReceiptReceived: ((m: ImChatReadReceipt) => void) | null = null;
    onTypingReceived: ((m: ImTypingIndicator) => void) | null = null;
    onContactStatusReceived: ((m: ImContactOnlineStatus) => void) | null = null;
    onSystemNotificationReceived: ((m: ImSystemNotification) => void) | null = null;
    onCallSignalReceived: ((m: ImCallSignal) => void) | null = null;
    onConnectionChanged: ((connected: boolean) => void) | null = null;
    get isConnected(): boolean {
        return this.connected;
    }
    /** 建立连接（幂等，复用进行中的连接过程） */
    async ensureConnected(): Promise<boolean> {
        if (this.connected) {
            return true;
        }
        if (this.connecting !== null) {
            return await this.connecting;
        }
        this.closedByUser = false;
        this.connecting = this.connectInternal();
        const ok = await this.connecting;
        this.connecting = null;
        return ok;
    }
    private async connectInternal(): Promise<boolean> {
        const app = AppContext.getInstance();
        try {
            // 令牌新鲜度保障：本地缓存的 IM 令牌可能已过服务端有效期（历史版本签发 24h），
            // 连接前先经 refresh-token 接口换新，避免网关"鉴权令牌已过期"拒绝服务。
            await this.ensureImTokenFresh();
            this.tcp = socket.constructTCPSocketInstance();
            this.tcp.on('message', (info: socket.SocketMessageInfo) => {
                this.handleRecv(new Uint8Array(info.message));
            });
            this.tcp.on('close', () => {
                this.handleDisconnected();
            });
            this.tcp.on('error', (err: Error) => {
                hilog.error(DOMAIN, TAG, 'TCP 错误: %{public}s', err.message);
                this.handleDisconnected();
            });
            await this.tcp.connect({
                address: { address: app.imHost, port: app.imPort, family: 1 },
                timeout: 8000
            });
            this.connected = true;
            this.reconnectAttempts = 0;
            this.startHeartbeat();
            this.onConnectionChanged?.(true);
            hilog.info(DOMAIN, TAG, 'IM 网关已连接 %{public}s:%{public}d', app.imHost, app.imPort);
            return true;
        }
        catch (e) {
            hilog.error(DOMAIN, TAG, 'IM 网关连接失败: %{public}s', (e as Error).message);
            this.tcp = null;
            this.scheduleReconnect();
            return false;
        }
    }
    async close(): Promise<void> {
        this.closedByUser = true;
        this.stopHeartbeat();
        this.failAllPending(new Error('连接已关闭'));
        try {
            await this.tcp?.close();
        }
        catch (e) {
            hilog.warn(DOMAIN, TAG, '关闭连接异常: %{public}s', (e as Error).message);
        }
        this.tcp = null;
        this.connected = false;
    }
    private scheduleReconnect(): void {
        if (this.closedByUser) {
            return;
        }
        this.reconnectAttempts++;
        if (this.reconnectAttempts > 10) {
            hilog.error(DOMAIN, TAG, '重连次数超限，停止重连');
            return;
        }
        const delay = Math.min(1000 * (1 << Math.min(this.reconnectAttempts, 5)), 30000);
        setTimeout(() => {
            if (!this.closedByUser && !this.connected) {
                this.ensureConnected();
            }
        }, delay);
    }
    private handleDisconnected(): void {
        if (!this.connected) {
            return;
        }
        this.connected = false;
        this.stopHeartbeat();
        this.failAllPending(new Error('连接断开'));
        this.onConnectionChanged?.(false);
        this.scheduleReconnect();
    }
    // ===== IM 令牌刷新 =====
    /**
     * IM 令牌新鲜度保障（幂等，并发合并）：
     * 令牌为空或距上次获取超过阈值时，经 refresh-token 接口（携带旧 IM 令牌）换取新共享令牌。
     */
    private ensureImTokenFresh(): Promise<boolean> {
        const app = AppContext.getInstance();
        const isFresh = app.imAuthToken.length > 0
            && app.imAuthTokenUpdatedAt > 0
            && (Date.now() - app.imAuthTokenUpdatedAt) < IM_TOKEN_PROACTIVE_REFRESH_MS;
        if (isFresh) {
            return Promise.resolve(true);
        }
        if (app.refreshToken.length === 0) {
            // 无刷新凭据：保留现有令牌直接连接（令牌仍可能有效；失败走响应式刷新/重新登录）
            return Promise.resolve(true);
        }
        if (this.refreshingToken !== null) {
            return this.refreshingToken;
        }
        if (Date.now() - this.lastRefreshAttempt < IM_TOKEN_REFRESH_MIN_INTERVAL_MS) {
            return Promise.resolve(false);
        }
        this.refreshingToken = this.refreshImTokenInternal();
        return this.refreshingToken;
    }
    private async refreshImTokenInternal(): Promise<boolean> {
        this.lastRefreshAttempt = Date.now();
        const app = AppContext.getInstance();
        try {
            const session = await ApiClient.getInstance().refreshTokenAsync(app.refreshToken, app.imAuthToken);
            if (session.imAuthToken.length === 0) {
                // 服务端未能重签 IM 令牌（旧令牌无法解密身份）：沿用现令牌，仅更新 WebApi 会话
                session.imAuthToken = app.imAuthToken;
            }
            await app.setLoginSession(session);
            hilog.info(DOMAIN, TAG, 'IM 令牌刷新成功');
            return true;
        }
        catch (e) {
            hilog.warn(DOMAIN, TAG, 'IM 令牌刷新失败: %{public}s', (e as Error).message);
            return false;
        }
        finally {
            this.refreshingToken = null;
        }
    }
    /** 网关鉴权拒绝的特征匹配（错误消息由 IMNetworkServer.SendAuthErrorAsync 下发） */
    private isAuthError(message: string): boolean {
        return message.includes('重新登录') || message.includes('鉴权');
    }
    // ===== 收包 =====
    private handleRecv(chunk: Uint8Array): void {
        const merged = new Uint8Array(this.recvBuffer.length + chunk.length);
        merged.set(this.recvBuffer, 0);
        merged.set(chunk, this.recvBuffer.length);
        const result: UnpackResult = FrameCodec.unpackAll(merged);
        this.recvBuffer = merged.subarray(result.consumed);
        for (const frame of result.frames) {
            this.handleFrame(frame);
        }
    }
    private handleFrame(frame: ImFrame): void {
        try {
            if (FrameCodec.checksum(frame.payload) !== frame.checksum) {
                hilog.warn(DOMAIN, TAG, '校验和不匹配，丢弃帧');
                return;
            }
            let payload = frame.payload;
            if (frame.compressed) {
                if (payload.length < 4) {
                    return;
                }
                const dv = new DataView(payload.buffer, payload.byteOffset, payload.byteLength);
                const origLen = dv.getInt32(0, true);
                const decompressed = Lz4Block.decompress(payload.subarray(4), origLen);
                if (decompressed === null) {
                    hilog.warn(DOMAIN, TAG, 'LZ4 解压失败，丢弃帧');
                    return;
                }
                payload = decompressed;
            }
            const packet = ImPacket.read(new MemoryPackReader(payload));
            this.dispatchPacket(packet);
        }
        catch (e) {
            hilog.error(DOMAIN, TAG, '帧处理异常: %{public}s', (e as Error).message);
        }
    }
    private dispatchPacket(packet: ImPacket): void {
        const header = packet.header;
        // 响应：关联到挂起请求
        if (header.isResponse && header.responseToMessageId.length > 0) {
            const entry = this.pending.get(header.responseToMessageId);
            if (entry !== undefined) {
                this.clearPending(entry.messageId);
                entry.resolve(packet);
                return;
            }
        }
        // 推送
        const body = packet.body;
        if (body === null || body.message === null) {
            return;
        }
        const m = body.message;
        if (m instanceof ImPrivateChatNotify) {
            this.onPrivateChatReceived?.(m);
        }
        else if (m instanceof ImGroupChatNotify) {
            this.onGroupChatReceived?.(m);
        }
        else if (m instanceof ImChatAck) {
            this.onChatAckReceived?.(m);
        }
        else if (m instanceof ImChatRecallMessage) {
            this.onRecallReceived?.(m);
        }
        else if (m instanceof ImChatReadReceipt) {
            this.onReadReceiptReceived?.(m);
        }
        else if (m instanceof ImTypingIndicator) {
            this.onTypingReceived?.(m);
        }
        else if (m instanceof ImContactOnlineStatus) {
            this.onContactStatusReceived?.(m);
        }
        else if (m instanceof ImSystemNotification) {
            this.onSystemNotificationReceived?.(m);
        }
        else if (m instanceof ImCallSignal) {
            this.onCallSignalReceived?.(m);
        }
    }
    // ===== 发包 =====
    private buildPacket(serviceType: number, body: ImPacketBody): ImPacket {
        const app = AppContext.getInstance();
        const meta = resolveMessageMeta(body);
        const header = new ImHeader();
        header.userId = BigInt(app.user.userId);
        header.messageId = this.newMessageId();
        header.timestamp = Date.now();
        header.sequenceId = this.sequenceSeed++;
        header.requireResponse = true;
        // 网关按 Header.MessageType 路由处理器，缺失时会被判为 Unknown 并拒绝
        header.messageType = meta.type;
        header.authToken = app.imAuthToken;
        header.machineId = app.machineId;
        header.extensionData.set('nickname', app.user.nickname);
        header.extensionData.set('avatar', app.user.avatar);
        const packet = new ImPacket();
        packet.serviceType = serviceType;
        packet.header = header;
        packet.body = body;
        return packet;
    }
    private newMessageId(): string {
        const uuid = util.generateRandomUUID(false);
        return uuid.replace(/-/g, '');
    }
    private async sendPacket(packet: ImPacket): Promise<void> {
        const ok = await this.ensureConnected();
        if (!ok || this.tcp === null) {
            throw new Error('IM 网关未连接');
        }
        const w = new MemoryPackWriter(512);
        packet.write(w);
        const frame = FrameCodec.pack(w.toUint8Array(), packet.header.messageType & 0xFF);
        await this.tcp.send({ data: frame.buffer as ArrayBuffer });
    }
    /** 请求-响应（超时 15s，失败重试指数退避，对齐 C# 客户端；鉴权失败自动刷新令牌后重试一次） */
    private async request<T>(serviceType: number, body: ImPacketBody, extract: (p: ImPacket) => T): Promise<T> {
        let lastError: Error = new Error('未知错误');
        let authRefreshed = false;
        for (let attempt = 0; attempt <= MAX_RETRY; attempt++) {
            if (attempt > 0) {
                await new Promise<void>(resolve => setTimeout(resolve, Math.min(500 * (1 << (attempt - 1)), 2000)));
            }
            const packet = this.buildPacket(serviceType, body);
            const messageId = packet.header.messageId;
            const promise = new Promise<ImPacket>((resolve, reject) => {
                const entry = new PendingEntry();
                entry.messageId = messageId;
                entry.resolve = resolve;
                entry.reject = reject;
                entry.timer = setTimeout(() => {
                    this.clearPending(messageId);
                    reject(new Error('IM 请求超时'));
                }, REQUEST_TIMEOUT_MS);
                this.pending.set(messageId, entry);
            });
            try {
                await this.sendPacket(packet);
                const response = await promise;
                if (response.body !== null && response.body.tag === ImUnionTag.Error) {
                    const err = response.body.message as ImErrorMessage;
                    throw new Error(err.message.length > 0 ? err.message : err.details);
                }
                return extract(response);
            }
            catch (e) {
                lastError = e as Error;
                if ((e as Error).message === 'IM 请求超时') {
                    continue; // 超时才重试
                }
                // 网关鉴权拒绝（令牌过期等）：刷新 IM 令牌后重试一次
                if (this.isAuthError(lastError.message) && !authRefreshed) {
                    const refreshed = await this.refreshImTokenInternal();
                    if (refreshed) {
                        authRefreshed = true;
                        continue;
                    }
                }
                throw lastError;
            }
        }
        throw lastError;
    }
    private clearPending(messageId: string): void {
        const entry = this.pending.get(messageId);
        if (entry !== undefined) {
            if (entry.timer >= 0) {
                clearTimeout(entry.timer);
            }
            this.pending.delete(messageId);
        }
    }
    private failAllPending(err: Error): void {
        const entries = Array.from(this.pending.values());
        this.pending.clear();
        for (const entry of entries) {
            if (entry.timer >= 0) {
                clearTimeout(entry.timer);
            }
            entry.reject(err);
        }
    }
    // ===== 心跳 =====
    /** 立即发送一次心跳（Header 携带 AuthToken/MachineId 完成网关用户绑定）。 */
    private sendHeartbeatOnce(): void {
        const hb = new ImHeartbeatMessage();
        hb.userId = BigInt(AppContext.getInstance().user.userId);
        hb.clientTimestamp = Date.now();
        const body = new ImPacketBody();
        body.tag = ImUnionTag.Heartbeat;
        body.message = hb;
        const packet = this.buildPacket(ImServiceType.Gateway, body);
        packet.header.requireResponse = false;
        this.sendPacket(packet).catch((e: Error) => {
            hilog.warn(DOMAIN, TAG, '心跳发送失败: %{public}s', e.message);
        });
    }
    private startHeartbeat(): void {
        this.stopHeartbeat();
        // 连接建立即发首跳：网关只在收到心跳后才将连接绑定到用户并注册主动推送
        // （ShouldBindSession 仅认心跳）。若只靠周期定时器（首跳要等一个间隔），
        // 期间来电信令等推送全部不可达——表现为"服务端先启动、鸿蒙登录后立即
        // 测试通话完全失败"。重连成功后同样经此立即完成再绑定。
        this.sendHeartbeatOnce();
        this.heartbeatTimer = setInterval(() => {
            this.sendHeartbeatOnce();
        }, HEARTBEAT_INTERVAL_MS);
    }
    private stopHeartbeat(): void {
        if (this.heartbeatTimer >= 0) {
            clearInterval(this.heartbeatTimer);
            this.heartbeatTimer = -1;
        }
    }
    // ===== 业务 API =====
    /** 发送私聊消息 */
    async sendPrivateChatAsync(receiverId: bigint, content: string, clientMessageId: string, contentType: number = 0): Promise<void> {
        const app = AppContext.getInstance();
        const m = new ImPrivateChatSend();
        m.senderId = BigInt(app.user.userId);
        m.senderName = app.user.nickname;
        m.senderAvatar = app.user.avatar;
        m.receiverId = receiverId;
        m.content = content;
        m.contentType = contentType;
        m.clientMessageId = clientMessageId;
        m.timestamp = Date.now();
        const body = new ImPacketBody();
        body.tag = ImUnionTag.PrivateChatSend;
        body.message = m;
        await this.request(ImServiceType.Chat, body, (_p: ImPacket) => true);
    }
    /** 发送群聊消息 */
    async sendGroupChatAsync(groupId: bigint, content: string, clientMessageId: string, contentType: number = 0): Promise<void> {
        const app = AppContext.getInstance();
        const m = new ImGroupChatSend();
        m.senderId = BigInt(app.user.userId);
        m.senderName = app.user.nickname;
        m.senderAvatar = app.user.avatar;
        m.groupId = groupId;
        m.content = content;
        m.contentType = contentType;
        m.clientMessageId = clientMessageId;
        m.timestamp = Date.now();
        const body = new ImPacketBody();
        body.tag = ImUnionTag.GroupChatSend;
        body.message = m;
        await this.request(ImServiceType.Group, body, (_p: ImPacket) => true);
    }
    /** 发送通话信令（语音/视频通话状态流转；服务端转发到对端并返回受理结果） */
    async sendCallSignalAsync(signal: ImCallSignal): Promise<ImCallSignalAck> {
        const body = new ImPacketBody();
        body.tag = ImUnionTag.CallSignal;
        body.message = signal;
        return await this.request<ImCallSignalAck>(ImServiceType.Call, body, (p: ImPacket) => {
            return p.body?.message as ImCallSignalAck;
        });
    }
    /** 拉取会话列表 */
    async getConversationListAsync(): Promise<ImConversationListResponse> {
        const m = new ImConversationListRequest();
        m.userId = BigInt(AppContext.getInstance().user.userId);
        m.limit = 100;
        const body = new ImPacketBody();
        body.tag = ImUnionTag.ConversationListRequest;
        body.message = m;
        return await this.request<ImConversationListResponse>(ImServiceType.Chat, body, (p: ImPacket) => {
            return p.body?.message as ImConversationListResponse;
        });
    }
    /** 拉取联系人列表 */
    async getContactListAsync(): Promise<ImContactListResponse> {
        const m = new ImContactListRequest();
        m.userId = BigInt(AppContext.getInstance().user.userId);
        m.limit = 200;
        const body = new ImPacketBody();
        body.tag = ImUnionTag.ContactListRequest;
        body.message = m;
        return await this.request<ImContactListResponse>(ImServiceType.Contact, body, (p: ImPacket) => {
            return p.body?.message as ImContactListResponse;
        });
    }
    /** 发送好友申请（对方确认通过后成为好友） */
    async addContactAsync(targetUserId: bigint, verifyMessage: string, remarkName: string): Promise<ImContactAddResponse> {
        const app = AppContext.getInstance();
        const m = new ImContactAddRequest();
        m.userId = BigInt(app.user.userId);
        m.targetUserId = targetUserId;
        m.verifyMessage = verifyMessage;
        m.remarkName = remarkName;
        m.source = 'search';
        m.requesterName = app.user.nickname;
        m.requesterAvatar = app.user.avatar;
        const body = new ImPacketBody();
        body.tag = ImUnionTag.ContactAddRequest;
        body.message = m;
        return await this.request<ImContactAddResponse>(ImServiceType.Contact, body, (p: ImPacket) => {
            return p.body?.message as ImContactAddResponse;
        });
    }
    /** 删除好友（双向解除好友关系） */
    async removeContactAsync(targetUserId: bigint): Promise<ImContactRemoveResponse> {
        const m = new ImContactRemoveRequest();
        m.userId = BigInt(AppContext.getInstance().user.userId);
        m.targetUserId = targetUserId;
        const body = new ImPacketBody();
        body.tag = ImUnionTag.ContactRemoveRequest;
        body.message = m;
        return await this.request<ImContactRemoveResponse>(ImServiceType.Contact, body, (p: ImPacket) => {
            return p.body?.message as ImContactRemoveResponse;
        });
    }
    /** 拉取待处理好友申请列表 */
    async getPendingContactRequestsAsync(): Promise<ImPendingContactRequestListResponse> {
        const m = new ImPendingContactRequestListRequest();
        m.userId = BigInt(AppContext.getInstance().user.userId);
        m.offset = 0;
        m.limit = 50;
        const body = new ImPacketBody();
        body.tag = ImUnionTag.ContactPendingListRequest;
        body.message = m;
        return await this.request<ImPendingContactRequestListResponse>(ImServiceType.Contact, body, (p: ImPacket) => {
            return p.body?.message as ImPendingContactRequestListResponse;
        });
    }
    /** 处理好友申请（接受/拒绝） */
    async handleContactRequestAsync(requesterId: bigint, accept: boolean): Promise<ImContactRequestHandleResponse> {
        const m = new ImContactRequestHandleRequest();
        m.userId = BigInt(AppContext.getInstance().user.userId);
        m.requesterId = requesterId;
        m.accept = accept;
        const body = new ImPacketBody();
        body.tag = ImUnionTag.ContactRequestHandleRequest;
        body.message = m;
        return await this.request<ImContactRequestHandleResponse>(ImServiceType.Contact, body, (p: ImPacket) => {
            return p.body?.message as ImContactRequestHandleResponse;
        });
    }
    /** 拉取私聊历史（锚点翻页） */
    async getPrivateHistoryAsync(conversationId: string, peerId: bigint, anchorMessageId: string, count: number): Promise<ImChatHistoryQueryResponse> {
        const m = new ImChatHistoryQueryRequest();
        m.userId = BigInt(AppContext.getInstance().user.userId);
        m.conversationId = conversationId;
        m.peerId = peerId;
        m.count = count;
        m.anchorMessageId = anchorMessageId;
        m.endTime = Date.now();
        const body = new ImPacketBody();
        body.tag = ImUnionTag.ChatHistoryQueryRequest;
        body.message = m;
        return await this.request<ImChatHistoryQueryResponse>(ImServiceType.Chat, body, (p: ImPacket) => {
            return p.body?.message as ImChatHistoryQueryResponse;
        });
    }
    /** 发送已读回执 */
    async sendReadReceiptAsync(peerId: bigint, lastReadMessageId: string): Promise<void> {
        const m = new ImChatReadReceipt();
        m.userId = BigInt(AppContext.getInstance().user.userId);
        m.peerId = peerId;
        m.lastReadMessageId = lastReadMessageId;
        m.timestamp = Date.now();
        const body = new ImPacketBody();
        body.tag = ImUnionTag.ChatReadReceipt;
        body.message = m;
        const packet = this.buildPacket(ImServiceType.Chat, body);
        packet.header.requireResponse = false;
        await this.sendPacket(packet);
    }
    /**
     * 撤回消息（服务端校验 48 小时窗口与发送者身份；成功不回包，
     * 撤回生效通知经 onRecallReceived 推送回本端与对端）。
     */
    async sendRecallAsync(serverMessageId: string, peerId: bigint, isGroup: boolean): Promise<void> {
        const m = new ImChatRecallMessage();
        m.recalledMessageId = serverMessageId;
        m.userId = BigInt(AppContext.getInstance().user.userId);
        m.peerId = peerId;
        m.chatRelationType = isGroup ? 2 : 0;
        m.timestamp = Date.now();
        const body = new ImPacketBody();
        body.tag = ImUnionTag.ChatRecall;
        body.message = m;
        const packet = this.buildPacket(ImServiceType.Chat, body);
        packet.header.requireResponse = false;
        await this.sendPacket(packet);
    }
    /** 发送正在输入指示 */
    async sendTypingAsync(peerId: bigint, isTyping: boolean): Promise<void> {
        const m = new ImTypingIndicator();
        m.userId = BigInt(AppContext.getInstance().user.userId);
        m.peerId = peerId;
        m.isTyping = isTyping;
        const body = new ImPacketBody();
        body.tag = ImUnionTag.TypingIndicator;
        body.message = m;
        const packet = this.buildPacket(ImServiceType.Chat, body);
        packet.header.requireResponse = false;
        await this.sendPacket(packet);
    }
    /**
     * 删除会话（服务端成功不回包，fire-and-forget；失败下次会话列表刷新会被纠正）。
     * 服务端仅删除会话条目，本地消息记录由 ImLocalStore.deleteConversation 一并清理。
     */
    async deleteConversationAsync(conversationId: string): Promise<void> {
        const m = new ImConversationDeleteMessage();
        m.userId = BigInt(AppContext.getInstance().user.userId);
        m.conversationId = conversationId;
        const body = new ImPacketBody();
        body.tag = ImUnionTag.ConversationDelete;
        body.message = m;
        const packet = this.buildPacket(ImServiceType.Chat, body);
        packet.header.requireResponse = false;
        await this.sendPacket(packet);
    }
    /** 置顶/取消置顶会话（服务端持久化到用户 Grain，成功不回包） */
    async pinConversationAsync(conversationId: string, isPinned: boolean): Promise<void> {
        const m = new ImConversationPinMessage();
        m.userId = BigInt(AppContext.getInstance().user.userId);
        m.conversationId = conversationId;
        m.isPinned = isPinned;
        const body = new ImPacketBody();
        body.tag = ImUnionTag.ConversationPin;
        body.message = m;
        const packet = this.buildPacket(ImServiceType.Chat, body);
        packet.header.requireResponse = false;
        await this.sendPacket(packet);
    }
    /** 设置会话免打扰（服务端持久化到用户 Grain，成功不回包） */
    async muteConversationAsync(conversationId: string, isMuted: boolean): Promise<void> {
        const m = new ImConversationMuteMessage();
        m.userId = BigInt(AppContext.getInstance().user.userId);
        m.conversationId = conversationId;
        m.isMuted = isMuted;
        const body = new ImPacketBody();
        body.tag = ImUnionTag.ConversationMute;
        body.message = m;
        const packet = this.buildPacket(ImServiceType.Chat, body);
        packet.header.requireResponse = false;
        await this.sendPacket(packet);
    }
}
/** 从消息体实例解析 (MessageType, ServiceType)——对齐 C# ResolveMessageMetadata */
class ImMessageMeta {
    type: number = 0;
    serviceType: number = 0;
}
function resolveMessageMeta(body: ImPacketBody): ImMessageMeta {
    const meta = new ImMessageMeta();
    const m = body.message;
    if (m instanceof ImPrivateChatSend) {
        meta.type = m.type;
        meta.serviceType = m.serviceType;
    }
    else if (m instanceof ImGroupChatSend) {
        meta.type = m.type;
        meta.serviceType = m.serviceType;
    }
    else if (m instanceof ImChatReadReceipt) {
        meta.type = m.type;
        meta.serviceType = m.serviceType;
    }
    else if (m instanceof ImChatRecallMessage) {
        meta.type = m.type;
        meta.serviceType = m.serviceType;
    }
    else if (m instanceof ImTypingIndicator) {
        meta.type = m.type;
        meta.serviceType = m.serviceType;
    }
    else if (m instanceof ImHeartbeatMessage) {
        meta.type = m.type;
        meta.serviceType = m.serviceType;
    }
    else if (m instanceof ImContactListRequest) {
        meta.type = m.type;
        meta.serviceType = m.serviceType;
    }
    else if (m instanceof ImContactAddRequest) {
        meta.type = m.type;
        meta.serviceType = m.serviceType;
    }
    else if (m instanceof ImContactRemoveRequest) {
        meta.type = m.type;
        meta.serviceType = m.serviceType;
    }
    else if (m instanceof ImContactRequestHandleRequest) {
        meta.type = m.type;
        meta.serviceType = m.serviceType;
    }
    else if (m instanceof ImPendingContactRequestListRequest) {
        meta.type = m.type;
        meta.serviceType = m.serviceType;
    }
    else if (m instanceof ImConversationListRequest) {
        meta.type = m.type;
        meta.serviceType = m.serviceType;
    }
    else if (m instanceof ImConversationDeleteMessage) {
        meta.type = m.type;
        meta.serviceType = m.serviceType;
    }
    else if (m instanceof ImConversationPinMessage) {
        meta.type = m.type;
        meta.serviceType = m.serviceType;
    }
    else if (m instanceof ImConversationMuteMessage) {
        meta.type = m.type;
        meta.serviceType = m.serviceType;
    }
    else if (m instanceof ImChatHistoryQueryRequest) {
        meta.type = m.type;
        meta.serviceType = m.serviceType;
    }
    else if (m instanceof ImCallSignal) {
        meta.type = m.type;
        meta.serviceType = m.serviceType;
    }
    return meta;
}
