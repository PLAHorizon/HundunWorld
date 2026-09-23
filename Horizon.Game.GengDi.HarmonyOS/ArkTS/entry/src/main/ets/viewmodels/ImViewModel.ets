import {
  ImPrivateChatNotify, ImGroupChatNotify, ImChatAck, ImChatRecallMessage, ImTypingIndicator,
  ImContactOnlineStatus, ImSystemNotification, ImConversationInfo,
  ImOnlineStatus, ImChatHistoryQueryResponse,
  ImContactAddResponse, ImContactRemoveResponse, ImContactRequestHandleResponse,
  ImContactInfo, ImContactRelation
} from '../services/im/ImMessages';
import { ImSession } from '../services/im/ImSession';
import { ImLocalStore, LocalImMessage, LocalConversation, LocalContact } from '../services/im/ImLocalStore';
import { AppContext } from '../model/AppContext';
import { RichMessageContentSerializer } from '../model/RichMessageContent';
import { hilog } from '@kit.PerformanceAnalysisKit';

/**
 * IM 聊天 ViewModel（MVVM 核心）。
 * 集中管理连接状态、会话集合、未读数、消息流；
 * 页面只做数据绑定与动作转发。
 */

const TAG = 'GengDi.ImViewModel';

/** 会话条目视图模型 */
@Observed
export class ConversationItem {
  conversationId: string = '';
  peerId: number = 0;
  displayName: string = '';
  avatar: string = '';
  lastMessageSummary: string = '';
  lastMessageTime: number = 0;
  unreadCount: number = 0;
  isPinned: boolean = false;
  isMuted: boolean = false;
  isGroup: boolean = false;
}

/** 消息条目视图模型 */
@Observed
export class ChatMessageItem {
  messageId: string = '';
  clientMessageId: string = '';
  conversationId: string = '';
  senderId: number = 0;
  senderName: string = '';
  content: string = '';
  contentType: number = 0;
  timestamp: number = 0;
  isSelf: boolean = false;
  /** 0 发送中 / 1 已发送 / 2 已送达 / 3 已读 / 4 失败 */
  status: number = 0;
}

/** 联系人条目视图模型 */
@Observed
export class ContactItem {
  userId: number = 0;
  nickname: string = '';
  avatar: string = '';
  remarkName: string = '';
  onlineStatus: number = ImOnlineStatus.Offline;
  /** 个性签名 */
  signature: string = '';
  /** 关系：0 好友 / 1 陌生人 / 2 群组 */
  relation: number = 0;
  /** 群组显示名（relation=2 时有效） */
  groupName: string = '';
}

/** 联系人分组结果 */
@Observed
export class GroupedContacts {
  online: ContactItem[] = [];
  offline: ContactItem[] = [];
  groups: ContactItem[] = [];
}

/** 待处理好友申请条目视图模型 */
@Observed
export class PendingRequestItem {
  requesterId: number = 0;
  requesterName: string = '';
  message: string = '';
  timestamp: number = 0;
}

@Observed
export class ImViewModel {
  /** 连接状态 */
  connected: boolean = false;
  /** 会话列表（置顶在前，其次按最近消息时间倒序） */
  conversations: ConversationItem[] = [];
  /** 当前打开会话的消息流 */
  messages: ChatMessageItem[] = [];
  /** 当前会话 ID 与对方信息 */
  currentConversationId: string = '';
  currentPeerId: number = 0;
  currentPeerName: string = '';
  currentIsGroup: boolean = false;
  /** 对方正在输入 */
  peerTyping: boolean = false;
  /** 联系人 */
  contacts: ContactItem[] = [];
  /** 联系人是否正在加载（与会话加载解耦，避免互相覆盖 loading/错误文案） */
  contactsLoading: boolean = false;
  contactsLoadError: string = '';
  /** 列表加载态 */
  loading: boolean = false;
  loadError: string = '';
  /** 总未读数（用于入口红点） */
  totalUnread: number = 0;
  /** 联系人搜索关键字（实时过滤） */
  searchKeyword: string = '';
  /** 当前会话消息流变更回调（聊天详情页订阅，用于自动滚底） */
  onMessagesChanged: (() => void) | null = null;
  /**
   * 会话集合变更回调（会话列表页订阅）。
   * @Observed 实例挂在页面普通成员上时属性变更不会触发重绘，
   * 列表刷新/置顶/删除/未读变化必须主动通知页面同步 @State 镜像。
   */
  onConversationsChanged: (() => void) | null = null;
  /**
   * 联系人集合/加载状态变更回调（联系人页订阅）。
   * @Observed 实例挂在页面普通成员上时属性变更不会触发重绘，
   * 刷新完成（含 finally）必须主动通知页面同步 @State 镜像，否则会永久停留在"加载中"。
   */
  onContactsChanged: (() => void) | null = null;
  /** 待处理好友申请 */
  pendingRequests: PendingRequestItem[] = [];
  /** 待处理好友申请是否正在加载 */
  pendingRequestsLoading: boolean = false;
  /** 待处理好友申请集合变更回调（联系人页订阅，用于刷新申请面板/徽标） */
  onPendingRequestsChanged: (() => void) | null = null;

