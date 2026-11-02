using System;
using MemoryPack;
using Orleans;
using Horizon.Game.Message.Sync.Components;

namespace Horizon.Game.Message.Sync;

/// <summary>
/// 同步协议版本号：每次 <see cref="SnapshotPacket"/>/<see cref="InputPacket"/>/组件 schema 变更时递增。
/// 客户端 <see cref="HandshakePacket"/> 携带本地版本，服务器据此拒绝不兼容连接。
/// </summary>
public static class SyncProtocolVersion
{
    /// <summary>
    /// 当前协议版本：
    ///   v1 = 初版（PR1/PR2 引入：Handshake/Snapshot/Input/Event）。
    ///   v2 = P1-a 扩展：新增 <see cref="WorldChunkDiffPacket"/> / <see cref="WorldPatchManifestPacket"/> /
    ///        <see cref="InputAckPacket"/> / <see cref="ReconnectResumePacket"/>，并在 <see cref="HandshakePacket"/>
    ///        与 <see cref="SnapshotPacket"/> 上引入版本向量字段；旧客户端首次握手会被服务器拒绝（不兼容）。
    ///   v3 = 修复服务端握手响应类型：服务端 <c>SyncPacketHandler.HandleHandshakeAsync</c> 改为返回
    ///        <see cref="HandshakePacket"/>（回显 LocalCharacterId / InitialClientTick），使客户端
    ///        <c>SyncPacketMessageHandler.HandshakeReceived</c> 事件能正确触发。
    ///   v4 = 修复 SyncPacketHandler 单例下 _characterId 被多连接共享覆盖的 bug：
    ///        <see cref="InputPacket"/> 新增 <see cref="InputPacket.CharacterId"/> 字段，
    ///        服务端直接从输入包读取角色 ID，不再依赖握手时缓存的实例字段。
    ///   v5 = 阶段 B/C/D 同步协议扩展：
    ///        新增 <see cref="MovementStateAuthComponent"/>/<see cref="AnimationStateAuthComponent"/>/
    ///        <see cref="SceneObjectSyncPacket"/>，
    ///        <see cref="InputPacket"/> 引入冗余重传（客户端未确认队列 + 落后 5 tick 触发重传），
    ///        服务端 <c>SyncPacketHandler</c> 引入 per-characterId 去重（基于 ClientTick 序号），
    ///        <see cref="SnapshotPacket"/> 支持增量压缩（BaselineTick 非 0 时为 delta 帧）。
    ///        握手阶段严格拒绝协议版本不匹配的客户端。
    ///   v6 = <see cref="InputPacket"/> 新增 <see cref="InputPacket.MaxSpeed"/> 字段：
    ///        客户端每帧把当帧目标速度（含 Run/Sprint/Crouch 倍数）随输入上送，服务端权威回放
    ///        与客户端本地预测都用此值调用 <c>MovementFormula.Step</c>。修复"PlayerController.MoveSpeed
    ///        未进入网络同步链路，链路两端固定用 DefaultMaxSpeed=6 m/s 推进"的问题。
    ///   v7 = 新增 <see cref="BaselineResyncRequestPacket"/>：客户端 delta 解码时 baseline 不匹配，
    ///        主动请求服务端重传全量快照，替代原"直接应用 delta 兜底"，保证 baseline 一致性。
    ///   v8 = 同步链路归一重构 + 超大规模容量治理：归一唯一权威链路（旧脚本组件
    ///        <c>NetworkSyncManager</c>/<c>NpcSyncManager</c>/<c>SkillSyncHandler</c>/<c>NetworkSyncIntegration</c>/
    ///        <c>EntitySynchronizationManager</c> 物理删除）；服务端新增带宽预算限流
    ///        （<c>BandwidthBudgetOptions</c>，100kbps 红线）与兴趣区分级降频
    ///        （<c>InterestGradeOptions</c>/<c>ISyncInterestGradeStrategy</c>）；客户端新增规模档位控制
    ///        （<c>SyncScaleController</c>，20/100/1000/5000）。本次无包结构变更，版本提升用于标识
    ///        重构后的兼容语义边界。
    ///   v9 = <see cref="HandshakePacket"/> 新增 <see cref="HandshakePacket.FormulaVersion"/> 字段：
    ///        客户端在握手时上报自身确定性公式版本（<c>MovementFormula.FormulaVersion</c>），
    ///        服务器校验不匹配即拒绝连接，使"版本化公式双端同步"从测试断言变为运行时不变量。
    ///        <see cref="SyncProtocolVersion.MinimumSupported"/> 保持 5 不变（旧客户端仍被公式版本检查拒绝）。
    ///   v10 = <see cref="HandshakePacket"/> 新增 <see cref="HandshakePacket.ShardId"/> 字段（阶段 4 多 Shard 水平扩展）：
    ///        服务器握手成功后回填目标 Shard ID（<c>IShardRouter.Resolve(characterId)</c>），客户端据此知晓
    ///        自身所在 Shard（诊断/分流/未来客户端直连 DS 的依据）；客户端请求时可忽略（默认 0）。
    ///        追加在末尾且为引用类型类成员，MemoryPack 前向容错（旧读取端跳过未知尾部成员），
    ///        配合过渡期软校验（版本 != Current 仅告警不拒绝）保证 v9 客户端不中断。
    ///        <see cref="SyncProtocolVersion.MinimumSupported"/> 保持 5 不变。
    ///   v11 = <see cref="NetworkIdentityAuthComponent"/> 新增 <see cref="NetworkIdentityAuthComponent.Nickname"/>
    ///        字段（角色昵称，spawn-only 随 Identity 一次性下发，不进每 tick 通道）：使远程玩家头顶名牌
    ///        显示真实昵称而非 Player_&lt;id&gt; 占位。追加于结构末尾（order 3），旧字段 0/1/2 编号不变，
    ///        MemoryPack 前向容错（旧读取端跳过未知尾部成员）。<see cref="SyncProtocolVersion.MinimumSupported"/>
    ///        保持 5 不变，过渡期软校验保证旧客户端不中断。
    ///   v12 = 远程玩家"基础档案"端到端同步：<see cref="NetworkIdentityAuthComponent"/> 追加
    ///        <see cref="NetworkIdentityAuthComponent.Title"/>（称号名，order 4，spawn-only），
    ///        以及 <see cref="HandshakePacket"/> 追加由**网关权威填充**的
    ///        Nickname/Level/Title（order 9/10/11）——DS 灰度通道此前既不填昵称也不填等级，
    ///        导致远程玩家头顶只剩 Player_&lt;id&gt; 与 Lv.1。均为末尾追加，旧字段编号不变，
    ///        MemoryPack 前向容错。<see cref="MinimumSupported"/> 保持 5 不变。
    ///   v13 = <see cref="EntityDelta"/> 追加 <see cref="EntityDelta.Profile"/>（order 7，
    ///        承载 <see cref="Horizon.Game.Message.Sync.Components.ProfileAuthComponent"/>）：
    ///        昵称/称号此前只能随 spawn-only 的 Identity 下发，运行中改名或换称号无法告知
    ///        AOI 内其他客户端；补一条"仅变化时携带"的增量通道，避免为重发 Spawn 而销毁重建远程角色。
    ///        同时 DS 通道开始填 <see cref="EntityStateAuthComponent.StateBits"/>（死亡位），
    ///        使远程玩家倒下/复活即时可见。
    ///   v14 = <see cref="NetworkIdentityAuthComponent"/> 追加怪物外观四件套
    ///        TemplateId / ModelKey / ModelScale / StandState / Rank（order 5..9，spawn-only）：
    ///        怪物编辑器配出来的 model_key、prefab、体型、待机姿态、档位此前**无处下发**，
    ///        客户端只能把所有怪都画成占位人形。全部末尾追加，旧字段 0..4 编号不变，
    ///        旧读取端跳过未知尾部成员；<see cref="MinimumSupported"/> 保持 5 不变。
    ///   v15 = <see cref="NetworkIdentityAuthComponent"/> 追加"起手时正在吟唱"五件套
    ///        CastSkillId / CastTotalSeconds / CastElapsedSeconds / CastSchoolMask / CastInterruptible
    ///        （order 10..14，spawn-only）：施法起手是**瞬时事件**，只发给当时看得见的人，
    ///        而补发通路只有 Spawn——玩家走进一片正在读条的怪中间时，那些怪的条子对他是空的。
    ///        全部末尾追加，旧字段 0..9 编号不变；<see cref="MinimumSupported"/> 保持 5 不变。
    ///   v16 = <see cref="CombatActionPacket"/> 追加 <see cref="CombatActionPacket.StatusId"/> 与
    ///        <see cref="CombatActionPacket.StatusDurationSeconds"/>（order 8/9）：玩家技能第一次能把
    ///        控制状态挂到**怪物**身上（在此之前状态只能由怪物挂载挂到玩家身上，怪物侧两个闸口是空转的）。
    ///        末尾追加、旧字段 0..5 编号不变；<see cref="MinimumSupported"/> 保持 5 不变。
    ///   v17 = 翻越（Traversal）联机权威：新增 <see cref="TraversalRequestPacket"/>（客户端→服务器，
    ///        携带起点/终点/时长/弧高）与 <see cref="TraversalResultPacket"/>（服务器→客户端，受理/拒绝原因
    ///        + 权威终点回显）。服务端 <c>SimWorld</c> 按 <c>Horizon.Game.Sim.TraversalMath</c> 曲线推进
    ///        权威位置；老客户端不发这类包，行为不变；<see cref="MinimumSupported"/> 保持 5 不变。
    /// </summary>
    public const int Current = 17;

