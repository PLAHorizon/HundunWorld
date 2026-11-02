using System;

namespace NarrativePro.Character
{
    /// <summary>
    /// 经验/等级换算的纯接缝（无引擎依赖，harness 断的是线上同一条实现）。
    /// <para>
    /// 逐条照抄 UE5 <c>ANarrativeCharacter</c>：
    /// </para>
    /// <list type="bullet">
    /// <item><description><c>XPToLevel</c>（NarrativeCharacter.cpp:1715-1718）＝
    /// <c>TruncToInt(LevelExponentX * Sqrt(XP)) + 1</c>。注意 <c>+1</c>：0 经验就是 1 级。</description></item>
    /// <item><description><c>LevelToXP</c>（<c>:1720-1723</c>）＝
    /// <c>Pow((Level - 1) / LevelExponentX, LevelExponentY)</c>，是上式的解析反函数。</description></item>
    /// <item><description><c>OnXPChanged</c>（<c>:1696-1703</c>）：等级只在
    /// "新旧 XP 换算出的等级不同"时才升级，一次加满多级的经验只报一次 <c>OnLevelUp</c>。</description></item>
    /// </list>
    /// <para>
    /// 端口此前把这两条写成 <c>pow(xp, X/Y)</c> / <c>pow(level, Y/X)</c>（源码注释自称"简化"）。
    /// 那不是同一条曲线：按 UE5 默认 <c>X=0.07, Y=2</c>，2 级门槛应是约 204 经验，
    /// 而旧式给出的门槛是 <c>2^(1/0.07) ≈ 4.1 亿</c>——等于升级永远不会发生。
    /// </para>
    /// </summary>
    public static class ExperienceRules
    {
        /// <summary>UE5 的 XP→等级。负数与 0 都按 0 经验处理，返回最低 1 级。</summary>
        public static int XpToLevel(float xp, float levelExponentX)
        {
            if (xp <= 0f || levelExponentX <= 0f) return 1;
            int truncated = (int)(levelExponentX * Math.Sqrt(xp));   // UE5 TruncToInt：向零截断
            return truncated + 1;
        }

        /// <summary>UE5 的等级→该级起始 XP。<c>level &lt;= 1</c> 时是 0。</summary>
        public static float LevelToXp(int level, float levelExponentX, float levelExponentY)
        {
            if (level <= 1 || levelExponentX <= 0f) return 0f;
            double baseValue = (level - 1) / (double)levelExponentX;
            return (float)Math.Pow(baseValue, levelExponentY);
        }

        /// <summary>UE5 <c>OnXPChanged</c> 的升级判据。</summary>
        public static bool DidLevelUp(float oldXp, float newXp, float levelExponentX)
        {
            return XpToLevel(newXp, levelExponentX) > XpToLevel(oldXp, levelExponentX);
        }

        /// <summary>
        /// UE5 <c>GetPercentToNextLevel</c> 的进度比例：(当前XP - 本级起点) / (下级起点 - 本级起点)，
        /// 夹到 [0,1]；上下级起点相同（退化配置）时按 0 处理。
        /// </summary>
        public static float PercentToNextLevel(
            float xp, int currentLevel, float levelExponentX, float levelExponentY)
        {
            float currentLevelXp = LevelToXp(currentLevel, levelExponentX, levelExponentY);
            float nextLevelXp = LevelToXp(currentLevel + 1, levelExponentX, levelExponentY);
            if (nextLevelXp <= currentLevelXp) return 0f;
            float ratio = (xp - currentLevelXp) / (nextLevelXp - currentLevelXp);
            if (ratio < 0f) return 0f;
            if (ratio > 1f) return 1f;
            return ratio;
        }
    }
}
