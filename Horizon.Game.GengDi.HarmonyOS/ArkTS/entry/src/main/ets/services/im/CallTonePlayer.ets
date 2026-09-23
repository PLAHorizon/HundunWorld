import { audio } from '@kit.AudioKit';
import { hilog } from '@kit.PerformanceAnalysisKit';

const TAG = 'GengDi.CallTone';
const DOMAIN = 0x0001;
const SAMPLE_RATE = 16000;
/** A 羽调式五声琶音（A4→C5→D5→E5→G5→E5→D5→C5→A4）：上扬回环，中国风悠扬 */
const MELODY: number[] = [
    440.00, 523.25, 587.33, 659.25, 783.99,
    659.25, 587.33, 523.25, 440.00
];
/** 单音时长与音间隙（毫秒）：420+30ms 的连奏构成 4.05s 一轮的琶音 */
const NOTE_MS = 420;
const NOTE_GAP_MS = 30;

/**
 * 通话提示音播放器：合成中国风五声琶音（拨弦音色）循环播放。
 *
 * 当前 SDK 的 audio 命名空间无 TonePlayer（API 24），
 * 故直接合成 PCM16（16kHz 单声道）经 AudioRenderer 播放，无需资源文件。
 * 音色：基频 + 2/3 次泛音 + 指数衰减包络，模拟古筝拨弦。
 * 两种场景：
 *   KIND_RINGBACK 主叫回铃——通话语音流（音量随通话音量），较轻
 *   KIND_INCOMING 来电影铃——铃声流（音量随系统铃音），更醒目
 * 全程异常自吞：提示音失败仅降级无声，绝不影响通话信令流程。
 */
export class CallTonePlayer {
    static readonly KIND_RINGBACK: number = 0;
    static readonly KIND_INCOMING: number = 1;

    private renderer: audio.AudioRenderer | null = null;
    private active: boolean = false;

    /** 开始循环播放提示音（幂等；kind 决定音频流与音量）。 */
    start(kind: number): void {
        this.stop();
        this.active = true;
        this.createRenderer(kind)
            .then((): Promise<void> => this.playLoop(kind))
            .catch((e: Error): void => {
                hilog.warn(DOMAIN, TAG, '提示音初始化失败（降级无声）: %{public}s', e.message);
            });
    }

    /** 停止播放并释放渲染器（幂等）。 */
    stop(): void {
        this.active = false;
        const renderer = this.renderer;
        this.renderer = null;
        if (renderer !== null) {
            renderer.stop().then((): Promise<void> => {
                return renderer.release();
            }).catch((): void => {
                // 忽略释放异常
            });
        }
    }

    /** 创建并启动渲染器（按场景选择音频流：来电影铃走铃声流，回铃走通话流）。 */
    private async createRenderer(kind: number): Promise<void> {
        const opts: audio.AudioRendererOptions = {
            streamInfo: {
                samplingRate: audio.AudioSamplingRate.SAMPLE_RATE_16000,
                channels: audio.AudioChannel.CHANNEL_1,
                sampleFormat: audio.AudioSampleFormat.SAMPLE_FORMAT_S16LE,
                encodingType: audio.AudioEncodingType.ENCODING_TYPE_RAW
            },
            rendererInfo: {
                content: audio.ContentType.CONTENT_TYPE_RINGTONE,
                usage: kind === CallTonePlayer.KIND_INCOMING
                    ? audio.StreamUsage.STREAM_USAGE_RINGTONE
                    : audio.StreamUsage.STREAM_USAGE_VOICE_COMMUNICATION,
                rendererFlags: 0
            }
        };
        const renderer = await audio.createAudioRenderer(opts);
        if (!this.active) {
            // 等待期间已停止（秒接通/秒挂断）：丢弃迟到渲染器
            renderer.release().catch((): void => {
            });
            throw new Error('提示音已停止');
        }
        this.renderer = renderer;
        await renderer.start();
    }

    /** 循环播放：写入一轮琶音后按播放时长+停顿休眠，再写下一轮。 */
    private async playLoop(kind: number): Promise<void> {
        const buf = CallTonePlayer.synthesize(kind === CallTonePlayer.KIND_INCOMING ? 0.55 : 0.38);
        const durationMs = buf.byteLength / 2 / SAMPLE_RATE * 1000;
        // 回铃停顿更长（等待感），来电停顿短（催接感）
        const pauseMs = kind === CallTonePlayer.KIND_INCOMING ? 350 : 700;
        while (this.active && this.renderer !== null) {
            try {
                await this.renderer.write(buf.slice(0));
            } catch {
                break; // 渲染器异常（停止/释放竞态）：退出循环
            }
            await CallTonePlayer.sleep(durationMs + pauseMs - 60);
        }
    }

    private static sleep(ms: number): Promise<void> {
        return new Promise<void>((resolve) => {
            setTimeout((): void => {
                resolve();
            }, ms);
        });
    }

    /**
     * 合成一轮琶音 PCM16。
     * 拨弦音色：基频 + 0.35×二次泛音 + 0.12×三次泛音；
     * 包络：5ms 线性起音后指数衰减（模拟弦振衰减，衔接自然悠扬）。
     */
    private static synthesize(volume: number): ArrayBuffer {
        const noteSamples = Math.floor(SAMPLE_RATE * NOTE_MS / 1000);
        const gapSamples = Math.floor(SAMPLE_RATE * NOTE_GAP_MS / 1000);
        const totalSamples = MELODY.length * (noteSamples + gapSamples);
        const buf = new ArrayBuffer(totalSamples * 2);
        const view = new DataView(buf);
        const amp = volume * 32767;
        const fade = SAMPLE_RATE / 200; // 5ms 起音
        let offset = 0;
        for (const freq of MELODY) {
            for (let i = 0; i < noteSamples; i++) {
                const t = i / SAMPLE_RATE;
                const env = Math.min(1, i / fade) * Math.exp(-t * 2.0);
                const s = Math.sin(2 * Math.PI * freq * t)
                    + 0.35 * Math.sin(4 * Math.PI * freq * t)
                    + 0.12 * Math.sin(6 * Math.PI * freq * t);
                view.setInt16(offset * 2, Math.round(amp * env * s / 1.47), true);
                offset++;
            }
            offset += gapSamples;
        }
        return buf;
    }
}
