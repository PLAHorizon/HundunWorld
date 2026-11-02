using System;

namespace Horizon.Game.Sim;

/// <summary>
/// 翻越（mantle/vault）脚本位移计划：ECS 坐标（Z-up）。
/// <para>
/// **单位与引擎世界单位一致（厘米）**：客户端 <c>pred.X/Y/Z</c> 直接等于 Flax 世界坐标
/// （<c>LocalPlayerActorSyncSystem</c> 做的是 <c>Actor.Position=(pred.X, pred.Z, pred.Y)</c> 的轴交换、无缩放），
/// 服务端权威位置也用同一份数值。早期按"米"写限值会让所有计划被几十倍地拒绝。
/// </para>
/// <para>
/// 客户端用 Flax 射线检测出边沿后生成计划，服务端按 <see cref="TraversalMath.Sample"/> 用同一份
/// 计划推进权威位置——两端只交换计划参数，不交换逐帧位置，因此必须保持纯确定性：
/// 只依赖 float 四则运算，不引入时钟/随机/集合遍历顺序。
/// </para>
/// </summary>
public readonly struct TraversalPlan
{
    /// <summary>起点 X（应贴近玩家当前位置，服务端按 <see cref="TraversalLimits.StartTolerance"/> 校验）。</summary>
    public float StartX { get; init; }
    public float StartY { get; init; }
    public float StartZ { get; init; }

    /// <summary>终点 X/Y/Z（落在平台上，Z 为平台顶面高度）。</summary>
    public float EndX { get; init; }
    public float EndY { get; init; }
    public float EndZ { get; init; }

    /// <summary>总时长（秒），必须落在 <see cref="TraversalLimits.MinDuration"/>..<see cref="TraversalLimits.MaxDuration"/>。</summary>
    public float Duration { get; init; }

    /// <summary>弧顶额外抬升（米，0 = 纯平滑插值；> 0 时曲线在中点最高）。</summary>
    public float ArcHeight { get; init; }

    /// <summary>结束朝向（ECS 弧度，绕 Z 轴；与位置同一条 smoothstep 最短弧插值）。</summary>
    public float EndYaw { get; init; }

    /// <summary>
    /// 两个端点之间是否都落在同一份计划上：服务端只做"这计划本身合法"的判定，
    /// 位置连续由权威侧在结束时写入 End 保证。
    /// </summary>
    public bool HasDuration => Duration > 0f;
}

/// <summary>翻越计划的合法性限值（客户端生成计划与服务端校验共用同一份，禁止各写一套）。</summary>
public sealed class TraversalLimits
{
    /// <summary>最短时长（秒）。</summary>
    public float MinDuration { get; set; } = 0.25f;

    /// <summary>最长时长（秒）。</summary>
    public float MaxDuration { get; set; } = 2.0f;

    /// <summary>最低抬升（厘米）：终点 Z - 起点 Z 的下限（与 Flax 探针 MinHeight=45cm 同量级）。</summary>
    public float MinHeight { get; set; } = 20f;

    /// <summary>最高抬升（厘米）：终点 Z - 起点 Z 的上限（覆盖 Mantle_2m 的 220cm 选库上限）。</summary>
    public float MaxHeight { get; set; } = 250f;

    /// <summary>允许的最大水平位移（厘米）。</summary>
    public float MaxHorizontalDistance { get; set; } = 250f;

    /// <summary>弧顶最大额外抬升（厘米）。</summary>
    public float MaxArcHeight { get; set; } = 250f;

    /// <summary>计划起点与玩家权威位置允许的最大偏差（厘米）——防止客户端把起点"搬到"别处拿位移。</summary>
    public float StartTolerance { get; set; } = 75f;

    /// <summary>两次翻越之间的最短间隔（秒）。服务端按 tick 数换算，客户端同款限流。</summary>
    public float CooldownSeconds { get; set; } = 0.35f;
}

/// <summary>计划拒绝原因；客户端与服务端共用词表，便于日志与结果包对齐。</summary>
public enum TraversalRejectReason : byte
{
    None = 0,

    /// <summary>时长非正或超出限值。</summary>
    InvalidDuration = 1,

    /// <summary>抬升不在 [MinHeight, MaxHeight]。</summary>
    HeightOutOfRange = 2,

    /// <summary>水平位移为 0 或超过上限。</summary>
    HorizontalOutOfRange = 3,

    /// <summary>弧高为负或超过上限。</summary>
    ArcOutOfRange = 4,

    /// <summary>计划起点离权威位置太远（含 NaN/Inf）。</summary>
    StartTooFar = 5,

    /// <summary>计划含非法数值（NaN/Inf），或终点低于起点地面。</summary>
    InvalidNumbers = 6,

    /// <summary>该实体已经在翻越中（不允许叠加/续接）。</summary>
    AlreadyActive = 7,

    /// <summary>距离上一次翻越结束不足 <see cref="TraversalLimits.CooldownSeconds"/>。</summary>
    Cooldown = 8,

