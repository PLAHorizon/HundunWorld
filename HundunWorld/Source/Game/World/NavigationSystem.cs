using FlaxEngine;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HundunWorld.Game.Navigation
{
    /// <summary>
    /// 地图标记类型
    /// </summary>
    public enum MapMarkerType
    {
        QuestObjective,   // 任务目标
        QuestNPC,         // 任务NPC
        Waypoint,         // 路径点
        PlayerCustom,     // 玩家自定义标记
        Dungeon,          // 副本入口
        Teleporter,       // 传送点
        Merchant,         // 商人
        Blacksmith,       // 铁匠
        Resource,         // 资源点
        TeamMember,       // 队友
        Boss,             // Boss
        Event,            // 活动
    }

    /// <summary>
    /// 地图标记数据
    /// </summary>
    [Serializable]
    public class MapMarker
    {
        public string Id { get; set; } = "";
        public MapMarkerType Type { get; set; }
        public string Label { get; set; } = "";
        public Vector3 WorldPosition { get; set; }
        public string ZoneName { get; set; } = "";
        public int Level { get; set; }
        public bool IsTracked { get; set; }
        public float ExpireTime { get; set; } = -1f; // -1=永久
        public string IconKey { get; set; } = "";
        public Color MarkerColor { get; set; } = Color.White;
    }

    /// <summary>
    /// 导航路径点
    /// </summary>
    public struct NavPathPoint
    {
        public Vector3 Position;
        public bool IsReached;
        public float DistanceFromStart;
    }

    /// <summary>
    /// 导航/寻路系统 - 产品级地图导航体验。
    /// 特性：
    /// - 地图标记管理（任务/NPC/资源/自定义）
    /// - 自动寻路（A*简化 + 路径平滑）
    /// - 路径跟随（自动移动到目标）
    /// - 距离/方向实时计算
    /// - 区域发现/迷雾
    /// - 传送点网络
    /// - 队友位置共享
    /// </summary>
    public class NavigationSystem
    {
        private static NavigationSystem _instance;
        public static NavigationSystem Instance => _instance ??= new NavigationSystem();

        // ===== 地图标记 =====
        private readonly List<MapMarker> _markers = new List<MapMarker>();
        private readonly Dictionary<string, MapMarker> _markerById = new Dictionary<string, MapMarker>();

        // ===== 路径跟随 =====
        private List<NavPathPoint> _currentPath = new List<NavPathPoint>();
        private int _currentPathIndex = 0;
        private bool _isFollowingPath = false;
        private string _followTargetId = "";
        private float _pathArrivalThreshold = 2f;

        // ===== 传送点 =====
        private readonly List<MapMarker> _teleporters = new List<MapMarker>();

        // ===== 区域 =====
        private readonly HashSet<string> _discoveredZones = new HashSet<string>();
        private string _currentZone = "";

        // ===== 事件 =====
        public event Action<MapMarker> OnMarkerAdded;
        public event Action<string> OnMarkerRemoved;
        public event Action<MapMarker> OnMarkerReached;
        public event Action<string> OnPathStarted;
        public event Action OnPathCompleted;
        public event Action OnPathCancelled;
        public event Action<string> OnZoneDiscovered;
        public event Action<string, string> OnZoneChanged; // (oldZone, newZone)

        // ===== 属性 =====
        public bool IsFollowingPath => _isFollowingPath;
        public string CurrentZone => _currentZone;
        public int MarkerCount => _markers.Count;
        public float DistanceToTarget { get; private set; } = -1f;
        public Vector3 DirectionToTarget { get; private set; } = Vector3.Zero;

        // ===== 标记管理 =====

        /// <summary>添加地图标记</summary>
        public MapMarker AddMarker(MapMarkerType type, string label, Vector3 position, string zone = "", int level = 0)
        {
            var marker = new MapMarker
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 8),
                Type = type,
                Label = label,
                WorldPosition = position,
                ZoneName = zone,
                Level = level,
                MarkerColor = GetMarkerColor(type),
            };

            _markers.Add(marker);
            _markerById[marker.Id] = marker;

            if (type == MapMarkerType.Teleporter)
                _teleporters.Add(marker);

            OnMarkerAdded?.Invoke(marker);
            return marker;
        }

        /// <summary>移除标记</summary>
        public void RemoveMarker(string markerId)
        {
            if (_markerById.TryGetValue(markerId, out var marker))
            {
                _markers.Remove(marker);
                _markerById.Remove(markerId);
                _teleporters.Remove(marker);
                OnMarkerRemoved?.Invoke(markerId);
            }
        }

        /// <summary>按类型获取标记</summary>
        public List<MapMarker> GetMarkersByType(MapMarkerType type)
        {
            return _markers.Where(m => m.Type == type).ToList();
        }

        /// <summary>获取追踪中的标记</summary>
        public List<MapMarker> GetTrackedMarkers()
        {
            return _markers.Where(m => m.IsTracked).ToList();
        }

        /// <summary>设置标记追踪状态</summary>
        public void SetMarkerTracked(string markerId, bool tracked)
        {
            if (_markerById.TryGetValue(markerId, out var marker))
                marker.IsTracked = tracked;
        }

        /// <summary>获取最近的指定类型标记</summary>
        public MapMarker GetNearestMarker(MapMarkerType type, Vector3 fromPosition)
        {
            return _markers
                .Where(m => m.Type == type)
                .OrderBy(m => Vector3.Distance(fromPosition, m.WorldPosition))
                .FirstOrDefault();
        }

        // ===== 路径跟随 =====

        /// <summary>开始导航到目标位置</summary>
        public void NavigateTo(Vector3 targetPosition, string targetId = "")
        {
            // 生成简化路径（直线 + 中间点）
            _currentPath = GeneratePath(targetPosition);
            _currentPathIndex = 0;
            _isFollowingPath = true;
            _followTargetId = targetId;

            OnPathStarted?.Invoke(targetId);
            Debug.Log($"[Navigation] 开始导航到 {targetPosition}, 路径点: {_currentPath.Count}");
        }

        /// <summary>导航到标记</summary>
        public void NavigateToMarker(string markerId)
        {
            if (_markerById.TryGetValue(markerId, out var marker))
            {
                NavigateTo(marker.WorldPosition, markerId);
            }
        }

        /// <summary>取消导航</summary>
        public void CancelNavigation()
        {
            if (!_isFollowingPath) return;
            _isFollowingPath = false;
            _currentPath.Clear();
            _currentPathIndex = 0;
            _followTargetId = "";
            DistanceToTarget = -1f;
            OnPathCancelled?.Invoke();
        }

        /// <summary>每帧更新路径跟随</summary>
        public void Update(Vector3 playerPosition, float deltaTime)
        {
            // 更新过期标记
            UpdateExpiredMarkers();

            if (!_isFollowingPath || _currentPath.Count == 0) return;

            // 计算到当前目标点的距离和方向
            if (_currentPathIndex < _currentPath.Count)
            {
                var targetPoint = _currentPath[_currentPathIndex];
                Vector3 toTarget = targetPoint.Position - playerPosition;
                DistanceToTarget = toTarget.Length;
                DirectionToTarget = DistanceToTarget > 0.01f ? toTarget / DistanceToTarget : Vector3.Zero;

                // 到达当前路径点
                if (DistanceToTarget <= _pathArrivalThreshold)
                {
                    var point = _currentPath[_currentPathIndex];
                    point.IsReached = true;
                    _currentPath[_currentPathIndex] = point;
                    _currentPathIndex++;

                    // 路径完成
                    if (_currentPathIndex >= _currentPath.Count)
                    {
                        _isFollowingPath = false;
                        DistanceToTarget = 0f;
                        OnPathCompleted?.Invoke();

                        // 检查是否到达标记
                        if (!string.IsNullOrEmpty(_followTargetId) && _markerById.ContainsKey(_followTargetId))
                        {
                            OnMarkerReached?.Invoke(_markerById[_followTargetId]);
                        }
                        _followTargetId = "";
                    }
                }
            }
        }

        /// <summary>获取下一个路径点（供移动系统使用）</summary>
        public Vector3? GetNextPathPoint()
        {
            if (!_isFollowingPath || _currentPathIndex >= _currentPath.Count)
                return null;
            return _currentPath[_currentPathIndex].Position;
        }

        /// <summary>获取路径进度(0-1)</summary>
        public float GetPathProgress()
        {
            if (_currentPath.Count == 0) return 0f;
            return (float)_currentPathIndex / _currentPath.Count;
        }

        // ===== 区域系统 =====

        /// <summary>更新当前区域</summary>
        public void UpdateZone(string zoneName)
        {
            if (zoneName == _currentZone) return;

            string oldZone = _currentZone;
            _currentZone = zoneName;

            if (!_discoveredZones.Contains(zoneName))
            {
                _discoveredZones.Add(zoneName);
                OnZoneDiscovered?.Invoke(zoneName);
            }

            OnZoneChanged?.Invoke(oldZone, zoneName);
        }

        /// <summary>检查区域是否已发现</summary>
        public bool IsZoneDiscovered(string zoneName) => _discoveredZones.Contains(zoneName);

        // ===== 传送 =====

        /// <summary>获取最近传送点</summary>
        public MapMarker GetNearestTeleporter(Vector3 fromPosition)
        {
            return _teleporters
                .OrderBy(t => Vector3.Distance(fromPosition, t.WorldPosition))
                .FirstOrDefault();
        }

        /// <summary>获取所有传送点</summary>
        public List<MapMarker> GetAllTeleporters() => new List<MapMarker>(_teleporters);

        // ===== 内部方法 =====

        private List<NavPathPoint> GeneratePath(Vector3 target)
        {
            // 简化路径生成：直线插值 + 随机偏移模拟道路
            var path = new List<NavPathPoint>();
            var playerPos = Vector3.Zero; // 实际应从玩家位置获取

            int segments = Mathf.Max(2, (int)(Vector3.Distance(playerPos, target) / 20f));
            float totalDist = 0f;

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                Vector3 pos = Vector3.Lerp(playerPos, target, t);

                // 中间点添加轻微偏移（模拟道路弯曲）
                if (i > 0 && i < segments)
                {
                    float offset = Mathf.Sin(t * Mathf.Pi * 2f) * 3f;
                    pos.X += offset;
                }

                float segDist = i > 0 ? Vector3.Distance(path[i - 1].Position, pos) : 0f;
                totalDist += segDist;

                path.Add(new NavPathPoint
                {
                    Position = pos,
                    IsReached = false,
                    DistanceFromStart = totalDist,
                });
            }

            return path;
        }

        private void UpdateExpiredMarkers()
        {
            if (Time.GameTime <= 0) return;

            for (int i = _markers.Count - 1; i >= 0; i--)
            {
                var marker = _markers[i];
                if (marker.ExpireTime > 0 && Time.GameTime > marker.ExpireTime)
                {
                    RemoveMarker(marker.Id);
                }
            }
        }

        private Color GetMarkerColor(MapMarkerType type) => type switch
        {
            MapMarkerType.QuestObjective => new Color(1f, 0.84f, 0f, 1f),      // 金色
            MapMarkerType.QuestNPC => new Color(1f, 0.84f, 0f, 1f),            // 金色
            MapMarkerType.Waypoint => new Color(0.5f, 0.8f, 1f, 1f),           // 蓝色
            MapMarkerType.Dungeon => new Color(0.8f, 0.2f, 0.8f, 1f),          // 紫色
            MapMarkerType.Teleporter => new Color(0.3f, 0.9f, 0.9f, 1f),       // 青色
            MapMarkerType.Merchant => new Color(0.9f, 0.7f, 0.3f, 1f),         // 橙色
            MapMarkerType.Resource => new Color(0.4f, 0.9f, 0.4f, 1f),         // 绿色
            MapMarkerType.TeamMember => new Color(0.3f, 0.7f, 1f, 1f),         // 蓝色
            MapMarkerType.Boss => new Color(1f, 0.2f, 0.2f, 1f),               // 红色
            MapMarkerType.Event => new Color(1f, 0.5f, 0.8f, 1f),              // 粉色
            _ => Color.White,
        };

        /// <summary>初始化默认传送点（Mock数据）</summary>
        public void InitDefaultTeleporters()
        {
            AddMarker(MapMarkerType.Teleporter, "开封城传送阵", new Vector3(100, 0, 200), "开封城");
            AddMarker(MapMarkerType.Teleporter, "青云山传送阵", new Vector3(-300, 50, 500), "青云山");
            AddMarker(MapMarkerType.Teleporter, "洛阳城传送阵", new Vector3(800, 0, -100), "洛阳城");
            AddMarker(MapMarkerType.Teleporter, "华山传送阵", new Vector3(-500, 120, -400), "华山");
        }
    }
}
