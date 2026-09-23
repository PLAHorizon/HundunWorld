import { hilog } from '@kit.PerformanceAnalysisKit';
import { AppContext } from '../../model/AppContext';
import { ImSession } from './ImSession';
import { ImCallSignal, ImCallType, ImCallSignalType, ImCallEndReason } from './ImMessages';
import type { ImCallSignalAck } from './ImMessages';
import { CallMediaEngine } from './CallMediaEngine';
import { CallTonePlayer } from './CallTonePlayer';
import { util } from '@kit.ArkTS';

/**
 * 通话会话管理器（单例）：语音/视频通话状态机。
 * 信令走 IM 网关长连接（IMCallSignal，服务端仅转发+忙线判定，不落库）；
 * UI 通过 onStateChanged 回调驱动（CallOverlay 挂载于 Index/ChatDetailPage）。
 */

/** 通话 UI 状态 */
export class CallUiState {
    static readonly Idle: number = 0;
    /** 主叫拨出（等待对方接听） */
    static readonly Dialing: number = 1;
    /** 被叫来电（响铃） */
    static readonly Incoming: number = 2;
    /** 通话中 */
    static readonly Active: number = 3;
    /** 已结束（短暂展示结束原因后回 Idle） */
    static readonly Ended: number = 4;
}

/** 无应答超时（主叫拨出/被叫响铃共用） */
const RING_TIMEOUT_MS: number = 45000;
/** 通话计时步进 */
const DURATION_TICK_MS: number = 1000;
/** 通话保活间隔 */
const KEEPALIVE_INTERVAL_MS: number = 10000;
/** 结束态停留时长 */
const ENDED_LINGER_MS: number = 1800;

const TAG = 'GengDi.CallSession';
const DOMAIN = 0x0001;

@Observed
export class CallSessionManager {
    private static instance: CallSessionManager | null = null;
    static getInstance(): CallSessionManager {
        if (CallSessionManager.instance === null) {
            CallSessionManager.instance = new CallSessionManager();
        }
        return CallSessionManager.instance;
    }

    /** 当前 UI 状态（CallUiState） */
    state: number = CallUiState.Idle;
    /** 通话类型（ImCallType） */
    callType: number = ImCallType.Audio;
    /** 通话会话 ID（主叫生成，全通通话共享） */
    callId: string = '';
    /** 对端用户 */
    peerId: number = 0;
    peerName: string = '';
    peerAvatar: string = '';
    /** 是否主叫 */
    isCaller: boolean = false;
    /** 本端静音 */
    muted: boolean = false;
    /** 扬声器开启 */
    speakerOn: boolean = false;
    /** 本端摄像头关闭（视频通话） */
    cameraOff: boolean = false;
    /** 对端摄像头关闭（视频通话，MediaState 同步） */
    peerCameraOff: boolean = false;
    /** 通话时长（秒） */
    callSeconds: number = 0;
    /** 结束原因文案（Ended 态展示） */
    endText: string = '';

    /** 状态变更监听者（多个挂载点同时订阅来电浮层，页面销毁时必须 off） */
    private listeners: Set<() => void> = new Set<() => void>();

    /** 真实媒体引擎（UDP 直连：音频采集/播放、摄像头采集/视频帧收发） */
    private media: CallMediaEngine | null = null;
    /** 对端媒体端点（对端 MediaReady 信令携带，"ip:port"） */
    private peerMediaEndpoint: string = '';
    /** 视频帧消费者（CallOverlay 注册：本端/远端画面） */
    private videoSinkLocal: ((jpeg: Uint8Array) => void) | null = null;
    private videoSinkRemote: ((jpeg: Uint8Array) => void) | null = null;

    /** 通话提示音播放器（中国风五声琶音：主叫回铃 / 来电影铃） */
    private tone: CallTonePlayer = new CallTonePlayer();

