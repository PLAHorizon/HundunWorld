using System;
using System.Collections.Generic;
using System.IO;
using NarrativePro.Core;
using NarrativePro.EditorTools;
using NarrativePro.Tales.Dialogue;
using NarrativePro.Tales.Quest;

internal static class Program
{
    private static int _checks;
    private static int _failures;

    private static int Main()
    {
        CalibrateProjectDirectory();
        try
        {
            TestQuestLayout();
            TestQuestDuplicate();
            TestDialogueLayoutAndDuplicate();
            TestInMemoryValidation();
            TestWriteRoundTrip();
            TestAuthoringOperations();
            TestStackedDetection();
            TestRenames();
            TestSnapshots();
            TestHistory();
            TestDropdownContracts();
            TestGraphAuthoringPrimitives();
            TestAssetFileOps();
            TestSearchAndOutline();
            TestReferenceAudit();
            TestTemplateLink();
            TestFusionContracts();
            TestDialogueShots();
            TestDialogueCutscenes();
            TestStoryEvents();
            TestDialogueConfig();
        }
        catch (Exception ex)
        {
            _failures++;
            Console.WriteLine("EXCEPTION: " + ex);
        }

        Console.WriteLine($"checks={_checks} failures={_failures}");
        return _failures == 0 ? 0 : 1;
    }

    // -------------------------------------------------------------------
    // 任务：自动布局
    // -------------------------------------------------------------------

    private static void TestQuestLayout()
    {
        var data = new QuestData { QuestId = "Q_test", StartStateId = "start" };
        data.States.Add(new QuestStateData { Id = "start", Position = new NodePositionData { X = 0f, Y = 0f } });
        data.States.Add(new QuestStateData { Id = "mid", Position = new NodePositionData { X = 0f, Y = 0f } });
        data.States.Add(new QuestStateData { Id = "win", StateType = EStateNodeType.Success, Position = new NodePositionData { X = 0f, Y = 0f } });
        data.Branches.Add(new QuestBranchData { Id = "b1", FromStateId = "start", ToStateId = "mid", Tasks = new List<TaskData>() });
        data.Branches.Add(new QuestBranchData { Id = "b2", FromStateId = "mid", ToStateId = "win", Tasks = new List<TaskData>() });

        Check(NarrativeAssetStore.IsQuestLayoutDegenerate(data), "三点重合应被判定为退化布局");

        NarrativeAssetStore.LayoutQuest(data);

        var start = data.States.Find(s => s.Id == "start");
        var mid = data.States.Find(s => s.Id == "mid");
        var win = data.States.Find(s => s.Id == "win");
        Check(start != null && mid != null && win != null, "布局后状态仍在");
        Check(start.Position.X < mid.Position.X && mid.Position.X < win.Position.X, "列号应按 BFS 深度递增");
        Check(!NarrativeAssetStore.IsQuestLayoutDegenerate(data), "布局后不再是退化布局");

        var b1 = data.Branches.Find(b => b.Id == "b1");
        Check(b1.Position != null && b1.Position.X > start.Position.X && b1.Position.X < mid.Position.X,
            "分支节点应落在两端状态之间");

        // 重复布局必须幂等（同一输入两次布局结果一致）
        float firstX = mid.Position.X;
        float firstY = mid.Position.Y;
        NarrativeAssetStore.LayoutQuest(data);
        Check(Math.Abs(mid.Position.X - firstX) < 0.001f && Math.Abs(mid.Position.Y - firstY) < 0.001f,
            "自动布局应幂等");
    }

    // -------------------------------------------------------------------
    // 任务：复制状态 / 分支
    // -------------------------------------------------------------------

    private static void TestQuestDuplicate()
    {
        var data = new QuestData { QuestId = "Q_dup", StartStateId = "start" };
        data.States.Add(new QuestStateData
        {
            Id = "start",
            Description = "开始",
            StateType = EStateNodeType.Regular,
            Position = new NodePositionData { X = 10f, Y = 20f }
        });
        data.States.Add(new QuestStateData { Id = "win", StateType = EStateNodeType.Success, Position = new NodePositionData { X = 400f, Y = 0f } });
        data.Branches.Add(new QuestBranchData
        {
            Id = "b",
            FromStateId = "start",
            ToStateId = "win",
            Description = "主线",
            Position = new NodePositionData { X = 200f, Y = 20f },
            Tasks = new List<TaskData>
            {
                new TaskData { Type = "GenericTask", TargetId = "item_a", RequiredQuantity = 3, Description = "采药" }
            }
        });

        string error = NarrativeAssetStore.DuplicateQuestState(data, "start", out string copyId);
        Check(error == null, "复制状态应成功: " + error);
        Check(copyId == "start_copy", "复制体 id 应为 start_copy，实际 " + copyId);
        var original = data.States.Find(s => s.Id == "start");
        var copy = data.States.Find(s => s.Id == copyId);
        Check(copy != null && copy.Description == original.Description && copy.StateType == original.StateType,
            "复制体应保留描述与类型");
        Check(copy.Position.X != original.Position.X || copy.Position.Y != original.Position.Y, "复制体位置应有偏移");

        NarrativeAssetStore.DuplicateQuestState(data, "start", out string secondCopy);
        Check(secondCopy == "start_copy_2", "第二次复制应得到 start_copy_2，实际 " + secondCopy);

        error = NarrativeAssetStore.DuplicateQuestBranch(data, "b", out string branchCopy);
        Check(error == null, "复制分支应成功: " + error);
        Check(branchCopy == "b_copy", "分支复制体 id 应为 b_copy，实际 " + branchCopy);
        var newBranch = data.Branches.Find(x => x.Id == branchCopy);
        Check(newBranch.Tasks.Count == 1 && newBranch.Tasks[0].RequiredQuantity == 3, "分支任务应被复制");
        Check(!ReferenceEquals(newBranch.Tasks[0], data.Branches.Find(x => x.Id == "b").Tasks[0]),
            "任务应深拷贝（不能与原分支共享实例）");
    }

    // -------------------------------------------------------------------
    // 对话：布局 + 复制
    // -------------------------------------------------------------------

    private static void TestDialogueLayoutAndDuplicate()
    {
        var dialogue = new DialogueData { DialogueId = "D_test" };
        dialogue.Speakers = new List<SpeakerData>
        {
            new SpeakerData { SpeakerId = "npc", DisplayName = "村民" },
            new SpeakerData { SpeakerId = "player", DisplayName = "我", IsPlayer = true }
        };
        dialogue.NpcReplies = new List<NpcReplyData>
        {
            new NpcReplyData
            {
                Id = "root", SpeakerId = "npc", IsRoot = true,
                Line = new DialogueLineData { Text = "你好" },
                NpcReplies = new List<string>(), PlayerReplies = new List<string> { "p1" }
            }
        };
        dialogue.PlayerReplies = new List<PlayerReplyData>
        {
            new PlayerReplyData
            {
                Id = "p1", OptionText = "继续",
                Line = new DialogueLineData { Text = "嗯" },
                NpcReplies = new List<string>(), PlayerReplies = new List<string>()
            }
        };

        NarrativeAssetStore.LayoutDialogue(dialogue);
        var root = dialogue.NpcReplies[0];
        var player = dialogue.PlayerReplies[0];
        Check(player.NodePosX > root.NodePosX, "玩家选项应排在父 NPC 右侧");

        string error = NarrativeAssetStore.DuplicateDialogueNpc(dialogue, "root", out string copyId);
        Check(error == null, "复制 NPC 应成功: " + error);
        var copy = dialogue.NpcReplies.Find(n => n.Id == copyId);
        Check(copy != null && !copy.IsRoot, "复制体不应是根节点");
        Check(copy.Line != null && copy.Line.Text == "你好", "台词应被复制");
        Check(copy.PlayerReplies.Count == 0 && copy.NpcReplies.Count == 0, "连线不应被复制");
        Check(!ReferenceEquals(copy.Line, root.Line), "台词对象应深拷贝");

        error = NarrativeAssetStore.DuplicateDialoguePlayer(dialogue, "p1", out string playerCopy);
        Check(error == null, "复制玩家选项应成功: " + error);
        var playerNode = dialogue.PlayerReplies.Find(p => p.Id == playerCopy);
        Check(playerNode != null && playerNode.OptionText == "继续", "选项文本应被复制");
    }

    // -------------------------------------------------------------------
    // 内存校验（临时文件 + 运行时装载器）
    // -------------------------------------------------------------------

