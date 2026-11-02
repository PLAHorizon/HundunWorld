// PCG Plugin - Executable tests for Split Grammar (docs/PCG_PLUGIN_DESIGN.md §5.1.29).
//
// A subdivision is the kind of code that looks right while being wrong: cutting a cell in half and putting the
// halves in the wrong place still produces the expected count, the expected sizes, and a picture that is only
// subtly off. So the checks below are the algebraic ones - children must tile their parent exactly - plus the
// direction nobody thinks to assert: that a rotated seed is cut along its own axes and not along world X.

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG;
using PCG.Execution;
using PCG.Nodes;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class GrammarTests
    {
        internal static void Run(CheckDelegate check)
        {
            RuleParsingTests(check);
            SubdivisionTests(check);
            RotationTests(check);
            ColumnAndSeedTests(check);
            FailureAndLimitTests(check);
        }

        private static void RuleParsingTests(CheckDelegate check)
        {
            bool ok = PCGSplitGrammarNode.TryParseRules("X:0.5,0.5", out var one, out string oneError);
            check("a one-rule table parses",
                ok && one.Length == 1 && one[0].Axis == 0 && one[0].Slices.Length == 2 &&
                Near(one[0].Slices[0], 0.5f) && Near(one[0].Slices[1], 0.5f),
                oneError ?? Describe(one));

            ok = PCGSplitGrammarNode.TryParseRules(" X : 1 , 3 ", out var scaled, out string scaledError);
            check("whitespace and case are tolerated, and ratios are proportions rather than absolutes",
                ok && scaled.Length == 1 && Near(scaled[0].Slices[0], 0.25f) && Near(scaled[0].Slices[1], 0.75f),
                scaledError ?? Describe(scaled));

            // A single ratio is how a table stops a branch, so it has to be a valid rule rather than an authoring
            // mistake - the twin of the rejection checks below.
            ok = PCGSplitGrammarNode.TryParseRules("Z:1", out var stop, out string stopError);
            check("a single ratio is accepted and means stop", ok && stop.Length == 1 && stop[0].Slices.Length == 1,
                stopError);

            ok = PCGSplitGrammarNode.TryParseRules("X:0.5,0.5; Z:0.1,0.2,0.7", out var two, out string twoError);
            check("depth selects the rule, and each rule keeps its own axis",
                ok && two.Length == 2 && two[0].Axis == 0 && two[1].Axis == 2 &&
                Near(two[1].Slices[0], 0.1f) && Near(two[1].Slices[2], 0.7f),
                twoError ?? Describe(two));

            check("an empty table is refused",
                !PCGSplitGrammarNode.TryParseRules("   ", out _, out string emptyError) && emptyError != null,
                emptyError);

            check("an unknown axis is refused with the axis named",
                !PCGSplitGrammarNode.TryParseRules("Q:1,1", out _, out string axisError) &&
                axisError != null && axisError.Contains("Q"),
                axisError);

            check("a rule with no ratios is refused",
                !PCGSplitGrammarNode.TryParseRules("X:", out _, out string shapeError) && shapeError != null,
                shapeError);

            check("a non-positive slice is refused",
                !PCGSplitGrammarNode.TryParseRules("X:0,1", out _, out string zeroError) && zeroError != null,
                zeroError);

            check("a slice that is not a number is refused",
                !PCGSplitGrammarNode.TryParseRules("X:a,b", out _, out string textError) && textError != null,
                textError);

            string tooMany = string.Join(";", System.Linq.Enumerable.Repeat("X:0.5,0.5", PCGSplitGrammarNode.MaxRules + 1));
            check("a table longer than the rule limit is refused",
                !PCGSplitGrammarNode.TryParseRules(tooMany, out _, out string manyError) && manyError != null,
                manyError);

            string tooWide = "X:" + string.Join(",", System.Linq.Enumerable.Repeat("1", PCGSplitGrammarNode.MaxSlices + 1));
            check("a rule asking for too many slices is refused",
                !PCGSplitGrammarNode.TryParseRules(tooWide, out _, out string wideError) && wideError != null,
                wideError);
        }

        private static void SubdivisionTests(CheckDelegate check)
        {
            // The primitive the whole node is built on, checked on its own before anything is built on it: a point
            // whose scale describes a cell must get a box the size of that cell.
            var sized = PCGPoint.Create(Vector3.Zero, 1.0f, 0);
            sized.Scale = new Float3(500.0f, 200.0f, 1000.0f);
            sized.Bounds = PCGPointTransform.MakeBox(
                sized.Position, PCGPointTransform.EnclosingExtents(
                    new Vector3(250.0f, 100.0f, 500.0f), sized.Rotation));

            check("a cell's box is built from the cell's size",
                Near(PCGPointTransform.HalfExtents(sized.Bounds).X, 250.0f) &&
                Near(PCGPointTransform.HalfExtents(sized.Bounds).Y, 100.0f) &&
                Near(PCGPointTransform.HalfExtents(sized.Bounds).Z, 500.0f),
                PCGPointTransform.HalfExtents(sized.Bounds).ToString());

            // The trap this node was written around, pinned so nobody "simplifies" the box back to it: UpdateBounds
            // recomputes from the transform's rotation and translation but NOT from its scale, so a leaf built that
            // way gets a one-unit box however large its cell is - and since extents are what pruning tests, the
            // mistake would show up as props disappearing rather than as a wrong number anywhere.
            var naive = PCGPoint.Create(Vector3.Zero, 1.0f, 0);
            naive.Scale = new Float3(500.0f, 200.0f, 1000.0f);
            naive.UpdateBounds(new Vector3(0.5f, 0.5f, 0.5f));

            check("UpdateBounds is not scale aware, which is why the box above is built by hand",
                Near(PCGPointTransform.HalfExtents(naive.Bounds).X, 0.5f) && Near(naive.Scale.X, 500.0f),
                PCGPointTransform.HalfExtents(naive.Bounds).ToString());

            // One cut in half: two cells, same count as a bad implementation would produce, so the placement and
            // the sizes are what is actually being checked here.
            var halves = Run("X:0.5,0.5", 1, 0.0f, new Vector3(1000.0f, 200.0f, 1000.0f), false, true, Points(1));
            check("a cell cut in half produces two equal children side by side",
                halves.Count == 2 &&
                Near(halves.Points[0].Position.X, -250.0f) && Near(halves.Points[1].Position.X, 250.0f) &&
                Near(halves.Points[0].Position.Y, 0.0f) && Near(halves.Points[1].Position.Z, 0.0f) &&
                Near(halves.Points[0].Scale.X, 500.0f) && Near(halves.Points[1].Scale.X, 500.0f),
                Describe(halves));

            // The invariant that catches a misplaced or mis-sized child: the children have to tile the parent, in
            // every axis, not just along the one being cut.
            check("the children tile their parent exactly",
                TileVolume(halves) == 1000.0f * 200.0f * 1000.0f &&
                Near(MinX(halves), -500.0f) && Near(MaxX(halves), 500.0f) &&
                Near(MinZ(halves), -500.0f) && Near(MaxZ(halves), 500.0f),
                $"{TileVolume(halves):F0} vs {1000.0f * 200.0f * 1000.0f:F0}");

            // Two depths: 2 cells then 4, and the second cut is on a different axis, which is what makes this a
            // table rather than a grid.
            var quarters = Run("X:0.5,0.5; Z:0.25,0.75", 2, 0.0f, new Vector3(1000.0f, 200.0f, 1000.0f), false, true, Points(1));
            check("a second depth cuts the children of the first",
                quarters.Count == 4 &&
                Near(quarters.Points[0].Position.X, -250.0f) && Near(quarters.Points[0].Position.Z, -375.0f) &&
                Near(quarters.Points[0].Scale.Z, 250.0f) &&
                Near(quarters.Points[3].Position.X, 250.0f) && Near(quarters.Points[3].Position.Z, 125.0f) &&
                Near(quarters.Points[3].Scale.Z, 750.0f),
                Describe(quarters));

            check("the depth-two leaves still tile their parent",
                TileVolume(quarters) == 1000.0f * 200.0f * 1000.0f,
                $"{TileVolume(quarters):F0}");

            // Proportions, not absolutes: 1,3 has to cut where 0.25,0.75 does.
            var thirds = Run("X:0.25,0.75", 1, 0.0f, new Vector3(1000.0f, 200.0f, 1000.0f), false, true, Points(1));
            check("two ways of writing the same proportions cut the same cell",
                Near(thirds.Points[0].Position.X, -375.0f) && Near(thirds.Points[1].Position.X, 125.0f) &&
                Near(thirds.Points[0].Scale.X, 250.0f) && Near(thirds.Points[1].Scale.X, 750.0f),
                Describe(thirds));

            // Depth zero is the twin of the checks above: the node must be a real subdivision when it is asked
            // for one, and exactly a copy when it is not.
            var untouched = Run("X:0.5,0.5", 0, 0.0f, new Vector3(1000.0f, 200.0f, 1000.0f), false, true, Points(1));
            check("a depth limit of zero leaves the seed alone",
                untouched.Count == 1 && Near(untouched.Points[0].Scale.X, 1000.0f) &&
                Near(untouched.Points[0].Position.X, 0.0f),
                Describe(untouched));

            // A stop rule ends the branch at that depth, so leaves of different depths coexist - a building whose
            // ground floor is split but whose attic is one room.
            var mixed = Run("X:0.5,0.5; Z:1", 4, 0.0f, new Vector3(1000.0f, 200.0f, 1000.0f), false, true, Points(1));
            check("a stop rule ends the branch instead of splitting it forever",
                mixed.Count == 2 && Near(mixed.Points[0].Scale.Z, 1000.0f),
                Describe(mixed));

            // The minimum cell size is the guard against a table that would otherwise shave slivers. It is measured
            // along the axis being cut, so a 1000-wide cell cut in half twice reaches 250 and stops before the third
            // cut: four leaves rather than sixteen. (The first version of this check used a one-rule table, where the
            // table itself stopped the recursion - the assertion passed while never exercising the floor at all.)
            var floored = Run(Repeated("X:0.5,0.5", 4), 4, 400.0f, new Vector3(1000.0f, 200.0f, 1000.0f), false,
                true, Points(1));

            var unfloored = Run(Repeated("X:0.5,0.5", 4), 4, 0.0f, new Vector3(1000.0f, 200.0f, 1000.0f), false,
                true, Points(1));

            check("a minimum cell size stops the split before the cells get thinner than it",
                floored.Count == 4 && unfloored.Count == 16 && Near(floored.Points[0].Scale.X, 250.0f),
                $"{floored.Count} floored vs {unfloored.Count} unfloored");

            // The floor is about the axis being cut, not about the cell's longest side: a cell 100 wide and 10000
            // deep can still be cut along X. Pinned because the "longest side" reading is the tempting one and it
            // makes the floor unreachable for any single-axis rule.
            var alongAxis = Run(Repeated("X:0.5,0.5", 4), 4, 60.0f,
                new Vector3(100.0f, 10000.0f, 100.0f), false, true, Points(1));

            check("the floor is measured along the cut axis rather than the longest side",
                alongAxis.Count == 2, alongAxis.Count.ToString());

            // Seeds whose scale cannot describe a cell must fall back to the configured cell, not to a one-unit
            // cube. The fixture's scale is zeroed for exactly that reason: 1 is a usable cell size, so a fixture
            // that left it alone would be testing the other branch.
            var collapsed = Points(1);
            collapsed.Points[0].Scale = Float3.Zero;

            var probe = new PCGSplitGrammarNode(Serialization(new object[]
            {
                "X:0.5,0.5", 1, 0.0f, new Vector3(800.0f, 100.0f, 100.0f), true, true,
            }));

            var fromParam = probe.Execute(Context(), new[] { collapsed });
            check("a seed whose scale cannot describe a cell falls back to the configured cell size",
                fromParam.Count == 2 && Near(fromParam.Points[0].Scale.X, 400.0f) &&
                Near(fromParam.Points[1].Position.X, 200.0f),
                $"seedScale={collapsed.Points[0].Scale} cell={probe.CellSize} | {Describe(fromParam)}");

            var fromScale = Run("X:0.5,0.5", 1, 0.0f, new Vector3(800.0f, 100.0f, 100.0f), true, true, ScaledPoints(1, 600.0f));
            check("a seed that carries a scale uses it as its cell",
                fromScale.Count == 2 && Near(fromScale.Points[0].Scale.X, 300.0f) &&
                Near(fromScale.Points[1].Position.X, 150.0f),
                Describe(fromScale));

            // No input at all: the generation bounds become the seed, which is how a diagram starts. The bounds
            // here span -2000..-500, so the configured 400-unit cell halves around their centre at -1250.
            var fromBounds = Run("X:0.5,0.5", 1, 0.0f, new Vector3(400.0f, 100.0f, 400.0f), false, true, null);
            check("with no input the node subdivides a cell at the generation bounds centre",
                fromBounds.Count == 2 && Near(fromBounds.Points[0].Position.X, -1350.0f) &&
                Near(fromBounds.Points[1].Position.X, -1150.0f),
                Describe(fromBounds));

            // Determinism, and the reason it is not on the seed: the same table over the same cells must lay out
            // the same structure, or a partitioned world would draw different buildings in different cells.
            var first = Run("X:0.5,0.5; Z:0.5,0.5", 2, 0.0f, new Vector3(1000.0f, 200.0f, 1000.0f), false, true, Points(3));
            var second = Run("X:0.5,0.5; Z:0.5,0.5", 2, 0.0f, new Vector3(1000.0f, 200.0f, 1000.0f), false, true, Points(3));

            bool identical = first.Count == second.Count;

            for (int i = 0; identical && i < first.Count; i++)
            {
                identical = Near(first.Points[i].Position.X, second.Points[i].Position.X) &&
                            Near(first.Points[i].Position.Z, second.Points[i].Position.Z) &&
                            Near(first.Points[i].Scale.X, second.Points[i].Scale.X);
            }

            check("two runs of the same table produce the same structure", identical,
                $"{first.Count} vs {second.Count} cell(s)");

            check("three seeds produce three times the structure",
                first.Count == 12, first.Count.ToString());
        }

        private static void RotationTests(CheckDelegate check)
        {
            // The direction that a world-frame implementation would get wrong while passing every check above: a
            // seed turned 90 degrees must be cut along its own X, which points along world -Z.
            var rotated = Run("X:0.5,0.5", 1, 0.0f, new Vector3(1000.0f, 200.0f, 1000.0f), false, true,
                RotatedPoints(1, 90.0f));

            check("a rotated seed is cut along its own axis, not along world X",
                rotated.Count == 2 &&
                Math.Abs(rotated.Points[0].Position.X) < 1e-3f && Math.Abs(rotated.Points[1].Position.X) < 1e-3f &&
                Near(Math.Abs(rotated.Points[0].Position.Z), 250.0f) &&
                Near(Math.Abs(rotated.Points[1].Position.Z), 250.0f),
                Describe(rotated));

            check("the leaves keep the seed's rotation, so the cut cells stay aligned with it",
                Math.Abs(rotated.Points[0].Rotation.Y) > 0.1f,
                rotated.Points[0].Rotation.ToString());

            // Their boxes must follow that rotation rather than staying axis aligned around it - the check that
            // catches bounds computed without the transform.
            bool boxesFollow = rotated.Count == 2;

            for (int i = 0; boxesFollow && i < rotated.Count; i++)
            {
                var box = rotated.Points[i].Bounds;
                float spanX = box.Maximum.X - box.Minimum.X;
                float spanZ = box.Maximum.Z - box.Minimum.Z;

                // A 90 degree turn swaps the cell's X and Z extents in world space; a box that ignored the
                // rotation would report 500 across and 1000 up the other way.
                boxesFollow = Near(spanX, 1000.0f) && Near(spanZ, 500.0f);
            }

            check("a rotated leaf's bounds are rotated with it",
                boxesFollow, rotated.Count > 0 ? rotated.Points[0].Bounds.ToString() : "no cells");
        }

        private static void ColumnAndSeedTests(CheckDelegate check)
        {
            var layered = Run("X:0.5,0.5; Z:0.5,0.5", 2, 0.0f, new Vector3(1000.0f, 200.0f, 1000.0f), false, true, Points(1));

            var column = layered.SharedAttributes?.TryGetFloatArray(PCGSplitGrammarNode.DepthColumn);
            check("the depth column records which cut each leaf came from",
                column != null && column.Length == 4 &&
                Near(column[0], 2.0f) && Near(column[1], 2.0f) && Near(column[2], 2.0f) && Near(column[3], 2.0f),
                column == null ? "no column" : string.Join(",", column));

            var mixed = Run("X:0.5,0.5; Z:1", 4, 0.0f, new Vector3(1000.0f, 200.0f, 1000.0f), false, true, Points(1));
            var mixedColumn = mixed.SharedAttributes?.TryGetFloatArray(PCGSplitGrammarNode.DepthColumn);
            check("leaves that stopped at different depths write different values",
                mixedColumn != null && mixedColumn.Length == 2 && Near(mixedColumn[0], 1.0f) && Near(mixedColumn[1], 1.0f),
                mixedColumn == null ? "no column" : string.Join(",", mixedColumn));

            var silent = Run("X:0.5,0.5", 1, 0.0f, new Vector3(1000.0f, 200.0f, 1000.0f), false, false, Points(1));
            check("turning the column off writes no column at all",
                silent.SharedAttributes?.TryGetFloatArray(PCGSplitGrammarNode.DepthColumn) == null);

            var seeded = Run("X:0.5,0.5", 1, 0.0f, new Vector3(1000.0f, 200.0f, 1000.0f), false, true, Points(1));
            check("each leaf gets its own seed so downstream variation stays per leaf",
                seeded.Points[0].Seed != seeded.Points[1].Seed,
                $"{seeded.Points[0].Seed} vs {seeded.Points[1].Seed}");

            var dense = Run("X:0.5,0.5", 1, 0.0f, new Vector3(1000.0f, 200.0f, 1000.0f), false, true, DensePoints(1, 0.25f));
            check("a leaf inherits its seed's density, so a density filter upstream still applies",
                Near(dense.Points[0].Density, 0.25f) && Near(dense.Points[1].Density, 0.25f),
                dense.Points[0].Density.ToString("F3"));
        }

        private static void FailureAndLimitTests(CheckDelegate check)
        {
            // A bad table must not empty the graph: the convention every other node follows is to report and pass
            // the input through, and the positive twin is the same node with a valid table.
            var seeds = Points(2);

            var node = new PCGSplitGrammarNode(Serialization(new object[]
            {
                "Q:oops", 2, 0.0f, new Vector3(1000.0f, 200.0f, 1000.0f), false, true,
            }));

            var passed = node.Execute(Context(), new[] { seeds });
            check("a table that cannot be read reports why and passes the input through",
                passed.Count == 2 && !string.IsNullOrEmpty(node.LastError) && node.LastError.Contains("Q:oops"),
                node.LastError);

            var good = new PCGSplitGrammarNode(Serialization(new object[]
            {
                "X:0.5,0.5", 1, 0.0f, new Vector3(1000.0f, 200.0f, 1000.0f), false, true,
            }));

            var built = good.Execute(Context(), new[] { seeds });
            check("the same node with a readable table reports nothing",
                built.Count == 4 && string.IsNullOrEmpty(good.LastError),
                good.LastError ?? built.Count.ToString());

            // The budget: a four-way cut nested sixteen deep is 4^16 cells, and a node that tried is a node that
            // hangs the editor. It has to stop and say so. The rule is repeated once per depth because a table is
            // read by depth - a single rule cuts once and then every cell is a leaf.
            var hungry = new PCGSplitGrammarNode(Serialization(new object[]
            {
                Repeated("X:0.25,0.25,0.25,0.25", PCGSplitGrammarNode.MaxDepthLimit),
                PCGSplitGrammarNode.MaxDepthLimit, 0.0f,
                new Vector3(1000.0f, 200.0f, 1000.0f), false, false,
            }));

            var bounded = hungry.Execute(Context(), new[] { Units(1) });
            check("a table that would explode stops at the budget and says so",
                bounded.Count == PCGSplitGrammarNode.LeafBudget &&
                !string.IsNullOrEmpty(hungry.LastError) && hungry.LastError.Contains("budget"),
                $"{bounded.Count} cell(s): {hungry.LastError}");

            // The twin that keeps the budget check honest: a table of the same depth under the budget is not
            // truncated, so "it stopped" is a real limit rather than a node that always reports one.
            var deep = new PCGSplitGrammarNode(Serialization(new object[]
            {
                Repeated("X:0.5,0.5", PCGSplitGrammarNode.MaxDepthLimit),
                PCGSplitGrammarNode.MaxDepthLimit, 0.0f,
                new Vector3(1000.0f, 200.0f, 1000.0f), false, false,
            }));

            var deepResult = deep.Execute(Context(), new[] { Units(1) });
            check("a deep but affordable table is not truncated",
                deepResult.Count == 1 << PCGSplitGrammarNode.MaxDepthLimit && string.IsNullOrEmpty(deep.LastError),
                $"{deepResult.Count} cell(s): {deep.LastError}");
        }

        // ---- fixtures -------------------------------------------------------

        private static PCGNodeRegistry.PCGNodeSerializationData Serialization(object[] values) =>
            new PCGNodeRegistry.PCGNodeSerializationData(PCGSplitGrammarNode.Id, values, 0);

        /// <summary>One rule repeated once per depth, which is how a table keeps cutting below the first level.</summary>
        private static string Repeated(string rule, int times)
        {
            var parts = new string[times];

            for (int i = 0; i < times; i++)
                parts[i] = rule;

            return string.Join(";", parts);
        }

        private static PCGExecutionContext Context() =>
            new PCGExecutionContext
            {
                Seed = 20261008,
                GenerationBounds = new BoundingBox(new Vector3(-2000.0f, -100.0f, -2000.0f),
                                                   new Vector3(-500.0f, 100.0f, -2000.0f)),
            };

        private static PCGData Run(
            string rules, int maxDepth, float minCell, Vector3 cell, bool useScale, bool writeDepth, PCGData input)
        {
            var node = new PCGSplitGrammarNode(Serialization(new object[]
            {
                rules, maxDepth, minCell, cell, useScale, writeDepth,
            }));

            return node.Execute(Context(), new[] { input });
        }

        /// <summary>Seeds at the origin with a scale that cannot describe a cell.</summary>
        private static PCGData Points(int count)
        {
            var data = PCGData.Create(count);

            for (int i = 0; i < count; i++)
            {
                data.Points[i] = PCGPoint.Create(new Vector3(i * 10.0f, 0.0f, 0.0f), 1.0f, i);
                data.Points[i].UpdateBounds(Vector3.One);
            }

            return data;
        }

        private static PCGData Units(int count)
        {
            var data = PCGData.Create(count);

            for (int i = 0; i < count; i++)
            {
                data.Points[i] = PCGPoint.Create(Vector3.Zero, 1.0f, i);
                data.Points[i].UpdateBounds(new Vector3(0.5f, 0.5f, 0.5f));
            }

            return data;
        }

        private static PCGData ScaledPoints(int count, float size)
        {
            var data = PCGData.Create(count);

            for (int i = 0; i < count; i++)
            {
                data.Points[i] = PCGPoint.Create(Vector3.Zero, 1.0f, i);
                data.Points[i].Scale = new Float3(size, size, size);
                data.Points[i].UpdateBounds(new Vector3(0.5f, 0.5f, 0.5f));
            }

            return data;
        }

        private static PCGData DensePoints(int count, float density)
        {
            var data = PCGData.Create(count);

            for (int i = 0; i < count; i++)
            {
                data.Points[i] = PCGPoint.Create(Vector3.Zero, density, i);
                data.Points[i].UpdateBounds(new Vector3(0.5f, 0.5f, 0.5f));
            }

            return data;
        }

        private static PCGData RotatedPoints(int count, float yawDegrees)
        {
            var data = PCGData.Create(count);

            // Degrees in, through the helper that already knows the engine's convention - constructing the
            // quaternion by hand here would be a second opinion about whether the engine takes degrees or radians.
            var rotation = PCGPointTransform.EulerDegrees(new Vector3(0.0f, yawDegrees, 0.0f));

            for (int i = 0; i < count; i++)
            {
                data.Points[i] = PCGPoint.Create(Vector3.Zero, 1.0f, i);
                data.Points[i].Rotation = rotation;
                data.Points[i].UpdateBounds(new Vector3(0.5f, 0.5f, 0.5f));
            }

            return data;
        }

        // ---- measurement ----------------------------------------------------

        private static float TileVolume(PCGData data)
        {
            float total = 0.0f;

            for (int i = 0; i < data.Count; i++)
                total += data.Points[i].Scale.X * data.Points[i].Scale.Y * data.Points[i].Scale.Z;

            return total;
        }

        private static float MinX(PCGData data) => MinMaxX(data, true);
        private static float MaxX(PCGData data) => MinMaxX(data, false);
        private static float MinZ(PCGData data) => MinMaxZ(data, true);
        private static float MaxZ(PCGData data) => MinMaxZ(data, false);

        private static float MinMaxX(PCGData data, bool min)
        {
            float result = min ? float.MaxValue : float.MinValue;

            for (int i = 0; i < data.Count; i++)
            {
                float half = data.Points[i].Scale.X * 0.5f;
                float edge = data.Points[i].Position.X + (min ? -half : half);
                result = min ? Math.Min(result, edge) : Math.Max(result, edge);
            }

            return result;
        }

        private static float MinMaxZ(PCGData data, bool min)
        {
            float result = min ? float.MaxValue : float.MinValue;

            for (int i = 0; i < data.Count; i++)
            {
                float half = data.Points[i].Scale.Z * 0.5f;
                float edge = data.Points[i].Position.Z + (min ? -half : half);
                result = min ? Math.Min(result, edge) : Math.Max(result, edge);
            }

            return result;
        }

        private static bool Near(float a, float b, float tolerance = 1e-3f) => Math.Abs(a - b) <= tolerance;

        private static string Describe(PCGData data)
        {
            if (data == null)
                return "null";

            var parts = new List<string>();

            for (int i = 0; i < Math.Min(data.Count, 6); i++)
            {
                var point = data.Points[i];
                parts.Add($"({point.Position.X:F1},{point.Position.Y:F1},{point.Position.Z:F1})");
            }

            return $"{data.Count} cell(s): {string.Join(" ", parts)}";
        }

        private static string Describe(PCGSplitGrammarNode.Rule[] rules)
        {
            if (rules == null)
                return "no rules";

            var parts = new List<string>();

            foreach (var rule in rules)
                parts.Add($"{"XYZ"[rule.Axis]}:{string.Join(",", Array.ConvertAll(rule.Slices, s => s.ToString("F2")))}");

            return string.Join("; ", parts);
        }
    }
}
