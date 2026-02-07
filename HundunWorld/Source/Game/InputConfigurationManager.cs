using FlaxEngine;
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace HundunWorld.Game
{
    /// <summary>
    /// 输入配置管理器，负责保存和加载用户的输入设置
    /// </summary>
    public class InputConfigurationManager : Script
    {
        #region 配置文件管理

        /// <summary>
        /// 配置文件路径
        /// </summary>
        [Tooltip("配置文件路径")]
        public string ConfigFilePath { get; set; } = "Config/InputSettings.json";

        /// <summary>
        /// 是否自动保存配置
        /// </summary>
        [Tooltip("是否自动保存配置")]
        public bool AutoSaveConfig { get; set; } = true;

        /// <summary>
        /// 自动保存间隔（秒）
        /// </summary>
        [Tooltip("自动保存间隔（秒）")]
        public float AutoSaveInterval { get; set; } = 30.0f;

        /// <summary>
        /// 上次保存时间
        /// </summary>
        private float _lastSaveTime = 0f;

        /// <summary>
        /// 配置是否已修改
        /// </summary>
        private bool _configModified = false;

        #endregion

        #region 输入配置数据

        /// <summary>
        /// 当前输入配置
        /// </summary>
        public InputConfiguration CurrentConfig { get; private set; } = new InputConfiguration();

        /// <summary>
        /// 默认输入配置
        /// </summary>
        private InputConfiguration _defaultConfig;

        /// <summary>
        /// 高级输入管理器引用
        /// </summary>
        private AdvancedInputManager _inputManager;

        #endregion

        #region 配置预设

        /// <summary>
        /// 配置预设字典
        /// </summary>
        private Dictionary<string, InputConfiguration> _configPresets = new Dictionary<string, InputConfiguration>();

        #endregion

        #region 事件系统

        /// <summary>
        /// 配置加载完成事件
        /// </summary>
        public event Action<InputConfiguration> OnConfigurationLoaded;

        /// <summary>
        /// 配置保存完成事件
        /// </summary>
        public event Action<InputConfiguration> OnConfigurationSaved;

        /// <summary>
        /// 配置重置事件
        /// </summary>
        public event Action<InputConfiguration> OnConfigurationReset;

        #endregion

        #region 生命周期方法

        public override void OnStart()
        {
            // 获取高级输入管理器引用
            _inputManager = Actor.GetScript<AdvancedInputManager>();
            if (_inputManager == null)
            {
                _inputManager = Actor.Parent?.GetScript<AdvancedInputManager>();
            }

            // 初始化默认配置
            InitializeDefaultConfiguration();

            // 初始化配置预设
            InitializeConfigurationPresets();

            // 加载配置文件
            LoadConfiguration();
        }

        public override void OnUpdate()
        {
            // 自动保存配置
            if (AutoSaveConfig && _configModified && Time.GameTime - _lastSaveTime >= AutoSaveInterval)
            {
                SaveConfiguration();
            }
        }

        #endregion

        #region 配置初始化

        /// <summary>
        /// 初始化默认配置
        /// </summary>
        private void InitializeDefaultConfiguration()
        {
            _defaultConfig = new InputConfiguration
            {
                MouseSensitivity = 1.0f,
                GamepadSensitivity = 1.0f,
                DeadZoneThreshold = 0.1f,
                MouseSmoothing = 0.1f,
                InputBufferTime = 0.1f,
                EnableGamepadSupport = true,
                KeyboardMappings = CreateDefaultKeyboardMappings(),
                GamepadMappings = CreateDefaultGamepadMappings()
            };

            CurrentConfig = CloneConfiguration(_defaultConfig);
        }

        /// <summary>
        /// 创建默认键盘映射
        /// </summary>
        /// <returns>键盘映射字典</returns>
        private Dictionary<string, KeyMappingData> CreateDefaultKeyboardMappings()
        {
            return new Dictionary<string, KeyMappingData>
            {
                { "MoveForward", new KeyMappingData { PrimaryKey = KeyboardKeys.W, SecondaryKey = KeyboardKeys.ArrowUp } },
                { "MoveBackward", new KeyMappingData { PrimaryKey = KeyboardKeys.S, SecondaryKey = KeyboardKeys.ArrowDown } },
                { "MoveLeft", new KeyMappingData { PrimaryKey = KeyboardKeys.A, SecondaryKey = KeyboardKeys.ArrowLeft } },
                { "MoveRight", new KeyMappingData { PrimaryKey = KeyboardKeys.D, SecondaryKey = KeyboardKeys.ArrowRight } },
                { "Jump", new KeyMappingData { PrimaryKey = KeyboardKeys.Spacebar } },
                { "Run", new KeyMappingData { PrimaryKey = KeyboardKeys.Shift } },
                { "Sprint", new KeyMappingData { PrimaryKey = KeyboardKeys.Shift } },
                { "Crouch", new KeyMappingData { PrimaryKey = KeyboardKeys.C } },
                { "CameraControl", new KeyMappingData { PrimaryMouseButton = MouseButton.Right } },
                { "ToggleFollowRotation", new KeyMappingData { PrimaryKey = KeyboardKeys.Alt } },
                { "SwitchCameraMode", new KeyMappingData { PrimaryKey = KeyboardKeys.V } },
                { "GroundClick", new KeyMappingData { PrimaryMouseButton = MouseButton.Left } }
            };
        }

        /// <summary>
        /// 创建默认手柄映射
        /// </summary>
        /// <returns>手柄映射字典</returns>
        private Dictionary<string, GamepadMappingData> CreateDefaultGamepadMappings()
        {
            return new Dictionary<string, GamepadMappingData>
            {
                { "MoveHorizontal", new GamepadMappingData { Axis = GamepadAxis.LeftStickX } },
                { "MoveVertical", new GamepadMappingData { Axis = GamepadAxis.LeftStickY } },
                { "CameraHorizontal", new GamepadMappingData { Axis = GamepadAxis.RightStickX } },
                { "CameraVertical", new GamepadMappingData { Axis = GamepadAxis.RightStickY } },
                { "Jump", new GamepadMappingData { PrimaryButton = GamepadButton.A } },
                { "Run", new GamepadMappingData { PrimaryButton = GamepadButton.LeftShoulder } },
                { "Sprint", new GamepadMappingData { Trigger = GamepadAxis.LeftTrigger } },
                { "Crouch", new GamepadMappingData { PrimaryButton = GamepadButton.B } },
                { "CameraControl", new GamepadMappingData { PrimaryButton = GamepadButton.RightShoulder } },
                { "ToggleFollowRotation", new GamepadMappingData { PrimaryButton = GamepadButton.RightThumb } },
                { "SwitchCameraMode", new GamepadMappingData { PrimaryButton = GamepadButton.Y } },
                { "GroundClick", new GamepadMappingData { PrimaryButton = GamepadButton.X } }
            };
        }

        /// <summary>
        /// 初始化配置预设
        /// </summary>
        private void InitializeConfigurationPresets()
        {
            // 默认预设
            _configPresets["Default"] = CloneConfiguration(_defaultConfig);

            // WASD预设
            var wasdConfig = CloneConfiguration(_defaultConfig);
            _configPresets["WASD"] = wasdConfig;

            // 箭头键预设
            var arrowConfig = CloneConfiguration(_defaultConfig);
            arrowConfig.KeyboardMappings["MoveForward"] = new KeyMappingData { PrimaryKey = KeyboardKeys.ArrowUp };
            arrowConfig.KeyboardMappings["MoveBackward"] = new KeyMappingData { PrimaryKey = KeyboardKeys.ArrowDown };
            arrowConfig.KeyboardMappings["MoveLeft"] = new KeyMappingData { PrimaryKey = KeyboardKeys.ArrowLeft };
            arrowConfig.KeyboardMappings["MoveRight"] = new KeyMappingData { PrimaryKey = KeyboardKeys.ArrowRight };
            _configPresets["Arrows"] = arrowConfig;

            // 高敏感度预设
            var highSensConfig = CloneConfiguration(_defaultConfig);
            highSensConfig.MouseSensitivity = 2.0f;
            highSensConfig.GamepadSensitivity = 2.0f;
            _configPresets["HighSensitivity"] = highSensConfig;

            // 低敏感度预设
            var lowSensConfig = CloneConfiguration(_defaultConfig);
            lowSensConfig.MouseSensitivity = 0.5f;
            lowSensConfig.GamepadSensitivity = 0.5f;
            _configPresets["LowSensitivity"] = lowSensConfig;
        }

        #endregion

        #region 配置文件操作

        /// <summary>
        /// 加载配置文件
        /// </summary>
        public void LoadConfiguration()
        {
            try
            {
                string fullPath = GetFullConfigPath();
                
                if (File.Exists(fullPath))
                {
                    string json = File.ReadAllText(fullPath);
                    var loadedConfig = JsonConvert.DeserializeObject<InputConfiguration>(json);
                    
                    if (loadedConfig != null)
                    {
                        CurrentConfig = loadedConfig;
                        ApplyConfiguration();
                        OnConfigurationLoaded?.Invoke(CurrentConfig);
                        Debug.Log("输入配置已加载");
                    }
                    else
                    {
                        Debug.LogWarning("配置文件格式错误，使用默认配置");
                        ResetToDefault();
                    }
                }
                else
                {
                    Debug.Log("配置文件不存在，使用默认配置");
                    ResetToDefault();
                    SaveConfiguration(); // 创建默认配置文件
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"加载配置文件失败: {ex.Message}");
                ResetToDefault();
            }
        }

        /// <summary>
        /// 保存配置文件
        /// </summary>
        public void SaveConfiguration()
        {
            try
            {
                string fullPath = GetFullConfigPath();
                
                // 确保目录存在
                string directory = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonConvert.SerializeObject(CurrentConfig, Formatting.Indented);
                File.WriteAllText(fullPath, json);
                
                _configModified = false;
                _lastSaveTime = Time.GameTime;
                OnConfigurationSaved?.Invoke(CurrentConfig);
                Debug.Log("输入配置已保存");
            }
            catch (Exception ex)
            {
                Debug.LogError($"保存配置文件失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取完整配置文件路径
        /// </summary>
        /// <returns>完整路径</returns>
        private string GetFullConfigPath()
        {
            return Path.Combine(Globals.ProjectFolder, ConfigFilePath);
        }

        #endregion

        #region 配置应用

        /// <summary>
        /// 应用配置到输入管理器
        /// </summary>
        private void ApplyConfiguration()
        {
            if (_inputManager == null) return;

            // 应用敏感度设置
            _inputManager.MouseSensitivity = CurrentConfig.MouseSensitivity;
            _inputManager.GamepadSensitivity = CurrentConfig.GamepadSensitivity;
            _inputManager.DeadZoneThreshold = CurrentConfig.DeadZoneThreshold;
            _inputManager.MouseSmoothing = CurrentConfig.MouseSmoothing;
            _inputManager.InputBufferTime = CurrentConfig.InputBufferTime;
            _inputManager.EnableGamepadSupport = CurrentConfig.EnableGamepadSupport;

            // 应用按键映射
            ApplyKeyboardMappings();
            ApplyGamepadMappings();
        }

        /// <summary>
        /// 应用键盘映射
        /// </summary>
        private void ApplyKeyboardMappings()
        {
            foreach (var mapping in CurrentConfig.KeyboardMappings)
            {
                var inputMapping = new InputMapping
                {
                    KeyboardKey = mapping.Value.PrimaryKey,
                    MouseButton = mapping.Value.PrimaryMouseButton,
                    ActionType = InputActionType.Button
                };

                _inputManager.SetInputMapping(mapping.Key, inputMapping);
            }
        }

        /// <summary>
        /// 应用手柄映射
        /// </summary>
        private void ApplyGamepadMappings()
        {
            foreach (var mapping in CurrentConfig.GamepadMappings)
            {
                var inputMapping = new InputMapping
                {
                    GamepadButton = mapping.Value.PrimaryButton,
                    ActionType = mapping.Value.Axis != GamepadAxis.None ? InputActionType.Axis : InputActionType.Button
                };

                _inputManager.SetInputMapping(mapping.Key, inputMapping);
            }
        }

        #endregion

        #region 公共接口

        /// <summary>
        /// 设置按键映射
        /// </summary>
        /// <param name="actionName">动作名称</param>
        /// <param name="keyMapping">按键映射数据</param>
        public void SetKeyMapping(string actionName, KeyMappingData keyMapping)
        {
            CurrentConfig.KeyboardMappings[actionName] = keyMapping;
            _configModified = true;
            ApplyConfiguration();
        }

        /// <summary>
        /// 设置手柄映射
        /// </summary>
        /// <param name="actionName">动作名称</param>
        /// <param name="gamepadMapping">手柄映射数据</param>
        public void SetGamepadMapping(string actionName, GamepadMappingData gamepadMapping)
        {
            CurrentConfig.GamepadMappings[actionName] = gamepadMapping;
            _configModified = true;
            ApplyConfiguration();
        }

        /// <summary>
        /// 设置鼠标敏感度
        /// </summary>
        /// <param name="sensitivity">敏感度</param>
        public void SetMouseSensitivity(float sensitivity)
        {
            CurrentConfig.MouseSensitivity = Mathf.Clamp(sensitivity, 0.1f, 5.0f);
            _configModified = true;
            ApplyConfiguration();
        }

        /// <summary>
        /// 设置手柄敏感度
        /// </summary>
        /// <param name="sensitivity">敏感度</param>
        public void SetGamepadSensitivity(float sensitivity)
        {
            CurrentConfig.GamepadSensitivity = Mathf.Clamp(sensitivity, 0.1f, 5.0f);
            _configModified = true;
            ApplyConfiguration();
        }

        /// <summary>
        /// 应用预设配置
        /// </summary>
        /// <param name="presetName">预设名称</param>
        public void ApplyPreset(string presetName)
        {
            if (_configPresets.ContainsKey(presetName))
            {
                CurrentConfig = CloneConfiguration(_configPresets[presetName]);
                _configModified = true;
                ApplyConfiguration();
                Debug.Log($"已应用预设配置: {presetName}");
            }
            else
            {
                Debug.LogWarning($"预设配置不存在: {presetName}");
            }
        }

        /// <summary>
        /// 重置为默认配置
        /// </summary>
        public void ResetToDefault()
        {
            CurrentConfig = CloneConfiguration(_defaultConfig);
            _configModified = true;
            ApplyConfiguration();
            OnConfigurationReset?.Invoke(CurrentConfig);
            Debug.Log("已重置为默认输入配置");
        }

        /// <summary>
        /// 获取可用的预设列表
        /// </summary>
        /// <returns>预设名称列表</returns>
        public List<string> GetAvailablePresets()
        {
            return new List<string>(_configPresets.Keys);
        }

        /// <summary>
        /// 强制保存配置
        /// </summary>
        public void ForceSave()
        {
            SaveConfiguration();
        }

        /// <summary>
        /// 检查配置是否已修改
        /// </summary>
        /// <returns>是否已修改</returns>
        public bool IsConfigModified()
        {
            return _configModified;
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 克隆配置
        /// </summary>
        /// <param name="config">源配置</param>
        /// <returns>克隆的配置</returns>
        private InputConfiguration CloneConfiguration(InputConfiguration config)
        {
            string json = JsonConvert.SerializeObject(config);
            return JsonConvert.DeserializeObject<InputConfiguration>(json);
        }

        #endregion
    }

    #region 配置数据结构

    /// <summary>
    /// 输入配置
    /// </summary>
    [System.Serializable]
    public class InputConfiguration
    {
        public float MouseSensitivity { get; set; } = 1.0f;
        public float GamepadSensitivity { get; set; } = 1.0f;
        public float DeadZoneThreshold { get; set; } = 0.1f;
        public float MouseSmoothing { get; set; } = 0.1f;
        public float InputBufferTime { get; set; } = 0.1f;
        public bool EnableGamepadSupport { get; set; } = true;
        public Dictionary<string, KeyMappingData> KeyboardMappings { get; set; } = new Dictionary<string, KeyMappingData>();
        public Dictionary<string, GamepadMappingData> GamepadMappings { get; set; } = new Dictionary<string, GamepadMappingData>();
    }

    /// <summary>
    /// 按键映射数据
    /// </summary>
    [System.Serializable]
    public struct KeyMappingData
    {
        public KeyboardKeys PrimaryKey { get; set; }
        public KeyboardKeys SecondaryKey { get; set; }
        public MouseButton PrimaryMouseButton { get; set; }
        public MouseButton SecondaryMouseButton { get; set; }
    }

    /// <summary>
    /// 手柄映射数据
    /// </summary>
    [System.Serializable]
    public struct GamepadMappingData
    {
        public GamepadButton PrimaryButton { get; set; }
        public GamepadButton SecondaryButton { get; set; }
        public GamepadAxis Axis { get; set; }
        public GamepadAxis Trigger { get; set; }
    }

    #endregion
}