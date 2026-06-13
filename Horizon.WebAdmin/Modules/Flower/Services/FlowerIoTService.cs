using System.Text.Json;
using Horizon.Share.VMs;
using Microsoft.Extensions.Configuration;

namespace Horizon.WebAdmin.Modules.Flower.Services;

public class FlowerIoTService : FlowerApiServiceBase
{
    public FlowerIoTService(HttpClient httpClient, IConfiguration configuration) : base(httpClient, configuration) { }

    public async Task<ResultVM<JsonElement?>?> ListAsync() => await GetAsync<JsonElement?>("FlowerAdmin/iot-devices");

    public async Task<ResultVM<JsonElement?>?> RegisterDeviceAsync(object data) => await PostAsync<JsonElement?>("FlowerIoT/devices", data);

    public async Task<ResultVM<JsonElement?>?> GetDeviceAsync(string deviceCode) => await GetAsync<JsonElement?>($"FlowerIoT/devices/{deviceCode}");

    public async Task<ResultVM<JsonElement?>?> ListDevicesByGreenhouseAsync(string greenhouseId) => await GetAsync<JsonElement?>($"FlowerIoT/devices/greenhouse/{greenhouseId}");

    public async Task<ResultVM<JsonElement?>?> UpdateOnlineStatusAsync(string deviceCode, object data) => await PutAsync<JsonElement?>($"FlowerIoT/devices/{deviceCode}/status", data);

    public async Task<ResultVM<JsonElement?>?> UpdateHeartbeatAsync(string deviceCode) => await PutAsync<JsonElement?>($"FlowerIoT/devices/{deviceCode}/heartbeat");

    public async Task<ResultVM<JsonElement?>?> DeleteDeviceAsync(string deviceCode) => await DeleteAsync<JsonElement?>($"FlowerIoT/devices/{deviceCode}");

    public async Task<ResultVM<JsonElement?>?> CreateGroupAsync(object data) => await PostAsync<JsonElement?>("FlowerIoT/groups", data);

    public async Task<ResultVM<JsonElement?>?> ListGroupsAsync(string greenhouseId) => await GetAsync<JsonElement?>($"FlowerIoT/groups/{greenhouseId}");

    public async Task<ResultVM<JsonElement?>?> DeleteGroupAsync(string groupId) => await DeleteAsync<JsonElement?>($"FlowerIoT/groups/{groupId}");

    public async Task<ResultVM<JsonElement?>?> BindDeviceAsync(object data) => await PostAsync<JsonElement?>("FlowerIoT/devices/bind", data);

    public async Task<ResultVM<JsonElement?>?> UnbindDeviceAsync(string deviceCode) => await PostAsync<JsonElement?>($"FlowerIoT/devices/{deviceCode}/unbind");

    public async Task<ResultVM<JsonElement?>?> ChangeDeviceGroupAsync(string deviceCode, object data) => await PutAsync<JsonElement?>($"FlowerIoT/devices/{deviceCode}/group", data);

    public async Task<ResultVM<JsonElement?>?> GetDeviceTwinAsync(string deviceCode) => await GetAsync<JsonElement?>($"FlowerIoT/devices/{deviceCode}/twin");

    public async Task<ResultVM<JsonElement?>?> SendDeviceCommandAsync(string deviceCode, object data) => await PostAsync<JsonElement?>($"FlowerIoT/devices/{deviceCode}/command", data);

    public async Task<ResultVM<JsonElement?>?> SetThresholdAsync(string deviceCode, object data) => await PutAsync<JsonElement?>($"FlowerIoT/devices/{deviceCode}/threshold", data);
}
