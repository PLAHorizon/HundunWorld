using System.Collections.Generic;
using NpcEditor;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 插件草稿模型的合并语义：刷点/路线/配方**按条合并**，不许整表替换。
/// <para>
/// 症状：一个 NPC 可以有多个刷点/路线/配方，面板只显示第一条；保存时把整份列表换成"界面上那一条"，
/// 另外几条就静默消失（保存成功、库里少行）。玩家看到的是"另一个营地的人没了"，而编辑器里从没见过那一条。
/// </para>
/// <para>
/// 这份模型由 <c>Horizon.Game.Gateway.Tests.csproj</c> 直接链接插件源码编译（纯 C#，不含 Flax 类型）。
/// </para>
/// </summary>
public class NpcDraftModelMergeTests
{
    [Fact]
    public void ReplaceSpawn_KeepsOtherSpawns()
    {
        var draft = new NpcDraftModel();
        draft.SpawnPoints.Add(new NpcSpawnPointModel { SpawnPointId = 1, X = 1f, NpcId = 400001 });
        draft.SpawnPoints.Add(new NpcSpawnPointModel { SpawnPointId = 2, X = 50f, NpcId = 400001 });

        draft.ReplaceSpawn(0, new NpcSpawnPointModel { SpawnPointId = 1, X = 2f, NpcId = 400001 });

        Assert.Equal(2, draft.SpawnPoints.Count);
        Assert.Equal(2f, draft.SpawnPoints[0].X);
        // 没显示的那一条必须原样还在（id 与坐标都不变）。
        Assert.Equal(2, draft.SpawnPoints[1].SpawnPointId);
        Assert.Equal(50f, draft.SpawnPoints[1].X);
    }

    [Fact]
    public void ReplaceSpawn_OutOfRange_Appends()
    {
        var draft = new NpcDraftModel();
        draft.SpawnPoints.Add(new NpcSpawnPointModel { SpawnPointId = 1 });

        draft.ReplaceSpawn(-1, new NpcSpawnPointModel { SpawnPointId = 3 });
        Assert.Equal(2, draft.SpawnPoints.Count);
        Assert.Equal(3, draft.SpawnPoints[1].SpawnPointId);

        // 越界的高索引同样只能追加，不能覆盖出界（也不能丢）。
        draft.ReplaceSpawn(99, new NpcSpawnPointModel { SpawnPointId = 4 });
        Assert.Equal(3, draft.SpawnPoints.Count);
    }

    [Fact]
    public void UpsertRoute_ReplacesByPathId_AndKeepsOthers()
    {
        var draft = new NpcDraftModel();
        draft.Routes.Add(new NpcRouteModel
        {
            PathId = 7, Name = "白天", Points = { new NpcRoutePointModel { Seq = 0, X = 0f } },
        });
        draft.Routes.Add(new NpcRouteModel
        {
            PathId = 8, Name = "夜里", Points = { new NpcRoutePointModel { Seq = 0, X = 100f } },
        });

        draft.UpsertRoute(new NpcRouteModel
        {
            PathId = 7, Name = "白天改", Points = { new NpcRoutePointModel { Seq = 0, X = 5f } },
        });

        Assert.Equal(2, draft.Routes.Count);
        Assert.Equal("白天改", draft.Routes[0].Name);
        Assert.Equal(5f, draft.Routes[0].Points[0].X);
        Assert.Equal(8, draft.Routes[1].PathId);
        Assert.Equal(100f, draft.Routes[1].Points[0].X);

        // 0 = 新建（CLI 保存时分配号）：只能追加。
        draft.UpsertRoute(new NpcRouteModel { PathId = 0, Name = "新线" });
        Assert.Equal(3, draft.Routes.Count);
        Assert.Equal("新线", draft.Routes[2].Name);
    }

    [Fact]
    public void UpsertRecipe_ReplacesByRecipeId_AndKeepsOthers()
    {
        var draft = new NpcDraftModel();
        draft.Recipes.Add(new NpcRecipeModel { RecipeId = 900001, Name = "旧的" });
        draft.Recipes.Add(new NpcRecipeModel { RecipeId = 900002, Name = "另一张" });

        draft.UpsertRecipe(new NpcRecipeModel { RecipeId = 900001, Name = "新的" });

        Assert.Equal(2, draft.Recipes.Count);
        Assert.Equal("新的", draft.Recipes[0].Name);
        Assert.Equal("另一张", draft.Recipes[1].Name);

        draft.UpsertRecipe(new NpcRecipeModel { RecipeId = 0, Name = "未编号" });
        Assert.Equal(3, draft.Recipes.Count);
        Assert.Equal("未编号", draft.Recipes[2].Name);
    }

