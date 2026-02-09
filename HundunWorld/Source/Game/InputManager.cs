using FlaxEngine;
using System.Collections.Generic;
using System.Linq;
using System;
using System.IO;
using System.Text.Json;

namespace HundunWorld.Game
{
    /// <summary>
    /// 输入管理器，优化输入处理系统
    /// </summary>
    public class InputManager : Script
    {
        /// <summary>
        /// 输入绑定配置
        /// </summary>
        public class InputBinding
        {
            public string ActionName;
            public List<KeyboardKeys> Keys;
            public List<MouseButton> MouseButtons;
            public float DeadZone;
            public bool Enabled;
            
            public InputBinding(string actionName)
            {
                ActionName = actionName;
                Keys = new List<KeyboardKeys>();
                MouseButtons = new List<MouseButton>();
                DeadZone = 0.1f;
                Enabled = true;
            }
        }
        
        /// <summary>
        /// 手势识别结构
        /// </summary>
        public struct GestureData
        {
            public string ActionName;
            public float LastTriggerTime;
            public int TriggerCount;
            public Vector2 StartPosition;
            public Vector2 CurrentPosition;
        }
        
        /// <summary>
        /// 组合键配置
        /// </summary>
        public class ComboBinding
        {
            public string ActionName;
            public List<string> RequiredActions;
            public float TimeWindow;
            public bool RequireSimultaneous;
            
            public ComboBinding(string actionName)
            {
                ActionName = actionName;
                RequiredActions = new List<string>();
                TimeWindow = 0.5f;
                RequireSimultaneous = false;
            }
        }
        
        // 输入绑定字典
        private Dictionary<string, InputBinding> _inputBindings = new Dictionary<string, InputBinding>();
        
        // 按键状态字典
        private Dictionary<string, bool> _actionStates = new Dictionary<string, bool>();
        
        // 按键按下帧状态字典
        private Dictionary<string, bool> _actionDownStates = new Dictionary<string, bool>();
        
        // 按键抬起帧状态字典
        private Dictionary<string, bool> _actionUpStates = new Dictionary<string, bool>();
        
        // 手势识别数据
        private Dictionary<string, GestureData> _gestureData = new Dictionary<string, GestureData>();
        
        // 组合键绑定
        private Dictionary<string, ComboBinding> _comboBindings = new Dictionary<string, ComboBinding>();
        
        // 组合键状态跟踪
        private Dictionary<string, Queue<float>> _comboActionTimes = new Dictionary<string, Queue<float>>();
        
        // 双击检测参数
        [Tooltip("双击检测时间窗口（秒）")]
        public float DoubleClickWindow { get; set; } = 0.3f;
        
        // 长按检测参数
        [Tooltip("长按检测时间阈值（秒）")]
        public float LongPressThreshold { get; set; } = 1.0f;
        
        // 手势识别参数
        [Tooltip("手势识别最小距离")]
        public float GestureMinDistance { get; set; } = 50.0f;
        
        // 配置文件路径
        [Tooltip("输入配置文件路径")]
        public string ConfigFilePath { get; set; } = "Settings/Input Settings.json";
        
        // 是否启用输入预测
        [Tooltip("是否启用输入预测")]
        public bool EnableInputPrediction { get; set; } = true;
        
        // 输入预测缓冲
        private Queue<InputPredictionData> _inputPredictionBuffer = new Queue<InputPredictionData>();
        
        /// <summary>
        /// 输入预测数据
        /// </summary>
        private struct InputPredictionData
        {
            public string ActionName;
            public bool State;
            public float Timestamp;
        }
        
        public override void OnStart()
        {
            // 初始化默认输入绑定
            InitializeDefaultBindings();
            
            // 加载配置文件
            LoadConfiguration();
            
            // 初始化组合键绑定
            InitializeComboBindings();
        }
        
