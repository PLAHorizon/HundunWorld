using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Sim.Pets;
using Horizon.Model.GameModel;
using Horizon.Orleans.Grains.Pets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 活库判据：真 SQL Server 里的宠物个体行 → 真 EF 取数 → <see cref="PetRosterGrain"/> → 反哺归因量。
/// <para>
/// 环境门：<c>HORIZON_PET_LIVE_DB=1</c>。没开时这条直接返回（与仓库里其它 <c>Live_</c> 测试同一口径）——
/// 也就是说 <b>CI 上它等于没跑</b>，这句话写在 §10.2b 里而不是藏在日志里。</para>
/// <para>
/// 开了门却没数据会**大声失败**（不会安静通过）：那种情况说明有人清了库却忘了这一步依赖真行，
/// 而"跳过"与"通过"同形正是这条测试最不该有的样子（记忆 <c>green-selftest-can-mean-the-line-never-ran</c>）。</para>
/// <para>本测试只读不写。</para>
/// </summary>
public class PetRosterLiveTests
{
    /// <summary>造数据用的测试角色（由 CLI <c>instance add --owner 424242</c> 写入）。</summary>
    private const long TestOwnerCharacterId = 42_4242L;

    /// <summary>环境门的值就是连接串本身（测试工程不引用 PetCli/QuestCli，不去蹭它的 .env 解析）。</summary>
    private static string? LiveGate => Environment.GetEnvironmentVariable("HORIZON_PET_LIVE_DB");

    [Fact]
    public async Task Live_名册Grain对真库里的两只宠算出与手算一致的归因量()
    {
        // 环境门控：没设 HORIZON_PET_LIVE_DB=<连接串> 时这条直接返回（等于没跑，见类注释与 §10.2b）。
        if (string.IsNullOrWhiteSpace(LiveGate)) return;

        var connectionString = LiveGate;
        var options = new DbContextOptionsBuilder<GameEntityContext>()
            .UseSqlServer(connectionString!)
            .Options;
        using var db = new GameEntityContext((DbContextOptions)options);

        // 期望值必须**另算一遍**：从库里的裸资质与等级出发，用测试里手写死的常数推。
        // 如果期望值是调被测函数得到的，那这条判据只会跟着实现一起错。
        var rows = db.PetInstances.AsNoTracking()
            .Where(p => p.OwnerCharacterId == TestOwnerCharacterId && p.IsValid)
            .OrderBy(p => p.Id)
            .ToList();

        Assert.True(rows.Count >= 2,
            $"活库门已开，但角色 {TestOwnerCharacterId} 名下不足 2 只个体（实际 {rows.Count}）。"
            + "请先跑：dotnet run --project Horizon.PetCli -- instance add --species 500003 "
            + $"--owner {TestOwnerCharacterId} --state battling --skill 800901:4 --force");

        var rules = db.PetAssistRules.AsNoTracking().Where(r => r.IsValid).ToList();
        var bands = db.PetStatBands.AsNoTracking().Where(b => b.IsValid).ToList();

        // ── 手算参照 ──
        // 附体位：每级每维 4 点 × 有效资质 / 1000（常数在 PetAssistCore.PossessionLevelCoefficient）。
        // 这里的 4 / 1000 / 2 / 8 / 1 / 3 全部按字面量写，不复用被测代码的常量名。
        var battling = rows.Where(r => r.State == (byte)PetInstanceState.Battling).ToList();
        var possessed = rows.Where(r => r.State == (byte)PetInstanceState.Possessed).ToList();

        var assistSkillIds = rules.Where(r => r.SpeciesId > 0).Select(r => r.SkillId).ToHashSet();
        var expectedStrength = 0;
        foreach (var pet in battling)
        {
            var slots = DecodeSlots(pet.SkillSlotsJson);
            foreach (var slot in slots)
            {
                if (slot.SkillId <= 0 || !assistSkillIds.Contains(slot.SkillId)) continue;
                if (slot.SkillId == 800901) expectedStrength += slot.Level * 5;   // 借力：5 点/级
            }
        }
        var expectedSpirit = 0;
        var expectedVitality = 0;
        var expectedAgility = 0;
        var expectedSteadiness = 0;
        foreach (var pet in possessed)
        {
            expectedStrength += pet.Level * 4 * pet.AttackQuality / 1000;
            expectedSpirit += pet.Level * 4 * pet.ManaQuality / 1000;
            expectedVitality += pet.Level * 4 * pet.StaminaQuality / 1000;
            expectedAgility += pet.Level * 4 * pet.SpeedQuality / 1000;
            expectedSteadiness += pet.Level * 4 * pet.DefenseQuality / 1000;
        }

        // ── 被测通路：真 EF 上下文喂进名册 Grain ──
        var grain = CreateRosterGrain(db);
        // 名册按角色键取数：这一句先钉住"Grain 的键确实是角色 id"，
        // 否则下面的 0 行既可能是键错了、也可能是谓词错了，两种病看不出来。
        Assert.Equal(TestOwnerCharacterId, grain.GetPrimaryKeyLong());
        var snapshot = await grain.GetSnapshotAsync();

        // 失败消息要能直接说出"差在哪一层"：库里几行、Grain 读到几行、被挡下几行、有没有报问题。
        Assert.True(rows.Count == snapshot.RosterCount,
            $"库里 {rows.Count} 行，Grain 读到 {snapshot.RosterCount} 行"
            + $"（Rejected={snapshot.Rejected} Degraded={snapshot.Degraded} Contributors={snapshot.Contributors}）；"
            + $"Issues={snapshot.Issues}");
        Assert.Equal(battling.Count + possessed.Count, snapshot.Contributors);
        Assert.Equal(expectedStrength, snapshot.Strength);
        Assert.Equal(expectedSpirit, snapshot.Spirit);
        Assert.Equal(expectedVitality, snapshot.Vitality);
        Assert.Equal(expectedAgility, snapshot.Agility);
        Assert.Equal(expectedSteadiness, snapshot.Steadiness);
        // 附体宠的技能不吃辅主技（inBattle=false），忠心只在出战那只身上才算——所以这里应恒 0
        Assert.Equal(0, snapshot.CritTankChanceBps);
        Assert.Equal(string.Empty, snapshot.Issues);

        // ── 面板归因量：主人基础值全 0，只剩宠物那一份 ──
        var display = PetBonusProjection.Project(snapshot);
        Assert.Equal(expectedStrength * 2 + expectedSpirit * 2, display.Attack);
        Assert.Equal(expectedVitality * 8, display.Health);
        Assert.Equal(expectedSteadiness * 2 + expectedVitality * 1, display.Defense);
        Assert.Equal(expectedAgility * 3, display.Hit);
        Assert.True(display.Attack > 0, "真库里既然有出战/附体的宠，归因量不该是 0");
    }

