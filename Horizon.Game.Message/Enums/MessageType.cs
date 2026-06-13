using MemoryPack;
using System.ComponentModel;

namespace Horizon.Game.Message.Enums
{
    /// <summary>
    /// 武侠MMORPG游戏消息类型枚举
    /// </summary>
    public enum MessageType : ushort
    {
        #region 账户与角色管理消息 (1-99)
        /// <summary>
        /// 登录请求
        /// </summary>
        [Description("登录请求")]
        LoginRequest = 1,

        /// <summary>
        /// 登录响应
        /// </summary>
        [Description("登录响应")]
        LoginResponse = 2,

        /// <summary>
        /// 登出
        /// </summary>
        [Description("登出")]
        Logout = 3,

        /// <summary>
        /// 角色列表
        /// </summary>
        [Description("角色列表")]
        CharacterList = 4,

        /// <summary>
        /// 创建角色
        /// </summary>
        [Description("创建角色")]
        CreateCharacter = 5,

        /// <summary>
        /// 选择角色
        /// </summary>
        [Description("选择角色")]
        SelectCharacter = 6,

        /// <summary>
        /// 进入游戏
        /// </summary>
        [Description("进入游戏")]
        EnterGame = 7,

        /// <summary>
        /// 玩家出生
        /// </summary>
        [Description("玩家出生")]
        PlayerSpawn = 8,

        /// <summary>
        /// 删除角色
        /// </summary>
        [Description("删除角色")]
        CharacterDelete = 9,

        /// <summary>
        /// 角色名检查
        /// </summary>
        [Description("角色名检查")]
        CharacterNameCheck = 10,

        /// <summary>
        /// 角色外观
        /// </summary>
        [Description("角色外观")]
        Appearance = 11,

        /// <summary>
        /// 注册请求
        /// </summary>
        [Description("注册请求")]
        RegisterRequest = 12,

        /// <summary>
        /// 注册响应
        /// </summary>
        [Description("注册响应")]
        RegisterResponse = 13,

        /// <summary>
        /// 验证码请求
        /// </summary>
        [Description("验证码请求")]
        VerificationCodeRequest = 14,

        /// <summary>
        /// 验证码响应
        /// </summary>
        [Description("验证码响应")]
        VerificationCodeResponse = 15,

        /// <summary>
        /// 构建游戏用户请求
        /// </summary>
        [Description("构建游戏用户请求")]
        BuildGameUserRequest = 16,

        /// <summary>
        /// 构建游戏用户响应
        /// </summary>
        [Description("构建游戏用户响应")]
        BuildGameUserResponse = 17,

        [Description("Token登录请求")]
        TokenLoginRequest = 18,

        [Description("Token登录响应")]
        TokenLoginResponse = 19,

        #endregion

        #region 游戏核心玩法消息 (100-299)
        /// <summary>
        /// 移动同步
        /// </summary>
        [Description("移动同步")]
        Movement = 100,

        /// <summary>
        /// 属性更新
        /// </summary>
        [Description("属性更新")]
        AttributeUpdate = 101,

        /// <summary>
        /// 玩家动画
        /// </summary>
        [Description("玩家动画")]
        PlayerAnimation = 102,

        /// <summary>
        /// 普通攻击
        /// </summary>
        [Description("普通攻击")]
        Attack = 103,

        /// <summary>
        /// 技能施放
        /// </summary>
        [Description("技能施放")]
        SkillCast = 104,

        /// <summary>
        /// 技能效果
        /// </summary>
        [Description("技能效果")]
        Skill = 105,

        /// <summary>
        /// 轻功
        /// </summary>
        [Description("轻功")]
        QingGong = 105,

        /// <summary>
        /// 内功
        /// </summary>
        [Description("内功")]
        NeiGong = 106,

        /// <summary>
        /// 招式连击
        /// </summary>
        [Description("招式连击")]
        ComboAttack = 107,

        /// <summary>
        /// 格挡/闪避
        /// </summary>
        [Description("格挡/闪避")]
        Defense = 108,