        public override void OnUpdate()
        {
            // 更新所有输入状态
            UpdateInputStates();
            
            // 更新手势识别
            UpdateGestureRecognition();
            
            // 更新组合键检测
            UpdateComboDetection();
            
            // 更新输入预测
            if (EnableInputPrediction)
            {
                UpdateInputPrediction();
            }
        }
        
        /// <summary>
        /// 初始化默认输入绑定
        /// </summary>
        private void InitializeDefaultBindings()
        {
            // 移动输入
            var moveForward = new InputBinding("MoveForward");
            moveForward.Keys.Add(KeyboardKeys.W);
            moveForward.Keys.Add(KeyboardKeys.ArrowUp);
            _inputBindings.Add("MoveForward", moveForward);
            
            var moveBackward = new InputBinding("MoveBackward");
            moveBackward.Keys.Add(KeyboardKeys.S);
            moveBackward.Keys.Add(KeyboardKeys.ArrowDown);
            _inputBindings.Add("MoveBackward", moveBackward);
            
            var moveLeft = new InputBinding("MoveLeft");
            moveLeft.Keys.Add(KeyboardKeys.A);
            moveLeft.Keys.Add(KeyboardKeys.ArrowLeft);
            _inputBindings.Add("MoveLeft", moveLeft);
            
            var moveRight = new InputBinding("MoveRight");
            moveRight.Keys.Add(KeyboardKeys.D);
            moveRight.Keys.Add(KeyboardKeys.ArrowRight);
            _inputBindings.Add("MoveRight", moveRight);
            
            // 跑步输入
            var run = new InputBinding("Run");
            run.Keys.Add(KeyboardKeys.Shift);
            _inputBindings.Add("Run", run);
            
            // 跳跃输入
            var jump = new InputBinding("Jump");
            jump.Keys.Add(KeyboardKeys.Spacebar);
            _inputBindings.Add("Jump", jump);
            
            // 蹲伏输入
            var crouch = new InputBinding("Crouch");
            crouch.Keys.Add(KeyboardKeys.C);
            _inputBindings.Add("Crouch", crouch);
            
            // 相机控制输入
            var cameraControl = new InputBinding("CameraControl");
            cameraControl.MouseButtons.Add(MouseButton.Right);
            _inputBindings.Add("CameraControl", cameraControl);
            
            // 地面点击输入
            var groundClick = new InputBinding("GroundClick");
            groundClick.MouseButtons.Add(MouseButton.Left);
            _inputBindings.Add("GroundClick", groundClick);
            
            // 相机模式切换输入
            var switchCameraMode = new InputBinding("SwitchCameraMode");
            switchCameraMode.Keys.Add(KeyboardKeys.V);
            _inputBindings.Add("SwitchCameraMode", switchCameraMode);
            
            // 攀爬输入
            var climb = new InputBinding("Climb");
            climb.Keys.Add(KeyboardKeys.F);
            _inputBindings.Add("Climb", climb);
            
            // 初始化状态字典
            foreach (var binding in _inputBindings.Values)
            {
                _actionStates[binding.ActionName] = false;
                _actionDownStates[binding.ActionName] = false;
                _actionUpStates[binding.ActionName] = false;
            }
        }
        
        /// <summary>
        /// 更新输入状态
        /// </summary>
        private void UpdateInputStates()
        {
            // 重置按下和抬起状态
            foreach (var action in _actionStates.Keys)
            {
                _actionDownStates[action] = false;
                _actionUpStates[action] = false;
            }
            
            // 更新每个动作的状态
            foreach (var binding in _inputBindings.Values)
            {
                bool previousState = _actionStates[binding.ActionName];
                bool currentState = false;
                
                // 检查键盘按键
                foreach (var key in binding.Keys)
                {
                    if (Input.GetKey(key))
                    {
                        currentState = true;
                        break;
                    }
                }
                
                // 检查鼠标按键
                if (!currentState)
                {
                    foreach (var button in binding.MouseButtons)
                    {
                        if (Input.GetMouseButton(button))
                        {
                            currentState = true;
                            break;
                        }
                    }
                }
                
                // 更新状态
                _actionStates[binding.ActionName] = currentState;
                
                // 更新按下和抬起状态
                if (!previousState && currentState)
                {
                    _actionDownStates[binding.ActionName] = true;
                }
                else if (previousState && !currentState)
                {
                    _actionUpStates[binding.ActionName] = true;
                }
            }
        }
        
