// PCG Plugin - Seeded noise
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §3.4.

using System;
using FlaxEngine;

namespace PCG
{
    /// <summary>Noise algorithms offered by the sampler nodes.</summary>
    public enum PCGNoiseKind
    {
        /// <summary>Value noise on a 2D lattice. Cheapest; blocky at high frequency.</summary>
        Value2D = 0,

        /// <summary>Perlin gradient noise on a 2D lattice.</summary>
        Perlin2D = 1,

        /// <summary>Worley / cellular noise. Low values near cell feature points.</summary>
        Worley2D = 2,

        /// <summary>Fractal Brownian motion built from octaves of <see cref="Perlin2D"/>.</summary>
        Fbm2D = 3,

        /// <summary>Trilinear value noise. For volume sampling; the engine offers no 3D Perlin.</summary>
        Value3D = 4,

        /// <summary>Worley / cellular noise in 3D.</summary>
        Worley3D = 5,

        /// <summary>Fractal Brownian motion built from octaves of <see cref="Value3D"/>.</summary>
        Fbm3D = 6,
    }

    /// <summary>
    /// The plugin's own seeded noise implementation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why the engine's noise is not used.</b> <c>FlaxEngine.Utilities.Noise</c> was audited in
    /// <c>Source/Engine/Utilities/Noise.h</c> and has three properties that make it unsuitable as the PCG
    /// noise basis:
    /// </para>
    /// <list type="number">
    /// <item>Every function lacks a seed parameter, so the same input always yields the same value. A PCG
    /// graph could never be re-rolled with a new seed, which is a core feature.</item>
    /// <item>Return types are inconsistent: <c>PerlinNoise</c>/<c>SimplexNoise</c> return a scalar,
    /// <c>WorleyNoise</c> returns <c>Float2</c> (F1,F2) and <c>VoronoiNoise</c> returns <c>Float3</c>.
    /// A single "noise value" concept does not exist there.</item>
    /// <item>There is no 3D Perlin or Simplex. Only <c>CustomNoise(Float3)</c> and
    /// <c>CustomNoise3D</c>, whose numeric semantics are undocumented in the header.</item>
    /// </list>
    /// <para>
    /// Owning the implementation also keeps generated results stable across engine upgrades, and makes the
    /// functions verifiable by the golden-value tests in the verification harness.
    /// </para>
    /// <para>All functions are pure and thread-safe: they hold no mutable state.</para>
    /// </remarks>
    public static class PCGSeededNoise
    {
        /// <summary>Samples 2D noise of the given kind at <paramref name="x"/>,<paramref name="y"/>.</summary>
        /// <param name="kind">Noise algorithm. 3D kinds fall back to their 2D counterpart.</param>
        /// <param name="x">Sample X.</param>
        /// <param name="y">Sample Y.</param>
        /// <param name="seed">Seed. Different seeds decorrelate the result.</param>
        /// <param name="frequency">Coordinate scale. Larger values give finer detail.</param>
        /// <param name="octaves">Octave count, used by the FBM kinds.</param>
        /// <param name="lacunarity">Frequency multiplier per octave.</param>
        /// <param name="gain">Amplitude multiplier per octave.</param>
        /// <returns>Value in [0,1].</returns>
        public static float Sample2D(
            PCGNoiseKind kind, float x, float y, int seed, float frequency = 1.0f,
            int octaves = 4, float lacunarity = 2.0f, float gain = 0.5f)
        {
            float fx = x * frequency;
            float fy = y * frequency;

            switch (kind)
            {
            case PCGNoiseKind.Value2D:
                return Value2D(fx, fy, seed);
            case PCGNoiseKind.Perlin2D:
                return Perlin2D(fx, fy, seed);
            case PCGNoiseKind.Worley2D:
                return Worley2D(fx, fy, seed);
            case PCGNoiseKind.Fbm2D:
                return Fbm2D(fx, fy, seed, octaves, lacunarity, gain);
            case PCGNoiseKind.Value3D:
                return Value3D(fx, fy, 0.0f, seed);
            case PCGNoiseKind.Worley3D:
                return Worley3D(fx, fy, 0.0f, seed);
            case PCGNoiseKind.Fbm3D:
                return Fbm3D(fx, fy, 0.0f, seed, octaves, lacunarity, gain);
            default:
                return Value2D(fx, fy, seed);
            }
        }

