using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 怪物内容静态注册表的串行集合。
/// <para>
/// 原因：<c>MonsterContent</c> 的内容注册表是**进程级静态**（<c>Install(bundle)</c> /
/// <c>ResetToHardcoded()</c> 换的是全局那一份），而 <c>MonsterTemplateCatalog</c> 是它未装配时的
/// 兜底目录。装配一份只含少数模板的内容包，会让**同一时刻其他测试类**对目录的查询落到
/// 一份不含它们那只怪的内容上——症状是掉落/让分/等级一类的断言时灵时不灵：
/// 单跑永远绿，全量并行跑偶发红（实测：BOSS 击杀掷出空掉落）。
/// </para>
/// <para>
/// 以前这里只写了 <c>[Collection("monster-content")]</c> 而**没有对应的 CollectionDefinition**，
/// 于是它只把两个怪物测试类互相串行，对其它读目录的类完全不起作用。
/// 现在把"写内容"的类与"读目录/内容"的类一起挂进来。
/// </para>
/// </summary>
[CollectionDefinition(MonsterContentStaticCollection.Name, DisableParallelization = true)]
public class MonsterContentStaticCollection
{
    public const string Name = "monster-content";
}
