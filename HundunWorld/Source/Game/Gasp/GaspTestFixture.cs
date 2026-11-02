using System;
using System.IO;
using FlaxEngine;

namespace HundunWorld.Game.Gasp
{
    /// <summary>
    /// 运行期 GASP 夹具（仅标记文件存在时激活）：用代码装配 Ada 骨架 + 动画图 + GaspAnimator，
    /// 不经过编辑器属性面板，也不依赖登录/服务器。用于 PIE 自动化取证。
    /// 标记：%APPDATA%/HundunWorld/gasp_fixture.on；移动测试：gasp_fixture_move.on
    /// </summary>
    public class GaspTestFixture : Script
    {
        public string BodyPath = "Content/Character/MetaHuman_Conform_Topology/Ada_BodyPreview4.flax";
        public string GraphPath = "Content/Character/Models/Animation Graph.flax";
        public string ResultPath = "Saved/gasp_fixture.result.txt";
        public bool MoveTest = true;
        public bool DebugLog = true;

        private AnimatedModel _model;
        private GaspAnimator _gasp;
        private Actor _root;
        private AnimatedModel _remoteModel;
        private GaspAnimator _remoteGasp;
        private Actor _remoteRoot;
        private GaspTraversalDriver _traversal;
        private bool _traversalRequested;
        private bool _traversalDoneLogged;
        private float _time;
        private bool _turnTest;
        private int _phase = -1;
        private float _phaseTime;
        private readonly System.Text.StringBuilder _log = new System.Text.StringBuilder();
        private bool _active;

        public override void OnStart()
        {
            _active = HasMarker("gasp_fixture.on");
            if (!_active)
            {
                Enabled = false;
                return;
            }
            MoveTest = HasMarker("gasp_fixture_move.on") || MoveTest;
            _turnTest = HasMarker("gasp_fixture_turn.on");
#if FLAX_EDITOR
            if (HasMarker("gasp_asset_bake.on"))
            {
                bool baked = GaspDataAssetBaker.Bake("Content/Gasp/gasp_anims.bin", GaspAnimationDatabase.DefaultContentAssetPath);
                Line($"asset bake ok={baked} path={GaspAnimationDatabase.DefaultContentAssetPath}");
            }
#endif
            try
            {
                Spawn();
                Line("spawn ok");
            }
            catch (Exception ex)
            {
                Line("spawn FAILED " + ex.GetType().Name + ": " + ex.Message);
                Debug.LogError("[GaspFixture] " + ex);
            }
        }

        private void Spawn()
        {
            var scene = FindScene();
            _root = new EmptyActor { Name = "GaspFixtureRoot" };
            Level.SpawnActor(_root, scene);
            _root.Position = new Vector3(0, 0, 0);

            _model = new AnimatedModel { Name = "GaspFixtureModel" };
            Level.SpawnActor(_model, _root);
            // Ada 有 282 根骨；PerBoneMotionBlur 开启时骨骼缓冲首块 16KB 不够（引擎 AnimatedModel.cpp:82 断言）。
            _model.PerBoneMotionBlur = false;
            _model.SkinnedModel = Content.Load<SkinnedModel>(BodyPath, 15000.0);
            _model.AnimationGraph = Content.Load<AnimationGraph>(GraphPath, 15000.0);
            _model.UpdateWhenOffscreen = true;
            _model.UpdateMode = AnimatedModel.AnimationUpdateMode.EveryUpdate;
            _model.IsActive = true;
            _model.Scale = Vector3.One;

            bool graphOnly = HasMarker("gasp_fixture_graph.on");
            if (graphOnly)
            {
                // 诊断模式：只播放动画图 + ClipFrameFix，不跑 GASP。
                HundunWorld.Game.Character.MetaHumanClipFrameFix.Attach(_model);
                Line("graph-only mode: GASP disabled, ClipFrameFix attached");
            }
            else
            {
                _gasp = _root.AddScript<GaspAnimator>();
                if (_gasp != null)
                {
                    _root.AddScript<GaspFoley>();
                    if (HasMarker("gasp_debug.on"))
                        _root.AddScript<GaspDebugDraw>();
                    _gasp.DebugLog = DebugLog;
                    if (HasMarker("gasp_fixture_nofoot.on"))
                        _gasp.EnableFootPlacement = false;
                    if (HasMarker("gasp_fixture_graphwrite.on"))
                    {
                        _gasp.WriteGraphPoseOnly = true;
                        _gasp.EnableFootPlacement = false;
                        Line("graph-write mode: SetCurrentPose receives graph pose");
                    }
                }
            }

            // 目视取证：相机跟随角色，另加一盏方向光（测试场景没有灯光）。
            var cam = new Camera { Name = "GaspFixtureCam", FieldOfView = 45 };
            Level.SpawnActor(cam, _root);
            cam.LocalPosition = new Vector3(0, 170, -450);
            cam.LocalOrientation = Quaternion.Identity; // Flax 相机沿 +Z 看
            cam.IsActive = true;
            Camera.OverrideMainCamera = cam;

            var light = new DirectionalLight { Name = "GaspFixtureSun" };
            Level.SpawnActor(light, scene);
            light.EulerAngles = new Vector3(-50, -30, 0);
            light.IsActive = true;

            Line("root=" + _root.Name + " model=" + (_model.SkinnedModel != null ? _model.SkinnedModel.Path : "null"));

            if (HasMarker("gasp_fixture_remote.on"))
                SpawnRemoteFixture(scene);
            if (HasMarker("gasp_fixture_traverse.on"))
                SetUpTraversalTest(scene);
        }

