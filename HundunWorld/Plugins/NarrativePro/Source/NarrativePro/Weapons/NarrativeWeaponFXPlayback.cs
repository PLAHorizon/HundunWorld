// NarrativePro —— 武器 FX 播放层（唯一 spawn 点）
//
// UE5 依据（只读参考工程 C:\Works\GitHubProjects\20260801\HundunWorld\Plugins\NarrativePro\Source\NarrativeArsenal）：
//   - Public/Weapons/NarrativeWeaponFX.h:23-25  "Handles spawning some FX ... decouple FX from any particular class"
//     —— UE5 把 spawn 实现放在 BP 子类；端口没有 BP，故把这条"通用 spawn"收敛到本类的单一入口。
//   - Public/Weapons/NarrativeWeaponFX.h:37-59  GetWorld() 经 Outer 链取世界后 SpawnEmitterAtLocation；
//     Flax 等价物是 Level.SpawnActor + Actor.Position（见下"端口差异"）。
//
// 端口差异：
//   - 粒子：Flax 无 Niagara / SpawnEmitterAtLocation。已核 FlaxEngine.CSharp.xml：
//     宿主是 `ParticleEffect` Actor（属性 ParticleSystem / IsLooping / PlayOnStart，方法 Play/Stop），
//     资产是 `ParticleSystem`（Spawn(...) 重载存在，但为减少 Actor 生命周期歧义，这里统一用
//     new ParticleEffect + Level.SpawnActor 的仓库既有做法，见 Audio/NarrativeFoleyPlayback.cs:253-283）。
//     Level.SpawnActor 返回 true 才是失败；销毁用 FlaxEngine.Object.Destroy(actor, seconds)。
//   - 音频：Flax 无 MetaSound / UAudioComponent。用 `AudioSource` Actor（Clip/Volume/Pitch/PlayOnStart，
//     ClipFinished 事件）做一次性播放，与 NarrativeFoleyPlayback 的一次性音源完全同构。
//   - 持久型 slot（Trail）：UE5 靠 Niagara Ribbon 的 activate/deactivate；Flax 无对应，
//     这里用"挂到宿主 Actor 下、显式销毁"的等价生命周期（activate 生成子 ParticleEffect，
//     deactivate 停并 Destroy）。owner 用引用键登记表管理，避免同宿主同 slot 重复生成。

