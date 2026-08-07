using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlaxEngine;
using FlaxEngine.GUI;
using FlaxEditor;
using FlaxEditor.Windows;
using HundunAgent.Core;

namespace HundunAgent.Chat
{
    /// <summary>
    /// 编辑器内 HundunAgent 聊天窗口：
    /// 用户输入任务 → LLM function-calling 循环 → 直接调用编辑器工具完成开发工作。
    /// </summary>
    public sealed class AgentChatWindow : EditorWindow
    {
        private static AgentChatWindow _instance;

        private const string SystemPrompt =
            "你是 HundunWorld 游戏客户端的 AI 开发助手，可以直接操控 Flax 编辑器完成游戏开发工作。\n" +
            "工作准则：\n" +
            "1. 操作场景前先调用 scene_list / scene_hierarchy 了解当前状态；需要时先用 scene_load 打开场景。\n" +
            "2. 开始一批修改前调用 undo_checkpoint 建立检查点，出错可用 undo_rollback 回滚。\n" +
            "3. 修改完成后调用 scene_save 保存，并用 viewport_screenshot 截图自查效果。\n" +
            "4. 查找资产用 asset_search；装配预制体用 prefab_spawn；材质相关用 material_* 工具。\n" +
            "5. 修改代码用 code_write + code_build_wait 确认编译通过。\n" +
            "6. 用中文简洁回复，说明每一步做了什么。";

        private TextBox _logBox;
        private TextBox _inputBox;
        private TextBox _baseUrlBox;
        private TextBox _apiKeyBox;
        private TextBox _modelBox;
        private Label _statusLabel;
        private Button _sendButton;
        private Button _stopButton;

        private readonly List<Dictionary<string, object>> _messages =
            new List<Dictionary<string, object>>();

        private CancellationTokenSource _runCts;
        private bool _isBusy;

        /// <summary>打开或聚焦聊天窗口（必须在主线程调用）。</summary>
        public static void ShowWindow()
        {
            var editor = FlaxEditor.Editor.Instance;

            if (_instance != null)
            {
                _instance.FocusOrShow(FlaxEditor.GUI.Docking.DockState.DockRight);
                return;
            }

            _instance = new AgentChatWindow(editor);
            editor.Windows.Open(_instance);
            _instance.Show(FlaxEditor.GUI.Docking.DockState.DockRight);
        }

        public AgentChatWindow(FlaxEditor.Editor editor)
            : base(editor, false, ScrollBars.None)
        {
            Title = "HundunAgent";

            BuildUi();

            SizeChanged += control => LayoutNow();
            LayoutNow();

            _messages.Add(new Dictionary<string, object>
            {
                { "role", "system" },
                { "content", SystemPrompt }
            });

            AppendLog("HundunAgent 就绪。工具数: " + ToolRegistry.All.Count +
                      "\n请先在上方配置 LLM（BaseUrl / ApiKey / Model），然后输入任务。");
        }

        private const float TopBarHeight = 54f;
        private const float BottomBarHeight = 62f;

        private Panel _topBar;
        private Panel _bottomBar;

        private void LayoutNow()
        {
            if (_topBar == null)
                return;

            var w = Width;
            var h = Height;

            _topBar.Bounds = new Rectangle(0, 0, w, TopBarHeight);
            _bottomBar.Bounds = new Rectangle(0, h - BottomBarHeight, w, BottomBarHeight);
            _logBox.Bounds = new Rectangle(0, TopBarHeight, w, Mathf.Max(20f, h - TopBarHeight - BottomBarHeight));

            _inputBox.Width = Mathf.Max(80f, _bottomBar.Width - 176f);
            _sendButton.X = _inputBox.Width + 12f;
            _stopButton.X = _inputBox.Width + 88f;
            _statusLabel.Width = Mathf.Max(60f, _topBar.Width - 590f);
        }

