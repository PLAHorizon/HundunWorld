// PCG Plugin - Attribute system
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §3.2.

using System;
using System.Collections.Generic;
using FlaxEngine;

namespace PCG
{
    /// <summary>Attribute value types supported by <see cref="PCGAttributeSet"/>.</summary>
    public enum PCGAttributeType
    {
        Float = 0,
        Int = 1,
        Bool = 2,
        Vector2 = 3,
        Vector3 = 4,
        Vector4 = 5,
        Quaternion = 6,
        String = 7,
    }

    /// <summary>
    /// Describes one attribute: its storage name, value type and whether it is a built-in point channel.
    /// </summary>
    [Serializable]
    public struct PCGAttributeDescriptor
    {
        /// <summary>Storage name. Built-ins carry a leading '$' (for example <c>$Density</c>).</summary>
        public string Name;

        /// <summary>Value type.</summary>
        public PCGAttributeType Type;

        /// <summary>True when this attribute is a built-in point channel rather than custom metadata.</summary>
        public bool IsBuiltIn;

        /// <inheritdoc />
        public override string ToString() => $"{Name}:{Type}{(IsBuiltIn ? " (builtin)" : string.Empty)}";
    }

    /// <summary>
    /// Column-oriented (SoA) attribute storage with copy-on-write sharing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists.</b> <see cref="PCGPoint"/> is a struct but holds this class by reference, so
    /// copying a point array copies the <i>reference</i>. Several points (or several data sets produced by
    /// Merge/Split nodes) can therefore alias one storage instance. Without protection, writing one point's
    /// attribute would silently change every aliasing point - a bug that is very hard to trace back.
    /// </para>
    /// <para>
    /// <b>The contract.</b> Sharing is allowed; in-place mutation of shared storage is not. Every mutating
    /// entry point calls <see cref="Detach"/>, which clones the backing columns the first time a shared
    /// instance is written. After the detach, this instance owns fresh arrays while other holders keep
    /// reading the original ones. Detach mutates <c>this</c> in place rather than returning a replacement,
    /// so a mutator cannot accidentally drop the copy - that mistake is impossible by construction here.
    /// </para>
    /// <para>
    /// <b>Consequence.</b> <see cref="GetFloatArray"/> and friends hand out the live column for speed. They
    /// are for readers only. A node that intends to write rows must use the matching <c>GetWritable*</c>
    /// accessor, which detaches first; writing through a read-only column would bypass the contract and
    /// reintroduce the aliasing bug.
    /// </para>
    /// <para>
    /// Built-in channels (<c>$Position</c>, <c>$Rotation</c>, <c>$Scale</c>, <c>$Density</c>, <c>$Seed</c>,
    /// <c>$Bounds</c>, <c>$Slope</c>) are <b>declared</b> here so nodes can query uniformly, but they are not
    /// stored here: they live in the point array. Attempting to write one raises
    /// <see cref="PCGAttributeError.BuiltInReadOnly"/>, which makes the separation impossible to violate
    /// silently.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class PCGAttributeSet
    {
        /// <summary>Leading character that marks a built-in (non-custom) attribute name.</summary>
        public const char BuiltInPrefix = '$';

        /// <summary>Built-in: point world position (Vector3).</summary>
        public const string PositionName = "$Position";
        /// <summary>Built-in: point rotation (Quaternion).</summary>
        public const string RotationName = "$Rotation";
        /// <summary>Built-in: point scale (Vector3, widened from Float3).</summary>
        public const string ScaleName = "$Scale";
        /// <summary>Built-in: point density (Float).</summary>
        public const string DensityName = "$Density";
        /// <summary>Built-in: point seed (Int).</summary>
        public const string SeedName = "$Seed";
        /// <summary>Built-in: point bounds extents (Vector3, widened).</summary>
        public const string BoundsName = "$Bounds";
        /// <summary>Built-in: cached surface slope in degrees (Float). Written by terrain/spline samplers.</summary>
        public const string SlopeName = "$Slope";

        private static readonly PCGAttributeDescriptor[] BuiltInDescriptors =
        {
            new PCGAttributeDescriptor { Name = PositionName, Type = PCGAttributeType.Vector3, IsBuiltIn = true },
            new PCGAttributeDescriptor { Name = RotationName, Type = PCGAttributeType.Quaternion, IsBuiltIn = true },
            new PCGAttributeDescriptor { Name = ScaleName, Type = PCGAttributeType.Vector3, IsBuiltIn = true },
            new PCGAttributeDescriptor { Name = DensityName, Type = PCGAttributeType.Float, IsBuiltIn = true },
            new PCGAttributeDescriptor { Name = SeedName, Type = PCGAttributeType.Int, IsBuiltIn = true },
            new PCGAttributeDescriptor { Name = BoundsName, Type = PCGAttributeType.Vector3, IsBuiltIn = true },
            new PCGAttributeDescriptor { Name = SlopeName, Type = PCGAttributeType.Float, IsBuiltIn = true },
        };

        private Dictionary<string, PCGAttributeDescriptor> _descriptors;
        private Dictionary<string, float[]> _floatArrays;
        private Dictionary<string, int[]> _intArrays;
        private Dictionary<string, bool[]> _boolArrays;
        private Dictionary<string, Vector2[]> _vector2Arrays;
        private Dictionary<string, Vector3[]> _vector3Arrays;
        private Dictionary<string, Vector4[]> _vector4Arrays;
        private Dictionary<string, Quaternion[]> _quaternionArrays;
        private Dictionary<string, string[]> _stringArrays;

        [NonSerialized] private int _refCount = 1;
        private int _count;

        /// <summary>Creates an empty attribute set sized for <paramref name="count"/> points.</summary>
        public PCGAttributeSet(int count)
        {
            _count = count < 0 ? 0 : count;
        }

        /// <summary>Parameterless constructor required by the serializer.</summary>
        public PCGAttributeSet()
        {
        }

        /// <summary>Gets the number of points this set is sized for.</summary>
        public int Count => _count;

        /// <summary>Gets the number of custom (non-built-in) attributes stored.</summary>
        public int AttributeCount => _descriptors?.Count ?? 0;

        /// <summary>Gets the number of live holders. Used by <see cref="Detach"/>.</summary>
        public int RefCount => _refCount;

        /// <summary>True when more than one holder references this storage and the next write will detach it.</summary>
        public bool IsShared => _refCount > 1;

        /// <summary>Enumerates every attribute descriptor, custom first then built-in.</summary>
        public IEnumerable<PCGAttributeDescriptor> GetDescriptors()
        {
            if (_descriptors != null)
            {
                foreach (var kv in _descriptors)
                    yield return kv.Value;
            }

            for (int i = 0; i < BuiltInDescriptors.Length; i++)
                yield return BuiltInDescriptors[i];
        }

        /// <summary>Enumerates only the custom (non-built-in) attribute descriptors.</summary>
        public IEnumerable<PCGAttributeDescriptor> GetCustomDescriptors()
        {
            if (_descriptors == null)
                yield break;

            foreach (var kv in _descriptors)
                yield return kv.Value;
        }

        /// <summary>Returns true when the named attribute exists, including built-in channels.</summary>
        public bool Contains(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            if (IsBuiltInName(name))
                return GetBuiltInDescriptor(name).HasValue;

            return _descriptors != null && _descriptors.ContainsKey(name);
        }

        /// <summary>Gets the descriptor for any attribute, built-in or custom.</summary>
        public bool TryGetDescriptor(string name, out PCGAttributeDescriptor descriptor)
        {
            descriptor = default;
            if (string.IsNullOrEmpty(name))
                return false;

            if (IsBuiltInName(name))
            {
                var builtIn = GetBuiltInDescriptor(name);
                if (!builtIn.HasValue)
                    return false;

                descriptor = builtIn.Value;
                return true;
            }

            return _descriptors != null && _descriptors.TryGetValue(name, out descriptor);
        }

        /// <summary>Returns true when <paramref name="name"/> denotes a built-in channel.</summary>
        public static bool IsBuiltInName(string name)
        {
            return !string.IsNullOrEmpty(name) && name[0] == BuiltInPrefix;
        }

        private static PCGAttributeDescriptor? GetBuiltInDescriptor(string name)
        {
            for (int i = 0; i < BuiltInDescriptors.Length; i++)
            {
                if (string.Equals(BuiltInDescriptors[i].Name, name, StringComparison.Ordinal))
                    return BuiltInDescriptors[i];
            }

            return null;
        }

        // ---- Copy-on-write ---------------------------------------------------

        /// <summary>
        /// Detaches this storage from any other holder by cloning the backing columns.
        /// </summary>
        /// <remarks>
        /// This is the single write barrier of the type. It mutates <c>this</c> rather than returning a
        /// replacement, so it is impossible for a caller to drop the clone. Cheap (a refcount check) and
        /// idempotent once the instance is solely owned.
        /// </remarks>
        public void Detach()
        {
            if (_refCount <= 1)
                return;

            if (_descriptors != null)
            {
                var newDescriptors = new Dictionary<string, PCGAttributeDescriptor>(_descriptors, StringComparer.Ordinal);

                if (_floatArrays != null)
                {
                    var m = new Dictionary<string, float[]>(_floatArrays.Count, StringComparer.Ordinal);
                    foreach (var kv in _floatArrays) m[kv.Key] = (float[])kv.Value.Clone();
                    _floatArrays = m;
                }

                if (_intArrays != null)
                {
                    var m = new Dictionary<string, int[]>(_intArrays.Count, StringComparer.Ordinal);
                    foreach (var kv in _intArrays) m[kv.Key] = (int[])kv.Value.Clone();
                    _intArrays = m;
                }

                if (_boolArrays != null)
                {
                    var m = new Dictionary<string, bool[]>(_boolArrays.Count, StringComparer.Ordinal);
                    foreach (var kv in _boolArrays) m[kv.Key] = (bool[])kv.Value.Clone();
                    _boolArrays = m;
                }

                if (_vector2Arrays != null)
                {
                    var m = new Dictionary<string, Vector2[]>(_vector2Arrays.Count, StringComparer.Ordinal);
                    foreach (var kv in _vector2Arrays) m[kv.Key] = (Vector2[])kv.Value.Clone();
                    _vector2Arrays = m;
                }

                if (_vector3Arrays != null)
                {
                    var m = new Dictionary<string, Vector3[]>(_vector3Arrays.Count, StringComparer.Ordinal);
                    foreach (var kv in _vector3Arrays) m[kv.Key] = (Vector3[])kv.Value.Clone();
                    _vector3Arrays = m;
                }

                if (_vector4Arrays != null)
                {
                    var m = new Dictionary<string, Vector4[]>(_vector4Arrays.Count, StringComparer.Ordinal);
                    foreach (var kv in _vector4Arrays) m[kv.Key] = (Vector4[])kv.Value.Clone();
                    _vector4Arrays = m;
                }

                if (_quaternionArrays != null)
                {
                    var m = new Dictionary<string, Quaternion[]>(_quaternionArrays.Count, StringComparer.Ordinal);
                    foreach (var kv in _quaternionArrays) m[kv.Key] = (Quaternion[])kv.Value.Clone();
                    _quaternionArrays = m;
                }

                if (_stringArrays != null)
                {
                    var m = new Dictionary<string, string[]>(_stringArrays.Count, StringComparer.Ordinal);
                    foreach (var kv in _stringArrays) m[kv.Key] = (string[])kv.Value.Clone();
                    _stringArrays = m;
                }

                _descriptors = newDescriptors;
            }

            _refCount = 1;
        }

        /// <summary>
        /// Registers an additional holder. Every call must be paired with <see cref="Release"/>.
        /// Callers share storage for reads only; the first write detaches.
        /// </summary>
        public PCGAttributeSet Share()
        {
            _refCount++;
            return this;
        }

        /// <summary>Drops one holder.</summary>
        public void Release()
        {
            if (_refCount > 0)
                _refCount--;
        }

        /// <summary>Creates a deep copy sized for the same number of points, solely owned.</summary>
        public PCGAttributeSet Clone()
        {
            var clone = new PCGAttributeSet(_count);

            if (_descriptors != null)
            {
                clone._descriptors = new Dictionary<string, PCGAttributeDescriptor>(_descriptors, StringComparer.Ordinal);

                if (_floatArrays != null)
                {
                    clone._floatArrays = new Dictionary<string, float[]>(_floatArrays.Count, StringComparer.Ordinal);
                    foreach (var kv in _floatArrays) clone._floatArrays[kv.Key] = (float[])kv.Value.Clone();
                }

                if (_intArrays != null)
                {
                    clone._intArrays = new Dictionary<string, int[]>(_intArrays.Count, StringComparer.Ordinal);
                    foreach (var kv in _intArrays) clone._intArrays[kv.Key] = (int[])kv.Value.Clone();
                }

                if (_boolArrays != null)
                {
                    clone._boolArrays = new Dictionary<string, bool[]>(_boolArrays.Count, StringComparer.Ordinal);
                    foreach (var kv in _boolArrays) clone._boolArrays[kv.Key] = (bool[])kv.Value.Clone();
                }

                if (_vector2Arrays != null)
                {
                    clone._vector2Arrays = new Dictionary<string, Vector2[]>(_vector2Arrays.Count, StringComparer.Ordinal);
                    foreach (var kv in _vector2Arrays) clone._vector2Arrays[kv.Key] = (Vector2[])kv.Value.Clone();
                }

                if (_vector3Arrays != null)
                {
                    clone._vector3Arrays = new Dictionary<string, Vector3[]>(_vector3Arrays.Count, StringComparer.Ordinal);
                    foreach (var kv in _vector3Arrays) clone._vector3Arrays[kv.Key] = (Vector3[])kv.Value.Clone();
                }

                if (_vector4Arrays != null)
                {
                    clone._vector4Arrays = new Dictionary<string, Vector4[]>(_vector4Arrays.Count, StringComparer.Ordinal);
                    foreach (var kv in _vector4Arrays) clone._vector4Arrays[kv.Key] = (Vector4[])kv.Value.Clone();
                }

                if (_quaternionArrays != null)
                {
                    clone._quaternionArrays = new Dictionary<string, Quaternion[]>(_quaternionArrays.Count, StringComparer.Ordinal);
                    foreach (var kv in _quaternionArrays) clone._quaternionArrays[kv.Key] = (Quaternion[])kv.Value.Clone();
                }

                if (_stringArrays != null)
                {
                    clone._stringArrays = new Dictionary<string, string[]>(_stringArrays.Count, StringComparer.Ordinal);
                    foreach (var kv in _stringArrays) clone._stringArrays[kv.Key] = (string[])kv.Value.Clone();
                }
            }

            clone._refCount = 1;
            return clone;
        }

        /// <summary>
        /// Declares a custom attribute sized for a different row count, detaching first when shared.
        /// </summary>
        public void DeclareAttribute(string name, PCGAttributeType type)
        {
            ValidateWritableName(name);
            Detach();

            _descriptors ??= new Dictionary<string, PCGAttributeDescriptor>(StringComparer.Ordinal);

            if (_descriptors.TryGetValue(name, out var existing))
            {
                if (existing.Type != type)
                {
                    throw new PCGAttributeException(
                        PCGAttributeError.TypeMismatch,
                        $"Attribute '{name}' already exists with type {existing.Type}, cannot redeclare as {type}.");
                }

                return;
            }

            _descriptors.Add(name, new PCGAttributeDescriptor { Name = name, Type = type, IsBuiltIn = false });
            AllocateStorage(name, type, _count);
        }

        /// <summary>
        /// Declares a custom attribute without detaching. For exclusive use during construction of a set
        /// that is known to be unshared (for example right after <c>new PCGAttributeSet(n)</c>), so a
        /// sampler can declare many attributes without cloning repeatedly.
        /// </summary>
        public void DeclareAttributeUnsafe(string name, PCGAttributeType type)
        {
            ValidateWritableName(name);

            _descriptors ??= new Dictionary<string, PCGAttributeDescriptor>(StringComparer.Ordinal);

            if (_descriptors.ContainsKey(name))
                return;

            _descriptors.Add(name, new PCGAttributeDescriptor { Name = name, Type = type, IsBuiltIn = false });
            AllocateStorage(name, type, _count);
        }

        private void AllocateStorage(string name, PCGAttributeType type, int count)
        {
            switch (type)
            {
            case PCGAttributeType.Float:
                (_floatArrays ??= new Dictionary<string, float[]>(StringComparer.Ordinal))[name] = new float[count];
                break;
            case PCGAttributeType.Int:
                (_intArrays ??= new Dictionary<string, int[]>(StringComparer.Ordinal))[name] = new int[count];
                break;
            case PCGAttributeType.Bool:
                (_boolArrays ??= new Dictionary<string, bool[]>(StringComparer.Ordinal))[name] = new bool[count];
                break;
            case PCGAttributeType.Vector2:
                (_vector2Arrays ??= new Dictionary<string, Vector2[]>(StringComparer.Ordinal))[name] = new Vector2[count];
                break;
            case PCGAttributeType.Vector3:
                (_vector3Arrays ??= new Dictionary<string, Vector3[]>(StringComparer.Ordinal))[name] = new Vector3[count];
                break;
            case PCGAttributeType.Vector4:
                (_vector4Arrays ??= new Dictionary<string, Vector4[]>(StringComparer.Ordinal))[name] = new Vector4[count];
                break;
            case PCGAttributeType.Quaternion:
                (_quaternionArrays ??= new Dictionary<string, Quaternion[]>(StringComparer.Ordinal))[name] = new Quaternion[count];
                break;
            case PCGAttributeType.String:
                (_stringArrays ??= new Dictionary<string, string[]>(StringComparer.Ordinal))[name] = new string[count];
                break;
            default:
                throw new PCGAttributeException(PCGAttributeError.UnsupportedType, $"Unsupported attribute type {type}.");
            }
        }

        /// <summary>Resizes all columns to <paramref name="count"/>, preserving existing values.</summary>
        public void Resize(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            Detach();

            if (count == _count)
                return;

            if (_descriptors != null)
            {
                // Each column is read out, resized, and written back: a dictionary indexer is not a
                // variable, so it cannot be passed by ref directly.
                foreach (var kv in _descriptors)
                {
                    var name = kv.Key;
                    switch (kv.Value.Type)
                    {
                    case PCGAttributeType.Float:
                    {
                        var a = _floatArrays[name];
                        Array.Resize(ref a, count);
                        _floatArrays[name] = a;
                        break;
                    }
                    case PCGAttributeType.Int:
                    {
                        var a = _intArrays[name];
                        Array.Resize(ref a, count);
                        _intArrays[name] = a;
                        break;
                    }
                    case PCGAttributeType.Bool:
                    {
                        var a = _boolArrays[name];
                        Array.Resize(ref a, count);
                        _boolArrays[name] = a;
                        break;
                    }
                    case PCGAttributeType.Vector2:
                    {
                        var a = _vector2Arrays[name];
                        Array.Resize(ref a, count);
                        _vector2Arrays[name] = a;
                        break;
                    }
                    case PCGAttributeType.Vector3:
                    {
                        var a = _vector3Arrays[name];
                        Array.Resize(ref a, count);
                        _vector3Arrays[name] = a;
                        break;
                    }
                    case PCGAttributeType.Vector4:
                    {
                        var a = _vector4Arrays[name];
                        Array.Resize(ref a, count);
                        _vector4Arrays[name] = a;
                        break;
                    }
                    case PCGAttributeType.Quaternion:
                    {
                        var a = _quaternionArrays[name];
                        Array.Resize(ref a, count);
                        _quaternionArrays[name] = a;
                        break;
                    }
                    case PCGAttributeType.String:
                    {
                        var a = _stringArrays[name];
                        Array.Resize(ref a, count);
                        _stringArrays[name] = a;
                        break;
                    }
                    }
                }
            }

            _count = count;
        }

        /// <summary>
        /// Copies the rows listed in <paramref name="indices"/> into a new, solely owned and compacted set,
        /// so that row <c>i</c> of the result corresponds to <c>indices[i]</c> of this set.
        /// </summary>
        public PCGAttributeSet Gather(int[] indices, int resultCount)
        {
            if (indices == null)
                throw new ArgumentNullException(nameof(indices));

            var result = new PCGAttributeSet(resultCount);

            if (_descriptors == null || _descriptors.Count == 0)
                return result;

            result._descriptors = new Dictionary<string, PCGAttributeDescriptor>(_descriptors, StringComparer.Ordinal);

            foreach (var kv in _descriptors)
            {
                var name = kv.Key;
                switch (kv.Value.Type)
                {
                case PCGAttributeType.Float:
                {
                    var src = _floatArrays[name];
                    var dst = new float[resultCount];
                    for (int i = 0; i < resultCount; i++) dst[i] = src[indices[i]];
                    (result._floatArrays ??= new Dictionary<string, float[]>(StringComparer.Ordinal))[name] = dst;
                    break;
                }
                case PCGAttributeType.Int:
                {
                    var src = _intArrays[name];
                    var dst = new int[resultCount];
                    for (int i = 0; i < resultCount; i++) dst[i] = src[indices[i]];
                    (result._intArrays ??= new Dictionary<string, int[]>(StringComparer.Ordinal))[name] = dst;
                    break;
                }
                case PCGAttributeType.Bool:
                {
                    var src = _boolArrays[name];
                    var dst = new bool[resultCount];
                    for (int i = 0; i < resultCount; i++) dst[i] = src[indices[i]];
                    (result._boolArrays ??= new Dictionary<string, bool[]>(StringComparer.Ordinal))[name] = dst;
                    break;
                }
                case PCGAttributeType.Vector2:
                {
                    var src = _vector2Arrays[name];
                    var dst = new Vector2[resultCount];
                    for (int i = 0; i < resultCount; i++) dst[i] = src[indices[i]];
                    (result._vector2Arrays ??= new Dictionary<string, Vector2[]>(StringComparer.Ordinal))[name] = dst;
                    break;
                }
                case PCGAttributeType.Vector3:
                {
                    var src = _vector3Arrays[name];
                    var dst = new Vector3[resultCount];
                    for (int i = 0; i < resultCount; i++) dst[i] = src[indices[i]];
                    (result._vector3Arrays ??= new Dictionary<string, Vector3[]>(StringComparer.Ordinal))[name] = dst;
                    break;
                }
                case PCGAttributeType.Vector4:
                {
                    var src = _vector4Arrays[name];
                    var dst = new Vector4[resultCount];
                    for (int i = 0; i < resultCount; i++) dst[i] = src[indices[i]];
                    (result._vector4Arrays ??= new Dictionary<string, Vector4[]>(StringComparer.Ordinal))[name] = dst;
                    break;
                }
                case PCGAttributeType.Quaternion:
                {
                    var src = _quaternionArrays[name];
                    var dst = new Quaternion[resultCount];
                    for (int i = 0; i < resultCount; i++) dst[i] = src[indices[i]];
                    (result._quaternionArrays ??= new Dictionary<string, Quaternion[]>(StringComparer.Ordinal))[name] = dst;
                    break;
                }
                case PCGAttributeType.String:
                {
                    var src = _stringArrays[name];
                    var dst = new string[resultCount];
                    for (int i = 0; i < resultCount; i++) dst[i] = src[indices[i]];
                    (result._stringArrays ??= new Dictionary<string, string[]>(StringComparer.Ordinal))[name] = dst;
                    break;
                }
                }
            }

            return result;
        }

        internal void SetCount(int count) => _count = count;

        private void ValidateWritableName(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new PCGAttributeException(PCGAttributeError.InvalidName, "Attribute name cannot be null or empty.");

            if (IsBuiltInName(name))
            {
                throw new PCGAttributeException(
                    PCGAttributeError.BuiltInReadOnly,
                    $"'{name}' is a built-in point channel stored in the point array, not in the attribute set. " +
                    "Write it through the point (for example PCGPoint.Density) instead.");
            }
        }

        private void ValidateIndex(string name, int index, PCGAttributeType type)
        {
            if (index < 0 || index >= _count)
            {
                throw new PCGAttributeException(
                    PCGAttributeError.IndexOutOfRange,
                    $"Index {index} is out of range for attribute '{name}' (count {_count}).");
            }

            if (!TryGetDescriptor(name, out var descriptor))
                throw new PCGAttributeException(PCGAttributeError.NotFound, $"Attribute '{name}' does not exist.");

            if (descriptor.Type != type)
            {
                throw new PCGAttributeException(
                    PCGAttributeError.TypeMismatch,
                    $"Attribute '{name}' is {descriptor.Type}, not {type}.");
            }
        }

        private void ValidateArrayName(string name, PCGAttributeType type)
        {
            if (IsBuiltInName(name))
            {
                throw new PCGAttributeException(
                    PCGAttributeError.BuiltInReadOnly,
                    $"'{name}' is a built-in point channel and cannot be accessed through the attribute set.");
            }

            if (!TryGetDescriptor(name, out var descriptor))
                throw new PCGAttributeException(PCGAttributeError.NotFound, $"Attribute '{name}' does not exist.");

            if (descriptor.Type != type)
            {
                throw new PCGAttributeException(
                    PCGAttributeError.TypeMismatch,
                    $"Attribute '{name}' is {descriptor.Type}, not {type}.");
            }
        }

        /// <summary>
        /// Write-side validation: rejects built-in channels first, then defers to the ordinary checks.
        /// </summary>
        /// <remarks>
        /// <c>ValidateIndex</c> alone is not enough on the write path because <c>TryGetDescriptor</c> also
        /// resolves the built-in channels, so <c>SetFloat("$Density", ...)</c> would pass validation and then
        /// index the custom float store: a silent no-op when that store happens to exist, and a raw
        /// NullReferenceException when it does not. Reading built-ins through the getters stays allowed, which
        /// is why this is a separate validator rather than a change to <c>ValidateIndex</c>.
        /// </remarks>
        private void ValidateWritableIndex(string name, int index, PCGAttributeType type)
        {
            ValidateWritableName(name);
            ValidateIndex(name, index, type);
        }

        // ---- Scalar accessors -------------------------------------------------

        /// <summary>Reads a float attribute.</summary>
        public float GetFloat(string name, int index)
        {
            ValidateIndex(name, index, PCGAttributeType.Float);
            return _floatArrays[name][index];
        }

        /// <summary>Writes a float attribute, detaching first when this storage is shared.</summary>
        public void SetFloat(string name, int index, float value)
        {
            ValidateWritableIndex(name, index, PCGAttributeType.Float);
            Detach();
            _floatArrays[name][index] = value;
        }

        /// <summary>Reads an int attribute.</summary>
        public int GetInt(string name, int index)
        {
            ValidateIndex(name, index, PCGAttributeType.Int);
            return _intArrays[name][index];
        }

        /// <summary>Writes an int attribute, detaching first when this storage is shared.</summary>
        public void SetInt(string name, int index, int value)
        {
            ValidateWritableIndex(name, index, PCGAttributeType.Int);
            Detach();
            _intArrays[name][index] = value;
        }

        /// <summary>Reads a bool attribute.</summary>
        public bool GetBool(string name, int index)
        {
            ValidateIndex(name, index, PCGAttributeType.Bool);
            return _boolArrays[name][index];
        }

        /// <summary>Writes a bool attribute, detaching first when this storage is shared.</summary>
        public void SetBool(string name, int index, bool value)
        {
            ValidateWritableIndex(name, index, PCGAttributeType.Bool);
            Detach();
            _boolArrays[name][index] = value;
        }

        /// <summary>Reads a Vector2 attribute.</summary>
        public Vector2 GetVector2(string name, int index)
        {
            ValidateIndex(name, index, PCGAttributeType.Vector2);
            return _vector2Arrays[name][index];
        }

        /// <summary>Writes a Vector2 attribute, detaching first when this storage is shared.</summary>
        public void SetVector2(string name, int index, Vector2 value)
        {
            ValidateWritableIndex(name, index, PCGAttributeType.Vector2);
            Detach();
            _vector2Arrays[name][index] = value;
        }

        /// <summary>Reads a Vector3 attribute.</summary>
        public Vector3 GetVector3(string name, int index)
        {
            ValidateIndex(name, index, PCGAttributeType.Vector3);
            return _vector3Arrays[name][index];
        }

        /// <summary>Writes a Vector3 attribute, detaching first when this storage is shared.</summary>
        public void SetVector3(string name, int index, Vector3 value)
        {
            ValidateWritableIndex(name, index, PCGAttributeType.Vector3);
            Detach();
            _vector3Arrays[name][index] = value;
        }

        /// <summary>Reads a Vector4 attribute.</summary>
        public Vector4 GetVector4(string name, int index)
        {
            ValidateIndex(name, index, PCGAttributeType.Vector4);
            return _vector4Arrays[name][index];
        }

        /// <summary>Writes a Vector4 attribute, detaching first when this storage is shared.</summary>
        public void SetVector4(string name, int index, Vector4 value)
        {
            ValidateWritableIndex(name, index, PCGAttributeType.Vector4);
            Detach();
            _vector4Arrays[name][index] = value;
        }

        /// <summary>Reads a Quaternion attribute.</summary>
        public Quaternion GetQuaternion(string name, int index)
        {
            ValidateIndex(name, index, PCGAttributeType.Quaternion);
            return _quaternionArrays[name][index];
        }

        /// <summary>Writes a Quaternion attribute, detaching first when this storage is shared.</summary>
        public void SetQuaternion(string name, int index, Quaternion value)
        {
            ValidateWritableIndex(name, index, PCGAttributeType.Quaternion);
            Detach();
            _quaternionArrays[name][index] = value;
        }

        /// <summary>Reads a string attribute.</summary>
        public string GetString(string name, int index)
        {
            ValidateIndex(name, index, PCGAttributeType.String);
            return _stringArrays[name][index];
        }

        /// <summary>Writes a string attribute, detaching first when this storage is shared.</summary>
        public void SetString(string name, int index, string value)
        {
            ValidateWritableIndex(name, index, PCGAttributeType.String);
            Detach();
            _stringArrays[name][index] = value;
        }

        // ---- Bulk accessors ---------------------------------------------------
        // Read-only columns are for readers. Writers must take the Writable variant, which detaches once
        // and then lets a node fill thousands of rows without a refcount check per row.

        /// <summary>Gets the live float column for reading. Do not write through this array on shared storage.</summary>
        public float[] GetFloatArray(string name)
        {
            ValidateArrayName(name, PCGAttributeType.Float);
            return _floatArrays[name];
        }

        /// <summary>Gets the live float column or null when the attribute is absent. Read-only.</summary>
        public float[] TryGetFloatArray(string name)
        {
            if (IsBuiltInName(name) || _descriptors == null ||
                !_descriptors.TryGetValue(name, out var d) || d.Type != PCGAttributeType.Float)
            {
                return null;
            }

            return _floatArrays[name];
        }

        /// <summary>Gets the live Vector3 column or null when the attribute is absent. Read-only.</summary>
        public Vector3[] TryGetVector3Array(string name)
        {
            if (IsBuiltInName(name) || _descriptors == null ||
                !_descriptors.TryGetValue(name, out var d) || d.Type != PCGAttributeType.Vector3)
            {
                return null;
            }

            return _vector3Arrays[name];
        }

        /// <summary>Gets an exclusive float column for writing, detaching first when this storage is shared.</summary>
        public float[] GetWritableFloatArray(string name)
        {
            ValidateArrayName(name, PCGAttributeType.Float);
            Detach();
            return _floatArrays[name];
        }

        /// <summary>Gets an exclusive int column for writing, detaching first when this storage is shared.</summary>
        public int[] GetWritableIntArray(string name)
        {
            ValidateArrayName(name, PCGAttributeType.Int);
            Detach();
            return _intArrays[name];
        }

        /// <summary>Gets an exclusive bool column for writing, detaching first when this storage is shared.</summary>
        public bool[] GetWritableBoolArray(string name)
        {
            ValidateArrayName(name, PCGAttributeType.Bool);
            Detach();
            return _boolArrays[name];
        }

        /// <summary>Gets an exclusive Vector2 column for writing, detaching first when this storage is shared.</summary>
        public Vector2[] GetWritableVector2Array(string name)
        {
            ValidateArrayName(name, PCGAttributeType.Vector2);
            Detach();
            return _vector2Arrays[name];
        }

        /// <summary>Gets an exclusive Vector3 column for writing, detaching first when this storage is shared.</summary>
        public Vector3[] GetWritableVector3Array(string name)
        {
            ValidateArrayName(name, PCGAttributeType.Vector3);
            Detach();
            return _vector3Arrays[name];
        }

        /// <summary>Gets an exclusive Vector4 column for writing, detaching first when this storage is shared.</summary>
        public Vector4[] GetWritableVector4Array(string name)
        {
            ValidateArrayName(name, PCGAttributeType.Vector4);
            Detach();
            return _vector4Arrays[name];
        }

        /// <summary>Gets an exclusive Quaternion column for writing, detaching first when this storage is shared.</summary>
        public Quaternion[] GetWritableQuaternionArray(string name)
        {
            ValidateArrayName(name, PCGAttributeType.Quaternion);
            Detach();
            return _quaternionArrays[name];
        }

        /// <summary>Gets an exclusive string column for writing, detaching first when this storage is shared.</summary>
        public string[] GetWritableStringArray(string name)
        {
            ValidateArrayName(name, PCGAttributeType.String);
            Detach();
            return _stringArrays[name];
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"PCGAttributeSet(Count={_count}, Custom={AttributeCount}, Refs={_refCount})";
        }
    }

    /// <summary>Failure reasons raised by <see cref="PCGAttributeException"/>.</summary>
    public enum PCGAttributeError
    {
        /// <summary>Attribute name was null or empty.</summary>
        InvalidName,
        /// <summary>Attribute does not exist in the set.</summary>
        NotFound,
        /// <summary>Attribute exists but holds a different type.</summary>
        TypeMismatch,
        /// <summary>Attempted to write a built-in channel through the attribute set.</summary>
        BuiltInReadOnly,
        /// <summary>Row index was outside the set's range.</summary>
        IndexOutOfRange,
        /// <summary>Attribute type is not supported by the storage.</summary>
        UnsupportedType,
    }

    /// <summary>
    /// Raised on attribute misuse. These indicate programming errors rather than data problems, so they are
    /// raised at the call site instead of being collected as graph compile diagnostics.
    /// </summary>
    public sealed class PCGAttributeException : Exception
    {
        /// <summary>Gets the failure reason.</summary>
        public PCGAttributeError Error { get; }

        /// <summary>Creates the exception.</summary>
        public PCGAttributeException(PCGAttributeError error, string message) : base(message)
        {
            Error = error;
        }
    }
}
