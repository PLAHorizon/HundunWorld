namespace Horizon.Game.ECS.Arch.Components;

/// <summary>
/// 网络实体标识组件：区分本地玩家与远程实体。
/// </summary>
public struct NetworkIdentityComponent
{
    /// <summary>服务器分配的实体唯一 ID。</summary>
    public ulong EntityId;

    /// <summary>是否为本地玩家所控制的实体。</summary>
    public bool IsLocalPlayer;
}
