// PCG Plugin - Executable tests for the actor sampler (docs/PCG_PLUGIN_DESIGN.md §5.1.32).
//
// Everything here is about the part a level cannot be asked to demonstrate on demand: which actors a filter keeps,
// what happens when the host lies about how many it wrote, and whether a run can feed on its own output. The scene
// enumeration itself is one line in the volume and is covered by the PIE checklist instead.

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG;
using PCG.Execution;
using PCG.Nodes;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class ActorSamplerTests
    {
        internal static void Run(CheckDelegate check)
        {
            ReadingTests(check);
            FilterTests(check);
            OutputTests(check);
            FailureTests(check);
        }

        private static void ReadingTests(CheckDelegate check)
        {
            // The contract the other world sources use: write into the caller's list and return the count.
            var source = new List<PCGActorInfo> { Actor("a"), Actor("b") };
            var scratch = new List<PCGActorInfo>();
            var into = new List<PCGActorInfo>();

            int read = PCGActors.FromSource(list => { list.AddRange(source); return source.Count; }, scratch, into);
            check("a source's entries arrive in order", read == 2 && into[0].Name == "a" && into[1].Name == "b",
                $"{read} actor(s)");

            // A source that claims more than it wrote must be clamped: the list is reused, so trusting the count
            // would read the previous call's leftovers - the bug that shows up as props around a marker that moved.
            var reused = new List<PCGActorInfo> { Actor("stale-1"), Actor("stale-2"), Actor("stale-3") };

            read = PCGActors.FromSource(list => { list.Add(Actor("fresh")); return 3; }, scratch, reused);
            check("a source that over-claims is clamped to what it actually wrote",
                read == 1 && reused.Count == 1 && reused[0].Name == "fresh",
                $"{read} actor(s), first={reused[0].Name}");

            // ...and one that under-claims (or returns a negative) is clamped too, rather than producing nothing or
            // reading past the end.
            read = PCGActors.FromSource(list => { list.Add(Actor("a")); list.Add(Actor("b")); return -1; }, scratch, into);
            check("a negative count is read as nothing rather than as an error",
                read == 0 && into.Count == 0, read.ToString());

            read = PCGActors.FromSource(list => { list.Add(Actor("a")); list.Add(Actor("b")); return 1; }, scratch, into);
            check("a source that under-claims is trimmed to what it claims", read == 1 && into.Count == 1, read.ToString());

            check("no source at all reads as nothing",
                PCGActors.FromSource(null, scratch, into) == 0 && into.Count == 0);

            // The scratch list must end up describing what the source really produced: the run's own logs read it.
            check("the source's leftovers are trimmed off the scratch list", scratch.Count == 1, scratch.Count.ToString());
        }

        private static void FilterTests(CheckDelegate check)
        {
            var marker = Actor("Marker_North");
            marker.Tag = "village";
            marker.LayerName = "Triggers";

            var other = Actor("Marker_South");
            other.Tag = "village";
            other.LayerName = "Default";

            var generated = Actor("PCG_Gen_rock_7");

            var rock = Actor("rock_01");
            rock.Tag = "village_small";
            rock.LayerName = "Triggers";

            // No filter at all keeps every actor, including the ones the plugin made.
            check("an empty filter keeps everything",
                PCGActors.Matches(marker, string.Empty, string.Empty, string.Empty, false) &&
                PCGActors.Matches(generated, string.Empty, string.Empty, string.Empty, false));

            // An empty filter is "no constraint", not "match the empty string": that reading would drop every actor
            // without a tag, which is most of them.
            check("an empty tag does not mean 'actors with no tag'",
                PCGActors.Matches(rock, string.Empty, string.Empty, string.Empty, false));

            check("name matching is a substring and ignores case",
                PCGActors.Matches(marker, "marker_", string.Empty, string.Empty, false) &&
                PCGActors.Matches(marker, "NORTH", string.Empty, string.Empty, false) &&
                !PCGActors.Matches(rock, "marker", string.Empty, string.Empty, false));

            // Tag and layer are exact matches: grouping by tag is the common use, and a substring match would pull
            // in "village_small" whenever someone asked for "village".
            check("tag matching is exact, so a longer tag is not a match",
                PCGActors.Matches(marker, string.Empty, "village", string.Empty, false) &&
                !PCGActors.Matches(rock, string.Empty, "village", string.Empty, false));

            check("layer matching is exact as well",
                PCGActors.Matches(marker, string.Empty, string.Empty, "Triggers", false) &&
                !PCGActors.Matches(other, string.Empty, string.Empty, "Triggers", false));

            // Filters combine as "all of them", which is what a designer means when they fill in two boxes.
            check("two filters have to both hold",
                PCGActors.Matches(marker, string.Empty, "village", "Triggers", false) &&
                !PCGActors.Matches(marker, "south", "village", "Triggers", false));

            // The one that keeps a run from growing on its own output.
            check("generated actors are skipped by default and kept when asked for",
                !PCGActors.Matches(generated, string.Empty, string.Empty, string.Empty, true) &&
                PCGActors.Matches(generated, string.Empty, string.Empty, string.Empty, false));
        }

        private static void OutputTests(CheckDelegate check)
        {
            // The second actor deliberately carries a different tag: the filter check below needs one actor the
            // tag filter should keep and one it should not, and a fixture where every actor matches would pass it
            // by accident.
            var scenery = Actor("rock_01", new Vector3(500.0f, 0.0f, 0.0f), 0.0f);
            scenery.Tag = "scenery";

            var world = World(Actor("Marker_A", new Vector3(10.0f, 0.0f, 20.0f), 90.0f), scenery);

            var node = Make(world);
            var result = node.Execute(Context(world), Array.Empty<PCGData>());

            check("every matching actor becomes a point",
                result.Count == 2 && node.MatchedCount == 2, $"{result.Count} point(s)");

            check("the point sits where the actor is",
                Near(result.Points[0].Position.X, 10.0f) && Near(result.Points[0].Position.Z, 20.0f) &&
                Near(result.Points[1].Position.X, 500.0f),
                Describe(result));

            check("the point takes the actor's rotation",
                Math.Abs(result.Points[0].Rotation.Y) > 0.1f && Math.Abs(result.Points[1].Rotation.Y) < 1e-4f,
                result.Points[0].Rotation.ToString());

            // The box has to be the actor's own footprint: pruning downstream tests it, so a point left with the
            // degenerate box Create() starts from would be culled while its actor is plainly visible.
            check("the box follows the actor's scale rather than staying degenerate",
                Near(PCGPointTransform.HalfExtents(result.Points[0].Bounds).X, 50.0f),
                PCGPointTransform.HalfExtents(result.Points[0].Bounds).ToString("F1"));

            // The metadata columns are the reason the node is more than a scatter of transforms.
            var names = ReadStrings(result, PCGActorSamplerNode.NameColumn, 2);
            var tags = ReadStrings(result, PCGActorSamplerNode.TagColumn, 2);
            var layers = ReadStrings(result, PCGActorSamplerNode.LayerColumn, 2);

            check("the name column says which actor each point came from",
                names != null && names[0] == "Marker_A" && names[1] == "rock_01",
                names == null ? "no column" : string.Join(",", names));

            check("the tag and layer columns carry the actor's labels",
                tags != null && tags[0] == "village" && layers != null && layers[0] == "Triggers",
                $"tags={string.Join(",", tags ?? Array.Empty<string>())} " +
                $"layers={string.Join(",", layers ?? Array.Empty<string>())}");

            // Turning the metadata off must remove those columns, not leave them empty.
            var bare = Make(world);
            bare.WriteMetadata = false;

            var bareResult = bare.Execute(Context(world), Array.Empty<PCGData>());

            check("with metadata off the points are still produced and the columns are gone",
                bareResult.Count == 2 &&
                !bareResult.SharedAttributes.Contains(PCGActorSamplerNode.NameColumn),
                bareResult.SharedAttributes.Contains(PCGActorSamplerNode.NameColumn).ToString());

            // Seeds have to differ per point, or two markers would spawn identically after a random op.
            check("each point gets its own seed",
                result.Points[0].Seed != result.Points[1].Seed,
                $"{result.Points[0].Seed} vs {result.Points[1].Seed}");

            // Filters are applied in the node, and the cap counts what matched rather than what was read.
            var byTag = Make(world);
            byTag.Tag = "village";
            var byTagResult = byTag.Execute(Context(world), Array.Empty<PCGData>());

            check("a tag filter narrows the layer to the actors that carry it",
                byTagResult.Count == 1 && byTagResult.Points[0].Position.X > 0.0f &&
                byTagResult.Points[0].Position.X < 100.0f,
                Describe(byTagResult));

            var capped = Make(world);
            capped.Tag = "village";
            capped.MaxActors = 0;

            var cappedWorld = World(Actor("M1"), Actor("M2"), Actor("M3"));
            var cappedNode = Make(cappedWorld);
            cappedNode.MaxActors = 2;

            check("the cap counts matches rather than the actors read",
                cappedNode.Execute(Context(cappedWorld), Array.Empty<PCGData>()).Count == 2);

            // Determinism: the same scene produces the same layer, which is what makes a partitioned world agree
            // with itself across cells.
            var first = Make(world).Execute(Context(world), Array.Empty<PCGData>());
            var second = Make(world).Execute(Context(world), Array.Empty<PCGData>());

            bool same = first.Count == second.Count;

            for (int i = 0; same && i < first.Count; i++)
                same = Near(first.Points[i].Position.X, second.Points[i].Position.X) &&
                       first.Points[i].Seed == second.Points[i].Seed;

            check("two runs over the same scene produce the same layer", same);
        }

        private static void FailureTests(CheckDelegate check)
        {
            // No source - a headless run, or a graph run outside a level: the contract is to say so and produce
            // nothing rather than to invent actors or to empty a layer that had something.
            var empty = new PCGWorldContext();
            var node = Make(empty);

            var result = node.Execute(Context(empty), Array.Empty<PCGData>());

            check("a run with no actor source reports why and produces nothing",
                result.Count == 0 && !string.IsNullOrEmpty(node.LastError) && node.LastError.Contains("no actor source"),
                node.LastError);

            // A scene whose actors none match is not an error: it is the honest answer for "no marker has that tag".
            var world = World(Actor("Marker_A"));
            var picky = Make(world);
            picky.Tag = "nothing-has-this";

            var picked = picky.Execute(Context(world), Array.Empty<PCGData>());

            check("a filter that matches nothing is empty without complaining",
                picked.Count == 0 && string.IsNullOrEmpty(picky.LastError) && picky.MatchedCount == 0,
                picky.LastError ?? "ok");

            // An actor with no tag must not throw or drop out when no tag filter is set - the empty string is a
            // legitimate value, not a missing one.
            var untagged = Actor("Marker_B");
            untagged.Tag = null;
            untagged.LayerName = null;

            var untaggedWorld = World(untagged);
            var plain = Make(untaggedWorld);
            var plainResult = plain.Execute(Context(untaggedWorld), Array.Empty<PCGData>());

            check("an actor with no tag or layer still produces a point",
                plainResult.Count == 1 && string.IsNullOrEmpty(plain.LastError), plainResult.Count.ToString());
        }

        // ---- fixtures -------------------------------------------------------

        private static PCGActorSamplerNode Make(PCGWorldContext world)
        {
            return new PCGActorSamplerNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGActorSamplerNode.Id,
                new object[] { string.Empty, string.Empty, string.Empty, true, 0, 0, true },
                0));
        }

        private static PCGWorldContext World(params PCGActorInfo[] actors)
        {
            var list = new List<PCGActorInfo>(actors);

            return new PCGWorldContext
            {
                ActorSource = into =>
                {
                    into.Clear();
                    into.AddRange(list);
                    return list.Count;
                },
            };
        }

        private static PCGExecutionContext Context(PCGWorldContext world) =>
            new PCGExecutionContext
            {
                Seed = 20261008,
                World = world,
                GenerationBounds = new BoundingBox(new Vector3(-2000.0f, -100.0f, -2000.0f),
                                                   new Vector3(2000.0f, 100.0f, 2000.0f)),
            };

        private static PCGActorInfo Actor(string name, Vector3? position = null, float yawDegrees = 0.0f)
        {
            return new PCGActorInfo
            {
                Id = Guid.NewGuid(),
                Name = name,
                Tag = "village",
                LayerName = "Triggers",
                Position = position ?? Vector3.Zero,
                Rotation = PCGPointTransform.EulerDegrees(new Vector3(0.0f, yawDegrees, 0.0f)),
                Scale = new Vector3(100.0f, 100.0f, 100.0f),
            };
        }

        private static string[] ReadStrings(PCGData data, string column, int count)
        {
            var attributes = data.SharedAttributes;

            if (attributes == null || !attributes.Contains(column))
                return null;

            var values = new string[count];

            for (int i = 0; i < count; i++)
                values[i] = attributes.GetString(column, i);

            return values;
        }

        private static bool Near(float a, float b, float tolerance = 1e-3f) => Math.Abs(a - b) <= tolerance;

        private static string Describe(PCGData data)
        {
            var parts = new List<string>();

            for (int i = 0; i < Math.Min(data.Count, 4); i++)
            {
                var point = data.Points[i];
                parts.Add($"({point.Position.X:F0},{point.Position.Z:F0})");
            }

            return $"{data.Count}: {string.Join(" ", parts)}";
        }

    }
}
