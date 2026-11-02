using System;
using System.Collections.Generic;

namespace Horizon.Game.Sim.Pets;

/// <summary>
/// 运行时可消费的宠物物种模板（一个物种 = 一个 <see cref="PetIdentityCore"/> 物种号）。
/// <para>
/// <b>它只装"这一种宠的规则"，不装任何个体数据</b>：资质在这里是**区间**而不是值，
/// 成长是**档位集合**而不是一个数。个体由 <see cref="PetInstanceState"/> + 实例编号表达，
/// 存运行时表，绝不进内容包（否则一只宠会被全服共享）。
/// </para>
/// </summary>
public sealed class PetSpeciesTemplate
{
    /// <summary>物种号（500000–799999）。</summary>
    public int SpeciesId { get; set; }

    /// <summary>名称（显示用，<b>不参与任何判等</b>）。</summary>
    public string Name { get; set; }

    /// <summary>模型键（外观链的第一半；与怪物侧同名约定）。</summary>
    public string ModelKey { get; set; }

    /// <summary>预制体路径（外观链第二半；空 = 还没有美术）。</summary>
    public string PrefabPath { get; set; }

    /// <summary>体型缩放（1.00 = 原始）。</summary>
    public float ModelScale { get; set; } = 1f;

    /// <summary>待机状态名（外观表导出用，同怪物侧）。</summary>
    public string StandState { get; set; }

    /// <summary>基色键（染色前的默认色）。</summary>
    public string BaseColorKey { get; set; }

    /// <summary>可携带/参战等级段下界（梦幻按携带等级分层，§B1）。</summary>
    public int CarryLevelMin { get; set; } = 0;

    /// <summary>可携带等级段上界。</summary>
    public int CarryLevelMax { get; set; } = 175;

    /// <summary>出身集合（该物种能以哪些出身被获得）。</summary>
    public PetOrigin[] AllowedOrigins { get; set; } = { PetOrigin.Wild, PetOrigin.Baby };

    /// <summary>资质区间下界。</summary>
    public PetQualities QualityMin { get; set; } = PetQualities.Base;

    /// <summary>资质区间上界。</summary>
    public PetQualities QualityMax { get; set; } = PetQualities.Base;

    /// <summary>该物种允许的档位集合（不是所有物种都能出"完美"，§B3）。</summary>
    public PetGrowthTier[] AllowedGrowthTiers { get; set; } =
        { PetGrowthTier.Ordinary, PetGrowthTier.Good, PetGrowthTier.Fine };

    /// <summary>孵化秒数（0 = 捕捉即得，WoW 档，§A1）。</summary>
    public int HatchSeconds { get; set; }

    /// <summary>出壳等级。</summary>
    public int HatchLevel { get; set; } = PetGrowthCore.BabyStartLevel;

    /// <summary>允许超出主人的级数（天龙 10，WoW 0）。</summary>
    public int OwnerLevelSlack { get; set; } = PetGrowthCore.DefaultOwnerLevelSlack;

    /// <summary>槽位规则。</summary>
    public PetSlotRuleConfig SlotRule { get; set; } = PetSlotRuleConfig.Default;

    /// <summary>天生必带技能 id（洗炼必出，§B4）。</summary>
    public int[] InnateSkillIds { get; set; } = Array.Empty<int>();

    /// <summary>可学技能池 id。</summary>
    public int[] LearnableSkillIds { get; set; } = Array.Empty<int>();

    /// <summary>领悟候选池 id（大话"有空栏每场概率获得随机技能"，§C4）。</summary>
    public int[] EpiphanySkillIds { get; set; } = Array.Empty<int>();

    /// <summary>领悟率（bps）。</summary>
    public int EpiphanyChanceBps { get; set; } = 1_000;

    /// <summary>AI 智能档位。</summary>
    public PetIntellectTier IntellectTier { get; set; } = PetIntellectTier.Trained;

    /// <summary>五个能力位的模板覆盖（null = 用档位默认，三态）。</summary>
    public int?[] AiFlagOverrides { get; set; } = new int?[5];

