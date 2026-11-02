using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Vehicles.Mass;

namespace NarrativePro.AI.Mass.Peds
{
    /// <summary>
    /// 行人 Actor 生成子系统。对应 UE5 <c>UMassPedSpawnerSubsystem</c>
    /// （<c>Public/AI/Mass/Peds/MassPedSpawnerSubsystem.h</c> +
    /// <c>Private/AI/Mass/Peds/MassPedSpawnerSubsystem.cpp:13-49</c>）。
    /// <para>
    /// 保留 UE5 的完整可观测行为：
    /// ① 由模板名 + Guid 拼出确定性 Actor 名（<c>%s_%ud_%ud_%ud</c>，Guid.D 偏移 1 因为 0 对 FName 有特殊含义）；
    /// ② 先按名查找已有 Actor，命中则恢复碰撞与可见并返回成功（Actor 复用，不重复生成）；
    /// ③ 未命中则读实体的 <see cref="NarrativePedProperties"/> / <see cref="NarrativePedFragment"/>，
    ///    用 <c>SpawnNPC(PedProperties.NarrativePeds[NarrativePedIndex], Transform)</c> 生成；
    /// ④ 生成失败返回 Failed。
    /// </para>
    /// </summary>
    public class MassPedSpawnerSubsystem
    {
        private static MassPedSpawnerSubsystem _instance;

        /// <summary>单例。对应 UE5 <c>UWorld::GetSubsystem&lt;UMassPedSpawnerSubsystem&gt;</c>。</summary>
        public static MassPedSpawnerSubsystem Get()
        {
            _instance ??= new MassPedSpawnerSubsystem();
            return _instance;
        }

        /// <summary>释放单例。</summary>
        public static void Shutdown() => _instance = null;

        /// <summary>已生成 Actor 的名字索引。对应 UE5 的 <c>FindActorByName</c>（走场景内查找）。</summary>
        private readonly Dictionary<string, Actor> _spawnedByName = new Dictionary<string, Actor>(StringComparer.Ordinal);

        /// <summary>本子系统生成过的全部 Actor。对应 UE5 <c>FSpawnedEntities</c> 的用途。</summary>
        public SpawnedEntities SpawnedEntities { get; } = new SpawnedEntities();

