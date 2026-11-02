// 模块关系说明：
// 本文件属于 NarrativePro.CharacterCreator（UE5 CharacterCreator 移植）。
//
// 为什么需要它（缺陷修复）：
// UE5 把角色创建器产出的外观存在 UNarrativeSaveWithCreatorData 里，ACharacterCreatorComponent 与
// ANarrativePlayerCharacter::GetCharacterCreatorData 都读它；Flax 端口此前只在
// CharacterCreatorFactory.CreateDefaultAttributeSet 里把 NarrativeSaveWithCreatorData 当临时构造器用
// （工厂 168 行那一处），NarrativeSave 里没有创建器字段，读档后外观按随机种子重掷。
//
// 本文件把“创建器产出的属性集”压成一份纯字符串/数值快照（NarrativeCreatorSnapshot）持久化进
// NarrativeSave.CreatorData，并能在读档时按路径重新解析资产、还原成 CharacterCreatorAttributeSet
// 交给 NarrativeCharacterVisual.InitializeFromCharacterAndAttributes。
//
// 为什么用扁平快照而不是直接序列化属性集：
//   属性集里有 SkinnedModel / Material 这类引擎资产引用（FlaxEngine.Object 派生），
//   它们不能跨会话还原；快照一律以“资产路径 + 标签字符串”表达，天然与枚举顺序无关，
//   也不依赖存档状态归档器（NarrativeSaveStateArchive）的按键定位，
//   因此不会重新引入“按 GetScripts 顺序定位”的老问题。

using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Character;
using NarrativePro.Core;
using NarrativePro.Items;
using NarrativePro.SaveSystem;
using NarrativePlayerCharacter = NarrativePro.UnrealFramework.NarrativePlayerCharacter;

namespace NarrativePro.CharacterCreator
{
    /// <summary>创建器外观快照：一条网格槽位。</summary>
    [Serializable]
    public class CreatorMeshSnapshot
    {
        /// <summary>槽位标签名（如 Narrative.Equipment.Slot.Torso）。</summary>
        public string Slot = "";

        /// <summary>3P 网格资产路径。</summary>
        public string MeshPath = "";

        /// <summary>1P 网格资产路径。</summary>
        public string Mesh1PPath = "";

        /// <summary>是否使用 Leader Pose。</summary>
        public bool bUseLeaderPose = true;

        /// <summary>是否静态网格。</summary>
        public bool bIsStaticMesh;

        /// <summary>静态网格资产路径。</summary>
        public string StaticMeshPath = "";

        /// <summary>附着骨骼名。</summary>
        public string AttachSocket = "";

        /// <summary>附着偏移。</summary>
        public Transform AttachOffset = Transform.Identity;

        /// <summary>动画（AnimationGraph）资产路径。</summary>
        public string AnimBPPath = "";

        /// <summary>需要一并隐藏的槽位标签名。</summary>
        public List<string> HideSlots = new List<string>();

        /// <summary>材质资产路径（按材质索引顺序）。</summary>
        public List<string> MaterialPaths = new List<string>();

        /// <summary>本网格携带的 morph 组（标量标签 + morph 名）。</summary>
        public List<CreatorMorphGroupSnapshot> Morphs = new List<CreatorMorphGroupSnapshot>();
    }

    /// <summary>创建器外观快照：morph 组（一个标量驱动一组 morph 名）。</summary>
    [Serializable]
    public class CreatorMorphGroupSnapshot
    {
        /// <summary>驱动该组的标量标签名。</summary>
        public string ScalarTag = "";

        /// <summary>受该标量控制的 morph 名。</summary>
        public List<string> MorphNames = new List<string>();
    }

    /// <summary>创建器外观快照：一条毛发槽位。</summary>
    [Serializable]
    public class CreatorGroomSnapshot
    {
        /// <summary>槽位标签名。</summary>
        public string Slot = "";

        /// <summary>毛发资产路径。</summary>
        public string GroomAssetPath = "";

        /// <summary>毛发绑定资产路径。</summary>
        public string GroomBindingAssetPath = "";

        /// <summary>材质资产路径。</summary>
        public List<string> MaterialPaths = new List<string>();
    }

