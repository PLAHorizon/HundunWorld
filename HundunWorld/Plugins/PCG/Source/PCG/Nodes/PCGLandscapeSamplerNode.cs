// PCG Plugin - Landscape Sampler node
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.1 and §3.5.

using System;
using System.Threading;
using System.Threading.Tasks;
using FlaxEngine;
using PCG.Execution;
using PCG.TerrainSampling;

namespace PCG.Nodes
{
    /// <summary>
    /// Samples points across the terrain surface inside the generation bounds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Points are laid out on a jittered grid rather than by uniform random sampling. Uniform random
    /// placement clumps: some cells end up with several points and others with none, which reads as patches
    /// of bare terrain next to dense clusters. A jittered grid guarantees an even spread while still looking
    /// irregular. Each point's jitter comes from <see cref="PCGDeterministicRng"/> keyed on the point index,
    /// so a point recomputed in isolation lands in exactly the same place.
    /// </para>
    /// <para>
    /// The node writes <c>$Slope</c> for each point it produces, so a downstream slope filter can reuse the
    /// normal this node already had to compute instead of triggering a second terrain query per point.
    /// </para>
    /// </remarks>
    public sealed class PCGLandscapeSamplerNode : PCGNodeBase
    {
        /// <summary>Stable type id.</summary>
        public const string Id = "LandscapeSampler";

        // Parameter indices. Order is part of the saved graph format; append only.
        private const int PPointCount = 0;
        private const int PJitter = 1;
        private const int PHeightOffset = 2;
        private const int PAlignToNormal = 3;
        private const int PSampleMode = 4;
        private const int PFallbackToRayCast = 5;
        private const int PScale = 6;
        private const int PMaxSlopeDegrees = 7;
        private const int PSeedOffset = 8;

        /// <summary>Number of parameters this node reads. Used by the editor to size its value array.</summary>
        public const int ParameterCount = 9;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>Target number of points to place.</summary>
        public int PointCount = 1000;

        /// <summary>How far a point may move from its grid cell centre, as a fraction of the cell size.</summary>
        public float Jitter = 0.5f;

        /// <summary>Offset applied to the sampled surface height.</summary>
        public float HeightOffset;

        /// <summary>When set, each point's rotation aligns to the surface normal.</summary>
        public bool AlignToNormal = true;

        /// <summary>Which terrain query path to use.</summary>
        public PCGTerrainSampleMode SampleMode = PCGTerrainSampleMode.RayCast;

        /// <summary>When set, an unusable heightfield path falls back to ray casts.</summary>
        public bool FallbackToRayCast = true;

        /// <summary>Uniform scale applied to produced points.</summary>
        public float Scale = 1.0f;

        /// <summary>Points on surfaces steeper than this are skipped. 90 disables the check.</summary>
        public float MaxSlopeDegrees = 90.0f;

        /// <summary>Added to the graph seed so two samplers in one graph do not jitter identically.</summary>
        public int SeedOffset;

        /// <summary>Reported when the sampler could not run, for surfacing in the editor.</summary>
        public string LastError { get; private set; }

        /// <summary>Creates the node from serialized parameters.</summary>
        public PCGLandscapeSamplerNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            PointCount = Mathf.Max(0, _params.Get(PPointCount, 1000));
            Jitter = Mathf.Clamp(_params.Get(PJitter, 0.5f), 0.0f, 1.0f);
            HeightOffset = _params.Get(PHeightOffset, 0.0f);
            AlignToNormal = _params.Get(PAlignToNormal, true);
            SampleMode = _params.Get(PSampleMode, PCGTerrainSampleMode.RayCast);
            FallbackToRayCast = _params.Get(PFallbackToRayCast, true);
            Scale = Mathf.Max(0.0001f, _params.Get(PScale, 1.0f));
            MaxSlopeDegrees = Mathf.Clamp(_params.Get(PMaxSlopeDegrees, 90.0f), 0.0f, 90.0f);
            SeedOffset = _params.Get(PSeedOffset, 0);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Landscape Sampler";

