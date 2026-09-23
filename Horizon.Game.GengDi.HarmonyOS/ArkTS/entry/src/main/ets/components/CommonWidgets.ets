import { AppTheme } from '../theme/AppTheme';

/**
 * 通用 UI 组件库：
 * - UserAvatar / GradientAvatar：文字/图片/渐变头像
 * - GlassCard：磨砂玻璃卡片容器
 * - SectionCard：暗色卡片容器（标题 + 内容）
 * - LoadingView / ErrorView / EmptyView：列表三态
 * - MessageBubble / MessageImageCard / MessageFileCard：聊天消息多类型
 * - CapsuleDivider / SectionTitle / StatBadge：装饰与状态
 */

/** 渐变头像：按 userId 哈希到 gradientPool，无头像时显示首字 + linearGradient 背景 */
@Component
export struct GradientAvatar {
  @Prop name: string = '';
  @Prop avatar: string = '';
  @Prop avatarSize: number = 44;
  @Prop userId: number = 0;
  @Prop isGroup: boolean = false;

  private initial(): string {
    const n = this.name.trim();
    return n.length > 0 ? n.charAt(0) : '?';
  }

  private gradientIndex(): number {
    if (this.isGroup) {
      return AppTheme.groupGradientIndex;
    }
    return AppTheme.gradientIndexFor(this.userId);
  }

  build() {
    if (this.avatar && this.avatar.startsWith('http')) {
      Image(this.avatar)
        .width(this.avatarSize)
        .height(this.avatarSize)
        .borderRadius(this.avatarSize / 2)
        .autoResize(true)
        .alt('.jpg')
    } else {
      Stack() {
        Text(this.initial())
          .fontSize(this.avatarSize * 0.42)
          .fontColor('#ffffff')
          .fontWeight(FontWeight.Medium)
          .textAlign(TextAlign.Center)
      }
      .width(this.avatarSize)
      .height(this.avatarSize)
      .borderRadius(this.avatarSize / 2)
      .linearGradient({
        angle: 135,

        colors: [[AppTheme.gradientStart(this.gradientIndex()), 0],
          [AppTheme.gradientEnd(this.gradientIndex()), 1]]
      })
    }
  }
}

/** 兼容旧调用：文字/图片头像（无渐变，回退到 bgColor 单色） */
@Component
export struct UserAvatar {
  @Prop name: string = '';
  @Prop avatar: string = '';
  @Prop avatarSize: number = 44;
  @Prop bgColor: string = '#2dd4bf';

  private initial(): string {
    const n = this.name.trim();
    return n.length > 0 ? n.charAt(0) : '?';
  }

  build() {
    if (this.avatar && this.avatar.startsWith('http')) {
      Image(this.avatar)
        .width(this.avatarSize)
        .height(this.avatarSize)
        .borderRadius(this.avatarSize / 2)
        .autoResize(true)
        .alt('.jpg')
    } else {
      Text(this.initial())
        .fontSize(this.avatarSize * 0.42)
        .fontColor('#ffffff')
        .fontWeight(FontWeight.Medium)
        .textAlign(TextAlign.Center)
        .width(this.avatarSize)
        .height(this.avatarSize)
        .borderRadius(this.avatarSize / 2)
        .backgroundColor(this.bgColor)
    }
  }
}

/** 磨砂玻璃卡片容器（backgroundBlurStyle + 半透明背景 + 圆角24 + 白色6%边框 + 阴影） */
@Component
export struct GlassCard {
  @BuilderParam content: () => void = emptyBuilder;

  build() {
    Column() {
      this.content()
    }
    .width('100%')
    .backgroundColor(AppTheme.glassBg)
    .backgroundBlurStyle(BlurStyle.Thick)
    .border({ width: 1, color: AppTheme.glassBorder })
    .borderRadius(AppTheme.glassRadius)
    .shadow({ radius: 20, color: 'rgba(0,0,0,0.25)', offsetX: 0, offsetY: 8 })
  }
}