  private typingResetTimer: number = -1;
  private store: ImLocalStore = ImLocalStore.getInstance();
  private session: ImSession = ImSession.getInstance();
  /** start() 进行中 Promise（重复调用返回同一 Promise），避免 Tab 并发拉起串行化 */
  private starting: Promise<void> | null = null;

  private static _instance: ImViewModel | null = null;
  /**
   * 共享单例：联系人/会话页/聊天详情页跨页面共用状态，避免每次进 Tab 重新 new 导致
   * 1) 已加载缓存丢失，2) Tab 切换时重复触发 ensureConnected/拉取导致界面长时间显示加载中。
   */
  static getInstance(): ImViewModel {
    if (!ImViewModel._instance) {
      ImViewModel._instance = new ImViewModel();
    }
    return ImViewModel._instance;
  }

  /** 订阅推送并启动连接（页面 aboutToAppear/onPageShow 都可调用，内部幂等） */
  async start(): Promise<void> {
    // 先重设回调（每次进入页面都可能被其他页 stop 清空），回调重写本身是幂等的。
    this.session.onConnectionChanged = (connected: boolean) => {
      this.connected = connected;
    };
    this.session.onPrivateChatReceived = (m: ImPrivateChatNotify) => {
      this.onPrivateNotify(m);
    };
    this.session.onGroupChatReceived = (m: ImGroupChatNotify) => {
      this.onGroupNotify(m);
    };
    this.session.onChatAckReceived = (m: ImChatAck) => {
      this.onAck(m);
    };
    this.session.onRecallReceived = (m: ImChatRecallMessage) => {
      this.onRecall(m);
    };
    this.session.onTypingReceived = (m: ImTypingIndicator) => {
      const selfId = BigInt(AppContext.getInstance().user.userId);
      // peerId 是"正在输入"指示的接收者，应该是当前用户
      if (BigInt(m.peerId) === selfId) {
        this.peerTyping = m.isTyping;
        if (this.typingResetTimer >= 0) {
          clearTimeout(this.typingResetTimer);
        }
        this.typingResetTimer = setTimeout(() => {
          this.peerTyping = false;
        }, 4000);
      }
    };
    this.session.onContactStatusReceived = (m: ImContactOnlineStatus) => {
      this.updateContactStatus(Number(m.userId), m.onlineStatus);
    };
    this.session.onSystemNotificationReceived = (m: ImSystemNotification) => {
      this.onSystemNotification(m);
    };

    if (this.starting !== null) {
      await this.starting;
      return;
    }
    this.starting = this.startInternal();
    try {
      await this.starting;
    } finally {
      this.starting = null;
    }
  }

  private async startInternal(): Promise<void> {
    // 旧版本地数据用 p:{a}:{b} 冒号格式会话 ID，与服务端 p_{peerId} 不一致，先迁移再拉数据
    await this.store.migrateLegacyConversationIds(AppContext.getInstance().user.userId);
    await this.session.ensureConnected();
    // 会话、联系人、待处理好友申请并行拉取（三者互不依赖）
    await Promise.all([this.refreshConversations(), this.refreshContacts(), this.refreshPendingRequests()]);
  }

  stop(): void {
    this.session.onConnectionChanged = null;
    this.session.onPrivateChatReceived = null;
    this.session.onGroupChatReceived = null;
    this.session.onChatAckReceived = null;
    this.session.onRecallReceived = null;
    this.session.onTypingReceived = null;
    this.session.onContactStatusReceived = null;
    this.session.onSystemNotificationReceived = null;
    this.onMessagesChanged = null;
    this.onConversationsChanged = null;
    this.onContactsChanged = null;
    this.onPendingRequestsChanged = null;
  }

  /**
   * 系统通知处理（服务端 IMUserGrain 推送）：
   * - "新的好友申请"（含被拒通知）→ 刷新待处理申请列表
   * - "好友列表已更新"（对方接受了申请）→ 刷新联系人列表
   */
  private onSystemNotification(m: ImSystemNotification): void {
    hilog.info(0x0001, TAG, '系统通知: %{public}s', m.title);
    if (m.title.includes('好友申请')) {
      this.refreshPendingRequests();
    } else if (m.title.includes('好友列表')) {
      this.refreshContacts();
    }
  }

  /** 刷新会话列表：本地缓存 → 服务端合并 */
  async refreshConversations(): Promise<void> {
    this.loading = true;
    this.loadError = '';
    try {
      const cached = await this.store.getConversations();
      if (cached.length > 0) {
        this.applyConversations(cached.map((c: LocalConversation) => this.toConvItem(c)));
      }
      const resp = await this.session.getConversationListAsync();
      const items = resp.conversations.map((c: ImConversationInfo) => {
        const item = new ConversationItem();
        item.conversationId = c.conversationId;
        item.peerId = Number(c.peerId);
        item.displayName = c.displayName;
        item.avatar = c.avatar;
        item.lastMessageSummary = c.lastMessageSummary;
        item.lastMessageTime = c.lastMessageTime;
        item.unreadCount = c.unreadCount;
        item.isPinned = c.isPinned;
        item.isMuted = c.isMuted;
        item.isGroup = c.chatRelationType === 2;
        return item;
      });
      this.applyConversations(items);
      for (const c of resp.conversations) {
        await this.store.upsertConversation({
          conversationId: c.conversationId,
          peerId: Number(c.peerId),
          displayName: c.displayName,
          avatar: c.avatar,
          lastMessageSummary: c.lastMessageSummary,
          lastMessageTime: c.lastMessageTime,
          unreadCount: c.unreadCount,
          isPinned: c.isPinned,
          isMuted: c.isMuted
        } as LocalConversation);
      }
    } catch (e) {
      this.loadError = (e as Error).message;
    } finally {
      this.loading = false;
      // 无论成功/失败都通知页面同步镜像（loading → false 需要上屏）
      this.onConversationsChanged?.();
    }
  }

