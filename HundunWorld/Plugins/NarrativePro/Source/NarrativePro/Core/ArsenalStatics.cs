using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.AI;
using NarrativePro.GAS;
using NarrativePro.Items;

namespace NarrativePro.Core
{
    /// <summary>
    /// GPU 信息。对应 UE5 FGPUInfo。
    /// </summary>
    [Serializable]
    public class GPUInfo
    {
        public int TotalVRAM = 0;
        public int CurrentVRAM = 0;
        public string GPUBrand = "";
    }

    /// <summary>
    /// Narrative Pro 静态工具函数库。对应 UE5 UArsenalStatics。
    /// 仅移植与 Flax 兼容的核心功能，移除 Mass/ZoneGraph/Vehicles/CommonUI 等 UE5 特有功能。
    /// </summary>
    public static class ArsenalStatics
    {
        /// <summary>获取显示器名称列表。</summary>
        public static List<string> GetMonitorNames()
        {
            // Flax 暂无原生多显示器枚举 API，返回主显示器
            return new List<string> { "Primary Monitor" };
        }

        /// <summary>获取 GPU 信息（仅 Windows）。返回是否成功。</summary>
        public static bool GetGPUInfo(out GPUInfo outInfo)
        {
            // Flax-不兼容: Flax 暂无原生 GPU 信息 API，保留占位。原文 TODO: Flax 暂无原生 GPU 信息 API
            outInfo = new GPUInfo();
            return false;
        }

        /// <summary>获取标签友好显示名。</summary>
        public static bool GetGameplayTagFriendlyDisplayName(GameplayTag tag, out string outText)
        {
            outText = ArsenalSettings.Instance.GetTagFriendlyDisplayName(tag);
            return !string.IsNullOrEmpty(outText);
        }

        /// <summary>检查 Actor 是否为网络启动 Actor。Flax 无网络启动概念，返回 false。</summary>
        public static bool IsActorNetStartup(Actor testActor)
        {
            return false;
        }

        /// <summary>检查两个 Actor 是否同一队伍。</summary>
        public static bool IsSameTeam(Actor testActor, Actor target)
        {
            if (testActor == null || target == null) return false;
            var testFactions = GetActorFactions(testActor);
            var targetFactions = GetActorFactions(target);
            return testFactions.HasAny(targetFactions);
        }

        /// <summary>队伍态度枚举（对应 UE5 ETeamAttitude）。</summary>
        public enum ETeamAttitude
        {
            Friendly,
            Neutral,
            Hostile
        }

        /// <summary>获取 TestActor 对 Target 的态度。</summary>
        public static ETeamAttitude GetAttitude(Actor testActor, Actor target)
        {
            if (testActor == null || target == null) return ETeamAttitude.Neutral;
            if (testActor == target) return ETeamAttitude.Friendly;
            if (IsSameTeam(testActor, target)) return ETeamAttitude.Friendly;
            return ETeamAttitude.Hostile;
        }

        /// <summary>获取 Actor 的阵营标签。</summary>
        public static GameplayTagContainer GetActorFactions(Actor actor)
        {
            var container = new GameplayTagContainer();
            if (actor == null) return container;
            // Flax-不兼容: UE5 的 TeamAgentInterface 在 Flax 无对应物，暂以空容器占位。原文 TODO: 通过 TeamAgentInterface 获取，Flax 中暂以 Script 查找
            return container;
        }

        /// <summary>向 Actor 添加阵营标签。</summary>
        public static void AddFactionsToActor(Actor actor, GameplayTagContainer factions)
        {
            // Flax-不兼容: UE5 的 TeamAgentInterface 在 Flax 无对应物，需自定义 Script 实现。原文 TODO: Flax 中无 TeamAgentInterface，需要自定义 Script 实现
        }

        /// <summary>从 Actor 移除阵营标签。</summary>
        public static void RemoveFactionsFromActor(Actor actor, GameplayTagContainer factions)
        {
            // Flax-不兼容: UE5 的 TeamAgentInterface 在 Flax 无对应物，保留占位。原文 TODO: Flax 中无 TeamAgentInterface
        }

        /// <summary>获取 Narrative Pro 设置。</summary>
        public static ArsenalSettings GetNarrativeProSettings()
        {
            return ArsenalSettings.Instance;
        }

        /// <summary>获取游戏默认地图名（通常是主菜单）。</summary>
        public static string GetGameDefaultMapName()
        {
            // Flax 中通过 Scene 查询
            return "";
        }

        /// <summary>获取游戏入口地图名。</summary>
        public static string GetGameEntryMapName()
        {
            return ArsenalSettings.Instance.GameEntryMap;
        }

        /// <summary>获取角色创建器地图名。</summary>
        public static string GetCharacterCreatorMapName()
        {
            return ArsenalSettings.Instance.CharacterCreatorMap;
        }

        /// <summary>获取当前屏幕分辨率。</summary>
        public static Vector2 GetGameResolution()
        {
            return Screen.Size;
        }

        /// <summary>获取游戏内时间。</summary>
        public static float GetTimeOfDay(object worldContextObject)
        {
            var gameState = NarrativePro.UnrealFramework.NarrativeGameState.GetCurrent();
            return gameState != null ? gameState.TimeOfDay : 0f;
        }

