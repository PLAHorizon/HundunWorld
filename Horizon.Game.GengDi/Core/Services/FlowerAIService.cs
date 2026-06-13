using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Horizon.Game.GengDi.Core.Services
{
    public class FlowerAIService
    {
        private sealed class AIChatResponse
        {
            public string Answer { get; set; } = "";
            public DateTime Timestamp { get; set; }
        }

        public async Task<string?> GetAISummaryAsync()
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerDashboard/ai-summary").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<string>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerAIService] {nameof(GetAISummaryAsync)}: {ex.Message}");
                return null;
            }
        }

        public async Task<string?> ChatWithAIAsync(string question)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { Question = question }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}flower-ai/chat", content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<AIChatResponse>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data?.Answer : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerAIService] {nameof(ChatWithAIAsync)}: {ex.Message}");
                return null;
            }
        }

        public async Task<List<PlantingAdviceInfo>?> GenerateAdviceAsync(long batchId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerAdvice/generate/{batchId}", null).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<PlantingAdviceInfo>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerAIService] {nameof(GenerateAdviceAsync)}: {ex.Message}"); return null; }
        }

        public async Task<List<PlantingAdviceInfo>?> GetActiveAdviceAsync(long batchId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerAdvice/active/{batchId}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<PlantingAdviceInfo>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerAIService] {nameof(GetActiveAdviceAsync)}: {ex.Message}"); return null; }
        }

        public async Task<bool> MarkAdviceExecutedAsync(long adviceId, long batchId, string action = "Executed")
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var request = new { BatchId = batchId, Action = action };
                var content = new StringContent(JsonSerializer.Serialize(request, FlowerHttpConfig.JsonOptions), Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PutAsync($"{baseUri}FlowerAdvice/{adviceId}/execute", content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerAIService] {nameof(MarkAdviceExecutedAsync)}: {ex.Message}"); return false; }
        }

        public async Task<List<PlantingAdviceInfo>?> GetAdviceByTypeAsync(long batchId, string adviceType)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerAdvice/type/{batchId}/{adviceType}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<PlantingAdviceInfo>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerAIService] {nameof(GetAdviceByTypeAsync)}: {ex.Message}"); return null; }
        }
    }
}
