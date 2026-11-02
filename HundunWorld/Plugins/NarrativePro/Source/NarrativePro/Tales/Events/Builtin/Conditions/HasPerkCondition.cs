using NarrativePro.Core;
using NarrativePro.SkillTrees;

namespace NarrativePro.Tales.Events.Builtin.Conditions
{
    /// <summary>
    /// 拥有 Perk（技能树天赋）条件。对应 UE5 蓝图资产 <c>BPC_HasPerk</c>。
    /// <para>
    /// 二进制证据：CheckCondition 图为 <c>CallFunc_GetComponentByClass</c>（ComponentClass）→
    /// <c>CallFunc_HasPerk</c>，与蓝图变量 <c>Perk</c>/<c>PerkToCheck</c> 比对；
    /// DynamicCast 到 NarrativePlayerState/BP_NarrativePlayerCharacter。
    /// 端口先取 PlayerState 上的 <see cref="SkillTreeComponent.HasPerk"/>（perkClassPath 索引），
    /// 取不到再回退角色 Actor 上的同名组件（见 <c>ResolveSkillTree</c>）。
    /// </para>
    /// </summary>
    public class HasPerkCondition : NarrativeCondition
    {
        /// <summary>要检查的 Perk 类路径。对应 UE5 蓝图变量 <c>Perk</c>（Perk 类）。</summary>
        public string PerkClassId { get; set; } = "";

        public override bool CheckCondition(object target, object controller, TalesComponent narrativeComponent)
        {
            if (string.IsNullOrEmpty(PerkClassId))
            {
                NarrativeLog.LogWarning("HasPerkCondition: PerkClassId 为空，条件按失败处理");
                return false;
            }

            var character = ArsenalStatics.ResolveCharacterFromContext(target, controller);
            var actor = character?.Actor ?? (target as FlaxEngine.Actor);
            if (actor == null)
                return false;

            var skillTree = ResolveSkillTree(character, actor);
            return skillTree != null && skillTree.HasPerk(PerkClassId);
        }

        /// <summary>
        /// 取技能树组件。UE5 的 BPC_HasPerk 用 <c>GetComponentByClass</c> 打在 <b>PlayerState</b> 上
        /// （组件由 <c>ANarrativePlayerState</c> 构造期 <c>CreateDefaultSubobject</c> 创建，
        /// NarrativePlayerState.cpp:26），所以这里先走 PlayerState，再回退角色 Actor
        /// （宿主也可以把组件直接挂在角色身上）。
        /// </summary>
        private static SkillTreeComponent ResolveSkillTree(
            NarrativePro.UnrealFramework.NarrativeCharacter character, FlaxEngine.Actor actor)
        {
            var playerCharacter = character as NarrativePro.UnrealFramework.NarrativePlayerCharacter;
            var skillTree = playerCharacter?.GetNarrativePlayerState()?.GetSkillTreeComponent();
            if (skillTree != null) return skillTree;

            return actor.GetScript<SkillTreeComponent>();
        }

        public override string GetGraphDisplayText() => $"Has Perk: {PerkClassId}";
    }
}
