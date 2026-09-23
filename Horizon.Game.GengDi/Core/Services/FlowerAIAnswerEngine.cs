using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;

namespace Horizon.Game.GengDi.Core.Services
{
    /// <summary>AI 应答意图分类。</summary>
    public enum AIAnswerIntent
    {
        Unknown = 0,
        Greeting,
        Market,
        Planting,
        Pest,
        Order,
        Account,
    }

    /// <summary>行情卡片数据（由引擎随回答一并返回，避免二次请求）。</summary>
    public sealed class AIMarketCard
    {
        public string Header { get; init; } = "";
        public List<AIMarketRow> Rows { get; init; } = new();
        public string Summary { get; init; } = "";
        public bool IsRealtime { get; init; }
    }

    public sealed class AIMarketRow
    {
        public string Variety { get; init; } = "";
        public string Price { get; init; } = "";
        public string Change { get; init; } = "";
        public bool IsUp { get; init; }
        public string Suggestion { get; init; } = "";
    }

    public sealed class AIAnswerResult
    {
        public string Text { get; init; } = "";
        public AIAnswerIntent Intent { get; init; }
        public AIMarketCard? MarketCard { get; init; }
    }

    /// <summary>
    /// 本地意图应答引擎（2026-09-18 新增）。
    ///
    /// 背景：AI 助手原先把**所有**提问都丢给后端 flower-ai/chat，后端返回泛化文案，
    /// 表现为"答非所问"。这里改为：先在本机按意图作答 —— 行情类用**真实行情接口**、
    /// 种植/病虫害/订单/账户类用**内置知识库**；只有意图识别不出来时才回落远程 AI。
    /// 这样常见提问的答案一定与问题相关，且行情答案带真实数据。
    /// </summary>
    public sealed class FlowerAIAnswerEngine
    {
        private readonly FlowerMarketService _marketService = new();

        // ============================================================
        // 关键词表（命中数越多优先级越高；按列表顺序给出答案）
        // ============================================================

        private static readonly string[] GreetingKeywords = { "你好", "您好", "hi", "hello", "在吗", "你是谁", "介绍一下你", "你能做什么", "会什么" };

        private static readonly string[] MarketKeywords =
        {
            "行情", "价格", "均价", "报价", "花价", "涨", "跌", "走势", "趋势",
            "市场", "采购", "备货", "多少钱", "便宜", "贵", "什么价", "价",
            // 口语/经营类问法（原先会漏到远程 AI）
            "补货", "囤货", "进价", "成本", "利润", "赚", "值得", "划算", "适合进", "能不能进", "入手",
        };

        private static readonly string[] OrderKeywords =
        {
            "订单", "退款", "退货", "售后", "发货", "物流", "快递", "多久到", "发票", "取消订单",
            // 口语化：破损/少件/赔付
            "破损", "损坏", "少发", "漏发", "缺货", "赔付", "赔偿", "没收到", "什么时候发",
        };

        private static readonly string[] AccountKeywords =
        {
            "会员", "账户", "账号", "帐号", "升级", "手机", "绑定", "密码", "登录", "实名", "认证", "积分",
            "注册", "注销", "资料", "设置", "头像", "昵称",
        };

        private static readonly string[] PestKeywords =
        {
            "病虫", "病", "虫", "白粉", "蚜虫", "红蜘蛛", "叶斑", "黑斑", "枯萎", "烂根",
            "防治", "打药", "农药", "掉叶", "霉", "斑点", "虫害", "喷药",
        };

        private static readonly string[] PlantingKeywords =
        {
            "种植", "种", "栽", "养护", "施肥", "浇水", "光照", "温度", "土壤", "ph", "pH",
            "推荐", "品种", "春季", "夏季", "秋季", "冬季", "大棚", "育苗", "修剪", "收益", "亩",
            "肥", "水", "花期", "保鲜", "生根", "花苞", "疏蕾", "管理", "营养",
            // 症状类口语问法（"叶子发黄""不开花""蔫了"）
            "养", "打理", "伺候", "黄叶", "叶子", "叶片", "不开花", "不长", "蔫", "萎", "徒长", "落蕾", "掉苞", "落花", "盆",
        };

        /// <summary>
        /// 意图判定（2026-09-18 改为**计分式**）：对每类关键词累计命中分（词越长权重越高），
        /// 取最高分意图。相比原先"按顺序命中即返回"，混合提问（如"红玫瑰多少钱，另外好养吗"）
        /// 会落到更准确的一类，也不会因为某个宽泛词（如"病"）抢走整句。
        /// </summary>
        public async Task<AIAnswerResult?> TryAnswerAsync(string question)
        {
            if (string.IsNullOrWhiteSpace(question))
            {
                return null;
            }

            var q = question.Trim();

            var scores = new (AIAnswerIntent Intent, int Score, string[] Keywords)[]
            {
                (AIAnswerIntent.Market,   ScoreKeywords(q, MarketKeywords),   MarketKeywords),
                (AIAnswerIntent.Pest,     ScoreKeywords(q, PestKeywords),     PestKeywords),
                (AIAnswerIntent.Order,    ScoreKeywords(q, OrderKeywords),    OrderKeywords),
                (AIAnswerIntent.Account,  ScoreKeywords(q, AccountKeywords),  AccountKeywords),
                (AIAnswerIntent.Planting, ScoreKeywords(q, PlantingKeywords), PlantingKeywords),
                (AIAnswerIntent.Greeting, ScoreKeywords(q, GreetingKeywords), GreetingKeywords),
            };

            var best = default((AIAnswerIntent Intent, int Score));
            foreach (var item in scores)
            {
                if (item.Score > best.Score)
                {
                    best = (item.Intent, item.Score);
                }
            }

            if (best.Score <= 0)
            {
                // 意图不明 → 返回 null，交由上层走远程 AI / 智能兜底分诊
                return null;
            }

            // 问句里点名了品种时，把品种名带进回答，避免"答的是这个品种、问的是另一个"
            var species = ResolveSpeciesFromQuestion(q);
            var speciesPrefix = species != null ? $"【{species.Value.Name}】" : string.Empty;

            switch (best.Intent)
            {
                case AIAnswerIntent.Market:
                    return await BuildMarketAnswerAsync(q).ConfigureAwait(false);

                case AIAnswerIntent.Pest:
                    return new AIAnswerResult
                    {
                        Intent = AIAnswerIntent.Pest,
                        Text = speciesPrefix + BuildPestAnswer(q),
                    };

                case AIAnswerIntent.Order:
                    return new AIAnswerResult { Intent = AIAnswerIntent.Order, Text = BuildOrderAnswer(q) };

                case AIAnswerIntent.Account:
                    return new AIAnswerResult { Intent = AIAnswerIntent.Account, Text = BuildAccountAnswer(q) };

                case AIAnswerIntent.Planting:
                    return new AIAnswerResult
                    {
                        Intent = AIAnswerIntent.Planting,
                        Text = speciesPrefix + BuildPlantingAnswer(q),
                    };

                case AIAnswerIntent.Greeting:
                    return new AIAnswerResult { Intent = AIAnswerIntent.Greeting, Text = BuildGreetingAnswer() };

                default:
                    return null;
            }
        }

