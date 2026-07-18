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
        /// <summary>组件名称（用于在 Actor 内定位组件）。</summary>
        public string ComponentName = string.Empty;

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
        /// 动态生成的 Actor 需要记住其类型路径，加载时才能重新生成。
        /// 对应 UE5 TSoftClassPtr&lt;AActor&gt;，Flax 中使用类型路径占位字符串。
        /// </summary>
        public string ActorSoftClassPath = string.Empty;

        /// <summary>所有被存档组件的数据。</summary>
        public List<NarrativeSaveComponent> SavedComponents = new List<NarrativeSaveComponent>();

        /// <summary>Actor 所有 SaveGame 标记变量的二进制归档数据。</summary>
        public byte[] ByteData;

        /// <summary>该记录是否有效（ActorName 非空表示有效）。</summary>
        public bool IsValid() => !string.IsNullOrEmpty(ActorName);
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

        /// <summary>新版本应在此之前添加。</summary>
        public const int VersionPlusOne = 1;

        /// <summary>最新版本。</summary>
        public const int LatestVersion = VersionPlusOne - 1;
    }

    /// <summary>
    /// 关卡存档记录。对应 UE5 FNarrativeSavedLevel。
    /// 用于按关卡存储 Actor 记录（当前未启用，预留结构）。
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

        // TODO [需接入按关卡存储系统]: 未来按关卡存储 Actor 记录，支持多关卡独立存档。
        // public List<NarrativeSavedLevel> SavedLevels;

        /// <summary>GUID → Actor 记录映射，用于加载时按 GUID 还原。</summary>
        public Dictionary<Guid, NarrativeActorRecord> RecordMap = new Dictionary<Guid, NarrativeActorRecord>();

        /// <summary>
        /// 玩家专属数据。玩家数据独立于关卡存储，
        /// 因为玩家等级/属性/物品等不应与关卡绑定。
        /// </summary>
        public NarrativeSavePlayer PlayerData = new NarrativeSavePlayer();

        /// <summary>覆盖当前存档的关卡名。仅角色创建器使用，避免把创建器关卡保存为当前关卡。</summary>
        public void OverrideLevelName(string inLevelName)
        {
            LevelName = inLevelName;
        }

        /// <summary>存档时的版本号，用于加载时进行版本修复。</summary>
        public int SavedDataVersion = NarrativeSaveGameVersion.LatestVersion;

        /// <summary>
        /// 版本修复钩子。对应 UE5 Serialize 重载。
        /// 加载时若版本不匹配，可在此处执行数据迁移。
        /// </summary>
        /// <param name="isLoading">是否为加载流程。</param>
        public virtual void OnSerialize(bool isLoading)
        {
            if (isLoading && SavedDataVersion != NarrativeSaveGameVersion.LatestVersion)
            {
                // 未来如需版本修复在此处实现
            }
        }
    }
}
