using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 角色属性实体
    /// </summary>
    [Table("Game_HunduShijie_CharacterAttribute"), TableDescription(Name = "Game_HunduShijie_CharacterAttribute", Order = "HunduShijie_004", Description = "角色属性信息")]
    [Comment("角色属性表")]
    [EntityStorage("Game")]
    public class CharacterAttributeEntity : BaseGameModel<long>
    {
        /// <summary>
        /// 自增ID
        /// </summary>
        [Key]
        [Column("id", TypeName = "bigint", Order = 1), TableDescription(TypeName = "bigint", Name = "id", Order = "1", Description = "自增ID")]
        [Comment("自增ID")]
        public new long Id { get; set; }
        
        /// <summary>
        /// 角色ID
        /// </summary>
        [Column("character_id")]
        public ulong CharacterId { get; set; }
        
        /// <summary>
        /// 根骨
        /// </summary>
        [Column("constitution")]
        public int Constitution { get; set; }
        
        /// <summary>
        /// 悟性
        /// </summary>
        [Column("comprehension")]
        public int Comprehension { get; set; }
        
        /// <summary>
        /// 身法
        /// </summary>
        [Column("agility")]
        public int Agility { get; set; }
        
        /// <summary>
        /// 臂力
        /// </summary>
        [Column("strength")]
        public int Strength { get; set; }
        
        /// <summary>
        /// 内劲
        /// </summary>
        [Column("internal_force")]
        public int InternalForce { get; set; }
        
        /// <summary>
        /// 当前生命值
        /// </summary>
        [Column("current_health")]
        public int CurrentHealth { get; set; }
        
        /// <summary>
        /// 最大生命值
        /// </summary>
        [Column("max_health")]
        public int MaxHealth { get; set; }
        
        /// <summary>
        /// 当前内力值
        /// </summary>
        [Column("current_internal_energy")]
        public int CurrentInternalEnergy { get; set; }
        
        /// <summary>
        /// 最大内力值
        /// </summary>
        [Column("max_internal_energy")]
        public int MaxInternalEnergy { get; set; }
        
        /// <summary>
        /// 攻击力
        /// </summary>
        [Column("attack_power")]
        public int AttackPower { get; set; }
        
        /// <summary>
        /// 防御力
        /// </summary>
        [Column("defense")]
        public int Defense { get; set; }
        
        /// <summary>
        /// 命中率
        /// </summary>
        [Column("hit_rate")]
        public float HitRate { get; set; }
        
        /// <summary>
        /// 闪避率
        /// </summary>
        [Column("dodge_rate")]
        public float DodgeRate { get; set; }
        
        /// <summary>
        /// 暴击率
        /// </summary>
        [Column("critical_rate")]
        public float CriticalRate { get; set; }
        
        /// <summary>
        /// 暴击伤害
        /// </summary>
        [Column("critical_damage")]
        public float CriticalDamage { get; set; }
        
        /// <summary>
        /// 移动速度
        /// </summary>
        [Column("move_speed")]
        public float MoveSpeed { get; set; }
        
        /// <summary>
        /// 攻击速度
        /// </summary>
        [Column("attack_speed")]
        public float AttackSpeed { get; set; }
        
        /// <summary>
        /// 金攻击
        /// </summary>
        [Column("metal_attack")]
        public int MetalAttack { get; set; }
        
        /// <summary>
        /// 木攻击
        /// </summary>
        [Column("wood_attack")]
        public int WoodAttack { get; set; }
        
        /// <summary>
        /// 水攻击
        /// </summary>
        [Column("water_attack")]
        public int WaterAttack { get; set; }
        
        /// <summary>
        /// 火攻击
        /// </summary>
        [Column("fire_attack")]
        public int FireAttack { get; set; }
        
        /// <summary>
        /// 土攻击
        /// </summary>
        [Column("earth_attack")]
        public int EarthAttack { get; set; }
        
        /// <summary>
        /// 金抗性
        /// </summary>
        [Column("metal_resistance")]
        public int MetalResistance { get; set; }
        
        /// <summary>
        /// 木抗性
        /// </summary>
        [Column("wood_resistance")]
        public int WoodResistance { get; set; }
        
        /// <summary>
        /// 水抗性
        /// </summary>
        [Column("water_resistance")]
        public int WaterResistance { get; set; }
        
        /// <summary>
        /// 火抗性
        /// </summary>
        [Column("fire_resistance")]
        public int FireResistance { get; set; }
        
        /// <summary>
        /// 土抗性
        /// </summary>
        [Column("earth_resistance")]
        public int EarthResistance { get; set; }
        
        /// <summary>
        /// 内功攻击
        /// </summary>
        [Column("internal_attack")]
        public int InternalAttack { get; set; }
        
        /// <summary>
        /// 外功攻击
        /// </summary>
        [Column("external_attack")]
        public int ExternalAttack { get; set; }
        
        /// <summary>
        /// 内功防御
        /// </summary>
        [Column("internal_defense")]
        public int InternalDefense { get; set; }
        
        /// <summary>
        /// 外功防御
        /// </summary>
        [Column("external_defense")]
        public int ExternalDefense { get; set; }
        
        /// <summary>
        /// 格挡率
        /// </summary>
        [Column("block_rate")]
        public float BlockRate { get; set; }
        
        /// <summary>
        /// 韧性
        /// </summary>
        [Column("tenacity")]
        public int Tenacity { get; set; }
        
        /// <summary>
        /// 伤害减免
        /// </summary>
        [Column("damage_reduction")]
        public float DamageReduction { get; set; }
        
        /// <summary>
        /// 反弹伤害
        /// </summary>
        [Column("reflect_damage")]
        public float ReflectDamage { get; set; }
        
        /// <summary>
        /// 护体真气
        /// </summary>
        [Column("qi_shield")]
        public int QiShield { get; set; }
        
        /// <summary>
        /// 生命回复
        /// </summary>
        [Column("health_regeneration")]
        public int HealthRegeneration { get; set; }
        
        /// <summary>
        /// 内力回复
        /// </summary>
        [Column("energy_regeneration")]
        public int EnergyRegeneration { get; set; }
        
        /// <summary>
        /// 更新时间
        /// </summary>
        [Column("update_time")]
        public DateTime UpdateTime { get; set; }
    }
}
