using NarrativePro.Core;
using NarrativePro.SaveSystem.Subsystems;

namespace NarrativePro.Tales.Events.Builtin
{
    /// <summary>
    /// 添加存档检查点事件。
    /// <para>
    /// UE5 依据（Content/Pro/Core/Tales/Events/NE_AddSaveCheckpoint.uasset 符号表）：
    /// CallFunc 含 <c>GetGameInstance</c>（转 BP_NarrativeGameInstance 读 <c>CurrentSaveName</c>）、
    /// <c>IsEmpty</c>/<c>Contains</c>/<c>SelectString</c>（决定检查点存档名，注释框文本 "Autosaved."）、
    /// <c>GetWorldSubsystem</c>（NarrativeSaveSubsystem）→ <c>GetFirstEmptySaveSlot</c>
    /// （回传 <c>SlotName</c>/<c>HasEmptySlot</c>）→ <c>SaveGame</c>（回传 <c>Succeeded</c>），
    /// 即「本次会话尚未建立正式存档时，把当前进度落到第一个空槽做检查点」；
    /// C++ 侧同名子系统见 NarrativeSaveSystem/Public/SaveSystemStatics.h。
    /// </para>
    /// <para>
    /// 端口差异：SaveSystem/Subsystems/NarrativeSaveSubsystem.cs 只有公开
    /// <c>Save(saveName, slot)</c>（槽位扫描 DoesSaveGameExist 是私有静态，:1766），
    /// 没有 GetFirstEmptySaveSlot 等价物 —— 端口把检查点写到显式
    /// <see cref="Slot"/>/<see cref="SaveName"/>；两者留默认值时由
    /// <c>ApplyDefaultSlot</c> 套开发者设置的默认槽，语义退化为「覆盖式检查点」
    /// 而非 UE5 的「找空槽新建」，需要空槽轮转时应先给子系统补公开槽扫描。
    /// </para>
    /// </summary>
    public class AddSaveCheckpointEvent : NarrativeEvent
    {
        /// <summary>存档槽名。留空走端口默认槽（对应 UE5 GetFirstEmptySaveSlot 的 SlotName）。</summary>
        public string SaveName { get; set; } = "";

        /// <summary>槽号。对应 UE5 SaveGame 的 Slot。端口不做空槽扫描，见类注释端口差异。</summary>
        public int Slot { get; set; } = 0;

        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            var subsystem = NarrativeSaveSubsystem.Instance;
            if (subsystem == null)
            {
                NarrativeLog.LogWarning("AddSaveCheckpointEvent: NarrativeSaveSubsystem 不存在，检查点未写入");
                return;
            }

            // 对应 UE5 图里 SaveGame 前判 IsLoading：加载还原过程中再存一遍会把半成品写盘
            if (subsystem.IsLoading())
            {
                NarrativeLog.LogWarning("AddSaveCheckpointEvent: 正在加载存档，检查点跳过");
                return;
            }

            var name = string.IsNullOrEmpty(SaveName) ? "NarrativeSave" : SaveName;
            var succeeded = subsystem.Save(name, Slot);
            if (!succeeded)
                NarrativeLog.LogWarning($"AddSaveCheckpointEvent: 检查点写入失败（槽 '{name}' / {Slot}）");
            else
                NarrativeLog.Log($"AddSaveCheckpointEvent: 检查点已写入（槽 '{name}' / {Slot}）");
        }

        public override string GetGraphDisplayText() => $"Add Save Checkpoint: {SaveName}";
    }
}
