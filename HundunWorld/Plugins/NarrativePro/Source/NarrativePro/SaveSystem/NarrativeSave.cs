// 模块关系说明：
// 本文件属于新模块 NarrativePro.SaveSystem（UE5 NarrativeSaveSystem 移植）。
// 与现有 NarrativePro.Save 模块的关系：
//   - Save.NarrativeSaveData 是业务级存档（任务/对话/进度等），结构化字段；
//   - 本文件 SaveSystem.NarrativeSave 是世界级快照存档（Actor 记录 + 玩家数据 + 字节归档）。
// 二者结构不同、用途不同，互不替代。若需合并可在调用层将 SaveData 嵌入 Save 的 ByteData 中。

using System;
using System.Collections.Generic;

namespace NarrativePro.SaveSystem
{
    /// <summary>
    /// 存档组件记录。对应 UE5 FNarrativeSaveComponent。
    /// 存储某个被存档组件的名称与其全部 SaveGame 标记变量的二进制数据。
    /// </summary>
    [Serializable]
    public class NarrativeSaveComponent
    {
        /// <summary>
        /// 组件归档键（用于在 Actor 内定位组件）。
        /// 老语义：类型全名 + 可选 "#索引"；声明了 [NarrativeSaveKey]/SaveComponentKey 的组件写显式键。
        /// </summary>
        public string ComponentName = string.Empty;

        /// <summary>
        /// 组件的稳定身份（随存档持久化）。确定性方案：id 即归档键（显式键 / 类型全名#同类内序号），
        /// 写读两侧对同一对象按同规则重算出同值，无需进程内注册表即可跨重启成立；
        /// 会话内脚本中途重排时读档优先用注册表按 id 找回原对象。
        /// 序号挪位带来的错配由 <see cref="ComponentFingerprint"/> 闸门拒绝应用兜底。
        /// 老存档该字段为空（或旧 GUID 形态，新会话解析不到）→ 自动退回 <see cref="ComponentName"/> 旧定位。
        /// </summary>
        public string ComponentId = string.Empty;

        /// <summary>
        /// 组件状态载荷指纹（声明类型+成员数+有序成员名散列，见 NarrativeSaveStateArchive.ComputeFingerprintForObject）。
        /// 读档时与定位到的组件比对：不一致则拒绝应用字节并告警，组件保留默认值；
        /// 老档为空 → 不设闸门照常还原，下次保存自动补写。
        /// </summary>
        public string ComponentFingerprint = string.Empty;

        /// <summary>组件所有 SaveGame 标记变量的二进制归档数据。</summary>
        public byte[] ByteData;
    }

    /// <summary>
    /// Actor 存档记录。对应 UE5 FNarrativeActorRecord。
    /// 描述一个被存档 Actor 的关键状态：GUID、变换、销毁标志、组件数据等。
    /// </summary>
    [Serializable]
    public class NarrativeActorRecord
    {
        /// <summary>用于识别该 Actor 的稳定 GUID。</summary>
        public Guid ActorGUID = Guid.Empty;

        /// <summary>Actor 名称（用于调试与定位，非必需）。</summary>
        public string ActorName = string.Empty;

        /// <summary>Actor 的存档变换（位置/旋转/缩放）。</summary>
        public FlaxEngine.Transform Transform = FlaxEngine.Transform.Identity;

        /// <summary>
        /// 该 Actor 是否已被销毁。仅对放置在关卡中的 Actor 有意义；
        /// 动态 Actor 销毁时直接移除其记录。
        /// </summary>
        public bool bDestroyed = false;

        /// <summary>是否为 net startup Actor（关卡启动时存在）或动态生成的 Actor。</summary>
        public bool bNetStartup = true;

        /// <summary>是否希望存档系统自动重新生成此动态 Actor。</summary>
        public bool bNeedsDynamicSpawn = false;

