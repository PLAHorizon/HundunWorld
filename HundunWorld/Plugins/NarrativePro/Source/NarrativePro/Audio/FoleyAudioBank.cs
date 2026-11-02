using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Audio
{
    /// <summary>
    /// 单一表面下的声音变体集合。
    /// 对应 UE5 <c>FNarrativeFoleySound::FoleySounds</c> 里某个 <c>EPhysicalSurface</c> key 的 value
    /// （UE5 那里 key 对应一个 USoundBase —— 变体随机交给 MetaSound；
    /// Flax 没有 MetaSound，所以这里直接允许配多条 <see cref="AudioClip"/> 变体并随机取一条）。
    /// </summary>
    [Serializable]
    public class FoleySurfaceSoundEntry
    {
        /// <summary>该条目服务的物理表面。</summary>
        public NarrativePhysicalSurface Surface = NarrativePhysicalSurface.Default;

        /// <summary>该表面下的声音变体（随机选取其一，等价 UE5 的 MetaSound 变体层）。</summary>
        public List<AudioClip> Sounds = new List<AudioClip>();

        /// <summary>随机取一条变体；没有配置返回 null。</summary>
        public AudioClip PickRandom(System.Random rng = null)
        {
            if (Sounds == null || Sounds.Count == 0) return null;

            // 过滤掉未配置（null）的槽位，避免随机命中空clip导致静音
            int count = 0;
            AudioClip first = null;
            for (int i = 0; i < Sounds.Count; i++)
            {
                if (Sounds[i] == null) continue;
                if (first == null) first = Sounds[i];
                count++;
            }
            if (count == 0) return null;
            if (count == 1) return first;

            int index = (rng ?? FoleyAudioBank.Random).Next(Sounds.Count);
            // 随机命中空槽时向后顺延到第一条有效变体
            for (int step = 0; step < Sounds.Count; step++)
            {
                int candidate = (index + step) % Sounds.Count;
                if (Sounds[candidate] != null) return Sounds[candidate];
            }
            return first;
        }
    }

    /// <summary>
    /// 单一表面下的粒子特效变体集合。
    /// 对应 UE5 <c>FNarrativeFoleySound::FoleyParticles</c>（TMap&lt;EPhysicalSurface, UNiagaraSystem*&gt;）。
    /// Flax 的等价资产是 <see cref="ParticleSystem"/>（由 <see cref="ParticleEffect"/> Actor 播放）。
    /// </summary>
    [Serializable]
    public class FoleySurfaceEffectEntry
    {
        /// <summary>该条目服务的物理表面。</summary>
        public NarrativePhysicalSurface Surface = NarrativePhysicalSurface.Default;

        /// <summary>该表面下的粒子特效变体。</summary>
        public List<ParticleSystem> Effects = new List<ParticleSystem>();

        /// <summary>随机取一条特效变体；没有配置返回 null。</summary>
        public ParticleSystem PickRandom(System.Random rng = null)
        {
            if (Effects == null || Effects.Count == 0) return null;

            int index = (rng ?? FoleyAudioBank.Random).Next(Effects.Count);
            for (int step = 0; step < Effects.Count; step++)
            {
                int candidate = (index + step) % Effects.Count;
                if (Effects[candidate] != null) return Effects[candidate];
            }
            return null;
        }
    }

    /// <summary>
    /// 一个 foley 事件在各表面上的声音/特效配置。对应 UE5 <c>FNarrativeFoleySound</c>。
    /// </summary>
    [Serializable]
    public class NarrativeFoleySound
    {
        /// <summary>表面 → 声音变体（对应 UE5 FoleySounds TMap）。</summary>
        public List<FoleySurfaceSoundEntry> FoleySounds = new List<FoleySurfaceSoundEntry>();

        /// <summary>表面 → 粒子特效变体（对应 UE5 FoleyParticles TMap）。</summary>
        public List<FoleySurfaceEffectEntry> FoleyParticles = new List<FoleySurfaceEffectEntry>();

        public NarrativeFoleySound() { }

        /// <summary>取指定表面的声音条目；不存在返回 null。</summary>
        public FoleySurfaceSoundEntry GetSoundEntry(NarrativePhysicalSurface surface)
        {
            if (FoleySounds == null) return null;
            for (int i = 0; i < FoleySounds.Count; i++)
            {
                if (FoleySounds[i] != null && FoleySounds[i].Surface == surface) return FoleySounds[i];
            }
            return null;
        }

        /// <summary>取指定表面的特效条目；不存在返回 null。</summary>
        public FoleySurfaceEffectEntry GetParticleEntry(NarrativePhysicalSurface surface)
        {
            if (FoleyParticles == null) return null;
            for (int i = 0; i < FoleyParticles.Count; i++)
            {
                if (FoleyParticles[i] != null && FoleyParticles[i].Surface == surface) return FoleyParticles[i];
            }
            return null;
        }

        /// <summary>
        /// 按表面随机取一条声音变体。等价 UE5 的 <c>FoleySounds.Find(SurfaceType)</c> + MetaSound 变体。
        /// 表面没有配置时退回 <see cref="NarrativePhysicalSurface.Default"/>（UE5 的 SurfaceTypeDefault 兜底）。
        /// </summary>
        public AudioClip GetSound(NarrativePhysicalSurface surface, System.Random rng = null)
        {
            var entry = GetSoundEntry(surface);
            if (entry == null && surface != NarrativePhysicalSurface.Default)
            {
                entry = GetSoundEntry(NarrativePhysicalSurface.Default);
            }
            return entry?.PickRandom(rng);
        }

        /// <summary>按表面随机取一条粒子特效；表面缺失时退回 Default 表面。</summary>
        public ParticleSystem GetEffect(NarrativePhysicalSurface surface, System.Random rng = null)
        {
            var entry = GetParticleEntry(surface);
            if (entry == null && surface != NarrativePhysicalSurface.Default)
            {
                entry = GetParticleEntry(NarrativePhysicalSurface.Default);
            }
            return entry?.PickRandom(rng);
        }

        /// <summary>为指定表面追加一条声音变体（没有该表面条目时自动建立）。</summary>
        public FoleySurfaceSoundEntry AddSound(NarrativePhysicalSurface surface, AudioClip sound)
        {
            if (sound == null) return GetSoundEntry(surface);

            var entry = GetSoundEntry(surface);
            if (entry == null)
            {
                entry = new FoleySurfaceSoundEntry { Surface = surface };
                FoleySounds.Add(entry);
            }
            entry.Sounds.Add(sound);
            return entry;
        }

        /// <summary>为指定表面追加一条粒子特效变体（没有该表面条目时自动建立）。</summary>
        public FoleySurfaceEffectEntry AddEffect(NarrativePhysicalSurface surface, ParticleSystem effect)
        {
            if (effect == null) return GetParticleEntry(surface);

            var entry = GetParticleEntry(surface);
            if (entry == null)
            {
                entry = new FoleySurfaceEffectEntry { Surface = surface };
                FoleyParticles.Add(entry);
            }
            entry.Effects.Add(effect);
            return entry;
        }
    }

    /// <summary>
    /// gameplay tag → foley 配置 的映射条目（Flax 序列化不支持 Dictionary，按 TaggedMusicSet 的做法用 List）。
    /// </summary>
    [Serializable]
    public class FoleyTagEntry
    {
        /// <summary>foley 事件标签（例如 "Foley.Event.feet.Walk"）。</summary>
        public GameplayTag FoleyTag;

        /// <summary>该事件在各表面上的声音/特效。</summary>
        public NarrativeFoleySound Sound = new NarrativeFoleySound();
    }

    /// <summary>
    /// Foley 音效库。对应 UE5 <c>UFoleyAudioBank</c>（"GASPs audio bank 的增强版 —— 加入表面类型"）：
    /// 以 gameplay tag 索引 <see cref="NarrativeFoleySound"/>，后者再以物理表面索引声音与粒子。
    /// <para>
    /// UE5 里它是 UDataAsset；Flax 1.12 没有可供脚本继承的 DataAsset/ScriptableObject
    /// （已核对 FlaxEngine.CSharp.dll：无 FlaxEngine.ScriptableObject 类型），
    /// 因此与 <see cref="NarrativePro.Music.TaggedMusicSet"/> 保持一致，用 [Serializable] 数据类，
    /// 直接作为脚本的公共字段在场景/预制体里配置，也可由代码构造后设为 <see cref="Active"/>。
    /// </para>
    /// </summary>
    [Serializable]
    public class FoleyAudioBank
    {
        /// <summary>共享随机源（变体选择用）。对应 UE5 的 FMath::Rand。</summary>
        internal static readonly System.Random Random = new System.Random();

        /// <summary>
        /// 事件 → foley 配置（对应 UE5 TMap&lt;FGameplayTag, FNarrativeFoleySound&gt; FoleySounds）。
        /// </summary>
        public List<FoleyTagEntry> FoleySounds = new List<FoleyTagEntry>();

        /// <summary>
        /// 是否允许父标签兜底。UE5 用 TMap::Find 做精确查找，因此默认 false（精确匹配）。
        /// 打开后，"Foley.Event.feet.Walk.Run" 没配置时会退回 "Foley.Event.feet"。
        /// </summary>
        public bool bAllowParentTagFallback = false;

        /// <summary>运行时查找表（Entries 变化后由 RebuildLookup 刷新）。</summary>
        [NonSerialized]
        private Dictionary<GameplayTag, NarrativeFoleySound> _lookup;

        [NonSerialized]
        private bool _lookupBuilt;

        // ===== 全局活动库（对应 UE5 蓝图经 I_FoleyAudioBankInterface 拿到库的用法） =====

        private static FoleyAudioBank _active;

        /// <summary>当前全局音效库。角色没有自带库时使用它。</summary>
        public static FoleyAudioBank Active
        {
            get => _active;
            set => _active = value;
        }

        /// <summary>获取当前活动库（没有则返回空库，保证调用方拿到非 null 对象）。</summary>
        public static FoleyAudioBank GetActiveBank()
        {
            if (_active == null) _active = new FoleyAudioBank();
            return _active;
        }

        private void EnsureLookupBuilt()
        {
            if (_lookupBuilt) return;

            _lookup = new Dictionary<GameplayTag, NarrativeFoleySound>();
            if (FoleySounds != null)
            {
                for (int i = 0; i < FoleySounds.Count; i++)
                {
                    var entry = FoleySounds[i];
                    if (entry == null || !entry.FoleyTag.IsValid() || entry.Sound == null) continue;
                    _lookup[entry.FoleyTag] = entry.Sound;
                }
            }
            _lookupBuilt = true;
        }

        /// <summary>重建查找表（外部改动 <see cref="FoleySounds"/> 后调用）。</summary>
        public void RebuildLookup()
        {
            _lookupBuilt = false;
            EnsureLookupBuilt();
        }

        /// <summary>
        /// 取某个 foley 事件的配置。对应 UE5 的 <c>FoleySounds.Find(FoleyEvent)</c>（精确匹配）。
        /// 未命中且开启 <see cref="bAllowParentTagFallback"/> 时逐级向父标签回溯。
        /// </summary>
        public NarrativeFoleySound GetFoley(GameplayTag foleyTag)
        {
            if (!foleyTag.IsValid()) return null;
            EnsureLookupBuilt();
            if (_lookup == null) return null;

            if (_lookup.TryGetValue(foleyTag, out var exact)) return exact;

            if (bAllowParentTagFallback)
            {
                string name = foleyTag.TagName;
                while (true)
                {
                    int lastDot = name.LastIndexOf('.');
                    if (lastDot <= 0) break;
                    name = name.Substring(0, lastDot);
                    if (_lookup.TryGetValue(new GameplayTag(name), out var parent)) return parent;
                }
            }
            return null;
        }

        /// <summary>是否存在指定事件的配置。</summary>
        public bool HasFoley(GameplayTag foleyTag) => GetFoley(foleyTag) != null;

        /// <summary>
        /// 按 (表面, 事件标签) 取一条随机声音变体。这是 UE5 版本的等价查询入口。
        /// </summary>
        public AudioClip GetSound(GameplayTag foleyTag, NarrativePhysicalSurface surface, System.Random rng = null)
        {
            return GetFoley(foleyTag)?.GetSound(surface, rng);
        }

        /// <summary>按 (表面, 事件标签) 取一条随机粒子特效。</summary>
        public ParticleSystem GetEffect(GameplayTag foleyTag, NarrativePhysicalSurface surface, System.Random rng = null)
        {
            return GetFoley(foleyTag)?.GetEffect(surface, rng);
        }

        /// <summary>
        /// 为事件的指定表面追加一条声音变体（条目不存在时自动创建）。
        /// </summary>
        public NarrativeFoleySound AddSound(GameplayTag foleyTag, NarrativePhysicalSurface surface, AudioClip sound)
        {
            var foley = GetOrCreateFoley(foleyTag);
            foley.AddSound(surface, sound);
            RebuildLookup();
            return foley;
        }

        /// <summary>为事件的指定表面追加一条粒子特效变体。</summary>
        public NarrativeFoleySound AddEffect(GameplayTag foleyTag, NarrativePhysicalSurface surface, ParticleSystem effect)
        {
            var foley = GetOrCreateFoley(foleyTag);
            foley.AddEffect(surface, effect);
            RebuildLookup();
            return foley;
        }

        /// <summary>取（或建）某个事件标签的 foley 配置。</summary>
        public NarrativeFoleySound GetOrCreateFoley(GameplayTag foleyTag)
        {
            if (!foleyTag.IsValid())
            {
                NarrativeLog.LogWarning("FoleyAudioBank.GetOrCreateFoley: 标签无效，已忽略");
                return null;
            }

            EnsureLookupBuilt();
            if (_lookup != null && _lookup.TryGetValue(foleyTag, out var existing)) return existing;

            var created = new NarrativeFoleySound();
            FoleySounds ??= new List<FoleyTagEntry>();
            FoleySounds.Add(new FoleyTagEntry { FoleyTag = foleyTag, Sound = created });
            _lookupBuilt = false;
            EnsureLookupBuilt();
            return created;
        }
    }
}