    /// <summary>创建器外观快照：全局标量值。</summary>
    [Serializable]
    public class CreatorScalarSnapshot
    {
        /// <summary>标量标签名。</summary>
        public string Tag = "";

        /// <summary>数值。</summary>
        public float Value;
    }

    /// <summary>创建器外观快照：全局颜色值（按通道展开，避免依赖引擎结构体的序列化行为）。</summary>
    [Serializable]
    public class CreatorVectorSnapshot
    {
        /// <summary>颜色标签名。</summary>
        public string Tag = "";

        public float R;
        public float G;
        public float B;
        public float A = 1f;
    }

    /// <summary>创建器外观快照：全局 morph 值。</summary>
    [Serializable]
    public class CreatorMorphSnapshot
    {
        /// <summary>morph 名。</summary>
        public string MorphName = "";

        /// <summary>morph 权重。</summary>
        public float Value;
    }

    /// <summary>
    /// 创建器外观的存档形态（对应 UE5 UNarrativeSaveWithCreatorData 的持久化用法）。
    /// 只含字符串/数值/Transform，因此与组件归档的按键定位完全无关，也不受脚本顺序影响。
    /// </summary>
    [Serializable]
    public class NarrativeCreatorSnapshot
    {
        /// <summary>快照结构版本（后续字段增加时用于兼容判断）。</summary>
        public int Version = 1;

        /// <summary>创建器里设置的用户名（对应 NarrativeSaveWithCreatorData.CharacterCreatorUsername）。</summary>
        public string Username = "";

        /// <summary>体型表单标签名（对应属性集 FormTag）。</summary>
        public string FormTag = "";

        /// <summary>视觉类路径。</summary>
        public string CharacterVisualClassPath = "";

        /// <summary>基础网格资产路径。</summary>
        public string BaseMeshPath = "";

        /// <summary>是否隐藏基础网格。</summary>
        public bool bHideBaseMesh = true;

        /// <summary>基础网格动画路径。</summary>
        public string BaseMeshAnimBPPath = "";

        /// <summary>本地（1P）网格动画路径。</summary>
        public string BaseLocalMeshAnimBPPath = "";

        /// <summary>徒手动画层路径。</summary>
        public string UnarmedAnimLayerPath = "";

        /// <summary>各槽位网格。</summary>
        public List<CreatorMeshSnapshot> Meshes = new List<CreatorMeshSnapshot>();

        /// <summary>各槽位毛发。</summary>
        public List<CreatorGroomSnapshot> Grooms = new List<CreatorGroomSnapshot>();

        /// <summary>全局 morph 值。</summary>
        public List<CreatorMorphSnapshot> Morphs = new List<CreatorMorphSnapshot>();

        /// <summary>全局标量值。</summary>
        public List<CreatorScalarSnapshot> Scalars = new List<CreatorScalarSnapshot>();

        /// <summary>全局颜色值。</summary>
        public List<CreatorVectorSnapshot> Vectors = new List<CreatorVectorSnapshot>();

