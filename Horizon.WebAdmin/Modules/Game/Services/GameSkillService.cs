using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Game.Services;

public class GameSkillService : GameApiServiceBase
{
    public GameSkillService(HttpClient httpClient, IConfiguration configuration)
        : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement>?> GetSkillTemplatesAsync(int page = 1, int pageSize = 20, string? skillType = null, int? sectId = null)
    {
        var url = $"/GameSkill/templates?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrEmpty(skillType)) url += $"&skillType={skillType}";
        if (sectId.HasValue) url += $"&sectId={sectId}";
        return await GetAsync<JsonElement>(url);
    }

    public async Task<ResultVM<JsonElement>?> GetSkillTemplateDetailAsync(int templateId)
        => await GetAsync<JsonElement>($"/GameSkill/templates/{templateId}");
}
