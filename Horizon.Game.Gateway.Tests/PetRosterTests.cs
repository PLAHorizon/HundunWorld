using Horizon.Game.Message.Network;
using Horizon.Game.Sim.Pets;
using Horizon.Model.GameModel;
using Horizon.Orleans.Grains.Pets;
using Horizon.Orleans.Interface;
using Moq;
using Orleans;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 宠物个体读侧 → Orleans 名册 → 反哺投影这一段的设计期判据（设计文档 §9/§10.2）。
/// <para>这里的断言对象是"读出来的数字对不对、读坏了有没有说话"，不是"能不能编译"。</para>
/// </summary>
public class PetRosterTests
{
    private const int WolfSpecies = 500003;
    private const int AssistStrengthSkill = 800901;   // 借力
    private const int AssistCritTankSkill = 800913;   // 忠心

    private static PetInstanceEntity Instance(long id, int species, PetInstanceState state,
        int level = 30, string slots = "[]", string gear = "[]", string timed = null)
        => new()
        {
            Id = id,
            SpeciesId = species,
            OwnerCharacterId = 7L,
            Nickname = "赤瞳",
            Generation = 2,
            State = (byte)state,
            Level = level,
            Loyalty = 100,
            Happiness = 700,
            Lifespan = 100,
            SkillSlotsJson = slots,
            EquipmentJson = gear,
            TimedMutationJson = timed,
            IsValid = true,
        };

    private static PetAssistRuleEntity Rule(int entry, int species, int skill)
        => new()
        {
            Id = entry,
            SpeciesId = species,
            SkillId = skill,
            AssistKind = (byte)PetAssistKind.OwnerStrength,
            RequiresBattle = true,
            Possessable = true,
            Seq = entry,
            IsValid = true,
        };

    // ==================== ① 编解码：三列 JSON 的唯一出口 ====================

    [Fact]
    public void 技能槽编解码逐字段往返()
    {
        var slots = new[]
        {
            new PetSkillSlot { SkillId = 800901, Kind = PetSkillSlotKind.Innate, Level = 4, Locked = false },
            new PetSkillSlot { SkillId = 800913, Kind = PetSkillSlotKind.Locked, Level = 10, Locked = true },
            new PetSkillSlot { SkillId = 0, Kind = PetSkillSlotKind.Empty, Level = 1, Locked = false },
        };

        var json = PetInstanceCodec.EncodeSkillSlots(slots);
        Assert.True(PetInstanceCodec.TryDecodeSkillSlots(json, out var back, out var error));
        Assert.Null(error);
        Assert.Equal(slots.Length, back.Length);
        // 逐字段比而不是整串比：空槽的 level 由解码给默认值，整串比会红在无关处
        for (var i = 0; i < slots.Length; i++)
        {
            Assert.Equal(slots[i].SkillId, back[i].SkillId);
            Assert.Equal(slots[i].Kind, back[i].Kind);
            Assert.Equal(slots[i].Level, back[i].Level);
            Assert.Equal(slots[i].Locked, back[i].Locked);
        }
    }

    [Fact]
    public void 解码认外部手写的JSON而不是只认自己编的()
    {
        // 参照必须是另一只手写的字面量：编解码互为镜像的写法抓不出键名写错（记忆里栽过）。
        const string handWritten = @"[{""skillId"":800903,""kind"":2,""level"":7,""locked"":true}]";
        Assert.True(PetInstanceCodec.TryDecodeSkillSlots(handWritten, out var slots, out var error));
        Assert.Null(error);
        Assert.Single(slots);
        Assert.Equal(800903, slots[0].SkillId);
        Assert.Equal(PetSkillSlotKind.Learned, slots[0].Kind);
        Assert.Equal(7, slots[0].Level);
        Assert.True(slots[0].Locked);
    }

    [Fact]
    public void 空槽与Empty不成对时报错而不是静默收下()
    {
        // "skillId=0 但 kind=Learned"这种半吊子行，按 skillId 过滤会当技能、按 kind 过滤会当空槽。
        Assert.False(PetInstanceCodec.TryDecodeSkillSlots(
            @"[{""skillId"":0,""kind"":2,""level"":1,""locked"":false}]", out _, out var error));
        Assert.Contains("空槽必须成对", error);

        Assert.False(PetInstanceCodec.TryDecodeSkillSlots(
            @"[{""skillId"":800901,""kind"":0,""level"":1,""locked"":false}]", out _, out var error2));
        Assert.Contains("却标成 Empty", error2);
    }

