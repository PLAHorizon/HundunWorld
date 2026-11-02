using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Horizon.Game.Sim.Pets;

/// <summary>宠物内容快照的装载回执。</summary>
public sealed class PetContentSnapshotResult
{
    /// <summary>是否装配成功。</summary>
    public bool Ok { get; set; }

    /// <summary>来源路径。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>内容修订号。</summary>
    public int ContentRevision { get; set; }

    /// <summary>装进来的物种数。</summary>
    public int Species { get; set; }

    /// <summary>被挡下的条目数（号段非法/区间反了/技能 id 越域）。</summary>
    public int Rejected { get; set; }

    /// <summary>问题清单。</summary>
    public List<string> Problems { get; set; } = new();

    /// <summary>
    /// 一行自证文本。宿主启动日志里必须看得见"装了几条"：
    /// <b>文件读到了不等于内容装上了</b>，而没装上的表现是所有宠物都退回硬编码目录的 5 个物种。
    /// </summary>
    public string Describe() => string.Format(
        "宠物内容 rev={0} 物种={1} 挡下={2} 问题={3}",
        ContentRevision, Species, Rejected, Problems.Count);
}

/// <summary>
/// 宠物内容快照装载器：把 <c>PetCli export-content</c> 导出的 JSON 装成进程内权威源。
/// <para>
/// <b>大声失败</b>：文件缺失、格式坏、内容为空都返回 <c>Ok=false</c> 并带问题清单，
/// 由宿主决定是回退硬编码目录还是拒绝启动。绝不"静默装成空内容"——那会让所有宠物都退回
/// 5 条硬编码物种，表现是"我配的那个物种怎么没了"，没人会归因到快照文件。
/// </para>
/// <para>
/// <b>逐条校验而不是整包信任</b>：导出侧的校验是发布前那一刻的事，文件在磁盘上可能被手改过。
/// 号段非法的条目挡下并计数，比"整包拒绝"更可用（策划改了 1 个号不该让另外 30 个物种读不到），
/// 也比"整包接受"更安全。
/// </para>
/// </summary>
public static class PetContentLoader
{
    /// <summary>技能 id 域下界（与 CLI 校验器同值；两处各写一份由契约测试钉住）。</summary>
    public const int SkillIdDomainMin = 800_000;

