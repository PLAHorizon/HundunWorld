using Arch.Core;
using Arch.Core.Utils;
using HundunWorld.Game.ECS.Components;
using System.Numerics;

namespace HundunWorld.Game.ECS.Systems
{
    /// <summary>
    /// 移动系统，负责更新具有位置和速度组件的实体
    /// </summary>
    public class MovementSystem : BaseSystem
    {
        private QueryDescription _queryDescription;

        public override void Initialize(World world)
        {
            base.Initialize(world);
            
            // 定义查询描述，查找同时具有位置和速度组件的实体
            _queryDescription = new QueryDescription().WithAll<PositionComponent, VelocityComponent>();
        }

        public override void Update(World world, float deltaTime)
        {
            // 查询所有具有位置和速度组件的实体
            world.Query(in _queryDescription, (Entity entity, ref PositionComponent position, ref VelocityComponent velocity) =>
            {
                // 根据速度更新位置
                position.Position += velocity.Velocity * deltaTime;
            });
        }
    }
}