using FlaxEngine;

namespace HundunWorld.Server
{
    /// <summary>
    /// FlaxEngine <c>GamePlugin</c> —— <b>无头 Dedicated Server 的唯一承载者</b>。
    ///
    /// <para><b>为什么必须是 <c>GamePlugin</c> 而不是 <c>Script</c>，也不是裸 <c>Plugin</c></b>：</para>
    ///
    /// <para>引擎源码给出三条不可辩驳的事实（<c>C:\Works\FlaxEngine\Source\Engine\Scripting\Plugins\</c>）：</para>
    /// <list type="bullet">
    ///   <item><c>Script.h</c> → <c>class Script : public SceneObject</c>，带 <c>GetActor()</c>/<c>SetActor()</c>。
    ///   <c>Script</c> 的生命周期（<c>OnStart</c>/<c>OnUpdate</c>/<c>OnDestroy</c>）由 <b>场景里的 Actor</b> 驱动。</item>
    ///   <item><c>GamePlugin.h</c> → <c>class GamePlugin : public Plugin</c>，注释原文：
    ///   <i>"Base class for all plugins used at runtime in game."</i></item>
    ///   <item><c>PluginManager.cpp::OnAssemblyLoaded</c> 的发现条件是
    ///   <c>mclass->IsSubClassOf(gamePluginClass)</c>，其中
    ///   <c>gamePluginClass = GamePlugin::GetStaticClass()</c> —— <b>只收 <c>GamePlugin</c> 子类</b>，
    ///   裸 <c>Plugin</c> 子类<b>不在</b>收集范围内。</item>
    /// </list>
    ///
    /// <para><c>ServerTarget</c> 的设计前提恰恰是<b>"纯托管、无场景依赖"</b>
    /// （见 <c>Source/ServerTarget.Build.cs</c> 的注释，且它<b>不引用 <c>Game</c> 模块</b>）。
    /// 因此任何 <c>Script</c> 子类在本目标下<b>永远不会被实例化</b> —— 没有场景去承载它，
    /// DS 会「静默起不来」：进程活着、日志干净、数据面端口不监听。</para>
    ///
    /// <para><b>⚠️ 一个容易踩的反直觉点</b>：仓内 <c>HundunWorld/Source/Game/HundunWorldGamePlugin.cs</c>
    /// 用的是 <c>: Plugin</c>，且在客户端上跑得通。那是因为客户端走的是 <b>Editor / PIE 路径</b> ——
    /// 该路径由托管侧 <c>PluginManager.InitializeGamePlugins()</c>（受 <c>#if USE_EDITOR</c> 包裹）
    /// 驱动，扫描规则与运行时不同。而 <c>ServerTarget</c> 是<b>独立游戏构建</b>，
    /// 走上面那条 <c>IsSubClassOf(GamePlugin)</c> 的原生发现路径。两条路径的基类要求<b>不一样</b>，
    /// 照抄 <c>: Plugin</c> 会导致 DS 侧「编译通过、运行无日志、端口不监听」。
    /// 本类因此<b>刻意</b>继承 <c>GamePlugin</c>。
    ///
    /// <para><b>为什么用反射而不是直接 <c>new DedicatedServerHost()</c></b>：
    /// 这里其实<b>可以</b>直接 new —— <c>DedicatedServerHost</c> 与本类同处
    /// <c>ServerGame</c> 模块，编译期即可见。保留反射只是为了与
    /// <c>HundunWorldGamePlugin</c> 保持同构，便于日后把 DS 宿主挪到独立程序集时零改动。
    /// 反射查不到时<b>会打印全部已加载程序集</b>，这是排查「模块没被加载」的第一手证据。</para>
    /// </summary>
    public class ServerGamePlugin : GamePlugin
    {
        private static ServerGamePlugin _instance;

        /// <summary>当前插件实例（诊断用）。</summary>
        public static ServerGamePlugin Instance => _instance;

        /// <summary>DS 宿主实例。跨反射边界持有为 <c>object</c>，由 <see cref="DedicatedServerHost"/> 强类型转发。</summary>
        private DedicatedServerHost _host;

        public ServerGamePlugin()
        {
            _instance = this;
        }

        /// <summary>
        /// 引擎启动时调用（由 <c>PluginManager</c> 触发，<b>无场景、无 Actor</b>）。
        /// <para>这里创建并启动 <see cref="DedicatedServerHost"/>。启动失败<b>必须留下日志</b>：
        /// DS 是无人值守进程，静默失败等于「进程在跑但没服务」。</para>
        /// </summary>
        public override void Initialize()
        {
            FlaxEngine.Debug.Log("[DS] ServerGamePlugin.Initialize() by engine — 开始启动无头 Dedicated Server");

            // 注册全局未处理异常捕获，防止异步任务中的异常导致 DS 进程静默退出。
            // DS 无人值守，这条链路是排障的第一手证据。
            System.AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                var ex = e.ExceptionObject as System.Exception;
                FlaxEngine.Debug.LogError($"[DS][UnhandledException] {ex?.Message}\n{ex?.StackTrace}");
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                FlaxEngine.Debug.LogError($"[DS][UnobservedTaskException] {e.Exception?.GetBaseException().Message}");
                e.SetObserved(); // 标记为已观察，防止升级为进程崩溃
            };

            try
            {
                var hostType = System.Type.GetType(
                    "HundunWorld.Server.DedicatedServerHost, ServerGame.CSharp", false);

                if (hostType == null)
                {
                    // 同模块内直接可见，理论上必能命中；落到这里说明模块没被加载 —— 打印全部程序集留证。
                    FlaxEngine.Debug.LogError(
                        "[DS] DedicatedServerHost type not found in ServerGame.CSharp. Loaded assemblies:");
                    foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            FlaxEngine.Debug.LogError($"  - {asm.GetName().Name}");
                        }
                        catch
                        {
                            // 某些程序集可能无法获取名称，忽略
                        }
                    }
                    return;
                }

                _host = (DedicatedServerHost)System.Activator.CreateInstance(hostType);

                // Start() 内部是同步的（开数据面 + 起 tick 线程），失败会抛异常 —— 由此处的 catch 留痕。
                _host.Start();

                FlaxEngine.Debug.Log(
                    $"[DS] DedicatedServerHost 已启动。DsId={_host.DsId} ListenPort={_host.ListenPort} Mode={_host.Mode}");
            }
            catch (System.Reflection.TargetInvocationException tie)
            {
                FlaxEngine.Debug.LogError(
                    $"[DS] 宿主启动失败（内部异常）: {tie.InnerException?.Message}\n{tie.InnerException?.StackTrace}");
            }
            catch (System.Exception ex)
            {
                FlaxEngine.Debug.LogError($"[DS] 宿主启动失败: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// 引擎关闭 / PIE 停止时调用。停 tick 线程 → 摘除网关注册 → 释放 runner/uplink。
        /// <para><b>必须做</b>：否则残余实例会继续向网关宣示存活，造成「幽灵 DS」
        /// （网关注册表里挂着、实际已无 tick）。</para>
        /// </summary>
        public override void Deinitialize()
        {
            FlaxEngine.Debug.Log("[DS] ServerGamePlugin.Deinitialize()");

            if (_host != null)
            {
                try
                {
                    _host.Stop();
                    FlaxEngine.Debug.Log(
                        $"[DS] DedicatedServerHost 已停止。TicksExecuted={_host.TicksExecuted}");
                }
                catch (System.Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[DS] 停止宿主时发生错误: {ex.Message}");
                }
                finally
                {
                    _host = null;
                }
            }

            _instance = null;
        }
    }
}
