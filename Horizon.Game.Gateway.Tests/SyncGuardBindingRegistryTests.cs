using System;
using System.Linq;
using Horizon.Game.ECS.Arch.SyncGuard.Contracts;
using HundunWorld.Game.SyncGuard;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 绑定关系注册表（BindingRelationshipRegistry）单元测试：覆盖 spec 6.2 全部约束。
/// </summary>
public class BindingRelationshipRegistryTests
{
    private const ulong LocalPlayerId = 1001;
    private const ulong BoundSummonId = 2001;
    private const ulong BoundPetId = 2002;

    [Fact]
    public void RegisterBinding_Valid_ThenTryGetValidBinding_Succeeds()
    {
        var registry = new BindingRelationshipRegistry();
        registry.RegisterBinding(BoundSummonId, LocalPlayerId, BindingType.Summon);

        Assert.True(registry.TryGetValidBinding(BoundSummonId, out var rel));
        Assert.Equal(LocalPlayerId, rel.OwnerEntityId);
        Assert.Equal(BindingType.Summon, rel.BindingType);
        Assert.True(rel.IsValid);
        Assert.NotEqual(default, rel.BoundAt);
    }

    [Fact]
    public void RegisterBinding_Unregistered_QueryFails()
    {
        var registry = new BindingRelationshipRegistry();
        Assert.False(registry.TryGetValidBinding(BoundPetId, out _));
    }

    [Fact]
    public void RegisterBinding_DuplicateOverwritesOld_WithWarning()
    {
        var registry = new BindingRelationshipRegistry();
        int duplicateWarnings = 0;
        registry.SetDuplicateRegisterCallback((bound, owner) => duplicateWarnings++);

        registry.RegisterBinding(BoundSummonId, LocalPlayerId, BindingType.Summon);
        registry.RegisterBinding(BoundSummonId, LocalPlayerId, BindingType.Pet);

        Assert.Equal(1, duplicateWarnings);
        Assert.True(registry.TryGetValidBinding(BoundSummonId, out var rel));
        Assert.Equal(BindingType.Pet, rel.BindingType); // 新类型覆盖旧记录
    }

    [Fact]
    public void RegisterBinding_ZeroEntityId_Rejected()
    {
        var registry = new BindingRelationshipRegistry();
        registry.RegisterBinding(0, LocalPlayerId, BindingType.Summon);

        Assert.Equal(0, registry.TotalBindingCount);
    }

    [Fact]
    public void RegisterBinding_UndefinedType_Rejected()
    {
        var registry = new BindingRelationshipRegistry();
        registry.RegisterBinding(BoundSummonId, LocalPlayerId, (BindingType)99);

        Assert.Equal(0, registry.TotalBindingCount);
    }

    [Fact]
    public void InvalidateBinding_SetsInvalid_NotifiesCallback()
    {
        var registry = new BindingRelationshipRegistry();
        registry.RegisterBinding(BoundSummonId, LocalPlayerId, BindingType.Summon);

        (ulong Bound, BindingInvalidateReason Reason)? notified = null;
        registry.SetInvalidationCallback((bound, reason) => notified = (bound, reason));

        registry.InvalidateBinding(BoundSummonId, BindingInvalidateReason.Dismissed);

        Assert.False(registry.TryGetValidBinding(BoundSummonId, out _));
        Assert.Equal((BoundSummonId, BindingInvalidateReason.Dismissed), notified);
        Assert.NotEqual(default, registry.TotalBindingCount);
    }

    [Fact]
    public void InvalidateBinding_UnknownEntity_Noop()
    {
        var registry = new BindingRelationshipRegistry();
        int notified = 0;
        registry.SetInvalidationCallback((_, _) => notified++);

        registry.InvalidateBinding(99999, BindingInvalidateReason.Unbound);
        Assert.Equal(0, notified);
    }

    [Fact]
    public void GetValidBoundEntityIds_OnlyReturnsValid()
    {
        var registry = new BindingRelationshipRegistry();
        registry.RegisterBinding(BoundSummonId, LocalPlayerId, BindingType.Summon);
        registry.RegisterBinding(BoundPetId, LocalPlayerId, BindingType.Pet);

        Assert.Equal(2, registry.GetValidBoundEntityIds().Count);

        registry.InvalidateBinding(BoundSummonId, BindingInvalidateReason.Died);

        var remaining = registry.GetValidBoundEntityIds().ToArray();
        Assert.Single(remaining);
        Assert.Equal(BoundPetId, remaining[0]);
    }

    [Fact]
    public void Clear_RemovesAll()
    {
        var registry = new BindingRelationshipRegistry();
        registry.RegisterBinding(BoundSummonId, LocalPlayerId, BindingType.Summon);
        registry.Clear();

        Assert.Equal(0, registry.TotalBindingCount);
        Assert.Empty(registry.GetValidBoundEntityIds());
    }
}