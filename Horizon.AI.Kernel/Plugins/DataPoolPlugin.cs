using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace Horizon.AI.Kernel.Plugins
{
    public class DataPoolPlugin
    {
        [KernelFunction]
        [Description("查询数据池中的历史数据")]
        public string QueryHistoricalData(
            [Description("数据类型")] string dataType,
            [Description("开始时间")] string startTime,
            [Description("结束时间")] string endTime)
        {
            return $"数据池查询{dataType}从{startTime}到{endTime}的占位结果（需接入Orleans客户端获取实时数据）";
        }
    }
}
