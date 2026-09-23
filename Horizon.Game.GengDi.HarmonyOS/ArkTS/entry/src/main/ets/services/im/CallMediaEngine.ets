import { socket } from '@kit.NetworkKit';
import { wifiManager } from '@kit.ConnectivityKit';
import { audio } from '@kit.AudioKit';
import { camera } from '@kit.CameraKit';
import { image } from '@kit.ImageKit';
import { BusinessError } from '@kit.BasicServicesKit';
import { hilog } from '@kit.PerformanceAnalysisKit';
import { AppContext } from '../../model/AppContext';

const TAG = 'GengDi.CallMedia';
const DOMAIN = 0x0001;

/**
 * 通话媒体引擎（与 PC 端 CallMediaTransport/AudioCallEngine/VideoCallEngine 协议逐字节兼容）。
 *
 * UDP 数据包格式（小端）：
 *   [0..4)  魔数 0x4844434C ("HDCL")
 *   [4..12) 通话会话ID的64位哈希（FNV-1a，丢弃其他会话串扰包）
 *   [12]    类型：1=音频帧，2=视频分片
 *   音频帧：[13..)=PCM16 16kHz 单声道
 *   视频分片：[13..17)帧ID，[17..19)分片索引，[19..21)分片总数，[21..)=分片数据
 *
 * 音频：AudioCapturer(16k/mono/S16LE) → UDP；UDP → AudioRenderer 播放（通话模式带 AEC）。
 * 视频：相机拍照流（photoOutput 周期触发，预览流是 YUV 无 JPEG 分量）→
 *       ImageReceiver JPEG 帧 → 分片发送；收包重组为完整 JPEG 帧。
 * 注意：麦克风/相机均为后台激活，绝不阻塞 start() 返回——握手信令（Accept/MediaReady）
 * 只等 UDP 绑定（毫秒级），相机开启慢/卡死不影响接通。
 * 静音=停止发送音频（采集管线保持运行，取消静音立即恢复）；
 * 关摄像头=停止发送视频帧（相机管线保持运行，恢复立即出画）。
 */
export class CallMediaEngine {
    /** 包头固定 13 字节 */
    private static readonly HEADER_SIZE = 13;
    private static readonly MAGIC = 0x4844434C;
    private static readonly KIND_AUDIO = 1;
    private static readonly KIND_VIDEO_FRAGMENT = 2;
    private static readonly MAX_DATAGRAM_SIZE = 1200;
    private static readonly VIDEO_FRAG_HEADER_SIZE = 21;
    private static readonly VIDEO_FRAG_PAYLOAD = CallMediaEngine.MAX_DATAGRAM_SIZE - CallMediaEngine.VIDEO_FRAG_HEADER_SIZE;
    /** 视频发送节流间隔（毫秒）：拍照流约 6-7fps，兼顾流畅与流量 */
    private static readonly VIDEO_FRAME_INTERVAL_MS = 150;
    /** 绑定端口尝试起点（UDPSocket 不暴露已绑定端口，采用固定区间自选） */
    private static readonly LOCAL_PORT_BASE = 21000;
    private static readonly LOCAL_PORT_MAX = 21100;
    /** 未完成视频帧的超时清理 */
    private static readonly PENDING_FRAME_TIMEOUT_MS = 3000;
    private static readonly MAX_PENDING_FRAMES = 8;

    private udp: socket.UDPSocket | null = null;
    private localPort: number = 0;
    private localIp: string = '';
    private callIdHash = 0n;
    private remoteHost: string = '';
    private remotePort: number = 0;

    private capturer: audio.AudioCapturer | null = null;
    private renderer: audio.AudioRenderer | null = null;
    private captureRunning = false;

    private cameraSession: camera.CaptureSession | null = null;
    private cameraInput: camera.CameraInput | null = null;
    private photoOutput: camera.PhotoOutput | null = null;
    private imageReceiver: image.ImageReceiver | null = null;
    private nextFrameId: number = 0;
    private captureTimerId: number = -1;
    private captureBusy: boolean = false;

