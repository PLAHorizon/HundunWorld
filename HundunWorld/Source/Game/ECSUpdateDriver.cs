using FlaxEngine;

namespace HundunWorld.Game
{
    /// <summary>
    /// ECS更新驱动脚本，负责在Flax主线程每帧更新ECS系统
    /// </summary>
    public class ECSUpdateDriver : Script
    {
        public override void OnUpdate()
        {
            // 确保游戏已初始化并启动
            if (HundunWorldGame.Instance != null && 
                HundunWorldGamePlugin.Instance != null &&
                HundunWorldGame.Instance.ECSManager != null)
            {
                // 在Flax主线程上更新ECS系统
                HundunWorldGame.Instance.ECSManager.Update(Time.DeltaTime);
            }
        }
    }
}
