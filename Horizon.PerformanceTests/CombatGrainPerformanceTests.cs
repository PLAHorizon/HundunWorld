using NBomber.CSharp;
using NBomber.Contracts;
using Microsoft.Extensions.Logging;
using Orleans.TestingHost;
using Horizon.Orleans.Interface;
using Xunit;

namespace Horizon.PerformanceTests;

/// <summary>
/// CombatGrain战斗性能测试
/// 目标: <30ms 95分位延迟
/// </summary>
public class CombatGrainPerformanceTests : IDisposable
{
    private TestCluster? _cluster;
    private readonly ILogger<CombatGrainPerformanceTests> _logger;
    
    public CombatGrainPerformanceTests()
    {
        _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CombatGrainPerformanceTests>();
    }
    
    private async Task InitializeCluster()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TestSiloConfigurations>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }
    
    /// <summary>
    /// 战斗性能测试 - 攻击操作
    /// </summary>
    [Fact]
    public async Task CombatGrain_AttackPerformanceTest()
    {
        await InitializeCluster();
        
        var scenario = Scenario.Create("combat_attack_test", async context =>
        {
            var attackerId = $"attacker_{Random.Shared.Next(1, 100)}";
            var targetId = $"target_{Random.Shared.Next(1, 100)}";
            var grain = _cluster!.GrainFactory.GetGrain<ICombatGrain>(attackerId);
            
            // 模拟攻击操作
            var result = await grain.AttackAsync(new Horizon.Game.Message.Network.AttackRequest
            {
                AttackerId = attackerId,
                TargetId = targetId,
                SkillId = 1001,
                Damage = 100.0f
            });
            
            return result != null ? Response.Ok() : Response.Fail();
        })
        .WithWarmUpDuration(TimeSpan.FromSeconds(10))
        .WithLoadSimulations(
            Simulation.Inject(rate: 100, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30))
        );
        
        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .Run();
        
        // 验证性能目标
        var attackStats = stats.ScenarioStats[0];
        Assert.True(attackStats.Ok.Request.RPS > 80, $"RPS should be > 80, actual: {attackStats.Ok.Request.RPS}");
        Assert.True(attackStats.Ok.Latency.Percent95 < 30, $"P95 latency should be < 30ms, actual: {attackStats.Ok.Latency.Percent95}ms");
    }
    
    /// <summary>
    /// 战斗性能测试 - 技能释放
    /// </summary>
    [Fact]
    public async Task CombatGrain_SkillCastPerformanceTest()
    {
        await InitializeCluster();
        
        var scenario = Scenario.Create("combat_skill_cast_test", async context =>
        {
            var casterId = $"caster_{Random.Shared.Next(1, 100)}";
            var targetId = $"target_{Random.Shared.Next(1, 100)}";
            var grain = _cluster!.GrainFactory.GetGrain<ICombatGrain>(casterId);
            
            // 模拟技能释放
            var result = await grain.CastSkillAsync(new Horizon.Game.Message.Network.SkillCastRequest
            {
                CasterId = casterId,
                TargetId = targetId,
                SkillId = Random.Shared.Next(1001, 1010),
                Position = new Horizon.Game.Message.Network.Vector3Message { X = 0, Y = 0, Z = 0 }
            });
            
            return result != null ? Response.Ok() : Response.Fail();
        })
        .WithWarmUpDuration(TimeSpan.FromSeconds(10))
        .WithLoadSimulations(
            Simulation.Inject(rate: 80, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30))
        );
        
        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .Run();
        
        // 验证性能目标
        var skillStats = stats.ScenarioStats[0];
        Assert.True(skillStats.Ok.Request.RPS > 60, $"RPS should be > 60, actual: {skillStats.Ok.Request.RPS}");
        Assert.True(skillStats.Ok.Latency.Percent95 < 40, $"P95 latency should be < 40ms, actual: {skillStats.Ok.Latency.Percent95}ms");
    }
    
    public void Dispose()
    {
        _cluster?.StopAllSilosAsync().Wait();
        _cluster?.Dispose();
    }
}
