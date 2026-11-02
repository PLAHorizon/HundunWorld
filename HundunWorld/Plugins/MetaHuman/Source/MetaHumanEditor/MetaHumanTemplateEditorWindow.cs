using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using FlaxEditor;
using FlaxEditor.Windows;
using FlaxEngine;
using FlaxEngine.GUI;
using MetaHuman;
using MetaHuman.Core;
using MetaHuman.Runtime;
using EditorComboBox = FlaxEditor.GUI.ComboBox;

namespace MetaHumanEditor
{
    /// <summary>
    /// 数字人模板编辑器。界面按「导入 → 核对/回填 → 另存 → 绑动画」四条工作流分区，
    /// 每区的输入与动作同行，按钮紧跟它作用的那个框。
    ///
    /// 三条贯穿全窗的口径：
    /// 1. 只读动作（自检、扫描、预览映射）与写盘动作分家，点错不毁东西；
    /// 2. 每个写盘动作在报告里写清「落到哪个文件」，没落盘的（回填、预览）当场说明；
    /// 3. 空输入不再去撞底层报错——先告诉用户缺哪一格、去哪儿填。
    /// </summary>
    public sealed class MetaHumanTemplateEditorWindow : EditorWindow
    {
        private const float RowHeight = 24.0f;
        private const float LabelWidth = 92.0f;
        private const float ButtonWidth = 92.0f;
        private const float Gap = 4.0f;
        private const float Padding = 10.0f;
        private const float MinFieldWidth = 150.0f;
        private const float MinReportHeight = 90.0f;

        private const string DefaultTemplateId = "metahuman.face_archetype";

        private sealed class UiRow
        {
            public Label Title;
            public Control Field;
            public readonly List<Button> Buttons = new List<Button>();
            public bool IsHeader;

            public float Height
            {
                get { return IsHeader ? RowHeight + 6.0f : RowHeight + 2.0f; }
            }
        }

        private readonly List<UiRow> _rows = new List<UiRow>();

        private EditorComboBox _templateCombo;
        private TextBox _outputIdBox;
        private TextBox _ueDirBox;
        private TextBox _destFolderBox;
        private TextBox _assetsBox;
        private TextBox _targetBox;
        private TextBox _sourceBox;
        private TextBox _animDirBox;
        private TextBox _reportBox;

        private string _lastLayoutSize = string.Empty;
        private bool _suppressComboEvents;
        private bool _hasUnsavedEdits;

        private MetaHumanTemplate _template;

        public MetaHumanTemplateEditorWindow(Editor editor) : base(editor, true, ScrollBars.None)
        {
            Title = "数字人模板编辑器";
            RestoreSettings();
            BuildGui();
            ApplyRestoredSettings();
            RefreshTemplateItems(_pendingTemplateId ?? DefaultTemplateId);
            LoadTemplate(CurrentTemplateId, false);
        }

        public static MetaHumanTemplateEditorWindow ShowFor(Editor editor)
        {
            if (editor == null)
            {
                return null;
            }

            MetaHumanTemplateEditorWindow window = new MetaHumanTemplateEditorWindow(editor);
            window.Show();
            return window;
        }

        #region 界面搭建