        /// <summary>
        /// 受伤
        /// </summary>
        [Description("受伤")]
        Damage = 109,

        /// <summary>
        /// 死亡
        /// </summary>
        [Description("死亡")]
        Death = 110,

        /// <summary>
        /// 复活
        /// </summary>
        [Description("复活")]
        Resurrect = 111,

        #endregion

        #region 社交与门派系统消息 (300-499)
        /// <summary>
        /// 门派信息
        /// </summary>
        [Description("门派信息")]
        SectInfo = 300,

        /// <summary>
        /// 加入门派
        /// </summary>
        [Description("加入门派")]
        JoinSect = 301,

        /// <summary>
        /// 门派技能
        /// </summary>
        [Description("门派技能")]
        SectSkill = 302,

        /// <summary>
        /// 门派任务
        /// </summary>
        [Description("门派任务")]
        SectQuest = 303,

        /// <summary>
        /// 江湖声望
        /// </summary>
        [Description("江湖声望")]
        Reputation = 304,

        /// <summary>
        /// 侠义值
        /// </summary>
        [Description("侠义值")]
        Chivalrous = 305,

        /// <summary>
        /// 结拜
        /// </summary>
        [Description("结拜")]
        SwornBrother = 306,

        /// <summary>
        /// 师徒
        /// </summary>
        [Description("师徒")]
        MasterApprentice = 307,

        /// <summary>
        /// 帮派
        /// </summary>
        [Description("帮派")]
        Guild = 308,

        /// <summary>
        /// 好友
        /// </summary>
        [Description("好友")]
        Friend = 309,

        /// <summary>
        /// 邮件
        /// </summary>
        [Description("邮件")]
        Mail = 310,

        /// <summary>
        /// 聊天
        /// </summary>
        [Description("聊天")]
        Chat = 311,

        /// <summary>
        /// 聊天消息（别名）
        /// </summary>
        [Description("聊天消息")]
        ChatMessage = 311,

        /// <summary>
        /// 组队
        /// </summary>
        [Description("组队")]
        Team = 312,

        /// <summary>
        /// PK
        /// </summary>
        [Description("PK")]
        PK = 313,

        /// <summary>
        /// 决斗
        /// </summary>
        [Description("决斗")]
        Duel = 314,

        #endregion

        #region 物品与交易系统消息 (500-699)
        /// <summary>
        /// 背包
        /// </summary>
        [Description("背包")]
        Inventory = 500,

        /// <summary>
        /// 装备
        /// </summary>
        [Description("装备")]
        Equipment = 501,

        /// <summary>
        /// 商城
        /// </summary>
        [Description("商城")]
        Shop = 502,

        /// <summary>
        /// 拍卖行
        /// </summary>
        [Description("拍卖行")]
        Auction = 503,

        /// <summary>
        /// 交易
        /// </summary>
        [Description("交易")]
        Trade = 504,

        /// <summary>
        /// 仓库
        /// </summary>
        [Description("仓库")]
        Storage = 505,

        /// <summary>
        /// 制造
        /// </summary>
        [Description("制造")]
        Crafting = 506,

        /// <summary>
        /// 强化
        /// </summary>
        [Description("强化")]
        Enhancement = 507,

        /// <summary>
        /// 宝石镶嵌
        /// </summary>
        [Description("宝石镶嵌")]
        GemInlay = 508,

        /// <summary>
        /// 属性继承
        /// </summary>
        [Description("属性继承")]
        AttributeInheritance = 509,

        /// <summary>
        /// 五行锻造
        /// </summary>
        [Description("五行锻造")]
        WuXingCrafting = 510,

        /// <summary>
        /// 装备精炼
        /// </summary>
        [Description("装备精炼")]
        EquipmentRefine = 511,

        #endregion

        #region 技能与武学系统消息 (700-899)
        /// <summary>
        /// 技能信息
        /// </summary>
        [Description("技能信息")]
        SkillInfo = 700,

        /// <summary>
        /// 学习技能
        /// </summary>
        [Description("学习技能")]
        LearnSkill = 701,

        /// <summary>
        /// 升级技能
        /// </summary>
        [Description("升级技能")]
        UpgradeSkill = 702,

