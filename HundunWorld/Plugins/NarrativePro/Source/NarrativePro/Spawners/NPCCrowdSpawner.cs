using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.AI;
using NarrativePro.AI.Mass.Peds;
using NarrativePro.Core;
using NarrativePro.Data;

namespace NarrativePro.Spawners
{
    /// <summary>
    /// 运行期群体生成器（JSON 驱动的 NPC / 行人生成头）。
    /// <para>
    /// 为什么需要它：UE5 侧 NPC 由关卡里摆放的 <see cref="NPCSpawner"/> / AMassSpawner 生成，
    /// 而本游戏的场景资产（Content/Maps/*.scene）从不实例化插件 Actor ——
    /// 于是 <see cref="MassPedSimulation"/>（唯一驱动行人实体管理器的脚本）没有任何创建者，
    /// <see cref="MassPedSpawnerSubsystem"/> 从未运行，NPC 定义/活动调度/目标评分/装备/战利品
    /// 整条通路没有消费者。本脚本把「数据 → 定义 → 生成 → SetNPCDefinition」这条头接到
    /// <see cref="NarrativePro.CommonUI.NarrativeRuntimeBootstrap"/> 的运行期宿主上，
    /// 与本作「玩家由代码生成」（Source/Game/HundunWorldGame.cs、GameSceneInitializer.cs）的既有先例一致。
    /// </para>
    /// <para>
    /// 数据来源：<c>Content/NarrativePro/NPCs/*.json</c>（<see cref="NarrativeNPCDatabase"/>）
    /// 与 <c>Content/NarrativePro/Crowd/*.json</c>（<see cref="NarrativeCrowdConfig"/>）。
    /// 生成分两条通路：交互型 NPC 走 <see cref="NarrativeCharacterSubsystem.SpawnNPC"/>；
    /// 行人走 <see cref="MassPedSimulation.SpawnPedGroup"/>（近景再由生成子系统换成 Actor 并 SetNPCDefinition）。
    /// </para>
    /// </summary>
    public class NPCCrowdSpawner : Script
    {
        /// <summary>单例（运行期宿主上唯一实例）。</summary>
        public static NPCCrowdSpawner Instance { get; private set; }

        /// <summary>群体配置目录（留空则用 <see cref="NarrativeCrowdConfig.DefaultCrowdDirectory"/>）。</summary>
        [Serialize]
        public string CrowdDirectory = NarrativeCrowdConfig.DefaultCrowdDirectory;

        /// <summary>是否同时启动行人群集模拟。</summary>
        [Serialize]
        public bool bSpawnPeds = true;

        /// <summary>是否自动生成（关闭后只能手动调用 <see cref="RunCrowd"/>）。</summary>
        [Serialize]
        public bool bSpawnOnEnable = true;

        /// <summary>宿主/玩家解析的重试节奏（引导可能晚于本脚本创建，必须有限重试而非每帧扫描）。</summary>
        private const float RetryIntervalSeconds = 0.25f;
        private const int MaxRetryAttempts = 80;

        private readonly List<Actor> _spawnedNpcs = new List<Actor>();
        private readonly List<NarrativePro.Vehicles.Mass.MassEntityHandle> _spawnedPeds =
            new List<NarrativePro.Vehicles.Mass.MassEntityHandle>();
        private readonly Dictionary<string, int> _missingDefinitionIds = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly System.Random _random = new System.Random();

        private MassPedSimulation _simulation;
        private float _retryTimer;
        private int _retryAttempts;
        private bool _bRan;

        /// <summary>已生成的交互型 NPC Actor 数。</summary>
        public int SpawnedNpcCount => _spawnedNpcs.Count;

        /// <summary>已生成的行人实体数。</summary>
        public int SpawnedPedCount => _spawnedPeds.Count;

        /// <summary>是否已执行过一轮生成。</summary>
        public bool HasRun => _bRan;

        /// <summary>最近一次阶段（诊断/自证用，配合运行日志读取）。</summary>
        public string LastPhase { get; private set; } = "等待角色子系统";

        /// <summary>本生成器持有的群集模拟（未启用群集时为 null）。</summary>
        public MassPedSimulation PedSimulation => _simulation;

        /// <summary>已生成的 NPC Actor 快照。</summary>
        public IReadOnlyList<Actor> SpawnedNpcs => _spawnedNpcs;

        public override void OnEnable()
        {
            base.OnEnable();
            if (Instance != null && Instance != this)
            {
                NarrativeLog.LogWarning("已存在 NPCCrowdSpawner，当前实例不再生效");
                return;
            }
            Instance = this;
            _bRan = false;
            _retryAttempts = 0;
            _retryTimer = 0f;
        }

