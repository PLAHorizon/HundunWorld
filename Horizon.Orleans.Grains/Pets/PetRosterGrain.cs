using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Game.Sim.Pets;
using Horizon.Model.GameModel;
using Horizon.Orleans.Interface;
using Horizon.Orleans.Grains.Pets;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains.Pets;

/// <summary>
/// 宠物名册 Grain：从 <c>Game_HunduShijie_PetInstance</c> 读某主人的全部个体，
/// 合成主人反哺账；并作为"出战/附体/收回"的**唯一**写入口。
/// <para>
/// <b>不带 Grain 状态是刻意的</b>：个体表的 <c>pet_instance_id</c> 就是权威（宠物被抓后永久存在，
/// 放生也留档）。若在 Redis 里再存一份名册，就会有两套账——"面板有 6 只而仓库只有 5 只"
/// 这类问题在双轨持久化上查不清（角色位置的双轨已经付过学费，见记忆 <c>position-persistence-dual-tier</c>）。
/// 因此每次直接读表；表就 1 次按主键前缀的查询，量级上不是热点。</para>
/// </summary>
public class PetRosterGrain : Grain, IPetRosterGrain
{
    /// <summary>状态变更的结果码（界面按此分支，文案只作展示）。</summary>
    public const int CodeOk = 0;
    public const int CodeNotFound = 1;
    public const int CodeNotOwner = 2;
    public const int CodeBadTarget = 3;
    public const int CodePossessionFull = 4;
    public const int CodeBondBlocked = 5;
    public const int CodeStoreFailed = 6;

    private readonly ILogger<PetRosterGrain> _logger;
    private readonly IDataContext<GameEntityContext, PetInstanceEntity, long> _instances;
    private readonly IDataContext<GameEntityContext, PetAssistRuleEntity, int> _assistRules;
    private readonly IDataContext<GameEntityContext, PetStatBandEntity, int> _bands;
    private readonly IDataContext<GameEntityContext, PetInstanceEventEntity, int>? _events;

    public PetRosterGrain(
        ILogger<PetRosterGrain> logger,
        IDataContext<GameEntityContext, PetInstanceEntity, long> instances,
        IDataContext<GameEntityContext, PetAssistRuleEntity, int> assistRules,
        IDataContext<GameEntityContext, PetStatBandEntity, int> bands,
        IDataContext<GameEntityContext, PetInstanceEventEntity, int>? events = null)
    {
        _logger = logger;
        _instances = instances;
        _assistRules = assistRules;
        _bands = bands;
        _events = events;
    }

    /// <inheritdoc />
    public async Task<PetRosterSnapshotDto> GetSnapshotAsync()
    {
        var owner = this.GetPrimaryKeyLong();
        var roster = await LoadRosterAsync(owner);
        return roster.Snapshot;
    }