        /// <summary>关键词累计计分：命中一次加 词长+1 分（长词更具体，权重更高）。</summary>
        private static int ScoreKeywords(string text, string[] keywords)
        {
            var score = 0;
            foreach (var keyword in keywords)
            {
                if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    score += keyword.Length + 1;
                }
            }
            return score;
        }

        private static bool ContainsAny(string text, string[] keywords)
        {
            foreach (var keyword in keywords)
            {
                if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        // ============================================================
        // 问候
        // ============================================================

        private static string BuildGreetingAnswer() =>
            "您好，我是花卉 AI 助手 👋\n" +
            "我可以帮您：\n" +
            "· 查行情 —— 各品种均价、涨跌与备货建议\n" +
            "· 讲种植 —— 品种选择、水肥光照、土壤改良\n" +
            "· 诊病虫害 —— 白粉病、蚜虫、叶斑等防治方案\n" +
            "· 处理订单与账户问题\n" +
            "直接说出您的问题即可，例如「今天红玫瑰均价多少」。";

        // ============================================================
        // 行情（真实数据 · 精准化）
        //
        // 2026-09-18 精准化改造：原实现不管问哪个品种，一律返回"成交量 Top3"通用卡片，
        // 属于"泛化"。现在先解析出【具体品种】与【问法】，再定向取数回答：
        //   报价(Quote)   —— "红玫瑰多少钱" → 该品种均价 + 涨跌% + 近 7 天区间
        //   走势(Trend)   —— "百合最近一周走势" → 逐日均价序列 + 方向判断
        //   预测(Forecast)—— "下周价格预判" → 该品种 7 天预测价 + 区间 + 置信度
        //   排行(Ranking) —— "本周涨幅最大的品种" → 按涨跌% 排出的涨幅榜
        //   通用(General) —— 没点品种也没点问法 → 才给市场概览
        // ============================================================

        private enum MarketQueryKind
        {
            General,
            Quote,
            Trend,
            Forecast,
            Ranking,
        }

        /// <summary>提问里解析出的品种（Id 可能为 0，表示只匹配到名称）。</summary>
        private readonly record struct SpeciesMatch(int Id, string Name, int Score);

        private static readonly string[] RankingKeywords = { "涨幅最大", "涨得最多", "涨最多", "跌得最多", "跌幅最大", "最贵", "最便宜", "排行", "排名", "top", "榜" };
        private static readonly string[] ForecastKeywords = { "下周", "明天", "预测", "预计", "预判", "后市" };
        private static readonly string[] TrendKeywords = { "走势", "趋势", "最近", "近一周", "一周", "近七天", "近7天", "历史", "日线" };
        private static readonly string[] QuoteKeywords = { "多少钱", "均价", "报价", "价格", "花价", "贵", "便宜", "什么价" };

        /// <summary>口语简称 → 品种表里的正式名后缀（用于"玫瑰"这类笼统说法）。</summary>
        private static readonly Dictionary<string, string[]> SpeciesAliases = new(StringComparer.Ordinal)
        {
            { "玫瑰", new[] { "玫瑰" } },
            { "百合", new[] { "百合" } },
            { "康乃馨", new[] { "康乃馨" } },
            { "菊", new[] { "菊" } },
            { "桔梗", new[] { "桔梗", "洋桔梗" } },
            { "满天星", new[] { "满天星" } },
            { "扶郎", new[] { "扶郎", "非洲菊" } },
        };

        private static MarketQueryKind DetectMarketKind(string question)
        {
            if (ContainsAny(question, RankingKeywords)) return MarketQueryKind.Ranking;
            if (ContainsAny(question, ForecastKeywords)) return MarketQueryKind.Forecast;
            if (ContainsAny(question, TrendKeywords)) return MarketQueryKind.Trend;
            if (ContainsAny(question, QuoteKeywords)) return MarketQueryKind.Quote;
            return MarketQueryKind.General;
        }

        /// <summary>
        /// 从提问中解析品种：优先精确名（最长匹配），其次去掉括号后缀的名（如"康乃馨"匹配"康乃馨(多头)"），
        /// 最后按别名匹配（"玫瑰"→红/白/粉玫瑰，并按提问里的颜色词消歧）。
        /// </summary>
        private static SpeciesMatch? ResolveSpeciesFromQuestion(string question)
        {
            Dictionary<int, string> species;
            try
            {
                species = FlowerSpeciesLookupService.Instance.GetAllSpecies();
            }
            catch (Exception)
            {
                return null;
            }

            if (species == null || species.Count == 0)
            {
                return null;
            }

            // 提问中的颜色词，用于"玫瑰"这类笼统说法的消歧
            string? color = null;
            foreach (var c in new[] { "红", "白", "粉", "黄", "紫", "香槟" })
            {
                if (question.Contains(c, StringComparison.Ordinal))
                {
                    color = c;
                    break;
                }
            }

            SpeciesMatch? best = null;

            foreach (var kvp in species)
            {
                var fullName = kvp.Value;
                if (string.IsNullOrWhiteSpace(fullName)) continue;

                var baseName = StripQualifier(fullName);

                // ① 正式名完整命中（含括号限定，最精确）
                if (fullName.Length >= 2 && question.Contains(fullName, StringComparison.Ordinal))
                {
                    Consider(ref best, new SpeciesMatch(kvp.Key, fullName, 1000 + fullName.Length));
                    continue;
                }

                // ② 去掉括号后的基础名命中
                if (baseName.Length >= 2 && question.Contains(baseName, StringComparison.Ordinal))
                {
                    var score = 500 + baseName.Length;
                    // 提问里带颜色且与名称一致时加分（"红色单头" vs 提问"红"）
                    if (color != null && fullName.Contains(color, StringComparison.Ordinal)) score += 50;
                    Consider(ref best, new SpeciesMatch(kvp.Key, fullName, score));
                    continue;
                }

                // ③ 别名命中（最泛，得分最低）
                foreach (var alias in SpeciesAliases)
                {
                    if (!question.Contains(alias.Key, StringComparison.Ordinal)) continue;
                    if (!alias.Value.Any(v => fullName.Contains(v, StringComparison.Ordinal))) continue;

                    var score = 100 + alias.Key.Length;
                    if (color != null && fullName.Contains(color, StringComparison.Ordinal)) score += 50;
                    Consider(ref best, new SpeciesMatch(kvp.Key, fullName, score));
                }
            }

            return best;
        }

        private static void Consider(ref SpeciesMatch? best, SpeciesMatch candidate)
        {
            if (best == null || candidate.Score > best.Value.Score)
            {
                best = candidate;
            }
        }

        /// <summary>去掉名称里的括号限定，如 "康乃馨(红色单头)" → "康乃馨"。</summary>
        private static string StripQualifier(string name)
        {
            var idx = name.IndexOfAny(new[] { '(', '（' });
            return idx > 0 ? name.Substring(0, idx) : name;
        }

        private async Task<AIAnswerResult> BuildMarketAnswerAsync(string question)
        {
            var kind = DetectMarketKind(question);
            var match = ResolveSpeciesFromQuestion(question);

            var overview = await TryGetOverviewAsync().ConfigureAwait(false);
            if (overview?.Snapshots == null || overview.Snapshots.Count == 0)
            {
                return new AIAnswerResult
                {
                    Intent = AIAnswerIntent.Market,
                    Text = "暂时没能连上行情服务，稍后我再帮您查。\n" +
                           "您也可以先到「行情仪表盘」查看最新报价。",
                };
            }

            try
            {
                await FlowerSpeciesLookupService.Instance.EnsureLoadedAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 品种表不可用时仍可返回行情（品种名退化为编号）
            }

            return kind switch
            {
                MarketQueryKind.Ranking => await BuildRankingAnswerAsync(overview).ConfigureAwait(false),
                MarketQueryKind.Forecast => await BuildForecastAnswerAsync(overview, match).ConfigureAwait(false),
                MarketQueryKind.Trend => await BuildTrendAnswerAsync(overview, match).ConfigureAwait(false),
                MarketQueryKind.Quote when match != null => await BuildQuoteAnswerAsync(overview, match.Value).ConfigureAwait(false),
                _ => await BuildOverviewAnswerAsync(overview).ConfigureAwait(false),
            };
        }

        private async Task<FlowerMarketOverview?> TryGetOverviewAsync()
        {
            try
            {
                return await _marketService.GetMarketOverviewAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerAIAnswerEngine] overview: {ex.Message}");
                return null;
            }
        }

        /// <summary>取某品种近 N 天均价序列（失败返回空表）。</summary>
        private async Task<List<(DateTime Time, decimal Price)>> TryGetHistoryAsync(int speciesId, int days)
        {
            var result = new List<(DateTime, decimal)>();
            try
            {
                var history = await _marketService.GetPriceHistoryAsync(speciesId, days).ConfigureAwait(false);
                if (history == null) return result;

                foreach (var point in history.OrderBy(x => x.SnapshotTime))
                {
                    result.Add((point.SnapshotTime, point.AvgPrice));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerAIAnswerEngine] history({speciesId}): {ex.Message}");
            }
            return result;
        }

        private static decimal CalcChangePercent(List<(DateTime Time, decimal Price)> history)
        {
            if (history.Count < 2) return 0m;
            var last = history[^1].Price;
            var prev = history[^2].Price;
            return prev > 0m ? (last - prev) / prev * 100m : 0m;
        }

        private static string FormatChange(decimal pct) => $"{pct:+0.0;-0.0;0.0}%";

        /// <summary>报价：某品种当前均价 / 涨跌 / 近 7 天区间。</summary>
        private async Task<AIAnswerResult> BuildQuoteAnswerAsync(FlowerMarketOverview overview, SpeciesMatch match)
        {
            var snap = FindSnapshot(overview, match.Id) ?? PickByVolume(overview, 1).FirstOrDefault();
            if (snap == null)
            {
                return await BuildOverviewAnswerAsync(overview).ConfigureAwait(false);
            }

            var speciesId = (int)snap.SpeciesId;
            var displayName = match.Name;

            var history = await TryGetHistoryAsync(speciesId, 7).ConfigureAwait(false);
            var change = CalcChangePercent(history);
            var direction = change > 0.05m ? "较上一交易日上涨" : change < -0.05m ? "较上一交易日回落" : "较上一交易日基本持平";

            var text = new StringBuilder();
            text.Append($"{displayName} 今日均价 ¥{snap.AvgPrice:0.00}/支，{direction} {FormatChange(change)}。");
            if (history.Count >= 2)
            {
                text.Append($"\n近 7 天区间 ¥{history.Min(x => x.Price):0.00} ~ ¥{history.Max(x => x.Price):0.00}，今日成交 {snap.Volume} 支。");
            }
            text.Append($"\n\n建议：{BuildSuggestion(change)}（涨幅 ≥2% 增持、跌幅 ≥2% 观望、其余持有）。");

            return new AIAnswerResult
            {
                Intent = AIAnswerIntent.Market,
                Text = text.ToString(),
                MarketCard = await BuildCardAsync(displayName, snap, change, overview, match.Id).ConfigureAwait(false),
            };
        }

        /// <summary>走势：逐日均价序列 + 方向。</summary>
        private async Task<AIAnswerResult> BuildTrendAnswerAsync(FlowerMarketOverview overview, SpeciesMatch? match)
        {
            var snap = (match != null ? FindSnapshot(overview, match.Value.Id) : null)
                       ?? PickByVolume(overview, 1).FirstOrDefault();
            if (snap == null)
            {
                return await BuildOverviewAnswerAsync(overview).ConfigureAwait(false);
            }

            var speciesId = (int)snap.SpeciesId;
            var displayName = match?.Name ?? ResolveSpeciesName(speciesId, snap.SpeciesId);
            var history = await TryGetHistoryAsync(speciesId, 7).ConfigureAwait(false);

            if (history.Count < 2)
            {
                return await BuildQuoteAnswerAsync(overview, new SpeciesMatch(speciesId, displayName, 0)).ConfigureAwait(false);
            }

            var change = CalcChangePercent(history);
            var first = history[0].Price;
            var last = history[^1].Price;
            var trend = last > first ? "整体上行" : last < first ? "整体回落" : "区间震荡";

            var text = new StringBuilder();
            text.Append($"{displayName} 近 7 天{trend}（{FormatChange((last - first) / Math.Max(first, 0.01m) * 100m)}）：\n");
            foreach (var point in history.TakeLast(5))
            {
                text.Append($"· {point.Time:MM-dd}：¥{point.Price:0.00}\n");
            }
            text.Append($"\n最新 ¥{last:0.00}/支，{FormatChange(change)}；建议{BuildSuggestion(change)}。");

            return new AIAnswerResult
            {
                Intent = AIAnswerIntent.Market,
                Text = text.ToString(),
                MarketCard = await BuildCardAsync(displayName, snap, change, overview, speciesId).ConfigureAwait(false),
            };
        }

        /// <summary>预测：7 天预测价 + 区间 + 置信度。</summary>
        private async Task<AIAnswerResult> BuildForecastAnswerAsync(FlowerMarketOverview overview, SpeciesMatch? match)
        {
            var snap = (match != null ? FindSnapshot(overview, match.Value.Id) : null)
                       ?? PickByVolume(overview, 1).FirstOrDefault();
            if (snap == null)
            {
                return await BuildOverviewAnswerAsync(overview).ConfigureAwait(false);
            }

            var speciesId = (int)snap.SpeciesId;
            var displayName = match?.Name ?? ResolveSpeciesName(speciesId, snap.SpeciesId);
            var history = await TryGetHistoryAsync(speciesId, 7).ConfigureAwait(false);
            var change = CalcChangePercent(history);

            FlowerPriceForecast? forecast = null;
            try
            {
                forecast = await _marketService.GetPriceForecastAsync(speciesId, 7).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FlowerAIAnswerEngine] forecast({speciesId}): {ex.Message}");
            }

            var text = new StringBuilder();
            text.Append($"当前 {displayName} 均价 ¥{snap.AvgPrice:0.00}/支（{FormatChange(change)}）。\n");

            var predicted = forecast?.PredictedPrices;
            if (predicted != null && predicted.Count > 0)
            {
                var last = predicted[^1];
                var confidence = forecast!.Confidence > 0 ? forecast.Confidence : 0;
                text.Append($"未来 7 天预计 ¥{last.PredictedPrice:0.00}/支" +
                            $"（区间 ¥{last.LowerBound:0.00}~¥{last.UpperBound:0.00}）");
                if (confidence > 0) text.Append($"，模型置信度 {confidence * 100:0}%");
                text.Append("。");
            }
            else
            {
                text.Append("价格预测服务暂时不可用，建议以近 7 天走势与实际到货量判断备货节奏。");
            }

            text.Append($"\n\n建议：{BuildSuggestion(change)}。");

            return new AIAnswerResult
            {
                Intent = AIAnswerIntent.Market,
                Text = text.ToString(),
                MarketCard = await BuildCardAsync(displayName, snap, change, overview, speciesId).ConfigureAwait(false),
            };
        }

