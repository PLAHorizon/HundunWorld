using System;
using System.Collections.Generic;
using Horizon.Game.ECS.Arch.SyncGuard.Contracts;
using Horizon.Game.Message.Sync;

namespace HundunWorld.Game.SyncGuard;

/// <summary>
/// 集中式发送资格判定组件：代码库中唯一的资格判定规则源。
/// <para>
/// 判定依据仅为"身份标志 + 绑定关系 + 前置条件"三元组，输出唯一分类与结论。
/// 判定实现为 O(1)：身份分类走委托缓存查询、绑定查询走注册表索引，无数据库、无反射。
/// </para>
/// </summary>
public sealed class OutboundSyncAuthorizer : IOutboundSyncAuthorizer
{
    private readonly IBindingRelationshipRegistry _bindingRegistry;
    private readonly ILocalSendEligibilityState _eligibilityState;
    private readonly ISendViolationReporter _violationReporter;

    /// <summary>查询实体是否为本地玩家（O(1)，由装配方提供缓存字典查询）。</summary>
    private readonly Func<ulong, bool> _isLocalPlayerEntity;

    /// <summary>获取当前本地玩家实体 ID（0 表示身份未确立）。</summary>
    private readonly Func<ulong> _getLocalPlayerEntityId;

    /// <summary>获取当前场景内本地玩家实体数量（用于唯一性校验）。</summary>
    private readonly Func<int> _getLocalPlayerEntityCount;


    /// <summary>
    /// 初始化授权器。
    /// </summary>
    /// <param name="bindingRegistry">绑定关系注册表（深度绑定实体资格判定数据来源）。</param>
    /// <param name="eligibilityState">本地资格状态机（前置条件与绑定资格联动来源）。</param>
    /// <param name="violationReporter">违规告警上报。</param>
    /// <param name="isLocalPlayerEntity">实体是否为本地玩家的 O(1) 查询。</param>
    /// <param name="getLocalPlayerEntityId">当前本地玩家实体 ID 提供者。</param>
    /// <param name="getLocalPlayerEntityCount">本地玩家实体数量提供者（唯一性校验）。</param>
    public OutboundSyncAuthorizer(
        IBindingRelationshipRegistry bindingRegistry,
        ILocalSendEligibilityState eligibilityState,
        ISendViolationReporter violationReporter,
        Func<ulong, bool> isLocalPlayerEntity,
        Func<ulong> getLocalPlayerEntityId,
        Func<int> getLocalPlayerEntityCount)
    {
        _bindingRegistry = bindingRegistry ?? throw new ArgumentNullException(nameof(bindingRegistry));
        _eligibilityState = eligibilityState ?? throw new ArgumentNullException(nameof(eligibilityState));
        _violationReporter = violationReporter ?? throw new ArgumentNullException(nameof(violationReporter));
        _isLocalPlayerEntity = isLocalPlayerEntity ?? throw new ArgumentNullException(nameof(isLocalPlayerEntity));
        _getLocalPlayerEntityId = getLocalPlayerEntityId ?? throw new ArgumentNullException(nameof(getLocalPlayerEntityId));
        _getLocalPlayerEntityCount = getLocalPlayerEntityCount ?? throw new ArgumentNullException(nameof(getLocalPlayerEntityCount));
    }