        public override void OnDisable()
        {
            if (Instance == this) Instance = null;
            DespawnAll();
            base.OnDisable();
        }

        public override void OnUpdate()
        {
            if (!bSpawnOnEnable || _bRan) return;

            // 时间节拍先行：只有到 0.25s 一次的决策点才消耗重试次数，
            // 80 次 = 约 20 秒窗口（修复：此前 _retryAttempts 每帧自增、在节拍门之前，
            // 窗口实际只有 80 帧 ≈ 1.3 秒，子系统初始化稍慢就永久放弃生成）。
            _retryTimer += (float)Time.DeltaTime;
            if (_retryTimer < RetryIntervalSeconds) return;
            _retryTimer = 0f;

            if (_retryAttempts++ >= MaxRetryAttempts)
            {
                if (_retryAttempts == MaxRetryAttempts)
                {
                    NarrativeLog.LogWarning($"[Spawners] NPCCrowdSpawner：{MaxRetryAttempts} 次重试（约 {MaxRetryAttempts * RetryIntervalSeconds:0.#} 秒）后仍未见角色子系统，群体未生成。");
                }
                return;
            }

            // 角色子系统由运行期引导挂在宿主 Actor 上；它不在时生成无处登记，等下一轮
            if (NarrativeCharacterSubsystem.Instance == null) return;

            RunCrowd();
        }

        /// <summary>
        /// 执行一轮生成：预热 NPC 定义库 → 读群体配置 → 生成 NPC / 行人。
        /// 幂等（重复调用只追加，不会重复启动模拟）。
        /// </summary>
        public bool RunCrowd()
        {
            _bRan = true;

            NarrativeNPCDatabase.Initialize();
            var configs = NarrativeCrowdConfig.LoadAll();
            if (configs.Count == 0)
            {
                LastPhase = "无群体配置";
                return false;
            }

            int npcCreated = 0;
            int pedCreated = 0;

            foreach (var config in configs)
            {
                Vector3 center = ResolveCenter(config);

                foreach (var entry in config.Entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.NpcId)) continue;

                    NPCDefinition definition = NarrativeNPCDatabase.Get(entry.NpcId);
                    if (definition == null)
                    {
                        ReportMissingDefinition(entry.NpcId);
                        continue;
                    }

                    bool bPedEntry = entry.bIsPed || definition is PedNPCDefinition;
                    float spread = entry.SpreadRadius > 0f ? entry.SpreadRadius : config.SpawnRadius;

                    if (bPedEntry)
                    {
                        if (!bSpawnPeds) continue;
                        pedCreated += EnsurePeds(definition.NPCID, entry.Count, center, spread);
                        continue;
                    }

                    npcCreated += SpawnNpcActors(definition, entry.Count, center, spread, entry);
                }
            }

