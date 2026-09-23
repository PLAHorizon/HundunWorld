import { AppTheme } from '../theme/AppTheme';
import { ChatAvatar } from '../components/CommonWidgets';
import { CallOverlay } from '../components/CallOverlay';
import { ChatMessageItem, ConversationItem, ImViewModel } from '../viewmodels/ImViewModel';
import { RichMessageContent, RichMessageContentSerializer, MessageType } from '../model/RichMessageContent';
import { CallSessionManager } from '../services/im/CallSessionManager';
import { ImCallType, ImContentType } from '../services/im/ImMessages';
import { VoicePlayerManager, VoiceRecorder, VoiceRecordResult, MAX_VOICE_SECONDS } from '../services/im/VoiceMessageService';
import { AppContext } from '../model/AppContext';
import { ApiClient } from '../services/ApiClient';
import { pasteboard } from '@kit.BasicServicesKit';
import { hilog } from '@kit.PerformanceAnalysisKit';
import { photoAccessHelper } from '@kit.MediaLibraryKit';
import { camera, cameraPicker } from '@kit.CameraKit';
import { geoLocationManager } from '@kit.LocationKit';
import { abilityAccessCtrl, common, Permissions, Want } from '@kit.AbilityKit';
import { filePreview } from '@kit.PreviewKit';
import { fileUri } from '@kit.CoreFileKit';
import fs from '@ohos.file.fs';

/**
 * 聊天详情页（对标微信聊天页布局）。
 * 消息流绑定 ImViewModel.messages，发送走 IM 网关（私聊/群聊），
 * 乐观上屏 + Ack 状态回写（发送中/已发送/失败）。
 * 支持富文本消息解析（对齐 C# RichMessageContent）。
 * 微信交互：双端头像、长按消息菜单（复制/删除）、顶栏通话入口、
 * 输入栏 语音/表情/加号 三键布局、加号扩展面板
 * （相册/拍摄发送图片，位置发送定位卡片，音视频通话）。
 */

const TAG = 'GengDi.ChatDetailPage';

/** 表情面板分类 */
class EmojiCategory {
    name: string = '';
    emojis: string[] = [];
}

/** 表情集（常用聊天表情，按分类） */
const EMOJI_CATEGORIES: EmojiCategory[] = [
    {
        name: '常用',
        emojis: ['😀', '😂', '🥰', '😍', '🤔', '😅', '😊', '🙃', '😉', '😎', '🥳', '😭',
            '😡', '👍', '👎', '🙏', '👌', '💪', '🤝', '👏', '❤️', '💔', '✨', '🔥',
            '🌹', '🌸', '🌻', '🌷']
    },
    {
        name: '表情',
        emojis: ['😁', '😆', '😋', '😘', '🤗', '🤭', '😴', '🥱', '😱', '🤯', '🥺', '😤',
            '😢', '🤩', '🫡', '🤫', '🫢', '😵', '🤠', '😈', '🤡', '👻', '💀', '🤖',
            '😺', '🐶', '🙈', '🙉']
    },
    {
        name: '手势',
        emojis: ['✌️', '🤞', '🤟', '🤘', '👋', '🖐️', '✋', '🖖', '☝️', '👇', '👈', '👉',
            '👊', '✊', '🤛', '🤜', '🫶', '🤌', '🫰', '💝', '🎁', '🎉', '🎊', '🎈',
            '🏆', '🥇', '🎯', '🎮']
    },
    {
        name: '自然',
        emojis: ['🌞', '🌙', '⭐', '🌟', '☁️', '🌧️', '⛈️', '🌈', '❄️', '🌊', '🍀', '🌱',
            '🌿', '🍁', '🌾', '🌵', '🌴', '🌲', '💐', '🌼', '🌷', '🦋', '🐝', '🐞',
            '🐦', '🕊️', '🐇', '🦌']
    },
    {
        name: '物品',
        emojis: ['📞', '📱', '💻', '🎧', '📷', '📺', '🔋', '💡', '🔒', '🔑', '🛒', '💰',
            '💎', '📦', '📝', '📖', '✏️', '📌', '🧧', '🍚', '🍜', '🍵', '☕', '🍰',
            '🍺', '🥂', '🎂', '🚗']
    }
];

class ChatTarget {
  conversationId: string = '';
  peerId: number = 0;
  displayName: string = '';
  isGroup: boolean = false;
}

/** 待确认发送的位置（定位成功后弹确认面板） */
class PendingLocation {
  lat: number = 0;
  lng: number = 0;
  name: string = '';
  address: string = '';
}

/** 富文本消息渲染组件（在组件内部解析，避开ArkTS Builder限制） */
@Component
struct RichMessageRenderer {
  message!: ChatMessageItem
  isSelf!: boolean
  /** 远程图片解析后的沙箱路径（自签 https 不能被 Image 直接加载，须落盘转本地） */
  @State imgSrc: string = '';
  /** 语音播放中（点击切换播放/停止） */
  @State playing: boolean = false;
  /** 图片点击查看大图（参数为本地沙箱路径；由宿主页面显示全屏查看器） */
  onImageTap: (localPath: string) => void = (_p: string) => {
  };
  private voicePlayer: VoicePlayerManager = VoicePlayerManager.getInstance();

  aboutToAppear(): void {
    // 图片消息：把远程 mediaUrl 下载到沙箱缓存后本地渲染（命中缓存无网络开销）
    if (this.message.contentType === ImContentType.Image) {
      const url = this.getRichContent().mediaUrl;
      if (url.startsWith('http')) {
        ApiClient.getInstance().downloadImMediaAsync(url).then((path: string) => {
          this.imgSrc = path;
        }).catch((_e: Error) => {
          // 下载失败保持占位图
        });
      }
    }
  }

  private getRichContent(): RichMessageContent {
    try {
      return RichMessageContentSerializer.deserialize(this.message.contentType, this.message.content || '');
    } catch (_e) {
      return RichMessageContent.createText(this.message.content || '');
    }
  }

  private getShowStatus(): boolean {
    return this.isSelf && (this.message.status === 0 || this.message.status === 4);
  }

  private getStatusText(): string {
    if (this.message.status === 0) {
      return '发送中…';
    }
    if (this.message.status === 4) {
      return '发送失败';
    }
    return '';
  }

  build() {
    Column({ space: 4 }) {
      if (this.message.contentType === ImContentType.Audio) {
        this.AudioCard()
      } else if (this.message.contentType === ImContentType.Location) {
        this.LocationCard()
      } else if (this.message.contentType === ImContentType.Image) {
        this.ImageCard()
      } else if (this.message.contentType === MessageType.Video) {
        this.VideoCard()
      } else if (this.message.contentType === MessageType.LinkCard) {
        this.LinkCard()
      } else if (this.message.contentType === MessageType.File) {
        this.FileCard()
      } else {
        this.TextBubble()
      }
    }
    .alignItems(this.isSelf ? HorizontalAlign.End : HorizontalAlign.Start)
    .width('100%')
  }

  /** 语音消息卡片（🎤 + 声波 + 时长；点击播放/停止，全局单播放器） */
  @Builder
  private AudioCard() {
    Column({ space: 4 }) {
      Row({ space: 8 }) {
        if (this.isSelf) {
          // 自己的语音：时长在左，图标在右（微信镜像布局）
          Text(`${this.getRichContent().getAudioDuration()}"`)
            .fontSize(13)
            .fontColor(this.isSelf ? '#ffffff' : AppTheme.text)
          Text(this.playing ? '⏸' : '🎤')
            .fontSize(18)
        } else {
          Text(this.playing ? '⏸' : '🎤')
            .fontSize(18)
          Text(`${this.getRichContent().getAudioDuration()}"`)
            .fontSize(13)
            .fontColor(AppTheme.text)
        }
      }
      .padding({ left: 16, right: 16, top: 10, bottom: 10 })
      .constraintSize({ minWidth: 90 + Math.min(this.getRichContent().getAudioDuration(), 40) * 2 })
      .justifyContent(FlexAlign.Center)
      .borderRadius({
        topLeft: this.isSelf ? AppTheme.chatBubbleRadius : AppTheme.chatBubbleTipRadius,
        topRight: this.isSelf ? AppTheme.chatBubbleTipRadius : AppTheme.chatBubbleRadius,
        bottomLeft: AppTheme.chatBubbleRadius,
        bottomRight: AppTheme.chatBubbleRadius
      })
      .backgroundColor(this.playing ? AppTheme.activeBg : (this.isSelf ? AppTheme.primary : AppTheme.card))
      .border(this.isSelf ? {} : { width: 1, color: AppTheme.chatBorderFaint })
      .onClick(() => {
        this.togglePlay();
      })
      if (this.getShowStatus()) {
        Text(this.getStatusText())
          .fontSize(11)
          .fontColor(this.message.status === 4 ? AppTheme.down : AppTheme.textSecondary)
      }
    }
    .alignItems(this.isSelf ? HorizontalAlign.End : HorizontalAlign.Start)
    .width('100%')
  }

  /** 播放/停止语音（全局单播放器；停止回调复位图标） */
  private togglePlay(): void {
    const url = this.getRichContent().playbackUrl.length > 0
      ? this.getRichContent().playbackUrl
      : this.getRichContent().mediaUrl;
    if (url.length === 0) {
      return;
    }
    this.playing = true;
    this.voicePlayer.togglePlay(url, () => {
      this.playing = false;
    }).catch(() => {
      this.playing = false;
    });
  }

