import { AppTheme } from '../theme/AppTheme';
import { CallSessionManager, CallUiState } from '../services/im/CallSessionManager';
import { ImCallType } from '../services/im/ImMessages';
import { image } from '@kit.ImageKit';

/**
 * 全屏通话浮层：拨出等待 / 来电响铃 / 通话中 / 已结束 四态。
 * 挂载于 Index.ets（全局来电）与 ChatDetailPage.ets（聊天内拨打）。
 * 状态源为 CallSessionManager 单例，本组件仅做 @State 镜像。
 */

/** 呼吸缩放动画（来电头像） */
@Component
struct PulseAvatar {
    @State pulseScale: number = 1.0;
    private timer: number = -1;
    peerInitial: string = '';
    avatarSize: number = 110;

    aboutToAppear(): void {
        this.timer = setInterval(() => {
            this.pulseScale = this.pulseScale > 1.0 ? 1.0 : 1.08;
        }, 900);
    }

    aboutToDisappear(): void {
        if (this.timer >= 0) {
            clearInterval(this.timer);
            this.timer = -1;
        }
    }

    build() {
        Column({ space: 10 }) {
            Text(this.peerInitial.length > 0 ? this.peerInitial[0] : '?')
                .fontSize(44)
                .fontWeight(FontWeight.Bold)
                .fontColor('#ffffff')
        }
        .width(this.avatarSize)
        .height(this.avatarSize)
        .borderRadius(this.avatarSize / 2)
        .linearGradient({ angle: 135, colors: [[AppTheme.primary, 0], [AppTheme.primaryDeep, 1]] })
        .justifyContent(FlexAlign.Center)
        .scale({ x: this.pulseScale, y: this.pulseScale })
        .animation({ duration: 900, curve: Curve.EaseInOut })
    }
}

@Component
export struct CallOverlay {
    private mgr: CallSessionManager = CallSessionManager.getInstance();
    @State show: boolean = false;
    @State state: number = CallUiState.Idle;
    @State callType: number = ImCallType.Audio;
    @State peerName: string = '';
    @State callSeconds: number = 0;
    @State muted: boolean = false;
    @State speakerOn: boolean = false;
    @State cameraOff: boolean = false;
    @State peerCameraOff: boolean = false;
    @State endText: string = '';
    /** 对端真实视频帧（媒体引擎 UDP 收包重组，JPEG→PixelMap） */
    @State remoteFrame: image.PixelMap | null = null;
    /** 本端摄像头预览帧（引擎采集回调，JPEG→PixelMap） */
    @State localFrame: image.PixelMap | null = null;

    /** 状态镜像同步（manager 单例非响应式，必须镜像到 @State 驱动 UI） */
    private sync(): void {
        this.show = this.mgr.state !== CallUiState.Idle;
        this.state = this.mgr.state;
        this.callType = this.mgr.callType;
        this.peerName = this.mgr.peerName;
        this.callSeconds = this.mgr.callSeconds;
        this.muted = this.mgr.muted;
        this.speakerOn = this.mgr.speakerOn;
        this.cameraOff = this.mgr.cameraOff;
        this.peerCameraOff = this.mgr.peerCameraOff;
        this.endText = this.mgr.endText;
        // 通话结束/未接通时清空视频帧（释放 PixelMap 防泄漏）
        if (this.state !== CallUiState.Active) {
            if (this.remoteFrame !== null) {
                this.remoteFrame.release();
                this.remoteFrame = null;
            }
            if (this.localFrame !== null) {
                this.localFrame.release();
                this.localFrame = null;
            }
        }
    }

    /** 绑定到 manager 的监听函数（保持引用以便 off） */
    private listener: () => void = () => {
        this.sync();
    };

    aboutToAppear(): void {
        this.mgr.on(this.listener);
        this.mgr.setVideoSink((jpeg: Uint8Array): void => {
            this.updateFrame(false, jpeg);
        }, (jpeg: Uint8Array): void => {
            this.updateFrame(true, jpeg);
        });
        this.sync();
    }

    aboutToDisappear(): void {
        this.mgr.off(this.listener);
        this.mgr.setVideoSink(null, null);
    }

    /** JPEG 帧 → PixelMap 并刷新对应画面（旧帧释放，防内存泄漏）。 */
    private async updateFrame(remote: boolean, jpeg: Uint8Array): Promise<void> {
        try {
            const copy = new ArrayBuffer(jpeg.byteLength);
            new Uint8Array(copy).set(jpeg);
            const source = image.createImageSource(copy);
            const pixelMap = await source.createPixelMap();
            source.release();
            if (remote) {
                this.remoteFrame?.release();
                this.remoteFrame = pixelMap;
            } else {
                this.localFrame?.release();
                this.localFrame = pixelMap;
            }
        } catch {
            // 解码失败丢弃该帧（通话容忍）
        }
    }

    private isVideo(): boolean {
        return this.callType === ImCallType.Video;
    }

