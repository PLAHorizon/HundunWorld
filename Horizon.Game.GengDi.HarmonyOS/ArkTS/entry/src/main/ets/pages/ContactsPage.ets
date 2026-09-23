import { AppTheme } from '../theme/AppTheme';
import { LoadingView, ErrorView, EmptyView, ChatAvatar } from '../components/CommonWidgets';
import { ContactItem, ImViewModel, PendingRequestItem } from '../viewmodels/ImViewModel';
import { AppContext } from '../model/AppContext';
import { PinyinHelper } from '../model/PinyinHelper';
import { ImContactAddResponse, ImContactRelation } from '../services/im/ImMessages';

/**
 * 联系人页（对标微信"通讯录"Tab）。
 * 数据源：ImViewModel.contacts（IM 网关联系人列表 + SQLite 缓存），
 * 在线状态经 ContactOnlineStatus 推送实时刷新。
 * 微信交互：新的朋友/群聊功能入口行、好友按拼音首字母 A-Z 分组、
 * 右侧字母索引条（点击跳转分组 + 当前字母气泡）、点击进联系人资料页。
 * 拼音 collation 不可用时自动退化为不分组列表。
 */

/** 拼音分组段 */
class ContactSection {
  letter: string = '';
  contacts: ContactItem[] = [];
}

/** 列表渲染模型（渲染顺序与索引条跳转映射保持一致） */
class ContactsListModel {
  sections: ContactSection[] = [];
  /** 索引条字母（含 '#'） */
  letters: string[] = [];
  /** 字母 → 分组头在 List 中的 item 序号 */
  letterToItemIndex: Map<string, number> = new Map();
  /** 每个 item 所属分组字母（功能行/群组行为 ''） */
  itemLetters: string[] = [];
}

/** 资料页路由参数 */
interface ContactProfileParams {
  contactJson: string;
}

@Entry
@Component
struct ContactsPage {
  /**
   * 共享单例：跨 Tab 复用已加载缓存与连接状态，不再每次都从头拉取。
   * 注意：@Observed 类（ImViewModel）本身不会触发页面刷新，
   * 必须用下面的 @State 镜像字段作为渲染依据，并在 aboutToAppear/onPageShow 同步一次。
   */
  vm: ImViewModel = ImViewModel.getInstance();
  /** 搜索关键字（昵称/备注/userId/群组名） */
  @State keyword: string = '';
  /** 按压中的联系人（按压态） */
  @State pressedId: number = -1;
  /** 群聊分组展开状态 */
  @State groupsExpanded: boolean = false;
  /** 索引条按压中（显示当前字母气泡） */
  @State indexTouching: boolean = false;
  /** 索引条当前字母（按压或滚动跟随） */
  @State currentLetter: string = '';
  /** 索引条可用（拼音 collation 自检通过） */
  @State pinyinReady: boolean = PinyinHelper.isAvailable();

  // ===== @Observed 类字段 UI 镜像（确保页面重绘）=====
  @State uiContactsLoading: boolean = true;
  @State uiContactsLoadError: string = '';
  @State uiContacts: ContactItem[] = [];
  @State uiNeedLogin: boolean = false;
  @State uiPendingRequests: PendingRequestItem[] = [];

  // ===== 添加好友弹窗 =====
  @State showAddDialog: boolean = false;
  @State addTargetId: string = '';
  @State addVerifyMessage: string = '';
  @State addSubmitting: boolean = false;

  // ===== 好友申请面板 =====
  @State showRequestsPanel: boolean = false;
  /** 正在处理的申请（按钮转圈/防重复提交） */
  @State handlingRequestId: number = -1;

  private scroller: Scroller = new Scroller();

  private syncFromVm(): void {
    // 把 @Observed 实例中的状态复制到 @State，强制触发页面刷新
    this.uiContactsLoading = this.vm.contactsLoading;
    this.uiContactsLoadError = this.vm.contactsLoadError;
    this.uiContacts = this.vm.contacts.slice();
    this.uiPendingRequests = this.vm.pendingRequests.slice();
  }

  private showToast(message: string): void {
    this.getUIContext().getPromptAction().showToast({ message: message, duration: 2000 });
  }

