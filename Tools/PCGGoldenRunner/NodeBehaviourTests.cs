// PCG Plugin - Executable behaviour tests for the Phase 2 node library and the attribute layer.
//
// Why this lives here: Tools/PCGVerify only compiles the plugin sources, and PCGSelfTest needs a running
// editor. Everything asserted below is pure managed maths over PCGData, so it can be executed headless -
// which is the only way these invariants get checked without a scene loaded.

using System;
using System.Collections.Generic;
using System.Text;
using FlaxEngine;
using PCG;
using PCG.Execution;
using PCG.Graph;
using PCG.Nodes;
using PCGEditor;

namespace PCGGoldenRunner
{
    internal static class NodeBehaviourTests
    {
        internal delegate void CheckDelegate(string what, bool ok, string detail = null);

        internal static void Run(CheckDelegate check)
        {
            AttributeChannelTests(check);
            DensityAndHeightFilterTests(check);
            AreaFilterTests(check);
            SlopeFilterTests(check);
            TransformPointsTests(check);
            RandomOffsetTests(check);
            MergePointsTests(check);
            SelfPruningTests(check);
            OverridePlanTests(check);
            SamplerTests(check);
            ParameterLayoutTests(check);
            CategoryIdStabilityTests(check);
            ParameterLabelTests(check);
            ParameterEditorKindTests(check);
            ComponentSerializationTests(check);
            CompileRegistersTypesTests(check);
            RawSpawnTripwireTests(check);
            ParameterSchemaTests(check);
        }

        // ---- helpers --------------------------------------------------------

        private static PCGNodeRegistry.PCGNodeSerializationData Data(string typeId, params object[] values)
        {
            return new PCGNodeRegistry.PCGNodeSerializationData(typeId, values, 1);
        }

        private static PCGExecutionContext Context(int seed = 12345)
        {
            return new PCGExecutionContext
            {
                Seed = seed,
                CurrentNodeId = 7,
                GenerationBounds = new BoundingBox(new Vector3(-1000, 0, -1000), new Vector3(1000, 200, 1000)),
            };
        }

        private static PCGData Points(Vector3[] positions, float density = 1.0f)
        {
            var data = PCGData.Create(positions.Length);
            for (int i = 0; i < positions.Length; i++)
            {
                var point = PCGPoint.Create(positions[i], density, i);
                point.UpdateBounds(new Vector3(1f, 1f, 1f));
                data.Points[i] = point;
            }

            return data;
        }

        private static PCGData Run(PCGNodeBase node, PCGData input)
        {
            return node.OnExecute(Context(), new[] { input });
        }

        private static float[] SlopeColumn(PCGData data)
        {
            return data?.SharedAttributes?.TryGetFloatArray(PCGAttributeSet.SlopeName);
        }

        private static bool Near(Vector3 a, Vector3 b, float tolerance = 0.01f)
        {
            return Math.Abs(a.X - b.X) <= tolerance &&
                   Math.Abs(a.Y - b.Y) <= tolerance &&
                   Math.Abs(a.Z - b.Z) <= tolerance;
        }

        private static Vector3[] Line(int count, float step)
        {
            var positions = new Vector3[count];
            for (int i = 0; i < count; i++)
                positions[i] = new Vector3(i * step, 0f, 0f);

            return positions;
        }

        // ---- tests ----------------------------------------------------------

        private static void AttributeChannelTests(CheckDelegate check)
        {
            // Regression guard for the built-in channel split. $Slope has no PCGPoint field, so it has to be a
            // real column; while every $-prefixed name was refused by the write validator, declaring it threw
            // and the Landscape Sampler failed on any run that actually produced points.
            bool writable = true;
            string failure = null;
            var set = new PCGAttributeSet(4);
            try
            {
                set.DeclareAttributeUnsafe(PCGAttributeSet.SlopeName, PCGAttributeType.Float);
                var column = set.GetWritableFloatArray(PCGAttributeSet.SlopeName);
                column[0] = 12.5f;
                column[3] = 31.25f;
            }
            catch (Exception ex)
            {
                writable = false;
                failure = ex.GetType().Name + ": " + ex.Message;
            }

            check("$Slope is declarable and writable as a stored channel", writable, failure);
            check(
                "$Slope reads back per row",
                writable && set.GetFloat(PCGAttributeSet.SlopeName, 0) == 12.5f &&
                set.GetFloat(PCGAttributeSet.SlopeName, 3) == 31.25f);

            // Virtual channels live on the point. They must be refused with a typed error rather than a raw
            // NullReferenceException out of a dictionary that never held them.
            foreach (var name in new[] { PCGAttributeSet.DensityName, PCGAttributeSet.PositionName })
            {
                string message = null;
                bool rejected = false;
                try
                {
                    if (name == PCGAttributeSet.DensityName)
                        set.GetFloat(name, 0);
                    else
                        set.GetVector3(name, 0);
                }
                catch (PCGAttributeException ex)
                {
                    rejected = true;
                    message = ex.Error.ToString();
                }
                catch (Exception ex)
                {
                    message = "untyped " + ex.GetType().Name;
                }

                check($"reading the virtual channel {name} throws a typed error", rejected, message);
            }

            check("Contains reports only column-backed names",
                set.Contains(PCGAttributeSet.SlopeName) && !set.Contains(PCGAttributeSet.DensityName));

            // The stored column must survive compaction, or a filter between the sampler and a spawner would
            // silently drop the slope the sampler paid a terrain query for.
            var gathered = set.Gather(new[] { 3, 1, 0 }, 3);
            check(
                "Gather carries the $Slope column",
                gathered.Contains(PCGAttributeSet.SlopeName) &&
                gathered.GetFloat(PCGAttributeSet.SlopeName, 0) == 31.25f &&
                gathered.GetFloat(PCGAttributeSet.SlopeName, 2) == 12.5f);

            var clone = set.Clone();
            clone.GetWritableFloatArray(PCGAttributeSet.SlopeName)[0] = -1f;
            check(
                "Clone isolates writes to the $Slope column",
                set.GetFloat(PCGAttributeSet.SlopeName, 0) == 12.5f &&
                clone.GetFloat(PCGAttributeSet.SlopeName, 0) == -1f);
        }

        private static void DensityAndHeightFilterTests(CheckDelegate check)
        {
            var positions = new[]
            {
                new Vector3(0, 0, 0), new Vector3(10, 5, 0), new Vector3(20, 50, 0), new Vector3(30, 120, 0),
            };
            var data = Points(positions);
            for (int i = 0; i < 4; i++)
            {
                var point = data.Points[i];
                point.Density = 0.25f * i;
                data.Points[i] = point;
            }

            var density = new PCGDensityFilterNode(Data(PCGDensityFilterNode.Id, 0.3f, 0.9f, false));
            var kept = Run(density, data);
            check("density filter keeps the range and drops the rest",
                kept != null && kept.Count == 2 && kept.Points[0].Density == 0.5f && kept.Points[1].Density == 0.75f,
                kept?.ToString());

            var inverted = new PCGDensityFilterNode(Data(PCGDensityFilterNode.Id, 0.3f, 0.9f, true));
            var complement = Run(inverted, data);
            check("density filter invert yields the exact complement",
                complement != null && complement.Count == 2 &&
                complement.Points[0].Density == 0f && complement.Points[1].Density == 0.25f);

            var height = new PCGHeightFilterNode(Data(PCGHeightFilterNode.Id, 4f, 100f, false));
            var high = Run(height, data);
            check("height filter selects by world Y",
                high != null && high.Count == 2 &&
                high.Points[0].Position.Y == 5f && high.Points[1].Position.Y == 50f);

            // A reversed range must be read as the author's intent instead of rejecting everything.
            var reversed = new PCGHeightFilterNode(Data(PCGHeightFilterNode.Id, 100f, 4f, false));
            check("height filter swaps a reversed range", Run(reversed, data)?.Count == 2);

            // Compaction must own its storage: writing through the filtered set may not touch the source.
            var passthrough = new PCGHeightFilterNode(Data(PCGHeightFilterNode.Id, -1f, 1000f, false));
            var copy = Run(passthrough, data);
            copy.SharedAttributes.DeclareAttribute("tag", PCGAttributeType.Float);
            copy.SharedAttributes.SetFloat("tag", 0, 9f);
            check("a filter's output does not leak attribute writes into its input",
                !data.SharedAttributes.Contains("tag") && copy.SharedAttributes.GetFloat("tag", 0) == 9f);
        }

