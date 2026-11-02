using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Core.Sim.Server;
using Orleans.TestingHost;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 条件掉落的"配不配拿"判据走**真 Orleans 运行时**（TestCluster）：任务状态在 Grain 里、读取端是生产那个适配器。
/// <para>
/// 已有的用例都是替身：<c>FakeQuestStateReader</c> 直接返回 Held/NotHeld，证明的是"拿到了答案之后怎么处理"，
/// 证明不了 <see cref="OrleansQuestStateReader"/> 与真 <c>QuestGrain</c> 的口径对得上——
/// 比如"进行中"到底是 <c>Status==0</c> 还是 1、键是不是同一个玩家、已完成的任务还算不算持有。
/// 这几格错任何一处，表现都是"接了任务也拿不到"或"任务做完了还能一直刷"，
/// 而替身用例全绿。
/// </para>
/// </summary>
public class ConditionalLootClusterTests : IAsyncLifetime
{
    private TestCluster? _cluster;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<ZoneShardTestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<ZoneShardTestClientConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (_cluster != null)
        {
            await _cluster.StopAllSilosAsync();
            _cluster.Dispose();
        }
    }

    [Fact]
    public async Task 任务持有判据在真Orleans里按玩家与任务号逐一对上()
    {
        const ulong holderId = 9_900_000_601UL;
        const ulong strangerId = 9_900_000_602UL;
        const int heldQuestId = 90007;      // 生产库里在线的那条「木狼成灾」
        const int missingQuestId = 90008;   // 没接过的那条

        var client = _cluster!.Client;
        var quests = client.GetGrain<Horizon.Orleans.Interface.IQuestGrain>(GrainKeyMapper.ToInventoryKey(holderId));
        Assert.True(await quests.AcceptQuestAsync(heldQuestId, "木狼成灾", "杀死狼", questType: 1, level: 1,
                                                  rewards: new Dictionary<string, int>()), "接任务应成功");
        Assert.True(await quests.AddQuestObjectiveAsync(heldQuestId, "kill_monster:2", "杀死15只狼", 15),
                    "加击杀目标应成功");

        var reader = new OrleansQuestStateReader(client);

        // ① 接了就判持有（这就是条件件发放的唯一依据）
        Assert.Equal(QuestHoldState.Held, await reader.QueryHoldAsync(holderId, heldQuestId));
        // ② 同一个人没接的那条：不持有（不能退化成"有任意任务就放行"）
        Assert.Equal(QuestHoldState.NotHeld, await reader.QueryHoldAsync(holderId, missingQuestId));
        // ③ 没接过任务的人：不持有（键不能串到别人身上）
        Assert.Equal(QuestHoldState.NotHeld, await reader.QueryHoldAsync(strangerId, heldQuestId));
        // ④ 非法入参不查库也不放行
        Assert.Equal(QuestHoldState.NotHeld, await reader.QueryHoldAsync(0UL, heldQuestId));
        Assert.Equal(QuestHoldState.NotHeld, await reader.QueryHoldAsync(holderId, 0));

        // ⑤ 真发奖：把两条同形条件事件推给生产那个 sink（真 QuestGrain 判持有 + 真背包 Grain 写入）。
        //    持任务的人背包里要真多出这件；没接任务的人一件都不能有——只信计数会把"选了但没写进去"放过去，
        //    所以背包侧必须独立回读（`GetInventoryAsync` 跨 Grain 边界回来）。
        const int itemId = 700007;
        await using (var sink = new OrleansGameplayEventSink(new OrleansInventoryWriter(client), null, questStates: reader))
        {
            sink.Publish(new DsGameplayEvent
            {
                Kind = DsGameplayEventKind.LootConditional, ActorCharacterId = holderId,
                ItemId = itemId, ItemCount = 1, QuestId = heldQuestId, ServerTick = 1,
            });
            sink.Publish(new DsGameplayEvent
            {
                Kind = DsGameplayEventKind.LootConditional, ActorCharacterId = strangerId,
                ItemId = itemId, ItemCount = 1, QuestId = heldQuestId, ServerTick = 1,
            });
            await sink.DrainAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, sink.ConditionalLootGranted);
            Assert.Equal(1, sink.ConditionalLootDeniedNotHeld);
            Assert.Equal(0, sink.ConditionalLootUnknown);
        }

        var holderBag = await client
            .GetGrain<Horizon.Orleans.Interface.IInventoryGrain>(GrainKeyMapper.ToInventoryKey(holderId))
            .GetInventoryAsync();
        var strangerBag = await client
            .GetGrain<Horizon.Orleans.Interface.IInventoryGrain>(GrainKeyMapper.ToInventoryKey(strangerId))
            .GetInventoryAsync();
        Assert.Contains(holderBag.Items, i => i.TemplateId == itemId);
        Assert.DoesNotContain(strangerBag.Items, i => i.TemplateId == itemId);

        // ⑥ 交付之后不再算持有：否则"做完了任务还能从同一只怪身上继续掉任务件"这条刷取会成立。
        //    完成需要目标先满足，这里把进度推进到满再交。
        //    **反证记录**：把读取端那句 `quest.Status == 0` 放宽成 `<= 1`，这条**照样绿**——
        //    因为 `CompleteQuestAsync` 会把任务从 ActiveQuests 挪进 CompletedQuests（QuestGrain.cs:525），
        //    读取端遍历的是进行中列表，条目根本不在了。也就是说这条性质成立靠的是
        //    "交付即移出名单"，那句状态判断是防御性冗余（对可观测行为是等价变异体），别把它当成本用例钉住的东西。
        await new OrleansQuestProgressWriter(client).AdvanceKillObjectiveAsync(holderId, monsterTemplateId: 2, count: 15);
        Assert.True((await quests.CompleteQuestAsync(heldQuestId)).Success, "目标已满足时应可交付");
        Assert.Equal(QuestHoldState.NotHeld, await reader.QueryHoldAsync(holderId, heldQuestId));
    }
}
