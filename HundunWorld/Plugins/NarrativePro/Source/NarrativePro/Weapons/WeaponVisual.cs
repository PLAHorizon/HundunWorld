using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;
using NarrativePro.UnrealFramework;
using WeaponVisualEntry = NarrativePro.Character.WeaponVisualEntry;

namespace NarrativePro.Weapons
{
    /// <summary>
    /// 武器视觉挂载状态。移植自 UE5 FWeaponVisualAttachState。
    /// UE5 用结构体一次性复制所有挂载所需信息；Flax 无网络复制，
    /// 仍保留该结构，因为 AttachState 是"信息齐了才允许挂载"的门槛。
    /// </summary>
    [Serializable]
    public class WeaponVisualAttachState
    {
        /// <summary>武器视觉收起（装备）进的槽位。</summary>
        public GameplayTag EquippedSlot = GameplayTag.None;

        /// <summary>武器视觉持握进的槽位。</summary>
        public GameplayTag WieldedSlot = GameplayTag.None;

        /// <summary>创建本视觉的武器物品。</summary>
        public WeaponItem WeaponOwner;

        /// <summary>创建本视觉的角色。</summary>
        public NarrativeCharacter CharOwner;

        /// <summary>创建本视觉的角色视觉组件。</summary>
        public NarrativePro.Character.NarrativeCharacterVisual VisualOwner;

        /// <summary>信息是否足以完成挂载。对应 UE5 FWeaponVisualAttachState::CanAttach。</summary>
        public bool CanAttach()
        {
            return WeaponOwner != null && VisualOwner != null && EquippedSlot.IsValid();
        }
    }

    /// <summary>
    /// 武器视觉 Actor。移植自 UE5 NarrativeArsenal: Weapons/WeaponVisual.h + .cpp（AWeaponVisual : AActor）。
    /// 负责武器网格、动画层、配件网格、碰撞检测与命中扫描的可视化与逻辑承载。
    ///
    /// 简化点：
    /// - AActor → Flax Script 挂载到武器视觉 Actor。
    /// - 移除 UE5 复制（OnRep_* 改为本地方法），保留调用时序。
    /// - USkeletalMeshComponent → FlaxEngine.AnimatedModel；UStaticMeshComponent → StaticModel；UStaticMesh → FlaxEngine.Model。
    /// - UPrimitiveComponent（无直接对应）→ Collider Actor（Flax 的碰撞图元宿主）。
    /// - TSubclassOf&lt;UAnimInstance&gt; → string 路径占位。
    /// - FGameplayTag → NarrativePro.Items.GameplayTag；FHitResult → FlaxEngine.RayCastHit。
    /// - UE5 从 AnimSequence 离线采样骨骼姿态（FBoneContainer/CompactPose）；Flax 无该能力，
    ///   改为在扫描时按帧从 AnimatedModel.GetNodeTransformation 抓取实时骨骼姿态增量采样
    ///   （PerformCollisionCheck 注释原文即为"对尚未计算过的当前与历史动画数据做扫描"），
    ///   低帧率下的连续性扫描语义与 UE5 一致。
    /// - FCollisionShape 仅支持胶囊（UE5 CacheCollisionData 注释同样只走 SphylElems 分支）。
    /// </summary>
    public class WeaponVisual : Script
    {
        /// <summary>武器骨骼网格组件（对应 UE5 WeaponMesh）</summary>
        public AnimatedModel WeaponMesh { get; set; }

        /// <summary>本地空间武器骨骼网格组件（对应 UE5 LocalWeaponMesh，第一人称）</summary>
        public AnimatedModel LocalWeaponMesh { get; set; }

        /// <summary>默认武器动画层（TSubclassOf&lt;UAnimInstance&gt; → string 路径）</summary>
        public string DefaultWeaponAnimLayer { get; set; } = "";

        /// <summary>双持武器动画层</summary>
        public string DualWieldWeaponAnimLayer { get; set; } = "";

        /// <summary>第一人称武器动画层</summary>
        public string Weapon1PAnimLayer { get; set; } = "";

        /// <summary>第一人称双持武器动画层</summary>
        public string DualWieldWeapon1PAnimLayer { get; set; } = "";

        /// <summary>角色形态特定的动画层（形态标签 → 动画层路径）</summary>
        public Dictionary<GameplayTag, string> FormSpecificLayers { get; set; } = new Dictionary<GameplayTag, string>();

        /// <summary>本次攻击已命中的 Actor 缓存（对应 UE5 CachedHitActors）</summary>
        public List<Actor> CachedHitActors { get; set; } = new List<Actor>();

        /// <summary>配件槽位 → 配件网格组件（UStaticMeshComponent → StaticModel）</summary>
        public Dictionary<GameplayTag, StaticModel> AttachmentMeshComps { get; set; } = new Dictionary<GameplayTag, StaticModel>();

        /// <summary>本地空间配件网格组件映射</summary>
        public Dictionary<GameplayTag, StaticModel> LocalAttachmentMeshComps { get; set; } = new Dictionary<GameplayTag, StaticModel>();

        /// <summary>配件槽位 → 默认静态网格资源（UStaticMesh → FlaxEngine.Model）</summary>
        public Dictionary<GameplayTag, Model> AttachmentMeshDefaultMeshes { get; set; } = new Dictionary<GameplayTag, Model>();

        /// <summary>当前动画通知事件引用（对应 UE5 CurrentNotifyEvent）</summary>
        public AnimNotifyEventReference CurrentNotifyEvent { get; set; }

        /// <summary>武器碰撞数据列表（对应 UE5 CollisionData）</summary>
        public List<WeaponCollisionData> CollisionData { get; set; } = new List<WeaponCollisionData>();

        /// <summary>拥有此武器视觉的角色（对应 UE5 CharacterOwner）</summary>
        public NarrativeCharacter CharacterOwner { get; set; }

        /// <summary>拥有此视觉的武器物品（对应 UE5 WeaponOwner，原为复制属性）</summary>
        public WeaponItem WeaponOwner { get; set; }

        /// <summary>武器视觉挂载状态（对应 UE5 AttachState）</summary>
        public WeaponVisualAttachState AttachState = new WeaponVisualAttachState();

        /// <summary>创建本视觉的角色视觉组件（对应 UE5 VisualOwner）</summary>
        public NarrativePro.Character.NarrativeCharacterVisual VisualOwner { get; set; }

        /// <summary>是否已成功挂载（对应 UE5 bAttachedSuccesfully）</summary>
        public bool bAttachedSuccessfully = false;

        /// <summary>
        /// 已缓存的伤害状态数据集合，键为通知事件本体（对应 UE5 CachedDamageStateData，
        /// 其键是 FAnimNotifyEvent 指针，这里用引用等价的 AnimNotifyEvent 对象）。
        /// </summary>
        protected readonly Dictionary<AnimNotifyEvent, DamageStateDataContainer> CachedDamageStateData =
            new Dictionary<AnimNotifyEvent, DamageStateDataContainer>(ReferenceComparer<AnimNotifyEvent>.Instance);

        /// <summary>本帧扫描用的动画时间游标（对应 UE5 MontageInstance->GetPosition）。</summary>
        private float _notifyStartTime = -1f;
        private float _notifyPlayRate = 1f;

