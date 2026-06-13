using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Tools.ExcelProcessor.Models;
using MiniExcelLibs;

namespace Horizon.Game.GengDi.Tools.ExcelProcessor.Services
{
    public class ExcelReaderService
    {
        public async Task<ExcelSourceFile> AnalyzeFileAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                var result = new ExcelSourceFile
                {
                    FileName = Path.GetFileName(filePath),
                    FilePath = filePath
                };

                try
                {
                    if (!File.Exists(filePath))
                    {
                        result.ErrorMessage = "文件不存在";
                        return result;
                    }

                    var ext = Path.GetExtension(filePath).ToLower();
                    if (ext != ".xlsx" && ext != ".xls" && ext != ".csv")
                    {
                        result.ErrorMessage = "仅支持 .xlsx, .xls, .csv 格式";
                        return result;
                    }

                    using var stream = File.OpenRead(filePath);
                    var sheets = MiniExcel.GetSheetNames(stream);
                    result.SheetNames = sheets.ToList();

                    if (sheets.Any())
                    {
                        using var stream2 = File.OpenRead(filePath);
                        var rows = MiniExcel.Query(stream2, sheetName: sheets.First()).ToList();
                        result.RowCount = rows.Count;

                        if (rows.Count > 0)
                        {
                            var firstRow = rows[0] as IDictionary<string, object>;
                            if (firstRow != null)
                            {
                                int idx = 0;
                                foreach (var col in firstRow.Keys)
                                {
                                    var val = firstRow[col];
                                    result.Columns.Add(new ExcelColumn
                                    {
                                        Index = idx++,
                                        Name = col,
                                        DetectedType = DetectType(val),
                                        IsSelected = true,
                                        Alias = col
                                    });
                                }
                            }
                        }
                    }

                    result.IsLoaded = true;
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = $"分析失败: {ex.Message}";
                }

                return result;
            });
        }

        public async Task<List<Dictionary<string, object?>>> ReadDataAsync(string filePath, string? sheetName = null)
        {
            return await Task.Run(() =>
            {
                using var stream = File.OpenRead(filePath);
                var rows = MiniExcel.Query(stream, sheetName: sheetName).ToList();
                var result = new List<Dictionary<string, object?>>();

                foreach (var row in rows)
                {
                    if (row is IDictionary<string, object> dict)
                    {
                        result.Add(dict.ToDictionary(k => k.Key, v => (object?)v.Value));
                    }
                }
                return result;
            });
        }

        private static ColumnType DetectType(object? value)
        {
            if (value == null) return ColumnType.Text;
            if (value is bool) return ColumnType.Boolean;
            if (value is DateTime) return ColumnType.Date;
            if (double.TryParse(value.ToString(), out _)) return ColumnType.Number;
            return ColumnType.Text;
        }
    }
}
