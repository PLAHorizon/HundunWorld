using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Horizon.Planet;
using Horizon.Planet.Authority;
using Horizon.Planet.Wire;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace Horizon.Orleans.Grains;

/// <summary>
/// 一条实体记录的落盘形态。
/// <para>只存"重建权威表所需的最少字段"：全局整数厘米坐标 + 归属区域号 + 该实体最后一次被受理的 tick。
/// 归属区域号是<em>冗余</em>的（它能从坐标反查出来），留着它正是为了在恢复时做一致性检查：
/// 两者对不上就说明这份存档不是当前口径写出来的，宁可不恢复。</para>
/// <para>不存重叠窗口：合法迁移的目标必然与旧区共边（§13.13），旧区本来就一直在订阅集里，
/// 存下来只会多一份和 tick 赛跑的脏状态。</para>
/// </summary>
[GenerateSerializer]
public sealed class PlanetRegionEntityRecord
{
    [Id(0)] public long RegionId { get; set; }
    [Id(1)] public long Xcm { get; set; }
    [Id(2)] public long Ycm { get; set; }
    [Id(3)] public long Zcm { get; set; }
    [Id(4)] public long Tick { get; set; }
}

/// <summary>
/// 一颗星球的区域权威投影。
/// <para><see cref="PlanetRadiusCm"/> 与 <see cref="RegionLevel"/> 一起写出去，是为了让"口径漂移"可判：
/// 半径或层级改了，旧存档里的区域号和新口径不可比，硬恢复会把人放进错区域——
/// 那种错不报错，只表现为"上线后位置被拉偏"，比拒绝恢复难查一个数量级。</para>
/// <para><see cref="RegionLevel"/> 默认 -1 表示"还没写过"（新存档），恢复侧据此跳过漂移判定，
/// 免得第一次激活就因为空字段被判成口径不符。</para>
/// </summary>
[GenerateSerializer]
public sealed class PlanetRegionPersistedState
{
    [Id(0)] public Dictionary<long, PlanetRegionEntityRecord> Entities { get; set; } = new();
    [Id(1)] public long Clock { get; set; }
    [Id(2)] public long PlanetRadiusCm { get; set; }
    [Id(3)] public int RegionLevel { get; set; } = -1;
}

/// <summary>
/// <see cref="IPlanetRegionGrain"/> 的实现：把 <see cref="PlanetRegionAuthority"/> 接上 Orleans 的
/// 消息注册与持久化，判定逻辑一条都不在这里重写。
/// <para>三条刻意的设计，都是为了能写出判据：</para>
/// <list type="number">
/// <item><description><b>映射表是纯函数</b>（<see cref="ToCode"/>）：两套库枚举的数值会撞车，
/// 所以逐条映射、由闸门钉住"全覆盖 + 不撞码"。</description></item>
/// <item><description><b>恢复按 tick 升序回放</b>：落位会拒绝比时钟旧的 tick，而字典枚举顺序不保证。
/// 回放完再把时钟抬到存档值，所以每条记录都能进表。</description></item>
/// <item><description><b>归属变更立刻落盘，时钟推进攒着</b>：前者是玩家位置（丢了会被拉回旧区），
/// 后者只是重叠窗口的摘除时序（丢了最多少摘一个窗口，客户端下一帧就自愈）。</description></item>
/// </list>
/// </summary>
public class PlanetRegionGrain : Grain, IPlanetRegionGrain
{
    /// <summary>持久化 state 名；同一 store 下按 grain 主键（星球 id）分行。</summary>
    public const string StateName = "planet_region";

    /// <summary>攒这么多次时钟推进才落一次盘。归属变更不受此限，当场写。</summary>
    internal const int ClockAdvancesPerFlush = 256;

    private readonly ILogger<PlanetRegionGrain> _logger;
    private readonly IPersistentState<PlanetRegionPersistedState> _persisted;

    /// <summary>
    /// 服务端的星球口径来源。<b>可以为 null</b>：没挂基场数据的部署（本地跑服务端、灰度还没接星球）
    /// 也走到这段判定，把它当成"全员口径不符"会让整颗星球起不来。null 或取不出来 → 不判，
    /// 与 <c>PlanetRegionHandler</c> 里会话绑定校验缺席时同口径。
    /// </summary>
    private readonly IPlanetCaliberSource? _caliberSource;

