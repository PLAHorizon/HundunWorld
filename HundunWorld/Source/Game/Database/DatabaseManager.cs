using FlaxEngine;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using static Horizon.Game.Core.Database.LiteDataContext;

namespace Horizon.Game.Core.Database
{
    /// <summary>
    /// 数据库管理器 - 统一管理游戏本地数据存储
    /// 提供高级数据操作接口，简化游戏中的数据访问
    /// </summary>
    public static class DatabaseManager
    {
        #region 事件定义
        /// <summary>
        /// 数据库初始化完成事件
        /// </summary>
        public static event Action<bool> OnDatabaseInitialized;

        /// <summary>
        /// 角色数据更新事件
        /// </summary>
        public static event Action<ulong> OnCharacterDataUpdated;

        /// <summary>
        /// 配置更新事件
        /// </summary>
        public static event Action<string, string> OnConfigUpdated;
        #endregion

        #region 属性
        /// <summary>
        /// 数据库是否已准备就绪
        /// </summary>
        public static bool IsReady => LiteDataContext.IsInitialized;

        /// <summary>
        /// 当前活跃角色ID
        /// </summary>
        public static ulong CurrentCharacterId { get; private set; }
        #endregion

        #region 初始化管理
        /// <summary>
        /// 异步初始化数据库管理器
        /// </summary>
        public static async Task<bool> InitializeAsync()
        {
            try
            {
                Debug.Log("[DatabaseManager] 开始初始化数据库管理器...");

                // 在后台线程初始化数据库
                bool success = await Task.Run(() => LiteDataContext.Initialize());

                if (success)
                {
                    Debug.Log("[DatabaseManager] 数据库初始化成功");

                    // 执行启动时的数据维护
                    await PerformStartupMaintenance();

                    // 加载默认配置
                    LoadDefaultConfigurations();

                    Debug.Log("[DatabaseManager] 数据库管理器初始化完成");
                }
                else
                {
                    Debug.LogError("[DatabaseManager] 数据库初始化失败");
                }

                OnDatabaseInitialized?.Invoke(success);
                return success;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DatabaseManager] 初始化过程中发生异常: {ex.Message}");
                OnDatabaseInitialized?.Invoke(false);
                return false;
            }
        }

