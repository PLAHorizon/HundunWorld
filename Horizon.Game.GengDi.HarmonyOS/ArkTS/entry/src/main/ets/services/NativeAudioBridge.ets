import { media } from '@kit.MediaKit';
import { hilog } from '@kit.PerformanceAnalysisKit';

const TAG = 'GengDi.AudioBridge';
const DOMAIN = 0x0001;

/**
 * 原生音频播放桥。
 *
 * 问题背景：鸿蒙 ArkWeb 的 HTML5 <audio> 在模拟器上仅于 ArkWeb 进程内解码
 * （currentTime 正常推进），但音频流从未进入系统音频输出管线
 * （AudioPolicyService 的 AudioSessionMap 恒为空、media 默认输出设备 UNKNOWN），
 * 因此物理无声。此处改用 @ohos.multimedia.media 的 AVPlayer 原生播放，
 * 由系统媒体框架把音频真实路由到扬声器。
 *
 * 通过 javaScriptProxy 以「同步方法 + 状态/进度轮询」与 HTML 交互：
 * 播放控制方法（startPlay/resumePlay/pausePlay/seekMs/setPlayVol）为同步触发，
 * 内部异步执行；getPlayPos/getPlayDur/getPlayState/getPlayError 为同步查询。
 */
export class NativeAudioBridge {
  private av: media.AVPlayer | null = null;
  private initing: boolean = false;
  private pendingUrl: string = '';

  /** 同步可见播放状态 */
  private state: string = 'idle';
  private pos: number = 0;   // 毫秒
  private dur: number = 0;   // 毫秒
  private errorText: string = '';
  private playWhenReady: boolean = false;
  /** 是否已完成首次 setUrl（真正进入过 initialized/stopped），用于决定是否需要 reset */
  private urlSetOnce: boolean = false;
  /** 目标音量 0~1，缓存到 AVPlayer 进入 prepared 后再应用 */
  private vol: number = 1.0;
  /** 等待进入 initialized 后自动执行 prepare */
  private pendingPrepare: boolean = false;
  /** 防止首播与切歌重复触发 prepare */
  private preparing: boolean = false;

  /** AVPlayer 是否已就绪（供前端判断走原生还是回退 <audio>） */
  get isAvailable(): boolean {
    return this.av !== null;
  }

  private ensureInit(): void {
    if (this.av || this.initing) return;
    this.initing = true;
    media.createAVPlayer().then((p: media.AVPlayer) => {
      this.av = p;
      this.initing = false;
      hilog.info(DOMAIN, TAG, 'AVPlayer created，用 title 后可 url=空；pending=%{public}s', this.pendingUrl.length > 0 ? 'yes' : 'no');
      p.on('stateChange', (s: media.AVPlayerState, reason: media.StateChangeReason) => {
        const st = typeof s === 'string' ? s : String(s);
        this.state = st;
        if (st === 'initialized') {
          // setUrl 成功，可以 prepare 了
          if (this.pendingPrepare && !this.preparing) {
            this.preparing = true;
            this.pendingPrepare = false;
            p.prepare().catch((e: Error) => {
              this.state = 'error';
              this.errorText = e.message || String(e);
              this.preparing = false;
              hilog.error(DOMAIN, TAG, 'prepare 失败: %{public}s', this.errorText);
            });
          }
        } else if (st === 'prepared') {
          if (p.duration > 0) this.dur = p.duration;
          this.preparing = false;
          // prepared 状态才允许设置音量，避免 5400102 报错
          p.setVolume(this.vol);
          if (this.playWhenReady) {
            p.play().catch((e: Error) => {
              this.state = 'error';
              this.errorText = e.message || String(e);
              hilog.error(DOMAIN, TAG, 'play 失败: %{public}s', this.errorText);
            });
          }
        } else if (st === 'playing') {
          if (p.duration > 0) this.dur = p.duration;
        } else if (st === 'completed') {
          if (p.duration > 0) this.dur = p.duration;
          this.pos = this.dur;
        } else if (st === 'error' || st === 'failed') {
          this.errorText = 'AVPlayer error' + (reason ? ': ' + String(reason) : '');
        }
        hilog.info(DOMAIN, TAG, 'stateChange -> %{public}s', st);
      });
      p.on('timeUpdate', (t: number) => {
        this.pos = t;
      });
      p.on('error', (err: BusinessError) => {
        this.state = 'error';
        this.errorText = String(err);
        hilog.error(DOMAIN, TAG, 'AVPlayer error event: %{public}s', this.errorText);
      });
      // 初始化完成后若有排队 URL，继续播放
      if (this.pendingUrl.length > 0) {
        const u = this.pendingUrl;
        this.pendingUrl = '';
        this.setupTrack(u);
      }
    }).catch((e: Error) => {
      this.initing = false;
      this.state = 'error';
      this.errorText = e.message || String(e);
      hilog.error(DOMAIN, TAG, 'AVPlayer 初始化失败: %{public}s', this.errorText);
    });
  }

