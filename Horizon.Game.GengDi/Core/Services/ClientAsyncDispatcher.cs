using System;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Game.GengDi.Core.Services;

internal static class ClientAsyncDispatcher
{
    private static readonly SemaphoreSlim LiteDbGate = new(1, 1);
    private static readonly SemaphoreSlim ConfigGate = new(1, 1);

    public static Task RunLiteDbAsync(Action action)
    {
        return RunSerializedAsync(LiteDbGate, action);
    }

    public static Task<T> RunLiteDbAsync<T>(Func<T> action)
    {
        return RunSerializedAsync(LiteDbGate, action);
    }

    public static Task RunConfigAsync(Action action)
    {
        return RunSerializedAsync(ConfigGate, action);
    }

    public static Task<T> RunConfigAsync<T>(Func<T> action)
    {
        return RunSerializedAsync(ConfigGate, action);
    }

    public static Task RunBackgroundAsync(Action action)
    {
        return Task.Run(action);
    }

    public static Task<T> RunBackgroundAsync<T>(Func<T> action)
    {
        return Task.Run(action);
    }

    private static async Task RunSerializedAsync(SemaphoreSlim gate, Action action)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(action).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<T> RunSerializedAsync<T>(SemaphoreSlim gate, Func<T> action)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(action).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }
}