    /// <inheritdoc />
    public async Task<PetDeployResultDto> SetDeployStateAsync(long instanceId, int targetState)
    {
        var owner = this.GetPrimaryKeyLong();
        var target = (PetInstanceState)(byte)targetState;

        // 白名单而不是黑名单：新增状态（未来的"受伤疗养"…）默认不可被客户端一句话切进去。
        if (target is not (PetInstanceState.Owned or PetInstanceState.Following
                           or PetInstanceState.Battling or PetInstanceState.Possessed
                           or PetInstanceState.Stable or PetInstanceState.Released))
        {
            return Fail(CodeBadTarget, $"状态 {targetState} 不在可切换的白名单里（出战/附体/放养/兽栏/收回/放生）");
        }

        PetInstanceEntity? row;
        try
        {
            row = await _instances.QueryFirstOrDefaultAsync(i => i.Id == instanceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PetRosterGrain {Owner}: 读个体 {InstanceId} 失败。", owner, instanceId);
            return Fail(CodeStoreFailed, "读个体行失败（数据库不可达或查询异常），本次未改任何状态");
        }

        if (row == null || !row.IsValid) return Fail(CodeNotFound, $"没有编号 {instanceId} 的宠物个体");
        if (row.OwnerCharacterId != owner) return Fail(CodeNotOwner, $"编号 {instanceId} 的宠不属于当前主人，改不了");

        var current = (PetInstanceState)row.State;
        if (current == target)
        {
            // 幂等：重复点"出战"不该把另一只宠顶下去，也不该白记一条事件。
            var same = await LoadRosterAsync(owner);
            return new PetDeployResultDto
            {
                Success = true,
                Code = CodeOk,
                Message = $"已经是{StateText(target)}了",
                Snapshot = same.Snapshot,
            };
        }

        // 蛋与"非持有态"由内核的默契门统一裁决：这里不复制判据，只翻译结果。
        // 等级差那条门槛要主人的等级，名册 Grain 拿不到，留给上层（见返回值 Issues 的口径）。
        var bond = new PetBondValues
        {
            Loyalty = row.Loyalty,
            Happiness = row.Happiness,
            Lifespan = row.Lifespan,
            Intimacy = row.Intimacy,
        };
        var block = PetBondCore.CheckDeployable(in bond, row.Level, 0,
            isHatched: row.Generation > 0,
            isAvailable: current is not (PetInstanceState.Breeding or PetInstanceState.PendingClaim));
        // 收回/寄存/放生不受出战门槛约束——低忠诚也该能把宠收回来。
        var needsDeployGate = target is PetInstanceState.Battling or PetInstanceState.Following
                                                 or PetInstanceState.Possessed;
        if (needsDeployGate && block != PetBondBlockReason.None)
        {
            return Fail(CodeBondBlocked, $"{StateText(target)}被挡下：{BondBlockText(block)}");
        }

        if (needsDeployGate)
        {
            var owned = await LoadRosterAsync(owner);
            if (target == PetInstanceState.Possessed
                && CountWithState(owned.Rows, PetInstanceState.Possessed) >= PetAiCore.MaxPossessedSlots)
            {
                return Fail(CodePossessionFull,
                    $"附体位只有 {PetAiCore.MaxPossessedSlots} 个，请先收回一只（本次未改任何状态）");
            }
        }

        try
        {
            // 出战位每玩家只许一只：把同主的另一只顶回 Owned，而不是拒绝新命令——
            // 玩家的直观期望是"我点了新宠出战，旧宠自己回栏"（WoW 换宠、梦幻参战位同构）。
            if (target == PetInstanceState.Battling)
            {
                var battlingNow = await _instances.QueryAsync(i => i.OwnerCharacterId == owner
                                                                  && i.State == (byte)PetInstanceState.Battling);
                if (battlingNow != null)
                {
                    // 先物化再写：在枚举查询结果的同时改状态，会让"取旧出战宠"这一步在
                    // 任何按活集合实现的取数器上炸掉（测试夹具第一轮就是这样炸的）。
                    foreach (var other in battlingNow.ToList())
                    {
                        if (other == null || other.Id == instanceId) continue;
                        var demoted = other.Id;
                        other.State = (byte)PetInstanceState.Owned;
                        await _instances.UpdateAsync(other, demoted);
                        await WriteEventAsync(other, PetInstanceState.Owned, 17, "被新出战的宠顶回待命");
                    }
                }
            }

            row.State = (byte)target;
            var ok = await _instances.UpdateAsync(row, instanceId);
            if (!ok) return Fail(CodeStoreFailed, "状态写回失败（更新影响 0 行），数据库里可能仍是旧状态");

            await WriteEventAsync(row, target, 17, $"状态变更：{StateText(current)} → {StateText(target)}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PetRosterGrain {Owner}: 写个体 {InstanceId} 状态失败。", owner, instanceId);
            return Fail(CodeStoreFailed, $"写状态时异常，未确认落库：{ex.GetBaseException().Message}");
        }

        var after = await LoadRosterAsync(owner);
        return new PetDeployResultDto
        {
            Success = true,
            Code = CodeOk,
            Message = $"编号 {instanceId} 已{StateText(target)}",
            Snapshot = after.Snapshot,
        };
    }

    private PetDeployResultDto Fail(int code, string message)
        => new() { Success = false, Code = code, Message = message };

    /// <summary>一次装载：读三张表 → 折成内核名册 → 聚合反哺 → 出 DTO。</summary>
    private async Task<(PetRosterEntry[] Rows, PetRosterSnapshotDto Snapshot)> LoadRosterAsync(long owner)
    {
        List<PetInstanceEntity>? rows = null;
        List<PetAssistRuleEntity>? rules = null;
        List<PetStatBandEntity>? bands = null;
        var loadIssue = string.Empty;

        try
        {
            var q = await _instances.QueryAsync(i => i.OwnerCharacterId == owner && i.IsValid);
            rows = q?.ToList() ?? new List<PetInstanceEntity>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PetRosterGrain {Owner}: 读个体表失败，按无名册处理。", owner);
            rows = new List<PetInstanceEntity>();
            loadIssue = "读个体表失败（数据库不可达），本次名册按空处理——这不是\"你没有宠\"";
        }

        try
        {
            var rq = await _assistRules.QueryAsync(r => r.IsValid);
            rules = rq?.ToList() ?? new List<PetAssistRuleEntity>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PetRosterGrain {Owner}: 读辅助规则表失败，辅主技一律不计。", owner);
            rules = new List<PetAssistRuleEntity>();
            loadIssue = AppendIssue(loadIssue, "读辅助规则表失败，辅主特技本轮全部没算进加成");
        }

        try
        {
            var bq = await _bands.QueryAsync(b => b.IsValid);
            bands = bq?.ToList() ?? new List<PetStatBandEntity>();
        }
        catch (Exception ex)
        {
            // 超级灵性只影响"灵性加成选哪张表"，读不到时按普通档算并在 Issues 里说明。
            _logger.LogWarning(ex, "PetRosterGrain {Owner}: 读资质区间表失败，超级灵性按普通档处理。", owner);
            bands = new List<PetStatBandEntity>();
            loadIssue = AppendIssue(loadIssue, "读资质区间表失败，超级灵性按普通档折算");
        }

        var result = PetRosterMapper.Build(rows, rules, bands);
        var bonus = PetAssistCore.AggregateOwnerBonuses(result.ActivePets, out var excluded, out var exclusionReason);

        var issues = loadIssue;
        if (result.Rejected > 0)
            issues = AppendIssue(issues, $"{result.Rejected} 只个体编号与物种不符，已整条挡下：{result.FirstRejection}");
        if (result.Degraded > 0)
        {
            var first = FirstIssue(result.Entries);
            issues = AppendIssue(issues, $"{result.Degraded} 只的 JSON 列读坏了（技能/装备/临时变异按空处理）：{first}");
        }
        if (excluded > 0)
            issues = AppendIssue(issues, $"{excluded} 条加成被挡下：{exclusionReason}");

        var snapshot = new PetRosterSnapshotDto
        {
            RosterCount = result.Entries.Length,
            // 被聚合层挡下的那些不算"参与反哺"，否则计数会比面板上的加成来源多。
            Contributors = result.ActivePets.Length - excluded,
            Excluded = excluded,
            Rejected = result.Rejected,
            Degraded = result.Degraded,
            Issues = issues,
            Strength = bonus.Strength,
            Spirit = bonus.Spirit,
            Vitality = bonus.Vitality,
            Agility = bonus.Agility,
            Steadiness = bonus.Steadiness,
            HpFlat = bonus.HpFlat,
            ManaRegen = bonus.ManaRegen,
            CritTankChanceBps = bonus.CritTankChanceBps,
            DamageLeechBps = bonus.DamageLeechBps,
            ReadSideVersion = PetRosterMapper.Version,
        };
        return (result.Entries, snapshot);
    }

    /// <summary>写一条个体事件（审计用）。事件表没接或写失败都不该拖累状态变更本身，但要留话。</summary>
    private async Task WriteEventAsync(PetInstanceEntity row, PetInstanceState to, byte kind, string note)
    {
        if (_events == null) return;
        try
        {
            await _events.AddAsync(new PetInstanceEventEntity
            {
                PetInstanceId = row.Id,
                EventKind = kind,
                OccurredAt = DateTime.UtcNow,
                PayloadJson = $"{{\"state\":{(byte)to},\"note\":{Escape(note)}}}",
                Seed = 0,
                IsValid = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PetRosterGrain: 个体 {InstanceId} 的事件流水写入失败（状态已改，仅审计缺一行）。", row.Id);
        }
    }

    private static int CountWithState(PetRosterEntry[] rows, PetInstanceState state)
    {
        var n = 0;
        for (var i = 0; i < rows.Length; i++)
            if (rows[i].State == state) n++;
        return n;
    }

    private static string FirstIssue(PetRosterEntry[] rows)
    {
        for (var i = 0; i < rows.Length; i++)
            if (!string.IsNullOrEmpty(rows[i].Issue)) return rows[i].Issue!;
        return string.Empty;
    }

    private static string AppendIssue(string current, string add)
        => string.IsNullOrEmpty(current) ? add : current + "；" + add;

    private static string Escape(string text)
    {
        if (string.IsNullOrEmpty(text)) return "\"\"";
        var sb = new System.Text.StringBuilder(text.Length + 8);
        sb.Append('"');
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c < 0x20) sb.Append(' ');
            else sb.Append(c);
        }
        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>状态的人话名（界面念"已出战"而不是"state=4"）。</summary>
    private static string StateText(PetInstanceState state) => state switch
    {
        PetInstanceState.Owned => "收回待命",
        PetInstanceState.Following => "放养跟随",
        PetInstanceState.Battling => "出战",
        PetInstanceState.Possessed => "附体",
        PetInstanceState.Stable => "寄存兽栏",
        PetInstanceState.Released => "放生",
        PetInstanceState.Egg => "孵化中",
        PetInstanceState.Breeding => "繁殖冷却",
        PetInstanceState.PendingClaim => "待领取",
        _ => state.ToString(),
    };

    private static string BondBlockText(PetBondBlockReason reason) => reason switch
    {
        PetBondBlockReason.NotHatched => "还在蛋里，没孵化",
        PetBondBlockReason.NotAvailable => "当前不在可支配状态（繁殖冷却或待领取）",
        PetBondBlockReason.LowHappiness => "快乐太低（红脸），它不肯上场",
        PetBondBlockReason.LowLifespan => "寿命不足",
        PetBondBlockReason.OverOwnerLevel => "等级超过主人可携带上限",
        _ => "未满足出战门槛",
    };
}
