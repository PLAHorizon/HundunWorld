using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEngine;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// 只读：把一张 Texture 资产从 GPU 下载到 CPU，输出每通道统计与一张粗略网格，
    /// 用来离线判断“掩码是不是空的 / 哪一块有内容”。源 PNG 已丢失时，这是唯一能拿到像素的路径。
    /// </summary>
    public static class TextureStatsTool
    {
        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "texture_stats",
                Description = "下载 Texture 像素并返回通道统计 + 网格均值（只读）。用于判断掩码是否为空、内容分布在哪个 UV 区域。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" +
                                  "\"path\":{\"type\":\"string\"}," +
                                  "\"grid\":{\"type\":\"integer\",\"description\":\"网格边长，默认 16\"}," +
                                  "\"channel\":{\"type\":\"string\",\"description\":\"r|g|b|a|luma，默认 luma\"}," +
                                  "\"outPath\":{\"type\":\"string\",\"description\":\"压缩格式时把 mip0 原始字节写到这个路径\"}}," +
                                  "\"required\":[\"path\"]}",
                Execute = ExecuteAsync
            });
        }

        private static Task<object> ExecuteAsync(JsonElement args)
        {
            string path = EditorUtils.GetString(args, "path");
            int grid = Math.Clamp(EditorUtils.GetInt(args, "grid", 16), 2, 64);
            string channel = (EditorUtils.GetString(args, "channel") ?? "luma").ToLowerInvariant();

            return MainThread.InvokeAsync<object>(() =>
            {
                Texture texture = Content.Load<Texture>(path, 15000.0);
                if (texture == null || !texture.IsLoaded)
                {
                    throw new ArgumentException("贴图加载失败: " + path);
                }

                string outPath = EditorUtils.GetString(args, "outPath");
                // Texture 继承 TextureBase：像素直接从 CPU 侧取（压缩格式若不是 basic format 会返回失败）。
                bool failed = texture.GetPixels(out Color32[] pixels, 0, 0);
                if (failed || pixels == null)
                {
                    // 压缩格式：把 mip0 的原始字节落盘，交给离线解码器（BC1/BC5 等）。
                    byte[] raw = texture.GetMipData(0, out int rowPitch, out int slicePitch);
                    if (raw == null || raw.Length == 0)
                    {
                        throw new InvalidOperationException("GetPixels 与 GetMipData 均失败（format=" + texture.Format + "）: " + path);
                    }
                    if (string.IsNullOrEmpty(outPath))
                    {
                        outPath = System.IO.Path.Combine(
                            System.IO.Directory.GetCurrentDirectory(), "Tools", "_texdump.bin");
                    }
                    System.IO.File.WriteAllBytes(outPath, raw);
                    return new
                    {
                        path,
                        width = texture.Width,
                        height = texture.Height,
                        format = texture.Format.ToString(),
                        dumped = true,
                        outPath,
                        bytes = raw.Length,
                        rowPitch,
                        slicePitch
                    };
                }

                int w = texture.Width;
                int h = texture.Height;
                long n = Math.Min((long)w * h, pixels.Length);

                int[] sum = new int[4];
                int[] min = { 255, 255, 255, 255 };
                int[] max = new int[4];
                long above = 0;
                const int threshold = 32;

                for (long i = 0; i < n; i++)
                {
                    Color32 c = pixels[i];
                    int[] v = { c.R, c.G, c.B, c.A };
                    int vc = channel switch
                    {
                        "r" => c.R,
                        "g" => c.G,
                        "b" => c.B,
                        "a" => c.A,
                        _ => (c.R * 299 + c.G * 587 + c.B * 114) / 1000
                    };
                    if (vc > threshold)
                    {
                        above++;
                    }
                    for (int k = 0; k < 4; k++)
                    {
                        sum[k] += v[k];
                        if (v[k] < min[k]) min[k] = v[k];
                        if (v[k] > max[k]) max[k] = v[k];
                    }
                }

                var cells = new List<int>(grid * grid);
                for (int gy = 0; gy < grid; gy++)
                {
                    for (int gx = 0; gx < grid; gx++)
                    {
                        int x0 = (int)((long)gx * w / grid);
                        int x1 = (int)((long)(gx + 1) * w / grid);
                        int y0 = (int)((long)gy * h / grid);
                        int y1 = (int)((long)(gy + 1) * h / grid);
                        long acc = 0;
                        long cnt = 0;
                        for (int y = y0; y < y1; y++)
                        {
                            for (int x = x0; x < x1; x++)
                            {
                                Color32 c = pixels[(long)y * w + x];
                                int vc = channel switch
                                {
                                    "r" => c.R,
                                    "g" => c.G,
                                    "b" => c.B,
                                    "a" => c.A,
                                    _ => (c.R * 299 + c.G * 587 + c.B * 114) / 1000
                                };
                                acc += vc;
                                cnt++;
                            }
                        }
                        cells.Add(cnt > 0 ? (int)(acc / cnt) : 0);
                    }
                }

                double denom = Math.Max(1, n);
                return new
                {
                    path,
                    width = w,
                    height = h,
                    format = texture.Format.ToString(),
                    channel,
                    mean = new[]
                    {
                        Math.Round(sum[0] / denom, 1),
                        Math.Round(sum[1] / denom, 1),
                        Math.Round(sum[2] / denom, 1),
                        Math.Round(sum[3] / denom, 1)
                    },
                    min,
                    max,
                    coverage = Math.Round(above / denom, 4),
                    grid,
                    cells
                };
            });
        }
    }
}
