using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.AI;
using NarrativePro.Audio;
using NarrativePro.Core;
using NarrativePro.Items;
using NarrativePro.UnrealFramework;

namespace NarrativePro.Character
{
    /// <summary>
    /// 角色 foley 调用的接线点（UE5 脚步/foley 播放路径在 Flax 侧的等价物）。
    /// <para>
    /// UE5 中 foley 由两处发起：动画脚步 Notify 与 GASPs 的移动状态事件，它们经
    /// <c>I_FoleyAudioBankInterface</c> 拿到 <c>UFoleyAudioBank</c>，再按
    /// (事件标签, 命中表面) 取 MetaSound 播放。本插件的 C++ 侧只保留了库本身
    /// （<c>Audio/FoleyAudioBank.h</c>），播放发生在蓝图，因此 Flax 端口把"请求 foley"
    /// 收敛成这里的扩展方法 + <see cref="NarrativeFootstepFoley"/> 组件：
    /// 组件按移动驱动（位移累计 → 脚步；模式切换 → 起跳/落地），
    /// 向下射线取 <see cref="RayCastHit.Material"/>，经 <see cref="NarrativePhysicalMaterial"/>
    /// 解析表面后交给 <see cref="NarrativeFoleyPlayback"/> 播放，并且只在本地权威角色上播放，
    /// 远端占位角色保持静默（否则联机时每台机器都会为每个远端角色刷一遍脚步声）。
    /// </para>
    /// </summary>
    public static class NarrativeCharacterFoleyExtensions
    {
        /// <summary>
        /// 角色请求一条 foley（对应 UE5 蓝图调 PlayFoley/GetFoleySoundForSurface 的写法）。
        /// 默认走附着式播放：音源挂在角色 Actor 上，随角色移动并做 3D 衰减。
        /// </summary>
        /// <param name="character">发起 foley 的角色。</param>
        /// <param name="foleyTag">foley 事件标签（见 <see cref="NarrativeFoleyTags"/>）。</param>
        /// <param name="surface">发声的物理表面。</param>
        /// <param name="bank">指定音效库；null 时取角色自带库或全局活动库。</param>
        /// <param name="volume">音量。</param>
        /// <param name="bSpawnEffect">是否同时播放库中配置的粒子特效。</param>
        /// <returns>是否实际播放。</returns>
        public static bool RequestFoley(
            this NarrativeCharacter character,
            GameplayTag foleyTag,
            NarrativePhysicalSurface surface,
            FoleyAudioBank bank = null,
            float volume = 1f,
            bool bSpawnEffect = true)
        {
            if (character == null || !foleyTag.IsValid()) return false;

            var ownerActor = character.Actor;
            if (ownerActor == null) return false;

            return NarrativeFoleyPlayback.PlayFoleyAttached(
                ResolveBank(character, bank),
                foleyTag,
                surface,
                ownerActor,
                volume,
                0.08f,
                bSpawnEffect,
                new Vector3(0f, -80f, 0f),
                character.GetFoleyRandom());
        }

        /// <summary>
        /// 按一次射线命中请求 foley：表面直接从命中的物理材质解析
        /// （对应 UE5 用 <c>HitResult.PhysMaterial</c> 的 <c>SurfaceType</c> 查库）。
        /// </summary>
        public static bool RequestFoleyFromHit(this NarrativeCharacter character, GameplayTag foleyTag, RayCastHit hit, FoleyAudioBank bank = null, float volume = 1f, bool bSpawnEffect = true)
        {
            return character.RequestFoley(foleyTag, NarrativePhysicalMaterial.GetSurface(hit.Material), bank, volume, bSpawnEffect);
        }

        /// <summary>
        /// 伤害/命中侧 foley 的公开静态入口（UE5 由攻击 Notify 与命中回调发起 foley，
        /// Flax 侧的伤害路径在 GAS 里，ASC 无法反向依赖角色，故在此提供零门槛入口）。
        /// <para>
        /// 解析顺序：受击 Actor 上有 <see cref="NarrativeCharacter"/> → 走角色的附着式播放
        /// （使用角色自带库/种子，随角色移动做 3D 衰减）；否则退化为命中点的一次性位置播放。
        /// </para>
        /// </summary>
        /// <param name="avatarActor">受击（或命中）的 Actor。</param>
        /// <param name="foleyTag">foley 事件标签（如 <see cref="NarrativeFoleyTags.Feet_Land"/>）。</param>
        /// <param name="hit">命中结果，表面取自 <see cref="RayCastHit.Material"/>。</param>
        /// <param name="volume">音量。</param>
        /// <param name="bSpawnEffect">是否同时播放库中配置的粒子特效。</param>
        /// <returns>是否实际播放。</returns>
        public static bool PlayFoleyFromHit(Actor avatarActor, GameplayTag foleyTag, RayCastHit hit, float volume = 1f, bool bSpawnEffect = false)
        {
            if (avatarActor == null || !foleyTag.IsValid()) return false;

            var character = avatarActor.GetScript<NarrativeCharacter>();
            if (character != null)
            {
                return character.RequestFoleyFromHit(foleyTag, hit, null, volume, bSpawnEffect);
            }

            // 非角色 Actor（可破坏物、场景件）：在命中点做一次位置播放，表面仍由物理材质解析
            return NarrativeFoleyPlayback.PlayFoleyAtLocation(
                FoleyAudioBank.GetActiveBank(),
                foleyTag,
                NarrativePhysicalMaterial.GetSurface(hit.Material),
                hit.Point,
                volume,
                0.08f,
                bSpawnEffect,
                null);
        }