        /// <summary>快照是否携带任何外观信息。</summary>
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(FormTag)
                   || !string.IsNullOrEmpty(BaseMeshPath)
                   || (Meshes != null && Meshes.Count > 0)
                   || (Grooms != null && Grooms.Count > 0)
                   || (Morphs != null && Morphs.Count > 0)
                   || (Scalars != null && Scalars.Count > 0)
                   || (Vectors != null && Vectors.Count > 0);
        }
    }

    /// <summary>
    /// 创建器外观的存取入口：写盘前从本地玩家的创建器数据快照，读档后在视觉初始化前还原。
    /// </summary>
    public static class NarrativeCreatorPersistence
    {
        /// <summary>
        /// 把当前本地玩家外观写入存档（由 NarrativeSaveSubsystem 在每次写盘前调用）。
        /// 取不到玩家/属性集为空时保持存档里已有的快照不变 —— 绝不做“清空外观”这种事。
        /// </summary>
        public static void CaptureIntoSave(NarrativeSave save)
        {
            if (save == null) return;

            try
            {
                var creatorData = GetLocalPlayerCreatorData();
                if (creatorData == null || creatorData.CharacterCreatorAttributes == null) return;
                if (!HasAppearance(creatorData.CharacterCreatorAttributes)) return;

                var snapshot = FromAttributeSet(creatorData.CharacterCreatorAttributes);
                if (snapshot == null || !snapshot.IsValid()) return;

                // 用户名以创建器数据为准，缺省时沿用存档里已有的（避免每次保存把名字抹掉）
                snapshot.Username = !string.IsNullOrEmpty(creatorData.CharacterCreatorUsername)
                    ? creatorData.CharacterCreatorUsername
                    : (save.CreatorData != null ? save.CreatorData.Username : string.Empty);

                save.CreatorData = snapshot;
            }
            catch (Exception ex)
            {
                Core.NarrativeLog.LogWarning("[CharacterCreator] 创建器外观快照失败（本次保存沿用旧快照）：" + ex.Message);
            }
        }

        /// <summary>
        /// 本地玩家的创建器存档数据。对应 UE5 ANarrativePlayerCharacter::GetCharacterCreatorData。
        /// 角色路径不可用时，退回到场景里已存在的角色视觉上的属性集（宿主装配模式）。
        /// </summary>
        public static NarrativeSaveWithCreatorData GetLocalPlayerCreatorData()
        {
            var player = FindScriptSafe<NarrativePlayerCharacter>();
            if (player != null)
            {
                var fromPlayer = player.GetCharacterCreatorData();
                if (fromPlayer != null) return fromPlayer;
            }

            var visual = FindScriptSafe<NarrativeCharacterVisual>();
            if (visual != null && visual.AppearanceAttributeSet != null)
            {
                return new NarrativeSaveWithCreatorData
                {
                    CharacterCreatorAttributes = visual.AppearanceAttributeSet,
                };
            }

            return null;
        }

        /// <summary>属性集里是否有值得持久化的外观内容。</summary>
        public static bool HasAppearance(CharacterCreatorAttributeSet attributes)
        {
            if (attributes == null) return false;
            return attributes.FormTag.IsValid()
                   || attributes.BaseMesh != null
                   || !string.IsNullOrEmpty(GetAssetPath(attributes.BaseMesh))
                   || (attributes.MeshEntries != null && attributes.MeshEntries.Count > 0)
                   || (attributes.GroomEntries != null && attributes.GroomEntries.Count > 0)
                   || (attributes.Morphs != null && attributes.Morphs.Count > 0)
                   || (attributes.ScalarEntries != null && attributes.ScalarEntries.Count > 0)
                   || (attributes.VectorEntries != null && attributes.VectorEntries.Count > 0);
        }

        /// <summary>属性集 → 快照（按槽位/标签寻址，不依赖任何列表顺序）。</summary>
        public static NarrativeCreatorSnapshot FromAttributeSet(CharacterCreatorAttributeSet attributes)
        {
            if (attributes == null) return null;

            var snapshot = new NarrativeCreatorSnapshot
            {
                Version = 1,
                FormTag = attributes.FormTag.TagName ?? string.Empty,
                CharacterVisualClassPath = attributes.CharacterVisualClassPath ?? string.Empty,
                BaseMeshPath = GetAssetPath(attributes.BaseMesh),
                bHideBaseMesh = attributes.bHideBaseMesh,
                BaseMeshAnimBPPath = attributes.BaseMeshAnimBPPath ?? string.Empty,
                BaseLocalMeshAnimBPPath = attributes.BaseLocalMeshAnimBPPath ?? string.Empty,
                UnarmedAnimLayerPath = attributes.UnarmedAnimLayerPath ?? string.Empty,
            };

            if (attributes.MeshEntries != null)
            {
                foreach (var entry in attributes.MeshEntries)
                {
                    if (entry == null || entry.Attribute == null) continue;
                    snapshot.Meshes.Add(FromMeshAttribute(entry.Slot, entry.Attribute));
                }
            }

            if (attributes.GroomEntries != null)
            {
                foreach (var entry in attributes.GroomEntries)
                {
                    if (entry == null || entry.Attribute == null) continue;
                    snapshot.Grooms.Add(new CreatorGroomSnapshot
                    {
                        Slot = entry.Slot.TagName ?? string.Empty,
                        GroomAssetPath = entry.Attribute.GroomAssetPath ?? string.Empty,
                        GroomBindingAssetPath = entry.Attribute.GroomBindingAssetPath ?? string.Empty,
                        MaterialPaths = CollectMaterialPaths(entry.Attribute.GroomMaterials),
                    });
                }
            }

            if (attributes.Morphs != null)
            {
                foreach (var morph in attributes.Morphs)
                {
                    if (morph == null) continue;
                    snapshot.Morphs.Add(new CreatorMorphSnapshot
                    {
                        MorphName = morph.MorphName ?? string.Empty,
                        Value = morph.MorphValue,
                    });
                }
            }

            if (attributes.ScalarEntries != null)
            {
                foreach (var scalar in attributes.ScalarEntries)
                {
                    if (scalar == null) continue;
                    snapshot.Scalars.Add(new CreatorScalarSnapshot
                    {
                        Tag = scalar.Tag.TagName ?? string.Empty,
                        Value = scalar.Value,
                    });
                }
            }

            if (attributes.VectorEntries != null)
            {
                foreach (var vector in attributes.VectorEntries)
                {
                    if (vector == null) continue;
                    snapshot.Vectors.Add(new CreatorVectorSnapshot
                    {
                        Tag = vector.Tag.TagName ?? string.Empty,
                        R = vector.Value.R,
                        G = vector.Value.G,
                        B = vector.Value.B,
                        A = vector.Value.A,
                    });
                }
            }

            return snapshot;
        }

        private static CreatorMeshSnapshot FromMeshAttribute(GameplayTag slot, CharacterCreatorAttribute_Mesh attribute)
        {
            var snapshot = new CreatorMeshSnapshot
            {
                Slot = slot.TagName ?? string.Empty,
                // 优先用显式路径字段；老数据只有资产引用时按 Asset.Path 兜底
                MeshPath = !string.IsNullOrEmpty(attribute.MeshPath) ? attribute.MeshPath : GetAssetPath(attribute.Mesh),
                Mesh1PPath = !string.IsNullOrEmpty(attribute.Mesh1PPath) ? attribute.Mesh1PPath : GetAssetPath(attribute.Mesh1P),
                bUseLeaderPose = attribute.bUseLeaderPose,
                bIsStaticMesh = attribute.bIsStaticMesh,
                StaticMeshPath = GetAssetPath(attribute.StaticMesh),
                AttachSocket = attribute.MeshAttachSocket ?? string.Empty,
                AttachOffset = attribute.MeshAttachOffset,
                AnimBPPath = attribute.MeshAnimBPPath ?? string.Empty,
            };

            if (attribute.HideSlots != null)
            {
                foreach (var hide in attribute.HideSlots)
                {
                    if (hide.IsValid()) snapshot.HideSlots.Add(hide.TagName);
                }
            }

            snapshot.MaterialPaths = CollectMaterialPaths(attribute.MeshMaterials);

            if (attribute.Morphs != null)
            {
                foreach (var morph in attribute.Morphs)
                {
                    if (morph == null) continue;
                    var group = new CreatorMorphGroupSnapshot
                    {
                        ScalarTag = morph.ScalarTag.TagName ?? string.Empty,
                    };
                    if (morph.MorphNames != null)
                    {
                        foreach (var name in morph.MorphNames)
                        {
                            if (!string.IsNullOrEmpty(name)) group.MorphNames.Add(name);
                        }
                    }
                    snapshot.Morphs.Add(group);
                }
            }

            return snapshot;
        }

        private static List<string> CollectMaterialPaths(List<CreatorMeshMaterial> materials)
        {
            var paths = new List<string>();
            if (materials == null) return paths;

            foreach (var material in materials)
            {
                if (material == null) continue;
                string path = !string.IsNullOrEmpty(material.MaterialPath)
                    ? material.MaterialPath
                    : GetAssetPath(material.Material);
                paths.Add(path ?? string.Empty);
            }

            return paths;
        }

        /// <summary>快照 → 属性集（按路径重新解析资产；路径解析失败只跳过该项，不抛）。</summary>
        public static CharacterCreatorAttributeSet ToAttributeSet(NarrativeCreatorSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.IsValid()) return null;

            var attributes = new CharacterCreatorAttributeSet
            {
                FormTag = new GameplayTag(snapshot.FormTag),
                CharacterVisualClassPath = snapshot.CharacterVisualClassPath ?? string.Empty,
                BaseMesh = LoadSkinnedModel(snapshot.BaseMeshPath),
                bHideBaseMesh = snapshot.bHideBaseMesh,
                BaseMeshAnimBPPath = snapshot.BaseMeshAnimBPPath ?? string.Empty,
                BaseLocalMeshAnimBPPath = snapshot.BaseLocalMeshAnimBPPath ?? string.Empty,
                UnarmedAnimLayerPath = snapshot.UnarmedAnimLayerPath ?? string.Empty,
            };

            if (snapshot.Meshes != null)
            {
                foreach (var mesh in snapshot.Meshes)
                {
                    if (mesh == null) continue;

                    var attribute = new CharacterCreatorAttribute_Mesh
                    {
                        MeshPath = mesh.MeshPath ?? string.Empty,
                        Mesh1PPath = mesh.Mesh1PPath ?? string.Empty,
                        Mesh = LoadSkinnedModel(mesh.MeshPath),
                        Mesh1P = LoadSkinnedModel(mesh.Mesh1PPath),
                        bUseLeaderPose = mesh.bUseLeaderPose,
                        bIsStaticMesh = mesh.bIsStaticMesh,
                        StaticMesh = LoadModel(mesh.StaticMeshPath),
                        MeshAttachSocket = mesh.AttachSocket ?? string.Empty,
                        MeshAttachOffset = mesh.AttachOffset,
                        MeshAnimBPPath = mesh.AnimBPPath ?? string.Empty,
                    };

                    if (mesh.HideSlots != null)
                    {
                        foreach (var hide in mesh.HideSlots)
                        {
                            var tag = new GameplayTag(hide);
                            if (tag.IsValid()) attribute.HideSlots.Add(tag);
                        }
                    }

                    if (mesh.MaterialPaths != null)
                    {
                        foreach (var path in mesh.MaterialPaths)
                        {
                            attribute.MeshMaterials.Add(new CreatorMeshMaterial
                            {
                                MaterialPath = path ?? string.Empty,
                                Material = LoadMaterial(path),
                            });
                        }
                    }

                    if (mesh.Morphs != null)
                    {
                        foreach (var group in mesh.Morphs)
                        {
                            if (group == null) continue;
                            var morph = new CreatorMeshMorph { ScalarTag = new GameplayTag(group.ScalarTag) };
                            if (group.MorphNames != null)
                            {
                                foreach (var name in group.MorphNames)
                                {
                                    if (!string.IsNullOrEmpty(name)) morph.MorphNames.Add(name);
                                }
                            }
                            attribute.Morphs.Add(morph);
                        }
                    }

                    SetMeshEntry(attributes, new GameplayTag(mesh.Slot), attribute);
                }
            }

            if (snapshot.Grooms != null)
            {
                foreach (var groom in snapshot.Grooms)
                {
                    if (groom == null) continue;

                    var attribute = new CharacterCreatorAttribute_Groom
                    {
                        GroomAssetPath = groom.GroomAssetPath ?? string.Empty,
                        GroomBindingAssetPath = groom.GroomBindingAssetPath ?? string.Empty,
                    };

                    if (groom.MaterialPaths != null)
                    {
                        foreach (var path in groom.MaterialPaths)
                        {
                            attribute.GroomMaterials.Add(new CreatorMeshMaterial
                            {
                                MaterialPath = path ?? string.Empty,
                                Material = LoadMaterial(path),
                            });
                        }
                    }

                    var slot = new GameplayTag(groom.Slot);
                    int existing = attributes.GroomEntries.FindIndex(e => e != null && e.Slot == slot);
                    var entry = new GroomAttributeEntry { Slot = slot, Attribute = attribute };
                    if (existing >= 0) attributes.GroomEntries[existing] = entry;
                    else attributes.GroomEntries.Add(entry);
                }
            }

            if (snapshot.Morphs != null)
            {
                foreach (var morph in snapshot.Morphs)
                {
                    if (morph == null || string.IsNullOrEmpty(morph.MorphName)) continue;
                    attributes.Morphs.Add(new CharacterCreatorAttribute_Morph
                    {
                        MorphName = morph.MorphName,
                        MorphValue = morph.Value,
                    });
                }
            }

            if (snapshot.Scalars != null)
            {
                foreach (var scalar in snapshot.Scalars)
                {
                    if (scalar == null) continue;
                    var tag = new GameplayTag(scalar.Tag);
                    if (!tag.IsValid()) continue;
                    int existing = attributes.ScalarEntries.FindIndex(e => e != null && e.Tag == tag);
                    if (existing >= 0) attributes.ScalarEntries[existing].Value = scalar.Value;
                    else attributes.ScalarEntries.Add(new ScalarValueEntry { Tag = tag, Value = scalar.Value });
                }
            }

            if (snapshot.Vectors != null)
            {
                foreach (var vector in snapshot.Vectors)
                {
                    if (vector == null) continue;
                    var tag = new GameplayTag(vector.Tag);
                    if (!tag.IsValid()) continue;
                    var color = new Color(vector.R, vector.G, vector.B, vector.A);
                    int existing = attributes.VectorEntries.FindIndex(e => e != null && e.Tag == tag);
                    if (existing >= 0) attributes.VectorEntries[existing].Value = color;
                    else attributes.VectorEntries.Add(new VectorValueEntry { Tag = tag, Value = color });
                }
            }

            return attributes;
        }

        /// <summary>按槽位写入/替换网格条目（槽位即身份，与列表顺序无关）。</summary>
        private static void SetMeshEntry(CharacterCreatorAttributeSet attributes, GameplayTag slot, CharacterCreatorAttribute_Mesh attribute)
        {
            if (attributes == null || !slot.IsValid() || attribute == null) return;

            int existing = attributes.MeshEntries.FindIndex(e => e != null && e.Slot == slot);
            var entry = new MeshAttributeEntry { Slot = slot, Attribute = attribute };
            if (existing >= 0) attributes.MeshEntries[existing] = entry;
            else attributes.MeshEntries.Add(entry);
        }

        /// <summary>存档里的创建器快照（无存档 / 无快照时为 null）。</summary>
        public static NarrativeCreatorSnapshot LoadFromCurrentSave()
        {
            try
            {
                var subsystem = NarrativePro.SaveSystem.Subsystems.NarrativeSaveSubsystem.Instance;
                var save = subsystem != null ? subsystem.GetSaveObject() : null;
                return save != null ? save.CreatorData : null;
            }
            catch (Exception ex)
            {
                Core.NarrativeLog.LogWarning("[CharacterCreator] 读取存档创建器数据失败：" + ex.Message);
                return null;
            }
        }

        private static SkinnedModel LoadSkinnedModel(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                return Content.Load<SkinnedModel>(path);
            }
            catch (Exception ex)
            {
                Core.NarrativeLog.LogWarning($"[CharacterCreator] 网格资产加载失败 {path}: {ex.Message}");
                return null;
            }
        }

        private static Model LoadModel(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                return Content.Load<Model>(path);
            }
            catch (Exception ex)
            {
                Core.NarrativeLog.LogWarning($"[CharacterCreator] 静态网格加载失败 {path}: {ex.Message}");
                return null;
            }
        }

        private static Material LoadMaterial(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                return Content.Load<Material>(path);
            }
            catch (Exception ex)
            {
                Core.NarrativeLog.LogWarning($"[CharacterCreator] 材质加载失败 {path}: {ex.Message}");
                return null;
            }
        }

        private static string GetAssetPath(SkinnedModel model) => GetAssetPath((Asset)model);

        private static string GetAssetPath(Asset asset)
        {
            if (asset == null) return string.Empty;
            try
            {
                return asset.Path ?? string.Empty;
            }
            catch
            {
                // 虚拟/未加载资产取路径可能异常：按“无法持久化”处理
                return string.Empty;
            }
        }

        private static T FindScriptSafe<T>() where T : Script
        {
            try
            {
                return Level.FindScript<T>();
            }
            catch
            {
                return null;
            }
        }
    }
}