  async aboutToAppear(): Promise<void> {
    // 先等全局 AppContext 初始化完成（恢复本地持久化的登录会话），避免拿到默认值 0/''
    await AppContext.getInstance().whenReady();

    const app = AppContext.getInstance();
    // 前置门禁：未登录 / 缺少 IM 令牌 → 直接跳登录，不再做无谓的 45 秒重试
    if (!app.isSignedIn || app.imAuthToken.length === 0 || app.user.userId <= 0) {
      this.uiNeedLogin = true;
      this.uiContactsLoading = false;
      return;
    }
    this.uiNeedLogin = false;

    // 先同步一次当前缓存（VM 是单例，可能已经加载完）
    this.syncFromVm();

    // 订阅联系人变更通知：@Observed 实例挂在普通成员上时属性变更不会触发重绘，
    // 缓存上屏/刷新完成/在线状态推送都依赖该回调同步 @State 镜像
    this.vm.onContactsChanged = () => {
      this.syncFromVm();
    };
    this.vm.onPendingRequestsChanged = () => {
      this.syncFromVm();
    };
    this.vm.start();

    // 确保 start 完成后再同步一次（start 内部去重，重复调用只返回同一 Promise）
    try {
      await this.vm.start();
    } catch (_e) {
      // 错误已由 refreshContacts 写入 contactsLoadError
    } finally {
      this.syncFromVm();
    }
  }

  /** 从聊天详情返回时重新订阅推送（详情页 stop 会清空会话单例回调） */
  onPageShow(): void {
    const app = AppContext.getInstance();
    if (!app.isSignedIn || app.imAuthToken.length === 0 || app.user.userId <= 0) {
      this.uiNeedLogin = true;
      this.uiContactsLoading = false;
      return;
    }
    this.uiNeedLogin = false;
    // 从聊天详情返回时回调可能已被 stop() 清空，先重订再拉取；start() 内部幂等处理连接与拉取
    this.vm.onContactsChanged = () => {
      this.syncFromVm();
    };
    this.vm.onPendingRequestsChanged = () => {
      this.syncFromVm();
    };
    this.vm.start().finally(() => {
      this.syncFromVm();
    });
  }

  aboutToDisappear(): void {
    this.vm.stop();
  }

  private displayName(c: ContactItem): string {
    return c.remarkName.length > 0 ? c.remarkName : c.nickname;
  }

  /** 按关键字过滤（基于 @State 镜像渲染，确保 VM 变更经 syncFromVm 后能刷新） */
  private filtered(): ContactItem[] {
    const kw = this.keyword.trim().toLowerCase();
    if (kw.length === 0) {
      return this.uiContacts;
    }
    return this.uiContacts.filter((c: ContactItem) =>
      c.nickname.toLowerCase().includes(kw) ||
      c.remarkName.toLowerCase().includes(kw) ||
      c.groupName.toLowerCase().includes(kw) ||
      c.userId.toString().includes(kw));
  }

  private groupList(): ContactItem[] {
    return this.filtered().filter((c: ContactItem) => c.relation === 2);
  }

  /** 好友（非群组）按拼音首字母分组；collation 不可用归并为一组 */
  private buildModel(): ContactsListModel {
    const model = new ContactsListModel();
    const friends = this.filtered().filter((c: ContactItem) => c.relation !== 2);
    const sectionMap = new Map<string, ContactItem[]>();
    for (const c of friends) {
      const letter = PinyinHelper.groupLetter(this.displayName(c));
      const bucket = sectionMap.get(letter);
      if (bucket === undefined) {
        sectionMap.set(letter, [c]);
      } else {
        bucket.push(c);
      }
    }
    const letters = Array.from(sectionMap.keys());
    // 字母升序，'#' 兜底组最后
    letters.sort((a: string, b: string) => {
      if (a === '#' && b !== '#') {
        return 1;
      }
      if (b === '#' && a !== '#') {
        return -1;
      }
      return a < b ? -1 : 1;
    });
    for (const letter of letters) {
      const section = new ContactSection();
      section.letter = letter;
      section.contacts = sectionMap.get(letter) ?? [];
      model.sections.push(section);
    }
    model.letters = letters;

    // item 序号映射：0=新的朋友；群聊行/群组项；随后各分组（头 + 成员）
    let index = 0;
    model.itemLetters.push(''); // 新的朋友
    index += 1;
    if (this.groupList().length > 0) {
      model.itemLetters.push(''); // 群聊
      index += 1;
      if (this.groupsExpanded) {
        const groupCount = this.groupList().length;
        for (let i = 0; i < groupCount; i++) {
          model.itemLetters.push('');
        }
        index += groupCount;
      }
    }
    for (const section of model.sections) {
      model.letterToItemIndex.set(section.letter, index);
      model.itemLetters.push(section.letter);
      index += 1;
      for (let i = 0; i < section.contacts.length; i++) {
        model.itemLetters.push(section.letter);
        index += 1;
      }
    }
    return model;
  }