        /// <summary>
        /// 区域过滤器：X 与 Z 各一段闭区间，两轴都在区间里才算"在这片区域"。
        /// </summary>
        /// <remarks>
        /// 这是插件里唯一回答"长在哪"的筛选器（高度管上下、距离管一个圆）。几条断言各钉一件容易写错的事：
        /// 闭区间（边界上的点要留下）、**只看 XZ**（Y 再离谱也不该被排除）、写反的区间按作者的原意换回来、
        /// 全在里面时原样透传、全在外面时给空层而不是 null（空层与 null 在下游是两种不同的东西）。
        /// </remarks>
        private static void AreaFilterTests(CheckDelegate check)
        {
            // 5×5 的网格，间距 50：-100,-50,0,50,100。
            var positions = new List<Vector3>();

            for (int ix = -2; ix <= 2; ix++)
                for (int iz = -2; iz <= 2; iz++)
                    positions.Add(new Vector3(ix * 50.0f, 0.0f, iz * 50.0f));

            // 再补一个"XZ 在框里、Y 高得离谱"的点：它必须被留下，否则说明这个筛选器偷偷按盒子算。
            positions.Add(new Vector3(0.0f, 9999.0f, 0.0f));

            var data = Points(positions.ToArray());

            var area = new PCGAreaFilterNode(Data(PCGAreaFilterNode.Id, -60.0f, 60.0f, -60.0f, 60.0f, false));
            var kept = Run(area, data);

            // x、z 各取 -50/0/50 三个值 → 9 个点，加上那个高空点 → 10 个；边界上的 ±50 必须算在里面。
            check("区域过滤器只留 XZ 都在框里的点（闭区间，边界算在内）",
                kept != null && kept.Count == 10, kept == null ? "null" : $"{kept.Count} point(s)");

            bool allInside = kept != null;

            if (kept != null)
            {
                foreach (var point in kept.Points)
                    allInside &= Math.Abs(point.Position.X) <= 60.0f && Math.Abs(point.Position.Z) <= 60.0f;
            }

            check("留下来的点确实都在框里，而且那个高空点也在（只看 XZ，不看高度）",
                allInside && CountAtZ(kept, 0.0f, 9999.0f) == 1,
                kept == null ? "null" : $"{kept.Count} point(s)");

            var inverted = new PCGAreaFilterNode(Data(PCGAreaFilterNode.Id, -60.0f, 60.0f, -60.0f, 60.0f, true));
            var complement = Run(inverted, data);

            check("取反之后拿到的正好是补集（两层加起来等于原样）",
                complement != null && kept != null && complement.Count == data.Count - kept.Count,
                complement == null ? "null" : $"{complement.Count} + {kept?.Count} vs {data.Count}");

            // 写反的区间按作者的原意换回来，而不是一个点都不留。
            var reversed = new PCGAreaFilterNode(Data(PCGAreaFilterNode.Id, 60.0f, -60.0f, 60.0f, -60.0f, false));
            check("两轴写反了也能读懂（换成作者显然想要的那两个端点）", Run(reversed, data)?.Count == 10);

            var everything = new PCGAreaFilterNode(Data(PCGAreaFilterNode.Id, -1e6f, 1e6f, -1e6f, 1e6f, false));

            check("整层都在框里时原样透传（点数不变）", Run(everything, data)?.Count == data.Count);

            var nothing = new PCGAreaFilterNode(Data(PCGAreaFilterNode.Id, 900.0f, 1000.0f, 900.0f, 1000.0f, false));
            var empty = Run(nothing, data);

            check("一个点都不在框里时给空层，而不是 null", empty != null && empty.Count == 0,
                empty == null ? "null" : $"{empty.Count}");
        }

        /// <summary>数一数某个高度上的点有几个（用来证明筛选器没有看 Y）。</summary>
        private static int CountAtZ(PCGData data, float z, float height)
        {
            if (data == null)
                return 0;

            int count = 0;

            foreach (var point in data.Points)
            {
                if (Math.Abs(point.Position.Z - z) <= 0.01f && Math.Abs(point.Position.Y - height) <= 0.01f)
                    count++;
            }

            return count;
        }

        private static void SlopeFilterTests(CheckDelegate check)
        {
            // 0 deg = flat ground (identity rotation), larger = steeper.
            var flat = PCGPoint.Create(new Vector3(0, 0, 0), 1f, 0);
            var steep = PCGPoint.Create(new Vector3(10, 0, 0), 1f, 1);
            steep.Rotation = Quaternion.Euler(0f, 0f, 80f);
            flat.UpdateBounds(new Vector3(1f, 1f, 1f));
            steep.UpdateBounds(new Vector3(1f, 1f, 1f));

            var input = new PCGData(new[] { flat, steep });
            var gentle = new PCGSlopeFilterNode(Data(PCGSlopeFilterNode.Id, 0f, 10f, false));
            var result = Run(gentle, input);

            check("slope filter derives steepness from orientation when no column exists",
                result != null && result.Count == 1 && result.Points[0].Position.X == 0f, result?.ToString());

            var column = SlopeColumn(result);
            check("slope filter publishes the derived $Slope for downstream reuse",
                column != null && column.Length == 1 && column[0] < 1f,
                column != null ? column[0].ToString("F4") : "null");

            // A cached column wins over the orientation: a point rotated after sampling still reports the slope
            // of the surface it came from, which is the entire purpose of caching it.
            var cached = Points(new[] { new Vector3(0, 0, 0), new Vector3(5, 0, 0) });
            cached.SharedAttributes.DeclareAttributeUnsafe(PCGAttributeSet.SlopeName, PCGAttributeType.Float);
            var src = cached.SharedAttributes.GetWritableFloatArray(PCGAttributeSet.SlopeName);
            src[0] = 45f;
            src[1] = 45f;

            var stillGentle = new PCGSlopeFilterNode(Data(PCGSlopeFilterNode.Id, 0f, 10f, false));
            check("a cached $Slope column overrides orientation-derived steepness",
                Run(stillGentle, cached)?.Count == 0);

            var wide = new PCGSlopeFilterNode(Data(PCGSlopeFilterNode.Id, 40f, 50f, false));
            var wideResult = Run(wide, cached);
            var wideColumn = SlopeColumn(wideResult);
            check("cached column is carried through compaction",
                wideResult?.Count == 2 && wideColumn != null && wideColumn[1] == 45f);
        }

        private static void TransformPointsTests(CheckDelegate check)
        {
            var point = PCGPoint.Create(new Vector3(10, 0, 0), 1f, 0);
            point.Rotation = Quaternion.Euler(0f, 90f, 0f);
            point.UpdateBounds(new Vector3(2f, 2f, 2f));
            var input = new PCGData(new[] { point });

            var worldNode = new PCGTransformPointsNode(Data(
                PCGTransformPointsNode.Id, new Vector3(0, 5, 0), new Vector3(0, 0, 0), new Vector3(1, 1, 1), false));
            var moved = Run(worldNode, input);
            check("world-space translation moves along world axes",
                moved != null && Near(moved.Points[0].Position, new Vector3(10, 5, 0)),
                moved?.Points[0].Position.ToString());

            // A quarter turn about Y maps the local X axis onto world Z, so the same offset must move the point
            // five units sideways and not at all along X.
            var localNode = new PCGTransformPointsNode(Data(
                PCGTransformPointsNode.Id, new Vector3(5, 0, 0), new Vector3(0, 0, 0), new Vector3(1, 1, 1), true));
            var slid = Run(localNode, input);
            var slide = new Vector3(slid.Points[0].Position.X - 10f, slid.Points[0].Position.Y,
                slid.Points[0].Position.Z - 0f);
            check("local-space translation follows the point's own frame",
                Math.Abs(Vector3Length(slide) - 5f) < 0.01f && Math.Abs(slide.X) < 0.01f && Math.Abs(slide.Z) > 4.9f,
                $"delta={slide}");

            var scaled = new PCGTransformPointsNode(Data(
                PCGTransformPointsNode.Id, new Vector3(0, 0, 0), new Vector3(0, 0, 0), new Vector3(2, 2, 2), false));
            var bigger = Run(scaled, input);
            var extents = PCGPointTransform.HalfExtents(bigger.Points[0].Bounds);
            check("scale multiplies both the point scale and its bounds",
                Math.Abs(bigger.Points[0].Scale.X - 2f) < 1e-5f && Math.Abs(extents.X - 4f) < 1e-4f,
                $"scale={bigger.Points[0].Scale.X} extents={extents.X:F4}");

            var again = Run(scaled, input);
            check("transform points is a pure function of its inputs",
                again.Points[0].Position == bigger.Points[0].Position && again.Points[0].Scale == bigger.Points[0].Scale);

            check("a modifier does not write through to its input",
                input.Points[0].Position.X == 10f && Math.Abs(input.Points[0].Scale.X - 1f) < 1e-5f);
        }

        private static float Vector3Length(Vector3 v)
        {
            return (float)Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        }

        private static void RandomOffsetTests(CheckDelegate check)
        {
            var input = Points(Line(64, 10f));
            var node = () => new PCGRandomOffsetNode(Data(
                PCGRandomOffsetNode.Id,
                new Vector3(-5f, -1f, -5f), new Vector3(5f, 1f, 5f),
                new Vector3(0f, 30f, 0f), 0.9f, 1.1f, 0));

            var first = Run(node(), input);
            var second = Run(node(), input);

            bool same = true;
            for (int i = 0; i < first.Count; i++)
            {
                if (first.Points[i].Position != second.Points[i].Position ||
                    Math.Abs(first.Points[i].Scale.X - second.Points[i].Scale.X) > 1e-6f)
                {
                    same = false;
                    break;
                }
            }

            check("random offset repeats exactly for the same seed", same);

            bool inRange = true;
            for (int i = 0; i < first.Count; i++)
            {
                var d = first.Points[i].Position - input.Points[i].Position;
                if (d.X < -5.0001f || d.X > 5.0001f || d.Y < -1.0001f || d.Y > 1.0001f ||
                    d.Z < -5.0001f || d.Z > 5.0001f)
                {
                    inRange = false;
                    break;
                }
            }

            check("random offset stays inside the declared box", inRange);
            check("random offset actually moves the points", first.Points[0].Position != input.Points[0].Position);

            // The property that makes incremental execution possible: a point's stream is keyed on its index, so
            // a prefix run must agree with the full run. A single sequential generator would depend on how many
            // points the array held and reshuffle every value.
            var prefix = Points(Line(16, 10f));
            var partial = Run(node(), prefix);
            bool stable = partial.Count == 16;
            for (int i = 0; stable && i < 16; i++)
            {
                if (partial.Points[i].Position != first.Points[i].Position)
                    stable = false;
            }

            check("offsets for a subset match the full run (index-keyed stream)", stable);

            // Seed offset must actually change the field, otherwise two offset nodes in one graph would place
            // every prop in exactly the same spot.
            var otherSeed = new PCGRandomOffsetNode(Data(
                PCGRandomOffsetNode.Id,
                new Vector3(-5f, -1f, -5f), new Vector3(5f, 1f, 5f),
                new Vector3(0f, 30f, 0f), 0.9f, 1.1f, 999));
            var shifted = Run(otherSeed, input);
            check("seed offset changes the produced offsets",
                shifted.Points[10].Position != first.Points[10].Position);
        }

