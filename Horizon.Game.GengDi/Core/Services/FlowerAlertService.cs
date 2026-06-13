using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;

namespace Horizon.Game.GengDi.Core.Services
{
    public class FlowerAlertService
    {
        public async Task<List<AlertMessage>?> GetAlertsAsync(int speciesId, int skip, int take)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerAlert/species/{speciesId}?skip={skip}&take={take}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<AlertMessage>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
