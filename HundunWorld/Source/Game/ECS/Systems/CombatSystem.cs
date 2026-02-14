using Arch.Core;
using Arch.Core.Utils;
using FlaxEngine;
using HundunWorld.Game.ECS.Components;
using Horizon.Game.Message.Network;
using Game.Character.Attributes;
using Horizon.Game.Message.Enums;

namespace HundunWorld.Game.ECS.Systems
{
    /// <summary>
    /// 战斗系统，处理战斗逻辑和伤害计算
    /// </summary>
    public class CombatSystem : BaseSystem
    {
        private QueryDescription _combatQuery;
        private QueryDescription _damageQuery;

        public override void Initialize(World world)
        {
            base.Initialize(world);
            
            // 查询具有战斗组件的实体
            _combatQuery = new QueryDescription().WithAll<CombatComponent, HealthComponent, PositionComponent>();
            
            // 查询具有伤害组件的实体
            _damageQuery = new QueryDescription().WithAll<DamageComponent, HealthComponent, PositionComponent>();
        }

        public override void Update(World world, float deltaTime)
        {
            // 处理伤害
            ProcessDamage(world, deltaTime);
            
            // 更新战斗状态
            UpdateCombatState(world, deltaTime);
        }

        /// <summary>
        /// 处理伤害
        /// </summary>
        private void ProcessDamage(World world, float deltaTime)
        {
            world.Query(in _damageQuery, (Entity entity, ref DamageComponent damage, ref HealthComponent health, ref PositionComponent position) =>
            {
                // 应用伤害
                float actualDamage = CalculateActualDamage(entity, damage, world);
                health.CurrentHealth -= actualDamage;

                // 触发伤害事件（可以用于特效、音效等）
                OnDamageDealt(entity, damage, actualDamage, position.Position);

                // 移除伤害组件（一次性）
                world.Remove<DamageComponent>(entity);
            });
        }

        /// <summary>
        /// 更新战斗状态
        /// </summary>
        private void UpdateCombatState(World world, float deltaTime)
        {
            world.Query(in _combatQuery, (Entity entity, ref CombatComponent combat, ref HealthComponent health, ref PositionComponent position) =>
            {
                // 更新连击重置
                if (combat.ComboCount > 0)
                {
                    float timeSinceLastAttack = Time.GameTime - combat.LastAttackTime;
                    if (timeSinceLastAttack > combat.ComboResetTime)
                    {
                        combat.ComboCount = 0;
                    }
                }

                // 检查是否脱离战斗
                if (combat.IsInCombat)
                {
                    float timeSinceLastAttack = Time.GameTime - combat.LastAttackTime;
                    if (timeSinceLastAttack > 5.0f) // 5秒未攻击则脱离战斗
                    {
                        combat.IsInCombat = false;
                    }
                }
            });
        }

        /// <summary>
        /// 计算实际伤害
        /// </summary>
        private float CalculateActualDamage(Entity target, DamageComponent damage, World world)
        {
            float actualDamage = damage.Amount;

            // 应用五行相克系数
            if (world.Has<WuxingComponent>(target))
            {
                var targetWuxing = world.Get<WuxingComponent>(target);
                float wuxingMultiplier = CalculateWuxingMultiplier(damage, targetWuxing);
                actualDamage *= wuxingMultiplier;
            }

            // 暴击伤害
            if (damage.IsCritical)
            {
                actualDamage *= 1.5f; // 暴击增伤50%
            }

            // 应用防御减免
            if (world.Has<HealthComponent>(target))
            {
                var health = world.Get<HealthComponent>(target);
                float damageReduction = CalculateDamageReduction(health.Defense);
                actualDamage *= (1.0f - damageReduction);
            }

            return actualDamage;
        }

        /// <summary>
        /// 计算五行相克系数
        /// 金克木、木克土、土克水、水克火、火克金
        /// </summary>
        private float CalculateWuxingMultiplier(DamageComponent damage, WuxingComponent target)
        {
            // 简化实现：根据五行相克关系返回伤害系数
            // 相克：1.25倍，相生：0.8倍，相同：1.0倍
            
            // 这里需要知道攻击方的元素属性
            // 暂时返回1.0，实际应该从伤害来源获取元素信息
            return 1.0f;
        }

        /// <summary>
        /// 计算防御减免
        /// </summary>
        private float CalculateDamageReduction(float defense)
        {
            // 使用公式：减免 = 防御 / (防御 + 100)
            return defense / (defense + 100f);
        }

