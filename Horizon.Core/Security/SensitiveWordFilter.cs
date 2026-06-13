using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Horizon.Core.Security
{
    /// <summary>
    /// 敏感词分类
    /// </summary>
    public enum SensitiveWordCategory
    {
        /// <summary>通用/系统滥用类（作弊、诈骗、垃圾信息等）</summary>
        General = 0,

        /// <summary>宗教类（宗教极端主义、邪教等）</summary>
        Religion = 1,

        /// <summary>政治类（危害国家统一、煽动颠覆等）</summary>
        Politics = 2,

        /// <summary>种族歧视类（民族歧视、地域歧视等）</summary>
        RacialDiscrimination = 3,

        /// <summary>色情类（淫秽、低俗内容等）</summary>
        Pornography = 4,

        /// <summary>暴力类（煽动暴力、伤害性内容等）</summary>
        Violence = 5,

        /// <summary>犯罪类（毒品、武器、洗钱等非法活动）</summary>
        Crime = 6
    }

    /// <summary>
    /// 敏感词过滤结果
    /// </summary>
    public sealed class SensitiveWordCheckResult
    {
        /// <summary>是否命中敏感词</summary>
        public bool IsViolation { get; init; }

        /// <summary>命中的敏感词列表</summary>
        public IReadOnlyList<string> MatchedWords { get; init; } = Array.Empty<string>();

        /// <summary>命中的敏感词分类列表</summary>
        public IReadOnlyList<SensitiveWordCategory> MatchedCategories { get; init; } = Array.Empty<SensitiveWordCategory>();

        public static SensitiveWordCheckResult Clean { get; } = new() { IsViolation = false };
    }

    /// <summary>
    /// 敏感词过滤器
    /// <para>
    /// 词库按分类管理，支持宗教、政治、种族歧视、色情、暴力、犯罪等多个维度的过滤。
    /// 生产环境建议通过配置文件或数据库动态加载完整词库，此处内置词条仅作为默认兜底。
    /// </para>
    /// </summary>
    public class SensitiveWordFilter
    {
        // ──────────────────────────────────────────────────────────────────────────
        // 默认分类词库（每类保留少量代表性词条，生产环境应通过外部配置扩充）
        // ──────────────────────────────────────────────────────────────────────────

        private static readonly IReadOnlyDictionary<SensitiveWordCategory, string[]> DefaultWordsByCategory =
            new Dictionary<SensitiveWordCategory, string[]>
            {
                [SensitiveWordCategory.General] = new[]
                {
                    // 系统/账号类滥用
                    "admin", "administrator", "root", "system", "guest",
                    // 游戏作弊/黑产
                    "spam", "abuse", "cheat", "hack", "exploit", "scam",
                    "phishing", "botting", "外挂", "私服", "代练", "刷金币",
                    // 侮辱性词语（通用）
                    "fuck", "shit", "asshole", "moron", "傻逼", "妈的", "操你妈",
                    "stupid", "idiot"
                },

                [SensitiveWordCategory.Religion] = new[]
                {
                    // 邪教组织名称（依法禁止）
                    "邪教", "法轮功", "东方闪电", "全能神", "主神教", "门徒会",
                    // 宗教极端主义
                    "圣战", "杰哈德", "异教徒", "jihad", "infidel",
                    // 煽动性宗教仇恨
                    "灭绝宗教", "宗教迫害"
                },

                [SensitiveWordCategory.Politics] = new[]
                {
                    // 危害国家统一/领土完整
                    "台独", "港独", "藏独", "疆独", "分裂国家", "独立运动",
                    // 煽动颠覆国家政权
                    "推翻政府", "颠覆国家", "武装政变",
                    // 境外敌对组织（示例）
                    "境外势力渗透"
                },

                [SensitiveWordCategory.RacialDiscrimination] = new[]
                {
                    // 民族歧视（通用示例词）
                    "种族歧视", "种族灭绝", "racism", "genocide",
                    // 煽动民族仇恨
                    "民族灭绝", "排华", "排外", "仇视少数民族",
                    // 英文种族侮辱词（仅列通用代表词，实际词库由运营维护）
                    "slur", "racial slur"
                },

                [SensitiveWordCategory.Pornography] = new[]
                {
                    // 淫秽低俗内容
                    "色情", "淫秽", "裸聊", "援交", "性交易", "卖淫",
                    "porn", "pornography", "obscene", "prostitution",
                    // 软色情诱导
                    "约炮", "一夜情", "找小姐"
                },

                [SensitiveWordCategory.Violence] = new[]
                {
                    // 煽动现实暴力
                    "杀人教程", "爆炸教程", "制造炸弹", "行刑", "虐杀",
                    "斩首", "beheading", "torture",
                    // 自我伤害诱导
                    "自杀教程", "割腕方法",
                    // 校园/公共暴力
                    "无差别屠杀", "大规模枪击"
                },

                [SensitiveWordCategory.Crime] = new[]
                {
                    // 毒品
                    "贩毒", "吸毒", "制毒", "冰毒", "海洛因", "大麻交易",
                    "drug deal", "narcotics",
                    // 非法武器
                    "非法枪支", "走私枪", "枪支买卖",
                    // 洗钱/欺诈
                    "洗钱", "诈骗", "赌博", "开赌场", "地下钱庄",
                    // 人口贩卖
                    "人口贩卖", "卖人", "trafficking"
                }
            };

        // ──────────────────────────────────────────────────────────────────────────
        // 内部存储
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>按分类存储的词库</summary>
        private readonly Dictionary<SensitiveWordCategory, HashSet<string>> _wordsByCategory;

        /// <summary>全量词集合（所有分类的并集），用于快速检测</summary>
        private HashSet<string> _allWords;

        // ──────────────────────────────────────────────────────────────────────────
        // 构造函数
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 创建包含所有默认分类词库的过滤器
        /// </summary>
        public SensitiveWordFilter()
        {
            _wordsByCategory = new Dictionary<SensitiveWordCategory, HashSet<string>>();
            foreach (var (category, words) in DefaultWordsByCategory)
            {
                _wordsByCategory[category] = BuildWordSet(words);
            }

            _allWords = BuildUnionSet(_wordsByCategory);
        }

        /// <summary>
        /// 创建仅包含自定义词条的过滤器（归入 <see cref="SensitiveWordCategory.General"/> 分类）。
        /// 此构造函数保持向后兼容，不加载默认词库。
        /// </summary>
        public SensitiveWordFilter(IEnumerable<string> words)
        {
            _wordsByCategory = new Dictionary<SensitiveWordCategory, HashSet<string>>
            {
                [SensitiveWordCategory.General] = BuildWordSet(words)
            };
            _allWords = BuildUnionSet(_wordsByCategory);
        }

        /// <summary>
        /// 使用空词库创建过滤器（不加载默认词条，供外部完全自定义时使用）
        /// </summary>
        private SensitiveWordFilter(bool _)
        {
            _wordsByCategory = new Dictionary<SensitiveWordCategory, HashSet<string>>();
            _allWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 工厂方法（推荐生产环境使用）
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 从外部词库字典创建过滤器（生产推荐方式）。
        /// 词库应从加密配置、数据库或密钥管理服务中读取，避免将敏感词条硬编码在应用代码内。
        /// </summary>
        /// <param name="externalWords">
        /// 外部提供的分类词库。若 <c>null</c> 或为空，则回退到内置默认词库。
        /// </param>
        /// <returns>已初始化的 <see cref="SensitiveWordFilter"/> 实例</returns>
        public static SensitiveWordFilter CreateFromConfiguration(
            IReadOnlyDictionary<SensitiveWordCategory, IEnumerable<string>>? externalWords)
        {
            if (externalWords == null || externalWords.Count == 0)
            {
                // 未提供外部配置时退化为默认实例
                return new SensitiveWordFilter();
            }

            var filter = new SensitiveWordFilter(false);
            foreach (var (category, words) in externalWords)
            {
                filter._wordsByCategory[category] = BuildWordSet(words);
            }

            filter._allWords = BuildUnionSet(filter._wordsByCategory);
            return filter;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 向后兼容 API（不指定分类，对所有词有效）
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>检测文本是否包含任意分类的敏感词</summary>
        public bool ContainsSensitiveWord(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return _allWords.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>过滤文本中所有分类的敏感词，以 <paramref name="replacement"/> 替换</summary>
        public string? FilterText(string? text, char replacement = '*')
        {
            return FilterTextByWords(text, _allWords, replacement);
        }

        /// <summary>获取文本中匹配的所有敏感词（不区分分类）</summary>
        public List<string> GetMatchedWords(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<string>();
            }

            return _allWords
                .Where(word => text.Contains(word, StringComparison.OrdinalIgnoreCase))
                .OrderBy(static word => word, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>向 <see cref="SensitiveWordCategory.General"/> 分类添加词条</summary>
        public void AddWord(string? word)
        {
            AddWord(word, SensitiveWordCategory.General);
        }

        /// <summary>从所有分类中移除指定词条</summary>
        public void RemoveWord(string? word)
        {
            if (string.IsNullOrWhiteSpace(word))
            {
                return;
            }

            var trimmed = word.Trim();
            foreach (var wordSet in _wordsByCategory.Values)
            {
                wordSet.Remove(trimmed);
            }

            RebuildAllWords();
        }

        /// <summary>所有分类词条总数</summary>
        public int WordCount => _allWords.Count;

        /// <summary>
        /// 批量加载指定分类的词条（追加，不覆盖现有词条）。
        /// 生产环境应在应用启动时调用此方法，从安全的外部配置源注入完整词库。
        /// </summary>
        public void LoadWords(SensitiveWordCategory category, IEnumerable<string> words)
        {
            if (!_wordsByCategory.TryGetValue(category, out var wordSet))
            {
                wordSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _wordsByCategory[category] = wordSet;
            }

            foreach (var word in words.Where(static w => !string.IsNullOrWhiteSpace(w)))
            {
                wordSet.Add(word.Trim());
            }

            RebuildAllWords();
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 分类感知 API
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>检测文本是否包含指定分类的敏感词</summary>
        public bool ContainsSensitiveWord(string? text, SensitiveWordCategory category)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (!_wordsByCategory.TryGetValue(category, out var words))
            {
                return false;
            }

            return words.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>检测文本是否包含指定多个分类之一的敏感词</summary>
        public bool ContainsSensitiveWord(string? text, IEnumerable<SensitiveWordCategory> categories)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return categories.Any(cat => ContainsSensitiveWord(text, cat));
        }

        /// <summary>
        /// 对文本进行完整检测，返回命中的词条和分类
        /// </summary>
        public SensitiveWordCheckResult Check(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return SensitiveWordCheckResult.Clean;
            }

            var matchedWords = new List<string>();
            var matchedCategories = new List<SensitiveWordCategory>();

            foreach (var (category, words) in _wordsByCategory)
            {
                foreach (var word in words)
                {
                    if (text.Contains(word, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedWords.Add(word);
                        if (!matchedCategories.Contains(category))
                        {
                            matchedCategories.Add(category);
                        }
                    }
                }
            }

            if (matchedWords.Count == 0)
            {
                return SensitiveWordCheckResult.Clean;
            }

            return new SensitiveWordCheckResult
            {
                IsViolation = true,
                MatchedWords = matchedWords.Distinct().OrderBy(static w => w).ToList(),
                MatchedCategories = matchedCategories.OrderBy(static c => (int)c).ToList()
            };
        }

        /// <summary>仅过滤指定分类的敏感词</summary>
        public string? FilterText(string? text, IEnumerable<SensitiveWordCategory> categories, char replacement = '*')
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            // 合并所选分类的词集合
            var combined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var category in categories)
            {
                if (_wordsByCategory.TryGetValue(category, out var words))
                {
                    combined.UnionWith(words);
                }
            }

            return FilterTextByWords(text, combined, replacement);
        }

        /// <summary>获取文本在指定分类中匹配的词条</summary>
        public List<string> GetMatchedWords(string? text, SensitiveWordCategory category)
        {
            if (string.IsNullOrWhiteSpace(text) || !_wordsByCategory.TryGetValue(category, out var words))
            {
                return new List<string>();
            }

            return words
                .Where(word => text.Contains(word, StringComparison.OrdinalIgnoreCase))
                .OrderBy(static word => word, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>获取文本中命中的所有分类</summary>
        public List<SensitiveWordCategory> GetMatchedCategories(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<SensitiveWordCategory>();
            }

            return _wordsByCategory
                .Where(kvp => kvp.Value.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)))
                .Select(static kvp => kvp.Key)
                .OrderBy(static c => (int)c)
                .ToList();
        }

        /// <summary>向指定分类添加词条</summary>
        public void AddWord(string? word, SensitiveWordCategory category)
        {
            if (string.IsNullOrWhiteSpace(word))
            {
                return;
            }

            var trimmed = word.Trim();
            if (!_wordsByCategory.TryGetValue(category, out var words))
            {
                words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _wordsByCategory[category] = words;
            }

            words.Add(trimmed);
            _allWords.Add(trimmed);
        }

        /// <summary>获取指定分类的词条数量</summary>
        public int GetWordCount(SensitiveWordCategory category)
        {
            return _wordsByCategory.TryGetValue(category, out var words) ? words.Count : 0;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // 私有辅助方法
        // ──────────────────────────────────────────────────────────────────────────

        private static HashSet<string> BuildWordSet(IEnumerable<string> words) =>
            new HashSet<string>(
                words.Where(static w => !string.IsNullOrWhiteSpace(w)).Select(static w => w.Trim()),
                StringComparer.OrdinalIgnoreCase);

        private static HashSet<string> BuildUnionSet(Dictionary<SensitiveWordCategory, HashSet<string>> byCategory)
        {
            var union = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var wordSet in byCategory.Values)
            {
                union.UnionWith(wordSet);
            }

            return union;
        }

        private void RebuildAllWords()
        {
            _allWords = BuildUnionSet(_wordsByCategory);
        }

        private static string? FilterTextByWords(string? text, HashSet<string> words, char replacement)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            var builder = new StringBuilder(text);
            // 优先匹配更长的词，避免短词把长词的一部分提前替换掉
            foreach (var word in words.OrderByDescending(static w => w.Length))
            {
                var searchFrom = 0;
                while (searchFrom < builder.Length)
                {
                    var current = builder.ToString();
                    var index = current.IndexOf(word, searchFrom, StringComparison.OrdinalIgnoreCase);
                    if (index < 0)
                    {
                        break;
                    }

                    for (var i = 0; i < word.Length; i++)
                    {
                        builder[index + i] = replacement;
                    }

                    searchFrom = index + word.Length;
                }
            }

            return builder.ToString();
        }
    }
}
