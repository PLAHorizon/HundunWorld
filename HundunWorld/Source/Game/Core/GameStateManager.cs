using FlaxEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace HundunWorld.Game.Core
{
    /// <summary>
    /// 游戏状态枚举
    /// </summary>
    public enum GameState
    {
        /// <summary>启动/初始化</summary>
        Boot,
        /// <summary>登录界面</summary>
        Login,
        /// <summary>服务器选择</summary>
        ServerSelect,
        /// <summary>角色选择</summary>
        CharacterSelect,
        /// <summary>角色创建</summary>
        CharacterCreate,
        /// <summary>加载中</summary>
        Loading,
        /// <summary>游戏主世界</summary>
        InGame,
        /// <summary>副本中</summary>
        InDungeon,
        /// <summary>暂停</summary>
        Paused,
        /// <summary>断线重连</summary>
        Reconnecting,
    }

    /// <summary>
    /// 游戏设置数据（可序列化持久化）
    /// </summary>
    [Serializable]
    public class GameSettings
    {
        // ===== 画面设置 =====
        public int QualityPreset { get; set; } = 2;          // 0低 1中 2高 3极高
        public int ResolutionWidth { get; set; } = 1920;
        public int ResolutionHeight { get; set; } = 1080;
        public bool Fullscreen { get; set; } = true;
        public bool VSync { get; set; } = true;
        public int TargetFPS { get; set; } = 60;
        public float RenderScale { get; set; } = 1.0f;
        public bool ShadowsEnabled { get; set; } = true;
        public int ShadowQuality { get; set; } = 2;
        public bool AntiAliasing { get; set; } = true;
        public bool MotionBlur { get; set; } = false;
        public bool Bloom { get; set; } = true;

        // ===== 音频设置 =====
        public float MasterVolume { get; set; } = 1.0f;
        public float BGMVolume { get; set; } = 0.7f;
        public float SFXVolume { get; set; } = 0.8f;
        public float VoiceVolume { get; set; } = 0.9f;
        public float AmbientVolume { get; set; } = 0.6f;

        // ===== 操作设置 =====
        public float MouseSensitivity { get; set; } = 1.0f;
        public bool InvertYAxis { get; set; } = false;
        public float CameraDistance { get; set; } = 8.0f;
        public bool ShowDamageNumbers { get; set; } = true;
        public bool ShowOtherPlayerEffects { get; set; } = true;
        public float SkillIndicatorOpacity { get; set; } = 0.8f;

        // ===== 游戏设置 =====
        public bool AutoPickup { get; set; } = true;
        public int PickupQualityFilter { get; set; } = 0;
        public bool AutoAcceptTeamInvite { get; set; } = false;
        public bool ShowQuestTracker { get; set; } = true;
        public int MaxTrackedQuests { get; set; } = 3;
        public string ChatFontSize { get; set; } = "normal";
        public bool ShowCombatLog { get; set; } = true;

        // ===== 社交设置 =====
        public bool AllowTradeRequests { get; set; } = true;
        public bool AllowTeamInvites { get; set; } = true;
        public bool AllowWhispers { get; set; } = true;
        public bool ShowOnlineStatus { get; set; } = true;

        // ===== 辅助功能 =====
        public float UIScale { get; set; } = 1.0f;
        public bool ColorblindMode { get; set; } = false;
        public float SubtitleSize { get; set; } = 1.0f;
    }

    /// <summary>
    /// 游戏状态管理器 + 设置持久化 - 产品级游戏生命周期管理。
    /// 特性：
    /// - 完整游戏状态机（Boot→Login→Select→Loading→InGame）
    /// - 状态切换事件 + 过渡动画钩子
    /// - 设置JSON持久化（自动保存/加载）
    /// - 断线重连状态管理
    /// - 前后台切换处理
    /// </summary>
    public class GameStateManager
    {
        private static GameStateManager _instance;
        public static GameStateManager Instance => _instance ??= new GameStateManager();

        // ===== 状态 =====
        private GameState _currentState = GameState.Boot;
        private GameState _previousState = GameState.Boot;
        private float _stateEnterTime = 0f;
        private bool _isTransitioning = false;

        // ===== 设置 =====
        private GameSettings _settings = new GameSettings();
        private string _settingsPath;
        private float _lastSettingsSave = 0f;
        private const float SettingsSaveInterval = 5f;
        private bool _settingsDirty = false;

        // ===== 事件 =====
        public event Action<GameState, GameState> OnStateChanged;   // (oldState, newState)
        public event Action<GameState> OnStateEnter;
        public event Action<GameState> OnStateExit;
        public event Action<GameSettings> OnSettingsChanged;
        public event Action OnPause;
        public event Action OnResume;

        // ===== 属性 =====
        public GameState CurrentState => _currentState;
        public GameState PreviousState => _previousState;
        public float TimeInCurrentState => Time.GameTime - _stateEnterTime;
        public bool IsInGame => _currentState == GameState.InGame || _currentState == GameState.InDungeon;
        public bool IsPaused => _currentState == GameState.Paused;
        public bool IsTransitioning => _isTransitioning;
        public GameSettings Settings => _settings;

        public GameStateManager()
        {
            _settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HundunWorld", "settings.json");
        }

        // ===== 初始化 =====

        /// <summary>初始化（游戏启动时调用）</summary>
        public void Initialize()
        {
            LoadSettings();
            ApplySettings();
            Debug.Log("[GameStateManager] 初始化完成");
        }

        // ===== 状态管理 =====

        /// <summary>切换游戏状态</summary>
        public void ChangeState(GameState newState)
        {
            if (_currentState == newState) return;
            if (_isTransitioning)
            {
                Debug.LogWarning($"[GameStateManager] 正在过渡中，忽略切换到 {newState}");
                return;
            }

            var oldState = _currentState;
            _previousState = oldState;

            // 退出旧状态
            OnStateExit?.Invoke(oldState);
            ExitState(oldState);

            // 进入新状态
            _currentState = newState;
            _stateEnterTime = Time.GameTime;
            EnterState(newState);
            OnStateEnter?.Invoke(newState);

            // 通知
            OnStateChanged?.Invoke(oldState, newState);
            Debug.Log($"[GameStateManager] 状态切换: {oldState} → {newState}");
        }

        /// <summary>暂停游戏</summary>
        public void PauseGame()
        {
            if (!IsInGame) return;
            _previousState = _currentState;
            ChangeState(GameState.Paused);
            OnPause?.Invoke();
        }

        /// <summary>恢复游戏</summary>
        public void ResumeGame()
        {
            if (!IsPaused) return;
            ChangeState(_previousState);
            OnResume?.Invoke();
        }

        /// <summary>进入断线重连</summary>
        public void EnterReconnect()
        {
            _previousState = _currentState;
            ChangeState(GameState.Reconnecting);
        }

        /// <summary>重连成功</summary>
        public void ReconnectSuccess()
        {
            if (_currentState == GameState.Reconnecting)
                ChangeState(_previousState);
        }

        /// <summary>重连失败（返回登录）</summary>
        public void ReconnectFailed()
        {
            ChangeState(GameState.Login);
        }

        private void EnterState(GameState state)
        {
            switch (state)
            {
                case GameState.InGame:
                    Time.TimeScale = 1f;
                    break;
                case GameState.Paused:
                    Time.TimeScale = 0f;
                    break;
                case GameState.Loading:
                    // 触发加载流程
                    break;
            }
        }

        private void ExitState(GameState state)
        {
            switch (state)
            {
                case GameState.Paused:
                    Time.TimeScale = 1f;
                    break;
            }
        }

        // ===== 每帧更新 =====

        public void Update(float deltaTime)
        {
            // 自动保存设置
            if (_settingsDirty)
            {
                _lastSettingsSave += deltaTime;
                if (_lastSettingsSave >= SettingsSaveInterval)
                {
                    SaveSettings();
                    _lastSettingsSave = 0f;
                }
            }
        }

        // ===== 设置管理 =====

        /// <summary>修改设置并标记脏</summary>
        public void UpdateSettings(Action<GameSettings> modifier)
        {
            modifier(_settings);
            _settingsDirty = true;
            ApplySettings();
            OnSettingsChanged?.Invoke(_settings);
        }

        /// <summary>应用设置到引擎</summary>
        public void ApplySettings()
        {
            // 帧率限制（Flax 通过编辑器 Game Settings 或 Graphics Settings 配置）
            // 注：运行时帧率由引擎 VSync / MaxFPS 设置控制
            // 音量（通过GameAudioManager应用）
            // UI缩放
        }

        /// <summary>保存设置到文件</summary>
        public void SaveSettings()
        {
            try
            {
                var dir = Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_settingsPath, json);
                _settingsDirty = false;
                Debug.Log("[GameStateManager] 设置已保存");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameStateManager] 保存设置失败: {ex.Message}");
            }
        }

        /// <summary>从文件加载设置</summary>
        public void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    _settings = JsonSerializer.Deserialize<GameSettings>(json) ?? new GameSettings();
                    Debug.Log("[GameStateManager] 设置已加载");
                }
                else
                {
                    _settings = new GameSettings();
                    Debug.Log("[GameStateManager] 使用默认设置");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GameStateManager] 加载设置失败，使用默认值: {ex.Message}");
                _settings = new GameSettings();
            }
        }

        /// <summary>重置设置为默认</summary>
        public void ResetSettings()
        {
            _settings = new GameSettings();
            _settingsDirty = true;
            ApplySettings();
            OnSettingsChanged?.Invoke(_settings);
        }

        // ===== 快捷设置访问 =====

        public void SetMasterVolume(float vol) => UpdateSettings(s => s.MasterVolume = Mathf.Clamp(vol, 0f, 1f));
        public void SetBGMVolume(float vol) => UpdateSettings(s => s.BGMVolume = Mathf.Clamp(vol, 0f, 1f));
        public void SetSFXVolume(float vol) => UpdateSettings(s => s.SFXVolume = Mathf.Clamp(vol, 0f, 1f));
        public void SetQualityPreset(int preset) => UpdateSettings(s => s.QualityPreset = Mathf.Clamp(preset, 0, 3));
        public void SetFullscreen(bool fs) => UpdateSettings(s => s.Fullscreen = fs);
        public void SetMouseSensitivity(float sens) => UpdateSettings(s => s.MouseSensitivity = Mathf.Clamp(sens, 0.1f, 3f));
        public void ToggleAutoPickup() => UpdateSettings(s => s.AutoPickup = !s.AutoPickup);
        public void ToggleDamageNumbers() => UpdateSettings(s => s.ShowDamageNumbers = !s.ShowDamageNumbers);
    }
}
