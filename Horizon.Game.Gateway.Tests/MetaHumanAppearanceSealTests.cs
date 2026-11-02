using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.GameModel;
using Horizon.Orleans.Grains;
using MemoryPack;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans.Runtime;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 「保存 → 发布后不再可回溯」这条产品契约在服务端的闸门，常驻回归。
/// <para>
/// 此前它只有一次性脚本（Tools/MhAppearanceVerify）连真集群跑出来的 22/0，仓库里 grep 零命中：
/// 闸门被改坏时没有任何东西会亮。这里把同一组判定钉成常驻测试，并补上那次没覆盖的一种失效形状——
/// grain 状态（Redis 热缓存）被清空后，SQL 那列才是发布与否的权威；只看状态等于清一次缓存就能改已发布的脸。
/// </para>
/// </summary>
public class MetaHumanAppearanceSealTests
{
    private const ulong CharacterId = 910001UL;
    private const string Template = "metahuman.face_archetype+metahuman.body_base";

    /// <summary>裸激活的 CharacterGrain + 可控的 grain 状态 + 可控的角色行。</summary>
    private static (CharacterGrain grain, CharacterState state, CharacterEntity row,
                    Mock<IDataContext<GameEntityContext, CharacterEntity, long>> db) CreateGrain(
        MetaHumanLookPayload? storedInState,
        bool rowPublished,
        byte[]? rowBlob)
    {
        var grain = (CharacterGrain)RuntimeHelpers.GetUninitializedObject(typeof(CharacterGrain));
        GrainTestHarness.InjectField(grain, "_logger", NullLogger<CharacterGrain>.Instance);

        // CharacterId 是自动属性不是字段；显式走 setter，将来改名要炸在测试里而不是静默写成 0 号角色。
        var characterIdProperty = typeof(CharacterGrain)
            .GetProperty("CharacterId", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(characterIdProperty);
        characterIdProperty!.SetValue(grain, CharacterId);

        var state = new CharacterState
        {
            CharacterInfo = new CharacterInfo { CharacterId = (long)CharacterId, CharacterName = "封存测试" },
            MetaHumanLook = storedInState,
            MetaHumanLookRevision = storedInState != null ? 1 : 0,
        };
        var persistent = new Mock<IPersistentState<CharacterState>>();
        persistent.SetupGet(s => s.State).Returns(state);
        persistent.Setup(s => s.WriteStateAsync()).Returns(Task.CompletedTask);
        GrainTestHarness.InjectField(grain, "_characterState", persistent.Object);

        var row = new CharacterEntity
        {
            Id = (long)CharacterId,
            MetaHumanPublished = rowPublished,
            MetaHumanLook = rowBlob,
            MetaHumanTemplateId = rowBlob != null ? Template : null,
            MetaHumanChecksum = rowBlob != null ? unchecked((int)MetaHumanLookBlobHash.Compute(rowBlob)) : null,
            MetaHumanRevision = rowBlob != null ? 1 : null,
        };
        var db = new Mock<IDataContext<GameEntityContext, CharacterEntity, long>>();
        db.Setup(d => d.QueryFirstOrDefaultAsync(It.IsAny<Expression<Func<CharacterEntity, bool>>>(), It.IsAny<bool>()))
          .ReturnsAsync(row);
        db.Setup(d => d.UpdateAsync(It.IsAny<CharacterEntity>(), It.IsAny<long>())).ReturnsAsync(true);
        GrainTestHarness.InjectField(grain, "_gameCharacterContext", db.Object);

        return (grain, state, row, db);
    }

    private static MetaHumanLookPayload Look(byte seed, bool published = false)
    {
        var blob = new byte[64];
        for (int i = 0; i < blob.Length; i++)
        {
            blob[i] = (byte)(seed * 31 + i);
        }

        return new MetaHumanLookPayload
        {
            TemplateId = Template,
            TemplateVersion = "1",
            TemplateParamCount = blob.Length,
            EntryCount = 1,
            Blob = blob,
            BlobChecksum = MetaHumanLookBlobHash.Compute(blob),
            Published = published,
        };
    }

    /// <summary>
    /// 正例对照：同一套脚手架必须能把未发布的存档写进去。
    /// 没有这条，下面每一条「被拒」断言都可能只是夹具根本存不进去的空转。
    /// </summary>
    [Fact]
    public async Task UnpublishedSaveIsAcceptedAndLandsInBothTiers()
    {
        var (grain, state, row, db) = CreateGrain(storedInState: null, rowPublished: false, rowBlob: null);
        var look = Look(seed: 1);

        var response = await grain.UpdateAppearanceAsync(look);

        Assert.True(response.Success, response.Error);
        Assert.Equal(look.BlobChecksum, response.StoredChecksum);
        Assert.Equal(1, response.StoredRevision);
        Assert.Same(look, state.MetaHumanLook);
        db.Verify(d => d.UpdateAsync(It.Is<CharacterEntity>(r =>
            r.MetaHumanPublished == false && r.MetaHumanLook == look.Blob), It.IsAny<long>()), Times.Once);
    }

    /// <summary>发布这一步本身必须能写进去——否则「发布」这个动作没有入口，封存永远测不到。</summary>
    [Fact]
    public async Task PublishIsAcceptedAndMarksTheRowSealed()
    {
        var published = Look(seed: 2, published: true);
        var (grain, _, row, _) = CreateGrain(storedInState: Look(seed: 2), rowPublished: false, rowBlob: null);

        var response = await grain.UpdateAppearanceAsync(published);

        Assert.True(response.Success, response.Error);
        Assert.True(row.MetaHumanPublished);
        Assert.Equal(published.Blob, row.MetaHumanLook);
    }

    [Fact]
    public async Task EditAfterPublishIsRejectedAndNothingIsOverwritten()
    {
        var published = Look(seed: 3, published: true);
        var (grain, state, row, db) = CreateGrain(published, rowPublished: true, rowBlob: published.Blob);

        var response = await grain.UpdateAppearanceAsync(Look(seed: 4));

        Assert.False(response.Success);
        Assert.Contains("已发布", response.Error);
        // 回执要指回库里那份已发布的存档，客户端据此判断"不是我算错了"
        Assert.Equal(published.BlobChecksum, response.StoredChecksum);
        Assert.Equal(1, response.StoredRevision);
        Assert.Same(published, state.MetaHumanLook);
        Assert.Equal(published.Blob, row.MetaHumanLook);
        db.Verify(d => d.UpdateAsync(It.IsAny<CharacterEntity>(), It.IsAny<long>()), Times.Never);
    }

    /// <summary>
    /// 回溯语义的真实形状：客户端把发布前那一版的 blob 再发一遍。
    /// 闸门拒的是「任何后续写入」，不看内容像不像新版本——否则玩家只要留了旧存档就能解封。
    /// </summary>
    [Fact]
    public async Task RollingBackToThePrePublishBlobIsRejected()
    {
        var published = Look(seed: 5, published: true);
        var beforePublish = Look(seed: 5);
        var (grain, _, _, _) = CreateGrain(published, rowPublished: true, rowBlob: published.Blob);

        var response = await grain.UpdateAppearanceAsync(beforePublish);

        Assert.False(response.Success);
        Assert.Contains("已发布", response.Error);
        Assert.Equal(published.BlobChecksum, response.StoredChecksum);
    }

    /// <summary>
    /// 这次补的真缺陷：Redis 状态被清（LRU 淘汰 / 清库）后 state.MetaHumanLook 为 null，
    /// 而 SQL 行仍写着已发布。只看状态的那版闸门会放行，等于"清一次缓存便能改回已发布的脸"。
    /// </summary>
    [Fact]
    public async Task SealStillHoldsAfterGrainStateEviction()
    {
        var published = Look(seed: 6, published: true);
        var (grain, _, row, db) = CreateGrain(storedInState: null, rowPublished: true, rowBlob: published.Blob);

        var response = await grain.UpdateAppearanceAsync(Look(seed: 7));

        Assert.False(response.Success, "热缓存清空后已发布的外观被改写了：封存判定必须回看 SQL 那列");
        Assert.Contains("已发布", response.Error);
        // 状态里没有那份时，回执要从行里取；报 0 会让客户端以为"从没存过"而重建一份
        Assert.Equal(published.BlobChecksum, response.StoredChecksum);
        Assert.Equal(1, response.StoredRevision);
        Assert.Equal(published.Blob, row.MetaHumanLook);
        db.Verify(d => d.UpdateAsync(It.IsAny<CharacterEntity>(), It.IsAny<long>()), Times.Never);
    }

    /// <summary>
    /// 反向对照：行存在但<b>没</b>发布、状态为空（首次进场/清缓存）时必须照常放行第一次保存。
    /// 防住把封存判定写成"查过行就一律拒"的改法——那会把整条外观通道一起拦死。
    /// </summary>
    [Fact]
    public async Task UnpublishedRowWithEmptyStateStillAcceptsTheFirstSave()
    {
        var (grain, state, _, _) = CreateGrain(storedInState: null, rowPublished: false, rowBlob: Look(seed: 8).Blob);

        var response = await grain.UpdateAppearanceAsync(Look(seed: 9));

        Assert.True(response.Success, response.Error);
        Assert.NotNull(state.MetaHumanLook);
    }

    /// <summary>
    /// 同族拒绝分支要回同一套对账字段。逐条走一遍五条门，任何一条把回执留成 0 都会在这里红。
    /// </summary>
    [Fact]
    public async Task EveryRejectionBranchReportsWhichLookIsStored()
    {
        var stored = Look(seed: 10);
        var cases = new List<(string Name, MetaHumanLookPayload Payload)>
        {
            ("外观数据为空", new MetaHumanLookPayload { TemplateId = Template, Blob = new byte[0] }),
            ("外观超限", new MetaHumanLookPayload
            {
                TemplateId = Template,
                Blob = new byte[256 * 1024 + 1],
                BlobChecksum = 12345,
            }),
            ("校验和不符", new MetaHumanLookPayload
            {
                TemplateId = Template,
                Blob = new byte[32],
                BlobChecksum = 1,
            }),
            ("模板不一致", new MetaHumanLookPayload
            {
                TemplateId = "别的模板",
                Blob = new byte[32],
                BlobChecksum = MetaHumanLookBlobHash.Compute(new byte[32]),
            }),
            ("已发布", Look(seed: 11, published: true)),
        };

        foreach (var (name, payload) in cases)
        {
            var published = new MetaHumanLookPayload
            {
                TemplateId = Template,
                Blob = stored.Blob,
                BlobChecksum = stored.BlobChecksum,
                Published = name == "已发布",
            };
            var (grain, _, _, _) = CreateGrain(published,
                rowPublished: name == "已发布",
                rowBlob: published.Blob);

            var response = await grain.UpdateAppearanceAsync(payload);

            Assert.False(response.Success, name + "：这条本该被拒却写进去了");
            Assert.False(string.IsNullOrEmpty(response.Error), name + "：被拒却没说原因");
            Assert.Equal(stored.BlobChecksum, response.StoredChecksum);
            Assert.Equal(1, response.StoredRevision);

            // 封存是**机器可读的状态位**，不是"错误文案里含已发布"。
            // 只有封存那一条门能置真：其余四条也是拒绝，客户端要是按"被拒"就封存，
            // 一次校验和算错就会把玩家的面板锁死。
            Assert.Equal(name == "已发布", response.AppearanceSealed);
        }
    }

    /// <summary>
    /// 新增的序列化槽位要按位对得上：AppearanceSealed 是追加在 StoredRevision 之后的 order 4。
    /// 这里独立走一遍 MemoryPack 往返，钉住"追加字段不改动已有字段的槽位"——
    /// 槽位错位不会报错，只会让旧存档整体平移成另一个含义。
    /// </summary>
    [Fact]
    public void AppendedSealFieldDoesNotShiftExistingSlots()
    {
        var original = new UpdateCharacterAppearanceResponse
        {
            Success = false,
            Error = "该角色外观已发布，服务端拒绝再改",
            StoredChecksum = 0xDEADBEEF,
            StoredRevision = 7,
            AppearanceSealed = true,
        };

        var bytes = MemoryPackSerializer.Serialize(original);
        var round = MemoryPackSerializer.Deserialize<UpdateCharacterAppearanceResponse>(bytes);

        Assert.Equal(original.Success, round.Success);
        Assert.Equal(original.Error, round.Error);
        Assert.Equal(original.StoredChecksum, round.StoredChecksum);
        Assert.Equal(original.StoredRevision, round.StoredRevision);
        Assert.True(round.AppearanceSealed);

        // 老那份"没有这格"的载荷（只有前四个槽位有值）必须仍解得出默认 false，
        // 而不是把 Revision 的位读进新字段里。
        var legacy = new UpdateCharacterAppearanceResponse
        {
            Success = true,
            Error = string.Empty,
            StoredChecksum = 0x1234,
            StoredRevision = 3,
        };
        var legacyRound = MemoryPackSerializer.Deserialize<UpdateCharacterAppearanceResponse>(
            MemoryPackSerializer.Serialize(legacy));
        Assert.False(legacyRound.AppearanceSealed);
        Assert.Equal(3, legacyRound.StoredRevision);
    }

    /// <summary>
    /// 同一个事实在两个方法上必须给同一个答案：IsAppearancePublishedAsync 答"未封存"而
    /// UpdateAppearanceAsync 却拒收，客户端就会把回溯入口点亮、一路操作到被服务端拒才发现问题。
    /// 五种 (状态, 行) 组合同时问两边。
    /// </summary>
    [Fact]
    public async Task ReadSideAnswerAgreesWithWriteSideGate()
    {
        var publishedLook = Look(seed: 12, published: true);
        var draftLook = Look(seed: 12);

        var combos = new (string Name, MetaHumanLookPayload? State, bool RowPublished, bool ExpectSealed)[]
        {
            ("状态与行都说已发布", publishedLook, true, true),
            ("只有状态说已发布", publishedLook, false, true),
            ("只有行说已发布（清过缓存）", null, true, true),
            ("两边都没存档", null, false, false),
            ("存档是草稿未发布", draftLook, false, false),
        };

        foreach (var (name, stateLook, rowPublished, expectSealed) in combos)
        {
            var rowBlob = stateLook?.Blob ?? (rowPublished ? publishedLook.Blob : null);
            var (grain, _, _, _) = CreateGrain(stateLook, rowPublished, rowBlob);

            Assert.Equal(expectSealed, await grain.IsAppearancePublishedAsync());

            // 写侧的口径用同一夹具再问一遍：被拒 当且仅当 判定为已封存
            var response = await grain.UpdateAppearanceAsync(Look(seed: 13));
            Assert.True(!response.Success == expectSealed,
                name + "：读侧答 " + expectSealed + "，写侧" + (response.Success ? "放行" : "拒收 " + response.Error));
        }
    }

    /// <summary>
    /// 写进 Redis 但**没落进 SQL 权威副本**时，必须机器可读地报出来。
    /// 起因是真集群那轮：对根本不存在的关系（`character_id=40` 那行不在表里）工装照样全绿 ——
    /// 因为 grain 写状态成功、SQL 落库在找不到实体时只 LogWarning。
    /// 两条一起跑才算数：行在 ⇒ 不报缺失（闸门不许在正常路径上常亮）；行不在 ⇒ 成功但报缺失。
    /// </summary>
    [Fact]
    public async Task MissingSqlRowIsReportedEvenThoughTheWriteSucceeded()
    {
        var (grain, _, _, _) = CreateGrain(storedInState: null, rowPublished: false, rowBlob: null);
        var ok = await grain.UpdateAppearanceAsync(Look(seed: 14));
        Assert.True(ok.Success, ok.Error);
        Assert.False(ok.AuthorityCopyMissing, "行存在时不该报权威副本缺失");

        var (grainNoRow, _, _, dbNoRow) = CreateGrain(storedInState: null, rowPublished: false, rowBlob: null);
        dbNoRow.Setup(d => d.QueryFirstOrDefaultAsync(It.IsAny<Expression<Func<CharacterEntity, bool>>>(), It.IsAny<bool>()))
               .ReturnsAsync((CharacterEntity?)null);

        var orphan = await grainNoRow.UpdateAppearanceAsync(Look(seed: 15));
        Assert.True(orphan.Success, "grain 状态确实存住了，所以不算写失败");
        Assert.True(orphan.AuthorityCopyMissing,
                    "SQL 里没有这一行 ⇒ Redis 被淘汰后就没有东西兜住「发布后永久生效」，必须报出来");
    }

    /// <summary>
    /// 重登那一侧的封存：热缓存被清后，<see cref="CharacterGrain.GetAppearanceAsync"/> 从 SQL 补水时
    /// 必须把"已发布"一起带回来。写侧回看 SQL 而读侧只看缓存的话，同一个事实在两个方法上给出两个答案——
    /// 客户端会把回溯入口点亮、滑杆照拖，然后每次保存都被写侧拒，玩家看到的只是"我的脸改不动"。
    /// </summary>
    [Fact]
    public async Task RehydrationCarriesThePublishedFlagSoRelogSealsToo()
    {
        byte[] publishedBlob = Look(seed: 30).Blob;
        var (grain, state, _, _) = CreateGrain(storedInState: null, rowPublished: true, rowBlob: publishedBlob);

        MetaHumanLookPayload? back = await grain.GetAppearanceAsync();

        Assert.NotNull(back);
        Assert.True(back!.Published,
            "状态被清空后重登，读侧必须把 SQL 那列的已发布带回来（否则写侧拒、读侧说没封存，客户端会把编辑入口点亮）");
        Assert.Equal(publishedBlob, back.Blob);
        Assert.Equal(MetaHumanLookBlobHash.Compute(back.Blob), back.BlobChecksum);
        Assert.True(state.MetaHumanLook?.Published ?? false,
                    "补水要把这一份填回状态：读写两侧必须来自同一份事实，不能各查各的");

        // 正向对照：行里未发布 ⇒ 读侧不许凭空报已发布（否则这条门会把没发布的角色一起封掉）
        var (grainDraft, stateDraft, _, _) = CreateGrain(storedInState: null, rowPublished: false, rowBlob: Look(seed: 31).Blob);
        MetaHumanLookPayload? draft = await grainDraft.GetAppearanceAsync();

        Assert.NotNull(draft);
        Assert.False(draft!.Published, "未发布时补水结果不该带已发布标记");
        Assert.False(stateDraft.MetaHumanLook?.Published ?? false, "未发布时状态回填也不许把它标成已发布");
    }

    /// <summary>
    /// 退化输入：SQL 里那一行是**已发布**的，但它的 blob 与自述校验和不符（行被截断或写坏）。
    /// 这一格要钉住三件事，缺一件就会变成"玩家的脸看不见、面板却点亮着、保存又被拒"那种查不动的现象：
    /// ① 不许把坏数据当外观发给客户端；② 封存这个事实**不依赖** blob 完好（published 是另一列）；
    /// ③ 写侧与读侧对"是否已发布"必须给同一个答案。
    /// </summary>
    [Fact]
    public async Task CorruptPublishedRowLeaksNoLookButStillAnswersSealed()
    {
        byte[] blob = Look(seed: 32).Blob;
        var (grain, state, row, _) = CreateGrain(storedInState: null, rowPublished: true, rowBlob: blob);

        // 夹具里的校验和是按 rowBlob 现算的（自洽）；这里把它改成对不上，模拟行被截断/写坏。
        row.MetaHumanChecksum = unchecked((int)MetaHumanLookBlobHash.Compute(blob) + 1);

        MetaHumanLookPayload? back = await grain.GetAppearanceAsync();
        Assert.Null(back ?? state.MetaHumanLook);          // 坏的那份不许出门
        Assert.False(state.MetaHumanLook?.Published ?? false, "校验和不符时不得把坏 blob 回填进状态");

        Assert.True(await grain.IsAppearancePublishedAsync(),
            "封存写在 published 那一列上，不该因为 blob 坏了就答\"没发布\"");

        var rejected = await grain.UpdateAppearanceAsync(Look(seed: 33));
        Assert.False(rejected.Success, "坏行不解除闸门：" + rejected.Error);
        Assert.True(rejected.AppearanceSealed,
            "读侧答已发布 ⇒ 写侧也必须以已发布拒改，同一个事实两个方法只能有一个答案");
    }
}