    private ringTimer: number = -1;
    private durationTimer: number = -1;
    private keepAliveTimer: number = -1;
    private endedTimer: number = -1;

    private constructor() {
        // 挂接网关信令推送（单例常驻，随 IM 连接接收来电）
        ImSession.getInstance().onCallSignalReceived = (m: ImCallSignal) => {
            this.handleSignal(m);
        };
    }

    /** 是否有正在进行的通话（含拨出/来电） */
    get busy(): boolean {
        return this.state === CallUiState.Dialing || this.state === CallUiState.Incoming || this.state === CallUiState.Active;
    }

    /** 订阅状态变更 */
    on(listener: () => void): void {
        this.listeners.add(listener);
    }

    /** 取消订阅 */
    off(listener: () => void): void {
        this.listeners.delete(listener);
    }

    private notify(): void {
        this.listeners.forEach((l: () => void) => {
            l();
        });
    }

    /**
     * 注册视频帧消费者（CallOverlay 挂载/卸载时注册与注销）。
     * 传 null 注销。引擎帧到达时高频回调（约 10fps），回调内应自行节流 UI 刷新。
     */
    setVideoSink(local: ((jpeg: Uint8Array) => void) | null, remote: ((jpeg: Uint8Array) => void) | null): void {
        this.videoSinkLocal = local;
        this.videoSinkRemote = remote;
    }

    /**
     * 启动媒体引擎（UDP/麦克风/摄像头）。
     * 被叫在 accept 时、主叫在收到 Accept 时调用；对端端点若已知晓则直接配置，
     * 否则等对端 MediaReady 信令到达后经 setRemoteEndpoint 补充。
     */
    private async startMediaEngine(): Promise<void> {
        await this.stopMediaEngine();
        const engine = new CallMediaEngine();
        this.media = engine;
        engine.onLocalFrame = (jpeg: Uint8Array): void => {
            this.videoSinkLocal?.(jpeg);
        };
        engine.onRemoteFrame = (jpeg: Uint8Array): void => {
            this.videoSinkRemote?.(jpeg);
        };
        try {
            await engine.start(this.callId, this.callType === ImCallType.Video, this.peerMediaEndpoint);
        } catch (e) {
            hilog.error(DOMAIN, TAG, '媒体引擎启动失败: %{public}s', (e as Error).message);
        }
        // 启动耗时期间通话可能已结束（秒挂/对端取消）：立即回收
        if ((this.state === CallUiState.Ended || this.state === CallUiState.Idle) && this.media === engine) {
            this.media = null;
            await engine.stop();
        }
    }

    /** 停止媒体引擎并复位对端端点（幂等，通话结束时调用）。 */
    private async stopMediaEngine(): Promise<void> {
        const engine = this.media;
        this.media = null;
        this.peerMediaEndpoint = '';
        if (engine !== null) {
            // 超时兜底：真机相机 close 可能卡死，不能阻塞下一通的握手信令链
            await Promise.race([
                engine.stop(),
                new Promise<void>((resolve) => {
                    setTimeout((): void => {
                        resolve();
                    }, 3000);
                })
            ]);
        }
    }

    // ===== 通话提示音 =====

    /** 播放主叫回铃提示音（中国风五声琶音，等待对端应答期间循环）。 */
    private startRingbackTone(): void {
        this.tone.start(CallTonePlayer.KIND_RINGBACK);
    }

    /** 播放来电影铃提示音（中国风五声琶音，响铃期间循环）。 */
    private startIncomingTone(): void {
        this.tone.start(CallTonePlayer.KIND_INCOMING);
    }

    /** 停止提示音（幂等；接通/取消/拒接/忙线/超时/结束均调用）。 */
    private stopTone(): void {
        this.tone.stop();
    }

    private newCallId(): string {
        const uuid = util.generateRandomUUID(false);
        return `call-${uuid.replace(/-/g, '').slice(0, 16)}`;
    }

