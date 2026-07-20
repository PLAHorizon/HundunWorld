using System.Collections.Generic;

namespace UE5ToFlaxConverter.Core.Writers;

/// <summary>
/// 单个资源的 Writer 输出结果：包含输出文件列表、目标目录与待手动完成步骤。
/// </summary>
public sealed class WriterOutput
{
    /// <summary>该资源输出所在目录（绝对路径）。</summary>
    public string TargetDirectory { get; set; } = string.Empty;

    /// <summary>本次输出的所有文件清单（用于全局报告与导入脚本）。</summary>
    public List<OutputFile> Files { get; set; } = new();

    /// <summary>需要用户手动完成的步骤提示（如导出 FBX、复制 CS 文件等）。</summary>
    public List<string> PendingManualSteps { get; set; } = new();
}

/// <summary>
/// 输出文件描述。
/// </summary>
public sealed class OutputFile
{
    /// <summary>相对输出根目录的路径（使用正斜杠）。</summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// 文件种类标签：Prefab / Model / ImportManifest / ImportScript / MaterialMap / SkeletonHierarchy /
    /// AnimationMetadata / MontageSections / ParticleEmitter / ParticleSystem / ParticlePrefab /
    /// GameplayAbility / GameplayEffect / AttributeSet / EditorScript / Registry 等。
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>文件扩展名/格式：json / prefab / cs / bat 等。</summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>文件字节数（用于报告统计）。</summary>
    public long SizeBytes { get; set; }
}
