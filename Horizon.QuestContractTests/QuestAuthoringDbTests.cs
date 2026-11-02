using System;
using System.Collections.Generic;
using System.Linq;
using Horizon.Entities;
using Horizon.Game.Sim.Quests;
using Horizon.Model.GameModel;
using Horizon.QuestCli;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Horizon.QuestContractTests;

/// <summary>
/// 任务作者闭环对**真实 SQL Server** 的端到端验证（环境门控）。
/// <para>
/// <b>为什么要连真库跑</b：模板权威源是 <c>Game_HunduShijie_QuestTemplate</c>。
/// 离线单测只能证明"草稿↔线格式"自洽，证不了四件最容易翻车的事：
/// ① 显式 id 能否写进 IDENTITY 列（<c>SET IDENTITY_INSERT</c> 必须占住同一条连接）；
/// ② 校验器能否查出"奖励指向库里不存在的物品"——这条只有连库才判得准；
/// ③ <c>publish_state</c> 与 <c>content_version</c> 是否真按发布/更新自增；
/// ④ 迁移没应用时会不会静默半套。
/// </para>
/// <para>
/// <b>门控与护栏</b>：未设 <c>HORIZON_QUEST_DB=1</c> 时整个类直接返回（视为跳过），不污染无库环境；
/// 开启时只用探针号段 <see cref="ProbeQuestId"/>/<see cref="ProbeBadId"/>，进出自删，
/// 不碰 90001-90005 / 10001 / 20001 这些真数据（它们由 012/014 种子管）。
/// </para>
/// <para>
/// 运行方式：
/// <code>
/// dotnet test Horizon.QuestContractTests --filter "FullyQualifiedName~QuestAuthoringDbTests" `
///   --logger "console;verbosity=detailed"
/// # 环境变量 HORIZON_QUEST_DB=1；连接串取 DB_GAME_CONNECTION_STRING 或 Silo/Gateway 的 bin/.env
/// </code>
/// </para>
/// </summary>
public class QuestAuthoringDbTests
{
    /// <summary>探针任务 id（与正式号段隔离，测试结束物理删除）。</summary>
    private const int ProbeQuestId = 95001;

    /// <summary>第二枚探针：专门用来验"发布被校验拦下"。</summary>
    private const int ProbeBadId = 95002;

    private static bool Enabled => Environment.GetEnvironmentVariable("HORIZON_QUEST_DB") == "1";

