using System;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;
using NarrativePro.UnrealFramework;

namespace NarrativePro.GAS
{
    /// <summary>
    /// 瞄准变换提供者基类。移植自 UE5 NarrativeCombatAbility.h:39-53（UTargetingTransformProvider）。
    /// UE5 用 Instanced UObject 组合，把"从哪里朝哪里打"与战斗能力本体解耦；
    /// Flax 无 EditInlineNew，改用 [Serializable] 类 + 类型名字段实例化，语义等价。
    /// </summary>
    [Serializable]
    public class TargetingTransformProvider
    {
        /// <summary>
        /// 提供战斗能力用于瞄准的世界变换。
        /// 对应 UE5 ProvideTargetingTransform_Implementation(const AController*)。
        /// </summary>
        /// <param name="controller">发起者控制器（Flax 中为 Actor，NPC 控制器/玩家控制器皆可）。</param>
        public virtual Transform ProvideTargetingTransform(Actor controller)
        {
            return Transform.Identity;
        }

        /// <summary>按角色上下文提供瞄准变换（无控制器时的回退入口）。</summary>
        public virtual Transform ProvideTargetingTransform(NarrativeCharacter character)
        {
            if (character == null || character.Actor == null) return Transform.Identity;
            return ProvideTargetingTransform(character.Actor);
        }

        /// <summary>把控制器 Actor 解析为 Narrative 角色（对应 UE5 Cast&lt;INarrativeCharacterOwner&gt;）。</summary>
        public static NarrativeCharacter ResolveCharacter(Actor controller)
        {
            if (controller == null) return null;

            var character = controller.GetScript<NarrativeCharacter>();
            if (character != null) return character;

            var controllerScript = controller.GetScript<NarrativePlayerController>();
            if (controllerScript != null) return controllerScript.GetNarrativeCharacter();

            return null;
        }

        /// <summary>
        /// 取角色的视点（眼睛位置 + 朝向）。对应 UE5 Controller->GetPlayerViewPoint。
        /// 玩家本地取活动相机，其余（NPC/远端）退回角色 Actor 朝向。
        /// </summary>
        protected static void GetPlayerViewPoint(NarrativeCharacter character, out Vector3 location, out Quaternion rotation)
        {
            location = Vector3.Zero;
            rotation = Quaternion.Identity;

            Actor pawn = character != null ? character.Actor : null;
            if (pawn == null) return;

            location = pawn.Position;
            rotation = pawn.Orientation;

            // 本地玩家：以相机为准（Flax 中 NarrativeCharacter 的视线由相机组件/主相机提供）
            if (character.IsLocallyControlled())
            {
                // NarrativePro.Camera 命名空间遮蔽了 FlaxEngine.Camera，须显式限定
                FlaxEngine.Camera camera = FlaxEngine.Camera.MainCamera;
                if (camera != null)
                {
                    location = camera.Position;
                    rotation = camera.Orientation;
                }
                else
                {
                    var visual = character.GetCharacterVisual();
                    if (visual != null && visual.CharacterVisualRoot != null)
                    {
                        Vector3 head = visual.CharacterVisualRoot.Position;
                        location = head + Vector3.Up * 160f;
                    }
                }
            }
        }

        /// <summary>取角色自身的忽略集合（UE5 GetIgnoreCharacterParams 的等价：追踪时忽略自己）。</summary>
        protected static bool ShouldIgnoreActor(NarrativeCharacter character, Actor candidate)
        {
            if (character == null || candidate == null) return false;
            if (candidate == character.Actor) return true;

            var visual = character.GetCharacterVisual();
            if (visual != null && visual.CharacterVisualRoot != null &&
                visual.CharacterVisualRoot.HasActorInHierarchy(candidate) != null)
            {
                return true;
            }

            var moveIgnore = character.MoveIgnoreActors;
            if (moveIgnore != null && moveIgnore.Contains(candidate)) return true;

            return false;
        }
    }

    /// <summary>
    /// 以相机为起点、聚焦我方准心的瞄准变换。
    /// 移植自 UE5 UTargetingTransformProvider_CameraTowardsFocus（NarrativeCombatAbility.h:58-68 + .cpp:404-432）。
    /// </summary>
    [Serializable]
    public class TargetingTransformProvider_CameraTowardsFocus : TargetingTransformProvider
    {
        /// <summary>视线前方 focal 点距离（UE5 硬编码 1024）。</summary>
        public float FocalDistance = 1024f;

