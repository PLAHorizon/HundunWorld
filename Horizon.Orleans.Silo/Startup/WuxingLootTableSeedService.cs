using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Model.GameModel;
using Horizon.Orleans.Interface.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Orleans.Silo.Startup;

/// <summary>
/// 把物品模板表灌成按五行域的掉落表（方案 §14.2：初级书、灵材、信物要靠刷怪产出）。
/// <para>
/// 为什么在启动装配而不是每次击杀现算：掉落表是配置，DS 侧的设计就是把配置取到本地缓存、
/// 在 tick 内确定性掷骰（见 <c>ILootTableConfigSource</c> 的注释）；每杀一问 Grain
/// 会把单 shard 的模拟拖成瓶颈。
/// </para>
/// <para>
/// 两个必须绕开的坑，都是这个仓库里踩过的：
/// ① 本服务是单例，<c>IDataContext</c> 是按作用域注册的 —— 不能从构造函数直接注入，
///    否则宿主在 BuildServiceProvider 校验作用域时报错，整个 Silo 起不来；
/// ② <c>StartAsync</c> 里 Orleans 运行时可能还没就绪，此时拿 Grain 会失败 ——
///    所以真正的装配挂在 <see cref="IHostApplicationLifetime.ApplicationStarted"/> 上。
/// </para>
/// <para>
/// 全程容错：读不到表、某张表写入失败都只记日志。掉落没装配上只是"少一路产出"，
/// 把集群搞崩才是事故。
/// </para>
/// </summary>
public class WuxingLootTableSeedService : IHostedService
{
    private readonly ILogger<WuxingLootTableSeedService> _logger;
    private readonly IClusterClient _clusterClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostApplicationLifetime _lifetime;
    private CancellationTokenSource? _linked;

    public WuxingLootTableSeedService(
        ILogger<WuxingLootTableSeedService> logger,
        IClusterClient clusterClient,
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime lifetime)
    {
        _logger = logger;
        _clusterClient = clusterClient;
        _scopeFactory = scopeFactory;
        _lifetime = lifetime;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.ApplicationStopping);
        // 等宿主真正启动完再动手：那时 Orleans 的运行时与 grain 调用才可用。
        _lifetime.ApplicationStarted.Register(OnStarted);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _linked?.Cancel();
        return Task.CompletedTask;
    }

    private void OnStarted()
    {
        var token = _linked?.Token ?? CancellationToken.None;
        _ = Task.Run(() => SeedAsync(token), token);
    }

    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        try
        {
            var tables = await BuildTablesAsync(cancellationToken).ConfigureAwait(false);
            if (tables is null || tables.Count == 0) return;

            var first = await WuxingLootTableEnsure.EnsureAsync(
                tables,
                domain => _clusterClient.GetGrain<ILootTableGrain>(domain),
                onlyIfAbsent: false,
                cancellationToken).ConfigureAwait(false);
            LogFailures(first);

            _logger.LogInformation(
                "五行域掉落表装配完成：{Tables} 张 / {Entries} 条产出（表 ID = 五行域编号，怪→域见怪物模板的 WuxingDomain 列）",
                first.Seeded.Count, EntriesOf(tables, first.Seeded));

            // 启动装配只活一次激活：LootTableGrain 把配置放在普通字段里（没有 IPersistentState），
            // Orleans 回收空闲激活后就带走了，表现是"刷怪什么都不掉"且两端日志都正常。
            // 所以后续按周期只做"缺则补填"——已有表的域绝不覆盖（见 WuxingLootTableEnsure）。
            await HealLoopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 关闭期
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "五行域掉落表装配整体失败：该路产出暂时为空，不影响其它系统");
        }
    }

    private async Task HealLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(HealInterval, cancellationToken).ConfigureAwait(false);

            var tables = await BuildTablesAsync(cancellationToken).ConfigureAwait(false);
            if (tables is null || tables.Count == 0) continue;

            var result = await WuxingLootTableEnsure.EnsureAsync(
                tables,
                domain => _clusterClient.GetGrain<ILootTableGrain>(domain),
                onlyIfAbsent: true,
                cancellationToken).ConfigureAwait(false);
            LogFailures(result);

            // 只在真的补了东西（或有失败）时说话，否则每五分钟一条同样的日志会把有用信息淹掉。
            if (result.Seeded.Count > 0)
            {
                _logger.LogWarning(
                    "五行域掉落表自愈：补装配 {Seeded} 张（域 {Domains}，原本为空），已有 {Skipped} 张未覆盖",
                    result.Seeded.Count, string.Join(",", result.Seeded), result.Skipped.Count);
            }
        }
    }

    private void LogFailures(WuxingLootTableEnsure.Result result)
    {
        foreach (var kv in result.Errors)
        {
            _logger.LogWarning(kv.Value, "第 {Domain} 域掉落表装配失败，其余表继续", kv.Key);
        }
    }

    private static int EntriesOf(IReadOnlyDictionary<int, LootTableConfig> tables, IEnumerable<int> domains)
    {
        var total = 0;
        foreach (var d in domains)
        {
            if (tables.TryGetValue(d, out var t)) total += t.Entries.Length;
        }
        return total;
    }

    /// <summary>每次装配都重新读库：内容改了不必重启 Silo（缺哪个域就补哪个域）。</summary>
    private async Task<Dictionary<int, LootTableConfig>?> BuildTablesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider
            .GetService<IDataContext<GameEntityContext, ItemTemplateEntity, int>>();
        if (context == null)
        {
            _logger.LogInformation("未注册物品模板上下文，跳过五行域掉落表装配");
            return null;
        }

        var rows = await context.QueryAsync(t => t.IsValid).ConfigureAwait(false);
        var tables = WuxingLootTables.Build(rows);
        if (tables.Count == 0)
        {
            _logger.LogInformation("物品表里没有可刷的产出行（source_detail.via 不含 loot），掉落表保持为空");
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return tables;
    }

    /// <summary>两次装配之间的间隔。</summary>
    /// <remarks>
    /// 不必很短：DS 侧把配置缓存在本地（<c>OrleansLootProvider</c> 只在启动预热时读一次），
    /// 所以真正会被"空表"影响的是**在表为空期间启动的那些 DS**。五分钟够把下一次 DS 重启带上。
    /// </remarks>
    public static readonly TimeSpan HealInterval = TimeSpan.FromMinutes(5);
}