    private running = false;
    private muted = false;
    private cameraOff = false;

    /** 本端摄像头帧（JPEG）回调——供 UI 本地预览。 */
    onLocalFrame?: (jpeg: Uint8Array) => void;
    /** 对端完整视频帧（JPEG）回调——供 UI 远端画面。 */
    onRemoteFrame?: (jpeg: Uint8Array) => void;

    private pendingFrames = new Map<number, PendingFrame>();

    /** 本地媒体端点（"ip:port"，供 MediaReady 信令携带）；UDP 绑定失败时返回空串。 */
    get localEndpoint(): string {
        return this.localPort > 0 ? `${this.localIp}:${this.localPort}` : '';
    }

    /**
     * 启动媒体引擎。
     * 仅 UDP 绑定参与等待（毫秒级），返回时 localEndpoint 即可用于 MediaReady 握手；
     * 麦克风/相机在后台激活——真机相机开启可达数秒甚至卡死，
     * 若阻塞本方法会导致 Accept 信令发不出、对端振铃超时（视频打不通的根因）。
     * @param callId  通话会话ID（用于包过滤哈希）
     * @param isVideo 是否视频通话（视频通话才启动相机）
     * @param remoteEndpoint 对端媒体端点 "ip:port"（可能为空，稍后经 setRemoteEndpoint 配置）
     */
    async start(callId: string, isVideo: boolean, remoteEndpoint: string): Promise<void> {
        this.callIdHash = CallMediaEngine.computeCallIdHash(callId);
        this.running = true;

        await this.bindUdp();
        this.localIp = CallMediaEngine.resolveLocalIp();
        if (remoteEndpoint.length > 0) {
            this.setRemoteEndpoint(remoteEndpoint);
        }

        // 媒体设备后台激活（内部全量异常捕获，慢/失败均不影响已返回的握手）
        this.activateAudio().catch((): void => {
        });
        if (isVideo) {
            this.activateCamera().catch((): void => {
            });
        }

        hilog.info(DOMAIN, TAG, '媒体引擎启动: local=%{public}s remote=%{public}s',
            this.localEndpoint, `${this.remoteHost}:${this.remotePort}`);
    }

    /** 配置对端媒体端点（收到对端 MediaReady 信令时调用）。 */
    setRemoteEndpoint(endpoint: string): void {
        const idx = endpoint.lastIndexOf(':');
        if (idx <= 0 || idx === endpoint.length - 1) {
            return;
        }
        const host = endpoint.substring(0, idx);
        const port = Number(endpoint.substring(idx + 1));
        if (host.length === 0 || Number.isNaN(port) || port <= 0) {
            return;
        }
        this.remoteHost = host;
        this.remotePort = port;
    }

    /** 静音开关：true=停止发送音频（对端无声），取消后立即恢复。 */
    setMuted(muted: boolean): void {
        this.muted = muted;
    }

    /** 摄像头开关：true=停止发送视频帧，取消后立即恢复出画。 */
    setCameraOff(off: boolean): void {
        this.cameraOff = off;
    }

    /** 停止并释放全部媒体资源（幂等）。 */
    async stop(): Promise<void> {
        if (!this.running && this.udp === null) {
            return;
        }
        this.running = false;
        this.captureRunning = false;
        this.pendingFrames.clear();

        await this.stopCamera();
        await this.stopAudio();

        const udp = this.udp;
        this.udp = null;
        if (udp !== null) {
            try {
                udp.off('message');
                await udp.close();
            } catch (e) {
                // 关闭异常忽略（连接可能已断）
            }
        }
        hilog.info(DOMAIN, TAG, '媒体引擎已停止');
    }

    // ==================== UDP 传输 ====================