    [Fact]
    public void 作者闭环_草稿入库到发布再到作废()
    {
        if (!Enabled) return;

        var connection = ConnectionConfig.Resolve(out var hint);
        Assert.True(!string.IsNullOrWhiteSpace(connection), "连不上 Game 库：" + hint);

        Purge(connection!);     // 上一次中断留下的残行会污染断言，先清干净（幂等护栏）

        try
        {
            using var store = new QuestStore(connection!);

            // ── ① 新建：显式 id 写进 IDENTITY 列 ─────────────────
            var draft = NewDraft(ProbeQuestId);
            var created = store.Upsert(draft);
            Assert.True(created.Ok, created.Message);
            Assert.Equal(ProbeQuestId, created.QuestId);
            Assert.Equal(1, created.ContentVersion);
            Assert.Empty(created.Warnings);

            // ── ② 读回：每一列都要原样回来 ───────────────────────
            var row = store.FindQuest(ProbeQuestId);
            Assert.NotNull(row);
            Assert.Equal((int)QuestEntityKind.Npc, row!.GiverKind);
            Assert.Equal(3, row.GiverTemplateId);
            Assert.Equal((int)QuestPublishState.Offline, row.PublishState);
            Assert.Contains("kill_monster:4", row.Objectives);
            Assert.Contains("kill_domain:5", row.Objectives);
            Assert.Contains("\"item:810101\"", row.Rewards);
            Assert.Equal("90001", row.PrerequisiteQuests);

            var back = QuestTemplateMapper.ToDraft(row);
            Assert.Equal(draft.QuestName, back.QuestName);
            Assert.Equal(ProbeQuestId, back.QuestId);
            Assert.Equal(2, back.Objectives.Count);
            // 三类档位的奖励必须**逐条原样出来**（顺序、id、数量都对），否则"奖励可关联所有游戏物品"
            // 只是下拉里能选到，落库这一层其实会吞档。
            Assert.Equal(3, back.Rewards.Items.Count);
            Assert.Equal(810101, back.Rewards.Items[0].TemplateId);
            Assert.Equal(1, back.Rewards.Items[0].Count);
            Assert.Equal(10001, back.Rewards.Items[1].TemplateId);
            Assert.Equal(2, back.Rewards.Items[1].Count);
            Assert.Equal(700001, back.Rewards.Items[2].TemplateId);
            Assert.Equal(5, back.Rewards.Items[2].Count);

            // ── ③ 更新：同一行版本号自增，不产生第二条 ────────────
            draft.QuestName = "探针任务·改名";
            var updated = store.Upsert(draft);
            Assert.True(updated.Ok, updated.Message);
            Assert.Equal(2, updated.ContentVersion);
            Assert.Equal(1, store.AllQuests().Count(item => item.Id == ProbeQuestId));
            Assert.Equal("探针任务·改名", store.FindQuest(ProbeQuestId)!.QuestName);

            // ── ④ 发布：状态位与版本一起对账 ──────────────────────
            var published = store.SetPublishState(ProbeQuestId, (int)QuestPublishState.Online);
            Assert.True(published.Ok, published.Message);
            var publishedRow = store.FindQuest(ProbeQuestId)!;
            Assert.Equal((int)QuestPublishState.Online, publishedRow.PublishState);

            var offline = store.SetPublishState(ProbeQuestId, (int)QuestPublishState.Offline);
            Assert.True(offline.Ok, offline.Message);
            Assert.Equal((int)QuestPublishState.Offline, store.FindQuest(ProbeQuestId)!.PublishState);

            // ── ⑤ 只有连库才判得准的两条拒绝 ──────────────────────
            var bogusItem = NewDraft(ProbeBadId);
            bogusItem.Rewards.Items[0].TemplateId = 99999999;
            var rejectedItem = store.Upsert(bogusItem);
            Assert.False(rejectedItem.Ok, "奖励指向库里不存在的物品时必须被拒（现场表现是交了任务东西没到手）");
            Assert.Contains("Q034", rejectedItem.Message);
            Assert.Null(store.FindQuest(ProbeBadId));

            var unpublishable = NewDraft(ProbeBadId);
            unpublishable.Objectives.Clear();
            unpublishable.Rewards = new QuestRewardSpec();
            var rejectedPublish = store.SetPublishState(ProbeBadId, (int)QuestPublishState.Online);
            Assert.False(rejectedPublish.Ok, "没目标没奖励的任务不能被发布");

            // ── ⑥ 作废（软删）＋ 物品发布者名字解析 ───────────────
            var retired = store.Retire(ProbeQuestId);
            Assert.True(retired.Ok, retired.Message);
            var retiredRow = store.FindQuest(ProbeQuestId)!;
            Assert.False(retiredRow.IsValid);
            Assert.Equal((int)QuestPublishState.Offline, retiredRow.PublishState);
            Assert.Equal(3, retiredRow.ContentVersion);

            var giverName = QuestGiverNaming.Resolve(retiredRow.GiverKind, retiredRow.GiverTemplateId);
            Assert.Equal("游方商队", giverName);
        }
        finally
        {
            Purge(connection!);
        }
    }

