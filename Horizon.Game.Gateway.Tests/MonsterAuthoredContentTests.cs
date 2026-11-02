using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Sim;
using Horizon.Game.Sim.Monsters;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 「内容形状 → 运行期行为」这一段的判据。
/// <para>
/// 前面几轮已经把三段各自钉过：库里逐格往返（<c>Tools/db-roundtrip-check.py</c>，124 格）、
/// 包与库逐格相同（同脚本第二跳，121 格）、装配回执与摘要行数对账。而那三段用的都是**哨兵值**
/// ——每格填一个互不相同的数，只证明"这一格搬得动"，证明不了"搬过去之后那只怪真的按这一格行事"。
/// </para>
/// <para>
/// 这里换成 <c>Tools/author-mob-*.py</c> 按 AzerothCore 实测形状写进排练库、再由
/// <c>export-content</c> 冻结进来的那一份真内容（<c>authored_rehearsal_content.json</c>），
/// 问三件运行期的事：
/// ① 「会读条但不走图」那一格组合（<c>aiProfile=1 + flagUseSkills=1 + flagUseNavMesh=0</c>）
///    展开成了什么——这个组合档位表里**没有**，只能靠逐位覆盖表达；少认一位的表现是
///    "怪要么全不会施法，要么去查一张不存在的导航图"；
/// ② 那条 13 点环线的几何**就是量出来的那几个数**（点数 13、步长 ≈13 米、闭合、半径贴着 leash）；
/// ③ 装配出来的线在 v1 那条路上**真的被走下去**，而同一只怪没有那条线时走不出同样的轨迹
///    （正反两半只差"有没有这条线"，否则"动了"可能只是游荡）。
/// </para>
/// </summary>
[Collection("monster-content")]
public class MonsterAuthoredContentTests
{
    /// <summary>临终台词那条用例的现场读数（红了先看它：mob / 剩余血 / 最近一发攻击结算 / 收到的全部喊话）。</summary>
    private string _lastKillProbe = "-";
    private static string RepoRoot()
    {
        // 与同目录那批用例同一个标记：向上找 .git 所在处（不是 global.json——那个不在仓库根）。
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git"))) dir = dir.Parent;
        Assert.True(dir != null, "找不到仓库根（.git）");
        return dir!.FullName;
    }

    private const string FixtureName = "authored_rehearsal_content.json";

    private static string Fixture(string name) => Path.Combine(RepoRoot(), "HundunWorld", "Data", "Monsters", name);

    private static MonsterContentLoadReceipt Load()
    {
        MonsterContent.ResetToHardcoded();
        var receipt = MonsterContentLoader.LoadFile(Fixture(FixtureName));
        Assert.True(receipt.Ok, $"{FixtureName} 装不进去了：{receipt.Describe()} | " +
                               string.Join(";", receipt.Problems) +
                               "（这份是排练库 export-content 的冻结件；要么内容真的坏了，要么该重新冻结一次）");
        return receipt;
    }

