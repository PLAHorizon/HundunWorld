using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.EditorModules.NarrativeQuestEditor
{
    /// <summary>
    /// Narrative 任务编辑器模块入口。对应 UE5 FNarrativeQuestEditorModule。
    ///
    /// 移植说明：
    /// UE5 中此模块提供任务图编辑器（QuestGraphEditor）、任务蓝图编译器（QuestBlueprintCompiler）、
    /// 任务节点图（QuestGraphNode_Action/State/Root/Success/Failure/PersistentTasks）、
    /// 任务连接绘制策略（QuestConnectionDrawingPolicy）、任务调试器（QuestDebugger）、
    /// 任务编辑器命令/样式/工具栏/标签页等编辑器功能。
    ///
    /// Flax Engine 限制：
    /// - Flax 没有 FAssetEditorToolkit 资产编辑器工具包
    /// - Flax 没有 UEdGraph 图编辑器框架
    /// - Flax 没有 FConnectionDrawingPolicy 连接绘制策略
    /// - Flax 没有 SGraphNode 图节点 Slate 控件
    /// - Flax 没有 FBlueprintCompiler 蓝图编译器
    /// - Flax 没有 FUICommandList/FUICommandInfo 命令系统
    ///
    /// 因此本模块仅保留模块入口占位与资产分类常量。
    /// 任务数据运行时部分已移植到 NarrativePro.Tales（QuestSM.cs、QuestBlueprintGeneratedClass.cs 等）。
    /// 原始 UE5 文件清单见 <see cref="SourceFileList"/>。
    /// </summary>
    public static class NarrativeQuestEditorModule
    {
        /// <summary>游戏资产分类标识。对应 UE5 FNarrativeQuestEditorModule::GameAssetCategory。</summary>
        public static uint GameAssetCategory = 0;

        /// <summary>任务编辑器 App 标识。对应 UE5 FNarrativeQuestEditorModule::QuestEditorAppId。</summary>
        public static readonly string QuestEditorAppId = "NarrativeQuestEditorApp";

        /// <summary>模块启动。对应 UE5 StartupModule。</summary>
        public static void StartupModule()
        {
            NarrativeLog.Log("[NarrativeQuestEditor] StartupModule - UE5 任务编辑器模块（Flax 中为占位）");
            GameAssetCategory = 1;
        }

        /// <summary>模块关闭。对应 UE5 ShutdownModule。</summary>
        public static void ShutdownModule()
        {
            NarrativeLog.Log("[NarrativeQuestEditor] ShutdownModule");
        }

        /// <summary>
        /// 原始 UE5 文件清单（共 73 个 .h/.cpp 文件）。
        /// 任务运行时数据结构已移植到 NarrativePro.Tales 命名空间。
        /// </summary>
        public static readonly string[] SourceFileList = new string[]
        {
            "AssetTypeActions_NarrativeQuestTask.cpp/h",
            "AssetTypeActions_QuestAction.cpp/h",
            "AssetTypeActions_QuestAsset.cpp/h",
            "K2Node_CompleteNarrativeTask.cpp/h",
            "NarrativeQuestEditorModule.cpp",
            "NarrativeQuestTaskBlueprint.cpp",
            "NarrativeQuestTaskBlueprint.h",
            "NodeSelector/GraphPin_QuestNodeSelector.cpp/h",
            "NodeSelector/QuestNodeSelectorPropertyCustomization.cpp/h",
            "QuestActionFactory.cpp/h",
            "QuestAssetFactory.cpp/h",
            "QuestBlueprintCompiler.cpp/h",
            "QuestConnectionDrawingPolicy.cpp/h",
            "QuestDebugger.cpp/h",
            "QuestEditorCommands.cpp/h",
            "QuestEditorDetails.cpp/h",
            "QuestEditorModes.cpp/h",
            "QuestEditorSettings.cpp/h",
            "QuestEditorStyle.cpp/h",
            "QuestEditorTabFactories.cpp/h",
            "QuestEditorTabs.cpp/h",
            "QuestEditorToolbar.cpp/h",
            "QuestEditorTypes.cpp/h",
            "QuestGraph.cpp/h",
            "QuestGraphEditor.cpp/h",
            "QuestGraphNode.cpp/h",
            "QuestGraphNode_Action.cpp/h",
            "QuestGraphNode_Failure.cpp/h",
            "QuestGraphNode_PersistentTasks.cpp/h",
            "QuestGraphNode_Root.cpp/h",
            "QuestGraphNode_State.cpp/h",
            "QuestGraphNode_Success.cpp/h",
            "QuestGraphSchema.cpp/h",
            "QuestNodeUserWidget.cpp",
            "QuestTaskBlueprintFactory.h",
            "SQuestGraphNode.cpp/h",
            "IQuestEditor.h",
            "NarrativeQuestEditorModule.h",
            "QuestBlueprint.cpp/h",
            "QuestNodeUserWidget.h"
        };
    }
}
