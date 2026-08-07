using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FlaxEngine;

namespace HundunAgent.Core
{
    /// <summary>
    /// 主线程派发器：所有 Flax 编辑器 API 必须在主线程（Update 循环）执行。
    /// 工具实现从任意线程调用本类方法即可安全访问编辑器。
    /// </summary>
    public static class MainThread
    {
        public const int DefaultTimeoutMs = 60000;

        /// <summary>
        /// 在主线程执行一个有返回值的操作。
        /// </summary>
        public static Task<T> InvokeAsync<T>(Func<T> action, int timeoutMs = DefaultTimeoutMs)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            Scripting.InvokeOnUpdate(() =>
            {
                try
                {
                    tcs.SetResult(action());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            return WithTimeout(tcs.Task, timeoutMs, "操作");
        }

        /// <summary>
        /// 在主线程执行一个无返回值的操作。
        /// </summary>
        public static Task InvokeAsync(Action action, int timeoutMs = DefaultTimeoutMs)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            Scripting.InvokeOnUpdate(() =>
            {
                try
                {
                    action();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            return WithTimeout(tcs.Task, timeoutMs, "操作");
        }

        /// <summary>
        /// 每帧轮询条件直到为 true（条件在主线程评估），用于等待异步编辑器操作完成。
        /// </summary>
        public static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = DefaultTimeoutMs)
        {
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);

            while (DateTime.Now < deadline)
            {
                var result = await InvokeAsync(() =>
                {
                    try
                    {
                        return condition();
                    }
                    catch
                    {
                        return false;
                    }
                }, Math.Max(1000, (int)(deadline - DateTime.Now).TotalMilliseconds));

                if (result)
                    return true;

                await Task.Delay(50);
            }

            return false;
        }

        private static async Task<T> WithTimeout<T>(Task<T> task, int timeoutMs, string what)
        {
            var completed = await Task.WhenAny(task, Task.Delay(timeoutMs));
            if (completed != task)
                throw new TimeoutException(what + " 超时（" + timeoutMs + "ms），编辑器可能正忙于模态操作或处于断点状态");
            return await task;
        }

        private static async Task WithTimeout(Task task, int timeoutMs, string what)
        {
            var completed = await Task.WhenAny(task, Task.Delay(timeoutMs));
            if (completed != task)
                throw new TimeoutException(what + " 超时（" + timeoutMs + "ms），编辑器可能正忙于模态操作或处于断点状态");
            await task;
        }
    }
}
