using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Game.Services;

public class GameTradeService : GameApiServiceBase
{
    public GameTradeService(HttpClient httpClient, IConfiguration configuration)
        : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement>?> GetTradeLogsAsync(int page = 1, int pageSize = 20, int? tradeType = null, string? startTime = null, string? endTime = null)
    {
        var url = $"/GameTrade/logs?page={page}&pageSize={pageSize}";
        if (tradeType.HasValue) url += $"&tradeType={tradeType}";
        if (!string.IsNullOrEmpty(startTime)) url += $"&startTime={startTime}";
        if (!string.IsNullOrEmpty(endTime)) url += $"&endTime={endTime}";
        return await GetAsync<JsonElement>(url);
    }
}
