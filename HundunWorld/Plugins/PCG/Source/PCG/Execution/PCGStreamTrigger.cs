// PCG 插件 - 流式跟随触发器
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §7.3。

using System;
using FlaxEngine;
using PCG.Graph;

namespace PCG.Execution
{
    /// <summary>
    /// 流式体积每帧跟随目标时做的决定：这一帧是不是那一帧——必须去请求一次生成的那一帧，
    /// 以及如果是，是因为哪一条理由。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 跟随一个玩家不该等于按定时器重生成。值得发一次请求的事件只有两个：被跟随的点跨进了
    /// 另一个格子（窗口的内容眼看就要是错的），或者上一次请求按 <c>StreamCellsPerRequest</c>
    /// 留下了被延后的格子、而紧接的那一帧是空闲的（窗口是对的，只是只填了一部分）。
    /// 其余情形都是空闲帧，而一个空闲帧的代价必须不超过——
    /// 读一个位置，此外什么都不做。
    /// </para>
    /// <para>
    /// 延后那一条分支刻意由"有没有进展"来把关：如果一次请求无法缩小积压——
    /// 比如某张图的合并点数上限已经到了，或者某个格子的生成一直在失败——
    /// 那么每帧再问一次就会把一份有界的积压变成一场无界的停滞。
    /// 所以一旦某次请求没有进展，就不再发请求，直到被跟随的目标真的又动了。
    /// </para>
    /// </remarks>
    public static class PCGStreamTrigger
    {
        /// <summary>
        /// 一个坐标落在固定网格的哪一格，朝负无穷方向取整。
        /// </summary>
        /// <remarks>
        /// 负浮点数做整数除法是朝零截断的，在这里恰好是错的：步长 400 时 -1 和 +1 都会算成"第 0 格"，
        /// 玩家明明只走了 2 个单位，而到 -400 时又会跳到同一个下标。
        /// 这样朝负方向跨过原点永远不会被认成一次跨格，
        /// 世界的西半边也就此停止请求格子。
        /// </remarks>
        /// <param name="position">以世界单位计的坐标。</param>
        /// <param name="step">格子边长。为零或负数时返回 0，而不是拿它去除。</param>
        public static int CellIndexOf(float position, float step)
        {
            if (step <= 0.0f)
                return 0;

            return (int)MathF.Floor(position / step);
        }

        /// <summary>
        /// 这一帧是否应该请求体积生成。
        /// </summary>
        /// <param name="crossedCell">被跟随的点所在格子与上次请求时不同。</param>
        /// <param name="deferredCells">上一次请求因预算没吃下的格子数。</param>
        /// <param name="madeProgress">上一次请求缩小了那份积压；为 false 就停掉重试循环。</param>
        public static bool ShouldRequest(bool crossedCell, int deferredCells, bool madeProgress)
        {
            if (crossedCell)
                return true;

            return deferredCells > 0 && madeProgress;
        }

        /// <summary>
        /// 一个位置映到的格子下标对，给需要一次拿到两个轴的调用方。
        /// </summary>
        public static (int X, int Z) CellOf(in Vector3 position, float step) =>
            (CellIndexOf(position.X, step), CellIndexOf(position.Z, step));

        /// <summary>
        /// 被跟随的点要移动多远才算进入了新的邻域。
        /// </summary>
        /// <remarks>
        /// 分层时，除非取最细的那一档，否则没有哪个步长能同时适合所有层：
        /// 一个装着 800 单位层和 100 单位层的体积会每 800 单位才切一次，
        /// 而细那一层的窗口会落后玩家多达八个格子——在场景里的观感就是
        /// 地面细节迟迟不出，直到玩家几乎走出一公里才出现。最小的正数层格子尺寸
        /// 是唯一一档让每一层窗口都保持最新的步长。被关掉的层、或没有可用尺寸的层，
        /// 会被跳过，而不是去赢下这个最小值、把跟随整个关掉；
        /// 对尺寸无话可说的体积退回调用方自己的默认值。
        /// </remarks>
        /// <param name="explicitStep">调用方坚持要用的步长；正数直接胜出。</param>
        /// <param name="defaultSize">体积自己的分区格子尺寸，没有层发声时使用。</param>
        /// <param name="layers">体积的层列表；没有层时为空。</param>
        public static float FollowStep(float explicitStep, float defaultSize, PCGPartitionLayer[] layers)
        {
            if (explicitStep > 0.0f)
                return explicitStep;

            float smallest = 0.0f;

            if (layers != null)
            {
                for (int i = 0; i < layers.Length; i++)
                {
                    var layer = layers[i];
                    if (layer == null || !layer.Enabled || layer.CellSize <= 0.0f)
                        continue;

                    if (smallest <= 0.0f || layer.CellSize < smallest)
                        smallest = layer.CellSize;
                }
            }

            return smallest > 0.0f ? smallest : defaultSize;
        }
    }
}
