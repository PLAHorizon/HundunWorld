using System;
using System.Threading.Tasks;
using Horizon.Game.Core;
using Horizon.Game.Core.Handlers;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Game.Sim.Economy;
using Horizon.Game.Sim.Npcs;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 学技能（训练师通道）的**执行面**：只能学这位教的、按教学行收学费/材料，学不成原路退回。
/// <para>
/// 与 §11.51 的配方教学同形，但走的是另一条处理器（<see cref="SkillHandler"/> 的 <c>LearnSkill</c>）
/// 与另一个 Grain（<see cref="ISkillGrain"/>）。用真 <see cref="NpcContent"/> + 真
/// <see cref="DsEntityRegistry"/> + 假集群跑 <c>RouteHandlerAsync</c>，钉五格：
/// 不教的式一分不扣、没有训练师位直接拒、教的按行扣费并落库、门槛失败原路退回、材料不齐退款且不落库。
/// </para>
/// </summary>
[Collection(MonsterContentStaticCollection.Name)]
public class SkillLearnGateHandlerTests
{
    private const ulong CharacterId = 8804UL;
    private const ulong EntityId = 70004UL;
    private const int TrainerNpcId = 1;
    private const int TaughtSkill = 1001;
    private const int OtherSkill = 1002;
    private const long Tuition = 500;

    private sealed class Harness
    {
        public SkillHandler Handler = null!;
        public Mock<ICurrencyGrain> Wallet = null!;
        public Mock<IInventoryGrain> Inventory = null!;
        public Mock<ISkillGrain> Skill = null!;
    }

    private static NpcContentSnapshot Snapshot(uint serviceFlags, int trainerItemId = 0, int trainerItemCount = 0) => new()
    {
        ContentRevision = 1,
        Templates =
        {
            new NpcTemplateSnapshot
            {
                NpcId = TrainerNpcId, Name = "传功长老", ModelKey = "npc_trainer",
                Reaction = (byte)NpcStance.Friendly, ServiceFlags = serviceFlags,
            },
        },
        TrainerRows =
        {
            new NpcTrainerRowDto
            {
                NpcId = TrainerNpcId, SkillId = TaughtSkill, ServiceKind = (byte)NpcTrainerService.Skill,
                CostGold = Tuition, CostItemId = trainerItemId, CostItemCount = trainerItemCount, Seq = 0,
            },
        },
    };

