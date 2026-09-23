// PCG Plugin - Execution context and node interfaces
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §4.2 and §4.3.

using System;
using System.Threading;
using FlaxEngine;

namespace PCG.Execution
{
    /// <summary>Debug output level requested for a graph run.</summary>
    public enum PCGDebugMode
    {
        /// <summary>No debug output.</summary>
        None = 0,

        /// <summary>Draw point positions.</summary>
        Points = 1,

        /// <summary>Draw point positions coloured by density.</summary>
        PointsDensity = 2,

        /// <summary>Draw point bounds.</summary>
        Bounds = 3,

        /// <summary>Draw everything available.</summary>
        Full = 4,
    }

    /// <summary>
    /// Everything a node needs from its surroundings: seeding, the generation volume, world queries and
    /// cancellation.
    /// </summary>
    /// <remarks>
    /// An instance is created per graph execution and is safe to read from worker threads. Nodes must not
    /// mutate it.
    /// </remarks>
    public sealed class PCGExecutionContext
    {
        /// <summary>Graph-level random seed. All node randomness derives from this plus node and point ids.</summary>
        public int Seed;

        /// <summary>Region the graph is allowed to generate into, in world space.</summary>
        public BoundingBox GenerationBounds;

        /// <summary>World queries (terrain, actor lookup).</summary>
        public PCGWorldContext World;

        /// <summary>Cooperative cancellation. Long loops must poll this; see <see cref="ThrowIfCancelled"/>.</summary>
        public CancellationToken CancelToken;

        /// <summary>Requested debug output.</summary>
        public PCGDebugMode DebugMode;

        /// <summary>Originating PCG volume, when the run came from one. Nodes may read but not write it.</summary>
        public object Source;

        /// <summary>Identifier of the node currently executing, for logging and diagnostics.</summary>
        public int CurrentNodeId;

        /// <summary>Creates a context with default values.</summary>
        public PCGExecutionContext()
        {
            World = new PCGWorldContext();
        }

        /// <summary>Creates a fully specified context.</summary>
        public PCGExecutionContext(int seed, BoundingBox bounds, PCGWorldContext world, CancellationToken token)
        {
            Seed = seed;
            GenerationBounds = bounds;
            World = world;
            CancelToken = token;
        }

        /// <summary>
        /// Polls the cancellation token every <paramref name="interval"/> iterations.
        /// </summary>
        /// <remarks>
        /// A token only takes effect where it is checked. Long per-point loops must call this periodically,
        /// otherwise "cancel the previous run when the graph changes" does not actually interrupt anything
        /// and fast parameter dragging piles up CPU-saturating stale runs.
        /// </remarks>
        public void ThrowIfCancelled(int iteration, int interval = 256)
        {
            if (interval <= 0 || (iteration % interval) == 0)
                CancelToken.ThrowIfCancellationRequested();
        }

        /// <summary>Gets a deterministic RNG for one point of the currently executing node.</summary>
        public PCGDeterministicRng GetRng(int pointIndex)
        {
            return new PCGDeterministicRng(Seed, CurrentNodeId, pointIndex);
        }

        /// <summary>Gets a deterministic RNG for the currently executing node as a whole.</summary>
        public PCGDeterministicRng GetNodeRng()
        {
            return new PCGDeterministicRng(Seed, CurrentNodeId);
        }
    }

    /// <summary>Direction of a node pin.</summary>
    public enum PCGPinDirection
    {
        /// <summary>Consumes data.</summary>
        Input = 0,

        /// <summary>Produces data.</summary>
        Output = 1,
    }

    /// <summary>
    /// Declares one pin of a node. Order matters: it defines the pin index used by graph edges.
    /// </summary>
    public sealed class PCGPinDefinition
    {
        /// <summary>Display name, unique within the node.</summary>
        public string Name;

        /// <summary>Direction.</summary>
        public PCGPinDirection Direction;

        /// <summary>True when the node cannot run without data on this input.</summary>
        public bool IsRequired;

        /// <summary>When true, several edges may target this input and the executor passes them as one array.</summary>
        public bool AllowMultiple;

        /// <summary>Human-readable description shown in the graph editor.</summary>
        public string Tooltip;

        /// <summary>Creates a definition.</summary>
        public PCGPinDefinition(string name, PCGPinDirection direction, bool isRequired = false,
            bool allowMultiple = false, string tooltip = null)
        {
            Name = name;
            Direction = direction;
            IsRequired = isRequired;
            AllowMultiple = allowMultiple;
            Tooltip = tooltip;
        }

        /// <inheritdoc />
        public override string ToString() => $"{(Direction == PCGPinDirection.Input ? "In" : "Out")} {Name}";
    }

    /// <summary>
    /// Base class for every PCG node.
    /// </summary>
    /// <remarks>
    /// Implementations must be stateless beyond their serialized parameters: the executor caches one
    /// instance per graph node and may call <see cref="OnExecute"/> repeatedly, and in the editor across
    /// script reloads. Never cache <see cref="PCGData"/> between runs.
    /// </remarks>
    public abstract class PCGNodeBase
    {
        /// <summary>
        /// Stable type identifier. Must match the id registered in <see cref="PCGNodeRegistry"/> and, once
        /// the graph editor exists, the <c>NodeArchetype.TypeID</c>.
        /// </summary>
        public abstract string TypeId { get; }

        /// <summary>Human-readable name for logs and the editor.</summary>
        public abstract string DisplayName { get; }

        /// <summary>Input pins, in index order.</summary>
        public abstract PCGPinDefinition[] InputPins { get; }

        /// <summary>Output pins, in index order.</summary>
        public abstract PCGPinDefinition[] OutputPins { get; }

        /// <summary>
        /// True when <see cref="OnExecute"/> may run on a worker thread. Defaults to false.
        /// </summary>
        /// <remarks>
        /// Only sampling-style nodes that read immutable inputs and touch no scene state qualify. Anything
        /// that creates actors, writes assets or calls the physics scene must stay false.
        /// </remarks>
        public virtual bool IsThreadSafe => false;

        /// <summary>
        /// Runs the node.
        /// </summary>
        /// <param name="context">Execution context.</param>
        /// <param name="inputs">
        /// One entry per input pin in declaration order. Entries are null for unconnected optional pins.
        /// For pins with <see cref="PCGPinDefinition.AllowMultiple"/>, the entry is an array of every
        /// incoming data set in edge order, and is never null (it may be empty).
        /// </param>
        /// <returns>The output data for pin 0, or null when the node has no output pins.</returns>
        public abstract PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs);

        /// <summary>
        /// Runs the node, validating that the executor supplied the pins the node declared.
        /// </summary>
        internal PCGData Execute(PCGExecutionContext context, PCGData[] inputs)
        {
            var declared = InputPins;
            if (inputs == null || inputs.Length != declared.Length)
            {
                throw new PCGNodeExecutionException(
                    $"Node '{TypeId}' declares {declared.Length} input pin(s) but received {inputs?.Length ?? 0}.");
            }

            return OnExecute(context, inputs);
        }

        /// <summary>
        /// Produces a short summary of the node's current parameters, for graph logging.
        /// </summary>
        public virtual string DescribeParameters() => string.Empty;

        /// <inheritdoc />
        public override string ToString() => $"{DisplayName} ({TypeId})";
    }

    /// <summary>Raised when a node fails at run time.</summary>
    public sealed class PCGNodeExecutionException : Exception
    {
        /// <summary>Creates the exception.</summary>
        public PCGNodeExecutionException(string message) : base(message)
        {
        }

        /// <summary>Creates the exception with an inner cause.</summary>
        public PCGNodeExecutionException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