    /// <summary>
    /// 奖励选择器的实际覆盖域 = 库里真有的物品类型。
    /// <para>
    /// <b>材料（item_type=4）为什么断言"不存在"</b>：客户端 <c>MaterialDatabase</c> 用的是
    /// 10001-10005 / 20001-20005 / 30001… 这套号，而 <c>ItemTemplate</c> 表里的 10001-10013、
    /// 20001、30001 已经是<b>装备</b>（006 种子落的）。同一个整数在两处不同义时，
    /// 把材料行塞进物品表会让"奖励 item:10001"变成一句读不出来的话。
    /// 所以材料要进奖励域，必须先定新号段（方案文档 §12 的决策点）；这条断言就是那一步的地面：
    /// 一旦有人不先定号段就直接建材料行，这里会立刻红。
    /// </para>
    /// </summary>
    [Fact]
    public void 奖励域覆盖库里真有的物品类型()
    {
        if (!Enabled) return;

        var connection = ConnectionConfig.Resolve(out var hint);
        Assert.True(!string.IsNullOrWhiteSpace(connection), "连不上 Game 库：" + hint);

        using var store = new QuestStore(connection!);
        var items = store.AllItems();
        Assert.NotEmpty(items);

        var types = items.Select(item => item.ItemType).Distinct().OrderBy(value => value).ToArray();
        Assert.Contains(0, types);   // 武器
        Assert.Contains(1, types);   // 防具
        Assert.Contains(2, types);   // 饰品
        Assert.Contains(3, types);   // 消耗品
        Assert.Contains(4, types);   // 材料：015 已入库（id 段 700001..700041，与客户端 MaterialData 同号）
        Assert.Contains(6, types);   // 秘籍

        // 材料这一档是"奖励可关联所有游戏物品"里最后补上的一块，所以要正面钉住：
        // 行数、号段、以及"每一行都有名字"（没名字的话奖励下拉是空白，玩家看不见自己拿到了什么）。
        var materials = items.Where(item => item.ItemType == 4).ToArray();
        Assert.Equal(41, materials.Length);
        Assert.All(materials, item => Assert.InRange(item.Id, 700001, 700041));
        Assert.Equal(41, materials.Select(item => item.Id).Distinct().Count());

        // 秘籍 id 段约定：800000 + 技能 id，必须能作为奖励被选中。
        Assert.Contains(items, item => item.Id >= ItemIdConstants.BookIdBase);
        Assert.All(items, item => Assert.False(string.IsNullOrWhiteSpace(item.ItemName),
                                               $"物品 {item.Id} 没有名字，奖励下拉会显示成空白"));
    }

    [Fact]
    public void 秘籍号段里不得混入非秘籍的物品_现有15件五行素材登记在案()
    {
        if (!Enabled) return;

        var connection = ConnectionConfig.Resolve(out var hint);
        Assert.True(!string.IsNullOrWhiteSpace(connection), "连不上 Game 库：" + hint);

        using var store = new QuestStore(connection!);
        var items = store.AllItems();

        // "奖励可关联所有游戏物品"在**发放**这一侧是无条件的：`QuestHandler.GrantQuestRewardsAsync`
        // 只要 templateId>0 且数量>0 就投给 `IInventoryGrain.AddItemAsync`，而 `AddItemAsync` 自己写着
        // "服务端没有运行时物品表可校验"（InventoryGrain.cs:114-116），既不查类型也不查 max_stack。
        // 所以真正会暴露的是**使用与显示**这两侧——它们都按"id ≥ 800000 就是秘籍"这条号段启发式分流：
        //   · 服务端 `InventoryGrain.UseItemWithResultAsync`（:234）先把这类 id 转投
        //     `SkillGrain.LearnFromBookAsync(id - 800000)`，于是永远走不到 :272 那条按 use_effect 加好感的分支；
        //   · 客户端 `ItemCatalog.Build`（:172）同样先判号段，把这类物品显示成"无名残页/秘籍"。
        // 库里五行素材与信物 910001..910015（15 件）正好落在这个号段里、类型却不是秘籍
        // （10 件素材 item_type=3 且无 use_effect，5 件信物 item_type=2 带 {"kind":"favor"} 效果）。
        // 这 15 件是五行系统的历史号段选择，不是任务系统引入的；但任务奖励一旦指向它们，
        // 玩家拿到的就是"用不了、还显示成残页"的东西。本条断言把现状钉死：**只许这 15 件，多一件立刻判红**。
        // 真要修得让物品名与类别随下行给出，见 TECH_DEBT T9。
        var strays = items
            .Where(item => item.Id >= ItemIdConstants.BookIdBase && item.ItemType != 6)
            .OrderBy(item => item.Id)
            .ToArray();

        Assert.Equal(15, strays.Length);
        Assert.All(strays, item => Assert.InRange(item.Id, 910001, 910015));
        Assert.Equal(15, strays.Select(item => item.Id).Distinct().Count());

        // 后果分两档，数字要各对得上：带好感效果的会被号段启发式**吞掉效果**，
        // 不带效果的只是用不了；混在一起说"都别发"会掩盖真正的损失。
        var withFavorEffect = strays.Where(item => !string.IsNullOrWhiteSpace(item.UseEffect)
                                                 && item.UseEffect!.Contains("\"favor\"")).ToArray();
        Assert.Equal(5, withFavorEffect.Length);
        Assert.Equal(10, strays.Length - withFavorEffect.Length);

        // 真正是秘籍的那一档必须成段存在（奖励下拉里的"技能书"靠它）。
        Assert.Equal(91, items.Count(item => item.Id >= ItemIdConstants.BookIdBase && item.ItemType == 6));
    }

