using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin.Conditions
{
    /// <summary>
    /// 游戏难度条件。对应 UE5 蓝图资产 <c>BPC_DifficultyCheck</c>。
    /// <para>
    /// 二进制证据：CheckCondition 图为 <c>CallFunc_GetGameplayDifficultyLevel</c> →
    /// <c>CallFunc_Conv_ByteToInt</c> → <c>CallFunc_GreaterEqual_IntInt</c> 与蓝图变量
    /// <c>Difficulty</c> 比较（即「当前难度 &ge; 要求难度」）。
    /// 端口读 <c>NarrativeGameUserSettings.Get().GetGameplayDifficulty()</c>（与
    /// <c>GetCurrentDifficultyForTokens</c> 同一数据源），按底层整数值比较。
    /// </para>
    /// </summary>
    public class DifficultyCheckCondition : NarrativeCondition
    {
        /// <summary>要求达到的最低难度整数值（Easy=0…Insane=3）。对应 UE5 蓝图变量 <c>Difficulty</c>。</summary>
        public int Difficulty { get; set; } = 0;

        public override bool CheckCondition(object target, object controller, TalesComponent narrativeComponent)
        {
            var userSettings = NarrativePro.UnrealFramework.NarrativeGameUserSettings.Get();
            if (userSettings == null)
            {
                // 对齐 C++ GetGameplayDifficultyLevel 的空配置默认（Easy=0）：无设置时只有难度<=0 才成立。
                return 0 >= Difficulty;
            }

            int current = (int)userSettings.GetGameplayDifficulty();
            return current >= Difficulty;
        }

        public override string GetGraphDisplayText() => $"Difficulty >= {Difficulty}";
    }
}
