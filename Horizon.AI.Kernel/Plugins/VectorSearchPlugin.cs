using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace Horizon.AI.Kernel.Plugins
{
    public class VectorSearchPlugin
    {
        [KernelFunction]
        [Description("搜索知识库获取相关信息")]
        public string SearchKnowledge(
            [Description("搜索查询")] string query,
            [Description("返回结果数量")] int topK = 5)
        {
            return $"知识库搜索'{query}'的占位结果（需接入Redis向量索引获取实时数据）";
        }
    }
}
