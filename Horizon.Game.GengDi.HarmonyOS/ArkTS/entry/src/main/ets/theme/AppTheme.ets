/**
 * 全局主题令牌。
 *
 * 色彩对齐"天气"模块（墨迹天气风格）：深蓝灰渐变背景（#242c3e → #171d2b）
 * + 青色 #2dd4bf 强调 + 半透明白卡片。原生页面统一从这里取值，保证视觉一致。
 */

export class AppTheme {
  /** 页面背景（天气渐变尾部色，配合 skyGradient 使用） */
  static readonly bg: string = '#171d2b';
  /** 天空渐变起始色 */
  static readonly skyFrom: string = '#242c3e';
  /** 天空渐变中间色 */
  static readonly skyMid: string = '#1d2434';
  /** 天空渐变终止色 */
  static readonly skyTo: string = '#171d2b';
  /** 卡片/面板背景（天气页半透明白卡） */
  static readonly card: string = 'rgba(255,255,255,0.055)';
  /** 卡片描边 */
  static readonly cardBorder: string = 'rgba(255,255,255,0.08)';
  /** 卡片内单元格背景 */
  static readonly cellBg: string = 'rgba(255,255,255,0.04)';
  /** 激活/选中底色（青色 12%） */
  static readonly activeBg: string = 'rgba(45,212,191,0.12)';
  /** 弹窗/浮层背景 */
  static readonly sheet: string = '#1d2434';
  /** 分隔线 */
  static readonly divider: string = 'rgba(255,255,255,0.08)';
  /** 主色（青色：激活/按钮/强调） */
  static readonly primary: string = '#2dd4bf';
  /** 主色渐变尾（深青） */
  static readonly primaryDeep: string = '#0d9488';
  /** 辅助色（品牌蓝：曲线/次强调） */
  static readonly brand: string = '#3d8bff';
  /** 主文字 */
  static readonly text: string = '#ffffff';
  /** 次要文字 */
  static readonly textSecondary: string = 'rgba(255,255,255,0.66)';
  /** 弱文字 */
  static readonly textLow: string = 'rgba(255,255,255,0.42)';
  /** 上涨/成功 */
  static readonly up: string = '#26b5a0';
  /** 下跌/危险 */
  static readonly down: string = '#ff6b6b';
  /** 警告 */
  static readonly warn: string = '#ffd54a';
  /** 未读红点 */
  static readonly badge: string = '#ff6b6b';

  static readonly fontTitle: number = 18;
  static readonly fontSub: number = 15;
  static readonly fontBody: number = 13;
  static readonly fontCaption: number = 11;

  static readonly spaceSm: number = 8;
  static readonly spaceMd: number = 12;
  static readonly spaceLg: number = 16;
  static readonly radiusCard: number = 16;
  static readonly radiusBubble: number = 10;

  // ===== 天空渐变（页面根背景，linearGradient angle 225）=====
  /** 渐变色标（[颜色, 位置]） */
  static readonly skyColors: [ResourceColor, number][] = [
    ['#242c3e', 0], ['#1d2434', 0.45], ['#171d2b', 1]
  ];
  /** 渐变角度 */
  static readonly skyAngle: number = 225;

  // ===== 磨砂玻璃 / 渐变 / 胶囊分隔 令牌（来自 harmony-discover.html）=====
  /** 磨砂玻璃卡片背景（bg-card/45） */
  static readonly glassBg: string = 'rgba(19,23,34,0.45)';
  /** 玻璃卡片白色 6% 边框 */
  static readonly glassBorder: string = 'rgba(255,255,255,0.06)';
  /** 玻璃卡片圆角 */
  static readonly glassRadius: number = 24;
  /** 卡片顶部高光分割线 */
  static readonly highlightLine: string = 'rgba(255,255,255,0.10)';
  /** 胶囊分隔（条目之间） */
  static readonly capsuleDivider: string = 'rgba(255,255,255,0.05)';
  /** 置顶会话底色 */
  static readonly pinnedBg: string = 'rgba(255,255,255,0.04)';
  /** 等宽字体（时间戳/文件大小） */
  static readonly fontMono: string = 'HarmonyOS Sans Mono';
  /** 群聊固定渐变索引 */
  static readonly groupGradientIndex: number = 3;

