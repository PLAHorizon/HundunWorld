using FlaxEngine;

namespace HundunWorld.Game
{
public class HundunWorldGamePlugin : Plugin
{
    private static HundunWorldGamePlugin _instance;
    public static HundunWorldGamePlugin Instance => _instance;

    // 保存 HundunWorldGame 实例，以便在 Deinitialize 时正确释放资源
    private object _gameInstance;

    public HundunWorldGamePlugin()
    {
        _instance = this;
    }

    public override void Initialize()
    {
        Debug.Log("HundunWorldGamePlugin.Initialize() by engine");

        // 注册全局未处理异常捕获，防止异步任务中的异常导致进程崩溃
        System.AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            var ex = e.ExceptionObject as System.Exception;
            Debug.LogError($"[UnhandledException] {ex?.Message}\n{ex?.StackTrace}");
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            Debug.LogError($"[UnobservedTaskException] {e.Exception?.GetBaseException().Message}");
            e.SetObserved(); // 标记为已观察，防止升级为进程崩溃
        };

        try
        {
            var gameType = System.Type.GetType(
                "HundunWorld.Game.HundunWorldGame, Game.CSharp", false);
            if (gameType != null)
            {
                _gameInstance = System.Activator.CreateInstance(gameType);
                var startMethod = gameType.GetMethod("StartAsync",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Instance);
                if (startMethod != null)
                {
                    var task = startMethod.Invoke(_gameInstance, null);
                    if (task is System.Threading.Tasks.Task t)
                    {
                        _ = t.ContinueWith(x =>
                        {
                            if (x.IsFaulted)
                                Debug.LogError($"Game start failed: {x.Exception?.GetBaseException().Message}");
                        });
                    }
                }
            }
            else
            {
                Debug.LogError("HundunWorldGame type not found. Loaded assemblies:");
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        Debug.LogError($"  - {asm.GetName().Name}");
                    }
                    catch
                    {
                        // 某些程序集可能无法获取名称
                    }
                }
            }
        }
        catch (System.Reflection.TargetInvocationException tie)
        {
            // Activator.CreateInstance 包装的内部异常
            Debug.LogError($"Plugin init failed (inner): {tie.InnerException?.Message}\n{tie.InnerException?.StackTrace}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Plugin init failed: {ex.Message}");
        }
    }

    public override void Deinitialize()
    {
        Debug.Log("HundunWorldGamePlugin.Deinitialize()");

        // [修复] PIE 停止时正确释放 HundunWorldGame 及其内部的 NetworkManager/ReconnectionManager，
        // 防止残余实例持续发起 TCP 连接导致服务端幽灵连接风暴。
        // PIE 停止触发 Plugin.Deinitialize()，而非 Engine.RequestingExit，
        // 因此 HundunWorldGame 构造函数中订阅的 Engine.RequestingExit 不会在 PIE 停止时触发，
        // 必须在此处主动释放。
        if (_gameInstance != null)
        {
            try
            {
                var disposeMethod = _gameInstance.GetType().GetMethod("Dispose",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Instance);
                if (disposeMethod != null)
                {
                    disposeMethod.Invoke(_gameInstance, null);
                    Debug.Log("HundunWorldGamePlugin: 已释放 HundunWorldGame 实例");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"HundunWorldGamePlugin: 释放 HundunWorldGame 时发生错误: {ex.Message}");
            }
            _gameInstance = null;
        }

        _instance = null;
    }
}
}
