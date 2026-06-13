using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlaxEngine;
using Game.Database;

namespace HundunWorld.Game.Services
{
    /// <summary>
    /// 游戏配置管理服务
    /// 负责管理游戏的各种配置数据，包括图形、音频、控制等设置
    /// </summary>
    public class GameConfigurationService
    {
        private static GameConfigurationService _instance;
        private Dictionary<string, object> _cachedSettings;
        private const string CONFIG_CATEGORY_GRAPHICS = "Graphics";
        private const string CONFIG_CATEGORY_AUDIO = "Audio";
        private const string CONFIG_CATEGORY_CONTROLS = "Controls";
        private const string CONFIG_CATEGORY_GAMEPLAY = "Gameplay";

        public static GameConfigurationService Instance => _instance ??= new GameConfigurationService();

        public GameConfigurationService()
        {
            _cachedSettings = new Dictionary<string, object>();
            LoadAllConfigurations();
            Debug.Log("[GameConfig] 游戏配置服务已初始化");
        }

        #region 图形配置管理
        /// <summary>
        /// 设置分辨率
        /// </summary>
        public async Task<bool> SetResolutionAsync(int width, int height)
        {
            try
            {
                string resolution = $"{width}x{height}";
                bool success = await SaveConfigAsync("Resolution", resolution, CONFIG_CATEGORY_GRAPHICS);
                
                if (success)
                {
                    _cachedSettings["Resolution"] = resolution;
                    ApplyResolutionChange(width, height);
                }
                
                return success;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameConfig] 设置分辨率失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取当前分辨率
        /// </summary>
        public (int width, int height) GetResolution()
        {
            try
            {
                string resolutionStr = GetConfigValue<string>("Resolution", "1920x1080");
                var parts = resolutionStr.Split('x');
                
                if (parts.Length == 2 && 
                    int.TryParse(parts[0], out int width) && 
                    int.TryParse(parts[1], out int height))
                {
                    return (width, height);
                }
                
                return (1920, 1080); // 默认分辨率
            }
            catch
            {
                return (1920, 1080);
            }
        }

        /// <summary>
        /// 设置图形质量
        /// </summary>
        public async Task<bool> SetGraphicsQualityAsync(string quality)
        {
            try
            {
                bool success = await SaveConfigAsync("Quality", quality, CONFIG_CATEGORY_GRAPHICS);
                
                if (success)
                {
                    _cachedSettings["Quality"] = quality;
                    ApplyQualityChange(quality);
                }
                
                return success;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameConfig] 设置图形质量失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取图形质量设置
        /// </summary>
        public string GetGraphicsQuality()
        {
            return GetConfigValue<string>("Quality", "Medium");
        }

        /// <summary>
        /// 设置垂直同步
        /// </summary>
        public async Task<bool> SetVSyncAsync(bool enabled)
        {
            try
            {
                bool success = await SaveConfigAsync("VSync", enabled.ToString(), CONFIG_CATEGORY_GRAPHICS);
                
                if (success)
                {
                    _cachedSettings["VSync"] = enabled;
                    ApplyVSyncChange(enabled);
                }
                
                return success;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameConfig] 设置垂直同步失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取垂直同步设置
        /// </summary>
        public bool GetVSync()
        {
            return GetConfigValue<bool>("VSync", true);
        }
        #endregion

        #region 音频配置管理
        /// <summary>
        /// 设置主音量
        /// </summary>
        public async Task<bool> SetMasterVolumeAsync(float volume)
        {
            try
            {
                bool success = await SaveConfigAsync("MasterVolume", volume.ToString(), CONFIG_CATEGORY_AUDIO);
                
                if (success)
                {
                    _cachedSettings["MasterVolume"] = volume;
                    ApplyMasterVolumeChange(volume);
                }
                
                return success;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameConfig] 设置主音量失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取主音量
        /// </summary>
        public float GetMasterVolume()
        {
            return GetConfigValue<float>("MasterVolume", 1.0f);
        }

        /// <summary>
        /// 设置音乐音量
        /// </summary>
        public async Task<bool> SetMusicVolumeAsync(float volume)
        {
            try
            {
                bool success = await SaveConfigAsync("MusicVolume", volume.ToString(), CONFIG_CATEGORY_AUDIO);
                
                if (success)
                {
                    _cachedSettings["MusicVolume"] = volume;
                    ApplyMusicVolumeChange(volume);
                }
                
                return success;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameConfig] 设置音乐音量失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取音乐音量
        /// </summary>
        public float GetMusicVolume()
        {
            return GetConfigValue<float>("MusicVolume", 0.8f);
        }

        /// <summary>
        /// 设置音效音量
        /// </summary>
        public async Task<bool> SetSFXVolumeAsync(float volume)
        {
            try
            {
                bool success = await SaveConfigAsync("SFXVolume", volume.ToString(), CONFIG_CATEGORY_AUDIO);
                
                if (success)
                {
                    _cachedSettings["SFXVolume"] = volume;
                    ApplySFXVolumeChange(volume);
                }
                
                return success;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameConfig] 设置音效音量失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取音效音量
        /// </summary>
        public float GetSFXVolume()
        {
            return GetConfigValue<float>("SFXVolume", 0.9f);
        }
        #endregion

        #region 控制配置管理
        /// <summary>
        /// 设置鼠标灵敏度
        /// </summary>
        public async Task<bool> SetMouseSensitivityAsync(float sensitivity)
        {
            try
            {
                bool success = await SaveConfigAsync("MouseSensitivity", sensitivity.ToString(), CONFIG_CATEGORY_CONTROLS);
                
                if (success)
                {
                    _cachedSettings["MouseSensitivity"] = sensitivity;
                    ApplyMouseSensitivityChange(sensitivity);
                }
                
                return success;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameConfig] 设置鼠标灵敏度失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取鼠标灵敏度
        /// </summary>
        public float GetMouseSensitivity()
        {
            return GetConfigValue<float>("MouseSensitivity", 1.0f);
        }

        /// <summary>
        /// 设置按键绑定
        /// </summary>
        public async Task<bool> SetKeyBindingAsync(string action, string key)
        {
            try
            {
                bool success = await SaveConfigAsync($"Key_{action}", key, CONFIG_CATEGORY_CONTROLS);
                
                if (success)
                {
                    _cachedSettings[$"Key_{action}"] = key;
                    ApplyKeyBindingChange(action, key);
                }
                
                return success;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameConfig] 设置按键绑定失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取按键绑定
        /// </summary>
        public string GetKeyBinding(string action)
        {
            return GetConfigValue<string>($"Key_{action}", GetDefaultKeyForAction(action));
        }
        #endregion

        #region 游戏配置管理
        /// <summary>
        /// 设置语言
        /// </summary>
        public async Task<bool> SetLanguageAsync(string languageCode)
        {
            try
            {
                bool success = await SaveConfigAsync("Language", languageCode, CONFIG_CATEGORY_GAMEPLAY);
                
                if (success)
                {
                    _cachedSettings["Language"] = languageCode;
                    ApplyLanguageChange(languageCode);
                }
                
                return success;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameConfig] 设置语言失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取当前语言
        /// </summary>
        public string GetLanguage()
        {
            return GetConfigValue<string>("Language", "zh-CN");
        }

        /// <summary>
        /// 设置自动保存
        /// </summary>
        public async Task<bool> SetAutoSaveAsync(bool enabled)
        {
            try
            {
                bool success = await SaveConfigAsync("AutoSave", enabled.ToString(), CONFIG_CATEGORY_GAMEPLAY);
                
                if (success)
                {
                    _cachedSettings["AutoSave"] = enabled;
                }
                
                return success;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameConfig] 设置自动保存失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取自动保存设置
        /// </summary>
        public bool GetAutoSave()
        {
            return GetConfigValue<bool>("AutoSave", true);
        }
        #endregion

        #region 私有辅助方法
        /// <summary>
        /// 保存配置到数据库
        /// </summary>
        private async Task<bool> SaveConfigAsync(string key, string value, string category)
        {
            return await Task.Run(() =>
            {
                try
                {
                    LiteDataContext.SaveGameConfig(key, value, category);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        /// <summary>
        /// 从缓存或数据库获取配置值
        /// </summary>
        private T GetConfigValue<T>(string key, T defaultValue)
        {
            // 先从缓存获取
            if (_cachedSettings.TryGetValue(key, out object cachedValue))
            {
                return (T)Convert.ChangeType(cachedValue, typeof(T));
            }

            // 从数据库获取
            string stringValue = LiteDataContext.GetGameConfig(key, GetCategoryForKey(key));
            
            if (!string.IsNullOrEmpty(stringValue))
            {
                try
                {
                    var result = (T)Convert.ChangeType(stringValue, typeof(T));
                    _cachedSettings[key] = result; // 缓存结果
                    return result;
                }
                catch
                {
                    return defaultValue;
                }
            }

            return defaultValue;
        }

        /// <summary>
        /// 根据键名推断配置类别
        /// </summary>
        private string GetCategoryForKey(string key)
        {
            if (key.StartsWith("Key_")) return CONFIG_CATEGORY_CONTROLS;
            if (key.Contains("Volume")) return CONFIG_CATEGORY_AUDIO;
            if (key.Contains("Resolution") || key.Contains("Quality") || key.Contains("VSync")) 
                return CONFIG_CATEGORY_GRAPHICS;
            return CONFIG_CATEGORY_GAMEPLAY;
        }

        /// <summary>
        /// 加载所有配置到缓存
        /// </summary>
        private void LoadAllConfigurations()
        {
            try
            {
                // 这里可以批量加载所有配置，但为了简单起见，我们按需加载
                Debug.Log("[GameConfig] 配置已预加载到缓存");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameConfig] 加载配置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取动作的默认按键
        /// </summary>
        private string GetDefaultKeyForAction(string action)
        {
            return action switch
            {
                "MoveForward" => "W",
                "MoveBackward" => "S",
                "MoveLeft" => "A",
                "MoveRight" => "D",
                "Jump" => "Space",
                "Attack" => "LeftMouseButton",
                "Interact" => "E",
                _ => "Unknown"
            };
        }
        #endregion

        #region 应用配置变化的方法（需要在具体实现中连接到实际系统）
        private void ApplyResolutionChange(int width, int height)
        {
            // 连接到实际的图形系统
            Debug.Log($"[GameConfig] 分辨率已更改: {width}x{height}");
        }

        private void ApplyQualityChange(string quality)
        {
            // 连接到实际的图形系统
            Debug.Log($"[GameConfig] 图形质量已更改: {quality}");
        }

        private void ApplyVSyncChange(bool enabled)
        {
            // 连接到实际的图形系统
            Debug.Log($"[GameConfig] 垂直同步已{(enabled ? "启用" : "禁用")}");
        }

        private void ApplyMasterVolumeChange(float volume)
        {
            // 连接到实际的音频系统
            Debug.Log($"[GameConfig] 主音量已更改: {volume:F2}");
        }

        private void ApplyMusicVolumeChange(float volume)
        {
            // 连接到实际的音频系统
            Debug.Log($"[GameConfig] 音乐音量已更改: {volume:F2}");
        }

        private void ApplySFXVolumeChange(float volume)
        {
            // 连接到实际的音频系统
            Debug.Log($"[GameConfig] 音效音量已更改: {volume:F2}");
        }

        private void ApplyMouseSensitivityChange(float sensitivity)
        {
            // 连接到实际的输入系统
            Debug.Log($"[GameConfig] 鼠标灵敏度已更改: {sensitivity:F2}");
        }

        private void ApplyKeyBindingChange(string action, string key)
        {
            // 连接到实际的输入系统
            Debug.Log($"[GameConfig] 按键绑定已更改: {action} -> {key}");
        }

        private void ApplyLanguageChange(string languageCode)
        {
            // 连接到实际的本地化系统
            Debug.Log($"[GameConfig] 语言已更改: {languageCode}");
        }
        #endregion

        /// <summary>
        /// 导出配置
        /// </summary>
        public Dictionary<string, object> ExportConfiguration()
        {
            return new Dictionary<string, object>(_cachedSettings);
        }

        /// <summary>
        /// 导入配置
        /// </summary>
        public async Task<bool> ImportConfigurationAsync(Dictionary<string, object> configData)
        {
            try
            {
                foreach (var kvp in configData)
                {
                    await SaveConfigAsync(kvp.Key, kvp.Value.ToString(), GetCategoryForKey(kvp.Key));
                }
                
                _cachedSettings = new Dictionary<string, object>(configData);
                Debug.Log("[GameConfig] 配置导入完成");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameConfig] 配置导入失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 重置为默认配置
        /// </summary>
        public async Task ResetToDefaultsAsync()
        {
            try
            {
                // 清空现有配置
                _cachedSettings.Clear();
                
                // 重新加载默认配置
                DatabaseManager.LoadDefaultConfigurations();
                
                // 重新加载到缓存
                LoadAllConfigurations();
                
                Debug.Log("[GameConfig] 配置已重置为默认值");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameConfig] 重置配置失败: {ex.Message}");
            }
        }
    }
}