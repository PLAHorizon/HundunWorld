using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin.Conditions
{
    /// <summary>
    /// 距离条件。对应 UE5 蓝图资产 <c>BPC_CheckDistance</c>。
    /// <para>
    /// 二进制证据：CheckCondition 图为 <c>CallFunc_GetDistanceTo</c> →
    /// <c>CallFunc_Less_DoubleDouble</c> 与蓝图变量 <c>MaxDist</c> 比较；另一端 Actor 由
    /// <c>InstancedActorProvider</c>（蓝图变量 <c>OtherActor</c>）提供。
    /// <c>端口差异</c>：Flax 无「条件内注入具体 Actor 引用」的通路，端口按场景 Actor 名解析另一端，
    /// 遍历 <see cref="Level.GetActors{T}(bool)"/> 命中同名 Actor。
    /// </para>
    /// </summary>
    public class CheckDistanceCondition : NarrativeCondition
    {
        /// <summary>另一端 Actor 名称。对应 UE5 蓝图变量 <c>OtherActor</c>（端口以名字解析）。</summary>
        public string OtherActorName { get; set; } = "";

        /// <summary>最大允许距离（小于该值成立）。对应 UE5 蓝图变量 <c>MaxDist</c>。</summary>
        public float MaxDist { get; set; } = 0f;

        public override bool CheckCondition(object target, object controller, TalesComponent narrativeComponent)
        {
            var self = ResolveSelfActor(target, controller);
            if (self == null || string.IsNullOrEmpty(OtherActorName))
                return false;

            var other = FindActorByName(OtherActorName);
            if (other == null)
                return false;

            float distance = (self.Position - other.Position).Length;
            return distance < MaxDist;
        }

        public override string GetGraphDisplayText() => $"Distance To {OtherActorName} < {MaxDist}";

        private static Actor ResolveSelfActor(object target, object controller)
        {
            if (target is Actor targetActor)
                return targetActor;

            var character = ArsenalStatics.ResolveCharacterFromContext(target, controller);
            return character?.Actor;
        }

        private static Actor FindActorByName(string actorName)
        {
            try
            {
                foreach (var actor in Level.GetActors<Actor>())
                {
                    if (actor != null && actor.Name == actorName)
                        return actor;
                }
            }
            catch
            {
                // 场景未就绪：与端口内其它按名查找点保持同样的静默兜底。
            }
            return null;
        }
    }
}
