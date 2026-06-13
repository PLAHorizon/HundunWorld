using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerAIService : FlowerApiServiceBase
{
    public FlowerAIService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> GetDocumentListAsync() => await GetAsync<JsonElement?>("FlowerAdmin/ai-documents");

    public async Task<ResultVM<JsonElement?>?> CreateDocumentAsync(object data) => await PostAsync<JsonElement?>("FlowerAdmin/ai-documents", data);

    public async Task<ResultVM<JsonElement?>?> DeleteDocumentAsync(long documentId) => await DeleteAsync<JsonElement?>($"FlowerAdmin/ai-documents/{documentId}");

    public async Task<ResultVM<JsonElement?>?> ReIndexDocumentAsync(long documentId) => await PostAsync<JsonElement?>($"FlowerAdmin/ai-documents/{documentId}/reindex");

    public async Task<ResultVM<JsonElement?>?> ChatAsync(object data) => await PostAsync<JsonElement?>("flower-ai/chat", data);
}
