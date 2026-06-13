using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Game.Services;

public class GameChatService : GameApiServiceBase
{
    public GameChatService(HttpClient httpClient, IConfiguration configuration)
        : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement>?> GetChatMessagesAsync(int page = 1, int pageSize = 20, int? channel = null, string? startTime = null, string? endTime = null)
    {
        var url = $"/GameChat/messages?page={page}&pageSize={pageSize}";
        if (channel.HasValue) url += $"&channel={channel}";
        if (!string.IsNullOrEmpty(startTime)) url += $"&startTime={startTime}";
        if (!string.IsNullOrEmpty(endTime)) url += $"&endTime={endTime}";
        return await GetAsync<JsonElement>(url);
    }

    public async Task<ResultVM<JsonElement>?> GetChannelSettingsAsync()
        => await GetAsync<JsonElement>("/GameChat/channel-settings");

    public async Task<ResultVM<bool>?> UpdateChannelSettingAsync(object setting)
        => await PutAsync<bool>("/GameChat/channel-settings", setting);

    public async Task<ResultVM<JsonElement>?> GetBlacklistAsync(int page = 1, int pageSize = 20)
        => await GetAsync<JsonElement>($"/GameChat/blacklist?page={page}&pageSize={pageSize}");

    public async Task<ResultVM<bool>?> RemoveFromBlacklistAsync(long blacklistId)
        => await DeleteAsync<bool>($"/GameChat/blacklist/{blacklistId}");
}
