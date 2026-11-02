using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Audio
{
    /// <summary>
    /// Foley 播放辅助。对应 UE5 里 foley 库的消费端
    /// （UE5 用 <c>UGameplayStatics::SpawnEmitterAtLocation</c> + MetaSound / 附着式 AudioComponent；
    /// Flax 的等价物是 <see cref="AudioSource"/> Actor + <see cref="ParticleEffect"/> Actor）。
    /// <para>
    /// 两种播放形态：
    /// <list type="bullet">
    /// <item><description><see cref="PlayFoleyAtLocation"/>：一次性音源，播完即销毁（对应 PlaySoundAtLocation）。</description></item>
    /// <item><description><see cref="PlayFoleyAttached"/>：复用挂在角色上的常驻音源，随角色移动（对应 AttachSoundToActor，
    /// 脚步声这类高频事件走它，避免每步生成/销毁 Actor）。</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// 所有方法都必须在主线程（脚本生命周期 / OnUpdate）调用：Flax 的 Actor 与 GUI 同样只允许主线程操作。
    /// </para>
    /// </summary>
    public static class NarrativeFoleyPlayback
    {
        /// <summary>附着式音源的固定名字，用于在角色子 Actor 里复用查找。</summary>
        public const string AttachedSourceName = "NarrativeFoleySource";

        /// <summary>一次性音源的名字。</summary>
        public const string OneShotSourceName = "NarrativeFoleyOneShot";

        /// <summary>粒子特效的默认存活时间（秒）—— Flax 没有 Niagara 的自动回收，需要显式销毁。</summary>
        public const float DefaultEffectLifetime = 3f;

        private static readonly System.Random _pitchRandom = new System.Random();

        /// <summary>
        /// 库中是否存在该事件在该表面上的配置（声音或特效任一）。
        /// 对应 UE5 蓝图在播放前对 <c>FoleySounds</c> 映射的非空判定。
        /// </summary>
        public static bool HasFoley(FoleyAudioBank bank, GameplayTag foleyTag, NarrativePhysicalSurface surface)
        {
            var foley = bank?.GetFoley(foleyTag);
            if (foley == null) return false;
            return foley.GetSound(surface) != null || foley.GetEffect(surface) != null;
        }

        /// <summary>按 (事件, 表面) 解析一条随机声音变体。</summary>
        public static AudioClip ResolveSound(FoleyAudioBank bank, GameplayTag foleyTag, NarrativePhysicalSurface surface, System.Random rng = null)
        {
            return bank?.GetSound(foleyTag, surface, rng);
        }

        /// <summary>按 (事件, 表面) 解析一条随机粒子特效。</summary>
        public static ParticleSystem ResolveEffect(FoleyAudioBank bank, GameplayTag foleyTag, NarrativePhysicalSurface surface, System.Random rng = null)
        {
            return bank?.GetEffect(foleyTag, surface, rng);
        }

        /// <summary>
        /// 在世界坐标处播放一条 foley（一次性音源，播完自动回收）。
        /// </summary>
        /// <param name="bank">音效库，null 时使用 <see cref="FoleyAudioBank.GetActiveBank"/>。</param>
        /// <param name="foleyTag">foley 事件标签。</param>
        /// <param name="surface">命中的物理表面。</param>
        /// <param name="location">播放位置。</param>
        /// <param name="volume">基础音量。</param>
        /// <param name="pitchRange">音高随机幅度（0 表示不随机化）。</param>
        /// <param name="bSpawnEffect">是否同时生成库中配置的粒子特效。</param>
        /// <param name="rng">随机源（角色可传自己的种子 RNG 保证可复现）。</param>
        /// <returns>是否成功播放（库里没有该组合时返回 false）。</returns>
        public static bool PlayFoleyAtLocation(
            FoleyAudioBank bank,
            GameplayTag foleyTag,
            NarrativePhysicalSurface surface,
            Vector3 location,
            float volume = 1f,
            float pitchRange = 0.08f,
            bool bSpawnEffect = true,
            System.Random rng = null)
        {
            // 传 null 时先确保默认库已从磁盘 JSON 装载进 FoleyAudioBank.Active
            if (bank == null) FoleyAudioBankLoader.EnsureActive();
            bank ??= FoleyAudioBank.GetActiveBank();

            AudioClip clip = bank.GetSound(foleyTag, surface, rng);
            ParticleSystem effect = bSpawnEffect ? bank.GetEffect(foleyTag, surface, rng) : null;

            if (clip == null && effect == null) return false;

            if (clip != null)
            {
                var source = new AudioSource
                {
                    Name = OneShotSourceName,
                    Clip = clip,
                    Volume = volume,
                    Pitch = RandomizePitch(pitchRange, rng),
                    IsLooping = false,
                    PlayOnStart = true,
                    // 3D 衰减由位置决定（关掉空间化就变成 2D 提示音）
                    AllowSpatialization = true,
                };

                // 3D 世界音源必须显式生成进玩法场景（无父级版落 Scenes[0]=RootScene 且跨场景滞留）
                if (!NarrativePro.CommonUI.NarrativeRuntimeBootstrap.TrySpawnToGameplayScene(source, foleyTag))
                {
                    source.Position = location;
                    ScheduleOneShotCleanup(source, clip);
                }
                else
                {
                    NarrativeLog.LogWarning($"NarrativeFoleyPlayback: 一次性音源生成失败：{foleyTag} @ {location}");
                    return false;
                }
            }

            if (effect != null)
            {
                SpawnEffectAtLocation(effect, location);
            }

            return true;
        }

        /// <summary>
        /// 在指定 Actor（角色）身上播放一条 foley：复用其子 <see cref="AudioSource"/>，
        /// 音源随角色移动，3D 衰减自动跟随。对应 UE5 的 UAudioComponent 附着播放。
        /// </summary>
        /// <param name="bank">音效库，null 时使用 <see cref="FoleyAudioBank.GetActiveBank"/>。</param>
        /// <param name="foleyTag">foley 事件标签。</param>
        /// <param name="surface">物理表面。</param>
        /// <param name="owner">附着宿主（通常是角色 Actor）。</param>
        /// <param name="volume">音量。</param>
        /// <param name="pitchRange">音高随机幅度。</param>
        /// <param name="bSpawnEffect">是否在宿主脚底位置生成粒子特效。</param>
        /// <param name="effectOffset">特效相对宿主的偏移。</param>
        /// <param name="rng">随机源。</param>
        /// <returns>是否成功播放。</returns>
        public static bool PlayFoleyAttached(
            FoleyAudioBank bank,
            GameplayTag foleyTag,
            NarrativePhysicalSurface surface,
            Actor owner,
            float volume = 1f,
            float pitchRange = 0.08f,
            bool bSpawnEffect = true,
            Vector3 effectOffset = default,
            System.Random rng = null)
        {
            if (owner == null) return false;

            if (bank == null) FoleyAudioBankLoader.EnsureActive();
            bank ??= FoleyAudioBank.GetActiveBank();

            AudioClip clip = bank.GetSound(foleyTag, surface, rng);
            ParticleSystem effect = bSpawnEffect ? bank.GetEffect(foleyTag, surface, rng) : null;

            if (clip == null && effect == null) return false;

            if (clip != null)
            {
                var source = GetOrCreateAttachedSource(owner);
                if (source != null)
                {
                    // 重设 Clip 前必须停掉当前播放，否则 Flax 不会切换源数据
                    source.Stop();
                    source.Clip = clip;
                    source.Volume = volume;
                    source.Pitch = RandomizePitch(pitchRange, rng);
                    source.IsLooping = false;
                    source.Play();
                }
            }

            if (effect != null)
            {
                SpawnEffectAtLocation(effect, owner.Position + effectOffset);
            }

            return true;
        }

        /// <summary>
        /// 取宿主身上复用的 foley 音源；不存在时在宿主下新建一个子 <see cref="AudioSource"/>。
        /// </summary>
        public static AudioSource GetOrCreateAttachedSource(Actor owner)
        {
            if (owner == null) return null;

            var existing = FindAttachedSource(owner);
            if (existing != null) return existing;

            try
            {
                var source = owner.AddChild<AudioSource>();
                if (source != null)
                {
                    source.Name = AttachedSourceName;
                    source.IsLooping = false;
                    source.PlayOnStart = false;
                    source.AllowSpatialization = true;
                }
                return source;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"NarrativeFoleyPlayback: 为 {owner.Name} 创建附着音源失败：{ex.Message}");
                return null;
            }
        }

        /// <summary>在宿主的直接子 Actor 中查找 foley 音源。</summary>
        public static AudioSource FindAttachedSource(Actor owner)
        {
            if (owner == null || !owner.HasChildren) return null;

            var children = owner.Children;
            if (children == null) return null;

            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] is AudioSource source && source != null && source.Name == AttachedSourceName)
                {
                    return source;
                }
            }
            return null;
        }

        /// <summary>停止宿主当前的 foley 音源（角色死亡/离开视野时用，避免残留）。</summary>
        public static void StopAttachedSource(Actor owner)
        {
            FindAttachedSource(owner)?.Stop();
        }

        /// <summary>销毁宿主上的 foley 音源子 Actor（角色销毁时随宿主一起回收，这里只提供显式入口）。</summary>
        public static void DestroyAttachedSource(Actor owner)
        {
            var source = FindAttachedSource(owner);
            if (source != null)
            {
                source.Stop();
                FlaxEngine.Object.Destroy(source);
            }
        }

        /// <summary>
        /// 在世界坐标生成一个一次性粒子特效（对应 UE5 的 SpawnEmitterAtLocation）。
        /// Flax 的粒子宿主是 <see cref="ParticleEffect"/> Actor，需要手动定时回收。
        /// </summary>
        public static ParticleEffect SpawnEffectAtLocation(ParticleSystem system, Vector3 location, float lifetime = DefaultEffectLifetime)
        {
            if (system == null) return null;

            try
            {
                var effect = new ParticleEffect
                {
                    Name = "NarrativeFoleyEffect",
                    ParticleSystem = system,
                    IsLooping = false,
                    PlayOnStart = true,
                };

                if (NarrativePro.CommonUI.NarrativeRuntimeBootstrap.TrySpawnToGameplayScene(effect, "NarrativeFoleyEffect"))
                {
                    NarrativeLog.LogWarning($"NarrativeFoleyPlayback: 粒子特效生成失败：{system.Path}");
                    FlaxEngine.Object.Destroy(effect);
                    return null;
                }

                effect.Position = location;
                FlaxEngine.Object.Destroy(effect, lifetime > 0f ? lifetime : DefaultEffectLifetime);
                return effect;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"NarrativeFoleyPlayback: 粒子特效异常：{ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 一次性音源的回收：优先在 ClipFinished 事件里销毁，
        /// 同时按片段时长挂一个兜底延时（事件在极端情况下可能不回调，避免音源 Actor 泄漏）。
        /// </summary>
        private static void ScheduleOneShotCleanup(AudioSource source, AudioClip clip)
        {
            float length = clip != null ? Math.Max(clip.Length, 0.1f) : 1f;
            bool destroyed = false;

            if (clip != null)
            {
                source.ClipFinished += () =>
                {
                    if (destroyed) return;
                    destroyed = true;
                    FlaxEngine.Object.Destroy(source);
                };
            }

            FlaxEngine.Object.Destroy(source, length + 0.5f);
        }

        /// <summary>在 [1-range, 1+range] 内随机音高（增加变体听感差异）。</summary>
        private static float RandomizePitch(float range, System.Random rng)
        {
            if (range <= 0f) return 1f;
            var random = rng ?? _pitchRandom;
            return 1f + (float)(random.NextDouble() * 2.0 - 1.0) * range;
        }
    }
}
