using System;
using System.Collections.Generic;

namespace Horizon.Game.Message.Sync.Server;

/// <summary>
/// <see cref="PlayerSessionState"/> 的配置参数（运行时可调）。
/// </summary>
public sealed class PlayerSessionOptions
{
    /// <summary>输入环形缓冲的最大容量；超过后最旧的输入被丢弃（用于服务器回放 reconciliation）。</summary>
    public int InputBufferCapacity { get; set; } = 256;

    /// <summary>每个连接 AOI Interest Set 允许订阅的最大 ChunkCell 数；防御外挂超范围订阅。</summary>
    public int MaxInterestChunks { get; set; } = 4096;

    /// <summary>允许接受的最老输入与当前服务器 tick 的最大差值（太旧的输入视作重放攻击）。</summary>
    public long MaxInputLagTicks { get; set; } = 600; // 60Hz × 10s
}

/// <summary>
/// 纯数据/逻辑层的玩家会话状态机。<br/>
/// - 不含任何 Orleans/Gateway 依赖，可被服务器 grain、单测、以及未来的模拟客户端直接复用。<br/>
/// - 所有公开方法要求单线程调用；grain 的 turn-based 执行已天然满足该约束，测试中请自行串行化。
/// </summary>
/// <remarks>
/// 职责（对齐 P1-a 计划）：
/// <list type="bullet">
///   <item>接收 <see cref="InputPacket"/> 并按 <see cref="InputPacket.ClientTick"/> 去重 / 单调性校验。</item>
///   <item>维护 "最近已处理客户端 tick"，用于下发 <see cref="InputAckPacket"/> 与 reconciliation。</item>
///   <item>维护 AOI Interest Set（<c>HashSet&lt;ulong&gt;</c> 的 ChunkCell Morton 键），供 Zone→Gateway fanout 使用。</item>
///   <item>维护版本向量（<c>baselineVersion / worldPatchVersion / lastAppliedDiffSeq</c>），用于 <see cref="ReconnectResumePacket"/> 续连决策。</item>
/// </list>
/// </remarks>
public sealed class PlayerSessionState
{
    private readonly PlayerSessionOptions _options;
    private readonly Queue<InputPacket> _inputBuffer;
    private readonly HashSet<ulong> _interestChunks = new();

    /// <summary>最近一次被服务器"接受并入队"的客户端 tick；用于单调性校验。</summary>
    public long LastAcceptedClientTick { get; private set; }

    /// <summary>服务器权威模拟已消费到的客户端 tick；由 <see cref="ConsumeInputs"/> 推进，决定 <see cref="BuildInputAck"/> 下发值。</summary>
    public long LastProcessedClientTick { get; private set; }

    /// <summary>当前服务器 tick（由所属 Zone 按帧推进，写入 PlayerSession 以便 ACK 帧携带同源时基）。</summary>
    public long ServerTick { get; private set; }

    /// <summary>客户端上报的基线世界版本（来自 <see cref="HandshakePacket"/> 扩展字段 / <see cref="ReconnectResumePacket"/>）。</summary>
    public int BaselineVersion { get; private set; }

    /// <summary>客户端上报的 WorldPatch 版本。</summary>
    public int WorldPatchVersion { get; private set; }

    /// <summary>客户端上报的已应用 diff 全局序号（high-water mark）。</summary>
    public long LastAppliedDiffSeq { get; private set; }

    /// <summary>当前 AOI Interest Set 的只读视图；调用方不得修改底层集合。</summary>
    public IReadOnlyCollection<ulong> InterestChunks => _interestChunks;

    /// <summary>输入缓冲区当前长度（用于监控 / 验证）。</summary>
    public int BufferedInputCount => _inputBuffer.Count;

