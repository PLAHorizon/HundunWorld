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
            EnsureMouseSensitivityLoaded();
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

        // ===== 画面设置的其余项：只负责持久化 =====
        // 现有 Apply*Change 全是 log-only；这些值先落库，等引擎侧字段确定后再接应用，
        // 至少"改完重开还是这个值"，而不是每次回到设计稿默认。

        public async Task<bool> SetFrameRateAsync(int fps)
        {
            fps = Mathf.Clamp(fps, 20, 240);
            bool ok = await SaveConfigAsync("FrameRate", fps.ToString(), CONFIG_CATEGORY_GRAPHICS);
            if (ok) _cachedSettings["FrameRate"] = fps;
            return ok;
        }

        public int GetFrameRate() => GetConfigValue<int>("FrameRate", 60);

        public async Task<bool> SetViewDistanceAsync(float percent)
        {
            percent = Mathf.Clamp(percent, 0f, 100f);
            bool ok = await SaveConfigAsync("ViewDistance", percent.ToString("0.##"), CONFIG_CATEGORY_GRAPHICS);
            if (ok)
            {
                _cachedSettings["ViewDistance"] = percent;
                ViewDistancePercent = percent;   // 相机系统每帧对比这个静态值来改远裁剪面
            }
            return ok;
        }

        /// <summary>
        /// 视距（0~100 百分比）。引擎没有"视距"这一项，实际落到主相机的远裁剪面：
        /// 100% → 150m，0% → 20m，中间线性。相机系统读这个静态值，设置页保存后立刻生效。
        /// </summary>
        public static float ViewDistancePercent { get; private set; } = 75f;

        /// <summary>把已持久化的视距载入静态值（相机系统初始化时调用一次）。</summary>
        public void EnsureViewDistanceLoaded()
        {
            ViewDistancePercent = Mathf.Clamp(GetViewDistance(), 0f, 100f);
        }

        /// <summary>视距百分比 → 相机远裁剪面（厘米）。</summary>
        public static float ViewDistanceToFarPlane(float percent)
        {
            float t = Mathf.Clamp(percent, 0f, 100f) / 100f;
            return Mathf.Lerp(2000f, 15000f, t);
        }

        public float GetViewDistance() => GetConfigValue<float>("ViewDistance", 75f);

        public async Task<bool> SetEffectQualityAsync(int index)
        {
            index = Mathf.Clamp(index, 0, 3);
            bool ok = await SaveConfigAsync("EffectQuality", index.ToString(), CONFIG_CATEGORY_GRAPHICS);
            if (ok)
            {
                _cachedSettings["EffectQuality"] = index;
                var effectQuality = ToQuality(index);
                try
                {
                    FlaxEngine.Graphics.SSRQuality = effectQuality;
                    FlaxEngine.Graphics.VolumetricFogQuality = effectQuality;
                }
                catch (Exception ex) { Debug.LogWarning($"[GameConfig] 特效质量下发失败: {ex.Message}"); }
            }
            return ok;
        }

        public int GetEffectQuality() => Mathf.Clamp(GetConfigValue<int>("EffectQuality", 2), 0, 3);

        public async Task<bool> SetShadowQualityAsync(int index)
        {
            index = Mathf.Clamp(index, 0, 4);
            bool ok = await SaveConfigAsync("ShadowQuality", index.ToString(), CONFIG_CATEGORY_GRAPHICS);
            if (ok)
            {
                _cachedSettings["ShadowQuality"] = index;
                try { FlaxEngine.Graphics.ShadowsQuality = ToQuality(index); }
                catch (Exception ex) { Debug.LogWarning($"[GameConfig] 阴影质量下发失败: {ex.Message}"); }
            }
            return ok;
        }

        public int GetShadowQuality() => Mathf.Clamp(GetConfigValue<int>("ShadowQuality", 2), 0, 4);

        public async Task<bool> SetAntiAliasingAsync(bool enabled)
        {
            bool ok = await SaveConfigAsync("AntiAliasing", enabled.ToString(), CONFIG_CATEGORY_GRAPHICS);
            if (ok)
            {
                _cachedSettings["AntiAliasing"] = enabled;
                try { FlaxEngine.Graphics.AAQuality = enabled ? FlaxEngine.Quality.High : FlaxEngine.Quality.Low; }
                catch (Exception ex) { Debug.LogWarning($"[GameConfig] 抗锯齿下发失败: {ex.Message}"); }
            }
            return ok;
        }

        public bool GetAntiAliasing() => GetConfigValue<bool>("AntiAliasing", true);

        public async Task<bool> SetAutoHideUiAsync(bool enabled)
        {
            bool ok = await SaveConfigAsync("AutoHideUI", enabled.ToString(), CONFIG_CATEGORY_GRAPHICS);
            if (ok) _cachedSettings["AutoHideUI"] = enabled;
            return ok;
        }

        public bool GetAutoHideUi() => GetConfigValue<bool>("AutoHideUI", true);

        public async Task<bool> SetPowerSaveAsync(bool enabled)
        {
            bool ok = await SaveConfigAsync("PowerSave", enabled.ToString(), CONFIG_CATEGORY_GRAPHICS);
            if (ok) _cachedSettings["PowerSave"] = enabled;
            return ok;
        }

        public bool GetPowerSave() => GetConfigValue<bool>("PowerSave", false);
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

        #region 路牌设置（仅保存偏好；路牌系统未接入）
        public bool GetRoadSignSetting(string key) => GetConfigValue<bool>("RoadSign_" + key, true);

        public async Task<bool> SetRoadSignSettingAsync(string key, bool enabled)
        {
            return await SaveConfigAsync("RoadSign_" + key, enabled.ToString(), CONFIG_CATEGORY_GAMEPLAY);
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
            if (key == "MouseSensitivity") return CONFIG_CATEGORY_CONTROLS;
            if (key.Contains("Volume")) return CONFIG_CATEGORY_AUDIO;

            // 画面键必须显式列出：写方保存时用的是 Graphics，读方若推断成 Gameplay，
            // LiteDataContext 会按类别查 → 永远读不到、静默回落默认值。
            switch (key)
            {
                case "Resolution":
                case "Quality":
                case "VSync":
                case "Fullscreen":
                case "FrameRate":
                case "ViewDistance":
                case "EffectQuality":
                case "ShadowQuality":
                case "AntiAliasing":
                case "AutoHideUI":
                case "PowerSave":
                    return CONFIG_CATEGORY_GRAPHICS;
            }

            if (key.Contains("Quality")) return CONFIG_CATEGORY_GRAPHICS;
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

        private void ApplyVSyncChange(bool enabled)
        {
            try
            {
                FlaxEngine.Graphics.UseVSync = enabled;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GameConfig] 垂直同步应用失败: {ex.Message}");
            }
            Debug.Log($"[GameConfig] 垂直同步已{(enabled ? "启用" : "禁用")}");
        }

        /// <summary>
        /// 画面预设下发：一套预设同时改抗锯齿/阴影/特效三档。
        /// 注意——实测（PIE）这几项质量字段写进去后读回不变，疑似只在渲染器初始化时消费；
        /// 同一组里只有 <c>Graphics.UseVSync</c> 能立刻读回新值。所以这里仍照写（打包后可能按初始化值生效），
        /// 但不要在 UI 上承诺"切了立刻变"。
        /// </summary>
        private void ApplyQualityChange(string quality)
        {
            var q = QualityFromName(quality);
            ApplyQualityFields(q, q, q);
            Debug.Log($"[GameConfig] 图形质量已更改: {quality}（{q}）");
        }

        /// <summary>把已持久化的画面设置整体下发到引擎；打开设置页时调用一次即可生效。</summary>
        public void ApplyGraphicsSettings()
        {
            try
            {
                FlaxEngine.Graphics.UseVSync = GetVSync();
                FlaxEngine.Graphics.AAQuality = GetAntiAliasing() ? FlaxEngine.Quality.High : FlaxEngine.Quality.Low;
                FlaxEngine.Graphics.ShadowsQuality = ToQuality(GetShadowQuality());
                var effect = ToQuality(GetEffectQuality());
                FlaxEngine.Graphics.SSRQuality = effect;
                FlaxEngine.Graphics.VolumetricFogQuality = effect;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GameConfig] 画面设置下发失败: {ex.Message}");
            }
        }

        private static void ApplyQualityFields(FlaxEngine.Quality aa, FlaxEngine.Quality shadows, FlaxEngine.Quality effects)
        {
            try
            {
                FlaxEngine.Graphics.AAQuality = aa;
                FlaxEngine.Graphics.ShadowsQuality = shadows;
                FlaxEngine.Graphics.SSRQuality = effects;
                FlaxEngine.Graphics.VolumetricFogQuality = effects;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GameConfig] 画面质量字段下发失败: {ex.Message}");
            }
        }

        /// <summary>0..3 → Low/Medium/High/Ultra（阴影档位 0..4 也走这里，超出按 Ultra 处理）。</summary>
        private static FlaxEngine.Quality ToQuality(int index) => index switch
        {
            <= 0 => FlaxEngine.Quality.Low,
            1 => FlaxEngine.Quality.Medium,
            2 => FlaxEngine.Quality.High,
            _ => FlaxEngine.Quality.Ultra,
        };

        private static FlaxEngine.Quality QualityFromName(string name) => name switch
        {
            "流畅" => FlaxEngine.Quality.Low,
            "标准" => FlaxEngine.Quality.Medium,
            "高清" => FlaxEngine.Quality.High,
            "极致" => FlaxEngine.Quality.Ultra,
            _ => FlaxEngine.Quality.Medium,
        };

        private void ApplyMasterVolumeChange(float volume)
        {
            try { HundunWorld.Game.Audio.GameAudioManager.Instance.MasterVolume = Mathf.Clamp(volume, 0f, 1f); }
            catch (Exception ex) { Debug.LogWarning($"[GameConfig] 主音量下发失败: {ex.Message}"); }
            Debug.Log($"[GameConfig] 主音量已更改: {volume:F2}");
        }

        private void ApplyMusicVolumeChange(float volume)
        {
            try
            {
                var v = Mathf.Clamp(volume, 0f, 1f);
                HundunWorld.Game.Audio.GameAudioManager.Instance.MusicVolume = v;
                // 背景音乐走独立的 MusicManager（它不看 GameAudioManager），两边都下发才不会"调了没声"。
                HundunWorld.Game.Audio.MusicManager.Instance.SetGlobalVolume(v);
            }
            catch (Exception ex) { Debug.LogWarning($"[GameConfig] 音乐音量下发失败: {ex.Message}"); }
            Debug.Log($"[GameConfig] 音乐音量已更改: {volume:F2}");
        }

        private void ApplySFXVolumeChange(float volume)
        {
            try { HundunWorld.Game.Audio.GameAudioManager.Instance.SfxVolume = Mathf.Clamp(volume, 0f, 1f); }
            catch (Exception ex) { Debug.LogWarning($"[GameConfig] 音效音量下发失败: {ex.Message}"); }
            Debug.Log($"[GameConfig] 音效音量已更改: {volume:F2}");
        }

        /// <summary>
        /// 鼠标灵敏度在引擎侧的唯一消费点（ThirdPersonCamera 每帧读这个静态值，不再每帧查库）。
        /// 由 <see cref="ApplyMouseSensitivityChange"/> 与 <see cref="EnsureMouseSensitivityLoaded"/> 维护。
        /// </summary>
        public static float MouseSensitivityFactor { get; private set; } = 1f;

        /// <summary>把已持久化的灵敏度载入静态因子（相机系统初始化时调用一次）。</summary>
        public void EnsureMouseSensitivityLoaded()
        {
            MouseSensitivityFactor = Mathf.Clamp(GetMouseSensitivity(), 0.1f, 3f);
        }

        private void ApplyMouseSensitivityChange(float sensitivity)
        {
            MouseSensitivityFactor = Mathf.Clamp(sensitivity, 0.1f, 3f);
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