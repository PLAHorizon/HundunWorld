using System.Globalization;
using System.Text.Json;
using Horizon.Entities;
using Horizon.QuestCli;
using Microsoft.EntityFrameworkCore;

namespace Horizon.EconomyCli;

/// <summary>
/// 经济账本 CLI（P0）：读**持久化**的角色钱包累计，回答"现在有多少钱、产出了多少、消耗了多少、账对不上没有"。
/// <para>
/// 数据源是 <c>CurrencyEntity</c>（<c>CurrencyGrain</c> 用 GameStore 持久化的同一份）：
/// 每行有当前余额 <c>amount</c>、累计获得 <c>total_earned</c>、累计消耗 <c>total_spent</c>。
/// 内存里的 <c>IEconomyMonitorGrain</c> 只做异常检测（Silo 重启清零），不能当账本用。
/// </para>
/// </summary>
public static class Program
{
    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static int Main(string[] args)
    {
        var verb = args.Length > 0 ? args[0] : "help";
        var rest = args.Length > 1 ? args[1..] : Array.Empty<string>();
        try
        {
            return verb switch
            {
                "report" => RunReport(rest),
                "character" => RunCharacter(rest),
                _ => Emit("help", true,
                    "经济账本 CLI：report [--top N] [--currency 1] | character <id> [--currency 1]", null, Array.Empty<string>()),
            };
        }
        catch (Exception ex)
        {
            return Emit(verb, false, $"未处理异常：{ex.GetType().Name}: {ex.Message}", null,
                new[] { ex.StackTrace ?? string.Empty });
        }
    }

    private static int RunReport(string[] rest)
    {
        var map = ParseArgs(rest);
        var top = IntArg(map, "--top", 10);
        var currency = IntArg(map, "--currency", 1);

        if (!TryOpenStore(out var db, out var error)) return Emit("report", false, error, null, Array.Empty<string>());

        using (db)
        {
            var rows = db!.Currencies.AsNoTracking()
                .Where(x => x.CurrencyType == currency)
                .ToList();

            long supply = 0, earned = 0, spent = 0;
            var negative = 0;
            foreach (var row in rows)
            {
                supply += row.Amount;
                earned += row.TotalEarned;
                spent += row.TotalSpent;
                if (row.Amount < 0) negative++;
            }

            // 每行自身也要对得上：余额 = 累计获得 − 累计消耗。单行不平说明有写路径绕过了账本。
            var rowsOffBook = rows.Count(row => row.Amount != row.TotalEarned - row.TotalSpent);
            var net = earned - spent;

            var topHolders = rows
                .Where(x => x.Amount != 0)
                .OrderByDescending(x => x.Amount)
                .Take(Math.Clamp(top, 0, 100))
                .Select(x => new { characterId = x.CharacterId, amount = x.Amount })
                .ToArray();

            // 周期快照：通胀率用"最新两拍"现算（不在服务里缓存上期，避免重启丢历史）。
            var snapshots = db!.EconomySnapshots.AsNoTracking()
                .Where(x => x.CurrencyType == currency)
                .OrderByDescending(x => x.TakenAt)
                .ThenByDescending(x => x.Id)
                .Take(2)
                .ToList();
            var latest = snapshots.Count > 0 ? snapshots[0] : null;
            double? inflationPct = null;
            if (snapshots.Count == 2 && snapshots[1].Supply > 0)
            {
                inflationPct = Math.Round(
                    (snapshots[0].Supply - snapshots[1].Supply) * 100.0 / snapshots[1].Supply, 4);
            }

            var snapshotAgeMinutes = latest == null
                ? (double?)null
                : Math.Round((DateTime.UtcNow - latest.TakenAt).TotalMinutes, 1);

            // ASCII 自证（中文标签会吞掉紧随其后的数字）。
            var line = $"economy_report currency={currency} accounts={rows.Count} supply={supply} " +
                       $"earned={earned} spent={spent} net={net} negative_accounts={negative} " +
                       $"rows_off_book={rowsOffBook} supply_matches_ledger={(supply == net ? 1 : 0)} " +
                       $"snapshot_age_min={(snapshotAgeMinutes.HasValue ? snapshotAgeMinutes.Value.ToString(CultureInfo.InvariantCulture) : "na")} " +
                       $"inflation_pct={(inflationPct.HasValue ? inflationPct.Value.ToString(CultureInfo.InvariantCulture) : "na")}";

            return Emit("report", rowsOffBook == 0 && negative == 0,
                rowsOffBook == 0 && negative == 0 ? line : line + " (账不平，见 rows_off_book/negative_accounts)",
                new
                {
                    currency,
                    accounts = rows.Count,
                    supply,
                    earned,
                    spent,
                    net,
                    negativeAccounts = negative,
                    rowsOffBook,
                    supplyMatchesLedger = supply == net,
                    snapshotAgeMinutes,
                    inflationPct,
                    lastSnapshot = latest == null ? null : new
                    {
                        takenAt = latest.TakenAt,
                        supply = latest.Supply,
                        earned = latest.Earned,
                        spent = latest.Spent,
                        accounts = latest.Accounts,
                        rowsOffBook = latest.RowsOffBook,
                    },
                    topHolders,
                },
                rowsOffBook == 0 && negative == 0 ? Array.Empty<string>() : new[] { "ledger_mismatch" });
        }
    }

