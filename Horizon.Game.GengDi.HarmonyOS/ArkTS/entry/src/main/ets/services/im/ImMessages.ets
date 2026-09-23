import type { MemoryPackReader, MemoryPackWriter } from './codec/MemoryPackCodec';
/**
 * IM 消息模型与 MemoryPack 编解码。
 *
 * 字段顺序与 C# Horizon.IM.Message 中 [MemoryPackOrder] 的数值序一致，
 * union tag 与 IMMessageUnion.cs 的 [MemoryPackUnion] 标签一致，
 * 字节布局已经由 tools/ImMemoryPackFixtures 对拍验证。
 * 本文件不依赖 @ohos 模块。
 */
// ===== 枚举常量（IMMessageType : ushort）=====
export class ImMessageType {
    static readonly PrivateChatSend: number = 1;
    static readonly PrivateChatNotify: number = 2;
    static readonly ChatAck: number = 3;
    static readonly ChatRecall: number = 4;
    static readonly ChatReadReceipt: number = 5;
    static readonly TypingIndicator: number = 6;
    static readonly GroupChatSend: number = 100;
    static readonly GroupChatNotify: number = 101;
    static readonly StrangerChatNotify: number = 203;
    static readonly ContactAddRequest: number = 300;
    static readonly ContactAddResponse: number = 301;
    static readonly ContactRemoveRequest: number = 302;
    static readonly ContactRemoveResponse: number = 303;
    static readonly ContactListRequest: number = 306;
    static readonly ContactListResponse: number = 307;
    static readonly ContactPendingListRequest: number = 311;
    static readonly ContactPendingListResponse: number = 312;
    static readonly ContactRequestHandleRequest: number = 313;
    static readonly ContactRequestHandleResponse: number = 314;
    static readonly ContactOnlineStatus: number = 310;
    static readonly ConversationListRequest: number = 400;
    static readonly ConversationListResponse: number = 401;
    static readonly ConversationDelete: number = 402;
    static readonly ConversationPin: number = 403;
    static readonly ConversationMute: number = 404;
    static readonly ChatHistoryQuery: number = 500;
    static readonly ChatHistoryQueryResponse: number = 501;
    static readonly ChatHistoryClear: number = 502;
    static readonly CallSignal: number = 600;
    static readonly CallSignalAck: number = 601;
    static readonly Heartbeat: number = 900;
    static readonly HeartbeatResponse: number = 901;
    static readonly Error: number = 902;
    static readonly SystemNotification: number = 903;
}
/** IMServiceType : byte */
export class ImServiceType {
    static readonly Gateway: number = 1;
    static readonly Chat: number = 2;
    static readonly Contact: number = 3;
    static readonly Notification: number = 4;
    static readonly Group: number = 5;
    static readonly Call: number = 7;
}
/** IMContentType / IMChatRelationType / IMOnlineStatus / IMMessageStatus（均为 int 枚举） */
export class ImContentType {
    static readonly Text: number = 0;
    static readonly Image: number = 1;
    static readonly Audio: number = 2;
    static readonly Video: number = 3;
    static readonly File: number = 4;
    static readonly Location: number = 5;
    static readonly Emoji: number = 7;
}
/** IMCallType / IMCallSignalType / IMCallEndReason（服务端 IMEnums.cs，int 枚举） */
export class ImCallType {
    static readonly Audio: number = 0;
    static readonly Video: number = 1;
}
export class ImCallSignalType {
    static readonly Offer: number = 0;
    static readonly Accept: number = 1;
    static readonly Reject: number = 2;
    static readonly Cancel: number = 3;
    static readonly Busy: number = 4;
    static readonly Hangup: number = 5;
    static readonly MediaReady: number = 6;
    static readonly KeepAlive: number = 7;
    static readonly MediaState: number = 8;
    static readonly Timeout: number = 9;
}
export class ImCallEndReason {
    static readonly Normal: number = 0;
    static readonly Rejected: number = 1;
    static readonly Busy: number = 2;
    static readonly Timeout: number = 3;
    static readonly Cancelled: number = 4;
    static readonly Lost: number = 5;
    static readonly DeviceError: number = 6;
}
export class ImChatRelationType {
    static readonly Friend: number = 0;
    static readonly Stranger: number = 1;
    static readonly Group: number = 2;
}
export class ImOnlineStatus {
    static readonly Offline: number = 0;
    static readonly Online: number = 1;
    static readonly Away: number = 2;
    static readonly Busy: number = 3;
    static readonly Invisible: number = 4;
}
export class ImMessageStatus {
    static readonly Sending: number = 0;
    static readonly Sent: number = 1;
    static readonly Delivered: number = 2;
    static readonly Read: number = 3;
    static readonly Failed: number = 4;
}
/** IMContactRelation : int（服务端 IMEnums.cs） */
export class ImContactRelation {
    static readonly None: number = 0;
    static readonly PendingRequest: number = 1;
    static readonly Friend: number = 2;
}
/** union 标签（IMMessageUnion.cs 的 [MemoryPackUnion]） */
export class ImUnionTag {
    static readonly PrivateChatSend: number = 0;
    static readonly PrivateChatNotify: number = 1;
    static readonly ChatAck: number = 2;
    static readonly ChatRecall: number = 3;
    static readonly ChatReadReceipt: number = 4;
    static readonly TypingIndicator: number = 5;
    static readonly GroupChatSend: number = 6;
    static readonly GroupChatNotify: number = 7;
    static readonly ContactAddRequest: number = 23;
    static readonly ContactAddResponse: number = 24;
    static readonly ContactRemoveRequest: number = 25;
    static readonly ContactRemoveResponse: number = 26;
    static readonly ContactListRequest: number = 29;
    static readonly ContactListResponse: number = 30;
    static readonly ContactOnlineStatus: number = 33;
    static readonly ContactRequestHandleRequest: number = 51;
    static readonly ContactRequestHandleResponse: number = 52;
    static readonly ContactPendingListRequest: number = 53;
    static readonly ContactPendingListResponse: number = 54;
    static readonly ConversationListRequest: number = 34;
    static readonly ConversationListResponse: number = 35;
    static readonly ConversationDelete: number = 36;
    static readonly ConversationPin: number = 37;
    static readonly ConversationMute: number = 38;
    static readonly ChatHistoryQueryRequest: number = 39;
    static readonly ChatHistoryQueryResponse: number = 40;
    static readonly ChatHistoryClear: number = 41;
    static readonly Heartbeat: number = 42;
    static readonly HeartbeatResponse: number = 43;
    static readonly Error: number = 44;
    static readonly SystemNotification: number = 45;
    static readonly CallSignal: number = 72;
    static readonly CallSignalAck: number = 73;
}
// ===== 公共编解码小工具 =====
function writeStringList(w: MemoryPackWriter, list: Array<string> | null): void {
    if (list === null) {
        w.writeCollectionHeader(-1);
        return;
    }
    w.writeCollectionHeader(list.length);
    for (const s of list) {
        w.writeString(s);
    }
}
function readStringList(r: MemoryPackReader): Array<string> {
    const count = r.readCollectionHeader();
    const list: Array<string> = [];
    for (let i = 0; i < count; i++) {
        const s = r.readString();
        list.push(s === null ? '' : s);
    }
    return list;
}
function writeU64List(w: MemoryPackWriter, list: Array<bigint> | null): void {
    if (list === null) {
        w.writeCollectionHeader(-1);
        return;
    }
    w.writeCollectionHeader(list.length);
    for (const v of list) {
        w.writeUInt64(v);
    }
}
function readU64List(r: MemoryPackReader): Array<bigint> {
    const count = r.readCollectionHeader();
    const list: Array<bigint> = [];
    for (let i = 0; i < count; i++) {
        list.push(r.readUInt64());
    }
    return list;
}
function writeStrDict(w: MemoryPackWriter, dict: Map<string, string> | null): void {
    if (dict === null) {
        w.writeCollectionHeader(-1);
        return;
    }
    w.writeCollectionHeader(dict.size);
    dict.forEach((v: string, k: string) => {
        w.writeString(k);
        w.writeString(v);
    });
}
function readStrDict(r: MemoryPackReader): Map<string, string> {
    const count = r.readCollectionHeader();
    const map = new Map<string, string>();
    for (let i = 0; i < count; i++) {
        const k = r.readString();
        const v = r.readString();
        map.set(k === null ? '' : k, v === null ? '' : v);
    }
    return map;
}
// ===== 消息头 =====
export class ImHeader {
    userId: bigint = 0n;
    messageId: string = '';
    sequenceId: number = 0;
    timestamp: number = 0;
    messageType: number = 0;
    serviceType: number = 0;
    hash: string = '';
    flags: number = 0;
    extensionData: Map<string, string> = new Map();
    requireResponse: boolean = false;
    isResponse: boolean = false;
    responseToMessageId: string = '';
    priority: number = 0;
    version: number = 1;
    authToken: string = '';
    machineId: string = '';
    write(w: MemoryPackWriter): void {
        w.writeObjectHeader(16);
        w.writeUInt64(this.userId);
        w.writeString(this.messageId);
        w.writeI64(this.sequenceId);
        w.writeI64(this.timestamp);
        w.writeU16(this.messageType);
        w.writeU8(this.serviceType);
        w.writeString(this.hash);
        w.writeU32(this.flags);
        writeStrDict(w, this.extensionData);
        w.writeBool(this.requireResponse);
        w.writeBool(this.isResponse);
        w.writeString(this.responseToMessageId);
        w.writeU8(this.priority);
        w.writeU8(this.version);
        w.writeString(this.authToken);
        w.writeString(this.machineId);
    }
    static read(r: MemoryPackReader): ImHeader {
        const h = new ImHeader();
        r.readObjectHeader();
        h.userId = r.readUInt64();
        h.messageId = r.readString() ?? '';
        h.sequenceId = r.readI64();
        h.timestamp = r.readI64();
        h.messageType = r.readU16();
        h.serviceType = r.readU8();
        h.hash = r.readString() ?? '';
        h.flags = r.readU32();
        h.extensionData = readStrDict(r);
        h.requireResponse = r.readBool();
        h.isResponse = r.readBool();
        h.responseToMessageId = r.readString() ?? '';
        h.priority = r.readU8();
        h.version = r.readU8();
        h.authToken = r.readString() ?? '';
        h.machineId = r.readString() ?? '';
        return h;
    }
}
// ===== 消息体 =====
export class ImPrivateChatSend {
    senderId: bigint = 0n;
    senderName: string = '';
    senderAvatar: string = '';
    receiverId: bigint = 0n;
    content: string = '';
    contentType: number = ImContentType.Text;
    clientMessageId: string = '';
    timestamp: number = 0;
    attachments: Array<string> = [];
    extData: Map<string, string> = new Map();
    type: number = ImMessageType.PrivateChatSend;
    serviceType: number = ImServiceType.Chat;
    write(w: MemoryPackWriter): void {
        w.writeObjectHeader(12);
        w.writeUInt64(this.senderId);
        w.writeString(this.senderName);
        w.writeString(this.senderAvatar);
        w.writeUInt64(this.receiverId);
        w.writeString(this.content);
        w.writeI32(this.contentType);
        w.writeString(this.clientMessageId);
        w.writeI64(this.timestamp);
        writeStringList(w, this.attachments);
        writeStrDict(w, this.extData);
        w.writeU16(this.type);
        w.writeU8(this.serviceType);
    }
    static read(r: MemoryPackReader): ImPrivateChatSend {
        const m = new ImPrivateChatSend();
        r.readObjectHeader();
        m.senderId = r.readUInt64();
        m.senderName = r.readString() ?? '';
        m.senderAvatar = r.readString() ?? '';
        m.receiverId = r.readUInt64();
        m.content = r.readString() ?? '';
        m.contentType = r.readI32();
        m.clientMessageId = r.readString() ?? '';
        m.timestamp = r.readI64();
        m.attachments = readStringList(r);
        m.extData = readStrDict(r);
        m.type = r.readU16();
        m.serviceType = r.readU8();
        return m;
    }
}
export class ImPrivateChatNotify {
    serverMessageId: string = '';
    senderId: bigint = 0n;
    senderName: string = '';
    senderAvatar: string = '';
    receiverId: bigint = 0n;
    content: string = '';
    contentType: number = ImContentType.Text;
    timestamp: number = 0;
    attachments: Array<string> = [];
    extData: Map<string, string> = new Map();
    type: number = ImMessageType.PrivateChatNotify;
    serviceType: number = ImServiceType.Chat;
    clientMessageId: string = '';
    write(w: MemoryPackWriter): void {
        w.writeObjectHeader(13);
        w.writeString(this.serverMessageId);
        w.writeUInt64(this.senderId);
        w.writeString(this.senderName);
        w.writeString(this.senderAvatar);
        w.writeUInt64(this.receiverId);
        w.writeString(this.content);
        w.writeI32(this.contentType);
        w.writeI64(this.timestamp);
        writeStringList(w, this.attachments);
        writeStrDict(w, this.extData);
        w.writeU16(this.type);
        w.writeU8(this.serviceType);
        w.writeString(this.clientMessageId);
    }
    static read(r: MemoryPackReader): ImPrivateChatNotify {
        const m = new ImPrivateChatNotify();
        r.readObjectHeader();
        m.serverMessageId = r.readString() ?? '';
        m.senderId = r.readUInt64();
        m.senderName = r.readString() ?? '';
        m.senderAvatar = r.readString() ?? '';
        m.receiverId = r.readUInt64();
        m.content = r.readString() ?? '';
        m.contentType = r.readI32();
        m.timestamp = r.readI64();
        m.attachments = readStringList(r);
        m.extData = readStrDict(r);
        m.type = r.readU16();
        m.serviceType = r.readU8();
        m.clientMessageId = r.readString() ?? '';
        return m;
    }
}
export class ImChatAck {
    ackedMessageId: string = '';
    status: number = ImMessageStatus.Sent;
    timestamp: number = 0;
    userId: bigint = 0n;
    type: number = ImMessageType.ChatAck;
    serviceType: number = ImServiceType.Chat;
    write(w: MemoryPackWriter): void {
        w.writeObjectHeader(6);
        w.writeString(this.ackedMessageId);
        w.writeI32(this.status);
        w.writeI64(this.timestamp);
        w.writeUInt64(this.userId);
        w.writeU16(this.type);
        w.writeU8(this.serviceType);
    }
    static read(r: MemoryPackReader): ImChatAck {
        const m = new ImChatAck();
        r.readObjectHeader();
        m.ackedMessageId = r.readString() ?? '';
        m.status = r.readI32();
        m.timestamp = r.readI64();
        m.userId = r.readUInt64();
        m.type = r.readU16();
        m.serviceType = r.readU8();
        return m;
    }
}
export class ImChatRecallMessage {
    /** 被撤回的消息ID（serverMessageId） */
    recalledMessageId: string = '';
    /** 撤回者用户ID */
    userId: bigint = 0n;
    /** 会话对方ID（私聊时为对方用户ID，群聊时为群组ID） */
    peerId: bigint = 0n;
    /** 聊天关系类型 */
    chatRelationType: number = ImChatRelationType.Friend;
    /** 撤回时间戳 */
    timestamp: number = 0;
    type: number = ImMessageType.ChatRecall;
    serviceType: number = ImServiceType.Chat;
    write(w: MemoryPackWriter): void {
        w.writeObjectHeader(7);
        w.writeString(this.recalledMessageId);
        w.writeUInt64(this.userId);
        w.writeUInt64(this.peerId);
        w.writeI32(this.chatRelationType);
        w.writeI64(this.timestamp);
        w.writeU16(this.type);
        w.writeU8(this.serviceType);
    }
    static read(r: MemoryPackReader): ImChatRecallMessage {
        const m = new ImChatRecallMessage();
        r.readObjectHeader();
        m.recalledMessageId = r.readString() ?? '';
        m.userId = r.readUInt64();
        m.peerId = r.readUInt64();
        m.chatRelationType = r.readI32();
        m.timestamp = r.readI64();
        m.type = r.readU16();
        m.serviceType = r.readU8();
        return m;
    }
}
export class ImChatReadReceipt {
    userId: bigint = 0n;
    peerId: bigint = 0n;
    lastReadMessageId: string = '';
    timestamp: number = 0;
    type: number = ImMessageType.ChatReadReceipt;
    serviceType: number = ImServiceType.Chat;
    write(w: MemoryPackWriter): void {
        w.writeObjectHeader(6);
        w.writeUInt64(this.userId);
        w.writeUInt64(this.peerId);
        w.writeString(this.lastReadMessageId);
        w.writeI64(this.timestamp);
        w.writeU16(this.type);
        w.writeU8(this.serviceType);
    }
    static read(r: MemoryPackReader): ImChatReadReceipt {
        const m = new ImChatReadReceipt();
        r.readObjectHeader();
        m.userId = r.readUInt64();
        m.peerId = r.readUInt64();
        m.lastReadMessageId = r.readString() ?? '';
        m.timestamp = r.readI64();
        m.type = r.readU16();
        m.serviceType = r.readU8();
        return m;
    }
}
export class ImTypingIndicator {
    userId: bigint = 0n;
    peerId: bigint = 0n;
    isTyping: boolean = false;
    type: number = ImMessageType.TypingIndicator;
    serviceType: number = ImServiceType.Chat;
    write(w: MemoryPackWriter): void {
        w.writeObjectHeader(5);
        w.writeUInt64(this.userId);
        w.writeUInt64(this.peerId);
        w.writeBool(this.isTyping);
        w.writeU16(this.type);
        w.writeU8(this.serviceType);
    }
    static read(r: MemoryPackReader): ImTypingIndicator {
        const m = new ImTypingIndicator();
        r.readObjectHeader();
        m.userId = r.readUInt64();
        m.peerId = r.readUInt64();
        m.isTyping = r.readBool();
        m.type = r.readU16();
        m.serviceType = r.readU8();
        return m;
    }
}
export class ImGroupChatSend {
    senderId: bigint = 0n;
    senderName: string = '';
    senderAvatar: string = '';
    groupId: bigint = 0n;
    content: string = '';
    contentType: number = ImContentType.Text;
    clientMessageId: string = '';
    timestamp: number = 0;
    mentionedUserIds: Array<bigint> = [];
    mentionAll: boolean = false;
    attachments: Array<string> = [];
    type: number = ImMessageType.GroupChatSend;
    serviceType: number = ImServiceType.Group;
    write(w: MemoryPackWriter): void {
        w.writeObjectHeader(13);
        w.writeUInt64(this.senderId);
        w.writeString(this.senderName);
        w.writeString(this.senderAvatar);
        w.writeUInt64(this.groupId);
        w.writeString(this.content);
        w.writeI32(this.contentType);
        w.writeString(this.clientMessageId);
        w.writeI64(this.timestamp);
        writeU64List(w, this.mentionedUserIds);
        w.writeBool(this.mentionAll);
        writeStringList(w, this.attachments);
        w.writeU16(this.type);
        w.writeU8(this.serviceType);
    }
    static read(r: MemoryPackReader): ImGroupChatSend {
        const m = new ImGroupChatSend();
        r.readObjectHeader();
        m.senderId = r.readUInt64();
        m.senderName = r.readString() ?? '';
        m.senderAvatar = r.readString() ?? '';
        m.groupId = r.readUInt64();
        m.content = r.readString() ?? '';
        m.contentType = r.readI32();
        m.clientMessageId = r.readString() ?? '';
        m.timestamp = r.readI64();
        m.mentionedUserIds = readU64List(r);
        m.mentionAll = r.readBool();
        m.attachments = readStringList(r);
        m.type = r.readU16();
        m.serviceType = r.readU8();
        return m;
    }
}
export class ImGroupChatNotify {
    serverMessageId: string = '';
    senderId: bigint = 0n;
    senderName: string = '';
    senderAvatar: string = '';
    groupId: bigint = 0n;
    groupName: string = '';
    content: string = '';
    contentType: number = ImContentType.Text;
    timestamp: number = 0;
    mentionedUserIds: Array<bigint> = [];
    mentionAll: boolean = false;
    attachments: Array<string> = [];
    type: number = ImMessageType.GroupChatNotify;
    serviceType: number = ImServiceType.Group;
    clientMessageId: string = '';
    write(w: MemoryPackWriter): void {
        w.writeObjectHeader(15);
        w.writeString(this.serverMessageId);
        w.writeUInt64(this.senderId);
        w.writeString(this.senderName);
        w.writeString(this.senderAvatar);
        w.writeUInt64(this.groupId);
        w.writeString(this.groupName);
        w.writeString(this.content);
        w.writeI32(this.contentType);
        w.writeI64(this.timestamp);
        writeU64List(w, this.mentionedUserIds);
        w.writeBool(this.mentionAll);
        writeStringList(w, this.attachments);
        w.writeU16(this.type);
        w.writeU8(this.serviceType);
        w.writeString(this.clientMessageId);
    }
    static read(r: MemoryPackReader): ImGroupChatNotify {
        const m = new ImGroupChatNotify();
        r.readObjectHeader();
        m.serverMessageId = r.readString() ?? '';
        m.senderId = r.readUInt64();
        m.senderName = r.readString() ?? '';
        m.senderAvatar = r.readString() ?? '';
        m.groupId = r.readUInt64();
        m.groupName = r.readString() ?? '';
        m.content = r.readString() ?? '';
        m.contentType = r.readI32();
        m.timestamp = r.readI64();
        m.mentionedUserIds = readU64List(r);
        m.mentionAll = r.readBool();
        m.attachments = readStringList(r);
        m.type = r.readU16();
        m.serviceType = r.readU8();
        m.clientMessageId = r.readString() ?? '';
        return m;
    }
}
export class ImContactInfo {
    userId: bigint = 0n;
    nickname: string = '';
    avatar: string = '';
    remarkName: string = '';
    relation: number = 0;
    onlineStatus: number = ImOnlineStatus.Offline;
    signature: string = '';
    verificationStatus: number = 0;
    type: number = ImMessageType.ContactListResponse;
    serviceType: number = ImServiceType.Contact;
    groupName: string = '';
    write(w: MemoryPackWriter): void {
        w.writeObjectHeader(11);
        w.writeUInt64(this.userId);
        w.writeString(this.nickname);
        w.writeString(this.avatar);
        w.writeString(this.remarkName);
        w.writeI32(this.relation);
        w.writeI32(this.onlineStatus);
        w.writeString(this.signature);
        w.writeI32(this.verificationStatus);
        w.writeU16(this.type);
        w.writeU8(this.serviceType);
        w.writeString(this.groupName);
    }
    static read(r: MemoryPackReader): ImContactInfo {
        r.readObjectHeader();
        return ImContactInfo.readBody(r);
    }
    /** 读取对象体（对象头已由调用方消费；用于 ImContactRequestHandleResponse.newContact 可空字段解析） */
    static readBody(r: MemoryPackReader): ImContactInfo {
        const m = new ImContactInfo();
        m.userId = r.readUInt64();
        m.nickname = r.readString() ?? '';
        m.avatar = r.readString() ?? '';
        m.remarkName = r.readString() ?? '';
        m.relation = r.readI32();
        m.onlineStatus = r.readI32();
        m.signature = r.readString() ?? '';
        m.verificationStatus = r.readI32();
        m.type = r.readU16();
        m.serviceType = r.readU8();
        m.groupName = r.readString() ?? '';
        return m;
    }
}
export class ImPendingContactRequest {
    requesterId: bigint = 0n;
    requesterName: string = '';
    message: string = '';
    timestamp: number = 0;
    static read(r: MemoryPackReader): ImPendingContactRequest {
        const m = new ImPendingContactRequest();
        r.readObjectHeader();
        m.requesterId = r.readUInt64();
        m.requesterName = r.readString() ?? '';
        m.message = r.readString() ?? '';
        m.timestamp = r.readI64();
        return m;
    }
}
/** 添加联系人请求（客户端 → 网关，IMContactMessages.cs，字段按 MemoryPackOrder 数值序） */
export class ImContactAddRequest {
    userId: bigint = 0n;
    targetUserId: bigint = 0n;
    verifyMessage: string = '';
    remarkName: string = '';
    source: string = '';
    requesterName: string = '';
    requesterAvatar: string = '';
    type: number = ImMessageType.ContactAddRequest;
    serviceType: number = ImServiceType.Contact;
    write(w: MemoryPackWriter): void {
        w.writeObjectHeader(9);
        w.writeUInt64(this.userId);
        w.writeUInt64(this.targetUserId);
        w.writeString(this.verifyMessage);
        w.writeString(this.remarkName);
        w.writeString(this.source);
        w.writeString(this.requesterName);
        w.writeString(this.requesterAvatar);
        w.writeU16(this.type);
        w.writeU8(this.serviceType);
    }
}
/** 添加联系人响应 */
export class ImContactAddResponse {
    success: boolean = false;
    message: string = '';
    userId: bigint = 0n;
    targetUserId: bigint = 0n;
    relation: number = ImContactRelation.None;
    type: number = ImMessageType.ContactAddResponse;
    serviceType: number = ImServiceType.Contact;
    static read(r: MemoryPackReader): ImContactAddResponse {
        const m = new ImContactAddResponse();
        r.readObjectHeader();
        m.success = r.readBool();
        m.message = r.readString() ?? '';
        m.userId = r.readUInt64();
        m.targetUserId = r.readUInt64();
        m.relation = r.readI32();
        m.type = r.readU16();
        m.serviceType = r.readU8();
        return m;
    }
}
/** 删除联系人请求 */
export class ImContactRemoveRequest {
    userId: bigint = 0n;
    targetUserId: bigint = 0n;
    type: number = ImMessageType.ContactRemoveRequest;
    serviceType: number = ImServiceType.Contact;
    write(w: MemoryPackWriter): void {
        w.writeObjectHeader(4);
        w.writeUInt64(this.userId);
        w.writeUInt64(this.targetUserId);
        w.writeU16(this.type);
        w.writeU8(this.serviceType);
    }
}
/** 删除联系人响应 */
export class ImContactRemoveResponse {
    success: boolean = false;
    message: string = '';
    targetUserId: bigint = 0n;
    type: number = ImMessageType.ContactRemoveResponse;
    serviceType: number = ImServiceType.Contact;
    static read(r: MemoryPackReader): ImContactRemoveResponse {
        const m = new ImContactRemoveResponse();
        r.readObjectHeader();
        m.success = r.readBool();
        m.message = r.readString() ?? '';
        m.targetUserId = r.readUInt64();
        m.type = r.readU16();
        m.serviceType = r.readU8();
        return m;
    }
}
/** 处理好友申请请求（接受/拒绝） */
export class ImContactRequestHandleRequest {
    userId: bigint = 0n;
    requesterId: bigint = 0n;
    accept: boolean = false;
    type: number = ImMessageType.ContactRequestHandleRequest;
    serviceType: number = ImServiceType.Contact;
    write(w: MemoryPackWriter): void {
        w.writeObjectHeader(5);
        w.writeUInt64(this.userId);
        w.writeUInt64(this.requesterId);
        w.writeBool(this.accept);
        w.writeU16(this.type);
        w.writeU8(this.serviceType);
    }
}
/** 处理好友申请响应（注意：NewContact 的 MemoryPackOrder=6，排在 Type/ServiceType 之后） */
export class ImContactRequestHandleResponse {
    success: boolean = false;
    message: string = '';
    requesterId: bigint = 0n;
    accepted: boolean = false;
    type: number = ImMessageType.ContactRequestHandleResponse;
    serviceType: number = ImServiceType.Contact;
    /** 接受申请成功时返回的新联系人信息（拒绝/失败时为 null） */
    newContact: ImContactInfo | null = null;
    static read(r: MemoryPackReader): ImContactRequestHandleResponse {
        const m = new ImContactRequestHandleResponse();
        r.readObjectHeader();
        m.success = r.readBool();
        m.message = r.readString() ?? '';
        m.requesterId = r.readUInt64();
        m.accepted = r.readBool();
        m.type = r.readU16();
        m.serviceType = r.readU8();
        // NewContact 是可空引用类型：服务端写 0xFF 单字节 null 对象头；非 null 时该字节为对象头（成员数）
        const header = r.readU8();
        if (header === 0xFF) {
            m.newContact = null;
        } else {
            m.newContact = ImContactInfo.readBody(r);
        }
        return m;
    }
}
/** 待处理好友申请列表请求 */
export class ImPendingContactRequestListRequest {
    userId: bigint = 0n;
    offset: number = 0;
    limit: number = 50;
    type: number = ImMessageType.ContactPendingListRequest;
    serviceType: number = ImServiceType.Contact;
    write(w: MemoryPackWriter): void {
        w.writeObjectHeader(5);
        w.writeUInt64(this.userId);
        w.writeI32(this.offset);
        w.writeI32(this.limit);
        w.writeU16(this.type);
        w.writeU8(this.serviceType);
    }
}
/** 待处理好友申请列表响应 */
export class ImPendingContactRequestListResponse {
    pendingRequests: Array<ImPendingContactRequest> = [];
    type: number = ImMessageType.ContactPendingListResponse;
    serviceType: number = ImServiceType.Contact;
    static read(r: MemoryPackReader): ImPendingContactRequestListResponse {
        const m = new ImPendingContactRequestListResponse();
        r.readObjectHeader();
        const count = r.readCollectionHeader();
        for (let i = 0; i < count; i++) {
            m.pendingRequests.push(ImPendingContactRequest.read(r));
        }
        m.type = r.readU16();
        m.serviceType = r.readU8();
        return m;
    }
}
export class ImContactListRequest {
    userId: bigint = 0n;
    offset: number = 0;
    limit: number = 50;
    onlineOnly: boolean = false;
    type: number = ImMessageType.ContactListRequest;
    serviceType: number = ImServiceType.Contact;
    write(w: MemoryPackWriter): void {
        w.writeObjectHeader(6);
        w.writeUInt64(this.userId);
        w.writeI32(this.offset);
        w.writeI32(this.limit);
        w.writeBool(this.onlineOnly);
        w.writeU16(this.type);
        w.writeU8(this.serviceType);
    }
}
export class ImContactListResponse {
    contacts: Array<ImContactInfo> = [];
    totalCount: number = 0;
    hasMore: boolean = false;
    type: number = ImMessageType.ContactListResponse;
    serviceType: number = ImServiceType.Contact;
    static read(r: MemoryPackReader): ImContactListResponse {
        const m = new ImContactListResponse();
        r.readObjectHeader();
        const count = r.readCollectionHeader();
        for (let i = 0; i < count; i++) {
            m.contacts.push(ImContactInfo.read(r));
        }
        m.totalCount = r.readI32();
        m.hasMore = r.readBool();
        m.type = r.readU16();
        m.serviceType = r.readU8();
        return m;
    }
}
export class ImContactOnlineStatus {
    userId: bigint = 0n;
    onlineStatus: number = ImOnlineStatus.Offline;
    timestamp: number = 0;
    type: number = ImMessageType.ContactOnlineStatus;
    serviceType: number = ImServiceType.Contact;
    static read(r: MemoryPackReader): ImContactOnlineStatus {
        const m = new ImContactOnlineStatus();
        r.readObjectHeader();
        m.userId = r.readUInt64();
        m.onlineStatus = r.readI32();
        m.timestamp = r.readI64();
        m.type = r.readU16();
        m.serviceType = r.readU8();
        return m;
    }
}
export class ImConversationListRequest {
    userId: bigint = 0n;
    offset: number = 0;
    limit: number = 20;
    type: number = ImMessageType.ConversationListRequest;
    serviceType: number = ImServiceType.Chat;
    write(w: MemoryPackWriter): void {
        w.writeObjectHeader(5);
        w.writeUInt64(this.userId);
        w.writeI32(this.offset);
        w.writeI32(this.limit);
        w.writeU16(this.type);
        w.writeU8(this.serviceType);
    }
}
/** 删除会话（服务端 IMConversationDeleteMessage：tag 36 / type 402） */
export class ImConversationDeleteMessage {
    userId: bigint = 0n;
    conversationId: string = '';
    type: number = ImMessageType.ConversationDelete;
    serviceType: number = ImServiceType.Chat;
    write(w: MemoryPackWriter): void {
        w.writeObjectHeader(4);
        w.writeUInt64(this.userId);
        w.writeString(this.conversationId);
        w.writeU16(this.type);
        w.writeU8(this.serviceType);
    }
}
/** 置顶/取消置顶会话（服务端 IMConversationPinMessage：tag 37 / type 403） */
export class ImConversationPinMessage {
    userId: bigint = 0n;
    conversationId: string = '';
    isPinned: boolean = false;
    type: number = ImMessageType.ConversationPin;
    serviceType: number = ImServiceType.Chat;
    write(w: MemoryPackWriter): void {
        w.writeObjectHeader(5);
        w.writeUInt64(this.userId);
        w.writeString(this.conversationId);
        w.writeBool(this.isPinned);
        w.writeU16(this.type);
        w.writeU8(this.serviceType);
    }
}
/** 会话免打扰开关（服务端 IMConversationMuteMessage：tag 38 / type 404） */
export class ImConversationMuteMessage {
    userId: bigint = 0n;
    conversationId: string = '';
    isMuted: boolean = false;
    type: number = ImMessageType.ConversationMute;
    serviceType: number = ImServiceType.Chat;
    write(w: MemoryPackWriter): void {
        w.writeObjectHeader(5);
        w.writeUInt64(this.userId);
        w.writeString(this.conversationId);
        w.writeBool(this.isMuted);
        w.writeU16(this.type);
        w.writeU8(this.serviceType);
    }
}
export class ImConversationInfo {
    conversationId: string = '';
    chatRelationType: number = ImChatRelationType.Friend;
    peerId: bigint = 0n;
    displayName: string = '';
    avatar: string = '';
    lastMessageSummary: string = '';
    lastMessageTime: number = 0;
    unreadCount: number = 0;
    isPinned: boolean = false;
    isMuted: boolean = false;
    type: number = ImMessageType.ConversationListResponse;
    serviceType: number = ImServiceType.Chat;
    static read(r: MemoryPackReader): ImConversationInfo {
        const m = new ImConversationInfo();
        r.readObjectHeader();
        m.conversationId = r.readString() ?? '';
        m.chatRelationType = r.readI32();
        m.peerId = r.readUInt64();
        m.displayName = r.readString() ?? '';
        m.avatar = r.readString() ?? '';
        m.lastMessageSummary = r.readString() ?? '';
        m.lastMessageTime = r.readI64();
        m.unreadCount = r.readI32();
        m.isPinned = r.readBool();
        m.isMuted = r.readBool();
        m.type = r.readU16();
        m.serviceType = r.readU8();
        return m;
    }
}
export class ImConversationListResponse {
    conversations: Array<ImConversationInfo> = [];
    totalCount: number = 0;
    hasMore: boolean = false;
    type: number = ImMessageType.ConversationListResponse;
    serviceType: number = ImServiceType.Chat;
    static read(r: MemoryPackReader): ImConversationListResponse {
        const m = new ImConversationListResponse();
        r.readObjectHeader();
        const count = r.readCollectionHeader();
        for (let i = 0; i < count; i++) {
            m.conversations.push(ImConversationInfo.read(r));
        }
        m.totalCount = r.readI32();
        m.hasMore = r.readBool();
        m.type = r.readU16();
        m.serviceType = r.readU8();
        return m;
    }
}
export class ImChatHistoryQueryRequest {
    userId: bigint = 0n;
    conversationId: string = '';
    chatRelationType: number = ImChatRelationType.Friend;
    peerId: bigint = 0n;
    startTime: number = 0;
    endTime: number = 0;
    count: number = 20;
    anchorMessageId: string = '';
    type: number = ImMessageType.ChatHistoryQuery;
    serviceType: number = ImServiceType.Chat;
    write(w: MemoryPackWriter): void {
        w.writeObjectHeader(10);
        w.writeUInt64(this.userId);
        w.writeString(this.conversationId);
        w.writeI32(this.chatRelationType);
        w.writeUInt64(this.peerId);
        w.writeI64(this.startTime);
        w.writeI64(this.endTime);
        w.writeI32(this.count);
        w.writeString(this.anchorMessageId);
        w.writeU16(this.type);
        w.writeU8(this.serviceType);
    }
}
export class ImStrangerChatNotify {
    serverMessageId: string = '';
    senderId: bigint = 0n;
    senderName: string = '';
    senderAvatar: string = '';
    receiverId: bigint = 0n;
    sessionId: string = '';
    content: string = '';
    contentType: number = ImContentType.Text;
    timestamp: number = 0;
    attachments: Array<string> = [];
    isStrangerMessage: boolean = true;
    type: number = ImMessageType.StrangerChatNotify;
    serviceType: number = ImServiceType.Chat;
    static read(r: MemoryPackReader): ImStrangerChatNotify {
        const m = new ImStrangerChatNotify();
        r.readObjectHeader();
        m.serverMessageId = r.readString() ?? '';
        m.senderId = r.readUInt64();
        m.senderName = r.readString() ?? '';
        m.senderAvatar = r.readString() ?? '';
        m.receiverId = r.readUInt64();
        m.sessionId = r.readString() ?? '';
        m.content = r.readString() ?? '';
        m.contentType = r.readI32();
        m.timestamp = r.readI64();
        m.attachments = readStringList(r);
        m.isStrangerMessage = r.readBool();
        m.type = r.readU16();
        m.serviceType = r.readU8();
        return m;
    }
}
export class ImChatHistoryQueryResponse {
    conversationId: string = '';
    privateMessages: Array<ImPrivateChatNotify> = [];
    groupMessages: Array<ImGroupChatNotify> = [];
    strangerMessages: Array<ImStrangerChatNotify> = [];
    chatRelationType: number = ImChatRelationType.Friend;
    hasMore: boolean = false;
    type: number = ImMessageType.ChatHistoryQueryResponse;
    serviceType: number = ImServiceType.Chat;
    static read(r: MemoryPackReader): ImChatHistoryQueryResponse {
        const m = new ImChatHistoryQueryResponse();
        r.readObjectHeader();
        m.conversationId = r.readString() ?? '';
        let count = r.readCollectionHeader();
        for (let i = 0; i < count; i++) {
            m.privateMessages.push(ImPrivateChatNotify.read(r));
        }
        count = r.readCollectionHeader();
        for (let i = 0; i < count; i++) {
            m.groupMessages.push(ImGroupChatNotify.read(r));
        }
        count = r.readCollectionHeader();
        for (let i = 0; i < count; i++) {
            m.strangerMessages.push(ImStrangerChatNotify.read(r));
        }
        m.chatRelationType = r.readI32();
        m.hasMore = r.readBool();
        m.type = r.readU16();
        m.serviceType = r.readU8();
        return m;
    }
}
export class ImHeartbeatMessage {
    userId: bigint = 0n;
    clientTimestamp: number = 0;
    type: number = ImMessageType.Heartbeat;
    serviceType: number = ImServiceType.Gateway;
    write(w: MemoryPackWriter): void {
        w.writeObjectHeader(4);
        w.writeUInt64(this.userId);
        w.writeI64(this.clientTimestamp);
        w.writeU16(this.type);
        w.writeU8(this.serviceType);
    }
}
export class ImHeartbeatResponse {
    serverTimestamp: number = 0;
    pendingMessageCount: number = 0;
    type: number = ImMessageType.HeartbeatResponse;
    serviceType: number = ImServiceType.Gateway;
    static read(r: MemoryPackReader): ImHeartbeatResponse {
        const m = new ImHeartbeatResponse();
        r.readObjectHeader();
        m.serverTimestamp = r.readI64();
        m.pendingMessageCount = r.readI32();
        m.type = r.readU16();
        m.serviceType = r.readU8();
        return m;
    }
}
export class ImErrorMessage {
    errorCode: number = 0;
    message: string = '';
    relatedMessageId: string = '';
    details: string = '';
    timestamp: number = 0;
    type: number = ImMessageType.Error;
    serviceType: number = ImServiceType.Gateway;
    static read(r: MemoryPackReader): ImErrorMessage {
        const m = new ImErrorMessage();
        r.readObjectHeader();
        m.errorCode = r.readI32();
        m.message = r.readString() ?? '';
        m.relatedMessageId = r.readString() ?? '';
        m.details = r.readString() ?? '';
        m.timestamp = r.readI64();
        m.type = r.readU16();
        m.serviceType = r.readU8();
        return m;
    }
}
export class ImSystemNotification {
    title: string = '';
    content: string = '';
    targetUserId: bigint = 0n;
    timestamp: number = 0;
    priority: number = 0;
    type: number = ImMessageType.SystemNotification;
    serviceType: number = ImServiceType.Notification;
    static read(r: MemoryPackReader): ImSystemNotification {
        const m = new ImSystemNotification();
        r.readObjectHeader();
        m.title = r.readString() ?? '';
        m.content = r.readString() ?? '';
        m.targetUserId = r.readUInt64();
        m.timestamp = r.readI64();
        m.priority = r.readU8();
        m.type = r.readU16();
        m.serviceType = r.readU8();
        return m;
    }
}
// ===== 通话信令（对齐 C# IMCallSignalMessage / IMCallSignalAckMessage，字段序=MemoryPackOrder）=====
export class ImCallSignal {
    callId: string = '';
    senderId: bigint = 0n;
    receiverId: bigint = 0n;
    signalType: number = ImCallSignalType.Offer;
    callType: number = ImCallType.Audio;
    senderName: string = '';
    senderAvatar: string = '';
    mediaEndpoint: string = '';
    isMuted: boolean = false;
    isCameraOff: boolean = false;
    endReason: number = ImCallEndReason.Normal;
    remark: string = '';
    timestamp: number = 0;
    type: number = ImMessageType.CallSignal;
    serviceType: number = ImServiceType.Call;
    write(w: MemoryPackWriter): void {
        w.writeObjectHeader(15);
        w.writeString(this.callId);
        w.writeUInt64(this.senderId);
        w.writeUInt64(this.receiverId);
        w.writeI32(this.signalType);
        w.writeI32(this.callType);
        w.writeString(this.senderName);
        w.writeString(this.senderAvatar);
        w.writeString(this.mediaEndpoint);
        w.writeBool(this.isMuted);
        w.writeBool(this.isCameraOff);
        w.writeI32(this.endReason);
        w.writeString(this.remark);
        w.writeI64(this.timestamp);
        w.writeU16(this.type);
        w.writeU8(this.serviceType);
    }
    static read(r: MemoryPackReader): ImCallSignal {
        const m = new ImCallSignal();
        r.readObjectHeader();
        m.callId = r.readString() ?? '';
        m.senderId = r.readUInt64();
        m.receiverId = r.readUInt64();
        m.signalType = r.readI32();
        m.callType = r.readI32();
        m.senderName = r.readString() ?? '';
        m.senderAvatar = r.readString() ?? '';
        m.mediaEndpoint = r.readString() ?? '';
        m.isMuted = r.readBool();
        m.isCameraOff = r.readBool();
        m.endReason = r.readI32();
        m.remark = r.readString() ?? '';
        m.timestamp = r.readI64();
        m.type = r.readU16();
        m.serviceType = r.readU8();
        return m;
    }
}
export class ImCallSignalAck {
    callId: string = '';
    signalType: number = ImCallSignalType.Offer;
    accepted: boolean = false;
    endReason: number = ImCallEndReason.Normal;
    message: string = '';
    timestamp: number = 0;
    type: number = ImMessageType.CallSignalAck;
    serviceType: number = ImServiceType.Call;
    static read(r: MemoryPackReader): ImCallSignalAck {
        const m = new ImCallSignalAck();
        r.readObjectHeader();
        m.callId = r.readString() ?? '';
        m.signalType = r.readI32();
        m.accepted = r.readBool();
        m.endReason = r.readI32();
        m.message = r.readString() ?? '';
        m.timestamp = r.readI64();
        m.type = r.readU16();
        m.serviceType = r.readU8();
        return m;
    }
}
// ===== 包 =====
/** 消息体统一载体：tag + 任一具体消息实例 */
export class ImPacketBody {
    tag: number = 0;
    /** 具体消息对象（类型见 tag 对应的 Im* 类） */
    message: object | null = null;
}
export class ImPacket {
    serviceType: number = ImServiceType.Gateway;
    header: ImHeader = new ImHeader();
    body: ImPacketBody | null = null;
    write(w: MemoryPackWriter): void {
        w.writeObjectHeader(3);
        w.writeU8(this.serviceType);
        this.header.write(w);
        if (this.body === null) {
            w.writeU8(0xFF);
        }
        else {
            w.writeUnionTag(this.body.tag);
            this.writeBody(w, this.body);
        }
    }
    private writeBody(w: MemoryPackWriter, body: ImPacketBody): void {
        const m = body.message;
        if (m instanceof ImPrivateChatSend) {
            m.write(w);
        }
        else if (m instanceof ImHeartbeatMessage) {
            m.write(w);
        }
        else if (m instanceof ImContactListRequest) {
            m.write(w);
        }
        else if (m instanceof ImContactAddRequest) {
            m.write(w);
        }
        else if (m instanceof ImContactRemoveRequest) {
            m.write(w);
        }
        else if (m instanceof ImContactRequestHandleRequest) {
            m.write(w);
        }
        else if (m instanceof ImPendingContactRequestListRequest) {
            m.write(w);
        }
        else if (m instanceof ImConversationListRequest) {
            m.write(w);
        }
        else if (m instanceof ImConversationDeleteMessage) {
            m.write(w);
        }
        else if (m instanceof ImConversationPinMessage) {
            m.write(w);
        }
        else if (m instanceof ImConversationMuteMessage) {
            m.write(w);
        }
        else if (m instanceof ImChatHistoryQueryRequest) {
            m.write(w);
        }
        else if (m instanceof ImGroupChatSend) {
            m.write(w);
        }
        else if (m instanceof ImChatReadReceipt) {
            m.write(w);
        }
        else if (m instanceof ImTypingIndicator) {
            m.write(w);
        }
        else if (m instanceof ImPrivateChatNotify) {
            m.write(w);
        }
        else if (m instanceof ImChatAck) {
            m.write(w);
        }
        else if (m instanceof ImChatRecallMessage) {
            m.write(w);
        }
        else if (m instanceof ImGroupChatNotify) {
            m.write(w);
        }
        else if (m instanceof ImCallSignal) {
            m.write(w);
        }
        else {
            throw new Error(`不支持编码的消息体 tag=${body.tag}`);
        }
    }
    static read(r: MemoryPackReader): ImPacket {
        const p = new ImPacket();
        r.readObjectHeader();
        p.serviceType = r.readU8();
        p.header = ImHeader.read(r);
        const tag = r.readUnionTag();
        if (tag === 0xFF) {
            return p;
        }
        const body = new ImPacketBody();
        body.tag = tag;
        switch (tag) {
            case ImUnionTag.PrivateChatNotify:
                body.message = ImPrivateChatNotify.read(r);
                break;
            case ImUnionTag.GroupChatNotify:
                body.message = ImGroupChatNotify.read(r);
                break;
            case ImUnionTag.ChatAck:
                body.message = ImChatAck.read(r);
                break;
            case ImUnionTag.ChatRecall:
                body.message = ImChatRecallMessage.read(r);
                break;
            case ImUnionTag.ChatReadReceipt:
                body.message = ImChatReadReceipt.read(r);
                break;
            case ImUnionTag.TypingIndicator:
                body.message = ImTypingIndicator.read(r);
                break;
            case ImUnionTag.ContactListResponse:
                body.message = ImContactListResponse.read(r);
                break;
            case ImUnionTag.ContactAddResponse:
                body.message = ImContactAddResponse.read(r);
                break;
            case ImUnionTag.ContactRemoveResponse:
                body.message = ImContactRemoveResponse.read(r);
                break;
            case ImUnionTag.ContactRequestHandleResponse:
                body.message = ImContactRequestHandleResponse.read(r);
                break;
            case ImUnionTag.ContactPendingListResponse:
                body.message = ImPendingContactRequestListResponse.read(r);
                break;
            case ImUnionTag.ContactOnlineStatus:
                body.message = ImContactOnlineStatus.read(r);
                break;
            case ImUnionTag.ConversationListResponse:
                body.message = ImConversationListResponse.read(r);
                break;
            case ImUnionTag.ChatHistoryQueryResponse:
                body.message = ImChatHistoryQueryResponse.read(r);
                break;
            case ImUnionTag.ChatHistoryClear:
                // 清空确认无本地处理字段，跳过（成员按 Error 以外的最小结构不解析）
                body.message = null;
                break;
            case ImUnionTag.HeartbeatResponse:
                body.message = ImHeartbeatResponse.read(r);
                break;
            case ImUnionTag.Error:
                body.message = ImErrorMessage.read(r);
                break;
            case ImUnionTag.SystemNotification:
                body.message = ImSystemNotification.read(r);
                break;
            case ImUnionTag.CallSignal:
                body.message = ImCallSignal.read(r);
                break;
            case ImUnionTag.CallSignalAck:
                body.message = ImCallSignalAck.read(r);
                break;
            case ImUnionTag.PrivateChatSend:
                body.message = ImPrivateChatSend.read(r);
                break;
            case ImUnionTag.GroupChatSend:
                body.message = ImGroupChatSend.read(r);
                break;
            default:
                body.message = null;
                break;
        }
        p.body = body;
        return p;
    }
}
