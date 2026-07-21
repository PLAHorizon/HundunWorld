using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Horizon.Orleans.Interface.World;

namespace Horizon.Orleans.Grains.World;

/// <summary>
/// P2.2 副本管理器 Grain 实现（全局单例）。<br/>
/// 负责副本实例的创建/销毁/ID 分配/活跃列表维护。
/// </summary>
public sealed class InstanceManagerGrain : Grain, IInstanceManagerGrain
{
    private readonly ILogger<InstanceManagerGrain> _logger;

    /// <summary>活跃副本注册表（instanceId → 摘要信息）。</summary>
    private readonly Dictionary<long, ActiveInstanceInfo> _activeInstances = new();

    /// <summary>实例 ID 自增计数器。</summary>
    private long _nextInstanceId = 1;

    /// <summary>累计创建数。</summary>
    private long _totalCreated;

    /// <summary>累计销毁数。</summary>
    private long _totalDestroyed;

    /// <summary>最大并发副本数限制。</summary>
    private const int MaxConcurrentInstances = 1000;

    public InstanceManagerGrain(ILogger<InstanceManagerGrain> logger)
    {
        _logger = logger;
    }

    public async Task<CreateInstanceResult> CreateInstanceAsync(CreateInstanceRequest request)
    {
        if (_activeInstances.Count >= MaxConcurrentInstances)
        {
            _logger.LogWarning("副本创建失败：达到最大并发数限制。Active={Active}, Max={Max}",
                _activeInstances.Count, MaxConcurrentInstances);
            return new CreateInstanceResult
            {
                Success = false,
                ErrorMessage = $"服务器副本数已达上限（{MaxConcurrentInstances}），请稍后再试。",
            };
        }

        var instanceId = _nextInstanceId++;
        _totalCreated++;

        // 获取 InstanceGrain 并初始化
        var instanceGrain = GrainFactory.GetGrain<IInstanceGrain>(instanceId);
        var config = new InstanceConfig
        {
            TemplateId = request.TemplateId,
            Name = request.Name,
            Type = request.Type,
            MaxPlayers = request.MaxPlayers,
            TimeoutSeconds = request.TimeoutSeconds,
            CreatorId = request.CreatorId,
            OriginZoneShardId = request.OriginZoneShardId,
            Difficulty = request.Difficulty,
        };

        await instanceGrain.InitializeAsync(config);

        // 注册到活跃列表
        _activeInstances[instanceId] = new ActiveInstanceInfo
        {
            InstanceId = instanceId,
            TemplateId = request.TemplateId,
            Name = request.Name,
            Type = request.Type,
            CurrentPlayers = 0,
            MaxPlayers = request.MaxPlayers,
            Phase = InstancePhase.Waiting,
            CreateTime = DateTime.UtcNow,
        };

        _logger.LogInformation(
            "副本创建成功。InstanceId={InstanceId}, Template={Template}, Name={Name}, Type={Type}, Creator={Creator}",
            instanceId, request.TemplateId, request.Name, request.Type, request.CreatorId);

        return new CreateInstanceResult
        {
            Success = true,
            InstanceId = instanceId,
        };
    }

    public async Task DestroyInstanceAsync(long instanceId, InstanceCloseReason reason)
    {
        if (!_activeInstances.ContainsKey(instanceId))
        {
            _logger.LogWarning("销毁副本失败：副本不存在。InstanceId={InstanceId}", instanceId);
            return;
        }

        var instanceGrain = GrainFactory.GetGrain<IInstanceGrain>(instanceId);
        await instanceGrain.CloseAsync(reason);

        _activeInstances.Remove(instanceId);
        _totalDestroyed++;

        _logger.LogInformation(
            "副本销毁。InstanceId={InstanceId}, Reason={Reason}",
            instanceId, reason);
    }

    public Task<ActiveInstanceInfo[]> GetActiveInstancesAsync()
    {
        return Task.FromResult(_activeInstances.Values.ToArray());
    }

    public async Task<InstanceState?> GetInstanceStateAsync(long instanceId)
    {
        if (!_activeInstances.ContainsKey(instanceId))
            return null;

        var instanceGrain = GrainFactory.GetGrain<IInstanceGrain>(instanceId);
        return await instanceGrain.GetStateAsync();
    }

    public Task<InstanceManagerStats> GetStatsAsync()
    {
        return Task.FromResult(new InstanceManagerStats
        {
            ActiveInstanceCount = _activeInstances.Count,
            TotalCreatedCount = _totalCreated,
            TotalDestroyedCount = _totalDestroyed,
            TotalActivePlayers = _activeInstances.Values.Sum(i => i.CurrentPlayers),
        });
    }
}
