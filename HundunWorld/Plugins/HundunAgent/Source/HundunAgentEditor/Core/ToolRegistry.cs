using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;

namespace HundunAgent.Core
{
    /// <summary>
    /// 描述一个可被 AI Agent 调用的编辑器工具。
    /// </summary>
    public sealed class AgentToolDescriptor
    {
        /// <summary>工具名（snake_case，全局唯一）。</summary>
        public string Name;

        /// <summary>给 AI 看的工具说明。</summary>
        public string Description;

        /// <summary>JSON Schema（原始 JSON 字符串），描述参数。</summary>
        public string InputSchemaJson;

        /// <summary>危险操作标记（聊天窗口中执行前需人工确认）。</summary>
        public bool Dangerous;

        /// <summary>
        /// 是否会产生可撤销的场景/资产变更（参与 undo 计数）。
        /// </summary>
        public bool Undoable;

        /// <summary>
        /// 执行委托。实现内部必须通过 <see cref="MainThread"/> 派发所有编辑器 API 调用。
        /// 返回的 object 将被序列化为 JSON 回传给 Agent。
        /// </summary>
        public Func<JsonElement, Task<object>> Execute;
    }

    /// <summary>
    /// 工具注册表：全部编辑器工具的统一入口。
    /// HTTP / MCP / 编辑器聊天窗口三种通道都通过这里执行工具。
    /// </summary>
    public static class ToolRegistry
    {
        private static readonly Dictionary<string, AgentToolDescriptor> _tools =
            new Dictionary<string, AgentToolDescriptor>(StringComparer.OrdinalIgnoreCase);

        private static int _undoableActionsSinceCheckpoint;

        public static bool IsInitialized { get; private set; }

        public static IReadOnlyCollection<AgentToolDescriptor> All => _tools.Values;

        /// <summary>自上一个检查点以来记录的 undoable 操作数（用于整体回滚）。</summary>
        public static int UndoableActionsSinceCheckpoint => _undoableActionsSinceCheckpoint;

        public static void ResetCheckpointCounter()
        {
            _undoableActionsSinceCheckpoint = 0;
        }

        public static void OnUndoableAction()
        {
            _undoableActionsSinceCheckpoint++;
        }

        public static void Register(AgentToolDescriptor tool)
        {
            if (tool == null || string.IsNullOrEmpty(tool.Name))
                return;
            _tools[tool.Name] = tool;
        }

        public static bool TryGet(string name, out AgentToolDescriptor tool)
        {
            return _tools.TryGetValue(name, out tool);
        }

        /// <summary>初始化并注册全部工具（幂等）。</summary>
        public static void Init()
        {
            if (IsInitialized)
                return;

            Tools.SceneActorTools.Register();
            Tools.MaterialAssetTools.Register();
            Tools.ViewportEnvTools.Register();
            Tools.CodeTools.Register();
            Tools.TaskTools.Register();

            IsInitialized = true;
            FlaxEngine.Debug.Log("[HundunAgent] 工具注册完成，共 " + _tools.Count + " 个工具");
        }

        /// <summary>
        /// 执行工具（任意线程调用均可）。返回 { success, data | error } 结构。
        /// </summary>
        public static async Task<Dictionary<string, object>> ExecuteAsync(string toolName, JsonElement arguments)
        {
            var started = DateTime.Now;

            if (!TryGet(toolName, out var tool))
            {
                AgentAuditLog.Write(toolName, arguments, false, "工具不存在", 0);
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "error", "unknown tool: " + toolName }
                };
            }

            try
            {
                var result = await tool.Execute(arguments);
                var ms = (DateTime.Now - started).TotalMilliseconds;
                AgentAuditLog.Write(toolName, arguments, true, null, ms);
                return new Dictionary<string, object>
                {
                    { "success", true },
                    { "data", result },
                    { "elapsedMs", Math.Round(ms, 1) }
                };
            }
            catch (Exception ex)
            {
                var ms = (DateTime.Now - started).TotalMilliseconds;
                AgentAuditLog.Write(toolName, arguments, false, ex.Message, ms);
                FlaxEngine.Debug.LogError("[HundunAgent] 工具 " + toolName + " 执行失败: " + ex.Message);
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "error", ex.Message }
                };
            }
        }
    }
}
