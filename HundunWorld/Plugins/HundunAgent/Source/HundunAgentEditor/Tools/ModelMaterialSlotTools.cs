using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEngine;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// 模型资产的**槽位材质**读写（G1「适配 UE MetaHuman 资源」的一环）。
    ///
    /// 为什么要有这个工具：导入的真头 <c>Ada_FaceMesh.flax</c> 有 9 个材质槽但**全是 null**
    /// （实测 <c>model_inspect</c> 的 <c>materialSlots[i].material = null</c>），
    /// 而按槽名生成好的供体材质（head/teeth/eyeLeft/eyeRight/eyeshell/eyeEdge/eyelashes/saliva）
    /// 确实存在于盘上。槽位没材质=头部没有外观宿主，肤色/贴图那几条通道连写哪儿都没有。
    /// 现有的 <c>material_assign</c> 只作用在**场景 Actor** 上（每次摆场景都要重做），
    /// 这里补的是**资产级**的一次性修复。
    ///
    /// 口径与 <c>metahuman_bind</c> 一致：默认 <c>apply=false</c> 只算不写，回执里的
    /// <c>after</c> 一律从资产上读回来，不是"请求里想要的值"。
    /// </summary>
    public static class ModelMaterialSlotTools
    {
        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "model_material_slots_assign",
                Description = "给模型资产的材质槽指认材质（资产级，写一次永久生效）。" +
                              "materials 按槽索引给路径；bySlotName 按槽名给路径（大小写不敏感，索引版优先）。" +
                              "默认 apply=false 只算不写；apply=true 才赋值并 Save。" +
                              "回执 before/after 都从资产读回，槽名不匹配或材质加载失败会逐条列在 problems 里。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" +
                                  "\"model\":{\"type\":\"string\",\"description\":\"ModelBase/SkinnedModel 资产路径\"}," +
                                  "\"materials\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"按槽索引顺序给材质资产路径，短于槽数的部分留空\"}," +
                                  "\"bySlotName\":{\"type\":\"object\",\"description\":\"槽名→材质路径（不区分大小写、忽略首尾空格）\"}," +
                                  "\"apply\":{\"type\":\"boolean\",\"description\":\"true 才写资产，默认 false\"}}," +
                                  "\"required\":[\"model\"]}",
                Execute = AssignAsync
            });
        }

        private static Task<object> AssignAsync(JsonElement args)
        {
            return Task<object>.Run(() =>
            {
                string modelPath = EditorUtils.GetString(args, "model", null);
                if (string.IsNullOrWhiteSpace(modelPath))
                {
                    throw new InvalidOperationException("model 不能为空（要指向一份模型资产）");
                }

                bool apply = EditorUtils.GetBool(args, "apply", false);
                List<string> byIndex = EditorUtils.GetStringArray(args, "materials");
                Dictionary<string, string> byName = ReadNameMap(args);

                ModelBase model = LoadModel(modelPath);
                if (model == null)
                {
                    throw new InvalidOperationException("模型资产加载失败：" + modelPath
                        + "（不在内容数据库时先让编辑器重建数据库）");
                }

                MaterialSlot[] slots;
                try
                {
                    slots = model.MaterialSlots;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("取不到 MaterialSlots：" + ex.Message);
                }

                if (slots == null || slots.Length == 0)
                {
                    throw new InvalidOperationException("这份资产上没有材质槽（" + modelPath + "）");
                }

                var problems = new List<string>();
                var receipt = new List<object>();
                var expectedAfter = new List<string>();
                var cache = new Dictionary<string, Material>(StringComparer.Ordinal);
                int planned = 0;
                int changed = 0;

                for (int i = 0; i < slots.Length; i++)
                {
                    MaterialSlot slot = slots[i];
                    string slotName = slot != null && slot.Name != null ? slot.Name.Trim() : string.Empty;
                    string wanted = ResolvePath(i, slotName, byIndex, byName);

                    string before = slot != null && slot.Material != null ? slot.Material.Path : null;
                    string after = before;

                    if (!string.IsNullOrWhiteSpace(wanted))
                    {
                        planned++;
                        Material material = LoadMaterial(wanted, cache, problems);
                        if (material != null)
                        {
                            if (apply)
                            {
                                try
                                {
                                    slot.Material = material;
                                }
                                catch (Exception ex)
                                {
                                    problems.Add("槽 " + i + "(" + slotName + ") 赋值失败：" + ex.Message);
                                }
                            }

                            after = wanted;
                        }
                        else
                        {
                            problems.Add("槽 " + i + "(" + slotName + ") 想要的材质加载不到：" + wanted);
                        }
                    }

                    if (!string.Equals(before ?? string.Empty, after ?? string.Empty, StringComparison.Ordinal))
                    {
                        changed++;
                    }

                    receipt.Add(new
                    {
                        index = i,
                        slotName,
                        before,
                        planned = string.IsNullOrWhiteSpace(wanted) ? null : wanted,
                        after = apply ? (slot != null && slot.Material != null ? slot.Material.Path : null) : after
                    });
                    expectedAfter.Add(after);
                }

                bool? saved = null;
                if (apply && changed > 0)
                {
                    // 与绑骨核同一条经验：Flax 的 Save 返回 true 表示"存不了"，判据是反的
                    saved = !model.Save(false, string.Empty);
                }

                // 写过的槽再从资产上读一遍：回执以读回值为准，不拿请求值冒充
                List<object> verify = null;
                if (apply && changed > 0)
                {
                    verify = new List<object>();
                    MaterialSlot[] reread = null;
                    try
                    {
                        reread = model.MaterialSlots;
                    }
                    catch (Exception ex)
                    {
                        problems.Add("写后回读 MaterialSlots 失败：" + ex.Message);
                    }

                    for (int i = 0; i < slots.Length; i++)
                    {
                        string read = reread != null && i < reread.Length && reread[i] != null && reread[i].Material != null
                            ? reread[i].Material.Path
                            : null;
                        string expect = i < expectedAfter.Count ? expectedAfter[i] : null;
                        verify.Add(new
                        {
                            index = i,
                            expected = expect,
                            readBack = read,
                            matches = string.Equals(expect ?? string.Empty, read ?? string.Empty, StringComparison.Ordinal)
                        });
                    }
                }

                return (object)new
                {
                    model = model.Path ?? modelPath,
                    assetId = model.ID.ToString(),
                    slotCount = slots.Length,
                    apply,
                    slotsPlanned = planned,
                    slotsChanged = changed,
                    saved,
                    nonNullSlotsAfter = CountNonNull(model),
                    receipts = receipt,
                    reread = verify,
                    problems
                };
            });
        }

        private static string ResolvePath(int index, string slotName, List<string> byIndex, Dictionary<string, string> byName)
        {
            if (byIndex != null && index < byIndex.Count && !string.IsNullOrWhiteSpace(byIndex[index]))
            {
                return byIndex[index];
            }

            if (byName != null && !string.IsNullOrEmpty(slotName))
            {
                string hit;
                if (byName.TryGetValue(slotName.ToLowerInvariant(), out hit) && !string.IsNullOrWhiteSpace(hit))
                {
                    return hit;
                }
            }

            return null;
        }

        private static Dictionary<string, string> ReadNameMap(JsonElement args)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            JsonElement node;
            if (!args.TryGetProperty("bySlotName", out node) || node.ValueKind != JsonValueKind.Object)
            {
                return map;
            }

            foreach (JsonProperty prop in node.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    map[prop.Name.Trim().ToLowerInvariant()] = prop.Value.GetString();
                }
            }

            return map;
        }

        private static int CountNonNull(ModelBase model)
        {
            try
            {
                MaterialSlot[] slots = model.MaterialSlots;
                if (slots == null)
                {
                    return 0;
                }

                int n = 0;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] != null && slots[i].Material != null)
                    {
                        n++;
                    }
                }

                return n;
            }
            catch
            {
                return -1;
            }
        }

        private static ModelBase LoadModel(string path)
        {
            string normalized = path.Replace('\\', '/');
            Content.GetAssetInfo(normalized, out AssetInfo info);
            SkinnedModel skinned = Content.Load<SkinnedModel>(normalized, 10000.0f);
            if (skinned != null)
            {
                return skinned;
            }

            return Content.Load<ModelBase>(normalized, 10000.0f);
        }

        private static Material LoadMaterial(string path, Dictionary<string, Material> cache, List<string> problems)
        {
            string normalized = path.Replace('\\', '/');
            Material cached;
            if (cache.TryGetValue(normalized, out cached))
            {
                return cached;
            }

            Material material = Content.Load<Material>(normalized, 10000.0f);
            if (material == null)
            {
                Content.GetAssetInfo(normalized, out AssetInfo info);
                if (info.ID != Guid.Empty)
                {
                    material = Content.Load<Material>(info.ID, 10000.0f);
                }
            }

            cache[normalized] = material;
            if (material == null)
            {
                problems.Add("材质资产不在数据库或加载失败：" + normalized);
            }

            return material;
        }
    }
}
