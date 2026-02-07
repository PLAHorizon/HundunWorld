using System;
using FlaxEngine;
using Game.Character.Attributes;

namespace HundunWorld.Game.Character
{
    /// <summary>
    /// 角色属性结构体
    /// </summary>
    public struct CharacterStats
    {
        /// <summary>
        /// 角色名称
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// 等级
        /// </summary>
        public int Level { get; set; }
        
        /// <summary>
        /// 物理攻击力
        /// </summary>
        public float Attack { get; set; }
        
        /// <summary>
        /// 法术攻击力
        /// </summary>
        public float MagicAttack { get; set; }
        
        /// <summary>
        /// 物理防御力
        /// </summary>
        public float Defense { get; set; }
        
        /// <summary>
        /// 法术防御力
        /// </summary>
        public float MagicDefense { get; set; }
        
        /// <summary>
        /// 最大生命值
        /// </summary>
        public float MaxHealth { get; set; }
        
        /// <summary>
        /// 当前生命值
        /// </summary>
        public float CurrentHealth { get; set; }
        
        /// <summary>
        /// 移动速度
        /// </summary>
        public float MoveSpeed { get; set; }
        
        /// <summary>
        /// 攻击速度
        /// </summary>
        public float AttackSpeed { get; set; }
        
        /// <summary>
        /// 暴击率 (0-1)
        /// </summary>
        public float CriticalRate { get; set; }
        
        /// <summary>
        /// 暴击伤害倍数
        /// </summary>
        public float CriticalDamage { get; set; }
        
        /// <summary>
        /// 五行元素属性
        /// </summary>
        public WuxingElement Element { get; set; }
    }
}