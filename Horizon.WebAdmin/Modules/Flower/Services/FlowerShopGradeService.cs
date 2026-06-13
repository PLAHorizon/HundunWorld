using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerShopGradeService : FlowerApiServiceBase
{
    public FlowerShopGradeService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> GetAllGradesAsync() => await GetAsync<JsonElement?>("FlowerShopGrade");

    public async Task<ResultVM<JsonElement?>?> GetGradeAsync(long gradeId) => await GetAsync<JsonElement?>($"FlowerShopGrade/{gradeId}");

    public async Task<ResultVM<JsonElement?>?> AddGradeAsync(object data) => await PostAsync<JsonElement?>("FlowerShopGrade", data);

    public async Task<ResultVM<JsonElement?>?> UpdateGradeAsync(long gradeId, object data) => await PutAsync<JsonElement?>($"FlowerShopGrade/{gradeId}", data);

    public async Task<ResultVM<JsonElement?>?> DeleteGradeAsync(long gradeId) => await DeleteAsync<JsonElement?>($"FlowerShopGrade/{gradeId}");
}