        /// <summary>
        /// 执行启动时的数据维护
        /// </summary>
        private static async Task PerformStartupMaintenance()
        {
            await Task.Run(() =>
            {
                try
                {
                    // 清理过期缓存
                    LiteDataContext.CleanExpiredCache();

                    // 记录启动统计
                    RecordSystemStatistic("AppStartup", 1);

                    Debug.Log("[DatabaseManager] 启动维护完成");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[DatabaseManager] 启动维护失败: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 加载默认配置
        /// </summary>
        public static void LoadDefaultConfigurations()
        {
            try
            {
                // 设置默认图形配置
                SetDefaultConfig("Resolution", "1920x1080", "Graphics");
                SetDefaultConfig("Quality", "Medium", "Graphics");
                SetDefaultConfig("VSync", "true", "Graphics");
                SetDefaultConfig("Fullscreen", "false", "Graphics");

                // 设置默认音频配置
                SetDefaultConfig("MasterVolume", "1.0", "Audio");
                SetDefaultConfig("MusicVolume", "0.8", "Audio");
                SetDefaultConfig("SFXVolume", "0.9", "Audio");
                SetDefaultConfig("VoiceVolume", "1.0", "Audio");

                // 设置默认游戏配置
                SetDefaultConfig("Language", "zh-CN", "Game");
                SetDefaultConfig("AutoSave", "true", "Game");
                SetDefaultConfig("ShowTutorial", "true", "Game");

                Debug.Log("[DatabaseManager] 默认配置加载完成");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DatabaseManager] 加载默认配置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置默认配置（仅在配置不存在时设置）
        /// </summary>
        private static void SetDefaultConfig(string key, string defaultValue, string category)
        {
            string currentValue = LiteDataContext.GetGameConfig(key, category);
            if (string.IsNullOrEmpty(currentValue))
            {
                LiteDataContext.SaveGameConfig(key, defaultValue, category);
            }
        }
        #endregion

        #region 角色数据管理
        /// <summary>
        /// 设置当前活跃角色
        /// </summary>
        public static bool SetCurrentCharacter(ulong characterId)
        {
            if (characterId <= 0)
            {
                CurrentCharacterId = 0;
                return true;
            }

            var character = LiteDataContext.GetCharacterData(characterId);
            if (character != null)
            {
                CurrentCharacterId = characterId;

                // 更新最后登录时间
                character.LastLoginTime = DateTime.Now;
                LiteDataContext.SaveCharacterData(character);

                Debug.Log($"[DatabaseManager] 设置当前角色: {character.CharacterName}");
                return true;
            }

            Debug.LogWarning($"[DatabaseManager] 角色不存在: {characterId}");
            return false;
        }

        /// <summary>
        /// 获取当前角色数据
        /// </summary>
        public static LiteDataContext.CharacterLocalData GetCurrentCharacter()
        {
            if (CurrentCharacterId <= 0)
                return null;

            return LiteDataContext.GetCharacterData(CurrentCharacterId);
        }

        /// <summary>
        /// 更新当前角色属性
        /// </summary>
        public static bool UpdateCharacterAttribute(string attributeName, object value)
        {
            var character = GetCurrentCharacter();
            if (character == null)
            {
                Debug.LogWarning("[DatabaseManager] 没有当前活跃角色");
                return false;
            }

            character.Attributes[attributeName] = value;
            character.IsDirty = true;

            bool success = LiteDataContext.SaveCharacterData(character);
            if (success)
            {
                OnCharacterDataUpdated?.Invoke(CurrentCharacterId);
            }

            return success;
        }

        /// <summary>
        /// 更新当前角色装备
        /// </summary>
        public static bool UpdateCharacterEquipment(string slot, string itemName)
        {
            var character = GetCurrentCharacter();
            if (character == null)
            {
                Debug.LogWarning("[DatabaseManager] 没有当前活跃角色");
                return false;
            }

            character.Equipment[slot] = itemName;
            character.IsDirty = true;

            bool success = LiteDataContext.SaveCharacterData(character);
            if (success)
            {
                OnCharacterDataUpdated?.Invoke(CurrentCharacterId);
            }

            return success;
        }

        /// <summary>
        /// 添加角色技能
        /// </summary>
        public static bool AddCharacterSkill(string skillName)
        {
            var character = GetCurrentCharacter();
            if (character == null)
            {
                Debug.LogWarning("[DatabaseManager] 没有当前活跃角色");
                return false;
            }

            if (!character.Skills.Contains(skillName))
            {
                character.Skills.Add(skillName);
                character.IsDirty = true;

                bool success = LiteDataContext.SaveCharacterData(character);
                if (success)
                {
                    OnCharacterDataUpdated?.Invoke(CurrentCharacterId);
                    RecordCharacterStatistic("SkillLearned", 1);
                }

                return success;
            }

            return true; // 技能已存在
        }

        /// <summary>
        /// 获取角色列表（按最后登录时间排序）
        /// </summary>
        public static List<LiteDataContext.CharacterLocalData> GetCharacterList()
        {
            return LiteDataContext.GetAllCharacterData();
        }
        #endregion

        #region 配置管理
        /// <summary>
        /// 获取图形配置
        /// </summary>
        public static Dictionary<string, string> GetGraphicsSettings()
        {
            return LiteDataContext.GetGameConfigsByCategory("Graphics");
        }

        /// <summary>
        /// 获取音频配置
        /// </summary>
        public static Dictionary<string, string> GetAudioSettings()
        {
            return LiteDataContext.GetGameConfigsByCategory("Audio");
        }

        /// <summary>
        /// 获取游戏配置
        /// </summary>
        public static Dictionary<string, string> GetGameSettings()
        {
            return LiteDataContext.GetGameConfigsByCategory("Game");
        }

        /// <summary>
        /// 保存图形设置
        /// </summary>
        public static bool SaveGraphicsSettings(Dictionary<string, string> settings)
        {
            return SaveCategorySettings(settings, "Graphics");
        }

        /// <summary>
        /// 保存音频设置
        /// </summary>
        public static bool SaveAudioSettings(Dictionary<string, string> settings)
        {
            return SaveCategorySettings(settings, "Audio");
        }

        /// <summary>
        /// 保存游戏设置
        /// </summary>
        public static bool SaveGameSettings(Dictionary<string, string> settings)
        {
            return SaveCategorySettings(settings, "Game");
        }

        /// <summary>
        /// 保存分类设置
        /// </summary>
        private static bool SaveCategorySettings(Dictionary<string, string> settings, string category)
        {
            try
            {
                foreach (var setting in settings)
                {
                    bool success = LiteDataContext.SaveGameConfig(setting.Key, setting.Value, category);
                    if (success)
                    {
                        OnConfigUpdated?.Invoke(setting.Key, setting.Value);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DatabaseManager] 保存{category}设置失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取单个配置值
        /// </summary>
        public static T GetConfig<T>(string key, string category, T defaultValue = default(T))
        {
            try
            {
                string value = LiteDataContext.GetGameConfig(key, category, defaultValue?.ToString());
                if (string.IsNullOrEmpty(value))
                    return defaultValue;

                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DatabaseManager] 获取配置失败 {key}: {ex.Message}");
                return defaultValue;
            }
        }

        /// <summary>
        /// 设置单个配置值
        /// </summary>
        public static bool SetConfig<T>(string key, T value, string category)
        {
            try
            {
                bool success = LiteDataContext.SaveGameConfig(key, value?.ToString(), category);
                if (success)
                {
                    OnConfigUpdated?.Invoke(key, value?.ToString());
                }
                return success;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DatabaseManager] 设置配置失败 {key}: {ex.Message}");
                return false;
            }
        }
        #endregion

        #region 缓存管理
        /// <summary>
        /// 缓存服务器数据
        /// </summary>
        public static bool CacheServerData(string key, string data, TimeSpan? expiration = null)
        {
            return LiteDataContext.SetCache($"server_{key}", data, expiration ?? TimeSpan.FromMinutes(10));
        }

        /// <summary>
        /// 获取服务器缓存数据
        /// </summary>
        public static string GetServerCache(string key)
        {
            return LiteDataContext.GetCache($"server_{key}");
        }

        /// <summary>
        /// 缓存物品数据
        /// </summary>
        public static bool CacheItemData(string itemId, string data)
        {
            return LiteDataContext.SetCache($"item_{itemId}", data, TimeSpan.FromHours(1));
        }

        /// <summary>
        /// 获取物品缓存数据
        /// </summary>
        public static string GetItemCache(string itemId)
        {
            return LiteDataContext.GetCache($"item_{itemId}");
        }

        /// <summary>
        /// 缓存任务数据
        /// </summary>
        public static bool CacheQuestData(string questId, string data)
        {
            return LiteDataContext.SetCache($"quest_{questId}", data, TimeSpan.FromDays(1));
        }

        /// <summary>
        /// 获取任务缓存数据
        /// </summary>
        public static string GetQuestCache(string questId)
        {
            return LiteDataContext.GetCache($"quest_{questId}");
        }
        #endregion

        #region 统计管理
        /// <summary>
        /// 记录当前角色统计
        /// </summary>
        public static bool RecordCharacterStatistic(string statType, long value)
        {
            if (CurrentCharacterId <= 0)
            {
                Debug.LogWarning("[DatabaseManager] 没有当前活跃角色，无法记录统计");
                return false;
            }

            return LiteDataContext.RecordStatistic(CurrentCharacterId, statType, value);
        }

        /// <summary>
        /// 记录系统统计
        /// </summary>
        public static bool RecordSystemStatistic(string statType, long value)
        {
            return LiteDataContext.RecordStatistic(0, statType, value);
        }


        /// <summary>
        /// 获取当前角色统计摘要
        /// </summary>
        public static Dictionary<string, long> GetCharacterStatisticsSummary()
        {
            if (CurrentCharacterId <= 0)
                return new Dictionary<string, long>();

            var stats = LiteDataContext.GetStatistics(CurrentCharacterId);
            return stats.GroupBy(s => s.StatType)
                       .ToDictionary(g => g.Key, g => g.Sum(s => s.Value));
        }

        /// <summary>
        /// 获取今日角色统计
        /// </summary>
        public static Dictionary<string, long> GetTodayCharacterStatistics()
        {
            if (CurrentCharacterId <= 0)
                return new Dictionary<string, long>();

            var stats = LiteDataContext.GetStatistics(CurrentCharacterId, null, DateTime.Today);
            return stats.GroupBy(s => s.StatType)
                       .ToDictionary(g => g.Key, g => g.Sum(s => s.Value));
        }
        #endregion

        #region 数据同步
        /// <summary>
        /// 标记角色数据需要同步
        /// </summary>
        public static bool MarkCharacterForSync(ulong characterId = 0)
        {
            characterId = characterId <= 0 ? CurrentCharacterId : characterId;
            if (characterId <= 0)
                return false;

            var character = LiteDataContext.GetCharacterData(characterId);
            if (character != null)
            {
                character.IsDirty = true;
                return LiteDataContext.SaveCharacterData(character);
            }

            return false;
        }

        /// <summary>
        /// 获取需要同步的角色列表
        /// </summary>
        public static List<LiteDataContext.CharacterLocalData> GetCharactersNeedingSync()
        {
            return LiteDataContext.GetAllCharacterData()
                                 .Where(c => c.IsDirty)
                                 .ToList();
        }

        /// <summary>
        /// 标记角色已同步
        /// </summary>
        public static bool MarkCharacterSynced(ulong characterId)
        {
            var character = LiteDataContext.GetCharacterData(characterId);
            if (character != null)
            {
                character.IsDirty = false;
                character.LastSyncTime = DateTime.Now;
                return LiteDataContext.SaveCharacterData(character);
            }

            return false;
        }
        #endregion

        #region 数据库维护
        /// <summary>
        /// 执行数据库维护
        /// </summary>
        public static async Task PerformMaintenanceAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    Debug.Log("[DatabaseManager] 开始数据库维护...");

                    // 清理过期缓存
                    LiteDataContext.CleanExpiredCache();

                    // 压缩数据库
                    LiteDataContext.CompactDatabase();

                    // 记录维护统计
                    RecordSystemStatistic("DatabaseMaintenance", 1);

                    Debug.Log("[DatabaseManager] 数据库维护完成");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[DatabaseManager] 数据库维护失败: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 获取数据库状态信息
        /// </summary>
        public static Dictionary<string, object> GetDatabaseStatus()
        {
            var info = LiteDataContext.GetDatabaseInfo();

            // 添加管理器特定信息
            info["CurrentCharacter"] = CurrentCharacterId;
            info["ManagerReady"] = IsReady;

            return info;
        }
        #endregion

        #region 清理和关闭
        /// <summary>
        /// 关闭数据库管理器
        /// </summary>
        public static void Shutdown()
        {
            try
            {
                Debug.Log("[DatabaseManager] 正在关闭数据库管理器...");

                // 记录关闭统计
                RecordSystemStatistic("AppShutdown", 1);

                // 清空当前角色
                CurrentCharacterId = 0;

                // 关闭数据库连接
                LiteDataContext.Close();

                Debug.Log("[DatabaseManager] 数据库管理器已关闭");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DatabaseManager] 关闭数据库管理器时发生错误: {ex.Message}");
            }
        }
        /// <summary>
        /// 清除所有缓存数据（用于解决序列化版本兼容问题）
        /// </summary>
        public static void ClearAllCacheData()
        {
            try
            {
                Debug.Log("[DatabaseManager] 开始清除所有缓存数据...");

                // 清除缓存数据库中的所有数据
                LiteDataContext.DeletedAll<LiteDataContext.CacheData>(LiteDatabaseKind.Cache);
                
                // 清除过期缓存
                LiteDataContext.CleanExpiredCache();

                Debug.Log("[DatabaseManager] 所有缓存数据已清除");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DatabaseManager] 清除缓存数据时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 完全重置数据库（清除所有数据）
        /// </summary>
        public static void ResetAllData()
        {
            try
            {
                Debug.Log("[DatabaseManager] 开始重置所有数据...");

                // 清除所有角色数据
                LiteDataContext.DeletedAll<LiteDataContext.CharacterLocalData>(LiteDatabaseKind.Game);

                // 清除所有配置
                LiteDataContext.DeletedAll<LiteDataContext.GameConfig>(LiteDatabaseKind.Config);

                // 清除所有缓存
                LiteDataContext.DeletedAll<LiteDataContext.CacheData>(LiteDatabaseKind.Cache);

                // 清除当前角色
                CurrentCharacterId = 0;

                Debug.Log("[DatabaseManager] 所有数据已重置");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DatabaseManager] 重置数据时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置登录通行证信息
        /// </summary>
        /// <param name="passportId"></param>
        /// <param name="userId"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public static async Task SetPassport(string passportId, ulong userId, string token)
        {
            DeletedAll<PassportInfo>(LiteDatabaseKind.Game);
            var character = Add(
                liteDatabaseKind: LiteDatabaseKind.Game,
                new PassportInfo
                {
                    IsCurrentPassport = true,
                    PassportId = passportId,
                    Token = token,
                    Password = "*********"
                });


        }
        /// <summary>
        /// 设置登录通行证信息
        /// </summary>
        /// <param name="passport"></param>
        /// <returns></returns>
        public static async Task SetPassport(PassportInfo passport)
        {
            DeletedAll<PassportInfo>(LiteDatabaseKind.Game);
            var character = Add(
                liteDatabaseKind: LiteDatabaseKind.Game,
                passport);


        }
        /// <summary>
        ///获取最近已登录的通行证
        /// </summary>
        /// <returns></returns>
        public static async Task<PassportInfo> GetPassport()
        {

            return await Task.FromResult(FirstOrDefault<PassportInfo>(liteDatabaseKind: LiteDatabaseKind.Game));
        }
        #endregion
    }
}