// PCG Plugin - Grammar (Structure)
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §5.1.31.

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// Grows a structure from a symbol by rewriting it, so a block becomes a row of shops, a shop becomes rooms.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="PCGSplitGrammarNode"/> cuts by a table indexed on depth, which is enough while every cell at the
    /// same depth is the same kind of thing. Real structures are not like that: a lot holds shops and a back yard,
    /// a shop holds rooms, a room holds nothing - the <b>kind</b> of a cell is what decides how it is cut, and the
    /// same depth can hold several kinds at once. That is what a symbol grammar adds, and it is the difference
    /// between a grid of equal plots and a street.
    /// </para>
    /// <para>
    /// The rule language is one line of productions, applied from left to right:
    /// <code>
    /// Lot = Shop,Shop: X:0.5,0.5; Shop = Room,Room,Back: Z:0.35,0.35,0.3
    /// </code>
    /// <c>Lot</c> is cut along X into two <c>Shop</c>s; each <c>Shop</c> is cut along Z into two <c>Room</c>s and a
    /// <c>Back</c>. A symbol with <b>no</b> production is a leaf - <c>Room</c> and <c>Back</c> above - so a grammar
    /// ends wherever the author stops naming productions, and leaves of different kinds and different depths
    /// coexist, which is the point.
    /// </para>
    /// <para>
    /// A production may name the symbol it rewrites (<c>Room = Room,Room</c>), which is how recursion - and with it
    /// a hierarchy of any depth - is expressed. Recursion terminates on <c>Max Depth</c>, on <c>Min Cell Size</c>,
    /// and finally on the cell budget, so a grammar that never bottoms out is reported rather than allowed to run.
    /// </para>
    /// <para>
    /// Each leaf carries its symbol twice: as a number (<c>Symbol</c>, the ordinal of the symbol in the production
    /// list) so filters, remaps and math can drive off it, and as text (<c>SymbolName</c>) so a designer reading an
    /// inspection dump can see which kind of thing they are looking at. Ordinals follow first appearance in the
    /// text, so editing the production list can renumber them - the name column is the stable one.
    /// </para>
    /// </remarks>
    public sealed class PCGGrammarNode : PCGNodeBase
    {
        /// <summary>Stable type id.</summary>
        public const string Id = "Grammar";

        // Parameter indices. Order is part of the saved graph format; append only.
        private const int PProductions = 0;
        private const int PRoot = 1;
        private const int PMaxDepth = 2;
        private const int PMinCellSize = 3;
        private const int PCellSize = 4;
        private const int PUsePointScale = 5;
        private const int PWriteNames = 6;

        /// <summary>Number of parameters this node reads.</summary>
        public const int ParameterCount = 7;

        /// <summary>Column holding the leaf's symbol ordinal.</summary>
        public const string SymbolColumn = "Symbol";

        /// <summary>Column holding the leaf's symbol name.</summary>
        public const string SymbolNameColumn = "SymbolName";

        /// <summary>Most productions a grammar may have.</summary>
        public const int MaxProductions = 32;

        /// <summary>Deepest rewrite accepted, whatever the field says.</summary>
        public const int MaxDepthLimit = 32;

        /// <summary>Cells one run may emit before it stops and says so.</summary>
        public const int LeafBudget = 200000;

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>The productions, semicolon separated.</summary>
        public string Productions = "Lot = Shop,Shop: X:0.5,0.5; Shop = Room,Room,Back: Z:0.35,0.35,0.3";

        /// <summary>Symbol every seed starts as.</summary>
        public string Root = "Lot";

        /// <summary>How many rewrites a cell may take before it is a leaf.</summary>
        public int MaxDepth = 4;

        /// <summary>Cells whose largest side is below this stop being rewritten. Zero disables the check.</summary>
        public float MinCellSize;

        /// <summary>Cell size used for seeds that carry no usable scale.</summary>
        public Vector3 CellSize = new Vector3(1000.0f, 200.0f, 1000.0f);

        /// <summary>When set, each seed's own scale is its cell size instead of <see cref="CellSize"/>.</summary>
        public bool UsePointScale;

        /// <summary>When set, the leaf's symbol name is written to <see cref="SymbolNameColumn"/>.</summary>
        public bool WriteNames = true;

        /// <summary>Creates the node from serialized parameters.</summary>
        public PCGGrammarNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            Productions = _params.Get(PProductions,
                "Lot = Shop,Shop: X:0.5,0.5; Shop = Room,Room,Back: Z:0.35,0.35,0.3") ?? string.Empty;
            Root = _params.Get(PRoot, "Lot") ?? string.Empty;
            MaxDepth = Math.Max(0, _params.Get(PMaxDepth, 4));
            MinCellSize = Math.Max(0.0f, _params.Get(PMinCellSize, 0.0f));
            CellSize = _params.GetVector3(PCellSize, new Vector3(1000.0f, 200.0f, 1000.0f));
            UsePointScale = _params.Get(PUsePointScale, false);
            WriteNames = _params.Get(PWriteNames, true);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Grammar";

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
                tooltip: "One point per leaf cell, carrying the symbol it ended as."),
        };

        /// <inheritdoc />
        public override string DescribeParameters()
        {
            int productions = CountProductions(Productions);
            return $"{Root}, {productions} production(s), depth<={Math.Min(MaxDepth, MaxDepthLimit)}" +
                   (MinCellSize > 0.0f ? $", min cell {MinCellSize:F0}" : string.Empty) +
                   (UsePointScale ? ", cell = point scale" : $", cell = {CellSize}");
        }

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs != null && inputs.Length > 0 ? inputs[0] : null;

            if (!TryParseProductions(Productions, out var grammar, out string parseError))
            {
                LastError = $"Grammar could not read its productions: {parseError}. The input passes through unchanged.";
                return input != null ? input.Clone() : PCGData.Empty;
            }

            if (!grammar.TryFindSymbol(Root, out int root))
            {
                // A root that appears nowhere is almost always a typo, and the silent alternative - one leaf per
                // seed - would look like the grammar "doing nothing" rather than like a name that does not exist.
                LastError = $"Grammar's root symbol '{Root}' does not appear in the productions{grammar.SymbolList()}. " +
                            "The input passes through unchanged.";
                return input != null ? input.Clone() : PCGData.Empty;
            }

            var leaves = new List<Leaf>(64);
            int depthLimit = Math.Min(Math.Max(MaxDepth, 0), MaxDepthLimit);

            if (input == null || input.Count == 0)
            {
                var size = ClampedCellSize();

                Expand(new PCGCell(context.GenerationBounds.Center, size, Quaternion.Identity, 1.0f, context.Seed, 0),
                       root, grammar, depthLimit, leaves);
            }
            else
            {
                for (int i = 0; i < input.Count; i++)
                {
                    context.ThrowIfCancelled(i);

                    var seed = input.Points[i];
                    var fromPoint = new Vector3(seed.Scale.X, seed.Scale.Y, seed.Scale.Z);

                    // Same rule as Split Grammar: a scale that cannot describe a cell means "use the configured one".
                    var size = UsePointScale && IsUsable(fromPoint) ? fromPoint : ClampedCellSize();

                    Expand(new PCGCell(seed.Position, size, seed.Rotation, seed.Density, seed.Seed, 0),
                           root, grammar, depthLimit, leaves);
                }
            }

            if (leaves.Count == 0)
                return PCGData.Empty;

            var cells = new List<PCGCell>(leaves.Count);

            for (int i = 0; i < leaves.Count; i++)
                cells.Add(leaves[i].Cell);

            var result = PCGCellSplit.ToPoints(cells);
            var symbols = PCGCellSplit.DeclareColumn(result, SymbolColumn);
            var names = WriteNames ? PCGCellSplit.DeclareTextColumn(result, SymbolNameColumn) : null;

            for (int i = 0; i < leaves.Count; i++)
            {
                if (symbols != null)
                    symbols[i] = leaves[i].Symbol;

                if (names != null)
                    names[i] = grammar.NameOf(leaves[i].Symbol);
            }

            return result;
        }

        /// <summary>A leaf cell together with the symbol it ended as.</summary>
        private struct Leaf
        {
            public PCGCell Cell;
            public int Symbol;
        }

        /// <summary>
        /// Rewrites <paramref name="root"/> until its cells stop, appending the survivors to <paramref name="leaves"/>.
        /// </summary>
        /// <remarks>
        /// Depth first, children in production order, so leaves read left to right the way the productions were
        /// written; the children are pushed in reverse because a stack pops them back in order.
        /// </remarks>
        private void Expand(
            PCGCell root, int rootSymbol, Grammar grammar, int depthLimit, List<Leaf> leaves)
        {
            var stack = new Stack<Leaf>(16);
            stack.Push(new Leaf { Cell = root, Symbol = rootSymbol });

            while (stack.Count > 0)
            {
                var leaf = stack.Pop();

                if (leaves.Count >= LeafBudget)
                {
                    LastError = $"Grammar stopped at its budget of {LeafBudget} cells. " +
                                "Lower Max Depth, set a Min Cell Size, or stop the recursion sooner.";
                    return;
                }

                if (leaf.Cell.Depth >= depthLimit ||
                    !grammar.TryGet(leaf.Symbol, out var production) ||
                    !PCGCellSplit.CanCut(leaf.Cell, production.Axis, MinCellSize))
                {
                    leaves.Add(leaf);
                    continue;
                }

                var children = new PCGCell[production.Slices.Length];
                PCGCellSplit.Split(leaf.Cell, production.Axis, production.Slices, children);

                for (int i = children.Length - 1; i >= 0; i--)
                    stack.Push(new Leaf { Cell = children[i], Symbol = production.Children[i] });
            }
        }

        private Vector3 ClampedCellSize() =>
            new Vector3(
                Math.Max(0.001f, CellSize.X),
                Math.Max(0.001f, CellSize.Y),
                Math.Max(0.001f, CellSize.Z));

        private static bool IsUsable(Vector3 size) =>
            size.X > 0.0f && size.Y > 0.0f && size.Z > 0.0f;

        private static int CountProductions(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            int count = 0;

            foreach (string part in text.Split(';'))
            {
                if (part.IndexOf('=') > 0)
                    count++;
            }

            return count;
        }

        /// <summary>One production: how a symbol is replaced, and how its cell is cut.</summary>
        internal struct Production
        {
            public int[] Children;
            public int Axis;
            public float[] Slices;
        }

        /// <summary>A parsed grammar: its symbol table and its productions.</summary>
        internal sealed class Grammar
        {
            private readonly List<string> _symbols;
            private readonly List<Production?> _productions;

            internal Grammar(List<string> symbols, List<Production?> productions)
            {
                _symbols = symbols;
                _productions = productions;
            }

            /// <summary>How many distinct symbols the grammar names.</summary>
            public int SymbolCount => _symbols.Count;

            /// <summary>Resolves a symbol name to its ordinal.</summary>
            public bool TryFindSymbol(string name, out int symbol)
            {
                symbol = -1;

                if (string.IsNullOrWhiteSpace(name))
                    return false;

                for (int i = 0; i < _symbols.Count; i++)
                {
                    if (string.Equals(_symbols[i], name.Trim(), StringComparison.Ordinal))
                    {
                        symbol = i;
                        return true;
                    }
                }

                return false;
            }

            /// <summary>Whether a symbol is rewritten at all; a symbol with no production is a leaf.</summary>
            public bool TryGet(int symbol, out Production production)
            {
                production = default;

                if (symbol < 0 || symbol >= _productions.Count)
                    return false;

                var found = _productions[symbol];

                if (found == null)
                    return false;

                production = found.Value;
                return true;
            }

            /// <summary>The name of a symbol ordinal, for the name column.</summary>
            public string NameOf(int symbol) =>
                symbol >= 0 && symbol < _symbols.Count ? _symbols[symbol] : string.Empty;

            /// <summary>The symbol names, for a message that has to say what the grammar does know.</summary>
            public string SymbolList() =>
                _symbols.Count == 0 ? " (it names no symbols)" : $" (it names: {string.Join(", ", _symbols)})";
        }

        /// <summary>
        /// Parses the production list: <c>Symbol = Child,Child: axis:ratio,ratio</c>, separated by semicolons.
        /// </summary>
        /// <remarks>
        /// The number of children must equal the number of ratios - a mismatch is the one mistake that would
        /// otherwise be invisible, because the cut would be right while the kinds were silently shifted along it.
        /// Symbol ordinals come from first appearance in the text, so a table's numbers are stable while it is
        /// appended to and change when it is reordered; the name column is what a designer should key on.
        /// </remarks>
        internal static bool TryParseProductions(string text, out Grammar grammar, out string error)
        {
            grammar = null;
            error = null;

            if (string.IsNullOrWhiteSpace(text))
            {
                error = "the production list is empty";
                return false;
            }

            var symbols = new List<string>(8);
            var pending = new List<PendingProduction>(8);
            var parts = text.Split(';');

            for (int p = 0; p < parts.Length; p++)
            {
                string part = parts[p].Trim();

                if (part.Length == 0)
                    continue;

                if (pending.Count >= MaxProductions)
                {
                    error = $"more than {MaxProductions} productions";
                    return false;
                }

                int equals = part.IndexOf('=');

                if (equals <= 0 || equals == part.Length - 1)
                {
                    error = $"production {p + 1} ('{part}') is not written as Symbol = Child,Child: axis:ratio,ratio";
                    return false;
                }

                string name = part.Substring(0, equals).Trim();

                if (name.Length == 0 || name.IndexOf(':') >= 0 || name.IndexOf(',') >= 0)
                {
                    error = $"production {p + 1} has no usable symbol name before the '='";
                    return false;
                }

                string body = part.Substring(equals + 1);
                int colon = body.IndexOf(':');

                if (colon <= 0 || colon == body.Length - 1)
                {
                    error = $"production {p + 1} ('{name}') has no ': axis:ratios' after its children";
                    return false;
                }

                string childrenText = body.Substring(0, colon).Trim();
                string splitText = body.Substring(colon + 1).Trim();
                var children = new List<string>(4);

                foreach (string child in childrenText.Split(','))
                {
                    string trimmed = child.Trim();

                    if (trimmed.Length == 0)
                    {
                        error = $"production {p + 1} ('{name}') has an empty child name";
                        return false;
                    }

                    children.Add(trimmed);
                }

                int splitColon = splitText.IndexOf(':');

                if (splitColon <= 0 || splitColon == splitText.Length - 1)
                {
                    error = $"production {p + 1} ('{name}') is not written as axis:ratio,ratio";
                    return false;
                }

                if (!PCGCellSplit.TryParseAxis(splitText.Substring(0, splitColon), out int axis, out string axisError))
                {
                    error = $"production {p + 1} ('{name}') names axis {axisError}";
                    return false;
                }

                if (!PCGCellSplit.TryParseRatios(splitText.Substring(splitColon + 1), out var slices,
                        out string ratioError))
                {
                    error = $"production {p + 1} ('{name}') {ratioError}";
                    return false;
                }

                // The one mismatch that would otherwise be invisible: the cut would be right while the kinds were
                // silently shifted along it.
                if (children.Count != slices.Length)
                {
                    error = $"production {p + 1} ('{name}') names {children.Count} child symbol(s) but " +
                            $"{slices.Length} slice(s): each child needs its own share of the cell";
                    return false;
                }

                Collect(symbols, name);

                foreach (string child in children)
                    Collect(symbols, child);

                pending.Add(new PendingProduction(name, children, axis, slices));
            }

            if (pending.Count == 0)
            {
                error = "the production list has no productions";
                return false;
            }

            // Resolved after the whole list is read: a child may name a symbol whose own production comes later.
            var productions = new List<Production?>(symbols.Count);

            for (int i = 0; i < symbols.Count; i++)
                productions.Add(null);

            foreach (var item in pending)
            {
                int symbol = symbols.IndexOf(item.Name);

                if (productions[symbol] != null)
                {
                    error = $"symbol '{item.Name}' has more than one production";
                    return false;
                }

                var production = new Production
                {
                    Axis = item.Axis,
                    Slices = item.Slices,
                    Children = new int[item.Children.Count],
                };

                for (int i = 0; i < item.Children.Count; i++)
                    production.Children[i] = symbols.IndexOf(item.Children[i]);

                productions[symbol] = production;
            }

            grammar = new Grammar(symbols, productions);
            return true;
        }

        /// <summary>One production while the symbol table is still being collected.</summary>
        private readonly struct PendingProduction
        {
            public readonly string Name;
            public readonly List<string> Children;
            public readonly int Axis;
            public readonly float[] Slices;

            public PendingProduction(string name, List<string> children, int axis, float[] slices)
            {
                Name = name;
                Children = children;
                Axis = axis;
                Slices = slices;
            }
        }

        private static void Collect(List<string> symbols, string name)
        {
            foreach (string existing in symbols)
            {
                if (string.Equals(existing, name, StringComparison.Ordinal))
                    return;
            }

            symbols.Add(name);
        }
    }
}