    [Fact]
    public void MergeHelpers_TolerateNullContainers()
    {
        // 老草稿/手工 JSON 可能缺这些栏；合并入口必须自己补齐，不能 NRE 在保存路径上。
        var draft = new NpcDraftModel { SpawnPoints = null, Routes = null, Recipes = null };

        draft.ReplaceSpawn(-1, new NpcSpawnPointModel { SpawnPointId = 9 });
        draft.UpsertRoute(new NpcRouteModel { PathId = 0 });
        draft.UpsertRecipe(new NpcRecipeModel { RecipeId = 0 });

        Assert.Single(draft.SpawnPoints!);
        Assert.Single(draft.Routes!);
        Assert.Single(draft.Recipes!);
    }

    /// <summary>
    /// 逐件掉落条件的下拉映射：值域与库列同号，**默认必须是"继承（-1）"**。
    /// <para>
    /// 默认写成"无条件(0)"的话，没配过的行会集体变成人人可拿——与实体列默认值 -1 是同一条纪律；
    /// 越界索引/未知库值也必须回继承，不许猜。
    /// </para>
    /// </summary>
    [Fact]
    public void LootConditionUi_MapsBothWays_AndDefaultsToInherit()
    {
        Assert.Equal(-1, NpcLootConditionUi.ValueAt(0));
        Assert.Equal(0, NpcLootConditionUi.ValueAt(1));
        Assert.Equal(1, NpcLootConditionUi.ValueAt(2));
        Assert.Equal(2, NpcLootConditionUi.ValueAt(3));
        Assert.Equal(3, NpcLootConditionUi.ValueAt(4));
        Assert.Equal(-1, NpcLootConditionUi.ValueAt(99));
        Assert.Equal(-1, NpcLootConditionUi.ValueAt(-1));

        // 两个方向互为逆：四个库值 + 继承都要能往返。
        foreach (var value in new[] { -1, 0, 1, 2, 3 })
        {
            Assert.Equal(value, NpcLootConditionUi.ValueAt(NpcLootConditionUi.IndexOf(value)));
        }

        Assert.Equal(0, NpcLootConditionUi.IndexOf(7));       // 未知库值 → 继承
        Assert.Equal(5, NpcLootConditionUi.Names.Length);
        Assert.Contains("继承", NpcLootConditionUi.Names[0]);
    }

    /// <summary>
    /// 绑定级下拉：没有"继承"档，索引即库值；越界回 0（无条件）。
    /// 写成"继承"档会让绑定层显示一个它自己无法满足的值（绑定就是继承链的终点）。
    /// </summary>
    [Fact]
    public void LootConditionUi_BindingDropdownHasNoInheritSlot()
    {
        Assert.Equal(4, NpcLootConditionUi.BindingNames.Length);
        Assert.DoesNotContain("继承", NpcLootConditionUi.BindingNames[0]);

        for (var value = 0; value <= 3; value++)
        {
            Assert.Equal(value, NpcLootConditionUi.BindingValueAt(value));
            Assert.Equal(value, NpcLootConditionUi.BindingIndexOf(value));
        }

        Assert.Equal(0, NpcLootConditionUi.BindingValueAt(-1));
        Assert.Equal(0, NpcLootConditionUi.BindingValueAt(9));
        Assert.Equal(0, NpcLootConditionUi.BindingIndexOf(-5));
        Assert.Equal(0, NpcLootConditionUi.BindingIndexOf(9));
    }

