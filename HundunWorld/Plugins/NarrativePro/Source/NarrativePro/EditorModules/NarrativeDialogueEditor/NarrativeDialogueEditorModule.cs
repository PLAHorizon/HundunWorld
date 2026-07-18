using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.EditorModules.NarrativeDialogueEditor
{
    /// <summary>
    /// Narrative 对话编辑器模块入口。对应 UE5 FNarrativeDialogueEditorModule。
    ///
    /// 移植说明：
    /// UE5 中此模块提供对话图编辑器（DialogueGraphEditor）、对话蓝图编译器（DialogueBlueprintCompiler）、
    /// 对话节点图（DialogueGraphNode）、对话连接绘制策略（DialogueConnectionDrawingPolicy）、
    /// 对话调试器（DialogueDebugger）、对话编辑器命令/样式/工具栏/标签页等编辑器功能。
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
    /// 对话数据运行时部分已移植到 NarrativePro.Tales（DialogueSM.cs、DialogueAsset.cs 等）。
    /// 原始 UE5 文件清单见 <see cref="SourceFileList"/>。
    /// </summary>
    public static class NarrativeDialogueEditorModule
    {
        /// <summary>游戏资产分类标识。对应 UE5 FNarrativeDialogueEditorModule::GameAssetCategory。</summary>
        public static uint GameAssetCategory = 0;

        /// <summary>对话编辑器 App 标识。对应 UE5 FNarrativeDialogueEditorModule::DialogueEditorAppId。</summary>
        public static readonly string DialogueEditorAppId = "NarrativeDialogueEditorApp";

        /// <summary>模块启动。对应 UE5 StartupModule。</summary>
        public static void StartupModule()
        {
            NarrativeLog.Log("[NarrativeDialogueEditor] StartupModule - UE5 对话编辑器模块（Flax 中为占位）");
            GameAssetCategory = 1;
        }

        /// <summary>模块关闭。对应 UE5 ShutdownModule。</summary>
        public static void ShutdownModule()
        {
            NarrativeLog.Log("[NarrativeDialogueEditor] ShutdownModule");
        }

        /// <summary>
        /// 原始 UE5 文件清单（共 59 个 .h/.cpp 文件）。
        /// 对话运行时数据结构已移植到 NarrativePro.Tales 命名空间。
        /// </summary>
        public static readonly string[] SourceFileList = new string[]
        {
            "AssetTypeActions_DialogueAsset.cpp/h",
            "AssetTypeActions_DialogueBlueprint.cpp/h",
            "DialogueAssetFactory.cpp/h",
            "DialogueBlueprintCompiler.cpp/h",
            "DialogueConnectionDrawingPolicy.cpp/h",
            "DialogueDebugger.cpp/h",
            "DialogueEditorCommands.cpp/h",
            "DialogueEditorDetails.cpp/h",
            "DialogueEditorModes.cpp/h",
            "DialogueEditorSettings.cpp/h",
            "DialogueEditorStyle.cpp/h",
            "DialogueEditorTabFactories.cpp/h",
            "DialogueEditorTabs.cpp/h",
            "DialogueEditorToolbar.cpp/h",
            "DialogueEditorTypes.cpp/h",
            "DialogueGraph.cpp/h",
            "DialogueGraphEditor.cpp/h",
            "DialogueGraphNode.cpp/h",
            "DialogueGraphNode_NPC.cpp/h",
            "DialogueGraphNode_Player.cpp/h",
            "DialogueGraphNode_Root.cpp/h",
            "DialogueGraphSchema.cpp/h",
            "DialogueNodeUserWidget.cpp",
            "NarrativeDialogueEditorModule.cpp",
            "NodeSelector/DialogueNodeSelectorPropertyCustomization.cpp/h",
            "NodeSelector/GraphPin_DialogueNodeSelector.cpp/h",
            "SDialogueGraphNode.cpp/h",
            "SDialogueGraphPin.cpp/h",
            "DialogueBlueprint.cpp/h",
            "DialogueNodeUserWidget.h",
            "IDialogueEditor.h",
            "NarrativeDialogueEditorModule.h"
        };
    }
}