  private toConvItem(c: LocalConversation): ConversationItem {
    const item = new ConversationItem();
    item.conversationId = c.conversationId;
    item.peerId = c.peerId;
    item.displayName = c.displayName;
    item.avatar = c.avatar;
    item.lastMessageSummary = c.lastMessageSummary;
    item.lastMessageTime = c.lastMessageTime;
    item.unreadCount = c.unreadCount;
    item.isPinned = c.isPinned;
    item.isMuted = c.isMuted;
    item.isGroup = c.conversationId.startsWith('g_');
    return item;
  }

  private applyConversations(items: ConversationItem[]): void {
    items.sort((a: ConversationItem, b: ConversationItem) => {
      if (a.isPinned !== b.isPinned) {
        return a.isPinned ? -1 : 1;
      }
      return b.lastMessageTime - a.lastMessageTime;
    });
    this.conversations = items;
    this.totalUnread = items.reduce((sum: number, c: ConversationItem) => sum + c.unreadCount, 0);
    this.onConversationsChanged?.();
  }

  async refreshContacts(): Promise<void> {
    // 幂等：已经在拉取中就跳过，避免“加载中”卡死（Tab 重复进入/回调风暴）
    if (this.contactsLoading) {
      return;
    }
    this.contactsLoading = true;
    this.contactsLoadError = '';

    // 步骤 1：先读取本地缓存立马上屏，用户不会看到长时间空列表 loading。
    try {
      const cached = await this.store.getContacts();
      if (cached && cached.length > 0 && this.contacts.length === 0) {
        this.contacts = cached.map((c: LocalContact) => {
          const item = new ContactItem();
          item.userId = c.userId;
          item.nickname = c.nickname ?? '';
          item.avatar = c.avatar ?? '';
          item.remarkName = c.remarkName ?? '';
          item.onlineStatus = c.onlineStatus;
          item.signature = c.signature ?? '';
          item.relation = 0;
          item.groupName = '';
          return item;
        });
        // 缓存上屏也要通知页面，避免远端请求超时期间 UI 一直空白/转圈
        this.onContactsChanged?.();
      }
    } catch (e) {
      hilog.warn(0x0001, TAG, '联系人本地缓存读取失败: %{public}s', (e as Error).message);
    }

    // 步骤 2：拉远端，响应成功/失败/超时均写错误状态，避免永远显示 loading。
    try {
      const resp = await this.session.getContactListAsync();
      const raw = resp?.contacts ?? [];
      const items: ContactItem[] = raw.map((c) => {
        const item = new ContactItem();
        item.userId = Number(c.userId);
        item.nickname = c.nickname ?? '';
        item.avatar = c.avatar ?? '';
        item.remarkName = c.remarkName ?? '';
        item.onlineStatus = c.onlineStatus;
        item.signature = c.signature ?? '';
        item.relation = c.relation ?? 0;
        item.groupName = c.groupName ?? '';
        return item;
      });
      this.contacts = items;
      hilog.info(0x0001, TAG, '联系人刷新成功: userId=%{public}d, 数量=%{public}d (服务端总数 %{public}d)',
        AppContext.getInstance().user.userId, items.length, resp?.totalCount ?? -1);
      try {
        await this.store.upsertContacts(raw.map((c) => {
          const lc = new LocalContact();
          lc.userId = Number(c.userId);
          lc.nickname = c.nickname ?? '';
          lc.avatar = c.avatar ?? '';
          lc.remarkName = c.remarkName ?? '';
          lc.onlineStatus = c.onlineStatus;
          lc.signature = c.signature ?? '';
          return lc;
        }));
      } catch (e) {
        hilog.warn(0x0001, TAG, '联系人写入缓存失败: %{public}s', (e as Error).message);
      }
    } catch (e) {
      const msg = (e as Error)?.message ?? '拉取联系人失败';
      hilog.warn(0x0001, TAG, '联系人刷新失败: %{public}s', msg);
      // 本地没有任何兜底数据时才把错误抛给 UI；有缓存继续显示缓存（离线友好）
      if (this.contacts.length === 0) {
        this.contactsLoadError = msg;
      }
    } finally {
      this.contactsLoading = false;
      // 无论成功/失败/超时都通知页面同步镜像，避免 UI 停留在"加载中"
      this.onContactsChanged?.();
    }
  }

  /** 发送好友申请（服务端返回结果；申请需对方确认后才成为好友） */
  async addFriend(targetUserId: number, verifyMessage: string): Promise<ImContactAddResponse> {
    return await this.session.addContactAsync(BigInt(targetUserId), verifyMessage, '');
  }

