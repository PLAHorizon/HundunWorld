// 模块关系说明：
// 本文件属于 NarrativePro.SaveSystem（UE5 NarrativeSaveSystem 移植）。
//
// UE5 中玩家数据由 GameMode/PlayerController 体系自然提供：
// NarrativeSaveSubsystem.cpp:50-83 遍历 PlayerController，把 PC / PlayerState / Pawn 三份记录
// 折进 NarrativeSaveGame->PlayerData，并在 LoadPlayerData(:255-299) 里按 Pawn → PlayerState → PC
// 的顺序还原（关卡切换时保留 Pawn 的新 Transform）。
//
// Flax 没有 APlayerController/APlayerState/APawn 基类，本项目的“玩家”由三部分组成：
//   1) 场景里承载本地玩家的 Actor（Game 模块的 HundunWorldGame.LocalPlayerActor）；
//   2) UnrealFramework.NarrativePlayerController / NarrativePlayerState 这类挂载脚本；
//   3) NarrativePlayerCharacter / NarrativeCharacter 这类角色脚本。
// 因此本文件提供 NarrativePlayerDataRegistry：
//   - 其它模块可用一行 NarrativePlayerDataRegistry.RegisterLocalPlayer(pawn, controller, playerState) 指定玩家；
//   - 或让玩家相关对象实现 INarrativePlayerDataParticipant 声明自己的角色；
//   - 都没有时，注册表按接口 → 已知类型名 → 反射桥接 HundunWorldGame.LocalPlayerActor 的顺序自动发现。
// 还原侧提供 INarrativePlayerDataReceiver：物品/技能树/任务等子系统实现它即可在对应角色记录还原后收到通知。

