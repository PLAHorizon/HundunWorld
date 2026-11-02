using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Horizon.Entities;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Sim;
using Horizon.Game.Sim.Quests;
using Horizon.Model.GameModel;
using Horizon.Orleans.Grains;
using Xunit;

namespace Horizon.QuestContractTests;

/// <summary>
/// 任务编辑器与运行时之间的契约测试。
/// <para>
/// 这里断言的都是"改了名字/语法不会报错、只会在游戏里静默失效"的那一类：
/// 目标类型串、奖励键、草稿字段名、发布状态门槛。它们各自只有一份真相，
/// 一旦分叉，现场表现是"编辑器里填了、玩家看不见"或"任务永远推不动"。
/// </para>
/// </summary>
public class QuestEditorContractTests
{
    // ── 目标类型串 ────────────────────────────────────────────

    [Theory]
    [InlineData(QuestObjectiveKind.KillMonster, 4, "kill_monster:4")]
    [InlineData(QuestObjectiveKind.KillDomain, 3, "kill_domain:3")]
    [InlineData(QuestObjectiveKind.TalkNpc, 1, "talk_npc:1")]
    [InlineData(QuestObjectiveKind.ReachZone, 7, "reach_zone:7")]
    public void 目标串编码与解码必须可逆(QuestObjectiveKind kind, int templateId, string expected)
    {
        var encoded = QuestObjectiveCodec.Encode(new QuestObjectiveSpec(kind, templateId));
        Assert.Equal(expected, encoded);

        Assert.True(QuestObjectiveCodec.TryDecode(encoded, out var decoded));
        Assert.Equal(kind, decoded.Kind);
        Assert.Equal(templateId, decoded.TemplateId);
    }

    [Fact]
    public void 域击杀的域号越界必须编不出串()
    {
        Assert.Null(QuestObjectiveCodec.Encode(new QuestObjectiveSpec(QuestObjectiveKind.KillDomain, 0)));
        Assert.Null(QuestObjectiveCodec.Encode(new QuestObjectiveSpec(QuestObjectiveKind.KillDomain, 6)));
        Assert.False(QuestObjectiveCodec.TryDecode("kill_domain:9", out _));
    }

    [Fact]
    public void 历史裸击杀串归一为任意击杀且不报错()
    {
        Assert.True(QuestObjectiveCodec.TryDecode("kill", out var decoded));
        Assert.Equal(QuestObjectiveKind.KillMonster, decoded.Kind);
        Assert.Equal(0, decoded.TemplateId);
    }

    /// <summary>
    /// 编出来的串必须能被 DS 侧的匹配器认下——两处各写一套语法是这条链最贵的分叉：
    /// 编辑器以为写了"杀 4 号怪"，匹配器却按"任意击杀"放行，玩家杀错怪也计数。
    /// </summary>
    [Fact]
    public void 编码器产出的击杀串与匹配器同语法()
    {
        var matcher = new KillObjectiveTypeMatcher();

        Assert.True(matcher.ShouldAdvance("kill_monster:4", "", 4));
        Assert.False(matcher.ShouldAdvance("kill_monster:4", "", 1));
        Assert.True(matcher.ShouldAdvance("kill_domain:3", "", 1));
        Assert.False(matcher.ShouldAdvance("kill_domain:3", "", 2));
        Assert.True(matcher.ShouldAdvance("kill", "", 9));
    }

    [Fact]
    public void 没有事件源的目标类别必须被点名()
    {
        Assert.True(QuestObjectiveCodec.HasProducer(QuestObjectiveKind.KillMonster));
        Assert.True(QuestObjectiveCodec.HasProducer(QuestObjectiveKind.KillDomain));
        // 对话/物件交互自 §11 起有了服务端事件源：DS 上行实体身份，网关按"存在+可交互+够近"裁决。
        // 注意这不等于"客户端说什么都行"——采信的是实体 id 解出来的模板 id，见 QuestInteractionAuthority。
        Assert.True(QuestObjectiveCodec.HasProducer(QuestObjectiveKind.TalkNpc));
        Assert.True(QuestObjectiveCodec.HasProducer(QuestObjectiveKind.InteractProp));
        Assert.False(QuestObjectiveCodec.HasProducer(QuestObjectiveKind.ReachZone));
        Assert.DoesNotContain(QuestObjectiveKind.TalkNpc, QuestObjectiveCodec.KindsWithoutProducer());
        Assert.Contains(QuestObjectiveKind.ReachZone, QuestObjectiveCodec.KindsWithoutProducer());
        Assert.NotEmpty(QuestObjectiveCodec.KindsWithoutProducer());
    }

