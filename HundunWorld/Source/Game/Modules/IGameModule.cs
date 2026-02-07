using System;

namespace HundunWorld.Game.Modules
{
    /// <summary>
    /// 游戏模块接口，定义模块的基本功能
    /// </summary>
    public interface IGameModule : IDisposable
    {
        /// <summary>
        /// 模块名称
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 模块版本
        /// </summary>
        string Version { get; }

        /// <summary>
        /// 模块描述
        /// </summary>
        string Description { get; }

        /// <summary>
        /// 初始化模块
        /// </summary>
        void Initialize();

        /// <summary>
        /// 启动模块
        /// </summary>
        void Start();

        /// <summary>
        /// 停止模块
        /// </summary>
        void Stop();

        /// <summary>
        /// 更新模块逻辑
        /// </summary>
        /// <param name="deltaTime">帧间隔时间</param>
        void Update(float deltaTime);

        /// <summary>
        /// 渲染模块内容
        /// </summary>
        void Render();
    }
}