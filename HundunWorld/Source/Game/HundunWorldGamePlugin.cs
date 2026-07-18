using FlaxEngine;

namespace HundunWorld.Game
{
    public class HundunWorldGamePlugin : Plugin
    {
        private static HundunWorldGamePlugin _instance;
        public static HundunWorldGamePlugin Instance => _instance;

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
                    var gameInstance = System.Activator.CreateInstance(gameType);
                    var startMethod = gameType.GetMethod("StartAsync",
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Instance);
                    if (startMethod != null)
                    {
                        var task = startMethod.Invoke(gameInstance, null);
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
            _instance = null;
        }
    }
}
