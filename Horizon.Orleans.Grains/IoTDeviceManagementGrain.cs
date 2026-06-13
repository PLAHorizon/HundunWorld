using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.Flower;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class IoTDeviceManagementGrain : Grain, IIoTDeviceManagementGrain
    {
        private readonly ILogger<IoTDeviceManagementGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerIoTDevice, long> _deviceContext;
        private readonly IDataContext<FlowerEntityContext, FlowerDeviceGroup, long> _groupContext;

        public IoTDeviceManagementGrain(
            ILogger<IoTDeviceManagementGrain> logger,
            IDataContext<FlowerEntityContext, FlowerIoTDevice, long> deviceContext,
            IDataContext<FlowerEntityContext, FlowerDeviceGroup, long> groupContext)
        {
            _logger = logger;
            _deviceContext = deviceContext;
            _groupContext = groupContext;
        }

        public async Task<FlowerIoTDeviceInfo> RegisterDeviceAsync(FlowerIoTDeviceRegisterRequest request)
        {
            try
            {
                var deviceCode = $"DEV-{Guid.NewGuid():N}".Substring(0, 16).ToUpper();
                var greenhouseId = !string.IsNullOrEmpty(request.GreenhouseId) ? request.GreenhouseId : GenerateGreenhouseId();
                var mqttTopic = $"flower/iot/{greenhouseId}/{deviceCode}";
                var apiKey = Guid.NewGuid().ToString("N");

                var entity = new FlowerIoTDevice
                {
                    DeviceCode = deviceCode,
                    DeviceName = request.DeviceName,
                    DeviceType = request.DeviceType,
                    GreenhouseId = greenhouseId,
                    GroupId = request.GroupId,
                    Protocol = request.Protocol,
                    MqttTopic = mqttTopic,
                    ApiKey = apiKey,
                    OnlineStatus = "Offline",
                    FirmwareVersion = "",
                    BindingStatus = "Bound",
                    IsEnabled = true,
                    IsDeleted = false,
                    Location = request.Location,
                    Manufacturer = request.Manufacturer,
                    Model = request.Model,
                    SensorCapabilities = request.SensorCapabilities,
                    Remark = request.Remark,
                    Passport = request.Passport,
                    CreateTime = DateTime.Now,
                    IsValid = true,
                  
                };

                var result = await _deviceContext.AddAsync(entity);
                if (result == null)
                {
                    _logger.LogError("注册IoT设备失败: 数据库保存返回null");
                    return null;
                }

                _logger.LogInformation("注册IoT设备: DeviceCode={DeviceCode}, Name={DeviceName}", deviceCode, request.DeviceName);

                return MapToInfo(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注册IoT设备失败: Name={DeviceName}", request.DeviceName);
                throw;
            }
        }

        public async Task<FlowerIoTDeviceInfo> GetDeviceAsync(string deviceCode, string passportId)
        {
            try
            {
                var device = await _deviceContext.QueryFirstOrDefaultAsync(d => d.DeviceCode == deviceCode && !d.IsDeleted);
                if (device == null) return null;
                if (!string.IsNullOrEmpty(device.Passport) && device.Passport != passportId)
                {
                    _logger.LogWarning("获取设备失败: 设备不属于当前用户 DeviceCode={DeviceCode}", deviceCode);
                    return null;
                }
                return MapToInfo(device);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取IoT设备失败: DeviceCode={DeviceCode}", deviceCode);
                throw;
            }
        }

        public async Task<List<FlowerIoTDeviceInfo>> ListDevicesByGreenhouseAsync(string greenhouseId)
        {
            try
            {
                var devices = await _deviceContext.QueryAsync(d => d.GreenhouseId == greenhouseId && !d.IsDeleted);
                return devices.Select(MapToInfo).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取温室设备列表失败: GreenhouseId={GreenhouseId}", greenhouseId);
                throw;
            }
        }

        public async Task<List<FlowerIoTDeviceInfo>> ListDevicesByGroupAsync(string groupId)
        {
            try
            {
                var devices = await _deviceContext.QueryAsync(d => d.GroupId == groupId && !d.IsDeleted);
                return devices.Select(MapToInfo).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取分组设备列表失败: GroupId={GroupId}", groupId);
                throw;
            }
        }

        public async Task UpdateOnlineStatusAsync(string deviceCode, string status)
        {
            try
            {
                var device = await _deviceContext.QueryFirstOrDefaultAsync(d => d.DeviceCode == deviceCode && !d.IsDeleted);
                if (device != null)
                {
                    device.OnlineStatus = status;
                    await _deviceContext.UpdateAsync(device, device.Id);
                    _logger.LogInformation("更新设备状态: DeviceCode={DeviceCode}, Status={Status}", deviceCode, status);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新设备状态失败: DeviceCode={DeviceCode}", deviceCode);
                throw;
            }
        }

        public async Task UpdateHeartbeatAsync(string deviceCode)
        {
            try
            {
                var device = await _deviceContext.QueryFirstOrDefaultAsync(d => d.DeviceCode == deviceCode && !d.IsDeleted);
                if (device != null)
                {
                    device.OnlineStatus = "Online";
                    device.LastHeartbeatTime = DateTime.Now;
                    await _deviceContext.UpdateAsync(device, device.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新设备心跳失败: DeviceCode={DeviceCode}", deviceCode);
                throw;
            }
        }

        public async Task DeleteDeviceAsync(string deviceCode)
        {
            try
            {
                var device = await _deviceContext.QueryFirstOrDefaultAsync(d => d.DeviceCode == deviceCode && !d.IsDeleted);
                if (device != null)
                {
                    device.IsDeleted = true;
                    await _deviceContext.UpdateAsync(device, device.Id);
                    _logger.LogInformation("删除IoT设备: DeviceCode={DeviceCode}", deviceCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除IoT设备失败: DeviceCode={DeviceCode}", deviceCode);
                throw;
            }
        }

        public async Task<FlowerDeviceGroupInfo> CreateGroupAsync(FlowerDeviceGroupCreateRequest request)
        {
            try
            {
                var greenhouseId = !string.IsNullOrEmpty(request.GreenhouseId) ? request.GreenhouseId : GenerateGreenhouseId();

                var entity = new FlowerDeviceGroup
                {
                    GroupName = request.GroupName,
                    Description = request.Description,
                    GreenhouseId = greenhouseId,
                    IsDeleted = false,
                    Passport = request.Passport,
                    CreateTime = DateTime.Now,
                    IsValid = true,
                   
                };

                var result = await _groupContext.AddAsync(entity);
                if (result == null) return null;

                return new FlowerDeviceGroupInfo
                {
                    Id = result.Id,
                    GroupName = result.GroupName,
                    Description = result.Description,
                    GreenhouseId = result.GreenhouseId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建设备分组失败: GroupName={GroupName}", request.GroupName);
                throw;
            }
        }

        public async Task<List<FlowerDeviceGroupInfo>> ListGroupsAsync(string passport)
        {
            try
            {
                var groups = await _groupContext.QueryAsync(g => (g.Passport == passport || string.IsNullOrEmpty(g.Passport)) && !g.IsDeleted);
                return groups.Select(g => new FlowerDeviceGroupInfo
                {
                    Id = g.Id,
                    GroupName = g.GroupName,
                    Description = g.Description,
                    GreenhouseId = g.GreenhouseId
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取设备分组列表失败: 通行证={passport}", passport);
                throw;
            }
        }

        public async Task DeleteGroupAsync(string groupId, string passportId)
        {
            try
            {
                if (long.TryParse(groupId, out var id))
                {
                    var group = await _groupContext.QueryFirstOrDefaultAsync(g => g.Id == id && !g.IsDeleted);
                    if (group == null) return;
                    if (!string.IsNullOrEmpty(group.Passport) && !string.IsNullOrEmpty(passportId) && group.Passport != passportId)
                    {
                        _logger.LogWarning("删除分组失败: 分组不属于当前用户");
                        return;
                    }
                    group.IsDeleted = true;
                    await _groupContext.UpdateAsync(group, group.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除设备分组失败: GroupId={GroupId}", groupId);
                throw;
            }
        }

        public async Task<FlowerDeviceGroupInfo> RenameGroupAsync(string groupId, string newName, string passportId)
        {
            var group = await _groupContext.QueryFirstOrDefaultAsync(g => g.Id.ToString() == groupId && !g.IsDeleted);
            if (group == null) return null;
            if (!string.IsNullOrEmpty(group.Passport) && !string.IsNullOrEmpty(passportId) && group.Passport != passportId)
            {
                _logger.LogWarning("重命名分组失败: 分组不属于当前用户");
                return null;
            }
            group.GroupName = newName;
            await _groupContext.UpdateAsync(group, group.Id);
            return new FlowerDeviceGroupInfo { Id = group.Id, GroupName = group.GroupName, GreenhouseId = group.GreenhouseId };
        }

        public async Task<FlowerIoTDeviceInfo> BindDeviceAsync(BindDeviceRequest request)
        {
            try
            {
                var device = await _deviceContext.QueryFirstOrDefaultAsync(d => d.DeviceCode == request.DeviceCode && !d.IsDeleted);
                if (device == null)
                {
                    _logger.LogWarning("绑定设备失败: 设备不存在 DeviceCode={DeviceCode}", request.DeviceCode);
                    return null;
                }

                if (device.BindingStatus == "Bound")
                {
                    _logger.LogWarning("绑定设备失败: 设备已绑定到温室 {GreenhouseId}", device.GreenhouseId);
                    return null;
                }

                device.GreenhouseId = request.GreenhouseId;
                device.GroupId = request.GroupId;
                device.BindingStatus = "Bound";
                device.BoundAt = DateTime.Now;

                await _deviceContext.UpdateAsync(device, device.Id);

                try
                {
                    var deviceGrain = GrainFactory.GetGrain<IIoTDeviceGrain>(device.DeviceCode);
                    await deviceGrain.SetThresholdAsync("Temperature", 35);
                    await deviceGrain.SetThresholdAsync("Humidity", 80);
                    await deviceGrain.SetThresholdAsync("Co2Level", 500);
                    await deviceGrain.SetThresholdAsync("LightIntensity", 50000);
                    await deviceGrain.SetThresholdAsync("SoilMoisture", 40);
                }
                catch { }

                _logger.LogInformation("绑定IoT设备: DeviceCode={DeviceCode}, GreenhouseId={GreenhouseId}", request.DeviceCode, request.GreenhouseId);

                return MapToInfo(device);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "绑定IoT设备失败: DeviceCode={DeviceCode}", request.DeviceCode);
                throw;
            }
        }

        public async Task<FlowerIoTDeviceInfo> UnbindDeviceAsync(string deviceCode)
        {
            try
            {
                var device = await _deviceContext.QueryFirstOrDefaultAsync(d => d.DeviceCode == deviceCode && !d.IsDeleted);
                if (device == null) return null;

                device.GreenhouseId = "";
                device.GroupId = "";
                device.BindingStatus = "Unbound";
                device.BoundAt = null;

                await _deviceContext.UpdateAsync(device, device.Id);

                _logger.LogInformation("解绑IoT设备: DeviceCode={DeviceCode}", deviceCode);

                return MapToInfo(device);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解绑IoT设备失败: DeviceCode={DeviceCode}", deviceCode);
                throw;
            }
        }

        public async Task<FlowerIoTDeviceInfo> ChangeDeviceGroupAsync(string deviceCode, string groupId)
        {
            try
            {
                var device = await _deviceContext.QueryFirstOrDefaultAsync(d => d.DeviceCode == deviceCode && !d.IsDeleted);
                if (device == null) return null;

                var group = await _groupContext.QueryFirstOrDefaultAsync(g => g.Id.ToString() == groupId && !g.IsDeleted);
                if (group == null) return null;

                if (!string.IsNullOrEmpty(group.GreenhouseId) && group.GreenhouseId != device.GreenhouseId)
                {
                    _logger.LogWarning("变更设备分组失败: 目标分组不属于当前设备所在的温室. DeviceCode={DeviceCode}, GroupId={GroupId}, GroupGreenhouseId={GroupGH}, DeviceGreenhouseId={DeviceGH}",
                        deviceCode, groupId, group.GreenhouseId, device.GreenhouseId);
                    return null;
                }

                if (string.IsNullOrEmpty(group.GreenhouseId))
                {
                    group.GreenhouseId = device.GreenhouseId;
                    await _groupContext.UpdateAsync(group, group.Id);
                    _logger.LogInformation("自动修复分组GreenhouseId: GroupId={GroupId}, GreenhouseId={GreenhouseId}", groupId, device.GreenhouseId);
                }

                device.GroupId = groupId;
                await _deviceContext.UpdateAsync(device, device.Id);

                _logger.LogInformation("变更设备分组: DeviceCode={DeviceCode}, GroupId={GroupId}", deviceCode, groupId);

                return MapToInfo(device);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "变更设备分组失败: DeviceCode={DeviceCode}", deviceCode);
                throw;
            }
        }

        public async Task<List<string>> GetAllGreenhouseIdsAsync()
        {
            try
            {
                var devices = await _deviceContext.QueryAsync(d => !d.IsDeleted && d.GreenhouseId != "");
                return devices.Select(d => d.GreenhouseId).Distinct().ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取所有温室ID失败");
                throw;
            }
        }

        public async Task<List<FlowerIoTDeviceInfo>> ListAllDevicesAsync()
        {
            try
            {
                var devices = await _deviceContext.QueryAsync(d => !d.IsDeleted);
                return devices.Select(MapToInfo).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取所有IoT设备列表失败");
                throw;
            }
        }

        private static string GenerateGreenhouseId()
        {
            Span<byte> bytes = stackalloc byte[2];
            RandomNumberGenerator.Fill(bytes);
            var randomPart = BitConverter.ToUInt16(bytes) % 10000;
            return $"GH{DateTime.Now:yyyyMMddHHmmss}{randomPart:D4}";
        }

        private static FlowerIoTDeviceInfo MapToInfo(FlowerIoTDevice d) => new()
        {
            Id = d.Id,
            DeviceCode = d.DeviceCode,
            DeviceName = d.DeviceName,
            DeviceType = d.DeviceType,
            GreenhouseId = d.GreenhouseId,
            GroupId = d.GroupId,
            Protocol = d.Protocol,
            MqttTopic = d.MqttTopic,
            ApiKey = d.ApiKey,
            OnlineStatus = d.OnlineStatus,
            FirmwareVersion = d.FirmwareVersion,
            LastHeartbeatTime = d.LastHeartbeatTime,
            IsEnabled = d.IsEnabled,
            BindingStatus = d.BindingStatus,
            BoundAt = d.BoundAt,
            Location = d.Location,
            Manufacturer = d.Manufacturer,
            Model = d.Model,
            SerialNumber = d.SerialNumber,
            BatteryLevel = d.BatteryLevel,
            SignalStrength = d.SignalStrength,
            SensorCapabilities = d.SensorCapabilities,
            InstallDate = d.InstallDate,
            Remark = d.Remark,
            Passport = d.Passport
        };
    }
}