  /** 删除好友：服务端解除关系 → 本地列表与缓存同步移除（聊天记录/会话保留） */
  async removeFriend(userId: number): Promise<boolean> {
    const resp: ImContactRemoveResponse = await this.session.removeContactAsync(BigInt(userId));
    if (!resp.success) {
      throw new Error(resp.message.length > 0 ? resp.message : '删除好友失败');
    }
    this.contacts = this.contacts.filter((c: ContactItem) => c.userId !== userId);
    this.onContactsChanged?.();
    try {
      await this.store.deleteContact(userId);
    } catch (e) {
      hilog.warn(0x0001, TAG, '本地联系人缓存删除失败: %{public}s', (e as Error).message);
    }
    return true;
  }

  /** 拉取待处理好友申请列表 */
  async refreshPendingRequests(): Promise<void> {
    // 幂等：已在拉取中就跳过
    if (this.pendingRequestsLoading) {
      return;
    }
    this.pendingRequestsLoading = true;
    try {
      const resp = await this.session.getPendingContactRequestsAsync();
      const items: PendingRequestItem[] = (resp?.pendingRequests ?? []).map((p) => {
        const item = new PendingRequestItem();
        item.requesterId = Number(p.requesterId);
        item.requesterName = p.requesterName;
        item.message = p.message;
        item.timestamp = p.timestamp;
        return item;
      });
      this.pendingRequests = items;
    } catch (e) {
      hilog.warn(0x0001, TAG, '待处理好友申请拉取失败: %{public}s', (e as Error).message);
    } finally {
      this.pendingRequestsLoading = false;
      this.onPendingRequestsChanged?.();
    }
  }

  /** 处理好友申请（accept=true 接受 / false 拒绝），返回服务端响应 */
  async handleFriendRequest(requesterId: number, accept: boolean): Promise<ImContactRequestHandleResponse> {
    const resp: ImContactRequestHandleResponse =
      await this.session.handleContactRequestAsync(BigInt(requesterId), accept);
    if (!resp.success) {
      throw new Error(resp.message.length > 0 ? resp.message : '处理好友申请失败');
    }
    // 从待处理列表移除（重新赋值触发页面刷新）
    this.pendingRequests = this.pendingRequests.filter((p: PendingRequestItem) => p.requesterId !== requesterId);
    this.onPendingRequestsChanged?.();
    if (accept) {
      // 接受成功：新联系人立即上屏（响应携带 NewContact），并同步本地缓存
      const info: ImContactInfo | null = resp.newContact;
      if (info !== null) {
        const item = new ContactItem();
        item.userId = Number(info.userId);
        item.nickname = info.nickname;
        item.avatar = info.avatar;
        item.remarkName = info.remarkName;
        item.onlineStatus = info.onlineStatus;
        item.signature = info.signature;
        item.relation = info.relation === ImContactRelation.Friend ? 0 : info.relation;
        item.groupName = info.groupName;
        // 去重合并（服务端联系人刷新可能并发写入）
        const merged = this.contacts.filter((c: ContactItem) => c.userId !== item.userId);
        merged.push(item);
        this.contacts = merged;
        this.onContactsChanged?.();
        try {
          const lc = new LocalContact();
          lc.userId = item.userId;
          lc.nickname = item.nickname;
          lc.avatar = item.avatar;
          lc.remarkName = item.remarkName;
          lc.onlineStatus = item.onlineStatus;
          lc.signature = item.signature;
          await this.store.upsertContacts([lc]);
        } catch (e) {
          hilog.warn(0x0001, TAG, '新好友写入缓存失败: %{public}s', (e as Error).message);
        }
      } else {
        // 响应未携带资料：直接全量刷新联系人
        this.refreshContacts();
      }
    }
    return resp;
  }

  /** 按 searchKeyword 过滤联系人（昵称/备注名/通行证号/userId） */
  get filteredContacts(): ContactItem[] {
    const kw = this.searchKeyword.trim().toLowerCase();
    if (kw.length === 0) {
      return this.contacts;
    }
    const result: ContactItem[] = [];
    for (const c of this.contacts) {
      const nick = c.nickname.toLowerCase();
      const remark = c.remarkName.toLowerCase();
      const group = c.groupName.toLowerCase();
      const uid = c.userId.toString();
      if (nick.includes(kw) || remark.includes(kw) || group.includes(kw) || uid.includes(kw)) {
        result.push(c);
      }
    }
    return result;
  }

  /** 联系人分组：在线好友 / 离线好友 / 群组 */
  get groupedContacts(): GroupedContacts {
    const g = new GroupedContacts();
    for (const c of this.filteredContacts) {
      if (c.relation === 2) {
        g.groups.push(c);
      } else if (c.onlineStatus === ImOnlineStatus.Online) {
        g.online.push(c);
      } else {
        g.offline.push(c);
      }
    }
    return g;
  }

  /** 清空指定会话：清未读 + 清空列表摘要 */
  async clearConversation(conversationId: string): Promise<void> {
    await this.store.clearUnread(conversationId);
    for (const c of this.conversations) {
      if (c.conversationId === conversationId) {
        c.unreadCount = 0;
        c.lastMessageSummary = '';
      }
    }
    this.applyConversations(this.conversations.slice());
  }

  // ===== 会话管理（对标微信消息列表操作；服务端成功不回包，本地乐观更新 + 落库，服务端状态由下次列表刷新校准） =====