        /// <summary>
        /// 动态生成 Actor 的 Prefab 资产路径（如 "Content/Items/Pickup_Health.flax"），加载时据此重生成。
        /// 对应 UE5 TSoftClassPtr&lt;AActor&gt;（NarrativeSaveSubsystem.cpp:705 ActorSoftClass = Actor->GetClass()）。
        /// 注意：Flax 中 Actor 是密封类，不能像 UE5 那样用类型名生成对象，
        /// 这里必须是可被 Content.LoadAsync&lt;Prefab&gt; 解析的资产路径，写类型全名会被
        /// NarrativeDynamicSpawnSourceResolver / LoadDynamicRecord 判定为非法并拒绝。
        /// </summary>
        public string ActorSoftClassPath = string.Empty;

        /// <summary>所有被存档组件的数据。</summary>
        public List<NarrativeSaveComponent> SavedComponents = new List<NarrativeSaveComponent>();

        /// <summary>Actor 所有 SaveGame 标记变量的二进制归档数据。</summary>
        public byte[] ByteData;

        /// <summary>该记录是否有效（ActorName 非空表示有效）。</summary>
        public bool IsValid() => !string.IsNullOrEmpty(ActorName);

        /// <summary>
        /// 深拷贝一份记录。玩家三元组可能共用同一个 Actor（Flax 无独立 PlayerState/Controller Actor），
        /// 需要各自独立的记录，避免修改 PlayerStateData.Transform 时把 PawnData 的变换一起清零。
        /// </summary>
        public NarrativeActorRecord Clone()
        {
            var copy = new NarrativeActorRecord
            {
                ActorGUID = ActorGUID,
                ActorName = ActorName,
                Transform = this.Transform,
                bDestroyed = bDestroyed,
                bNetStartup = bNetStartup,
                bNeedsDynamicSpawn = bNeedsDynamicSpawn,
                ActorSoftClassPath = ActorSoftClassPath,
                ByteData = ByteData != null ? (byte[])ByteData.Clone() : null,
            };

            if (SavedComponents != null)
            {
                foreach (var component in SavedComponents)
                {
                    if (component == null) continue;
                    copy.SavedComponents.Add(new NarrativeSaveComponent
                    {
                        ComponentName = component.ComponentName,
                        ComponentId = component.ComponentId,
                        ComponentFingerprint = component.ComponentFingerprint,
                        ByteData = component.ByteData != null ? (byte[])component.ByteData.Clone() : null,
                    });
                }
            }

            return copy;
        }
    }

    /// <summary>
    /// 玩家存档数据。对应 UE5 FNarrativeSavePlayer。
    /// 玩家由多个 Actor 组成（PlayerState / Controller / Pawn），打包到一份记录中。
    /// </summary>
    [Serializable]
    public class NarrativeSavePlayer
    {
        /// <summary>玩家控制器记录。</summary>
        public NarrativeActorRecord ControllerData = new NarrativeActorRecord();

        /// <summary>玩家 Pawn 记录。</summary>
        public NarrativeActorRecord PawnData = new NarrativeActorRecord();

        /// <summary>玩家状态记录。</summary>
        public NarrativeActorRecord PlayerStateData = new NarrativeActorRecord();

        /// <summary>该玩家数据是否有效（以 PawnData 为准）。</summary>
        public bool IsValid() => !string.IsNullOrEmpty(PawnData?.ActorName);
    }

    /// <summary>
    /// 存档版本号。对应 UE5 ENarrativeSaveGameVersion。
    /// 使用 Epic 的“版本加一”模式：新版本必须加在 VersionPlusOne 之前。
    /// </summary>
    public static class NarrativeSaveGameVersion
    {
        /// <summary>初始版本。</summary>
        public const int Initial = 0;

        /// <summary>v1：新增按关卡镜像 SavedLevels，并规范化记录默认值（迁移在 OnSerialize 实现）。</summary>
        public const int AddedSavedLevels = 1;

        /// <summary>新版本应在此之前添加。</summary>
        public const int VersionPlusOne = 2;

        /// <summary>最新版本。</summary>
        public const int LatestVersion = VersionPlusOne - 1;
    }

