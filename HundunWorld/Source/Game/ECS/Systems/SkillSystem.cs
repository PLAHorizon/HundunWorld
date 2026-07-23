using Arch.Core;
using Arch.Core.Utils;
using FlaxEngine;
using HundunWorld.Game.ECS.Components;
using HundunWorld.Game.Combat.Skills;
using HundunWorld.Game.Audio;
using Horizon.Game.Message.Network;
using Game.Character.Attributes;
using System;
using System.Collections.Generic;
using Horizon.Game.Message.Enums;

namespace HundunWorld.Game.ECS.Systems
{
    /// <summary>
    /// 技能系统，处理技能的施放、冷却和效果。
    /// 使用数据驱动的 SkillDatabase 替代硬编码技能。
    /// </summary>
    public class SkillSystem : BaseSystem
    {
        private QueryDescription _skillQuery;
        private QueryDescription _castingQuery;
        private Random _random;
        private NetworkEntityRegistry _entityRegistry;

        public SkillSystem()
        {
            _random = new Random();
        }

        /// <summary>
        /// 设置网络实体注册表引用
        /// </summary>
        public void SetEntityRegistry(NetworkEntityRegistry registry)
        {
            _entityRegistry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public override void Initialize(World world)
        {
            base.Initialize(world);
            
            // 查询具有技能组件的实体
            _skillQuery = new QueryDescription().WithAll<SkillComponent, HealthComponent>();
            
            // 查询正在施法的实体
            _castingQuery = new QueryDescription().WithAll<SkillCastingComponent, PositionComponent>();
            
            // 初始化数据驱动技能数据库
            SkillDatabase.Initialize();
        }

        public override void Update(World world, float deltaTime)
        {
            // 更新技能冷却
            UpdateSkillCooldowns(world, deltaTime);
            
            // 更新施法进度
            UpdateSkillCasting(world, deltaTime);
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

            // 从数据驱动数据库获取技能配置
            var skillConfig = SkillDatabase.GetSkill(skillId);
            if (skillConfig == null)
            {
                Debug.LogWarning($"Skill {skillId} not found in SkillDatabase");
                return false;
            }

            // 检查能量
            if (world.Has<HealthComponent>(caster))
            {
                var health = world.Get<HealthComponent>(caster);
                if (health.Energy < skillConfig.EnergyCost)
                {
                    Debug.LogWarning("Not enough energy to cast skill");
                    return false;
                }

                // 消耗能量
                health.Energy -= skillConfig.EnergyCost;
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
            StartSkillCast(world, caster, skillConfig, target, targetPosition);
            return true;
        }

        /// <summary>
        /// 开始施法
        /// </summary>
        private void StartSkillCast(World world, Entity caster, SkillConfig skillConfig, Entity target, Vector3 targetPosition)
        {
            var targetId = target.Id;
            
            var castingComponent = new SkillCastingComponent(
                skillConfig.SkillId,
                skillConfig.CastTime,
                (ulong)targetId,
                targetPosition,
                skillConfig.CanMoveWhileCasting
            );

            world.Set(caster, castingComponent);

            // 触发施法开始事件（播放动画、特效、音效）
            OnSkillCastStart(world, caster, skillConfig);
        }

        /// <summary>
        /// 完成施法
        /// </summary>
        private void CompleteSkillCast(World world, Entity caster, SkillCastingComponent casting, PositionComponent position)
        {
            var skillConfig = SkillDatabase.GetSkill(casting.SkillId);
            if (skillConfig == null) return;

            // 执行技能效果
            ExecuteSkillEffect(world, caster, skillConfig, casting.TargetEntityId, casting.TargetPosition);

            // 设置冷却
            if (world.Has<SkillComponent>(caster))
            {
                var skill = world.Get<SkillComponent>(caster);
                skill.CurrentCooldown = skillConfig.Cooldown;
                world.Set(caster, skill);
            }

            // 触发施法完成事件
            OnSkillCastComplete(world, caster, skillConfig);
        }

        /// <summary>
        /// 执行技能效果
        /// </summary>
        private void ExecuteSkillEffect(World world, Entity caster, SkillConfig skillConfig, ulong targetId, Vector3 targetPosition)
        {
            var skillType = skillConfig.GetSkillType();
            switch (skillType)
            {
                case SkillType.ActiveAttack:
                    ExecuteAttackSkill(world, caster, skillConfig, targetId, targetPosition);
                    break;
                case SkillType.Control:
                    ExecuteControlSkill(world, caster, skillConfig, targetId);
                    break;
                case SkillType.Dash:
                    ExecuteDashSkill(world, caster, skillConfig, targetPosition);
                    break;
                case SkillType.Support:
                    ExecuteSupportSkill(world, caster, skillConfig, targetId);
                    break;
                case SkillType.Ultimate:
                    ExecuteUltimateSkill(world, caster, skillConfig, targetId, targetPosition);
                    break;
            }

            // 应用附带效果（Buff/Debuff/DoT等）
            ApplySkillEffects(world, caster, skillConfig, targetId);
        }

        /// <summary>
        /// 执行攻击技能
        /// </summary>
        private void ExecuteAttackSkill(World world, Entity caster, SkillConfig skillConfig, ulong targetId, Vector3 targetPosition)
        {
            float damage = CalculateSkillDamage(world, caster, skillConfig);
            bool isCritical = _random.NextSingle() < skillConfig.CritRate;
            if (isCritical) damage *= skillConfig.CritMultiplier;

            if (_entityRegistry != null && _entityRegistry.TryGetEntity(targetId, out var targetEntity))
            {
                if (world.IsAlive(targetEntity))
                {
                    CombatSystem.ApplyDamage(world, targetEntity, damage,
                        DamageType.Magic, caster, targetPosition, isCritical);

                    // 播放命中音效
                    if (!string.IsNullOrEmpty(skillConfig.HitSound))
                    {
                        GameAudioManager.Instance.Play3D(skillConfig.HitSound, targetPosition, GameAudioCategory.Skill);
                    }
                }
            }
        }

        /// <summary>
        /// 执行控制技能
        /// </summary>
        private void ExecuteControlSkill(World world, Entity caster, SkillConfig skillConfig, ulong targetId)
        {
            if (_entityRegistry != null && _entityRegistry.TryGetEntity(targetId, out var targetEntity))
            {
                if (world.IsAlive(targetEntity))
                {
                    float duration = 3.0f;
                    if (skillConfig.Effects != null && skillConfig.Effects.Count > 0)
                    {
                        var controlEffect = skillConfig.Effects.Find(e => e.EffectType == "Stun" || e.EffectType == "Slow");
                        if (controlEffect != null) duration = controlEffect.Duration;
                    }
                    EffectSystem.ApplyEffect(world, targetEntity, skillConfig.SkillId, EffectType.Control, duration, 1.0f);
                }
            }
        }

        /// <summary>
        /// 执行位移技能
        /// </summary>
        private void ExecuteDashSkill(World world, Entity caster, SkillConfig skillConfig, Vector3 targetPosition)
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
        private void ExecuteSupportSkill(World world, Entity caster, SkillConfig skillConfig, ulong targetId)
        {
            if (_entityRegistry != null && _entityRegistry.TryGetEntity(targetId, out var targetEntity))
            {
                if (world.IsAlive(targetEntity))
                {
                    float healValue = skillConfig.DamageMultiplier * 10f;
                    if (skillConfig.Effects != null)
                    {
                        var hotEffect = skillConfig.Effects.Find(e => e.EffectType == "HoT");
                        if (hotEffect != null) healValue = hotEffect.Value;
                    }
                    EffectSystem.ApplyEffect(world, targetEntity, skillConfig.SkillId, EffectType.HoT, 5.0f, healValue);
                }
            }
        }

        /// <summary>
        /// 执行终结技
        /// </summary>
        private void ExecuteUltimateSkill(World world, Entity caster, SkillConfig skillConfig, ulong targetId, Vector3 targetPosition)
        {
            float damage = CalculateSkillDamage(world, caster, skillConfig) * 2.0f;
            bool isCritical = _random.NextSingle() < skillConfig.CritRate;
            if (isCritical) damage *= skillConfig.CritMultiplier;

            if (_entityRegistry != null && _entityRegistry.TryGetEntity(targetId, out var targetEntity))
            {
                if (world.IsAlive(targetEntity))
                {
                    CombatSystem.ApplyDamage(world, targetEntity, damage,
                        DamageType.Magic, caster, targetPosition, isCritical);
                }
            }
        }

        /// <summary>
        /// 应用技能附带效果（Buff/Debuff/DoT/控制等）
        /// </summary>
        private void ApplySkillEffects(World world, Entity caster, SkillConfig skillConfig, ulong targetId)
        {
            if (skillConfig.Effects == null || skillConfig.Effects.Count == 0) return;
            if (_entityRegistry == null || !_entityRegistry.TryGetEntity(targetId, out var targetEntity)) return;
            if (!world.IsAlive(targetEntity)) return;

            foreach (var effect in skillConfig.Effects)
            {
                // 概率触发
                if (_random.NextSingle() > effect.Chance) continue;

                var effectType = effect.EffectType switch
                {
                    "Burn" => EffectType.DoT,
                    "DoT" => EffectType.DoT,
                    "Slow" => EffectType.Control,
                    "Stun" => EffectType.Control,
                    "HoT" => EffectType.HoT,
                    "Shield" => EffectType.Buff,
                    "Invincible" => EffectType.Buff,
                    "Cleanse" => EffectType.Buff,
                    _ => EffectType.Buff
                };

                EffectSystem.ApplyEffect(world, targetEntity, skillConfig.SkillId, effectType, effect.Duration, effect.Value);
            }
        }

        /// <summary>
        /// 计算技能伤害（基于数据配置 + 五行加成 + 等级缩放）
        /// </summary>
        private float CalculateSkillDamage(World world, Entity caster, SkillConfig skillConfig)
        {
            float baseDamage = 100f;

            if (world.Has<HealthComponent>(caster))
            {
                var health = world.Get<HealthComponent>(caster);
                baseDamage = health.Attack;
            }

            // 等级缩放的伤害倍率
            int skillLevel = 1;
            if (world.Has<SkillComponent>(caster))
            {
                skillLevel = world.Get<SkillComponent>(caster).Level;
            }
            float damageMultiplier = skillConfig.GetDamageMultiplierAtLevel(skillLevel);
            float damage = baseDamage * damageMultiplier;

            // 应用五行亲和度加成
            if (world.Has<WuxingComponent>(caster))
            {
                var wuxing = world.Get<WuxingComponent>(caster);
                int affinity = wuxing.GetAffinity(skillConfig.GetWuxingElement());
                float affinityBonus = 1.0f + (affinity / 10) * 0.005f;
                damage *= affinityBonus;
            }

            return damage;
        }

        /// <summary>
        /// 施法开始事件（播放动画、音效、特效）
        /// </summary>
        private void OnSkillCastStart(World world, Entity caster, SkillConfig skillConfig)
        {
            Debug.Log($"[SkillSystem] 开始施放: {skillConfig.SkillName}");

            // 播放施法音效
            if (!string.IsNullOrEmpty(skillConfig.CastSound))
            {
                Vector3 pos = Vector3.Zero;
                if (world.Has<PositionComponent>(caster))
                    pos = world.Get<PositionComponent>(caster).Position;
                GameAudioManager.Instance.Play3D(skillConfig.CastSound, pos, GameAudioCategory.Skill);
            }
        }

        /// <summary>
        /// 施法完成事件
        /// </summary>
        private void OnSkillCastComplete(World world, Entity caster, SkillConfig skillConfig)
        {
            Debug.Log($"[SkillSystem] 施放完成: {skillConfig.SkillName}");
        }


    }
}
