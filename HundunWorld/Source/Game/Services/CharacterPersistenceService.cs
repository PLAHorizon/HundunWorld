using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlaxEngine;
using Horizon.Game.Core.Database;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Enums;
using HundunWorld.Game.UI.Authentication;

namespace HundunWorld.Game.Services
{
    /// <summary>
    /// 角色数据持久化服务
    /// 负责角色数据的本地存储和同步管理
    /// </summary>
    public class CharacterPersistenceService
    {
        private static CharacterPersistenceService _instance;
        private object _databaseManager; // 使用object类型避免静态类型问题
        private Dictionary<ulong, DateTime> _lastSaveTimes;
        private const float AUTO_SAVE_INTERVAL = 30.0f; // 30秒自动保存间隔
        private float _lastAutoSaveTime = 0f;

        public static CharacterPersistenceService Instance => _instance ??= new CharacterPersistenceService();

        private CharacterPersistenceService()
        {
            // DatabaseManager是静态类，不需要实例化
            _lastSaveTimes = new Dictionary<ulong, DateTime>();
            Debug.Log("[CharacterPersistence] 角色持久化服务已初始化");
        }

        /// <summary>
        /// 保存角色基本信息
        /// </summary>
        public async Task<bool> SaveCharacterBasicInfoAsync(CharacterInfo characterInfo)
        {
            try
            {
                var localData = new LiteDataContext.CharacterLocalData
                {
                    CharacterId = characterInfo.CharacterId,
                    CharacterName = characterInfo.CharacterName,
                    Level = characterInfo.Level,
                    Exp = (ulong)characterInfo.Experience,
                    Class = characterInfo.Profession.ToString(),
                    PassportId = AuthenticationManager.Instance?.Passport?.PassportId ?? "",
                    GameUserId = AuthenticationManager.Instance?.Passport?.UserId ?? 0,
                    LastLoginTime = DateTime.Now,
                    LastSyncTime = DateTime.Now,
                    IsDirty = false
                };

                // 保存到数据库
                bool success = await Task.Run(() => 
                {
                    LiteDataContext.SaveCharacterData(localData);
                    return true;
                });

                if (success)
                {
                    _lastSaveTimes[characterInfo.CharacterId] = DateTime.Now;
                    Debug.Log($"[CharacterPersistence] 角色基础信息已保存: {characterInfo.CharacterName}");
                }

                return success;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterPersistence] 保存角色基础信息失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 保存角色完整数据（包括属性、装备、技能等）
        /// </summary>
        public async Task<bool> SaveCharacterFullDataAsync(CharacterInfo characterInfo, Dictionary<string, object> attributes = null, 
            Dictionary<string, object> equipment = null, List<string> skills = null)
        {
            try
            {
                var localData = new LiteDataContext.CharacterLocalData
                {
                    CharacterId = characterInfo.CharacterId,
                    CharacterName = characterInfo.CharacterName,
                    Level = characterInfo.Level,
                    Exp = (ulong)characterInfo.Experience,
                    Class = characterInfo.Profession.ToString(),
                    PassportId = AuthenticationManager.Instance?.Passport?.PassportId ?? "",
                    GameUserId = AuthenticationManager.Instance?.Passport?.UserId ?? 0,
                    Attributes = attributes ?? new Dictionary<string, object>(),
                    Equipment = equipment ?? new Dictionary<string, object>(),
                    Skills = skills ?? new List<string>(),
                    LastLoginTime = DateTime.Now,
                    LastSyncTime = DateTime.Now,
                    IsDirty = true // 标记为需要同步
                };

                bool success = await Task.Run(() => 
                {
                    LiteDataContext.SaveCharacterData(localData);
                    return true;
                });

                if (success)
                {
                    _lastSaveTimes[characterInfo.CharacterId] = DateTime.Now;
                    Debug.Log($"[CharacterPersistence] 角色完整数据已保存: {characterInfo.CharacterName}");
                }

                return success;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterPersistence] 保存角色完整数据失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 加载角色数据
        /// </summary>
        public async Task<CharacterInfo> LoadCharacterAsync(ulong characterId)
        {
            try
            {
                var localData = await Task.Run(() => LiteDataContext.GetCharacterData(characterId));
                
                if (localData != null)
                {
                    var characterInfo = new CharacterInfo
                    {
                        CharacterId = localData.CharacterId,
                        CharacterName = localData.CharacterName,
                        Level = localData.Level,
                        Experience = (long)localData.Exp,
                        Profession = Enum.TryParse<Profession>(localData.Class, out var prof) ? prof : Profession.None,
                        CreatedTime = localData.LastLoginTime
                    };

                    Debug.Log($"[CharacterPersistence] 角色数据已加载: {characterInfo.CharacterName}");
                    return characterInfo;
                }

                Debug.LogWarning($"[CharacterPersistence] 未找到角色数据: {characterId}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterPersistence] 加载角色数据失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 更新角色属性
        /// </summary>
        public async Task<bool> UpdateCharacterAttributeAsync(ulong characterId, string attributeName, object value)
        {
            try
            {
                var characterData = await Task.Run(() => LiteDataContext.GetCharacterData(characterId));
                
                if (characterData != null)
                {
                    if (characterData.Attributes == null)
                        characterData.Attributes = new Dictionary<string, object>();

                    characterData.Attributes[attributeName] = value;
                    characterData.IsDirty = true;
                    characterData.LastSyncTime = DateTime.Now;

                    bool success = await Task.Run(() => 
                    {
                        LiteDataContext.SaveCharacterData(characterData);
                        return true;
                    });

                    if (success)
                    {
                        _lastSaveTimes[characterId] = DateTime.Now;
                        Debug.Log($"[CharacterPersistence] 角色属性已更新: {characterId}.{attributeName}");
                    }

                    return success;
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterPersistence] 更新角色属性失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取需要同步的角色列表
        /// </summary>
        public async Task<List<ulong>> GetCharactersNeedingSyncAsync()
        {
            try
            {
                return await Task.Run(() =>
                {
                    var characters = LiteDataContext.GetAllCharacterData();
                    var needingSync = new List<ulong>();

                    foreach (var character in characters)
                    {
                        if (character.IsDirty || 
                            (DateTime.Now - character.LastSyncTime).TotalMinutes > 5)
                        {
                            needingSync.Add(character.CharacterId);
                        }
                    }

                    return needingSync;
                });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterPersistence] 获取需要同步的角色列表失败: {ex.Message}");
                return new List<ulong>();
            }
        }

        /// <summary>
        /// 标记角色数据为已同步
        /// </summary>
        public async Task<bool> MarkCharacterSyncedAsync(ulong characterId)
        {
            try
            {
                var characterData = await Task.Run(() => LiteDataContext.GetCharacterData(characterId));
                
                if (characterData != null)
                {
                    characterData.IsDirty = false;
                    characterData.LastSyncTime = DateTime.Now;

                    return await Task.Run(() => 
                    {
                        LiteDataContext.SaveCharacterData(characterData);
                        return true;
                    });
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterPersistence] 标记角色同步状态失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 自动保存检查
        /// </summary>
        public void Update(float deltaTime)
        {
            _lastAutoSaveTime += deltaTime;
            
            if (_lastAutoSaveTime >= AUTO_SAVE_INTERVAL)
            {
                PerformAutoSave();
                _lastAutoSaveTime = 0f;
            }
        }

        /// <summary>
        /// 执行自动保存
        /// </summary>
        private async void PerformAutoSave()
        {
            try
            {
                var currentCharacter = DatabaseManager.GetCurrentCharacter();
                if (currentCharacter != null)
                {
                    // 检查是否需要保存（基于上次保存时间和数据变化）
                    if (_lastSaveTimes.TryGetValue(currentCharacter.CharacterId, out var lastSave) &&
                        (DateTime.Now - lastSave).TotalSeconds > 10)
                    {
                        // 这里可以添加更智能的脏数据检测逻辑
                        await SaveCharacterBasicInfoAsync(new CharacterInfo
                        {
                            CharacterId = currentCharacter.CharacterId,
                            CharacterName = currentCharacter.CharacterName,
                            Level = currentCharacter.Level,
                            Experience = (long)currentCharacter.Exp,
                            Profession = Enum.TryParse<Profession>(currentCharacter.Class, out var prof) ? prof : Profession.None
                        });

                        Debug.Log($"[CharacterPersistence] 自动保存完成: {currentCharacter.CharacterName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterPersistence] 自动保存失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        public void Dispose()
        {
            // 执行最终保存
            PerformAutoSave();
            _lastSaveTimes.Clear();
            Debug.Log("[CharacterPersistence] 角色持久化服务已清理");
        }
    }
}