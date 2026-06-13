using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class KdniaoApiClient
    {
        private const string ApiUrl = "https://api.kdniao.com/Ebusiness/EbusinessOrderHandle.aspx";
        private const string RequestType = "1002";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<KdniaoApiClient> _logger;
        private readonly string _eBusinessID;
        private readonly string _appKey;

        public KdniaoApiClient(IHttpClientFactory httpClientFactory, ILogger<KdniaoApiClient> logger, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            var section = configuration.GetSection("KdniaoSettings");
            _eBusinessID = section["EBusinessID"] ?? "";
            _appKey = section["AppKey"] ?? "";
        }

        public async Task<string> QueryAsync(string shipperCode, string logisticCode)
        {
            if (string.IsNullOrEmpty(_eBusinessID) || string.IsNullOrEmpty(_appKey))
            {
                _logger.LogWarning("快递鸟API配置缺失: EBusinessID或AppKey为空");
                return "";
            }

            var requestData = new
            {
                OrderCode = "",
                ShipperCode = shipperCode,
                LogisticCode = logisticCode
            };

            var requestDataJson = JsonConvert.SerializeObject(requestData);
            var dataSign = GenerateDataSign(requestDataJson, _appKey);

            var postData = $"RequestData={Uri.EscapeDataString(requestDataJson)}&EBusinessID={_eBusinessID}&RequestType={RequestType}&DataSign={Uri.EscapeDataString(dataSign)}&DataType=2";

            try
            {
                var client = _httpClientFactory.CreateClient();
                var content = new StringContent(postData, Encoding.UTF8, "application/x-www-form-urlencoded");
                var response = await client.PostAsync(ApiUrl, content);
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadAsStringAsync();
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "快递鸟API调用失败: ShipperCode={ShipperCode}, LogisticCode={LogisticCode}", shipperCode, logisticCode);
                return "";
            }
        }

        private static string GenerateDataSign(string data, string appKey)
        {
            var combined = data + appKey;
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(combined));
            return Convert.ToBase64String(hash);
        }
    }
}