    private static void TestInMemoryValidation()
    {
        string temp = Path.GetTempPath();
        int before = CountTempFiles(temp);

        var good = MinimalQuest();
        var problems = NarrativeAssetStore.ValidateQuestData(good);
        Check(problems.Count == 0, "合法任务应通过内存校验，实际: " + string.Join(" | ", problems));

        var bad = MinimalQuest();
        bad.Branches.Add(new QuestBranchData { Id = "broken", FromStateId = "start", ToStateId = "missing" });
        var badProblems = NarrativeAssetStore.ValidateQuestData(bad);
        Check(badProblems.Count > 0, "分支指向不存在状态应被报出");
        Console.WriteLine("broken-branch problems: " + string.Join(" | ", badProblems));

        var badType = MinimalQuest();
        badType.States[0].StateType = (EStateNodeType)9;
        Check(NarrativeAssetStore.ValidateQuestData(badType).Count > 0, "非法 stateType 应被报出");

        var dialogue = new DialogueData { DialogueId = "D_min" };
        dialogue.Speakers = new List<SpeakerData> { new SpeakerData { SpeakerId = "npc", DisplayName = "村民" } };
        dialogue.NpcReplies = new List<NpcReplyData>
        {
            new NpcReplyData
            {
                Id = "root", SpeakerId = "npc", IsRoot = true,
                Line = new DialogueLineData { Text = "你好" },
                NpcReplies = new List<string>(), PlayerReplies = new List<string>()
            }
        };
        var dialogueProblems = NarrativeAssetStore.ValidateDialogueData(dialogue);
        Check(dialogueProblems.Count == 0, "合法对话应通过内存校验，实际: " + string.Join(" | ", dialogueProblems));

        var noRoot = new DialogueData { DialogueId = "D_noroot" };
        noRoot.Speakers = new List<SpeakerData> { new SpeakerData { SpeakerId = "npc" } };
        noRoot.NpcReplies = new List<NpcReplyData>
        {
            new NpcReplyData
            {
                Id = "a", SpeakerId = "npc", IsRoot = false,
                Line = new DialogueLineData { Text = "x" },
                NpcReplies = new List<string>(), PlayerReplies = new List<string>()
            }
        };
        Check(NarrativeAssetStore.ValidateDialogueData(noRoot).Count > 0, "缺少根节点应被报出");

        int after = CountTempFiles(temp);
        Check(after == before, $"校验临时文件应清理干净（before={before}, after={after}）");
    }

    // -------------------------------------------------------------------
    // 保存 / 读回（编辑器「保存」按钮走的同一条路径）
    // -------------------------------------------------------------------

    private static void TestWriteRoundTrip()
    {
        string questPath = Path.Combine(Path.GetTempPath(), "narrative_selftest_" + Guid.NewGuid().ToString("N") + ".json");
        string dialoguePath = Path.Combine(Path.GetTempPath(), "narrative_selftest_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var quest = MinimalQuest();
            quest.States[0].Description = "往返测试";
            NarrativeAssetStore.WriteQuest(questPath, quest);
            Check(File.Exists(questPath), "WriteQuest 应落盘");

            var loaded = NarrativeAssetStore.LoadQuestData(questPath);
            Check(loaded.StartStateId == "start" && loaded.States.Count == 2, "任务读回结构一致");
            Check(loaded.States[0].Description == "往返测试", "任务描述读回一致");
            Check(loaded.States.Find(s => s.Id == "win").StateType == EStateNodeType.Success, "状态类型读回一致");
            Check(NarrativeAssetStore.ValidateQuestFile(questPath).Count == 0, "写出的任务文件应通过运行时校验");
            string json = File.ReadAllText(questPath);
            Check(json.Contains("\"stateType\": 1"), "枚举必须写数值（运行时装载器不认字符串枚举）");

            var dialogue = new DialogueData { DialogueId = "D_rt" };
            dialogue.Speakers = new List<SpeakerData> { new SpeakerData { SpeakerId = "npc", DisplayName = "村民" } };
            dialogue.NpcReplies = new List<NpcReplyData>
            {
                new NpcReplyData
                {
                    Id = "root", SpeakerId = "npc", IsRoot = true,
                    Line = new DialogueLineData { Text = "往返" },
                    NpcReplies = new List<string>(), PlayerReplies = new List<string>()
                }
            };
            NarrativeAssetStore.WriteDialogue(dialoguePath, dialogue);
            var loadedDialogue = NarrativeAssetStore.LoadDialogueData(dialoguePath);
            Check(loadedDialogue.DialogueId == "D_rt" && loadedDialogue.NpcReplies.Count == 1, "对话读回结构一致");
            Check(loadedDialogue.NpcReplies[0].Line.Text == "往返", "对话台词读回一致");
            Check(NarrativeAssetStore.ValidateDialogueFile(dialoguePath).Count == 0, "写出的对话文件应通过运行时校验");
        }
        finally
        {
            try { if (File.Exists(questPath)) File.Delete(questPath); } catch { }
            try { if (File.Exists(dialoguePath)) File.Delete(dialoguePath); } catch { }
        }
    }

    // -------------------------------------------------------------------
    // 检查器调用的增删改入口
    // -------------------------------------------------------------------

    private static void TestAuthoringOperations()
    {
        NarrativeAssetStore.WarmLogicRegistry();

        var quest = MinimalQuest();

        // 状态字段：合法类型名可写，非法类型被拒
        Check(NarrativeAssetStore.SetQuestState(quest, "start", "改名", "Success") == null, "SetQuestState 接受 Success");
        Check(quest.States.Find(s => s.Id == "start").StateType == EStateNodeType.Success, "stateType 应被改写");
        Check(NarrativeAssetStore.SetQuestState(quest, "start", "改名", "7") != null, "SetQuestState 应拒绝 7");
        NarrativeAssetStore.SetQuestState(quest, "start", "开始", "0");

        // 分支端点必须存在
        Check(NarrativeAssetStore.SetQuestBranch(quest, "b", "主线", "start", "missing", "0") != null,
            "SetQuestBranch 应拒绝不存在的目标状态");

        // 事件/条件：注册表内的类型可加，未注册类型被拒
        string error = NarrativeAssetStore.AddQuestLogic(quest, "start", NarrativeLogicKind.Event, "SetTag",
            "{ \"tag\": \"Quest.SelfTest\", \"bAdd\": true }");
        Check(error == null, "加事件 SetTag 应成功: " + error);
        Check(quest.States.Find(s => s.Id == "start").Events.Count == 1, "事件应被写入状态");
        Check(NarrativeAssetStore.AddQuestLogic(quest, "start", NarrativeLogicKind.Event, "NoSuchEvent", "") != null,
            "未注册事件类型应被拒");
        error = NarrativeAssetStore.RemoveQuestLogic(quest, "start", NarrativeLogicKind.Event, "0");
        Check(error == null && quest.States.Find(s => s.Id == "start").Events.Count == 0, "删除事件应成功");

        // 分支任务：数量解析、越界删除
        error = NarrativeAssetStore.AddQuestTask(quest, "b", "GenericTask", "item_a", "2", "采药",
            "{ \"itemId\": \"item_a\" }");
        Check(error == null, "加任务应成功: " + error);
        var branch = quest.Branches.Find(x => x.Id == "b");
        Check(branch.Tasks.Count == 1 && branch.Tasks[0].RequiredQuantity == 2, "任务数量应写入");
        Check(NarrativeAssetStore.AddQuestTask(quest, "b", "GenericTask", "", "abc", "", "") != null,
            "非整数数量应被拒");
        Check(NarrativeAssetStore.RemoveQuestTask(quest, "b", "5") != null, "越界任务删除应被拒");
        Check(NarrativeAssetStore.RemoveQuestTask(quest, "b", "0") == null, "删除任务应成功");

        // 删除状态要连带删掉分支
        var cascade = new QuestData { QuestId = "Q_cascade", StartStateId = "a" };
        cascade.States.Add(new QuestStateData { Id = "a", Position = new NodePositionData() });
        cascade.States.Add(new QuestStateData { Id = "b", Position = new NodePositionData { X = 100f } });
        cascade.Branches.Add(new QuestBranchData { Id = "ab", FromStateId = "a", ToStateId = "b", Tasks = new List<TaskData>() });
        Check(NarrativeAssetStore.RemoveQuestState(cascade, "b") == null, "删除状态应成功");
        Check(cascade.Branches.Count == 0, "指向被删状态的分支应连带删除");

        // 对话：说话者、根节点唯一、连线规则
        var dialogue = new DialogueData { DialogueId = "D_ops" };
        Check(NarrativeAssetStore.AddDialogueSpeaker(dialogue, "npc", "村民", "0") == null, "加说话者应成功");
        Check(NarrativeAssetStore.AddDialogueSpeaker(dialogue, "npc", "重复", "0") != null, "重复说话者应被拒");
        Check(NarrativeAssetStore.AddDialogueNpcReply(dialogue, "n1", "npc", "一", "1") == null, "加根 NPC 应成功");
        Check(NarrativeAssetStore.AddDialogueNpcReply(dialogue, "n2", "npc", "二", "0") == null, "加普通 NPC 应成功");
        Check(NarrativeAssetStore.AddDialoguePlayerReply(dialogue, "p1", "继续", "嗯") == null, "加玩家选项应成功");
        Check(NarrativeAssetStore.AddDialogueNpcReply(dialogue, "bad", "missing", "x", "0") != null,
            "引用不存在的说话者应被拒");

        Check(NarrativeAssetStore.SetDialogueRootNode(dialogue, "n2") == null, "切换根节点应成功");
        Check(dialogue.NpcReplies.Find(n => n.Id == "n1").IsRoot == false &&
              dialogue.NpcReplies.Find(n => n.Id == "n2").IsRoot, "根节点必须唯一");

        Check(NarrativeAssetStore.LinkDialogueAuto(dialogue, "n2", "p1") == null, "NPC→玩家连线应成功");
        Check(NarrativeAssetStore.LinkDialogueAuto(dialogue, "n2", "p1") != null, "重复连线应被拒");
        Check(NarrativeAssetStore.LinkDialogueAuto(dialogue, "n2", "n2") != null, "自连应被拒");
        Check(NarrativeAssetStore.RemoveDialogueLink(dialogue, "n2", "p1") == null, "断线应成功");
        Check(dialogue.NpcReplies.Find(n => n.Id == "n2").PlayerReplies.Count == 0, "断线后邻接表应为空");

        // 改字段（检查器「应用修改」走这里）
        Check(NarrativeAssetStore.SetDialogueNpc(dialogue, "n1", "npc", "新台词", "0", "0") == null, "改 NPC 字段应成功");
        Check(dialogue.NpcReplies.Find(n => n.Id == "n1").Line.Text == "新台词", "台词应被改写");
        Check(NarrativeAssetStore.SetDialoguePlayer(dialogue, "p1", "再想想", "嗯…", "1", "0") == null, "改玩家选项应成功");
        var player = dialogue.PlayerReplies.Find(p => p.Id == "p1");
        Check(player.OptionText == "再想想" && player.AutoSelect, "玩家选项字段应被改写");

        // 拖拽节点写回位置（图上的拖拽最终调的就是这两个入口）
        Check(NarrativeAssetStore.SetQuestNodePosition(quest, "start", 123f, 45f) == null, "写任务状态位置应成功");
        var moved = quest.States.Find(s => s.Id == "start").Position;
        Check(moved.X == 123f && moved.Y == 45f, "任务状态位置应被写入");
        Check(NarrativeAssetStore.SetQuestNodePosition(quest, "b", 55f, 66f) == null, "写分支位置应成功");
        Check(quest.Branches.Find(b => b.Id == "b").Position.X == 55f, "分支位置应被写入");
        Check(NarrativeAssetStore.SetQuestNodePosition(quest, "nope", 0f, 0f) != null, "写不存在的节点应被拒");

        Check(NarrativeAssetStore.SetDialogueNodePosition(dialogue, "n1", 300f, 200f) == null, "写 NPC 位置应成功");
        Check(dialogue.NpcReplies.Find(n => n.Id == "n1").NodePosX == 300f, "NPC 位置应被写入");
        Check(NarrativeAssetStore.SetDialogueNodePosition(dialogue, "p1", 500f, 260f) == null, "写玩家节点位置应成功");
        Check(dialogue.PlayerReplies.Find(p => p.Id == "p1").NodePosY == 260f, "玩家节点位置应被写入");
    }

