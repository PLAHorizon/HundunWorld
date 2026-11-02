using Horizon.Game.Message.Enums;
using MemoryPack;
using Orleans;
using System;
using System.Collections.Generic;

namespace Horizon.Game.Message.Network
{
    #region 脚本化世界生成契约（DS 怪物/NPC/特殊事件 + Grain 权威授权）

    /// <summary>
    /// 脚本生成物类别（设计方案 §3）。
    /// </summary>
    [GenerateSerializer]
    public enum ScriptedKind : byte
    {
        /// <summary>未指定（视为非法）。</summary>
        None = 0,

        /// <summary>怪物：复用 DS 的 <c>DsMonster</c> + <c>MonsterAiCore</c>，可被击杀、掉落。</summary>
        Monster = 1,

        /// <summary>NPC：无战斗、可交互、常驻或带 TTL。</summary>
        Npc = 2,

        /// <summary>特殊事件（表现型广播或玩法型实体，由模板决定）。</summary>
        Event = 3,

        /// <summary>触发型事件：放置一个球形触发区域。</summary>
        TriggerZone = 4,
    }

    /// <summary>脚本操作类型。</summary>
    [GenerateSerializer]
    public enum ScriptOp : byte
    {
        /// <summary>生成。</summary>
        Spawn = 1,

        /// <summary>按实体 ID 清除（需 <c>DespawnAny</c>，或目标为本人生成物且持 <c>DespawnOwned</c>）。</summary>
        Despawn = 2,

        /// <summary>清除本人全部生成物。</summary>
        DespawnMine = 3,

        /// <summary>查询自身授权位与配额余量。</summary>
        Query = 4,

        /// <summary>
        /// 授予脚本权限。<b>与生成类操作不同：不产生令牌、也不经 DS</b>——
        /// 网关直接交给 Grain 的 GrantScriptAsync，由 Grain 校验调用者持 GmOps 位
        /// 或位列引导配置。授权面因此仍然只有一个真值源，没有第二条 HTTP 管理门。
        /// </summary>
        Grant = 5,

        /// <summary>回收脚本权限。</summary>
        Revoke = 6,

        /// <summary>查询操作审计。</summary>
        Audit = 7,
    }

    /// <summary>触发区域重复模式。</summary>
    [GenerateSerializer]
    public enum ScriptTriggerMode : byte
    {
        /// <summary>仅触发一次。</summary>
        Once = 0,

        /// <summary>可重复触发，受 <c>CooldownSeconds</c> 限频。</summary>
        Repeat = 1,
    }

    /// <summary>脚本操作被拒绝的原因码（回给客户端用于文案区分，同时进审计）。</summary>
    [GenerateSerializer]
    public enum ScriptRejectReason : byte
    {
        /// <summary>无（成功）。</summary>
        None = 0,

        /// <summary>功能未开启。</summary>
        Disabled = 1,

        /// <summary>无该角色授权记录。</summary>
        NoGrant = 2,

        /// <summary>授权位不覆盖本操作。</summary>
        InsufficientPermission = 3,

        /// <summary>授权已过期。</summary>
        Expired = 4,

        /// <summary>模板不在白名单目录内。</summary>
        UnknownTemplate = 5,

        /// <summary>参数非法（数量/半径/TTL 越界）。</summary>
        InvalidArgument = 6,

        /// <summary>目标点超出允许距离。</summary>
        TooFar = 7,

        // 8 号已废弃且**不再使用**：跨 shard 的判定在 DS 侧（令牌 `TargetShardId` 与本 DS 声明的片不符），
        // 它以字符串 reason（`wrong-shard:{n}`）落 DS 日志，从不作为 Grain 裁决码回给操作者。
        // 编号冻结是为了不动后面这些**在线上跑的整数**（重排会让陈旧客户端把 12 读成 9）。

        /// <summary>冷却中。</summary>
        Cooldown = 9,

        /// <summary>分钟配额耗尽。</summary>
        QuotaExhausted = 10,

        /// <summary>该 shard 脚本实体已达上限。</summary>
        EntityBudgetExceeded = 11,

        /// <summary>内部错误（授权层不可用等，一律 fail-closed）。</summary>
        Internal = 12,
    }

