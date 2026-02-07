using System;

namespace HundunWorld.Game.Modules
{
    /// <summary>
    /// 基础模块类，实现IGameModule接口的基本功能
    /// </summary>
    public abstract class BaseModule : IGameModule
    {
        /// <summary>
        /// 模块名称
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// 模块版本
        /// </summary>
        public virtual string Version => "1.0.0";

        /// <summary>
        /// 模块描述
        /// </summary>
        public virtual string Description => "基础游戏模块";

        /// <summary>
        /// 是否已初始化
        /// </summary>
        protected bool IsInitialized { get; private set; }

        /// <summary>
        /// 是否已启动
        /// </summary>
        protected bool IsStarted { get; private set; }

        /// <summary>
        /// 初始化模块
        /// </summary>
        public virtual void Initialize()
        {
            if (IsInitialized)
                return;

            OnInitialize();
            IsInitialized = true;
        }

        /// <summary>
        /// 启动模块
        /// </summary>
        public virtual void Start()
        {
            if (!IsInitialized)
                throw new InvalidOperationException("模块未初始化");

            if (IsStarted)
                return;

            OnStart();
            IsStarted = true;
        }

        /// <summary>
        /// 停止模块
        /// </summary>
        public virtual void Stop()
        {
            if (!IsStarted)
                return;

            OnStop();
            IsStarted = false;
        }

        /// <summary>
        /// 更新模块逻辑
        /// </summary>
        /// <param name="deltaTime">帧间隔时间</param>
        public virtual void Update(float deltaTime)
        {
            if (!IsStarted)
                return;

            OnUpdate(deltaTime);
        }

        /// <summary>
        /// 渲染模块内容
        /// </summary>
        public virtual void Render()
        {
            if (!IsStarted)
                return;

            OnRender();
        }

        /// <summary>
        /// 子类重写此方法以实现初始化逻辑
        /// </summary>
        protected virtual void OnInitialize()
        {
        }

        /// <summary>
        /// 子类重写此方法以实现启动逻辑
        /// </summary>
        protected virtual void OnStart()
        {
        }

        /// <summary>
        /// 子类重写此方法以实现停止逻辑
        /// </summary>
        protected virtual void OnStop()
        {
        }

        /// <summary>
        /// 子类重写此方法以实现更新逻辑
        /// </summary>
        /// <param name="deltaTime">帧间隔时间</param>
        protected virtual void OnUpdate(float deltaTime)
        {
        }

        /// <summary>
        /// 子类重写此方法以实现渲染逻辑
        /// </summary>
        protected virtual void OnRender()
        {
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public virtual void Dispose()
        {
            Stop();
            OnDispose();
        }

        /// <summary>
        /// 子类重写此方法以实现资源释放逻辑
        /// </summary>
        protected virtual void OnDispose()
        {
        }
    }
}