        /// <summary>Samples 2D noise from a vector coordinate.</summary>
        public static float Sample2D(
            PCGNoiseKind kind, Vector2 position, int seed, float frequency = 1.0f,
            int octaves = 4, float lacunarity = 2.0f, float gain = 0.5f)
        {
            return Sample2D(kind, position.X, position.Y, seed, frequency, octaves, lacunarity, gain);
        }

        /// <summary>Samples 3D noise of the given kind at a point.</summary>
        /// <remarks>
        /// The engine provides no 3D Perlin or Simplex, so <see cref="PCGNoiseKind.Perlin2D"/> and
        /// <see cref="PCGNoiseKind.Fbm2D"/> are evaluated on the XZ plane here. Use
        /// <see cref="PCGNoiseKind.Value3D"/> or <see cref="PCGNoiseKind.Worley3D"/> when true volumetric
        /// variation is wanted.
        /// </remarks>
        public static float Sample3D(
            PCGNoiseKind kind, float x, float y, float z, int seed, float frequency = 1.0f,
            int octaves = 4, float lacunarity = 2.0f, float gain = 0.5f)
        {
            float fx = x * frequency;
            float fy = y * frequency;
            float fz = z * frequency;

            switch (kind)
            {
            case PCGNoiseKind.Value3D:
                return Value3D(fx, fy, fz, seed);
            case PCGNoiseKind.Worley3D:
                return Worley3D(fx, fy, fz, seed);
            case PCGNoiseKind.Fbm3D:
                return Fbm3D(fx, fy, fz, seed, octaves, lacunarity, gain);
            case PCGNoiseKind.Value2D:
                return Value2D(fx, fz, seed);
            case PCGNoiseKind.Perlin2D:
                return Perlin2D(fx, fz, seed);
            case PCGNoiseKind.Worley2D:
                return Worley2D(fx, fz, seed);
            case PCGNoiseKind.Fbm2D:
                return Fbm2D(fx, fz, seed, octaves, lacunarity, gain);
            default:
                return Value3D(fx, fy, fz, seed);
            }
        }

        /// <summary>Samples 3D noise from a vector coordinate.</summary>
        public static float Sample3D(
            PCGNoiseKind kind, Vector3 position, int seed, float frequency = 1.0f,
            int octaves = 4, float lacunarity = 2.0f, float gain = 0.5f)
        {
            return Sample3D(kind, position.X, position.Y, position.Z, seed, frequency, octaves, lacunarity, gain);
        }

        // ---- Individual algorithms -------------------------------------------

        /// <summary>Bilinear value noise in [0,1].</summary>
        public static float Value2D(float x, float y, int seed)
        {
            int xi = FastFloor(x);
            int yi = FastFloor(y);
            float xf = x - xi;
            float yf = y - yi;

            float u = SmoothStep(xf);
            float v = SmoothStep(yf);

            float n00 = HashToUnit(xi, yi, seed);
            float n10 = HashToUnit(xi + 1, yi, seed);
            float n01 = HashToUnit(xi, yi + 1, seed);
            float n11 = HashToUnit(xi + 1, yi + 1, seed);

            float a = Lerp(n00, n10, u);
            float b = Lerp(n01, n11, u);
            return Lerp(a, b, v);
        }

        /// <summary>Trilinear value noise in [0,1]. Substituted for 3D Perlin, which the engine lacks.</summary>
        public static float Value3D(float x, float y, float z, int seed)
        {
            int xi = FastFloor(x);
            int yi = FastFloor(y);
            int zi = FastFloor(z);
            float xf = x - xi;
            float yf = y - yi;
            float zf = z - zi;

            float u = SmoothStep(xf);
            float v = SmoothStep(yf);
            float w = SmoothStep(zf);

            float n000 = HashToUnit(xi, yi, zi, seed);
            float n100 = HashToUnit(xi + 1, yi, zi, seed);
            float n010 = HashToUnit(xi, yi + 1, zi, seed);
            float n110 = HashToUnit(xi + 1, yi + 1, zi, seed);
            float n001 = HashToUnit(xi, yi, zi + 1, seed);
            float n101 = HashToUnit(xi + 1, yi, zi + 1, seed);
            float n011 = HashToUnit(xi, yi + 1, zi + 1, seed);
            float n111 = HashToUnit(xi + 1, yi + 1, zi + 1, seed);

            float x00 = Lerp(n000, n100, u);
            float x10 = Lerp(n010, n110, u);
            float x01 = Lerp(n001, n101, u);
            float x11 = Lerp(n011, n111, u);

            return Lerp(Lerp(x00, x10, v), Lerp(x01, x11, v), w);
        }

