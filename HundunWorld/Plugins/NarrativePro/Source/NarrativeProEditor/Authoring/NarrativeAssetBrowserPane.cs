#if FLAX_EDITOR
using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;

namespace NarrativePro.EditorTools
{
    internal sealed class NarrativeAssetEntry
    {
        public string Path;
        public bool IsQuest;
        public string Name;
        public string Subtitle;
        public int Problems;

        // 内容命中行专用：点开后窗口应顺带选中这个节点
        public string TargetKindTag = "";
        public string TargetId = "";
    }

    /// <summary>
    /// 左侧资产浏览器：搜索框 + 任务/对话分组列表（自绘行，双击/单击都能打开）。
    /// </summary>
    internal sealed class NarrativeAssetBrowserPane : ContainerControl
    {
        private readonly TextBox _search;
        private readonly AssetList _list;

        public event Action<NarrativeAssetEntry> AssetActivated;

        public NarrativeAssetBrowserPane()
        {
            _search = new TextBox
            {
                WatermarkText = "搜索资产名…",
                Bounds = new Rectangle(8f, 8f, 200f, 26f)
            };
            _search.TextChanged += () => _list.Filter = _search.Text;
            AddChild(_search);

            _list = new AssetList { Filter = "" };
            _list.AssetActivated += entry => AssetActivated?.Invoke(entry);
            AddChild(_list);
        }

        public void SetAssets(List<NarrativeAssetEntry> quests, List<NarrativeAssetEntry> dialogues)
        {
            _list.SetAssets(quests, dialogues);
        }

        /// <summary>把每个资产的问题数标到行尾（键=绝对路径）。</summary>
        public void SetProblems(Dictionary<string, int> problems)
        {
            _list.SetProblems(problems);
        }

        public void SetCurrent(string path)
        {
            _list.CurrentPath = path ?? "";
        }

        /// <summary>由窗口在布局时调用：把搜索框与列表按当前尺寸摆好。</summary>
        public void Arrange()
        {
            float width = Math.Max(120f, Width);
            _search.Bounds = new Rectangle(8f, 8f, Math.Max(60f, width - 16f), 26f);
            _list.Bounds = new Rectangle(0f, 42f, width, Math.Max(40f, Height - 42f));
        }

        private sealed class AssetList : Control
        {
            private sealed class Row
            {
                public bool Header;
                public string Label;
                public NarrativeAssetEntry Entry;
                public Rectangle Bounds;
            }

            private readonly List<Row> _rows = new List<Row>();
            private List<NarrativeAssetEntry> _quests = new List<NarrativeAssetEntry>();
            private List<NarrativeAssetEntry> _dialogues = new List<NarrativeAssetEntry>();
            private float _scroll;

            public event Action<NarrativeAssetEntry> AssetActivated;

            public string CurrentPath = "";
            private string _filter = "";
            private int _hover = -1;

            public string Filter
            {
                get => _filter;
                set
                {
                    string next = value ?? "";
                    if (string.Equals(next, _filter, StringComparison.Ordinal)) return;
                    _filter = next;
                    _scroll = 0f;
                    Rebuild();
                }
            }

            public void SetAssets(List<NarrativeAssetEntry> quests, List<NarrativeAssetEntry> dialogues)
            {
                _quests = quests ?? new List<NarrativeAssetEntry>();
                _dialogues = dialogues ?? new List<NarrativeAssetEntry>();
                _scroll = 0f;
                Rebuild();
            }

            public void SetProblems(Dictionary<string, int> problems)
            {
                foreach (var entry in _quests) entry.Problems = Lookup(problems, entry.Path);
                foreach (var entry in _dialogues) entry.Problems = Lookup(problems, entry.Path);
            }

            private static int Lookup(Dictionary<string, int> problems, string path)
            {
                if (problems == null || string.IsNullOrEmpty(path)) return 0;
                return problems.TryGetValue(path, out int count) ? count : 0;
            }

            private void Rebuild()
            {
                _rows.Clear();
                float y = 4f;

                AddSection("任务", _quests, y);
                AddSection("对话", _dialogues, _rows.Count == 0 ? y : _rows[_rows.Count - 1].Bounds.Bottom + 8f);
                AddContentHits(_rows.Count == 0 ? y : _rows[_rows.Count - 1].Bounds.Bottom + 8f);
            }

            /// <summary>按名字没匹配上时的兜底：全文搜台词/描述/选项，命中行点开直接定位到那个节点。</summary>
            private void AddContentHits(float y)
            {
                if (string.IsNullOrEmpty(Filter) || Filter.Trim().Length < 2) return;

                var hits = NarrativeAssetStore.SearchAllContent(Filter, 2);
                if (hits.Count == 0) return;

                _rows.Add(new Row
                {
                    Header = true,
                    Label = "内容命中 · " + hits.Count,
                    Bounds = new Rectangle(0f, y, Width, 24f)
                });
                y += 24f;

                foreach (var hit in hits)
                {
                    _rows.Add(new Row
                    {
                        Entry = new NarrativeAssetEntry
                        {
                            Path = hit.Path,
                            IsQuest = hit.IsQuest,
                            Name = NarrativeAssetStore.QuestAssetName(hit.Path) + " · " + hit.NodeId,
                            Subtitle = hit.Snippet,
                            TargetKindTag = hit.KindTag,
                            TargetId = hit.NodeId
                        },
                        Bounds = new Rectangle(0f, y, Width, 46f)
                    });
                    y += 46f;
                }
            }

