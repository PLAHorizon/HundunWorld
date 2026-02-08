using MemoryPack;
using Orleans;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 游戏事件流命名空间常量
    /// </summary>
    public static class GameStreamNamespaces
    {
        /// <summary>角色事件（登录、登出、升级等）</summary>
        public const string CharacterEvents = "CharacterEvents";

        /// <summary>战斗事件（攻击、死亡、复活等）</summary>
        public const string CombatEvents = "CombatEvents";

        /// <summary>社交事件（好友、公会、组队等）</summary>
        public const string SocialEvents = "SocialEvents";

        /// <summary>系统事件（服务器状态、活动等）</summary>
        public const string SystemEvents = "SystemEvents";
    }

    /// <summary>
    /// 游戏事件类型枚举
    /// </summary>
    public enum GameEventType
    {
        // 角色事件
        CharacterLogin = 100,
        CharacterLogout = 101,
        CharacterLevelUp = 102,
        CharacterCreated = 103,

        // 战斗事件
        CombatDamageDealt = 200,
        CombatPlayerKill = 201,
        CombatPlayerDeath = 202,
        CombatPlayerResurrect = 203,
        CombatSkillCast = 204,

        // 社交事件
        GuildCreated = 300,
        GuildMemberJoined = 301,
        TeamCreated = 302,
        FriendAdded = 303,

        // 系统事件
        ServerStatusChanged = 400,
        ActivityStarted = 401,
        ActivityEnded = 402,
        DungeonCompleted = 403,
        QuestCompleted = 404
    }

    /// <summary>
    /// 游戏事件基类 — 通过Orleans Stream发布的事件消息
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class GameEvent
    {
        /// <summary>事件唯一ID</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string EventId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>事件类型</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public GameEventType EventType { get; set; }

        /// <summary>事件发生时间（UTC Ticks）</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long Timestamp { get; set; } = DateTime.UtcNow.Ticks;

        /// <summary>触发事件的角色ID（0表示系统事件）</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public ulong CharacterId { get; set; }

        /// <summary>事件描述</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public string Description { get; set; } = string.Empty;

        /// <summary>附加数据（JSON格式的扩展信息）</summary>
        [MemoryPackOrder(5)]
        [Id(5)]
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    /// <summary>
    /// 游戏事件发布器接口 — 用于向Orleans Stream发布游戏事件
    /// </summary>
    public interface IGameEventPublisher
    {
        /// <summary>发布角色事件</summary>
        Task PublishCharacterEventAsync(GameEvent gameEvent);

        /// <summary>发布战斗事件</summary>
        Task PublishCombatEventAsync(GameEvent gameEvent);

        /// <summary>发布社交事件</summary>
        Task PublishSocialEventAsync(GameEvent gameEvent);

        /// <summary>发布系统事件</summary>
        Task PublishSystemEventAsync(GameEvent gameEvent);
    }
}
