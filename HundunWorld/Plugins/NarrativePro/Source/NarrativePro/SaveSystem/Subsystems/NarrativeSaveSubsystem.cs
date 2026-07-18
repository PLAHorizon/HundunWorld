// 模块关系说明：
// 本文件属于新模块 NarrativePro.SaveSystem（UE5 NarrativeSaveSystem 移植）。
// 与现有 NarrativePro.Save 模块的关系：
//   - Save.NarrativeSaveManager 仅做业务级 JSON 文件存储，不涉及 Actor/组件状态；
//   - 本子系统做“世界级 Actor 快照”：遍历实现 INarrativeSavableActor 的 Actor，
//     生成 NarrativeActorRecord（含变换 + 字节归档），并写入磁盘 JSON。
//   - 两者互不替代；如需合并，可将 Save.NarrativeSaveData 嵌入本子系统的 PlayerData 或自定义记录中。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.SaveSystem.Subsystems
{
    /// <summary>
    /// 存档阶段变化事件。对应 UE5 FOnSavePhaseChanged。
    /// </summary>
    public delegate void OnSavePhaseChanged();

    /// <summary>
    /// 可选接口：供 Actor / 组件提供自定义字节状态。
    /// 由于 Flax 无 UE5 的 UPROPERTY(SaveGame) 反射归档机制，
    /// 实现此接口的 Actor / 组件可将自己的状态序列化为 byte[]，
    /// 存档子系统会将其写入 NarrativeActorRecord.ByteData。
    /// </summary>
    public interface INarrativeSaveStateProvider
    {
        /// <summary>获取当前状态字节（通常为 JSON/MemoryPack 等序列化结果）。</summary>
        byte[] GetSaveStateBytes();

        /// <summary>从字节恢复状态。</summary>
        void LoadSaveStateBytes(byte[] data);
    }

    /// <summary>
    /// 存档子系统。对应 UE5 UNarrativeSaveSubsystem（继承 UWorldSubsystem）。
    /// 负责保存场景中所有实现 INarrativeSavableActor 接口的 Actor 及其 INarrativeSavableComponent 组件。
    /// 默认会记录 Actor 的 Transform，以及通过 INarrativeSaveStateProvider 提供的字节状态。
    /// Flax 无 WorldSubsystem 等价物，改为 Script 单例（Instance）。
    /// </summary>
    public class NarrativeSaveSubsystem : Script
    {
        // ===== 单例 =====

        private static NarrativeSaveSubsystem _instance;

        /// <summary>当前场景的存档子系统实例（可能为空）。</summary>
        public static NarrativeSaveSubsystem Instance => _instance;

        // ===== 事件 =====

        /// <summary>开始加载时触发。对应 UE5 OnBeginLoad。</summary>
        public event OnSavePhaseChanged OnBeginLoad;

        /// <summary>加载完成时触发。对应 UE5 OnFinishedLoad。</summary>
        public event OnSavePhaseChanged OnFinishedLoad;

        /// <summary>开始保存时触发。对应 UE5 OnBeginSave。</summary>
        public event OnSavePhaseChanged OnBeginSave;

        /// <summary>保存完成时触发。对应 UE5 OnFinishedSave。</summary>
        public event OnSavePhaseChanged OnFinishedSave;

        // ===== 内部状态 =====

        private NarrativeSave _narrativeSaveGame;
        private string _optionString = string.Empty;
        private bool _bIsCurrentlyLoading;
        private int _currentSaveSlot = -1;
        private string _currentSaveName = string.Empty;
        private bool _bSavingDisabled;

        /// <summary>GUID → 运行时 Actor 引用的快速查找表。对应 UE5 QuickLookupMap。</summary>
        private readonly Dictionary<Guid, WeakReference<Actor>> _quickLookupMap = new Dictionary<Guid, WeakReference<Actor>>();

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false
        };

        /// <summary>构造。</summary>
        public NarrativeSaveSubsystem()
        {
        }

        // ===== 生命周期 =====

        public override void OnEnable()
        {
            base.OnEnable();
            _instance = this;
            // 缓存当前场景中所有稳定 Actor
            RebuildLookupMap();
        }

        public override void OnDisable()
        {
            _quickLookupMap.Clear();
            if (_instance == this) _instance = null;
            base.OnDisable();
        }

        /// <summary>重建 GUID → Actor 查找表。</summary>
        private void RebuildLookupMap()
        {
            _quickLookupMap.Clear();
            foreach (var actor in Level.GetActors<Actor>())
            {
                RegisterActorInLookup(actor);
            }
        }

        /// <summary>注册一个 Actor 到查找表（若其实现 INarrativeStableActor）。</summary>
        private void RegisterActorInLookup(Actor actor)
        {
            if (actor is INarrativeStableActor stable)
            {
                Guid guid = stable.GetActorGUID();
                if (guid != Guid.Empty)
                {
                    _quickLookupMap[guid] = new WeakReference<Actor>(actor);
                }
            }
        }

        /// <summary>从查找表移除一个 Actor。</summary>
        private void UnregisterActorFromLookup(Actor actor)
        {
            if (actor is INarrativeStableActor stable)
            {
                Guid guid = stable.GetActorGUID();
                if (guid != Guid.Empty)
                {
                    _quickLookupMap.Remove(guid);
                }
            }
        }

        // ===== 存档对象更新 =====

        /// <summary>
        /// 创建/更新存档对象，将世界状态写入其中（不写入磁盘）。
        /// 对应 UE5 UpdateSaveObject。
        /// </summary>
        /// <param name="bSkipRecordCreation">是否跳过记录生成（用于新游戏初始化）。</param>
        public virtual bool UpdateSaveObject(bool bSkipRecordCreation = false)
        {
            if (_narrativeSaveGame == null)
            {
                _narrativeSaveGame = CreateSaveObject();
            }

            if (bSkipRecordCreation)
            {
                return true;
            }

            if (_narrativeSaveGame == null) return false;

            // 主菜单根据此字段记忆上一次加载的关卡
            Scene scene = Level.GetScene(0);
            _narrativeSaveGame.LevelName = scene != null ? scene.Name : string.Empty;

            // 遍历场景中所有 Actor，为实现了 INarrativeStableActor 的 Actor 生成记录
            foreach (var actor in Level.GetActors<Actor>())
            {
                if (actor == null) continue;
                if (actor is INarrativeStableActor stable)
                {
                    Guid actorGuid = stable.GetActorGUID();
                    if (actorGuid == Guid.Empty) continue;

                    var record = new NarrativeActorRecord();
                    if (CreateActorRecord(actor, record))
                    {
                        _narrativeSaveGame.RecordMap[actorGuid] = record;
                    }
                }
            }

            return true;
        }

        // ===== 存读档 =====

        /// <summary>
        /// 将记录写入存档文件并提交到磁盘。对应 UE5 Save。
        /// </summary>
        /// <param name="saveName">存档名（默认 "NarrativeSave"）。</param>
        /// <param name="slot">存档槽（默认 0）。</param>
        public virtual bool Save(string saveName = "NarrativeSave", int slot = 0)
        {
            if (_bSavingDisabled) return false;

            OnBeginSave?.Invoke();

            UpdateSaveObject();

            bool bSaved = false;
            if (_narrativeSaveGame != null)
            {
                bSaved = SaveGameToSlot(_narrativeSaveGame, saveName, slot);
                if (bSaved)
                {
                    _currentSaveName = saveName;
                    _currentSaveSlot = slot;
                }
                else
                {
                    _currentSaveName = string.Empty;
                    _currentSaveSlot = -1;
                }
            }

            OnFinishedSave?.Invoke();
            return bSaved;
        }

        /// <summary>
        /// 加载存档文件。对应 UE5 Load。
        /// 会更新场景中已存在的 INarrativeSavableActor 状态，并尝试重新生成缺失的动态 Actor。
        /// </summary>
        public virtual bool Load(string saveName = "NarrativeSave", int slot = 0)
        {
            if (!DoesSaveGameExist(saveName, slot))
            {
                return false;
            }

            OnBeginLoad?.Invoke();
            _bIsCurrentlyLoading = true;

            _narrativeSaveGame = LoadGameFromSlot(saveName, slot);

            if (_narrativeSaveGame != null)
            {
                _currentSaveName = saveName;
                _currentSaveSlot = slot;

                var foundRecords = new HashSet<Guid>();

                // 遍历场景中已有 Actor，按 GUID 还原状态
                foreach (var actor in Level.GetActors<Actor>())
                {
                    if (actor == null) continue;
                    if (actor is INarrativeStableActor stable)
                    {
                        Guid actorGuid = stable.GetActorGUID();
                        if (actorGuid == Guid.Empty) continue;

                        if (_narrativeSaveGame.RecordMap.TryGetValue(actorGuid, out var record))
                        {
                            LoadActorFromRecord(actor, record);
                            foundRecords.Add(actorGuid);
                        }
                    }
                }

                // 收集需要动态生成的 Actor
                var dynamicActors = new List<NarrativeActorRecord>();
                foreach (var kvp in _narrativeSaveGame.RecordMap)
                {
                    var record = kvp.Value;
                    if (!foundRecords.Contains(record.ActorGUID))
                    {
                        if (!record.bNetStartup && record.bNeedsDynamicSpawn)
                        {
                            dynamicActors.Add(record);
                        }
                    }
                }

                foreach (var record in dynamicActors)
                {
                    LoadDynamicRecord(record);
                }
            }
            else
            {
                _currentSaveName = string.Empty;
                _currentSaveSlot = -1;
            }

            _bIsCurrentlyLoading = false;
            OnFinishedLoad?.Invoke();
            return true;
        }

        /// <summary>删除磁盘上的存档。对应 UE5 DeleteSave。</summary>
        public virtual bool DeleteSave(string saveName = "NarrativeSave", int slot = 0)
        {
            string filePath = GetSaveFilePath(saveName, slot);
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    if (saveName == _currentSaveName)
                    {
                        _currentSaveName = string.Empty;
                        _currentSaveSlot = -1;
                        _narrativeSaveGame = null;
                    }
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"[SaveSystem] 删除存档失败 {filePath}: {ex.Message}");
                return false;
            }
        }

        /// <summary>加载玩家数据。对应 UE5 LoadPlayerData。</summary>
        public virtual void LoadPlayerData()
        {
            if (_narrativeSaveGame == null) return;
            if (!_narrativeSaveGame.PlayerData.IsValid()) return;

            _bIsCurrentlyLoading = true;
            // Flax 中无 PlayerController/PlayerState/Pawn 等价物，
            // 实际项目可在此通过自定义 PlayerData 还原玩家状态。
            // 保留接口以兼容 UE5 调用流程。
            // TODO [需接入玩家数据还原系统]: 由具体项目根据 PlayerData 中的
            // ControllerData/PawnData/PlayerStateData 还原玩家控制器、Pawn 与状态。
            _bIsCurrentlyLoading = false;
        }

        /// <summary>延迟加载玩家数据。对应 UE5 DeferredLoadPlayerData。</summary>
        public void DeferredLoadPlayerData()
        {
            LoadPlayerData();
        }

        // ===== 单 Actor 操作 =====

        /// <summary>加载单个 Actor 的状态。对应 UE5 LoadSingleActor。</summary>
        public virtual bool LoadSingleActor(Actor actor)
        {
            if (actor == null || _narrativeSaveGame == null) return false;
            if (actor is INarrativeSavableActor savable)
            {
                Guid actorGuid = savable.GetActorGUID();
                if (actorGuid != Guid.Empty && _narrativeSaveGame.RecordMap.TryGetValue(actorGuid, out var record))
                {
                    LoadActorFromRecord(actor, record);
                    return true;
                }
            }
            return false;
        }

        /// <summary>保存单个 Actor 的状态。对应 UE5 SaveSingleActor。</summary>
        public virtual bool SaveSingleActor(Actor actor)
        {
            if (actor == null || _narrativeSaveGame == null) return false;
            if (actor is INarrativeSavableActor savable)
            {
                Guid actorGuid = savable.GetActorGUID();
                if (actorGuid != Guid.Empty)
                {
                    var newRecord = new NarrativeActorRecord();
                    if (CreateActorRecord(actor, newRecord))
                    {
                        _narrativeSaveGame.RecordMap[newRecord.ActorGUID] = newRecord;
                    }
                    return true;
                }
            }
            return false;
        }

        /// <summary>从存档移除单个 Actor 的记录。对应 UE5 RemoveSingleActor。</summary>
        public virtual bool RemoveSingleActor(Actor actor)
        {
            if (actor == null || _narrativeSaveGame == null) return false;
            if (actor is INarrativeSavableActor savable)
            {
                Guid actorGuid = savable.GetActorGUID();

                // net startup Actor 若无记录，先创建一份以便标记销毁
                bool bNetStartup = IsNetStartupActor(actor);
                if (bNetStartup && !_narrativeSaveGame.RecordMap.ContainsKey(actorGuid))
                {
                    SaveSingleActor(actor);
                }

                if (actorGuid != Guid.Empty && _narrativeSaveGame.RecordMap.TryGetValue(actorGuid, out var record))
                {
                    if (record.bNetStartup != bNetStartup)
                    {
                        NarrativeLog.LogWarning($"[SaveSystem] 记录与 Actor 的 net startup 状态不一致: {actor.Name}");
                    }

                    if (record.bNetStartup)
                    {
                        if (!record.bDestroyed)
                        {
                            record.bDestroyed = true;
                            NarrativeLog.Log($"[SaveSystem] 标记 net startup Actor {actor.Name} 为已销毁。");
                        }
                    }
                    else
                    {
                        _narrativeSaveGame.RecordMap.Remove(actorGuid);
                    }
                    return true;
                }
            }
            return false;
        }

        /// <summary>检查指定 GUID 的记录是否存在。对应 UE5 DoesRecordExist。</summary>
        public bool DoesRecordExist(Guid recordGUID)
        {
            return _narrativeSaveGame != null && _narrativeSaveGame.RecordMap.ContainsKey(recordGUID);
        }

        /// <summary>按 GUID 加载动态记录。对应 UE5 LoadDynamicRecord(GUID)。</summary>
        public virtual bool LoadDynamicRecord(Guid recordGUID)
        {
            if (_narrativeSaveGame != null && _narrativeSaveGame.RecordMap.TryGetValue(recordGUID, out var record))
            {
                return LoadDynamicRecord(record);
            }
            return false;
        }

        /// <summary>从动态记录生成 Actor。对应 UE5 LoadDynamicRecord(record)。</summary>
        public virtual bool LoadDynamicRecord(NarrativeActorRecord record)
        {
            if (record == null || !record.IsValid() || !record.bNeedsDynamicSpawn) return false;

            if (string.IsNullOrEmpty(record.ActorSoftClassPath))
            {
                NarrativeLog.LogWarning($"[SaveSystem] LoadDynamicRecord 失败：ActorSoftClassPath 为空，GUID={record.ActorGUID}");
                return false;
            }

            // 通过 Flax 的 Content.LoadAsync 加载预制体，再由 PrefabManager 在记录的 Transform 处生成。
            Prefab prefab = Content.LoadAsync<Prefab>(record.ActorSoftClassPath);
            if (prefab == null)
            {
                NarrativeLog.LogError($"[SaveSystem] LoadDynamicRecord 加载预制体失败：路径={record.ActorSoftClassPath}，GUID={record.ActorGUID}");
                return false;
            }

            Actor spawnedActor = PrefabManager.SpawnPrefab(prefab, record.Transform.Translation, record.Transform.Orientation);
            if (spawnedActor == null)
            {
                NarrativeLog.LogError($"[SaveSystem] LoadDynamicRecord 生成 Actor 失败：路径={record.ActorSoftClassPath}，GUID={record.ActorGUID}");
                return false;
            }

            spawnedActor.Transform = record.Transform;
            spawnedActor.Name = record.ActorName;

            // 若生成的 Actor 实现 INarrativeSavableActor，恢复其 GUID 并加载记录状态
            if (spawnedActor is INarrativeSavableActor savable)
            {
                savable.SetActorGUID(record.ActorGUID);
                LoadActorFromRecord(spawnedActor, record);
            }
            else if (spawnedActor is INarrativeSaveStateProvider provider
                && record.ByteData != null && record.ByteData.Length > 0)
            {
                provider.LoadSaveStateBytes(record.ByteData);
            }

            RegisterActorInLookup(spawnedActor);
            NarrativeLog.Log($"[SaveSystem] LoadDynamicRecord 生成 Actor {spawnedActor.Name} 成功，GUID={record.ActorGUID}");
            return true;
        }

        /// <summary>启用/禁用保存。对应 UE5 SetSavingDisabled。</summary>
        public void SetSavingDisabled(bool bShouldDisable)
        {
            _bSavingDisabled = bShouldDisable;
        }

        /// <summary>保存是否被禁用。对应 UE5 IsSavingDisabled。</summary>
        public bool IsSavingDisabled => _bSavingDisabled;

        /// <summary>是否为新游戏（尚未保存过）。对应 UE5 IsNewGame。</summary>
        public bool IsNewGame()
        {
            if (_narrativeSaveGame != null)
            {
                return !_narrativeSaveGame.PlayerData.IsValid();
            }
            return true;
        }

        /// <summary>是否正在加载。对应 UE5 IsLoading。</summary>
        public bool IsLoading()
        {
            return _bIsCurrentlyLoading;
        }

        /// <summary>获取当前存档对象。对应 UE5 GetSaveObject。</summary>
        public NarrativeSave GetSaveObject()
        {
            return _narrativeSaveGame;
        }

        /// <summary>按 GUID 查找场景中的 Actor。对应 UE5 LookupActorByGUID。</summary>
        public Actor LookupActorByGUID(Guid searchGUID)
        {
            if (searchGUID == Guid.Empty) return null;
            if (_quickLookupMap.TryGetValue(searchGUID, out var weakRef))
            {
                if (weakRef.TryGetTarget(out var actor) && actor != null)
                {
                    return actor;
                }
                _quickLookupMap.Remove(searchGUID);
            }
            return null;
        }

        // ===== Actor 记录创建/还原 =====

        /// <summary>从 Actor 创建存档记录。对应 UE5 CreateActorRecord。</summary>
        public bool CreateActorRecord(Actor actor, NarrativeActorRecord actorRecord)
        {
            if (actor == null || actorRecord == null) return false;

            if (actor is INarrativeSavableActor savable)
            {
                actorRecord.ActorGUID = savable.GetActorGUID();
                if (actorRecord.ActorGUID == Guid.Empty)
                {
                    return false;
                }
                actorRecord.bNeedsDynamicSpawn = savable.ShouldRespawn();
            }

            NarrativeLog.Log($"[SaveSystem] 为 Actor {actor.Name} 创建记录，GUID={actorRecord.ActorGUID}");

            actorRecord.ActorName = actor.Name;

            // 仅可移动 Actor 保存 Transform（避免覆盖设计师调整）
            // Flax 中 StaticModel 等 Actor 不可移动，简化为：始终保存 Transform，加载时再判断
            actorRecord.Transform = actor.Transform;

            actorRecord.ActorSoftClassPath = actor.GetType().FullName ?? string.Empty;
            actorRecord.bNetStartup = IsNetStartupActor(actor);

            // 通知 Actor 即将被保存
            if (actor is INarrativeSavableActor savableActor)
            {
                savableActor.PrepareForSave();
            }

            // Flax 无反射归档；若 Actor 实现 INarrativeSaveStateProvider，则用其字节
            actorRecord.ByteData = (actor is INarrativeSaveStateProvider provider)
                ? provider.GetSaveStateBytes()
                : null;

            // 收集可存档组件
            actorRecord.SavedComponents.Clear();
            foreach (var comp in actor.GetChildren<Actor>())
            {
                // Flax 中 Actor 子物体可视为“组件”，尝试匹配 INarrativeSavableComponent
                if (comp is INarrativeSavableComponent savableComp)
                {
                    savableComp.PrepareForSave();
                    var saveComp = new NarrativeSaveComponent
                    {
                        ComponentName = comp.Name
                    };
                    saveComp.ByteData = (comp is INarrativeSaveStateProvider compProvider)
                        ? compProvider.GetSaveStateBytes()
                        : null;
                    actorRecord.SavedComponents.Add(saveComp);
                }
            }

            // 同时收集挂载到该 Actor 上的 Script 派生组件
            foreach (var script in actor.GetScripts<Script>())
            {
                if (script is INarrativeSavableComponent savableComp)
                {
                    savableComp.PrepareForSave();
                    var saveComp = new NarrativeSaveComponent
                    {
                        ComponentName = script.GetType().Name
                    };
                    saveComp.ByteData = (script is INarrativeSaveStateProvider compProvider)
                        ? compProvider.GetSaveStateBytes()
                        : null;
                    actorRecord.SavedComponents.Add(saveComp);
                }
            }

            return true;
        }

        /// <summary>从存档记录还原 Actor 状态。对应 UE5 LoadActorFromRecord。</summary>
        public void LoadActorFromRecord(Actor actor, NarrativeActorRecord actorRecord)
        {
            if (actor == null || actorRecord == null) return;

            // net startup Actor 且记录标记销毁，则销毁当前 Actor
            if (actorRecord.bNetStartup && actorRecord.bDestroyed)
            {
                NarrativeLog.Log($"[SaveSystem] 从记录销毁 net startup Actor {actor.Name}，GUID={actorRecord.ActorGUID}");
                DestroyActor(actor);
                return;
            }

            // 应用 Transform（非 Identity 时）
            if (!IsIdentityTransform(actorRecord.Transform))
            {
                actor.Transform = actorRecord.Transform;
            }

            NarrativeLog.Log($"[SaveSystem] 从记录加载 Actor {actor.Name}，GUID={actorRecord.ActorGUID}");

            // 还原字节状态
            if (actorRecord.ByteData != null && actorRecord.ByteData.Length > 0
                && actor is INarrativeSaveStateProvider provider)
            {
                provider.LoadSaveStateBytes(actorRecord.ByteData);
            }

            // 通知 Actor 已加载
            if (actor is INarrativeSavableActor savable)
            {
                savable.SetActorGUID(actorRecord.ActorGUID);
                savable.Load();
            }

            // 还原可存档组件
            foreach (var savedComp in actorRecord.SavedComponents)
            {
                // 在 Actor 子物体中查找
                bool found = false;
                foreach (var child in actor.GetChildren<Actor>())
                {
                    if (child != null && child.Name == savedComp.ComponentName
                        && child is INarrativeSavableComponent savableComp)
                    {
                        if (savedComp.ByteData != null && savedComp.ByteData.Length > 0
                            && child is INarrativeSaveStateProvider compProvider)
                        {
                            compProvider.LoadSaveStateBytes(savedComp.ByteData);
                        }
                        savableComp.Load();
                        found = true;
                        break;
                    }
                }
                if (found) continue;

                // 在 Actor 挂载的 Script 中查找
                foreach (var script in actor.GetScripts<Script>())
                {
                    if (script is INarrativeSavableComponent savableComp
                        && script.GetType().Name == savedComp.ComponentName)
                    {
                        if (savedComp.ByteData != null && savedComp.ByteData.Length > 0
                            && script is INarrativeSaveStateProvider compProvider)
                        {
                            compProvider.LoadSaveStateBytes(savedComp.ByteData);
                        }
                        savableComp.Load();
                        break;
                    }
                }
            }
        }

        /// <summary>初始化存档系统。对应 UE5 InitializeSaveSystem（由 GameMode 调用）。</summary>
        public void InitializeSaveSystem()
        {
            // 重建查找表
            RebuildLookupMap();

            string slotString = string.Empty;
            bool bIsLevelTransition = false;
            int slot = 0;

            if (!string.IsNullOrEmpty(_optionString))
            {
                slotString = ParseOption(_optionString, "SaveGameName");
                bIsLevelTransition = ParseOption(_optionString, "LevelTransition").Length > 0;
            }

            if (DoesSaveGameExist(slotString, slot))
            {
                // 关卡切换时加载，但角色 Transform 由新关卡决定
                Load(slotString, slot);
            }
            else
            {
                // 新游戏：仅创建空存档对象
                UpdateSaveObject(true);
            }
        }

        /// <summary>设置 GameMode 选项字符串。对应 UE5 中读取 GM->OptionsString。</summary>
        public void SetOptionString(string optionString)
        {
            _optionString = optionString ?? string.Empty;
        }

        // ===== 内部辅助 =====

        /// <summary>创建默认存档对象。对应 UE5 GetSaveGameClass() + CreateSaveGameObject。</summary>
        private NarrativeSave CreateSaveObject()
        {
            // 简化：始终创建 NarrativeSave。若 SaveSystemDeveloperSettings 配置了子类路径，
            // 实际项目可在此通过反射创建子类。
            return new NarrativeSave();
        }

        /// <summary>判断 Actor 是否为 net startup（关卡启动时存在）。
        /// Flax 无 net startup 概念，简化为：所有运行时 Spawn 的 Actor 视为非 net startup。</summary>
        private bool IsNetStartupActor(Actor actor)
        {
            // Flax-不兼容: UE5 的 UNetDriver/IsNetStartupActor API 在 Flax 无对应物，
            // Flax 无法直接判定 Actor 是否为关卡内置（Scene 内）或运行时生成。
            // 此处采用保守策略：默认按 true 处理，确保 net startup Actor 的销毁状态会被正确记录。
            return true;
        }

        /// <summary>判断 Transform 是否为 Identity（近似）。</summary>
        private bool IsIdentityTransform(Transform t)
        {
            const float eps = 1e-5f;
            // Quaternion.Angle 在 Flax 中是属性，使用点积计算角度差
            float dot = (float)Math.Clamp(Quaternion.Dot(t.Orientation, Quaternion.Identity), -1f, 1f);
            float angleRad = (float)Math.Acos(dot);
            return Vector3.DistanceSquared(t.Translation, Vector3.Zero) < eps
                && angleRad < eps
                && Math.Abs(t.Scale.X - 1f) < eps
                && Math.Abs(t.Scale.Y - 1f) < eps
                && Math.Abs(t.Scale.Z - 1f) < eps;
        }

        /// <summary>销毁 Actor。Flax 中通过静态方法 Actor.Destroy(actor) 销毁。</summary>
        private void DestroyActor(Actor actor)
        {
            if (actor != null)
            {
                UnregisterActorFromLookup(actor);
                Actor.Destroy(actor);
            }
        }

        /// <summary>从选项字符串解析键值。对应 UE5 UGameplayStatics::ParseOption。</summary>
        private static string ParseOption(string options, string key)
        {
            if (string.IsNullOrEmpty(options) || string.IsNullOrEmpty(key)) return string.Empty;
            var parts = options.Split('?');
            foreach (var part in parts)
            {
                int eq = part.IndexOf('=');
                if (eq > 0)
                {
                    string k = part.Substring(0, eq);
                    string v = part.Substring(eq + 1);
                    if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                    {
                        return v;
                    }
                }
            }
            return string.Empty;
        }

        // ===== 磁盘 IO（替代 UE5 UGameplayStatics::SaveGameToSlot 等） =====

        /// <summary>存档目录。Flax 中使用系统本地应用数据目录下的 HundunWorld/SaveSystem 子目录。</summary>
        private static string SaveDirectory
        {
            get
            {
                // 使用系统本地应用数据目录（Windows: %LOCALAPPDATA%，跨平台可移植），
                // 避免依赖当前工作目录（打包后工作目录可能不可写或被覆盖）。
                string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(baseDir))
                {
                    baseDir = Environment.CurrentDirectory;
                }
                return Path.Combine(baseDir, "HundunWorld", "SaveSystem");
            }
        }

        /// <summary>获取存档文件完整路径。</summary>
        private static string GetSaveFilePath(string saveName, int slot)
        {
            string dir = SaveDirectory;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return Path.Combine(dir, $"{saveName}_{slot}.json");
        }

        /// <summary>判断存档文件是否存在。对应 UE5 UGameplayStatics::DoesSaveGameExist。</summary>
        private static bool DoesSaveGameExist(string saveName, int slot)
        {
            return File.Exists(GetSaveFilePath(saveName, slot));
        }

        /// <summary>写入存档到磁盘。对应 UE5 UGameplayStatics::SaveGameToSlot。</summary>
        private static bool SaveGameToSlot(NarrativeSave save, string saveName, int slot)
        {
            try
            {
                string filePath = GetSaveFilePath(saveName, slot);
                string json = JsonSerializer.Serialize(save, _jsonOptions);
                File.WriteAllText(filePath, json);
                NarrativeLog.Log($"[SaveSystem] 存档已写入 {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"[SaveSystem] 写入存档失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>从磁盘加载存档。对应 UE5 UGameplayStatics::LoadGameFromSlot。</summary>
        private static NarrativeSave LoadGameFromSlot(string saveName, int slot)
        {
            try
            {
                string filePath = GetSaveFilePath(saveName, slot);
                if (!File.Exists(filePath)) return null;
                string json = File.ReadAllText(filePath);
                var save = JsonSerializer.Deserialize<NarrativeSave>(json, _jsonOptions);
                save?.OnSerialize(true);
                NarrativeLog.Log($"[SaveSystem] 存档已读取 {filePath}");
                return save;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"[SaveSystem] 读取存档失败: {ex.Message}");
                return null;
            }
        }
    }
}
