#if FLAX_EDITOR
using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;

namespace NarrativePro.EditorTools
{
    internal sealed class NarrativeIssue
    {
        public string File = "";
        public string Message = "";
        public string NodeId = "";
        public bool IsError = true;
        public bool IsQuest;
    }

    /// <summary>
    /// 底部校验面板：显示最近一次校验的逐条问题，点一行就跳到对应资产的对应节点。
    /// </summary>
    internal sealed class NarrativeIssuePane : Control
    {
        private const float HeaderHeight = 26f;
        private const float RowHeight = 22f;

        private readonly List<NarrativeIssue> _issues = new List<NarrativeIssue>();
        private string _summary = "尚未校验。点上方「校验当前」或「校验全部」。";
        private float _scroll;
        private int _hover = -1;

        public event Action<NarrativeIssue> IssueActivated;

        public NarrativeIssuePane()
        {
        }

        public IReadOnlyList<NarrativeIssue> Issues => _issues;

        public void SetIssues(List<NarrativeIssue> issues, string summary)
        {
            _issues.Clear();
            if (issues != null) _issues.AddRange(issues);
            _summary = summary ?? "";
            _scroll = 0f;
        }

        public void SetSummary(string summary)
        {
            _summary = summary ?? "";
        }

        public override void Draw()
        {
            var bounds = new Rectangle(Float2.Zero, Size);
            NarrativeEditorTheme.Fill(bounds, NarrativeEditorTheme.Panel);
            NarrativeEditorTheme.Fill(new Rectangle(0f, 0f, Width, 1f), NarrativeEditorTheme.Border);

            var summaryRect = new Rectangle(10f, 0f, Width - 20f, HeaderHeight);
            bool clean = _issues.Count == 0;
            Color summaryColor = clean ? NarrativeEditorTheme.Success : NarrativeEditorTheme.Failure;
            if (_summary.StartsWith("尚未", StringComparison.Ordinal)) summaryColor = NarrativeEditorTheme.TextDim;
            NarrativeEditorTheme.DrawText(_summary, summaryRect, summaryColor, NarrativeEditorTheme.FontSmall);

            float contentHeight = _issues.Count * RowHeight;
            float viewHeight = Math.Max(0f, Height - HeaderHeight);
            _scroll = Mathf.Clamp(_scroll, 0f, Math.Max(0f, contentHeight - viewHeight));

            for (int i = 0; i < _issues.Count; i++)
            {
                float y = HeaderHeight + i * RowHeight - _scroll;
                if (y + RowHeight < HeaderHeight || y > Height) continue;

                var rect = new Rectangle(0f, y, Width, RowHeight);
                if (i == _hover) NarrativeEditorTheme.Fill(rect, NarrativeEditorTheme.PanelHover);

                var issue = _issues[i];
                var dot = issue.IsError ? NarrativeEditorTheme.Failure : NarrativeEditorTheme.Warning;
                NarrativeEditorTheme.Fill(new Rectangle(12f, y + 8f, 6f, 6f), dot);

                string text = string.IsNullOrEmpty(issue.File) ? issue.Message : issue.File + "  ·  " + issue.Message;
                NarrativeEditorTheme.DrawText(NarrativeEditorTheme.Fit(text, Width - 40f, NarrativeEditorTheme.FontSmall),
                    new Rectangle(26f, y, Width - 34f, RowHeight),
                    issue.IsError ? NarrativeEditorTheme.Text : NarrativeEditorTheme.TextDim,
                    NarrativeEditorTheme.FontSmall);
            }

            if (_issues.Count == 0 && _summary.StartsWith("尚未", StringComparison.Ordinal))
            {
                NarrativeEditorTheme.DrawText("校验结果会把「哪个资产、哪条规则没过」逐条列在这里，点条目可定位节点。",
                    new Rectangle(10f, HeaderHeight + 2f, Width - 20f, 20f),
                    NarrativeEditorTheme.TextDim, NarrativeEditorTheme.FontSmall);
            }
        }

        public override void OnMouseMove(Float2 location)
        {
            _hover = RowAt(location);
            base.OnMouseMove(location);
        }

        public override void OnMouseLeave()
        {
            _hover = -1;
            base.OnMouseLeave();
        }

        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button != MouseButton.Left) return base.OnMouseDown(location, button);

            int index = RowAt(location);
            if (index >= 0)
            {
                IssueActivated?.Invoke(_issues[index]);
                return true;
            }
            return base.OnMouseDown(location, button);
        }

        public override bool OnMouseWheel(Float2 location, float delta)
        {
            float contentHeight = _issues.Count * RowHeight;
            float viewHeight = Math.Max(0f, Height - HeaderHeight);
            if (contentHeight <= viewHeight) return false;

            _scroll = Mathf.Clamp(_scroll - delta * 60f, 0f, contentHeight - viewHeight);
            return true;
        }

        private int RowAt(Float2 location)
        {
            if (location.Y < HeaderHeight) return -1;
            int index = (int)((location.Y - HeaderHeight + _scroll) / RowHeight);
            return index >= 0 && index < _issues.Count ? index : -1;
        }
    }
}
#endif
