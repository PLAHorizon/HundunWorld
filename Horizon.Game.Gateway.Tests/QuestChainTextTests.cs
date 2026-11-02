using Horizon.NpcCli;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 任务链显示文本规则（<see cref="QuestChainText"/>）。
/// <para>
/// 描述/目标两行的文本只在这里拼一次，CLI 把它们当**成品行**发出去、编辑器逐行打印。
/// 两边各拼一份时，"×N"这种小规则会先漂：界面上写着 ×3、客户端进度条却按 3 次算不动，
/// 而谁都不报错。这里把三条边界钉住：带数量、数量 ≤1、以及没写目标文本时的兜底。
/// </para>
/// </summary>
public class QuestChainTextTests
{
    [Fact]
    public void ObjectiveLine_AppendsCountOnlyWhenMeaningful()
    {
        Assert.Equal("击杀荒古凶兽 ×3", QuestChainText.ObjectiveLine("击杀荒古凶兽", 3));
        Assert.Equal("击杀荒古凶兽", QuestChainText.ObjectiveLine("击杀荒古凶兽", 1));
        Assert.Equal("击杀荒古凶兽", QuestChainText.ObjectiveLine("击杀荒古凶兽", 0));
    }

    [Fact]
    public void ObjectiveLine_FallsBackWhenDescriptionMissing()
    {
        Assert.Equal("完成目标", QuestChainText.ObjectiveLine("", 1));
        Assert.Equal("完成目标", QuestChainText.ObjectiveLine(null!, 1));
        Assert.Equal("完成目标 ×5", QuestChainText.ObjectiveLine("   ", 5));
    }

    [Fact]
    public void ObjectiveLine_TrimsButKeepsInnerText()
    {
        Assert.Equal("与指定人物对话", QuestChainText.ObjectiveLine("  与指定人物对话  ", 1));
    }
}