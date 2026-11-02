// PCG 插件 - 符号语法的可执行测试（docs/PCG_PLUGIN_DESIGN.md §5.1.31）。
//
// 符号语法是重写系统，所以真正值得测的失败是图上看不出来的那些：一条产生式的
// 子节点沿切线整体错位（宽度对、种类错）、一个因名字拼错而悄悄变成叶子的
// 符号、以及不会收敛的递归。正是“种类”这一列才让前两者
// 看得见，所以它和几何一样要仔细查。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG;
using PCG.Execution;
using PCG.Nodes;

using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class SymbolGrammarTests
    {
        internal static void Run(CheckDelegate check)
        {
            ParserTests(check);
            ExpansionTests(check);
            FailureTests(check);
        }

        private static void ParserTests(CheckDelegate check)
        {
            bool ok = PCGGrammarNode.TryParseProductions(
                "Lot = Shop,Shop: X:0.5,0.5; Shop = Room,Back: Z:0.75,0.25", out var grammar, out string error);

            check("a two-production grammar parses", ok && grammar != null, error);
            int lot = -1;
            int shop = -1;
            int room = -1;

            if (ok)
            {
                grammar.TryFindSymbol("Lot", out lot);
                grammar.TryFindSymbol("Shop", out shop);
                grammar.TryFindSymbol("Room", out room);
            }

            check("symbols are numbered by first appearance, left sides first",
                ok && grammar.SymbolCount == 4 && lot == 0 && shop == 1 && room == 2,
                ok ? $"{grammar.SymbolCount} symbol(s): Lot={lot} Shop={shop} Room={room}" : error);

            var lotRule = default(PCGGrammarNode.Production);
            bool hasLot = ok && grammar.TryGet(lot, out lotRule);

            check("a production carries both the cut and the child kinds",
                hasLot && lotRule.Axis == 0 && lotRule.Slices.Length == 2 && lotRule.Children.Length == 2 &&
                lotRule.Children[0] == shop && lotRule.Children[1] == shop,
                hasLot ? $"{lotRule.Children.Length} child(ren)" : error);

            // 没有产生式的符号就是叶子，这正是语法能终止的全部原因。
            var roomRule = default(PCGGrammarNode.Production);

            check("a symbol with no production is a leaf rather than an error",
                ok && !grammar.TryGet(room, out roomRule) && grammar.NameOf(room) == "Room",
                ok ? grammar.NameOf(room) : error);

            // 子节点可以引用一个产生式在文本里更靠后的符号——第二遍解析时解决，
            // 所以列表顺序不是作者必须遵守的约束。
            ok = PCGGrammarNode.TryParseProductions("A = B: X:1; B = C,C: Z:0.5,0.5", out var forward, out string forwardError);

            int b = -1;
            var bRule = default(PCGGrammarNode.Production);
            bool forwardOk = ok && forward.TryFindSymbol("B", out b) && forward.TryGet(b, out bRule);

            check("a child may be defined after it is used",
                forwardOk && bRule.Children.Length == 2, forwardError);

            // 递归是特性不是错误：任意深度的层级就是这么写出来的。
            ok = PCGGrammarNode.TryParseProductions("Room = Room,Room: X:0.5,0.5", out var recursive, out string recursiveError);

            int self = -1;
            var selfRule = default(PCGGrammarNode.Production);
            bool selfOk = ok && recursive.TryFindSymbol("Room", out self) && recursive.TryGet(self, out selfRule);

            check("a self-referential production parses, because recursion is how depth is written",
                selfOk && selfRule.Children[0] == self && selfRule.Children[1] == self, recursiveError);

            check("an empty list is refused",
                !PCGGrammarNode.TryParseProductions("  ", out _, out string emptyError) && emptyError != null,
                emptyError);

            check("a production with no '=' is refused",
                !PCGGrammarNode.TryParseProductions("Lot Shop: X:1,1", out _, out string shapeError) && shapeError != null,
                shapeError);

            check("a production with no cut is refused rather than guessed",
                !PCGGrammarNode.TryParseProductions("Lot = Shop,Shop", out _, out string cutError) && cutError != null,
                cutError);

            check("a production with no children is refused",
                !PCGGrammarNode.TryParseProductions("Lot = : X:1,1", out _, out string childError) && childError != null,
                childError);

            // 否则就看不见的那种错位：切线是对的，种类却沿它整体挪了。
            check("a production whose children and slices disagree is refused",
                !PCGGrammarNode.TryParseProductions("Lot = Shop,Shop,Back: X:0.5,0.5", out _, out string countError) &&
                countError != null && countError.Contains("3 child") && countError.Contains("2 slice"),
                countError);

            check("a bad axis is refused with it named",
                !PCGGrammarNode.TryParseProductions("Lot = Shop: Q:1,1", out _, out string axisError) &&
                axisError != null && axisError.Contains("Q"),
                axisError);

            check("a bad ratio is refused",
                !PCGGrammarNode.TryParseProductions("Lot = Shop: X:0,1", out _, out string ratioError) && ratioError != null,
                ratioError);

            check("a symbol defined twice is refused",
                !PCGGrammarNode.TryParseProductions("Lot = A: X:0.5,0.5; Lot = B: X:0.5,0.5", out _,
                    out string twiceError) && twiceError != null,
                twiceError);
        }

        private static void ExpansionTests(CheckDelegate check)
        {
            var node = Make("Lot = Shop,Shop: X:0.5,0.5; Shop = Room,Back: Z:0.75,0.25", "Lot", 4, 0.0f);
            var result = node.Execute(Context(), new[] { Seeds(1) });

            check("the leaves of a two-level grammar are its four grand-children",
                result.Count == 4, $"{result.Count} leaf/leaves");

            // 几何：地块沿 X 切一半，每半再沿 Z 按 75/25 切。子节点必须仍然铺满整块地。
            check("the cut follows each production's own axis and shares",
                Near(result.Points[0].Position.X, -250.0f) && Near(result.Points[0].Position.Z, -125.0f) &&
                Near(result.Points[0].Scale.Z, 750.0f) &&
                Near(result.Points[1].Position.Z, 375.0f) && Near(result.Points[1].Scale.Z, 250.0f) &&
                Near(result.Points[2].Position.X, 250.0f),
                Describe(result));

            check("the leaves tile the seed cell with no gap",
                Near(TileVolume(result), 1000.0f * 200.0f * 1000.0f), TileVolume(result).ToString("F0"));

            // “种类”列：同一深度上有两种时，“这是哪一种”正是符号的意义所在。
            var attributes = result.SharedAttributes;
            var symbols = attributes?.TryGetFloatArray(PCGGrammarNode.SymbolColumn);

            check("each leaf says which symbol it ended as",
                symbols != null && symbols.Length == 4 && Near(symbols[0], symbols[2]) && !Near(symbols[0], symbols[1]),
                symbols == null ? "no column" : string.Join(",", symbols));

            // 逐个元素读：字符串列写的时候是数组、读的时候按下标，
            // 而且走读取器还顺带证明这一列真的带文字、不是空串。
            string[] names = null;

            if (attributes != null && attributes.Contains(PCGGrammarNode.SymbolNameColumn))
            {
                names = new string[4];

                for (int i = 0; i < 4; i++)
                    names[i] = attributes.GetString(PCGGrammarNode.SymbolNameColumn, i);
            }

            check("the name column spells those symbols out",
                names != null && names[0] == "Room" && names[1] == "Back" &&
                names[2] == "Room" && names[3] == "Back",
                names == null ? "no column" : string.Join(",", names));

            // 列检查的对照：关掉名字必须**移除**那一列，而不是留一列空的。
            // 要两片叶子而不是一片：单片叶子的夹具即使节点把输入原样透传、
            // 什么都没产出，也照样满足“名字列不存在”。
            var silent = Make("Lot = Shop,Shop: X:0.5,0.5", "Lot", 4, 0.0f);
            silent.WriteNames = false;

            var silentResult = silent.Execute(Context(), new[] { Seeds(1) });
            var silentAttributes = silentResult.SharedAttributes;

            check("with names off the ordinal column is still written and the name column is not",
                silentResult.Count == 2 &&
                silentAttributes != null &&
                !silentAttributes.Contains(PCGGrammarNode.SymbolNameColumn) &&
                silentAttributes.Contains(PCGGrammarNode.SymbolColumn),
                silentAttributes == null
                    ? "no storage"
                    : $"ordinal={silentAttributes.Contains(PCGGrammarNode.SymbolColumn)} " +
                      $"name={silentAttributes.Contains(PCGGrammarNode.SymbolNameColumn)} writeNames={silent.WriteNames}");

            // 递归是撞到深度上限才停的，而不是因为产生式用光了。
            var recursive = Make("Root = Root,Root: X:0.5,0.5", "Root", 3, 0.0f);
            var recursiveResult = recursive.Execute(Context(), new[] { Seeds(1) });

            check("a recursive production terminates on the depth limit",
                recursiveResult.Count == 8 && string.IsNullOrEmpty(recursive.LastError),
                $"{recursiveResult.Count} leaf/leaves: {recursive.LastError}");

            check("each recursion level halves the cell",
                Near(recursiveResult.Points[0].Scale.X, 125.0f), recursiveResult.Points[0].Scale.X.ToString("F1"));

            // 永远不会被重写的种类就是每个种子一片叶子：
            // 为种子自己的符号不写产生式，是“它本身就是成品”的合法写法。
            var terminal = Make("Lot = Room: X:1", "Lot", 4, 0.0f);
            var terminalResult = terminal.Execute(Context(), new[] { Seeds(1) });

            check("the root symbol itself can be a leaf",
                terminalResult.Count == 1, terminalResult.Count.ToString());

            // 最小格尺寸拦住了那种否则会削出碎片的递归。
            var floored = Make("Root = Root,Root: X:0.5,0.5", "Root", 32, 400.0f);
            var flooredResult = floored.Execute(Context(), new[] { Seeds(1) });

            // 1000 切一半是 500，再切是 250——低于下限——所以是四片叶子，而不是
            // 只靠深度上限会放出的 2^32 片。
            check("a minimum cell size stops the recursion early",
                flooredResult.Count == 4 && string.IsNullOrEmpty(floored.LastError),
                $"{flooredResult.Count} leaf/leaves");

            // 确定性，以及同一张表在多个种子上的表现。
            var many = Make("Lot = Shop,Shop: X:0.5,0.5; Shop = Room,Room,Back: Z:0.35,0.35,0.3", "Lot", 4, 0.0f);
            var manyResult = many.Execute(Context(), new[] { Seeds(3) });

            check("three seeds produce three grammars",
                manyResult.Count == 18, manyResult.Count.ToString());

            // 每片叶子自己的种子必须不同，否则下游的变化处处都一样。
            var seeds = new HashSet<int>();

            for (int i = 0; i < manyResult.Count; i++)
                seeds.Add(manyResult.Points[i].Seed);

            check("every leaf carries its own seed", seeds.Count == manyResult.Count, $"{seeds.Count} distinct");
        }

        private static void FailureTests(CheckDelegate check)
        {
            var seeds = Seeds(2);

            // 表里没有的根几乎总是打错了字，而另一种处理（每个种子一片叶子）
            // 会看起来像语法什么都没做，而不是像名字缺失。
            var missingRoot = Make("Lot = Shop: X:1", "Plot", 4, 0.0f);
            var passed = missingRoot.Execute(Context(), new[] { seeds });

            check("a root symbol that is not in the productions reports why and passes the input through",
                passed.Count == 2 && !string.IsNullOrEmpty(missingRoot.LastError) &&
                missingRoot.LastError.Contains("Plot") && missingRoot.LastError.Contains("Lot"),
                missingRoot.LastError);

            var broken = Make("Lot = Shop,Nope: X:0.5,0.5,0.5", "Lot", 4, 0.0f);
            var brokenResult = broken.Execute(Context(), new[] { seeds });

            check("a production that cannot be read passes the layer through instead of emptying it",
                brokenResult.Count == 2 && !string.IsNullOrEmpty(broken.LastError),
                broken.LastError);

            // 预算：四叉递归三十层不是结构，真让它跑会把
            // 编辑器挂住。
            var hungry = Make("Root = A,A,A,A: X:0.25,0.25,0.25,0.25; A = Root: X:1,1", "Root",
                PCGGrammarNode.MaxDepthLimit, 0.0f);

            // 上面那条产生式因为“点了 1 个子节点、切了 2 片”被拒——这件事本身值得钉住：
            var refused = hungry.Execute(Context(), new[] { Seeds(1) });

            check("a production whose children and slices disagree is refused at run time too",
                refused.Count == 1 && !string.IsNullOrEmpty(hungry.LastError),
                hungry.LastError);

            var expanding = Make(
                "Root = A,A,A,A: X:0.25,0.25,0.25,0.25; A = Root,Root,Root,Root: X:0.25,0.25,0.25,0.25",
                "Root", PCGGrammarNode.MaxDepthLimit, 0.0f);

            var bounded = expanding.Execute(Context(), new[] { Seeds(1) });

            check("a grammar that would explode stops at the budget and says so",
                bounded.Count == PCGGrammarNode.LeafBudget &&
                !string.IsNullOrEmpty(expanding.LastError) && expanding.LastError.Contains("budget"),
                $"{bounded.Count} cell(s): {expanding.LastError}");

            // 让预算检查保持诚实的对照：同一段递归被深度拦住时，不算被截断。
            var deep = Make("Root = Root,Root: X:0.5,0.5", "Root", 16, 0.0f);
            var deepResult = deep.Execute(Context(), new[] { Seeds(1) });

            check("a deep but affordable grammar is not truncated",
                deepResult.Count == 1 << 16 && string.IsNullOrEmpty(deep.LastError),
                $"{deepResult.Count} cell(s): {deep.LastError}");
        }

        // ---- 夹具 -------------------------------------------------------

        private static PCGGrammarNode Make(string productions, string root, int maxDepth, float minCell)
        {
            var node = new PCGGrammarNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCGGrammarNode.Id,
                new object[] { productions, root, maxDepth, minCell, new Vector3(1000.0f, 200.0f, 1000.0f), false, true },
                0));

            return node;
        }

        private static PCGExecutionContext Context() =>
            new PCGExecutionContext
            {
                Seed = 20261008,
                GenerationBounds = new BoundingBox(new Vector3(-2000.0f, -100.0f, -2000.0f),
                                                   new Vector3(-500.0f, 100.0f, -2000.0f)),
            };

        private static PCGData Seeds(int count)
        {
            var data = PCGData.Create(count);

            for (int i = 0; i < count; i++)
            {
                data.Points[i] = PCGPoint.Create(new Vector3(i * 4000.0f, 0.0f, 0.0f), 1.0f, i);
                data.Points[i].Scale = Float3.One;
                data.Points[i].UpdateBounds(Vector3.One);
            }

            return data;
        }

        private static float TileVolume(PCGData data)
        {
            float total = 0.0f;

            for (int i = 0; i < data.Count; i++)
                total += data.Points[i].Scale.X * data.Points[i].Scale.Y * data.Points[i].Scale.Z;

            return total;
        }

        private static bool Near(float a, float b, float tolerance = 1e-3f) => Math.Abs(a - b) <= tolerance;

        private static string Describe(PCGData data)
        {
            var parts = new List<string>();

            for (int i = 0; i < Math.Min(data.Count, 6); i++)
            {
                var point = data.Points[i];
                parts.Add($"({point.Position.X:F0},{point.Position.Z:F0})");
            }

            return $"{data.Count}: {string.Join(" ", parts)}";
        }
    }
}