    [Fact]
    public void 坏JSON与越界值都大声失败且不返回空数组当成功()
    {
        Assert.False(PetInstanceCodec.TryDecodeSkillSlots("{不是JSON", out _, out var parseError));
        Assert.Contains("解析失败", parseError);

        Assert.False(PetInstanceCodec.TryDecodeSkillSlots(
            @"[{""skillId"":800901,""kind"":9,""level"":1,""locked"":false}]", out _, out var kindError));
        Assert.Contains("kind=9 越界", kindError);

        Assert.False(PetInstanceCodec.TryDecodeSkillSlots(
            @"[{""skillId"":800901,""kind"":2,""level"":99,""locked"":false}]", out _, out var levelError));
        Assert.Contains("level=99 越界", levelError);
    }

    [Fact]
    public void 穿戴清单重复占同一部位被挡下()
    {
        var json = PetInstanceCodec.EncodeWornGear(new[]
        {
            new PetWornGear { ItemInstanceId = 910001, Slot = PetEquipSlot.Collar },
        });
        Assert.True(PetInstanceCodec.TryDecodeWornGear(json, out var one, out var error));
        Assert.Null(error);
        Assert.Single(one);

        Assert.False(PetInstanceCodec.TryDecodeWornGear(
            @"[{""itemInstanceId"":910001,""slot"":1},{""itemInstanceId"":910002,""slot"":1}]",
            out _, out var dupError));
        Assert.Contains("重复占用部位", dupError);

        Assert.False(PetInstanceCodec.TryDecodeWornGear(
            @"[{""itemInstanceId"":0,""slot"":1}]", out _, out var zeroError));
        Assert.Contains("无效", zeroError);
    }

    [Fact]
    public void 临时变异的到期哨兵与缺失键分别处理()
    {
        // expireTick<=0 是"从未生效"的哨兵：present=false 且不算错误。
        Assert.True(PetInstanceCodec.TryDecodeTimedMutation(
            @"{""key"":""dye_common"",""startTick"":0,""expireTick"":0,""qualityCapBps"":10500}",
            out _, out var presentZero, out var errorZero));
        Assert.False(presentZero);
        Assert.Null(errorZero);

        // 有到期时间却没有键 → 无法回指物种表那行，必须报错。
        Assert.False(PetInstanceCodec.TryDecodeTimedMutation(
            @"{""expireTick"":36000,""qualityCapBps"":10500}", out _, out _, out var keyError));
        Assert.Contains("没有 key", keyError);

        var timed = new TimedPetMutation("dye_gold", 100, 36100, 10_800, "gold", 1, PetMutationLayer.Temporary);
        var roundTrip = PetInstanceCodec.EncodeTimedMutation(timed);
        Assert.True(PetInstanceCodec.TryDecodeTimedMutation(roundTrip, out var back, out var present, out var error));
        Assert.True(present);
        Assert.Null(error);
        Assert.Equal("dye_gold", back.Key);
        Assert.Equal(36100L, back.ExpireTick);
        Assert.Equal(10_800, back.QualityCapBps);
        Assert.Equal(1, back.ExtraSkillSlots);
    }

    // ==================== ② 名册装载：身份、辅助域、状态门 ====================

    [Fact]
    public void 个体编号与物种列不符的整条挡下()
    {
        // 500003×100000+1 才是 500003 的第 1 只；这里编号写成了 500002 的第 1 只。
        var rows = new[] { Instance(PetIdentityCore.NewInstanceId(500002, 1), WolfSpecies, PetInstanceState.Battling) };
        var result = PetRosterMapper.Build(rows);

        Assert.Empty(result.Entries);
        Assert.Equal(1, result.Rejected);
        Assert.Contains("不符", result.FirstRejection);
    }

    [Fact]
    public void 同一物种的重复副本全部保留且按编号定序()
    {
        // 本内核存在的首要理由：玩家抓的三只同名宠不能被按 (owner, species) 去重吃成一只。
        var rows = new[]
        {
            Instance(PetIdentityCore.NewInstanceId(WolfSpecies, 3), WolfSpecies, PetInstanceState.Owned),
            Instance(PetIdentityCore.NewInstanceId(WolfSpecies, 1), WolfSpecies, PetInstanceState.Owned),
            Instance(PetIdentityCore.NewInstanceId(WolfSpecies, 2), WolfSpecies, PetInstanceState.Owned),
        };
        var result = PetRosterMapper.Build(rows);

        Assert.Equal(3, result.Entries.Length);
        Assert.Equal(new[] { 1, 2, 3 },
            result.Entries.Select(e => PetIdentityCore.TryParseInstanceId(e.InstanceId, out _, out var seq) ? seq : 0));
    }

