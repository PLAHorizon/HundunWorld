using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Horizon.Orleans.Interface.World;

namespace Horizon.Orleans.Grains.World;

/// <summary>
/// P2.5 任务 Grain 实现：任务状态机 + 目标进度追踪。
/// </summary>
[GrainType("world.quest")]
public sealed class QuestGrain : Grain, IQuestGrain
{
    private readonly ILogger<QuestGrain> _logger;
    private QuestInitData _data = null!;
    private QuestPhase _phase = QuestPhase.Active;
    private QuestObjectiveProgress[] _objectives = Array.Empty<QuestObjectiveProgress>();
    private DateTime _acceptTime;

    public QuestGrain(ILogger<QuestGrain> logger)
    {
        _logger = logger;
    }

    public Task InitializeAsync(QuestInitData data)
    {
        _data = data;
        _phase = QuestPhase.Active;
        _acceptTime = DateTime.UtcNow;
        _objectives = data.Objectives.Select(o => new QuestObjectiveProgress
        {
            Type = o.Type,
            TargetId = o.TargetId,
            CurrentCount = 0,
            RequiredCount = o.RequiredCount,
            IsComplete = false,
            Description = o.Description,
        }).ToArray();

        _logger.LogInformation(
            "任务接取。QuestInstanceId={InstanceId}, Template={Template}, Name={Name}, Character={Character}",
            this.GetPrimaryKeyLong(), data.QuestTemplateId, data.QuestName, data.CharacterId);

        return Task.CompletedTask;
    }

    public Task<QuestState> UpdateObjectiveAsync(QuestObjectiveType objectiveType, int targetId, int count = 1)
    {
        if (_phase != QuestPhase.Active)
        {
            return Task.FromResult(BuildState());
        }

        // 查找匹配的目标
        foreach (var obj in _objectives)
        {
            if (obj.Type == objectiveType && obj.TargetId == targetId && !obj.IsComplete)
            {
                obj.CurrentCount = Math.Min(obj.RequiredCount, obj.CurrentCount + count);
                if (obj.CurrentCount >= obj.RequiredCount)
                {
                    obj.IsComplete = true;
                    _logger.LogDebug(
                        "任务目标完成。QuestInstanceId={InstanceId}, Type={Type}, TargetId={TargetId}",
                        this.GetPrimaryKeyLong(), objectiveType, targetId);
                }
                break;
            }
        }

        // 检查是否所有目标完成
        if (_objectives.All(o => o.IsComplete))
        {
            _phase = QuestPhase.ReadyToSubmit;
            _logger.LogInformation(
                "任务所有目标完成，待提交。QuestInstanceId={InstanceId}",
                this.GetPrimaryKeyLong());
        }

        return Task.FromResult(BuildState());
    }

    public Task<QuestSubmitResult> SubmitAsync()
    {
        if (_phase != QuestPhase.ReadyToSubmit)
        {
            return Task.FromResult(new QuestSubmitResult
            {
                Success = false,
                ErrorMessage = _phase == QuestPhase.Active ? "任务目标尚未全部完成。" : "任务状态异常，无法提交。",
            });
        }

        _phase = QuestPhase.Completed;

        _logger.LogInformation(
            "任务提交成功。QuestInstanceId={InstanceId}, Template={Template}, ExpReward={Exp}, GoldReward={Gold}",
            this.GetPrimaryKeyLong(), _data.QuestTemplateId, _data.Reward.ExpReward, _data.Reward.GoldReward);

        // TODO: 通过 ICharacterGrain 发放奖励（经验/金币/物品）
        return Task.FromResult(new QuestSubmitResult
        {
            Success = true,
            Reward = _data.Reward,
        });
    }

    public Task AbandonAsync()
    {
        _phase = QuestPhase.Abandoned;
        _logger.LogInformation("任务放弃。QuestInstanceId={InstanceId}", this.GetPrimaryKeyLong());
        return Task.CompletedTask;
    }

    public Task<QuestState> GetStateAsync() => Task.FromResult(BuildState());

    private QuestState BuildState() => new()
    {
        QuestInstanceId = this.GetPrimaryKeyLong(),
        QuestTemplateId = _data?.QuestTemplateId ?? 0,
        QuestName = _data?.QuestName ?? string.Empty,
        Phase = _phase,
        Objectives = _objectives,
        AcceptTime = _acceptTime,
        AreAllObjectivesComplete = _objectives.Length > 0 && _objectives.All(o => o.IsComplete),
    };
}