        /// <summary>
        /// 技能熟练度
        /// </summary>
        [Description("技能熟练度")]
        SkillProficiency = 703,

        /// <summary>
        /// 技能槽位
        /// </summary>
        [Description("技能槽位")]
        SkillSlot = 704,

        /// <summary>
        /// 武功秘籍
        /// </summary>
        [Description("武功秘籍")]
        MartialArtsManual = 705,

        /// <summary>
        /// 心法
        /// </summary>
        [Description("心法")]
        XinFa = 706,

        /// <summary>
        /// 经脉
        /// </summary>
        [Description("经脉")]
        Meridian = 707,

        /// <summary>
        /// 内力
        /// </summary>
        [Description("内力")]
        InternalForce = 708,

        #endregion

        #region 任务与成就系统消息 (900-1099)
        /// <summary>
        /// 任务
        /// </summary>
        [Description("任务")]
        Quest = 900,

        /// <summary>
        /// 日常任务
        /// </summary>
        [Description("日常任务")]
        DailyQuest = 901,

        /// <summary>
        /// 支线任务
        /// </summary>
        [Description("支线任务")]
        SideQuest = 902,

        /// <summary>
        /// 主线任务
        /// </summary>
        [Description("主线任务")]
        MainQuest = 903,

        /// <summary>
        /// 成就
        /// </summary>
        [Description("成就")]
        Achievement = 904,

        /// <summary>
        /// 称号
        /// </summary>
        [Description("称号")]
        Title = 905,

        /// <summary>
        /// 图鉴
        /// </summary>
        [Description("图鉴")]
        Collection = 906,

        #endregion

        #region 系统与管理消息 (1100-1299)
        /// <summary>
        /// 心跳包
        /// </summary>
        [Description("心跳包")]
        Heartbeat = 1100,

        /// <summary>
        /// 系统通知
        /// </summary>
        [Description("系统通知")]
        SystemNotification = 1101,

        /// <summary>
        /// 系统公告
        /// </summary>
        [Description("系统公告")]
        SystemAnnouncement = 1102,

        /// <summary>
        /// 错误消息
        /// </summary>
        [Description("错误消息")]
        Error = 1103,

        /// <summary>
        /// 服务器列表
        /// </summary>
        [Description("服务器列表")]
        ServerList = 1104,

        /// <summary>
        /// 服务器状态
        /// </summary>
        [Description("服务器状态")]
        ServerStatus = 1105,

        /// <summary>
        /// 版本更新
        /// </summary>
        [Description("版本更新")]
        VersionUpdate = 1106,

        /// <summary>
        /// 配置更新
        /// </summary>
        [Description("配置更新")]
        ConfigUpdate = 1107,

        /// <summary>
        /// 日志消息
        /// </summary>
        [Description("日志消息")]
        Log = 1108,

        #endregion

        #region 其他消息 (1300+)
        /// <summary>
        /// 自定义消息
        /// </summary>
        [Description("自定义消息")]
        Custom = 1300,

        /// <summary>
        /// 未知消息
        /// </summary>
        [Description("未知消息")]
        Unknown = 0,
        MapPlayer = 1301,
        GuildMember = 1302,
        QuestUpdate = 1303,
        AcceptQuest = 1304,
        CompleteQuest = 1305,
        InventoryUpdate = 1306,
        EquipItem = 1307,
        WeaponSwitch = 1308,
        UseItem = 1309,
        EquipmentInfo = 1310,
        EquipmentEnhance = 1311,
        InventoryInfo = 1312,
        CraftingResult = 1313,
        CraftingRecipe = 1314,
        System = 1315,
        ZoneAndServerInfo = 1316,
        SessionInfo = 1317,
        SkillCooldown = 1318,
        ChivalryPoint = 1319,
        HeartbeatResponse = 1320,
        ChatHistory = 1321,
        ServerManagement = 1325,
        PlayerManagement = 1326,
        GameEvent = 1327,
        GuildInfo = 1329,
        GuildSkillInfo = 1330,
        WuXingSystem = 1331,

