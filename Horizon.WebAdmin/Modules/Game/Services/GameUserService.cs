using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Game.Services;

public class GameUserService : GameApiServiceBase
{
    public GameUserService(HttpClient httpClient, IConfiguration configuration)
        : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement>?> GetGameUserAsync(object queryDto)
        => await PostAsync<JsonElement>("/GameUserRole/gameuser", queryDto);

    public async Task<ResultVM<JsonElement>?> GetGameRolesAsync(object queryDto)
        => await PostAsync<JsonElement>("/GameUserRole/gameroles", queryDto);

    public async Task<ResultVM<JsonElement>?> GetGameUserRolesAsync(object queryDto)
        => await PostAsync<JsonElement>("/GameUserRole/gameuserroles", queryDto);

    public async Task<ResultVM<bool>?> FreezeUserAsync(long userId)
        => await PostAsync<bool>($"/GameUserRole/setgamerole", new { userId, action = "freeze" });

    public async Task<ResultVM<bool>?> UnfreezeUserAsync(long userId)
        => await PostAsync<bool>($"/GameUserRole/setgamerole", new { userId, action = "unfreeze" });

    public async Task<ResultVM<bool>?> BanUserAsync(long userId)
        => await PostAsync<bool>($"/GameUserRole/setgamerole", new { userId, action = "ban" });
}
