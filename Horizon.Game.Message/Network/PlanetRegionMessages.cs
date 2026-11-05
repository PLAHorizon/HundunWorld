using System;
using MemoryPack;
using Orleans;
using Horizon.Game.Message.Enums;

namespace Horizon.Game.Message.Network
{
    /// <summary>
    /// 星球区域迁移的线上拒绝码。<b>这是一条独立的码段，不是两个库枚举的直接拷贝</b>：
    /// <see cref="Horizon.Planet.AuthorityReject"/> 与
    /// <see cref="Horizon.Planet.TransferRejectReason"/> 的数值会撞车（两边都有 1/2/3），
    /// 直接透传会让客户端把"几何不共边"读成"实体未知"。映射表在
    /// <c>Horizon.Orleans.Grains.PlanetRegionGrain</c>，判据钉住"每个枚举值都有唯一码"。
    /// </summary>
    public enum PlanetRegionRejectCode
    {
        None = 0,

        // 几何侧（来自 TransferRejectReason）
        MalformedRegion = 1,
        MalformedPoint = 2,
        LevelMismatch = 3,
        PointNotInTarget = 4,
        SameRegion = 5,
        NotAdjacent = 6,
        TooFarFromSurface = 7,

        // 权限侧（来自 AuthorityReject）
        UnknownEntity = 8,
        OwnerMismatch = 9,
        TickRegression = 10,

        /// <summary>
        /// 请求根本进不了判定：包体为 null，或 <see cref="PlanetRegionTransferRequest.CharacterId"/>
        /// 超出 int64（线上是 ulong，权威表与位打包区域号都用有符号 long，越界后
        /// <c>unchecked</c> 会翻成负数——那会变成一个"看起来合法"的实体号，比拒掉危险得多）。
        /// 也用于角色号 0（0 在本项目里表示"没有角色"）。</summary>
        MalformedMessage = 11,

        /// <summary>
        /// 包里的角色号和这条连接绑定的角色不一致（会话绑定校验开着时才会判）。
        /// <para>归在适配器侧而不是几何/权限侧：迁移请求一旦过了绑定检查就能改动别人的归属——
        /// 归属一变，那头的 AOI、地形原点与切平面全跟着动，所以伪造角色号不是"读到别人的数据"，
        /// 是"改写别人的世界状态"。这条码在网关就出，不进 grain。</para>
        /// </summary>
        SessionMismatch = 12,

        /// <summary>
        /// 客户端声明的星球口径与服务端不是同一颗星球（§6.2 那句"terrainSeed/terrainVersion 不匹配
        /// 直接拒绝进入世界"的线上形状）。
        /// <para><b>具体差在哪一项写在 <see cref="PlanetRegionTransferResult.Reason"/> 里</b>：
        /// 半径/层级错是 build 选错了星球，要换包；地形版本/基场哈希错是数据没同步，要重新拉数据——
        /// 处置完全不同，合并成一条码等于让排查靠猜。判定本身在
        /// <c>Horizon.Planet.PlanetHandshakePolicy</c>，这里只是它的线上出口。</para>
        /// </summary>
        CaliberMismatch = 13,

        /// <summary>
        /// 声明的点在<em>地面以下</em>（§6.1 要求服务端也做地面约束）。
        /// <para>与 <see cref="TooFarFromSurface"/> 不是一回事：那条看的是"离海平面多远"（半径口径），
        /// 这条看的是"离脚下那块真实地面多远"。山地上海拔几千米都正常，
        /// 而海平面以下三十米在陆地上就是穿模。</para>
        /// <para>只在服务端真的挂了基场数据时才可能发出来：没挂时这条判定不生效，
        /// <b>不会</b>退化成"海拔 0 当地面"（那样所有站在山上的人都会被拒）。</para>
        /// </summary>
        Underground = 14,
    }