  /** 位置消息卡片（📍 + 地点名 + 地址/坐标；点击复制坐标） */
  @Builder
  private LocationCard() {
    Column({ space: 6 }) {
      Row({ space: 10 }) {
        Text('📍')
          .fontSize(28)
        Column({ space: 4 }) {
          Text(this.getRichContent().title.length > 0 ? this.getRichContent().title : '位置')
            .fontSize(15)
            .fontWeight(600)
            .fontColor(this.isSelf ? '#ffffff' : AppTheme.text)
            .maxLines(1)
            .textOverflow({ overflow: TextOverflow.Ellipsis })
          Text(this.getRichContent().subtitle.length > 0
            ? this.getRichContent().subtitle
            : this.getRichContent().originalUrl.replace('geo:', '坐标: '))
            .fontSize(12)
            .fontColor(this.isSelf ? 'rgba(255,255,255,0.7)' : AppTheme.textSecondary)
            .maxLines(2)
            .textOverflow({ overflow: TextOverflow.Ellipsis })
        }
        .alignItems(HorizontalAlign.Start)
        .layoutWeight(1)
      }
      .width('100%')
      .padding({ left: 14, right: 14, top: 12, bottom: 12 })
      .constraintSize({ maxWidth: '75%' })
      .borderRadius({
        topLeft: this.isSelf ? AppTheme.chatBubbleRadius : AppTheme.chatBubbleTipRadius,
        topRight: this.isSelf ? AppTheme.chatBubbleTipRadius : AppTheme.chatBubbleRadius,
        bottomLeft: AppTheme.chatBubbleRadius,
        bottomRight: AppTheme.chatBubbleRadius
      })
      .backgroundColor(this.isSelf ? 'rgba(0,0,0,0.2)' : AppTheme.card)
      .border({ width: 1, color: this.isSelf ? 'rgba(255,255,255,0.1)' : AppTheme.chatBorderFaint })
      .onClick(() => {
        // 复制坐标文本，便于粘贴到地图 App 检索
        const loc = this.getRichContent().getLocation();
        if (loc !== null) {
          const text = `${loc[0]},${loc[1]}`;
          pasteboard.getSystemPasteboard().setData(
            pasteboard.createData(pasteboard.MIMETYPE_TEXT_PLAIN, text)).catch(() => {
          });
        }
      })
      if (this.getShowStatus()) {
        Text(this.getStatusText())
          .fontSize(11)
          .fontColor(this.message.status === 4 ? AppTheme.down : AppTheme.textSecondary)
      }
    }
    .alignItems(this.isSelf ? HorizontalAlign.End : HorizontalAlign.Start)
    .width('100%')
  }

  @Builder
  private TextBubble() {
    Column({ space: 3 }) {
      if (this.getRichContent().isEmoji && this.getRichContent().text && this.getRichContent().text.length > 0) {
        Text(this.getRichContent().text)
          .fontSize(32)
          .padding({ left: 8, right: 8, top: 4, bottom: 4 })
      } else {
        Text(this.getRichContent().text)
          .fontSize(14)
          .lineHeight(22)
          .fontColor(this.isSelf ? '#ffffff' : AppTheme.text)
          .padding({ left: 14, right: 14, top: 10, bottom: 10 })
          .constraintSize({ maxWidth: '75%' })
          .borderRadius({
            topLeft: this.isSelf ? AppTheme.chatBubbleRadius : AppTheme.chatBubbleTipRadius,
            topRight: this.isSelf ? AppTheme.chatBubbleTipRadius : AppTheme.chatBubbleRadius,
            bottomLeft: AppTheme.chatBubbleRadius,
            bottomRight: AppTheme.chatBubbleRadius
          })
          .backgroundColor(this.isSelf ? AppTheme.primary : AppTheme.card)
          .border(this.isSelf ? {} : { width: 1, color: AppTheme.chatBorderFaint })
      }
      if (this.getShowStatus()) {
        Text(this.getStatusText())
          .fontSize(11)
          .fontColor(this.message.status === 4 ? AppTheme.down : AppTheme.textSecondary)
      }
    }
    .alignItems(this.isSelf ? HorizontalAlign.End : HorizontalAlign.Start)
    .width('100%')
  }

  @Builder
  private ImageCard() {
    Column({ space: 4 }) {
      Column() {
        if (this.imgSrc.length > 0) {
          // 远程图已下载至沙箱缓存（自签 https 不能直连 Image 组件）
          // draggable(false)：禁用图片拖拽手势，长按才能冒泡到外层消息菜单（撤回/删除）
          Image('file://' + this.imgSrc)
            .width(160)
            .height(120)
            .objectFit(ImageFit.Cover)
            .borderRadius(8)
            .draggable(false)
        } else if (this.getRichContent().mediaUrl && this.getRichContent().mediaUrl.length > 0
          && !this.getRichContent().mediaUrl.startsWith('http')) {
          // 非网络地址（本地路径/占位）直接渲染
          Image(this.getRichContent().mediaUrl)
            .width(160)
            .height(120)
            .objectFit(ImageFit.Cover)
            .borderRadius(8)
            .draggable(false)
        } else {
          Column() {
            Text('🖼️')
              .fontSize(28)
            Text('图片')
              .fontSize(12)
              .fontColor(AppTheme.textSecondary)
              .margin({ top: 4 })
          }
          .width(120)
          .height(100)
          .justifyContent(FlexAlign.Center)
          .borderRadius(8)
          .backgroundColor(AppTheme.card)
        }
      }
      .constraintSize({ maxWidth: '75%' })
      // 点击查看大图（已下载到沙箱的本地路径）
      .onClick(() => {
        if (this.imgSrc.length > 0) {
          this.onImageTap(this.imgSrc);
        } else {
          this.getUIContext().getPromptAction().showToast({ message: '图片加载中，请稍候', duration: 1500 });
        }
      })
      if (this.getRichContent().text && this.getRichContent().text.length > 0) {
        Text(this.getRichContent().text)
          .fontSize(13)
          .fontColor(this.isSelf ? '#ffffff' : AppTheme.text)
          .padding({ left: 4, right: 4 })
      }
      if (this.getShowStatus()) {
        Text(this.getStatusText())
          .fontSize(11)
          .fontColor(this.message.status === 4 ? AppTheme.down : AppTheme.textSecondary)
      }
    }
    .alignItems(this.isSelf ? HorizontalAlign.End : HorizontalAlign.Start)
    .width('100%')
  }

  @Builder
  private VideoCard() {
    Column({ space: 4 }) {
      Column() {
        if (this.getRichContent().previewImageUrl && this.getRichContent().previewImageUrl.length > 0) {
          Image(this.getRichContent().previewImageUrl)
            .width(160)
            .height(100)
            .objectFit(ImageFit.Cover)
            .borderRadius(8)
        } else {
          Column() {
            Text('🎬')
              .fontSize(28)
            Text('视频')
              .fontSize(12)
              .fontColor(AppTheme.textSecondary)
              .margin({ top: 4 })
          }
          .width(140)
          .height(90)
          .justifyContent(FlexAlign.Center)
          .borderRadius(8)
          .backgroundColor(AppTheme.card)
        }
      }
      .constraintSize({ maxWidth: '75%' })
      if (this.getRichContent().text && this.getRichContent().text.length > 0) {
        Text(this.getRichContent().text)
          .fontSize(13)
          .fontColor(this.isSelf ? '#ffffff' : AppTheme.text)
          .padding({ left: 4, right: 4 })
      }
      if (this.getShowStatus()) {
        Text(this.getStatusText())
          .fontSize(11)
          .fontColor(this.message.status === 4 ? AppTheme.down : AppTheme.textSecondary)
      }
    }
    .alignItems(this.isSelf ? HorizontalAlign.End : HorizontalAlign.Start)
    .width('100%')
  }

  @Builder
  private LinkCard() {
    Column({ space: 6 }) {
      Column({ space: 4 }) {
        if (this.getRichContent().title && this.getRichContent().title.length > 0) {
          Text(this.getRichContent().title)
            .fontSize(14)
            .fontWeight(600)
            .fontColor(this.isSelf ? '#ffffff' : AppTheme.text)
            .maxLines(1)
            .textOverflow({ overflow: TextOverflow.Ellipsis })
        }
        if (this.getRichContent().subtitle && this.getRichContent().subtitle.length > 0) {
          Text(this.getRichContent().subtitle)
            .fontSize(12)
            .fontColor(this.isSelf ? 'rgba(255,255,255,0.7)' : AppTheme.textSecondary)
            .maxLines(2)
            .textOverflow({ overflow: TextOverflow.Ellipsis })
        }
        Row({ space: 4 }) {
          Text(this.getRichContent().siteName && this.getRichContent().siteName.length > 0 ? this.getRichContent().siteName : '链接')
            .fontSize(11)
            .fontColor(this.isSelf ? 'rgba(255,255,255,0.5)' : AppTheme.textSecondary)
        }
      }
      .padding({ left: 12, right: 12, top: 10, bottom: 10 })
      .constraintSize({ maxWidth: '75%' })
      .borderRadius({
        topLeft: this.isSelf ? AppTheme.chatBubbleRadius : AppTheme.chatBubbleTipRadius,
        topRight: this.isSelf ? AppTheme.chatBubbleTipRadius : AppTheme.chatBubbleRadius,
        bottomLeft: AppTheme.chatBubbleRadius,
        bottomRight: AppTheme.chatBubbleRadius
      })
      .backgroundColor(this.isSelf ? 'rgba(0,0,0,0.2)' : AppTheme.card)
      .border({ width: 1, color: this.isSelf ? 'rgba(255,255,255,0.1)' : AppTheme.chatBorderFaint })
      if (this.getShowStatus()) {
        Text(this.getStatusText())
          .fontSize(11)
          .fontColor(this.message.status === 4 ? AppTheme.down : AppTheme.textSecondary)
      }
    }
    .alignItems(this.isSelf ? HorizontalAlign.End : HorizontalAlign.Start)
    .width('100%')
  }