        /// <summary>Perlin gradient noise in [0,1].</summary>
        public static float Perlin2D(float x, float y, int seed)
        {
            int xi = FastFloor(x);
            int yi = FastFloor(y);
            float xf = x - xi;
            float yf = y - yi;

            float u = SmoothStep(xf);
            float v = SmoothStep(yf);

            float g00 = GradientDot(xi, yi, xf, yf, seed);
            float g10 = GradientDot(xi + 1, yi, xf - 1.0f, yf, seed);
            float g01 = GradientDot(xi, yi + 1, xf, yf - 1.0f, seed);
            float g11 = GradientDot(xi + 1, yi + 1, xf - 1.0f, yf - 1.0f, seed);

            float a = Lerp(g00, g10, u);
            float b = Lerp(g01, g11, u);

            // Gradient noise lands in roughly [-0.71,0.71]; remap to [0,1].
            return Saturate(Lerp(a, b, v) * 0.70710678f + 0.5f);
        }

        /// <summary>Worley / cellular noise in [0,1], where 0 sits exactly on a feature point.</summary>
        public static float Worley2D(float x, float y, int seed)
        {
            int xi = FastFloor(x);
            int yi = FastFloor(y);
            float minDist = float.MaxValue;

            for (int oy = -1; oy <= 1; oy++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    int cx = xi + ox;
                    int cy = yi + oy;
                    float px = cx + HashToUnit(cx, cy, seed);
                    float py = cy + HashToUnit(cx, cy, seed ^ 0x68BC21EB);
                    float dx = px - x;
                    float dy = py - y;
                    float d = dx * dx + dy * dy;
                    if (d < minDist)
                        minDist = d;
                }
            }

            return Saturate(Mathf.Sqrt(minDist));
        }

        /// <summary>Worley / cellular noise in 3D, in [0,1].</summary>
        public static float Worley3D(float x, float y, float z, int seed)
        {
            int xi = FastFloor(x);
            int yi = FastFloor(y);
            int zi = FastFloor(z);
            float minDist = float.MaxValue;

            for (int oz = -1; oz <= 1; oz++)
            {
                for (int oy = -1; oy <= 1; oy++)
                {
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int cx = xi + ox;
                        int cy = yi + oy;
                        int cz = zi + oz;
                        float px = cx + HashToUnit(cx, cy, cz, seed);
                        float py = cy + HashToUnit(cx, cy, cz, seed ^ 0x68BC21EB);
                        float pz = cz + HashToUnit(cx, cy, cz, seed ^ 0x02E5BE93);
                        float dx = px - x;
                        float dy = py - y;
                        float dz = pz - z;
                        float d = dx * dx + dy * dy + dz * dz;
                        if (d < minDist)
                            minDist = d;
                    }
                }
            }