        private void BuildUi()
        {
            var settings = AgentSettings.Instance;

            // 顶部设置栏
            _topBar = new Panel(ScrollBars.None)
            {
                Parent = this
            };

            new Label(4, 4, 60, 18) { Text = "BaseUrl", Parent = _topBar };
            _baseUrlBox = new TextBox(false, 4, 22, 220) { Text = settings.BaseUrl, WatermarkText = "https://api.xxx.com/v1", Parent = _topBar };

            new Label(232, 4, 60, 18) { Text = "ApiKey", Parent = _topBar };
            _apiKeyBox = new TextBox(false, 232, 22, 140) { Text = settings.ApiKey, WatermarkText = "sk-...", Parent = _topBar };

            new Label(380, 4, 60, 18) { Text = "Model", Parent = _topBar };
            _modelBox = new TextBox(false, 380, 22, 130) { Text = settings.Model, WatermarkText = "gpt-4o", Parent = _topBar };

            var saveButton = new Button(518, 20, 60, 24) { Text = "保存", Parent = _topBar };
            saveButton.Clicked += OnSaveSettings;

            _statusLabel = new Label(586, 24, 180, 18) { Text = "空闲", Parent = _topBar };

            // 底部输入栏
            _bottomBar = new Panel(ScrollBars.None)
            {
                Parent = this
            };

            _inputBox = new TextBox(false, 6, 8, 380)
            {
                WatermarkText = "输入任务，例如：在场景中放一棵树并调整光照...",
                Parent = _bottomBar
            };
            _inputBox.KeyDown += OnInputKeyDown;

            _sendButton = new Button(394, 6, 70, 28) { Text = "发送", Parent = _bottomBar };
            _sendButton.Clicked += OnSendClicked;

            _stopButton = new Button(470, 6, 70, 28) { Text = "停止", Enabled = false, Parent = _bottomBar };
            _stopButton.Clicked += OnStopClicked;

            // 中间日志区
            _logBox = new TextBox(true, 0, 0, 100)
            {
                IsReadOnly = true,
                Wrapping = TextWrapping.WrapWords,
                Parent = this
            };
        }

        private void OnSaveSettings()
        {
            var settings = AgentSettings.Instance;
            settings.BaseUrl = _baseUrlBox.Text?.Trim() ?? "";
            settings.ApiKey = _apiKeyBox.Text?.Trim() ?? "";
            settings.Model = _modelBox.Text?.Trim() ?? "";
            settings.Save();
            AppendLog("设置已保存。");
        }

        private void OnInputKeyDown(KeyboardKeys key)
        {
            if (key == KeyboardKeys.Return && !_isBusy)
                StartTask();
        }

        private void OnSendClicked()
        {
            if (!_isBusy)
                StartTask();
        }

        private void OnStopClicked()
        {
            _runCts?.Cancel();
            AppendLog("正在停止...");
        }

        private void StartTask()
        {
            var input = _inputBox.Text?.Trim();
            if (string.IsNullOrEmpty(input))
                return;

            if (!AgentSettings.Instance.IsConfigured)
            {
                AppendLog("错误：请先配置 BaseUrl 和 Model 并保存。");
                return;
            }

            _inputBox.Text = "";
            _messages.Add(new Dictionary<string, object> { { "role", "user" }, { "content", input } });
            AppendLog("\n[用户] " + input);

            SetBusy(true);
            _runCts = new CancellationTokenSource();
            var token = _runCts.Token;

            Task.Run(async () =>
            {
                try
                {
                    await RunAgentLoopAsync(token);
                }
                catch (OperationCanceledException)
                {
                    await AppendLogAsync("\n[系统] 任务已停止。");
                }
                catch (Exception ex)
                {
                    await AppendLogAsync("\n[错误] " + ex.Message);
                }
                finally
                {
                    await MainThread.InvokeAsync(() => SetBusy(false));
                }
            }, token);
        }