    [Fact]
    public void 辅主技只认该物种在规则表里登记过的技能()
    {
        var slots = PetInstanceCodec.EncodeSkillSlots(new[]
        {
            new PetSkillSlot { SkillId = AssistStrengthSkill, Kind = PetSkillSlotKind.Learned, Level = 4 },
            // 800903 强身没在规则表里 → 不能凭"id 在辅助区间"就把它算成辅主技
            new PetSkillSlot { SkillId = 800903, Kind = PetSkillSlotKind.Learned, Level = 9 },
        });
        var rows = new[] { Instance(PetIdentityCore.NewInstanceId(WolfSpecies, 1), WolfSpecies, PetInstanceState.Battling, slots: slots) };
        var rules = new[] { Rule(1, WolfSpecies, AssistStrengthSkill) };

        var result = PetRosterMapper.Build(rows, rules);

        Assert.Single(result.Entries[0].AssistSkillIds);
        Assert.Equal(AssistStrengthSkill, result.Entries[0].AssistSkillIds[0]);
        Assert.Equal(4, result.Entries[0].AssistSkillLevels[0]);
    }

    [Fact]
    public void 只有出战与附体参与反哺蛋和兽栏不算()
    {
        var slots = PetInstanceCodec.EncodeSkillSlots(new[]
        {
            new PetSkillSlot { SkillId = AssistStrengthSkill, Kind = PetSkillSlotKind.Learned, Level = 4 },
        });
        var rows = new[]
        {
            Instance(PetIdentityCore.NewInstanceId(WolfSpecies, 1), WolfSpecies, PetInstanceState.Battling, slots: slots),
            Instance(PetIdentityCore.NewInstanceId(WolfSpecies, 2), WolfSpecies, PetInstanceState.Possessed, slots: slots),
            Instance(PetIdentityCore.NewInstanceId(WolfSpecies, 3), WolfSpecies, PetInstanceState.Egg, slots: slots),
            Instance(PetIdentityCore.NewInstanceId(WolfSpecies, 4), WolfSpecies, PetInstanceState.Stable, slots: slots),
            Instance(PetIdentityCore.NewInstanceId(WolfSpecies, 5), WolfSpecies, PetInstanceState.Released, slots: slots),
        };
        var result = PetRosterMapper.Build(rows, new[] { Rule(1, WolfSpecies, AssistStrengthSkill) });

        Assert.Equal(5, result.Entries.Length);
        Assert.Equal(2, result.ActivePets.Length);
        Assert.Equal(1, result.ActivePets.Count(p => p.InBattle && !p.Possessed));
        Assert.Equal(1, result.ActivePets.Count(p => p.Possessed && !p.InBattle));
    }

    [Fact]
    public void 某一列读坏了那只宠仍在名册里但问题被记下()
    {
        var rows = new[]
        {
            Instance(PetIdentityCore.NewInstanceId(WolfSpecies, 1), WolfSpecies, PetInstanceState.Owned, gear: "{坏掉的不是数组"),
        };
        var result = PetRosterMapper.Build(rows);

        Assert.Single(result.Entries);
        Assert.Equal(0, result.Rejected);
        Assert.Equal(1, result.Degraded);
        Assert.NotNull(result.Entries[0].Issue);
        Assert.Contains("穿戴清单", result.Entries[0].Issue);
        Assert.Empty(result.Entries[0].Gear);
    }

    [Fact]
    public void 有效资质由内核那一份算而不是读侧自己乘()
    {
        // 裸资 1000、悟性 4 档、灵性 2 档、无变异：期望值直接从内核表取，读侧只允许转述。
        var row = Instance(PetIdentityCore.NewInstanceId(WolfSpecies, 1), WolfSpecies, PetInstanceState.Owned);
        row.AttackQuality = 1000;
        row.ComprehensionLevel = 4;
        row.SpiritLevel = 2;

        var result = PetRosterMapper.Build(new[] { row });
        var expected = PetStatsCore.EffectiveQuality(1000, 4, 2, superSpirit: false);

        Assert.Equal(expected, result.Entries[0].EffectiveQualities.Attack);
        Assert.True(result.Entries[0].EffectiveQualities.Attack > 1000,
            "悟性/灵性应当把裸资放大，相等说明这一层没接上");
        Assert.True(result.Entries[0].PetHp > 0);
    }

    // ==================== ③ 反哺投影：面板那几列的数字出处 ====================

