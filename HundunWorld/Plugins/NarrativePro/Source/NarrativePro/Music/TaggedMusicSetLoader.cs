using System;
using System.Collections.Generic;
using System.IO;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Data;
using NarrativePro.Items;

namespace NarrativePro.Music
{
    /// <summary>
    /// 音乐集磁盘 JSON 的加载 DTO。运行时槽位是引擎资产（<see cref="AudioClip"/>），
    /// 必须经 <see cref="Content.Load{T}"/> 按路径解析，因此磁盘格式用字符串路径。
    /// </summary>
    [Serializable]
    public class MusicSetAssetJson
    {
        public string setName;
        public List<MusicSetEntryJson> entries = new List<MusicSetEntryJson>();
    }

    /// <summary>一个主题（gameplay tag）下的音轨集合。</summary>
    [Serializable]
    public class MusicSetEntryJson
    {
        /// <summary>主题标签，如 "Music.Ambient"。</summary>
        public string theme;

        /// <summary>主题下的音轨。</summary>
        public List<MusicTrackJson> tracks = new List<MusicTrackJson>();
    }

    /// <summary>单条音轨。</summary>
    [Serializable]
    public class MusicTrackJson
    {
        /// <summary>AudioClip 的 Flax 资产引用路径。</summary>
        public string musicPath;

        public float fadeInDuration = 3.0f;
        public float fadeOutDuration = 3.0f;
    }

    /// <summary>
    /// <see cref="TaggedMusicSet"/> 的 JSON 加载器。对应审计缺口：UE5 里它是 UDataAsset 资产引用，
    /// Flax 端口此前既没有磁盘资产也没有装载入口，音乐子系统永远拿到 null。
    /// 磁盘资产：<c>Content/NarrativePro/Music/MusicSet_Default.json</c>；
    /// <see cref="NarrativeMusicSubsystem.OnEnable"/> 在 Inspector 未配置时兜底取 <see cref="DefaultSet"/>。
    /// </summary>
    public static class TaggedMusicSetLoader
    {
        /// <summary>默认音乐集 JSON 的工程相对路径。</summary>
        public const string DefaultSetJsonPath = "Content/NarrativePro/Music/MusicSet_Default.json";

        private static TaggedMusicSet _defaultSet;
        private static bool _bLoadedOnce;

        /// <summary>
        /// 默认音乐集（懒加载一次；资产缺失时为空集合并写 Error，保证调用方非 null）。
        /// </summary>
        public static TaggedMusicSet DefaultSet
        {
            get
            {
                EnsureDefaultLoaded();
                return _defaultSet;
            }
        }

        /// <summary>幂等预热入口（NarrativeDataLoader.InitializeAll 调用）。</summary>
        public static TaggedMusicSet EnsureDefaultLoaded(bool force = false)
        {
            if (_bLoadedOnce && !force) return _defaultSet;
            _bLoadedOnce = true;

            string resolved = NarrativeDataPaths.Resolve(DefaultSetJsonPath);
            var set = LoadFromFile(resolved);
            if (set == null)
            {
                NarrativeLog.LogError(
                    $"[Music] 默认音乐集缺失或加载失败: '{resolved}'。主题切歌将无轨道可取。Has it been renamed or moved?");
                set = new TaggedMusicSet();
            }
            _defaultSet = set;
            return _defaultSet;
        }

        /// <summary>从 JSON 文件构建音乐集；文件不存在/解析失败返回 null。</summary>
        public static TaggedMusicSet LoadFromFile(string jsonFilePath)
        {
            string resolved = NarrativeDataPaths.Resolve(jsonFilePath);
            if (string.IsNullOrEmpty(resolved) || !File.Exists(resolved))
                return null;

            try
            {
                string json = File.ReadAllText(resolved);
                var asset = System.Text.Json.JsonSerializer.Deserialize<MusicSetAssetJson>(json,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                        AllowTrailingCommas = true,
                        IncludeFields = true
                    });
                if (asset == null) return null;

                var set = new TaggedMusicSet();
                if (asset.entries != null)
                {
                    foreach (var entry in asset.entries)
                    {
                        if (entry == null) continue;
                        var theme = new GameplayTag(entry.theme ?? "");
                        if (!theme.IsValid())
                        {
                            NarrativeLog.LogWarning($"[Music] 音乐集条目缺少有效 theme（来源 {resolved}），已跳过。");
                            continue;
                        }

                        var container = new MusicTracksContainer();
                        if (entry.tracks != null)
                        {
                            foreach (var track in entry.tracks)
                            {
                                if (track == null || string.IsNullOrEmpty(track.musicPath)) continue;
                                var clip = LoadClip(track.musicPath, resolved);
                                if (clip == null) continue;
                                container.MusicSounds.Add(new MusicSound(clip, track.fadeInDuration, track.fadeOutDuration));
                            }
                        }

                        set.Entries.Add(new MusicSetEntry { Theme = theme, Container = container });
                    }
                }

                set.RebuildLookup();
                NarrativeLog.Log($"[Music] 默认音乐集已从 {resolved} 加载：{set.Entries.Count} 个主题。");
                return set;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"[Music] 加载音乐集 '{resolved}' 失败: {ex.Message}");
                return null;
            }
        }

        private static AudioClip LoadClip(string path, string source)
        {
            try
            {
                var clip = Content.Load<AudioClip>(path);
                if (clip == null)
                    NarrativeLog.LogWarning($"[Music] 音轨资产不存在: '{path}'（来源 {source}），该轨道跳过。");
                return clip;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"[Music] 音轨 '{path}' 加载异常: {ex.Message}");
                return null;
            }
        }
    }
}