  @Builder
  private FileCard() {
    Row({ space: 10 }) {
      Text('📄')
        .fontSize(28)
      Column({ space: 4 }) {
        Text(this.getRichContent().fileName && this.getRichContent().fileName.length > 0 ? this.getRichContent().fileName : '未知文件')
          .fontSize(14)
          .fontWeight(500)
          .fontColor(this.isSelf ? '#ffffff' : AppTheme.text)
          .maxLines(1)
          .textOverflow({ overflow: TextOverflow.Ellipsis })
        Text(this.fileSizeText())
          .fontSize(12)
          .fontColor(this.isSelf ? 'rgba(255,255,255,0.6)' : AppTheme.textSecondary)
      }
      .alignItems(HorizontalAlign.Start)
      .layoutWeight(1)
    }
    .padding({ left: 14, right: 14, top: 10, bottom: 10 })
    .constraintSize({ maxWidth: '75%' })
    .borderRadius({
      topLeft: this.isSelf ? AppTheme.chatBubbleRadius : AppTheme.chatBubbleTipRadius,
      topRight: this.isSelf ? AppTheme.chatBubbleTipRadius : AppTheme.chatBubbleRadius,
      bottomLeft: AppTheme.chatBubbleRadius,
      bottomRight: AppTheme.chatBubbleRadius
    })
    .backgroundColor(this.isSelf ? 'rgba(0,0,0,0.2)' : AppTheme.card)
    .border({ width: 1, color: this.isSelf ? 'rgba(255,255,255,0.1)' : AppTheme.chatBorderFaint })
    .margin({ bottom: 4 })
    // 点击：远程文件先下载沙箱，再调系统文件预览打开（按类型匹配预览/编辑程序）
    .onClick(() => {
      this.openSharedFile();
    })
  }

  /** 文件大小显示：>0 显示 KB/MB；未知显示文案 */
  private fileSizeText(): string {
    const size = this.getRichContent().fileSize;
    if (size <= 0) {
      return '点击打开';
    }
    if (size < 1024 * 1024) {
      return `${(size / 1024).toFixed(1)} KB · 点击打开`;
    }
    return `${(size / 1024 / 1024).toFixed(1)} MB · 点击打开`;
  }

  /** 打开共享文件：远程下载到沙箱缓存（带原始扩展名）→ filePreview 系统预览 */
  private async openSharedFile(): Promise<void> {
    const rich = this.getRichContent();
    const url = rich.mediaUrl;
    if (url.length === 0) {
      this.getUIContext().getPromptAction().showToast({ message: '文件地址无效', duration: 2000 });
      return;
    }
    const prompt = this.getUIContext().getPromptAction();
    try {
      let localPath = url;
      if (url.startsWith('http')) {
        prompt.showToast({ message: '正在获取文件…', duration: 1500 });
        localPath = await ApiClient.getInstance().downloadSharedFileAsync(url, rich.fileName);
      } else {
        // 非 http 路径：发送端未上传成功的历史消息（仅对方本机可见），无法跨端获取
        prompt.showToast({ message: '该文件未上传到服务器，请联系发送方重新发送', duration: 2500 });
        return;
      }
      if (!fs.accessSync(localPath)) {
        prompt.showToast({ message: '本地文件不存在', duration: 2000 });
        return;
      }
      const ctx = AppContext.getInstance().context as common.UIAbilityContext;
      const fileUriStr = fileUri.getUriFromPath(localPath);
      const mimeType = this.mimeTypeOf(localPath);
      // 优先系统文件预览服务（文档/表格/PDF 等原生预览）；
      // 预览服务不可用（模拟器/裁剪固件，报 Failed on connect to the server 等）时
      // 回退隐式 Want 查找设备上已安装的可打开应用，均失败才提示用户。
      try {
        const info: filePreview.PreviewInfo = {
          title: rich.fileName,
          uri: fileUriStr,
          mimeType: mimeType
        };
        await filePreview.openPreview(ctx, info);
      } catch (previewErr) {
        hilog.warn(0x0001, TAG, '文件预览服务不可用，回退隐式打开: %{public}s', (previewErr as Error).message);
        await this.openFileWithWant(ctx, fileUriStr, mimeType, rich.fileName);
      }
    } catch (e) {
      hilog.warn(0x0001, TAG, '打开文件失败: %{public}s', (e as Error).message);
      prompt.showToast({ message: '打开文件失败：' + (e as Error).message, duration: 2500 });
    }
  }

  /** 隐式 Want 打开（ACTION_VIEW_DATA）：系统按 mimeType 匹配已安装应用（WPS/查看器等） */
  private async openFileWithWant(ctx: common.UIAbilityContext, uri: string, mimeType: string, fileName: string): Promise<void> {
    try {
      const want: Want = {
        action: 'ohos.want.action.viewData',
        uri: uri,
        type: mimeType
      };
      await ctx.startAbility(want);
    } catch (wantErr) {
      const msg = (wantErr as Error).message;
      hilog.warn(0x0001, TAG, '隐式打开失败（无匹配应用）: %{public}s', msg);
      this.getUIContext().getPromptAction().showToast({
        message: `暂无应用可打开 "${fileName}"（${mimeType}）`,
        duration: 3000
      });
    }
  }

  /** 扩展名 → mimeType（系统预览按此匹配打开方式） */
  private mimeTypeOf(path: string): string {
    const dot = path.lastIndexOf('.');
    const ext = dot >= 0 ? path.slice(dot).toLowerCase() : '';
    const map: Record<string, string> = {
      '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.png': 'image/png', '.gif': 'image/gif',
      '.webp': 'image/webp', '.bmp': 'image/bmp', '.svg': 'image/svg+xml',
      '.mp4': 'video/mp4', '.mov': 'video/quicktime', '.avi': 'video/x-msvideo', '.mkv': 'video/x-matroska',
      '.mp3': 'audio/mpeg', '.m4a': 'audio/mp4', '.wav': 'audio/wav', '.flac': 'audio/flac',
      '.pdf': 'application/pdf',
      '.txt': 'text/plain', '.md': 'text/markdown', '.log': 'text/plain', '.csv': 'text/csv',
      '.doc': 'application/msword', '.docx': 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      '.xls': 'application/vnd.ms-excel', '.xlsx': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      '.ppt': 'application/vnd.ms-powerpoint', '.pptx': 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
      '.zip': 'application/zip', '.rar': 'application/vnd.rar', '.7z': 'application/x-7z-compressed',
      '.json': 'application/json', '.xml': 'application/xml', '.html': 'text/html'
    };
    return map[ext] ?? 'application/octet-stream';
  }
}

/** 正在输入指示：三点相位差跳动 */
@Component
struct TypingDots {
  @State tick: number = 0;
  private timer: number = -1;

  aboutToAppear(): void {
    this.timer = setInterval(() => {
      this.tick += 1;
    }, 90);
  }

  aboutToDisappear(): void {
    if (this.timer >= 0) {
      clearInterval(this.timer);
      this.timer = -1;
    }
  }

  private dotOffset(i: number): number {
    const phase = this.tick * 0.35 - i * 1.05;
    return -3 * Math.max(0, Math.sin(phase));
  }

  build() {
    Row({ space: 4 }) {
      ForEach([0, 1, 2], (i: number) => {
        Circle({ width: 7, height: 7 })
          .fill(AppTheme.textSecondary)
          .translate({ y: this.dotOffset(i) })
          .animation({ duration: 90, curve: Curve.EaseInOut })
      }, (i: number) => `dot-${i}`)
    }
    .height(14)
    .alignItems(VerticalAlign.Center)
  }
}

/** 表情面板：分类 tab + 8 列网格；输入框有内容时点按追加，为空时作为大表情直接发送 */
@Component
struct EmojiPanel {
    onInsert: (emoji: string) => void = (_e: string) => {
    };
    onDelete: () => void = () => {
    };
    @State activeCategory: number = 0;

