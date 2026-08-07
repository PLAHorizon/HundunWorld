using System;
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
    /// HundunAgent HTTP REST 服务（localhost:21900）：
    /// GET  /health              健康检查
    /// GET  /api/tools           工具清单（含 JSON Schema）
    /// POST /api/tools/{name}    调用工具，请求体为参数 JSON
    /// </summary>
    public sealed class AgentHttpServer
    {
        public const int DefaultPort = 21900;

        private static readonly Lazy<AgentHttpServer> _instance =
            new Lazy<AgentHttpServer>(() => new AgentHttpServer());

        public static AgentHttpServer Instance => _instance.Value;

        public static bool IsRunning => Instance._listener != null && Instance._listener.IsListening;

        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private Task _serverTask;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private AgentHttpServer()
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

                Debug.Log("[HundunAgent] HTTP 服务已启动: http://localhost:" + DefaultPort + "/");
            }
            catch (Exception ex)
            {
                Debug.LogError("[HundunAgent] HTTP 服务启动失败（端口 " + DefaultPort + " 可能被占用）: " + ex.Message);
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
                Debug.LogWarning("[HundunAgent] HTTP 服务停止异常: " + ex.Message);
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
                    break; // listener 已关闭
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

            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.AddHeader("Access-Control-Allow-Headers", "Content-Type");

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 204;
                response.Close();
                return;
            }

            try
            {
                var path = request.Url.AbsolutePath.TrimEnd('/');

                if (request.HttpMethod == "GET" && (path == "" || path == "/health"))
                {
                    await SendJsonAsync(response, new
                    {
                        status = "ok",
                        plugin = "HundunAgent",
                        toolCount = ToolRegistry.All.Count
                    });
                    return;
                }

                if (request.HttpMethod == "GET" && path == "/api/tools")
                {
                    var tools = new System.Collections.Generic.List<object>();
                    foreach (var tool in ToolRegistry.All)
                    {
                        tools.Add(new
                        {
                            name = tool.Name,
                            description = tool.Description,
                            dangerous = tool.Dangerous,
                            undoable = tool.Undoable,
                            inputSchema = ParseSchema(tool.InputSchemaJson)
                        });
                    }
                    await SendJsonAsync(response, new { tools });
                    return;
                }

                if (request.HttpMethod == "POST" && path.StartsWith("/api/tools/"))
                {
                    var toolName = Uri.UnescapeDataString(path.Substring("/api/tools/".Length));
                    string body;
                    using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                        body = await reader.ReadToEndAsync();

                    JsonElement args;
                    try
                    {
                        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                        args = doc.RootElement.Clone();
                    }
                    catch (JsonException jex)
                    {
                        await SendJsonAsync(response, new { success = false, error = "参数 JSON 解析失败: " + jex.Message }, 400);
                        return;
                    }

                    var result = await ToolRegistry.ExecuteAsync(toolName, args);
                    var ok = result.TryGetValue("success", out var s) && s is true;
                    await SendJsonAsync(response, result, ok ? 200 : 400);
                    return;
                }

                await SendJsonAsync(response, new { error = "Not Found: " + path }, 404);
            }
            catch (Exception ex)
            {
                Debug.LogError("[HundunAgent] HTTP 请求处理异常: " + ex.Message);
                try
                {
                    await SendJsonAsync(response, new { error = ex.Message }, 500);
                }
                catch { }
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

                var json = JsonSerializer.Serialize(data, JsonOptions);
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