        private static void MergePointsTests(CheckDelegate check)
        {
            var a = Points(new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0) });
            var b = Points(new[] { new Vector3(2, 0, 0) });

            a.SharedAttributes.DeclareAttribute("kind", PCGAttributeType.Float);
            a.SharedAttributes.SetFloat("kind", 0, 3f);
            b.SharedAttributes.DeclareAttribute("tag", PCGAttributeType.Int);
            b.SharedAttributes.SetInt("tag", 0, 7);

            var merge = new PCGMergePointsNode(Data(PCGMergePointsNode.Id));
            var merged = merge.OnExecute(Context(), new[] { PCGData.Aggregate(new[] { a, b }) });

            check("merge concatenates in edge order",
                merged != null && merged.Count == 3 &&
                merged.Points[0].Position.X == 0f && merged.Points[2].Position.X == 2f,
                merged?.ToString());

            check("merge unions attribute columns and defaults the missing side",
                merged.SharedAttributes.Contains("kind") && merged.SharedAttributes.Contains("tag") &&
                merged.SharedAttributes.GetFloat("kind", 0) == 3f &&
                merged.SharedAttributes.GetFloat("kind", 2) == 0f &&
                merged.SharedAttributes.GetInt("tag", 2) == 7);

            var c = Points(new[] { new Vector3(9, 0, 0) });
            c.SharedAttributes.DeclareAttribute("kind", PCGAttributeType.Int);

            string error = null;
            try
            {
                merge.OnExecute(Context(), new[] { PCGData.Aggregate(new[] { a, c }) });
            }
            catch (PCGNodeExecutionException ex)
            {
                error = ex.Message;
            }

            check("a same-name different-type attribute is rejected, not coerced",
                error != null && error.Contains("kind"), error);

            var slopeA = Points(new[] { new Vector3(0, 0, 0) });
            slopeA.SharedAttributes.DeclareAttributeUnsafe(PCGAttributeSet.SlopeName, PCGAttributeType.Float);
            slopeA.SharedAttributes.GetWritableFloatArray(PCGAttributeSet.SlopeName)[0] = 33f;
            var slopeMerged = merge.OnExecute(Context(), new[] { PCGData.Aggregate(new[] { slopeA, b }) });
            var slopeAfterMerge = SlopeColumn(slopeMerged);
            check("merge preserves the cached $Slope column",
                slopeAfterMerge != null && Math.Abs(slopeAfterMerge[0] - 33f) < 1e-5f,
                slopeAfterMerge != null ? slopeAfterMerge[0].ToString("F1") : "null");

