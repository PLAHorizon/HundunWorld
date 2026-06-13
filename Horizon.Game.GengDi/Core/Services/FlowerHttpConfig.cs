using System;
using System.Net.Http;
using System.Text.Json;
using Horizon.Game.Message.Network;

namespace Horizon.Game.GengDi.Core.Services
{
    public static class FlowerHttpConfig
    {
        public static readonly HttpClient HttpClient = new HttpClient(new AuthHeaderHandler(SslConfiguration.CreateTestEnvironmentHandler()))
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static string GetBaseUri()
        {
            return GatewayDiscoveryService.GetWebApiBaseUrl();
        }
    }

    public class FlowerApiResult<T>
    {
        public int Code { get; set; } = 200;
        public string ErrorMessage { get; set; }
        public bool IsSuccess { get; set; } = false;
        public T Data { get; set; }
    }
}
