using FlaxEngine;

namespace HundunWorld.Game.Gasp
{
    /// <summary>翻越检测结果（世界空间，厘米）。</summary>
    public struct GaspLedgeInfo
    {
        public bool Found;
        public Vector3 WallPoint;
        public Vector3 TopPoint;
        public Vector3 WallNormal;
        public float Height;
        /// <summary>0=左手, 1=右手（按角色右向量判定）。</summary>
        public byte Side;
        public bool IsValid => Found;
    }

    /// <summary>引擎侧翻越探测器接口（射线实现见 FlaxTraversalProbe；测试可替换）。</summary>
    public interface IGaspTraversalProbe
    {
        bool TryFindLedge(Vector3 feetWorld, Vector3 moveDirWorld, out GaspLedgeInfo info);
    }

    public sealed class GaspTraversalSettings
    {
        public float MinHeight = 45f;
        public float MaxHeight = 220f;
        public float OneMeterMaxHeight = 120f;
        /// <summary>落地时从墙沿向平台内侧偏移（厘米）。</summary>
        public float LandingOffset = 45f;
        public float CooldownSeconds = 0.4f;
        public float ArcHeight = 8f;
    }

    /// <summary>
    /// 一次翻越的运动扭曲计划：起点=角色脚底，终点=平台顶内侧；位置按 smoothstep 分配，
    /// 中段略抬形成小弧线。这里的输出只描述“应该在哪”，真正移动仍由位移权威层消费。
    /// </summary>
    public sealed class GaspTraversalPlan
    {
        public int Clip;
        public string ClipName;
        public float Duration;
        public Vector3 StartPosition;
        public Vector3 EndPosition;
        public Quaternion StartRotation;
        public Quaternion EndRotation;
        public float ArcHeight = 8f;
        public float Elapsed;

        public bool Done => Elapsed >= Duration;
        public float Alpha => Mathf.Saturate(Duration <= 1e-4f ? 1f : Elapsed / Duration);

        public Vector3 EvaluatePosition()
        {
            float t = Mathf.SmoothStep(0f, 1f, Alpha);
            var p = Vector3.Lerp(StartPosition, EndPosition, t);
            p.Y += ArcHeight * Mathf.Sin(Alpha * Mathf.Pi);
            return p;
        }

        public Quaternion EvaluateRotation()
        {
            float t = Mathf.SmoothStep(0f, 1f, Alpha);
            return Quaternion.Slerp(StartRotation, EndRotation, t);
        }

        public Vector3 EvaluateVelocity(float dt)
        {
            if (dt <= 1e-5f)
                return Vector3.Zero;
            float old = Elapsed;
            Elapsed = Mathf.Max(0f, old - dt);
            var prev = EvaluatePosition();
            Elapsed = Mathf.Min(Duration, old + dt);
            var next = EvaluatePosition();
            Elapsed = old;
            return (next - prev) / (2f * dt);
        }
    }