    /// <summary>
    /// 行选择索引：**count 是合法值**（"新增一行"那一档），负数回 0，超界夹回 count。
    /// 钳到 count-1 会让"新建"从界面上消失；不夹会让"上一/下一"把已经加载的行挤没。
    /// </summary>
    [Fact]
    public void RowIndex_KeepsTheNewRowSlotReachable()
    {
        Assert.Equal(0, NpcRowIndex.Clamp(-5, 3));
        Assert.Equal(0, NpcRowIndex.Clamp(0, 3));
        Assert.Equal(2, NpcRowIndex.Clamp(2, 3));
        Assert.Equal(3, NpcRowIndex.Clamp(3, 3));     // 尾行 = 新增
        Assert.Equal(3, NpcRowIndex.Clamp(99, 3));
        Assert.Equal(0, NpcRowIndex.Clamp(0, 0));     // 一行都没有时只剩"新增"档

        // 上/下一：两端都停得住，且能停在"新增"档。
        Assert.Equal(0, NpcRowIndex.Step(0, -1, 2));
        Assert.Equal(2, NpcRowIndex.Step(1, 1, 2));
        Assert.Equal(1, NpcRowIndex.Step(2, -1, 2));
        Assert.Equal(0, NpcRowIndex.Step(-1, -1, 2));
    }

    /// <summary>
    /// 行走/奔跑速度必须是草稿 JSON 里的一等字段（编辑器输入框 ↔ CLI 草稿共用 camelCase）。
    /// 以前模型有这两格但面板没有输入框，等于"只能改库"；跑速在运行期更是零读者（现在逃跑用它）。
    /// </summary>
    [Fact]
    public void SpeedFields_RoundTripThroughDraftJson()
    {
        var draft = new NpcDraftModel
        {
            Template = new NpcTemplateModel { NpcId = 400001, SpeedWalk = 2.25f, SpeedRun = 6.5f },
        };

        var json = draft.ToJson();
        Assert.Contains("\"speedWalk\":2.25", json, System.StringComparison.Ordinal);
        Assert.Contains("\"speedRun\":6.5", json, System.StringComparison.Ordinal);

        var back = NpcDraftModel.FromJson(json);
        Assert.NotNull(back);
        Assert.Equal(2.25f, back!.Template.SpeedWalk);
        Assert.Equal(6.5f, back.Template.SpeedRun);
    }

    /// <summary>
    /// 删除刷点：只动被点中的那一行、其余原序保留；越界不动数据；删空是合法状态（NPC 还没摆进世界）。
    /// <para>路线/配方**没有**删除入口——它们跨 NPC 共享，删一条会牵动引用它的其它 NPC，属另一套退役流程。</para>
    /// </summary>
    [Fact]
    public void RemoveSpawn_OnlyRemovesThePointedRow()
    {
        var draft = new NpcDraftModel();
        draft.SpawnPoints.Add(new NpcSpawnPointModel { SpawnPointId = 1, X = 1f });
        draft.SpawnPoints.Add(new NpcSpawnPointModel { SpawnPointId = 2, X = 2f });
        draft.SpawnPoints.Add(new NpcSpawnPointModel { SpawnPointId = 3, X = 3f });

        Assert.True(draft.RemoveSpawn(1));
        Assert.Equal(2, draft.SpawnPoints.Count);
        Assert.Equal(1, draft.SpawnPoints[0].SpawnPointId);
        Assert.Equal(3, draft.SpawnPoints[1].SpawnPointId);

        // 越界（含"新增档"= count）不动数据。
        Assert.False(draft.RemoveSpawn(2));
        Assert.False(draft.RemoveSpawn(-1));
        Assert.Equal(2, draft.SpawnPoints.Count);

        Assert.True(draft.RemoveSpawn(0));
        Assert.True(draft.RemoveSpawn(0));
        Assert.Empty(draft.SpawnPoints);
        Assert.False(draft.RemoveSpawn(0));
    }

