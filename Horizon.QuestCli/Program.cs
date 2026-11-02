using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Horizon.Entities;
using Horizon.Game.Sim;
using Horizon.Game.Sim.Quests;
using StackExchange.Redis;

namespace Horizon.QuestCli;

/// <summary>
/// 任务模板的命令行入口：任务编辑器（Flax 插件）以"短生命周期子进程"的方式调用它，
/// 由它把校验和写库这两件只在服务端一侧才说得清的事做完。
/// <para>
/// 输出恒为一段 JSON（UTF-8），字段固定 <c>ok/verb/message/data/warnings</c>；
/// 出错也走同一段 JSON，退出码非 0。这样编辑器不需要解析人类可读文本，
/// 也不会因为一条错误信息的措辞变化就改解析代码。
/// </para>
/// </summary>
public static class Program
{
    /// <summary>标准输出用的 JSON 约定（与草稿同一套驼峰命名）。</summary>
    private static readonly JsonSerializerOptions OutputOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        // 中文原样输出：这段 JSON 只被解析、不会被拼进 HTML，转成 \uXXXX 只是让日志没法读。
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>UTF-8（无 BOM）的标准输出。刻意不用 <see cref="Console.Out"/>：它的编码取决于控制台码页。</summary>
    private static TextWriter _stdout = Console.Out;

    /// <summary>UTF-8 的标准输入（容忍 BOM）。同理不用 <see cref="Console.In"/>。</summary>
    private static TextReader _stdin = Console.In;