    /// <summary>
    /// 发布者与交付者的"范围限定"落库是**不对称**的，这条把它钉成事实：
    /// <para>
    /// <c>giver</c> 有 <c>giver_zone_id</c> / <c>giver_spawn_point_id</c> 两列，写进去读得回来；
    /// <c>turnIn</c> **没有对应列**（013 只给了发布者），所以草稿里 <c>turnIn.zoneId=7</c> 这类值
    /// 在 upsert 时被静默丢弃，且校验器不会报——与 T12 说的"填了没人读"是同一族，只是发生在存储层。
    /// </para>
    /// 这条断言现在按**现状**写（交付者限定回来是 0）。哪天补了列，它会变红，
    /// 那正是提醒去把这段说明与 <c>TECH_DEBT</c> T12 一起改的时候——不要顺手把断言放宽。
    /// </summary>
    [Fact]
    public void 发布者的范围限定入库而交付者的会被丢弃()
    {
        if (!Enabled) return;

        var connection = ConnectionConfig.Resolve(out var hint);
        Assert.True(!string.IsNullOrWhiteSpace(connection), "连不上 Game 库：" + hint);

        Purge(connection!);
        try
        {
            using var store = new QuestStore(connection!);
            var draft = NewDraft(ProbeQuestId);
            draft.Giver.ZoneId = 12;
            draft.Giver.SpawnPointId = 34;
            draft.TurnIn = new QuestEntityRefDraft { Kind = "npc", TemplateId = 3, ZoneId = 56, SpawnPointId = 78 };

            var created = store.Upsert(draft);
            Assert.True(created.Ok, created.Message);

            var row = store.FindQuest(ProbeQuestId);
            Assert.NotNull(row);
            // 发布者这一侧：两列都在，原样回来。
            Assert.Equal(12, row!.GiverZoneId);
            Assert.Equal(34, row.GiverSpawnPointId);

            // 交付者这一侧：库里根本没有承载它的列 → 读回来必然是 0。
            var back = QuestTemplateMapper.ToDraft(row);
            Assert.NotNull(back.TurnIn);
            Assert.Equal(0, back.TurnIn!.ZoneId);
            Assert.Equal(0, back.TurnIn!.SpawnPointId);

            // 而这一切不给任何警告：作者以为限定了范围，实际限定蒸发。
            Assert.Empty(created.Warnings);
        }
        finally
        {
            Purge(connection!);
        }
    }

    /// <summary>删除专探针（与 95001/95002 分开，免得和上面那条闭环测试互相踩）。</summary>
    private const int ProbeDeleteId = 95003;

    /// <summary>
    /// 作废之后可以**物理删除**，且"必须先作废"这道两步门不能被跨过。
    /// <para>
    /// "删除不影响已接该任务的角色"这半句在这里跑不出来（需要一条真实持有者链路 + 真集群），
    /// 它的依据被单独钉成源码闸门 <see cref="QuestDeletionSafetyTests"/>：接取时名字/目标/奖励
    /// 已抄进角色状态，完成路径不回查模板表。两条合起来才是这条需求的完整证据。
    /// </para>
    /// </summary>
    [Fact]
    public void 作废后可物理删除_未作废不许删()
    {
        if (!Enabled) return;

        var connection = ConnectionConfig.Resolve(out var hint);
        Assert.True(!string.IsNullOrWhiteSpace(connection), "连不上 Game 库：" + hint);

        try
        {
            using var store = new QuestStore(connection!);
            CleanupDeleteProbe(store);

            Assert.True(store.Upsert(NewDraft(ProbeDeleteId)).Ok, "探针草稿入库失败");

            // ① 还在有效状态就直接删 → 必须被拒，并说清"先作废"。
            var tooEarly = store.Delete(ProbeDeleteId);
            Assert.False(tooEarly.Ok, "有效状态的任务竟然能被直接删除——两步门被跨过了");
            Assert.Contains("作废", tooEarly.Message);

            // ② 作废 → 删除
            Assert.True(store.Retire(ProbeDeleteId).Ok, "作废失败");

            // ②' 作废是终态：不能再被拉回上线。库里实测出现过 IsValid=0 且 publish_state=1 的行
            //     （作废之后又被打了一次 online），那种行不会被接取、却在清单里显示"已发布"，最容易误导运营。
            var relive = store.SetPublishState(ProbeDeleteId, (int)QuestPublishState.Online);
            Assert.False(relive.Ok, "已作废的任务竟然还能被 online 拉回上线");
            Assert.Contains("作废", relive.Message);

            var deleted = store.Delete(ProbeDeleteId);
            Assert.True(deleted.Ok, deleted.Message);

            // ③ 真的没了：查不到、不在清单里、也发布不了
            Assert.Null(store.FindQuest(ProbeDeleteId));
            Assert.DoesNotContain(store.AllQuests(), item => item.Id == ProbeDeleteId);
            Assert.False(store.SetPublishState(ProbeDeleteId, (int)QuestPublishState.Online).Ok,
                "行已经删掉了，却还能被发布");

            // ④ 再删一次给的是"不存在"，而不是抛异常（幂等对运维很重要）
            var again = store.Delete(ProbeDeleteId);
            Assert.False(again.Ok);
            Assert.Contains("不存在", again.Message);
        }
        finally
        {
            using var cleanup = new QuestStore(connection!);
            CleanupDeleteProbe(cleanup);
        }
    }

