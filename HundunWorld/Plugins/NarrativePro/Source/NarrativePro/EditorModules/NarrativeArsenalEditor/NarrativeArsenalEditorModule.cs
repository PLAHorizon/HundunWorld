using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.EditorModules.NarrativeArsenalEditor
{
    /// <summary>
    /// NarrativeArsenal 编辑器模块入口。对应 UE5 FNarrativeArsenalEditorModule。
    ///
    /// 移植说明：
    /// UE5 中此模块提供资产类型操作（AssetTypeActions）、组件可视化器（ComponentVisualizer）、
    /// 详情面板定制（DetailCustomization）、项目设置（ProjectSetup）、工具栏菜单（Toolbar）、
    /// 蓝图工厂（BlueprintFactories）等编辑器扩展功能。
    ///
    /// Flax Engine 限制：
    /// - Flax 没有 FAssetTypeActions_Base 等资产类型操作框架
    /// - Flax 没有 FComponentVisualizer 组件可视化器
    /// - Flax 没有 IDetailCustomization 详情面板定制
    /// - Flax 没有 Slate/UMG 编辑器 UI 框架
    /// - Flax 没有 AssetEditorToolkit 资产编辑器工具包
    /// - Flax 没有 FExtensibilityManager 扩展性管理器
    ///
    /// 因此本模块仅保留模块入口占位与资产分类常量，具体编辑器功能需用 Flax Editor API 重新实现。
    /// 原始 UE5 文件清单见 <see cref="SourceFileList"/>。
    /// </summary>
    public static class NarrativeArsenalEditorModule
    {
        /// <summary>游戏资产分类标识。对应 UE5 FNarrativeArsenalEditorModule::GameAssetCategory。</summary>
        public static uint GameAssetCategory = 0;

        /// <summary>角色创建器资产分类标识。对应 UE5 FNarrativeArsenalEditorModule::CharacterCreatorAssetCategory。</summary>
        public static uint CharacterCreatorAssetCategory = 0;

        /// <summary>模块启动。对应 UE5 StartupModule。Flax 中以日志占位。</summary>
        public static void StartupModule()
        {
            NarrativeLog.Log("[NarrativeArsenalEditor] StartupModule - UE5 编辑器扩展模块（Flax 中为占位）");
            RegisterAssetCategories();
        }

        /// <summary>模块关闭。对应 UE5 ShutdownModule。</summary>
        public static void ShutdownModule()
        {
            NarrativeLog.Log("[NarrativeArsenalEditor] ShutdownModule");
        }

        /// <summary>注册资产分类。对应 UE5 中 GameAssetCategory 的初始化。</summary>
        private static void RegisterAssetCategories()
        {
            // Flax-不兼容: UE5 的 FAssetCategoryManager/IAssetTools 资产分类注册 API 在 Flax 无对应物，
            // Flax 编辑器无内置的资产分类树结构，此处仅以常量占位保留分类 ID。
            GameAssetCategory = 1;
            CharacterCreatorAssetCategory = 2;
        }

        /// <summary>
        /// 原始 UE5 文件清单（共 61 个 .h/.cpp 文件）。
        /// 这些文件全部依赖 UE5 编辑器 API，Flax 中无对应物，此处仅作记录。
        /// </summary>
        public static readonly string[] SourceFileList = new string[]
        {
            // Private
            "ArsenalBlueprintFactories.cpp/h",
            "ArsenalBlueprints.cpp/h",
            "ArsenalSettingsDetailCustomization.cpp/h",
            "AssetTypeActions_ArsenalItems.cpp/h",
            "AssetTypeActions_EquippableItem.cpp/h",
            "AssetTypeActions_ItemCollection.cpp/h",
            "AssetTypeActions_NarrativeItem.cpp/h",
            "AssetTypeActions_NPCDefinition.cpp/h",
            "EquippableItemBlueprint.cpp/h",
            "EquippableItemBlueprintFactory.cpp/h",
            "Interaction/InteractableComponentVisualizer.cpp/h",
            "NarrativeArsenalEditorModule.cpp",
            "NarrativeArsenalStyle.cpp/h",
            "NarrativeEditorSaveMenus.cpp/h",
            "NarrativeInventoryStyle.cpp/h",
            "NarrativeItemBlueprint.cpp/h",
            "NarrativeItemBlueprintFactory.cpp/h",
            "NarrativeToolbar/NarrativeToolbar.cpp/h",
            "Navigation/POIDetailsCustomization.cpp",
            "Navigation/POIRenderingVisualizer.cpp",
            "NavigatorFunctionLibrary.cpp",
            "ProjectSetup/NarrativeProjectSetup.cpp/h",
            "ProjectSetup/NarrativeProjectSetupNotice.cpp/h",
            "Spawners/SpawnComponentVisualizer.cpp",
            "Spawners/SpawnerDetailsCustomization.cpp",
            "StableActor/StableActorPropertyCustomization.cpp",
            "TimeOfDay/TimeOfDayPropertyTypeCustomization.cpp/h",
            "TimeOfDay/TimeOfDayRangePropertyTypeCustomization.cpp/h",
            "TimeOfDay/TimeRuler.cpp/h",
            "ToolUtils/ToolUtils.cpp",
            // Public
            "ArsenalBlueprintFactories.h",
            "ArsenalBlueprints.h",
            "Interaction/InteractableComponentVisualizer.h",
            "NarrativeArsenalEditorModule.h",
            "Navigation/POIDetailsCustomization.h",
            "Navigation/POIRenderingVisualizer.h",
            "NavigatorFunctionLibrary.h",
            "Spawners/SpawnComponentVisualizer.h",
            "Spawners/SpawnerDetailsCustomization.h",
            "StableActor/StableActorPropertyCustomization.h",
            "ToolUtils/ToolUtils.h"
        };
    }
}
