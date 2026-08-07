using Horizon.Game.ECS.Arch.SyncGuard.Contracts;

namespace Horizon.Game.ECS.Arch.Components;

/// <summary>
/// 深度绑定实体标记组件：仅唤物/宠物等与本地角色深度绑定的自主行为实体携带。
/// 与 <see cref="NetworkIdentityComponent"/>（IsLocalPlayer=false）正交共存，
/// 供 <c>SendAuthorizationSystem</c> 一条查询定位绑定实体集合。
/// </summary>
public struct BoundEntityTagComponent
{
    /// <summary>绑定实体（唤物/宠物）自身 ID，等于实体 NetworkIdentityComponent.EntityId。</summary>
    public ulong BoundEntityId;

    /// <summary>归属主人（本地角色）实体 ID。</summary>
    public ulong OwnerEntityId;

    /// <summary>绑定类型（唤物/宠物）。</summary>
    public BindingType BindingType;
}