        /// <summary>检查时间是否在指定范围内（支持跨天循环）。</summary>
        public static bool IsTimeInRange(float time, float rangeStart, float rangeEnd)
        {
            if (rangeStart <= rangeEnd)
            {
                return time >= rangeStart && time <= rangeEnd;
            }
            // 跨天
            return time >= rangeStart || time <= rangeEnd;
        }

        /// <summary>当前是否为白天。</summary>
        public static bool IsDayTime(object worldContextObject)
        {
            float time = GetTimeOfDay(worldContextObject);
            return IsTimeInRange(time, 600f, 1800f);
        }

        /// <summary>获取游戏内时间字符串（如 16:35）。</summary>
        public static string GetTimeOfDayAsString(object worldContextObject)
        {
            return TimeToString(GetTimeOfDay(worldContextObject));
        }

        /// <summary>将浮点时间转为 24 小时字符串。</summary>
        public static string TimeToString(float time)
        {
            time = time % 2400f;
            if (time < 0) time += 2400f;
            int hours = (int)(time / 100f);
            int minutes = (int)((time % 100f) * 0.6f);
            return $"{hours:D2}:{minutes:D2}";
        }

        /// <summary>获取累计游戏时间。</summary>
        public static float GetTotalAccumulatedTime(object worldContextObject)
        {
            return Time.UnscaledGameTime;
        }

        /// <summary>检查是否在编辑器中运行。</summary>
        public static bool IsWithEditor()
        {
            // Flax 中判断是否在编辑器中运行
            return false; // Flax-不兼容: Flax 暂无直接判断是否在编辑器中运行的公开 API，保留占位。原文 TODO: Flax 暂无直接判断是否在编辑器中运行的 API
        }

        /// <summary>检查对象是否由指定 NPC 定义拥有。</summary>
        public static bool IsObjectOwnedByNPC(object testObject, NPCDefinition npcDefinition)
        {
            // TODO [需接入 NPC 拥有关系系统]: 需要实现 NPC 拥有关系
            return false;
        }

        /// <summary>查找 Actor 上的 NarrativeAbilitySystemComponent。</summary>
        public static NarrativeAbilitySystemComponent GetNarrativeAbilitySystemComponent(Actor actor)
        {
            if (actor == null) return null;
            return actor.GetScript<NarrativeAbilitySystemComponent>();
        }

        /// <summary>添加松散标签到 ASC。</summary>
        public static bool AddLooseGameplayTagsCount(Actor actor, GameplayTagContainer gameplayTags, int count, bool bDontAddIfAlreadyOwned)
        {
            var asc = GetNarrativeAbilitySystemComponent(actor);
            if (asc == null) return false;
            // TODO [需接入 GAS 松散标签计数系统]: 实现 ASC 松散标签计数（需 FGameplayTagCountContainer 等价物）
            return false;
        }

        /// <summary>移除松散标签。</summary>
        public static bool RemoveLooseGameplayTagsCount(Actor actor, GameplayTagContainer gameplayTags, int count)
        {
            var asc = GetNarrativeAbilitySystemComponent(actor);
            if (asc == null) return false;
            // TODO [需接入 GAS 松散标签计数系统]: 实现 ASC 松散标签计数移除（需 FGameplayTagCountContainer 等价物）
            return false;
        }

        /// <summary>查找最近点索引。</summary>
        public static int GetClosestPoint(List<Vector3> points, Vector3 location)
        {
            if (points == null || points.Count == 0) return -1;
            int bestIdx = 0;
            float bestDistSq = float.MaxValue;
            for (int i = 0; i < points.Count; i++)
            {
                float distSq = Vector3.DistanceSquared(points[i], location);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        /// <summary>计算碰撞前时间。</summary>
        public static float TimeUntilCollision(
            Vector3 agentLocation, Vector3 agentVelocity, float agentRadius,
            Vector3 obstacleLocation, Vector3 obstacleVelocity, float obstacleRadius)
        {
            Vector3 relativeVelocity = agentVelocity - obstacleVelocity;
            float speedSq = relativeVelocity.LengthSquared;
            if (speedSq < 1e-6f) return float.MaxValue;

            Vector3 toObstacle = obstacleLocation - agentLocation;
            float distance = toObstacle.Length;
            float sumRadii = agentRadius + obstacleRadius;

            if (distance <= sumRadii) return 0f;

            Vector3 dirToObstacle = toObstacle / distance;
            float closingSpeed = Vector3.Dot(relativeVelocity, dirToObstacle);
            if (closingSpeed <= 0f) return float.MaxValue;

            float distanceToCollision = distance - sumRadii;
            return distanceToCollision / closingSpeed;
        }

        /// <summary>检查点是否在水体中。</summary>
        public static bool IsPointInWaterVolume(object worldContextObject, Vector3 point)
        {
            // Flax-不兼容: Flax 无原生 Water Volume Actor，保留占位。原文 TODO: Flax 中需要检查 Water Volume Actor
            return false;
        }

        /// <summary>对给定数组进行排序。</summary>
        public static List<T> SortObjectArray_Comparator<T>(List<T> objectArray, Comparison<T> comparator, bool bReverse)
        {
            if (objectArray == null) return new List<T>();
            var result = new List<T>(objectArray);
            result.Sort(comparator);
            if (bReverse) result.Reverse();
            return result;
        }
    }
}