    /// <summary>默认姿态。</summary>
    public PetStance DefaultStance { get; set; } = PetStance.Aggressive;

    /// <summary>默认指令。</summary>
    public PetCommand DefaultCommand { get; set; } = PetCommand.Follow;

    /// <summary>性格集合（该物种能出哪些性格）。</summary>
    public PetPersonality[] AllowedPersonalities { get; set; } =
        { PetPersonality.Brave, PetPersonality.Cautious, PetPersonality.Loyal };

    /// <summary>四轴默契初值。</summary>
    public PetBondValues InitialBond { get; set; } = PetBondValues.Default;

    /// <summary>寿命是否无限（神兽出战不掉寿命，§B6）。</summary>
    public bool ImmortalLifespan { get; set; }

    /// <summary>快乐刻度（1000 = WoW/梦幻统一到千分制；100 = 天龙口径）。</summary>
    public int HappinessScale { get; set; } = PetBondCore.HappinessMax;

    /// <summary>开放的装备部位（空 = 无宠物装备，WoW 档，§A7）。</summary>
    public PetEquipSlot[] EquipSlots { get; set; } = Array.Empty<PetEquipSlot>();

    /// <summary>携带的辅助技能 id（辅主特技/护主）。</summary>
    public int[] AssistSkillIds { get; set; } = Array.Empty<int>();

    /// <summary>变异 roll 概率（bps）；0 = 该物种不变异。</summary>
    public int MutationChanceBps { get; set; }

    /// <summary>炼妖最低等级（默认 30，§B9）。</summary>
    public int FuseMinLevel { get; set; } = PetMergeCore.FuseMinLevel;

    /// <summary>跟随参数。</summary>
    public PetFollowConfig Follow { get; set; } = PetFollowConfig.Default;

    /// <summary>可捕捉方式。</summary>
    public PetCaptureMode CaptureMode { get; set; } = PetCaptureMode.CombatWeaken;

    /// <summary>捕捉基准概率（bps）。</summary>
    public int CaptureBaseChanceBps { get; set; } = 3_000;

    /// <summary>捕捉所需引导秒数（WoW 驯服 20 秒，§A1；瞬捕为 0）。</summary>
    public int CaptureGuidanceSeconds { get; set; }

    /// <summary>发布态；只有已发布物种进运行时内容包。</summary>
    public bool Published { get; set; }

    /// <summary>内容版本（原子整包替换时的比较位）。</summary>
    public int ContentVersion { get; set; } = 1;

    /// <summary>备注。</summary>
    public string Remark { get; set; }
}

/// <summary>内容包不可用的原因。</summary>
public enum PetContentLoadStatus : byte
{
    /// <summary>装配成功。</summary>
    Installed = 0,

    /// <summary>文件不存在——<b>与"存在但零条"必须区分</b>。</summary>
    FileMissing = 1,

    /// <summary>解析失败。</summary>
    Malformed = 2,

    /// <summary>解析成功但零可用条目。</summary>
    Empty = 3,
}

/// <summary>内容装配回执。</summary>
public readonly struct PetContentLoadReceipt
{
    /// <summary>状态。</summary>
    public readonly PetContentLoadStatus Status;

    /// <summary>装配的物种数。</summary>
    public readonly int SpeciesCount;

    /// <summary>内容修订号。</summary>
    public readonly int ContentRevision;

    /// <summary>人可读说明。</summary>
    public readonly string Message;

    public PetContentLoadReceipt(PetContentLoadStatus status, int speciesCount, int contentRevision, string message)
    {
        Status = status;
        SpeciesCount = speciesCount;
        ContentRevision = contentRevision;
        Message = message;
    }
}

