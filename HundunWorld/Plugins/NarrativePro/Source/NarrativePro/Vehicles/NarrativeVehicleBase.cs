using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.GAS;
using NarrativePro.Items;
using NarrativePro.UnrealFramework;

namespace NarrativePro.Vehicles
{
    /// <summary>
    /// 种子设置事件委托。对应 UE5 FSeedSet。
    /// </summary>
    /// <param name="newSeed">新的随机种子。</param>
    public delegate void SeedSetDelegate(int newSeed);

    /// <summary>
    /// Narrative 载具基类。对应 UE5 ANarrativeVehicleBase（NarrativeVehicleBase.h/.cpp）。
    /// UE5 中继承 APawn 并实现 INarrativeCharacterOwner、IAbilitySystemInterface、IGameplayTagAssetInterface、
    /// INarrativeTeamAgentInterface、INarrativeImpactInterface、INarrativeSavableActor 等多个接口；
    /// Flax 中 Actor 为 sealed，改为 Script 挂载到 Actor 上。
    /// 载具拥有 ASC 以处理伤害、效果、死亡等。
    /// 简化点：
    /// - 移除 UE5 复制/RPC（OnRep_Xxx、Server_、Client_），改为本地逻辑 + 事件回调
    /// - 移除 IAbilitySystemInterface/IGameplayTagAssetInterface（直接实现方法）
    /// - INarrativeSavableActor 简化为字段 + 方法
    /// - APawn 的 PossessedBy/UnPossessed/GetController 简化为占位（Flax 无 Controller 概念）
    /// - USkeletalMeshComponent → FlaxEngine.AnimatedModel
    /// - UNavModifierComponent → 占位字段（Flax 无导航系统对应）
    /// - UCurveFloat → string 路径占位
    /// - TSubclassOf&lt;UGameplayEffect&gt; → string 路径占位
    /// - FGameplayTag → GameplayTag，FGameplayTagContainer → GameplayTagContainer
    /// - Mass 相关逻辑（SetManagedByMass）用 TODO 占位
    /// </summary>
    public abstract class NarrativeVehicleBase : Script, INarrativeCharacterOwner, INarrativeImpactInterface, INarrativeTeamAgentInterface
    {
        /// <summary>载具网格组件名称。对应 UE5 VehicleMeshComponentName。</summary>
        public const string VehicleMeshComponentName = "VehicleMesh";

        /// <summary>主骨骼网格组件。对应 UE5 Mesh（USkeletalMeshComponent）。
        /// Flax 中用 AnimatedModel 简化对应（作为子 Actor）。</summary>
        [NonSerialized]
        protected AnimatedModel Mesh;

        /// <summary>隐藏的略放大版载具网格，用于生成重叠和载具伤害。对应 UE5 ImpactMesh。</summary>
        [NonSerialized]
        protected AnimatedModel ImpactMesh;

        /// <summary>载具导航修改器。对应 UE5 VehicleNavModifier（UNavModifierComponent）。
        /// Flax 无导航系统对应，占位字段。</summary>
        [NonSerialized]
        protected object VehicleNavModifier;

        /// <summary>能力系统组件，支持生命、死亡等。对应 UE5 AbilitySystemComponent。</summary>
        [NonSerialized]
        protected NarrativeAbilitySystemComponent AbilitySystemComponent;

        /// <summary>属性集基类。对应 UE5 AttributeSetBase。</summary>
        [NonSerialized]
        protected NarrativeAttributeSetBase AttributeSetBase;

        /// <summary>包含默认授予的能力、属性等。对应 UE5 AbilityConfiguration。</summary>
        public AbilityConfiguration AbilityConfiguration;

        /// <summary>载具撞击角色时应用的伤害效果路径。对应 UE5 VehicleDamageEffect（TSubclassOf&lt;UGameplayEffect&gt;）。</summary>
        public string VehicleDamageEffectPath = "";

        /// <summary>载具撞击自身伤害映射范围（速度 → 伤害）。
        /// 将速度映射到 0.2-1.0 倍最大生命值伤害。对应 UE5 VehicleImpactSelfDamage。</summary>
        public Float2 VehicleImpactSelfDamage = new Float2(200f, 600f);

        /// <summary>用于将载具存盘。为零则不保存。对应 UE5 VehicleSaveGUID。</summary>
        public Guid VehicleSaveGUID = Guid.Empty;

        /// <summary>载具随机种子，生成一次并同步。可用于任何此角色需要的随机化。对应 UE5 VehicleRandomSeed。</summary>
        public int VehicleRandomSeed = 0;