    /// <inheritdoc />
    public SyncSendVerdict Authorize(in SendRequestContext request)
    {
        try
        {
            // 前置条件校验（spec 5.1.3 异常 1、4.5.4 降级兼容）：
            // 本地资格未具备（连接未就绪 / 握手未完成 / 本地身份未确立）→ 一律拒绝。
            if (!_eligibilityState.IsLocalEligible)
            {
                return DenyAndReport(request, EntitySendCategory.Unqualified, SendRejectReason.IdentityNotEstablished);
            }

            var entityId = request.RequestingEntityId;
            var category = ClassifyEntity(entityId);

            switch (category)
            {
                case EntitySendCategory.LocalPlayer:
                    return AuthorizeLocalPlayer(request, entityId);

                case EntitySendCategory.BoundEntity:
                    return AuthorizeBoundEntity(request, entityId);

                default:
                    // 区分"绑定已失效"与"从未绑定"（spec 6.2：绑定失效后立即停止上行，拒绝原因应为 BindingInvalid）。
                    if (entityId != 0 && _bindingRegistry.HasBindingRecord(entityId))
                    {
                        return DenyAndReport(request, category, SendRejectReason.BindingInvalid);
                    }
                    return DenyAndReport(request, category, SendRejectReason.NotLocalPlayer);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OutboundSyncAuthorizer] 判定内部异常: {ex.Message}");
            return DenyAndReport(request, EntitySendCategory.Unqualified, SendRejectReason.InternalError);
        }
    }

    /// <inheritdoc />
    public EntitySendCategory ClassifyEntity(ulong entityId)
    {
        // 身份为本地玩家 → 本地角色资格（spec 5.1.1 规则 3）。
        if (entityId != 0 && _isLocalPlayerEntity(entityId))
        {
            return EntitySendCategory.LocalPlayer;
        }

        // 非本地玩家但存在有效绑定关系 → 深度绑定实体资格。
        if (entityId != 0 && _bindingRegistry.TryGetValidBinding(entityId, out _))
        {
            return EntitySendCategory.BoundEntity;
        }

        return EntitySendCategory.Unqualified;
    }

    /// <summary>本地角色分支：场景内本地玩家唯一性校验 + 放行。</summary>
    private SyncSendVerdict AuthorizeLocalPlayer(in SendRequestContext request, ulong entityId)
    {
        // 本地角色唯一性校验（spec 6.1 规则 3、5.3.3 异常 4）：
        // 重复本地实体中仅与真实身份完全一致者放行，其余拒绝并告警。
        var localPlayerId = _getLocalPlayerEntityId();
        if (localPlayerId != 0 && entityId != localPlayerId)
        {
            return DenyAndReport(request, EntitySendCategory.Unqualified, SendRejectReason.LocalPlayerDuplicated);
        }

        if (_getLocalPlayerEntityCount() > 1)
        {
            // 场景内存在多个本地玩家实体：仅真实身份（与提供者一致）放行。
            if (entityId != localPlayerId)
            {
                return DenyAndReport(request, EntitySendCategory.Unqualified, SendRejectReason.LocalPlayerDuplicated);
            }
        }

        return SyncSendVerdict.Allow(EntitySendCategory.LocalPlayer);
    }

    /// <summary>深度绑定实体分支：绑定关系有效 + 归属本地角色 + 本地资格具备。</summary>
    private SyncSendVerdict AuthorizeBoundEntity(in SendRequestContext request, ulong entityId)
    {
        if (!_bindingRegistry.TryGetValidBinding(entityId, out var binding))
        {
            return DenyAndReport(request, EntitySendCategory.Unqualified, SendRejectReason.BindingInvalid);
        }

        var localPlayerId = _getLocalPlayerEntityId();
        if (binding.OwnerEntityId != localPlayerId)
        {
            return DenyAndReport(request, EntitySendCategory.BoundEntity, SendRejectReason.BindingNotOwnedByLocal);
        }

        if (!_eligibilityState.IsBoundEntityEligible(entityId))
        {
            return DenyAndReport(request, EntitySendCategory.BoundEntity, SendRejectReason.IdentityNotEstablished);
        }

        return SyncSendVerdict.Allow(EntitySendCategory.BoundEntity);
    }

    private SyncSendVerdict DenyAndReport(in SendRequestContext request, EntitySendCategory category, SendRejectReason reason)
    {
        var violation = new SendViolationInfo(request.RequestingEntityId, category, reason, request.RequestedAt);

        _violationReporter.ReportViolation(in violation);
        return SyncSendVerdict.Deny(category, reason);
    }
}