/// <summary>
/// 宠物内容的安装/读取点（照抄 <c>MonsterContent</c> 的原子整包替换纪律）。
/// <para>
/// <b>为什么是整包换引用而不是逐项合并</b>：逐项合并会让"半个新模板 + 半个旧模板"成为可见状态，
/// 而这类状态下算出来的面板数字谁都无法复现。换引用失败则整体保持旧包，读者永远只看到一份完整内容。
/// </para>
/// <para>
/// <b>没装配时必须等价于硬编码目录</b>（记忆：内容改由每个读法先查已装配表，装不装都等价）——
/// 所以 <see cref="TryGetTemplate"/> 在未装配时回落到 <see cref="PetSpeciesCatalog"/>，
/// 而不是返回 null 让上层各自兜底。
/// </para>
/// </summary>
public static class PetContent
{
    private static readonly object Gate = new();
    private static PetContentBundle _current;
    private static PetContentBundle _hardcodedFallback;

    /// <summary>当前已装配内容包；null = 未装配（读走硬编码目录）。</summary>
    public static PetContentBundle Current => _current;

    /// <summary>装配一份内容包（原子替换）。</summary>
    /// <returns>回执；<paramref name="bundle"/> 为 null 或空时状态非 <see cref="PetContentLoadStatus.Installed"/>，
    /// 且<b>旧包保持不变</b>。</returns>
    public static PetContentLoadReceipt Install(PetContentBundle bundle)
    {
        if (bundle is null)
            return new PetContentLoadReceipt(PetContentLoadStatus.Malformed, 0, 0, "内容包为 null，保持原内容");
        if (bundle.Species is null || bundle.Species.Count == 0)
            return new PetContentLoadReceipt(PetContentLoadStatus.Empty, 0, bundle.ContentRevision, "内容包零条目，保持原内容");

        lock (Gate)
        {
            _current = bundle;
        }
        var published = 0;
        foreach (var kv in bundle.Species)
        {
            if (kv.Value is { Published: true }) published++;
        }
        return new PetContentLoadReceipt(PetContentLoadStatus.Installed, bundle.Species.Count,
            bundle.ContentRevision, $"物种={bundle.Species.Count} 已发布={published}");
    }

    /// <summary>清回硬编码目录（测试与"内容加载失败继续跑"的通路）。</summary>
    public static void ResetToHardcoded()
    {
        lock (Gate)
        {
            _current = null;
            _hardcodedFallback = PetSpeciesCatalog.Build();
        }
    }

    /// <summary>
    /// 读一个物种模板。<b>先查已装配包，再落硬编码目录</b>；两条路都给同一个类型，
    /// 所以上层不需要知道自己读的是哪一份。
    /// </summary>
    public static bool TryGetTemplate(int speciesId, out PetSpeciesTemplate template)
    {
        var current = _current;
        if (current?.Species is not null && current.Species.TryGetValue(speciesId, out var found))
        {
            template = found;
            return true;
        }

        if (_hardcodedFallback is null) _hardcodedFallback = PetSpeciesCatalog.Build();
        if (_hardcodedFallback.Species.TryGetValue(speciesId, out var hard))
        {
            template = hard;
            return true;
        }

        template = null;
        return false;
    }

    /// <summary>当前可见（已发布）物种清单。</summary>
    public static List<PetSpeciesTemplate> Published()
    {
        var source = _current ?? (_hardcodedFallback ??= PetSpeciesCatalog.Build());
        var list = new List<PetSpeciesTemplate>(source.Species.Count);
        foreach (var kv in source.Species)
        {
            if (kv.Value is { Published: true }) list.Add(kv.Value);
        }
        list.Sort((a, b) => a.SpeciesId.CompareTo(b.SpeciesId));
        return list;
    }

    /// <summary>内容修订号；未装配时为 0。</summary>
    public static int Revision => _current?.ContentRevision ?? 0;
}

/// <summary>宠物内容包。</summary>
public sealed class PetContentBundle
{
    /// <summary>物种号 → 模板。</summary>
    public Dictionary<int, PetSpeciesTemplate> Species { get; } = new();

    /// <summary>内容修订号。</summary>
    public int ContentRevision { get; set; } = 1;
}

