using System;
using FlaxEngine;
using FlaxEditor;

namespace HundunAgent
{
    /// <summary>
    /// HundunAgent 编辑器插件入口：
    /// 让 AI Agent 直接在 Flax 编辑器中完成游戏客户端开发工作
    /// （场景/Actor/预制体/材质/贴图/代码热重载），三种接入方式：
    /// 1. MCP 服务器   http://localhost:21901/mcp
    /// 2. HTTP REST    http://localhost:21900/api/tools/{name}
    /// 3. 编辑器内聊天窗口（菜单 Tools → HundunAgent 聊天窗口）
    /// </summary>
    public class HundunAgentPlugin : EditorPlugin
    {
        private static HundunAgentPlugin _instance;
        private bool _menuAdded;

        public static HundunAgentPlugin Instance => _instance;

        public HundunAgentPlugin()
        {
            _description = new PluginDescription
            {
                Name = "HundunAgent",
                Category = "Editor",
                Author = "HundunWorld",
                Description = "HundunAgent - 编辑器 AI Agent：MCP/HTTP/聊天窗口三种方式直接操控编辑器进行游戏开发",
                Version = new Version(1, 0),
                IsAlpha = false,
                IsBeta = false,
            };
        }

        /// <inheritdoc />
        public override void Initialize()
        {
            base.Initialize();

            if (!Engine.IsEditor)
                return;

            _instance = this;

            try
            {
                Debug.Log("[HundunAgent] 插件初始化...");

                Core.ToolRegistry.Init();
                Server.AgentHttpServer.Instance.Start();
                Server.McpServer.Instance.Start();

                Debug.Log("[HundunAgent] 初始化完成。MCP: http://localhost:" +
                          Server.McpServer.DefaultPort + "/mcp | HTTP: http://localhost:" +
                          Server.AgentHttpServer.DefaultPort + "/");
            }
            catch (Exception ex)
            {
                Debug.LogError("[HundunAgent] 初始化失败: " + ex.Message + "\n" + ex.StackTrace);
            }
        }

        /// <inheritdoc />
        public override void InitializeEditor()
        {
            base.InitializeEditor();

            // 菜单按钮（UI 可能尚未就绪，失败则在 EditorUpdate 中重试）
            TryAddMenuButton();
            if (!_menuAdded && Editor != null)
                Editor.EditorUpdate += OnEditorUpdateForMenu;
        }

        private void OnEditorUpdateForMenu()
        {
            TryAddMenuButton();
            if (_menuAdded && Editor != null)
                Editor.EditorUpdate -= OnEditorUpdateForMenu;
        }

        private void TryAddMenuButton()
        {
            if (_menuAdded)
                return;

            try
            {
                if (Editor?.UI == null)
                    return;

                Editor.UI.AddMenuButton("Tools", "HundunAgent 聊天窗口",
                    () => Chat.AgentChatWindow.ShowWindow());
                _menuAdded = true;
                Debug.Log("[HundunAgent] 菜单按钮已添加: Tools → HundunAgent 聊天窗口");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HundunAgent] 菜单按钮添加失败（可用 chat_window_open 工具打开窗口）: " + ex.Message);
            }
        }

        /// <inheritdoc />
        public override void DeinitializeEditor()
        {
            try
            {
                if (Editor != null)
                    Editor.EditorUpdate -= OnEditorUpdateForMenu;
            }
            catch { }

            base.DeinitializeEditor();
        }

        /// <inheritdoc />
        public override void Deinitialize()
        {
            try
            {
                Server.McpServer.Instance.Stop();
                Server.AgentHttpServer.Instance.Stop();
                Debug.Log("[HundunAgent] 插件已卸载");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HundunAgent] 卸载异常: " + ex.Message);
            }

            _instance = null;
            base.Deinitialize();
        }
    }
}
