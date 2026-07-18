using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Items;

namespace NarrativePro.Music
{
    /// <summary>
    /// 音乐音轨配置。对应 UE5 FMusicSound。
    /// 描述一段音乐资源及其淡入淡出时长。
    /// </summary>
    [Serializable]
    public class MusicSound
    {
        /// <summary>要播放的音频资源。</summary>
        public AudioClip Music;

        /// <summary>淡入时长（秒）。</summary>
        public float FadeInDuration = 3.0f;

        /// <summary>淡出时长（秒）。</summary>
        public float FadeOutDuration = 3.0f;

        public MusicSound() { }

        public MusicSound(AudioClip music, float fadeIn = 3.0f, float fadeOut = 3.0f)
        {
            Music = music;
            FadeInDuration = fadeIn;
            FadeOutDuration = fadeOut;
        }
    }

    /// <summary>
    /// 同一主题下的多条音轨容器。对应 UE5 FMusicTracksContainer。
    /// 切换主题时随机选取一条播放。
    /// </summary>
    [Serializable]
    public class MusicTracksContainer
    {
        /// <summary>主题下可选的音乐列表。</summary>
        public List<MusicSound> MusicSounds = new List<MusicSound>();
    }

    /// <summary>
    /// 标签映射条目（用于序列化）。
    /// </summary>
    [Serializable]
    public class MusicSetEntry
    {
        public GameplayTag Theme;
        public MusicTracksContainer Container;
    }

    /// <summary>
    /// 音乐集数据资产。对应 UE5 UTaggedMusicSet。
    /// 以 <see cref="GameplayTag"/> 为索引存储多组音乐音轨，运行时按主题随机获取一条音轨。
    /// 用 List<see cref="MusicSetEntry"/> 序列化、Dictionary 查找的方式兼容 Flax 序列化器与运行时性能。
    /// </summary>
    [Serializable]
    public class TaggedMusicSet
    {
        /// <summary>主题条目列表（编辑器配置入口）。</summary>
        public List<MusicSetEntry> Entries = new List<MusicSetEntry>();

        // 运行时查找表，由 RebuildLookup 构建。
        [NonSerialized]
        private Dictionary<GameplayTag, MusicTracksContainer> _lookup;

        [NonSerialized]
        private bool _lookupBuilt;

        // 共享随机数生成器
        private static readonly System.Random _rng = new System.Random();

        private void EnsureLookupBuilt()
        {
            if (_lookupBuilt) return;
            _lookup = new Dictionary<GameplayTag, MusicTracksContainer>();
            if (Entries != null)
            {
                foreach (var entry in Entries)
                {
                    if (entry != null && entry.Theme.IsValid() && entry.Container != null)
                    {
                        _lookup[entry.Theme] = entry.Container;
                    }
                }
            }
            _lookupBuilt = true;
        }

        /// <summary>重建查找表。外部修改 Entries 后调用以刷新。</summary>
        public void RebuildLookup()
        {
            _lookupBuilt = false;
            EnsureLookupBuilt();
        }

        /// <summary>是否存在指定主题的音乐。</summary>
        public bool Has(GameplayTag tag)
        {
            EnsureLookupBuilt();
            return _lookup != null && _lookup.Count > 0 && _lookup.ContainsKey(tag);
        }

        /// <summary>
        /// 获取指定主题下的随机一条音轨；不存在则返回 null。
        /// </summary>
        public MusicSound Get(GameplayTag tag)
        {
            EnsureLookupBuilt();
            if (_lookup != null && _lookup.TryGetValue(tag, out var container) && container != null
                && container.MusicSounds != null && container.MusicSounds.Count > 0)
            {
                int idx = _rng.Next(0, container.MusicSounds.Count);
                return container.MusicSounds[idx];
            }
            return null;
        }
    }
}
