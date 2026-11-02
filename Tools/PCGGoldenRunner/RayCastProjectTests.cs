// PCG 插件 - Ray Cast Project 的可执行测试（docs/PCG_PLUGIN_DESIGN.md §5.1.30）。
//
// 引擎调用是通过世界上下文注入的，所以真正值得测的那部分能离屏跑：
// 一次命中把点放哪儿、它怎么倾斜、一次未命中会怎样，以及——没人想到要断言的那个方向——
// 查询自己的参数（层级掩码、触发器、距离、方向）是否真的到达了投射器，而不是
// 在路上被丢掉。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG;
using PCG.Execution;
using PCG.Nodes;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class RayCastProjectTests
    {
        internal static void Run(CheckDelegate check)
        {
            ProjectionTests(check);
            AlignmentTests(check);
            MissTests(check);
            QueryTests(check);
            FailureTests(check);
        }

        private static void ProjectionTests(CheckDelegate check)
        {
            // 一片 y = 0 的平地，于是期望的落地高度是算出来的，而不是一张截图。
            var world = FlatWorld(0.0f, Vector3.Up);
            var node = Make(world, heightOffset: 0.0f, align: false, dropMisses: false, maxDistance: 10000.0f);

            var result = node.Execute(Context(world), new[] { Points(3, 1000.0f) });

            check("every point lands on the plane under it",
                result.Count == 3 && Near(result.Points[0].Position.Y, 0.0f) &&
                Near(result.Points[1].Position.Y, 0.0f) && Near(result.Points[2].Position.Y, 0.0f),
                Describe(result));

            check("landing keeps the XZ the point had",
                Near(result.Points[0].Position.X, 0.0f) && Near(result.Points[0].Position.Z, 0.0f) &&
                Near(result.Points[1].Position.X, 100.0f) && Near(result.Points[1].Position.Z, -100.0f) &&
                Near(result.Points[2].Position.X, 200.0f) && Near(result.Points[2].Position.Z, -200.0f),
                Describe(result));

            check("the run counts what it landed on",
                node.HitCount == 3 && node.MissedCount == 0, $"{node.HitCount} hit(s), {node.MissedCount} miss(es)");

            // 偏移是沿命中法线施加的，而在平地上那就是笔直向上：孪生的那一例是一段斜坡，
            // 在下面查，那里 up 与法线不一致。
            var lifted = Make(FlatWorld(0.0f, Vector3.Up), heightOffset: 25.0f, align: false, dropMisses: false);
            var liftedResult = lifted.Execute(Context(FlatWorld(0.0f, Vector3.Up)), new[] { Points(1, 500.0f) });

            check("the height offset lifts the point off the surface",
                Near(liftedResult.Points[0].Position.Y, 25.0f), liftedResult.Points[0].Position.Y.ToString("F1"));
        }

        private static void AlignmentTests(CheckDelegate check)
        {
            // 平地：对齐必须是恒等，这正是共享的表面助手所保证的那条性质，
            // 也是一个把叉积写反的实现会弄错的地方（它会返回 180）。
            var flat = Make(FlatWorld(0.0f, Vector3.Up), 0.0f, align: true, dropMisses: false);
            var flatResult = flat.Execute(Context(FlatWorld(0.0f, Vector3.Up)), new[] { Points(1, 500.0f) });

            check("aligning to flat ground changes nothing",
                Near(flatResult.Points[0].Rotation.X, 0.0f) && Near(flatResult.Points[0].Rotation.Y, 0.0f) &&
                Near(flatResult.Points[0].Rotation.Z, 0.0f) && Near(Math.Abs(flatResult.Points[0].Rotation.W), 1.0f),
                flatResult.Points[0].Rotation.ToString());

            // 一段 45 度的坡：点的 up 必须跟着法线走，而坡度那一列必须与它一致。
            var slopeNormal = new Vector3(0.0f, 1.0f, 1.0f);
            slopeNormal /= slopeNormal.Length;

            var sloped = Make(FlatWorld(0.0f, slopeNormal), 0.0f, align: true, dropMisses: false);
            var slopedResult = sloped.Execute(Context(FlatWorld(0.0f, slopeNormal)), new[] { Points(1, 500.0f) });

            var up = Vector3.Transform(new Vector3(0.0f, 1.0f, 0.0f), slopedResult.Points[0].Rotation);

            check("aligning turns the point until its up matches the surface normal",
                Near(up.X, slopeNormal.X, 1e-3f) && Near(up.Y, slopeNormal.Y, 1e-3f) && Near(up.Z, slopeNormal.Z, 1e-3f),
                $"up=({up.X:F3},{up.Y:F3},{up.Z:F3})");

            var slopes = slopedResult.SharedAttributes?.TryGetFloatArray(PCGAttributeSet.SlopeName);
            check("a sloped hit writes the slope it landed on",
                slopes != null && slopes.Length == 1 && slopes[0] > 40.0f && slopes[0] < 50.0f,
                slopes == null ? "no column" : slopes[0].ToString("F1"));

            // 关掉对齐时，即使在斜坡上也必须不碰旋转——这条孪生让上面那个检查
            // 不至于因为"助手总是倾斜"而蒙过去。
            var unaligned = Make(FlatWorld(0.0f, slopeNormal), 0.0f, align: false, dropMisses: false);
            var unalignedResult = unaligned.Execute(Context(FlatWorld(0.0f, slopeNormal)), new[] { Points(1, 500.0f) });

            check("with alignment off the rotation is left as it was",
                Near(unalignedResult.Points[0].Rotation.Y, 0.0f) &&
                Near(Math.Abs(unalignedResult.Points[0].Rotation.W), 1.0f),
                unalignedResult.Points[0].Rotation.ToString());

            // 一个报告不了法线的碰撞体（球的默认值）不许产出一个随意的旋转，也不许把
            // 点放回表面内部。
            var noNormal = new PCGWorldContext { RayCaster = (o, d, m, mask, t) => new RayCastHit { Point = new Vector3(o.X, 5.0f, o.Z) } };
            var withoutNormal = Make(noNormal, 10.0f, align: true, dropMisses: false);
            var noNormalResult = withoutNormal.Execute(Context(noNormal), new[] { Points(1, 500.0f) });

            check("a hit with no normal is treated as flat rather than tilting the point randomly",
                Near(Math.Abs(noNormalResult.Points[0].Rotation.W), 1.0f) &&
                Near(noNormalResult.Points[0].Position.Y, 15.0f),
                $"{noNormalResult.Points[0].Rotation}, y={noNormalResult.Points[0].Position.Y:F1}");
        }

        private static void MissTests(CheckDelegate check)
        {
            var narrow = Make(FlatWorld(0.0f, Vector3.Up), 0.0f, align: false, dropMisses: false, maxDistance: 100.0f);
            var points = Points(2, 1000.0f);

            var result = narrow.Execute(Context(FlatWorld(0.0f, Vector3.Up)), new[] { points });

            check("a ray too short to reach anything is a miss, and the point keeps its position",
                result.Count == 2 && Near(result.Points[0].Position.Y, 1000.0f) && Near(result.Points[1].Position.Y, 1000.0f),
                Describe(result));

            check("misses are counted separately from hits",
                narrow.HitCount == 0 && narrow.MissedCount == 2, $"{narrow.HitCount}/{narrow.MissedCount}");

            // 孪生项：同样那两个点，射线长到足以碰到地面时就是命中，所以上面那个"未命中"
            // 说的是距离，而不是投射器压根没回答。
            var wide = Make(FlatWorld(0.0f, Vector3.Up), 0.0f, align: false, dropMisses: false, maxDistance: 2000.0f);
            var wideResult = wide.Execute(Context(FlatWorld(0.0f, Vector3.Up)), new[] { points });

            check("the same points hit once the ray can reach",
                wideResult.Count == 2 && Near(wideResult.Points[0].Position.Y, 0.0f) &&
                wide.HitCount == 2 && wide.MissedCount == 0,
                Describe(wideResult));

            // 丢掉未命中，是设计者表达"这些未命中不要"的方式。
            var dropping = Make(FlatWorld(0.0f, Vector3.Up), 0.0f, align: false, dropMisses: true, maxDistance: 100.0f);
            var dropped = dropping.Execute(Context(FlatWorld(0.0f, Vector3.Up)), new[] { points });

            check("with dropping on, only the hits are handed on",
                dropped.Count == 0, dropped.Count.ToString());
        }

        private static void QueryTests(CheckDelegate check)
        {
            // 参数必须到达投射器：一个无视自己层级掩码的节点，在上面每一条几何检查里
            // 都长得一模一样，只有带分层碰撞体的场景才会发现。
            var seen = new List<(Vector3 origin, Vector3 direction, float maxDistance, uint mask, bool triggers)>();

            var world = new PCGWorldContext
            {
                RayCaster = (origin, direction, maxDistance, mask, triggers) =>
                {
                    seen.Add((origin, direction, maxDistance, mask, triggers));
                    return null;
                },
            };

            var node = Make(world, 0.0f, align: false, dropMisses: false, maxDistance: 123.0f);
            node.LayerMask = 4;
            node.HitTriggers = true;

            node.Execute(Context(world), new[] { Points(1, 500.0f) });

            check("the query's own settings reach the caster",
                seen.Count == 1 && seen[0].mask == 4 && seen[0].triggers && Near(seen[0].maxDistance, 123.0f),
                seen.Count == 0 ? "never called" : $"mask={seen[0].mask} triggers={seen[0].triggers} max={seen[0].maxDistance}");

            check("a negative mask means every layer rather than none",
                MaskOf(-1) == uint.MaxValue && MaskOf(0) == 0 && MaskOf(8) == 8,
                $"{MaskOf(-1)}");

            // 一个不是单位向量的方向，行为必须与它归一化后的形式完全一致，否则命中点
            // 就会取决于作者恰好怎么写下那个向量。
            var shortHand = MakeCapturing(world, new Vector3(0.0f, -3.0f, 0.0f), 500.0f);
            var longHand = MakeCapturing(world, new Vector3(0.0f, -1.0f, 0.0f), 500.0f);

            check("a longer direction vector casts along the same ray",
                Near(shortHand.X, longHand.X) && Near(shortHand.Y, longHand.Y) &&
                Near(shortHand.Z, longHand.Z) && Near(shortHand.Length, 1.0f),
                $"{shortHand} vs {longHand}");

            // ……而零方向意味着笔直向下，不是"哪儿都不投射"。
            var zero = MakeCapturing(world, Vector3.Zero, 500.0f);
            check("a zero direction falls back to straight down",
                Near(zero.Y, -1.0f) && Near(zero.X, 0.0f) && Near(zero.Z, 0.0f), zero.ToString());
        }

        private static void FailureTests(CheckDelegate check)
        {
            // 没装投射器——一次离屏运行，或一个没有物理的场景：每一个依赖世界的节点
            // 都遵守的约定是把这件事说出来、并把输入原样递出去，而不是把这一层清空。
            var empty = new PCGWorldContext();
            var node = Make(empty, 0.0f, align: false, dropMisses: true);

            var result = node.Execute(Context(empty), new[] { Points(2, 500.0f) });

            check("a run with no physics reports why and passes the layer through",
                result.Count == 2 && !string.IsNullOrEmpty(node.LastError) && node.LastError.Contains("no physics"),
                node.LastError);

            check("the points are untouched when there is nothing to cast against",
                Near(result.Points[0].Position.Y, 500.0f) && Near(result.Points[1].Position.Y, 500.0f),
                Describe(result));

            // 空的一层仍然是空的，而且不许被报成失败。
            var someWorld = FlatWorld(0.0f, Vector3.Up);
            var emptyNode = Make(someWorld, 0.0f, align: false, dropMisses: false);

            var emptyResult = emptyNode.Execute(Context(someWorld), new[] { PCGData.Empty });
            check("an empty input stays an empty output with no complaint",
                emptyResult.Count == 0 && string.IsNullOrEmpty(emptyNode.LastError), emptyNode.LastError ?? "ok");
        }

        // ---- 夹具 -------------------------------------------------------

        /// <summary>一个世界：它的所有射线都命中一张穿过 <paramref name="height"/> 的平面，且法线固定。</summary>
        private static PCGWorldContext FlatWorld(float height, Vector3 normal)
        {
            return new PCGWorldContext
            {
                RayCaster = (origin, direction, maxDistance, mask, triggers) =>
                {
                    if (Math.Abs(direction.Y) < 1e-6f)
                        return null;

                    float travel = (height - origin.Y) / direction.Y;

                    if (travel < 0.0f || travel > maxDistance)
                        return null;

                    return new RayCastHit
                    {
                        Point = origin + direction * travel,
                        Normal = normal,
                        Distance = travel,
                    };
                },
            };
        }

        private static PCGWorldRayProjectNode Make(
            PCGWorldContext world, float heightOffset, bool align, bool dropMisses, float maxDistance = 10000.0f)
        {
            return new PCGWorldRayProjectNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGWorldRayProjectNode.Id,
                new object[]
                {
                    -1, false, new Vector3(0.0f, -1.0f, 0.0f), maxDistance, heightOffset, align, dropMisses,
                },
                0));
        }

        private static Vector3 MakeCapturing(PCGWorldContext world, Vector3 direction, float maxDistance)
        {
            var seen = new List<Vector3>();

            world.RayCaster = (origin, castDirection, _, _, _) =>
            {
                seen.Add(castDirection);
                return null;
            };

            var node = new PCGWorldRayProjectNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGWorldRayProjectNode.Id,
                new object[] { -1, false, direction, maxDistance, 0.0f, false, false },
                0));

            node.Execute(Context(world), new[] { Points(1, 500.0f) });

            return seen.Count > 0 ? seen[0] : Vector3.Zero;
        }

        private static uint MaskOf(int mask) => mask < 0 ? uint.MaxValue : (uint)mask;

        private static PCGExecutionContext Context(PCGWorldContext world) =>
            new PCGExecutionContext
            {
                Seed = 20261008,
                World = world,
                GenerationBounds = new BoundingBox(new Vector3(-2000.0f, -100.0f, -2000.0f),
                                                   new Vector3(2000.0f, 100.0f, 2000.0f)),
            };

        private static PCGData Points(int count, float height)
        {
            var data = PCGData.Create(count);

            for (int i = 0; i < count; i++)
            {
                data.Points[i] = PCGPoint.Create(new Vector3(i * 100.0f, height, i * -100.0f), 1.0f, i);
                data.Points[i].UpdateBounds(new Vector3(1.0f, 1.0f, 1.0f));
            }

            return data;
        }

        private static bool Near(float a, float b, float tolerance = 1e-3f) => Math.Abs(a - b) <= tolerance;

        private static string Describe(PCGData data)
        {
            var parts = new List<string>();

            for (int i = 0; i < Math.Min(data.Count, 4); i++)
            {
                var point = data.Points[i];
                parts.Add($"({point.Position.X:F0},{point.Position.Y:F0},{point.Position.Z:F0})");
            }

            return $"{data.Count}: {string.Join(" ", parts)}";
        }
    }
}
