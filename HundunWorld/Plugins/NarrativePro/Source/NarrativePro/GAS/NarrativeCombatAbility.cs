using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Items;

namespace NarrativePro.GAS
{
    /// <summary>
    /// 战斗追踪数据。对应 UE5 FCombatTraceData。
    /// 描述攻击的检测范围（距离、半径、通道）。
    /// </summary>
    [Serializable]
    public class CombatTraceData
    {
        /// <summary>追踪距离。</summary>
        public float TraceDistance = 300f;

        /// <summary>追踪半径（>0 表示 SphereCast，=0 表示 RayCast）。</summary>
        public float TraceRadius = 100f;

        /// <summary>追踪通道名称（用于碰撞过滤）。</summary>
        public string TraceChannel = "Visibility";

        /// <summary>是否忽略自身。</summary>
        public bool bIgnoreSelf = true;

        public CombatTraceData() { }

        public CombatTraceData(float distance, float radius = 0f)
        {
            TraceDistance = distance;
            TraceRadius = radius;
        }
    }

    /// <summary>
    /// 命中结果。对应 UE5 FHitResult（简化版）。
    /// </summary>
    [Serializable]
    public class CombatHitResult
    {
        /// <summary>被命中的 Actor。</summary>
        public Actor HitActor;

        /// <summary>命中点世界坐标。</summary>
        public Vector3 ImpactPoint = Vector3.Zero;

        /// <summary>命中法线。</summary>
        public Vector3 ImpactNormal = Vector3.Up;

        /// <summary>追踪起点。</summary>
        public Vector3 TraceStart = Vector3.Zero;

        /// <summary>追踪终点。</summary>
        public Vector3 TraceEnd = Vector3.Zero;

        /// <summary>是否命中。</summary>
        public bool bBlockingHit = false;

        public CombatHitResult() { }
    }

    /// <summary>
    /// 战斗能力。对应 UE5 UNarrativeCombatAbility。
    /// 内建 hitscan/碰撞检测、伤害处理，被 melee 武器和 hitscan 武器共用。
    /// 简化点：
    /// - 移除 UE5 FGameplayAbilityTargetDataHandle，改为 CombatHitResult 列表
    /// - 移除网络复制，目标数据本地处理
    /// - 武器引用通过路径占位（GAS Phase 7 + Items Phase 1 已有 WeaponItem）
    /// </summary>
    public class NarrativeCombatAbility : NarrativeGameplayAbility
    {
        // ===== 配置字段 =====

        /// <summary>是否需要弹药（如果为 true，检查 Item 是否有 ammo class）。</summary>
        public bool bRequiresAmmo = false;

        /// <summary>默认攻击伤害值（能力激活时直接使用，可被 GetAttackDamage 覆盖）。</summary>
        public float DefaultAttackDamage = 10f;

        /// <summary>默认 Bot 攻击频率（次/秒，越大越频繁）。</summary>
        public float DefaultBotAttackFrequency = 1f;

        /// <summary>默认 Bot 攻击范围（cm）。</summary>
        public float DefaultBotAttackRange = 300f;

        /// <summary>是否主手武器（false 表示副手）。</summary>
        public bool bIsMainhand = true;

        /// <summary>武器散射角度（度，0 表示无散射）。</summary>
        public float WeaponSpread = 0f;

        // ===== 运行时状态 =====

        /// <summary>当前应用标签（每次 GenerateTargetData 时使用）。</summary>
        protected GameplayTag _currentApplicationTag = GameplayTag.None;

        /// <summary>当前追踪数据。</summary>
        protected CombatTraceData _currentTraceData;

        /// <summary>当前追踪起点。</summary>
        protected Vector3 _currentTraceStart = Vector3.Zero;

        // ===== 目标数据生成 =====

