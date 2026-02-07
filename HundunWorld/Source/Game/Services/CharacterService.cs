using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;

namespace HundunWorld.Game.Services
{
    /// <summary>
    /// 角色服务类
    /// 负责角色相关的业务逻辑
    /// </summary>
    public class CharacterService
    {
        private static CharacterService _instance;
        public static CharacterService Instance => _instance ??= new CharacterService();

        // 角色数据缓存
        private Dictionary<ulong, CharacterInfo> _characterCache = new Dictionary<ulong, CharacterInfo>();
        private CharacterInfo _selectedCharacter;

        public CharacterInfo SelectedCharacter => _selectedCharacter;
        public IReadOnlyDictionary<ulong, CharacterInfo> CharacterCache => _characterCache;

        private CharacterService()
        {
        }

        /// <summary>
        /// 选择角色
        /// </summary>
        public void SelectCharacter(CharacterInfo character)
        {
            _selectedCharacter = character;
            Debug.Log($"选择角色: {character?.CharacterName ?? "无"}");
        }

        /// <summary>
        /// 添加角色到缓存
        /// </summary>
        public void AddCharacterToCache(CharacterInfo character)
        {
            if (character != null)
            {
                _characterCache[character.CharacterId] = character;
                Debug.Log($"角色已添加到缓存: {character.CharacterName}");
            }
        }

        /// <summary>
        /// 从缓存中获取角色
        /// </summary>
        public CharacterInfo GetCharacterFromCache(ulong characterId)
        {
            return _characterCache.TryGetValue(characterId, out var character) ? character : null;
        }

        /// <summary>
        /// 清除角色缓存
        /// </summary>
        public void ClearCharacterCache()
        {
            _characterCache.Clear();
            _selectedCharacter = null;
            Debug.Log("角色缓存已清除");
        }

        /// <summary>
        /// 更新角色信息
        /// </summary>
        public void UpdateCharacterInfo(CharacterInfo character)
        {
            if (character != null)
            {
                _characterCache[character.CharacterId] = character;
                if (_selectedCharacter?.CharacterId == character.CharacterId)
                {
                    _selectedCharacter = character;
                }
                Debug.Log($"角色信息已更新: {character.CharacterName}");
            }
        }

        /// <summary>
        /// 获取缓存的角色列表
        /// </summary>
        public List<CharacterInfo> GetCachedCharacters()
        {
            return _characterCache.Values.ToList();
        }

        /// <summary>
        /// 从缓存中移除角色
        /// </summary>
        public void RemoveCharacterFromCache(ulong characterId)
        {
            if (_characterCache.ContainsKey(characterId))
            {
                _characterCache.Remove(characterId);
                if (_selectedCharacter?.CharacterId == characterId)
                {
                    _selectedCharacter = null;
                }
                Debug.Log($"角色已从缓存中移除: {characterId}");
            }
        }

        /// <summary>
        /// 更新角色缓存
        /// </summary>
        public void UpdateCharacterCache(CharacterInfo character)
        {
            if (character != null)
            {
                _characterCache[character.CharacterId] = character;
                Debug.Log($"角色缓存已更新: {character.CharacterName}");
            }
        }

        /// <summary>
        /// 获取选中的角色
        /// </summary>
        public CharacterInfo GetSelectedCharacter()
        {
            return _selectedCharacter;
        }

        /// <summary>
        /// 异步创建角色
        /// </summary>
        public async Task<CreateCharacterResponse> CreateCharacterAsync(string characterName, Profession profession, int gender, AppearanceInfo appearance)
        {
            try
            {
                // 这里应该调用网络管理器发送创建角色请求
                // 暂时返回成功模拟
                await Task.Delay(1000);
                Debug.Log($"角色创建成功: {characterName}");
                
                // 创建模拟的响应对象
                var newCharacter = new CharacterInfo
                {
                    CharacterId = (ulong)new Random().Next(1000, 9999),
                    CharacterName = characterName,
                    Profession = profession,
                    Gender = gender,
                    Level = 1,
                    Experience = 0,
                    Gold = 0,
                    CreatedTime = DateTime.Now
                };
                
                return new CreateCharacterResponse
                {
                    IsSuccess = true,
                    Message = "角色创建成功",
                    Character = newCharacter
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"创建角色失败: {ex.Message}");
                return new CreateCharacterResponse
                {
                    IsSuccess = false,
                    Message = ex.Message
                };
            }
        }

        /// <summary>
        /// 异步获取角色列表
        /// </summary>
        public async Task<List<CharacterInfo>> GetCharacterListAsync()
        {
            try
            {
                // 这里应该调用网络管理器发送获取角色列表请求
                // 暂时返回模拟数据
                await Task.Delay(500);
                
                // 如果缓存中有数据，返回缓存数据
                if (_characterCache.Count > 0)
                {
                    return GetCachedCharacters();
                }
                
                // 模拟一些默认角色数据
                var mockCharacters = new List<CharacterInfo>
                {
                    new CharacterInfo
                    {
                        CharacterId = 1001,
                        CharacterName = "剑仙",
                        Profession = Profession.Shaolin,
                        Gender = 0,
                        Level = 10,
                        Experience = 5000,
                        Gold = 1000,
                        CreatedTime = DateTime.Now.AddDays(-30)
                    },
                    new CharacterInfo
                    {
                        CharacterId = 1002,
                        CharacterName = "法师",
                        Profession = Profession.Wudang,
                        Gender = 1,
                        Level = 8,
                        Experience = 3200,
                        Gold = 800,
                        CreatedTime = DateTime.Now.AddDays(-20)
                    }
                };
                
                // 缓存模拟数据
                foreach (var character in mockCharacters)
                {
                    _characterCache[character.CharacterId] = character;
                }
                
                Debug.Log($"获取到 {mockCharacters.Count} 个角色");
                return mockCharacters;
            }
            catch (Exception ex)
            {
                Debug.LogError($"获取角色列表失败: {ex.Message}");
                return new List<CharacterInfo>();
            }
        }

        /// <summary>
        /// 异步删除角色
        /// </summary>
        public async Task<bool> DeleteCharacterAsync(ulong characterId)
        {
            try
            {
                // 这里应该调用网络管理器发送删除角色请求
                // 暂时返回成功模拟
                await Task.Delay(500);
                
                // 从缓存中移除
                if (_characterCache.ContainsKey(characterId))
                {
                    _characterCache.Remove(characterId);
                    if (_selectedCharacter?.CharacterId == characterId)
                    {
                        _selectedCharacter = null;
                    }
                }
                
                Debug.Log($"角色删除成功: {characterId}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"删除角色失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 选择角色（添加缺失的方法）
        /// </summary>
        public async Task<bool> SelectCharacterAsync(ulong characterId)
        {
            try
            {
                // 这里应该调用网络管理器发送选择角色请求
                // 暂时返回成功模拟
                await Task.Delay(300);
                
                var character = GetCharacterFromCache(characterId);
                if (character != null)
                {
                    _selectedCharacter = character;
                    Debug.Log($"角色选择成功: {character.CharacterName}");
                    return true;
                }
                
                Debug.LogWarning($"未找到角色: {characterId}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"选择角色失败: {ex.Message}");
                return false;
            }
        }
    }
}