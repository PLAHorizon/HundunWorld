using Arch.Core;
using FlaxEngine;
using Game;
using HundunWorld.Game.ECS.Components;
using HundunWorld.Game.ECS.Systems;
using System.Threading.Tasks;

namespace HundunWorld.Game
{
    /// <summary>
    /// 混沌世界游戏脚本，用于在FlaxEngine中运行游戏
    /// </summary>
    public class HundunWorldGamePlugin : Plugin
    {
        private HundunWorldGame _game;
        private bool _isGameStarted = false;
        private CameraSystem _cameraSystem;
        private CharacterControllerSystem _characterControllerSystem;
        private InputSystem _inputSystem;
        //private ThirdPersonCamera _thirdPersonCamera;
        //private PlayerController _characterController;


        // 添加一个静态属性来获取插件实例
        private static HundunWorldGamePlugin _instance;
        public static HundunWorldGamePlugin Instance => _instance;

        public static void Init()
        {
            // 确保只创建一个实例
            if (_instance == null)
            {
                _instance = new HundunWorldGamePlugin();
                _instance.Initialize();
            }
        }

        public override void Initialize()
        {
            // 初始化游戏
            _game = new HundunWorldGame();

            // 初始化ECS系统
            InitializeECSSystems();

            // 初始化相机和角色控制器
            InitializeCameraAndCharacterController();

            // 启动游戏
            _ = StartGameAsync();
        }

        /// <summary>
        /// 初始化相机和角色控制器
        /// </summary>
        private void InitializeCameraAndCharacterController()
        {
            // 查找场景中的相机和角色
            Actor cameraActor = Level.FindActor("Camera");
            Actor playerActor = Level.FindActor("Player");

            if (cameraActor != null && playerActor != null)
            {
                // 获取或添加ThirdPersonCamera脚本
                ThirdPersonCamera thirdPersonCamera = cameraActor.GetScript<ThirdPersonCamera>();
                if (thirdPersonCamera == null)
                {
                    thirdPersonCamera = cameraActor.AddScript<ThirdPersonCamera>();
                }

                // 获取或添加PlayerController脚本
                PlayerController characterController = playerActor.GetScript<PlayerController>();
                if (characterController == null)
                {
                    characterController = playerActor.AddScript<PlayerController>();
                }

                // 关联相机和角色控制器
                thirdPersonCamera.Target = playerActor;
                characterController.Camera = thirdPersonCamera;

                Debug.Log("相机和角色控制器初始化完成");
            }
            else
            {
                Debug.LogWarning("未找到相机或玩家Actor，请确保场景中包含名为'Camera'和'Player'的Actor");
            }
        }

        /// <summary>
        /// 初始化ECS系统
        /// </summary>
        private void InitializeECSSystems()
        {
            // 添加输入系统
            _inputSystem = new InputSystem();
            _game.ECSManager.AddSystem(_inputSystem);

            // 添加移动系统
            _game.ECSManager.AddSystem(new MovementSystem());

            // 添加相机系统
            _cameraSystem = new CameraSystem();
            _game.ECSManager.AddSystem(_cameraSystem);

            // 添加角色控制器系统
            _characterControllerSystem = new CharacterControllerSystem();
            _game.ECSManager.AddSystem(_characterControllerSystem);

            // 添加渲染系统
            _game.ECSManager.AddSystem(new RenderingSystem());

            // 添加生命值系统
            _game.ECSManager.AddSystem(new HealthSystem());

            // 创建一些示例实体
            CreateSampleEntities();
        }

        /// <summary>
        /// 创建示例实体
        /// </summary>
        private void CreateSampleEntities()
        {
            // 获取Arch ECS世界
            var world = _game.ArchWorld;

            // 创建一个玩家实体，带有位置、速度、相机、角色控制器和输入组件
            var playerEntity = world.Create();
            world.Add(playerEntity, new PositionComponent(0, 0, 0));
            world.Add(playerEntity, new VelocityComponent(0, 0, 0));
            world.Add(playerEntity, new CameraComponent(10.0f, 30.0f, 45.0f)); // 距离10，俯仰角30度，偏航角45度
            world.Add(playerEntity, new CharacterControllerComponent(5.0f, 10.0f)); // 移动速度5，跳跃力度10
            world.Add(playerEntity, new InputComponent());
            world.Add(playerEntity, new HealthComponent(100.0f));

            // 将实体添加到世界管理器
            _game.WorldManager.AddEntity(1, playerEntity);

            // 创建一个静止的实体，带有较低的生命值
            var entity2 = world.Create();
            world.Add(entity2, new PositionComponent(5, 0, 0));
            world.Add(entity2, new HealthComponent(30.0f, 100.0f));

            // 将实体添加到世界管理器
            _game.WorldManager.AddEntity(2, entity2);
        }

        /// <summary>
        /// 启动游戏
        /// </summary>
        private async Task StartGameAsync()
        {
            // 连接到网关（示例地址）
            // bool connected = await _game.ConnectToGatewayAsync("127.0.0.1", 8080);

            // if (connected)
            // {
            // 启动游戏
            await _game.StartAsync();
            _isGameStarted = true;
            Debug.Log("混沌世界游戏已启动");
            // }
            // else
            // {
            //     Debug.LogError("无法连接到游戏网关");
            // }
        }



        public override void Deinitialize()
        {

            _instance = null;
            // 释放游戏资源
            _game?.Dispose();
        }
    }
}