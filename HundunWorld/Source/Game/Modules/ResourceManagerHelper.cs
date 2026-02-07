
using FlaxEngine;
using System;
using System.Threading.Tasks;

namespace HundunWorld.Game.Modules
{
    /// <summary>
    /// 资源管理辅助类
    /// 提供资源释放和IDisposable接口实现的辅助方法
    /// </summary>
    public static class ResourceManagerHelper
    {
        /// <summary>
        /// 安全释放资源
        /// </summary>
        /// <param name="disposable">可释放资源</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="resourceName">资源名称</param>
        public static void SafeDispose(this IDisposable disposable, ILogger logger = null, string resourceName = "Resource")
        {
            if (disposable != null)
            {
                try
                {
                    disposable.Dispose();
                    FlaxEngine.Debug.Log($"成功释放资源: {resourceName}");
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError( $"释放资源失败: {resourceName},{ex.Message}");
                }
            }
        }

        /// <summary>
        /// 异步安全释放资源
        /// </summary>
        /// <param name="disposable">可释放资源</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="resourceName">资源名称</param>
        public static async Task SafeDisposeAsync(this IAsyncDisposable disposable, ILogger logger = null, string resourceName = "Resource")
        {
            if (disposable != null)
            {
                try
                {
                    await disposable.DisposeAsync();
                    FlaxEngine.Debug.Log($"成功释放异步资源: {resourceName}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"释放异步资源失败: {resourceName},{ex.Message}");
                }
            }
        }

        /// <summary>
        /// 检查对象是否已释放
        /// </summary>
        /// <param name="isDisposed">是否已释放的标志</param>
        /// <param name="objectName">对象名称</param>
        /// <exception cref="ObjectDisposedException">如果对象已释放，则抛出异常</exception>
        public static void ThrowIfDisposed(bool isDisposed, string objectName)
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException(objectName);
            }
        }

        /// <summary>
        /// 创建标准的IDisposable实现模板
        /// </summary>
        /// <typeparam name="T">实现类类型</typeparam>
        public abstract class DisposableBase<T> : IDisposable where T : class
        {
            private volatile bool _disposed = false;

            /// <summary>
            /// 是否已释放
            /// </summary>
            public bool IsDisposed => _disposed;

            /// <summary>
            /// 释放资源
            /// </summary>
            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            /// <summary>
            /// 释放资源
            /// </summary>
            /// <param name="disposing">是否从Dispose方法调用</param>
            protected virtual void Dispose(bool disposing)
            {
                if (!_disposed)
                {
                    if (disposing)
                    {
                        // 释放托管资源
                        DisposeManagedResources();
                    }

                    // 释放非托管资源
                    DisposeUnmanagedResources();

                    _disposed = true;
                }
            }

            /// <summary>
            /// 释放托管资源
            /// 子类应该重写此方法来释放托管资源
            /// </summary>
            protected virtual void DisposeManagedResources() { }

            /// <summary>
            /// 释放非托管资源
            /// 子类应该重写此方法来释放非托管资源
            /// </summary>
            protected virtual void DisposeUnmanagedResources() { }

            /// <summary>
            /// 检查对象是否已释放
            /// </summary>
            protected void ThrowIfDisposed()
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(typeof(T).Name);
                }
            }

            /// <summary>
            /// 析构函数
            /// </summary>
            ~DisposableBase()
            {
                Dispose(false);
            }
        }

        /// <summary>
        /// 创建标准的IAsyncDisposable实现模板
        /// </summary>
        /// <typeparam name="T">实现类类型</typeparam>
        public abstract class AsyncDisposableBase<T> : IAsyncDisposable where T : class
        {
            private volatile bool _disposed = false;

            /// <summary>
            /// 是否已释放
            /// </summary>
            public bool IsDisposed => _disposed;

            /// <summary>
            /// 异步释放资源
            /// </summary>
            public async ValueTask DisposeAsync()
            {
                await DisposeAsyncCore();
                Dispose(false);
                GC.SuppressFinalize(this);
            }

            /// <summary>
            /// 核心异步释放逻辑
            /// </summary>
            protected virtual async ValueTask DisposeAsyncCore()
            {
                if (!_disposed)
                {
                    // 异步释放托管资源
                    await DisposeManagedResourcesAsync();
                }
            }

            /// <summary>
            /// 释放资源
            /// </summary>
            /// <param name="disposing">是否从Dispose方法调用</param>
            protected virtual void Dispose(bool disposing)
            {
                if (!_disposed)
                {
                    if (disposing)
                    {
                        // 释放同步托管资源
                        DisposeManagedResources();
                    }

                    // 释放非托管资源
                    DisposeUnmanagedResources();

                    _disposed = true;
                }
            }

            /// <summary>
            /// 异步释放托管资源
            /// </summary>
            protected virtual ValueTask DisposeManagedResourcesAsync()
            {
                return ValueTask.CompletedTask;
            }

            /// <summary>
            /// 释放托管资源
            /// </summary>
            protected virtual void DisposeManagedResources() { }

            /// <summary>
            /// 释放非托管资源
            /// </summary>
            protected virtual void DisposeUnmanagedResources() { }

            /// <summary>
            /// 检查对象是否已释放
            /// </summary>
            protected void ThrowIfDisposed()
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(typeof(T).Name);
                }
            }
        }
    }
}