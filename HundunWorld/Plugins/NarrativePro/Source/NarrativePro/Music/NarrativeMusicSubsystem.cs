using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Music
{
    /// <summary>
    /// 主题覆盖清除模式。对应 UE5 EThemeOverrideClearMode。
    /// </summary>
    public enum ThemeOverrideClearMode
    {
        /// <summary>移除非持久和持久覆盖。</summary>
        Both,
        /// <summary>仅移除非持久覆盖。</summary>
        NonPersistant,
        /// <summary>仅移除持久覆盖。</summary>
        Persistant
    }

    /// <summary>
    /// 音乐轨道状态。对应 UE5 FMusicTrackState。
    /// 实现双轨交错淡入淡出：每条轨道维护一个 AudioSource，淡入/淡出时插值 Volume。
    /// </summary>
    public class MusicTrackState
    {
        /// <summary>轨道 fade 状态。</summary>
        public enum TrackFadeState : byte
        {
            None,
            In,
            Out
        }

        /// <summary>轨道 ID（1 或 2，0 表示队列槽）。</summary>
        public int TrackID = -1;

        /// <summary>当前主题。</summary>
        public GameplayTag Theme;

        /// <summary>当前 fade 状态。</summary>
        public TrackFadeState FadeState = TrackFadeState.None;

        /// <summary>最近一次使用的音乐集。</summary>
        public TaggedMusicSet MusicSet;

        /// <summary>最近一次使用的音轨。</summary>
        public MusicSound Sound;

        /// <summary>fade 累计时间。</summary>
        public float FadeElapsed;

        /// <summary>fade 目标总时长。</summary>
        public float FadeDuration;

        /// <summary>关联的 AudioSource（由子系统注入）。</summary>
        public AudioSource Source;

        public MusicTrackState() { }

        public MusicTrackState(int trackID)
        {
            TrackID = trackID;
        }

        /// <summary>主题与音乐集是否同时匹配。</summary>
        public bool DoesThemeMatch(TaggedMusicSet inMusicSet, GameplayTag inTheme)
        {
            return inMusicSet == MusicSet && inTheme == Theme;
        }

        public bool IsFadingIn => FadeState == TrackFadeState.In;
        public bool IsFadingOut => FadeState == TrackFadeState.Out;
        public bool IsFading => FadeState != TrackFadeState.None;

        /// <summary>启动淡入。</summary>
        public void StartFadeIn(MusicSound sound, bool immediate)
        {
            Sound = sound;
            float dur = immediate ? 0.01f : (sound?.FadeInDuration ?? 3.0f);
            FadeDuration = dur;
            FadeElapsed = 0;
            FadeState = TrackFadeState.In;

            if (Source != null && sound?.Music != null)
            {
                Source.Clip = sound.Music;
                Source.Volume = 0;
                Source.IsLooping = true;
                Source.Play();
            }
        }

        /// <summary>启动淡出。</summary>
        public void StartFadeOut(bool immediate)
        {
            float dur = immediate ? 0.01f : (Sound?.FadeOutDuration ?? 3.0f);
            FadeDuration = dur;
            FadeElapsed = 0;
            FadeState = TrackFadeState.Out;
        }

        /// <summary>每帧推进 fade，返回 true 表示本轮 fade 刚刚完成。</summary>
        public bool Tick(float deltaTime)
        {
            if (!IsFading || Source == null) return false;

            FadeElapsed += deltaTime;
            float t = FadeDuration > 0 ? Math.Min(1, FadeElapsed / FadeDuration) : 1;
            bool done = false;

            if (FadeState == TrackFadeState.In)
            {
                Source.Volume = t;
                if (t >= 1)
                {
                    FadeState = TrackFadeState.None;
                    done = true;
                }
            }
            else if (FadeState == TrackFadeState.Out)
            {
                Source.Volume = 1 - t;
                if (t >= 1)
                {
                    FadeState = TrackFadeState.None;
                    Source.Stop();
                    // 清空轨道
                    Theme = GameplayTag.None;
                    MusicSet = null;
                    Sound = null;
                    Source.Volume = 0;
                    done = true;
                }
            }
            return done;
        }

        /// <summary>重置轨道状态。</summary>
        public void Reset()
        {
            Theme = GameplayTag.None;
            FadeState = TrackFadeState.None;
            FadeElapsed = 0;
            FadeDuration = 0;
            MusicSet = null;
            Sound = null;
            if (Source != null)
            {
                Source.Stop();
                Source.Volume = 0;
            }
        }
    }

    /// <summary>
    /// 音乐子系统。对应 UE5 UNarrativeMusicSubsystem。
    /// 管理主题切换、双轨交错淡入淡出、主题覆盖、音乐集覆盖、单曲覆盖以及队列机制。
    /// Flax 无 GameInstanceSubsystem 等价物，使用 Singleton Script 模式（参考 NavigationSubsystem）。
    /// </summary>
    public class NarrativeMusicSubsystem : Script
    {
        private static NarrativeMusicSubsystem _instance;

        // 双轨：track1、track2
        private readonly MusicTrackState _trackOne = new MusicTrackState(1);
        private readonly MusicTrackState _trackTwo = new MusicTrackState(2);
        // 队列：当前正在 fade 时排队下一次切换
        private readonly MusicTrackState _trackQueue = new MusicTrackState(0);

        // 当前活跃轨道 ID（1 或 2），-1 表示尚未启动
        private int _activeTrackID = -1;

        // 当前活跃主题
        private GameplayTag _activeTheme = GameplayTag.None;

        // 当前音乐集
        private TaggedMusicSet _currentMusicSet;

        // 主题覆盖（持久，跨音乐集变更）
        private readonly Dictionary<GameplayTag, MusicSound> _persistantOverrides = new Dictionary<GameplayTag, MusicSound>();

        // 主题覆盖（非持久，仅当前音乐集）
        private readonly Dictionary<GameplayTag, MusicSound> _themeOverrides = new Dictionary<GameplayTag, MusicSound>();

        // 单曲覆盖状态
        private MusicSound _overrideSound;
        private float _overrideFadeDuration;
        private bool _overridePausedPrimary;

        // 持有用于单曲覆盖的独立 AudioSource
        private AudioSource _overrideSource;

        /// <summary>
        /// 默认音乐集（Inspector 中设置，无 WorldOverride 时使用）。
        /// 对应 UE5 UArsenalSettings::DefaultMusicSet。
        /// </summary>
        public TaggedMusicSet DefaultMusicSet;

        /// <summary>
        /// 场景级音乐集覆盖。对应 UE5 ANarrativeWorldSettings::DefaultMusicSetOverride。
        /// </summary>
        public TaggedMusicSet WorldMusicSetOverride;

        /// <summary>当前实例。</summary>
        public static NarrativeMusicSubsystem Instance => _instance;

        /// <summary>当前活跃主题。</summary>
        public GameplayTag GetActiveTheme() => _activeTheme;

        /// <summary>当前音乐集。</summary>
        public TaggedMusicSet GetActiveMusicSet() => _currentMusicSet;

        /// <summary>
        /// 当前活跃的 AudioSource（覆盖播放时返回 override 源，否则返回活跃轨道源）。
        /// </summary>
        public AudioSource GetActiveAudioComponent()
        {
            if (_overrideSound?.Music != null)
            {
                return _overrideSource;
            }
            return _activeTrackID == _trackOne.TrackID ? _trackOne.Source : _trackTwo.Source;
        }

        public override void OnEnable()
        {
            base.OnEnable();
            _instance = this;

            InitPrimaryAudioSources();
            InitOverrideAudioSource();

            _activeTheme = NativeMusicThemeTags.TAG_MUSIC_AMBIENT;

            // 应用默认/世界覆盖音乐集（等价 UE5 WorldInit 中的初始化）
            var musicSet = WorldMusicSetOverride ?? DefaultMusicSet;
            if (musicSet != null)
            {
                _currentMusicSet = musicSet;
            }
        }

        public override void OnDisable()
        {
            ResetTracks();

            DestroyAudioSource(_overrideSource);
            DestroyAudioSource(_trackOne.Source);
            DestroyAudioSource(_trackTwo.Source);

            _trackOne.Source = null;
            _trackTwo.Source = null;
            _overrideSource = null;

            _activeTrackID = -1;
            _activeTheme = GameplayTag.None;
            _currentMusicSet = null;
            _persistantOverrides.Clear();
            _themeOverrides.Clear();
            _trackQueue.Reset();
            _overrideSound = null;
            _overrideFadeDuration = 0;
            _overridePausedPrimary = false;

            if (_instance == this) _instance = null;
            base.OnDisable();
        }

        public override void OnUpdate()
        {
            // 推进 fade
            bool trackOneDone = _trackOne.Tick(Time.DeltaTime);
            bool trackTwoDone = _trackTwo.Tick(Time.DeltaTime);

            // 任意轨道 fade 完成，尝试处理队列
            if (trackOneDone || trackTwoDone)
            {
                TryProcessQueue();
            }
        }

        // ====== 初始化 ======

        private void InitPrimaryAudioSources()
        {
            if (_trackOne.Source == null) _trackOne.Source = CreateAudioSource("MusicTrackOne");
            if (_trackTwo.Source == null) _trackTwo.Source = CreateAudioSource("MusicTrackTwo");
        }

        private void InitOverrideAudioSource()
        {
            if (_overrideSource == null) _overrideSource = CreateAudioSource("MusicOverride");
        }

        private AudioSource CreateAudioSource(string name)
        {
            var source = new AudioSource
            {
                Name = name,
                Volume = 0,
                IsLooping = true
            };
            Level.SpawnActor(source);
            return source;
        }

        private void DestroyAudioSource(AudioSource source)
        {
            if (source != null)
            {
                source.Stop();
                Actor.Destroy(source);
            }
        }

        private MusicTrackState GetTrackFromID(int trackID)
        {
            return trackID == _trackOne.TrackID ? _trackOne : _trackTwo;
        }

        /// <summary>获取当前活跃轨道。</summary>
        private MusicTrackState ActiveTrack => _activeTrackID == _trackOne.TrackID ? _trackOne : _trackTwo;

        // ====== 公共 API ======

        /// <summary>
        /// 设置当前主题。
        /// </summary>
        /// <param name="theme">主题标签。</param>
        /// <param name="immediate">true 则忽略淡入淡出时长，立即切换。</param>
        /// <returns>是否成功设置。</returns>
        public bool SetTheme(GameplayTag theme, bool immediate)
        {
            if (!theme.IsValid() || _currentMusicSet == null) return false;

            // 移除单曲覆盖
            if (_overrideSound?.Music != null)
            {
                ClearOverrideMusicWithSound();
            }

            // 主题覆盖优先
            MusicSound musicSound = GetThemeOverride(theme);
            if (musicSound?.Music == null)
            {
                musicSound = _currentMusicSet.Get(theme);
                if (musicSound?.Music == null)
                {
                    NarrativeLog.LogWarning($"Theme {theme} not found in current music set.");
                    return false;
                }
            }

            // 当前有 fade 进行中，尝试入队
            if (_trackOne.IsFadingOut || _trackTwo.IsFadingOut)
            {
                if (CanQueueTheme(_currentMusicSet, theme))
                {
                    _trackQueue.Theme = theme;
                    _trackQueue.MusicSet = _currentMusicSet;
                    _trackQueue.Sound = musicSound;
                    return true;
                }
                return false;
            }

            // 主题已播放，无需切换
            if (_trackOne.DoesThemeMatch(_currentMusicSet, theme) || _trackTwo.DoesThemeMatch(_currentMusicSet, theme))
            {
                return false;
            }

            MusicTrackState fadeInTrack;
            MusicTrackState fadeOutTrack = null;

            if (_activeTrackID == -1)
            {
                fadeInTrack = _trackOne;
                immediate = true; // 首次播放立即切换
            }
            else if (_activeTrackID == _trackOne.TrackID)
            {
                fadeInTrack = _trackTwo;
                fadeOutTrack = _trackOne;
            }
            else
            {
                fadeInTrack = _trackOne;
                fadeOutTrack = _trackTwo;
            }

            _activeTrackID = fadeInTrack.TrackID;
            _activeTheme = theme;
            fadeInTrack.Theme = theme;
            fadeInTrack.MusicSet = _currentMusicSet;
            fadeInTrack.StartFadeIn(musicSound, immediate);

            if (fadeOutTrack != null && fadeOutTrack.Sound != null)
            {
                fadeOutTrack.StartFadeOut(immediate);
            }

            return true;
        }

        /// <summary>
        /// 覆盖当前音乐集。对应 UE5 OverrideMusicSet。
        /// </summary>
        public bool OverrideMusicSet(TaggedMusicSet newMusicSet)
        {
            if (newMusicSet == null) return false;
            _currentMusicSet = newMusicSet;
            if (_activeTheme.IsValid() && newMusicSet.Has(_activeTheme))
            {
                ClearAllThemeOverrides(ThemeOverrideClearMode.NonPersistant);
                SetTheme(_activeTheme, false);
            }
            return true;
        }

        /// <summary>
        /// 重置音乐集为默认（WorldOverride 优先，否则 DefaultMusicSet）。
        /// 对应 UE5 ResetMusicSetToDefault。
        /// </summary>
        public bool ResetMusicSetToDefault()
        {
            var musicSet = WorldMusicSetOverride ?? DefaultMusicSet;
            if (musicSet != null)
            {
                _currentMusicSet = musicSet;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 主题覆盖。对应 UE5 OverrideTheme。
        /// </summary>
        /// <param name="theme">要覆盖的主题。</param>
        /// <param name="sound">覆盖音轨。</param>
        /// <param name="fadeInDuration">淡入时长。</param>
        /// <param name="fadeOutDuration">淡出时长。</param>
        /// <param name="persistant">true 则跨音乐集变更保留。</param>
        public bool OverrideTheme(GameplayTag theme, AudioClip sound, float fadeInDuration = 3.0f, float fadeOutDuration = 3.0f, bool persistant = false)
        {
            if (sound == null || !theme.IsValid()) return false;

            var ms = new MusicSound(sound, fadeInDuration, fadeOutDuration);
            var dict = persistant ? _persistantOverrides : _themeOverrides;
            dict[theme] = ms;

            if (_activeTheme.IsValid())
            {
                return SetTheme(_activeTheme, false);
            }
            return true;
        }

        /// <summary>清除指定主题的覆盖。对应 UE5 ClearThemeOverride。</summary>
        public void ClearThemeOverride(GameplayTag theme)
        {
            _themeOverrides.Remove(theme);
            _persistantOverrides.Remove(theme);
            if (_activeTheme.IsValid())
            {
                SetTheme(_activeTheme, false);
            }
        }

        /// <summary>清除所有主题覆盖。对应 UE5 ClearAllThemeOverrides。</summary>
        public void ClearAllThemeOverrides(ThemeOverrideClearMode mode)
        {
            bool both = mode == ThemeOverrideClearMode.Both;
            if (both || mode == ThemeOverrideClearMode.NonPersistant)
            {
                _themeOverrides.Clear();
            }
            if (both || mode == ThemeOverrideClearMode.Persistant)
            {
                _persistantOverrides.Clear();
            }
        }

        /// <summary>
        /// 用具体 AudioClip 覆盖当前音乐。对应 UE5 OverrideMusicWithSound。
        /// </summary>
        /// <param name="sound">要播放的音频。</param>
        /// <param name="uiSound">是否为 UI 音效（占位，Flax 无 SoundClass）。</param>
        /// <param name="fadeDuration">淡入淡出时长。&gt;0 时静音主播放器，覆盖结束后恢复。</param>
        public bool OverrideMusicWithSound(AudioClip sound, bool uiSound, float fadeDuration)
        {
            if (sound == null) return false;

            _overrideFadeDuration = 0;
            bool fade = fadeDuration > 0;

            // 静音主播放器（等价 UE5 AdjustVolume / SetPaused）
            var primary = ActiveTrack?.Source;
            if (primary != null)
            {
                if (fade)
                {
                    primary.Volume = 0.01f;
                }
                else
                {
                    primary.Volume = 0;
                    _overridePausedPrimary = true;
                }
            }

            // 播放覆盖音轨
            if (_overrideSource != null)
            {
                _overrideSource.Clip = sound;
                _overrideSource.Volume = fade ? 0 : 1;
                _overrideSource.IsLooping = false;
                _overrideSource.Play();
            }

            _overrideSound = new MusicSound(sound, fadeDuration, fadeDuration);

            if (fade)
            {
                _overrideFadeDuration = fadeDuration;
            }
            return true;
        }

        /// <summary>清除单曲覆盖。对应 UE5 ClearOverrideMusicWithSound。</summary>
        public void ClearOverrideMusicWithSound()
        {
            if (_overrideSound?.Music == null) return;

            _overrideSound = null;
            if (_overrideSource != null)
            {
                _overrideSource.Stop();
            }

            // 恢复主播放器
            var primary = ActiveTrack?.Source;
            if (primary != null && _overridePausedPrimary)
            {
                primary.Volume = 1;
                _overridePausedPrimary = false;
            }

            SetTheme(_activeTheme, true);
            _overrideFadeDuration = 0;
        }

        // ====== 内部 ======

        private MusicSound GetThemeOverride(GameplayTag theme)
        {
            if (_persistantOverrides.TryGetValue(theme, out var p)) return p;
            if (_themeOverrides.TryGetValue(theme, out var n)) return n;
            return null;
        }

        private bool CanQueueTheme(TaggedMusicSet newMusicSet, GameplayTag theme)
        {
            if (_trackQueue.DoesThemeMatch(newMusicSet, theme))
            {
                return false; // 已在队列中
            }
            if (_trackOne.DoesThemeMatch(_currentMusicSet, theme) && !_trackOne.IsFadingOut)
            {
                return false;
            }
            if (_trackTwo.DoesThemeMatch(_currentMusicSet, theme) && !_trackTwo.IsFadingOut)
            {
                return false;
            }
            return true;
        }

        private void TryProcessQueue()
        {
            if (!_trackQueue.Theme.IsValid()) return;
            if (_trackOne.IsFadingOut || _trackTwo.IsFadingOut) return;

            var pendingTheme = _trackQueue.Theme;
            var pendingSound = _trackQueue.Sound;
            _trackQueue.Reset();

            // 直接播放，不再入队
            SetThemeDirect(pendingTheme, pendingSound, false);
        }

        // 直接播放给定主题与音轨（无入队检查）
        private void SetThemeDirect(GameplayTag theme, MusicSound sound, bool immediate)
        {
            if (sound?.Music == null || _currentMusicSet == null) return;

            MusicTrackState fadeInTrack;
            MusicTrackState fadeOutTrack = null;

            if (_activeTrackID == -1)
            {
                fadeInTrack = _trackOne;
                immediate = true;
            }
            else if (_activeTrackID == _trackOne.TrackID)
            {
                fadeInTrack = _trackTwo;
                fadeOutTrack = _trackOne;
            }
            else
            {
                fadeInTrack = _trackOne;
                fadeOutTrack = _trackTwo;
            }

            _activeTrackID = fadeInTrack.TrackID;
            _activeTheme = theme;
            fadeInTrack.Theme = theme;
            fadeInTrack.MusicSet = _currentMusicSet;
            fadeInTrack.StartFadeIn(sound, immediate);

            if (fadeOutTrack != null && fadeOutTrack.Sound != null)
            {
                fadeOutTrack.StartFadeOut(immediate);
            }
        }

        private void ResetTracks()
        {
            _trackOne.Reset();
            _trackTwo.Reset();
            _trackQueue.Reset();
        }
    }
}
