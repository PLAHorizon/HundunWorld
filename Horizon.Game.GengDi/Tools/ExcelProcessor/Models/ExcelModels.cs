using System;
using System.Collections.Generic;

namespace Horizon.Game.GengDi.Tools.ExcelProcessor.Models
{
    public class ExcelSourceFile
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public List<string> SheetNames { get; set; } = new();
        public List<ExcelColumn> Columns { get; set; } = new();
        public int RowCount { get; set; }
        public bool IsLoaded { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public class ExcelColumn
    {
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public ColumnType DetectedType { get; set; } = ColumnType.Text;
        public bool IsSelected { get; set; } = true;
        public string Alias { get; set; } = string.Empty;
    }

    public enum ColumnType
    {
        Text,
        Number,
        Date,
        Boolean
    }

    public class MergeConfig
    {
        public List<string> SourceFilePaths { get; set; } = new();
        public string TargetFilePath { get; set; } = string.Empty;
        public string TargetSheetName { get; set; } = "合并结果";
        public bool AppendToExisting { get; set; } = false;
        public MergeMode Mode { get; set; } = MergeMode.ByName;
        public List<string> UniqueKeyColumns { get; set; } = new();
        public bool RemoveDuplicates { get; set; } = true;
        public bool SkipHeader { get; set; } = true;
        public List<ColumnMapping> ColumnMappings { get; set; } = new();
    }

    public enum MergeMode
    {
        ByName,
        ByOrder
    }

    public class ColumnMapping
    {
        public string SourceColumn { get; set; } = string.Empty;
        public string TargetColumn { get; set; } = string.Empty;
    }

    public class OperationLog
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public LogLevel Level { get; set; } = LogLevel.Info;
        public string Message { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
    }

    public enum LogLevel
    {
        Info,
        Success,
        Warning,
        Error
    }

    public class PreviewRow
    {
        public int RowNumber { get; set; }
        public Dictionary<string, object?> Cells { get; set; } = new();
    }
}
