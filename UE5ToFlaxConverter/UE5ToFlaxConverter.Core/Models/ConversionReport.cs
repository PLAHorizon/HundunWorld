namespace UE5ToFlaxConverter.Core.Models;

/// <summary>
/// 单次资源转换的报告。
/// </summary>
public sealed class ConversionReport
{
    public string SourcePath { get; set; } = string.Empty;
    public string? TargetPath { get; set; }
    public ConversionStatus Status { get; set; } = ConversionStatus.Pending;
    public TimeSpan Elapsed { get; set; }
    public List<ConversionMessage> Messages { get; set; } = new();

    public void Info(string message) =>
        Messages.Add(new ConversionMessage(ConversionSeverity.Info, message));

    public void Warn(string message) =>
        Messages.Add(new ConversionMessage(ConversionSeverity.Warning, message));

    public void Error(string message, Exception? ex = null) =>
        Messages.Add(new ConversionMessage(ConversionSeverity.Error, message, ex?.ToString()));
}

public enum ConversionStatus { Pending, Running, Success, PartialSuccess, Failed, Skipped }
public enum ConversionSeverity { Info, Warning, Error }

public sealed record ConversionMessage(
    ConversionSeverity Severity,
    string Text,
    string? ExceptionDetails = null);