        private void SetUpTraversalTest(FlaxEngine.Scene scene)
        {
            var platform = new BoxCollider
            {
                Name = "GaspTraversalPlatform",
                Size = new Vector3(300, 100, 300),
                IsActive = true,
            };
            Level.SpawnActor(platform, scene);
            platform.Position = new Vector3(0, 50, 300);
            platform.IsActive = true;
            Line($"platform spawned name={platform.Name} scene={(platform.Scene != null ? platform.Scene.Name : "<null>")}"
                 + $" active={platform.IsActive} trigger={platform.IsTrigger} layer={platform.Layer}"
                 + $" pos=({platform.Position.X:F0},{platform.Position.Y:F0},{platform.Position.Z:F0})"
                 + $" size=({platform.Size.X:F0},{platform.Size.Y:F0},{platform.Size.Z:F0})");

            _traversal = _root.AddScript<GaspTraversalDriver>();
            if (_traversal != null)
            {
                _traversal.AutoApplyMovement = true;
                _traversal.Enable = true;
                // PIE scenes 场景里物理查询不可用（平台射线全 miss），用假探测器验证驱动/动画/扭曲链。
                _traversal.ProbeOverride = new GaspFakeLedgeProbe();
            }
            Line("traversal test ready");
        }

        private sealed class GaspFakeLedgeProbe : IGaspTraversalProbe
        {
            public bool TryFindLedge(Vector3 feetWorld, Vector3 moveDirWorld, out GaspLedgeInfo info)
            {
                info = default;
                if (feetWorld.Z < 40f || feetWorld.Z > 175f)
                    return false;
                info = new GaspLedgeInfo
                {
                    Found = true,
                    WallPoint = new Vector3(0, 70, 150),
                    TopPoint = new Vector3(0, 100, 170),
                    WallNormal = new Vector3(0, 0, -1),
                    Height = 100f,
                    Side = 1,
                };
                return true;
            }
        }

        private void SpawnRemoteFixture(FlaxEngine.Scene scene)
        {
            _remoteRoot = new EmptyActor { Name = "GaspFixtureRemote" };
            Level.SpawnActor(_remoteRoot, scene);
            _remoteRoot.Position = new Vector3(120, 0, 0);

            _remoteModel = new AnimatedModel { Name = "GaspFixtureRemoteModel" };
            Level.SpawnActor(_remoteModel, _remoteRoot);
            _remoteModel.PerBoneMotionBlur = false;
            _remoteModel.SkinnedModel = Content.Load<SkinnedModel>(BodyPath, 15000.0);
            _remoteModel.AnimationGraph = Content.Load<AnimationGraph>(GraphPath, 15000.0);
            _remoteModel.UpdateMode = AnimatedModel.AnimationUpdateMode.Auto;
            _remoteModel.IsActive = true;
            _remoteModel.Scale = Vector3.One;

            _remoteGasp = _remoteRoot.AddScript<GaspAnimator>();
            if (_remoteGasp != null)
            {
                _remoteGasp.RemoteMode = true;
                _remoteGasp.EnableFootPlacement = false;
                _remoteGasp.DebugLog = DebugLog;
            }
            Line("remote fixture spawned");
        }