/// <summary>
/// 硬编码宠物物种目录（未装配内容包时的回落，也是 CLI 与测试的对照夹具）。
/// <para>刻意做成小样本（5 种）而不是全量：它的职责是"没内容包时系统照跑"，
/// 不是替代数据库。真正的物种走 021/022 与 <c>Horizon.PetCli</c>。</para>
/// </summary>
public static class PetSpeciesCatalog
{
    /// <summary>目录条目（保持与怪物目录同样的最小描述形状：id、名称、携带段、模型键、是否发布）。</summary>
    public readonly struct Row
    {
        /// <summary>物种号。</summary>
        public readonly int SpeciesId;

        /// <summary>名称。</summary>
        public readonly string Name;

        /// <summary>携带等级段下界。</summary>
        public readonly int CarryMin;

        /// <summary>模型键。</summary>
        public readonly string ModelKey;

        /// <summary>是否为变异/神兽段（决定号段与可否炼妖）。</summary>
        public readonly PetOrigin PrimaryOrigin;

        public Row(int speciesId, string name, int carryMin, string modelKey, PetOrigin primaryOrigin)
        {
            SpeciesId = speciesId;
            Name = name;
            CarryMin = carryMin;
            ModelKey = modelKey;
            PrimaryOrigin = primaryOrigin;
        }
    }

    /// <summary>目录行（号段规则见 <see cref="PetIdentityCore.RarityTierOf"/>）。</summary>
    public static readonly Row[] Rows =
    {
        new(500_001, "墨麟幼崽", 0, "pet_mo_qilin_cub", PetOrigin.Baby),
        new(500_002, "山鬼", 15, "pet_shan_gui", PetOrigin.Wild),
        new(500_003, "赤瞳狼", 25, "pet_red_wolf", PetOrigin.Wild),
        new(600_001, "变异·赤瞳狼", 25, "pet_red_wolf_mutant", PetOrigin.Mutant),
        new(650_001, "混沌遗蜕·守灵", 175, "pet_hundun_warden", PetOrigin.Divine),
    };

    /// <summary>构造回落内容包。</summary>
    public static PetContentBundle Build()
    {
        var bundle = new PetContentBundle { ContentRevision = 1 };
        for (var i = 0; i < Rows.Length; i++)
        {
            var row = Rows[i];
            var template = new PetSpeciesTemplate
            {
                SpeciesId = row.SpeciesId,
                Name = row.Name,
                ModelKey = row.ModelKey,
                PrefabPath = string.Empty, // 美术未就位：如实留空，导出时会报 problem 而不是假装有
                ModelScale = 1f,
                StandState = "Idle",
                BaseColorKey = "base",
                CarryLevelMin = row.CarryMin,
                CarryLevelMax = row.CarryMin + 25 > 175 ? 175 : row.CarryMin + 25,
                AllowedOrigins = new[] { row.PrimaryOrigin },
                QualityMin = new PetQualities { Attack = 900, Defense = 900, Stamina = 900, Mana = 900, Speed = 900 },
                QualityMax = new PetQualities { Attack = 1500, Defense = 1500, Stamina = 1500, Mana = 1500, Speed = 1500 },
                InnateSkillIds = new[] { 800901 },
                LearnableSkillIds = new[] { 800901, 800903, 800913 },
                EpiphanySkillIds = new[] { 800913 },
                AssistSkillIds = new[] { 800901, 800913 },
                EquipSlots = new[] { PetEquipSlot.Wrist, PetEquipSlot.Collar, PetEquipSlot.Body },
                IntellectTier = row.PrimaryOrigin == PetOrigin.Divine ? PetIntellectTier.Master : PetIntellectTier.Trained,
                ImmortalLifespan = row.PrimaryOrigin == PetOrigin.Divine,
                MutationChanceBps = row.PrimaryOrigin == PetOrigin.Mutant ? 0 : 50,
                Published = true,
                ContentVersion = 1,
                Remark = "硬编码回落目录",
            };
            bundle.Species[row.SpeciesId] = template;
        }
        return bundle;
    }
}