        /// <summary>排行：按涨跌% 排出涨幅榜 / 跌幅榜（取成交量前 6 个品种计算，控制请求量）。</summary>
        private async Task<AIAnswerResult> BuildRankingAnswerAsync(FlowerMarketOverview overview)
        {
            var candidates = PickByVolume(overview, 6);
            var ranked = new List<(FlowerPriceSnapshot Snap, decimal Change)>();

            foreach (var snap in candidates)
            {
                var history = await TryGetHistoryAsync((int)snap.SpeciesId, 7).ConfigureAwait(false);
                ranked.Add((snap, CalcChangePercent(history)));
            }

            if (ranked.Count == 0)
            {
                return await BuildOverviewAnswerAsync(overview).ConfigureAwait(false);
            }

            var ordered = ranked.OrderByDescending(x => x.Change).ToList();
            var topGainers = ordered.Where(x => x.Change > 0m).Take(3).ToList();
            var topLosers = ordered.Where(x => x.Change < 0m).Reverse().Take(2).ToList();

            var text = new StringBuilder();
            if (topGainers.Count > 0)
            {
                text.Append("本周涨幅榜：\n");
                foreach (var item in topGainers)
                {
                    text.Append($"· {ResolveSpeciesName((int)item.Snap.SpeciesId, item.Snap.SpeciesId)}：" +
                                $"¥{item.Snap.AvgPrice:0.00}/支　{FormatChange(item.Change)}　建议{BuildSuggestion(item.Change)}\n");
                }
            }
            else
            {
                text.Append("本周暂无上涨品种，市场整体偏弱。\n");
            }

            if (topLosers.Count > 0)
            {
                text.Append("\n跌幅较大（可等回调再补货）：\n");
                foreach (var item in topLosers)
                {
                    text.Append($"· {ResolveSpeciesName((int)item.Snap.SpeciesId, item.Snap.SpeciesId)}：" +
                                $"¥{item.Snap.AvgPrice:0.00}/支　{FormatChange(item.Change)}\n");
                }
            }

            var rows = ordered.Take(3).Select(item => new AIMarketRow
            {
                Variety = ResolveSpeciesName((int)item.Snap.SpeciesId, item.Snap.SpeciesId),
                Price = $"¥{item.Snap.AvgPrice:0.00}",
                Change = FormatChange(item.Change),
                IsUp = item.Change >= 0m,
                Suggestion = BuildSuggestion(item.Change),
            }).ToList();

            return new AIAnswerResult
            {
                Intent = AIAnswerIntent.Market,
                Text = text.ToString().TrimEnd(),
                MarketCard = new AIMarketCard
                {
                    Header = "本周涨跌榜 · 成交量前 6 品种",
                    Rows = rows,
                    Summary = ordered.Count > 0
                        ? $"统计范围：成交量前 {ordered.Count} 个品种；涨跌基于近 7 天日均价对比。"
                        : string.Empty,
                    IsRealtime = true,
                },
            };
        }

