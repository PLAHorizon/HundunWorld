#if FLAX_EDITOR
using System;
using FlaxEngine;
using FlaxEditor;

namespace HundunWorld.TraeBridge
{
    public class TraeBridgeEditorPlugin : EditorPlugin
    {
        private static TraeBridgeEditorPlugin _instance;
        private static bool _started = false;

        public static TraeBridgeEditorPlugin Instance => _instance;

        public TraeBridgeEditorPlugin()
        {
            _description = new PluginDescription
            {
                Name = "TraeBridge",
                Category = "Editor",
                Author = "成阳",
                Description = "TraeBridge Editor 插件 - 提供 HTTP API 供外部工具与 Flax Editor 交互",
                Version = new Version(1, 0),
                IsAlpha = false,
                IsBeta = false,
            };
        }

        public override void Initialize()
        {
            _instance = this;

            Debug.Log("[TraeBridge] Editor 插件初始化");

            if (!_started)
            {
                try
                {
                    Debug.Log("[TraeBridge] 正在启动 HTTP 服务器...");
                    TraeBridgeServer.Instance.Start();
                    _started = true;
                    Debug.Log("[TraeBridge] HTTP 服务器已启动: http://localhost:21888/");
                }
                catch (Exception ex)
                {
                    Debug.LogError("[TraeBridge] 启动 HTTP 服务器失败: " + ex.Message + "\n" + ex.StackTrace);
                }
            }

            base.Initialize();
        }

        public override void Deinitialize()
        {
            try
            {
                TraeBridgeServer.Instance.Stop();
                _started = false;
                Debug.Log("[TraeBridge] HTTP 服务器已停止");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TraeBridge] 停止 HTTP 服务器异常: " + ex.Message);
            }

            _instance = null;
            base.Deinitialize();
        }
    }
}
#endif
