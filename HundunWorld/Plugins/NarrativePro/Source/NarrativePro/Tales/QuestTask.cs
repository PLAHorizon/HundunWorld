using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Items;
using NarrativePro.Tales.Tasks;

namespace NarrativePro.Tales
{
    /// <summary>
    /// 任务导航标记设置。对应 UE5 FTaskNavigationMarker。
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
        /// 地图标记类路径占位。UE5 中为 TSubclassOf&lt;UMapMarker&gt;，
        /// Flax 中无蓝图类，用 string 路径占位。
        /// </summary>
        public string MarkerClassPath = "";

        /// <summary>
        /// 导航标记图标路径。UE5 中为 UTexture2D*，
        /// Flax 中用 string 路径占位。
        /// </summary>
        public string NavigationMarkerIconPath = "";

        /// <summary>标记颜色。UE5 中为 FLinearColor::Yellow。</summary>
        public Color MarkerColor = Color.Yellow;

        /// <summary>
        /// 标记所属导航器域。UE5 中为 FGameplayTagContainer（meta Categories="Navigator.NavigatorTypes"），
        /// Flax 中使用 NarrativePro.Items.GameplayTagContainer。
        /// </summary>
        public GameplayTagContainer MarkerDomains = new GameplayTagContainer();

        /// <summary>标记显示文本。为空时使用任务描述。</summary>
        public string MarkerDisplayText = "";

        /// <summary>标记副标题文本。</summary>
        public string MarkerSubtitleText = "";

        /// <summary>
        /// 标记世界位置。若有 AttachActor 则视为相对位置。
        /// UE5 中为 FVector::ZeroVector。
        /// </summary>
        public Vector3 MarkerLocation = Vector3.Zero;

        public TaskNavigationMarker() { }
    }

    /// <summary>
    /// 任务条目辅助工具。对应 UE5 QuestTask.h 中除 UNarrativeTask 基类以外的运行时逻辑。
    /// UNarrativeTask 基类已移植到 Tasks/NarrativeTask.cs，
    /// 此处仅保留 UE5 中尚未移植的导航标记相关结构与占位方法。
    /// </summary>
    public static class QuestTask
    {
        // Flax-不兼容: UE5 的 MapMarker / NavigationMarkerComponent 在 Flax 无对应物，保留占位。原文 TODO: Flax 中无 UE5 的 MapMarker / NavigationMarkerComponent 系统，
        // 以下方法为占位，需要项目方自行实现导航标记生成与生命周期管理。

        /// <summary>
        /// 为任务生成默认导航标记。对应 UE5 UNarrativeTask::SpawnDefaultNavigationMarker。
        /// 使用 GetNavigationMarkerLocation 与 GetNavigationMarkerAttachActor 的返回值生成标记。
        /// </summary>
        public static void SpawnDefaultNavigationMarker(NarrativeTask task)
        {
            // Flax-不兼容: UE5 的 MapMarker 在 Flax 无对应物，保留占位。原文 TODO: Flax 无内置导航标记系统，需自行实现。
            // UE5 中调用 SpawnNavigationMarker(GetNavigationMarkerLocation(), GetNavigationMarkerAttachActor())
        }

        /// <summary>
        /// 在指定位置生成导航标记。对应 UE5 UNarrativeTask::SpawnNavigationMarker。
        /// </summary>
        /// <param name="task">关联的任务</param>
        /// <param name="markerLocation">标记位置</param>
        /// <param name="attachActor">标记附加的 Actor（可为 null）</param>
        public static void SpawnNavigationMarker(NarrativeTask task, Vector3 markerLocation, Actor attachActor = null)
        {
            // Flax-不兼容: UE5 的 MapMarker 在 Flax 无对应物，保留占位。原文 TODO: Flax 无内置导航标记系统，需自行实现。
            // UE5 中会 NewObject<UMapMarker> 并设置 MarkerTransform/DefaultDomains/Icon/Tint 等，
            // 当任务所属任务被追踪时注册标记。
        }

        /// <summary>
        /// 获取导航标记位置。对应 UE5 UNarrativeTask::GetNavigationMarkerLocation。
        /// 默认返回 MarkerSettings.MarkerLocation，子类可重写以提供动态位置。
        /// </summary>
        public static Vector3 GetNavigationMarkerLocation(NarrativeTask task)
        {
            // TODO [待源码]: 获取 UE5 源 UNarrativeTask.cpp 后补全 GetNavigationMarkerLocation 实现。默认返回零向量，子类可重写以提供动态位置。
            return Vector3.Zero;
        }

        /// <summary>
        /// 获取导航标记应附加的 Actor。对应 UE5 UNarrativeTask::GetNavigationMarkerAttachActor。
        /// 默认返回 null，子类可重写。
        /// </summary>
        public static Actor GetNavigationMarkerAttachActor(NarrativeTask task)
        {
            // TODO [待源码]: 获取 UE5 源 UNarrativeTask.cpp 后补全 GetNavigationMarkerAttachActor 实现。默认返回 null，子类可重写。
            return null;
        }
    }
}
