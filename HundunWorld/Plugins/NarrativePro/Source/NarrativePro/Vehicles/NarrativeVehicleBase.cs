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
    /// - Mass 托管逻辑（SetManagedByMass）改为由交通模拟层调用，见 Vehicles/VehicleTrafficSimulation.cs
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

        /// <summary>返回与此载具关联的 NarrativeCharacter。
        /// 对应 UE5 <c>Cast&lt;ANarrativeCharacter&gt;(GetController()-&gt;GetPawn())</c>：
        /// Flax 无 Controller 类，故从拥有者 Actor 上直接解析角色脚本（子类可覆写为「车上乘员」）。</summary>
        public virtual NarrativeCharacter GetNarrativeCharacter()
        {
            return VehicleController != null ? VehicleController.GetScript<NarrativeCharacter>() : null;
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

        /// <summary>获取 Actor 的 GUID。对应 UE5 GetActorGUID_Implementation：
        /// 由模拟层（UE5 Mass / Flax 交通模拟）托管的载具不参与存盘，返回空 GUID。</summary>
        public virtual Guid GetActorGUID()
        {
            // 对应 UE5「Mass 控制的载具不应存盘」的分支（ANarrativeVehicleBase::GetActorGUID 先查控制器/托管态）
            return bManagedByMass ? Guid.Empty : VehicleSaveGUID;
        }

        /// <summary>设置载具存盘 GUID。对应 UE5 SetVehicleSaveGuid。</summary>
        public virtual void SetVehicleSaveGuid(Guid newGUID)
        {
            VehicleSaveGUID = newGUID;
        }

        // ===== 拥有（PossessedBy 等价：Flax 无 Controller 类，用「拥有者 Actor 引用」表达同一概念）=====

        /// <summary>当前拥有此载具的 Actor（玩家角色/AI 控制器）。对应 UE5 的 Controller。</summary>
        [NonSerialized]
        protected Actor VehicleController;

        /// <summary>被控制器拥有时调用。对应 UE5 PossessedBy（子类补充移动组件语义）。</summary>
        public virtual void PossessedBy(Actor newController)
        {
            VehicleController = newController;
        }

        /// <summary>取消拥有时调用。对应 UE5 UnPossessed。</summary>
        public virtual void UnPossessed()
        {
            VehicleController = null;
        }

        /// <summary>当前拥有者。对应 UE5 GetController。</summary>
        public Actor GetVehicleController() => VehicleController;

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

        /// <summary>是否由群集/交通模拟层托管（对应 UE5 bManagedByMass）。</summary>
        private bool bManagedByMass;

        /// <summary>设置是否由模拟层（UE5 Mass / Flax 交通模拟）托管。对应 UE5 SetManagedByMass。</summary>
        public virtual void SetManagedByMass(bool bShouldManageByMass)
        {
            bManagedByMass = bShouldManageByMass;
            // UE5 在此分支里把 Actor 的自主寻路/导航注册让给 Mass；
            // Flax 侧等价的可见效果是：托管载具不再写存盘 GUID（车流不入存档，读档不会复活一堆车）。
            if (bShouldManageByMass)
            {
                VehicleSaveGUID = Guid.Empty;
            }
        }

        /// <summary>是否由模拟层托管。对应 UE5 bManagedByMass 读取。</summary>
        public bool IsManagedByMass() => bManagedByMass;

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
                    // 对应 UE5 DealVehicleDamage（NarrativeVehicleBase.cpp:433-455）：
                    // MakeOutgoingSpec(VehicleDamageEffect) + SetSetByCallerMagnitude(SetByCaller_Damage)
                    // + ApplyGameplayEffectSpecToTargetData（UE5 由 HitResult 构造 TargetData，
                    // Flax 侧对应 ApplyGameplayEffectSpecToTargetData 的目标 ASC 列表，见 GAS/NarrativeAbilitySystemComponent.cs:1604）。
                    Actor hitActor = hit.Collider; // Flax 中 RayCastHit.Collider 即命中脚本 Actor（同 GAS/NarrativeCombatAbility.cs:294 用法）
                    NarrativeAbilitySystemComponent targetASC = null;
                    for (Actor a = hitActor; a != null && targetASC == null; a = a.Parent)
                    {
                        targetASC = a.GetScript<NarrativeAbilitySystemComponent>();
                    }

                    GameplayEffect damageEffect = !string.IsNullOrEmpty(VehicleDamageEffectPath)
                        ? NarrativeGASAssetRegistry.ResolveEffect(VehicleDamageEffectPath)
                        : null;

                    if (damageEffect != null && targetASC != null)
                    {
                        var spec = new GameplayEffectSpec(damageEffect, driverNASC);
                        spec.SetSetByCallerMagnitude(NarrativePro.Core.NarrativeGameplayTags.SetByCaller_Damage, damageAmount);
                        driverNASC.ApplyGameplayEffectSpecToTargetData(
                            new GameplayEffectSpecHandle(spec),
                            new List<NarrativeAbilitySystemComponent> { targetASC });
                    }
                    else if (targetASC != null)
                    {
                        // 未配置载具伤害效果资产时回退为直接伤害。
                        // UE5 的回退同样打在命中目标身上，并以驾驶员为 Instigator、载具为 Causer
                        // （NarrativeVehicleBase.cpp DealVehicleDamage 用 HitResult 的 Actor 作目标），
                        // 这样被撞死的 NPC 才会把击杀记到司机头上（掉落/助攻/仇恨归因）。
                        targetASC.DealDamage(damageAmount, driver?.Actor ?? Actor, Actor);
                    }
                    else
                    {
                        // 目标没有 ASC（纯环境几何体）：不造成伤害，自伤由 OnVehicleMeshHit 负责。
                        NarrativeLog.LogWarning($"[Vehicle] 撞击 {hit.Collider?.Name} 未找到目标 ASC，载具伤害未应用。");
                    }
                }
            }
        }

        // ===== ASC 初始化 =====

        /// <summary>初始化载具 ASC。对应 UE5 InitializeVehicleASC（NarrativeVehicleBase.cpp:80-89）。</summary>
        protected virtual void InitializeVehicleASC()
        {
            if (AbilitySystemComponent != null)
            {
                // UE5 的 InitAbilityActorInfo(Owner, Avatar) 在 Flax 侧无对应概念：
                // NarrativeAbilitySystemComponent 的拥有者/化身即其挂载的 Actor
                // （GAS/NarrativeAbilitySystemComponent.cs:300 GetAvatarOwner 直接返回 Actor），无需额外初始化。
                // 订阅死亡事件，对应 UE5 HandleDeath 绑定的 OnDeath 动态委托（先减后加，OnEnable 重入幂等）。
                AbilitySystemComponent.OnDied -= HandleDeath;
                AbilitySystemComponent.OnDied += HandleDeath;

                InitializeAttributes();
                AddStartupEffects();
            }
        }

        /// <summary>初始化属性。对应 UE5 InitializeAttributes（NarrativeVehicleBase.cpp:96-126，
        /// MakeOutgoingSpec(DefaultAttributes, GetVehicleLevel()) + ApplyGameplayEffectSpecToTarget）。
        /// Flax 侧由 NarrativeAbilitySystemComponent.InitializeAttributes 统一实现属性效果、
        /// 启动效果与默认能力授予（GAS/NarrativeAbilitySystemComponent.cs:333，对应 UE5
        /// InitializeAttributes + InitStartUpEffects，内部 bStartupEffectsApplied 保证幂等），
        /// 载具只需把 AbilityConfiguration 的路径同步进 ASC。</summary>
        protected virtual void InitializeAttributes()
        {
            if (AbilitySystemComponent == null || AbilityConfiguration == null) return;

            if (string.IsNullOrEmpty(AbilityConfiguration.DefaultAttributesEffectPath))
            {
                // 对应 UE5 cpp:105-109 缺失 DefaultAttributes 时的 Error 日志分支
                NarrativeLog.LogError($"[NarrativeVehicleBase] {Actor?.Name} 缺少 DefaultAttributes 配置，请在载具定义中填充。");
                return;
            }

            AbilitySystemComponent.DefaultAttributesEffectPath = AbilityConfiguration.DefaultAttributesEffectPath;
            AbilitySystemComponent.StartupEffectPaths = AbilityConfiguration.StartupEffectPaths;
            // 默认能力经 ASC.GrantDefaultAbilities 幂等授予（UE5 cpp:91 AddDefaultAbilities 为空实现、
            // cpp:87 调用被注释，实际授予走属性初始化管线），此处同步配置路径列表。
            AbilitySystemComponent.DefaultAbilityPaths = AbilityConfiguration.DefaultAbilityPaths;

            AbilitySystemComponent.InitializeAttributes();
        }

        /// <summary>添加启动效果。对应 UE5 AddStartupEffects（NarrativeVehicleBase.cpp:128-150，
        /// 遍历 AbilityConfiguration->StartupEffects 逐个 MakeOutgoingSpec+Apply 并置 bStartupEffectsApplied）。
        /// Flax 侧启动效果已并入 ASC.InitializeAttributes 并在其中做幂等保护，
        /// 此处作为再入兜底：重复调用只会触发 ASC 内部的短路返回，不会二次应用。</summary>
        protected virtual void AddStartupEffects()
        {
            if (AbilitySystemComponent == null || AbilityConfiguration == null) return;
            AbilitySystemComponent.InitializeAttributes();
        }

        // ===== 死亡/碰撞回调（BlueprintNativeEvent 等价）=====

        /// <summary>处理载具死亡。对应 UE5 HandleDeath_Implementation（NarrativeVehicleBase.cpp:153-171）。
        /// 销毁所有附加的角色（调用其 Instakill）。</summary>
        protected virtual void HandleDeath(Actor killedActor, NarrativeAbilitySystemComponent killedActorASC)
        {
            // 对应 UE5 bIsDead 分支：仅在载具自身死亡时处决乘员；
            // Flax ASC 的 OnDied 也会因附加乘客的死亡回调进来，用 killedActor 甄别来源。
            if (killedActor == null || killedActor != Actor) return;

            // 对应 UE5 GetAttachedActors(AttachedActors, /*bIncludeAllDescendants=*/true)：
            // Flax 中附加 Actor 即子 Actor 层级，递归遍历全部后代。
            InstakillAttachedCharacters(Actor);
        }

        /// <summary>递归遍历附加（子）Actor，对其上的 NarrativeCharacter 的 ASC 调用 Instakill。
        /// 对应 UE5 HandleDeath_Implementation 内 Cast&lt;ANarrativeCharacter&gt; + Instakill 循环。</summary>
        private static void InstakillAttachedCharacters(Actor root)
        {
            foreach (var child in root.Children)
            {
                if (child == null) continue;
                NarrativeCharacter nchar = child.GetScript<NarrativeCharacter>();
                if (nchar != null)
                {
                    NarrativeAbilitySystemComponent nasc = nchar.GetNarrativeAbilitySystemComponent();
                    if (nasc != null)
                    {
                        nasc.Instakill();
                    }
                }
                InstakillAttachedCharacters(child);
            }
        }

        /// <summary>主网格撞击某物时调用。对应 UE5 OnVehicleMeshHit_Implementation（NarrativeVehicleBase.cpp:173-191）。
        /// 撞击静态物体时根据速度和曲线计算载具自伤。</summary>
        public virtual void OnVehicleMeshHit(Collider hitComponent, Actor otherActor, Collider otherComp, Vector3 normalImpulse, RayCastHit hit)
        {
            // UE5 以 RootComponent Mobility==Static 判定静态物；Flax 中撞击世界几何体时
            // 命中的 Actor 为空（Collider 脚本不在任何逻辑 Actor 上），以此对应同一分支。
            if (otherActor != null || AbilitySystemComponent == null) return;

            // 忽略垂直速度：落地不应伤车（对应 UE5 GetVelocity().Size2D 的注释语义）。
            float magnitude = normalImpulse.Length;
            if (magnitude <= 0f) return;

            // UE5 用 VehicleImpactObjectDamageCurve->GetFloatValue(NormalImpulse.Length()) 求伤。
            // Flax 1.12 无可运行时求值的标量曲线资产（FlaxEngine 仅有骨骼动画用的 AnimationCurve，
            // 无 UCurveFloat::GetFloatValue 对应物），故采用 UE5 cpp:183 注释保留的同源映射式：
            // GetMappedRangeValueClamped(VehicleImpactSelfDamage, (MaxHealth/5, MaxHealth), 撞击量级)。
            float rangeMin = VehicleImpactSelfDamage.X;
            float rangeMax = VehicleImpactSelfDamage.Y;
            if (rangeMax <= rangeMin) return;

            float t = Mathf.Clamp((magnitude - rangeMin) / (rangeMax - rangeMin), 0f, 1f);
            float maxHealth = GetMaxHealth();
            float desiredDamage = Mathf.Lerp(maxHealth * 0.2f, maxHealth, t);

            if (desiredDamage > 0f)
            {
                // UE5 的撞击自伤将驾驶员记为施害者、载具记为伤害源，
                // 载具因此被"撞死"时击杀归因不会退化成"载具自杀"（无归属 → 不掉不落、不结助攻）。
                var driverActor = GetNarrativeCharacter()?.Actor;
                AbilitySystemComponent.DealDamage(desiredDamage, driverActor ?? Actor, Actor);
            }
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
