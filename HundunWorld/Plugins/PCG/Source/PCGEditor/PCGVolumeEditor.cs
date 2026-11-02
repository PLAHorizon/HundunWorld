// PCG 插件 - 场景里那张 PCG 体积的属性面板：生成 / 清空
// 属于 FlaxEngine PCG 插件。

using FlaxEditor.CustomEditors;
using FlaxEditor.CustomEditors.Editors;
using FlaxEditor.CustomEditors.Elements;
using FlaxEngine;
using PCG.Runtime;

namespace PCGEditor
{
    /// <summary>
    /// 给场景里的 <see cref="PCGVolume"/> 加「生成」「清空」两个按钮，并把上一次的结果报出来。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 在这之前，把图**用到场景里**这一步在界面上是断的：`PCGVolume` 有 `Generate()`，但没有任何界面调它
    /// （它也不在 `OnUpdate` 里跑——`Actor` 不该按帧重算整张图），于是只有流式驱动和自检代码会调。设计者把体积
    /// 拖进场景、填好图名之后，没有任何地方可以点。这个面板补的就是这一步。
    /// </para>
    /// <para>
    /// 面板报的是**这一次**运行的结果：点数、Actor 数、耗时、告警条数。报出来而不是只让日志说，是因为
    /// "生成完了但场景里什么都没有"最常见的三个成因（图名写错、模型资产没指定、生成范围不覆盖地形）都会在这几个
    /// 数字里现形：点数是零说明图那一头就没出东西，点数有而 Actor 是零说明是生成器那一头。
    /// </para>
    /// </remarks>
    [CustomEditor(typeof(PCGVolume))]
    public sealed class PCGVolumeEditor : GenericEditor
    {
        private LabelElement _readout;

        /// <inheritdoc />
        public override void Initialize(LayoutElementsContainer layout)
        {
            // 先把**默认的那一面**画出来：`GraphPath`、`Volume`、`Seed` 这些字段的输入控件是它画的。
            // 少了这一步，这个面板就成了一张只有按钮和读数的只读卡片——图名根本没地方填
            // （这正是本轮反馈"无法填写图名"的原因：覆写 Initialize 等于把默认编辑器整个换掉）。
            base.Initialize(layout);

            var first = Values.Count > 0 ? Values[0] as PCGVolume : null;

            string graph = first == null || string.IsNullOrEmpty(first.GraphPath)
                ? "（还没填图名）"
                : first.GraphPath;

            layout.Label("图", graph,
                "仓里的图存在 Content/PCG/Graphs 下；写裸名字（例如 valley）即可，也接受完整路径。空着时这个体积什么都不做。");

            var generate = layout.Button("生成", "按当前的图与参数生成本体积的内容，并替换上一次的产出。");
            generate.Button.Clicked += GenerateClicked;

            var clear = layout.Button("清空", "销毁这个体积生成的全部 Actor（手工摆的物件不受影响）。");
            clear.Button.Clicked += ClearClicked;

            _readout = layout.Label("上一次", Describe(null, 0));

            ShowLastRun(first);
        }

        private void GenerateClicked()
        {
            int volumes = 0;
            int points = 0;
            int actors = 0;
            int warnings = 0;
            double milliseconds = 0.0;
            PCGGenerationResult last = null;

            for (int i = 0; i < Values.Count; i++)
            {
                if (!(Values[i] is PCGVolume volume))
                    continue;

                volume.Generate();
                volumes++;

                // 生成物是走运行期接口建出来的，编辑器不会因此把场景标脏——不标这一下，
                // 设计师看见的内容在他按下保存时会被**安静地丢掉**（重开场景就没了）。
                // 归属是结构性的（产出挂在这个体积之下），所以标脏要标的是它所在的那个场景。
                if (volume.Scene != null)
                    FlaxEditor.Editor.Instance?.Scene?.MarkSceneEdited(volume.Scene);

                var result = volume.LastResult;

                if (result == null)
                    continue;

                points += result.PointCount;
                actors += result.SpawnedActors?.Count ?? 0;
                warnings += result.Warnings?.Length ?? 0;
                milliseconds += result.Milliseconds;
                last = result;
            }

            SetReadout(Describe(last, volumes));
        }

        private void ClearClicked()
        {
            int cleared = 0;

            for (int i = 0; i < Values.Count; i++)
            {
                if (!(Values[i] is PCGVolume volume))
                    continue;

                cleared += volume.GeneratedActorCount;
                volume.Clear();
            }

            SetReadout($"已清空：{cleared} 个 Actor（重新点「生成」可以再造一遍）。");
        }

        private void ShowLastRun(PCGVolume volume)
        {
            var result = volume?.LastResult;

            if (result == null)
            {
                SetReadout(Describe(null, 0));
                return;
            }

            SetReadout(Describe(result, 1));
        }

        private void SetReadout(string text)
        {
            if (_readout?.Label != null)
                _readout.Label.Text = text ?? string.Empty;
        }

        /// <summary>把一次（或几次）运行的结果写成一行话。</summary>
        private static string Describe(PCGGenerationResult result, int volumes)
        {
            if (result == null)
            {
                return volumes > 0
                    ? "没有产出：检查图名写对了没、生成器指了模型没有、生成范围盖住地形没有。"
                    : "还没生成过。填好图名后点「生成」。";
            }

            string summary = $"{(volumes > 1 ? $"{volumes} 个体积，" : string.Empty)}" +
                             $"{result.PointCount} 点 / {result.SpawnedActors?.Count ?? 0} 个 Actor，" +
                             $"耗时 {result.Milliseconds:F1} ms";

            int warnings = result.Warnings?.Length ?? 0;

            if (warnings > 0)
                summary += $"；{warnings} 条告警（见日志）";

            if (result.PointCount == 0)
                summary += "。一个点都没出：先在图编辑器里「试跑」看看是图的问题还是范围的问题。";

            return summary;
        }
    }
}