    /// <summary>技能挂载字段必须随草稿 JSON 往返（与 CLI 的 NpcSkillMountDto 同名 camelCase）；未显示字段也要保留。</summary>
    [Fact]
    public void SkillMountFields_RoundTripThroughDraftJson()
    {
        var draft = new NpcDraftModel
        {
            Template = new NpcTemplateModel { NpcId = 500010 },
            SkillMounts =
            {
                new NpcSkillMountModel
                {
                    NpcId = 500010, SkillId = 1001, SlotSeq = 0, SlotType = 0, ChanceBps = 10000,
                    CastMs = 1000, CooldownMs = 5000, MinRange = 3f, MaxRange = 20f,
                    TargetSelect = 4, TargetCount = 3, AoeRadius = 8f, SchoolMask = 2,
                    Interruptible = 0, AllowMovement = 1, DamageScaleBps = 12500,
                    StatusId = 7, StatusDurationSeconds = 2.5f, StartEmote = 9, EmergencyHpPercent = 40,
                },
            },
        };

        var json = draft.ToJson();
        Assert.Contains("\"skillId\":1001", json, System.StringComparison.Ordinal);
        Assert.Contains("\"damageScaleBps\":12500", json, System.StringComparison.Ordinal);

        var back = NpcDraftModel.FromJson(json);
        Assert.NotNull(back);
        var m = Assert.Single(back!.SkillMounts);
        Assert.Equal(1001, m.SkillId);
        Assert.Equal(1000, m.CastMs);
        Assert.Equal(20f, m.MaxRange);
        Assert.Equal(4, m.TargetSelect);
        Assert.Equal(3, m.TargetCount);
        Assert.Equal(2, m.SchoolMask);
        Assert.Equal(0, m.Interruptible);
        Assert.Equal(1, m.AllowMovement);
        Assert.Equal(12500, m.DamageScaleBps);
        Assert.Equal(7, m.StatusId);
        Assert.Equal(2.5f, m.StatusDurationSeconds);
    }

    private static List<NpcSkillMountModel> FiveMounts()
    {
        var rows = new List<NpcSkillMountModel>();
        for (var i = 0; i < 5; i++)
        {
            rows.Add(new NpcSkillMountModel
            {
                NpcId = 500010, SkillId = 1001 + i, SlotSeq = i,
                DamageScaleBps = 10000 + i, StatusId = 100 + i,
            });
        }

        return rows;
    }

    [Fact]
    public void MergeSkillMounts_KeepsRowsBeyondThePanel()
    {
        // 面板只有 4 行，库里 5 条：保存是整组替换，第 5 条必须原样带回。
        var existing = FiveMounts();
        var visible = new List<NpcSkillMountModel> { existing[0], existing[1], existing[2], existing[3] };

        var merged = NpcSkillMountEditing.MergeSkillMounts(existing, visible, 4);

        Assert.Equal(5, merged.Count);
        Assert.Equal(new[] { 1001, 1002, 1003, 1004, 1005 }, merged.ConvertAll(m => m.SkillId).ToArray());
        var tail = merged[4];
        Assert.Equal(4, tail.SlotSeq);          // 排在可见行之后，不与它们撞号
        Assert.Equal(10004, tail.DamageScaleBps); // 面板没显示的列不被重置
        Assert.Equal(104, tail.StatusId);
        for (var i = 0; i < 4; i++) Assert.Equal(i, merged[i].SlotSeq);
    }

    [Fact]
    public void MergeSkillMounts_ClearingAVisibleRowReallyDeletesIt()
    {
        // 正对照：可见区里清空技能列 = 作者要删，不能以"隐藏行"名义复活；
        // 同时面板之外的幸存行仍要带回（两个方向各断言一次，否则"全都带回"的实现也会绿）。
        var existing = FiveMounts();
        var visible = new List<NpcSkillMountModel> { existing[0], existing[2], existing[3] };

        var merged = NpcSkillMountEditing.MergeSkillMounts(existing, visible, 4);

        Assert.Equal(4, merged.Count);
        Assert.DoesNotContain(merged, m => m.SkillId == 1002);
        Assert.Contains(merged, m => m.SkillId == 1005);
        Assert.Equal(4, merged.Find(m => m.SkillId == 1005).SlotSeq);
    }

    [Fact]
    public void MergeSkillMounts_NewNpcKeepsOnlyVisibleRows()
    {
        var visible = new List<NpcSkillMountModel>
        {
            new NpcSkillMountModel { NpcId = 500011, SkillId = 2001, SlotSeq = 0 },
        };

        var merged = NpcSkillMountEditing.MergeSkillMounts(null, visible, 4);

        var only = Assert.Single(merged);
        Assert.Equal(2001, only.SkillId);
    }