    // ── 奖励键 ────────────────────────────────────────────────

    [Fact]
    public void 奖励编码与既有消费方同键同形()
    {
        var reward = new QuestRewardSpec { Exp = 80, Gold = 500 };
        reward.Items.Add(new QuestRewardItem { TemplateId = 810101, Count = 1 });
        reward.Favor.Add(new QuestRewardFavor { Domain = 1, Points = 20 });

        var json = reward.ToJson();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(80, root.GetProperty("exp").GetInt32());
        Assert.Equal(500, root.GetProperty("gold").GetInt32());
        Assert.Equal(1, root.GetProperty("item:810101").GetInt32());
        Assert.Equal(20, root.GetProperty("favor:1").GetInt32());
    }

    [Fact]
    public void 奖励解析双向可逆且坏键只丢不炸()
    {
        var source = "{\"item:810101\":1,\"favor:3\":20,\"exp\":80,\"gold\":\"1000\",\"bogus\":0}";
        var parsed = QuestRewardSpec.FromJson(source);
        Assert.Single(parsed.Items);
        Assert.Equal(810101, parsed.Items[0].TemplateId);
        Assert.Equal(3, parsed.Favor[0].Domain);
        Assert.Equal(80, parsed.Exp);
        Assert.Equal(0, parsed.Gold);   // 字符串值不认：与 QuestGrain.ParseRewards 的既有口径一致

        // gold（字符串）与 bogus（不认识/非正数）各丢一条。
        Assert.Equal(2, QuestRewardSpec.ParseRejectedKeyCount(source));

        // 重新编码后就是干净数据：再解析不该再有丢弃。
        Assert.Equal(0, QuestRewardSpec.ParseRejectedKeyCount(parsed.ToJson()));
    }

    [Fact]
    public void 空奖励要能被识别出来()
    {
        Assert.True(new QuestRewardSpec().IsEmpty);
        var withItem = new QuestRewardSpec();
        withItem.Items.Add(new QuestRewardItem { TemplateId = 50001, Count = 1 });
        Assert.False(withItem.IsEmpty);
    }

    // ── 校验器 ────────────────────────────────────────────────

    private static QuestDraft HealthyDraft(int questId = 10001) => new QuestDraft
    {
        QuestId = questId,
        QuestName = "初试锋芒",
        Description = "打三只银爪狸。",
        QuestType = 0,
        LevelRequire = 1,
        PublishState = (int)QuestPublishState.Online,
        Giver = new QuestEntityRefDraft { Kind = "npc", TemplateId = 1 },
        Objectives =
        {
            new QuestObjectiveDraft { Kind = "kill_monster", TemplateId = 4, Count = 3, Description = "猎杀 3 只银爪狸" },
        },
        Rewards = CreateReward(),
    };

    private static QuestRewardSpec CreateReward()
    {
        var reward = new QuestRewardSpec { Exp = 120 };
        reward.Items.Add(new QuestRewardItem { TemplateId = 810101, Count = 1 });
        return reward;
    }