        /// <summary>缺碰撞数据只提示一次（近战每次挥击都会调 PerformCollisionCheck）。</summary>
        private bool _bLoggedMissingCollisionData;

        /// <summary>最近一次报"缺伤害帧数据"的通知，用于去重（同一通知多帧扫描只警告一次）。</summary>
        private AnimNotifyEvent _lastLoggedMissingDamageStateNotify;

        /// <summary>调试：是否绘制近战扫描线（对应 UE5 控制台变量 n.gas.DrawMeleeTraces）。</summary>
        public static bool bDrawMeleeTraces = false;

        // ===== 活动实例登记表 =====
        //
        // 武器视觉挂在"持握时生成的武器预制体 Actor"上，不是角色 Actor 的脚本，
        // 因此 GAS 侧用 Actor.GetScripts<WeaponVisual>() 扫角色永远找不到它
        // （NarrativeCombatAbility.GetAbilityWeaponVisualScript），近战只能退回无动画的扇扫。
        // 这里维护一张等价于 UE5 "按武器查视觉组件" 的活动实例表，供授予/攻击两侧反查。

        private static readonly List<WeaponVisual> _activeVisuals = new List<WeaponVisual>();

        /// <summary>当前活动（已挂载且启用）的武器视觉实例快照。</summary>
        public static IReadOnlyList<WeaponVisual> ActiveVisuals => _activeVisuals;

        /// <summary>登记一个活动视觉实例（幂等）。</summary>
        public static void RegisterVisual(WeaponVisual visual)
        {
            if (visual == null || _activeVisuals.Contains(visual)) return;
            _activeVisuals.Add(visual);
        }

        /// <summary>注销视觉实例。</summary>
        public static void UnregisterVisual(WeaponVisual visual)
        {
            if (visual == null) return;
            _activeVisuals.Remove(visual);
        }

        /// <summary>
        /// 按武器反查视觉实例：优先精确匹配 <see cref="WeaponOwner"/>，
        /// 其次匹配"属于该角色且持握槽位一致"的视觉（武器对象在存档恢复后可能换新）。
        /// </summary>
        public static WeaponVisual FindVisualForWeapon(NarrativePro.Items.WeaponItem weapon, Actor characterActor)
        {
            if (weapon == null) return null;

            for (int i = _activeVisuals.Count - 1; i >= 0; i--)
            {
                var visual = _activeVisuals[i];
                if (visual == null) { _activeVisuals.RemoveAt(i); continue; }
                if (ReferenceEquals(visual.WeaponOwner, weapon)) return visual;
            }

            if (characterActor == null) return null;
            for (int i = _activeVisuals.Count - 1; i >= 0; i--)
            {
                var visual = _activeVisuals[i];
                if (visual == null) { _activeVisuals.RemoveAt(i); continue; }
                if (!BelongsToActor(visual, characterActor)) continue;
                if (visual.AttachState != null && visual.AttachState.WieldedSlot == weapon.WieldedSlot) return visual;
            }
            return null;
        }

        /// <summary>
        /// 取一个"刚由角色视觉生成、还没绑定武器"的实例用于绑定（按生成顺序取最后一个未绑定者）。
        /// </summary>
        public static WeaponVisual TakeUnboundVisual(Actor characterActor)
        {
            for (int i = _activeVisuals.Count - 1; i >= 0; i--)
            {
                var visual = _activeVisuals[i];
                if (visual == null) { _activeVisuals.RemoveAt(i); continue; }
                if (visual.WeaponOwner != null) continue;
                if (characterActor != null && !BelongsToActor(visual, characterActor)) continue;
                return visual;
            }
            return null;
        }

        /// <summary>视觉 Actor 是否挂在指定角色 Actor 之下（含自身）。</summary>
        private static bool BelongsToActor(WeaponVisual visual, Actor actor)
        {
            if (visual == null || actor == null) return false;
            if (ReferenceEquals(visual.Actor, actor)) return true;
            if (visual.AttachState != null && ReferenceEquals(visual.AttachState.VisualOwner?.Actor, actor)) return true;
            if (ReferenceEquals(visual.CharacterOwner?.Actor, actor)) return true;

            var current = visual.Actor != null ? visual.Actor.Parent : null;
            int guard = 0;
            while (current != null && guard++ < 32)
            {
                if (ReferenceEquals(current, actor)) return true;
                current = current.Parent;
            }
            return false;
        }

        // ===== 武器 FX =====
        //
        // 近战挥击/命中要真正消费 FX 资产，否则 NarrativeWeaponFX 只是死数据。
        // FX 来源优先级：显式挂在本视觉上的 InstancedWeaponFX（对应 UE5 武器上手工放置的 instanced FX）
        // → 按 WeaponOwner.ItemClassId 反查注册表（sword/bow 的 JSON 走这条，无需改物品类）。

        /// <summary>本视觉显式配置的实例化 FX 引用（对应 UE5 FInstancedWeaponFX）。可留空走物品反查。</summary>
        public InstancedWeaponFX WeaponFX = new InstancedWeaponFX();

        private NarrativeWeaponFX _cachedFx;
        private WeaponItem _cachedFxForWeapon;

        /// <summary>解析本武器应使用的 FX 定义（带按武器对象的缓存，换武器即失效）。</summary>
        protected NarrativeWeaponFX ResolveWeaponFX()
        {
            if (!ReferenceEquals(_cachedFxForWeapon, WeaponOwner))
            {
                _cachedFxForWeapon = WeaponOwner;
                _cachedFx = null;
            }
            if (_cachedFx != null) return _cachedFx;

            if (WeaponFX != null && WeaponFX.IsValid)
                _cachedFx = WeaponFX.Resolve();
            if (_cachedFx == null)
                _cachedFx = NarrativeWeaponFXRegistry.ResolveForWeapon(WeaponOwner);
            return _cachedFx;
        }

        /// <summary>把 socket 名 + slot 偏移组合成最终世界变换；socket 缺失时回退武器 Actor 世界变换。</summary>
        protected Transform ResolveFXWorldTransform(NarrativeWeaponFXSlotEntry entry)
        {
            Transform base_ = Actor != null ? Actor.Transform : Transform.Identity;
            if (entry != null && WeaponMesh != null && !string.IsNullOrEmpty(entry.Socket.SocketName)
                && TryGetSocketTransform(WeaponMesh, entry.Socket.SocketName, out Transform socketWorld))
            {
                base_ = socketWorld;
            }
            return ApplySocketOffset(base_, entry);
        }

        /// <summary>把 slot 的相对偏移/旋转变换到挂载基准之下，并叠加实例级偏移（对应 UE5 instanced FX 的宿主级微调）。</summary>
        protected Transform ApplySocketOffset(Transform attach, NarrativeWeaponFXSlotEntry entry)
        {
            if (entry == null) return attach;
            Vector3 rel = entry.Socket.RelativeLocation;
            Quaternion rot = Quaternion.Euler(entry.Socket.RelativeRotationEulerDegrees);
            Vector3 scale = entry.Socket.Scale;

            if (WeaponFX != null)
            {
                rel = rel + Vector3.Transform(WeaponFX.InstanceOffset, attach.Orientation);
                scale = Vector3.Multiply(scale, WeaponFX.InstanceScale);
            }

            Transform local = new Transform(rel, rot, scale);
            // 用仓库既有的 TRS 复合（WeaponAnimPose.Compose = attach 的 LocalToWorld(local)），
            // 而非未证实的 Transform.Multiply。
            return WeaponAnimPose.Compose(attach, local);
        }