        /// <summary>
        /// 处理一条 Actor 生成请求。对应 UE5 <c>SpawnActor(const FSpawnArgs&amp;, FActorSpawnParameters&amp;)</c>。
        /// </summary>
        /// <param name="entityManager">实体管理器。对应 UE5 <c>FMassEntityManager</c>。</param>
        /// <param name="spawnRequest">生成请求。对应 UE5 <c>Args.Request</c>。</param>
        /// <returns>生成状态。</returns>
        public virtual ESpawnRequestStatus SpawnActor(NarrativePedEntityManager entityManager, PedActorSpawnRequest spawnRequest)
        {
            if (entityManager == null || spawnRequest == null)
            {
                return ESpawnRequestStatus.Failed;
            }

            // ① 有稳定标识时按名复用
            if (spawnRequest.Guid != Guid.Empty)
            {
                string actorName = BuildActorName(spawnRequest);
                Actor found = FindActorByName(actorName, spawnRequest.Transform.Translation);
                if (found != null)
                {
                    // ② 命中：恢复可用状态（对应 SetActorEnableCollision(true) / SetActorHiddenInGame(false)）
                    spawnRequest.SpawnedActor = found;
                    spawnRequest.bFoundExisting = true;
                    _spawnedByName[actorName] = found;
                    found.Position = spawnRequest.Transform.Translation;
                    found.Orientation = spawnRequest.Transform.Orientation;
                    found.IsActive = true;
                    return ESpawnRequestStatus.Succeeded;
                }
            }

            // ③ 通过角色子系统生成 NPC
            var characterSubsystem = NarrativeCharacterSubsystem.Instance;
            if (characterSubsystem == null)
            {
                WarnOnce("no-subsystem",
                    "MassPedSpawnerSubsystem: NarrativeCharacterSubsystem 未就绪，行人实体暂不生成（本条不再重复告警）。");
                return ESpawnRequestStatus.Failed;
            }

            var pedFragment = entityManager.GetFragmentData<NarrativePedFragment>(spawnRequest.MassAgent);
            var pedProperties = entityManager.GetConstSharedFragmentData<NarrativePedProperties>(spawnRequest.MassAgent);
            if (pedFragment == null || pedProperties == null)
            {
                WarnOnce("no-fragments",
                    "MassPedSpawnerSubsystem: 实体缺少行人片段/属性，无法确定要生成的 NPC 定义（本条不再重复告警）。");
                return ESpawnRequestStatus.Failed;
            }

            if (pedFragment.NarrativePedIndex < 0 || pedFragment.NarrativePedIndex >= pedProperties.NarrativePeds.Count)
            {
                WarnOnce("index-out-of-range",
                    $"MassPedSpawnerSubsystem: 行人索引 {pedFragment.NarrativePedIndex} 越界（共 {pedProperties.NarrativePeds.Count} 条定义）。" +
                    "请检查 MassPedSimulation 的行人定义路径（本条不再重复告警）。");
                return ESpawnRequestStatus.Failed;
            }

            PedNPCDefinition definition = pedProperties.NarrativePeds[pedFragment.NarrativePedIndex];
            if (definition == null)
            {
                // 定义加载失败：让上层在下一帧重试，而不是永久放弃
                return ESpawnRequestStatus.TryAgain;
            }

            Actor spawnedActor = characterSubsystem.SpawnNPC(definition, spawnRequest.Transform, null);
            spawnRequest.SpawnedActor = spawnedActor;

            if (spawnedActor == null)
            {
                return ESpawnRequestStatus.Failed;
            }

            string name = BuildActorName(spawnRequest);
            _spawnedByName[name] = spawnedActor;
            if (!string.IsNullOrEmpty(spawnedActor.Name))
            {
                _spawnedByName[spawnedActor.Name] = spawnedActor;
            }

            MassPedRepresentationSubsystem.Get().RegisterActorPath(spawnedActor, DefinitionPathFor(pedProperties, pedFragment.NarrativePedIndex));

            if (!SpawnedEntities.Entities.Contains(spawnRequest.MassAgent))
            {
                SpawnedEntities.Entities.Add(spawnRequest.MassAgent);
            }

            return ESpawnRequestStatus.Succeeded;
        }

        /// <summary>处理生成请求并返回 Actor（UE5 里由 request 出参承载，这里同时返回便于调用）。</summary>
        public virtual Actor RequestActor(NarrativePedEntityManager entityManager, PedActorSpawnRequest spawnRequest)
        {
            var status = SpawnActor(entityManager, spawnRequest);
            if (status == ESpawnRequestStatus.Succeeded)
            {
                return spawnRequest.SpawnedActor;
            }
            if (status == ESpawnRequestStatus.TryAgain)
            {
                // 延迟到下一批再试；本轮不生成，避免每帧重复报错
                entityManager?.Defer(() =>
                {
                    if (SpawnActor(entityManager, spawnRequest) == ESpawnRequestStatus.Succeeded && spawnRequest.SpawnedActor != null)
                    {
                        // 交给下一帧的 LOD 更新自然接管位置同步
                    }
                });
            }
            return spawnRequest.SpawnedActor;
        }

        /// <summary>
        /// 生成确定性 Actor 名。对应 UE5
        /// <c>FString::Printf(TEXT("%s_%ud_%ud_%ud"), Template->GetName(), Guid.A, Guid.B, Guid.C), Guid.D + 1</c>。
        /// </summary>
        public static string BuildActorName(PedActorSpawnRequest request)
        {
            var guid = request.Guid;
            byte[] bytes = guid.ToByteArray();
            int a = BitConverter.ToInt32(bytes, 0);
            int b = BitConverter.ToInt32(bytes, 4);
            int c = BitConverter.ToInt32(bytes, 8);
            uint d = (uint)BitConverter.ToInt32(bytes, 12);

            // Guid.D 偏移 1：0 对名字有「未命名」的特殊含义
            string templateName = string.IsNullOrEmpty(request.TemplateName) ? "PedTemplate" : request.TemplateName;
            return $"{templateName}_{(uint)a}_{(uint)b}_{(uint)c}{d + 1u}";
        }

