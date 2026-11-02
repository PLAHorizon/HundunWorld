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

        /// <summary>进入玩法时被临时停用的相机（离开 World 时恢复）。</summary>
        private static readonly System.Collections.Generic.List<Camera> MutedCameras = new System.Collections.Generic.List<Camera>();

        /// <summary>
        /// 只让玩法相机参与渲染。Flax 的 <c>Camera.GetMainCamera()</c> 取"第一个注册的 active 相机"，
        /// 而 Login 场景的相机同样 active ⇒ 渲染走的是登录相机，角色在 3km 外只剩一个点（2026-10-25 实测）。
        /// </summary>
        private static void MuteOtherSceneCameras(FlaxEngine.Scene keep)
        {
            for (int i = 0; i < Level.ScenesCount; i++)
            {
                var sc = Level.GetScene(i);
                if (sc == null || sc == keep)
                {
                    continue;
                }

                var cam = sc.FindActor<Camera>();
                if (cam != null && cam.IsActive)
                {
                    cam.IsActive = false;
                    MutedCameras.Add(cam);
                    Debug.Log($"[WorldSceneInitializer] 停用非玩法相机: scene={sc.Name} camera={cam.Name}");
                }
            }
        }

        /// <summary>离开 World（切回登录/选角）时把停用的相机恢复，否则那边没有相机可用。</summary>
        public override void OnDestroy()
        {
            for (int i = 0; i < MutedCameras.Count; i++)
            {
                var cam = MutedCameras[i];
                if (cam != null && !cam.IsActive)
                {
                    cam.IsActive = true;
                }
            }
            MutedCameras.Clear();
            base.OnDestroy();
        }

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

                // Ada 是**厘米**模型（约 180 单位高），而相机默认的 Distance=15 是按米制角色调的，
                // 直接套上去会把镜头怼进头部（2026-10-25 实测：画面只剩一颗悬浮的头）。
                // 这里按厘米口径给第三人称机位：聚焦胸口附近、距离 5 米、可拉近到 2 米。
                camera.FocusOffset = new Vector3(0f, 120f, 0f);
                camera.CloseRangeFocusHeight = 120f;
                camera.FarRangeFocusHeight = 120f;
                camera.Distance = 500f;
                camera.MinDistance = 200f;
                camera.MaxDistance = 2000f;
                camera.CloseRangeDistance = 500f;
                camera.FarRangeDistance = 1500f;

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

                // 只保留玩法相机参与渲染（否则 Flax 可能继续用 Login 相机，角色只剩远处一个点）。
                MuteOtherSceneCameras(camera.Actor?.Scene);
            }
            else
            {
                Debug.LogWarning("[WorldSceneInitializer] 未找到 ThirdPersonCamera，无法设置 Target");
            }
        }
    }
}
