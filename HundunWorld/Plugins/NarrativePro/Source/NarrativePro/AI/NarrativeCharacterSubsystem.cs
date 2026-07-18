using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Character;
using NarrativePro.Core;

namespace NarrativePro.AI
{
    /// <summary>
    /// 角色数组包装。对应 UE5 FCharacterArray。
    /// 因 TMap 不能直接存储 TArray，需用结构体包装。
    /// </summary>
    [Serializable]
    public class CharacterArray
    {
        public List<Actor> Characters = new List<Actor>();

        public bool HasValidCharacters()
        {
            foreach (var c in Characters)
            {
                if (c != null) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// NPC 数组包装。对应 UE5 FNPCArray。
    /// </summary>
    [Serializable]
    public class NPCArray
    {
        public List<Actor> NPCs = new List<Actor>();

        public bool HasValidNPCs()
        {
            foreach (var n in NPCs)
            {
                if (n != null) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// NPC 生成参数。对应 UE5 FNPCSpawnParams。
    /// </summary>
    [Serializable]
    public class NPCSpawnParams
    {
        public bool bDeferSpawn = false;
        public string SpawnReason = "";
    }

    /// <summary>
    /// 请求 NPC 生成完成回调签名。对应 UE5 FOnRequestedNPCSpawned。
    /// </summary>
    /// <param name="npcData">NPC 数据资产</param>
    /// <param name="character">生成的 NPC 角色</param>
    public delegate void OnRequestedNPCSpawned(NPCDefinition npcData, Actor character);

    /// <summary>
    /// 角色子系统。对应 UE5 UNarrativeCharacterSubsystem。
    /// UE5 中继承 UWorldSubsystem；Flax 中改为单例模式（静态 Instance）。
    /// 负责：NPC 生成、查找、缓存、唯一性检查。
    /// 通过 TMap 缓存定义→角色映射，避免昂贵的 GetAllActorsOfClass 调用。
    /// </summary>
    public class NarrativeCharacterSubsystem : Script
    {
        /// <summary>单例实例</summary>
        public static NarrativeCharacterSubsystem Instance { get; private set; }

        /// <summary>NPC 生成完成事件</summary>
        public event OnRequestedNPCSpawned OnNPCSpawned;

        /// <summary>角色定义 → 角色数组映射</summary>
        private Dictionary<CharacterDefinition, CharacterArray> _characterMap = new Dictionary<CharacterDefinition, CharacterArray>();

        /// <summary>NPCID → NPC 数组映射</summary>
        private Dictionary<string, NPCArray> _npcMap = new Dictionary<string, NPCArray>();

        public override void OnEnable()
        {
            base.OnEnable();
            if (Instance != null && Instance != this)
            {
                NarrativeLog.LogWarning("已存在 NarrativeCharacterSubsystem 实例，当前实例将被销毁");
                Destroy(this);
                return;
            }
            Instance = this;
        }

        public override void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            base.OnDisable();
        }

        /// <summary>清理所有失效的角色引用</summary>
        public void CleanupInvalidReferences()
        {
            foreach (var kvp in _characterMap)
            {
                kvp.Value.Characters.RemoveAll(c => c == null);
            }
            foreach (var kvp in _npcMap)
            {
                kvp.Value.NPCs.RemoveAll(n => n == null);
            }
        }

        /// <summary>销毁指定 NPC</summary>
        public bool DestroyNPC(Actor npc)
        {
            if (npc == null) return false;
            // 从缓存中移除
            UnregisterCharacter(npc);
            Destroy(npc);
            return true;
        }

        /// <summary>
        /// 生成 NPC。
        /// </summary>
        /// <param name="npcData">NPC 定义数据资产</param>
        /// <param name="spawnTransform">生成变换（可选）</param>
        /// <param name="spawnParams">生成参数</param>
        /// <returns>生成的 NPC 角色 Actor</returns>
        public Actor SpawnNPC(NPCDefinition npcData, Transform spawnTransform = default, NPCSpawnParams spawnParams = null)
        {
            if (npcData == null)
            {
                NarrativeLog.LogWarning("SpawnNPC 失败：npcData 为空");
                return null;
            }

            // 检查唯一性
            if (!npcData.bAllowMultipleInstances)
            {
                var existing = FindNPC(npcData);
                if (existing != null)
                {
                    NarrativeLog.Log($"NPC {npcData.NPCName} 已存在唯一实例，返回现有实例");
                    return existing;
                }
            }

            return SpawnNPC_Internal(npcData, spawnTransform, spawnParams);
        }

        /// <summary>查找或生成 NPC</summary>
        public Actor FindOrSpawnNPC(NPCDefinition npcData, Transform spawnTransform = default)
        {
            if (npcData == null) return null;
            var existing = FindNPC(npcData);
            if (existing != null) return existing;
            return SpawnNPC(npcData, spawnTransform);
        }

        /// <summary>通过 NPC 定义查找 NPC</summary>
        public Actor FindNPC(NPCDefinition npcData)
        {
            if (npcData == null) return null;
            if (_npcMap.TryGetValue(npcData.NPCID, out var npcArray))
            {
                foreach (var npc in npcArray.NPCs)
                {
                    if (npc != null) return npc;
                }
            }
            return null;
        }

        /// <summary>通过 NPC 定义查找 NPC（带成功标志）</summary>
        public Actor FindNPCWithStatus(NPCDefinition npcData, out bool bOutSucceeded)
        {
            bOutSucceeded = false;
            if (npcData == null) return null;
            if (_npcMap.TryGetValue(npcData.NPCID, out var npcArray))
            {
                foreach (var npc in npcArray.NPCs)
                {
                    if (npc != null)
                    {
                        bOutSucceeded = true;
                        return npc;
                    }
                }
            }
            return null;
        }

        /// <summary>通过 NPCID 查找 NPC（高效 TMap 查询）</summary>
        public Actor FindNPCByID(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return null;
            if (_npcMap.TryGetValue(npcId, out var npcArray))
            {
                foreach (var npc in npcArray.NPCs)
                {
                    if (npc != null) return npc;
                }
            }
            return null;
        }

        /// <summary>通过角色定义查找角色</summary>
        public Actor FindCharacter(CharacterDefinition characterDefinition)
        {
            if (characterDefinition == null) return null;
            if (_characterMap.TryGetValue(characterDefinition, out var charArray))
            {
                foreach (var c in charArray.Characters)
                {
                    if (c != null) return c;
                }
            }
            return null;
        }

        /// <summary>查找所有指定类型的 NPC</summary>
        public void FindNPCs(NPCDefinition npcData, List<Actor> outActors)
        {
            if (npcData == null || outActors == null) return;
            if (_npcMap.TryGetValue(npcData.NPCID, out var npcArray))
            {
                foreach (var npc in npcArray.NPCs)
                {
                    if (npc != null) outActors.Add(npc);
                }
            }
        }

        /// <summary>查找所有指定定义的角色</summary>
        public void FindCharacters(CharacterDefinition characterDefinition, List<Actor> outActors)
        {
            if (characterDefinition == null || outActors == null) return;
            if (_characterMap.TryGetValue(characterDefinition, out var charArray))
            {
                foreach (var c in charArray.Characters)
                {
                    if (c != null) outActors.Add(c);
                }
            }
        }

        /// <summary>查询指定角色是否已生成</summary>
        public bool IsCharacterSpawned(CharacterDefinition characterDefinition)
        {
            if (characterDefinition == null) return false;
            if (_characterMap.TryGetValue(characterDefinition, out var charArray))
            {
                return charArray.HasValidCharacters();
            }
            return false;
        }

        /// <summary>内部生成 NPC 实现</summary>
        protected virtual Actor SpawnNPC_Internal(NPCDefinition npcData, Transform spawnTransform, NPCSpawnParams spawnParams)
        {
            if (string.IsNullOrEmpty(npcData.NPCClassPath))
            {
                NarrativeLog.LogWarning($"NPC {npcData.NPCName} 未配置 NPCClassPath，无法生成");
                return null;
            }

            // 使用 Flax 的 Content.LoadAsync 异步加载 Prefab 资源引用
            Prefab prefab = Content.LoadAsync<Prefab>(npcData.NPCClassPath);
            if (prefab == null)
            {
                NarrativeLog.LogError($"加载 NPC Prefab 失败：{npcData.NPCClassPath}");
                return null;
            }

            Actor npcActor = PrefabManager.SpawnPrefab(prefab, spawnTransform.Translation, spawnTransform.Orientation);
            if (npcActor == null)
            {
                NarrativeLog.LogError($"生成 NPC Actor 失败：{npcData.NPCName}");
                return null;
            }

            // 设置 NPC 控制器的定义
            var controller = npcActor.GetScript<NarrativeNPCController>();
            if (controller != null)
            {
                controller.NPCData = npcData;
            }

            RegisterCharacter(npcActor, npcData);
            OnNPCSpawned?.Invoke(npcData, npcActor);
            return npcActor;
        }

        /// <summary>注册角色到缓存</summary>
        public void RegisterCharacter(Actor character, NPCDefinition npcData = null)
        {
            if (character == null) return;

            if (npcData != null)
            {
                if (!_npcMap.TryGetValue(npcData.NPCID, out var npcArray))
                {
                    npcArray = new NPCArray();
                    _npcMap[npcData.NPCID] = npcArray;
                }
                npcArray.NPCs.Add(character);
            }
        }

        /// <summary>从缓存注销角色</summary>
        public void UnregisterCharacter(Actor character)
        {
            if (character == null) return;
            foreach (var kvp in _npcMap)
            {
                kvp.Value.NPCs.Remove(character);
            }
            foreach (var kvp in _characterMap)
            {
                kvp.Value.Characters.Remove(character);
            }
        }
    }
}

