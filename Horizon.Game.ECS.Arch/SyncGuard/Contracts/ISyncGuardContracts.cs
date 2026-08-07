using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Sync;

namespace Horizon.Game.ECS.Arch.SyncGuard.Contracts;

/// <summary>
/// 集中式发送资格判定组件：代码库中唯一的资格判定规则源。
/// 判定依据仅为"身份标志 + 绑定关系 + 前置条件"三元组，输出唯一分类与结论。
/// </summary>
public interface IOutboundSyncAuthorizer
{
    /// <summary>对一次上行发送请求进行资格判定，输出唯一结论（拒绝路径内部已上报违规告警）。</summary>
    SyncSendVerdict Authorize(in SendRequestContext request);

    /// <summary>将实体归类为三选一的发送资格分类（不改变状态，纯查询）。</summary>
    EntitySendCategory ClassifyEntity(ulong entityId);
}

/// <summary>
/// 唤物/宠物绑定关系注册表：登记、查询、失效深度绑定实体与本地角色的归属关系。
/// 是资格判定中绑定数据来源。
/// </summary>
public interface IBindingRelationshipRegistry
{
    /// <summary>登记一条绑定关系（召唤/携带唤物、宠物）。同一 BoundEntityId 重复登记自动覆盖旧记录并触发告警。</summary>
    void RegisterBinding(ulong boundEntityId, ulong ownerEntityId, BindingType bindingType);

    /// <summary>查询有效绑定关系（仅返回 Valid=true 且归属 ownerEntityId 的记录）。</summary>
    bool TryGetValidBinding(ulong boundEntityId, out BindingRelationship relationship);

    /// <summary>是否存在绑定记录（含已失效记录；用于区分"从未绑定"与"绑定已失效"）。</summary>
    bool HasBindingRecord(ulong boundEntityId);

    /// <summary>使绑定关系失效（遣散/死亡/解绑），并触发绑定实体资格联动撤销。</summary>
    void InvalidateBinding(ulong boundEntityId, BindingInvalidateReason reason);

    /// <summary>枚举当前所有有效绑定实体 ID（供资格联动批量撤销/恢复）。</summary>
    IReadOnlyCollection<ulong> GetValidBoundEntityIds();

    /// <summary>设置绑定失效联动回调（绑定失效时通知资格状态机撤销该实体发送资格）。</summary>
    void SetInvalidationCallback(Action<ulong, BindingInvalidateReason> callback);

    /// <summary>设置重复登记告警回调（同一 BoundEntityId 重复登记时触发）。</summary>
    void SetDuplicateRegisterCallback(Action<ulong, ulong> callback);
}

/// <summary>
/// 本地角色发送资格状态机：维护"连接就绪 + 握手完成 + 身份确立"的资格相位，
/// 并联动派生深度绑定实体的发送资格。
/// </summary>
public interface ILocalSendEligibilityState
{
    /// <summary>本地角色当前是否具备发送资格（连接就绪 + 握手完成 + 身份确立）。</summary>
    bool IsLocalEligible { get; }

    /// <summary>绑定实体是否具备发送资格（本地资格具备 且 该实体持有有效绑定关系）。</summary>
    bool IsBoundEntityEligible(ulong boundEntityId);

    /// <summary>本地资格状态变化事件（供绑定实体资格联动与日志输出）。</summary>
    event Action<LocalEligibilitySnapshot> StateChanged;
}

/// <summary>
/// 受控发送服务：实体同步类上行的唯一合法发送入口。
/// 内部完成资格授权 → 放行则调用网络发送通道 / 拒绝则静默拦截并告警。
/// </summary>
public interface IGuardSyncSender
{
    /// <summary>以本地角色身份发送同步帧（内部完成授权）。</summary>
    Task<bool> SendLocalAsync(SyncPacket packet, ulong localCharacterId);

    /// <summary>以深度绑定实体身份发送同步帧（内部完成绑定校验 + 本地资格校验）。</summary>
    Task<bool> SendBoundEntityAsync(SyncPacket packet, ulong boundEntityId);
}

/// <summary>
/// 违规发送尝试告警上报接口：输出限频日志与监控指标。
/// </summary>
public interface ISendViolationReporter
{
    /// <summary>上报一次违规发送尝试（内部按 实体ID+原因 维度限频，每秒最多 1 条）。</summary>
    void ReportViolation(in SendViolationInfo violation);
}

/// <summary>
/// 注入 <see cref="NetworkManager"/> 的兜底守卫回调：拦截绕过受控发送入口的旁路发送。
/// 仅针对实体同步类上行帧，不拦截登录握手、心跳等连接层消息。
/// </summary>
public interface IOutboundSyncGuard
{
    /// <summary>兜底校验一条即将入网的实体同步上行帧；返回 false 时消息不得进入发送通道。</summary>
    bool TryApprove(SyncFrameMessage syncFrame, ulong senderEntityId);
}