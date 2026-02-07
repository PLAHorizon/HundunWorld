using Arch.Core;
using Arch.Core.Utils;
using HundunWorld.Game.ECS.Components;

namespace HundunWorld.Game.ECS.Systems
{
    /// <summary>
    /// 渲染系统，负责渲染具有位置组件的实体
    /// </summary>
    public class RenderingSystem : BaseSystem
    {
        private QueryDescription _queryDescription;

        public override void Initialize(World world)
        {
            base.Initialize(world);
            
            // 定义查询描述，查找具有位置组件的实体
            _queryDescription = new QueryDescription().WithAll<PositionComponent>();
        }

        public override void Update(World world, float deltaTime)
        {
            // 这里可以更新渲染相关的状态
            // 实际的渲染操作会在Render方法中执行
        }

        public override void Render(World world)
        {
            // 查询所有具有位置组件的实体并进行渲染
            world.Query(in _queryDescription, (Entity entity, ref PositionComponent position) =>
            {
                // 在实际项目中，这里会调用FlaxEngine的渲染API来渲染实体
                // 例如：RenderEntity(entity, position.Position);
            });
        }
    }
}