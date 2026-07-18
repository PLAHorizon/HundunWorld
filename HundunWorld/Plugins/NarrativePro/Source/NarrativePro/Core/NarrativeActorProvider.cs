using System;
using FlaxEngine;
using NarrativePro.AI;
using NarrativePro.Items;

namespace NarrativePro.Core
{
    /// <summary>
    /// 实例化 Actor 提供者包装。对应 UE5 FInstancedActorProvider。
    /// </summary>
    [Serializable]
    public class InstancedActorProvider
    {
        public NarrativeActorProvider Provider;
    }

    /// <summary>
    /// 实例化 Transform 提供者包装。对应 UE5 FInstancedTransformProvider。
    /// </summary>
    [Serializable]
    public class InstancedTransformProvider
    {
        public NarrativeTransformProvider Provider;
    }

    /// <summary>
    /// 提供者基类。对应 UE5 UNarrativeProviderBase。
    /// 基于 EQSContexts 概念，提供 Actor/Transform 查询。
    /// </summary>
    [Serializable]
    public abstract class NarrativeProviderBase
    {
        /// <summary>返回提供者描述文本。</summary>
        public virtual string GetDescription()
        {
            return GetType().Name;
        }
    }

    /// <summary>
    /// Transform 提供者基类。对应 UE5 UNarrativeTransformProvider。
    /// </summary>
    [Serializable]
    public abstract class NarrativeTransformProvider : NarrativeProviderBase
    {
        /// <summary>提供 Transform。</summary>
        public abstract Transform ProvideTransform(object worldContextObject);

        public override string GetDescription()
        {
            return "Transform Provider";
        }
    }

    /// <summary>
    /// Actor 提供者基类。对应 UE5 UNarrativeActorProvider。
    /// </summary>
    [Serializable]
    public abstract class NarrativeActorProvider : NarrativeTransformProvider
    {
        /// <summary>提供 Actor。</summary>
        public abstract Actor ProvideActor(object worldContextObject);

        /// <summary>默认使用 ProvideActor 的 Transform。</summary>
        public override Transform ProvideTransform(object worldContextObject)
        {
            var actor = ProvideActor(worldContextObject);
            return actor != null ? actor.Transform : Transform.Identity;
        }

        public override string GetDescription()
        {
            return "Actor Provider";
        }
    }

    /// <summary>
    /// NPC 提供者。对应 UE5 UNarrativeActorProvider_NPC。
    /// 通过 NPCDefinition 查找场景中的 NPC。
    /// </summary>
    [Serializable]
    public class NarrativeActorProvider_NPC : NarrativeActorProvider
    {
        /// <summary>要查找的 NPC 定义。</summary>
        public NPCDefinition NPCDefinition;

        public override Actor ProvideActor(object worldContextObject)
        {
            if (NPCDefinition == null) return null;
            // 通过 NarrativeCharacterSubsystem 查找
            var subsystem = NarrativeCharacterSubsystem.Instance;
            if (subsystem == null) return null;
            var npcs = new System.Collections.Generic.List<Actor>();
            subsystem.FindNPCs(NPCDefinition, npcs);
            return npcs.Count > 0 ? npcs[0] : null;
        }

        public override string GetDescription()
        {
            return $"Find NPC: {NPCDefinition?.NPCName ?? "None"}";
        }
    }

    /// <summary>
    /// 通过 Save GUID 查找 Actor。对应 UE5 UNarrativeActorProvider_GUIDLookup。
    /// </summary>
    [Serializable]
    public class NarrativeActorProvider_GUIDLookup : NarrativeActorProvider
    {
        /// <summary>要查找的 GUID。</summary>
        public Guid GUIDToLookup;

        public override Actor ProvideActor(object worldContextObject)
        {
            // 通过 NarrativeSaveSubsystem 的 GUID 查找表查找场景中的 Actor
            var saveSubsystem = NarrativePro.SaveSystem.Subsystems.NarrativeSaveSubsystem.Instance;
            return saveSubsystem?.LookupActorByGUID(GUIDToLookup);
        }

        public override string GetDescription()
        {
            return $"Find Actor By GUID: {GUIDToLookup}";
        }
    }

    /// <summary>
    /// 通过关卡引用查找 Actor。对应 UE5 UNarrativeActorProvider_LevelReference。
    /// </summary>
    [Serializable]
    public class NarrativeActorProvider_LevelReference : NarrativeActorProvider
    {
        /// <summary>Actor 引用路径。</summary>
        public string SoftActorReference = "";

        public override Actor ProvideActor(object worldContextObject)
        {
            if (string.IsNullOrEmpty(SoftActorReference)) return null;
            // 通过名称在当前场景中查找 Actor（SoftActorReference 视为 Actor 名称）
            foreach (var actor in Level.GetActors<Actor>())
            {
                if (actor != null && actor.Name == SoftActorReference)
                    return actor;
            }
            return null;
        }

        public override string GetDescription()
        {
            return $"Level Actor: {SoftActorReference}";
        }
    }

    /// <summary>
    /// 通过类查找 Actor。对应 UE5 UNarrativeActorProvider_ActorOfClass。
    /// </summary>
    [Serializable]
    public class NarrativeActorProvider_ActorOfClass : NarrativeActorProvider
    {
        /// <summary>要查找的 Actor 类名。</summary>
        public string ActorClassName = "";

        public override Actor ProvideActor(object worldContextObject)
        {
            if (string.IsNullOrEmpty(ActorClassName)) return null;
            var all = Level.GetActors<Actor>();
            foreach (var actor in all)
            {
                if (actor != null && actor.GetType().Name == ActorClassName)
                    return actor;
            }
            return null;
        }

        public override string GetDescription()
        {
            return $"Actor of Class: {ActorClassName}";
        }
    }

    /// <summary>
    /// POI Transform 提供者。对应 UE5 UNarrativeTransformProvider_POI。
    /// </summary>
    [Serializable]
    public class NarrativeTransformProvider_POI : NarrativeTransformProvider
    {
        /// <summary>POI 标签。</summary>
        public GameplayTag POITag = GameplayTag.None;

        public override Transform ProvideTransform(object worldContextObject)
        {
            if (!POITag.IsValid()) return Transform.Identity;
            // 通过 NavigationSubsystem 查找 POI 并返回其快速旅行变换
            var subsystem = NarrativePro.Navigation.NavigationSubsystem.Instance;
            if (subsystem != null && subsystem.GetPointOfInterest(out var poi, POITag))
            {
                return poi.POIFastTravelSpot;
            }
            return Transform.Identity;
        }

        public override string GetDescription()
        {
            return $"POI: {POITag}";
        }
    }

    /// <summary>
    /// 指定 Transform 提供者。对应 UE5 UNarrativeTransformProvider_SpecifiedTransform。
    /// </summary>
    [Serializable]
    public class NarrativeTransformProvider_SpecifiedTransform : NarrativeTransformProvider
    {
        /// <summary>硬编码 Transform。</summary>
        public Transform SpecifiedTransform = Transform.Identity;

        public override Transform ProvideTransform(object worldContextObject)
        {
            return SpecifiedTransform;
        }

        public override string GetDescription()
        {
            return "Specified Transform";
        }
    }
}