using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Weapons
{
    /// <summary>
    /// 武器 FX 的实际生成/销毁入口。WeaponVisual / FirearmWeaponVisual 通过它消费 FX 资产。
    /// </summary>
    public static class NarrativeWeaponFXPlayback
    {
        /// <summary>一次性粒子缺省存活时长（秒）—— slot 未配 autoDestroySeconds 时使用。</summary>
        public const float DefaultOneShotLifetime = 1.5f;

        /// <summary>持久型 FX 登记表键：宿主 owner + slot。</summary>
        private struct PersistentKey
        {
            public object Owner;
            public EWeaponFXSlot Slot;
            public override int GetHashCode() =>
                unchecked((Owner != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Owner) : 0) * 397 + (int)Slot);
            public override bool Equals(object obj) =>
                obj is PersistentKey k && ReferenceEquals(k.Owner, Owner) && k.Slot == Slot;
        }

        private static readonly Dictionary<PersistentKey, ParticleEffect> _activePersistent =
            new Dictionary<PersistentKey, ParticleEffect>();

        private static readonly Dictionary<string, ParticleSystem> _particleCache =
            new Dictionary<string, ParticleSystem>(StringComparer.Ordinal);
        private static readonly Dictionary<string, AudioClip> _audioCache =
            new Dictionary<string, AudioClip>(StringComparer.Ordinal);

        /// <summary>
        /// 播放一次性 FX（粒子 + 音）。<paramref name="worldAttach"/> 已是最终世界变换
        /// （调用方用 socket 解析 + slot.Socket 偏移组合好，见 WeaponVisual.ResolveFXWorldTransform）。
        /// </summary>
        public static void PlayOneShot(NarrativeWeaponFXSlotEntry entry, Transform worldAttach)
        {
            if (entry == null) return;
            ResolveAssets(entry);

            if (entry.ResolvedParticle != null)
            {
                SpawnOneShotParticle(entry, worldAttach);
            }
            if (entry.ResolvedAudio != null)
            {
                SpawnOneShotAudio(entry, worldAttach.Translation);
            }
        }

        /// <summary>
        /// 激活持久型 FX（如拖尾）：作为 <paramref name="parent"/> 的子 Actor 生成并循环，随宿主移动。
        /// 同 owner + 同 slot 已激活时不重复生成。
        /// </summary>
        public static void ActivatePersistent(object owner, NarrativeWeaponFXSlotEntry entry, Actor parent, Transform localAttach)
        {
            if (owner == null || entry == null || parent == null) return;
            ResolveAssets(entry);
            if (entry.ResolvedParticle == null) return;

            var key = new PersistentKey { Owner = owner, Slot = entry.Slot };
            if (_activePersistent.ContainsKey(key)) return;

            var effect = parent.AddChild<ParticleEffect>();
            if (effect == null) return;
            effect.Name = $"WeaponFX_{entry.Slot}";
            effect.ParticleSystem = entry.ResolvedParticle;
            effect.IsLooping = true;
            effect.PlayOnStart = true;
            effect.LocalTransform = localAttach;
            effect.Scale = Vector3.Multiply(entry.Socket.Scale, effect.Scale);
            effect.Play();
            _activePersistent[key] = effect;
        }

        /// <summary>反激活持久型 FX：停并销毁登记的子 Actor。</summary>
        public static void DeactivatePersistent(object owner, EWeaponFXSlot slot)
        {
            if (owner == null) return;
            var key = new PersistentKey { Owner = owner, Slot = slot };
            if (!_activePersistent.TryGetValue(key, out var effect)) return;
            _activePersistent.Remove(key);
            if (effect == null) return;
            effect.Stop();
            FlaxEngine.Object.Destroy(effect);
        }

        /// <summary>反激活某宿主的全部持久型 FX（武器收起/视觉销毁时调用，防残留）。</summary>
        public static void DeactivateAllFor(object owner)
        {
            if (owner == null) return;
            var toRemove = new List<PersistentKey>();
            foreach (var kvp in _activePersistent)
            {
                if (ReferenceEquals(kvp.Key.Owner, owner)) toRemove.Add(kvp.Key);
            }
            foreach (var key in toRemove)
            {
                if (_activePersistent.TryGetValue(key, out var effect))
                {
                    if (effect != null)
                    {
                        effect.Stop();
                        FlaxEngine.Object.Destroy(effect);
                    }
                }
                _activePersistent.Remove(key);
            }
        }

        private static void SpawnOneShotParticle(NarrativeWeaponFXSlotEntry entry, Transform worldAttach)
        {
            try
            {
                var effect = new ParticleEffect
                {
                    Name = $"WeaponFX_{entry.Slot}",
                    ParticleSystem = entry.ResolvedParticle,
                    IsLooping = false,
                    PlayOnStart = true,
                };
                // 世界特效须显式生成进玩法场景；Flax 约定 SpawnActor 返回 true 表示失败
                if (NarrativePro.CommonUI.NarrativeRuntimeBootstrap.TrySpawnToGameplayScene(effect, effect.Name))
                {
                    FlaxEngine.Object.Destroy(effect);
                    return;
                }
                effect.Transform = worldAttach;
                effect.Scale = entry.Socket.Scale;
                float life = entry.AutoDestroySeconds > 0f ? entry.AutoDestroySeconds : DefaultOneShotLifetime;
                FlaxEngine.Object.Destroy(effect, life);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"[WeaponFX] 粒子生成异常 {entry.Slot}: {ex.Message}");
            }
        }

        private static void SpawnOneShotAudio(NarrativeWeaponFXSlotEntry entry, Vector3 location)
        {
            try
            {
                var source = new AudioSource
                {
                    Name = $"WeaponFXAudio_{entry.Slot}",
                    Clip = entry.ResolvedAudio,
                    Volume = entry.Volume,
                    Pitch = entry.Pitch,
                    IsLooping = false,
                    PlayOnStart = true,
                    AllowSpatialization = true,
                };
                // 3D 世界音源须显式生成进玩法场景，不靠"无父级默认落 RootScene"
                if (!NarrativePro.CommonUI.NarrativeRuntimeBootstrap.TrySpawnToGameplayScene(source, source.Name))
                {
                    source.Position = location;
                    float length = Math.Max(entry.ResolvedAudio.Length, 0.1f);
                    FlaxEngine.Object.Destroy(source, length + 0.5f);
                }
                else
                {
                    FlaxEngine.Object.Destroy(source);
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"[WeaponFX] 音频生成异常 {entry.Slot}: {ex.Message}");
            }
        }

        /// <summary>惰性解析并缓存 slot 引用的资产（缺资产只告警一次，之后走 null 快速路径）。</summary>
        private static void ResolveAssets(NarrativeWeaponFXSlotEntry entry)
        {
            if (entry == null || entry.bAssetsResolved) return;
            entry.bAssetsResolved = true;

            if (!string.IsNullOrEmpty(entry.ParticleSystemPath))
                entry.ResolvedParticle = LoadParticle(entry.ParticleSystemPath);
            if (!string.IsNullOrEmpty(entry.AudioClipPath))
                entry.ResolvedAudio = LoadAudio(entry.AudioClipPath);
        }

        private static ParticleSystem LoadParticle(string path)
        {
            if (_particleCache.TryGetValue(path, out var cached)) return cached;
            ParticleSystem asset = null;
            try { asset = Content.Load<ParticleSystem>(path); }
            catch (Exception ex) { NarrativeLog.LogWarning($"[WeaponFX] 粒子资产 '{path}' 加载异常: {ex.Message}"); }
            if (asset == null) NarrativeLog.LogWarning($"[WeaponFX] 粒子资产不存在或加载失败: '{path}'（slot 跳过）。");
            _particleCache[path] = asset;
            return asset;
        }

        private static AudioClip LoadAudio(string path)
        {
            if (_audioCache.TryGetValue(path, out var cached)) return cached;
            AudioClip asset = null;
            try { asset = Content.Load<AudioClip>(path); }
            catch (Exception ex) { NarrativeLog.LogWarning($"[WeaponFX] 音频资产 '{path}' 加载异常: {ex.Message}"); }
            if (asset == null) NarrativeLog.LogWarning($"[WeaponFX] 音频资产不存在或加载失败: '{path}'（slot 跳过）。");
            _audioCache[path] = asset;
            return asset;
        }
    }
}