  /**
   * （同步桥接）开始播放指定 URL。AVPlayer 未就绪时先入队，就绪后自动播放。
   */
  startPlay(url: string): void {
    hilog.info(DOMAIN, TAG, 'startPlay url=%{public}s av=%{public}s', url, this.av !== null);
    this.playWhenReady = true;
    this.pos = 0;
    this.errorText = '';
    this.state = 'loading';
    if (!this.av) {
      this.pendingUrl = url;
      this.ensureInit();
      return;
    }
    this.setupTrack(url);
  }

  private setupTrack(url: string): void {
    const p = this.av;
    if (!p) return;
    this.state = 'loading';
    this.playWhenReady = true;
    // 状态驱动：setUrl 后等待 stateChange -> initialized 回调再 prepare，避免
    // 未就绪就 prepare 报 "current state is not stopped or initialized"
    const doSetUrl = (): void => {
      this.urlSetOnce = true;
      this.pendingPrepare = true;
      this.preparing = false;
      p.url = url;
    };
    if (this.urlSetOnce) {
      // 切歌：reset 回 idle，再 setUrl
      p.reset().then(() => {
        doSetUrl();
      }).catch((e: Error) => {
        this.state = 'error';
        this.errorText = e.message || String(e);
        hilog.error(DOMAIN, TAG, 'reset 失败: %{public}s', this.errorText);
      });
    } else {
      doSetUrl();
    }
  }

  /** （同步桥接）继续播放 */
  resumePlay(): void {
    this.playWhenReady = true;
    if (this.av) {
      this.av.play().catch((e: Error) => {
        this.state = 'error';
        this.errorText = e.message || String(e);
      });
    }
  }

  /** （同步桥接）暂停播放 */
  pausePlay(): void {
    this.playWhenReady = false;
    if (this.av) {
      this.av.pause().catch((e: Error) => {
        this.state = 'error';
        this.errorText = e.message || String(e);
      });
    }
  }

  /** （同步桥接）跳转到指定毫秒 */
  seekMs(ms: number): void {
    this.pos = ms;
    if (this.av && ms >= 0) {
      this.av.seek(ms);
    }
  }

  /** （同步桥接）设置音量 0~1：先缓存，等 AVPlayer 进入 prepared/playing 再应用 */
  setPlayVol(v: number): void {
    this.vol = v;
    const p = this.av;
    if (p && (this.state === 'prepared' || this.state === 'playing' || this.state === 'paused')) {
      p.setVolume(v);
    }
  }

  /** （同步桥接）读取当前播放位置（毫秒） */
  getPlayPos(): number {
    return this.pos;
  }

  /** （同步桥接）读取总时长（毫秒） */
  getPlayDur(): number {
    return this.dur;
  }

  /** （同步桥接）读取播放状态：idle/loading/prepared/playing/paused/completed/stopped/released/error */
  getPlayState(): string {
    return this.state;
  }

  /** （同步桥接）读取错误信息 */
  getPlayError(): string {
    return this.errorText;
  }

  /** （同步桥接）原生播放是否可用 */
  nativeReady(): boolean {
    return this.isAvailable;
  }

  /** 页面销毁时释放播放器 */
  release(): void {
    if (this.av) {
      const p = this.av;
      this.av = null;
      p.release().catch((e: Error) => {
        hilog.warn(DOMAIN, TAG, 'release 失败: %{public}s', e.message);
      });
    }
  }
}