        /// <summary>
        /// 检查动作是否处于激活状态
        /// </summary>
        /// <param name="actionName">动作名称</param>
        /// <returns>是否激活</returns>
        public bool IsActionPressed(string actionName)
        {
            if (_actionStates.ContainsKey(actionName))
            {
                return _actionStates[actionName];
            }
            return false;
        }
        
        /// <summary>
        /// 检查动作是否在当前帧按下
        /// </summary>
        /// <param name="actionName">动作名称</param>
        /// <returns>是否按下</returns>
        public bool IsActionDown(string actionName)
        {
            if (_actionDownStates.ContainsKey(actionName))
            {
                return _actionDownStates[actionName];
            }
            return false;
        }
        
        /// <summary>
        /// 检查双击动作
        /// </summary>
        /// <param name="actionName">动作名称</param>
        /// <returns>是否双击</returns>
        public bool IsActionDoubleClick(string actionName)
        {
            if (!_gestureData.ContainsKey(actionName))
                return false;
                
            var gesture = _gestureData[actionName];
            return gesture.TriggerCount >= 2 && 
                   (Time.GameTime - gesture.LastTriggerTime) <= DoubleClickWindow;
        }
        
        /// <summary>
        /// 检查长按动作
        /// </summary>
        /// <param name="actionName">动作名称</param>
        /// <returns>是否长按</returns>
        public bool IsActionLongPress(string actionName)
        {
            if (!_gestureData.ContainsKey(actionName))
                return false;
                
            var gesture = _gestureData[actionName];
            return IsActionPressed(actionName) && 
                   (Time.GameTime - gesture.LastTriggerTime) >= LongPressThreshold;
        }
        
        /// <summary>
        /// 检查组合键动作
        /// </summary>
        /// <param name="comboName">组合键名称</param>
        /// <returns>是否触发组合键</returns>
        public bool IsComboTriggered(string comboName)
        {
            if (!_comboBindings.ContainsKey(comboName))
                return false;
                
            var combo = _comboBindings[comboName];
            
            if (combo.RequireSimultaneous)
            {
                // 同时按下所有按键
                return combo.RequiredActions.All(action => IsActionPressed(action));
            }
            else
            {
                // 在时间窗口内顺序按下
                return CheckSequentialCombo(combo);
            }
        }
        
        /// <summary>
        /// 获取输入强度（用于模拟量化输入）
        /// </summary>
        /// <param name="actionName">动作名称</param>
        /// <returns>输入强度 (0-1)</returns>
        public float GetActionStrength(string actionName)
        {
            // 对于数字输入，返回0或1
            return IsActionPressed(actionName) ? 1.0f : 0.0f;
        }
        
        /// <summary>
        /// 获取输入方向向量（用于手势识别）
        /// </summary>
        /// <param name="actionName">动作名称</param>
        /// <returns>方向向量</returns>
        public Vector2 GetGestureDirection(string actionName)
        {
            if (!_gestureData.ContainsKey(actionName))
                return Vector2.Zero;
                
            var gesture = _gestureData[actionName];
            Vector2 direction = gesture.CurrentPosition - gesture.StartPosition;
            
            if (direction.Length >= GestureMinDistance)
            {
                return direction.Normalized;
            }
            
            return Vector2.Zero;
        }
        