    /// <summary>
    /// 星球区域迁移请求。
    /// <para>坐标一律 int64 厘米：这颗星球半径 6.371e9 cm，客户端在局部切平面里用 float 没问题，
    /// 但跨端要逐位对得上，所以线上是整数厘米（§9 的 P1 协议与 <c>PlanetWire</c> 同一口径）。</para>
    /// <para><see cref="ToRegionId"/> 是客户端的<em>声明</em>，服务端不照它判：先用
    /// <see cref="GlobalXcm"/> 反查真正所在的区域，再要求它与 from 共边。
    /// 声明与反查不一致正是 §13.15 那条"客户端误用服务端共边不变量"的合法对角跨格，
    /// 不能当作弊拒。</para>
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(256, ServiceType.Game)]
    public partial class PlanetRegionTransferRequest : MessageUnion, INetworkMessage
    {
        public PlanetRegionTransferRequest()
        {
            // Type/ServiceType 由 MessageUnion 基类负责序列化，这里只赋值不再声明一遍。
            Type = MessageType.PlanetRegionTransfer;
            ServiceType = ServiceType.Game;
        }

        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>
        /// 客户端认为自己在的区域。<b>必须是 long</b>：位打包把 face 放在 bit58、level 放在 bit54
        /// （<see cref="Horizon.Planet.RegionKey.ToId"/>），int 装不下——面号会被截成 0，
        /// 于是六个面的请求在服务端看起来都像是 face 0。
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long FromRegionId { get; set; }

        /// <summary>客户端声明要去的区域。服务端只把它当声明用。同样是位打包 long。</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long ToRegionId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public long GlobalXcm { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public long GlobalYcm { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public long GlobalZcm { get; set; }

        /// <summary>客户端 tick。服务端只接受单调不减的 tick，回退一律拒（防止旧帧重放把归属拉回去）。</summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public long Tick { get; set; }

        /// <summary>
        /// 客户端声明的星球口径，<b>就是 §6.2 那条 <c>PlanetHandshake</c> 的 35 字节线上形态</b>
        /// （由 <c>PlanetHandshake.TryWrite</c> 产出，服务端用 <c>TryRead</c> 解）。
        /// <para>为什么挂在这条请求上而不是只做一条"进世界时握手"：这是当前唯一会改动权威归属、
        /// 并把<em>新切平面姿态</em>交给客户端的上行。服务端按自己的基场算帧、客户端按自己的基场写局部
        /// 坐标时不会报错，只会让人走几步之后突然歪掉（§13.15 的形状）。一次性握手挡不住
        /// "运行中磁盘上的 DEM 被换掉"，而那条恰恰最难查。</para>
        /// <para>为什么是一段字节而不是七个标量字段：那样两端各要一份手写搬运，错位之后判定照样跑、
        /// 只是每次比错一项。用库里已经逐字节验过的编解码，映射只有一份，读失败还能走
        /// <c>PlanetHandshakePolicy.EvaluateRead</c> 那一支（截断包/多余字节/协议号不对各归各的码）。</para>
        /// <para>空数组表示"客户端没声明"：它与任何服务端口径都解不到一起（<c>TryRead</c> 会报
        /// Truncated），因此被拒。这里刻意不留"没声明就放行"的旁路——那会让旧客户端静默绕过闸门；
        /// 要关这条判定，关掉的是服务端口径来源，不是客户端的声明义务。</para>
        /// </summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public byte[] PlanetCaliber { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// 星球区域迁移回执。
    /// <para>两条硬规矩：① 拒绝必须带 <see cref="Reason"/> 非空——"拒了但没说为什么"在客户端
    /// 与丢包不可分辨；② <see cref="Applied"/> 与 <see cref="Accepted"/> 分开，
    /// 因为"合法但没改变归属"（同一区域、重复请求）是成功的空操作，不是失败。</para>
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(257, ServiceType.Game)]
    public partial class PlanetRegionTransferResult : MessageUnion, INetworkMessage
    {
        public PlanetRegionTransferResult()
        {
            Type = MessageType.PlanetRegionTransferResult;
            ServiceType = ServiceType.Game;
        }

        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>几何与权限都过了才算接受。</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public bool Accepted { get; set; }

        /// <summary>接受之后归属是否真的变了。同区域重复请求是 accepted 但 not applied。</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public bool Applied { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public PlanetRegionRejectCode Code { get; set; }

        /// <summary>人类可读的原因，带齐反查结果。拒绝时不许为空。</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string Reason { get; set; } = "";

        /// <summary>
        /// 服务端裁定的权威区域（位打包 Id）。接受的迁移=新区；被拒=当前仍有效的旧归属；
        /// 实体在服务端不存在（含 <see cref="PlanetRegionRejectCode.MalformedMessage"/>）时为 0，
        /// 表示"服务端没有归属可报"，客户端要重新落位而不是照这个值切区。
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long NewRegionId { get; set; }

        /// <summary>交接 tick：客户端要把早于此帧的状态丢弃。</summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public long HandoverTick { get; set; }

        /// <summary>服务端时钟，客户端用它对时（服务端只在绑定/传送/接受的迁移上前进）。</summary>
        [MemoryPackOrder(7)]
        [Id(7)]
        public long ServerTick { get; set; }

        // ── 以下就是 §6.2 里那条 `RegionTransferAck` 的字段。只有 Applied 为 true 时才有意义 ──
        //
        // 为什么把 ack 并进取而不是再发一条消息：交接的判定与交接的目标坐标系必须**同生同死**。
        // 分成两条就是"客户端收到 accepted 但 ack 丢了"这种半状态，而它没法恢复——
        // 客户端照旧用旧切平面写局部坐标，服务端已经按新区记账了。
        // 字段形状（局部 i32 厘米 + 原点 i64 厘米 + Q15 四元数）刻意与
        // <c>Horizon.Planet.Wire.RegionTransferAck</c> 逐一对应，判据里也按逐字段对账。

        /// <summary>新区局部坐标（厘米，i32）。取整规则与每帧状态包同一条：四舍五入远离零。</summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public int LocalXCm { get; set; }

        [MemoryPackOrder(9)]
        [Id(9)]
        public int LocalYCm { get; set; }

        [MemoryPackOrder(10)]
        [Id(10)]
        public int LocalZCm { get; set; }

        /// <summary>新区原点（ECEF 厘米）。客户端拿它换 <c>WorldOrigin</c>，也拿它校验服务端是不是同一颗星球。</summary>
        [MemoryPackOrder(11)]
        [Id(11)]
        public long OriginXcm { get; set; }

        [MemoryPackOrder(12)]
        [Id(12)]
        public long OriginYcm { get; set; }

        [MemoryPackOrder(13)]
        [Id(13)]
        public long OriginZcm { get; set; }

        /// <summary>
        /// 新区切平面姿态（Q15 压缩四元数，列 j 是局部第 j 轴在全局里的指向）。
        /// <para>没有这四个数，客户端就只能"自己猜"新帧的朝向；而 §13.15 那次实机事故正是猜错方向
        /// 导致换区后局部坐标凭空多出一整个区域宽。</para>
        /// </summary>
        [MemoryPackOrder(14)]
        [Id(14)]
        public short FrameX { get; set; }

        [MemoryPackOrder(15)]
        [Id(15)]
        public short FrameY { get; set; }

        [MemoryPackOrder(16)]
        [Id(16)]
        public short FrameZ { get; set; }

        [MemoryPackOrder(17)]
        [Id(17)]
        public short FrameW { get; set; }
    }

    /// <summary>
    /// 一个实体的区域层快照。<b>它不占 union tag</b>：只作为 grain 的返回值在服务端内部走
    /// Orleans 序列化。哪天要直接推给客户端，才需要给它登记 tag——那时别忘了注册表已经满了这件事。
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class PlanetRegionSnapshot
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>当前归属区域（位打包 Id）；实体没落过位时为 0。</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public long OwnerRegionId { get; set; }

        /// <summary>该订阅的区域集合：本区 + 四个共边邻居（跨面也算），外加还没过期的重叠旧区。</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long[] SubscribedRegionIds { get; set; } = Array.Empty<long>();

        /// <summary>区域层可见的实体（同区或共边）。AOI 的"看得见"到这一层为止。</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public ulong[] VisibleCharacterIds { get; set; } = Array.Empty<ulong>();

        /// <summary>服务端时钟，客户端用来对时。</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long ServerTick { get; set; }

        /// <summary>
        /// 邻区里要加载的那几列 100 m cell（§6.4 的第二层）。<b>本区不在里面</b>：
        /// 本区整块订阅，列出来只会把账撑大（一列 100 m，L14 一块区域 61 列见方）。
        /// <para>区域号是 <see cref="PlanetCellKey.RegionId"/>（位打包 long），cell 下标是带符号 int，
        /// 用的是<em>该区域自己</em>的局部帧——不是观察者的帧，跨面那对尤其不能混。</para>
        /// </summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public PlanetCellKey[] SubscribedBoundaryCells { get; set; } = Array.Empty<PlanetCellKey>();

        /// <summary>
        /// 这份快照是不是服务端受理的结果。<b>空数组有两种完全不同的意思</b>：
        /// "周围真的没人"与"你这条请求压根不该被处理（口径不符/角色号非法）"。
        /// 少了这一栏，客户端只能靠超时和猜来分辨，而猜错的代价是继续按一份旧订阅集加载。
        /// </summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public bool Accepted { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public PlanetRegionRejectCode Code { get; set; }

        /// <summary>被拒时的理由；接受时是一行订阅集摘要。<b>不许为空</b>。</summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public string Reason { get; set; } = "";
    }

    /// <summary>
    /// 一块区域里的一个 100 m cell。
    /// <para><b>为什么不能压成一个 long</b>：区域号本身已经占到位打包的 bit58（face 在最高位），
    /// 而 L14 的 cell 下标范围约 ±30，粗层级（L4）能到 ±31 000，L0 更是超出 16 bit——
    /// 三个字段硬塞进 64 位就得给下标设上限，那种上限会变成"边界外的 cell 静默消失"。
    /// 所以这里明写三个字段，多花几字节换"没有隐藏上限"。</para>
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    public partial class PlanetCellKey
    {
        /// <summary>所在区域（位打包 Id，必须 long：见 <see cref="PlanetRegionTransferRequest.FromRegionId"/>）。</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public long RegionId { get; set; }

        /// <summary>该区域局部帧里 X 向的 cell 下标（带符号，<c>floor(localX / 10000)</c>）。</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public int Cx { get; set; }

        /// <summary>该区域局部帧里 Z 向的 cell 下标（带符号）。</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int Cz { get; set; }
    }

    /// <summary>
    /// 请求一个实体的区域层快照（§6.4 的 AOI 入口，<b>只读</b>）。
    /// <para>并轨策略（§13.55）：客户端拿这份快照只为<em>决定加载哪些邻区与边界 cell</em>，
    /// 服务端权威位置仍归平面世界的 <c>ZoneShardGrain</c>。所以这里没有"上报位置"的字段——
    /// 那属于另一个决定（把每帧上行改到星球 grain），别混进来。</para>
    /// <para><see cref="PlanetCaliber"/> 也带上：读一条不属于这颗星球的订阅集，
    /// 得到的区域号会被按另一套层级解释，客户端会去加载<em>另一块地面</em>的实体而不出错。</para>
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(258, ServiceType.Game)]
    public partial class PlanetRegionSnapshotRequest : MessageUnion, INetworkMessage
    {
        public PlanetRegionSnapshotRequest()
        {
            Type = MessageType.PlanetRegionSnapshot;
            ServiceType = ServiceType.Game;
        }

        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        /// <summary>与迁移请求同一条 35 字节握手（<see cref="PlanetRegionTransferRequest.PlanetCaliber"/>）。</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public byte[] PlanetCaliber { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// 区域层快照的回执。<b>字段是 <see cref="PlanetRegionSnapshot"/> 的展开镜像</b>，
    /// 理由与 ack 并进迁移回执时相同：跨线时嵌套自定义类型会让两套序列化各解各的，
    /// 展开成数组 + 标量之后判据能逐字段对账。
    /// <para>拒绝也照发（<see cref="Accepted"/>=false 且 <see cref="Reason"/> 非空）：
    /// 空数组与"被拒"在客户端不可分辨的话，它只能靠超时猜，而猜错的代价是继续按旧订阅集加载。</para>
    /// </summary>
    [MemoryPackable]
    [GenerateSerializer]
    [NetworkMessage(259, ServiceType.Game)]
    public partial class PlanetRegionSnapshotResponse : MessageUnion, INetworkMessage
    {
        public PlanetRegionSnapshotResponse()
        {
            Type = MessageType.PlanetRegionSnapshotResult;
            ServiceType = ServiceType.Game;
        }

        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public bool Accepted { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public PlanetRegionRejectCode Code { get; set; }

        /// <summary>被拒时非空。接受时写一句"订阅集是什么形状"的摘要，方便对着日志查。</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public string Reason { get; set; } = "";

        /// <summary>当前归属区域（位打包 long）；实体没落过位时为 0。</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public long OwnerRegionId { get; set; }

        /// <summary>本区 + 共边邻区（跨面也算）+ 还没过期的重叠旧区。</summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public long[] SubscribedRegionIds { get; set; } = Array.Empty<long>();

        /// <summary>区域层看得见的实体。</summary>
        [MemoryPackOrder(6)]
        [Id(6)]
        public ulong[] VisibleCharacterIds { get; set; } = Array.Empty<ulong>();

        [MemoryPackOrder(7)]
        [Id(7)]
        public long ServerTick { get; set; }

        /// <summary>邻区里要加载的那几列 100 m cell（本区不在里面，见 <see cref="PlanetRegionSnapshot"/>）。</summary>
        [MemoryPackOrder(8)]
        [Id(8)]
        public PlanetCellKey[] SubscribedBoundaryCells { get; set; } = Array.Empty<PlanetCellKey>();

        /// <summary>把库里的快照摊平成线上形状。<b>唯一的摊平处</b>：再写一份就会两边字段顺序不同。</summary>
        public static PlanetRegionSnapshotResponse FromSnapshot(PlanetRegionSnapshot snapshot) => new()
        {
            CharacterId = snapshot.CharacterId,
            // 判定与理由都由 grain 决定，这里只搬运：Handler 里再判一次就会得到第三种说法
            Accepted = snapshot.Accepted,
            Code = snapshot.Code,
            Reason = snapshot.Reason,
            OwnerRegionId = snapshot.OwnerRegionId,
            SubscribedRegionIds = snapshot.SubscribedRegionIds,
            VisibleCharacterIds = snapshot.VisibleCharacterIds,
            ServerTick = snapshot.ServerTick,
            SubscribedBoundaryCells = snapshot.SubscribedBoundaryCells,
        };
    }
}