    [Fact]
    public void 运行时奖励字典与结构化奖励互逆()
    {
        // 接取时模板奖励会被展平成 Dictionary<string,int> 存进角色状态；
        // 下发给客户端前要能原样还原，否则"发的"和"显示的"会是两套东西。
        var spec = new QuestRewardSpec { Exp = 120, Gold = 500 };
        spec.Items.Add(new QuestRewardItem { TemplateId = 810101, Count = 2 });
        spec.Favor.Add(new QuestRewardFavor { Domain = 3, Points = 20 });

        var legacy = spec.ToLegacyDictionary();
        Assert.Equal(2, legacy["item:810101"]);
        Assert.Equal(20, legacy["favor:3"]);
        Assert.Equal(120, legacy["exp"]);
        Assert.Equal(500, legacy["gold"]);

        var back = QuestRewardSpec.FromLegacyDictionary(legacy);
        Assert.Equal(spec.Exp, back.Exp);
        Assert.Equal(spec.Gold, back.Gold);
        Assert.Equal(810101, Assert.Single(back.Items).TemplateId);
        Assert.Equal(2, back.Items[0].Count);
        Assert.Equal(3, Assert.Single(back.Favor).Domain);

        // 非正数与不认识的键都该被丢掉，不能被当成"发了 0 个"。
        var dirty = QuestRewardSpec.FromLegacyDictionary(new Dictionary<string, int>
        {
            ["item:700000"] = 0, ["favor:9"] = 5, ["mystery"] = 3, ["exp"] = 60,
        });
        Assert.Empty(dirty.Items);
        Assert.Empty(dirty.Favor);
        Assert.Equal(60, dirty.Exp);
    }

    [Fact]
    public void 健康草稿在离线对账下无错误()
    {
        var result = QuestTemplateValidator.Validate(HealthyDraft(), StaticQuestReferenceIndex.Default, requirePublishable: true);
        Assert.False(result.HasErrors, result.ToText());
    }

    [Fact]
    public void 怪物不能当发布者()
    {
        var draft = HealthyDraft();
        draft.Giver = new QuestEntityRefDraft { Kind = "monster", TemplateId = 1 };
        var result = QuestTemplateValidator.Validate(draft);
        Assert.Contains(result.Issues, issue => issue.Code == "Q012");
    }

    [Fact]
    public void 未登记的引用一律报错而不是静默放过()
    {
        var npcDraft = HealthyDraft();
        npcDraft.Giver = new QuestEntityRefDraft { Kind = "npc", TemplateId = 999 };
        Assert.Contains(QuestTemplateValidator.Validate(npcDraft).Issues, issue => issue.Code == "Q013");

        var monsterDraft = HealthyDraft();
        monsterDraft.Objectives[0].TemplateId = 999;
        Assert.Contains(QuestTemplateValidator.Validate(monsterDraft).Issues, issue => issue.Code == "Q028");
    }

    [Fact]
    public void 没有事件源的目标类别是错误不是警告()
    {
        var draft = HealthyDraft();
        draft.Objectives[0].Kind = "reach_zone";
        draft.Objectives[0].TemplateId = 7;
        var result = QuestTemplateValidator.Validate(draft);
        var issue = Assert.Single(result.Issues, candidate => candidate.Code == "Q026");
        Assert.Equal(QuestIssueLevel.Error, issue.Level);
    }

    [Fact]
    public void 发布者是物品时能入库但要当场看清它走不通收窄可接()
    {
        var draft = HealthyDraft();
        draft.Giver.Kind = "item";
        draft.Giver.TemplateId = 20001;

        var result = QuestTemplateValidator.Validate(draft, StaticQuestReferenceIndex.Default, requirePublishable: true);

        // 目标要求"发布者可以是某个物品"，所以这条**不许**被拦成错误——写不下来才是缺陷。
        Assert.False(result.HasErrors, result.ToText());
        // 但世界里的实体身份只由 DS 上报，四类里没有 item，走近那个物件永远收窄不出这条任务。
        // 不告诉作者，就会变成"我挂了任务但现场看不见"的无解悬案，所以必须是一条常驻 Info。
        var note = Assert.Single(result.Issues, candidate => candidate.Code == "Q043");
        Assert.Equal(QuestIssueLevel.Info, note.Level);

        // npc 与 prop 这两个真能被解出的类别不该跟着吃这条提示（否则提示就成噪声了）。
        var npcDraft = HealthyDraft();
        npcDraft.Giver.Kind = "npc";
        npcDraft.Giver.TemplateId = 1;
        Assert.DoesNotContain(QuestTemplateValidator.Validate(npcDraft, StaticQuestReferenceIndex.Default, requirePublishable: true).Issues,
                              candidate => candidate.Code == "Q043");

        var propDraft = HealthyDraft();
        propDraft.Giver.Kind = "prop";
        propDraft.Giver.TemplateId = 5;
        Assert.DoesNotContain(QuestTemplateValidator.Validate(propDraft, StaticQuestReferenceIndex.Default, requirePublishable: true).Issues,
                              candidate => candidate.Code == "Q043");
    }