        /// <summary>
        /// 添加自定义输入绑定
        /// </summary>
        /// <param name="actionName">动作名称</param>
        /// <param name="keys">键盘按键列表</param>
        /// <param name="mouseButtons">鼠标按键列表</param>
        public void AddInputBinding(string actionName, List<KeyboardKeys> keys = null, List<MouseButton> mouseButtons = null)
        {
            var binding = new InputBinding(actionName);
            if (keys != null)
            {
                binding.Keys.AddRange(keys);
            }
            if (mouseButtons != null)
            {
                binding.MouseButtons.AddRange(mouseButtons);
            }
            
            _inputBindings[actionName] = binding;
            
            // 初始化状态
            _actionStates[actionName] = false;
            _actionDownStates[actionName] = false;
            _actionUpStates[actionName] = false;
        }
        
        /// <summary>
        /// 初始化组合键绑定
        /// </summary>
        private void InitializeComboBindings()
        {
            // 添加一些默认的组合键
            var ctrlRun = new ComboBinding("CtrlRun")
            {
                RequiredActions = { "Crouch", "Run" },
                RequireSimultaneous = true
            };
            _comboBindings.Add("CtrlRun", ctrlRun);
            
            var doubleJump = new ComboBinding("DoubleJump")
            {
                RequiredActions = { "Jump", "Jump" },
                TimeWindow = 0.4f,
                RequireSimultaneous = false
            };
            _comboBindings.Add("DoubleJump", doubleJump);
        }
        
        /// <summary>
        /// 更新手势识别
        /// </summary>
        private void UpdateGestureRecognition()
        {
            foreach (var binding in _inputBindings.Values)
            {
                if (!binding.Enabled) continue;
                
                bool isPressed = IsActionPressed(binding.ActionName);
                bool wasPressed = _gestureData.ContainsKey(binding.ActionName);
                
                if (isPressed && !wasPressed)
                {
                    // 开始手势
                    var gestureData = new GestureData
                    {
                        ActionName = binding.ActionName,
                        LastTriggerTime = Time.GameTime,
                        TriggerCount = 1,
                        StartPosition = Input.MouseScreenPosition,
                        CurrentPosition = Input.MouseScreenPosition
                    };
                    _gestureData[binding.ActionName] = gestureData;
                }
                else if (isPressed && wasPressed)
                {
                    // 更新手势
                    var gestureData = _gestureData[binding.ActionName];
                    gestureData.CurrentPosition = Input.MouseScreenPosition;
                    _gestureData[binding.ActionName] = gestureData;
                }
                else if (!isPressed && wasPressed)
                {
                    // 结束手势，检查双击
                    var gestureData = _gestureData[binding.ActionName];
                    if (Time.GameTime - gestureData.LastTriggerTime <= DoubleClickWindow)
                    {
                        gestureData.TriggerCount++;
                        gestureData.LastTriggerTime = Time.GameTime;
                        _gestureData[binding.ActionName] = gestureData;
                    }
                    else
                    {
                        _gestureData.Remove(binding.ActionName);
                    }
                }
            }
        }
        
        /// <summary>
        /// 更新组合键检测
        /// </summary>
        private void UpdateComboDetection()
        {
            foreach (var binding in _inputBindings.Values)
            {
                if (!binding.Enabled) continue;
                
                if (IsActionDown(binding.ActionName))
                {
                    // 记录按键时间
                    if (!_comboActionTimes.ContainsKey(binding.ActionName))
                    {
                        _comboActionTimes[binding.ActionName] = new Queue<float>();
                    }
                    
                    _comboActionTimes[binding.ActionName].Enqueue(Time.GameTime);
                    
                    // 清理过时的记录
                    while (_comboActionTimes[binding.ActionName].Count > 0 &&
                           Time.GameTime - _comboActionTimes[binding.ActionName].Peek() > 2.0f)
                    {
                        _comboActionTimes[binding.ActionName].Dequeue();
                    }
                }
            }
        }
        