    /// <summary>从 JSON 文件装入并安装为进程内权威源。</summary>
    public static PetContentSnapshotResult LoadFile(string path)
    {
        var receipt = new PetContentSnapshotResult { Path = path ?? string.Empty };
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            receipt.Problems.Add("文件不存在：" + path);
            return receipt;
        }

        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                receipt.Problems.Add("根节点不是对象");
                return receipt;
            }

            receipt.ContentRevision = ReadInt(doc.RootElement, "contentRevision");
            if (!doc.RootElement.TryGetProperty("species", out var speciesArray)
                || speciesArray.ValueKind != JsonValueKind.Array)
            {
                receipt.Problems.Add("缺少 species 数组");
                return receipt;
            }

            var bundle = new PetContentBundle { ContentRevision = receipt.ContentRevision };
            foreach (var item in speciesArray.EnumerateArray())
            {
                var template = FromJson(item, receipt);
                if (template == null)
                {
                    receipt.Rejected++;
                    continue;
                }
                bundle.Species[template.SpeciesId] = template;
            }

            if (bundle.Species.Count == 0)
            {
                receipt.Problems.Add("零可用物种（挡下 " + receipt.Rejected + " 条）");
                return receipt;
            }

            var install = PetContent.Install(bundle);
            receipt.Ok = install.Status == PetContentLoadStatus.Installed;
            receipt.Species = install.SpeciesCount;
            if (!receipt.Ok) receipt.Problems.Add("装配被拒：" + install.Message);
            else receipt.ContentRevision = install.ContentRevision;
            return receipt;
        }
        catch (Exception ex)
        {
            receipt.Ok = false;
            receipt.Problems.Add("解析失败：" + ex.GetBaseException().Message);
            return receipt;
        }
    }

    /// <summary>把 JSON 对象解成模板；不合法时把原因写进 <paramref name="receipt"/> 并返回 null。</summary>
    private static PetSpeciesTemplate? FromJson(JsonElement item, PetContentSnapshotResult receipt)
    {
        var speciesId = ReadInt(item, "speciesId");
        if (!PetIdentityCore.IsSpeciesId(speciesId))
        {
            receipt.Problems.Add($"物种 {speciesId} 不在号段内，已挡下");
            return null;
        }

        var template = new PetSpeciesTemplate
        {
            SpeciesId = speciesId,
            Name = ReadString(item, "name"),
            ModelKey = ReadString(item, "modelKey"),
            PrefabPath = ReadString(item, "prefabPath"),
            BaseColorKey = ReadString(item, "baseColorKey"),
            StandState = ReadString(item, "standState"),
            ModelScale = ReadFloat(item, "modelScale", 1f),
            CarryLevelMin = ReadInt(item, "carryLevelMin"),
            CarryLevelMax = ReadInt(item, "carryLevelMax"),
            HatchSeconds = ReadInt(item, "hatchSeconds"),
            HatchLevel = ReadInt(item, "hatchLevel"),
            OwnerLevelSlack = ReadInt(item, "ownerLevelSlack"),
            ImmortalLifespan = ReadBool(item, "immortalLifespan"),
            HappinessScale = ReadInt(item, "happinessScale"),
            CaptureMode = (PetCaptureMode)ReadInt(item, "captureMode"),
            CaptureBaseChanceBps = ReadInt(item, "captureBaseChanceBps"),
            CaptureGuidanceSeconds = ReadInt(item, "captureGuidanceSeconds"),
            MutationChanceBps = ReadInt(item, "mutationChanceBps"),
            Published = ReadBool(item, "published"),
            ContentVersion = ReadInt(item, "contentVersion"),
            Remark = ReadString(item, "remark"),
        };

        // 区间方向：反了不会报错，只会让 roll 静默返回下界（表现是"这只资质最差"）
        var min = ReadQuality(item, "qualityMin");
        var max = ReadQuality(item, "qualityMax");
        if (min.Attack > max.Attack || min.Defense > max.Defense || min.Stamina > max.Stamina
            || min.Mana > max.Mana || min.Speed > max.Speed)
        {
            receipt.Problems.Add($"物种 {speciesId} 的资质区间下界大于上界，已挡下");
            return null;
        }
        template.QualityMin = min;
        template.QualityMax = max;

        template.InnateSkillIds = ReadInts(item, "innateSkillIds");
        template.LearnableSkillIds = ReadInts(item, "learnableSkillIds");
        template.EpiphanySkillIds = ReadInts(item, "epiphanySkillIds");
        template.AssistSkillIds = ReadInts(item, "assistSkillIds");

        // 技能 id 域：宠物不另立编号，引用技能树那条域；越域说明导出侧的校验被绕过
        foreach (var id in template.InnateSkillIds)
        {
            if (id >= SkillIdDomainMin) continue;
            receipt.Problems.Add($"物种 {speciesId} 的天生技 {id} 不在技能域（{SkillIdDomainMin}+），已忽略该条");
            template.InnateSkillIds = Array.Empty<int>();
            break;
        }

        if (item.TryGetProperty("slotRule", out var slotRule) && slotRule.ValueKind == JsonValueKind.Object)
        {
            template.SlotRule = new PetSlotRuleConfig(
                ReadInt(slotRule, "baseSlots"), ReadInt(slotRule, "absoluteMaxSlots"),
                null,
                ReadInt(slotRule, "comprehensionUnlockA"), ReadInt(slotRule, "comprehensionUnlockB"),
                ReadInt(slotRule, "slotGrowMaxBattleLevel"), ReadInt(slotRule, "slotGrowMaxExistingSkills"),
                ReadInt(slotRule, "slotGrowChanceBps"), ReadInt(slotRule, "overwriteChanceBps"),
                ReadInt(slotRule, "maxLockableSlots"), ReadInt(slotRule, "learnedPoolMax"),
                ReadInt(slotRule, "equippedMax"));
        }

        if (item.TryGetProperty("initialBond", out var bond) && bond.ValueKind == JsonValueKind.Object)
        {
            template.InitialBond = new PetBondValues
            {
                Loyalty = ReadInt(bond, "loyalty"), Happiness = ReadInt(bond, "happiness"),
                Lifespan = ReadInt(bond, "lifespan"), Intimacy = ReadInt(bond, "intimacy"),
            };
        }

        if (item.TryGetProperty("follow", out var follow) && follow.ValueKind == JsonValueKind.Object)
        {
            template.Follow = new PetFollowConfig(
                ReadFloat(follow, "followDistance", 3f), ReadFloat(follow, "leashRadius", 12f),
                ReadFloat(follow, "teleportBeyondRadius", 40f), ReadInt(follow, "repathIntervalTicks"),
                ReadFloat(follow, "speed", 5.5f), ReadFloat(follow, "arriveEpsilon", 0.6f),
                ReadInt(follow, "corpseSeconds"), ReadInt(follow, "reviveCastSeconds"),
                ReadInt(follow, "summonCooldownSeconds"), ReadInt(follow, "followStartDelayMs"));
        }

        if (item.TryGetProperty("equipSlots", out var equipSlots) && equipSlots.ValueKind == JsonValueKind.Array)
        {
            var list = new List<PetEquipSlot>();
            foreach (var e in equipSlots.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Number) continue;
                var v = e.GetInt32();
                if (v >= 0 && v <= (int)PetEquipSlot.Bell) list.Add((PetEquipSlot)v);
            }
            template.EquipSlots = list.ToArray();
        }

        if (item.TryGetProperty("allowedOrigins", out var origins) && origins.ValueKind == JsonValueKind.Array)
        {
            var list = new List<PetOrigin>();
            foreach (var e in origins.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Number) continue;
                var v = e.GetInt32();
                if (v >= 0 && v <= (int)PetOrigin.Divine) list.Add((PetOrigin)v);
            }
            if (list.Count > 0) template.AllowedOrigins = list.ToArray();
        }

        if (item.TryGetProperty("allowedGrowthTiers", out var tiers) && tiers.ValueKind == JsonValueKind.Array)
        {
            var list = new List<PetGrowthTier>();
            foreach (var e in tiers.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Number) continue;
                var v = e.GetInt32();
                if (v >= 0 && v <= (int)PetGrowthTier.Perfect) list.Add((PetGrowthTier)v);
            }
            if (list.Count > 0) template.AllowedGrowthTiers = list.ToArray();
        }

        if (item.TryGetProperty("aiFlagOverrides", out var overrides) && overrides.ValueKind == JsonValueKind.Array)
        {
            var list = new int?[5];
            var index = 0;
            foreach (var e in overrides.EnumerateArray())
            {
                if (index >= list.Length) break;
                list[index++] = e.ValueKind == JsonValueKind.Null ? (int?)null : e.GetInt32();
            }
            template.AiFlagOverrides = list;
        }
        else
        {
            template.AiFlagOverrides = new int?[] { null, null, null, null, null };
        }

        template.IntellectTier = (PetIntellectTier)Math.Clamp(ReadInt(item, "intellectTier"),
            (int)PetIntellectTier.Mindless, (int)PetIntellectTier.Master);
        template.DefaultStance = (PetStance)Math.Clamp(ReadInt(item, "defaultStance"),
            (int)PetStance.Cowardly, (int)PetStance.Aggressive);
        template.DefaultCommand = (PetCommand)Math.Clamp(ReadInt(item, "defaultCommand"),
            (int)PetCommand.Attack, (int)PetCommand.Retreat);
        template.EpiphanyChanceBps = ReadInt(item, "epiphanyChanceBps");
        template.FuseMinLevel = ReadInt(item, "fuseMinLevel");

        if (item.TryGetProperty("allowedPersonalities", out var personalities)
            && personalities.ValueKind == JsonValueKind.Array)
        {
            var list = new List<PetPersonality>();
            foreach (var e in personalities.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Number) continue;
                var v = e.GetInt32();
                if (v >= 0 && v <= (int)PetPersonality.Loyal) list.Add((PetPersonality)v);
            }
            if (list.Count > 0) template.AllowedPersonalities = list.ToArray();
        }

        return template;
    }

    private static PetQualities ReadQuality(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var q) || q.ValueKind != JsonValueKind.Object)
            return PetQualities.Base;
        return new PetQualities
        {
            Attack = ReadInt(q, "attack"), Defense = ReadInt(q, "defense"), Stamina = ReadInt(q, "stamina"),
            Mana = ReadInt(q, "mana"), Speed = ReadInt(q, "speed"),
        };
    }

    private static int[] ReadInts(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<int>();
        var list = new List<int>();
        foreach (var e in arr.EnumerateArray())
        {
            if (e.ValueKind == JsonValueKind.Number) list.Add(e.GetInt32());
        }
        return list.ToArray();
    }

    private static int ReadInt(JsonElement item, string name)
        => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private static bool ReadBool(JsonElement item, string name)
        => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static float ReadFloat(JsonElement item, string name, float fallback)
    {
        if (!item.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number) return fallback;
        var got = v.GetSingle();
        return float.IsNaN(got) || float.IsInfinity(got) ? fallback : got;
    }

    private static string ReadString(JsonElement item, string name)
        => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? string.Empty) : string.Empty;
}
