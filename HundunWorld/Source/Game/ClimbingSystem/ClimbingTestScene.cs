using FlaxEngine;

namespace HundunWorld.Game.ClimbingSystem
{
    /// <summary>
    /// 攀爬系统测试场景脚本
    /// </summary>
    public class ClimbingTestScene : Script
    {
        /// <summary>
        /// 玩家角色引用
        /// </summary>
        [Tooltip("玩家角色引用")]
        public Actor PlayerCharacter { get; set; }
        
        /// <summary>
        /// 测试用的可攀爬墙面
        /// </summary>
        [Tooltip("测试用的可攀爬墙面")]
        public Actor ClimbableWall { get; set; }
        
        public override void OnStart()
        {
            // 初始化测试场景
            SetupTestEnvironment();
        }
        
        /// <summary>
        /// 设置测试环境
        /// </summary>
        private void SetupTestEnvironment()
        {
            if (PlayerCharacter == null)
            {
                Debug.LogWarning("未设置玩家角色引用");
                return;
            }
            
            // 确保玩家角色上有必要的组件
            EnsureComponent<PlayerController>(PlayerCharacter);
            EnsureComponent<ClimbDetector>(PlayerCharacter);
            EnsureComponent<ClimbingController>(PlayerCharacter);
            
            Debug.Log("攀爬系统测试环境已设置完成");
        }
        
        /// <summary>
        /// 确保Actor上有指定组件
        /// </summary>
        /// <typeparam name="T">组件类型</typeparam>
        /// <param name="actor">目标Actor</param>
        private void EnsureComponent<T>(Actor actor) where T : Script
        {
            if (actor.GetScript<T>() == null)
            {
                actor.AddScript(typeof(T));
                Debug.Log($"已添加 {typeof(T).Name} 组件到 {actor.Name}");
            }
        }
        
        public override void OnUpdate()
        {
            // 显示攀爬系统状态信息
            DisplayClimbingStatus();
        }
        
        /// <summary>
        /// 显示攀爬系统状态信息
        /// </summary>
        private void DisplayClimbingStatus()
        {
            if (PlayerCharacter == null)
                return;
                
            var climbDetector = PlayerCharacter.GetScript<ClimbDetector>();
            var climbingController = PlayerCharacter.GetScript<ClimbingController>();
            
            if (climbDetector != null && climbingController != null)
            {
                string statusInfo = $"攀爬检测: {(climbDetector.IsClimbableEdgeDetected ? "检测到可攀爬边缘" : "未检测到可攀爬边缘")}\n" +
                                   $"攀爬类型: {climbDetector.DetectedClimbType}\n" +
                                   $"攀爬状态: {climbingController.CurrentClimbingState}\n" +
                                   $"是否正在攀爬: {climbingController.IsClimbing()}";
                
                // 在调试输出中显示状态信息
                Debug.Log(statusInfo);
            }
        }
    }
}