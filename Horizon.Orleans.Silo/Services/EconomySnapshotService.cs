using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Sim.Economy;
using Horizon.Model.GameModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace Horizon.Orleans.Silo.Services;

/// <summary>
/// 经济周期快照（P1）：定期把金币的"总供给 / 累计产出 / 累计消耗 / 账户数 / 账不平行数"落一行。
/// <para>
/// 快照只追加，通胀率由 <c>EconomyCli report</c> 用"本期相对上期"现算——不在服务里缓存上期，
/// 避免"进程重启后上期丢失"和"两份上期各说各话"。
/// </para>
/// <para>
/// 首拍延后 1 分钟（等 Silo/存储就绪），之后每 15 分钟一拍；单拍失败只记日志，不拖垮宿主。
/// </para>
/// </summary>
public sealed class EconomySnapshotService : BackgroundService
{
    private static readonly TimeSpan FirstDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EconomySnapshotService> _logger;

    public EconomySnapshotService(IServiceScopeFactory scopeFactory, ILogger<EconomySnapshotService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await TakeSnapshotAsync(stoppingToken);
                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停机。
        }
    }

    /// <summary>拍一次快照（供自检/手动触发调用）。</summary>
    public async Task TakeSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var currency = scope.ServiceProvider
                .GetRequiredService<IDataContext<GameEntityContext, CurrencyEntity, long>>();
            var db = currency.DbCurrent;

            var rows = await db.Currencies.AsNoTracking()
                .Where(x => x.CurrencyType == WalletCurrency.Gold)
                .ToListAsync(cancellationToken);

            long supply = 0, earned = 0, spent = 0;
            var offBook = 0;
            foreach (var row in rows)
            {
                supply += row.Amount;
                earned += row.TotalEarned;
                spent += row.TotalSpent;
                if (row.Amount != row.TotalEarned - row.TotalSpent) offBook++;
            }

            db.EconomySnapshots.Add(new EconomySnapshotEntity
            {
                CurrencyType = WalletCurrency.Gold,
                Supply = supply,
                Earned = earned,
                Spent = spent,
                Accounts = rows.Count,
                RowsOffBook = offBook,
                TakenAt = DateTime.UtcNow,
                IsValid = true,
            });
            await db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "经济快照：currency={Currency} supply={Supply} earned={Earned} spent={Spent} accounts={Accounts} off_book={OffBook}",
                WalletCurrency.Gold, supply, earned, spent, rows.Count, offBook);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 单拍失败不该拖垮宿主；但必须可检索（"通胀报表断档"要有原因）。
            _logger.LogError(ex, "经济快照失败");
        }
    }
}