        /// <summary>通用概览（只点了"行情"没点品种/问法时）。</summary>
        private async Task<AIAnswerResult> BuildOverviewAnswerAsync(FlowerMarketOverview overview)
        {
            var top = PickByVolume(overview, 3);
            var rows = new List<AIMarketRow>();
            foreach (var snap in top)
            {
                var history = await TryGetHistoryAsync((int)snap.SpeciesId, 7).ConfigureAwait(false);
                var change = CalcChangePercent(history);
                rows.Add(new AIMarketRow
                {
                    Variety = ResolveSpeciesName((int)snap.SpeciesId, snap.SpeciesId),
                    Price = $"¥{snap.AvgPrice:0.00}",
                    Change = FormatChange(change),
                    IsUp = change >= 0m,
                    Suggestion = BuildSuggestion(change),
                });
            }

            var text = new StringBuilder();
            text.Append($"今日市场均价 ¥{overview.AvgPrice:0.00}/支（{FormatChange(overview.PriceChange)}），" +
                        $"活跃品种 {overview.ActiveSpeciesCount} 个，预警 {overview.AlertCount} 条。\n\n");
            text.Append("成交量前 3 个品种：\n");
            foreach (var row in rows)
            {
                text.Append($"· {row.Variety}：{row.Price}/支　{row.Change}　建议{row.Suggestion}\n");
            }
            text.Append("\n想更精准的话，可以点名品种或问法，例如「红玫瑰今天多少钱」「百合最近一周走势」「下周价格预判」。");

            return new AIAnswerResult
            {
                Intent = AIAnswerIntent.Market,
                Text = text.ToString().TrimEnd(),
                MarketCard = new AIMarketCard
                {
                    Header = "市场概览 · 成交量 Top3",
                    Rows = rows,
                    Summary = await BuildForecastSummaryAsync(top).ConfigureAwait(false),
                    IsRealtime = true,
                },
            };
        }