  /** 点击索引条字母：滚动到对应分组头并短暂显示字母气泡 */
  private jumpToLetter(letter: string): void {
    const model = this.buildModel();
    const target = model.letterToItemIndex.get(letter);
    if (target !== undefined) {
      this.scroller.scrollToIndex(target, true, ScrollAlign.START);
    }
    this.currentLetter = letter;
    this.indexTouching = true;
    setTimeout(() => {
      this.indexTouching = false;
    }, 600);
  }

  /** 列表滚动跟随：更新索引条当前字母 */
  private onListScrollIndex(first: number): void {
    const model = this.buildModel();
    if (first >= 0 && first < model.itemLetters.length) {
      const letter = model.itemLetters[first];
      if (letter.length > 0) {
        this.currentLetter = letter;
      }
    }
  }

  private statusText(s: number): string {
    switch (s) {
      case 1:
        return '在线';
      case 2:
        return '离开';
      case 3:
        return '忙碌';
      case 4:
        return '隐身';
      default:
        return '离线';
    }
  }

  /** 副标题：个性签名优先，其次在线状态 */
  private subTitle(c: ContactItem): string {
    if (c.signature && c.signature.trim().length > 0) {
      return c.signature;
    }
    return this.statusText(c.onlineStatus);
  }

  /** 进入联系人资料页（对标微信点击联系人 → 资料页） */
  private openProfile(c: ContactItem): void {
    this.getUIContext().getRouter().pushUrl({
      url: 'pages/ContactProfilePage',
      params: {
        contactJson: JSON.stringify({
          userId: c.userId,
          nickname: c.nickname,
          avatar: c.avatar,
          remarkName: c.remarkName,
          onlineStatus: c.onlineStatus,
          signature: c.signature,
          relation: c.relation,
          groupName: c.groupName
        })
      } as ContactProfileParams
    });
  }

  // ===== 添加好友 =====
  private openAddDialog(): void {
    this.addTargetId = '';
    this.addVerifyMessage = '';
    this.addSubmitting = false;
    this.showAddDialog = true;
  }

  private async submitAddFriend(): Promise<void> {
    const targetId = Number(this.addTargetId.trim());
    if (!Number.isInteger(targetId) || targetId <= 0) {
      this.showToast('请输入有效的用户 ID');
      return;
    }
    const selfId = AppContext.getInstance().user.userId;
    if (targetId === selfId) {
      this.showToast('不能添加自己为好友');
      return;
    }
    this.addSubmitting = true;
    try {
      const resp: ImContactAddResponse = await this.vm.addFriend(targetId, this.addVerifyMessage.trim());
      if (resp.success) {
        this.showAddDialog = false;
        this.showToast(resp.relation === ImContactRelation.PendingRequest
          ? '好友申请已发送，等待对方确认' : '好友申请已发送');
      } else {
        // 服务端业务失败（已是好友/目标不存在/超上限），弹窗保留输入供修正
        this.showToast(resp.message.length > 0 ? resp.message : '发送好友申请失败');
      }
    } catch (e) {
      this.showToast((e as Error).message || '发送好友申请失败，请检查网络');
    } finally {
      this.addSubmitting = false;
    }
  }

  // ===== 好友申请处理 =====
  private openRequestsPanel(): void {
    this.showRequestsPanel = true;
    this.vm.refreshPendingRequests();
  }

  private async handleRequest(p: PendingRequestItem, accept: boolean): Promise<void> {
    if (this.handlingRequestId === p.requesterId) {
      return;
    }
    this.handlingRequestId = p.requesterId;
    try {
      await this.vm.handleFriendRequest(p.requesterId, accept);
      this.syncFromVm();
      this.showToast(accept ? `已添加 ${p.requesterName || '对方'} 为好友` : '已拒绝好友申请');
    } catch (e) {
      this.showToast((e as Error).message || '操作失败，请重试');
    } finally {
      this.handlingRequestId = -1;
    }
  }

