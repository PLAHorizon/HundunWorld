import { AppTheme } from '../theme/AppTheme';
import { LoadingView, ErrorView, EmptyView, ChatAvatar } from '../components/CommonWidgets';
import { ConversationItem, ImViewModel } from '../viewmodels/ImViewModel';

/**
 * 消息列表页（对标微信"消息"Tab）。
 * 数据源：ImViewModel.conversations（IM 网关 + SQLite 缓存合并）。
 * 微信交互：置顶会话灰底置前、免打扰小圆点、长按操作菜单
 * （标为已读/未读、置顶、免打扰、删除该聊天）、左滑快速删除、右上角"+"菜单。
 */

interface ChatDetailParams {
  conversationJson: string;
}

@Entry
@Component
struct ChatListPage {
  /** 共享单例：跨 Tab 复用已加载缓存与连接状态，不再每次都从头拉取 */
  vm: ImViewModel = ImViewModel.getInstance();
  /** 搜索关键字（过滤会话名与摘要） */
  @State keyword: string = '';
  /** 按压中的会话（按压态） */
  @State pressedId: string = '';
  /** 长按操作菜单目标会话（null = 菜单关闭） */
  @State menuTarget: ConversationItem | null = null;
  /** 右上角"+"下拉菜单 */
  @State plusMenuOpen: boolean = false;

  // ===== @Observed 类字段 UI 镜像（确保页面重绘；直接读 vm 属性变更不会触发刷新）=====
  @State uiConversations: ConversationItem[] = [];
  @State uiLoading: boolean = true;
  @State uiLoadError: string = '';

  private syncFromVm(): void {
    this.uiConversations = this.vm.conversations.slice();
    this.uiLoading = this.vm.loading;
    this.uiLoadError = this.vm.loadError;
  }

  aboutToAppear(): void {
    this.vm.onConversationsChanged = () => {
      this.syncFromVm();
    };
    this.syncFromVm();
    this.vm.start();
  }

  /** 从聊天详情返回时重新订阅推送（详情页 stop 会清空会话单例回调） */
  onPageShow(): void {
    // 无论 connected 状态如何，回调都要重新订阅；start() 内部幂等处理连接与拉取
    this.vm.onConversationsChanged = () => {
      this.syncFromVm();
    };
    this.vm.start();
  }

  aboutToDisappear(): void {
    this.vm.stop();
  }

  /** 按关键字过滤会话 */
  private filtered(): ConversationItem[] {
    const kw = this.keyword.trim().toLowerCase();
    if (kw.length === 0) {
      return this.uiConversations;
    }
    return this.uiConversations.filter((item: ConversationItem) =>
      item.displayName.toLowerCase().includes(kw) ||
      item.lastMessageSummary.toLowerCase().includes(kw));
  }

  private openChat(item: ConversationItem): void {
    this.getUIContext().getRouter().pushUrl({
      url: 'pages/ChatDetailPage',
      params: {
        conversationJson: JSON.stringify({
          conversationId: item.conversationId,
          peerId: item.peerId,
          displayName: item.displayName,
          isGroup: item.isGroup
        })
      } as ChatDetailParams
    });
  }

  /** 时间文案：今天 HH:mm / 昨天 / 周X / M/D / 早期带年份 */
  private timeText(ts: number): string {
    if (ts <= 0) {
      return '';
    }
    const d = new Date(ts);
    const now = new Date();
    const hh = d.getHours().toString().padStart(2, '0');
    const mm = d.getMinutes().toString().padStart(2, '0');
    if (d.toDateString() === now.toDateString()) {
      return `${hh}:${mm}`;
    }
    const yesterday = new Date(now);
    yesterday.setDate(yesterday.getDate() - 1);
    if (d.toDateString() === yesterday.toDateString()) {
      return '昨天';
    }
    const weekNames: string[] = ['周日', '周一', '周二', '周三', '周四', '周五', '周六'];
    if (now.getTime() - d.getTime() < 7 * 24 * 3600 * 1000) {
      return weekNames[d.getDay()];
    }
    if (d.getFullYear() === now.getFullYear()) {
      return `${d.getMonth() + 1}/${d.getDate()}`;
    }
    return `${d.getFullYear()}/${d.getMonth() + 1}/${d.getDate()}`;
  }

