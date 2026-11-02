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
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 技能系统Grain实现 - 负责技能学习、释放、冷却管理
    /// </summary>
    public class SkillGrain : Grain, ISkillGrain
    {
        private readonly ILogger<SkillGrain> _logger;
        private readonly IPersistentState<SkillState> _skillState;

        private bool _isCasting;
        private int _castingSkillId;
        private ulong _castingCasterId;

        public bool IsCasting => _isCasting;

        public SkillGrain(
            ILogger<SkillGrain> logger,
            [PersistentState("skill", "GameStore")] IPersistentState<SkillState> skillState,
            IDataContext<GameEntityContext, SkillTemplateEntity, int>? skillTemplateContext = null,
            IDataContext<GameEntityContext, SkillBookEntity, int>? skillBookContext = null)
        {
            _logger = logger;
            _skillState = skillState;
            _skillTemplateContext = skillTemplateContext;
            _skillBookContext = skillBookContext;
        }

        /// <summary>技能模板表上下文。可为 null（未注入/单测）→ 目录下行如实回失败。</summary>
        private readonly IDataContext<GameEntityContext, SkillTemplateEntity, int>? _skillTemplateContext;

        /// <summary>秘籍表上下文。可为 null → 研读路径按"目录不可用"如实失败，不放行学习。</summary>
        private readonly IDataContext<GameEntityContext, SkillBookEntity, int>? _skillBookContext;

        /// <summary>
        /// 模板与秘籍是静态参考数据，按 Grain 生命周期缓存一份：
        /// 演化封存判定要遍历已学集合反查模板，逐次查库会把施放路径拖成 N 次查询。
        /// </summary>
        private Dictionary<int, SkillTemplateEntity>? _templates;
        private Dictionary<int, SkillBookEntity>? _books;

        /// <summary>
        /// 角色 grain 的取用缝隙。默认走 <c>GrainFactory</c>；测试注入假实现——
        /// 走工厂的跨 grain 调用在脚手架里会被 <c>catch</c> 静默吞掉，"死亡判定到底读的是什么"
        /// 这种关键分支就永远拿不到断言（本仓已踩过两次，见 CombatGrain._skillGrainProvider）。
        /// </summary>
        private Func<ulong, ICharacterGrain>? _characterGrainProvider;

        private Func<ulong, ICharacterGrain> CharacterGrainOf()
            => _characterGrainProvider ??= id => GrainFactory.GetGrain<ICharacterGrain>((long)id);

        private async Task EnsureReferenceDataAsync()
        {
            if (_templates != null && _books != null) return;

            if (_skillTemplateContext != null && _templates == null)
            {
                var loaded = new Dictionary<int, SkillTemplateEntity>();
                var rows = await _skillTemplateContext.QueryAsync(t => t.Id > 0);
                foreach (var row in rows)
                    loaded[row.Id] = row;
                _templates = loaded;
            }

            if (_skillBookContext != null && _books == null)
            {
                var loaded = new Dictionary<int, SkillBookEntity>();
                var rows = await _skillBookContext.QueryAsync(b => b.SkillId > 0);
                foreach (var row in rows)
                    loaded[row.SkillId] = row;
                _books = loaded;
            }
        }

        // ===================================================================
        // 五行技能树：阶/境门槛、演化封存
        // ===================================================================

        /// <summary>模板 learn_cost（演化源 + 周天/道阶计数门槛）。</summary>
        private static WuxingEvolveRules.LearnCost LearnCostOf(SkillTemplateEntity? template)
            => WuxingEvolveRules.Parse(template?.LearnCost);

        /// <summary>
        /// 已封存集合 = 被某个<b>已学</b>演化式融合的源技。
        /// <para>
        /// 刻意派生而不落库：散功（删掉演化式）后源技自动解封，不会出现
        /// "忘改标记导致格子永久灰着"的僵尸态；栏位/技能树/施放三处判定也天然一致。
        /// </para>
        /// </summary>
        private HashSet<int> ComputeRetired()
        {
            var retired = new HashSet<int>();
            if (_templates == null) return retired;

            foreach (var skillId in _skillState.State.LearnedSkills.Keys)
            {
                if (!_templates.TryGetValue(skillId, out var template)) continue;
                foreach (var source in LearnCostOf(template).Consume)
                    retired.Add(source);
            }
            return retired;
        }

        /// <summary>取该行亲和（五行点），来源是服务端权威加点分配，与客户端雷达图同源。</summary>
        private static int AffinityOf(AttributeAllocationData? allocation, int wuxingElement) => wuxingElement switch
        {
            1 => allocation?.Metal ?? 0,
            2 => allocation?.Wood ?? 0,
            3 => allocation?.Water ?? 0,
            4 => allocation?.Fire ?? 0,
            5 => allocation?.Earth ?? 0,
            _ => int.MaxValue,          // 无属性式不因亲和被卡
        };

        /// <summary>技能所属五行。模板列 wuxing_element 权威（0=无属性式，不受亲和门槛约束）。</summary>
        private static int ElementOf(SkillTemplateEntity template) => template.WuxingElement;

        /// <summary>
        /// 按模板行建 SkillInfo（消耗/冷却/射程/上限都来自表，不再写死一份 3000ms/10 气的假数据）。
        /// 公式列存的是常数串，解析不了按 0 处理。
        /// </summary>
        private static SkillInfo BuildSkillInfo(SkillTemplateEntity template)
        {
            return new SkillInfo
            {
                SkillId = template.Id,
                SkillName = template.SkillName ?? "",
                Description = template.Description ?? "",
                Icon = template.IconPath ?? "",
                SkillType = template.SkillType,
                Level = 1,
                MaxLevel = template.MaxLevel > 0 ? template.MaxLevel : 10,
                NeiLiCost = (int)ParseFormula(template.EnergyCostFormula),
                Cooldown = (long)(ParseFormula(template.CooldownFormula) * 1000f),
                CastTime = (long)(ParseFormula(template.CastTimeFormula) * 1000f),
                Range = template.AttackRange,
            };
        }

        /// <summary>
        /// 把这一式的材料清单随回执带回去。<b>只报清单，不在这里扣</b>：
        /// SkillGrain 看不到背包（也不该去看，那条边会和研读通道凑成 Grain 环），
        /// 真正的扣减由 InventoryGrain 在习得提交之后执行。
        /// </summary>
        private static void AttachMaterialDemand(LearnSkillResponse response, SkillTemplateEntity template)
        {
            if (response == null || template == null) return;
            foreach (var need in WuxingEvolveRules.Parse(template.LearnCost).Materials
                     ?? System.Array.Empty<WuxingEvolveRules.MaterialNeed>())
            {
                response.MaterialItemIds.Add(need.ItemId);
                response.MaterialCounts.Add(need.Count);
            }
        }

        /// <summary>
        /// 学习门槛检查（等级、亲和、前置式、演化源式）。返回 null 表示可以学。
        /// </summary>
        private string? CheckLearnGates(SkillTemplateEntity template, CharacterInfo? info, bool requireEvolveSourcesMaxed,
            LearnGateContext? dynamicContext = null)
        {
            var level = info?.Level ?? 0;
            if (template.LearnLevelRequire > 0 && level < template.LearnLevelRequire)
                return $"等级不足：需 {template.LearnLevelRequire}，当前 {level}";

            // 境界（阶序 1..6）按 §2.1 由等级派生后再判：表里 learn_realm_require 一直有值却没人读过，
            // "数据在、代码不判"的门槛比没有这列更坏——看上去生效了，实际形同虚设。
            var realmBlocker = ProgressionCurve.RealmBlocker(level, template.LearnRealmRequire);
            if (realmBlocker != null) return realmBlocker;

            int affinity = AffinityOf(info?.Allocation, ElementOf(template));
            if (template.ComprehensionRequire > 0 && affinity < template.ComprehensionRequire)
                return $"五行亲和不足：需 {template.ComprehensionRequire}，当前 {affinity}";

            var state = _skillState.State;
            if (template.PreSkillId is int preId && preId > 0 && !state.LearnedSkills.ContainsKey(preId))
            {
                string preName = _templates != null && _templates.TryGetValue(preId, out var pre)
                    ? pre.SkillName : preId.ToString();
                return $"前置招式未习得：{preName}";
            }

            var cost = LearnCostOf(template);

            // 演化式：两段弧的源技都得已学（研读路径还要练满，§6.2 的"圆满"用 Level==MaxLevel 表达）
            foreach (var sourceId in cost.Consume)
            {
                if (!state.LearnedSkills.TryGetValue(sourceId, out var learned))
                {
                    string name = _templates != null && _templates.TryGetValue(sourceId, out var t) ? t.SkillName : sourceId.ToString();
                    return $"演化源未习得：{name}";
                }
                if (requireEvolveSourcesMaxed && learned.MaxLevel > 0 && learned.Level < learned.MaxLevel)
                {
                    string name = _templates != null && _templates.TryGetValue(sourceId, out var t) ? t.SkillName : sourceId.ToString();
                    return $"演化源未圆满：{name}（{learned.Level}/{learned.MaxLevel}）";
                }
            }

            // 周天圆满次数与道阶系对数（§6.2 的后两条）。普通式这两个字段恒为 0，判定自然放行，
            // 因此不必先问"这是不是演化式"——把混沌终式漏掉的正是这种"按字段判而不是按身份判"的写法。
            var cycleBlocker = WuxingEvolveRules.CheckCycleGates(cost, state.LoopCompletionCount, CountCompleteEvolutionPairs());
            if (cycleBlocker != null) return cycleBlocker;

            // §14 的动态门槛（区域好感 / 特殊材料 / 基础属性）。判定是纯函数，
            // 事实由调用方在服务端装配 —— SkillGrain 不去调 InventoryGrain/FavorabilityGrain，
            // 否则与"秘籍研读"那条边凑成 Grain 环，非重入 Grain 当场死锁。
            return WuxingDynamicGates.Check(cost, dynamicContext);
        }

        /// <summary>已练满的道阶<b>系</b>数（一对三境俱全才算一系），供 §6.2 的"任意 3 对"门槛用。</summary>
        private int CountCompleteEvolutionPairs()
        {
            if (_templates == null) return 0;

            var maxedDao = new List<int>();
            foreach (var (skillId, info) in _skillState.State.LearnedSkills)
            {
                if (info.MaxLevel > 0 && info.Level < info.MaxLevel) continue;
                if (_templates.TryGetValue(skillId, out var template) && template.SkillGrade == 6)
                    maxedDao.Add(skillId);
            }

            return WuxingEvolveRules.CountCompleteEvolutionPairs(maxedDao);
        }

        /// <summary>
        /// 研读秘籍习得技能：在 <see cref="LearnSkillAsync"/> 的门槛之上，额外要求演化源式已练满，
        /// 并按秘籍表 learn_success_rate 掷签。
        /// <para>
        /// 掷签失败<b>也吃掉一本</b>：不然同一本可以反复重读刷概率。而门槛未达（等级/亲和/前置）
        /// <b>不吃书</b>——书还在手上，练够了再来读，玩家不该因为还没长级被罚款。
        /// </para>
        /// </summary>
        public async Task<LearnSkillResponse> LearnFromBookAsync(long characterId, int skillId, LearnGateContext? dynamicContext = null)
        {
            await EnsureReferenceDataAsync();

            if (_templates == null || !_templates.TryGetValue(skillId, out var template))
                return FailedLearn("技能模板不存在，服务端不放行");

            var state = _skillState.State;
            if (state.LearnedSkills.ContainsKey(skillId))
                return FailedLearn("已习得此式");

            var info = await (await CharacterAsync(characterId)).GetCharacterInfoAsync(null);

            var blocker = CheckLearnGates(template, info, requireEvolveSourcesMaxed: true, dynamicContext);
            if (blocker != null) return FailedLearn(blocker);

            // 秘籍成功率：表里没有这本 → 按 100% 走（门派传授/补读路径不配书也能学）
            double rate = 1.0;
            if (_books != null && _books.TryGetValue(skillId, out var book) && book.LearnSuccessRate > 0)
                rate = book.LearnSuccessRate;
            if (rate < 1.0 && _rng.NextDouble() > rate)
            {
                // 掷签失败也吃掉一本：否则同一本可以反复重读刷概率
                var spent = FailedLearn($"参悟未成（成功率 {rate:P0}），此卷已尽");
                spent.BookConsumed = true;
                return spent;
            }

            var learned = await CommitLearnAsync(template, info);
            AttachMaterialDemand(learned, template);
            return learned;
        }

        /// <summary>
        /// 散功：忘掉一式。删掉演化式即可让被封存的源技自动回到可用状态（封存是派生态）。
        /// </summary>
        public async Task<bool> UnlearnAsync(long characterId, int skillId)
        {
            var state = _skillState.State;
            if (!state.LearnedSkills.Remove(skillId)) return false;

            state.SkillCooldowns.Remove(skillId);
            await _skillState.WriteStateAsync();
            _logger.LogInformation("散功成功: SkillId={SkillId}，相关演化源自动解封", skillId);
            return true;
        }

        private async Task<LearnSkillResponse> CommitLearnAsync(SkillTemplateEntity template, CharacterInfo? info)
        {
            var state = _skillState.State;
            state.LearnedSkills[template.Id] = BuildSkillInfo(template);
            await _skillState.WriteStateAsync();

            _logger.LogInformation("习得技能: SkillId={SkillId}, Name={Name}, Level={Level}",
                template.Id, template.SkillName, info?.Level ?? 0);

            return new LearnSkillResponse { Success = true, Message = $"习得《{template.SkillName}》" };
        }

        private static LearnSkillResponse FailedLearn(string message) => new() { Success = false, Message = message };

        /// <summary>顺生窗口（秒）：上一式之后多久内打出"子行"才算续上链。</summary>
        private const int CycleWindowSeconds = 3;

        /// <summary>每层节数的全系增伤。</summary>
        private const float CycleBonusPerStack = 0.03f;

        /// <summary>周天圆满那一击的额外系数。</summary>
        private const float CycleCompletionMultiplier = 1.25f;

        /// <summary>满层即周天：清零、计数 +1，本击仍按圆满算。</summary>
        private const int CycleMaxStack = 5;

        /// <summary>
        /// 推进一次五行循环。规则（方案 §5.1）：窗口超时则重新起链；
        /// 打出上一行的"子行"为顺生 → 节数 +1；同属与逆生都不掉层，只把链移到当前行并刷新窗口。
        /// 无属性式（0）完全不碰链，避免把混沌/身法之类算成断链。
        /// </summary>
        private (int Stack, float Multiplier, bool Completed, int CompletionCount) AdvanceCycle(int element)
        {
            var state = _skillState.State;
            var step = CombatCalculator.StepWuxingCycle(
                state.LoopStack, state.LoopLastElement, state.LoopExpireAt, state.LoopCompletionCount,
                element, DateTime.UtcNow, CycleWindowSeconds, CycleMaxStack, CycleBonusPerStack, CycleCompletionMultiplier);

            state.LoopStack = step.Stack;
            state.LoopLastElement = step.LastElement;
            state.LoopExpireAt = step.ExpireAt;
            state.LoopCompletionCount = step.CompletionCount;

            // §5.3：周天清零后从镇岳行重新起链。玩家选的这一行因此有两个作用——
            // 既是"谁克我"，也是下一轮相生从哪儿起步；不指定时留在刚打过的这一行，等于顺着刚才的势头续。
            if (step.Completed && state.GuardElement > 0)
                state.LoopLastElement = state.GuardElement;

            return (step.Stack, step.Multiplier, step.Completed, step.CompletionCount);
        }

        /// <summary>
        /// 破势：夺走本角色一层节数。护体行在这里判（镇岳是本 Grain 的状态），
        /// 调用方只把它从防御向量算出的兜底行传进来——判定口径因此只有一处。
        /// 只动链上的"层"，不重置 lastElement：被夺一次不该顺手把人家辛苦续上的相生方向也抹掉。
        /// </summary>
        public async Task<bool> BreakCycleAsync(int attackElement, int dominantDefenseRow)
        {
            var state = _skillState.State;
            int guardRow = CombatCalculator.ResolveGuardRow(state.GuardElement, dominantDefenseRow);
            if (!CombatCalculator.Counters(attackElement, guardRow)) return false;
            if (state.LoopStack <= 0) return false;

            state.LoopStack--;
            await _skillState.WriteStateAsync();
            return true;
        }

        /// <summary>镇岳切换冷却（秒，§5.3）。</summary>
        private const int GuardSwitchSeconds = 10;

        /// <summary>
        /// 指定护体行。只能选已学技能所在的那一行（0 = 取消指定，回到"防御最高行"自动口径）：
        /// 把没练过的行当护体等于凭空多一面盾，也会让破势判定打在一个玩家根本没能力反击的行上。
        /// </summary>
        public async Task<GuardElementResult> SetGuardElementAsync(int element)
        {
            var state = _skillState.State;
            var now = DateTime.UtcNow;
            float remaining = (float)Math.Max(0, (state.GuardChangedAt.AddSeconds(GuardSwitchSeconds) - now).TotalSeconds);

            var result = new GuardElementResult
            {
                GuardElement = state.GuardElement,
                NextChangeInSeconds = remaining,
            };

            if (element < 0 || element > 5)
            {
                result.Message = "镇岳行只认五行（0=自动）";
                return result;
            }

            if (remaining > 0f)
            {
                result.Message = $"镇岳切换冷却中，剩余 {remaining:F1} 秒";
                return result;
            }

            if (element > 0 && !HasLearnedRow(element))
            {
                result.Message = $"尚未习得该行招式，无法以其镇岳";
                return result;
            }

            state.GuardElement = element;
            state.GuardChangedAt = now;
            await _skillState.WriteStateAsync();

            result.Success = true;
            result.GuardElement = element;
            result.NextChangeInSeconds = GuardSwitchSeconds;
            result.Message = element > 0 ? $"镇岳于行 {element}" : "镇岳已放开（自动取防御最高行）";
            return result;
        }

        /// <summary>本角色是否练过这一行的任意一式。</summary>
        private bool HasLearnedRow(int element)
        {
            if (_templates == null) return true;     // 模板不可用时不因数据缺失误锁玩家
            foreach (var skillId in _skillState.State.LearnedSkills.Keys)
                if (_templates.TryGetValue(skillId, out var t) && t.WuxingElement == element)
                    return true;
            return false;
        }

        /// <summary>夺元成功：攻方 +1 层，满 5 层同样算一次周天圆满。</summary>
        public async Task<bool> GrantCycleAsync()
        {
            var state = _skillState.State;
            state.LoopStack++;
            if (state.LoopStack >= CycleMaxStack)
            {
                state.LoopStack = 0;
                state.LoopCompletionCount++;
            }
            state.LoopExpireAt = DateTime.UtcNow.AddSeconds(CycleWindowSeconds);
            await _skillState.WriteStateAsync();
            return true;
        }

        /// <summary>取角色 Grain。等级与五行亲和都在 CharacterGrain 那边，学习门槛必须问权威侧。</summary>
        private Task<ICharacterGrain> CharacterAsync(long characterId)
            => Task.FromResult(GrainFactory.GetGrain<ICharacterGrain>(characterId));

        /// <summary>研读掷签。Grain 单线程执行，静态 Random 足够；道阶 35% 这类低概率必须真掷。</summary>
        private static readonly Random _rng = new Random();


        /// <summary>
        /// 技能模板目录 + 本角色已学技能的一次性下行。
        /// <para>
        /// 读失败时如实回 <c>Success=false</c> 且不带半份目录：客户端据此保留本地兜底表，
        /// 而不是把"查询失败"当成"服务端确认没有任何技能"——后者会让技能栏整屏清空。
        /// </para>
        /// </summary>
        public async Task<SkillCatalogSyncMessage> GetSkillCatalogAsync()
        {
            var response = new SkillCatalogSyncMessage();

            if (_skillTemplateContext == null)
            {
                response.Message = "技能模板表不可用";
                return response;
            }

            try
            {
                var query = await _skillTemplateContext.QueryAsync(t => t.Id > 0);
                foreach (var t in query.OrderBy(t => t.Id))
                {
                    response.Templates.Add(SkillCatalogMapper.ToEntry(t));
                }

                response.Learned = _skillState.State.LearnedSkills?.Values.ToList()
                                     ?? new List<Horizon.Game.Message.Network.SkillInfo>();
                // 周天与镇岳的权威状态一并下行：客户端指示器不该靠"事后累积施放回执"来猜自己有几层，
                // 重登/换端或刚被破势之后，只有服务端能给得出这一帧。
                var cycle = _skillState.State;
                response.LoopStack = cycle.LoopStack;
                response.LoopLastElement = cycle.LoopLastElement;
                response.LoopCompletionCount = cycle.LoopCompletionCount;
                response.GuardElement = cycle.GuardElement;


                response.Success = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取技能模板目录失败，客户端将沿用本地兜底技能表。");
                response.Success = false;
                response.Message = "读取技能模板目录失败";
                response.Templates.Clear();
                response.Learned.Clear();
            }

            return response;
        }

        /// <summary>
        /// 技能表的消耗/冷却列存的是公式串（现有内容一律是常数），下行前折成数值。
        /// 解析不了按 0 处理而不是抛：一个脏字符串不该拖垮整份目录。
        /// </summary>
        /// <summary>公式列折数值。口径与目录下行共用一处（见 <see cref="SkillCatalogMapper.ParseFormula"/>）。</summary>
        private static float ParseFormula(string formula) => SkillCatalogMapper.ParseFormula(formula);

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("SkillGrain {GrainKey} activating.", this.GetPrimaryKey());

            if (_skillState.State.LearnedSkills == null)
                _skillState.State.LearnedSkills = new Dictionary<int, SkillInfo>();

            if (_skillState.State.SkillCooldowns == null)
                _skillState.State.SkillCooldowns = new Dictionary<int, DateTime>();

            if (_skillState.State.SkillDependencies == null)
                _skillState.State.SkillDependencies = new Dictionary<int, List<int>>();

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<SkillCastMessage> CastSkillAsync(SkillCastMessage request)
        {
            try
            {
                _logger.LogInformation("处理技能施放: SkillId={SkillId}", request.SkillId);

                var state = _skillState.State;

                // 检查是否已学习该技能
                if (!state.LearnedSkills.ContainsKey(request.SkillId))
                {
                    return new SkillCastMessage
                    {
                        CasterId = request.CasterId,
                        SkillId = request.SkillId,
                        Success = false,
                        Message = "未学习该技能"
                    };
                }

                var skill = state.LearnedSkills[request.SkillId];

                // 演化换代后源技封存：学还在、进度还在，但不再可用（散功删掉演化式即自动恢复）。
                await EnsureReferenceDataAsync();
                if (ComputeRetired().Contains(request.SkillId))
                {
                    return new SkillCastMessage
                    {
                        CasterId = request.CasterId,
                        SkillId = request.SkillId,
                        Success = false,
                        Message = "该式已融于演化式，需先散功"
                    };
                }

                // 检查冷却
                if (state.SkillCooldowns.TryGetValue(request.SkillId, out var lastCast))
                {
                    if (!CombatCalculator.IsSkillReady(lastCast, skill.Cooldown))
                    {
                        var remaining = CombatCalculator.GetRemainingCooldown(lastCast, skill.Cooldown);
                        return new SkillCastMessage
                        {
                            CasterId = request.CasterId,
                            SkillId = request.SkillId,
                            Success = false,
                            Message = $"技能冷却中，剩余{remaining:F1}秒"
                        };
                    }
                }

                // 记录冷却
                state.SkillCooldowns[request.SkillId] = DateTime.Now;

                if (request.TargetEntityId > 0)
                {
                    var targetGrain = GrainFactory.GetGrain<ICharacterGrain>((long)request.TargetEntityId);
                    var targetInfo = await targetGrain.GetCharacterInfoAsync(null);
                    if (targetInfo == null)
                    {
                        return new SkillCastMessage
                        {
                            CasterId = request.CasterId,
                            SkillId = request.SkillId,
                            Success = false,
                            Message = "目标不存在"
                        };
                    }
                }

                if (skill.Range > 0 && request.TargetEntityId > 0)
                {
                    var casterX = request.StartPosition.X;
                    var casterZ = request.StartPosition.Z;
                    var targetX = request.TargetPosition.X;
                    var targetZ = request.TargetPosition.Z;

                    if (casterX != 0 || casterZ != 0 || targetX != 0 || targetZ != 0)
                    {
                        float dx = targetX - casterX;
                        float dz = targetZ - casterZ;
                        float distance = MathF.Sqrt(dx * dx + dz * dz);
                        if (distance > skill.Range)
                        {
                            return new SkillCastMessage
                            {
                                CasterId = request.CasterId,
                                SkillId = request.SkillId,
                                Success = false,
                                Message = "目标超出技能范围"
                            };
                        }
                    }
                }

                var casterGrain = CharacterGrainOf()(request.CasterId);
                var casterInfo = await casterGrain.GetCharacterInfoAsync(null);
                // "死了"必须是真死：有血上限而血为 0。
                // 只看 CurrentHealth<=0 会把"服务端属性还没算过"（MaxHealth=0 的新角色/未同步存档）
                // 判成死亡，表现是玩家一进游戏所有技能都回"施法者已死亡"，
                // 而角色在客户端看起来活得好好的——它的血是本地聚合出来的，服务端那份还是 0。
                if (casterInfo != null && casterInfo.MaxHealth > 0f && casterInfo.CurrentHealth <= 0f)
                {
                    return new SkillCastMessage
                    {
                        CasterId = request.CasterId,
                        SkillId = request.SkillId,
                        Success = false,
                        Message = "施法者已死亡"
                    };
                }

                // 血代价招式（G27）：先算清"这式要付多少、够不够付"，不够就不放行。
                // 放在这里（死亡判定之后、写冷却与状态之前）是为了和上面的死亡判定同一条纪律：
                // 被挡下的施放不该顺手把冷却状态写进存档。
                string? castEffects = _templates != null && _templates.TryGetValue(request.SkillId, out var effectsTemplate)
                    ? effectsTemplate.SkillEffects
                    : null;
                if (!TryPlanSkillHealthFlow(casterInfo?.MaxHealth ?? 0f, casterInfo?.CurrentHealth ?? 0f,
                        castEffects, out var healthFlow, out var healthRefusal))
                {
                    return new SkillCastMessage
                    {
                        CasterId = request.CasterId,
                        SkillId = request.SkillId,
                        Success = false,
                        Message = healthRefusal ?? "代价不足",
                    };
                }

                _isCasting = true;
                _castingSkillId = request.SkillId;
                _castingCasterId = request.CasterId;

                // 五行循环节在这里结算：SkillHandler 会把本方法返回的消息原样交给
                // CombatGrain.ProcessSkillCastAsync 做伤害，权威系数必须搭同一条链路过去。
                // 不走"CombatGrain 反过来调 SkillGrain"那条路——SkillGrain 此刻正 await 着
                // 下游 Grain，非重入 Grain 的这种回身调用就是死锁的常客。
                var loop = AdvanceCycle(_templates != null && _templates.TryGetValue(request.SkillId, out var skillTemplate)
                    ? skillTemplate.WuxingElement : 0);
                request.LoopStack = loop.Stack;
                request.LoopMultiplier = loop.Multiplier;
                request.CycleCompleted = loop.Completed;
                request.LoopCompletionCount = loop.CompletionCount;

                await _skillState.WriteStateAsync();

                // §14.7 G26/G27：把血量收支落到权威 HP 上。代价先付、回复后吃，
                // 只认服务端返回的实际变化量。补的永远是施法者自己：`toAllies` 还没实现，
                // "顺手也奶一下目标"等于把没做的功能伪造成做过了。
                var applied = await ApplySkillHealthFlowAsync(request, casterGrain, healthFlow);
                var outcome = DescribeHealthOutcome(applied);

                _logger.LogInformation("技能施放成功: SkillId={SkillId}, 自损={Cost}, 回复={Heal}",
                    request.SkillId, applied.Cost, applied.Heal);

                return new SkillCastMessage
                {
                    CasterId = request.CasterId,
                    SkillId = request.SkillId,
                    Success = true,
                    // 数字直接写进回执：客户端无需改协议就能看到"这式真的付了多少/回了多少"，
                    // PIE 目视也有一条可核对的具体值（而不是"看起来好像活着"）。
                    Message = outcome is null ? "技能施放成功" : $"技能施放成功，{outcome}",
                    EnergyCost = EffectiveNeiLiCost(skill),
                    CastTime = skill.CastTime,
                    Range = skill.Range,
                    TargetEntityId = request.TargetEntityId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "技能施放失败: SkillId={SkillId}", request.SkillId);
                throw;
            }
        }

        /// <summary>
        /// 这一式此刻**实际**该收多少内力：模板基数在手就按重数推导，模板取不到才退回存储值。
        /// <para>
        /// 为什么读点也要推一遍：<c>UpgradeSkillAsync</c> 只在"还能往上练"时才重写存储值，
        /// 而旧口径（每重 ×1.1）已经把不少式子推到 <c>Level == MaxLevel</c> 的死角——
        /// 它们再也点不到升级按钮，于是那笔虚高耗气会永久留在存档里，症状是
        /// "同样满重的两式，先练满的那式明显更费蓝"。放在读点，它们在下一次施放时自己归位。
        /// </para>
        /// </summary>
        internal int EffectiveNeiLiCost(SkillInfo skill)
        {
            if (skill == null) return 0;
            int baseCost = ResolveBaseNeiLiCost(skill.SkillId);
            return baseCost > 0 ? ComputeNeiLiCostAtLevel(baseCost, skill.Level) : skill.NeiLiCost;
        }

        /// <summary>一次施放的血量收支：两条各自独立的效果（<c>hpCost</c> 代价 / <c>healPct</c> 回复）。</summary>
        internal readonly record struct SkillHealthFlow(int Cost, int Heal)
        {
            public bool IsEmpty => Cost == 0 && Heal == 0;
        }

        /// <summary>
        /// 纯函数：按施法者当前血池与这式的效果 JSON，算出该付与该回各多少（G27）。
        /// <para>
        /// 三条刻意边界，与 G26 同源：① 实现侧不给默认值，比例缺失/≤0/非数值都算 0
        /// （把"没配"变成"配了个错的"比不配更难查）；② 血上限还没建立（<c>MaxHealth &lt;= 0</c>）时
        /// 既不收费也不回血，<b>但也不拦施放</b>——拦了就等于让 G15 那个"不存在的池子"重新咬玩家；
        /// ③ 算不足 1 点的比例当 0。
        /// </para>
        /// <para>
        /// 唯一一条拒绝：<b>代价会把人打死</b>时不放行（"以血为引"不是自杀按钮）。
        /// 这只是把"故意自杀"挡在门外；挡不掉"我们扣血的同时对手正好也打了我一口"那种并发，
        /// 那一口由 <c>CharacterGrain</c> 权威裁决，我们只如实上报它认下的数。
        /// </para>
        /// </summary>
        internal static bool TryPlanSkillHealthFlow(
            float maxHealth, float currentHealth, string? effectsJson,
            out SkillHealthFlow flow, out string? refusal)
        {
            flow = new SkillHealthFlow(0, 0);
            refusal = null;
            if (maxHealth <= 0f) return true;

            int cost = ScaleByRatio(maxHealth,
                Horizon.Game.Sim.Skills.WuxingSkillEffects.ReadNumber(effectsJson, "hpCost"));
            int heal = ScaleByRatio(maxHealth,
                Horizon.Game.Sim.Skills.WuxingSkillEffects.ReadNumber(effectsJson, "healPct"));

            if (cost > 0 && currentHealth - cost <= 0f)
            {
                refusal = "气血不足以承此式代价";
                return false;
            }

            flow = new SkillHealthFlow(cost, heal);
            return true;
        }

        /// <summary>比例 × 上限，取整；比例缺失、非正或算不足 1 点都是 0。</summary>
        private static int ScaleByRatio(float maxHealth, double? ratio) =>
            ratio is > 0d ? (int)(maxHealth * ratio.Value) : 0;

        /// <summary>回执文案：只报真发生的数字；两个都没发生就返回 null（让调用方回到普通"施放成功"）。</summary>
        private static string? DescribeHealthOutcome(SkillHealthFlow applied)
        {
            if (applied.IsEmpty) return null;
            if (applied.Cost > 0 && applied.Heal > 0) return $"自损 {applied.Cost}、回复 {applied.Heal} 点气血";
            if (applied.Cost > 0) return $"自损 {applied.Cost} 点气血";
            return $"回复 {applied.Heal} 点气血";
        }

        /// <summary>
        /// 把计划落到权威血量上：代价先付、回复后吃，两条各自走 <c>RequestHpChangeAsync</c>，
        /// 并各自用返回的 <see cref="HpChangeResult.ActualDelta"/> 记账——被上限挡掉的回血、
        /// 或被抵抗掉的自伤，都不能算进玩家看到的数字（假反馈比没反馈更难查）。
        /// </summary>
        private async Task<SkillHealthFlow> ApplySkillHealthFlowAsync(
            SkillCastMessage request, ICharacterGrain grain, SkillHealthFlow flow)
        {
            if (flow.IsEmpty) return flow;

            int paid = 0, healed = 0;

            if (flow.Cost > 0)
            {
                var costResult = await grain.RequestHpChangeAsync(-flow.Cost, request.CasterId,
                    Horizon.Orleans.Interface.World.DamageType.Internal);
                paid = costResult.ActualDelta < 0 ? -costResult.ActualDelta : 0;
            }

            if (flow.Heal > 0)
            {
                var healResult = await grain.RequestHpChangeAsync(flow.Heal, request.CasterId,
                    Horizon.Orleans.Interface.World.DamageType.Healing);
                healed = healResult.ActualDelta > 0 ? healResult.ActualDelta : 0;
            }

            return new SkillHealthFlow(paid, healed);
        }

        public async Task<LearnSkillResponse> LearnSkillAsync(long characterId, int skillId, LearnGateContext? dynamicContext = null)
        {
            try
            {
                await EnsureReferenceDataAsync();
                var state = _skillState.State;

                if (state.LearnedSkills.ContainsKey(skillId))
                {
                    _logger.LogWarning("技能已学习: SkillId={SkillId}", skillId);
                    return FailedLearn("已习得此式");
                }

                // 模板里有这一式 → 按模板建行并过门槛（等级/亲和/前置）。
                var template = _templates != null && _templates.TryGetValue(skillId, out var t) ? t : null;
                if (template != null)
                {
                    // §14.5：通道不再由客户端上报的 LearnMethod 决定。这一式在秘籍表里登记过书 →
                    // 必须真持有一本书来研读；没登记过的行（旧 26 技、剧情授予的特殊式）才允许直接习得。
                    // 白阶也在这条规则里：它的书就是任务与刷怪产出的，不给绕过。
                    var book = _books != null && _books.TryGetValue(template.Id, out var registered) ? registered : null;
                    var channelBlocker = WuxingEvolveRules.DirectLearnBlocker(book != null, book?.BookName);
                    if (channelBlocker != null)
                    {
                        _logger.LogInformation("直接学习通道不放开: SkillId={SkillId}, 原因={Reason}", skillId, channelBlocker);
                        return FailedLearn(channelBlocker);
                    }

                    var info = await (await CharacterAsync(characterId)).GetCharacterInfoAsync(null);
                    var blocker = CheckLearnGates(template, info, requireEvolveSourcesMaxed: false, dynamicContext);
                    if (blocker != null)
                    {
                        _logger.LogWarning("学习被拒: SkillId={SkillId}, 原因={Reason}", skillId, blocker);
                        return FailedLearn(blocker);
                    }

                    var direct = await CommitLearnAsync(template, info);
                    AttachMaterialDemand(direct, template);
                    return direct;
                }

                // 模板缺行（未种子/单测环境）→ 保留旧的技能点通道，但不再写死一份 3000ms/10 气的假数值，
                // 因为那会让"表里根本没有的技能"看起来像合法技能。
                if (state.SkillPoints <= 0)
                {
                    _logger.LogWarning("技能点不足: SkillId={SkillId}, SkillPoints={SkillPoints}", skillId, state.SkillPoints);
                    return FailedLearn("技能点不足");
                }

                if (state.SkillDependencies.TryGetValue(skillId, out var prereqs))
                {
                    foreach (var prereqId in prereqs)
                    {
                        if (!state.LearnedSkills.ContainsKey(prereqId))
                        {
                            _logger.LogWarning("前置技能未学习: SkillId={SkillId}, PrereqId={PrereqId}", skillId, prereqId);
                            return FailedLearn("前置技能未学习");
                        }
                    }
                }

                state.LearnedSkills[skillId] = new SkillInfo { SkillId = skillId, Level = 1, MaxLevel = 1 };
                state.SkillPoints--;
                state.TotalSkillPointsUsed++;
                await _skillState.WriteStateAsync();

                _logger.LogWarning("技能模板缺行，按无模板通道登记: SkillId={SkillId}", skillId);
                return new LearnSkillResponse
                {
                    Success = true,
                    Message = "习得成功（无模板行，按技能点通道登记）",
                    LearnedSkill = new SkillInfo { SkillId = skillId },
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "学习技能失败: SkillId={SkillId}", skillId);
                throw;
            }
        }

        /// <summary>每重耗气系数（0.98 = 省 2%）。与伤害侧 §15.9 的 B 档同一把尺子。</summary>
        internal const double LevelCostFactor = 0.98d;

        /// <summary>
        /// 按重数折算耗气：<paramref name="baseCost"/> 必须是模板里那一式的**原始**消耗，
        /// 不能是上一次写进存储的值（否则系数一改就永久失真）。下限 1：练得再满也不免费施放。
        /// </summary>
        internal static int ComputeNeiLiCostAtLevel(int baseCost, int level)
        {
            if (baseCost <= 0) return Math.Max(0, baseCost);
            int steps = Math.Max(1, level) - 1;
            return Math.Max(1, (int)Math.Round(baseCost * Math.Pow(LevelCostFactor, steps)));
        }

        /// <summary>取这一式模板里的能量消耗式（与习得时同一个出处）；取不到返回 0，调用方按"不动"处理。</summary>
        private int ResolveBaseNeiLiCost(int skillId)
        {
            var template = _templates != null && _templates.TryGetValue(skillId, out var t) ? t : null;
            if (template == null) return 0;
            try
            {
                return (int)ParseFormula(template.EnergyCostFormula);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "解析技能 {SkillId} 的耗气式失败，本次精研不改耗气。", skillId);
                return 0;
            }
        }

        public async Task<bool> UpgradeSkillAsync(int skillId)
        {
            try
            {
                var state = _skillState.State;

                if (!state.LearnedSkills.TryGetValue(skillId, out var skill))
                {
                    _logger.LogWarning("技能未学习: SkillId={SkillId}", skillId);
                    return false;
                }

                if (skill.Level >= skill.MaxLevel)
                {
                    _logger.LogWarning("技能已达最大等级: SkillId={SkillId}, Level={Level}", skillId, skill.Level);
                    return false;
                }

                skill.Level++;
                // 精研必须给真东西（§15.4 用户拍板）。旧口径是每重把耗气乘 1.1——
                // 练到第 10 重等于每次多花近一倍内力，而 §6.2 的演化前置偏偏要求练满：
                // 那条"源式圆满"因此是在收过路费，不是成长。
                // 现在每重省 2%（满重约 -17%），与 §15.9 的 B 档同尺，且**基数一律从模板重算**：
                // 在上一次的存储值上连乘会让"改一次系数"变成对老角色的永久失真，
                // 模板取不到时宁可不动这一格，也不猜一个基数。
                int baseNeiLiCost = ResolveBaseNeiLiCost(skillId);
                if (baseNeiLiCost > 0)
                    skill.NeiLiCost = ComputeNeiLiCostAtLevel(baseNeiLiCost, skill.Level);
                skill.Range += 0.5f;

                await _skillState.WriteStateAsync();

                _logger.LogInformation("技能升级成功: SkillId={SkillId}, NewLevel={Level}", skillId, skill.Level);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "技能升级失败: SkillId={SkillId}", skillId);
                throw;
            }
        }

        public Task<List<SkillInfo>> GetSkillsAsync()
        {
            try
            {
                var skills = _skillState.State.LearnedSkills.Values.ToList();
                return Task.FromResult(skills);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取技能列表失败");
                throw;
            }
        }

        public Task<float> GetSkillCooldownAsync(int skillId)
        {
            try
            {
                var state = _skillState.State;

                if (!state.LearnedSkills.TryGetValue(skillId, out var skill))
                {
                    return Task.FromResult(0f);
                }

                if (!state.SkillCooldowns.TryGetValue(skillId, out var lastCast))
                {
                    return Task.FromResult(0f);
                }

                var remaining = CombatCalculator.GetRemainingCooldown(lastCast, skill.Cooldown);
                return Task.FromResult(remaining);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询技能冷却失败: SkillId={SkillId}", skillId);
                throw;
            }
        }

        public async Task<bool> ResetSkillCooldownAsync(int skillId)
        {
            try
            {
                var state = _skillState.State;

                if (state.SkillCooldowns.Remove(skillId))
                {
                    await _skillState.WriteStateAsync();
                    _logger.LogInformation("重置技能冷却: SkillId={SkillId}", skillId);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重置技能冷却失败: SkillId={SkillId}", skillId);
                throw;
            }
        }

        public async Task<bool> ResetAllSkillsAsync()
        {
            try
            {
                var state = _skillState.State;

                // Refund 1 point per skill level
                int refundedPoints = 0;
                foreach (var skill in state.LearnedSkills.Values)
                {
                    refundedPoints += skill.Level;
                }

                state.LearnedSkills.Clear();
                state.SkillCooldowns.Clear();
                state.SkillPoints += refundedPoints;
                state.TotalSkillPointsUsed -= refundedPoints;
                if (state.TotalSkillPointsUsed < 0)
                {
                    _logger.LogWarning("技能点统计异常: TotalSkillPointsUsed={TotalSkillPointsUsed}", state.TotalSkillPointsUsed);
                    state.TotalSkillPointsUsed = 0;
                }

                await _skillState.WriteStateAsync();
                _logger.LogInformation("重置所有技能成功: RefundedPoints={RefundedPoints}", refundedPoints);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重置所有技能失败");
                throw;
            }
        }

        public async Task<bool> SetSkillDependencyAsync(int skillId, List<int> prerequisites)
        {
            try
            {
                var prereqs = prerequisites ?? new List<int>();

                // Validate: no self-dependency
                if (prereqs.Contains(skillId))
                {
                    _logger.LogWarning("技能不能依赖自身: SkillId={SkillId}", skillId);
                    return false;
                }

                // Validate: no circular dependencies
                var tempDeps = new Dictionary<int, List<int>>(_skillState.State.SkillDependencies);
                tempDeps[skillId] = prereqs;

                if (HasCircularDependency(skillId, tempDeps))
                {
                    _logger.LogWarning("检测到循环依赖: SkillId={SkillId}, Prerequisites={Prerequisites}",
                        skillId, string.Join(",", prereqs));
                    return false;
                }

                _skillState.State.SkillDependencies[skillId] = prereqs;
                await _skillState.WriteStateAsync();
                _logger.LogInformation("设置技能依赖: SkillId={SkillId}, Prerequisites={Prerequisites}", skillId, string.Join(",", prereqs));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置技能依赖失败: SkillId={SkillId}", skillId);
                throw;
            }
        }

        /// <summary>
        /// 检测技能依赖图中是否存在从startSkill出发的循环依赖
        /// </summary>
        public static bool HasCircularDependency(int startSkill, Dictionary<int, List<int>> dependencies)
        {
            var visited = new HashSet<int>();
            return HasCircularDependencyDfs(startSkill, startSkill, dependencies, visited);
        }

        private static bool HasCircularDependencyDfs(int current, int startSkill, Dictionary<int, List<int>> dependencies, HashSet<int> visited)
        {
            if (!dependencies.TryGetValue(current, out var prereqs))
                return false;

            foreach (var prereq in prereqs)
            {
                if (prereq == startSkill)
                    return true;

                if (visited.Add(prereq))
                {
                    if (HasCircularDependencyDfs(prereq, startSkill, dependencies, visited))
                        return true;
                }
            }

            return false;
        }

        public Task<int> GetSkillPointsAsync()
        {
            try
            {
                return Task.FromResult(_skillState.State.SkillPoints);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取技能点失败");
                throw;
            }
        }

        public async Task<bool> AddSkillPointsAsync(int points)
        {
            try
            {
                if (points <= 0)
                {
                    _logger.LogWarning("添加技能点数无效: {Points}", points);
                    return false;
                }

                _skillState.State.SkillPoints += points;
                await _skillState.WriteStateAsync();
                _logger.LogInformation("添加技能点成功: Points={Points}, Total={Total}", points, _skillState.State.SkillPoints);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加技能点失败: Points={Points}", points);
                throw;
            }
        }

        public Task InterruptCastAsync()
        {
            _isCasting = false;
            _castingSkillId = 0;
            _castingCasterId = 0;
            _logger.LogInformation("施法被打断");
            return Task.CompletedTask;
        }
    }
}