    /// <summary>幂等清理删除探针：存在就"作废 → 删"（两次中断都能清干净）。</summary>
    private static void CleanupDeleteProbe(QuestStore store)
    {
        if (store.FindQuest(ProbeDeleteId) == null) return;
        store.Retire(ProbeDeleteId);
        store.Delete(ProbeDeleteId);
    }

    private static QuestDraft NewDraft(int questId)
    {
        var draft = new QuestDraft
        {
            QuestId = questId,
            QuestName = "探针任务",
            Description = "作者闭环测试用，跑完即删。",
            QuestType = (int)QuestTypeCode.Side,
            LevelRequire = 1,
            PublishState = (int)QuestPublishState.Offline,
            IsValid = true,
            Giver = new QuestEntityRefDraft { Kind = "npc", TemplateId = 3 },
            PrerequisiteQuests = { 90001 },
        };
        draft.Objectives.Add(new QuestObjectiveDraft { Kind = "kill_monster", TemplateId = 4, Count = 2, Description = "猎杀 2 只银爪狸" });
        draft.Objectives.Add(new QuestObjectiveDraft { Kind = "kill_domain", TemplateId = 5, Count = 3, Description = "肃清土行异动 3 处" });
        draft.Rewards = new QuestRewardSpec { Exp = 100, Gold = 200 };
        draft.Rewards.Items.Add(new QuestRewardItem { TemplateId = 810101, Count = 1 });
        // 奖励要"关联所有游戏物品"，所以闭环测试里一次带齐三类**库里真有的**档位：
        // 装备(item_type=0)、秘籍(6, 800000+技能id)、材料(4, 700000+序号)。
        // 之前这份草稿只有秘籍一种，等于"多档奖励能不能原样进出库"从来没被执行过。
        draft.Rewards.Items.Add(new QuestRewardItem { TemplateId = 10001, Count = 2 });
        draft.Rewards.Items.Add(new QuestRewardItem { TemplateId = 700001, Count = 5 });
        draft.Rewards.Favor.Add(new QuestRewardFavor { Domain = 1, Points = 10 });
        return draft;
    }

    /// <summary>物理删除探针行：只按这两个 id 删，不碰任何正式任务。</summary>
    private static void Purge(string connection)
    {
        var options = new DbContextOptionsBuilder<GameEntityContext>().UseSqlServer(connection).Options;
        using var db = new GameEntityContext(options);
        var doomed = db.QuestTemplates.Where(row => row.Id == ProbeQuestId || row.Id == ProbeBadId).ToList();
        if (doomed.Count == 0) return;
        db.QuestTemplates.RemoveRange(doomed);
        db.SaveChanges();
    }
}

/// <summary>秘籍 id 段起点（与 <c>ItemCatalog.BookIdBase</c>、服务端种子同一约定）。</summary>
internal static class ItemIdConstants
{
    /// <summary>秘籍物品模板 id = 本值 + 技能 id。</summary>
    public const int BookIdBase = 800000;
}
