// PCG Plugin - partition result cache
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §7.3 (决定 A/B/C 的落地).

using System;
using System.Collections.Generic;
using FlaxEngine;

namespace PCG.Execution
{
    /// <summary>
    /// Everything a single partition cell's result depends on, compared field by field.
    /// </summary>
    /// <remarks>
    /// Deliberately not a hash: a cache hit whose key collided would hand back a stale point set, and a stale
    /// generation is silent - the level looks fine and the props are in the wrong place. The fields are cheap to
    /// compare and each one is a real input to the cell's run.
    /// </remarks>
    public readonly struct PCGCellSignature : IEquatable<PCGCellSignature>
    {
        /// <summary>Structural hash of the graph topology, from <see cref="PCGGraphCompiler"/>.</summary>
        public readonly int StructuralHash;

        /// <summary>Run seed, so re-rolling invalidates every cell.</summary>
        public readonly int Seed;

        /// <summary>The graph's own exposed-parameter overrides, folded into the structural hash by the caller.</summary>
        public readonly int ParameterVersion;

        /// <summary>
        /// A caller-maintained version for inputs the graph cannot see - terrain edits, a moved host actor.
        /// </summary>
        public readonly int WorldVersion;

        public readonly Vector3 GenerationMin;
        public readonly Vector3 GenerationMax;

        /// <summary>The cell proper, which is what the merged result keeps.</summary>
        public readonly Vector3 ClipMin;

        public readonly Vector3 ClipMax;

        /// <summary>Padding actually applied, so widening it invalidates the cells it touched.</summary>
        public readonly float Padding;

        public PCGCellSignature(
            int structuralHash, int seed, int parameterVersion, int worldVersion,
            in PCGPartitionCell cell, float padding)
        {
            StructuralHash = structuralHash;
            Seed = seed;
            ParameterVersion = parameterVersion;
            WorldVersion = worldVersion;
            GenerationMin = cell.GenerationBounds.Minimum;
            GenerationMax = cell.GenerationBounds.Maximum;
            ClipMin = cell.ClipBounds.Minimum;
            ClipMax = cell.ClipBounds.Maximum;
            Padding = padding;
        }

        /// <inheritdoc />
        public bool Equals(PCGCellSignature other) =>
            StructuralHash == other.StructuralHash &&
            Seed == other.Seed &&
            ParameterVersion == other.ParameterVersion &&
            WorldVersion == other.WorldVersion &&
            Padding == other.Padding &&
            GenerationMin == other.GenerationMin && GenerationMax == other.GenerationMax &&
            ClipMin == other.ClipMin && ClipMax == other.ClipMax;

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is PCGCellSignature other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StructuralHash;
                hash = hash * 397 + Seed;
                hash = hash * 397 + ParameterVersion;
                hash = hash * 397 + WorldVersion;
                hash = hash * 397 + Padding.GetHashCode();
                return hash;
            }
        }

        /// <inheritdoc />
        public override string ToString() =>
            $"graph {StructuralHash:X8}, seed {Seed}, params {ParameterVersion}, world {WorldVersion}, " +
            $"pad {Padding:0.##}, clip [{ClipMin.X:0.#},{ClipMin.Z:0.#}]-[{ClipMax.X:0.#},{ClipMax.Z:0.#}]";
    }

    /// <summary>
    /// Reuses unchanged partition cells between generation runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why reuse is safe per cell.</b> A cell's run reads the region it was handed, the seed, and the graph -
    /// it does not read its neighbours' points, because the padding made its own neighbourhood complete
    /// (see <see cref="PCGPartitionPlanner"/>). So a cell whose signature is unchanged produces the same points
    /// whether or not anything else moved, and the merged result is identical to running every cell.
    /// </para>
    /// <para>
    /// <b>Two things this refuses to do, on purpose.</b>
    /// <list type="bullet">
    /// <item>It does not cache graphs that write into the scene. A spawner's cell is not just data: its actors
    /// exist in the level, and reuse would mean those actors were never created this run while the cache
    /// happily returns their points. **The rule lives in `PCGVolume.PartitionRequest`**, which refuses reuse
    /// through the authoritative predicate (`PCGGraphEffects.GraphWritesScene`) rather than keeping a second
    /// list of "which nodes write the scene" — that duplicate knowledge is exactly how this rule went missing
    /// for a long time: it was documented here as a check on `IsThreadSafe == false`, and nothing ever read
    /// that flag, so partitioned volumes with a spawner silently reused cells (measured: 120 / 60 / 72 actors
    /// on three identical runs of the same volume).</item>
    /// <item>It does not survive a truncated run. The combined point ceiling is applied across cells in order, so
    /// a smaller cell earlier in the list can change which cells past it get dropped. When that happens the
    /// cache is emptied rather than kept.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>What is left for the actor route (决定 C).</b> Replacing only one cell's actors needs per-cell
    /// ownership, which the plugin does not record today - a volume's cleanup is "destroy my children". Until
    /// that exists, spawner graphs regenerate in full, which is correct and slower, rather than half-cached.
    /// </para>
    /// </remarks>
    public sealed class PCGPartitionCache
    {
        /// <summary>
        /// Folds the volume's manual version together with a host-supplied one, so either can invalidate cells.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The manual number is the plugin's honest admission that it cannot see the world change - a terrain
        /// brush stroke or a host actor moving reaches no signature it computes by itself. A game that <i>does</i>
        /// know (a terrain revision, an actor's transform stamp, a save counter) hands its number over, and the
        /// cell signatures move with it.
        /// </para>
        /// <para>
        /// Combined rather than replaced, so raising the manual field still works and a volume behaves as before
        /// when no host number is wired. The mixing is a plain positional hash: what matters is that neither input
        /// can be ignored and that the same pair always produces the same token, which is what the tests pin.
        /// </para>
        /// </remarks>
        public static int CombineWorldVersion(int manualVersion, int hostVersion)
        {
            return unchecked(manualVersion * 31 + hostVersion);
        }
        private sealed class Entry
        {
            internal PCGCellSignature Signature;
            internal PCGData[] Outputs;
        }

        private readonly Dictionary<(int, int), Entry> _entries = new Dictionary<(int, int), Entry>();

        /// <summary>Cells reused instead of executed during the most recent run.</summary>
        public int LastRunReusedCells { get; internal set; }

        /// <summary>Cells actually executed during the most recent run.</summary>
        public int LastRunExecutedCells { get; internal set; }

        /// <summary>Cells stored after the most recent run.</summary>
        public int CellCount => _entries.Count;

        /// <summary>Why the last run could not use the cache at all, when that happened.</summary>
        public string LastRefusalReason { get; internal set; }

        /// <summary>
        /// Which sink nodes the stored parts came from. Held once per cache rather than per cell, because the
        /// sink set is the graph's topology and every cell of one graph shares it.
        /// </summary>
        public int[] KnownOutputNodeIndices { get; internal set; }

        /// <summary>Drops every stored cell, e.g. after the volume is cleared.</summary>
        public void Clear(string reason = null)
        {
            _entries.Clear();
            LastRunReusedCells = 0;
            LastRunExecutedCells = 0;
            LastRefusalReason = reason;
        }

        /// <summary>True when a stored cell matches this signature exactly.</summary>
        internal bool TryGet(in PCGPartitionCell cell, in PCGCellSignature signature, out PCGData[] outputs)
        {
            outputs = null;

            if (!_entries.TryGetValue((cell.IndexX, cell.IndexZ), out Entry entry))
                return false;

            if (!entry.Signature.Equals(signature))
                return false;

            outputs = entry.Outputs;
            return true;
        }

        /// <summary>
        /// Records one cell's clipped, per-sink output under the signature that produced it.
        /// </summary>
        internal void Store(
            in PCGPartitionCell cell, in PCGCellSignature signature, PCGData[] outputs, int[] outputNodeIndices)
        {
            if (outputs == null)
                return;

            _entries[(cell.IndexX, cell.IndexZ)] = new Entry
            {
                Signature = signature,
                Outputs = outputs,
            };

            KnownOutputNodeIndices = outputNodeIndices ?? KnownOutputNodeIndices;
        }

        /// <summary>Drops every stored cell; used when a run stopped early and its picture is incomplete.</summary>
        public void InvalidateAll()
        {
            _entries.Clear();
            KnownOutputNodeIndices = null;
        }

        /// <summary>
        /// Forgets one cell's stored points, which is what a streaming eviction does.
        /// </summary>
        /// <remarks>
        /// A stored cell is a description of props standing in the scene, so forgetting it has to travel with
        /// destroying them: leave the entry behind and the next request that reaches this cell reuses points
        /// whose actors are gone - the stale pairing every other reuse path guards against.
        /// </remarks>
        /// <returns>True when an entry was dropped.</returns>
        public bool Evict(int indexX, int indexZ) => _entries.Remove((indexX, indexZ));

        /// <summary>
        /// The cells this run has to execute, given what the cache already holds.
        /// </summary>
        /// <remarks>
        /// This is what lets a scene-writing graph be cached: the caller destroys the props of exactly these
        /// cells before running, and leaves the fresh cells' props standing. Reuse and cleanup therefore consult
        /// one decision, so a cell cannot be reused by the runner while its actors are being destroyed by the
        /// volume (or the other way round), which is the pairing that would either double up props or erase a
        /// cell whose points are still being reported.
        /// </remarks>
        /// <param name="cells">Every planned cell.</param>
        /// <param name="request">The run's cache request; when reuse is off, every cell is dirty.</param>
        /// <param name="seed">The run seed, part of each cell's signature.</param>
        public List<PCGPartitionCell> DirtyCells(
            IReadOnlyList<PCGPartitionCell> cells, PCGPartitionCacheRequest request, int seed)
        {
            var dirty = new List<PCGPartitionCell>();
            if (cells == null)
                return dirty;

            for (int i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];

                if (!request.IsEnabled || !IsFresh(cell, request.SignatureFor(cell, seed)))
                    dirty.Add(cell);
            }

            return dirty;
        }

        /// <summary>True when a stored cell matches this signature exactly.</summary>
        public bool IsFresh(in PCGPartitionCell cell, in PCGCellSignature signature)
        {
            return _entries.TryGetValue((cell.IndexX, cell.IndexZ), out Entry entry) &&
                   entry.Signature.Equals(signature);
        }
    }
}