        /// <summary>使用追踪生成目标数据。命中时默认调用 HitTarget Attack Event。</summary>
        /// <param name="traceData">追踪数据。</param>
        /// <param name="traceStart">追踪起点。</param>
        /// <param name="applicationTag">应用标签（可空）。</param>
        public virtual void GenerateTargetDataUsingTrace(CombatTraceData traceData, Vector3 traceStart, GameplayTag applicationTag = default)
        {
            _currentTraceData = traceData;
            _currentTraceStart = traceStart;
            _currentApplicationTag = applicationTag.IsValid() ? applicationTag : GameplayTag.None;

            var hits = GetTargetDataUsingTrace(traceData, traceStart);
            if (hits != null && hits.Count > 0)
            {
                HitTarget(hits);
            }
        }

        /// <summary>使用追踪获取目标数据（不应用伤害）。</summary>
        public virtual List<CombatHitResult> GetTargetDataUsingTrace(CombatTraceData traceData, Vector3 traceStart)
        {
            var result = new List<CombatHitResult>();
            if (traceData == null) return result;

            // 计算追踪终点（向前方 TraceDistance）
            Vector3 forward = Actor.Direction;
            Vector3 traceEnd = traceStart + forward * traceData.TraceDistance;

            // 应用武器散射
            if (WeaponSpread > 0f)
            {
                traceEnd = ApplySpread(traceEnd, WeaponSpread);
            }

            if (traceData.TraceRadius > 0f)
            {
                // SphereCast
                var hits = PerformTraceMulti(traceStart, traceEnd, traceData.TraceRadius);
                if (hits != null) result.AddRange(hits);
            }
            else
            {
                // RayCast
                var hit = PerformTrace(traceStart, traceEnd, traceData.TraceRadius);
                if (hit != null) result.Add(hit);
            }

            return result;
        }

        /// <summary>最终化目标数据：应用伤害/效果到目标。</summary>
        public virtual void FinalizeTargetData(List<CombatHitResult> targetData, GameplayTag applicationTag)
        {
            if (targetData == null || targetData.Count == 0) return;
            HitTarget(targetData);
        }

        /// <summary>执行武器追踪（RayCast）。</summary>
        public virtual CombatHitResult PerformTrace(Vector3 start, Vector3 end, float sweepRadius)
        {
            var result = new CombatHitResult
            {
                TraceStart = start,
                TraceEnd = end
            };

            // Flax Physics.RayCast 返回 bool
            if (Physics.RayCast(start, end, out RayCastHit hit))
            {
                result.bBlockingHit = true;
                result.HitActor = hit.Collider;
                result.ImpactPoint = hit.Point;
                result.ImpactNormal = hit.Normal;
            }

            return result;
        }

        /// <summary>执行武器追踪（SphereCast 多命中）。
        /// 使用 Flax 原生 Physics.SphereCastAll API 进行球体扫描，获取路径上所有命中。
        /// </summary>
        public virtual List<CombatHitResult> PerformTraceMulti(Vector3 start, Vector3 end, float sweepRadius)
        {
            var result = new List<CombatHitResult>();
            var seenActors = new HashSet<Actor>();

            Vector3 dir = end - start;
            float dist = dir.Length;
            if (dist < 1f) return result;
            Vector3 forward = dir / dist;

            // 使用 Flax Physics.SphereCastAll 获取球体扫描的所有命中
            if (Physics.SphereCastAll(start, sweepRadius, forward, out RayCastHit[] hits, dist))
            {
                foreach (var hit in hits)
                {
                    var hitActor = hit.Collider;
                    if (hitActor != null && seenActors.Add(hitActor))
                    {
                        result.Add(new CombatHitResult
                        {
                            bBlockingHit = true,
                            HitActor = hitActor,
                            ImpactPoint = hit.Point,
                            ImpactNormal = hit.Normal,
                            TraceStart = start,
                            TraceEnd = end
                        });
                    }
                }
            }

            return result;
        }

