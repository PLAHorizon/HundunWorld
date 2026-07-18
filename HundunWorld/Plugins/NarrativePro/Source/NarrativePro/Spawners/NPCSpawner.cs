using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Spawners
{
    /// <summary>
    /// NPC 生成器。对应 UE5 ANPCSpawner。
    /// 继承自 SpawnerBase，提供与 NPC 生成相关的便捷功能。
    /// 架构说明：UE5 中继承 AActor（ASpawnerBase）；Flax 中改为 Script，详见 SpawnerBase 说明。
    /// </summary>
    public class NPCSpawner : SpawnerBase
    {
        /// <summary>
        /// 获取所有在此生成的 NPC。对应 UE5 GetSpawnedNPCs。
        /// 注：Flax 中无 ANarrativeNPCCharacter 类型，统一以 Actor 表示生成的 NPC。
        /// </summary>
        /// <param name="outNPCs">输出已生成的 NPC 列表</param>
        public void GetSpawnedNPCs(List<Actor> outNPCs)
        {
            if (outNPCs == null) return;
            outNPCs.Clear();

            var spawnComponents = Actor.GetScripts<NPCSpawnComponent>();
            if (spawnComponents == null) return;

            foreach (var comp in spawnComponents)
            {
                if (comp == null) continue;
                Actor npc = comp.GetSpawnedNPC();
                if (npc != null)
                {
                    outNPCs.Add(npc);
                }
            }
        }

        /// <summary>
        /// 创建一个新的 NPC 生成组件并添加到此生成器。对应 UE5 CreateNPCSpawner。
        /// 用于编辑器流程中程序化添加生成组件。
        /// </summary>
        /// <returns>新创建的 NPCSpawnComponent</returns>
        public NPCSpawnComponent CreateNPCSpawner()
        {
            var comp = Actor.AddScript<NPCSpawnComponent>();
            return comp;
        }

        /// <summary>
        /// 编辑器数据校验。对应 UE5 IsDataValid。
        /// 检查生成器配置是否有效，返回是否通过校验及错误信息。
        /// </summary>
        /// <param name="errorMessage">校验失败时的错误信息</param>
        /// <returns>true 表示有效；false 表示无效</returns>
        public bool IsDataValid(out string errorMessage)
        {
            errorMessage = "";

            var spawnComponents = Actor.GetScripts<NPCSpawnComponent>();
            if (spawnComponents == null || spawnComponents.Length == 0)
            {
                // 无生成组件不算错误，但可提示
                return true;
            }

            foreach (var comp in spawnComponents)
            {
                if (comp == null) continue;
                if (comp.NPCToSpawn == null)
                {
                    errorMessage = $"NPCSpawnComponent 上未配置 NPCToSpawn（NPC 定义）";
                    return false;
                }
            }

            return true;
        }
    }
}
