using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;
using NarrativePro.Navigation;
using NarrativePro.Tales.Tasks;

namespace NarrativePro.Tales
{
    /// <summary>
    /// 任务导航标记设置。对应 UE5 <c>FTaskNavigationMarker</c>（QuestTask.h:12-77）。
    /// 定义任务期间在世界中添加的导航标记/面包屑。
    /// </summary>
    [Serializable]
    public class TaskNavigationMarker
    {
        /// <summary>是否为该任务在世界中添加导航标记。</summary>
        public bool bAddNavigationMarker = false;

        /// <summary>导航标记是否绘制面包屑。</summary>
        public bool bDrawBreadcrumbs = true;

        /// <summary>
        /// 地图标记类。UE5 中为 TSubclassOf&lt;UMapMarker&gt;，
        /// Flax 的 <see cref="MapMarker"/> 是普通对象而非 Actor 预制，故用类型名占位，
        /// 生成时统一使用 <see cref="MapMarker"/>。
        /// </summary>
        public string MarkerClassPath = "";

        /// <summary>
        /// 导航标记图标路径。UE5 中为 UTexture2D*，Flax 中用 string 路径占位。
        /// </summary>
        public string NavigationMarkerIconPath = "";

        /// <summary>标记颜色。UE5 默认 FLinearColor::Yellow。</summary>
        public Color MarkerColor = Color.Yellow;

        /// <summary>
        /// 标记所属导航器域。UE5 构造函数默认加入 Compass/Minimap/Worldmap/Screenspace 四个域。
        /// </summary>
        public GameplayTagContainer MarkerDomains = CreateDefaultMarkerDomains();

        /// <summary>标记显示文本。为空时使用任务描述。</summary>
        public string MarkerDisplayText = "";

        /// <summary>标记副标题文本。</summary>
        public string MarkerSubtitleText = "";

        /// <summary>
        /// 强制把标记附着到该提供者给出的 Actor 上，会覆盖任务自身提供的 Actor。
        /// 对应 UE5 <c>MarkerAttachActorOverride</c>（TObjectPtr&lt;UNarrativeActorProvider&gt;）。
        /// </summary>
        public NarrativeActorProvider MarkerAttachActorOverride;

        /// <summary>
        /// 当目标 Actor 尚未生成时使用的回退位置。对应 UE5 <c>ActorFallbackLocation</c>。
        /// </summary>
        public Vector3 ActorFallbackLocation = Vector3.Zero;

        /// <summary>
        /// 标记世界位置。若存在 AttachActor 则视为相对位置。UE5 中为 FVector::ZeroVector。
        /// </summary>
        public Vector3 MarkerLocation = Vector3.Zero;

        public TaskNavigationMarker() { }

        /// <summary>对应 UE5 UNarrativeTask 构造函数里给 MarkerDomains 填的四个默认域。</summary>
        private static GameplayTagContainer CreateDefaultMarkerDomains()
        {
            var domains = new GameplayTagContainer();
            domains.AddTag(NavigatorGameplayTags.NavigatorTypes_Compass);
            domains.AddTag(NavigatorGameplayTags.NavigatorTypes_Minimap);
            domains.AddTag(NavigatorGameplayTags.NavigatorTypes_Worldmap);
            domains.AddTag(NavigatorGameplayTags.NavigatorTypes_Screenspace);
            return domains;
        }
    }

    /// <summary>
    /// 任务条目静态辅助。对应 UE5 QuestTask.h 中除 <c>UNarrativeTask</c> 成员以外的部分。
    /// <para>
    /// UE5 里 <c>SpawnDefaultNavigationMarker</c> / <c>SpawnNavigationMarker</c> 都是任务实例方法，
    /// Flax 端口已把它们实现在 <see cref="NarrativeTask"/> 上，此处保留同名静态入口做转发，
    /// 供旧的静态调用点使用。
    /// </para>
    /// </summary>
    public static class QuestTask
    {
        /// <summary>
        /// 为任务生成默认导航标记。对应 UE5 <c>UNarrativeTask::SpawnDefaultNavigationMarker</c>。
        /// </summary>
        public static void SpawnDefaultNavigationMarker(NarrativeTask task)
        {
            task?.SpawnDefaultNavigationMarker();
        }

        /// <summary>
        /// 在指定位置生成导航标记。对应 UE5 <c>UNarrativeTask::SpawnNavigationMarker</c>。
        /// </summary>
        public static void SpawnNavigationMarker(NarrativeTask task, Vector3 markerLocation, Actor attachActor = null)
        {
            task?.SpawnNavigationMarker(markerLocation, attachActor);
        }

        /// <summary>
        /// 获取导航标记位置。对应 UE5 <c>UNarrativeTask::GetNavigationMarkerLocation</c>。
        /// </summary>
        public static Vector3 GetNavigationMarkerLocation(NarrativeTask task)
        {
            return task != null ? task.GetNavigationMarkerLocation() : Vector3.Zero;
        }

        /// <summary>
        /// 获取导航标记应附加的 Actor。对应 UE5 <c>UNarrativeTask::GetNavigationMarkerAttachActor</c>。
        /// </summary>
        public static Actor GetNavigationMarkerAttachActor(NarrativeTask task)
        {
            return task != null ? task.GetNavigationMarkerAttachActor() : null;
        }

        /// <summary>
        /// 任务标记的移除入口。对应 UE5 <c>UNarrativeTask::EndTask</c> 里对 SpawnedMarker 的清理。
        /// </summary>
        public static void ClearNavigationMarker(NarrativeTask task)
        {
            if (task == null || task.SpawnedMarker == null) return;
            task.SpawnedMarker.RemoveMarker();
            task.SpawnedMarker = null;
        }
    }
}
