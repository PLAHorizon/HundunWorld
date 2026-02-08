using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using MemoryPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 任务系统状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class QuestState
    {
        /// <summary>
        /// 进行中的任务 (QuestId -> QuestData)
        /// </summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public Dictionary<int, QuestData> ActiveQuests { get; set; } = new();

        /// <summary>
        /// 已完成的任务 (QuestId -> QuestData)
        /// </summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public Dictionary<int, QuestData> CompletedQuests { get; set; } = new();

        /// <summary>
        /// 最大同时接受任务数
        /// </summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public int MaxActiveQuests { get; set; } = 20;
    }

    /// <summary>
    /// 任务系统Grain实现 - 负责任务接取、进度更新、完成、放弃
    /// </summary>
    public class QuestGrain : Grain, IQuestGrain
    {
        private readonly ILogger<QuestGrain> _logger;
        private readonly IPersistentState<QuestState> _questState;

        public QuestGrain(
            ILogger<QuestGrain> logger,
            [PersistentState("quest", "GameStore")] IPersistentState<QuestState> questState)
        {
            _logger = logger;
            _questState = questState;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("QuestGrain {GrainKey} activating.", this.GetPrimaryKey());

            if (_questState.State.ActiveQuests == null)
                _questState.State.ActiveQuests = new Dictionary<int, QuestData>();
            if (_questState.State.CompletedQuests == null)
                _questState.State.CompletedQuests = new Dictionary<int, QuestData>();

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<bool> AcceptQuestAsync(int questId, string questName, string description, int questType, int level, Dictionary<string, int> rewards)
        {
            try
            {
                var state = _questState.State;

                if (questId <= 0)
                {
                    _logger.LogWarning("任务ID无效: QuestId={QuestId}", questId);
                    return false;
                }

                if (string.IsNullOrWhiteSpace(questName))
                {
                    _logger.LogWarning("任务名称无效");
                    return false;
                }

                if (state.ActiveQuests.ContainsKey(questId))
                {
                    _logger.LogWarning("任务已接受: QuestId={QuestId}", questId);
                    return false;
                }

                if (state.CompletedQuests.ContainsKey(questId))
                {
                    _logger.LogWarning("任务已完成: QuestId={QuestId}", questId);
                    return false;
                }

                if (state.ActiveQuests.Count >= state.MaxActiveQuests)
                {
                    _logger.LogWarning("进行中任务数量已达上限: Max={Max}", state.MaxActiveQuests);
                    return false;
                }

                var questData = new QuestData
                {
                    QuestId = questId,
                    QuestName = questName.Trim(),
                    Description = description ?? "",
                    QuestType = questType,
                    Level = level,
                    Status = (int)QuestProgressStatus.InProgress,
                    Rewards = rewards ?? new Dictionary<string, int>(),
                    AcceptTime = DateTime.UtcNow,
                    Objectives = new List<QuestObjectiveData>()
                };

                state.ActiveQuests[questId] = questData;
                await _questState.WriteStateAsync();

                _logger.LogInformation("接受任务成功: QuestId={QuestId}, Name={QuestName}", questId, questName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "接受任务失败: QuestId={QuestId}", questId);
                throw;
            }
        }

        public async Task<bool> AddQuestObjectiveAsync(int questId, string objectiveType, string description, int requiredCount)
        {
            try
            {
                var state = _questState.State;

                if (!state.ActiveQuests.TryGetValue(questId, out var quest))
                {
                    _logger.LogWarning("任务不存在或未接受: QuestId={QuestId}", questId);
                    return false;
                }

                if (quest.Status != (int)QuestProgressStatus.InProgress)
                {
                    _logger.LogWarning("任务不在进行中: QuestId={QuestId}, Status={Status}", questId, quest.Status);
                    return false;
                }

                if (string.IsNullOrWhiteSpace(objectiveType))
                {
                    _logger.LogWarning("目标类型无效");
                    return false;
                }

                if (requiredCount <= 0)
                {
                    _logger.LogWarning("目标数量无效: RequiredCount={RequiredCount}", requiredCount);
                    return false;
                }

                quest.Objectives.Add(new QuestObjectiveData
                {
                    ObjectiveType = objectiveType.Trim(),
                    Description = description ?? "",
                    RequiredCount = requiredCount,
                    CurrentCount = 0,
                    IsCompleted = false
                });

                await _questState.WriteStateAsync();

                _logger.LogInformation("添加任务目标: QuestId={QuestId}, Type={ObjectiveType}, RequiredCount={RequiredCount}",
                    questId, objectiveType, requiredCount);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加任务目标失败: QuestId={QuestId}", questId);
                throw;
            }
        }

        public async Task<bool> UpdateQuestProgressAsync(int questId, int objectiveIndex, int progressCount)
        {
            try
            {
                var state = _questState.State;

                if (!state.ActiveQuests.TryGetValue(questId, out var quest))
                {
                    _logger.LogWarning("任务不存在或未接受: QuestId={QuestId}", questId);
                    return false;
                }

                if (quest.Status != (int)QuestProgressStatus.InProgress &&
                    quest.Status != (int)QuestProgressStatus.ReadyToSubmit)
                {
                    _logger.LogWarning("任务状态不支持进度更新: QuestId={QuestId}, Status={Status}", questId, quest.Status);
                    return false;
                }

                if (objectiveIndex < 0 || objectiveIndex >= quest.Objectives.Count)
                {
                    _logger.LogWarning("目标索引无效: QuestId={QuestId}, ObjectiveIndex={ObjectiveIndex}", questId, objectiveIndex);
                    return false;
                }

                if (progressCount <= 0)
                {
                    _logger.LogWarning("进度增量无效: ProgressCount={ProgressCount}", progressCount);
                    return false;
                }

                var objective = quest.Objectives[objectiveIndex];

                if (objective.IsCompleted)
                {
                    _logger.LogWarning("目标已完成: QuestId={QuestId}, ObjectiveIndex={ObjectiveIndex}", questId, objectiveIndex);
                    return false;
                }

                objective.CurrentCount = Math.Min(objective.CurrentCount + progressCount, objective.RequiredCount);

                if (objective.CurrentCount >= objective.RequiredCount)
                {
                    objective.IsCompleted = true;
                }

                // Check if all objectives are completed
                if (quest.Objectives.Count > 0 && quest.Objectives.All(o => o.IsCompleted))
                {
                    quest.Status = (int)QuestProgressStatus.ReadyToSubmit;
                }

                await _questState.WriteStateAsync();

                _logger.LogInformation("更新任务进度: QuestId={QuestId}, ObjectiveIndex={ObjectiveIndex}, Current={Current}/{Required}",
                    questId, objectiveIndex, objective.CurrentCount, objective.RequiredCount);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新任务进度失败: QuestId={QuestId}", questId);
                throw;
            }
        }

        public async Task<QuestCompleteResult> CompleteQuestAsync(int questId)
        {
            try
            {
                var state = _questState.State;

                if (!state.ActiveQuests.TryGetValue(questId, out var quest))
                {
                    return new QuestCompleteResult
                    {
                        Success = false,
                        Message = "任务不存在或未接受",
                        QuestId = questId
                    };
                }

                // Allow completion if quest has no objectives or all objectives are completed
                bool allObjectivesComplete = quest.Objectives.Count == 0 || quest.Objectives.All(o => o.IsCompleted);

                if (!allObjectivesComplete)
                {
                    return new QuestCompleteResult
                    {
                        Success = false,
                        Message = "任务目标尚未完成",
                        QuestId = questId
                    };
                }

                quest.Status = (int)QuestProgressStatus.Completed;
                quest.CompleteTime = DateTime.UtcNow;

                // Move from active to completed
                state.ActiveQuests.Remove(questId);
                state.CompletedQuests[questId] = quest;

                await _questState.WriteStateAsync();

                _logger.LogInformation("完成任务: QuestId={QuestId}, Name={QuestName}", questId, quest.QuestName);

                return new QuestCompleteResult
                {
                    Success = true,
                    Message = "完成任务成功",
                    QuestId = questId,
                    Rewards = quest.Rewards
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "完成任务失败: QuestId={QuestId}", questId);
                throw;
            }
        }

        public async Task<bool> AbandonQuestAsync(int questId)
        {
            try
            {
                var state = _questState.State;

                if (!state.ActiveQuests.TryGetValue(questId, out var quest))
                {
                    _logger.LogWarning("任务不存在或未接受: QuestId={QuestId}", questId);
                    return false;
                }

                quest.Status = (int)QuestProgressStatus.Abandoned;
                state.ActiveQuests.Remove(questId);

                await _questState.WriteStateAsync();

                _logger.LogInformation("放弃任务: QuestId={QuestId}, Name={QuestName}", questId, quest.QuestName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "放弃任务失败: QuestId={QuestId}", questId);
                throw;
            }
        }

        public Task<List<QuestData>> GetActiveQuestsAsync()
        {
            try
            {
                var quests = _questState.State.ActiveQuests.Values.ToList();
                return Task.FromResult(quests);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取进行中任务失败");
                throw;
            }
        }

        public Task<List<QuestData>> GetCompletedQuestsAsync()
        {
            try
            {
                var quests = _questState.State.CompletedQuests.Values.ToList();
                return Task.FromResult(quests);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取已完成任务失败");
                throw;
            }
        }

        public Task<QuestData?> GetQuestAsync(int questId)
        {
            try
            {
                if (_questState.State.ActiveQuests.TryGetValue(questId, out var activeQuest))
                {
                    return Task.FromResult(activeQuest);
                }

                if (_questState.State.CompletedQuests.TryGetValue(questId, out var completedQuest))
                {
                    return Task.FromResult(completedQuest);
                }

                return Task.FromResult<QuestData?>(null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取任务详情失败: QuestId={QuestId}", questId);
                throw;
            }
        }
    }
}