    private async bindUdp(): Promise<void> {
        const start = CallMediaEngine.LOCAL_PORT_BASE +
            Math.floor(Math.random() * (CallMediaEngine.LOCAL_PORT_MAX - CallMediaEngine.LOCAL_PORT_BASE));
        let boundPort = 0;
        for (let attempt = 0; attempt < 8; attempt++) {
            const port = start + attempt <= CallMediaEngine.LOCAL_PORT_MAX
                ? start + attempt
                : CallMediaEngine.LOCAL_PORT_BASE + attempt;
            const udp = socket.constructUDPSocketInstance();
            const addr: socket.NetAddress = { address: '0.0.0.0', port: port };
            try {
                await udp.bind(addr);
                this.udp = udp;
                boundPort = port;
                break;
            } catch (e) {
                try {
                    await udp.close();
                } catch {
                    // 忽略
                }
            }
        }
        if (boundPort === 0) {
            hilog.error(DOMAIN, TAG, 'UDP 端口绑定失败（%{public}d-%{public}d 区间）',
                CallMediaEngine.LOCAL_PORT_BASE, CallMediaEngine.LOCAL_PORT_MAX);
            return;
        }
        this.localPort = boundPort;
        this.udp!.on('message', (info: socket.SocketMessageInfo) => this.handleDatagram(info));
    }

    private sendPacket(packet: Uint8Array): void {
        const udp = this.udp;
        if (udp === null || this.remotePort === 0) {
            return;
        }
        const dest: socket.NetAddress = { address: this.remoteHost, port: this.remotePort };
        const options: socket.UDPSendOptions = {
            data: packet.buffer.slice(packet.byteOffset, packet.byteOffset + packet.byteLength),
            address: dest
        };
        udp.send(options).catch(() => {
            // 发送失败多为瞬时网络抖动，静默丢弃（UDP 本就不保证送达）
        });
    }

    private handleDatagram(info: socket.SocketMessageInfo): void {
        if (!this.running || info.message.byteLength < CallMediaEngine.HEADER_SIZE) {
            return;
        }
        const data = new Uint8Array(info.message);
        if (CallMediaEngine.readU32(data, 0) !== CallMediaEngine.MAGIC
            || CallMediaEngine.readU64(data, 4) !== this.callIdHash) {
            return;
        }

        const kind = data[12];
        if (kind === CallMediaEngine.KIND_AUDIO) {
            const pcm = data.slice(CallMediaEngine.HEADER_SIZE);
            this.playRemoteAudio(pcm);
        } else if (kind === CallMediaEngine.KIND_VIDEO_FRAGMENT
            && data.length > CallMediaEngine.VIDEO_FRAG_HEADER_SIZE) {
            this.handleVideoFragment(data);
        }
    }

    // ==================== 音频 ====================

    /** 后台激活音频管线（播放+采集），全程异常自吞。 */
    private async activateAudio(): Promise<void> {
        try {
            // 播放：16kHz 单声道 PCM16
            const renderOpts: audio.AudioRendererOptions = {
                streamInfo: {
                    samplingRate: audio.AudioSamplingRate.SAMPLE_RATE_16000,
                    channels: audio.AudioChannel.CHANNEL_1,
                    sampleFormat: audio.AudioSampleFormat.SAMPLE_FORMAT_S16LE,
                    encodingType: audio.AudioEncodingType.ENCODING_TYPE_RAW
                },
                rendererInfo: {
                    content: audio.ContentType.CONTENT_TYPE_SPEECH,
                    usage: audio.StreamUsage.STREAM_USAGE_VOICE_COMMUNICATION,
                    rendererFlags: 0
                }
            };
            this.renderer = await audio.createAudioRenderer(renderOpts);
            await this.renderer.start();
        } catch (e) {
            hilog.error(DOMAIN, TAG, '扬声器初始化失败: %{public}s', (e as BusinessError).message);
        }

        try {
            // 采集：16kHz 单声道 PCM16（与 PC 端协议一致）。
            // 采用 readdata 回调驱动（系统就绪即回调，无轮询无阻塞），
            // 静音时丢弃数据（采集管线保持运行，取消静音立即恢复）。
            const capturerOpts: audio.AudioCapturerOptions = {
                streamInfo: {
                    samplingRate: audio.AudioSamplingRate.SAMPLE_RATE_16000,
                    channels: audio.AudioChannel.CHANNEL_1,
                    sampleFormat: audio.AudioSampleFormat.SAMPLE_FORMAT_S16LE,
                    encodingType: audio.AudioEncodingType.ENCODING_TYPE_RAW
                },
                capturerInfo: {
                    source: audio.SourceType.SOURCE_TYPE_VOICE_COMMUNICATION,
                    capturerFlags: 0
                }
            };
            const capturer = await audio.createAudioCapturer(capturerOpts);
            this.capturer = capturer;
            this.captureRunning = true;
            capturer.on('readData', (buffer: ArrayBuffer) => {
                if (!this.captureRunning || this.muted || buffer.byteLength === 0) {
                    return;
                }
                this.sendAudioPacket(new Uint8Array(buffer));
            });
            await capturer.start();
        } catch (e) {
            this.captureRunning = false;
            this.capturer = null;
            hilog.error(DOMAIN, TAG, '麦克风初始化失败: %{public}s', (e as BusinessError).message);
        }
    }

