using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Horizon.Game.GengDi.Tools.ExcelProcessor.Models;

namespace Horizon.Game.GengDi.Tools.ExcelProcessor.Services
{
    public class MergeRequirementParser
    {
        public ParsedRequirement Parse(string input)
        {
            var result = new ParsedRequirement
            {
                RawInput = input,
                Confidence = 0,
                Notes = new List<string>()
            };

            if (string.IsNullOrWhiteSpace(input))
            {
                result.Notes.Add("未输入合并需求，将使用默认配置");
                ApplyDefaultConfig(result);
                return result;
            }

            var text = input.Trim();
            int score = 0;

            // 1. 检测目标文件名
            result.TargetFileName = ExtractTargetFileName(text);
            if (!string.IsNullOrEmpty(result.TargetFileName))
                score += 10;

            // 2. 检测工作表名称
            result.TargetSheetName = ExtractSheetName(text);
            if (!string.IsNullOrEmpty(result.TargetSheetName))
                score += 5;

            // 3. 检测去重需求
            result.RemoveDuplicates = DetectDedup(text);
            if (result.RemoveDuplicates)
                score += 10;

            // 4. 检测唯一键
            result.UniqueKeyColumns = ExtractUniqueKeys(text);
            if (result.UniqueKeyColumns.Any())
                score += 15;

            // 5. 检测表头处理
            result.SkipHeader = DetectSkipHeader(text);
            score += 5;

            // 6. 检测合并方式
            result.MergeMode = DetectMergeMode(text);
            score += 5;

            // 7. 检测排序需求
            result.SortColumn = ExtractSortColumn(text);
            if (!string.IsNullOrEmpty(result.SortColumn))
                score += 10;

            result.SortDescending = DetectSortDescending(text);

            // 8. 检测过滤需求
            result.FilterCondition = ExtractFilterCondition(text);
            if (!string.IsNullOrEmpty(result.FilterCondition))
                score += 10;

            // 9. 检测列选择
            result.SelectedColumns = ExtractSelectedColumns(text);
            if (result.SelectedColumns.Any())
                score += 10;

            // 10. 检测追加模式
            result.AppendMode = DetectAppendMode(text);
            if (result.AppendMode)
                score += 5;

            result.Confidence = Math.Min(score, 100);

            if (score == 0)
            {
                result.Notes.Add("未识别到具体合并规则，将使用默认配置");
            }
            else
            {
                result.Notes.Add($"已识别 {score}% 的合并需求");
            }

            GenerateSummary(result);
            return result;
        }

        private static string ExtractTargetFileName(string text)
        {
            var patterns = new[]
            {
                @"保存[到至]?\s*[""']?([\w\u4e00-\u9fa5\.\-_\s]+\.xlsx?)[""']?",
                @"输出[到至]?\s*[""']?([\w\u4e00-\u9fa5\.\-_\s]+\.xlsx?)[""']?",
                @"目标文件\s*[：:]\s*[""']?([\w\u4e00-\u9fa5\.\-_\s]+\.xlsx?)[""']?",
                @"生成\s*[""']?([\w\u4e00-\u9fa5\.\-_\s]+\.xlsx?)[""']?",
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                    return match.Groups[1].Value.Trim();
            }
            return string.Empty;
        }

        private static string ExtractSheetName(string text)
        {
            var patterns = new[]
            {
                @"工作表\s*[名名称称]?\s*[：:]\s*[""']?([\w\u4e00-\u9fa5]+)[""']?",
                @"sheet\s*[名名称称]?\s*[：:]\s*[""']?([\w\u4e00-\u9fa5]+)[""']?",
                @"表名\s*[：:]\s*[""']?([\w\u4e00-\u9fa5]+)[""']?",
                @"存[放到入至]?\s*[""']?([\w\u4e00-\u9fa5]+)[""']?\s*工作表",
                @"存[放到入至]?\s*[""']?([\w\u4e00-\u9fa5]+)[""']?\s*sheet",
                @"存[放到入至]?\s*[""']?([\w\u4e00-\u9fa5]+)[""']?\s*表",
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                    return match.Groups[1].Value.Trim();
            }
            return string.Empty;
        }

        private static bool DetectDedup(string text)
        {
            var keywords = new[]
            {
                "去重", "去除重复", "删除重复", "不重复", "唯一", "distinct",
                "去掉重复", "剔除重复", "避免重复", "无重复"
            };
            return keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
        }

        private static List<string> ExtractUniqueKeys(string text)
        {
            var keys = new List<string>();

            // 模式: 以 XX 为唯一键 / 按 XX 去重 / XX 不能重复
            var patterns = new[]
            {
                @"以\s*([^\s,，。！!；;]+)\s*为\s*唯一键",
                @"以\s*([^\s,，。！!；;]+)\s*作为\s*唯一键",
                @"按\s*([^\s,，。！!；;]+)\s*去重",
                @"按\s*([^\s,，。！!；;]+)\s*去除重复",
                @"([^\s,，。！!；;]+)\s*不能重复",
                @"([^\s,，。！!；;]+)\s*不重复",
                @"唯一键[是：:]\s*([^\s,，。！!；;]+)",
                @"去重字段[是：:]\s*([^\s,，。！!；;]+)",
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(text, pattern);
                if (match.Success)
                {
                    var key = match.Groups[1].Value.Trim();
                    if (!keys.Contains(key) && !string.IsNullOrEmpty(key))
                        keys.Add(key);
                }
            }

            // 模式: 多个字段，用逗号或顿号分隔
            var multiPattern = @"(?:唯一键|去重字段|按)\s*[是：:]\s*([^\s。！!；;]+)";
            var multiMatch = Regex.Match(text, multiPattern);
            if (multiMatch.Success)
            {
                var parts = multiMatch.Groups[1].Value
                    .Split(new[] { ',', '，', '、', '和', '与' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s));
                foreach (var part in parts)
                {
                    if (!keys.Contains(part))
                        keys.Add(part);
                }
            }

            return keys;
        }