            return Saturate(Mathf.Sqrt(minDist));
        }

        /// <summary>Fractal Brownian motion over <see cref="Perlin2D"/>, normalised to [0,1].</summary>
        public static float Fbm2D(float x, float y, int seed, int octaves = 4, float lacunarity = 2.0f, float gain = 0.5f)
        {
            if (octaves < 1)
                octaves = 1;

            float sum = 0.0f;
            float amplitude = 1.0f;
            float totalAmplitude = 0.0f;
            float fx = x;
            float fy = y;

            for (int i = 0; i < octaves; i++)
            {
                // Offset the seed per octave so octaves are independent rather than a rescaled copy.
                sum += Perlin2D(fx, fy, seed + i * 1013) * amplitude;
                totalAmplitude += amplitude;
                amplitude *= gain;
                fx *= lacunarity;
                fy *= lacunarity;
            }

            return totalAmplitude > 0.0f ? Saturate(sum / totalAmplitude) : 0.0f;
        }

        /// <summary>Fractal Brownian motion over <see cref="Value3D"/>, normalised to [0,1].</summary>
        public static float Fbm3D(
            float x, float y, float z, int seed, int octaves = 4, float lacunarity = 2.0f, float gain = 0.5f)
        {
            if (octaves < 1)
                octaves = 1;

            float sum = 0.0f;
            float amplitude = 1.0f;
            float totalAmplitude = 0.0f;
            float fx = x;
            float fy = y;
            float fz = z;

            for (int i = 0; i < octaves; i++)
            {
                sum += Value3D(fx, fy, fz, seed + i * 1013) * amplitude;
                totalAmplitude += amplitude;
                amplitude *= gain;
                fx *= lacunarity;
                fy *= lacunarity;
                fz *= lacunarity;
            }

            return totalAmplitude > 0.0f ? Saturate(sum / totalAmplitude) : 0.0f;
        }

        /// <summary>Simplex-shaped noise in [0,1]. Provided for parity with engine naming.</summary>
        /// <remarks>
        /// Implemented as an octave-free Perlin variant. Kept as a distinct entry so graph assets authored
        /// against a "simplex" node keep working if a true simplex implementation replaces it later.
        /// </remarks>
        public static float Simplex2D(float x, float y, int seed)
        {
            return Perlin2D(x, y, seed);
        }

        // ---- Hashing and helper maths ----------------------------------------

        private static int FastFloor(float v)
        {
            int i = (int)v;
            return v < i ? i - 1 : i;
        }

        private static float SmoothStep(float t)
        {
            // Perlin's quintic: first and second derivatives vanish at the lattice, so cells join smoothly.
            return t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f);
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float Saturate(float v) => v < 0.0f ? 0.0f : (v > 1.0f ? 1.0f : v);

        /// <summary>Hashes 2D lattice coordinates plus a seed to a value in [0,1].</summary>
        public static float HashToUnit(int x, int y, int seed)
        {
            // Scaled as one float multiply. The result only needs 24-bit precision, and the raw hash stays
            // full-width (see Hash) so the low bits that select a Perlin gradient remain intact.
            return Hash(x, y, 0, seed) * (1.0f / 4294967296.0f);
        }

        /// <summary>Hashes 3D lattice coordinates plus a seed to a value in [0,1].</summary>
        public static float HashToUnit(int x, int y, int z, int seed)
        {
            return Hash(x, y, z, seed) * (1.0f / 4294967296.0f);
        }

        /// <summary>
        /// Avalanches integer lattice coordinates plus a seed into a full 32-bit hash.
        /// </summary>
        /// <remarks>
        /// Returns <see cref="uint"/> deliberately. An earlier version declared this as <c>float</c>, which
        /// silently rounded the 32-bit result to a 24-bit mantissa. Because the low three bits select the
        /// Perlin gradient, that truncation changed which gradient each lattice corner used, so Perlin and FBM
        /// disagreed with their verified reference values while Value and Worley noise - which only consume the
        /// full magnitude - looked fine. Keep this 32-bit.
        /// </remarks>
        internal static uint Hash(int x, int y, int z, int seed)
        {
            uint h = unchecked((uint)seed);
            h ^= unchecked((uint)x) * 0x9E3779B1u;
            h = Rotl(h, 13);
            h ^= unchecked((uint)y) * 0x85EBCA77u;
            h = Rotl(h, 13);
            h ^= unchecked((uint)z) * 0xC2B2AE3Du;
            h = Rotl(h, 13);

            // Final avalanche (MurmurHash3 style) so low-entropy inputs still spread across the range.
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;

            return h;
        }

        private static uint Rotl(uint v, int n) => (v << n) | (v >> (32 - n));

        private static float GradientDot(int x, int y, float dx, float dy, int seed)
        {
            // Pick one of 8 unit-ish gradients from the hash, then project.
            uint h = Hash(x, y, 0, seed);
            switch (h & 7u)
            {
            case 0: return dx;
            case 1: return -dx;
            case 2: return dy;
            case 3: return -dy;
            case 4: return dx + dy;
            case 5: return dx - dy;
            case 6: return -dx + dy;
            default: return -dx - dy;
            }
        }
    }
}