    /** 主叫发起通话 */
    startCall(peerId: number, peerName: string, peerAvatar: string, callType: number): void {
        if (this.busy) {
            hilog.warn(DOMAIN, TAG, '发起通话被忽略：通话进行中 state=%{public}d', this.state);
            return;
        }
        // 清理上一通电话的结束态复位定时器，防止新通话被旧回调强制打断
        this.stopEndedTimer();
        const app = AppContext.getInstance();
        this.callId = this.newCallId();
        this.callType = callType;
        this.peerId = peerId;
        this.peerName = peerName;
        this.peerAvatar = peerAvatar;
        this.isCaller = true;
        this.muted = false;
        this.speakerOn = callType === ImCallType.Video;
        this.cameraOff = false;
        this.peerCameraOff = false;
        this.callSeconds = 0;
        this.endText = '';
        this.state = CallUiState.Dialing;
        this.notify();
        // 主叫回铃提示音（等待对端接听期间；接通/取消/超时自动停止）
        this.startRingbackTone();
        this.startRingTimeout(true);
        this.sendSignal(ImCallSignalType.Offer, ImCallEndReason.Normal, '').catch((e: Error) => {
            hilog.error(DOMAIN, TAG, '发起通话失败: %{public}s', e.message);
            // 服务端拒绝（忙线残留等）时延长展示，避免 1.8s 闪退看不清原因
            this.finish(`呼叫失败：${e.message}`, ImCallEndReason.DeviceError, 3200);
        });
    }

    /** 被叫接听 */
    accept(): void {
        if (this.state !== CallUiState.Incoming) {
            return;
        }
        this.stopRingTimeout();
        // 串行链：启动媒体引擎（UDP/麦克风/摄像头，拿到真实端点）→ 发 Accept → 发 MediaReady（真实端点）。
        // Accept 的 ack 返回时服务端已完成向对端推送，此时再发 MediaReady 才能保证对端先收到
        // Accept（进入 Connecting）再收到 MediaReady（进入通话中）。
        this.startMediaEngine()
            .then(() => this.sendSignal(ImCallSignalType.Accept, ImCallEndReason.Normal, ''))
            .then(() => this.sendMediaReady())
            .catch((e: Error) => {
                hilog.error(DOMAIN, TAG, '接听/媒体协商失败: %{public}s', e.message);
            });
        this.enterActive();
    }

    /** 被叫拒接 */
    reject(): void {
        if (this.state !== CallUiState.Incoming) {
            return;
        }
        this.stopRingTimeout();
        this.sendFinalSignal(ImCallSignalType.Reject, ImCallEndReason.Rejected, '');
        this.finish('已拒绝', ImCallEndReason.Rejected);
    }

    /** 主叫取消呼叫（对方接听前） */
    cancel(): void {
        if (this.state !== CallUiState.Dialing) {
            return;
        }
        this.stopRingTimeout();
        this.sendFinalSignal(ImCallSignalType.Cancel, ImCallEndReason.Cancelled, '');
        this.finish('已取消', ImCallEndReason.Cancelled);
    }

    /** 通话中挂断 */
    hangup(): void {
        if (this.state === CallUiState.Dialing) {
            this.cancel();
            return;
        }
        if (this.state !== CallUiState.Active) {
            return;
        }
        this.sendFinalSignal(ImCallSignalType.Hangup, ImCallEndReason.Normal, '');
        this.finish('通话结束', ImCallEndReason.Normal);
    }

    /** 切换静音（本端停止发送音频 + MediaState 同步对端） */
    toggleMute(): void {
        this.muted = !this.muted;
        this.media?.setMuted(this.muted);
        this.notify();
        if (this.state === CallUiState.Active) {
            this.sendSignal(ImCallSignalType.MediaState, ImCallEndReason.Normal, '').catch(() => {
            });
        }
    }

    /** 切换免提 */
    toggleSpeaker(): void {
        this.speakerOn = !this.speakerOn;
        this.notify();
    }

