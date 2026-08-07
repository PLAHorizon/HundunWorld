using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEngine;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// 任务控制工具：Agent 状态查询、Undo 检查点/回滚、执行计划审计。
    /// </summary>
    public static class TaskTools
    {
        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "agent_status",
                Description = "查询 HundunAgent 与编辑器状态：版本、端口、已加载场景、播放模式、工具数量。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}",
                Execute = AgentStatusAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "undo_checkpoint",
                Description = "建立撤销检查点。之后的 AI 变更可用 undo_rollback 一键整体回滚。开始一个新任务前建议调用。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\",\"description\":\"检查点备注\"}}}",
                Execute = UndoCheckpointAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "undo_rollback",
                Description = "执行撤销：缺省回滚自上一个检查点以来 AI 记录的全部操作；steps 指定时仅撤销 N 步。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"steps\":{\"type\":\"integer\",\"description\":\"可选，撤销步数，缺省为检查点以来的全部\"}}}",
                Execute = UndoRollbackAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "agent_plan_echo",
                Description = "上报本次任务的执行计划（仅记录到审计日志，便于追溯 AI 的操作意图）。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"plan\":{\"type\":\"string\"}},\"required\":[\"plan\"]}",
                Execute = AgentPlanEchoAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "chat_window_open",
                Description = "打开编辑器内的 HundunAgent 聊天窗口。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}",
                Execute = ChatWindowOpenAsync
            });
        }

        // ==================== Handlers ====================

        private static Task<object> AgentStatusAsync(JsonElement args)
        {
            return MainThread.InvokeAsync<object>(() =>
            {
                var scenes = Level.Scenes
                    .Where(s => s != null)
                    .Select(s => new { name = s.Name, path = s.Path })
                    .ToList();

                return new
                {
                    plugin = "HundunAgent",
                    version = "1.0",
                    engineVersion = typeof(FlaxEngine.Engine).Assembly.GetName().Version?.ToString(),
                    projectFolder = Globals.ProjectFolder,
                    isPlayMode = Engine.IsPlayMode,
                    httpPort = Server.AgentHttpServer.DefaultPort,
                    mcpPort = Server.McpServer.DefaultPort,
                    httpRunning = Server.AgentHttpServer.IsRunning,
                    mcpRunning = Server.McpServer.IsRunning,
                    toolCount = ToolRegistry.All.Count,
                    undoableActionsSinceCheckpoint = ToolRegistry.UndoableActionsSinceCheckpoint,
                    scenes
                };
            });
        }

        private static Task<object> UndoCheckpointAsync(JsonElement args)
        {
            var name = EditorUtils.GetString(args, "name", "checkpoint");

            return MainThread.InvokeAsync<object>(() =>
            {
                var before = ToolRegistry.UndoableActionsSinceCheckpoint;
                ToolRegistry.ResetCheckpointCounter();
                FlaxEngine.Debug.Log("[HundunAgent] 检查点已建立: " + name);
                return new
                {
                    status = "checkpoint",
                    name,
                    clearedPendingActions = before
                };
            });
        }

        private static async Task<object> UndoRollbackAsync(JsonElement args)
        {
            var steps = EditorUtils.GetInt(args, "steps", 0);

            int count = await MainThread.InvokeAsync(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                if (editor?.Undo == null)
                    throw new InvalidOperationException("编辑器 Undo 不可用");

                var n = steps > 0 ? steps : ToolRegistry.UndoableActionsSinceCheckpoint;
                if (n <= 0)
                    return 0;

                // 不能超过可撤销栈深度（尽力而为）
                var performed = 0;
                for (var i = 0; i < n; i++)
                {
                    if (!editor.Undo.CanUndo)
                        break;
                    editor.PerformUndo();
                    performed++;
                }

                if (steps <= 0)
                    ToolRegistry.ResetCheckpointCounter();

                return performed;
            });

            return new { status = "rolledBack", steps = count };
        }

        private static Task<object> AgentPlanEchoAsync(JsonElement args)
        {
            var plan = EditorUtils.GetString(args, "plan", "");
            FlaxEngine.Debug.Log("[HundunAgent] Agent 计划: " + plan);
            return Task.FromResult<object>(new { status = "logged", length = plan.Length });
        }

        private static Task<object> ChatWindowOpenAsync(JsonElement args)
        {
            return MainThread.InvokeAsync<object>(() =>
            {
                Chat.AgentChatWindow.ShowWindow();
                return new { status = "opened" };
            });
        }
    }
}
