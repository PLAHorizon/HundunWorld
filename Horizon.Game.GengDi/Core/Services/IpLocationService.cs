using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Services
{
    public class IpLocationService
    {
        private static readonly HttpClient _httpClient = new HttpClient(SslConfiguration.CreateStandardHandler())
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        private static IpLocationResult? _cachedResult;
        private static DateTime _cacheTime = DateTime.MinValue;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

        public static async Task<IpLocationResult?> LocateAsync()
        {
            if (_cachedResult != null && DateTime.UtcNow - _cacheTime < CacheDuration)
                return _cachedResult;

            var apis = new Func<Task<IpLocationResult?>>[]
            {
                LocateViaIpApiAsync,
                LocateViaIpSbAsync,
                LocateViaIpInfoAsync
            };

            foreach (var api in apis)
            {
                try
                {
                    var result = await api();
                    if (result != null && IsValidChinaLocation(result))
                    {
                        _cachedResult = result;
                        _cacheTime = DateTime.UtcNow;
                        return result;
                    }
                }
                catch { }
            }

            return null;
        }

        private static bool IsValidChinaLocation(IpLocationResult result)
        {
            if (result.Latitude == 0 && result.Longitude == 0) return false;
            if (double.IsNaN(result.Latitude) || double.IsNaN(result.Longitude)) return false;
            if (result.Latitude < 3.0 || result.Latitude > 55.0) return false;
            if (result.Longitude < 70.0 || result.Longitude > 140.0) return false;
            return true;
        }

        private static async Task<IpLocationResult?> LocateViaIpApiAsync()
        {
            var json = await _httpClient.GetStringAsync("http://ip-api.com/json/?lang=zh-CN&fields=status,country,regionName,city,lat,lon,query");
            var response = JsonSerializer.Deserialize<IpApiResponse>(json);

            if (response?.Status != "success") return null;
            if (response.Country != "China" && response.Country != "中国") return null;

            return new IpLocationResult
            {
                Latitude = response.Lat,
                Longitude = response.Lon,
                Province = response.RegionName ?? string.Empty,
                City = response.City ?? string.Empty,
                Ip = response.Query ?? string.Empty,
                Source = "ip-api"
            };
        }

        private static async Task<IpLocationResult?> LocateViaIpSbAsync()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.ip.sb/geoip");
            request.Headers.UserAgent.ParseAdd("HundunWorld/1.0");
            var responseMsg = await _httpClient.SendAsync(request);
            var json = await responseMsg.Content.ReadAsStringAsync();
            var response = JsonSerializer.Deserialize<IpSbResponse>(json);

            if (response == null) return null;
            if (response.Country != "China" && response.Country != "CN") return null;

            return new IpLocationResult
            {
                Latitude = response.Latitude,
                Longitude = response.Longitude,
                Province = response.Region ?? string.Empty,
                City = response.City ?? string.Empty,
                Ip = response.Ip ?? string.Empty,
                Source = "ip-sb"
            };
        }

        private static async Task<IpLocationResult?> LocateViaIpInfoAsync()
        {
            var json = await _httpClient.GetStringAsync("https://ipinfo.io/json");
            var response = JsonSerializer.Deserialize<IpInfoResponse>(json);

            if (response == null) return null;
            if (response.Country != "CN") return null;
            if (string.IsNullOrWhiteSpace(response.Loc)) return null;

            var parts = response.Loc.Split(',');
            if (parts.Length != 2) return null;
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)) return null;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) return null;

            return new IpLocationResult
            {
                Latitude = lat,
                Longitude = lon,
                Province = response.Region ?? string.Empty,
                City = response.City ?? string.Empty,
                Ip = response.Ip ?? string.Empty,
                Source = "ipinfo"
            };
        }

        public static CityInfo? FindNearestCity(double lat, double lon)
        {
            return CitySearchService.FindNearestCity(lat, lon);
        }
    }

    public class IpLocationResult
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Province { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Ip { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }

    internal class IpApiResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }
        [JsonPropertyName("country")]
        public string? Country { get; set; }
        [JsonPropertyName("regionName")]
        public string? RegionName { get; set; }
        [JsonPropertyName("city")]
        public string? City { get; set; }
        [JsonPropertyName("lat")]
        public double Lat { get; set; }
        [JsonPropertyName("lon")]
        public double Lon { get; set; }
        [JsonPropertyName("query")]
        public string? Query { get; set; }
    }

    internal class IpSbResponse
    {
        [JsonPropertyName("ip")]
        public string? Ip { get; set; }
        [JsonPropertyName("country")]
        public string? Country { get; set; }
        [JsonPropertyName("region")]
        public string? Region { get; set; }
        [JsonPropertyName("city")]
        public string? City { get; set; }
        [JsonPropertyName("latitude")]
        public double Latitude { get; set; }
        [JsonPropertyName("longitude")]
        public double Longitude { get; set; }
    }

    internal class IpInfoResponse
    {
        [JsonPropertyName("ip")]
        public string? Ip { get; set; }
        [JsonPropertyName("country")]
        public string? Country { get; set; }
        [JsonPropertyName("region")]
        public string? Region { get; set; }
        [JsonPropertyName("city")]
        public string? City { get; set; }
        [JsonPropertyName("loc")]
        public string? Loc { get; set; }
    }
}
