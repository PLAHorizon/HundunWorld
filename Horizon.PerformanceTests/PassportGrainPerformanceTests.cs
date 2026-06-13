using NBomber.CSharp;
using NBomber.Contracts;
using Microsoft.Extensions.Logging;
using Orleans.TestingHost;
using Horizon.Orleans.Interface;
using Horizon.Share.Dtos.User;
using Xunit;

namespace Horizon.PerformanceTests;

/// <summary>
/// PassportGrain认证性能测试
/// 目标: <100ms 95分位延迟
/// </summary>
public class PassportGrainPerformanceTests : IDisposable
{
    private TestCluster? _cluster;
    private readonly ILogger<PassportGrainPerformanceTests> _logger;
    
    public PassportGrainPerformanceTests()
    {
        _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<PassportGrainPerformanceTests>();
    }
    
    /// <summary>
    /// 初始化Orleans测试集群
    /// </summary>
    private async Task InitializeCluster()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TestSiloConfigurations>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }
    
    /// <summary>
    /// 认证性能测试 - 登录操作
    /// </summary>
    [Fact]
    public async Task PassportGrain_LoginPerformanceTest()
    {
        await InitializeCluster();
        
        var scenario = Scenario.Create("passport_login_test", async context =>
        {
            var grainId = Guid.NewGuid();
            var grain = _cluster!.GrainFactory.GetGrain<IPassportGrain>(grainId);
            
            // 模拟登录操作
            var result = await grain.AuthenticationAsync(new LoginDto
            {
                PassportId = grainId.ToString(),
                Password = "TestPassword123!"
            });
            
            return result != null ? Response.Ok() : Response.Fail();
        })
        .WithWarmUpDuration(TimeSpan.FromSeconds(10))
        .WithLoadSimulations(
            Simulation.Inject(rate: 50, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30))
        );
        
        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .Run();
        
        // 验证性能目标
        var loginStats = stats.ScenarioStats[0];
        var loginRps = loginStats.Ok.Request.RPS + loginStats.Fail.Request.RPS;
        Assert.True(loginRps > 40, $"RPS should be > 40, actual: {loginRps}");
        Assert.True(loginStats.Ok.Latency.Percent95 < 100, $"P95 latency should be < 100ms, actual: {loginStats.Ok.Latency.Percent95}ms");
    }
    
    /// <summary>
    /// 认证性能测试 - 注册操作
    /// </summary>
    [Fact]
    public async Task PassportGrain_RegisterPerformanceTest()
    {
        await InitializeCluster();
        
        var scenario = Scenario.Create("passport_register_test", async context =>
        {
            var grainId = Guid.NewGuid();
            var grain = _cluster!.GrainFactory.GetGrain<IPassportGrain>(grainId);
            
            // 模拟注册操作
            var result = await grain.RegisterAsync(new RegisterDto
            {
                Password = "NewPassword123!",
                Phone = $"1{Random.Shared.Next(300000000, 399999999)}"
            });
            
            return result != null ? Response.Ok() : Response.Fail();
        })
        .WithWarmUpDuration(TimeSpan.FromSeconds(5))
        .WithLoadSimulations(
            Simulation.Inject(rate: 20, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(20))
        );
        
        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .Run();
        
        // 验证性能目标
        var registerStats = stats.ScenarioStats[0];
        var registerRps = registerStats.Ok.Request.RPS + registerStats.Fail.Request.RPS;
        Assert.True(registerRps > 15, $"RPS should be > 15, actual: {registerRps}");
        Assert.True(registerStats.Ok.Latency.Percent95 < 150, $"P95 latency should be < 150ms, actual: {registerStats.Ok.Latency.Percent95}ms");
    }
    
    public void Dispose()
    {
        _cluster?.StopAllSilosAsync().Wait();
        _cluster?.Dispose();
    }
}

