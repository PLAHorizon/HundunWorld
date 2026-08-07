using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEngine;
using FlaxEditor;
using FlaxEditor.Content;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// 材质与资产工具集：资产搜索/导入/查询、材质创建/参数设置/指派、材质实例。
    /// </summary>
    public static class MaterialAssetTools
    {
        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "asset_search",
                Description = "在项目 Content 数据库中搜索资产（模型/材质/贴图/预制体/场景等）。query 为名称关键字，type 可选过滤（如 Material、Texture、Model、Prefab、Scene）。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"},\"type\":{\"type\":\"string\",\"description\":\"可选：Material/MaterialInstance/Texture/Model/SkinnedModel/Prefab/Scene/Animation/AudioClip/Shader\"},\"limit\":{\"type\":\"integer\"}},\"required\":[\"query\"]}",
                Execute = AssetSearchAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "asset_get",
                Description = "查询单个资产的详细信息（类型、Guid、路径、是否已加载）。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"资产项目内路径或Guid\"}},\"required\":[\"path\"]}",
                Execute = AssetGetAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "asset_import",
                Description = "把外部文件导入为项目资产（贴图/模型/音频等），走编辑器内容导入管线。sourcePath 为本机绝对路径，targetDir 为项目内目标目录（默认 Content）。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"sourcePath\":{\"type\":\"string\"},\"targetDir\":{\"type\":\"string\"}},\"required\":[\"sourcePath\"]}",
                Execute = AssetImportAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "material_create",
                Description = "创建新的材质资产。path 为项目内路径，如 Content/Materials/MyMat.flax。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"]}",
                Execute = MaterialCreateAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "material_set_param",
                Description = "设置材质或材质实例的参数值并保存。value 支持数字(float)、颜色(十六进制字符串或{r,g,b})、向量({x,y,z,w})、贴图资产路径字符串。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"材质资产路径\"},\"param\":{\"type\":\"string\",\"description\":\"参数名\"},\"value\":{}},\"required\":[\"path\",\"param\",\"value\"]}",
                Execute = MaterialSetParamAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "material_assign",
                Description = "为模型 Actor（StaticModel/AnimatedModel 等）指派材质。可撤销。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"actor\":{\"type\":\"string\",\"description\":\"模型Actor名称或Guid\"},\"material\":{\"type\":\"string\",\"description\":\"材质资产路径\"},\"entryIndex\":{\"type\":\"integer\",\"description\":\"材质槽索引，默认0\"}},\"required\":[\"actor\",\"material\"]}",
                Undoable = true,
                Execute = MaterialAssignAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "material_instance_create",
                Description = "基于现有材质创建材质实例资产。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"baseMaterial\":{\"type\":\"string\",\"description\":\"基础材质路径\"},\"path\":{\"type\":\"string\",\"description\":\"实例资产路径，如 Content/Materials/MyMatInst.flax\"}},\"required\":[\"baseMaterial\",\"path\"]}",
                Execute = MaterialInstanceCreateAsync
            });
        }

        // ==================== Handlers ====================

        private static Task<object> AssetSearchAsync(JsonElement args)
        {
            var query = EditorUtils.GetString(args, "query", "");
            var typeFilter = EditorUtils.GetString(args, "type");
            var limit = EditorUtils.GetInt(args, "limit", 50);

            return MainThread.InvokeAsync<object>(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                var results = new List<object>();
                CollectAssets(editor.ContentDatabase.Game.Folder, query, typeFilter, results, 0, limit);
                return new { count = results.Count, assets = results };
            });
        }

        private static void CollectAssets(ContentFolder folder, string query, string typeFilter, List<object> results, int depth, int limit)
        {
            if (folder == null || depth > 14 || results.Count >= limit)
                return;

            foreach (var child in folder.Children)
            {
                if (results.Count >= limit)
                    return;

                if (child is AssetItem assetItem)
                {
                    var kind = GetAssetKind(assetItem);
                    var name = child.ShortName ?? "";

                    bool nameMatch = string.IsNullOrEmpty(query) ||
                                     name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     (child.Path != null && child.Path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
                    bool typeMatch = string.IsNullOrEmpty(typeFilter) ||
                                     kind.Equals(typeFilter, StringComparison.OrdinalIgnoreCase);

                    if (nameMatch && typeMatch)
                    {
                        results.Add(new
                        {
                            name,
                            path = child.Path,
                            kind,
                            id = assetItem.ID.ToString()
                        });
                    }
                }
                else if (child is ContentFolder sub)
                {
                    CollectAssets(sub, query, typeFilter, results, depth + 1, limit);
                }
            }
        }

        private static string GetAssetKind(AssetItem item)
        {
            if (item is SceneItem) return "Scene";
            if (item is PrefabItem) return "Prefab";
            if (item is ModelItem) return "Model";
            if (item is TextureAssetItem) return "Texture";
            if (item is ShaderSourceItem) return "ShaderSource";

            // 通过 BinaryAssetItem.Type 或 TypeName 细化
            if (item is BinaryAssetItem binary && binary.Type != null)
                return binary.Type.Name;

            var tn = item.TypeName;
            if (!string.IsNullOrEmpty(tn))
                return tn;

            return "Unknown";
        }

        private static Task<object> AssetGetAsync(JsonElement args)
        {
            var pathOrId = EditorUtils.GetString(args, "path");

            return MainThread.InvokeAsync<object>(() =>
            {
                var editor = FlaxEditor.Editor.Instance;

                ContentItem item = null;
                if (Guid.TryParse(pathOrId, out var guid))
                    item = editor.ContentDatabase.Find(guid);
                if (item == null)
                    item = editor.ContentDatabase.Find(pathOrId);

                if (item == null)
                    throw new ArgumentException("资产不存在: " + pathOrId);

                var assetItem = item as AssetItem;
                Asset loadedAsset = null;
                if (assetItem != null)
                {
                    loadedAsset = Content.GetAsset(assetItem.ID);
                }

                return new
                {
                    name = item.ShortName,
                    path = item.Path,
                    kind = assetItem != null ? GetAssetKind(assetItem) : (item is ContentFolder ? "Folder" : "File"),
                    id = assetItem?.ID.ToString(),
                    isLoaded = loadedAsset != null && loadedAsset.IsLoaded
                };
            });
        }

        private static async Task<object> AssetImportAsync(JsonElement args)
        {
            var sourcePath = EditorUtils.GetString(args, "sourcePath");
            var targetDir = EditorUtils.GetString(args, "targetDir", "Content");

            if (string.IsNullOrEmpty(sourcePath))
                throw new ArgumentException("缺少 sourcePath");
            if (!File.Exists(sourcePath))
                throw new ArgumentException("源文件不存在: " + sourcePath);

            await MainThread.InvokeAsync(() =>
            {
                var editor = FlaxEditor.Editor.Instance;

                var fullDir = Path.Combine(Globals.ProjectFolder, targetDir);
                if (!Directory.Exists(fullDir))
                    Directory.CreateDirectory(fullDir);

                var folder = editor.ContentDatabase.Find(fullDir) as ContentFolder;
                if (folder == null)
                {
                    editor.ContentDatabase.Rebuild(true);
                    folder = editor.ContentDatabase.Find(fullDir) as ContentFolder;
                }
                if (folder == null)
                    throw new ArgumentException("无法定位目标目录: " + targetDir);

                editor.ContentImporting.Import(sourcePath, folder, true);
            });

            // 等待导入队列完成
            var done = await MainThread.WaitUntilAsync(
                () => !FlaxEditor.Editor.Instance.ContentImporting.IsImporting, 180000);

            if (!done)
                return new { status = "importing", message = "导入仍在进行，可稍后用 asset_search 确认" };

            // 刷新数据库并查找新资产
            var imported = await MainThread.InvokeAsync(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                editor.ContentDatabase.Rebuild(true);

                var expectedName = Path.GetFileNameWithoutExtension(sourcePath);
                var matches = new List<object>();
                CollectAssetsByExactName(editor.ContentDatabase.Game.Folder, expectedName, matches, 0);
                return matches;
            });

            return new { status = "imported", source = sourcePath, targetDir, foundAssets = imported };
        }

        private static void CollectAssetsByExactName(ContentFolder folder, string name, List<object> results, int depth)
        {
            if (folder == null || depth > 14)
                return;
            foreach (var child in folder.Children)
            {
                if (child is AssetItem assetItem &&
                    string.Equals(child.ShortName, name, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new
                    {
                        name = child.ShortName,
                        path = child.Path,
                        kind = GetAssetKind(assetItem),
                        id = assetItem.ID.ToString()
                    });
                }
                else if (child is ContentFolder sub)
                {
                    CollectAssetsByExactName(sub, name, results, depth + 1);
                }
            }
        }

        private static async Task<object> MaterialCreateAsync(JsonElement args)
        {
            var relPath = EditorUtils.GetString(args, "path");
            if (string.IsNullOrEmpty(relPath))
                throw new ArgumentException("缺少 path 参数");

            var created = await MainThread.InvokeAsync(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                var fullPath = Path.Combine(Globals.ProjectFolder, relPath);
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // CreateAsset 偏好正斜杠项目相对路径；失败时再用文件存在性二次验证
                var normalized = relPath.Replace('\\', '/');
                var ok = FlaxEditor.Editor.CreateAsset(FlaxEditor.Editor.NewAssetType.Material, normalized)
                         || FlaxEditor.Editor.CreateAsset(FlaxEditor.Editor.NewAssetType.Material, fullPath);
                if (!ok && !File.Exists(fullPath))
                    throw new InvalidOperationException("材质创建失败: " + relPath);

                editor.ContentDatabase.Rebuild(true);
                return true;
            });

            return new { status = "created", path = relPath };
        }

        private static Task<object> MaterialSetParamAsync(JsonElement args)
        {
            var path = EditorUtils.GetString(args, "path");
            var paramName = EditorUtils.GetString(args, "param");

            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(paramName))
                throw new ArgumentException("缺少 path 或 param");
            if (!args.TryGetProperty("value", out var value))
                throw new ArgumentException("缺少 value");

            return MainThread.InvokeAsync<object>(() =>
            {
                var material = Content.Load<MaterialBase>(path, 10000.0);
                if (material == null || !material.IsLoaded)
                    throw new ArgumentException("材质加载失败: " + path);

                var parameter = material.GetParameter(paramName);
                if (parameter == null)
                {
                    var available = material.Parameters.Select(p => p.Name).ToList();
                    throw new ArgumentException("材质参数不存在: " + paramName + "（可用: " + string.Join(", ", available) + "）");
                }

                var converted = ConvertMaterialParamValue(value);
                material.SetParameterValue(paramName, converted, true);

                if (!material.Save(path))
                    throw new InvalidOperationException("材质保存失败: " + path);

                return new
                {
                    status = "set",
                    path,
                    param = paramName,
                    value = converted?.ToString()
                };
            });
        }

        private static object ConvertMaterialParamValue(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Number:
                    return value.GetSingle();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.String:
                {
                    var str = value.GetString();
                    // 优先按颜色解析
                    if (Color.TryParse(str, out var color))
                        return color;
                    // 否则按贴图资产路径加载
                    var tex = Content.Load<Texture>(str, 10000.0);
                    if (tex != null && tex.IsLoaded)
                        return tex;
                    return str;
                }
                case JsonValueKind.Object:
                {
                    if (value.TryGetProperty("w", out _))
                        return new Float4(
                            EditorUtils.GetNum(value, "x"),
                            EditorUtils.GetNum(value, "y"),
                            EditorUtils.GetNum(value, "z"),
                            EditorUtils.GetNum(value, "w"));
                    if (value.TryGetProperty("r", out _) || value.TryGetProperty("R", out _))
                        return new Color(
                            EditorUtils.GetNum(value, "r"),
                            EditorUtils.GetNum(value, "g"),
                            EditorUtils.GetNum(value, "b"),
                            EditorUtils.GetNum(value, "a", 1f));
                    return new Float4(
                        EditorUtils.GetNum(value, "x"),
                        EditorUtils.GetNum(value, "y"),
                        EditorUtils.GetNum(value, "z"),
                        0f);
                }
                default:
                    throw new ArgumentException("不支持的材质参数值类型: " + value.ValueKind);
            }
        }

        private static Task<object> MaterialAssignAsync(JsonElement args)
        {
            var actorRef = EditorUtils.GetString(args, "actor");
            var materialPath = EditorUtils.GetString(args, "material");
            var entryIndex = EditorUtils.GetInt(args, "entryIndex", 0);

            return MainThread.InvokeAsync<object>(() =>
            {
                var actor = EditorUtils.FindActor(actorRef);
                if (actor == null)
                    throw new ArgumentException("Actor 不存在: " + actorRef);

                // 自身或子级中找 ModelInstanceActor
                var model = actor as ModelInstanceActor;
                if (model == null)
                    model = FindChildOfType(actor, typeof(ModelInstanceActor)) as ModelInstanceActor;
                if (model == null)
                    throw new ArgumentException(actorRef + " 不是模型 Actor（StaticModel/AnimatedModel），也未找到模型子级");

                var material = Content.Load<MaterialBase>(materialPath, 10000.0);
                if (material == null || !material.IsLoaded)
                    throw new ArgumentException("材质加载失败: " + materialPath);

                if (entryIndex < 0 || entryIndex >= model.MaterialSlots.Length)
                    throw new ArgumentException("材质槽索引越界: " + entryIndex + "（共 " + model.MaterialSlots.Length + " 个槽位）");

                var oldMaterial = model.GetMaterial(entryIndex);
                model.SetMaterial(entryIndex, material);

                var capturedModel = model;
                AgentUndo.Record("AI: 指派材质 (" + model.Name + ")", () =>
                {
                    capturedModel.SetMaterial(entryIndex, oldMaterial);
                }, null);

                return new
                {
                    status = "assigned",
                    actor = model.Name,
                    entryIndex,
                    material = materialPath,
                    previousMaterial = oldMaterial?.Path
                };
            });
        }

        private static Actor FindChildOfType(Actor root, Type type)
        {
            foreach (var child in root.Children)
            {
                if (type.IsInstanceOfType(child))
                    return child;
                var found = FindChildOfType(child, type);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static Task<object> MaterialInstanceCreateAsync(JsonElement args)
        {
            var basePath = EditorUtils.GetString(args, "baseMaterial");
            var relPath = EditorUtils.GetString(args, "path");

            if (string.IsNullOrEmpty(basePath) || string.IsNullOrEmpty(relPath))
                throw new ArgumentException("缺少 baseMaterial 或 path");

            return MainThread.InvokeAsync<object>(() =>
            {
                var editor = FlaxEditor.Editor.Instance;

                var baseMaterial = Content.Load<Material>(basePath, 10000.0);
                if (baseMaterial == null || !baseMaterial.IsLoaded)
                    throw new ArgumentException("基础材质加载失败: " + basePath);

                var fullPath = Path.Combine(Globals.ProjectFolder, relPath);
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var normalized = relPath.Replace('\\', '/');
                var ok = FlaxEditor.Editor.CreateAsset(FlaxEditor.Editor.NewAssetType.MaterialInstance, normalized)
                         || FlaxEditor.Editor.CreateAsset(FlaxEditor.Editor.NewAssetType.MaterialInstance, fullPath);
                if (!ok && !File.Exists(fullPath))
                    throw new InvalidOperationException("材质实例创建失败: " + relPath);

                editor.ContentDatabase.Rebuild(true);

                var instance = Content.Load<MaterialInstance>(relPath, 10000.0);
                if (instance != null && instance.IsLoaded)
                {
                    instance.BaseMaterial = baseMaterial;
                    instance.Save(relPath);
                }

                return new { status = "created", path = relPath, baseMaterial = basePath };
            });
        }
    }
}