    /// <summary>
    /// 服务器支持的最低协议版本。低于此版本的客户端握手将被拒绝并收到 <see cref="HandshakeRejectPacket"/>，
    /// 触发强制更新流程。允许 v5/v6 并存过渡。
    /// </summary>
    public const int MinimumSupported = 5;
}

/// <summary>
/// 同步包的判别字段（写入帧头第一个字节，便于在不解码 union 的情况下做 fast-path 路由）。
/// </summary>
public enum SyncPacketKind : byte
{
    Unknown = 0,
    Handshake = 1,
    Snapshot = 2,
    Input = 3,
    Event = 4,
    /// <summary>P3-b：世界 voxel/prefab diff 流（服务器→客户端，按 ChunkCell 推送）。</summary>
    WorldChunkDiff = 5,
    /// <summary>P5：世界补丁清单/版本向量协商（服务器→客户端，握手后下发）。</summary>
    WorldPatchManifest = 6,
    /// <summary>P6：服务器对客户端 input 的确认（携带 LastProcessedClientTick，用于 reconciliation）。</summary>
    InputAck = 7,
    /// <summary>P6-b：客户端断线重连时的 resume 握手（携带 lastApplied tick / diff seq / patch version）。</summary>
    ReconnectResume = 8,
    /// <summary>阶段 1：NarrativePro 交互槽状态同步（服务器→客户端，按交互槽推送占用/进行中/结束/被抢占等状态）。</summary>
    InteractionSync = 9,
    /// <summary>阶段 C：场景对象状态同步（服务器→客户端，按对象推送开启/激活/锁定/重置/冷却/归属/可选 Transform）。</summary>
    SceneObjectSync = 10,
    /// <summary>多玩家 AOI：动态 chunk 订阅变更（服务器→客户端，下发本次新增/移除的 chunk key 集合）。</summary>
    SubscriptionUpdate = 11,
    /// <summary>P1.3：握手拒绝（服务器→客户端，协议版本低于 <see cref="SyncProtocolVersion.MinimumSupported"/> 时下发，含最低版本号）。</summary>
    HandshakeReject = 12,
    /// <summary>P1.4：战斗动作（客户端→服务器，攻击/技能/道具使用请求）。</summary>
    CombatAction = 13,
    /// <summary>P1.4：伤害通知（服务器→客户端，携带伤害数值/类型/暴击/剩余HP）。</summary>
    Damage = 14,
    /// <summary>P1.4：死亡通知（服务器→客户端，实体死亡/击杀者/死亡类型）。</summary>
    Death = 15,
    /// <summary>客户端→服务器：baseline 缺失重传请求（delta 解码时 baseline 不匹配，请求服务端下发全量快照）。</summary>
    BaselineResyncRequest = 16,

    /// <summary>客户端→服务器：从一具尸体上取走某一槽（2026-10-28 拍板：尸体拾取）。</summary>
    CorpseLootTakeRequest = 17,

    /// <summary>服务器→客户端：拾取结果（状态 + 取走时的槽内容）。</summary>
    CorpseLootTakeResult = 18,

    /// <summary>
    /// 客户端→服务器：翻越（mantle/vault）计划请求。客户端用 Flax 射线检测出边沿后提交起点/终点/时长，
    /// 服务端按 <c>TraversalMath</c> 同一曲线推进权威位置。
    /// </summary>
    TraversalRequest = 19,

    /// <summary>服务器→客户端：翻越计划裁决结果（受理/拒绝原因 + 权威终点回显）。</summary>
    TraversalResult = 20,
}

/// <summary>
/// 同步协议消息基类（以 <see cref="Horizon.Game.Message.MessageUnion"/> 为蓝本）。
/// 通过 <see cref="MemoryPackUnionAttribute"/> 注册各派生类型，实现单一 channel 多类型派发。
/// 本类负责系统自主同步消息（快照、输入、心跳等），
/// 与 <see cref="MessageUnion"/>（用户主动请求或响应消息）在职责上互补。
/// 基类元字段（Kind、ProtocolVersion）的 Orleans [Id] 置于高位（254/255），
/// 使派生类从 0 开始编号，与 <see cref="MessageUnion"/> 的子类保持一致的编号风格。
/// </summary>
[MemoryPackable(SerializeLayout.Explicit)]
[GenerateSerializer]
[MemoryPackUnion(0, typeof(HandshakePacket))]
[MemoryPackUnion(1, typeof(SnapshotPacket))]
[MemoryPackUnion(2, typeof(InputPacket))]
[MemoryPackUnion(3, typeof(EventPacket))]
[MemoryPackUnion(4, typeof(WorldChunkDiffPacket))]
[MemoryPackUnion(5, typeof(WorldPatchManifestPacket))]
[MemoryPackUnion(6, typeof(InputAckPacket))]
[MemoryPackUnion(7, typeof(ReconnectResumePacket))]
// 注意：union tag 8 对应 SyncPacketKind.InteractionSync=9（枚举中 Unknown=0 占位导致偏移 1）。
// MemoryPack union 只要求 tag 唯一（不要求连续）；"连续递增"仅为本项目约定。此处显式写 8 而非 (byte)SyncPacketKind.InteractionSync(=9)。
[MemoryPackUnion(8, typeof(InteractionSyncPacket))]
// 注意：union tag 9 对应 SyncPacketKind.SceneObjectSync=10（枚举中 Unknown=0 占位导致偏移 1）。
// 紧跟 InteractionSyncPacket 的 tag 8，保持 0..9 连续递增。
[MemoryPackUnion(9, typeof(SceneObjectSyncPacket))]
// 注意：union tag 10 对应 SyncPacketKind.SubscriptionUpdate=11（枚举中 Unknown=0 占位导致偏移 1）。
// 紧跟 SceneObjectSyncPacket 的 tag 9，保持 0..10 连续递增。
[MemoryPackUnion(10, typeof(SubscriptionUpdatePacket))]
// P1.3：握手拒绝包（协议版本过低时服务器下发）。
[MemoryPackUnion(11, typeof(HandshakeRejectPacket))]
// P1.4：战斗协议包。
[MemoryPackUnion(12, typeof(CombatActionPacket))]
[MemoryPackUnion(13, typeof(DamagePacket))]
[MemoryPackUnion(14, typeof(DeathPacket))]
// BaselineResyncRequestPacket：tag 15 对应 SyncPacketKind.BaselineResyncRequest=16（枚举中 Unknown=0 占位导致偏移 1）。
// 紧跟 DeathPacket 的 tag 14，保持 0..15 连续递增。
[MemoryPackUnion(15, typeof(BaselineResyncRequestPacket))]
[MemoryPackUnion(16, typeof(CorpseLootTakeRequestPacket))]
[MemoryPackUnion(17, typeof(CorpseLootTakeResultPacket))]
// v17：翻越权威包（tag 18/19 对应 SyncPacketKind.TraversalRequest=19 / TraversalResult=20）。
[MemoryPackUnion(18, typeof(TraversalRequestPacket))]
[MemoryPackUnion(19, typeof(TraversalResultPacket))]
public abstract partial class SyncPacket
{
    /// <summary>包种类（冗余字段，便于不解码 union 时也能识别）。</summary>
    [MemoryPackOrder(0)]
    [Id(254)]
    public SyncPacketKind Kind { get; set; }