        /// <summary>
        /// 检查顺序组合键
        /// </summary>
        /// <param name="combo">组合键配置</param>
        /// <returns>是否触发</returns>
        private bool CheckSequentialCombo(ComboBinding combo)
        {
            float currentTime = Time.GameTime;
            List<float> allTimes = new List<float>();
            
            foreach (string actionName in combo.RequiredActions)
            {
                if (_comboActionTimes.ContainsKey(actionName))
                {
                    allTimes.AddRange(_comboActionTimes[actionName].Where(t => currentTime - t <= combo.TimeWindow));
                }
            }
            
            // 按时间排序
            allTimes.Sort();
            
            // 检查是否按照正确顺序
            if (allTimes.Count >= combo.RequiredActions.Count)
            {
                // 取最近的N个按键事件
                var recentTimes = allTimes.TakeLast(combo.RequiredActions.Count).ToArray();
                return currentTime - recentTimes[0] <= combo.TimeWindow;
            }
            
            return false;
        }
        
        /// <summary>
        /// 更新输入预测
        /// </summary>
        private void UpdateInputPrediction()
        {
            // 清理过时的预测数据
            while (_inputPredictionBuffer.Count > 0 && 
                   Time.GameTime - _inputPredictionBuffer.Peek().Timestamp > 0.5f)
            {
                _inputPredictionBuffer.Dequeue();
            }
            
            // 添加当前输入到预测缓冲
            foreach (var binding in _inputBindings.Values)
            {
                if (!binding.Enabled) continue;
                
                bool currentState = IsActionPressed(binding.ActionName);
                _inputPredictionBuffer.Enqueue(new InputPredictionData
                {
                    ActionName = binding.ActionName,
                    State = currentState,
                    Timestamp = Time.GameTime
                });
            }
        }
        
        /// <summary>
        /// 加载配置文件
        /// </summary>
        private void LoadConfiguration()
        {
            try
            {
                var fullPath = Path.Combine(Globals.ProjectFolder, ConfigFilePath);
                if (!File.Exists(fullPath))
                {
                    Debug.Log($"输入配置文件不存在，使用默认配置: {fullPath}");
                    return;
                }

                var json = File.ReadAllText(fullPath);
                var configEntries = JsonSerializer.Deserialize<List<InputConfigEntry>>(json);
                if (configEntries == null) return;

                foreach (var entry in configEntries)
                {
                    if (string.IsNullOrEmpty(entry.ActionName)) continue;

                    if (_inputBindings.TryGetValue(entry.ActionName, out var binding))
                    {
                        // 更新已有绑定
                        if (entry.Keys != null)
                        {
                            binding.Keys.Clear();
                            foreach (var keyName in entry.Keys)
                            {
                                if (Enum.TryParse<KeyboardKeys>(keyName, out var key))
                                {
                                    binding.Keys.Add(key);
                                }
                            }
                        }

                        binding.Enabled = entry.Enabled;
                        binding.DeadZone = entry.DeadZone;
                    }
                }

                Debug.Log($"已加载输入配置: {fullPath}");
            }
            catch (Exception ex)
            {
                Debug.LogError("加载输入配置失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 保存配置文件
        /// </summary>
        public void SaveConfiguration()
        {
            try
            {
                var fullPath = Path.Combine(Globals.ProjectFolder, ConfigFilePath);
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var configEntries = new List<InputConfigEntry>();
                foreach (var kvp in _inputBindings)
                {
                    configEntries.Add(new InputConfigEntry
                    {
                        ActionName = kvp.Key,
                        Keys = kvp.Value.Keys.Select(k => k.ToString()).ToList(),
                        Enabled = kvp.Value.Enabled,
                        DeadZone = kvp.Value.DeadZone
                    });
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(configEntries, options);
                File.WriteAllText(fullPath, json);

                Debug.Log($"已保存输入配置: {fullPath}");
            }
            catch (Exception ex)
            {
                Debug.LogError("保存输入配置失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 输入配置序列化条目
        /// </summary>
        private class InputConfigEntry
        {
            public string ActionName { get; set; } = "";
            public List<string> Keys { get; set; } = new();
            public bool Enabled { get; set; } = true;
            public float DeadZone { get; set; } = 0.1f;
        }
    }
}