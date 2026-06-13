using Horizon.Core.Abstract;
using Horizon.Entities;
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
    public class FlowerApiKeyGrain : Grain, IApiKeyManagementGrain
    {
        private readonly ILogger<FlowerApiKeyGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerApiKey, long> _dataContext;
        private readonly IPersistentState<ApiKeyManagementState> _state;

        public FlowerApiKeyGrain(
            ILogger<FlowerApiKeyGrain> logger,
            IDataContext<FlowerEntityContext, FlowerApiKey, long> dataContext,
            [PersistentState("apikeymgmt", "FlowerStore")] IPersistentState<ApiKeyManagementState> state)
        {
            _logger = logger;
            _dataContext = dataContext;
            _state = state;
        }

        public async Task<ApiKeyInfo> CreateApiKeyAsync(long ownerPassportId, string name, string plan)
        {
            try
            {
                var apiKey = GenerateApiKey(plan);

                var entity = new FlowerApiKey
                {
                    ApiKey = apiKey,
                    Name = name,
                    OwnerPassportId = ownerPassportId,
                    Plan = plan,
                    IsEnabled = true,
                    TotalCallCount = 0,
                    ExpiresAt = plan == "lite" ? DateTime.Now.AddMonths(6) : DateTime.Now.AddYears(1)
                };

                var result = await _dataContext.AddAsync(entity);
                if (result == null)
                {
                    _logger.LogError("创建API Key失败: 数据库保存返回null");
                    return null;
                }

                _logger.LogInformation("创建API Key: KeyId={KeyId}, Owner={OwnerPassportId}, Plan={Plan}", result.Id, ownerPassportId, plan);

                return new ApiKeyInfo
                {
                    KeyId = result.Id,
                    ApiKey = apiKey,
                    Name = name,
                    Plan = plan,
                    IsEnabled = true,
                    ExpiresAt = entity.ExpiresAt
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建API Key失败: Owner={OwnerPassportId}", ownerPassportId);
                throw;
            }
        }

        public async Task<List<ApiKeyInfo>> ListApiKeysAsync(long ownerPassportId)
        {
            try
            {
                var keys = await _dataContext.QueryAsync(k => k.OwnerPassportId == ownerPassportId && !k.IsDeleted);
                var keyList = keys.ToList();

                return keyList.Select(k => new ApiKeyInfo
                {
                    KeyId = k.Id,
                    ApiKey = k.ApiKey[..12] + "..." + k.ApiKey[^4..],
                    Name = k.Name,
                    Plan = k.Plan,
                    IsEnabled = k.IsEnabled,
                    TotalCallCount = k.TotalCallCount,
                    LastCallTime = k.LastCallTime,
                    ExpiresAt = k.ExpiresAt
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取API Key列表失败: Owner={OwnerPassportId}", ownerPassportId);
                throw;
            }
        }

        public async Task<bool> RevokeApiKeyAsync(long keyId, long ownerPassportId)
        {
            try
            {
                var entity = await _dataContext.QueryFirstOrDefaultAsync(
                    k => k.Id == keyId && k.OwnerPassportId == ownerPassportId);

                if (entity == null)
                {
                    _logger.LogWarning("API Key不存在或无权操作: KeyId={KeyId}, Owner={OwnerPassportId}", keyId, ownerPassportId);
                    return false;
                }

                entity.IsEnabled = false;
                await _dataContext.UpdateAsync(entity, entity.Id);

                _logger.LogInformation("撤销API Key: KeyId={KeyId}", keyId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "撤销API Key失败: KeyId={KeyId}", keyId);
                throw;
            }
        }

        public async Task<bool> RecordUsageAsync(string apiKey)
        {
            try
            {
                var entity = await _dataContext.QueryFirstOrDefaultAsync(k => k.ApiKey == apiKey && k.IsEnabled);
                if (entity == null) return false;

                entity.TotalCallCount++;
                entity.LastCallTime = DateTime.Now;
                await _dataContext.UpdateAsync(entity, entity.Id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "记录API Key使用失败");
                return false;
            }
        }

        private static string GenerateApiKey(string plan = "lite")
        {
            var prefix = plan.ToLowerInvariant() switch
            {
                "pro" => "fk_p",
                "team" => "fk_t",
                _ => "fk_l"
            };
            var bytes = RandomNumberGenerator.GetBytes(32);
            var key = Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
            return $"{prefix}_{key}";
        }
    }

    [Serializable]
    [GenerateSerializer]
    public class ApiKeyManagementState
    {
        [Id(0)]
        public Dictionary<long, List<ApiKeyInfo>> OwnerKeys { get; set; } = new();
    }
}
