using System;
using Horizon.Orleans.Grains;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 五行相生循环（方案 §5.1 / §5.3）的逐值回归——"循环往复不断决"这句目标话的全部规则。
/// <para>
/// <b>为什么单独钉这一份</b>：这条规则住在 <see cref="CombatCalculator.StepWuxingCycle"/> 这个纯函数里，
/// 由施放路径（<c>SkillGrain.AdvanceCycle</c>）每次施放调用一次。此前它**零直接断言**：
/// 演化/混沌那几条实机测试推周天用的是 <c>GrantCycleAsync</c>（破势夺元那一路），
/// 走的是另一条口子，根本没有经过相生判定。也就是说"按相生序施放会续上链"这件事
/// 一直是靠读代码相信的，改坏了不会有任何测试变红。
/// </para>
/// </summary>
public class WuxingCycleRuleTests
{
    private static readonly DateTime T0 = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>沿 GenerateOf 走一圈，取得该体系真正使用的有向相生环（不把"金生水"之类抄成字面量）。</summary>
    private static int[] GenerationCycle()
    {
        var chain = new int[5];
        var current = CombatCalculator.GenerateOf(1);   // 金的子行
        chain[0] = 1;
        chain[1] = current;
        for (var i = 2; i < 5; i++)
        {
            current = CombatCalculator.GenerateOf(current);
            chain[i] = current;
        }

        // 五行各占一次、且构成闭合环：出现重复就说明某两行被跳过，玩家永远凑不满周天
        Assert.Equal(5, chain.Distinct().Count());
        Assert.Equal(1, CombatCalculator.GenerateOf(chain[4]));
        return chain;
    }

    [Fact]
    public void FirstCastOfAChainAnchorsItAndOccupiesTheFirstRow()
    {
        // 起手那一式自己占一层：少这一层，按相生序打完五行只到 4 层，圆满永远凑不成
        var step = CombatCalculator.StepWuxingCycle(
            stack: 0, lastElement: 0, expireAt: default, completionCount: 0,
            element: 1, now: T0);

        Assert.Equal(1, step.Stack);
        Assert.Equal(1, step.LastElement);
        Assert.False(step.Completed);
        Assert.Equal(0, step.CompletionCount);
        Assert.Equal(1.03f, step.Multiplier, 3);        // 1 + 0.03 × 1 层
    }

    [Fact]
    public void FiveCastsInGeneratingOrderCompleteOneCycle()
    {
        var cycle = GenerationCycle();
        var stack = 0;
        var last = 0;
        var expire = default(DateTime);
        var completions = 0;

        for (var i = 0; i < 5; i++)
        {
            var step = CombatCalculator.StepWuxingCycle(stack, last, expire, completions, cycle[i], T0.AddSeconds(2 * i));
            Assert.True(step.CompletionCount - completions is 0 or 1, "圆满计数只许 +1 或不加");

            if (i < 4)
            {
                Assert.Equal(i + 1, step.Stack);                              // 顺生逐层累加
                Assert.False(step.Completed);
                Assert.Equal(1f + 0.03f * (i + 1), step.Multiplier, 2);
            }
            else
            {
                Assert.True(step.Completed, "第五式（子行回到起链行）必须判周天圆满");
                Assert.Equal(0, step.Stack);                                   // 圆满后清零，下一轮从这一行续起
                Assert.Equal(1, step.CompletionCount);
            }

            stack = step.Stack;
            last = step.LastElement;
            expire = step.ExpireAt;
            completions = step.CompletionCount;
        }
    }

    [Fact]
    public void CompletingHitTakesTheCompletionMultiplier_AndItIsStillTheBiggestNumberOfTheRun()
    {
        var cycle = GenerationCycle();
        // 直接摆到"再打一式就圆满"的状态
        var step = CombatCalculator.StepWuxingCycle(
            stack: 4, lastElement: cycle[3], expireAt: T0.AddSeconds(3),
            completionCount: 0, element: cycle[4], now: T0);

        Assert.True(step.Completed);
        // 清零后本击是 1.0 × 1.25，而不是 1+0.03×4=1.12 —— 圆满那一击必须比第四式更痛，
        // 否则"攒满周天"这个目标本身就成了负收益。
        Assert.Equal(1.25f, step.Multiplier, 3);
        Assert.True(step.Multiplier > 1f + 0.03f * 4);
    }