    /// <summary>
    /// 关卡存档记录。对应 UE5 FNarrativeSavedLevel。
    /// 按关卡镜像的 Actor 记录分片：由 <see cref="NarrativeSave.SyncSavedLevel"/> 在每次
    /// 世界状态捕获时维护，<see cref="NarrativeSave.MergeSavedLevel"/> 在加载时并回当前关卡分片。
    /// </summary>
    [Serializable]
    public class NarrativeSavedLevel
    {
        /// <summary>该记录所属关卡名称。</summary>
        public string LevelName = string.Empty;

        /// <summary>GUID → Actor 记录映射，用于按 GUID 还原。</summary>
        public Dictionary<Guid, NarrativeActorRecord> RecordMap = new Dictionary<Guid, NarrativeActorRecord>();
    }

    /// <summary>
    /// 存档对象。对应 UE5 UNarrativeSave（继承 USaveGame）。
    /// 持有所有实现 INarrativeSavableActor 接口的 Actor 的存档记录，
    /// 以及独立的玩家数据。Flax 中以 [Serializable] plain class 表达。
    /// </summary>
    [Serializable]
    public class NarrativeSave
    {
        /// <summary>构造时记录当前最新版本号。</summary>
        public NarrativeSave()
        {
            SavedDataVersion = NarrativeSaveGameVersion.LatestVersion;
        }

        /// <summary>
        /// 当前已加载的关卡名。主菜单根据此字段决定加载哪个关卡，
        /// 存档子系统自身不直接使用此字段。
        /// </summary>
        public string LevelName = string.Empty;

        // 关卡分片设计决策（对应 UE5 FNarrativeSavedLevel 的按关卡存储）：
        // UE5 需要按关卡分片，是因为同一份 SaveGame 要随流送关卡增删载载还原 Actor 记录。
        // Flax 端口运行单一持续世界（主场景 + additive 分块场景共享同一世界，记录 GUID 全局唯一），
        // 因此以 RecordMap 合并视图作为加载真相；SavedLevels 维护按关卡镜像的当前分片，
        // 由 NarrativeSaveSubsystem 在每次 UpdateSaveObject/Load 时同步与合并，用于跨关卡恢复与诊断。
        /// <summary>按关卡镜像的记录分片（NarrativeSavedLevel 的实际使用位，见 SyncSavedLevel/MergeSavedLevel）。</summary>
        public List<NarrativeSavedLevel> SavedLevels = new List<NarrativeSavedLevel>();

        /// <summary>GUID → Actor 记录映射，用于加载时按 GUID 还原。</summary>
        public Dictionary<Guid, NarrativeActorRecord> RecordMap = new Dictionary<Guid, NarrativeActorRecord>();

        /// <summary>
        /// 玩家专属数据。玩家数据独立于关卡存储，
        /// 因为玩家等级/属性/物品等不应与关卡绑定。
        /// </summary>
        public NarrativeSavePlayer PlayerData = new NarrativeSavePlayer();

        /// <summary>
        /// 角色创建器外观数据（对应 UE5 里 UNarrativeSaveWithCreatorData 的用法：创建器产出的属性集随存档持久化）。
        /// 由 NarrativeSaveSubsystem 在每次写盘前从本地玩家的角色视觉快照得到，
        /// 读档时由 NarrativePlayerCharacter 在视觉初始化前应用，从而外观不再每次会话重掷。
        /// 纯字符串/数值结构（不含引擎资产引用），因此不受组件归档的按序定位问题影响。
        /// </summary>
        public NarrativePro.CharacterCreator.NarrativeCreatorSnapshot CreatorData;

        /// <summary>覆盖当前存档的关卡名。仅角色创建器使用，避免把创建器关卡保存为当前关卡。</summary>
        public void OverrideLevelName(string inLevelName)
        {
            LevelName = inLevelName;
        }

        /// <summary>存档时的版本号，用于加载时进行版本修复。</summary>
        public int SavedDataVersion = NarrativeSaveGameVersion.LatestVersion;