    build() {
        Column() {
            // 分类 tab
            Scroll() {
                Row({ space: 8 }) {
                    ForEach(EMOJI_CATEGORIES, (cat: EmojiCategory, index: number) => {
                        Text(cat.name)
                            .fontSize(13)
                            .fontColor(this.activeCategory === index ? AppTheme.primary : AppTheme.textSecondary)
                            .padding({ left: 14, right: 14, top: 6, bottom: 6 })
                            .borderRadius(14)
                            .backgroundColor(this.activeCategory === index ? AppTheme.activeBg : Color.Transparent)
                            .onClick(() => {
                                this.activeCategory = index;
                            })
                    }, (cat: EmojiCategory) => cat.name)
                }
                .padding({ left: 12, right: 12 })
            }
            .scrollable(ScrollDirection.Horizontal)
            .scrollBar(BarState.Off)
            .width('100%')
            .margin({ top: 10, bottom: 6 })

            // 表情网格
            Grid() {
                ForEach(EMOJI_CATEGORIES[this.activeCategory].emojis, (emoji: string, index: number) => {
                    GridItem() {
                        Text(emoji)
                            .fontSize(26)
                            .width('100%')
                            .height('100%')
                            .textAlign(TextAlign.Center)
                            .onClick(() => {
                                this.onInsert(emoji);
                            })
                    }
                }, (emoji: string, index: number) => `${emoji}-${index}`)
            }
            .columnsTemplate('1fr 1fr 1fr 1fr 1fr 1fr 1fr 1fr')
            .rowsGap(4)
            .columnsGap(0)
            .height(168)
            .width('100%')
            .padding({ left: 10, right: 10, top: 4, bottom: 4 })
            .scrollBar(BarState.Off)

            // 删除键
            Row() {
                Button() {
                    SymbolGlyph($r('sys.symbol.delete_left'))
                        .fontSize(20)
                        .fontColor([AppTheme.textSecondary])
                }
                .width(44)
                .height(36)
                .borderRadius(12)
                .backgroundColor('rgba(255,255,255,0.08)')
                .padding(0)
                .onClick(() => {
                    this.onDelete();
                })
            }
            .width('100%')
            .justifyContent(FlexAlign.End)
            .padding({ left: 12, right: 12, bottom: 10 })
        }
        .width('100%')
        .backgroundColor(AppTheme.chatInputBg)
        .border({ width: { top: 1 }, color: AppTheme.chatBorderFaint })
    }
}

/** "+"扩展面板条目 */
class MorePanelItem {
  label: string = '';
  icon: string = '';
  /** 动作标识：call_audio / call_video 可用，其余占位提示 */
  action: string = '';

  constructor(label: string, icon: string, action: string) {
    this.label = label;
    this.icon = icon;
    this.action = action;
  }
}

/** "+"扩展面板：4 列网格（对标微信加号面板；音视频通话可用，其余为占位入口） */
@Component
struct MorePanel {
  onAction: (action: string) => void = (_a: string) => {
  };

  build() {
    Column() {
      Grid() {
        ForEach([
          new MorePanelItem('相册', '🖼', 'album'),
          new MorePanelItem('拍摄', '📷', 'camera'),
          new MorePanelItem('视频通话', '🎥', 'call_video'),
          new MorePanelItem('语音通话', '📞', 'call_audio'),
          new MorePanelItem('位置', '📍', 'location'),
          new MorePanelItem('红包', '🧧', 'redpacket'),
          new MorePanelItem('转账', '💸', 'transfer'),
          new MorePanelItem('我的收藏', '⭐', 'favorite')
        ], (item: MorePanelItem) => {
          GridItem() {
            Column({ space: 6 }) {
              Text(item.icon)
                .fontSize(26)
                .width(52)
                .height(52)
                .textAlign(TextAlign.Center)
                .borderRadius(13)
                .backgroundColor(AppTheme.card)
              Text(item.label)
                .fontSize(11)
                .fontColor(AppTheme.textSecondary)
            }
            .justifyContent(FlexAlign.Center)
            .onClick(() => {
              this.onAction(item.action);
            })
          }
        }, (item: MorePanelItem) => item.action)
      }
      .columnsTemplate('1fr 1fr 1fr 1fr')
      .rowsGap(14)
      .columnsGap(0)
      .height(190)
      .width('100%')
      .padding({ left: 22, right: 22, top: 16 })
      .scrollBar(BarState.Off)
    }
    .width('100%')
    .backgroundColor(AppTheme.chatInputBg)
    .border({ width: { top: 1 }, color: AppTheme.chatBorderFaint })
  }
}

@Entry
@Component
struct ChatDetailPage {
  vm: ImViewModel = ImViewModel.getInstance();
  private callMgr: CallSessionManager = CallSessionManager.getInstance();
  @State inputText: string = '';
  @State target: ChatTarget = new ChatTarget();
  @State pressBack: boolean = false;
  @State pressMore: boolean = false;
  /** 表情面板展开 */
  @State showEmojiPanel: boolean = false;
  /** "+"扩展面板展开 */
  @State showMorePanel: boolean = false;
  /** 长按消息菜单目标（null = 菜单关闭） */
  @State msgMenuTarget: ChatMessageItem | null = null;
  /** 全屏图片查看器：当前展示的沙箱图片路径（空=关闭） */
  @State viewerImgPath: string = '';
  /** 待确认发送的位置（null = 确认面板关闭） */
  @State pendingLocation: PendingLocation | null = null;
  // ===== 语音消息 =====
  /** 语音/键盘输入模式切换（对标微信左下角切换键） */
  @State voiceMode: boolean = false;
  /** 录音中 */
  @State recording: boolean = false;
  /** 录音启动中（权限弹窗 + AVRecorder 初始化耗时期间为 true；此阶段松手需丢弃录音） */
  @State recordStarting: boolean = false;
  /** 录音计时（秒） */
  @State recordSeconds: number = 0;
  /** 上滑进入取消区 */
  @State recordWillCancel: boolean = false;
  /** 启动期间收到松手/取消（启动完成后丢弃本次录音） */
  private recordAbortRequested: boolean = false;
  private voiceRecorder: VoiceRecorder = new VoiceRecorder();
  private recordTimer: number = -1;
  /** 按下时的触点 Y（上滑取消判定基准） */
  private recordTouchStartY: number = 0;
  /** 消息列表状态镜像：vm.messages 非响应式，必须通过 @State 镜像驱动 UI 刷新 */
  @State uiMessages: ChatMessageItem[] = [];
  @State uiPeerTyping: boolean = false;
  /** 富文本缓存：避免重复解析 */
  private richContentCache: Map<string, RichMessageContent> = new Map();
  private scroller: Scroller = new Scroller();

  /** 发起通话（仅私聊） */
  private startCall(callType: number): void {
    if (this.target.isGroup || this.target.peerId <= 0) {
      return;
    }
    if (this.callMgr.busy) {
      this.getUIContext().getPromptAction().showToast({ message: '当前已有通话进行中', duration: 2000 });
      return;
    }
    this.callMgr.startCall(this.target.peerId, this.target.displayName, '', callType);
  }

  /** "+"扩展面板动作分发 */
  private onMoreAction(action: string): void {
    if (action === 'call_audio') {
      this.startCall(ImCallType.Audio);
      this.showMorePanel = false;
    } else if (action === 'call_video') {
      this.startCall(ImCallType.Video);
      this.showMorePanel = false;
    } else if (action === 'album') {
      this.pickImageFromAlbum();
    } else if (action === 'camera') {
      this.capturePhoto();
    } else if (action === 'location') {
      this.openLocationPicker();
    } else {
      this.getUIContext().getPromptAction().showToast({ message: '该功能即将开放', duration: 1500 });
    }
  }

  // ===== 相册 / 拍摄：选图 → 拷贝沙箱 → 上传 WebApi → 发送图片消息 =====

  /** 相册选图发送（系统 PhotoViewPicker，无需相册权限） */
  private async pickImageFromAlbum(): Promise<void> {
    this.showMorePanel = false;
    try {
      const options = new photoAccessHelper.PhotoSelectOptions();
      options.MIMEType = photoAccessHelper.PhotoViewMIMETypes.IMAGE_TYPE;
      options.maxSelectNumber = 1;
      const picker = new photoAccessHelper.PhotoViewPicker();
      const result = await picker.select(options);
      if (result.photoUris && result.photoUris.length > 0) {
        await this.sendImageFromUri(result.photoUris[0]);
      }
    } catch (e) {
      hilog.warn(0x0001, TAG, '相册选图取消/失败: %{public}s', (e as Error).message);
    }
  }

  /** 拍摄照片发送（系统相机选择器） */
  private async capturePhoto(): Promise<void> {
    this.showMorePanel = false;
    try {
      const ctx = this.getUIContext().getHostContext() as common.UIAbilityContext;
      const profile: cameraPicker.PickerProfile = { cameraPosition: camera.CameraPosition.CAMERA_POSITION_BACK };
      const result = await cameraPicker.pick(ctx, [cameraPicker.PickerMediaType.PHOTO], profile);
      if (result.resultCode === 0 && result.resultUri.length > 0) {
        await this.sendImageFromUri(result.resultUri);
      }
    } catch (e) {
      hilog.warn(0x0001, TAG, '拍摄取消/失败: %{public}s', (e as Error).message);
    }
  }

  /** 从媒体 uri 提取带扩展名的上传文件名（相册/相机 uri 内嵌原始扩展名） */
  private mediaFileNameOf(uri: string): string {
    const match = uri.match(/\.(jpg|jpeg|png|webp|gif)/i);
    return match !== null ? `img_${Date.now()}${match[0].toLowerCase()}` : `img_${Date.now()}.jpg`;
  }