        /// <summary>撞击角色伤害映射范围（速度 → 伤害）。
        /// 将速度映射到 0.2-1.0 倍最大生命值伤害。对应 UE5 VehicleImpactCharacterDamage。</summary>
        public Float2 VehicleImpactCharacterDamage = new Float2(200f, 600f);

        /// <summary>根据撞击法线大小读取此曲线得到伤害值。对应 UE5 VehicleImpactObjectDamageCurve（UCurveFloat）。
        /// Flax 无 UCurveFloat 对应，用路径占位。</summary>
        public string VehicleImpactObjectDamageCurvePath = "";

        /// <summary>种子设置事件。对应 UE5 OnSeedSet（FSeedSet）。</summary>
        public event SeedSetDelegate OnSeedSet;

        // ===== 阵营（INarrativeTeamAgentInterface 本地实现）=====

        private GameplayTagContainer _factions = new GameplayTagContainer();

        /// <summary>构造函数默认值初始化。对应 UE5 ANarrativeVehicleBase 构造函数。</summary>
        protected NarrativeVehicleBase()
        {
            VehicleImpactSelfDamage = new Float2(200f, 600f);
            VehicleImpactCharacterDamage = new Float2(200f, 600f);
            VehicleSaveGUID = Guid.Empty;
        }

        // ===== 生命周期 =====

        public override void OnEnable()
        {
            base.OnEnable();

            // 查找网格组件（Flax 中 AnimatedModel 是子 Actor）
            if (Actor != null)
            {
                foreach (var child in Actor.Children)
                {
                    if (child is AnimatedModel am)
                    {
                        if (Mesh == null)
                        {
                            Mesh = am;
                        }
                        else if (ImpactMesh == null)
                        {
                            ImpactMesh = am;
                            break;
                        }
                    }
                }
            }

            // 查找 ASC 与属性集
            AbilitySystemComponent = Actor.GetScript<NarrativeAbilitySystemComponent>();
            AttributeSetBase = Actor.GetScript<NarrativeAttributeSetBase>();

            if (VehicleRandomSeed < 0)
            {
                SetRandomSeed(new System.Random().Next());
            }

            InitializeVehicleASC();
        }

        public override void OnDisable()
        {
            // 对应 UE5 Destroyed：销毁所有附加的子 Actor
            if (Actor != null)
            {
                // Flax-已实现: 销毁父 Actor 时会自动销毁子 Actor，无需手动遍历销毁
            }
            base.OnDisable();
        }

        // ===== 接口实现 =====

        /// <summary>返回与此载具关联的 NarrativeCharacter（通过拥有控制器）。对应 UE5 GetNarrativeCharacter。</summary>
        public virtual NarrativeCharacter GetNarrativeCharacter()
        {
            // Flax 无 Controller 概念，简化为返回 null；子类可重写
            return null;
        }

        /// <summary>返回能力系统组件。对应 UE5 GetAbilitySystemComponent（IAbilitySystemInterface）。</summary>
        public virtual NarrativeAbilitySystemComponent GetAbilitySystemComponent() => AbilitySystemComponent;

        /// <summary>处理载具撞击此载具的事件。对应 UE5 HandleVehicleImpact_Implementation（INarrativeImpactInterface）。</summary>
        public virtual void HandleVehicleImpact(NarrativeVehicleBase vehicle, Collider overlappedComponent, Collider otherComp, int otherBodyIndex, bool bFromSweep, RayCastHit sweepResult)
        {
            // 默认不处理；子类可重写
        }

        /// <summary>处理爆炸冲击的事件。对应 UE5 HandleExplosionImpact_Implementation（INarrativeImpactInterface）。</summary>
        public virtual void HandleExplosionImpact(NarrativeAbilitySystemComponent explosionCauser, Vector3 explosionLocation, float intendedDamage)
        {
            // 默认不处理；子类可重写
        }

        // ===== 标签查询（IGameplayTagAssetInterface 等价）=====

        /// <summary>获取拥有的所有 GameplayTag。对应 UE5 GetOwnedGameplayTags。</summary>
        public virtual void GetOwnedGameplayTags(GameplayTagContainer tagContainer)
        {
            AbilitySystemComponent?.GetOwnedGameplayTags(tagContainer);
        }

        /// <summary>是否拥有指定标签。对应 UE5 HasMatchingGameplayTag。</summary>
        public virtual bool HasMatchingGameplayTag(GameplayTag tagToCheck)
        {
            return AbilitySystemComponent != null && AbilitySystemComponent.HasMatchingGameplayTag(tagToCheck);
        }

        /// <summary>是否拥有所有指定标签。对应 UE5 HasAllMatchingGameplayTags。</summary>
        public virtual bool HasAllMatchingGameplayTags(GameplayTagContainer tagsToCheck)
        {
            return AbilitySystemComponent != null && AbilitySystemComponent.HasAllMatchingGameplayTags(tagsToCheck);
        }

