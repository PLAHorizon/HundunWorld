using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HundunAgent.Core;

namespace HundunAgent.Chat
{
    /// <summary>
    /// OpenAI 兼容 chat/completions 客户端（支持 tools / function-calling）。
    /// </summary>
    public static class LlmClient
    {
        private static readonly HttpClient _http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All
        });

        /// <summary>
        /// 根据工具注册表构造 OpenAI tools 数组。
        /// </summary>
        public static List<object> BuildToolsArray()
        {
            var tools = new List<object>();
            foreach (var tool in ToolRegistry.All)
            {
                JsonElement schema;
                try
                {
                    using var doc = JsonDocument.Parse(tool.InputSchemaJson);
                    schema = doc.RootElement.Clone();
                }
                catch
                {
                    using var doc = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}");
                    schema = doc.RootElement.Clone();
                }

                tools.Add(new Dictionary<string, object>
                {
                    { "type", "function" },
                    {
                        "function", new Dictionary<string, object>
                        {
                            { "name", tool.Name },
                            { "description", tool.Description },
                            { "parameters", schema }
                        }
                    }
                });
            }
            return tools;
        }

        /// <summary>
        /// 调用 chat/completions，返回 choices[0].message（原始 JsonElement）。
        /// </summary>
        public static async Task<JsonElement> ChatCompletionAsync(
            List<Dictionary<string, object>> messages,
            AgentSettings settings,
            CancellationToken token = default)
        {
            if (!settings.IsConfigured)
                throw new InvalidOperationException("尚未配置 LLM（BaseUrl/Model），请先在聊天窗口填写设置");

            var url = settings.BaseUrl.TrimEnd('/') + "/chat/completions";

            var request = new Dictionary<string, object>
            {
                { "model", settings.Model },
                { "messages", messages },
                { "tools", BuildToolsArray() },
                { "stream", false }
            };

            var body = JsonSerializer.Serialize(request, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Content = new StringContent(body, Encoding.UTF8, "application/json");
            if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(10, settings.RequestTimeoutSeconds)));

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(httpRequest, timeoutCts.Token);
            }
            catch (TaskCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException("LLM 请求超时（" + settings.RequestTimeoutSeconds + "s）: " + url);
            }

            using (response)
            {
                var text = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var snippet = text.Length > 500 ? text.Substring(0, 500) : text;
                    throw new InvalidOperationException("LLM 接口错误 HTTP " + (int)response.StatusCode + ": " + snippet);
                }

                JsonDocument doc;
                try
                {
                    doc = JsonDocument.Parse(text);
                }
                catch (JsonException jex)
                {
                    throw new InvalidOperationException("LLM 响应解析失败: " + jex.Message);
                }

                using (doc)
                {
                    if (!doc.RootElement.TryGetProperty("choices", out var choices) ||
                        choices.ValueKind != JsonValueKind.Array ||
                        choices.GetArrayLength() == 0)
                    {
                        throw new InvalidOperationException("LLM 响应缺少 choices");
                    }

                    var message = choices[0].GetProperty("message");
                    return message.Clone();
                }
            }
        }
    }
}
