namespace Horizon.Game.ECS.Arch.SyncGuard.Contracts;

/// <summary>
/// 实体发送资格分类：三类之一，判定结果必须唯一。
/// </summary>
public enum EntitySendCategory
{
    /// <summary>本地角色：发送资格源点，具备完整上行权限。</summary>
    LocalPlayer,

    /// <summary>深度绑定实体（唤物/宠物）：具备与本地角色等同的上行权限，但只能以自身身份发送。</summary>
    BoundEntity,

    /// <summary>无资格实体（远程角色/NPC/怪物/无绑定召唤物）：一律禁止发送。</summary>
    Unqualified,
}

/// <summary>
/// 发送拒绝原因：显式区分场景，便于告警审计与可追溯性。
/// </summary>
public enum SendRejectReason
{
    /// <summary>连接未就绪（未连接/连接状态异常）。</summary>
    ConnectionNotReady,

    /// <summary>本地身份未确立（同步握手未完成或本地玩家 ID 未设置）。</summary>
    IdentityNotEstablished,

    /// <summary>发起实体非本地角色且无有效绑定关系。</summary>
    NotLocalPlayer,

    /// <summary>场景内本地玩家实体重复，非真实身份的本地实体被拒绝。</summary>
    LocalPlayerDuplicated,

    /// <summary>绑定关系无效（不存在/已失效/类型未定义）。</summary>
    BindingInvalid,

    /// <summary>绑定关系归属方非本地角色。</summary>
    BindingNotOwnedByLocal,

    /// <summary>消息携带身份与授权上下文身份不一致（身份冒充/身份绑定错位）。</summary>
    IdentityMismatch,

    /// <summary>检测到绕过受控发送入口直接调用发送通道的旁路行为。</summary>
    BypassDetected,

    /// <summary>判定内部异常，按拒绝处理并告警。</summary>
    InternalError,
}

/// <summary>
/// 绑定类型：深度绑定实体的归属分类。
/// </summary>
public enum BindingType
{
    /// <summary>唤物（召唤兽、法阵造物等由本地角色召唤的自主行为实体）。</summary>
    Summon = 0,

    /// <summary>宠物（随行宠物等由本地角色携带的自主行为实体）。</summary>
    Pet = 1,
}

/// <summary>
/// 绑定失效原因。
/// </summary>
public enum BindingInvalidateReason
{
    /// <summary>唤物被遣散。</summary>
    Dismissed = 0,

    /// <summary>实体死亡。</summary>
    Died = 1,

    /// <summary>解除绑定。</summary>
    Unbound = 2,

    /// <summary>归属主人资格丧失（本地角色身份丢失/握手重置）。</summary>
    OwnerLost = 3,
}

/// <summary>
/// 本地角色发送资格状态相位。
/// </summary>
public enum LocalEligibilityPhase
{
    /// <summary>未连接：无发送资格。</summary>
    Disconnected = 0,

    /// <summary>已连接、同步握手未完成：无发送资格。</summary>
    ConnectedHandshakePending = 1,

    /// <summary>身份确立/资格具备：本地角色及有效绑定实体可上行。</summary>
    Established = 2,

    /// <summary>资格丧失（身份标志丢失/握手重置）：本地角色及全部绑定实体禁止上行。</summary>
    EligibilityLost = 3,
}