/// <summary>
/// 把装配好的五行域掉落表写进 Grain，并决定"哪些域该写"。
/// <para>
/// 抽成独立类型只为可断言：<see cref="EnsureAsync"/> 不碰 <c>IClusterClient</c>，
/// 靠传入的 provider 拿 <see cref="ILootTableGrain"/>，所以测试能用假 Grain 把
/// "已有表绝不覆盖 / 空表必须补 / 单张失败不影响其余"三条都钉住。
/// </para>
/// </summary>
public static class WuxingLootTableEnsure
{
    /// <summary>
    /// 一次装配的结果。<see cref="Failed"/> 里带域号，由调用方决定怎么记日志——
    /// 本类是纯装配逻辑，不该自己拥有一个日志目标。
    /// </summary>
    public sealed class Result
    {
        public List<int> Seeded { get; } = new();
        public List<int> Skipped { get; } = new();
        public List<int> Failed { get; } = new();

        /// <summary>把失败域上的异常按域收着，交给调用方打印。</summary>
        public Dictionary<int, Exception> Errors { get; } = new();
    }

    public static async Task<Result> EnsureAsync(
        IReadOnlyDictionary<int, LootTableConfig> tables,
        Func<int, ILootTableGrain> grainProvider,
        bool onlyIfAbsent,
        CancellationToken cancellationToken = default)
    {
        if (tables is null) throw new ArgumentNullException(nameof(tables));
        if (grainProvider is null) throw new ArgumentNullException(nameof(grainProvider));

        var result = new Result();

        foreach (var kv in tables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var grain = grainProvider(kv.Key);
            try
            {
                if (onlyIfAbsent)
                {
                    var current = await grain.GetConfigAsync().ConfigureAwait(false);
                    if (current is { Entries: not null } && current.Entries.Length > 0)
                    {
                        result.Skipped.Add(kv.Key);
                        continue;
                    }
                }

                await grain.InitializeAsync(kv.Value).ConfigureAwait(false);
                result.Seeded.Add(kv.Key);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 单张表失败不许带走其余的：少一域产出比整个装配半途抛出更容易看清。
                result.Failed.Add(kv.Key);
                result.Errors[kv.Key] = ex;
            }
        }

        return result;
    }
}