    /// <summary>按高度/手侧选择 Mantle 剪辑。</summary>
    public static class GaspTraversalSelector
    {
        public static int Select(GaspAnimationDatabase db, in GaspLedgeInfo ledge, out string reason)
        {
            reason = null;
            if (db == null || !db.IsLoaded)
            {
                reason = "动画库未加载";
                return -1;
            }
            if (!ledge.Found)
            {
                reason = "没有检测到边缘";
                return -1;
            }

            bool oneMeter = ledge.Height <= 120f;
            string heightToken = oneMeter ? "Mantle_1m" : "Mantle_2m";
            string sideToken = ledge.Side == 0 ? "LH" : "RH";

            int fallback = -1;
            for (int i = 0; i < db.Clips.Count; i++)
            {
                var clip = db.Clips[i];
                if (clip.Tag != GaspClipTag.Traversal)
                    continue;
                if (clip.Name.IndexOf(heightToken, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (fallback < 0)
                    fallback = i;
                // 2m 只有一条；1m 优先匹配手侧。
                if (!oneMeter || clip.Name.IndexOf(sideToken, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return i;
            }
            if (fallback >= 0)
            {
                reason = "手侧剪辑缺失，回退同高度剪辑";
                return fallback;
            }
            reason = "没有高度匹配的 Mantle 剪辑";
            return -1;
        }
    }

    /// <summary>
    /// 翻越状态机。只负责检测→选库→运动扭曲计划；位移由调用方按权威层消费。
    /// </summary>
    public sealed class GaspTraversalController
    {
        public GaspTraversalSettings Settings = new GaspTraversalSettings();
        public GaspAnimationDatabase Database;
        public bool Active { get; private set; }
        public GaspTraversalPlan Plan { get; private set; }
        public GaspLedgeInfo Ledge { get; private set; }
        public int StartCount { get; private set; }
        public int FinishCount { get; private set; }
        public int RejectCount { get; private set; }
        public string LastReason { get; private set; } = "";
        public bool StartedThisTick { get; private set; }
        public bool FinishedThisTick { get; private set; }

        private float _cooldown;

        public bool TryStart(in GaspLedgeInfo ledge, Vector3 feetPosition, Quaternion rotation, float now, float clipDurationOverride = -1f)
        {
            StartedThisTick = false;
            LastReason = "";
            if (Active)
            {
                LastReason = "已在翻越中";
                RejectCount++;
                return false;
            }
            if (_cooldown > 0f)
            {
                LastReason = $"冷却中 {_cooldown:F2}s";
                RejectCount++;
                return false;
            }
            if (!ledge.Found)
            {
                LastReason = "没有检测到边缘";
                RejectCount++;
                return false;
            }
            if (ledge.Height < Settings.MinHeight || ledge.Height > Settings.MaxHeight)
            {
                LastReason = $"高度超范围 {ledge.Height:F0}cm";
                RejectCount++;
                return false;
            }

            int clip = GaspTraversalSelector.Select(Database, ledge, out string reason);
            if (clip < 0)
            {
                LastReason = reason ?? "没有可用剪辑";
                RejectCount++;
                return false;
            }

            var n = ledge.WallNormal;
            n.Y = 0f;
            if (n.LengthSquared < 1e-5f)
                n = Vector3.Backward;
            n.Normalize();

            var end = ledge.TopPoint - n * Settings.LandingOffset;
            end.Y = ledge.TopPoint.Y;
            float duration = clipDurationOverride > 0f
                ? clipDurationOverride
                : Database.Clips[clip].Length;

            Plan = new GaspTraversalPlan
            {
                Clip = clip,
                ClipName = Database.Clips[clip].Name,
                Duration = Mathf.Max(0.2f, duration),
                StartPosition = feetPosition,
                EndPosition = end,
                StartRotation = rotation,
                EndRotation = rotation,
                ArcHeight = Settings.ArcHeight,
                Elapsed = 0f,
            };
            Ledge = ledge;
            Active = true;
            StartedThisTick = true;
            StartCount++;
            return true;
        }

        public void Tick(float dt)
        {
            FinishedThisTick = false;
            if (_cooldown > 0f)
                _cooldown = Mathf.Max(0f, _cooldown - dt);
            if (!Active || Plan == null)
                return;

            Plan.Elapsed += dt;
            if (Plan.Done)
            {
                Active = false;
                FinishedThisTick = true;
                FinishCount++;
                _cooldown = Settings.CooldownSeconds;
            }
        }

        public void Cancel()
        {
            Active = false;
            Plan = null;
            _cooldown = Settings.CooldownSeconds;
        }

        public Vector3 CurrentPosition => Plan != null ? Plan.EvaluatePosition() : Vector3.Zero;
        public Quaternion CurrentRotation => Plan != null ? Plan.EvaluateRotation() : Quaternion.Identity;
    }
}
