using Arch.Core;

namespace HundunWorld.Game.ECS.Components
{
    /// <summary>
    /// 怪物组件
    /// 标识一个实体是怪物
    /// </summary>
    public struct MonsterComponent 
    {
        public ulong MonsterId;
        public string MonsterName;
        public int MonsterType; // 0-普通怪, 1-精英怪, 2-Boss
        public int Level;
        public bool IsAlive;
        
        public MonsterComponent(ulong monsterId, string monsterName, int monsterType = 0, int level = 1)
        {
            MonsterId = monsterId;
            MonsterName = monsterName;
            MonsterType = monsterType;
            Level = level;
            IsAlive = true;
        }
    }
}