    /// <summary>协议版本号（应等于 <see cref="SyncProtocolVersion.Current"/>）。</summary>
    [MemoryPackOrder(1)]
    [Id(255)]
    public int ProtocolVersion { get; set; } = SyncProtocolVersion.Current;
}

/// <summary>
/// 握手包：客户端连接成功后第一个包，告知协议版本与本地玩家身份。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public sealed partial class HandshakePacket : SyncPacket
{
    /// <summary>本地玩家所控制的服务器实体 ID（用于服务器把 input 路由到对应 grain）。</summary>
    [MemoryPackOrder(2)]
    [Id(0)]
    public ulong LocalCharacterId { get; set; }

    /// <summary>客户端 tick 起始值（用于服务器对齐 reconciliation 时间轴）。</summary>
    [MemoryPackOrder(3)]
    [Id(1)]
    public long InitialClientTick { get; set; }

    /// <summary>客户端初始位置 X（服务器创建实体时复用，避免落地修正抖动）。</summary>
    [MemoryPackOrder(4)]
    [Id(2)]
    public float InitialX { get; set; }

    /// <summary>客户端初始位置 Y。</summary>
    [MemoryPackOrder(5)]
    [Id(3)]
    public float InitialY { get; set; }

    /// <summary>客户端初始位置 Z。</summary>
    [MemoryPackOrder(6)]
    [Id(4)]
    public float InitialZ { get; set; }

    /// <summary>
    /// 客户端使用的确定性公式版本（<see cref="Horizon.Game.Sim.MovementFormula.FormulaVersion"/>）。
    /// 服务器在 <c>HandleHandshakeAsync</c> 中校验：不匹配则拒绝连接并返回 <see cref="HandshakeRejectPacket"/>，
    /// 避免双端公式版本不一致导致校正风暴。
    /// 默认 0 表示未设置（旧客户端），服务器会拒绝。
    /// </summary>
    [MemoryPackOrder(7)]
    [Id(5)]
    public int FormulaVersion { get; set; }

    /// <summary>
    /// 服务器分配的目标 Shard ID（服务器→客户端回显字段；客户端请求时可忽略，默认 0）。<br/>
    /// 握手成功后服务器把 <c>IShardRouter.Resolve(characterId)</c> 的结果回填，
    /// 使客户端知晓自身所在 Shard（阶段 4 多 Shard 水平扩展：诊断/分流/未来客户端直连 DS 的依据）。<br/>
    /// 默认 0 = 未知（单 Shard 或旧客户端）。
    /// </summary>
    [MemoryPackOrder(8)]
    [Id(6)]
    public long ShardId { get; set; }

    // 以下三字段是 **网关→DS 的权威注入通道**，不是客户端上行字段：
    // 网关在转发握手前无条件用 CharacterGrain 查得的档案覆盖它们
    // （见 SyncHandshakeHandler）。客户端即便自行填入也会被覆盖，故不构成伪造入口。
    // DS 侧在 AddPlayer 时落到 DsPlayer，spawn 时经 Identity/State 下发给 AOI 内其他客户端。

    /// <summary>角色昵称（网关权威填充；DS 通道专用）。</summary>
    [MemoryPackOrder(9)]
    [Id(7)]
    public string Nickname { get; set; } = "";

    /// <summary>角色等级（网关权威填充；DS 通道专用）。</summary>
    [MemoryPackOrder(10)]
    [Id(8)]
    public int Level { get; set; }

    /// <summary>角色称号名（网关权威填充，空=未佩戴；DS 通道专用）。</summary>
    [MemoryPackOrder(11)]
    [Id(9)]
    public string Title { get; set; } = "";

    /// <summary>
    /// 角色阵营（0 未入 / 1 正派 / 2 邪派；网关权威填充，DS 通道专用）。
    /// <para>DS 存进 <c>DsPlayer.Faction</c> 后，玩家互殴的合法性只由
    /// <c>FactionCore.Evaluate</c> 判——客户端自报的阵营不采信。</para>
    /// </summary>
    [MemoryPackOrder(12)]
    [Id(10)]
    public int Faction { get; set; }

    public HandshakePacket() { Kind = SyncPacketKind.Handshake; }
}

/// <summary>
/// 服务器→客户端 快照包：可以是 baseline（全量）或 delta（仅变化）。
/// <see cref="BaselineTick"/> = 0 表示本包自身为 baseline；
/// 否则客户端必须先持有 BaselineTick 对应的 baseline 才能解码本包（缺失则请求重传）。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public sealed partial class SnapshotPacket : SyncPacket
{
    /// <summary>本包对应的服务器 tick。</summary>
    [MemoryPackOrder(2)]
    [Id(0)]
    public long ServerTick { get; set; }

    /// <summary>基线 tick；为 0 表示这是一个 baseline 自身。</summary>
    [MemoryPackOrder(3)]
    [Id(1)]
    public long BaselineTick { get; set; }

    /// <summary>本帧实体变更。</summary>
    [MemoryPackOrder(4)]
    [Id(2)]
    public EntityDelta[] Deltas { get; set; } = Array.Empty<EntityDelta>();

    public SnapshotPacket() { Kind = SyncPacketKind.Snapshot; }
}

/// <summary>
/// 客户端→服务器 输入包：定频上行，永不压缩以保延迟。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public sealed partial class InputPacket : SyncPacket
{
    /// <summary>客户端 tick / 输入序号。</summary>
    [MemoryPackOrder(2)]
    [Id(0)]
    public long ClientTick { get; set; }

    /// <summary>位掩码：移动方向、跳跃、技能 1..N。</summary>
    [MemoryPackOrder(3)]
    [Id(1)]
    public uint InputBits { get; set; }

    /// <summary>视角朝向（Yaw 弧度）。</summary>
    [MemoryPackOrder(4)]
    [Id(2)]
    public float LookYaw { get; set; }

    /// <summary>视角俯仰（Pitch 弧度）。</summary>
    [MemoryPackOrder(5)]
    [Id(3)]
    public float LookPitch { get; set; }

    /// <summary>移动输入 X（-1..1）。</summary>
    [MemoryPackOrder(6)]
    [Id(4)]
    public float MoveX { get; set; }

    /// <summary>移动输入 Y（-1..1）。</summary>
    [MemoryPackOrder(7)]
    [Id(5)]
    public float MoveY { get; set; }

    /// <summary>
    /// 发送该输入的本地玩家角色 ID（与 <see cref="HandshakePacket.LocalCharacterId"/> 同义）。
    /// 服务端 <c>SyncPacketHandler</c> 为单例，无法安全地在实例字段中缓存每连接的 characterId，
    /// 因此由客户端在每个 InputPacket 中显式携带，服务端直接读取以路由输入到对应 grain。
    /// </summary>
    [MemoryPackOrder(8)]
    [Id(6)]
    public ulong CharacterId { get; set; }

    /// <summary>客户端预测的本 tick 结束位置 X（米）。服务端 MovementValidator 据此做权威校验。</summary>
    [MemoryPackOrder(9)]
    [Id(7)]
    public float PredictedEndX { get; set; }

    /// <summary>客户端预测的本 tick 结束位置 Y（米）。</summary>
    [MemoryPackOrder(10)]
    [Id(8)]
    public float PredictedEndY { get; set; }

    /// <summary>客户端预测的本 tick 结束位置 Z（米）。</summary>
    [MemoryPackOrder(11)]
    [Id(9)]
    public float PredictedEndZ { get; set; }

    /// <summary>
    /// 本帧目标最大水平移动速度（米/秒）。由客户端根据 PlayerController.MoveSpeed 及当前状态
    /// （Run/Sprint/Crouch 倍数）计算后随输入上送，服务端权威回放与客户端本地预测都用此值
    /// 调用 <c>MovementFormula.Step</c>，保证两端按同一速度推进。
    /// <para>
    /// 取值约定：
    /// <list type="bullet">
    ///   <item>&gt; 0：使用客户端指定的速度（服务端会按 <c>HardSpeedCap</c> 上限校验防作弊）。</item>
    ///   <item>&lt;= 0：服务端兜底使用 <c>MovementFormula.DefaultMaxSpeed</c>（向后兼容旧客户端）。</item>
    /// </list>
    /// </para>
    /// </summary>
    [MemoryPackOrder(12)]
    [Id(10)]
    public float MaxSpeed { get; set; }

    public InputPacket() { Kind = SyncPacketKind.Input; }
}