        /// <summary>是否拥有任意指定标签。对应 UE5 HasAnyMatchingGameplayTags。</summary>
        public virtual bool HasAnyMatchingGameplayTags(GameplayTagContainer tagsToCheck)
        {
            return AbilitySystemComponent != null && AbilitySystemComponent.HasAnyMatchingGameplayTags(tagsToCheck);
        }

        // ===== 队伍/阵营（INarrativeTeamAgentInterface）=====

        /// <summary>获取朝向目标 Actor 的态度。对应 UE5 GetTeamAttitudeTowards。</summary>
        public virtual ArsenalStatics.ETeamAttitude GetTeamAttitudeTowards(Actor other)
        {
            // 简化：委托给 ArsenalStatics
            return ArsenalStatics.GetAttitude(Actor, other);
        }

        /// <summary>添加阵营。对应 UE5 AddFaction。</summary>
        public virtual void AddFaction(GameplayTag faction)
        {
            _factions.AddTag(faction);
        }

        /// <summary>移除阵营。对应 UE5 RemoveFaction。</summary>
        public virtual void RemoveFaction(GameplayTag faction)
        {
            _factions.RemoveTag(faction);
        }

        /// <summary>返回此代理所在的阵营。对应 UE5 GetFactions。</summary>
        public virtual GameplayTagContainer GetFactions() => _factions;

        // ===== 存档（INarrativeSavableActor 等价）=====

        /// <summary>获取 Actor 的 GUID。对应 UE5 GetActorGUID_Implementation。
        /// Mass 控制的载具不应存盘；当前无控制器时返回 VehicleSaveGUID。</summary>
        public virtual Guid GetActorGUID()
        {
            // Flax-不兼容: UE5 的 Mass/Controller 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass/Controller 概念，简化为始终返回 VehicleSaveGUID
            return VehicleSaveGUID;
        }

        /// <summary>设置载具存盘 GUID。对应 UE5 SetVehicleSaveGuid。</summary>
        public virtual void SetVehicleSaveGuid(Guid newGUID)
        {
            VehicleSaveGUID = newGUID;
        }

        // ===== 拥有（PossessedBy 等价，Flax 无 Controller 概念，简化为占位）=====

        /// <summary>被控制器拥有时调用。对应 UE5 PossessedBy。Flax 无 Controller 概念，简化为占位。</summary>
        public virtual void PossessedBy(Actor newController)
        {
            // Flax-不兼容: UE5 的 Controller 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Controller 概念，启用 ImpactMesh 碰撞等逻辑
        }

        /// <summary>取消拥有时调用。对应 UE5 UnPossessed。Flax 无 Controller 概念，简化为占位。</summary>
        public virtual void UnPossessed()
        {
            // Flax-不兼容: UE5 的 Controller 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Controller 概念，禁用 ImpactMesh 碰撞
        }

        // ===== 生命/属性查询 =====

        /// <summary>获取当前生命值。对应 UE5 GetHealth。</summary>
        public virtual float GetHealth()
        {
            return AttributeSetBase != null ? AttributeSetBase.Health.CurrentValue : 0f;
        }

        /// <summary>获取最大生命值。对应 UE5 GetMaxHealth。</summary>
        public virtual float GetMaxHealth()
        {
            return AttributeSetBase != null ? AttributeSetBase.MaxHealth.CurrentValue : 0f;
        }

        /// <summary>获取载具等级。对应 UE5 GetVehicleLevel。</summary>
        public virtual int GetVehicleLevel()
        {
            return 1;
        }

        /// <summary>设置载具随机种子。对应 UE5 SetRandomSeed。</summary>
        public virtual void SetRandomSeed(int newSeed)
        {
            VehicleRandomSeed = newSeed;
            OnSeedSet?.Invoke(VehicleRandomSeed);
        }

        // ===== Mass 管理 =====

        /// <summary>设置是否由 Mass 管理。对应 UE5 SetManagedByMass。
        /// Flax-不兼容: UE5 的 Mass Entity System 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass Entity System，需自定义实现。</summary>
        public virtual void SetManagedByMass(bool bManagedByMass)
        {
            // Flax-不兼容: UE5 的 Mass Entity System 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass Entity System，需自定义实现
            NarrativeLog.LogWarning("[NarrativeVehicleBase] SetManagedByMass: Flax 无 Mass Entity System，需自定义实现");
        }