    private sendAudioPacket(pcm: Uint8Array): void {
        const packet = new Uint8Array(CallMediaEngine.HEADER_SIZE + pcm.byteLength);
        CallMediaEngine.writeU32(packet, 0, CallMediaEngine.MAGIC);
        CallMediaEngine.writeU64(packet, 4, this.callIdHash);
        packet[12] = CallMediaEngine.KIND_AUDIO;
        packet.set(pcm, CallMediaEngine.HEADER_SIZE);
        this.sendPacket(packet);
    }

    private playRemoteAudio(pcm: Uint8Array): void {
        const renderer = this.renderer;
        if (renderer === null) {
            return;
        }
        const copy = new ArrayBuffer(pcm.byteLength);
        new Uint8Array(copy).set(pcm);
        renderer.write(copy).catch(() => {
            // 播放缓冲满等瞬时异常：丢弃该段（通话容忍）
        });
    }

    private async stopAudio(): Promise<void> {
        const capturer = this.capturer;
        this.capturer = null;
        if (capturer !== null) {
            try {
                await capturer.stop();
            } catch {
                // 忽略
            }
            try {
                capturer.release();
            } catch {
                // 忽略
            }
        }
        const renderer = this.renderer;
        this.renderer = null;
        if (renderer !== null) {
            try {
                await renderer.stop();
            } catch {
                // 忽略
            }
            try {
                renderer.release();
            } catch {
                // 忽略
            }
        }
    }

    // ==================== 视频（拍照流采集 JPEG） ====================

