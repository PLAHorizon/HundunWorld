using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Components;

namespace HundunWorld.Game.UI.GameMain
{
    /// <summary>
    /// 公会管理面板
    /// 支持成员管理、职位调整、公告编辑等公会管理功能
    /// </summary>
    public class GuildManagementUI
    {
        /// <summary>
        /// 公会成员信息
        /// </summary>
        public class GuildMemberInfo
        {
            public ulong MemberId { get; set; }
            public string Name { get; set; } = "";
            public int Level { get; set; }
            public string Rank { get; set; } = "成员";
            public bool IsOnline { get; set; }
            public string Profession { get; set; } = "";
            public int Contribution { get; set; }
        }

        private Panel _panel;
        private Panel _memberListPanel;
        private TextBox _announcementInput;

        // 事件
        public event Action<ulong, string> OnKickMember;      // (memberId, memberName)
        public event Action<ulong, string> OnPromoteMember;    // (memberId, memberName)
        public event Action<ulong, string> OnDemoteMember;     // (memberId, memberName)
        public event Action<string> OnUpdateAnnouncement;       // (announcement)

        /// <summary>
        /// 创建公会管理面板内容
        /// </summary>
        public void PopulatePanel(Panel panel, float startY, float width, float height)
        {
            _panel = panel;
            float y = startY;

            // === 公会信息区域 ===
            var infoTitle = new Label
            {
                Text = "── 公会信息 ──",
                TextColor = new Color(1.0f, 0.84f, 0.0f),
                Bounds = new Rectangle(10, y, width - 20, 25),
                HorizontalAlignment = TextAlignment.Center
            };
            panel.AddChild(infoTitle);
            y += 30;

            // 公会名称和等级
            var guildNameLabel = new Label
            {
                Text = "【天山派】 Lv.5    成员: 32/50",
                TextColor = Color.White,
                Bounds = new Rectangle(10, y, width - 20, 22),
                HorizontalAlignment = TextAlignment.Center
            };
            panel.AddChild(guildNameLabel);
            y += 28;

            // 公会资金和经验
            var resourceLabel = new Label
            {
                Text = "资金: 125,000    经验: 45,000/100,000",
                TextColor = new Color(0.7f, 0.8f, 0.7f),
                Bounds = new Rectangle(10, y, width - 20, 22),
                HorizontalAlignment = TextAlignment.Center
            };
            panel.AddChild(resourceLabel);
            y += 30;

            // === 公告编辑 ===
            var announcementTitle = new Label
            {
                Text = "公会公告:",
                TextColor = new Color(0.8f, 0.8f, 0.8f),
                Bounds = new Rectangle(10, y, 100, 22),
                HorizontalAlignment = TextAlignment.Near
            };
            panel.AddChild(announcementTitle);
            y += 25;

            _announcementInput = new TextBox
            {
                Text = "欢迎加入天山派！每日活动时间20:00-22:00",
                Bounds = new Rectangle(10, y, width - 100, 50),
                BackgroundColor = new Color(0.15f, 0.15f, 0.2f, 0.9f),
                TextColor = Color.White
            };
            panel.AddChild(_announcementInput);

            var saveButton = new Button
            {
                Text = "保存",
                Bounds = new Rectangle(width - 80, y, 70, 50),
                BackgroundColor = new Color(0.2f, 0.5f, 0.3f, 0.9f)
            };
            saveButton.Clicked += () => OnUpdateAnnouncement?.Invoke(_announcementInput.Text);
            panel.AddChild(saveButton);
            y += 60;

            // === 成员列表 ===
            var memberTitle = new Label
            {
                Text = "── 成员列表 ──",
                TextColor = new Color(1.0f, 0.84f, 0.0f),
                Bounds = new Rectangle(10, y, width - 20, 25),
                HorizontalAlignment = TextAlignment.Center
            };
            panel.AddChild(memberTitle);
            y += 28;

            // 列标题
            CreateMemberHeader(panel, y, width);
            y += 25;

            // 成员列表容器
            float remainingHeight = height - (y - startY) - 10;
            _memberListPanel = new Panel
            {
                Bounds = new Rectangle(10, y, width - 20, remainingHeight),
                BackgroundColor = new Color(0.08f, 0.08f, 0.12f, 0.5f)
            };
            panel.AddChild(_memberListPanel);

            // 填充示例成员数据
            PopulateSampleMembers();
        }

        /// <summary>
        /// 创建成员列表表头
        /// </summary>
        private void CreateMemberHeader(Panel panel, float y, float width)
        {
            string[] headers = { "名称", "等级", "职位", "贡献", "状态", "操作" };
            float[] widths = { 0.2f, 0.1f, 0.12f, 0.15f, 0.1f, 0.28f };
            float x = 10;

            foreach (int i in System.Linq.Enumerable.Range(0, headers.Length))
            {
                var headerLabel = new Label
                {
                    Text = headers[i],
                    TextColor = new Color(0.6f, 0.6f, 0.6f),
                    Bounds = new Rectangle(x, y, (width - 20) * widths[i], 22),
                    HorizontalAlignment = TextAlignment.Center
                };
                panel.AddChild(headerLabel);
                x += (width - 20) * widths[i];
            }
        }