/// <summary>
/// 离散事件包（技能命中、伤害、死亡、特效触发等），与 snapshot 解耦走可靠通道。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public sealed partial class EventPacket : SyncPacket
{
    /// <summary>事件发生时的服务器 tick。</summary>
    [MemoryPackOrder(2)]
    [Id(0)]
    public long ServerTick { get; set; }

    /// <summary>事件序列。</summary>
    [MemoryPackOrder(3)]
    [Id(1)]
    public SyncEvent[] Events { get; set; } = Array.Empty<SyncEvent>();

    public EventPacket() { Kind = SyncPacketKind.Event; }
}

/// <summary>
/// 单实体的快照变更 / 全量数据。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public partial struct EntityDelta
{
    /// <summary>实体 ID。</summary>
    [MemoryPackOrder(0)] [Id(0)] public ulong EntityId;

    /// <summary>变更类型。</summary>
    [MemoryPackOrder(1)] [Id(1)] public EntityDeltaKind Kind;

    /// <summary>身份信息（仅 Spawn / 全量时有效）。</summary>
    [MemoryPackOrder(2)] [Id(2)] public NetworkIdentityAuthComponent? Identity;

    /// <summary>Transform（变更时携带）。</summary>
    [MemoryPackOrder(3)] [Id(3)] public AuthTransformComponent? Transform;

    /// <summary>状态（变更时携带）。</summary>
    [MemoryPackOrder(4)] [Id(4)] public EntityStateAuthComponent? State;

    /// <summary>移动状态（MovementMode + 水平速度 + 落地标志，10Hz 心跳 + 变化触发）。</summary>
    [MemoryPackOrder(5)] [Id(5)] public MovementStateAuthComponent? MovementState;

    /// <summary>动画状态（仅 Montage 触发/结束事件时携带，循环动画由客户端根据 MovementState 驱动）。</summary>
    [MemoryPackOrder(6)] [Id(6)] public AnimationStateAuthComponent? AnimationState;

    /// <summary>
    /// 玩家基础档案变更（v13，仅玩家实体、仅变化时携带）。
    /// 昵称/称号在 Identity 里是 spawn-only，运行中改名/换称号必须另开通道，
    /// 否则只能重发 Spawn —— 而重复 Spawn 会让客户端销毁重建远程角色。
    /// </summary>
    [MemoryPackOrder(7)] [Id(7)] public ProfileAuthComponent? Profile;
}

/// <summary>实体增量种类。</summary>
public enum EntityDeltaKind : byte
{
    /// <summary>新建实体（Identity/Transform/State 都应有效）。</summary>
    Spawn = 1,

    /// <summary>已有实体的字段更新（仅携带变化字段）。</summary>
    Update = 2,

    /// <summary>实体销毁。</summary>
    Despawn = 3,
}

/// <summary>事件类型（与服务器侧枚举对齐）。</summary>
public enum SyncEventKind : ushort
{
    Unknown = 0,
    SkillCast = 1,
    Damage = 2,
    Death = 3,
    Vfx = 4,
    Sfx = 5,
    Pickup = 6,
    /// <summary>阶段 1：交互开始（玩家占用交互槽）。</summary>
    InteractStart = 7,
    /// <summary>阶段 1：交互结束（正常完成或主动取消）。</summary>
    InteractEnd = 8,
    /// <summary>阶段 1：交互被抢占（槽位被更高优先级的交互者夺走）。</summary>
    InteractStolen = 9,
    /// <summary>位置修正事件：Payload 为序列化的 CorrectionPacket，客户端 EventApplySystem 提取后路由到 CorrectionReceiveBuffer。</summary>
    Correction = 10,
    /// <summary>输入确认事件：Payload 为序列化的 InputAckPacket，客户端 EventApplySystem 提取后路由到 InputAckReceiveBuffer。
    /// 服务端在 TickAsync 处理完输入后通过此事件下发 LastProcessedClientTick，让客户端定期清理 InputHistoryBuffer
    /// 并推进 InputSendSystem._lastAckedClientTick，避免冗余重传每帧触发。
    /// </summary>
    InputAck = 11,
    /// <summary>
    /// 脚本化生成结果回执：Payload 为序列化的 <c>ScriptedSpawnResultPayload</c>，
    /// 由服务端在指令生效后**定向**发给操作者会话（不是广播——别人不该看到你的指令回执）。
    /// <para>尾部追加，旧客户端遇未知 Kind 只记日志不受影响。</para>
    /// </summary>
    ScriptedSpawnResult = 12,

    /// <summary>
    /// 怪物起手（吟唱开始）：<c>SourceEntityId</c>=怪、<c>TargetEntityId</c>=起手锁定的目标、
    /// <c>IntValue</c>=技能 id、<c>FloatValue</c>=吟唱总秒数，
    /// <c>Payload</c> 为序列化的 <see cref="MonsterCastPayload"/>（系别/可打断/可否移动）。
    /// <para>
    /// <b>为什么只发"起手"而不逐 tick 发进度</b>：吟唱时长已经随事件给了，客户端按本地时钟插值就是
    /// WoW 的观感（条子在长，施法者不动）；每 tick 一条的话，一条 1.2 秒的吟唱要烧掉 72 帧带宽，
    /// 而它是**每个在场玩家都要收**的广播。
    /// </para>
    /// <para>尾部追加，旧客户端遇未知 Kind 只记日志不受影响（同 <see cref="ScriptedSpawnResult"/> 的纪律）。</para>
    /// </summary>
    MonsterCastStart = 13,

    /// <summary>
    /// 怪物吟唱结束（条子必须收掉）：<c>IntValue</c>=结束原因，取值见 <see cref="MonsterCastEndReason"/>
    /// （0=完成、1=被打断、2=取消）。施法者死亡/消失**不发**这条——那条走 Despawn 全广播，实体连带条子一起没了。
    /// <para>每个"读条结束"的出口都要发同一条：漏掉任何一个，客户端那根条子会一直挂在头顶不动——
    /// 比"没有施法条"更糟，因为它谎报"这只怪还在读条"。</para>
    /// </summary>
    MonsterCastStop = 14,

    /// <summary>
    /// 怪物进战喊话：<c>IntValue</c>=**文本号**（<c>aggro_say_id</c> 指的那一号），<c>SourceEntityId</c>=喊话的怪。
    /// <para>线上**只有号，没有文案**：文案随客户端内容包发布（与外观表 <c>client_visuals.json</c> 同一口径）。
    /// 把文案塞进实时包 = 把展示文本当身份用——翻译一次就作废全部旧引用（这条工程里栽过）。</para>
    /// <para>客户端不认识这一种时会被每个 handler 的 <c>if (kind != …) continue</c> 安静跳过，
    /// 所以服务端先落地、气泡渲染后到，中间态是"有回执没画面"，不是"报错"。</para>
    /// </summary>
    MonsterSay = 15,

    /// <summary>
    /// 尸体拾取窗口到期时**东西还没拿完**：<c>SourceEntityId</c>=尸体实体、<c>TargetEntityId</c>=收到提示的角色、
    /// <c>IntValue</c>=没拿走的件数。
    /// <para>2026-10-29 拍板："窗口到期而东西没拿完，给一条提示"。只发给击杀参与者里**还在线**的人，
    /// 且每人一条（同一具尸体不重复发）——它是"你错过了"的提示，不是每拍刷新的状态。</para>
    /// <para>与其它事件一样，客户端不认识这一种时会被安静跳过：服务端先落地、提示 UI 后到，
    /// 中间态是"有回执没提示"，不是"报错"。</para>
    /// </summary>
    CorpseLootExpired = 16,
}

/// <summary>
/// 通用同步事件：Payload 由 sender/receiver 按 Kind 解释，
/// 复杂 Payload 可在双方约定后用 MemoryPack 二次序列化嵌入 <see cref="Payload"/>。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public partial struct SyncEvent
{
    [MemoryPackOrder(0)] [Id(0)] public SyncEventKind Kind;

    /// <summary>事件源实体（攻击者 / 施法者）。</summary>
    [MemoryPackOrder(1)] [Id(1)] public ulong SourceEntityId;

    /// <summary>事件目标实体（受害者 / 拾取者）。</summary>
    [MemoryPackOrder(2)] [Id(2)] public ulong TargetEntityId;

    /// <summary>整型主参数（技能 ID / 伤害值）。</summary>
    [MemoryPackOrder(3)] [Id(3)] public int IntValue;

    /// <summary>浮点参数（暴击倍率 / 持续时间）。</summary>
    [MemoryPackOrder(4)] [Id(4)] public float FloatValue;

    /// <summary>额外二进制载荷。</summary>
    [MemoryPackOrder(5)] [Id(5)] public byte[]? Payload;
}

