using Horizon.AI.Kernel;
using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.Flower;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Orleans;
using Orleans.Runtime;
using System;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 报告生成Grain实现 - 负责每日报告生成
    /// </summary>
    public class ReportGeneratorGrain : Grain, IReportGeneratorGrain
    {
        private readonly ILogger<ReportGeneratorGrain> _logger;
        private readonly IPersistentState<ReportGeneratorState> _state;
        private readonly SemanticKernelConfig _kernelConfig;
        private readonly IDataContext<FlowerEntityContext, FlowerGeneratedReport, long> _dataContext;

        public ReportGeneratorGrain(
            ILogger<ReportGeneratorGrain> logger,
            [PersistentState("reportgenerator", "FlowerStore")] IPersistentState<ReportGeneratorState> state,
            SemanticKernelConfig kernelConfig,
            IDataContext<FlowerEntityContext, FlowerGeneratedReport, long> dataContext)
        {
            _logger = logger;
            _state = state;
            _kernelConfig = kernelConfig;
            _dataContext = dataContext;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("ReportGeneratorGrain {GrainKey} activating.", this.GetPrimaryKeyLong());

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<string> GenerateDailyReportAsync(DateTime date)
        {
            try
            {
                var kernel = KernelBuilder.Build(_kernelConfig);
                var chatService = kernel.GetRequiredService<IChatCompletionService>();
                var chatHistory = new ChatHistory();

                chatHistory.AddSystemMessage("你是花卉市场分析专家，请生成专业的每日市场分析报告，包含市场概况、价格走势分析、热门品种推荐和风险提示。");
                chatHistory.AddUserMessage($"请生成{date:yyyy-MM-dd}的花卉市场每日分析报告。");

                var response = await chatService.GetChatMessageContentAsync(chatHistory);
                var report = response.Content ?? "无法生成报告。";

                var entity = new FlowerGeneratedReport
                {
                    ReportType = "DailyAnalysis",
                    ReportDate = date,
                    Content = report,
                    ModelVersion = _kernelConfig.ChatModelId
                };

                var result = await _dataContext.AddAsync(entity);

                _state.State.LastReportDate = date;
                _state.State.TotalReports++;
                await _state.WriteStateAsync();

                _logger.LogInformation("生成每日报告: Date={Date}, ReportId={ReportId}", date, result?.Id);

                return report;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成每日报告失败: Date={Date}", date);
                throw;
            }
        }
    }
}
