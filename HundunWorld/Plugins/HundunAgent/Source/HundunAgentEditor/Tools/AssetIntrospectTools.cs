using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEngine;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// 引擎原生资产探查：通过 Flax 内容系统加载资产后读取真实几何数据。
    /// 与 <see cref="AssetForensicsTools"/> 的字节取证互补——字节直方图只能猜，
    /// 这里拿到的 LOD/顶点/包围盒/材质槽是引擎解析后的确定值，
    /// 排查「模型导入后尺度或朝向不对」时应优先看这里。
    ///
    /// 注意：<c>Model</c> 与 <c>SkinnedModel</c> 是兄弟类型（都继承 <c>ModelBase</c>），
    /// 因此通用信息一律走 <c>ModelBase</c> / <c>ModelLODBase</c> / <c>MeshBase</c>，
    /// 骨骼相关内容再单独按 <c>SkinnedModel</c> 取值。
    /// </summary>
    public static class AssetIntrospectTools
    {
        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "model_inspect",
                Description = "加载模型资产并输出引擎解析后的真实数据：资产 Guid/类型、LOD 数量与逐级包围盒/网格数/顶点数/三角面数、" +
                              "材质槽与材质路径、骨骼/节点/BlendShape 数量与名称，并对包围盒给出「过小/过大」异常提示。只读。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" +
                                  "\"path\":{\"type\":\"string\",\"description\":\"资产路径，如 Content/Character/Models/skm_uefn_mannequin.flax；也可传 Guid\"}," +
                                  "\"includeBones\":{\"type\":\"boolean\",\"description\":\"是否列出骨骼/节点名，默认 true\"}," +
                                  "\"nameLimit\":{\"type\":\"integer\",\"description\":\"名称条数上限，默认 30\"}," +
                                  "\"timeoutMs\":{\"type\":\"integer\",\"description\":\"资产加载超时(毫秒)，默认 60000（12MB/821 形状的大头冷加载实测要 10s 以上）\"}}," +
                                  "\"required\":[\"path\"]}",
                Execute = ModelInspectAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "skeleton_retarget_list",
                Description = "列出 SkinnedModel 上已有的骨架重定向（SkeletonRetarget）条目，并顺带证实托管层能读到重定向 API。" +
                              "重定向条目是「把别的骨架上的动画剪剪辑接到本骨架」的唯一官方通路：对 Animation 源，引擎要先经 SkeletonAsset 中介再按名匹配。只读。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" +
                                  "\"path\":{\"type\":\"string\",\"description\":\"SkinnedModel 资产路径或 Guid，默认 Content/Character/Models/Character_1.flax\"}," +
                                  "\"maxEntries\":{\"type\":\"integer\",\"description\":\"最多返回条目数，默认 60\"}}}",
                Execute = SkeletonRetargetListAsync
            });
        }

        private static Task<object> SkeletonRetargetListAsync(JsonElement args)
        {
            var pathArg = EditorUtils.GetString(args, "path", "Content/Character/Models/Character_1.flax");
            var maxEntries = Math.Clamp(EditorUtils.GetInt(args, "maxEntries", 60), 1, 500);

            return MainThread.InvokeAsync<object>(() =>
            {
                var asset = EditorUtils.LoadAsset(pathArg, 15000);
                var model = asset as SkinnedModel;
                if (model == null)
                    throw new ArgumentException("资产加载失败或不是 SkinnedModel: " + pathArg);

                // 重定向条目在 C++ 侧包在 #if USE_EDITOR 里，托管绑定不一定生成；
                // 用反射枚举真实可见的成员，避免按头文件猜测 API 存在性。
                var retargetMembers = typeof(SkinnedModel)
                    .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Where(m => m.Name.IndexOf("Retarget", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                m.Name.IndexOf("Mapping", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(m => m.MemberType + " " + m.Name)
                    .Distinct()
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToList();

                var retargetTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a =>
                    {
                        try { return a.GetTypes(); }
                        catch { return Type.EmptyTypes; }
                    })
                    .Where(t => t.Name.IndexOf("Retarget", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(t => t.FullName + " :: " + string.Join(",", t.GetMembers(BindingFlags.Public | BindingFlags.Instance)
                                                                                  .Select(x => x.Name)
                                                                                  .Distinct()
                                                                                  .Take(8)))
                    .Distinct()
                    .Take(maxEntries)
                    .ToList();

                var entries = new List<object>();
                // 这里原先只找 `GetSkeletonRetargets()`，而 1.12 托管层没有这个方法——它自己的成员 dump
                // 就写着 `Property SkeletonRetargets` + `Method get_SkeletonRetargets`。于是 GetMethod
                // 恒返回 null、条目恒空，把「工具读不到」伪装成「资产里没有」：同一个 Character_1，
                // metahuman_bind 从对象上读到 1 条，这里却报 0 条。属性访问器在同装配的
                // MetaHumanTools 里是编译过、也跑过的写法，所以直接拿它兜底。
                var getRetargets = typeof(SkinnedModel).GetMethod("GetSkeletonRetargets", BindingFlags.Public | BindingFlags.Instance);
                var array = getRetargets?.Invoke(model, Type.EmptyTypes) as System.Collections.IEnumerable;
                var viaProperty = false;
                if (array == null)
                {
                    array = model.SkeletonRetargets as System.Collections.IEnumerable;
                    viaProperty = array != null;
                }
                if (array != null)
                {
                    foreach (var retarget in array)
                    {
                        if (entries.Count >= maxEntries)
                            break;

                        var type = retarget.GetType();
                        string ReadText(string name)
                        {
                            var value = type.GetProperty(name)?.GetValue(retarget) ?? type.GetField(name)?.GetValue(retarget);
                            return value?.ToString();
                        }

                        entries.Add(new
                        {
                            sourceAsset = ReadText("SourceAsset"),
                            skeletonAsset = ReadText("SkeletonAsset"),
                            nodesMapping = ReadText("NodesMapping")
                        });
                    }
                }

                var nodes = model.Nodes;
                return new
                {
                    path = model.Path ?? pathArg,
                    assetId = model.ID.ToString(),
                    loaded = model.IsLoaded,
                    nodeCount = nodes != null ? nodes.Length : 0,
                    managedRetargetMembers = retargetMembers,
                    managedRetargetTypes = retargetTypes,
                    hasGetSkeletonRetargets = getRetargets != null,
                    // 这条是这次改动唯一自证的生效判据：旧 DLL 里没有这个键，
                    // 只读普查一旦回 `retargetsViaProperty:true`，就说明装配套件确实换了新码。
                    retargetsViaProperty = viaProperty,
                    retargetCount = entries.Count,
                    entries
                };
            });
        }

        private static Task<object> ModelInspectAsync(JsonElement args)
        {
            var pathArg = EditorUtils.GetString(args, "path");
            if (string.IsNullOrEmpty(pathArg))
                throw new ArgumentException("缺少 path 参数");

            var includeBones = EditorUtils.GetBool(args, "includeBones", true);
            var nameLimit = Math.Clamp(EditorUtils.GetInt(args, "nameLimit", 30), 0, 500);
            var timeoutMs = Math.Clamp(EditorUtils.GetInt(args, "timeoutMs", 60000), 1000, 300000);

            return MainThread.InvokeAsync<object>(() =>
            {
                var warnings = new List<string>();
                var isGuid = Guid.TryParse(pathArg, out var guid);
                // 相对路径先转绝对：否则内容库会再登记一份同一个文件并改写 GUID（#77）。
                if (!isGuid)
                {
                    pathArg = EditorUtils.ResolveAssetPath(pathArg);
                }

                AssetInfo assetInfo = default;
                bool foundInDatabase;
                if (isGuid)
                {
                    foundInDatabase = Content.GetAssetInfo(guid, out assetInfo);
                }
                else
                {
                    // **绝不能**对路径调 Content.GetAssetInfo：注册表按字符串精确比对，
                    // 拼写不同就会走“打开存储重新登记”的分支，同一 GUID 变成两条记录，
                    // 引擎随后改名→重打包→文件被占用（0x20）→改名不落盘（实测见 6a2z）。
                    // 先按内容数据库的条目拿 ID，再问 ID。
                    var item = EditorUtils.FindAssetItem(pathArg);
                    foundInDatabase = item != null && Content.GetAssetInfo(item.ID, out assetInfo);
                }
                if (!foundInDatabase)
                    warnings.Add("内容数据库中未找到该资产（可能未导入或路径写法不同），仍尝试直接加载");

                // 一律走 EditorUtils.LoadAsset：它先按内容数据库的资产 ID 加载，
                // 不把调用方的路径拼写送进注册表（否则会重复登记同一 GUID，见 6a2z 的复发记录）。
                var asset = EditorUtils.LoadAsset(isGuid ? guid.ToString() : pathArg, timeoutMs);

                if (asset == null)
                {
                    // GetAssetInfo 与内容数据库是两套索引：前者按注册表路径宽松匹配，后者是编辑器真正可见的资产树。
                    // 新导入/手拷进来但未入库的资产会在前者命中、后者查不到，加载自然失败——这里把两边都报出来。
                    var knownToContentDatabase = false;
                    try
                    {
                        // 内容数据库的 Find(string) 也是精确比对，路径拼写不同会误报“不在库里”，
                        // 因此走分隔符不敏感的条目查找。
                        knownToContentDatabase = EditorUtils.FindAssetItem(pathArg) != null;
                    }
                    catch
                    {
                        // 内容数据库尚未就绪时忽略
                    }

                    return new
                    {
                        path = pathArg,
                        foundInDatabase,
                        knownToContentDatabase,
                        // 注册表记录的是「资产身份」，与磁盘上的文件头可能不一致（文件可被重新导入改写），
                        // 需要磁盘上的权威 Guid / 导入设置时用 asset_inspect
                        registryAssetId = foundInDatabase ? assetInfo.ID.ToString() : null,
                        registryTypeName = foundInDatabase ? assetInfo.TypeName : null,
                        loaded = false,
                        warnings = warnings.Concat(new[]
                        {
                            "资产加载失败或不存在",
                            knownToContentDatabase
                                ? "资产已在内容数据库中，加载失败通常意味着文件头版本不受支持或数据损坏（用 asset_inspect 查看 serializedVersion 与导入设置）"
                                : "资产不在编辑器内容数据库中（通常是新拷入未入库或位于被忽略的目录），请先让编辑器重建数据库后再试",
                            "registryAssetId 来自内容注册表，可能与磁盘文件头不一致（文件被重新导入后会改写），磁盘侧请用 asset_inspect 读取"
                        }).ToList()
                    };
                }

                var result = new Dictionary<string, object>
                {
                    { "path", asset.Path ?? pathArg },
                    { "foundInDatabase", foundInDatabase },
                    { "assetId", asset.ID.ToString() },
                    { "typeName", asset.GetType().Name },
                    { "loaded", asset.IsLoaded },
                    { "memoryUsageBytes", asset.MemoryUsage }
                };

                var modelBase = asset as ModelBase;
                if (modelBase == null)
                {
                    warnings.Add("该资产不是模型（" + asset.GetType().Name + "），只返回通用信息");
                    result["warnings"] = warnings;
                    return result;
                }

                InspectLods(modelBase, result, warnings);
                InspectMaterialSlots(modelBase, result);

                var skinned = asset as SkinnedModel;
                if (skinned != null)
                    InspectSkeleton(skinned, result, includeBones, nameLimit);

                result["warnings"] = warnings;
                return result;
            });
        }

        private static void InspectLods(ModelBase modelBase, Dictionary<string, object> result, List<string> warnings)
        {
            var lodCount = modelBase.LODsCount;
            result["lodCount"] = lodCount;
            result["loadedLodCount"] = modelBase.LoadedLODs;

            var lods = new List<object>();
            var sizeHints = new List<string>();

            for (var i = 0; i < lodCount; i++)
            {
                ModelLODBase lod;
                try
                {
                    lod = modelBase.GetLOD(i);
                }
                catch (Exception ex)
                {
                    warnings.Add("读取 LOD " + i + " 失败: " + ex.Message);
                    break;
                }

                if (lod == null)
                    continue;

                MeshBase[] meshes = null;
                try
                {
                    modelBase.GetMeshes(out meshes, i);
                }
                catch (Exception ex)
                {
                    warnings.Add("读取 LOD " + i + " 网格列表失败: " + ex.Message);
                }

                var vertexCount = 0;
                var triangleCount = 0;
                var meshBrief = new List<object>();
                if (meshes != null)
                {
                    for (var m = 0; m < meshes.Length; m++)
                    {
                        var mesh = meshes[m];
                        if (mesh == null)
                            continue;
                        vertexCount += mesh.VertexCount;
                        triangleCount += mesh.TriangleCount;

                        // 网格侧的名字与材质数组是"哪个槽是哪块皮"的唯一可靠键：
                        // 真头 Ada_FaceMesh 的槽名是通用的 Material 0..8，靠槽名对不上 head/teeth/eyeLeft…
                        // 而按索引猜又等于把导入顺序当契约。这里把两边都读出来，映射才有证据。
                        meshBrief.Add(new
                        {
                            index = m,
                            name = ReadMember(mesh, "Name"),
                            materialCount = (ReadMember(mesh, "Materials") as MaterialBase[])?.Length,
                            materials = (ReadMember(mesh, "Materials") as MaterialBase[])?.Select(x => x?.Path).ToArray(),
                            vertexCount = mesh.VertexCount,
                            triangleCount = mesh.TriangleCount
                        });
                    }
                }

                if (i == 0 && meshes != null && meshes.Length > 0 && meshes[0] != null)
                {
                    // 一次性把 MeshBase 的公开成员列出来：以后要按网格侧的哪个键做映射，看这里而不是猜
                    result["meshMemberDump"] = meshes[0].GetType().GetProperties()
                        .Select(p => p.Name + ":" + p.PropertyType.Name).ToArray();
                    result["meshesLod0"] = meshBrief;
                }

                object bounds = null;
                try
                {
                    bounds = ToBounds(lod.Box);
                }
                catch (Exception ex)
                {
                    warnings.Add("读取 LOD " + i + " 包围盒失败: " + ex.Message);
                }

                lods.Add(new
                {
                    index = i,
                    lodIndex = lod.LODIndex,
                    screenSize = lod.ScreenSize,
                    meshCount = meshes?.Length ?? 0,
                    vertexCount,
                    triangleCount,
                    bounds
                });

                // 只用最高精度 LOD0 的形状给出尺度提示，避免低级别 LOD 误导
                if (i == 0)
                    sizeHints.AddRange(CheckBounds(lod.Box));
            }

            result["lods"] = lods;
            foreach (var hint in sizeHints)
                warnings.Add(hint);
        }

        private static void InspectMaterialSlots(ModelBase modelBase, Dictionary<string, object> result)
        {
            var slotCount = modelBase.MaterialSlotsCount;
            result["materialSlotCount"] = slotCount;

            var slots = new List<object>();
            MaterialSlot[] materialSlots = null;
            try
            {
                materialSlots = modelBase.MaterialSlots;
            }
            catch
            {
                // 资产未加载完整时可能取不到，忽略
            }

            for (var i = 0; i < slotCount; i++)
            {
                MaterialSlot slot = null;
                if (materialSlots != null && i < materialSlots.Length)
                    slot = materialSlots[i];

                slots.Add(new
                {
                    index = i,
                    name = slot?.Name,
                    material = slot?.Material?.Path
                });
            }

            result["materialSlots"] = slots;
        }

        /// <summary>
        /// 反射读一个公开成员（属性或字段），不存在就返回 null。
        /// 网格侧的 Name/Materials 在 Flax 各版本里归属不同（`MeshBase` 还是 `ModelMesh`），
        /// 这里不写死类型名：读到什么就报什么，读不到也只是少一个键，不会把整次探查带崩。
        /// </summary>
        private static object ReadMember(object target, string name)
        {
            if (target == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            try
            {
                var type = target.GetType();
                var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.CanRead)
                {
                    return prop.GetValue(target, null);
                }

                var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                return field != null ? field.GetValue(target) : null;
            }
            catch
            {
                return null;
            }
        }

        private static void InspectSkeleton(SkinnedModel skinned, Dictionary<string, object> result, bool includeBones, int nameLimit)
        {
            var bones = skinned.Bones;
            var nodes = skinned.Nodes;
            var blendShapes = skinned.BlendShapes;

            result["boneCount"] = bones != null ? bones.Length : 0;
            result["nodeCount"] = nodes != null ? nodes.Length : 0;
            result["blendShapeCount"] = blendShapes != null ? blendShapes.Length : 0;

            if (!includeBones)
                return;

            if (nodes != null && nodes.Length > 0)
            {
                result["nodeNames"] = nodes
                    .Take(nameLimit)
                    .Where(n => !string.IsNullOrEmpty(n.Name))
                    .Select(n => n.Name)
                    .ToList();
            }

            if (bones != null && bones.Length > 0 && nodes != null)
            {
                var boneNames = new List<string>();
                foreach (var bone in bones.Take(nameLimit))
                {
                    if (bone.NodeIndex >= 0 && bone.NodeIndex < nodes.Length)
                        boneNames.Add(nodes[bone.NodeIndex].Name);
                }
                result["boneNames"] = boneNames;
            }

            if (blendShapes != null && blendShapes.Length > 0)
            {
                result["blendShapeNames"] = blendShapes
                    .Take(nameLimit)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();
            }
        }

        private static object ToBounds(BoundingBox box)
        {
            var size = box.Size;
            return new
            {
                minimum = new { x = box.Minimum.X, y = box.Minimum.Y, z = box.Minimum.Z },
                maximum = new { x = box.Maximum.X, y = box.Maximum.Y, z = box.Maximum.Z },
                center = new { x = box.Center.X, y = box.Center.Y, z = box.Center.Z },
                size = new { x = size.X, y = size.Y, z = size.Z }
            };
        }

        /// <summary>按最长边给出「过小/过大」提示——导入 Scale 写错时这里是第一现场。</summary>
        private static List<string> CheckBounds(BoundingBox box)
        {
            var hints = new List<string>();
            var size = box.Size;
            var maxAxis = Math.Max(size.X, Math.Max(size.Y, size.Z));

            if (maxAxis <= 0.0001f)
                hints.Add("LOD0 包围盒几乎为零（最长边 " + maxAxis + "），模型可能是空网格或导入失败");
            else if (maxAxis > 1000f)
                hints.Add("LOD0 包围盒过大（最长边 " + maxAxis + "），通常意味着导入 Scale 或单位不一致");
            else if (maxAxis < 0.01f)
                hints.Add("LOD0 包围盒过小（最长边 " + maxAxis + "），通常意味着导入 Scale 被写成极小值");

            return hints;
        }
    }
}