  /** 置顶/取消置顶会话 */
  async setConversationPinned(item: ConversationItem, isPinned: boolean): Promise<void> {
    item.isPinned = isPinned;
    this.applyConversations(this.conversations.slice());
    try {
      await this.store.updateConversationFlags(item.conversationId, isPinned, item.isMuted);
    } catch (e) {
      hilog.warn(0x0001, TAG, '置顶状态写缓存失败: %{public}s', (e as Error).message);
    }
    this.session.pinConversationAsync(item.conversationId, isPinned).catch((e: Error) => {
      hilog.warn(0x0001, TAG, '置顶状态同步服务端失败: %{public}s', e.message);
    });
  }

  /** 会话免打扰开关 */
  async setConversationMuted(item: ConversationItem, isMuted: boolean): Promise<void> {
    item.isMuted = isMuted;
    this.applyConversations(this.conversations.slice());
    try {
      await this.store.updateConversationFlags(item.conversationId, item.isPinned, isMuted);
    } catch (e) {
      hilog.warn(0x0001, TAG, '免打扰状态写缓存失败: %{public}s', (e as Error).message);
    }
    this.session.muteConversationAsync(item.conversationId, isMuted).catch((e: Error) => {
      hilog.warn(0x0001, TAG, '免打扰状态同步服务端失败: %{public}s', e.message);
    });
  }

  /** 删除会话（对标微信"删除该聊天"：列表移除 + 本地消息记录一并删除） */
  async deleteConversation(item: ConversationItem): Promise<void> {
    this.conversations = this.conversations.filter((c: ConversationItem) => c.conversationId !== item.conversationId);
    this.totalUnread = this.conversations.reduce((s: number, c: ConversationItem) => s + c.unreadCount, 0);
    this.onConversationsChanged?.();
    try {
      await this.store.deleteConversation(item.conversationId);
    } catch (e) {
      hilog.warn(0x0001, TAG, '会话本地删除失败: %{public}s', (e as Error).message);
    }
    this.session.deleteConversationAsync(item.conversationId).catch((e: Error) => {
      hilog.warn(0x0001, TAG, '会话删除同步服务端失败: %{public}s', e.message);
    });
  }

  /** 标为未读（本地生效，未读数置 1；服务端无对应接口，下次列表刷新会以服务端为准） */
  async markConversationUnread(item: ConversationItem): Promise<void> {
    if (item.unreadCount !== 1) {
      item.unreadCount = 1;
      this.applyConversations(this.conversations.slice());
    }
    await this.store.setUnreadCount(item.conversationId, 1);
  }

  /** 标为已读（清未读） */
  async markConversationRead(item: ConversationItem): Promise<void> {
    if (item.unreadCount !== 0) {
      item.unreadCount = 0;
      this.applyConversations(this.conversations.slice());
    }
    await this.store.setUnreadCount(item.conversationId, 0);
  }

  /** 删除单条本地消息（聊天详情页长按删除；仅本地有效，重进不复活） */
  async deleteLocalMessage(m: ChatMessageItem): Promise<void> {
    const key = m.messageId.length > 0 ? m.messageId : m.clientMessageId;
    this.messages = this.messages.filter((x: ChatMessageItem) => x !== m);
    this.onMessagesChanged?.();
    if (key.length > 0) {
      await this.store.deleteMessage(key);
      await this.store.addDeletedMessage(key);
    }
  }

  private updateContactStatus(userId: number, status: number): void {
    let changed = false;
    for (const c of this.contacts) {
      if (c.userId === userId && c.onlineStatus !== status) {
        c.onlineStatus = status;
        changed = true;
      }
    }
    if (changed) {
      // 重新赋值触发联系人列表/在线状态点刷新
      this.contacts = this.contacts.slice();
      this.onContactsChanged?.();
    }
  }

  /** 会话切换令牌：防止快速切换会话时过期响应污染新会话的消息列表 */
  private conversationToken: number = 0;

  /** 打开会话：先显缓存，再拉服务端历史合并 */
  async openConversation(item: ConversationItem): Promise<void> {
    const token = ++this.conversationToken;
    this.currentConversationId = item.conversationId;
    this.currentPeerId = item.peerId;
    this.currentPeerName = item.displayName;
    this.currentIsGroup = item.isGroup;
    this.messages = [];
    this.peerTyping = false;
    this.onMessagesChanged?.();

    const cached = await this.store.getMessages(item.conversationId, 100);
    if (token !== this.conversationToken) {
      return; // 会话已切换，丢弃过期结果
    }
    this.messages = cached.map((m: LocalImMessage) => this.toMessageItem(m));
    this.onMessagesChanged?.();

    // 本地已删除消息黑名单：服务端历史回拉时过滤，保证本地删除不复活
    const deletedKeys: Set<string> = await this.store.getDeletedMessageKeys();

    try {
      const resp: ImChatHistoryQueryResponse = await this.session.getPrivateHistoryAsync(
        item.conversationId, BigInt(item.peerId), '', 50);
      if (token !== this.conversationToken) {
        return; // 会话已切换，丢弃过期响应
      }
      const fresh = resp.privateMessages.filter((m: ImPrivateChatNotify) =>
        !(m.serverMessageId.length > 0 && deletedKeys.has(m.serverMessageId)));
      const serverItems: ChatMessageItem[] = fresh.map((m: ImPrivateChatNotify) => {
        const it = new ChatMessageItem();
        it.messageId = m.serverMessageId;
        it.conversationId = item.conversationId;
        it.senderId = Number(m.senderId);
        it.senderName = m.senderName;
        it.content = m.content;
        it.contentType = m.contentType;
        it.timestamp = m.timestamp;
        it.isSelf = BigInt(m.senderId) === BigInt(AppContext.getInstance().user.userId);
        it.status = 3;
        return it;
      });
      this.mergeMessages(serverItems);
      for (const m of fresh) {
        await this.store.insertMessage({
          serverMessageId: m.serverMessageId,
          clientMessageId: '',
          conversationId: item.conversationId,
          senderId: Number(m.senderId),
          senderName: m.senderName,
          content: m.content,
          contentType: m.contentType,
          timestamp: m.timestamp,
          isSelf: BigInt(m.senderId) === BigInt(AppContext.getInstance().user.userId),
          status: 3
        } as LocalImMessage);
      }
    } catch (e) {
      hilog.warn(0x0001, TAG, '历史拉取失败（仅显示本地缓存）: %{public}s', (e as Error).message);
    }

    // 清未读
    item.unreadCount = 0;
    this.totalUnread = this.conversations.reduce((s: number, c: ConversationItem) => s + c.unreadCount, 0);
    this.onConversationsChanged?.();
    await this.store.clearUnread(item.conversationId);
  }

