using FlaxEngine;
using Game;

namespace HundunWorld.Game
{
    /// <summary>
    /// 游戏场景初始化脚本
    /// </summary>
    public class GameSceneInitializer : Script
    {
        /// <summary>
        /// 相机Actor名称
        /// </summary>
        [Tooltip("相机Actor名称")]
        public string CameraActorName { get; set; } = "Camera";

        /// <summary>
        /// 玩家Actor名称
        /// </summary>
        [Tooltip("玩家Actor名称")]
        public string PlayerActorName { get; set; } = "Player";

        /// <summary>
        /// 是否启用相机碰撞检测
        /// </summary>
        [Tooltip("是否启用相机碰撞检测")]
        public bool EnableCameraCollision { get; set; } = true;

        /// <summary>
        /// 碰撞平滑速度
        /// </summary>
        [Tooltip("碰撞平滑过渡速度")]
        public float CollisionSmoothSpeed { get; set; } = 10f;

        public override void OnStart()
        {
            InitializeScene();
        }

        /// <summary>
        /// 初始化场景
        /// </summary>
        private void InitializeScene()
        {
            // 查找相机和玩家Actor
            Actor cameraActor = Actor.FindActor(CameraActorName);
            Actor playerActor = Actor.FindActor(PlayerActorName);

            // 确保相机和玩家Actor存在
            if (cameraActor == null)
            {
                Debug.LogWarning("未找到相机Actor，请确保场景中包含名为'Camera'的Actor");
                return;
            }

            if (playerActor == null)
            {
                Debug.LogWarning("未找到玩家Actor，请确保场景中包含名为'Player'的Actor");
                return;
            }

            // 获取或添加ThirdPersonCamera脚本
            ThirdPersonCamera thirdPersonCamera = cameraActor.GetScript<ThirdPersonCamera>();
            if (thirdPersonCamera == null)
            {
                thirdPersonCamera = cameraActor.AddScript<ThirdPersonCamera>();
            }

            // 获取或添加CharacterController脚本
            PlayerController characterController = playerActor.GetScript<PlayerController>();
            if (characterController == null)
            {
                characterController = playerActor.AddScript<PlayerController>();
            }

            // 配置相机参数
            thirdPersonCamera.EnableCameraCollision = EnableCameraCollision;
            thirdPersonCamera.CollisionSmoothSpeed = CollisionSmoothSpeed;

            // 设置相机目标为玩家
            thirdPersonCamera.Target = playerActor;

            // 设置角色控制器的相机引用
            characterController.Camera = thirdPersonCamera;

            Debug.Log("场景初始化完成");
        }
    }
}