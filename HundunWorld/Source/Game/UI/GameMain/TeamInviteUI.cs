using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Components;

namespace HundunWorld.Game.UI.GameMain
{
    /// <summary>
    /// 组队邀请面板
    /// 显示收到的组队邀请，支持接受/拒绝/邀请他人等操作
    /// </summary>
    public class TeamInviteUI
    {
        /// <summary>
        /// 邀请信息
        /// </summary>
        public class InviteInfo
        {
            public ulong InviterId { get; set; }
            public string InviterName { get; set; } = "";
            public int InviterLevel { get; set; }
            public string InviterProfession { get; set; } = "";
            public ulong TeamId { get; set; }
            public int TeamSize { get; set; }
            public int MaxTeamSize { get; set; } = 5;
            public DateTime InviteTime { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// 队伍成员信息
        /// </summary>
        public class TeamMemberInfo
        {
            public ulong MemberId { get; set; }
            public string Name { get; set; } = "";
            public int Level { get; set; }
            public string Profession { get; set; } = "";
            public bool IsLeader { get; set; }
            public float HealthPercent { get; set; } = 1.0f;
            public float ManaPercent { get; set; } = 1.0f;
        }

        private Panel _panel;
        private Panel _inviteListPanel;
        private Panel _teamInfoPanel;

        // 事件
        public event Action<ulong> OnAcceptInvite;   // (inviterId)
        public event Action<ulong> OnDeclineInvite;  // (inviterId)
        public event Action<string> OnInvitePlayer;  // (playerName)
        public event Action OnLeaveTeam;

        /// <summary>
        /// 创建组队邀请面板内容
        /// </summary>
        public void PopulatePanel(Panel panel, float startY, float width, float height)
        {
            _panel = panel;
            float y = startY;

            // === 当前队伍信息 ===
            var teamTitle = new Label
            {
                Text = "── 当前队伍 ──",
                TextColor = new Color(1.0f, 0.84f, 0.0f),
                Bounds = new Rectangle(10, y, width - 20, 25),
                HorizontalAlignment = TextAlignment.Center
            };
            panel.AddChild(teamTitle);
            y += 28;

            // 队伍信息容器
            _teamInfoPanel = new Panel
            {
                Bounds = new Rectangle(10, y, width - 20, 120),
                BackgroundColor = new Color(0.08f, 0.08f, 0.12f, 0.5f)
            };
            panel.AddChild(_teamInfoPanel);

            PopulateTeamInfo();
            y += 130;

            // === 邀请操作区 ===
            var inviteTitle = new Label
            {
                Text = "── 邀请玩家 ──",
                TextColor = new Color(0.5f, 0.8f, 1.0f),
                Bounds = new Rectangle(10, y, width - 20, 25),
                HorizontalAlignment = TextAlignment.Center
            };
            panel.AddChild(inviteTitle);
            y += 28;

            // 搜索框
            var searchBox = new TextBox
            {
                Text = "",
                WatermarkText = "输入玩家名称...",
                Bounds = new Rectangle(10, y, width - 100, 30),
                BackgroundColor = new Color(0.15f, 0.15f, 0.2f, 0.9f),
                TextColor = Color.White
            };
            panel.AddChild(searchBox);

            var inviteBtn = new Button
            {
                Text = "邀请",
                Bounds = new Rectangle(width - 80, y, 70, 30),
                BackgroundColor = new Color(0.2f, 0.5f, 0.7f, 0.9f)
            };
            inviteBtn.Clicked += () => OnInvitePlayer?.Invoke(searchBox.Text);
            panel.AddChild(inviteBtn);
            y += 40;

            // === 收到的邀请列表 ===
            var pendingTitle = new Label
            {
                Text = "── 待处理邀请 ──",
                TextColor = new Color(1.0f, 0.6f, 0.2f),
                Bounds = new Rectangle(10, y, width - 20, 25),
                HorizontalAlignment = TextAlignment.Center
            };
            panel.AddChild(pendingTitle);
            y += 28;

            // 邀请列表容器
            float remainingHeight = height - (y - startY) - 10;
            _inviteListPanel = new Panel
            {
                Bounds = new Rectangle(10, y, width - 20, remainingHeight),
                BackgroundColor = new Color(0.08f, 0.08f, 0.12f, 0.5f)
            };
            panel.AddChild(_inviteListPanel);

            // 填充示例邀请数据
            PopulateSampleInvites();
        }

        /// <summary>
        /// 填充当前队伍信息
        /// </summary>
        private void PopulateTeamInfo()
        {
            var members = new List<TeamMemberInfo>
            {
                new() { MemberId = 1, Name = "队长大人", Level = 65, Profession = "剑客", IsLeader = true, HealthPercent = 0.85f, ManaPercent = 0.6f },
                new() { MemberId = 2, Name = "治疗小妹", Level = 60, Profession = "医师", IsLeader = false, HealthPercent = 1.0f, ManaPercent = 0.3f },
                new() { MemberId = 3, Name = "我自己", Level = 55, Profession = "法师", IsLeader = false, HealthPercent = 0.7f, ManaPercent = 0.5f }
            };

            float y = 5;
            float panelWidth = _teamInfoPanel.Width;

            // 队伍标题
            var teamLabel = new Label
            {
                Text = $"队伍 (3/5)",
                TextColor = new Color(0.8f, 0.8f, 0.8f),
                Bounds = new Rectangle(5, y, panelWidth - 10, 20),
                HorizontalAlignment = TextAlignment.Near
            };
            _teamInfoPanel.AddChild(teamLabel);
            y += 22;

            foreach (var member in members)
            {
                AddTeamMemberRow(member, y, panelWidth);
                y += 28;
            }
        }

        /// <summary>
        /// 添加队伍成员行
        /// </summary>
        private void AddTeamMemberRow(TeamMemberInfo member, float y, float width)
        {
            // 队长标记
            string leaderMark = member.IsLeader ? "★ " : "  ";

            var nameLabel = new Label
            {
                Text = $"{leaderMark}{member.Name}  Lv.{member.Level}  {member.Profession}",
                TextColor = member.IsLeader ? new Color(1.0f, 0.84f, 0.0f) : Color.White,
                Bounds = new Rectangle(5, y, width * 0.55f, 22),
                HorizontalAlignment = TextAlignment.Near
            };
            _teamInfoPanel.AddChild(nameLabel);

            // 生命条
            float barX = width * 0.58f;
            float barWidth = width * 0.18f;
            
            var hpBg = new Panel
            {
                Bounds = new Rectangle(barX, y + 3, barWidth, 8),
                BackgroundColor = new Color(0.3f, 0.1f, 0.1f)
            };
            _teamInfoPanel.AddChild(hpBg);

            var hpFill = new Panel
            {
                Bounds = new Rectangle(barX, y + 3, barWidth * member.HealthPercent, 8),
                BackgroundColor = new Color(0.2f, 0.8f, 0.2f)
            };
            _teamInfoPanel.AddChild(hpFill);

            // 蓝条
            float mpX = width * 0.78f;
            
            var mpBg = new Panel
            {
                Bounds = new Rectangle(mpX, y + 3, barWidth, 8),
                BackgroundColor = new Color(0.1f, 0.1f, 0.3f)
            };
            _teamInfoPanel.AddChild(mpBg);

            var mpFill = new Panel
            {
                Bounds = new Rectangle(mpX, y + 3, barWidth * member.ManaPercent, 8),
                BackgroundColor = new Color(0.2f, 0.4f, 0.9f)
            };
            _teamInfoPanel.AddChild(mpFill);
        }

        /// <summary>
        /// 填充示例邀请数据
        /// </summary>
        private void PopulateSampleInvites()
        {
            var sampleInvites = new List<InviteInfo>
            {
                new() { InviterId = 2001, InviterName = "江湖侠客", InviterLevel = 70, InviterProfession = "剑客", TeamSize = 3, MaxTeamSize = 5 },
                new() { InviterId = 2002, InviterName = "独行大侠", InviterLevel = 55, InviterProfession = "刺客", TeamSize = 1, MaxTeamSize = 5 }
            };

            float y = 5;
            float panelWidth = _inviteListPanel.Width;

            foreach (var invite in sampleInvites)
            {
                AddInviteRow(invite, y, panelWidth);
                y += 45;
            }
        }

        /// <summary>
        /// 添加邀请行
        /// </summary>
        private void AddInviteRow(InviteInfo invite, float y, float width)
        {
            // 邀请信息
            var infoLabel = new Label
            {
                Text = $"{invite.InviterName} (Lv.{invite.InviterLevel} {invite.InviterProfession}) 邀请你加入队伍 ({invite.TeamSize}/{invite.MaxTeamSize})",
                TextColor = Color.White,
                Bounds = new Rectangle(5, y, width - 10, 20),
                HorizontalAlignment = TextAlignment.Near
            };
            _inviteListPanel.AddChild(infoLabel);

            // 接受按钮
            var acceptBtn = new Button
            {
                Text = "接受",
                Bounds = new Rectangle(width - 150, y + 22, 65, 20),
                BackgroundColor = new Color(0.2f, 0.6f, 0.3f, 0.9f)
            };
            var capturedInvite = invite;
            acceptBtn.Clicked += () => OnAcceptInvite?.Invoke(capturedInvite.InviterId);
            _inviteListPanel.AddChild(acceptBtn);

            // 拒绝按钮
            var declineBtn = new Button
            {
                Text = "拒绝",
                Bounds = new Rectangle(width - 75, y + 22, 65, 20),
                BackgroundColor = new Color(0.6f, 0.2f, 0.2f, 0.9f)
            };
            declineBtn.Clicked += () => OnDeclineInvite?.Invoke(capturedInvite.InviterId);
            _inviteListPanel.AddChild(declineBtn);
        }
    }
}