// ---------------------------------------------------------------------------
// P1-a：以下四类包属于"补齐服务器响应 + 大世界同步基础设施"的最小协议扩展。
// 它们的运行时处理（grain、Gateway 路由、客户端 system）已由 InteractionApplySystem、SyncPacketHandler 等模块实装；
// 本文件承担 wire-protocol 形态定义，运行时处理由上层模块（InteractionApplySystem / SyncPacketHandler 等）落地。
// ---------------------------------------------------------------------------

/// <summary>
/// <see cref="WorldChunkDiffPacket.Payload"/> 的内部载荷类型标识（P8-8.3）。
/// 由于 <see cref="WorldChunkDiffPacket.Payload"/> 被复用于承载多种序列化包，
/// 客户端需依赖本字段决定如何反序列化，避免类型歧义。
/// </summary>
public enum WorldChunkDiffPayloadType : byte
{
    /// <summary>Payload 为 <see cref="EntityDelta"/>[]（快照/生命周期 delta）。</summary>
    EntityDelta = 0,
    /// <summary>Payload 为 <see cref="InteractionSyncPacket"/>（交互槽状态同步）。</summary>
    InteractionSync = 1,
    /// <summary>Payload 为 <see cref="EventPacket"/>（离散事件，含包裹了 CorrectionPacket 的事件）。</summary>
    Event = 2,
    /// <summary>Payload 为直接嵌入的位移校正包（CorrectionPacket，预留）。</summary>
    Correction = 3,
    /// <summary>Task C.4：Payload 为 <see cref="SceneObjectSyncPacket"/>（场景对象状态同步）。</summary>
    SceneObjectSync = 4,
}

/// <summary>
/// 服务器→客户端：世界 voxel/prefab diff 数据流（P3-b）。
/// 每条 diff 对应一个 ChunkCell（16m），按 <see cref="ChunkMortonKey"/> 寻址；
/// 客户端按 <see cref="DiffSeqStart"/>..<see cref="DiffSeqEnd"/> 区间应用并更新 <c>WorldDiffBufferComponent</c>，
/// 缺包时通过 <see cref="ReconnectResumePacket"/> 主动请求补流。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public sealed partial class WorldChunkDiffPacket : SyncPacket
{
    /// <summary>目标 ChunkCell 的 Morton 键（24 位地址压入 ulong）。</summary>
    [MemoryPackOrder(2)]
    [Id(0)]
    public ulong ChunkMortonKey { get; set; }

    /// <summary>本批 diff 的起始序号（含）。</summary>
    [MemoryPackOrder(3)]
    [Id(1)]
    public long DiffSeqStart { get; set; }

    /// <summary>本批 diff 的终止序号（含）；客户端落盘后应更新到该值。</summary>
    [MemoryPackOrder(4)]
    [Id(2)]
    public long DiffSeqEnd { get; set; }

    /// <summary>该 chunk 当前所基于的 baseline 版本（与 <see cref="WorldPatchManifestPacket"/> 对齐）。</summary>
    [MemoryPackOrder(5)]
    [Id(3)]
    public int BaselineVersion { get; set; }

    /// <summary>该 chunk 当前累积的 patch 版本。</summary>
    [MemoryPackOrder(6)]
    [Id(4)]
    public int WorldPatchVersion { get; set; }

    /// <summary>
    /// 已序列化（且可能 LZ4 压缩）的 voxel/prefab op 序列。
    /// 内部 schema 由 <c>Horizon.Game.World</c>（P3-a 引入）定义，本层仅做透传，避免 wire-schema 频繁变更。
    /// </summary>
    [MemoryPackOrder(7)]
    [Id(5)]
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    /// <summary>当 <see cref="Payload"/> 为 LZ4 压缩流时为真。</summary>
    [MemoryPackOrder(8)]
    [Id(6)]
    public bool PayloadCompressed { get; set; }

    /// <summary>
    /// <see cref="Payload"/> 的内部载荷类型（P8-8.3）。
    /// 客户端据此决定如何反序列化 <see cref="Payload"/>，消除多类型复用同一字段的歧义。
    /// </summary>
    [MemoryPackOrder(9)]
    [Id(7)]
    public WorldChunkDiffPayloadType PayloadType { get; set; }

    public WorldChunkDiffPacket() { Kind = SyncPacketKind.WorldChunkDiff; }
}

/// <summary>
/// 服务器→客户端：世界补丁清单（P5）。
/// 在 <see cref="HandshakePacket"/> 完成后下发，告知客户端"当前服务器接受的世界版本向量"
/// 以及需要从启动器（GengDi）补齐的 chunk patch 列表（仅给出 manifest URL/哈希，文件下载走 CDN）。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public sealed partial class WorldPatchManifestPacket : SyncPacket
{
    /// <summary>当前权威 baseline 版本（与游戏二进制版本一一对应，由 GengDi 通过 .pak 投递）。</summary>
    [MemoryPackOrder(2)]
    [Id(0)]
    public int BaselineVersion { get; set; }

    /// <summary>当前权威 worldPatch 版本（每次正式发布时由 <c>Horizon.Tools.WorldPatchBuilder</c> 累加）。</summary>
    [MemoryPackOrder(3)]
    [Id(1)]
    public int WorldPatchVersion { get; set; }

    /// <summary>清单根 URL（CDN 入口；客户端按 worldPatchVersion 拼接子路径）。</summary>
    [MemoryPackOrder(4)]
    [Id(2)]
    public string ManifestUrl { get; set; } = string.Empty;

    /// <summary>清单文件的 SHA256（hex）。客户端下载 manifest 后须校验一致。</summary>
    [MemoryPackOrder(5)]
    [Id(3)]
    public string ManifestSha256 { get; set; } = string.Empty;

    /// <summary>在线增量与本地补丁的边界 diff 序号；客户端 ≥ 本值则可纯走在线 diff。</summary>
    [MemoryPackOrder(6)]
    [Id(4)]
    public long PatchCutoverDiffSeq { get; set; }

    public WorldPatchManifestPacket() { Kind = SyncPacketKind.WorldPatchManifest; }
}

/// <summary>
/// 服务器→客户端：input ACK（P6）。
/// 与 <see cref="SnapshotPacket"/> 解耦，可在两次 snapshot 之间高频下发以缩短 reconciliation 窗口；
/// <see cref="LastProcessedClientTick"/> 用于客户端 <c>ReconciliationSystem</c> 丢弃已确认的预测输入并按需 rewind。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public sealed partial class InputAckPacket : SyncPacket
{
    /// <summary>服务器最近一次处理过的客户端 tick（含）。</summary>
    [MemoryPackOrder(2)]
    [Id(0)]
    public long LastProcessedClientTick { get; set; }

    /// <summary>服务器当前 tick（与 <see cref="SnapshotPacket.ServerTick"/> 同义，便于纯 ACK 帧也带时基）。</summary>
    [MemoryPackOrder(3)]
    [Id(1)]
    public long ServerTick { get; set; }

    /// <summary>当 RTT 估算需要时的 echo（客户端 input 中的 ClientTick），可为 0 表示未携带。</summary>
    [MemoryPackOrder(4)]
    [Id(2)]
    public long EchoClientTick { get; set; }

    public InputAckPacket() { Kind = SyncPacketKind.InputAck; }
}

/// <summary>
/// 客户端→服务器：断线重连后的 resume 握手（P6-b）。
/// 服务器据此决定 "继续推增量 / 强制 baseline 重传 / 让客户端先去补 worldPatch"。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public sealed partial class ReconnectResumePacket : SyncPacket
{
    /// <summary>本地玩家所控制的角色 ID（与 <see cref="HandshakePacket.LocalCharacterId"/> 同义）。</summary>
    [MemoryPackOrder(2)]
    [Id(0)]
    public ulong LocalCharacterId { get; set; }

    /// <summary>客户端最后已应用的 snapshot tick。</summary>
    [MemoryPackOrder(3)]
    [Id(1)]
    public long LastAppliedSnapshotTick { get; set; }

    /// <summary>客户端最后已应用的世界 diff 全局序号（跨 chunk 单调递增的 high-water mark）。</summary>
    [MemoryPackOrder(4)]
    [Id(2)]
    public long LastAppliedDiffSeq { get; set; }

    /// <summary>客户端本地 baseline 版本（来自 .pak）。</summary>
    [MemoryPackOrder(5)]
    [Id(3)]
    public int BaselineVersion { get; set; }

    /// <summary>客户端本地已套用的 worldPatch 版本（来自 GengDi 的 WorldData/）。</summary>
    [MemoryPackOrder(6)]
    [Id(4)]
    public int WorldPatchVersion { get; set; }

    public ReconnectResumePacket() { Kind = SyncPacketKind.ReconnectResume; }
}