  private showToast(message: string): void {
    this.getUIContext().getPromptAction().showToast({ message: message, duration: 2000 });
  }

  /** 长按菜单操作 */
  private async doMenuAction(action: string): Promise<void> {
    const target = this.menuTarget;
    this.menuTarget = null;
    if (target === null) {
      return;
    }
    try {
      if (action === 'unread') {
        await this.vm.markConversationUnread(target);
      } else if (action === 'read') {
        await this.vm.markConversationRead(target);
      } else if (action === 'pin') {
        await this.vm.setConversationPinned(target, true);
      } else if (action === 'unpin') {
        await this.vm.setConversationPinned(target, false);
      } else if (action === 'mute') {
        await this.vm.setConversationMuted(target, true);
      } else if (action === 'unmute') {
        await this.vm.setConversationMuted(target, false);
      } else if (action === 'delete') {
        await this.vm.deleteConversation(target);
      }
    } catch (e) {
      this.showToast((e as Error).message || '操作失败');
    }
  }

  /** 左滑删除 */
  private async doSwipeDelete(item: ConversationItem): Promise<void> {
    try {
      await this.vm.deleteConversation(item);
    } catch (e) {
      this.showToast((e as Error).message || '删除失败');
    }
  }

  @Builder
  unreadBadge(item: ConversationItem) {
    if (item.isMuted) {
      // 免打扰：小圆点（微信行为）
      if (item.unreadCount > 0) {
        Circle({ width: 8, height: 8 })
          .fill(AppTheme.badge)
          .margin({ left: 8 })
      }
    } else if (item.unreadCount > 0) {
      Text(item.unreadCount > 99 ? '99+' : `${item.unreadCount}`)
        .fontSize(11)
        .fontColor('#ffffff')
        .fontWeight(FontWeight.Medium)
        .textAlign(TextAlign.Center)
        .constraintSize({ minWidth: 18 })
        .height(18)
        .borderRadius(9)
        .padding({ left: 5, right: 5 })
        .margin({ left: 8 })
        .backgroundColor(AppTheme.badge)
    }
  }

  @Builder
  conversationItem(item: ConversationItem) {
    Row({ space: 12 }) {
      Stack({ alignContent: Alignment.BottomEnd }) {
        ChatAvatar({
          name: item.displayName,
          avatar: item.avatar,
          userId: item.peerId,
          isGroup: item.isGroup,
          avatarSize: 48
        })
        if (item.isMuted) {
          // 免打扰角标（小喇叭斜杠）
          Text('🔇')
            .fontSize(10)
            .padding(2)
            .borderRadius(8)
            .backgroundColor(AppTheme.sheet)
            .offset({ x: -2, y: -2 })
        }
      }
      .width(48)
      .height(48)

      Column() {
        Row() {
          Text(item.displayName)
            .fontSize(16)
            .fontColor(AppTheme.text)
            .maxLines(1)
            .textOverflow({ overflow: TextOverflow.Ellipsis })
            .layoutWeight(1)
          Text(this.timeText(item.lastMessageTime))
            .fontSize(11)
            .fontColor(AppTheme.textSecondary)
            .margin({ left: 8 })
        }
        .width('100%')

        Row() {
          Text(item.lastMessageSummary.length > 0 ? item.lastMessageSummary : ' ')
            .fontSize(13)
            .fontColor(AppTheme.textSecondary)
            .maxLines(1)
            .textOverflow({ overflow: TextOverflow.Ellipsis })
            .layoutWeight(1)
          this.unreadBadge(item)
        }
        .width('100%')
        .margin({ top: 4 })
      }
      .layoutWeight(1)
      .alignItems(HorizontalAlign.Start)
    }
    .width('100%')
    .constraintSize({ minHeight: 66 })
    .padding({ left: 16, right: 16, top: 10, bottom: 10 })
    .backgroundColor(this.pressedId === item.conversationId
      ? AppTheme.chatPressBg
      : (item.isPinned ? AppTheme.pinnedBg : Color.Transparent))
    .onClick(() => this.openChat(item))
    .gesture(LongPressGesture()
      .onAction(() => {
        this.menuTarget = item;
      }))
    .onTouch((event: TouchEvent) => {
      if (event.type === TouchType.Down) {
        this.pressedId = item.conversationId;
      } else if (event.type === TouchType.Up || event.type === TouchType.Cancel) {
        this.pressedId = '';
      }
    })
  }