        /// <summary>对某 Actor 造成载具伤害。对应 UE5 DealVehicleDamage。
        /// 伤害通过驾驶员的 ASC 路由，使伤害数字/击杀任务等正确处理。</summary>
        /// <param name="damageASC">伤害源 ASC。</param>
        /// <param name="damageAmount">伤害量。</param>
        /// <param name="hit">命中结果。</param>
        public virtual void DealVehicleDamage(NarrativeAbilitySystemComponent damageASC, float damageAmount, RayCastHit hit)
        {
            NarrativeCharacter driver = GetNarrativeCharacter();
            if (driver != null)
            {
                NarrativeAbilitySystemComponent driverNASC = driver.GetNarrativeAbilitySystemComponent();
                if (driverNASC != null)
                {
                    // TODO [需接入 ASC 系统]: 应用 VehicleDamageEffect 并设置 SetByCaller_Damage 标签的幅度
                    driverNASC.DealDamage(damageAmount);
                }
            }
        }

        // ===== ASC 初始化 =====

        /// <summary>初始化载具 ASC。对应 UE5 InitializeVehicleASC。</summary>
        protected virtual void InitializeVehicleASC()
        {
            if (AbilitySystemComponent != null)
            {
                // TODO [需接入 ASC 系统]: InitAbilityActorInfo（Flax ASC 简化）
                InitializeAttributes();
                AddStartupEffects();
            }
        }

        /// <summary>添加默认能力。对应 UE5 AddDefaultAbilities。</summary>
        protected virtual void AddDefaultAbilities()
        {
            // TODO [需接入 ASC 系统]: 通过 ASC 授予默认能力
        }

        /// <summary>初始化属性。对应 UE5 InitializeAttributes。</summary>
        protected virtual void InitializeAttributes()
        {
            if (AbilitySystemComponent == null || AbilityConfiguration == null) return;
            // TODO [需接入 ASC 系统]: 应用默认属性效果（AbilityConfiguration.DefaultAttributesEffectPath）
        }

        /// <summary>添加启动效果。对应 UE5 AddStartupEffects。</summary>
        protected virtual void AddStartupEffects()
        {
            if (AbilitySystemComponent == null || AbilityConfiguration == null) return;
            // TODO [需接入 ASC 系统]: 应用启动效果列表（AbilityConfiguration.StartupEffectPaths）
        }

        // ===== 死亡/碰撞回调（BlueprintNativeEvent 等价）=====

        /// <summary>处理载具死亡。对应 UE5 HandleDeath_Implementation。
        /// 销毁所有附加的角色（调用其 Instakill）。</summary>
        protected virtual void HandleDeath(Actor killedActor, NarrativeAbilitySystemComponent killedActorASC)
        {
            // TODO [需接入 ASC 系统]: 遍历附加 Actor，对 NarrativeCharacter 调用 Instakill
        }

        /// <summary>主网格撞击某物时调用。对应 UE5 OnVehicleMeshHit_Implementation。
        /// 撞击静态物体时根据速度和曲线计算载具自伤。</summary>
        public virtual void OnVehicleMeshHit(Collider hitComponent, Actor otherActor, Collider otherComp, Vector3 normalImpulse, RayCastHit hit)
        {
            // TODO [需接入 ASC 系统]: 根据 VehicleImpactObjectDamageCurve 和速度计算自伤
        }

        /// <summary>碰撞网格重叠某物时调用。对应 UE5 OnCollisionMeshOverlap_Implementation。
        /// 此网格不会阻止载具移动。</summary>
        public virtual void OnCollisionMeshOverlap(Collider overlappedComponent, Actor otherActor, Collider otherComp, int otherBodyIndex, bool bFromSweep, RayCastHit sweepResult)
        {
            if (otherActor == null) return;
            // 查找实现 INarrativeImpactInterface 的 Script 并触发其 HandleVehicleImpact
            INarrativeImpactInterface impactImpl = FindImpactInterface(otherActor);
            impactImpl?.HandleVehicleImpact(this, overlappedComponent, otherComp, otherBodyIndex, bFromSweep, sweepResult);
        }

        /// <summary>在 Actor 上查找实现 INarrativeImpactInterface 的 Script。</summary>
        protected static INarrativeImpactInterface FindImpactInterface(Actor actor)
        {
            if (actor == null) return null;
            foreach (var s in actor.Scripts)
            {
                if (s is INarrativeImpactInterface impl) return impl;
            }
            return null;
        }

        // ===== 网格访问 =====

        /// <summary>返回主网格。对应 UE5 GetMesh。</summary>
        public virtual AnimatedModel GetMesh() => Mesh;

        /// <summary>返回重叠网格。对应 UE5 GetOverlapMesh。</summary>
        public virtual AnimatedModel GetOverlapMesh() => ImpactMesh;
    }
}
