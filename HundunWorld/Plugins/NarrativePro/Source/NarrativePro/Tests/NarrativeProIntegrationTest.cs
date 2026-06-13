using System;
using System.Collections.Generic;
using System.IO;
using NarrativePro.Core;
using NarrativePro.Tales;
using NarrativePro.Tales.Data;
using NarrativePro.Tales.Dialogue;
using NarrativePro.Tales.Nodes;
using NarrativePro.Tales.Quest;
using NarrativePro.Tales.Tasks;
using NarrativePro.Save;

namespace NarrativePro.Tests
{
    public static class NarrativeProIntegrationTest
    {
        private static List<string> _testResults = new List<string>();
        private static int _passed = 0;
        private static int _failed = 0;

        public static void RunAllTests()
        {
            _testResults.Clear();
            _passed = 0;
            _failed = 0;

            TestQuestFactoryLoad();
            TestQuestStateMachine();
            TestQuestTaskCompletion();
            TestDialogueFactoryLoad();
            TestDialogueNodeChain();
            TestNarrativeDataTask();
            TestSaveLoadRoundTrip();

            NarrativeLog.Log($"=== NarrativePro Tests: {_passed} passed, {_failed} failed ===");
            foreach (var result in _testResults)
            {
                NarrativeLog.Log(result);
            }
        }

        private static void Assert(bool condition, string testName, string message = "")
        {
            if (condition)
            {
                _passed++;
                _testResults.Add($"[PASS] {testName}");
            }
            else
            {
                _failed++;
                _testResults.Add($"[FAIL] {testName}: {message}");
            }
        }

        private static void TestQuestFactoryLoad()
        {
            try
            {
                string tempFile = Path.Combine(Path.GetTempPath(), "TestQuest.json");
                string json = @"{
                    ""questId"": ""TestQuest"",
                    ""questName"": ""测试任务"",
                    ""questDescription"": ""这是一个测试任务"",
                    ""questDialogueClass"": """",
                    ""states"": [
                        { ""id"": ""Start"", ""description"": ""开始"", ""stateType"": 0, ""position"": { ""x"": 0, ""y"": 0 } },
                        { ""id"": ""Done"", ""description"": ""完成"", ""stateType"": 1, ""position"": { ""x"": 300, ""y"": 0 } }
                    ],
                    ""branches"": [
                        { ""id"": ""B1"", ""description"": ""分支1"", ""fromStateId"": ""Start"", ""toStateId"": ""Done"", ""tasks"": [{ ""type"": ""KillNPC"", ""targetId"": ""Enemy"", ""requiredQuantity"": 1, ""description"": ""击败敌人"" }], ""hidden"": false }
                    ],
                    ""startStateId"": ""Start""
                }";
                File.WriteAllText(tempFile, json);

                var quest = QuestFactory.LoadQuest(tempFile);
                Assert(quest != null, "QuestFactory.LoadQuest returns non-null");
                Assert(quest.QuestName == "测试任务", "Quest name matches");
                Assert(quest.States.Count == 2, "Quest has 2 states");
                Assert(quest.Branches.Count == 1, "Quest has 1 branch");
                Assert(quest.QuestStartState != null, "Quest has start state");
                Assert(quest.QuestStartState.ID == "Start", "Start state ID matches");

                File.Delete(tempFile);
            }
            catch (Exception ex)
            {
                Assert(false, "QuestFactory.LoadQuest", ex.Message);
            }
        }

        private static void TestQuestStateMachine()
        {
            try
            {
                var startState = new QuestState { ID = "Start", StateNodeType = EStateNodeType.Regular };
                var successState = new QuestState { ID = "Success", StateNodeType = EStateNodeType.Success };
                var branch = new QuestBranch { ID = "B1", DestinationState = successState };
                branch.QuestTasks.Add(new GenericTask { TaskTypeId = "Test", RequiredQuantity = 1 });
                startState.Branches.Add(branch);

                var quest = new Quest();
                quest.QuestName = "TestQuest";
                quest.States.Add(startState);
                quest.States.Add(successState);
                quest.Branches.Add(branch);
                quest.QuestStartState = startState;

                quest.Initialize(null, "");
                quest.BeginQuest();

                Assert(quest.QuestCompletion == EQuestCompletion.Started, "Quest is started after BeginQuest");
                Assert(quest.CurrentState == startState, "Quest current state is Start");

                File.Delete(Path.GetTempPath() + "TestQuest.json");
            }
            catch (Exception ex)
            {
                Assert(false, "QuestStateMachine", ex.Message);
            }
        }

        private static void TestQuestTaskCompletion()
        {
            try
            {
                var startState = new QuestState { ID = "Start", StateNodeType = EStateNodeType.Regular };
                var successState = new QuestState { ID = "Success", StateNodeType = EStateNodeType.Success };
                var task = new GenericTask { TaskTypeId = "KillNPC", TargetId = "Guard", RequiredQuantity = 3 };
                var branch = new QuestBranch { ID = "B1", DestinationState = successState };
                branch.QuestTasks.Add(task);
                startState.Branches.Add(branch);

                var quest = new Quest();
                quest.QuestName = "TaskTest";
                quest.States.Add(startState);
                quest.States.Add(successState);
                quest.Branches.Add(branch);
                quest.QuestStartState = startState;

                quest.Initialize(null, "");
                quest.BeginQuest();

                task.AddProgress(1);
                Assert(task.CurrentProgress == 1, "Task progress is 1 after AddProgress(1)");
                Assert(!task.IsComplete(), "Task is not complete at 1/3");

                task.AddProgress(2);
                Assert(task.CurrentProgress == 3, "Task progress is 3 after AddProgress(2)");
                Assert(task.IsComplete(), "Task is complete at 3/3");
            }
            catch (Exception ex)
            {
                Assert(false, "QuestTaskCompletion", ex.Message);
            }
        }

        private static void TestDialogueFactoryLoad()
        {
            try
            {
                string tempFile = Path.Combine(Path.GetTempPath(), "TestDialogue.json");
                string json = @"{
                    ""dialogueId"": ""TestDialogue"",
                    ""speakers"": [
                        { ""speakerId"": ""NPC"", ""displayName"": ""NPC"", ""tags"": [], ""isPlayer"": false },
                        { ""speakerId"": ""Player"", ""displayName"": ""Player"", ""tags"": [], ""isPlayer"": true }
                    ],
                    ""config"": { ""endDialogueDist"": 500, ""showCinematicBars"": false, ""unskippable"": false, ""freeMovement"": true, ""canBeExited"": true, ""priority"": 0 },
                    ""npcReplies"": [
                        { ""id"": ""Root"", ""speakerId"": ""NPC"", ""isRoot"": true, ""isSkippable"": true, ""line"": { ""text"": ""Hello!"", ""duration"": 3 }, ""npcReplies"": [], ""playerReplies"": [""Reply1""] }
                    ],
                    ""playerReplies"": [
                        { ""id"": ""Reply1"", ""optionText"": ""Hi"", ""hintText"": """", ""autoSelect"": false, ""autoSelectIfOnlyReply"": true, ""line"": { ""text"": ""Hi there!"", ""duration"": 3 }, ""npcReplies"": [], ""playerReplies"": [] }
                    ]
                }";
                File.WriteAllText(tempFile, json);

