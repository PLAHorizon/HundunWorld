namespace HundunWorld.Game.ECS.Components
{
    /// <summary>
    /// 网络实体ID组件
    /// 将ECS实体与网络层的实体ID关联起来
    /// </summary>
    public struct NetworkEntityIdComponent
    {
        /// <summary>
        /// 网络实体ID（服务端分配的唯一标识）
        /// </summary>
        public ulong NetworkId;

        /// <summary>
        /// 实体所有者类型
        /// </summary>
        public NetworkEntityType EntityType;

        public NetworkEntityIdComponent(ulong networkId, NetworkEntityType entityType = NetworkEntityType.Unknown)
        {
            NetworkId = networkId;
            EntityType = entityType;
        }

        public override string ToString()
        {
            return $"NetworkEntity({NetworkId}, {EntityType})";
        }
    }

    /// <summary>
    /// 网络实体类型
    /// </summary>
    public enum NetworkEntityType
    {
        Unknown = 0,
        LocalPlayer = 1,
        RemotePlayer = 2,
        Npc = 3,
        Monster = 4,
        Projectile = 5,
        Item = 6
    }
}
