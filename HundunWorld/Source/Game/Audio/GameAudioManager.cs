using FlaxEngine;
using Horizon.Game.Message.Network;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.Audio
{
    /// <summary>
    /// 游戏音频管理器
    /// 提供统一的音频播放接口，支持2D/3D音效、音量控制和类别管理
    /// </summary>
    public class GameAudioManager
    {
        private static GameAudioManager _instance;

        /// <summary>
        /// 单例实例
        /// </summary>
        public static GameAudioManager Instance => _instance ??= new GameAudioManager();

        private float _masterVolume = 1.0f;
        private float _sfxVolume = 1.0f;
        private float _musicVolume = 1.0f;

        private readonly Dictionary<GameAudioCategory, float> _categoryVolumes;
        private readonly List<ActiveSound> _activeSounds;
        private const int MaxConcurrentSounds = 32;
        private const float MinAudibleVolume = 0.001f;

        public GameAudioManager()
        {
            _categoryVolumes = new Dictionary<GameAudioCategory, float>();
            _activeSounds = new List<ActiveSound>();

            // 初始化各类别音量为默认值
            foreach (GameAudioCategory category in Enum.GetValues(typeof(GameAudioCategory)))
            {
                _categoryVolumes[category] = 1.0f;
            }

            Debug.Log("[GameAudioManager] 音频管理器已初始化");
        }

        #region 音量控制

        /// <summary>
        /// 主音量（0.0-1.0）
        /// </summary>
        public float MasterVolume
        {
            get => _masterVolume;
            set => _masterVolume = Mathf.Clamp(value, 0f, 1f);
        }

        /// <summary>
        /// 音效音量（0.0-1.0）
        /// </summary>
        public float SfxVolume
        {
            get => _sfxVolume;
            set => _sfxVolume = Mathf.Clamp(value, 0f, 1f);
        }

        /// <summary>
        /// 音乐音量（0.0-1.0）
        /// </summary>
        public float MusicVolume
        {
            get => _musicVolume;
            set => _musicVolume = Mathf.Clamp(value, 0f, 1f);
        }

        /// <summary>
        /// 设置指定类别的音量
        /// </summary>
        public void SetCategoryVolume(GameAudioCategory category, float volume)
        {
            _categoryVolumes[category] = Mathf.Clamp(volume, 0f, 1f);
        }

        /// <summary>
        /// 获取指定类别的音量
        /// </summary>
        public float GetCategoryVolume(GameAudioCategory category)
        {
            return _categoryVolumes.TryGetValue(category, out var vol) ? vol : 1.0f;
        }

        /// <summary>
        /// 计算最终播放音量
        /// </summary>
        private float CalculateFinalVolume(GameAudioCategory category, float baseVolume)
        {
            var categoryVol = GetCategoryVolume(category);
            return _masterVolume * _sfxVolume * categoryVol * baseVolume;
        }

        #endregion

        #region 音效播放

        /// <summary>
        /// 播放2D音效（非空间化）
        /// </summary>
        /// <param name="soundPath">音效资源路径</param>
        /// <param name="category">音效类别</param>
        /// <param name="volume">基础音量</param>
        public void Play2D(string soundPath, GameAudioCategory category = GameAudioCategory.UI, float volume = 1.0f)
        {
            if (string.IsNullOrEmpty(soundPath))
                return;

            try
            {
                var finalVolume = CalculateFinalVolume(category, volume);
                if (finalVolume <= MinAudibleVolume)
                    return;

                // 清理过期的音效记录
                CleanupFinishedSounds();

                if (_activeSounds.Count >= MaxConcurrentSounds)
                {
                    Debug.Log($"[GameAudioManager] 并发音效数达到上限 {MaxConcurrentSounds}，跳过: {soundPath}");
                    return;
                }

                // 加载并播放音效
                var audioClip = Content.Load<AudioClip>(soundPath);
                if (audioClip != null)
                {
                    var source = AudioSource.PlayOneShot(audioClip, finalVolume);
                    if (source != null)
                    {
                        _activeSounds.Add(new ActiveSound
                        {
                            SoundPath = soundPath,
                            Category = category,
                            StartTime = Time.GameTime,
                            Source = source
                        });
                    }
                }
                else
                {
                    Debug.Log($"[GameAudioManager] 音效资源未找到: {soundPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameAudioManager] 播放2D音效失败 ({soundPath}): {ex.Message}");
            }
        }

        /// <summary>
        /// 播放3D空间音效
        /// </summary>
        /// <param name="soundPath">音效资源路径</param>
        /// <param name="position">世界坐标位置</param>
        /// <param name="category">音效类别</param>
        /// <param name="volume">基础音量</param>
        public void Play3D(string soundPath, Vector3 position, GameAudioCategory category = GameAudioCategory.Skill, float volume = 1.0f)
        {
            if (string.IsNullOrEmpty(soundPath))
                return;

            try
            {
                var finalVolume = CalculateFinalVolume(category, volume);
                if (finalVolume <= MinAudibleVolume)
                    return;

                CleanupFinishedSounds();

                if (_activeSounds.Count >= MaxConcurrentSounds)
                {
                    Debug.Log($"[GameAudioManager] 并发音效数达到上限，跳过: {soundPath}");
                    return;
                }

                var audioClip = Content.Load<AudioClip>(soundPath);
                if (audioClip != null)
                {
                    var source = AudioSource.PlayOneShot(audioClip, finalVolume, position);
                    if (source != null)
                    {
                        _activeSounds.Add(new ActiveSound
                        {
                            SoundPath = soundPath,
                            Category = category,
                            StartTime = Time.GameTime,
                            Source = source
                        });
                    }
                }
                else
                {
                    Debug.Log($"[GameAudioManager] 音效资源未找到: {soundPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameAudioManager] 播放3D音效失败 ({soundPath}): {ex.Message}");
            }
        }

        /// <summary>
        /// 播放技能音效
        /// </summary>
        /// <param name="skillId">技能ID</param>
        /// <param name="position">播放位置（null表示2D播放）</param>
        public void PlaySkillSound(int skillId, Vector3? position = null)
        {
            var soundPath = $"/Game/Audio/Skills/Skill_{skillId}";
            if (position.HasValue)
                Play3D(soundPath, position.Value, GameAudioCategory.Skill);
            else
                Play2D(soundPath, GameAudioCategory.Skill);
        }

        /// <summary>
        /// 播放攻击音效
        /// </summary>
        /// <param name="skillId">使用的技能ID</param>
        /// <param name="position">播放位置（null表示2D播放）</param>
        public void PlayAttackSound(int skillId, Vector3? position = null)
        {
            var soundPath = $"/Game/Audio/Skills/Skill_{skillId}_Attack";
            if (position.HasValue)
                Play3D(soundPath, position.Value, GameAudioCategory.Attack);
            else
                Play2D(soundPath, GameAudioCategory.Attack);
        }

        /// <summary>
        /// 播放死亡音效
        /// </summary>
        public void PlayDeathSound(Vector3? position = null)
        {
            var soundPath = "/Game/Audio/Effects/Death_Sound";
            if (position.HasValue)
                Play3D(soundPath, position.Value, GameAudioCategory.Death);
            else
                Play2D(soundPath, GameAudioCategory.Death);
        }

        /// <summary>
        /// 播放复活音效
        /// </summary>
        public void PlayResurrectSound(Vector3? position = null)
        {
            var soundPath = "/Game/Audio/Effects/Resurrect_Sound";
            if (position.HasValue)
                Play3D(soundPath, position.Value, GameAudioCategory.Resurrect);
            else
                Play2D(soundPath, GameAudioCategory.Resurrect);
        }

        /// <summary>
        /// 播放受击音效
        /// </summary>
        public void PlayHitSound(Vector3? position = null)
        {
            var soundPath = "/Game/Audio/Effects/Hit_Sound";
            if (position.HasValue)
                Play3D(soundPath, position.Value, GameAudioCategory.Hit);
            else
                Play2D(soundPath, GameAudioCategory.Hit);
        }

        /// <summary>
        /// 处理音频播放网络消息
        /// </summary>
        public void HandleAudioMessage(AudioPlaybackMessage message)
        {
            if (message == null)
                return;

            if (message.Is3D)
            {
                var pos = new Vector3(message.PositionX, message.PositionY, message.PositionZ);
                Play3D(message.SoundPath, pos, message.Category, message.Volume);
            }
            else
            {
                Play2D(message.SoundPath, message.Category, message.Volume);
            }
        }

        #endregion

        #region 内部管理

        /// <summary>
        /// 清理已播放完毕的音效记录
        /// </summary>
        private void CleanupFinishedSounds()
        {
            _activeSounds.RemoveAll(s => s.Source == null || !s.Source.IsActuallyPlayingSth);
        }

        /// <summary>
        /// 停止所有音效
        /// </summary>
        public void StopAll()
        {
            foreach (var sound in _activeSounds)
            {
                if (sound.Source != null && sound.Source.IsActuallyPlayingSth)
                {
                    sound.Source.Stop();
                }
            }
            _activeSounds.Clear();
        }

        /// <summary>
        /// 停止指定类别的所有音效
        /// </summary>
        public void StopCategory(GameAudioCategory category)
        {
            var toRemove = _activeSounds.FindAll(s => s.Category == category);
            foreach (var sound in toRemove)
            {
                if (sound.Source != null && sound.Source.IsActuallyPlayingSth)
                {
                    sound.Source.Stop();
                }
            }
            _activeSounds.RemoveAll(s => s.Category == category);
        }

        #endregion

        /// <summary>
        /// 活跃音效信息
        /// </summary>
        private class ActiveSound
        {
            public string SoundPath { get; set; }
            public GameAudioCategory Category { get; set; }
            public float StartTime { get; set; }
            public AudioSource Source { get; set; }
        }
    }
}