    /** 切换摄像头（本端停止发送视频帧 + MediaState 同步对端） */
    toggleCamera(): void {
        if (this.callType !== ImCallType.Video) {
            return;
        }
        this.cameraOff = !this.cameraOff;
        this.media?.setCameraOff(this.cameraOff);
        this.notify();
        if (this.state === CallUiState.Active) {
            this.sendSignal(ImCallSignalType.MediaState, ImCallEndReason.Normal, '').catch(() => {
            });
        }
    }

    private enterActive(): void {
        this.stopTone();
        this.state = CallUiState.Active;
        this.callSeconds = 0;
        this.notify();
        // 计时
        this.durationTimer = setInterval(() => {
            this.callSeconds += 1;
            this.notify();
        }, DURATION_TICK_MS);
        // 保活（服务端忙线状态维护）
        this.keepAliveTimer = setInterval(() => {
            this.sendSignal(ImCallSignalType.KeepAlive, ImCallEndReason.Normal, '').catch(() => {
            });
        }, KEEPALIVE_INTERVAL_MS);
    }

    private finish(endText: string, _reason: number, lingerMs: number = ENDED_LINGER_MS): void {
        this.stopAllTimers();
        this.stopTone();
        this.stopMediaEngine().catch(() => {
        });
        const wasActive = this.state === CallUiState.Active;
        this.endText = endText;
        this.state = CallUiState.Ended;
        this.notify();
        // 通话记录消息：挂断方发送（双方均可见），仅接通后的通话记录时长
        if (wasActive && this.peerId > 0) {
            const typeText = this.callType === ImCallType.Video ? '视频通话' : '语音通话';
            const record = `📞 ${typeText} ${this.formatDuration(this.callSeconds)}`;
            ImSession.getInstance().sendPrivateChatAsync(BigInt(this.peerId), record, `cr-${Date.now()}-${Math.floor(Math.random() * 10000)}`).catch((e: Error) => {
                hilog.warn(DOMAIN, TAG, '通话记录消息发送失败: %{public}s', e.message);
            });
        }
        // 短暂展示结束态后复位（仅当仍处于结束态；期间若进入新通话/新来电则不得打断）
        this.endedTimer = setTimeout(() => {
            this.endedTimer = -1;
            if (this.state !== CallUiState.Ended) {
                return;
            }
            this.state = CallUiState.Idle;
            this.endText = '';
            this.notify();
        }, lingerMs);
    }

