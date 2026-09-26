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
    /// 编辑器插件：自动检测并修复角色骨骼网格体的导入设置。
    /// <para>
    /// 负责两类历史故障：
    /// </para>
    /// <list type="number">
    /// <item>
    /// FBX 被导入为 <c>Model</c> 而非 <c>SkinnedModel</c> → <c>AnimatedModel.SkinnedModel</c> 无法赋值
    /// （<c>Content.Load&lt;SkinnedModel&gt;</c> 报 "Different loaded asset type"）。
    /// </item>
    /// <item>
    /// 重建导入时用 <c>new ModelTool.Options { ... }</c> 构造设置对象，**Scale / Rotation 等字段没有初值**
    /// （结构体字段默认值），从而写出 Scale=0.0001、Rotation=(0,0,0,0) 的非法导入设置：
    /// 模型被缩小到万分之一（实测包围盒身高 0.03，正确资产应为约 300），
    /// 且退化四元数会污染姿态校正（曾被迫用 ModelPitchCorrection/ModelYawCorrection=180° 去蒙）。
    /// 正确做法是以 <c>ModelTool.Options.Default</c> 为基准，并用 <c>Editor.TryRestoreImportOptions</c>
    /// 读回该资产原有设置后再改动需要改的字段。
    /// </item>
    /// </list>
    /// </summary>
    public class AutoSkinnedModelImportPlugin : EditorPlugin
    {
        /// <summary>目标资产路径（相对 Content 目录）</summary>
        private const string AssetPath = "Content/Character/Models/skm_uefn_mannequin.flax";

        /// <summary>
        /// 目标资产对应的源文件（相对工程根目录）——与资产内嵌导入设置里的 ImportPath 一致，
        /// 用于不依赖 ContentDatabase 的回退导入。
        /// </summary>
        private const string SourceFilePath = "SourceAssets/Character/Models/skm_uefn_mannequin.FBX";

        /// <summary>
        /// 正确导入缩放：与仓库内历史正常资产一致
        /// （`skm_uefn_mannequin.flax.backup` 与 `_import_test/skm_uefn_mannequin.flax` 均为 1.0，
        /// 也是标准导入对话框的 `ModelTool.Options.Default.Scale`）。
        /// </summary>
        private const float CorrectImportScale = 1.0f;

        /// <summary>
        /// 模型包围盒最大边的合理下限（单位）。低于此值说明导入缩放被写坏——
        /// 实测损坏资产为 0.03 单位，而角色约 180~300 单位，会导致世界场景中相机完全看不到角色。
        /// </summary>
        private const float MinSaneModelSize = 1f;

        /// <summary>包围盒最大边的合理上限（单位），超出则同样判定为脏数据</summary>
        private const float MaxSaneModelSize = 100000f;

        public AutoSkinnedModelImportPlugin()
        {
            _description = new PluginDescription
            {
                Name = "Auto SkinnedModel Import",
                Category = "Tools",
                Author = "HundunWorld",
                Version = new Version(1, 0),
                Description = "自动检测并修复 skm_uefn_mannequin 的导入设置（类型 SkinnedModel + 缩放/旋转有效性）"
            };
        }

        /// <summary>
        /// 一次性重建标记。资产文件被移出工程（几何块数据损坏且 git 历史里同样损坏、无法回退）后写入，
        /// 存在时允许走"新建资产"导入——这会分配**新 GUID**，所以必须同时更新按 GUID 的代码引用。
        /// 正常启动路径绝不触碰它。
        /// </summary>
        private const string FreshImportMarkerPath = "Content/Character/Models/.force-fresh-skm-import";

        /// <summary>重建目标所在的 Content 目录</summary>
        private const string TargetFolderVirtualPath = "Content/Character/Models";

        public override void InitializeEditor()
        {
            base.InitializeEditor();

            try
            {
                EnsureCorrectImportSettings();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AutoSkinnedModelImport] 初始化异常: {ex.Message}\n{ex.StackTrace}");
            }

            // 插件初始化阶段 ContentDatabase 的目录树还没建完，重建请求留到编辑器初始化结束后再发。
            FlaxEditor.Editor.Instance.InitializationEnd += OnEditorReadyTryFreshImport;
        }

        private void OnEditorReadyTryFreshImport()
        {
            FlaxEditor.Editor.Instance.InitializationEnd -= OnEditorReadyTryFreshImport;
            if (!HasFreshImportMarker()) return;

            try
            {
                RequestFreshImportFromSource();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AutoSkinnedModelImport] 重建导入异常: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static bool HasFreshImportMarker() =>
            System.IO.File.Exists(System.IO.Path.Combine(ProjectDirectory, FreshImportMarkerPath));

        private static string ProjectDirectory => StringUtils.NormalizePath(Globals.ProjectFolder);

        /// <summary>
        /// 资产已不存在时，从原始源文件重建。先订阅导入结束事件，好把新 GUID 直接打到日志里，
        /// 免得再去二进制里猜。
        /// </summary>
        private void RequestFreshImportFromSource()
        {
            var src = System.IO.Path.Combine(ProjectDirectory, SourceFilePath);
            if (!System.IO.File.Exists(src))
            {
                Debug.LogError($"[AutoSkinnedModelImport] 重建失败：源文件不存在 {src}");
                return;
            }

            var folder = FindTargetFolder();
            if (folder == null)
            {
                FlaxEditor.Editor.Instance.ContentDatabase.Rebuild(true);
                folder = FindTargetFolder();
            }

            if (folder == null)
            {
                Debug.LogError($"[AutoSkinnedModelImport] 无法定位目标目录，已放弃重建: {TargetFolderVirtualPath}");
                return;
            }

            var options = ModelTool.Options.Default;
            options.Type = ModelTool.ModelType.SkinnedModel;
            options.Scale = CorrectImportScale;
            options.Rotation = Quaternion.Identity;

            var importing = FlaxEditor.Editor.Instance.ContentImporting;
            importing.ImportFileEnd += OnFreshImportFileEnd;
            importing.Import(src, folder, true, options);

            var marker = System.IO.Path.Combine(ProjectDirectory, FreshImportMarkerPath);
            System.IO.File.Delete(marker);

            Debug.LogWarning(
                $"[AutoSkinnedModelImport] 已提交重建导入（资产此前不存在）：{SourceFilePath} → {TargetFolderVirtualPath}，" +
                "将生成**新的资产 GUID**，导入结束后请在日志中读取新 ID 并回填代码引用。");
        }

        private void OnFreshImportFileEnd(IFileEntryAction entry, bool failed)
        {
            FlaxEditor.Editor.Instance.ContentImporting.ImportFileEnd -= OnFreshImportFileEnd;

            if (failed)
            {
                Debug.LogError($"[AutoSkinnedModelImport] 重建导入失败: {entry?.ResultUrl}");
                return;
            }

            var abs = StringUtils.NormalizePath(System.IO.Path.Combine(ProjectDirectory, AssetPath));
            if (FlaxEditor.Editor.Instance.ContentDatabase.Find(abs) is BinaryAssetItem item)
            {
                Debug.Log($"[AutoSkinnedModelImport] 重建完成：{AssetPath} 新 GUID={item.ID:N}");
                return;
            }

            Debug.LogWarning($"[AutoSkinnedModelImport] 导入已结束但内容库尚未登记该资产，请重启编辑器后确认: {AssetPath}");
        }

        private static ContentFolder FindTargetFolder()
        {
            var abs = StringUtils.NormalizePath(System.IO.Path.Combine(ProjectDirectory, TargetFolderVirtualPath));
            return FlaxEditor.Editor.Instance.ContentDatabase.Find(abs) as ContentFolder;
        }

        /// <summary>
        /// 校验目标资产是否为 SkinnedModel 且导入设置（缩放/旋转）有效；不满足则按已知正确值修复后重建导入。
        /// </summary>
        private void EnsureCorrectImportSettings()
        {
            // 1. 加载资产
            var asset = Content.Load<Asset>(AssetPath);
            if (asset == null)
            {
                Debug.LogWarning($"[AutoSkinnedModelImport] 找不到资产: {AssetPath}");
                return;
            }

            // 2. 以引擎默认值为基准，再读回该资产已有的导入设置（保留历史/用户设置，只修坏掉的字段）
            //    注意：不能只依赖读回的设置做判断——若读回失败（路径格式不匹配等）会退化为引擎默认值，
            //    从而"看起来一切正常"而漏修。因此以**模型实际包围盒**为主判据。
            var options = ModelTool.Options.Default;
            if (!FlaxEditor.Editor.TryRestoreImportOptions(ref options, AssetPath))
            {
                Debug.LogWarning($"[AutoSkinnedModelImport] 未能读回资产导入设置，改用引擎默认值作为基准: {AssetPath}");
            }

            float modelSize = GetModelMaxDimension(asset);
            bool wrongType = !(asset is SkinnedModel);
            bool brokenSize = modelSize > 0f && (modelSize < MinSaneModelSize || modelSize > MaxSaneModelSize);
            bool brokenScale = options.Scale < 0.001f || options.Scale > 1000f;
            bool brokenRotation = !IsValidRotation(options.Rotation);

            if (!wrongType && !brokenSize && !brokenScale && !brokenRotation)
            {
                Debug.Log($"[AutoSkinnedModelImport] 导入设置正常（Type={asset.GetType().Name}, Scale={options.Scale:G6}, 模型最大边={modelSize:F1}），无需处理: {AssetPath}");
                return;
            }

            // 3. 修复：类型强制为 SkinnedModel；尺寸/缩放异常时回退到已知正确值
            if (wrongType)
                Debug.LogWarning($"[AutoSkinnedModelImport] 资产类型不正确: {AssetPath} 当前为 {asset.GetType().Name}，需要 SkinnedModel。");
            if (brokenSize)
                Debug.LogError(
                    $"[AutoSkinnedModelImport] **模型尺寸异常**：包围盒最大边={modelSize:F3} 单位" +
                    $"（合理区间 {MinSaneModelSize}~{MaxSaneModelSize}）。世界/UI 相机按约 180 单位身高取景，" +
                    $"该尺寸下角色完全不可见。判定为导入缩放被写坏，将重建导入。");
            if (brokenScale)
                Debug.LogWarning($"[AutoSkinnedModelImport] 导入缩放异常: Scale={options.Scale:G6}，回退为 {CorrectImportScale}。");
            if (brokenRotation)
                Debug.LogWarning($"[AutoSkinnedModelImport] 导入旋转非法（非单位四元数）: ({options.Rotation.X},{options.Rotation.Y},{options.Rotation.Z},{options.Rotation.W})，回退为 Identity。");

            options.Type = ModelTool.ModelType.SkinnedModel;
            if (brokenSize || brokenScale)
                options.Scale = CorrectImportScale;
            if (brokenSize || brokenRotation)
                options.Rotation = Quaternion.Identity;

            // 4. 执行重建导入（跳过设置对话框）
            Debug.Log($"[AutoSkinnedModelImport] 正在按修复后的设置重建导入: Type=SkinnedModel, Scale={options.Scale:G6}, Rotation=({options.Rotation.X},{options.Rotation.Y},{options.Rotation.Z},{options.Rotation.W})");

            // 4a. 首选：通过 ContentDatabase 资产项 Reimport
            //     注意：ContentDatabase.Find(path) 的路径格式匹配在实测中不可靠（可能返回 null）；
            //     引擎提供了可靠的 Find(Guid) 重载（见 ContentDatabaseModule.cs L251），故以 GUID 查找为主。
            var contentDb = FlaxEditor.Editor.Instance.ContentDatabase;
            var item = contentDb.Find(asset.ID) as BinaryAssetItem ?? contentDb.Find(AssetPath) as BinaryAssetItem;
            if (item != null)
            {
                FlaxEditor.Editor.Instance.ContentImporting.Reimport(item, options, true);
                Debug.Log("[AutoSkinnedModelImport] 重建导入请求已提交，请等待导入完成（完成后模型尺寸应恢复到数百单位）。");
                Debug.LogWarning(
                    "[AutoSkinnedModelImport] 注意：重建导入会替换资产文件，**本次编辑器会话内的内容注册可能因此失效**" +
                    "（表现为后续 PIE 里 Content.Load 找不到模型、日志出现 \"Missing file .../skm_uefn_mannequin\"）。" +
                    "导入完成后请重启 Flax Editor 再运行；重启后插件会发现设置已正确，不会再改动资产。");
                return;
            }

            // 4b. 不做"源文件直接导入"的回退！
            //     `Editor.Import(src, dst, options)` 会**新建一个资产（新 GUID）**，从而改变资产身份：
            //     所有按 GUID 的引用（EquipmentDatabase.DefaultBodyModelGuid、Prefab 里的 SkinnedModel、
            //     其它 .prefab/.scene 引用）会一起失效。实测已因此产生一次"资产在但 Content.Load 找不到"。
            //     因此这里只报错并给出可执行动作，绝不擅自改变资产身份。
            Debug.LogError(
                $"[AutoSkinnedModelImport] ContentDatabase 查不到资产项（GUID/路径双查失败，ID={asset.ID}），" +
                $"已放弃自动修复以避免改变资产 GUID。请**重启 Flax Editor** 让本插件在启动阶段重跑，" +
                $"或在编辑器里手动对「{AssetPath}」执行 Reimport（源文件 {SourceFilePath}，" +
                $"Type=SkinnedModel、Scale={CorrectImportScale}、Rotation=Identity）。");
        }

        /// <summary>
        /// 取模型包围盒的最大边（单位）。**这是判断导入缩放是否被写坏的主判据**：
        /// 无论导入设置如何损坏，几何体被 cook 进资产后的实际尺寸都是唯一事实。
        /// </summary>
        /// <returns>最大边长度；无法获取时返回 0</returns>
        private static float GetModelMaxDimension(Asset asset)
        {
            try
            {
                BoundingBox box;
                if (asset is SkinnedModel skinned)
                    box = skinned.GetBox(0);
                else if (asset is Model model)
                    box = model.GetBox(0);
                else
                    return 0f;

                return Mathf.Max(box.Size.X, Mathf.Max(box.Size.Y, box.Size.Z));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AutoSkinnedModelImport] 读取模型包围盒失败: {ex.Message}");
                return 0f;
            }
        }

        /// <summary>
        /// 判断导入旋转是否为可用的单位四元数。
        /// <c>(0,0,0,0)</c> 这类退化值（结构体未初始化时产生）会被判定为非法。
        /// </summary>
        private static bool IsValidRotation(Quaternion r)
        {
            float lenSqr = r.X * r.X + r.Y * r.Y + r.Z * r.Z + r.W * r.W;
            return lenSqr > 0.5f && lenSqr < 1.5f;
        }
    }
}
#endif
