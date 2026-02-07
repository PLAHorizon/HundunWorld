using Arch.Core;

namespace HundunWorld.Game.ECS.Components
{
    /// <summary>
    /// 玩家组件
    /// 标识一个实体是玩家角色
    /// </summary>
    public struct PlayerComponent 
    {
        public ulong PlayerId;
        public string PlayerName;
        public int Level;
        
        public PlayerComponent(ulong playerId, string playerName, int level = 1)
        {
            PlayerId = playerId;
            PlayerName = playerName;
            Level = level;
        }
    }
}
