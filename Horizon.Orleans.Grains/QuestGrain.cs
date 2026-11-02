using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.GameModel;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using MemoryPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 任务系统Grain实现 - 负责任务接取、进度更新、完成、放弃
    /// </summary>
    public class QuestGrain : Grain, IQuestGrain
    {
        private readonly ILogger<QuestGrain> _logger;
        private readonly IPersistentState<QuestState> _questState;
        private readonly IDataContext<GameEntityContext, QuestTemplateEntity, int>? _templates;

        public QuestGrain(
            ILogger<QuestGrain> logger,
            [PersistentState("quest", "GameStore")] IPersistentState<QuestState> questState,
            IDataContext<GameEntityContext, QuestTemplateEntity, int>? templates = null)
        {
            _logger = logger;
            _questState = questState;
            _templates = templates;
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
                    AcceptTime = DateTime.Now,
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

        /// <summary>
        /// 按服务端任务模板接任务：调用方只报 questId，名称、描述与<b>奖励</b>一律读模板表（§14.2）。
        /// <para>
        /// 为什么不复用 <see cref="AcceptQuestAsync"/> 那个签名：它让调用方自带 <c>rewards</c> 字典。
        /// 这条口子一旦接到网络上，客户端就能自己声明"这个任务给我三十本秘籍、五千点好感"，
        /// 整套 §14 习得门槛当场失效——而这些门槛的全部价值就在于它不能绕。
        /// 所以模板是唯一来源，接不到模板就接不了任务（fail closed）。
        /// </para>
        /// </summary>
        public async Task<QuestData?> AcceptFromTemplateAsync(int questId, int characterLevel)
        {
            if (_templates == null)
            {
                _logger.LogWarning("任务模板上下文未注册，无法按模板接任务: QuestId={QuestId}", questId);
                return null;
            }

            var rows = await _templates.QueryAsync(t => t.IsValid);
            var template = rows.FirstOrDefault(t => t.Id == questId);
            if (template == null)
            {
                _logger.LogWarning("任务模板不存在: QuestId={QuestId}", questId);
                return null;
            }

            // 实时关闭在这里生效：publish_state 一改成 0，新接取当场停掉，不用重启也不用发版。
            // 已经接在手上的实例不受影响（提交侧不看这个位），否则关一条任务就等于抹掉玩家的进度。
            if (template.PublishState != (int)Horizon.Game.Sim.Quests.QuestPublishState.Online)
            {
                _logger.LogWarning("任务未发布，拒绝接取: QuestId={QuestId}（publish_state={State}）",
                    questId, template.PublishState);
                return null;
            }

            // 等级门槛必须在接取侧再校一次：它以前只在"可接列表"的查询里过滤，那是**展示层**的过滤，
            // 任何直接指名 questId 的接取请求都绕得过去。等级由网关从角色 Grain 取（权威值），
            // 读不到时传 0 —— 有门槛的任务一律拒，宁可说不清原因，也不能"读不到=没门槛"。
            if (!MeetsLevelRequire(template.LevelRequire, characterLevel))
            {
                _logger.LogWarning("等级不足，拒绝接取: QuestId={QuestId} 需要={Require} 实际={Actual}",
                    questId, template.LevelRequire, characterLevel);
                return null;
            }

            var state = _questState.State;

            if (state.CompletedQuests.TryGetValue(questId, out var completed))
            {
                if (template.RepeatLimit <= 0)
                {
                    _logger.LogWarning("一次性任务已交过，不再提供: QuestId={QuestId}", questId);
                    return null;
                }

                if (!IsInNewPeriod(completed.CompleteTime, template.QuestType))
                {
                    _logger.LogWarning("可重复任务尚未到下一个周期: QuestId={QuestId} type={Type} 上次={Time}",
                        questId, template.QuestType, completed.CompleteTime);
                    return null;
                }

                // 周期重置后允许再接一轮；这条日志就是"第几轮"的审计线索（当前状态结构只留最近一次完成记录）。
                state.CompletedQuests.Remove(questId);
                _logger.LogInformation("可重复任务进入新周期，允许再次接取: QuestId={QuestId}", questId);
            }

            foreach (var prerequisite in Horizon.Game.Sim.Quests.QuestDraft.ParsePrerequisites(template.PrerequisiteQuests))
            {
                if (!state.CompletedQuests.ContainsKey(prerequisite))
                {
                    _logger.LogWarning("前置任务未完成，拒绝接取: QuestId={QuestId} 需要={Prerequisite}",
                        questId, prerequisite);
                    return null;
                }
            }

            await AcceptQuestAsync(template.Id, template.QuestName, template.Description,
                template.QuestType, template.LevelRequire, ParseRewards(template.Rewards));

            // 目标：没有这一段时"接了就能交"，任务这条产出通路就是个空转按钮（§14.2 要求的是
            // 通过任务获得初级书，前提是任务真的要求你做点什么）。
            var objectives = ParseObjectives(template.Objectives, out var rejected);
            foreach (var objective in objectives)
            {
                await AddQuestObjectiveAsync(template.Id, objective.Type, objective.Description, objective.Count);
            }
            if (rejected > 0)
            {
                _logger.LogWarning(
                    "任务模板 {QuestId} 有 {Rejected} 条目标被拒（类型或数量不合法）：宁可少一条目标，也不要一条永远推不动的目标",
                    template.Id, rejected);
            }

            // 登记持有者：运营实时关闭并"强制取消进行中实例"时要能定位到人。
            // 失败不影响接取本身（任务状态才是权威事实），但必须留下可见痕迹。
            try
            {
                var manager = GrainFactory.GetGrain<global::Horizon.Orleans.Interface.World.IQuestManagerGrain>(
                    global::Horizon.Orleans.Interface.World.QuestManagerKeys.Singleton);
                await manager.NotifyAcceptedAsync(template.Id, this.GetPrimaryKey());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "任务持有者登记失败（强制取消将找不到这个实例）: QuestId={QuestId}", template.Id);
            }

            return await GetQuestAsync(template.Id);
        }

        /// <summary>
        /// 接取等级门槛判定。<paramref name="characterLevel"/> 传 0 表示"网关没读到权威等级"——
        /// 这时**有门槛的任务一律不满足**（fail closed），把"读不到"解释成"没门槛"就等于没有这道门。
        /// <para>public static 的理由与 <see cref="IsInNewPeriod"/> 相同：这道门只在 Grain 里判一次的话，
        /// 改错了现场只是"谁都能接高等级任务"，不会有任何报错。</para>
        /// </summary>
        public static bool MeetsLevelRequire(int templateLevelRequire, int characterLevel)
            => templateLevelRequire <= 0 || characterLevel >= templateLevelRequire;

        /// <summary>
        /// 可重复任务是否已进入新周期。日常按自然日、周常按自然周，其余类型只要交过就等下一次重置判断。
        /// <para>public 是为了让这条重置口径能被独立断言：它决定"任务能不能再交一遍"，
        /// 只写在 Grain 内部的话，改错了在测试里看不见。</para>
        /// </summary>
        public static bool IsInNewPeriod(DateTime? lastComplete, int questType)
        {
            if (lastComplete == null || lastComplete.Value == default) return true;
            var previous = lastComplete.Value;
            var now = DateTime.Now;

            return questType switch
            {
                2 => previous.Date < now.Date,                       // 日常：跨自然日
                3 => WeekOf(previous) < WeekOf(now),                 // 周常：跨自然周
                _ => previous.Date < now.Date,                       // 主线/支线标成可重复时，按日兜底
            };
        }

        private static int WeekOf(DateTime value)
        {
            var yearStart = new DateTime(value.Year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
            return value.Year * 100 + (value.DayOfYear - 1) / 7 + (yearStart.DayOfWeek == DayOfWeek.Monday ? 0 : 1);
        }

        /// <summary>
        /// 注销持有者（任务已提交或已放弃，强制取消不必再找这个实例）。
        /// 与登记一样是 best-effort：失败只让"强制取消"多打一次空调用，不影响任务状态。
        /// </summary>
        private async Task NotifyManagerFinishedAsync(int questId)
        {
            try
            {
                var manager = GrainFactory.GetGrain<global::Horizon.Orleans.Interface.World.IQuestManagerGrain>(
                    global::Horizon.Orleans.Interface.World.QuestManagerKeys.Singleton);
                await manager.NotifyFinishedAsync(questId, this.GetPrimaryKey());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "任务持有者注销失败: QuestId={QuestId}", questId);
            }
        }

        /// <summary>
        /// 目标 JSON（<c>[{"type":"kill_domain:3","desc":"猎杀 5 只水域异兽","count":5}]</c>）→ 目标列表。
        /// <para>
        /// <b>为什么在这里就拒而不是在推进时拒</b>：一条永远推不动的目标会让任务卡死在"进行中"，
        /// 玩家什么提示都拿不到；接任务时丢掉它并记日志，代价只是少一路进度，且日志说得出原因。
        /// </para>
        /// <para>
        /// <c>kill_domain:N</c> 的域号必须落在 1..5：这套类型串在 DS 侧由
        /// <c>KillObjectiveTypeMatcher</c> 解析，两处共用同一个字面量，靠
        /// <c>QuestObjectiveContractTests</c> 里那条"接出来的目标能被匹配器认下"的对账测试兜住分叉。
        /// </para>
        /// </summary>
        internal static List<(string Type, string Description, int Count)> ParseObjectives(
            string? json, out int rejected)
        {
            var objectives = new List<(string, string, int)>();
            rejected = 0;
            if (string.IsNullOrWhiteSpace(json)) return objectives;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) { rejected = 1; return objectives; }

                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Object) { rejected++; continue; }

                    var type = ReadString(element, "type");
                    var description = ReadString(element, "desc") ?? ReadString(element, "description") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(type)) { rejected++; continue; }

                    if (!element.TryGetProperty("count", out var countElement)
                        || countElement.ValueKind != JsonValueKind.Number
                        || !countElement.TryGetInt32(out var count) || count <= 0)
                    {
                        rejected++;
                        continue;
                    }

                    // 语法只有一份权威：问任务目标编解码器（编辑器、匹配器、种子共用它）。
                    // 不认识的一律拒——类型串没有消费方等于把任务永久挂起，比少一条目标更糟。
                    if (!Horizon.Game.Sim.Quests.QuestObjectiveCodec.TryDecode(type, out var spec))
                    {
                        rejected++;
                        continue;
                    }

                    // 带冒号却没解出正对象 id（kill_monster:、talk_npc:0）谁都推不动；
                    // 无冒号的历史裸串（kill / hunt）留 0 是合法的，匹配器按关键词兜底认它。
                    if (type.IndexOf(':') >= 0 && spec.TemplateId <= 0)
                    {
                        rejected++;
                        continue;
                    }

                    objectives.Add((type.Trim(), description, count));
                }
            }
            catch (JsonException)
            {
                // 整份 JSON 坏掉：任务照接，只是没有目标；rejected=1 让调用方把这条记进日志。
                rejected = Math.Max(rejected, 1);
            }

            return objectives;
        }

        private static string? ReadString(JsonElement element, string name)
            => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        /// <summary>
        /// 奖励 JSON（<c>{"item:810101":1,"favor:1":20}</c>）→ 字典。
        /// 只丢脏的那一条键，不让整个任务因为一个写坏的数值接不上。
        /// </summary>
        internal static Dictionary<string, int> ParseRewards(string? json)
        {
            var rewards = new Dictionary<string, int>();
            if (string.IsNullOrWhiteSpace(json)) return rewards;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return rewards;

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    // TryGetInt32 遇到字符串值是抛异常而不是返回 false，所以先看 ValueKind。
                    if (prop.Value.ValueKind == JsonValueKind.Number
                        && prop.Value.TryGetInt32(out var value) && value > 0)
                        rewards[prop.Name] = value;
                }
            }
            catch (JsonException)
            {
                // 整份 JSON 坏掉：返回空表，任务照样能接，只是不发东西——发奖侧会记日志，比抛出来好。
            }

            return rewards;
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
                quest.CompleteTime = DateTime.Now;

                // Move from active to completed
                state.ActiveQuests.Remove(questId);
                state.CompletedQuests[questId] = quest;

                await _questState.WriteStateAsync();
                await NotifyManagerFinishedAsync(questId);

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
                await NotifyManagerFinishedAsync(questId);

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
