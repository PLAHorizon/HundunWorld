using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.AI;
using NarrativePro.GAS;
using NarrativePro.Items;
using NarrativePro.Navigation;

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

        /// <summary>
        /// 检查两点之间是否存在可通行的完整导航路径。对应 UE5 <c>UArsenalStatics::CheckPathExists</c>。
        /// <para>
        /// <b>降级语义</b>：导航网格未生成/构建中时，Flax 的查询一律返回 false。
        /// 若直接采信会把所有远程移动判定为「不可达」，因此未就绪时返回 true（视为未知而非不可达）。
        /// </para>
        /// </summary>
        /// <param name="start">起点</param>
        /// <param name="end">终点</param>
        /// <param name="agent">发起查询的 Agent（当前未参与过滤，保留签名以对齐 UE5）</param>
        /// <param name="maxDistance">大于 0 时，超距直接判负（避免昂贵的远距离查询）</param>
        public static bool CheckPathExists(Vector3 start, Vector3 end, Actor agent = null, float maxDistance = 0f)
        {
            if (maxDistance > 0f && Vector3.Distance(start, end) > maxDistance) return false;

            if (!NavigatorStatics.IsNavigationReady()) return true;

            return NavigatorStatics.TestPath(start, end);
        }

        // ===================================================================
        // 传送落点求解
        // ===================================================================

        /// <summary>默认 Agent 半径（未找到 CharacterController 时使用）</summary>
        public const float DefaultAgentRadius = 40f;

        /// <summary>默认 Agent 站立高度（未找到 CharacterController 时使用）</summary>
        public const float DefaultAgentHeight = 180f;

        /// <summary>落点重采样的搜索半径</summary>
        public const float DefaultSpotSearchRadius = 1000f;

        /// <summary>落点重采样的最大尝试次数</summary>
        public const int DefaultSpotSearchAttempts = 8;

        /// <summary>
        /// 求解一个安全的传送落点。对应 UE5 <c>UArsenalStatics::BPFindTeleportSpot</c>（<c>UWorld::FindTeleportSpot</c>）。
        /// <para>
        /// 策略：① 把目标点投影到导航网格（保证可行走）；② 做胶囊净空校验（避免落进几何体）；
        /// ③ 净空失败时在半径内随机重采样若干次；④ 仍未找到则回退到导航网格投影点（其本身已保证可行走）并告警。
        /// 导航网格不可用时退化为纯几何净空校验，不因导航未就绪而阻断传送。
        /// </para>
        /// </summary>
        /// <param name="actor">被传送的 Actor（用于取胶囊尺寸）</param>
        /// <param name="location">入参为目标点，出参为求解出的落点</param>
        /// <param name="rotation">保留朝向（此方法不修改）</param>
        /// <param name="searchRadius">重采样搜索半径</param>
        /// <param name="maxAttempts">重采样最大次数</param>
        public static bool FindTeleportSpot(Actor actor, ref Vector3 location, ref Quaternion rotation,
            float searchRadius = DefaultSpotSearchRadius, int maxAttempts = DefaultSpotSearchAttempts)
        {
            float radius = DefaultAgentRadius;
            float height = DefaultAgentHeight;
            ResolveAgentSize(actor, ref radius, ref height);

            if (NavigatorStatics.IsNavigationReady() && NavigatorStatics.TryFindClosestPoint(location, out var projected))
            {
                if (IsTeleportSpotClear(projected, radius, height))
                {
                    location = projected;
                    return true;
                }

                for (int i = 0; i < maxAttempts; i++)
                {
                    if (!NavigatorStatics.TryFindRandomPointAroundCircle(projected, searchRadius, out var candidate)) break;
                    if (IsTeleportSpotClear(candidate, radius, height))
                    {
                        location = candidate;
                        return true;
                    }
                }

                // 未找到完全净空的点：回退到导航网格投影点（本身保证可行走），仅告警不阻断
                NarrativeLog.LogWarning($"FindTeleportSpot: 半径 {searchRadius} 内未找到完全净空的落点，回退到导航网格投影点 {projected}");
                location = projected;
                return true;
            }

            // 导航网格不可用：退化为纯几何净空校验
            return IsTeleportSpotClear(location, radius, height);
        }

        /// <summary>取 Actor 上（或子节点上）的 CharacterController，用于读取胶囊尺寸。</summary>
        public static CharacterController FindCharacterController(Actor actor)
        {
            if (actor == null) return null;

            if (actor is CharacterController self) return self;
            return actor.GetChild<CharacterController>();
        }

        /// <summary>
        /// 从事件/任务的上下文对象中解析出 Narrative 角色。
        /// 对应 UE5 中从 <c>target</c> / <c>controller</c> 取 Pawn 的惯用做法，
        /// 依次尝试：直接是角色 → 目标 Actor 上的角色脚本 → 目标上的玩家控制器 → 控制器的受控角色 → 场景中的玩家角色。
        /// </summary>
        public static NarrativePro.UnrealFramework.NarrativeCharacter ResolveCharacterFromContext(object target, object controller)
        {
            if (target is NarrativePro.UnrealFramework.NarrativeCharacter direct) return direct;

            if (target is Actor targetActor)
            {
                var onActor = targetActor.GetScript<NarrativePro.UnrealFramework.NarrativeCharacter>();
                if (onActor != null) return onActor;

                var pcOnTarget = targetActor.GetScript<NarrativePro.UnrealFramework.NarrativePlayerController>();
                var controlled = pcOnTarget?.GetControlledCharacter();
                if (controlled != null) return controlled;
            }

            if (controller is NarrativePro.UnrealFramework.NarrativePlayerController playerController)
            {
                return playerController.GetControlledCharacter();
            }

            if (controller is Actor controllerActor)
            {
                var pc = controllerActor.GetScript<NarrativePro.UnrealFramework.NarrativePlayerController>();
                var controlled = pc?.GetControlledCharacter();
                if (controlled != null) return controlled;
            }

            return Level.FindScript<NarrativePro.UnrealFramework.NarrativePlayerCharacter>()
                ?? Level.FindScript<NarrativePro.UnrealFramework.NarrativeCharacter>();
        }

        /// <summary>
        /// 把 Actor 传送到指定位置/朝向（不要求该 Actor 是 <c>NarrativeCharacter</c>）。
        /// 若该 Actor 上恰好挂有 <c>NarrativeCharacter</c>，一并触发其传送回调以保持 UE5 语义。
        /// </summary>
        public static void TeleportActor(Actor actor, Vector3 location, Quaternion rotation)
        {
            if (actor == null) return;

            actor.Position = location;
            actor.Orientation = rotation;

            var controller = FindCharacterController(actor);
            if (controller != null)
            {
                controller.Position = location;
                controller.Orientation = rotation;
            }

            var narrativeCharacter = actor.GetScript<NarrativePro.UnrealFramework.NarrativeCharacter>();
            narrativeCharacter?.OnTeleportSucceeded(false);
        }

        /// <summary>
        /// 把角色传送到指定位置/朝向，并同步 CharacterController（避免其位置缓存覆盖传送结果导致回弹），
        /// 最后触发 <see cref="NarrativePro.UnrealFramework.NarrativeCharacter.OnTeleportSucceeded"/>。
        /// 调用方应先用 <see cref="FindTeleportSpot"/> 求解安全落点。
        /// </summary>
        public static void TeleportCharacter(NarrativePro.UnrealFramework.NarrativeCharacter character, Vector3 location, Quaternion rotation)
        {
            TeleportActor(character?.Actor, location, rotation);
        }

        /// <summary>按 Actor 的 CharacterController 覆盖 Agent 胶囊尺寸；未找到时保持默认值。</summary>
        public static void ResolveAgentSize(Actor actor, ref float radius, ref float height)
        {
            var controller = FindCharacterController(actor);
            if (controller == null) return;

            if (controller.Radius > 0f) radius = controller.Radius;
            if (controller.Height > 0f) height = controller.Height;
        }

        /// <summary>
        /// 检查落点是否有地面支撑且站立空间净空。
        /// <b>hitTriggers 必须传 false</b>：否则 POI 的发现触发器会把自己判定为阻挡者。
        /// </summary>
        public static bool IsTeleportSpotClear(Vector3 location, float radius, float height)
        {
            if (radius <= 0f) radius = DefaultAgentRadius;
            if (height <= 0f) height = DefaultAgentHeight;

            // ① 脚下必须有地面，避免悬空落点
            float probeHeight = height + 2f;
            Vector3 probeOrigin = location + Vector3.Up * probeHeight;
            if (!Physics.RayCast(probeOrigin, Vector3.Down, out var groundHit, probeHeight * 2f, uint.MaxValue, false))
            {
                return false;
            }

            // ② 用上下两个球体近似胶囊，检查站立空间是否被实体占用
            Vector3 lower = groundHit.Point + Vector3.Up * (height * 0.5f);
            if (Physics.CheckSphere(lower, radius, uint.MaxValue, false)) return false;

            Vector3 upper = groundHit.Point + Vector3.Up * Mathf.Max(radius, height - radius);
            if (Physics.CheckSphere(upper, radius, uint.MaxValue, false)) return false;

            return true;
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