  /** 申请时间显示（今天显示时间，更早显示日期） */
  private requestTimeText(ts: number): string {
    if (ts <= 0) {
      return '';
    }
    const d = new Date(ts);
    const now = new Date();
    const pad = (n: number): string => (n < 10 ? `0${n}` : `${n}`);
    if (d.toDateString() === now.toDateString()) {
      return `${pad(d.getHours())}:${pad(d.getMinutes())}`;
    }
    return `${d.getMonth() + 1}月${d.getDate()}日`;
  }

  @Builder
  contactRow(c: ContactItem) {
    Row({ space: 12 }) {
      Stack({ alignContent: Alignment.BottomEnd }) {
        ChatAvatar({
          name: this.displayName(c),
          avatar: c.avatar,
          userId: c.userId,
          isGroup: c.relation === 2,
          avatarSize: 40
        })
        if (c.relation !== 2 && c.onlineStatus === 1) {
          Circle({ width: 10, height: 10 })
            .fill(AppTheme.up)
            .stroke(AppTheme.chatDotStroke)
            .strokeWidth(2)
            .margin(1)
        }
      }
      .width(40)
      .height(40)

      Text(this.displayName(c))
        .fontSize(16)
        .fontColor(AppTheme.text)
        .maxLines(1)
        .textOverflow({ overflow: TextOverflow.Ellipsis })
        .layoutWeight(1)

      Text(this.subTitle(c))
        .fontSize(12)
        .fontColor(AppTheme.textSecondary)
        .maxLines(1)
        .textOverflow({ overflow: TextOverflow.Ellipsis })
        .margin({ left: 8 })
    }
    .width('100%')
    .constraintSize({ minHeight: 56 })
    .padding({ left: 16, right: 16, top: 8, bottom: 8 })
    .backgroundColor(this.pressedId === c.userId ? AppTheme.chatPressBg : Color.Transparent)
    .onClick(() => this.openProfile(c))
    .onTouch((event: TouchEvent) => {
      if (event.type === TouchType.Down) {
        this.pressedId = c.userId;
      } else if (event.type === TouchType.Up || event.type === TouchType.Cancel) {
        this.pressedId = -1;
      }
    })
  }

  /** 功能入口行（新的朋友 / 群聊） */
  @Builder
  functionRow(icon: string, label: string, badgeCount: number, expanded: boolean) {
    Row({ space: 12 }) {
      Text(icon)
        .fontSize(20)
        .width(40)
        .height(40)
        .textAlign(TextAlign.Center)
        .borderRadius(8)
        .backgroundColor(AppTheme.activeBg)
      Text(label)
        .fontSize(16)
        .fontColor(AppTheme.text)
        .layoutWeight(1)
      if (badgeCount > 0) {
        Text(badgeCount > 99 ? '99+' : `${badgeCount}`)
          .fontSize(11)
          .fontColor('#ffffff')
          .textAlign(TextAlign.Center)
          .constraintSize({ minWidth: 18 })
          .height(18)
          .borderRadius(9)
          .padding({ left: 5, right: 5 })
          .backgroundColor(AppTheme.badge)
      }
      if (expanded) {
        Text('▲')
          .fontSize(12)
          .fontColor(AppTheme.textSecondary)
      } else {
        Text('›')
          .fontSize(18)
          .fontColor(AppTheme.textSecondary)
      }
    }
    .width('100%')
    .constraintSize({ minHeight: 56 })
    .padding({ left: 16, right: 16, top: 8, bottom: 8 })
    .backgroundColor(this.pressedId === -2 ? AppTheme.chatPressBg : Color.Transparent)
  }

  /** 分组字母头 */
  @Builder
  sectionHeader(letter: string, count: number) {
    Row() {
      Text(letter)
        .fontSize(13)
        .fontWeight(FontWeight.Medium)
        .fontColor(AppTheme.primary)
      Text(`(${count})`)
        .fontSize(11)
        .fontColor(AppTheme.textSecondary)
        .margin({ left: 4 })
    }
    .width('100%')
    .padding({ left: 16, top: 8, bottom: 6 })
    .backgroundColor(AppTheme.pinnedBg)
  }

