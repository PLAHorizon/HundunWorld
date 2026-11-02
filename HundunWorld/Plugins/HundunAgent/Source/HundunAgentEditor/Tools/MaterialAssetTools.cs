using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEngine;
using FlaxEditor;
using FlaxEditor.Content;
using FlaxEditor.Scripting;
using FlaxEditor.Surface;
using FlaxEditor.Surface.Elements;
using FlaxEditor.Windows.Assets;
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
                Description = "把外部文件导入为项目资产（贴图/模型/音频等），走编辑器内容导入管线。sourcePath 为本机绝对路径，targetDir 为项目内目标目录（默认 Content）。"
                    + "options 传 ModelTool.Options 的字段名→值（字符串即可，枚举按名解析），例如 {\"Type\":\"SkinnedModel\",\"ImportBlendShapes\":true,\"MergeMeshes\":false}。"
                    + "不传 options 时沿用引擎默认（Type=Model ⇒ 引擎压根不解析骨骼，蒙皮与 morph 全丢；blendshape 还要 ImportBlendShapes=true 才进）。"
                    + "waitMs 是等导入队列排空的预算（默认 180000；几百 MB 的带 morph 的 glb 要给到 600000 以上）。"
                    + "回执里的 optionsReadback 是把改完的设置读回来的实测值，applied/unknownOptions 点名每一格的落点，别把「没报错」当成「形状进来了」。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"sourcePath\":{\"type\":\"string\"},\"targetDir\":{\"type\":\"string\"},\"options\":{\"type\":\"object\"},\"waitMs\":{\"type\":\"integer\"}},\"required\":[\"sourcePath\"]}",
                Execute = AssetImportAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "material_graph_add_params",
                Description = "给一张材质资产**补命名参数**（材质图参数）。按 path 打开材质窗口，逐个添加尚不存在的参数，然后保存。"
                    + "params 是 [{name,type}]，type 支持 Texture/float/Bool/Int/Vector2/Vector3/Vector4/Color（缺省 Texture）。"
                    + "已存在的参数**跳过不覆盖**。判据：别看我自己的回执，用 Tools/MhHeadRosterProbe.py 对同一张材质另测一遍名录"
                    + "（回执里的 rosterReadback 只证明我写进了手里那个 surface，不证明它落到了资产上）。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"params\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"},\"type\":{\"type\":\"string\"},\"value\":{}},\"required\":[\"name\"]}},\"removeParams\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},\"apply\":{\"type\":\"boolean\"}},\"required\":[\"path\",\"params\"]}",
                Execute = MaterialGraphAddParamsAsync
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

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "material_set_info",
                Description = "读/写材质资产的 MaterialInfo（混合模式/着色模型/蒙版阈值）。省略写入参数=只读；写时给 blendMode/shadingModel 且 apply=true。"
                    + "写盘走 Material.SaveSurface(LoadSurface(true), info)（与材质窗口保存同一条引擎 API），保存后 Reload 并回读复核。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{"
                    + "\"path\":{\"type\":\"string\",\"description\":\"材质资产路径\"},"
                    + "\"blendMode\":{\"type\":\"string\",\"description\":\"Opaque/Transparent/Masked/Add/Modulate/AlphaComposite\"},"
                    + "\"shadingModel\":{\"type\":\"string\",\"description\":\"Unlit/Lit/Subsurface/Foliage/Particle/...\"},"
                    + "\"cullMode\":{\"type\":\"string\",\"description\":\"Normal/Inverted/TwoSided\"},"
                    + "\"apply\":{\"type\":\"boolean\",\"description\":\"true 才写入\"}},"
                    + "\"required\":[\"path\"]}",
                Execute = MaterialSetInfoAsync
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

            var optionsArg = args.TryGetProperty("options", out JsonElement optsEl) && optsEl.ValueKind == JsonValueKind.Object
                ? optsEl
                : (JsonElement?)null;

            // 这里刻意用反射而不是 `new ModelTool.Options { ... }`：
            // 引擎头文件 ModelTool.h 里的默认值不是零值（Scale=1、OptimizeMeshes/MergeMeshes/
            // ImportLODs/ImportVertexColors=true、SmoothingNormalsAngle=175、Type=Model、
            // ImportBlendShapes=false），托管侧半填结构体会把其余四十多格一起清成零值 ——
            // 等于把模型压成 0 缩放。所以先取引擎自己的默认设置对象（ModelTool.Options.Default），
            // 只在其上覆盖调用方点名的字段，再把改完的值读回来进回执（声明≠落地，判据要问"值到了吗"）。
            object settings = null;
            var settingsNote = new List<string>();
            if (optionsArg != null)
            {
                settings = BuildImportOptions(optionsArg.Value, settingsNote);
            }

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

                if (settings != null)
                {
                    editor.ContentImporting.Import(sourcePath, folder, true, settings);
                    return;
                }

                editor.ContentImporting.Import(sourcePath, folder, true);
            });

            // 等待导入队列完成。大文件（几百 MB 的 glb 带 800+ morph）远超默认预算时
            // 由调用方抬高 waitMs；拿不到结论就明确回 status=importing，不假装成功。
            var waitMs = EditorUtils.GetInt(args, "waitMs", 180000);
            var done = await MainThread.WaitUntilAsync(
                () => !FlaxEditor.Editor.Instance.ContentImporting.IsImporting, waitMs);

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

            // 形状数走反射读：SkinnedModel.BlendShapes 的元素类型与成员名不该在我这侧钉死，
            // 猜错只是少报一个数，不该让整条导入路径失败。
            var shapeReport = await MainThread.InvokeAsync(() =>
            {
                var expectedPath = Path.Combine(Globals.ProjectFolder, targetDir,
                    Path.GetFileNameWithoutExtension(sourcePath) + ".flax");
                var model = Content.Load<SkinnedModel>(expectedPath);
                if (model == null)
                    return (object)new { skinnedModel = false, expectedPath };

                var names = new List<string>();
                int count = 0;
                int unnamed = 0;
                var shapeTypes = new HashSet<string>();
                object shapes = model.GetType().GetProperty("BlendShapes")?.GetValue(model);
                if (shapes is System.Collections.IEnumerable enumerable)
                {
                    foreach (var shape in enumerable)
                    {
                        count++;
                        if (shape != null)
                        {
                            shapeTypes.Add(shape.GetType().FullName);
                        }

                        string shapeName = ReadShapeName(shape);
                        if (string.IsNullOrEmpty(shapeName))
                        {
                            unnamed++;
                            shapeName = "<空名>";
                        }

                        if (names.Count < 40)
                        {
                            names.Add(shapeName);
                        }
                    }
                }

                // 骨骼/节点数量也要量：只数 morph 会漏掉"骨骼整条没解析"这一族失败
                //（引擎按 Type 推导 ImportTypes，Type=Model 时根本不含 Skeleton）。
                int nodeCount = CountCollection(model, "Nodes");
                int boneCount = CountCollection(model, "Bones");

                return (object)new
                {
                    skinnedModel = true,
                    path = expectedPath,
                    blendShapeCount = count,
                    unnamedShapeCount = unnamed,
                    shapeElementType = string.Join(",", shapeTypes),
                    blendShapeNames = names,
                    nodeCount = nodeCount,
                    boneCount = boneCount
                };
            });

            return new
            {
                status = "imported",
                source = sourcePath,
                targetDir,
                foundAssets = imported,
                shapes = shapeReport,
                optionsApplied = settings != null,
                importOptions = settingsNote
            };
        }

        /// <summary>形状名可能是属性/字段，也可能元素本身就是名字（SkinnedModel.BlendShapes 实测是 string[]）。</summary>
        private static string ReadShapeName(object shape)
        {
            if (shape == null)
            {
                return null;
            }

            // 实测教训：BlendShapes 枚举出来的元素是 System.String，
            // 对它取 .Name 永远拿不到东西 —— "读不到"会被误报成"名字是空的"。
            if (shape is string direct)
            {
                return direct;
            }

            var type = shape.GetType();
            var prop = type.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null)
            {
                return prop.GetValue(shape) as string;
            }

            var field = type.GetField("Name", BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
            {
                return field.GetValue(shape) as string;
            }

            return null;
        }

        private static int CountCollection(object asset, string memberName)
        {
            if (asset == null)
            {
                return -1;
            }

            object value = asset.GetType().GetProperty(memberName)?.GetValue(asset);
            if (value is System.Collections.ICollection collection)
            {
                return collection.Count;
            }

            return -1;
        }

        /// <summary>
        /// 在引擎默认导入设置之上覆盖调用方点名的字段。
        /// 全程反射：类型名与枚举成员名钉死在源码里，猜错就是整条导入路径编译不过，
        /// 而反射把它们变成回执里能看见的一次失败。
        /// </summary>
        private static object BuildImportOptions(JsonElement optionsEl, List<string> note)
        {
            // 贴图与模型的导入设置类都叫 Options、也都有一格 Type，但枚举类型与默认值完全不同
            // （模型 Type 是 Model/SkinnedModel，贴图 Type 是 Default/Normal/Mask…），
            // 靠猜会把值写进错的那一套。所以让调用方显式点名 Importer，缺省仍是模型（老行为不变）。
            string importer = null;
            foreach (JsonProperty kv in optionsEl.EnumerateObject())
            {
                if (kv.NameEquals("Importer"))
                {
                    importer = kv.Value.GetString();
                }
            }

            Type optionsType = FindImportOptionsType(importer);
            if (optionsType == null)
            {
                note.Add("找不到 " + (importer ?? "Model") + "Tool.Options 类型，本次不覆盖任何设置（管线会用引擎默认）");
                return null;
            }

            object options = null;
            PropertyInfo defaultProp = optionsType.GetProperty("Default", BindingFlags.Public | BindingFlags.Static);
            if (defaultProp != null)
            {
                options = defaultProp.GetValue(null);
                note.Add("默认设置取自 " + optionsType.FullName + ".Default");
            }
            else
            {
                options = Activator.CreateInstance(optionsType);
                note.Add("没有 " + optionsType.FullName + ".Default，退回无参构造：其余字段将是零值（Scale 可能被压成 0），谨慎判读");
            }

            foreach (JsonProperty kv in optionsEl.EnumerateObject())
            {
                // Importer 是"用哪套设置"的选择器，不是设置项本身，不能当成未知字段报出去。
                if (kv.NameEquals("Importer"))
                {
                    continue;
                }

                PropertyInfo prop = optionsType.GetProperty(kv.Name, BindingFlags.Public | BindingFlags.Instance);
                Type memberType = prop != null ? prop.PropertyType : null;
                FieldInfo field = null;
                if (prop == null)
                {
                    field = optionsType.GetField(kv.Name, BindingFlags.Public | BindingFlags.Instance);
                    memberType = field != null ? field.FieldType : null;
                }

                if (memberType == null)
                {
                    note.Add("未知设置项: " + kv.Name + "（该字段在 Options 上不存在，值被丢弃）");
                    continue;
                }

                if (!TryConvert(kv.Value, memberType, out object converted))
                {
                    note.Add("无法转换 " + kv.Name + " = " + kv.Value.ToString() + " → " + memberType.Name + "（未写入）");
                    continue;
                }

                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(options, converted);
                }
                else if (field != null)
                {
                    field.SetValue(options, converted);
                }
                else
                {
                    note.Add(kv.Name + " 只读，写不进去（未生效）");
                    continue;
                }

                // 写完立刻读回：报告"设了 X"之前先证明 X 真在对象上
                object readback = prop != null ? prop.GetValue(options) : field.GetValue(options);
                note.Add(kv.Name + " → " + memberType.Name + " = " + readback);
            }

            return options;
        }

        private static Type FindImportOptionsType(string importer)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    string wantTool = string.IsNullOrEmpty(importer) ? "ModelTool" : importer + "Tool";
                    if (type.Name == "Options" && type.DeclaringType != null && type.DeclaringType.Name == wantTool)
                    {
                        return type;
                    }
                }
            }

            return null;
        }

        private static bool TryConvert(JsonElement value, Type target, out object result)
        {
            result = null;
            Type underlying = Nullable.GetUnderlyingType(target) ?? target;

            try
            {
                if (underlying.IsEnum)
                {
                    string name = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
                    result = Enum.Parse(underlying, name, true);
                    return true;
                }

                if (underlying == typeof(bool))
                {
                    if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                    {
                        result = value.GetBoolean();
                        return true;
                    }

                    result = bool.Parse(value.GetString());
                    return true;
                }

                if (underlying == typeof(string))
                {
                    result = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
                    return true;
                }

                string raw = value.ValueKind == JsonValueKind.Number ? value.GetRawText() : value.ToString();
                result = Convert.ChangeType(raw, underlying, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
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

        /// <summary>要补的参数：名字 + 类型名 + 可选的逐参数默认值。</summary>
        private sealed class WantedParam
        {
            public string Name;
            public string TypeName;
            public JsonElement? Default;
        }

        /// <summary>
        /// 给材质资产补命名参数：打开材质窗口 → 逐个添加尚不存在的参数 → 保存。
        ///
        /// 走的是**编辑器自己的正规做法**（抄自它自己的 undo 动作类
        /// `VisjectSurfaceWindow.Add()`）：构造 `SurfaceParameter` → `Parameters.Insert/Add` →
        /// `OnParamCreated(param)` 通知依赖参数的节点，最后 `window.Save()` 落盘。
        ///
        /// 两条刻意的边界：
        /// 1) **已存在的参数一律跳过**，不覆盖——覆盖会改掉别人手工调好的默认值；
        /// 2) 回执里的 `rosterReadback` 只证明"参数写进了手里这个 surface"，
        ///    不证明它落到了资产上（窗口复用/绑定这类事我控制不了）。
        ///    真正的判据是**独立探针**：`Tools/MhHeadRosterProbe.py` 对同一张材质另测一遍名录。
        /// </summary>
        private static async Task<object> MaterialGraphAddParamsAsync(JsonElement args)
        {
            var pathArg = EditorUtils.GetString(args, "path");
            if (string.IsNullOrEmpty(pathArg))
            {
                throw new ArgumentException("缺少 path 参数");
            }

            JsonElement paramsEl;
            if (!args.TryGetProperty("params", out paramsEl) || paramsEl.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException("缺少 params 数组（[{name,type}]）");
            }

            // 每一项是 {name, type, value?}。**value 是可选的逐参数默认值**：
            // 少了它，`NormalIntensity=1` / `MinRoughness=0.2` 这类"默认不为 0"的系数会全变成 0，
            // 脸会被压平 —— 而回执照样说"加成功"（这类静默失效本项目栽过多次）。
            var wanted = new List<WantedParam>();
            foreach (JsonElement p in paramsEl.EnumerateArray())
            {
                JsonElement nv;
                if (!p.TryGetProperty("name", out nv))
                {
                    continue;
                }

                string name = nv.GetString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                string typeName = "Texture";
                JsonElement tv;
                if (p.TryGetProperty("type", out tv) && !string.IsNullOrWhiteSpace(tv.GetString()))
                {
                    typeName = tv.GetString();
                }

                var wp = new WantedParam { Name = name, TypeName = typeName };
                JsonElement vv;
                if (p.TryGetProperty("value", out vv))
                {
                    wp.Default = vv;
                }

                wanted.Add(wp);
            }

            // 可选的删除清单：连同承载节点一起删，避免留下引用已删参数的孤儿节点。
            var removeNames = new List<string>();
            JsonElement rmEl;
            if (args.TryGetProperty("removeParams", out rmEl) && rmEl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement r in rmEl.EnumerateArray())
                {
                    if (r.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(r.GetString()))
                        removeNames.Add(r.GetString());
                }
            }
            var removed = new List<string>();

            if (wanted.Count == 0 && removeNames.Count == 0)
            {
                throw new ArgumentException("params 里没有有效条目");
            }

            // 默认**只算不写**：`apply` 缺省 false。理由是一次实测教训——
            // 2026-10-15 我在没验证"surface 真把资产图加载进来了"之前就 Save，
            // 把一张 29KB 的材质覆盖成了 2.3KB 的近似空图（那份资产未入库、没有备份）。
            // 现在写盘必须显式点名 apply=true，且回执里把"surface 侧 vs 资产侧"的读数并排给出来。
            // 这里必须**在下面几个 lambda 之前**解析：lambda 里要用它来决定"改不改图"。
            bool apply = false;
            JsonElement applyEl;
            if (args.TryGetProperty("apply", out applyEl) && applyEl.ValueKind == JsonValueKind.True)
            {
                apply = true;
            }

            // 默认**不覆盖已存在参数的值**（怕改掉别人手工调好的）。但"按清单校准"是正当需求：
            // 2026-10-16 实测过一次——43 条系数被上一版工具以默认值 0 加了进去
            // （NormalIntensity=0 / MinRoughness=0 ⇒ 脸会被压平），而回执照样说"加成功"。
            // 所以给一个显式开关：`overwriteValues=true` 时，把清单里的默认值写进已存在的同名参数。
            bool overwriteValues = args.TryGetProperty("overwriteValues", out var owEl)
                                   && owEl.ValueKind == JsonValueKind.True;

            // 分三步，每步一个主线程 tick。为什么不能一口气做完：
            // 窗口的 surface 内容是在它自己的 Update() 里灌进去的
            // （VisjectSurfaceWindow.LoadSurface → OnSurfaceEditingStart 才把 Surface.Enabled 置 true）。
            // 同一个 tick 里"开窗 + 写参数 + 保存"，写到的是**还没加载的空图**——
            // 回执会显示 added=18，但资产纹丝不动（2026-10-15 实测：探针仍 0/18、mtime 不变）。
            MaterialWindow window = null;
            string normalizedPath = null;
            int addedCount = 0;
            int skippedCount = 0;
            int removedCount = 0;
            var added = new List<string>();
            var skipped = new List<string>();
            var updated = new List<string>();

            // ① 开窗（同时把资产预加载一次，缩短 surface 就绪的等待）
            await MainThread.InvokeAsync(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                normalizedPath = pathArg.Replace('\\', '/');
                var assetItem = editor.ContentDatabase.Find(pathArg) as AssetItem
                                ?? editor.ContentDatabase.Find(normalizedPath) as AssetItem;
                if (assetItem == null)
                {
                    // 本会话实测：按路径查内容库整体失效（连老资产也查不到），但按 Guid 查是好的
                    // ⇒ 先用引擎自己的加载器拿到资产 ID，再反查 AssetItem。
                    var loaded = Content.Load<MaterialBase>(normalizedPath, 10000.0);
                    if (loaded == null || !loaded.IsLoaded)
                    {
                        throw new ArgumentException("材质加载失败: " + pathArg);
                    }

                    assetItem = editor.ContentDatabase.Find(loaded.ID) as AssetItem;
                    if (assetItem == null)
                    {
                        editor.ContentDatabase.Rebuild(true);
                        assetItem = editor.ContentDatabase.Find(loaded.ID) as AssetItem;
                    }
                }

                if (assetItem == null)
                {
                    throw new ArgumentException("找不到该材质的 AssetItem（路径与 Guid 两条都试过）: " + pathArg);
                }

                Content.Load<MaterialBase>(normalizedPath, 10000.0);
                window = new MaterialWindow(editor, assetItem);
                editor.Windows.Open(window);
            });

            // ② 等 surface 真的加载完：Surface.Enabled 由 OnSurfaceEditingStart 置真。
            //    等不到就**大声失败**——继续往下写只会得到一个"绿着但没落盘"的假成功。
            int waited = 0;
            while (waited < 240)
            {
                bool ready = await MainThread.InvokeAsync(() => window != null && window.Surface != null && window.Surface.Enabled);
                if (ready)
                {
                    break;
                }

                waited++;
            }

            bool surfaceReady = await MainThread.InvokeAsync(() => window != null && window.Surface != null && window.Surface.Enabled);
            if (!surfaceReady)
            {
                throw new InvalidOperationException("材质窗口的 surface 在 " + waited
                    + " 帧内没有就绪（Surface.Enabled 仍为 false）⇒ 不写参数、也不保存；请确认该材质能在编辑器里正常打开");
            }

            // ③ 写参数 + 保存（此时 surface 已绑定资产）
            await MainThread.InvokeAsync(() =>
            {
                var surface = window.Surface;
                foreach (var wp in wanted)
                {
                    var existing = surface.GetParameter(wp.Name);
                    if (existing != null)
                    {
                        skipped.Add(wp.Name);
                        // 校准已存在参数的值（只在显式 overwriteValues=true 且 apply=true 时）
                        if (apply && overwriteValues && wp.Default.HasValue)
                        {
                            existing.Value = ConvertMaterialParamValue(wp.Default.Value);
                            updated.Add(wp.Name);
                        }
                        continue;
                    }

                    ScriptType? type;
                    object value;
                    if (!TryResolveParameterType(wp.TypeName, out type, out value))
                    {
                        throw new ArgumentException("不支持的参数类型: " + wp.TypeName + "（参数 " + wp.Name + "）");
                    }

                    // 调用方给了默认值就以它为准（值来自 UE 那把尺，不是这里编的）；
                    // 类型由 wp.TypeName 决定，值按同一套转换（见 ConvertMaterialParamValue）。
                    if (wp.Default.HasValue)
                    {
                        value = ConvertMaterialParamValue(wp.Default.Value);
                    }

                    // **dry-run 一律不改图**：只校验类型并记账。
                    // 为什么必须这样：`Parameters.Add` / `OnParamCreated` 会把 surface 标脏，
                    // 编辑器随后会自己存盘 —— 2026-10-16 实测：一次 dry-run 加到第 44 条才因
                    // `Color` 类型不支持抛错，前 43 条已经躺在 surface 里，最终被写进了资产。
                    // 换句话说 dry-run 曾经**不是**"只算不写"。这里按"绝不落盘"来实现。
                    if (!apply)
                    {
                        added.Add(wp.Name);
                        continue;
                    }

                    var param = new SurfaceParameter
                    {
                        ID = Guid.NewGuid(),
                        IsPublic = true,
                        Name = wp.Name,
                        Type = type.Value,
                        Value = value,
                    };
                    surface.Parameters.Add(param);
                    surface.OnParamCreated(param);
                    // 参数表自身不标脏：没有承载节点（或承载节点建失败）时，后面的 window.Save()
                    // 会因 !IsEdited 空转，参数只活在本会话里、重开资产就消失（贴图参数实测如此）。
                    surface.RootContext.MarkAsModified(true);
                    added.Add(wp.Name);
                }

                    addedCount = added.Count;
                    skippedCount = skipped.Count;
                    removedCount = removed.Count;

                if (apply && removeNames.Count > 0)
                {
                    var carrierArch = FlaxEditor.Surface.Archetypes.Parameters.Nodes.Length > 0
                        ? FlaxEditor.Surface.Archetypes.Parameters.Nodes[0] : null;
                    foreach (var name in removeNames)
                    {
                        var sp = surface.GetParameter(name);
                        if (sp == null)
                        {
                            removed.Add(name + "(absent)");
                            continue;
                        }

                        if (carrierArch != null)
                        {
                            for (int i = surface.RootContext.Nodes.Count - 1; i >= 0; i--)
                            {
                                var n = surface.RootContext.Nodes[i];
                                if (n == null || n.Archetype != carrierArch || n.Values == null || n.Values.Length == 0)
                                    continue;
                                if (n.Values[0] is Guid g && g == sp.ID)
                                {
                                    n.RemoveConnections();
                                    surface.RootContext.Nodes.RemoveAt(i);
                                }
                            }
                        }

                        surface.Parameters.Remove(sp);
                        removed.Add(name);
                    }

                    if (removed.Count > 0)
                        surface.RootContext.MarkAsModified(true);
                    removedCount = removed.Count;
                }
            });

            // 默认顺手给参数放 Get Parameter 节点。**注意：这不是参数进运行时表的条件**——
            // 运行时材质参数表在材质编译时由图参数**全量**生成：
            // MaterialGenerator::prepareLayer 遍历 layer->Graph.Parameters 逐条 _parameters.AddOne()
            //（MaterialGenerator.Layer.cpp:170-190），Material::load 再写进 SHADER_FILE_CHUNK_MATERIAL_PARAMS
            //（Material.cpp:284-293）；游离（没被任何节点引用）的参数一样进表、一样带值。
            // 放节点的意义在于让参数在图里**被引用**：没人消费的参数虽在表里，却对该材质外观零影响。
            bool withNodes = true;
            JsonElement withNodesEl;
            if (args.TryGetProperty("withNodes", out withNodesEl) && withNodesEl.ValueKind == JsonValueKind.False)
            {
                withNodes = false;
            }

            var nodesSpawned = new List<string>();
            var wired = new List<string>();
            if (withNodes)
            {
                // 连线清单：把"参数 → 材质输入"接上。接线的作用是让参数**真的影响外观**
                //（不被任何材质输入消费的参数在图里是死代码，改了值也看不出来）。
                // 它**不是**"参数进运行时表"的前提——那件事由编译期的 generator 无条件完成，
                // 回执里的 runtimeAfterSave / missingFromRuntimeTable 就是这条的复核。
                var wiring = new List<KeyValuePair<string, string>>();
                JsonElement wiringEl;
                if (args.TryGetProperty("wiring", out wiringEl) && wiringEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement w in wiringEl.EnumerateArray())
                    {
                        JsonElement pv, iv;
                        if (w.TryGetProperty("param", out pv) && w.TryGetProperty("input", out iv))
                        {
                            string pn = pv.GetString();
                            string inn = iv.GetString();
                            if (!string.IsNullOrWhiteSpace(pn) && !string.IsNullOrWhiteSpace(inn))
                            {
                                wiring.Add(new KeyValuePair<string, string>(pn, inn));
                            }
                        }
                    }
                }

                await MainThread.InvokeAsync(() =>
                {
                    var surface = window.Surface;

                    // Parameters 组（NodeFactory.DefaultGroups 里按名字找）+ 材质图那一档的
                    // "Get Parameter" 原型（Archetypes.Parameters.Nodes[0]，TypeID=1，
                    // Flags = MaterialGraph | AnimGraph，DefaultValues[0] 就是参数 Guid）。
                    GroupArchetype paramGroup = null;
                    foreach (GroupArchetype g in NodeFactory.DefaultGroups)
                    {
                        if (g != null && g.Name == "Parameters")
                        {
                            paramGroup = g;
                            break;
                        }
                    }

                    if (paramGroup == null || FlaxEditor.Surface.Archetypes.Parameters.Nodes.Length == 0)
                    {
                        throw new InvalidOperationException("找不到 Parameters 组或它的节点原型，无法放承载节点");
                    }

                    var arch = FlaxEditor.Surface.Archetypes.Parameters.Nodes[0];
                    float x = 0f;
                    foreach (var wp in wanted)
                    {
                        var kv = new KeyValuePair<string, string>(wp.Name, wp.TypeName);
                        if (nodesSpawned.Contains(kv.Key))
                        {
                            continue;
                        }

                        var sp = surface.GetParameter(kv.Key);
                        if (sp == null)
                        {
                            continue;
                        }

                        // 幂等：该参数已有承载节点就不再放第二个。
                        // 少了这条，修好 Save 触发条件后会把**重复节点**一起落盘
                        //（第二次 apply 起每次多一份，图越调越乱）。
                        if (FindParameterNode(surface, arch, sp.ID) != null)
                        {
                            continue;
                        }

                        var node = surface.RootContext.SpawnNode(paramGroup, arch, new Float2(x, 0f), new object[] { sp.ID });
                        if (node != null)
                        {
                            nodesSpawned.Add(kv.Key);
                            x += 160f;
                        }
                    }

                    if (nodesSpawned.Count > 0)
                    {
                        surface.RootContext.MarkAsModified(true);
                    }

                    if (wiring.Count == 0)
                    {
                        return;
                    }

                    // 材质输出节点：Material 组里 TypeID==1 那个（NoRemove 的根节点）
                    GroupArchetype matGroup = null;
                    foreach (GroupArchetype g in NodeFactory.DefaultGroups)
                    {
                        if (g != null && g.Name == "Material")
                        {
                            matGroup = g;
                            break;
                        }
                    }

                    if (matGroup == null)
                    {
                        throw new InvalidOperationException("找不到 Material 组，无法连线");
                    }

                    NodeArchetype matArch = null;
                    foreach (NodeArchetype a in matGroup.Archetypes)
                    {
                        if (a != null && a.TypeID == 1)
                        {
                            matArch = a;
                            break;
                        }
                    }

                    if (matArch == null)
                    {
                        throw new InvalidOperationException("找不到材质输出节点原型（TypeID=1）");
                    }

                    SurfaceNode root = null;
                    foreach (SurfaceNode n in surface.RootContext.Nodes)
                    {
                        if (n != null && n.Archetype == matArch)
                        {
                            root = n;
                            break;
                        }
                    }

                    if (root == null)
                    {
                        throw new InvalidOperationException("图里没有材质输出节点，无法连线");
                    }

                    foreach (var w in wiring)
                    {
                        int inputId;
                        if (!TryMapMaterialInput(w.Value, out inputId))
                        {
                            throw new ArgumentException("不认识的材质输入名: " + w.Value);
                        }

                        var sp = surface.GetParameter(w.Key);
                        if (sp == null)
                        {
                            continue;
                        }

                        SurfaceNode paramNode = FindParameterNode(surface, arch, sp.ID);
                        if (paramNode == null)
                        {
                            throw new InvalidOperationException("找不到参数 " + w.Key + " 的 Get Parameter 节点（先 withNodes=true 放好节点）");
                        }

                        // 贴图参数不能直连数值输入（Flax 会按类型拒绝）：中间补一个 Sample Texture
                        // （Textures 组 TypeID=9：输入 Texture=pin0，输出 Color=pin4/Float4）。
                        Box src = paramNode.GetBox(0);
                        bool srcIsVector4 = false;
                        if (sp.Type.Type == typeof(Texture))
                        {
                            GroupArchetype texGroup = null;
                            foreach (GroupArchetype g in NodeFactory.DefaultGroups)
                            {
                                if (g != null && g.Name == "Textures")
                                {
                                    texGroup = g;
                                    break;
                                }
                            }

                            NodeArchetype sampleArch = null;
                            if (texGroup != null)
                            {
                                foreach (NodeArchetype a in texGroup.Archetypes)
                                {
                                    if (a != null && a.TypeID == 9)
                                    {
                                        sampleArch = a;
                                        break;
                                    }
                                }
                            }

                            if (texGroup == null || sampleArch == null)
                            {
                                throw new InvalidOperationException("找不到 Textures 组或 Sample Texture 原型（TypeID=9）");
                            }

                            var sampler = surface.RootContext.SpawnNode(texGroup, sampleArch, new Float2(x, 220f), null);
                            if (sampler == null)
                            {
                                throw new InvalidOperationException("Sample Texture 节点创建失败");
                            }

                            x += 160f;
                            var texIn = sampler.GetBox(0);
                            if (src == null || texIn == null)
                            {
                                throw new InvalidOperationException("取不到贴图采样节点的 pin");
                            }

                            texIn.Connections.Remove(src);
                            src.Connections.Remove(texIn);
                            texIn.Connections.Add(src);
                            src.Connections.Add(texIn);
                            src.OnConnectionsChanged();
                            src.ConnectionTick();
                            texIn.OnConnectionsChanged();
                            texIn.ConnectionTick();

                            src = sampler.GetBox(4);
                            srcIsVector4 = true;
                        }

                        var dst = root.GetBox(inputId);
                        if (src == null || dst == null)
                        {
                            throw new InvalidOperationException("取不到 pin：src=" + (src != null) + " dst=" + (dst != null)
                                + "（input=" + w.Value + " id=" + inputId + "）");
                        }

                        // 标量输入吃不下 Float4：中间再补一个 Unpack Float4
                        // （Packing 组 TypeID=32：输入 Value=pin0/Float4，输出 X=pin1、Y=2、Z=3、W=4）。
                        // 取 X 分量接过去（掩码/粗糙度这类单通道贴图就是靠这个）。
                        if (srcIsVector4 && IsScalarMaterialInput(inputId))
                        {
                            GroupArchetype packGroup = null;
                            foreach (GroupArchetype g in NodeFactory.DefaultGroups)
                            {
                                if (g != null && g.Name == "Packing")
                                {
                                    packGroup = g;
                                    break;
                                }
                            }

                            NodeArchetype unpackArch = null;
                            if (packGroup != null)
                            {
                                foreach (NodeArchetype a in packGroup.Archetypes)
                                {
                                    if (a != null && a.TypeID == 32)
                                    {
                                        unpackArch = a;
                                        break;
                                    }
                                }
                            }

                            if (packGroup == null || unpackArch == null)
                            {
                                throw new InvalidOperationException("找不到 Packing 组或 Unpack Float4 原型（TypeID=32）");
                            }

                            var unpack = surface.RootContext.SpawnNode(packGroup, unpackArch, new Float2(x, 380f), null);
                            if (unpack == null)
                            {
                                throw new InvalidOperationException("Unpack Float4 节点创建失败");
                            }

                            x += 160f;
                            var unpackIn = unpack.GetBox(0);
                            if (unpackIn == null || src == null)
                            {
                                throw new InvalidOperationException("取不到 Unpack 的输入 pin");
                            }

                            unpackIn.Connections.Remove(src);
                            src.Connections.Remove(unpackIn);
                            unpackIn.Connections.Add(src);
                            src.Connections.Add(unpackIn);
                            src.OnConnectionsChanged();
                            src.ConnectionTick();
                            unpackIn.OnConnectionsChanged();
                            unpackIn.ConnectionTick();

                            src = unpack.GetBox(1);   // X 分量
                        }

                        // 一个输入只吃一个来源：先摘掉它现有的连接（两边都要摘），再接。
                        for (int i = dst.Connections.Count - 1; i >= 0; i--)
                        {
                            var other = dst.Connections[i];
                            other.Connections.Remove(dst);
                            other.OnConnectionsChanged();
                            other.ConnectionTick();
                        }

                        dst.Connections.Add(src);
                        src.Connections.Add(dst);
                        src.OnConnectionsChanged();
                        src.ConnectionTick();
                        dst.OnConnectionsChanged();
                        dst.ConnectionTick();
                        wired.Add(w.Key + "→" + w.Value);
                    }

                    if (wired.Count > 0)
                    {
                        surface.RootContext.MarkAsModified(true);
                    }
                });
            }

            var roster = new List<string>();
            int assetParamCount = -1;
            await MainThread.InvokeAsync(() =>
            {
                foreach (SurfaceParameter p in window.Surface.Parameters)
                {
                    roster.Add(p.Name);
                }

                // 资产侧自己认多少参数（独立于 surface）——两边并排看才知道图加载成功没
                var loadedAsset = Content.Load<MaterialBase>(normalizedPath, 10000.0);
                if (loadedAsset != null && loadedAsset.IsLoaded && loadedAsset.Parameters != null)
                {
                    assetParamCount = loadedAsset.Parameters.Length;
                }
            });

            // 只要有实质改动就落盘。原来的条件是 `addedCount > 0 && apply`，
            // 于是"只建节点 / 只连线"这类没有新增参数的调用**不会保存**（文件 mtime 不变），
            // 用户看到的"改了没反应"就是这个。
            bool didWrite = apply && (addedCount > 0 || updated.Count > 0 || removedCount > 0
                                      || nodesSpawned.Count > 0 || wired.Count > 0);
            if (didWrite)
            {
                await MainThread.InvokeAsync(() =>
                {
                    // **必须先把 surface 标脏**：`VisjectSurfaceWindow.Save()` 第一句是
                    // `if (!IsEdited) return;` —— 只改参数**值**（不新增参数/节点）不会置编辑标记，
                    // 于是 Save() 直接空转：回执说 didWrite=true，文件却一动不动（2026-10-16 实测）。
                    window.Surface.RootContext.MarkAsModified(true);
                    window.Save();
                });
            }

            // 保存后复核**运行时材质参数表**——这就是"图参数同时写进了材质参数表"的判据。
            // 表由编译期的 MaterialGenerator 从图参数派生（见上面两处注释的出处），
            // 所以新增的名字若不在表里，只可能是"没编译 / 没落盘"。
            // 注意这里读的是刚被 window.Save() 里 Reload 过的那个资产对象，故反映磁盘状态。
            var runtimeAfterSave = new List<string>();
            var missingFromRuntimeTable = new List<string>();
            await MainThread.InvokeAsync(() =>
            {
                var loadedAsset = Content.Load<MaterialBase>(normalizedPath, 10000.0);
                if (loadedAsset != null && loadedAsset.IsLoaded && loadedAsset.Parameters != null)
                {
                    foreach (MaterialParameter mp in loadedAsset.Parameters)
                    {
                        runtimeAfterSave.Add(mp.Name);
                    }
                }

                foreach (var wp in wanted)
                {
                    if (!runtimeAfterSave.Contains(wp.Name))
                    {
                        missingFromRuntimeTable.Add(wp.Name);
                    }
                }
            });

            return new
            {
                status = apply ? (didWrite ? "ok" : "ok/no-op") : "dry-run",
                path = normalizedPath,
                applied = apply,
                didWrite,
                addedCount,
                added,
                updatedCount = updated.Count,
                updated,
                skippedCount,
                skipped,
                removedCount,
                removed,
                surfaceReadyAfterFrames = waited,
                surfaceParamCount = roster.Count,
                assetParamCount,
                nodesSpawnedCount = nodesSpawned.Count,
                nodesSpawned,
                wiredCount = wired.Count,
                wired,
                rosterReadback = roster,
                runtimeParamCount = runtimeAfterSave.Count,
                runtimeAfterSave,
                missingFromRuntimeTable,
                runtimeTableHasAllRequestedParams = missingFromRuntimeTable.Count == 0,
                note = !apply
                    ? "dry-run：没有写盘。surfaceParamCount / assetParamCount 是双侧当前读数，"
                      + "runtimeAfterSave 是现有运行时参数表（此时与磁盘同源）。"
                    : (didWrite
                        ? "已调 window.Save() 落盘，并复核了运行时材质参数表（runtimeAfterSave）。"
                          + "missingFromRuntimeTable 非空即该参数没进表，按缺陷查。"
                          + "仍可用 Tools/MhHeadRosterProbe.py 独立复测一遍名录。"
                        : "apply=true 但没有实质改动（无新增参数 / 节点 / 连线），未触发保存。"),
            };
        }

        /// <summary>
        /// 在图上找承载指定参数 ID 的 Get Parameter 节点（Parameters 组 TypeID=1，`Values[0]` 是参数 Guid）。
        /// 放节点与连线两处都靠它判"是否已存在"，保证重复调用不会攒出重复节点。
        /// </summary>
        private static SurfaceNode FindParameterNode(VisjectSurface surface, NodeArchetype arch, Guid paramId)
        {
            foreach (SurfaceNode n in surface.RootContext.Nodes)
            {
                if (n == null || n.Archetype != arch || n.Values == null || n.Values.Length == 0)
                {
                    continue;
                }

                if (n.Values[0] is Guid g && g == paramId)
                {
                    return n;
                }
            }

            return null;
        }

        /// <summary>
        /// 材质输出节点（Material 组 TypeID=1）的输入名 → pin id。
        /// 这张表是照 `Surface/Archetypes/Material.cs` 里那段 `Elements` 抄的，不是猜的：
        /// Color=1, Mask=2, Emissive=3, Metalness=4, Specular=5, Roughness=6,
        /// AmbientOcclusion=7, Normal=8, Opacity=9, Refraction=10, PositionOffset=11,
        /// Tessellation=12, WorldDisplacement=13, Subsurface=14。
        /// </summary>
        private static bool TryMapMaterialInput(string name, out int id)
        {
            id = -1;
            switch ((name ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", string.Empty))
            {
                case "color": case "basecolor": case "albedo": id = 1; return true;
                case "mask": id = 2; return true;
                case "emissive": case "emission": id = 3; return true;
                case "metalness": case "metallic": id = 4; return true;
                case "specular": id = 5; return true;
                case "roughness": id = 6; return true;
                case "ao": case "ambientocclusion": id = 7; return true;
                case "normal": id = 8; return true;
                case "opacity": case "alpha": id = 9; return true;
                case "refraction": id = 10; return true;
                case "positionoffset": id = 11; return true;
                case "tessellation": case "tessellationmultiplier": id = 12; return true;
                case "worlddisplacement": case "displacement": id = 13; return true;
                case "subsurface": case "subsurfacecolor": id = 14; return true;
                default: return false;
            }
        }

        /// <summary>
        /// 该材质输入是不是标量（float）。同样照 `Archetypes/Material.cs` 的 Elements 判：
        /// 2 Mask、4 Metalness、5 Specular、6 Roughness、7 AO、9 Opacity、10 Refraction、12 Tessellation 是 float；
        /// 1 Color、3 Emissive、8 Normal、11 PositionOffset、13 WorldDisplacement、14 Subsurface 是 Float3。
        /// 用途：贴图采样输出是 Float4，接标量输入前必须有"取分量"节点——本轮没实现，所以就这条要说清楚。
        /// </summary>
        private static bool IsScalarMaterialInput(int id)
        {
            return id == 2 || id == 4 || id == 5 || id == 6 || id == 7 || id == 9 || id == 10 || id == 12;
        }

        /// <summary>
        /// 参数类型名 → ScriptType 与默认值。默认值自己按类型给（不去引 TypeUtils，那里的类名我没核实）。
        /// 只支持材质通道真正会用到的那几种；其它一律抛错，不静默降级。
        /// </summary>
        /// <remarks>做法抄自编辑器自己的 VisjectSurfaceWindow.Add()；改这里之前先看那段。</remarks>
        private static bool TryResolveParameterType(string typeName, out ScriptType? type, out object value)
        {
            type = null;
            value = null;
            switch ((typeName ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "texture":
                    type = new ScriptType(typeof(Texture));
                    value = null;
                    return true;
                case "float":
                    type = new ScriptType(typeof(float));
                    value = 0.0f;
                    return true;
                case "bool":
                    type = new ScriptType(typeof(bool));
                    value = false;
                    return true;
                case "int":
                    type = new ScriptType(typeof(int));
                    value = 0;
                    return true;
                case "vector2":
                    type = new ScriptType(typeof(Vector2));
                    value = Vector2.Zero;
                    return true;
                case "vector3":
                    type = new ScriptType(typeof(Float3));
                    value = Float3.Zero;
                    return true;
                case "vector4":
                    type = new ScriptType(typeof(Float4));
                    value = Float4.Zero;
                    return true;
                case "color":
                    // UE 的妆色是 LinearColor ⇒ Flax 侧最贴近的是 Color（Get Parameter 上给的是 Color/R/G/B/A 五个出口）
                    type = new ScriptType(typeof(Color));
                    value = Color.White;
                    return true;
                default:
                    return false;
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

        /// <summary>
        /// 读/写材质资产的 MaterialInfo。
        /// 为什么单独一条：材质图工具只碰图，混合模式藏在资产头信息里；
        /// 身体材质被写成 Transparent 后，无论图里怎么接不透明度都只能得到"半透明的人"。
        /// 写盘用 Material.SaveSurface(data, info)——材质窗口的 SurfaceData setter 走的就是这条。
        /// </summary>
        private static async Task<object> MaterialSetInfoAsync(JsonElement args)
        {
            var pathArg = EditorUtils.GetString(args, "path");
            if (string.IsNullOrEmpty(pathArg))
                throw new ArgumentException("缺少 path 参数");

            bool apply = args.TryGetProperty("apply", out var applyEl) && applyEl.ValueKind == JsonValueKind.True;
            string blendArg = EditorUtils.GetString(args, "blendMode");
            string shadingArg = EditorUtils.GetString(args, "shadingModel");
            string cullArg = EditorUtils.GetString(args, "cullMode");

            return await MainThread.InvokeAsync<object>(() =>
            {
                var normalized = pathArg.Replace('\\', '/');
                var mat = Content.Load<Material>(normalized, 10000.0);
                if (mat == null || !mat.IsLoaded)
                    throw new ArgumentException("材质加载失败: " + pathArg);

                var infoBefore = mat.Info;
                var before = DescribeMaterialInfo(infoBefore);

                if (!apply)
                {
                    return new
                    {
                        status = "read",
                        path = normalized,
                        before,
                        after = before,
                        apply,
                        infoProbe = "material_set_info/v1"
                    };
                }

                var info = infoBefore;
                if (!string.IsNullOrEmpty(blendArg))
                {
                    if (!Enum.TryParse<MaterialBlendMode>(blendArg, true, out var blendMode))
                        throw new ArgumentException("未知 blendMode: " + blendArg);
                    info.BlendMode = blendMode;
                }
                if (!string.IsNullOrEmpty(shadingArg))
                {
                    if (!Enum.TryParse<MaterialShadingModel>(shadingArg, true, out var shadingModel))
                        throw new ArgumentException("未知 shadingModel: " + shadingArg);
                    info.ShadingModel = shadingModel;
                }
                if (!string.IsNullOrEmpty(cullArg))
                {
                    if (!Enum.TryParse<CullMode>(cullArg, true, out var cullMode))
                        throw new ArgumentException("未知 cullMode: " + cullArg);
                    info.CullMode = cullMode;
                }

                var surface = mat.LoadSurface(true);
                if (surface == null || surface.Length == 0)
                    throw new InvalidOperationException("LoadSurface 返回空图，拒绝写入（继续写会把材质图清空）");

                if (mat.SaveSurface(surface, info))
                    throw new InvalidOperationException("SaveSurface 失败（引擎返回 true）");

                mat.Reload();
                mat.WaitForLoaded();
                var reloaded = Content.Load<Material>(normalized, 10000.0);
                var afterInfo = reloaded != null && reloaded.IsLoaded ? reloaded.Info : mat.Info;
                var after = DescribeMaterialInfo(afterInfo);

                return new
                {
                    status = "ok",
                    path = normalized,
                    before,
                    after,
                    apply,
                    surfaceBytes = surface.Length,
                    blendModeMatched = afterInfo.BlendMode == info.BlendMode,
                    shadingModelMatched = afterInfo.ShadingModel == info.ShadingModel,
                    infoProbe = "material_set_info/v1"
                };
            });
        }

        private static object DescribeMaterialInfo(MaterialInfo info)
        {
            return new
            {
                blendMode = info.BlendMode.ToString(),
                shadingModel = info.ShadingModel.ToString(),
                cullMode = info.CullMode.ToString(),
                domain = info.Domain.ToString(),
                maskThreshold = info.MaskThreshold,
                opacityThreshold = info.OpacityThreshold
            };
        }

        /// <summary>
        /// 写材质参数的**值**并落盘。
        ///
        /// 为什么不再走 `material.Save(path)`：实测它在**全新干净材质**上同样回 false，
        /// 且只回一句"保存失败"、没有原因；而同一张材质走**编辑器窗口**那条路（window.Save()）是通的
        /// —— MH_Head 的 18 个参数就是这样落上去的。所以统一改成已验证通路：
        /// 开窗 → 等 `Surface.Enabled`（VisjectSurfaceWindow.OnSurfaceEditingStart 才置真）
        /// → 改 `SurfaceParameter.Value` → `window.Save()`。
        ///
        /// 回执给 `valueBefore` / `valueAfter` 两格：连着调两次（第二次读到的 valueBefore
        /// 应当等于第一次写的值）就构成一次跨调用的往返判据，比"调用没报错"可信。
        /// </summary>
        private static async Task<object> MaterialSetParamAsync(JsonElement args)
        {
            var pathArg = EditorUtils.GetString(args, "path");
            var paramName = EditorUtils.GetString(args, "param");

            if (string.IsNullOrEmpty(pathArg) || string.IsNullOrEmpty(paramName))
                throw new ArgumentException("缺少 path 或 param");
            JsonElement valueEl;
            if (!args.TryGetProperty("value", out valueEl))
                throw new ArgumentException("缺少 value");

            var converted = ConvertMaterialParamValue(valueEl);
            MaterialWindow window = null;
            var normalized = pathArg.Replace('\\', '/');
            string valueBefore = null;
            string valueAfter = null;
            int waited = 0;

            // 材质**实例**没有图、也就没有可开的材质窗口：它只能走运行时 API。
            // （原来那份实现就是运行时路径，对实例是对的；改窗口路径时若不分流会把它弄坏。）
            var probe = Content.Load<MaterialBase>(normalized, 10000.0);
            if (probe != null && probe.IsLoaded && probe is MaterialInstance instance)
            {
                object runtimeBefore = null;
                object runtimeAfter = null;
                bool overrodeOnInstance = false;
                await MainThread.InvokeAsync(() =>
                {
                    // 注意：`MaterialInstance.GetParameter(name)` **只认实例自己的覆盖项**，不解析基材
                    // —— 实测连"基材有明确参数"的实例都回"没有该参数"（2026-10-15）。
                    // 所以是否存在要同时问实例与它的基材；写值用 SetParameterValue（它会落到实例的覆盖里）。
                    var baseMaterial = instance.BaseMaterial;
                    bool onInstance = instance.GetParameter(paramName) != null;
                    bool onBase = baseMaterial != null && baseMaterial.GetParameter(paramName) != null;
                    if (!onInstance && !onBase)
                    {
                        throw new ArgumentException("材质实例与基材上都没有该参数: " + paramName);
                    }

                    runtimeBefore = instance.GetParameterValue(paramName);
                    instance.SetParameterValue(paramName, converted, true);
                    runtimeAfter = instance.GetParameterValue(paramName);
                    if (!instance.Save(normalized))
                    {
                        throw new InvalidOperationException("材质实例保存失败: " + normalized);
                    }

                    overrodeOnInstance = instance.GetParameter(paramName) != null;
                });

                return new
                {
                    status = "set",
                    path = normalized,
                    param = paramName,
                    valueBefore = runtimeBefore == null ? "<null>" : runtimeBefore.ToString(),
                    valueAfter = runtimeAfter == null ? "<null>" : runtimeAfter.ToString(),
                    overrodeOnInstance,
                    savedVia = "runtime-instance",
                };
            }

            // ① 开窗（资产预加载一次，缩短 surface 就绪等待）
            await MainThread.InvokeAsync(() =>
            {
                var editor = FlaxEditor.Editor.Instance;
                var assetItem = editor.ContentDatabase.Find(pathArg) as AssetItem
                                ?? editor.ContentDatabase.Find(normalized) as AssetItem;
                if (assetItem == null)
                {
                    var loaded = Content.Load<MaterialBase>(normalized, 10000.0);
                    if (loaded == null || !loaded.IsLoaded)
                    {
                        throw new ArgumentException("材质加载失败: " + pathArg);
                    }

                    assetItem = editor.ContentDatabase.Find(loaded.ID) as AssetItem;
                    if (assetItem == null)
                    {
                        editor.ContentDatabase.Rebuild(true);
                        assetItem = editor.ContentDatabase.Find(loaded.ID) as AssetItem;
                    }
                }

                if (assetItem == null)
                {
                    throw new ArgumentException("找不到该材质的 AssetItem（路径与 Guid 两条都试过）: " + pathArg);
                }

                Content.Load<MaterialBase>(normalized, 10000.0);
                window = new MaterialWindow(editor, assetItem);
                editor.Windows.Open(window);
            });

            // ② 等 surface 就绪
            while (waited < 240)
            {
                bool ready = await MainThread.InvokeAsync(() => window != null && window.Surface != null && window.Surface.Enabled);
                if (ready)
                {
                    break;
                }

                waited++;
            }

            bool surfaceReady = await MainThread.InvokeAsync(() => window != null && window.Surface != null && window.Surface.Enabled);
            if (!surfaceReady)
            {
                throw new InvalidOperationException("材质窗口的 surface 在 " + waited
                    + " 帧内没有就绪（Surface.Enabled 仍为 false）⇒ 不写值、也不保存");
            }

            // ③ 改值 + 保存
            await MainThread.InvokeAsync(() =>
            {
                var surface = window.Surface;
                var sp = surface.GetParameter(paramName);
                if (sp == null)
                {
                    var names = new List<string>();
                    foreach (SurfaceParameter p in surface.Parameters)
                    {
                        names.Add(p.Name);
                    }

                    throw new ArgumentException("材质参数不存在: " + paramName
                        + "（可用: " + string.Join(", ", names.ToArray()) + "）");
                }

                valueBefore = sp.Value == null ? "<null>" : sp.Value.ToString();
                sp.Value = converted;
                // 同 add_params：改值不置编辑标记，`Save()` 会因 `!IsEdited` 空转 —— 这正是
                // "valueAfter 变了、下次读 valueBefore 还是旧值"那条坏往返的根因。
                surface.RootContext.MarkAsModified(true);
                window.Save();
                var after = surface.GetParameter(paramName);
                valueAfter = after == null || after.Value == null ? "<null>" : after.Value.ToString();
            });

            return new
            {
                status = "set",
                path = normalized,
                param = paramName,
                valueBefore,
                valueAfter,
                surfaceReadyAfterFrames = waited,
                savedVia = "editor-window",
            };
        }

        private static object ConvertMaterialParamValue(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Null:
                    // 贴图参数回到“未指定贴图”的初始态；序列化会保留 null（原资产里就是这个值）。
                    return null;
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