  /** 媒体 uri（file://media/...）→ 沙箱缓存文件（fd 拷贝） */
  private copyMediaToCache(uri: string): string {
    const ctx = AppContext.getInstance().context;
    const destPath = `${ctx?.cacheDir ?? ''}/im_send_${Date.now()}.img`;
    const src = fs.openSync(uri, fs.OpenMode.READ_ONLY);
    try {
      const dest = fs.openSync(destPath, fs.OpenMode.READ_WRITE | fs.OpenMode.CREATE | fs.OpenMode.TRUNC);
      try {
        fs.copyFileSync(src.fd, dest.fd);
      } finally {
        fs.closeSync(dest);
      }
      return destPath;
    } finally {
      fs.closeSync(src);
    }
  }

  /** 发送图片消息：拷贝 → 上传 → Image 消息（富文本 JSON，mediaUrl 为服务端完整 URL） */
  private async sendImageFromUri(uri: string): Promise<void> {
    this.getUIContext().getPromptAction().showToast({ message: '正在发送图片…', duration: 1500 });
    let tempPath = '';
    try {
      tempPath = this.copyMediaToCache(uri);
      const remoteUrl = await ApiClient.getInstance().uploadImMediaAsync(tempPath, this.mediaFileNameOf(uri));
      const rich = RichMessageContent.createImage(remoteUrl);
      await this.vm.sendMessage(RichMessageContentSerializer.serialize(rich), ImContentType.Image);
      this.syncUiState();
      this.scrollToBottom(120);
    } catch (e) {
      this.getUIContext().getPromptAction().showToast({
        message: (e as Error).message || '图片发送失败，请检查网络', duration: 2500
      });
    } finally {
      // 清理沙箱临时文件（上传失败也不残留）
      if (tempPath.length > 0) {
        try {
          fs.unlinkSync(tempPath);
        } catch (_e) {
          // 忽略清理失败
        }
      }
    }
  }

  // ===== 位置消息：申请定位权限 → 取当前位置 → 反向地理编码 → 确认面板 → 发送 =====

  /** 申请精确定位权限（LOCATION + APPROXIMATELY_LOCATION 需同时授予） */
  private async ensureLocationPermission(): Promise<boolean> {
    try {
      const ctx = this.getUIContext().getHostContext() as common.UIAbilityContext;
      const atManager = abilityAccessCtrl.createAtManager();
      const perms: Array<Permissions> = ['ohos.permission.LOCATION', 'ohos.permission.APPROXIMATELY_LOCATION'];
      const result = await atManager.requestPermissionsFromUser(ctx, perms);
      return result.authResults.length > 0 && result.authResults.every((r: number) => r === 0);
    } catch (e) {
      hilog.warn(0x0001, TAG, '定位权限申请失败: %{public}s', (e as Error).message);
      return false;
    }
  }

  /** 逆地编码组合地址（失败返回空串，由调用方回退坐标显示） */
  private async reverseGeocode(lat: number, lng: number): Promise<string> {
    try {
      const req: geoLocationManager.ReverseGeoCodeRequest = { latitude: lat, longitude: lng, maxItems: 1 };
      const addrs = await geoLocationManager.getAddressesFromLocation(req);
      if (addrs && addrs.length > 0) {
        const a = addrs[0];
        const parts: string[] = [];
        if (a.countryName) {
          parts.push(a.countryName);
        }
        if (a.administrativeArea) {
          parts.push(a.administrativeArea);
        }
        if (a.locality) {
          parts.push(a.locality);
        }
        if (a.subLocality) {
          parts.push(a.subLocality);
        }
        if (a.roadName) {
          parts.push(a.roadName);
        }
        return parts.join('');
      }
    } catch (_e) {
      // 反向地理编码不可用（无网/服务未支持）时回退坐标
    }
    return '';
  }

  /** 打开位置发送流程（获取当前位置并弹确认面板） */
  private async openLocationPicker(): Promise<void> {
    this.showMorePanel = false;
    const granted = await this.ensureLocationPermission();
    if (!granted) {
      this.getUIContext().getPromptAction().showToast({ message: '需要定位权限才能发送位置', duration: 2500 });
      return;
    }
    this.getUIContext().getPromptAction().showToast({ message: '正在获取位置…', duration: 1500 });
    try {
      const req: geoLocationManager.CurrentLocationRequest = { timeoutMs: 10000 };
      const loc = await geoLocationManager.getCurrentLocation(req);
      const address = await this.reverseGeocode(loc.latitude, loc.longitude);
      const pending = new PendingLocation();
      pending.lat = loc.latitude;
      pending.lng = loc.longitude;
      pending.name = '我的位置';
      pending.address = address.length > 0
        ? address
        : `纬度 ${loc.latitude.toFixed(6)} / 经度 ${loc.longitude.toFixed(6)}`;
      this.pendingLocation = pending;
    } catch (e) {
      this.getUIContext().getPromptAction().showToast({
        message: (e as Error).message || '获取位置失败，请检查系统定位服务', duration: 2500
      });
    }
  }

  /** 确认发送位置消息 */
  private async sendPendingLocation(): Promise<void> {
    const loc = this.pendingLocation;
    this.pendingLocation = null;
    if (loc === null) {
      return;
    }
    try {
      const rich = RichMessageContent.createLocation(loc.lat, loc.lng, loc.name, loc.address);
      await this.vm.sendMessage(RichMessageContentSerializer.serialize(rich), ImContentType.Location);
      this.syncUiState();
      this.scrollToBottom(120);
    } catch (e) {
      this.getUIContext().getPromptAction().showToast({
        message: (e as Error).message || '位置发送失败', duration: 2500
      });
    }
  }

  // ===== 语音消息：按住说话（松开发送 / 上滑取消 / 60s 上限自动发送） =====

  /** 确保麦克风权限（启动时已申请过；用户曾拒绝则再次弹窗） */
  private async ensureMicrophonePermission(): Promise<boolean> {
    try {
      const ctx = this.getUIContext().getHostContext() as common.UIAbilityContext;
      const atManager = abilityAccessCtrl.createAtManager();
      const perms: Array<Permissions> = ['ohos.permission.MICROPHONE'];
      const result = await atManager.requestPermissionsFromUser(ctx, perms);
      return result.authResults.length > 0 && result.authResults[0] === 0;
    } catch (e) {
      hilog.warn(0x0001, TAG, '麦克风权限申请失败: %{public}s', (e as Error).message);
      return false;
    }
  }

  /** 按下：开始录音（权限申请与 AVRecorder 初始化为异步，期间松手经 recordAbortRequested 丢弃） */
  private async startVoiceRecord(): Promise<void> {
    if (this.recording || this.recordStarting) {
      return;
    }
    this.recordAbortRequested = false;
    this.recordStarting = true;
    const granted = await this.ensureMicrophonePermission();
    if (!granted) {
      this.recordStarting = false;
      this.getUIContext().getPromptAction().showToast({ message: '需要麦克风权限才能发送语音', duration: 2500 });
      return;
    }
    const ok = await this.voiceRecorder.start();
    this.recordStarting = false;
    if (!ok) {
      this.getUIContext().getPromptAction().showToast({ message: '录音启动失败，请重试', duration: 2000 });
      return;
    }
    if (this.recordAbortRequested) {
      // 启动期间用户已松手：丢弃本次录音（否则会悬挂录音占用麦克风）
      await this.voiceRecorder.cancel();
      return;
    }
    this.recording = true;
    this.recordWillCancel = false;
    this.recordSeconds = 0;
    this.recordTimer = setInterval(() => {
      this.recordSeconds += 1;
      if (this.recordSeconds >= MAX_VOICE_SECONDS) {
        // 达到上限自动作为正常录音发送
        this.endVoiceRecord(false);
      }
    }, 1000);
  }

  /** 触点移动：上滑超过阈值进入取消区 */
  private moveVoiceRecord(y: number): void {
    if (!this.recording) {
      return;
    }
    this.recordWillCancel = (this.recordTouchStartY - y) > 80;
  }

  /** 松开：结束录音并发送（willCancel=true 丢弃）；启动阶段松手则标记丢弃 */
  private async endVoiceRecord(willCancel: boolean): Promise<void> {
    if (!this.recording) {
      if (this.recordStarting) {
        // 录音仍在启动中：等 startVoiceRecord 完成后按标记丢弃
        this.recordAbortRequested = true;
      }
      return;
    }
    this.recording = false;
    if (this.recordTimer >= 0) {
      clearInterval(this.recordTimer);
      this.recordTimer = -1;
    }
    if (willCancel) {
      await this.voiceRecorder.cancel();
      this.recordWillCancel = false;
      return;
    }
    const result: VoiceRecordResult | null = await this.voiceRecorder.stop();
    if (result === null) {
      this.getUIContext().getPromptAction().showToast({ message: '说话时间太短', duration: 1500 });
      return;
    }
    await this.sendVoiceMessage(result);
  }

  /** 上传录音并发送语音消息 */
  private async sendVoiceMessage(result: VoiceRecordResult): Promise<void> {
    try {
      const fileName = `voice_${Date.now()}.m4a`;
      const remoteUrl = await ApiClient.getInstance().uploadImMediaAsync(result.filePath, fileName);
      const rich = RichMessageContent.createAudio(remoteUrl, result.durationSeconds);
      await this.vm.sendMessage(RichMessageContentSerializer.serialize(rich), ImContentType.Audio);
      this.syncUiState();
      this.scrollToBottom(120);
    } catch (e) {
      this.getUIContext().getPromptAction().showToast({
        message: (e as Error).message || '语音发送失败，请检查网络', duration: 2500
      });
    } finally {
      // 清理沙箱录音临时文件
      try {
        fs.unlinkSync(result.filePath);
      } catch (_e) {
        // 忽略清理失败
      }
    }
  }