    // -------------------------------------------------------------------
    // 叠在一起的节点（手写资产常见）必须被检出并自动排开
    // -------------------------------------------------------------------

    private static void TestStackedDetection()
    {
        var stacked = MinimalQuest();
        stacked.States[0].Position = new NodePositionData { X = 0f, Y = 0f };
        stacked.States[1].Position = new NodePositionData { X = 0f, Y = 0f };
        Check(NarrativeAssetStore.HasStackedQuestNodes(stacked), "两个状态同格应被检出");
        NarrativeAssetStore.LayoutQuest(stacked);
        Check(!NarrativeAssetStore.HasStackedQuestNodes(stacked), "自动布局后不应再有叠在一起的节点");

        var branchStacked = MinimalQuest();
        branchStacked.Branches[0].Position = new NodePositionData { X = 0f, Y = 0f };
        Check(NarrativeAssetStore.HasStackedQuestNodes(branchStacked), "分支与状态同格应被检出");

        // 同一对状态间的并行分支：自动布局后必须各自错开
        var parallel = MinimalQuest();
        parallel.Branches.Add(new QuestBranchData { Id = "b2", FromStateId = "start", ToStateId = "win", Tasks = new List<TaskData>() });
        NarrativeAssetStore.LayoutQuest(parallel);
        Check(!NarrativeAssetStore.HasStackedQuestNodes(parallel), "并行分支自动布局后不应叠在一起");

        var dialogue = new DialogueData { DialogueId = "D_stack" };
        dialogue.NpcReplies = new List<NpcReplyData>
        {
            new NpcReplyData { Id = "root", IsRoot = true, Line = new DialogueLineData { Text = "a" }, NodePosX = 0f, NodePosY = 0f },
            new NpcReplyData { Id = "n2", Line = new DialogueLineData { Text = "b" }, NodePosX = 0f, NodePosY = 0f }
        };
        Check(NarrativeAssetStore.HasStackedDialogueNodes(dialogue), "两个 NPC 同格应被检出");
        NarrativeAssetStore.LayoutDialogue(dialogue);
        Check(!NarrativeAssetStore.HasStackedDialogueNodes(dialogue), "对话自动布局后不应再有叠在一起的节点");
    }

    // -------------------------------------------------------------------
    // 重命名必须级联改写引用
    // -------------------------------------------------------------------

    private static void TestRenames()
    {
        var quest = MinimalQuest();
        Check(NarrativeAssetStore.RenameQuestState(quest, "start", "begin") == null, "重命名状态应成功");
        Check(quest.StartStateId == "begin", "起始状态 id 应跟着改");
        Check(quest.Branches.Find(b => b.Id == "b").FromStateId == "begin", "分支起点应跟着改");
        Check(NarrativeAssetStore.RenameQuestState(quest, "begin", "win") != null, "与已有 id 冲突应被拒");

        Check(NarrativeAssetStore.RenameQuestBranch(quest, "b", "b_main") == null, "重命名分支应成功");
        Check(quest.Branches.Exists(b => b.Id == "b_main"), "分支新 id 应生效");
        Check(NarrativeAssetStore.RenameQuestBranch(quest, "b_main", "win") != null, "分支不能占用状态 id");

        var dialogue = new DialogueData { DialogueId = "D_rename" };
        dialogue.Speakers = new List<SpeakerData> { new SpeakerData { SpeakerId = "npc", DisplayName = "村民" } };
        dialogue.NpcReplies = new List<NpcReplyData>
        {
            new NpcReplyData
            {
                Id = "root", SpeakerId = "npc", IsRoot = true,
                Line = new DialogueLineData { Text = "你好" },
                NpcReplies = new List<string>(), PlayerReplies = new List<string> { "p1" }
            }
        };
        dialogue.PlayerReplies = new List<PlayerReplyData>
        {
            new PlayerReplyData { Id = "p1", OptionText = "继续", Line = new DialogueLineData { Text = "嗯" } }
        };

        Check(NarrativeAssetStore.RenameDialogueNode(dialogue, "p1", "p2") == null, "重命名对话节点应成功");
        Check(dialogue.NpcReplies[0].PlayerReplies.Contains("p2"), "父节点邻接表应改写为新 id");
        Check(!dialogue.NpcReplies[0].PlayerReplies.Contains("p1"), "旧 id 不应残留");
        Check(NarrativeAssetStore.RenameDialogueNode(dialogue, "p2", "root") != null, "节点 id 冲突应被拒");

        Check(NarrativeAssetStore.RenameDialogueSpeaker(dialogue, "npc", "elder") == null, "重命名说话者应成功");
        Check(dialogue.NpcReplies[0].SpeakerId == "elder", "NPC 节点 speakerId 应跟着改");
        Check(NarrativeAssetStore.RenameDialogueSpeaker(dialogue, "elder", "elder") == null, "改名成自身应为无操作成功");
    }

    private static QuestData MinimalQuest()    {
        var data = new QuestData { QuestId = "Q_min", StartStateId = "start" };
        data.States.Add(new QuestStateData
        {
            Id = "start",
            Description = "开始",
            StateType = EStateNodeType.Regular,
            Position = new NodePositionData { X = 0f, Y = 0f }
        });
        data.States.Add(new QuestStateData
        {
            Id = "win",
            Description = "完成",
            StateType = EStateNodeType.Success,
            Position = new NodePositionData { X = 400f, Y = 0f }
        });
        data.Branches.Add(new QuestBranchData
        {
            Id = "b",
            FromStateId = "start",
            ToStateId = "win",
            Tasks = new List<TaskData>()
        });
        return data;
    }

    private static int CountTempFiles(string directory)
    {
        try
        {
            return Directory.GetFiles(directory, "narrative_validate_*.json").Length;
        }
        catch
        {
            return 0;
        }
    }

    // -------------------------------------------------------------------
    // 内存快照与撤销/重做
    // -------------------------------------------------------------------

