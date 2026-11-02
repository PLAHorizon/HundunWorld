namespace Horizon.Game.Sim.Npcs;

/// <summary>
/// 路线试算结果（编辑器「刷点·路线」页给运营看的那一份）。
/// <para>距离/时间都由点数与步行速度算出；**顺序与停留时长则由巡逻内核现算**（见 Preview）。</para>
/// </summary>
public readonly struct NpcRoutePreview
{
    public readonly int Points;
    public readonly int Arrivals;
    public readonly float TotalMetres;
    public readonly float MinLegMetres;
    public readonly float MaxLegMetres;
    public readonly float PauseSeconds;
    public readonly float CycleSeconds;
    public readonly bool Closed;
    public readonly string[] Warnings;

    public NpcRoutePreview(int points, int arrivals, float totalMetres, float minLegMetres, float maxLegMetres,
        float pauseSeconds, float cycleSeconds, bool closed, string[] warnings)
    {
        Points = points;
        Arrivals = arrivals;
        TotalMetres = totalMetres;
        MinLegMetres = minLegMetres;
        MaxLegMetres = maxLegMetres;
        PauseSeconds = pauseSeconds;
        CycleSeconds = cycleSeconds;
        Closed = closed;
        Warnings = warnings ?? System.Array.Empty<string>();
    }

    public string Describe()
        => $"points={Points} arrivals={Arrivals} metres={TotalMetres:0.#} leg=[{MinLegMetres:0.#},{MaxLegMetres:0.#}] " +
           $"pause={PauseSeconds:0.#}s cycle={(CycleSeconds >= 0f ? CycleSeconds.ToString("0.#") + "s" : "?")} " +
           $"closed={(Closed ? 1 : 0)} warn={Warnings.Length}";

    public static NpcRoutePreview Incomplete(NpcRoutePreviewProblem problem)
        => new(0, 0, 0f, 0f, 0f, 0f, -1f, false, new[] { ProblemName(problem) });

    internal static string ProblemName(NpcRoutePreviewProblem problem) => problem switch
    {
        NpcRoutePreviewProblem.NeedsTwoPoints => "route_needs_two_points",
        _ => "route_preview_unknown",
    };
}

/// <summary>试算的"根本没跑"档（与"跑出零米"必须分开：后者是数据问题，前者是连路都不成立）。</summary>
public enum NpcRoutePreviewProblem
{
    NeedsTwoPoints = 0,
}

/// <summary>
/// NPC 路线试算（纯函数，离线可跑）。
/// <para>
/// <b>为什么顺序要问内核</b>：往返/循环/随机的换点规则住在 <c>MonsterPatrolCore.Advance</c>，
/// 演算层自己再写一遍"下一站是谁"就是第二份规则——漂了之后编辑器上的"一个循环 N 米"和世界里
/// 真正走法会分叉，而且谁都不报错。这里把走法交给内核（宿主视角：到点即瞬移到位、
/// 停留按内核自己的计时推进），演算层只负责量距离与报问题。
/// </para>
/// <para>
/// <b>不估计"随机模式"的循环</b>：随机下一点由宿主的确定性随机决定，这里只按顺序采样一圈，
/// 并在回执里点名 <c>random_mode_order_unpredictable</c>——宁可说"这一项不可估"，也不给一个像真的假数字。
/// </para>
/// </summary>
public static class NpcRoutePreviewCore
{
    /// <summary>版本（改动回合/停留判据时递增，测试锚点跟着走）。</summary>
    public const int Version = 1;

    /// <summary>短段阈值：与巡逻内核的 <c>ArriveDistance</c> 同值——短于它的段"出发即到点"。</summary>
    public const float ShortLegMetres = 0.5f;

    /// <summary>长段阈值：直线连段走场景，超过它多半会穿墙。</summary>
    public const float LongLegMetres = 60f;

    /// <summary>换点上限：防止坏数据（比如往返模式两人点）把演算拖死。</summary>
    public const int MaxSwitches = 64;

    /// <summary>停留推进的步长与上限（内核按 dt 累计停留秒；这里只是把它走完）。</summary>
    public const float PauseStepSeconds = 0.25f;
    public const int MaxPauseSteps = 8192;