// ---------------------------------------------------------------------------
// 阶段 1：NarrativePro 交互槽状态同步协议扩展。
// 承担 wire-protocol 形态定义，运行时处理（grain / Gateway 路由 / 客户端 system）已由 InteractionApplySystem、SyncPacketHandler 等实装。
// ---------------------------------------------------------------------------

/// <summary>
/// 服务器→客户端：交互槽状态同步（阶段 1）。
/// 用于 NarrativePro 交互槽的占用/进行中/结束/被抢占等状态推送，
/// 与 <see cref="SnapshotPacket"/> 解耦走独立通道，避免高频交互状态污染 baseline/delta 流。
/// </summary>
[MemoryPackable(SerializeLayout.Explicit)]
[GenerateSerializer]
public sealed partial class InteractionSyncPacket : SyncPacket
{
    /// <summary>交互槽索引（同一 InteractableId 下可有多个槽位）。</summary>
    [MemoryPackOrder(2)]
    [Id(0)]
    public int SlotIdx { get; set; }

    /// <summary>可交互对象的 NetworkId。</summary>
    [MemoryPackOrder(3)]
    [Id(1)]
    public long InteractableId { get; set; }

    /// <summary>交互者（玩家）的 NetworkId。</summary>
    [MemoryPackOrder(4)]
    [Id(2)]
    public long InteractorId { get; set; }

    /// <summary>交互状态位标志（占用/进行中/结束/被抢占等）。</summary>
    [MemoryPackOrder(5)]
    [Id(3)]
    public byte StateBits { get; set; }

    /// <summary>本包对应的服务器 tick。</summary>
    [MemoryPackOrder(6)]
    [Id(4)]
    public long ServerTick { get; set; }

    public InteractionSyncPacket() { Kind = SyncPacketKind.InteractionSync; }
}

// ---------------------------------------------------------------------------
// 阶段 C：场景对象状态同步协议扩展。
// 承担 wire-protocol 形态定义，运行时处理（grain / Gateway 路由 / 客户端 system）由上层模块落地。
// ---------------------------------------------------------------------------

/// <summary>
/// 服务器→客户端：场景对象状态同步（阶段 C）。
/// 用于宝箱/开关/门/拉杆/传送门等场景对象的开启/激活/锁定/重置/冷却/归属等状态推送，
/// 与 <see cref="SnapshotPacket"/> 解耦走独立通道，避免高频场景对象状态污染 baseline/delta 流。
/// 可选承载 <see cref="TransformX/Y/Z"/> 与 <see cref="TransformPitch/Yaw/Roll"/>，
/// 由 <see cref="HasTransform"/> 标记是否有效（仅可移动场景对象需要）。
/// </summary>
[MemoryPackable(SerializeLayout.Explicit)]
[GenerateSerializer]
public sealed partial class SceneObjectSyncPacket : SyncPacket
{
    /// <summary>场景对象的全局唯一 ID。</summary>
    [MemoryPackOrder(2)]
    [Id(0)]
    public ulong ObjectId { get; set; }

    /// <summary>状态位掩码（Opened/Activated/Locked/Reset，参考 <see cref="SceneObjectStateBits"/>）。</summary>
    [MemoryPackOrder(3)]
    [Id(1)]
    public uint StateBits { get; set; }

    /// <summary>冷却结束的服务器 tick（0 表示无冷却）。</summary>
    [MemoryPackOrder(4)]
    [Id(2)]
    public long CooldownEndTick { get; set; }

    /// <summary>当前归属角色 ID（0 表示无归属）。</summary>
    [MemoryPackOrder(5)]
    [Id(3)]
    public ulong OwnerCharacterId { get; set; }

    /// <summary>标记 <see cref="TransformX/Y/Z"/> 与 <see cref="TransformPitch/Yaw/Roll"/> 是否有效。</summary>
    [MemoryPackOrder(6)]
    [Id(4)]
    public bool HasTransform { get; set; }

    /// <summary>可选 Transform - X 坐标（仅 <see cref="HasTransform"/> 为 true 时有效）。</summary>
    [MemoryPackOrder(7)]
    [Id(5)]
    public float TransformX { get; set; }

    /// <summary>可选 Transform - Y 坐标。</summary>
    [MemoryPackOrder(8)]
    [Id(6)]
    public float TransformY { get; set; }

    /// <summary>可选 Transform - Z 坐标。</summary>
    [MemoryPackOrder(9)]
    [Id(7)]
    public float TransformZ { get; set; }

    /// <summary>可选 Transform - Pitch（弧度）。</summary>
    [MemoryPackOrder(10)]
    [Id(8)]
    public float TransformPitch { get; set; }

    /// <summary>可选 Transform - Yaw（弧度）。</summary>
    [MemoryPackOrder(11)]
    [Id(9)]
    public float TransformYaw { get; set; }

    /// <summary>可选 Transform - Roll（弧度）。</summary>
    [MemoryPackOrder(12)]
    [Id(10)]
    public float TransformRoll { get; set; }

    /// <summary>本包对应的服务器 tick。</summary>
    [MemoryPackOrder(13)]
    [Id(11)]
    public long ServerTick { get; set; }

    public SceneObjectSyncPacket() { Kind = SyncPacketKind.SceneObjectSync; }
}

// ---------------------------------------------------------------------------
// 多玩家 AOI 与动态 Chunk 订阅：协议包定义。
// 承担 wire-protocol 形态定义，运行时处理（grain / Gateway 路由 / 客户端 system）由上层模块落地。
// ---------------------------------------------------------------------------

/// <summary>
/// 服务器→客户端：动态 chunk 订阅变更（多玩家 AOI）。
/// 当玩家跨越 chunk 边界导致 AOI 窗口滚动时，服务器下发本包通知客户端
/// 新增订阅（<see cref="AddedChunks"/>）与移除订阅（<see cref="RemovedChunks"/>）的 chunk key 集合。
/// 客户端据此拉起/卸载对应的 chunk 流接收与实体兴趣管理。
/// 与 <see cref="SnapshotPacket"/> 解耦走独立通道，避免 AOI 边界变更与 baseline/delta 帧产生耦合。
/// </summary>
[MemoryPackable(SerializeLayout.Explicit)]
[GenerateSerializer]
public sealed partial class SubscriptionUpdatePacket : SyncPacket
{
    /// <summary>本次新增订阅的 chunk key 集合（客户端应开始接收这些 chunk 的 WorldChunkDiff 流并预加载）。</summary>
    [MemoryPackOrder(2)]
    [Id(0)]
    public ulong[] AddedChunks { get; set; } = Array.Empty<ulong>();

    /// <summary>本次移除订阅的 chunk key 集合（客户端应停止接收并释放这些 chunk 的本地缓存）。</summary>
    [MemoryPackOrder(3)]
    [Id(1)]
    public ulong[] RemovedChunks { get; set; } = Array.Empty<ulong>();

    /// <summary>玩家当前世界坐标 X（Flax Y-up 坐标系）。非零时网关侧改用位置驱动订阅，避免传输大量 chunk key。</summary>
    [MemoryPackOrder(4)]
    [Id(2)]
    public float PositionX { get; set; }

    /// <summary>玩家当前世界坐标 Y（Flax Y-up 坐标系，= 高度）。</summary>
    [MemoryPackOrder(5)]
    [Id(3)]
    public float PositionY { get; set; }

    /// <summary>玩家当前世界坐标 Z（Flax Y-up 坐标系）。</summary>
    [MemoryPackOrder(6)]
    [Id(4)]
    public float PositionZ { get; set; }

    /// <summary>PositionX/Y/Z 是否已被客户端填充（用于判断是否走位置驱动订阅路径）。</summary>
    [MemoryPackOrder(7)]
    [Id(5)]
    public bool HasPosition { get; set; }

    public SubscriptionUpdatePacket() { Kind = SyncPacketKind.SubscriptionUpdate; }
}

