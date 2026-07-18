using System;
using System.Collections.Generic;
using FlaxEngine;
using Horizon.Game.Message.Network;

namespace HundunWorld.Game.UI.Character
{
    /// <summary>
    /// 角色状态管理器 - 负责角色数据、选中状态和角色 ID 的管理
    /// 从 CharacterSceneController 中提取的职责：
    /// - 角色列表维护
    /// - 选中角色状态
    /// - 角色 ID 同步与事件发布
    /// </summary>
    public class CharacterStateManager
    {
        private List<CharacterInfo> _characters = new List<CharacterInfo>();
        private CharacterInfo _selectedCharacter;
        private string _currentCharacterId = "0126998214";

        /// <summary>
        /// 当前角色 ID
        /// </summary>
        public string CurrentCharacterId => _currentCharacterId;

        /// <summary>
        /// 当前选中的角色
        /// </summary>
        public CharacterInfo SelectedCharacter => _selectedCharacter;

        /// <summary>
        /// 角色 ID 变更事件
        /// </summary>
        public event Action<string> OnCharacterIdChanged;

        /// <summary>
        /// 角色列表变更事件
        /// </summary>
        public event Action<List<CharacterInfo>> OnCharacterListChanged;

        /// <summary>
        /// 角色选中事件
        /// </summary>
        public event Action<CharacterInfo> OnCharacterSelected;

        /// <summary>
        /// 更新角色列表
        /// </summary>
        public void UpdateCharacterList(List<CharacterInfo> characters)
        {
            _characters = characters ?? new List<CharacterInfo>();
            OnCharacterListChanged?.Invoke(_characters);
        }

        /// <summary>
        /// 获取当前角色列表
        /// </summary>
        public List<CharacterInfo> GetCharacters()
        {
            return _characters;
        }

        /// <summary>
        /// 选中角色
        /// </summary>
        public void SelectCharacter(CharacterInfo character)
        {
            _selectedCharacter = character;
            OnCharacterSelected?.Invoke(character);
        }

        /// <summary>
        /// 设置当前角色 ID
        /// </summary>
        public void SetCharacterId(string id)
        {
            if (string.IsNullOrEmpty(id) || _currentCharacterId == id)
                return;

            _currentCharacterId = id;
            Debug.Log($"[CharacterStateManager] 角色ID已更新: {id}");

            try
            {
                OnCharacterIdChanged?.Invoke(id);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterStateManager] OnCharacterIdChanged 订阅者抛出异常: {ex}");
            }
        }
    }
}
