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
        /// 阵营关系走 <see cref="NarrativePro.Core.ArsenalStatics.GetAttitude"/>（Factions 标签容器口径）。
        /// </summary>
        public override Color GetMarkerColor(NarrativeNavigationComponent selector, GameplayTag navigatorType)
        {
            var owner = ActorOwner;
            if (owner == null) return base.GetMarkerColor(selector, navigatorType);

            // selector 是 NarrativeNavigationComponent（Script），通过 Actor 获取其拥有者
            var selectorActor = selector?.Actor;
            if (selectorActor == null) return base.GetMarkerColor(selector, navigatorType);

            ENavigationAttitude attitude = GetAttitude(selectorActor, owner);
            return NavigatorStatics.GetColorForAttitude(attitude);
        }

        /// <summary>
        /// 获取 selector 拥有者对 target 的敌对态度。
        /// 对应 UE5 <c>UCharacterMapMarker::GetAttitude</c>（走 ATeamAgentInterface 的阵营容器）：
        /// Flax 无 TeamAgentInterface，此处使用 <see cref="NarrativePro.Core.ArsenalStatics"/> 的阵营查询与态度映射。
        /// </summary>
        protected virtual ENavigationAttitude GetAttitude(Actor selectorActor, Actor targetActor)
        {
            if (selectorActor == null || targetActor == null) return ENavigationAttitude.Neutral;

            // 本地玩家：与导航组件登记的本地玩家 Actor 相同
            if (ReferenceEquals(selectorActor, targetActor)) return ENavigationAttitude.Player;
            var localPlayer = selectorActor.GetScript<NarrativeNavigationComponent>()?.ResolveLocalPlayerActor();
            if (localPlayer != null && localPlayer == targetActor) return ENavigationAttitude.Player;

            var attitude = NarrativePro.Core.ArsenalStatics.GetAttitude(selectorActor, targetActor);
            switch (attitude)
            {
                case NarrativePro.Core.ArsenalStatics.ETeamAttitude.Friendly: return ENavigationAttitude.Friendly;
                case NarrativePro.Core.ArsenalStatics.ETeamAttitude.Hostile: return ENavigationAttitude.Hostile;
                default: return ENavigationAttitude.Neutral;
            }
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
