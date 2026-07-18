using System;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.UnrealFramework
{
    /// <summary>
    /// 通用队伍代理接口。对应 UE5 IGenericTeamAgentInterface。
    /// Flax 无等价接口，改为 C# interface 占位。
    /// 提供 Actor 的队伍/阵营态度查询能力。
    /// </summary>
    public interface IGenericTeamAgentInterface
    {
        /// <summary>获取朝向目标 Actor 的态度。对应 UE5 GetTeamAttitudeTowards。</summary>
        /// <param name="other">目标 Actor。</param>
        /// <returns>态度枚举（Friendly/Neutral/Hostile）。</returns>
        ArsenalStatics.ETeamAttitude GetTeamAttitudeTowards(Actor other);
    }

    /// <summary>
    /// Narrative 队伍代理接口。对应 UE5 INarrativeTeamAgentInterface。
    /// UE5 中继承 IGenericTeamAgentInterface；Flax 中改为 C# interface 继承 IGenericTeamAgentInterface。
    /// 在 Narrative Pro 中使用基于 GameplayTag 的自定义队伍代理接口，
    /// 相对 UE5 通用 team ID 更清晰、更便于设计师使用。
    /// 简化点：
    /// - FGameplayTag → GameplayTag，FGameplayTagContainer → GameplayTagContainer
    /// - AActor → FlaxEngine.Actor
    /// - ETeamAttitude::Type → ArsenalStatics.ETeamAttitude
    /// - 移除 UE5 UInterface（C# 无此概念）
    /// </summary>
    public interface INarrativeTeamAgentInterface : IGenericTeamAgentInterface
    {
        /// <summary>为代理添加一个阵营。对应 UE5 AddFaction(Faction)。
        /// 阵营标签应属于 Narrative.Factions 类别。</summary>
        /// <param name="faction">阵营标签。</param>
        void AddFaction(GameplayTag faction);

        /// <summary>从代理移除一个阵营。对应 UE5 RemoveFaction(Faction)。
        /// 阵营标签应属于 Narrative.Factions 类别。</summary>
        /// <param name="faction">阵营标签。</param>
        void RemoveFaction(GameplayTag faction);

        /// <summary>返回此代理所在的阵营。对应 UE5 GetFactions。</summary>
        /// <returns>阵营标签容器。</returns>
        GameplayTagContainer GetFactions();
    }
}