        /// <summary>
        /// ASC 版伤害侧 foley 入口：直接传受击方的 <see cref="NarrativePro.GAS.NarrativeAbilitySystemComponent"/>，
        /// 由 GAS 的伤害应用处一行调用（GAS 不引用角色模块，这里把角色解析藏在内部）。
        /// </summary>
        public static bool PlayFoleyFromHit(NarrativePro.GAS.NarrativeAbilitySystemComponent affectedASC, GameplayTag foleyTag, RayCastHit hit, float volume = 1f, bool bSpawnEffect = false)
        {
            if (affectedASC == null) return false;
            return PlayFoleyFromHit(affectedASC.GetAvatarOwner(), foleyTag, hit, volume, bSpawnEffect);
        }

        /// <summary>
        /// 由 CombatHitResult 触发的伤害侧 foley（GAS 的命中目标数据就是该结构，避免调用方手工拼 RayCastHit）。
        /// 表面取受击 Actor 的 foley 驱动所解析的脚下地面，取不到时按默认表面处理。
        /// </summary>
        public static bool PlayFoleyFromCombatHit(NarrativePro.GAS.NarrativeAbilitySystemComponent affectedASC, GameplayTag foleyTag, NarrativePro.GAS.CombatHitResult hitResult, float volume = 1f, bool bSpawnEffect = false)
        {
            if (affectedASC == null || hitResult == null || !foleyTag.IsValid()) return false;

            var avatar = affectedASC.GetAvatarOwner();
            if (avatar == null) return false;

            var character = avatar.GetScript<NarrativeCharacter>();
            if (character == null) return false;

            // 表面：优先用角色自带的 foley 驱动解脚下地面，退化到默认表面（与 UE5 SurfaceTypeDefault 一致）
            var driver = avatar.GetScript<NarrativeFootstepFoley>();
            NarrativePhysicalSurface surface = driver != null
                ? driver.ResolveGroundSurface(out _)
                : NarrativePhysicalSurface.Default;

            return NarrativeFoleyPlayback.PlayFoleyAtLocation(
                character.ResolveBank(),
                foleyTag,
                surface,
                hitResult.ImpactPoint,
                volume,
                0.08f,
                bSpawnEffect,
                character.GetFoleyRandom());
        }

        /// <summary>取角色的音效库：显式库 &gt; 角色身上的 <see cref="NarrativeFootstepFoley"/> 配置 &gt; 全局活动库。</summary>
        public static FoleyAudioBank ResolveBank(this NarrativeCharacter character, FoleyAudioBank bank = null)
        {
            if (bank != null) return bank;
            if (character?.Actor != null)
            {
                var driver = character.Actor.GetScript<NarrativeFootstepFoley>();
                if (driver != null && driver.Bank != null) return driver.Bank;
            }
            return FoleyAudioBank.GetActiveBank();
        }

        /// <summary>
        /// 取角色专用随机源（用 <see cref="NarrativeCharacter.CharacterRandomSeed"/> 播种，
        /// 让变体选择在同一角色上稳定且不与其他角色互相干扰）。
        /// </summary>
        public static System.Random GetFoleyRandom(this NarrativeCharacter character)
        {
            if (character == null) return null;

            if (_characterRandoms.TryGetValue(character, out var cached) && cached != null) return cached;

            var created = new System.Random(character.CharacterRandomSeed);
            _characterRandoms[character] = created;
            return created;
        }

        private static readonly Dictionary<NarrativeCharacter, System.Random> _characterRandoms = new Dictionary<NarrativeCharacter, System.Random>();
    }