        /// <summary>
        /// 实体生成
        /// </summary>
        [Description("实体生成")]
        EntitySpawn = 1332,

        /// <summary>
        /// 实体销毁
        /// </summary>
        [Description("实体销毁")]
        EntityDespawn = 1333,

        /// <summary>
        /// Buff/效果同步
        /// </summary>
        [Description("效果同步")]
        EffectSync = 1334,

        /// <summary>
        /// AOI视野更新
        /// </summary>
        [Description("AOI更新")]
        AoiUpdate = 1335,

        /// <summary>
        /// 移动速度验证
        /// </summary>
        [Description("移动速度验证")]
        MovementSpeedValidation = 1336,

        /// <summary>
        /// 技能打断
        /// </summary>
        [Description("技能打断")]
        SkillInterrupt = 1337,

        /// <summary>
        /// 好友列表
        /// </summary>
        [Description("好友列表")]
        FriendList = 1338,

        /// <summary>
        /// 好友操作
        /// </summary>
        [Description("好友操作")]
        FriendOperation = 1339,

        /// <summary>
        /// 传送点信息
        /// </summary>
        [Description("传送点信息")]
        TeleportPoint = 1340,

        /// <summary>
        /// 小地图标记
        /// </summary>
        [Description("小地图标记")]
        MinimapMarker = 1341,

        /// <summary>
        /// 聊天消息发送
        /// </summary>
        [Description("聊天消息发送")]
        ChatSend = 1342,

        /// <summary>
        /// 装备对比
        /// </summary>
        [Description("装备对比")]
        EquipmentComparison = 1343,

        /// <summary>
        /// 公会管理操作
        /// </summary>
        [Description("公会管理操作")]
        GuildManagement = 1344,

        /// <summary>
        /// 组队邀请
        /// </summary>
        [Description("组队邀请")]
        TeamInvite = 1345,

        /// <summary>
        /// 击杀特写
        /// </summary>
        [Description("击杀特写")]
        KillCam = 1346,

        /// <summary>
        /// 快捷键配置
        /// </summary>
        [Description("快捷键配置")]
        HotkeyConfig = 1347,

        /// <summary>
        /// 音频播放
        /// </summary>
        [Description("音频播放")]
        AudioPlayback = 1348,

        /// <summary>
        /// Buff/Debuff状态显示
        /// </summary>
        [Description("Buff显示")]
        BuffDisplay = 1349,

        /// <summary>
        /// 背包物品拖拽操作
        /// </summary>
        [Description("背包拖拽")]
        InventoryDragDrop = 1350,

        /// <summary>
        /// 输入配置同步
        /// </summary>
        [Description("输入配置同步")]
        InputConfigSync = 1351,

        /// <summary>
        /// 动画状态同步
        /// </summary>
        [Description("动画状态同步")]
        AnimationSync = 1352,

        /// <summary>
        /// 性能报告
        /// </summary>
        [Description("性能报告")]
        PerformanceReport = 1353,

        /// <summary>
        /// 断线重连
        /// </summary>
        [Description("断线重连")]
        Reconnection = 1354,

        /// <summary>
        /// LOD配置
        /// </summary>
        [Description("LOD配置")]
        LODConfig = 1355,

        /// <summary>
        /// 粒子预算
        /// </summary>
        [Description("粒子预算")]
        ParticleBudget = 1356,

        /// <summary>
        /// 消息压缩配置
        /// </summary>
        [Description("消息压缩配置")]
        MessageCompressionConfig = 1357,
        #endregion

        #region Phase 7 - 背包管理消息

        /// <summary>
        /// 背包排序
        /// </summary>
        [Description("背包排序")]
        InventorySort = 1358,

        /// <summary>
        /// 物品拆分
        /// </summary>
        [Description("物品拆分")]
        ItemSplit = 1359,

        /// <summary>
        /// 物品丢弃
        /// </summary>
        [Description("物品丢弃")]
        ItemDiscard = 1360,

        /// <summary>
        /// 物品锁定
        /// </summary>
        [Description("物品锁定")]
        ItemLock = 1361,

