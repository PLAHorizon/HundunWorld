using Arch.Core;

namespace HundunWorld.Game.ECS.Components
{
    /// <summary>
    /// NPC组件
    /// 标识一个实体是非玩家角色
    /// </summary>
    public struct NpcComponent 
    {
        public ulong NpcId;
        public string NpcName;
        public int NpcType; // 0-普通NPC, 1-任务NPC, 2-Boss等
        
        public NpcComponent(ulong npcId, string npcName, int npcType = 0)
        {
            NpcId = npcId;
            NpcName = npcName;
            NpcType = npcType;
        }
    }
}
