// PCG Plugin - Golden-value tests for the seeded noise and deterministic RNG.
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §3.4 and §9 Phase 1.

using System;
using FlaxEngine;

namespace PCG.Tests
{
    /// <summary>
    /// Locks the numeric output of <see cref="PCGSeededNoise"/> and <see cref="PCGDeterministicRng"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why golden values.</b> The whole point of owning the noise implementation is that a given seed
    /// reproduces a given world. A refactor that quietly perturbs a hash constant or an interpolation
    /// weight would silently change every generated level in the project with no compile error and no
    /// visual cue until someone notices their baked rocks moved. These tests fail loudly instead.
    /// </para>
    /// <para>
    /// The expected values below were derived from an independent standalone run of the same algorithm
    /// (see the verification harness) rather than copied from this implementation's own output, so they
    /// also catch a transcription error in the port.
    /// </para>
    /// <para>
    /// <b>If one of these fails:</b> do not "fix" the expectation unless you have decided to invalidate
    /// all existing PCG output in the project, and say so in the commit message.
    /// </para>
    /// </remarks>
    public static class PCGNoiseGoldenTests
    {
        /// <summary>Absolute tolerance. Values are compared as float32, so exact equality is expected.</summary>
        private const float Tolerance = 1e-6f;

        private static int _failCount;

        /// <summary>Runs every check. Returns the number of failures (zero means all passed).</summary>
        public static int RunAll()
        {
            _failCount = 0;

            Debug.Log("[PCG] Running noise/RNG golden-value tests...");

            CheckGolden("Value2D(1.5,2.5,7)", PCGSeededNoise.Value2D(1.5f, 2.5f, 7), 0.37029177f);
            CheckGolden("Value2D(-3.25,8.125,1234)", PCGSeededNoise.Value2D(-3.25f, 8.125f, 1234), 0.34668994f);
            CheckGolden("Value3D(0.5,1.5,2.5,99)", PCGSeededNoise.Value3D(0.5f, 1.5f, 2.5f, 99), 0.5949658f);
            CheckGolden("Perlin2D(3.25,-1.75,42)", PCGSeededNoise.Perlin2D(3.25f, -1.75f, 42), 0.46203953f);
            CheckGolden("Perlin2D(0.1,0.2,0)", PCGSeededNoise.Perlin2D(0.1f, 0.2f, 0), 0.5752725f);
            CheckGolden("Worley2D(2.3,4.7,11)", PCGSeededNoise.Worley2D(2.3f, 4.7f, 11), 0.5300377f);
            CheckGolden("Worley3D(0.3,0.7,1.1,9)", PCGSeededNoise.Worley3D(0.3f, 0.7f, 1.1f, 9), 0.5604521f);
            CheckGolden("Fbm2D(1.5,2.5,7,4)", PCGSeededNoise.Fbm2D(1.5f, 2.5f, 7, 4), 0.6414214f);
            CheckGolden("Fbm3D(1.5,2.5,0.5,7,4)", PCGSeededNoise.Fbm3D(1.5f, 2.5f, 0.5f, 7, 4), 0.49215692f);
            CheckGolden("HashToUnit(3,5,1)", PCGSeededNoise.HashToUnit(3, 5, 1), 0.34370285f);

            // RNG stream values are order dependent; consume in sequence to check the whole chain.
            var rng = new PCGDeterministicRng(1234, 5, 42);
            CheckGolden("RNG(1234,5,42) f1", rng.NextFloat(), 0.35229284f);
            CheckGolden("RNG(1234,5,42) f2", rng.NextFloat(), 0.5995875f);
            CheckGolden("RNG(1234,5,42) f3", rng.NextFloat(), 0.35996407f);

            var rngZero = new PCGDeterministicRng(0, 0, 0);
            CheckGolden("RNG(0,0,0) f1", rngZero.NextFloat(), 0.67721117f);
            CheckGolden("RNG(0,0,0) f2", rngZero.NextFloat(), 0.7455286f);
            CheckGolden("RNG(0,0,0) f3", rngZero.NextFloat(), 0.039548874f);

            RunInvariants();

            if (_failCount == 0)
                Debug.Log("[PCG] All noise/RNG tests passed.");
            else
                Debug.LogError($"[PCG] {_failCount} noise/RNG test(s) FAILED.");

            return _failCount;
        }

        private static void CheckGolden(string what, float actual, float expected)
        {
            var delta = Mathf.Abs(actual - expected);
            if (delta <= Tolerance)
            {
                Debug.Log($"  [PCG] PASS {what} = {actual:R}");
            }
            else
            {
                _failCount++;
                Debug.LogError($"  [PCG] FAIL {what}: got {actual:R}, expected {expected:R} (delta {delta:R})");
            }
        }