    /// <summary>
    /// 服务端这一侧的地面来源，可以为 null（没挂基场数据的部署）。
    /// <para>null 时地面约束<em>整条不判</em>，而不是拿 0 当地面：这颗星球洋底是负的、山地是正的，
    /// 用 0 会让所有站在山上的人被判成穿模，而所有在水里的人被判成正常。</para>
    /// </summary>
    private readonly IPlanetGroundSource? _groundSource;

    private PlanetRegionAuthority? _authority;
    private int _advancesSinceSave;

    public PlanetRegionGrain(
        ILogger<PlanetRegionGrain> logger,
        [PersistentState(StateName, OrleansConst.GameStore)] IPersistentState<PlanetRegionPersistedState> persisted,
        IPlanetCaliberSource? caliberSource = null,
        IPlanetGroundSource? groundSource = null)
    {
        _logger = logger;
        _persisted = persisted;
        _caliberSource = caliberSource;
        _groundSource = groundSource;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        int restored = RestoreFromState();
        string caliber = CaliberGateStatus, ground = GroundGateStatus;
        // 闸门关着时必须过 Warning：Silo 默认 LogLevel=Warning，Info 那条会被过滤掉，
        // 于是"闸门没开"在默认部署里等于没有记录——而它正是 §13.59 要防的那种静默失效。
        // 开着时用 Info（并在 appsettings 里给本 grain 单独开 Information，别为此抬高全局级别）。
        const string template = "星球区域 grain 主键={Planet} 恢复={Restored} {Describe} 口径闸门={Caliber} 地面闸门={Ground}";
        bool bothOn = caliber.StartsWith("on(", StringComparison.Ordinal)
                      && ground.StartsWith("on(", StringComparison.Ordinal);
        if (bothOn)
            _logger.LogInformation(template, this.GetPrimaryKeyLong(), restored, EnsureAuthority().Describe(), caliber, ground);
        else
            _logger.LogWarning(template, this.GetPrimaryKeyLong(), restored, EnsureAuthority().Describe(), caliber, ground);
        await base.OnActivateAsync(cancellationToken);
    }

    /// <summary>
    /// 口径闸门现在是什么状态，<b>一行说清并且进激活日志</b>。
    /// <para>为什么要有这个东西：这个构造参数是可选的，漏注册不报错、不失败，只是那条判定从此不生效——
    /// 而"判定存在但没人调用"正是 §6.2 这件事此前的状态（库里有 DescribeMismatch，服务端一侧一直是空的）。
    /// 让"关着"成为一个能读出来的状态，比让它成为一个看不见的差别要诚实。</para>
    /// </summary>
    internal string CaliberGateStatus
    {
        get
        {
            if (_caliberSource is null) return "off(未注册口径来源)";
            if (_caliberSource is DemPlanetCaliberSource dem && dem.RootDirectory is null)
                return "off(未配置基场目录 Planet:DemRoot)";
            return _caliberSource.TryGetCaliber(out var caliber)
                ? string.Format(CultureInfo.InvariantCulture, "on(terrainVersion={0:x8} basefield={1})",
                    caliber.TerrainVersion, caliber.BasefieldVersion)
                : string.Format(CultureInfo.InvariantCulture, "off(口径取不出来：{0})",
                    (_caliberSource as DemPlanetCaliberSource)?.Failure ?? "来源未给出原因");
        }
    }

    /// <summary>
    /// 地面闸门的状态，与 <see cref="CaliberGateStatus"/> 同一条理由：可选注入漏注册不报错，
    /// 只让判定静默不生效，所以把"关着"写成一个读得出来的状态。
    /// <para>顺带做一件只有两边同框才能做的事：把地面数据的 terrainVersion 与口径数据的比一次。
    /// 两个来源配成不同目录时，两条判定各自都自洽、互相看不出毛病——线上唯一的症状是玩家被判成穿模。</para>
    /// </summary>
    internal string GroundGateStatus
    {
        get
        {
            if (_groundSource is null) return "off(未注册地面来源)";
            if (_groundSource is DemPlanetGroundSource dem && dem.RootDirectory is null)
                return "off(未配置基场目录 Planet:DemRoot)";
            uint? groundVersion = _groundSource.TerrainVersion;
            if (!groundVersion.HasValue)
                return string.Format(CultureInfo.InvariantCulture, "off(地面数据取不出来：{0})", _groundSource.Describe());
            if (_caliberSource is not null && _caliberSource.TryGetCaliber(out var caliber)
                && caliber.TerrainVersion != groundVersion.Value)
                return string.Format(CultureInfo.InvariantCulture,
                    "on(terrainVersion={0:x8}) 但与口径不符！口径={1:x8} → 两个来源挂的不是同一份数据",
                    groundVersion.Value, caliber.TerrainVersion);
            return string.Format(CultureInfo.InvariantCulture, "on(terrainVersion={0:x8})", groundVersion.Value);
        }
    }