    private static void TestSnapshots()
    {
        string path = Path.Combine(Path.GetTempPath(), "narrative_selftest_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var quest = MinimalQuest();
            quest.States[0].Description = "快照前";
            quest.States[0].Position = new NodePositionData { X = 12f, Y = 34f };

            // 期望值在改动前一份份量下来：参照物不能是"再序列化一遍"，否则两边同错也发现不了
            var expectedIds = new List<string>();
            var expectedDescriptions = new List<string>();
            var expectedTypes = new List<EStateNodeType>();
            var expectedX = new List<float>();
            var expectedY = new List<float>();
            foreach (var state in quest.States)
            {
                expectedIds.Add(state.Id);
                expectedDescriptions.Add(state.Description);
                expectedTypes.Add(state.StateType);
                expectedX.Add(state.Position?.X ?? 0f);
                expectedY.Add(state.Position?.Y ?? 0f);
            }
            int expectedBranches = quest.Branches?.Count ?? 0;
            string expectedStart = quest.StartStateId;

            string snapshot = NarrativeAssetStore.SnapshotQuest(quest);
            Check(!string.IsNullOrEmpty(snapshot), "任务快照应是非空串");

            NarrativeAssetStore.WriteQuest(path, quest);
            Check(snapshot == File.ReadAllText(path), "任务快照串必须与写盘内容逐字相同（脏标记靠这个口径对照磁盘版本）");

            var restored = NarrativeAssetStore.RestoreQuest(snapshot);
            Check(restored != null && restored.States.Count == expectedIds.Count, "还原后状态数一致");
            Check(restored.StartStateId == expectedStart, "还原后起始状态一致");
            Check((restored.Branches?.Count ?? 0) == expectedBranches, "还原后分支数一致");

            bool fieldsMatch = true;
            for (int i = 0; i < expectedIds.Count; i++)
            {
                var state = restored.States.Find(s => s.Id == expectedIds[i]);
                if (state == null)
                {
                    fieldsMatch = false;
                    break;
                }
                if (state.Description != expectedDescriptions[i]) fieldsMatch = false;
                if (state.StateType != expectedTypes[i]) fieldsMatch = false;
                if ((state.Position?.X ?? 0f) != expectedX[i]) fieldsMatch = false;
                if ((state.Position?.Y ?? 0f) != expectedY[i]) fieldsMatch = false;
            }
            Check(fieldsMatch, "还原后每个状态的描述/类型/位置逐项一致");
            Check(NarrativeAssetStore.SnapshotQuest(restored) == snapshot, "快照→还原→再快照应逐字闭合");

            Check(NarrativeAssetStore.SnapshotQuest(null) == null, "空任务的快照应是 null");
            Check(NarrativeAssetStore.RestoreQuest(null) == null && NarrativeAssetStore.RestoreQuest("") == null, "空快照应还原成 null");

            var dialogue = new DialogueData
            {
                DialogueId = "D_snap",
                Speakers = new List<SpeakerData> { new SpeakerData { SpeakerId = "npc", DisplayName = "村民" } },
                NpcReplies = new List<NpcReplyData>
                {
                    new NpcReplyData
                    {
                        Id = "root",
                        SpeakerId = "npc",
                        IsRoot = true,
                        Line = new DialogueLineData { Text = "快照台词" },
                        NpcReplies = new List<string>(),
                        PlayerReplies = new List<string>()
                    }
                }
            };
            string dialogueSnapshot = NarrativeAssetStore.SnapshotDialogue(dialogue);
            Check(NarrativeAssetStore.RestoreDialogue(dialogueSnapshot).NpcReplies[0].Line.Text == "快照台词", "对话快照应带走台词");
            Check(NarrativeAssetStore.RestoreDialogue(dialogueSnapshot).Speakers[0].DisplayName == "村民", "对话快照应带走说话者名");
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    private static void TestHistory()
    {
        var history = new NarrativeHistory(3);
        history.OpenDocument("A");

        Check(history.UndoCount == 0 && history.RedoCount == 0, "开新文档时两侧历史都应为空");
        Check(history.Baseline == "A", "基线应是打开时的版本");

        Check(!history.Commit("A"), "内容没真变不应入栈");
        Check(history.UndoCount == 0, "空提交不应产生可撤销层");

        Check(history.Commit("B"), "真实改动应入栈");
        Check(history.Commit("C"), "第二次改动应再入一层");
        Check(history.UndoCount == 2, "两层改动应留下两层可撤销");
        Check(history.RedoCount == 0, "新改动应清空重做栈");

        Check(history.Undo("C") == "B", "撤销应回到改动前的那份");
        Check(history.RedoCount == 1, "撤销后应能重做回来");

        Check(history.Commit("D"), "撤销完再改应继续入栈");
        Check(history.RedoCount == 0, "撤销后另起改动，重做栈必须清空");

        // 撤销后另起一支，被放弃的那一支（C）不该再出现在回退链里
        Check(history.Undo("D") == "B" && history.Undo("B") == "A",
            "连续撤销应逐层回退到打开时的那份");
        Check(history.Undo("A") == null, "撤到尽头应返回 null 而不是继续回退");
        Check(history.Redo("A") == "B", "重做应回到刚撤掉的那份");
        Check(history.UndoCount == 1 && history.RedoCount == 1, "重做一步后两侧层数应各就各位");

        var tight = new NarrativeHistory(2);
        tight.OpenDocument("A");
        tight.Commit("B");
        tight.Commit("C");
        tight.Commit("D");
        Check(tight.UndoCount == 2, "超过上限时只应保留最近的层");
        Check(tight.Undo("D") == "C" && tight.Undo("C") == "B", "截断后仍应逐层向上回退");
        Check(tight.Undo("B") == null, "被上限挤掉的最老那份不该还能撤到");

        var single = new NarrativeHistory(1);
        single.OpenDocument("A");
        Check(single.Commit("B") && single.Commit("C"), "上限 1 时也应能连续提交");
        Check(single.UndoCount == 1, "上限 1 时只应留一层");
        Check(single.Undo("C") == "B" && single.Undo("B") == null, "上限 1 时撤一层就该到头");

        var fresh = new NarrativeHistory(5);
        Check(fresh.Undo("X") == null && fresh.Redo("X") == null, "空历史里撤销/重做都应返回 null");
        fresh.OpenDocument("A");
        Check(fresh.Commit(null), "null 快照与基线不同，应算一次改动");
        Check(fresh.Baseline == "", "null 应被归一成空串基线");
        Check(fresh.Undo("") == "A", "从空内容撤销应回到打开时的那份");
    }

    /// <summary>
    /// 检查器把"手打 id / 手打 0-1"换成下拉之后，下拉吐出的每个值都必须正好是存储层认得的值。
    /// 这里把这条契约钉住：界面侧一旦改了标签而忘了改映射，红的是这里，而不是运行时静默吃坏数据。
    /// </summary>
    private static void TestDropdownContracts()
    {
        var quest = MinimalQuest();

        var ids = NarrativeAssetStore.QuestStateIds(quest);
        Check(ids.Count == 2 && ids[0] == "start" && ids[1] == "win", "状态 id 清单按资产顺序给（下拉候选就是它）");
        Check(NarrativeAssetStore.QuestStateIds(null).Count == 0, "空任务的状态清单应是空表而不是抛异常");

        // 签名是 (data, stateId, description, stateType)：夹具第一次把后两个参数写反，红的是夹具不是产品
        string stateErr = NarrativeAssetStore.SetQuestState(quest, "start", "改", "Success");
        Check(stateErr == null, "状态下拉交名字，存储层要认（实际返回：" + (stateErr ?? "-") + "）");
        Check(quest.States[0].StateType == EStateNodeType.Success && quest.States[0].Description == "改", "名字要翻成枚举，描述同时落位");
        Check(NarrativeAssetStore.SnapshotQuest(quest).Contains("\"stateType\": 1"), "落盘仍是数值口径（运行时不认字符串枚举）");
        Check(NarrativeAssetStore.SetQuestState(quest, "start", "是", null) != null, "认不出的状态类型必须报错，不能静默退回 Regular");

        Check(NarrativeAssetStore.SetQuestBranch(quest, "b", "描述", "win", "start", "1") == null && quest.Branches[0].Hidden,
            "分支端点取下拉里的 id；布尔下拉写 \"1\" 应置真");
        Check(NarrativeAssetStore.SetQuestBranch(quest, "b", "描述", "win", "start", "0") == null && !quest.Branches[0].Hidden,
            "布尔下拉写 \"0\" 应置假");
        Check(NarrativeAssetStore.SetQuestBranch(quest, "b", "描述", "win", "ghost", "0") != null,
            "端点 id 失效（例如状态被改名后残留）要被拒，不能写坏引用");
        Check(NarrativeAssetStore.AddQuestState(quest, "s_new", "Failure", "新") == null &&
              quest.States.Find(s => s.Id == "s_new").StateType == EStateNodeType.Failure, "新建状态表单走同一套名字解析");

        var dialogue = new DialogueData
        {
            DialogueId = "D_dd",
            Speakers = new List<SpeakerData> { new SpeakerData { SpeakerId = "npc", DisplayName = "村民" } },
            NpcReplies = new List<NpcReplyData>
            {
                new NpcReplyData
                {
                    Id = "root", SpeakerId = "npc", IsRoot = true,
                    Line = new DialogueLineData { Text = "你好" },
                    NpcReplies = new List<string>(), PlayerReplies = new List<string>()
                }
            },
            PlayerReplies = new List<PlayerReplyData>
            {
                new PlayerReplyData { Id = "p_1", OptionText = "问路", Line = new DialogueLineData { Text = "..." } }
            }
        };

        var nodeIds = NarrativeAssetStore.DialogueNodeIds(dialogue);
        Check(nodeIds.Count == 2 && nodeIds[0] == "root" && nodeIds[1] == "p_1", "节点清单 NPC 在前、玩家选项在后");
        Check(NarrativeAssetStore.LinkDialogueAuto(dialogue, "root", "p_1") == null, "连线下拉选到的目标应能直接连");
        Check(dialogue.NpcReplies[0].PlayerReplies != null && dialogue.NpcReplies[0].PlayerReplies.Contains("p_1"),
            "连线要真的写进源节点，不是只回个成功");
        Check(NarrativeAssetStore.LinkDialogueAuto(dialogue, "root", "不存在的节点") != null, "选不到目标时报错要说清楚");

        Check(NarrativeAssetStore.SetDialogueSpeaker(dialogue, "npc", "村民", "1") == null && dialogue.Speakers[0].IsPlayer,
            "说话者布尔下拉写 \"1\" 应置真");
        Check(NarrativeAssetStore.SetDialogueSpeaker(dialogue, "npc", "村民", "0") == null && !dialogue.Speakers[0].IsPlayer,
            "说话者布尔下拉写 \"0\" 应置假");
        // 存储层对认不出的布尔文本是"保持原值"，不报错：界面侧必须自己把 是/否 翻成 1/0，否则用户点了没反应。
        dialogue.Speakers[0].IsPlayer = true;
        Check(NarrativeAssetStore.SetDialogueSpeaker(dialogue, "npc", "村民", "是") == null && dialogue.Speakers[0].IsPlayer,
            "中文标签直接进存储层会静默保持原值（钉住这个坑，映射只能留在界面侧）");

        Check(NarrativeAssetStore.DialogueAssetNames() != null, "对话资产名清单不该抛异常");
    }

    /// <summary>图上"长节点/反转分支"这三条新原语：它们是右键菜单唯一的数据入口。</summary>
    private static void TestGraphAuthoringPrimitives()
    {
        var quest = MinimalQuest();
        int statesBefore = quest.States.Count;
        int branchesBefore = quest.Branches.Count;

        Check(NarrativeAssetStore.AddQuestStateConnected(quest, "start", out string newId, out string newBranch) == null,
            "从起始状态长出一个新状态应成功");
        Check(quest.States.Count == statesBefore + 1 && quest.Branches.Count == branchesBefore + 1,
            "应各多一个状态与一条分支");

        var added = NarrativeAssetStore.FindQuestState(quest, newId);
        var branch = NarrativeAssetStore.FindQuestBranch(quest, newBranch);
        Check(added != null && branch != null, "新状态与新分支都按返回的 id 找得到");
        Check(branch.FromStateId == "start" && branch.ToStateId == newId, "分支方向是 源→新");
        Check(added.Position.X > quest.States[0].Position.X, "新状态排在源状态右侧");
        Check(added.Position.X != quest.States[1].Position.X || added.Position.Y != quest.States[1].Position.Y,
            "新节点不该和已有节点叠在同一格");

        Check(NarrativeAssetStore.AddQuestStateConnected(quest, "start", out string second, out string _) == null &&
              second != newId, "连着长两次应拿到不同 id");
        Check(NarrativeAssetStore.AddQuestStateConnected(quest, "ghost", out string none, out string _) != null && none == null,
            "源状态不存在时要拒绝且不留半成品");

        Check(NarrativeAssetStore.FlipQuestBranch(quest, newBranch) == null &&
              NarrativeAssetStore.FindQuestBranch(quest, newBranch).FromStateId == newId, "反转后起点变成新节点");
        Check(NarrativeAssetStore.FlipQuestBranch(quest, newBranch) == null &&
              NarrativeAssetStore.FindQuestBranch(quest, newBranch).ToStateId == newId, "反转两次应回到原样");
        Check(NarrativeAssetStore.FlipQuestBranch(quest, "b_ghost") != null, "反转不存在的分支要报错");

        quest.Branches.Add(new QuestBranchData { Id = "b_dangling", FromStateId = "start", ToStateId = "gone", Tasks = new List<TaskData>() });
        Check(NarrativeAssetStore.FlipQuestBranch(quest, "b_dangling") != null, "端点已失效的分支不许反转（会把坏引用挪到另一头）");
        // 这条坏分支是夹具故意造的，判完就撤掉：留着它，后面"长完节点还能被运行时装载"的判据就没意义了
        quest.Branches.RemoveAll(b => b.Id == "b_dangling");

        var dialogue = new DialogueData
        {
            DialogueId = "D_grow",
            Speakers = new List<SpeakerData>
            {
                new SpeakerData { SpeakerId = "npc", DisplayName = "村民" },
                new SpeakerData { SpeakerId = "you", DisplayName = "旅人", IsPlayer = true }
            },
            NpcReplies = new List<NpcReplyData>
            {
                new NpcReplyData
                {
                    Id = "root", SpeakerId = "npc", IsRoot = true,
                    Line = new DialogueLineData { Text = "你好" },
                    NpcReplies = new List<string>(), PlayerReplies = new List<string>()
                }
            },
            PlayerReplies = new List<PlayerReplyData>()
        };

        Check(NarrativeAssetStore.AddDialogueNpcReplyConnected(dialogue, "root", out string grown) == null,
            "对话节点往下长一个应成功");
        var grownNode = NarrativeAssetStore.FindNpcNode(dialogue, grown);
        Check(grownNode != null && grownNode.SpeakerId == "npc", "新节点沿用源节点的说话者");
        Check(dialogue.NpcReplies[0].NpcReplies != null && dialogue.NpcReplies[0].NpcReplies.Contains(grown),
            "源节点要真的连上新节点");
        Check(grownNode.NodePosX > dialogue.NpcReplies[0].NodePosX, "新节点排在右下方");
        Check(NarrativeAssetStore.AddDialogueNpcReplyConnected(dialogue, "root", out string grown2) == null &&
              grown2 != grown, "再长一次应拿到不同 id");

        dialogue.NpcReplies[0].SpeakerId = "不存在的说话者";
        Check(NarrativeAssetStore.AddDialogueNpcReplyConnected(dialogue, "root", out string _) != null,
            "源节点说话者失效时要拒绝，而不是造个运行时找不到发言人的节点");

        // 长跑之后资产仍应被运行时装载器读懂（长节点不该产出结构上非法的图）
        var questProblems = NarrativeAssetStore.ValidateQuestData(quest);
        var dialogueProblems = NarrativeAssetStore.ValidateDialogueData(dialogue);
        Console.WriteLine($"graph-validate-after-grow: quest={questProblems.Count} dialogue={dialogueProblems.Count}");
        foreach (var problem in questProblems) Console.WriteLine("  quest: " + problem);
        foreach (var problem in dialogueProblems) Console.WriteLine("  dialogue: " + problem);
        Check(questProblems.Count == 0 && dialogueProblems.Count == 0, "长完节点、反转完分支后运行时校验仍应通过");
    }

    /// <summary>资产级文件操作：改名要连内部 id 一起动、复制不能动原件、删除要报错而不是静默。</summary>
    private static void TestAssetFileOps()
    {
        string dir = Path.Combine(Path.GetTempPath(), "narrative_selftest_files_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string src = Path.Combine(dir, "Q_src.json");
            NarrativeAssetStore.WriteQuest(src, MinimalQuest());

            Check(NarrativeAssetStore.RenameQuestAsset(src, "Q_renamed", out string renamed) == null, "重命名应成功");
            Check(!File.Exists(src) && File.Exists(renamed), "旧文件应消失、新文件应存在");
            Check(NarrativeAssetStore.LoadQuestData(renamed).QuestId == "Q_renamed", "内部 QuestId 应与新文件名同步");
            Check(NarrativeAssetStore.ValidateQuestFile(renamed).Count == 0, "重命名后的文件应通过运行时校验");

            Check(NarrativeAssetStore.RenameQuestAsset(renamed, "  ", out string _) != null, "空新名要拒绝");
            Check(NarrativeAssetStore.RenameQuestAsset(renamed, "Q_renamed", out string _) != null, "新旧同名要拒绝");
            Check(NarrativeAssetStore.RenameQuestAsset(renamed, "Q_src", out string _) == null && File.Exists(src),
                "改回原名后原路径应回来");

            Check(NarrativeAssetStore.DuplicateQuestAsset(src, "Q_copy", out string copy) == null, "复制应成功");
            Check(File.Exists(src) && File.Exists(copy), "复制后原文件必须还在");
            Check(NarrativeAssetStore.LoadQuestData(copy).QuestId == "Q_copy", "副本的 id 应是新名");
            Check(NarrativeAssetStore.DuplicateQuestAsset(src, "Q_copy", out string _) != null, "撞名的复制要拒绝");

            var quest = NarrativeAssetStore.LoadQuestData(src);
            Check(NarrativeAssetStore.AddQuestBranchConnected(quest, "start", "start", out string _) != null, "自连要拒绝");
            Check(NarrativeAssetStore.AddQuestBranchConnected(quest, "start", "win", out string linkId) == null, "状态间连线应成功");
            var link = NarrativeAssetStore.FindQuestBranch(quest, linkId);
            Check(link != null && link.FromStateId == "start" && link.ToStateId == "win", "连线方向应是 源→目标");
            Check(NarrativeAssetStore.AddQuestBranchConnected(quest, "start", "win", out string link2) == null && link2 != linkId,
                "同一条线再连一次应拿到不同分支 id");
            Check(NarrativeAssetStore.ValidateQuestData(quest).Count == 0, "连完线运行时校验应通过");

            Check(NarrativeAssetStore.DeleteAssetFile(copy) == null && !File.Exists(copy), "删除应生效");
            Check(NarrativeAssetStore.DeleteAssetFile(copy) != null, "删不存在的文件要报错而不是静默成功");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>全文检索与大纲：搜索要能落到"哪个文件哪个节点"，大纲要按可达顺序排。</summary>
    private static void TestSearchAndOutline()
    {
        string dir = Path.Combine(Path.GetTempPath(), "narrative_selftest_search_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var quest = MinimalQuest();
            quest.States.Add(new QuestStateData
            {
                Id = "gather",
                Description = "gather 三七药草并 gather 水分",
                Position = new NodePositionData { X = 800f, Y = 0f }
            });
            string questPath = Path.Combine(dir, "Q_search.json");
            NarrativeAssetStore.WriteQuest(questPath, quest);

            var hits = NarrativeAssetStore.SearchContent(questPath, true, "gather", 5);
            Check(hits.Count == 2, "id 与描述各命中一次（共两条）");
            Check(hits[0].KindTag == "state" && hits[0].NodeId == "gather", "命中要带节点身份");
            Check(hits[0].Snippet.Contains("gather"), "片段里要看得见查询词");
            Check(NarrativeAssetStore.SearchContent(questPath, true, "gather", 1).Count == 1, "limit 要真的截住");
            Check(NarrativeAssetStore.SearchContent(questPath, true, "zzz", 5).Count == 0, "搜不到就是空表");
            Check(NarrativeAssetStore.SearchContent(questPath, true, "g", 5).Count == 0, "单字符查询直接不搜（否则每敲一下都扫盘）");

            var dialogue = new DialogueData
            {
                DialogueId = "D_search",
                Speakers = new List<SpeakerData> { new SpeakerData { SpeakerId = "npc", DisplayName = "村民" } },
                NpcReplies = new List<NpcReplyData>
                {
                    new NpcReplyData
                    {
                        Id = "root", SpeakerId = "npc", IsRoot = true,
                        Line = new DialogueLineData { Text = "你好，旅人" },
                        NpcReplies = new List<string>(),
                        PlayerReplies = new List<string> { "p_ask" }
                    },
                    new NpcReplyData
                    {
                        Id = "orphan", SpeakerId = "npc",
                        Line = new DialogueLineData { Text = "没人接的话" },
                        NpcReplies = new List<string>(), PlayerReplies = new List<string>()
                    }
                },
                PlayerReplies = new List<PlayerReplyData>
                {
                    new PlayerReplyData { Id = "p_ask", OptionText = "问路怎么走", Line = new DialogueLineData { Text = "" } }
                }
            };
            string dialoguePath = Path.Combine(dir, "D_search.json");
            NarrativeAssetStore.WriteDialogue(dialoguePath, dialogue);

            var npcHits = NarrativeAssetStore.SearchContent(dialoguePath, false, "旅人", 5);
            Check(npcHits.Count == 1 && npcHits[0].KindTag == "npc" && npcHits[0].NodeId == "root", "台词命中要落到 NPC 节点");
            var playerHits = NarrativeAssetStore.SearchContent(dialoguePath, false, "问路", 5);
            Check(playerHits.Count == 1 && playerHits[0].KindTag == "player" && playerHits[0].NodeId == "p_ask", "选项文本命中要落到玩家节点");
            var speakerHits = NarrativeAssetStore.SearchContent(dialoguePath, false, "村民", 5);
            Check(speakerHits.Count == 1 && speakerHits[0].KindTag == "speaker", "说话者名也要搜得到");

            string questOutline = NarrativeAssetStore.DescribeQuestOutline(quest);
            Check(questOutline.IndexOf("1. start", StringComparison.Ordinal) >= 0, "大纲从起始状态开始编号");
            Check(questOutline.IndexOf("→ win", StringComparison.Ordinal) >= 0, "分支要列出指向");
            Check(questOutline.Contains("（不可达）gather"), "没被任何分支指到的状态要单列出来");

            string dialogueOutline = NarrativeAssetStore.DescribeDialogueOutline(dialogue);
            Check(dialogueOutline.Contains("[村民] 你好，旅人"), "大纲要带说话者名与台词");
            Check(dialogueOutline.Contains("玩家：问路怎么走"), "玩家选项要缩进列出");
            Check(dialogueOutline.Contains("（不可达）orphan"), "不可达节点要单列出来");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>引用完整性审计：运行时装载器不管的那些作者错误（死节点、悬空引用、没人用的说话者）。</summary>
    private static void TestReferenceAudit()
    {
        var clean = MinimalQuest();
        Check(NarrativeAssetStore.AuditQuest(clean).Count == 0, "干净的任务应零发现（正例对照，防止审计恒红）");

        var broken = MinimalQuest();
        broken.QuestDialogueClass = "D_不存在的对话";
        broken.States.Add(new QuestStateData
        {
            Id = "dead",
            Description = "没人指向也没有出口",
            StateType = EStateNodeType.Regular,
            Position = new NodePositionData { X = 1200f, Y = 0f }
        });
        broken.Branches.Add(new QuestBranchData
        {
            Id = "b2",
            FromStateId = "start",
            ToStateId = "gone",
            Tasks = new List<TaskData>()
        });

        var questIssues = NarrativeAssetStore.AuditQuest(broken);
        int questErrors = 0, questWarnings = 0;
        foreach (var issue in questIssues)
        {
            if (issue.IsError) questErrors++;
            else questWarnings++;
        }
        Check(questErrors == 2, "悬空终点 + 关联对话缺文件 = 2 项错误（实际 " + questErrors + "）");
        Check(questWarnings == 2, "死节点 = 不可达 + 无出口 两条提醒（实际 " + questWarnings + "）");
        Check(questIssues.Exists(i => i.NodeId == "b2" && i.IsError), "悬空分支要能定位到分支节点");
        Check(questIssues.Exists(i => i.Message.Contains("关联对话") && i.IsError), "关联对话缺文件要报错误");
        Check(questIssues.Exists(i => i.NodeId == "dead" && !i.IsError), "死节点提醒要带节点 id 供跳转");
        Check(!questIssues.Exists(i => i.NodeId == "win"), "终点状态不该被误报（正例对照）");

        var cleanDialogue = new DialogueData
        {
            DialogueId = "D_ok",
            Speakers = new List<SpeakerData> { new SpeakerData { SpeakerId = "npc", DisplayName = "村民" } },
            NpcReplies = new List<NpcReplyData>
            {
                new NpcReplyData
                {
                    Id = "root", SpeakerId = "npc", IsRoot = true,
                    Line = new DialogueLineData { Text = "你好" },
                    NpcReplies = new List<string>(), PlayerReplies = new List<string> { "p1" }
                }
            },
            PlayerReplies = new List<PlayerReplyData>
            {
                new PlayerReplyData { Id = "p1", OptionText = "嗯", Line = new DialogueLineData { Text = "" }, NpcReplies = new List<string>() }
            }
        };
        Check(NarrativeAssetStore.AuditDialogue(cleanDialogue).Count == 0, "干净的对话应零发现（正例对照）");

        cleanDialogue.Speakers.Add(new SpeakerData { SpeakerId = "unused", DisplayName = "路人" });
        // 玩家说话者按约定不被 NPC 节点引用（D001 里就有），审计不该把它当缺陷
        cleanDialogue.Speakers.Add(new SpeakerData { SpeakerId = "Player", DisplayName = "旅人", IsPlayer = true });
        cleanDialogue.NpcReplies.Add(new NpcReplyData
        {
            Id = "orphan",
            SpeakerId = "不存在的说话者",
            Line = new DialogueLineData { Text = "悬空" },
            NpcReplies = new List<string>(),
            PlayerReplies = new List<string> { "p_missing" }
        });

        var dialogueIssues = NarrativeAssetStore.AuditDialogue(cleanDialogue);
        int dialogueErrors = 0, dialogueWarnings = 0;
        foreach (var issue in dialogueIssues)
        {
            if (issue.IsError) dialogueErrors++;
            else dialogueWarnings++;
        }
        Check(dialogueErrors == 2, "说话者悬空 + 连线目标缺失 = 2 项错误（实际 " + dialogueErrors + "）");
        Check(dialogueWarnings == 2, "没人用的说话者 + 走不到的节点 = 2 条提醒（实际 " + dialogueWarnings + "）");
        Check(!dialogueIssues.Exists(i => i.NodeId == "Player"), "玩家说话者没被 NPC 引用属于约定，不该报警");
        Check(dialogueIssues.Exists(i => i.NodeId == "orphan" && i.IsError), "悬空引用要能定位到出问题的节点");
    }

    /// <summary>叙事资产 ↔ 任务编辑器的显式链接：字段口径、一键骨架、扫描与重复声明警告。</summary>
    private static void TestTemplateLink()
    {
        string dir = Path.Combine(Path.GetTempPath(), "narrative_selftest_link_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var quest = MinimalQuest();
            Check(quest.QuestTemplateId == null, "新资产的模板 id 默认为空（不影响老文件）");

            Check(NarrativeAssetStore.SetQuestTemplateId(quest, "") == null && quest.QuestTemplateId == null, "空串＝清空链接");
            Check(NarrativeAssetStore.SetQuestTemplateId(quest, "abc") != null, "非数字要拒绝");
            Check(NarrativeAssetStore.SetQuestTemplateId(quest, "0") != null, "0 要拒绝");
            Check(NarrativeAssetStore.SetQuestTemplateId(quest, " 10001 ") == null && quest.QuestTemplateId == 10001, "数字（带空格）应写入");
            Check(NarrativeAssetStore.SnapshotQuest(quest).Contains("\"questTemplateId\": 10001"), "模板 id 要进快照/落盘");

            Check(NarrativeAssetStore.CreateQuestAssetWithTemplate(20001, "Q_linktest", out string created, out string assetId, dir) == null &&
                  File.Exists(created), "一键骨架应落盘");
            Check(assetId == "Q_linktest", "返回的资产 id 应是文件名");
            var skeleton = NarrativeAssetStore.LoadQuestData(created);
            Check(skeleton.QuestTemplateId == 20001 && skeleton.StartStateId == "start", "骨架应带模板 id 与起始状态");
            Check(NarrativeAssetStore.AuditQuest(skeleton).Count == 0, "骨架本身要过审计（开始→完成，不是半成品）");
            Check(NarrativeAssetStore.CreateQuestAssetWithTemplate(20002, "Q_linktest", out string _, out string _, dir) != null,
                "同名骨架要拒绝");

            var second = MinimalQuest();
            second.QuestId = "Q_linktest2";
            NarrativeAssetStore.SetQuestTemplateId(second, "20001");
            NarrativeAssetStore.WriteQuest(Path.Combine(dir, "Q_linktest2.json"), second);

            Check(NarrativeAssetStore.QuestAssetsForTemplate(20001, dir).Count == 2, "两条资产声明同一模板时应都列出");
            Check(NarrativeAssetStore.QuestAssetsForTemplate(99999, dir).Count == 0, "没人声明的模板应为空表");

            var conflicts = NarrativeAssetStore.AuditTemplateLink(skeleton, created, dir);
            Check(conflicts.Count == 1 && !conflicts[0].IsError, "重复声明给 1 条警告（不是错误）");
            Check(conflicts[0].Message.Contains("Q_linktest2"), "警告要点名另一条资产");

            // 链接状态的一句话口径（审计与界面共用）：目录没查 ≠ 模板不存在
            Check(NarrativeAssetStore.DescribeTemplateLinkStatus(10001, false, true, true, 1) == "目录未查", "没查目录时要如实说");
            Check(NarrativeAssetStore.DescribeTemplateLinkStatus(10001, true, false, true, 1) == "模板不存在", "目录查了但没有这条");
            Check(NarrativeAssetStore.DescribeTemplateLinkStatus(10001, true, true, false, 1) == "模板已作废", "作废优先于发布状态");
            Check(NarrativeAssetStore.DescribeTemplateLinkStatus(10001, true, true, true, 1) == "已发布", "已发布口径");
            Check(NarrativeAssetStore.DescribeTemplateLinkStatus(10001, true, true, true, 0) == "未发布（草稿/下线）", "草稿口径");

            // 模板 → 资产映射（任务清单的 ▣ 标记与资产行副标题共用）
            var map = NarrativeAssetStore.TemplateAssetMap(dir);
            Check(map.ContainsKey(20001) && map[20001] == "Q_linktest", "映射要给出模板对应的资产名");
            Check(!map.ContainsKey(99999), "没被声明的模板不进映射");
            Check(NarrativeAssetStore.QuestLinkLabel(skeleton) == "模板 20001", "资产头标签口径");
            Check(NarrativeAssetStore.QuestLinkLabel(MinimalQuest()) == "", "没挂模板时不显示标签");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>
    /// 融合契约：任务编辑器是**反射**装配内嵌叙事面板的，所以面板的公开 API 与"必须是 Control"这两条
    /// 只能靠测试钉住——改坏了编译门不会红（两边没编译依赖），运行时才发现。
    /// </summary>
    private static void TestFusionContracts()
    {
        var panel = typeof(NarrativePro.EditorTools.NarrativeQuestPanel);
        Check(typeof(FlaxEngine.GUI.Control).IsAssignableFrom(panel), "内嵌面板必须是 Control（任务编辑器直接 AddChild）");

        var create = panel.GetMethod("Create", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Check(create != null && create.ReturnType == typeof(FlaxEngine.GUI.Control), "面板必须提供 Create() → Control");

        foreach (var name in new[] { "OpenForTemplate", "EnsureForTemplate", "ClearForTemplate", "OpenQuest", "Save", "ValidateNow", "Undo", "Redo" })
        {
            var method = panel.GetMethod(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            Check(method != null, "内嵌面板必须公开方法 " + name + "（任务编辑器靠反射调它）");
        }

        var host = typeof(NarrativePro.EditorTools.INarrativeEditorHost);
        Check(host.IsInterface && host.IsPublic, "宿主接口必须是公开接口（窗口与面板跨类型共用）");
        Check(host.IsAssignableFrom(typeof(NarrativePro.EditorTools.NarrativeProAuthoringWindow)),
            "独立窗口必须实现宿主接口（否则检查器拿不到宿主）");
        Check(typeof(FlaxEngine.GUI.Control).IsAssignableFrom(
                typeof(NarrativePro.EditorTools.NarrativeGraphView)), "图视图必须是 Control（可被面板/窗口共同托管）");
    }

    /// <summary>对话镜头：Shots 目录解析、逐行/说话者镜头写回、坏引用。</summary>
    private static void TestDialogueShots()
    {
        string dir = Path.Combine(Path.GetTempPath(), "np_shots_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Shot_Medium.json"),
                "{\"shotName\":\"Shot_Medium\",\"friendlyShotName\":\"Medium\",\"aliases\":[\"DS_Medium\"]}");
            File.WriteAllText(Path.Combine(dir, "Shot_CloseUp.json"), "{\"shotName\":\"Shot_CloseUp\"}");

            var choices = NarrativeAssetStore.ShotChoices(dir);
            Check(choices.Count == 2, "镜头清单应读到 2 个模板（实际 " + choices.Count + "）");
            Check(choices.Exists(c => c.Value == "Shot_Medium" && c.Display == "Medium (Shot_Medium)"), "有友好名时显示成 友好名(真名)");
            Check(choices.Exists(c => c.Value == "Shot_CloseUp" && c.Display == "Shot_CloseUp"), "没有友好名时显示真名");

            var known = NarrativeAssetStore.KnownShotNames(dir);
            Check(known.Contains("Shot_Medium") && known.Contains("Shot_CloseUp") && known.Contains("DS_Medium"),
                "已知镜头集合要含真名与别名");

            var dialogue = new DialogueData
            {
                DialogueId = "D_shot",
                Speakers = new List<SpeakerData>
                {
                    new SpeakerData { SpeakerId = "npc", DisplayName = "村民" },
                    new SpeakerData { SpeakerId = "you", DisplayName = "旅人", IsPlayer = true }
                },
                NpcReplies = new List<NpcReplyData>
                {
                    new NpcReplyData
                    {
                        Id = "root", SpeakerId = "npc", IsRoot = true,
                        Line = new DialogueLineData { Text = "你好" },
                        NpcReplies = new List<string>(), PlayerReplies = new List<string> { "p_1" }
                    }
                },
                PlayerReplies = new List<PlayerReplyData>
                {
                    new PlayerReplyData { Id = "p_1", OptionText = "嗯" }
                }
            };

            Check(NarrativeAssetStore.SetDialogueLineShot(dialogue, "root", "Shot_Medium") == null &&
                  dialogue.NpcReplies[0].Line.ShotName == "Shot_Medium", "NPC 行的镜头要写回");
            Check(NarrativeAssetStore.SetDialogueLineShot(dialogue, "p_1", "Shot_CloseUp") == null &&
                  dialogue.PlayerReplies[0].Line.ShotName == "Shot_CloseUp", "玩家行的镜头要写回（原本没有 Line 也要建出来）");
            Check(NarrativeAssetStore.SetDialogueLineShot(dialogue, "root", "") == null &&
                  dialogue.NpcReplies[0].Line.ShotName == "", "空串＝清除镜头、回落说话者默认");
            Check(NarrativeAssetStore.SetDialogueLineShot(dialogue, "ghost", "Shot_Medium") != null, "未知节点要拒绝");

            Check(NarrativeAssetStore.SetDialogueSpeakerShots(dialogue, "npc", "Shot_CloseUp", "") == null &&
                  dialogue.Speakers[0].DefaultShotName == "Shot_CloseUp", "说话者默认镜头要写回");
            Check(NarrativeAssetStore.SetDialogueSpeakerShots(dialogue, "ghost", "x", "y") != null, "未知说话者要拒绝");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>行内 CG：视频清单、写入/清除、等待开关、路径不存在要让审计喊。</summary>
    private static void TestDialogueCutscenes()
    {
        string dir = Path.Combine(Path.GetTempPath(), "np_movies_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "intro.mp4"), "x");
            File.WriteAllText(Path.Combine(dir, "outro.webm"), "x");
            File.WriteAllText(Path.Combine(dir, "readme.txt"), "x");

            var movies = NarrativeAssetStore.MovieChoices(dir);
            Check(movies.Count == 2, "视频清单只收 mp4/webm（实际 " + movies.Count + "）");

            var dialogue = new DialogueData
            {
                DialogueId = "D_cg",
                Speakers = new List<SpeakerData> { new SpeakerData { SpeakerId = "npc", DisplayName = "村民" } },
                NpcReplies = new List<NpcReplyData>
                {
                    new NpcReplyData
                    {
                        Id = "root", SpeakerId = "npc", IsRoot = true,
                        Line = new DialogueLineData { Text = "看那边" },
                        NpcReplies = new List<string>(), PlayerReplies = new List<string>()
                    }
                },
                PlayerReplies = new List<PlayerReplyData>()
            };

            Check(NarrativeAssetStore.SetDialogueLineCutscene(dialogue, "root", "Content/Movies/intro.mp4", true) == null &&
                  dialogue.NpcReplies[0].Line.CutscenePath == "Content/Movies/intro.mp4",
                "CG 路径要写回行上");
            Check(dialogue.NpcReplies[0].Line.WaitForCutscene, "等 CG 播完的开关要写回");
            Check(NarrativeAssetStore.SnapshotDialogue(dialogue).Contains("\"cutscenePath\": "), "CG 路径要进快照/落盘");

            Check(NarrativeAssetStore.SetDialogueLineCutscene(dialogue, "root", "", true) == null &&
                  dialogue.NpcReplies[0].Line.CutscenePath == "" && !dialogue.NpcReplies[0].Line.WaitForCutscene,
                "清空 CG 时应顺带把等待开关关掉");
            Check(NarrativeAssetStore.SetDialogueLineCutscene(dialogue, "ghost", "x", false) != null, "未知节点要拒绝");

            // 审计：指向不存在的文件要报；指向真实文件不报
            dialogue.NpcReplies[0].Line.CutscenePath = Path.Combine(dir, "missing.mp4");
            Check(NarrativeAssetStore.AuditDialogue(dialogue).Exists(i => i.Message.Contains("文件不存在")), "CG 文件缺失要被审计喊出来");
            dialogue.NpcReplies[0].Line.CutscenePath = Path.Combine(dir, "intro.mp4");
            Check(!NarrativeAssetStore.AuditDialogue(dialogue).Exists(i => i.Message.Contains("文件不存在")), "CG 文件存在时不该报");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>
    /// 叙事里穿插任务：快捷事件的目标清单、写出的 JSON 字段，以及审计对"目标缺失 / 目标为空"的判定。
    /// </summary>
    private static void TestStoryEvents()
    {
        // 事件向导的两个下拉：磁盘资产 → 真实 id
        var questNames = NarrativeAssetStore.QuestAssetNames();
        Check(questNames.Contains("Q001_HerbRations"), "任务资产清单应包含 Q001_HerbRations");
        Check(NarrativeAssetStore.QuestStateIdsFor("Q001_HerbRations").Contains("s_begin"), "起始状态下拉要能读到真实状态 id");
        Check(NarrativeAssetStore.QuestStateIdsFor("Q_not_exists").Count == 0, "不存在的任务资产状态清单应为空");
        Check(NarrativeAssetStore.DialogueNodeIdsFor("D001_HerbRationsIntro").Contains("n_greet"), "对话起始节点下拉要能读到真实节点 id");
        Check(NarrativeAssetStore.DialogueNodeIdsFor("D_not_exists").Count == 0, "不存在的对话资产节点清单应为空");

        var quest = MinimalQuest();
        Check(NarrativeAssetStore.AddQuestLogic(quest, "start", NarrativeLogicKind.Event, "StartQuestEvent",
                "{\"questClassId\":\"Q001_HerbRations\",\"startFromId\":\"s_begin\"}") == null, "开始任务事件应写入");
        var startEvent = quest.States[0].Events[0];
        Check(ReadJsonString(startEvent, "type") == "StartQuestEvent", "事件类型应写真实注册名");
        Check(ReadJsonString(startEvent, "questClassId") == "Q001_HerbRations", "事件目标应写任务资产名");
        Check(ReadJsonString(startEvent, "startFromId") == "s_begin", "起始状态应落盘");
        Check(!NarrativeAssetStore.AuditQuest(quest).Exists(i => i.Message.Contains("StartQuestEvent")), "目标存在时不该报开始任务问题");

        Check(NarrativeAssetStore.AddQuestLogic(quest, "b", NarrativeLogicKind.Event, "CompleteQuestEvent",
                "{\"questClassId\":\"Q002_NightfallRelief\",\"bSucceed\":false,\"message\":\"误了\"}") == null, "结算任务事件应写入分支");
        var completeEvent = quest.Branches[0].Events[0];
        Check(string.Equals(ReadJsonString(completeEvent, "bSucceed"), "false", StringComparison.OrdinalIgnoreCase),
            "结算失败要落成 false（实际 " + ReadJsonString(completeEvent, "bSucceed") + "）");
        Check(ReadJsonString(completeEvent, "message") == "误了", "结算消息应落盘");

        // 引用不存在的资产 = 警告级（流程能跑，但事件会被跳过）
        var missing = MinimalQuest();
        NarrativeAssetStore.AddQuestLogic(missing, "start", NarrativeLogicKind.Event, "StartQuestEvent",
            "{\"questClassId\":\"Q_does_not_exist\"}");
        Check(NarrativeAssetStore.AuditQuest(missing).Exists(i => !i.IsError && i.Message.Contains("Q_does_not_exist")),
            "引用不存在的任务资产要给出警告");

        // 空目标 = 错误级
        var empty = MinimalQuest();
        NarrativeAssetStore.AddQuestLogic(empty, "start", NarrativeLogicKind.Event, "CompleteQuestEvent", "{}");
        Check(NarrativeAssetStore.AuditQuest(empty).Exists(i => i.IsError && i.Message.Contains("没有填任务类")),
            "空任务类要报错误");

        // 对话节点上的"穿插任务 / 播对话"事件同样受审计
        var dialogue = new DialogueData
        {
            DialogueId = "D_event",
            Speakers = new List<SpeakerData> { new SpeakerData { SpeakerId = "npc", DisplayName = "村民" } },
            NpcReplies = new List<NpcReplyData>
            {
                new NpcReplyData
                {
                    Id = "root", SpeakerId = "npc", IsRoot = true,
                    Line = new DialogueLineData { Text = "去吧" },
                    NpcReplies = new List<string>(), PlayerReplies = new List<string>()
                }
            },
            PlayerReplies = new List<PlayerReplyData>()
        };
        Check(NarrativeAssetStore.AddDialogueLogic(dialogue, "root", NarrativeLogicKind.Event, "BeginDialogueEvent",
                "{\"dialogueClassId\":\"D_does_not_exist\",\"startFromID\":\"n_greet\"}") == null, "播放对话事件应写入");
        Check(ReadJsonString(dialogue.NpcReplies[0].Events[0], "startFromID") == "n_greet", "对话起始节点应落盘");
        Check(NarrativeAssetStore.AuditDialogue(dialogue).Exists(i => i.Message.Contains("D_does_not_exist")),
            "对话事件引用不存在的对话要给出警告");

        // 图内事件提示：作者不点开检查器也要能看见"这里穿插了任务/对话"
        Check(NarrativeGraphView.EventTagSuffix(null) == "", "无事件时图内不显示标签");
        Check(NarrativeGraphView.EventTagSuffix(quest.States[0].Events).Contains("任务×1"), "开始任务要在图上显示任务标签");

        using (var doc = System.Text.Json.JsonDocument.Parse(
                   "[{\"type\":\"StartQuest\"},{\"type\":\"CompleteQuestEvent\"},{\"type\":\"BeginDialogue\"},{\"type\":\"SetTag\"},{\"type\":\"PlaySoundEvent\"}]"))
        {
            var mixed = new List<System.Text.Json.JsonElement>();
            foreach (var element in doc.RootElement.EnumerateArray()) mixed.Add(element.Clone());
            string suffix = NarrativeGraphView.EventTagSuffix(mixed);
            Check(suffix.Contains("任务×2") && suffix.Contains("对话×1") && suffix.Contains("事件×2"),
                "图内标签按类别计数（实际 " + suffix + "）");
        }
    }

    /// <summary>对话级设置（黑边/禁跳/移动/退出/优先级/默认镜头/自动结束距离）写回与审计。</summary>
    private static void TestDialogueConfig()
    {
        var dialogue = new DialogueData { DialogueId = "D_cfg" };

        Check(NarrativeAssetStore.SetDialogueConfig(dialogue, true, true, false, false, 7, "Shot_Medium", 12.5f) == null,
            "对话级设置应写回");
        Check(dialogue.Config != null && dialogue.Config.ShowCinematicBars && dialogue.Config.Unskippable,
            "电影黑边 / 禁止跳过要写回");
        Check(!dialogue.Config.FreeMovement && !dialogue.Config.CanBeExited, "允许移动 / 可中途退出要写回");
        Check(dialogue.Config.Priority == 7 && dialogue.Config.DefaultShotName == "Shot_Medium" &&
              Math.Abs(dialogue.Config.EndDialogueDist - 12.5f) < 0.001f, "优先级 / 默认镜头 / 自动结束距离要写回");
        Check(NarrativeAssetStore.SnapshotDialogue(dialogue).Contains("\"showCinematicBars\": true"),
            "对话级设置要进快照/落盘");

        dialogue.Config.DefaultShotName = "Shot_does_not_exist";
        Check(NarrativeAssetStore.AuditDialogue(dialogue).Exists(i => i.Message.Contains("Shot_does_not_exist")),
            "未知默认镜头要被审计喊出来");
    }

    /// <summary>
    /// 自检在仓库根启动，而叙事资产在 Flax 工程目录下。沿着当前目录/程序目录向上找 .flaxproj，
    /// 用运行时认可的 HUNDUN_PROJECT_DIR 钉住工程根，否则"资产清单"这类断言只能读到空表。
    /// </summary>
    private static void CalibrateProjectDirectory()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HUNDUN_PROJECT_DIR"))) return;

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            for (int hop = 0; dir != null && hop < 8; hop++, dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "HundunWorld.flaxproj")))
                {
                    Environment.SetEnvironmentVariable("HUNDUN_PROJECT_DIR", dir.FullName);
                    return;
                }

                string nested = Path.Combine(dir.FullName, "HundunWorld");
                if (File.Exists(Path.Combine(nested, "HundunWorld.flaxproj")))
                {
                    Environment.SetEnvironmentVariable("HUNDUN_PROJECT_DIR", nested);
                    return;
                }
            }
        }
    }

    private static string ReadJsonString(System.Text.Json.JsonElement element, string name)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            return prop.Value.ValueKind == System.Text.Json.JsonValueKind.String
                ? (prop.Value.GetString() ?? "")
                : prop.Value.ToString();
        }
        return "";
    }

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (condition) return;
        _failures++;
        Console.WriteLine("FAIL: " + message);
    }
}
