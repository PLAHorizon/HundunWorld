using FlaxEngine;
using NarrativePro.Items;
using NarrativePro.Navigation;

namespace NarrativePro.Character
{
    /// <summary>
    /// 角色地图标记。对应 UE5 UCharacterMapMarker。
    /// 根据 selector 与 Owner 的关系（友/中/敌）显示不同颜色。
    /// 简化点：Flax 中无 NarrativeTeamAgentInterface，阵营查询逻辑简化为基础实现。
    /// </summary>
    public class CharacterMapMarker : MapMarker
    {
        /// <summary>
        /// 获取标记颜色：根据 Owner 与 selector 的关系返回 NavigationDeveloperSettings 中配置的颜色。
        /// 简化版：若 Owner == selector 的 Actor 则视为 Player，否则 Neutral。
        /// 待接入：完整阵营系统（Factions 标签容器）以判定 Friendly/Hostile。
        /// </summary>
        public override Color GetMarkerColor(NarrativeNavigationComponent selector, GameplayTag navigatorType)
        {
            var owner = ActorOwner;
            if (owner == null) return base.GetMarkerColor(selector, navigatorType);

            // selector 是 NarrativeNavigationComponent（Script），通过 Actor 获取其拥有者
            var selectorActor = selector?.Actor;
            if (selectorActor == null) return base.GetMarkerColor(selector, navigatorType);

            // TODO [需接入阵营系统]: 当前简化为 Player/Neutral 判定，需通过 Factions 标签容器以判定 Friendly/Hostile
            ENavigationAttitude attitude = GetAttitude(selectorActor, owner);
            return NavigatorStatics.GetColorForAttitude(attitude);
        }

        /// <summary>
        /// 获取 selector 拥有者对 target 的敌对态度。
        /// 简化版：相同 Actor 视为 Player，否则 Neutral。
        /// 待接入：Factions/GameplayTag 容器查询以返回 Friendly/Hostile。
        /// </summary>
        protected virtual ENavigationAttitude GetAttitude(Actor selectorActor, Actor targetActor)
        {
            if (selectorActor == targetActor) return ENavigationAttitude.Player;
            // TODO [需接入阵营系统]: 通过 Factions/GameplayTag 容器查询以返回 Friendly/Hostile，当前简化为 Neutral
            return ENavigationAttitude.Neutral;
        }

        public override void OnMarkerAdded(NarrativeNavigationComponent ownerNavComp)
        {
            base.OnMarkerAdded(ownerNavComp);
        }

        public override void OnMarkerRemoved(NarrativeNavigationComponent ownerNavComp)
        {
            base.OnMarkerRemoved(ownerNavComp);
        }
    }
}