    private static NpcLootBindingModel LootBindingOf(int firstItemId, int count)
    {
        var binding = new NpcLootBindingModel { MonsterTemplateId = 500010, LootTableId = 1 };
        for (var i = 0; i < count; i++)
        {
            binding.Entries.Add(new NpcLootEntryModel
            {
                ItemId = firstItemId + i, Seq = i, ChanceBps = 1000 + i,
                GroupId = 2, ConditionKind = -1, QuestId = 700 + i,
            });
        }

        return binding;
    }

    [Fact]
    public void AppendHiddenLootEntries_KeepsItemsBeyondThePanel()
    {
        var previous = LootBindingOf(700001, 5);
        var edited = new NpcLootBindingModel { MonsterTemplateId = 500010, LootTableId = 1 };
        for (var i = 0; i < 4; i++) edited.Entries.Add(previous.Entries[i]);

        NpcLootEditing.AppendHiddenEntries(edited, previous, 4);

        Assert.Equal(5, edited.Entries.Count);
        var tail = edited.Entries[4];
        Assert.Equal(700005, tail.ItemId);
        Assert.Equal(4, tail.Seq);          // 排在可见件之后
        Assert.Equal(2, tail.GroupId);      // 面板没有的列保持原值
        Assert.Equal(704, tail.QuestId);
        Assert.Equal(1004, tail.ChanceBps);
    }

    [Fact]
    public void AppendHiddenLootEntries_ClearingAVisibleRowReallyDeletesIt()
    {
        // 正对照：可见区清空物品列=作者要删；面板外的第 5 件仍要带回。
        var previous = LootBindingOf(700001, 5);
        var edited = new NpcLootBindingModel { MonsterTemplateId = 500010, LootTableId = 1 };
        edited.Entries.Add(previous.Entries[0]);
        edited.Entries.Add(previous.Entries[2]);
        edited.Entries.Add(previous.Entries[3]);

        NpcLootEditing.AppendHiddenEntries(edited, previous, 4);

        Assert.Equal(4, edited.Entries.Count);
        Assert.DoesNotContain(edited.Entries, e => e.ItemId == 700002);
        Assert.Contains(edited.Entries, e => e.ItemId == 700005);
    }

    [Fact]
    public void MergeLootBindings_KeepsBindingsBeyondTheFirst()
    {
        var edited = LootBindingOf(700001, 1);
        var second = LootBindingOf(700101, 2);
        second.LootTableId = 7;
        var previous = new List<NpcLootBindingModel> { LootBindingOf(700001, 1), second };

        var merged = NpcLootEditing.MergeBindings(edited, previous);

        Assert.Equal(2, merged.Count);
        Assert.Same(edited, merged[0]);
        Assert.Same(second, merged[1]);
        Assert.Equal(7, merged[1].LootTableId);

        // 第 0 份被清空（无件也没金币）时整份消失，但其余绑定不受牵连。
        var empty = new NpcLootBindingModel { MonsterTemplateId = 500010, LootTableId = 1 };
        var onlyTail = NpcLootEditing.MergeBindings(empty, previous);
        var survivor = Assert.Single(onlyTail);
        Assert.Same(second, survivor);
    }

    [Fact]
    public void AppendHiddenRows_KeepsVendorsBeyondThePanel_WithoutRenumberingSlots()
    {
        // 随包商队的货架就是 5 行，面板只有 4 行：保存不能把第 5 件删掉，也不能改它的槽位。
        var existing = new List<NpcVendorRowModel>();
        for (var i = 0; i < 5; i++)
        {
            existing.Add(new NpcVendorRowModel
            {
                NpcId = 400001, ItemId = 800001 + i, Slot = 10 + i, StockMax = 5 + i,
            });
        }

        var edited = new List<NpcVendorRowModel> { existing[0], existing[1], existing[2], existing[3] };

        NpcRowListEditing.AppendHidden(edited, existing, 4, r => r.ItemId, null);

        Assert.Equal(5, edited.Count);
        Assert.Equal(800005, edited[4].ItemId);
        Assert.Equal(14, edited[4].Slot);   // 槽位是稳定键，不顺延
        Assert.Equal(9, edited[4].StockMax);
    }