        /// <summary>在武器 socket 处播放某 slot 的一次性 FX。</summary>
        protected void PlayWeaponFXSlot(EWeaponFXSlot slot)
        {
            var fx = ResolveWeaponFX();
            if (fx == null) return;
            var entry = fx.GetSlot(slot);
            if (entry == null || entry.bLoopWhileActive) return;
            NarrativeWeaponFXPlayback.PlayOneShot(entry, ResolveFXWorldTransform(entry));
        }

        /// <summary>
        /// 在世界命中点播放命中 FX。
        /// 端口差异：不做"贴法线"的朝向对齐（Flax 的 Quaternion.LookAt/LookRotation 语义未逐一核实，
        /// 宁可让 FX 沿用挂载朝向落在命中点，也不臆造朝向），位置才是关键且确定正确的量。
        /// </summary>
        protected void PlayWeaponImpactFX(Vector3 point, Vector3 normal)
        {
            var fx = ResolveWeaponFX();
            if (fx == null) return;
            var entry = fx.GetSlot(EWeaponFXSlot.Impact);
            if (entry == null) return;

            Transform base_ = Actor != null ? Actor.Transform : Transform.Identity;
            Transform attach = new Transform(point, base_.Orientation, entry.Socket.Scale);
            NarrativeWeaponFXPlayback.PlayOneShot(entry, ApplySocketOffset(attach, entry));
        }

        /// <summary>激活本武器所有持久型 FX（拖尾），随武器网格存续。</summary>
        protected void ActivatePersistentFX()
        {
            var fx = ResolveWeaponFX();
            if (fx == null || fx.Slots == null) return;
            foreach (var entry in fx.Slots)
            {
                if (entry == null || !entry.bLoopWhileActive) continue;
                Transform local = new Transform(
                    entry.Socket.RelativeLocation,
                    Quaternion.Euler(entry.Socket.RelativeRotationEulerDegrees),
                    entry.Socket.Scale);
                NarrativeWeaponFXPlayback.ActivatePersistent(this, entry, Actor, local);
            }
        }

        // ===== 生命周期 =====

        /// <summary>对应 UE5 BeginPlay：为已存在的配件重放挂载。</summary>
        public override void OnEnable()
        {
            base.OnEnable();

            // 进入活动实例表：GAS 侧按武器/角色反查视觉（视觉挂在武器预制体 Actor 上，角色 GetScripts 扫不到）
            RegisterVisual(this);

            if (WeaponOwner != null && WeaponOwner.WeaponAttachments != null)
            {
                foreach (var kvp in WeaponOwner.WeaponAttachments)
                {
                    WeaponAttachmentItem attachment = kvp.Value;
                    if (attachment != null)
                    {
                        attachment.HandleAttach(WeaponOwner);
                        WeaponOwner.AddAttachmentVisual(attachment);
                    }
                }
            }

            OnRep_AttachState();
        }

        /// <summary>对应 UE5 Destroyed：把自己从角色视觉的 SpawnedWeaponVisuals 中同步移除。</summary>
        public override void OnDisable()
        {
            // 离开活动实例表，避免 GAS 反查到已销毁的视觉
            UnregisterVisual(this);

            var ourVisual = AttachState.VisualOwner;
            if (ourVisual != null && AttachState.EquippedSlot.IsValid())
            {
                WeaponVisualEntry entry = FindEntry(ourVisual, AttachState.EquippedSlot);
                if (entry != null && ReferenceEquals(entry.WeaponVisualActor, Actor))
                {
                    ourVisual.SpawnedWeaponVisuals.Remove(entry);
                }
            }

            // 视觉下线：回收仍挂在武器 Actor 下的持久 FX，防止拖尾残留（父 Actor 销毁前主动 Stop）
            NarrativeWeaponFXPlayback.DeactivateAllFor(this);
            base.OnDisable();
        }

        /// <summary>返回本视觉所属的 Narrative 角色。对应 UE5 GetNarrativeCharacter。</summary>
        public NarrativeCharacter GetNarrativeCharacter()
        {
            return CharacterOwner;
        }

        // ===== 动画层选择 =====

        /// <summary>返回所有参与碰撞的图元组件（UE5 UPrimitiveComponent* → Collider Actor）。</summary>
        public virtual List<Actor> GetCollidingPrimitives_Implementation()
        {
            // UE5: GetComponents(UPrimitiveComponent::StaticClass(), Primitives)
            // Flax: 收集本 Actor 及其子层中的碰撞体 Actor。
            var primitives = new List<Actor>();
            CollectColliders(Actor, primitives);
            return primitives;
        }

        /// <summary>返回所有参与碰撞的图元组件。</summary>
        public List<Actor> GetCollidingPrimitives()
        {
            return GetCollidingPrimitives_Implementation();
        }

        private static void CollectColliders(Actor root, List<Actor> outColliders)
        {
            if (root == null) return;
            if (root is Collider)
            {
                outColliders.Add(root);
            }
            if (root.HasChildren)
            {
                var children = root.Children;
                if (children != null)
                {
                    for (int i = 0; i < children.Length; i++)
                    {
                        CollectColliders(children[i], outColliders);
                    }
                }
            }
        }

        /// <summary>返回所有武器骨骼网格组件。</summary>
        public virtual List<AnimatedModel> GetWeaponMeshes()
        {
            var meshes = new List<AnimatedModel>();
            if (WeaponMesh != null) meshes.Add(WeaponMesh);
            if (LocalWeaponMesh != null) meshes.Add(LocalWeaponMesh);
            return meshes;
        }

        /// <summary>返回当前视角下相关的武器骨骼网格组件（对应 UE5 GetRelevantWeaponMesh）。</summary>
        public virtual AnimatedModel GetRelevantWeaponMesh()
        {
            if (CharacterOwner != null && CharacterOwner.IsCameraInsideHead() && LocalWeaponMesh != null)
            {
                return LocalWeaponMesh;
            }
            return WeaponMesh;
        }

        /// <summary>
        /// 返回武器覆盖动画层（对应 UE5 GetWeaponOverlayLayer_Implementation）。
        /// 双持与单手持握选择不同层，第一人称缺省回落到第三人称层。
        /// </summary>
        public virtual string GetWeaponOverlayLayer(bool bFirstPersonMesh = false)
        {
            if (CharacterOwner != null)
            {
                var weapons = CharacterOwner.GetWieldedWeapons();
                int count = weapons != null ? weapons.Count : 0;
                bool bDual = count > 1;

                if (bFirstPersonMesh)
                {
                    if (!bDual)
                    {
                        return string.IsNullOrEmpty(Weapon1PAnimLayer) ? DefaultWeaponAnimLayer : Weapon1PAnimLayer;
                    }
                    return string.IsNullOrEmpty(DualWieldWeapon1PAnimLayer) ? DualWieldWeaponAnimLayer : DualWieldWeapon1PAnimLayer;
                }

                // 形态特定的覆盖层优先（如剑盾需要自定义层，UE5 亦说明单一规则不够）
                string formSpecific = GetFormSpecificOverlayLayer();
                if (!string.IsNullOrEmpty(formSpecific)) return formSpecific;

                return bDual ? DualWieldWeaponAnimLayer : DefaultWeaponAnimLayer;
            }

            return DefaultWeaponAnimLayer;
        }

