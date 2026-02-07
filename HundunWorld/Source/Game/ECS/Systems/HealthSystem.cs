using Arch.Core;
using Arch.Core.Utils;
using HundunWorld.Game.ECS.Components;

namespace HundunWorld.Game.ECS.Systems
{
    /// <summary>
    /// 生命值系统，负责处理具有生命值组件的实体
    /// </summary>
    public class HealthSystem : BaseSystem
    {
        private QueryDescription _queryDescription;

        public override void Initialize(World world)
        {
            base.Initialize(world);
            
            // 定义查询描述，查找具有生命值组件的实体
            _queryDescription = new QueryDescription().WithAll<HealthComponent>();
        }

        public override void Update(World world, float deltaTime)
        {
            // 查询所有具有生命值组件的实体
            world.Query(in _queryDescription, (Entity entity, ref HealthComponent health) =>
            {
                // 在这里可以处理生命值相关的逻辑
                // 例如：自然恢复、持续伤害等
                
                // 示例：如果生命值低于最大值的50%，每秒恢复1点生命值
                if (health.HealthPercentage < 0.5f)
                {
                    health.CurrentHealth += 1.0f * deltaTime;
                    if (health.CurrentHealth > health.MaxHealth)
                    {
                        health.CurrentHealth = health.MaxHealth;
                    }
                    
                    // 更新组件
                    world.Set(entity, health);
                }
            });
        }
    }
}