    /// <summary>
    /// 把存档回放进权威表。<b>单独成一个方法是为了能被离线判据直接调用</b>：
    /// <see cref="OnActivateAsync"/> 里那句日志要 grain 身份（<c>GetPrimaryKeyLong</c> 需要运行时上下文），
    /// 判据在 silo 外构造的 grain 上取不到它，于是"恢复"和"记日志"必须切开——
    /// 否则恢复这段就只能靠起集群才能验，代价是每条判据都慢一个数量级。
    /// </summary>
    internal int RestoreFromState() => Restore(_persisted.State, EnsureAuthority());

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        // 攒着的时钟推进在这里补写；归属变更早就写过了，_advancesSinceSave 为 0 时不产生多余 IO
        if (_advancesSinceSave > 0) await PersistAsync();
        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    private PlanetRegionAuthority EnsureAuthority() =>
        _authority ??= new PlanetRegionAuthority(PlanetConstants.PlanetRadiusCm, PlanetConstants.DefaultRegionLevel);

    /// <summary>
    /// 判据用的只读视角。<b>适配器不许有自己的判定状态</b>：账（applied / rejected / clock）
    /// 只有库里这一份，判据才有地方去对。运行期不对外暴露，避免变成第二条调用路径。
    /// </summary>
    internal PlanetRegionAuthority AuthorityForChecks => EnsureAuthority();

    /// <summary>
    /// 从存档重建权威表。
    /// <para>升序回放的理由见类型注释。返回没恢复成功的条数——不抛异常：存档里一条坏记录
    /// 不该让整颗星球起不来，那条记录对应的客户端会自己重新落位。</para>
    /// </summary>
    internal static int Restore(PlanetRegionPersistedState state, PlanetRegionAuthority authority)
    {
        if (state.Entities.Count == 0)
        {
            // 空表也要把时钟接上：否则重启后"重启前那一批还没落地的包"会被当成新包受理
            authority.AdvanceTo(Math.Max(0, state.Clock));
            return 0;
        }
        if (state.RegionLevel != authority.Level || state.PlanetRadiusCm != authority.RadiusCm)
            return state.Entities.Count;          // 口径不符：整份存档按不可用处理

        int skipped = 0;
        var ordered = new List<KeyValuePair<long, PlanetRegionEntityRecord>>(state.Entities);
        ordered.Sort(static (a, b) => a.Value.Tick.CompareTo(b.Value.Tick));
        foreach (var pair in ordered)
        {
            var record = pair.Value;
            if (pair.Key <= 0) { skipped++; continue; }        // 存档里不该有非正实体号（0 表示没有角色）
            var point = new PlanetVector3(record.Xcm, record.Ycm, record.Zcm);
            // 退化点先挡掉：FromDirection 对零方向会抛，而"存档里一行坐标坏了"是数据问题，
            // 不该冒泡成整颗星球起不来的程序异常（库里 TryBind 现在也同口径，两处都拦才谈得上不靠运气）
            double length = point.Length;
            if (!double.IsFinite(length) || length <= 1e-300) { skipped++; continue; }
            // 先验冗余字段再写表：反查出来的区域和存下来的不一致，说明这份记录不可信，
            // 这时 TryBind 已经插进去了，而权威表没有删除接口——顺序反过来就得给库加"撤销"这种没人要的 API
            if (RegionKey.FromDirection(point, authority.Level).ToId() != record.RegionId) { skipped++; continue; }
            if (!authority.TryBind(pair.Key, point, record.Tick, out _)) { skipped++; continue; }
        }
        // 存档时钟可能比任何一条记录都新（重启前只推进过时钟）；取 max 免得 AdvanceTo 因倒退抛
        authority.AdvanceTo(Math.Max(state.Clock, authority.Clock));
        return skipped;
    }

