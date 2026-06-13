using NBomber.CSharp;
using NBomber.Contracts;
using Microsoft.Extensions.Logging;
using Orleans.TestingHost;
using Horizon.Orleans.Interface;
using Horizon.Game.Message.Network;
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
            var grainId = Guid.NewGuid();
            var grain = _cluster!.GrainFactory.GetGrain<ICombatGrain>(grainId);
            
            // 模拟攻击操作
            var result = await grain.ProcessAttackAsync(new AttackMessage
            {
                AttackerId = (ulong)Random.Shared.Next(1, 100),
                TargetId = (ulong)Random.Shared.Next(1, 100),
                SkillId = 1001,
                Damage = 100
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
        var attackRps = attackStats.Ok.Request.RPS + attackStats.Fail.Request.RPS;
        Assert.True(attackRps > 80, $"RPS should be > 80, actual: {attackRps}");
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
            var grainId = Guid.NewGuid();
            var grain = _cluster!.GrainFactory.GetGrain<ICombatGrain>(grainId);
            
            // 模拟技能释放
            var result = await grain.ProcessSkillCastAsync(new SkillCastMessage
            {
                CasterId = (ulong)Random.Shared.Next(1, 100),
                SkillId = Random.Shared.Next(1001, 1010),
                TargetIds = new List<ulong> { (ulong)Random.Shared.Next(1, 100) },
                CastPosition = new Position { X = 0, Y = 0, Z = 0 }
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
        var skillRps = skillStats.Ok.Request.RPS + skillStats.Fail.Request.RPS;
        Assert.True(skillRps > 60, $"RPS should be > 60, actual: {skillRps}");
        Assert.True(skillStats.Ok.Latency.Percent95 < 40, $"P95 latency should be < 40ms, actual: {skillStats.Ok.Latency.Percent95}ms");
    }
    
    public void Dispose()
    {
        _cluster?.StopAllSilosAsync().Wait();
        _cluster?.Dispose();
    }
}
