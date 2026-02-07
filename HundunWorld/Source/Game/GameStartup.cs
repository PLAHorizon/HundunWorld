using System;
using System.Threading.Tasks;
using FlaxEngine;

namespace HundunWorld.Game
{
    /// <summary>
    /// 游戏启动脚本，用于确保插件正确初始化
    /// </summary>
    public class GameStartup : Script
    {
        /// <summary>
        /// 启动时要加载的场景，建议在编辑器属性面板中进行赋值以避免 ID 错误
        /// </summary>
        public SceneReference LoginScene;

        public override  void OnStart()
        {
            Debug.Log("GameStartup 初始化开始");

            // 确保游戏插件已初始化
            HundunWorldGamePlugin.Init();
            Task.Factory.StartNew(async () =>await  LoadScenes());

            Debug.Log("GameStartup 初始化完成");
        }

        private async Task LoadScenes()
        {
            // 优先加载编辑器中配置的场景，若无则使用硬编码 ID，并添加错误检查
            bool loaded = false;
            
            if (LoginScene != null)
            {
                  Level.LoadSceneAsync(LoginScene);
            }
            else
            {
                // Fallback 到硬编码 ID
                Guid sceneId = Guid.Parse("dc72848f4b0f90ee2df186a728f14858");
                loaded = Level.LoadSceneAsync(sceneId);
            }

            if (!loaded)
            {
                Debug.LogError("场景加载失败！请检查 LoginScene 是否已赋值，或 GUID 是否正确及资源是否存在。");
            }
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            HundunWorldGamePlugin.Instance.Deinitialize();
        }
    }
}