            LastPhase = $"NPC {npcCreated} 个 / 行人 {pedCreated} 个";
            NarrativeLog.Log($"[Spawners] 群体生成完成：交互型 NPC {npcCreated} 个、行人实体 {pedCreated} 个" +
                             $"（定义库 {NarrativeNPCDatabase.Count} 条、配置 {configs.Count} 份）。");
            return npcCreated > 0 || pedCreated > 0;
        }

        // =====================================================================
        // 交互型 NPC
        // =====================================================================

        private int SpawnNpcActors(NPCDefinition definition, int count, Vector3 center, float spread, NpcCrowdEntryJson entry)
        {
            var subsystem = NarrativeCharacterSubsystem.Instance;
            if (subsystem == null) return 0;

            int created = 0;
            for (int i = 0; i < Math.Max(1, count); i++)
            {
                Vector3 location = ResolveLocation(center, spread, entry, i);
                var transform = new Transform(location, Quaternion.Identity);

                Actor actor = subsystem.SpawnNPC(definition, transform, null);
                if (actor == null)
                {
                    NarrativeLog.LogWarning($"[Spawners] SpawnNPC 返回空（定义 {definition.NPCID}，序号 {i}）。");
                    continue;
                }

                _spawnedNpcs.Add(actor);
                created++;

                // 子对象兜底：SpawnNPC 的代码生成路径已装配；预制体路径由角色自身的 OnEnable 装配，
                // 这里再调一次是幂等的（EnsureSubobjects 内部有 _bSubobjectsEnsured 短路）。
                var character = actor.GetScript<NarrativePro.UnrealFramework.NarrativeNPCCharacter>();
                character?.EnsureSubobjects();

                // 动态生成的 NPC 进存档查找表（对应 UE5 动态 Actor 的存档登记）
                NarrativePro.SaveSystem.Subsystems.NarrativeSaveSubsystem.Instance?
                    .NotifyActorDynamicallySpawned(actor);
            }

            return created;
        }

        // =====================================================================
        // 行人群集
        // =====================================================================

        /// <summary>
        /// 确保群集模拟在跑，并按定义路径生成实体。
        /// 这一步同时补上了「没有任何脚本创建 <see cref="MassPedSimulation"/>」这一根本缺口 ——
        /// 没有它，行人实体管理器永不被 Tick，群体侧的避让/危险检测/LOD/Actor 复用全部不可达。
        /// </summary>
        private int EnsurePeds(string npcId, int count, Vector3 center, float spread)
        {
            EnsureSimulation();
            if (_simulation == null) return 0;

            _simulation.ConfigurePedDefinitions(NarrativeNPCDatabase.PedDefinitionPaths);

            var created = _simulation.SpawnPedGroup(Math.Max(1, count), center, spread);
            foreach (var entity in created)
            {
                if (!_spawnedPeds.Contains(entity)) _spawnedPeds.Add(entity);
            }
            return created.Count;
        }

        private void EnsureSimulation()
        {
            if (_simulation != null && _simulation.Actor != null) return;

            _simulation = Level.FindScript<MassPedSimulation>();
            if (_simulation == null && Actor != null)
            {
                _simulation = Actor.AddScript<MassPedSimulation>();
            }

            if (_simulation == null)
            {
                NarrativeLog.LogWarning("[Spawners] MassPedSimulation 创建失败，行人未生成。");
                return;
            }

            NarrativeLog.Log("[Spawners] 已启动 MassPedSimulation（行人实体管理器开始 Tick）。");
        }

        // =====================================================================
        // 位置解析
        // =====================================================================

        private Vector3 ResolveCenter(NpcCrowdConfigJson config)
        {
            if (config == null) return Actor != null ? Actor.Position : Vector3.Zero;
            if (!config.bFollowLocalPlayer) return config.SpawnCenter;

            // 与 MassPedSimulation.ResolveObserverLocation 同一口径：先插件玩家角色，再插件角色，最后宿主
            var player = Level.FindScript<NarrativePro.UnrealFramework.NarrativePlayerCharacter>();
            if (player?.Actor != null) return player.Actor.Position;

            var character = Level.FindScript<NarrativePro.UnrealFramework.NarrativeCharacter>();
            if (character?.Actor != null) return character.Actor.Position;

            var host = NarrativePro.CommonUI.NarrativeRuntimeBootstrap.LocalPlayerHostActor;
            if (host != null) return host.Position;

            return Actor != null ? Actor.Position : config.SpawnCenter;
        }

        private Vector3 ResolveLocation(Vector3 center, float spread, NpcCrowdEntryJson entry, int index)
        {
            if (entry != null && entry.bUseOffset)
            {
                return center + entry.Offset;
            }

            // 确定性散布：同一序号总落在同一位置（避免每次运行 NPC 乱跳，便于对照日志）
            float angle = (float)(index * 2.39996323) ; // 黄金角展开
            float radius = spread * (0.35f + 0.65f * ((index % 3) / 2f));
            Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Vector3 location = center + offset;

            if (NarrativePro.Navigation.NavigatorStatics.TryFindClosestPoint(location, out var projected))
            {
                location = projected;
            }
            return location;
        }

        // =====================================================================
        // 收尾
        // =====================================================================

        private void ReportMissingDefinition(string npcId)
        {
            if (_missingDefinitionIds.ContainsKey(npcId)) return;
            _missingDefinitionIds[npcId] = 1;
            NarrativeLog.LogError(
                $"[Spawners] 群体配置引用了不存在的 NPC 定义 '{npcId}'。" +
                $"请确认 Content/NarrativePro/NPCs/{npcId}.json 存在且 NPCID 字段匹配（已加载 {NarrativeNPCDatabase.Count} 条）。");
        }

        /// <summary>销毁本生成器生成的 NPC Actor 与行人实体。</summary>
        public void DespawnAll()
        {
            if (_simulation != null)
            {
                foreach (var entity in _spawnedPeds)
                {
                    _simulation.DespawnEntity(entity);
                }
            }
            _spawnedPeds.Clear();

            for (int i = _spawnedNpcs.Count - 1; i >= 0; i--)
            {
                var actor = _spawnedNpcs[i];
                if (actor != null) FlaxEngine.Object.Destroy(actor);
            }
            _spawnedNpcs.Clear();
        }
    }
}
