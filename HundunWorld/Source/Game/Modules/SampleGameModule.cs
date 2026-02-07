using System;

namespace HundunWorld.Game.Modules
{
    /// <summary>
    /// 示例游戏模块，演示模块系统的基本用法
    /// </summary>
    public class SampleGameModule : BaseModule
    {
        public override string Name => "SampleGameModule";

        public override string Description => "示例游戏模块，演示模块系统的基本用法";

        protected override void OnInitialize()
        {
            Console.WriteLine($"{Name} 模块初始化");
        }

        protected override void OnStart()
        {
            Console.WriteLine($"{Name} 模块启动");
        }

        protected override void OnStop()
        {
            Console.WriteLine($"{Name} 模块停止");
        }

        protected override void OnUpdate(float deltaTime)
        {
            // 在这里实现模块的更新逻辑
            // Console.WriteLine($"{Name} 模块更新， deltaTime: {deltaTime}");
        }

        protected override void OnRender()
        {
            // 在这里实现模块的渲染逻辑
            // Console.WriteLine($"{Name} 模块渲染");
        }

        protected override void OnDispose()
        {
            Console.WriteLine($"{Name} 模块资源释放");
        }
    }
}