/** 卡片顶部高光分割线（渐变细线） */
@Component
export struct HighlightLine {
  build() {
    Row()
      .width('90%')
      .height(1)
      .linearGradient({
        angle: 90,

        colors: [['rgba(255,255,255,0)', 0],
          [AppTheme.highlightLine, 0.5],
          ['rgba(255,255,255,0)', 1]]
      })
  }
}

/** 胶囊分隔（条目之间，8x2 圆角白色5%） */
@Component
export struct CapsuleDivider {
  build() {
    Row()
      .width(8)
      .height(2)
      .borderRadius(1)
      .backgroundColor(AppTheme.capsuleDivider)
  }
}

/** 章节标题：竖条 + 标题 + 可选未读胶囊 + 可选操作按钮 */
@Component
export struct SectionTitle {
  @Prop title: string = '';
  @Prop badgeText: string = '';
  @Prop actionText: string = '';
  onAction: () => void = () => {
  };

  build() {
    Row({ space: 8 }) {
      Column()
        .width(4)
        .height(16)
        .borderRadius(2)
        .linearGradient({ angle: 180, colors: [[AppTheme.primary, 0], ['#0d9488', 1]] })
      Text(this.title)
        .fontSize(AppTheme.fontSub)
        .fontColor(AppTheme.text)
        .fontWeight(FontWeight.Bold)
        .letterSpacing(0.5)
        .layoutWeight(1)
      if (this.badgeText.length > 0) {
        Text(this.badgeText)
          .fontSize(10)
          .fontColor('#ffffff')
          .padding({ left: 6, right: 6, top: 2, bottom: 2 })
          .borderRadius(9)
          .backgroundColor(AppTheme.badge)
      }
      if (this.actionText.length > 0) {
        Text(this.actionText)
          .fontSize(AppTheme.fontCaption)
          .fontColor(AppTheme.primary)
          .onClick(() => this.onAction())
      }
    }
    .width('100%')
    .alignItems(VerticalAlign.Center)
  }
}

@Component
export struct SectionCard {
  @Prop title: string = '';
  @Prop showTitle: boolean = true;
  @BuilderParam content: () => void = emptyBuilder;

  build() {
    Column() {
      if (this.showTitle && this.title.length > 0) {
        Text(this.title)
          .fontSize(AppTheme.fontSub)
          .fontColor(AppTheme.text)
          .fontWeight(FontWeight.Bold)
          .margin({ bottom: AppTheme.spaceMd })
      }
      this.content()
    }
    .width('100%')
    .padding(AppTheme.spaceLg)
    .backgroundColor(AppTheme.card)
    .border({ width: 1, color: AppTheme.cardBorder })
    .borderRadius(AppTheme.radiusCard)
    .alignItems(HorizontalAlign.Start)
  }
}

/** 聊天头像（100% 还原 harmony-chat.html）：渐变底 + 首字；群聊为双人群标；http 头像直出 */
@Component
export struct ChatAvatar {
  @Prop name: string = '';
  @Prop avatar: string = '';
  @Prop userId: number = 0;
  @Prop isGroup: boolean = false;
  @Prop avatarSize: number = 48;

  private initial(): string {
    const n = this.name.trim();
    return n.length > 0 ? n.charAt(0) : '?';
  }

  build() {
    Stack() {
      if (this.avatar && this.avatar.startsWith('http')) {
        Image(this.avatar)
          .width(this.avatarSize)
          .height(this.avatarSize)
          .borderRadius(this.avatarSize / 2)
          .autoResize(true)
          .alt('.jpg')
      } else if (this.isGroup) {
        SymbolGlyph($r('sys.symbol.person_2'))
          .fontSize(this.avatarSize * 0.42)
          .fontColor(['#ffffff'])
      } else {
        Text(this.initial())
          .fontSize(this.avatarSize * 0.36)
          .fontColor('#ffffff')
          .fontWeight(FontWeight.Medium)
          .textAlign(TextAlign.Center)
      }
    }
    .width(this.avatarSize)
    .height(this.avatarSize)
    .borderRadius(this.avatarSize / 2)
    .linearGradient({
      angle: 135,
      colors: [[AppTheme.chatGradientStart(this.userId), 0],
        [AppTheme.chatGradientEnd(this.userId), 1]]
    })
  }
}

