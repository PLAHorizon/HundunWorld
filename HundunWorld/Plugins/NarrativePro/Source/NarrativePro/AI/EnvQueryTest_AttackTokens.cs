using System;

namespace NarrativePro.AI
{
    /// <summary>
    /// 攻击令牌 EQS 测试。对应 UE5 UEnvQueryTest_AttackTokens。
    /// 根据目标拥有的空闲攻击令牌数量对 Actor 评分。
    /// 注：Flax 无 EQS（环境查询系统），此处简化为 [Serializable] 类占位（Flax-不兼容）。
    /// </summary>
    [Serializable]
    public class EnvQueryTest_AttackTokens
    {
        // Flax-不兼容: UE5 的 EQS（环境查询系统）在 Flax 无对应物，保留占位。原文 TODO: Flax 无 EQS 系统，待查询系统实现后补全 RunTest 逻辑

        /// <summary>测试名称（用于调试显示）</summary>
        public string TestName = "AttackTokens";

        /// <summary>
        /// 若为 true，检查已授予的令牌数量而非可用令牌数量。
        /// </summary>
        public bool bCheckGrantedTokens = false;

        /// <summary>
        /// 执行测试。根据目标空闲（或已授予）的攻击令牌数量评分。
        /// </summary>
        /// <param name="queryContext">查询上下文（候选目标列表）</param>
        public virtual void RunTest(object queryContext)
        {
            // Flax-不兼容: UE5 的 EQS 测试逻辑在 Flax 无对应物，保留占位。原文 TODO: 实现 EQS 测试逻辑
            // UE5 中遍历候选项，读取其 ASC 的攻击令牌数（AttackTokens 标签计数）并评分
        }

        /// <summary>获取测试标题（用于调试显示）</summary>
        public virtual string GetDescriptionTitle()
        {
            return "Attack Tokens";
        }

        /// <summary>获取测试详情（用于调试显示）</summary>
        public virtual string GetDescriptionDetails()
        {
            return bCheckGrantedTokens
                ? "根据已授予的攻击令牌数量评分"
                : "根据可用的攻击令牌数量评分";
        }
    }
}