    [Fact]
    public void 投影的归因量与手算参照逐位相等()
    {
        // 参照是暴力手写的：每点力量 2 攻、每点灵气 2 攻、每点体质 8 血、
        // 每点定力 2 防、每点体力 1 防 + 8 血、每点身法 3 命中（PetAssistCore 的那组常数）。
        var snapshot = new PetRosterSnapshotDto
        {
            Strength = 50, Spirit = 25, Vitality = 10, Agility = 8, Steadiness = 5,
            HpFlat = 300, CritTankChanceBps = 900, Contributors = 2, RosterCount = 6,
        };

        var display = PetBonusProjection.Project(snapshot);

        Assert.Equal(50 * 2 + 25 * 2, display.Attack);
        Assert.Equal(10 * 8 + 300, display.Health);
        Assert.Equal(5 * 2 + 10 * 1, display.Defense);
        Assert.Equal(8 * 3, display.Hit);
        Assert.Equal(900, display.CritTankChanceBps);
        Assert.Equal(2, display.Contributors);
        Assert.Equal(6, display.RosterCount);
    }

    [Fact]
    public void 没有读数器时说未取数而不是零加成()
    {
        // 0 会被面板念成"你没有出战宠"，与"这条装配没接名册通路"是两件事。
        var display = PetBonusProjection.Project(null);
        Assert.Equal(0, display.Attack);
        Assert.Equal(PetBonusProjection.NotInjectedIssue, display.Issues);
        Assert.Equal(0, display.RosterCount);
    }

    [Fact]
    public void 名册聚合与投影串起来等于内核合成主人属性的那份()
    {
        // 一只出战宠带借力 4 级：力量 +20（AssistSkillPoints(4,false)=4×5）。
        var slots = PetInstanceCodec.EncodeSkillSlots(new[]
        {
            new PetSkillSlot { SkillId = AssistStrengthSkill, Kind = PetSkillSlotKind.Learned, Level = 4 },
        });
        var result = PetRosterMapper.Build(
            new[] { Instance(PetIdentityCore.NewInstanceId(WolfSpecies, 1), WolfSpecies, PetInstanceState.Battling, slots: slots) },
            new[] { Rule(1, WolfSpecies, AssistStrengthSkill) });

        var bonus = PetAssistCore.AggregateOwnerBonuses(result.ActivePets, out var excluded, out _);
        Assert.Equal(0, excluded);
        Assert.Equal(20, bonus.Strength);

        var display = PetBonusProjection.Project(new PetRosterSnapshotDto
        {
            Strength = bonus.Strength, Spirit = bonus.Spirit, Vitality = bonus.Vitality,
            Agility = bonus.Agility, Steadiness = bonus.Steadiness, HpFlat = bonus.HpFlat,
            ManaRegen = bonus.ManaRegen, CritTankChanceBps = bonus.CritTankChanceBps,
            DamageLeechBps = bonus.DamageLeechBps,
            Contributors = result.ActivePets.Length, RosterCount = result.Entries.Length,
        });
        // 20 点力量 × 每点 2 攻 —— 与面板显示的攻击归因量同一个数
        Assert.Equal(40, display.Attack);
        Assert.Equal(1, display.Contributors);
        Assert.Equal(1, display.RosterCount);
    }

    // ==================== ④ 读数门：失败要说、没角色 id 不打 Grain ====================

    [Fact]
    public async Task 名册Grain抛异常时回空账并把原因写进Issues()
    {
        var grain = new Mock<IPetRosterGrain>();
        grain.Setup(g => g.GetSnapshotAsync()).Throws(new InvalidOperationException("silo 掉线"));

        var factory = new Mock<IGrainFactory>();
        factory.Setup(f => f.GetGrain<IPetRosterGrain>(It.IsAny<long>(), It.IsAny<string>()))
               .Returns(() => grain.Object);

        var reader = new PetBonusReader(factory.Object, Microsoft.Extensions.Logging.Abstractions.NullLogger<PetBonusReader>.Instance);
        var snapshot = await reader.ReadAsync(7L);

        Assert.Equal(0, snapshot.RosterCount);
        Assert.Contains("silo 掉线", snapshot.Issues);
    }

    [Fact]
    public async Task 角色id无效时根本不打Grain并说明未查询()
    {
        var factory = new Mock<IGrainFactory>();
        var reader = new PetBonusReader(factory.Object, Microsoft.Extensions.Logging.Abstractions.NullLogger<PetBonusReader>.Instance);

        var snapshot = await reader.ReadAsync(0L);

        Assert.Contains("角色 id 无效", snapshot.Issues);
        factory.Verify(f => f.GetGrain<IPetRosterGrain>(It.IsAny<long>(), It.IsAny<string>()), Times.Never);
    }

    // ==================== ⑤ 消息槽位：追加字段不许错位 ====================

