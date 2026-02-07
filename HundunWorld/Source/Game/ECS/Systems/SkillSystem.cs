using Arch.Core;
using Arch.Core.Utils;
using FlaxEngine;
using HundunWorld.Game.ECS.Components;
using Game.Character.Attributes;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.ECS.Systems
{
    /// <summary>
    /// 技能系统，处理技能的施放、冷却和效果
    /// </summary>
    public class SkillSystem : BaseSystem
    {
        private QueryDescription _skillQuery;
        private QueryDescription _castingQuery;
        private Dictionary<int, SkillData> _skillDatabase;
        private Random _random; // 添加随机数生成器

        public SkillSystem()
        {
            _skillDatabase = new Dictionary<int, SkillData>();
            _random = new Random(); // 初始化随机数生成器
        }

        public override void Initialize(World world)
        {
            base.Initialize(world);
            
            // 查询具有技能组件的实体
            _skillQuery = new QueryDescription().WithAll<SkillComponent, HealthComponent>();
            
            // 查询正在施法的实体
            _castingQuery = new QueryDescription().WithAll<SkillCastingComponent, PositionComponent>();
            
            // 初始化技能数据库
            InitializeSkillDatabase();
        }

        public override void Update(World world, float deltaTime)
        {
            // 更新技能冷却
            UpdateSkillCooldowns(world, deltaTime);
            
            // 更新施法进度
            UpdateSkillCasting(world, deltaTime);
        }

        /// <summary>
        /// 初始化技能数据库
        /// </summary>
        private void InitializeSkillDatabase()
        {
            // 示例技能数据
            _skillDatabase[1] = new SkillData 
            { 
                SkillId = 1, 
                SkillName = "火球术",
                Element = WuxingElement.Fire,
                Type = SkillType.ActiveAttack,
                DamageMultiplier = 1.5f,
                EnergyCost = 30f,
                Cooldown = 3f,
                Range = 15f,
                CastTime = 0.8f
            };

            _skillDatabase[2] = new SkillData 
            { 
                SkillId = 2, 
                SkillName = "冰霜箭",
                Element = WuxingElement.Water,
                Type = SkillType.ActiveAttack,
                DamageMultiplier = 1.2f,
                EnergyCost = 25f,
                Cooldown = 2.5f,
                Range = 20f,
                CastTime = 0.6f
            };
        }

        /// <summary>
        /// 更新技能冷却
        /// </summary>
        private void UpdateSkillCooldowns(World world, float deltaTime)
        {
            world.Query(in _skillQuery, (Entity entity, ref SkillComponent skill) =>
            {
                if (skill.CurrentCooldown > 0)
                {
                    skill.CurrentCooldown = Mathf.Max(0, skill.CurrentCooldown - deltaTime);
                }
            });
        }

        /// <summary>
        /// 更新施法进度
        /// </summary>
        private void UpdateSkillCasting(World world, float deltaTime)
        {
            world.Query(in _castingQuery, (Entity entity, ref SkillCastingComponent casting, ref PositionComponent position) =>
            {
                casting.CastProgress += deltaTime;

                // 施法完成
                if (casting.CastProgress >= casting.TotalCastTime)
                {
                    CompleteSkillCast(world, entity, casting, position);
                    world.Remove<SkillCastingComponent>(entity);
                }
            });
        }

        /// <summary>
        /// 尝试施放技能
        /// </summary>
        public bool TryCastSkill(World world, Entity caster, int skillId, Entity target, Vector3 targetPosition)
        {
            if (!world.IsAlive(caster))
                return false;

            // 检查是否已在施法
            if (world.Has<SkillCastingComponent>(caster))
            {
                Debug.LogWarning("Already casting a skill");
                return false;
            }

            // 获取技能数据
            if (!_skillDatabase.TryGetValue(skillId, out var skillData))
            {
                Debug.LogWarning($"Skill {skillId} not found in database");
                return false;
            }

            // 检查能量
            if (world.Has<HealthComponent>(caster))
            {
                var health = world.Get<HealthComponent>(caster);
                if (health.Energy < skillData.EnergyCost)
                {
                    Debug.LogWarning("Not enough energy to cast skill");
                    return false;
                }

                // 消耗能量
                health.Energy -= skillData.EnergyCost;
                world.Set(caster, health);
            }

            // 检查冷却
            if (world.Has<SkillComponent>(caster))
            {
                var skill = world.Get<SkillComponent>(caster);
                if (skill.SkillId == skillId && skill.CurrentCooldown > 0)
                {
                    Debug.LogWarning($"Skill {skillId} is on cooldown");
                    return false;
                }
            }

            // 开始施法
            StartSkillCast(world, caster, skillData, target, targetPosition);
            return true;
        }

        /// <summary>
        /// 开始施法
        /// </summary>
        private void StartSkillCast(World world, Entity caster, SkillData skillData, Entity target, Vector3 targetPosition)
        {
            // 注意：这里我们假设target实体是有效的，并且我们存储它的ID
            // 在实际应用中，可能需要更复杂的实体引用管理机制
            var targetId = target.Id; // 使用Entity的Id属性
            
            var castingComponent = new SkillCastingComponent(
                skillData.SkillId,
                skillData.CastTime,
                (ulong)targetId,
                targetPosition,
                false
            );

            world.Set(caster, castingComponent);

            // 触发施法开始事件（播放动画、特效等）
            OnSkillCastStart(world, caster, skillData);
        }

        /// <summary>
        /// 完成施法
        /// </summary>
        private void CompleteSkillCast(World world, Entity caster, SkillCastingComponent casting, PositionComponent position)
        {
            if (!_skillDatabase.TryGetValue(casting.SkillId, out var skillData))
                return;

            // 执行技能效果
            ExecuteSkillEffect(world, caster, skillData, casting.TargetEntityId, casting.TargetPosition);

            // 设置冷却
            if (world.Has<SkillComponent>(caster))
            {
                var skill = world.Get<SkillComponent>(caster);
                skill.CurrentCooldown = skill.Cooldown;
                world.Set(caster, skill);
            }

            // 触发施法完成事件
            OnSkillCastComplete(world, caster, skillData);
        }

        /// <summary>
        /// 执行技能效果
        /// </summary>
        private void ExecuteSkillEffect(World world, Entity caster, SkillData skillData, ulong targetId, Vector3 targetPosition)
        {
            // 根据技能类型执行不同的效果
            switch (skillData.Type)
            {
                case SkillType.ActiveAttack:
                    ExecuteAttackSkill(world, caster, skillData, targetId, targetPosition);
                    break;

                case SkillType.Control:
                    ExecuteControlSkill(world, caster, skillData, targetId);
                    break;

                case SkillType.Dash:
                    ExecuteDashSkill(world, caster, skillData, targetPosition);
                    break;

                case SkillType.Support:
                    ExecuteSupportSkill(world, caster, skillData, targetId);
                    break;

                case SkillType.Ultimate:
                    ExecuteUltimateSkill(world, caster, skillData, targetId, targetPosition);
                    break;
            }
        }

        /// <summary>
        /// 执行攻击技能
        /// </summary>
        private void ExecuteAttackSkill(World world, Entity caster, SkillData skillData, ulong targetId, Vector3 targetPosition)
        {
            // 计算伤害
            float damage = CalculateSkillDamage(world, caster, skillData);

            // 判断是否暴击
            bool isCritical = _random.NextSingle() < 0.2f; // 20%暴击率

            // 注意：这里我们假设targetId是有效的实体ID
            // 在实际应用中，可能需要通过世界管理器或其他机制来获取实体
            // 由于Arch ECS没有直接通过ID获取实体的方法，我们在这里记录一个TODO
            // TODO: 实现通过targetId获取实体的机制，可能需要使用WorldManager或其他实体引用管理机制
            Debug.LogWarning("注意：需要实现通过targetId获取实体的机制");
        }

        /// <summary>
        /// 执行控制技能
        /// </summary>
        private void ExecuteControlSkill(World world, Entity caster, SkillData skillData, ulong targetId)
        {
            // 注意：这里我们假设targetId是有效的实体ID
            // 在实际应用中，可能需要通过世界管理器或其他机制来获取实体
            Debug.LogWarning("注意：需要实现通过targetId获取实体的机制");
        }

        /// <summary>
        /// 执行位移技能
        /// </summary>
        private void ExecuteDashSkill(World world, Entity caster, SkillData skillData, Vector3 targetPosition)
        {
            if (world.Has<PositionComponent>(caster))
            {
                var position = world.Get<PositionComponent>(caster);
                position.Position = targetPosition;
                world.Set(caster, position);
            }
        }

        /// <summary>
        /// 执行辅助技能
        /// </summary>
        private void ExecuteSupportSkill(World world, Entity caster, SkillData skillData, ulong targetId)
        {
            // 注意：这里我们假设targetId是有效的实体ID
            // 在实际应用中，可能需要通过世界管理器或其他机制来获取实体
            Debug.LogWarning("注意：需要实现通过targetId获取实体的机制");
        }

        /// <summary>
        /// 执行终结技
        /// </summary>
        private void ExecuteUltimateSkill(World world, Entity caster, SkillData skillData, ulong targetId, Vector3 targetPosition)
        {
            // 终结技通常有更强大的效果
            float damage = CalculateSkillDamage(world, caster, skillData) * 2.0f;
            
            // 注意：这里我们假设targetId是有效的实体ID
            // 在实际应用中，可能需要通过世界管理器或其他机制来获取实体
            Debug.LogWarning("注意：需要实现通过targetId获取实体的机制");
        }

        /// <summary>
        /// 计算技能伤害
        /// </summary>
        private float CalculateSkillDamage(World world, Entity caster, SkillData skillData)
        {
            float baseDamage = 100f; // 基础伤害

            if (world.Has<HealthComponent>(caster))
            {
                var health = world.Get<HealthComponent>(caster);
                baseDamage = health.Attack;
            }

            float damage = baseDamage * skillData.DamageMultiplier;

            // 应用五行亲和度加成
            if (world.Has<WuxingComponent>(caster))
            {
                var wuxing = world.Get<WuxingComponent>(caster);
                int affinity = wuxing.GetAffinity(skillData.Element);
                float affinityBonus = 1.0f + (affinity / 10) * 0.005f;
                damage *= affinityBonus;
            }

            return damage;
        }

        /// <summary>
        /// 施法开始事件
        /// </summary>
        private void OnSkillCastStart(World world, Entity caster, SkillData skillData)
        {
            Debug.Log($"Started casting {skillData.SkillName}");
            
            // 这里可以触发动画、特效等
            // 例如：播放施法动画、显示施法特效
        }

        /// <summary>
        /// 施法完成事件
        /// </summary>
        private void OnSkillCastComplete(World world, Entity caster, SkillData skillData)
        {
            Debug.Log($"Completed casting {skillData.SkillName}");
            
            // 这里可以触发完成动画、特效等
        }

        /// <summary>
        /// 技能数据类
        /// </summary>
        public class SkillData
        {
            public int SkillId { get; set; }
            public string SkillName { get; set; }
            public WuxingElement Element { get; set; }
            public SkillType Type { get; set; }
            public float DamageMultiplier { get; set; }
            public float EnergyCost { get; set; }
            public float Cooldown { get; set; }
            public float Range { get; set; }
            public float CastTime { get; set; }
        }
    }
}
