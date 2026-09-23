// PCG Plugin - Deterministic random number generation
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §3.4.

using System;
using FlaxEngine;

namespace PCG
{
    /// <summary>
    /// PCG-specific deterministic RNG derived from a (globalSeed, nodeId, pointIndex) triple.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why not a sequential RNG.</b> Incremental graph execution re-runs only the nodes downstream of a
    /// changed parameter. With a sequential generator, recomputing a subset of points yields different values
    /// than a full run - visually, "I changed one parameter and unrelated points jumped". Deriving the stream
    /// from a stable per-point triple instead means a single point recomputes to exactly the same value as it
    /// would in a full run, which is what makes incremental execution correct.
    /// </para>
    /// <para>
    /// This is why the design ties incremental execution and deterministic RNG together: they cannot be
    /// chosen independently.
    /// </para>
    /// <para>
    /// Deliberately does <b>not</b> use <c>FlaxEngine.RandomStream</c> for the per-point stream: its sequence
    /// is not specified across engine versions, which would break result reproducibility after an engine
    /// upgrade. The engine type is used only for whole-graph seeding convenience.
    /// </para>
    /// </remarks>
    [Serializable]
    public struct PCGDeterministicRng
    {
        private ulong _state;
        private int _spareIndex;
        private bool _hasSpare;
        private float _spare;

        /// <summary>Creates a generator for one point of one node.</summary>
        /// <param name="globalSeed">Graph-level seed (<see cref="PCGExecutionContext.Seed"/>).</param>
        /// <param name="nodeId">Stable node identifier within the compiled graph.</param>
        /// <param name="pointIndex">Index of the point being generated.</param>
        public PCGDeterministicRng(int globalSeed, int nodeId, int pointIndex)
        {
            _state = Mix3(globalSeed, nodeId, pointIndex);
            _spareIndex = 0;
            _hasSpare = false;
            _spare = 0.0f;
        }

        /// <summary>
        /// Creates a generator from two values, for cases without a natural point index
        /// (for example picking a spawn variant per node).
        /// </summary>
        public PCGDeterministicRng(int globalSeed, int nodeId)
        {
            _state = Mix3(globalSeed, nodeId, 0x5F3759DF);
            _spareIndex = 0;
            _hasSpare = false;
            _spare = 0.0f;
        }

        private static ulong Mix3(int globalSeed, int nodeId, int pointIndex)
        {
            // SplitMix64-style avalanche so neighbouring (seed, node, index) triples land far apart in the
            // stream. Without this, adjacent point indices would produce correlated first outputs.
            ulong x = unchecked((ulong)(uint)globalSeed);
            x = x * 0x9E3779B97F4A7C15UL + unchecked((ulong)(uint)nodeId);
            x ^= x >> 30;
            x *= 0xBF58476D1CE4E5B9UL;
            x ^= x >> 27;
            x = x * 0x94D049BB133111EBUL + unchecked((ulong)(uint)pointIndex);
            x ^= x >> 31;
            return x == 0 ? 0x2545F4914F6CDD1DUL : x;
        }

        private uint NextUInt()
        {
            // xorshift64* with the standard multiplier; period 2^64-1, adequate for procedural placement.
            _state ^= _state >> 12;
            _state ^= _state << 25;
            _state ^= _state >> 27;
            return (uint)((_state * 0x2545F4914F6CDD1DUL) >> 32);
        }

        /// <summary>Returns the next value in [0,1).</summary>
        public float NextFloat()
        {
            // 24 bits keeps every result exactly representable as float, so results are stable.
            return (NextUInt() >> 8) * (1.0f / 16777216.0f);
        }

        /// <summary>Returns the next value in [min,max).</summary>
        public float NextRange(float min, float max)
        {
            return min + (max - min) * NextFloat();
        }

        /// <summary>Returns the next integer in [min,max).</summary>
        public int NextRange(int min, int max)
        {
            if (max <= min)
                return min;

            return min + (int)(NextUInt() % (uint)(max - min));
        }

        /// <summary>Returns the next boolean with the given probability of being true.</summary>
        public bool NextBool(float probability = 0.5f)
        {
            return NextFloat() < probability;
        }

        /// <summary>Returns a uniformly distributed unit-length direction.</summary>
        public Vector3 NextUnitVector()
        {
            // Marsaglia's method: pick a point in the unit disk, lift to the sphere.
            float x, y, s;
            int guard = 0;
            do
            {
                x = NextRange(-1.0f, 1.0f);
                y = NextRange(-1.0f, 1.0f);
                s = x * x + y * y;
                guard++;
            }
            while (s >= 1.0f && guard < 32);

            if (s >= 1.0f)
            {
                // Degenerate fallback; keeps the function total without biasing normal use.
                x = 0.0f;
                y = 0.0f;
                s = 0.0f;
            }

            float f = 2.0f * Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - s));
            return new Vector3(2.0f * x * f * 0.5f, 2.0f * y * f * 0.5f, 1.0f - 2.0f * s);
        }

        /// <summary>Returns a uniformly distributed random rotation.</summary>
        public Quaternion NextRotation()
        {
            // Shoemake's uniform random quaternion.
            float u1 = NextFloat();
            float u2 = NextFloat();
            float u3 = NextFloat();

            float r1 = Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - u1));
            float r2 = Mathf.Sqrt(u1);
            float t1 = Mathf.TwoPi * u2;
            float t2 = Mathf.TwoPi * u3;

            return new Quaternion(r1 * Mathf.Sin(t1), r1 * Mathf.Cos(t1), r2 * Mathf.Sin(t2), r2 * Mathf.Cos(t2));
        }

        /// <summary>Returns a random point inside the given box, in the box's own space.</summary>
        public Vector3 NextPointInBox(BoundingBox box)
        {
            var min = box.Minimum;
            var max = box.Maximum;
            return new Vector3(NextRange(min.X, max.X), NextRange(min.Y, max.Y), NextRange(min.Z, max.Z));
        }

        /// <summary>Returns the next standard-normal sample (Box-Muller), buffering the second value.</summary>
        public float NextGaussian()
        {
            if (_hasSpare)
            {
                _hasSpare = false;
                return _spare;
            }

            float u, v, s;
            do
            {
                u = NextRange(-1.0f, 1.0f);
                v = NextRange(-1.0f, 1.0f);
                s = u * u + v * v;
            }
            while (s >= 1.0f || s == 0.0f);

            float mul = Mathf.Sqrt(-2.0f * Mathf.Log(s) / s);
            _spare = v * mul;
            _spareIndex++;
            _hasSpare = true;
            return u * mul;
        }

        /// <summary>Gets how many values have been drawn. Useful for tests and diagnostics.</summary>
        public int ConsumedCount => _spareIndex;

        /// <summary>
        /// Builds a graph-level seed. Uses the engine's RNG only to pick a starting value; the actual
        /// per-point streams are produced by this struct so results stay reproducible.
        /// </summary>
        public static int MakeGraphSeed(bool randomize)
        {
            if (!randomize)
                return 0;

            var stream = new RandomStream();
            stream.GenerateNewSeed();
            return stream.GetCurrentSeed();
        }
    }
}
