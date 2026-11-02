using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.AI.Mass.Peds
{
    /// <summary>
    /// 行人可视化特质。对应 UE5 <c>UNarrativeVisualizationTrait</c>
    /// （<c>Public/AI/Mass/Peds/NarrativeVisualizationTrait.h</c> +
    /// <c>Private/AI/Mass/Peds/NarrativeVisualizationTrait.cpp</c>）。
    /// <para>
    /// 保留 UE5 <c>BuildTemplate</c>（:35-88）的可观测行为：
    /// ① 先由基类群集可视化特质挂好变换/速度/半径/Actor 片段与表现片段；
    /// ② 加 <see cref="NarrativePedFragment"/>；
    /// ③ 把每条 NPC 定义的 <c>BakedPedMesh</c> 注册成可视化描述并向表现子系统换取句柄；
    /// ④ 把带句柄的 <see cref="NarrativePedProperties"/> 作为常量共享片段写入模板；
    /// ⑤ <c>ValidateParams</c>（:92-106）：没有配置任何行人定义时报错。
    /// </para>
    /// </summary>
    [Serializable]
    public class NarrativeVisualizationTrait : PedEntityTraitBase
    {
        /// <summary>行人属性（NPC 定义路径表）。对应 UE5 <c>PedProperties</c>。</summary>
        public NarrativePedProperties PedProperties = new NarrativePedProperties();

        /// <summary>低精度合批可视化的网格描述参数。对应 UE5 <c>StaticMeshInstanceDesc</c>。</summary>
        public StaticMeshInstanceVisualizationDesc StaticMeshInstanceDesc = new StaticMeshInstanceVisualizationDesc();

        /// <summary>是否注册静态网格描述。对应 UE5 <c>bRegisterStaticMeshDesc = false</c>（本移植需要句柄，故默认开）。</summary>
        public bool bRegisterStaticMeshDesc = true;

        /// <summary>
        /// 清洗表现参数。对应 UE5 <c>SanitizeParams</c>（:26-31）：
        /// UE5 特意不调用基类清洗，因为静态网格描述稍后才由 NPC 定义决定。
        /// </summary>
        public virtual void SanitizeParams(PedRepresentationParameters inOutParams, bool bStaticMeshDeterminedInvalid = false)
        {
            // 与 UE5 一致：不清空，稍后由 BuildTemplate 填充
        }

        public override void BuildTemplate(PedEntityTemplateBuildContext buildContext, NarrativePedEntityManager world)
        {
            BuildTemplateInternal(buildContext, world, MassPedRepresentationSubsystem.Get());
        }

        /// <summary>带表现子系统入口的重载，便于测试注入。</summary>
        public void BuildTemplateInternal(PedEntityTemplateBuildContext buildContext, NarrativePedEntityManager world,
            MassPedRepresentationSubsystem representationSubsystem)
        {
            if (buildContext == null) return;

            // ① 群集可视化基类特质提供的片段集合
            new MassCrowdVisualizationTrait().BuildTemplate(buildContext, world);

            // ② 行人专有片段
            buildContext.AddFragment<NarrativePedFragment>();

            var pedPropertiesCopy = PedProperties;
            pedPropertiesCopy.VisualizationHandles = new List<int>();
            pedPropertiesCopy.ResolveDefinitions();

            // ③ 把每条 NPC 定义的外观注册进表现子系统，换取可视化句柄
            if (representationSubsystem == null && !buildContext.bInspectingData)
            {
                representationSubsystem = MassPedRepresentationSubsystem.Get();
            }

            if (representationSubsystem != null)
            {
                foreach (var pedDefinition in pedPropertiesCopy.NarrativePeds)
                {
                    // 对应 UE5 `if (PedNPCDefinition.IsNull()) continue;`
                    if (pedDefinition == null)
                    {
                        pedPropertiesCopy.VisualizationHandles.Add(-1);
                        continue;
                    }

                    var meshDesc = new StaticMeshInstanceVisualizationMeshDesc
                    {
                        // 对应 UE5 `MeshDesc.Mesh = PedNPCDefinition.LoadSynchronous()->BakedPedMesh.LoadSynchronous()`
                        MeshPath = pedDefinition.BakedPedMeshPath,
                        SkinnedMeshPath = pedDefinition.PedMeshPath,
                        AnimInstancePath = pedDefinition.PedMeshAnimBPPath
                    };

                    var visualizationDesc = StaticMeshInstanceDesc.Clone();
                    visualizationDesc.Meshes.Add(meshDesc);

                    pedPropertiesCopy.VisualizationHandles.Add(representationSubsystem.FindOrAddStaticMeshDesc(visualizationDesc));
                }
            }

            // ④ 常量共享片段
            if (world != null)
            {
                buildContext.AddConstSharedFragment(world.GetOrCreateConstSharedFragment(pedPropertiesCopy));
            }
            else
            {
                buildContext.AddConstSharedFragment(pedPropertiesCopy);
            }
        }

        /// <summary>
        /// 参数校验。对应 UE5 <c>ValidateParams</c>（:92-106）：未定义任何行人时视为配置错误。
        /// </summary>
        public virtual bool ValidateParams()
        {
            if (PedProperties == null || PedProperties.NarrativePedPaths == null || PedProperties.NarrativePedPaths.Count == 0)
            {
                NarrativeLog.LogError("NarrativeVisualizationTrait: 未定义任何 Narrative Peds，无法为实体生成可视化。");
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// 群集可视化特质。对应 UE5 <c>UMassCrowdVisualizationTrait</c>（Mass 插件提供，本仓库需自建其可观测结果）：
    /// 挂变换/速度/半径/Actor/表现/加速片段。
    /// </summary>
    [Serializable]
    public class MassCrowdVisualizationTrait : PedEntityTraitBase
    {
        public override void BuildTemplate(PedEntityTemplateBuildContext buildContext, NarrativePedEntityManager world)
        {
            if (buildContext == null) return;

            buildContext.AddFragment<PedTransformFragment>();
            buildContext.AddFragment<PedVelocityFragment>();
            buildContext.AddFragment<AgentRadiusFragment>(() => new AgentRadiusFragment { Radius = 40f });
            buildContext.AddFragment<PedActorFragment>();
            buildContext.AddFragment<PedRepresentationFragment>();
            buildContext.AddFragment<PedAccelerationFragment>();
        }
    }

    /// <summary>
    /// 表现参数。对应 UE5 <c>FMassRepresentationParameters</c> 的可观测子集。
    /// </summary>
    [Serializable]
    public class PedRepresentationParameters
    {
        /// <summary>Actor 表现管理类。对应 UE5 <c>RepresentationActorManagementClass</c>。</summary>
        public MassNarrativePedRepresentationActorManagement ActorManagement = new MassNarrativePedRepresentationActorManagement();

        /// <summary>低精度静态网格可视化描述。对应 UE5 <c>StaticMeshInstanceDesc</c>。</summary>
        public StaticMeshInstanceVisualizationDesc StaticMeshInstanceDesc = new StaticMeshInstanceVisualizationDesc();
    }

    /// <summary>
    /// 静态网格实例可视化描述。对应 UE5 <c>FStaticMeshInstanceVisualizationDesc</c>。
    /// </summary>
    [Serializable]
    public class StaticMeshInstanceVisualizationDesc
    {
        /// <summary>候选网格描述。对应 UE5 <c>Meshes</c>。</summary>
        public List<StaticMeshInstanceVisualizationMeshDesc> Meshes = new List<StaticMeshInstanceVisualizationMeshDesc>();

        /// <summary>LOD 偏置。</summary>
        public int LodBias = 0;

        /// <summary>是否投影。</summary>
        public bool bCastShadows = true;

        /// <summary>复制一份（UE5 里是值语义结构体）。</summary>
        public StaticMeshInstanceVisualizationDesc Clone()
        {
            var copy = new StaticMeshInstanceVisualizationDesc
            {
                LodBias = LodBias,
                bCastShadows = bCastShadows
            };
            foreach (var mesh in Meshes)
            {
                copy.Meshes.Add(mesh.Clone());
            }
            return copy;
        }

        /// <summary>用于句柄去重的稳定签名。</summary>
        public string GetSignature()
        {
            var builder = new System.Text.StringBuilder();
            builder.Append(LodBias).Append('|').Append(bCastShadows).Append('|');
            foreach (var mesh in Meshes)
            {
                builder.Append(mesh.MeshPath).Append(',').Append(mesh.SkinnedMeshPath).Append(';');
            }
            return builder.ToString();
        }
    }

    /// <summary>
    /// 单个网格描述。对应 UE5 <c>FMassStaticMeshInstanceVisualizationMeshDesc</c>。
    /// </summary>
    [Serializable]
    public class StaticMeshInstanceVisualizationMeshDesc
    {
        /// <summary>烘焙静态网格路径。对应 UE5 <c>Mesh</c>（UStaticMesh*）。</summary>
        public string MeshPath = "";

        /// <summary>高精度骨骼网格路径（本移植扩展：Flax 用 SkinnedModel 承载）。</summary>
        public string SkinnedMeshPath = "";

        /// <summary>动画实例脚本路径（对应 UE5 <c>PedMeshAnimBP</c>）。</summary>
        public string AnimInstancePath = "";

        public StaticMeshInstanceVisualizationMeshDesc Clone()
        {
            return new StaticMeshInstanceVisualizationMeshDesc
            {
                MeshPath = MeshPath,
                SkinnedMeshPath = SkinnedMeshPath,
                AnimInstancePath = AnimInstancePath
            };
        }
    }

    /// <summary>
    /// 行人 Actor 表现管理。对应 UE5 <c>UMassNarrativePedRepresentationActorManagement</c>
    /// （<c>Private/AI/Mass/Peds/MassNarrativePedRepresentationActorManagement.cpp</c>）。
    /// </summary>
    public class MassNarrativePedRepresentationActorManagement
    {
        /// <summary>
        /// Actor 生成后的处理：把实体上的行人索引/种子/定义回写到生成的 NPC 角色。
        /// 对应 UE5 <c>OnPostActorSpawn</c>（:20-40）。
        /// </summary>
        public virtual EMassActorSpawnRequestAction OnPostActorSpawn(PedActorSpawnRequest spawnRequest, NarrativePedEntityManager entityManager)
        {
            if (spawnRequest == null || spawnRequest.SpawnedActor == null || entityManager == null)
            {
                return EMassActorSpawnRequestAction.Keep;
            }

            var pedProperties = entityManager.GetConstSharedFragmentData<NarrativePedProperties>(spawnRequest.MassAgent);
            var pedFragment = entityManager.GetFragmentData<NarrativePedFragment>(spawnRequest.MassAgent);
            if (pedProperties == null || pedFragment == null)
            {
                return EMassActorSpawnRequestAction.Keep;
            }

            PedNPCDefinition definition = pedFragment.NarrativePedIndex >= 0
                && pedFragment.NarrativePedIndex < pedProperties.NarrativePeds.Count
                    ? pedProperties.NarrativePeds[pedFragment.NarrativePedIndex]
                    : null;

            var npcCharacter = spawnRequest.SpawnedActor.GetScript<NarrativePro.UnrealFramework.NarrativeNPCCharacter>();
            if (npcCharacter != null)
            {
                // 对应 UE5 NPCCharacter->SetRandomSeed / SetNPCDefinition
                npcCharacter.SetRandomSeed(pedFragment.NarrativePedSeed);
                if (definition != null)
                {
                    npcCharacter.SetNPCDefinition(definition);
                }
            }

            return EMassActorSpawnRequestAction.Keep;
        }

        /// <summary>
        /// 启用/停用 Actor 表现：连子 Actor 一起关碰撞、隐藏、关 tick。
        /// 对应 UE5 <c>SetActorEnabled</c>（:42-64，UE5 里作为延迟命令推送）。
        /// </summary>
        public virtual void SetActorEnabled(EMassActorEnabledType enabledType, Actor actor, int entityIndex, NarrativePedEntityManager entityManager)
        {
            if (actor == null) return;
            bool bEnabled = enabledType != EMassActorEnabledType.Disabled;

            // 结构性切换在 UE5 中被延迟以避免回调在游戏线程外触发 Chaos 断言；这里同样走延迟命令
            void Apply()
            {
                ApplyRecursive(actor, bEnabled);
            }

            if (entityManager != null)
            {
                entityManager.Defer(Apply);
            }
            else
            {
                Apply();
            }
        }

        private static void ApplyRecursive(Actor actor, bool bEnabled)
        {
            if (actor == null) return;

            // Flax 中 Actor.IsActive 同时关掉渲染、脚本 tick 与物理参与，
            // 等价于 UE5 的 SetActorHiddenInGame + SetActorEnableCollision + SetActorTickEnabled 三连
            try
            {
                actor.IsActive = bEnabled;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"切换 Actor {actor.Name} 激活状态失败：{ex.Message}");
            }

            foreach (var child in actor.GetChildren<Actor>())
            {
                if (child == null || child == actor) continue;
                ApplyRecursive(child, bEnabled);
            }
        }
    }

    /// <summary>Actor 生成后的处置。对应 UE5 <c>EMassActorSpawnRequestAction</c>。</summary>
    public enum EMassActorSpawnRequestAction
    {
        Keep = 0,
        DeleteOnRelease = 1,
        Release = 2
    }

    /// <summary>Actor 启用状态。对应 UE5 <c>EMassActorEnabledType</c>。</summary>
    public enum EMassActorEnabledType
    {
        Fully = 0,
        Partially = 1,
        Disabled = 2
    }
}