        /// <summary>点位卡片：被问品种置顶 + 最多 2 个对比品种。</summary>
        private async Task<AIMarketCard> BuildCardAsync(
            string displayName, FlowerPriceSnapshot head, decimal headChange,
            FlowerMarketOverview overview, int headSpeciesId)
        {
            var rows = new List<AIMarketRow>
            {
                new()
                {
                    Variety = displayName,
                    Price = $"¥{head.AvgPrice:0.00}",
                    Change = FormatChange(headChange),
                    IsUp = headChange >= 0m,
                    Suggestion = BuildSuggestion(headChange),
                },
            };

            foreach (var snap in PickByVolume(overview, 3))
            {
                if ((int)snap.SpeciesId == headSpeciesId) continue;

                var history = await TryGetHistoryAsync((int)snap.SpeciesId, 7).ConfigureAwait(false);
                var change = CalcChangePercent(history);
                rows.Add(new AIMarketRow
                {
                    Variety = ResolveSpeciesName((int)snap.SpeciesId, snap.SpeciesId),
                    Price = $"¥{snap.AvgPrice:0.00}",
                    Change = FormatChange(change),
                    IsUp = change >= 0m,
                    Suggestion = BuildSuggestion(change),
                });

                if (rows.Count >= 3) break;
            }

            return new AIMarketCard
            {
                Header = $"{displayName} · 行情明细",
                Rows = rows,
                Summary = await BuildForecastSummaryAsync(PickByVolume(overview, 1)).ConfigureAwait(false),
                IsRealtime = true,
            };
        }

        private static FlowerPriceSnapshot? FindSnapshot(FlowerMarketOverview overview, int speciesId) =>
            overview.Snapshots.FirstOrDefault(x => (int)x.SpeciesId == speciesId);

        private static List<FlowerPriceSnapshot> PickByVolume(FlowerMarketOverview overview, int count) =>
            overview.Snapshots.OrderByDescending(x => x.Volume).Take(count).ToList();

        /// <summary>品种名解析（id → 名称；查不到退化为品种#id）。</summary>
        private static string ResolveSpeciesName(int speciesId, long fallbackId)
        {
            try
            {
                var name = FlowerSpeciesLookupService.Instance.GetSpeciesName(speciesId);
                if (!string.IsNullOrWhiteSpace(name) &&
                    !name.StartsWith("品种", StringComparison.Ordinal) &&
                    !name.StartsWith("未知", StringComparison.Ordinal))
                {
                    return name;
                }
            }
            catch (Exception)
            {
                // 品种表未就绪：走兜底名称
            }

            return $"品种#{fallbackId}";
        }

        /// <summary>按涨跌幅给出操作建议（阈值与行业惯例一致）。</summary>
        private static string BuildSuggestion(decimal changePercent) => changePercent switch
        {
            >= 2m => "增持",
            <= -2m => "观望",
            _ => "持有",
        };

        /// <summary>用价格预测生成总结句；预测服务不可用时退回描述性总结。</summary>
        private async Task<string> BuildForecastSummaryAsync(List<FlowerPriceSnapshot> top)
        {
            if (top.Count == 0) return string.Empty;

            var head = top[0];
            var headName = ResolveSpeciesName((int)head.SpeciesId, head.SpeciesId);

            try
            {
                var forecast = await _marketService
                    .GetPriceForecastAsync((int)head.SpeciesId, 7)
                    .ConfigureAwait(false);

                var predicted = forecast?.PredictedPrices;
                if (predicted != null && predicted.Count > 0)
                {
                    var last = predicted[^1];
                    var confidence = forecast!.Confidence > 0 ? forecast.Confidence : 0;
                    var confidenceText = confidence > 0 ? $"，模型置信度 {confidence * 100:0}%" : string.Empty;
                    return $"未来 7 天 {headName} 均价预计 ¥{last.PredictedPrice:0.00}/支" +
                           $"（区间 ¥{last.LowerBound:0.00}~¥{last.UpperBound:0.00}{confidenceText}）。";
                }
            }
            catch (Exception)
            {
                // 预测服务不可用：退回描述性总结
            }

            return $"当前 {headName} 均价 ¥{head.AvgPrice:0.00}/支，今日成交 {head.Volume} 支，可关注量价变化再决定备货节奏。";
        }

        // ============================================================
        // 种植（知识库）
        // ============================================================

