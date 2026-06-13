using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerSpeciesService : FlowerApiServiceBase
{
    public FlowerSpeciesService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> GetSpeciesListAsync() => await GetAsync<JsonElement?>("FlowerSpecies/list");
}
