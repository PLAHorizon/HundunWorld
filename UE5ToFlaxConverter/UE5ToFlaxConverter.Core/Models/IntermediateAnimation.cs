using System.Numerics;

namespace UE5ToFlaxConverter.Core.Models;

/// <summary>
/// UE5 动画资源（AnimSequence / AnimMontage / BlendSpace）的中间表示。
/// </summary>
public sealed class IntermediateAnimation
{
    public string SourcePath { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public AnimationKind Kind { get; set; } = AnimationKind.Sequence;

    public string SkeletonName { get; set; } = string.Empty;
    public List<string> TrackBoneNames { get; set; } = new();

    public float DurationSeconds { get; set; }
    public float FrameRate { get; set; } = 30.0f;
    public int TotalFrames { get; set; }

    public List<AnimationTrack> Tracks { get; set; } = new();
    public List<AnimationCurve> FloatCurves { get; set; } = new();

    // 仅 AnimMontage
    public List<AnimNotify> Notifies { get; set; } = new();
    public List<AnimSegment> MontageSegments { get; set; } = new(); // 用于 Slot/Section

    // 仅 BlendSpace
    public List<BlendSpaceSample> BlendSamples { get; set; } = new();
    public List<Vector2> BlendAxes { get; set; } = new();
}

public enum AnimationKind { Sequence, Montage, BlendSpace, AnimBP }

public sealed class AnimationTrack
{
    public string BoneName { get; set; } = string.Empty;
    public List<Vector3> PositionKeys { get; set; } = new();
    public List<Quaternion> RotationKeys { get; set; } = new();
    public List<Vector3> ScaleKeys { get; set; } = new();
    public List<float> KeyTimes { get; set; } = new(); // 秒
}

public sealed class AnimationCurve
{
    public string Name { get; set; } = string.Empty; // 如 "Weight.GroundSpeed"
    public List<float> Times { get; set; } = new();
    public List<float> Values { get; set; } = new();
}

public sealed class AnimNotify
{
    public string Name { get; set; } = string.Empty;
    public float Time { get; set; }
    public string? ClassPath { get; set; } // UE5 Notify 类路径
    public Dictionary<string, object?> Payload { get; set; } = new();
}

public sealed class AnimSegment
{
    public string SectionName { get; set; } = string.Empty;
    public float Start { get; set; }
    /// <summary>
    /// 段结束时间（秒）。-1 表示尚未初始化，Writer 在写入前应替换为动画总时长。
    /// </summary>
    public float End { get; set; } = -1f;
    public int LoopCount { get; set; } = 1;
}

public sealed class BlendSpaceSample
{
    public string AnimName { get; set; } = string.Empty;
    public Vector2 Position { get; set; }
}
