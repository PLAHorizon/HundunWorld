using Horizon.Game.Message.Network;
using Orleans;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 宠物名册 Grain（键 = 主人<b>角色</b> id）。
    /// <para>
    /// 为什么按角色而不是按账号：个体表 <c>owner_character_id</c> 与背包/技能/货币那些
    /// 玩家私有表同键（都挂 <c>character_id</c>），名册与它们一致才连得上角色面板那条链。
    /// 梦幻西游/大话的召唤兽本就属于角色，不存在跨角色共享。</para>
    /// <para>
    /// 本期只开两条通路：<see cref="GetSnapshotAsync"/>（反哺取数，角色面板消费）与
    /// <see cref="SetDeployStateAsync"/>（出战/附体/收回）。
    /// 捕捉落库与客户端按钮尚未接线，接缝记在设计文档 §9/§10.4。</para>
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IPetRosterGrain : IGrainWithIntegerKey
    {
        /// <summary>读出名册并把参与反哺的宠物合成一份主人加成账。</summary>
        Task<PetRosterSnapshotDto> GetSnapshotAsync();

        /// <summary>
        /// 改个体状态。<paramref name="targetState"/> 取 <c>PetInstanceState</c> 的序号：
        /// 2=Owned（收回）、3=Following（放养）、4=Battling（出战）、5=Possessed（附体）、6=Stable（兽栏）、9=Released（放生）。
        /// </summary>
        Task<PetDeployResultDto> SetDeployStateAsync(long instanceId, int targetState);
    }

    /// <summary>
    /// 反哺取数的可注入门面（角色 Grain 经它拿宠物账）。
    /// <para>
    /// 为什么包一层而不是在 <c>CharacterGrain</c> 里直接 <c>GrainFactory.GetGrain&lt;IPetRosterGrain&gt;</c>：
    /// 跨 Grain 编排在 Moq 脚手架里会被 catch 吞掉，测试看到的是"加成 0 且无异常"，
    /// 与"真的没有宠"同形（记忆 <c>cross-grain-orchestration-needs-injectable-provider</c>）。
    /// 有了这一层，单测注入假读数器就能断言"值到没到消息里"。</para>
    /// </summary>
    public interface IPetBonusReader
    {
        /// <summary>取该角色的宠物加成账；<b>读不到要如实回空账并把原因写进 Issues</b>，不许抛到调用方。</summary>
        Task<PetRosterSnapshotDto> ReadAsync(long ownerCharacterId, CancellationToken cancellationToken = default);
    }
}
