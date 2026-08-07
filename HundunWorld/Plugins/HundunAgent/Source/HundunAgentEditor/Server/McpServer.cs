using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlaxEngine;
using HundunAgent.Core;

namespace HundunAgent.Server
{
    /// <summary>
    /// HundunAgent MCP (Model Context Protocol) 服务器（Streamable HTTP，localhost:21901）。
    /// 支持 JSON-RPC 方法：initialize / notifications/* / tools/list / tools/call / ping。
    /// 任意支持 MCP 的 AI 客户端（Qoder、Claude、Trae 等）可直接连接 http://localhost:21901/mcp。
    /// </summary>
    public sealed class McpServer
    {
        public const int DefaultPort = 21901;
        private const string ProtocolVersion = "2025-03-26";
        private const string SessionId = "hundun-agent-flax";

        private static readonly Lazy<McpServer> _instance = new Lazy<McpServer>(() => new McpServer());

        public static McpServer Instance => _instance.Value;

        public static bool IsRunning => Instance._listener != null && Instance._listener.IsListening;

        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private Task _serverTask;

        private McpServer()
        {
        }

        public void Start()
        {
            if (_listener != null)
                return;

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add("http://localhost:" + DefaultPort + "/");
                _listener.Start();

                _cts = new CancellationTokenSource();
                _serverTask = Task.Run(() => ListenAsync(_cts.Token));

                Debug.Log("[HundunAgent] MCP 服务已启动: http://localhost:" + DefaultPort + "/mcp");
            }
            catch (Exception ex)
            {
                Debug.LogError("[HundunAgent] MCP 服务启动失败（端口 " + DefaultPort + " 可能被占用）: " + ex.Message);
                try { _listener?.Close(); } catch { }
                _listener = null;
            }
        }

        public void Stop()
        {
            if (_listener == null)
                return;

            try
            {
                _cts?.Cancel();
                _listener.Stop();
                _listener.Close();
                _serverTask?.Wait(TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HundunAgent] MCP 服务停止异常: " + ex.Message);
            }
            finally
            {
                _listener = null;
                _cts?.Dispose();
                _cts = null;
                _serverTask = null;
            }
        }

