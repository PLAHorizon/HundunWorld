using System;
using FlaxEngine;
using FlaxEngine.GUI;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.Team
{
    /// <summary>
    /// 组队页（dom-id: nav-team）——重铸为“创建/加入 + 当前队伍列表”。
    /// 数据来自 TeamClientService（服务端 TeamGrain 的真实 Create/Join 回执）；
    /// 未回执时显示“未组队/同步中”，不编造队友与队伍。
    /// </summary>
    public class TeamRosterPage : ContainerControl, IInkPage, IInkNavigationSource
    {
        private const float ScreenEdge = 16f;
        private const float HeaderHeight = 64f;
        private const float SidebarWidth = 196f;
        private const float SidebarGap = 16f;
        private const float ContentTop = 88f;
        private const float LeftWidth = 420f;
        private const float PanelGap = 16f;
        private const float RefreshInterval = 0.25f;

        private InkBackButton _back;
        private InkTextBlock _title;
        private InkTextBlock _subtitle;
        private InkTextBlock _note;
        private InkSidebarList _sidebar;

        private InkPanel _left;
        private InkTextBlock _createTitle;
        private TextBox _nameBox;
        private TextBox _goalBox;
        private InkButton _createBtn;
        private InkTextBlock _joinTitle;
        private TextBox _teamIdBox;
        private InkButton _joinBtn;
        private InkTextBlock _notice;

        private InkPanel _right;
        private InkTextBlock _teamTitle;
        private InkBadge _teamBadge;
        private InkTextBlock _leaderLabel;
        private InkTextBlock _goalLabel;
        private InkStatCell _memberStat;
        private ContainerControl _membersHost;
        private InkTextBlock _emptyHint;
        private InkTextBlock _rosterNote;
        private InkButton _leaveBtn;
        private InkButton _disbandBtn;

        private int _lastRevision = -1;
        private float _refreshTimer;

        public event Action<string> NavigationRequested;

        public TeamRosterPage()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            Offsets = Margin.Zero;
            BackgroundColor = InkWashTheme.BaseSecondary;
            ClipChildren = false;
            AutoFocus = false;

            BuildHeader();
            BuildSidebar();
            BuildLeftPanel();
            BuildRightPanel();

            ApplyLayout();
            TeamClientService.Instance.EnsureAttached();
            RefreshFromService();
            FlaxEngine.Debug.Log("[UI-REFORGE] TeamRosterPage built");
        }

        public void RefreshLayout() => ApplyLayout();

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            TeamClientService.Instance.EnsureAttached();
            _refreshTimer += deltaTime;
            if (_refreshTimer < RefreshInterval) return;
            _refreshTimer = 0f;
            RefreshFromService();
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            ApplyLayout();
        }

        private void BuildHeader()
        {
            _back = new InkBackButton { AnchorPreset = AnchorPresets.TopLeft };
            _back.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.BackOneLevel);
            AddChild(_back);

            _title = MakeText("队伍", InkTextStyle.Display, InkWashTheme.GoldPrimary, 26f);
            AddChild(_title);
            _subtitle = MakeText("创建 / 加入 · 队伍成员", InkTextStyle.Caption, InkWashTheme.TextTertiary, 12f);
            AddChild(_subtitle);
            _note = MakeText("创建/加入已接服务端；离开/踢人/转移待接入", InkTextStyle.Caption, InkWashTheme.TextFaint, 11f);
            _note.HorizontalAlignment = TextAlignment.Far;
            AddChild(_note);
        }

        private void BuildSidebar()
        {
            _sidebar = new InkSidebarList(InkPageDomIds.NavTeam);
            _sidebar.NavigationRequested += id => NavigationRequested?.Invoke(id);
            AddChild(_sidebar);
        }

        private void BuildLeftPanel()
        {
            _left = new InkPanel { Radius = 10f };
            AddChild(_left);

            _createTitle = MakeText("创建队伍", InkTextStyle.Heading, InkWashTheme.GoldBright, 18f);
            _left.AddChild(_createTitle);

            _nameBox = MakeTextBox("队伍名称");
            _left.AddChild(_nameBox);
            _goalBox = MakeTextBox("队伍目标（可选）");
            _left.AddChild(_goalBox);

            _createBtn = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Md,
                Text = "创建队伍",
            };
            _createBtn.Clicked += OnCreateClicked;
            _left.AddChild(_createBtn);

            _joinTitle = MakeText("加入队伍", InkTextStyle.Heading, InkWashTheme.JadeBright, 18f);
            _left.AddChild(_joinTitle);

            _teamIdBox = MakeTextBox("队伍ID（服务端回执中的 TeamId）");
            _left.AddChild(_teamIdBox);

            _joinBtn = new InkButton
            {
                Variant = InkButtonVariant.Secondary,
                ButtonSize = InkButtonSize.Md,
                Text = "加入队伍",
            };
            _joinBtn.Clicked += OnJoinClicked;
            _left.AddChild(_joinBtn);

            _notice = MakeText("—", InkTextStyle.Caption, InkWashTheme.TextTertiary, 11f);
            _left.AddChild(_notice);
        }

        private void BuildRightPanel()
        {
            _right = new InkPanel { Radius = 10f };
            AddChild(_right);

            _teamTitle = MakeText("尚未加入队伍", InkTextStyle.Heading, InkWashTheme.TextDefault, 22f);
            _right.AddChild(_teamTitle);

            _teamBadge = new InkBadge("未组队", InkWashTheme.TextTertiary);
            _right.AddChild(_teamBadge);

            _leaderLabel = MakeText("队长    —", InkTextStyle.Caption, InkWashTheme.TextSecondary, 12f);
            _right.AddChild(_leaderLabel);
            _goalLabel = MakeText("目标    —", InkTextStyle.Caption, InkWashTheme.TextSecondary, 12f);
            _right.AddChild(_goalLabel);

            _memberStat = new InkStatCell("队伍人数", "— / —", "服务端未回执", InkWashTheme.GoldBright);
            _right.AddChild(_memberStat);

            _membersHost = new ContainerControl { ClipChildren = true, BackgroundColor = Color.Transparent };
            _right.AddChild(_membersHost);

            _emptyHint = MakeText("尚未加入队伍。创建队伍或输入 TeamId 加入。", InkTextStyle.Body, InkWashTheme.TextTertiary, 13f);
            _right.AddChild(_emptyHint);

            _rosterNote = MakeText("成员名称/等级由服务端回执提供；当前回执只带 ID/队长/在线位。",
                InkTextStyle.Caption, InkWashTheme.TextFaint, 11f);
            _right.AddChild(_rosterNote);

            _leaveBtn = new InkButton
            {
                Variant = InkButtonVariant.Secondary,
                ButtonSize = InkButtonSize.Sm,
                Text = "离开队伍",
            };
            _leaveBtn.Clicked += () => TeamClientService.Instance.LeaveTeam();
            _right.AddChild(_leaveBtn);

            _disbandBtn = new InkButton
            {
                Variant = InkButtonVariant.Danger,
                ButtonSize = InkButtonSize.Sm,
                Text = "解散队伍",
            };
            _disbandBtn.Clicked += () => TeamClientService.Instance.DisbandTeam();
            _right.AddChild(_disbandBtn);
        }

        private void OnCreateClicked()
        {
            var ok = TeamClientService.Instance.CreateTeam(_nameBox.Text, _goalBox.Text);
            if (!ok) _notice.Text = TeamClientService.Instance.LastNotice;
        }

        private void OnJoinClicked()
        {
            if (!ulong.TryParse(_teamIdBox.Text?.Trim(), out var teamId))
            {
                _notice.Text = "请输入有效的队伍ID";
                return;
            }

            var ok = TeamClientService.Instance.JoinTeam(teamId);
            if (!ok) _notice.Text = TeamClientService.Instance.LastNotice;
        }

        private void RefreshFromService()
        {
            var service = TeamClientService.Instance;
            if (service.Revision == _lastRevision) return;
            _lastRevision = service.Revision;

            _notice.Text = string.IsNullOrEmpty(service.LastNotice) ? "—" : service.LastNotice;
            var team = service.CurrentTeam;
            if (team == null || team.TeamId == 0UL)
            {
                _teamTitle.Text = "尚未加入队伍";
                _teamBadge.Text = "未组队";
                _teamBadge.SetAccent(InkWashTheme.TextTertiary);
                _leaderLabel.Text = "队长    —";
                _goalLabel.Text = "目标    —";
                _memberStat.SetValue("— / —", "服务端未回执");
                _membersHost.RemoveChildren();
                _emptyHint.Visible = true;
                if (_leaveBtn != null) _leaveBtn.Visible = false;
                if (_disbandBtn != null) _disbandBtn.Visible = false;
                return;
            }

            _teamTitle.Text = string.IsNullOrWhiteSpace(team.TeamName) ? $"队伍 #{team.TeamId}" : team.TeamName;
            _teamBadge.Text = "已组队";
            _teamBadge.SetAccent(InkWashTheme.JadeBright);
            _leaderLabel.Text = $"队长    #{team.LeaderId}";
            _goalLabel.Text = string.IsNullOrWhiteSpace(team.TeamGoal) ? "目标    —" : "目标    " + team.TeamGoal;
            var count = team.Members?.Count ?? 0;
            _memberStat.SetValue($"{count} / 5", "成员列表来自服务端回执");
            _emptyHint.Visible = count == 0;
            var self = HundunWorldGame.Instance?.PlayerId ?? 0UL;
            if (_leaveBtn != null) _leaveBtn.Visible = true;
            if (_disbandBtn != null) _disbandBtn.Visible = team.LeaderId == self;
            RebuildMembers(team);
        }

        private void RebuildMembers(TeamInfo team)
        {
            _membersHost.RemoveChildren();
            if (team.Members == null) return;

            const float rowH = 44f;
            const float rowGap = 6f;
            var self = HundunWorldGame.Instance?.PlayerId ?? 0UL;
            var viewerIsLeader = team.LeaderId == self;
            for (var i = 0; i < team.Members.Count; i++)
            {
                var m = team.Members[i];
                if (m == null) continue;

                var row = new InkPanel
                {
                    Radius = 6f,
                    BackgroundColor = new Color(InkWashTheme.Panel.R, InkWashTheme.Panel.G, InkWashTheme.Panel.B, 0.55f),
                };
                row.Location = new Float2(0f, i * (rowH + rowGap));
                row.Size = new Float2(_membersHost.Width, rowH);

                var name = string.IsNullOrWhiteSpace(m.CharacterName) ? $"#{m.CharacterId}" : m.CharacterName;
                var nameLabel = MakeText(name, InkTextStyle.Body, InkWashTheme.TextDefault, 13f);
                nameLabel.Location = new Float2(12f, 4f);
                var extraButtons = viewerIsLeader && !m.IsLeader ? 120f : 0f;
                nameLabel.Size = new Float2(Mathf.Max(0f, row.Size.X - 120f - extraButtons), 20f);
                row.AddChild(nameLabel);

                var roleLabel = MakeText(m.IsLeader ? "队长" : "队员", InkTextStyle.Caption,
                    m.IsLeader ? InkWashTheme.GoldBright : InkWashTheme.TextTertiary, 11f);
                roleLabel.Location = new Float2(12f, 23f);
                roleLabel.Size = new Float2(80f, 14f);
                row.AddChild(roleLabel);

                var online = MakeText(m.IsOnline ? "在线" : "离线", InkTextStyle.Caption,
                    m.IsOnline ? InkWashTheme.JadeBright : InkWashTheme.TextFaint, 11f);
                online.HorizontalAlignment = TextAlignment.Far;
                online.Location = new Float2(Mathf.Max(0f, row.Size.X - 72f - extraButtons), 12f);
                online.Size = new Float2(60f, 18f);
                row.AddChild(online);

                if (viewerIsLeader && !m.IsLeader)
                {
                    var kick = new InkButton
                    {
                        Variant = InkButtonVariant.Ghost,
                        ButtonSize = InkButtonSize.Sm,
                        Text = "踢出",
                    };
                    kick.Location = new Float2(Mathf.Max(0f, row.Size.X - 120f), 8f);
                    kick.Size = new Float2(52f, 28f);
                    var targetId = m.CharacterId;
                    kick.Clicked += () => TeamClientService.Instance.KickMember(targetId);
                    row.AddChild(kick);

                    var transfer = new InkButton
                    {
                        Variant = InkButtonVariant.Ghost,
                        ButtonSize = InkButtonSize.Sm,
                        Text = "转让",
                    };
                    transfer.Location = new Float2(Mathf.Max(0f, row.Size.X - 62f), 8f);
                    transfer.Size = new Float2(52f, 28f);
                    transfer.Clicked += () => TeamClientService.Instance.TransferLeader(targetId);
                    row.AddChild(transfer);
                }

                _membersHost.AddChild(row);
            }
        }

        private static TextBox MakeTextBox(string watermark)
        {
            return new TextBox
            {
                WatermarkText = watermark,
                WatermarkTextColor = InkWashTheme.TextFaint,
                TextColor = InkWashTheme.TextDefault,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                BackgroundColor = new Color(InkWashTheme.Panel.R, InkWashTheme.Panel.G, InkWashTheme.Panel.B, 0.72f),
                BackgroundSelectedColor = new Color(InkWashTheme.Panel.R, InkWashTheme.Panel.G, InkWashTheme.Panel.B, 0.9f),
            };
        }

        private static InkTextBlock MakeText(string text, InkTextStyle style, Color color, float size)
        {
            var label = new InkTextBlock(style)
            {
                Text = text,
                TextColor = color,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
            };
            label.Font = InkRenderHelper.GetFontRef(
                style == InkTextStyle.Display ? InkWashTheme.FontRole.Display :
                style == InkTextStyle.Heading ? InkWashTheme.FontRole.Heading : InkWashTheme.FontRole.Body, size);
            return label;
        }

        private void ApplyLayout()
        {
            if (_back == null) return;

            float w = Width > 0f ? Width : 1920f;
            float h = Height > 0f ? Height : 1080f;

            _back.Location = new Float2(ScreenEdge, ScreenEdge);
            _title.Location = new Float2(64f, 8f);
            _title.Size = new Float2(220f, 32f);
            _subtitle.Location = new Float2(64f, 38f);
            _subtitle.Size = new Float2(420f, 18f);
            _note.Location = new Float2(w - 560f, 24f);
            _note.Size = new Float2(540f, 18f);

            _sidebar.Layout(ScreenEdge, ContentTop, SidebarWidth, h - ContentTop - ScreenEdge);

            float contentX = ScreenEdge + SidebarWidth + SidebarGap;
            float contentW = Mathf.Max(0f, w - contentX - ScreenEdge);
            float panelH = Mathf.Min(640f, h - ContentTop - 48f);

            _left.Location = new Float2(contentX, ContentTop);
            _left.Size = new Float2(LeftWidth, panelH);

            float pad = 20f;
            _createTitle.Location = new Float2(pad, 16f);
            _createTitle.Size = new Float2(Mathf.Max(0f, LeftWidth - pad * 2f), 24f);
            _nameBox.Location = new Float2(pad, 52f);
            _nameBox.Size = new Float2(Mathf.Max(0f, LeftWidth - pad * 2f), 36f);
            _goalBox.Location = new Float2(pad, 96f);
            _goalBox.Size = new Float2(Mathf.Max(0f, LeftWidth - pad * 2f), 36f);
            _createBtn.Location = new Float2(pad, 142f);
            _createBtn.Size = new Float2(140f, 36f);
            _joinTitle.Location = new Float2(pad, 202f);
            _joinTitle.Size = new Float2(Mathf.Max(0f, LeftWidth - pad * 2f), 24f);
            _teamIdBox.Location = new Float2(pad, 238f);
            _teamIdBox.Size = new Float2(Mathf.Max(0f, LeftWidth - pad * 2f), 36f);
            _joinBtn.Location = new Float2(pad, 284f);
            _joinBtn.Size = new Float2(140f, 36f);
            _notice.Location = new Float2(pad, 334f);
            _notice.Size = new Float2(Mathf.Max(0f, LeftWidth - pad * 2f), 18f);

            float rightX = contentX + LeftWidth + PanelGap;
            float rightW = Mathf.Max(0f, w - rightX - ScreenEdge);
            _right.Location = new Float2(rightX, ContentTop);
            _right.Size = new Float2(rightW, panelH);

            _teamTitle.Location = new Float2(pad, 18f);
            _teamTitle.Size = new Float2(Mathf.Max(0f, rightW - pad * 2f - 96f), 30f);
            _teamBadge.Location = new Float2(rightW - 96f, 18f);
            _teamBadge.Size = new Float2(76f, 22f);
            _leaderLabel.Location = new Float2(pad, 56f);
            _leaderLabel.Size = new Float2(Mathf.Max(0f, rightW - pad * 2f), 18f);
            _goalLabel.Location = new Float2(pad, 78f);
            _goalLabel.Size = new Float2(Mathf.Max(0f, rightW - pad * 2f), 18f);

            _memberStat.Location = new Float2(pad, 108f);
            _memberStat.Size = new Float2(220f, 64f);

            _membersHost.Location = new Float2(pad, 186f);
            _membersHost.Size = new Float2(Mathf.Max(0f, rightW - pad * 2f), Mathf.Max(0f, panelH - 310f));
            _emptyHint.Location = new Float2(pad, 190f);
            _emptyHint.Size = new Float2(Mathf.Max(0f, rightW - pad * 2f), 20f);

            _leaveBtn.Location = new Float2(pad, panelH - 76f);
            _leaveBtn.Size = new Float2(110f, 32f);
            _disbandBtn.Location = new Float2(pad + 122f, panelH - 76f);
            _disbandBtn.Size = new Float2(110f, 32f);

            _rosterNote.Location = new Float2(pad, panelH - 28f);
            _rosterNote.Size = new Float2(Mathf.Max(0f, rightW - pad * 2f), 16f);
        }
    }
}