        private async Task RunAgentLoopAsync(CancellationToken token)
        {
            var settings = AgentSettings.Instance;
            var maxSteps = Math.Max(1, settings.MaxToolSteps);

            for (var step = 0; step < maxSteps; step++)
            {
                token.ThrowIfCancellationRequested();
                await SetStatusAsync("思考中... (第 " + (step + 1) + " 轮)");

                JsonElement message;
                try
                {
                    message = await LlmClient.ChatCompletionAsync(_messages, settings, token);
                }
                catch (Exception ex)
                {
                    await AppendLogAsync("\n[错误] LLM 调用失败: " + ex.Message);
                    return;
                }

                // 收集助手消息
                string content = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                    ? c.GetString()
                    : null;

                var hasToolCalls = message.TryGetProperty("tool_calls", out var toolCalls) &&
                                   toolCalls.ValueKind == JsonValueKind.Array &&
                                   toolCalls.GetArrayLength() > 0;

                var assistantMsg = new Dictionary<string, object> { { "role", "assistant" } };
                if (content != null)
                    assistantMsg["content"] = content;

                if (!hasToolCalls)
                {
                    _messages.Add(assistantMsg);
                    await AppendLogAsync("\n[助手] " + (content ?? "(无回复)"));
                    return;
                }

                // 组装 tool_calls 存入历史
                var callList = new List<Dictionary<string, object>>();
                var callItems = new List<(string Id, string Name, string ArgsJson)>();

                foreach (var call in toolCalls.EnumerateArray())
                {
                    var id = call.TryGetProperty("id", out var idEl) ? idEl.GetString() : Guid.NewGuid().ToString();
                    var fn = call.GetProperty("function");
                    var name = fn.GetProperty("name").GetString();
                    var argsJson = fn.TryGetProperty("arguments", out var a) ? a.GetString() : "{}";

                    callList.Add(new Dictionary<string, object>
                    {
                        { "id", id },
                        { "type", "function" },
                        {
                            "function", new Dictionary<string, object>
                            {
                                { "name", name },
                                { "arguments", argsJson }
                            }
                        }
                    });
                    callItems.Add((id, name, argsJson));
                }

                assistantMsg["tool_calls"] = callList;
                _messages.Add(assistantMsg);

                // 逐个执行工具
                foreach (var (id, name, argsJson) in callItems)
                {
                    token.ThrowIfCancellationRequested();

                    await AppendLogAsync("\n[工具] " + name + " " + TruncateForLog(argsJson));
                    await SetStatusAsync("执行工具: " + name);

                    // 危险操作确认
                    if (ToolRegistry.TryGet(name, out var descriptor) && descriptor.Dangerous)
                    {
                        var confirmed = await MainThread.InvokeAsync(() =>
                        {
                            var result = FlaxEngine.MessageBox.Show(
                                "AI 请求执行危险操作：\n\n" + name + "\n参数: " + TruncateForLog(argsJson) + "\n\n是否允许？",
                                "HundunAgent 安全确认",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Warning);
                            return result == DialogResult.Yes;
                        });

                        if (!confirmed)
                        {
                            await AppendLogAsync("  → 已被用户拒绝");
                            _messages.Add(new Dictionary<string, object>
                            {
                                { "role", "tool" },
                                { "tool_call_id", id },
                                { "content", "{\"success\":false,\"error\":\"用户拒绝了该危险操作\"}" }
                            });
                            continue;
                        }
                    }

                    JsonElement argsEl;
                    try
                    {
                        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson);
                        argsEl = doc.RootElement.Clone();
                    }
                    catch (JsonException jex)
                    {
                        var errText = "{\"success\":false,\"error\":\"参数JSON解析失败: " + jex.Message.Replace("\"", "'") + "\"}";
                        await AppendLogAsync("  → 参数解析失败");
                        _messages.Add(new Dictionary<string, object>
                        {
                            { "role", "tool" },
                            { "tool_call_id", id },
                            { "content", errText }
                        });
                        continue;
                    }

                    var result = await ToolRegistry.ExecuteAsync(name, argsEl);
                    var resultText = JsonSerializer.Serialize(result);
                    await AppendLogAsync("  → " + TruncateForLog(resultText));

                    _messages.Add(new Dictionary<string, object>
                    {
                        { "role", "tool" },
                        { "tool_call_id", id },
                        { "content", resultText }
                    });
                }

                // 控制历史长度
                TrimMessages();
            }

            await AppendLogAsync("\n[系统] 达到最大轮数（" + maxSteps + "），任务循环结束。");
        }

        private void TrimMessages()
        {
            const int maxMessages = 60;
            while (_messages.Count > maxMessages)
            {
                // 保留 system（索引0），从索引1开始删
                _messages.RemoveAt(1);
            }
        }

        private static string TruncateForLog(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            text = text.Replace("\n", " ");
            return text.Length > 300 ? text.Substring(0, 300) + "..." : text;
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            _sendButton.Enabled = !busy;
            _stopButton.Enabled = busy;
            _inputBox.IsReadOnly = busy;
            if (!busy)
                _statusLabel.Text = "空闲";
        }

        private Task SetStatusAsync(string text)
        {
            return MainThread.InvokeAsync(() => _statusLabel.Text = text);
        }

        private void AppendLog(string text)
        {
            _logBox.Text += text;
            _logBox.TargetViewOffset = new Float2(0, float.MaxValue);
        }

        private Task AppendLogAsync(string text)
        {
            return MainThread.InvokeAsync(() => AppendLog(text));
        }

        /// <inheritdoc />
        public override void OnDestroy()
        {
            _runCts?.Cancel();
            if (ReferenceEquals(_instance, this))
                _instance = null;
            base.OnDestroy();
        }
    }
}