    /// <summary>
    /// 显式接管两条标准流。
    /// <para>
    /// 为什么不靠 <c>Console.OutputEncoding/InputEncoding</c>：在**被重定向的句柄**上这个 setter 会抛
    /// <see cref="IOException"/>，原来的写法把异常 catch 掉之后编码就悄悄退回控制台码页（本机 936）。
    /// 后果有两个，都实测过：① 编辑器送来的 UTF-8 BOM 被按 GBK 解成 <c>ï</c>，STJ 报
    /// <c>'0xEF' is an invalid start of a value</c>；② 中文任务名在管道里被读坏——
    /// 这正是"库能写进去但名字是乱码"那类静默缺陷。
    /// </para>
    /// </summary>
    private static void InitializeStreams()
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        _stdout = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true };
        _stdin = new StreamReader(Console.OpenStandardInput(), utf8, detectEncodingFromByteOrderMarks: true);
    }

    /// <summary>入口。</summary>
    /// <param name="args">动词与参数。</param>
    /// <returns>0 = ok=true；1 = 失败。</returns>
    public static int Main(string[] args)
    {
        var verb = (args.Length > 0 ? args[0] : "help").Trim().ToLowerInvariant();
        var rest = args.Skip(1).ToArray();

        try
        {
            // 流接管失败不影响主流程（诊断信息走 stderr），但绝不静默退回码页：
            // 退回之后 JSON 还能输出、只有中文会坏，那种坏法最难查。
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
                "help" or "--help" or "-h" => Run(verb, true, "任务模板 CLI：catalog | list | get <id> | validate <file|-> | draft save|list|get|delete | publish <id> | online <id> | unpublish <id> [--force] | retire <id> | delete <id>（只允许删已作废的；已接该任务的角色不受影响）。草稿住 Redis（72 小时、每次保存续期），只有 publish 会把内容写进 SQL Server 并置为已发布。", null),
                "catalog" => RunCatalog(rest),
                "list" => RunList(rest),
                "get" => RunGet(rest),
                "validate" => RunValidate(rest),
                "upsert" => RunUpsert(rest),
                "draft" => RunDraft(rest),
                // publish 的语义已改成"草稿落库并发布"（Redis 是编辑期唯一落点，SQL 只在发布那一下被写）。
                // 原来"把库里已有的下线行放上线"这个动作保留成 online，别把它和 publish 混在一个词上。
                "publish" => RunPublish(rest),
                "online" => RunSetState(rest, (int)QuestPublishState.Online),
                "unpublish" => RunSetState(rest, (int)QuestPublishState.Offline),
                "retire" => RunRetire(rest),
                "delete" => RunDelete(rest),
                _ => Run(verb, false, "未知动词：" + verb + "（跑 help 看用法）", null),
            };
        }
        catch (Exception ex)
        {
            return Run(verb, false, "执行失败：" + ex.GetBaseException().Message, null);
        }
    }

    /// <summary>
    /// 收集 Redis 里的草稿，供 <c>catalog</c> 一起返回。
    /// Redis 不可用时给空数组 + 一句原因——草稿层坏掉不该让整个目录刷新失败。
    /// </summary>
    private static (object[] Items, string Hint) CollectDrafts()
    {
        if (!QuestDraftStore.TryConnect(out var drafts, out var source, out var error))
            return (Array.Empty<object>(), error);

        using (drafts)
        {
            var items = drafts!.List().Select(entry => (object)new
            {
                questId = entry.QuestId,
                questName = entry.QuestName,
                ttlHours = Math.Round(entry.TtlMilliseconds / 3600000.0, 1),
            }).ToArray();
            return (items, "草稿层可用（" + source + "）");
        }
    }

    private static int RunCatalog(string[] rest)
    {
        var monsters = MonsterTemplateCatalog.RegisteredTemplates()
            .Select(info => new { templateId = info.TemplateId, name = info.Name })
            .ToArray();

        if (!TryOpenStore(out var store, out var error)) return Run("catalog", false, error, null);

        using (store)
        {
            // NPC 下拉**以库为准**：静态目录只有 1/2/3，新建/发布的 NPC 会从下拉里消失、校验还会报 Missing。
            var npcs = store!.AllNpcTemplates()
                .Select(npc => new
                {
                    templateId = npc.Id,
                    name = npc.Name,
                    title = npc.Title,
                    modelKey = npc.ModelKey,
                    publishState = npc.PublishState,
                })
                .ToArray();

            // 奖励与"任务物品/触发物"的物品下拉：以库为准（那才是发奖时真的会查的那张表）。
            var items = store!.AllItems()
                .Select(item => new
                {
                    templateId = item.Id,
                    name = item.ItemName,
                    itemType = item.ItemType,
                    typeName = ItemTypeName(item.ItemType),
                    quality = item.BaseQuality,
                    levelRequire = item.LevelRequire,
                })
                .ToArray();

            var quests = store.AllQuests()
                .Select(row => new
                {
                    questId = row.Id,
                    questName = row.QuestName,
                    questType = row.QuestType,
                    publishState = row.PublishState,
                    valid = row.IsValid,
                    contentVersion = row.ContentVersion,
                })
                .ToArray();

            // 草稿顺手一起回：编辑器每次刷新都要它，而单开一次子进程等于每次刷新多付一遍
            // "进程启动 + 连 Redis"的延迟——"点一下卡一下"里有一半是这么来的。
            var draftData = CollectDrafts();

            var data = new
            {
                npcs,
                monsters,
                items,
                quests,
                objectiveKinds = Enum.GetValues(typeof(QuestObjectiveKind)).Cast<QuestObjectiveKind>()
                    .Select(kind => new
                    {
                        kind = kind.ToString(),
                        verb = QuestObjectiveCodec.VerbOf(kind),
                        hasProducer = QuestObjectiveCodec.HasProducer(kind),
                    })
                    .Where(entry => entry.verb != null)
                    .ToArray(),
                entityKinds = new[]
                {
                    new { kind = "npc", canBeGiver = QuestEntityRef.CanBeGiver(QuestEntityKind.Npc) },
                    new { kind = "monster", canBeGiver = QuestEntityRef.CanBeGiver(QuestEntityKind.Monster) },
                    new { kind = "item", canBeGiver = QuestEntityRef.CanBeGiver(QuestEntityKind.Item) },
                    new { kind = "prop", canBeGiver = QuestEntityRef.CanBeGiver(QuestEntityKind.Prop) },
                    new { kind = "zone", canBeGiver = QuestEntityRef.CanBeGiver(QuestEntityKind.Zone) },
                },
                drafts = draftData.Items,
                draftsHint = draftData.Hint,
            };

            return Run("catalog", true, "ok", data);
        }
    }

    private static int RunList(string[] rest)
    {
        if (!TryOpenStore(out var store, out var error)) return Run("list", false, error, null);
        using (store)
        {
            var data = store.AllQuests().Select(row => new
            {
                questId = row.Id,
                questName = row.QuestName,
                questType = row.QuestType,
                levelRequire = row.LevelRequire,
                giverKind = row.GiverKind,
                giverTemplateId = row.GiverTemplateId,
                publishState = row.PublishState,
                valid = row.IsValid,
                contentVersion = row.ContentVersion,
            }).ToArray();
            return Run("list", true, "共 " + data.Length + " 条。", data);
        }
    }

    private static int RunGet(string[] rest)
    {
        if (!TryId(rest, out var questId, out var error)) return Run("get", false, error, null);
        if (!TryOpenStore(out var store, out var error2)) return Run("get", false, error2, null);

        using (store)
        {
            var row = store!.FindQuest(questId);
            if (row == null) return Run("get", false, "任务模板不存在：" + questId, null);
            var draft = QuestTemplateMapper.ToDraft(row);
            return Run("get", true, "ok", new { draft, json = draft.ToJson() });
        }
    }

    private static int RunValidate(string[] rest)
    {
        if (!ReadDraft(rest, out var draft, out var error)) return Run("validate", false, error, null);
        if (!TryOpenStore(out var store, out var error2)) return Run("validate", false, error2, null);

        using (store)
        {
            var result = QuestTemplateValidator.Validate(draft, store!.Index());
            var data = new
            {
                hasErrors = result.HasErrors,
                issues = result.Issues.Select(issue => new
                {
                    code = issue.Code,
                    level = issue.Level.ToString(),
                    path = issue.Path,
                    message = issue.Message,
                }).ToArray(),
            };
            return Run("validate", !result.HasErrors, result.HasErrors ? "校验有错误。" : "校验通过。", data);
        }
    }

    /// <summary>
    /// 草稿的增删查（只碰 Redis，不碰 SQL）。
    /// <para>
    /// 这一层刻意**不做校验**：草稿之所以叫草稿，就是允许半成品存在。校验是"校验"按钮那一档，
    /// 由 <c>validate</c> 走服务端规则单独回答；发布时再校验一次才是硬门槛。
    /// </para>
    /// </summary>
    private static int RunDraft(string[] rest)
    {
        var sub = (rest.Length > 0 ? rest[0] : "").Trim().ToLowerInvariant();
        var tail = rest.Skip(1).ToArray();

        if (!TryOpenDraftStore(out var drafts, out var storeError))
            return Run("draft", false, storeError, null);

        using (drafts)
        {
            switch (sub)
            {
                case "save":
                    if (!ReadDraft(tail, out var draft, out var readError))
                        return Run("draft", false, readError, null);
                    drafts!.Save(draft!);
                    return Run("draft", true,
                        "草稿已写入 Redis（不落库）。72 小时内可发布，每次保存都会续期。Redis 来源：" + _draftSource,
                        new
                        {
                            questId = draft!.QuestId,
                            key = QuestDraftStore.BodyKey(draft.QuestId),
                            ttlHours = (int)QuestDraftStore.DraftTtl.TotalHours,
                            redisSource = _draftSource,
                        });

                case "list":
                    var entries = drafts!.List().Select(entry => new
                    {
                        questId = entry.QuestId,
                        questName = entry.QuestName,
                        lastWrite = entry.LastWrite.ToString("yyyy-MM-dd HH:mm:ss"),
                        ttlHours = Math.Round(entry.TtlMilliseconds / 3600000.0, 1),
                    }).ToArray();
                    return Run("draft", true, entries.Length == 0
                        ? "Redis 里没有未过期的草稿。"
                        : $"草稿 {entries.Length} 条（都在 Redis 里，未落库）。",
                        new { count = entries.Length, drafts = entries });

                case "get":
                    if (!TryId(tail, out var getId, out var idError))
                        return Run("draft", false, idError, null);
                    var raw = drafts!.GetRaw(getId);
                    if (raw == null)
                        return Run("draft", false, $"草稿 {getId} 不存在（没保存过，或已过 72 小时被 Redis 自动清除）。", null);
                    return Run("draft", true, $"草稿 {getId} 原文。", new { questId = getId, json = raw });

                case "delete":
                    if (!TryId(tail, out var deleteId, out var deleteIdError))
                        return Run("draft", false, deleteIdError, null);
                    var deleted = drafts!.Delete(deleteId);
                    return Run("draft", true,
                        deleted ? $"草稿 {deleteId} 已删除。" : $"草稿 {deleteId} 本来就不存在。",
                        new { questId = deleteId, deleted });

                default:
                    return Run("draft", false, "用法：draft save <file|-> | draft list | draft get <id> | draft delete <id>", null);
            }
        }
    }

    /// <summary>
    /// 发布 = **唯一**把内容写进 SQL Server 的动作：取 Redis 草稿 → 服务端校验 → 落库 → 置已发布 → 删草稿。
    /// <para>
    /// 失败时不留半截状态：内容校验不过就一行都不写，草稿原地不动；内容过了但"发布"这步被拒，
    /// 会明确回一句"已入库为下线状态"——把库里的真实状态说出来，而不是笼统报个失败让作者以为没写。
    /// </para>
    /// </summary>
    private static int RunPublish(string[] rest)
    {
        if (!TryId(rest, out var questId, out var idError))
            return Run("publish", false, idError, null);

        if (!TryOpenDraftStore(out var drafts, out var storeError))
            return Run("publish", false, "发布要先读草稿，而 " + storeError, null);

        using (drafts)
        {
            var draft = drafts!.Get(questId);
            if (draft == null)
                return Run("publish", false,
                    $"Redis 里没有草稿 {questId}：它可能从没保存过，或已过 72 小时被清除。" +
                    "要发布库里已有的行请用 online。", null);

            if (!TryOpenStore(out var store, out var sqlError))
                return Run("publish", false, sqlError, null);

            using (store)
            {
                var content = store!.Upsert(draft);
                if (!content.Ok)
                    // 内容不合规：**一行都不写**，草稿留在 Redis 里让作者改。
                    return Run("publish", false, "内容未通过校验，未落库：" + content.Message,
                        new { questId, warnings = content.Warnings });

                var published = store.SetPublishState(questId, (int)QuestPublishState.Online);
                if (!published.Ok)
                {
                    return Run("publish", false,
                        "内容已入库为**下线**状态，但发布被拒：" + published.Message +
                        "（草稿保留在 Redis，改完再发布）",
                        new { questId, contentVersion = content.ContentVersion, publishState = (int)QuestPublishState.Offline });
                }

                // 内容权威权此刻交给 SQL：草稿退场，避免同一任务短期存在两份可能不一致的内容。
                drafts.Delete(questId);
                return Run("publish", true, "已落库并发布；Redis 草稿已删除。",
                    new
                    {
                        questId,
                        contentVersion = content.ContentVersion,
                        publishState = (int)QuestPublishState.Online,
                        catalogHint = "正在跑的 Grain 会在目录缓存 TTL 内自行看见这条（不必重启）。",
                    });
            }
        }
    }

    private static bool TryOpenDraftStore(out QuestDraftStore? drafts, out string error)
    {
        if (QuestDraftStore.TryConnect(out drafts, out var source, out error))
        {
            // 连上哪一份要留痕：本机同时有 .env 与环境变量时，"这次用的是哪个地址"是排错第一问。
            _draftSource = source;
            return true;
        }

        return false;
    }

    /// <summary>草稿层的实际来源（连接串从哪儿来的），失败信息里会带上。</summary>
    private static string _draftSource = string.Empty;

    private static int RunUpsert(string[] rest)
    {
        if (!ReadDraft(rest, out var draft, out var error)) return Run("upsert", false, error, null);
        if (!TryOpenStore(out var store, out var error2)) return Run("upsert", false, error2, null);

        using (store)
        {
            var result = store!.Upsert(draft!);
            return Run("upsert", result.Ok, result.Message, new
            {
                questId = result.QuestId,
                contentVersion = result.ContentVersion,
                warnings = result.Warnings,
            });
        }
    }

    private static int RunSetState(string[] rest, int publishState)
    {
        var verbName = publishState == (int)QuestPublishState.Online ? "publish" : "unpublish";
        if (!TryId(rest, out var questId, out var error)) return Run(verbName, false, error, null);
        if (!TryOpenStore(out var store, out var error2)) return Run(verbName, false, error2, null);

        using (store)
        {
            var result = store!.SetPublishState(questId, publishState);
            var warnings = new List<string>(result.Warnings);
            if (publishState == (int)QuestPublishState.Offline && rest.Any(arg => arg.Equals("--force", StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add("[F001] force: CLI 只改库；要真的强制放弃玩家身上进行中的实例，须调 IQuestManagerGrain.UnpublishAsync(questId, forceAbandonActive:true)。");
            }
            warnings.Add("[F002] 运行时缓存默认 10 秒后生效；立即生效可调 IQuestManagerGrain.GetCatalogAsync(forceRefresh:true)。");
            return Run(verbName, result.Ok, result.Message, new
            {
                questId = result.QuestId,
                publishState,
                contentVersion = result.ContentVersion,
                warnings,
            });
        }
    }

    private static int RunRetire(string[] rest)
    {
        if (!TryId(rest, out var questId, out var error)) return Run("retire", false, error, null);
        if (!TryOpenStore(out var store, out var error2)) return Run("retire", false, error2, null);
        using (store)
        {
            var result = store!.Retire(questId);
            if (result.Ok)
                return Run("retire", true, result.Message, new { questId = result.QuestId, contentVersion = result.ContentVersion });

            // 库里没有这一行时，最可能的情况是"它只是一份从未发布的草稿"——那不需要作废，直接删即可。
            // 光回一句"任务模板不存在"，作者会以为是系统出了问题。
            var isDraftOnly = QuestDraftStore.TryConnect(out var drafts, out _, out _)
                              && drafts!.GetRaw(questId) != null;
            using (drafts)
            {
                return Run("retire", false, result.Message + (isDraftOnly
                    ? "（它是一份**未发布的草稿**：草稿不需要作废，直接点「删除」即可）"
                    : string.Empty), null);
            }
        }
    }

    /// <summary>
    /// 删除一条任务：**草稿可直接删，库里的行必须先作废**。
    /// <para>
    /// 为什么草稿不需要那道门：作废是"下线 + 停止新接取"的确认，而**从未发布过的草稿根本没上线过**，
    /// 没有任何角色可能持有它——再要求先作废只是让人走一步无意义的流程（而且会报"任务模板不存在"，
    /// 因为库里压根没有行）。反过来，库里存在且仍有效的行一律拒绝：那才是"有人接得到"的东西，
    /// 必须先作废；并且**不顺手删掉草稿**——若只是想放弃未发布的改动，那是另一个意图
    /// （用 <c>draft delete</c>），不该由"删除"悄悄代做。
    /// </para>
    /// </summary>
    private static int RunDelete(string[] rest)
    {
        if (!TryId(rest, out var questId, out var error)) return Run("delete", false, error, null);

        // 草稿是"未发布"的唯一载体，先握住它（连不上只是拿不到它，不该挡住删行那条路）。
        var hasDraftStore = QuestDraftStore.TryConnect(out var drafts, out _, out _);
        using (drafts)
        {
            var hadDraft = hasDraftStore && drafts!.GetRaw(questId) != null;

            if (!TryOpenStore(out var store, out var sqlError))
            {
                if (!hadDraft) return Run("delete", false, sqlError, null);

                // 库连不上也不该卡住一个纯 Redis 的操作；但必须说清"没核对过行"，别让人以为整条都清了。
                drafts!.Delete(questId);
                return Run("delete", true,
                    "已删除草稿（Redis）。库连不上，未核对库里是否有同 id 的行——若有，请连库后再确认一次：" + sqlError,
                    new { questId, deletedDraft = true, deletedRow = false });
            }

            using (store)
            {
                var row = store!.FindQuest(questId);

                if (row == null)
                {
                    if (!hadDraft)
                        return Run("delete", false, "任务 " + questId + " 不存在：库里没有这一行，Redis 里也没有草稿。", null);

                    drafts!.Delete(questId);
                    return Run("delete", true,
                        "已删除草稿（从未发布过，所以不必先作废；库里本来也没有这一行）。",
                        new { questId, deletedDraft = true, deletedRow = false });
                }

                if (row.IsValid)
                {
                    // 有效行一律拒绝，而且**不顺手删草稿**：那可能是作者正在改的东西，
                    // "放弃未发布的改动"是另一个意图（draft delete），不该由"删除"代做。
                    return Run("delete", false,
                        "库里有这一行且仍在有效状态，不能直接删除（有人可能接得到）：请先「作废」。" +
                        (hadDraft ? "（这条任务另有一份未发布的草稿，已保留——只想放弃那份改动请用 draft delete）" : string.Empty),
                        new { questId, deletedDraft = false, deletedRow = false, hasDraft = hadDraft });
                }

                var deleted = store.Delete(questId);
                if (!deleted.Ok) return Run("delete", false, deleted.Message, null);

                var draftRemoved = hadDraft && drafts != null;
                if (draftRemoved) drafts!.Delete(questId);

                return Run("delete", true,
                    "已物理删除。已接这条任务的角色不受影响：仍能继续推进、提交并查看详情。" +
                    (draftRemoved ? "（同时清掉了它的 Redis 草稿）" : string.Empty),
                    new { questId, deletedDraft = draftRemoved, deletedRow = true });
            }
        }
    }

    private static bool TryOpenStore(out QuestStore? store, out string error)
    {
        var connection = ConnectionConfig.Resolve(out var hint);
        if (string.IsNullOrWhiteSpace(connection))
        {
            store = null;
            error = "拿不到 Game 库连接串。" + hint;
            return false;
        }

        try
        {
            store = new QuestStore(connection);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            store = null;
            error = "打不开数据库：" + ex.GetBaseException().Message;
            return false;
        }
    }

    private static bool TryId(string[] rest, out int questId, out string error)
    {
        questId = 0;
        error = string.Empty;
        var first = rest.FirstOrDefault(arg => !arg.StartsWith("-", StringComparison.Ordinal));
        if (first == null || !int.TryParse(first, out questId) || questId <= 0)
        {
            error = "需要一个正整数任务 id。";
            return false;
        }
        return true;
    }

    private static bool ReadDraft(string[] rest, out QuestDraft? draft, out string error)
    {
        draft = null;
        error = string.Empty;

        var path = rest.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal));
        string? json;
        try
        {
            if (string.Equals(path, "-", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(path))
                // TrimStart('\uFEFF')：StreamReader 的 BOM 探测只在真的存在 BOM 时生效，
                // 而"BOM 已被上游当普通字符送进来"这条路上，字符串首会留一个 U+FEFF，STJ 照样拒。
                json = _stdin.ReadToEnd().TrimStart('\uFEFF');
            else
                json = File.ReadAllText(path).TrimStart('\uFEFF');
        }
        catch (Exception ex)
        {
            error = "读草稿失败：" + ex.Message;
            return false;
        }

        draft = QuestDraft.TryParse(json, out var parseError);
        if (draft == null)
        {
            error = parseError ?? "草稿解析失败。";
            return false;
        }
        return true;
    }

    private static string ItemTypeName(int itemType) => itemType switch
    {
        0 => "武器",
        1 => "防具",
        2 => "饰品",
        3 => "消耗品",
        4 => "材料",
        5 => "任务物品",
        6 => "秘籍",
        7 => "配方",
        8 => "宝箱",
        _ => "未知",
    };

    private static int Run(string verb, bool ok, string message, object? data)
    {
        var payload = new Dictionary<string, object?>
        {
            ["ok"] = ok,
            ["verb"] = verb,
            ["message"] = message,
            ["data"] = data,
        };
        _stdout.WriteLine(JsonSerializer.Serialize(payload, OutputOptions));
        return ok ? 0 : 1;
    }
}

/// <summary>让 <see cref="QuestStore"/> 直接当校验器的引用对账口用。</summary>
internal static class QuestStoreExtensions
{
    /// <summary>自身即索引（CLI 连的是权威库，物品/任务都判得准）。</summary>
    public static IQuestReferenceIndex Index(this QuestStore store) => store;
}
