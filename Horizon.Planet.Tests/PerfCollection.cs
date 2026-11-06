using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>
/// 带<em>墙钟</em>断言的用例放这个集合：xunit 默认并行跑不同集合，而这类断言（"每帧 &lt; 5 ms"）
/// 在别的用例占满 CPU 时会假红——实测：单跑 3/3 绿，整包跑偶发红。
/// <para><c>DisableParallelization = true</c> 让它独自一段跑；注意光有
/// <c>[Collection("名")]</c> 而没有这里的定义，只串行化集合<em>内部</em>，与别的集合照样并行。</para>
/// </summary>
[CollectionDefinition("perf-serial", DisableParallelization = true)]
public class PerfSerialCollection
{
}
