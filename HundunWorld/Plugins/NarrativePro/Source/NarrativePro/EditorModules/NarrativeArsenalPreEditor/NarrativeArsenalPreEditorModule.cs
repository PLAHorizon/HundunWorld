using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.EditorModules.NarrativeArsenalPreEditor
{
    /// <summary>
    /// NarrativeArsenal 预编辑器模块入口。对应 UE5 FNarrativeArsenalPreEditorModule。
    ///
    /// 移植说明：
    /// UE5 中此模块提供 Actor 工厂（ActorFactory）、节点选择器属性定制（NodeSelectorPropertyCustomization）、
    /// 编辑器引擎扩展（NarrativeEditorEngine）等预编辑器功能。
    ///
    /// Flax Engine 限制：
    /// - Flax 没有 UActorFactory Actor 工厂概念
    /// - Flax 没有 FGraphEditor 图编辑器
    /// - Flax 没有 IPropertyTypeCustomization 属性定制
    /// - Flax 没有 UEditorEngine 编辑器引擎基类
    ///
    /// 因此本模块仅保留模块入口占位，具体预编辑器功能需用 Flax Editor API 重新实现。
    /// 原始 UE5 文件清单见 <see cref="SourceFileList"/>。
    /// </summary>
    public static class NarrativeArsenalPreEditorModule
    {
        /// <summary>模块启动。对应 UE5 StartupModule。</summary>
        public static void StartupModule()
        {
            NarrativeLog.Log("[NarrativeArsenalPreEditor] StartupModule - UE5 预编辑器模块（Flax 中为占位）");
        }

        /// <summary>模块关闭。对应 UE5 ShutdownModule。</summary>
        public static void ShutdownModule()
        {
            NarrativeLog.Log("[NarrativeArsenalPreEditor] ShutdownModule");
        }

        /// <summary>
        /// 原始 UE5 文件清单（共 12 个 .h/.cpp 文件）。
        /// </summary>
        public static readonly string[] SourceFileList = new string[]
        {
            "Interaction/ActorFactoryNarrativeItem.cpp/h",
            "NarrativeArsenalPreEditor.cpp/h",
            "NarrativeEditorEngine.cpp/h",
            "NodeSelector/GraphPin_NodeSelectorBase.cpp/h",
            "NodeSelector/NodeSelectorPropertyCustomizationBase.cpp/h",
            "Spawners/ActorFactoryNPCDefinition.cpp/h"
        };
    }
}