  /** 左滑出现的删除按钮 */
  @Builder
  swipeDeleteButton(item: ConversationItem) {
    Column() {
      Text('删除')
        .fontSize(14)
        .fontColor(Color.White)
    }
    .width(64)
    .height('100%')
    .justifyContent(FlexAlign.Center)
    .backgroundColor(AppTheme.down)
    .borderRadius(8)
    .onClick(() => {
      this.doSwipeDelete(item);
    })
  }

  build() {
    Stack() {
      Column() {
        // ========== 顶部标题栏（微信风格：标题居左 + 搜索/加号居右） ==========
        Row() {
          Text(this.vm.totalUnread > 0 ? `消息(${this.vm.totalUnread > 99 ? '99+' : this.vm.totalUnread})` : '消息')
            .fontSize(20)
            .fontColor(AppTheme.text)
            .fontWeight(600)
          Blank()
          Row({ space: 4 }) {
            Button() {
              SymbolGlyph($r('sys.symbol.magnifyingglass'))
                .fontSize(20)
                .fontColor([AppTheme.text])
            }
            .width(40)
            .height(40)
            .borderRadius(20)
            .backgroundColor(Color.Transparent)
            .padding(0)
            .onClick(() => {
              this.keyword = '';
            })

            Stack({ alignContent: Alignment.TopEnd }) {
              Button() {
                SymbolGlyph($r('sys.symbol.plus'))
                  .fontSize(20)
                  .fontColor([AppTheme.text])
              }
              .width(40)
              .height(40)
              .borderRadius(20)
              .backgroundColor(Color.Transparent)
              .padding(0)
              .onClick(() => {
                this.plusMenuOpen = !this.plusMenuOpen;
              })
            }
            .width(40)
            .height(40)
          }
        }
        .width('100%')
        .height(52)
        .padding({ left: 16, right: 8 })
        .backgroundColor(AppTheme.chatHeaderBg)
        .backgroundBlurStyle(BlurStyle.Thin)
        .border({ width: { bottom: 1 }, color: AppTheme.chatBorderFaint })

        // ========== 搜索框（胶囊） ==========
        Row({ space: 8 }) {
          SymbolGlyph($r('sys.symbol.magnifyingglass'))
            .fontSize(16)
            .fontColor([AppTheme.textSecondary])
          TextInput({ placeholder: '搜索', text: this.keyword })
            .layoutWeight(1)
            .height(36)
            .fontSize(14)
            .fontColor(AppTheme.text)
            .placeholderColor(AppTheme.textSecondary)
            .placeholderFont({ size: 14 })
            .backgroundColor(Color.Transparent)
            .padding(0)
            .onChange((v: string) => {
              this.keyword = v;
            })
        }
        .width('100%')
        .height(40)
        .padding({ left: 16, right: 16 })
        .borderRadius(20)
        .backgroundColor(AppTheme.card)
        .border({ width: 1, color: AppTheme.chatBorderFaint })
        .margin({ left: 16, right: 16, top: 10, bottom: 4 })

        // ========== 会话列表 ==========
        if (this.uiLoading && this.uiConversations.length === 0) {
          LoadingView({ message: '加载会话…' })
        } else if (this.uiLoadError.length > 0 && this.uiConversations.length === 0) {
          ErrorView({
            message: this.uiLoadError,
            onRetry: () => {
              this.vm.refreshConversations();
            }
          })
        } else if (this.filtered().length === 0) {
          EmptyView({ message: this.keyword.length > 0 ? '未找到相关会话' : '暂无会话，去联系人页发起聊天吧' })
        } else {
          List() {
            ForEach(this.filtered(), (item: ConversationItem) => {
              ListItem() {
                this.conversationItem(item)
              }
              .swipeAction({ end: this.swipeDeleteButton(item) })
            }, (item: ConversationItem) => `${item.conversationId}-${item.lastMessageTime}-${item.unreadCount}-${item.isPinned}-${item.isMuted}`)
          }
          .layoutWeight(1)
          .width('100%')
          .scrollBar(BarState.Off)
          .edgeEffect(EdgeEffect.Spring)
          .divider({
            strokeWidth: 1,
            color: AppTheme.chatBorderFaint,
            startMargin: 76,
            endMargin: 16
          })
        }
      }
      .width('100%')
      .height('100%')

      // ===== 长按操作菜单（后声明在上层）=====
      if (this.menuTarget !== null) {
        this.actionMenu(this.menuTarget)
      }

      // ===== 右上角"+"下拉菜单 =====
      if (this.plusMenuOpen) {
        this.plusMenu()
      }
    }
    .width('100%')
    .height('100%')
    .backgroundColor(AppTheme.bg)
  }