    private static List<PetSkillSlot> DecodeSlots(string json)
    {
        Assert.True(PetInstanceCodec.TryDecodeSkillSlots(json, out var slots, out var error),
            $"个体表里的 skill_slots_json 读不出来：{error}（列内容={json}）");
        return slots.ToList();
    }

    /// <summary>用真 GameEntityContext 包出 Grain 需要的四个取数通道。</summary>
    private static PetRosterGrain CreateRosterGrain(GameEntityContext db)
    {
        var grain = (PetRosterGrain)System.Runtime.Serialization.FormatterServices
            .GetUninitializedObject(typeof(PetRosterGrain));
        // Orleans 把 GrainId 的键串按**十六进制**解析成 long（先试过直接给 "424242"，
        // GetPrimaryKeyLong() 回的是 0x424242=4342338，于是查不到任何行）。
        // 所以这里给的是目标数字的小写十六进制形式，上面那条 GetPrimaryKeyLong 断言就是它的回归门。
        GrainTestHarness.AttachGrainContext(grain, "PetRoster", TestOwnerCharacterId.ToString("x"));
        GrainTestHarness.InjectField(grain, "_logger", NullLogger<PetRosterGrain>.Instance);
        GrainTestHarness.InjectField(grain, "_instances", new DataServiceProvide<GameEntityContext, PetInstanceEntity, long>(db));
        GrainTestHarness.InjectField(grain, "_assistRules", new DataServiceProvide<GameEntityContext, PetAssistRuleEntity, int>(db));
        GrainTestHarness.InjectField(grain, "_bands", new DataServiceProvide<GameEntityContext, PetStatBandEntity, int>(db));
        GrainTestHarness.InjectField(grain, "_events", new DataServiceProvide<GameEntityContext, PetInstanceEventEntity, int>(db));
        return grain;
    }
}