        private static string BuildPlantingAnswer(string question)
        {
            switch (question)
            {
                case var _ when question.Contains("春季", StringComparison.Ordinal):
                    return "春季推荐这三种，兼顾行情与成活率：\n" +
                           "· 高原红玫瑰 —— 收益约 ¥2,580/亩，抗病性强，春季定植最佳\n" +
                           "· 多头百合 —— 周期短（约 90 天），适合轮作补空档\n" +
                           "· 康乃馨 —— 管理简单，适合新手先跑通流程\n" +
                           "定植前记得做土壤消毒，并把 pH 调到 6.0~6.5。";

                case var _ when question.Contains("黄叶", StringComparison.Ordinal) ||
                                question.Contains("叶子发黄", StringComparison.Ordinal) ||
                                question.Contains("叶片发黄", StringComparison.Ordinal) ||
                                question.Contains("变黄", StringComparison.Ordinal):
                    return "叶片发黄先分清部位与表现，再对症处理：\n"
                           + "· 老叶先黄、整片均匀 → 多为**缺氮或积水闷根**：控土松土，追一次平衡肥\n"
                           + "· 新叶发黄、叶脉仍绿 → 多为**缺铁/土壤偏碱**：浇 0.2% 硫酸亚铁，并把 pH 调到 6.0~6.5\n"
                           + "· 叶脉间黄化 → 多为**缺镁**：补硫酸镁（每 100㎡ 1~2kg）\n"
                           + "· 黄斑带霉层/虫体 → 属病害或虫害，可直接问我「白粉病怎么治」「红蜘蛛怎么处理」\n"
                           + "判断口诀：先看新叶还是老叶，再看有没有霉层或虫。";

                case var _ when question.Contains("不开花", StringComparison.Ordinal) ||
                                question.Contains("不结蕾", StringComparison.Ordinal) ||
                                question.Contains("没有花", StringComparison.Ordinal):
                    return "不开花常见四个原因，按顺序自查：\n"
                           + "1. **光照不足**：玫瑰需每天 ≥6 小时直射光，光照不够只长叶不结蕾\n"
                           + "2. **氮肥过多**：枝叶徒长、花芽分化受抑 → 转施磷钾（如 10-30-20），7~10 天一次\n"
                           + "3. **温度不适**：百合 >28℃ 易消蕾，夏季需遮阳降温\n"
                           + "4. **未修剪**：残花与弱枝不疏、养分分散 → 及时疏蕾、剪除盲枝";

                case var _ when question.Contains("掉苞", StringComparison.Ordinal) ||
                                question.Contains("落蕾", StringComparison.Ordinal) ||
                                question.Contains("掉花", StringComparison.Ordinal) ||
                                question.Contains("落花", StringComparison.Ordinal):
                    return "掉花苞/落蕾通常是应激反应，优先排查这三点：\n"
                           + "1. **温度骤变**（空调直吹、昼夜温差大）—— 保持 15~20℃ 稳定\n"
                           + "2. **水分忽干忽湿** —— 改滴灌，保持基质稳定湿润但不积水\n"
                           + "3. **乙烯或机械损伤** —— 远离成熟水果，搬运轻拿轻放\n"
                           + "另外花期忌大量施氮肥，会加速落蕾。";

                case var _ when question.Contains("蔫", StringComparison.Ordinal) ||
                                question.Contains("萎", StringComparison.Ordinal) ||
                                question.Contains("耷拉", StringComparison.Ordinal):
                    return "发蔫分两种，处理方式相反：\n"
                           + "· **缺水型**：盆土干硬、叶片薄软 → 缓慢浇透（分 2~3 次），1~2 小时可恢复\n"
                           + "· **烂根型**：盆土潮湿、叶片发软发黑 → 停水松土透气，严重时脱盆剪腐根并用恶霉灵灌根\n"
                           + "判断方法：手指插土 3cm，干即缺水，湿还蔫就是烂根。";

                case var _ when question.Contains("徒长", StringComparison.Ordinal) ||
                                question.Contains("只长叶", StringComparison.Ordinal) ||
                                question.Contains("长太高", StringComparison.Ordinal):
                    return "徒长（茎细长、节间拉长、叶色淡）的原因与对策：\n"
                           + "· 光照不足 → 增加直射光，必要时补灯\n"
                           + "· 氮肥偏多 → 停氮、改磷钾\n"
                           + "· 密度过大 → 疏苗、加大株距改善通风\n"
                           + "· 水分偏多 → 见干见湿，配合摘心控制高度";

                case var _ when question.Contains("ph", StringComparison.OrdinalIgnoreCase) ||
                                question.Contains("土壤", StringComparison.Ordinal) ||
                                question.Contains("酸化", StringComparison.Ordinal):
                    return "土壤 pH 偏高（偏碱）会锁住铁、锰等微量元素，表现为新叶发黄。处理顺序：\n" +
                           "1. 测土：先确认实际 pH 与有机质含量\n" +
                           "2. 调酸：每 100㎡ 施硫磺粉 1~1.5kg，或腐熟有机肥 200kg，分 2 次施\n" +
                           "3. 中和：浇施 0.2% 硫酸亚铁或稀释食醋水（1:500）\n" +
                           "4. 复测：3~4 周后复测，目标 pH 6.0~6.5\n" +
                           "⚠️ 不要一次猛调，pH 骤变会伤根。";

                case var _ when question.Contains("施肥", StringComparison.Ordinal) ||
                                question.Contains("肥", StringComparison.Ordinal):
                    return "施肥遵循「薄肥勤施」：\n" +
                           "· 苗期：氮为主，促枝叶，10~15 天一次\n" +
                           "· 花芽分化期：转磷钾（如 10-30-20），7~10 天一次\n" +
                           "· 花期：停氮，补钾与钙，延长瓶插期\n" +
                           "施肥后浇一次清水，避免盐分在根系附近累积。";

                case var _ when question.Contains("浇", StringComparison.Ordinal) ||
                                question.Contains("湿度", StringComparison.Ordinal) ||
                                question.Contains("水", StringComparison.Ordinal):
                    return "浇水要点：见干见湿，忌积水。\n" +
                           "· 判断标准：手指插入 3cm，干爽即浇\n" +
                           "· 频率参考：春秋 2~3 天一次；夏季早晚各一次；冬季 5~7 天一次\n" +
                           "· 湿度：玫瑰/百合适宜空气湿度 60%~70%，过高易发白粉病\n" +
                           "建议用滴灌，既省水又能降低叶面湿度。";

                case var _ when question.Contains("光", StringComparison.Ordinal) ||
                                question.Contains("温度", StringComparison.Ordinal):
                    return "光照与温度：\n" +
                           "· 玫瑰：每天 ≥6 小时直射光，适宜 15~25℃\n" +
                           "· 百合：喜凉爽，适宜 15~20℃，>28℃ 易消蕾\n" +
                           "· 康乃馨：耐寒不耐热，适宜 14~21℃\n" +
                           "夏季高温建议加盖遮阳网（50%~70% 遮光），并加强通风。";

                case var _ when question.Contains("保鲜", StringComparison.Ordinal) ||
                                question.Contains("花期", StringComparison.Ordinal) ||
                                question.Contains("瓶插", StringComparison.Ordinal) ||
                                question.Contains("养得久", StringComparison.Ordinal):
                    return "切花保鲜（延长花期）要点：\n" +
                           "1. 剪口：45° 斜剪 2~3cm 增大吸水面积，每次换水都重剪一次\n" +
                           "2. 水质：每升水加 1 片阿司匹林或专用保鲜剂，抑制细菌繁殖\n" +
                           "3. 去叶：水面以下的叶片全部摘除，避免腐烂污染水质\n" +
                           "4. 温度：12~15℃ 最耐放，避开阳光直射与空调直吹\n" +
                           "5. 隔离：远离苹果、香蕉等释放乙烯的水果\n" +
                           "按这套做，玫瑰一般可延长 5~7 天观赏期。";

                default:
                    return "关于种植，我可以给您这些方向的建议：\n" +
                           "· 品种选择与收益测算（按行情给推荐）\n" +
                           "· 土壤改良与 pH 调整\n" +
                           "· 水肥管理与光照温度控制\n" +
                           "您可以直接问，例如「土壤 pH 偏高怎么办」「春季种什么好」。";
            }
        }