  // ========== 长按操作菜单（对标微信：标为未读/置顶/免打扰/删除） ==========
  @Builder
  actionMenu(target: ConversationItem | null) {
    Stack() {
      // 半透明遮罩：点击关闭
      Column()
        .width('100%')
        .height('100%')
        .backgroundColor('rgba(0,0,0,0.45)')
        .onClick(() => {
          this.menuTarget = null;
        })

      Column() {
        if (target === null) {
          Text('')
        } else {
          if (target.unreadCount > 0) {
            this.menuRow('标为已读', '✉', 'read', false)
            this.menuDivider()
          } else {
            this.menuRow('标为未读', '✉', 'unread', false)
            this.menuDivider()
          }
          this.menuRow(target.isPinned ? '取消置顶' : '置顶该聊天', '📌', target.isPinned ? 'unpin' : 'pin', false)
          this.menuDivider()
          this.menuRow(target.isMuted ? '关闭免打扰' : '消息免打扰', '🔕', target.isMuted ? 'unmute' : 'mute', false)
          this.menuDivider()
          this.menuRow('删除该聊天', '🗑', 'delete', true)
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

  @Builder
  menuRow(label: string, icon: string, action: string, danger: boolean) {
    Row({ space: 12 }) {
      Text(icon)
        .fontSize(18)
        .width(24)
        .textAlign(TextAlign.Center)
      Text(label)
        .fontSize(16)
        .fontColor(danger ? AppTheme.down : AppTheme.text)
        .layoutWeight(1)
      Blank()
    }
    .width('100%')
    .height(52)
    .padding({ left: 18, right: 18 })
    .onClick(() => {
      this.doMenuAction(action);
    })
  }

  @Builder
  menuDivider() {
    Row()
      .height(1)
      .width('100%')
      .backgroundColor(AppTheme.chatBorderFaint)
      .margin({ left: 50 })
  }

  // ========== 右上角"+"下拉菜单（对标微信：发起群聊/添加朋友） ==========
  @Builder
  plusMenu() {
    Stack({ alignContent: Alignment.TopEnd }) {
      // 半透明遮罩：点击关闭
      Column()
        .width('100%')
        .height('100%')
        .backgroundColor('rgba(0,0,0,0.25)')
        .onClick(() => {
          this.plusMenuOpen = false;
        })

      Column() {
        this.plusMenuRow('发起群聊', '👥')
        Row()
          .height(1)
          .width('100%')
          .backgroundColor(AppTheme.chatBorderFaint)
        this.plusMenuRow('添加朋友', '➕')
      }
      .width(180)
      .margin({ top: 52, right: 12 })
      .borderRadius(12)
      .backgroundColor(AppTheme.sheet)
      .border({ width: 1, color: AppTheme.chatBorderFaint })
      .clip(true)
    }
    .width('100%')
    .height('100%')
  }

  @Builder
  plusMenuRow(label: string, icon: string) {
    Row({ space: 10 }) {
      Text(icon)
        .fontSize(17)
        .width(22)
        .textAlign(TextAlign.Center)
      Text(label)
        .fontSize(15)
        .fontColor(AppTheme.text)
    }
    .width('100%')
    .height(48)
    .padding({ left: 16, right: 16 })
    .alignItems(VerticalAlign.Center)
    .onClick(() => {
      this.plusMenuOpen = false;
      if (label === '添加朋友') {
        this.getUIContext().getRouter().pushUrl({ url: 'pages/ContactsPage' });
      } else {
        this.showToast('群聊功能即将开放');
      }
    })
  }
}