                var dialogue = DialogueFactory.LoadDialogue(tempFile);
                Assert(dialogue != null, "DialogueFactory.LoadDialogue returns non-null");
                Assert(dialogue.DialogueId == "TestDialogue", "Dialogue ID matches");
                Assert(dialogue.RootDialogue != null, "Dialogue has root node");
                Assert(dialogue.RootDialogue.SpeakerID == "NPC", "Root speaker is NPC");
                Assert(dialogue.PlayerReplies.Count == 1, "Dialogue has 1 player reply");
                Assert(dialogue.PlayerSpeakerInfo != null, "Dialogue has player speaker info");

                File.Delete(tempFile);
            }
            catch (Exception ex)
            {
                Assert(false, "DialogueFactory.LoadDialogue", ex.Message);
            }
        }

        private static void TestDialogueNodeChain()
        {
            try
            {
                var npc1 = new DialogueNode_NPC { ID = "NPC1", SpeakerID = "Elder" };
                var npc2 = new DialogueNode_NPC { ID = "NPC2", SpeakerID = "Elder" };
                var npc3 = new DialogueNode_NPC { ID = "NPC3", SpeakerID = "Elder" };

                npc1.NPCReplies.Add(npc2);
                npc2.NPCReplies.Add(npc3);

                var chain = npc1.GetReplyChain();
                Assert(chain.Count == 3, "NPC reply chain has 3 nodes");
                Assert(chain[0].ID == "NPC1", "Chain[0] is NPC1");
                Assert(chain[2].ID == "NPC3", "Chain[2] is NPC3");
            }
            catch (Exception ex)
            {
                Assert(false, "DialogueNodeChain", ex.Message);
            }
        }

        private static void TestNarrativeDataTask()
        {
            try
            {
                var task = new NarrativeDataTask { TaskName = "KillNPC", ArgumentName = "Boss" };
                string taskString = task.MakeTaskString("Boss");
                Assert(taskString == "killnpc_boss", $"DataTask string is 'killnpc_boss', got '{taskString}'");

                var task2 = new NarrativeDataTask { TaskName = "CollectItem", ArgumentName = "Relic" };
                string taskString2 = task2.MakeTaskString("AncientRelic");
                Assert(taskString2 == "collectitem_ancientrelic", "DataTask string for CollectItem is correct");
            }
            catch (Exception ex)
            {
                Assert(false, "NarrativeDataTask", ex.Message);
            }
        }

        private static void TestSaveLoadRoundTrip()
        {
            try
            {
                var saveData = new NarrativeSaveData();
                var savedQuest = new SavedQuest
                {
                    QuestClassId = "TestQuest",
                    QuestCompletion = EQuestCompletion.Started,
                    CurrentStateId = "Start",
                    bTracked = true
                };
                savedQuest.ReachedStateIds.Add("Start");
                savedQuest.Branches.Add(new SavedQuestBranch
                {
                    BranchId = "B1",
                    TasksProgress = new List<int> { 2 }
                });
                saveData.SavedQuests.Add(savedQuest);
                saveData.MasterTaskList["killnpc_guard"] = 3;

                var saveManager = new NarrativeSaveManager();
                string json = saveManager.SerializeToJson(saveData);
                Assert(!string.IsNullOrEmpty(json), "Save data serialized to non-empty JSON");

                var loaded = saveManager.DeserializeFromJson(json);
                Assert(loaded != null, "Deserialized save data is not null");
                Assert(loaded.SavedQuests.Count == 1, "Loaded 1 saved quest");
                Assert(loaded.SavedQuests[0].QuestClassId == "TestQuest", "Loaded quest class ID matches");
                Assert(loaded.MasterTaskList["killnpc_guard"] == 3, "Loaded master task list entry matches");
            }
            catch (Exception ex)
            {
                Assert(false, "SaveLoadRoundTrip", ex.Message);
            }
        }
    }
}