  private toMessageItem(m: LocalImMessage): ChatMessageItem {
    const it = new ChatMessageItem();
    it.messageId = m.serverMessageId;
    it.clientMessageId = m.clientMessageId;
    it.conversationId = m.conversationId;
    it.senderId = m.senderId;
    it.senderName = m.senderName;
    it.content = m.content;
    it.contentType = m.contentType;
    it.timestamp = m.timestamp;
    it.isSelf = m.isSelf;
    it.status = m.status;
    return it;
  }

  /** 按 server/client 消息 ID 去重合并（时间升序；重新赋值触发 UI 刷新） */
  private mergeMessages(incoming: ChatMessageItem[]): void {
    const seen = new Set<string>();
    for (const m of this.messages) {
      if (m.messageId.length > 0) {
        seen.add(m.messageId);
      } else if (m.clientMessageId.length > 0) {
        seen.add(m.clientMessageId);
      }
    }
    let appended = false;
    for (const m of incoming) {
      const key = m.messageId.length > 0 ? m.messageId : m.clientMessageId;
      if (!seen.has(key)) {
        this.messages.push(m);
        seen.add(key);
        appended = true;
      }
    }
    if (appended) {
      this.messages = this.messages.slice().sort((a: ChatMessageItem, b: ChatMessageItem) => a.timestamp - b.timestamp);
      this.onMessagesChanged?.();
    }
  }

  /** 发送消息：乐观上屏 + Ack 确认（contentType 见 ImContentType，大表情消息传 7） */
  async sendMessage(content: string, contentType: number = 0): Promise<void> {
    if (content.trim().length === 0 || this.currentConversationId.length === 0) {
      return;
    }
    const clientMessageId = `c-${Date.now()}-${Math.floor(Math.random() * 10000)}`;
    const optimistic = new ChatMessageItem();
    optimistic.clientMessageId = clientMessageId;
    optimistic.conversationId = this.currentConversationId;
    optimistic.senderId = AppContext.getInstance().user.userId;
    optimistic.senderName = AppContext.getInstance().user.nickname;
    optimistic.content = content;
    optimistic.contentType = contentType;
    optimistic.timestamp = Date.now();
    optimistic.isSelf = true;
    optimistic.status = 0;
    this.messages = this.messages.concat([optimistic]);
    this.onMessagesChanged?.();

    await this.store.insertMessage({
      serverMessageId: '',
      clientMessageId: clientMessageId,
      conversationId: this.currentConversationId,
      senderId: optimistic.senderId,
      senderName: optimistic.senderName,
      content: content,
      contentType: contentType,
      timestamp: optimistic.timestamp,
      isSelf: true,
      status: 0
    } as LocalImMessage);

    try {
      if (this.currentIsGroup) {
        await this.session.sendGroupChatAsync(BigInt(this.currentPeerId), content, clientMessageId, contentType);
      } else {
        await this.session.sendPrivateChatAsync(BigInt(this.currentPeerId), content, clientMessageId, contentType);
      }
      // 防降级：若服务端回推（notify）先到已置为送达/已读，不回退为"已发送"
      if (optimistic.status < 1) {
        optimistic.status = 1;
      }
      this.messages = this.messages.slice();
      this.onMessagesChanged?.();
      await this.store.updateMessageStatus(clientMessageId, 1);
    } catch (e) {
      optimistic.status = 4;
      this.messages = this.messages.slice();
      this.onMessagesChanged?.();
      await this.store.updateMessageStatus(clientMessageId, 4);
      hilog.error(0x0001, TAG, '发送失败: %{public}s', (e as Error).message);
    }
  }

