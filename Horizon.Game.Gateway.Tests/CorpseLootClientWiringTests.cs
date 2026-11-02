using System;
using System.IO;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 尸体拾取的**客户端入口**在源码里全程可查（源级闸门）。
/// <para>
/// 这条补丁是 10 处锚定插入（`CORPSE-LOOT-PATCH` 记号），跨 6 个文件：
/// 发包口 → 状态位搬进选中标记 → G 键分支 → 收包事件 → 事件入口的到期提示。
/// 少任何一处，玩家那边都只是"按了 G 没反应"或"提示永远不出现"，而服务端每一道判据都还是绿的——
/// 这正是源级闸门要挡的那类回归（并行会话/回退/手改都可能只带走其中一处）。
/// </para>
/// <para>它<b>不</b>证明运行期表现（提示位置、G 键与谁冲突、尸体高亮），那些要 PIE。</para>
/// </summary>
public class CorpseLootClientWiringTests
{
    private const string Marker = "CORPSE-LOOT-PATCH";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Horizon.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ClientFile(params string[] parts)
    {
        var path = Path.Combine(RepoRoot(), "HundunWorld", "Source", "Game");
        foreach (var p in parts) path = Path.Combine(path, p);
        Assert.True(File.Exists(path), "缺文件：" + path);
        return File.ReadAllText(path);
    }

    private static int CountOccurrences(string text, string needle)
    {
        var n = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(needle, i + 1, StringComparison.Ordinal)) n++;
        return n;
    }

    [Fact]
    public void 自动验收驱动被标记文件关着且只走玩家同一个入口()
    {
        // 给"没人按键"的自动验收留的驱动：存在 `monster-accept.on` 时才动，且**只调用玩家同一个入口**
        // （TryBasicAttackSelected / TryInteractSelected），不绕过任何判据；每一步打一行 [MonsterAccept]。
        // 这条闸门钉两件：① 默认（标记不存在）什么都不做；② 它没有自己写一份攻击/拾取逻辑。
        var selection = ClientFile("Selection", "WorldSelectionService.cs");
        Assert.Contains("monster-accept.on", selection);
        Assert.Contains("[MonsterAccept]", selection);
        Assert.Contains("if (!System.IO.File.Exists(AcceptMarker)) return;", selection);
        Assert.Contains("TryBasicAttackSelected();", selection);
        Assert.Contains("TryInteractSelected();", selection);
        // 驱动里不许出现第二次上报（自写一条上报 = 判据/计数分叉）
        Assert.True(CountOccurrences(selection, "ReportAttack(") == 1,
                    "驱动或别的入口自己又发了一次攻击上报：服务端会收到两份");
    }

    [Fact]
    public void 可拾取尸体在世界里有一句提示且只在边沿打日志()
    {
        // 死怪**不挂名牌**（这是刻意与 WoW 对齐的），所以"按 G 拾取"必须是单独一句提示；
        // 少了它，玩家只看到地上一个模型，不知道还能取——这正是"配了但玩家看不见"那一类。
        // 同时日志必须**边沿触发**：逐帧打会把日志刷满，也让验收脚本读不出"什么时候出现的"。
        var nameplate = ClientFile("UI", "GameMain", "NameplateUI.cs");
        Assert.Contains("按 G 拾取", nameplate);
        Assert.Contains("UpdateLootPrompt(host, camera, tag)", nameplate);   // 死怪分支真的调它
        Assert.Contains("tag.CorpseInteractable", nameplate);                // 可见性由快照状态位驱动
        Assert.Contains("if (show != _lootPromptShown)", nameplate);         // 边沿守卫
        Assert.Contains("[CorpsePrompt]", nameplate);                        // 验收脚本认这一行
        Assert.True(CountOccurrences(nameplate, "HideLootPrompt();") >= 2,
                    "提示只有'显示'没有'收起'：走开/复活后它会一直挂在那具尸体原来的位置");
        // 与非尸体分支互斥：活着的怪不该出现"按 G 拾取"
        Assert.Contains("if (attrs != null && attrs.IsDead && !isPlayerLike)", nameplate);
    }

    [Fact]
    public void 尸体拾取的客户端入口在源码里全程可查()
    {
        // ① 发包口：G 键点下去要有东西发出去
        var reporter = ClientFile("Combat", "CombatReporter.cs");
        Assert.Contains("public static void ReportCorpseLoot(ulong localCharacterId", reporter);
        Assert.Contains("CorpseLootTakeRequestPacket", reporter);

        // ② 快照状态位 → 选中标记（每快照无条件搬：窗口到期时 dead/evade 不翻转）
        Assert.Contains("public bool CorpseInteractable", ClientFile("Selection", "SelectableEntityTag.cs"));
        Assert.Contains("stateTag.CorpseInteractable = stateAuth.IsCorpseInteractable",
                        ClientFile("FlaxActorSyncSystem.cs"));

        // ③ G 键分支 + 回执/到期提示的挂载点与消费点
        var selection = ClientFile("Selection", "WorldSelectionService.cs");
        Assert.Contains("tag.CorpseInteractable", selection);
        Assert.Contains("ReportCorpseLoot(", selection);
        Assert.Contains("NotifyCorpseLootResult", selection);
        Assert.Contains("NotifyCorpseLootExpired", selection);

        // ④ 收包事件与分析 + 事件入口扫 CorpseLootExpired
        var handler = ClientFile("Network", "MessageHandlers", "SyncPacketMessageHandler.cs");
        Assert.Contains("CorpseLootResultReceived", handler);
        Assert.Contains("case CorpseLootTakeResultPacket", handler);
        var game = ClientFile("HundunWorldGame.cs");
        Assert.Contains("SyncEventKind.CorpseLootExpired", game);
        Assert.Contains("NotifyCorpseLootExpired", game);

        // ⑤ 状态位只有一处定义（位号写错会静默跟别的位撞车）
        var bitsPath = Path.Combine(RepoRoot(), "Horizon.Game.Message", "Sync", "Components", "SyncComponents.cs");
        var bits = File.ReadAllText(bitsPath);
        Assert.Equal(1, CountOccurrences(bits, "CorpseInteractable = 1u << 8"));
        Assert.Equal(1, CountOccurrences(bits, "public bool IsCorpseInteractable"));

        // ⑥ 记号总数：10 处（漏一半也要红；"幂等跳过"只说明有旧插入体，不足以证明补全了）
        var total = 0;
        foreach (var f in new[]
                 {
                     new[] { "Combat", "CombatReporter.cs" },
                     new[] { "Selection", "SelectableEntityTag.cs" },
                     new[] { "FlaxActorSyncSystem.cs" },
                     new[] { "Selection", "WorldSelectionService.cs" },
                     new[] { "Network", "MessageHandlers", "SyncPacketMessageHandler.cs" },
                     new[] { "HundunWorldGame.cs" },
                 })
        {
            total += CountOccurrences(ClientFile(f), Marker);
        }
        Assert.Equal(10, total);
    }
}