        /// <summary>命中目标后处理（应用伤害）。</summary>
        public virtual void HitTarget(List<CombatHitResult> hits)
        {
            if (hits == null || hits.Count == 0) return;

            float attackDamage = GetAttackDamage();
            foreach (var hit in hits)
            {
                if (hit?.HitActor == null) continue;

                // 查找被命中 Actor 的 ASC
                var targetASC = hit.HitActor.GetScript<NarrativeAbilitySystemComponent>();
                if (targetASC == null) continue;

                // 创建伤害 GameplayEffect 并应用
                var damageEffect = CreateDamageEffect(attackDamage);
                var spec = new GameplayEffectSpec(damageEffect, OwningASC);
                targetASC.ApplyGameplayEffectSpecToSelf(new GameplayEffectSpecHandle(spec));
            }
        }

        /// <summary>创建伤害效果。</summary>
        protected virtual GameplayEffect CreateDamageEffect(float damageAmount)
        {
            var effect = new GameplayEffect("Damage")
            {
                DurationType = EGameplayEffectDurationType.Instant,
                ExecuteCalcTypeId = "Damage"
            };
            effect.Modifiers.Add(new GameplayModifierInfo
            {
                AttributeName = "Damage",
                ModifierOp = EGameplayModOp.Add,
                Magnitude = damageAmount
            });
            return effect;
        }

        // ===== 查询方法 =====

        /// <summary>获取攻击伤害值。</summary>
        public virtual float GetAttackDamage()
        {
            if (OwningASC?.AttributeSet != null)
            {
                // 使用属性集的 AttackDamage
                return OwningASC.AttributeSet.AttackDamage.CurrentValue;
            }
            return DefaultAttackDamage;
        }

        /// <summary>应用武器散射。</summary>
        public virtual Vector3 ApplyWeaponSpread(Vector3 viewpoint)
        {
            return ApplySpread(viewpoint, WeaponSpread);
        }

        /// <summary>应用固定散射到方向。</summary>
        public virtual Vector3 ApplySpread(Vector3 viewpoint, float spread)
        {
            if (spread <= 0f) return viewpoint;
            // 简化版：随机扰动方向
            // TODO [待源码]: 获取 UE5 源 NarrativeCombatAbility.cpp 的散射算法后补全更精确实现
            var rand = new System.Random();
            float angleX = (float)(rand.NextDouble() - 0.5) * spread * 2f * Mathf.DegreesToRadians;
            float angleY = (float)(rand.NextDouble() - 0.5) * spread * 2f * Mathf.DegreesToRadians;
            // 在 X-Y 平面随机扰动
            return viewpoint + new Vector3(angleX * 100f, angleY * 100f, 0f);
        }

        /// <summary>获取 Bot 攻击频率。</summary>
        public virtual float GetBotAttackFrequency() => DefaultBotAttackFrequency;

        /// <summary>获取 Bot 攻击范围。</summary>
        public virtual float GetBotAttackRange() => DefaultBotAttackRange;

        /// <summary>是否主手武器。</summary>
        public virtual bool IsMainhand() => bIsMainhand;

        // ===== 武器引用（占位） =====

        /// <summary>获取授予此能力的武器。返回 null 表示无关联武器（徒手能力）。</summary>
        public virtual NarrativePro.Items.WeaponItem GetAbilityWeapon()
        {
            // TODO [需接入物品/能力关联机制]: 通过 OwningASC 或 Item Component 关联
            return null;
        }

        /// <summary>获取武器的弹药对象（如果有的话）。</summary>
        public virtual NarrativePro.Items.NarrativeItem GetAbilityWeaponAmmo()
        {
            // TODO [需接入 WeaponItem ammo 关联机制]: 通过 WeaponItem 的 ammo slot 关联
            return null;
        }

        /// <summary>获取武器视觉 Actor（主手或副手）。</summary>
        public virtual FlaxEngine.Actor GetAbilityWeaponVisual()
        {
            // TODO [需接入 NarrativeCharacterVisual 武器视觉查询机制]: 通过 NarrativeCharacterVisual 查询
            return null;
        }
    }
}