        // ============================================================
        // 病虫害（知识库）
        // ============================================================

        private static string BuildPestAnswer(string question)
        {
            switch (question)
            {
                case var _ when question.Contains("白粉", StringComparison.Ordinal):
                    return "白粉病（叶片出现白色粉状霉层）防治：\n" +
                           "1. 先隔离：摘除病叶并带出园区销毁，勿堆肥\n" +
                           "2. 通风降湿：湿度降到 60% 以下，避免傍晚浇水\n" +
                           "3. 药剂：戊唑醇或嘧菌酯，7~10 天一次，连喷 2~3 次，交替用药防抗性\n" +
                           "4. 预防：增施钾肥提高抗性，保持株距通风\n" +
                           "⚠️ 采收前 7 天停药。";

                case var _ when question.Contains("蚜虫", StringComparison.Ordinal):
                    return "蚜虫（嫩梢聚集、叶片卷曲、有蜜露）防治：\n" +
                           "· 物理：黄板诱杀 + 银灰膜驱避\n" +
                           "· 生物：释放瓢虫/草蛉，或喷苦参碱\n" +
                           "· 化学：吡虫啉或啶虫脒，重点喷叶背与嫩梢\n" +
                           "蚜虫会传播病毒，发现即处理，别等扩散。";

                case var _ when question.Contains("红蜘蛛", StringComparison.Ordinal) ||
                                question.Contains("螨", StringComparison.Ordinal):
                    return "红蜘蛛（叶背细密黄白点、有蛛丝）防治：\n" +
                           "· 高温干旱是诱因，先提高空气湿度\n" +
                           "· 药剂：阿维菌素或螺螨酯，重点喷**叶背**，5~7 天一次，连喷 2 次\n" +
                           "· 轮换用药，避免连续同一种（抗性极快）";

                case var _ when question.Contains("叶斑", StringComparison.Ordinal) ||
                                question.Contains("黑斑", StringComparison.Ordinal):
                    return "叶斑/黑斑病防治：\n" +
                           "1. 清除病叶，避免淋浇（改滴灌）\n" +
                           "2. 药剂：代森锰锌或苯醚甲环唑，7~10 天一次\n" +
                           "3. 改善通风，降低种植密度\n" +
                           "同时注意镁元素补充，缺镁也会出现叶脉间黄化（易与叶斑混淆）。";

                case var _ when question.Contains("枯萎", StringComparison.Ordinal) ||
                                question.Contains("烂根", StringComparison.Ordinal):
                    return "枯萎/烂根多为根腐或积水引起：\n" +
                           "1. 立即控水、松土透气，检查排水沟\n" +
                           "2. 挖检根系：发黑发软即根腐 → 用恶霉灵灌根\n" +
                           "3. 严重株直接拔除，病穴撒石灰消毒\n" +
                           "4. 后续改用高垄栽培 + 滴灌，避免大水漫灌";

                default:
                    return "常见的病虫害我可以直接给方案：白粉病、蚜虫、红蜘蛛、叶斑病、根腐/枯萎。\n" +
                           "描述一下症状（叶片/茎/根部、颜色、是否有霉层或虫体），我给您对应的处理步骤。";
            }
        }

        // ============================================================
        // 订单（流程知识库）
        // ============================================================

        private static string BuildOrderAnswer(string question)
        {
            switch (question)
            {
                case var _ when question.Contains("退款", StringComparison.Ordinal) ||
                                question.Contains("退货", StringComparison.Ordinal):
                    return "退款/退货流程：\n" +
                           "1. 进入「订单中心」→ 找到该订单 → 点「申请退款」\n" +
                           "2. 选择原因并上传凭证（如花材损伤照片）\n" +
                           "3. 商家 24 小时内审核，通过后原路退回\n" +
                           "· 未发货：一般即时退款\n" +
                           "· 已发货：需先拒收或寄回，运费责任按原因判定\n" +
                           "· 生鲜类需在签收后 **24 小时内** 发起，超时较难受理，请尽快处理。";

                case var _ when question.Contains("发货", StringComparison.Ordinal) ||
                                question.Contains("多久", StringComparison.Ordinal) ||
                                question.Contains("时效", StringComparison.Ordinal):
                    return "发货时效（鲜花类）：\n" +
                           "· 现货：当天 18:00 前下单当日发出，之后次日发出\n" +
                           "· 预售/定制：按商品页标注的备货期（通常 2~5 天）\n" +
                           "· 运输：同城冷链次日达；跨省冷链 1~3 天\n" +
                           "鲜切花对温度敏感，建议优先选择冷链配送：可在订单备注里注明。";

                case var _ when question.Contains("物流", StringComparison.Ordinal) ||
                                question.Contains("快递", StringComparison.Ordinal) ||
                                question.Contains("到哪", StringComparison.Ordinal):
                    return "查物流：进入「订单中心」→ 点开对应订单 → 右上角「查看物流」，可以看到冷链节点与预计到达时间。\n" +
                           "如果 48 小时没有揽收记录，把订单号发我或联系商家客服，通常需要重新发货。";

                case var _ when question.Contains("取消", StringComparison.Ordinal):
                    return "取消订单：未发货前都可以在「订单中心」自助取消，款项原路退回。\n" +
                           "若已点击发货，就只能走「拒收 + 申请退款」流程。";

                case var _ when question.Contains("发票", StringComparison.Ordinal):
                    return "开票：在「订单中心」的已完成订单里点「申请发票」，填写抬头与税号，电子发票一般 1~3 个工作日发送到您的邮箱。";

                default:
                    return "订单相关的问题我可以帮您处理：退款/退货流程、发货时效、物流查询、取消订单、开发票。\n" +
                           "您也可以直接到「订单中心」查看实时状态。";
            }
        }

