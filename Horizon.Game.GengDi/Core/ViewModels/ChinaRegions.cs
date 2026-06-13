using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Core.Services.Database;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public static class ChinaRegions
    {
        public static async Task<bool> LoadAsync()
        {
            return await RegionStore.InitializeAsync();
        }

        public static List<string> GetProvinces()
        {
            return RegionStore.GetProvinces();
        }

        public static List<string> GetCities(string province)
        {
            return RegionStore.GetCities(province);
        }

        public static List<string> GetDistricts(string province, string city)
        {
            return RegionStore.GetDistricts(province, city);
        }

        public static List<string> GetStreets(string province, string city, string district)
        {
            return RegionStore.GetStreets(province, city, district);
        }

        public static List<string> GetCommunities(string province, string city, string district, string street)
        {
            return RegionStore.GetCommunities(province, city, district, street);
        }

        public static void ClearCache()
        {
            // LiteDB handles caching internally
        }
    }
}
