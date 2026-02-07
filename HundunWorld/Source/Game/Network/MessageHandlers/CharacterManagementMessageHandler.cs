using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Services;
using HundunWorld.Game.UI;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.Network.MessageHandlers
{
    /// <summary>
    /// 角色管理消息处理器
    /// 处理与角色创建、删除、查询相关的服务器响应
    /// </summary>
    public class CharacterManagementMessageHandler : IMessageHandler
    {
        private readonly CharacterService _characterService;
        private readonly UI.UIStateManager _stateManager;

        public CharacterManagementMessageHandler()
        {
            _characterService = CharacterService.Instance;
            _stateManager = UI.UIStateManager.Instance;
        }

        /// <summary>
        /// 处理创建角色响应
        /// </summary>
        public void HandleCreateCharacterResponse(CreateCharacterResponse response)
        {
            try
            {
                Debug.Log($"[CharacterManagementMessageHandler] 收到创建角色响应: Success={response.IsSuccess}, Message={response.Message}");

                if (response.IsSuccess && response.Character != null)
                {
                    // 添加新角色到缓存
                    _characterService.AddCharacterToCache(response.Character);
                    
                    // 发布事件通知UI更新
                    var stateManager = UI.UIStateManager.Instance;
                    // 通过更新角色列表来间接触发事件
                    stateManager.UpdateCharacterList(_characterService.GetCachedCharacters());
                    
                    Debug.Log($"[CharacterManagementMessageHandler] 角色创建成功: {response.Character.CharacterName}");
                }
                else
                {
                    Debug.LogWarning($"[CharacterManagementMessageHandler] 角色创建失败: {response.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterManagementMessageHandler] 处理创建角色响应时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理删除角色响应
        /// </summary>
        public void HandleDeleteCharacterResponse(DeleteCharacterResponse response)
        {
            try
            {
                Debug.Log($"[CharacterManagementMessageHandler] 收到删除角色响应: Success={response.Success}, Message={response.Message}");

                if (response.Success)
                {
                    // 从缓存中移除角色
                    _characterService.RemoveCharacterFromCache(response.CharacterId);
                    
                    // 发布事件通知UI更新
                    var stateManager = UI.UIStateManager.Instance;
                    // 通过更新角色列表来间接触发事件
                    stateManager.UpdateCharacterList(_characterService.GetCachedCharacters());
                    
                    Debug.Log($"[CharacterManagementMessageHandler] 角色删除成功: {response.CharacterId}");
                }
                else
                {
                    Debug.LogWarning($"[CharacterManagementMessageHandler] 角色删除失败: {response.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterManagementMessageHandler] 处理删除角色响应时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理角色列表响应
        /// </summary>
        public void HandleCharacterListResponse(CharacterListResponse response)
        {
            try
            {
                Debug.Log($"[CharacterManagementMessageHandler] 收到角色列表响应: Count={response.Characters?.Count ?? 0}");

                if (response.Characters != null)
                {
                    // 更新角色缓存
                    foreach (var character in response.Characters)
                    {
                        _characterService.UpdateCharacterCache(character);
                    }
                    
                    // 发布事件通知UI更新
                    var stateManager = UI.UIStateManager.Instance;
                    // 通过更新角色列表来间接触发事件
                    stateManager.UpdateCharacterList(response.Characters);
                    
                    Debug.Log($"[CharacterManagementMessageHandler] 角色列表更新完成，共 {response.Characters.Count} 个角色");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterManagementMessageHandler] 处理角色列表响应时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理进入游戏响应
        /// </summary>
        public void HandleEnterGameResponse(EnterGameResponse response)
        {
            try
            {
                Debug.Log($"[CharacterManagementMessageHandler] 收到进入游戏响应: Success={response.IsSuccess}, Message={response.Message}");

                if (response.IsSuccess)
                {
                    // 更新用户状态
                    var stateManager = UI.UIStateManager.Instance;
                    if (stateManager != null)
                    {
                        // 使用正确的参数调用UpdateUserSession
                        stateManager.UpdateUserSession(
                            "temp_user", // 临时用户名
                            response.CharacterId, 
                            "temp_token", // 临时token
                            ""
                        );
                    }

                    // 切换到游戏场景
                    stateManager?.TransitionToScene(SceneType.GameWorld);
                    
                    Debug.Log($"[CharacterManagementMessageHandler] 进入游戏成功，角色ID: {response.CharacterId}");
                }
                else
                {
                    Debug.LogWarning($"[CharacterManagementMessageHandler] 进入游戏失败: {response.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterManagementMessageHandler] 处理进入游戏响应时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理角色名称验证响应
        /// </summary>
        public void HandleValidateCharacterNameResponse(ValidateCharacterNameResponse response)
        {
            try
            {
                Debug.Log($"[CharacterManagementMessageHandler] 收到角色名称验证响应: IsValid={response.IsValid}, Message={response.Message}");
                
                // 这里可以触发UI中的验证提示
                // 例如调用CharacterCreationUI中的验证回调方法
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterManagementMessageHandler] 处理角色名称验证响应时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取当前缓存的角色列表
        /// </summary>
        public List<CharacterInfo> GetCachedCharacters()
        {
            return _characterService.GetCachedCharacters();
        }

        /// <summary>
        /// 获取选中的角色
        /// </summary>
        public CharacterInfo GetSelectedCharacter()
        {
            return _characterService.GetSelectedCharacter();
        }
    }

    /// <summary>
    /// 消息处理器接口
    /// </summary>
    public interface IMessageHandler
    {
        // 基础接口，可以根据需要扩展
    }
}