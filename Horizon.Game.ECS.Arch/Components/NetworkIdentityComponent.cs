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

    /// <summary>角色昵称（随 Identity 于 Spawn 下发；NPC 为空）。供头顶名牌显示远程玩家名称。</summary>
    public string Nickname;

    /// <summary>角色称号名（随 Identity 于 Spawn 下发；空 = 未佩戴）。供头顶名牌显示称号。</summary>
    public string Title;
}
