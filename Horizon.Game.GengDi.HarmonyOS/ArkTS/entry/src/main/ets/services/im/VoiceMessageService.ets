import { media } from '@kit.MediaKit';
import fs from '@ohos.file.fs';
import { AppContext } from '../../model/AppContext';
import { ApiClient } from '../ApiClient';
import { hilog } from '@kit.PerformanceAnalysisKit';

/**
 * 语音消息服务：录音器 + 全局唯一播放器。
 *
 * 录音（VoiceRecorder）：
 * - AVRecorder 录制 m4a（AAC 48kbps 单声道 44.1kHz），产物落沙箱 cacheDir；
 * - start 成功后 UI 计时；stop 正常收尾返回路径与时长；cancel 丢弃（文件删除）。
 *
 * 播放（VoicePlayerManager）：
 * - 全局单实例：切换播放时自动停上一条（回调通知旧卡片复位播放态）；
 * - 远程 URL 先经 ApiClient.downloadImMediaAsync 缓存（自签 https 需 caPath 校验），
 *   再以 fd:// 喂给 AVPlayer；completed/stopped/error 统一收尾释放。
 */

const TAG = 'GengDi.VoiceMessage';
/** 语音上限（对齐微信 60s） */
export const MAX_VOICE_SECONDS = 60;
/** 最短有效时长（低于此值视为误触） */
export const MIN_VOICE_SECONDS = 1;

/** 录音结果 */
export class VoiceRecordResult {
  /** 沙箱文件路径（m4a） */
  filePath: string = '';
  /** 时长（秒） */
  durationSeconds: number = 0;
}

/** 录音器（同一时刻仅一个实例；页面按住说话调用） */
export class VoiceRecorder {
  private recorder: media.AVRecorder | null = null;
  private recordFile: fs.File | null = null;
  private filePath: string = '';
  private startedAt: number = 0;

  get isRecording(): boolean {
    return this.recorder !== null;
  }

  /**
   * 开始录音（返回 false 表示设备/权限不可用，调用方应提示）。
   * 注意：prepare/start 任一步失败时必须显式 release 局部 recorder 并关闭局部文件——
   * 此时它们尚未赋给成员，漏释放会占用麦克风导致后续录音全部失败。
   */
  async start(): Promise<boolean> {
    if (this.recorder !== null) {
      return true;
    }
    let recorder: media.AVRecorder | null = null;
    let file: fs.File | null = null;
    let path = '';
    try {
      const ctx = AppContext.getInstance().context;
      path = `${ctx?.cacheDir ?? ''}/im_voice_${Date.now()}.m4a`;
      file = fs.openSync(path, fs.OpenMode.READ_WRITE | fs.OpenMode.CREATE | fs.OpenMode.TRUNC);
      const profile: media.AVRecorderProfile = {
        audioBitrate: 48000,
        audioChannels: 1,
        audioCodec: media.CodecMimeType.AUDIO_AAC,
        audioSampleRate: 44100,
        fileFormat: media.ContainerFormatType.CFT_MPEG_4A
      };
      const config: media.AVRecorderConfig = {
        audioSourceType: media.AudioSourceType.AUDIO_SOURCE_TYPE_MIC,
        profile: profile,
        url: `fd://${file.fd}`
      };
      recorder = await media.createAVRecorder();
      await recorder.prepare(config);
      await recorder.start();
      this.recorder = recorder;
      this.recordFile = file;
      this.filePath = path;
      this.startedAt = Date.now();
      return true;
    } catch (e) {
      hilog.warn(0x0001, TAG, '开始录音失败: %{public}s', (e as Error).message);
      // 失败路径完整清理（recorder 未 release 会持续占用麦克风）
      if (recorder !== null) {
        try {
          await recorder.release();
        } catch (_re) {
          // release 异常只能吞掉，避免掩盖原始错误
        }
      }
      if (file !== null) {
        try {
          fs.closeSync(file);
        } catch (_fe) {
          // 忽略
        }
      }
      if (path.length > 0) {
        try {
          if (fs.accessSync(path)) {
            fs.unlinkSync(path);
          }
        } catch (_de) {
          // 忽略
        }
      }
      return false;
    }
  }

  /** 停止并返回录音结果（duration < 最短值时返回 null，调用方提示"说话时间太短"） */
  async stop(): Promise<VoiceRecordResult | null> {
    const recorder = this.recorder;
    if (recorder === null) {
      return null;
    }
    const durationSeconds = (Date.now() - this.startedAt) / 1000;
    const recordedPath = this.filePath;
    this.recorder = null;
    this.filePath = '';
    let stopFailed = false;
    try {
      await recorder.stop();
    } catch (e) {
      stopFailed = true;
      hilog.warn(0x0001, TAG, '停止录音异常: %{public}s', (e as Error).message);
    }
    // release 必须执行（含 stop 失败路径），否则麦克风被占导致后续录音失败
    try {
      await recorder.release();
    } catch (_e) {
      // 已 stop 的 recorder release 报状态异常时忽略
    }
    this.closeFileQuietly();
    if (stopFailed || durationSeconds < MIN_VOICE_SECONDS) {
      this.deleteRecordedFile(recordedPath);
      return null;
    }
    const result = new VoiceRecordResult();
    result.filePath = recordedPath;
    // 上限截断（AVRecorder 未设 maxDuration 的兜底）
    result.durationSeconds = Math.min(Math.round(durationSeconds), MAX_VOICE_SECONDS);
    return result;
  }

