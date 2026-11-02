using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Security;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using StackExchange.Redis;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// **真集群**取证：把脚本授权面打到正在运行的 Silo 与真实 Redis 上。
/// <para>
/// 与 <see cref="OrleansGrainLiveIntegrationTests"/> 同一套环境门控（未设 <c>HORIZON_ORLEANS_REDIS</c> 即跳过），
/// 因为它需要一个活着的 Silo。这条通路是唯一能在**不开 Flax 客户端**的前提下证明
/// "真 Grain + 真持久层（Redis 热层）+ Version(2) 契约在活进程里可调用"的办法——
/// 本目标其余 128 例都在测试宿主里，照不出"接口没注册上/热层写不进 Redis/审计不落盘"这类只在真进程里出现的问题。
/// </para>
/// <para>
/// <b>刻意只走负向与只读</b>：授予/回收会改真集群里的授权状态，且正向还需要 Silo 侧开启开关与引导名单
/// （那是另一次重启的事）。这里用的是合成角色键，且被拒的指令不会改动任何授权。
/// </para>
/// </summary>
public class GmCommandGrainScriptAuthorityLiveClusterTests
{
    private const long SyntheticOperator = 9_900_777_001;
    private const long SyntheticCaller = 9_900_777_002;
    private const long SyntheticShard = 999;

    private readonly ITestOutputHelper _output;

    public GmCommandGrainScriptAuthorityLiveClusterTests(ITestOutputHelper output) => _output = output;

    private static string? RedisConnectionString => Environment.GetEnvironmentVariable("HORIZON_ORLEANS_REDIS");
    private static bool LiveEnabled => !string.IsNullOrWhiteSpace(RedisConnectionString);
    private static string ClusterId => Environment.GetEnvironmentVariable("HORIZON_ORLEANS_CLUSTER_ID") ?? "dev";
    private static string ServiceId => Environment.GetEnvironmentVariable("HORIZON_ORLEANS_SERVICE_ID") ?? "BaseService";

    private static async Task<(IHost Host, IClusterClient Client)> ConnectAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        var redisOptions = ConfigurationOptions.Parse(RedisConnectionString!);
        builder.UseOrleansClient(client =>
        {
            client.UseRedisClustering(o => o.ConfigurationOptions = redisOptions);
            client.Configure<ClusterOptions>(o =>
            {
                o.ClusterId = ClusterId;
                o.ServiceId = ServiceId;
            });
        });

        var host = builder.Build();
        await host.StartAsync();
        return (host, host.Services.GetRequiredService<IClusterClient>());
    }

    [Fact]
    public async Task Live_授权面在真进程里可调用_未授权角色一律被拒且不发令牌()
    {
        if (!LiveEnabled) return;

        var (host, client) = await ConnectAsync();
        try
        {
            var grain = client.GetGrain<IGmCommandGrain>(0);

            // 1) 契约可达：Version(2) 的新方法在活 Silo 上必须调得通（旧 Silo 混跑会抛 TypeLoad/找不到方法）。
            var snapshot = await grain.GetScriptPermissionsAsync(SyntheticOperator);
            _output.WriteLine($"GetScriptPermissionsAsync(合成角色) => {(snapshot is null ? "null" : $"flags={snapshot.Flags}")}");
            Assert.Null(snapshot);

            // 2) 裁决必须是**结构化拒绝**而不是异常，且绝不发令牌。
            var verdict = await grain.TryConsumeScriptAsync(new GmScriptTicket
            {
                OperatorCharacterId = SyntheticOperator,
                RequestId = 990_001,
                Op = ScriptOp.Spawn,
                Kind = ScriptedKind.Monster,
                TemplateKey = "墨麒麟",
                Count = 1,
                OperatorShardId = SyntheticShard,
            });

            _output.WriteLine($"TryConsumeScriptAsync => Allowed={verdict.Allowed} Reason={verdict.Reason} Message={verdict.Message}");
            Assert.False(verdict.Allowed);
            Assert.Null(verdict.Token);
            // 活 Silo 是否开启开关决定了具体原因，两种都要能解释；出现"通过"才是问题。
            Assert.Contains(verdict.Reason, new[]
            {
                ScriptRejectReason.Disabled,        // ScriptedSpawn__Enabled 未设/为 0
                ScriptRejectReason.NoGrant,         // 已开启，但合成角色从未被授予
            });

            // 3) 授权管理面同样 fail-closed：调用者不持 GmOps 就不能授予。
            var grant = await grain.GrantScriptAsync(
                SyntheticOperator, ScriptPermissionFlags.SpawnMonster, TimeSpan.FromHours(1), SyntheticCaller, "live 取证");
            _output.WriteLine($"GrantScriptAsync(非 GM 调用者) => Success={grant.Success} Error={grant.ErrorMessage}");
            Assert.False(grant.Success);

            // 4) 被拒也要留痕，且**读得回来**——这验的是"审计进持久层"那处改动在真 Redis 上成立。
            var audit = await grain.QueryScriptAuditAsync(SyntheticCaller, 20);
            _output.WriteLine($"QueryScriptAuditAsync(caller) => {audit.Length} 条：{string.Join(" | ", audit.Select(e => e.OperationType))}");
            Assert.Contains(audit, e => e.OperationType == "GrantScript" && e.Details.Contains("拒绝"));

            // 5) 授权状态确实没被那次失败的授予改动过。
            Assert.Null(await grain.GetScriptPermissionsAsync(SyntheticOperator));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task Live_令牌字段与查询面在真进程里自洽()
    {
        if (!LiveEnabled) return;

        var (host, client) = await ConnectAsync();
        try
        {
            var grain = client.GetGrain<IGmCommandGrain>(0);

            // Query 类操作不需要权威位置（它不生成东西）：真进程里必须能走通并给出结构化答复，
            // 而不是被"尚无位置"这类生成面守卫误伤。
            var verdict = await grain.TryConsumeScriptAsync(new GmScriptTicket
            {
                OperatorCharacterId = SyntheticOperator,
                RequestId = 990_002,
                Op = ScriptOp.Query,
                Kind = ScriptedKind.None,
                OperatorShardId = SyntheticShard,
            });

            _output.WriteLine($"TryConsumeScriptAsync(Query) => Allowed={verdict.Allowed} Reason={verdict.Reason} Flags={verdict.EffectiveFlags}");
            Assert.DoesNotContain("权威位置", verdict.Message ?? string.Empty, StringComparison.Ordinal);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}
