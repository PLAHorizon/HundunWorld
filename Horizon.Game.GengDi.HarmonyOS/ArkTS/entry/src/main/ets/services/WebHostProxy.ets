import { FlowerDataService } from './FlowerDataService';
import { NativeAudioBridge } from './NativeAudioBridge';
import { hilog } from '@kit.PerformanceAnalysisKit';

const TAG = 'GengDi.WebHost';
const DOMAIN = 0x0001;

/**
 * Web 桥聚合代理。
 *
 * ArkWeb 的 javaScriptProxy 每次仅支持注册命名唯一的一个桥对象（name='flowerApi'）。
 * 此处通过对 FlowerDataService 继承并追加原生音频播放方法，把音乐 <audio> 播放
 * 从 ArkWeb 内部音频改为系统原生 AVPlayer（解决模拟器 ArkWeb 音频无声问题），
 * 同时完整保留既有业务数据方法的对外调用。
 */
export class WebHostProxy extends FlowerDataService {
  private audio: NativeAudioBridge = new NativeAudioBridge();

  /** 开始/切换播放（同步触发，内部转原生 AVPlayer） */
  startPlay(url: string): void {
    hilog.info(DOMAIN, TAG, 'startPlay url=%{public}s', url);
    this.audio.startPlay(url);
  }

  /** 继续播放 */
  resumePlay(): void {
    this.audio.resumePlay();
  }

  /** 暂停播放 */
  pausePlay(): void {
    this.audio.pausePlay();
  }

  /** 跳转（毫秒） */
  seekMs(ms: number): void {
    this.audio.seekMs(ms);
  }

  /** 设置音量 0~1 */
  setPlayVol(v: number): void {
    this.audio.setPlayVol(v);
  }

  /** 读取播放位置（毫秒） */
  getPlayPos(): number {
    return this.audio.getPlayPos();
  }

  /** 读取总时长（毫秒） */
  getPlayDur(): number {
    return this.audio.getPlayDur();
  }

  /** 读取播放状态 */
  getPlayState(): string {
    return this.audio.getPlayState();
  }

  /** 读取错误信息 */
  getPlayError(): string {
    return this.audio.getPlayError();
  }

  /** 原生播放是否可用 */
  nativeReady(): boolean {
    return this.audio.isAvailable;
  }

  /** 页面销毁时释放播放器 */
  releaseAudio(): void {
    this.audio.release();
  }
}