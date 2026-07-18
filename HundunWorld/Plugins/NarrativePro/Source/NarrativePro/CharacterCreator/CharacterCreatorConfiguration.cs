using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.CharacterCreator.Items;
using NarrativePro.CharacterCreator.Options;
using NarrativePro.Items;

namespace NarrativePro.CharacterCreator
{
    /// <summary>
    /// 角色创建器页面。对应 UE5 UCharacterCreatorPage。
    /// 由多个分区组成，选择页面时相机插值到指定变换。
    /// </summary>
    [Serializable]
    public class CharacterCreatorPage
    {
        /// <summary>页面标签 ID</summary>
        public GameplayTag PageID = GameplayTag.None;

        /// <summary>选择此页面时相机的目标变换</summary>
        public Transform PageCameraTransform = Transform.Identity;

        /// <summary>页面显示名</summary>
        public string PageTitleText = "";

        /// <summary>页面包含的分区列表</summary>
        public List<CharacterCreatorSection> PageSections = new List<CharacterCreatorSection>();
    }

    /// <summary>
    /// 角色创建器分区。对应 UE5 UCharacterCreatorSection。
    /// 由多个选项组成。
    /// </summary>
    [Serializable]
    public class CharacterCreatorSection
    {
        /// <summary>分区标签 ID</summary>
        public GameplayTag SectionID = GameplayTag.None;

        /// <summary>分区显示名</summary>
        public string SectionDisplayName = "";

        /// <summary>此分区可编辑的选项列表</summary>
        public List<CharacterCreatorOption> Options = new List<CharacterCreatorOption>();
    }

    /// <summary>
    /// 角色创建器表单。对应 UE5 UCharacterCreatorForm。
    /// 定义一种角色创建器选项集（男性、女性、哥布林等）及默认外观。
    /// </summary>
    [Serializable]
    public class CharacterCreatorForm
    {
        /// <summary>表单显示名（Male/Female/Dog/Cat 等）</summary>
        public string FormDisplayName = "";

        /// <summary>表单标签 ID（Narrative.CharacterCreator.Forms）</summary>
        public GameplayTag FormTag = GameplayTag.None;

        /// <summary>此表单使用的视觉类路径</summary>
        public string CharacterVisualClassPath = "";

        /// <summary>基础骨骼网格资源路径（通常隐藏）</summary>
        public string BaseMeshPath = "";

        /// <summary>是否隐藏基础网格</summary>
        public bool bHideBaseMesh = true;

        /// <summary>默认徒手动画蓝图路径</summary>
        public string DefaultCharacterUnarmedAnimPath = "";

        /// <summary>表单包含的页面列表</summary>
        public List<CharacterCreatorPage> CharacterCreatorPages = new List<CharacterCreatorPage>();

        /// <summary>默认网格项映射（按 slot 标签索引）</summary>
        public List<DefaultMeshEntry> DefaultCharacterCreatorMeshes = new List<DefaultMeshEntry>();

        /// <summary>默认毛发项映射（按 slot 标签索引）</summary>
        public List<DefaultGroomEntry> DefaultCharacterCreatorGrooms = new List<DefaultGroomEntry>();
    }

    /// <summary>默认网格项条目</summary>
    [Serializable]
    public class DefaultMeshEntry
    {
        public GameplayTag Slot;
        public CharacterCreatorItem_Mesh Mesh;
    }

    /// <summary>默认毛发项条目</summary>
    [Serializable]
    public class DefaultGroomEntry
    {
        public GameplayTag Slot;
        public CharacterCreatorItem_Groom Groom;
    }

    /// <summary>
    /// 角色创建器配置。对应 UE5 UCharacterCreatorConfiguration。
    /// 定义角色创建器的所有可配置设置。
    /// </summary>
    [Serializable]
    public class CharacterCreatorConfiguration
    {
        /// <summary>角色创建器表单列表</summary>
        public List<CharacterCreatorForm> CharacterCreatorForms = new List<CharacterCreatorForm>();
    }
}