    [Fact]
    public void 对话类目标可入库并给一条部署前提提示()
    {
        var draft = HealthyDraft();
        draft.Objectives[0].Kind = "talk_npc";
        draft.Objectives[0].TemplateId = 1;
        draft.Objectives[0].Description = "与村口的里正打探消息";

        var result = QuestTemplateValidator.Validate(draft, StaticQuestReferenceIndex.Default, requirePublishable: true);

        // 服务端有事件源、客户端有触发点 → 不再是错误，发布不该被拦。
        Assert.False(result.HasErrors, result.ToText());
        // 但这条链路吃部署态（DS/网关必须已重启到带实体身份上行那一版），给一条 Info 便于现场归因。
        var note = Assert.Single(result.Issues, candidate => candidate.Code == "Q039");
        Assert.Equal(QuestIssueLevel.Info, note.Level);
    }

    [Fact]
    public void 上线任务必须有目标与奖励()
    {
        var noObjective = HealthyDraft();
        noObjective.Objectives.Clear();
        Assert.Contains(QuestTemplateValidator.Validate(noObjective).Issues, issue => issue.Code == "Q020");

        var noReward = HealthyDraft();
        noReward.Rewards = new QuestRewardSpec();
        Assert.Contains(QuestTemplateValidator.Validate(noReward).Issues, issue => issue.Code == "Q031");
    }

    [Fact]
    public void 发布校验看的是发布之后的样子()
    {
        // 库里这条任务此刻当然是 publish_state=0（正因为如此才要发布）。
        // 若校验器拿"当前状态"去判 Q050，每一次发布都会被自相矛盾地拒掉。
        var draft = HealthyDraft();
        draft.PublishState = (int)QuestPublishState.Offline;

        var asPublished = HealthyDraft();
        Assert.False(QuestTemplateValidator.Validate(asPublished, StaticQuestReferenceIndex.Default, requirePublishable: true).HasErrors);

        // 但"没有目标"这类硬伤在发布这一刀上必须变成错误（草稿期只是警告）。
        var broken = HealthyDraft();
        broken.Objectives.Clear();
        Assert.True(QuestTemplateValidator.Validate(broken, StaticQuestReferenceIndex.Default, requirePublishable: true).HasErrors);
    }

    [Fact]
    public void 前置任务不能指向自己()
    {
        var draft = HealthyDraft(10001);
        draft.PrerequisiteQuests.Add(10001);
        Assert.Contains(QuestTemplateValidator.Validate(draft).Issues, issue => issue.Code == "Q041");
    }

    [Fact]
    public void 发布态非法要拦住()
    {
        var draft = HealthyDraft();
        draft.PublishState = 7;
        Assert.Contains(QuestTemplateValidator.Validate(draft).Issues, issue => issue.Code == "Q009");
    }

    // ── 草稿 JSON：字段名就是协议 ─────────────────────────────

    /// <summary>
    /// 与任务编辑器插件里的 <c>QuestDraftModel.KnownTopLevelKeys</c> 对账。
    /// 这里改名不会让插件报错——System.Text.Json 只会把对不上的键静默丢掉，
    /// 于是表现成"编辑器里填了、库里是空的"。这条测试把那次改名变成一次红。
    /// </summary>
    [Fact]
    public void 草稿顶层字段名不得漂移()
    {
        var sample = new QuestDraft { QuestId = 1 };
        sample.TurnIn = new QuestEntityRefDraft { Kind = "npc", TemplateId = 2 };

        using var document = JsonDocument.Parse(sample.ToJson());
        var keys = document.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray();

        var expected = new[]
        {
            "contentVersion", "description", "giver", "isValid", "levelRequire", "objectives",
            "prerequisiteQuests", "publishState", "questId", "questName", "questType", "repeatLimit",
            "rewards", "turnIn", "wuxingDomain",
        }.OrderBy(name => name).ToArray();

        Assert.Equal(expected, keys);
    }