        /// <summary>按角色当前形态标签查形态特定动画层。</summary>
        protected virtual string GetFormSpecificOverlayLayer()
        {
            if (CharacterOwner == null || FormSpecificLayers == null || FormSpecificLayers.Count == 0) return null;

            var ownedTags = new GameplayTagContainer();
            CharacterOwner.GetOwnedGameplayTags(ownedTags);
            if (ownedTags.GetTags() == null) return null;

            foreach (var kvp in FormSpecificLayers)
            {
                if (kvp.Key.IsValid() && ownedTags.HasTag(kvp.Key))
                {
                    return kvp.Value;
                }
            }
            return null;
        }

        // ===== 配件 =====

        /// <summary>处理添加配件（实现）。对应 UE5 HandleAddAttachment_Implementation。</summary>
        public virtual void HandleAddAttachment_Implementation(WeaponAttachmentItem attachment, WeaponAttachmentSlotConfig weaponSlotConfig)
        {
            if (WeaponOwner == null || attachment == null || attachment.AttachmentMesh == null) return;

            GameplayTag slot = attachment.WeaponAttachmentSlot;

            if (!AttachmentMeshComps.ContainsKey(slot))
            {
                StaticModel newMesh = SpawnAttachmentMesh(WeaponMesh, slot);
                if (newMesh != null) AttachmentMeshComps[slot] = newMesh;
            }

            if (LocalWeaponMesh != null && !LocalAttachmentMeshComps.ContainsKey(slot))
            {
                StaticModel newMesh = SpawnAttachmentMesh(LocalWeaponMesh, slot);
                if (newMesh != null) LocalAttachmentMeshComps[slot] = newMesh;
            }

            if (AttachmentMeshComps.TryGetValue(slot, out var mesh3P) && mesh3P != null)
            {
                ApplyAttachmentMesh(mesh3P, attachment, WeaponMesh, weaponSlotConfig);
            }
            else
            {
                NarrativeLog.LogError($"Unable to spawn mesh component for added attachment {attachment.DisplayName}");
            }

            if (LocalWeaponMesh != null && LocalAttachmentMeshComps.TryGetValue(slot, out var mesh1P) && mesh1P != null)
            {
                ApplyAttachmentMesh(mesh1P, attachment, LocalWeaponMesh, weaponSlotConfig);
            }
        }

        /// <summary>处理添加配件。</summary>
        public void HandleAddAttachment(WeaponAttachmentItem attachment, WeaponAttachmentSlotConfig weaponSlotConfig)
        {
            HandleAddAttachment_Implementation(attachment, weaponSlotConfig);
        }

        /// <summary>处理移除配件（实现）。对应 UE5 HandleRemoveAttachment_Implementation。</summary>
        public virtual void HandleRemoveAttachment_Implementation(WeaponAttachmentItem attachment)
        {
            if (attachment == null) return;
            GameplayTag slot = attachment.WeaponAttachmentSlot;

            if (AttachmentMeshComps.TryGetValue(slot, out var mesh3P) && mesh3P != null)
            {
                RestoreDefaultAttachmentMesh(mesh3P, slot, WeaponMesh);
            }

            if (LocalWeaponMesh != null && LocalAttachmentMeshComps.TryGetValue(slot, out var mesh1P) && mesh1P != null)
            {
                RestoreDefaultAttachmentMesh(mesh1P, slot, LocalWeaponMesh);
            }
        }

        /// <summary>处理移除配件。</summary>
        public void HandleRemoveAttachment(WeaponAttachmentItem attachment)
        {
            HandleRemoveAttachment_Implementation(attachment);
        }

        /// <summary>在父网格下生成一个配件静态网格 Actor。对应 UE5 AddComponentByClass(UStaticMeshComponent)。</summary>
        protected virtual StaticModel SpawnAttachmentMesh(AnimatedModel parentMesh, GameplayTag slot)
        {
            if (parentMesh == null) return null;

            // UE5: 配件不参与碰撞（SetCollisionEnabled(NoCollision)），命中由 WeaponCollisionData 负责。
            var mesh = new StaticModel
            {
                Name = $"AttachmentMesh_{slot.TagName}"
            };

            // Level.SpawnActor 返回 true 表示失败（Flax 约定）
            if (Level.SpawnActor(mesh, parentMesh))
            {
                NarrativeLog.LogWarning($"[WeaponVisual] 生成配件网格失败：{slot.TagName}");
                return null;
            }

            mesh.LocalTransform = Transform.Identity;
            return mesh;
        }

        /// <summary>把配件网格与偏移应用到配件 Actor 上。对应 UE5 SetStaticMesh + AttachToComponent(Socket)。</summary>
        protected virtual void ApplyAttachmentMesh(StaticModel target, WeaponAttachmentItem attachment, AnimatedModel parentMesh, WeaponAttachmentSlotConfig config)
        {
            if (target == null || attachment == null) return;

            target.Model = attachment.AttachmentMesh;

            Transform local = attachment.AttachmentLocalTransform;
            if (config != null && !string.IsNullOrEmpty(config.SocketName) && parentMesh != null
                && TryGetBonePose(parentMesh, config.SocketName, out Transform socketLocal))
            {
                // UE5: AttachToComponent(..., SnapToTargetIncludingScale, SocketName)
                local = WeaponAnimPose.Compose(socketLocal, local);
            }

            target.LocalTransform = local;
        }

        /// <summary>移除配件后恢复默认网格（如机械瞄具）。对应 UE5 SetStaticMesh(AttachmentMeshDefaultMeshes[Slot])。</summary>
        protected virtual void RestoreDefaultAttachmentMesh(StaticModel target, GameplayTag slot, AnimatedModel parentMesh)
        {
            if (target == null) return;

            if (AttachmentMeshDefaultMeshes.TryGetValue(slot, out var defaultMesh) && defaultMesh != null)
            {
                target.Model = defaultMesh;
                target.LocalTransform = Transform.Identity;
            }
            else
            {
                target.Model = null;
            }
        }

        /// <summary>注册默认配件（如机械瞄具）。对应 UE5 RegisterDefaultAttachment。</summary>
        public virtual void RegisterDefaultAttachment(GameplayTag slot, StaticModel mesh, StaticModel localMesh)
        {
            if (!slot.IsValid()) return;

            if (mesh != null)
            {
                AttachmentMeshComps[slot] = mesh;
                if (mesh.Model != null) AttachmentMeshDefaultMeshes[slot] = mesh.Model;
            }
            if (localMesh != null) LocalAttachmentMeshComps[slot] = localMesh;
        }

        /// <summary>处理视角更新（第一/第三人称切换）。对应 UE5 HandlePerspectiveUpdate_Implementation。</summary>
        public virtual void HandlePerspectiveUpdate(bool bIsFirstPerson)
        {
            bool bHolstered = !AttachState.WieldedSlot.IsValid();

            // UE5 通过 FirstPersonPrimitiveType 控制 1P 渲染；Flax 中没有该通道，
            // 等价处理为：第一人称时只显示 1P（Local）网格，第三人称只显示世界网格。
            // （Flax 的 Actor 显隐 API 是 IsActive 属性，无 SetActive()。）
            if (WeaponMesh != null)
            {
                WeaponMesh.IsActive = !bIsFirstPerson;
            }

            foreach (var kvp in AttachmentMeshComps)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.IsActive = !bIsFirstPerson;
                }
            }

