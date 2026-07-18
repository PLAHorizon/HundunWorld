#if FLAX_EDITOR
using System;
using FlaxEditor;
using FlaxEditor.Content;
using FlaxEditor.Content.Import;
using FlaxEngine;
using FlaxEngine.Tools;

namespace HundunWorld.Game.Editor
{
    /// <summary>
    /// 编辑器插件：自动检测并重新导入角色骨骼网格体为 SkinnedModel。
    /// 解决因 FBX 导入类型错误（Model 而非 SkinnedModel）导致 AnimatedModel 无法赋值的问题。
    /// </summary>
    public class AutoSkinnedModelImportPlugin : EditorPlugin
    {
        public AutoSkinnedModelImportPlugin()
        {
            _description = new PluginDescription
            {
                Name = "Auto SkinnedModel Import",
                Category = "Tools",
                Author = "HundunWorld",
                Version = new Version(1, 0),
                Description = "自动检测 skm_uefn_mannequin 是否为 SkinnedModel，否则自动 Reimport"
            };
        }

        public override void InitializeEditor()
        {
            base.InitializeEditor();

            try
            {
                AutoReimportIfNeeded();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AutoSkinnedModelImport] 初始化异常: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void AutoReimportIfNeeded()
        {
            const string assetPath = "Content/Character/Models/skm_uefn_mannequin.flax";

            // 1. 加载资产检查当前类型
            var asset = Content.Load<Asset>(assetPath);
            if (asset == null)
            {
                Debug.LogWarning($"[AutoSkinnedModelImport] 找不到资产: {assetPath}");
                return;
            }

            if (asset is SkinnedModel)
            {
                Debug.Log($"[AutoSkinnedModelImport] 资产已经是 SkinnedModel，无需处理: {assetPath}");
                return;
            }

            Debug.LogWarning($"[AutoSkinnedModelImport] 资产类型不正确: {assetPath} 当前为 {asset.GetType().Name}，需要 SkinnedModel。准备自动 Reimport...");

            // 2. 在 ContentDatabase 中查找该资产项
            var item = FlaxEditor.Editor.Instance.ContentDatabase.Find(assetPath);
            if (item == null)
            {
                Debug.LogError($"[AutoSkinnedModelImport] 在 ContentDatabase 中找不到: {assetPath}");
                return;
            }

            if (!(item is BinaryAssetItem binaryItem))
            {
                Debug.LogError($"[AutoSkinnedModelImport] 资产项不是 BinaryAssetItem: {item.GetType().Name}");
                return;
            }

            // 3. 构造 SkinnedModel 导入设置
            var settings = new ModelTool.Options
            {
                Type = ModelTool.ModelType.SkinnedModel,
                ImportLODs = true,
                ImportMaterials = true,
                ImportTextures = true,
                MergeMeshes = true,
                OptimizeMeshes = true,
            };

            // 4. 执行 Reimport（跳过设置对话框）
            Debug.Log($"[AutoSkinnedModelImport] 正在以 SkinnedModel 类型 Reimport: {assetPath}");
            FlaxEditor.Editor.Instance.ContentImporting.Reimport(binaryItem, settings, true);
            Debug.Log($"[AutoSkinnedModelImport] Reimport 请求已提交，请等待导入完成。");
        }
    }
}
#endif
