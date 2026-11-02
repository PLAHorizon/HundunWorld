using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Horizon.Game.Sim.Items;
using Horizon.QuestCli;

namespace Horizon.ItemCli;

/// <summary>
/// 物品命令行的入口：物品编辑器（Flax 插件）以"短生命周期子进程"的方式调用它，
/// 由它把<b>校验</b>与<b>写库</b>这两件只在服务端一侧才说得清的事做完，并代跑内核的确定性算法。
/// <para>
/// 输出恒为一段 JSON（UTF-8），字段固定 <c>ok/verb/message/data/warnings</c>；退出码 0 成功、非 0 失败。
/// 与 <c>Horizon.PetCli</c> 逐字同构，编辑器的解析代码因此可以照搬。
/// </para>
/// <para>
/// <b>preview-* 一律调内核而不是在这儿再算一遍</b>：编辑器里看到的伤害/预算/售价
/// 必须与将来运行时算出的是同一个函数的结果。
/// </para>
/// </summary>
public static class Program
{
    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static TextWriter _stdout = Console.Out;
    private static TextReader _stdin = Console.In;

    private static void InitializeStreams()
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        _stdout = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true };
        _stdin = new StreamReader(Console.OpenStandardInput(), utf8, detectEncodingFromByteOrderMarks: true);
    }

    public static int Main(string[] args)
    {
        var verb = (args.Length > 0 ? args[0] : "help").Trim().ToLowerInvariant();
        var rest = args.Skip(1).ToArray();

        try
        {
            try
            {
                InitializeStreams();
            }
            catch (Exception streamFailure)
            {
                Console.Error.WriteLine("标准流接管失败：" + streamFailure.Message);
            }

            return verb switch
            {
                "help" or "--help" or "-h" => Run(verb, true,
                    "物品 CLI：catalog | list [--class N] [--quality N] [--segment N] | get <id> | "
                    + "validate - | save - | publish <id> | retire <id> | delete <id> | "
                    + "draft save|list|get|delete | export-client --out <path> | "
                    + "preview-tooltip - [--level N] [--setPieces N] | preview-budget --ilvl N --quality Q [--sweep] | "
                    + "preview-random - --seed N | references <id>", null),

                "catalog" => RunCatalog(),
                "list" => RunList(rest),
                "get" => RunGet(rest),
                "validate" => RunValidate(rest),
                "save" => RunSave(rest),
                "publish" => RunSetState(rest, 1),
                "retire" => RunSetState(rest, 2),
                "delete" => RunDelete(rest),
                "draft" => RunDraft(rest),
                "export-client" => RunExportClient(rest),
                "preview-tooltip" => RunPreviewTooltip(rest),
                "preview-budget" => RunPreviewBudget(rest),
                "preview-random" => RunPreviewRandom(rest),
                "references" => RunReferences(rest),
                _ => Run(verb, false, "未知动词：" + verb + "（跑 help 看用法）", null),
            };
        }
        catch (Exception ex)
        {
            // 上报必须带 errorType/errorStack：只回 ex.Message 的话，同一个神秘异常会被猜三次。
            return Run(verb, false, "执行失败：" + ex.GetBaseException().Message, new Dictionary<string, object?>
            {
                ["errorType"] = ex.GetType().FullName,
                ["errorStack"] = ex.ToString(),
            });
        }
    }

    // ---------------- 无库依赖的动词 ----------------

    /// <summary>号段、枚举与内核默认值：编辑器启动时先调这个，不需要连库。</summary>
    private static int RunCatalog()
    {
        var segments = new List<Dictionary<string, object?>>();
        foreach (ItemSegment segment in Enum.GetValues<ItemSegment>())
        {
            if (segment == ItemSegment.Invalid) continue;
            var (min, max) = ItemCore.RangeOf(segment);
            segments.Add(new Dictionary<string, object?>
            {
                ["segment"] = (int)segment,
                ["label"] = ItemCore.SegmentName(segment),
                ["min"] = min,
                ["max"] = max,
            });
        }

        var classes = new List<Dictionary<string, object?>>();
        for (var value = 0; value <= (int)ItemClass.Appearance; value++)
            classes.Add(new Dictionary<string, object?> { ["value"] = value, ["label"] = ItemCore.ClassName(value) });

        var inventoryTypes = new List<Dictionary<string, object?>>();
        for (var value = 0; value <= (int)InventoryType.Bag; value++)
            inventoryTypes.Add(new Dictionary<string, object?>
            {
                ["value"] = value, ["label"] = ItemCore.InventoryTypeName(value),
                ["slotIndex"] = ItemCore.EquipSlotIndex(value),
            });

        var qualities = new List<Dictionary<string, object?>>();
        for (var value = 0; value <= 6; value++)
            qualities.Add(new Dictionary<string, object?>
            {
                ["value"] = value, ["label"] = ItemCore.QualityName(value),
                ["color"] = ItemCore.QualityColorHex(value), ["multiplierBps"] = ItemCore.QualityMultiplierBps(value),
            });

        var statTypes = new List<Dictionary<string, object?>>();
        for (var value = 1; value <= (int)ItemStatType.Energy; value++)
            statTypes.Add(new Dictionary<string, object?>
            {
                ["value"] = value, ["label"] = ItemCore.StatTypeName(value),
                ["key"] = ItemCore.AttributeKey(value), ["percent"] = ItemCore.IsPercentStat(value),
            });

        var data = new Dictionary<string, object?>
        {
            ["coreVersion"] = ItemCore.Version,
            ["segments"] = segments,
            ["classes"] = classes,
            ["inventoryTypes"] = inventoryTypes,
            ["qualities"] = qualities,
            ["statTypes"] = statTypes,
            ["socketColors"] = Enumerable.Range(0, 5).Select(v => new Dictionary<string, object?>
            {
                ["value"] = v, ["label"] = ItemCore.SocketColorName(v),
            }).ToArray(),
            ["limits"] = new Dictionary<string, object?>
            {
                ["maxStatLines"] = ItemSpec.MaxStatLines,
                ["maxDamageLines"] = ItemSpec.MaxDamageLines,
                ["maxSockets"] = ItemSpec.MaxSockets,
                ["maxSpellLines"] = ItemSpec.MaxSpellLines,
                ["maxStackLimit"] = ItemSpec.MaxStackLimit,
                ["maxItemLevel"] = ItemSpec.MaxItemLevel,
                ["maxRequiredLevel"] = ItemCore.MaxRequiredLevel,
            },
        };

        return Run("catalog", true, "号段与内核默认值（不依赖数据库）", data);
    }

    private static int RunPreviewBudget(string[] args)
    {
        if (!TryGetOpt(args, "ilvl", out var itemLevel)) return Run("preview-budget", false, "缺 --ilvl", null);
        var quality = TryGetOpt(args, "quality", out var q) ? q : (int)ItemQuality.Common;

        var single = new Dictionary<string, object?>
        {
            ["itemLevel"] = itemLevel,
            ["quality"] = quality,
            ["qualityName"] = ItemCore.QualityName(quality),
            ["budget"] = ItemCore.StatBudget(itemLevel, quality),
            ["armor"] = ItemCore.ArmorFromBudget(ItemCore.StatBudget(itemLevel, quality), false),
            ["shieldArmor"] = ItemCore.ArmorFromBudget(ItemCore.StatBudget(itemLevel, quality), true),
            ["coreVersion"] = ItemCore.Version,
        };

        var data = new Dictionary<string, object?> { ["single"] = single };

        if (args.Any(a => string.Equals(a, "--sweep", StringComparison.OrdinalIgnoreCase)))
        {
            var sweep = new List<Dictionary<string, object?>>();
            var previous = 0;
            var strictlyIncreasing = true;
            for (var level = 1; level <= Math.Min(itemLevel, ItemSpec.MaxItemLevel); level++)
            {
                var budget = ItemCore.StatBudget(level, quality);
                if (level > 1 && budget <= previous) strictlyIncreasing = false;
                previous = budget;
                sweep.Add(new Dictionary<string, object?>
                {
                    ["itemLevel"] = level, ["budget"] = budget,
                    ["armor"] = ItemCore.ArmorFromBudget(budget, false),
                });
            }
            data["sweep"] = sweep;
            // 判据随结果一起回：调用方不需要再自己比一遍。
            data["strictlyIncreasing"] = strictlyIncreasing;
        }

        return Run("preview-budget", true, "属性预算试算完成", data);
    }

    private static int RunPreviewTooltip(string[] args)
    {
        if (!ReadDraft(args, out var draft, out var readError)) return Run("preview-tooltip", false, readError, null);
        var playerLevel = TryGetOpt(args, "level", out var level) ? level : 0;
        var setPieces = TryGetOpt(args, "setPieces", out var pieces) ? pieces : 0;

        var spec = draft!.ToSpec();

        // 套装明细能读就读：读不到（没连库）就只画"套装 #id"，绝不编造档位文案。
        Horizon.Game.Sim.Items.ItemSetSpec? setSpec = null;
        string setProblem = string.Empty;
        if (spec.SetId > 0)
        {
            if (TryOpenStore(out var setStore, out var setError))
            {
                using (setStore)
                {
                    var setDraft = setStore!.GetSet(spec.SetId);
                    if (setDraft != null) setSpec = setDraft.ToKernelSet();
                }
            }
            else
            {
                setProblem = setError;
            }
        }

        var lines = ItemCore.BuildTooltip(spec, setSpec, setPieces, playerLevel);

        var data = new Dictionary<string, object?>
        {
            ["templateId"] = spec.TemplateId,
            ["setId"] = spec.SetId,
            ["setName"] = setSpec?.Name,
            ["setResolved"] = setSpec != null,
            ["setProblem"] = string.IsNullOrEmpty(setProblem) ? null : setProblem,
            ["lineCount"] = lines.Count,
            ["lines"] = lines.Select(l => new Dictionary<string, object?>
            {
                ["kind"] = l.Kind, ["text"] = l.Text,
            }).ToArray(),
            ["budget"] = ItemCore.StatBudget(spec.ItemLevel, spec.Quality),
            ["dps"] = spec.Damage.Length > 0
                ? ItemCore.WeaponDps(spec.Damage[0].Min, spec.Damage[0].Max, spec.AttackDelayMs)
                : 0,
            ["coreVersion"] = ItemCore.Version,
        };
        return Run("preview-tooltip", true, "tooltip 生成完成", data);
    }

    private static int RunPreviewRandom(string[] args)
    {
        if (!ReadDraft(args, out var draft, out var readError)) return Run("preview-random", false, readError, null);
        var seed = TryGetOpt(args, "seed", out var s) ? s : 1;
        var affix = draft!.Affix;
        if (affix is null || affix.AffixId <= 0)
            return Run("preview-random", false, "草稿里没有词缀（affix.affixId > 0）", null);

        var spec = new ItemAffixSpec
        {
            AffixId = affix.AffixId,
            Kind = affix.Kind,
            Name = affix.Name,
            ValueMin = affix.ValueMin,
            ValueMax = affix.ValueMax,
            Stats = affix.Stats.Select(x => new ItemStatLine { Type = x.StatType, Value = x.StatValue }).ToArray(),
        };
        var roll = ItemCore.RollAffixValue(seed, affix.ValueMin, affix.ValueMax);
        var applied = ItemCore.ApplyAffix(spec, seed);

        var data = new Dictionary<string, object?>
        {
            ["affixId"] = affix.AffixId,
            ["seed"] = seed,
            ["roll"] = roll,
            ["stats"] = applied.Select(l => new Dictionary<string, object?>
            {
                ["type"] = l.Type, ["name"] = ItemCore.StatTypeName(l.Type), ["value"] = l.Value,
            }).ToArray(),
            ["coreVersion"] = ItemCore.Version,
        };
        return Run("preview-random", true, "词缀抽取完成", data);
    }

    // ---------------- 需要连库的动词 ----------------

    private static int RunList(string[] args)
    {
        if (!TryOpenStore(out var store, out var error)) return Run("list", false, error, null);
        using (store)
        {
            var itemClass = TryGetOpt(args, "class", out var c) ? c : (int?)null;
            var quality = TryGetOpt(args, "quality", out var q) ? q : (int?)null;
            ItemSegment? segment = TryGetOpt(args, "segment", out var seg) ? (ItemSegment)seg : null;

            var rows = store!.List(itemClass, quality, segment);
            return Run("list", true, $"共 {rows.Count} 件物品", new Dictionary<string, object?>
            {
                ["rows"] = rows,
                ["count"] = rows.Count,
            });
        }
    }

    private static int RunGet(string[] rest)
    {
        if (!TryParseInt(rest, out var templateId)) return Run("get", false, "缺物品编号", null);
        if (!TryOpenStore(out var store, out var error)) return Run("get", false, error, null);
        using (store)
        {
            var draft = store!.GetDraft(templateId, out var getError);
            if (draft is null) return Run("get", false, getError, null);
            return Run("get", true, $"物品 {templateId}", new Dictionary<string, object?>
            {
                ["draft"] = draft,
                ["draftJson"] = draft.ToJson(),
            });
        }
    }

    private static int RunValidate(string[] rest)
    {
        if (!ReadDraft(rest, out var draft, out var readError)) return Run("validate", false, readError, null);

        var spec = draft!.ToSpec();
        var result = ItemCore.Validate(spec);
        ItemStore? store = null;
        var openError = string.Empty;
        if (TryOpenStore(out store, out openError))
        {
            CrossCheckReferences(store!, draft, result);
            store!.Dispose();
        }
        else if (!string.IsNullOrWhiteSpace(openError))
        {
            result.Warn("参考索引不可用，跨域对账已跳过：" + openError);
        }

        return Run("validate", result.Ok,
            result.Ok ? "校验通过" : $"{result.Errors.Count} 条错误",
            new Dictionary<string, object?>
            {
                ["errors"] = result.Errors,
                ["errorPaths"] = result.ErrorPaths,
                ["warnings"] = result.Warnings,
                ["errorCount"] = result.Errors.Count,
            },
            result.Warnings);
    }

    private static void CrossCheckReferences(ItemStore store, ItemDraft draft, ItemCore.ValidationResult result)
    {
        if (draft.Set is { SetId: > 0 } set && !store.SetExists(set.SetId))
            result.Warn($"套装 {set.SetId} 在库里还不存在：本次保存会把它一并写入");
        if (draft.Affix is { AffixId: > 0 } affix && !store.AffixExists(affix.AffixId))
            result.Warn($"词缀 {affix.AffixId} 在库里还不存在：本次保存会把它一并写入");
        if (draft.Set?.Tiers != null)
        {
            foreach (var tier in draft.Set.Tiers)
            {
                if (tier.SpellId > 0 && !store.SkillExists(tier.SpellId))
                    result.Warn($"套装档 {tier.Pieces} 引用的法术 {tier.SpellId} 在技能表里查不到");
            }
        }
    }

    private static int RunSave(string[] rest)
    {
        if (!ReadDraft(rest, out var draft, out var readError)) return Run("save", false, readError, null);
        var validation = ItemCore.Validate(draft!.ToSpec());
        if (!validation.Ok)
        {
            return Run("save", false, $"校验未通过（{validation.Errors.Count} 条），拒绝写库",
                new Dictionary<string, object?>
                {
                    ["errors"] = validation.Errors,
                    ["errorPaths"] = validation.ErrorPaths,
                });
        }

        if (!TryOpenStore(out var store, out var error)) return Run("save", false, error, null);
        using (store)
        {
            var result = store!.Save(draft);
            return Run("save", result.Ok, result.Message,
                new Dictionary<string, object?>
                {
                    ["templateId"] = result.TemplateId,
                    ["contentVersion"] = result.ContentVersion,
                    ["writtenRows"] = result.WrittenRows,
                },
                result.Warnings);
        }
    }

    private static int RunSetState(string[] rest, int publishState)
    {
        var verb = publishState == 1 ? "publish" : "retire";
        if (!TryParseInt(rest, out var templateId)) return Run(verb, false, "缺物品编号", null);
        if (!TryOpenStore(out var store, out var error)) return Run(verb, false, error, null);
        using (store)
        {
            var result = store!.SetPublishState(templateId, publishState);
            return Run(verb, result.Ok, result.Message,
                new Dictionary<string, object?> { ["writtenRows"] = result.WrittenRows });
        }
    }

    private static int RunDelete(string[] rest)
    {
        if (!TryParseInt(rest, out var templateId)) return Run("delete", false, "缺物品编号", null);
        if (!TryOpenStore(out var store, out var error)) return Run("delete", false, error, null);
        using (store)
        {
            var result = store!.Delete(templateId);
            return Run("delete", result.Ok, result.Message,
                new Dictionary<string, object?> { ["writtenRows"] = result.WrittenRows });
        }
    }

    private static int RunDraft(string[] rest)
    {
        var sub = (rest.Length > 0 ? rest[0] : "list").ToLowerInvariant();
        if (!ItemDraftStore.TryConnect(out var drafts, out var source, out var error))
            return Run("draft " + sub, false, error, null);

        using (drafts)
        {
            switch (sub)
            {
                case "save":
                {
                    if (!ReadDraft(rest.Skip(1).ToArray(), out var draft, out var readError))
                        return Run("draft save", false, readError, null);
                    drafts!.Save(draft!);
                    return Run("draft save", true, $"草稿已写入 Redis（{source}）",
                        new Dictionary<string, object?> { ["templateId"] = draft!.TemplateId });
                }
                case "list":
                {
                    var list = drafts!.List();
                    return Run("draft list", true, $"共 {list.Count} 份草稿",
                        new Dictionary<string, object?> { ["drafts"] = list, ["count"] = list.Count });
                }
                case "get":
                {
                    if (!TryParseInt(rest.Skip(1).ToArray(), out var templateId))
                        return Run("draft get", false, "缺物品编号", null);
                    var body = drafts!.GetRaw(templateId);
                    if (body is null) return Run("draft get", false, $"没有物品 {templateId} 的草稿", null);
                    return Run("draft get", true, $"物品 {templateId} 的草稿",
                        new Dictionary<string, object?> { ["draftJson"] = body });
                }
                case "delete":
                {
                    if (!TryParseInt(rest.Skip(1).ToArray(), out var templateId))
                        return Run("draft delete", false, "缺物品编号", null);
                    var removed = drafts!.Delete(templateId);
                    return Run("draft delete", removed, removed ? "草稿已删除" : "没有这份草稿", null);
                }
                default:
                    return Run("draft " + sub, false, "draft 只支持 save|list|get|delete", null);
            }
        }
    }

    private static int RunExportClient(string[] rest)
    {
        var path = TryGetOptString(rest, "out");
        if (string.IsNullOrWhiteSpace(path)) return Run("export-client", false, "缺 --out <path>", null);
        if (!TryOpenStore(out var store, out var error)) return Run("export-client", false, error, null);
        using (store)
        {
            var bundle = store!.BuildClientBundle(out var published, out var problems);
            bundle["contentRevision"] = 1;
            bundle["exportedAt"] = DateTimeOffset.UtcNow.ToString("O");

            var json = JsonSerializer.Serialize(bundle, OutputOptions);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, json, new UTF8Encoding(false));

            return Run("export-client", published > 0,
                published > 0 ? $"导出 {published} 件已发布物品、{problems.Count} 条问题" : "没有任何已发布物品可导出",
                new Dictionary<string, object?>
                {
                    ["path"] = path, ["count"] = published, ["problems"] = problems,
                });
        }
    }

    /// <summary>
    /// 只读反查：这件物品被哪些掉落表 / 商店引用。用于"删之前先看有没有人用"。
    /// 表不存在时如实回 0 而不是失败（这些表属于别的编辑器）。
    /// </summary>
    private static int RunReferences(string[] rest)
    {
        if (!TryParseInt(rest, out var templateId)) return Run("references", false, "缺物品编号", null);
        if (!TryOpenStore(out var store, out var error)) return Run("references", false, error, null);
        using (store)
        {
            var references = store!.ReferencesOf(templateId);
            return Run("references", true, $"物品 {templateId} 被 {references.Count} 处引用",
                new Dictionary<string, object?> { ["rows"] = references, ["count"] = references.Count });
        }
    }

    // ---------------- 助手 ----------------

    private static bool TryOpenStore(out ItemStore? store, out string error)
    {
        var conn = ConnectionConfig.Resolve(out var hint);
        if (string.IsNullOrWhiteSpace(conn))
        {
            store = null;
            error = "数据库连接串不可用。" + hint;
            return false;
        }
        store = new ItemStore(conn!);
        if (!store.TemplateTableReachable(out var tableError))
        {
            error = "物品表不可达（迁移脚本 scripts/sql/023_item_editor_tables.sql 是否已执行？）：" + tableError;
            store.Dispose();
            store = null;
            return false;
        }
        error = string.Empty;
        return true;
    }

    /// <summary>从 stdin（"-"）或文件读草稿；顺带做<b>键名自证</b>：未知键说明两侧漂移，而 STJ 会静默丢弃它。</summary>
    private static bool ReadDraft(string[] rest, out ItemDraft? draft, out string error)
    {
        draft = null;
        error = string.Empty;
        var source = rest.Length > 0 && !rest[0].StartsWith("--", StringComparison.Ordinal) ? rest[0] : "-";
        string json;
        try
        {
            json = source == "-" ? _stdin.ReadToEnd() : File.ReadAllText(source);
        }
        catch (Exception ex)
        {
            error = "读取草稿失败：" + ex.GetBaseException().Message;
            return false;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "草稿为空（stdin 没有内容）。";
            return false;
        }

        var unknownKeys = FindUnknownKeys(json);
        if (unknownKeys.Count > 0)
        {
            error = "草稿含未知键，将被静默丢弃：" + string.Join(", ", unknownKeys)
                    + "（请对齐 ItemDraft.KnownTopLevelKeys / KnownSectionKeys；两侧字段名是协议）";
            return false;
        }

        draft = ItemDraft.FromJson(json);
        if (draft == null)
        {
            error = "JSON 反序列化失败";
            return false;
        }
        return true;
    }

    /// <summary>
    /// 找出顶层与子段里对不上已知键集合的键名。一次真实缺陷模式：字段名漂一个字母，
    /// STJ 不报错、值全是初值，而"两侧同为 0"让自校验反而全绿。
    /// </summary>
    private static List<string> FindUnknownKeys(string json)
    {
        var unknown = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return unknown;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!ItemDraft.KnownTopLevelKeys.Contains(prop.Name))
                {
                    unknown.Add(prop.Name);
                    continue;
                }
                if (!ItemDraft.KnownSectionKeys.TryGetValue(prop.Name, out var allowed)) continue;

                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    CollectUnknown(prop.Value, allowed, prop.Name, unknown);
                    if (ItemDraft.KnownSectionKeys.TryGetValue(prop.Name + ".tiers", out var tierKeys)
                        && prop.Value.TryGetProperty("tiers", out var tiers)
                        && tiers.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tier in tiers.EnumerateArray())
                        {
                            CollectUnknown(tier, tierKeys, prop.Name + ".tiers[]", unknown);
                            if (ItemDraft.KnownSectionKeys.TryGetValue(prop.Name + ".tiers.stats", out var tierStatKeys)
                                && tier.TryGetProperty("stats", out var tierStats)
                                && tierStats.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var stat in tierStats.EnumerateArray())
                                    CollectUnknown(stat, tierStatKeys, prop.Name + ".tiers[].stats[]", unknown);
                            }
                        }
                    }
                    if (ItemDraft.KnownSectionKeys.TryGetValue(prop.Name + ".stats", out var childStatKeys)
                        && prop.Value.TryGetProperty("stats", out var childStats)
                        && childStats.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var stat in childStats.EnumerateArray())
                            CollectUnknown(stat, childStatKeys, prop.Name + ".stats[]", unknown);
                    }
                }
                else if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in prop.Value.EnumerateArray())
                        CollectUnknown(item, allowed, prop.Name + "[]", unknown);
                }
            }
        }
        catch (JsonException)
        {
            // 交给反序列化那一步去报"JSON 坏了"，这里不重复报。
        }
        return unknown;
    }

    private static void CollectUnknown(JsonElement element, string[] allowed, string prefix, List<string> unknown)
    {
        if (element.ValueKind != JsonValueKind.Object) return;
        foreach (var child in element.EnumerateObject())
        {
            if (!allowed.Contains(child.Name)) unknown.Add(prefix + "." + child.Name);
        }
    }

    private static bool TryParseInt(string[] args, out int value)
    {
        foreach (var a in args)
        {
            if (a.StartsWith("--", StringComparison.Ordinal)) continue;
            var candidate = a.Contains('=') ? a.Split('=', 2)[1] : a;
            if (int.TryParse(candidate, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return true;
        }
        value = 0;
        return false;
    }

    private static bool TryGetOpt(string[] args, string name, out int value)
    {
        value = 0;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], "--" + name, StringComparison.OrdinalIgnoreCase)) continue;
            return int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
        var prefix = "--" + name + "=";
        var hit = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (hit != null) return int.TryParse(hit.Substring(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        return false;
    }

    private static string? TryGetOptString(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--" + name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        }
        var prefix = "--" + name + "=";
        var hit = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return hit != null ? hit.Substring(prefix.Length) : null;
    }

    private static int Run(string verb, bool ok, string message, object? data = null, List<string>? warnings = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["ok"] = ok,
            ["verb"] = verb,
            ["message"] = message,
            ["data"] = data,
            ["warnings"] = warnings != null && warnings.Count > 0 ? warnings : null,
        };
        _stdout.Write(JsonSerializer.Serialize(payload, OutputOptions));
        _stdout.Write("\n");
        return ok ? 0 : 1;
    }
}