    [Fact]
    public void SameElementCastKeepsTheStackAndJustMovesTheChain()
    {
        // §5.1：同属不掉层。掉层的表现是"连打两式金就把攒了一半的周天打回原形"，玩家会觉得规则在惩罚他。
        var step = CombatCalculator.StepWuxingCycle(
            stack: 3, lastElement: 2, expireAt: T0.AddSeconds(3), completionCount: 1,
            element: 2, now: T0);

        Assert.Equal(3, step.Stack);
        Assert.Equal(2, step.LastElement);
        Assert.False(step.Completed);
        Assert.Equal(1, step.CompletionCount);
        Assert.Equal(1.09f, step.Multiplier, 2);        // 1 + 0.03 × 3
        Assert.Equal(T0.AddSeconds(3), step.ExpireAt);  // 窗口刷新
    }

    [Fact]
    public void ReverseGenerationCastAlsoKeepsTheStack_WithoutAdvancing()
    {
        // 逆生（打母行）：不掉层也不加层，只把链挪到当前行
        var cycle = GenerationCycle();
        var child = cycle[1];
        var mother = cycle[0];

        var step = CombatCalculator.StepWuxingCycle(
            stack: 2, lastElement: child, expireAt: T0.AddSeconds(3), completionCount: 0,
            element: mother, now: T0);

        Assert.Equal(2, step.Stack);
        Assert.Equal(mother, step.LastElement);
        Assert.False(step.Completed);
    }

    [Fact]
    public void WindowTimeoutRestartsTheChain_FromTheCastItself_NotFromZero()
    {
        // 超时后重新起链：这一式自己就是新的第一层（不是"清零后什么都没得到"）
        var step = CombatCalculator.StepWuxingCycle(
            stack: 4, lastElement: 3, expireAt: T0.AddSeconds(-1), completionCount: 2,
            element: 5, now: T0);

        Assert.Equal(1, step.Stack);
        Assert.Equal(5, step.LastElement);
        Assert.False(step.Completed);
        Assert.Equal(2, step.CompletionCount);        // 超时不清零已有的圆满计数
    }

    [Fact]
    public void ElementlessCast_DoesNotTouchTheChainAtAll()
    {
        // 无属性式（混沌/身法之类）完全不碰链：把它算成"逆生"会莫名打断别人的周天
        var step = CombatCalculator.StepWuxingCycle(
            stack: 3, lastElement: 4, expireAt: T0.AddSeconds(3), completionCount: 7,
            element: 0, now: T0);

        Assert.Equal(3, step.Stack);
        Assert.Equal(4, step.LastElement);
        Assert.Equal(T0.AddSeconds(3), step.ExpireAt);
        Assert.Equal(7, step.CompletionCount);
        Assert.False(step.Completed);
        Assert.Equal(1.09f, step.Multiplier, 2);
    }

    [Fact]
    public void CycleRunsForever_ASecondRoundCompletesAfterTheFirst()
    {
        // "循环往复不断决"的字面要求：圆满之后还能再圆满，计数是累加的而不是只能达成一次
        var cycle = GenerationCycle();
        var stack = 0;
        var last = 0;
        var expire = default(DateTime);
        var completions = 0;
        var completedHits = 0;

        for (var cast = 0; cast < 10; cast++)
        {
            var step = CombatCalculator.StepWuxingCycle(stack, last, expire, completions, cycle[cast % 5], T0.AddSeconds(cast * 2));
            if (step.Completed) completedHits++;
            stack = step.Stack;
            last = step.LastElement;
            expire = step.ExpireAt;
            completions = step.CompletionCount;
        }

        Assert.Equal(2, completedHits);
        Assert.Equal(2, completions);
    }

    [Fact]
    public void OvercomingIsTheDirectedCounterTable_AndIsNotTheGeneratingTable()
    {
        // 相克与相生是两张不同的有向表；把它们混为一谈会让"破势"打在错误的行上
        Assert.Equal(2, CombatCalculator.CounteredElement(1));   // 金克木
        Assert.Equal(5, CombatCalculator.CounteredElement(2));   // 木克土
        Assert.Equal(3, CombatCalculator.CounteredElement(5));   // 土克水
        Assert.Equal(4, CombatCalculator.CounteredElement(3));   // 水克火
        Assert.Equal(1, CombatCalculator.CounteredElement(4));   // 火克金

        for (var e = 1; e <= 5; e++)
        {
            Assert.NotEqual(e, CombatCalculator.CounteredElement(e));
            Assert.NotEqual(CombatCalculator.GenerateOf(e), CombatCalculator.CounteredElement(e));
        }

        Assert.Equal(0, CombatCalculator.CounteredElement(0));
        Assert.Equal(0, CombatCalculator.GenerateOf(0));
    }
}
