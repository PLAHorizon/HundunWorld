import relationalStore from "@ohos.data.relationalStore";
import { common } from '@kit.AbilityKit';
import hilog from "@ohos.hilog";
type Context = common.Context;
/**
 * IM 本地缓存（SQLite，@ohos.data.relationalStore）。
 * 对应 .NET 侧 LiteDB 的职责：聊天记录、会话、联系人落盘，
 * 启动时先显缓存再与服务端历史合并去重。
 */
const TAG = 'GengDi.ImLocalStore';
const DB_NAME = 'gengdi_im.db';
const DB_VERSION = 2;
/** 历史版本（v1）messages 建表片段，用于迁移判定 */
const LEGACY_MESSAGES_SQL = 'UNIQUE(server_message_id, client_message_id)';
export class ImLocalStore {
    private static instance: ImLocalStore | null = null;
    static getInstance(): ImLocalStore {
        if (ImLocalStore.instance === null) {
            ImLocalStore.instance = new ImLocalStore();
        }
        return ImLocalStore.instance;
    }
    private db: relationalStore.RdbStore | null = null;
    async initialize(context: Context): Promise<void> {
        if (this.db !== null) {
            return;
        }
        const config: relationalStore.StoreConfig = {
            name: DB_NAME,
            securityLevel: relationalStore.SecurityLevel.S1
        };
        this.db = await relationalStore.getRdbStore(context, config);
        await this.db.executeSql(`
      CREATE TABLE IF NOT EXISTS im_messages (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        server_message_id TEXT,
        client_message_id TEXT,
        conversation_id TEXT NOT NULL,
        sender_id INTEGER NOT NULL,
        sender_name TEXT,
        content TEXT,
        content_type INTEGER,
        timestamp INTEGER,
        is_self INTEGER,
        status INTEGER
      );
    `);
        // v1 → v2 迁移：旧表用组合唯一约束导致"同一条消息占用两个唯一键"而重复落库，
        // 新版改为 server_message_id / client_message_id 各自的"非空精确唯一"索引，并去重存量数据。
        await this.migrateMessagesIfLegacy();
        try {
            // 若历史残留"同一 clientMessageId 两行（乐观行+回推行）"，创建唯一索引会触发约束冲突，
            // 捕获后走"自然键合并重建"，避免应用初始化崩溃。
            await this.createContentIndexes();
        }
        catch (e) {
            hilog.warn(0x0001, TAG, '消息唯一索引冲突，执行重复消息重建: %{public}s', (e as Error).message);
            await this.rebuildMessagesDeduped();
        }
        await this.db.executeSql(`
      CREATE TABLE IF NOT EXISTS im_conversations (
        conversation_id TEXT PRIMARY KEY,
        peer_id INTEGER,
        display_name TEXT,
        avatar TEXT,
        last_message_summary TEXT,
        last_message_time INTEGER,
        unread_count INTEGER DEFAULT 0,
        is_pinned INTEGER DEFAULT 0,
        is_muted INTEGER DEFAULT 0
      );
    `);
        await this.db.executeSql(`
      CREATE TABLE IF NOT EXISTS im_contacts (
        user_id INTEGER PRIMARY KEY,
        nickname TEXT,
        avatar TEXT,
        remark_name TEXT,
        online_status INTEGER,
        signature TEXT
      );
    `);
        // 本地已删除消息黑名单：防止重进聊天时服务端历史回拉使已删除消息"复活"
        await this.db.executeSql(`
      CREATE TABLE IF NOT EXISTS im_deleted_messages (
        message_key TEXT PRIMARY KEY
      );
    `);
    }
    /** 追加一条消息（server/client 消息 ID 任一命中则更新而非新增，杜绝重复落库） */
    async insertMessage(m: LocalImMessage): Promise<void> {
        if (this.db === null) {
            return;
        }
        try {
            // 已本地删除的消息：即使服务端历史回拉也不再落库（重进聊天不复活）
            const delKey = m.serverMessageId.length > 0 ? m.serverMessageId : m.clientMessageId;
            if (delKey.length > 0 && await this.isMessageDeleted(delKey)) {
                return;
            }
            // 幂等预查：全局按 server/client 消息 ID 命中已有行（不带会话过滤——
            // 同一条消息可能经不同会话 ID 推导先后到达），命中则补全 server/状态/会话，不新增
            const existing = await this.findByMessageKey(m.serverMessageId, m.clientMessageId);
            if (existing !== null) {
                const patch: relationalStore.ValuesBucket = {};
                if (m.serverMessageId.length > 0 && existing.serverMessageId !== m.serverMessageId) {
                    patch['server_message_id'] = m.serverMessageId;
                }
                if (m.status > existing.status) {
                    patch['status'] = m.status;
                }
                // 命中行会话与新推导会话不一致时（旧格式残留/推导修正），以新会话为准
                if (m.conversationId.length > 0 && existing.conversationId !== m.conversationId) {
                    patch['conversation_id'] = m.conversationId;
                }
                if (Object.keys(patch).length > 0) {
                    const pred = new relationalStore.RdbPredicates('im_messages');
                    pred.equalTo('id', existing.id);
                    await this.db.update(patch, pred);
                }
                return;
            }
            const values: relationalStore.ValuesBucket = {
                'server_message_id': m.serverMessageId,
                'client_message_id': m.clientMessageId,
                'conversation_id': m.conversationId,
                'sender_id': m.senderId,
                'sender_name': m.senderName,
                'content': m.content,
                'content_type': m.contentType,
                'timestamp': m.timestamp,
                'is_self': m.isSelf ? 1 : 0,
                'status': m.status
            };
            await this.db.insert('im_messages', values, relationalStore.ConflictResolution.ON_CONFLICT_IGNORE);
        }
        catch (e) {
            hilog.warn(0x0001, TAG, '消息落库失败: %{public}s', (e as Error).message);
        }
    }
    /** 全局按 server 或 client 消息 ID 查找已有消息（优先 server） */
    private async findByMessageKey(serverMessageId: string, clientMessageId: string): Promise<LocalImMessage | null> {
        if (this.db === null) {
            return null;
        }
        const serverKey = serverMessageId.length > 0 ? serverMessageId : '';
        const clientKey = clientMessageId.length > 0 ? clientMessageId : '';
        if (serverKey.length === 0 && clientKey.length === 0) {
            return null;
        }
        try {
            if (serverKey.length > 0) {
                const pred = new relationalStore.RdbPredicates('im_messages');
                pred.equalTo('server_message_id', serverKey);
                const rs = await this.db.query(pred);
                try {
                    if (rs.goToFirstRow()) {
                        return this.rowToMessage(rs);
                    }
                }
                finally {
                    rs.close();
                }
            }
            if (clientKey.length > 0) {
                const pred = new relationalStore.RdbPredicates('im_messages');
                pred.equalTo('client_message_id', clientKey);
                const rs = await this.db.query(pred);
                try {
                    if (rs.goToFirstRow()) {
                        return this.rowToMessage(rs);
                    }
                }
                finally {
                    rs.close();
                }
            }
        }
        catch (e) {
            hilog.warn(0x0001, TAG, '消息查重失败: %{public}s', (e as Error).message);
        }
        return null;
    }
    /** 按会话读取消息（时间升序，最近 limit 条） */
    async getMessages(conversationId: string, limit: number): Promise<LocalImMessage[]> {
        if (this.db === null) {
            return [];
        }
        const predicates = new relationalStore.RdbPredicates('im_messages');
        predicates.equalTo('conversation_id', conversationId)
            .orderByDesc('timestamp')
            .limitAs(limit);
        const resultSet = await this.db.query(predicates);
        const list: LocalImMessage[] = [];
        try {
            while (resultSet.goToNextRow()) {
                list.push(this.rowToMessage(resultSet));
            }
        }
        finally {
            resultSet.close();
        }
        list.reverse();
        return list;
    }
    /**
     * 建消息表上的三类约束索引（幂等）。
     * 两个部分唯一索引分别对"非空 server_message_id / 非空 client_message_id"精确唯一，
     * 从数据库层面杜绝同一条消息重复落库。
     */
    private async createContentIndexes(): Promise<void> {
        if (this.db === null) {
            return;
        }
        await this.db.executeSql('CREATE UNIQUE INDEX IF NOT EXISTS uniq_msg_server ON im_messages(server_message_id) WHERE server_message_id != \'\' AND server_message_id IS NOT NULL;');
        await this.db.executeSql('CREATE UNIQUE INDEX IF NOT EXISTS uniq_msg_client ON im_messages(client_message_id) WHERE client_message_id != \'\' AND client_message_id IS NOT NULL;');
        await this.db.executeSql('CREATE INDEX IF NOT EXISTS idx_messages_conv ON im_messages(conversation_id, timestamp);');
    }
    /** 将消息转为落库 ValuesBucket */
    private messageToValues(m: LocalImMessage): relationalStore.ValuesBucket {
        return {
            'server_message_id': m.serverMessageId,
            'client_message_id': m.clientMessageId,
            'conversation_id': m.conversationId,
            'sender_id': m.senderId,
            'sender_name': m.senderName,
            'content': m.content,
            'content_type': m.contentType,
            'timestamp': m.timestamp,
            'is_self': m.isSelf ? 1 : 0,
            'status': m.status
        } as relationalStore.ValuesBucket;
    }
    /**
     * 自然键去重合并：
     * 两条记录只要共享任一"非空 serverMessageId 或 clientMessageId"即视为同一条消息。
     * 发送端 v1 曾把同一条自发消息写成两行——乐观上屏行(server='', client=X)与
     * 服务端回推行(server=S, client=X)共享 clientMessageId=X，按"每行取一个键"的去重会漏掉，
     * 导致重复落库并在建唯一索引时因 client 重复而约束冲突。此处用并查集把共享任一键的行合并。
     */
    private mergeRowsByNaturalKey(raw: LocalImMessage[]): LocalImMessage[] {
        const n = raw.length;
        if (n === 0) {
            return [];
        }
        const parent: number[] = new Array<number>(n);
        for (let i = 0; i < n; i++) {
            parent[i] = i;
        }
        const find = (x: number): number => {
            while (parent[x] !== x) {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        };
        const union = (a: number, b: number): void => {
            const ra = find(a);
            const rb = find(b);
            if (ra !== rb) {
                parent[ra] = rb;
            }
        };
        const serverIdx = new Map<string, number>();
        const clientIdx = new Map<string, number>();
        for (let i = 0; i < n; i++) {
            const s = raw[i].serverMessageId;
            const c = raw[i].clientMessageId;
            if (s.length > 0) {
                const prev = serverIdx.get(s);
                if (prev !== undefined) {
                    union(prev, i);
                }
                else {
                    serverIdx.set(s, i);
                }
            }
            if (c.length > 0) {
                const prev = clientIdx.get(c);
                if (prev !== undefined) {
                    union(prev, i);
                }
                else {
                    clientIdx.set(c, i);
                }
            }
        }
        const merged = new Map<number, LocalImMessage>();
        for (let i = 0; i < n; i++) {
            const root = find(i);
            const src = raw[i];
            const out = merged.get(root);
            if (out === undefined) {
                const first = new LocalImMessage();
                first.serverMessageId = src.serverMessageId;
                first.clientMessageId = src.clientMessageId;
                first.conversationId = src.conversationId;
                first.senderId = src.senderId;
                first.senderName = src.senderName;
                first.content = src.content;
                first.contentType = src.contentType;
                first.timestamp = src.timestamp;
                first.isSelf = src.isSelf;
                first.status = src.status;
                merged.set(root, first);
            }
            else {
                // 合并：优先保留非空 server/client 与更完整的状态、时间、内容
                if (out.serverMessageId.length === 0 && src.serverMessageId.length > 0) {
                    out.serverMessageId = src.serverMessageId;
                }
                if (out.clientMessageId.length === 0 && src.clientMessageId.length > 0) {
                    out.clientMessageId = src.clientMessageId;
                }
                if (src.status > out.status) {
                    out.status = src.status;
                }
                if (out.contentType === 0 && src.contentType !== 0) {
                    out.contentType = src.contentType;
                }
                if (out.content.length === 0 && src.content.length > 0) {
                    out.content = src.content;
                }
                if (src.timestamp > out.timestamp) {
                    out.timestamp = src.timestamp;
                }
                if (out.senderName.length === 0 && src.senderName.length > 0) {
                    out.senderName = src.senderName;
                }
                if (!out.isSelf && src.isSelf) {
                    out.isSelf = src.isSelf;
                }
            }
        }
        return Array.from(merged.values());
    }
    /**
     * 重复消息重建（兜底）：老库可能已是 v2 结构但残留"同一 client/server 键两行"，
     * 使 createContentIndexes 建唯一索引报 "SQLite: Abort due to constraint violation"。
     * 此方法读全部行 → 自然键合并 → 重建表 + 索引 → 写回，保证初始化不崩溃且历史无重复。
     */
    private async rebuildMessagesDeduped(): Promise<void> {
        if (this.db === null) {
            return;
        }
        try {
            const rs = await this.db.querySql('SELECT * FROM im_messages;');
            const raw: LocalImMessage[] = [];
            try {
                while (rs.goToNextRow()) {
                    raw.push(this.rowToMessage(rs));
                }
            }
            finally {
                rs.close();
            }
            if (raw.length === 0) {
                await this.createContentIndexes();
                return;
            }
            const deduped = this.mergeRowsByNaturalKey(raw);
            await this.db.executeSql('ALTER TABLE im_messages RENAME TO im_messages_old;');
            await this.db.executeSql(`
        CREATE TABLE IF NOT EXISTS im_messages (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          server_message_id TEXT,
          client_message_id TEXT,
          conversation_id TEXT NOT NULL,
          sender_id INTEGER NOT NULL,
          sender_name TEXT,
          content TEXT,
          content_type INTEGER,
          timestamp INTEGER,
          is_self INTEGER,
          status INTEGER
        );
      `);
            await this.createContentIndexes();
            for (const m of deduped) {
                await this.db.insert('im_messages', this.messageToValues(m), relationalStore.ConflictResolution.ON_CONFLICT_IGNORE);
            }
            await this.db.executeSql('DROP TABLE IF EXISTS im_messages_old;');
            hilog.info(0x0001, TAG, '消息表重建完成（%{public}d 行 → %{public}d 行，无重复）', raw.length, deduped.length);
        }
        catch (e) {
            hilog.warn(0x0001, TAG, '消息表重建失败: %{public}s', (e as Error).message);
        }
    }
    /**
     * v1 → v2 迁移：检测旧表是否使用组合唯一约束（导致同一条消息可能占两个唯一键而重复落库）。
     * 若命中，则重建表并按"共享 server/client 键"做并查集去重，随后由 initialize 在干净表上建唯一索引。
     */
    private async migrateMessagesIfLegacy(): Promise<void> {
        if (this.db === null) {
            return;
        }
        try {
            const rs = await this.db.querySql("SELECT sql FROM sqlite_master WHERE type='table' AND name='im_messages'");
            let ddl = '';
            try {
                if (rs.goToFirstRow()) {
                    ddl = rs.getString(rs.getColumnIndex('sql')) ?? '';
                }
            }
            finally {
                rs.close();
            }
            if (ddl.length === 0 || !ddl.includes(LEGACY_MESSAGES_SQL)) {
                return; // 已是新版或无表定义
            }
            await this.db.executeSql('ALTER TABLE im_messages RENAME TO im_messages_v1;');
            await this.db.executeSql(`
        CREATE TABLE IF NOT EXISTS im_messages (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          server_message_id TEXT,
          client_message_id TEXT,
          conversation_id TEXT NOT NULL,
          sender_id INTEGER NOT NULL,
          sender_name TEXT,
          content TEXT,
          content_type INTEGER,
          timestamp INTEGER,
          is_self INTEGER,
          status INTEGER
        );
      `);
            const oldRs = await this.db.querySql('SELECT * FROM im_messages_v1;');
            const raw: LocalImMessage[] = [];
            try {
                while (oldRs.goToNextRow()) {
                    raw.push(this.rowToMessage(oldRs));
                }
            }
            finally {
                oldRs.close();
            }
            const deduped = this.mergeRowsByNaturalKey(raw);
            for (const m of deduped) {
                await this.db.insert('im_messages', this.messageToValues(m), relationalStore.ConflictResolution.ON_CONFLICT_IGNORE);
            }
            await this.db.executeSql('DROP TABLE IF EXISTS im_messages_v1;');
            hilog.info(0x0001, TAG, '消息表 v1→v2 迁移完成（去重 %{public}d 行 → %{public}d 行）', raw.length, deduped.length);
        }
        catch (e) {
            hilog.warn(0x0001, TAG, '消息表迁移跳过: %{public}s', (e as Error).message);
        }
    }
    /**
     * 旧版会话 ID 格式迁移：客户端旧版用 p:{a}:{b}（冒号+双端 ID）/ g:{id} 格式，
     * 与服务端 p_{peerId} / g_{groupId} 不一致，导致推送消息落入错误会话。
     * 此方法把消息表的旧格式会话改写为服务端格式；会话表旧行直接删除
     * （refreshConversations 拉取服务端列表后会按新格式重建，未读/摘要以服务端为准）。幂等可重复调用。
     */
    async migrateLegacyConversationIds(selfUserId: number): Promise<void> {
        if (this.db === null || selfUserId <= 0) {
            return;
        }
        try {
            let migrated = 0;
            // 私聊：p:{a}:{b} → p_{对端}
            migrated += await this.rewriteLegacyConversations('p:%', (conv: string): string => {
                const parts = conv.split(':');
                if (parts.length !== 3) {
                    return '';
                }
                const a = Number(parts[1]);
                const b = Number(parts[2]);
                if (!Number.isFinite(a) || !Number.isFinite(b)) {
                    return '';
                }
                const peer = a === selfUserId ? b : a;
                return `p_${peer}`;
            });
            // 群聊：g:{id} → g_{id}
            migrated += await this.rewriteLegacyConversations('g:%', (conv: string): string => {
                const parts = conv.split(':');
                if (parts.length !== 2) {
                    return '';
                }
                const gid = Number(parts[1]);
                if (!Number.isFinite(gid)) {
                    return '';
                }
                return `g_${gid}`;
            });
            if (migrated > 0) {
                hilog.info(0x0001, TAG, '会话 ID 格式迁移完成: %{public}d 条消息已归位', migrated);
            }
        }
        catch (e) {
            hilog.warn(0x0001, TAG, '会话 ID 格式迁移跳过: %{public}s', (e as Error).message);
        }
    }
    /** 按旧格式会话前缀查询消息行并改写 conversation_id，返回改写条数 */
    private async rewriteLegacyConversations(likePattern: string, convert: (conv: string) => string): Promise<number> {
        if (this.db === null) {
            return 0;
        }
        const pred = new relationalStore.RdbPredicates('im_messages');
        pred.like('conversation_id', likePattern);
        const rs = await this.db.query(pred);
        const ids: number[] = [];
        const targets: string[] = [];
        try {
            while (rs.goToNextRow()) {
                const id = rs.getLong(rs.getColumnIndex('id'));
                const conv = rs.getString(rs.getColumnIndex('conversation_id'));
                const target = convert(conv);
                if (target.length > 0 && target !== conv) {
                    ids.push(id);
                    targets.push(target);
                }
            }
        }
        finally {
            rs.close();
        }
        for (let i = 0; i < ids.length; i++) {
            const p = new relationalStore.RdbPredicates('im_messages');
            p.equalTo('id', ids[i]);
            await this.db.update({ 'conversation_id': targets[i] } as relationalStore.ValuesBucket, p);
        }
        // 会话表同格式旧行删除（服务端列表刷新时按新格式重建）
        const delPred = new relationalStore.RdbPredicates('im_conversations');
        delPred.like('conversation_id', likePattern);
        await this.db.delete(delPred);
        return ids.length;
    }
    private rowToMessage(rs: relationalStore.ResultSet): LocalImMessage {
        const m = new LocalImMessage();
        m.id = rs.getLong(rs.getColumnIndex('id'));
        m.serverMessageId = rs.getString(rs.getColumnIndex('server_message_id'));
        m.clientMessageId = rs.getString(rs.getColumnIndex('client_message_id'));
        m.conversationId = rs.getString(rs.getColumnIndex('conversation_id'));
        m.senderId = rs.getLong(rs.getColumnIndex('sender_id'));
        m.senderName = rs.getString(rs.getColumnIndex('sender_name'));
        m.content = rs.getString(rs.getColumnIndex('content'));
        m.contentType = rs.getLong(rs.getColumnIndex('content_type'));
        m.timestamp = rs.getLong(rs.getColumnIndex('timestamp'));
        m.isSelf = rs.getLong(rs.getColumnIndex('is_self')) !== 0;
        m.status = rs.getLong(rs.getColumnIndex('status'));
        return m;
    }
    /**
     * 更新本地消息状态（按 clientMessageId 或 serverMessageId，仅升不降）。
     * 服务端 ACK 的 ackedMessageId 是 serverMessageId，本机乐观上屏用 clientMessageId，两者都要覆盖。
     */
    async updateMessageStatus(messageId: string, status: number): Promise<void> {
        if (this.db === null || messageId.length === 0) {
            return;
        }
        const values: relationalStore.ValuesBucket = { 'status': status };
        const predClient = new relationalStore.RdbPredicates('im_messages');
        predClient.equalTo('client_message_id', messageId).lessThan('status', status);
        await this.db.update(values, predClient);
        const predServer = new relationalStore.RdbPredicates('im_messages');
        predServer.equalTo('server_message_id', messageId).lessThan('status', status);
        await this.db.update(values, predServer);
    }
    /** 用 serverMessageId 更新乐观上屏消息（幂等：先按 client 找乐观行，再处理 server 已存在的情况） */
    async updateMessageServerId(clientMessageId: string, serverMessageId: string, status: number): Promise<void> {
        if (this.db === null || serverMessageId.length === 0) {
            return;
        }
        // 按 clientMessageId 定位乐观行
        const optimistic = await this.findByClientMessageId(clientMessageId);
        if (optimistic !== null) {
            // 若另一个 serverId 相同的行已存在（历史拉取先行落库），删除乐观行避免双行，
            // 否则直接把乐观行补上 serverId。
            const serverHolder = optimistic.serverMessageId === serverMessageId
                ? optimistic
                : await this.findByServerMessageId(serverMessageId);
            const pred = new relationalStore.RdbPredicates('im_messages');
            pred.equalTo('client_message_id', clientMessageId);
            if (serverHolder !== null && serverHolder.clientMessageId !== clientMessageId) {
                await this.db.delete(pred);
                return;
            }
            const values: relationalStore.ValuesBucket = {
                'server_message_id': serverMessageId
            };
            await this.db.update(values, pred);
            // 状态仅升不降（notify 可能先到已置更高状态）
            const statusPred = new relationalStore.RdbPredicates('im_messages');
            statusPred.equalTo('client_message_id', clientMessageId).lessThan('status', status);
            await this.db.update({ 'status': status } as relationalStore.ValuesBucket, statusPred);
            return;
        }
        // 乐观行不存在（如重启后丢失），仅提升已有 server 行状态
        const holder = await this.findByServerMessageId(serverMessageId);
        if (holder !== null) {
            const pred = new relationalStore.RdbPredicates('im_messages');
            pred.equalTo('server_message_id', serverMessageId);
            await this.db.update({ 'status': status } as relationalStore.ValuesBucket, pred);
        }
    }
    /** 按 serverMessageId 查找消息 */
    private async findByServerMessageId(serverMessageId: string): Promise<LocalImMessage | null> {
        if (this.db === null || serverMessageId.length === 0) {
            return null;
        }
        const pred = new relationalStore.RdbPredicates('im_messages');
        pred.equalTo('server_message_id', serverMessageId);
        const rs = await this.db.query(pred);
        try {
            if (rs.goToFirstRow()) {
                return this.rowToMessage(rs);
            }
        }
        finally {
            rs.close();
        }
        return null;
    }
    /** 按 clientMessageId 查找消息 */
    private async findByClientMessageId(clientMessageId: string): Promise<LocalImMessage | null> {
        if (this.db === null || clientMessageId.length === 0) {
            return null;
        }
        const pred = new relationalStore.RdbPredicates('im_messages');
        pred.equalTo('client_message_id', clientMessageId);
        const rs = await this.db.query(pred);
        try {
            if (rs.goToFirstRow()) {
                return this.rowToMessage(rs);
            }
        }
        finally {
            rs.close();
        }
        return null;
    }
    async upsertConversation(c: LocalConversation): Promise<void> {
        if (this.db === null) {
            return;
        }
        const values: relationalStore.ValuesBucket = {
            'conversation_id': c.conversationId,
            'peer_id': c.peerId,
            'display_name': c.displayName,
            'avatar': c.avatar,
            'last_message_summary': c.lastMessageSummary,
            'last_message_time': c.lastMessageTime,
            'unread_count': c.unreadCount,
            'is_pinned': c.isPinned ? 1 : 0,
            'is_muted': c.isMuted ? 1 : 0
        };
        await this.db.insert('im_conversations', values, relationalStore.ConflictResolution.ON_CONFLICT_REPLACE);
    }
    async getConversations(): Promise<LocalConversation[]> {
        if (this.db === null) {
            return [];
        }
        const predicates = new relationalStore.RdbPredicates('im_conversations');
        predicates.orderByDesc('is_pinned').orderByDesc('last_message_time');
        const rs = await this.db.query(predicates);
        const list: LocalConversation[] = [];
        try {
            while (rs.goToNextRow()) {
                const c = new LocalConversation();
                c.conversationId = rs.getString(rs.getColumnIndex('conversation_id'));
                c.peerId = rs.getLong(rs.getColumnIndex('peer_id'));
                c.displayName = rs.getString(rs.getColumnIndex('display_name'));
                c.avatar = rs.getString(rs.getColumnIndex('avatar'));
                c.lastMessageSummary = rs.getString(rs.getColumnIndex('last_message_summary'));
                c.lastMessageTime = rs.getLong(rs.getColumnIndex('last_message_time'));
                c.unreadCount = rs.getLong(rs.getColumnIndex('unread_count'));
                c.isPinned = rs.getLong(rs.getColumnIndex('is_pinned')) !== 0;
                c.isMuted = rs.getLong(rs.getColumnIndex('is_muted')) !== 0;
                list.push(c);
            }
        }
        finally {
            rs.close();
        }
        return list;
    }
    async clearUnread(conversationId: string): Promise<void> {
        if (this.db === null) {
            return;
        }
        const values: relationalStore.ValuesBucket = { 'unread_count': 0 };
        const predicates = new relationalStore.RdbPredicates('im_conversations');
        predicates.equalTo('conversation_id', conversationId);
        await this.db.update(values, predicates);
    }
    /** 设置会话未读数（标为未读传 1，标为已读传 0） */
    async setUnreadCount(conversationId: string, count: number): Promise<void> {
        if (this.db === null) {
            return;
        }
        const values: relationalStore.ValuesBucket = { 'unread_count': count };
        const predicates = new relationalStore.RdbPredicates('im_conversations');
        predicates.equalTo('conversation_id', conversationId);
        await this.db.update(values, predicates);
    }
    /** 更新会话置顶/免打扰标志 */
    async updateConversationFlags(conversationId: string, isPinned: boolean, isMuted: boolean): Promise<void> {
        if (this.db === null) {
            return;
        }
        const values: relationalStore.ValuesBucket = {
            'is_pinned': isPinned ? 1 : 0,
            'is_muted': isMuted ? 1 : 0
        };
        const predicates = new relationalStore.RdbPredicates('im_conversations');
        predicates.equalTo('conversation_id', conversationId);
        await this.db.update(values, predicates);
    }
    /** 删除会话（对标微信"删除该聊天"：会话行 + 本地消息记录一并删除） */
    async deleteConversation(conversationId: string): Promise<void> {
        if (this.db === null) {
            return;
        }
        const convPred = new relationalStore.RdbPredicates('im_conversations');
        convPred.equalTo('conversation_id', conversationId);
        await this.db.delete(convPred);
        const msgPred = new relationalStore.RdbPredicates('im_messages');
        msgPred.equalTo('conversation_id', conversationId);
        await this.db.delete(msgPred);
    }
    /** 删除单条本地消息（按 server/client 消息 ID 匹配） */
    async deleteMessage(messageKey: string): Promise<void> {
        if (this.db === null || messageKey.length === 0) {
            return;
        }
        const predServer = new relationalStore.RdbPredicates('im_messages');
        predServer.equalTo('server_message_id', messageKey);
        await this.db.delete(predServer);
        const predClient = new relationalStore.RdbPredicates('im_messages');
        predClient.equalTo('client_message_id', messageKey);
        await this.db.delete(predClient);
    }
    /** 记录一条本地已删除消息（重进聊天时服务端历史不再复活该消息） */
    async addDeletedMessage(messageKey: string): Promise<void> {
        if (this.db === null || messageKey.length === 0) {
            return;
        }
        await this.db.insert('im_deleted_messages', { 'message_key': messageKey },
            relationalStore.ConflictResolution.ON_CONFLICT_IGNORE);
    }
    /** 读取全部已删除消息键 */
    async getDeletedMessageKeys(): Promise<Set<string>> {
        const set = new Set<string>();
        if (this.db === null) {
            return set;
        }
        const rs = await this.db.query(new relationalStore.RdbPredicates('im_deleted_messages'));
        try {
            while (rs.goToNextRow()) {
                const k = rs.getString(rs.getColumnIndex('message_key'));
                if (k.length > 0) {
                    set.add(k);
                }
            }
        }
        finally {
            rs.close();
        }
        return set;
    }
    /** 某日消息键是否已被本地删除 */
    async isMessageDeleted(messageKey: string): Promise<boolean> {
        if (this.db === null || messageKey.length === 0) {
            return false;
        }
        const pred = new relationalStore.RdbPredicates('im_deleted_messages');
        pred.equalTo('message_key', messageKey);
        const rs = await this.db.query(pred);
        try {
            return rs.goToFirstRow();
        }
        finally {
            rs.close();
        }
    }
    /** 标记消息为已撤回（status=5 + 内容替换为占位提示文案） */
    async markMessageRecalled(messageKey: string, placeholder: string): Promise<void> {
        if (this.db === null || messageKey.length === 0) {
            return;
        }
        const values: relationalStore.ValuesBucket = {
            'status': 5,
            'content': placeholder,
            'content_type': 0
        };
        const predServer = new relationalStore.RdbPredicates('im_messages');
        predServer.equalTo('server_message_id', messageKey);
        await this.db.update(values, predServer);
        const predClient = new relationalStore.RdbPredicates('im_messages');
        predClient.equalTo('client_message_id', messageKey);
        await this.db.update(values, predClient);
    }
    async upsertContacts(contacts: LocalContact[]): Promise<void> {
        if (this.db === null || contacts.length === 0) {
            return;
        }
        for (const c of contacts) {
            const values: relationalStore.ValuesBucket = {
                'user_id': c.userId,
                'nickname': c.nickname,
                'avatar': c.avatar,
                'remark_name': c.remarkName,
                'online_status': c.onlineStatus,
                'signature': c.signature
            };
            await this.db.insert('im_contacts', values, relationalStore.ConflictResolution.ON_CONFLICT_REPLACE);
        }
    }
    async updateContactStatus(userId: number, status: number): Promise<void> {
        if (this.db === null) {
            return;
        }
        const values: relationalStore.ValuesBucket = { 'online_status': status };
        const predicates = new relationalStore.RdbPredicates('im_contacts');
        predicates.equalTo('user_id', userId);
        await this.db.update(values, predicates);
    }
    /** 删除本地联系人缓存（删除好友后调用；聊天记录与会话保留） */
    async deleteContact(userId: number): Promise<void> {
        if (this.db === null) {
            return;
        }
        const predicates = new relationalStore.RdbPredicates('im_contacts');
        predicates.equalTo('user_id', userId);
        await this.db.delete(predicates);
    }
    async getContacts(): Promise<LocalContact[]> {
        if (this.db === null) {
            return [];
        }
        const rs = await this.db.query(new relationalStore.RdbPredicates('im_contacts'));
        const list: LocalContact[] = [];
        try {
            while (rs.goToNextRow()) {
                const c = new LocalContact();
                c.userId = rs.getLong(rs.getColumnIndex('user_id'));
                c.nickname = rs.getString(rs.getColumnIndex('nickname'));
                c.avatar = rs.getString(rs.getColumnIndex('avatar'));
                c.remarkName = rs.getString(rs.getColumnIndex('remark_name'));
                c.onlineStatus = rs.getLong(rs.getColumnIndex('online_status'));
                c.signature = rs.getString(rs.getColumnIndex('signature'));
                list.push(c);
            }
        }
        finally {
            rs.close();
        }
        return list;
    }
}
export class LocalImMessage {
    id: number = 0;
    serverMessageId: string = '';
    clientMessageId: string = '';
    conversationId: string = '';
    senderId: number = 0;
    senderName: string = '';
    content: string = '';
    contentType: number = 0;
    timestamp: number = 0;
    isSelf: boolean = false;
    status: number = 0;
}
export class LocalConversation {
    conversationId: string = '';
    peerId: number = 0;
    displayName: string = '';
    avatar: string = '';
    lastMessageSummary: string = '';
    lastMessageTime: number = 0;
    unreadCount: number = 0;
    isPinned: boolean = false;
    isMuted: boolean = false;
}
export class LocalContact {
    userId: number = 0;
    nickname: string = '';
    avatar: string = '';
    remarkName: string = '';
    onlineStatus: number = 0;
    signature: string = '';
}