  /** 收到私聊推送 */
  private onPrivateNotify(m: ImPrivateChatNotify): void {
    const selfId = BigInt(AppContext.getInstance().user.userId);
    const senderId = BigInt(m.senderId);
    const isSelf = senderId === selfId;
    // 会话对端：自己发送的同步消息对端是 receiverId，别人发来的对端是 senderId。
    // 服务端多端同步 senderNotify 未携带 conversationId，若误用 senderId(selfId) 会算出 p:{self}:{self} 错会话。
    const peerForConversation = isSelf ? Number(m.receiverId) : Number(m.senderId);
    const conversationId = m.extData.get('conversationId') ?? this.peerConversationId(peerForConversation);

    const item = new ChatMessageItem();
    item.messageId = m.serverMessageId;
    item.clientMessageId = m.clientMessageId ?? '';
    item.conversationId = conversationId;
    item.senderId = Number(m.senderId);
    item.senderName = m.senderName;
    item.content = m.content;
    item.contentType = m.contentType;
    item.timestamp = m.timestamp;
    item.isSelf = isSelf;
    item.status = 3;

    // 多端同步：自己发送的消息被服务端回推（可能是当前设备的乐观上屏确认，也可能是其他设备发送的同步消息）
    if (isSelf && conversationId === this.currentConversationId) {
      // 优先尝试匹配当前设备的乐观上屏消息
      const updated = this.updateOptimisticMessage(m.clientMessageId ?? '', m.serverMessageId);
      if (updated) {
        return;
      }
      // 未匹配到乐观消息：说明消息来自其他设备，作为新消息添加
      this.mergeMessages([item]);
    } else if (conversationId === this.currentConversationId) {
      this.mergeMessages([item]);
      if (!isSelf) {
        this.session.sendReadReceiptAsync(BigInt(m.senderId), m.serverMessageId).catch(() => {
        });
      }
    } else {
      this.bumpConversation(conversationId, peerForConversation, item);
    }

    this.store.insertMessage({
      serverMessageId: m.serverMessageId,
      clientMessageId: isSelf ? (m.clientMessageId ?? '') : '',
      conversationId: conversationId,
      senderId: Number(m.senderId),
      senderName: m.senderName,
      content: m.content,
      contentType: m.contentType,
      timestamp: m.timestamp,
      isSelf: isSelf,
      status: 3
    } as LocalImMessage).catch(() => {
    });
  }

  /** 用服务端回推的 serverMessageId 更新乐观上屏消息，返回是否匹配成功 */
  private updateOptimisticMessage(clientMessageId: string, serverMessageId: string): boolean {
    if (!serverMessageId || serverMessageId.length === 0) {
      return false;
    }
    let changed = false;
    for (const msg of this.messages) {
      if (clientMessageId && msg.clientMessageId === clientMessageId && msg.status < 2) {
        msg.messageId = serverMessageId;
        msg.status = 2;
        changed = true;
        break;
      }
    }
    if (!changed && (!clientMessageId || clientMessageId.length === 0)) {
      const now = Date.now();
      for (const msg of this.messages) {
        if (msg.isSelf && msg.status < 2 && !msg.messageId &&
          msg.timestamp > now - 10000 && msg.timestamp < now + 5000) {
          msg.messageId = serverMessageId;
          msg.status = 2;
          changed = true;
          break;
        }
      }
    }
    if (changed) {
      this.messages = this.messages.slice();
      this.onMessagesChanged?.();
      const cid = clientMessageId && clientMessageId.length > 0
        ? clientMessageId
        : (this.messages.find((m: ChatMessageItem) => m.messageId === serverMessageId)?.clientMessageId ?? '');
      if (cid && cid.length > 0) {
        this.store.updateMessageServerId(cid, serverMessageId, 2).catch(() => {
        });
      }
    }
    return changed;
  }

  private onGroupNotify(m: ImGroupChatNotify): void {
    const selfId = BigInt(AppContext.getInstance().user.userId);
    const isSelf = BigInt(m.senderId) === selfId;
    // 与服务端群会话 ID 格式一致（g_{groupId}）
    const conversationId = `g_${m.groupId}`;
    const item = new ChatMessageItem();
    item.messageId = m.serverMessageId;
    item.clientMessageId = m.clientMessageId ?? '';
    item.conversationId = conversationId;
    item.senderId = Number(m.senderId);
    item.senderName = m.senderName;
    item.content = m.content;
    item.contentType = m.contentType;
    item.timestamp = m.timestamp;
    item.isSelf = isSelf;
    item.status = 3;

    // 多端同步：自己发送的消息被服务端回推（可能是当前设备的乐观上屏确认，也可能是其他设备发送的同步消息）
    if (isSelf && conversationId === this.currentConversationId) {
      const updated = this.updateOptimisticMessage(m.clientMessageId ?? '', m.serverMessageId);
      if (updated) {
        return;
      }
      // 未匹配到乐观消息：说明消息来自其他设备，作为新消息添加
      this.mergeMessages([item]);
    } else if (conversationId === this.currentConversationId) {
      this.mergeMessages([item]);
    } else {
      this.bumpConversation(conversationId, Number(m.groupId), item);
    }

    this.store.insertMessage({
      serverMessageId: m.serverMessageId,
      clientMessageId: isSelf ? (m.clientMessageId ?? '') : '',
      conversationId: conversationId,
      senderId: Number(m.senderId),
      senderName: m.senderName,
      content: m.content,
      contentType: m.contentType,
      timestamp: m.timestamp,
      isSelf: isSelf,
      status: 3
    } as LocalImMessage).catch(() => {
    });
  }