        /// <inheritdoc />
        public override bool IsThreadSafe => SampleMode == PCGTerrainSampleMode.HeightfieldInterpolated;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => Array.Empty<PCGPinDefinition>();

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output, tooltip: "Sampled surface points."),
        };

        /// <inheritdoc />
        public override string DescribeParameters() =>
            $"count={PointCount}, jitter={Jitter:F2}, mode={SampleMode}, align={AlignToNormal}, maxSlope={MaxSlopeDegrees:F1}";

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            LastError = null;

            if (PointCount <= 0)
                return PCGData.Empty;

            if (context.World == null || !context.World.HasTerrain)
            {
                LastError = "No terrain is registered in the execution context.";
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
                return PCGData.Empty;
            }

            var terrain = context.World.FindTerrainAt(context.GenerationBounds.Center);
            if (terrain == null)
            {
                LastError = "No terrain covers the generation bounds.";
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
                return PCGData.Empty;
            }

            var calibration = PCGTerrainCalibrationStore.Get(terrain);
            var sampler = new PCGTerrainSampler(terrain, calibration);

            if (SampleMode == PCGTerrainSampleMode.HeightfieldInterpolated && !sampler.HasValidCalibration)
            {
                if (!FallbackToRayCast)
                {
                    LastError = "Heightfield mode requested but no valid terrain calibration exists.";
                    Debug.LogWarning($"[PCG] {Id}: {LastError}");
                    return PCGData.Empty;
                }

                // Not an error: falling back is the designed behaviour, but it should be visible so the
                // performance difference is not a mystery.
                Debug.Log($"[PCG] {Id}: terrain is not calibrated, using ray casts. " +
                          "Run PCGTerrainProbe.Calibrate to enable the fast heightfield path.");
            }

            var bounds = context.GenerationBounds;

            if (bounds.Maximum.X - bounds.Minimum.X <= 0.0f || bounds.Maximum.Z - bounds.Minimum.Z <= 0.0f)
            {
                LastError = "Generation bounds have no XZ extent.";
                Debug.LogWarning($"[PCG] {Id}: {LastError}");
                return PCGData.Empty;
            }

            // Jittered grid: choose a cell count whose product is close to the requested point count while
            // matching the bounds' aspect ratio, so the density is even rather than stretched.
            float widthX = bounds.Maximum.X - bounds.Minimum.X;
            float widthZ = bounds.Maximum.Z - bounds.Minimum.Z;
            double area = (double)widthX * widthZ;

            float cellSize = Mathf.Sqrt((float)(area / PointCount));

            int cellsX = Mathf.Max(1, (int)Mathf.Round(widthX / cellSize));
            int cellsZ = Mathf.Max(1, (int)Mathf.Round(widthZ / cellSize));

            // Recompute the cell size from the rounded counts so the requested count is honoured as closely
            // as an integer grid allows.
            float stepX = widthX / cellsX;
            float stepZ = widthZ / cellsZ;
            int capacity = cellsX * cellsZ;

            var points = new PCGPoint[capacity];
            var slopes = new float[capacity];
            int written = 0;

            float startY = bounds.Maximum.Y > 0.0f ? bounds.Maximum.Y + 1000.0f : 10000.0f;

            for (int iz = 0; iz < cellsZ; iz++)
            {
                context.ThrowIfCancelled(iz, 1);

                for (int ix = 0; ix < cellsX; ix++)
                {
                    int cellIndex = iz * cellsX + ix;

                    // Per-point stream: stable under reordering and partial recomputation.
                    var rng = new PCGDeterministicRng(context.Seed + SeedOffset, context.CurrentNodeId, cellIndex);

                    float baseX = bounds.Minimum.X + (ix + 0.5f) * stepX;
                    float baseZ = bounds.Minimum.Z + (iz + 0.5f) * stepZ;

                    float x = baseX + rng.NextRange(-0.5f, 0.5f) * stepX * Jitter;
                    float z = baseZ + rng.NextRange(-0.5f, 0.5f) * stepZ * Jitter;

                    var sample = sampler.Sample(x, z, SampleMode, startY, FallbackToRayCast);
                    if (!sample.Hit)
                        continue;

                    if (sample.SlopeDegrees > MaxSlopeDegrees)
                        continue;

                    var rotation = Quaternion.Identity;
                    if (AlignToNormal)
                    {
                        // LookRotation orients +Z along the given forward, so build a frame whose up is the
                        // surface normal and whose forward stays as close to world +Z as possible.
                        var up = sample.Normal;
                        var forward = Vector3.Normalize(Vector3.Cross(up, Vector3.Right));
                        if (forward.LengthSquared < 1e-6f)
                            forward = Vector3.Normalize(Vector3.Cross(up, Vector3.Forward));

                        rotation = Quaternion.LookRotation(new Float3(forward.X, forward.Y, forward.Z),
                            new Float3(up.X, up.Y, up.Z));
                    }

                    var position = new Vector3(x, sample.Height + HeightOffset, z);

                    var point = PCGPoint.Create(position, 1.0f, cellIndex + SeedOffset);
                    point.Rotation = rotation;
                    point.Scale = new Float3(Scale);

                    // Give the point a non-degenerate bounds so pruning has something to work with before a
                    // spawner assigns a real model size.
                    point.UpdateBounds(new Vector3(Mathf.Max(stepX, 0.01f) * 0.5f, 0.0f, Mathf.Max(stepZ, 0.01f) * 0.5f));

                    points[written] = point;
                    slopes[written] = sample.SlopeDegrees;
                    written++;
                }
            }

            if (written == 0)
            {
                LastError = "No points were produced; every sample missed the terrain or exceeded the slope limit.";
                return PCGData.Empty;
            }

            // Trim to the points that actually hit, then attach storage sized to match. Building the
            // attribute set after the trim avoids allocating columns for the rejected cells.
            if (written < points.Length)
            {
                Array.Resize(ref points, written);
                Array.Resize(ref slopes, written);
            }

            var attributes = new PCGAttributeSet(written);
            attributes.DeclareAttributeUnsafe(PCGAttributeSet.SlopeName, PCGAttributeType.Float);

            var slopeColumn = attributes.GetWritableFloatArray(PCGAttributeSet.SlopeName);
            Array.Copy(slopes, slopeColumn, written);

            for (int i = 0; i < written; i++)
                points[i].Attributes = attributes;

            return new PCGData(points, attributes);
        }
    }
}