/// <summary>
/// P1.3 服务器→客户端：握手拒绝包。
/// 当客户端协议版本低于 <see cref="SyncProtocolVersion.MinimumSupported"/> 时，
/// 服务器返回本包通知客户端触发强制更新流程。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public sealed partial class HandshakeRejectPacket : SyncPacket
{
    /// <summary>服务器当前协议版本。</summary>
    [MemoryPackOrder(2)]
    [Id(0)]
    public int ServerVersion { get; set; }

    /// <summary>服务器支持的最低协议版本（客户端需升级到此版本以上才能连接）。</summary>
    [MemoryPackOrder(3)]
    [Id(1)]
    public int MinimumVersion { get; set; }

    /// <summary>拒绝原因（便于客户端展示/日志）。</summary>
    [MemoryPackOrder(4)]
    [Id(2)]
    public string Reason { get; set; } = string.Empty;

    public HandshakeRejectPacket() { Kind = SyncPacketKind.HandshakeReject; }
}

// ---------------------------------------------------------------------------
// P1.4 战斗协议包：最小可玩 PVE 战斗闭环（攻击/受击/死亡/复活）。
// ---------------------------------------------------------------------------

/// <summary>
/// 战斗动作类型。
/// </summary>
public enum CombatActionKind : byte
{
    /// <summary>普通攻击。</summary>
    NormalAttack = 0,
    /// <summary>技能释放。</summary>
    SkillCast = 1,
    /// <summary>道具使用（药水/暗器）。</summary>
    ItemUse = 2,
    /// <summary>格挡/闪避。</summary>
    Defend = 3,
    /// <summary>
    /// 打断（脚踢那一类）：目标是"正在读一条可打断的条"的怪。
    /// <para>尾部追加：老客户端从不发这个值，服务端只是开始受理一种以前会被判 Invalid 的动作，
    /// 因此不需要升 <see cref="SyncProtocolVersion"/>——与移动/战斗上行同一条纪律。</para>
    /// <para>它不造成伤害：走的是 <c>DsWorldSession</c> 的施法打断通路，不进 <c>CombatCore</c> 的命中/伤害结算。</para>
    /// </summary>
    Interrupt = 4,
    /// <summary>
    /// 嘲讽（坦克把人拉走那一类）：目标是一只怪，效果是**换强制目标**，不造成伤害。
    /// <para>与 <see cref="Interrupt"/> 同一条纪律：尾部追加一个新枚举值，包体一个字段都没动，
    /// 服务端只是开始受理一种以前会被判 Invalid 的动作，所以不升 <see cref="SyncProtocolVersion"/>。</para>
    /// <para>走 <c>DsWorldSession.RequestMonsterTaunt</c>：威胁表抬到最高 + 一段锁定窗口，
    /// 窗口内不因滞回换回去（只抬威胁不锁窗口＝"坦克拉了个寂寞"，别人平 A 一下就盖过去）。</para>
    /// </summary>
    Taunt = 5,
}

/// <summary>
/// 客户端→服务器：从一具尸体上取走某一个槽。
/// <para>只带"哪具尸体、第几个槽"：距离与窗口由 DS 量（它才是唯一知道玩家权威位置的地方），
/// 列表归属在 Orleans（见 <c>ICorpseLootStore</c>）。</para>
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public sealed partial class CorpseLootTakeRequestPacket : SyncPacket
{
    /// <summary>发起者角色/会话 ID（与 InputPacket 同源；DS 用它找会话，不采信包里的其它身份）。</summary>
    [MemoryPackOrder(2)]
    [Id(0)]
    public ulong CharacterId { get; set; }

    /// <summary>尸体实体 ID。</summary>
    [MemoryPackOrder(3)]
    [Id(1)]
    public ulong CorpseEntityId { get; set; }

    /// <summary>槽号（0 基；越界由服务端拒绝并回 InvalidSlot）。</summary>
    [MemoryPackOrder(4)]
    [Id(2)]
    public int SlotIndex { get; set; }
}

/// <summary>
/// 服务器→客户端：拾取结果。
/// <para><see cref="Status"/> 取值与 <c>CorpseLootTakeStatus</c> 一致（1=Taken … 7=UnknownCorpse）；
/// 取走时带槽内容（物品或金币），供客户端弹"获得 X"。</para>
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public sealed partial class CorpseLootTakeResultPacket : SyncPacket
{
    [MemoryPackOrder(2)]
    [Id(0)]
    public ulong CorpseEntityId { get; set; }

    [MemoryPackOrder(3)]
    [Id(1)]
    public int SlotIndex { get; set; }

    [MemoryPackOrder(4)]
    [Id(2)]
    public byte Status { get; set; }

    [MemoryPackOrder(5)]
    [Id(3)]
    public int ItemId { get; set; }

    [MemoryPackOrder(6)]
    [Id(4)]
    public int Count { get; set; }

    [MemoryPackOrder(7)]
    [Id(5)]
    public int Money { get; set; }
}

/// <summary>
/// 客户端→服务器：战斗动作请求（攻击/技能/道具）。
/// 服务端收到后由 CombatSystemGrain 裁决命中/伤害，然后下发 <see cref="DamagePacket"/>。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public sealed partial class CombatActionPacket : SyncPacket
{
    /// <summary>攻击者实体 ID。</summary>
    [MemoryPackOrder(2)]
    [Id(0)]
    public ulong AttackerId { get; set; }

    /// <summary>目标实体 ID（0 表示无目标/自身）。</summary>
    [MemoryPackOrder(3)]
    [Id(1)]
    public ulong TargetId { get; set; }

    /// <summary>动作类型。</summary>
    [MemoryPackOrder(4)]
    [Id(2)]
    public CombatActionKind ActionKind { get; set; }

    /// <summary>技能/道具 ID（0 = 普通攻击）。</summary>
    [MemoryPackOrder(5)]
    [Id(3)]
    public int SkillId { get; set; }

    /// <summary>客户端 tick（用于 reconciliation 与攻速校验）。</summary>
    [MemoryPackOrder(6)]
    [Id(4)]
    public long ClientTick { get; set; }

    /// <summary>攻击者朝向 Yaw（弧度，用于背刺/侧击判定）。</summary>
    [MemoryPackOrder(7)]
    [Id(5)]
    public float AttackerYaw { get; set; }

    /// <summary>
    /// 这次动作附带的状态号（0 = 不带；1=眩晕 2=沉默 3=定身 4=减速，词表在
    /// <c>MonsterStatusCore.MonsterStatusKind</c>）。<b>v16 末尾追加</b>。
    /// <para>为什么挂在**动作**上而不是只挂在怪物技能挂载上：AC 里"这式技能附带什么状态"本来就是
    /// 技能属性（`Spell::DoSpellHitOnUnit → EffectApplyAura`，`Spell.cpp:2925/:3080`），
    /// 而玩家技能今天走的是这条动作包；怪物那一侧则用挂载行（`MonsterSkillMount.StatusId`）。</para>
    /// </summary>
    [MemoryPackOrder(8)]
    [Id(6)]
    public int StatusId { get; set; }

    /// <summary>上面那条状态的持续秒数（<c>&lt;= 0</c> ⇒ 拒收；与 <see cref="StatusId"/> 成对）。v16 末尾追加。</summary>
    [MemoryPackOrder(9)]
    [Id(7)]
    public float StatusDurationSeconds { get; set; }

    /// <summary>
    /// 状态强度（基点，10000 = 100%）。今天只有减速用它：5000 = 走速打五折；<b>0 = 没填</b>（不生效）。
    /// <para>与怪物挂载行上那一格（`MonsterSkillMount.StatusMagnitudeBps`）同一个含义，
    /// 因为玩家与怪物都要能施加同一种状态、并用同一个内核判据。v16 末尾追加。</para>
    /// </summary>
    [MemoryPackOrder(10)]
    [Id(8)]
    public int StatusMagnitudeBps { get; set; }

    public CombatActionPacket() { Kind = SyncPacketKind.CombatAction; }
}