        private void BuildGui()
        {
            AddHeader("模板");
            _templateCombo = new EditorComboBox();
            AddRow("当前模板", _templateCombo, "盘上 Templates 目录里的模板 Id。列表在每次展开时重新扫描。",
                   MakeButton("重载", ReloadClicked, "丢弃内存里未另存的回填，从盘上重新读当前模板"));
            _templateCombo.SelectedIndexChanged += OnTemplateComboChanged;
            _templateCombo.PopupShowing += OnTemplateComboPopup;

            _outputIdBox = new TextBox();
            AddRow("输出模板 Id", _outputIdBox, "「另存模板」与「导入并生成模板」写到的 Id。留空这两个按钮会拒绝执行。",
                   MakeButton("另存模板", SaveTemplateClicked, "把当前模板（含未另存的回填）另存为该 Id"));

            AddHeader("① 从 UE 导出目录导入");
            _ueDirBox = new TextBox();
            AddRow("UE 导出目录", _ueDirBox, "MetaHuman 管线导出物的文件夹本身（里面直接放 FBX/OBJ、EXR/PNG 贴图、描述 JSON）",
                   MakeButton("浏览目录", BrowseUeDirClicked, "选一个目录，选完自动扫描一次"),
                   MakeButton("扫描预览", ScanUeClicked, "只数一遍有什么，不导入、不写任何文件"));

            _destFolderBox = new TextBox();
            AddRow("资产输出到", _destFolderBox, "导入的模型资产落到这个 Content 目录下（按源文件名命名）",
                   MakeButton("导入并生成模板", ImportUeClicked, "真正执行导入，并据导入结果产出一份新模板"));

            AddHeader("② 模板与资产核对");
            _assetsBox = new TextBox();
            AddRow("模型资产", _assetsBox, "一条或多条 Content 路径，用分号隔开。一个角色常由头/身/眼等多份网格组成，全填上核对才准。",
                   MakeButton("浏览资产", BrowseAssetsClicked, "从 Content 目录里挑一个或多个 .flax"),
                   MakeButton("核对绑定", ValidateClicked, "只对照不改：模板引用的 morph/骨骼在这批资产里有没有"),
                   MakeButton("回填 morph", BackfillClicked, "把资产里多出来的 blendshape 加成滑杆。只在内存里，要另存才落盘"));

            AddHeader("③ 骨骼动画绑定");
            _targetBox = new TextBox();
            AddRow("目标模型", _targetBox, "要能播这批动画的那份 SkinnedModel（数字人本体）",
                   MakeButton("浏览", BrowseTargetClicked, "从 Content 里挑目标模型资产"));

            _sourceBox = new TextBox();
            AddRow("来源骨架", _sourceBox, "动画原本绑的那份 SkinnedModel（如 ALS/UEFN 人偶）",
                   MakeButton("浏览", BrowseSourceClicked, "从 Content 里挑来源模型资产"));

            _animDirBox = new TextBox();
            AddRow("动画目录", _animDirBox, "可选。填了就在写入时给目录内每份动画补「动画 Id → 来源骨架」条目——引擎按动画资产 Id 查重定向，缺这步播放时不会命中。",
                   MakeButton("预览映射", PreviewRetargetClicked, "只算映射表并报告命中数，不写资产"),
                   MakeButton("写入并保存", BindAnimationClicked, "算完写回目标资产的 SkeletonRetargets 并保存"));

            AddHeader("报告");
            AddButtonRow(MakeButton("环境自检", EnvCheckClicked, "把窗口上每一格的现状一次摊开，不改任何文件"),
                         MakeButton("复制报告", CopyReportClicked, "把报告全文放进剪贴板，便于贴进工单"),
                         MakeButton("打开模板目录", OpenTemplateDirClicked, "在资源管理器里打开 Templates 根目录"),
                         MakeButton("使用手册", OpenManualClicked, "在资源管理器里打开 docs/metahuman，本窗口的说明在那里"));

            _reportBox = new TextBox { IsMultiline = true, IsReadOnly = true };
            _reportBox.Parent = this;
            _reportBox.Text = "先点「环境自检」。它会把模板根目录、当前模板、模型资产、UE 目录一次说清，"
                              + "再决定走哪条工作流。\n手册：" + ManualPath;
        }

        /// <summary>把上次关窗时的填写放回各格（控件要等 BuildGui 才存在，所以回填单独一步）。</summary>
        private void ApplyRestoredSettings()
        {
            _outputIdBox.Text = string.IsNullOrEmpty(_outputIdText) ? "metahuman.imported" : _outputIdText;
            _destFolderBox.Text = string.IsNullOrEmpty(_destText) ? "Content/MetaHuman" : _destText;
            _ueDirBox.Text = _ueDirText ?? string.Empty;
            _assetsBox.Text = _assetsText ?? string.Empty;
            _targetBox.Text = _targetText ?? string.Empty;
            _sourceBox.Text = _sourceText ?? string.Empty;
            _animDirBox.Text = _animDirText ?? string.Empty;
        }

        private Button MakeButton(string text, Action onClick, string tooltip)
        {
            Button button = new Button
            {
                Text = text,
                TooltipText = tooltip,
            };
            button.Clicked += onClick;
            return button;
        }

        private void AddHeader(string text)
        {
            Label label = new Label { Text = text, Parent = this };
            UiRow row = new UiRow { Title = label, IsHeader = true };
            _rows.Add(row);
        }

        private void AddRow(string labelText, Control field, string tooltip, params Button[] buttons)
        {
            Label label = new Label { Text = labelText, Parent = this };
            field.Parent = this;
            field.TooltipText = tooltip;

            UiRow row = new UiRow { Title = label, Field = field };
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].Parent = this;
                row.Buttons.Add(buttons[i]);
            }

