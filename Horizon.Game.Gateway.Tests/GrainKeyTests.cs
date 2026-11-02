using System;
using Horizon.Game.Core;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 角色维度 Grain key 的回归测试。
/// <para>
/// 攻击/施法路径历史上把 CombatGrain key 写成 <c>Guid.NewGuid()</c>，每次请求都激活一个新 Grain，
/// 于是 <c>CombatState.CombatParticipants</c> 缓存、技能冷却与战斗日志永远命不中同一份状态。
/// 本套件钉死"同实体同 key、按受击方收敛对抗"的约定。
/// </para>
/// </summary>
public class GrainKeyTests
{
    [Fact]
    public void ForEntity_is_stable_for_the_same_id()
        => Assert.Equal(GrainKeys.ForEntity(42), GrainKeys.ForEntity(42));

    [Fact]
    public void ForEntity_distinguishes_different_ids()
        => Assert.NotEqual(GrainKeys.ForEntity(42), GrainKeys.ForEntity(43));

    [Fact]
    public void ForEntity_round_trips_the_id()
        => Assert.Equal(1234567890UL, BitConverter.ToUInt64(GrainKeys.ForEntity(1234567890UL).ToByteArray(), 0));

    [Fact]
    public void ForEngagement_converges_attackers_on_the_same_target()
    {
        var a = GrainKeys.ForEngagement(targetId: 200, fallback: 1);
        var b = GrainKeys.ForEngagement(targetId: 200, fallback: 2);

        Assert.Equal(a, b);
        Assert.Equal(GrainKeys.ForEntity(200), a);
    }

    [Fact]
    public void ForEngagement_falls_back_to_the_initiator_when_there_is_no_target()
        => Assert.Equal(GrainKeys.ForEntity(7), GrainKeys.ForEngagement(targetId: 0, fallback: 7));
}