  /** 长按消息菜单：复制（文本类消息） */
  private async copyMessageText(m: ChatMessageItem): Promise<void> {
    const text = this.getMessageDisplayText(m);
    if (text.length === 0) {
      return;
    }
    try {
      const data = pasteboard.createData(pasteboard.MIMETYPE_TEXT_PLAIN, text);
      await pasteboard.getSystemPasteboard().setData(data);
      this.getUIContext().getPromptAction().showToast({ message: '已复制', duration: 1200 });
    } catch (e) {
      hilog.warn(0x0001, TAG, '复制到剪贴板失败: %{public}s', (e as Error).message);
    }
  }

  /** 长按消息菜单动作分发 */
  private async doMsgMenuAction(action: string): Promise<void> {
    const target = this.msgMenuTarget;
    this.msgMenuTarget = null;
    if (target === null) {
      return;
    }
    try {
      if (action === 'copy') {
        await this.copyMessageText(target);
      } else if (action === 'delete') {
        await this.vm.deleteLocalMessage(target);
        this.syncUiState();
      } else if (action === 'recall') {
        // fire-and-forget：撤回生效通知经推送回写 UI（含对端与自己的其他设备）
        this.vm.recallMessage(target);
      }
    } catch (e) {
      hilog.warn(0x0001, TAG, '消息菜单操作失败: %{public}s', (e as Error).message);
    }
  }

  /** 消息是否可复制（文本/大表情类有纯文本） */
  private isCopyable(m: ChatMessageItem): boolean {
    return m.contentType === ImContentType.Text || m.contentType === ImContentType.Emoji;
  }

  /** 表情点按：输入框有内容时追加；为空时作为大表情直接发送 */
  private onEmojiInsert(emoji: string): void {
    if (this.inputText.trim().length > 0) {
      this.inputText = this.inputText + emoji;
    } else {
      // 大表情消息：content=表情本体，contentType=Emoji(7)，接收端反序列化后大字号展示
      this.vm.sendMessage(emoji, ImContentType.Emoji).then(() => {
        this.syncUiState();
        this.scrollToBottom(120);
      }).catch((e: Error) => {
        hilog.warn(0x0001, TAG, '大表情发送失败: %{public}s', e.message);
      });
    }
  }

  /** 表情删除键：移除末尾一个字符 */
  private onEmojiDelete(): void {
    if (this.inputText.length > 0) {
      this.inputText = this.inputText.slice(0, this.inputText.length - 1);
    }
  }

  /** 将 VM 数据同步到 @State 镜像（触发 UI 刷新） */
  private syncUiState(): void {
    this.uiMessages = this.vm.messages.slice();
    this.uiPeerTyping = this.vm.peerTyping;
  }

  aboutToAppear(): void {
    try {
      const params = this.getUIContext().getRouter().getParams() as Record<string, string>;
      if (params && params.conversationJson) {
        this.target = JSON.parse(params.conversationJson) as ChatTarget;
      }
    } catch (e) {
      hilog.warn(0x0001, TAG, '路由参数解析失败: %{public}s', (e as Error).message);
    }

    if (!this.target.conversationId || this.target.conversationId.length === 0) {
      hilog.warn(0x0001, TAG, '会话ID为空，返回列表');
      this.getUIContext().getRouter().back();
      return;
    }

    this.vm.onMessagesChanged = () => {
      this.syncUiState();
      this.scrollToBottom(120);
    };
    this.vm.start().then(() => {
      const item = new ConversationItem();
      item.conversationId = this.target.conversationId;
      item.peerId = this.target.peerId;
      item.displayName = this.target.displayName;
      item.isGroup = this.target.isGroup;
      this.vm.openConversation(item).then(() => {
        this.syncUiState();
        this.scrollToBottom(200);
      }).catch((e: Error) => {
        hilog.error(0x0001, TAG, '打开会话失败: %{public}s', e.message);
      });
    }).catch((e: Error) => {
      hilog.error(0x0001, TAG, 'VM启动失败: %{public}s', e.message);
    });
  }

  aboutToDisappear(): void {
    this.vm.onMessagesChanged = null;
    this.vm.onContactsChanged = null;
    this.vm.stop();
    this.richContentCache.clear();
    // 录音/播放中离开页面：丢弃录音、停止播放
    if (this.recording || this.recordStarting) {
      this.recording = false;
      this.recordAbortRequested = true;
      if (this.recordTimer >= 0) {
        clearInterval(this.recordTimer);
        this.recordTimer = -1;
      }
      this.voiceRecorder.cancel();
    }
    VoicePlayerManager.getInstance().stop();
  }

  /** 解析消息内容为富文本结构 */
  private parseRichContent(m: ChatMessageItem): RichMessageContent {
    if (!m) {
      return RichMessageContent.createText('');
    }
    try {
      const cacheKey = `${m.messageId || 'empty'}-${m.contentType}-${(m.content || '').length}`;
      const cached = this.richContentCache.get(cacheKey);
      if (cached) {
        return cached;
      }
      const parsed = RichMessageContentSerializer.deserialize(m.contentType, m.content || '');
      this.richContentCache.set(cacheKey, parsed);
      return parsed;
    } catch (e) {
      hilog.warn(0x0001, TAG, '富文本解析失败: %{public}s', (e as Error).message);
      return RichMessageContent.createText(m.content || '');
    }
  }

  /** 提取消息显示用的纯文本 */
  private getMessageDisplayText(m: ChatMessageItem): string {
    const rich = this.parseRichContent(m);
    return rich.getDisplayText();
  }

  /** 对方在线状态 */
  private peerOnline(): boolean {
    for (const c of this.vm.contacts) {
      if (c.userId === this.target.peerId) {
        return c.onlineStatus === 1;
      }
    }
    return false;
  }

  /** 导航栏副标题 */
  private statusText(): string {
    if (this.vm.peerTyping) {
      return '正在输入…';
    }
    if (this.target.isGroup) {
      return '群聊';
    }
    return this.peerOnline() ? '在线' : '离线';
  }

  /** 时间分隔文案 */
  private separatorText(ts: number): string {
    const d = new Date(ts);
    const now = new Date();
    const hh = d.getHours().toString().padStart(2, '0');
    const mm = d.getMinutes().toString().padStart(2, '0');
    if (d.toDateString() === now.toDateString()) {
      return `今天 ${hh}:${mm}`;
    }
    const yesterday = new Date(now);
    yesterday.setDate(yesterday.getDate() - 1);
    if (d.toDateString() === yesterday.toDateString()) {
      return `昨天 ${hh}:${mm}`;
    }
    return `${d.getMonth() + 1}月${d.getDate()}日 ${hh}:${mm}`;
  }

  /** 是否显示时间分隔 */
  private showSeparator(index: number): boolean {
    if (index === 0) {
      return true;
    }
    if (!this.uiMessages || index >= this.uiMessages.length) {
      return false;
    }
    const cur = this.uiMessages[index].timestamp;
    const prev = this.uiMessages[index - 1].timestamp;
    const curD = new Date(cur);
    const prevD = new Date(prev);
    return curD.toDateString() !== prevD.toDateString() || cur - prev > 5 * 60 * 1000;
  }

  private async doSend(): Promise<void> {
    const text = this.inputText.trim();
    if (text.length === 0) {
      return;
    }
    this.inputText = '';
    this.showEmojiPanel = false;
    this.showMorePanel = false;
    await this.vm.sendMessage(text);
    this.syncUiState();
    this.scrollToBottom(120);
  }

  private scrollToBottom(delayMs: number): void {
    setTimeout(() => {
      this.scroller.scrollEdge(Edge.Bottom);
    }, delayMs);
  }

  /** 单条消息（含时间分隔、双端头像、气泡、状态；长按弹出消息菜单；撤回态居中灰字） */
  @Builder
  messageItem(m: ChatMessageItem, index: number) {
    Column() {
      if (this.showSeparator(index)) {
        Text(this.separatorText(m.timestamp))
          .fontSize(11)
          .fontColor(AppTheme.textSecondary)
          .backgroundColor('rgba(255,255,255,0.04)')
          .padding({ left: 10, right: 10, top: 4, bottom: 4 })
          .borderRadius(12)
          .margin({ bottom: 10 })
      }

      if (m.status === 5) {
        // 已撤回：居中灰字系统提示（微信样式）
        Text(m.content.length > 0 ? m.content : '消息已撤回')
          .fontSize(11)
          .fontColor(AppTheme.textSecondary)
          .padding({ left: 10, right: 10, top: 4, bottom: 4 })
          .margin({ bottom: 4 })
      } else if (m.isSelf) {
        // 自己的消息：气泡居右 + 自己头像（微信布局）
        Row({ space: 8 }) {
          Column({ space: 3 }) {
            RichMessageRenderer({ message: m, isSelf: true })
          }
          .alignItems(HorizontalAlign.End)
          .layoutWeight(1)

          ChatAvatar({
            name: AppContext.getInstance().user.nickname,
            avatar: AppContext.getInstance().user.avatar,
            userId: AppContext.getInstance().user.userId,
            isGroup: false,
            avatarSize: 32
          })
        }
        .width('100%')
        .alignItems(VerticalAlign.Top)
      } else {
        Row({ space: 8 }) {
          ChatAvatar({
            name: this.target.isGroup ? m.senderName : this.target.displayName,
            avatar: '',
            userId: m.senderId,
            isGroup: false,
            avatarSize: 32
          })
          Column({ space: 3 }) {
            if (this.target.isGroup) {
              Text(m.senderName)
                .fontSize(11)
                .fontColor(AppTheme.textSecondary)
            }
            RichMessageRenderer({
              message: m,
              isSelf: false,
              onImageTap: (p: string) => {
                this.viewerImgPath = p;
              }
            })
          }
          .alignItems(HorizontalAlign.Start)
          .layoutWeight(1)
        }
        .width('100%')
        .alignItems(VerticalAlign.Top)
      }
    }
    .width('100%')
    .margin({ bottom: 12 })
    .gesture(LongPressGesture()
      .onAction(() => {
        this.msgMenuTarget = m;
      }))
  }