    /** 网关信令推送处理 */
    private handleSignal(m: ImCallSignal): void {
        const selfId = AppContext.getInstance().user.userId;
        // 自己多端同步的信令忽略（信令接收方是本端才处理）
        if (Number(m.receiverId) !== selfId) {
            return;
        }
        switch (m.signalType) {
            case ImCallSignalType.Offer:
                if (this.busy) {
                    // 已有通话：忙线
                    const busy = new ImCallSignal();
                    this.fillSignal(busy, ImCallSignalType.Busy, ImCallEndReason.Busy, '对方正在通话中');
                    busy.callId = m.callId;
                    busy.receiverId = m.senderId;
                    this.sendSignalInstance(busy).catch(() => {
                    });
                    return;
                }
                // 清理上一通电话的结束态复位定时器，防止来电被旧回调强制打断
                this.stopEndedTimer();
                this.callId = m.callId;
                this.callType = m.callType;
                this.peerId = Number(m.senderId);
                this.peerName = m.senderName.length > 0 ? m.senderName : `用户 ${m.senderId}`;
                this.peerAvatar = m.senderAvatar;
                this.isCaller = false;
                this.muted = false;
                this.speakerOn = m.callType === ImCallType.Video;
                this.cameraOff = false;
                this.peerCameraOff = false;
                this.callSeconds = 0;
                this.endText = '';
                this.state = CallUiState.Incoming;
                this.notify();
                // 来电影铃提示音（响铃期间循环；接听/拒接/取消/超时自动停止）
                this.startIncomingTone();
                this.startRingTimeout(false);
                break;
            case ImCallSignalType.Accept:
                if (this.state === CallUiState.Dialing && m.callId === this.callId) {
                    this.stopRingTimeout();
                    // 主叫收到 Accept：启动媒体引擎（UDP/麦克风/摄像头，真实端点）后发 MediaReady 完成握手
                    this.startMediaEngine()
                        .then(() => this.sendMediaReady())
                        .catch((e: Error) => {
                            hilog.error(DOMAIN, TAG, '媒体通道协商失败: %{public}s', e.message);
                        });
                    this.enterActive();
                }
                break;
            case ImCallSignalType.Reject:
                if (this.state === CallUiState.Dialing && m.callId === this.callId) {
                    this.stopRingTimeout();
                    this.finish('对方已拒绝', ImCallEndReason.Rejected);
                }
                break;
            case ImCallSignalType.Busy:
                if (this.state === CallUiState.Dialing && m.callId === this.callId) {
                    this.stopRingTimeout();
                    this.finish('对方忙线中', ImCallEndReason.Busy);
                }
                break;
            case ImCallSignalType.Cancel:
                if (this.state === CallUiState.Incoming && m.callId === this.callId) {
                    this.stopRingTimeout();
                    this.finish('对方已取消', ImCallEndReason.Cancelled);
                }
                break;
            case ImCallSignalType.Hangup:
                if (this.state === CallUiState.Active && m.callId === this.callId) {
                    this.finish('通话结束', ImCallEndReason.Normal);
                }
                break;
            case ImCallSignalType.Timeout:
                if ((this.state === CallUiState.Incoming || this.state === CallUiState.Dialing) && m.callId === this.callId) {
                    this.stopRingTimeout();
                    this.finish('无人接听', ImCallEndReason.Timeout);
                }
                break;
            case ImCallSignalType.MediaState:
                if (this.state === CallUiState.Active && m.callId === this.callId) {
                    this.peerCameraOff = m.isCameraOff;
                    this.notify();
                }
                break;
            case ImCallSignalType.MediaReady:
                // 对端媒体端点到达：配置引擎（对端 IP 可能获取失败/被 NAT 改写，
                // 引擎收发路径上还有 C# 端源地址学习兜底）
                if (m.callId === this.callId && m.mediaEndpoint.length > 0) {
                    this.peerMediaEndpoint = m.mediaEndpoint;
                    this.media?.setRemoteEndpoint(m.mediaEndpoint);
                }
                break;
            case ImCallSignalType.KeepAlive:
            default:
                // 保活：无 UI 变化
                break;
        }
    }

    private startRingTimeout(isCallerSide: boolean): void {
        this.stopRingTimeout();
        this.ringTimer = setTimeout(() => {
            this.ringTimer = -1;
            // 超时释放：通知对端 Timeout 后本地结束
            this.sendFinalSignal(ImCallSignalType.Timeout, ImCallEndReason.Timeout, '');
            this.finish(isCallerSide ? '无人接听' : '已超时', ImCallEndReason.Timeout);
        }, RING_TIMEOUT_MS);
    }

    private stopRingTimeout(): void {
        if (this.ringTimer >= 0) {
            clearTimeout(this.ringTimer);
            this.ringTimer = -1;
        }
    }

    /** 清理结束态复位定时器（新通话/新来电进入前必须调用，防止幽灵回调打断） */
    private stopEndedTimer(): void {
        if (this.endedTimer >= 0) {
            clearTimeout(this.endedTimer);
            this.endedTimer = -1;
        }
    }

    private stopAllTimers(): void {
        this.stopRingTimeout();
        this.stopEndedTimer();
        if (this.durationTimer >= 0) {
            clearInterval(this.durationTimer);
            this.durationTimer = -1;
        }
        if (this.keepAliveTimer >= 0) {
            clearInterval(this.keepAliveTimer);
            this.keepAliveTimer = -1;
        }
    }