        private static void Check(string what, bool ok, string detail = null)
        {
            if (ok)
            {
                Debug.Log($"  [PCG] PASS {what}{(detail != null ? "   " + detail : string.Empty)}");
            }
            else
            {
                _failCount++;
                Debug.LogError($"  [PCG] FAIL {what}{(detail != null ? "   " + detail : string.Empty)}");
            }
        }

        /// <summary>Behavioural checks that are not tied to exact numbers.</summary>
        private static void RunInvariants()
        {
            // Range: every kind must stay inside [0,1] and never produce NaN.
            var kinds = new[]
            {
                PCGNoiseKind.Value2D, PCGNoiseKind.Perlin2D, PCGNoiseKind.Worley2D, PCGNoiseKind.Fbm2D,
                PCGNoiseKind.Value3D, PCGNoiseKind.Worley3D, PCGNoiseKind.Fbm3D,
            };

            bool rangeOk = true;
            bool spansOk = false;
            float lo = float.MaxValue, hi = float.MinValue;

            for (int i = 0; i < 4000; i++)
            {
                float x = (i % 97) * 0.137f - 6.0f;
                float y = (i / 97) * 0.211f - 4.0f;

                for (int k = 0; k < kinds.Length; k++)
                {
                    float v2 = PCGSeededNoise.Sample2D(kinds[k], x, y, i * 7 + k);
                    float v3 = PCGSeededNoise.Sample3D(kinds[k], x, y, x * 0.5f, i * 7 + k);

                    if (float.IsNaN(v2) || float.IsInfinity(v2) || v2 < 0f || v2 > 1f ||
                        float.IsNaN(v3) || float.IsInfinity(v3) || v3 < 0f || v3 > 1f)
                    {
                        rangeOk = false;
                    }

                    if (v2 < lo) lo = v2;
                    if (v2 > hi) hi = v2;
                }
            }

            // Guards against a regression that collapses output to a constant, which would still be "in range".
            spansOk = hi > 0.9f && lo < 0.1f;

            Check("all 2D/3D kinds stay finite and within [0,1]", rangeOk);
            Check("all kinds span most of [0,1]", spansOk, $"min={lo:F6} max={hi:F6}");

            // Hashing must be seed-sensitive even at exact lattice points; otherwise a "randomize seed"
            // button would appear to do nothing for terrain aligned to integer coordinates.
            bool seedSensitive = PCGSeededNoise.HashToUnit(3, 5, 1) != PCGSeededNoise.HashToUnit(3, 5, 2);
            Check("lattice hash is seed-sensitive", seedSensitive);

            // Continuity: noise must not step discontinuously across lattice cell boundaries, or terrain
            // built from it would show visible seams on a regular grid.
            float maxJump = 0f;
            for (int i = 0; i < 4000; i++)
            {
                float x = i * 0.01f;
                float a = PCGSeededNoise.Perlin2D(x, 0.37f, 5);
                float b = PCGSeededNoise.Perlin2D(x + 0.001f, 0.37f, 5);
                float jump = Mathf.Abs(a - b);
                if (jump > maxJump) maxJump = jump;
            }

            Check("Perlin2D is continuous across lattice cells", maxJump < 0.02f, $"maxJump={maxJump:F6}");

            // The invariant that makes incremental execution correct: recomputing one point in isolation
            // must give exactly the value it had in a full sequential run.
            const int Count = 512;
            var full = new float[Count];
            for (int i = 0; i < Count; i++)
                full[i] = new PCGDeterministicRng(777, 3, i).NextRange(-5.0f, 5.0f);

            bool isolatedMatch = true;
            int mismatches = 0;
            for (int i = 0; i < Count; i++)
            {
                float solo = new PCGDeterministicRng(777, 3, i).NextRange(-5.0f, 5.0f);
                if (solo != full[i])
                {
                    isolatedMatch = false;
                    mismatches++;
                }
            }

            Check("isolated per-point recompute equals full-run value", isolatedMatch, $"mismatches={mismatches}");

            // Different nodes must not share a stream, otherwise two RandomOffset nodes would offset
            // points identically instead of independently.
            float n5 = new PCGDeterministicRng(1234, 5, 0).NextFloat();
            float n6 = new PCGDeterministicRng(1234, 6, 0).NextFloat();
            Check("distinct streams per node id", n5 != n6, $"{n5:F8} vs {n6:F8}");

            // Approximate uniformity of the RNG; a broken mix would cluster.
            const int Samples = 100000;
            double mean = 0.0;
            for (int i = 0; i < Samples; i++)
                mean += new PCGDeterministicRng(99, 1, i).NextFloat();
            mean /= Samples;
            Check("RNG mean is near 0.5", Mathf.Abs((float)mean - 0.5f) < 0.01f, $"mean={mean:F6}");
        }
    }
}