    [Fact]
    public void 排练库那份内容装配后会读条但不走图这一格组合落到运行期()
    {
        var receipt = Load();
        try
        {
            Assert.Empty(receipt.Problems);

            // ① 冥羽鸩(3)：域 4（火）、档位 1 + 只把技能位打开
            Assert.True(MonsterContent.TryGetConfig(3, out var mob3) && mob3 != null);
            var f3 = mob3!.Behavior.Flags;
            Assert.True(f3.UseThreatTable, "威胁位该开着（切片 56 起内容里显式覆盖成 1，不再只靠档位 1）");
            Assert.True(f3.UseSkills, "flagUseSkills=1 是这份内容里唯一的「会读条」来源；它没展开成开，玩家的施法条永远不出现");
            Assert.False(f3.UseNavigation, "导航位被显式关着（World 没有 NavMesh actor）；开着就是去查一张不存在的图");
            // 求援位（切片 56 的第一份内容）。这条合取是**本作**的实现形状：AC 的 `CallAssistance`
            // 每次进战自动触发、不需要威胁表，而内核把求援整段写在威胁表分支里
            // （`MonsterValidator.cs:270` 那条告警就是在报这个合取）⇒ 内容必须两位一起开才叫得动人。
            Assert.True(f3.UseAssist, "求援位没展开成开：排练库里那 5 只普通怪已经配了位，"
                                      + "这一格到不了运行期就等于「拉一只只来一只」照旧");
            Assert.Equal(9.144f, mob3.Behavior.AssistRadius, 3);   // AC 的 10 码换算到本作的"米"

            Assert.Single(mob3.Skills);
            var mount = mob3.Skills[0];
            // 这四格全部来自本库的技能模板（4001 烈焰掌：施法 0.3 秒 / 冷却 7 秒 / 射程 8 米），
            // 不是脚本拍的数——所以这里钉的是「模板那五格真的穿过 库→包→运行时 落到了挂载上」。
            Assert.Equal(4001, mount.SkillId);
            Assert.Equal(300, mount.CastMs);
            Assert.Equal(7000, mount.CooldownMs);
            Assert.Equal(8f, mount.MaxRange);
            Assert.Equal(10000, mount.ChanceBasisPoints);

            // 掉落：一表 5 件、不掉钱（带钱的是 4/5/11 号）
            var lb3 = MonsterContent.LootBindings(3);
            Assert.Single(lb3);
            Assert.Equal(4, lb3[0].LootTableId);          // 域 4 ⇒ 表号 4（与域推导同号）
            Assert.Equal(5, lb3[0].Entries.Length);
            Assert.Equal(0, lb3[0].MoneyMin);
            Assert.Equal(0, lb3[0].MoneyMax);
            Assert.Equal(3, lb3[0].Entries.Count(e => e.GroupId > 0));   // 普通怪那一组 3 行（AC：组内合计中位 10）

            // ② 世界 BOSS：两式各 50%（chance 量不到 ⇒ 均分）+ 表号走 100+域
            Assert.True(MonsterContent.TryGetConfig(6, out var boss6) && boss6 != null);
            Assert.Equal(2, boss6!.Skills.Length);
            Assert.All(boss6.Skills, s => Assert.Equal(5000, s.ChanceBasisPoints));
            Assert.Equal(new[] { 1002, 1001 }, boss6.Skills.Select(s => s.SkillId).ToArray());
            Assert.Equal(101, MonsterContent.LootBindings(6)[0].LootTableId);
            var lb11 = MonsterContent.LootBindings(11);
            Assert.Equal(4776, lb11[0].MoneyMin);
            Assert.Equal(10859, lb11[0].MoneyMax);
            Assert.Equal(100, lb11[0].LootTableId);        // 无域世界 BOSS 落总表 100

            // ③ 那条线：13 点、回环、**不查图**
            var route = MonsterContent.Route(3);
            Assert.NotNull(route);
            Assert.Equal(13, route!.Value.Points.Length);
            Assert.Equal(MonsterPatrolMode.Loop, route.Value.Mode);
            Assert.False(route.Value.UseNavMesh, "线上也写着不查图：与 flagUseNavMesh=0 同口径，否则「这条线走不走图」又分两派");
            Assert.Equal(0f, route.Value.PauseAtPointSeconds);   // AC 实测 97.8% 的点不停
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 装配出来那条环线在v1上真的被走下去而没有线时走不出同样的轨迹()
    {
        const float cx = 140f, cy = 0f;                    // 刷怪点中心（脚本按它画的圆）
        static List<(float X, float Y)> Walk(MonsterPatrolRoute? line, int ticks)
        {
            var cfg = MonsterAiConfig.Default;
            var self = new MonsterAiSelf
            {
                State = MonsterAiState.Patrol, X = 166.897f, Y = cy, Z = 0f,
                SpawnX = cx, SpawnY = cy, SpawnZ = 0f, Hp = 100,
                StateElapsedSeconds = 10f, PatrolDirection = 1f,
            };
            var none = Array.Empty<MonsterAiTarget>();
            var path = new List<(float, float)>();
            for (var i = 0; i < ticks; i++)
            {
                var d = MonsterAiCore.Decide(self, none, 1f / 60f, cfg, 2000L + i, 60, line);
                self.X += d.DeltaX;
                self.Y += d.DeltaY;
                self.State = d.NextState;
                self.Patrol = d.Patrol;                    // 宿主就是这一句的语义（无条件写回）
                self.StateElapsedSeconds += 1f / 60f;
                path.Add((self.X, self.Y));
            }
            return path;
        }

        Load();
        try
        {
            var line = MonsterContent.Route(3);
            Assert.NotNull(line);
            var walked = Walk(line!, 900);                 // 15 秒：按 walk_speed 2.5 米/秒约 37 米

            var moved = 0f;
            for (var i = 1; i < walked.Count; i++)
                moved += MathF.Abs(walked[i].X - walked[i - 1].X) + MathF.Abs(walked[i].Y - walked[i - 1].Y);
            Assert.True(moved > 20f, $"这条 13 点环线装配之后没被走（累计位移 {moved:0.##} 米）");

            // 朝第二个点那一侧走（point2 的 y=+12.5）——不是朝中心、也不是原地抖
            Assert.True(walked[^1].Y > 5f, $"末点 y={walked[^1].Y:0.##}：没朝第二个点那侧去");

            // 整段都贴着那个环：半径 26.9 的圆，走弦时最小到 26.9×cos(π/13)≈26.1
            var radii = walked.Select(p => MathF.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy))).ToList();
            Assert.True(radii.Min() > 24f && radii.Max() < 29f,
                        $"脱环了：半径最小 {radii.Min():0.##}、最大 {radii.Max():0.##}（该在 24..29 之间）");

            // 反向对照：**同一只怪、同一份 cfg，只把那条线拿掉** ⇒ 走不出同样的轨迹。
            // 阈值不用字面米数，而拿**同一个 cfg 的缺省游荡半径**去量：写死 5 米会在
            // "缺省 wander_radius=6" 这种真实数字上一次就红，而那次的红既不是线也不是产品的问题。
            var wanderRadius = MonsterAiConfig.Default.WanderRadius;
            var lineSwing = walked.Max(p => MathF.Abs(p.Y - cy));
            var noLineSwing = Walk(null, 900).Max(p => MathF.Abs(p.Y - cy));
            Assert.True(lineSwing > wanderRadius * 2f,
                        $"给了线也只在 {lineSwing:0.##} 米内晃（缺省游荡半径 {wanderRadius:0.#}）⇒ 这条线没被走");
            Assert.True(noLineSwing <= wanderRadius * 1.5f,
                        $"没给线却晃到 {noLineSwing:0.##} 米（游荡半径 {wanderRadius:0.#}）⇒ 上面那条不能归因给这条线");
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 环线几何就是AC量出来的那几个数()
    {
        Load();
        try
        {
            var pts = MonsterContent.Route(3)!.Value.Points;
            Assert.Equal(13, pts.Length);

            // 相邻点（含首尾闭合那一段）的步长：AC 实测中位 13.0 米（14.2 码），四分位 7.2/13.0/23.5
            var steps = new List<float>();
            for (var i = 0; i < pts.Length; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Length];
                steps.Add(MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)));
            }
            Assert.All(steps, s => Assert.True(s > 12f && s < 14f, $"步长 {s:0.###} 米不在实测的 13 米档上"));
            // 防空转：这 13 段必须真的都在，而不是循环体一次没进
            Assert.Equal(13, steps.Count);

            // 每点都贴在同一只怪 leash 得住的环上：半径 = 点数×步长/(2π) ≈ 26.9 米 < leash 30
            var radii = pts.Select(p => MathF.Sqrt((p.X - 140f) * (p.X - 140f) + p.Y * p.Y)).ToList();
            Assert.True(radii.Min() > 26f && radii.Max() < 27.5f,
                        $"半径散了：{radii.Min():0.###}..{radii.Max():0.###}（该贴着 26.9）");

            // 逐点不停（AC 那 97.8%）：>=0 才是"配了 0 秒"，-1 是"没配、用路线默认值"
            Assert.All(pts, p => Assert.Equal(0f, p.PauseSeconds));
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 那条线在真DS世界里被走到而巡逻到点计数跟着涨()
    {
        // 为什么还要这一条：上面两条量的是"内核拿到这条线会怎么走"，而真 DS 世界里还隔着
        // 刷怪点解析路线、Idle→Patrol 的等待秒数、停下时被拖到点上这几层。
        // 计数刻意挂在**唯一的游标写回处**（宿主那句无条件 `self.Patrol = decision.Patrol`），
        // 所以这里红的就是"没人走线"，而不是"走线判据分了两派"。
        Load();
        try
        {
            // 走 DsHost 真正那条装配口（内容包里的刷怪点 → MonsterSpawnPointBuilder），
            // 而不是手搓一个刷怪点：这条要问的就是"库里那份内容进了世界之后"。
            var receipt = Horizon.Game.Core.Sim.Server.MonsterSpawnPointBuilder.Build(-1);
            Assert.Empty(receipt.Problems);          // 每个点都该拿到内容，否则这条链根本没跑成

            var routed = RunWorld(receipt, templateId: 3, ticks: 1800);   // 30 秒：每段 13 米、走速 2.5 米/秒 ⇒ 约 5 段
            Assert.True(routed >= 2,
                        $"配了 13 点环线的那只怪 30 秒里到点 {routed} 次（期望 ≥2）：那条线没在世界里被走");

            // 反向对照：同一份世界、同样的 tick 数，只换成一只有刷怪点而**没有线**的怪 ⇒ 计数恒 0。
            // 少了这一半，"计数在涨"可能只是任何一次状态迁移都被算成到点。
            var unrouted = RunWorld(receipt, templateId: 1, ticks: 1800);
            Assert.Equal(0, unrouted);
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 装配回执有问题时必须把问题逐条念出来而不是只报个数()
    {
        // 触发这件事的现场：生产包在 DS 启动日志里是 `问题=1`，而 CLI 的导出回执 problems=[]。
        // DS 校验快照文件、CLI 校验库里的草稿，两份清单不同源 ⇒ 那一条内容到底是什么问题，
        // 在 DS 自己的日志里查不出来。计数会红，但红了也没人知道该改哪一格。
        var clean = new MonsterContentLoadReceipt { ContentRevision = 1, Templates = 2 };
        Assert.DoesNotContain("明细=", clean.Describe());
        Assert.Contains("问题=0", clean.Describe());

        var one = new MonsterContentLoadReceipt { ContentRevision = 1, Templates = 2 };
        one.Problems.Add("刷怪点 91 指向未发布的模板 7");
        Assert.Contains("问题=1", one.Describe());
        Assert.Contains("刷怪点 91 指向未发布的模板 7", one.Describe());

        var four = new MonsterContentLoadReceipt { ContentRevision = 1, Templates = 2 };
        for (var i = 1; i <= 4; i++) four.Problems.Add($"第{i}条问题");
        var text = four.Describe();
        Assert.Contains("问题=4", text);
        Assert.Contains("第1条问题", text);
        Assert.Contains("第3条问题", text);
        Assert.DoesNotContain("第4条问题", text);
        // 截断必须自己承认截断了：只列 3 条而计数是 4，不写"另有"就是这一行在说谎
        Assert.Contains("另有 1 条", text);
    }

    [Fact]
    public void 包内引用对账这条判据会红而导出回执与DS念的是同一份清单()
    {
        // 现场：生产库里模板 4 的行为表指向巡逻线 1，而路线 1 没进包（只装已发布的行）。
        // DS 装载时念得出这条，CLI 的导出回执却报 problems=[] —— 因为导出侧从没跑过包内对账。
        // 两份清单不同源时，"编辑器里配了线"与"游戏里那只怪在游荡"永远对不上账。
        var snapshot = MonsterContentSnapshot.FromJson(
            File.ReadAllText(Fixture("authored_rehearsal_content.json")));
        Assert.NotNull(snapshot);

        // 夹具本身必须是干净的：否则下面"改一格就转红"没有基线可比
        snapshot.ToBundle(out var baseline);
        Assert.Empty(baseline);

        var routed = snapshot.Templates.Find(t => t.Behavior.PatrolRouteId != 0);
        Assert.NotNull(routed);       // 夹具里必须有走线的怪，否则这条判据在测空集
        var missing = 777;
        Assert.True(snapshot.Routes.Find(r => r.RouteId == missing) == null,
                    "夹具里已经有 777 号线：这条用例的悬空引用是假的");
        routed!.Behavior.PatrolRouteId = missing;
        snapshot.ToBundle(out var problems);
        Assert.Single(problems);
        Assert.Contains("巡逻线", problems[0]);
        Assert.Contains("777", problems[0]);

        // 导出侧必须**调同一个判据**并把结果并进它自己那份 problems（消费者只问 problems）
        var cli = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.MonsterCli", "Program.cs"));
        Assert.Contains("ToBundle(out var assemblyProblems)", cli);
        Assert.Contains("[\"assemblyProblems\"]", cli);
        Assert.Contains("[\"problems\"] = problems", cli);
        Assert.DoesNotContain("[\"problems\"] = snapshot.Problems", cli);
    }

    [Fact]
    public void 导航网格的装载回执数得出挡了多少格而全空的图要自己承认等于没装()
    {
        // 尺寸只证明"文件读到了"；**不可走格数**才证明"它挡了东西"。
        // 一张全 10 的网格是合法文件、IsValid 为真、路径查询全部返回直线，
        // 于是"开了导航位的怪还是只会走直线"与"导航图没装"在玩家那边同形，而日志原本只报尺寸。
        var dir = Path.Combine(Path.GetTempPath(), "hzn-nav-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            const int w = 6, h = 4;
            var rows = new[]
            {
                "10 10 0 10 10 10",
                "10 10 0 20 10 10",
                "10 10 0 20 10 10",
                "10 10 0 10 10 10",
            };
            var path = Path.Combine(dir, "wall.monsternav");
            File.WriteAllText(path, string.Join(Environment.NewLine,
                new[] { $"MONSTERNAV {w} {h} 2.5 100 -30" }.Concat(rows)) + Environment.NewLine);

            var grid = MonsterNavGridLoader.Load(path, out var error);
            Assert.True(string.IsNullOrEmpty(error), error);
            Assert.True(grid.IsValid, "网格自洽性没过：后面的计数都是在数一个没人用的对象");
            Assert.Equal(2.5, grid.CellSize, 3);
            Assert.Equal(100.0, grid.OriginX, 3);
            Assert.Equal(-30.0, grid.OriginY, 3);

            // 期望值**独立数一遍**：直接从文件文本数 token，不调 Census（拿被测物证被测物等于没测）
            var tokens = rows.SelectMany(r => r.Split(' ')).ToArray();
            var census = grid.Census();
            Assert.Equal(tokens.Count(t => t == "0"), census.Blocked);
            Assert.Equal(tokens.Count(t => t == "20"), census.Hard);
            Assert.Equal(w * h, census.Total);
            Assert.Equal(4, census.Blocked);       // 防空转：这份夹具必须真的挡了 4 格

            // 合法但什么都没挡住的空图：IsValid 仍为真，普查必须把它和"装了墙"分开
            var open = Path.Combine(dir, "open.monsternav");
            File.WriteAllText(open, string.Join(Environment.NewLine,
                new[] { $"MONSTERNAV {w} {h} 1 0 0" }
                    .Concat(Enumerable.Repeat(string.Join(' ', Enumerable.Repeat("10", w)), h)))
                + Environment.NewLine);
            var openGrid = MonsterNavGridLoader.Load(open, out var openError);
            Assert.True(string.IsNullOrEmpty(openError), openError);
            Assert.True(openGrid.IsValid);
            Assert.Equal(0, openGrid.Census().Blocked);

            // 三种读数各断一条（判据住在装载器旁边，宿主只念它）：
            var walled = MonsterNavGridLoader.DescribeLoad(grid, error);
            Assert.Contains("不可走格=4/24", walled);
            Assert.Contains("难行格=2", walled);
            Assert.DoesNotContain("等价于没装", walled);          // 装了且挡得住 ⇒ 不许乱报警
            Assert.Contains("等价于没装", MonsterNavGridLoader.DescribeLoad(openGrid, openError));
            Assert.Contains("未装入", MonsterNavGridLoader.DescribeLoad(new MonsterNavGrid(), "文件不存在：x"));

            // 宿主必须念这一个函数（自己再拼一份字符串 = 第二份清单，迟早与这里分家）
            var host = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.DsHost", "Program.cs"));
            Assert.Contains("MonsterNavGridLoader.DescribeLoad(navGrid, navError)", host);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* 临时目录清不掉不影响判据 */ }
        }
    }

    [Fact]
    public void 进战喊话真的发一条只带文本号的事件而悬空指针各念一次()
    {
        // 目标里"行为"那一格最后一处悬空：aggro_say_id 以前是"配了没人读"的死列。
        // 这一条同时问三件事：① 文本行真的进了包（HasText 认得）；② 进战那一拍真的下发一条
        // 且**只一条**（边沿对账，不是每拍一条）；③ 号悬空时装配期与运行期各自都要念出来。
        var snapshot = MonsterContentSnapshot.FromJson(
            File.ReadAllText(Fixture("authored_rehearsal_content.json")));
        Assert.NotNull(snapshot);
        var mob3 = snapshot!.Templates.Find(t => t.MonsterId == 3);
        Assert.NotNull(mob3);
        mob3!.AggroSayId = 77;
        snapshot.Texts.Add(new MonsterTextSnapshotDto
        {
            MonsterTemplateId = 3, SayId = 77, Group = 0, Seq = 0, Text = "测试文本", Emote = 1,
        });

        var bundle = snapshot.ToBundle(out var problems);
        Assert.Empty(problems);
        Assert.True(bundle.HasText(3, 77), "文本行没进包：HasText 恒假，这一格还是没人读");
        Assert.False(bundle.HasText(3, 78), "没配过的号居然也算有：这条判据在测空集");

        // 悬空引用：号配了而包里没有那一号 ⇒ 装配期就要念（与巡逻线那条同一个形状）
        mob3.AggroSayId = 999;
        snapshot.ToBundle(out var dangling);
        Assert.Contains(dangling, p => p.Contains("起手喊话指向文本 999"));

        MonsterContent.Install(bundle);
        try
        {
            var (packets, sayEvents, missEvents, tailState, _) = RunSay(templateId: 3, expectSayId: 77);
            Assert.Equal(1, packets);            // 一条，且只一条（边沿对账，不是每拍一条）
            Assert.Equal(1, sayEvents);
            Assert.Equal(0, missEvents);
            Assert.True(tailState.Contains("Chase") || tailState.Contains("Attack"), tailState);
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }

        // 运行期的漂移：包已装好、号却被换掉（装配期那次检查已经过去了）⇒ 只计数、不发空号
        var drifted = snapshot.ToBundle(out _);
        drifted.ByTemplateId[3].AggroSayId = 4242;
        MonsterContent.Install(drifted);
        try
        {
            var (packets, sayEvents, missEvents, _, _) = RunSay(templateId: 3, expectSayId: 0);
            Assert.Equal(0, packets);
            Assert.Equal(0, sayEvents);
            Assert.True(missEvents >= 1, $"号悬空却一次都没计数 ⇒ 这条漂移在运行期是静默的（{missEvents}）");
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }

        // 组内多句（AC `creature_text` 的 group）：一句一组时服务端只可能念行为表那一号，
        // 而这一层的意义就是"同一事件组写三句、每次进战挑一句"。这里把 77/78/79 放进同一组，
        // 断言下发的那一号**在组里**（挑的是真实存在的号，客户端按 (modelKey, sayId) 查得到文案），
        // 且服务端确实走的是挑号这一层——通道里不许再出现"只查有无"的 HasText。
        var grouped = MonsterContentSnapshot.FromJson(
            File.ReadAllText(Fixture("authored_rehearsal_content.json")));
        var g3 = grouped!.Templates.Find(t => t.MonsterId == 3);
        Assert.NotNull(g3);
        g3!.AggroSayId = 77;
        grouped.Texts.RemoveAll(t => t.MonsterTemplateId == 3);
        grouped.Texts.Add(new MonsterTextSnapshotDto { MonsterTemplateId = 3, SayId = 77, Group = 7, Seq = 1, Text = "受死吧" });
        grouped.Texts.Add(new MonsterTextSnapshotDto { MonsterTemplateId = 3, SayId = 78, Group = 7, Seq = 2, Text = "又一个送死的" });
        grouped.Texts.Add(new MonsterTextSnapshotDto { MonsterTemplateId = 3, SayId = 79, Group = 7, Seq = 3, Text = "血肉之躯" });
        var groupBundle = grouped.ToBundle(out var groupProblems);
        Assert.Empty(groupProblems);
        MonsterContent.Install(groupBundle);
        try
        {
            var (gPackets, gEvents, gMisses, _, gSayIds) = RunSay(templateId: 3, expectSayId: 0);
            Assert.Equal(1, gPackets);
            Assert.Equal(1, gEvents);
            Assert.Equal(0, gMisses);
            Assert.Single(gSayIds);
            Assert.Contains(gSayIds[0], new[] { 77, 78, 79 });
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }

        // 机制层：喊话通路问的必须是"挑哪一号"，而不是"有没有这一号"（后者永远发行为表那一格）。
        var session = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.Game.Core", "Sim", "Server", "DsWorldSession.cs"));
        Assert.Contains("MonsterContent.ResolveSayId(", session);
        Assert.DoesNotContain("MonsterContent.HasText(", session);

        // 摘要必须念得出这两格（只有进程内计数器 = 运行期还是问不出来）
        var runner = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.DsHost", "DsHostRunner.cs"));
        Assert.Contains("喊话={_world.MonsterSayEvents}", runner);
        Assert.Contains("喊话指针落空={_world.MonsterSayPointerMisses}", runner);
    }

    [Fact]
    public void 怪临死会念死亡组那一句而没配死亡组就一声不出()
    {
        // 死亡喊话与进战喊话走同一条事件通路（只发号），区别只在"哪一拍"与"从哪一组挑"。
        // 两个半份：配了死亡组 → 临死恰发一条、号在组内、且**不是**进战那一号；没配 → 一声不出（行为零变化），
        // 而进战那一句在两种情形里都照发（否则"没发"可能只是喊话通路整个坏了）。
        (int Aggro, int Death, int SayEvents, bool Alive, List<int> SayIds) Run(bool withDeathGroup)
        {
            const int templateId = 15501;
            var behavior = MonsterBehaviorConfig.Default;
            behavior.IsAggressive = true;
            behavior.AggroRadius = 30f;
            behavior.AggroConeDegrees = 360f;
            behavior.LeashRadius = 200f;
            behavior.MeleeSwingSeconds = 1.5f;

            var bundle = new MonsterContentBundle { ContentRevision = 1 };
            bundle.ByTemplateId[templateId] = new MonsterRuntimeConfig
            {
                TemplateId = templateId, Name = "临终台词怪", Rank = MonsterRank.Normal,
                LevelBase = 10, LevelMin = 10, LevelMax = 10, AggroSayId = 1, CorpseSeconds = 30f,
                RespawnBaseSeconds = 60f, RespawnPerLevelSeconds = 0f,
                Stats = new MonsterStatsConfig { HpBase = 1000, HpPerLevel = 0, AtkBase = 1, AtkPerLevel = 0 },
                Xp = new MonsterXpConfig(45, 5, new[] { 1.0 }),
                Behavior = behavior,
            };
            bundle.Texts[templateId] = new List<MonsterTextSnapshot>
            {
                new MonsterTextSnapshot { SayId = 1, Group = 0, Seq = 0, Text = "进战那句" },
            };
            if (withDeathGroup)
            {
                bundle.Texts[templateId].Add(new MonsterTextSnapshot { SayId = 2, Group = MonsterContent.DeathSayGroup, Seq = 1, Text = "不甘" });
                bundle.Texts[templateId].Add(new MonsterTextSnapshot { SayId = 3, Group = MonsterContent.DeathSayGroup, Seq = 2, Text = "休想" });
            }
            MonsterContent.Install(bundle);
            try
            {
                var point = SpawnPointConfig.Default;
                point.SpawnPointId = 9511;
                point.MonsterTemplateId = templateId;
                point.MaxAlive = 1;
                point.X = 0f; point.Y = 0f; point.Z = 0f;
                point.MaxHp = 1000;   // 一刀 100 伤害：要砍十刀才死，进战那一拍与死亡那一拍才分得开
                point.AggroRadius = 30f;
                point.RespawnDelaySeconds = 1f;

                var world = new DsWorldSession(new DsWorldSessionOptions
                {
                    ShardId = 0,
                    TicksPerSecond = 60,
                    SnapshotIntervalTicks = 600,
                    PlayerAttackPower = 100,       // 一刀 100 伤害 × 怪物 100 血 ⇒ 首刀即死
                    PlayerDefaultMaxHp = 100000,
                });
                world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
                Assert.True(world.AddPlayer(94101, 94101, -2f, 0f, 0f, out var why, "乙", 30), why);

                // **从第 0 拍就开始收事件**：怪物头几拍就进战并喊话，先空跑一段再开始扫描
                // 会把进战那句留在窗口外（上一版正是这样，读到 sayEvents=1 而进战那句计数是 0）。
                ulong mob = 0UL;
                var said = new List<(ulong Source, int SayId)>();
                var lastOutcome = "-";
                for (var tick = 0; tick < 3600; tick++)
                {
                    if (mob == 0UL)
                    {
                        for (var slot = 0; slot < 4 && mob == 0UL; slot++)
                        {
                            var candidate = SpawnDirectorCore.ComputeEntityId(point.SpawnPointId, slot);
                            if (world.TryGetMonster(candidate, out var found) && found != null) mob = candidate;
                        }
                    }
                    else if (tick % 61 == 0)
                    {
                        var o = world.SubmitCombatAction(94101, new Horizon.Game.Message.Sync.CombatActionPacket
                        {
                            AttackerId = 94101UL, TargetId = mob,
                            ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
                        });
                        lastOutcome = o.Outcome + "/" + o.Damage;
                    }
                    var r = world.Tick();
                    foreach (var (_, packet) in r.Events)
                    {
                        if (packet is not Horizon.Game.Message.Sync.EventPacket ep || ep.Events is null) continue;
                        foreach (var e in ep.Events)
                        {
                            if (e.Kind == Horizon.Game.Message.Sync.SyncEventKind.MonsterSay)
                                said.Add((e.SourceEntityId, e.IntValue));
                        }
                    }
                    if (world.MonsterDeathSayEvents > 0) break;   // 已经念过临终台词就不必再跑
                }
                Assert.True(mob != 0UL, "怪没生成：这条测的是临终台词，不是刷怪");
                var alive = world.TryGetMonster(mob, out var tail) && tail != null && tail.Ai.Hp > 0;
                var hpTail = world.TryGetMonster(mob, out var t2) && t2 != null ? t2.Ai.Hp : -1f;
                _lastKillProbe = $"mob={mob} hp={hpTail} 攻={lastOutcome} 全部喊话=[{string.Join(",", said.Select(x => x.Source + ":" + x.SayId))}]";
                var mine = said.Where(x => x.Source == mob).Select(x => x.SayId).ToList();
                return (mine.Count(id => id == 1), world.MonsterDeathSayEvents, world.MonsterSayEvents, alive, mine);
            }
            finally
            {
                MonsterContent.ResetToHardcoded();
            }
        }

        var withDeath = Run(withDeathGroup: true);
        Assert.True(withDeath.Aggro >= 1,
                    $"进战那句都没发（sayEvents={withDeath.SayEvents} death={withDeath.Death} 活着={withDeath.Alive} " +
                    $"念过的号=[{string.Join(",", withDeath.SayIds)}] 现场={_lastKillProbe}）：喊话通路本身坏了，下面两半都算不得数");
        Assert.True(withDeath.Death == 1, $"临终台词发了 {withDeath.Death} 条（现场={_lastKillProbe}）：配了死亡组就该恰发一条");
        var deathIds = withDeath.SayIds.Where(id => id != 1).ToList();
        Assert.Single(deathIds);
        Assert.Contains(deathIds[0], new[] { 2, 3 });

        var withoutDeath = Run(withDeathGroup: false);
        Assert.True(withoutDeath.Aggro >= 1,
                    $"没配死亡组的怪连进战都不喊了（sayEvents={withoutDeath.SayEvents} death={withoutDeath.Death} " +
                    $"活着={withoutDeath.Alive}）：这一半在测空集");
        Assert.Equal(0, withoutDeath.Death);
    }

    /// <summary>
    /// 一只怪 + 一个 4 米外的玩家，跑 400 拍，数 <c>MonsterSay</c> 事件。
    /// <para>刻意**不打它**：这一条测的是"进战要不要喊"，攻击会把状态推进到 Attack 并掺进施法通路。</para>
    /// </summary>
    private static (int Packets, int SayEvents, int PointerMisses, string TailState, List<int> SayIds) RunSay(int templateId, int expectSayId)
    {
        var point = SpawnPointConfig.Default;
        point.SpawnPointId = 9500 + templateId;
        point.MonsterTemplateId = templateId;
        point.MaxAlive = 1;
        point.X = 0f; point.Y = 0f; point.Z = 0f;
        point.MaxHp = 5000;                      // 这一条不测击杀：别让它半路死掉
        point.AggroRadius = 30f;
        point.RespawnDelaySeconds = 1f;

        var world = new Horizon.Game.Core.Sim.Server.DsWorldSession(
            new Horizon.Game.Core.Sim.Server.DsWorldSessionOptions
            {
                ShardId = 0,
                TicksPerSecond = 60,
                SnapshotIntervalTicks = 600,
                PlayerDefaultMaxHp = 100000,
            });
        world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
        Assert.True(world.AddPlayer(95001, 95001, -4f, 0f, 0f, out var why, "喊话证人", 30), why);
        var packets = 0;
        var mob = 0UL;
        var observed = new List<(ulong Source, int SayId)>();
        // **一拍都不许跳过**：怪在头几拍就进战，喊话那一条也在那一拍发出去。
        // 第一版这里先"空跑 5 拍找怪"再开始扫描，于是计数说"发了 1 条"而扫描窗口里什么都没有——
        // 我把这读成了产品缺陷（"投递被吞了"），红的是夹具的窗口，不是事件通路。
        for (var tick = 0; tick < 400; tick++)
        {
            if (mob == 0UL)
            {
                for (var slot = 0; slot < 4 && mob == 0UL; slot++)
                {
                    var candidate = SpawnDirectorCore.ComputeEntityId(point.SpawnPointId, slot);
                    if (world.TryGetMonster(candidate, out var found) && found != null) mob = candidate;
                }
            }
            var r = world.Tick();
            foreach (var (_, packet) in r.Events)
            {
                if (packet is not Horizon.Game.Message.Sync.EventPacket ep || ep.Events is null) continue;
                foreach (var e in ep.Events)
                {
                    if (e.Kind != Horizon.Game.Message.Sync.SyncEventKind.MonsterSay) continue;
                    packets++;
                    observed.Add((e.SourceEntityId, e.IntValue));
                }
            }
        }
        Assert.True(mob != 0UL, "怪没生成：这一条测的是喊话，不是刷怪");
        Assert.All(observed, o => Assert.Equal(mob, o.Source));
        // expectSayId > 0 才逐条钉号；<= 0 表示"号由调用方另外判"（组内挑选那种用例）
        if (expectSayId > 0) Assert.All(observed, o => Assert.Equal(expectSayId, o.SayId));
        Assert.True(world.MonsterSayEvents + world.MonsterSayPointerMisses >= 1,
                    "跑了 400 拍，喊话通路一次都没被走到：这条判据在空转");
        var tail = world.TryGetMonster(mob, out var last) && last != null ? last.Ai.State.ToString() : "不在世界";
        return (packets, world.MonsterSayEvents, world.MonsterSayPointerMisses, tail,
                observed.Select(o => o.SayId).ToList());
    }

    private static int RunWorld(
        Horizon.Game.Core.Sim.Server.MonsterSpawnPointBuilder.Receipt contentPoints, int templateId, int ticks)
    {
        var points = contentPoints.Points.FindAll(p => p.MonsterTemplateId == templateId);
        Assert.NotEmpty(points);

        var world = new Horizon.Game.Core.Sim.Server.DsWorldSession(
            new Horizon.Game.Core.Sim.Server.DsWorldSessionOptions
            {
                ShardId = 0,
                TicksPerSecond = 60,
                SnapshotIntervalTicks = 600,
            });
        world.ConfigureSpawnPoints(points);
        for (var i = 0; i < ticks; i++) world.Tick();
        return world.MonsterPatrolPointsReached;
    }

    [Fact]
    public void 排练库那份内容喂进真DS之后击杀真的掷这只怪自己的绑定而会读条的怪真的起手()
    {
        // 效果层，不是机制层：前面几条钉的是"值到了运行期"，这一条问的是
        // **"拿这份内容打一只怪，会不会真的掷它自己的掉落表、掉出东西、掉出钱，
        //   而配了技能的那只会不会真的起手把施法条发出来"**。
        // 这四件事在玩家那边就是"杀怪有收获 / 怪会读条"，是目标里掉落与技能两格的最终形态。
        Load();
        try
        {
            var (kills3, rolled3, items3, money3, casts3) = RunKills(templateId: 3, targetKills: 15);
            Assert.Equal(15, rolled3);        // 每次击杀都掷了这只怪自己的绑定（不是退回全局假表）
            Assert.True(items3 >= 1, $"15 次击杀一件没掉（期望件数 0.2/杀 ⇒ 期望 3 件）：绑定掷了但没出东西");
            Assert.Equal(0L, money3);          // 3 号刻意没配钱；这一半证明"没配就是不掉"，不是漏接线
            Assert.True(casts3 >= 1, "配了 4001（chance 100%、射程 8 米、施法 300ms）的怪一次都没起手 ⇒ 施法条在玩家那边不会出现");

            var (kills4, rolled4, items4, money4, _) = RunKills(templateId: 4, targetKills: 15);
            Assert.Equal(15, rolled4);
            Assert.True(items4 >= 1, $"4 号 15 次击杀一件没掉（items={items4}）");
            Assert.True(money4 >= 1, $"4 号配了 3..15 铜却一次钱都没掉（money={money4}）：金币那一格没接到击杀");
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }

    /// <summary>
    /// 造一个只有一只怪的 DS 世界，让一个 30 级玩家反复把它打死，返回五个累计读数。
    /// <para>攻击按 61 tick 一发：数据面那道 60 tick 边界限流不是 bug，按每拍发会把用例
    /// 变成"在测时序而不是在测掉落"。</para>
    /// </summary>
    private static (int Kills, int BindingsRolled, int ItemsRolled, long MoneyRolled, int CastStarts) RunKills(
        int templateId, int targetKills)
    {
        var point = SpawnPointConfig.Default;
        point.SpawnPointId = 9400 + templateId;
        point.MonsterTemplateId = templateId;
        point.MaxAlive = 1;
        point.X = 0f;
        point.Y = 0f;
        point.Z = 0f;
        point.MaxHp = 60;                        // 让"击杀"这件事便宜：这一条测的是掉落，不是血量公式
        point.AggroRadius = 30f;
        point.RespawnDelaySeconds = 1f;

        var world = new Horizon.Game.Core.Sim.Server.DsWorldSession(
            new Horizon.Game.Core.Sim.Server.DsWorldSessionOptions
            {
                ShardId = 0,
                TicksPerSecond = 60,
                SnapshotIntervalTicks = 600,
                PlayerAttackPower = 100,
                // 玩家血量必须抬高：排练库里 3 号是 26 级怪，默认 100 血的玩家在 ~500 拍内会被它打死
                // （实测 t549 起全部回 AttackerDead）。玩家一死，怪就脱离战斗去走它的巡逻环，
                // 于是后面全变成 OutOfRange——这条用例测的是"击杀掷不掷绑定"，不该测复活与追击。
                PlayerDefaultMaxHp = 100000,
            });
        world.ConfigureSpawnPoints(new List<SpawnPointConfig> { point });
        // 玩家站在目标的 −X 侧：默认朝向 +X，站 +X 等于背对目标，一刀都不会落（夹具坑，别退回）
        Assert.True(world.AddPlayer(94001, 94001, -2f, 0f, 0f, out var why, "甲", 30), why);

        // 实体 id 由刷怪点与槽位算出来；MaxAlive=1 时槽位号取哪个值不在这里猜，扫一遍认出来。
        // 先拍几拍：ConfigureSpawnPoints 只建槽位表，真正 spawn 发生在 Tick 里
        // （刚配完就查一定查不到，红会写成"击杀链路没开始"这种谎话）。
        for (var warmup = 0; warmup < 5; warmup++) world.Tick();
        ulong mob = 0UL;
        for (var slot = 0; slot < 4 && mob == 0UL; slot++)
        {
            var candidate = SpawnDirectorCore.ComputeEntityId(point.SpawnPointId, slot);
            if (world.TryGetMonster(candidate, out var found) && found != null) mob = candidate;
        }
        Assert.True(mob != 0UL, $"没找到模板 {templateId} 的那只怪（刷怪点 {point.SpawnPointId}）：击杀链路根本没开始");

        var kills = 0;
        var swings = 0;
        var swingReport = new List<string>();
        for (var tick = 0; tick < 61 * (targetKills + 6) * 3 && kills < targetKills; tick++)
        {
            if (tick % 61 == 0)
            {
                var o = world.SubmitCombatAction(94001, new Horizon.Game.Message.Sync.CombatActionPacket
                {
                    AttackerId = 94001UL, TargetId = mob,
                    ActionKind = Horizon.Game.Message.Sync.CombatActionKind.NormalAttack,
                });
                swings++;
                // 只留前 24 发：这一串的用处是"红了以后知道卡在哪一格"，不是日志本身。
                if (swingReport.Count < 24) swingReport.Add($"t{tick}:{o.Outcome}/{o.Damage}");
            }
            var r = world.Tick();
            foreach (var e in r.GameplayEvents)
                if (e.Kind == Horizon.Game.Core.Sim.Server.DsGameplayEventKind.Kill
                    && e.MonsterTemplateId == templateId) kills++;
        }
        // 收工时怪那一侧的读数：alive/hp/坐标。掉落链路对不对，先看"打没打到"这一层。
        var stillThere = world.TryGetMonster(mob, out var tailMob) && tailMob != null;
        var tail = stillThere
            ? $"活={tailMob!.Ai.Hp > 0} hp={tailMob.Ai.Hp:0.##} 位置=({tailMob.Ai.X:0.#},{tailMob.Ai.Y:0.#})"
            : "已不在世界";
        Assert.True(kills >= targetKills,
            $"{templateId} 号打了 {swings} 发只死 {kills}/{targetKills} 次。收尾={tail} 前几发={string.Join(" ", swingReport)}");
        return (kills, world.MonsterLootBindingsRolled, world.MonsterLootItemsRolled,
                world.MonsterLootMoneyRolled, world.MonsterCastStartEvents);
    }

    [Fact]
    public void 排练库那份求援内容在真DS里把隔壁那只同族叫进战斗而关位或半径小于槽距就叫不动()
    {
        // 效果层，接着切片 56 那份内容问：`flagUseAssist=1 + assistRadius=9.144 + flagUseThreat=1`
        // 三格从库里写进去之后，**中间隔着 内容包→快照→刷怪点装配→盟友清单→内核合取** 这五跳，
        // 玩家那边到底是不是"拉一只、隔壁那只也上来"。
        // 三条判据共用同一份几何（模板 1 的刷怪点 1：三只同族沿 X 排在 12/20/28，槽距 8 米），
        // 只差被问的那一格 ⇒ 正例涨了才能归因给那一格，而不是归因给站位或时序。
        const int templateId = 1;
        const int nearPointId = 1;                    // 库里那个 x=20 的刷怪点
        const int fightingIndex = 0;                  // 槽位 0 ⇒ x=12，玩家贴到它 6 米内
        const int idleIndex = 1;                      // 槽位 1 ⇒ x=20，距玩家 14 米，自己够不着

        // 正例同时在冻结件里加一句**应援台词**（组 `AssistSayGroup`）：求援真的成立时它必须被念出来。
        var (seeds, idleThreat, assistSays, diag) = RunAssistScene(templateId, nearPointId, fightingIndex, idleIndex, s =>
            s.Texts.Add(new Horizon.Game.Core.Sim.Server.MonsterTextSnapshotDto
            {
                MonsterTemplateId = templateId, SayId = 31,
                Group = Horizon.Game.Sim.Monsters.MonsterContent.AssistSayGroup, Seq = 1, Text = "来援！",
            }));
        Assert.True(seeds >= 1,
                    $"真 DS 世界里跑了 600 拍，求援种子 {seeds} 次：排练库那份求援内容到不了运行期"
                    + $"（槽距 8 米 < 半径 9.144 米，本该叫得动）。现场读数：{diag}");
        Assert.True(idleThreat != 0UL, $"隔壁那只没被叫起来：它的威胁表一直是空的，"
                    + $"玩家看到的仍然是一只一只单打。现场读数：{diag}");
        Assert.True(assistSays >= 1,
                    $"求援成立但被叫起来的那只一句应援台词都没发（应援计数 {assistSays}）："
                    + "组 3 那一类台词在玩家那边是哑的");

        // 负对照一：只把求援位关掉（几何、物种、威胁位、半径全部不动）⇒ 必须一次都不叫。
        var (offSeeds, offThreat, offSays, offDiag) = RunAssistScene(templateId, nearPointId, fightingIndex, idleIndex, s =>
            s.Templates.Find(t => t.MonsterId == templateId)!.Behavior.FlagUseAssist = 0);
        Assert.Equal(0, offSeeds);
        Assert.Equal(0UL, offThreat);
        Assert.Equal(0, offSays);     // 没被叫起来 ⇒ 应援台词也不许出现（否则"求援关着也在喊"）

        // 负对照二：位开着，但把半径收到槽距以下（4 米 < 8 米）⇒ 叫不动，证明上面那一条涨的是**这一格半径**。
        var (shortSeeds, shortThreat, shortSays, shortDiag) = RunAssistScene(templateId, nearPointId, fightingIndex, idleIndex, s =>
            s.Templates.Find(t => t.MonsterId == templateId)!.Behavior.AssistRadius = 4f);
        Assert.Equal(0, shortSeeds);
        Assert.Equal(0UL, shortThreat);
        Assert.Equal(0, shortSays);
        // 两条对照各自也要"确实跑过这一族"：关掉位之后展开出来的求援位必须是关的，
        // 半径那一格必须真的变成 4——否则"0 次"可能只是那条覆盖没落到运行期，等于没对照。
        Assert.Contains("assist=0", offDiag);
        Assert.Contains("半径=4", shortDiag);
    }

    /// <summary>
    /// 用"冻结件（可选改一格）→ 刷怪点装配 → 真 DsWorldSession"跑一段，返回求援种子次数与
    /// 那只本来闲着的同族最后的威胁目标。
    /// <para>为什么走文件而不是直接改 <c>MonsterContent</c>：这条判据要测的就是装配口，
    /// 手搓一个刷怪点会跳过 <c>MonsterSpawnPointBuilder</c>，那前五跳里就少证了一跳。</para>
    /// </summary>
    private static (int Seeds, ulong IdleThreat, int AssistSays, string Diag) RunAssistScene(
        int templateId, int nearPointId, int fightingIndex, int idleIndex,
        Action<MonsterContentSnapshot>? mutate)
    {
        MonsterContent.ResetToHardcoded();
        var snapshot = MonsterContentSnapshot.FromJson(File.ReadAllText(Fixture(FixtureName)));
        Assert.NotNull(snapshot);
        var mob = snapshot!.Templates.Find(t => t.MonsterId == templateId);
        Assert.NotNull(mob);
        // 把**这一条不测的那一格**钉成 0：游荡会让两只同族在开跑之前就走散（±6 米），
        // 那时"叫不动"到底是半径的问题还是几何散了，这条判据分不出来。槽距 8 米必须保持成库里那个 8 米。
        mob!.Behavior.WanderRadius = 0f;
        mutate?.Invoke(snapshot);
        var path = Path.Combine(Path.GetTempPath(), $"hzn-assist-{Guid.NewGuid().ToString("N")[..8]}.json");
        File.WriteAllText(path, snapshot!.ToJson());
        try
        {
            var load = MonsterContentLoader.LoadFile(path);
            Assert.True(load.Ok, $"改过的内容包装不进去：{load.Describe()} | {string.Join(";", load.Problems)}");
            Assert.Empty(load.Problems);

            var built = Horizon.Game.Core.Sim.Server.MonsterSpawnPointBuilder.Build(-1);
            Assert.Empty(built.Problems);
            var points = built.Points.FindAll(p => p.MonsterTemplateId == templateId);
            Assert.NotEmpty(points);
            Assert.True(points.Exists(p => p.SpawnPointId == nearPointId),
                        $"内容装配出来的刷怪点里没有 {nearPointId}：这条用例的几何前提没了（槽距与站位都对不上）");

            var world = new Horizon.Game.Core.Sim.Server.DsWorldSession(
                new Horizon.Game.Core.Sim.Server.DsWorldSessionOptions
                {
                    ShardId = 0,
                    TicksPerSecond = 60,
                    SnapshotIntervalTicks = 600,
                    PlayerDefaultMaxHp = 100000,    // 玩家不许先死：这条测的是求援，不是复活
                });
            world.ConfigureSpawnPoints(points);
            var idFighting = SpawnDirectorCore.ComputeEntityId(nearPointId, fightingIndex);
            var idIdle = SpawnDirectorCore.ComputeEntityId(nearPointId, idleIndex);

            // 先空跑 6 秒：① 让槽位几何稳定下来并把实际位置量成前提（下面两条 assert）；
            // ② 万一"刚出现不主动接敌"的宽限在这条路上成立（内核只在**复活**那一支挂它，
            //    见 `DsWorldSession.cs:2527` 的 `SpawnIsRespawn`），也不至于让唯一一次
            //    "打架那只还在队友 9.144 米内"的窗口整个落在宽限里。
            for (var t = 0; t < 360; t++) world.Tick();

            Assert.True(world.TryGetMonster(idFighting, out var pre) && pre != null,
                        $"槽位 0 那只（实体 {idFighting}）空跑 6 秒后不在场：几何前提没了");
            Assert.True(world.TryGetMonster(idIdle, out var preIdle) && preIdle != null,
                        "槽位 1 那只空跑 6 秒后不在场");
            MonsterContent.TryGetConfig(templateId, out var authored);
            Assert.NotNull(authored);
            const float playerX = 6f;
            var packGap = MathF.Abs(preIdle!.Ai.X - pre.Ai.X);
            var fighterToPlayer = MathF.Abs(pre.Ai.X - playerX);
            var idleToPlayer = MathF.Abs(preIdle.Ai.X - playerX);
            // 索敌半径是**逐目标按等级差算出来的**（AC 的 `GetAggroRange`，本作 `MonsterAiGate.EffectiveAggroRadius`），
            // 所以这里不能用"库里那格写着 10 米"当判据：玩家等级比怪高时它被抬到 26 米（14 米外那只直接冲上来，
            // 求援分支一次都进不去），比怪低时被压到 5 码下限（谁都看不见玩家，全场空转）。
            // 取"与怪同级"⇒ 有效半径正好等于作者配的那一格，两条前提才各自成立。
            var playerLevel = pre.EffectiveLevel;
            Assert.True(playerLevel > 0,
                        $"槽位 0 那只的等级读出来是 {pre.EffectiveLevel}：这条用例的等级前提没了"
                        + "（有效半径按怪与玩家的等级差算，等级为 0 时内核按「没带等级」处理、不做修正）");
            var effFighter = MonsterAiGate.EffectiveAggroRadius(authored!.Behavior.AggroRadius, playerLevel, playerLevel);
            var effIdle = MonsterAiGate.EffectiveAggroRadius(authored.Behavior.AggroRadius, preIdle.EffectiveLevel, playerLevel);
            // 前提要量出来写在回执里，而不是按"槽距 8 米"的口算假设跑下去
            Assert.True(packGap <= 9.144f,
                        $"两只同族空跑之后隔了 {packGap:0.###} 米（>求援半径 9.144）⇒ 这一轮测的几何已经不成立了");
            Assert.True(fighterToPlayer <= effFighter,
                        $"打架那只离玩家 {fighterToPlayer:0.##} 米而它的有效索敌只有 {effFighter:0.##} 米"
                        + "（等级差把半径压下去了）⇒ 没人进战，正例与两条负对照都在测空集");
            Assert.True(idleToPlayer > effIdle,
                        $"「闲着的」那只离玩家 {idleToPlayer:0.##} 米，但它的有效索敌有 {effIdle:0.##} 米"
                        + $"（怪 {preIdle.EffectiveLevel} 级 / 玩家 {playerLevel} 级）⇒ 它会自己看见玩家，"
                        + "这条判据就分不清「被叫起来」与「自己看见」了");

            var premise = $"怪级={pre.EffectiveLevel}/闲怪级={preIdle.EffectiveLevel} 玩家级={playerLevel} "
                        + $"作者半径={authored.Behavior.AggroRadius:0.###} eff打架={effFighter:0.###} eff闲着={effIdle:0.###} "
                        + $"位移:打架={fighterToPlayer:0.##} 闲着={idleToPlayer:0.##}";
            // 玩家站在 12 号槽那一侧 6 米处：它只够得着槽位 0，槽位 1 在 20 处、够不着
            // ⇒ 槽位 1 进战斗**只能**是因为听见了同族。
            Assert.True(world.AddPlayer(97001, 97001, playerX, 0f, 0f, out var why, "求援证人", playerLevel), why);

            var diag = $"(槽位 0 一直没进战) 前提:{premise}";
            var capturedAtFirstCombat = false;
            for (var t = 0; t < 600; t++)
            {
                world.Tick();
                if (!capturedAtFirstCombat
                    && world.TryGetMonster(idFighting, out var f0) && f0 != null
                    && MonsterAiGate.InCombat(f0.Ai.State))
                {
                    // 第一拍就把所有中间量抄下来：红了之后"到底是哪一格没成立"要能一眼看出来，
                    // 而不是只看到"种子 0 次"这一句同义反复。
                    capturedAtFirstCombat = true;
                    world.TryGetMonster(idIdle, out var i0);
                    MonsterContent.TryGetConfig(templateId, out var c0);
                    diag = $"首次进战 t={t} 打架那只=({f0.Ai.X:0.##},{f0.Ai.Y:0.##}) 状态={f0.Ai.State} "
                         + $"闲着那只=({i0?.Ai.X:0.##},{i0?.Ai.Y:0.##}) 状态={i0?.Ai.State} 目标={i0?.Ai.ThreatTargetId} "
                         + $"间隔={(i0 == null ? -1f : MathF.Abs(i0.Ai.X - f0.Ai.X)):0.###} "
                         + $"宽限到={i0?.Ai.NoInitiateUntilTick} 免疫到={i0?.Ai.EvadeImmuneUntilTick} "
                         + $"内容展开={(c0 == null ? "没查到内容" : c0.Behavior.Flags.Describe())} "
                         + $"半径={(c0 == null ? -1f : c0.Behavior.AssistRadius):0.###} "
                         + $"这一拍种子累计={world.MonsterAssistSeeds} "
                         + $"清单截断次数={world.MonsterAssistAlliesTruncated} 前提:{premise}";
                }
            }

            Assert.True(world.TryGetMonster(idFighting, out var fighter) && fighter != null,
                        $"槽位 0 那只（实体 {idFighting}）没在场：这一串跑到别的槽位上了");
            Assert.True(MonsterAiGate.InCombat(fighter!.Ai.State),
                        $"槽位 0 那只 10 秒里没进战斗（状态 {fighter.Ai.State}、距玩家 {MathF.Abs(fighter.Ai.X - playerX):0.#} 米）"
                        + "⇒ 没人可被叫，正例与两条负对照全都在测空集");
            Assert.True(world.TryGetMonster(idIdle, out var idle) && idle != null,
                        $"槽位 1 那只（实体 {idIdle}）没在场");
            return (world.MonsterAssistSeeds, idle!.Ai.ThreatTargetId, world.MonsterAssistSayEvents, diag);
        }
        finally
        {
            File.Delete(path);
            MonsterContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 巡逻到点施法起手与求援叫起三格都进了DsHost的周期摘要()
    {
        // 摘要是这台 DS 唯一会自己念出来的东西。计数装了但没进摘要 = 运行期还是问不出来，
        // 而"配了没人读"这一族的教训反复就是：离屏的闸门看不见只在进程里跑的那一层。
        var runner = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.DsHost", "DsHostRunner.cs"));
        Assert.Contains("巡逻到点(含NPC线)={_world.MonsterPatrolPointsReached}", runner);
        // 标签必须写"含NPC线"：路线解析先查怪物内容、查不到才退 NPC 内容，
        // 生产包路线=0 时这一格照样在涨（涨的是 NPC 作息那条线）——不写清楚就是一个会说谎的标签。
        Assert.Contains("施法起手={_world.MonsterCastStartEvents}", runner);
        // 求援那一格（切片 56 的内容 + 切片 57 的 AC 两秒延迟）也必须自己念得出：
        // 有人开打之后它一路是 0，才是"库里的求援位没走到世界"的证据；没人时 0 是预期。
        Assert.Contains("求援叫起={_world.MonsterAssistSeeds}", runner);
        // 切片 94/95 的三格同样必须自己念得出：宽限窗被打开、求援并组、共享刷新。
        // 三个数都是"接了但没走"时恒 0，进不了摘要就只能靠翻字典。
        Assert.Contains("并组={_world.MonsterLeashGroupsLinked}", runner);
        Assert.Contains("共享刷新={_world.MonsterLeashSharedRefreshes}", runner);
        Assert.Contains("宽限刷新={_world.MonsterLeashExtensions}", runner);
        // 2026-10-29（D23 尸体拾取）三格：登记 / 取走 / 登记失败。三格都为 0 时"没接"与"没发生"同形。
        Assert.Contains("尸体登记={_world.MonsterCorpseLootRegistered}", runner);
        Assert.Contains("尸体取走={_world.MonsterCorpseLootTaken}", runner);
        Assert.Contains("尸体登记失败={_world.MonsterCorpseLootRegisterFailures}", runner);
        // 2026-10-29 三段：数据面收到的请求、数据面当场拒、以及真正排给客户端的结果包。
        // 三者都为 0 时"玩家没点"与"点了没人接"同形。
        Assert.Contains("拾取请求={_dataPlane.CorpseLootRequestsSeen}", runner);
        Assert.Contains("拾取数据面拒={_dataPlane.CorpseLootRejected}", runner);
        Assert.Contains("拾取结果包={_world.CorpseLootResultPacketsQueued}", runner);

        var session = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.Game.Core", "Sim", "Server", "DsWorldSession.cs"));
        // 只有一个自增点，且就挂在唯一的游标写回那句旁边——两处清单迟早分家
        Assert.Equal(1, session.Split(new[] { "MonsterPatrolPointsReached++" }, StringSplitOptions.None).Length - 1);
        // 求援种子同理：只有"v2 带回 SeededByAssist"那一处能加，别在宿主里再抄一份判据
        Assert.Equal(1, session.Split(new[] { "MonsterAssistSeeds++" }, StringSplitOptions.None).Length - 1);
        // 切片 83 起这一支还顺带发应援台词，所以判据钉的是"进入条件"与"唯一的自增点"，
        // 而不是整行的字面形状（钉整行会在加业务时把正确改动判红）。
        Assert.Contains("if (v2.HasValue && v2.Value.SeededByAssist)", session);
        Assert.Equal(1, session.Split(new[] { "EmitMonsterAssistSay(monster);" }, StringSplitOptions.None).Length - 1);
        // 自增点各自唯一：并组只在求援接手那一拍、共享刷新只在刷新循环里，
        // 而"宽限刷新"**必须两处**（伤害落点 + Stun/Root）——少一处就是 AC 那一半丢了。
        Assert.Equal(1, session.Split(new[] { "MonsterLeashGroupsLinked++" }, StringSplitOptions.None).Length - 1);
        Assert.Equal(1, session.Split(new[] { "MonsterLeashSharedRefreshes++" }, StringSplitOptions.None).Length - 1);
        Assert.Equal(2, session.Split(new[] { "MonsterLeashExtensions++" }, StringSplitOptions.None).Length - 1);
        Assert.Equal(1, session.Split(new[] { "self.Patrol = decision.Patrol;" }, StringSplitOptions.None).Length - 1);
        var writeback = session.IndexOf("self.Patrol = decision.Patrol;", StringComparison.Ordinal);
        var increment = session.IndexOf("MonsterPatrolPointsReached++", StringComparison.Ordinal);
        Assert.True(increment - writeback >= 0 && increment - writeback < 400,
                    "到点计数没挂在游标写回那一句旁边：它会与「谁真的走了线」分家");

        // **两条通路各自都要填这一位**——只填 v1 就是本轮实测到的那个坏形状
        // （配了 13 点环线的怪在 v2 上走满 30 秒、到点计数 0），所以这里两侧各钉一处赋值。
        var v1 = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.Game.Sim", "MonsterAiCore.cs"));
        var v2 = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.Game.Sim", "Monsters", "MonsterAiRuntime.cs"));
        Assert.Equal(1, v1.Split(new[] { "decision.PatrolPointSwitched = step.SwitchedPoint;" }, StringSplitOptions.None).Length - 1);
        Assert.Equal(1, v2.Split(new[] { "decision.PatrolPointSwitched = step.SwitchedPoint;" }, StringSplitOptions.None).Length - 1);
        Assert.Equal(1, session.Split(new[] { "if (decision.PatrolPointSwitched)" }, StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void 没有会话的输入与卡顿丢帧分开计数而前者第一次就会念出来()
    {
        // 现场（2026-10-28 取 PIE 证据时撞上的）：客户端**有画面**、这台 DS 恒
        // `玩家=0 收帧=1753 丢帧=1753`，而日志里一条握手行都没有。
        // 原因是"未握手的输入"与"入站队列溢出"共用一个计数器，而且前者**完全静默**（溢出那一路每 100 条还会念一次）。
        // 两种故障在玩家那边同形（动了没反应），成因却一个是"这台机器跑不过来"、
        // 一个是"会话根本不在这台"（典型：同一个 shard 上起了两台 DS）。
        var logged = new List<string>();
        var world = new Horizon.Game.Core.Sim.Server.DsWorldSession(
            new Horizon.Game.Core.Sim.Server.DsWorldSessionOptions
            {
                ShardId = 0,
                TicksPerSecond = 60,
                SnapshotIntervalTicks = 600,
            });
        var plane = new Horizon.Game.Core.Sim.Server.DsDataPlaneHost(
            world, listenPort: 0, log: logged.Add);

        plane.Apply(new Horizon.Game.Message.Sync.InputPacket
        {
            CharacterId = 96001UL, ClientTick = 1, InputBits = 0,
        });
        Assert.Equal(1, plane.InputWithoutSessionCount);
        Assert.Equal(0, plane.DroppedFrameCount);        // 不再挂到"丢帧"那一格上
        Assert.Contains(logged, m => m.Contains("没有会话") && m.Contains("96001"));

        // 正向对照（拒绝路径必须配同夹具的正例）：握手过的会话，输入不许再进"无会话"那一格，
        // 也不许被那条告警点名——否则这一格计数只是"什么都算进去"。
        plane.Apply(new Horizon.Game.Message.Sync.HandshakePacket
        {
            LocalCharacterId = 96001UL, InitialClientTick = 1,
            ProtocolVersion = Horizon.Game.Message.Sync.SyncProtocolVersion.Current,
            Nickname = "属主证人", Level = 10, Title = string.Empty,
            InitialX = -2f, InitialY = 0f, InitialZ = 0f,
        });
        var loggedBefore = logged.Count;
        plane.Apply(new Horizon.Game.Message.Sync.InputPacket
        {
            CharacterId = 96001UL, ClientTick = 2, InputBits = 0,
        });
        Assert.Equal(1, plane.InputWithoutSessionCount);
        Assert.Equal(1, world.PlayerCount);
        Assert.Equal(loggedBefore, logged.Count);

        // 这一格必须进周期摘要：只有进程内计数器 = 运行期还是问不出来
        var runner = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.DsHost", "DsHostRunner.cs"));
        Assert.Contains("无会话输入={_dataPlane.InputWithoutSessionCount}", runner);
        // 旧那句"静默丢弃"的注释不许回来——它就是这条故障的藏身处
        var host = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.Game.Core", "Sim", "Server", "DsDataPlaneHost.cs"));
        Assert.Equal(0, host.Split(new[] { "未握手的输入：静默丢弃" }, StringSplitOptions.None).Length - 1);
    }
    [Fact]
    public void 排练库那份真文案装进世界之后怪进战真的喊出那一句()
    {
        // 切片 66 给排练库 5 只普通怪配了第一份真文案（sayId=1），冻结件里也带上了。
        // 这一条问的是**运行期**：装包之后 mob 3 进战，是否真的发一条 sayId=1 的事件、
        // 且包里认得出这一号（HasText）——认得不出的话客户端拿到号也翻不出字，玩家那边是空气泡。
        Load();
        try
        {
            Assert.True(MonsterContent.HasText(3, 1), "包里没有怪 3 的第 1 号文本：客户端的气泡会是空的");
            var (packets, sayEvents, missEvents, tailState, _) = RunSay(templateId: 3, expectSayId: 1);
            Assert.Equal(1, packets);
            Assert.Equal(1, sayEvents);
            Assert.Equal(0, missEvents);
            Assert.True(tailState.Contains("Chase") || tailState.Contains("Attack"), tailState);
        }
        finally
        {
            MonsterContent.ResetToHardcoded();
        }
    }
}
