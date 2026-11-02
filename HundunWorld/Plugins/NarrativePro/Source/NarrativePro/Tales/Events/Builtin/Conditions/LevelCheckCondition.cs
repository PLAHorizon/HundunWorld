using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin.Conditions
{
    /// <summary>
    /// 角色等级条件。对应 UE5 蓝图资产 <c>BPC_LevelCheck</c>。
    /// <para>
    /// 二进制证据：CheckCondition 图为 DynamicCast 到 NarrativeCharacter →
    /// <c>CallFunc_GetCharacterLevel</c> → <c>CallFunc_Conv_IntToInt64</c> +
    /// <c>CallFunc_GreaterEqual_IntInt</c> 与蓝图变量 <c>RequiredLevel</c> 比较。
    /// 端口用 <c>NarrativeCharacter.GetCharacterLevel()</c>（UnrealFramework/NarrativeCharacter.cs:758）。
    /// </para>
    /// </summary>
    public class LevelCheckCondition : NarrativeCondition
    {
        /// <summary>要求达到的最低等级。对应 UE5 蓝图变量 <c>RequiredLevel</c>。</summary>
        public int RequiredLevel { get; set; } = 1;

        public override bool CheckCondition(object target, object controller, TalesComponent narrativeComponent)
        {
            var character = ArsenalStatics.ResolveCharacterFromContext(target, controller);
            if (character == null)
                return false;

            return character.GetCharacterLevel() >= RequiredLevel;
        }

        public override string GetGraphDisplayText() => $"Level >= {RequiredLevel}";
    }
}
