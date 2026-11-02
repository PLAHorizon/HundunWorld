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

        // 注：本文件里三条 UE5 死声明已删除，避免"看着像已复刻其实没人调"：
        //   GetGPUInfo（编辑器工具用）、IsActorNetStartup（ArsenalStatics.cpp:183，UE5 内零调用方）、
        //   GetGameDefaultMapName（:308，同样零调用方）。端口没有蓝图，这类 BlueprintPure 静态工具
        //   一旦没有 C++ 调用方就没有存在意义；保留下来的都必须有活调用方。

        /// <summary>获取标签友好显示名。</summary>
        public static bool GetGameplayTagFriendlyDisplayName(GameplayTag tag, out string outText)
        {
            outText = ArsenalSettings.Instance.GetTagFriendlyDisplayName(tag);
            return !string.IsNullOrEmpty(outText);
        }

        /// <summary>
        /// 检查两个 Actor 是否同一队伍。引擎侧薄适配：取阵营容器后交给纯规则层
        /// （<see cref="NarrativeFactionAttitude.SharesAnyFaction"/>），harness 驱动的是同一条比较逻辑。
        /// </summary>
        public static bool IsSameTeam(Actor testActor, Actor target)
        {
            if (testActor == null || target == null) return false;
            var testFactions = GetActorFactions(testActor);
            var targetFactions = GetActorFactions(target);
            return NarrativeFactionAttitude.SharesAnyFaction(testFactions.GetTags(), targetFactions.GetTags());
        }

        /// <summary>队伍态度枚举（对应 UE5 ETeamAttitude）。</summary>
        public enum ETeamAttitude
        {
            Friendly,
            Neutral,
            Hostile
        }

        /// <summary>
        /// 获取 TestActor 对 Target 的态度。引擎侧薄适配：Actor → 阵营名集合 + "是否同一个 Actor"，
        /// 判定全部在 <see cref="NarrativeFactionAttitude.ComputeActorAttitude"/>（纯函数、进程外可断言）。
        /// <para>
        /// 口径保持与原实现逐支对应：任一 Actor 为 null → Neutral；同一 Actor / 有共同阵营 → Friendly；
        /// 双方都无阵营 → Neutral（修复：此前一律 Hostile，无阵营的道具/NPC 被全员自动敌对）；
        /// 只要有一方登记了阵营而双方又不同队，维持 Hostile：匪徒（Bandits+HostileAll）
        /// 对友方阵营与无阵营目标依旧敌对，战斗行为不被削弱。
        /// </para>
        /// </summary>
        public static ETeamAttitude GetAttitude(Actor testActor, Actor target)
        {
            if (testActor == null || target == null) return ETeamAttitude.Neutral;
            return NarrativeFactionAttitude.ComputeActorAttitude(
                GetActorFactionNames(testActor), GetActorFactionNames(target), ReferenceEquals(testActor, target));
        }

        /// <summary>取 Actor 的阵营名集合（纯规则层的入参形态；引擎侧只做容器 → 名称的取值）。</summary>
        public static System.Collections.Generic.IEnumerable<string> GetActorFactionNames(Actor actor)
        {
            var factions = GetActorFactions(actor);
            return factions != null ? factions.GetTags() : System.Array.Empty<string>();
        }

        /// <summary>获取 Actor 的阵营标签。</summary>
        public static GameplayTagContainer GetActorFactions(Actor actor)
        {
            if (actor == null) return new GameplayTagContainer();
            // 与 NarrativePlayerController.ResolveActorFactions 同一套解析顺序：
            // 角色定义阵营 → PlayerState 阵营 → 控制器阵营。
            // 只读定义会让"宿主装配模式"的本地玩家永远没有阵营（本游戏的玩家 Actor 不是
            // NarrativeCharacter），NPC 的仇恨扫描与友军伤害判定因此把他当敌对目标。
            var factions = NarrativePro.UnrealFramework.NarrativePlayerController.ResolveActorFactions(actor);
            return factions ?? new GameplayTagContainer();
        }

        // 注：UE5 的 AddFactionsToActor/RemoveFactionsFromActor（ITeamAgentInterface 的
        // 逐 Actor 阵营覆写）在本端口无任何调用方，且阵营数据源为共享的 CharacterDefinition，
        // 就地修改会污染所有同定义角色，已作为死声明删除。

        /// <summary>获取 Narrative Pro 设置。</summary>
        public static ArsenalSettings GetNarrativeProSettings()
        {
            return ArsenalSettings.Instance;
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

        // 注：UE5 的 IsWithEditor / IsPointInWaterVolume（水体 Volume 查询）在本端口均无任何调用方；
        // Flax 1.12 无公开水体 Volume 脚本 API（FlaxEngine.CSharp.xml 核实无 WaterVolume 类型），
        // 两者作为死声明删除。

        // 注：UE5 的 IsObjectOwnedByNPC 与 Add/RemoveLooseGameplayTagsCount（依赖
        // FGameplayTagCountContainer 计数容器）在本端口无任何调用方，且 Flax 侧无对象→NPC 定义
        // 拥有关系注册表、ASC 松散标签计数等价物，已作为死声明删除。
        // 松散标签能力现经 NarrativeNPCController.AddLooseGameplayTags/RemoveLooseGameplayTags 提供。

        /// <summary>
        /// 查找 Actor 上的 NarrativeAbilitySystemComponent。
        /// <para>
        /// UE5 中 ASC 是角色/Actors 的类默认子对象，任何 Actor 上直接 GetScript 就能拿到；
        /// Flax 端口里它由宿主脚本构建（角色：<c>NarrativeCharacter.EnsureAbilitySystemComponent</c>；
        /// 道具：<c>NarrativeASCActor.GetAbilitySystemComponent</c>），因此这里做多级解析：
        /// 直接挂的 ASC → 角色脚本自有/兜底构建的 ASC → ASCActor 自持的 ASC →
        /// 控制器/玩家角色转手 → 场景内本地玩家角色，确保运行期不再有静默 null。
        /// </para>
        /// </summary>
        public static NarrativeAbilitySystemComponent GetNarrativeAbilitySystemComponent(Actor actor)
        {
            if (actor == null) return null;

            // 1) Actor 直接挂了 ASC 脚本（角色 OnEnable 构建后即在 Actor 上；
            //    运行期「宿主装配」模式下引导也是把 ASC 直接 AddScript 到宿主 Actor 上，同样命中这一级）
            var asc = actor.GetScript<NarrativeAbilitySystemComponent>();
            if (asc != null) return asc;

            // 2) Actor 上有角色脚本：走角色的构建入口（幂等），对应 UE5 类默认对象语义
            var character = actor.GetScript<NarrativePro.UnrealFramework.NarrativeCharacter>();
            if (character != null)
            {
                asc = character.GetAbilitySystemComponent();
                if (asc != null) return asc;
            }

            // 3) 带 ASC 的普通 Actor（可破坏物等）
            if (actor is NarrativeASCActor ascActor)
            {
                asc = ascActor.GetAbilitySystemComponent();
                if (asc != null) return asc;
            }

            // 4) 控制器 Actor：从其受控角色取（对应 UE5 GetPlayerController()->GetPawnAsc）；
            //    控制器没有受控角色时（宿主装配模式）NarrativePlayerController 会回落到宿主 ASC。
            var playerController = actor.GetScript<NarrativePro.UnrealFramework.NarrativePlayerController>();
            asc = playerController?.GetAbilitySystemComponent();
            if (asc != null) return asc;

            // 4.5) 控制器与宿主不同 Actor 的布局：从引导登记的本地玩家宿主取（缓存引用，避免每帧全场景扫描）
            var runtimeHost = NarrativePro.CommonUI.NarrativeRuntimeBootstrap.LocalPlayerHostActor;
            if (runtimeHost != null && (ReferenceEquals(runtimeHost, actor) || playerController != null))
            {
                asc = NarrativePro.CommonUI.NarrativeRuntimeBootstrap.LocalPlayerAbilitySystemComponent;
                if (asc != null) return asc;
            }

            // 5) 兜底：ASC 的 Avatar 指向本 Actor（脚本层级被外部改写的情形）
            var allActors = Level.GetActors<Actor>(true);
            if (allActors != null)
            {
                for (int i = 0; i < allActors.Length; i++)
                {
                    if (allActors[i] == null) continue;
                    var candidate = allActors[i].GetScript<NarrativeAbilitySystemComponent>();
                    if (candidate != null && candidate.GetAvatarOwner() == actor) return candidate;
                }
            }

            return null;
        }

        // ===================================================================
        // 本地玩家宿主解析（本游戏不实例化插件角色，插件组件挂在游戏宿主 Actor 上）
        // ===================================================================

        /// <summary>
        /// 游戏侧本地玩家宿主的命名约定（<c>Source/Game/GameSceneInitializer.cs:21</c> 的
        /// <c>PlayerActorName</c> 默认值即 "Player"）。
        /// </summary>
        public static readonly string[] LocalPlayerHostActorNames =
        {
            "Player",
            "PlayerActor",
            "LocalPlayer",
            "MainPlayer",
        };

        /// <summary>扫描层级（②-⑦）的重试节奏（秒）：这些层级每次要跑多次全场景扫描，必须限速。</summary>
        private const float LocalPlayerScanRetrySeconds = 2f;
        private static Actor _cachedScanHostActor;
        private static bool _cachedScanHostUsesCharacter;
        private static double _nextLocalPlayerScanTime;

        /// <summary>
        /// 解析本地玩家的宿主 Actor —— 不要求场景里存在 <c>NarrativeCharacter</c>。
        /// <para>
        /// 快路径：⓪ 游戏侧权威登记点（反射，解析成功后零成本）→ ① 引导已登记的宿主；
        /// 这两个层级每帧可安全调用。②-⑦ 需要 <c>Level.FindScript</c>/<c>Level.GetActors</c>
        /// 全场景扫描（约 8 次完整扫描/调用），改为最多每 <see cref="LocalPlayerScanRetrySeconds"/> 秒
        /// 重扫一次，窗口内复用上次缓存结果；找到有效宿主后同样直接复用缓存，不再扫描。
        /// </para>
        /// </summary>
        /// <param name="bUsesNarrativeCharacter">真表示找到的是真正的插件角色（引导应走角色路径，不做宿主装配）。</param>
        public static Actor ResolveLocalPlayerHostActor(out bool bUsesNarrativeCharacter)
        {
            bUsesNarrativeCharacter = false;

            // ⓪ 游戏侧的权威登记点：HundunWorldGame.LocalPlayerActor。
            //   游戏生成玩家后会把它改名为 LocalPlayer_{characterId}（Source/Game/HundunWorldGame.cs:2343），
            //   因此按名匹配根本不可靠，必须直接读这个属性；插件不能编译期引用 Game.CSharp，走反射。
            var gameLocalPlayer = ResolveGameLocalPlayerActor();
            if (IsValidActor(gameLocalPlayer))
            {
                if (gameLocalPlayer.GetScript<NarrativePro.UnrealFramework.NarrativeCharacter>() != null) bUsesNarrativeCharacter = true;
                return gameLocalPlayer;
            }

            // ① 引导已经登记过：直接复用，保证幂等与一致性
            var registered = NarrativePro.CommonUI.NarrativeRuntimeBootstrap.LocalPlayerHostActor;
            if (IsValidActor(registered))
            {
                if (registered.GetScript<NarrativePro.UnrealFramework.NarrativeCharacter>() != null) bUsesNarrativeCharacter = true;
                return registered;
            }

            // 已有一次扫描命中且宿主仍有效：直接复用，避免重复全场景扫描
            if (IsValidActor(_cachedScanHostActor))
            {
                bUsesNarrativeCharacter = _cachedScanHostUsesCharacter;
                return _cachedScanHostActor;
            }

            // ②-⑦ 扫描层级限速：窗口内保持上次结论（包括“没找到”），不做全场景扫描
            double now = Time.UnscaledGameTime;
            if (now < _nextLocalPlayerScanTime)
            {
                return null;
            }
            _nextLocalPlayerScanTime = now + LocalPlayerScanRetrySeconds;

            var found = ScanForLocalPlayerHost(out bUsesNarrativeCharacter);
            _cachedScanHostActor = found;
            _cachedScanHostUsesCharacter = bUsesNarrativeCharacter;
            return found;
        }

        /// <summary>
        /// 全场景扫描层级（②-⑦）。仅在限速窗口到期且 ⓪① 未命中时执行。
        /// 层级顺序与语义保持不变：控制器 → 插件玩家角色 → 约定命名 → 相机/交互组件 →
        /// 大小写不敏感名扫描 → 兜底任意非 NPC 插件角色。
        /// </summary>
        private static Actor ScanForLocalPlayerHost(out bool bUsesNarrativeCharacter)
        {
            bUsesNarrativeCharacter = false;

            // ② 场景里手工挂好的插件控制器（它的 Actor 就是宿主）
            var controller = Level.FindScript<NarrativePro.UnrealFramework.NarrativePlayerController>();
            if (controller != null && IsValidActor(controller.Actor)) return controller.Actor;

            // ③ 真正的插件玩家角色：走角色自身的子对象装配
            var playerCharacter = Level.FindScript<NarrativePro.UnrealFramework.NarrativePlayerCharacter>();
            if (playerCharacter != null && IsValidActor(playerCharacter.Actor))
            {
                bUsesNarrativeCharacter = true;
                return playerCharacter.Actor;
            }

            // ④ 本游戏的约定：场景里名为 "Player" 的 Actor（游戏自己的 PlayerController 挂在它上面）
            //   注：Flax 1.12 的 Actor.FindActor(string) 是「在子节点中查找」的实例方法，
            //   全局按名查找必须走 Level.GetActors 扫描（见 FindSceneActorByName）。
            for (int i = 0; i < LocalPlayerHostActorNames.Length; i++)
            {
                var named = FindSceneActorByName(LocalPlayerHostActorNames[i], System.StringComparison.Ordinal);
                if (IsValidActor(named)) return named;
            }

            // ⑤ 插件组件所在 Actor：玩法场景里已经摆放了 NarrativeCameraComponent / 交互组件
            var camera = Level.FindScript<NarrativePro.Camera.NarrativeCameraComponent>();
            if (camera != null && IsValidActor(camera.Actor)) return camera.Actor;

            var interaction = Level.FindScript<NarrativePro.Interaction.PlayerInteractionComponent>();
            if (interaction != null && IsValidActor(interaction.Actor)) return interaction.Actor;

            // ⑥ 全场景扫描：按名字匹配（大小写不敏感），并跳过插件自己的容器
            for (int n = 0; n < LocalPlayerHostActorNames.Length; n++)
            {
                var scanned = FindSceneActorByName(LocalPlayerHostActorNames[n], System.StringComparison.OrdinalIgnoreCase);
                if (IsValidActor(scanned)) return scanned;
            }

            // ⑦ 兜底：场景里任意「非 NPC」插件角色（NPC 由运行期生成，绝不能当成本地玩家宿主）
            var actors = Level.GetActors<Actor>(true);
            if (actors != null)
            {
                for (int i = 0; i < actors.Length; i++)
                {
                    var actor = actors[i];
                    if (!IsValidActor(actor)) continue;
                    if (NarrativePro.SaveSystem.NarrativeSaveContainerFilter.IsRuntimeBootstrapHost(actor)) continue;

                    var character = actor.GetScript<NarrativePro.UnrealFramework.NarrativeCharacter>();
                    if (character == null) continue;
                    if (character is NarrativePro.UnrealFramework.NarrativeNPCCharacter) continue;

                    bUsesNarrativeCharacter = true;
                    return actor;
                }
            }

            return null;
        }

        /// <summary>
        /// 按名字在已加载场景中查找 Actor（跳过插件运行期容器）。
        /// Flax 1.12 没有「按名的全局静态查找」，只能遍历 <c>Level.GetActors</c>。
        /// </summary>
        private static Actor FindSceneActorByName(string name, System.StringComparison comparison)
        {
            if (string.IsNullOrEmpty(name)) return null;

            var actors = Level.GetActors<Actor>(true);
            if (actors == null) return null;

            for (int i = 0; i < actors.Length; i++)
            {
                var actor = actors[i];
                if (!IsValidActor(actor)) continue;
                if (NarrativePro.SaveSystem.NarrativeSaveContainerFilter.IsRuntimeBootstrapHost(actor)) continue;
                if (ActorNameMatchesConvention(actor.Name, name, comparison)) return actor;
            }

            return null;
        }

        /// <summary>
        /// 宿主命名匹配：完全相等，或"约定名 + '_' / '-' / 数字"前缀形式。
        /// 必须容忍前缀，因为游戏会把本地玩家 Actor 改名为 <c>LocalPlayer_{characterId}</c>；
        /// 但要求分隔符，避免 "Player" 误命中 "PlayerSpawnPoint" 这类同前缀的场景对象。
        /// </summary>
        private static bool ActorNameMatchesConvention(string actorName, string conventionName, System.StringComparison comparison)
        {
            if (string.IsNullOrEmpty(actorName) || string.IsNullOrEmpty(conventionName)) return false;
            if (string.Equals(actorName, conventionName, comparison)) return true;
            if (actorName.Length <= conventionName.Length) return false;
            if (!actorName.StartsWith(conventionName, comparison)) return false;

            char separator = actorName[conventionName.Length];
            return separator == '_' || separator == '-' || char.IsDigit(separator);
        }

        private static System.Type _gameType;
        private static System.Reflection.PropertyInfo _gameInstanceProp;
        private static System.Reflection.PropertyInfo _gameLocalPlayerActorProp;
        private static bool _bGamePlayerRefProbed;
        private static bool _bGamePlayerProbeLogged;

        /// <summary>
        /// 反射读取 <c>HundunWorld.Game.HundunWorldGame.Instance.LocalPlayerActor</c>。
        /// 反射信息一次解析后长期缓存；值为空时不缓存结果（玩家稍后才生成）。
        /// <para>
        /// 修复（此前一次早失败会永久关闭 Tier ⓪）：<c>_bGamePlayerRefProbed</c> 只有在
        /// 类型与属性真正解析成功后才置位；Game.CSharp 尚未加载时保持廉价重试
        /// （仅一次 <c>Type.GetType</c>，无场景扫描），不可恢复的失败只告警一次。
        /// </para>
        /// </summary>
        private static Actor ResolveGameLocalPlayerActor()
        {
            if (!_bGamePlayerRefProbed)
            {
                try
                {
                    _gameType = System.Type.GetType("HundunWorld.Game.HundunWorldGame, Game.CSharp");
                }
                catch (System.Exception ex)
                {
                    if (!_bGamePlayerProbeLogged)
                    {
                        _bGamePlayerProbeLogged = true;
                        NarrativeLog.LogWarning($"[ArsenalStatics] 反射解析游戏本地玩家失败：{ex.Message}");
                    }
                    _gameType = null;
                }

                if (_gameType != null)
                {
                    _gameInstanceProp = _gameType.GetProperty("Instance",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                    _gameLocalPlayerActorProp = _gameType.GetProperty("LocalPlayerActor",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

                    if (_gameInstanceProp != null && _gameLocalPlayerActorProp != null)
                    {
                        // 真正解析成功才锁存“已探测”
                        _bGamePlayerRefProbed = true;
                    }
                    else
                    {
                        // 类型在但属性缺失＝游戏侧 API 不匹配：这是确定性失败，锁存并只告警一次
                        _bGamePlayerRefProbed = true;
                        if (!_bGamePlayerProbeLogged)
                        {
                            _bGamePlayerProbeLogged = true;
                            NarrativeLog.LogWarning("[ArsenalStatics] HundunWorldGame 已解析但缺少 Instance/LocalPlayerActor 属性，Tier ⓪ 永久降级到命名/组件探测层级。");
                        }
                    }
                }
                else if (!_bGamePlayerProbeLogged)
                {
                    // Game.CSharp 尚未加载：不锁存，后续调用继续廉价重试；这里只提示一次
                    _bGamePlayerProbeLogged = true;
                    NarrativeLog.LogWarning("[ArsenalStatics] Game.CSharp 类型尚未加载，本地玩家 Tier ⓪ 暂不可用，将继续廉价重试。");
                }
            }

            if (_gameInstanceProp == null || _gameLocalPlayerActorProp == null) return null;

            try
            {
                var game = _gameInstanceProp.GetValue(null);
                if (game == null) return null;
                return _gameLocalPlayerActorProp.GetValue(game) as Actor;
            }
            catch
            {
                // 游戏侧属性不可用时静默降级到后续命名/组件探测层级
                return null;
            }
        }

        /// <summary>解析本地玩家宿主 Actor（不需要角色路径标记）。</summary>
        public static Actor ResolveLocalPlayerHostActor()
        {
            return ResolveLocalPlayerHostActor(out _);
        }

        /// <summary>该 Actor 是否就是本地玩家宿主（角色路径或宿主装配路径都算）。</summary>
        public static bool IsLocalPlayerHostActor(Actor actor)
        {
            if (actor == null) return false;
            if (NarrativePro.CommonUI.NarrativeRuntimeBootstrap.IsLocalPlayerHostActor(actor)) return true;
            return actor.GetScript<NarrativePro.UnrealFramework.NarrativeCharacter>() != null
                   || actor.GetScript<NarrativePro.UnrealFramework.NarrativePlayerController>() != null;
        }

        /// <summary>本地玩家 ASC（宿主装配模式与角色模式统一入口）。</summary>
        public static NarrativeAbilitySystemComponent GetLocalPlayerAbilitySystemComponent()
        {
            var host = ResolveLocalPlayerHostActor();
            return host != null ? GetNarrativeAbilitySystemComponent(host) : null;
        }

        /// <summary>
        /// 解析背包组件：既支持组件挂在 <c>NarrativeCharacter</c> 上的角色模式，
        /// 也支持引导把 <c>NarrativeInventoryComponent</c> 装配到本地玩家宿主的宿主模式。
        /// </summary>
        public static NarrativePro.Items.NarrativeInventoryComponent GetNarrativeInventoryComponent(Actor actor)
        {
            return GetLocalPlayerScopedComponent<NarrativePro.Items.NarrativeInventoryComponent>(actor);
        }

        /// <summary>解析玩家交互组件（角色模式 / 宿主装配模式皆可）。</summary>
        public static NarrativePro.Interaction.PlayerInteractionComponent GetPlayerInteractionComponent(Actor actor)
        {
            var direct = GetLocalPlayerScopedComponent<NarrativePro.Interaction.PlayerInteractionComponent>(actor);
            if (direct != null) return direct;

            // 角色侧缓存的交互组件（UE5 类默认对象语义）
            var character = actor != null ? actor.GetScript<NarrativePro.UnrealFramework.NarrativeCharacter>() : null;
            if (character != null)
            {
                var host = character.Actor != null ? character.Actor : actor;
                return host.GetScript<NarrativePro.Interaction.PlayerInteractionComponent>();
            }
            return null;
        }

        /// <summary>解析 Tales 组件（角色模式 / 宿主装配模式皆可）。</summary>
        public static NarrativePro.Tales.TalesComponent GetTalesComponent(Actor actor)
        {
            var direct = GetLocalPlayerScopedComponent<NarrativePro.Tales.TalesComponent>(actor);
            if (direct != null) return direct;

            var character = actor != null ? actor.GetScript<NarrativePro.UnrealFramework.NarrativeCharacter>() : null;
            return character?.GetTalesComponent();
        }

        /// <summary>
        /// 组件解析通用链：本 Actor 直挂 → 本 Actor 上的控制器所在 Actor → 引导登记的本地玩家宿主。
        /// NPC 等其它 Actor 不会被误落到玩家宿主上（只有玩家侧对象才走最后一跳）。
        /// </summary>
        private static T GetLocalPlayerScopedComponent<T>(Actor actor) where T : Script
        {
            if (actor == null) return null;

            var direct = actor.GetScript<T>();
            if (direct != null) return direct;

            var controller = actor.GetScript<NarrativePro.UnrealFramework.NarrativePlayerController>();
            if (controller != null && IsValidActor(controller.Actor) && !ReferenceEquals(controller.Actor, actor))
            {
                var onControllerActor = controller.Actor.GetScript<T>();
                if (onControllerActor != null) return onControllerActor;
            }

            bool isPlayerSide = IsLocalPlayerHostActor(actor);
            if (!isPlayerSide) return null;

            var host = NarrativePro.CommonUI.NarrativeRuntimeBootstrap.LocalPlayerHostActor;
            if (host == null || ReferenceEquals(host, actor)) return null;
            return host.GetScript<T>();
        }

        /// <summary>Actor 有效性判定（Flax 的已销毁原生对象在托管侧表现为 null）。</summary>
        private static bool IsValidActor(Actor actor)
        {
            return actor != null;
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

        // IsPointInWaterVolume 已删除：见上方说明（无调用方、Flax 无水体 Volume 脚本 API）。

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