  build() {
    Stack() {
      Column() {
      // 聊天导航栏
      Row() {
        Button() {
          SymbolGlyph($r('sys.symbol.chevron_left'))
            .fontSize(20)
            .fontColor([AppTheme.text])
        }
        .width(40)
        .height(40)
        .borderRadius(20)
        .backgroundColor(this.pressBack ? AppTheme.chatPressBgStrong : Color.Transparent)
        .padding(0)
        .onClick(() => {
          this.getUIContext().getRouter().back();
        })
        .onTouch((event: TouchEvent) => {
          if (event.type === TouchType.Down) {
            this.pressBack = true;
          } else if (event.type === TouchType.Up || event.type === TouchType.Cancel) {
            this.pressBack = false;
          }
        })

        Column({ space: 2 }) {
          Text(this.target.displayName)
            .fontSize(16)
            .fontColor(AppTheme.text)
            .fontWeight(600)
            .maxLines(1)
            .textOverflow({ overflow: TextOverflow.Ellipsis })
          Row({ space: 4 }) {
            if (!this.target.isGroup) {
              Circle({ width: 6, height: 6 })
                .fill(this.peerOnline() ? AppTheme.up : AppTheme.textSecondary)
            }
            Text(this.statusText())
              .fontSize(11)
              .fontColor(this.vm.peerTyping ? AppTheme.primary : AppTheme.textSecondary)
          }
        }
        .layoutWeight(1)
        .alignItems(HorizontalAlign.Center)

        // 微信布局：电话图标（私聊可用）+ "···"更多
        Button() {
          SymbolGlyph($r('sys.symbol.phone'))
            .fontSize(19)
            .fontColor([this.target.isGroup ? AppTheme.textLow : AppTheme.text])
        }
        .width(40)
        .height(40)
        .borderRadius(20)
        .backgroundColor(Color.Transparent)
        .padding(0)
        .enabled(!this.target.isGroup)
        .onClick(() => {
          this.startCall(ImCallType.Audio);
        })

        Button() {
          SymbolGlyph($r('sys.symbol.more'))
            .fontSize(20)
            .fontColor([AppTheme.text])
        }
        .width(40)
        .height(40)
        .borderRadius(20)
        .backgroundColor(this.pressMore ? AppTheme.chatPressBgStrong : Color.Transparent)
        .padding(0)
        .onTouch((event: TouchEvent) => {
          if (event.type === TouchType.Down) {
            this.pressMore = true;
          } else if (event.type === TouchType.Up || event.type === TouchType.Cancel) {
            this.pressMore = false;
          }
        })
        .onClick(() => {
          this.getUIContext().getPromptAction().showToast({ message: '聊天设置即将开放', duration: 1500 });
        })
      }
      .width('100%')
      .height(52)
      .padding({ left: 8, right: 8 })
      .backgroundColor(AppTheme.chatHeaderBg)
      .backgroundBlurStyle(BlurStyle.Thin)
      .border({ width: { bottom: 1 }, color: AppTheme.chatBorderFaint })

      // 消息流
      List({ scroller: this.scroller }) {
        ForEach(this.uiMessages, (m: ChatMessageItem, index: number) => {
          ListItem() {
            this.messageItem(m, index)
          }
        }, (m: ChatMessageItem) => `${m.messageId || 'no-msg'}-${m.clientMessageId || 'no-client'}-${m.status}-${m.timestamp}-${m.contentType}-${(m.content || '').length}`)

        if (this.uiPeerTyping) {
          ListItem() {
            Row({ space: 8 }) {
              ChatAvatar({
                name: this.target.displayName,
                avatar: '',
                userId: this.target.peerId,
                isGroup: false,
                avatarSize: 32
              })
              Row() {
                TypingDots()
              }
              .padding({ left: 16, right: 16, top: 10, bottom: 10 })
              .borderRadius({
                topLeft: AppTheme.chatBubbleTipRadius,
                topRight: AppTheme.chatBubbleRadius,
                bottomLeft: AppTheme.chatBubbleRadius,
                bottomRight: AppTheme.chatBubbleRadius
              })
              .backgroundColor(AppTheme.card)
              .border({ width: 1, color: AppTheme.chatBorderFaint })
            }
            .width('100%')
            .alignItems(VerticalAlign.Top)
          }
        }
      }
      .layoutWeight(1)
      .width('100%')
      .padding({ left: 16, right: 16, top: 16, bottom: 8 })
      .scrollBar(BarState.Off)
      .edgeEffect(EdgeEffect.Spring)
      .onAppear(() => {
        this.scrollToBottom(300);
      })

      // 底部输入栏（微信布局：语音/键盘切换 / 输入框或按住说话 / 表情 + 加号，输入后变发送）
      Row({ space: 6 }) {
        // 语音/键盘模式切换键（对标微信左下角）
        Button() {
          Text(this.voiceMode ? '⌨' : '🎤')
            .fontSize(19)
        }
        .width(34)
        .height(36)
        .borderRadius(17)
        .backgroundColor(this.voiceMode ? AppTheme.activeBg : Color.Transparent)
        .padding(0)
        .onClick(() => {
          this.voiceMode = !this.voiceMode;
          if (this.voiceMode) {
            this.showEmojiPanel = false;
            this.showMorePanel = false;
          }
        })

        if (this.voiceMode) {
          // 按住说话（按下录音 / 上滑取消 / 松开发送）
          Button(this.recording
            ? (this.recordWillCancel ? '松开手指，取消发送' : '松开 发送')
            : '按住 说话')
            .fontSize(15)
            .fontColor(this.recording
              ? (this.recordWillCancel ? AppTheme.down : AppTheme.primary)
              : AppTheme.text)
            .layoutWeight(1)
            .height(38)
            .borderRadius(16)
            .backgroundColor(this.recording
              ? (this.recordWillCancel ? 'rgba(255,107,107,0.15)' : AppTheme.activeBg)
              : AppTheme.card)
            .border({ width: 1, color: AppTheme.chatBorderFaint })
            .padding(0)
            .onTouch((event: TouchEvent) => {
              if (event.type === TouchType.Down) {
                this.recordTouchStartY = event.touches[0].y;
                this.startVoiceRecord();
              } else if (event.type === TouchType.Move) {
                this.moveVoiceRecord(event.touches[0].y);
              } else if (event.type === TouchType.Up) {
                this.endVoiceRecord(this.recordWillCancel);
              } else if (event.type === TouchType.Cancel) {
                this.endVoiceRecord(true);
              }
            })
        } else {
          TextInput({ placeholder: '输入消息...', text: this.inputText })
            .layoutWeight(1)
            .height(38)
            .fontSize(14)
            .fontColor(AppTheme.text)
            .placeholderColor(AppTheme.textSecondary)
            .placeholderFont({ size: 14 })
            .backgroundColor(AppTheme.card)
            .borderRadius(16)
            .border({ width: 1, color: AppTheme.chatBorderFaint })
            .padding({ left: 14, right: 14 })
            .onChange((v: string) => {
              this.inputText = v;
              this.vm.sendTyping();
            })
            .onSubmit(() => {
              this.doSend();
            })
        }

        if (!this.voiceMode) {
          Button() {
            SymbolGlyph($r('sys.symbol.face_smiling'))
              .fontSize(20)
              .fontColor([this.showEmojiPanel ? AppTheme.primary : AppTheme.textSecondary])
          }
          .width(34)
          .height(36)
          .borderRadius(17)
          .backgroundColor(this.showEmojiPanel ? AppTheme.activeBg : Color.Transparent)
          .padding(0)
          .onClick(() => {
            this.showEmojiPanel = !this.showEmojiPanel;
            if (this.showEmojiPanel) {
              this.showMorePanel = false;
            }
          })
        }

        if (this.inputText.trim().length > 0 && !this.voiceMode) {
          Button('发送')
            .height(36)
            .fontSize(13)
            .fontWeight(FontWeight.Medium)
            .fontColor('#ffffff')
            .backgroundColor(AppTheme.primary)
            .borderRadius(18)
            .padding({ left: 14, right: 14 })
            .onClick(() => {
              this.doSend();
            })
        } else {
          Button() {
            SymbolGlyph($r('sys.symbol.plus_circle'))
              .fontSize(20)
              .fontColor([this.showMorePanel ? AppTheme.primary : AppTheme.textSecondary])
          }
          .width(34)
          .height(36)
          .borderRadius(17)
          .backgroundColor(this.showMorePanel ? AppTheme.activeBg : Color.Transparent)
          .padding(0)
          .onClick(() => {
            this.showMorePanel = !this.showMorePanel;
            if (this.showMorePanel) {
              this.showEmojiPanel = false;
            }
          })
        }
      }
      .width('100%')
      .padding({ left: 10, right: 10, top: 10, bottom: 10 })
      .backgroundColor(AppTheme.chatInputBg)
      .border({ width: { top: 1 }, color: AppTheme.chatBorderFaint })

      // 表情面板
      if (this.showEmojiPanel) {
        EmojiPanel({
            onInsert: (emoji: string) => {
                this.onEmojiInsert(emoji);
            },
            onDelete: () => {
                this.onEmojiDelete();
            }
        })
      }

      // "+"扩展面板（微信加号面板：相册/拍摄/音视频通话/位置/红包等）
      if (this.showMorePanel) {
        MorePanel({
          onAction: (action: string) => {
            this.onMoreAction(action);
          }
        })
      }
      }
      .width('100%')
      .height('100%')
      .backgroundColor(AppTheme.bg)

      // 长按消息菜单（复制/删除）
      if (this.msgMenuTarget !== null) {
        this.messageMenu(this.msgMenuTarget)
      }

      // 全屏图片查看器（点击图片消息打开；点击任意处关闭）
      if (this.viewerImgPath.length > 0) {
        this.imageViewer(this.viewerImgPath)
      }

      // 位置发送确认面板
      if (this.pendingLocation !== null) {
        this.locationConfirmPanel(this.pendingLocation)
      }

      // 录音浮层（对标微信"松开手指，取消发送"）
      if (this.recording) {
        this.recordingOverlay()
      }

      // 全屏通话浮层（拨打/来电/通话中）
      CallOverlay()
    }
    .width('100%')
    .height('100%')
  }