    private static int RunCharacter(string[] rest)
    {
        if (rest.Length == 0 || !ulong.TryParse(rest[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var characterId))
            return Emit("character", false, "用法：character <characterId> [--currency 1]", null, Array.Empty<string>());

        var map = ParseArgs(rest.Length > 1 ? rest[1..] : Array.Empty<string>());
        var currency = IntArg(map, "--currency", 1);

        if (!TryOpenStore(out var db, out var error)) return Emit("character", false, error, null, Array.Empty<string>());
        using (db)
        {
            var row = db!.Currencies.AsNoTracking()
                .FirstOrDefault(x => x.CharacterId == characterId && x.CurrencyType == currency);
            if (row == null)
                return Emit("character", true, $"character={characterId} currency={currency} no_wallet=1", null, Array.Empty<string>());

            var balanced = row.Amount == row.TotalEarned - row.TotalSpent;
            return Emit("character", balanced,
                $"character={characterId} currency={currency} amount={row.Amount} earned={row.TotalEarned} " +
                $"spent={row.TotalSpent} balanced={(balanced ? 1 : 0)}",
                new { characterId, currency, amount = row.Amount, earned = row.TotalEarned, spent = row.TotalSpent, balanced },
                balanced ? Array.Empty<string>() : new[] { "wallet_off_book" });
        }
    }

    private static bool TryOpenStore(out GameEntityContext? db, out string error)
    {
        db = null;
        var connectionString = ConnectionConfig.Resolve(out var hint);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            error = "找不到 Game 库连接串：" + hint;
            return false;
        }

        try
        {
            var options = new DbContextOptionsBuilder<GameEntityContext>()
                .UseSqlServer(connectionString)
                .Options;
            db = new GameEntityContext(options);
            _ = db.Currencies.Count();   // 连接串只解析不连库：真正连库在这一步
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = "连库失败：" + ex.GetBaseException().Message;
            db = null;
            return false;
        }
    }

    private static Dictionary<string, string?> ParseArgs(string[] args)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i] ?? string.Empty;
            if (!a.StartsWith("--", StringComparison.Ordinal)) continue;
            var eq = a.IndexOf('=');
            if (eq > 0) map[a[..eq]] = a[(eq + 1)..];
            else map[a] = i + 1 < args.Length ? args[i + 1] : null;
        }

        return map;
    }

    private static int IntArg(Dictionary<string, string?> map, string name, int fallback)
        => map.TryGetValue(name, out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static int Emit(string verb, bool ok, string message, object? data, string[] warnings)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            ok,
            verb,
            message,
            data,
            warnings,
        }, OutputOptions));
        return ok ? 0 : 1;
    }
}