            // A single edge into a multi-input pin must still republish, or a downstream write would land in
            // the upstream node's cached result.
            var single = merge.OnExecute(Context(), new[] { PCGData.Aggregate(new[] { a }) });
            single.SharedAttributes.SetFloat("kind", 0, -5f);
            check("a lone input is republished rather than passed through",
                a.SharedAttributes.GetFloat("kind", 0) == 3f && single.SharedAttributes.GetFloat("kind", 0) == -5f);
        }

        private static void SelfPruningTests(CheckDelegate check)
        {
            // Five points 30 units apart on a line with a 100-unit spacing: the densest wins its cluster, and
            // the point 120 units away is far enough to stand on its own.
            var crowded = new PCGPoint[5];
            for (int i = 0; i < 5; i++)
            {
                var p = PCGPoint.Create(new Vector3(i * 30f, 0f, 0f), 0.2f + 0.1f * i, i);
                p.UpdateBounds(new Vector3(1f, 1f, 1f));
                crowded[i] = p;
            }

            var prune = new PCGSelfPruningFilterNode(Data(PCGSelfPruningFilterNode.Id, 100f, false, true));
            var result = Run(prune, new PCGData(crowded));

            check("self-pruning keeps the densest of a crowded group",
                result != null && result.Count == 2 &&
                result.Points[0].Position.X == 0f && result.Points[1].Position.X == 120f &&
                result.Points[1].Density > 0.49f,
                result?.ToString());

            // Same stack separated only in Y. Plane mode must still prune them; a grid keyed on Y would put
            // them outside each other's neighbourhood and let every one through.
            var stacked = new PCGPoint[3];
            for (int i = 0; i < 3; i++)
            {
                var p = PCGPoint.Create(new Vector3(0f, i * 500f, 0f), 1f, i);
                p.UpdateBounds(new Vector3(1f, 1f, 1f));
                stacked[i] = p;
            }

            var planePrune = Run(prune, new PCGData(stacked));
            check("plane mode ignores the vertical axis when testing crowding",
                planePrune != null && planePrune.Count == 1, planePrune?.ToString());

            var volumePrune = new PCGSelfPruningFilterNode(Data(PCGSelfPruningFilterNode.Id, 100f, false, false));
            var volumeKept = Run(volumePrune, new PCGData(stacked));
            check("3D mode keeps vertically separated points",
                volumeKept != null && volumeKept.Count == 3, volumeKept?.ToString());

            // A jittered grid tiles its cells so that neighbours touch exactly. Contact must not read as a
            // crowd, or bounds mode on landscape points would delete all but one.
            var tiled = new PCGPoint[4];
            var overlapping = new PCGPoint[4];
            for (int i = 0; i < 4; i++)
            {
                var t = PCGPoint.Create(new Vector3(i * 20f, 0f, 0f), 1f, i);
                t.UpdateBounds(new Vector3(10f, 10f, 10f));
                tiled[i] = t;

                var o = PCGPoint.Create(new Vector3(i * 5f, 0f, 0f), 1f, i);
                o.UpdateBounds(new Vector3(10f, 10f, 10f));
                overlapping[i] = o;
            }

            var overlapPrune = new PCGSelfPruningFilterNode(Data(PCGSelfPruningFilterNode.Id, 0f, true, true));
            var touched = Run(overlapPrune, new PCGData(tiled));
            check("bounds-overlap mode does not prune merely touching boxes",
                touched != null && touched.Count == 4, touched?.ToString());

            var crushed = Run(overlapPrune, new PCGData(overlapping));
            check("bounds-overlap mode prunes genuinely overlapping points",
                crushed != null && crushed.Count == 1, crushed?.ToString());

            var spread = Run(prune, Points(Line(4, 500f)));
            bool ordered = spread != null && spread.Count == 4;
            for (int i = 1; ordered && i < 4; i++)
                ordered = spread.Points[i - 1].Position.X < spread.Points[i].Position.X;

            check("well-separated points survive and keep the original point order", ordered, spread?.ToString());
        }

        /// <summary>
        /// Exercises the exposed-parameter override path end to end: plan build, per-run node instantiation,
        /// executor hand-off and the "one compile, many instances" guarantee.
        /// </summary>
        private static void OverridePlanTests(CheckDelegate check)
        {
            PCGNodeRegistry.Register(
                PCGDensityFilterNode.Id, "Filter", d => new PCGDensityFilterNode(d), "Density Filter");
            PCGNodeRegistry.Register(
                FixedSourceNode.Id, "Sampler", d => new FixedSourceNode(d), "Fixed Source");

            var source = new FixedSourceNode(Data(FixedSourceNode.Id));

            var graph = new PCGGraph
            {
                Name = "OverrideProbe",
                Nodes = new[]
                {
                    new PCGNode { TypeId = PCGDensityFilterNode.Id, Values = new object[] { 0.3f, 0.9f, false } },
                    new PCGNode { TypeId = FixedSourceNode.Id, Values = Array.Empty<object>() },
                },
                Edges = new[]
                {
                    new PCGEdge { OutputNodeIndex = 1, OutputPinIndex = 0, InputNodeIndex = 0, InputPinIndex = 0 },
                },
                ExposedParameters = new[]
                {
                    new PCGParameterDefinition
                    {
                        Name = "Min", Type = PCGParameterType.Float, NodeIndex = 0, ValueIndex = 0,
                        DefaultValue = PCGParameterValue.FromFloat(0.6f),
                    },
                    new PCGParameterDefinition
                    {
                        Name = "Max", Type = PCGParameterType.Float, NodeIndex = 0, ValueIndex = 1,
                        DefaultValue = PCGParameterValue.FromFloat(0.9f),
                    },
                },
            };

            var authoredNode = new PCGDensityFilterNode(Data(PCGDensityFilterNode.Id, 0.3f, 0.9f, false));
            var compiled = new PCGCompiledGraph
            {
                Nodes = new PCGNodeBase[] { authoredNode, source },
                Links = new PCGInputLink[][][]
                {
                    new[] { new[] { new PCGInputLink { SourceNodeIndex = 1, SourcePinIndex = 0 } } },
                    Array.Empty<PCGInputLink[]>(),
                },
                ExecutionOrder = new[] { 1, 0 },
                SinkNodes = new[] { 0 },
                Source = graph,
                StructuralHash = PCGGraphCompiler.ComputeStructuralHash(graph),
            };

            int hashBefore = compiled.StructuralHash;
            var data = Points(Line(8, 10f));
            for (int i = 0; i < data.Count; i++)
            {
                var p = data.Points[i];
                p.Density = 0.25f * (i % 4);
                data.Points[i] = p;
            }

            source.Data = data;

            // No overrides: the compiled instances are reused rather than rebuilt.
            var plain = PCGExecutionPlan.Build(compiled, null);
            check("a plan with no overrides reuses the compiled instances",
                plain.ReusedCompiledNodes && ReferenceEquals(plain.Nodes[0], authoredNode));

            // An override rebuilds only the affected node and changes what the run produces.
            var tight = PCGExecutionPlan.Build(compiled, new Dictionary<string, PCGParameterValue>(StringComparer.Ordinal)
            {
                ["Min"] = PCGParameterValue.FromFloat(0.7f),
            });

            var rebuilt = tight.Nodes[0] as PCGDensityFilterNode;
            check("an override rebuilds the node with the new value",
                !tight.ReusedCompiledNodes && rebuilt != null && Math.Abs(rebuilt.MinDensity - 0.7f) < 1e-6f,
                rebuilt != null ? $"MinDensity={rebuilt.MinDensity:F3}" : "null");

            check("the compiled graph is never mutated by an override",
                ReferenceEquals(compiled.Nodes[0], authoredNode) &&
                Math.Abs(authoredNode.MinDensity - 0.3f) < 1e-6f &&
                Math.Abs((float)graph.Nodes[0].Values[0] - 0.3f) < 1e-6f);

            int hashAfter = PCGGraphCompiler.ComputeStructuralHash(graph);
            check("override tables do not disturb the structural hash", hashBefore == hashAfter,
                $"{hashBefore} vs {hashAfter}");

            int authoredKept = new PCGGraphExecutor(compiled, plain).Execute(Context()).PrimaryOutput.Count;
            int tightKept = new PCGGraphExecutor(compiled, tight).Execute(Context()).PrimaryOutput.Count;
            check("the override actually reaches execution",
                authoredKept == 4 && tightKept == 2, $"authored kept {authoredKept}, overridden kept {tightKept}");

            // Two exposed parameters on one node must compose rather than the second starting from scratch.
            var both = PCGExecutionPlan.Build(compiled, new Dictionary<string, PCGParameterValue>(StringComparer.Ordinal)
            {
                ["Min"] = PCGParameterValue.FromFloat(0.5f),
                ["Max"] = PCGParameterValue.FromFloat(0.5f),
            });

            var bothNode = both.Nodes[0] as PCGDensityFilterNode;
            check("several exposed parameters on one node compose",
                bothNode != null && Math.Abs(bothNode.MinDensity - 0.5f) < 1e-6f &&
                Math.Abs(bothNode.MaxDensity - 0.5f) < 1e-6f,
                bothNode?.DescribeParameters());
            check("a zero-width window keeps only points at that exact density",
                new PCGGraphExecutor(compiled, both).Execute(Context()).PrimaryOutput.Count == 2);

            // The graph default fills only an empty slot; an authored value is the author's decision.
            graph.Nodes[0].Values = new object[] { null, 0.9f, false };
            var defaulted = PCGExecutionPlan.Build(compiled, null);
            var defaultedNode = defaulted.Nodes[0] as PCGDensityFilterNode;
            check("an exposed default fills a slot the node left empty",
                defaultedNode != null && Math.Abs(defaultedNode.MinDensity - 0.6f) < 1e-6f,
                defaultedNode != null ? $"MinDensity={defaultedNode.MinDensity:F3}" : "null");

            graph.Nodes[0].Values = new object[] { 0.3f, 0.9f, false };

            // An override naming nothing must be reported, not quietly dropped.
            var bogus = PCGExecutionPlan.Build(compiled, new Dictionary<string, PCGParameterValue>(StringComparer.Ordinal)
            {
                ["NoSuchParameter"] = PCGParameterValue.FromFloat(1f),
            });

            bool warned = false;
            for (int i = 0; i < bogus.Warnings.Length; i++)
            {
                if (bogus.Warnings[i] != null && bogus.Warnings[i].Contains("NoSuchParameter"))
                    warned = true;
            }

            check("an override for an unknown parameter name is reported", warned,
                bogus.Warnings.Length > 0 ? bogus.Warnings[0] : "no warnings");

            // ... and the warning has to reach the run result, or nothing consumes it.
            var run = new PCGGraphExecutor(compiled, bogus).Execute(Context());
            bool surfaced = false;
            for (int i = 0; i < (run.Warnings?.Length ?? 0); i++)
            {
                if (run.Warnings[i] != null && run.Warnings[i].Contains("NoSuchParameter"))
                    surfaced = true;
            }

            check("plan warnings surface on the execution result", surfaced);

            // A definition pointing outside the graph is a data error the author must see.
            graph.ExposedParameters = new[]
            {
                new PCGParameterDefinition
                {
                    Name = "Min", Type = PCGParameterType.Float, NodeIndex = 42, ValueIndex = 0,
                },
            };

            var outOfRange = PCGExecutionPlan.Build(compiled, null);
            check("an exposed parameter targeting a missing node is reported and ignored",
                outOfRange.Warnings.Length > 0 && outOfRange.Warnings[0].Contains("42"),
                outOfRange.Warnings.Length > 0 ? outOfRange.Warnings[0] : "no warnings");

            PCGNodeRegistry.Clear();
        }

        /// <summary>
        /// Exercises the noise and volume samplers and the distance filter.
        /// </summary>
        private static void SamplerTests(CheckDelegate check)
        {
            PCGNodeRegistry.Register(PCGNoiseSamplerNode.Id, "Sampler", d => new PCGNoiseSamplerNode(d), "Noise Sampler");
            PCGNodeRegistry.Register(PCGVolumeSamplerNode.Id, "Sampler", d => new PCGVolumeSamplerNode(d), "Volume Sampler");
            PCGNodeRegistry.Register(PCGDistanceFilterNode.Id, "Filter", d => new PCGDistanceFilterNode(d), "Distance Filter");

            var noiseValues = new object[] { 1 /* Perlin2D */, 0.005f, 0.05f, 1, 40f, 0.5f, 0f, 0, 1f };
            var noise = new PCGNoiseSamplerNode(Data(PCGNoiseSamplerNode.Id, noiseValues));
            var field = Run(noise, PCGData.Empty);

            check("the noise sampler produces a field of points",
                field != null && field.Count > 0, $"{field?.Count ?? -1} point(s), {noise.LastError}");

            var fieldAgain = Run(new PCGNoiseSamplerNode(Data(PCGNoiseSamplerNode.Id, noiseValues)), PCGData.Empty);
            bool noiseSame = field != null && fieldAgain != null && field.Count == fieldAgain.Count;
            for (int i = 0; noiseSame && i < field.Count; i++)
                noiseSame = field.Points[i].Position == fieldAgain.Points[i].Position;

            check("the noise sampler is reproducible for its seed", noiseSame);

            var otherSeed = Run(new PCGNoiseSamplerNode(Data(PCGNoiseSamplerNode.Id,
                new object[] { 1, 0.005f, 0.05f, 1, 40f, 0.5f, 0f, 777, 1f })), PCGData.Empty);
            check("a seed offset changes the noise field",
                otherSeed != null && (otherSeed.Count != field.Count ||
                    (otherSeed.Count > 0 && otherSeed.Points[0].Position != field.Points[0].Position)),
                $"{field.Count} vs {otherSeed?.Count ?? -1}");

            var strict = Run(new PCGNoiseSamplerNode(Data(PCGNoiseSamplerNode.Id,
                new object[] { 1, 0.005f, 0.9f, 1, 40f, 0.5f, 0f, 0, 1f })), PCGData.Empty);
            check("a higher threshold never yields more points",
                strict == null || strict.Count <= field.Count, $"{field.Count} loose vs {strict?.Count ?? 0} strict");

            bool inside = true;
            float minDensity = float.MaxValue;
            foreach (var point in field.Points)
            {
                if (point.Position.X < -1020f || point.Position.X > 1020f ||
                    point.Position.Z < -1020f || point.Position.Z > 1020f)
                    inside = false;

                if (point.Density < minDensity)
                    minDensity = point.Density;
            }

            check("noise points stay inside the generation bounds", inside);
            check("point density carries the field value, so edges are sparse rather than absent",
                minDensity >= 0.05f - 1e-4f, $"min density {minDensity:F4}");

            var volume = new PCGVolumeSamplerNode(Data(PCGVolumeSamplerNode.Id, 500, 0, 1f, true, 1f));
            var scattered = Run(volume, PCGData.Empty);
            check("the volume sampler yields exactly the requested count",
                scattered != null && scattered.Count == 500, scattered?.ToString());

            bool inVolume = true;
            for (int i = 0; scattered != null && i < scattered.Count; i++)
            {
                var p = scattered.Points[i].Position;
                if (p.X < -1000f || p.X > 1000f || p.Y < 0f || p.Y > 200f || p.Z < -1000f || p.Z > 1000f)
                    inVolume = false;
            }

            check("volume points land inside the bounds on all three axes", inVolume);

            var flat = new PCGExecutionContext
            {
                Seed = 1,
                CurrentNodeId = 7,
                GenerationBounds = new BoundingBox(new Vector3(-100, 0, -100), new Vector3(100, 0, 100)),
            };
            var flatRun = volume.OnExecute(flat, new[] { PCGData.Empty });
            check("a flat volume reports why it produced nothing instead of failing silently",
                (flatRun == null || flatRun.Count == 0) && volume.LastError != null, volume.LastError);

            // Reporting is only half of it: the run has to carry that problem out, or the designer sees an
            // empty scene and no line pointing at the graph that caused it.
            var errorGraph = new PCGGraph
            {
                Name = "FlatVolume",
                Nodes = new[]
                {
                    new PCGNode { TypeId = PCGVolumeSamplerNode.Id, Values = new object[] { 10, 0, 1f, true, 1f } },
                },
                Edges = Array.Empty<PCGEdge>(),
            };

            var errorCompiled = new PCGCompiledGraph
            {
                Nodes = new PCGNodeBase[] { volume },
                Links = new PCGInputLink[][][] { Array.Empty<PCGInputLink[]>() },
                ExecutionOrder = new[] { 0 },
                SinkNodes = new[] { 0 },
                Source = errorGraph,
            };

            var errorRun = new PCGGraphExecutor(errorCompiled).Execute(flat);
            bool surfaced = false;
            for (int i = 0; i < (errorRun.Warnings?.Length ?? 0); i++)
            {
                if (errorRun.Warnings[i] != null && errorRun.Warnings[i].Contains("bounds are flat"))
                    surfaced = true;
            }

            check("a node's reported problem surfaces on the run warnings", surfaced,
                errorRun.Warnings != null && errorRun.Warnings.Length > 0 ? errorRun.Warnings[0] : "no warnings");

            check("the problem is cleared once collected, so it is not reported per consumer",
                volume.LastError == null, volume.LastError);

            // ---- distance filter ----------------------------------------------

            var ring = Points(new[]
            {
                new Vector3(0, 0, 0), new Vector3(10, 0, 0), new Vector3(20, 0, 0), new Vector3(30, 0, 0),
            });

            var band = new PCGDistanceFilterNode(Data(PCGDistanceFilterNode.Id,
                new Vector3(), 5f, 25f, true, false));
            var banded = Run(band, ring);
            check("the distance band keeps only the points inside it",
                banded != null && banded.Count == 2 && banded.Points[1].Position.X == 20f,
                banded?.ToString());

            var stacked = Points(new[] { new Vector3(20, 0, 0), new Vector3(20, 500, 0) });
            var planeKept = Run(band, stacked);
            check("plane mode leaves the vertical axis out of the measurement",
                planeKept != null && planeKept.Count == 2, planeKept?.ToString());

            var solid = new PCGDistanceFilterNode(Data(PCGDistanceFilterNode.Id,
                new Vector3(), 5f, 25f, false, false));
            var solidKept = Run(solid, stacked);
            check("3D mode does measure the vertical axis",
                solidKept != null && solidKept.Count == 1 && solidKept.Points[0].Position.Y == 0f,
                solidKept?.ToString());

            check("the distance filter's invert yields the complement",
                Run(new PCGDistanceFilterNode(Data(PCGDistanceFilterNode.Id,
                    new Vector3(), 5f, 25f, true, true)), ring)?.Count == 2);

            check("a reversed distance range is honoured rather than rejecting everything",
                Run(new PCGDistanceFilterNode(Data(PCGDistanceFilterNode.Id,
                    new Vector3(), 25f, 5f, true, false)), ring)?.Count == 2);
        }

        /// <summary>
        /// Checks the parameter schema table against the nodes it describes, and the compiler's use of it.
        /// </summary>
        /// <remarks>
        /// The schema is what the graph editor pre-fills and labels, so a slot default that disagrees with
        /// the node's own fallback means the canvas shows one value while the graph runs another. That drift
        /// is invisible to a type checker, so it is caught here instead: build the node twice - once from an
        /// empty value array, once from the schema defaults - and the two must describe themselves identically.
        /// </remarks>
        /// <summary>
        /// 参数列的摆放不变量：**每个矩形都在那一列之内**，而且输入框彼此不重叠。
        /// </summary>
        /// <remarks>
        /// 抽这条出来是因为"输入框右端伸出面板、露出去的那截点不到"是一类只会在屏幕上显形的缺陷，而离屏门禁
        /// 看不见编辑器。摆放规则现在是纯函数（<see cref="PCGParameterLayout"/>），于是这条不变量能被断言钉住；
        /// 窗口那边只负责把算出来的矩形赋给控件，用的是同一份规则。
        /// </remarks>
        private static void ParameterLayoutTests(CheckDelegate check)
        {
            float[] widths = { 340.0f, 264.0f, 240.0f, 200.0f, 180.0f };
            var problems = new List<string>();

            foreach (float panel in widths)
            {
                foreach (int components in new[] { 1, 2, 3, 4 })
                {
                    var layout = PCGParameterLayout.Row(panel, components);
                    float limit = Mathf.Max(PCGParameterLayout.MinPanelWidth, panel) + 0.01f;

                    if (layout.Label.X < -0.01f || layout.Label.Right > limit)
                        problems.Add($"{panel}/{components}: 标签出界 {layout.Label}");

                    if (layout.ExposeLabel.Right > limit || layout.ExposeBox.Right > limit)
                        problems.Add($"{panel}/{components}: 暴露那一块出界");

                    if (layout.Label.Right > layout.ExposeLabel.X + 0.01f)
                        problems.Add($"{panel}/{components}: 标签压住了暴露");

                    for (int c = 0; c < components; c++)
                    {
                        var field = layout.FieldRect(c);

                        if (field.X < -0.01f || field.Right > limit)
                            problems.Add($"{panel}/{components}: 第 {c} 个输入框出界 {field}");

                        if (c > 0 && field.X < layout.FieldRect(c - 1).Right - 0.01f)
                            problems.Add($"{panel}/{components}: 输入框 {c - 1} 与 {c} 重叠");

                        if (field.Width < PCGParameterLayout.MinFieldWidth - 0.01f)
                            problems.Add($"{panel}/{components}: 输入框只有 {field.Width:F1} 宽");
                    }
                }
            }

            check("参数列里每个矩形都在列内、输入框互不重叠（含最窄的面板）",
                problems.Count == 0, string.Join(" | ", problems));

            // 标题条与那句提示也属于这一列：它们出界会把"没有属性面板"重新变成看不清的一块。
            var headerProblems = new List<string>();

            foreach (float panel in widths)
            {
                float limit = Mathf.Max(PCGParameterLayout.MinPanelWidth, panel) + 0.01f;
                var header = PCGParameterLayout.Header(panel);
                var hint = PCGParameterLayout.Hint(panel);

                if (header.X < -0.01f || header.Right > limit)
                    headerProblems.Add($"{panel}: 标题出界 {header}");

                if (hint.X < -0.01f || hint.Right > limit)
                    headerProblems.Add($"{panel}: 提示出界 {hint}");

                if (hint.Y < header.Bottom - 0.01f)
                    headerProblems.Add($"{panel}: 提示压在标题上");
            }

            check("标题条与提示也都在列内、且不叠在标题上",
                headerProblems.Count == 0, string.Join(" | ", headerProblems));

            // 面板窄到离谱时**向上钳到可用下限**，而不是算出一堆出界的矩形交出去。
            var clamped = PCGParameterLayout.Row(40.0f, 4);

            check("面板窄于下限时按下限算，不给出界的结果",
                clamped.RightEdge <= PCGParameterLayout.MinPanelWidth + 0.01f,
                $"右边界 {clamped.RightEdge:F1}");

            // 加新类型时的守门：现在最多的是颜色（4 个分量），最窄的面板也必须装得下它。
            int capacity = PCGParameterLayout.MaxFittingComponents(PCGParameterLayout.MinPanelWidth);

            check("最窄的面板也装得下 4 个分量（颜色），否则加新类型会先把输入框挤出去",
                capacity >= 4, $"最窄时只装得下 {capacity} 个");
        }

        /// <summary>
        /// 分组的数值 id 由**稳定键**算出，与显示名无关——翻译显示名不该换掉 id。
        /// </summary>
        /// <remarks>
        /// 这条是补一个真缺陷：分组 id 原本按分组名哈希，而画布载荷里存的正是"分组 id : 类型 id"这一对，
        /// 于是把分组名译成简体中文这个**纯显示**动作换掉了每个分组的 id，旧存档里的节点随即解析不到
        /// （引擎只留一行 "Failed to create Visject Surface node with id: 7:29"，设计者看到的是一张少了节点的图）。
        /// 最后一条是**反向对照**：直接拿翻译后的显示名去算，结果必须与稳定键算出来的不同，
        /// 否则这条断言证明不了自己守得住什么。
        /// </remarks>
        /// <summary>
        /// 参数列显示的中文：注册表里**每一个**槽位名与**每一个**枚举选项都要有中文。
        /// </summary>
        /// <remarks>
        /// 遍历的是注册表本身，不是我手抄的一份名单：加一个槽位、加一个枚举选项而忘了配中文，就会当场红，
        /// 理由里带着缺的原文。"某个参数还写着英文"这种事只有眼睛会发现，离屏门禁看不见编辑器，
        /// 所以覆盖要靠这条断言来保证。
        /// </remarks>
        private static void ParameterLabelTests(CheckDelegate check)
        {
            PCGNodeRegistry.Clear();
            PCG.Runtime.PCGNodeRegistration.RegisterAll();

            var missing = new List<string>();
            int slots = 0;
            int options = 0;

            foreach (var info in PCGNodeRegistry.All)
            {
                var schema = info.Parameters;

                if (schema == null)
                    continue;

                foreach (var slot in schema)
                {
                    if (slot == null || string.IsNullOrEmpty(slot.Name))
                        continue;

                    slots++;

                    if (!PCGParameterLabels.Has(slot.Name))
                        missing.Add($"{info.TypeId}.{slot.Name}");

                    if (slot.Options == null)
                        continue;

                    foreach (var option in slot.Options)
                    {
                        options++;

                        if (!PCGParameterLabels.Has(option))
                            missing.Add($"{info.TypeId}.{slot.Name} → {option}");
                    }
                }
            }

            check("每个槽位名与每个枚举选项都有中文标签（缺了会在这里报出原文）",
                missing.Count == 0, $"{slots} 个槽位 / {options} 个选项；缺：{string.Join(" | ", missing)}");

            var untranslated = new List<string>();

            foreach (var name in new[] { "Point Count", "Max Depth", "Align To Normal", "Ray Cast" })
            {
                if (PCGParameterLabels.Label(name) == name)
                    untranslated.Add(name);
            }

            check("抽查几个名字确实被翻成了中文（表非空且真的在起作用）",
                untranslated.Count == 0, string.Join(" | ", untranslated));
        }

        private static void CategoryIdStabilityTests(CheckDelegate check)
        {
            string[] displays =
            {
                PCG.Runtime.PCGNodeCategories.Sampler,
                PCG.Runtime.PCGNodeCategories.Filter,
                PCG.Runtime.PCGNodeCategories.Modifier,
                PCG.Runtime.PCGNodeCategories.Combinator,
                PCG.Runtime.PCGNodeCategories.PointOps,
                PCG.Runtime.PCGNodeCategories.ControlFlow,
                PCG.Runtime.PCGNodeCategories.Spawner,
                PCG.Runtime.PCGNodeCategories.Structure,
            };

            string[] canonical =
            {
                "Sampler", "Filter", "Modifier", "Combinator",
                "Point Ops", "Control Flow", "Spawner", "Structure",
            };

            var keys = new string[displays.Length];
            var mismatched = new List<string>();

            for (int i = 0; i < displays.Length; i++)
            {
                keys[i] = PCG.Runtime.PCGNodeCategories.KeyOf(displays[i]);

                if (keys[i] != canonical[i])
                    mismatched.Add($"{displays[i]}→{keys[i]}（应为 {canonical[i]}）");
            }

            check("每个分组名都映射回它原来的稳定键（键与显示名分开）",
                mismatched.Count == 0, string.Join(" | ", mismatched));

            var byKey = PCGNodeStableIds.Assign(keys);
            var byEnglish = PCGNodeStableIds.Assign(canonical);
            var byDisplay = PCGNodeStableIds.Assign(displays);

            check("按稳定键算出来的分组 id 与按英文名算的一致（翻译不会换掉 id）",
                SameIds(byKey, byEnglish), JoinIds(byKey, byEnglish));

            check("反向对照：直接拿中文显示名算，结果与稳定键不同（否则上一条守不住东西）",
                !SameIds(byDisplay, byKey), JoinIds(byDisplay, byKey));
        }

        private static bool SameIds(ushort[] a, ushort[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }

        private static string JoinIds(ushort[] a, ushort[] b)
        {
            var text = new StringBuilder();

            for (int i = 0; i < a.Length; i++)
            {
                if (i > 0)
                    text.Append(", ");

                text.Append(a[i]).Append('/').Append(b[i]);
            }

            return text.ToString();
        }

        /// <summary>
        /// 每种参数拿到的编辑控件要对：资产槽位必须是**选择器**（可筛类型、带搜索），枚举槽位必须是**下拉框**。
        /// </summary>
        /// <remarks>
        /// "资产槽位给成了让人手打 32 位 GUID 的文本框"只有眼睛会发现，而它恰恰是设计者最先撞上的一道墙。
        /// 判定抽成了纯函数（<see cref="PCGParameterText.EditorKindOf"/>），这里遍历注册表把它钉住：
        /// 谁把某个资产槽位换回文本框、或加一个没有选项的枚举，这条会先红。
        /// </remarks>
        private static void ParameterEditorKindTests(CheckDelegate check)
        {
            PCGNodeRegistry.Clear();
            PCG.Runtime.PCGNodeRegistration.RegisterAll();

            int assets = 0;
            int enums = 0;
            int texts = 0;
            int paths = 0;
            var problems = new List<string>();

            foreach (var info in PCGNodeRegistry.All)
            {
                if (info.Parameters == null)
                    continue;

                foreach (var slot in info.Parameters)
                {
                    if (slot == null)
                        continue;

                    var editor = PCGParameterText.EditorKindOf(slot.Name, slot.Kind);

                    // 路径槽位（存的是路径字符串而不是资产 id）先判：它们必须走选择器，绝不能退回文本框。
                    if (PCGParameterText.IsContentPathSlot(slot.Name))
                    {
                        paths++;

                        if (editor != PCGParameterText.PCGParameterEditorKind.PathPicker)
                            problems.Add($"{info.TypeId}.{slot.Name} 路径槽位没走选择器");

                        // `continue` 而不是 `break`：break 会跳出整个槽位循环，那条链上排在它后面的槽位
                        // 就从覆盖里消失了（第一次写成了 break，槽位总数从 319 掉到 308 才发现）。
                        continue;
                    }


                    switch (slot.Kind)
                    {
                    case PCGParameterType.Asset:
                        assets++;

                        if (editor != PCGParameterText.PCGParameterEditorKind.Asset)
                            problems.Add($"{info.TypeId}.{slot.Name} 资产槽位没走选择器");

                        break;

                    case PCGParameterType.Enum:
                        enums++;

                        if (editor != PCGParameterText.PCGParameterEditorKind.Combo)
                            problems.Add($"{info.TypeId}.{slot.Name} 枚举槽位没走下拉框");

                        if (slot.Options == null || slot.Options.Length < 2)
                            problems.Add($"{info.TypeId}.{slot.Name} 的选项少于两个");

                        break;

                    default:
                        texts++;

                        if (editor != PCGParameterText.PCGParameterEditorKind.Text)
                            problems.Add($"{info.TypeId}.{slot.Name} 普通槽位没走文本框");

                        break;
                    }
                }
            }

            check("资产走选择器、枚举走下拉框、其余走文本框（逐槽位全覆盖）",
                problems.Count == 0,
                $"资产 {assets} / 路径 {paths} / 枚举 {enums} / 文本 {texts}；问题：{string.Join(" | ", problems)}");

            check("确实有资产槽位（否则上一条是空转）", assets > 0, assets.ToString());

            check("确实有路径槽位（模型路径 / 纹理路径 / 子图各一个）", paths == 3, paths.ToString());

            // 路径规整：去掉首尾空白、反斜杠统一成正斜杠，且**幂等**（再规整一次不变）。
            check("路径规整：空白去掉、反斜杠转正斜杠、幂等",
                PCGParameterText.NormalizeContentPath("  Content\\PCG\\M.flax \n") == "Content/PCG/M.flax" &&
                PCGParameterText.NormalizeContentPath("Content/PCG/M.flax") == "Content/PCG/M.flax" &&
                PCGParameterText.NormalizeContentPath(null) == null);
        }

        /// <summary>
        /// 场景组件里不许有"存不进场景"的公开字段——**委托**是这一类里最常见、也最致命的一种。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 这条是补一个真崩溃：`PCGVolume` 上有三个 `Func<...>` 宿主钩子（版本号来源、视角来源、可见 chunk 来源），
        /// 都是公开字段。Flax 存场景时按公开字段走，遇到委托写不进字节流——**PIE 一启动就崩**，
        /// 日志恰好停在「Saving scene to bytes」那一步，而离屏门禁完全看不见（它不存场景，也不跑 PIE）。
        /// </para>
        /// <para>
        /// 判据是"公开实例字段的类型是委托，且没标 <c>[NoSerialize]</c>"。PIE 专有、我跑不了，所以这条断言
        /// 就是它的替代：谁再往组件上挂一个委托字段而忘了标注，这里先红。
        /// </para>
        /// </remarks>
        private static void ComponentSerializationTests(CheckDelegate check)
        {
            var problems = new List<string>();
            int components = 0;
            int fields = 0;

            foreach (var type in typeof(PCG.Runtime.PCGVolume).Assembly.GetTypes())
            {
                if (type.IsAbstract || !typeof(Actor).IsAssignableFrom(type) && !typeof(Script).IsAssignableFrom(type))
                    continue;

                components++;

                foreach (var field in type.GetFields(System.Reflection.BindingFlags.Public |
                                                      System.Reflection.BindingFlags.Instance))
                {
                    fields++;

                    bool isDelegate = typeof(Delegate).IsAssignableFrom(field.FieldType);

                    if (!isDelegate)
                        continue;

                    bool optedOut = field.GetCustomAttributes(typeof(NoSerializeAttribute), true).Length > 0;

                    if (!optedOut)
                        problems.Add($"{type.Name}.{field.Name}（{field.FieldType.Name}）");
                }
            }

            check("组件里没有未标注 [NoSerialize] 的委托字段（它会 PIE 存场景时崩）",
                problems.Count == 0, string.Join(" | ", problems));

            // 更狠的一条：Flax 的契约解析器按 `Public | NonPublic | Instance` 枚举字段，
            // 也就是**私有字段也会被写进场景**（ExtendedDefaultContractResolver）。本插件的约定是
            // "公开字段才是设置、私有字段都是运行时状态"，所以**私有/受保护实例字段必须 [NoSerialize]**。
            // 这条正是本轮真崩溃的根因：`_lastOutput`（PCGData + 带字典与回指的 PCGAttributeSet）
            // 只在点过「生成」之后才有值——所以"只有生成过的场景才崩"，而崩在存场景那一步（原生栈溢出）。
            var privateProblems = new List<string>();
            int privateFields = 0;

            foreach (var type in typeof(PCG.Runtime.PCGVolume).Assembly.GetTypes())
            {
                if (type.IsAbstract ||
                    !typeof(Actor).IsAssignableFrom(type) && !typeof(Script).IsAssignableFrom(type))
                    continue;

                foreach (var field in type.GetFields(System.Reflection.BindingFlags.NonPublic |
                                                      System.Reflection.BindingFlags.Instance))
                {
                    // 只管**我们自己声明**的字段：引擎基类里那些 internal 字段（`__unmanagedPtr` 之类）
                    // 不是我们的，也不由我们决定怎么存。
                    if (field.DeclaringType != type || field.Name.Contains("k__BackingField"))
                        continue;

                    privateFields++;

                    if (field.GetCustomAttributes(typeof(NoSerializeAttribute), true).Length == 0)
                        privateProblems.Add($"{type.Name}.{field.Name}（{field.FieldType.Name}）");
                }
            }

            check("组件的私有字段都是 [NoSerialize]（Flax 连私有字段也存，会存进成环的运行时状态）",
                privateProblems.Count == 0, string.Join(" | ", privateProblems));

            check("确实扫到了私有字段（否则上一条是空转）", privateFields > 0, privateFields.ToString());

            check("确实扫到了组件与字段（否则上一条是空转）",
                components > 0 && fields > 0, $"组件 {components} / 字段 {fields}");

            // 正面同夹具：那三个宿主钩子必须**还在**、并且都标了 [NoSerialize]。
            // 少了这半条，上一条可以靠"把字段删掉"变绿——而那会把宿主接线的 API 一起删掉。
            var hooks = new[] { "PartitionVersionSource", "StreamViewerSource", "StreamChunkSource" };
            var missingHooks = new List<string>();

            foreach (var name in hooks)
            {
                var field = typeof(PCG.Runtime.PCGVolume).GetField(name,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                if (field == null)
                {
                    missingHooks.Add($"{name} 不见了");
                    continue;
                }

                if (field.GetCustomAttributes(typeof(NoSerializeAttribute), true).Length == 0)
                    missingHooks.Add($"{name} 没标 [NoSerialize]");
            }

            check("三个宿主钩子仍在，且都标了 [NoSerialize]（上一条的正面同夹具）",
                missingHooks.Count == 0, string.Join(" | ", missingHooks));
        }

        /// <summary>
        /// 编译入口自己保证节点表装好：把表清空之后直接编译一张图，必须仍然成功。
        /// </summary>
        /// <remarks>
        /// 补的真缺陷：编辑器那侧是画布（`PCGNodeArchetypes.Build`）替我们注册的，而 PIE / 游戏里没有画布，
        /// 表可能被脚本重载清过却没人重装——于是"编辑器里跑得好好的图，一进 PIE 就报
        /// Unknown node type 'LandscapeSampler' … a plugin that is not loaded"。
        /// </remarks>
        private static void CompileRegistersTypesTests(CheckDelegate check)
        {
            PCGNodeRegistry.Clear();
            PCG.Runtime.PCGNodeRegistration.RegisterAll();

            // 先在建图时表是在的（编译器要用注册表查槽位名），再把表清掉，模拟运行期那种"没人重装"的状态。
            var built = PCGSpecCompiler.Compile(
                PCGSpecParser.TryParse("world 探针\nscatter trees points 10", out var spec, out _) ? spec : null,
                null);

            PCGNodeRegistry.Clear();

            check("夹具真的把表清空了（否则下一条是空转）",
                PCGNodeRegistry.Count == 0, PCGNodeRegistry.Count.ToString());

            bool compiled = built != null && built.Graph != null &&
                            PCGGraphCompiler.TryCompile(built.Graph, out var program) && program != null;

            check("清表之后直接编译仍然成功（入口自己会装回节点表）", compiled);

            PCG.Runtime.PCGNodeRegistration.RegisterAll();
        }

        /// <summary>
        /// 除包装本体外，插件里不许出现裸的 <c>Level.SpawnActor</c> 调用。
        /// </summary>
        /// <remarks>
        /// 这条是补一族的真缺陷：引擎的 `Level.SpawnActor` **返回 true 表示失败**（`Level.h`：
        /// "True if action cannot be done, otherwise false"），而插件里五处调用都当成"true 是成功"——
        /// 于是每个生成器都在**成功之后**报 "failed to spawn …" 并丢掉自己刚创建的 Actor，
        /// 整齐地表现为"图跑得出几百个点，场景里 0 个东西"；其中一处还把它跟 null 比（那个表达式恒为真）。
        /// 离线门禁看不见场景，所以用"只许经包装调用"这条 lint 钉住它——并要求包装本体确实做了取反，
        /// 否则它可靠删掉包装来变绿。
        /// </remarks>
        private static void RawSpawnTripwireTests(CheckDelegate check)
        {
            string pluginRoot = FindPluginSourceRoot();

            if (pluginRoot == null)
            {
                check("找得到插件源码目录（否则本组是空转）", false, "没找到 HundunWorld/Plugins/PCG/Source/PCG");
                return;
            }

            var offenders = new List<string>();
            string wrapper = null;
            int scanned = 0;

            foreach (var file in System.IO.Directory.GetFiles(pluginRoot, "*.cs", System.IO.SearchOption.AllDirectories))
            {
                string text = System.IO.File.ReadAllText(file);

                if (System.IO.Path.GetFileName(file) == "PCGActors.cs")
                {
                    wrapper = text;
                    continue;
                }

                scanned++;

                if (text.Contains("Level.SpawnActor("))
                    offenders.Add(System.IO.Path.GetFileName(file));
            }

            check("扫到了源码（否则本组是空转）", scanned > 0, $"{scanned} 个文件");

            check("除包装本体外没有裸的 Level.SpawnActor 调用",
                offenders.Count == 0, string.Join(" | ", offenders));

            check("包装本体确实把反的返回值翻了过来（!Level.SpawnActor）",
                wrapper != null && wrapper.Contains("!Level.SpawnActor("),
                wrapper == null ? "没找到 PCGActors.cs" : $"包装里有没有取反：{wrapper.Contains("!Level.SpawnActor(")}");
        }

        /// <summary>从程序集所在目录往上找插件源码根（离屏环境里没有工程，只能这样找）。</summary>
        private static string FindPluginSourceRoot()
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);

            while (dir != null)
            {
                string candidate = System.IO.Path.Combine(dir.FullName, "HundunWorld", "Plugins", "PCG", "Source", "PCG");

                if (System.IO.Directory.Exists(candidate))
                    return candidate;

                dir = dir.Parent;
            }

            return null;
        }

        private static void ParameterSchemaTests(CheckDelegate check)
        {
            PCGNodeRegistry.Clear();

            // A deliberate tripwire, named so its intent survives: registering a node is a two-file change - the
            // registration and this number - and the count is what makes the second half mandatory instead of
            // forgotten. It is also what caught the Point Ops addition.
            const int BuiltInTypes = 61;

            int count = PCG.Runtime.PCGNodeRegistration.RegisterAll();
            check($"the node table registers {BuiltInTypes} built-in types", count == BuiltInTypes, count.ToString());
            check("re-registration does not duplicate entries",
                PCG.Runtime.PCGNodeRegistration.RegisterAll() == BuiltInTypes);

            var problems = new List<string>();
            int unnamedSlots = 0;
            int checkedSlots = 0;
            int defaultsDisagree = 0;

            foreach (var info in PCGNodeRegistry.All)
            {
                var schema = info.Parameters;
                if (schema == null || schema.Length == 0)
                    continue;

                var defaults = new object[schema.Length];
                for (int i = 0; i < schema.Length; i++)
                {
                    var slot = schema[i];
                    checkedSlots++;

                    if (string.IsNullOrEmpty(slot.Name))
                        unnamedSlots++;

                    if (!slot.IsCompatible(slot.Default))
                        problems.Add($"{info.TypeId}.{slot.Name}: default '{slot.Default}' is incompatible with {slot.Kind}");

                    defaults[i] = slot.Default;
                }

                var fromEmpty = PCGNodeRegistry.CreateNode(
                    new PCGNode { TypeId = info.TypeId, Values = Array.Empty<object>() }, 0, out var emptyError);
                var fromDefaults = PCGNodeRegistry.CreateNode(
                    new PCGNode { TypeId = info.TypeId, Values = defaults }, 0, out var defaultError);

                if (fromEmpty == null || fromDefaults == null)
                {
                    problems.Add($"{info.TypeId}: could not build a prototype ({emptyError ?? defaultError})");
                    continue;
                }

                if (fromEmpty.DescribeParameters() != fromDefaults.DescribeParameters())
                {
                    defaultsDisagree++;
                    problems.Add($"{info.TypeId}: schema defaults run differently from the node's own fallbacks\n" +
                                 $"    empty: {fromEmpty.DescribeParameters()}\n" +
                                 $"    defaults: {fromDefaults.DescribeParameters()}");
                }
            }

            check("every parameter slot in the table is named", unnamedSlots == 0, $"{checkedSlots} slot(s) checked");
            check("no schema default contradicts its declared kind",
                problems.Count == 0, problems.Count > 0 ? problems[0] : null);
            check("schema defaults equal the node fallbacks",
                defaultsDisagree == 0, problems.Count > 0 ? string.Join("\n  ", problems) : null);

            // An enum slot is stored as an option index; if the reader could not convert it, the node would
            // quietly keep its default and the graph would look like it ignored the dropdown.
            var modeValues = new object[PCGLandscapeSamplerNode.ParameterCount];
            modeValues[4] = 1;
            var modeNode = PCGNodeRegistry.CreateNode(
                new PCGNode { TypeId = PCGLandscapeSamplerNode.Id, Values = modeValues }, 0, out var modeError);
            check("an enum slot written as an option index is honoured",
                modeNode != null && modeNode.DescribeParameters().Contains("HeightfieldInterpolated"),
                modeNode?.DescribeParameters() ?? modeError);

            var staleValues = new object[PCGLandscapeSamplerNode.ParameterCount];
            staleValues[4] = 77;
            var staleNode = PCGNodeRegistry.CreateNode(
                new PCGNode { TypeId = PCGLandscapeSamplerNode.Id, Values = staleValues }, 0, out _);
            check("an option index outside the enum falls back instead of inventing a mode",
                staleNode != null && staleNode.DescribeParameters().Contains("mode=RayCast"),
                staleNode?.DescribeParameters());

            // ---- compiler use of the schema -------------------------------------

            var mismatched = new PCGGraph
            {
                Name = "SchemaMismatch",
                Nodes = new[]
                {
                    new PCGNode
                    {
                        TypeId = PCGDensityFilterNode.Id,
                        Values = new object[] { new Vector3(1f, 2f, 3f), 0.9f, false },
                    },
                },
                Edges = Array.Empty<PCGEdge>(),
            };

            bool compiled = PCGGraphCompiler.TryCompile(mismatched, out var compiledGraph);
            string mismatchMessage = FindDiagnostic(compiledGraph, "Min Density");
            check("a value of the wrong kind in a parameter slot is a compile error",
                !compiled && mismatchMessage != null, mismatchMessage ?? "no diagnostic mentioned the slot");

            var surplus = new PCGGraph
            {
                Name = "SurplusSlot",
                Nodes = new[]
                {
                    new PCGNode { TypeId = PCGMergePointsNode.Id, Values = new object[] { 1.0f } },
                },
                Edges = Array.Empty<PCGEdge>(),
            };

            PCGGraphCompiler.TryCompile(surplus, out var surplusCompiled);
            string surplusMessage = FindDiagnostic(surplusCompiled, "declares no parameters");
            check("storing values on a parameterless node is reported as a warning",
                surplusMessage != null, surplusMessage ?? "no diagnostic raised");

            var clean = new PCGGraph
            {
                Name = "Clean",
                Nodes = new[]
                {
                    new PCGNode { TypeId = PCGDensityFilterNode.Id, Values = new object[] { 0.2f, 0.8f, false } },
                },
                Edges = Array.Empty<PCGEdge>(),
            };

            PCGGraphCompiler.TryCompile(clean, out var cleanCompiled);
            bool cleanHasParameterError = FindDiagnostic(cleanCompiled, "expects") != null;
            check("a correctly authored graph raises no parameter complaints", !cleanHasParameterError);

            ParameterTextTests(check);
        }

        /// <summary>
        /// Exercises the text conversion behind the graph editor's parameter fields.
        /// </summary>
        /// <remarks>
        /// The panel is the only place an authored value gets typed, and a slot that comes back as the wrong
        /// boxed type is silently ignored by the node's reader. The conversions are pure, so they are asserted
        /// here rather than only by someone opening the editor.
        /// </remarks>
        private static void ParameterTextTests(CheckDelegate check)
        {
            check("a float slot round-trips as a boxed float",
                RoundTrips(PCGParameterType.Float, 0.5f, out string floatError), floatError);

            check("an int slot round-trips as a boxed int",
                RoundTrips(PCGParameterType.Int, 7, out string intError), intError);

            check("a bool slot round-trips as a boxed bool",
                RoundTrips(PCGParameterType.Bool, true, out string boolError), boolError);

            check("a vector slot round-trips as a boxed Vector3",
                RoundTrips(PCGParameterType.Vector, new Vector3(1f, -2.5f, 300f), out string vectorError), vectorError);

            check("a colour slot round-trips as a boxed Color",
                RoundTrips(PCGParameterType.Color, new Color(0.1f, 0.2f, 0.3f, 1f), out string colorError), colorError);

            check("an asset slot round-trips as a boxed Guid",
                RoundTrips(PCGParameterType.Asset, new Guid("11112222-3333-4444-5555-666677778888"), out string assetError), assetError);

            check("an enum slot round-trips as an option index",
                RoundTrips(PCGParameterType.Enum, 2, out string enumError), enumError);

            // Height Filter's defaults are the unbounded range, and an infinite float renders as "∞" under
            // ordinary formatting - a field cannot type that back, so the formatter has its own spelling.
            bool posOk = RoundTrips(PCGParameterType.Float, float.PositiveInfinity, out string posError);
            bool negOk = RoundTrips(PCGParameterType.Float, float.NegativeInfinity, out string negError);
            check("an unbounded float default round-trips", posOk && negOk, posError ?? negError);

            bool emptyOk = PCGParameterText.TryParse(PCGParameterType.Float, new[] { string.Empty }, out var emptyValue, out string emptyError);
            check("an empty number field parses as zero without an error, so mid-typing is not flagged",
                emptyOk && emptyError == null && emptyValue is float && (float)emptyValue == 0.0f, emptyError);

            bool badRejected = !PCGParameterText.TryParse(PCGParameterType.Float, new[] { "abc" }, out _, out string badError);
            check("a non-numeric field is refused with a message", badRejected && badError != null, badError);

            bool shortVector = !PCGParameterText.TryParse(PCGParameterType.Vector, new[] { "1", "2" }, out _, out string shortError);
            check("a vector with too few components is refused", shortVector && shortError != null, shortError);

            bool negativeOption = !PCGParameterText.TryParse(PCGParameterType.Enum, new[] { "-1" }, out _, out string negativeError);
            check("a negative option index is refused", negativeOption && negativeError != null, negativeError);

            bool emptyGuid = PCGParameterText.TryParse(PCGParameterType.Asset, new[] { string.Empty }, out var guidValue, out string guidError) &&
                             guidValue is Guid && (Guid)guidValue == Guid.Empty;
            check("an empty asset field means no reference", emptyGuid, guidError);

            var grown = PCGParameterText.WithValue(new object[] { 1.0f, 2.0f }, 0, 5.0f);
            check("writing one slot keeps the other authored values",
                grown.Length == 2 && (float)grown[0] == 5.0f && (float)grown[1] == 2.0f);

            var extended = PCGParameterText.WithValue(Array.Empty<object>(), 3, true);
            check("writing past the end grows the array rather than dropping the value",
                extended.Length == 4 && (bool)extended[3] && extended[0] == null);

            // 显示名走中文表：名字与认识得出来的选项都换成中文，表里没有的（这里是 Heightfield）原样保留。
            check("an enum field's label lists the options with their indexes",
                PCGParameterText.LabelFor(new PCGNodeParameterInfo("Mode", PCGParameterType.Enum, 0,
                    new[] { "Ray Cast", "Heightfield" }, null)) == "模式（0=射线投射、1=Heightfield）");

            // The real table: every declared default must survive the round trip through its own field, which
            // is what a designer sees when a node is placed.
            PCGNodeRegistry.Clear();
            int count = PCG.Runtime.PCGNodeRegistration.RegisterAll();
            int mismatches = 0;
            string firstMismatch = null;

            foreach (var info in PCGNodeRegistry.All)
            {
                var schema = info.Parameters;
                for (int i = 0; schema != null && i < schema.Length; i++)
                {
                    var parameter = schema[i];
                    if (parameter == null)
                        continue;

                    if (RoundTrips(parameter.Kind, parameter.Default, out string error))
                        continue;

                    mismatches++;
                    firstMismatch ??= $"{info.TypeId}.{parameter.Name}: {error}";
                }
            }

            check($"every one of the {count} node types' defaults round-trips through its editor field",
                mismatches == 0, firstMismatch);

            ExposedMappingTests(check);
        }

            /// <summary>Exposed-parameter re-indexing: the mapping that keeps a volume's overrides pointed right.</summary>
        private static void ExposedMappingTests(CheckDelegate check)
        {
            var materialized = PCGParameterText.MaterializeExposed(
                new List<PCGParameterText.TrackedExposed>
                {
                    new PCGParameterText.TrackedExposed(10u, 0, "A", "Alpha", PCGParameterType.Float, 0.25f),
                    new PCGParameterText.TrackedExposed(12u, 3, "B", "Bravo", PCGParameterType.Vector, new Vector3(1f, 2f, 3f)),
                    new PCGParameterText.TrackedExposed(99u, 1, "Gone", "Gone", PCGParameterType.Bool, true),
                },
                new List<uint> { 10u, 11u, 12u });

            check("exposed parameters are re-indexed by node id at save time",
                materialized.Length == 2 &&
                materialized[0].NodeIndex == 0 && materialized[0].ValueIndex == 0 && materialized[0].Name == "A" &&
                materialized[1].NodeIndex == 2 && materialized[1].ValueIndex == 3 &&
                materialized[1].Type == PCGParameterType.Vector,
                materialized.Length.ToString());

            check("an exposed parameter on a deleted node is dropped rather than retargeted",
                materialized.Length == 2 && Array.FindIndex(materialized, d => d.Name == "Gone") < 0);

            check("materializing with no nodes yields no parameters",
                PCGParameterText.MaterializeExposed(null, null).Length == 0 &&
                PCGParameterText.MaterializeExposed(
                    new List<PCGParameterText.TrackedExposed>(), new List<uint>()).Length == 0);

            check("an exposed parameter name is built from the node type and the slot label",
                PCGParameterText.ExposedName("DensityFilter", 0,
                    new PCGNodeParameterInfo("Min Density", PCGParameterType.Float, 0.0f)) == "DensityFilter_Min_Density");
        }

        private static bool RoundTrips(PCGParameterType kind, object original, out string error)
        {
            error = null;

            var texts = PCGParameterText.Components(kind, original);

            if (texts == null || texts.Length != PCGParameterText.ComponentCount(kind))
            {
                error = $"{kind}: formatted {texts?.Length ?? 0} component(s), expected {PCGParameterText.ComponentCount(kind)}";
                return false;
            }

            if (!PCGParameterText.TryParse(kind, texts, out var value, out error))
                return false;

            // A null default (an unset string, say) is indistinguishable from an empty one in a field.
            if (original == null)
                return value is string text && text.Length == 0;

            if (value == null || value.GetType() != original.GetType())
            {
                error = $"{kind}: '{value?.GetType().Name ?? "null"}' came back for a {original.GetType().Name} default";
                return false;
            }

            return original.Equals(value) ||
                   (kind == PCGParameterType.Vector && AsVectorEqual(original, value)) ||
                   (kind == PCGParameterType.Color && AsColorEqual(original, value));
        }

        private static bool AsVectorEqual(object a, object b)
        {
            var x = (Vector3)a;
            var y = (Vector3)b;
            return x == y;
        }

        private static bool AsColorEqual(object a, object b)
        {
            var x = (Color)a;
            var y = (Color)b;
            return x.R == y.R && x.G == y.G && x.B == y.B && x.A == y.A;
        }

        private static string FindDiagnostic(PCGCompiledGraph compiled, string needle)
        {
            if (compiled?.Diagnostics == null)
                return null;

            foreach (var diagnostic in compiled.Diagnostics)
            {
                if (diagnostic.Message != null && diagnostic.Message.Contains(needle))
                    return diagnostic.Message;
            }

            return null;
        }

        /// <summary>
        /// A sampler stand-in that hands out a fixed data set, so the executor path can be exercised without
        /// a terrain actor. It exists only for these tests.
        /// </summary>
        private sealed class FixedSourceNode : PCGNodeBase
        {
            public const string Id = "TestFixedSource";

            public PCGData Data;

            public FixedSourceNode(PCGNodeRegistry.PCGNodeSerializationData data)
            {
            }

            public override string TypeId => Id;

            public override string DisplayName => "Fixed Source";

            public override PCGPinDefinition[] InputPins => Array.Empty<PCGPinDefinition>();

            public override PCGPinDefinition[] OutputPins => new[]
            {
                new PCGPinDefinition("Out", PCGPinDirection.Output),
            };

            public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs) => Data ?? PCGData.Empty;
        }
    }
}