    /// <summary>
    /// §6.2 的口径闸门，三个会改动权威状态的入口共用这一份实现。
    /// <para>返回 null 表示放行——其中包含"服务端没有声明口径"那种<em>不判</em>的情况
    /// （见 <see cref="_caliberSource"/>）。返回的是给人看的理由。</para>
    /// <para>判定本体在 <see cref="PlanetHandshakePolicy"/>；这里只负责解字节、记一条日志、把理由递回去。
    /// 三个入口各写一遍比较逻辑，就是"同一个握手两套真相"的开工方式。</para>
    /// </summary>
    private string? CaliberRejection(byte[]? caliberBytes, object? context)
    {
        if (_caliberSource is not { } caliberSource || !caliberSource.TryGetCaliber(out var serverCaliber))
            return null;

        bool readOk = PlanetHandshake.TryRead(caliberBytes ?? Array.Empty<byte>(),
                                              out var declared, out var wireStatus, out _);
        var verdict = PlanetHandshakePolicy.EvaluateRead(wireStatus, readOk, in serverCaliber, in declared);
        if (verdict.Accepted) return null;

        _logger.LogWarning(
            "星球口径不符，拒绝进入世界：差在={Field} 理由={Detail} 服务端={Server} 上下文={Context}",
            verdict.Field, verdict.Detail, serverCaliber, context);
        return verdict.Detail;
    }

    /// <summary>
    /// 超出这个容差才判穿模。<b>数在 <see cref="PlanetGround"/>，这里只是别名</b>：客户端抬位用的
    /// 必须是同一个门槛，否则"客户端不抬、服务端拒"那段区间就成了一台每秒敲一次的循环机。
    /// </summary>
    internal const double BelowGroundToleranceCm = PlanetGround.BelowToleranceCm;

    /// <summary>
    /// 地面约束。<b>没挂基场就返回 null（不判）</b>，理由见 <see cref="_groundSource"/>。
    /// <para>三个会写权威表的入口（落位/传送/迁移）都在判定之前调它。快照不判：那条读路径拿的是
    /// 已经在表里的位置，而表里的位置只可能从这三个入口进来——在出口再拒一次，客户端就没有任何
    /// 办法把自己救出来了（它收到的每一条回执都会是同一个拒）。</para>
    /// <para>取不出地面高程时也只<em>不判</em>：误拒会把人挡在世界之外，
    /// 而漏判最多是一次穿模，之后会被几何那一支与地面那支的下一回检查接住。</para>
    /// </summary>
    private string? GroundRejection(in PlanetVector3 globalCm)
    {
        if (_groundSource is not { } ground) return null;
        if (!ground.TryGetGroundElevationCm(in globalCm, out double groundElevationCm))
        {
            _logger.LogInformation("地面高程取不出来，跳过地面约束。点={Point} 来源={Source}",
                globalCm, ground.Describe());
            return null;
        }
        double pointElevationCm = globalCm.Length - PlanetConstants.PlanetRadiusCm;
        if (PlanetGround.BelowGroundCm(groundElevationCm, pointElevationCm) <= BelowGroundToleranceCm) return null;
        return PlanetGround.Describe(groundElevationCm, pointElevationCm);
    }

