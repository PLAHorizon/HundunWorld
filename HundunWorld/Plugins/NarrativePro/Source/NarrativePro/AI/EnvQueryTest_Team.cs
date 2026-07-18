using System;

namespace NarrativePro.AI
{
    /// <summary>
    /// 阵营 EQS 测试。对应 UE5 UEnvQueryTest_Team。
    /// 过滤出符合所需态度（友好/中立/敌对）的 Actor。
    /// 注：Flax 无 EQS（环境查询系统），此处简化为 [Serializable] 类占位（Flax-不兼容）。
    /// </summary>
    [Serializable]
    public class EnvQueryTest_Team
    {
        // Flax-不兼容: UE5 的 EQS（环境查询系统）在 Flax 无对应物，保留占位。原文 TODO: Flax 无 EQS 系统，待查询系统实现后补全 RunTest 逻辑

        /// <summary>测试名称（用于调试显示）</summary>
        public string TestName = "Team";

        /// <summary>是否包含友方目标</summary>
        public bool bIncludeFriendlies = false;

        /// <summary>是否包含中立目标</summary>
        public bool bIncludeNeutrals = false;

        /// <summary>是否包含敌方目标</summary>
        public bool bIncludeEnemies = true;

        /// <summary>
        /// 执行测试。过滤出符合所需态度的候选项。
        /// </summary>
        /// <param name="queryContext">查询上下文（候选目标列表）</param>
        public virtual void RunTest(object queryContext)
        {
            // Flax-不兼容: UE5 的 EQS 测试逻辑在 Flax 无对应物，保留占位。原文 TODO: 实现 EQS 测试逻辑
            // UE5 中遍历候选项，根据其与查询者的阵营态度（友好/中立/敌对）过滤
        }

        /// <summary>获取测试详情（用于调试显示）</summary>
        public virtual string GetDescriptionDetails()
        {
            var includes = new System.Collections.Generic.List<string>();
            if (bIncludeFriendlies) includes.Add("友方");
            if (bIncludeNeutrals) includes.Add("中立");
            if (bIncludeEnemies) includes.Add("敌对");
            return "包含阵营: " + (includes.Count > 0 ? string.Join("/", includes) : "无");
        }
    }
}