    /**
     * 后台激活相机（拍照流），全程异常自吞。
     * 使用 photoOutput 周期触发拍照获取 JPEG 帧——预览流是 YUV 格式，
     * ImageReceiver 取不到 JPEG 分量，无法用于分片发送。
     */
    private async activateCamera(): Promise<void> {
        try {
            const context = AppContext.getInstance().context;
            if (context === null) {
                throw new Error('UIAbility 上下文未初始化');
            }
            const manager = camera.getCameraManager(context);
            const devices = manager.getSupportedCameras();
            if (devices.length === 0) {
                hilog.warn(DOMAIN, TAG, '无可用摄像头，视频发送降级为占位');
                return;
            }
            // 优先前置摄像头（自拍通话场景）
            const device = devices.find(d => d.cameraPosition === camera.CameraPosition.CAMERA_POSITION_FRONT) ?? devices[0];

            // 拍照档位：优先 640x480（帧小、分片少），否则取面积最小档位（控制 UDP 分片洪泛）
            const photoProfiles = manager.getSupportedOutputCapability(device).photoProfiles;
            if (photoProfiles === undefined || photoProfiles.length === 0) {
                throw new Error('相机无可用拍照档位');
            }
            let profile: camera.Profile | undefined = undefined;
            for (const p of photoProfiles) {
                if (p.size.width === 640 && p.size.height === 480) {
                    profile = p;
                    break;
                }
                if (profile === undefined
                    || p.size.width * p.size.height < profile.size.width * profile.size.height) {
                    profile = p;
                }
            }
            if (profile === undefined) {
                throw new Error('相机拍照档位选择失败');
            }

            // ImageReceiver 以 JPEG 流接收拍照帧（免二次编码，直接分片发送）
            const receiverSize: image.Size = { width: profile.size.width, height: profile.size.height };
            const receiver = image.createImageReceiver(receiverSize, image.ImageFormat.JPEG, 8);
            this.imageReceiver = receiver;
            const surfaceId = await receiver.getReceivingSurfaceId();
            if (!this.running) {
                return; // 等待期间通话已结束，stop() 已释放 receiver
            }

            const cameraInput = manager.createCameraInput(device);
            await cameraInput.open();
            if (!this.running) {
                try {
                    await cameraInput.close();
                } catch {
                    // 忽略
                }
                return;
            }
            this.cameraInput = cameraInput;

            // 先赋值再 await：等待期间 stop() 也能经 stopCamera 释放已构造资源
            const session = manager.createCaptureSession();
            this.cameraSession = session;
            const photoOutput = manager.createPhotoOutput(profile, surfaceId);
            this.photoOutput = photoOutput;

            await session.beginConfig();
            session.addInput(cameraInput);
            session.addOutput(photoOutput);
            await session.commitConfig();
            if (!this.running) {
                return;
            }
            await session.start();

            receiver.on('imageArrival', () => {
                this.readNextFrame();
            });

            // 周期触发拍照（busy 防重入：上一张未完成时跳过本次节拍）
            this.captureTimerId = setInterval((): void => {
                this.triggerCapture();
            }, CallMediaEngine.VIDEO_FRAME_INTERVAL_MS);
        } catch (e) {
            hilog.error(DOMAIN, TAG, '相机初始化失败（视频发送降级为占位）: %{public}s',
                (e as BusinessError).message ?? (e as Error).message);
        }
    }

    /** 触发一次拍照（photoOutput 出 JPEG 帧到 ImageReceiver）。 */
    private triggerCapture(): void {
        const output = this.photoOutput;
        if (output === null || !this.running || this.captureBusy) {
            return;
        }
        this.captureBusy = true;
        output.capture()
            .then((): void => {
                this.captureBusy = false;
            })
            .catch((): void => {
                this.captureBusy = false;
            });
    }

    /** 从 ImageReceiver 读取最新 JPEG 帧。 */
    private readNextFrame(): void {
        try {
            this.imageReceiver?.readLatestImage((err: BusinessError, img: image.Image) => {
                if (err.code !== 0 || img === undefined || img === null) {
                    return;
                }
                this.handleCameraImage(img);
            });
        } catch (e) {
            hilog.warn(DOMAIN, TAG, '读帧异常: %{public}s', (e as BusinessError).message);
        }
    }

    private async handleCameraImage(img: image.Image): Promise<void> {
        try {
            if (!this.running) {
                return;
            }
            const component = await img.getComponent(image.ComponentType.JPEG);
            if (component === undefined || component === null) {
                return;
            }
            const jpeg = CallMediaEngine.trimJpeg(new Uint8Array(component.byteBuffer));
            if (jpeg.byteLength > 0) {
                this.onLocalFrame?.(jpeg);
                if (!this.cameraOff) {
                    this.sendVideoFrame(jpeg);
                }
            }
        } catch (e) {
            hilog.warn(DOMAIN, TAG, '相机帧处理异常: %{public}s', (e as BusinessError).message);
        } finally {
            try {
                img.release();
            } catch {
                // 忽略
            }
        }
    }