  build() {
    Stack() {
      Row() {
        Column() {
          // ========== 顶部标题栏 ==========
          Row({ space: 14 }) {
            Text('通讯录')
              .fontSize(20)
              .fontColor(AppTheme.text)
              .fontWeight(600)
            Blank()
            Text(`${this.uiContacts.length} 位好友`)
              .fontSize(11)
              .fontColor(AppTheme.textSecondary)
            // 好友申请入口（信封 + 未处理数量徽标）
            Stack({ alignContent: Alignment.TopEnd }) {
              SymbolGlyph($r('sys.symbol.envelope'))
                .fontSize(21)
                .fontColor([this.uiPendingRequests.length > 0 ? AppTheme.primary : AppTheme.textSecondary])
              if (this.uiPendingRequests.length > 0) {
                Text(this.uiPendingRequests.length > 99 ? '99+' : `${this.uiPendingRequests.length}`)
                  .fontSize(9)
                  .fontColor(Color.White)
                  .backgroundColor(AppTheme.badge)
                  .borderRadius(8)
                  .constraintSize({ minWidth: 15 })
                  .padding({ left: 3, right: 3, top: 1, bottom: 1 })
                  .offset({ x: 7, y: -7 })
              }
            }
            .width(26)
            .height(26)
            .onClick(() => this.openRequestsPanel())

            // 添加好友按钮
            SymbolGlyph($r('sys.symbol.plus'))
              .fontSize(22)
              .fontColor([AppTheme.primary])
              .onClick(() => this.openAddDialog())
          }
          .width('100%')
          .height(52)
          .padding({ left: 16, right: 16 })
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

          // ========== 联系人列表（微信：功能入口 + 拼音分组 + 右侧索引条） ==========
          if (this.uiContactsLoading) {
            LoadingView({ message: '加载联系人…' })
          } else if (this.uiNeedLogin) {
            EmptyView({ message: '请先登录' })
          } else if (this.uiContactsLoadError.length > 0 && this.uiContacts.length === 0) {
            ErrorView({
              message: this.uiContactsLoadError,
              onRetry: () => {
                this.vm.refreshContacts();
              }
            })
          } else if (this.uiContacts.length === 0) {
            EmptyView({ message: '暂无联系人，去发起添加好友吧' })
          } else if (this.filtered().length === 0) {
            EmptyView({ message: '未找到相关联系人' })
          } else {
            Row() {
              // 列表主体
              List({ scroller: this.scroller }) {
                // 功能入口：新的朋友（对标微信）
                ListItem() {
                  this.functionRow('👋', '新的朋友', this.uiPendingRequests.length, false)
                }
                .onClick(() => this.openRequestsPanel())

                // 功能入口：群聊（可展开群组列表）
                if (this.groupList().length > 0) {
                  ListItem() {
                    this.functionRow('👥', '群聊', this.groupList().length, this.groupsExpanded)
                  }
                  .onClick(() => {
                    this.groupsExpanded = !this.groupsExpanded;
                  })

                  if (this.groupsExpanded) {
                    ForEach(this.groupList(), (c: ContactItem) => {
                      ListItem() {
                        this.contactRow(c)
                      }
                    }, (c: ContactItem) => `g-${c.userId}-${c.onlineStatus}`)
                  }
                }

                // 好友拼音分组
                ForEach(this.buildModel().sections, (section: ContactSection) => {
                  ListItem() {
                    this.sectionHeader(section.letter, section.contacts.length)
                  }
                  ForEach(section.contacts, (c: ContactItem) => {
                    ListItem() {
                      this.contactRow(c)
                    }
                  }, (c: ContactItem) => `${section.letter}-${c.userId}-${c.onlineStatus}`)
                }, (section: ContactSection) => `s-${section.letter}-${section.contacts.length}`)
              }
              .layoutWeight(1)
              .width('100%')
              .scrollBar(BarState.Off)
              .edgeEffect(EdgeEffect.Spring)
              .divider({
                strokeWidth: 1,
                color: AppTheme.chatBorderFaint,
                startMargin: 68,
                endMargin: 16
              })
              .onScrollIndex((first: number) => {
                this.onListScrollIndex(first);
              })

              // 右侧字母索引条（对标微信；拼音不可用时隐藏）
              if (this.pinyinReady) {
                Column() {
                  ForEach(this.buildModel().letters, (letter: string) => {
                    Text(letter)
                      .fontSize(10)
                      .fontColor(this.currentLetter === letter ? AppTheme.primary : AppTheme.textSecondary)
                      .fontWeight(this.currentLetter === letter ? FontWeight.Bold : FontWeight.Normal)
                      .textAlign(TextAlign.Center)
                      .width(18)
                      .height(16)
                      .onClick(() => {
                        this.jumpToLetter(letter);
                      })
                  }, (letter: string) => `idx-${letter}`)
                }
                .padding({ top: 60, bottom: 60 })
                .margin({ right: 2 })
                .alignItems(HorizontalAlign.Center)
              }
            }
            .width('100%')
            .layoutWeight(1)
          }
        }
        .width('100%')
        .height('100%')

        // ===== 索引条按压时的当前字母气泡（居中提示） =====
        if (this.indexTouching && this.currentLetter.length > 0) {
          Text(this.currentLetter)
            .fontSize(44)
            .fontWeight(FontWeight.Bold)
            .fontColor(Color.White)
            .width(84)
            .height(84)
            .textAlign(TextAlign.Center)
            .borderRadius(20)
            .backgroundColor('rgba(45,212,191,0.85)')
      }
      }
      .width('100%')
      .height('100%')

      // ===== 弹窗层（后声明在上层）=====
      if (this.showAddDialog) {
        this.addFriendDialog()
      }
      if (this.showRequestsPanel) {
        this.requestsPanel()
      }
    }
    .width('100%')
    .height('100%')
    .backgroundColor(AppTheme.bg)
  }