    /// <summary>
    /// Grain 裁决通过后签发的**自证式签名令牌**（设计方案 §4.2）。
    /// <para>
    /// DS 是 60Hz 独立进程，tick 内不得做跨进程 RPC，故裁决结果以令牌随网关→DS 下行帧带下去：
    /// DS 只验签、只验过期/重放/shard 归属，**不参与"谁有权"的判定**（单一真值源在 Grain）。
    /// </para>
    /// </summary>
    /// <remarks>
    /// 签名字段 <see cref="Signature"/> 覆盖除自身外的全部字段规范字节序，见
    /// <c>Horizon.Game.Message.Security.ScriptGrantTokenSigner</c>。
    /// </remarks>
    [MemoryPackable]
    [GenerateSerializer]
    public sealed partial class ScriptGrantToken
    {
        /// <summary>一次性随机数，DS 侧有界去重，防重放。</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public long Nonce { get; set; }

        /// <summary>发起角色 ID（由网关从 AuthToken 解出，非客户端自报）。</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long OperatorCharacterId { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public ScriptOp Op { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public ScriptedKind Kind { get; set; }

        /// <summary>模板 ID（已在 Grain 侧白名单校验过）。</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int TemplateId { get; set; }

        /// <summary>目标 shard。DS 必须核对"本 DS 是否持有该 shard"。</summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long TargetShardId { get; set; }

        /// <summary>生成位（服务端 Z-up 世界坐标，米）。</summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public float X { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public float Y { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public float Z { get; set; }

        /// <summary>生成数量（已在 Grain 侧按 <c>MultiEntity</c> 位与硬顶校验过）。</summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public int Count { get; set; } = 1;

        /// <summary>存活秒数；0 = 永久（触发区域不允许为 0）。</summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public int TtlSeconds { get; set; }

        /// <summary>球形触发区域半径（米）；仅 <see cref="ScriptedKind.TriggerZone"/> 使用。</summary>
        [MemoryPackOrder(11)]
        [Id(11)]
        public float Radius { get; set; }

        [MemoryPackOrder(12)]
        [Id(12)]
        public ScriptTriggerMode TriggerMode { get; set; }

        /// <summary>触发区域冷却秒数；<see cref="ScriptTriggerMode.Repeat"/> 时不得为 0。</summary>
        [MemoryPackOrder(13)]
        [Id(13)]
        public int CooldownSeconds { get; set; }

        /// <summary>要清除的目标实体 ID；仅 <see cref="ScriptOp.Despawn"/> 使用。</summary>
        [MemoryPackOrder(14)]
        [Id(14)]
        public ulong TargetEntityId { get; set; }

        /// <summary>签发时刻（Unix 毫秒）。</summary>
        [MemoryPackOrder(15)]
        [Id(15)]
        public long IssuedAtUnixMs { get; set; }

        /// <summary>过期时刻（Unix 毫秒），默认签发后 5 秒。</summary>
        [MemoryPackOrder(16)]
        [Id(16)]
        public long ExpiresAtUnixMs { get; set; }

        /// <summary>
        /// 客户端请求序号，由 Grain 从票据原样带入令牌。<b>参与签名</b>，
        /// 因此回执可以安全地对号入座，客户端无法把别人的回执认成自己的。
        /// </summary>
        [MemoryPackOrder(17)]
        [Id(17)]
        public long RequestId { get; set; }

        /// <summary>HMAC-SHA256 签名（网关与 DS 共享密钥）。</summary>
        [MemoryPackOrder(18)]
        [Id(18)]
        public byte[] Signature { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// 客户端 → 服务端的脚本指令请求（union tag 238）。
    /// <para>
    /// 客户端只填模板标识与坐标；<b>一切权限与数值判定在 Grain</b>。身份取
    /// <c>MessageHeader.AuthToken</c> 解出的 CharacterId，本消息内的
    /// <see cref="RequestId"/> 仅用于回包配对。
    /// </para>
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(238, Enums.ServiceType.System)]
    public partial class GmScriptCommandMessage : MessageUnion, INetworkMessage
    {
        /// <summary>客户端生成的请求序号，用于把结果回包配对到本地指令气泡。</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public long RequestId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public ScriptOp Op { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public ScriptedKind Kind { get; set; }

        /// <summary>模板标识（名字或数字 ID 均可，服务端按白名单目录解析）。</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string TemplateKey { get; set; } = "";

        /// <summary>生成数量，服务端硬顶裁剪。</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public int Count { get; set; } = 1;