    /** JPEG 流缓冲区可能大于实际帧长：从尾部向前找 EOI(FFD9) 截断。 */
    private static trimJpeg(buf: Uint8Array): Uint8Array {
        if (buf.byteLength < 4) {
            return new Uint8Array(0);
        }
        for (let i = buf.byteLength - 2; i >= 0; i--) {
            if (buf[i] === 0xFF && buf[i + 1] === 0xD9) {
                return buf.slice(0, i + 2);
            }
        }
        return new Uint8Array(0);
    }

    private sendVideoFrame(jpeg: Uint8Array): void {
        const frameId = this.nextFrameId++ >>> 0;
        const fragmentCount = Math.ceil(jpeg.byteLength / CallMediaEngine.VIDEO_FRAG_PAYLOAD);
        if (fragmentCount > 65535) {
            return;
        }
        for (let index = 0; index < fragmentCount; index++) {
            const offset = index * CallMediaEngine.VIDEO_FRAG_PAYLOAD;
            const length = Math.min(CallMediaEngine.VIDEO_FRAG_PAYLOAD, jpeg.byteLength - offset);
            const packet = new Uint8Array(CallMediaEngine.VIDEO_FRAG_HEADER_SIZE + length);
            CallMediaEngine.writeU32(packet, 0, CallMediaEngine.MAGIC);
            CallMediaEngine.writeU64(packet, 4, this.callIdHash);
            packet[12] = CallMediaEngine.KIND_VIDEO_FRAGMENT;
            CallMediaEngine.writeU32(packet, 13, frameId);
            CallMediaEngine.writeU16(packet, 17, index);
            CallMediaEngine.writeU16(packet, 19, fragmentCount);
            packet.set(jpeg.subarray(offset, offset + length), CallMediaEngine.VIDEO_FRAG_HEADER_SIZE);
            this.sendPacket(packet);
        }
    }

    /** 视频分片重组：与 PC 端逻辑一致（超时清理 + 完整帧回调）。 */
    private handleVideoFragment(data: Uint8Array): void {
        const frameId = CallMediaEngine.readU32(data, 13);
        const fragIndex = CallMediaEngine.readU16(data, 17);
        const fragCount = CallMediaEngine.readU16(data, 19);
        if (fragCount === 0 || fragIndex >= fragCount) {
            return;
        }

        const now = Date.now();
        // 清理超时未完成帧
        this.pendingFrames.forEach((frame: PendingFrame, key: number): void => {
            if (now - frame.lastActivityMs > CallMediaEngine.PENDING_FRAME_TIMEOUT_MS) {
                this.pendingFrames.delete(key);
            }
        });

        let frame = this.pendingFrames.get(frameId);
        if (frame === undefined) {
            if (this.pendingFrames.size >= CallMediaEngine.MAX_PENDING_FRAMES) {
                return;
            }
            frame = new PendingFrame(fragCount, now);
            this.pendingFrames.set(frameId, frame);
        }
        if (frame.fragments.length !== fragCount || frame.received >= fragCount
            || frame.fragments[fragIndex] !== null) {
            return;
        }

        frame.lastActivityMs = now;
        frame.fragments[fragIndex] = data.slice(CallMediaEngine.VIDEO_FRAG_HEADER_SIZE);
        frame.received++;

        if (frame.received < fragCount) {
            return;
        }
        this.pendingFrames.delete(frameId);

        let total = 0;
        for (const frag of frame.fragments) {
            if (frag === null) {
                return;
            }
            total += frag.byteLength;
        }
        const jpeg = new Uint8Array(total);
        let offset = 0;
        for (const frag of frame.fragments) {
            jpeg.set(frag as Uint8Array, offset);
            offset += (frag as Uint8Array).byteLength;
        }
        this.onRemoteFrame?.(jpeg);
    }

