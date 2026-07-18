using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Items;

namespace NarrativePro.Widgets
{
    /// <summary>
    /// 游戏玩法 HUD。移植自 UE5 NarrativeArsenal: Widgets/NarrativeGameplayHUD.h（UNarrativeGameplayHUD : UCommonUserWidget）。
    /// 管理分层 UI 容器、菜单打开与通知显示。
    ///
    /// 简化点：
    /// - 任务要求：Flax 无 HUD 基类，NarrativeGameplayHUD 改为 Script 占位（保留字段定义与方法签名）。
    /// - Flax 无 UMG/CommonUI，分层容器（UCommonActivatableWidgetContainerBase）与控件（UWidget）用 object 占位。
    /// - TSubclassOf&lt;UNarrativeMenu&gt; → string 路径；FText → string；FGameplayTag → GameplayTag。
    /// - 渲染/激活相关方法体以占位形式保留，需用 Flax UIControl/UICanvas 重新实现。
    /// Flax-不兼容: UE5 的 UMG/CommonUI 在 Flax 无对应物，保留占位。原文 TODO: 使用 Flax UIControl/UICanvas 重新实现 HUD 层与通知逻辑。
    /// </summary>
    public class NarrativeGameplayHUD : Script
    {
        /// <summary>关键控件列表（对应 UE5 EssentialWidgets，UWidget* → object 占位）</summary>
        public List<object> EssentialWidgets { get; set; } = new List<object>();

        /// <summary>分层 UI 容器（层标签 → 容器，UCommonActivatableWidgetContainerBase* → object 占位）</summary>
        public Dictionary<GameplayTag, object> Layers { get; set; } = new Dictionary<GameplayTag, object>();

        /// <summary>注册一个 UI 层。</summary>
        /// <param name="layerTag">层标签</param>
        /// <param name="layerWidget">层容器（占位）</param>
        public virtual void RegisterLayer(GameplayTag layerTag, object layerWidget)
        {
            // Flax-不兼容: UE5 的 UMG/CommonUI 在 Flax 无对应物，保留占位。原文 TODO: 使用 Flax UICanvas/UIControl 重新实现层注册。
            if (layerTag.IsValid() && layerWidget != null)
            {
                Layers[layerTag] = layerWidget;
            }
        }

        /// <summary>返回指定层的容器。</summary>
        /// <param name="layerTag">层标签</param>
        /// <returns>层容器（占位）</returns>
        public virtual object GetLayerContainer(GameplayTag layerTag)
        {
            // Flax-不兼容: UE5 的 UMG/CommonUI 在 Flax 无对应物，保留占位。原文 TODO: 使用 Flax UICanvas/UIControl 重新实现。
            if (layerTag.IsValid() && Layers.TryGetValue(layerTag, out var container))
            {
                return container;
            }
            return null;
        }

        /// <summary>打开菜单。</summary>
        /// <param name="menuClass">菜单类路径（TSubclassOf&lt;UNarrativeMenu&gt; → string）</param>
        /// <param name="layerTag">要在其上打开菜单的层标签</param>
        /// <returns>打开的菜单实例（占位）</returns>
        public virtual NarrativeMenu OpenMenu(string menuClass, GameplayTag layerTag)
        {
            // Flax-不兼容: UE5 的 UMG/CommonUI 在 Flax 无对应物，保留占位。原文 TODO: 使用 Flax UIControl 重新实现菜单打开逻辑。
            return null;
        }

        /// <summary>设置 HUD 隐藏状态。</summary>
        /// <param name="bHideHUD">是否隐藏 HUD</param>
        /// <param name="bHideEvenEssentialWidgets">是否连关键控件一起隐藏</param>
        public virtual void SetHUDHidden(bool bHideHUD, bool bHideEvenEssentialWidgets)
        {
            // Flax-不兼容: UE5 的 UMG/CommonUI 在 Flax 无对应物，保留占位。原文 TODO: 使用 Flax UIControl 可见性重新实现。
        }

        /// <summary>显示通知。</summary>
        /// <param name="notificationText">通知文本</param>
        /// <param name="duration">显示时长（秒）</param>
        public virtual void ShowNotification(string notificationText, float duration)
        {
            // Flax-不兼容: UE5 的 UMG/CommonUI 在 Flax 无对应物，保留占位。原文 TODO: 使用 Flax UI 控件重新实现通知显示。
        }

        /// <summary>显示重大通知。</summary>
        /// <param name="notificationText">主通知文本</param>
        /// <param name="majorNotificationSubtext">通知副文本</param>
        /// <param name="duration">显示时长（秒）</param>
        /// <param name="bOverrideCurrentNotification">是否覆盖当前通知</param>
        public virtual void ShowMajorNotification(string notificationText, string majorNotificationSubtext, float duration, bool bOverrideCurrentNotification)
        {
            // Flax-不兼容: UE5 的 UMG/CommonUI 在 Flax 无对应物，保留占位。原文 TODO: 使用 Flax UI 控件重新实现重大通知显示。
        }
    }
}
