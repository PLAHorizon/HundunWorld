// PCG 插件 - Point Ops 节点的可执行测试。
//
// Difference 与 Intersection 是把一层从另一层里挖出去的那两个算子，而它们的正确性标准不是
// "数量看着合理"：这一对必须把输入划分开（每个点恰好被其中之一保留），并且
// 两者都要与一份独立写出的参照一致——那份参照不是照着它们真正用的那张网格写的。网格才是
// 值得怀疑的那部分——它是项优化，而一处静默漏掉候选项的优化会把一条路廊
// 变成漏的。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG;
using PCG.Execution;
using PCG.Graph;
using PCG.Nodes;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class PointOpsTests
    {
        private static CheckDelegate _check;

        // 先写条件，于是一句断言读起来是"这件事成立，因为……"。
        private static void check(bool ok, string what, string detail = null) => _check(what, ok, detail);

        internal static void Run(CheckDelegate callback)
        {
            _check = callback;

            PCGNodeRegistry.Clear();
            PCG.Runtime.PCGNodeRegistration.RegisterAll();

            try
            {
                ComplementTests();
                ReferenceTests();
                EdgeTests();
                CopyTests();
                EulerDegreesTests();
                LocalYawTests();
                SlopeConventionTests();
                BranchTests();
                ScaleByAttributeTests();
                OffsetByAttributeTests();
                RotateByAttributeTests();
                SortTests();
                BoundsTests();
                AttributeOpTests();
                SelectTests();
                NeighborhoodTests();
                RelaxTests();
                AttributeMathTests();
                AttributeFilterTests();
                AttributeTransferTests();
                TransferMatchTests();
                DistanceToPointsTests();
                NoiseToAttributeTests();
                ChannelToAttributeTests();
                SmoothTests();
                ParameterCoverageTests();
                NormalToDensityTests();
                InspectionTests();
                DrawListTests();
            }
            finally
            {
                PCGNodeRegistry.Clear();
            }
        }

        // ---- 这一对把输入划分开 -----------------------------------

        private static void ComplementTests()
        {
            var input = Scatter(20, 20, 50.0f, Vector3.Zero);
            var cut = Scatter(6, 6, 50.0f, new Vector3(200.0f, 0.0f, 120.0f));
            Mark(cut.Points, 60.0f);

            foreach (var fixture in new[]
                     {
                         (Distance: false, Padding: 0.0f, Plane: true),
                         (Distance: false, Padding: 25.0f, Plane: true),
                         (Distance: true, Padding: 0.0f, Plane: true),
                         (Distance: true, Padding: 40.0f, Plane: false),
                     })
            {
                var difference = Difference(input, cut, fixture.Distance, 80.0f, fixture.Padding, fixture.Plane);
                var intersection = Intersection(input, cut, fixture.Distance, 80.0f, fixture.Padding, fixture.Plane);

                int kept = difference.PrimaryOutput.Count;
                int inside = intersection.PrimaryOutput.Count;

                check(kept + inside == input.Count,
                    $"difference + intersection account for every input point " +
                    $"(mode {(fixture.Distance ? "distance" : "bounds")}, padding {fixture.Padding:0.#})",
                    $"{kept} + {inside} vs {input.Count}");

                check(SameSet(difference.PrimaryOutput, intersection.PrimaryOutput) == false,
                    $"the two outputs do not overlap (mode {(fixture.Distance ? "distance" : "bounds")})",
                    $"{kept} outside, {inside} inside");

                check(intersection.PrimaryOutput.Count > 0 && difference.PrimaryOutput.Count > 0,
                    "the fixture really cuts through the middle: neither side is empty",
                    $"{kept} / {inside}");
            }
        }

        // ---- 与一份独立写出的参照对照 ------------------------------

        private static void ReferenceTests()
        {
            var input = Scatter(24, 24, 40.0f, Vector3.Zero);
            var cut = Scatter(5, 7, 90.0f, new Vector3(-140.0f, 0.0f, 60.0f));
            Mark(cut.Points, 35.0f);

            foreach (float padding in new[] { 0.0f, 20.0f, 75.0f })
            {
                var difference = Difference(input, cut, false, 100.0f, padding, true);

                int expected = 0;
                for (int i = 0; i < input.Points.Length; i++)
                {
                    if (!ReferenceInsideBounds(input.Points[i].Position, cut.Points, padding))
                        expected++;
                }

                check(difference.PrimaryOutput.Count == expected,
                    $"bounds mode agrees with the brute-force reference at padding {padding:0.#}",
                    $"{difference.PrimaryOutput.Count} vs {expected}");

                var intersection = Intersection(input, cut, true, 120.0f, padding, true);

                int expectedDistance = 0;
                for (int i = 0; i < input.Points.Length; i++)
                {
                    if (ReferenceInsideDistance(input.Points[i].Position, cut.Points, 120.0f + padding))
                        expectedDistance++;
                }

                check(intersection.PrimaryOutput.Count == expectedDistance,
                    $"distance mode agrees with the brute-force reference at padding {padding:0.#}",
                    $"{intersection.PrimaryOutput.Count} vs {expectedDistance}");
            }

            // 稀疏退回路径是一条不同的代码路径（哈希分桶而不是稠密网格），那里的候选窗口弄错了
            // 只会在"点集铺得比它的点数宽得多"的情形下才暴露出来。
            var wide = new PCGPoint[12];
            for (int i = 0; i < wide.Length; i++)
                wide[i] = PCGPoint.Create(new Vector3(i * 9000.0f, 0.0f, (i % 3) * 9000.0f), 1.0f, i);
            Mark(wide, 40.0f);

            // 查询是由它本该落进去的那个盒子推出来的：早先一稿把偏移硬写成从原点出发，
            // 于是静默地测了一个 Z 方向 18000 单位之外的盒子（i % 3 是 2 不是 0），
            // 这被当成稀疏路径的缺陷，直到失败详情把边界打出来才看清。
            var anchor = wide[2].Position;
            var far = PCGPoint.Create(new Vector3(anchor.X + 10.0f, anchor.Y, anchor.Z + 20.0f), 1.0f, 99);
            far.UpdateBounds(new Vector3(40.0f, 40.0f, 40.0f));
            var probe = new PCGPointProbe(wide, false, 0.0f, true, 0.0f);

            check(probe.IsSparse, "the wide fixture really takes the sparse bucket path");
            check(probe.IsInside(far.Position),
                "a point 10 units from a box is found through the sparse path",
                $"probe count={probe.Count}, anchor box={wide[2].Bounds}, far={far.Position}");

            var nowhere = new Vector3(anchor.X + 4500.0f, 0.0f, anchor.Z + 4500.0f);
            check(!probe.IsInside(nowhere),
                "and a point far from every box is not, so the sparse path is not answering yes to everything");
        }

        // ---- 边界情形 ----------------------------------------------

        private static void EdgeTests()
        {
            var input = Scatter(10, 10, 50.0f, Vector3.Zero);
            var cut = Scatter(4, 4, 50.0f, Vector3.Zero);
            Mark(cut.Points, 30.0f);

            // "没东西可减"不等于"把一切都减掉"：被关掉的分支必须把这一层原样递出去，
            // 而不是静默地把整张图清空。
            var empty = Difference(input, PCGData.Empty, false, 100.0f, 0.0f, true);
            check(empty.PrimaryOutput.Count == input.Count,
                "an empty cutting input leaves Difference's input untouched",
                $"{empty.PrimaryOutput.Count} vs {input.Count}");

            var emptyIntersection = Intersection(input, PCGData.Empty, false, 100.0f, 0.0f, true);
            check(emptyIntersection.PrimaryOutput.Count == 0,
                "an empty region intersects nothing",
                $"{emptyIntersection.PrimaryOutput.Count}");

            // 平面模式正是这个模式的意义：一个高出裁切盒 2000 单位的点，
            // 在忽略高度时仍在廊道内，而不忽略时就在廊道外。
            var high = new PCGPoint[1];
            high[0] = PCGPoint.Create(new Vector3(0.0f, 2000.0f, 0.0f), 1.0f, 0);
            high[0].UpdateBounds(new Vector3(10.0f, 10.0f, 10.0f));

            var planeCut = Intersection(Data(high), Data(cut.Points), false, 100.0f, 0.0f, true);
            var solidCut = Intersection(Data(high), Data(cut.Points), false, 100.0f, 0.0f, false);

            check(planeCut.PrimaryOutput.Count == 1 && solidCut.PrimaryOutput.Count == 0,
                "plane mode keeps a point 2000 units up, and turning it off rejects that point",
                $"plane={planeCut.PrimaryOutput.Count} solid={solidCut.PrimaryOutput.Count}");

            // 存活者保持输入顺序，因为下游节点按位置给点排下标。比对用的是
            // 夹具写下的 seed，也就是那个点原来的下标：单看 X 每一行都会循环。
            var difference = Difference(input, cut, false, 100.0f, 0.0f, true);
            var points = difference.PrimaryOutput.Points;
            bool ordered = true;

            for (int i = 1; i < (points?.Length ?? 0); i++)
            {
                if (points[i].Seed <= points[i - 1].Seed)
                    ordered = false;
            }

            check(ordered && points != null && points.Length > 10,
                "survivors come back in the input's own order rather than the grid's",
                $"{points?.Length ?? 0} survivor(s)");
        }

        // ---- 拷贝点 CopyPoints -------------------------------------------

        private static void CopyTests()
        {
            var source = new PCGPoint[4];

            for (int i = 0; i < source.Length; i++)
            {
                source[i] = PCGPoint.Create(new Vector3(i * 100.0f, 5.0f, -i * 40.0f), 0.5f, i);
                source[i].UpdateBounds(new Vector3(10.0f, 10.0f, 100.0f));
            }

            var data = Data(source);
            var plain = Copy(data, 3, Vector3.Zero, true, Vector3.Zero, 1.0f, 1.0f, 0);

            check(plain.PrimaryOutput.Count == source.Length * 3,
                "three copies per point, laid out in source order",
                $"{plain.PrimaryOutput.Count}");

            bool identical = true;
            var copies = plain.PrimaryOutput.Points;

            for (int i = 0; i < source.Length; i++)
            {
                for (int c = 0; c < 3; c++)
                {
                    var copy = copies[i * 3 + c];
                    if (copy.Position != source[i].Position || copy.Rotation != source[i].Rotation ||
                        copy.Scale != source[i].Scale)
                    {
                        identical = false;
                    }
                }
            }

            check(identical, "zero offset, no spread and unit scale leave every copy as the source point");

            // 局部空间跟着点自己的朝向走，世界空间不跟。偏移在两种情况下长度不变，
            // 这正是让它成为对参考系的检验、而不是对算术的检验。
            var rotated = new PCGPoint[1];
            rotated[0] = PCGPoint.Create(Vector3.Zero, 1.0f, 0);
            rotated[0].Rotation = PCGPointTransform.EulerDegrees(new Vector3(0.0f, 90.0f, 0.0f));
            rotated[0].UpdateBounds(new Vector3(10.0f, 10.0f, 10.0f));

            var offset = new Vector3(100.0f, 0.0f, 0.0f);
            var local = Copy(Data(rotated), 2, offset, true, Vector3.Zero, 1.0f, 1.0f, 0);
            var world = Copy(Data(rotated), 2, offset, false, Vector3.Zero, 1.0f, 1.0f, 0);

            var localStep = local.PrimaryOutput.Points[1].Position;
            var worldStep = world.PrimaryOutput.Points[1].Position;

            // 参考系这件事用几何方式断言，而不是对着一个数：偏移在两种情况下都沿该点自己的
            // 右轴指，所以局部的一步垂直于该点的前进轴，而世界的一步平行于世界 X。
            // 早先一版从一个 90 度的 yaw 期望恰好得到 (0,0,-100)，实测却是 116.62 度，
            // 起初被归咎于"Euler 的约定不一样"。真正的原因是 EulerDegrees
            // 在把角度交给一个本来就收角度的 API 之前又乘了一道 180/pi
            // （已修，并由 EulerDegreesTests 直接钉住）。几何式的写法仍然留着：
            // 它是更强的那条陈述，而且换一次手性也照样成立。
            var forward = Vector3.Transform(Vector3.Forward, rotated[0].Rotation);
            var localLength = localStep.Length;
            var worldLength = worldStep.Length;

            bool localPerpendicular = Math.Abs(Vector3.Dot(localStep / localLength, forward)) < 0.01f;
            bool worldAlongX = Math.Abs(Vector3.Dot(worldStep / worldLength, Vector3.UnitX) - 1.0f) < 0.001f;

            check(Math.Abs(localLength - 100.0f) < 0.01f && Math.Abs(worldLength - 100.0f) < 0.01f,
                "the step keeps its length in both frames",
                $"{localLength:0.###} / {worldLength:0.###}");

            check(localPerpendicular && worldAlongX,
                "local space turns the offset with the point, world space leaves it pointing along world X",
                $"local step={localStep}, forward={forward}, world step={worldStep}");

            var scaled = Copy(Data(source), 2, Vector3.Zero, true, Vector3.Zero, 2.0f, 2.0f, 0);
            var scaledHalf = PCGPointTransform.HalfExtents(scaled.PrimaryOutput.Points[0].Bounds);

            check(Math.Abs(scaledHalf.X - 20.0f) < 0.5f && Math.Abs(scaledHalf.Z - 200.0f) < 1.0f,
                "a doubled copy carries a doubled box, or the pruner would test a box it no longer fits",
                $"{scaledHalf}, scale={scaled.PrimaryOutput.Points[0].Scale}");

            // 每一份副本的盒子都必须装得下源盒子按该副本自己的旋转过之后的样子。这是逐份副本
            // 拿测试自己转过的角点来断言的，而不是断言"盒子一直在 Z 方向很长"——一个转过
            // 四分之一圈的盒子沿 Z 就该是扁的，那是合法的，而这一检查的早先版本把那称作缺陷。
            var spread = Copy(Data(source), 3, Vector3.Zero, true, new Vector3(0.0f, 180.0f, 0.0f), 1.0f, 1.0f, 0);
            int enclosed = 0;
            int checkedCopies = 0;

            for (int i = 0; i < spread.PrimaryOutput.Count; i++)
            {
                var copy = spread.PrimaryOutput.Points[i];
                var sourceHalf = PCGPointTransform.HalfExtents(source[copy.Seed / 3].Bounds);
                var required = TurnedHalfExtents(sourceHalf, copy.Rotation);
                var actual = PCGPointTransform.HalfExtents(copy.Bounds);

                checkedCopies++;

                if (actual.X >= required.X - 0.5f && actual.Y >= required.Y - 0.5f &&
                    actual.Z >= required.Z - 0.5f)
                {
                    enclosed++;
                }
            }

            check(checkedCopies > 0 && enclosed == checkedCopies,
                "every rotated copy's box still contains the source box turned by its own rotation",
                $"{enclosed}/{checkedCopies} enclosed");

            var seedOffset = new Vector3(0.0f, 20.0f, 0.0f);
            var spreadRot = new Vector3(0.0f, 30.0f, 0.0f);

            var again = Copy(Data(source), 3, seedOffset, true, spreadRot, 0.8f, 1.2f, 7);
            var repeat = Copy(Data(source), 3, seedOffset, true, spreadRot, 0.8f, 1.2f, 7);
            var shifted = Copy(Data(source), 3, seedOffset, true, spreadRot, 0.8f, 1.2f, 8);

            check(Keys(again.PrimaryOutput) == Keys(repeat.PrimaryOutput),
                "the same seed reproduces the same cluster");
            check(Keys(again.PrimaryOutput) != Keys(shifted.PrimaryOutput),
                "a different seed offset deliberately disagrees, so the seed is really being read");
        }

        // ---- 排序点 SortPoints -------------------------------------------

        private static void EulerDegreesTests()
        {
            // 包装的 API 收的是角度，所以四分之一圈必须真的是四分之一圈：90 度的 yaw
            // 会把前进轴搬到 X 上。这就是助手随后被拿来对照的那个控制项。
            var engineYaw = Vector3.Transform(Vector3.Forward, Quaternion.Euler(0.0f, 90.0f, 0.0f));
            check(Near(MathF.Abs(engineYaw.X), 1.0f) && Near(engineYaw.Y, 0.0f) && Near(engineYaw.Z, 0.0f),
                "the engine's Euler reads its arguments as degrees",
                engineYaw.ToString());

            var helperYaw = Vector3.Transform(Vector3.Forward,
                PCGPointTransform.EulerDegrees(new Vector3(0.0f, 90.0f, 0.0f)));
            check(Near(helperYaw.X, engineYaw.X) && Near(helperYaw.Y, engineYaw.Y) && Near(helperYaw.Z, engineYaw.Z),
                "EulerDegrees agrees with the engine call it wraps, degree for degree",
                helperYaw.ToString());

            // 一整圈就是恒等。多出来的一道缩放系数伪造不了这件事：它正是当初静默地把
            // 人人期望的"90 度"变成 116.62 度的那个东西。
            var fullTurn = PCGPointTransform.EulerDegrees(new Vector3(0.0f, 360.0f, 0.0f));
            check(Near(Vector3.Transform(Vector3.Forward, fullTurn).Z, 1.0f),
                "a 360-degree yaw is the identity rotation",
                Vector3.Transform(Vector3.Forward, fullTurn).ToString());

            // 插件里有两个基于角度的 API，它们必须说的是同一件事：旋转参数要的口径
            // (EulerDegrees) 与坡度读数报出的口径 (SlopeDegrees) 得一致。修之前它们是两种单位，
            // 而没人察觉，因为这两个从没被拿来互相断言过。
            var tilted = new[] { PCGPoint.Create(Vector3.Zero, 1.0f, 0) };
            tilted[0].Rotation = PCGPointTransform.EulerDegrees(new Vector3(0.0f, 0.0f, 80.0f));
            check(Near(PCGPointTransform.SlopeDegrees(tilted[0]), 80.0f),
                "an 80-degree tilt in the rotation parameter reads back as an 80-degree slope",
                PCGPointTransform.SlopeDegrees(tilted[0]).ToString("F4"));

            var upright = PCGPoint.Create(Vector3.Zero, 1.0f, 0);
            check(Near(PCGPointTransform.SlopeDegrees(upright), 0.0f),
                "an unturned point has no slope",
                PCGPointTransform.SlopeDegrees(upright).ToString("F4"));
        }

        private static void BranchTests()
        {
            var source = Data(new[]
            {
                PCGPoint.Create(Vector3.Zero, 1.0f, 0),
                PCGPoint.Create(new Vector3(50.0f, 0.0f, 0.0f), 1.0f, 1),
            });
            source.SharedAttributes = Column(source, "v", new[] { 1.0f, 2.0f });

            var passed = Branch(source, true);
            check(passed.PrimaryOutput.Count == 2 && Column(passed.PrimaryOutput, "v") == "1,2",
                "a set condition passes the layer through with its columns intact",
                Column(passed.PrimaryOutput, "v"));

            // 原样递出去那一路必须是自己的一份拷贝：之后对该分支产出的写入
            // 不许出现在它上游的那一层里，否则一个被关掉的分支还在改喂给它的数据。
            var writable = passed.PrimaryOutput.SharedAttributes.GetWritableFloatArray("v");
            writable[0] = 99.0f;
            check(source.SharedAttributes.GetFloat("v", 0) == 1.0f,
                "the branch republishes rather than handing the upstream layer back",
                source.SharedAttributes.GetFloat("v", 0).ToString("F3"));

            // 被拦住的那一例是正向孪生的反面：这一层是空的，而不只是没变。
            var blocked = Branch(source, false);
            check(blocked.PrimaryOutput.Count == 0,
                "a clear condition produces nothing rather than a partial layer",
                blocked.PrimaryOutput.Count.ToString());

            check(Branch(PCGData.Empty, true).PrimaryOutput.Count == 0, "an empty layer stays empty when the condition is set");
            check(Branch(PCGData.Empty, false).PrimaryOutput.Count == 0, "an empty layer stays empty when the condition is clear");

            // 完全没有存值地构造出来时，节点必须照 schema 声明的那样退回。
            var byDefault = Run(new PCGBranchNode(Serialization(PCGBranchNode.Id, Array.Empty<object>())), source, PCGData.Empty);
            check(byDefault.PrimaryOutput.Count == source.Count,
                "the default condition passes the layer, matching the schema slot",
                byDefault.PrimaryOutput.Count.ToString());
        }

        private static PCGExecutionResult Branch(PCGData input, bool condition)
        {
            var node = new PCGBranchNode(Serialization(PCGBranchNode.Id, new object[] { condition }));

            return Run(node, input, PCGData.Empty);
        }

        private static void LocalYawTests()
        {
            // 水平的那一例必须原样不动：一个没倾斜的点要像以前那样转，否则
            // 这就是行为改动，而不是一处修复。
            float angle = 0.7f;
            check(CloseYaw(PCGPointTransform.YawAboutLocalUp(Quaternion.Identity, angle),
                           Quaternion.RotationAxis(Vector3.Up, angle)),
                "an untilted point spins exactly as a world yaw would");

            // 倾斜的那一例才是重点：绕点自己的 up 旋转不许把那个 up 挪走。
            var slopeUp = Vector3.Normalize(new Vector3(1.0f, 0.0f, 1.0f));
            var tilted = PCGSurfaceAlign.RotationFromNormal(slopeUp);

            bool preserved = true;
            bool unit = true;

            for (int i = 0; i < 8; i++)
            {
                var spun = PCGPointTransform.YawAboutLocalUp(tilted, i * (Mathf.TwoPi / 8.0f));

                if (!AlignsUp(spun, slopeUp))
                    preserved = false;

                if (MathF.Abs(spun.Length - 1.0f) > 1e-4f)
                    unit = false;
            }

            check(preserved, "spinning a tilted point never tilts it further: its up survives any yaw");
            check(unit, "the composed rotation stays a unit quaternion, so nothing is scaled by the spin");

            // 为什么要改：把同一个 yaw 复合在左边（也就是世界轴），在倾斜的点上保不住 up，
            // 而这一例是孪生项，它让上面那条断言不至于空转。
            var worldYaw = Quaternion.RotationAxis(Vector3.Up, angle);
            var wrong = Quaternion.Multiply(worldYaw, tilted);
            check(!AlignsUp(wrong, slopeUp),
                "the old world-space composition is rejected by the same check, which is what made it a defect",
                Vector3.Transform(Vector3.Up, wrong).ToString());

            check(CloseYaw(PCGPointTransform.YawAboutLocalUp(tilted, 0.0f), tilted),
                "a zero yaw leaves the rotation alone");
            check(CloseYaw(PCGPointTransform.YawAboutLocalUp(tilted, Mathf.TwoPi), tilted),
                "a full turn is the identity yaw");
        }

        /// <summary>
        /// 用两个旋转各自把两条独立轴送到哪里来比较它们，于是 q 与 -q
        /// 表示同一个朝向这件事骗不了它。
        /// </summary>
        private static bool CloseYaw(Quaternion a, Quaternion b)
        {
            return (Vector3.Transform(Vector3.Up, a) - Vector3.Transform(Vector3.Up, b)).LengthSquared < 1e-6f &&
                   (Vector3.Transform(Vector3.Forward, a) - Vector3.Transform(Vector3.Forward, b)).LengthSquared < 1e-6f;
        }

        private static bool AlignsUp(Quaternion rotation, Vector3 normal)
        {
            var up = Vector3.Transform(Vector3.Up, rotation);
            var target = normal.LengthSquared > 1e-12f ? Vector3.Normalize(normal) : Vector3.Up;

            return (up - target).LengthSquared < 1e-4f;
        }

        /// <summary>
        /// 钉住宿件文档写了却没人断言过的坡度约定：与铅垂方向的夹角、落在
        /// [0,90]，而且是<b>绝对值</b>——翻转过的点读出同样的陡度。
        /// </summary>
        /// <remarks>
        /// 取绝对值不是装饰。少了它，一个只管旋转的修改器只要把点翻一面，
        /// 就能让点跨过坡度阈值，而坡度的两个产出方（地形采样器与由旋转读出的那一条）
        /// 会对"哪边是上"各说各话。
        /// </remarks>
        private static void SlopeConventionTests()
        {
            var flat = PCGPoint.Create(Vector3.Zero, 1.0f, 0);
            check(Near(PCGPointTransform.SlopeDegrees(flat), 0.0f),
                "an unturned point has no slope", PCGPointTransform.SlopeDegrees(flat).ToString("F4"));

            var tilted = PCGPoint.Create(Vector3.Zero, 1.0f, 0);
            tilted.Rotation = PCGPointTransform.EulerDegrees(new Vector3(0.0f, 0.0f, 80.0f));
            check(Near(PCGPointTransform.SlopeDegrees(tilted), 80.0f),
                "an 80-degree tilt reads as an 80-degree slope",
                PCGPointTransform.SlopeDegrees(tilted).ToString("F4"));

            var flippedTilt = PCGPoint.Create(Vector3.Zero, 1.0f, 0);
            flippedTilt.Rotation = PCGPointTransform.EulerDegrees(new Vector3(0.0f, 0.0f, -80.0f));
            check(Near(PCGPointTransform.SlopeDegrees(flippedTilt), 80.0f),
                "tilting the other way reads the same, because the slope is absolute",
                PCGPointTransform.SlopeDegrees(flippedTilt).ToString("F4"));

            var upsideDown = PCGPoint.Create(Vector3.Zero, 1.0f, 0);
            upsideDown.Rotation = PCGPointTransform.EulerDegrees(new Vector3(180.0f, 0.0f, 0.0f));
            check(Near(PCGPointTransform.SlopeDegrees(upsideDown), 0.0f),
                "an upside-down point is still flat, not 180 degrees of slope",
                PCGPointTransform.SlopeDegrees(upsideDown).ToString("F4"));

            bool inRange = true;
            for (int i = 0; i < 16; i++)
            {
                var point = PCGPoint.Create(Vector3.Zero, 1.0f, 0);
                point.Rotation = PCGPointTransform.EulerDegrees(new Vector3(i * 23.0f, i * 47.0f, i * 71.0f));

                float slope = PCGPointTransform.SlopeDegrees(point);
                if (slope < 0.0f || slope > 90.0f || float.IsNaN(slope))
                    inRange = false;
            }

            check(inRange, "the slope always lands inside [0,90], whatever the orientation");
        }

        private static void ScaleByAttributeTests()
        {
            var data = SizedPoints();

            var scaled = ScaleBy(data, string.Empty, 0.5f, 2.0f, false, true);
            var result = scaled.PrimaryOutput;

            check(Near(result.Points[0].Scale.X, 0.5f) && Near(result.Points[1].Scale.X, 1.25f) &&
                  Near(result.Points[2].Scale.X, 2.0f),
                "the column maps onto the scale range, both ends included",
                $"{result.Points[0].Scale.X:F3}, {result.Points[1].Scale.X:F3}, {result.Points[2].Scale.X:F3}");

            check(Near(PCGPointTransform.HalfExtents(result.Points[2].Bounds).X, 4.0f) &&
                  Near(PCGPointTransform.HalfExtents(result.Points[0].Bounds).X, 1.0f),
                "the bounds grow and shrink with the point, or pruning would cull what is still visible",
                PCGPointTransform.HalfExtents(result.Points[2].Bounds).ToString());

            var inverted = ScaleBy(data, string.Empty, 0.5f, 2.0f, true, true);
            check(Near(inverted.PrimaryOutput.Points[0].Scale.X, 2.0f) &&
                  Near(inverted.PrimaryOutput.Points[2].Scale.X, 0.5f),
                "inverting makes a low column mean a large point",
                $"{inverted.PrimaryOutput.Points[0].Scale.X:F3}, {inverted.PrimaryOutput.Points[2].Scale.X:F3}");

            // 这个系数是相乘而不是替换，所以上游 Transform Points 的缩放会活下来，
            // 而且两个这样的节点可以复合。
            var heavy = new[] { PCGPoint.Create(Vector3.Zero, 1.0f, 0) };
            heavy[0].Scale = new Float3(2.0f, 2.0f, 2.0f);
            heavy[0].UpdateBounds(new Vector3(2.0f, 2.0f, 2.0f));

            check(Near(ScaleBy(Data(heavy), string.Empty, 0.5f, 2.0f, false, true).PrimaryOutput.Points[0].Scale.X, 4.0f),
                "the factor multiplies the point's own scale rather than replacing it",
                ScaleBy(Data(heavy), string.Empty, 0.5f, 2.0f, false, true).PrimaryOutput.Points[0].Scale.X.ToString("F3"));

            // 一个来路不明的列值不许把一件道具抬成地标。
            var extreme = Data(new[] { PCGPoint.Create(Vector3.Zero, 1.0f, 0) });
            extreme.SharedAttributes = Column(extreme, "w", new[] { 3.0f });

            var clamped = ScaleBy(extreme, "w", 0.5f, 2.0f, false, true);
            check(Near(clamped.PrimaryOutput.Points[0].Scale.X, 2.0f),
                "a column value above one is clamped rather than extrapolated",
                clamped.PrimaryOutput.Points[0].Scale.X.ToString("F3"));

            var boxed = ScaleBy(data, string.Empty, 0.5f, 2.0f, false, false);
            check(Near(boxed.PrimaryOutput.Points[2].Scale.X, 2.0f) &&
                  Near(PCGPointTransform.HalfExtents(boxed.PrimaryOutput.Points[2].Bounds).X, 2.0f),
                "with bounds scaling off the box stays exactly as it was",
                PCGPointTransform.HalfExtents(boxed.PrimaryOutput.Points[2].Bounds).ToString());

            // 具名的列会被读到；缺列时这一层的尺寸不动。具名那一例是正向孪生项，
            // 少了它，"没变"可能只是因为节点什么都没干。
            var named = Data(new[] { PCGPoint.Create(Vector3.Zero, 0.2f, 0) });
            named.SharedAttributes = Column(named, "w", new[] { 1.0f });
            check(Near(ScaleBy(named, "w", 0.5f, 2.0f, false, true).PrimaryOutput.Points[0].Scale.X, 2.0f),
                "a named column is used in place of the density",
                ScaleBy(named, "w", 0.5f, 2.0f, false, true).PrimaryOutput.Points[0].Scale.X.ToString("F3"));

            var missing = ScaleBy(data, "notThere", 0.5f, 2.0f, false, true);
            check(missing.PrimaryOutput.Count == 3 && Near(missing.PrimaryOutput.Points[2].Scale.X, 1.0f),
                "a missing column leaves every point's size as it was", missing.PrimaryOutput.Count.ToString());

            check(ScaleBy(PCGData.Empty, string.Empty, 0.5f, 2.0f, false, true).PrimaryOutput.Count == 0,
                "an empty layer stays empty");
        }

        private static PCGData SizedPoints()
        {
            var points = new[]
            {
                PCGPoint.Create(Vector3.Zero, 0.0f, 0),
                PCGPoint.Create(new Vector3(10.0f, 0.0f, 0.0f), 0.5f, 1),
                PCGPoint.Create(new Vector3(20.0f, 0.0f, 0.0f), 1.0f, 2),
            };

            for (int i = 0; i < points.Length; i++)
            {
                points[i].Scale = new Float3(1.0f, 1.0f, 1.0f);
                points[i].UpdateBounds(new Vector3(2.0f, 2.0f, 2.0f));
            }

            return Data(points);
        }

        private static PCGExecutionResult ScaleBy(
            PCGData input, string attribute, float min, float max, bool invert, bool scaleBounds)
        {
            var node = new PCGScaleByAttributeNode(Serialization(PCGScaleByAttributeNode.Id,
                new object[] { attribute, min, max, invert, scaleBounds }));

            return Run(node, input, PCGData.Empty);
        }

        private static void OffsetByAttributeTests()
        {
            var data = SizedPoints();

            var lifted = OffsetBy(data, string.Empty, PCGOffsetByAttributeNode.OffsetAxis.Y, 0.0f, 100.0f, false);
            var result = lifted.PrimaryOutput;

            check(Close(result.Points[0].Position, new Vector3(0.0f, 0.0f, 0.0f)) &&
                  Close(result.Points[1].Position, new Vector3(10.0f, 50.0f, 0.0f)) &&
                  Close(result.Points[2].Position, new Vector3(20.0f, 100.0f, 0.0f)),
                "the column maps onto the distance range along the chosen axis",
                $"{result.Points[0].Position} {result.Points[1].Position} {result.Points[2].Position}");

            // 包围盒跟着点走，这与任何位置会变的场合用的是同一条规则。
            var boxCentre = (result.Points[2].Bounds.Minimum + result.Points[2].Bounds.Maximum) * 0.5f;
            check(Close(boxCentre, new Vector3(20.0f, 100.0f, 0.0f)) &&
                  Near(PCGPointTransform.HalfExtents(result.Points[2].Bounds).X, 2.0f),
                "a displaced point's bounds follow it, keeping their extents",
                $"centre={boxCentre} half={PCGPointTransform.HalfExtents(result.Points[2].Bounds)}");

            var sideways = OffsetBy(data, string.Empty, PCGOffsetByAttributeNode.OffsetAxis.X, 0.0f, 100.0f, false);
            check(Close(sideways.PrimaryOutput.Points[2].Position, new Vector3(120.0f, 0.0f, 0.0f)),
                "the axis parameter decides which way the column pushes",
                sideways.PrimaryOutput.Points[2].Position.ToString());

            var inverted = OffsetBy(data, string.Empty, PCGOffsetByAttributeNode.OffsetAxis.Y, 0.0f, 100.0f, true);
            check(Close(inverted.PrimaryOutput.Points[2].Position, new Vector3(20.0f, 0.0f, 0.0f)) &&
                  Close(inverted.PrimaryOutput.Points[0].Position, new Vector3(0.0f, 100.0f, 0.0f)),
                "inverting makes a low column mean the larger displacement",
                $"{inverted.PrimaryOutput.Points[0].Position} {inverted.PrimaryOutput.Points[2].Position}");

            // 范围为零就是空操作，这正是设计者对"什么都没配"的期待。
            var still = OffsetBy(data, string.Empty, PCGOffsetByAttributeNode.OffsetAxis.Y, 0.0f, 0.0f, false);
            check(Close(still.PrimaryOutput.Points[2].Position, new Vector3(20.0f, 0.0f, 0.0f)),
                "a zero range moves nothing", still.PrimaryOutput.Points[2].Position.ToString());

            var extreme = Data(new[] { PCGPoint.Create(Vector3.Zero, 1.0f, 0) });
            extreme.SharedAttributes = Column(extreme, "w", new[] { 3.0f });

            check(Close(OffsetBy(extreme, "w", PCGOffsetByAttributeNode.OffsetAxis.Y, 0.0f, 100.0f, false)
                        .PrimaryOutput.Points[0].Position, new Vector3(0.0f, 100.0f, 0.0f)),
                "a column value above one is clamped rather than extrapolated");

            // 具名列会被采用；缺列时这一层留在原处。具名那一例是正向孪生项，
            // 于是"没变"不能拿一个什么都不做的节点来充数。
            var named = Data(new[] { PCGPoint.Create(Vector3.Zero, 0.2f, 0) });
            named.SharedAttributes = Column(named, "w", new[] { 1.0f });
            check(Close(OffsetBy(named, "w", PCGOffsetByAttributeNode.OffsetAxis.Y, 0.0f, 100.0f, false)
                        .PrimaryOutput.Points[0].Position, new Vector3(0.0f, 100.0f, 0.0f)),
                "a named column is used in place of the density");

            var missing = OffsetBy(data, "notThere", PCGOffsetByAttributeNode.OffsetAxis.Y, 0.0f, 100.0f, false);
            check(missing.PrimaryOutput.Count == 3 &&
                  Close(missing.PrimaryOutput.Points[2].Position, new Vector3(20.0f, 0.0f, 0.0f)),
                "a missing column leaves every point where it was", missing.PrimaryOutput.Count.ToString());

            check(OffsetBy(PCGData.Empty, string.Empty, PCGOffsetByAttributeNode.OffsetAxis.Y, 0.0f, 100.0f, false)
                      .PrimaryOutput.Count == 0,
                "an empty layer stays empty");
        }

        private static PCGExecutionResult OffsetBy(
            PCGData input, string attribute, PCGOffsetByAttributeNode.OffsetAxis axis,
            float min, float max, bool invert)
        {
            var node = new PCGOffsetByAttributeNode(Serialization(PCGOffsetByAttributeNode.Id,
                new object[] { attribute, (int)axis, min, max, invert }));

            return Run(node, input, PCGData.Empty);
        }

        private static void RotateByAttributeTests()
        {
            var data = SizedPoints();

            var turned = RotateBy(data, string.Empty, 0.0f, 90.0f, false).PrimaryOutput;

            // 密度 0 / 0.5 / 1 映到 0 / 45 / 90 度，而前进轴自己会说明这一点，
            // 不需要谁来约定符号：不转时它不动，四分之一圈把它放到 X 上，
            // 半圈则把它平摊到 X 与 Z 之间。
            var none = Vector3.Transform(Vector3.Forward, turned.Points[0].Rotation);
            var quarter = Vector3.Transform(Vector3.Forward, turned.Points[2].Rotation);
            var eighth = Vector3.Transform(Vector3.Forward, turned.Points[1].Rotation);

            check(Close(none, Vector3.Forward),
                "a zero column leaves the point facing where it did", none.ToString());
            check(Near(MathF.Abs(quarter.X), 1.0f) && Near(quarter.Y, 0.0f),
                "a quarter turn puts the forward axis on X", quarter.ToString());
            check(Near(MathF.Abs(eighth.X), 0.70710678f) && Near(MathF.Abs(eighth.Z), 0.70710678f),
                "half way through the range is a 45-degree turn", eighth.ToString());

            // 盒子是轴对齐的，所以绕 Y 的四分之一圈会换掉它的 X 与 Z 范围。这是一个
            // 关于盒子的手算事实，正因如此它能不借节点自己的算术来断言。
            var box = RotateBy(Turnable(), string.Empty, 90.0f, 90.0f, false).PrimaryOutput;
            var half = PCGPointTransform.HalfExtents(box.Points[0].Bounds);

            check(Near(half.X, 2.0f) && Near(half.Z, 4.0f) && Near(half.Y, 1.0f),
                "the bounds follow the turn: a quarter turn swaps the box's X and Z extents",
                half.ToString());

            var upright = RotateBy(Turnable(), string.Empty, 90.0f, 90.0f, false).PrimaryOutput;
            check(Close(Vector3.Transform(Vector3.Up, upright.Points[0].Rotation),
                        Vector3.Transform(Vector3.Up, Quaternion.Identity)),
                "turning about the point's own up axis leaves that axis where it was");

            var inverted = RotateBy(data, string.Empty, 0.0f, 90.0f, true).PrimaryOutput;
            check(Close(Vector3.Transform(Vector3.Forward, inverted.Points[2].Rotation), Vector3.Forward) &&
                  Near(MathF.Abs(Vector3.Transform(Vector3.Forward, inverted.Points[0].Rotation).X), 1.0f),
                "inverting makes a low column mean the larger turn");

            var still = RotateBy(data, string.Empty, 0.0f, 0.0f, false).PrimaryOutput;
            check(Close(Vector3.Transform(Vector3.Forward, still.Points[2].Rotation), Vector3.Forward),
                "a zero range turns nothing");

            // 具名列会被读到；缺列时这一层保持原样。具名那一例是正向孪生项，
            // 少了它，"没变"就能拿一个什么都不做的节点蒙过去。
            var named = Turnable();
            named.SharedAttributes = Column(named, "w", new[] { 1.0f });
            check(Near(MathF.Abs(Vector3.Transform(Vector3.Forward,
                        RotateBy(named, "w", 0.0f, 90.0f, false).PrimaryOutput.Points[0].Rotation).X), 1.0f),
                "a named column is used in place of the density");

            var missing = RotateBy(data, "notThere", 0.0f, 90.0f, false);
            check(missing.PrimaryOutput.Count == 3 &&
                  Close(Vector3.Transform(Vector3.Forward, missing.PrimaryOutput.Points[2].Rotation), Vector3.Forward),
                "a missing column leaves every point facing where it did", missing.PrimaryOutput.Count.ToString());

            check(RotateBy(PCGData.Empty, string.Empty, 0.0f, 90.0f, false).PrimaryOutput.Count == 0,
                "an empty layer stays empty");
        }

        private static PCGData Turnable()
        {
            var points = new[] { PCGPoint.Create(Vector3.Zero, 1.0f, 0) };
            points[0].Rotation = Quaternion.Identity;
            points[0].UpdateBounds(new Vector3(4.0f, 1.0f, 2.0f));

            return Data(points);
        }

        private static PCGExecutionResult RotateBy(PCGData input, string attribute, float min, float max, bool invert)
        {
            var node = new PCGRotateByAttributeNode(Serialization(PCGRotateByAttributeNode.Id,
                new object[] { attribute, min, max, invert }));

            return Run(node, input, PCGData.Empty);
        }

        private static void SortTests()
        {
            float[] densities = { 0.1f, 0.9f, 0.5f, 0.9f, 0.2f, 0.5f };
            var points = new PCGPoint[densities.Length];

            for (int i = 0; i < points.Length; i++)
            {
                points[i] = PCGPoint.Create(new Vector3(i * 10.0f, i * 3.0f, -i * 2.0f), densities[i], i);
                points[i].UpdateBounds(new Vector3(5.0f, 5.0f, 5.0f));
            }

            var data = Data(points);

            // 最密的排前面，平手时保持这一层产出的顺序：断言的是精确的 seed 序列，
            // 因为那才是要的性质——一个不稳定的排序也能过"密度递减"这种检查。
            var byDensity = Sort(data, PCGSortPointsNode.SortKey.Density, string.Empty, true);
            check(Seeds(byDensity.PrimaryOutput) == "1,3,2,5,4,0",
                "density descending, with ties resolved by the input order",
                Seeds(byDensity.PrimaryOutput));

            var ascending = Sort(data, PCGSortPointsNode.SortKey.Density, string.Empty, false);
            check(Seeds(ascending.PrimaryOutput) == "0,4,2,5,1,3",
                "flipping the direction reverses the order and still breaks ties by input order",
                Seeds(ascending.PrimaryOutput));

            var byHeight = Sort(data, PCGSortPointsNode.SortKey.Height, string.Empty, false);
            bool nonDecreasing = true;
            var heights = byHeight.PrimaryOutput.Points;

            for (int i = 1; i < (heights?.Length ?? 0); i++)
            {
                if (heights[i].Position.Y < heights[i - 1].Position.Y)
                    nonDecreasing = false;
            }

            check(nonDecreasing && Seeds(byHeight.PrimaryOutput) == "0,1,2,3,4,5",
                "sorting by height ascending leaves an already-ordered set alone",
                Seeds(byHeight.PrimaryOutput));

            // 按一列没人写过的属性排序，是一张跑在采样器前面的图：顺序必须成立，
            // 不许抛异常，也不许把顺序打乱。
            var unwritten = Sort(data, PCGSortPointsNode.SortKey.Attribute, "priority", true);
            check(Seeds(unwritten.PrimaryOutput) == "0,1,2,3,4,5",
                "an attribute nobody wrote sorts as all-equal, leaving the input order intact",
                Seeds(unwritten.PrimaryOutput));

            var unnamed = Sort(data, PCGSortPointsNode.SortKey.Attribute, string.Empty, true);
            check(Seeds(unnamed.PrimaryOutput) == "0,1,2,3,4,5",
                "sorting by an attribute with no name is refused by name, not guessed at",
                Seeds(unnamed.PrimaryOutput));

            // 真实属性：节点必须读得到这些值，而且那一份集合必须跟着点走，否则
            // 下一个读该属性的会看到一层的值挂在另一层的点上。
            var priorities = new[] { 5.0f, 1.0f, 4.0f, 2.0f, 3.0f, 0.0f };
            var attributed = Data(points);
            var attributes = new PCGAttributeSet(points.Length);
            attributes.DeclareAttribute("priority", PCGAttributeType.Float);

            for (int i = 0; i < points.Length; i++)
            {
                attributes.SetFloat("priority", i, priorities[i]);
                attributed.Points[i].Attributes = attributes;
            }

            attributed.SharedAttributes = attributes;

            var byAttribute = Sort(attributed, PCGSortPointsNode.SortKey.Attribute, "priority", true);
            check(Seeds(byAttribute.PrimaryOutput) == "0,2,4,3,1,5",
                "sorting by an attribute orders by the values, not by the fallback",
                Seeds(byAttribute.PrimaryOutput));

            var sorted = byAttribute.PrimaryOutput;
            var expectedOrder = new[] { 0, 2, 4, 3, 1, 5 };
            bool gatheredWithPoints = true;

            for (int i = 0; i < (sorted?.Points?.Length ?? 0) && gatheredWithPoints; i++)
            {
                var set = sorted.Points[i].Attributes;

                if (set == null || !set.Contains("priority") || i >= expectedOrder.Length)
                {
                    gatheredWithPoints = false;
                    break;
                }

                // 排序后那一层的第 n 个点，必须带着排序结果里的第 n 个优先级。
                if (Math.Abs(set.GetFloat("priority", i) - priorities[expectedOrder[i]]) > 1e-6f)
                    gatheredWithPoints = false;
            }

            check(gatheredWithPoints,
                "the attribute set is gathered in the new order, so the values stay with their points",
                gatheredWithPoints ? "ok" : "a point carries a neighbour's value");

            var attributeAscending = Sort(attributed, PCGSortPointsNode.SortKey.Attribute, "priority", false);
            check(Seeds(attributeAscending.PrimaryOutput) == "5,1,3,4,2,0",
                "the attribute sort honours the direction too",
                Seeds(attributeAscending.PrimaryOutput));
        }

        // ---- 包围盒修改器 -------------------------------------------------

        private static void BoundsTests()
        {
            var points = new PCGPoint[3];

            for (int i = 0; i < points.Length; i++)
            {
                points[i] = PCGPoint.Create(new Vector3(i * 100.0f, i * 7.0f, -i * 3.0f), 0.7f, i);
                points[i].UpdateBounds(new Vector3(10.0f, 20.0f, 30.0f));
            }

            var data = Data(points);

            var replaced = Bounds(data, PCGBoundsModifierNode.BoundsMode.Set, new Vector3(50.0f, 60.0f, 70.0f), 1.0f);
            check(Close(PCGPointTransform.HalfExtents(replaced.PrimaryOutput.Points[0].Bounds), new Vector3(50.0f, 60.0f, 70.0f)),
                "Set replaces the box outright",
                $"{PCGPointTransform.HalfExtents(replaced.PrimaryOutput.Points[0].Bounds)}");

            var grown = Bounds(data, PCGBoundsModifierNode.BoundsMode.Add, new Vector3(5.0f, -10.0f, 100.0f), 1.0f);
            check(Close(PCGPointTransform.HalfExtents(grown.PrimaryOutput.Points[1].Bounds), new Vector3(15.0f, 10.0f, 130.0f)),
                "Add grows and shrinks each axis on its own",
                $"{PCGPointTransform.HalfExtents(grown.PrimaryOutput.Points[1].Bounds)}");

            var scaled = Bounds(data, PCGBoundsModifierNode.BoundsMode.Scale, Vector3.Zero, 2.5f);
            check(Close(PCGPointTransform.HalfExtents(scaled.PrimaryOutput.Points[2].Bounds), new Vector3(25.0f, 50.0f, 75.0f)),
                "Scale multiplies every axis",
                $"{PCGPointTransform.HalfExtents(scaled.PrimaryOutput.Points[2].Bounds)}");

            // 压扁的盒子不可能是作者想要的决定：剪枝器会让所有东西互相重叠，
            // 而距离检验会把它读成退化，所以守住下限是这个节点的职责。这里断言的是
            // "等于下限"而不是"至少是下限"——这一检查的第一版在节点对 -1e6 的内缩
            // 回答一个 999990 单位的盒子时照样通过，而那正好是压扁的反面。
            var collapsed = Bounds(data, PCGBoundsModifierNode.BoundsMode.Add, new Vector3(-1e6f, -1e6f, -1e6f), 1.0f);
            var collapsedHalf = PCGPointTransform.HalfExtents(collapsed.PrimaryOutput.Points[0].Bounds);
            check(Close(collapsedHalf, new Vector3(PCGBoundsModifierNode.MinHalfExtent), 1e-4f),
                "an inset larger than the box lands on the floor instead of becoming a huge box",
                $"{collapsedHalf}");

            var zeroScale = Bounds(data, PCGBoundsModifierNode.BoundsMode.Scale, Vector3.Zero, 0.0f);
            var zeroHalf = PCGPointTransform.HalfExtents(zeroScale.PrimaryOutput.Points[0].Bounds);
            check(zeroHalf.X > 0.0f && zeroHalf.Y > 0.0f && zeroHalf.Z > 0.0f,
                "a zero scale factor leaves a usable box rather than a degenerate one",
                $"{zeroHalf}");

            // 除了盒子以外的东西都原样带过去，这正是本节点能放在任何位置的缘故。
            bool preserved = true;
            var result = replaced.PrimaryOutput.Points;

            for (int i = 0; i < points.Length; i++)
            {
                if (result[i].Position != points[i].Position || result[i].Seed != points[i].Seed ||
                    Math.Abs(result[i].Density - points[i].Density) > 1e-6f)
                {
                    preserved = false;
                }
            }

            check(preserved, "positions, seeds and densities come through a bounds change unchanged");

            check(Bounds(PCGData.Empty, PCGBoundsModifierNode.BoundsMode.Set, new Vector3(10.0f), 1.0f)
                      .PrimaryOutput.Count == 0,
                "an empty input stays empty");
        }

        // ---- 属性运算 ---------------------------------------------------

        private static void AttributeOpTests()
        {
            var points = new PCGPoint[4];

            for (int i = 0; i < points.Length; i++)
            {
                points[i] = PCGPoint.Create(new Vector3(i * 10.0f, i * 4.0f, 0.0f), 0.25f * (i + 1), i);
                points[i].UpdateBounds(new Vector3(5.0f, 5.0f, 5.0f));
            }

            var data = Data(points);

            // 常量源，配上缩放与偏置。
            var constant = SetAttribute(data, "growth", PCGSetAttributeNode.ValueSource.Constant,
                2.0f, 3.0f, 1.0f, string.Empty);
            check(Column(constant.PrimaryOutput, "growth") == "7,7,7,7",
                "a constant source writes scale x constant + bias onto every point",
                Column(constant.PrimaryOutput, "growth"));

            var fromDensity = SetAttribute(data, "growth", PCGSetAttributeNode.ValueSource.Density,
                1.0f, 2.0f, 0.0f, string.Empty);
            check(Column(fromDensity.PrimaryOutput, "growth") == "0.5,1,1.5,2",
                "the density source reads each point's own density",
                Column(fromDensity.PrimaryOutput, "growth"));

            var fromHeight = SetAttribute(data, "growth", PCGSetAttributeNode.ValueSource.PositionY,
                1.0f, 1.0f, 0.0f, string.Empty);
            check(Column(fromHeight.PrimaryOutput, "growth") == "0,4,8,12",
                "the position source reads the axis it names",
                Column(fromHeight.PrimaryOutput, "growth"));

            // 写时复制的约定：进来的那一层不许在下头长出这一列。要用一份新数组，
            // 因为 PCGData 会接管递给它的那份数组——早先一版复用了前一个夹具的数组，
            // 盖在上面的那份属性就漏进了这一个里，看上去像是节点自己的缺陷。
            var source = Data(SamplePoints(4));
            var original = new PCGAttributeSet(points.Length);
            original.DeclareAttribute("seedValue", PCGAttributeType.Float);

            for (int i = 0; i < source.Points.Length; i++)
            {
                original.SetFloat("seedValue", i, i + 1.0f);
                source.Points[i].Attributes = original;
            }

            source.SharedAttributes = original;

            var copied = SetAttribute(source, "copy", PCGSetAttributeNode.ValueSource.Attribute,
                1.0f, 1.0f, 0.0f, "seedValue");

            check(Column(copied.PrimaryOutput, "copy") == "1,2,3,4",
                "an attribute source copies the column it names",
                Column(copied.PrimaryOutput, "copy"));
            check(!source.SharedAttributes.Contains("copy"),
                "the input layer does not gain the written column - the write is copy-on-write",
                string.Join(",", Column(source, "copy")));

            // 无名的写入按名字拒绝，并把这一层原样递出去。
            var unnamed = SetAttribute(Data(SamplePoints(4)), string.Empty, PCGSetAttributeNode.ValueSource.Constant,
                1.0f, 1.0f, 0.0f, string.Empty);
            check(unnamed.PrimaryOutput.Count == points.Length && unnamed.PrimaryOutput.Points[0].Attributes == null,
                "a write with no attribute name leaves the layer and its attributes alone",
                $"{unnamed.PrimaryOutput.Count} point(s), attributes={(unnamed.PrimaryOutput.Points[0].Attributes == null ? "none" : "present")}");

            // ---- 重映射 Remap ----

            var densityRemap = Remap(data, string.Empty, string.Empty, 0.0f, 1.0f, 10.0f, 20.0f, true);
            string densities = Densities(densityRemap.PrimaryOutput);
            check(densities == "12.5,15,17.5,20",
                "remapping density onto density maps the input range onto the output range",
                densities);

            var slopeColumn = Data(points);
            var slopeSet = new PCGAttributeSet(points.Length);
            slopeSet.DeclareAttribute("slope", PCGAttributeType.Float);
            float[] slopes = { 0.0f, 30.0f, 60.0f, 90.0f };

            for (int i = 0; i < points.Length; i++)
            {
                slopeSet.SetFloat("slope", i, slopes[i]);
                slopeColumn.Points[i].Attributes = slopeSet;
            }

            slopeColumn.SharedAttributes = slopeSet;

            // 陡的地方少放道具：把输出区间反过来，不需要第二个节点。
            var ontoDensity = Remap(slopeColumn, "slope", string.Empty, 0.0f, 90.0f, 1.0f, 0.0f, true);
            check(Densities(ontoDensity.PrimaryOutput) == "1,0.6667,0.3333,0",
                "a slope column remapped onto density is how steep ground loses props",
                Densities(ontoDensity.PrimaryOutput));

            var clamped = Remap(slopeColumn, "slope", string.Empty, 0.0f, 30.0f, 0.0f, 1.0f, true);
            check(Densities(clamped.PrimaryOutput) == "0,1,1,1",
                "clamping keeps values past the input range at the output end",
                Densities(clamped.PrimaryOutput));

            var unclamped = Remap(slopeColumn, "slope", string.Empty, 0.0f, 30.0f, 0.0f, 1.0f, false);
            check(Densities(unclamped.PrimaryOutput) == "0,1,2,3",
                "without clamping the mapping extrapolates, which is what a designer asking for it gets",
                Densities(unclamped.PrimaryOutput));

            // 输入区间是平的，说明这张图从没标定过：每个值都走到某一端，
            // 而不是去除以零。
            var flat = Remap(slopeColumn, "slope", string.Empty, 45.0f, 45.0f, 0.0f, 1.0f, true);
            check(Densities(flat.PrimaryOutput) == "0,0,1,1",
                "a zero-width input range resolves to the output ends instead of producing NaN",
                Densities(flat.PrimaryOutput));

            // 一列没人写过的东西会被安静地拒绝，这一层保持原样。
            var missing = Remap(data, "notThere", string.Empty, 0.0f, 1.0f, 5.0f, 5.0f, true);
            check(Densities(missing.PrimaryOutput) == Densities(data),
                "remapping a column nobody wrote passes the layer through unchanged",
                Densities(missing.PrimaryOutput));

            // 写属性时密度不动；写密度时其他列不动。
            var toColumn = Remap(slopeColumn, "slope", "steepness", 0.0f, 90.0f, 0.0f, 1.0f, true);
            check(Densities(toColumn.PrimaryOutput) == Densities(slopeColumn) &&
                      Column(toColumn.PrimaryOutput, "steepness") == "0,0.3333,0.6667,1",
                "a named target keeps density and fills the column instead",
                $"{Densities(toColumn.PrimaryOutput)} / {Column(toColumn.PrimaryOutput, "steepness")}");
        }

        /// <summary>属性夹具所用的四点布局，每次都新造一份数组。</summary>
        /// <remarks>
        /// 用工厂而不是共享数组：<c>new PCGData(points)</c> 会接管递给它的那份数组，所以
        /// 一个往自己点上盖属性的夹具，也会把同一份属性盖到用同一个数组建出的每一份数据上——
        /// 那是测试里的引用重叠缺陷，不是节点的缺陷。
        /// </remarks>
        private static PCGPoint[] SamplePoints(int count)
        {
            var points = new PCGPoint[count];

            for (int i = 0; i < count; i++)
            {
                points[i] = PCGPoint.Create(new Vector3(i * 10.0f, i * 4.0f, 0.0f), 0.25f * (i + 1), i);
                points[i].UpdateBounds(new Vector3(5.0f, 5.0f, 5.0f));
            }

            return points;
        }

        private static PCGExecutionResult SetAttribute(
            PCGData input, string name, PCGSetAttributeNode.ValueSource source,
            float constant, float scale, float bias, string sourceName)
        {
            var node = new PCGSetAttributeNode(Serialization(PCGSetAttributeNode.Id, new object[]
            {
                name, (int)source, constant, scale, bias, sourceName,
            }));

            return Run(node, input, PCGData.Empty);
        }

        private static PCGExecutionResult Remap(
            PCGData input, string sourceName, string targetName,
            float inMin, float inMax, float outMin, float outMax, bool clamp)
        {
            var node = new PCGRemapAttributeNode(Serialization(PCGRemapAttributeNode.Id, new object[]
            {
                sourceName, targetName, inMin, inMax, outMin, outMax, clamp,
            }));

            return Run(node, input, PCGData.Empty);
        }

        private static string Column(PCGData data, string name)
        {
            var set = data?.SharedAttributes;

            if (set == null || !set.Contains(name))
                return "missing";

            var builder = new System.Text.StringBuilder();

            for (int i = 0; i < (data?.Points?.Length ?? 0); i++)
            {
                if (i > 0)
                    builder.Append(',');

                builder.Append(Number(set.GetFloat(name, i)));
            }

            return builder.ToString();
        }

        private static string Densities(PCGData data)
        {
            var builder = new System.Text.StringBuilder();

            for (int i = 0; i < (data?.Points?.Length ?? 0); i++)
            {
                if (i > 0)
                    builder.Append(',');

                builder.Append(Number(data.Points[i].Density));
            }

            return builder.ToString();
        }

        /// <summary>四舍五入到四位小数，好让夹具能写出它期望的值而不带浮点噪声。</summary>
        private static string Number(float value) =>
            value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

        // ---- 选择（控制流） -------------------------------------------

        private static void SelectTests()
        {
            var a = Data(SamplePoints(4));
            var b = Scatter(3, 1, 50.0f, new Vector3(0.0f, 0.0f, 500.0f));

            var switchOn = Select(a, b, PCGSelectPointsNode.SelectionRule.Switch, true, string.Empty, true);
            var switchOff = Select(a, b, PCGSelectPointsNode.SelectionRule.Switch, false, string.Empty, true);

            check(Seeds(switchOn.PrimaryOutput) == "0,1,2,3" && switchOn.PrimaryOutput.Count == a.Count,
                "the switch passes the first layer through whole",
                $"{switchOn.PrimaryOutput.Count} point(s)");
            check(Seeds(switchOff.PrimaryOutput) == "0,1,2" && switchOff.PrimaryOutput.Count == b.Count,
                "flipping the switch passes the second layer instead - one layer, not a merge",
                $"{switchOff.PrimaryOutput.Count} point(s)");

            // 按数量选：这条规则省下的是"设计者得猜哪一个采样器产得多"。
            var bigger = Select(a, b, PCGSelectPointsNode.SelectionRule.PointCount, true, string.Empty, true);
            var smaller = Select(a, b, PCGSelectPointsNode.SelectionRule.PointCount, true, string.Empty, false);

            check(bigger.PrimaryOutput.Count == a.Count && smaller.PrimaryOutput.Count == b.Count,
                "the count rule picks the larger layer, and the smaller when told to",
                $"{bigger.PrimaryOutput.Count} / {smaller.PrimaryOutput.Count}");

            // 按属性均值选：两层的差别只在于被告知要长什么。
            var low = Data(SamplePoints(4));
            var high = Data(SamplePoints(4));
            low.SharedAttributes = Column(low, "growth", new[] { 1.0f, 1.0f, 1.0f, 1.0f });
            high.SharedAttributes = Column(high, "growth", new[] { 4.0f, 6.0f, 6.0f, 8.0f });

            var richer = Select(low, high, PCGSelectPointsNode.SelectionRule.AverageAttribute, true, "growth", true);
            var poorer = Select(low, high, PCGSelectPointsNode.SelectionRule.AverageAttribute, true, "growth", false);

            check(Column(richer.PrimaryOutput, "growth") == "4,6,6,8" &&
                      Column(poorer.PrimaryOutput, "growth") == "1,1,1,1",
                "the average rule picks the layer averaging higher on the named attribute",
                $"{Column(richer.PrimaryOutput, "growth")} / {Column(poorer.PrimaryOutput, "growth")}");

            // 平手必须有落点：每次运行都取第一个输入，而不是看浮点噪声。
            var tieA = Data(SamplePoints(4));
            var tieB = Data(SamplePoints(4));
            tieA.SharedAttributes = Column(tieA, "growth", new[] { 3.0f, 3.0f, 3.0f, 3.0f });
            tieB.SharedAttributes = Column(tieB, "growth", new[] { 3.0f, 3.0f, 3.0f, 3.0f });

            var tied = Select(tieA, tieB, PCGSelectPointsNode.SelectionRule.AverageAttribute, true, "growth", true);
            check(Seeds(tied.PrimaryOutput) == "0,1,2,3" && tied.PrimaryOutput.Count == tieA.Count,
                "an exact tie resolves to the first input, so the world does not depend on float noise",
                $"{tied.PrimaryOutput.Count} point(s)");

            // 从没拿到那个属性的一层没法求均值：按名字说明原因，并采用第一个输入。
            var missing = Select(a, high, PCGSelectPointsNode.SelectionRule.AverageAttribute, true, "growth", true);
            check(Seeds(missing.PrimaryOutput) == "0,1,2,3",
                "an attribute missing on one side is reported and the first input is used",
                Seeds(missing.PrimaryOutput));

            // 有空的一侧：存活者就是答案；两个都空才是唯一的空结果。
            var onlyB = Select(PCGData.Empty, b, PCGSelectPointsNode.SelectionRule.Switch, true, string.Empty, true);
            var onlyA = Select(a, PCGData.Empty, PCGSelectPointsNode.SelectionRule.Switch, false, string.Empty, true);
            var neither = Select(PCGData.Empty, PCGData.Empty, PCGSelectPointsNode.SelectionRule.Switch, true,
                string.Empty, true);

            check(onlyB.PrimaryOutput.Count == b.Count && onlyA.PrimaryOutput.Count == a.Count &&
                      neither.PrimaryOutput.Count == 0,
                "an empty side is not a decision: the surviving layer passes, and both empty yields empty",
                $"{onlyB.PrimaryOutput.Count} / {onlyA.PrimaryOutput.Count} / {neither.PrimaryOutput.Count}");
        }

        /// <summary>给一份数据的点与共享存储挂上一列浮点值。</summary>
        private static PCGAttributeSet Column(PCGData data, string name, float[] values)
        {
            var set = new PCGAttributeSet(data.Count);
            set.DeclareAttribute(name, PCGAttributeType.Float);

            for (int i = 0; i < data.Count && i < values.Length; i++)
            {
                set.SetFloat(name, i, values[i]);
                data.Points[i].Attributes = set;
            }

            return set;
        }

        private static PCGExecutionResult Select(
            PCGData a, PCGData b, PCGSelectPointsNode.SelectionRule rule,
            bool useFirst, string attribute, bool preferHigher)
        {
            var node = new PCGSelectPointsNode(Serialization(PCGSelectPointsNode.Id, new object[]
            {
                (int)rule, useFirst, attribute, preferHigher,
            }));

            return Run(node, a, b);
        }

        // ---- 点邻域 ---------------------------------------------

        private static void NeighborhoodTests()
        {
            // 一条线上相距 10 的三个点：半径 15 时中间那个有两个邻居、两端的各有一个，
            // 所以期望的计数值得精确写出来，而不是"大于零"。
            var line = Data(LinePoints(3, 10.0f, 0.0f));
            var counted = Neighborhood(line, 15.0f, "neighbors", string.Empty, "neighborAverage", false, true);

            check(Column(counted.PrimaryOutput, "neighbors") == "1,2,1",
                "the neighbour count is the number of points inside the radius",
                Column(counted.PrimaryOutput, "neighbors"));

            var withSelf = Neighborhood(line, 15.0f, "neighbors", string.Empty, "neighborAverage", true, true);
            check(Column(withSelf.PrimaryOutput, "neighbors") == "2,3,2",
                "counting self shifts every count by the point itself",
                Column(withSelf.PrimaryOutput, "neighbors"));

            // 由邻居平均出来的属性：值 1、2、3 意味着每个点看到的均值都是 2。
            var valued = Data(LinePoints(3, 10.0f, 0.0f));
            valued.SharedAttributes = Column(valued, "value", new[] { 1.0f, 2.0f, 3.0f });

            var averaged = Neighborhood(valued, 15.0f, "neighbors", "value", "neighborAverage", false, true);
            check(Column(averaged.PrimaryOutput, "neighborAverage") == "2,2,2",
                "the average is taken over the neighbours, excluding the point itself",
                Column(averaged.PrimaryOutput, "neighborAverage"));

            var averagedWithSelf = Neighborhood(valued, 15.0f, "neighbors", "value", "neighborAverage", true, true);
            check(Column(averagedWithSelf.PrimaryOutput, "neighborAverage") == "1.5,2,2.5",
                "including self pulls each average towards the point's own value",
                Column(averagedWithSelf.PrimaryOutput, "neighborAverage"));

            // 高度：第三个点与前两个同列，却坐在上方 1000 单位处。平面模式下
            // 每一对都在半径内（于是三个点都看到两个邻居）；实体模式下只有地上那两个
            // 点配对。
            var stacked = Data(StackedPoints());
            var plane = Neighborhood(stacked, 15.0f, "neighbors", string.Empty, "neighborAverage", false, true);
            var solid = Neighborhood(stacked, 15.0f, "neighbors", string.Empty, "neighborAverage", false, false);

            check(Column(plane.PrimaryOutput, "neighbors") == "2,2,2" &&
                      Column(solid.PrimaryOutput, "neighbors") == "1,1,0",
                "plane mode counts the point 1000 units up and solid mode does not",
                $"{Column(plane.PrimaryOutput, "neighbors")} / {Column(solid.PrimaryOutput, "neighbors")}");

            // 半径为零是一张没填完的图：按名字拒绝，而不是写出一堆零。
            var noRadius = Neighborhood(line, 0.0f, "neighbors", string.Empty, "neighborAverage", false, true);
            var noRadiusSet = noRadius.PrimaryOutput.SharedAttributes;

            check(noRadius.PrimaryOutput.Count == line.Count &&
                      (noRadiusSet == null || !noRadiusSet.Contains("neighbors")),
                "a zero radius passes the layer through without writing a column",
                noRadiusSet == null ? "no attributes at all" : Column(noRadius.PrimaryOutput, "neighbors"));

            // 写时复制，与其他写属性的节点一致。这里要设守卫，因为这个夹具一个属性都没有：
            // "没多出这一列"会因为没有存放它的地方而自动满足；
            // 少了那道守卫就穿过那份集合去断言，得到的会是空引用解引用，而不是一次测试失败。
            var lineSet = line.SharedAttributes;
            check(lineSet == null || !lineSet.Contains("neighbors"),
                "the input layer does not gain the neighbour column",
                lineSet == null ? "no attributes at all" : Column(line, "neighbors"));

            // 一个不存在的求均值来源会被报告出来，而只写入数量。
            var missingAverage = Neighborhood(line, 15.0f, "neighbors", "notThere", "neighborAverage", false, true);
            check(Column(missingAverage.PrimaryOutput, "neighborAverage") == "missing" &&
                      Column(missingAverage.PrimaryOutput, "neighbors") == "1,2,1",
                "an average whose source is missing is reported and only the count lands",
                Column(missingAverage.PrimaryOutput, "neighborAverage"));
        }

        private static PCGPoint[] LinePoints(int count, float spacing, float y)
        {
            var points = new PCGPoint[count];

            for (int i = 0; i < count; i++)
            {
                points[i] = PCGPoint.Create(new Vector3(i * spacing, y, 0.0f), 1.0f, i);
                points[i].UpdateBounds(new Vector3(5.0f, 5.0f, 5.0f));
            }

            return points;
        }

        private static PCGPoint[] StackedPoints() => new[]
        {
            PCGPoint.Create(new Vector3(0.0f, 0.0f, 0.0f), 1.0f, 0),
            PCGPoint.Create(new Vector3(10.0f, 0.0f, 0.0f), 1.0f, 1),
            PCGPoint.Create(new Vector3(0.0f, 1000.0f, 0.0f), 1.0f, 2),
        };

        private static PCGExecutionResult Neighborhood(
            PCGData input, float radius, string countAttribute, string averageSource,
            string averageResult, bool includeSelf, bool planeOnly)
        {
            var node = new PCGPointNeighborhoodNode(Serialization(PCGPointNeighborhoodNode.Id, new object[]
            {
                radius, countAttribute, averageSource, averageResult, includeSelf, planeOnly,
            }));

            return Run(node, input, PCGData.Empty);
        }

        // ---- 点位松弛 ----------------------------------------------------

        private static void RelaxTests()
        {
            // 刻意摆得很紧：一格内间隔 1 单位的 3x3 一团。松弛应当把它摊开；
            // "跑过了"不是受检验的性质，任意两点之间的最小间隔才是。
            var clump = Data(Clump(3, 1.0f));
            var spread = Relax(clump, 8, 10.0f, 0.5f, 5.0f, true);

            float before = MinimumGap(clump.Points);
            float after = MinimumGap(spread.PrimaryOutput.Points);

            check(before < 1.01f && after > before * 1.05f,
                "relaxing a clump increases the smallest gap between points",
                $"{before:0.###} -> {after:0.###}");

            // 有界：任何点移动的幅度不许超过迭代次数乘最大步长，也不许离开这一团的邻域，
            // 因为一件道具被瞬移出它的格子，就是站错在地面上的道具。
            float travelled = 0.0f;

            for (int i = 0; i < spread.PrimaryOutput.Count; i++)
            {
                var delta = spread.PrimaryOutput.Points[i].Position - clump.Points[i].Position;
                travelled = Math.Max(travelled, delta.Length);
            }

            check(travelled <= 8 * 5.0f + 0.001f,
                "no point moves further than iterations x max step",
                $"{travelled:0.###} of {8 * 5.0f:0.#}");

            // 平面模式保持高度：这是落地撒点，把石头抬起来不算松弛。
            bool heightKept = true;

            for (int i = 0; i < spread.PrimaryOutput.Count; i++)
            {
                if (Math.Abs(spread.PrimaryOutput.Points[i].Position.Y - clump.Points[i].Position.Y) > 1e-4f)
                    heightKept = false;
            }

            check(heightKept, "plane mode leaves every point on its own height");

            // 上下叠着的两个点：在平面上彼此看不见，在三维里则被推开。
            var stacked = Data(new[]
            {
                PCGPoint.Create(Vector3.Zero, 1.0f, 0),
                PCGPoint.Create(new Vector3(0.0f, 1.0f, 0.0f), 1.0f, 1),
            });

            var stackedPlane = Relax(stacked, 4, 10.0f, 0.5f, 5.0f, true);
            var stackedSolid = Relax(stacked, 4, 10.0f, 0.5f, 5.0f, false);
            float planeGap = Math.Abs(stackedPlane.PrimaryOutput.Points[1].Position.Y -
                                       stackedPlane.PrimaryOutput.Points[0].Position.Y);
            float solidGap = Math.Abs(stackedSolid.PrimaryOutput.Points[1].Position.Y -
                                      stackedSolid.PrimaryOutput.Points[0].Position.Y);

            check(Math.Abs(planeGap - 1.0f) < 1e-4f && solidGap > 1.0f,
                "a vertical neighbour is invisible on the plane and pushes apart in 3D",
                $"plane {planeGap:0.###}, solid {solidGap:0.###}");

            // 开关关掉：没有迭代次数或没有力度时必须原样递出去，而不是造出一个略不同的世界。
            var noIterations = Relax(clump, 0, 10.0f, 0.5f, 5.0f, true);
            var noStrength = Relax(clump, 8, 10.0f, 0.0f, 5.0f, true);

            check(Seeds(noIterations.PrimaryOutput) == Seeds(clump) &&
                      SamePositions(noIterations.PrimaryOutput, clump) &&
                      SamePositions(noStrength.PrimaryOutput, clump),
                "zero iterations or zero strength leaves every position exactly where it was");

            // 同样的输入、同样的输出——双缓冲的那些遍次正是这件事成立的原因。
            var repeat = Relax(clump, 8, 10.0f, 0.5f, 5.0f, true);
            check(SamePositions(repeat.PrimaryOutput, spread.PrimaryOutput),
                "the same input relaxes to the same positions every run");

            // 顺序、数量、seed 与属性都跟着这次移动活下来。
            check(Seeds(spread.PrimaryOutput) == Seeds(clump) && spread.PrimaryOutput.Count == clump.Count,
                "points keep their count and their order while moving");

            check(Relax(PCGData.Empty, 4, 10.0f, 0.5f, 5.0f, true).PrimaryOutput.Count == 0 &&
                      Relax(Data(LinePoints(1, 10.0f, 0.0f)), 4, 10.0f, 0.5f, 5.0f, true)
                          .PrimaryOutput.Count == 1,
                "an empty layer stays empty and a lone point has nothing to be pushed by");
        }

        private static PCGPoint[] Clump(int side, float spacing)
        {
            var points = new PCGPoint[side * side];
            int index = 0;

            for (int z = 0; z < side; z++)
            {
                for (int x = 0; x < side; x++)
                {
                    points[index] = PCGPoint.Create(new Vector3(x * spacing, 0.0f, z * spacing), 1.0f, index);
                    points[index].UpdateBounds(new Vector3(0.5f, 0.5f, 0.5f));
                    index++;
                }
            }

            return points;
        }

        /// <summary>任意两点之间的最小距离，用最普通的双层循环写出来。</summary>
        /// <remarks>
        /// 刻意不用节点自己的网格：给松弛打分的那把尺子，不该和松弛所信赖的是同一套
        /// 机器——参照要独立另写，否则两边同一个错误会互相盖章。
        /// </remarks>
        private static float MinimumGap(PCGPoint[] points)
        {
            float smallest = float.MaxValue;

            for (int i = 0; i < points.Length; i++)
            {
                for (int j = i + 1; j < points.Length; j++)
                {
                    float gap = (points[i].Position - points[j].Position).Length;

                    if (gap < smallest)
                        smallest = gap;
                }
            }

            return smallest;
        }

        private static bool SamePositions(PCGData a, PCGData b)
        {
            if (a.Count != b.Count)
                return false;

            for (int i = 0; i < a.Count; i++)
            {
                if ((a.Points[i].Position - b.Points[i].Position).Length > 1e-5f)
                    return false;
            }

            return true;
        }

        private static PCGExecutionResult Relax(
            PCGData input, int iterations, float radius, float strength, float maxStep, bool planeOnly)
        {
            var node = new PCGRelaxPointsNode(Serialization(PCGRelaxPointsNode.Id, new object[]
            {
                iterations, radius, strength, maxStep, planeOnly,
            }));

            return Run(node, input, PCGData.Empty);
        }

        // ---- 属性运算 --------------------------------------------------

        private static void AttributeMathTests()
        {
            var data = MathFixture();

            // 把密度减半：常量那一路，也是设计者第一个伸手用的。夹具里的密度是
            // 0.25、0.5、0.75、1——由采样器写明的，不是我假设的。
            var halved = Combine(data, PCGAttributeMathNode.MathOperation.Multiply, string.Empty, string.Empty,
                0.5f, string.Empty, true);
            check(Densities(halved.PrimaryOutput) == "0.125,0.25,0.375,0.5",
                "density times a constant halves the layer",
                Densities(halved.PrimaryOutput));

            // 两列相加。钳位关掉，因为这些是计数而不是概率——而这恰恰是
            // 钳位做成可退出、而不是永远打开的理由。
            var summed = Combine(data, PCGAttributeMathNode.MathOperation.Add, "a", "b", 0.0f, string.Empty, false);
            check(Densities(summed.PrimaryOutput) == "3,6,10,8",
                "adding two columns gives per-point sums when clamping is off",
                Densities(summed.PrimaryOutput));

            var product = Combine(data, PCGAttributeMathNode.MathOperation.Multiply, "a", "b", 0.0f, string.Empty, false);
            check(Densities(product.PrimaryOutput) == "2,8,24,0",
                "multiplying two columns is the shape a spawn rule is built from",
                Densities(product.PrimaryOutput));

            // 第四个点的 b 是零：一个 NaN 密度会把后面每个阶段都毒掉，所以答案是 0，
            // 并且节点会说清它做了什么。
            var quotient = Combine(data, PCGAttributeMathNode.MathOperation.Divide, "a", "b", 0.0f, string.Empty, false);
            check(Densities(quotient.PrimaryOutput) == "2,2,1.5,0",
                "dividing by a zero column yields zero rather than an infinity",
                Densities(quotient.PrimaryOutput));

            var minimum = Combine(data, PCGAttributeMathNode.MathOperation.Min, "a", "b", 0.0f, string.Empty, false);
            var maximum = Combine(data, PCGAttributeMathNode.MathOperation.Max, "a", "b", 0.0f, string.Empty, false);
            check(Densities(minimum.PrimaryOutput) == "1,2,4,0" && Densities(maximum.PrimaryOutput) == "2,4,6,8",
                "min and max pick per point",
                $"{Densities(minimum.PrimaryOutput)} / {Densities(maximum.PrimaryOutput)}");

            // 钳位：先看它把活干了，再看它让开路。
            var clamped = Combine(data, PCGAttributeMathNode.MathOperation.Add, "a", "b", 0.0f, string.Empty, true);
            check(Densities(clamped.PrimaryOutput) == "1,1,1,1",
                "clamping keeps a sum-of-columns inside the probability range",
                Densities(clamped.PrimaryOutput));

            // 一列没人写过的具名列会被报告出来，而这一层原样递出去，不是被清零。
            var missing = Combine(data, PCGAttributeMathNode.MathOperation.Multiply, "notThere", string.Empty,
                0.5f, string.Empty, false);
            check(Densities(missing.PrimaryOutput) == Densities(data),
                "a source column that does not exist leaves the layer untouched",
                Densities(missing.PrimaryOutput));

            // 写进一个具名列时密度不动；写时复制让输入保持干净。
            var toColumn = Combine(data, PCGAttributeMathNode.MathOperation.Multiply, "a", "b", 0.0f, "combined", false);
            check(Column(toColumn.PrimaryOutput, "combined") == "2,8,24,0" &&
                      Densities(toColumn.PrimaryOutput) == Densities(data),
                "a named target fills its column and leaves density alone",
                Column(toColumn.PrimaryOutput, "combined"));

            var sourceSet = data.SharedAttributes;
            check(sourceSet != null && !sourceSet.Contains("combined"),
                "the input layer does not gain the combined column",
                Column(data, "combined"));

            check(Combine(PCGData.Empty, PCGAttributeMathNode.MathOperation.Add, string.Empty, string.Empty,
                      1.0f, string.Empty, true).PrimaryOutput.Count == 0,
                "an empty layer stays empty");
        }

        private static PCGData MathFixture()
        {
            var points = SamplePoints(4);
            var data = Data(points);
            var set = new PCGAttributeSet(points.Length);
            set.DeclareAttribute("a", PCGAttributeType.Float);
            set.DeclareAttribute("b", PCGAttributeType.Float);

            float[] a = { 2.0f, 4.0f, 6.0f, 8.0f };
            float[] b = { 1.0f, 2.0f, 4.0f, 0.0f };

            for (int i = 0; i < points.Length; i++)
            {
                set.SetFloat("a", i, a[i]);
                set.SetFloat("b", i, b[i]);
                data.Points[i].Attributes = set;
            }

            data.SharedAttributes = set;
            return data;
        }

        private static PCGExecutionResult Combine(
            PCGData input, PCGAttributeMathNode.MathOperation operation, string left, string right,
            float rightConstant, string target, bool clamp)
        {
            var node = new PCGAttributeMathNode(Serialization(PCGAttributeMathNode.Id, new object[]
            {
                (int)operation, left, right, rightConstant, target, clamp,
            }));

            return Run(node, input, PCGData.Empty);
        }

        // ---- 属性过滤器 ------------------------------------------------

        private static void AttributeFilterTests()
        {
            var data = MathFixture();

            var byDensity = Filter(data, string.Empty, 0.3f, 0.6f, false);
            check(Seeds(byDensity.PrimaryOutput) == "1",
                "filtering the built-in density keeps only the point inside the range",
                Seeds(byDensity.PrimaryOutput));

            var byColumn = Filter(data, "a", 3.0f, 7.0f, false);
            check(Seeds(byColumn.PrimaryOutput) == "1,2",
                "filtering a derived column keeps the points whose value is in range",
                Seeds(byColumn.PrimaryOutput));

            // 收拢来的那一层必须把属性一起带上，否则下一个节点读到的是邻居的值。
            check(Column(byColumn.PrimaryOutput, "a") == "4,6",
                "the surviving points keep their own attribute values",
                Column(byColumn.PrimaryOutput, "a"));

            var inverted = Filter(data, "a", 3.0f, 7.0f, true);
            check(Seeds(inverted.PrimaryOutput) == "0,3",
                "inverting keeps the complement rather than nothing",
                Seeds(inverted.PrimaryOutput));

            // 两端都闭，这是"落在 [3,7] 里"与"3 到 7 之间"的区别所在。
            var closed = Filter(data, "a", 4.0f, 6.0f, false);
            check(Seeds(closed.PrimaryOutput) == "1,2",
                "both bounds are inclusive, so the end values themselves survive",
                Seeds(closed.PrimaryOutput));

            // 一个写反了的区间仍然只代表那两个数，而不是"全都拒绝"。
            var reversed = Filter(data, "a", 7.0f, 3.0f, false);
            check(Seeds(reversed.PrimaryOutput) == "1,2",
                "a reversed range is read as the bounds the author meant",
                Seeds(reversed.PrimaryOutput));

            // 一列没人写过的属性会让这一层完整地留下，而不是在看不见的地方把它清空。
            var missing = Filter(data, "notThere", 3.0f, 7.0f, false);
            check(missing.PrimaryOutput.Count == data.Count && Seeds(missing.PrimaryOutput) == "0,1,2,3",
                "a missing column is reported and the layer passes through unfiltered",
                $"{missing.PrimaryOutput.Count} point(s)");

            check(Filter(PCGData.Empty, string.Empty, 0.0f, 1.0f, false).PrimaryOutput.Count == 0,
                "an empty layer stays empty");
        }

        private static PCGExecutionResult Filter(PCGData input, string attribute, float min, float max, bool invert)
        {
            var node = new PCGAttributeFilterNode(Serialization(PCGAttributeFilterNode.Id, new object[]
            {
                attribute, min, max, invert,
            }));

            return Run(node, input, PCGData.Empty);
        }

        // ---- 属性搬运 ----------------------------------------------

        private static void AttributeTransferTests()
        {
            // 源的值就是源的下标，于是目标最终拿到的那个值*就是*节点选中的下标。这把
            // "哪个点赢了"变成一个数，可以让一份暴力参照来比对，
            // 而两边都不必借用对方对"最近"的想法。
            var source = Scatter(4, 4, 100.0f, Vector3.Zero);
            source.SharedAttributes = Column(source, "v", IndexValues(source.Count));

            var onTop = Data(new[] { PCGPoint.Create(new Vector3(300.0f, 0.0f, 200.0f), 1.0f, 0) });
            var hit = Transfer(onTop, source, "v", 1000.0f, true);
            check(Column(hit.PrimaryOutput, "v") == "11",
                "a target sitting on a source point takes that point's value",
                Column(hit.PrimaryOutput, "v"));

            // 四个角的正中：四个一样远，所以结果只由打破平手的那条规则决定——
            // 规则是"源下标最低的那个"，整个答案都不许从这一点漂移。
            var midpoint = Data(new[] { PCGPoint.Create(new Vector3(150.0f, 0.0f, 150.0f), 1.0f, 0) });
            check(Column(Transfer(midpoint, source, "v", 1000.0f, true).PrimaryOutput, "v") == "5",
                "equally distant sources are resolved by the lowest source index",
                Column(Transfer(midpoint, source, "v", 1000.0f, true).PrimaryOutput, "v"));

            // 够不着：这一列仍然存在，好让下游节点读得到，但值是默认值。
            // 这刻意不是过滤器——那个点留着。
            var far = Data(new[] { PCGPoint.Create(new Vector3(5000.0f, 0.0f, 5000.0f), 1.0f, 0) });
            var missed = Transfer(far, source, "v", 200.0f, true);
            check(missed.PrimaryOutput.Count == 1 && Column(missed.PrimaryOutput, "v") == "0",
                "a target with no source inside the reach keeps the default value rather than being dropped",
                $"{missed.PrimaryOutput.Count} point(s), v={Column(missed.PrimaryOutput, "v")}");

            // 源上一列没人写过的东西会被报告，而这一层完整地递出去。
            var missing = Transfer(onTop, source, "notThere", 1000.0f, true);
            check(missing.PrimaryOutput.Count == onTop.Count,
                "a source missing the named column leaves the input unchanged",
                $"{missing.PrimaryOutput.Count} point(s)");

            // 空的名字意味着点自己的密度，而那并不是那份集合能交回来的属性。
            var donors = Data(new[]
            {
                PCGPoint.Create(new Vector3(0.0f, 0.0f, 0.0f), 0.25f, 0),
                PCGPoint.Create(new Vector3(100.0f, 0.0f, 0.0f), 0.75f, 1),
            });
            var densityTarget = Data(new[] { PCGPoint.Create(new Vector3(120.0f, 0.0f, 0.0f), 1.0f, 0) });
            var byDensity = Transfer(densityTarget, donors, string.Empty, 500.0f, true);
            check(MathF.Abs(byDensity.PrimaryOutput.Points[0].Density - 0.75f) < 1e-4f,
                "an empty attribute name transfers the built-in density",
                byDensity.PrimaryOutput.Points[0].Density.ToString("F3"));

            // 高度正是只看平面那个开关的意义：这两个点在两个源之间来回跳。
            var stacked = Data(new[]
            {
                PCGPoint.Create(new Vector3(0.0f, 0.0f, 0.0f), 1.0f, 0),
                PCGPoint.Create(new Vector3(5.0f, 1000.0f, 0.0f), 1.0f, 1),
            });
            stacked.SharedAttributes = Column(stacked, "v", new[] { 0.0f, 1.0f });
            var high = Data(new[] { PCGPoint.Create(new Vector3(0.0f, 1000.0f, 0.0f), 1.0f, 0) });

            check(Column(Transfer(high, stacked, "v", 5000.0f, false).PrimaryOutput, "v") == "1",
                "with height counted, the nearer-in-3D source wins even though it is offset in XZ",
                Column(Transfer(high, stacked, "v", 5000.0f, false).PrimaryOutput, "v"));
            check(Column(Transfer(high, stacked, "v", 5000.0f, true).PrimaryOutput, "v") == "0",
                "plane-only ignores the height difference and picks the source below it",
                Column(Transfer(high, stacked, "v", 5000.0f, true).PrimaryOutput, "v"));

            // 每个目标都对着一份独立写出的最近邻扫描来验。
            var targets = Scatter(5, 5, 60.0f, new Vector3(-30.0f, 0.0f, -30.0f));
            var transferred = Transfer(targets, source, "v", 1000.0f, true);

            var expected = new List<string>(targets.Count);
            for (int i = 0; i < targets.Count; i++)
                expected.Add(ReferenceNearest(source.Points, targets.Points[i].Position, 1000.0f, true).ToString());

            check(Column(transferred.PrimaryOutput, "v") == string.Join(",", expected),
                "every target agrees with a brute-force nearest-neighbour scan",
                Column(transferred.PrimaryOutput, "v"));

            // 同一份输入跑两遍：答案不许依赖哈希表恰好把候选
            // 交回来的那个顺序。
            check(Column(Transfer(targets, source, "v", 1000.0f, true).PrimaryOutput, "v") ==
                  Column(transferred.PrimaryOutput, "v"),
                "the same inputs produce the same transfer twice");
        }

        private static PCGExecutionResult Transfer(
            PCGData target, PCGData source, string attribute, float maxDistance, bool plane)
        {
            var node = new PCGAttributeTransferNode(Serialization(PCGAttributeTransferNode.Id,
                new object[] { attribute, maxDistance, plane }));

            return Run(node, target, source);
        }

        /// <summary>用显式双层循环求最近的源下标，与节点自己的网格不通气。</summary>
        private static int ReferenceNearest(PCGPoint[] source, Vector3 position, float maxDistance, bool plane)
        {
            float bestSquared = maxDistance * maxDistance;
            int best = -1;

            for (int j = 0; j < source.Length; j++)
            {
                float dx = source[j].Position.X - position.X;
                float dy = source[j].Position.Y - position.Y;
                float dz = source[j].Position.Z - position.Z;
                float squared = plane ? dx * dx + dz * dz : dx * dx + dy * dy + dz * dz;

                if (squared < bestSquared || (squared == bestSquared && (best < 0 || j < best)))
                {
                    bestSquared = squared;
                    best = j;
                }
            }

            return best;
        }

        private static float[] IndexValues(int count)
        {
            var values = new float[count];
            for (int i = 0; i < count; i++)
                values[i] = i;

            return values;
        }

        // ---- 匹配放置，而不只是搬一列 ---------------------------

        private static void TransferMatchTests()
        {
            var donors = MatchedDonors();
            var target = MatchedTarget();

            // 默认档：节点仍然只是照抄一列，所以目标的放置什么都不动。
            var untouched = TransferMatch(target, donors, "v", 1000.0f, true, PCGAttributeTransferNode.MatchMode.None);
            check(Close(untouched.PrimaryOutput.Points[0].Position, new Vector3(10.0f, 0.0f, 0.0f)) &&
                  Near(ReadFloat(untouched.PrimaryOutput, "v", 0), 7.0f),
                "with matching off the point keeps its placement and only the column arrives",
                untouched.PrimaryOutput.Points[0].Position.ToString());

            // 位置：搬到最近的捐赠者身上，保留它自己的旋转与缩放。
            var moved = TransferMatch(target, donors, "v", 1000.0f, true, PCGAttributeTransferNode.MatchMode.Position);
            var movedPoint = moved.PrimaryOutput.Points[0];

            check(Close(movedPoint.Position, new Vector3(0.0f, 5.0f, 0.0f)),
                "matching position lands the point on its donor", movedPoint.Position.ToString());
            check(Near(Quaternion.Identity.X, movedPoint.Rotation.X) && Near(1.0f, movedPoint.Rotation.W) &&
                  Near(movedPoint.Scale.X, 1.0f),
                "matching position leaves the point's own rotation and scale alone",
                $"{movedPoint.Rotation} scale={movedPoint.Scale}");

            // 包围盒必须跟着点走，否则下游的剪枝检验会算它原来所在的位置。
            var half = PCGPointTransform.HalfExtents(movedPoint.Bounds);
            var centre = (movedPoint.Bounds.Minimum + movedPoint.Bounds.Maximum) * 0.5f;
            check(Close(centre, new Vector3(0.0f, 5.0f, 0.0f)) && Near(half.X, 1.0f),
                "a matched point's bounds follow it, keeping its own extents",
                $"centre={centre} half={half}");

            // 变换：这个点整个替掉捐赠者，放置与其余一切照搬。
            var full = TransferMatch(target, donors, "v", 1000.0f, true, PCGAttributeTransferNode.MatchMode.Transform);
            var fullPoint = full.PrimaryOutput.Points[0];
            var donor = donors.Points[0];

            check(Close(fullPoint.Position, donor.Position) &&
                  Near(fullPoint.Rotation.X, donor.Rotation.X) && Near(fullPoint.Rotation.Y, donor.Rotation.Y) &&
                  Near(fullPoint.Rotation.Z, donor.Rotation.Z) && Near(fullPoint.Rotation.W, donor.Rotation.W) &&
                  Near(fullPoint.Scale.X, donor.Scale.X),
                "matching the transform adopts the donor's position, rotation and scale",
                $"{fullPoint.Position} {fullPoint.Rotation} {fullPoint.Scale}");
            check(Close(fullPoint.Bounds.Minimum, donor.Bounds.Minimum) &&
                  Close(fullPoint.Bounds.Maximum, donor.Bounds.Maximum),
                "matching the transform adopts the donor's box as well",
                $"{fullPoint.Bounds.Minimum}..{fullPoint.Bounds.Maximum}");

            // 一列不存在，不许把放置匹配一起取消掉——那才是被要求的功能；
            // 缺的那一列会被报告，并留在它的默认值上。
            var missing = TransferMatch(target, donors, "notThere", 1000.0f, true,
                PCGAttributeTransferNode.MatchMode.Transform);
            check(Close(missing.PrimaryOutput.Points[0].Position, donor.Position) &&
                  missing.PrimaryOutput.Count == 1,
                "a missing column is reported but still matches the placement",
                missing.PrimaryOutput.Points[0].Position.ToString());

            // 可达范围仍然管着匹配：范围外的捐赠者什么都改不了。
            var far = TransferMatch(target, donors, "v", 1.0f, true, PCGAttributeTransferNode.MatchMode.Transform);
            check(Close(far.PrimaryOutput.Points[0].Position, new Vector3(10.0f, 0.0f, 0.0f)),
                "a donor beyond the reach does not move the point",
                far.PrimaryOutput.Points[0].Position.ToString());

            // 一个倾斜的目标正是"搬盒子"与"重算盒子"分道扬镳的地方：重算会把
            // 点自己的旋转再施加一遍到本来就是世界空间的范围上，于是盒子被双层包住。
            var tilted = MatchedTarget();
            tilted.Points[0].Rotation = PCGPointTransform.EulerDegrees(new Vector3(0.0f, 0.0f, 45.0f));
            tilted.Points[0].UpdateBounds(new Vector3(2.0f, 2.0f, 2.0f));

            var before = PCGPointTransform.HalfExtents(tilted.Points[0].Bounds);
            var tiltedMatch = TransferMatch(tilted, donors, "v", 1000.0f, true,
                PCGAttributeTransferNode.MatchMode.Position);
            var after = PCGPointTransform.HalfExtents(tiltedMatch.PrimaryOutput.Points[0].Bounds);

            check(Near(after.X, before.X) && Near(after.Y, before.Y) && Near(after.Z, before.Z) &&
                  Close(tiltedMatch.PrimaryOutput.Points[0].Position, new Vector3(0.0f, 5.0f, 0.0f)),
                "matching position moves a tilted point's box without reshaping it",
                $"{before} -> {after}");
        }

        private static PCGData MatchedDonors()
        {
            var donors = new[]
            {
                PCGPoint.Create(new Vector3(0.0f, 5.0f, 0.0f), 1.0f, 0),
                PCGPoint.Create(new Vector3(500.0f, 5.0f, 0.0f), 1.0f, 1),
            };

            donors[0].Rotation = PCGPointTransform.EulerDegrees(new Vector3(0.0f, 90.0f, 0.0f));
            donors[0].Scale = new Float3(2.0f, 2.0f, 2.0f);
            donors[0].UpdateBounds(new Vector3(3.0f, 4.0f, 5.0f));

            donors[1].UpdateBounds(new Vector3(1.0f, 1.0f, 1.0f));

            var data = Data(donors);
            data.SharedAttributes = Column(data, "v", new[] { 7.0f, 9.0f });

            return data;
        }

        private static PCGData MatchedTarget()
        {
            var point = PCGPoint.Create(new Vector3(10.0f, 0.0f, 0.0f), 1.0f, 0);
            point.Rotation = Quaternion.Identity;
            point.Scale = new Float3(1.0f, 1.0f, 1.0f);
            point.UpdateBounds(new Vector3(1.0f, 1.0f, 1.0f));

            return Data(new[] { point });
        }

        private static PCGExecutionResult TransferMatch(
            PCGData target, PCGData source, string attribute, float maxDistance, bool plane,
            PCGAttributeTransferNode.MatchMode match)
        {
            var node = new PCGAttributeTransferNode(Serialization(PCGAttributeTransferNode.Id,
                new object[] { attribute, maxDistance, plane, (int)match }));

            return Run(node, target, source);
        }

        // ---- 到点的距离 ----------------------------------------------

        private static void DistanceToPointsTests()
        {
            var reference = Data(new[]
            {
                PCGPoint.Create(Vector3.Zero, 1.0f, 0),
                PCGPoint.Create(new Vector3(100.0f, 0.0f, 0.0f), 1.0f, 1),
            });

            var near = Data(new[] { PCGPoint.Create(new Vector3(0.0f, 0.0f, 30.0f), 1.0f, 0) });
            check(Near(ReadFloat(Distance(near, reference, "d", 1000.0f, true).PrimaryOutput, "d", 0), 30.0f),
                "the distance to the nearest reference point is written",
                ReadFloat(Distance(near, reference, "d", 1000.0f, true).PrimaryOutput, "d", 0).ToString("F3"));

            // 高度正是"只看平面"那个开关决定的：同一个点读出两个不同的数。
            var high = Data(new[] { PCGPoint.Create(new Vector3(0.0f, 500.0f, 0.0f), 1.0f, 0) });
            check(Near(ReadFloat(Distance(high, reference, "d", 1000.0f, true).PrimaryOutput, "d", 0), 0.0f),
                "plane-only ignores the height difference",
                ReadFloat(Distance(high, reference, "d", 1000.0f, true).PrimaryOutput, "d", 0).ToString("F3"));
            check(Near(ReadFloat(Distance(high, reference, "d", 1000.0f, false).PrimaryOutput, "d", 0), 500.0f),
                "with height counted the distance is the true 3D one",
                ReadFloat(Distance(high, reference, "d", 1000.0f, false).PrimaryOutput, "d", 0).ToString("F3"));

            // 超出可达范围：点留着并读到上限，而不是变成一份空层。
            var far = Data(new[] { PCGPoint.Create(new Vector3(10000.0f, 0.0f, 0.0f), 1.0f, 0) });
            var clamped = Distance(far, reference, "d", 250.0f, true);
            check(clamped.PrimaryOutput.Count == 1 && Near(ReadFloat(clamped.PrimaryOutput, "d", 0), 250.0f),
                "a target beyond the reach is clamped to the cap rather than dropped",
                $"{clamped.PrimaryOutput.Count} point(s), d={ReadFloat(clamped.PrimaryOutput, "d", 0):F3}");

            // 没东西可量时，这一层完整地递出去——而上面那个成功的例子就是
            // 正向孪生，它证明了这个读操作本来是会给出一个值的。
            var noReference = Distance(near, PCGData.Empty, "d", 1000.0f, true);
            check(noReference.PrimaryOutput.Count == near.Count,
                "an empty reference layer leaves the input unchanged",
                $"{noReference.PrimaryOutput.Count} point(s)");

            // 空的名字意味着点自己的密度，而那并不是那份集合能交回来的属性。
            var byDensity = Distance(near, reference, string.Empty, 1000.0f, true);
            check(Near(byDensity.PrimaryOutput.Points[0].Density, 30.0f),
                "an empty attribute name writes the built-in density",
                byDensity.PrimaryOutput.Points[0].Density.ToString("F3"));

            // 每个点都对着一份独立写出的暴力扫描来验。
            var targets = Scatter(5, 5, 60.0f, new Vector3(-30.0f, 0.0f, -30.0f));
            var written = Distance(targets, reference, "d", 1000.0f, true);

            bool allAgree = true;
            for (int i = 0; i < targets.Count; i++)
            {
                float expected = ReferenceNearestDistance(reference.Points, targets.Points[i].Position, 1000.0f, true);
                if (!Near(ReadFloat(written.PrimaryOutput, "d", i), expected))
                    allAgree = false;
            }

            check(allAgree, "every written distance agrees with a brute-force nearest scan");
        }

        private static PCGExecutionResult Distance(
            PCGData target, PCGData reference, string attribute, float maxDistance, bool plane)
        {
            var node = new PCGDistanceToPointsNode(Serialization(PCGDistanceToPointsNode.Id,
                new object[] { attribute, maxDistance, plane }));

            return Run(node, target, reference);
        }

        private static float ReadFloat(PCGData data, string name, int index) =>
            data.SharedAttributes.GetFloat(name, index);

        private static bool Near(float a, float b) => MathF.Abs(a - b) < 1e-3f;

        /// <summary>用显式双层循环求最近距离，与节点自己的网格不通气。</summary>
        private static float ReferenceNearestDistance(PCGPoint[] reference, Vector3 position, float cap, bool plane)
        {
            float bestSquared = cap * cap;

            for (int j = 0; j < reference.Length; j++)
            {
                float dx = reference[j].Position.X - position.X;
                float dy = reference[j].Position.Y - position.Y;
                float dz = reference[j].Position.Z - position.Z;
                float squared = plane ? dx * dx + dz * dz : dx * dx + dy * dy + dz * dz;

                if (squared < bestSquared)
                    bestSquared = squared;
            }

            return bestSquared >= cap * cap ? cap : MathF.Sqrt(bestSquared);
        }

        // ---- 噪声写属性 ----------------------------------------------

        private static void NoiseToAttributeTests()
        {
            const int Seed = 20261007; // the Run() helper's graph seed

            // 落在采样格点上的那个点会把双线性混合塌成一个角，所以答案正是
            // 构成这份噪声的那个哈希——一份节点自己的代码从不调用的参照。
            var lattice = Data(new[] { PCGPoint.Create(new Vector3(3.0f, 0.0f, 5.0f), 1.0f, 0) });
            var placed = Noise(lattice, "n", PCGNoiseKind.Value2D, 1.0f, 1, 0, true);
            check(Near(ReadFloat(placed.PrimaryOutput, "n", 0), PCGSeededNoise.HashToUnit(3, 5, Seed)),
                "a lattice-aligned sample equals the corner hash the noise is built from",
                ReadFloat(placed.PrimaryOutput, "n", 0).ToString("F6"));

            // 只看平面时绝不能看见高度。
            var raised = Data(new[] { PCGPoint.Create(new Vector3(3.0f, 999.0f, 5.0f), 1.0f, 0) });
            check(Near(ReadFloat(Noise(raised, "n", PCGNoiseKind.Value2D, 1.0f, 1, 0, true).PrimaryOutput, "n", 0),
                       PCGSeededNoise.HashToUnit(3, 5, Seed)),
                "plane-only ignores height, so the raised point reads the same value as its flat twin");

            // 算上高度且用三维那一种时，同样的 XZ 换个高度就是另一个采样值。
            var volumetric = Data(new[] { PCGPoint.Create(new Vector3(3.0f, 7.0f, 5.0f), 1.0f, 0) });
            var volume = Noise(volumetric, "n", PCGNoiseKind.Value3D, 1.0f, 1, 0, false);
            check(Near(ReadFloat(volume.PrimaryOutput, "n", 0), PCGSeededNoise.HashToUnit(3, 7, 5, Seed)),
                "with height counted the sample is taken at the point's own height",
                ReadFloat(volume.PrimaryOutput, "n", 0).ToString("F6"));

            // 真实撒下来一遍：每个值都是可用的权重，而这个场确实在变。
            var scatter = Scatter(6, 6, 90.0f, Vector3.Zero);
            var written = Noise(scatter, "n", PCGNoiseKind.Fbm2D, 0.01f, 4, 0, true);

            bool inRange = true;
            var distinct = new HashSet<string>();
            for (int i = 0; i < scatter.Count; i++)
            {
                float v = ReadFloat(written.PrimaryOutput, "n", i);
                if (v < 0.0f || v > 1.0f)
                    inRange = false;
                distinct.Add(v.ToString("F4"));
            }

            check(inRange, "every value is inside [0,1], so the column is usable as a weight");
            check(distinct.Count > 1, "the field varies across the scatter rather than writing one number",
                distinct.Count.ToString());
            check(Column(Noise(scatter, "n", PCGNoiseKind.Fbm2D, 0.01f, 4, 0, true).PrimaryOutput, "n") ==
                  Column(written.PrimaryOutput, "n"),
                "the same inputs produce the same field twice");
            check(Column(Noise(scatter, "n", PCGNoiseKind.Fbm2D, 0.01f, 4, 11, true).PrimaryOutput, "n") !=
                  Column(written.PrimaryOutput, "n"),
                "a node with a different seed offset decorrelates from this one");

            // 空的名字写进内建的密度，而这正是加了过滤器的散点通常想要的。
            var byDensity = Noise(lattice, string.Empty, PCGNoiseKind.Value2D, 1.0f, 1, 0, true);
            check(Near(byDensity.PrimaryOutput.Points[0].Density, PCGSeededNoise.HashToUnit(3, 5, Seed)),
                "an empty attribute name writes the built-in density",
                byDensity.PrimaryOutput.Points[0].Density.ToString("F6"));

            check(Noise(PCGData.Empty, "n", PCGNoiseKind.Perlin2D, 0.01f, 1, 0, true).PrimaryOutput.Count == 0,
                "an empty layer stays empty");
        }

        private static PCGExecutionResult Noise(
            PCGData input, string attribute, PCGNoiseKind kind, float frequency, int octaves, int seedOffset, bool plane)
        {
            var node = new PCGNoiseToAttributeNode(Serialization(PCGNoiseToAttributeNode.Id,
                new object[] { attribute, (int)kind, frequency, octaves, seedOffset, plane }));

            return Run(node, input, PCGData.Empty);
        }

        // ---- 通道拆列 --------------------------------------------

        private static void ChannelToAttributeTests()
        {
            var sized = new[] { PCGPoint.Create(Vector3.Zero, 1.0f, 0) };
            sized[0].Scale = new Float3(2.0f, 3.0f, 4.0f);
            sized[0].UpdateBounds(new Vector3(5.0f, 7.0f, 9.0f));
            var point = Data(sized);

            check(Near(ReadFloat(ChannelOf(point, "s", PCGChannelToAttributeNode.Channel.Scale,
                    PCGChannelToAttributeNode.Component.X).PrimaryOutput, "s", 0), 2.0f),
                "Scale.X reads the point's scale, which no other node can see",
                ReadFloat(ChannelOf(point, "s", PCGChannelToAttributeNode.Channel.Scale,
                    PCGChannelToAttributeNode.Component.X).PrimaryOutput, "s", 0).ToString("F3"));

            check(Near(ReadFloat(ChannelOf(point, "s", PCGChannelToAttributeNode.Channel.Scale,
                    PCGChannelToAttributeNode.Component.Length).PrimaryOutput, "s", 0), MathF.Sqrt(29.0f)),
                "Length collapses the channel to one size number",
                ReadFloat(ChannelOf(point, "s", PCGChannelToAttributeNode.Channel.Scale,
                    PCGChannelToAttributeNode.Component.Length).PrimaryOutput, "s", 0).ToString("F4"));

            check(Near(ReadFloat(ChannelOf(point, "b", PCGChannelToAttributeNode.Channel.Bounds,
                    PCGChannelToAttributeNode.Component.Y).PrimaryOutput, "b", 0), 7.0f),
                "Bounds reads the box's half extent, the same convention the modifiers use",
                ReadFloat(ChannelOf(point, "b", PCGChannelToAttributeNode.Channel.Bounds,
                    PCGChannelToAttributeNode.Component.Y).PrimaryOutput, "b", 0).ToString("F3"));

            // 一个手知道的朝向事实，而不是把节点自己的变换再跑一遍：把点绕世界 Y
            // 转四分之一圈，它的前进轴就落在 X 上且没有抬升。符号刻意不锁——它
            // 取决于手性，而两种答案说的是同一件事。
            var turned = new[] { PCGPoint.Create(Vector3.Zero, 1.0f, 0) };
            turned[0].Rotation = Quaternion.RotationAxis(new Float3(0.0f, 1.0f, 0.0f), MathF.PI * 0.5f);
            var yawed = Data(turned);

            float forwardX = ReadFloat(ChannelOf(yawed, "f", PCGChannelToAttributeNode.Channel.Forward,
                PCGChannelToAttributeNode.Component.X).PrimaryOutput, "f", 0);
            float forwardY = ReadFloat(ChannelOf(yawed, "f", PCGChannelToAttributeNode.Channel.Forward,
                PCGChannelToAttributeNode.Component.Y).PrimaryOutput, "f", 0);

            check(Near(MathF.Abs(forwardX), 1.0f) && Near(forwardY, 0.0f),
                "a point turned 90 degrees about Y faces along X and has no rise",
                $"({forwardX:F4}, {forwardY:F4})");

            var flat = Data(new[] { PCGPoint.Create(Vector3.Zero, 1.0f, 0) });
            check(Near(ReadFloat(ChannelOf(flat, "u", PCGChannelToAttributeNode.Channel.Up,
                    PCGChannelToAttributeNode.Component.Y).PrimaryOutput, "u", 0), 1.0f),
                "an unturned point's up is world up");

            // 空的名字写进内建的密度。
            var byDensity = ChannelOf(point, string.Empty, PCGChannelToAttributeNode.Channel.Scale,
                PCGChannelToAttributeNode.Component.X);
            check(Near(byDensity.PrimaryOutput.Points[0].Density, 2.0f),
                "an empty attribute name writes the built-in density",
                byDensity.PrimaryOutput.Points[0].Density.ToString("F3"));

            var scatter = Scatter(4, 4, 100.0f, Vector3.Zero);
            check(ChannelOf(scatter, "v", PCGChannelToAttributeNode.Channel.Bounds,
                    PCGChannelToAttributeNode.Component.Length).PrimaryOutput.Count == scatter.Count,
                "every point is written, none is dropped");

            check(ChannelOf(PCGData.Empty, "v", PCGChannelToAttributeNode.Channel.Scale,
                PCGChannelToAttributeNode.Component.Length).PrimaryOutput.Count == 0,
                "an empty layer stays empty");
        }

        private static PCGExecutionResult ChannelOf(
            PCGData input, string attribute, PCGChannelToAttributeNode.Channel channel,
            PCGChannelToAttributeNode.Component component)
        {
            var node = new PCGChannelToAttributeNode(Serialization(PCGChannelToAttributeNode.Id,
                new object[] { attribute, (int)channel, (int)component }));

            return Run(node, input, PCGData.Empty);
        }

        // ---- 属性平滑 ------------------------------------------------

        private static void SmoothTests()
        {
            // 两个点、值 0 和 10、近到能看见彼此：含自身一趟就把它们平均，
            // 于是两个都读到 5，而它们的均值不变。
            var alternating = Data(LinePoints(2, 10.0f, 0.0f));
            alternating.SharedAttributes = Column(alternating, "v", new[] { 0.0f, 10.0f });

            var smoothed = Smooth(alternating, "v", string.Empty, 15.0f, 1, true, true);
            var spread = Data(LinePoints(2, 10.0f, 0.0f));
            spread.SharedAttributes = Column(spread, "v", new[] { 0.0f, 10.0f });

            // 一列的离散度，平滑前后各量一次，且不用节点自己的机器来量。
            var step = Data(LinePoints(4, 5.0f, 0.0f));
            step.SharedAttributes = Column(step, "v", new[] { 0.0f, 10.0f, 0.0f, 10.0f });

            var flattened = Smooth(step, "v", string.Empty, 100.0f, 1, true, true);

            check(Spread(step, "v") > Spread(flattened.PrimaryOutput, "v"),
                "one pass over a saw-tooth column reduces its spread",
                $"{Spread(step, "v"):0.###} -> {Spread(flattened.PrimaryOutput, "v"):0.###}");

            var more = Smooth(step, "v", string.Empty, 100.0f, 3, true, true);
            check(Spread(more.PrimaryOutput, "v") <= Spread(flattened.PrimaryOutput, "v"),
                "further passes never undo the flattening",
                $"{Spread(flattened.PrimaryOutput, "v"):0.###} -> {Spread(more.PrimaryOutput, "v"):0.###}");

            // 只够到紧邻的半径会留下可抹平的东西，正是这一点让
            // "多趟更平"变得可观察，而不是两个零互比。
            var near = Data(LinePoints(4, 5.0f, 0.0f));
            near.SharedAttributes = Column(near, "v", new[] { 0.0f, 10.0f, 0.0f, 10.0f });

            var onePass = Smooth(near, "v", string.Empty, 7.0f, 1, true, true);
            var twoPasses = Smooth(near, "v", string.Empty, 7.0f, 2, true, true);

            check(Spread(onePass.PrimaryOutput, "v") < Spread(near, "v") &&
                      Spread(twoPasses.PrimaryOutput, "v") < Spread(onePass.PrimaryOutput, "v"),
                "with a local radius each further pass keeps flattening the column",
                $"{Spread(near, "v"):0.###} -> {Spread(onePass.PrimaryOutput, "v"):0.###} -> " +
                $"{Spread(twoPasses.PrimaryOutput, "v"):0.###}");

            check(Column(smoothed.PrimaryOutput, "v") == "5,5",
                "the neighbours average to the mean of the pair",
                Column(smoothed.PrimaryOutput, "v"));

            check(MeanPreserved(alternating, smoothed.PrimaryOutput, "v"),
                "smoothing moves values between points without inventing or losing any",
                $"mean {Column(alternating, "v")} -> {Column(smoothed.PrimaryOutput, "v")}");

            // 不含自身时，一个点拿到的永远只是邻居的平均，那是另一种运算、值得钉住：
            // 在这一对上它会把两个值互换。
            var withoutSelf = Smooth(alternating, "v", string.Empty, 15.0f, 1, true, false);
            check(Column(withoutSelf.PrimaryOutput, "v") == "10,0",
                "excluding self makes the average purely the neighbours', which on a pair swaps them",
                Column(withoutSelf.PrimaryOutput, "v"));

            // 就地平滑密度：没有具名目标，所以答案落回 density。这个夹具
            // 需要*各不相同*的密度——均匀的一层平滑后还是它自己，什么也证明不了。
            var varying = Data(SamplePoints(4));
            var byDensity = Smooth(varying, string.Empty, string.Empty, 100.0f, 1, true, true);

            check(Densities(byDensity.PrimaryOutput) != Densities(varying) &&
                      Column(byDensity.PrimaryOutput, PCGSmoothAttributeNode.WorkingAttribute) ==
                      Densities(byDensity.PrimaryOutput),
                "smoothing the density writes back to density, keeping the working column for inspection",
                $"{Densities(byDensity.PrimaryOutput)} / {Column(byDensity.PrimaryOutput, PCGSmoothAttributeNode.WorkingAttribute)}");

            // 具名的目标会让源那一列不受影响。
            var toColumn = Smooth(step, "v", "vSmooth", 100.0f, 1, true, true);
            check(Column(toColumn.PrimaryOutput, "v") == "0,10,0,10" &&
                      Spread(toColumn.PrimaryOutput, "vSmooth") < Spread(step, "v"),
                "a named target smooths into its own column and leaves the source untouched",
                Column(toColumn.PrimaryOutput, "v"));

            // 半径为零、趟数为零都是原样递出去，不是造出一个略不同的世界。
            var noRadius = Smooth(step, "v", string.Empty, 0.0f, 3, true, true);
            check(Column(noRadius.PrimaryOutput, "v") == "0,10,0,10" && Seeds(noRadius.PrimaryOutput) == Seeds(step),
                "a zero radius leaves the column exactly as it was");

            // 源不存在会被报告出来，而这一层原样递出去，不是被清零。
            var missing = Smooth(step, "notThere", string.Empty, 100.0f, 1, true, true);
            check(Column(missing.PrimaryOutput, "v") == "0,10,0,10" && missing.PrimaryOutput.Count == step.Count,
                "a source column that does not exist leaves the layer untouched",
                Column(missing.PrimaryOutput, "v"));

            check(Smooth(PCGData.Empty, "v", string.Empty, 100.0f, 1, true, true).PrimaryOutput.Count == 0,
                "an empty layer stays empty");
        }

        /// <summary>一列的离散度，直接量出来，不经过节点自己的求平均。</summary>
        private static float Spread(PCGData data, string name)
        {
            var set = data?.SharedAttributes;

            if (set == null || !set.Contains(name) || data.Count == 0)
                return 0.0f;

            float min = float.MaxValue, max = float.MinValue;

            for (int i = 0; i < data.Count; i++)
            {
                float value = set.GetFloat(name, i);
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }

            return max - min;
        }

        private static bool MeanPreserved(PCGData before, PCGData after, string name)
        {
            double sumBefore = 0.0, sumAfter = 0.0;

            for (int i = 0; i < before.Count; i++)
                sumBefore += before.SharedAttributes.GetFloat(name, i);

            for (int i = 0; i < after.Count; i++)
                sumAfter += after.SharedAttributes.GetFloat(name, i);

            return Math.Abs(sumBefore - sumAfter) < 1e-3;
        }

        private static PCGExecutionResult Smooth(
            PCGData input, string source, string target, float radius, int passes, bool planeOnly, bool includeSelf)
        {
            var node = new PCGSmoothAttributeNode(Serialization(PCGSmoothAttributeNode.Id, new object[]
            {
                source, target, radius, passes, planeOnly, includeSelf,
            }));

            return Run(node, input, PCGData.Empty);
        }

        // ---- 参数覆盖 ----------------------------------------------

        /// <summary>
        /// 把一次审计发现"从没被任何断言用过"的那些参数取值跑一遍。
        /// </summary>
        /// <remarks>
        /// 每个节点至少有一条断言，这是一句很弱的陈述：一个有六个参数的节点可以只测
        /// 其中一个就算"覆盖了"，剩下五个则是没人跑过的代码。下面这份清单来自
        /// 把本文件从没点名的枚举成员 grep 出来——Sort 的两个位置键、Set Attribute
        /// 的两个位置来源、减法，以及把垂直轴打开的平滑。
        /// </remarks>
        private static void ParameterCoverageTests()
        {
            // 按 Z 排序：夹具的深度是打乱的，所以输入顺序不可能蒙对。
            var depths = Data(new[]
            {
                PCGPoint.Create(new Vector3(0.0f, 0.0f, 0.0f), 1.0f, 0),
                PCGPoint.Create(new Vector3(0.0f, 0.0f, 30.0f), 1.0f, 1),
                PCGPoint.Create(new Vector3(0.0f, 0.0f, 10.0f), 1.0f, 2),
                PCGPoint.Create(new Vector3(0.0f, 0.0f, 20.0f), 1.0f, 3),
            });

            check(Seeds(Sort(depths, PCGSortPointsNode.SortKey.Depth, string.Empty, false).PrimaryOutput) == "0,2,3,1" &&
                      Seeds(Sort(depths, PCGSortPointsNode.SortKey.Depth, string.Empty, true).PrimaryOutput) == "1,3,2,0",
                "the depth key orders by Z in both directions",
                Seeds(Sort(depths, PCGSortPointsNode.SortKey.Depth, string.Empty, false).PrimaryOutput));

            // 按 X 排序：夹具的 X 随下标递减，所以升序并不是输入顺序。
            var xs = Data(new[]
            {
                PCGPoint.Create(new Vector3(0.0f, 0.0f, 0.0f), 1.0f, 0),
                PCGPoint.Create(new Vector3(-10.0f, 0.0f, 0.0f), 1.0f, 1),
                PCGPoint.Create(new Vector3(-20.0f, 0.0f, 0.0f), 1.0f, 2),
            });

            check(Seeds(Sort(xs, PCGSortPointsNode.SortKey.Horizontal, string.Empty, false).PrimaryOutput) == "2,1,0",
                "the horizontal key orders by X, which is not the order the points arrived in",
                Seeds(Sort(xs, PCGSortPointsNode.SortKey.Horizontal, string.Empty, false).PrimaryOutput));

            // Set Attribute 用它提供却没被任何测试用过的两个位置轴。
            var positions = Data(new[]
            {
                PCGPoint.Create(new Vector3(1.0f, 0.0f, 100.0f), 0.5f, 0),
                PCGPoint.Create(new Vector3(2.0f, 0.0f, 200.0f), 0.5f, 1),
            });

            var fromX = SetAttribute(positions, "x", PCGSetAttributeNode.ValueSource.PositionX,
                0.0f, 1.0f, 0.0f, string.Empty);
            var fromZ = SetAttribute(positions, "z", PCGSetAttributeNode.ValueSource.PositionZ,
                0.0f, 1.0f, 0.0f, string.Empty);

            check(Column(fromX.PrimaryOutput, "x") == "1,2" && Column(fromZ.PrimaryOutput, "z") == "100,200",
                "the X and Z sources read the axes they name, not the same one twice",
                $"{Column(fromX.PrimaryOutput, "x")} / {Column(fromZ.PrimaryOutput, "z")}");

            // 减法：唯一一条背后没有断言的算术运算。
            var difference = Combine(MathFixture(), PCGAttributeMathNode.MathOperation.Subtract, "a", "b",
                0.0f, string.Empty, false);
            check(Densities(difference.PrimaryOutput) == "1,2,2,8",
                "subtracting two columns works per point",
                Densities(difference.PrimaryOutput));

            // 把垂直轴打开来平滑。这一对在 X 上相距 1000、在 Y 上相距 1000，而
            // 半径落在两个距离之间：在平面上彼此可见（1000），在三维里不可见
            // （1414），所以两种模式必须给出不同的答案——这也是唯一能看出那个开关真被读了的办法。
            var diagonal = Data(new[]
            {
                PCGPoint.Create(new Vector3(0.0f, 0.0f, 0.0f), 1.0f, 0),
                PCGPoint.Create(new Vector3(1000.0f, 1000.0f, 0.0f), 1.0f, 1),
            });
            diagonal.SharedAttributes = Column(diagonal, "v", new[] { 0.0f, 10.0f });

            var onPlane = Smooth(diagonal, "v", string.Empty, 1200.0f, 1, true, true);
            var inSolid = Smooth(diagonal, "v", string.Empty, 1200.0f, 1, false, true);

            check(Column(onPlane.PrimaryOutput, "v") == "5,5" && Column(inSolid.PrimaryOutput, "v") == "0,10",
                "plane mode averages the pair and solid mode leaves them alone, so the flag really is read",
                $"{Column(onPlane.PrimaryOutput, "v")} / {Column(inSolid.PrimaryOutput, "v")}");
        }

        // ---- 法线转密度 -----------------------------------------------

        private static void NormalToDensityTests()
        {
            // 三种朝向：直立、离开上方四分之一圈、以及两者之间的 60 度——
            // 于是夹具覆盖的是整个区间，而不只是"有些点数字变小了"。
            var points = new[]
            {
                PCGPoint.Create(Vector3.Zero, 0.5f, 0),
                PCGPoint.Create(Vector3.Zero, 0.5f, 1),
                PCGPoint.Create(Vector3.Zero, 0.5f, 2),
            };

            // 旋转显式构造，角度用弧度、绕具名轴：EulerDegrees 喂给 Quaternion.Euler，
            // 而后者的分量映射并不是"某个角度"看上去的样子；猜错了的夹具测的是那个猜测，
            // 不是节点。
            points[1].Rotation = Quaternion.RotationAxis(new Float3(1.0f, 0.0f, 0.0f), MathF.PI / 2.0f);
            points[2].Rotation = Quaternion.RotationAxis(new Float3(1.0f, 0.0f, 0.0f), MathF.PI / 3.0f);

            var data = Data(points);
            var facing = Normal(data, Vector3.Up, string.Empty, false);

            check(Densities(facing.PrimaryOutput) == "1,0,0.5",
                "density is the clamped dot of the point's up axis and the direction",
                Densities(facing.PrimaryOutput));

            var inverted = Normal(data, Vector3.Up, string.Empty, true);
            check(Densities(inverted.PrimaryOutput) == "0,1,0.5",
                "inverting scores the faces pointing away from the direction",
                Densities(inverted.PrimaryOutput));

            // 具名目标会让密度不受影响，这正是本节点能与其他密度规则并排放着的原因。
            var toColumn = Normal(data, Vector3.Up, "flatness", false);
            check(Column(toColumn.PrimaryOutput, "flatness") == "1,0,0.5" &&
                      Densities(toColumn.PrimaryOutput) == Densities(data),
                "a named target fills its column and leaves density untouched",
                Column(toColumn.PrimaryOutput, "flatness"));

            // 零向量没有朝向可量，所以它退回 up，而不是把这一层清零。
            var zeroDirection = Normal(data, Vector3.Zero, string.Empty, false);
            check(Densities(zeroDirection.PrimaryOutput) == "1,0,0.5",
                "a zero direction is read as up instead of flattening every density to zero",
                Densities(zeroDirection.PrimaryOutput));

            check(Normal(PCGData.Empty, Vector3.Up, string.Empty, false).PrimaryOutput.Count == 0,
                "an empty layer stays empty");
        }

        private static PCGExecutionResult Normal(PCGData input, Vector3 direction, string target, bool invert)
        {
            var node = new PCGNormalToDensityNode(Serialization(PCGNormalToDensityNode.Id, new object[]
            {
                direction, target, invert,
            }));

            return Run(node, input, PCGData.Empty);
        }

        // ---- 按节点检视 ---------------------------------------------

        /// <summary>
        /// 编辑器的按节点检视所依赖的记录器：每个节点产出了什么，按执行顺序。
        /// </summary>
        /// <remarks>
        /// 要钉的性质不是"它能记录"，而是"挂上它不会改变这一次运行"，
        /// 以及"它有界"：一个把每个节点的每个点都留下的检视器，会是整张图里最贵的
        /// 那个东西。
        /// </remarks>
        private static void DrawListTests()
        {
            var inspection = new PCGNodeInspection();
            var data = Data(new[]
            {
                PCGPoint.Create(Vector3.Zero, 1.0f, 0),
                PCGPoint.Create(new Vector3(10.0f, 0.0f, 0.0f), 1.0f, 1),
            });

            // 节点 3 跑了三次（一次分区的运行）、5 两次、7 一次且什么都没产出。
            for (int i = 0; i < 3; i++)
                inspection.Record(3, "VolumeSampler", data);

            inspection.Record(5, "SelfPruningFilter", data);
            inspection.Record(5, "SelfPruningFilter", data);
            inspection.Record(7, "AttributeFilter", PCGData.Empty);

            var draws = new List<PCGNodeInspection.NodeDraw>();
            int count = inspection.CollectDraws(draws);

            check(count == 2 && draws.Count == 2,
                "a node is drawn once however many times it ran, and one with nothing to show is skipped",
                $"{count} draw(s)");
            check(draws[0].NodeIndex == 3 && draws[1].NodeIndex == 5,
                "draws come out in execution order",
                $"{draws[0].NodeIndex},{draws[1].NodeIndex}");
            check(draws[0].Samples.Length == 6,
                "every run's samples are gathered up to the recorder's own limit",
                draws[0].Samples.Length.ToString());

            check(draws[0].Color.Equals(PCGNodeInspection.ColorFor(3)) &&
                  draws[1].Color.Equals(PCGNodeInspection.ColorFor(5)) &&
                  !draws[0].Color.Equals(draws[1].Color),
                "each node gets a stable colour of its own",
                $"{draws[0].Color} vs {draws[1].Color}");

            var capped = new List<PCGNodeInspection.NodeDraw>();
            check(inspection.CollectDraws(capped, 1) == 1 && capped[0].NodeIndex == 3,
                "the draw list can be capped, and the cap keeps the earliest nodes");

            var report = inspection.FormatSummaries();
            check(report.Contains("[3]") && report.Contains("3 run(s)") &&
                  report.Contains("[7]") && report.Contains("0 point(s)") &&
                  report.IndexOf("[5]", StringComparison.Ordinal) == report.LastIndexOf("[5]", StringComparison.Ordinal),
                "the folded report lists each node once with its run count, empty ones included",
                report.Replace("\n", " | "));

            var empty = new PCGNodeInspection();
            check(empty.CollectDraws(new List<PCGNodeInspection.NodeDraw>()) == 0 &&
                  empty.FormatSummaries() == "no nodes were recorded",
                "an inspection that recorded nothing yields no draws and says so");

            var negative = PCGNodeInspection.ColorFor(-1);
            check(negative.Equals(PCGNodeInspection.ColorFor(-1)) && !negative.Equals(PCGNodeInspection.ColorFor(0)),
                "a negative node index folds into the palette instead of indexing out of it",
                negative.ToString());
        }

        private static void InspectionTests()
        {
            var graph = InspectionGraph();

            var plain = RunInspected(graph, null, out int plainPoints);
            var inspection = new PCGNodeInspection();
            var inspected = RunInspected(graph, inspection, out int inspectedPoints);

            check(plainPoints == inspectedPoints,
                "attaching an inspector does not change what the graph produces",
                $"{plainPoints} vs {inspectedPoints}");

            var records = inspection.Records;

            check(records.Count == 3,
                "every node that ran is reported, in execution order",
                $"{records.Count} record(s)");

            var order = new System.Text.StringBuilder();
            for (int i = 0; i < records.Count; i++)
            {
                if (i > 0)
                    order.Append(',');

                order.Append(records[i].NodeIndex);
            }

            check(records[0].NodeIndex == 0 && records[1].NodeIndex == 1 && records[2].NodeIndex == 2,
                "the records follow the order the nodes were evaluated in",
                order.ToString());

            check(records[0].PointCount == 1200 && records[1].PointCount > 0 &&
                      records[1].PointCount <= records[0].PointCount &&
                      records[2].PointCount <= records[1].PointCount,
                "each record says what that node handed on, so a filter that ate the layer is visible",
                $"{records[0].PointCount} -> {records[1].PointCount} -> {records[2].PointCount}");

            check(records[0].TypeId == PCGVolumeSamplerNode.Id && records[1].TypeId == PCGSelfPruningFilterNode.Id,
                "the records name the node types they came from",
                $"{records[0].TypeId}, {records[1].TypeId}");

            bool bounded = true;

            for (int i = 0; i < records.Count; i++)
            {
                int expected = Math.Min(PCGNodeInspection.SampleLimit, records[i].PointCount);

                if (records[i].Samples?.Length != expected)
                    bounded = false;
            }

            check(bounded,
                "sample positions stay inside the recorder's limit instead of copying whole layers",
                $"limit {PCGNodeInspection.SampleLimit}");

            // 插件总会暴露它的内建列（$Density、$Position……），所以"这个节点什么也没发布"
            // 并不表现为一个空列表——它要在一份根本没有存储的数据集上才看得见。
            check(records[0].AttributeNames.Contains("$Density", StringComparison.Ordinal),
                "the report lists the columns a node handed on, built-in ones included",
                records[0].AttributeNames);

            var bare = new PCGNodeInspection();
            bare.Record(0, PCGVolumeSamplerNode.Id, PCGData.Empty);
            check(bare.Records[0].AttributeNames == string.Empty,
                "a data set with no attribute storage at all reports no columns, rather than inventing a list");

            var formatter = new PCGNodeInspection();
            formatter.Record(0, PCGVolumeSamplerNode.Id, plain.PrimaryOutput);
            check(formatter.Format().Contains(PCGVolumeSamplerNode.Id, StringComparison.Ordinal),
                "the formatted report names the node it describes",
                formatter.Format().Trim());

            formatter.Clear();
            check(formatter.Records.Count == 0 && formatter.Format() == "no nodes were recorded",
                "clearing a recorder leaves it ready for the next run");

            // 把一个节点的记录折起来：编辑器的读数问的是"我选中的那个节点产出了什么"，
            // 而在一次分区的运行里那个节点是每格跑一次的。
            var folding = new PCGNodeInspection();
            folding.Record(0, PCGVolumeSamplerNode.Id, Data(SamplePoints(4)));
            folding.Record(1, PCGSelfPruningFilterNode.Id, Data(SamplePoints(6)));

            var sampler = folding.Summarize(0);
            check(sampler.Ran && sampler.Runs == 1 && sampler.PointCount == 4 && sampler.Samples.Length == 4,
                "one run summarises as one run with its points",
                sampler.ToString());

            var pruner = folding.Summarize(1);
            check(pruner.PointCount == 6 && pruner.Runs == 1,
                "and each node is summarised on its own",
                pruner.ToString());

            // 同一个节点跑三遍，正如一次分区生成会产生的那样：计数加得起来，
            // 而采样数停在限额内，不随区域一起长。
            var partitioned = new PCGNodeInspection();
            partitioned.Record(1, PCGSelfPruningFilterNode.Id, Data(SamplePoints(4)));
            partitioned.Record(1, PCGSelfPruningFilterNode.Id, Data(SamplePoints(4)));
            partitioned.Record(1, PCGSelfPruningFilterNode.Id, Data(SamplePoints(4)));

            var folded = partitioned.Summarize(1);
            check(folded.Runs == 3 && folded.PointCount == 12 &&
                      folded.Samples.Length == PCGNodeInspection.SampleLimit,
                "a node that ran once per cell folds into one answer with bounded samples",
                folded.ToString());

            // 从没跑过的节点会照实说："没跑过"与"跑了但没产出"是两个不同的答案。
            var absent = folding.Summarize(9);
            check(!absent.Ran && absent.Runs == 0 && absent.ToString() == "did not run",
                "a node the run never reached reports that it did not run",
                absent.ToString());
        }

        /// <summary>一条三个节点的链，于是记录顺序与递减的计数都观察得到。</summary>
        private static PCGGraph InspectionGraph() => new PCGGraph
        {
            Name = "Inspection",
            Nodes = new[]
            {
                new PCGNode { TypeId = PCGVolumeSamplerNode.Id, Values = new object[] { 1200, 0, 1.0f, true, 1.0f } },
                new PCGNode { TypeId = PCGSelfPruningFilterNode.Id, Values = new object[] { 30.0f, false, true } },
                new PCGNode { TypeId = PCGDensityFilterNode.Id, Values = new object[] { 0.0f, 1.0f, false } },
            },
            Edges = new[]
            {
                new PCGEdge { OutputNodeIndex = 0, InputNodeIndex = 1 },
                new PCGEdge { OutputNodeIndex = 1, InputNodeIndex = 2 },
            },
        };

        private static PCGExecutionResult RunInspected(PCGGraph graph, PCGNodeInspection inspection, out int points)
        {
            if (!PCGGraphCompiler.TryCompile(graph, out var compiled))
                throw new InvalidOperationException("the inspection fixture graph failed to compile");

            var context = new PCGExecutionContext
            {
                Seed = 20261007,
                GenerationBounds = new BoundingBox(
                    new Vector3(-2000.0f, -50.0f, -2000.0f), new Vector3(2000.0f, 50.0f, 2000.0f)),
                Inspection = inspection,
            };

            var result = new PCGGraphExecutor(compiled).Execute(context);
            points = result.PrimaryOutput?.Count ?? 0;

            return result;
        }

        // ---- 夹具与助手 -------------------------------------------

        private static PCGExecutionResult Bounds(
            PCGData input, PCGBoundsModifierNode.BoundsMode mode, Vector3 extents, float scale)
        {
            var node = new PCGBoundsModifierNode(Serialization(PCGBoundsModifierNode.Id, new object[]
            {
                (int)mode, extents, scale,
            }));

            return Run(node, input, PCGData.Empty);
        }

        private static bool Close(Vector3 a, Vector3 b, float tolerance = 0.01f) =>
            Math.Abs(a.X - b.X) < tolerance && Math.Abs(a.Y - b.Y) < tolerance && Math.Abs(a.Z - b.Z) < tolerance;

        private static PCGExecutionResult Copy(
            PCGData input, int count, Vector3 offset, bool localSpace, Vector3 spread,
            float scaleMin, float scaleMax, int seedOffset)
        {
            var node = new PCGCopyPointsNode(Serialization(PCGCopyPointsNode.Id, new object[]
            {
                count, offset, localSpace, spread, scaleMin, scaleMax, seedOffset,
            }));

            return Run(node, input, PCGData.Empty);
        }

        private static PCGExecutionResult Sort(
            PCGData input, PCGSortPointsNode.SortKey key, string attribute, bool descending)
        {
            var node = new PCGSortPointsNode(Serialization(PCGSortPointsNode.Id, new object[]
            {
                (int)key, attribute, descending,
            }));

            return Run(node, input, PCGData.Empty);
        }

        /// <summary>
        /// 半范围：把这样一组半范围的轴对齐盒子按某个旋转转开后，包住它的那个盒子的半范围。
        /// </summary>
        /// <remarks>
        /// 写法是转八个角再取跨度，这就是定义本身——节点用的是一个闭式解，
        /// 所以两者一致是第二意见，而不是同一份算术
        /// 写了两遍。
        /// </remarks>
        private static Vector3 TurnedHalfExtents(Vector3 half, Quaternion rotation)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        var corner = Vector3.Transform(
                            new Vector3(half.X * sx, half.Y * sy, half.Z * sz), rotation);

                        min = Vector3.Min(min, corner);
                        max = Vector3.Max(max, corner);
                    }
                }
            }

            return (max - min) * 0.5f;
        }

        private static string Seeds(PCGData data)
        {
            var builder = new System.Text.StringBuilder();

            for (int i = 0; i < (data?.Points?.Length ?? 0); i++)
            {
                if (i > 0)
                    builder.Append(',');

                builder.Append(data.Points[i].Seed);
            }

            return builder.ToString();
        }

        private static string Keys(PCGData data)
        {
            var builder = new System.Text.StringBuilder();

            for (int i = 0; i < (data?.Points?.Length ?? 0); i++)
            {
                var point = data.Points[i];
                builder.Append(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0:0.###},{1:0.###},{2:0.###},{3:0.###};",
                    point.Position.X, point.Position.Y, point.Position.Z, point.Scale.X));
            }

            return builder.ToString();
        }

        private static PCGData Scatter(int columns, int rows, float spacing, Vector3 origin)
        {
            var points = new PCGPoint[columns * rows];
            int index = 0;

            for (int z = 0; z < rows; z++)
            {
                for (int x = 0; x < columns; x++)
                {
                    points[index] = PCGPoint.Create(
                        new Vector3(origin.X + x * spacing, origin.Y, origin.Z + z * spacing), 1.0f, index);
                    index++;
                }
            }

            return Data(points);
        }

        private static void Mark(PCGPoint[] points, float halfExtent)
        {
            for (int i = 0; i < points.Length; i++)
                points[i].UpdateBounds(new Vector3(halfExtent, halfExtent, halfExtent));
        }

        private static PCGData Data(PCGPoint[] points) => new PCGData(points);

        private static PCGExecutionResult Difference(
            PCGData input, PCGData cut, bool distance, float maxDistance, float padding, bool plane)
        {
            var node = new PCGDifferencePointsNode(Serialization(
                PCGDifferencePointsNode.Id,
                new object[] { distance ? 1 : 0, maxDistance, padding, plane }));

            return Run(node, input, cut);
        }

        private static PCGExecutionResult Intersection(
            PCGData input, PCGData cut, bool distance, float maxDistance, float padding, bool plane)
        {
            var node = new PCGIntersectionPointsNode(Serialization(
                PCGIntersectionPointsNode.Id,
                new object[] { distance ? 1 : 0, maxDistance, padding, plane }));

            return Run(node, input, cut);
        }

        private static PCGNodeRegistry.PCGNodeSerializationData Serialization(string typeId, object[] values) =>
            new PCGNodeRegistry.PCGNodeSerializationData(typeId, values, 0);

        private static PCGExecutionResult Run(PCGNodeBase node, PCGData input, PCGData cut)
        {
            var context = new PCGExecutionContext
            {
                Seed = 20261007,
                GenerationBounds = new BoundingBox(
                    new Vector3(-2000.0f, -200.0f, -2000.0f), new Vector3(2000.0f, 200.0f, 2000.0f)),
            };

            // 输入个数由节点自己钉下的输入数决定：基类会拒绝更长的数组，
            // 而那道守卫是对的——一张接错线的图撞上的正是它。
            var inputs = node.InputPins.Length > 1 ? new[] { input, cut } : new[] { input };
            var output = node.Execute(context, inputs);

            return new PCGExecutionResult { Outputs = new[] { output } };
        }

        /// <summary>
        /// 包含关系写成"中心加半范围"，而节点自己的探测并不是这样问这个问题的。
        /// </summary>
        /// <remarks>
        /// 两种写法必须有区别，否则一致这件事对两者都证明不了什么：一种做钳位再比较，
        /// 这一种从盒子中心去量。这也是 UE5 的边界检验所用的写法，所以这份
        /// 参照至少是第二意见，而不是同一个作者的习惯被写了两遍。
        /// </remarks>
        private static bool ReferenceInsideBounds(Vector3 position, PCGPoint[] boxes, float padding)
        {
            for (int i = 0; i < boxes.Length; i++)
            {
                var bounds = boxes[i].Bounds;
                var centre = (bounds.Minimum + bounds.Maximum) * 0.5f;
                var half = (bounds.Maximum - bounds.Minimum) * 0.5f;

                // 只看 XZ：这些夹具都是平面模式。
                if (MathF.Abs(position.X - centre.X) <= half.X + padding &&
                    MathF.Abs(position.Z - centre.Z) <= half.Z + padding)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ReferenceInsideDistance(Vector3 position, PCGPoint[] centres, float reach)
        {
            for (int i = 0; i < centres.Length; i++)
            {
                float dx = centres[i].Position.X - position.X;
                float dz = centres[i].Position.Z - position.Z;

                if (MathF.Sqrt(dx * dx + dz * dz) <= reach)
                    return true;
            }

            return false;
        }

        private static bool SameSet(PCGData a, PCGData b)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < (a?.Points?.Length ?? 0); i++)
                keys.Add(Key(a.Points[i]));

            for (int i = 0; i < (b?.Points?.Length ?? 0); i++)
            {
                if (keys.Contains(Key(b.Points[i])))
                    return true;
            }

            return false;
        }

        private static string Key(in PCGPoint point)
        {
            var p = point.Position;
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0:0.###}|{1:0.###}|{2:0.###}", p.X, p.Y, p.Z);
        }
    }
}