    /** 状态主文案 */
    private statusText(): string {
        if (this.state === CallUiState.Dialing) {
            return '正在等待对方接受…';
        }
        if (this.state === CallUiState.Incoming) {
            return `邀请你${this.isVideo() ? '视频' : '语音'}通话`;
        }
        if (this.state === CallUiState.Active) {
            return this.mgr.formatDuration(this.callSeconds);
        }
        if (this.state === CallUiState.Ended) {
            return this.endText.length > 0 ? this.endText : '通话结束';
        }
        return '';
    }

    /**
     * 控制按钮（静音/免提/摄像头）。
     * 注意：@Builder 参数按值传递不会随 @State 变化刷新（ArkUI 已知行为），
     * 因此按钮必须直接在 Builder 体内引用 @State（muted/speakerOn/cameraOff），
     * 状态变化才能触发按钮高亮与文案刷新。
     */
    @Builder
    muteControl() {
        Column({ space: 6 }) {
            Button() {
                SymbolGlyph($r('sys.symbol.mic'))
                    .fontSize(22)
                    .fontColor([this.muted ? AppTheme.primary : '#ffffff'])
            }
            .width(56)
            .height(56)
            .borderRadius(28)
            .backgroundColor(this.muted ? AppTheme.activeBg : 'rgba(255,255,255,0.10)')
            .border({ width: 1, color: this.muted ? AppTheme.primary : AppTheme.cardBorder })
            .padding(0)
            .onClick(() => this.mgr.toggleMute())

            Text(this.muted ? '已静音' : '静音')
                .fontSize(11)
                .fontColor(this.muted ? AppTheme.primary : 'rgba(255,255,255,0.72)')
        }
        .width(64)
    }

    @Builder
    speakerControl() {
        Column({ space: 6 }) {
            Button() {
                SymbolGlyph($r('sys.symbol.speaker'))
                    .fontSize(22)
                    .fontColor([this.speakerOn ? AppTheme.primary : '#ffffff'])
            }
            .width(56)
            .height(56)
            .borderRadius(28)
            .backgroundColor(this.speakerOn ? AppTheme.activeBg : 'rgba(255,255,255,0.10)')
            .border({ width: 1, color: this.speakerOn ? AppTheme.primary : AppTheme.cardBorder })
            .padding(0)
            .onClick(() => this.mgr.toggleSpeaker())

            Text(this.speakerOn ? '免提开' : '免提')
                .fontSize(11)
                .fontColor(this.speakerOn ? AppTheme.primary : 'rgba(255,255,255,0.72)')
        }
        .width(64)
    }

    @Builder
    cameraControl() {
        Column({ space: 6 }) {
            Button() {
                SymbolGlyph($r('sys.symbol.video'))
                    .fontSize(22)
                    .fontColor([this.cameraOff ? AppTheme.primary : '#ffffff'])
            }
            .width(56)
            .height(56)
            .borderRadius(28)
            .backgroundColor(this.cameraOff ? AppTheme.activeBg : 'rgba(255,255,255,0.10)')
            .border({ width: 1, color: this.cameraOff ? AppTheme.primary : AppTheme.cardBorder })
            .padding(0)
            .onClick(() => this.mgr.toggleCamera())

            Text(this.cameraOff ? '摄像头关' : '摄像头')
                .fontSize(11)
                .fontColor(this.cameraOff ? AppTheme.primary : 'rgba(255,255,255,0.72)')
        }
        .width(64)
    }

    /** 主操作圆按钮（接听/拒接/挂断/取消） */
    @Builder
    actionButton(icon: Resource, bg: string, onTap: () => void) {
        Button() {
            SymbolGlyph(icon)
                .fontSize(28)
                .fontColor(['#ffffff'])
        }
        .width(68)
        .height(68)
        .borderRadius(34)
        .backgroundColor(bg)
        .shadow({ radius: 18, color: 'rgba(0,0,0,0.35)', offsetY: 6 })
        .padding(0)
        .onClick(() => onTap())
    }