    private async stopCamera(): Promise<void> {
        const receiver = this.imageReceiver;
        this.imageReceiver = null;
        if (receiver !== null) {
            try {
                receiver.release();
            } catch {
                // 忽略
            }
        }
        const session = this.cameraSession;
        this.cameraSession = null;
        if (session !== null) {
            try {
                await session.stop();
            } catch {
                // 忽略
            }
            try {
                await session.release();
            } catch {
                // 忽略
            }
        }
        if (this.captureTimerId >= 0) {
            clearInterval(this.captureTimerId);
            this.captureTimerId = -1;
        }
        this.captureBusy = false;
        const photoOutput = this.photoOutput;
        this.photoOutput = null;
        if (photoOutput !== null) {
            try {
                await photoOutput.release();
            } catch {
                // 忽略
            }
        }
        const cameraInput = this.cameraInput;
        this.cameraInput = null;
        if (cameraInput !== null) {
            try {
                await cameraInput.close();
            } catch {
                // 忽略
            }
        }
    }

    // ==================== 本机地址解析 ====================

    /**
     * 解析本机用于媒体直连的 IPv4 地址（WiFi 场景）。
     * 获取失败返回占位地址：PC 端收到本端媒体包后会通过源地址学习自动纠正。
     */
    private static resolveLocalIp(): string {
        try {
            const info = wifiManager.getIpInfo();
            const ipNum = info.ipAddress;
            if (ipNum > 0) {
                // ipAddress 为数字形式（高字节为第一段），转点分十进制
                return `${(ipNum >>> 24) & 0xFF}.${(ipNum >>> 16) & 0xFF}.${(ipNum >>> 8) & 0xFF}.${ipNum & 0xFF}`;
            }
        } catch (e) {
            hilog.warn(DOMAIN, TAG, '获取 WiFi 地址失败（蜂窝/NAT 场景，依赖对端地址学习）: %{public}s',
                (e as BusinessError).message);
        }
        return '127.0.0.1';
    }

    // ==================== 二进制读写（小端） ====================

    private static writeU32(buf: Uint8Array, offset: number, value: number): void {
        buf[offset] = value & 0xFF;
        buf[offset + 1] = (value >>> 8) & 0xFF;
        buf[offset + 2] = (value >>> 16) & 0xFF;
        buf[offset + 3] = (value >>> 24) & 0xFF;
    }

    private static writeU16(buf: Uint8Array, offset: number, value: number): void {
        buf[offset] = value & 0xFF;
        buf[offset + 1] = (value >>> 8) & 0xFF;
    }

    private static writeU64(buf: Uint8Array, offset: number, value: bigint): void {
        const mask = 0xFFn;
        for (let i = 0; i < 8; i++) {
            buf[offset + i] = Number((value >> BigInt(8 * i)) & mask);
        }
    }

    private static readU32(buf: Uint8Array, offset: number): number {
        return (buf[offset] | (buf[offset + 1] << 8) | (buf[offset + 2] << 16) | (buf[offset + 3] << 24)) >>> 0;
    }

    private static readU16(buf: Uint8Array, offset: number): number {
        return buf[offset] | (buf[offset + 1] << 8);
    }

    private static readU64(buf: Uint8Array, offset: number): bigint {
        let value = 0n;
        for (let i = 7; i >= 0; i--) {
            value = (value << 8n) | BigInt(buf[offset + i]);
        }
        return value;
    }

    /** FNV-1a 64 位哈希（与 C# ComputeCallIdHash 逐位一致；callId 为 ASCII 无差异）。 */
    private static computeCallIdHash(callId: string): bigint {
        const MASK = 0xFFFFFFFFFFFFFFFFn;
        let hash = 14695981039346656037n;
        for (let i = 0; i < callId.length; i++) {
            hash ^= BigInt(callId.charCodeAt(i) & 0xFFFF);
            hash = (hash * 1099511628211n) & MASK;
        }
        return hash;
    }
}

/** 重组中的视频帧。 */
class PendingFrame {
    readonly fragments: (Uint8Array | null)[];
    received: number = 0;
    lastActivityMs: number;

    constructor(fragmentCount: number, nowMs: number) {
        this.fragments = new Array(fragmentCount).fill(null);
        this.lastActivityMs = nowMs;
    }
}
