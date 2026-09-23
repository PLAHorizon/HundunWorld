// PCG Plugin - Node parameter helpers

using System;
using FlaxEngine;
using PCG.Graph;

namespace PCG.Nodes
{
    /// <summary>
    /// Typed, bounds-checked reader for a node's positional parameter array.
    /// </summary>
    /// <remarks>
    /// Node factories receive values positionally. Reading them through this helper means a graph saved
    /// against an older parameter layout degrades to defaults for the fields it does not have, instead of
    /// throwing an index exception while loading a scene.
    /// </remarks>
    public readonly struct PCGNodeParameters
    {
        private readonly object[] _values;
        private readonly string _nodeTypeId;

        /// <summary>Creates a reader over a value array.</summary>
        public PCGNodeParameters(object[] values, string nodeTypeId)
        {
            _values = values;
            _nodeTypeId = nodeTypeId;
        }

        /// <summary>Number of stored values.</summary>
        public int Count => _values?.Length ?? 0;

        /// <summary>True when the array holds nothing.</summary>
        public bool IsEmpty => Count == 0;

        /// <summary>Reads a value at an index, or the fallback when missing or of the wrong type.</summary>
        public T Get<T>(int index, T fallback)
        {
            if (_values == null || index < 0 || index >= _values.Length)
                return fallback;

            var raw = _values[index];
            if (raw == null)
                return fallback;

            if (raw is T typed)
                return typed;

            try
            {
                // Graphs are authored in the editor and stored as plain objects, so a float may well come
                // back as a double or an int. Convert rather than reject.
                if (typeof(T).IsEnum && raw is string s)
                    return (T)Enum.Parse(typeof(T), s, true);

                return (T)Convert.ChangeType(raw, typeof(T));
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        /// <summary>Reads a vector, accepting a Vector3 or a serialized Float3.</summary>
        public Vector3 GetVector3(int index, Vector3 fallback)
        {
            if (_values == null || index < 0 || index >= _values.Length)
                return fallback;

            switch (_values[index])
            {
            case Vector3 v: return v;
            case Float3 f: return new Vector3(f.X, f.Y, f.Z);
            default: return fallback;
            }
        }

        /// <summary>Reads an asset id, accepting a Guid or a string form.</summary>
        public Guid GetAssetId(int index, Guid fallback = default)
        {
            if (_values == null || index < 0 || index >= _values.Length)
                return fallback;

            switch (_values[index])
            {
            case Guid g: return g;
            case string s when Guid.TryParse(s, out var parsed): return parsed;
            default: return fallback;
            }
        }

        /// <inheritdoc />
        public override string ToString() => $"PCGNodeParameters({_nodeTypeId}, {Count} value(s))";
    }
}
