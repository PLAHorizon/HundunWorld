using FlaxEngine;
using Horizon.Game.Core.Database;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Horizon.Game.Core.Database
{
    /// <summary>
    /// LiteDataContext 使用示例
    /// 展示如何使用本地数据库存储游戏配置和角色数据
    /// </summary>
    public static class LiteDataContextExample
    {
        /// <summary>
        /// 初始化数据库示例
        /// </summary>
        public static void InitializeExample()
        {
            Debug.Log("=== LiteDataContext 初始化示例 ===");
            
            // 初始化数据库
            bool success = LiteDataContext.Initialize();
            Debug.Log($"数据库初始化结果: {success}");
            
            // 获取数据库信息
            var dbInfo = LiteDataContext.GetDatabaseInfo();
            foreach (var info in dbInfo)
            {
                Debug.Log($"{info.Key}: {info.Value}");
            }
        }

        /// <summary>
        /// 游戏配置管理示例
        /// </summary>
        public static void GameConfigExample()
        {
            Debug.Log("=== 游戏配置管理示例 ===");
            
            // 保存图形设置
            LiteDataContext.SaveGameConfig("Resolution", "1920x1080", "Graphics");
            LiteDataContext.SaveGameConfig("Quality", "High", "Graphics");
            LiteDataContext.SaveGameConfig("VSync", "true", "Graphics");
            
            // 保存音频设置
            LiteDataContext.SaveGameConfig("MasterVolume", "0.8", "Audio");
            LiteDataContext.SaveGameConfig("MusicVolume", "0.6", "Audio");
            LiteDataContext.SaveGameConfig("SFXVolume", "0.7", "Audio");
            
            // 保存控制设置
            LiteDataContext.SaveGameConfig("MouseSensitivity", "1.5", "Controls");
            LiteDataContext.SaveGameConfig("InvertY", "false", "Controls");
            
            // 读取配置
            string resolution = LiteDataContext.GetGameConfig("Resolution", "Graphics", "1280x720");
            string masterVolume = LiteDataContext.GetGameConfig("MasterVolume", "Audio", "1.0");
            
            Debug.Log($"当前分辨率: {resolution}");
            Debug.Log($"主音量: {masterVolume}");
            
            // 获取分类配置
            var graphicsSettings = LiteDataContext.GetGameConfigsByCategory("Graphics");
            Debug.Log($"图形设置数量: {graphicsSettings.Count}");
            foreach (var setting in graphicsSettings)
            {
                Debug.Log($"  {setting.Key}: {setting.Value}");
            }
        }

        /// <summary>
        /// 角色数据管理示例
        /// </summary>
        public static void CharacterDataExample()
        {
            Debug.Log("=== 角色数据管理示例 ===");
            
            // 创建角色数据
            var character1 = new LiteDataContext.CharacterLocalData
            {
                CharacterId = 001,
                CharacterName = "剑侠客",
                Level = 25,
                Class = "剑客",
                LastLoginTime = DateTime.Now
            };
            
            // 添加属性
            character1.Attributes["Strength"] = 85;
            character1.Attributes["Agility"] = 92;
            character1.Attributes["Intelligence"] = 78;
            character1.Attributes["Health"] = 1250;
            character1.Attributes["Mana"] = 800;
            
            // 添加装备
            character1.Equipment["Weapon"] = "青锋剑";
            character1.Equipment["Armor"] = "云纹战甲";
            character1.Equipment["Accessory"] = "玉佩";
            
            // 添加技能
            character1.Skills.Add("基础剑法");
            character1.Skills.Add("疾风步");
            character1.Skills.Add("剑气纵横");
            
            // 保存角色数据
            bool saved = LiteDataContext.SaveCharacterData(character1);
            Debug.Log($"角色数据保存结果: {saved}");
            
            // 创建第二个角色
            var character2 = new LiteDataContext.CharacterLocalData
            {
                CharacterId = 002,
                CharacterName = "法师",
                Level = 30,
                Class = "法师",
                LastLoginTime = DateTime.Now.AddHours(-2)
            };
            
            character2.Attributes["Intelligence"] = 95;
            character2.Attributes["Wisdom"] = 88;
            character2.Attributes["Health"] = 900;
            character2.Attributes["Mana"] = 1500;
            
            character2.Equipment["Weapon"] = "法杖";
            character2.Equipment["Robe"] = "星辰法袍";
            
            character2.Skills.Add("火球术");
            character2.Skills.Add("冰锥术");
            character2.Skills.Add("传送术");
            
            LiteDataContext.SaveCharacterData(character2);
            
            // 读取角色数据
            var loadedCharacter = LiteDataContext.GetCharacterData(001);
            if (loadedCharacter != null)
            {
                Debug.Log($"加载角色: {loadedCharacter.CharacterName}, 等级: {loadedCharacter.Level}");
                Debug.Log($"力量: {loadedCharacter.Attributes.GetValueOrDefault("Strength", 0)}");
                Debug.Log($"武器: {loadedCharacter.Equipment.GetValueOrDefault("Weapon", "无")}");
                Debug.Log($"技能数量: {loadedCharacter.Skills.Count}");
            }
            
            // 获取所有角色
            var allCharacters = LiteDataContext.GetAllCharacterData();
            Debug.Log($"总角色数量: {allCharacters.Count}");
            foreach (var character in allCharacters)
            {
                Debug.Log($"  {character.CharacterName} (等级 {character.Level}) - 最后登录: {character.LastLoginTime:yyyy-MM-dd HH:mm}");
            }
        }

        /// <summary>
        /// 用户偏好设置示例
        /// </summary>
        public static void UserPreferencesExample()
        {
            Debug.Log("=== 用户偏好设置示例 ===");
            
            var preferences = new LiteDataContext.UserPreferences
            {
                UserId = "user_001"
            };
            
            // 图形偏好
            preferences.GraphicsSettings["Brightness"] = 0.8;
            preferences.GraphicsSettings["Contrast"] = 1.1;
            preferences.GraphicsSettings["Gamma"] = 1.0;
            preferences.GraphicsSettings["AntiAliasing"] = "MSAA x4";
            
            // 音频偏好
            preferences.AudioSettings["MasterVolume"] = 0.85;
            preferences.AudioSettings["BackgroundMusic"] = true;
            preferences.AudioSettings["SoundEffects"] = true;
            preferences.AudioSettings["VoiceChat"] = false;
            
            // 控制偏好
            preferences.ControlSettings["KeyBinding_Attack"] = "LeftClick";
            preferences.ControlSettings["KeyBinding_Jump"] = "Space";
            preferences.ControlSettings["KeyBinding_Inventory"] = "I";
            preferences.ControlSettings["KeyBinding_Map"] = "M";
            
            // UI偏好
            preferences.UISettings["ChatWindowSize"] = "Medium";
            preferences.UISettings["MinimapSize"] = "Large";
            preferences.UISettings["ShowDamageNumbers"] = true;
            preferences.UISettings["AutoLoot"] = true;
            
            // 保存偏好
            bool saved = LiteDataContext.SaveUserPreferences(preferences);
            Debug.Log($"用户偏好保存结果: {saved}");
            
            // 读取偏好
            var loadedPreferences = LiteDataContext.GetUserPreferences("user_001");
            if (loadedPreferences != null)
            {
                Debug.Log($"用户偏好加载成功, 创建时间: {loadedPreferences.CreatedAt:yyyy-MM-dd HH:mm}");
                Debug.Log($"亮度设置: {loadedPreferences.GraphicsSettings.GetValueOrDefault("Brightness", 1.0)}");
                Debug.Log($"主音量: {loadedPreferences.AudioSettings.GetValueOrDefault("MasterVolume", 1.0)}");
                Debug.Log($"自动拾取: {loadedPreferences.UISettings.GetValueOrDefault("AutoLoot", false)}");
            }
        }

        /// <summary>
        /// 缓存管理示例
        /// </summary>
        public static void CacheManagementExample()
        {
            Debug.Log("=== 缓存管理示例 ===");
            
            // 设置缓存数据
            string serverData = "{\"serverTime\":\"2024-01-15T10:30:00Z\",\"playerCount\":1250}";
            LiteDataContext.SetCache("server_status", serverData, TimeSpan.FromMinutes(5));
            
            string itemData = "{\"itemId\":\"sword_001\",\"name\":\"青锋剑\",\"price\":1500}";
            LiteDataContext.SetCache("item_sword_001", itemData, TimeSpan.FromHours(1));
            
            string questData = "{\"questId\":\"quest_001\",\"title\":\"寻找失落的宝藏\",\"reward\":2000}";
            LiteDataContext.SetCache("quest_001", questData, TimeSpan.FromDays(1));
            
            Debug.Log("缓存数据已设置");
            
            // 读取缓存
            string cachedServerData = LiteDataContext.GetCache("server_status");
            string cachedItemData = LiteDataContext.GetCache("item_sword_001");
            
            Debug.Log($"服务器状态缓存: {cachedServerData ?? "未找到或已过期"}");
            Debug.Log($"物品数据缓存: {cachedItemData ?? "未找到或已过期"}");
            
            // 删除特定缓存
            bool removed = LiteDataContext.RemoveCache("server_status");
            Debug.Log($"删除服务器状态缓存: {removed}");
            
            // 清理过期缓存
            LiteDataContext.CleanExpiredCache();
            Debug.Log("过期缓存清理完成");
        }

        /// <summary>
        /// 游戏统计示例
        /// </summary>
        public static void GameStatisticsExample()
        {
            Debug.Log("=== 游戏统计示例 ===");
            
            ulong characterId = 001;
            
            // 记录各种统计数据
            LiteDataContext.RecordStatistic(characterId, "KillCount", 1);
            LiteDataContext.RecordStatistic(characterId, "ExperienceGained", 150);
            LiteDataContext.RecordStatistic(characterId, "GoldEarned", 50);
            LiteDataContext.RecordStatistic(characterId, "QuestCompleted", 1);
            LiteDataContext.RecordStatistic(characterId, "DeathCount", 0);
            LiteDataContext.RecordStatistic(characterId, "PlayTime", 3600); // 秒
            
            // 模拟更多数据
            for (int i = 0; i < 10; i++)
            {
                LiteDataContext.RecordStatistic(characterId, "KillCount", 1);
                LiteDataContext.RecordStatistic(characterId, "ExperienceGained", 100 + i * 10);
                LiteDataContext.RecordStatistic(characterId, "GoldEarned", 25 + i * 5);
            }
            
            Debug.Log("统计数据记录完成");
            
            // 获取统计数据
            var allStats = LiteDataContext.GetStatistics(characterId);
            Debug.Log($"角色 {characterId} 总统计记录数: {allStats.Count}");
            
            // 获取特定类型的统计
            var killStats = LiteDataContext.GetStatistics(characterId, "KillCount");
            long totalKills = killStats.Sum(s => s.Value);
            Debug.Log($"总击杀数: {totalKills}");
            
            var expStats = LiteDataContext.GetStatistics(characterId, "ExperienceGained");
            long totalExp = expStats.Sum(s => s.Value);
            Debug.Log($"总经验获得: {totalExp}");
            
            var goldStats = LiteDataContext.GetStatistics(characterId, "GoldEarned");
            long totalGold = goldStats.Sum(s => s.Value);
            Debug.Log($"总金币获得: {totalGold}");
            
            // 获取今天的统计
            var todayStats = LiteDataContext.GetStatistics(characterId, null, DateTime.Today);
            Debug.Log($"今日统计记录数: {todayStats.Count}");
        }

        /// <summary>
        /// 数据库维护示例
        /// </summary>
        public static void DatabaseMaintenanceExample()
        {
            Debug.Log("=== 数据库维护示例 ===");
            
            // 获取数据库信息
            var dbInfo = LiteDataContext.GetDatabaseInfo();
            Debug.Log("数据库信息:");
            foreach (var info in dbInfo)
            {
                Debug.Log($"  {info.Key}: {info.Value}");
            }
            
            // 压缩数据库
            Debug.Log("开始压缩数据库...");
            LiteDataContext.CompactDatabase();
            Debug.Log("数据库压缩完成");
            
            // 再次获取信息查看变化
            var dbInfoAfter = LiteDataContext.GetDatabaseInfo();
            Debug.Log("压缩后数据库信息:");
            foreach (var info in dbInfoAfter)
            {
                Debug.Log($"  {info.Key}: {info.Value}");
            }
        }

        /// <summary>
        /// 运行所有示例
        /// </summary>
        public static void RunAllExamples()
        {
            Debug.Log("\n========== LiteDataContext 完整示例 ==========");
            
            try
            {
                InitializeExample();
                Debug.Log("");
                
                GameConfigExample();
                Debug.Log("");
                
                CharacterDataExample();
                Debug.Log("");
                
                UserPreferencesExample();
                Debug.Log("");
                
                CacheManagementExample();
                Debug.Log("");
                
                GameStatisticsExample();
                Debug.Log("");
                
                DatabaseMaintenanceExample();
                
                Debug.Log("\n========== 所有示例运行完成 ==========");
            }
            catch (Exception ex)
            {
                Debug.LogError($"运行示例时发生错误: {ex.Message}");
            }
            finally
            {
                // 关闭数据库连接
                LiteDataContext.Close();
                Debug.Log("数据库连接已关闭");
            }
        }
    }
}