    [Fact]
    public void 属性下行新追加的宠物槽位连续且与Id特性同值()
    {
        // MemoryPack 按 Order 写、Orleans 按 Id 读；两条序列错位不会报错，只会把"血"显示成"攻"。
        var type = typeof(CharacterAttributeRefreshResponse);
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var orders = props
            .Select(p => p.GetCustomAttribute<global::MemoryPack.MemoryPackOrderAttribute>()?.Order ?? -1)
            .Where(o => o >= 0)
            .OrderBy(o => o)
            .ToArray();

        Assert.Equal(Enumerable.Range(0, orders.Length), orders);
        Assert.True(orders.Length >= 23, $"槽位数 {orders.Length} 少于预期的 23（0–22）");

        foreach (var name in new[] { "FromPetsAttack", "FromPetsHealth", "FromPetsDefense", "FromPetsHit",
                                     "PetCritTankChanceBps", "PetBonusContributors", "PetRosterCount", "PetBonusIssues" })
        {
            var prop = type.GetProperty(name) ?? throw new InvalidOperationException($"{name} 不存在");
            var order = prop.GetCustomAttribute<global::MemoryPack.MemoryPackOrderAttribute>()!.Order;
            var id = prop.GetCustomAttribute<global::Orleans.IdAttribute>()!.Id;
            Assert.True(order >= 15, $"{name} 的槽位 {order} 落在追加区之外（会挪动老字段）");
            Assert.Equal((long)order, (long)id);
        }
    }

    [Fact]
    public void 宠物栏字段默认值全为零且问题串为空串而非null()
    {
        // null 会让客户端的 ?? 兜底变成唯一路径，界面上"没数据"与"没问题"就分不开了。
        var response = new CharacterAttributeRefreshResponse();
        Assert.Equal(0, response.FromPetsAttack);
        Assert.Equal(0, response.PetRosterCount);
        Assert.Equal(string.Empty, response.PetBonusIssues);
    }

    // ==================== ⑥ 角色画像下行：值真的到了消息里 ====================

    /// <summary>裸造一个 Horizon.Orleans.Grains.CharacterGrain（照 CombatProfileSourceTests 的基建），只装必要字段。</summary>
    private static Horizon.Orleans.Grains.CharacterGrain CreateGrainWithPetReader(IPetBonusReader? reader)
    {
        var info = new CharacterInfo { CharacterId = 42, MaxHealth = 1000f, CurrentHealth = 1000f, IsAlive = true };
        var state = new CharacterState { CharacterInfo = info };
        var mockState = new Mock<global::Orleans.Runtime.IPersistentState<CharacterState>>();
        mockState.SetupGet(s => s.State).Returns(state);

        var grain = (Horizon.Orleans.Grains.CharacterGrain)System.Runtime.Serialization.FormatterServices
            .GetUninitializedObject(typeof(Horizon.Orleans.Grains.CharacterGrain));
        GrainTestHarness.InjectField(grain, "_logger",
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Horizon.Orleans.Grains.CharacterGrain>.Instance);
        GrainTestHarness.InjectField(grain, "_characterState", mockState.Object);
        // CharacterId 是 CharacterGrain 自己的 auto-property，InjectBackingField 只查 typeof(Grain)，必须用 InjectField。
        GrainTestHarness.InjectField(grain, "<CharacterId>k__BackingField", 42UL);
        if (reader != null) GrainTestHarness.InjectField(grain, "_petBonusReader", reader);
        // 刻意不注入 _itemTemplateContext / _gameAttributeContext：装备与属性表按"读不到"降级，
        // 正是本条要 isolate 的形状——宠物那几栏必须与它们无关地到位。
        return grain;
    }