    /// <summary>
    /// 初始化会话。<paramref name="options"/> 为 <c>null</c> 时使用默认配置。
    /// </summary>
    public PlayerSessionState(PlayerSessionOptions? options = null)
    {
        _options = options ?? new PlayerSessionOptions();
        if (_options.InputBufferCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options),
                "InputBufferCapacity 必须为正数。");
        }
        _inputBuffer = new Queue<InputPacket>(_options.InputBufferCapacity);
    }

    /// <summary>
    /// 应用握手信息（或重连 resume），把客户端自报的版本向量写入状态。
    /// </summary>
    /// <returns>true 表示版本向量有效被采纳；false 表示参数非法（含任一负值）。</returns>
    public bool ApplyHandshake(int baselineVersion, int worldPatchVersion, long lastAppliedDiffSeq)
    {
        if (baselineVersion < 0 || worldPatchVersion < 0 || lastAppliedDiffSeq < 0)
        {
            return false;
        }
        BaselineVersion = baselineVersion;
        WorldPatchVersion = worldPatchVersion;
        LastAppliedDiffSeq = lastAppliedDiffSeq;
        return true;
    }

    /// <summary>
    /// 推进服务器 tick；由所属 Zone 按帧调用（允许等值调用，不允许倒退）。
    /// </summary>
    /// <returns>true 表示被采纳；false 表示参数倒退。</returns>
    public bool AdvanceServerTick(long serverTick)
    {
        if (serverTick < ServerTick) return false;
        ServerTick = serverTick;
        return true;
    }

    /// <summary>
    /// 接收并缓冲客户端输入；返回值表达处理结果，便于服务器计量异常并做 anti-cheat 处置。
    /// </summary>
    public InputAcceptResult AcceptInput(InputPacket input)
    {
        if (input is null) return InputAcceptResult.Invalid;
        // 旧 tick（已处理或低于最新接受）→ 重复包，幂等丢弃。
        if (input.ClientTick <= LastAcceptedClientTick)
        {
            return InputAcceptResult.Duplicate;
        }
        // 太老的输入直接丢；避免客户端累积攒包导致巨量重放。
        if (ServerTick > 0 && (ServerTick - input.ClientTick) > _options.MaxInputLagTicks)
        {
            return InputAcceptResult.TooOld;
        }

        // 缓冲容量上限：覆盖最旧输入（环形语义）。
        if (_inputBuffer.Count >= _options.InputBufferCapacity)
        {
            _inputBuffer.Dequeue();
        }
        _inputBuffer.Enqueue(input);
        LastAcceptedClientTick = input.ClientTick;
        return InputAcceptResult.Accepted;
    }

    /// <summary>
    /// 服务器权威模拟调用：取出全部缓冲输入按 ClientTick 升序返回；返回后 <see cref="LastProcessedClientTick"/> 被推进到最高值。
    /// </summary>
    public IReadOnlyList<InputPacket> ConsumeInputs()
    {
        if (_inputBuffer.Count == 0)
        {
            return Array.Empty<InputPacket>();
        }
        var drained = new InputPacket[_inputBuffer.Count];
        int i = 0;
        while (_inputBuffer.Count > 0)
        {
            drained[i++] = _inputBuffer.Dequeue();
        }
        // AcceptInput 已保证单调，drained 天然按时间顺序。
        LastProcessedClientTick = drained[^1].ClientTick;
        return drained;
    }

    /// <summary>
    /// 构建一个 <see cref="InputAckPacket"/>，携带当前 <see cref="LastProcessedClientTick"/>。
    /// </summary>
    public InputAckPacket BuildInputAck(long echoClientTick = 0) => new()
    {
        LastProcessedClientTick = LastProcessedClientTick,
        ServerTick = ServerTick,
        EchoClientTick = echoClientTick,
    };

    /// <summary>
    /// AOI 增量订阅：把一组 ChunkCell Morton 键加入 Interest Set。
    /// </summary>
    /// <returns>实际新订阅的键数量（已存在的会被忽略）。</returns>
    public int SubscribeChunks(ReadOnlySpan<ulong> mortonKeys)
    {
        int added = 0;
        foreach (var key in mortonKeys)
        {
            if (_interestChunks.Count >= _options.MaxInterestChunks)
            {
                break; // 到达上限后停止，调用方可检查返回值判断是否全量成功。
            }
            if (_interestChunks.Add(key)) added++;
        }
        return added;
    }

    /// <summary>AOI 增量退订：把一组 ChunkCell Morton 键从 Interest Set 移除。</summary>
    /// <returns>实际被移除的键数量。</returns>
    public int UnsubscribeChunks(ReadOnlySpan<ulong> mortonKeys)
    {
        int removed = 0;
        foreach (var key in mortonKeys)
        {
            if (_interestChunks.Remove(key)) removed++;
        }
        return removed;
    }

    /// <summary>查询是否订阅了指定 ChunkCell（Gateway 本地扇出时用）。</summary>
    public bool IsSubscribedTo(ulong mortonKey) => _interestChunks.Contains(mortonKey);

    /// <summary>
    /// 处理 <see cref="ReconnectResumePacket"/>：把客户端自报的版本向量归并到当前状态，
    /// 取 (服务端已处理, 客户端上报) 的较小值，避免客户端伪造"已收到更多 diff"导致服务器漏发。
    /// </summary>
    /// <returns>续连决策（见 <see cref="ResumeDecision"/>）。</returns>
    public ResumeDecision ApplyReconnect(ReconnectResumePacket packet, long serverHeadDiffSeq, int serverWorldPatchVersion)
    {
        if (packet is null) throw new ArgumentNullException(nameof(packet));

        // 版本差过大：客户端必须重新走启动器拉 patch。
        if (packet.WorldPatchVersion < serverWorldPatchVersion - 1)
        {
            return ResumeDecision.RequireLauncherPatch;
        }

        // 客户端自报 diff 高于服务器实际 head：异常，踢回登录。
        if (packet.LastAppliedDiffSeq > serverHeadDiffSeq)
        {
            return ResumeDecision.ForceReLogin;
        }

        BaselineVersion = packet.BaselineVersion;
        WorldPatchVersion = packet.WorldPatchVersion;
        LastAppliedDiffSeq = Math.Min(packet.LastAppliedDiffSeq, serverHeadDiffSeq);

        // 增量窗口过大（> 窗口缓冲）→ 客户端需要重拉 chunk 全量。
        // 这里用 InputBufferCapacity × 64 作为保守上限，具体阈值后续由 Zone 的 checkpoint 策略决定。
        var backlog = serverHeadDiffSeq - LastAppliedDiffSeq;
        if (backlog > (long)_options.InputBufferCapacity * 64)
        {
            return ResumeDecision.ResendFullChunks;
        }

        return ResumeDecision.ResumeIncremental;
    }
}

