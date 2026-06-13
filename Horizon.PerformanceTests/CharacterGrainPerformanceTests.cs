using NBomber.CSharp;
using NBomber.Contracts;
using Microsoft.Extensions.Logging;
using Orleans.TestingHost;
using Horizon.Orleans.Interface;
using Horizon.Game.Message.Network;
using Xunit;

namespace Horizon.PerformanceTests;

/// <summary>
/// CharacterGrain角色操作性能测试
/// 目标: <50ms 95分位延迟
/// </summary>
public class CharacterGrainPerformanceTests : IDisposable
{
    private TestCluster? _cluster;
    private readonly ILogger<CharacterGrainPerformanceTests> _logger;
    
    public CharacterGrainPerformanceTests()
    {
        _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CharacterGrainPerformanceTests>();
    }
    
    private async Task InitializeCluster()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TestSiloConfigurations>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }
    
    /// <summary>
    /// 角色创建性能测试
    /// </summary>
    [Fact]
    public async Task CharacterGrain_CreateCharacterPerformanceTest()
    {
        await InitializeCluster();
        
        var scenario = Scenario.Create("character_create_test", async context =>
        {
            var characterId = Random.Shared.NextInt64(1, 100000);
            var grain = _cluster!.GrainFactory.GetGrain<ICharacterGrain>(characterId);
            
            var result = await grain.CreateCharacterAsync(new CreateCharacterRequest
            {
                UserId = (ulong)characterId,
                CharacterName = $"TestChar_{characterId}",
                Gender = Random.Shared.Next(0, 2)
            });
            
            return result != null ? Response.Ok() : Response.Fail();
        })
        .WithWarmUpDuration(TimeSpan.FromSeconds(5))
        .WithLoadSimulations(
            Simulation.Inject(rate: 30, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(20))
        );
        
        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .Run();
        
        var createStats = stats.ScenarioStats[0];
        var createRps = createStats.Ok.Request.RPS + createStats.Fail.Request.RPS;
        Assert.True(createRps > 20, $"RPS should be > 20, actual: {createRps}");
        Assert.True(createStats.Ok.Latency.Percent95 < 50, $"P95 latency should be < 50ms, actual: {createStats.Ok.Latency.Percent95}ms");
    }
    
    /// <summary>
    /// 角色移动性能测试
    /// </summary>
    [Fact]
    public async Task CharacterGrain_MovePerformanceTest()
    {
        await InitializeCluster();
        
        var scenario = Scenario.Create("character_move_test", async context =>
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
        .WithWarmUpDuration(TimeSpan.FromSeconds(10))
        .WithLoadSimulations(
            Simulation.Inject(rate: 100, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30))
        );
        
        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .Run();
        
        var moveStats = stats.ScenarioStats[0];
        var moveRps = moveStats.Ok.Request.RPS + moveStats.Fail.Request.RPS;
        Assert.True(moveRps > 80, $"RPS should be > 80, actual: {moveRps}");
        Assert.True(moveStats.Ok.Latency.Percent95 < 30, $"P95 latency should be < 30ms, actual: {moveStats.Ok.Latency.Percent95}ms");
    }
    
    /// <summary>
    /// 角色攻击性能测试
    /// </summary>
    [Fact]
    public async Task CharacterGrain_AttackPerformanceTest()
    {
        await InitializeCluster();
        
        var scenario = Scenario.Create("character_attack_test", async context =>
        {
            var characterId = Random.Shared.NextInt64(1, 1000);
            var grain = _cluster!.GrainFactory.GetGrain<ICharacterGrain>(characterId);
            
            var result = await grain.AttackAsync(new AttackMessage
            {
                AttackerId = (ulong)characterId,
                TargetId = (ulong)Random.Shared.Next(1, 100),
                SkillId = 1001,
                Damage = 100,
                AttackType = 1
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
        
        var attackStats = stats.ScenarioStats[0];
        var attackRps = attackStats.Ok.Request.RPS + attackStats.Fail.Request.RPS;
        Assert.True(attackRps > 60, $"RPS should be > 60, actual: {attackRps}");
        Assert.True(attackStats.Ok.Latency.Percent95 < 50, $"P95 latency should be < 50ms, actual: {attackStats.Ok.Latency.Percent95}ms");
    }
    
    public void Dispose()
    {
        _cluster?.StopAllSilosAsync().Wait();
        _cluster?.Dispose();
    }
}