        /// <summary>
        /// 背包扩容
        /// </summary>
        [Description("背包扩容")]
        InventoryExpand = 1362,
        #endregion

        #region Phase 8 - 角色管理与合成网络集成消息

        /// <summary>
        /// 角色状态同步
        /// </summary>
        [Description("角色状态同步")]
        CharacterStateSync = 1363,

        /// <summary>
        /// 合成金币同步
        /// </summary>
        [Description("合成金币同步")]
        CraftingGoldSync = 1364,

        /// <summary>
        /// 技能目标验证
        /// </summary>
        [Description("技能目标验证")]
        SkillTargetValidation = 1365,

        /// <summary>
        /// 截图通知
        /// </summary>
        [Description("截图通知")]
        ScreenshotNotify = 1366,

        /// <summary>
        /// 角色属性刷新
        /// </summary>
        [Description("角色属性刷新")]
        CharacterAttributeRefresh = 1367,
        #endregion

        #region Phase 9 - 聊天与好友系统客户端集成消息

        /// <summary>
        /// 聊天通知（服务器推送聊天消息到客户端）
        /// </summary>
        [Description("聊天通知")]
        ChatNotify = 1368,

        /// <summary>
        /// 好友状态更新（上线/下线通知）
        /// </summary>
        [Description("好友状态更新")]
        FriendStatusUpdate = 1369,

        /// <summary>
        /// 好友请求通知
        /// </summary>
        [Description("好友请求通知")]
        FriendRequestNotify = 1370,

        /// <summary>
        /// 加入聊天频道
        /// </summary>
        [Description("加入聊天频道")]
        ChatChannelJoin = 1371,

        /// <summary>
        /// 离开聊天频道
        /// </summary>
        [Description("离开聊天频道")]
        ChatChannelLeave = 1372,
        #endregion

        #region Phase 10 - 交易、邮件、任务、副本、成就、排行榜客户端集成消息

        /// <summary>
        /// 交易请求（发起面对面交易）
        /// </summary>
        [Description("交易请求")]
        TradeRequest = 1373,

        /// <summary>
        /// 交易响应（接受/拒绝交易）
        /// </summary>
        [Description("交易响应")]
        TradeResponse = 1374,

        /// <summary>
        /// 交易更新通知（交易状态变更）
        /// </summary>
        [Description("交易更新通知")]
        TradeUpdateNotify = 1375,

        /// <summary>
        /// 市场上架请求
        /// </summary>
        [Description("市场上架请求")]
        MarketListRequest = 1376,

        /// <summary>
        /// 市场搜索请求
        /// </summary>
        [Description("市场搜索请求")]
        MarketSearchRequest = 1377,

        /// <summary>
        /// 市场搜索响应
        /// </summary>
        [Description("市场搜索响应")]
        MarketSearchResponse = 1378,

        /// <summary>
        /// 邮件列表请求
        /// </summary>
        [Description("邮件列表请求")]
        MailListRequest = 1379,

        /// <summary>
        /// 邮件列表响应
        /// </summary>
        [Description("邮件列表响应")]
        MailListResponse = 1380,

        /// <summary>
        /// 邮件操作（阅读/领取附件/删除）
        /// </summary>
        [Description("邮件操作")]
        MailOperation = 1381,

        /// <summary>
        /// 新邮件通知
        /// </summary>
        [Description("新邮件通知")]
        MailNotify = 1382,

        /// <summary>
        /// 任务列表请求
        /// </summary>
        [Description("任务列表请求")]
        QuestListRequest = 1383,

        /// <summary>
        /// 任务列表响应
        /// </summary>
        [Description("任务列表响应")]
        QuestListResponse = 1384,

        /// <summary>
        /// 任务进度更新通知
        /// </summary>
        [Description("任务进度更新通知")]
        QuestProgressNotify = 1385,

        /// <summary>
        /// 副本进入请求
        /// </summary>
        [Description("副本进入请求")]
        DungeonEnterRequest = 1386,

        /// <summary>
        /// 副本状态通知
        /// </summary>
        [Description("副本状态通知")]
        DungeonStatusNotify = 1387,