    /// <inheritdoc />
    public async Task<bool> BindAsync(ulong characterId, long xCm, long yCm, long zCm, long tick, byte[] planetCaliber)
    {
        var authority = EnsureAuthority();
        // 口径排在最前：落位就是"进入世界"那一步，不匹配就不该在权威表里留下任何东西
        if (CaliberRejection(planetCaliber, $"bind 角色号={characterId}") is not null) return false;
        if (!TryEntityId(characterId, out long entityId, out string why))
        {
            _logger.LogWarning("区域落位被拒（不进判定）：{Why} 角色号={CharacterId}", why, characterId);
            return false;
        }
        // §6.1 的地面约束：三个会写权威表的入口（落位/传送/迁移）都要过，少一个就是留一条"绕过地面"的后门
        if (GroundRejection(new PlanetVector3(xCm, yCm, zCm)) is { } groundWhy)
        {
            _logger.LogWarning("区域落位被拒（地面约束）：{Why} 角色号={CharacterId}", groundWhy, characterId);
            return false;
        }
        if (!authority.TryBind(entityId, new PlanetVector3(xCm, yCm, zCm), tick, out var region))
        {
            _logger.LogWarning("区域落位失败：角色号={CharacterId} tick={Tick} 时钟={Clock}",
                characterId, tick, authority.Clock);
            return false;
        }
        await PersistAsync();
        _logger.LogDebug("区域落位：角色号={CharacterId} 区域={Region} tick={Tick}", characterId, region, tick);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> TeleportAsync(ulong characterId, long xCm, long yCm, long zCm, long tick, byte[] planetCaliber)
    {
        var authority = EnsureAuthority();
        if (CaliberRejection(planetCaliber, $"teleport 角色号={characterId}") is not null) return false;
        if (!TryEntityId(characterId, out long entityId, out string why))
        {
            _logger.LogWarning("区域传送被拒（不进判定）：{Why} 角色号={CharacterId}", why, characterId);
            return false;
        }
        if (GroundRejection(new PlanetVector3(xCm, yCm, zCm)) is { } teleportGroundWhy)
        {
            _logger.LogWarning("区域传送被拒（地面约束）：{Why} 角色号={CharacterId}", teleportGroundWhy, characterId);
            return false;
        }
        if (!authority.TryTeleport(entityId, new PlanetVector3(xCm, yCm, zCm), tick, out var region))
        {
            _logger.LogWarning("区域传送失败：角色号={CharacterId} 未知实体或 tick 倒退（实体 tick 由服务端持有）",
                characterId);
            return false;
        }
        await PersistAsync();
        _logger.LogInformation("区域传送：角色号={CharacterId} 新区={Region}", characterId, region);
        return true;
    }

    /// <inheritdoc />
    public async Task<PlanetRegionTransferResult> TransferAsync(PlanetRegionTransferRequest request)
    {
        var authority = EnsureAuthority();
        if (request is null)
            return Reject(0, PlanetRegionRejectCode.MalformedMessage, "迁移请求包体为空");

        // §6.2 那句"terrainSeed/terrainVersion 不匹配直接拒绝进入世界"的线上落点。
        // 排在几何与权限之前：不是同一个世界，两边的区域号和归属账没有共同分母，判了也是白判。
        // 服务端没声明口径时整段跳过（见 _caliberSource 的注释）。
        if (CaliberRejection(request.PlanetCaliber,
                             $"transfer 角色号={request.CharacterId} from={request.FromRegionId}") is { } caliberWhy)
            return Reject(request.CharacterId, PlanetRegionRejectCode.CaliberMismatch, caliberWhy);

        // §6.1 的地面约束排在几何之前：穿模的点反查出来的区域号是"地表下面那一格"，
        // 让它先过几何判定会得出一个"合法但没有意义"的交接。
        if (GroundRejection(new PlanetVector3(request.GlobalXcm, request.GlobalYcm, request.GlobalZcm)) is { } groundWhy)
            return Reject(request.CharacterId, PlanetRegionRejectCode.Underground, groundWhy);

        if (!TryEntityId(request.CharacterId, out long entityId, out string why))
            return Reject(request.CharacterId, PlanetRegionRejectCode.MalformedMessage, why);

        var wire = new RegionTransferRequest
        {
            FromRegionId = request.FromRegionId,
            ToRegionId = request.ToRegionId,
            GlobalCm = new EcefCentimeters(request.GlobalXcm, request.GlobalYcm, request.GlobalZcm),
            Tick = request.Tick,
        };
        var result = authority.RequestTransfer(entityId, in wire, request.Tick);
        bool accepted = result.Decision.Accepted && result.Reject == AuthorityReject.None;
        if (result.Applied) await PersistAsync();
        else _logger.LogInformation("区域迁移被拒：{Result} 角色号={CharacterId}", result, request.CharacterId);

        var code = accepted ? PlanetRegionRejectCode.None : ToCode(result.Decision.Reason, result.Reject);
        var receipt = new PlanetRegionTransferResult
        {
            CharacterId = request.CharacterId,
            Accepted = accepted,
            Applied = result.Applied,
            Code = code,
            Reason = accepted
                ? string.Format(CultureInfo.InvariantCulture, "已受理：{0}", result)
                : string.Format(CultureInfo.InvariantCulture, "{0} / {1}", Explain(code), result),
            NewRegionId = authority.OwnerOf(entityId) is { } owner ? owner.ToId() : 0,
            HandoverTick = result.HandoverTick,
            ServerTick = authority.Clock,
        };
        if (result.Applied && authority.GlobalOf(entityId) is { } globalNow)
            FillAck(receipt, result.Frame, globalNow);              // §6.3 第 3 步要的那套新坐标系，跟着判定一起回
        return receipt;
    }

    /// <summary>
    /// 造 §6.2 那条 <c>RegionTransferAck</c>：新区局部厘米、新区原点、新区切平面姿态。
    /// <para>这三样都必须由<em>服务端这一侧</em>算：客户端自己反查出来的帧和这里对不上时，
    /// 差的是整颗星球的朝向，而它不会报错，只会让人走几步之后突然歪掉（§13.15 的实机形状）。</para>
    /// <para>先造库里那个结构体、再逐字段抄进线上 DTO——不是多做一遍：此前 DTO 就是唯一一份定义，
    /// 而库里那个同名形状的 <see cref="RegionTransferAck"/> 谁都不建，等于"协议里有一条 ack、
    /// 实现里另有一条 ack"。现在字段与取整规则只有一处说一次，DTO 是它的镜像。</para>
    /// </summary>
    internal static RegionTransferAck BuildAck(in RegionFrame frame, in PlanetVector3 globalCm)
    {
        var local = frame.LocalFromGlobalCm(globalCm);
        return new RegionTransferAck
        {
            RegionId = frame.Key.ToId(),
            LocalXCm = QuantizeAckLocal(local.X, "X", frame.Key),
            LocalYCm = QuantizeAckLocal(local.Y, "Y", frame.Key),
            LocalZCm = QuantizeAckLocal(local.Z, "Z", frame.Key),
            OriginCm = frame.Origin,
            Frame = QuaternionQuantized.FromBasis(frame.East, frame.Up, frame.South),
        };
    }

    internal static void FillAck(PlanetRegionTransferResult target, in RegionFrame frame, in PlanetVector3 globalCm)
    {
        var ack = BuildAck(frame, globalCm);
        target.LocalXCm = ack.LocalXCm;
        target.LocalYCm = ack.LocalYCm;
        target.LocalZCm = ack.LocalZCm;
        target.OriginXcm = ack.OriginCm.X;
        target.OriginYcm = ack.OriginCm.Y;
        target.OriginZcm = ack.OriginCm.Z;
        target.FrameX = ack.Frame.X;
        target.FrameY = ack.Frame.Y;
        target.FrameZ = ack.Frame.Z;
        target.FrameW = ack.Frame.W;
    }

    /// <summary>
    /// 局部厘米 → 线上 i32。<b>取整规则必须与每帧状态包同一条</b>（四舍五入远离零），
    /// 否则交接那一帧会和之后每一帧差 1 cm——那种差在实机上表现为"站定后微微发抖"。
    /// <para>超出 i32 口径时当场抛而不是钳位：钳位会给出一个"看起来合理"的坐标，
    /// 而它真正在说的是"这颗星球的区域层级与协议不匹配"，属于部署错误，要响。</para>
    /// </summary>
    internal static int QuantizeAckLocal(double cm, string axis, RegionKey key)
    {
        if (Math.Abs(cm) > PlayerStatePacket.MaxAbsLocalCm)
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                "区域 {0} 的局部坐标 {1}={2:0} cm 超出线上 i32 口径（±{3} cm）：区域层级与 P1 协议不匹配",
                key, axis, cm, PlayerStatePacket.MaxAbsLocalCm));
        int value = PlayerStatePacket.QuantizeLocalCm(cm, out double error);
        if (Math.Abs(error) > 0.5 + 1e-6)
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                "局部坐标 {0}={1:0} 的取整误差 {2:0.###} 超过半个厘米，取整规则与状态包不一致", axis, cm, error));
        return value;
    }

    /// <inheritdoc />
    public async Task AdvanceAsync(long tick)
    {
        var authority = EnsureAuthority();
        authority.AdvanceTo(tick);                 // 时钟倒退在这里抛，不静默吞：吞掉等于把过期重叠窗口留在表里
        _advancesSinceSave++;
        if (_advancesSinceSave >= ClockAdvancesPerFlush) await PersistAsync();
    }

    /// <inheritdoc />
    public Task<PlanetRegionSnapshot> SnapshotAsync(ulong characterId, byte[] planetCaliber)
    {
        var authority = EnsureAuthority();
        var snapshot = new PlanetRegionSnapshot
        {
            CharacterId = characterId,
            ServerTick = authority.Clock,
        };

        // 读路径也过口径闸门：一份按别的层级/别的星球算出来的订阅集，客户端拿去加载的是
        // 另一块地面的实体，而且全程不会报错。判定与迁移那条共用 CaliberRejection，不另写一份。
        if (CaliberRejection(planetCaliber, $"snapshot 角色号={characterId}") is { } caliberWhy)
            return Task.FromResult(Rejected(snapshot, PlanetRegionRejectCode.CaliberMismatch, caliberWhy));

        if (!TryEntityId(characterId, out long entityId, out string why))
            return Task.FromResult(Rejected(snapshot, PlanetRegionRejectCode.MalformedMessage, why));

        snapshot.Accepted = true;
        snapshot.Code = PlanetRegionRejectCode.None;
        snapshot.OwnerRegionId = authority.OwnerOf(entityId) is { } owner ? owner.ToId() : 0;
        var subscriptions = authority.Subscriptions(entityId);
        var subscribedIds = new long[subscriptions.Count];
        for (int i = 0; i < subscriptions.Count; i++) subscribedIds[i] = subscriptions[i].ToId();
        snapshot.SubscribedRegionIds = subscribedIds;

        // §6.4 的第二层：邻区只列"靠着我们这一侧"的那几列 cell。这里用库里的默认余量，
        // 它是从两个实测锚（网络视野 448 m、近景加载半跨 1270 m）取大再向上取整来的，不是拍的。
        var ownerRegion = authority.OwnerOf(entityId);
        if (ownerRegion is { } own)
        {
            var cells = PlanetRegionAoi.BoundaryCellSubscriptions(own, subscriptions);
            var wireCells = new PlanetCellKey[cells.Count];
            for (int i = 0; i < cells.Count; i++)
                wireCells[i] = new PlanetCellKey
                {
                    RegionId = cells[i].Region.ToId(),
                    Cx = cells[i].Cx,
                    Cz = cells[i].Cz,
                };
            snapshot.SubscribedBoundaryCells = wireCells;
        }

        var visible = authority.VisibleEntities(entityId);
        var visibleIds = new ulong[visible.Count];
        for (int i = 0; i < visible.Count; i++) visibleIds[i] = (ulong)visible[i];   // 表里只有正 long，转换不翻号
        snapshot.VisibleCharacterIds = visibleIds;
        snapshot.Reason = string.Format(CultureInfo.InvariantCulture,
            "已受理：regions={0} visible={1} boundary_cells={2}",
            snapshot.SubscribedRegionIds.Length, snapshot.VisibleCharacterIds.Length,
            snapshot.SubscribedBoundaryCells.Length);
        return Task.FromResult(snapshot);
    }

    /// <summary>
    /// 带理由的空快照。<b>空数组必须能说出它为什么是空的</b>："周围真的没人"与
    /// "这条请求不该被处理"在客户端是完全不同的两件事，前者继续按空加载、后者要停手并报警。
    /// </summary>
    private static PlanetRegionSnapshot Rejected(PlanetRegionSnapshot snapshot,
                                                 PlanetRegionRejectCode code, string why)
    {
        snapshot.Accepted = false;
        snapshot.Code = code;
        snapshot.Reason = string.Format(CultureInfo.InvariantCulture, "{0}：{1}", Explain(code), why);
        return snapshot;
    }

    /// <summary>
    /// 角色号 → 实体号。线上是 <see cref="ulong"/>，权威表与位打包区域号都是有符号 long，
    /// 所以越界只能拒不能转：<c>unchecked</c> 会把 1&lt;&lt;63 翻成负数，那会是一个"看起来合法"的实体号。
    /// </summary>
    internal static bool TryEntityId(ulong characterId, out long entityId, out string why)
    {
        if (characterId == 0)
        {
            entityId = 0;
            why = "角色号为 0（本项目里表示没有角色）";
            return false;
        }
        if (characterId > long.MaxValue)
        {
            entityId = 0;
            why = string.Format(CultureInfo.InvariantCulture, "角色号 {0} 超出 int64，转成有符号会翻成负数", characterId);
            return false;
        }
        entityId = (long)characterId;
        why = "";
        return true;
    }

    /// <summary>
    /// 两套拒绝理由 → 一条线上码。几何侧先判：几何不过时 <see cref="TransferResult.Reject"/> 恒为
    /// <see cref="AuthorityReject.None"/>，两个枚举不会同时给出非 None，所以"先看权限侧"是安全的。
    /// </summary>
    internal static PlanetRegionRejectCode ToCode(TransferRejectReason reason, AuthorityReject reject) =>
        reject != AuthorityReject.None ? ToCode(reject) : ToCode(reason);

    internal static PlanetRegionRejectCode ToCode(TransferRejectReason reason) => reason switch
    {
        TransferRejectReason.None => PlanetRegionRejectCode.None,
        TransferRejectReason.MalformedRegion => PlanetRegionRejectCode.MalformedRegion,
        TransferRejectReason.MalformedPoint => PlanetRegionRejectCode.MalformedPoint,
        TransferRejectReason.LevelMismatch => PlanetRegionRejectCode.LevelMismatch,
        TransferRejectReason.PointNotInTarget => PlanetRegionRejectCode.PointNotInTarget,
        TransferRejectReason.SameRegion => PlanetRegionRejectCode.SameRegion,
        TransferRejectReason.NotAdjacent => PlanetRegionRejectCode.NotAdjacent,
        TransferRejectReason.TooFarFromSurface => PlanetRegionRejectCode.TooFarFromSurface,
        // 枚举加值而这里没跟上，宁可当场抛，也不要静默落成 None——那等于"拒了但告诉客户端没事"
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "出现了没有映射的几何拒绝理由"),
    };

    internal static PlanetRegionRejectCode ToCode(AuthorityReject reject) => reject switch
    {
        AuthorityReject.None => PlanetRegionRejectCode.None,
        AuthorityReject.UnknownEntity => PlanetRegionRejectCode.UnknownEntity,
        AuthorityReject.OwnerMismatch => PlanetRegionRejectCode.OwnerMismatch,
        AuthorityReject.TickRegression => PlanetRegionRejectCode.TickRegression,
        _ => throw new ArgumentOutOfRangeException(nameof(reject), reject, "出现了没有映射的权限拒绝理由"),
    };

    /// <summary>给客户端的一句话说明；与 <see cref="TransferResult.ToString"/> 的数字对账字段拼在一起。</summary>
    internal static string Explain(PlanetRegionRejectCode code) => code switch
    {
        PlanetRegionRejectCode.MalformedRegion => "区域号解不开（坏包）",
        PlanetRegionRejectCode.MalformedPoint => "全局点不可用",
        PlanetRegionRejectCode.LevelMismatch => "区域层级和服务端口径不一致",
        PlanetRegionRejectCode.PointNotInTarget => "声明的目标区域和坐标反查结果不一致",
        PlanetRegionRejectCode.SameRegion => "本来就在同一个区域",
        PlanetRegionRejectCode.NotAdjacent => "两个区域不共边",
        PlanetRegionRejectCode.TooFarFromSurface => "点离球面太远",
        PlanetRegionRejectCode.UnknownEntity => "服务端没有这个实体的归属记录",
        PlanetRegionRejectCode.OwnerMismatch => "声明的 from 不是当前归属",
        PlanetRegionRejectCode.TickRegression => "tick 比服务端已受理的旧",
        PlanetRegionRejectCode.MalformedMessage => "请求本身进不了判定",
        PlanetRegionRejectCode.SessionMismatch => "角色号和这条连接绑定的角色不一致",
        // 这一条必须显式写：Explain 的兜底是"已受理"，新增的拒绝码要是漏在这儿，
        // 客户端收到的就是"被拒 + 已受理"这种自相矛盾的行——而它只能照后半句处理。
        PlanetRegionRejectCode.CaliberMismatch => "客户端和服务端不是同一颗星球（口径不符，拒绝进入世界）",
        PlanetRegionRejectCode.Underground => "声明的位置在真实地面以下（穿模）",
        _ => "已受理",
    };

    private PlanetRegionTransferResult Reject(ulong characterId, PlanetRegionRejectCode code, string reason) =>
        new()
        {
            CharacterId = characterId,
            Accepted = false,
            Applied = false,
            Code = code,
            Reason = string.Format(CultureInfo.InvariantCulture, "{0}：{1}", Explain(code), reason),
            NewRegionId = 0,
            HandoverTick = 0,
            ServerTick = EnsureAuthority().Clock,
        };

    /// <summary>把权威表投影成存档并写下去。整表重建，不做增量修补——表小，修补会留下"删掉的行还在"这种脏。</summary>
    private async Task PersistAsync()
    {
        var authority = EnsureAuthority();
        var state = _persisted.State ??= new PlanetRegionPersistedState();
        var records = new Dictionary<long, PlanetRegionEntityRecord>();
        foreach (var entity in authority.SnapshotEntities())
        {
            var ecef = EcefCentimeters.FromVector(entity.GlobalCm);
            records[entity.EntityId] = new PlanetRegionEntityRecord
            {
                RegionId = entity.Region.ToId(),
                Xcm = ecef.X,
                Ycm = ecef.Y,
                Zcm = ecef.Z,
                Tick = entity.Tick,
            };
        }
        state.Entities = records;
        state.Clock = authority.Clock;
        state.PlanetRadiusCm = authority.RadiusCm;
        state.RegionLevel = authority.Level;
        _advancesSinceSave = 0;
        await _persisted.WriteStateAsync();
    }
}
