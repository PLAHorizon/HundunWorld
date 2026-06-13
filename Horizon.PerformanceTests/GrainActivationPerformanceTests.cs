using NBomber.CSharp;
using NBomber.Contracts;
using Microsoft.Extensions.Logging;
using Orleans.TestingHost;
using Horizon.Orleans.Interface;
using Horizon.Share.Dtos.User;
using Horizon.Game.Message.Network;
using Xunit;

namespace Horizon.PerformanceTests;

/// <summary>
/// 并发Grain激活性能测试
/// 目标: >1000 Grain/秒 激活速度
/// </summary>
public class GrainActivationPerformanceTests : IDisposable
{
    private TestCluster? _cluster;
    private readonly ILogger<GrainActivationPerformanceTests> _logger;
    
    public GrainActivationPerformanceTests()
    {
        _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<GrainActivationPerformanceTests>();
    }
    
    private async Task InitializeCluster()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TestSiloConfigurations>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }
    
    /// <summary>
    /// PassportGrain并发激活测试
    /// 模拟大量用户同时登录场景
    /// </summary>
    [Fact]
    public async Task GrainActivation_ConcurrentPassportActivation()
    {
        await InitializeCluster();
        
        var scenario = Scenario.Create("concurrent_passport_activation", async context =>
        {
            // 每次请求使用不同的Grain ID，触发新Grain激活
            var grainId = Guid.NewGuid();
            var grain = _cluster!.GrainFactory.GetGrain<IPassportGrain>(grainId);
            
            var result = await grain.AuthenticationAsync(new LoginDto
            {
                PassportId = grainId.ToString(),
                Password = "TestPassword123!"
            });
            
            return result != null ? Response.Ok() : Response.Fail();
        })
        .WithWarmUpDuration(TimeSpan.FromSeconds(5))
        .WithLoadSimulations(
            Simulation.Inject(rate: 200, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(15))
        );
        
        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .Run();
        
        var activationStats = stats.ScenarioStats[0];
        var passportActivationRps = activationStats.Ok.Request.RPS + activationStats.Fail.Request.RPS;
        Assert.True(passportActivationRps > 100,
            $"Grain activation RPS should be > 100, actual: {passportActivationRps}");
    }
    
    /// <summary>
    /// CombatGrain并发激活测试
    /// 模拟大量战斗同时发生场景
    /// </summary>
    [Fact]
    public async Task GrainActivation_ConcurrentCombatActivation()
    {
        await InitializeCluster();
        
        var scenario = Scenario.Create("concurrent_combat_activation", async context =>
        {
            var grainId = Guid.NewGuid();
            var grain = _cluster!.GrainFactory.GetGrain<ICombatGrain>(grainId);
            
            var result = await grain.ProcessAttackAsync(new AttackMessage
            {
                AttackerId = (ulong)Random.Shared.Next(1, 10000),
                TargetId = (ulong)Random.Shared.Next(1, 10000),
                SkillId = 1001,
                Damage = 50
            });
            
            return result != null ? Response.Ok() : Response.Fail();
        })
        .WithWarmUpDuration(TimeSpan.FromSeconds(5))
        .WithLoadSimulations(
            Simulation.Inject(rate: 200, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(15))
        );
        
        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .Run();
        
        var activationStats = stats.ScenarioStats[0];
        var combatActivationRps = activationStats.Ok.Request.RPS + activationStats.Fail.Request.RPS;
        Assert.True(combatActivationRps > 100,
            $"Combat Grain activation RPS should be > 100, actual: {combatActivationRps}");
    }
    
    /// <summary>
    /// 混合场景并发测试
    /// 模拟同时有登录、战斗、移动的混合场景
    /// </summary>
    [Fact]
    public async Task GrainActivation_MixedScenarioConcurrency()
    {
        await InitializeCluster();
        
        var loginScenario = Scenario.Create("mixed_login", async context =>
        {
            var grainId = Guid.NewGuid();
            var grain = _cluster!.GrainFactory.GetGrain<IPassportGrain>(grainId);
            
            var result = await grain.AuthenticationAsync(new LoginDto
            {
                PassportId = grainId.ToString(),
                Password = "TestPassword123!"
            });
            
            return result != null ? Response.Ok() : Response.Fail();
        })
        .WithWarmUpDuration(TimeSpan.FromSeconds(3))
        .WithLoadSimulations(
            Simulation.Inject(rate: 50, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(20))
        );
        
        var combatScenario = Scenario.Create("mixed_combat", async context =>
        {
            var grainId = Guid.NewGuid();
            var grain = _cluster!.GrainFactory.GetGrain<ICombatGrain>(grainId);
            
            var result = await grain.ProcessAttackAsync(new AttackMessage
            {
                AttackerId = (ulong)Random.Shared.Next(1, 1000),
                TargetId = (ulong)Random.Shared.Next(1, 1000),
                SkillId = 1001,
                Damage = 100
            });
            
            return result != null ? Response.Ok() : Response.Fail();
        })
        .WithWarmUpDuration(TimeSpan.FromSeconds(3))
        .WithLoadSimulations(
            Simulation.Inject(rate: 80, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(20))
        );
        
        var moveScenario = Scenario.Create("mixed_move", async context =>
        {
            var characterId = Random.Shared.NextInt64(1, 1000);
            var grain = _cluster!.GrainFactory.GetGrain<ICharacterGrain>(characterId);
            
            var result = await grain.MoveAsync(new MoveRequest
            {
                CharacterId = (ulong)characterId,
                TargetX = (float)(Random.Shared.NextDouble() * 1000),
                TargetY = 0,
                TargetZ = (float)(Random.Shared.NextDouble() * 1000),
                Speed = 5.0f,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
            
            return result != null ? Response.Ok() : Response.Fail();
        })
        .WithWarmUpDuration(TimeSpan.FromSeconds(3))
        .WithLoadSimulations(
            Simulation.Inject(rate: 100, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(20))
        );
        
        var stats = NBomberRunner
            .RegisterScenarios(loginScenario, combatScenario, moveScenario)
            .Run();
        
        // 混合场景下总吞吐量应超过150 RPS
        var totalRps = stats.ScenarioStats.Sum(s => s.Ok.Request.RPS + s.Fail.Request.RPS);
        Assert.True(totalRps > 150, $"Total mixed scenario RPS should be > 150, actual: {totalRps}");
    }
    
    public void Dispose()
    {
        _cluster?.StopAllSilosAsync().Wait();
        _cluster?.Dispose();
    }
}