  // ========== 录音浮层（录音中居中提示；上滑进入取消区变色） ==========
  @Builder
  recordingOverlay() {
    Stack() {
      Column({ space: 12 }) {
        Text(this.recordWillCancel ? '✕' : '🎤')
          .fontSize(40)
          .width(84)
          .height(84)
          .textAlign(TextAlign.Center)
          .borderRadius(20)
          .backgroundColor(this.recordWillCancel
            ? 'rgba(255,107,107,0.25)'
            : AppTheme.activeBg)
        if (this.recordWillCancel) {
          Text('松开手指，取消发送')
            .fontSize(15)
            .fontColor(AppTheme.down)
        } else {
          Text(`${this.recordSeconds}"`)
            .fontSize(20)
            .fontWeight(FontWeight.Medium)
            .fontColor(AppTheme.text)
            .fontFamily(AppTheme.fontMono)
          Text('上滑取消')
            .fontSize(12)
            .fontColor(AppTheme.textSecondary)
        }
      }
      .justifyContent(FlexAlign.Center)
      .width(160)
      .padding({ top: 24, bottom: 24 })
      .borderRadius(20)
      .backgroundColor(AppTheme.sheet)
      .border({ width: 1, color: AppTheme.chatBorderFaint })
    }
    .width('100%')
    .height('100%')
    .alignContent(Alignment.Center)
    .backgroundColor('rgba(0,0,0,0.35)')
  }

  // ========== 位置发送确认面板（对标微信"发送位置"） ==========
  @Builder
  locationConfirmPanel(loc: PendingLocation | null) {
    Stack() {
      // 半透明遮罩：点击关闭
      Column()
        .width('100%')
        .height('100%')
        .backgroundColor('rgba(0,0,0,0.55)')
        .onClick(() => {
          this.pendingLocation = null;
        })

      Column({ space: 14 }) {
        // 位置预览卡
        Column({ space: 8 }) {
          Row({ space: 12 }) {
            Text('📍')
              .fontSize(34)
            Column({ space: 4 }) {
              Text(loc !== null && loc.name.length > 0 ? loc.name : '我的位置')
                .fontSize(17)
                .fontWeight(FontWeight.Medium)
                .fontColor(AppTheme.text)
                .maxLines(1)
                .textOverflow({ overflow: TextOverflow.Ellipsis })
              if (loc !== null) {
                Text(loc.address)
                  .fontSize(12)
                  .fontColor(AppTheme.textSecondary)
                  .maxLines(2)
                  .textOverflow({ overflow: TextOverflow.Ellipsis })
              }
            }
            .alignItems(HorizontalAlign.Start)
            .layoutWeight(1)
          }
          .width('100%')
        }
        .width('100%')
        .padding(16)
        .borderRadius(12)
        .backgroundColor(AppTheme.card)
        .border({ width: 1, color: AppTheme.chatBorderFaint })

        // 坐标明细
        if (loc !== null) {
          Text(`纬度 ${loc.lat.toFixed(6)}　经度 ${loc.lng.toFixed(6)}`)
            .fontSize(11)
            .fontColor(AppTheme.textSecondary)
            .fontFamily(AppTheme.fontMono)
        }

        Row({ space: 10 }) {
          Button('取消')
            .fontSize(14)
            .fontColor(AppTheme.textSecondary)
            .backgroundColor('rgba(255,255,255,0.06)')
            .borderRadius(10)
            .layoutWeight(1)
            .height(42)
            .onClick(() => {
              this.pendingLocation = null;
            })
          Button('发送位置')
            .fontSize(14)
            .fontColor(Color.White)
            .backgroundColor(AppTheme.primary)
            .borderRadius(10)
            .layoutWeight(1)
            .height(42)
            .onClick(() => {
              this.sendPendingLocation();
            })
        }
        .width('100%')
      }
      .width('86%')
      .padding(20)
      .borderRadius(16)
      .backgroundColor(AppTheme.sheet)
      .border({ width: 1, color: AppTheme.chatBorderFaint })
      .onClick(() => {
        // 空处理器：拦截点击，避免穿透到遮罩关闭面板
      })
    }
    .width('100%')
    .height('100%')
  }

  // ========== 全屏图片查看器（点击图片消息打开；点击任意处关闭） ==========
  @Builder
  imageViewer(path: string) {
    Stack() {
      Column()
        .width('100%')
        .height('100%')
        .backgroundColor('rgba(0,0,0,0.92)')

      // 沙箱本地路径渲染（自签 https 已由下载链路落盘）
      Image('file://' + path)
        .objectFit(ImageFit.Contain)
        .width('100%')
        .height('100%')
        .draggable(false)

      // 顶部关闭提示
      Text('✕')
        .fontSize(18)
        .fontColor('#ffffff')
        .width(36)
        .height(36)
        .borderRadius(18)
        .backgroundColor('rgba(255,255,255,0.15)')
        .textAlign(TextAlign.Center)
        .position({ x: '88%', y: 48 })
        .onClick(() => {
          this.viewerImgPath = '';
        })
    }
    .width('100%')
    .height('100%')
    .onClick(() => {
      this.viewerImgPath = '';
    })
  }

  // ========== 长按消息菜单（对标微信：复制/删除） ==========
  @Builder
  messageMenu(target: ChatMessageItem | null) {
    Stack() {
      // 半透明遮罩：点击关闭
      Column()
        .width('100%')
        .height('100%')
        .backgroundColor('rgba(0,0,0,0.45)')
        .onClick(() => {
          this.msgMenuTarget = null;
        })

      Column() {
        if (target === null) {
          Text('')
        } else {
          if (this.isCopyable(target)) {
            Row({ space: 12 }) {
              Text('📋')
                .fontSize(18)
                .width(24)
                .textAlign(TextAlign.Center)
              Text('复制')
                .fontSize(16)
                .fontColor(AppTheme.text)
                .layoutWeight(1)
              Blank()
            }
            .width('100%')
            .height(52)
            .padding({ left: 18, right: 18 })
            .onClick(() => {
              this.doMsgMenuAction('copy');
            })

            Row()
              .height(1)
              .width('100%')
              .backgroundColor(AppTheme.chatBorderFaint)
              .margin({ left: 54 })
          }
          if (this.vm.canRecall(target)) {
            Row({ space: 12 }) {
              Text('↩')
                .fontSize(18)
                .width(24)
                .textAlign(TextAlign.Center)
              Text('撤回')
                .fontSize(16)
                .fontColor(AppTheme.warn)
                .layoutWeight(1)
              Blank()
            }
            .width('100%')
            .height(52)
            .padding({ left: 18, right: 18 })
            .onClick(() => {
              this.doMsgMenuAction('recall');
            })

            Row()
              .height(1)
              .width('100%')
              .backgroundColor(AppTheme.chatBorderFaint)
              .margin({ left: 54 })
          }
          Row({ space: 12 }) {
            Text('🗑')
              .fontSize(18)
              .width(24)
              .textAlign(TextAlign.Center)
            Text('删除')
              .fontSize(16)
              .fontColor(AppTheme.down)
              .layoutWeight(1)
            Blank()
          }
          .width('100%')
          .height(52)
          .padding({ left: 18, right: 18 })
          .onClick(() => {
            this.doMsgMenuAction('delete');
          })
        }
      }
      .width('76%')
      .borderRadius(14)
      .backgroundColor(AppTheme.sheet)
      .border({ width: 1, color: AppTheme.chatBorderFaint })
      .clip(true)
    }
    .width('100%')
    .height('100%')
  }
}

