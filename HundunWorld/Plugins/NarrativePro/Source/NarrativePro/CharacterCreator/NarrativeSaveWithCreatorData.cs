using System;
using FlaxEngine;
using NarrativePro.CharacterCreator.Items;
using NarrativePro.Items;
using NarrativePro.Save;

namespace NarrativePro.CharacterCreator
{
    /// <summary>
    /// 带创建器数据的存档。对应 UE5 UNarrativeSaveWithCreatorData。
    /// 在 NarrativeSaveData 基础上增加角色创建器属性和用户名。
    /// </summary>
    [Serializable]
    public class NarrativeSaveWithCreatorData : NarrativeSaveData
    {
        /// <summary>角色创建器属性集</summary>
        public CharacterCreatorAttributeSet CharacterCreatorAttributes = new CharacterCreatorAttributeSet();

        /// <summary>角色创建器中设置的用户名</summary>
        public string CharacterCreatorUsername = "";

        /// <summary>清空创建器网格和毛发（切换表单时调用）</summary>
        public virtual void ClearMeshesAndGrooms()
        {
            if (CharacterCreatorAttributes != null)
            {
                CharacterCreatorAttributes.MeshEntries.Clear();
                CharacterCreatorAttributes.GroomEntries.Clear();
            }
        }

        /// <summary>
        /// 设置创建器数据网格。对应 UE5 SetCreatorDataMesh（NarrativeSaveWithCreatorData.cpp:15-45）：
        /// 以 MeshItem 的 MeshAttributes 为基础写入 slot 条目，补齐材质/Morph，
        /// 并按路径解析 1P 网格、记录附着参数；嵌套标量/向量选项的默认值写入全局值表。
        /// </summary>
        public virtual void SetCreatorDataMesh(CharacterCreatorItem_Mesh meshItem)
        {
            if (meshItem == null || CharacterCreatorAttributes == null) return;

            // 查找是否已有此 slot 的网格
            var entry = CharacterCreatorAttributes.MeshEntries.Find(e => e.Slot == meshItem.Slot);
            if (entry == null)
            {
                entry = new MeshAttributeEntry { Slot = meshItem.Slot, Attribute = new CharacterCreatorAttribute_Mesh() };
                CharacterCreatorAttributes.MeshEntries.Add(entry);
            }

            var attribute = entry.Attribute;

            // 实际设置网格资产（对应 UE5 MeshAttributes 拷贝）：按路径加载 SkinnedModel
            if (!string.IsNullOrEmpty(meshItem.MeshPath))
            {
                attribute.Mesh = Content.Load<SkinnedModel>(meshItem.MeshPath);
            }
            if (!string.IsNullOrEmpty(meshItem.Mesh1PPath))
            {
                attribute.Mesh1P = Content.Load<SkinnedModel>(meshItem.Mesh1PPath);
            }

            // 路径本身也要留下：外观数据要进 JSON 存档（读档时按路径重新解析资产），
            // 只留 SkinnedModel 引用会在下一次会话变成 null（外观被重掷的直接原因之一）。
            attribute.MeshPath = meshItem.MeshPath ?? "";
            attribute.Mesh1PPath = meshItem.Mesh1PPath ?? "";

            // 附着与动画设置
            attribute.bUseLeaderPose = meshItem.bUseLeaderPose;
            attribute.MeshAnimBPPath = meshItem.MeshAnimBPPath;
            attribute.MeshAttachSocket = meshItem.MeshAttachSocket;
            attribute.MeshAttachOffset = meshItem.MeshAttachOffset;

            // 转换材质选项为材质
            attribute.MeshMaterials.Clear();
            foreach (var matOption in meshItem.MaterialOptions)
            {
                if (matOption != null)
                {
                    attribute.MeshMaterials.Add(matOption.GetDefaultMaterial());
                }
            }

            // 转换 Morph
            attribute.Morphs.Clear();
            foreach (var morph in meshItem.Morphs)
            {
                if (morph != null)
                {
                    attribute.Morphs.Add(morph);
                }
            }

            // 隐藏 slot 集合（对应 UE5 HideSlots）
            attribute.HideSlots.Clear();
            foreach (var hideSlot in meshItem.HideSlots)
            {
                if (hideSlot.IsValid()) attribute.HideSlots.Add(hideSlot);
            }

            // 嵌套标量/向量选项：默认值登记进全局值表，供材质参数/Morph 引用
            foreach (var scalarOption in meshItem.ScalarOptions)
            {
                if (scalarOption != null && scalarOption.ScalarTagID.IsValid())
                {
                    SetCreatorScalarValue(scalarOption.ScalarTagID, scalarOption.DefaultValue);
                }
            }
            foreach (var vectorOption in meshItem.VectorOptions)
            {
                if (vectorOption != null && vectorOption.VectorTagID.IsValid()
                    && !CharacterCreatorAttributes.VectorEntries.Exists(e => e != null && e.Tag == vectorOption.VectorTagID))
                {
                    // 未设置时取色板首色作为默认值（对应 UE5 色板默认）
                    var swatch = CharacterCreatorFactory.LoadColorSwatch(vectorOption.AvailableOptionsSwatchPath);
                    SetCreatorVectorValue(vectorOption.VectorTagID, swatch != null && swatch.Colors.Count > 0 ? swatch.Colors[0] : Color.White);
                }
            }
        }

        /// <summary>设置创建器数据毛发</summary>
        public virtual void SetCreatorDataGroom(CharacterCreatorItem_Groom groomItem)
        {
            if (groomItem == null || CharacterCreatorAttributes == null) return;

            var entry = CharacterCreatorAttributes.GroomEntries.Find(e => e.Slot == groomItem.Slot);
            if (entry == null)
            {
                entry = new GroomAttributeEntry { Slot = groomItem.Slot, Attribute = new CharacterCreatorAttribute_Groom() };
                CharacterCreatorAttributes.GroomEntries.Add(entry);
            }

            entry.Attribute.GroomAssetPath = groomItem.GroomAssetPath;
            entry.Attribute.GroomBindingAssetPath = groomItem.GroomBindingAssetPath;
            entry.Attribute.GroomMaterials.Clear();
            foreach (var matOption in groomItem.GroomMaterials)
            {
                if (matOption != null)
                {
                    entry.Attribute.GroomMaterials.Add(matOption.GetDefaultMaterial());
                }
            }
        }

        /// <summary>设置创建器标量值</summary>
        public virtual void SetCreatorScalarValue(GameplayTag tagID, float newValue)
        {
            if (CharacterCreatorAttributes == null) return;
            var entry = CharacterCreatorAttributes.ScalarEntries.Find(e => e.Tag == tagID);
            if (entry == null)
            {
                entry = new ScalarValueEntry { Tag = tagID, Value = newValue };
                CharacterCreatorAttributes.ScalarEntries.Add(entry);
            }
            else
            {
                entry.Value = newValue;
            }
        }

        /// <summary>设置创建器向量值</summary>
        public virtual void SetCreatorVectorValue(GameplayTag tagID, Color newValue)
        {
            if (CharacterCreatorAttributes == null) return;
            var entry = CharacterCreatorAttributes.VectorEntries.Find(e => e.Tag == tagID);
            if (entry == null)
            {
                entry = new VectorValueEntry { Tag = tagID, Value = newValue };
                CharacterCreatorAttributes.VectorEntries.Add(entry);
            }
            else
            {
                entry.Value = newValue;
            }
        }
    }
}
