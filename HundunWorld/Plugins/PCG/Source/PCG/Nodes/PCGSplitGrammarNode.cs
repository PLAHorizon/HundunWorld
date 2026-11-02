// PCG Plugin - Split Grammar (Structure)
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.29.

using System;
using System.Collections.Generic;
using System.Globalization;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// Recursively subdivides each seed cell along X/Y/Z by a rule table, emitting one point per surviving cell.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything else in the plugin turns a field into points and then filters or transforms them; a city block,
    /// a building with floors, a room with a corridor are none of those - they are <b>subdivisions</b>, and without
    /// one, "three shops along the front, each split twice going back" has to be hand-placed. UE5 calls this
    /// structured generation and drives it from a grammar; this is the split-grammar half of it: a rule table
    /// indexed by depth, each rule cutting its cell along one axis into proportional slices.
    /// </para>
    /// <para>
    /// The rule language is one line of text:
    /// <code>
    /// X:0.5,0.5; Z:0.6,0.4
    /// </code>
    /// Depth 0 cuts along X into two halves; depth 1 cuts those along Z into 60/40. A rule with a <b>single</b>
    /// ratio (<c>X:1</c>) means "stop here", so a rule table can end a branch at any depth and leaves of different
    /// depths can coexist. Rules are read by depth, so a table only has to be as long as the deepest cut; a cell
    /// that runs out of rules becomes a leaf.
    /// </para>
    /// <para>
    /// Ratios are normalised to sum 1, which makes <c>0.5,0.5</c> and <c>1,1</c> the same cut - authors write the
    /// proportions they are thinking in. The cut happens in each seed's <b>own</b> frame, so a lot rotated 30
    /// degrees subdivides along its own front, and the leaves keep the seed's rotation.
    /// </para>
    /// <para>
    /// Output points are the leaf cells: position at the cell centre, scale equal to the cell size, seed offset by
    /// the leaf ordinal so downstream randomisation still varies per leaf, and density inherited from the seed so
    /// "only build where the field is dense" keeps working. Bounds are recomputed from the rotation and scale, so
    /// pruning downstream tests the cell rather than a degenerate point.
    /// </para>
    /// <para>
    /// This node deliberately does not clip to the partition cell: a structure cut at a cell border would lose the
    /// half that belongs to its neighbour. The spawners clip, as they do for every other node.
    /// </para>
    /// </remarks>
    public sealed class PCGSplitGrammarNode : PCGNodeBase
    {
        /// <summary>Stable type id.</summary>
        public const string Id = "SplitGrammar";

        // Parameter indices. Order is part of the saved graph format; append only.
        private const int PRules = 0;
        private const int PMaxDepth = 1;
        private const int PMinCellSize = 2;
        private const int PCellSize = 3;
        private const int PUsePointScale = 4;
        private const int PWriteDepth = 5;

        /// <summary>Number of parameters this node reads.</summary>
        public const int ParameterCount = 6;

        /// <summary>Column holding the depth a leaf was produced at, when it is written.</summary>
        public const string DepthColumn = "Depth";

        /// <summary>Longest rule table accepted, and the most slices one rule may ask for.</summary>
        public const int MaxRules = 16;

        /// <summary>Most slices a single rule may produce.</summary>
        public const int MaxSlices = PCGCellSplit.MaxSlices;

        /// <summary>Deepest cut accepted, whatever the field says.</summary>
        public const int MaxDepthLimit = 16;

        /// <summary>Cells one run may emit before it stops and says so.</summary>
        public const int LeafBudget = 200000;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>The rule table, one rule per depth.</summary>
        public string Rules = "X:0.5,0.5; Z:0.6,0.4";

        /// <summary>How many cuts to make before a cell stops splitting.</summary>
        public int MaxDepth = 2;

        /// <summary>Cells whose largest side is below this stop splitting. Zero disables the check.</summary>
        public float MinCellSize;

        /// <summary>Cell size used for seeds that carry no usable scale.</summary>
        public Vector3 CellSize = new Vector3(1000.0f, 200.0f, 1000.0f);

        /// <summary>When set, each seed's own scale is its cell size instead of <see cref="CellSize"/>.</summary>
        public bool UsePointScale;

        /// <summary>When set, the depth each leaf came from is written to <see cref="DepthColumn"/>.</summary>
        public bool WriteDepth = true;

        /// <summary>Creates the node from serialized parameters.</summary>
        public PCGSplitGrammarNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            Rules = _params.Get(PRules, "X:0.5,0.5; Z:0.6,0.4") ?? string.Empty;
            MaxDepth = Math.Max(0, _params.Get(PMaxDepth, 2));
            MinCellSize = Math.Max(0.0f, _params.Get(PMinCellSize, 0.0f));
            CellSize = _params.GetVector3(PCellSize, new Vector3(1000.0f, 200.0f, 1000.0f));
            UsePointScale = _params.Get(PUsePointScale, false);
            WriteDepth = _params.Get(PWriteDepth, true);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Split Grammar";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input,
                tooltip: "Seed cells. Unconnected, the generation bounds are used as one seed."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "One point per leaf cell, sized to the cell."),
        };

        /// <inheritdoc />
        public override string DescribeParameters()
        {
            string rules = string.IsNullOrWhiteSpace(Rules) ? "no rules" : Rules;
            return $"{rules} | depth<={Math.Min(MaxDepth, MaxDepthLimit)}" +
                   (MinCellSize > 0.0f ? $", min cell {MinCellSize:F0}" : string.Empty) +
                   (UsePointScale ? ", cell = point scale" : $", cell = {CellSize}");
        }

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            if (!TryParseRules(Rules, out var rules, out string parseError))
            {
                LastError = $"Split Grammar could not read the rules '{Rules}': {parseError}. " +
                            "The input passes through unchanged.";
                var passthrough = inputs != null && inputs.Length > 0 ? inputs[0] : null;
                return passthrough != null ? passthrough.Clone() : PCGData.Empty;
            }

            var input = inputs != null && inputs.Length > 0 ? inputs[0] : null;

            var leaves = new List<PCGCell>(64);
            int depthLimit = Math.Min(Math.Max(MaxDepth, 0), MaxDepthLimit);

            if (input == null || input.Count == 0)
            {
                // No seeds: subdivide the generation volume once, so the node is usable on its own - a structure
                // placed by the volume's bounds is the common way to start a diagram.
                var size = ClampedCellSize();

                Grow(new PCGCell(context.GenerationBounds.Center, size, Quaternion.Identity, 1.0f, context.Seed, 0),
                     rules, depthLimit, leaves);
            }
            else
            {
                for (int i = 0; i < input.Count; i++)
                {
                    context.ThrowIfCancelled(i);

                    var seed = input.Points[i];
                    var fromPoint = new Vector3(seed.Scale.X, seed.Scale.Y, seed.Scale.Z);

                    // A seed's own scale is only used when it can actually describe a cell: a sampler that never
                    // touched Scale leaves it at 1, and cutting a 1-unit cell into hundredths produces dust.
                    var size = UsePointScale && IsUsable(fromPoint) ? fromPoint : ClampedCellSize();

                    Grow(new PCGCell(seed.Position, size, seed.Rotation, seed.Density, seed.Seed, 0),
                         rules, depthLimit, leaves);
                }
            }

            if (leaves.Count == 0)
                return PCGData.Empty;

            var result = PCGCellSplit.ToPoints(leaves);

            // Declared before the loop rather than written per point: a column has to exist before it can be
            // written, and the set is exclusive here (this node made it), so declaring it is safe.
            float[] depths = WriteDepth ? PCGCellSplit.DeclareColumn(result, DepthColumn) : null;

            if (depths != null)
            {
                for (int i = 0; i < leaves.Count; i++)
                    depths[i] = leaves[i].Depth;
            }

            return result;
        }

        /// <summary>One cell of the subdivision, in its own frame.</summary>
        /// <summary>
        /// Splits <paramref name="root"/> until its cells stop, appending the survivors to <paramref name="leaves"/>.
        /// </summary>
        /// <remarks>
        /// Depth first, children in rule order, so a leaf list reads left to right the way the cuts were written.
        /// The children are pushed in reverse for exactly that reason: a stack pops them back in order.
        /// </remarks>
        private void Grow(PCGCell root, Rule[] rules, int depthLimit, List<PCGCell> leaves)
        {
            var stack = new Stack<PCGCell>(16);
            stack.Push(root);

            while (stack.Count > 0)
            {
                var cell = stack.Pop();

                if (leaves.Count >= LeafBudget)
                {
                    // Bounded rather than trusted: 0.01 ratios nest into millions of cells, and a graph that hangs
                    // the editor is worse than a graph that says it was cut short.
                    LastError = $"Split Grammar stopped at its budget of {LeafBudget} cells. " +
                                "Raise the ratios, lower Max Depth, or set a Min Cell Size.";
                    return;
                }

                if (cell.Depth >= depthLimit ||
                    cell.Depth >= rules.Length ||
                    rules[cell.Depth].Slices.Length <= 1 ||
                    !PCGCellSplit.CanCut(cell, rules[cell.Depth].Axis, MinCellSize))
                {
                    leaves.Add(cell);
                    continue;
                }

                var children = new PCGCell[rules[cell.Depth].Slices.Length];

                // The cut itself lives in PCGCellSplit: the same code the symbol grammar cuts with, so "the children
                // tile their parent" is one implementation rather than two that could drift.
                PCGCellSplit.Split(cell, rules[cell.Depth].Axis, rules[cell.Depth].Slices, children);

                for (int i = children.Length - 1; i >= 0; i--)
                    stack.Push(children[i]);
            }
        }

        /// <summary>The configured cell size, with every axis made strictly positive.</summary>
        private Vector3 ClampedCellSize() =>
            new Vector3(
                Math.Max(0.001f, CellSize.X),
                Math.Max(0.001f, CellSize.Y),
                Math.Max(0.001f, CellSize.Z));

        /// <summary>True when a length can describe a cell.</summary>
        private static bool IsUsable(Vector3 size) =>
            size.X > 0.0f && size.Y > 0.0f && size.Z > 0.0f;

        /// <summary>One depth's rule: which axis to cut along, and the slice proportions.</summary>
        internal struct Rule
        {
            public int Axis;
            public float[] Slices;
        }

        /// <summary>
        /// Parses the rule table: rules separated by <c>;</c>, each <c>axis:ratio,ratio</c>.
        /// </summary>
        /// <remarks>
        /// A single ratio is a valid rule and means "do not split at this depth" - that is how a table ends a
        /// branch early, so it is not treated as an error. Ratios are normalised to sum 1 so proportions can be
        /// written the way they are thought about (<c>0.6,0.4</c> and <c>3,2</c> cut the same cell the same way).
        /// </remarks>
        internal static bool TryParseRules(string text, out Rule[] rules, out string error)
        {
            rules = Array.Empty<Rule>();
            error = null;

            if (string.IsNullOrWhiteSpace(text))
            {
                error = "the rule table is empty";
                return false;
            }

            var parts = text.Split(';');
            var parsed = new List<Rule>(parts.Length);

            for (int p = 0; p < parts.Length; p++)
            {
                string part = parts[p].Trim();

                if (part.Length == 0)
                    continue;

                if (parsed.Count >= MaxRules)
                {
                    error = $"more than {MaxRules} rules";
                    return false;
                }

                int colon = part.IndexOf(':');
                if (colon <= 0 || colon == part.Length - 1)
                {
                    error = $"rule {p + 1} ('{part}') is not written as axis:ratio,ratio";
                    return false;
                }

                // The axis and ratio vocabulary is shared with the symbol grammar, so the two nodes cannot come to
                // disagree about what "0.5,0.5" or "Z" means.
                if (!PCGCellSplit.TryParseAxis(part.Substring(0, colon), out int axis, out string axisError))
                {
                    error = $"rule {p + 1} names axis {axisError}";
                    return false;
                }

                if (!PCGCellSplit.TryParseRatios(part.Substring(colon + 1), out var slices, out string ratioError))
                {
                    error = $"rule {p + 1} {ratioError}";
                    return false;
                }

                parsed.Add(new Rule { Axis = axis, Slices = slices });
            }

            if (parsed.Count == 0)
            {
                error = "the rule table has no rules";
                return false;
            }

            rules = parsed.ToArray();
            return true;
        }
    }
}