    /// <summary>
    /// 试算一条路线：返回一个循环的距离/停留/时间与问题清单。
    /// </summary>
    /// <param name="points">点列（顺序即 <c>seq</c>；与快照同形）。</param>
    /// <param name="mode">走法（往返/循环/随机）。</param>
    /// <param name="pauseAtPointSeconds">点级没配时用的默认停留秒。</param>
    /// <param name="walkSpeed">步行速度（米/秒）。<b>&lt;=0 时只报距离与停留，循环秒数回 -1</b>。</param>
    public static NpcRoutePreview Preview(
        Horizon.Game.Sim.Monsters.MonsterPatrolPoint[] points,
        Horizon.Game.Sim.Monsters.MonsterPatrolMode mode,
        float pauseAtPointSeconds,
        float walkSpeed)
    {
        if (points == null || points.Length < 2)
            return NpcRoutePreview.Incomplete(NpcRoutePreviewProblem.NeedsTwoPoints);

        var route = new Horizon.Game.Sim.Monsters.MonsterPatrolRoute
        {
            RouteId = 0,
            Mode = mode,
            PauseAtPointSeconds = pauseAtPointSeconds,
            Points = points,
        };

        var self = new Horizon.Game.Sim.Monsters.MonsterPatrolSelf();
        Horizon.Game.Sim.Monsters.MonsterPatrolCore.ResetToStart(ref self, mode);

        var warnings = new System.Collections.Generic.List<string>();
        if (mode == Horizon.Game.Sim.Monsters.MonsterPatrolMode.RandomNext)
            warnings.Add("random_mode_order_unpredictable");
        if (walkSpeed <= 0f) warnings.Add("walk_speed_not_set");

        var x = points[0].X;
        var y = points[0].Y;

        // 以"内核状态"（当前点下标, 方向）为周期判据：同一状态再现即一个循环走完。
        // 不能只盯"回到 0 号点且方向 +1"——往返模式永远回不到那个组合，会被误报"路线不闭合"。
        var seen = new System.Collections.Generic.Dictionary<(int Index, int Direction), (int Legs, float Total, float Pauses, int Arrivals)>();
        seen[(self.PointIndex, self.Direction)] = (0, 0f, 0f, 0);

        var legsRun = 0;
        var arrivalsRun = 0;
        var totalRun = 0f;
        var minLeg = float.MaxValue;
        var maxLeg = 0f;
        var pausesRun = 0f;

        var legs = 0;
        var arrivals = 0;
        var total = 0f;
        var pauses = 0f;
        var closed = false;

        for (var guard = 0; guard < MaxSwitches; guard++)
        {
            var step = Horizon.Game.Sim.Monsters.MonsterPatrolCore.Advance(ref self, route, x, y, PauseStepSeconds);

            // 停留：按内核自己的计时推进（它判"停够了"的规则我们不复制）。
            // 记账口径 = "为停留花掉的拍数 × 拍长"，**含翻出停留的那一拍**（内核用它把计数器推到阈值）。
            var pauseOverrun = false;
            if (step.Waiting)
            {
                pausesRun += PauseStepSeconds;
                for (var pauseSteps = 0; pauseSteps < MaxPauseSteps; pauseSteps++)
                {
                    step = Horizon.Game.Sim.Monsters.MonsterPatrolCore.Advance(ref self, route, x, y, PauseStepSeconds);
                    pausesRun += PauseStepSeconds;
                    if (!step.Waiting) break;
                    if (pauseSteps == MaxPauseSteps - 1) pauseOverrun = true;
                }
            }

            if (pauseOverrun)
            {
                warnings.Add("pause_too_long");
                break;
            }

            // 非停留分支：内核给的是"本段目的地"，宿主会走过去——演算这里瞬移到位。
            var leg = Dist(x, y, step.TargetX, step.TargetY);
            legsRun++;
            arrivalsRun++;
            totalRun += leg;
            if (leg < minLeg) minLeg = leg;
            if (leg > maxLeg) maxLeg = leg;
            x = step.TargetX;
            y = step.TargetY;

            if (!step.SwitchedPoint) continue;

            var state = (self.PointIndex, self.Direction);
            if (seen.TryGetValue(state, out var before))
            {
                // 状态再现：这一段的增量就是一个循环。
                legs = legsRun - before.Legs;
                total = totalRun - before.Total;
                pauses = pausesRun - before.Pauses;
                arrivals = arrivalsRun - before.Arrivals;
                closed = true;
                break;
            }

            seen[state] = (legsRun, totalRun, pausesRun, arrivalsRun);
        }

        if (!closed)
        {
            legs = legsRun;
            total = totalRun;
            pauses = pausesRun;
            arrivals = arrivalsRun;
        }

        if (legs == 0) minLeg = 0f;
        if (minLeg < ShortLegMetres) warnings.Add("leg_too_short");
        if (maxLeg > LongLegMetres) warnings.Add("leg_too_long");
        if (!closed && mode != Horizon.Game.Sim.Monsters.MonsterPatrolMode.RandomNext)
            warnings.Add("route_not_closed");

        var cycle = walkSpeed > 0f ? total / walkSpeed + pauses : -1f;
        return new NpcRoutePreview(points.Length, arrivals, total, minLeg, maxLeg,
            pauses, cycle, closed, warnings.ToArray());
    }

    private static float Dist(float ax, float ay, float bx, float by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        return System.MathF.Sqrt(dx * dx + dy * dy);
    }
}