    /// <summary>
    /// 移动驱动的脚部 foley 组件（UE5 脚步 Notify 在 Flax 侧的等价实现）。
    /// <para>
    /// 挂在角色 Actor 上（与 <see cref="NarrativeCharacter"/>、<see cref="NarrativeCharacterMovement"/> 同级）：
    /// 每帧累计水平位移，跨过 <see cref="FootstepDistance"/> 就向下射线取地面物理材质，
    /// 解析出表面类型后按移动状态（走/慢走/冲刺）播放库中的脚步事件；
    /// 移动模式切换时播放起跳/落地。
    /// 只有本地权威角色（本地玩家控制器 或 拥有模拟权威的 NPC 控制器）才会发声。
    /// </para>
    /// </summary>
    public class NarrativeFootstepFoley : Script
    {
        /// <summary>本角色使用的 foley 音效库；为空则回落到 <see cref="FoleyAudioBank.Active"/>。</summary>
        public FoleyAudioBank Bank;

        /// <summary>需要登记给伤害/表面查询使用的物理材质条目（可空）。</summary>
        public List<NarrativePhysicalMaterial> PhysicalMaterials = new List<NarrativePhysicalMaterial>();

        /// <summary>行走时多少位移触发一次脚步（世界单位，本工程 1u = 1cm）。</summary>
        public float FootstepDistance = 90f;

        /// <summary>冲刺时多少位移触发一次脚步。</summary>
        public float SprintFootstepDistance = 130f;

        /// <summary>起跳/落地 foley 是否启用。</summary>
        public bool bPlayJumpAndLand = true;

        /// <summary>是否播放库中配置的粒子特效（UE5 的 FoleyParticles 映射）。</summary>
        public bool bPlayEffects = true;

        /// <summary>脚步音量。</summary>
        public float FootstepVolume = 1f;

        /// <summary>地面探测：射线起点相对角色位置的抬升。</summary>
        public float GroundTraceUpOffset = 100f;

        /// <summary>地面探测：射线向下的最大距离。</summary>
        public float GroundTraceDistance = 220f;

        /// <summary>地面探测的层掩码（默认全部层）。</summary>
        public uint GroundTraceMask = uint.MaxValue;

        /// <summary>是否只在本地权威角色上发声（关闭后所有角色都会播，联机时不建议）。</summary>
        public bool bRequireLocalAuthority = true;

        /// <summary>当前是否静音（过场/对话时由 SetMuted 控制，只读便于调试）。</summary>
        public bool bIsMuted { get; private set; }

        private NarrativeCharacter _character;
        private NarrativeCharacterMovement _movement;
        private float _distanceAccumulator;
        private Vector3 _lastPosition;
        private bool _hasLastPosition;
        private NarrativeMovementMode _previousMode = NarrativeMovementMode.Walking;
        private NarrativeMovementMode _modeAtLastFrame = NarrativeMovementMode.Walking;
        private float _lastVerticalVelocity;
        private bool _authorityChecked;
        private bool _bHasAuthority;

        public override void OnEnable()
        {
            base.OnEnable();

            _character = Actor != null ? Actor.GetScript<NarrativeCharacter>() : null;
            _movement = Actor != null ? Actor.GetScript<NarrativeCharacterMovement>() : null;

            if (_movement == null)
            {
                NarrativeLog.LogWarning($"NarrativeFootstepFoley: {Actor?.Name} 上没有 NarrativeCharacterMovement，脚步组件将按纯位移判定。");
            }

            if (PhysicalMaterials != null && PhysicalMaterials.Count > 0)
            {
                NarrativePhysicalMaterial.Register(PhysicalMaterials);
            }

            if (Bank != null && FoleyAudioBank.Active == null)
            {
                FoleyAudioBank.Active = Bank;
            }

            _hasLastPosition = false;
            _distanceAccumulator = 0f;
            _authorityChecked = false;
            _previousMode = _movement != null ? _movement.MovementMode : NarrativeMovementMode.Walking;
            _modeAtLastFrame = _previousMode;
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            if (_character == null || Actor == null) return;
            if (bIsMuted) return;
            if (!_character.IsAlive()) return;
            if (_character.bIsRagdoll) return;
            if (bRequireLocalAuthority && !HasLocalAuthority()) return;

            Vector3 position = Actor.Position;
            float deltaTime = Time.DeltaTime;
            if (deltaTime <= 0f)
            {
                _lastPosition = position;
                _hasLastPosition = true;
                return;
            }

            Vector3 delta = _hasLastPosition ? position - _lastPosition : Vector3.Zero;
            _lastPosition = position;
            _hasLastPosition = true;

            float horizontalDistance = new Vector3(delta.X, 0f, delta.Z).Length;
            _lastVerticalVelocity = delta.Y / deltaTime;

            NarrativeMovementMode currentMode = _movement != null ? _movement.MovementMode : NarrativeMovementMode.Walking;

            // 起跳：Walking -> Falling 且此刻仍在向上运动
            if (bPlayJumpAndLand && _modeAtLastFrame == NarrativeMovementMode.Walking && currentMode == NarrativeMovementMode.Falling && _lastVerticalVelocity > 0f)
            {
                PlayFoleyAtFeet(NarrativeFoleyTags.Feet_Jump);
            }

            // 落地：Falling -> Walking
            if (bPlayJumpAndLand && _modeAtLastFrame == NarrativeMovementMode.Falling && currentMode == NarrativeMovementMode.Walking)
            {
                PlayFoleyAtFeet(NarrativeFoleyTags.Feet_Land);
            }

            _modeAtLastFrame = currentMode;
            _previousMode = currentMode;

            if (currentMode != NarrativeMovementMode.Walking && currentMode != NarrativeMovementMode.NavWalking)
            {
                _distanceAccumulator = 0f;
                return;
            }

            _distanceAccumulator += horizontalDistance;

            float stepDistance = (_movement != null && _movement.IsSprinting()) ? SprintFootstepDistance : FootstepDistance;
            if (stepDistance <= 0f) stepDistance = FootstepDistance;

            // 一步一响：累计位移跨过阈值就播一次（动画 Notify 缺位时的位移节拍）
            while (_distanceAccumulator >= stepDistance)
            {
                _distanceAccumulator -= stepDistance;
                PlayFoleyAtFeet(NarrativeFoleyTags.ForMovementState(_movement));
            }
        }

