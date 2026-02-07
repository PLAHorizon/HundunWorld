using Arch.Core;
using Arch.Core.Utils;
using FlaxEngine;
using HundunWorld.Game.ECS.Components;

namespace HundunWorld.Game.ECS.Systems
{
    /// <summary>
    /// 输入系统，负责处理玩家输入并更新输入组件
    /// </summary>
    public class InputSystem : BaseSystem
    {
        private QueryDescription _queryDescription;

        public override void Initialize(World world)
        {
            base.Initialize(world);
            
            // 定义查询描述，查找具有输入组件的实体
            _queryDescription = new QueryDescription().WithAll<InputComponent>();
        }

        public override void Update(World world, float deltaTime)
        {
            // 查询所有具有输入组件的实体
            world.Query(in _queryDescription, (Entity entity, ref InputComponent input) =>
            {
                // 获取轴输入
                input.Horizontal = Input.GetAxis("Horizontal");
                input.Vertical = Input.GetAxis("Vertical");
                
                // 获取鼠标输入
                input.MouseX = Input.GetAxis("Mouse X");
                input.MouseY = Input.GetAxis("Mouse Y");
                input.MouseWheel = Input.GetAxis("Mouse ScrollWheel");
                
                // 获取按键输入
                input.Fire1 = Input.GetMouseButton(MouseButton.Left);
                input.Fire2 = Input.GetMouseButton(MouseButton.Right);
                input.Jump = Input.GetKey(KeyboardKeys.Spacebar);
                
                // 获取鼠标屏幕位置
                input.MouseScreenPosition = Input.MouseScreenPosition;
                
                // 更新组件
                world.Set(entity, input);
            });
        }
    }
}