        public override Transform ProvideTargetingTransform(Actor controller)
        {
            var character = ResolveCharacter(controller);
            if (character == null) return Transform.Identity;

            Vector3 pawnLoc = character.Actor.Position;
            GetPlayerViewPoint(character, out Vector3 eyesLoc, out Quaternion eyesRot);

            Vector3 aimDir = Vector3.Forward * eyesRot;
            if (aimDir.LengthSquared < 0.0001f) return new Transform(eyesLoc, eyesRot);

            Vector3 focalLoc = eyesLoc + aimDir * FocalDistance;

            // StartPoint = Focal + ((Pawn - Focal) · AimDir) * AimDir
            Vector3 toPawn = pawnLoc - focalLoc;
            float projection = Vector3.Dot(toPawn, aimDir);
            Vector3 startPoint = focalLoc + aimDir * projection;

            return new Transform(startPoint, eyesRot);
        }

    }

    /// <summary>
    /// 以武器为起点、朝聚焦点瞄准的变换。
    /// 移植自 UE5 UTargetingTransformProvider_WeaponTowardsFocus（.h:73-96 + .cpp:435-523）。
    /// </summary>
    [Serializable]
    public class TargetingTransformProvider_WeaponTowardsFocus : TargetingTransformProvider
    {
        /// <summary>true 时使用武器视觉网格，否则用角色网格上的骨骼。对应 UE5 bUseWeaponVisualMesh。</summary>
        public bool bUseWeaponVisualMesh = true;

        /// <summary>所使用的武器视觉/角色骨骼名。对应 UE5 BoneName。</summary>
        public string BoneName = "";

        /// <summary>
        /// 沿瞄准方向向前推出的距离，避免投射物生成在射手/武器内部。对应 UE5 ForwardPushDist。
        /// UE5 默认 0（改为让投射物短时间内忽略拥有者）。
        /// </summary>
        public float ForwardPushDist = 0f;

        /// <summary>聚焦追踪距离（UE5 硬编码 10000）。</summary>
        public float FocalTraceDistance = 10000f;

        public override Transform ProvideTargetingTransform(Actor controller)
        {
            var character = ResolveCharacter(controller);
            if (character == null) return Transform.Identity;

            // 先看我们瞄准了什么
            GetPlayerViewPoint(character, out Vector3 eyesLoc, out Quaternion eyesRot);
            Vector3 forward = Vector3.Forward * eyesRot;
            Vector3 end = eyesLoc + forward * FocalTraceDistance;

            Vector3 focal = end;
            if (Physics.LineCast(eyesLoc, end, out RayCastHit hit, NarrativeGASStatics.GetWeaponTraceMask(), false))
            {
                if (hit.Collider != null && !ShouldIgnoreActor(character, hit.Collider))
                {
                    focal = hit.Point;
                }
            }

            AnimatedModel meshToUse = character.GetCharacterVisual() != null ? character.GetCharacterVisual().GetLeaderMesh() : null;

            if (bUseWeaponVisualMesh)
            {
                var ability = GetOwningCombatAbility();
                bool bMainhand = ability != null ? ability.IsMainhand() : true;

                var weaponVisual = GetWieldedWeaponVisual(character, bMainhand);
                if (weaponVisual != null)
                {
                    AnimatedModel relevant = weaponVisual.GetRelevantWeaponMesh();
                    if (relevant != null) meshToUse = relevant;
                }
            }

            if (meshToUse == null) return Transform.Identity;

            string bone = string.IsNullOrEmpty(BoneName) ? GetWeaponAttachBone(character) : BoneName;
            if (string.IsNullOrEmpty(bone)) return Transform.Identity;

            if (meshToUse.SkinnedModel != null && meshToUse.SkinnedModel.FindNode(bone) < 0)
            {
                NarrativeLog.LogWarning($"[TargetingTransformProvider_WeaponTowardsFocus] 骨骼 {bone} 不在网格骨骼表中");
                return Transform.Identity;
            }

            meshToUse.GetNodeTransformation(bone, out Matrix boneMatrix, true);
            Vector3 socketLoc = new Transform(boneMatrix).Translation;

            Vector3 aimVector = focal - socketLoc;
            if (aimVector.LengthSquared < 0.0001f) aimVector = forward;

            Quaternion aimRot = Quaternion.LookRotation(aimVector.Normalized, Vector3.Up);
            Vector3 aimLoc = socketLoc + (Vector3.Forward * aimRot) * ForwardPushDist;

            return new Transform(aimLoc, aimRot, Vector3.One);
        }