        /// <summary>
        /// 版本修复钩子。对应 UE5 Serialize 重载（NarrativeSaveSystem：加载旧版本时逐级迁移）。
        /// 加载路径上按 SavedDataVersion 逐级迁移到 LatestVersion，并回写版本号；
        /// 保存路径上把版本号刷新为最新。由 NarrativeSaveSubsystem.LoadGameFromSlot 调用。
        /// </summary>
        /// <param name="isLoading">是否为加载流程。</param>
        public virtual void OnSerialize(bool isLoading)
        {
            if (!isLoading)
            {
                SavedDataVersion = NarrativeSaveGameVersion.LatestVersion;
                return;
            }

            if (SavedLevels == null)
            {
                SavedLevels = new List<NarrativeSavedLevel>();
            }

            // v0 → v1：补齐关卡镜像分片，并规范化每条记录的集合默认值。
            if (SavedDataVersion < NarrativeSaveGameVersion.AddedSavedLevels)
            {
                MigrateToAddedSavedLevels();
            }

            SavedDataVersion = NarrativeSaveGameVersion.LatestVersion;
        }

        /// <summary>v0 → v1 迁移：用当前 RecordMap 建立当前关卡分片，并为所有记录补齐默认字段。</summary>
        private void MigrateToAddedSavedLevels()
        {
            if (RecordMap == null)
            {
                RecordMap = new Dictionary<Guid, NarrativeActorRecord>();
                return;
            }

            foreach (var kvp in RecordMap)
            {
                var record = kvp.Value;
                if (record == null) continue;
                if (record.SavedComponents == null)
                {
                    record.SavedComponents = new List<NarrativeSaveComponent>();
                }
                if (record.ActorGUID == Guid.Empty)
                {
                    // 旧档个别记录缺 GUID：以字典键回填，避免加载循环整条跳过
                    record.ActorGUID = kvp.Key;
                }
            }

            SyncSavedLevel(LevelName);
        }

        /// <summary>
        /// 把当前 RecordMap 镜像进指定关卡的分片（找不到则新建）。
        /// 由 NarrativeSaveSubsystem.UpdateSaveObject 在每次世界捕获后调用。
        /// </summary>
        public void SyncSavedLevel(string levelName)
        {
            if (string.IsNullOrEmpty(levelName)) return;
            if (SavedLevels == null) SavedLevels = new List<NarrativeSavedLevel>();

            NarrativeSavedLevel level = null;
            foreach (var saved in SavedLevels)
            {
                if (saved != null && string.Equals(saved.LevelName, levelName, StringComparison.OrdinalIgnoreCase))
                {
                    level = saved;
                    break;
                }
            }

            if (level == null)
            {
                level = new NarrativeSavedLevel { LevelName = levelName };
                SavedLevels.Add(level);
            }

            level.RecordMap = RecordMap != null
                ? new Dictionary<Guid, NarrativeActorRecord>(RecordMap)
                : new Dictionary<Guid, NarrativeActorRecord>();
        }

        /// <summary>
        /// 加载时把指定关卡的分片并回合并 RecordMap（仅补缺失 GUID，不覆盖已有记录）。
        /// 由 NarrativeSaveSubsystem.Load 在读档后调用。返回补回的记录数。
        /// </summary>
        public int MergeSavedLevel(string levelName)
        {
            if (string.IsNullOrEmpty(levelName) || SavedLevels == null || RecordMap == null) return 0;

            NarrativeSavedLevel level = null;
            foreach (var saved in SavedLevels)
            {
                if (saved != null && string.Equals(saved.LevelName, levelName, StringComparison.OrdinalIgnoreCase))
                {
                    level = saved;
                    break;
                }
            }
            if (level?.RecordMap == null) return 0;

            int merged = 0;
            foreach (var kvp in level.RecordMap)
            {
                if (kvp.Value == null || RecordMap.ContainsKey(kvp.Key)) continue;
                RecordMap[kvp.Key] = kvp.Value;
                merged++;
            }
            return merged;
        }
    }
}
