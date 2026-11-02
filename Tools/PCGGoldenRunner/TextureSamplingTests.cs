// PCG Plugin - Executable tests for the texture sampling core.
//
// The sampler node needs the engine to fetch pixels; this file covers the part that does not - the texel-centre
// convention, the three address modes and the two filters - against hand-worked answers rather than against a
// second copy of the same arithmetic.

using System;
using FlaxEngine;
using PCG;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class TextureSamplingTests
    {
        private static CheckDelegate _check;

        // Condition first, so an assertion reads as "this holds, because ...".
        private static void check(bool ok, string what, string detail = null) => _check(what, ok, detail);

        internal static void Run(CheckDelegate callback)
        {
            _check = callback;

            ChannelTests();
            NearestTests();
            BilinearTests();
            AddressTests();
            WorldMappingTests();
            GuardTests();
        }

        // ---- channels --------------------------------------------------------

        private static void ChannelTests()
        {
            var mixed = new[] { new Color32(255, 128, 0, 64) };

            check(Near(PCGTextureSampling.ExtractChannel(mixed, PCGTextureChannel.Red)[0], 1.0f) &&
                  Near(PCGTextureSampling.ExtractChannel(mixed, PCGTextureChannel.Green)[0], 128f / 255f) &&
                  Near(PCGTextureSampling.ExtractChannel(mixed, PCGTextureChannel.Blue)[0], 0.0f) &&
                  Near(PCGTextureSampling.ExtractChannel(mixed, PCGTextureChannel.Alpha)[0], 64f / 255f),
                "each channel is normalised from its own byte");

            // Equal RGB means the weights sum to that value - a property that holds for any byte, so it needs no
            // hand-computed constant.
            var grey = new[] { new Color32(128, 128, 128, 255) };
            check(Near(PCGTextureSampling.ExtractChannel(grey, PCGTextureChannel.Luminance)[0], 128f / 255f),
                "luminance of an equal-RGB colour is that colour's own value",
                PCGTextureSampling.ExtractChannel(grey, PCGTextureChannel.Luminance)[0].ToString("F6"));

            var red = new[] { new Color32(255, 0, 0, 255) };
            check(Near(PCGTextureSampling.ExtractChannel(red, PCGTextureChannel.Luminance)[0], 0.299f),
                "luminance weights the channels unequally rather than averaging them",
                PCGTextureSampling.ExtractChannel(red, PCGTextureChannel.Luminance)[0].ToString("F6"));
        }

        // ---- nearest ---------------------------------------------------------

        private static void NearestTests()
        {
            var two = TwoWide();

            check(Near(Sample(two, 2, 1, 0.25f, 0.5f, Clamp, Nearest), 0.0f),
                "u at the first texel's centre reads that texel");
            check(Near(Sample(two, 2, 1, 0.75f, 0.5f, Clamp, Nearest), 1.0f),
                "u at the second texel's centre reads that texel");
            check(Near(Sample(two, 2, 1, 0.0f, 0.5f, Clamp, Nearest), 0.0f),
                "the left edge reads the first texel");
            check(Near(Sample(two, 2, 1, 1.0f, 0.5f, Clamp, Nearest), 1.0f),
                "the right edge reads the last texel");

            // Exactly on the boundary either texel is defensible, so the choice is pinned rather than claimed.
            check(Near(Sample(two, 2, 1, 0.5f, 0.5f, Clamp, Nearest), 1.0f),
                "a coordinate exactly between two texels resolves to the upper one (pinned so it cannot drift)",
                Sample(two, 2, 1, 0.5f, 0.5f, Clamp, Nearest).ToString("F6"));
        }

        // ---- bilinear --------------------------------------------------------

        private static void BilinearTests()
        {
            var two = TwoWide();

            check(Near(Sample(two, 2, 1, 0.5f, 0.5f, Clamp, Bilinear), 0.5f),
                "bilinear midway between black and white reads half",
                Sample(two, 2, 1, 0.5f, 0.5f, Clamp, Bilinear).ToString("F6"));
            check(Near(Sample(two, 2, 1, 0.25f, 0.5f, Clamp, Bilinear), 0.0f),
                "bilinear sitting exactly on a texel centre returns that texel");

            var checker = PCGTextureSampling.ExtractChannel(new[]
            {
                new Color32(0, 0, 0, 255), new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255), new Color32(0, 0, 0, 255),
            }, PCGTextureChannel.Red);

            check(Near(Sample(checker, 2, 2, 0.5f, 0.5f, Clamp, Bilinear), 0.5f),
                "the centre of a symmetric checkerboard averages to half",
                Sample(checker, 2, 2, 0.5f, 0.5f, Clamp, Bilinear).ToString("F6"));
        }

        // ---- address modes ---------------------------------------------------

        private static void AddressTests()
        {
            // 0, 128/255, 1 along one row.
            var three = PCGTextureSampling.ExtractChannel(new[]
            {
                new Color32(0, 0, 0, 255),
                new Color32(128, 128, 128, 255),
                new Color32(255, 255, 255, 255),
            }, PCGTextureChannel.Red);

            float mid = 128f / 255f;

            // u = 1.8333 puts the coordinate one whole image to the right, on the third texel of the next copy.
            float beyond = (5.0f + 0.5f) / 3.0f;

            check(Near(Sample(three, 3, 1, beyond, 0.5f, Repeat, Nearest), 1.0f),
                "repeat wraps a coordinate one image over and lands on the matching texel");
            check(Near(Sample(three, 3, 1, beyond, 0.5f, Mirror, Nearest), 0.0f),
                "mirror flips the same coordinate to the opposite edge, which is what makes tiled seams meet");
            check(Near(Sample(three, 3, 1, beyond, 0.5f, Clamp, Nearest), 1.0f),
                "clamp sticks to the last texel instead of wrapping");

            check(Near(Sample(three, 3, 1, -0.5f, 0.5f, Clamp, Nearest), 0.0f),
                "clamp sticks to the first texel below zero");
            check(Near(Sample(three, 3, 1, -0.5f, 0.5f, Repeat, Nearest), mid),
                "repeat wraps a negative coordinate forward into the image",
                Sample(three, 3, 1, -0.5f, 0.5f, Repeat, Nearest).ToString("F6"));

            // Inside the image every mode agrees, so a mode switch cannot quietly reshuffle the middle.
            check(Near(Sample(three, 3, 1, 0.5f, 0.5f, Repeat, Nearest),
                       Sample(three, 3, 1, 0.5f, 0.5f, Mirror, Nearest)) &&
                  Near(Sample(three, 3, 1, 0.5f, 0.5f, Clamp, Nearest), mid),
                "inside the image all three address modes agree");
        }

        // ---- world to UV -----------------------------------------------------

        private static void WorldMappingTests()
        {
            const float tile = 1000.0f;

            var origin = PCGTextureSampling.UvFromWorld(Vector3.Zero, tile);
            check(Near(origin.X, 0.5f) && Near(origin.Y, 0.5f),
                "the world origin sits at the middle of the image", origin.ToString());

            var right = PCGTextureSampling.UvFromWorld(new Vector3(tile * 0.5f, 0.0f, 0.0f), tile);
            check(Near(right.X, 1.0f) && Near(right.Y, 0.5f),
                "half a tile along X reaches the image's right edge", right.ToString());

            var alongZ = PCGTextureSampling.UvFromWorld(new Vector3(0.0f, 0.0f, tile * 0.5f), tile);
            check(Near(alongZ.X, 0.5f) && Near(alongZ.Y, 1.0f),
                "world Z drives V, so the image lies flat on the ground", alongZ.ToString());

            var oneOver = PCGTextureSampling.UvFromWorld(new Vector3(tile, 0.0f, 0.0f), tile);
            check(Near(oneOver.X, 1.5f),
                "one whole tile along X is one whole image over, which is what makes Repeat tile cleanly",
                oneOver.ToString());

            // A zero tile would divide the world away; the result has to stay finite.
            var degenerate = PCGTextureSampling.UvFromWorld(new Vector3(1.0f, 0.0f, 0.0f), 0.0f);
            check(!float.IsNaN(degenerate.X) && !float.IsInfinity(degenerate.X) &&
                  !float.IsNaN(degenerate.Y) && !float.IsInfinity(degenerate.Y),
                "a zero tile size is guarded rather than producing a non-finite coordinate",
                degenerate.ToString());
        }

        // ---- degenerate inputs -----------------------------------------------

        private static void GuardTests()
        {
            var two = TwoWide();

            check(PCGTextureSampling.Sample(null, 2, 1, 0.5f, 0.5f, Clamp, Bilinear) == 0.0f,
                "a missing grid samples as zero");
            check(PCGTextureSampling.Sample(two, 0, 1, 0.5f, 0.5f, Clamp, Bilinear) == 0.0f,
                "a zero-width image samples as zero");
            check(PCGTextureSampling.Sample(new float[1], 2, 1, 0.5f, 0.5f, Clamp, Bilinear) == 0.0f,
                "a grid shorter than width*height samples as zero rather than reading past the end");
            check(PCGTextureSampling.Wrap(7, 1, PCGTextureAddress.Repeat) == 0,
                "a single-texel axis always folds to that texel");
            check(PCGTextureSampling.ExtractChannel(null, PCGTextureChannel.Red).Length == 0,
                "no pixels extracts to an empty grid");
        }

        // ---- helpers ---------------------------------------------------------

        private static float[] TwoWide() => PCGTextureSampling.ExtractChannel(new[]
        {
            new Color32(0, 0, 0, 255), new Color32(255, 255, 255, 255),
        }, PCGTextureChannel.Red);

        private static float Sample(
            float[] grid, int width, int height, float u, float v, PCGTextureAddress address, PCGTextureFilter filter) =>
            PCGTextureSampling.Sample(grid, width, height, u, v, address, filter);

        private static bool Near(float a, float b) => MathF.Abs(a - b) < 1e-4f;

        private const PCGTextureAddress Clamp = PCGTextureAddress.Clamp;
        private const PCGTextureAddress Repeat = PCGTextureAddress.Repeat;
        private const PCGTextureAddress Mirror = PCGTextureAddress.Mirror;
        private const PCGTextureFilter Nearest = PCGTextureFilter.Nearest;
        private const PCGTextureFilter Bilinear = PCGTextureFilter.Bilinear;
    }
}