        /// <summary>
        /// 成就解锁通知
        /// </summary>
        [Description("成就解锁通知")]
        AchievementUnlockNotify = 1388,

        /// <summary>
        /// 成就列表响应
        /// </summary>
        [Description("成就列表响应")]
        AchievementListResponse = 1389,

        /// <summary>
        /// 排行榜查询请求
        /// </summary>
        [Description("排行榜查询请求")]
        RankingQueryRequest = 1390,

        /// <summary>
        /// 排行榜查询响应
        /// </summary>
        [Description("排行榜查询响应")]
        RankingQueryResponse = 1391,

        /// <summary>
        /// 实时同步帧（承载 SyncPacketCodec 编码后的帧）
        /// </summary>
        [Description("实时同步帧")]
        SyncPacket = 1392,
        #region Arena
        [Description("ArenaJoinRequest")]
        ArenaJoinRequest = 1400,
        [Description("ArenaJoinResponse")]
        ArenaJoinResponse = 1401,
        [Description("ArenaLeaveRequest")]
        ArenaLeaveRequest = 1402,
        [Description("ArenaLeaveResponse")]
        ArenaLeaveResponse = 1403,
        [Description("ArenaInfoRequest")]
        ArenaInfoRequest = 1404,
        [Description("ArenaInfoResponse")]
        ArenaInfoResponse = 1405,
        #endregion

        #region CrossServer
        [Description("CrossServerTransferRequest")]
        CrossServerTransferRequest = 1500,
        [Description("CrossServerTransferResponse")]
        CrossServerTransferResponse = 1501,
        #endregion

        #region IM即时通信消息 (1600-1699)

        /// <summary>
        /// IM私聊消息发送
        /// </summary>
        [Description("IM私聊消息发送")]
        IMPrivateChatSend = 1600,

        /// <summary>
        /// IM私聊消息通知
        /// </summary>
        [Description("IM私聊消息通知")]
        IMPrivateChatNotify = 1601,

        /// <summary>
        /// IM消息回执
        /// </summary>
        [Description("IM消息回执")]
        IMChatAck = 1602,

        /// <summary>
        /// IM消息撤回
        /// </summary>
        [Description("IM消息撤回")]
        IMChatRecall = 1603,

        /// <summary>
        /// IM已读回执
        /// </summary>
        [Description("IM已读回执")]
        IMChatReadReceipt = 1604,

        /// <summary>
        /// IM正在输入
        /// </summary>
        [Description("IM正在输入")]
        IMTypingIndicator = 1605,

        /// <summary>
        /// IM群聊消息发送
        /// </summary>
        [Description("IM群聊消息发送")]
        IMGroupChatSend = 1610,

        /// <summary>
        /// IM群聊消息通知
        /// </summary>
        [Description("IM群聊消息通知")]
        IMGroupChatNotify = 1611,

        /// <summary>
        /// IM陌生人聊天请求
        /// </summary>
        [Description("IM陌生人聊天请求")]
        IMStrangerChatRequest = 1620,

        /// <summary>
        /// IM陌生人聊天响应
        /// </summary>
        [Description("IM陌生人聊天响应")]
        IMStrangerChatResponse = 1621,

        /// <summary>
        /// IM陌生人聊天消息发送
        /// </summary>
        [Description("IM陌生人聊天消息发送")]
        IMStrangerChatSend = 1622,

        /// <summary>
        /// IM陌生人聊天消息通知
        /// </summary>
        [Description("IM陌生人聊天消息通知")]
        IMStrangerChatNotify = 1623,

        /// <summary>
        /// IM联系人操作
        /// </summary>
        [Description("IM联系人操作")]
        IMContactOperation = 1630,

        /// <summary>
        /// IM会话管理
        /// </summary>
        [Description("IM会话管理")]
        IMConversation = 1640,

        /// <summary>
        /// IM聊天记录查询
        /// </summary>
        [Description("IM聊天记录查询")]
        IMChatHistory = 1650,

        #endregion
    }
}



#endregion