@Builder
function emptyBuilder() {
}

@Component
export struct LoadingView {
  @Prop message: string = '加载中…';

  build() {
    Column({ space: AppTheme.spaceMd }) {
      LoadingProgress()
        .width(36)
        .height(36)
        .color(AppTheme.primary)
      Text(this.message)
        .fontSize(AppTheme.fontBody)
        .fontColor(AppTheme.textSecondary)
    }
    .width('100%')
    .padding(32)
    .justifyContent(FlexAlign.Center)
  }
}

@Component
export struct ErrorView {
  @Prop message: string = '加载失败';
  onRetry: () => void = () => {
  };

  build() {
    Column({ space: AppTheme.spaceMd }) {
      Text('⚠')
        .fontSize(32)
        .fontColor(AppTheme.warn)
      Text(this.message)
        .fontSize(AppTheme.fontBody)
        .fontColor(AppTheme.textSecondary)
        .textAlign(TextAlign.Center)
      Button('重试')
        .fontSize(AppTheme.fontBody)
        .fontColor(AppTheme.primary)
        .backgroundColor(AppTheme.activeBg)
        .padding({ left: 20, right: 20 })
        .onClick(() => this.onRetry())
    }
    .width('100%')
    .padding(32)
    .justifyContent(FlexAlign.Center)
  }
}

@Component
export struct EmptyView {
  @Prop message: string = '暂无数据';

  build() {
    Column() {
      Text(this.message)
        .fontSize(AppTheme.fontBody)
        .fontColor(AppTheme.textSecondary)
    }
    .width('100%')
    .padding(32)
    .justifyContent(FlexAlign.Center)
  }
}

/** 聊天消息气泡。isSelf 决定左右与配色。 */
@Component
export struct MessageBubble {
  @Prop content: string = '';
  @Prop isSelf: boolean = false;
  @Prop timeText: string = '';

  build() {
    Column() {
      if (this.timeText.length > 0) {
        Text(this.timeText)
          .fontSize(AppTheme.fontCaption)
          .fontColor(AppTheme.textSecondary)
          .margin({ bottom: 4 })
      }
      Text(this.content)
        .fontSize(AppTheme.fontSub)
        .fontColor(this.isSelf ? '#ffffff' : AppTheme.text)
        .lineHeight(22)
        .padding({ left: 12, right: 12, top: 9, bottom: 9 })
        .constraintSize({ maxWidth: '75%' })
        .borderRadius(AppTheme.radiusBubble)
        .backgroundColor(this.isSelf ? AppTheme.primary : 'rgba(255,255,255,0.06)')
    }
    .width('100%')
    .alignItems(this.isSelf ? HorizontalAlign.End : HorizontalAlign.Start)
  }
}

/** 图片消息卡片（contentType=1）：圆角图片 + 点击预览占位 */
@Component
export struct MessageImageCard {
  @Prop imageUrl: string = '';
  @Prop isSelf: boolean = false;
  @Prop timeText: string = '';
  onTap: () => void = () => {
  };

  build() {
    Column() {
      if (this.timeText.length > 0) {
        Text(this.timeText)
          .fontSize(AppTheme.fontCaption)
          .fontColor(AppTheme.textSecondary)
          .margin({ bottom: 4 })
      }
      Image(this.imageUrl.length > 0 ? this.imageUrl : 'placeholder')
        .width(180)
        .height(140)
        .borderRadius(8)
        .objectFit(ImageFit.Cover)
        .autoResize(true)
        .onClick(() => this.onTap())
    }
    .width('100%')
    .alignItems(this.isSelf ? HorizontalAlign.End : HorizontalAlign.Start)
  }
}

