using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.GameMain
{
    /// <summary>
    /// 好友信息数据（客户端本地数据结构）
    /// </summary>
    public class FriendDisplayInfo
    {
        public ulong CharacterId;
        public string Name = "";
        public int Level;
        public bool IsOnline;
        public string AreaName = "";
        public long LastOnlineTime;
    }

    /// <summary>
    /// 好友列表UI组件
    /// 显示好友列表、在线状态、添加/删除好友
    /// </summary>
    public class FriendListUI : Script
    {
        #region 配置参数

        [Header("好友列表配置")]
        [Tooltip("窗口宽度")]
        public float WindowWidth = 300f;

        [Tooltip("窗口高度")]
        public float WindowHeight = 400f;

        [Tooltip("好友条目高度")]
        public float EntryHeight = 40f;

        #endregion

        #region UI组件

        private Panel _friendWindow;
        private Panel _titleBar;
        private Label _titleLabel;
        private Button _closeButton;
        private Panel _listContainer;
        private Panel _actionPanel;
        private TextBox _addFriendInput;
        private Label _onlineCountLabel;

        private List<FriendDisplayInfo> _friends = new List<FriendDisplayInfo>();
        private List<FriendDisplayInfo> _pendingRequests = new List<FriendDisplayInfo>();
        private List<Panel> _friendEntries = new List<Panel>();

        private bool _isVisible = false;

        #endregion

        #region 生命周期

        public override void OnStart()
        {
            InitializeFriendListUI();
            HideFriendList();
            Debug.Log("[FriendListUI] 好友列表UI初始化完成");
        }

        public override void OnUpdate()
        {
            if (Input.GetKeyDown(KeyboardKeys.O))
            {
                ToggleFriendList();
            }
        }

        public override void OnDestroy()
        {
            CleanupFriendList();
        }

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化好友列表UI
        /// </summary>
        private void InitializeFriendListUI()
        {
            _friendWindow = new Panel
            {
                AnchorPreset = AnchorPresets.MiddleRight,
                Offsets = new Margin(-WindowWidth - 20, -WindowHeight / 2, -20, -WindowHeight / 2),
                Size = new Float2(WindowWidth, WindowHeight),
                BackgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.95f)
            };

            var canvas = Actor.GetScript<UICanvas>();
            if (canvas?.GUI != null)
            {
                canvas.GUI.AddChild(_friendWindow);
            }
            else
            {
                Debug.LogWarning("[FriendListUI] 未找到UICanvas组件");
                return;
            }

            CreateTitleBar();
            CreateListContainer();
            CreateActionPanel();
            LoadTestFriends();
        }

        /// <summary>
        /// 创建标题栏
        /// </summary>
        private void CreateTitleBar()
        {
            _titleBar = new Panel
            {
                Bounds = new Rectangle(0, 0, WindowWidth, 40),
                BackgroundColor = new Color(0.15f, 0.15f, 0.2f, 1.0f)
            };
            _friendWindow.AddChild(_titleBar);

            _titleLabel = new Label
            {
                Bounds = new Rectangle(10, 8, 120, 24),
                Text = "好友列表",
                TextColor = new Color(0.9f, 0.8f, 0.5f),
                TextColorHighlighted = new Color(0.9f, 0.8f, 0.5f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center
            };
            _titleBar.AddChild(_titleLabel);

            _onlineCountLabel = new Label
            {
                Bounds = new Rectangle(130, 8, 100, 24),
                Text = "在线: 0/0",
                TextColor = new Color(0.5f, 0.8f, 0.5f),
                TextColorHighlighted = new Color(0.5f, 0.8f, 0.5f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center
            };
            _titleBar.AddChild(_onlineCountLabel);

            _closeButton = new Button
            {
                Bounds = new Rectangle(WindowWidth - 35, 5, 30, 30),
                Text = "×",
                TextColor = Color.White,
                BackgroundColor = new Color(0.5f, 0.2f, 0.2f, 0.8f)
            };
            _closeButton.ButtonClicked += (btn) => HideFriendList();
            _titleBar.AddChild(_closeButton);
        }

        /// <summary>
        /// 创建好友列表容器
        /// </summary>
        private void CreateListContainer()
        {
            float containerHeight = WindowHeight - 40 - 50;

            _listContainer = new Panel
            {
                Bounds = new Rectangle(0, 40, WindowWidth, containerHeight),
                BackgroundColor = Color.Transparent
            };
            _friendWindow.AddChild(_listContainer);
        }

        /// <summary>
        /// 创建操作面板（添加好友输入框+按钮）
        /// </summary>
        private void CreateActionPanel()
        {
            float yPos = WindowHeight - 50;
            _actionPanel = new Panel
            {
                Bounds = new Rectangle(0, yPos, WindowWidth, 50),
                BackgroundColor = new Color(0.12f, 0.12f, 0.17f, 1.0f)
            };
            _friendWindow.AddChild(_actionPanel);

            _addFriendInput = new TextBox
            {
                Bounds = new Rectangle(10, 10, WindowWidth - 80, 30),
                WatermarkText = "输入角色名添加好友"
            };
            _actionPanel.AddChild(_addFriendInput);

            var addBtn = new Button
            {
                Bounds = new Rectangle(WindowWidth - 65, 10, 55, 30),
                Text = "添加",
                TextColor = Color.White,
                BackgroundColor = new Color(0.2f, 0.5f, 0.2f, 0.8f)
            };
            addBtn.ButtonClicked += (btn) => OnAddFriendClicked();
            _actionPanel.AddChild(addBtn);
        }

        #endregion

        #region 好友列表管理

        /// <summary>
        /// 添加测试好友数据
        /// </summary>
        private void LoadTestFriends()
        {
            _friends.Add(new FriendDisplayInfo { CharacterId = 1001, Name = "剑心", Level = 45, IsOnline = true, AreaName = "华山" });
            _friends.Add(new FriendDisplayInfo { CharacterId = 1002, Name = "风清扬", Level = 80, IsOnline = true, AreaName = "思过崖" });
            _friends.Add(new FriendDisplayInfo { CharacterId = 1003, Name = "令狐冲", Level = 35, IsOnline = false, AreaName = "" });
            _friends.Add(new FriendDisplayInfo { CharacterId = 1004, Name = "任盈盈", Level = 50, IsOnline = false, AreaName = "" });

            RefreshFriendList();
        }

        /// <summary>
        /// 更新好友列表（由网络消息触发）
        /// </summary>
        public void UpdateFriendList(List<FriendDisplayInfo> friends, List<FriendDisplayInfo> pendingRequests)
        {
            _friends = friends ?? new List<FriendDisplayInfo>();
            _pendingRequests = pendingRequests ?? new List<FriendDisplayInfo>();
            RefreshFriendList();
        }

        /// <summary>
        /// 刷新好友列表显示
        /// </summary>
        private void RefreshFriendList()
        {
            // 清除旧条目
            foreach (var entry in _friendEntries)
            {
                _listContainer.RemoveChild(entry);
                entry.Dispose();
            }
            _friendEntries.Clear();

            // 在线好友排在前面
            _friends.Sort((a, b) =>
            {
                if (a.IsOnline && !b.IsOnline) return -1;
                if (!a.IsOnline && b.IsOnline) return 1;
                return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            });

            float yOffset = 5f;

            // 添加好友条目
            foreach (var friend in _friends)
            {
                var entry = CreateFriendEntry(friend, yOffset);
                _friendEntries.Add(entry);
                _listContainer.AddChild(entry);
                yOffset += EntryHeight + 2f;
            }

            // 更新在线人数
            int onlineCount = _friends.FindAll(f => f.IsOnline).Count;
            _onlineCountLabel.Text = $"在线: {onlineCount}/{_friends.Count}";
        }

        /// <summary>
        /// 创建好友条目
        /// </summary>
        private Panel CreateFriendEntry(FriendDisplayInfo friend, float yPos)
        {
            var entry = new Panel
            {
                Bounds = new Rectangle(5, yPos, WindowWidth - 10, EntryHeight),
                BackgroundColor = friend.IsOnline
                    ? new Color(0.15f, 0.2f, 0.15f, 0.8f)
                    : new Color(0.15f, 0.15f, 0.2f, 0.6f)
            };

            // 在线状态指示器
            var statusDot = new Panel
            {
                Bounds = new Rectangle(5, (EntryHeight - 10) / 2, 10, 10),
                BackgroundColor = friend.IsOnline ? Color.Green : Color.Gray
            };
            entry.AddChild(statusDot);

            // 名称
            var nameLabel = new Label
            {
                Bounds = new Rectangle(20, 2, 120, 20),
                Text = friend.Name,
                TextColor = friend.IsOnline ? Color.White : Color.Gray,
                TextColorHighlighted = friend.IsOnline ? Color.White : Color.Gray,
                HorizontalAlignment = TextAlignment.Near
            };
            entry.AddChild(nameLabel);

            // 等级
            var levelLabel = new Label
            {
                Bounds = new Rectangle(20, 20, 60, 16),
                Text = $"Lv.{friend.Level}",
                TextColor = new Color(0.7f, 0.7f, 0.7f),
                TextColorHighlighted = new Color(0.7f, 0.7f, 0.7f),
                HorizontalAlignment = TextAlignment.Near
            };
            entry.AddChild(levelLabel);

            // 区域（在线时显示）
            if (friend.IsOnline && !string.IsNullOrEmpty(friend.AreaName))
            {
                var areaLabel = new Label
                {
                    Bounds = new Rectangle(80, 20, 100, 16),
                    Text = friend.AreaName,
                    TextColor = new Color(0.5f, 0.7f, 0.5f),
                    TextColorHighlighted = new Color(0.5f, 0.7f, 0.5f),
                    HorizontalAlignment = TextAlignment.Near
                };
                entry.AddChild(areaLabel);
            }

            // 删除按钮
            var deleteBtn = new Button
            {
                Bounds = new Rectangle(WindowWidth - 60, 8, 40, 24),
                Text = "删除",
                TextColor = Color.White,
                BackgroundColor = new Color(0.5f, 0.2f, 0.2f, 0.6f),
                Tag = friend.CharacterId
            };
            deleteBtn.ButtonClicked += (btn) =>
            {
                if (btn.Tag is ulong characterId)
                {
                    OnRemoveFriend(characterId);
                }
            };
            entry.AddChild(deleteBtn);

            return entry;
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 添加好友按钮点击
        /// </summary>
        private void OnAddFriendClicked()
        {
            var targetName = _addFriendInput.Text?.Trim();
            if (string.IsNullOrEmpty(targetName))
            {
                Debug.LogWarning("[FriendListUI] 请输入角色名");
                return;
            }

            Debug.Log($"[FriendListUI] 发送添加好友请求: {targetName}");
            _addFriendInput.Text = "";
        }

        /// <summary>
        /// 删除好友
        /// </summary>
        private void OnRemoveFriend(ulong characterId)
        {
            var friend = _friends.Find(f => f.CharacterId == characterId);
            if (friend != null)
            {
                Debug.Log($"[FriendListUI] 发送删除好友请求: {friend.Name}");
                _friends.Remove(friend);
                RefreshFriendList();
            }
        }

        #endregion

        #region 显示/隐藏

        /// <summary>
        /// 切换好友列表显示
        /// </summary>
        public void ToggleFriendList()
        {
            if (_isVisible)
                HideFriendList();
            else
                ShowFriendList();
        }

        /// <summary>
        /// 显示好友列表
        /// </summary>
        public void ShowFriendList()
        {
            _friendWindow.Visible = true;
            _isVisible = true;
            RefreshFriendList();
            Debug.Log("[FriendListUI] 显示好友列表");
        }

        /// <summary>
        /// 隐藏好友列表
        /// </summary>
        public void HideFriendList()
        {
            _friendWindow.Visible = false;
            _isVisible = false;
            Debug.Log("[FriendListUI] 隐藏好友列表");
        }

        /// <summary>
        /// 是否可见
        /// </summary>
        public bool IsVisible => _isVisible;

        #endregion

        #region 清理

        /// <summary>
        /// 清理好友列表UI
        /// </summary>
        private void CleanupFriendList()
        {
            if (_friendWindow != null && _friendWindow.Parent != null)
            {
                _friendWindow.Parent.RemoveChild(_friendWindow);
                _friendWindow.Dispose();
            }

            _friendEntries.Clear();
            _friends.Clear();
            _pendingRequests.Clear();
        }

        #endregion
    }
}