    /// <summary>当前没有可用的权威侧（无会话/权威不可达）；客户端应保持原姿势并回正。</summary>
    AuthorityUnavailable = 9,
}

/// <summary>
/// 翻越曲线的唯一实现：客户端预测与服务端权威都调本类采样，禁止各自复制一份曲线。
/// </summary>
public static class TraversalMath
{
    /// <summary>曲线版本；曲线语义变更必须递增（协议协商用）。</summary>
    public const int Version = 1;

    /// <summary>平滑步进 smoothstep(t)=t²(3-2t)，端点速度为 0，避免起止瞬移感。</summary>
    public static float SmoothStep(float t)
    {
        t = Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// 按计划采样 <paramref name="elapsedSeconds"/> 时刻的位置（越界钳制到端点）。
    /// 水平/垂直用 smoothstep 插值；<see cref="TraversalPlan.ArcHeight"/> 叠加 4t(1-t) 抛物弧。
    /// </summary>
    public static (float X, float Y, float Z) Sample(in TraversalPlan plan, float elapsedSeconds)
    {
        if (!(plan.Duration > 0f))
            return (plan.EndX, plan.EndY, plan.EndZ);

        var t = Clamp01(elapsedSeconds / plan.Duration);
        var s = SmoothStep(t);
        var x = plan.StartX + (plan.EndX - plan.StartX) * s;
        var y = plan.StartY + (plan.EndY - plan.StartY) * s;
        var z = plan.StartZ + (plan.EndZ - plan.StartZ) * s;
        if (plan.ArcHeight != 0f)
            z += plan.ArcHeight * (4f * t * (1f - t));
        return (x, y, z);
    }

    /// <summary>
    /// 朝向插值：从 <paramref name="startYaw"/> 走最短弧到计划的 <see cref="TraversalPlan.EndYaw"/>，
    /// 曲线与位置同一 smoothstep。两端调用同一个函数，避免"位置同步了、朝向各转各的"。
    /// </summary>
    public static float SampleYaw(float startYaw, in TraversalPlan plan, float elapsedSeconds)
    {
        if (!(plan.Duration > 0f))
            return plan.EndYaw;

        var t = Clamp01(elapsedSeconds / plan.Duration);
        var s = SmoothStep(t);
        var delta = plan.EndYaw - startYaw;
        // 最短弧：把差值折进 (-π, π]。
        while (delta > MathF.PI) delta -= MathF.Tau;
        while (delta < -MathF.PI) delta += MathF.Tau;
        return startYaw + delta * s;
    }

    /// <summary>
    /// 校验计划本身是否合法，以及起点是否贴近权威位置 <paramref name="fromX/Y/Z"/>。
    /// 与服务端 <c>MovementValidator</c>、客户端生成侧共用。
    /// </summary>
    public static TraversalRejectReason Validate(
        in TraversalPlan plan,
        float fromX,
        float fromY,
        float fromZ,
        TraversalLimits? limits = null)
    {
        limits ??= new TraversalLimits();

        if (!IsFinite(plan.StartX) || !IsFinite(plan.StartY) || !IsFinite(plan.StartZ)
            || !IsFinite(plan.EndX) || !IsFinite(plan.EndY) || !IsFinite(plan.EndZ)
            || !IsFinite(plan.Duration) || !IsFinite(plan.ArcHeight) || !IsFinite(plan.EndYaw))
            return TraversalRejectReason.InvalidNumbers;

        if (!(plan.Duration >= limits.MinDuration) || plan.Duration > limits.MaxDuration)
            return TraversalRejectReason.InvalidDuration;

        var height = plan.EndZ - plan.StartZ;
        if (height < limits.MinHeight || height > limits.MaxHeight)
            return TraversalRejectReason.HeightOutOfRange;

        var dx = plan.EndX - plan.StartX;
        var dy = plan.EndY - plan.StartY;
        var horizontal = MathF.Sqrt(dx * dx + dy * dy);
        if (!(horizontal > 0f) || horizontal > limits.MaxHorizontalDistance)
            return TraversalRejectReason.HorizontalOutOfRange;

        if (plan.ArcHeight < 0f || plan.ArcHeight > limits.MaxArcHeight)
            return TraversalRejectReason.ArcOutOfRange;

        var sx = plan.StartX - fromX;
        var sy = plan.StartY - fromY;
        var sz = plan.StartZ - fromZ;
        var startDistance = MathF.Sqrt(sx * sx + sy * sy + sz * sz);
        if (!IsFinite(startDistance) || startDistance > limits.StartTolerance)
            return TraversalRejectReason.StartTooFar;

        return TraversalRejectReason.None;
    }

    private static bool IsFinite(float value)
        => !float.IsNaN(value) && !float.IsInfinity(value);

    private static float Clamp01(float value)
        => value < 0f ? 0f : (value > 1f ? 1f : value);
}