    [Fact]
    public async Task 角色画像下行带上宠物归因量()
    {
        var reader = new Mock<IPetBonusReader>();
        reader.Setup(r => r.ReadAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new PetRosterSnapshotDto
              {
                  Strength = 50, Spirit = 25, Vitality = 10, Agility = 8, Steadiness = 5,
                  HpFlat = 300, CritTankChanceBps = 900, Contributors = 2, RosterCount = 6,
              });

        var response = await CreateGrainWithPetReader(reader.Object).GetCombatProfileSnapshotAsync();

        // 期望值是手算的：50×2 + 25×2 攻、10×8 + 300 血、5×2 + 10 防、8×3 命中
        Assert.Equal(150, response.FromPetsAttack);
        Assert.Equal(380, response.FromPetsHealth);
        Assert.Equal(20, response.FromPetsDefense);
        Assert.Equal(24, response.FromPetsHit);
        Assert.Equal(900, response.PetCritTankChanceBps);
        Assert.Equal(2, response.PetBonusContributors);
        Assert.Equal(6, response.PetRosterCount);
        // 取数必须用角色 id 去要账，键写错会读到别人的宠
        reader.Verify(r => r.ReadAsync(42L, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task 没有读数通路时下行说明未取数而不是报零加成()
    {
        var response = await CreateGrainWithPetReader(null).GetCombatProfileSnapshotAsync();

        Assert.Equal(0, response.FromPetsAttack);
        Assert.Equal(PetBonusProjection.NotInjectedIssue, response.PetBonusIssues);
        Assert.True(response.Success, "宠物栏缺失不该把整条画像下行判失败");
    }

    // ==================== ⑦ 名册 Grain 本体：取数、聚合、状态门 ====================

    private sealed class RosterFixture
    {
        public List<PetInstanceEntity> Instances = new();
        public List<PetAssistRuleEntity> Rules = new();
        public List<PetStatBandEntity> Bands = new();
        public List<PetInstanceEventEntity> Events = new();
        public bool ThrowOnInstanceRead;
        public int InstanceReads;
    }

    /// <summary>
    /// 造一个只装了数据通路的 PetRosterGrain。
    /// 假上下文<b>真的执行谓词</b>（不是无条件返回全部行）——否则"忘了按主人过滤"这种缺陷测不出来。
    /// </summary>
    private static PetRosterGrain CreateRosterGrain(long ownerCharacterId, RosterFixture f)
    {
        var grain = (Horizon.Orleans.Grains.Pets.PetRosterGrain)System.Runtime.Serialization.FormatterServices
            .GetUninitializedObject(typeof(Horizon.Orleans.Grains.Pets.PetRosterGrain));
        // 键串按 Orleans 的口径给**十六进制**形式：GetPrimaryKeyLong() 把键串当 hex 解析，
        // 直接给 "424242" 会得到 0x424242=4342338，于是所有谓词都匹配不到行而 Issues 仍是空串
        // （活库判据 PetRosterLiveTests 第一次红就是这样暴露的）。
        GrainTestHarness.AttachGrainContext(grain, "PetRoster", ownerCharacterId.ToString("x"));
        GrainTestHarness.InjectField(grain, "_logger",
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Horizon.Orleans.Grains.Pets.PetRosterGrain>.Instance);

        var instances = new Mock<Horizon.Core.Abstract.IDataContext<Horizon.Entities.GameEntityContext, PetInstanceEntity, long>>();
        instances.Setup(c => c.QueryAsync(It.IsAny<System.Linq.Expressions.Expression<Func<PetInstanceEntity, bool>>>(), It.IsAny<bool>()))
            .Returns((System.Linq.Expressions.Expression<Func<PetInstanceEntity, bool>> e, bool _) =>
            {
                f.InstanceReads++;
                if (f.ThrowOnInstanceRead) throw new InvalidOperationException("库不可达");
                return Task.FromResult(f.Instances.AsQueryable().Where(e));
            });
        instances.Setup(c => c.QueryFirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<PetInstanceEntity, bool>>>(), It.IsAny<bool>()))
            .Returns((System.Linq.Expressions.Expression<Func<PetInstanceEntity, bool>> e, bool _) =>
                Task.FromResult(f.Instances.AsQueryable().Where(e).FirstOrDefault()));
        instances.Setup(c => c.UpdateAsync(It.IsAny<PetInstanceEntity>(), It.IsAny<long>()))
            .Returns((PetInstanceEntity row, long id) =>
            {
                var idx = f.Instances.FindIndex(r => r.Id == id);
                if (idx < 0) return Task.FromResult(false);
                f.Instances[idx] = row;
                return Task.FromResult(true);
            });

        var rules = new Mock<Horizon.Core.Abstract.IDataContext<Horizon.Entities.GameEntityContext, PetAssistRuleEntity, int>>();
        rules.Setup(c => c.QueryAsync(It.IsAny<System.Linq.Expressions.Expression<Func<PetAssistRuleEntity, bool>>>(), It.IsAny<bool>()))
            .Returns((System.Linq.Expressions.Expression<Func<PetAssistRuleEntity, bool>> e, bool _) => Task.FromResult(f.Rules.AsQueryable().Where(e)));

        var bands = new Mock<Horizon.Core.Abstract.IDataContext<Horizon.Entities.GameEntityContext, PetStatBandEntity, int>>();
        bands.Setup(c => c.QueryAsync(It.IsAny<System.Linq.Expressions.Expression<Func<PetStatBandEntity, bool>>>(), It.IsAny<bool>()))
            .Returns((System.Linq.Expressions.Expression<Func<PetStatBandEntity, bool>> e, bool _) => Task.FromResult(f.Bands.AsQueryable().Where(e)));

        var events = new Mock<Horizon.Core.Abstract.IDataContext<Horizon.Entities.GameEntityContext, PetInstanceEventEntity, int>>();
        events.Setup(c => c.AddAsync(It.IsAny<PetInstanceEventEntity>()))
            .Returns((PetInstanceEventEntity row) =>
            {
                f.Events.Add(row);
                return Task.FromResult(row);
            });

        GrainTestHarness.InjectField(grain, "_instances", instances.Object);
        GrainTestHarness.InjectField(grain, "_assistRules", rules.Object);
        GrainTestHarness.InjectField(grain, "_bands", bands.Object);
        GrainTestHarness.InjectField(grain, "_events", events.Object);
        return grain;
    }

    [Fact]
    public async Task 名册Grain把出战宠的辅主技算进快照()
    {
        var slots = PetInstanceCodec.EncodeSkillSlots(new[]
        {
            new PetSkillSlot { SkillId = AssistStrengthSkill, Kind = PetSkillSlotKind.Learned, Level = 4 },
        });
        var f = new RosterFixture();
        f.Instances.Add(Instance(PetIdentityCore.NewInstanceId(WolfSpecies, 1), WolfSpecies, PetInstanceState.Battling, slots: slots));
        f.Rules.Add(Rule(1, WolfSpecies, AssistStrengthSkill));

        var snapshot = await CreateRosterGrain(7L, f).GetSnapshotAsync();

        Assert.Equal(1, snapshot.RosterCount);
        Assert.Equal(1, snapshot.Contributors);
        Assert.Equal(20, snapshot.Strength);          // 借力 4 级 = 4×5
        Assert.Equal(0, snapshot.Excluded);
        Assert.Equal(string.Empty, snapshot.Issues);
        Assert.Equal(0, snapshot.Degraded);
    }

    [Fact]
    public async Task 只取本主人的行而不是整张个体表()
    {
        var f = new RosterFixture();
        f.Instances.Add(Instance(PetIdentityCore.NewInstanceId(WolfSpecies, 1), WolfSpecies, PetInstanceState.Battling));
        var stranger = Instance(PetIdentityCore.NewInstanceId(WolfSpecies, 2), WolfSpecies, PetInstanceState.Battling);
        stranger.OwnerCharacterId = 99L;              // 别人的宠
        f.Instances.Add(stranger);

        var snapshot = await CreateRosterGrain(7L, f).GetSnapshotAsync();

        Assert.Equal(1, snapshot.RosterCount);
        Assert.Equal(1, snapshot.Contributors);
    }

    [Fact]
    public async Task 读库异常时如实报空账并把原因念出来()
    {
        var f = new RosterFixture { ThrowOnInstanceRead = true };

        var snapshot = await CreateRosterGrain(7L, f).GetSnapshotAsync();

        Assert.Equal(0, snapshot.RosterCount);
        Assert.Contains("库不可达", snapshot.Issues);
        Assert.True(f.InstanceReads > 0, "一次都没查库就说明这条判据在空转");
    }

    [Fact]
    public async Task 附体位超出上限时多出的那只被挡下且原因可读()
    {
        var f = new RosterFixture();
        for (var i = 1; i <= PetAiCore.MaxPossessedSlots + 1; i++)
            f.Instances.Add(Instance(PetIdentityCore.NewInstanceId(WolfSpecies, i), WolfSpecies, PetInstanceState.Possessed));

        var snapshot = await CreateRosterGrain(7L, f).GetSnapshotAsync();

        Assert.Equal(PetAiCore.MaxPossessedSlots + 1, snapshot.RosterCount);
        Assert.Equal(PetAiCore.MaxPossessedSlots, snapshot.Contributors);
        Assert.Equal(1, snapshot.Excluded);
        Assert.Contains("附体位", snapshot.Issues);
    }

    [Fact]
    public async Task 出战另一只时旧出战宠被顶回待命并留下事件流水()
    {
        var f = new RosterFixture();
        var old = Instance(PetIdentityCore.NewInstanceId(WolfSpecies, 1), WolfSpecies, PetInstanceState.Battling);
        var fresh = Instance(PetIdentityCore.NewInstanceId(WolfSpecies, 2), WolfSpecies, PetInstanceState.Owned);
        f.Instances.Add(old);
        f.Instances.Add(fresh);

        var result = await CreateRosterGrain(7L, f).SetDeployStateAsync(fresh.Id, (int)PetInstanceState.Battling);

        Assert.True(result.Success, result.Message);
        Assert.Equal(PetInstanceState.Battling, (PetInstanceState)f.Instances[1].State);
        Assert.Equal(PetInstanceState.Owned, (PetInstanceState)f.Instances[0].State);
        // 两次写：一次顶下旧宠、一次升上新宠；事件流水各记一条，历史才答得出"这只怎么变成现在这样"
        Assert.Equal(2, f.Events.Count);
        Assert.All(f.Events, e => Assert.Equal(17, e.EventKind));
        Assert.NotNull(result.Snapshot);
        Assert.Equal(1, result.Snapshot!.Contributors);
    }

    [Fact]
    public async Task 改别人主人的宠被拒()
    {
        var f = new RosterFixture();
        var stranger = Instance(PetIdentityCore.NewInstanceId(WolfSpecies, 1), WolfSpecies, PetInstanceState.Owned);
        stranger.OwnerCharacterId = 99L;
        f.Instances.Add(stranger);

        var result = await CreateRosterGrain(7L, f).SetDeployStateAsync(stranger.Id, (int)PetInstanceState.Battling);

        Assert.False(result.Success);
        Assert.Equal(Horizon.Orleans.Grains.Pets.PetRosterGrain.CodeNotOwner, result.Code);
        Assert.Equal(PetInstanceState.Owned, (PetInstanceState)f.Instances[0].State);
        Assert.Empty(f.Events);
    }

    [Fact]
    public async Task 白名单外的状态一律拒绝()
    {
        var f = new RosterFixture();
        f.Instances.Add(Instance(PetIdentityCore.NewInstanceId(WolfSpecies, 1), WolfSpecies, PetInstanceState.Owned));

        // 8 = PendingClaim：不是玩家可以随手切进去的状态
        var result = await CreateRosterGrain(7L, f).SetDeployStateAsync(f.Instances[0].Id, 8);

        Assert.False(result.Success);
        Assert.Equal(Horizon.Orleans.Grains.Pets.PetRosterGrain.CodeBadTarget, result.Code);
        Assert.Equal(PetInstanceState.Owned, (PetInstanceState)f.Instances[0].State);
    }

    [Fact]
    public async Task 红脸的宠不肯出战而原因说的是快乐()
    {
        var f = new RosterFixture();
        var grumpy = Instance(PetIdentityCore.NewInstanceId(WolfSpecies, 1), WolfSpecies, PetInstanceState.Owned);
        grumpy.Happiness = 10;                       // 低于 HappinessBlockThreshold
        f.Instances.Add(grumpy);

        var result = await CreateRosterGrain(7L, f).SetDeployStateAsync(grumpy.Id, (int)PetInstanceState.Battling);

        Assert.False(result.Success);
        Assert.Equal(Horizon.Orleans.Grains.Pets.PetRosterGrain.CodeBondBlocked, result.Code);
        Assert.Contains("快乐太低", result.Message);
        Assert.Equal(PetInstanceState.Owned, (PetInstanceState)f.Instances[0].State);
    }

    [Fact]
    public async Task 附体位满时新命令被拒而不挤掉已有那只()
    {
        var f = new RosterFixture();
        for (var i = 1; i <= PetAiCore.MaxPossessedSlots; i++)
            f.Instances.Add(Instance(PetIdentityCore.NewInstanceId(WolfSpecies, i), WolfSpecies, PetInstanceState.Possessed));
        var waiting = Instance(PetIdentityCore.NewInstanceId(WolfSpecies, 9), WolfSpecies, PetInstanceState.Owned);
        f.Instances.Add(waiting);

        var result = await CreateRosterGrain(7L, f).SetDeployStateAsync(waiting.Id, (int)PetInstanceState.Possessed);

        Assert.False(result.Success);
        Assert.Equal(Horizon.Orleans.Grains.Pets.PetRosterGrain.CodePossessionFull, result.Code);
        Assert.Equal(PetInstanceState.Owned, (PetInstanceState)f.Instances[^1].State);
        Assert.Empty(f.Events);
    }

    [Fact]
    public async Task 重复点同一个状态是幂等的且不记事件()
    {
        var f = new RosterFixture();
        f.Instances.Add(Instance(PetIdentityCore.NewInstanceId(WolfSpecies, 1), WolfSpecies, PetInstanceState.Battling));

        var result = await CreateRosterGrain(7L, f).SetDeployStateAsync(f.Instances[0].Id, (int)PetInstanceState.Battling);

        Assert.True(result.Success, result.Message);
        Assert.Equal(PetInstanceState.Battling, (PetInstanceState)f.Instances[0].State);
        Assert.Empty(f.Events);
    }
}