    [Fact]
    public void AppendHiddenRows_GossipKeepsOtherGroups()
    {
        // 随包台词 7 行（组 1 四条 + 组 2 三条），面板只露 4 行。
        var existing = new List<NpcGossipRowModel>();
        for (var i = 0; i < 4; i++)
            existing.Add(new NpcGossipRowModel { NpcId = 400001, GroupId = 1, Seq = i, ActionKind = 0 });
        for (var i = 0; i < 3; i++)
            existing.Add(new NpcGossipRowModel { NpcId = 400001, GroupId = 2, Seq = i, ActionKind = 1 });

        var edited = new List<NpcGossipRowModel> { existing[0], existing[1], existing[2], existing[3] };

        NpcRowListEditing.AppendHidden(edited, existing, 4,
            r => ((long)r.GroupId * 1000000L) + r.Seq, (r, seq) => r.Seq = seq);

        Assert.Equal(7, edited.Count);
        Assert.Equal(3, edited.FindAll(r => r.GroupId == 2).Count);
        Assert.Equal(new[] { 4, 5, 6 }, edited.GetRange(4, 3).ConvertAll(r => r.Seq).ToArray());
    }

    [Fact]
    public void AppendHiddenRows_TrainerClearingAVisibleRowStillDeletesIt()
    {
        var existing = new List<NpcTrainerRowModel>();
        for (var i = 0; i < 5; i++)
            existing.Add(new NpcTrainerRowModel { NpcId = 400002, SkillId = 9001 + i, Seq = i });

        // 第 2 条（9002）在可见区被清空 = 作者要删。
        var edited = new List<NpcTrainerRowModel> { existing[0], existing[2], existing[3] };

        NpcRowListEditing.AppendHidden(edited, existing, 4, r => r.SkillId, (r, seq) => r.Seq = seq);

        Assert.Equal(4, edited.Count);
        Assert.DoesNotContain(edited, r => r.SkillId == 9002);
        Assert.Equal(4, edited.Find(r => r.SkillId == 9005).Seq);
    }

    private static NpcRouteModel Route(int pathId, string name, int points)
    {
        var route = new NpcRouteModel { PathId = pathId, Name = name };
        for (var i = 0; i < points; i++)
            route.Points.Add(new NpcRoutePointModel { Seq = i, X = i * 3f, Y = 0f, Z = 0f });
        return route;
    }

    [Fact]
    public void UpsertRouteAt_NewRouteWithoutPathIdDoesNotDuplicateOnEveryCommit()
    {
        // 新建路线落库前 PathId 还是 0：保存/预览/校验都会提交一次草稿，
        // 旧走法只认 PathId，于是每点一次就多一条一模一样的巡街线。
        var draft = new NpcDraftModel();

        draft.UpsertRouteAt(0, Route(0, "新线", 2));
        draft.UpsertRouteAt(0, Route(0, "新线", 2));

        var only = Assert.Single(draft.Routes!);
        Assert.Equal("新线", only.Name);
    }

    [Fact]
    public void UpsertRouteAt_EditsTheSelectedRowOnly()
    {
        var draft = new NpcDraftModel();
        draft.UpsertRouteAt(0, Route(11, "东街", 2));
        draft.UpsertRouteAt(1, Route(12, "西街", 2));

        draft.UpsertRouteAt(1, Route(0, "西街改", 3));

        Assert.Equal(2, draft.Routes!.Count);
        Assert.Equal("东街", draft.Routes[0].Name);
        Assert.Equal("西街改", draft.Routes[1].Name);
        Assert.Equal(3, draft.Routes[1].Points.Count);
    }

    [Fact]
    public void UpsertRecipeAt_NewRecipeWithoutIdDoesNotDuplicateOnEveryCommit()
    {
        var draft = new NpcDraftModel();
        var recipe = new NpcRecipeModel { RecipeId = 0, ProductItemId = 700001, Name = "铁剑" };

        draft.UpsertRecipeAt(0, recipe);
        draft.UpsertRecipeAt(0, new NpcRecipeModel { RecipeId = 0, ProductItemId = 700001, Name = "铁剑" });

        Assert.Single(draft.Recipes!);
    }

    [Fact]
    public void AppendHiddenRoutePoints_KeepsPointsBeyondThePanel()
    {
        // 面板 6 个点；8 点的线保存一次不能变 6 点。
        var previous = Route(11, "长线", 8);
        var edited = new List<NpcRoutePointModel>();
        for (var i = 0; i < 6; i++) edited.Add(previous.Points[i]);

        NpcRowListEditing.AppendHidden(edited, previous.Points, 6, p => p.Seq, null);

        Assert.Equal(8, edited.Count);
        Assert.Equal(6, edited[6].Seq);
        Assert.Equal(21f, edited[7].X);   // 夹具 X = 序号×3：第 7 点 = 21，不是 18
    }