  /** 取消录音（丢弃产物） */
  async cancel(): Promise<void> {
    const recorder = this.recorder;
    this.recorder = null;
    if (recorder !== null) {
      try {
        await recorder.stop();
      } catch (_e) {
        // 取消路径上的异常无需上抛
      }
      try {
        await recorder.release();
      } catch (_e) {
        // 忽略
      }
    }
    this.closeFileQuietly();
    this.deleteFileQuietly();
  }

  private closeFileQuietly(): void {
    if (this.recordFile !== null) {
      try {
        fs.closeSync(this.recordFile);
      } catch (_e) {
        // 忽略
      }
      this.recordFile = null;
    }
  }

  private deleteFileQuietly(): void {
    this.deleteRecordedFile(this.filePath);
    this.filePath = '';
  }

  private deleteRecordedFile(path: string): void {
    if (path.length > 0) {
      try {
        if (fs.accessSync(path)) {
          fs.unlinkSync(path);
        }
      } catch (_e) {
        // 忽略
      }
    }
  }
}

/** 全局唯一语音播放器（切换播放自动停上一条） */
export class VoicePlayerManager {
  private static _instance: VoicePlayerManager | null = null;
  static getInstance(): VoicePlayerManager {
    if (VoicePlayerManager._instance === null) {
      VoicePlayerManager._instance = new VoicePlayerManager();
    }
    return VoicePlayerManager._instance;
  }

  private player: media.AVPlayer | null = null;
  private playFile: fs.File | null = null;
  /** 当前播放的媒体 URL（用于识别是否同一条） */
  private currentUrl: string = '';
  /** 播放结束/停止回调（通知卡片复位播放态；切换时先回调旧的再清空） */
  private onFinish: (() => void) | null = null;

  get playingUrl(): string {
    return this.currentUrl;
  }

  /** 播放远程语音（缓存命中无网络开销）；再次点击同一条则停止 */
  async togglePlay(url: string, onFinish: () => void): Promise<void> {
    if (url.length === 0) {
      return;
    }
    // 同一条：停止
    if (this.currentUrl === url) {
      await this.stop();
      return;
    }
    // 切换：停掉上一条（触发旧回调复位 UI）
    await this.stop();
    this.onFinish = onFinish;
    this.currentUrl = url;
    try {
      const path = await ApiClient.getInstance().downloadImMediaAsync(url);
      await this.playLocal(path);
    } catch (e) {
      hilog.warn(0x0001, TAG, '语音播放失败: %{public}s %{public}s', url, (e as Error).message);
      await this.stop();
    }
  }

  private async playLocal(path: string): Promise<void> {
    const player = await media.createAVPlayer();
    const file = fs.openSync(path, fs.OpenMode.READ_ONLY);
    this.player = player;
    this.playFile = file;
    player.on('stateChange', async (state: string) => {
      if (state === 'initialized') {
        try {
          await player.prepare();
        } catch (e) {
          hilog.warn(0x0001, TAG, '语音 prepare 失败: %{public}s', (e as Error).message);
          await this.stop();
        }
      } else if (state === 'prepared') {
        try {
          await player.play();
        } catch (e) {
          hilog.warn(0x0001, TAG, '语音 play 失败: %{public}s', (e as Error).message);
          await this.stop();
        }
      } else if (state === 'completed' || state === 'stopped') {
        await this.stop();
      } else if (state === 'error') {
        await this.stop();
      }
    });
    player.url = `fd://${file.fd}`;
  }

  /** 停止当前播放并释放（触发 onFinish 复位卡片 UI） */
  async stop(): Promise<void> {
    const player = this.player;
    const file = this.playFile;
    this.player = null;
    this.playFile = null;
    const url = this.currentUrl;
    const finish = this.onFinish;
    this.currentUrl = '';
    this.onFinish = null;
    if (player !== null) {
      try {
        player.off('stateChange');
      } catch (_e) {
        // 忽略
      }
      try {
        // completed/stop 路径下 release 前需保证非 playing；直接 release 兜底
        await player.release();
      } catch (_e) {
        // 忽略释放异常
      }
    }
    if (file !== null) {
      try {
        fs.closeSync(file);
      } catch (_e) {
        // 忽略
      }
    }
    if (finish !== null) {
      finish();
    }
    // url 仅用于日志语义，此处保持参数引用避免 lint 未使用
    if (url.length > 0) {
      hilog.debug(0x0001, TAG, '语音播放结束: %{public}s', url);
    }
  }
}