    build() {
        if (this.show) {
            Stack({ alignContent: Alignment.Top }) {
                // 全屏深色渐变底
                Column()
                    .width('100%')
                    .height('100%')
                    .linearGradient({
                        angle: 200,
                        colors: this.isVideo()
                            ? [['#1b2b33', 0], ['#171d2b', 1]]
                            : [['#243447', 0], ['#1d2434', 0.5], ['#171d2b', 1]]
                    })

                Column() {
                    // 顶部：对端信息
                    Column({ space: 12 }) {
                        Text(this.isVideo() ? '📹 视频通话' : '📞 语音通话')
                            .fontSize(13)
                            .fontColor('rgba(255,255,255,0.66)')
                            .padding({ left: 12, right: 12, top: 4, bottom: 4 })
                            .borderRadius(12)
                            .backgroundColor('rgba(255,255,255,0.08)')
                        Text(this.peerName)
                            .fontSize(24)
                            .fontWeight(FontWeight.Bold)
                            .fontColor('#ffffff')
                            .maxLines(1)
                            .textOverflow({ overflow: TextOverflow.Ellipsis })
                    }
                    .width('100%')
                    .alignItems(HorizontalAlign.Center)
                    .margin({ top: 84 })

                    // 中央：视频区（视频通话）或大头像（语音）
                    Column() {
                        if (this.isVideo()) {
                            // 视频通话：远端大画面 + 本地小窗（右下角悬浮）
                            Stack({ alignContent: Alignment.BottomEnd }) {
                                if (this.state === CallUiState.Active && !this.peerCameraOff
                                    && this.remoteFrame !== null) {
                                    // 对端真实画面（UDP 直连收包重组）
                                    Image(this.remoteFrame)
                                        .width(230)
                                        .height(300)
                                        .borderRadius(24)
                                        .objectFit(ImageFit.Cover)
                                } else {
                                    Column({ space: 10 }) {
                                        Text(this.peerName.length > 0 ? this.peerName[0] : '?')
                                            .fontSize(72)
                                            .fontWeight(FontWeight.Bold)
                                            .fontColor('rgba(255,255,255,0.28)')
                                        Text(this.state === CallUiState.Active && this.peerCameraOff
                                            ? '对方已关闭摄像头' : '等待接听…')
                                            .fontSize(12)
                                            .fontColor('rgba(255,255,255,0.34)')
                                    }
                                    .width(230)
                                    .height(300)
                                    .borderRadius(24)
                                    .backgroundColor('rgba(255,255,255,0.05)')
                                    .border({ width: 1, color: AppTheme.cardBorder })
                                    .justifyContent(FlexAlign.Center)
                                }

                                // 本地预览小窗（通话中且本端摄像头开启）
                                if (this.state === CallUiState.Active && !this.cameraOff
                                    && this.localFrame !== null) {
                                    Image(this.localFrame)
                                        .width(78)
                                        .height(104)
                                        .borderRadius(12)
                                        .objectFit(ImageFit.Cover)
                                        .border({ width: 1.5, color: 'rgba(255,255,255,0.4)' })
                                        .margin({ right: 10, bottom: 10 })
                                }
                            }
                            .margin({ top: 28 })
                        } else {
                            Column() {
                                if (this.state === CallUiState.Incoming || this.state === CallUiState.Dialing) {
                                    PulseAvatar({ peerInitial: this.peerName })
                                } else {
                                    Column({ space: 10 }) {
                                        Text(this.peerName.length > 0 ? this.peerName[0] : '?')
                                            .fontSize(44)
                                            .fontWeight(FontWeight.Bold)
                                            .fontColor('#ffffff')
                                    }
                                    .width(110)
                                    .height(110)
                                    .borderRadius(55)
                                    .linearGradient({ angle: 135, colors: [[AppTheme.primary, 0], [AppTheme.primaryDeep, 1]] })
                                    .justifyContent(FlexAlign.Center)
                                }
                            }
                            .margin({ top: 48 })
                        }
                    }
                    .layoutWeight(1)
                    .width('100%')
                    .alignItems(HorizontalAlign.Center)

                    // 状态文案
                    Text(this.statusText())
                        .fontSize(16)
                        .fontColor('rgba(255,255,255,0.88)')
                        .margin({ bottom: 40 })

                    // 底部操作区
                    if (this.state === CallUiState.Incoming) {
                        Row({ space: 64 }) {
                            Column({ space: 8 }) {
                                this.actionButton($r('sys.symbol.phone_down_fill'), AppTheme.down, () => {
                                    this.mgr.reject();
                                })
                                Text('拒接')
                                    .fontSize(13)
                                    .fontColor('rgba(255,255,255,0.78)')
                            }
                            Column({ space: 8 }) {
                                this.actionButton($r('sys.symbol.phone'), AppTheme.up, () => {
                                    this.mgr.accept();
                                })
                                Text('接听')
                                    .fontSize(13)
                                    .fontColor('rgba(255,255,255,0.78)')
                            }
                        }
                        .margin({ bottom: 72 })
                    }
                    else if (this.state === CallUiState.Dialing) {
                        Column({ space: 8 }) {
                            this.actionButton($r('sys.symbol.phone_down_fill'), AppTheme.down, () => {
                                this.mgr.cancel();
                            })
                            Text('取消')
                                .fontSize(13)
                                .fontColor('rgba(255,255,255,0.78)')
                        }
                        .margin({ bottom: 72 })
                    }
                    else if (this.state === CallUiState.Active) {
                        Column({ space: 26 }) {
                            Row({ space: 20 }) {
                                this.muteControl()
                                this.speakerControl()
                                if (this.isVideo()) {
                                    this.cameraControl()
                                }
                            }
                            this.actionButton($r('sys.symbol.phone_down_fill'), AppTheme.down, () => {
                                this.mgr.hangup();
                            })
                        }
                        .margin({ bottom: 64 })
                    }
                    else {
                        // Ended：无按钮，自动关闭
                        Column().height(100)
                    }
                }
                .width('100%')
                .height('100%')
            }
            .width('100%')
            .height('100%')
            .position({ x: 0, y: 0 })
        }
    }
}
