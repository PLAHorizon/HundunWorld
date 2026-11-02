using System;
using System.IO;
using System.Text;
using FlaxEngine;

namespace HundunWorld.Game.Gasp
{
    /// <summary>
    /// 运行期自检：只在标记文件存在时执行（不污染正常游玩）。
    /// 标记：%APPDATA%/HundunWorld/gasp_selftest.on 或 &lt;Project&gt;/Saved/gasp_selftest.on
    /// 结果写到 &lt;Project&gt;/Saved/gasp_selftest.result.txt，并打到日志。
    /// </summary>
    public static class GaspSelfTest
    {
        private static bool _ran;

        public static void TryRun(GaspAnimator animator)
        {
            if (_ran || animator == null || !animator.IsReady)
                return;
            if (!HasMarker())
                return;
            _ran = true;
            Run(animator);
        }

        private static bool HasMarker()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string a = Path.Combine(appData, "HundunWorld", "gasp_selftest.on");
            string b = Path.Combine(Globals.ProjectFolder, "Saved", "gasp_selftest.on");
            return File.Exists(a) || File.Exists(b);
        }

        private static void Run(GaspAnimator animator)
        {
            var sb = new StringBuilder();
            int pass = 0, fail = 0;
            void Check(string name, bool ok, string detail = "")
            {
                if (ok) pass++;
                else fail++;
                sb.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name + (string.IsNullOrEmpty(detail) ? "" : " | " + detail));
            }

            try
            {
                var db = animator.Database;
                var mdb = animator.MotionDatabase;
                var skel = animator.Skeleton;

                Check("database.loaded", db != null && db.IsLoaded, db?.SourcePath ?? "<null>");
                Check("database.clips", db != null && db.Clips.Count >= 20, "clips=" + (db?.Clips.Count ?? 0));
                Check("skeleton.pelvis/feet", skel != null && skel.Pelvis >= 0 && skel.FootL >= 0 && skel.FootR >= 0);
                Check("features.poses", mdb != null && mdb.Poses.Count > 500, "poses=" + (mdb?.Poses.Count ?? 0));
                Check("channels.mapped", animator.MappedChannelCount > 60, "mapped=" + animator.MappedChannelCount);

                // 检索一致性：取每个地面剪辑中间帧的特征，去掉续播偏置后应返回同一剪辑（或同族）。
                int exact = 0, tested = 0, sameFamily = 0;
                var recent = new System.Collections.Generic.Dictionary<long, float>();
                for (int ci = 0; ci < db.Clips.Count; ci++)
                {
                    var clip = db.Clips[ci];
                    if (clip.Tag != GaspClipTag.Ground || clip.FrameCount < 3)
                        continue;
                    int first = mdb.ClipFirstPose[ci];
                    if (first < 0)
                        continue;
                    int frame = clip.FrameCount / 2;
                    var pose = mdb.Poses[first + frame];
                    mdb.Search(pose.Features, -1, 0f, 0f, 0f, recent, -10f, 0, out var best, out _);
                    tested++;
                    if (best.Valid && best.Clip == ci)
                        exact++;
                    if (best.Valid && FamilyOf(db.Clips[best.Clip].Name) == FamilyOf(clip.Name))
                        sameFamily++;
                }
                Check("search.sameClip", tested > 0 && exact >= tested / 2, $"exact={exact}/{tested}");
                Check("search.sameFamily", tested > 0 && sameFamily >= tested * 2 / 3, $"family={sameFamily}/{tested}");

                // 姿势覆盖：任意地面帧都应覆盖 pelvis 与双脚。
                int probeClip = -1;
                for (int i = 0; i < db.Clips.Count; i++)
                {
                    if (db.Clips[i].Tag == GaspClipTag.Ground && db.Clips[i].FrameCount > 2)
                    {
                        probeClip = i;
                        break;
                    }
                }
                bool covered = false;
                if (probeClip >= 0)
                {
                    var pose = new GaspPoseBuffer(skel.Count);
                    var scratch = new float[Mathf.Max(db.MaxStride, 1)];
                    GaspPoseUtil.SampleClip(db.Clips[probeClip], 0f, skel, pose, scratch);
                    covered = pose.Covered[skel.Pelvis] && pose.Covered[skel.FootL] && pose.Covered[skel.FootR];
                }
                Check("pose.coversCore", covered);

                Check("runtime.poseApplied", animator.PoseApplyCount > 0, "apply=" + animator.PoseApplyCount);
                Check("runtime.lastClip", !string.IsNullOrEmpty(animator.LastClipName), animator.LastClipName);

                sb.AppendLine($"summary pass={pass} fail={fail} clip={animator.LastClipName} apply={animator.PoseApplyCount}");
                string result = sb.ToString();
                Debug.Log("[GaspSelfTest]\n" + result);
                try
                {
                    string dir = Path.Combine(Globals.ProjectFolder, "Saved");
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "gasp_selftest.result.txt"), result, Encoding.UTF8);
                }
                catch
                {
                    // 结果已经进了日志；文件写失败不改变断言结论
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[GaspSelfTest] 异常: " + ex);
            }
        }

        private static string FamilyOf(string name)
        {
            if (name.IndexOf("Walk", StringComparison.OrdinalIgnoreCase) >= 0) return "walk";
            if (name.IndexOf("Run", StringComparison.OrdinalIgnoreCase) >= 0) return "run";
            if (name.IndexOf("Sprint", StringComparison.OrdinalIgnoreCase) >= 0) return "sprint";
            if (name.IndexOf("Turn", StringComparison.OrdinalIgnoreCase) >= 0) return "turn";
            if (name.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0) return "stop";
            if (name.IndexOf("Transition", StringComparison.OrdinalIgnoreCase) >= 0) return "transition";
            if (name.IndexOf("Secondary", StringComparison.OrdinalIgnoreCase) >= 0) return "idle";
            return "other";
        }
    }
}
