using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HundunWorld.Game.UI
{
    /// <summary>
    /// 任务追踪条目数据
    /// </summary>
    [Serializable]
    public class QuestTrackEntry
    {
        public string QuestId { get; set; } = "";
        public string QuestName { get; set; } = "";
        public string Category { get; set; } = "主线";
        public int Level { get; set; }
        public float Progress { get; set; }
        public List<QuestObjectiveData> Objectives { get; set; } = new List<QuestObjectiveData>();
        public bool IsCompleted { get; set; }
        public bool IsCollapsed { get; set; }
        public string TargetLocation { get; set; } = "";
        public float DistanceToTarget { get; set; } = -1f;
    }

    /// <summary>
    /// 任务目标数据
    /// </summary>
    [Serializable]
    public class QuestObjectiveData
    {
        public string Description { get; set; } = "";
        public int Current { get; set; }
        public int Required { get; set; } = 1;
        public bool IsCompleted => Current >= Required;
        public float Progress => Required > 0 ? (float)Current / Required : 0f;
    }

    /// <summary>
    /// 任务追踪HUD — 游戏内实时任务追踪面板。
    /// 产品级特性：
    /// - 屏幕右侧紧凑面板，显示当前追踪的任务
    /// - 任务目标实时进度更新
    /// - 折叠/展开单个任务
    /// - 任务完成自动移除 + 完成动画
    /// - 距离指示（目标方位/距离）
    /// - 最多同时追踪5个任务
    /// - 平滑展开/折叠动画
    /// </summary>
    public class QuestTrackerHUD : ContainerControl
    {
        // ===== 配置 =====
        private const float PanelWidth = 320f;
        private const float HeaderHeight = 28f;
        private const float QuestHeaderHeight = 36f;
        private const float ObjectiveHeight = 20f;
        private const float QuestPadding = 8f;
        private const float ItemGap = 4f;
        private const int MaxTrackedQuests = 5;
        private const float CollapseAnimSpeed = 8f;

        // ===== 数据 =====
        private readonly List<QuestTrackEntry> _trackedQuests = new List<QuestTrackEntry>();
        private readonly Dictionary<string, float> _collapseAnims = new Dictionary<string, float>();

        // ===== UI控件 =====
        private Label _headerLabel;
        private Panel _backgroundPanel;
        private readonly List<QuestItemUI> _questItems = new List<QuestItemUI>();

        // ===== 事件 =====
        /// <summary>点击任务条目时触发（打开任务详情）</summary>
        public event Action<string> OnQuestClicked;
        /// <summary>任务完成时触发</summary>
        public event Action<string> OnQuestCompleted;
        /// <summary>点击追踪目标时触发（自动寻路）</summary>
        public event Action<string> OnNavigateRequested;

        public int TrackedCount => _trackedQuests.Count;

        public QuestTrackerHUD()
        {
            AnchorPreset = AnchorPresets.TopRight;
            Size = new Float2(PanelWidth, 400f);
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;

            BuildUI();
            LoadDefaultQuests();
        }

        private void BuildUI()
        {
            // 半透明背景
            _backgroundPanel = new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                BackgroundColor = new Color(10f / 255f, 12f / 255f, 18f / 255f, 0.65f),
            };
            AddChild(_backgroundPanel);

            // 标题栏
            _headerLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 4f),
                Size = new Float2(PanelWidth - 24f, HeaderHeight),
                Text = "◆ 任务追踪",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            AddChild(_headerLabel);
        }

        /// <summary>加载默认追踪任务（Mock数据，后续接入服务端）</summary>
        private void LoadDefaultQuests()
        {
            AddTrackedQuest(new QuestTrackEntry
            {
                QuestId = "main_001",
                QuestName = "初入江湖",
                Category = "主线",
                Level = 1,
                Progress = 0.4f,
                TargetLocation = "开封城",
                DistanceToTarget = 235f,
                Objectives = new List<QuestObjectiveData>
                {
                    new QuestObjectiveData { Description = "前往开封城", Current = 1, Required = 1 },
                    new QuestObjectiveData { Description = "与王铁匠对话", Current = 0, Required = 1 },
                    new QuestObjectiveData { Description = "击败山贼", Current = 2, Required = 5 },
                }
            });

            AddTrackedQuest(new QuestTrackEntry
            {
                QuestId = "side_003",
                QuestName = "药材采集",
                Category = "支线",
                Level = 5,
                Progress = 0.6f,
                TargetLocation = "青云山",
                DistanceToTarget = 890f,
                Objectives = new List<QuestObjectiveData>
                {
                    new QuestObjectiveData { Description = "采集灵芝草", Current = 6, Required = 10 },
                    new QuestObjectiveData { Description = "交付李药师", Current = 0, Required = 1 },
                }
            });

            AddTrackedQuest(new QuestTrackEntry
            {
                QuestId = "daily_012",
                QuestName = "日常·除魔卫道",
                Category = "日常",
                Level = 10,
                Progress = 0.3f,
                Objectives = new List<QuestObjectiveData>
                {
                    new QuestObjectiveData { Description = "击杀野外怪物", Current = 3, Required = 10 },
                }
            });

            RebuildQuestItems();
        }

        // ===== 公共API =====

        /// <summary>添加追踪任务</summary>
        public bool AddTrackedQuest(QuestTrackEntry quest)
        {
            if (_trackedQuests.Count >= MaxTrackedQuests)
            {
                Debug.LogWarning("[QuestTracker] 追踪任务已满（最多5个）");
                return false;
            }
            if (_trackedQuests.Any(q => q.QuestId == quest.QuestId))
                return false;

            _trackedQuests.Add(quest);
            _collapseAnims[quest.QuestId] = 1f; // 默认展开
            RebuildQuestItems();
            return true;
        }

        /// <summary>移除追踪任务</summary>
        public void RemoveTrackedQuest(string questId)
        {
            var quest = _trackedQuests.FirstOrDefault(q => q.QuestId == questId);
            if (quest != null)
            {
                _trackedQuests.Remove(quest);
                _collapseAnims.Remove(questId);
                RebuildQuestItems();
            }
        }

        /// <summary>更新任务目标进度</summary>
        public void UpdateObjectiveProgress(string questId, int objectiveIndex, int current, int required)
        {
            var quest = _trackedQuests.FirstOrDefault(q => q.QuestId == questId);
            if (quest == null || objectiveIndex >= quest.Objectives.Count) return;

            var obj = quest.Objectives[objectiveIndex];
            obj.Current = current;
            obj.Required = required;

            // 重新计算任务总进度
            float totalProgress = quest.Objectives.Average(o => o.Progress);
            quest.Progress = totalProgress;

            // 检查是否全部完成
            if (quest.Objectives.All(o => o.IsCompleted) && !quest.IsCompleted)
            {
                quest.IsCompleted = true;
                OnQuestCompleted?.Invoke(questId);
                // 延迟移除
                RemoveTrackedQuestDelayed(questId, 2f);
            }

            RefreshQuestItemUI(quest);
        }

        /// <summary>更新目标距离</summary>
        public void UpdateDistance(string questId, float distance)
        {
            var quest = _trackedQuests.FirstOrDefault(q => q.QuestId == questId);
            if (quest != null)
            {
                quest.DistanceToTarget = distance;
                RefreshQuestItemUI(quest);
            }
        }

        /// <summary>切换任务折叠状态</summary>
        public void ToggleCollapse(string questId)
        {
            var quest = _trackedQuests.FirstOrDefault(q => q.QuestId == questId);
            if (quest != null)
            {
                quest.IsCollapsed = !quest.IsCollapsed;
            }
        }

        /// <summary>清空所有追踪</summary>
        public void ClearAll()
        {
            _trackedQuests.Clear();
            _collapseAnims.Clear();
            RebuildQuestItems();
        }

        // ===== 每帧更新 =====

        public new void OnUpdate()
        {
            float dt = Time.DeltaTime;

            // 折叠/展开动画
            bool needsLayout = false;
            foreach (var quest in _trackedQuests)
            {
                float target = quest.IsCollapsed ? 0f : 1f;
                if (!_collapseAnims.ContainsKey(quest.QuestId))
                    _collapseAnims[quest.QuestId] = target;

                float current = _collapseAnims[quest.QuestId];
                if (Mathf.Abs(current - target) > 0.01f)
                {
                    float newVal = Mathf.Lerp(current, target, dt * CollapseAnimSpeed);
                    _collapseAnims[quest.QuestId] = newVal;
                    needsLayout = true;
                }
            }

            if (needsLayout)
                LayoutQuestItems();
        }

        // ===== 内部方法 =====

        private float _removeTimer = 0f;
        private string _pendingRemoveId = "";

        private void RemoveTrackedQuestDelayed(string questId, float delay)
        {
            _pendingRemoveId = questId;
            _removeTimer = delay;
        }

        private void RebuildQuestItems()
        {
            // 清除旧控件
            foreach (var item in _questItems)
            {
                item.Dispose();
            }
            _questItems.Clear();

            // 创建新控件
            foreach (var quest in _trackedQuests)
            {
                var itemUI = new QuestItemUI(quest, this);
                AddChild(itemUI);
                _questItems.Add(itemUI);
            }

            LayoutQuestItems();
        }

        private void LayoutQuestItems()
        {
            float y = HeaderHeight + 6f;

            for (int i = 0; i < _questItems.Count && i < _trackedQuests.Count; i++)
            {
                var quest = _trackedQuests[i];
                var itemUI = _questItems[i];

                float expandRatio = _collapseAnims.ContainsKey(quest.QuestId)
                    ? _collapseAnims[quest.QuestId] : 1f;

                float objectiveCount = quest.Objectives.Count;
                float fullHeight = QuestHeaderHeight + objectiveCount * ObjectiveHeight + QuestPadding * 2;
                float collapsedHeight = QuestHeaderHeight;
                float height = Mathf.Lerp(collapsedHeight, fullHeight, expandRatio);

                itemUI.Location = new Float2(4f, y);
                itemUI.Size = new Float2(PanelWidth - 8f, height);
                itemUI.UpdateExpandRatio(expandRatio);

                y += height + ItemGap;
            }

            // 更新整体面板高度
            float totalHeight = y + 8f;
            Height = Mathf.Max(HeaderHeight + 20f, totalHeight);
        }

        private void RefreshQuestItemUI(QuestTrackEntry quest)
        {
            foreach (var item in _questItems)
            {
                if (item.BoundQuestId == quest.QuestId)
                {
                    item.RefreshData(quest);
                    break;
                }
            }
        }

        /// <summary>内部：触发任务点击</summary>
        internal void RaiseQuestClicked(string questId) => OnQuestClicked?.Invoke(questId);

        /// <summary>内部：触发导航请求</summary>
        internal void RaiseNavigateRequested(string questId) => OnNavigateRequested?.Invoke(questId);
    }

    /// <summary>
    /// 单个任务追踪条目UI
    /// </summary>
    internal class QuestItemUI : ContainerControl
    {
        private readonly QuestTrackerHUD _owner;
        private QuestTrackEntry _quest;

        private Label _categoryTag;
        private Label _nameLabel;
        private Label _levelLabel;
        private Label _distanceLabel;
        private Panel _progressTrack;
        private Panel _progressFill;
        private Label[] _objectiveLabels;
        private Label _collapseHint;

        public string BoundQuestId => _quest?.QuestId ?? "";

        public QuestItemUI(QuestTrackEntry quest, QuestTrackerHUD owner)
        {
            _quest = quest;
            _owner = owner;
            BackgroundColor = new Color(20f / 255f, 24f / 255f, 32f / 255f, 0.5f);
            ClipChildren = true;

            BuildControls();
            RefreshData(quest);
        }

        private void BuildControls()
        {
            // 分类标签
            _categoryTag = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 4f),
                Size = new Float2(36f, 16f),
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 10f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                BackgroundColor = new Color(200f / 255f, 120f / 255f, 60f / 255f, 0.15f),
            };
            AddChild(_categoryTag);

            // 任务名
            _nameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(48f, 2f),
                Size = new Float2(160f, 20f),
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            AddChild(_nameLabel);

            // 等级
            _levelLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(210f, 2f),
                Size = new Float2(40f, 20f),
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            AddChild(_levelLabel);

            // 距离指示
            _distanceLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(250f, 2f),
                Size = new Float2(56f, 20f),
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 10f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            AddChild(_distanceLabel);

            // 进度条
            _progressTrack = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 24f),
                Size = new Float2(296f, 3f),
                BackgroundColor = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.1f),
            };
            AddChild(_progressTrack);

            _progressFill = new Panel
            {
                Location = Float2.Zero,
                Size = new Float2(0f, 3f),
                BackgroundColor = InkWashTheme.GoldPrimary,
            };
            _progressTrack.AddChild(_progressFill);

            // 折叠提示
            _collapseHint = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 28f),
                Size = new Float2(296f, 14f),
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 9f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            AddChild(_collapseHint);

            // 目标标签（最多5个）
            _objectiveLabels = new Label[5];
            for (int i = 0; i < 5; i++)
            {
                var objLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(16f, 34f + i * 20f),
                    Size = new Float2(280f, 18f),
                    TextColor = InkWashTheme.TextSecondary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Visible = false,
                };
                AddChild(objLabel);
                _objectiveLabels[i] = objLabel;
            }

            // 点击事件
        }

        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button == MouseButton.Left)
                _owner.RaiseQuestClicked(BoundQuestId);
            else if (button == MouseButton.Right)
                _owner.RaiseNavigateRequested(BoundQuestId);
            return base.OnMouseDown(location, button);
        }

        public void RefreshData(QuestTrackEntry quest)
        {
            _quest = quest;
            _categoryTag.Text = quest.Category;
            _nameLabel.Text = quest.QuestName;
            _levelLabel.Text = $"Lv.{quest.Level}";

            if (quest.DistanceToTarget >= 0)
            {
                _distanceLabel.Text = quest.DistanceToTarget < 1000f
                    ? $"{(int)quest.DistanceToTarget}m"
                    : $"{quest.DistanceToTarget / 1000f:F1}km";
            }
            else
            {
                _distanceLabel.Text = "";
            }

            // 进度条
            float trackW = 296f;
            _progressFill.Size = new Float2(trackW * Mathf.Clamp(quest.Progress, 0f, 1f), 3f);

            // 目标
            for (int i = 0; i < _objectiveLabels.Length; i++)
            {
                if (i < quest.Objectives.Count)
                {
                    var obj = quest.Objectives[i];
                    string symbol = obj.IsCompleted ? "✓" : "○";
                    string progressText = obj.Required > 1 ? $" ({obj.Current}/{obj.Required})" : "";
                    _objectiveLabels[i].Text = $"{symbol} {obj.Description}{progressText}";
                    _objectiveLabels[i].TextColor = obj.IsCompleted
                        ? InkWashTheme.TextJade
                        : InkWashTheme.TextSecondary;
                    _objectiveLabels[i].Visible = true;
                }
                else
                {
                    _objectiveLabels[i].Visible = false;
                }
            }

            _collapseHint.Text = quest.IsCollapsed ? "▸ 点击展开" : "";
        }

        public void UpdateExpandRatio(float ratio)
        {
            // 根据展开比例调整目标标签的可见性和透明度
            for (int i = 0; i < _objectiveLabels.Length; i++)
            {
                if (_objectiveLabels[i].Visible)
                {
                    float alpha = Mathf.Clamp(ratio * 2f - 0.5f, 0f, 1f);
                    var color = _objectiveLabels[i].TextColor;
                    _objectiveLabels[i].TextColor = new Color(color.R, color.G, color.B, alpha);
                }
            }
            _collapseHint.Text = ratio < 0.5f ? "▸ 点击展开" : "";
        }
    }
}