    /** 构造并以本端身份发送信令（接收方=当前对端） */
    private sendSignal(signalType: number, endReason: number, remark: string): Promise<void> {
        const m = new ImCallSignal();
        this.fillSignal(m, signalType, endReason, remark);
        return this.sendSignalInstance(m);
    }

    /**
     * 发送终结性信令（Cancel/Reject/Hangup/Timeout）并保证送达：
     * 失败延迟重发一次。服务端对重复终结信令幂等（状态已清理时再次清理无副作用），
     * 若丢失则对端 Grain 忙线状态最长残留 90 秒，期间本端重拨会被"对方正在通话中"拒绝。
     */
    private sendFinalSignal(signalType: number, endReason: number, remark: string): void {
        // 立即快照信令内容（callId/对端等），防止重发时已进入新通话导致字段错乱
        const m = new ImCallSignal();
        this.fillSignal(m, signalType, endReason, remark);
        this.sendSignalInstance(m).catch((e: Error) => {
            hilog.warn(DOMAIN, TAG, '终结信令发送失败（%{public}d），1.5s 后重发: %{public}s', signalType, e.message);
            setTimeout(() => {
                this.sendSignalInstance(m).catch((e2: Error) => {
                    hilog.error(DOMAIN, TAG, '终结信令重发仍失败: %{public}s', e2.message);
                });
            }, 1500);
        });
    }

    private sendSignalInstance(m: ImCallSignal): Promise<void> {
        return ImSession.getInstance().sendCallSignalAsync(m).then((ack: ImCallSignalAck) => {
            if (!ack.accepted) {
                throw new Error(ack.message.length > 0 ? ack.message : '服务端未受理信令');
            }
        });
    }

    /**
     * 向对端发送 MediaReady 信令，完成媒体端点握手。
     * C# 端只有在收到 MediaReady 且 MediaEndpoint 可解析时才会进入通话中（InCall），
     * 缺少该信令会导致对端一直停留在"正在接通"而无法打通。
     * 端点取媒体引擎真实 UDP 端点（本机 IP + 绑定端口）；
     * 引擎不可用（UDP 绑定失败等）时退化为占位端点，对端将经源地址学习兜底纠正。
     */
    private sendMediaReady(): Promise<void> {
        const m = new ImCallSignal();
        this.fillSignal(m, ImCallSignalType.MediaReady, ImCallEndReason.Normal, '');
        // 优先引擎真实 UDP 端点；UDP 绑定失败时退化为占位端点（对端经源地址学习纠正）
        const realEndpoint = this.media !== null ? this.media.localEndpoint : '';
        m.mediaEndpoint = realEndpoint.length > 0
            ? realEndpoint
            : `127.0.0.1:${20000 + (Date.now() % 40000)}`;
        return this.sendSignalInstance(m);
    }

    private fillSignal(m: ImCallSignal, signalType: number, endReason: number, remark: string): void {
        const app = AppContext.getInstance();
        m.callId = this.callId;
        m.senderId = BigInt(app.user.userId);
        m.senderName = app.user.nickname;
        m.senderAvatar = app.user.avatar;
        m.receiverId = BigInt(this.peerId);
        m.signalType = signalType;
        m.callType = this.callType;
        m.isMuted = this.muted;
        m.isCameraOff = this.cameraOff;
        m.endReason = endReason;
        m.remark = remark;
        m.timestamp = Date.now();
    }

    /** 秒 → mm:ss */
    formatDuration(seconds: number): string {
        const mm = Math.floor(seconds / 60).toString().padStart(2, '0');
        const ss = Math.floor(seconds % 60).toString().padStart(2, '0');
        return `${mm}:${ss}`;
    }

    /** 通话类型文案 */
    callTypeText(): string {
        return this.callType === ImCallType.Video ? '视频通话' : '语音通话';
    }
}