        /// <summary>
        /// 目标位。为 null 时服务端按调用者<b>权威坐标</b>填充（绝不采用客户端上报的"我在哪"）。
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public bool HasPosition { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public float X { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public float Y { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public float Z { get; set; }

        /// <summary>存活秒数；0 = 永久。</summary>
        [MemoryPackOrder(9)]
        [Id(9)]
        public int TtlSeconds { get; set; }

        /// <summary>触发区域半径（米）。</summary>
        [MemoryPackOrder(10)]
        [Id(10)]
        public float Radius { get; set; }

        [MemoryPackOrder(11)]
        [Id(11)]
        public ScriptTriggerMode TriggerMode { get; set; }

        [MemoryPackOrder(12)]
        [Id(12)]
        public int CooldownSeconds { get; set; }

    /// <summary>仅 <see cref="ScriptOp.Despawn"/> 使用。</summary>
        [MemoryPackOrder(13)]
        [Id(13)]
        public ulong TargetEntityId { get; set; }

        /// <summary>授权管理类操作（Grant/Revoke）的目标角色；0 = 本人。</summary>
        [MemoryPackOrder(16)]
        [Id(16)]
        public long TargetOperatorId { get; set; }

        /// <summary>Grant 时要授予的权限位；原样透传给 Grain，由 Grain 判断调用者能否授予。</summary>
        [MemoryPackOrder(17)]
        [Id(17)]
        public uint RequestedFlags { get; set; }

        /// <summary>Grant 的有效小时数；0 = 不过期。</summary>
        [MemoryPackOrder(18)]
        [Id(18)]
        public int TtlHours { get; set; } = 168;

        /// <summary>Audit 的返回条数。</summary>
        [MemoryPackOrder(19)]
        [Id(19)]
        public int AuditLimit { get; set; } = 20;

        [MemoryPackOrder(14)]
        [Id(14)]
        public MessageType Type { get; set; } = MessageType.ScriptCommand;

        [MemoryPackOrder(15)]
        [Id(15)]
        public ServiceType ServiceType { get; set; } = ServiceType.System;
    }

    /// <summary>
    /// 服务端 → 客户端的脚本指令结果（union tag 239）。
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(239, Enums.ServiceType.System)]
    public partial class GmScriptResultMessage : MessageUnion, INetworkMessage
    {
        /// <summary>与请求配对的序号。</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public long RequestId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public bool Success { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public ScriptRejectReason Reason { get; set; }

        /// <summary>人类可读说明（服务端产出，客户端直接展示）。</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Message { get; set; } = "";

        /// <summary>成功生成的实体 ID（供后续 /despawn 引用）。</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public List<ulong> EntityIds { get; set; } = new();

        /// <summary>本次裁决后剩余的分钟配额。</summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public int RemainingBudget { get; set; }

        /// <summary>操作者当前权限位（整数位透传，客户端按位展示）。</summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public uint PermissionFlags { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public MessageType Type { get; set; } = MessageType.ScriptResult;

        [MemoryPackOrder(8)]
        [Id(8)]
        public ServiceType ServiceType { get; set; } = ServiceType.System;
    }

    /// <summary>
    /// 脚本化世界生成的授权位。
    /// <para>
    /// 语义是"<b>允许做什么</b>"而非"数值多大"：模板/数量/距离等具体数值一律由服务端目录与配置决定，
    /// 授权位不参与数值放大——避免"给个位就能刷世界 BOSS"这类越权组合。
    /// </para>
    /// </summary>
    /// <remarks>定义在共享契约程序集，使<b>纯逻辑裁决核</b>（Horizon.Game.Core）与 Orleans Grain
    /// 能共用同一份位定义，不出现两套枚举口径。</remarks>
    [Flags]
    [GenerateSerializer]
    public enum ScriptPermissionFlags : uint
    {
        /// <summary>无任何授权。</summary>
        None = 0,

        /// <summary>生成怪物。</summary>
        SpawnMonster = 1u << 0,

        /// <summary>生成 NPC。</summary>
        SpawnNpc = 1u << 1,

        /// <summary>派发表现型事件（Vfx/Sfx/公告，不建实体）。</summary>
        FireEvent = 1u << 2,

        /// <summary>派发玩法型事件（建实体 + TTL）。</summary>
        SpawnEvent = 1u << 3,

        /// <summary>放置触发型事件（球形触发区域）。</summary>
        PlaceTrigger = 1u << 4,

        /// <summary>清除本人生成物。</summary>
        DespawnOwned = 1u << 5,

        /// <summary>
        /// 清除**他人**的脚本生成物（按实体 ID 的 <c>/despawn &lt;id&gt;</c> 形态需要它；
        /// <c>/despawn mine</c> 只需 <see cref="DespawnOwned"/>）。
        /// <para>注意它**不**解锁静态刷怪点实体：静态点不在脚本 ID 域内，DS 一律拒绝按 ID 清除。</para>
        /// </summary>
        DespawnAny = 1u << 6,

        /// <summary>单次 count 大于 1。</summary>
        MultiEntity = 1u << 7,

        /// <summary>
        /// 允许超距，并跳过"本 shard 脚本实体预算"这一档检查。
        /// <para>它**不**涉及分片归属：令牌的 <c>TargetShardId</c> 永远绑定在服务端读到的操作者所在片，
        /// 客户端根本没有指定他片的能力，所以也不存在"用这个位越权跨片生成"。</para>
        /// </summary>
        BypassDist = 1u << 8,

        /// <summary>
        /// 保留位（全服播报）。**当前无判定消费它**：全服公告走 GM 运维面的
        /// <c>BroadcastAnnouncementAsync</c>，由 <see cref="GmOps"/> 把关。
        /// <para>
        /// 留着是为了不让一个已经写进授权表的编号被将来另作他用；不要把它当作"授予了就有效"的能力位，
        /// 也不要在授权展示里当成一条权限报给操作者。
        /// </para>
        /// </summary>
        GlobalCast = 1u << 9,

        /// <summary>查询自身授权与审计。</summary>
        Query = 1u << 10,

        /// <summary>
        /// GM 运维操作（封禁/解封/补偿/邮件/公告/传送/角色查询）。
        /// 与"世界编排"位分列：运营人员不必拥有生成怪物的能力，反之亦然。
        /// </summary>
        GmOps = 1u << 11,

        /// <summary>玩法编排档：世界编排 + 查询（不含 GmOps）。</summary>
        WorldDirector = 0x7FF,

        /// <summary>运营档：GM 运维 + 查询。</summary>
        Operator = (uint)(GmOps | Query),

        /// <summary>玩法编排 + GM 运维。</summary>
        GameMaster = (uint)(WorldDirector | GmOps),

        /// <summary>管理员档：当前等于 <see cref="GameMaster"/>，更高位留给未来跨服运营。</summary>
        Admin = (uint)(WorldDirector | GmOps),
    }

    /// <summary>
    /// 授权裁决请求。<b>由网关在权威身份之上构造</b>：
    /// <see cref="OperatorCharacterId"/> 必须取自已验签的 AuthToken，绝不来自客户端 body。
    /// </summary>
    [GenerateSerializer]
    public sealed class GmScriptTicket
    {
        /// <summary>操作者角色 ID（网关从 AuthToken 解出）。</summary>
        [Id(0)]
        public long OperatorCharacterId { get; set; }

        /// <summary>请求配对序号，原样回填到结果消息。</summary>
        [Id(1)]
        public long RequestId { get; set; }

        [Id(2)]
        public ScriptOp Op { get; set; }

        [Id(3)]
        public ScriptedKind Kind { get; set; }

        /// <summary>模板标识（名字或数字 ID 字符串），由服务端目录解析为数值。</summary>
        [Id(4)]
        public string TemplateKey { get; set; } = "";

        [Id(5)]
        public int Count { get; set; } = 1;

        /// <summary>是否显式给定坐标。false 时服务端用操作者<b>权威坐标</b>填充。</summary>
        [Id(6)]
        public bool HasPosition { get; set; }

        [Id(7)]
        public float X { get; set; }

        [Id(8)]
        public float Y { get; set; }

        [Id(9)]
        public float Z { get; set; }

        /// <summary>存活秒数；0 = 永久（触发区域不允许为 0）。</summary>
        [Id(10)]
        public int TtlSeconds { get; set; }

        /// <summary>球形触发区域半径（米）。</summary>
        [Id(11)]
        public float Radius { get; set; }

        [Id(12)]
        public ScriptTriggerMode TriggerMode { get; set; }

        [Id(13)]
        public int CooldownSeconds { get; set; }

        /// <summary>要清除的目标实体 ID；仅 <see cref="ScriptOp.Despawn"/>。</summary>
        [Id(14)]
        public ulong TargetEntityId { get; set; }

        /// <summary>
        /// 操作者当前所在 shard。<b>由网关用其权威 shard 路由解析后填入</b>（网关本就负责把上行帧
        /// 路由到对应 DS），客户端无此字段、也无法伪造。裁决后的令牌会带上它，DS 据此核对
        /// "本 DS 是否持有该 shard"。
        /// </summary>
        [Id(15)]
        public long OperatorShardId { get; set; }
    }

    #endregion
}
