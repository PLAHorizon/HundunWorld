using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Game.Services;

public class GameGuideService : GameApiServiceBase
{
    public GameGuideService(HttpClient httpClient, IConfiguration configuration)
        : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement>?> GetGuidesAsync(int page = 1, int pageSize = 20, string? module = null, string? status = null)
    {
        var url = $"/Article/list?page={page}&pageSize={pageSize}&category=game-guide";
        if (!string.IsNullOrEmpty(module)) url += $"&tag={module}";
        if (!string.IsNullOrEmpty(status)) url += $"&status={status}";
        return await GetAsync<JsonElement>(url);
    }

    public async Task<ResultVM<JsonElement>?> GetGuideDetailAsync(long articleId)
        => await GetAsync<JsonElement>($"/Article/{articleId}");

    public async Task<ResultVM<JsonElement>?> CreateGuideAsync(object guideDto)
        => await PostAsync<JsonElement>("/Article/create", guideDto);

    public async Task<ResultVM<bool>?> UpdateGuideAsync(object guideDto)
        => await PutAsync<bool>("/Article/update", guideDto);

    public async Task<ResultVM<bool>?> DeleteGuideAsync(long articleId)
        => await DeleteAsync<bool>($"/Article/{articleId}");

    public async Task<ResultVM<bool>?> PublishGuideAsync(long articleId)
        => await PostAsync<bool>($"/Article/publish/{articleId}");

    public async Task<ResultVM<bool>?> UnpublishGuideAsync(long articleId)
        => await PostAsync<bool>($"/Article/unpublish/{articleId}");

    public async Task<string?> UploadImageAsync(string fileName, Stream fileStream, string contentType = "image/png")
    {
        try
        {
            ApplyAdminKeyIfConfigured();
            using var content = new MultipartFormDataContent();
            var streamContent = new StreamContent(fileStream);
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            content.Add(streamContent, "file", fileName);

            var response = await Http.PostAsync("/Article/upload-image", content);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ResultVM<JsonElement>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (result?.IsSuccess == true && result.Data.ValueKind != JsonValueKind.Undefined)
            {
                return result.Data.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}