        public override void OnUpdate()
        {
            if (!_active || _root == null)
                return;
            float dt = Mathf.Max(Time.DeltaTime, 0.0001f);
            _time += dt;
            _phaseTime += dt;

            if (!MoveTest)
                return;

            if (_traversal != null)
            {
                UpdateTraversalTest(dt);
                return;
            }

            // 0-4s 静止；4-12s 走 500cm/s；12-20s 跑 1000；20-23s 停；23-30s 再走
            int phase = _time < 4f ? 0 : _time < 12f ? 1 : _time < 20f ? 2 : _time < 23f ? 3 : 4;
            if (phase != _phase)
            {
                _phase = phase;
                _phaseTime = 0f;
                Line($"phase={phase} t={_time:F1} clip={(_gasp != null ? _gasp.LastClipName : "?")}");
            }

            float speed = phase == 1 ? 500f : phase == 2 ? 1000f : phase == 4 ? 500f : 0f;
            if (speed > 0f)
            {
                if (_turnTest)
                {
                    // 圆周（半径 300cm）+ 面朝切线：给转向扭曲一个确定的持续输入。
                    float ang = _time * (speed / 300f);
                    _root.Position = new Vector3(Mathf.Sin(ang) * 300f, 0f, Mathf.Cos(ang) * 300f);
                    _root.EulerAngles = new Vector3(0f, ang * Mathf.RadiansToDegrees, 0f);
                }
                else
                {
                    var p = _root.Position;
                    p.Z += speed * dt;
                    _root.Position = p;
                }
            }

            // 远端跟随体：位置随领导者平移（模拟网络插值后的位置），用于验证 RemoteMode 选库/写回。
            if (_remoteRoot != null)
            {
                var p = _root.Position;
                _remoteRoot.Position = p + new Vector3(120, 0, 0);
            }
            if (_time > 30f && _time - dt <= 30f)
            {
                Line("done clip=" + (_gasp != null ? _gasp.LastClipName : "?")
                     + " apply=" + (_gasp != null ? _gasp.PoseApplyCount : 0)
                     + " ready=" + (_gasp != null && _gasp.IsReady)
                     + " | remote clip=" + (_remoteGasp != null ? _remoteGasp.LastClipName : "<none>")
                     + " apply=" + (_remoteGasp != null ? _remoteGasp.PoseApplyCount : 0)
                     + " ready=" + (_remoteGasp != null && _remoteGasp.IsReady)
                     + " | traversal=" + (_traversal != null ? _traversal.LastClip + "/" + _traversal.LastMessage : "<none>"));
                WriteResult();
            }
        }

        private void UpdateTraversalTest(float dt)
        {
            var ctl = _traversal.Controller;
            if (ctl == null)
                return;

            // 先以 150cm/s 走向平台（前立面 z=150），0.8s 时请求翻越。
            if (!_traversalRequested && _time < 1.5f && !ctl.Active)
            {
                var p = _root.Position;
                p.Z += 150f * dt;
                _root.Position = p;
            }
            if (!_traversalRequested && _time >= 0.8f)
            {
                _traversalRequested = true;
                bool ok = _traversal.TryBeginTraversal();
                Line($"traversal request ok={ok} msg={_traversal.LastMessage} clip={_traversal.LastClip}"
                     + $" pos=({_root.Position.X:F0},{_root.Position.Y:F0},{_root.Position.Z:F0})");
            }
            if (_traversalRequested && !_traversalDoneLogged && !ctl.Active && _time > 1.2f)
            {
                _traversalDoneLogged = true;
                var p = _root.Position;
                Line($"traversal done clip={_traversal.LastClip} msg={_traversal.LastMessage}"
                     + $" pos=({p.X:F0},{p.Y:F0},{p.Z:F0})"
                     + $" start=({_traversal.LastStartPosition.X:F0},{_traversal.LastStartPosition.Y:F0},{_traversal.LastStartPosition.Z:F0})"
                     + $" end=({_traversal.LastEndPosition.X:F0},{_traversal.LastEndPosition.Y:F0},{_traversal.LastEndPosition.Z:F0})");
                WriteResult();
            }
        }

        private static FlaxEngine.Scene FindScene()
        {
            for (int i = 0; i < Level.ScenesCount; i++)
            {
                var s = Level.GetScene(i);
                if (s != null && s.Name != "TransitionScene")
                    return s;
            }
            throw new InvalidOperationException("没有可用场景");
        }

        private void Line(string msg)
        {
            string s = "[GaspFixture] " + msg;
            _log.AppendLine(s);
            Debug.Log(s);
        }

        private void WriteResult()
        {
            try
            {
                string dir = Path.Combine(Globals.ProjectFolder, "Saved");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(Globals.ProjectFolder, ResultPath), _log.ToString());
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GaspFixture] 写结果失败: " + ex.Message);
            }
        }

        public override void OnDestroy()
        {
            if (_root != null)
                _root.IsActive = false;
            base.OnDestroy();
        }

        private static bool HasMarker(string name)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string a = Path.Combine(appData, "HundunWorld", name);
            string b = Path.Combine(Globals.ProjectFolder, "Saved", name);
            return File.Exists(a) || File.Exists(b);
        }
    }
}
