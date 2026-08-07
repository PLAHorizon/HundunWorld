using System;
using System.Threading.Tasks;
using FlaxEngine;

namespace HundunWorld.Game
{
    public class GameStartup : Script
    {
        public SceneReference LoginScene;

        public override void OnStart()
        {
            Debug.Log("GameStartup 初始化开始");

            // 确认 GameStartup 运行在 RootScene 上
            var currentScene = Actor?.Scene;
            if (currentScene != null)
            {
                Debug.Log($"[GameStartup] 确认运行在场景: {currentScene.Name}");
            }
            else
            {
                Debug.LogWarning("[GameStartup] 无法确认所在场景，GameStartup 应挂载在 RootScene 上");
            }

            if (HundunWorldGamePlugin.Instance == null)
            {
                // 修复：Flax 不会自动发现并实例化游戏主程序集中的 Plugin 类，
                // 必须在 .flaxproj 的 GamePlugins 字段中注册或在此手动创建。
                // 这里采用手动创建作为兜底，确保 ECS 系统能被初始化。
                Debug.LogWarning("HundunWorldGamePlugin 尚未由引擎初始化，将手动创建 Plugin 实例以初始化游戏。");
                try
                {
                    var plugin = new HundunWorldGamePlugin();
                    plugin.Initialize();
                    Debug.Log("[GameStartup] 已手动初始化 HundunWorldGamePlugin");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[GameStartup] 手动初始化 HundunWorldGamePlugin 失败: {ex.Message}\n{ex.StackTrace}");
                }
            }

            _ = LoadScenesSafe();

            Debug.Log("GameStartup 初始化完成");
        }

        private async Task LoadScenesSafe()
        {
            try
            {
                await LoadScenes();
            }
            catch (Exception ex)
            {
                Debug.LogError($"场景加载异常: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private async Task LoadScenes()
        {
            bool loaded = false;

            if (LoginScene != null && LoginScene.ID != Guid.Empty)
            {
                loaded = Level.LoadSceneAsync(LoginScene);
            }
            else
            {
                Guid sceneId = Guid.Parse("dc72848f4b0f90ee2df186a728f14858");
                loaded = Level.LoadSceneAsync(sceneId);
            }

            if (!loaded)
            {
                Debug.LogError("场景加载失败！请检查 LoginScene 是否已赋值，或 GUID 是否正确及资源是否存在。");
            }
            else
            {
                Debug.Log("场景加载请求已提交");
            }

            await Task.CompletedTask;
        }

        public override void OnDisable()
        {
            // 修复 BUG：PIE 退出后网络任务（心跳/网关探查/监控循环）仍在后台运行。
            // 根因：PIE 退出时编辑器场景中的脚本对象不会被销毁，OnDestroy 不被调用，
            // 仅 OnDisable 必然触发（Play 模拟停止 = 脚本被禁用）。
            // 此前清理依赖 OnDestroy（仅引擎关闭/场景卸载时触发），导致 PIE 退出后
            // HundunWorldGame 及其 NetworkManager 后台循环全部残留。
            // 此处以 OnDisable 作为 PIE 退出兜底清理路径，与 OnDestroy 双保险（Dispose 幂等）。
            TryDisposeGame();
            base.OnDisable();
        }

        public override void OnDestroy()
        {
            // 修复 BUG：PIE 退出后 TCP 连接未断开，仍持续接收 SyncPacket。
            // 根因：HundunWorldGame 不继承 Script，Flax 不会在 PIE 退出时自动调用其 Dispose。
            // HundunWorldGamePlugin.Deinitialize() 中的释放逻辑仅在引擎关闭时触发，
            // PIE 退出不触发 Plugin.Deinitialize()（Plugin 是全局的，PIE 只停止场景模拟）。
            // GameStartup 是场景 Script，此处显式释放（OnDisable/OnDestroy 双路径兜底）。
            // HundunWorldGame.Dispose() 会级联释放 NetworkManager
            //（关闭 TCP 连接 + 后台接收线程）、ArchWorldHost、ECSManager 等全部子系统。
            // 安全性：Dispose() 的 finally 块会置 _instance = null，即使 Plugin.Deinitialize()
            // 后续（引擎关闭时）再调用也是空操作，不会重复释放。
            TryDisposeGame();
            base.OnDestroy();
        }

        /// <summary>
        /// 安全释放游戏实例：使用 HundunWorldGame.TryDisposeIfCreated()，
        /// 避免通过 Instance getter 触发延迟创建（PIE 退出时 _instance 可能已被置空，
        /// 访问 Instance 会 new 一个新游戏实例并重新启动网络，导致"清理反而复活残留任务"）。
        /// </summary>
        private void TryDisposeGame()
        {
            try
            {
                HundunWorldGame.TryDisposeIfCreated();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameStartup] 清理异常: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
