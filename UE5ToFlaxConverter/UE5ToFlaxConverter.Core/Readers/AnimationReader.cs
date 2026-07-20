using System.IO;
using System.Numerics;
using Microsoft.Extensions.Logging;
using UE5ToFlaxConverter.Core.Models;
using UObject = CUE4Parse.UE4.Assets.Exports.UObject;

namespace UE5ToFlaxConverter.Core.Readers;

/// <summary>
/// UE5 动画资源读取器。支持 AnimSequence / AnimMontage / BlendSpace。
/// 使用 CUE4Parse 强类型 GetOrDefault&lt;T&gt; + 反射回退，兼容多版本 CUE4Parse。
/// </summary>
public sealed class AnimationReader
{
    private readonly UassetProvider _provider;
    private readonly ILogger<AnimationReader>? _logger;

    public AnimationReader(UassetProvider provider, ILogger<AnimationReader>? logger = null)
    {
        _provider = provider;
        _logger = logger;
    }

    public IntermediateAnimation Read(string assetPath)
    {
        // 资源名优先从路径提取，避免误用辅助对象名
        var assetName = Path.GetFileNameWithoutExtension(assetPath);
        UObject? mainObj = null;
        string className = string.Empty;

        try
        {
            // 遍历所有 Export，找真正的动画主对象（AnimSequence/Montage/BlendSpace）
            // 优先按 ExportType 精确匹配（CUE4Parse 公有属性，比反射 Class 字段稳定）
            var allExports = _provider.LoadAllObjects(assetPath);
            foreach (var obj in allExports)
            {
                var exportType = obj.ExportType ?? string.Empty;
                if (string.IsNullOrEmpty(exportType)) continue;

                if (exportType.Contains("BlendSpace", StringComparison.OrdinalIgnoreCase))
                {
                    mainObj = obj; className = exportType; break;
                }
                if (exportType.Contains("AnimMontage", StringComparison.OrdinalIgnoreCase))
                {
                    mainObj = obj; className = exportType; break;
                }
                if (exportType.Contains("AnimSequence", StringComparison.OrdinalIgnoreCase)
                    || exportType.Contains("AnimStreamable", StringComparison.OrdinalIgnoreCase))
                {
                    mainObj = obj; className = exportType; break;
                }
            }

            // 退化1：按类名反射查找
            if (mainObj == null)
            {
                foreach (var obj in allExports)
                {
                    var cls = ReflectionHelper.GetClassName(obj);
                    if (cls.Contains("BlendSpace", StringComparison.OrdinalIgnoreCase) ||
                        cls.Contains("AnimMontage", StringComparison.OrdinalIgnoreCase) ||
                        cls.Contains("AnimSequence", StringComparison.OrdinalIgnoreCase))
                    {
                        mainObj = obj; className = cls; break;
                    }
                }
            }

            // 退化2：使用 LoadObject 自动推断
            if (mainObj == null)
            {
                mainObj = _provider.LoadObject(assetPath);
                className = mainObj.ExportType ?? ReflectionHelper.GetClassName(mainObj);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("加载 Animation 失败: {Path} -> {Msg}", assetPath, ex.Message);
            return new IntermediateAnimation
            {
                SourcePath = assetPath,
                AssetName = assetName,
                Kind = AnimationKind.Sequence
            };
        }

        _logger?.LogInformation("读取 Animation: {Name} ({Class})", assetName, className);

        // 根据 UE5 类名识别类型
        var kind = ResolveAnimationKind(className, assetName);
        var anim = new IntermediateAnimation
        {
            SourcePath = assetPath,
            AssetName = assetName,
            Kind = kind
        };

        // 公共属性（AnimSequenceBase 字段）
        ReadCommonProperties(mainObj, anim);

        // 类型特定读取
        switch (kind)
        {
            case AnimationKind.Montage:
                ReadMontage(mainObj, anim);
                break;
            case AnimationKind.BlendSpace:
                ReadBlendSpace(mainObj, anim);
                break;
        }

        return anim;
    }

    private static AnimationKind ResolveAnimationKind(string className, string assetName)
    {
        if (className.Contains("BlendSpace", StringComparison.OrdinalIgnoreCase))
            return AnimationKind.BlendSpace;
        if (className.Contains("AnimMontage", StringComparison.OrdinalIgnoreCase))
            return AnimationKind.Montage;
        if (className.Contains("AnimBlueprint", StringComparison.OrdinalIgnoreCase)
            || className.Contains("AnimBP", StringComparison.OrdinalIgnoreCase))
            return AnimationKind.AnimBP;
        // 后备：根据命名约定
        var upper = assetName.ToUpperInvariant();
        if (upper.StartsWith("BS_") || upper.Contains("BLENDSPACE"))
            return AnimationKind.BlendSpace;
        if (upper.StartsWith("AM_") || upper.Contains("MONTAGE"))
            return AnimationKind.Montage;
        if (upper.StartsWith("ABP_") || upper.Contains("ANIMBP"))
            return AnimationKind.AnimBP;
        return AnimationKind.Sequence;
    }

    /// <summary>
    /// 读取 AnimSequenceBase 的公共属性：
    /// SequenceLength / NumFrames / FrameRate / Skeleton / Notifies / TrackBoneNames。
    /// </summary>
    private void ReadCommonProperties(UObject obj, IntermediateAnimation anim)
    {
        // SequenceLength（float, 秒）
        anim.DurationSeconds = obj.GetOrDefault<float>("SequenceLength");

        // NumFrames
        anim.TotalFrames = obj.GetOrDefault<int>("NumFrames");
        if (anim.TotalFrames == 0 && anim.DurationSeconds > 0 && anim.FrameRate > 0)
            anim.TotalFrames = (int)(anim.DurationSeconds * anim.FrameRate);

        // FrameRate：FDisplayRate 有 Numerator/Denominator 两个字段
        var frameRateObj = obj.GetOrDefault<object>("FrameRate");
        if (frameRateObj != null)
        {
            var numerator = ReflectionHelper.GetSingle(frameRateObj, "Numerator", 30f);
            var denominator = ReflectionHelper.GetSingle(frameRateObj, "Denominator", 1f);
            if (denominator > 0)
                anim.FrameRate = numerator / denominator;
        }

        // 推导时长（若 SequenceLength 未读到但 NumFrames 与 FrameRate 已知）
        if (anim.DurationSeconds <= 0 && anim.TotalFrames > 0 && anim.FrameRate > 0)
            anim.DurationSeconds = anim.TotalFrames / anim.FrameRate;

        // Skeleton 引用名
        var skeleton = obj.GetOrDefault<object>("Skeleton");
        if (skeleton != null)
        {
            anim.SkeletonName = ReflectionHelper.GetMember(skeleton, "Name")?.ToString()
                              ?? ReflectionHelper.GetMember(skeleton, "AssetPathName")?.ToString()
                              ?? string.Empty;
        }

        // TrackNames（AnimSequence 拥有；AnimSequenceBase 没有）
        var trackNames = obj.GetOrDefault<object[]>("TrackNames");
        if (trackNames != null)
        {
            foreach (var t in trackNames)
            {
                var name = t?.ToString() ?? string.Empty;
                if (!string.IsNullOrEmpty(name))
                    anim.TrackBoneNames.Add(name);
            }
        }

        // AnimNotifies
        ReadNotifies(obj, anim);

        // FloatCurves（动画曲线）
        ReadFloatCurves(obj, anim);
    }

    private void ReadNotifies(UObject obj, IntermediateAnimation anim)
    {
        var notifies = obj.GetOrDefault<object[]>("Notifies");
        if (notifies == null) return;

        foreach (var n in notifies)
        {
            if (n == null) continue;
            var notify = new AnimNotify
            {
                Name = ReflectionHelper.GetMember(n, "Name")?.ToString() ?? "AnimNotify",
                Time = ReflectionHelper.GetSingle(n, "Time"),
                ClassPath = ReflectionHelper.GetMember(n, "NotifyName")?.ToString()
                         ?? ReflectionHelper.GetMember(n, "Notify")?.ToString()
            };

            // 尝试读取 NotifyState 持续时间
            var duration = ReflectionHelper.GetMember(n, "Duration");
            if (duration != null)
                notify.Payload["Duration"] = ReflectionHelper.GetSingle(duration, "Value");

            anim.Notifies.Add(notify);
        }
    }

    private void ReadFloatCurves(UObject obj, IntermediateAnimation anim)
    {
        var curves = obj.GetOrDefault<object[]>("FloatCurves");
        if (curves == null) return;

        foreach (var c in curves)
        {
            if (c == null) continue;
            var curve = new AnimationCurve
            {
                Name = ReflectionHelper.GetMember(c, "Name")?.ToString()
                     ?? ReflectionHelper.GetMember(c, "CurveName")?.ToString()
                     ?? $"Curve_{anim.FloatCurves.Count}"
            };

            // FloatCurve 内部 KeyValuePairs 或 Keys/Values
            var keys = ReflectionHelper.GetEnumerableMember(c, "Keys");
            var values = ReflectionHelper.GetEnumerableMember(c, "Values");
            if (keys != null)
            {
                foreach (var k in keys) curve.Times.Add(ReflectionHelper.GetSingle(k));
            }
            if (values != null)
            {
                foreach (var v in values) curve.Values.Add(ReflectionHelper.GetSingle(v));
            }

            // 退化：尝试 KeyValuePairs
            if (curve.Times.Count == 0)
            {
                var pairs = ReflectionHelper.GetEnumerableMember(c, "KeyValuePairs");
                if (pairs != null)
                {
                    foreach (var p in pairs)
                    {
                        curve.Times.Add(ReflectionHelper.GetSingle(p, "Time"));
                        curve.Values.Add(ReflectionHelper.GetSingle(p, "Value"));
                    }
                }
            }

            anim.FloatCurves.Add(curve);
        }
    }

    /// <summary>
    /// 读取 AnimMontage 的 SlotAnimTracks / CompositeSections，转换为 MontageSegments。
    /// </summary>
    private void ReadMontage(UObject obj, IntermediateAnimation anim)
    {
        // 方式1：CompositeSections
        var compositeSections = obj.GetOrDefault<object[]>("CompositeSections");
        if (compositeSections != null)
        {
            foreach (var sec in compositeSections)
            {
                if (sec == null) continue;
                // 优先读取 SegmentEndFrame（按帧数除以帧率换算为秒），
                // 若未读到则保留 -1（由 Writer 的 NormalizeMontageSegments 替换为动画总时长）
                var endFrame = ReflectionHelper.GetMember(sec, "SegmentEndFrame");
                var endSeconds = endFrame != null
                    ? ReflectionHelper.GetSingle(endFrame) / Math.Max(1f, anim.FrameRate)
                    : -1f;

                var segment = new AnimSegment
                {
                    SectionName = ReflectionHelper.GetMember(sec, "SectionName")?.ToString() ?? "Default",
                    Start = ReflectionHelper.GetSingle(sec, "StartFrame") / Math.Max(1f, anim.FrameRate),
                    End = endSeconds,
                    LoopCount = ReflectionHelper.GetInt32(sec, "LoopCount", 1)
                };
                anim.MontageSegments.Add(segment);
            }
        }

        // 方式2：SlotAnimTracks（用于补全 TrackBoneNames）
        var slotTracks = obj.GetOrDefault<object[]>("SlotAnimTracks");
        if (slotTracks != null)
        {
            foreach (var slot in slotTracks)
            {
                var slotName = ReflectionHelper.GetMember(slot, "SlotName")?.ToString();
                if (!string.IsNullOrEmpty(slotName))
                    anim.TrackBoneNames.Add(slotName);
            }
        }

        // 退化：若未读到 CompositeSections，至少建一个默认段
        if (anim.MontageSegments.Count == 0 && anim.DurationSeconds > 0)
        {
            anim.MontageSegments.Add(new AnimSegment
            {
                SectionName = "Default",
                Start = 0f,
                End = anim.DurationSeconds,
                LoopCount = 1
            });
        }
    }

    /// <summary>
    /// 读取 BlendSpace 的 BlendSamples。
    /// </summary>
    private void ReadBlendSpace(UObject obj, IntermediateAnimation anim)
    {
        var samples = obj.GetOrDefault<object[]>("BlendSamples");
        if (samples == null) return;

        foreach (var s in samples)
        {
            if (s == null) continue;
            var sample = new BlendSpaceSample
            {
                AnimName = ReflectionHelper.GetMember(s, "Animation")?.ToString()
                         ?? ReflectionHelper.GetMember(s, "AnimName")?.ToString()
                         ?? $"Sample_{anim.BlendSamples.Count}"
            };

            // BlendSample 通常有 X/Y 坐标
            var x = ReflectionHelper.GetSingle(s, "X");
            var y = ReflectionHelper.GetSingle(s, "Y");
            sample.Position = new Vector2(x, y);

            anim.BlendSamples.Add(sample);
        }

        // BlendAxes（轴标签）
        var axes = obj.GetOrDefault<object[]>("AxisLabels");
        if (axes != null)
        {
            foreach (var a in axes)
            {
                var label = a?.ToString();
                if (!string.IsNullOrEmpty(label))
                    anim.BlendAxes.Add(new Vector2(float.Parse(label), 0));
            }
        }
    }
}