        /// <summary>
        /// 伤害事件回调
        /// </summary>
        private void OnDamageDealt(Entity entity, DamageComponent damage, float actualDamage, Vector3 position)
        {
            // 这里可以触发特效、音效等
            // 例如：通知伤害数字系统显示伤害
            Debug.Log($"Entity {entity.Id} took {actualDamage} damage at {position}");
        }

        /// <summary>
        /// 应用伤害到实体
        /// </summary>
        public static void ApplyDamage(World world, Entity target, float amount, Horizon.Game.Message.Enums.DamageType type, Entity source, Vector3 hitPosition, bool isCritical = false)
        {
            if (!world.IsAlive(target))
                return;

            var damageComponent = new DamageComponent(amount, type, (ulong)source.Id, hitPosition, isCritical);
            
            if (world.Has<DamageComponent>(target))
            {
                // 如果已有伤害组件，累加伤害
                var existingDamage = world.Get<DamageComponent>(target);
                damageComponent.Amount += existingDamage.Amount;
            }

            world.Set(target, damageComponent);
        }
    }

    /// <summary>
    /// 五行系统，处理五行相关的逻辑
    /// </summary>
    public class WuxingSystem : BaseSystem
    {
        private QueryDescription _wuxingQuery;

        public override void Initialize(World world)
        {
            base.Initialize(world);
            _wuxingQuery = new QueryDescription().WithAll<WuxingComponent>();
        }

        public override void Update(World world, float deltaTime)
        {
            // 五行系统可以处理五行相关的特殊效果
            // 例如：五行共鸣、五行转换等
        }

        /// <summary>
        /// 计算五行相克伤害系数
        /// </summary>
        public static float GetElementalMultiplier(WuxingElement attacker, WuxingElement defender)
        {
            if (attacker == WuxingElement.None || defender == WuxingElement.None)
                return 1.0f;

            // 五行相克关系
            bool counters = (attacker == WuxingElement.Metal && defender == WuxingElement.Wood) ||
                           (attacker == WuxingElement.Wood && defender == WuxingElement.Earth) ||
                           (attacker == WuxingElement.Earth && defender == WuxingElement.Water) ||
                           (attacker == WuxingElement.Water && defender == WuxingElement.Fire) ||
                           (attacker == WuxingElement.Fire && defender == WuxingElement.Metal);

            // 五行相生关系
            bool generates = (attacker == WuxingElement.Metal && defender == WuxingElement.Water) ||
                            (attacker == WuxingElement.Water && defender == WuxingElement.Wood) ||
                            (attacker == WuxingElement.Wood && defender == WuxingElement.Fire) ||
                            (attacker == WuxingElement.Fire && defender == WuxingElement.Earth) ||
                            (attacker == WuxingElement.Earth && defender == WuxingElement.Metal);

            if (counters)
                return 1.25f; // 相克增伤25%
            else if (generates)
                return 0.8f;  // 相生减伤20%
            else if (attacker == defender)
                return 1.0f;  // 相同无加成
            else
                return 1.0f;  // 其他情况无加成
        }
    }

    /// <summary>
    /// 效果系统，处理Buff/Debuff
    /// </summary>
    public class EffectSystem : BaseSystem
    {
        private QueryDescription _effectQuery;

        public override void Initialize(World world)
        {
            base.Initialize(world);
            _effectQuery = new QueryDescription().WithAll<EffectComponent>();
        }

        public override void Update(World world, float deltaTime)
        {
            // 更新所有效果的持续时间
            world.Query(in _effectQuery, (Entity entity, ref EffectComponent effect) =>
            {
                effect.Duration -= deltaTime;

                // 应用效果（DoT/HoT）
                if (effect.Type == EffectType.DoT && world.Has<HealthComponent>(entity))
                {
                    var health = world.Get<HealthComponent>(entity);
                    health.CurrentHealth -= effect.Intensity * deltaTime;
                    world.Set(entity, health);
                }
                else if (effect.Type == EffectType.HoT && world.Has<HealthComponent>(entity))
                {
                    var health = world.Get<HealthComponent>(entity);
                    health.CurrentHealth = Mathf.Min(health.CurrentHealth + effect.Intensity * deltaTime, health.MaxHealth);
                    world.Set(entity, health);
                }

                // 移除过期效果
                if (effect.Duration <= 0)
                {
                    world.Remove<EffectComponent>(entity);
                }
            });
        }

        /// <summary>
        /// 应用效果到实体
        /// </summary>
        public static void ApplyEffect(World world, Entity target, int effectId, EffectType type, float duration, float intensity, int stacks = 1)
        {
            if (!world.IsAlive(target))
                return;

            var effectComponent = new EffectComponent(effectId, type, duration, intensity, stacks);
            world.Set(target, effectComponent);
        }
    }
}
