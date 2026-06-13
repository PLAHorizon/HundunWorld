using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Game.Services;

public class GameGuildService : GameApiServiceBase
{
    public GameGuildService(HttpClient httpClient, IConfiguration configuration)
        : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement>?> GetGuildsAsync(int page = 1, int pageSize = 20, string? status = null)
    {
        var url = $"/GameGuild/list?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrEmpty(status)) url += $"&status={status}";
        return await GetAsync<JsonElement>(url);
    }

    public async Task<ResultVM<JsonElement>?> GetGuildDetailAsync(long guildId)
        => await GetAsync<JsonElement>($"/GameGuild/{guildId}");
}