        /// <summary>
        /// 取所属战斗能力（UE5 通过 GetOuter() 拿到宿主 UNarrativeCombatAbility）。
        /// Flax 无 Outer 链，改由 OwningCombatAbility 显式回填（InInstancedTargetingTransform 组装时设置）。
        /// </summary>
        [NonSerialized]
        public NarrativeCombatAbility OwningCombatAbility;

        protected NarrativeCombatAbility GetOwningCombatAbility() => OwningCombatAbility;

        /// <summary>取主手/副手持握的武器视觉。</summary>
        protected static NarrativePro.Weapons.WeaponVisual GetWieldedWeaponVisual(NarrativeCharacter character, bool bMainhand)
        {
            var visual = character.GetCharacterVisual();
            if (visual == null) return null;

            // Core 与 Items 各有一份 NarrativeGameplayTags；持握槽位标签以 Core 的注册表为准
            GameplayTag wieldSlot = bMainhand
                ? NarrativePro.Core.NarrativeGameplayTags.Weapon_WieldSlot_Mainhand
                : NarrativePro.Core.NarrativeGameplayTags.Weapon_WieldSlot_Offhand;

            var equipment = character.GetEquipmentComponent();
            WeaponItem weapon = equipment != null ? equipment.GetWieldedWeaponAtSlot(wieldSlot) : null;
            if (weapon == null) return null;

            var entry = FindVisualEntry(visual, weapon.CurrentSlot);
            if (entry == null) return null;

            return entry.WeaponVisualActor != null ? entry.WeaponVisualActor.GetScript<NarrativePro.Weapons.WeaponVisual>() : null;
        }

        private static NarrativePro.Character.WeaponVisualEntry FindVisualEntry(
            NarrativePro.Character.NarrativeCharacterVisual visual, GameplayTag slot)
        {
            if (visual == null || !slot.IsValid()) return null;
            for (int i = 0; i < visual.SpawnedWeaponVisuals.Count; i++)
            {
                var e = visual.SpawnedWeaponVisuals[i];
                if (e != null && e.Slot == slot) return e;
            }
            return null;
        }

        /// <summary>取武器挂载骨骼名（UE5 WeaponItem::GetWeaponVisualAttachBone）。</summary>
        protected static string GetWeaponAttachBone(NarrativeCharacter character)
        {
            var weapons = character.GetWieldedWeapons();
            if (weapons == null || weapons.Count == 0) return null;
            return weapons[0] != null ? weapons[0].GetWeaponVisualAttachBone() : null;
        }

    }

    /// <summary>
    /// 实例化瞄准变换。移植自 UE5 FInstancedTargetingTransform（.h:99-110）。
    /// UE5 用 UPROPERTY(Instanced) 在编辑器下拉选择子类；
    /// Flax 序列化不保留多态实例，这里用 ProviderTypeName + 实例的组合，反序列化后可重建 Provider。
    /// </summary>
    [Serializable]
    public class InInstancedTargetingTransform
    {
        /// <summary>提供者类型名（TargetingTransformProvider 的子类名）。</summary>
        public string ProviderTypeName = "";

        /// <summary>提供者实例（运行时按 ProviderTypeName 创建，或外部直接赋值）。</summary>
        public TargetingTransformProvider Provider;

        public InInstancedTargetingTransform() { }

        public InInstancedTargetingTransform(TargetingTransformProvider provider)
        {
            Provider = provider;
            ProviderTypeName = provider != null ? provider.GetType().Name : "";
        }

        /// <summary>取提供者；缺失时按类型名重建（对应 UE5 Instanced 对象的默认实例化）。</summary>
        public TargetingTransformProvider GetProvider()
        {
            if (Provider == null)
            {
                Provider = CreateByTypeName(ProviderTypeName);
            }
            return Provider;
        }

        /// <summary>便捷求值：无提供者时返回 Identity。</summary>
        public Transform ProvideTargetingTransform(Actor controller)
        {
            var provider = GetProvider();
            return provider != null ? provider.ProvideTargetingTransform(controller) : Transform.Identity;
        }

        private static TargetingTransformProvider CreateByTypeName(string typeName)
        {
            switch (typeName)
            {
                case nameof(TargetingTransformProvider_CameraTowardsFocus):
                    return new TargetingTransformProvider_CameraTowardsFocus();
                case nameof(TargetingTransformProvider_WeaponTowardsFocus):
                    return new TargetingTransformProvider_WeaponTowardsFocus();
                default:
                    return null;
            }
        }
    }
}