using System;
using System.Collections.Generic;
using System.Reflection;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.SaveSystem
{
    /// <summary>玩家数据角色。对应 UE5 FNarrativeSavePlayer 的三个成员。</summary>
    public enum NarrativePlayerDataRole
    {
        /// <summary>玩家控制器（对应 UE5 APlayerController）。</summary>
        Controller = 0,

        /// <summary>玩家状态（对应 UE5 APlayerState，持有物品/阵营等跨生命周期数据）。</summary>
        PlayerState = 1,

        /// <summary>玩家 Pawn（对应 UE5 APawn / ANarrativePlayerCharacter）。</summary>
        Pawn = 2,
    }

    /// <summary>
    /// 玩家数据参与者。挂在 Actor 或 Script 上，声明自己扮演 PC / PlayerState / Pawn 中的哪一种，
    /// 存档子系统即可把其状态折进 NarrativeSaveGame.PlayerData。
    /// </summary>
    public interface INarrativePlayerDataParticipant
    {
        /// <summary>返回该对象在玩家数据中的角色。</summary>
        NarrativePlayerDataRole GetNarrativePlayerDataRole();
    }

    /// <summary>
    /// 玩家数据还原通知。Inventory / SkillTree / Tales 等子系统实现后，
    /// 在对应角色的记录被还原完成后会收到回调（对应 UE5 LoadPlayerData 中“先 Pawn 再 PlayerState 再 PC”的顺序约束）。
    /// </summary>
    public interface INarrativePlayerDataReceiver
    {
        /// <summary>某个角色的玩家数据已还原。</summary>
        /// <param name="role">刚还原的角色。</param>
        /// <param name="record">对应记录（可能为无效记录）。</param>
        void OnPlayerDataLoaded(NarrativePlayerDataRole role, NarrativeActorRecord record);
    }

    /// <summary>一次玩家解析得到的三个玩家侧对象。</summary>
    public sealed class NarrativePlayerSnapshot
    {
        /// <summary>玩家 Pawn Actor。</summary>
        public Actor Pawn;

        /// <summary>玩家控制器对象（Actor 或 Script）。</summary>
        public System.Object Controller;

        /// <summary>玩家状态对象（Actor 或 Script）。</summary>
        public System.Object PlayerState;

        /// <summary>是否有可保存的玩家（对应 UE5 要求 PS 与 Pawn 同时存在）。</summary>
        public bool IsValidForSave()
        {
            return Pawn != null;
        }

        /// <summary>按角色取对象。</summary>
        public System.Object GetObject(NarrativePlayerDataRole role)
        {
            switch (role)
            {
                case NarrativePlayerDataRole.Pawn: return Pawn;
                case NarrativePlayerDataRole.PlayerState: return PlayerState;
                case NarrativePlayerDataRole.Controller: return Controller;
                default: return null;
            }
        }
    }

    /// <summary>
    /// 玩家定位与登记中心。存档子系统据此捕获/还原 NarrativeSaveGame.PlayerData。
    /// </summary>
    public static class NarrativePlayerDataRegistry
    {
        // 显式登记的玩家三元组
        private static Actor _registeredPawn;
        private static System.Object _registeredController;
        private static System.Object _registeredPlayerState;

        private static Func<NarrativePlayerSnapshot> _externalResolver;
        private static readonly List<WeakReference<INarrativePlayerDataReceiver>> _receivers = new List<WeakReference<INarrativePlayerDataReceiver>>();
        private static readonly object _lock = new object();

        // 已知“影子类型名”（不产生编译期依赖，避免与并行修改的模块相互卡死）
        private static readonly string[] ControllerTypeNameHints = { "NarrativePlayerController", "PlayerController" };
        private static readonly string[] PlayerStateTypeNameHints = { "NarrativePlayerState", "PlayerState", "NarrativeGameState" };
        private static readonly string[] PawnTypeNameHints = { "NarrativePlayerCharacter", "NarrativeCharacter", "PlayerCharacter" };

        /// <summary>整体接管玩家解析（游戏侧可提供自己的解析器）。</summary>
        public static void SetResolver(Func<NarrativePlayerSnapshot> resolver)
        {
            _externalResolver = resolver;
        }

        /// <summary>登记本地玩家三元组。其它子系统一行接入即可让 PlayerData 被正确捕获。</summary>
        /// <param name="pawn">玩家 Actor（其 Transform 与状态构成 PawnData）。</param>
        /// <param name="controller">控制器对象（Actor 或 Script），可空。</param>
        /// <param name="playerState">玩家状态对象（Actor 或 Script），可空。</param>
        public static void RegisterLocalPlayer(Actor pawn, System.Object controller = null, System.Object playerState = null)
        {
            if (pawn != null) _registeredPawn = pawn;
            if (controller != null) _registeredController = controller;
            if (playerState != null) _registeredPlayerState = playerState;

            if (pawn != null)
            {
                NarrativeLog.Log($"[SaveSystem] 本地玩家已登记：{pawn.Name}");
            }
        }

        /// <summary>清除登记（玩家销毁/退出场景时调用）。</summary>
        public static void ClearLocalPlayer()
        {
            _registeredPawn = null;
            _registeredController = null;
            _registeredPlayerState = null;
        }

        /// <summary>登记一个玩家数据还原接收者。</summary>
        public static void RegisterReceiver(INarrativePlayerDataReceiver receiver)
        {
            if (receiver == null) return;
            lock (_lock)
            {
                foreach (var existing in _receivers)
                {
                    if (existing.TryGetTarget(out var target) && ReferenceEquals(target, receiver)) return;
                }
                _receivers.Add(new WeakReference<INarrativePlayerDataReceiver>(receiver));
            }
        }

        /// <summary>取消登记接收者。</summary>
        public static void UnregisterReceiver(INarrativePlayerDataReceiver receiver)
        {
            if (receiver == null) return;
            lock (_lock)
            {
                for (int i = _receivers.Count - 1; i >= 0; i--)
                {
                    if (!_receivers[i].TryGetTarget(out var target) || ReferenceEquals(target, receiver))
                    {
                        _receivers.RemoveAt(i);
                    }
                }
            }
        }

        /// <summary>
        /// 通知所有接收者（含场景内实现了 INarrativePlayerDataReceiver 的对象）。
        /// 对应 UE5 中 LoadPlayerData 之后各子系统（物品/任务）自行恢复的时机。
        /// </summary>
        public static void NotifyReceivers(NarrativePlayerDataRole role, NarrativeActorRecord record)
        {
            var snapshot = Resolve();

            // 1) 显式登记的接收者
            List<INarrativePlayerDataReceiver> registered = null;
            lock (_lock)
            {
                for (int i = _receivers.Count - 1; i >= 0; i--)
                {
                    if (!_receivers[i].TryGetTarget(out var target) || target == null)
                    {
                        _receivers.RemoveAt(i);
                        continue;
                    }
                    (registered ??= new List<INarrativePlayerDataReceiver>()).Add(target);
                }
            }

            if (registered != null)
            {
                foreach (var receiver in registered)
                {
                    InvokeReceiver(receiver, role, record);
                }
            }

            // 2) 玩家三元组自身实现的接收接口（PC/PS/Pawn 常自带恢复逻辑）
            InvokeReceiver(snapshot?.GetObject(role), role, record);
            InvokeReceiver(snapshot?.Pawn, role, record);

            // 3) 玩家 Actor 上的脚本
            var pawnActor = snapshot?.Pawn;
            if (pawnActor != null)
            {
                foreach (var script in NarrativeSavableActorResolver.GetScripts(pawnActor))
                {
                    InvokeReceiver(script, role, record);
                    if (script is INarrativeStableActor)
                    {
                        // 场景内其它稳定 Actor 不接收玩家事件，跳过
                    }
                }
            }
        }

        private static void InvokeReceiver(System.Object candidate, NarrativePlayerDataRole role, NarrativeActorRecord record)
        {
            if (candidate is not INarrativePlayerDataReceiver receiver) return;
            try
            {
                receiver.OnPlayerDataLoaded(role, record);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"[SaveSystem] OnPlayerDataLoaded 异常 {candidate.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// 解析当前玩家三元组。顺序：外部解析器 → 显式登记 → 接口声明 → 已知类型名匹配 → Game 模块反射桥接。
        /// </summary>
        public static NarrativePlayerSnapshot Resolve()
        {
            if (_externalResolver != null)
            {
                try
                {
                    var external = _externalResolver();
                    if (external != null && external.IsValidForSave()) return external;
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogWarning($"[SaveSystem] 外部玩家解析器异常: {ex.Message}");
                }
            }

            var snapshot = new NarrativePlayerSnapshot();

            // 显式登记优先
            snapshot.Pawn = _registeredPawn;
            snapshot.Controller = _registeredController;
            snapshot.PlayerState = _registeredPlayerState;

            // 场景中实现 INarrativePlayerDataParticipant 的对象
            ScanParticipants(snapshot);

            // 已知类型名兜底
            if (snapshot.Pawn == null) snapshot.Pawn = ResolveByTypeName(PawnTypeNameHints);
            if (snapshot.Controller == null) snapshot.Controller = FindScriptByTypeNameHints(ControllerTypeNameHints);
            if (snapshot.PlayerState == null) snapshot.PlayerState = FindScriptByTypeNameHints(PlayerStateTypeNameHints);

            // Pawn 仍为空时，反射桥接 Game 模块的本地玩家 Actor
            if (snapshot.Pawn == null)
            {
                snapshot.Pawn = ResolveLocalPlayerActorFromGameModule();
                if (snapshot.Pawn != null)
                {
                    if (snapshot.Controller == null) snapshot.Controller = snapshot.Pawn;
                }
            }

            if (!snapshot.IsValidForSave()) return null;
            return snapshot;
        }

        private static void ScanParticipants(NarrativePlayerSnapshot snapshot)
        {
            try
            {
                foreach (var actor in Level.GetActors<Actor>())
                {
                    if (actor == null) continue;

                    if (actor is INarrativePlayerDataParticipant actorParticipant)
                    {
                        ApplyParticipant(snapshot, actorParticipant.GetNarrativePlayerDataRole(), actor);
                    }

                    foreach (var script in NarrativeSavableActorResolver.GetScripts(actor))
                    {
                        if (script is INarrativePlayerDataParticipant participant)
                        {
                            ApplyParticipant(snapshot, participant.GetNarrativePlayerDataRole(), script);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"[SaveSystem] 玩家参与者扫描失败: {ex.Message}");
            }
        }

        private static void ApplyParticipant(NarrativePlayerSnapshot snapshot, NarrativePlayerDataRole role, System.Object candidate)
        {
            switch (role)
            {
                case NarrativePlayerDataRole.Pawn:
                    if (snapshot.Pawn == null)
                    {
                        snapshot.Pawn = candidate as Actor ?? (candidate as Script)?.Actor;
                    }
                    break;
                case NarrativePlayerDataRole.Controller:
                    if (snapshot.Controller == null) snapshot.Controller = candidate;
                    break;
                case NarrativePlayerDataRole.PlayerState:
                    if (snapshot.PlayerState == null) snapshot.PlayerState = candidate;
                    break;
            }
        }

        private static Actor ResolveByTypeName(string[] hints)
        {
            var script = FindScriptByTypeNameHints(hints);
            if (script is Script s) return s.Actor;
            if (script is Actor a) return a;
            return null;
        }

        private static System.Object FindScriptByTypeNameHints(string[] hints)
        {
            try
            {
                foreach (var actor in Level.GetActors<Actor>())
                {
                    if (actor == null) continue;

                    foreach (var script in NarrativeSavableActorResolver.GetScripts(actor))
                    {
                        var typeName = script.GetType().Name;
                        foreach (var hint in hints)
                        {
                            if (string.Equals(typeName, hint, StringComparison.Ordinal)) return script;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"[SaveSystem] 按类型名定位玩家对象失败: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// 反射桥接 Game 模块的本地玩家 Actor（HundunWorldGame.Instance.LocalPlayerActor）。
        /// 与 NarrativeProPlugin 中既有的反射桥接方式一致，不产生编译期依赖。
        /// </summary>
        private static Actor ResolveLocalPlayerActorFromGameModule()
        {
            try
            {
                var gameType = Type.GetType("HundunWorld.Game.HundunWorldGame, Game.CSharp");
                if (gameType == null) return null;

                var instanceProp = gameType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
                var gameInstance = instanceProp?.GetValue(null);
                if (gameInstance == null) return null;

                foreach (var name in new[] { "LocalPlayerActor", "CurrentPlayerActor", "PlayerActor" })
                {
                    var prop = gameType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                    if (prop == null || !typeof(Actor).IsAssignableFrom(prop.PropertyType)) continue;
                    if (prop.GetValue(gameInstance) is Actor actor && actor != null) return actor;
                }
            }
            catch
            {
                // Game 模块未加载或属性缺失：保持 null，由其它解析路径负责
            }
            return null;
        }
    }
}