            _rows.Add(row);
        }

        /// <summary>只有按钮的一行（报告区工具条）。</summary>
        private void AddButtonRow(params Button[] buttons)
        {
            UiRow row = new UiRow();
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].Parent = this;
                row.Buttons.Add(buttons[i]);
            }

            _rows.Add(row);
        }

        /// <summary>
        /// 构造期宽高为 0，控件必须等到有尺寸后显式摆位
        /// （Flax 的非 TopLeft 锚在构造期会按父尺寸 0 重算位置，控件会凭空不显示）。
        /// </summary>
        public override void OnUpdate()
        {
            base.OnUpdate();

            string size = Width + "x" + Height;
            if (size == _lastLayoutSize)
            {
                return;
            }

            _lastLayoutSize = size;
            LayoutControls();
        }

        private void LayoutControls()
        {
            float y = Padding;
            for (int i = 0; i < _rows.Count; i++)
            {
                UiRow row = _rows[i];
                if (row.IsHeader)
                {
                    row.Title.Bounds = new Rectangle(Padding, y, Mathf.Max(60.0f, Width - 2.0f * Padding), RowHeight);
                    y += row.Height;
                    continue;
                }

                float buttonsWidth = row.Buttons.Count * (ButtonWidth + Gap);
                float fieldX = Padding + LabelWidth;
                float fieldWidth = Mathf.Max(MinFieldWidth, Width - fieldX - buttonsWidth - 2.0f * Padding);

                if (row.Title != null)
                {
                    row.Title.Bounds = new Rectangle(Padding, y, LabelWidth - Gap, RowHeight);
                }

                if (row.Field != null)
                {
                    row.Field.Bounds = new Rectangle(fieldX, y, fieldWidth, RowHeight);
                }

                // 没有输入框的那一行（报告工具条）从左往右排，不跟着字段跑到底部右侧
                float x = row.Field == null ? Padding : fieldX + fieldWidth + Gap;
                for (int b = 0; b < row.Buttons.Count; b++)
                {
                    row.Buttons[b].Bounds = new Rectangle(x, y, ButtonWidth, RowHeight - 2.0f);
                    x += ButtonWidth + Gap;
                }

                y += row.Height;
            }

            float reportTop = y + Gap;
            _reportBox.Bounds = new Rectangle(Padding, reportTop,
                Mathf.Max(MinFieldWidth, Width - 2.0f * Padding),
                Mathf.Max(MinReportHeight, Height - reportTop - Padding));
        }

        #endregion

        #region 模板装载

        private void OnTemplateComboPopup(EditorComboBox combo)
        {
            RefreshTemplateItems(CurrentTemplateId);
        }

        private void OnTemplateComboChanged(EditorComboBox combo)
        {
            if (_suppressComboEvents)
            {
                return;
            }

            LoadTemplate(combo.SelectedItem, true);
        }

        private string CurrentTemplateId
        {
            get
            {
                string selected = _templateCombo != null ? _templateCombo.SelectedItem : null;
                if (!string.IsNullOrEmpty(selected))
                {
                    return selected.Trim();
                }

                return _template != null ? _template.TemplateId : string.Empty;
            }
        }

        private void RefreshTemplateItems(string preferId)
        {
            List<string> ids = MetaHumanTemplateStore.AvailableTemplateIds();
            if (ids.Count == 0)
            {
                // 盘上一个模板都没有时（比如模板根目录没解析出来），至少让内嵌表那两份可选，
                // 否则下拉框是空的，用户连「当前模板」都定不下来。
                ids.Add("metahuman.face_archetype");
                ids.Add("metahuman.body_base");
            }

            if (!string.IsNullOrEmpty(preferId) && !ids.Contains(preferId))
            {
                ids.Add(preferId);
            }

            _suppressComboEvents = true;
            _templateCombo.Items = ids;
            _templateCombo.SelectedItem = !string.IsNullOrEmpty(preferId) ? preferId : CurrentTemplateId;
            _suppressComboEvents = false;
        }

        private void ReloadClicked()
        {
            if (_hasUnsavedEdits)
            {
                AppendReport("注意：刚才的回填还没另存，重载会丢掉它。要留住就先点「另存模板」。");
                _hasUnsavedEdits = false;
            }

            LoadTemplate(CurrentTemplateId, true);
        }

        /// <summary>
        /// 保证内存里有一个可用模板。reload=false 时，只要手上这份就是请求的那份就不再读盘——
        /// 否则刚「回填 morph」还没另存，下一次核对会把回填静默冲掉。
        /// </summary>
        private bool EnsureTemplate(string templateId, bool reload)
        {
            if (!reload && _template != null && _template.TemplateId == templateId)
            {
                return true;
            }

            return LoadTemplate(templateId, false);
        }

        private bool LoadTemplate(string templateId, bool report)
        {
            if (string.IsNullOrEmpty(templateId))
            {
                SetReport("「当前模板」还没选。展开它挑一个，或点「重载」重新扫描。");
                return false;
            }

            MetaHumanTemplateStore.ClearCache();
            _template = MetaHumanTemplateStore.Get(templateId);
            if (_template == null)
            {
                SetReport("模板装载失败：" + templateId + "\n这个 Id 在模板根目录里没有对应文件（Error 日志里有候选路径清单）。");
                return false;
            }

            _hasUnsavedEdits = false;
            RefreshTemplateItems(templateId);

            if (report)
            {
                List<string> errors = _template.Validate();
                StringBuilder sb = new StringBuilder();
                sb.Append("已装载模板 ").Append(templateId)
                  .Append("：参数 ").Append(_template.Parameters.Count)
                  .Append("，morph 名录 ").Append(_template.MorphCatalog.Count)
                  .Append("，校验").Append(errors.Count == 0 ? "通过" : errors.Count + " 条错误");
                for (int i = 0; i < errors.Count && i < 6; i++)
                {
                    sb.AppendLine().Append("  ✗ ").Append(errors[i]);
                }

                SetReport(sb.ToString());
            }

            return true;
        }

        #endregion

        #region ① 导入

        private void BrowseUeDirClicked()
        {
            string picked;
            if (!TryPickFolder(_ueDirBox.Text, "选择 MetaHuman 导出目录", out picked))
            {
                return;
            }

            _ueDirBox.Text = picked;
            ScanUeClicked();
        }

        private void ScanUeClicked()
        {
            string directory = (_ueDirBox.Text ?? string.Empty).Trim();
            if (directory.Length == 0)
            {
                SetReport("「UE 导出目录」是空的。填上导出物文件夹，或点「浏览目录」选一个。");
                return;
            }

            MetaHumanUEImportPlan plan = MetaHumanUEImporter.Scan(directory, DestinationFolder());
            StringBuilder sb = new StringBuilder();
            sb.Append("扫描（未导入）").Append(directory).AppendLine();
            sb.Append("  模型 ").Append(plan.ModelFiles.Count)
              .Append("，贴图槽位 ").Append(plan.TexturesBySlot.Count)
              .Append("，描述 JSON ").Append(plan.DescriptionFiles.Count).AppendLine();
            sb.Append("  将输出到 ").Append(DestinationFolder()).Append("，模板 Id ").Append(OutputIdText());

            foreach (KeyValuePair<string, string> slot in plan.TexturesBySlot)
            {
                sb.AppendLine().Append("  · ").Append(slot.Key).Append(" ← ").Append(Path.GetFileName(slot.Value));
            }

            for (int i = 0; i < plan.Warnings.Count; i++)
            {
                sb.AppendLine().Append("  ! ").Append(plan.Warnings[i]);
            }

            if (plan.IsEmpty)
            {
                sb.AppendLine();
                sb.AppendLine("  没有可导入的模型 → 「导入并生成模板」这一步不会产出任何东西。");
            }

            SetReport(sb.ToString());
        }

        private void ImportUeClicked()
        {
            string directory = (_ueDirBox.Text ?? string.Empty).Trim();
            string outputId = OutputId();
            string missing = MissingInput(directory, "UE 导出目录", outputId, "输出模板 Id");
            if (missing != null)
            {
                SetReport(missing);
                return;
            }

            MetaHumanUEImportPlan plan = MetaHumanUEImporter.Scan(directory, DestinationFolder());
            if (plan.IsEmpty)
            {
                SetReport("目录里没有可导入的模型，未做任何改动。\n" + string.Join("\n", plan.Warnings.ToArray()));
                return;
            }

            int failed;
            List<string> imported = MetaHumanUEImporter.ImportModels(plan, out failed);
            if (imported.Count == 0)
            {
                SetReport("全部导入失败（" + plan.ModelFiles.Count + " 个候选），未生成模板。\n"
                    + string.Join("\n", plan.Warnings.ToArray()));
                return;
            }

            MetaHumanBindingReport report;
            string path = MetaHumanUEImporter.ComposeTemplateFromAssets(
                outputId,
                "导入自 " + directory,
                imported,
                out report);

            StringBuilder sb = new StringBuilder();
            sb.Append("导入成功 ").Append(imported.Count).Append(" 个，失败 ").Append(failed).Append(" 个").AppendLine();
            sb.Append("模板文件：").Append(path ?? "未产出（模板根目录没解析出来）").AppendLine();
            sb.Append(MetaHumanRigBinder.Format(outputId, report));
            if (plan.Warnings.Count > 0)
            {
                sb.AppendLine().Append("告警：").Append(string.Join(" / ", plan.Warnings.ToArray()));
            }

            SetReport(sb.ToString());
            AfterTemplateWritten(outputId);
        }

        private string DestinationFolder()
        {
            string folder = (_destFolderBox.Text ?? string.Empty).Trim();
            return folder.Length == 0 ? "Content/MetaHuman" : folder;
        }

        private string OutputId()
        {
            return (_outputIdBox.Text ?? string.Empty).Trim();
        }

        private string OutputIdText()
        {
            string id = OutputId();
            return id.Length == 0 ? "（未填，不会生成模板）" : id;
        }

        #endregion

        #region ② 核对与回填

        private SkinnedModel[] LoadModels(out string error)
        {
            error = null;
            List<string> paths = MetaHumanEditorDiagnostics.SplitPaths(_assetsBox.Text);
            if (paths.Count == 0)
            {
                error = "「模型资产」是空的。点「浏览资产」从 Content 里挑，或粘一条 Content/... 路径。";
                return new SkinnedModel[0];
            }

            List<SkinnedModel> models = new List<SkinnedModel>();
            for (int i = 0; i < paths.Count; i++)
            {
                SkinnedModel model = MetaHumanAssetUtil.LoadSkinnedModel(paths[i]);
                if (model == null)
                {
                    error = "装载失败：" + paths[i] + MetaHumanEditorDiagnostics.ResolveAssetFile(paths[i])
                            + "\n它必须是 SkinnedModel（蒙皮网格），贴图/材质/动画资产填进来没用。";
                    return new SkinnedModel[0];
                }

                models.Add(model);
            }

            return models.ToArray();
        }

        private void ValidateClicked()
        {
            string modelError;
            SkinnedModel[] models = LoadModels(out modelError);
            if (models.Length == 0)
            {
                SetReport(modelError);
                return;
            }

            if (!EnsureTemplate(CurrentTemplateId, false))
            {
                return;
            }

            MetaHumanBindingReport report = MetaHumanRigBinder.Bind(_template, models);
            StringBuilder sb = new StringBuilder();
            sb.Append(MetaHumanRigBinder.Format(_template.TemplateId, report)).AppendLine();
            sb.Append("参数总数 ").Append(_template.Parameters.Count)
              .Append("，morph 名录 ").Append(_template.MorphCatalog.Count).AppendLine();
            sb.Append("（核对只读，什么都没改）");
            if (report.MissingMorphs.Count > 0)
            {
                sb.AppendLine().Append("下一步：想让这些 morph 变成滑杆，点「回填 morph」，再点「另存模板」。");
            }

            SetReport(sb.ToString());
        }

        private void BackfillClicked()
        {
            string modelError;
            SkinnedModel[] models = LoadModels(out modelError);
            if (models.Length == 0)
            {
                SetReport(modelError);
                return;
            }

            if (!EnsureTemplate(CurrentTemplateId, false))
            {
                return;
            }

            int before = _template.Parameters.Count;
            int added = MetaHumanRigBinder.ImportMissingMorphs(_template, models);
            MetaHumanBindingReport report = MetaHumanRigBinder.Bind(_template, models);

            StringBuilder sb = new StringBuilder();
            sb.Append("回填新增 ").Append(added).Append(" 根 morph 滑杆（参数 ").Append(before)
              .Append(" → ").Append(_template.Parameters.Count).Append("），名录 ")
              .Append(_template.MorphCatalog.Count).Append(" 项。").AppendLine();
            sb.Append(MetaHumanRigBinder.Format(_template.TemplateId + "（回填后）", report)).AppendLine();
            sb.Append(added == 0
                ? "没有可回填的新 morph：这批资产的 blendshape 名录已经在模板里了。"
                : "回填只在内存里。点「另存模板」才会写盘；直接关窗就全丢。");

            // 第二次回填新增 0 根不代表第一次的回填已经落盘，脏标记只能置真不能置假
            _hasUnsavedEdits = _hasUnsavedEdits || added > 0;
            SetReport(sb.ToString());
        }

        private void SaveTemplateClicked()
        {
            string outputId = OutputId();
            if (outputId.Length == 0)
            {
                SetReport("「输出模板 Id」是空的，不知道要存成什么。填一个（如 metahuman.my_face）再点。");
                return;
            }

            if (_template == null && !LoadTemplate(CurrentTemplateId, false))
            {
                return;
            }

            List<string> errors = _template.Validate();
            if (errors.Count > 0)
            {
                SetReport("模板校验未通过，未保存：\n" + string.Join("\n", errors.ToArray()));
                return;
            }

            string root = MetaHumanTemplateStore.RootDirectory;
            if (string.IsNullOrEmpty(root))
            {
                SetReport("模板根目录没解析出来，无法保存。点「环境自检」看它到底找过哪些路径。");
                return;
            }

            string path = Path.Combine(root, "Template_" + outputId + ".json");
            bool overwrite = File.Exists(path);
            _template.TemplateId = outputId;
            _template.DisplayName = "派生自 " + CurrentTemplateId;
            _template.SaveToFile(path);
            _hasUnsavedEdits = false;

            StringBuilder sb = new StringBuilder();
            sb.Append(overwrite ? "已覆盖：" : "已保存：").Append(path).AppendLine();
            sb.Append("参数 ").Append(_template.Parameters.Count)
              .Append("，morph 名录 ").Append(_template.MorphCatalog.Count).AppendLine();
            sb.Append("提醒：编辑器窗口读的是这份 JSON，而游戏/PIE 读的是烘进程序集的那一份。");
            sb.Append("要让运行期生效，还得跑 python Tools/MetaHumanBakeTables.py 并重编 MetaHumanTarget。");

            SetReport(sb.ToString());
            RefreshTemplateItems(outputId);
        }

        private void AfterTemplateWritten(string templateId)
        {
            _hasUnsavedEdits = false;
            AppendReport("提醒：新模板只在盘上。要让游戏/PIE 读到，跑 python Tools/MetaHumanBakeTables.py 并重编 MetaHumanTarget。");
            LoadTemplate(templateId, false);
            RefreshTemplateItems(templateId);
        }

        #endregion

        #region ③ 骨骼动画绑定

        private SkinnedModel LoadSingle(string path, string which)
        {
            string trimmed = (path ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                AppendReport("「" + which + "」是空的，点它旁边的「浏览」从 Content 里挑一份 SkinnedModel。");
                return null;
            }

            SkinnedModel model = MetaHumanAssetUtil.LoadSkinnedModel(trimmed);
            if (model == null)
            {
                AppendReport("「" + which + "」装载失败：" + trimmed + MetaHumanEditorDiagnostics.ResolveAssetFile(trimmed));
            }

            return model;
        }

        private void PreviewRetargetClicked()
        {
            RetargetClicked(false);
        }

        private void BindAnimationClicked()
        {
            RetargetClicked(true);
        }

        private void RetargetClicked(bool apply)
        {
            SkinnedModel target = LoadSingle(_targetBox.Text, "目标模型");
            if (target == null)
            {
                return;
            }

            SkinnedModel source = LoadSingle(_sourceBox.Text, "来源骨架");
            if (source == null)
            {
                return;
            }

            if (_template == null && !LoadTemplate(CurrentTemplateId, false))
            {
                return;
            }

            MetaHumanAnimationBinder.Result result = new MetaHumanAnimationBinder.Result();
            bool ok;
            if (apply)
            {
                ok = MetaHumanAnimationBinder.Apply(target, source, _template, result);
            }
            else
            {
                // SkeletonRetarget 是结构体，预览只要算得出映射就算成功（它不返回 null）
                MetaHumanAnimationBinder.BuildEntry(target, source, _template, result);
                ok = true;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append(!apply ? "预览（未写盘）：" : (ok ? "已写入并保存：" : "未落盘（原因见下方备注）：")).AppendLine();
            if (ok && apply)
            {
                int animationEntries = AppendAnimationEntries(target, source, result);
                if (animationEntries > 0)
                {
                    sb.Append("另为 ").Append(animationEntries).Append(" 份动画补了动画源条目。").AppendLine();
                }
            }

            sb.Append(MetaHumanAnimationBinder.Format(
                MetaHumanAssetUtil.DisplayName(target), MetaHumanAssetUtil.DisplayName(source), result));
            if (!apply)
            {
                sb.AppendLine().Append("确认命中数没问题，再点「写入并保存」。");
            }

            SetReport(sb.ToString());
        }

        /// <summary>
        /// 给动画目录里的每份动画补「动画 Id → 来源骨架」条目。
        /// 引擎是按动画资产 Id 查 SkeletonRetargets 的，只写骨架模型 Id 的条目播放时永远命中不了。
        /// 返回补了多少条（0 表示没填目录，负数表示落盘失败）。
        /// </summary>
        private int AppendAnimationEntries(SkinnedModel target, SkinnedModel source, MetaHumanAnimationBinder.Result result)
        {
            string directory = (_animDirBox.Text ?? string.Empty).Trim();
            if (directory.Length == 0)
            {
                return 0;
            }

            string full = Path.Combine(Globals.ProjectFolder ?? string.Empty,
                directory.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar));
            if (!Directory.Exists(full))
            {
                result.Notes.Add("动画目录不存在，未补动画条目：" + directory);
                return -1;
            }

            List<Animation> animations = new List<Animation>();
            string[] files = Directory.GetFiles(full, "*.flax", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string relative = MetaHumanEditorDiagnostics.ToAssetPath(files[i]);
                Animation animation = Content.Load<Animation>(relative);
                if (animation != null)
                {
                    animations.Add(animation);
                }
                else
                {
                    result.Notes.Add("动画装载失败（可能不是 Animation 资产）：" + relative);
                }
            }

            if (animations.Count == 0)
            {
                result.Notes.Add("目录里 " + files.Length + " 个 .flax 没有一份是动画，未补条目");
                return 0;
            }

            int added = MetaHumanAnimationBinder.ApplyAnimations(target, source, animations.ToArray(), result);
            return added;
        }

        #endregion

        #region 浏览与报告工具

        private void BrowseAssetsClicked()
        {
            List<string> picked;
            if (!TryPickAssets("选择模型资产（可多选）", out picked))
            {
                return;
            }

            _assetsBox.Text = string.Join("; ", picked.ToArray());
        }

        private void BrowseTargetClicked()
        {
            List<string> picked;
            if (TryPickAssets("选择目标模型", out picked) && picked.Count > 0)
            {
                _targetBox.Text = picked[0];
            }
        }

        private void BrowseSourceClicked()
        {
            List<string> picked;
            if (TryPickAssets("选择来源骨架模型", out picked) && picked.Count > 0)
            {
                _sourceBox.Text = picked[0];
            }
        }

        private static bool TryPickAssets(string title, out List<string> assetPaths)
        {
            assetPaths = new List<string>();
            string[] files;
            // 这两个对话框的返回值都是 true=取消（WindowsFileSystem 里 result 先置 true，
            // 拿到结果才改 false；引擎自己的调用点也写成 if (Show...()) return;）。
            // 按直觉判成"真就往下走"，症状就是选完文件路径框纹丝不动。
            if (FileSystem.ShowOpenFileDialog(OwnerWindow, null, "Flax 资产 (*.flax)\0*.flax\0所有文件 (*.*)\0*.*\0", true, title, out files))
            {
                return false;
            }

            for (int i = 0; i < files.Length; i++)
            {
                assetPaths.Add(MetaHumanEditorDiagnostics.ToAssetPath(files[i]));
            }

            return assetPaths.Count > 0;
        }

        private static bool TryPickFolder(string initial, string title, out string folder)
        {
            folder = null;
            string start = string.IsNullOrEmpty(initial) ? Globals.ProjectFolder : initial;
            string picked;
            if (FileSystem.ShowBrowseFolderDialog(OwnerWindow, start, title, out picked))
            {
                return false;
            }

            folder = picked;
            return !string.IsNullOrEmpty(picked);
        }

        /// <summary>系统对话框要挂在编辑器主窗上，否则模态行为会飘。</summary>
        private static FlaxEngine.Window OwnerWindow
        {
            get { return Editor.Instance != null ? Editor.Instance.Windows.MainWindow : null; }
        }

        private void EnvCheckClicked()
        {
            MetaHumanEditorDiagnostics.Inputs inputs = new MetaHumanEditorDiagnostics.Inputs
            {
                TemplateId = CurrentTemplateId,
                OutputTemplateId = OutputId(),
                ModelPaths = _assetsBox.Text,
                UeDirectory = _ueDirBox.Text,
                DestinationFolder = DestinationFolder(),
            };

            SetReport(MetaHumanEditorDiagnostics.Check(inputs));
        }

        private void CopyReportClicked()
        {
            Clipboard.Text = _reportBox.Text ?? string.Empty;
            AppendReport("（报告全文已复制）");
        }

        private void OpenTemplateDirClicked()
        {
            string root = MetaHumanTemplateStore.RootDirectory;
            if (string.IsNullOrEmpty(root))
            {
                SetReport("模板根目录没解析出来，没有可打开的目录。");
                return;
            }

            FileSystem.ShowFileExplorer(root);
        }

        /// <summary>手册在仓库根的 docs/metahuman 下，而工程根是它的下一级目录，所以要从工程根往上走一层。</summary>
        private static string ManualPath
        {
            get
            {
                string project = Globals.ProjectFolder;
                if (string.IsNullOrEmpty(project))
                {
                    return "docs/metahuman/模板编辑器_使用手册.md";
                }

                return Path.GetFullPath(Path.Combine(project, "..", "docs", "metahuman", "模板编辑器_使用手册.md"));
            }
        }

        private void OpenManualClicked()
        {
            string manual = ManualPath;
            if (!File.Exists(manual))
            {
                SetReport("没找到手册文件：" + manual);
                return;
            }

            FileSystem.ShowFileExplorer(Path.GetDirectoryName(manual));
            SetReport("手册在：" + manual);
        }

        private void SetReport(string text)
        {
            _reportBox.Text = text ?? string.Empty;
            SaveSettings();
        }

        private void AppendReport(string text)
        {
            if (string.IsNullOrEmpty(_reportBox.Text))
            {
                _reportBox.Text = text ?? string.Empty;
            }
            else
            {
                _reportBox.Text = _reportBox.Text + "\n" + text;
            }
        }

        private static string MissingInput(string a, string aName, string b, string bName)
        {
            if (string.IsNullOrEmpty(a))
            {
                return "「" + aName + "」是空的。" + (aName == "UE 导出目录"
                    ? "点它旁边的「浏览目录」选 MetaHuman 导出物文件夹。"
                    : "填上再点。");
            }

            if (string.IsNullOrEmpty(b))
            {
                return "「" + bName + "」是空的，不知道要产出什么 Id。在顶部那一格填一个。";
            }

            return null;
        }

        #endregion

        #region 输入记忆

        private sealed class WindowSettings
        {
            public string TemplateId { get; set; }
            public string OutputTemplateId { get; set; }
            public string UeDirectory { get; set; }
            public string DestinationFolder { get; set; }
            public string ModelPaths { get; set; }
            public string TargetModel { get; set; }
            public string SourceModel { get; set; }
            public string AnimationsDir { get; set; }
        }

        private static string SettingsPath
        {
            get
            {
                string project = Globals.ProjectFolder;
                return string.IsNullOrEmpty(project)
                    ? null
                    : Path.Combine(project, "Cache", "MetaHumanTemplateEditorWindow.json");
            }
        }

        private void RestoreSettings()
        {
            WindowSettings settings = ReadSettings();
            if (settings == null)
            {
                return;
            }

            _pendingTemplateId = settings.TemplateId;
            _outputIdText = settings.OutputTemplateId;
            _ueDirText = settings.UeDirectory;
            _destText = settings.DestinationFolder;
            _assetsText = settings.ModelPaths;
            _targetText = settings.TargetModel;
            _sourceText = settings.SourceModel;
            _animDirText = settings.AnimationsDir;
        }

        private WindowSettings ReadSettings()
        {
            string path = SettingsPath;
            if (path == null || !File.Exists(path))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<WindowSettings>(File.ReadAllText(path));
            }
            catch (Exception)
            {
                // 上次写坏或手改过：丢掉记忆不影响任何功能，但别让它把窗口建不起来
                return null;
            }
        }

        // 控件在 BuildGui 里才创建，而 RestoreSettings 在那之前，所以先存字段、建控件时再回填。
        private string _pendingTemplateId;
        private string _outputIdText;
        private string _ueDirText;
        private string _destText;
        private string _assetsText;
        private string _targetText;
        private string _sourceText;
        private string _animDirText;

        private void SaveSettings()
        {
            string path = SettingsPath;
            if (path == null)
            {
                return;
            }

            WindowSettings settings = new WindowSettings
            {
                TemplateId = CurrentTemplateId,
                OutputTemplateId = OutputId(),
                UeDirectory = _ueDirBox.Text,
                DestinationFolder = _destFolderBox.Text,
                ModelPaths = _assetsBox.Text,
                TargetModel = _targetBox.Text,
                SourceModel = _sourceBox.Text,
                AnimationsDir = _animDirBox.Text,
            };

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception)
            {
            }
        }

        #endregion

        public override void OnDestroy()
        {
            SaveSettings();
            _rows.Clear();
            base.OnDestroy();
        }
    }
}
