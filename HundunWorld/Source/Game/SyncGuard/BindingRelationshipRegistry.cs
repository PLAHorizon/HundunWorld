using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Horizon.Game.ECS.Arch.SyncGuard.Contracts;

namespace HundunWorld.Game.SyncGuard;

/// <summary>
/// 唤物/宠物绑定关系注册表：登记、查询、失效深度绑定实体与本地角色的归属关系。
/// <para>
/// 以 <see cref="BindingRelationship.BoundEntityId"/> 为主键保证绑定关系唯一归属；
/// 登记/失效输出可观测日志；失效触发绑定实体资格联动（经注入的回调通知
/// <see cref="ILocalSendEligibilityState"/>）。
/// </para>
/// </summary>
public sealed class BindingRelationshipRegistry : IBindingRelationshipRegistry
{
    /// <summary>绑定关系索引：BoundEntityId → 绑定关系（进程内内存数据，不做持久化）。</summary>
    private readonly ConcurrentDictionary<ulong, BindingRelationship> _bindings = new();

    /// <summary>绑定关系失效后的联动回调（由装配方注入，通知资格状态机撤销该实体资格）。</summary>
    private Action<ulong, BindingInvalidateReason>? _onInvalidated;

    /// <summary>重复登记告警回调（供测试/观测注入）。</summary>
    private Action<ulong, ulong>? _onDuplicateRegister;

    /// <summary>绑定关系失效联动回调（由装配方注入，接收 (boundEntityId, reason)）。</summary>
    public void SetInvalidationCallback(Action<ulong, BindingInvalidateReason> callback)
    {
        _onInvalidated = callback ?? throw new ArgumentNullException(nameof(callback));
    }

    /// <summary>重复登记告警回调（由装配方注入）。</summary>
    public void SetDuplicateRegisterCallback(Action<ulong, ulong> callback)
    {
        _onDuplicateRegister = callback ?? throw new ArgumentNullException(nameof(callback));
    }

    /// <inheritdoc />
    public void RegisterBinding(ulong boundEntityId, ulong ownerEntityId, BindingType bindingType)
    {
        // 非法参数拒绝登记（spec 6.2 归属主人约束、2.2.2(2) 异常映射）。
        if (boundEntityId == 0 || ownerEntityId == 0)
        {
            _onDuplicateRegister?.Invoke(boundEntityId, ownerEntityId);
            System.Diagnostics.Debug.WriteLine($"[BindingRelationshipRegistry] 拒绝登记非法绑定: Bound={boundEntityId}, Owner={ownerEntityId}（ID 为 0）");
            return;
        }

        if (!Enum.IsDefined(bindingType))
        {
            System.Diagnostics.Debug.WriteLine($"[BindingRelationshipRegistry] 拒绝登记未定义绑定类型: Bound={boundEntityId}, Type={bindingType}");
            return;
        }

        var relationship = new BindingRelationship
        {
            BoundEntityId = boundEntityId,
            OwnerEntityId = ownerEntityId,
            BindingType = bindingType,
            IsValid = true,
            BoundAt = DateTimeOffset.UtcNow,
        };

        var replaced = _bindings.AddOrUpdate(boundEntityId, relationship, (_, old) =>
        {
            if (old.IsValid)
            {
                // 重复登记：旧绑定自动失效并告警（spec 6.2 单一归属、5.2.3 异常 4）。
                _onDuplicateRegister?.Invoke(boundEntityId, old.OwnerEntityId);
            }
            return relationship;
        });

        System.Diagnostics.Debug.WriteLine($"[BindingRelationshipRegistry] 登记绑定: Bound={boundEntityId}, Owner={ownerEntityId}, Type={bindingType}, At={relationship.BoundAt:O}");
    }

    /// <inheritdoc />
    public bool TryGetValidBinding(ulong boundEntityId, out BindingRelationship relationship)
    {
        if (_bindings.TryGetValue(boundEntityId, out relationship) && relationship.IsValid)
        {
            return true;
        }

        relationship = null!;
        return false;
    }

    /// <inheritdoc />
    public bool HasBindingRecord(ulong boundEntityId)
    {
        return _bindings.ContainsKey(boundEntityId);
    }

    /// <inheritdoc />
    public void InvalidateBinding(ulong boundEntityId, BindingInvalidateReason reason)
    {
        if (!_bindings.TryGetValue(boundEntityId, out var relationship))
        {
            return;
        }

        if (!relationship.IsValid)
        {
            return;
        }

        relationship.IsValid = false;
        relationship.InvalidatedAt = DateTimeOffset.UtcNow;

        System.Diagnostics.Debug.WriteLine($"[BindingRelationshipRegistry] 绑定失效: Bound={boundEntityId}, Owner={relationship.OwnerEntityId}, Reason={reason}, At={relationship.InvalidatedAt:O}");

        // 立即通知资格状态机联动撤销该实体发送资格（spec 5.2.1 规则 2、4.2.3 联动一致性）。
        _onInvalidated?.Invoke(boundEntityId, reason);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<ulong> GetValidBoundEntityIds()
    {
        return _bindings.Values.Where(v => v.IsValid).Select(v => v.BoundEntityId).ToArray();
    }

    /// <summary>当前全部绑定记录数（含失效，供观测）。</summary>
    public int TotalBindingCount => _bindings.Count;

    /// <summary>清空全部绑定记录（断线重连/退出场景使用）。</summary>
    public void Clear()
    {
        _bindings.Clear();
    }
}