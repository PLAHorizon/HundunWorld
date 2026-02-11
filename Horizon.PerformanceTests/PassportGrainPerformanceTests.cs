using NBomber.CSharp;
using NBomber.Contracts;
using Microsoft.Extensions.Logging;
using Orleans.TestingHost;
using Horizon.Orleans.Interface;
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
            var passportId = $"test_user_{Random.Shared.Next(1, 1000)}";
            var grain = _cluster!.GrainFactory.GetGrain<IPassportGrain>(passportId);
            
            // 模拟登录操作
            var result = await grain.AuthenticationAsync(new Horizon.Model.Dto.LoginDto
            {
                Account = passportId,
                Password = "TestPassword123!",
                LoginType = 1
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
        Assert.True(loginStats.Ok.Request.RPS > 40, $"RPS should be > 40, actual: {loginStats.Ok.Request.RPS}");
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
            var passportId = $"new_user_{Guid.NewGuid():N}";
            var grain = _cluster!.GrainFactory.GetGrain<IPassportGrain>(passportId);
            
            // 模拟注册操作
            var result = await grain.RegisterAsync(new Horizon.Model.Dto.RegisterDto
            {
                Account = passportId,
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
        Assert.True(registerStats.Ok.Request.RPS > 15, $"RPS should be > 15, actual: {registerStats.Ok.Request.RPS}");
        Assert.True(registerStats.Ok.Latency.Percent95 < 150, $"P95 latency should be < 150ms, actual: {registerStats.Ok.Latency.Percent95}ms");
    }
    
    public void Dispose()
    {
        _cluster?.StopAllSilosAsync().Wait();
        _cluster?.Dispose();
    }
}

/// <summary>
/// 测试Silo配置
/// </summary>
public class TestSiloConfigurations : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder
            .UseInMemoryReminderService()
            .AddMemoryGrainStorage("Default");
    }
}
