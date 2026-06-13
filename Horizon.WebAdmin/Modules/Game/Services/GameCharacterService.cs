using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Game.Services;

public class GameCharacterService : GameApiServiceBase
{
    public GameCharacterService(HttpClient httpClient, IConfiguration configuration)
        : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement>?> GetGameRolesAsync(object queryDto)
        => await PostAsync<JsonElement>("/GameUserRole/gameroles", queryDto);

    public async Task<ResultVM<JsonElement>?> GetGameUserRolesAsync(object queryDto)
        => await PostAsync<JsonElement>("/GameUserRole/gameuserroles", queryDto);

    public async Task<ResultVM<JsonElement>?> GetGameRoleWorldInfoAsync(object queryDto)
        => await PostAsync<JsonElement>("/GameUserRole/getgamerole", queryDto);

    public async Task<ResultVM<bool>?> SetGameRoleWorldInfoAsync(object roleDto)
        => await PostAsync<bool>("/GameUserRole/setgamerole", roleDto);

    public async Task<ResultVM<JsonElement>?> GetRoleEquipmentsAsync(object queryDto)
        => await PostAsync<JsonElement>("/GameUserRole/roleequipments", queryDto);
}
