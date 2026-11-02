using System;
using System.Collections.Generic;
using System.IO;
using FlaxEngine;

namespace HundunWorld.Game.Gasp
{
    /// <summary>
    /// 参考实现“调试绘制”的 Flax 版：画出 MM 查询轨迹（过去/预测）、剪辑朝向 vs 期望朝向、
    /// 速度向量、最近落脚点与当前剪辑信息。仅由自测标记 <c>%APPDATA%/HundunWorld/gasp_debug.on</c> 挂载。
    /// </summary>
    public class GaspDebugDraw : Script
    {
        public bool Enabled = true;
        public float StepMarkerSeconds = 0.6f;
        public float HeadingArrowLength = 100f;

        private GaspAnimator _gasp;
        private readonly List<StepMark> _steps = new List<StepMark>(8);
        private bool _loggedAttached;
        private bool _loggedFirstDraw;

        private struct StepMark
        {
            public Vector3 Position;
            public float Until;
        }

        public static bool DebugMarkerPresent()
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HundunWorld");
                return File.Exists(Path.Combine(dir, "gasp_debug.on"));
            }
            catch
            {
                return false;
            }
        }

        public override void OnStart()
        {
            _gasp = Actor.GetScript<GaspAnimator>();
            if (_gasp == null)
            {
                Enabled = false;
                return;
            }
            _gasp.Footstep += OnFootstep;
            if (!_loggedAttached)
            {
                _loggedAttached = true;
                Debug.Log($"[GaspDebugDraw] attached actor={Actor.Name}");
            }
        }

        public override void OnUpdate()
        {
            if (!Enabled || _gasp == null || !_gasp.IsReady || Actor == null)
                return;

            if (!_loggedFirstDraw)
            {
                _loggedFirstDraw = true;
                Debug.Log($"[GaspDebugDraw] first draw actor={Actor.Name} clip={_gasp.LastClipName}");
            }
            DrawTrajectory();
            DrawHeadingAndSpeed();
            DrawStepMarkers();
            DrawLabel();
        }

        private void DrawTrajectory()
        {
            var traj = _gasp.DebugTrajectory;
            if (traj == null || traj.Length < 2)
                return;
            var times = GaspFeatureSchema.TrajectoryTimes;
            float groundY = Actor.Position.Y + 2f;
            for (int i = 1; i < traj.Length && i < times.Length; i++)
            {
                var color = times[i] < 0f
                    ? new Color(0.72f, 0.72f, 0.72f)
                    : new Color(0.2f, 0.9f, 1f);
                var a = new Vector3(traj[i - 1].X, groundY, traj[i - 1].Z);
                var b = new Vector3(traj[i].X, groundY, traj[i].Z);
                DebugDraw.DrawLine(a, b, color, 0f, false);
                DebugDraw.DrawCircle(b, new Float3(0f, 1f, 0f), times[i] < 0f ? 4f : 6f, color, 0f, false);
            }
        }

        private void DrawHeadingAndSpeed()
        {
            var velocity = _gasp.LastPlanarVelocity;
            if (velocity.Length <= 20f)
                return;

            var origin = Actor.Position + new Vector3(0f, 95f, 0f);
            DrawArrow(origin, _gasp.LastClipHeadingRad, new Color(1f, 0.35f, 0.25f));
            DrawArrow(origin + new Vector3(0f, 6f, 0f), _gasp.LastDesiredHeadingRad, new Color(0.3f, 1f, 0.4f));

            var feet = Actor.Position + new Vector3(0f, 10f, 0f);
            DebugDraw.DrawLine(feet, feet + velocity * 0.25f, new Color(1f, 0.9f, 0.2f), 0f, false);
        }

        private void DrawArrow(Vector3 origin, float headingRad, Color color)
        {
            var dir = new Vector3(Mathf.Sin(headingRad), 0f, Mathf.Cos(headingRad));
            var side = new Vector3(dir.Z, 0f, -dir.X);
            var tip = origin + dir * HeadingArrowLength;
            DebugDraw.DrawLine(origin, tip, color, 0f, false);
            DebugDraw.DrawLine(tip, tip - dir * 24f + side * 12f, color, 0f, false);
            DebugDraw.DrawLine(tip, tip - dir * 24f - side * 12f, color, 0f, false);
        }

        private void OnFootstep(int side, Vector3 world, float speed)
        {
            _steps.Add(new StepMark { Position = world, Until = Time.GameTime + StepMarkerSeconds });
        }

        private void DrawStepMarkers()
        {
            float now = Time.GameTime;
            float groundY = Actor.Position.Y + 2f;
            for (int i = _steps.Count - 1; i >= 0; i--)
            {
                if (now > _steps[i].Until)
                {
                    _steps.RemoveAt(i);
                    continue;
                }
                var p = _steps[i].Position;
                var ground = new Vector3(p.X, groundY, p.Z);
                DebugDraw.DrawCircle(ground, new Float3(0f, 1f, 0f), 12f, Color.Yellow, 0f, false);
                DebugDraw.DrawLine(ground, p + new Vector3(0f, 25f, 0f), Color.Yellow, 0f, false);
            }
        }

        private void DrawLabel()
        {
            string text = $"{_gasp.LastClipName} cost={_gasp.LastCost:F0}"
                          + $" warp={_gasp.LastSteerWarpDegrees:F0}deg"
                          + $" speed={_gasp.LastPlanarVelocity.Length:F0}";
            DebugDraw.DrawText(text, Actor.Position + new Vector3(0f, 200f, 0f), Color.White, 16, 0f, 1f);
        }

        public override void OnDestroy()
        {
            if (_gasp != null)
                _gasp.Footstep -= OnFootstep;
        }
    }
}
