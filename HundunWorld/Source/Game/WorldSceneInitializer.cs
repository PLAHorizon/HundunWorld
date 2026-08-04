using FlaxEngine;
using Game.UI.Character;
using HundunWorld.Game;

namespace HundunWorld
{
    /// <summary>
    /// World 场景初始化器 - 兜底生成本地玩家角色
    /// 挂载到 World 场景的 Actor 上，场景加载后检查角色是否已生成
    /// 如果 5 秒内仍未生成，主动触发兜底生成
    /// </summary>
    public class WorldSceneInitializer : Script
    {
        private float _elapsedTime;
        private bool _playerSpawned;
        private bool _fallbackTriggered;
        private const float FallbackDelay = 5.0f;
        private const float MaxWaitTime = 30.0f;

        public override void OnStart()
        {
            _elapsedTime = 0;
            _playerSpawned = false;
            _fallbackTriggered = false;
            Debug.Log("[WorldSceneInitializer] OnStart - World 场景初始化器已启动");

            // 立即检查角色是否已生成（可能在场景加载过程中已由网络响应触发）
            CheckPlayerSpawned();
        }

        public override void OnUpdate()
        {
            if (_playerSpawned) return;

            _elapsedTime += Time.DeltaTime;

            // 每 0.5 秒检查一次
            if ((int)(_elapsedTime * 2) != (int)((_elapsedTime - Time.DeltaTime) * 2))
            {
                CheckPlayerSpawned();
            }

            // 超时兜底：5 秒后如果仍未生成，主动触发
            if (!_fallbackTriggered && _elapsedTime >= FallbackDelay)
            {
                _fallbackTriggered = true;
                Debug.LogWarning($"[WorldSceneInitializer] {FallbackDelay} 秒内未检测到本地玩家，触发兜底生成");
                TriggerFallbackSpawn();
            }

            // 最大等待时间，停止检查
            if (_elapsedTime >= MaxWaitTime)
            {
                Debug.LogError($"[WorldSceneInitializer] 等待 {MaxWaitTime} 秒后仍未生成角色，停止检查");
                _playerSpawned = true; // 停止更新
            }
        }

        /// <summary>
        /// 检查本地玩家是否已生成
        /// </summary>
        private void CheckPlayerSpawned()
        {
            var game = HundunWorldGame.Instance;
            if (game == null) return;

            var localPlayer = game.LocalPlayerActor;
            if (localPlayer != null)
            {
                _playerSpawned = true;
                Debug.Log($"[WorldSceneInitializer] 检测到本地玩家已生成: {localPlayer.Name}, Pos={localPlayer.Position}");

                // 设置相机 Target
                SetupCameraTarget(localPlayer);

                // 禁用自身（不再需要更新）
                Enabled = false;
            }
        }

        /// <summary>
        /// 兜底生成：使用本地缓存的角色数据主动调用 CreateLocalPlayerActor
        /// </summary>
        private void TriggerFallbackSpawn()
        {
            var game = HundunWorldGame.Instance;
            if (game == null)
            {
                Debug.LogError("[WorldSceneInitializer] HundunWorldGame.Instance 为空，无法兜底生成");
                return;
            }

            // 如果已经有待生成请求，说明网络响应链路已缓存请求但场景切换事件未触发
            // 直接调用 CreateLocalPlayerActor 使用默认位置
            ulong characterId = 0;
            float x = 0, y = 100, z = 0;

            // 尝试从 CharacterManager 获取选中角色信息
            var charMgr = CharacterManager.Instance;
            if (charMgr != null)
            {
                var selectedChar = charMgr.SelectedCharacter;
                if (selectedChar != null)
                {
                    characterId = selectedChar.CharacterId;
                    Debug.Log($"[WorldSceneInitializer] 从 CharacterManager 获取角色: Id={characterId}, Name={selectedChar.CharacterName}");
                }
            }

            Debug.Log($"[WorldSceneInitializer] 兜底生成本地玩家: CharacterId={characterId}, Pos=({x},{y},{z})");

            var actor = game.CreateLocalPlayerActor(characterId, x, y, z);
            if (actor != null)
            {
                _playerSpawned = true;
                Debug.Log($"[WorldSceneInitializer] 兜底生成成功: {actor.Name}");

                // 设置相机 Target
                SetupCameraTarget(actor);

                // 禁用自身
                Enabled = false;
            }
            else
            {
                Debug.LogError("[WorldSceneInitializer] 兜底生成失败：CreateLocalPlayerActor 返回 null");
            }
        }

        /// <summary>
        /// 查找场景中的 ThirdPersonCamera 并设置 Target
        /// </summary>
        private void SetupCameraTarget(Actor playerActor)
        {
            if (playerActor == null) return;

            // 查找场景中的 ThirdPersonCamera
            var camera = Actor?.Scene?.GetScript<ThirdPersonCamera>();
            if (camera == null)
            {
                // 全局查找
                var allCameras = Level.GetScripts<ThirdPersonCamera>();
                if (allCameras != null && allCameras.Length > 0)
                {
                    camera = allCameras[0];
                }
            }

            if (camera != null)
            {
                camera.Target = playerActor;
                Debug.Log($"[WorldSceneInitializer] 已设置 ThirdPersonCamera.Target = {playerActor.Name}");

                // 修复：将相机引用赋值给 PlayerController，避免 TryGetLookYawPitch 走降级路径。
                // 降级路径用 Actor.Orientation.Yaw 作为 LookYaw 发送服务端，导致 entity.Yaw 不准、
                // 远程角色朝向异常，并影响 InterpolationSystem 传送混合的 Yaw 起始值（TeleportBlendStartYaw）。
                // 原 GameSceneInitializer 静态路径有此赋值，但本兜底生成路径遗漏了。
                var playerController = playerActor.GetScript<PlayerController>();
                if (playerController != null)
                {
                    playerController.Camera = camera;
                    Debug.Log($"[WorldSceneInitializer] 已设置 PlayerController.Camera (cameraActor={camera.Actor?.Name ?? "null"})");
                }
                else
                {
                    Debug.LogWarning($"[WorldSceneInitializer] PlayerActor '{playerActor.Name}' 上未找到 PlayerController 脚本，无法赋值 Camera 引用");
                }
            }
            else
            {
                Debug.LogWarning("[WorldSceneInitializer] 未找到 ThirdPersonCamera，无法设置 Target");
            }
        }
    }
}