  // ========== 添加好友弹窗 ==========
  @Builder
  addFriendDialog() {
    Stack() {
      // 半透明遮罩：点击关闭（提交中禁用关闭防误触）
      Column()
        .width('100%')
        .height('100%')
        .backgroundColor('rgba(0,0,0,0.55)')
        .onClick(() => {
          if (!this.addSubmitting) {
            this.showAddDialog = false;
          }
        })

      Column({ space: 14 }) {
        Text('添加好友')
          .fontSize(17)
          .fontWeight(FontWeight.Medium)
          .fontColor(AppTheme.text)

        Column({ space: 6 }) {
          TextInput({ placeholder: '对方用户 ID', text: this.addTargetId })
            .type(InputType.Number)
            .height(44)
            .fontSize(14)
            .fontColor(AppTheme.text)
            .placeholderColor(AppTheme.textSecondary)
            .backgroundColor('rgba(255,255,255,0.06)')
            .borderRadius(10)
            .padding({ left: 12, right: 12 })
            .enabled(!this.addSubmitting)
            .onChange((v: string) => {
              this.addTargetId = v;
            })
          Text('输入对方的数字用户 ID 发送好友申请，待对方确认后成为好友')
            .fontSize(11)
            .fontColor(AppTheme.textSecondary)
        }
        .alignItems(HorizontalAlign.Start)

        TextInput({ placeholder: '验证消息（选填）', text: this.addVerifyMessage })
          .height(44)
          .fontSize(14)
          .fontColor(AppTheme.text)
          .placeholderColor(AppTheme.textSecondary)
          .backgroundColor('rgba(255,255,255,0.06)')
          .borderRadius(10)
          .padding({ left: 12, right: 12 })
          .enabled(!this.addSubmitting)
          .maxLength(50)
          .onChange((v: string) => {
            this.addVerifyMessage = v;
          })

        Row({ space: 10 }) {
          Button('取消')
            .fontSize(14)
            .fontColor(AppTheme.textSecondary)
            .backgroundColor('rgba(255,255,255,0.06)')
            .borderRadius(10)
            .layoutWeight(1)
            .height(42)
            .enabled(!this.addSubmitting)
            .onClick(() => {
              this.showAddDialog = false;
            })
          Button(this.addSubmitting ? '发送中…' : '发送申请')
            .fontSize(14)
            .fontColor(Color.White)
            .backgroundColor(AppTheme.primary)
            .borderRadius(10)
            .layoutWeight(1)
            .height(42)
            .enabled(!this.addSubmitting && this.addTargetId.trim().length > 0)
            .onClick(() => {
              this.submitAddFriend();
            })
        }
        .width('100%')
        .margin({ top: 4 })
      }
      .width('86%')
      .padding(20)
      .borderRadius(16)
      .backgroundColor(AppTheme.sheet)
      .border({ width: 1, color: AppTheme.chatBorderFaint })
      .onClick(() => {
        // 空处理器：拦截点击，避免穿透到遮罩关闭弹窗
      })
    }
    .width('100%')
    .height('100%')
  }

