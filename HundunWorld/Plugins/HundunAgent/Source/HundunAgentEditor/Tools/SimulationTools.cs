using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEngine;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// 运行验证工具：PIE 播放控制（Play Scenes / Play Game、暂停、单帧步进）、
    /// 运行状态查询、游戏视口截图。与 viewport_screenshot 配合形成
    /// 「编辑态截图 → 修改 → 运行态截图」的视觉反馈循环。
    /// </summary>
    public static class SimulationTools
    {
        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "sim_play",
                Description = "进入播放模式（PIE）。mode=scenes（默认，播放当前编辑场景）|game（播放游戏起始场景）。等待进入播放后返回。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"mode\":{\"type\":\"string\",\"description\":\"scenes|game，默认scenes\"},\"timeoutMs\":{\"type\":\"integer\",\"description\":\"等待超时毫秒，默认30000\"}}}",
                Execute = SimPlayAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "sim_stop",
                Description = "停止播放模式并等待编辑器回到编辑态。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"timeoutMs\":{\"type\":\"integer\",\"description\":\"等待超时毫秒，默认30000\"}}}",
                Execute = SimStopAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "sim_pause_resume",
                Description = "切换播放暂停/恢复。仅在播放模式下有效。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}",
                Execute = SimPauseResumeAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "sim_step_frame",
                Description = "单帧步进（需处于暂停态）。配合 sim_pause_resume 逐帧观察。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}",
                Execute = SimStepFrameAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "sim_status",
                Description = "查询运行状态：是否播放中、播放请求态、断点挂起、暂停态与已加载场景。只读。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}",
                Execute = SimStatusAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "game_screenshot",
                Description = "捕获游戏视口截图（PIE 运行态）并保存为 PNG，返回文件路径。建议在 sim_play 之后调用；失败时回退到整屏截图。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"可选保存路径，缺省自动保存到 Logs/HundunAgent/screenshots\"}}}",
                Execute = GameScreenshotAsync
            });
        }

        // ==================== Handlers ====================

        private static async Task<object> SimPlayAsync(JsonElement args)
        {
            var mode = (EditorUtils.GetString(args, "mode", "scenes") ?? "scenes").ToLowerInvariant();
            if (mode != "scenes" && mode != "game")
                throw new ArgumentException("mode 必须是 scenes 或 game");
            var timeoutMs = EditorUtils.GetInt(args, "timeoutMs", 30000);

            await MainThread.InvokeAsync(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                if (editor?.Simulation == null)
                    throw new InvalidOperationException("编辑器 Simulation 模块不可用");
                if (Engine.IsPlayMode)
                    return;

                if (mode == "game")
                    editor.Simulation.RequestStartPlayGame();
                else
                    editor.Simulation.RequestStartPlayScenes();
            });

            var ok = await MainThread.WaitUntilAsync(() => Engine.IsPlayMode, timeoutMs);
            if (!ok)
                throw new TimeoutException("进入播放模式超时（" + timeoutMs + "ms）");

            return new
            {
                status = "playing",
                mode,
                isPlayMode = true,
                hint = "可用 game_screenshot 观察运行效果，ui_tree 查看运行时 UI 树"
            };
        }

        private static async Task<object> SimStopAsync(JsonElement args)
        {
            var timeoutMs = EditorUtils.GetInt(args, "timeoutMs", 30000);

            await MainThread.InvokeAsync(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                if (editor?.Simulation == null)
                    throw new InvalidOperationException("编辑器 Simulation 模块不可用");
                if (!Engine.IsPlayMode)
                    return;
                editor.Simulation.RequestStopPlay();
            });

            var ok = await MainThread.WaitUntilAsync(() => !Engine.IsPlayMode, timeoutMs);
            if (!ok)
                throw new TimeoutException("退出播放模式超时（" + timeoutMs + "ms）");

            return new { status = "stopped", isPlayMode = false };
        }

        private static async Task<object> SimPauseResumeAsync(JsonElement args)
        {
            var result = await MainThread.InvokeAsync<object>(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                if (!Engine.IsPlayMode)
                    throw new InvalidOperationException("当前不在播放模式，请先 sim_play");
                editor.Simulation.RequestResumeOrPause();
                return new { requested = true };
            });

            // 暂停状态切换需要一个更新周期生效，稍等后回读
            await Task.Delay(300);

            return await MainThread.InvokeAsync<object>(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                return new
                {
                    status = "toggled",
                    isPlayMode = Engine.IsPlayMode,
                    isPaused = ReadIsPaused(editor)
                };
            });
        }

        private static Task<object> SimStepFrameAsync(JsonElement args)
        {
            return MainThread.InvokeAsync<object>(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                if (!Engine.IsPlayMode)
                    throw new InvalidOperationException("当前不在播放模式，请先 sim_play");
                editor.Simulation.RequestPlayOneFrame();
                return new
                {
                    status = "stepped",
                    isPaused = ReadIsPaused(editor),
                    hint = "单帧步进在暂停态生效；可用 game_screenshot 观察每帧结果"
                };
            });
        }

        private static Task<object> SimStatusAsync(JsonElement args)
        {
            return MainThread.InvokeAsync<object>(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                var sim = editor?.Simulation;
                var scenes = Level.Scenes
                    .Where(s => s != null)
                    .Select(s => new { name = s.Name, path = s.Path })
                    .ToList();

                return new
                {
                    isPlayMode = Engine.IsPlayMode,
                    isPlayModeRequested = sim != null && sim.IsPlayModeRequested,
                    isDuringBreakpointHang = sim != null && sim.IsDuringBreakpointHang,
                    isPaused = ReadIsPaused(editor),
                    scenes
                };
            });
        }

        private static async Task<object> GameScreenshotAsync(JsonElement args)
        {
            var customPath = EditorUtils.GetString(args, "path");

            var targetPath = await MainThread.InvokeAsync(() =>
            {
                string path;
                if (!string.IsNullOrEmpty(customPath))
                {
                    path = Path.IsPathRooted(customPath)
                        ? customPath
                        : Path.Combine(Globals.ProjectFolder, customPath);
                }
                else
                {
                    var dir = Path.Combine(Globals.ProjectFolder, "Logs", "HundunAgent", "screenshots");
                    Directory.CreateDirectory(dir);
                    path = Path.Combine(dir, "game_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
                }

                var dirName = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dirName) && !Directory.Exists(dirName))
                    Directory.CreateDirectory(dirName);

                var editor = FlaxEditor.Editor.Instance;
                var gameViewport = editor?.Windows?.GameWin?.Viewport;

                // GameWin.Viewport 静态类型为 EditorViewport，TakeScreenshot(string) 是否为基类方法未确认，
                // 故用反射调用（与 viewport_screenshot 对 EditWin 的直接调用互补），失败时回退整屏截图
                var shotTaken = false;
                if (gameViewport != null)
                {
                    try
                    {
                        var method = gameViewport.GetType().GetMethod("TakeScreenshot", new[] { typeof(string) });
                        if (method != null)
                        {
                            method.Invoke(gameViewport, new object[] { path });
                            shotTaken = true;
                        }
                    }
                    catch
                    {
                        shotTaken = false;
                    }
                }

                if (!shotTaken)
                    Screenshot.Capture(path);

                return path;
            });

            // 截图写入发生在下一渲染帧，等待文件出现
            var ok = await MainThread.WaitUntilAsync(() => File.Exists(targetPath), 8000);
            if (!ok)
                throw new TimeoutException("截图文件未生成: " + targetPath);

            return new
            {
                status = "captured",
                path = targetPath,
                size = new FileInfo(targetPath).Length,
                isPlayMode = Engine.IsPlayMode
            };
        }

        // ==================== 内部辅助 ====================

        /// <summary>读取暂停态（编辑器 UI 模块的暂停按钮勾选态，反射容错）。</summary>
        private static bool? ReadIsPaused(FlaxEditor.Editor editor)
        {
            try
            {
                if (editor?.UI == null)
                    return null;
                var prop = editor.UI.GetType().GetProperty("IsPauseButtonChecked");
                if (prop == null || prop.PropertyType != typeof(bool))
                    return null;
                return (bool)prop.GetValue(editor.UI);
            }
            catch
            {
                return null;
            }
        }
    }
}
