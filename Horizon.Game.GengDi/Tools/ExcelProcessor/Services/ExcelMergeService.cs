using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Tools.ExcelProcessor.Models;
using MiniExcelLibs;

namespace Horizon.Game.GengDi.Tools.ExcelProcessor.Services
{
    public class ExcelMergeService
    {
        private readonly ExcelReaderService _reader;
        private readonly List<OperationLog> _logs = new();

        public List<OperationLog> Logs => _logs;

        public ExcelMergeService()
        {
            _reader = new ExcelReaderService();
        }

        public async Task<(bool Success, string Message, List<PreviewRow> Preview)> MergeAsync(MergeConfig config)
        {
            _logs.Clear();
            Log(LogLevel.Info, "开始处理 Excel 合并任务");

            if (!config.SourceFilePaths.Any())
                return (false, "请添加至少一个源文件", new());

            try
            {
                Log(LogLevel.Info, $"加载 {config.SourceFilePaths.Count} 个源文件...");
                var allData = new List<Dictionary<string, object?>>();
                var allColumns = new HashSet<string>();

                foreach (var sourcePath in config.SourceFilePaths)
                {
                    Log(LogLevel.Info, $"读取文件: {Path.GetFileName(sourcePath)}");
                    var data = await _reader.ReadDataAsync(sourcePath);

                    if (data.Count == 0)
                    {
                        Log(LogLevel.Warning, $"文件 {Path.GetFileName(sourcePath)} 无数据，跳过");
                        continue;
                    }

                    foreach (var row in data)
                    {
                        foreach (var key in row.Keys)
                        {
                            allColumns.Add(key);
                        }
                    }

                    allData.AddRange(data);
                    Log(LogLevel.Success, $"已读取 {data.Count} 行数据");
                }

                Log(LogLevel.Info, $"共读取 {allData.Count} 行原始数据");

                int beforeDedup = allData.Count;

                if (config.RemoveDuplicates)
                {
                    allData = RemoveDuplicates(allData, config.UniqueKeyColumns);
                    int removed = beforeDedup - allData.Count;
                    if (removed > 0)
                        Log(LogLevel.Info, $"去重完成，移除 {removed} 条重复数据");
                    else
                        Log(LogLevel.Success, "无重复数据");
                }

                var preview = GeneratePreview(allData, allColumns, 50);

                string targetDir = Path.GetDirectoryName(config.TargetFilePath) ?? ".";
                if (!Directory.Exists(targetDir))
                    Directory.CreateDirectory(targetDir);

                Log(LogLevel.Info, $"写入目标文件: {config.TargetFilePath}");

                var outputData = NormalizeData(allData, allColumns);

                await Task.Run(() =>
                {
                    if (config.AppendToExisting && File.Exists(config.TargetFilePath))
                    {
                        MiniExcel.Insert(config.TargetFilePath, outputData, sheetName: config.TargetSheetName);
                    }
                    else
                    {
                        MiniExcel.SaveAs(config.TargetFilePath, outputData, sheetName: config.TargetSheetName);
                    }
                });

                Log(LogLevel.Success, $"合并完成！共 {allData.Count} 行数据已写入");
                return (true, $"成功！共合并 {allData.Count} 行数据", preview);
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"处理失败: {ex.Message}");
                return (false, $"处理失败: {ex.Message}", new());
            }
        }

        private List<Dictionary<string, object?>> RemoveDuplicates(
            List<Dictionary<string, object?>> data,
            List<string> keyColumns)
        {
            if (!keyColumns.Any())
            {
                var seen = new HashSet<string>();
                return data.Where(row =>
                {
                    var key = string.Join("|", row.Values.Select(v => v?.ToString() ?? ""));
                    return seen.Add(key);
                }).ToList();
            }

            var seenKeys = new HashSet<string>();
            return data.Where(row =>
            {
                var key = string.Join("|", keyColumns.Select(k => row.GetValueOrDefault(k)?.ToString() ?? ""));
                return seenKeys.Add(key);
            }).ToList();
        }

        private List<Dictionary<string, object?>> NormalizeData(
            List<Dictionary<string, object?>> data,
            HashSet<string> allColumns)
        {
            return data.Select(row =>
            {
                var normalized = new Dictionary<string, object?>();
                foreach (var col in allColumns)
                {
                    normalized[col] = row.GetValueOrDefault(col);
                }
                return normalized;
            }).ToList();
        }

        private List<PreviewRow> GeneratePreview(
            List<Dictionary<string, object?>> data,
            HashSet<string> columns,
            int maxRows)
        {
            var preview = new List<PreviewRow>();
            var cols = columns.ToList();
            var count = Math.Min(data.Count, maxRows);

            for (int i = 0; i < count; i++)
            {
                var row = new PreviewRow { RowNumber = i + 1 };
                foreach (var col in cols)
                {
                    row.Cells[col] = data[i].GetValueOrDefault(col);
                }
                preview.Add(row);
            }

            return preview;
        }

        private void Log(LogLevel level, string message, string detail = "")
        {
            _logs.Add(new OperationLog
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message,
                Detail = detail
            });
        }
    }
}