            if (LocalWeaponMesh != null)
            {
                LocalWeaponMesh.IsActive = bIsFirstPerson && !bHolstered;
            }

            foreach (var kvp in LocalAttachmentMeshComps)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.IsActive = bIsFirstPerson && !bHolstered;
                }
            }
        }

        /// <summary>无参重载：按当前拥有者的视角状态刷新。</summary>
        public virtual void HandlePerspectiveUpdate()
        {
            HandlePerspectiveUpdate(CharacterOwner != null && CharacterOwner.IsCameraInsideHead());
        }

        /// <summary>武器被持握时调用。对应 UE5 OnWielded。</summary>
        public virtual void OnWielded()
        {
            BPHandleWield();
        }

        /// <summary>武器被收起时调用。对应 UE5 OnHolstered。</summary>
        public virtual void OnHolstered()
        {
            BPHandleHolster();
        }

        /// <summary>蓝图扩展点：持握表现。对应 UE5 BPHandleWield。</summary>
        public virtual void BPHandleWield() { }

        /// <summary>蓝图扩展点：收起表现。对应 UE5 BPHandleHolster。</summary>
        public virtual void BPHandleHolster() { }

        /// <summary>成功挂载到拥有者后调用。对应 UE5 HandleAttachedToOwner_Implementation。</summary>
        public virtual void HandleAttachedToOwner_Implementation() { }

        // ===== 近战命中扫描 =====

        /// <summary>
        /// 沿胶囊从 start 扫描到 end。对应 UE5 SweepForHits（KismetTraceUtils::CapsuleSweepMulti）。
        /// UE5 用 FCollisionQueryParams 携带忽略列表；Flax 的 Physics API 无忽略列表，
        /// 故在结果侧过滤（拥有者 + 本次攻击已命中 Actor），语义等价。
        /// </summary>
        public virtual bool SweepForHits(Vector3 start, Vector3 end, Quaternion rot, Vector3 capsuleSize, List<RayCastHit> outHits)
        {
            if (CharacterOwner == null) return false;

            float radius = capsuleSize.X;
            // capsuleSize.Z 是 UE5 语义的半高（含端帽），Flax CapsuleCast 要求不含端帽的圆柱段长度
            float halfHeight = System.Math.Max(capsuleSize.Z, radius);
            float cylinderHeight = System.Math.Max(0f, (halfHeight - radius) * 2f);

            Vector3 delta = end - start;
            float distance = delta.Length;
            if (distance <= 0.0001f || radius <= 0f) return false;
            Vector3 direction = delta / distance;

            uint traceMask = NarrativePro.GAS.NarrativeGASStatics.GetWeaponTraceMask();

            if (!Physics.CapsuleCastAll(start, radius, cylinderHeight, direction, out RayCastHit[] hits, rot, distance, traceMask, false))
            {
                return false;
            }

            if (hits == null || hits.Length == 0) return false;

            Actor ownerActor = CharacterOwner.Actor;
            Actor ownerVisualRoot = GetOwnerVisualRoot();
            bool bResult = false;

            for (int i = 0; i < hits.Length; i++)
            {
                RayCastHit hit = hits[i];
                Actor hitActor = hit.Collider;
                if (hitActor == null) continue;
                if (hitActor == ownerActor) continue;
                if (ownerVisualRoot != null && ownerVisualRoot.HasActorInHierarchy(hitActor) != null) continue;
                if (CachedHitActors.Contains(hitActor)) continue;

                CachedHitActors.Add(hitActor);
                if (outHits != null) outHits.Add(hit);
                bResult = true;

                // 命中点消费 Impact FX（近战扫描每命中一个 Actor 一次，与 CachedHitActors 去重同频）
                PlayWeaponImpactFX(hit.Point, hit.Normal);

                if (bDrawMeleeTraces)
                {
                    NarrativeLog.Log($"[WeaponVisual] melee collider hit {hitActor.Name}");
                }
            }

            return bResult;
        }

        /// <summary>兼容旧签名：结果写入 outHits，返回无值。</summary>
        public virtual void SweepForHits(Vector3 start, Vector3 end, Quaternion rot, Vector3 capsuleSize, out List<RayCastHit> outHits)
        {
            outHits = new List<RayCastHit>();
            SweepForHits(start, end, rot, capsuleSize, outHits);
        }

        private Actor GetOwnerVisualRoot()
        {
            var visual = CharacterOwner != null ? CharacterOwner.GetCharacterVisual() : null;
            return visual != null ? visual.CharacterVisualRoot : null;
        }

        /// <summary>
        /// 缓存动画骨骼变换。对应 UE5 CacheAnimationTransform。
        /// UE5 从 AnimSequence 在通知窗口内离线采样 SampleCount 帧；
        /// Flax 无法离线采样任意时刻姿态，这里按同样窗口预生成帧槽并立即捕获第 0 帧，
        /// 其余帧由 PerformCollisionCheck 在动画推进到时用实时骨骼姿态增量捕获。
        /// </summary>
        public virtual void CacheAnimationTransform(AnimNotifyEventReference animNotifyEventRef)
        {
            CurrentNotifyEvent = animNotifyEventRef;

            // 挥击开始：一次性 Attack FX + 打开持久拖尾（对应 UE5 攻击分支点 NotifyBegin 时 spawn emitter）
            PlayWeaponFXSlot(EWeaponFXSlot.Attack);
            ActivatePersistentFX();

            AnimNotifyEvent notify = animNotifyEventRef.GetNotify();
            if (notify == null) return;

            // 已缓存过该通知，无需重复计算
            if (CachedDamageStateData.ContainsKey(notify)) return;

            AnimatedModel ownerMesh = GetOwnerMeshComponent();
            if (ownerMesh == null || CharacterOwner == null) return;

            // 取武器挂载骨骼/socket 信息（UE5: OwnerMeshComponent->GetSocketInfoByName(WeaponVisualAttachBone)）
            string attachBone = GetWeaponAttachBoneName();
            if (string.IsNullOrEmpty(attachBone)) return;

            if (!TryGetSocketTransform(ownerMesh, attachBone, out Transform socketTransform))
            {
                NarrativeLog.LogWarning($"[WeaponVisual] 找不到武器挂载骨骼 {attachBone}，近战扫描无法缓存动画变换");
                return;
            }

            int settings_samples = NarrativePro.Settings.NarrativeCombatDeveloperSettings.Instance != null
                ? NarrativePro.Settings.NarrativeCombatDeveloperSettings.Instance.MeleeCombatAnimSampleAmount
                : 10;
            int sampleCount = Math.Max(2, settings_samples);

            float startTime = notify.TriggerTime;
            float endTime = startTime + notify.Duration;

            var frames = new List<DamageStateData>(sampleCount);
            for (int i = 0; i < sampleCount; i++)
            {
                float alpha = (float)i / (sampleCount - 1);
                float time = startTime + (endTime - startTime) * alpha;
                frames.Add(new DamageStateData(time));
            }

            // 记录通知起点时间，PerformCollisionCheck 用它推导当前动画时间
            _notifyStartTime = Time.GameTime;
            _notifyPlayRate = notify.PlayRate > 0f ? notify.PlayRate : 1f;

            var container = new DamageStateDataContainer(frames);
            CachedDamageStateData[notify] = container;

            // 立即捕获首帧，保证即使第一帧就调用扫描也有数据
            CaptureDamageStateFrame(container.DamageStateDatas[0], ownerMesh, socketTransform);
        }

        /// <summary>无参兼容重载（UE5 该函数需要通知引用，保留旧签名不改变语义）。</summary>
        public virtual void CacheAnimationTransform()
        {
            CacheAnimationTransform(CurrentNotifyEvent);
        }

        /// <summary>用当前实时骨骼姿态填充一帧的相对变换。对应 UE5 采样循环体内的姿态计算。</summary>
        protected virtual void CaptureDamageStateFrame(DamageStateData state, AnimatedModel ownerMesh, Transform socketTransform)
        {
            if (state == null || ownerMesh == null) return;

            // 根骨骼（UE5: GetBoneLocation("root") / WorldSpacePoses[SkeletonBoneIndex]）
            Transform rootPose = GetRootBonePose(ownerMesh);

            // 武器挂载骨骼的动画姿态（相对根骨骼）
            string attachBone = GetWeaponAttachBoneName();
            if (!TryGetBonePose(ownerMesh, attachBone, out Transform bonePoseWorld))
            {
                return;
            }

            Transform relative = WorldToRootSpace(bonePoseWorld, rootPose);

            // UE5 顺序：先旋转 socket 位移，再乘网格/角色缩放
            Vector3 socketOffset = QuaternionMultiply(socketTransform.Translation, relative.Orientation);
            Vector3 localLocation = relative.Translation + socketOffset;

            // UE5 顺序：先旋转 socket 位移，再乘网格/角色相对缩放
            if (ownerMesh != null)
            {
                localLocation = Vector3.Multiply(localLocation, ownerMesh.Scale);
            }
            if (CharacterOwner != null && CharacterOwner.Actor != null)
            {
                localLocation = Vector3.Multiply(localLocation, CharacterOwner.Actor.Scale);
            }

            state.RelativeLocation = localLocation;
            state.RelativeRotation = Quaternion.Multiply(relative.Orientation, socketTransform.Orientation);
            state.bCaptured = true;
        }

        /// <summary>把世界姿态换算到根骨骼空间。对应 UE5 WorldSpacePoses 与 root 的相对关系。</summary>
        protected virtual Transform WorldToRootSpace(Transform worldPose, Transform rootPose)
        {
            Quaternion invRoot = rootPose.Orientation;
            invRoot.Invert();

            Vector3 offset = Vector3.Subtract(worldPose.Translation, rootPose.Translation);
            Vector3 localPos = QuaternionMultiply(offset, invRoot);
            Quaternion localRot = Quaternion.Multiply(invRoot, worldPose.Orientation);
            return new Transform(localPos, localRot, Vector3.One);
        }

        private static Vector3 QuaternionMultiply(Vector3 vector, Quaternion rotation)
        {
            // Flax 约定：vector * quaternion 表示用该四元数旋转 vector
            return vector * rotation;
        }

        /// <summary>取角色网格的根骨骼世界姿态。</summary>
        protected virtual Transform GetRootBonePose(AnimatedModel ownerMesh)
        {
            if (ownerMesh == null) return Transform.Identity;

            foreach (string rootName in RootBoneNames)
            {
                if (TryGetBonePose(ownerMesh, rootName, out Transform pose))
                {
                    return pose;
                }
            }

            // 骨骼名未知时以 Actor 世界变换作为根（Flax 中等价于组件挂载点）
            return ownerMesh.Transform;
        }

        private static readonly string[] RootBoneNames = { "root", "Root", "Root_M", "Hips", "pelvis", "Pelvis" };

        /// <summary>
        /// 按骨骼/socket 名取世界空间姿态。
        /// Flax 的 AnimatedModel.GetNodeTransformation 无返回值，故先用 SkinnedModel.FindNode 判存在性。
        /// </summary>
        protected virtual bool TryGetBonePose(AnimatedModel mesh, string boneName, out Transform worldPose)
        {
            worldPose = Transform.Identity;
            if (mesh == null || string.IsNullOrEmpty(boneName)) return false;

            SkinnedModel skinned = mesh.SkinnedModel;
            if (skinned == null) return false;
            if (skinned.FindNode(boneName) < 0) return false;

            mesh.GetNodeTransformation(boneName, out Matrix matrix, true);
            worldPose = new Transform(matrix);
            return true;
        }

        /// <summary>按骨骼名取该骨骼上的 socket 变换（世界空间）。Flax 中 socket 即骨骼节点。</summary>
        protected virtual bool TryGetSocketTransform(AnimatedModel mesh, string socketName, out Transform socketTransform)
        {
            return TryGetBonePose(mesh, socketName, out socketTransform);
        }

        /// <summary>武器挂载骨骼名（对应 UE5 WeaponItem::GetWeaponVisualAttachBone）。</summary>
        protected virtual string GetWeaponAttachBoneName()
        {
            if (WeaponOwner != null)
            {
                return WeaponOwner.GetWeaponVisualAttachBone();
            }
            return null;
        }

        /// <summary>角色的主骨骼网格组件（对应 UE5 CharacterOwner->GetMesh()）。</summary>
        protected virtual AnimatedModel GetOwnerMeshComponent()
        {
            var visual = CharacterOwner != null ? CharacterOwner.GetCharacterVisual() : null;
            if (visual != null)
            {
                AnimatedModel mesh = visual.GetLeaderMesh();
                if (mesh != null) return mesh;
                mesh = visual.GetMainMesh();
                if (mesh != null) return mesh;
            }
            return null;
        }

        /// <summary>
        /// 对当前及尚未计算过的动画数据做碰撞扫描。对应 UE5 PerformCollisionCheck。
        /// 保证低帧率下把两帧之间的位移用连续扫描补齐。
        /// </summary>
        public virtual void PerformCollisionCheck(List<RayCastHit> outHits)
        {
            if (outHits == null) return;
            outHits.Clear();

            if (CharacterOwner == null || CollisionData == null || CollisionData.Count == 0)
            {
                // 未配置近战碰撞数据：这是数据缺项而不是运行期错误，降为一次性 Info 以免每次挥击刷屏
                if (!_bLoggedMissingCollisionData)
                {
                    _bLoggedMissingCollisionData = true;
                    NarrativeLog.Log(
                        $"[WeaponVisual] '{Actor?.Name}' 没有近战碰撞数据（CollisionData 为空），本次攻击不做武器扫描命中。");
                }
                return;
            }

            AnimNotifyEvent notify = CurrentNotifyEvent.GetNotify();
            if (notify == null) return;

            if (!CachedDamageStateData.TryGetValue(notify, out var container) || container == null || container.DamageStateDatas.Count == 0)
            {
                // 动画通知没有对应的碰撞帧数据：属于配置缺项/时序问题，降为 Warning（每个通知只报一次），
                // 一次挥击扫多帧时不再刷 Error 日志
                if (!ReferenceEquals(notify, _lastLoggedMissingDamageStateNotify))
                {
                    _lastLoggedMissingDamageStateNotify = notify;
                    NarrativeLog.LogWarning(
                        $"[WeaponVisual] 未找到当前攻击通知 '{notify?.NotifyName}' 的缓存伤害帧，本次扫描跳过（No collision checks will be performed!）。");
                }
                return;
            }

            if (_notifyStartTime < 0f) return;

            // 当前动画时间：Flax 无蒙太奇位置查询，用通知起始游戏时间推算
            float currentTime = (float)(Time.GameTime - _notifyStartTime) * _notifyPlayRate;

            AnimatedModel ownerMesh = GetOwnerMeshComponent();
            if (ownerMesh == null) return;

            Transform rootPose = GetRootBonePose(ownerMesh);
            // UE5 按武器的 OwnerCapsuleRotationSetting 决定胶囊旋转（WeaponItem.h:207）：
            // NoRotation 沿用根骨骼姿态，控制器 yaw / 移动朝向两类模式则用对应 yaw 重建。
            Quaternion ownerRotation = rootPose.Orientation;
            var ownerActor = CharacterOwner != null ? CharacterOwner.Actor : null;
            if (WeaponOwner != null && ownerActor != null)
            {
                Vector3 actorForward = Vector3.Transform(Vector3.Forward, ownerActor.Orientation);
                float yawDegrees = (float)(System.Math.Atan2(actorForward.X, actorForward.Z) * (180.0 / System.Math.PI));
                ownerRotation = WeaponOwner.ResolveCapsuleRotation(yawDegrees, yawDegrees, rootPose.Orientation);
            }
            Vector3 ownerLocation = rootPose.Translation;

            // UE5 预留了 look-at 旋转钩子（未来支持瞄准修正时在此接入）
            Quaternion lookAtRotation = Quaternion.Identity;

            string attachBone = GetWeaponAttachBoneName();
            bool bHaveSocket = TryGetSocketTransform(ownerMesh, attachBone, out Transform socketTransform);

            for (int frameIndex = 0; frameIndex < container.DamageStateDatas.Count; frameIndex++)
            {
                DamageStateData state = container.DamageStateDatas[frameIndex];
                if (state == null) continue;
                if (currentTime < state.FrameTime || state.bTriggered) continue;

                // 尚未捕获的帧：此刻动画已推进到该帧，抓取实时姿态补算
                if (!state.bCaptured && bHaveSocket)
                {
                    CaptureDamageStateFrame(state, ownerMesh, socketTransform);
                }
                if (!state.bCaptured)
                {
                    state.bTriggered = true;
                    continue;
                }

                for (int c = 0; c < CollisionData.Count; c++)
                {
                    WeaponCollisionData collData = CollisionData[c];
                    if (collData == null) continue;

                    Quaternion rotation = ownerRotation;

                    Quaternion relativeRotation = Quaternion.Multiply(lookAtRotation, state.RelativeRotation);
                    Vector3 correctedLocation = QuaternionMultiply(collData.RelativeLocation, relativeRotation);

                    rotation = Quaternion.Multiply(rotation, relativeRotation);
                    rotation = Quaternion.Multiply(rotation, collData.RelativeRotation);

                    // 首次使用时补上上一帧位置，避免从 0 开始的假扫描
                    if (collData.LastLocationNearlyZero())
                    {
                        Vector3 last = state.RelativeLocation + correctedLocation;
                        last = QuaternionMultiply(last, lookAtRotation);
                        last = QuaternionMultiply(last, ownerRotation);
                        collData.LastLocation = last + ownerLocation;
                    }

                    Vector3 endLocation = state.RelativeLocation + correctedLocation;
                    endLocation = QuaternionMultiply(endLocation, lookAtRotation);
                    endLocation = QuaternionMultiply(endLocation, ownerRotation);
                    endLocation += ownerLocation;

                    Vector3 startLocation = collData.LastLocation;

                    var hits = new List<RayCastHit>();
                    Vector3 extent = collData.Shape.GetExtent();
                    if (SweepForHits(startLocation, endLocation, rotation, extent, hits))
                    {
                        outHits.AddRange(hits);
                    }

                    collData.LastLocation = endLocation;
                }

                state.bTriggered = true;
            }
        }

        /// <summary>
        /// 缓存碰撞数据。对应 UE5 CacheCollisionData(bool bForceUpdate)：
        /// 遍历参与碰撞的图元，把其胶囊几何换算成相对武器根（Actor）的位移/旋转后缓存。
        /// </summary>
        public virtual void CacheCollisionData(bool bForceUpdate)
        {
            if (!bForceUpdate && CollisionData != null && CollisionData.Count > 0) return;

            if (CollisionData == null) CollisionData = new List<WeaponCollisionData>();
            CollisionData.Clear();

            List<Actor> primitives = GetCollidingPrimitives();
            if (primitives == null || primitives.Count == 0) return;

            // UE5 以 RootComponent 的相对变换为参照；Flax 中武器视觉根即本 Actor
            Transform rootWorld = Actor != null ? Actor.Transform : Transform.Identity;

            for (int i = 0; i < primitives.Count; i++)
            {
                if (TryBuildCollisionData(primitives[i], rootWorld, out WeaponCollisionData data) && data != null)
                {
                    CollisionData.Add(data);
                }
            }
        }

        /// <summary>兼容旧签名：强制刷新。</summary>
        public virtual void CacheCollisionData()
        {
            CacheCollisionData(true);
        }

        /// <summary>由单个碰撞图元 Actor 生成一条 WeaponCollisionData。</summary>
        protected virtual bool TryBuildCollisionData(Actor primitive, Transform rootWorld, out WeaponCollisionData data)
        {
            data = null;
            if (primitive == null) return false;

            float radius;
            float halfHeight;
            Vector3 centerOffset;

            // UE5 只走 AggGeom.SphylElems（胶囊）分支，非胶囊图元忽略
            if (primitive is CapsuleCollider capsule)
            {
                // Flax CapsuleCollider.Height 是两端半球圆心之间的距离
                radius = capsule.Radius;
                halfHeight = radius + capsule.Height * 0.5f;
                centerOffset = Vector3.Zero;
            }
            else if (primitive is SphereCollider sphere)
            {
                radius = sphere.Radius;
                halfHeight = radius;
                centerOffset = Vector3.Zero;
            }
            else if (primitive is BoxCollider box)
            {
                Vector3 ext = box.Size * 0.5f;
                radius = System.Math.Max(ext.X, ext.Y);
                halfHeight = ext.Z + radius;
                centerOffset = Vector3.Zero;
            }
            else
            {
                return false;
            }

            // 世界缩放会同时放大半径与长度
            Vector3 scale = primitive.Scale;
            float uniformScale = (Mathf.Abs(scale.X) + Mathf.Abs(scale.Y) + Mathf.Abs(scale.Z)) / 3f;
            if (uniformScale > 0f)
            {
                radius *= uniformScale;
                halfHeight *= uniformScale;
            }

            if (radius <= 0f || halfHeight <= 0f) return false;

            // 相对武器根的位置/旋转：等价 UE5 PrimitiveTransform.GetRelativeTransform(RootTransform)
            Transform relative = WorldToRootSpace(primitive.Transform, rootWorld);

            data = new WeaponCollisionData(
                relative.Translation + QuaternionMultiply(centerOffset, relative.Orientation),
                relative.Orientation,
                Vector3.Zero,
                new WeaponCollisionShape(radius, halfHeight));
            return true;
        }

        /// <summary>清理攻击数据以便复用。对应 UE5 CleanupAttackData。</summary>
        public virtual void CleanupAttackData()
        {
            CachedHitActors.Clear();

            AnimNotifyEvent notify = CurrentNotifyEvent.GetNotify();
            if (notify != null && CachedDamageStateData.TryGetValue(notify, out var cached))
            {
                for (int i = 0; i < cached.DamageStateDatas.Count; i++)
                {
                    cached.DamageStateDatas[i].bTriggered = false;
                }
            }

            for (int i = 0; i < CollisionData.Count; i++)
            {
                if (CollisionData[i] != null)
                {
                    CollisionData[i].LastLocation = Vector3.Zero;
                }
            }

            CurrentNotifyEvent = default;
            _notifyStartTime = -1f;

            // 攻击窗口结束：关掉本次挥击打开的持久拖尾（对应 UE5 NotifyEnd 里 DeactivateEmitter）
            NarrativeWeaponFXPlayback.DeactivateAllFor(this);
        }

        /// <summary>取指定通知已缓存的帧数据，供能力/调试读取。</summary>
        public bool TryGetCachedDamageState(out DamageStateDataContainer container)
        {
            container = null;
            AnimNotifyEvent notify = CurrentNotifyEvent.GetNotify();
            return notify != null && CachedDamageStateData.TryGetValue(notify, out container);
        }

        // ===== 挂载流程 =====

        /// <summary>对应 UE5 UpdateWeaponAttachment：武器拥有者变化时刷新配件表现。</summary>
        protected virtual void UpdateWeaponAttachment()
        {
            if (WeaponOwner == null || !bAttachedSuccessfully) return;

            var attachments = new List<WeaponAttachmentItem>(WeaponOwner.WeaponAttachments.Count);
            foreach (var kvp in WeaponOwner.WeaponAttachments)
            {
                if (kvp.Value != null) attachments.Add(kvp.Value);
            }

            for (int i = 0; i < attachments.Count; i++)
            {
                var attachment = attachments[i];
                if (attachment.WeaponAttachmentSlot.IsValid() && !AttachmentMeshComps.ContainsKey(attachment.WeaponAttachmentSlot))
                {
                    WeaponOwner.AddAttachmentVisual(attachment);
                }
            }
        }

        /// <summary>应用挂载状态。对应 UE5 ApplyAttachState（UE5 原实现为空，保留扩展点）。</summary>
        public virtual void ApplyAttachState()
        {
            OnRep_AttachState();
        }

        /// <summary>挂载状态变更回调。对应 UE5 OnRep_AttachState（本地化，去掉网络措辞）。</summary>
        public virtual void OnRep_AttachState()
        {
            if (!AttachState.CanAttach()) return;

            CharacterOwner = AttachState.CharOwner;
            if (CharacterOwner == null) return;

            VisualOwner = AttachState.VisualOwner;
            WeaponOwner = AttachState.WeaponOwner;

            NarrativePro.Character.NarrativeCharacterVisual visual = VisualOwner;

            // 把自己登记进角色的 SpawnedWeaponVisuals（UE5: CharVisual->SpawnedWeaponVisuals.Add(EquippedSlot, this)）
            WeaponVisualEntry entry = FindEntry(visual, AttachState.EquippedSlot);
            if (entry == null)
            {
                entry = new WeaponVisualEntry { Slot = AttachState.EquippedSlot };
                visual.SpawnedWeaponVisuals.Add(entry);
            }
            entry.WeaponVisualActor = Actor;

            // 武器已就绪，把视觉挂到角色骨骼网格上
            AttachToVisual(visual);

            // 持握状态可能因视觉尚未生成而失败，要求重放（UE5 OnRep_WieldState）
            CharacterOwner.SetWieldState(CharacterOwner.GetWeaponWieldState());

            if (!bAttachedSuccessfully)
            {
                HandleAttachedToOwner_Implementation();
                UpdateWeaponAttachment();
                bAttachedSuccessfully = true;
            }

            // 非本地/机器人不需要第一人称网格
            bool bLocal = visual.IsLocallyControlled();
            bool bBot = IsOwnerBotControlled();
            if ((!bLocal || bBot) && LocalWeaponMesh != null)
            {
                DestroyLocalMeshes();
            }

            // 碰撞数据在挂载完成后才可靠
            CacheCollisionData(true);
        }

        /// <summary>把武器视觉挂到角色视觉根下。对应 UE5 CharVisual->AttachWeaponVisual(...)。</summary>
        protected virtual void AttachToVisual(NarrativePro.Character.NarrativeCharacterVisual visual)
        {
            if (visual == null || Actor == null) return;

            Actor parent = visual.GetLeaderMesh() != null ? visual.GetLeaderMesh() : visual.CharacterVisualRoot;
            if (parent == null || parent == Actor) return;

            // 已在目标父级下则不重复挂接
            if (parent.HasActorInHierarchy(Actor) != null) return;

            Actor child = Actor;
            if (WeaponMesh != null)
            {
                // 保持武器网格的世界位置后再建立父子关系
                child.SetParent(WeaponMesh, true, false);
            }
            else
            {
                child.SetParent(parent, false, false);
            }
        }

        /// <summary>机器人（NPC）控制判定。NPC 角色重写了 IsBotControlled。</summary>
        protected virtual bool IsOwnerBotControlled()
        {
            if (CharacterOwner is NarrativePro.UnrealFramework.NarrativeNPCCharacter npc)
            {
                return npc.IsBotControlled();
            }
            if (CharacterOwner is NarrativePro.UnrealFramework.NarrativePlayerCharacter player)
            {
                return player.IsBotControlled();
            }
            return false;
        }

        private static WeaponVisualEntry FindEntry(NarrativePro.Character.NarrativeCharacterVisual visual, GameplayTag slot)
        {
            if (visual == null || !slot.IsValid()) return null;
            for (int i = 0; i < visual.SpawnedWeaponVisuals.Count; i++)
            {
                var e = visual.SpawnedWeaponVisuals[i];
                if (e != null && e.Slot == slot) return e;
            }
            return null;
        }

        private void DestroyLocalMeshes()
        {
            foreach (var kvp in LocalAttachmentMeshComps)
            {
                if (kvp.Value != null)
                {
                    Destroy(kvp.Value);
                }
            }
            LocalAttachmentMeshComps.Clear();

            if (LocalWeaponMesh != null)
            {
                Destroy(LocalWeaponMesh);
            }
            LocalWeaponMesh = null;
        }

        /// <summary>WeaponOwner 复制回调（UE5 OnRep_WeaponOwner）。本地改为普通方法。</summary>
        public virtual void OnRep_WeaponOwner()
        {
            UpdateWeaponAttachment();
        }

        /// <summary>VisualOwner 变更回调（UE5 OnRep_VisualOwner）。</summary>
        public virtual void OnRep_VisualOwner()
        {
            UpdateWeaponAttachment();
        }
    }

    /// <summary>引用语义比较器，用于以 AnimNotifyEvent 对象作为字典键。</summary>
    public sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();
        public bool Equals(T a, T b) => ReferenceEquals(a, b);
        public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
