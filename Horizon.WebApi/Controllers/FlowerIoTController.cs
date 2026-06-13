using Horizon.Core.Abstract;
using Horizon.Core.Options;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Horizon.Share.VMs;
using Horizon.WebApi.Configs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.WebApi.Controllers
{
    [ApiGroup(ApiGroupName.Flower)]
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class FlowerIoTController : OrleansControllerBase
    {
        private readonly ILogger<FlowerIoTController> _logger;
        private readonly IPassportCurrentUser _passportCurrent;

        public FlowerIoTController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerIoTController> logger,
            IClusterClient clusterClient,
            IPassportCurrentUser passportCurrent)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
            _passportCurrent = passportCurrent;
        }

        private async Task<string> GetUserGreenhouseIdAsync(IClusterClient client)
        {
            var passportId = _passportCurrent.PassportId;
            if (string.IsNullOrEmpty(passportId)) return "";

            try
            {
                var grain = client.GetGrain<IIoTDeviceManagementGrain>(passportId);
                var allDevices = await grain.ListAllDevicesAsync();
                var userDevice = allDevices.FirstOrDefault(d =>
                    !string.IsNullOrEmpty(d.GreenhouseId) &&
                    d.Passport == passportId);
                if (userDevice != null && !string.IsNullOrEmpty(userDevice.GreenhouseId))
                    return userDevice.GreenhouseId;
            }
            catch { }

            return passportId;
        }

        [HttpPost("devices")]
        public async Task<ResultVM<FlowerIoTDeviceInfo>> RegisterDeviceAsync([FromBody] RegisterDeviceRequest request)
        {
            var result = new ResultVM<FlowerIoTDeviceInfo>();
            try
            {
                var client = await OrleansConnectClient();
                var greenhouseId = await GetUserGreenhouseIdAsync(client);
                var grain = client.GetGrain<IIoTDeviceManagementGrain>(greenhouseId);
                result.Data = await grain.RegisterDeviceAsync(new FlowerIoTDeviceRegisterRequest
                {
                    DeviceName = request.DeviceName ?? "",
                    DeviceType = request.DeviceType ?? "Sensor",
                    GreenhouseId = greenhouseId,
                    GroupId = request.GroupId ?? "",
                    Protocol = request.Protocol ?? "MQTT",
                    Location = request.Location ?? "",
                    Manufacturer = request.Manufacturer ?? "",
                    Model = request.Model ?? "",
                    SensorCapabilities = request.SensorCapabilities ?? "",
                    Remark = request.Remark ?? "",
                    Passport = _passportCurrent.PassportId

                });
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注册IoT设备失败: DeviceName={DeviceName}", request.DeviceName);
                result.ErrorMessage = "注册IoT设备失败";
            }
            return result;
        }

        [HttpGet("devices/{deviceCode}")]
        public async Task<ResultVM<FlowerIoTDeviceInfo>> GetDeviceAsync(string deviceCode)
        {
            var result = new ResultVM<FlowerIoTDeviceInfo>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IIoTDeviceManagementGrain>(await GetUserGreenhouseIdAsync(client));
                result.Data = await grain.GetDeviceAsync(deviceCode, _passportCurrent.PassportId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取IoT设备失败: DeviceCode={DeviceCode}", deviceCode);
                result.ErrorMessage = "获取IoT设备失败";
            }
            return result;
        }

        [HttpGet("devices/greenhouse/{greenhouseId}")]
        public async Task<ResultVM<List<FlowerIoTDeviceInfo>>> ListDevicesByGreenhouseAsync(string greenhouseId)
        {
            var result = new ResultVM<List<FlowerIoTDeviceInfo>>();
            try
            {
                var client = await OrleansConnectClient();
                var userGreenhouseId = await GetUserGreenhouseIdAsync(client);
                if (!string.IsNullOrEmpty(greenhouseId) && greenhouseId != userGreenhouseId)
                {
                    result.Data = new List<FlowerIoTDeviceInfo>();
                    result.IsSuccess = true;
                    return result;
                }
                var grain = client.GetGrain<IIoTDeviceManagementGrain>(userGreenhouseId);
                result.Data = await grain.ListDevicesByGreenhouseAsync(userGreenhouseId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取温室设备列表失败: GreenhouseId={GreenhouseId}", greenhouseId);
                result.ErrorMessage = "获取温室设备列表失败";
            }
            return result;
        }

        [HttpGet("greenhouses")]
        public async Task<ResultVM<List<string>>> GetGreenhousesAsync()
        {
            var result = new ResultVM<List<string>>();
            try
            {
                var client = await OrleansConnectClient();
                var userGreenhouseId = await GetUserGreenhouseIdAsync(client);
                result.Data = string.IsNullOrEmpty(userGreenhouseId) ? new List<string>() : new List<string> { userGreenhouseId };
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取温室列表失败");
                result.ErrorMessage = "获取温室列表失败";
            }
            return result;
        }

        [HttpPut("devices/{deviceCode}/status")]
        public async Task<ResultVM<object>> UpdateOnlineStatusAsync(string deviceCode, [FromBody] UpdateDeviceStatusRequest request)
        {
            var result = new ResultVM<object>();
            try
            {
                var client = await OrleansConnectClient();
                var greenhouseId = await GetUserGreenhouseIdAsync(client);
                var grain = client.GetGrain<IIoTDeviceManagementGrain>(greenhouseId);
                await grain.UpdateOnlineStatusAsync(deviceCode, request.Status ?? "Online");
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新设备状态失败: DeviceCode={DeviceCode}", deviceCode);
                result.ErrorMessage = "更新设备状态失败";
            }
            return result;
        }

        [HttpPut("devices/{deviceCode}/heartbeat")]
        public async Task<ResultVM<object>> UpdateHeartbeatAsync(string deviceCode)
        {
            var result = new ResultVM<object>();
            try
            {
                var client = await OrleansConnectClient();
                var greenhouseId = await GetUserGreenhouseIdAsync(client);
                var grain = client.GetGrain<IIoTDeviceManagementGrain>(greenhouseId);
                await grain.UpdateHeartbeatAsync(deviceCode);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新设备心跳失败: DeviceCode={DeviceCode}", deviceCode);
                result.ErrorMessage = "更新设备心跳失败";
            }
            return result;
        }

        [HttpDelete("devices/{deviceCode}")]
        public async Task<ResultVM<object>> DeleteDeviceAsync(string deviceCode)
        {
            var result = new ResultVM<object>();
            try
            {
                var client = await OrleansConnectClient();
                var greenhouseId = await GetUserGreenhouseIdAsync(client);
                var grain = client.GetGrain<IIoTDeviceManagementGrain>(greenhouseId);
                await grain.DeleteDeviceAsync(deviceCode);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除IoT设备失败: DeviceCode={DeviceCode}", deviceCode);
                result.ErrorMessage = "删除IoT设备失败";
            }
            return result;
        }

        [HttpPost("groups")]
        public async Task<ResultVM<FlowerDeviceGroupInfo>> CreateGroupAsync([FromBody] CreateDeviceGroupRequest request)
        {
            var result = new ResultVM<FlowerDeviceGroupInfo>();
            try
            {
                var client = await OrleansConnectClient();
                var greenhouseId = await GetUserGreenhouseIdAsync(client);
                var grain = client.GetGrain<IIoTDeviceManagementGrain>(greenhouseId);
                result.Data = await grain.CreateGroupAsync(new FlowerDeviceGroupCreateRequest
                {
                    GroupName = request.GroupName ?? "",
                    Description = request.Description ?? "",
                    GreenhouseId = greenhouseId,
                    Passport = _passportCurrent.PassportId
                });
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建设备分组失败: GroupName={GroupName}", request.GroupName);
                result.ErrorMessage = "创建设备分组失败";
            }
            return result;
        }

        [HttpGet("groups/{greenhouseId}")]
        public async Task<ResultVM<List<FlowerDeviceGroupInfo>>> ListGroupsAsync(string greenhouseId)
        {
            var result = new ResultVM<List<FlowerDeviceGroupInfo>>();
            try
            {
                var client = await OrleansConnectClient();
                var userGreenhouseId = await GetUserGreenhouseIdAsync(client);
                var grain = client.GetGrain<IIoTDeviceManagementGrain>(userGreenhouseId);
                result.Data = await grain.ListGroupsAsync(_passportCurrent.PassportId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取设备分组列表失败: GreenhouseId={GreenhouseId}", greenhouseId);
                result.ErrorMessage = "获取设备分组列表失败";
            }
            return result;
        }

        [HttpDelete("groups/{groupId}")]
        public async Task<ResultVM<object>> DeleteGroupAsync(string groupId)
        {
            var result = new ResultVM<object>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IIoTDeviceManagementGrain>(await GetUserGreenhouseIdAsync(client));
                await grain.DeleteGroupAsync(groupId, _passportCurrent.PassportId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除设备分组失败: GroupId={GroupId}", groupId);
                result.ErrorMessage = "删除设备分组失败";
            }
            return result;
        }

        [HttpPut("groups/{groupId}")]
        public async Task<ResultVM<FlowerDeviceGroupInfo>> RenameGroupAsync(string groupId, [FromBody] RenameGroupRequest request)
        {
            var result = new ResultVM<FlowerDeviceGroupInfo>();
            var client = await OrleansConnectClient();
            var managementGrain = client.GetGrain<IIoTDeviceManagementGrain>(await GetUserGreenhouseIdAsync(client));
            result.Data = await managementGrain.RenameGroupAsync(groupId, request.NewName, _passportCurrent.PassportId);
            result.IsSuccess = result.Data != null;
            return result;
        }

        [HttpPost("devices/bind")]
        public async Task<ResultVM<FlowerIoTDeviceInfo>> BindDeviceAsync([FromBody] BindDeviceApiRequest request)
        {
            var result = new ResultVM<FlowerIoTDeviceInfo>();
            try
            {
                var client = await OrleansConnectClient();
                var greenhouseId = await GetUserGreenhouseIdAsync(client);
                var grain = client.GetGrain<IIoTDeviceManagementGrain>(greenhouseId);
                result.Data = await grain.BindDeviceAsync(new BindDeviceRequest
                {
                    DeviceCode = request.DeviceCode ?? "",
                    GreenhouseId = greenhouseId,
                    GroupId = request.GroupId ?? ""
                });
                if (result.Data == null)
                {
                    result.ErrorMessage = "绑定设备失败，设备不存在或已绑定";
                }
                else
                {
                    result.IsSuccess = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "绑定IoT设备失败: DeviceCode={DeviceCode}", request.DeviceCode);
                result.ErrorMessage = "绑定IoT设备失败";
            }
            return result;
        }

        [HttpPost("devices/{deviceCode}/unbind")]
        public async Task<ResultVM<FlowerIoTDeviceInfo>> UnbindDeviceAsync(string deviceCode)
        {
            var result = new ResultVM<FlowerIoTDeviceInfo>();
            try
            {
                var client = await OrleansConnectClient();
                var greenhouseId = await GetUserGreenhouseIdAsync(client);
                var grain = client.GetGrain<IIoTDeviceManagementGrain>(greenhouseId);
                result.Data = await grain.UnbindDeviceAsync(deviceCode);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解绑IoT设备失败: DeviceCode={DeviceCode}", deviceCode);
                result.ErrorMessage = "解绑IoT设备失败";
            }
            return result;
        }

        [HttpPut("devices/{deviceCode}/group")]
        public async Task<ResultVM<FlowerIoTDeviceInfo>> ChangeDeviceGroupAsync(string deviceCode, [FromBody] ChangeGroupRequest request)
        {
            var result = new ResultVM<FlowerIoTDeviceInfo>();
            try
            {
                var client = await OrleansConnectClient();
                var greenhouseId = await GetUserGreenhouseIdAsync(client);
                var grain = client.GetGrain<IIoTDeviceManagementGrain>(greenhouseId);
                result.Data = await grain.ChangeDeviceGroupAsync(deviceCode, request.GroupId ?? "");
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "变更设备分组失败: DeviceCode={DeviceCode}", deviceCode);
                result.ErrorMessage = "变更设备分组失败";
            }
            return result;
        }

        [HttpGet("devices/{deviceCode}/twin")]
        public async Task<ResultVM<DeviceTwinInfo>> GetDeviceTwinAsync(string deviceCode)
        {
            var result = new ResultVM<DeviceTwinInfo>();
            try
            {
                var client = await OrleansConnectClient();
                var deviceGrain = client.GetGrain<IIoTDeviceGrain>(deviceCode);
                result.Data = await deviceGrain.GetDeviceTwinAsync();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取设备孪生失败: DeviceCode={DeviceCode}", deviceCode);
                result.ErrorMessage = "获取设备孪生失败";
            }
            return result;
        }

        [HttpPost("devices/{deviceCode}/command")]
        public async Task<ResultVM<object>> SendDeviceCommandAsync(string deviceCode, [FromBody] SendCommandApiRequest request)
        {
            var result = new ResultVM<object>();
            try
            {
                var client = await OrleansConnectClient();
                var deviceGrain = client.GetGrain<IIoTDeviceGrain>(deviceCode);
                await deviceGrain.SendCommandAsync(request.Action ?? "", request.Payload ?? "");
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送设备命令失败: DeviceCode={DeviceCode}, Action={Action}", deviceCode, request.Action);
                result.ErrorMessage = "发送设备命令失败";
            }
            return result;
        }

        [HttpPut("devices/{deviceCode}/threshold")]
        public async Task<ResultVM<object>> SetThresholdAsync(string deviceCode, [FromBody] SetThresholdRequest request)
        {
            var result = new ResultVM<object>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IIoTDeviceGrain>(deviceCode);
                await grain.SetThresholdAsync(request.MetricName ?? "Temperature", request.Threshold);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置阈值失败: DeviceCode={DeviceCode}", deviceCode);
                result.ErrorMessage = "设置阈值失败";
            }
            return result;
        }

        [HttpGet("devices/{deviceCode}/thresholds")]
        public async Task<ResultVM<Dictionary<string, double>>> GetThresholdsAsync(string deviceCode)
        {
            var result = new ResultVM<Dictionary<string, double>>();
            try
            {
                var client = await OrleansConnectClient();
                var deviceGrain = client.GetGrain<IIoTDeviceGrain>(deviceCode);
                result.Data = await deviceGrain.GetThresholdsAsync();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取设备阈值失败: DeviceCode={DeviceCode}", deviceCode);
                result.ErrorMessage = "获取设备阈值失败";
            }
            return result;
        }
    }

    public class SetThresholdRequest
    {
        public string MetricName { get; set; } = "Temperature";
        public double Threshold { get; set; }
    }

    public class RegisterDeviceRequest
    {
        public string DeviceName { get; set; } = "";
        public string DeviceType { get; set; } = "Sensor";
        public string GreenhouseId { get; set; } = "";
        public string GroupId { get; set; } = "";
        public string Protocol { get; set; } = "MQTT";
        public string Location { get; set; }
        public string Manufacturer { get; set; }
        public string Model { get; set; }
        public string SensorCapabilities { get; set; }
        public string Remark { get; set; }
    }

    public class UpdateDeviceStatusRequest
    {
        public string Status { get; set; } = "Online";
    }

    public class CreateDeviceGroupRequest
    {
        public string GroupName { get; set; } = "";
        public string Description { get; set; } = "";
        public string GreenhouseId { get; set; } = "";
    }

    public class BindDeviceApiRequest
    {
        public string DeviceCode { get; set; } = "";
        public string GreenhouseId { get; set; } = "";
        public string GroupId { get; set; } = "";
    }

    public class ChangeGroupRequest
    {
        public string GroupId { get; set; } = "";
    }

    public class RenameGroupRequest { public string NewName { get; set; } }

    public class SendCommandApiRequest
    {
        public string Action { get; set; } = "";
        public string Payload { get; set; } = "";
    }
}