        private async Task ListenAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception)
                {
                    break;
                }

                if (context == null)
                    break;

                _ = Task.Run(() => HandleAsync(context));
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            response.AddHeader("Mcp-Session-Id", SessionId);
            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.AddHeader("Access-Control-Allow-Methods", "GET, POST, DELETE, OPTIONS");
            response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Accept, Mcp-Session-Id");
            response.AddHeader("Access-Control-Expose-Headers", "Mcp-Session-Id");

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 204;
                response.Close();
                return;
            }

            var path = request.Url.AbsolutePath.TrimEnd('/');

            try
            {
                // GET /：服务说明（便于浏览器探测）
                if (request.HttpMethod == "GET")
                {
                    await SendJsonAsync(response, new
                    {
                        server = "hundun-agent-flax",
                        transport = "streamable-http",
                        endpoint = "/mcp",
                        toolCount = ToolRegistry.All.Count,
                        hint = "POST JSON-RPC 2.0 到 /mcp，方法：initialize / tools/list / tools/call"
                    });
                    return;
                }

                if (request.HttpMethod != "POST" || (path != "/mcp" && path != ""))
                {
                    await SendJsonAsync(response, new { error = "Not Found" }, 404);
                    return;
                }

                string body;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                    body = await reader.ReadToEndAsync();

                JsonElement msg;
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    msg = doc.RootElement.Clone();
                }
                catch (JsonException)
                {
                    await SendJsonAsync(response, JsonRpcError(default, -32700, "Parse error"), 200);
                    return;
                }

                await DispatchRpcAsync(msg, response);
            }
            catch (Exception ex)
            {
                Debug.LogError("[HundunAgent] MCP 请求处理异常: " + ex.Message);
                try
                {
                    await SendJsonAsync(response, new { error = ex.Message }, 500);
                }
                catch { }
            }
        }

        private async Task DispatchRpcAsync(JsonElement msg, HttpListenerResponse response)
        {
            var method = msg.TryGetProperty("method", out var m) ? m.GetString() : null;
            var hasId = msg.TryGetProperty("id", out var id) && id.ValueKind != JsonValueKind.Null;

            // 通知（无 id）：直接 202 接受
            if (!hasId)
            {
                response.StatusCode = 202;
                response.Close();
                return;
            }

            if (method == null)
            {
                await SendJsonAsync(response, JsonRpcError(id, -32600, "Invalid Request"), 200);
                return;
            }

            switch (method)
            {
                case "initialize":
                {
                    var result = new Dictionary<string, object>
                    {
                        { "protocolVersion", ProtocolVersion },
                        {
                            "capabilities", new Dictionary<string, object>
                            {
                                { "tools", new Dictionary<string, object>() }
                            }
                        },
                        {
                            "serverInfo", new Dictionary<string, object>
                            {
                                { "name", "hundun-agent-flax" },
                                { "version", "1.0.0" }
                            }
                        }
                    };
                    await SendJsonAsync(response, JsonRpcResult(id, result));
                    return;
                }

                case "ping":
                    await SendJsonAsync(response, JsonRpcResult(id, new Dictionary<string, object>()));
                    return;

                case "tools/list":
                {
                    var tools = new List<object>();
                    foreach (var tool in ToolRegistry.All)
                    {
                        tools.Add(new Dictionary<string, object>
                        {
                            { "name", tool.Name },
                            { "description", tool.Description },
                            { "inputSchema", ParseSchema(tool.InputSchemaJson) }
                        });
                    }
                    await SendJsonAsync(response, JsonRpcResult(id, new Dictionary<string, object> { { "tools", tools } }));
                    return;
                }

                case "tools/call":
                {
                    var paramsEl = msg.TryGetProperty("params", out var p) ? p : default;
                    var toolName = paramsEl.ValueKind == JsonValueKind.Object &&
                                   paramsEl.TryGetProperty("name", out var n) ? n.GetString() : null;

                    if (string.IsNullOrEmpty(toolName))
                    {
                        await SendJsonAsync(response, JsonRpcError(id, -32602, "缺少 params.name"));
                        return;
                    }

                    JsonElement toolArgs;
                    if (paramsEl.ValueKind == JsonValueKind.Object && paramsEl.TryGetProperty("arguments", out var a))
                        toolArgs = a.Clone();
                    else
                    {
                        using var empty = JsonDocument.Parse("{}");
                        toolArgs = empty.RootElement.Clone();
                    }

                    var execResult = await ToolRegistry.ExecuteAsync(toolName, toolArgs);
                    var ok = execResult.TryGetValue("success", out var s) && s is true;

                    var text = JsonSerializer.Serialize(execResult);
                    var content = new List<object>
                    {
                        new Dictionary<string, object> { { "type", "text" }, { "text", text } }
                    };

                    await SendJsonAsync(response, JsonRpcResult(id, new Dictionary<string, object>
                    {
                        { "content", content },
                        { "isError", !ok }
                    }));
                    return;
                }

                default:
                    await SendJsonAsync(response, JsonRpcError(id, -32601, "Method not found: " + method));
                    return;
            }
        }

        private static Dictionary<string, object> JsonRpcResult(JsonElement id, object result)
        {
            return new Dictionary<string, object>
            {
                { "jsonrpc", "2.0" },
                { "id", IdToObject(id) },
                { "result", result }
            };
        }

        private static Dictionary<string, object> JsonRpcError(JsonElement id, int code, string message)
        {
            return new Dictionary<string, object>
            {
                { "jsonrpc", "2.0" },
                { "id", id.ValueKind == JsonValueKind.Undefined ? (object)null : IdToObject(id) },
                { "error", new Dictionary<string, object> { { "code", code }, { "message", message } } }
            };
        }

        private static object IdToObject(JsonElement id)
        {
            switch (id.ValueKind)
            {
                case JsonValueKind.Number: return id.GetInt64();
                case JsonValueKind.String: return id.GetString();
                default: return null;
            }
        }

        private static JsonElement ParseSchema(string schemaJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(schemaJson);
                return doc.RootElement.Clone();
            }
            catch
            {
                using var doc = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}");
                return doc.RootElement.Clone();
            }
        }

        private static async Task SendJsonAsync(HttpListenerResponse response, object data, int statusCode = 200)
        {
            try
            {
                response.StatusCode = statusCode;
                response.ContentType = "application/json; charset=utf-8";

                var json = JsonSerializer.Serialize(data);
                var buffer = Encoding.UTF8.GetBytes(json);

                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            catch
            {
                // 客户端可能已断开
            }
            finally
            {
                try { response.Close(); } catch { }
            }
        }
    }
}