/** 文件消息卡片（contentType=4）：文件图标 + 文件名 + 大小 + 点击下载 */
@Component
export struct MessageFileCard {
  @Prop fileName: string = '';
  @Prop fileSize: number = 0;
  @Prop isSelf: boolean = false;
  @Prop timeText: string = '';
  onTap: () => void = () => {
  };

  private sizeText(): string {
    const s = this.fileSize;
    if (s <= 0) {
      return '0 B';
    }
    if (s < 1024) {
      return `${s} B`;
    }
    if (s < 1024 * 1024) {
      return `${(s / 1024).toFixed(1)} KB`;
    }
    if (s < 1024 * 1024 * 1024) {
      return `${(s / 1024 / 1024).toFixed(1)} MB`;
    }
    return `${(s / 1024 / 1024 / 1024).toFixed(2)} GB`;
  }

  build() {
    Column() {
      if (this.timeText.length > 0) {
        Text(this.timeText)
          .fontSize(AppTheme.fontCaption)
          .fontColor(AppTheme.textSecondary)
          .margin({ bottom: 4 })
      }
      Row({ space: 10 }) {
        Text('📄')
          .fontSize(28)
        Column({ space: 2 }) {
          Text(this.fileName.length > 0 ? this.fileName : '未命名文件')
            .fontSize(AppTheme.fontBody)
            .fontColor(AppTheme.text)
            .maxLines(1)
            .textOverflow({ overflow: TextOverflow.Ellipsis })
            .constraintSize({ maxWidth: 160 })
          Text(this.sizeText())
            .fontSize(AppTheme.fontCaption)
            .fontColor(AppTheme.textSecondary)
            .fontFamily(AppTheme.fontMono)
        }
        .alignItems(HorizontalAlign.Start)
      }
      .padding({ left: 12, right: 12, top: 10, bottom: 10 })
      .borderRadius(AppTheme.radiusBubble)
      .backgroundColor(this.isSelf ? AppTheme.primary : 'rgba(255,255,255,0.06)')
      .onClick(() => this.onTap())
    }
    .width('100%')
    .alignItems(this.isSelf ? HorizontalAlign.End : HorizontalAlign.Start)
  }
}

/** 音视频占位卡片（contentType=2/3） */
@Component
export struct MessageMediaPlaceholderCard {
  @Prop mediaType: string = '音频';
  @Prop isSelf: boolean = false;
  @Prop timeText: string = '';

  build() {
    Column() {
      if (this.timeText.length > 0) {
        Text(this.timeText)
          .fontSize(AppTheme.fontCaption)
          .fontColor(AppTheme.textSecondary)
          .margin({ bottom: 4 })
      }
      Row({ space: 8 }) {
        Text(this.mediaType === '视频' ? '🎬' : '🎵')
          .fontSize(20)
        Text(`[ ${this.mediaType}消息，暂不支持预览 ]`)
          .fontSize(AppTheme.fontBody)
          .fontColor(AppTheme.textSecondary)
      }
      .padding({ left: 12, right: 12, top: 9, bottom: 9 })
      .borderRadius(AppTheme.radiusBubble)
      .backgroundColor(this.isSelf ? AppTheme.primary : 'rgba(255,255,255,0.06)')
    }
    .width('100%')
    .alignItems(this.isSelf ? HorizontalAlign.End : HorizontalAlign.Start)
  }
}

/** 涨跌/状态小标签 */
@Component
export struct StatBadge {
  @Prop text: string = '';
  @Prop positive: boolean = true;

  build() {
    Text(this.text)
      .fontSize(AppTheme.fontCaption)
      .fontColor(this.positive ? AppTheme.up : AppTheme.down)
      .padding({ left: 6, right: 6, top: 2, bottom: 2 })
      .borderRadius(4)
      .backgroundColor(this.positive ? 'rgba(38,181,160,0.12)' : 'rgba(255,107,107,0.12)')
  }
}
