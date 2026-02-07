using FlaxEngine;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game
{
    /// <summary>
    /// 高级输入管理器，支持多种输入设备和输入映射配置
    /// </summary>
    public class AdvancedInputManager : Script
    {
        #region 输入设备管理

        /// <summary>
        /// 当前活跃的输入设备
        /// </summary>
        public InputDeviceType ActiveInputDevice { get; private set; } = InputDeviceType.KeyboardMouse;

        /// <summary>
        /// 是否支持手柄输入
        /// </summary>
        [Tooltip("是否支持手柄输入")]
        public bool EnableGamepadSupport { get; set; } = true;

        /// <summary>
        /// 手柄检测间隔（秒）
        /// </summary>
        [Tooltip("手柄检测间隔（秒）")]
        public float GamepadDetectionInterval { get; set; } = 1.0f;

        /// <summary>
        /// 输入设备切换阈值
        /// </summary>
        [Tooltip("输入设备切换阈值")]
        public float DeviceSwitchThreshold { get; set; } = 0.1f;

        /// <summary>
        /// 上次手柄检测时间
        /// </summary>
        private float _lastGamepadCheckTime = 0f;

        /// <summary>
        /// 手柄连接状态
        /// </summary>
        private bool _gamepadConnected = false;

        #endregion

        #region 输入映射配置

        /// <summary>
        /// 输入动作映射
        /// </summary>
        private Dictionary<string, InputMapping> _inputMappings = new Dictionary<string, InputMapping>();

        /// <summary>
        /// 输入缓冲
        /// </summary>
        private Dictionary<string, InputBuffer> _inputBuffers = new Dictionary<string, InputBuffer>();

        /// <summary>
        /// 输入缓冲时间（秒）
        /// </summary>
        [Tooltip("输入缓冲时间（秒）")]
        public float InputBufferTime { get; set; } = 0.1f;

        /// <summary>
        /// 输入历史记录
        /// </summary>
        private Queue<InputEvent> _inputHistory = new Queue<InputEvent>();

        /// <summary>
        /// 最大输入历史记录数量
        /// </summary>
        private const int MaxInputHistoryCount = 100;

        #endregion

        #region 输入敏感度设置

        /// <summary>
        /// 鼠标敏感度
        /// </summary>
        [Tooltip("鼠标敏感度")]
        public float MouseSensitivity { get; set; } = 1.0f;

        /// <summary>
        /// 手柄摇杆敏感度
        /// </summary>
        [Tooltip("手柄摇杆敏感度")]
        public float GamepadSensitivity { get; set; } = 1.0f;

        /// <summary>
        /// 死区阈值
        /// </summary>
        [Tooltip("死区阈值")]
        public float DeadZoneThreshold { get; set; } = 0.1f;

        /// <summary>
        /// 鼠标平滑
        /// </summary>
        [Tooltip("鼠标平滑")]
        public float MouseSmoothing { get; set; } = 0.1f;

        #endregion

        #region 事件系统

        /// <summary>
        /// 输入设备切换事件
        /// </summary>
        public event Action<InputDeviceType> OnInputDeviceChanged;

        /// <summary>
        /// 动作触发事件
        /// </summary>
        public event Action<string> OnActionTriggered;

        /// <summary>
        /// 动作释放事件
        /// </summary>
        public event Action<string> OnActionReleased;

        #endregion

        #region 生命周期方法

        public override void OnStart()
        {
            InitializeInputMappings();
            InitializeInputBuffers();
            
            // 检测初始手柄状态
            CheckGamepadConnection();
        }

        public override void OnUpdate()
        {
            // 定期检测手柄连接状态
            if (EnableGamepadSupport && Time.GameTime - _lastGamepadCheckTime >= GamepadDetectionInterval)
            {
                CheckGamepadConnection();
                _lastGamepadCheckTime = Time.GameTime;
            }

            // 检测输入设备切换
            DetectInputDeviceSwitch();

            // 更新输入映射
            UpdateInputMappings();

            // 更新输入缓冲
            UpdateInputBuffers();

            // 清理过期的输入历史
            CleanupInputHistory();
        }

        #endregion

        #region 输入映射初始化

        /// <summary>
        /// 初始化输入映射
        /// </summary>
        private void InitializeInputMappings()
        {
            // 移动相关映射
            _inputMappings["MoveForward"] = new InputMapping
            {
                KeyboardKey = KeyboardKeys.W,
                GamepadButton = GamepadButton.LeftStickUp,
                ActionType = InputActionType.Axis
            };

            _inputMappings["MoveBackward"] = new InputMapping
            {
                KeyboardKey = KeyboardKeys.S,
                GamepadButton = GamepadButton.LeftStickDown,
                ActionType = InputActionType.Axis
            };

            _inputMappings["MoveLeft"] = new InputMapping
            {
                KeyboardKey = KeyboardKeys.A,
                GamepadButton = GamepadButton.LeftStickLeft,
                ActionType = InputActionType.Axis
            };

            _inputMappings["MoveRight"] = new InputMapping
            {
                KeyboardKey = KeyboardKeys.D,
                GamepadButton = GamepadButton.LeftStickRight,
                ActionType = InputActionType.Axis
            };

            // 动作相关映射
            _inputMappings["Jump"] = new InputMapping
            {
                KeyboardKey = KeyboardKeys.Spacebar,
                GamepadButton = GamepadButton.A,
                ActionType = InputActionType.Button
            };

            _inputMappings["Run"] = new InputMapping
            {
                KeyboardKey = KeyboardKeys.Shift,
                GamepadButton = GamepadButton.LeftShoulder,
                ActionType = InputActionType.Button
            };

            _inputMappings["Sprint"] = new InputMapping
            {
                KeyboardKey = KeyboardKeys.Shift,
                GamepadButton = GamepadButton.LeftTrigger,
                ActionType = InputActionType.Button
            };

            _inputMappings["Crouch"] = new InputMapping
            {
                KeyboardKey = KeyboardKeys.C,
                GamepadButton = GamepadButton.B,
                ActionType = InputActionType.Button
            };

            // 相机相关映射
            _inputMappings["CameraControl"] = new InputMapping
            {
                MouseButton = MouseButton.Right,
                GamepadButton = GamepadButton.RightShoulder,
                ActionType = InputActionType.Button
            };

            _inputMappings["ToggleFollowRotation"] = new InputMapping
            {
                KeyboardKey = KeyboardKeys.Alt,
                GamepadButton = GamepadButton.LeftStickRight,
                ActionType = InputActionType.Button
            };

            _inputMappings["SwitchCameraMode"] = new InputMapping
            {
                KeyboardKey = KeyboardKeys.V,
                GamepadButton = GamepadButton.Y,
                ActionType = InputActionType.Button
            };

            // 交互相关映射
            _inputMappings["GroundClick"] = new InputMapping
            {
                MouseButton = MouseButton.Left,
                GamepadButton = GamepadButton.X,
                ActionType = InputActionType.Button
            };
        }

        /// <summary>
        /// 初始化输入缓冲
        /// </summary>
        private void InitializeInputBuffers()
        {
            foreach (var mapping in _inputMappings)
            {
                _inputBuffers[mapping.Key] = new InputBuffer();
            }
        }

        #endregion

        #region 输入设备检测

        /// <summary>
        /// 检测手柄连接状态
        /// </summary>
        private void CheckGamepadConnection()
        {
            bool previousState = _gamepadConnected;
            _gamepadConnected = Input.GetGamepad(0) != null;

            if (previousState != _gamepadConnected)
            {
                Debug.Log($"手柄连接状态变化: {(_gamepadConnected ? "已连接" : "已断开")}");
            }
        }

        /// <summary>
        /// 检测输入设备切换
        /// </summary>
        private void DetectInputDeviceSwitch()
        {
            InputDeviceType newDevice = DetermineActiveInputDevice();
            
            if (newDevice != ActiveInputDevice)
            {
                ActiveInputDevice = newDevice;
                OnInputDeviceChanged?.Invoke(ActiveInputDevice);
                Debug.Log($"输入设备切换为: {ActiveInputDevice}");
            }
        }

        /// <summary>
        /// 确定当前活跃的输入设备
        /// </summary>
        /// <returns>活跃的输入设备类型</returns>
        private InputDeviceType DetermineActiveInputDevice()
        {
            // 检查键盘输入
            if (HasKeyboardInput())
            {
                return InputDeviceType.KeyboardMouse;
            }

            // 检查鼠标输入
            if (HasMouseInput())
            {
                return InputDeviceType.KeyboardMouse;
            }

            // 检查手柄输入
            if (_gamepadConnected && HasGamepadInput())
            {
                return InputDeviceType.Gamepad;
            }

            return ActiveInputDevice; // 保持当前设备
        }

        /// <summary>
        /// 检查是否有键盘输入
        /// </summary>
        /// <returns>是否有键盘输入</returns>
        private bool HasKeyboardInput()
        {
            return Input.GetKey(KeyboardKeys.W) || Input.GetKey(KeyboardKeys.A) || 
                   Input.GetKey(KeyboardKeys.S) || Input.GetKey(KeyboardKeys.D) ||
                   Input.GetKey(KeyboardKeys.Spacebar) || Input.GetKey(KeyboardKeys.Shift);
        }

        /// <summary>
        /// 检查是否有鼠标输入
        /// </summary>
        /// <returns>是否有鼠标输入</returns>
        private bool HasMouseInput()
        {
            return Mathf.Abs(Input.GetAxis("Mouse X")) > DeviceSwitchThreshold ||
                   Mathf.Abs(Input.GetAxis("Mouse Y")) > DeviceSwitchThreshold ||
                   Input.GetMouseButton(MouseButton.Left) || Input.GetMouseButton(MouseButton.Right);
        }

        /// <summary>
        /// 检查是否有手柄输入
        /// </summary>
        /// <returns>是否有手柄输入</returns>
        private bool HasGamepadInput()
        {
            var gamepad = Input.GetGamepad(0);
            if (gamepad == null) return false;

            return gamepad.GetAxis(GamepadAxis.LeftStickX) != 0 ||
                   gamepad.GetAxis(GamepadAxis.LeftStickY) != 0 ||
                   gamepad.GetAxis(GamepadAxis.RightStickX) != 0 ||
                   gamepad.GetAxis(GamepadAxis.RightStickY) != 0 ||
                   gamepad.GetButton(GamepadButton.A) ||
                   gamepad.GetButton(GamepadButton.B);
        }

        #endregion

        #region 输入处理

        /// <summary>
        /// 更新输入映射
        /// </summary>
        private void UpdateInputMappings()
        {
            foreach (var mapping in _inputMappings)
            {
                bool currentState = GetMappingState(mapping.Value);
                bool previousState = _inputBuffers[mapping.Key].CurrentState;

                // 更新缓冲状态
                _inputBuffers[mapping.Key].Update(currentState, Time.GameTime);

                // 触发事件
                if (currentState && !previousState)
                {
                    OnActionTriggered?.Invoke(mapping.Key);
                    RecordInputEvent(mapping.Key, InputEventType.Press);
                }
                else if (!currentState && previousState)
                {
                    OnActionReleased?.Invoke(mapping.Key);
                    RecordInputEvent(mapping.Key, InputEventType.Release);
                }
            }
        }

        /// <summary>
        /// 获取映射状态
        /// </summary>
        /// <param name="mapping">输入映射</param>
        /// <returns>映射状态</returns>
        private bool GetMappingState(InputMapping mapping)
        {
            switch (ActiveInputDevice)
            {
                case InputDeviceType.KeyboardMouse:
                    if (mapping.KeyboardKey != KeyboardKeys.None && Input.GetKey(mapping.KeyboardKey))
                        return true;
                    if (mapping.MouseButton != MouseButton.None && Input.GetMouseButton(mapping.MouseButton))
                        return true;
                    break;

                case InputDeviceType.Gamepad:
                    var gamepad = Input.GetGamepad(0);
                    if (gamepad != null && mapping.GamepadButton != GamepadButton.None)
                    {
                        return gamepad.GetButton(mapping.GamepadButton);
                    }
                    break;
            }

            return false;
        }

        /// <summary>
        /// 更新输入缓冲
        /// </summary>
        private void UpdateInputBuffers()
        {
            foreach (var buffer in _inputBuffers.Values)
            {
                buffer.UpdateBuffer(Time.GameTime, InputBufferTime);
            }
        }

        /// <summary>
        /// 记录输入事件
        /// </summary>
        /// <param name="actionName">动作名称</param>
        /// <param name="eventType">事件类型</param>
        private void RecordInputEvent(string actionName, InputEventType eventType)
        {
            var inputEvent = new InputEvent
            {
                ActionName = actionName,
                EventType = eventType,
                Timestamp = Time.GameTime,
                InputDevice = ActiveInputDevice
            };

            _inputHistory.Enqueue(inputEvent);
        }

        /// <summary>
        /// 清理过期的输入历史
        /// </summary>
        private void CleanupInputHistory()
        {
            while (_inputHistory.Count > MaxInputHistoryCount)
            {
                _inputHistory.Dequeue();
            }
        }

        #endregion

        #region 公共接口

        /// <summary>
        /// 检查动作是否被按下
        /// </summary>
        /// <param name="actionName">动作名称</param>
        /// <returns>是否被按下</returns>
        public bool IsActionPressed(string actionName)
        {
            return _inputBuffers.ContainsKey(actionName) && _inputBuffers[actionName].CurrentState;
        }

        /// <summary>
        /// 检查动作是否刚被按下
        /// </summary>
        /// <param name="actionName">动作名称</param>
        /// <returns>是否刚被按下</returns>
        public bool IsActionDown(string actionName)
        {
            return _inputBuffers.ContainsKey(actionName) && _inputBuffers[actionName].JustPressed;
        }

        /// <summary>
        /// 检查动作是否刚被释放
        /// </summary>
        /// <param name="actionName">动作名称</param>
        /// <returns>是否刚被释放</returns>
        public bool IsActionUp(string actionName)
        {
            return _inputBuffers.ContainsKey(actionName) && _inputBuffers[actionName].JustReleased;
        }

        /// <summary>
        /// 获取轴输入值
        /// </summary>
        /// <param name="axisName">轴名称</param>
        /// <returns>轴值</returns>
        public float GetAxisValue(string axisName)
        {
            switch (ActiveInputDevice)
            {
                case InputDeviceType.KeyboardMouse:
                    return GetKeyboardMouseAxisValue(axisName);

                case InputDeviceType.Gamepad:
                    return GetGamepadAxisValue(axisName);

                default:
                    return 0f;
            }
        }

        /// <summary>
        /// 获取键盘鼠标轴值
        /// </summary>
        /// <param name="axisName">轴名称</param>
        /// <returns>轴值</returns>
        private float GetKeyboardMouseAxisValue(string axisName)
        {
            switch (axisName)
            {
                case "Horizontal":
                    float horizontal = 0f;
                    if (Input.GetKey(KeyboardKeys.D)) horizontal += 1f;
                    if (Input.GetKey(KeyboardKeys.A)) horizontal -= 1f;
                    return horizontal;

                case "Vertical":
                    float vertical = 0f;
                    if (Input.GetKey(KeyboardKeys.W)) vertical += 1f;
                    if (Input.GetKey(KeyboardKeys.S)) vertical -= 1f;
                    return vertical;

                case "Mouse X":
                    return Input.GetAxis("Mouse X") * MouseSensitivity;

                case "Mouse Y":
                    return Input.GetAxis("Mouse Y") * MouseSensitivity;

                default:
                    return 0f;
            }
        }

        /// <summary>
        /// 获取手柄轴值
        /// </summary>
        /// <param name="axisName">轴名称</param>
        /// <returns>轴值</returns>
        private float GetGamepadAxisValue(string axisName)
        {
            var gamepad = Input.GetGamepad(0);
            if (gamepad == null) return 0f;

            float value = 0f;

            switch (axisName)
            {
                case "Horizontal":
                    value = gamepad.GetAxis(GamepadAxis.LeftStickX);
                    break;

                case "Vertical":
                    value = gamepad.GetAxis(GamepadAxis.LeftStickY);
                    break;

                case "Mouse X":
                    value = gamepad.GetAxis(GamepadAxis.RightStickX) * GamepadSensitivity;
                    break;

                case "Mouse Y":
                    value = gamepad.GetAxis(GamepadAxis.RightStickY) * GamepadSensitivity;
                    break;
            }

            // 应用死区
            if (Mathf.Abs(value) < DeadZoneThreshold)
            {
                value = 0f;
            }

            return value;
        }

        /// <summary>
        /// 设置输入映射
        /// </summary>
        /// <param name="actionName">动作名称</param>
        /// <param name="mapping">输入映射</param>
        public void SetInputMapping(string actionName, InputMapping mapping)
        {
            _inputMappings[actionName] = mapping;
            if (!_inputBuffers.ContainsKey(actionName))
            {
                _inputBuffers[actionName] = new InputBuffer();
            }
        }

        /// <summary>
        /// 获取输入历史
        /// </summary>
        /// <returns>输入历史</returns>
        public List<InputEvent> GetInputHistory()
        {
            return new List<InputEvent>(_inputHistory);
        }

        /// <summary>
        /// 清空输入历史
        /// </summary>
        public void ClearInputHistory()
        {
            _inputHistory.Clear();
        }

        #endregion
    }

    #region 输入相关结构和枚举

    /// <summary>
    /// 输入设备类型
    /// </summary>
    public enum InputDeviceType
    {
        /// <summary>
        /// 键盘鼠标
        /// </summary>
        KeyboardMouse,
        
        /// <summary>
        /// 手柄
        /// </summary>
        Gamepad
    }

    /// <summary>
    /// 输入动作类型
    /// </summary>
    public enum InputActionType
    {
        /// <summary>
        /// 按钮
        /// </summary>
        Button,
        
        /// <summary>
        /// 轴
        /// </summary>
        Axis
    }

    /// <summary>
    /// 输入事件类型
    /// </summary>
    public enum InputEventType
    {
        /// <summary>
        /// 按下
        /// </summary>
        Press,
        
        /// <summary>
        /// 释放
        /// </summary>
        Release
    }

    /// <summary>
    /// 输入映射
    /// </summary>
    public struct InputMapping
    {
        public KeyboardKeys KeyboardKey;
        public MouseButton MouseButton;
        public GamepadButton GamepadButton;
        public InputActionType ActionType;
    }

    /// <summary>
    /// 输入缓冲
    /// </summary>
    public class InputBuffer
    {
        public bool CurrentState { get; private set; }
        public bool JustPressed { get; private set; }
        public bool JustReleased { get; private set; }
        
        private bool _previousState = false;
        private float _pressTime = 0f;
        private float _releaseTime = 0f;

        public void Update(bool newState, float currentTime)
        {
            JustPressed = newState && !_previousState;
            JustReleased = !newState && _previousState;
            
            if (JustPressed)
            {
                _pressTime = currentTime;
            }
            else if (JustReleased)
            {
                _releaseTime = currentTime;
            }

            _previousState = CurrentState;
            CurrentState = newState;
        }

        public void UpdateBuffer(float currentTime, float bufferTime)
        {
            // 检查是否在缓冲时间内
            if (!CurrentState && currentTime - _pressTime <= bufferTime)
            {
                JustPressed = true;
            }
        }
    }

    /// <summary>
    /// 输入事件
    /// </summary>
    public struct InputEvent
    {
        public string ActionName;
        public InputEventType EventType;
        public float Timestamp;
        public InputDeviceType InputDevice;
    }

    #endregion
}