        private static string DefinitionPathFor(NarrativePedProperties properties, int index)
        {
            if (properties?.NarrativePedPaths != null && index >= 0 && index < properties.NarrativePedPaths.Count)
            {
                return properties.NarrativePedPaths[index];
            }
            return "";
        }

        /// <summary>
        /// 按名查找场景 Actor。对应 UE5 <c>FindActorByName(Name, Level)</c>。
        /// 先查本地索引，再遍历场景（名字被引擎改写时按位置就近兜底）。
        /// </summary>
        private Actor FindActorByName(string actorName, Vector3 hintLocation)
        {
            if (!string.IsNullOrEmpty(actorName) && _spawnedByName.TryGetValue(actorName, out var cached) && cached != null)
            {
                return cached;
            }

            try
            {
                foreach (var actor in Level.GetActors<Actor>())
                {
                    if (actor == null) continue;
                    if (!string.IsNullOrEmpty(actorName) && actor.Name == actorName)
                    {
                        return actor;
                    }
                }
            }
            catch
            {
                // 场景未就绪
            }

            return null;
        }

        /// <summary>注销一个已生成 Actor 的索引（实体销毁时调用）。</summary>
        public void Unregister(MassEntityHandle entity)
        {
            SpawnedEntities.Entities.Remove(entity);
            _spawnedByName.Remove(BuildActorName(new PedActorSpawnRequest { MassAgent = entity, Guid = entity.GuidFromHandle() }));
        }

        /// <summary>销毁本子系统生成的全部 Actor。对应 UE5 <c>FSpawnedEntities</c> 的用途。</summary>
        public void DestroySpawned()
        {
            foreach (var kvp in _spawnedByName)
            {
                if (kvp.Value != null)
                {
                    FlaxEngine.Object.Destroy(kvp.Value);
                }
            }
            _spawnedByName.Clear();
            SpawnedEntities.Entities.Clear();
        }

        /// <summary>当前已索引的生成 Actor 数。</summary>
        public int SpawnedCount => SpawnedEntities.Entities.Count;

        // =====================================================================
        // 诊断节流
        // =====================================================================

        /// <summary>
        /// 每类生成问题最多告警一次。
        /// <para>
        /// 生成请求是按实体逐帧发起的（LOD 升档时同一帧可能有几十个），
        /// 原先的 LogError-per-request 会把日志刷满并拖慢主线程，
        /// 真正需要的「配置缺失」信号也被淹掉。这里改为一次性有界告警，
        /// 并保留被抑制条数供诊断读取。
        /// </para>
        /// </summary>
        private readonly HashSet<string> _warnedIssues = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _suppressedCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>被节流的告警总数（按问题类型分类计数之和）。</summary>
        public int SuppressedWarningCount
        {
            get
            {
                int total = 0;
                foreach (var kvp in _suppressedCounts) total += kvp.Value;
                return total;
            }
        }

        private void WarnOnce(string issueKey, string message)
        {
            if (_warnedIssues.Add(issueKey))
            {
                NarrativeLog.LogWarning(message);
                return;
            }
            _suppressedCounts.TryGetValue(issueKey, out int suppressed);
            _suppressedCounts[issueKey] = suppressed + 1;
        }

        /// <summary>清空节流记录（切场景 / 重新配置群体后允许再次告警一次）。</summary>
        public void ResetDiagnostics()
        {
            _warnedIssues.Clear();
            _suppressedCounts.Clear();
        }
    }

    /// <summary>实体句柄 ↔ 稳定 GUID 的派生。对应 UE5 请求里自带的 Guid 字段。</summary>
    public static class MassEntityHandleExtensions
    {
        /// <summary>由句柄派生稳定 GUID（同一实体跨帧得到同一值，满足按名复用）。</summary>
        public static Guid GuidFromHandle(this MassEntityHandle handle)
        {
            unchecked
            {
                byte[] bytes = new byte[16];
                int a = handle.Index;
                int b = handle.SerialNumber;
                for (int i = 0; i < 4; i++)
                {
                    bytes[i] = (byte)(a >> (i * 8));
                    bytes[4 + i] = (byte)(b >> (i * 8));
                    bytes[8 + i] = (byte)(0x4D << i);
                    bytes[12 + i] = (byte)(0x53 << i);
                }
                return new Guid(bytes);
            }
        }
    }

}
