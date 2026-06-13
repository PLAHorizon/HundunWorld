using System;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Services
{
    public class WeatherService
    {
        private const string DefaultLocation = "101010100";

        public async Task<WeatherForecast?> GetWeatherAsync(string? locationId = null)
        {
            var loc = string.IsNullOrWhiteSpace(locationId) ? DefaultLocation : locationId;
            var forecast = await QWeatherClient.GetWeatherForecastAsync(loc, 7).ConfigureAwait(false);
            if (forecast != null)
            {
                PopulateSolarTerm(forecast);
            }
            return forecast;
        }

        public async Task<WeatherForecast?> GetWeatherByCoordsAsync(double lat, double lon, string cityName)
        {
            var forecast = await QWeatherClient.GetWeatherForecastByCoordsAsync(lat, lon, cityName, 7).ConfigureAwait(false);
            if (forecast != null)
            {
                PopulateSolarTerm(forecast);
            }
            return forecast;
        }

        private static void PopulateSolarTerm(WeatherForecast forecast)
        {
            var solarTerm = SolarTermService.GetCurrentSolarTerm();
            forecast.SolarTerm.Name = solarTerm.Name;
            forecast.SolarTerm.Season = solarTerm.Season;
            forecast.SolarTerm.Description = solarTerm.Description;
            forecast.SolarTerm.DietaryTip = solarTerm.DietaryTip;
            forecast.SolarTerm.HealthTip = solarTerm.HealthTip;
            forecast.SolarTerm.RecommendedDish = solarTerm.RecommendedDish;
            forecast.SolarTerm.DishReason = solarTerm.DishReason;
            forecast.SolarTerm.Ingredients = solarTerm.Ingredients;
            forecast.SolarTerm.CookingMethod = solarTerm.CookingMethod;
            forecast.SolarTerm.Contraindications = solarTerm.Contraindications;
        }

        public string GetWeatherSummary(WeatherForecast forecast)
        {
            if (forecast == null) return "";

            var current = forecast.Current;
            return $"{forecast.City} {current.Temperature}° {current.ConditionText} 体感{current.FeelsLike}";
        }
    }
}
