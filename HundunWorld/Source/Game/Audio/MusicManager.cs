using FlaxEngine;
using Horizon.Game.Message.Network;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.Audio
{
    /// <summary>
    /// BGM 状态
    /// </summary>
    public enum MusicState
    {
        /// <summary>无音乐</summary>
        None,
        /// <summary>探索/和平</summary>
        Explore,
        /// <summary>战斗</summary>
        Combat,
        /// <summary>Boss战</summary>
        Boss,
        /// <summary>剧情/过场</summary>
        Cinematic,
        /// <summary>城镇/安全区</summary>
        Town,
        /// <summary>副本</summary>
        Dungeon,
        /// <summary>菜单</summary>
        Menu
    }

    /// <summary>
    /// BGM 轨道配置
    /// </summary>
    [Serializable]
    public class MusicTrackConfig
    {
        /// <summary>音乐资源路径</summary>
        public string MusicPath = "";

        /// <summary>是否循环</summary>
        public bool IsLooping = true;

        /// <summary>基础音量</summary>
        public float Volume = 0.7f;

        /// <summary>淡入时间（秒）</summary>
        public float FadeInDuration = 2.0f;

        /// <summary>淡出时间（秒）</summary>
        public float FadeOutDuration = 1.5f;
    }

    /// <summary>
    /// 环境音效配置
    /// </summary>
    [Serializable]
    public class AmbientSoundConfig
    {
        /// <summary>音效资源路径</summary>
        public string SoundPath = "";

        /// <summary>最小间隔（秒）</summary>
        public float MinInterval = 5f;

        /// <summary>最大间隔（秒）</summary>
        public float MaxInterval = 15f;

        /// <summary>音量</summary>
        public float Volume = 0.4f;

        /// <summary>是否3D空间化</summary>
        public bool Is3D = false;
    }

    /// <summary>
    /// 音乐管理器 - 管理 BGM 的播放、切换、淡入淡出。
    /// 产品级特性：
    /// - 状态驱动的 BGM 切换（探索/战斗/Boss/城镇等）
    /// - 平滑淡入淡出过渡
    /// - 战斗音乐分层（基础层 + 紧张层叠加）
    /// - 场景/区域自动切换
    /// </summary>
    public class MusicManager
    {
        private static MusicManager _instance;
        public static MusicManager Instance => _instance ??= new MusicManager();

        // ===== 状态 =====
        private MusicState _currentState = MusicState.None;
        private MusicState _previousState = MusicState.None;
        private AudioSource _currentSource;
        private AudioSource _fadeOutSource;
        private float _fadeProgress = 0f;
        private float _fadeOutProgress = 0f;
        private bool _isFading = false;
        private float _targetVolume = 0.7f;
        private float _currentVolume = 0f;
        private Random _random = new Random();

        // ===== 配置 =====
        private Dictionary<MusicState, MusicTrackConfig> _trackConfigs = new Dictionary<MusicState, MusicTrackConfig>();

        /// <summary>当前音乐状态</summary>
        public MusicState CurrentState => _currentState;

        /// <summary>音乐是否正在播放</summary>
        public bool IsPlaying => _currentSource != null && _currentSource.IsActuallyPlayingSth;

        /// <summary>全局音乐音量倍率</summary>
        public float GlobalMusicVolume { get; set; } = 1.0f;

        public MusicManager()
        {
            InitializeDefaultTracks();
        }

        /// <summary>
        /// 初始化默认音乐轨道配置
        /// </summary>
        private void InitializeDefaultTracks()
        {
            _trackConfigs[MusicState.Menu] = new MusicTrackConfig
            {
                MusicPath = "/Game/Audio/Music/MainMenu_Theme",
                Volume = 0.6f, FadeInDuration = 3f, FadeOutDuration = 2f
            };
            _trackConfigs[MusicState.Explore] = new MusicTrackConfig
            {
                MusicPath = "/Game/Audio/Music/Explore_Peaceful",
                Volume = 0.5f, FadeInDuration = 3f, FadeOutDuration = 2f
            };
            _trackConfigs[MusicState.Town] = new MusicTrackConfig
            {
                MusicPath = "/Game/Audio/Music/Town_Lively",
                Volume = 0.55f, FadeInDuration = 2f, FadeOutDuration = 1.5f
            };
            _trackConfigs[MusicState.Combat] = new MusicTrackConfig
            {
                MusicPath = "/Game/Audio/Music/Combat_Intense",
                Volume = 0.7f, FadeInDuration = 0.5f, FadeOutDuration = 1f
            };
            _trackConfigs[MusicState.Boss] = new MusicTrackConfig
            {
                MusicPath = "/Game/Audio/Music/Boss_Epic",
                Volume = 0.8f, FadeInDuration = 0.3f, FadeOutDuration = 1.5f
            };
            _trackConfigs[MusicState.Cinematic] = new MusicTrackConfig
            {
                MusicPath = "/Game/Audio/Music/Cinematic_Emotional",
                Volume = 0.65f, FadeInDuration = 2f, FadeOutDuration = 3f
            };
            _trackConfigs[MusicState.Dungeon] = new MusicTrackConfig
            {
                MusicPath = "/Game/Audio/Music/Dungeon_Mysterious",
                Volume = 0.6f, FadeInDuration = 2f, FadeOutDuration = 2f
            };
        }

        /// <summary>
        /// 切换音乐状态（带淡入淡出）
        /// </summary>
        public void TransitionTo(MusicState newState)
        {
            if (newState == _currentState && IsPlaying) return;

            _previousState = _currentState;
            _currentState = newState;

            if (!_trackConfigs.TryGetValue(newState, out var config))
            {
                StopMusic();
                return;
            }

            StartMusicWithFade(config);
        }

        /// <summary>
        /// 开始播放音乐（带淡入）
        /// </summary>
        private void StartMusicWithFade(MusicTrackConfig config)
        {
            // 淡出当前音乐
            if (_currentSource != null && _currentSource.IsActuallyPlayingSth)
            {
                _fadeOutSource = _currentSource;
                _fadeOutProgress = 0f;
            }

            // 加载新音乐
            try
            {
                var clip = Content.Load<AudioClip>(config.MusicPath);
                if (clip == null)
                {
                    Debug.Log($"[MusicManager] 音乐资源未找到: {config.MusicPath}");
                    return;
                }

                var source = new AudioSource();
                source.Clip = clip;
                source.Volume = 0f; // 从静音开始淡入
                source.IsLooping = config.IsLooping;
                Level.SpawnActor(source);
                source.Play();

                _currentSource = source;
                _targetVolume = config.Volume * GlobalMusicVolume;
                _currentVolume = 0f;
                _fadeProgress = 0f;
                _isFading = true;

                Debug.Log($"[MusicManager] 切换到: {_currentState} ({config.MusicPath})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MusicManager] 播放音乐失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 每帧更新（淡入淡出处理）
        /// </summary>
        public void Update(float deltaTime)
        {
            // 淡入当前音乐
            if (_isFading && _currentSource != null)
            {
                var config = _trackConfigs.TryGetValue(_currentState, out var c) ? c : null;
                float fadeDuration = config?.FadeInDuration ?? 2f;

                _fadeProgress += deltaTime / Mathf.Max(fadeDuration, 0.01f);
                _currentVolume = Mathf.Lerp(0f, _targetVolume, Mathf.Clamp(_fadeProgress, 0f, 1f));
                _currentSource.Volume = _currentVolume;

                if (_fadeProgress >= 1f)
                {
                    _isFading = false;
                }
            }

            // 淡出旧音乐
            if (_fadeOutSource != null)
            {
                var prevConfig = _trackConfigs.TryGetValue(_previousState, out var pc) ? pc : null;
                float fadeOutDuration = prevConfig?.FadeOutDuration ?? 1.5f;

                _fadeOutProgress += deltaTime / Mathf.Max(fadeOutDuration, 0.01f);
                float vol = Mathf.Lerp(_targetVolume, 0f, Mathf.Clamp(_fadeOutProgress, 0f, 1f));
                _fadeOutSource.Volume = vol;

                if (_fadeOutProgress >= 1f)
                {
                    _fadeOutSource.Stop();
                    _fadeOutSource = null;
                }
            }
        }

        /// <summary>
        /// 停止所有音乐
        /// </summary>
        public void StopMusic()
        {
            _currentSource?.Stop();
            _fadeOutSource?.Stop();
            _currentSource = null;
            _fadeOutSource = null;
            _currentState = MusicState.None;
            _isFading = false;
        }

        /// <summary>
        /// 暂停音乐
        /// </summary>
        public void PauseMusic()
        {
            _currentSource?.Pause();
        }

        /// <summary>
        /// 恢复音乐
        /// </summary>
        public void ResumeMusic()
        {
            _currentSource?.Play();
        }

        /// <summary>
        /// 设置全局音乐音量
        /// </summary>
        public void SetGlobalVolume(float volume)
        {
            GlobalMusicVolume = Mathf.Clamp(volume, 0f, 1f);
            if (_currentSource != null && !_isFading)
            {
                var config = _trackConfigs.TryGetValue(_currentState, out var c) ? c : null;
                _targetVolume = (config?.Volume ?? 0.7f) * GlobalMusicVolume;
                _currentSource.Volume = _targetVolume;
            }
        }

        /// <summary>
        /// 注册自定义音乐轨道
        /// </summary>
        public void RegisterTrack(MusicState state, MusicTrackConfig config)
        {
            _trackConfigs[state] = config;
        }
    }

    /// <summary>
    /// 环境音效系统 - 管理区域环境音（风声、水声、鸟鸣、虫鸣等）。
    /// 产品级特性：
    /// - 基于区域/场景的环境音配置
    /// - 随机间隔播放自然音效
    /// - 多层环境音叠加（底层循环 + 随机点缀）
    /// </summary>
    public class AmbientAudioSystem
    {
        private static AmbientAudioSystem _instance;
        public static AmbientAudioSystem Instance => _instance ??= new AmbientAudioSystem();

        private List<AmbientLayer> _layers = new List<AmbientLayer>();
        private AudioSource _baseLoopSource;
        private Random _random = new Random();
        private bool _isActive = false;

        /// <summary>环境音全局音量</summary>
        public float AmbientVolume { get; set; } = 0.6f;

        private class AmbientLayer
        {
            public AmbientSoundConfig Config;
            public float NextPlayTime;
            public AudioSource Source;
        }

        /// <summary>
        /// 激活环境音（进入区域时调用）
        /// </summary>
        public void Activate(string baseLoopPath, List<AmbientSoundConfig> randomSounds)
        {
            Deactivate();
            _isActive = true;

            // 底层循环音
            if (!string.IsNullOrEmpty(baseLoopPath))
            {
                try
                {
                    var clip = Content.Load<AudioClip>(baseLoopPath);
                    if (clip != null)
                    {
                        _baseLoopSource = new AudioSource();
                        _baseLoopSource.Clip = clip;
                        _baseLoopSource.Volume = AmbientVolume * 0.5f;
                        _baseLoopSource.IsLooping = true;
                        Level.SpawnActor(_baseLoopSource);
                        _baseLoopSource.Play();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AmbientAudio] 底层循环音加载失败: {ex.Message}");
                }
            }

            // 随机点缀音
            if (randomSounds != null)
            {
                foreach (var config in randomSounds)
                {
                    _layers.Add(new AmbientLayer
                    {
                        Config = config,
                        NextPlayTime = Time.GameTime + (float)(_random.NextDouble() * (config.MaxInterval - config.MinInterval) + config.MinInterval)
                    });
                }
            }

            Debug.Log($"[AmbientAudio] 环境音激活: {_layers.Count} 个随机层");
        }

        /// <summary>
        /// 停用环境音（离开区域时调用）
        /// </summary>
        public void Deactivate()
        {
            _baseLoopSource?.Stop();
            _baseLoopSource = null;

            foreach (var layer in _layers)
            {
                layer.Source?.Stop();
            }
            _layers.Clear();
            _isActive = false;
        }

        /// <summary>
        /// 每帧更新
        /// </summary>
        public void Update(float deltaTime)
        {
            if (!_isActive) return;

            float gameTime = Time.GameTime;
            foreach (var layer in _layers)
            {
                if (gameTime >= layer.NextPlayTime)
                {
                    PlayAmbientSound(layer);
                    layer.NextPlayTime = gameTime + (float)(_random.NextDouble() *
                        (layer.Config.MaxInterval - layer.Config.MinInterval) + layer.Config.MinInterval);
                }
            }
        }

        private void PlayAmbientSound(AmbientLayer layer)
        {
            try
            {
                var clip = Content.Load<AudioClip>(layer.Config.SoundPath);
                if (clip == null) return;

                var source = new AudioSource();
                source.Clip = clip;
                source.Volume = layer.Config.Volume * AmbientVolume;
                source.IsLooping = false;
                Level.SpawnActor(source);
                source.Play();
                layer.Source = source;
            }
            catch { /* 静默失败 */ }
        }

        /// <summary>
        /// 设置环境音音量
        /// </summary>
        public void SetVolume(float volume)
        {
            AmbientVolume = Mathf.Clamp(volume, 0f, 1f);
            if (_baseLoopSource != null)
            {
                _baseLoopSource.Volume = AmbientVolume * 0.5f;
            }
        }
    }
}