  // ========== 好友申请面板 ==========
  @Builder
  requestsPanel() {
    Stack() {
      Column()
        .width('100%')
        .height('100%')
        .backgroundColor('rgba(0,0,0,0.55)')
        .onClick(() => {
          this.showRequestsPanel = false;
        })

      Column() {
        Row({ space: 10 }) {
          Text('新的朋友')
            .fontSize(17)
            .fontWeight(FontWeight.Medium)
            .fontColor(AppTheme.text)
          Blank()
          Text(this.uiPendingRequests.length > 0 ? `${this.uiPendingRequests.length} 条待处理` : '')
            .fontSize(11)
            .fontColor(AppTheme.textSecondary)
          Text('关闭')
            .fontSize(13)
            .fontColor(AppTheme.textSecondary)
            .padding({ left: 8, top: 4, bottom: 4 })
            .onClick(() => {
              this.showRequestsPanel = false;
            })
        }
        .width('100%')
        .padding({ left: 18, right: 14, top: 16, bottom: 12 })

        if (this.uiPendingRequests.length === 0) {
          Column({ space: 8 }) {
            Text(this.vm.pendingRequestsLoading ? '正在加载…' : '暂无待处理的好友申请')
              .fontSize(13)
              .fontColor(AppTheme.textSecondary)
          }
          .width('100%')
          .constraintSize({ minHeight: 120 })
          .justifyContent(FlexAlign.Center)
        } else {
          List({ space: 2 }) {
            ForEach(this.uiPendingRequests, (p: PendingRequestItem) => {
              ListItem() {
                this.requestItem(p)
              }
            }, (p: PendingRequestItem) => `req-${p.requesterId}-${p.timestamp}`)
          }
          .width('100%')
          .padding({ left: 10, right: 10, bottom: 10 })
          .scrollBar(BarState.Off)
        }
      }
      .width('88%')
      .constraintSize({ maxHeight: '68%' })
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

  @Builder
  requestItem(p: PendingRequestItem) {
    Row({ space: 12 }) {
      ChatAvatar({
        name: p.requesterName.length > 0 ? p.requesterName : `${p.requesterId}`,
        avatar: '',
        userId: p.requesterId,
        isGroup: false,
        avatarSize: 44
      })

      Column({ space: 3 }) {
        Text(p.requesterName.length > 0 ? p.requesterName : `用户 ${p.requesterId}`)
          .fontSize(14)
          .fontColor(AppTheme.text)
          .fontWeight(FontWeight.Medium)
          .maxLines(1)
          .textOverflow({ overflow: TextOverflow.Ellipsis })
        Text(p.message.length > 0 ? p.message : '请求添加你为好友')
          .fontSize(12)
          .fontColor(AppTheme.textSecondary)
          .maxLines(1)
          .textOverflow({ overflow: TextOverflow.Ellipsis })
        if (p.timestamp > 0) {
          Text(this.requestTimeText(p.timestamp))
            .fontSize(10)
            .fontColor(AppTheme.textSecondary)
        }
      }
      .layoutWeight(1)
      .alignItems(HorizontalAlign.Start)

      if (this.handlingRequestId === p.requesterId) {
        LoadingProgress()
          .width(20)
          .height(20)
          .color(AppTheme.primary)
      } else {
        Column({ space: 6 }) {
          Text('接受')
            .fontSize(12)
            .fontColor(Color.White)
            .backgroundColor(AppTheme.primary)
            .borderRadius(12)
            .padding({ left: 14, right: 14, top: 5, bottom: 5 })
            .onClick(() => {
              this.handleRequest(p, true);
            })
          Text('拒绝')
            .fontSize(12)
            .fontColor(AppTheme.textSecondary)
            .borderRadius(12)
            .border({ width: 1, color: AppTheme.divider })
            .padding({ left: 14, right: 14, top: 4, bottom: 4 })
            .onClick(() => {
              this.handleRequest(p, false);
            })
        }
        .alignItems(HorizontalAlign.Center)
      }
    }
    .width('100%')
    .constraintSize({ minHeight: 64 })
    .padding({ left: 8, right: 8, top: 10, bottom: 10 })
    .borderRadius(12)
  }
}
