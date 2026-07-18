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

        public override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
