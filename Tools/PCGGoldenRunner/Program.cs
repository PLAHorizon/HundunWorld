// PCG golden-value probe - runs the real plugin noise/RNG sources with no engine present.
// See PCGGoldenRunner.csproj for why this exists.
//
// The EXPECTED table below is copied verbatim from
// HundunWorld/Plugins/PCG/Source/PCG/Tests/PCGNoiseGoldenTests.cs so that this probe answers exactly
// one question: do the constants committed as "golden" match what the shipped code computes?

using System;
using System.Globalization;
using FlaxEngine;
using PCG;

internal static class Program
{
    private const float Tolerance = 1e-6f;
    private static int _fail;
    private static int _pass;

    private static int Main()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        Console.WriteLine("== PCG golden-value probe ==");
        Console.WriteLine();

        // ---- Noise golden values (mirror of PCGNoiseGoldenTests.RunAll) ----
        Golden("Value2D(1.5,2.5,7)", PCGSeededNoise.Value2D(1.5f, 2.5f, 7), 0.37029177f);
        Golden("Value2D(-3.25,8.125,1234)", PCGSeededNoise.Value2D(-3.25f, 8.125f, 1234), 0.34668994f);
        Golden("Value3D(0.5,1.5,2.5,99)", PCGSeededNoise.Value3D(0.5f, 1.5f, 2.5f, 99), 0.5949658f);
        Golden("Perlin2D(3.25,-1.75,42)", PCGSeededNoise.Perlin2D(3.25f, -1.75f, 42), 0.46203953f);
        Golden("Perlin2D(0.1,0.2,0)", PCGSeededNoise.Perlin2D(0.1f, 0.2f, 0), 0.5752725f);
        Golden("Worley2D(2.3,4.7,11)", PCGSeededNoise.Worley2D(2.3f, 4.7f, 11), 0.5300377f);
        Golden("Worley3D(0.3,0.7,1.1,9)", PCGSeededNoise.Worley3D(0.3f, 0.7f, 1.1f, 9), 0.5604521f);
        Golden("Fbm2D(1.5,2.5,7,4)", PCGSeededNoise.Fbm2D(1.5f, 2.5f, 7, 4), 0.6414214f);
        Golden("Fbm3D(1.5,2.5,0.5,7,4)", PCGSeededNoise.Fbm3D(1.5f, 2.5f, 0.5f, 7, 4), 0.49215692f);
        Golden("HashToUnit(3,5,1)", PCGSeededNoise.HashToUnit(3, 5, 1), 0.34370285f);

        var rng = new PCGDeterministicRng(1234, 5, 42);
        Golden("RNG(1234,5,42) f1", rng.NextFloat(), 0.35229284f);
        Golden("RNG(1234,5,42) f2", rng.NextFloat(), 0.5995875f);
        Golden("RNG(1234,5,42) f3", rng.NextFloat(), 0.35996407f);

        var rngZero = new PCGDeterministicRng(0, 0, 0);
        Golden("RNG(0,0,0) f1", rngZero.NextFloat(), 0.67721117f);
        Golden("RNG(0,0,0) f2", rngZero.NextFloat(), 0.7455286f);
        Golden("RNG(0,0,0) f3", rngZero.NextFloat(), 0.039548874f);

        Console.WriteLine();
        Console.WriteLine("-- invariants --");

        var kinds = new[]
        {
            PCGNoiseKind.Value2D, PCGNoiseKind.Perlin2D, PCGNoiseKind.Worley2D, PCGNoiseKind.Fbm2D,
            PCGNoiseKind.Value3D, PCGNoiseKind.Worley3D, PCGNoiseKind.Fbm3D,
        };

        bool rangeOk = true;
        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0; i < 4000; i++)
        {
            float x = (i % 97) * 0.137f - 6.0f;
            float y = (i / 97) * 0.211f - 4.0f;
            for (int k = 0; k < kinds.Length; k++)
            {
                float v2 = PCGSeededNoise.Sample2D(kinds[k], x, y, i * 7 + k);
                float v3 = PCGSeededNoise.Sample3D(kinds[k], x, y, x * 0.5f, i * 7 + k);
                if (!IsUnit(v2) || !IsUnit(v3))
                    rangeOk = false;
                if (v2 < lo) lo = v2;
                if (v2 > hi) hi = v2;
            }
        }
        Check("kinds stay finite and within [0,1]", rangeOk);
        Check("kinds span most of [0,1]", hi > 0.9f && lo < 0.1f, $"min={lo:F6} max={hi:F6}");

        Check(
            "lattice hash is seed-sensitive",
            PCGSeededNoise.HashToUnit(3, 5, 1) != PCGSeededNoise.HashToUnit(3, 5, 2));

        float maxJump = 0f;
        for (int i = 0; i < 4000; i++)
        {
            float x = i * 0.01f;
            float jump = Math.Abs(
                PCGSeededNoise.Perlin2D(x, 0.37f, 5) - PCGSeededNoise.Perlin2D(x + 0.001f, 0.37f, 5));
            if (jump > maxJump) maxJump = jump;
        }
        Check("Perlin2D is continuous across lattice cells", maxJump < 0.02f, $"maxJump={maxJump:F6}");

        // The invariant that makes incremental execution sound.
        const int Count = 512;
        var full = new float[Count];
        for (int i = 0; i < Count; i++)
            full[i] = new PCGDeterministicRng(777, 3, i).NextRange(-5.0f, 5.0f);

        int mismatches = 0;
        for (int i = 0; i < Count; i++)
        {
            if (new PCGDeterministicRng(777, 3, i).NextRange(-5.0f, 5.0f) != full[i])
                mismatches++;
        }
        Check("isolated per-point recompute equals full-run value", mismatches == 0, $"mismatches={mismatches}");

        Check(
            "distinct streams per node id",
            new PCGDeterministicRng(1234, 5, 0).NextFloat() != new PCGDeterministicRng(1234, 6, 0).NextFloat());

        const int Samples = 100000;
        double mean = 0.0;
        for (int i = 0; i < Samples; i++)
            mean += new PCGDeterministicRng(99, 1, i).NextFloat();
        mean /= Samples;
        Check("RNG mean is near 0.5", Math.Abs((float)mean - 0.5f) < 0.01f, $"mean={mean:F6}");

        Console.WriteLine();
        Console.WriteLine($"== {_pass} passed, {_fail} failed ==");
        return _fail == 0 ? 0 : 1;
    }

    private static bool IsUnit(float v)
    {
        return !float.IsNaN(v) && !float.IsInfinity(v) && v >= 0.0f && v <= 1.0f;
    }

    private static void Golden(string what, float actual, float expected)
    {
        float delta = Math.Abs(actual - expected);
        if (delta <= Tolerance)
        {
            _pass++;
            Console.WriteLine($"  PASS {what} = {actual:R}");
        }
        else
        {
            _fail++;
            Console.WriteLine($"  FAIL {what}: got {actual:R}, expected {expected:R} (delta {delta:R})");
        }
    }

    private static void Check(string what, bool ok, string detail = null)
    {
        if (ok)
        {
            _pass++;
            Console.WriteLine($"  PASS {what}{(detail != null ? "   " + detail : string.Empty)}");
        }
        else
        {
            _fail++;
            Console.WriteLine($"  FAIL {what}{(detail != null ? "   " + detail : string.Empty)}");
        }
    }
}
