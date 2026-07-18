using System;

namespace NarrativePro.AI
{
    /// <summary>
    /// 攻击优先级 EQS 测试。对应 UE5 UEnvQueryTest_AttackPriority。
    /// 检查目标 ASC（能力系统组件）的攻击优先级值。
    /// 注：Flax 无 EQS（环境查询系统），此处简化为 [Serializable] 类占位（Flax-不兼容）。
    /// </summary>
    [Serializable]
    public class EnvQueryTest_AttackPriority
    {
        // Flax-不兼容: UE5 的 EQS（环境查询系统）在 Flax 无对应物，保留占位。原文 TODO: Flax 无 EQS 系统，待查询系统实现后补全 RunTest 逻辑

        /// <summary>测试名称（用于调试显示）</summary>
        public string TestName = "AttackPriority";

        /// <summary>
        /// 执行测试。检查候选目标的攻击优先级值并据此评分。
        /// </summary>
        /// <param name="queryContext">查询上下文（候选目标列表）</param>
        public virtual void RunTest(object queryContext)
        {
            // Flax-不兼容: UE5 的 EQS 测试逻辑在 Flax 无对应物，保留占位。原文 TODO: 实现 EQS 测试逻辑
            // UE5 中遍历查询实例的所有候选项，读取其 ASC 的攻击优先级属性并评分
        }

        /// <summary>获取测试标题（用于调试显示）</summary>
        public virtual string GetDescriptionTitle()
        {
            return "Attack Priority";
        }

        /// <summary>获取测试详情（用于调试显示）</summary>
        public virtual string GetDescriptionDetails()
        {
            return "检查目标 ASC 的攻击优先级值";
        }
    }
}
