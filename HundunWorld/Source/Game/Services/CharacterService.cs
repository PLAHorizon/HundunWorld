using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network;

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
                var networkManager = HundunWorldGame.Instance?.NetworkManager;
                if (networkManager == null || !networkManager.CanSendMessage())
                {
                    Debug.LogWarning("[CharacterService] 网络未连接，无法创建角色");
                    return new CreateCharacterResponse
                    {
                        IsSuccess = false,
                        Message = "网络未连接"
                    };
                }

                var request = new CreateCharacterRequest
                {
                    CharacterName = characterName,
                    Profession = profession,
                    Gender = gender,
                    Appearance = appearance,
                };

                bool sent = await networkManager.SendAsync(request);
                if (!sent)
                {
                    Debug.LogWarning("[CharacterService] 创建角色请求发送失败");
                    return new CreateCharacterResponse
                    {
                        IsSuccess = false,
                        Message = "请求发送失败"
                    };
                }

                Debug.Log($"[CharacterService] 创建角色请求已发送: {characterName}");
                // 请求已发送到服务器，实际创建结果将通过 CreateCharacterHandler 异步回调处理
                // 此处返回表示请求发送成功，非最终创建结果
                return new CreateCharacterResponse
                {
                    IsSuccess = false,
                    Message = "请求已发送，等待服务器响应"
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
        /// 注意：此方法发送请求后立即返回当前缓存数据。
        /// 服务器响应将通过 CharacterListHandler 异步回调处理并更新缓存，
        /// 调用方应监听 UIStateManager.UpdateCharacterList 事件获取最新数据。
        /// </summary>
        public async Task<List<CharacterInfo>> GetCharacterListAsync()
        {
            try
            {
                // 如果缓存中有数据，先返回缓存
                if (_characterCache.Count > 0)
                {
                    return GetCachedCharacters();
                }

                var networkManager = HundunWorldGame.Instance?.NetworkManager;
                if (networkManager == null || !networkManager.CanSendMessage())
                {
                    Debug.LogWarning("[CharacterService] 网络未连接，返回本地缓存");
                    return GetCachedCharacters();
                }

                var request = new CharacterListRequest();
                bool sent = await networkManager.SendAsync(request);
                if (!sent)
                {
                    Debug.LogWarning("[CharacterService] 角色列表请求发送失败");
                    return GetCachedCharacters();
                }

                Debug.Log("[CharacterService] 角色列表请求已发送，等待服务器响应");
                // 响应将通过 CharacterListHandler 异步回调处理，更新缓存
                return GetCachedCharacters();
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
                var networkManager = HundunWorldGame.Instance?.NetworkManager;
                if (networkManager == null || !networkManager.CanSendMessage())
                {
                    Debug.LogWarning("[CharacterService] 网络未连接，无法删除角色");
                    return false;
                }

                var request = new DeleteCharacterRequest
                {
                    CharacterId = characterId
                };

                bool sent = await networkManager.SendAsync(request);
                if (!sent)
                {
                    Debug.LogWarning("[CharacterService] 删除角色请求发送失败");
                    return false;
                }

                Debug.Log($"[CharacterService] 删除角色请求已发送: {characterId}");
                // 响应将通过 DeleteCharacterResponseHandler 异步回调处理
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
                var character = GetCharacterFromCache(characterId);
                if (character == null)
                {
                    Debug.LogWarning($"[CharacterService] 未找到角色: {characterId}");
                    return false;
                }

                var networkManager = HundunWorldGame.Instance?.NetworkManager;
                if (networkManager == null || !networkManager.CanSendMessage())
                {
                    Debug.LogWarning("[CharacterService] 网络未连接，无法选择角色");
                    return false;
                }

                var request = new EnterGameRequest
                {
                    CharacterId = characterId
                };

                bool sent = await networkManager.SendAsync(request);
                if (!sent)
                {
                    Debug.LogWarning("[CharacterService] 选择角色请求发送失败");
                    return false;
                }

                _selectedCharacter = character;
                Debug.Log($"[CharacterService] 选择角色请求已发送: {character.CharacterName}");
                // 响应将通过 EnterGameHandler 异步回调处理
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"选择角色失败: {ex.Message}");
                return false;
            }
        }
    }
}