    /// <summary>
    /// 顶层之外的**每一层**字段名都要两边一致。
    /// <para>
    /// 上一条只管根节点，但静默丢数据真正发生在嵌套层：插件按自己的属性名写 <c>giver.templateId</c>，
    /// 服务端解析不到就是**默认值 0**——不报错、不拒绝，作者点了保存，下次打开发现发布者空了。
    /// 这类"两份模型"的坑本仓已经踩过两次（种子的 INSERT/UPDATE 双分支、编辑器与服务端各一份校验）。
    /// </para>
    /// <para>
    /// 插件那份模型住在 Flax 插件源码里、不能被本工程引用，所以它的键清单是**从源文件文本里抠出来的**——
    /// 抠不到就直接判红，而不是退化成"只比自己"。
    /// </para>
    /// </summary>
    [Fact]
    public void 嵌套层的字段名两边必须一一对上()
    {
        var sample = new QuestDraft
        {
            QuestId = 1,
            QuestName = "探针",
            Giver = new QuestEntityRefDraft { Kind = "npc", TemplateId = 3 },
        };
        sample.TurnIn = new QuestEntityRefDraft { Kind = "item", TemplateId = 700001 };
        sample.Objectives.Add(new QuestObjectiveDraft { Kind = "kill_monster", TemplateId = 4, Count = 2, Description = "打两只" });
        sample.Rewards.Exp = 100;
        sample.Rewards.Gold = 200;
        sample.Rewards.Items.Add(new QuestRewardItem { TemplateId = 810101, Count = 1 });
        sample.Rewards.Favor.Add(new QuestRewardFavor { Domain = 1, Points = 10 });

        using var document = JsonDocument.Parse(sample.ToJson());
        var root = document.RootElement;

        var entityRef = PluginKeySet("KnownEntityRefKeys");
        var objectives = PluginKeySet("KnownObjectiveKeys");
        var rewards = PluginKeySet("KnownRewardKeys");

        AssertKeys(root.GetProperty("giver"), entityRef, "发布者");
        AssertKeys(root.GetProperty("turnIn"), entityRef, "交付者");
        AssertKeys(root.GetProperty("objectives")[0], objectives, "目标条目");
        AssertKeys(root.GetProperty("rewards"), rewards, "奖励");
        AssertKeys(root.GetProperty("rewards").GetProperty("items")[0],
                   new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "templateId", "count" }, "奖励物品条目");
        AssertKeys(root.GetProperty("rewards").GetProperty("favor")[0],
                   new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "domain", "points" }, "奖励好感条目");
        AssertKeys(root, PluginKeySet("KnownTopLevelKeys"), "草稿根节点");

        // 反向：服务端写出的这份 JSON，服务端自己当然读得回；关键是**每个嵌套值都不能掉**，
        // 掉了就说明两侧名字不一致（而 STJ 对不认识的键是静默忽略，不会报错）。
        var roundTripped = QuestDraft.TryParse(sample.ToJson(), out var parseError);
        Assert.True(roundTripped != null, "服务端读不回这份 JSON：" + parseError);
        Assert.True(string.IsNullOrEmpty(parseError), parseError);
        Assert.Equal(3, roundTripped!.Giver.TemplateId);
        Assert.Equal(700001, roundTripped.TurnIn!.TemplateId);
        Assert.Equal("item", roundTripped.TurnIn!.Kind);
        Assert.Equal(2, roundTripped.Objectives[0].Count);
        Assert.Equal("打两只", roundTripped.Objectives[0].Description);
        Assert.Single(roundTripped.Rewards.Items);
        Assert.Single(roundTripped.Rewards.Favor);
    }

    /// <summary>
    /// 从插件源码里抠出 <c>KnownXxxKeys</c> 的字符串字面量集合。
    /// 插件模型不在本工程的引用面上（Flax 插件的程序集由引擎决定），所以只能按文本对账——
    /// 这正好也逼着两边都用"显式清单"而不是靠反射，改名一定会留下一边变了另一边没变。
    /// </summary>
    private static HashSet<string> PluginKeySet(string field)
    {
        var path = System.IO.Path.Combine(RepoRootForSourceGates(),
            "HundunWorld", "Plugins", "QuestEditor", "Source", "QuestEditor", "QuestDraftModel.cs");
        Assert.True(System.IO.File.Exists(path), "找不到插件草稿模型：" + path);
        var source = System.IO.File.ReadAllText(path);

        // 必须贴着声明抠：`field = new HashSet<string>(...)\n{ "a", "b", ... }`。
        // 先前的"从字段名往后找第一个 { 到第一个 }"会撞上注释里的 {@code …} 这类花括号，
        // 抠出一段没有引号的空文本——那时本条会假绿（清单看着是空的却没报），所以这里宁可判红。
        var match = System.Text.RegularExpressions.Regex.Match(
            source,
            field + @"\s*=\s*new HashSet<string>\([^)]*\)\s*\{(?<body>[^}]*)\}");
        Assert.True(match.Success,
            "插件里没按预期形态找到 " + field + "（改名、换写法或删掉了？那这条对账就失效了，先确认再动，别改断言）");

        var keys = System.Text.RegularExpressions.Regex.Matches(match.Groups["body"].Value, "\"([^\"]+)\"")
            .Cast<System.Text.RegularExpressions.Match>()
            .Select(m => m.Groups[1].Value)
            .ToList();
        Assert.True(keys.Count > 0, field + " 的清单是空的——空清单会让本条对账变成空转");
        return new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
    }

    private static string RepoRootForSourceGates()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !dir.GetFiles("*.sln").Any()) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static void AssertKeys(JsonElement target, HashSet<string> expected, string what)
    {
        var actual = target.EnumerateObject().Select(property => property.Name)
                          .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        var want = expected.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.True(actual.SequenceEqual(want, StringComparer.OrdinalIgnoreCase),
            what + " 这一层两边字段名不一致：服务端写出 [" + string.Join(",", actual) + "]，插件声明认识 [" + string.Join(",", want) + "]");
    }

    [Fact]
    public void 条件与前置的落库形态可回读()
    {
        var draft = HealthyDraft();
        draft.PrerequisiteQuests.Add(10001);
        draft.PrerequisiteQuests.Add(10002);

        Assert.Equal("10001,10002", draft.BuildPrerequisitesCsv());
        Assert.Equal(new[] { 10001, 10002 }, QuestDraft.ParsePrerequisites(draft.BuildPrerequisitesCsv()).ToArray());

        var objectivesJson = draft.BuildObjectivesJson();
        var reread = QuestDraft.ParseObjectives(objectivesJson);
        var single = Assert.Single(reread);
        Assert.Equal("kill_monster", single.Kind);
        Assert.Equal(4, single.TemplateId);
        Assert.Equal(3, single.Count);
        Assert.Equal("猎杀 3 只银爪狸", single.Description);
    }

    [Fact]
    public void 坏的目标列不炸但条目会被校验器点名()
    {
        var reread = QuestDraft.ParseObjectives("[{\"type\":\"bogus\",\"count\":1},{\"type\":\"kill_monster:4\",\"count\":2}]");

        // 认不出的串原样留着（kind 保持 "bogus"），由校验器发 Q022 错误——
        // 在这里就丢掉整条的话，"库里少一条目标"这件事就没有任何地方说得清了。
        Assert.Equal(2, reread.Count);
        Assert.Equal("bogus", reread[0].Kind);
        Assert.Equal("kill_monster", reread[1].Kind);
        Assert.Equal(4, reread[1].TemplateId);

        var draft = HealthyDraft();
        draft.Objectives = reread;
        Assert.Contains(QuestTemplateValidator.Validate(draft).Issues, issue => issue.Code == "Q022");

        Assert.Empty(QuestDraft.ParseObjectives("{不是数组}"));
    }

    // ── 库行 ↔ 草稿映射 ───────────────────────────────────────

    [Fact]
    public void 库行与草稿双向映射不丢字段()
    {
        var draft = HealthyDraft();
        draft.WuxingDomain = 3;
        draft.RepeatLimit = 5;
        draft.PublishState = 1;
        draft.TurnIn = new QuestEntityRefDraft { Kind = "npc", TemplateId = 2 };
        draft.PrerequisiteQuests.Add(90001);   // 不是自己：填 10001 会被 Q041 当场拦下

        var row = new QuestTemplateEntity();
        QuestTemplateMapper.Apply(draft, row);
        // Apply 刻意不写主键：更新路径上改 Id 会被 EF 当成键变更。新建走 NewRow，那里才带 Id。
        row.Id = draft.QuestId;

        Assert.Equal((int)QuestEntityKind.Npc, row.GiverKind);
        Assert.Equal(1, row.GiverTemplateId);
        Assert.Equal((int)QuestEntityKind.Npc, row.TurnInKind);
        Assert.Equal(2, row.TurnInTemplateId);
        Assert.Equal("90001", row.PrerequisiteQuests);
        Assert.Contains("kill_monster:4", row.Objectives);
        Assert.Contains("item:810101", row.Rewards);

        var back = QuestTemplateMapper.ToDraft(row);
        Assert.Equal(draft.QuestName, back.QuestName);
        Assert.Equal(draft.WuxingDomain, back.WuxingDomain);
        Assert.Equal(draft.RepeatLimit, back.RepeatLimit);
        Assert.Equal(draft.PublishState, back.PublishState);
        Assert.Equal(2, back.TurnIn.TemplateId);
        Assert.Equal(new[] { 90001 }, back.PrerequisiteQuests.ToArray());
        Assert.Equal(810101, back.Rewards.Items[0].TemplateId);
        Assert.False(QuestTemplateValidator.Validate(back, StaticQuestReferenceIndex.Default).HasErrors);
    }

    [Fact]
    public void 映射不写校验该管的事()
    {
        // 名称过长要被截到列宽里（nvarchar(50)），否则 SQL 侧报"将字符串截断"，
        // 那是一条玩家与策划都看不懂的失败。
        var draft = HealthyDraft();
        draft.QuestName = new string('剑', 80);
        var row = new QuestTemplateEntity();
        QuestTemplateMapper.Apply(draft, row);
        Assert.Equal(50, row.QuestName.Length);
    }

    // ── 接取侧的可重复语义 ────────────────────────────────────

    [Fact]
    public void 日常按自然日周常按自然周重置()
    {
        var now = DateTime.Now;
        Assert.False(Horizon.Orleans.Grains.QuestGrain.IsInNewPeriod(now.AddMinutes(-5), 2));
        Assert.True(Horizon.Orleans.Grains.QuestGrain.IsInNewPeriod(now.AddDays(-1), 2));
        Assert.False(Horizon.Orleans.Grains.QuestGrain.IsInNewPeriod(now.AddDays(-1), 3));
        Assert.True(Horizon.Orleans.Grains.QuestGrain.IsInNewPeriod(now.AddDays(-8), 3));
        Assert.True(Horizon.Orleans.Grains.QuestGrain.IsInNewPeriod(null, 0));
    }

    [Theory]
    [InlineData(0, 1, true)]    // 无门槛：等级读不到也接得了
    [InlineData(0, 0, true)]
    [InlineData(5, 5, true)]    // 卡在线上算过
    [InlineData(5, 4, false)]
    [InlineData(5, 0, false)]   // 网关没读到权威等级 → fail closed，不能当成"没门槛"
    [InlineData(5, -1, false)]
    public void 接取侧必须自己再校一遍等级门槛(int levelRequire, int characterLevel, bool expected)
    {
        // 这条门以前只在"可接列表"的查询里过滤，那是展示层的过滤：直连一条 questId 就能把
        // 高等级任务接走。判定做成纯函数就是为了这里能把边界逐个钉死，而不是等现场有人举报。
        Assert.Equal(expected, Horizon.Orleans.Grains.QuestGrain.MeetsLevelRequire(levelRequire, characterLevel));
    }

    [Fact]
    public void 对话类交互串能被认出来()
    {
        // ReportInteractionAsync 只认完整串 talk_npc:<id>，这里守住那个字面量。
        Assert.True(QuestObjectiveCodec.TryDecode("talk_npc:1", out var spec));
        Assert.Equal(QuestObjectiveKind.TalkNpc, spec.Kind);
        Assert.Equal(1, spec.TemplateId);
    }
}
