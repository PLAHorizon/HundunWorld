using System;
using System.Collections.Generic;

namespace HundunWorld.Game.Gasp
{
    /// <summary>
    /// 脚步时间线：判断一次主剪辑时间推进 (from, to] 跨过了哪些落脚时刻。
    /// 纯数学、可离线对拍；循环剪辑的 to &lt; from 视为“跨过 0 点”。
    /// </summary>
    public static class GaspFootstepTimeline
    {
        private const float Eps = 1e-5f;

        /// <summary>
        /// 收集区间内的落脚索引（写入 <paramref name="outIndices"/>，按时间顺序）。
        /// </summary>
        /// <param name="times">落脚时刻（升序）。</param>
        /// <param name="from">上一帧剪辑时间（不含）。</param>
        /// <param name="to">本帧剪辑时间（含）。</param>
        /// <param name="loop">是否循环剪辑。</param>
        /// <param name="length">剪辑长度（秒；循环回绕时用于切分区间）。</param>
        /// <returns>命中数量。</returns>
        public static int Collect(float[] times, float from, float to, bool loop, float length,
            List<int> outIndices)
        {
            outIndices?.Clear();
            if (times == null || times.Length == 0 || outIndices == null)
                return 0;

            if (to >= from - Eps)
            {
                for (int i = 0; i < times.Length; i++)
                {
                    if (times[i] > from + Eps && times[i] <= to + Eps)
                        outIndices.Add(i);
                }
            }
            else if (loop)
            {
                // (from, length] + [0, to]
                for (int i = 0; i < times.Length; i++)
                {
                    if (times[i] > from + Eps && times[i] <= length + Eps)
                        outIndices.Add(i);
                }
                for (int i = 0; i < times.Length; i++)
                {
                    if (times[i] <= to + Eps)
                        outIndices.Add(i);
                }
            }
            return outIndices.Count;
        }
    }
}
