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
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// AI分析师Grain实现 - 负责智能对话分析
    /// </summary>
    public class AIAnalystGrain : Grain, IAIAnalystGrain
    {
        private readonly ILogger<AIAnalystGrain> _logger;
        private readonly IPersistentState<AIAnalystState> _state;
        private readonly SemanticKernelConfig _kernelConfig;

        public AIAnalystGrain(
            ILogger<AIAnalystGrain> logger,
            [PersistentState("aianalyst", "FlowerStore")] IPersistentState<AIAnalystState> state,
            SemanticKernelConfig kernelConfig)
        {
            _logger = logger;
            _state = state;
            _kernelConfig = kernelConfig;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("AIAnalystGrain {GrainKey} activating.", this.GetPrimaryKey());

            if (_state.State.Conversations == null)
                _state.State.Conversations = new Dictionary<string, List<AIChatMessage>>();

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<string> ChatAsync(string question, string conversationId)
        {
            try
            {
                if (!_state.State.Conversations.ContainsKey(conversationId))
                    _state.State.Conversations[conversationId] = new List<AIChatMessage>();

                var history = _state.State.Conversations[conversationId];
                history.Add(new AIChatMessage { Role = "user", Content = question, Timestamp = DateTime.Now });

                var kernel = KernelBuilder.Build(_kernelConfig);
                var chatService = kernel.GetRequiredService<IChatCompletionService>();
                var chatHistory = new ChatHistory();

                chatHistory.AddSystemMessage("你是花卉市场分析专家，专注于花卉价格预测、市场趋势分析和投资建议。");

                foreach (var msg in history.TakeLast(20))
                {
                    if (msg.Role == "user")
                        chatHistory.AddUserMessage(msg.Content);
                    else
                        chatHistory.AddAssistantMessage(msg.Content);
                }

                var response = await chatService.GetChatMessageContentAsync(chatHistory);
                var answer = response.Content ?? "抱歉，无法生成回复。";

                history.Add(new AIChatMessage { Role = "assistant", Content = answer, Timestamp = DateTime.Now });

                if (history.Count > 100)
                    _state.State.Conversations[conversationId] = history.TakeLast(100).ToList();

                await _state.WriteStateAsync();

                _logger.LogInformation("AI对话完成: ConversationId={ConversationId}, QuestionLength={QuestionLength}",
                    conversationId, question.Length);

                return answer;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI对话失败: ConversationId={ConversationId}", conversationId);
                throw;
            }
        }

        public async Task<string> GenerateDailyReportAsync(DateTime date)
        {
            try
            {
                var kernel = KernelBuilder.Build(_kernelConfig);
                var chatService = kernel.GetRequiredService<IChatCompletionService>();
                var chatHistory = new ChatHistory();

                chatHistory.AddSystemMessage("你是花卉市场分析专家，请生成专业的每日市场分析报告。");
                chatHistory.AddUserMessage($"请生成{date:yyyy-MM-dd}的花卉市场每日分析报告，包括市场概况、价格走势、热门品种和投资建议。");

                var response = await chatService.GetChatMessageContentAsync(chatHistory);
                var report = response.Content ?? "无法生成报告。";

                _logger.LogInformation("生成每日报告: Date={Date}", date);

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