  private onAck(m: ImChatAck): void {
    let changed = false;
    for (const msg of this.messages) {
      if ((msg.clientMessageId === m.ackedMessageId || msg.messageId === m.ackedMessageId) && msg.status !== m.status) {
        msg.status = Math.max(msg.status, m.status);
        changed = true;
      }
    }
    if (changed) {
      this.messages = this.messages.slice();
      this.onMessagesChanged?.();
    }
    this.store.updateMessageStatus(m.ackedMessageId, m.status).catch(() => {
    });
  }

  // ===== 消息撤回（全平台；服务端 48 小时窗口校验） =====

  /** 撤回时长窗口（毫秒），与服务端 RecallTimeWindowHours=48 对齐 */
  private static readonly RECALL_WINDOW_MS: number = 48 * 60 * 60 * 1000;

  /** 消息是否可撤回：自己发送、已获服务端消息ID、私聊、48 小时内 */
  canRecall(m: ChatMessageItem): boolean {
    return m.isSelf
      && m.messageId.length > 0
      && !this.currentIsGroup
      && Date.now() - m.timestamp <= ImViewModel.RECALL_WINDOW_MS;
  }

  /**
   * 撤回消息：发送撤回请求（fire-and-forget，服务端成功不回包，
   * 生效通知经 onRecall 推送回本端完成 UI 替换；超窗时服务端拒绝、本地不变）。
   */
  recallMessage(m: ChatMessageItem): void {
    if (!this.canRecall(m)) {
      return;
    }
    this.session.sendRecallAsync(m.messageId, BigInt(this.currentPeerId), false).catch((e: Error) => {
      hilog.warn(0x0001, TAG, '发送撤回请求失败: %{public}s', e.message);
    });
  }

  /** 收到撤回推送（自己撤回的多端同步 / 对方撤回）：替换当前会话消息 + 落库 + 更新列表摘要 */
  private onRecall(m: ImChatRecallMessage): void {
    const selfId = BigInt(AppContext.getInstance().user.userId);
    const isSelfRecall = BigInt(m.userId) === selfId;
    const placeholder = isSelfRecall ? '你撤回了一条消息' : '对方撤回了一条消息';
    // 推导撤回事件所属会话：撤回者是对方 → p_{对方}；自己多端撤回 → p_{对端（接收方）}
    const peerForConversation = isSelfRecall ? Number(m.peerId) : Number(m.userId);
    const conversationId = `p_${peerForConversation}`;

    // 当前会话：替换消息为撤回占位
    if (conversationId === this.currentConversationId) {
      let changed = false;
      for (const msg of this.messages) {
        if (msg.messageId === m.recalledMessageId) {
          msg.status = 5;
          msg.content = placeholder;
          msg.contentType = 0;
          changed = true;
          break;
        }
      }
      if (changed) {
        this.messages = this.messages.slice();
        this.onMessagesChanged?.();
      }
    }

    // 更新会话列表摘要（被撤回消息为最后一条时）
    for (const c of this.conversations) {
      if (c.conversationId === conversationId && c.lastMessageTime <= m.timestamp) {
        c.lastMessageSummary = '[消息已撤回]';
        break;
      }
    }
    this.onConversationsChanged?.();

    this.store.markMessageRecalled(m.recalledMessageId, placeholder).catch(() => {
    });
  }

  /** 未打开会话收到新消息：更新列表摘要与未读 */
  private bumpConversation(conversationId: string, peerId: number, item: ChatMessageItem): void {
    let conv: ConversationItem | null = null;
    for (const c of this.conversations) {
      if (c.conversationId === conversationId) {
        conv = c;
        break;
      }
    }
    if (conv === null) {
      conv = new ConversationItem();
      conv.conversationId = conversationId;
      // 对端 ID：多端同步的自发消息对端是 receiverId（由调用方传入），不能取 senderId（是自己）
      conv.peerId = peerId;
      conv.displayName = item.senderId === peerId ? item.senderName : '';
      conv.isGroup = conversationId.startsWith('g_');
      this.conversations.push(conv);
    }
    conv.lastMessageSummary = this.summaryOf(item);
    conv.lastMessageTime = item.timestamp;
    conv.unreadCount += 1;
    this.applyConversations(this.conversations.slice());
  }

  /** 消息 → 会话列表摘要（富文本消息显示"[图片]/[位置] xxx"等文案，避免裸 JSON 上列表） */
  private summaryOf(item: ChatMessageItem): string {
    try {
      const display = RichMessageContentSerializer
        .deserialize(item.contentType, item.content || '')
        .getDisplayText();
      return display.slice(0, 30);
    } catch (_e) {
      return (item.content || '').slice(0, 30);
    }
  }

  private peerConversationId(peerId: number): string {
    // 必须与服务端 IMUserGrain 的会话 ID 格式一致（p_{对端ID}），
    // 否则推送消息推导出的会话与当前打开会话对不上，导致消息进不了聊天页/重复落库
    return `p_${peerId}`;
  }

  /** 发送正在输入指示 */
  sendTyping(): void {
    if (!this.currentIsGroup && this.currentPeerId > 0) {
      this.session.sendTypingAsync(BigInt(this.currentPeerId), true).catch(() => {
      });
    }
  }
}
