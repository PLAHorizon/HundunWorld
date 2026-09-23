using Horizon.Game.Message.Sync;

namespace Horizon.Game.Sim;

/// <summary>
/// 一帧确定性输入：一个玩家在某个客户端 tick 的移动/动作输入。
/// <para>
/// 双端约定：客户端预测、Orleans 权威回放、UE5 DS 校验都消费同一份输入语义，
/// 输入包体复用 v6 协议字段（<see cref="Horizon.Game.Message.Sync.InputPacket"/>），
/// 保证 <see cref="Horizon.Game.Sim.MovementValidator"/> 在两端按 <see cref="MovementFormula.FormulaVersion"/> 按位一致回放。
/// </para>
/// </summary>
public interface ISimInput
{
    /// <summary>客户端 tick（升序、连续）；服务端据此排序与去重。</summary>
    long ClientTick { get; }

    /// <summary>该帧时间步长（秒）；服务端定频通常为 1/60。</summary>
    float DeltaSeconds { get; }

    /// <summary>原始移动输入（含 InputBits / MoveX / MoveY / MaxSpeed / PredictedEndZ）。</summary>
    InputPacket Packet { get; }
}

/// <summary>
/// 确定性模拟实体：以权威位置状态参与 <see cref="ISimWorld"/> 推进。
/// <para>
/// 实现者必须保持纯确定性：状态只能由 <see cref="ApplyInput"/> 驱动，
/// 不得依赖 <c>DateTime.Now</c>、随机源或遍历顺序不定的集合。
/// </para>
/// </summary>
public interface ISimEntity
{
    /// <summary>实体网络 ID（与 <see cref="CorrectionPacket.EntityId"/> 对应）。</summary>
    ulong EntityId { get; }

    /// <summary>当前权威位置（Z-up 世界坐标）。</summary>
    WorldPosition Position { get; }

    /// <summary>当前 Z 方向速度（供外部推演/校验用）。</summary>
    float Vz { get; }

    /// <summary>应用一帧输入（水平移动 + 跳跃/重力），推进自身状态。</summary>
    void ApplyInput(ISimInput input);
}

/// <summary>
/// 确定性世界：双端（Orleans 权威回放 / UE5 DS）共用的模拟容器契约。
/// <para>
/// 目标宿主：<c>ZoneShardGrain</c>（灰度期权威）与 <c>ManagedHundunWorld.SimDriver</c>（UE5 DS）。
/// 两者都经本契约驱动同一份 <see cref="Horizon.Game.Sim"/> 数学，杜绝“双份状态 + 双向 ring-call”。
/// </para>
/// </summary>
public interface ISimWorld
{
    /// <summary>本世界使用的公式版本；必须等于 <see cref="MovementFormula.FormulaVersion"/>，两端不一致即协议不兼容。</summary>
    int FormulaVersion { get; }

    /// <summary>推进一帧（<paramref name="dt"/> 秒），依次对每个实体应用已排队的输入。</summary>
    void Step(float dt);

    /// <summary>世界内所有实体的只读视图（顺序稳定，便于快照序列化）。</summary>
    IReadOnlyList<ISimEntity> Entities { get; }

    /// <summary>产出权威快照（经 <see cref="SyncPacketCodec"/> 下发客户端）。</summary>
    SnapshotPacket CaptureSnapshot();

    /// <summary>应用服务器修正（客户端预测回滚时吸附权威位置）。</summary>
    void ApplyCorrection(CorrectionPacket packet);
}
