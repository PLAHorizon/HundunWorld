namespace Horizon.Game.Sim;

/// <summary>
/// 跳跃状态机——**唯一**实现（阶段 1.3 延伸）：权威校验（<see cref="MovementValidator"/>）
/// 与客户端预测/重放（<see cref="MovementFormulaStepper"/>）共用同一跳跃计数演进规则，
/// 消除跨路径语义分叉（曾存在：权威非按下帧保留计数、预测非按下非轻功清零）。
/// <para>
/// 对齐契约（跨语言对拍锚点——UE5 DS 侧 <c>SimDriver</c> 若复刻本状态机，金轨迹方可逐字节一致）：
/// <list type="bullet">
///   <item>按下 + 轻功：jumpCount 递增；冲量取 <paramref name="jumpImpulses"/>[count-1]；</item>
///   <item>按下 + 非轻功：jumpCount 重置为 1；冲量取首段；</item>
///   <item>未按下：jumpCount <b>保留</b>（空中滞空延续；清零只由宿主落地约束负责）；</item>
///   <item>落地清零（jumpCount=0）不在本状态机内——由各宿主（SimWorld 地面约束 / LocalSimulationSystem /
///       ReconciliationSystem）在接地判定后负责，与重力/位置逻辑解耦。</item>
/// </list>
/// </para>
/// </summary>
public static class JumpStateMachine
{
    /// <summary>
    /// 推进一帧的跳跃状态。
    /// </summary>
    /// <param name="jumpCount">当前跳跃计数（上一帧遗留；0 = 未起跳/落地已重置）。</param>
    /// <param name="jumpPressed">本帧跳跃按下（边沿语义：客户端 InputBits bit0 / JumpPressedThisFrame）。</param>
    /// <param name="isQinggongJump">本帧是否为轻功跳跃（InputBits bit3）。</param>
    /// <param name="jumpImpulses">轻功冲量表（jumpCount=1 → [0]、2 → [1]…；越界 → 0）。</param>
    /// <param name="maxQinggongJumpCount">轻功最大段数（超过后冲量归零并标记超限，语义与历史一致）。</param>
    /// <returns>新计数、本帧冲量、是否超段数（仅权威 correction 判定用；预测侧忽略）。</returns>
    public static (int JumpCount, float Impulse, bool Exceeded) Advance(
        int jumpCount,
        bool jumpPressed,
        bool isQinggongJump,
        float[] jumpImpulses,
        int maxQinggongJumpCount)
    {
        if (jumpPressed)
        {
            if (isQinggongJump)
            {
                jumpCount++;
                var exceeded = jumpCount > maxQinggongJumpCount;
                var impulse = !exceeded && jumpCount >= 1 && jumpCount <= jumpImpulses.Length
                    ? jumpImpulses[jumpCount - 1]
                    : 0f;
                return (jumpCount, impulse, exceeded);
            }

            // 普通跳跃固定单跳（jumpCount 重置为 1），冲量 = 首段冲量。
            return (1, jumpImpulses.Length > 0 ? jumpImpulses[0] : 0f, false);
        }

        // 未按下：保留计数（空中滞空延续）。落地清零由宿主接地约束负责。
        return (jumpCount, 0f, false);
    }
}
