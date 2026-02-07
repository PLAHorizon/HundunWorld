using System;

namespace HundunWorld.Game.Combat
{
    /// <summary>
    /// 伤害类型枚举（战斗系统专用）
    /// </summary>
    public enum CombatDamageType
    {
        Physical = 0,    // 物理伤害
        Magical = 1,     // 法术伤害
        True = 2,        // 真实伤害
        Poison = 3,      // 毒素伤害
        Fire = 4,        // 火焰伤害
        Ice = 5,         // 冰霜伤害
        Lightning = 6,   // 雷电伤害
        Holy = 7,        // 神圣伤害
        Shadow = 8,      // 暗影伤害
        Nature = 9       // 自然伤害
    }
}