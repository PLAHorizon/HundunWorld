using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEngine;
using Game.Character.Attributes;
using Game.Database;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Enums;
using HundunWorld.Game.Equipment;
using HundunWorld.Game.UI.Authentication;
using EquipmentSlot = HundunWorld.Game.Equipment.EquipmentSlot;

namespace HundunWorld.Game.Services
{
    /// <summary>
    /// 五行属性数据
    /// </summary>
    public class WuxingAttributes
    {
        /// <summary>金属性</summary>
        public int Metal { get; set; }

        /// <summary>木属性</summary>
        public int Wood { get; set; }

        /// <summary>水属性</summary>
        public int Water { get; set; }

        /// <summary>火属性</summary>
        public int Fire { get; set; }

        /// <summary>土属性</summary>
        public int Earth { get; set; }
    }

    /// <summary>
    /// 背包物品数据
    /// </summary>
    public class InventoryItemData
    {
        /// <summary>物品ID</summary>
        public int ItemId { get; set; }

        /// <summary>物品数量</summary>
        public int Count { get; set; }
    }

    /// <summary>
    /// 角色数据持久化服务
    /// 负责角色数据的本地存储和同步管理
    /// </summary>
    public class CharacterPersistenceService
    {
        /// <summary>
        /// 角色外观数据
        /// </summary>
        public class AppearanceData
        {
            public int BodyEquipmentId { get; set; }
            public List<int> AccessoryIds { get; set; } = new List<int>();
            public List<int> WeaponIds { get; set; } = new List<int>();

            /// <summary>当前穿戴装备，键为装备槽位，值为装备ID</summary>
            public Dictionary<EquipmentSlot, int> EquippedItems { get; set; } = new Dictionary<EquipmentSlot, int>();

            /// <summary>背包物品列表</summary>
            public List<InventoryItemData> Inventory { get; set; } = new List<InventoryItemData>();

            public static AppearanceData GetDefaultAppearance() => new AppearanceData
            {
                BodyEquipmentId = 10001,
                AccessoryIds = new List<int> { 30001 },
                WeaponIds = new List<int> { 20001 },
                EquippedItems = new Dictionary<EquipmentSlot, int>
                {
                    { EquipmentSlot.Body, 10001 },
                    { EquipmentSlot.Head, 30001 },
                    { EquipmentSlot.RightHand, 20001 }
                },
                Inventory = new List<InventoryItemData>
                {
                    new InventoryItemData { ItemId = 10001, Count = 1 },
                    new InventoryItemData { ItemId = 20001, Count = 1 },
                    new InventoryItemData { ItemId = 30001, Count = 1 }
                }
            };
        }

        /// <summary>
        /// JSON 序列化选项，包含字段序列化以支持 EquipmentData 等字段型数据
        /// </summary>
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = false
        };

        private static CharacterPersistenceService _instance;
        private object _databaseManager; // 使用object类型避免静态类型问题
        private Dictionary<ulong, DateTime> _lastSaveTimes;
        private const float AUTO_SAVE_INTERVAL = 30.0f; // 30秒自动保存间隔
        private float _lastAutoSaveTime = 0f;

        public static CharacterPersistenceService Instance => _instance ??= new CharacterPersistenceService();

        /// <summary>
        /// 仅在已创建实例时释放资源，避免在Dispose期间创建新实例
        /// </summary>
        public static void DisposeIfCreated()
        {
            _instance?.Dispose();
            _instance = null;
        }

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
                var existingData = await Task.Run(() => LiteDataContext.GetCharacterData(characterInfo.CharacterId));
                var finalEquipment = equipment ?? new Dictionary<string, object>();

                // 保留已存储的外观数据，避免被传入的 equipment 覆盖
                if (existingData?.Equipment != null &&
                    existingData.Equipment.TryGetValue("Appearance", out var appearanceValue))
                {
                    finalEquipment["Appearance"] = appearanceValue;
                }
                else if (finalEquipment.ContainsKey("Appearance"))
                {
                    finalEquipment.Remove("Appearance");
                }

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
                    Equipment = finalEquipment,
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
        /// 保存角色外观数据
        /// </summary>
        public async Task<bool> SaveAppearanceAsync(ulong characterId, AppearanceData appearance)
        {
            try
            {
                var json = JsonSerializer.Serialize(appearance, _jsonOptions);
                var characterData = await Task.Run(() => LiteDataContext.GetCharacterData(characterId));

                if (characterData == null)
                {
                    characterData = new LiteDataContext.CharacterLocalData
                    {
                        CharacterId = characterId,
                        Equipment = new Dictionary<string, object>()
                    };
                }

                if (characterData.Equipment == null)
                    characterData.Equipment = new Dictionary<string, object>();

                characterData.Equipment["Appearance"] = json;

                bool success = await Task.Run(() =>
                {
                    LiteDataContext.SaveCharacterData(characterData);
                    return true;
                });

                if (success)
                {
                    _lastSaveTimes[characterId] = DateTime.Now;
                    Debug.Log($"[CharacterPersistence] 角色外观已保存: {characterId}");
                }

                return success;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterPersistence] 保存角色外观失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 加载角色外观数据
        /// </summary>
        public async Task<AppearanceData> LoadAppearanceAsync(ulong characterId)
        {
            try
            {
                var characterData = await Task.Run(() => LiteDataContext.GetCharacterData(characterId));

                if (characterData?.Equipment != null &&
                    characterData.Equipment.TryGetValue("Appearance", out var appearanceJson) &&
                    appearanceJson is string json)
                {
                    try
                    {
                        return JsonSerializer.Deserialize<AppearanceData>(json, _jsonOptions);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[CharacterPersistence] 反序列化角色外观失败: {ex.Message}");
                    }
                }

                return AppearanceData.GetDefaultAppearance();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterPersistence] 加载角色外观失败: {ex.Message}");
                return AppearanceData.GetDefaultAppearance();
            }
        }