    private static Harness Create(
        uint serviceFlags, bool consumeSucceeds = true, bool spendSucceeds = true, bool learnSucceeds = true,
        int trainerItemId = 0, int trainerItemCount = 0)
    {
        var (bundle, receipt) = NpcContentLoader.Compile(Snapshot(serviceFlags, trainerItemId, trainerItemCount));
        Assert.Equal(0, receipt.ProblemCount);
        NpcContent.Install(bundle);

        var registry = new DsEntityRegistry();
        registry.Apply(new DsEntityDescriptorBatch
        {
            ShardId = 0L,
            Descriptors = new[]
            {
                new DsEntityDescriptorDto
                {
                    EntityId = EntityId, ScriptedKind = 2, TemplateId = TrainerNpcId, SpawnPointId = -1,
                    X = 0f, Y = 0f, Z = 0f,
                },
            },
        });

        var wallet = new Mock<ICurrencyGrain>();
        wallet.Setup(w => w.GetBalanceAsync(It.IsAny<int>())).ReturnsAsync(1000);
        wallet.Setup(w => w.SpendAsync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<string>()))
              .ReturnsAsync(spendSucceeds
                  ? new WalletMutation { Ok = true, BalanceBefore = 1000, BalanceAfter = 1000 - Tuition }
                  : new WalletMutation
                  {
                      Ok = false, Reject = (int)WalletReject.InsufficientFunds,
                      BalanceBefore = 100, BalanceAfter = 100,
                  });
        wallet.Setup(w => w.AddAsync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<string>()))
              .ReturnsAsync(new WalletMutation { Ok = true, BalanceBefore = 1000 - Tuition, BalanceAfter = 1000 });

        var inventory = new Mock<IInventoryGrain>();
        inventory.Setup(i => i.ConsumeByTemplateAsync(It.IsAny<ulong>(), It.IsAny<int>(), It.IsAny<int>()))
                 .ReturnsAsync(consumeSucceeds);
        inventory.Setup(i => i.AddItemAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
                 .ReturnsAsync(true);
        inventory.Setup(i => i.BuildLearnGateContextAsync(It.IsAny<ulong>()))
                 .ReturnsAsync(new LearnGateContext());

        var skill = new Mock<ISkillGrain>();
        skill.Setup(s => s.LearnSkillAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<LearnGateContext>()))
             .ReturnsAsync(learnSucceeds
                 ? new LearnSkillResponse { Success = true, Message = "习得成功" }
                 : new LearnSkillResponse { Success = false, Message = "境界不足" });

        var character = new Mock<ICharacterGrain>();
        character.Setup(c => c.GetLastPositionAsync()).ReturnsAsync((0f, 0f, 0f, 0f));

        var cluster = new Mock<IClusterClient>();
        cluster.Setup(c => c.GetGrain<ICharacterGrain>(It.IsAny<long>(), It.IsAny<string>())).Returns(character.Object);
        cluster.Setup(c => c.GetGrain<ICurrencyGrain>(It.IsAny<long>(), It.IsAny<string>())).Returns(wallet.Object);
        cluster.Setup(c => c.GetGrain<IInventoryGrain>(It.IsAny<Guid>(), It.IsAny<string>())).Returns(inventory.Object);
        cluster.Setup(c => c.GetGrain<ISkillGrain>(It.IsAny<Guid>(), It.IsAny<string>())).Returns(skill.Object);

        var handler = new SkillHandler(
            NullLogger<MessageHandlerBase>.Instance, cluster.Object, new HorizonMessageAdapter(), registry, shardRouter: null);

        return new Harness { Handler = handler, Wallet = wallet, Inventory = inventory, Skill = skill };
    }

    private static HorizonMessagePacket Learn(int skillId) => new()
    {
        Header = new MessageHeader
        {
            MessageType = MessageType.LearnSkill,
            ServiceType = ServiceType.Combat,
            CharacterId = (long)CharacterId,
        },
        Body = new LearnSkillRequest
        {
            CharacterId = CharacterId,
            SkillId = skillId,
            EntityId = EntityId,
        },
    };

    [Fact]
    public async Task 学这位不教的式_一分钱不扣且不落库()
    {
        var h = Create((uint)NpcServiceBits.Trainer);
        try
        {
            var (_, packet) = await h.Handler.RouteHandlerAsync(Learn(OtherSkill));

            var response = Assert.IsType<LearnSkillResponse>(packet.Body);
            Assert.False(response.Success);
            Assert.Contains("不教这一式", response.Message);
            h.Wallet.Verify(w => w.SpendAsync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<string>()), Times.Never);
            h.Skill.Verify(s => s.LearnSkillAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<LearnGateContext>()), Times.Never);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public async Task 没有训练师位的NPC_直接拒且不扣费()
    {
        var h = Create(serviceFlags: 0u);
        try
        {
            var (_, packet) = await h.Handler.RouteHandlerAsync(Learn(TaughtSkill));

            var response = Assert.IsType<LearnSkillResponse>(packet.Body);
            Assert.False(response.Success);
            Assert.Contains("不教技能", response.Message);
            h.Wallet.Verify(w => w.SpendAsync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<string>()), Times.Never);
            h.Skill.Verify(s => s.LearnSkillAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<LearnGateContext>()), Times.Never);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public async Task 学这位教的式_按教学行扣500金并落库()
    {
        var h = Create((uint)NpcServiceBits.Trainer);
        try
        {
            var (_, packet) = await h.Handler.RouteHandlerAsync(Learn(TaughtSkill));

            var response = Assert.IsType<LearnSkillResponse>(packet.Body);
            Assert.True(response.Success, response.Message);

            h.Wallet.Verify(w => w.SpendAsync(WalletCurrency.Gold, Tuition,
                $"npc_trainer:{TrainerNpcId}:{TaughtSkill}"), Times.Once);
            h.Skill.Verify(s => s.LearnSkillAsync((long)CharacterId, TaughtSkill, It.IsAny<LearnGateContext>()), Times.Once);
            h.Wallet.Verify(w => w.AddAsync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<string>()), Times.Never);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public async Task 门槛不过_学费要原路退回()
    {
        var h = Create((uint)NpcServiceBits.Trainer, learnSucceeds: false);
        try
        {
            var (_, packet) = await h.Handler.RouteHandlerAsync(Learn(TaughtSkill));

            var response = Assert.IsType<LearnSkillResponse>(packet.Body);
            Assert.False(response.Success);
            Assert.Contains("境界不足", response.Message);

            h.Wallet.Verify(w => w.SpendAsync(WalletCurrency.Gold, Tuition, It.IsAny<string>()), Times.Once);
            // 退款标签与扣款不同：能分辨"退回"与"又扣了一次"
            h.Wallet.Verify(w => w.AddAsync(WalletCurrency.Gold, Tuition,
                $"npc_trainer_refund:{TaughtSkill}"), Times.Once);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public async Task 教学材料不齐_退款且不调用学习()
    {
        var h = Create((uint)NpcServiceBits.Trainer, consumeSucceeds: false,
            trainerItemId: 700011, trainerItemCount: 2);
        try
        {
            var (_, packet) = await h.Handler.RouteHandlerAsync(Learn(TaughtSkill));

            var response = Assert.IsType<LearnSkillResponse>(packet.Body);
            Assert.False(response.Success);
            Assert.Contains("缺少教学材料", response.Message);

            h.Inventory.Verify(i => i.ConsumeByTemplateAsync(CharacterId, 700011, 2), Times.Once);
            h.Wallet.Verify(w => w.AddAsync(WalletCurrency.Gold, Tuition, It.IsAny<string>()), Times.Once);
            h.Skill.Verify(s => s.LearnSkillAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<LearnGateContext>()), Times.Never);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }

    [Fact]
    public async Task 钱不够_金币不足且不落库()
    {
        var h = Create((uint)NpcServiceBits.Trainer, spendSucceeds: false);
        try
        {
            var (_, packet) = await h.Handler.RouteHandlerAsync(Learn(TaughtSkill));

            var response = Assert.IsType<LearnSkillResponse>(packet.Body);
            Assert.False(response.Success);
            Assert.Contains("金币不足", response.Message);
            h.Skill.Verify(s => s.LearnSkillAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<LearnGateContext>()), Times.Never);
            h.Wallet.Verify(w => w.AddAsync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<string>()), Times.Never);
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }
}
