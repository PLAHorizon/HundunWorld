using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace Horizon.AI.Kernel.Plugins
{
    public class MarketDataPlugin
    {
        [KernelFunction]
        [Description("获取指定花卉品种的最新价格")]
        public string GetLatestPrice(
            [Description("花卉品种ID")] int speciesId)
        {
            return $"品种{speciesId}的最新价格为占位数据（需接入Orleans客户端获取实时数据）";
        }

        [KernelFunction]
        [Description("获取指定花卉品种的价格预测")]
        public string GetPriceForecast(
            [Description("花卉品种ID")] int speciesId,
            [Description("预测天数")] int horizonDays = 7)
        {
            return $"品种{speciesId}的{horizonDays}天价格预测为占位数据（需接入Orleans客户端获取实时数据）";
        }
    }
}
