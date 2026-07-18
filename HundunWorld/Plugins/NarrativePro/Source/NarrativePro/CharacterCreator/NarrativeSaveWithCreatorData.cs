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

        /// <summary>设置创建器数据网格</summary>
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

            // 更新网格属性
            entry.Attribute.bUseLeaderPose = meshItem.bUseLeaderPose;
            entry.Attribute.MeshAnimBPPath = meshItem.MeshAnimBPPath;

            // 转换材质选项为材质
            entry.Attribute.MeshMaterials.Clear();
            foreach (var matOption in meshItem.MaterialOptions)
            {
                if (matOption != null)
                {
                    entry.Attribute.MeshMaterials.Add(matOption.GetDefaultMaterial());
                }
            }

            // 转换 Morph
            entry.Attribute.Morphs.Clear();
            foreach (var morph in meshItem.Morphs)
            {
                if (morph != null)
                {
                    entry.Attribute.Morphs.Add(morph);
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