/// <summary>
/// 服务器→客户端：伤害通知。
/// 服务端裁决完成后下发给 AOI 内所有玩家，客户端据此播放伤害数字/受击动画。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public sealed partial class DamagePacket : SyncPacket
{
    /// <summary>攻击者实体 ID。</summary>
    [MemoryPackOrder(2)]
    [Id(0)]
    public ulong AttackerId { get; set; }

    /// <summary>受击者实体 ID。</summary>
    [MemoryPackOrder(3)]
    [Id(1)]
    public ulong TargetId { get; set; }

    /// <summary>伤害值（正数=伤害，负数=治疗）。</summary>
    [MemoryPackOrder(4)]
    [Id(2)]
    public int DamageAmount { get; set; }

    /// <summary>伤害类型（复用 CharacterCombatContract 中 DamageType 的编码语义）。</summary>
    [MemoryPackOrder(5)]
    [Id(3)]
    public byte DamageType { get; set; }

    /// <summary>是否暴击。</summary>
    [MemoryPackOrder(6)]
    [Id(4)]
    public bool IsCritical { get; set; }

    /// <summary>受击者剩余 HP。</summary>
    [MemoryPackOrder(7)]
    [Id(5)]
    public int RemainingHp { get; set; }

    /// <summary>受击者最大 HP。</summary>
    [MemoryPackOrder(8)]
    [Id(6)]
    public int MaxHp { get; set; }

    /// <summary>触发本伤害的技能 ID（0 = 普攻）。</summary>
    [MemoryPackOrder(9)]
    [Id(7)]
    public int SkillId { get; set; }

    /// <summary>服务器 tick（用于客户端插值排序）。</summary>
    [MemoryPackOrder(10)]
    [Id(8)]
    public long ServerTick { get; set; }

    public DamagePacket() { Kind = SyncPacketKind.Damage; }
}

/// <summary>
/// 死亡类型。
/// </summary>
public enum DeathKind : byte
{
    /// <summary>被玩家击杀。</summary>
    KilledByPlayer = 0,
    /// <summary>被怪物/NPC 击杀。</summary>
    KilledByMonster = 1,
    /// <summary>环境伤害死亡（跌落/毒雾）。</summary>
    Environmental = 2,
    /// <summary>自杀/GM 命令。</summary>
    SelfDestruct = 3,
}

/// <summary>
/// 服务器→客户端：死亡通知。
/// 实体 HP 归零时服务端下发，客户端播放死亡动画并进入复活倒计时。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public sealed partial class DeathPacket : SyncPacket
{
    /// <summary>死亡实体 ID。</summary>
    [MemoryPackOrder(2)]
    [Id(0)]
    public ulong EntityId { get; set; }

    /// <summary>击杀者实体 ID（0 = 无击杀者/环境死亡）。</summary>
    [MemoryPackOrder(3)]
    [Id(1)]
    public ulong KillerId { get; set; }

    /// <summary>死亡类型。</summary>
    [MemoryPackOrder(4)]
    [Id(2)]
    public DeathKind DeathType { get; set; }

    /// <summary>复活倒计时（秒）。客户端据此显示复活 UI。</summary>
    [MemoryPackOrder(5)]
    [Id(3)]
    public float RespawnDelaySeconds { get; set; }

    /// <summary>服务器 tick。</summary>
    [MemoryPackOrder(6)]
    [Id(4)]
    public long ServerTick { get; set; }

    public DeathPacket() { Kind = SyncPacketKind.Death; }
}

// ---------------------------------------------------------------------------
// baseline 缺失重传请求：客户端 delta 解码时发现 baseline 不匹配，主动请求服务端下发全量快照。
// 走可靠通道，服务端收到后下一 tick 强制下发全量快照（baseline）给该 session。
// ---------------------------------------------------------------------------

/// <summary>
/// 客户端→服务器：baseline 缺失重传请求。
/// 当客户端收到 delta 帧但本地缺失对应 baseline（或 baseline tick 不匹配）时上送本包，
/// 服务端收到后设置 <c>_forceFullSnapshotNextTick = true</c>，下一 tick 强制下发全量快照。
/// 走可靠通道，保证重传请求必达。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public sealed partial class BaselineResyncRequestPacket : SyncPacket
{
    /// <summary>delta 帧期望的 baseline tick（客户端据此告知服务端需要哪个 baseline）。</summary>
    [MemoryPackOrder(2)]
    [Id(0)]
    public long ExpectedBaselineTick { get; set; }

    /// <summary>客户端最后已应用的 baseline tick（0 表示无任何 baseline，便于服务端诊断）。</summary>
    [MemoryPackOrder(3)]
    [Id(1)]
    public long ClientLastAppliedTick { get; set; }

    public BaselineResyncRequestPacket() { Kind = SyncPacketKind.BaselineResyncRequest; }
}

// ---------------------------------------------------------------------------
// v17：翻越（Traversal）联机权威。
// 客户端用 Flax 射线找到边沿后把整条脚本位移当成"请求"发上来；服务端校验计划本身
// （高度/距离/时长/起点贴合）后按同一份曲线推进权威位置，并在结束时落点。
// 逐帧位置不交换——只交换计划参数，避免 1s 量级的位移占用每 tick 带宽。
// ---------------------------------------------------------------------------

/// <summary>
/// 客户端→服务器：翻越计划请求。坐标一律 ECS（Z-up，米）。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public sealed partial class TraversalRequestPacket : SyncPacket
{
    /// <summary>发起者角色 ID（与 <see cref="InputPacket.CharacterId"/> 同义）。</summary>
    [MemoryPackOrder(2)]
    [Id(0)]
    public ulong CharacterId { get; set; }

    /// <summary>客户端 tick（仅诊断对齐用；权威与否由服务端裁决）。</summary>
    [MemoryPackOrder(3)]
    [Id(1)]
    public long ClientTick { get; set; }

    /// <summary>计划起点 X/Y/Z。</summary>
    [MemoryPackOrder(4)]
    [Id(2)]
    public float StartX { get; set; }

    [MemoryPackOrder(5)]
    [Id(3)]
    public float StartY { get; set; }

    [MemoryPackOrder(6)]
    [Id(4)]
    public float StartZ { get; set; }

    /// <summary>计划终点 X/Y/Z（平台顶面落点）。</summary>
    [MemoryPackOrder(7)]
    [Id(5)]
    public float EndX { get; set; }

    [MemoryPackOrder(8)]
    [Id(6)]
    public float EndY { get; set; }

    [MemoryPackOrder(9)]
    [Id(7)]
    public float EndZ { get; set; }

    /// <summary>总时长（秒）。</summary>
    [MemoryPackOrder(10)]
    [Id(8)]
    public float Duration { get; set; }

    /// <summary>弧顶额外抬升（米）。</summary>
    [MemoryPackOrder(11)]
    [Id(9)]
    public float ArcHeight { get; set; }

    /// <summary>结束朝向（ECS 弧度；服务端只记录不校验，客户端按本地动画播放）。</summary>
    [MemoryPackOrder(12)]
    [Id(10)]
    public float EndYaw { get; set; }

    /// <summary>客户端使用的曲线版本（<c>TraversalMath.Version</c>）；不一致时服务端拒绝，避免两套曲线推进同一计划。</summary>
    [MemoryPackOrder(13)]
    [Id(11)]
    public int CurveVersion { get; set; }

    public TraversalRequestPacket() { Kind = SyncPacketKind.TraversalRequest; }
}

/// <summary>
/// 服务器→客户端：翻越裁决结果。
/// <para>
/// <see cref="Status"/> 的取值与 <c>Horizon.Game.Sim.TraversalRejectReason</c> 对齐（0 = 受理）；
/// 包层不直接引用 Sim.Contracts，避免传输层反向依赖仿真层——该数值契约由
/// <c>TraversalProtocolContractTests</c> 断言。
/// </para>
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public sealed partial class TraversalResultPacket : SyncPacket
{
    /// <summary>请求角色 ID。</summary>
    [MemoryPackOrder(2)]
    [Id(0)]
    public ulong CharacterId { get; set; }

    /// <summary>回显请求的客户端 tick，客户端据此匹配待决请求。</summary>
    [MemoryPackOrder(3)]
    [Id(1)]
    public long ClientTick { get; set; }

    /// <summary>是否受理（true = 服务端会按计划推进权威位置）。</summary>
    [MemoryPackOrder(4)]
    [Id(2)]
    public bool Accepted { get; set; }

    /// <summary>拒绝原因（0 = 受理；其余见 <c>TraversalRejectReason</c>）。</summary>
    [MemoryPackOrder(5)]
    [Id(3)]
    public byte Status { get; set; }

    /// <summary>权威终点回显（受理时客户端可直接对齐；拒绝时为服务端当前权威位置，供客户端回正）。</summary>
    [MemoryPackOrder(6)]
    [Id(4)]
    public float EndX { get; set; }

    [MemoryPackOrder(7)]
    [Id(5)]
    public float EndY { get; set; }

    [MemoryPackOrder(8)]
    [Id(6)]
    public float EndZ { get; set; }

    /// <summary>服务端 tick。</summary>
    [MemoryPackOrder(9)]
    [Id(7)]
    public long ServerTick { get; set; }

    public TraversalResultPacket() { Kind = SyncPacketKind.TraversalResult; }
}