  /**
   * 渐变头像池（8 色）。索引按 userId 哈希取模。
   * 每项 [startColor, endColor]，用于 linearGradient。
   */
  static readonly gradientPool: string[][] = [
    ['#26b5a0', '#1d7a70'],
    ['#3a78ff', '#1f4fd6'],
    ['#ec4899', '#be185d'],
    ['#7e57c2', '#5e35b1'],
    ['#ff7043', '#d84315'],
    ['#66bb6a', '#43a047'],
    ['#ffa726', '#f57c00'],
    ['#5c6bc0', '#3949ab']
  ];

  /** 按 userId 哈希到 gradientPool 索引（稳定） */
  static gradientIndexFor(userId: number): number {
    if (userId <= 0) {
      return 0;
    }
    // Math.imul 做 32 位整数乘法，>>> 0 转无符号，避免大 userId 精度丢失
    const h = Math.imul(userId, 2654435761) >>> 0;
    return h % AppTheme.gradientPool.length;
  }

  /** 取渐变池某项的起始色（越界回退到首项） */
  static gradientStart(idx: number): string {
    const i = idx < 0 || idx >= AppTheme.gradientPool.length ? 0 : idx;
    return AppTheme.gradientPool[i][0];
  }

  /** 取渐变池某项的终止色（越界回退到首项） */
  static gradientEnd(idx: number): string {
    const i = idx < 0 || idx >= AppTheme.gradientPool.length ? 0 : idx;
    return AppTheme.gradientPool[i][1];
  }

  // ===== 聊天页设计令牌（对齐天空渐变底色）=====
  /** 顶部栏半透明底 */
  static readonly chatHeaderBg: string = 'rgba(23,29,43,0.55)';
  /** 输入栏半透明底 */
  static readonly chatInputBg: string = 'rgba(23,29,43,0.80)';
  /** 白色 4% 分隔线/描边（border-white/[0.04]） */
  static readonly chatBorderFaint: string = 'rgba(255,255,255,0.04)';
  /** 按压态白 5%（active:bg-white/5） */
  static readonly chatPressBg: string = 'rgba(255,255,255,0.05)';
  /** 按钮按压态白 10%（active:bg-white/10） */
  static readonly chatPressBgStrong: string = 'rgba(255,255,255,0.10)';
  /** 消息气泡圆角（rounded-2xl） */
  static readonly chatBubbleRadius: number = 16;
  /** 消息气泡小角（rounded-tl-md / rounded-tr-md = 6px） */
  static readonly chatBubbleTipRadius: number = 6;
  /** 在线状态点描边（border-2 border-card） */
  static readonly chatDotStroke: string = '#171d2b';

  /** 设计原型（harmony-chat.html）头像渐变池：Tailwind 500→700 八色，按 userId 哈希稳定取色 */
  static readonly chatGradientPool: string[][] = [
    ['#3b82f6', '#1d4ed8'], // blue
    ['#a855f7', '#7e22ce'], // purple
    ['#14b8a6', '#0f766e'], // teal
    ['#6366f1', '#4338ca'], // indigo
    ['#10b981', '#047857'], // emerald
    ['#f59e0b', '#b45309'], // amber
    ['#f43f5e', '#be123c'], // rose
    ['#ec4899', '#be185d']  // pink
  ];

  /** 按 userId 哈希到 chatGradientPool 索引（稳定） */
  static chatGradientIndexFor(userId: number): number {
    if (userId <= 0) {
      return 0;
    }
    const h = Math.imul(userId, 2654435761) >>> 0;
    return h % AppTheme.chatGradientPool.length;
  }

  /** 设计原型渐变起始色（by userId） */
  static chatGradientStart(userId: number): string {
    return AppTheme.chatGradientPool[AppTheme.chatGradientIndexFor(userId)][0];
  }

  /** 设计原型渐变终止色（by userId） */
  static chatGradientEnd(userId: number): string {
    return AppTheme.chatGradientPool[AppTheme.chatGradientIndexFor(userId)][1];
  }
}

/** 通用卡片容器样式常量（配合 .attributeModifier 或直接内联使用） */
export class CardStyle {
  static readonly normal: Record<string, Object> = {
    'backgroundColor': AppTheme.card,
    'borderRadius': AppTheme.radiusCard
  };
}
