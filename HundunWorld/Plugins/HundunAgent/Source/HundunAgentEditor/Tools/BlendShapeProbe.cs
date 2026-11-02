using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEngine;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// 读/写运行中 AnimatedModel 的 blendshape 权重（只用于取证）。
    /// <para>
    /// 为什么需要它：捏脸面板的 morph 滑杆是虚拟化列表，某些形状（如 jawOpen）不在当前
    /// 已实例化的行里，UI 自动化点不到；而张口是检查牙齿/唾液材质的唯一办法。
    /// </para>
    /// </summary>
    public static class BlendShapeProbe
    {
        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "model_blendshape",
                Description = "读/写 AnimatedModel 的 blendshape 权重。actor 传名称或 Guid；value 省略=只读；"
                    + "name 省略=按 filter 列出形状名与当前权重。只改运行态，不写资产。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{"
                    + "\"actor\":{\"type\":\"string\"},\"name\":{\"type\":\"string\"},"
                    + "\"value\":{\"type\":\"number\"},\"filter\":{\"type\":\"string\"},"
                    + "\"max\":{\"type\":\"integer\"}},\"required\":[\"actor\"]}",
                Execute = ExecuteAsync
            });
        }

        private static Task<object> ExecuteAsync(JsonElement args)
        {
            string actorArg = EditorUtils.GetString(args, "actor");
            string name = EditorUtils.GetString(args, "name");
            string filter = EditorUtils.GetString(args, "filter");
            int max = Math.Clamp(EditorUtils.GetInt(args, "max", 60), 1, 500);
            float? value = args.TryGetProperty("value", out JsonElement v) && v.ValueKind == JsonValueKind.Number
                ? (float)v.GetDouble()
                : (float?)null;

            return MainThread.InvokeAsync<object>(() =>
            {
                AnimatedModel model = FindModel(actorArg);
                if (model == null)
                {
                    throw new ArgumentException("找不到 AnimatedModel: " + actorArg);
                }
                SkinnedModel skin = model.SkinnedModel;
                if (skin == null || !skin.IsLoaded)
                {
                    throw new ArgumentException("AnimatedModel 没有已加载的 SkinnedModel: " + model.Name);
                }

                if (string.IsNullOrEmpty(name))
                {
                    var rows = new List<object>();
                    string f = (filter ?? string.Empty).ToLowerInvariant();
                    int total = skin.BlendShapes != null ? skin.BlendShapes.Length : 0;
                    for (int i = 0; i < total; i++)
                    {
                        string n = skin.BlendShapes[i];
                        if (f.Length > 0 && n.ToLowerInvariant().IndexOf(f, StringComparison.Ordinal) < 0)
                        {
                            continue;
                        }
                        rows.Add(new { name = n, weight = model.GetBlendShapeWeight(n) });
                        if (rows.Count >= max)
                        {
                            break;
                        }
                    }
                    return new { actor = model.Name, total, listed = rows.Count, rows };
                }

                float before = model.GetBlendShapeWeight(name);
                if (!value.HasValue)
                {
                    return new { actor = model.Name, name, before };
                }
                model.SetBlendShapeWeight(name, value.Value);
                float after = model.GetBlendShapeWeight(name);
                return new { actor = model.Name, name, before, after, applied = true };
            });
        }

        private static AnimatedModel FindModel(string actorArg)
        {
            if (string.IsNullOrEmpty(actorArg))
            {
                return null;
            }
            Actor actor = EditorUtils.FindActor(actorArg);
            if (actor == null)
            {
                return null;
            }
            if (actor is AnimatedModel model)
            {
                return model;
            }
            return actor.FindActor<AnimatedModel>();
        }
    }
}
