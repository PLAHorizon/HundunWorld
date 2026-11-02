using System;
using System.Collections.Generic;
using System.IO;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Data;
using NarrativePro.Items;

namespace NarrativePro.Audio
{
    /// <summary>
    /// FoleyBank_Default.json 的 JSON 视图（加载 DTO）。
    /// <para>
    /// 之所以不直接反序列化 <see cref="FoleyAudioBank"/>：库里的运行时槽位是引擎资产
    /// （<see cref="AudioClip"/> / <see cref="ParticleSystem"/>），System.Text.Json 只会造出
    /// 空壳资产对象；资产必须经 <see cref="Content.Load{T}"/> 按路径取。因此磁盘格式用
    /// 字符串路径 + 表面名，装载时逐个解析成真实资产（取不到只告警、不抛）。
    /// </para>
    /// </summary>
    [Serializable]
    public class FoleyBankAssetJson
    {
        public string bankName;
        public bool bAllowParentTagFallback;
        public List<FoleyTagEntryJson> entries = new List<FoleyTagEntryJson>();
    }

    /// <summary>一个 foley 事件标签下的声音/特效配置（surface × tag → sound）。</summary>
    [Serializable]
    public class FoleyTagEntryJson
    {
        /// <summary>foley 事件标签，如 "Foley.Event.feet.Walk"。</summary>
        public string foleyTag;

        /// <summary>表面 → 声音变体路径。</summary>
        public List<FoleySurfaceSlotsJson> sounds = new List<FoleySurfaceSlotsJson>();

        /// <summary>表面 → 粒子特效路径（可省略）。</summary>
        public List<FoleySurfaceSlotsJson> effects = new List<FoleySurfaceSlotsJson>();
    }

    /// <summary>单个表面上的声音/特效槽位。</summary>
    [Serializable]
    public class FoleySurfaceSlotsJson
    {
        /// <summary>表面名（"Grass"/"Stone"/"Surface.Wood"/"SurfaceType3" 均可）。</summary>
        public string surface;

        /// <summary>Flax 资产引用路径列表（AudioClip 或 ParticleSystem）。</summary>
        public List<string> clips = new List<string>();
    }

    /// <summary>
    /// Foley 音效库的 JSON 装载器（对应审计缺口：FoleyAudioBank 此前既无加载器也无磁盘资产）。
    /// 磁盘资产：<c>Content/NarrativePro/Audio/FoleyBank_Default.json</c>。
    /// 加载结果写入 <see cref="FoleyAudioBank.Active"/>，
    /// <see cref="NarrativeFoleyPlayback"/> 的 bank==null 兜底路径即可消费。
    /// </summary>
    public static class FoleyAudioBankLoader
    {
        /// <summary>默认库的磁盘 JSON（工程相对路径，经 NarrativeDataPaths 解析）。</summary>
        public const string DefaultBankJsonPath = "Content/NarrativePro/Audio/FoleyBank_Default.json";

        private static bool _bLoadedOnce;

        /// <summary>
        /// 幂等地把默认 foley 库存进 <see cref="FoleyAudioBank.Active"/>。
        /// 已被运行时（角色/场景脚本）显式设置过 Active 时不覆盖。
        /// </summary>
        public static FoleyAudioBank EnsureActive(bool force = false)
        {
            if (FoleyAudioBank.Active != null && !force) return FoleyAudioBank.Active;
            if (_bLoadedOnce && !force) return FoleyAudioBank.Active;
            _bLoadedOnce = true;

            var bank = LoadFromFile(NarrativeDataPaths.Resolve(DefaultBankJsonPath));
            if (bank == null)
            {
                NarrativeLog.LogError(
                    $"[Audio] Foley 音效库缺失或加载失败: '{NarrativeDataPaths.Resolve(DefaultBankJsonPath)}'。" +
                    "脚步声等 foley 播放将全部落空。Has it been renamed or moved?");
                bank = new FoleyAudioBank();
            }

            FoleyAudioBank.Active = bank;
            return bank;
        }

        /// <summary>从 JSON 文件构建音效库；文件不存在/解析失败返回 null。</summary>
        public static FoleyAudioBank LoadFromFile(string jsonFilePath)
        {
            string resolved = NarrativeDataPaths.Resolve(jsonFilePath);
            if (string.IsNullOrEmpty(resolved) || !File.Exists(resolved))
                return null;

            try
            {
                string json = File.ReadAllText(resolved);
                var asset = System.Text.Json.JsonSerializer.Deserialize<FoleyBankAssetJson>(json,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                        AllowTrailingCommas = true,
                        IncludeFields = true
                    });
                if (asset == null) return null;

                return BuildBank(asset, resolved);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"[Audio] 加载 foley 音效库 '{resolved}' 失败: {ex.Message}");
                return null;
            }
        }

        private static FoleyAudioBank BuildBank(FoleyBankAssetJson asset, string sourcePath)
        {
            var bank = new FoleyAudioBank
            {
                bAllowParentTagFallback = asset.bAllowParentTagFallback
            };

            if (asset.entries == null)
            {
                bank.RebuildLookup();
                return bank;
            }

            int loadedClips = 0;
            foreach (var entry in asset.entries)
            {
                if (entry == null) continue;
                var tag = new GameplayTag(entry.foleyTag ?? "");
                if (!tag.IsValid())
                {
                    NarrativeLog.LogWarning($"[Audio] foley 条目缺少有效标签，已跳过（来源 {sourcePath}）。");
                    continue;
                }

                var foley = bank.GetOrCreateFoley(tag);
                if (foley == null) continue;

                if (entry.sounds != null)
                {
                    foreach (var slot in entry.sounds)
                        loadedClips += LoadSurfaceSlots(bank, tag, slot, isSound: true);
                }
                if (entry.effects != null)
                {
                    foreach (var slot in entry.effects)
                        LoadSurfaceSlots(bank, tag, slot, isSound: false);
                }
            }

            bank.RebuildLookup();
            NarrativeLog.Log($"[Audio] Foley 音效库已从 {sourcePath} 加载：{asset.entries.Count} 个事件、{loadedClips} 条声音变体。");
            return bank;
        }

        private static int LoadSurfaceSlots(FoleyAudioBank bank, GameplayTag tag, FoleySurfaceSlotsJson slot, bool isSound)
        {
            if (slot == null || slot.clips == null) return 0;
            if (!NarrativePhysicalSurfaceUtility.TryFromName(slot.surface, out var surface))
            {
                NarrativeLog.LogWarning($"[Audio] foley '{tag}' 无法识别表面 '{slot.surface}'，按 Default 处理。");
                surface = NarrativePhysicalSurface.Default;
            }

            int loaded = 0;
            foreach (var path in slot.clips)
            {
                if (string.IsNullOrEmpty(path)) continue;

                if (isSound)
                {
                    var clip = LoadAsset<AudioClip>(path);
                    if (clip != null)
                    {
                        bank.AddSound(tag, surface, clip);
                        loaded++;
                    }
                }
                else
                {
                    var fx = LoadAsset<ParticleSystem>(path);
                    if (fx != null)
                    {
                        bank.AddEffect(tag, surface, fx);
                        loaded++;
                    }
                }
            }
            return loaded;
        }

        private static T LoadAsset<T>(string assetPath) where T : Asset
        {
            try
            {
                var asset = Content.Load<T>(assetPath);
                if (asset == null)
                    NarrativeLog.LogWarning($"[Audio] 资产不存在或加载失败: '{assetPath}'（foley 槽位跳过）。");
                return asset;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"[Audio] 资产 '{assetPath}' 加载异常: {ex.Message}");
                return null;
            }
        }
    }
}