            private void AddSection(string title, List<NarrativeAssetEntry> entries, float y)
            {
                var visible = new List<NarrativeAssetEntry>();
                foreach (var entry in entries)
                {
                    if (string.IsNullOrEmpty(Filter) ||
                        entry.Name.IndexOf(Filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        visible.Add(entry);
                    }
                }

                _rows.Add(new Row
                {
                    Header = true,
                    Label = title + " · " + visible.Count,
                    Bounds = new Rectangle(0f, y, Width, 24f)
                });
                y += 24f;

                if (visible.Count == 0)
                {
                    _rows.Add(new Row
                    {
                        Header = true,
                        Label = string.IsNullOrEmpty(Filter) ? "（空）" : "（无匹配）",
                        Bounds = new Rectangle(0f, y, Width, 22f)
                    });
                    y += 22f;
                }

                foreach (var entry in visible)
                {
                    _rows.Add(new Row
                    {
                        Entry = entry,
                        Bounds = new Rectangle(0f, y, Width, 46f)
                    });
                    y += 46f;
                }

                y += 6f;
            }

            public override void Draw()
            {
                NarrativeEditorTheme.Fill(new Rectangle(Float2.Zero, Size), NarrativeEditorTheme.Panel);

                float contentHeight = _rows.Count > 0 ? _rows[_rows.Count - 1].Bounds.Bottom + 6f : 0f;
                _scroll = Mathf.Clamp(_scroll, 0f, Math.Max(0f, contentHeight - Height));

                for (int i = 0; i < _rows.Count; i++)
                {
                    var row = _rows[i];
                    // 行宽用当前控件宽度：行是在布局完成前建的，Bounds.Width 那时还是 0。
                    var rect = new Rectangle(row.Bounds.X, row.Bounds.Y - _scroll, Width, row.Bounds.Height);
                    if (rect.Bottom < 0f || rect.Y > Height) continue;

                    if (row.Header)
                    {
                        bool empty = row.Label.StartsWith("（", StringComparison.Ordinal);
                        NarrativeEditorTheme.DrawText(row.Label,
                            new Rectangle(10f, rect.Y, Width - 16f, rect.Height),
                            empty ? NarrativeEditorTheme.TextDim : NarrativeEditorTheme.Accent,
                            NarrativeEditorTheme.FontSmall);
                        continue;
                    }

                    bool selected = string.Equals(row.Entry.Path, CurrentPath, StringComparison.OrdinalIgnoreCase);
                    bool hover = i == _hover;
                    if (selected || hover)
                    {
                        NarrativeEditorTheme.Fill(rect, selected ? NarrativeEditorTheme.PanelHover : new Color(0.16f, 0.165f, 0.175f));
                    }

                    var accent = row.Entry.IsQuest ? NarrativeEditorTheme.Accent : NarrativeEditorTheme.Player;
                    NarrativeEditorTheme.Fill(new Rectangle(rect.X + 4f, rect.Y + 8f, 3f, rect.Height - 16f), accent);

                    float textWidth = Width - 24f - (row.Entry.Problems > 0 ? 36f : 0f);
                    NarrativeEditorTheme.DrawText(NarrativeEditorTheme.Fit(row.Entry.Name, textWidth, NarrativeEditorTheme.FontMedium),
                        new Rectangle(rect.X + 14f, rect.Y + 5f, textWidth, 20f),
                        selected ? NarrativeEditorTheme.Selection : NarrativeEditorTheme.Text,
                        NarrativeEditorTheme.FontMedium);

                    NarrativeEditorTheme.DrawText(NarrativeEditorTheme.Fit(row.Entry.Subtitle, textWidth, NarrativeEditorTheme.FontSmall),
                        new Rectangle(rect.X + 14f, rect.Y + 25f, textWidth, 17f),
                        NarrativeEditorTheme.TextDim, NarrativeEditorTheme.FontSmall);

                    if (row.Entry.Problems > 0)
                    {
                        var badge = new Rectangle(rect.Right - 42f, rect.Y + 14f, 32f, 18f);
                        NarrativeEditorTheme.Fill(badge, new Color(0.42f, 0.15f, 0.15f));
                        NarrativeEditorTheme.DrawText(row.Entry.Problems.ToString(), badge, NarrativeEditorTheme.Failure,
                            NarrativeEditorTheme.FontSmall, TextAlignment.Center, TextAlignment.Center);
                    }
                }
            }

            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button != MouseButton.Left) return base.OnMouseDown(location, button);

                int index = RowAt(location);
                if (index >= 0 && _rows[index].Entry != null)
                {
                    AssetActivated?.Invoke(_rows[index].Entry);
                    return true;
                }
                return base.OnMouseDown(location, button);
            }

            public override void OnMouseMove(Float2 location)
            {
                int index = RowAt(location);
                if (index >= 0 && _rows[index].Entry == null) index = -1;
                _hover = index;
                base.OnMouseMove(location);
            }

            public override void OnMouseLeave()
            {
                _hover = -1;
                base.OnMouseLeave();
            }

            public override bool OnMouseWheel(Float2 location, float delta)
            {
                float contentHeight = _rows.Count > 0 ? _rows[_rows.Count - 1].Bounds.Bottom + 6f : 0f;
                if (contentHeight <= Height) return false;

                _scroll = Mathf.Clamp(_scroll - delta * 60f, 0f, contentHeight - Height);
                return true;
            }

            private int RowAt(Float2 location)
            {
                float y = location.Y + _scroll;
                for (int i = 0; i < _rows.Count; i++)
                {
                    var row = _rows[i];
                    if (y >= row.Bounds.Y && y <= row.Bounds.Bottom) return i;
                }
                return -1;
            }
        }
    }
}
#endif
