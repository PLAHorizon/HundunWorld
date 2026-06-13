using Horizon.IM.Core;
using Horizon.IM.Message.Network;
using Horizon.Orleans.Interface;

using Orleans.TestingHost;

namespace Horizon.IM.Gateway.Tests;

/// <summary>
/// 验证 PR #94 审查意见修复后的 IMGroupGrain / IMUserGrain 边缘行为：
/// 1. 已解散群组在同一 Grain 上重建时不继承旧 Members/ChatHistory。
/// 2. OwnedGroupNames 含过期条目时，用户粒子仍能正常激活并创建同名新群。
/// 3. 从未创建的 GroupGrain（GroupId == Guid.Empty）不会永久封堵群名。
/// </summary>
[Collection("OrleansCluster")]
public sealed class IMGroupGrainRebuildTests : IAsyncLifetime
{
    private TestCluster? _cluster;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<IMGatewayTestSiloConfigurator>();

        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (_cluster != null)
        {
            await _cluster.StopAllSilosAsync();
            await _cluster.DisposeAsync();
        }
    }

    /// <summary>
    /// 解散后在同一 Grain 上重建，新群不应继承旧成员列表。
    /// </summary>
    [Fact]
    public async Task RebuildAfterDisband_DoesNotInheritOldMembers()
    {
        Assert.NotNull(_cluster);

        var ownerId = IMGrainKey.NewUInt64Id();
        var oldMemberId = IMGrainKey.NewUInt64Id();
        var groupId = IMGrainKey.NewUInt64Id();

        var groupGrain = _cluster!.GrainFactory.GetGrain<IIMGroupGrain>(IMGrainKey.ToGuid(groupId));

        // 创建原始群组，加入一名成员
        var createResult = await groupGrain.CreateGroupAsync(new IMGroupCreateRequest
        {
            CreatorId = ownerId,
            GroupName = "旧群-重建测试",
            MaxMembers = 50,
            InitialMemberIds = new List<ulong> { oldMemberId }
        });
        Assert.True(createResult.Success, $"初次创建群组失败: {createResult.Message}");

        var infoBeforeDisband = await groupGrain.GetGroupInfoAsync();
        Assert.Equal(2, infoBeforeDisband.MemberCount); // 群主 + oldMember

        // 解散群组
        var disbandResult = await groupGrain.DisbandGroupAsync(new IMGroupDisbandRequest
        {
            OwnerId = ownerId,
            GroupId = groupId
        });
        Assert.True(disbandResult.Success, $"解散群组失败: {disbandResult.Message}");

        var infoAfterDisband = await groupGrain.GetGroupInfoAsync();
        Assert.True(infoAfterDisband.IsDisbanded, "解散后 IsDisbanded 应为 true");

        // 在同一 Grain 上以新群主重建（模拟 UUID 复用场景）
        var newOwnerId = IMGrainKey.NewUInt64Id();
        var rebuildResult = await groupGrain.CreateGroupAsync(new IMGroupCreateRequest
        {
            CreatorId = newOwnerId,
            GroupName = "新群-重建测试",
            MaxMembers = 50
        });
        Assert.True(rebuildResult.Success, $"重建群组失败: {rebuildResult.Message}");

        var infoAfterRebuild = await groupGrain.GetGroupInfoAsync();
        // 重建后只应有新群主，不应包含旧成员
        Assert.Equal(1, infoAfterRebuild.MemberCount);
        Assert.Equal(newOwnerId, infoAfterRebuild.OwnerId);
        Assert.False(infoAfterRebuild.IsDisbanded);
    }

    /// <summary>
    /// OwnedGroupNames 中含有过期条目（指向未初始化的 GroupGrain）时，
    /// 用户粒子应能正常激活，且同名新群可以创建成功。
    /// </summary>
    [Fact]
    public async Task StaleOwnedGroupNameEntry_DoesNotBlockNewGroupCreation()
    {
        Assert.NotNull(_cluster);

        var ownerId = IMGrainKey.NewUInt64Id();
        var staleGroupId = IMGrainKey.NewUInt64Id(); // 对应从未写入状态的 GroupGrain
        var newGroupId = IMGrainKey.NewUInt64Id();

        var userGrain = _cluster!.GrainFactory.GetGrain<IIMUserGrain>(IMGrainKey.ToGuid(ownerId));

        // 手动向用户粒子注册一个过期的群名（该 GroupGrain 从未写入状态，等效于 GroupId == Guid.Empty）
        var registered = await userGrain.CheckAndRegisterGroupNameAsync("过期群名", staleGroupId);
        Assert.True(registered, "首次注册群名应成功");

        // 尝试用同一群名创建新群：staleGroupId 对应的 GroupGrain 的 IsDisbanded 应被识别为 true（未初始化）
        var newGroupGrain = _cluster.GrainFactory.GetGrain<IIMGroupGrain>(IMGrainKey.ToGuid(newGroupId));
        var createResult = await newGroupGrain.CreateGroupAsync(new IMGroupCreateRequest
        {
            CreatorId = ownerId,
            GroupName = "过期群名",
            MaxMembers = 50
        });
        Assert.True(createResult.Success, $"未初始化旧 GroupGrain 不应封堵群名，但创建失败: {createResult.Message}");
    }

    /// <summary>
    /// 从未创建的 GroupGrain（state.GroupId == Guid.Empty）在 GetGroupInfoAsync 中
    /// IsDisbanded 应返回 true，以便 CheckAndRegisterGroupNameAsync 能解封群名。
    /// </summary>
    [Fact]
    public async Task NeverCreatedGroupGrain_ReportsIsDisbandedTrue()
    {
        Assert.NotNull(_cluster);

        // 获取一个从未调用过 CreateGroupAsync 的 GroupGrain
        var ghostGroupId = IMGrainKey.NewUInt64Id();
        var ghostGrain = _cluster!.GrainFactory.GetGrain<IIMGroupGrain>(IMGrainKey.ToGuid(ghostGroupId));

        var info = await ghostGrain.GetGroupInfoAsync();

        Assert.True(info.IsDisbanded, "从未创建的 GroupGrain 在 GetGroupInfoAsync 中 IsDisbanded 应为 true");
    }
}