        /// <summary>
        /// 保存角色属性数据
        /// </summary>
        /// <param name="characterId">角色ID</param>
        /// <param name="attributes">角色属性</param>
        public async Task<bool> SaveCharacterAttributesAsync(ulong characterId, CharacterAttributes attributes)
        {
            try
            {
                var json = JsonSerializer.Serialize(attributes, _jsonOptions);
                var characterData = await Task.Run(() => LiteDataContext.GetCharacterData(characterId));

                if (characterData == null)
                {
                    characterData = new LiteDataContext.CharacterLocalData
                    {
                        CharacterId = characterId,
                        Attributes = new Dictionary<string, object>()
                    };
                }

                if (characterData.Attributes == null)
                    characterData.Attributes = new Dictionary<string, object>();

                characterData.Attributes["CharacterAttributes"] = json;

                bool success = await Task.Run(() =>
                {
                    LiteDataContext.SaveCharacterData(characterData);
                    return true;
                });

                if (success)
                {
                    _lastSaveTimes[characterId] = DateTime.Now;
                    Debug.Log($"[CharacterPersistence] 角色属性已保存: {characterId}");
                }

                return success;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterPersistence] 保存角色属性失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 加载角色属性数据
        /// </summary>
        /// <param name="characterId">角色ID</param>
        public async Task<CharacterAttributes> LoadCharacterAttributesAsync(ulong characterId)
        {
            try
            {
                var characterData = await Task.Run(() => LiteDataContext.GetCharacterData(characterId));

                if (characterData?.Attributes != null &&
                    characterData.Attributes.TryGetValue("CharacterAttributes", out var attributesJson) &&
                    attributesJson is string json)
                {
                    try
                    {
                        return JsonSerializer.Deserialize<CharacterAttributes>(json, _jsonOptions);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[CharacterPersistence] 反序列化角色属性失败: {ex.Message}");
                    }
                }

                return CharacterAttributes.GetDefault();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterPersistence] 加载角色属性失败: {ex.Message}");
                return CharacterAttributes.GetDefault();
            }
        }

        /// <summary>
        /// 保存角色背包数据
        /// </summary>
        /// <param name="characterId">角色ID</param>
        /// <param name="inventory">背包物品列表</param>
        public async Task<bool> SaveInventoryAsync(ulong characterId, List<InventoryItemData> inventory)
        {
            try
            {
                var json = JsonSerializer.Serialize(inventory, _jsonOptions);
                var characterData = await Task.Run(() => LiteDataContext.GetCharacterData(characterId));

                if (characterData == null)
                {
                    characterData = new LiteDataContext.CharacterLocalData
                    {
                        CharacterId = characterId,
                        Attributes = new Dictionary<string, object>()
                    };
                }

                if (characterData.Attributes == null)
                    characterData.Attributes = new Dictionary<string, object>();

                characterData.Attributes["Inventory"] = json;

                bool success = await Task.Run(() =>
                {
                    LiteDataContext.SaveCharacterData(characterData);
                    return true;
                });

                if (success)
                {
                    _lastSaveTimes[characterId] = DateTime.Now;
                    Debug.Log($"[CharacterPersistence] 角色背包已保存: {characterId}");
                }

                return success;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterPersistence] 保存角色背包失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 加载角色背包数据
        /// </summary>
        /// <param name="characterId">角色ID</param>
        public async Task<List<InventoryItemData>> LoadInventoryAsync(ulong characterId)
        {
            try
            {
                var characterData = await Task.Run(() => LiteDataContext.GetCharacterData(characterId));

                if (characterData?.Attributes != null &&
                    characterData.Attributes.TryGetValue("Inventory", out var inventoryJson) &&
                    inventoryJson is string json)
                {
                    try
                    {
                        return JsonSerializer.Deserialize<List<InventoryItemData>>(json, _jsonOptions) ?? new List<InventoryItemData>();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[CharacterPersistence] 反序列化角色背包失败: {ex.Message}");
                    }
                }

                return new List<InventoryItemData>();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterPersistence] 加载角色背包失败: {ex.Message}");
                return new List<InventoryItemData>();
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