        /// <summary>
        /// 填充示例成员数据
        /// </summary>
        private void PopulateSampleMembers()
        {
            var sampleMembers = new List<GuildMemberInfo>
            {
                new() { MemberId = 1001, Name = "帮主大人", Level = 80, Rank = "帮主", IsOnline = true, Profession = "剑客", Contribution = 50000 },
                new() { MemberId = 1002, Name = "副帮主", Level = 75, Rank = "副帮主", IsOnline = true, Profession = "医师", Contribution = 35000 },
                new() { MemberId = 1003, Name = "精英弟子", Level = 60, Rank = "长老", IsOnline = false, Profession = "刺客", Contribution = 20000 },
                new() { MemberId = 1004, Name = "新人玩家", Level = 25, Rank = "成员", IsOnline = true, Profession = "法师", Contribution = 1500 }
            };

            float y = 5;
            float panelWidth = _memberListPanel.Width;

            foreach (var member in sampleMembers)
            {
                AddMemberRow(member, y, panelWidth);
                y += 30;
            }
        }

        /// <summary>
        /// 添加成员行
        /// </summary>
        private void AddMemberRow(GuildMemberInfo member, float y, float width)
        {
            float[] colWidths = { 0.2f, 0.1f, 0.12f, 0.15f, 0.1f, 0.28f };
            float x = 0;

            // 名称
            var nameLabel = new Label
            {
                Text = member.Name,
                TextColor = Color.White,
                Bounds = new Rectangle(x, y, width * colWidths[0], 22),
                HorizontalAlignment = TextAlignment.Center
            };
            _memberListPanel.AddChild(nameLabel);
            x += width * colWidths[0];

            // 等级
            var levelLabel = new Label
            {
                Text = $"Lv.{member.Level}",
                TextColor = new Color(0.8f, 0.8f, 0.8f),
                Bounds = new Rectangle(x, y, width * colWidths[1], 22),
                HorizontalAlignment = TextAlignment.Center
            };
            _memberListPanel.AddChild(levelLabel);
            x += width * colWidths[1];

            // 职位
            Color rankColor = member.Rank switch
            {
                "帮主" => new Color(1.0f, 0.84f, 0.0f),
                "副帮主" => new Color(0.7f, 0.3f, 0.9f),
                "长老" => new Color(0.3f, 0.5f, 1.0f),
                _ => new Color(0.7f, 0.7f, 0.7f)
            };

            var rankLabel = new Label
            {
                Text = member.Rank,
                TextColor = rankColor,
                Bounds = new Rectangle(x, y, width * colWidths[2], 22),
                HorizontalAlignment = TextAlignment.Center
            };
            _memberListPanel.AddChild(rankLabel);
            x += width * colWidths[2];

            // 贡献
            var contribLabel = new Label
            {
                Text = member.Contribution.ToString("N0"),
                TextColor = new Color(0.8f, 0.8f, 0.8f),
                Bounds = new Rectangle(x, y, width * colWidths[3], 22),
                HorizontalAlignment = TextAlignment.Center
            };
            _memberListPanel.AddChild(contribLabel);
            x += width * colWidths[3];

            // 在线状态
            var statusLabel = new Label
            {
                Text = member.IsOnline ? "●在线" : "○离线",
                TextColor = member.IsOnline ? new Color(0.3f, 1.0f, 0.3f) : new Color(0.5f, 0.5f, 0.5f),
                Bounds = new Rectangle(x, y, width * colWidths[4], 22),
                HorizontalAlignment = TextAlignment.Center
            };
            _memberListPanel.AddChild(statusLabel);
            x += width * colWidths[4];

            // 操作按钮（非帮主成员）
            if (member.Rank != "帮主")
            {
                float btnWidth = (width * colWidths[5] - 10) / 2;

                var promoteBtn = new Button
                {
                    Text = "提升",
                    Bounds = new Rectangle(x, y, btnWidth - 2, 22),
                    BackgroundColor = new Color(0.2f, 0.4f, 0.6f, 0.8f)
                };
                var capturedMember = member;
                promoteBtn.Clicked += () => OnPromoteMember?.Invoke(capturedMember.MemberId, capturedMember.Name);
                _memberListPanel.AddChild(promoteBtn);

                var kickBtn = new Button
                {
                    Text = "踢出",
                    Bounds = new Rectangle(x + btnWidth + 2, y, btnWidth - 2, 22),
                    BackgroundColor = new Color(0.6f, 0.2f, 0.2f, 0.8f)
                };
                kickBtn.Clicked += () => OnKickMember?.Invoke(capturedMember.MemberId, capturedMember.Name);
                _memberListPanel.AddChild(kickBtn);
            }
        }
    }
}
