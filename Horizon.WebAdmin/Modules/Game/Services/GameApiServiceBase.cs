using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Game.Services;

public class GameApiServiceBase
{
    private readonly HttpClient _httpClient;
    private readonly string _adminApiKey;

    protected HttpClient Http => _httpClient;

    protected GameApiServiceBase(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _adminApiKey = configuration["AdminAuth:ApiKey"] ?? "";
    }

    protected async Task<ResultVM<T>?> GetAsync<T>(string url)
    {
        try
        {
            ApplyAdminKeyIfConfigured();
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return new ResultVM<T> { Data = default, IsSuccess = false, ErrorMessage = $"请求失败: {(int)response.StatusCode} {response.ReasonPhrase}" };
            }
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<ResultVM<T>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            return new ResultVM<T> { Data = default, IsSuccess = false, ErrorMessage = $"请求异常: {ex.Message}" };
        }
    }

    protected async Task<ResultVM<T>?> PostAsync<T>(string url, object? data = null)
    {
        try
        {
            ApplyAdminKeyIfConfigured();
            var response = await _httpClient.PostAsJsonAsync(url, data);
            if (!response.IsSuccessStatusCode)
            {
                return new ResultVM<T> { Data = default, IsSuccess = false, ErrorMessage = $"请求失败: {(int)response.StatusCode} {response.ReasonPhrase}" };
            }
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<ResultVM<T>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            return new ResultVM<T> { Data = default, IsSuccess = false, ErrorMessage = $"请求异常: {ex.Message}" };
        }
    }

    protected async Task<ResultVM<T>?> PutAsync<T>(string url, object? data = null)
    {
        try
        {
            ApplyAdminKeyIfConfigured();
            var response = await _httpClient.PutAsJsonAsync(url, data);
            if (!response.IsSuccessStatusCode)
            {
                return new ResultVM<T> { Data = default, IsSuccess = false, ErrorMessage = $"请求失败: {(int)response.StatusCode} {response.ReasonPhrase}" };
            }
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<ResultVM<T>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            return new ResultVM<T> { Data = default, IsSuccess = false, ErrorMessage = $"请求异常: {ex.Message}" };
        }
    }

    protected async Task<ResultVM<T>?> DeleteAsync<T>(string url)
    {
        try
        {
            ApplyAdminKeyIfConfigured();
            var response = await _httpClient.DeleteAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return new ResultVM<T> { Data = default, IsSuccess = false, ErrorMessage = $"请求失败: {(int)response.StatusCode} {response.ReasonPhrase}" };
            }
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<ResultVM<T>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            return new ResultVM<T> { Data = default, IsSuccess = false, ErrorMessage = $"请求异常: {ex.Message}" };
        }
    }

    protected void ApplyAdminKeyIfConfigured()
    {
        if (!string.IsNullOrEmpty(_adminApiKey))
        {
            _httpClient.DefaultRequestHeaders.Remove("X-Admin-API-Key");
            _httpClient.DefaultRequestHeaders.Add("X-Admin-API-Key", _adminApiKey);
        }
    }
}
