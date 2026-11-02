// PCG Plugin - Executable tests for the spline data type.
//
// The spline is the second data kind a graph can carry, and all of it is plain arithmetic - which means its
// behaviour can be pinned offline: lengths, positions at a distance, tangents, and the two sampling modes. The
// cases worth stating are the degenerate ones (a path that repeats a vertex, a path with one vertex) where a
// naive implementation divides by a zero-length segment and hands back NaN.

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG;
using PCG.Execution;
using PCG.Nodes;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class SplineTests
    {
        private static CheckDelegate _check;

        // Condition first, so an assertion reads as "this holds, because ...".
        private static void check(bool ok, string what, string detail = null) => _check(what, ok, detail);

        internal static void Run(CheckDelegate callback)
        {
            _check = callback;

            LengthTests();
            SamplingTests();
            DegenerateTests();
            PropagationTests();
            MeshLayoutTests();
            CurveTests();
            NodeTests();
            TestArcLengthIndexConvention();
            TestArcLengthInversion();
        }

        private static void LengthTests()
        {
            var elbow = Elbow();

            check(Math.Abs(elbow.Length - 20.0f) < 1e-4f,
                "a right-angle path is as long as its two segments",
                $"{elbow.Length:0.###}");

            check(Close(elbow.PositionAt(0.0f), new Vector3(0.0f, 0.0f, 0.0f)) &&
                      Close(elbow.PositionAt(5.0f), new Vector3(5.0f, 0.0f, 0.0f)) &&
                      Close(elbow.PositionAt(10.0f), new Vector3(10.0f, 0.0f, 0.0f)) &&
                      Close(elbow.PositionAt(15.0f), new Vector3(10.0f, 5.0f, 0.0f)) &&
                      Close(elbow.PositionAt(20.0f), new Vector3(10.0f, 10.0f, 0.0f)),
                "positions along the path follow the arc length, corner included");

            // Past the end the path stops rather than extrapolating: a sample beyond a path is a graph asking for
            // something that does not exist, and inventing a straight line there would hide the mistake.
            check(Close(elbow.PositionAt(100.0f), new Vector3(10.0f, 10.0f, 0.0f)) &&
                      Close(elbow.PositionAt(-50.0f), new Vector3(0.0f, 0.0f, 0.0f)),
                "a distance beyond either end clamps to that end");

            var tangent = elbow.TangentAt(5.0f);
            check(Close(tangent, new Vector3(1.0f, 0.0f, 0.0f)) && Math.Abs(tangent.Length - 1.0f) < 1e-4f,
                "the tangent is the unit direction of the segment the distance falls in",
                $"{tangent}");

            // Closed: the last vertex joins the first, so the path is longer and its end is its start.
            var loop = new PCGSpline(new[]
            {
                new Vector3(0.0f, 0.0f, 0.0f),
                new Vector3(10.0f, 0.0f, 0.0f),
                new Vector3(10.0f, 0.0f, 10.0f),
            }, true);

            check(Math.Abs(loop.Length - (10.0f + 10.0f + MathF.Sqrt(200.0f))) < 1e-3f,
                "a closed path includes the segment back to its start",
                $"{loop.Length:0.###}");
            check(Close(loop.PositionAt(loop.Length), loop.PositionAt(0.0f)) &&
                      Close(loop.PositionAt(loop.Length + 5.0f), loop.PositionAt(5.0f)),
                "a distance past the end of a closed path wraps instead of clamping");
        }

        private static void SamplingTests()
        {
            var elbow = Elbow();

            var spaced = elbow.SampleBySpacing(5.0f);
            check(spaced.Count == 5 &&
                      Close(spaced[0], new Vector3(0.0f, 0.0f, 0.0f)) &&
                      Close(spaced[4], new Vector3(10.0f, 10.0f, 0.0f)),
                "sampling by spacing starts at the beginning and lands on the end",
                $"{spaced.Count} sample(s)");

            // A spacing that does not divide the length still gets the end point, because a path that stops short
            // reads as a bug rather than as a spacing choice.
            var ragged = elbow.SampleBySpacing(7.0f);
            check(ragged.Count == 4 &&
                      Close(ragged[ragged.Count - 1], new Vector3(10.0f, 10.0f, 0.0f)),
                "an uneven spacing adds the end point it would otherwise miss",
                $"{ragged.Count} sample(s)");

            var byCount = elbow.SampleByCount(3);
            check(byCount.Count == 3 &&
                      Close(byCount[0], new Vector3(0.0f, 0.0f, 0.0f)) &&
                      Close(byCount[1], new Vector3(10.0f, 0.0f, 0.0f)) &&
                      Close(byCount[2], new Vector3(10.0f, 10.0f, 0.0f)),
                "sampling by count distributes evenly along the arc length",
                $"{byCount.Count} sample(s)");

            var loop = new PCGSpline(new[]
            {
                new Vector3(0.0f, 0.0f, 0.0f),
                new Vector3(10.0f, 0.0f, 0.0f),
                new Vector3(10.0f, 0.0f, 10.0f),
            }, true);

            var loopSpaced = loop.SampleBySpacing(10.0f);
            check(loopSpaced.Count == 4,
                "a closed path's samples do not repeat its start at the end",
                $"{loopSpaced.Count} sample(s)");
        }

        private static void DegenerateTests()
        {
            // A single vertex, no vertices, and a repeated vertex: the three ways a polyline can have no
            // direction, and the three places a naive implementation returns NaN.
            var single = new PCGSpline(new[] { new Vector3(5.0f, 5.0f, 5.0f) }, false);

            check(!single.IsUsable && single.Length < 1e-4f &&
                      Close(single.PositionAt(10.0f), new Vector3(5.0f, 5.0f, 5.0f)) &&
                      single.SampleBySpacing(5.0f).Count == 0,
                "a single-vertex path has no length, samples nothing, and still answers a position",
                $"usable={single.IsUsable}");

            var empty = new PCGSpline(Array.Empty<Vector3>(), false);
            check(!empty.IsUsable && empty.SampleBySpacing(5.0f).Count == 0 &&
                      Close(empty.PositionAt(0.0f), Vector3.Zero) &&
                      Close(empty.TangentAt(0.0f), Vector3.Forward),
                "an empty path is empty rather than a crash");

            var repeated = new PCGSpline(new[]
            {
                new Vector3(0.0f, 0.0f, 0.0f),
                new Vector3(0.0f, 0.0f, 0.0f),
                new Vector3(10.0f, 0.0f, 0.0f),
            }, false);

            var tangent = repeated.TangentAt(0.0f);
            check(Math.Abs(repeated.Length - 10.0f) < 1e-4f &&
                      !float.IsNaN(tangent.X) && !float.IsNaN(tangent.Y) && !float.IsNaN(tangent.Z) &&
                      Math.Abs(tangent.Length - 1.0f) < 1e-4f,
                "a repeated vertex adds no length and yields a finite unit tangent, not a NaN",
                $"length {repeated.Length:0.###}, tangent {tangent}");

            check(repeated.SampleBySpacing(0.0f).Count == 0 && repeated.SampleByCount(0).Count == 0,
                "a zero spacing and a zero count both yield nothing instead of looping forever");
        }

        // ---- rounding a path's corners ----------------------------------------

        private static void CurveTests()
        {
            var path = new[]
            {
                new Vector3(0.0f, 0.0f, 0.0f),
                new Vector3(10.0f, 0.0f, 0.0f),
                new Vector3(10.0f, 0.0f, 10.0f),
                new Vector3(20.0f, 0.0f, 10.0f),
            };

            var smooth = PCGSplineCurves.Subdivide(path, false, 4);

            check(smooth.Length == 13, "subdividing three segments four ways gives 13 vertices", smooth.Length.ToString());
            check(Close(smooth[0], path[0]) && Close(smooth[smooth.Length - 1], path[3]),
                "subdivision leaves both ends exactly where the waypoints were");
            check(Close(smooth[4], path[1]) && Close(smooth[8], path[2]),
                "every control point survives as a vertex of the finer path",
                $"{smooth[4]} / {smooth[8]}");

            // A straight, evenly spaced run is reproduced exactly - that is the property that makes swapping the
            // finer path in safe, and the reason the caveat about uneven spacing is worth stating.
            var straight = new[]
            {
                new Vector3(-10.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 0.0f),
                new Vector3(10.0f, 0.0f, 0.0f), new Vector3(20.0f, 0.0f, 0.0f),
            };
            var straightFine = PCGSplineCurves.Subdivide(straight, false, 5);

            bool onLine = true;
            bool monotonic = true;
            for (int i = 0; i < straightFine.Length; i++)
            {
                if (Math.Abs(straightFine[i].Y) > 1e-4f || Math.Abs(straightFine[i].Z) > 1e-4f)
                    onLine = false;
                if (i > 0 && straightFine[i].X < straightFine[i - 1].X - 1e-4f)
                    monotonic = false;
            }

            check(onLine, "an evenly spaced straight run stays on its line rather than bulging");
            check(monotonic, "the finer path never doubles back on a straight run");
            check(Close(straightFine[7], new Vector3(4.0f, 0.0f, 0.0f)),
                "a point two fifths along the first segment sits at the hand-worked position",
                straightFine[7].ToString());

            // A corner is where the curve earns its keep. This fixture is deliberately asymmetric, so the
            // interpolated midpoint differs from the straight chord's midpoint (0.5, 0, 0).
            var corner = PCGSplineCurves.Interpolate(
                new Vector3(0.0f, 0.0f, -2.0f), new Vector3(0.0f, 0.0f, 0.0f),
                new Vector3(1.0f, 0.0f, 0.0f), new Vector3(1.0f, 0.0f, 0.5f), 0.5f);

            check(Close(corner, new Vector3(0.5f, 0.0f, 0.09375f)),
                "the midpoint on a corner is the hand-worked value, not the straight chord's",
                corner.ToString());

            // A closed path wraps its neighbours, so the seam is a corner like any other and there is no trailing
            // vertex duplicating the first.
            var square = new[]
            {
                new Vector3(0.0f, 0.0f, 0.0f), new Vector3(10.0f, 0.0f, 0.0f),
                new Vector3(10.0f, 0.0f, 10.0f), new Vector3(0.0f, 0.0f, 10.0f),
            };
            var loop = PCGSplineCurves.Subdivide(square, true, 3);

            check(loop.Length == 12, "a closed path subdivides every segment with no extra vertex", loop.Length.ToString());
            check(Close(loop[0], square[0]) && Close(loop[3], square[1]) &&
                  Close(loop[6], square[2]) && Close(loop[9], square[3]),
                "the closed path still passes through all four corners");

            check(PCGSplineCurves.Subdivide(path, false, 1).Length == path.Length,
                "a subdivision of one leaves the polyline alone");
            check(PCGSplineCurves.Subdivide(new[] { Vector3.Zero }, false, 8).Length == 1,
                "a path with one vertex has nothing to subdivide");

            // The node path: the parameter has to actually reach the curve, or the tests above would be proving
            // the helper works while the graph quietly ignores it.
            var line = LineData(4, 10.0f);
            var node = new PCGCreateSplineNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGCreateSplineNode.Id,
                new object[] { (int)PCGCreateSplineNode.VertexOrder.InputOrder, false, false, 4 }, 0));
            var curved = Run(node, line);

            check(curved.PrimaryOutput.Splines != null && curved.PrimaryOutput.Splines[0].Points.Length == 13,
                "the node's curve parameter reaches the produced spline",
                curved.PrimaryOutput.Splines?[0].Points.Length.ToString() ?? "no spline");
            check(curved.PrimaryOutput.Splines != null &&
                  Math.Abs(curved.PrimaryOutput.Splines[0].Length - 30.0f) < 1e-3f,
                "subdividing a straight run does not change its length",
                curved.PrimaryOutput.Splines?[0].Length.ToString("0.###") ?? "no spline");

            var plain = Create(line, PCGCreateSplineNode.VertexOrder.InputOrder, false, false);
            check(plain.PrimaryOutput.Splines != null && plain.PrimaryOutput.Splines[0].Points.Length == 4,
                "the default is still the plain four-vertex polyline",
                plain.PrimaryOutput.Splines?[0].Points.Length.ToString() ?? "no spline");
        }

        // ---- the two nodes that move curves in and out of a graph -------------

        private static void NodeTests()
        {
            PCGNodeRegistry.Clear();
            PCG.Runtime.PCGNodeRegistration.RegisterAll();

            try
            {
                var line = LineData(4, 10.0f);

                var curve = Create(line, PCGCreateSplineNode.VertexOrder.InputOrder, false, false);
                check(curve.PrimaryOutput.Count == 0 && curve.PrimaryOutput.Splines?.Length == 1,
                    "folding points into a curve replaces them by default",
                    $"{curve.PrimaryOutput.Count} point(s), {curve.PrimaryOutput.Splines?.Length ?? 0} curve(s)");

                check(Math.Abs(curve.PrimaryOutput.Splines[0].Length - 30.0f) < 1e-3f,
                    "the curve's length is the path through the points, measured by hand here",
                    $"{curve.PrimaryOutput.Splines[0].Length:0.###}");

                var kept = Create(line, PCGCreateSplineNode.VertexOrder.InputOrder, false, true);
                check(kept.PrimaryOutput.Count == 4 && kept.PrimaryOutput.Splines?.Length == 1,
                    "keeping the points gives both the layer and the curve",
                    $"{kept.PrimaryOutput.Count} point(s)");

                var closed = Create(line, PCGCreateSplineNode.VertexOrder.InputOrder, true, false);
                check(Math.Abs(closed.PrimaryOutput.Splines[0].Length - 60.0f) < 1e-3f,
                    "closing the path adds the segment back to the first vertex (30 out plus 30 back)",
                    $"{closed.PrimaryOutput.Splines[0].Length:0.###}");

                var single = Create(LineData(1, 10.0f), PCGCreateSplineNode.VertexOrder.InputOrder, false, false);
                check(single.PrimaryOutput.Count == 1 && single.PrimaryOutput.Splines == null,
                    "one point has no path: it passes through with no curve invented for it",
                    $"{single.PrimaryOutput.Count} point(s)");

                // Greedy ordering, on a set whose input order zig-zags: the walk from the first element is shorter
                // than the path the input order describes.
                var zigzag = Data(new[]
                {
                    PCGPoint.Create(new Vector3(20.0f, 0.0f, 0.0f), 1.0f, 0),
                    PCGPoint.Create(new Vector3(0.0f, 0.0f, 0.0f), 1.0f, 1),
                    PCGPoint.Create(new Vector3(30.0f, 0.0f, 0.0f), 1.0f, 2),
                    PCGPoint.Create(new Vector3(10.0f, 0.0f, 0.0f), 1.0f, 3),
                });

                var asGiven = Create(zigzag, PCGCreateSplineNode.VertexOrder.InputOrder, false, false);
                var walked = Create(zigzag, PCGCreateSplineNode.VertexOrder.NearestNeighbour, false, false);

                check(walked.PrimaryOutput.Splines[0].Length < asGiven.PrimaryOutput.Splines[0].Length,
                    "the nearest-neighbour walk is shorter than the order it was handed",
                    $"{walked.PrimaryOutput.Splines[0].Length:0.#} vs {asGiven.PrimaryOutput.Splines[0].Length:0.#}");

                // The walk starts at the first point and visits each of the others exactly once. Which of two
                // equidistant candidates it takes is decided by index (stated in the node), so the assertion is
                // about the set of vertices and the length rather than about a particular route through a tie.
                var visited = walked.PrimaryOutput.Splines[0].Points;
                var expected = new HashSet<string>(StringComparer.Ordinal)
                {
                    "20|0|0", "0|0|0", "30|0|0", "10|0|0",
                };
                int matched = 0;

                for (int i = 0; i < visited.Length; i++)
                {
                    string key = $"{visited[i].X:0.###}|{visited[i].Y:0.###}|{visited[i].Z:0.###}";

                    if (expected.Contains(key))
                        matched++;
                }

                check(matched == 4 && Close(visited[0], new Vector3(20.0f, 0.0f, 0.0f)) &&
                          Math.Abs(walked.PrimaryOutput.Splines[0].Length - 40.0f) < 1e-3f,
                    "the walk starts where the set starts and visits every point once",
                    $"{matched}/4 vertices, {walked.PrimaryOutput.Splines[0].Length:0.#} long");

                // Sampling the curve: the elbow that is 20 units long gets a point every 5.
                var elbowCurve = Create(ElbowData(), PCGCreateSplineNode.VertexOrder.InputOrder, false, false);
                var sampled = Sample(elbowCurve.PrimaryOutput, PCGSampleSplineNode.SampleMode.BySpacing, 5.0f, 0,
                    0.0f, false, 0.0f);

                check(sampled.PrimaryOutput.Count == 5 &&
                          Close(sampled.PrimaryOutput.Points[0].Position, Vector3.Zero) &&
                          Close(sampled.PrimaryOutput.Points[4].Position, new Vector3(10.0f, 10.0f, 0.0f)),
                    "sampling a curve by spacing walks it end to end",
                    $"{sampled.PrimaryOutput.Count} point(s)");

                check(sampled.PrimaryOutput.Splines?.Length == 1,
                    "the curve travels with its points, so it can be sampled again downstream",
                    $"{sampled.PrimaryOutput.Splines?.Length ?? 0} curve(s)");

                // Height offset lifts the whole run off the path.
                var lifted = Sample(elbowCurve.PrimaryOutput, PCGSampleSplineNode.SampleMode.BySpacing, 5.0f, 0,
                    10.0f, false, 0.0f);
                check(Math.Abs(lifted.PrimaryOutput.Points[0].Position.Y - 10.0f) < 1e-3f,
                    "a height offset lifts every sample off the curve",
                    $"{lifted.PrimaryOutput.Points[0].Position}");

                // Aligning to the tangent: the first sample of the elbow faces along its first segment, +X.
                var aligned = Sample(elbowCurve.PrimaryOutput, PCGSampleSplineNode.SampleMode.BySpacing, 5.0f, 0,
                    0.0f, true, 0.0f);
                var forward = Vector3.Transform(Vector3.Forward, aligned.PrimaryOutput.Points[0].Rotation);
                check(Vector3.Dot(forward, Vector3.UnitX) > 0.99f,
                    "alignment faces the first sample along the curve's first segment",
                    $"{forward}");

                // Two curves: each sample's direction comes from its own curve, not from whichever came first.
                var twoCurves = TwoCurveData();
                var bothSampled = Sample(twoCurves, PCGSampleSplineNode.SampleMode.ByCount, 0.0f, 2, 0.0f, true, 0.0f);

                check(bothSampled.PrimaryOutput.Count == 4,
                    "points come from every curve on the input",
                    $"{bothSampled.PrimaryOutput.Count} point(s)");

                var secondForward = Vector3.Transform(Vector3.Forward, bothSampled.PrimaryOutput.Points[3].Rotation);
                check(Vector3.Dot(secondForward, Vector3.UnitZ) > 0.99f,
                    "the second curve's samples face along the second curve",
                    $"{secondForward}");

                // A layer with no curves is a graph wired wrong: reported, and the points pass through.
                var noCurves = Sample(line, PCGSampleSplineNode.SampleMode.BySpacing, 5.0f, 0, 0.0f, false, 0.0f);
                check(noCurves.PrimaryOutput.Count == line.Count && noCurves.PrimaryOutput.Splines == null,
                    "an input without curves is reported and passes through untouched",
                    $"{noCurves.PrimaryOutput.Count} point(s)");

                var zeroSpacing = Sample(elbowCurve.PrimaryOutput, PCGSampleSplineNode.SampleMode.BySpacing, 0.0f, 0,
                    0.0f, false, 0.0f);
                check(zeroSpacing.PrimaryOutput.Count == 0,
                    "a zero spacing is refused rather than looping over the curve",
                    $"{zeroSpacing.PrimaryOutput.Count} point(s)");
            }
            finally
            {
                PCGNodeRegistry.Clear();
            }
        }

        private static PCGData LineData(int count, float spacing)
        {
            var points = new PCGPoint[count];

            for (int i = 0; i < count; i++)
                points[i] = PCGPoint.Create(new Vector3(i * spacing, 0.0f, 0.0f), 1.0f, i);

            return Data(points);
        }

        private static PCGData ElbowData() => Data(new[]
        {
            PCGPoint.Create(new Vector3(0.0f, 0.0f, 0.0f), 1.0f, 0),
            PCGPoint.Create(new Vector3(10.0f, 0.0f, 0.0f), 1.0f, 1),
            PCGPoint.Create(new Vector3(10.0f, 10.0f, 0.0f), 1.0f, 2),
        });

        /// <summary>One curve along +X and one far away along +Z, so their tangents cannot be confused.</summary>
        private static PCGData TwoCurveData() => new PCGData
        {
            Splines = new[]
            {
                new PCGSpline(new[] { new Vector3(0.0f, 0.0f, 0.0f), new Vector3(100.0f, 0.0f, 0.0f) }, false),
                new PCGSpline(new[] { new Vector3(0.0f, 0.0f, 1000.0f), new Vector3(0.0f, 0.0f, 1100.0f) }, false),
            },
        };

        // ---- how curves travel through the data operations -------------------

        /// <summary>
        /// The three propagation rules the design document states for a non-row payload: clone copies the array,
        /// gather keeps the curves, merge concatenates them.
        /// </summary>
        /// <remarks>
        /// Written because the document claimed all three and no assertion exercised any of them - the kind of
        /// gap where the code and the promise drift apart quietly, and the symptom downstream is simply "the
        /// curves are gone".
        /// </remarks>
        private static void PropagationTests()
        {
            var curveA = new PCGSpline(new[] { new Vector3(0.0f, 0.0f, 0.0f), new Vector3(10.0f, 0.0f, 0.0f) }, false);
            var curveB = new PCGSpline(new[] { new Vector3(0.0f, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, 10.0f) }, false);

            var withCurve = new PCGData(new[]
            {
                PCGPoint.Create(new Vector3(1.0f, 0.0f, 0.0f), 1.0f, 0),
                PCGPoint.Create(new Vector3(2.0f, 0.0f, 0.0f), 1.0f, 1),
                PCGPoint.Create(new Vector3(3.0f, 0.0f, 0.0f), 1.0f, 2),
            })
            {
                Splines = new[] { curveA },
            };

            // Clone: the array is a copy, so appending a curve to the clone cannot reach the original.
            var clone = withCurve.Clone();
            var appended = new PCGSpline[clone.Splines.Length + 1];
            Array.Copy(clone.Splines, appended, clone.Splines.Length);
            appended[clone.Splines.Length] = curveB;
            clone.Splines = appended;

            check(withCurve.Splines.Length == 1 && clone.Splines.Length == 2,
                "a clone's curve array is its own, so extending it does not touch the original",
                $"{withCurve.Splines.Length} vs {clone.Splines.Length}");

            // Gather: dropping points keeps the path, because a curve is not a row.
            var gathered = PCGData.Gather(withCurve, new[] { 0, 2 }, 2);
            check(gathered.Count == 2 && gathered.Splines?.Length == 1 &&
                      ReferenceEquals(gathered.Splines[0], curveA),
                "filtering the points keeps the curve that was travelling with them",
                $"{gathered.Count} point(s), {gathered.Splines?.Length ?? 0} curve(s)");

            // Merge: every part's curves survive, in part order.
            var left = new PCGData(new[] { PCGPoint.Create(Vector3.Zero, 1.0f, 0) }) { Splines = new[] { curveA } };
            var right = new PCGData(new[] { PCGPoint.Create(Vector3.One, 1.0f, 0) }) { Splines = new[] { curveB } };

            var merged = PCGData.Merge(new[] { left, right }, out string mergeError);
            check(mergeError == null && merged.Splines?.Length == 2 &&
                      ReferenceEquals(merged.Splines[0], curveA) && ReferenceEquals(merged.Splines[1], curveB),
                "merging two layers keeps both paths, in the order the layers were given",
                mergeError ?? $"{merged.Splines?.Length ?? 0} curve(s)");

            // A layer with no curves stays free of them: nothing invents a path on the way through.
            var plain = new PCGData(new[] { PCGPoint.Create(Vector3.Zero, 1.0f, 0) });
            var mergedPlain = PCGData.Merge(new[] { plain, plain }, out _);
            check(mergedPlain.Splines == null && plain.Clone().Splines == null &&
                      PCGData.Gather(plain, new[] { 0 }, 1).Splines == null,
                "a layer without curves does not acquire any through clone, gather or merge");
        }

        private static PCGData Data(PCGPoint[] points) => new PCGData(points);

        private static PCGExecutionResult Create(
            PCGData input, PCGCreateSplineNode.VertexOrder order, bool closed, bool keepPoints)
        {
            var node = new PCGCreateSplineNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGCreateSplineNode.Id, new object[] { (int)order, closed, keepPoints }, 0));

            return Run(node, input);
        }

        private static PCGExecutionResult Sample(
            PCGData input, PCGSampleSplineNode.SampleMode mode, float spacing, int count,
            float heightOffset, bool align, float scale)
        {
            var node = new PCGSampleSplineNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGSampleSplineNode.Id,
                new object[] { (int)mode, spacing, count, 0.0f, align, heightOffset, scale, 0 }, 0));

            return Run(node, input);
        }

        private static PCGExecutionResult Run(PCGNodeBase node, PCGData input)
        {
            var context = new PCGExecutionContext
            {
                Seed = 20261007,
                GenerationBounds = new BoundingBox(
                    new Vector3(-1000.0f, -100.0f, -1000.0f), new Vector3(1000.0f, 100.0f, 1000.0f)),
            };

            var output = node.Execute(context, new[] { input });

            return new PCGExecutionResult { Outputs = new[] { output } };
        }

        // ---- pieces a mesh travels along --------------------------------------

        /// <summary>
        /// Cutting a curve into equal pieces, which is how a fence or a pipe is actually built out of one mesh.
        /// </summary>
        private static void MeshLayoutTests()
        {
            var pieces = new List<PCGSplineSegment>();
            var straight = new PCGSpline(new[]
            {
                new Vector3(0.0f, 0.0f, 0.0f), new Vector3(20.0f, 0.0f, 0.0f),
            }, false);

            int count = PCGSplineMeshLayout.Layout(new[] { straight }, 5.0f, 0.5f, pieces);

            check(count == 4 && pieces.Count == 4,
                "a twenty-unit line becomes four five-unit pieces",
                $"{count} piece(s)");

            check(Close(pieces[0].Position, new Vector3(2.5f, 0.0f, 0.0f)) &&
                      Close(pieces[3].Position, new Vector3(17.5f, 0.0f, 0.0f)),
                "piece centres sit on the curve, half a piece in from each end",
                $"{pieces[0].Position} .. {pieces[3].Position}");

            var forward = Vector3.Transform(Vector3.Forward, pieces[0].Rotation);
            check(Vector3.Dot(forward, Vector3.UnitX) > 0.99f &&
                      Math.Abs(pieces[0].Scale.X - 5.0f) < 1e-3f &&
                      Math.Abs(pieces[0].Scale.Y - 0.5f) < 1e-3f,
                "a piece faces along the curve and is scaled to its own length",
                $"{forward}, scale {pieces[0].Scale}");

            // The requested length is a maximum: twenty units at six per piece is four fives, not three sixes
            // and a runt.
            PCGSplineMeshLayout.Layout(new[] { straight }, 6.0f, 0.5f, pieces);
            check(pieces.Count == 4 && Math.Abs(pieces[0].Scale.X - 5.0f) < 1e-3f,
                "a length that does not divide evenly gives equal pieces just under the request",
                $"{pieces.Count} piece(s) of {pieces[0].Scale.X:0.###}");

            // Around a corner the piece turns with it: the third piece of the elbow lies on the second segment,
            // which runs straight up - the case where a naive LookRotation returns a quaternion that is not even
            // normalised, so the length of the turned forward vector is part of the assertion.
            int elbowCount = PCGSplineMeshLayout.Layout(new[] { Elbow() }, 5.0f, 0.5f, pieces);
            var third = Vector3.Transform(Vector3.Forward, pieces[2].Rotation);

            check(elbowCount == 4 && Vector3.Dot(third, Vector3.UnitY) > 0.99f &&
                      Math.Abs(third.Length - 1.0f) < 1e-3f,
                "a piece on a vertical segment faces up with a normalised orientation",
                $"{third} (length {third.Length:0.###})");

            // A closed curve is covered whole, seam included.
            var loop = new PCGSpline(new[]
            {
                new Vector3(0.0f, 0.0f, 0.0f),
                new Vector3(10.0f, 0.0f, 0.0f),
                new Vector3(10.0f, 0.0f, 10.0f),
            }, true);

            PCGSplineMeshLayout.Layout(new[] { loop }, 4.0f, 0.5f, pieces);
            float covered = 0.0f;

            for (int i = 0; i < pieces.Count; i++)
                covered += pieces[i].Length;

            check(pieces.Count > 0 && Math.Abs(covered - loop.Length) < 1e-2f,
                "the pieces of a closed curve add up to its whole length, seam included",
                $"{covered:0.###} of {loop.Length:0.###}");

            // Degenerate inputs, and the ceiling that stops a long curve asking for millions of actors.
            var single = new PCGSpline(new[] { new Vector3(5.0f, 0.0f, 5.0f) }, false);
            check(PCGSplineMeshLayout.Layout(new[] { single }, 5.0f, 0.5f, pieces) == 0 &&
                      PCGSplineMeshLayout.Layout(new[] { straight }, 0.0f, 0.5f, pieces) == 0,
                "a curve with no length and a piece length of zero both produce nothing");

            var longLine = new PCGSpline(new[]
            {
                new Vector3(0.0f, 0.0f, 0.0f), new Vector3(1000000.0f, 0.0f, 0.0f),
            }, false);

            check(PCGSplineMeshLayout.Layout(new[] { longLine }, 1.0f, 0.5f, pieces) ==
                      PCGSplineMeshLayout.MaxSegments,
                "a curve long enough to ask for a million pieces is capped",
                $"{pieces.Count} piece(s)");

            // Same curve, same pieces: nothing here may depend on the order curves happen to be listed in.
            var first = new List<PCGSplineSegment>();
            var second = new List<PCGSplineSegment>();
            PCGSplineMeshLayout.Layout(new[] { Elbow(), loop }, 5.0f, 0.5f, first);
            PCGSplineMeshLayout.Layout(new[] { Elbow(), loop }, 5.0f, 0.5f, second);

            bool identical = first.Count == second.Count;

            for (int i = 0; identical && i < first.Count; i++)
            {
                if (!Close(first[i].Position, second[i].Position) ||
                    !Close(Vector3.Transform(Vector3.Forward, first[i].Rotation),
                           Vector3.Transform(Vector3.Forward, second[i].Rotation)))
                {
                    identical = false;
                }
            }

            check(identical && first.Count > 0,
                "laying out the same curves twice gives the same pieces",
                $"{first.Count} piece(s)");
        }

        /// <summary>
        /// 引擎的**弧长下标约定**：`Spline::GetSplineSegmentLength(index)` 的 0 号恒返回 0，
        /// 第 i 段的长度记在**终止 knot** 的下标 i 上（内部读 `Curve[i-1]`/`Curve[i]`），有效下标 1..knots-1。
        /// </summary>
        /// <remarks>
        /// 照直觉写 0..knots-2 会整整漏掉一段：一条 1000 单位的路按 100 间距本该 11 个点、实测只出 5，
        /// 而屏幕上只表现为"路沿线的东西少了一半"。这条断言钉的就是那个约定本身。
        /// </remarks>
        private static void TestArcLengthIndexConvention()
        {
            // 三个 knot、两段各 500：按引擎口径 length(0)=0、length(1)=500、length(2)=500。
            Func<int, float> lengthOf = index => index == 0 ? 0.0f : 500.0f;

            var starts = PCG.Nodes.PCGSplineSamplerNode.CumulativeLengths(2, lengthOf, out float total);

            _check("按引擎约定累加：两段各 500 ⇒ 总长 1000，且每段起点表为 [0,500]",
                Math.Abs(total - 1000.0f) < 1e-3f &&
                Math.Abs(starts[0] - 0.0f) < 1e-3f && Math.Abs(starts[1] - 500.0f) < 1e-3f,
                $"total={total:F1}, starts=[{starts[0]:F1},{starts[1]:F1}]");

            // 反面：从 0 累加到 knots-2 会漏一段（修之前就是那样写的）。把两种写法的差钉住，
            // 免得有人又改回去而没人察觉。
            float naive = 0.0f;
            for (int i = 0; i < 2; i++)
                naive += lengthOf(i);

            _check("从 0 累加到 knots-2 会漏掉一段（必须按引擎的 1..knots-1 下标）",
                Math.Abs(naive - 500.0f) < 1e-3f && Math.Abs(total - naive) > 1.0f,
                $"naive={naive:F1} vs engine={total:F1}");

            int count = (int)MathF.Floor(total / 100.0f) + 1;

            _check("1000 单位的路按 100 间距给出 11 个点（含两端）", count == 11, count.ToString());
        }

        /// <summary>
        /// 段内弧长与参数**不成比例**时，按弧长反解必须仍然等距。
        /// </summary>
        /// <remarks>
        /// 这是引擎侧那条"间距出来 52 而不是 100"的离线对照：不碰引擎，用一条解析曲线
        /// （零切线三次曲线，x(u) = -250 + 500·u²(3-2u)，两头慢中间快）造一张与
        /// <c>BuildArcTable</c> 同形的参数→弧长表，再把"沿真弧长每 100 取一点"喂给反解。
        /// 参照是解析式的：该曲线弧长 s(u) = 500·(3u²-2u³)，与实现里逐段插值那条路完全独立。
        /// 旧实现（段内按段长线性映射）在这里第一个间距会报 52 而不是 100。
        /// </remarks>
        private static void TestArcLengthInversion()
        {
            const int samples = 32;
            const float length = 500.0f;
            const float spacing = 100.0f;

            var times = new float[samples + 1];
            var distances = new float[samples + 1];

            times[0] = 0.0f;
            distances[0] = 0.0f;

            float travelled = 0.0f;
            float previous = CurvePosition(0.0f);

            for (int k = 1; k <= samples; k++)
            {
                float t = (float)k / samples;
                float position = CurvePosition(t);

                travelled += Math.Abs(position - previous);
                previous = position;

                times[k] = t;
                distances[k] = travelled;
            }

            // 按解析真弧长归一，与节点里"引擎段长是真值"那个口径一致。
            float chord = travelled;

            for (int k = 1; k <= samples; k++)
                distances[k] *= length / chord;

            // 前提自证：这张表的距离与参数**不是**线性的，否则这条断言会退化成恒等、
            // 旧实现也会通过。曲线是对称的，所以中点的弧长恰好是一半——非线性量在**第一段**：
            // 参数走四分之一时真弧长只有约 78（线性映射会给 125）。
            _check("夹具自证：这张表里参数走四分之一、弧长还差得远（不然这条对照恒等）",
                distances[samples / 4] < length * 0.25f - 10.0f,
                $"quarter-t reaches {distances[samples / 4]:F1} of {length:F0}");

            var startLength = new[] { 0.0f };
            int count = (int)MathF.Floor(length / spacing) + 1;

            _check("100 间距在 500 单位上给出 6 个点（含两端）", count == 6, count.ToString());

            var positions = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = PCG.Nodes.PCGSplineSamplerNode.TimeAt(
                    times, distances, samples + 1, 1, startLength, length, i * spacing);

                positions[i] = CurvePosition(t);
            }

            _check("两端严格落在曲线的两端",
                Math.Abs(positions[0] + 250.0f) < 0.5f && Math.Abs(positions[count - 1] - 250.0f) < 0.5f,
                $"{positions[0]:F1} .. {positions[count - 1]:F1}");

            float worst = 0.0f;

            for (int i = 1; i < count; i++)
            {
                float gap = positions[i] - positions[i - 1];
                worst = MathF.Max(worst, MathF.Abs(gap - spacing));
            }

            _check("沿线每 100 弧长处取的点，落点也等距 100（不是往段头挤）",
                worst < 1.0f, $"最差间距偏差 {worst:F2}");

            // 反面：段内按段长线性映射（旧实现）会把第一个点放到 52 附近。
            float linearT = 0.2f;   // distanceAlong=100 落在唯一一段的 100/500 处
            float linearPosition = CurvePosition(linearT);

            _check("对照：段内线性映射给出的第一点偏了 48（旧实现就是这个症状）",
                Math.Abs((linearPosition - positions[0]) - spacing) > 30.0f,
                $"线性映射 {linearPosition:F1}，正确 {positions[0]:F1}");
        }

        /// <summary>零切线三次曲线（单段、总长 500）：两头慢、中间快，弧长与参数明显不成比例。</summary>
        private static float CurvePosition(float u) => -250.0f + 500.0f * (u * u * (3.0f - 2.0f * u));

        private static PCGSpline Elbow() => new PCGSpline(new[]
        {
            new Vector3(0.0f, 0.0f, 0.0f),
            new Vector3(10.0f, 0.0f, 0.0f),
            new Vector3(10.0f, 10.0f, 0.0f),
        }, false);

        private static bool Close(Vector3 a, Vector3 b, float tolerance = 1e-3f) =>
            Math.Abs(a.X - b.X) < tolerance && Math.Abs(a.Y - b.Y) < tolerance && Math.Abs(a.Z - b.Z) < tolerance;
    }
}
