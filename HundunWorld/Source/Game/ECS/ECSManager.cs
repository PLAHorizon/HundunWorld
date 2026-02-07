using Arch.Core;
using Arch.Core.Utils;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.ECS
{
    /// <summary>
    /// ECS管理器，负责管理游戏世界中的实体、组件和系统
    /// </summary>
    public class ECSManager
    {
        private Arch.Core.World _world;
        private List<BaseSystem> _systems;
        private bool _isRunning;

        public ECSManager()
        {
            // 创建ECS世界
            _world = World.Create();
            _systems = new List<BaseSystem>();
        }

        /// <summary>
        /// 初始化ECS系统
        /// </summary>
        public void Initialize()
        {
            // 这里可以注册组件类型
            // ComponentRegistry.Register<MyComponent>();
        }

        /// <summary>
        /// 添加系统到ECS管理器
        /// </summary>
        /// <param name="system">系统实例</param>
        public void AddSystem(BaseSystem system)
        {
            if (system == null)
                throw new ArgumentNullException(nameof(system));

            _systems.Add(system);
            system.Initialize(_world);
        }

        /// <summary>
        /// 创建实体
        /// </summary>
        /// <returns>实体引用</returns>
        public Entity CreateEntity()
        {
            return _world.Create();
        }

        /// <summary>
        /// 删除实体
        /// </summary>
        /// <param name="entity">实体引用</param>
        public void DestroyEntity(Entity entity)
        {
            _world.Destroy(entity);
        }

        /// <summary>
        /// 启动ECS系统更新循环
        /// </summary>
        public void Start()
        {
            _isRunning = true;
        }

        /// <summary>
        /// 停止ECS系统更新循环
        /// </summary>
        public void Stop()
        {
            _isRunning = false;
        }

        /// <summary>
        /// 更新所有系统
        /// </summary>
        /// <param name="deltaTime">帧间隔时间</param>
        public void Update(float deltaTime)
        {
            if (!_isRunning)
                return;

            // 按顺序更新所有系统
            foreach (var system in _systems)
            {
                system.Update(_world, deltaTime);
            }
        }

        /// <summary>
        /// 渲染所有系统
        /// </summary>
        public void Render()
        {
            if (!_isRunning)
                return;

            // 按顺序渲染所有系统
            foreach (var system in _systems)
            {
                system.Render(_world);
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Stop();

            // 销毁所有系统
            foreach (var system in _systems)
            {
                system.Dispose();
            }
            _systems.Clear();

            // 销毁世界
            World.Destroy(_world);
        }
    }

    /// <summary>
    /// 基础系统类
    /// </summary>
    public abstract class BaseSystem : IDisposable
    {
        /// <summary>
        /// 系统初始化
        /// </summary>
        /// <param name="world">ECS世界</param>
        public virtual void Initialize(World world)
        {
        }

        /// <summary>
        /// 系统更新
        /// </summary>
        /// <param name="world">ECS世界</param>
        /// <param name="deltaTime">帧间隔时间</param>
        public abstract void Update(World world, float deltaTime);

        /// <summary>
        /// 系统渲染
        /// </summary>
        /// <param name="world">ECS世界</param>
        public virtual void Render(World world)
        {
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public virtual void Dispose()
        {
        }
    }
}