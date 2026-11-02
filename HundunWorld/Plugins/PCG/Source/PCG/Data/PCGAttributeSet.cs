// PCG 插件 - 属性系统
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §3.2。

using System;
using System.Collections.Generic;
using FlaxEngine;

namespace PCG
{
    /// <summary><see cref="PCGAttributeSet"/> 支持的属性取值类型。</summary>
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
    /// 描述一个属性：它的存储名、取值类型，以及它是不是一个内建的点通道。
    /// </summary>
    [Serializable]
    public struct PCGAttributeDescriptor
    {
        /// <summary>存储名。内建的名字带一个前导 '$'（例如 <c>$Density</c>）。</summary>
        public string Name;

        /// <summary>取值类型。</summary>
        public PCGAttributeType Type;

        /// <summary>这个属性是内建的点通道（而不是自定义元数据）时为 true。</summary>
        public bool IsBuiltIn;

        /// <inheritdoc />
        public override string ToString() => $"{Name}:{Type}{(IsBuiltIn ? " (builtin)" : string.Empty)}";
    }

    /// <summary>
    /// 按列存储（SoA）的属性存储，带写时复制式的共享。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么要有它。</b><see cref="PCGPoint"/> 是结构体，但它按**引用**持有这个类，所以
    /// 复制一个点数组复制的是那个<i>引用</i>。于是若干个点（或者由 Merge/Split 节点产出的若干份
    /// 数据集）可能别名到同一份存储。没有保护的话，写一个点的属性
    /// 会悄悄改掉**所有**别名点——一个极难顺藤摸瓜的缺陷。
    /// </para>
    /// <para>
    /// <b>契约。</b>允许共享；不允许就地改共享的存储。每一个会写
    /// 的入口都调用 <see cref="Detach"/>，它在**第一次**写一份被共享的实例时
    /// 克隆背后的那些列。脱开之后，这个实例拥有崭新的数组，而其他持有者继续
    /// 读原来那些。Detach 是就地改 <c>this</c> 而不是返回一个替代品，
    /// 所以调用方不可能不小心把副本丢掉——在这里那个错误从构造上就不存在。
    /// </para>
    /// <para>
    /// <b>后果。</b><see cref="GetFloatArray"/> 之类的接口为了速度直接把**活列**交出去。它们
    /// 只给读者用。打算写行的节点必须用配套的 <c>GetWritable*</c>
    /// 访问器，那个会先脱开；经只读列去写会绕过契约，
    /// 把别名缺陷重新引回来。
    /// </para>
    /// <para>
    /// 内建通道分成两类。<c>$Position</c>、<c>$Rotation</c>、<c>$Scale</c>、<c>$Density</c>、
    /// <c>$Seed</c> 与 <c>$Bounds</c> 是**虚**的：它们的值住在点数组里，所以这个集合
    /// 只知道它们的名字与类型。经访问器去碰它们会抛
    /// <see cref="PCGAttributeError.BuiltInReadOnly"/>，并把调用者指回点的字段。
    /// <c>$Slope</c> 是例外——<see cref="PCGPoint"/> 没有坡度字段，所以它是真正
    /// 由列支撑的通道：采样器声明它并填它，过滤器读它。以后任何没有
    /// 对应 <see cref="PCGPoint"/> 字段的通道也必须加进 <see cref="IsStoredBuiltInName"/>，
    /// 否则声明它过不了校验，而写它的采样器会在运行期抛异常。
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class PCGAttributeSet
    {
        /// <summary>标记内建（非自定义）属性名的前导字符。</summary>
        public const char BuiltInPrefix = '$';

        /// <summary>内建：点的世界位置（Vector3）。</summary>
        public const string PositionName = "$Position";
        /// <summary>内建：点的旋转（Quaternion）。</summary>
        public const string RotationName = "$Rotation";
        /// <summary>内建：点的缩放（Vector3，由 Float3 拓宽而来）。</summary>
        public const string ScaleName = "$Scale";
        /// <summary>内建：点的密度（Float）。</summary>
        public const string DensityName = "$Density";
        /// <summary>内建：点的种子（Int）。</summary>
        public const string SeedName = "$Seed";
        /// <summary>内建：点的包围盒尺寸（Vector3，拓宽过的）。</summary>
        public const string BoundsName = "$Bounds";
        /// <summary>内建：缓存的表面坡度（度，Float）。由地形/样条采样器写入。</summary>
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

        /// <summary>创建一份按 <paramref name="count"/> 个点开好的空属性集。</summary>
        public PCGAttributeSet(int count)
        {
            _count = count < 0 ? 0 : count;
        }

        /// <summary>序列化器要求的无参构造。</summary>
        public PCGAttributeSet()
        {
        }

        /// <summary>取这份集合是按多少个点开的。</summary>
        public int Count => _count;

        /// <summary>取存了多少个自定义（非内建）属性。</summary>
        public int AttributeCount => _descriptors?.Count ?? 0;

        /// <summary>取当前有几个活跃持有者。供 <see cref="Detach"/> 使用。</summary>
        public int RefCount => _refCount;

        /// <summary>当不止一个持有者引用这份存储、且下一次写会把它脱开时为 true。</summary>
        public bool IsShared => _refCount > 1;

        /// <summary>枚举每一个属性描述符，自定义的在前、内建的在后。</summary>
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

        /// <summary>
        /// <summary>枚举由列支撑的属性，包含被存下来的内建 <c>$Slope</c>。</summary>
        /// </summary>
        /// <remarks>
        /// <c>$Slope</c> 是有意包含进来的：一份合并出来的数据集不能丢掉采样器
        /// 已经为它付过一次地形查询的坡度，所以这一列必须跟着自定义属性一起走。
        /// </remarks>
        public IEnumerable<PCGAttributeDescriptor> GetCustomDescriptors()
        {
            if (_descriptors == null)
                yield break;

            foreach (var kv in _descriptors)
                yield return kv.Value;
        }

        /// <summary>
        /// 当这份集合**真的**为 <paramref name="name"/> 持有一列时返回 true，于是调用方可以
        /// 在这里读它。虚的内建通道（<c>$Density</c> 之类）回答 false：它们的数据在
        /// 点上，这里给 <c>true</c> 会诱使读者向这份集合要它并不拥有的值。
        /// </summary>
        public bool Contains(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            return _descriptors != null && _descriptors.ContainsKey(name);
        }

        /// <summary>取任何属性（内建或自定义）的描述符。</summary>
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

        /// <summary>当 <paramref name="name"/> 表示一个内建通道时返回 true。</summary>
        public static bool IsBuiltInName(string name)
        {
            return !string.IsNullOrEmpty(name) && name[0] == BuiltInPrefix;
        }

        /// <summary>
        /// 对那些**在这里被存成真列**（而不是从点数组镜像过来）的内建通道返回 true。
        /// 今天只有 <see cref="SlopeName"/> 符合，因为 <see cref="PCGPoint"/>
        /// 没有坡度字段。
        /// </summary>
        public static bool IsStoredBuiltInName(string name)
        {
            return string.Equals(name, SlopeName, StringComparison.Ordinal);
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

        // ---- 写时复制 ---------------------------------------------------

        /// <summary>
        /// 克隆背后的那些列，把这份存储与其他持有者脱开。
        /// </summary>
        /// <remarks>
        /// 这是这个类型**唯一**的写屏障。它就地改 <c>this</c> 而不是返回一个
        /// 替代品，所以调用方不可能把克隆丢掉。很便宜（一次引用计数检查），而且
        /// 一旦这个实例被独占，再调它是幂等的。
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
        /// 登记一个额外的持有者。每一次调用都必须配一次 <see cref="Release"/>。
        /// 调用方共享存储只用于**读**；第一次写就会脱开。
        /// </summary>
        public PCGAttributeSet Share()
        {
            _refCount++;
            return this;
        }

        /// <summary>丢掉一个持有者。</summary>
        public void Release()
        {
            if (_refCount > 0)
                _refCount--;
        }

        /// <summary>创建一份按同样点数开好的深拷贝，且独占。</summary>
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
        /// 声明一个按**不同**行数开的自定义属性；被共享时先脱开。
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

            _descriptors.Add(name, new PCGAttributeDescriptor { Name = name, Type = type, IsBuiltIn = IsBuiltInName(name) });
            AllocateStorage(name, type, _count);
        }

        /// <summary>
        /// 声明一个自定义属性且**不脱开**。供构造一份已知不共享的集合时
        /// 独占使用（例如刚 <c>new PCGAttributeSet(n)</c> 之后），于是
        /// 采样器可以一口气声明许多属性而不必反复克隆。
        /// </summary>
        public void DeclareAttributeUnsafe(string name, PCGAttributeType type)
        {
            ValidateWritableName(name);

            _descriptors ??= new Dictionary<string, PCGAttributeDescriptor>(StringComparer.Ordinal);

            if (_descriptors.ContainsKey(name))
                return;

            _descriptors.Add(name, new PCGAttributeDescriptor { Name = name, Type = type, IsBuiltIn = IsBuiltInName(name) });
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

        /// <summary>把所有列改到 <paramref name="count"/> 行，保留已有的值。</summary>
        public void Resize(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            Detach();

            if (count == _count)
                return;

            if (_descriptors != null)
            {
                // 每一列都要读出来、改尺寸、再写回去：字典的索引器不是
                // 变量，所以没法直接按 ref 传进去。
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
        /// 把 <paramref name="indices"/> 里列出的行复制进一份崭新的、独占且压紧的集合，
        /// 于是结果里的第 <c>i</c> 行对应这份集合里的 <c>indices[i]</c> 行。
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

            // $Slope 是唯一一个**存在这里**而不是从点镜像过来的内建通道，所以
            // 声明它并填它是合法的。其他每一个以 $ 开头的名字都属于点结构体。
            if (IsBuiltInName(name) && !IsStoredBuiltInName(name))
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

            // 虚通道的**读侧**守卫：TryGetDescriptor 会按名字把它们解析出来，所以没有
            // 这道检查的话 GetFloat("$Density", i) 会过掉校验，然后去索引一个根本没有
            // 这个键的列存储——float 存储为空时是 NullReferenceException，否则是 KeyNotFound。
            if (IsBuiltInName(name) && !IsStoredBuiltInName(name))
            {
                throw new PCGAttributeException(
                    PCGAttributeError.BuiltInReadOnly,
                    $"'{name}' is a built-in point channel; read it from the PCGPoint field, not from the attribute set.");
            }

            if (_descriptors == null || !_descriptors.TryGetValue(name, out var descriptor))
                throw new PCGAttributeException(PCGAttributeError.NotFound, $"Attribute '{name}' is not stored in this set.");

            if (descriptor.Type != type)
            {
                throw new PCGAttributeException(
                    PCGAttributeError.TypeMismatch,
                    $"Attribute '{name}' is {descriptor.Type}, not {type}.");
            }
        }

        private void ValidateArrayName(string name, PCGAttributeType type)
        {
            if (IsBuiltInName(name) && !IsStoredBuiltInName(name))
            {
                throw new PCGAttributeException(
                    PCGAttributeError.BuiltInReadOnly,
                    $"'{name}' is a built-in point channel and cannot be accessed through the attribute set.");
            }

            if (_descriptors == null || !_descriptors.TryGetValue(name, out var descriptor))
                throw new PCGAttributeException(PCGAttributeError.NotFound, $"Attribute '{name}' is not stored in this set.");

            if (descriptor.Type != type)
            {
                throw new PCGAttributeException(
                    PCGAttributeError.TypeMismatch,
                    $"Attribute '{name}' is {descriptor.Type}, not {type}.");
            }
        }

        /// <summary>
        /// 写侧校验：先拒内建通道，再交给常规检查。
        /// </summary>
        /// <remarks>
        /// 光有 <c>ValidateIndex</c> 在写路径上不够，因为 <c>TryGetDescriptor</c> 也会
        /// 解析内建通道，于是 <c>SetFloat("$Density", ...)</c> 会过掉校验，然后
        /// 去索引自定义的 float 存储：那个存储恰好存在时是一次静默的空操作，不存在时是裸的
        /// NullReferenceException。经 getter 读虚通道会因为同样的理由被
        /// <c>ValidateIndex</c> 拒掉；<see cref="TryGetDescriptor"/> 仍然解析它们，于是
        /// 工具能统一地报出这份 schema。
        /// </remarks>
        private void ValidateWritableIndex(string name, int index, PCGAttributeType type)
        {
            ValidateWritableName(name);
            ValidateIndex(name, index, type);
        }

        // ---- 标量访问器 -------------------------------------------------

        /// <summary>读一个 float 属性。</summary>
        public float GetFloat(string name, int index)
        {
            ValidateIndex(name, index, PCGAttributeType.Float);
            return _floatArrays[name][index];
        }

        /// <summary>写一个 float 属性；这份存储被共享时先脱开。</summary>
        public void SetFloat(string name, int index, float value)
        {
            ValidateWritableIndex(name, index, PCGAttributeType.Float);
            Detach();
            _floatArrays[name][index] = value;
        }

        /// <summary>读一个 int 属性。</summary>
        public int GetInt(string name, int index)
        {
            ValidateIndex(name, index, PCGAttributeType.Int);
            return _intArrays[name][index];
        }

        /// <summary>写一个 int 属性；这份存储被共享时先脱开。</summary>
        public void SetInt(string name, int index, int value)
        {
            ValidateWritableIndex(name, index, PCGAttributeType.Int);
            Detach();
            _intArrays[name][index] = value;
        }

        /// <summary>读一个 bool 属性。</summary>
        public bool GetBool(string name, int index)
        {
            ValidateIndex(name, index, PCGAttributeType.Bool);
            return _boolArrays[name][index];
        }

        /// <summary>写一个 bool 属性；这份存储被共享时先脱开。</summary>
        public void SetBool(string name, int index, bool value)
        {
            ValidateWritableIndex(name, index, PCGAttributeType.Bool);
            Detach();
            _boolArrays[name][index] = value;
        }

        /// <summary>读一个 Vector2 属性。</summary>
        public Vector2 GetVector2(string name, int index)
        {
            ValidateIndex(name, index, PCGAttributeType.Vector2);
            return _vector2Arrays[name][index];
        }

        /// <summary>写一个 Vector2 属性；这份存储被共享时先脱开。</summary>
        public void SetVector2(string name, int index, Vector2 value)
        {
            ValidateWritableIndex(name, index, PCGAttributeType.Vector2);
            Detach();
            _vector2Arrays[name][index] = value;
        }

        /// <summary>读一个 Vector3 属性。</summary>
        public Vector3 GetVector3(string name, int index)
        {
            ValidateIndex(name, index, PCGAttributeType.Vector3);
            return _vector3Arrays[name][index];
        }

        /// <summary>写一个 Vector3 属性；这份存储被共享时先脱开。</summary>
        public void SetVector3(string name, int index, Vector3 value)
        {
            ValidateWritableIndex(name, index, PCGAttributeType.Vector3);
            Detach();
            _vector3Arrays[name][index] = value;
        }

        /// <summary>读一个 Vector4 属性。</summary>
        public Vector4 GetVector4(string name, int index)
        {
            ValidateIndex(name, index, PCGAttributeType.Vector4);
            return _vector4Arrays[name][index];
        }

        /// <summary>写一个 Vector4 属性；这份存储被共享时先脱开。</summary>
        public void SetVector4(string name, int index, Vector4 value)
        {
            ValidateWritableIndex(name, index, PCGAttributeType.Vector4);
            Detach();
            _vector4Arrays[name][index] = value;
        }

        /// <summary>读一个 Quaternion 属性。</summary>
        public Quaternion GetQuaternion(string name, int index)
        {
            ValidateIndex(name, index, PCGAttributeType.Quaternion);
            return _quaternionArrays[name][index];
        }

        /// <summary>写一个 Quaternion 属性；这份存储被共享时先脱开。</summary>
        public void SetQuaternion(string name, int index, Quaternion value)
        {
            ValidateWritableIndex(name, index, PCGAttributeType.Quaternion);
            Detach();
            _quaternionArrays[name][index] = value;
        }

        /// <summary>读一个 string 属性。</summary>
        public string GetString(string name, int index)
        {
            ValidateIndex(name, index, PCGAttributeType.String);
            return _stringArrays[name][index];
        }

        /// <summary>写一个 string 属性；这份存储被共享时先脱开。</summary>
        public void SetString(string name, int index, string value)
        {
            ValidateWritableIndex(name, index, PCGAttributeType.String);
            Detach();
            _stringArrays[name][index] = value;
        }

        // ---- 批量访问器 ---------------------------------------------------
        // 只读列是给读者用的。写方必须取 Writable 变体，它脱开一次
        // 之后就让节点填几千行而不必每行做一次引用计数检查。

        /// <summary>取活的 float 列用于读。共享存储上不要经这个数组去写。</summary>
        public float[] GetFloatArray(string name)
        {
            ValidateArrayName(name, PCGAttributeType.Float);
            return _floatArrays[name];
        }

        /// <summary>取活的 float 列；属性不存在时为 null。只读。</summary>
        public float[] TryGetFloatArray(string name)
        {
            if (_descriptors == null ||
                !_descriptors.TryGetValue(name, out var d) || d.Type != PCGAttributeType.Float ||
                _floatArrays == null || !_floatArrays.TryGetValue(name, out var column))
            {
                return null;
            }

            return column;
        }

        /// <summary>取活的 Vector3 列；属性不存在时为 null。只读。</summary>
        public Vector3[] TryGetVector3Array(string name)
        {
            if (_descriptors == null ||
                !_descriptors.TryGetValue(name, out var d) || d.Type != PCGAttributeType.Vector3 ||
                _vector3Arrays == null || !_vector3Arrays.TryGetValue(name, out var column))
            {
                return null;
            }

            return column;
        }

        /// <summary>取一份独占的 float 列用于写；这份存储被共享时先脱开。</summary>
        public float[] GetWritableFloatArray(string name)
        {
            ValidateArrayName(name, PCGAttributeType.Float);
            Detach();
            return _floatArrays[name];
        }

        /// <summary>取一份独占的 int 列用于写；这份存储被共享时先脱开。</summary>
        public int[] GetWritableIntArray(string name)
        {
            ValidateArrayName(name, PCGAttributeType.Int);
            Detach();
            return _intArrays[name];
        }

        /// <summary>取一份独占的 bool 列用于写；这份存储被共享时先脱开。</summary>
        public bool[] GetWritableBoolArray(string name)
        {
            ValidateArrayName(name, PCGAttributeType.Bool);
            Detach();
            return _boolArrays[name];
        }

        /// <summary>取一份独占的 Vector2 列用于写；这份存储被共享时先脱开。</summary>
        public Vector2[] GetWritableVector2Array(string name)
        {
            ValidateArrayName(name, PCGAttributeType.Vector2);
            Detach();
            return _vector2Arrays[name];
        }

        /// <summary>取一份独占的 Vector3 列用于写；这份存储被共享时先脱开。</summary>
        public Vector3[] GetWritableVector3Array(string name)
        {
            ValidateArrayName(name, PCGAttributeType.Vector3);
            Detach();
            return _vector3Arrays[name];
        }

        /// <summary>取一份独占的 Vector4 列用于写；这份存储被共享时先脱开。</summary>
        public Vector4[] GetWritableVector4Array(string name)
        {
            ValidateArrayName(name, PCGAttributeType.Vector4);
            Detach();
            return _vector4Arrays[name];
        }

        /// <summary>取一份独占的 Quaternion 列用于写；这份存储被共享时先脱开。</summary>
        public Quaternion[] GetWritableQuaternionArray(string name)
        {
            ValidateArrayName(name, PCGAttributeType.Quaternion);
            Detach();
            return _quaternionArrays[name];
        }

        /// <summary>取一份独占的 string 列用于写；这份存储被共享时先脱开。</summary>
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

    /// <summary><see cref="PCGAttributeException"/> 会抛出的失败原因。</summary>
    public enum PCGAttributeError
    {
        /// <summary>属性名是 null 或空。</summary>
        InvalidName,
        /// <summary>这份集合里没有这个属性。</summary>
        NotFound,
        /// <summary>属性存在，但装的是另一种类型。</summary>
        TypeMismatch,
        /// <summary>试图经属性集去写一个内建通道。</summary>
        BuiltInReadOnly,
        /// <summary>行下标超出了这份集合的范围。</summary>
        IndexOutOfRange,
        /// <summary>存储不支持这种属性类型。</summary>
        UnsupportedType,
    }

    /// <summary>
    /// 属性误用时抛出。它们表示的是**编程错误**而不是数据问题，所以在调用点直接
    /// 抛出，而不是收成图的编译诊断。
    /// </summary>
    public sealed class PCGAttributeException : Exception
    {
        /// <summary>取失败原因。</summary>
        public PCGAttributeError Error { get; }

        /// <summary>创建异常。</summary>
        public PCGAttributeException(PCGAttributeError error, string message) : base(message)
        {
            Error = error;
        }
    }
}