        private static bool DetectSkipHeader(string text)
        {
            // 默认跳过表头，除非明确说不跳过
            var skipKeywords = new[] { "跳过表头", "不要表头", "忽略表头", "不含表头", "无表头", "skip header" };
            var keepKeywords = new[] { "保留表头", "包含表头", "有表头", "不要跳过", "不跳过" };

            if (keepKeywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return false;
            return true;
        }

        private static MergeMode DetectMergeMode(string text)
        {
            if (text.Contains("按列名", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("按字段名", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("字段对应", StringComparison.OrdinalIgnoreCase))
                return MergeMode.ByName;

            if (text.Contains("按顺序", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("按位置", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("按列序", StringComparison.OrdinalIgnoreCase))
                return MergeMode.ByOrder;

            return MergeMode.ByName;
        }

        private static string ExtractSortColumn(string text)
        {
            var patterns = new[]
            {
                @"按\s*([^\s,，。！!；;]+)\s*排序",
                @"以\s*([^\s,，。！!；;]+)\s*排序",
                @"根据\s*([^\s,，。！!；;]+)\s*排序",
                @"排序字段[是：:]\s*([^\s,，。！!；;]+)",
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(text, pattern);
                if (match.Success)
                    return match.Groups[1].Value.Trim();
            }
            return string.Empty;
        }

        private static bool DetectSortDescending(string text)
        {
            return text.Contains("降序", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("从大到小", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("倒序", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractFilterCondition(string text)
        {
            var patterns = new[]
            {
                @"只保留\s*([^\s。！!；;]+)",
                @"仅保留\s*([^\s。！!；;]+)",
                @"筛选\s*([^\s。！!；;]+)",
                @"过滤\s*([^\s。！!；;]+)",
                @"条件[是：:]\s*([^\s。！!；;]+)",
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(text, pattern);
                if (match.Success)
                    return match.Groups[1].Value.Trim();
            }
            return string.Empty;
        }

        private static List<string> ExtractSelectedColumns(string text)
        {
            var columns = new List<string>();

            var patterns = new[]
            {
                @"只保留\s*([^\s。！!；;]+)\s*列",
                @"仅保留\s*([^\s。！!；;]+)\s*列",
                @"需要的列[是：:]\s*([^\s。！!；;]+)",
                @"选择\s*([^\s。！!；;]+)\s*列",
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(text, pattern);
                if (match.Success)
                {
                    var parts = match.Groups[1].Value
                        .Split(new[] { ',', '，', '、', '和', '与' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s));
                    foreach (var part in parts)
                    {
                        if (!columns.Contains(part))
                            columns.Add(part);
                    }
                }
            }

            return columns;
        }

        private static bool DetectAppendMode(string text)
        {
            return text.Contains("追加", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("附加", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("添加到已有文件", StringComparison.OrdinalIgnoreCase);
        }

        private static void ApplyDefaultConfig(ParsedRequirement result)
        {
            result.RemoveDuplicates = true;
            result.SkipHeader = true;
            result.MergeMode = MergeMode.ByName;
            result.TargetSheetName = "合并结果";
        }

        private static void GenerateSummary(ParsedRequirement result)
        {
            var parts = new List<string>
            {
                $"工作表: {result.TargetSheetName}"
            };

            if (result.RemoveDuplicates)
            {
                if (result.UniqueKeyColumns.Any())
                    parts.Add($"按 {string.Join(", ", result.UniqueKeyColumns)} 去重");
                else
                    parts.Add("整行去重");
            }

            if (!string.IsNullOrEmpty(result.SortColumn))
                parts.Add($"按 {result.SortColumn} {(result.SortDescending ? "降序" : "升序")} 排序");

            if (!string.IsNullOrEmpty(result.FilterCondition))
                parts.Add($"筛选条件: {result.FilterCondition}");

            if (result.SelectedColumns.Any())
                parts.Add($"保留列: {string.Join(", ", result.SelectedColumns)}");

            if (result.AppendMode)
                parts.Add("追加到已有文件");

            result.Summary = string.Join("; ", parts);
        }
    }

    public class ParsedRequirement
    {
        public string RawInput { get; set; } = string.Empty;
        public int Confidence { get; set; }
        public List<string> Notes { get; set; } = new();
        public string Summary { get; set; } = string.Empty;

        public string TargetFileName { get; set; } = string.Empty;
        public string TargetSheetName { get; set; } = "合并结果";
        public bool RemoveDuplicates { get; set; } = true;
        public List<string> UniqueKeyColumns { get; set; } = new();
        public bool SkipHeader { get; set; } = true;
        public MergeMode MergeMode { get; set; } = MergeMode.ByName;
        public string SortColumn { get; set; } = string.Empty;
        public bool SortDescending { get; set; }
        public string FilterCondition { get; set; } = string.Empty;
        public List<string> SelectedColumns { get; set; } = new();
        public bool AppendMode { get; set; }
    }
}