    [Fact]
    public void AppendHiddenMaterials_KeepsMaterialsBeyondThePanel()
    {
        // 面板 3 格材料；4 种材料的配方保存一次不能少一种。
        var previous = new NpcRecipeModel { RecipeId = 5, ProductItemId = 700001 };
        for (var i = 0; i < 4; i++) previous.Materials.Add(new NpcMaterialModel { ItemId = 600001 + i, Count = 2 + i });

        var edited = new List<NpcMaterialModel>();
        for (var i = 0; i < 3; i++) edited.Add(previous.Materials[i]);

        NpcRowListEditing.AppendHidden(edited, previous.Materials, 3, m => m.ItemId, null);

        Assert.Equal(4, edited.Count);
        Assert.Equal(600004, edited[3].ItemId);
        Assert.Equal(5, edited[3].Count);
    }

    [Fact]
    public void MergeSkillMounts_KeepsTwoSlotsWithTheSameSkill()
    {
        // 同一技能挂"战斗"和"残血"两个时机是合法配置：按技能号去重会把第二条吃掉。
        var existing = new List<NpcSkillMountModel>
        {
            new NpcSkillMountModel { NpcId = 500010, SkillId = 1001, SlotSeq = 0, SlotType = 0 },
        };
        var visible = new List<NpcSkillMountModel>
        {
            new NpcSkillMountModel { NpcId = 500010, SkillId = 1001, SlotSeq = 0, SlotType = 0 },
            new NpcSkillMountModel { NpcId = 500010, SkillId = 1001, SlotSeq = 1, SlotType = 2 },
        };

        var merged = NpcSkillMountEditing.MergeSkillMounts(existing, visible, 4);

        Assert.Equal(2, merged.Count);
        Assert.Equal(2, merged[1].SlotType);
    }

    [Fact]
    public void AppendHiddenLootEntries_KeepsSameItemInDifferentGroups()
    {
        // 同一物品在两个掉落组各掉一次（同组互斥时常见）：按物品号去重会吃掉第二条。
        var previous = LootBindingOf(700001, 4);
        previous.Entries.Add(new NpcLootEntryModel { ItemId = 700001, Seq = 4, GroupId = 3, ChanceBps = 500 });
        previous.Entries.Add(new NpcLootEntryModel { ItemId = 700001, Seq = 5, GroupId = 4, ChanceBps = 500 });

        var edited = new NpcLootBindingModel { MonsterTemplateId = 500010, LootTableId = 1 };
        for (var i = 0; i < 4; i++) edited.Entries.Add(previous.Entries[i]);

        NpcLootEditing.AppendHiddenEntries(edited, previous, 4);

        Assert.Equal(6, edited.Entries.Count);
        Assert.Equal(2, edited.Entries.FindAll(e => e.ItemId == 700001 && e.Seq >= 4).Count);
        Assert.Contains(edited.Entries, e => e.GroupId == 4);
    }

    [Fact]
    public void AppendHiddenRows_KeepsDuplicateTrainerSkillWithDifferentServiceKind()
    {
        // 同一技能既能"教"又能"卖书"（ServiceKind 不同）：两条都要活。
        var existing = new List<NpcTrainerRowModel>();
        for (var i = 0; i < 4; i++)
            existing.Add(new NpcTrainerRowModel { NpcId = 400002, SkillId = 9001 + i, Seq = i });
        existing.Add(new NpcTrainerRowModel { NpcId = 400002, SkillId = 9004, ServiceKind = 2, Seq = 4 });

        var edited = new List<NpcTrainerRowModel>();
        for (var i = 0; i < 4; i++) edited.Add(existing[i]);

        NpcRowListEditing.AppendHidden(edited, existing, 4, r => r.SkillId, (r, seq) => r.Seq = seq);

        Assert.Equal(5, edited.Count);
        Assert.Equal(2, edited.FindAll(r => r.SkillId == 9004).Count);
        Assert.Equal(4, edited[4].Seq);
    }
}
