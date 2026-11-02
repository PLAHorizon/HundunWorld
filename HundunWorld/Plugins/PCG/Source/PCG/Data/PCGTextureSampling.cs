// PCG Plugin - Texture sampling core (no engine state)
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.22.

using System;
using FlaxEngine;

namespace PCG
{
    /// <summary>Which channel of a texture is read.</summary>
    public enum PCGTextureChannel
    {
        Red = 0,
        Green = 1,
        Blue = 2,
        Alpha = 3,

        /// <summary>Rec.601 luma, so a colour mask reads as one number.</summary>
        Luminance = 4,
    }

    /// <summary>What happens to coordinates outside the image.</summary>
    public enum PCGTextureAddress
    {
        /// <summary>Edge texels stretch outwards.</summary>
        Clamp = 0,

        /// <summary>The image tiles.</summary>
        Repeat = 1,

        /// <summary>The image tiles, flipping on every other copy, so the seams meet.</summary>
        Mirror = 2,
    }

    /// <summary>How a sample between texels is resolved.</summary>
    public enum PCGTextureFilter
    {
        Nearest = 0,
        Bilinear = 1,
    }

    /// <summary>
    /// Turns a texture's pixels into one number per point - the arithmetic half of the texture sampler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Split out from the node on purpose, exactly as <c>PCGMeshSurfaceSampling</c> was: reading a texture needs
    /// the engine, but everything worth getting wrong - the texel-centre convention, the three address modes,
    /// the two filters - is pure arithmetic over a float array, so it is tested offline against hand-worked
    /// answers and the node is left as an adapter.
    /// </para>
    /// <para>
    /// Texel <c>i</c> is treated as living at <c>i + 0.5</c>, so <c>u = (i + 0.5) / size</c> lands exactly on it.
    /// Getting that half-texel offset wrong is the classic "everything is shifted by one pixel" bug, and it is
    /// the reason the tests here sample on and between texel centres rather than only at u = 0 and u = 1.
    /// </para>
    /// </remarks>
    public static class PCGTextureSampling
    {
        /// <summary>Largest pixel count accepted, matching the engine's 8192 x 8192 ceiling with headroom.</summary>
        public const int MaxPixels = 16_777_216;

        /// <summary>
        /// Maps a world position onto texture coordinates for the ground-projected convention: one full copy of
        /// the image spans <paramref name="tileSize"/> world units in X and Z, centred on the origin, with the
        /// image's first row towards negative Z.
        /// </summary>
        /// <remarks>
        /// Pure and asserted rather than left inline in the node, because this mapping is the whole contract
        /// between a painted image and the world - and the node that uses it cannot be exercised offline.
        /// </remarks>
        public static Vector2 UvFromWorld(in Vector3 position, float tileSize)
        {
            // A zero or negative tile would divide the world away; the floor keeps the result finite and is the
            // same guard the node applies to its own setting.
            float tile = tileSize > 1e-3f ? tileSize : 1e-3f;

            return new Vector2(0.5f + position.X / tile, 0.5f + position.Z / tile);
        }

        /// <summary>Pulls one channel out of a texture read, normalised to [0,1].</summary>
        public static float[] ExtractChannel(Color32[] pixels, PCGTextureChannel channel)
        {
            if (pixels == null)
                return Array.Empty<float>();

            var result = new float[pixels.Length];

            for (int i = 0; i < pixels.Length; i++)
            {
                var c = pixels[i];

                result[i] = channel switch
                {
                    PCGTextureChannel.Red => c.R / 255f,
                    PCGTextureChannel.Green => c.G / 255f,
                    PCGTextureChannel.Blue => c.B / 255f,
                    PCGTextureChannel.Alpha => c.A / 255f,
                    _ => (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f,
                };
            }

            return result;
        }

        /// <summary>Folds an out-of-range index back into <c>[0, n)</c> according to the address mode.</summary>
        public static int Wrap(int index, int n, PCGTextureAddress address)
        {
            if (n <= 1)
                return 0;

            switch (address)
            {
            case PCGTextureAddress.Repeat:
            {
                int r = index % n;
                return r < 0 ? r + n : r;
            }
            case PCGTextureAddress.Mirror:
            {
                int period = 2 * n;
                int m = index % period;
                if (m < 0)
                    m += period;

                return m < n ? m : period - 1 - m;
            }
            default:
                return index < 0 ? 0 : (index >= n ? n - 1 : index);
            }
        }

        /// <summary>
        /// Samples <paramref name="grid"/> at normalised coordinates, returning a value in [0,1].
        /// </summary>
        /// <param name="grid">Row-major channel values, row length <paramref name="width"/>.</param>
        /// <param name="width">Image width in texels.</param>
        /// <param name="height">Image height in texels.</param>
        /// <param name="u">Horizontal coordinate; 0 is the left edge of the image, 1 the right edge.</param>
        /// <param name="v">Vertical coordinate; 0 is the first row, 1 the last. Row order is the image's own.</param>
        /// <param name="address">How coordinates outside the image are folded back in.</param>
        /// <param name="filter">Nearest texel, or a bilinear blend of four.</param>
        public static float Sample(
            float[] grid, int width, int height, float u, float v, PCGTextureAddress address, PCGTextureFilter filter)
        {
            if (grid == null || width <= 0 || height <= 0 || grid.Length < width * height)
                return 0.0f;

            // Texel i lives at i + 0.5, so this is the coordinate in texel space, not in [0,1].
            float fx = u * width - 0.5f;
            float fy = v * height - 0.5f;

            if (filter == PCGTextureFilter.Nearest)
            {
                // Floor(x + 0.5) rather than a round(): the framework's round() is banker's rounding, which
                // would make a coordinate exactly between two texels pick the even one - a tie-break nobody wants
                // and one that differs between platforms.
                int x = Wrap((int)MathF.Floor(fx + 0.5f), width, address);
                int y = Wrap((int)MathF.Floor(fy + 0.5f), height, address);

                return grid[y * width + x];
            }

            int x0 = (int)MathF.Floor(fx);
            int y0 = (int)MathF.Floor(fy);
            float tx = fx - x0;
            float ty = fy - y0;

            int xa = Wrap(x0, width, address);
            int xb = Wrap(x0 + 1, width, address);
            int ya = Wrap(y0, height, address);
            int yb = Wrap(y0 + 1, height, address);

            float top = grid[ya * width + xa] + (grid[ya * width + xb] - grid[ya * width + xa]) * tx;
            float bottom = grid[yb * width + xa] + (grid[yb * width + xb] - grid[yb * width + xa]) * tx;

            return top + (bottom - top) * ty;
        }
    }
}