        /// <summary>
        /// 在当前地面表面上播放指定 foley 事件。
        /// 表面解析失败（没有物理材质）时按 <see cref="NarrativePhysicalSurface.Default"/> 处理，
        /// 与 UE5 无 SurfaceType 时退回 SurfaceTypeDefault 的行为一致。
        /// </summary>
        public bool PlayFoleyAtFeet(GameplayTag foleyTag)
        {
            if (!foleyTag.IsValid()) return false;

            NarrativePhysicalSurface surface = ResolveGroundSurface(out _);
            return _character.RequestFoley(foleyTag, surface, Bank, FootstepVolume, bPlayEffects);
        }

        /// <summary>显式指定表面的播放入口（例如命中检测已拿到物理材质时）。</summary>
        public bool PlayFoleyOnSurface(GameplayTag foleyTag, NarrativePhysicalSurface surface)
        {
            return _character.RequestFoley(foleyTag, surface, Bank, FootstepVolume, bPlayEffects);
        }

        /// <summary>静音/恢复（过场动画、对话镜头等场景使用）。</summary>
        public void SetMuted(bool bMuted)
        {
            bIsMuted = bMuted;
            if (bMuted) NarrativeFoleyPlayback.StopAttachedSource(Actor);
        }

        /// <summary>射线取脚下地面，返回其表面类型（hit 同时输出，供上层复用）。</summary>
        public NarrativePhysicalSurface ResolveGroundSurface(out RayCastHit groundHit)
        {
            groundHit = default;

            if (Actor == null) return NarrativePhysicalSurface.Default;

            Vector3 origin = Actor.Position + new Vector3(0f, GroundTraceUpOffset, 0f);
            if (!Physics.RayCast(origin, Vector3.Down, out var hit, GroundTraceDistance, GroundTraceMask, false))
            {
                return NarrativePhysicalSurface.Default;
            }

            groundHit = hit;
            return NarrativePhysicalMaterial.GetSurface(hit.Material);
        }

        /// <summary>
        /// 本地是否有权为该角色发声。
        /// 对应 UE5 的 <c>Role == Authority</c> 判定：本地玩家控制器操控的角色，
        /// 或拥有模拟权威的 NPC 控制器；其余（远端占位角色）不发声音。
        /// </summary>
        public bool HasLocalAuthority()
        {
            if (_authorityChecked) return _bHasAuthority;

            _bHasAuthority = EvaluateLocalAuthority();
            _authorityChecked = true;
            return _bHasAuthority;
        }

        /// <summary>
        /// 作废本地权威缓存，下一次判定重新求值。
        /// 引导可能在角色启用之后才给玩家角色补挂 <see cref="NarrativePro.UnrealFramework.NarrativePlayerController"/>，
        /// 此时缓存里的 false 会把脚步永久静音，因此挂接控制器后必须调用本方法。
        /// </summary>
        public void InvalidateAuthorityCache()
        {
            _authorityChecked = false;
            _bHasAuthority = false;
        }

        private bool EvaluateLocalAuthority()
        {
            if (_character == null) return false;
            if (_character.IsLocallyControlled()) return true;

            if (Actor != null)
            {
                var npcController = Actor.GetScript<NarrativeNPCController>();
                if (npcController != null) return npcController.HasAuthority();
            }

            return false;
        }
    }
}