/// <summary><see cref="PlayerSessionState.AcceptInput"/> 的结果。</summary>
public enum InputAcceptResult : byte
{
    /// <summary>包体为 null 或基本字段非法。</summary>
    Invalid = 0,
    /// <summary>已接受并缓冲。</summary>
    Accepted = 1,
    /// <summary>ClientTick ≤ LastAcceptedClientTick，视为重复。</summary>
    Duplicate = 2,
    /// <summary>ClientTick 距离当前 ServerTick 过远，视为过期/重放。</summary>
    TooOld = 3,
}

/// <summary><see cref="PlayerSessionState.ApplyReconnect"/> 的决策。</summary>
public enum ResumeDecision : byte
{
    /// <summary>续连成功，服务器按 <c>[LastAppliedDiffSeq+1, head]</c> 推送增量。</summary>
    ResumeIncremental = 0,
    /// <summary>版本向量差距过大，需客户端先去 GengDi 启动器补齐 patch。</summary>
    RequireLauncherPatch = 1,
    /// <summary>backlog 过大，服务器改为重发全量 chunk，而不是推 diff 流。</summary>
    ResendFullChunks = 2,
    /// <summary>客户端上报的状态内部矛盾（如 diff &gt; head），直接踢回登录。</summary>
    ForceReLogin = 3,
}
