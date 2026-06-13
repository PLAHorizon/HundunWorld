using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Game.Services;

public class GameItemService : GameApiServiceBase
{
    public GameItemService(HttpClient httpClient, IConfiguration configuration)
        : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement>?> GetItemTemplatesAsync(int page = 1, int pageSize = 20, string? itemType = null, string? quality = null)
    {
        var url = $"/GameItem/templates?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrEmpty(itemType)) url += $"&itemType={itemType}";
        if (!string.IsNullOrEmpty(quality)) url += $"&quality={quality}";
        return await GetAsync<JsonElement>(url);
    }

    public async Task<ResultVM<JsonElement>?> GetItemTemplateDetailAsync(int templateId)
        => await GetAsync<JsonElement>($"/GameItem/templates/{templateId}");
}
