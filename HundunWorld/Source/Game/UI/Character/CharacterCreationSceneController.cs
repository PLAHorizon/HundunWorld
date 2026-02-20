using FlaxEngine;
using HundunWorld.Game.Services;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network;
using Horizon.Game.Message.Enums;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.Character
{
    /// <summary>
    /// 角色创建场景控制器
    /// 管理角色创建UI的生命周期和场景交互
    /// </summary>
    public class CharacterCreationSceneController : Script
    {
        private IntegratedCharacterCreationUI _creationUI;
        private UIStateManager _stateManager;
        private NetworkManager _networkManager;

        /// <inheritdoc/>
        public override void OnStart()
        {
            InitializeManagers();
            InitializeUI();
            SubscribeEvents();

            Debug.Log("[CharacterCreationScene] 角色创建场景初始化完成");
        }

        /// <inheritdoc/>
        public override void OnDestroy()
        {
            UnsubscribeEvents();
            base.OnDestroy();
        }

        private void InitializeManagers()
        {
            _stateManager = UIStateManager.Instance;
            _networkManager = HundunWorldGame.Instance?.NetworkManager;
        }

        private void InitializeUI()
        {
            // 查找UI Canvas
            var uiCanvas = FindUICanvas();
            if (uiCanvas?.GUI == null)
            {
                Debug.LogError("[CharacterCreationScene] 找不到UI Canvas");
                return;
            }

            // 创建集成的角色创建UI
            _creationUI = new IntegratedCharacterCreationUI
            {
                Parent = uiCanvas.GUI,
                AnchorPreset = AnchorPresets.StretchAll
            };

            _creationUI.OnCharacterCreated += OnCharacterCreated;
            _creationUI.OnCancelled += OnCreationCancelled;

            _creationUI.Show();
        }

        private void SubscribeEvents()
        {
            if (_stateManager != null)
            {
                _stateManager.SceneChanged += OnSceneChanged;
            }
        }

        private void UnsubscribeEvents()
        {
            if (_stateManager != null)
            {
                _stateManager.SceneChanged -= OnSceneChanged;
            }
        }

        private void OnSceneChanged(SceneType previousScene, SceneType newScene)
        {
            if (_creationUI == null) return;

            if (newScene == SceneType.CharacterCreation)
            {
                _creationUI.Show();
                Debug.Log("[CharacterCreationScene] 显示角色创建UI");
            }
            else
            {
                _creationUI.Hide();
                Debug.Log("[CharacterCreationScene] 隐藏角色创建UI");
            }
        }

        private void OnCharacterCreated(CharacterInfo character)
        {
            Debug.Log($"[CharacterCreationScene] 角色创建成功: {character.CharacterName}");

            // 更新服务中的选中角色
            var characterService = CharacterService.Instance;
            characterService.SelectCharacter(character);

            // 切换到角色选择场景或直接进入游戏
            _stateManager.TransitionToScene(SceneType.CharacterSelection);
        }

        private void OnCreationCancelled()
        {
            Debug.Log("[CharacterCreationScene] 取消角色创建");

            // 返回角色选择场景
            _stateManager.TransitionToScene(SceneType.CharacterSelection);
        }

        private UICanvas FindUICanvas()
        {
            // 先尝试从当前Actor获取
            var canvas = Actor.GetScript<UICanvas>();
            if (canvas != null)
                return canvas;

            // 尝试在场景中查找
            var allActors = Level.Scenes[0].GetScripts<UICanvas>();
            if (allActors.Length > 0)
                return allActors[0];

            Debug.LogWarning("[CharacterCreationScene] 未找到UICanvas");
            return null;
        }
    }
}