        // ============================================================
        // 账户（流程知识库）
        // ============================================================

        private static string BuildAccountAnswer(string question)
        {
            switch (question)
            {
                case var _ when question.Contains("会员", StringComparison.Ordinal) ||
                                question.Contains("升级", StringComparison.Ordinal):
                    return "会员升级：进入「我的」→「会员中心」，可选择月/季/年卡。\n" +
                           "等级越高，平台佣金越低、行情高级分析与优先冷链调度等权益越多。\n" +
                           "升级后立即生效，有效期从升级当日起算。";

                case var _ when question.Contains("手机", StringComparison.Ordinal) ||
                                question.Contains("绑定", StringComparison.Ordinal):
                    return "修改绑定手机：\n" +
                           "1. 「我的」→「账号安全」→「修改手机号」\n" +
                           "2. 先用**旧手机号**接收验证码验证身份\n" +
                           "3. 输入新手机号并完成验证\n" +
                           "若旧手机已停用，请通过「账号申诉」提交实名信息人工处理。";

                default:
                    return "账户相关我可以帮您：会员升级、修改绑定手机、登录与密码问题、实名认证、积分规则。\n" +
                           "涉及资金或实名信息时，系统会要求二次验证，属正常安全流程。";
            }
        }

        // ============================================================
        // 智能兜底（2026-09-18）
        //
        // 背景：用户问随意/自定义问题时，原实现直接回"抱歉，这个问题我还没完全理解"，观感很"傻"。
        // 这里改为**分诊式兜底**，保证任何输入都有体面且可执行的回应：
        //   ① 问句里能认出品种 → 给出该品种可问的四类问题（价格/走势/预测/养护）；
        //   ② 认不出品种 → 列出能确定回答的四类能力，并按问句线索给出"照抄即可"的改写建议。
        // ============================================================

        /// <summary>线索词 → 建议问法（让用户几乎零成本得到有效回答）。</summary>
        private static readonly (string[] Cues, string[] Suggestions)[] FallbackHints =
        {
            (new[] { "价", "贵", "便宜", "成本", "利润", "赚", "进价", "划算", "值得" },
                new[] { "今天红玫瑰多少钱？", "本周涨幅最大的品种", "下周价格预判" }),
            (new[] { "养", "浇", "肥", "土", "盆", "叶", "黄", "花苞", "开花", "蔫", "徒长", "保鲜", "花期" },
                new[] { "水肥怎么管理？", "叶子发黄怎么办？", "开花少/不开花怎么办？", "花期怎么保鲜？" }),
            (new[] { "虫", "病", "霉", "烂", "枯", "斑" },
                new[] { "白粉病怎么防治？", "红蜘蛛怎么处理？", "叶斑病怎么治？" }),
            (new[] { "单", "买", "付", "退", "发", "物流", "快递", "破损", "赔", "发票" },
                new[] { "如何申请退款？", "发货周期是多久？", "怎么查物流？", "花材破损怎么赔付？" }),
            (new[] { "会员", "账号", "帐号", "手机", "密码", "登录", "实名", "积分", "设置" },
                new[] { "如何升级会员？", "修改绑定手机", "忘记密码怎么办？" }),
        };

        /// <summary>
        /// 构造兜底答复：永远给出可执行的下一步，而不是"我没听懂"。
        /// 返回 null 表示连分诊也做不了（空输入）。
        /// </summary>
        public AIAnswerResult? BuildFallbackAnswer(string question)
        {
            if (string.IsNullOrWhiteSpace(question))
            {
                return null;
            }

            var q = question.Trim();
            var species = ResolveSpeciesFromQuestion(q);

            // ① 认出了品种 → 告诉他就这个品种可以怎么问
            if (species != null)
            {
                var name = species.Value.Name;
                return new AIAnswerResult
                {
                    Intent = AIAnswerIntent.Unknown,
                    Text = $"关于「{name}」，我可以直接给您这些：\n"
                           + $"· 价格：问我「{name}今天多少钱」\n"
                           + $"· 走势：问我「{name}最近一周走势」\n"
                           + $"· 预测：问我「{name}下周价格预判」\n"
                           + $"· 养护：问我「{name}怎么养」\n"
                           + "\n您也可以把想问的方向补一句，我就直接给数据或方案。",
                };
            }

            // ② 没认出品种 → 能力分诊 + 按问句线索给改写建议
            var suggestions = PickFallbackSuggestions(q);

            var text = new StringBuilder();
            text.Append("这个问题我暂时没有把握给准确的答案，不过下面这些我能确定帮到您：\n");
            text.Append("· **花价行情**：点名品种问价、看走势、要预测（如「百合今天多少钱」）\n");
            text.Append("· **种植养护**：土壤 pH、水肥、光照温度、黄叶、不开花、花期保鲜\n");
            text.Append("· **病虫害防治**：白粉病、蚜虫、红蜘蛛、叶斑、根腐\n");
            text.Append("· **订单与账户**：退款、发货时效、物流、破损赔付、会员与绑定手机\n");

            if (suggestions.Count > 0)
            {
                text.Append("\n照抄下面任意一句就能得到答案：\n");
                foreach (var suggestion in suggestions)
                {
                    text.Append($"· {suggestion}\n");
                }
            }

            return new AIAnswerResult
            {
                Intent = AIAnswerIntent.Unknown,
                Text = text.ToString().TrimEnd(),
            };
        }

        /// <summary>按问句里的线索词挑选建议问法（最多 4 条；无线索时每类给一条）。</summary>
        private static List<string> PickFallbackSuggestions(string question)
        {
            var result = new List<string>();

            foreach (var (cues, suggestions) in FallbackHints)
            {
                if (cues.Any(cue => question.Contains(cue, StringComparison.OrdinalIgnoreCase)))
                {
                    foreach (var suggestion in suggestions)
                    {
                        if (!result.Contains(suggestion)) result.Add(suggestion);
                    }
                }

                if (result.Count >= 4) break;
            }

            if (result.Count == 0)
            {
                result.AddRange(new[]
                {
                    "今天红玫瑰多少钱？",
                    "水肥怎么管理？",
                    "白粉病怎么防治？",
                    "如何申请退款？",
                });
            }

            return result.Take(4).ToList();
        }
    }
}
