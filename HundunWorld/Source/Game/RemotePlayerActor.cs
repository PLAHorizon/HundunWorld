using System;
using FlaxEngine;
using MetaHuman;
using MetaHuman.Runtime;

namespace HundunWorld.Game
{
    /// <summary>
    /// 远程角色 Actor 脚本：挂载在动态生成的远程角色 Actor 上，
    /// 提供实体 ID 关联、名称标签显示、基础动画状态控制等功能。
    /// </summary>
    public class RemotePlayerActor : Script
    {
        private ulong _entityId;
        private string _playerName = "Player";
        private AnimatedModel _animatedModel;
        private bool _metaHumanApplied;

        /// <summary>服务器分配的实体唯一 ID。</summary>
        public ulong EntityId
        {
            get => _entityId;
            set => _entityId = value;
        }

        /// <summary>玩家显示名称。</summary>
        public string PlayerName
        {
            get => _playerName;
            set => _playerName = value ?? "Player";
        }

        /// <summary>关联的动画模型。</summary>
        public AnimatedModel AnimatedModel => _animatedModel;

        public override void OnStart()
        {
            // AnimatedModel 在 Flax 中是 Actor 类型，在子 Actor 中查找
            for (int i = 0; i < Actor.ChildrenCount; i++)
            {
                var child = Actor.GetChild(i);
                if (child is AnimatedModel am)
                {
                    _animatedModel = am;
                    break;
                }
            }

            Debug.Log($"[RemotePlayerActor] 初始化完成: EntityId={_entityId}, Name={_playerName}");
        }

        public override void OnEnable()
        {
        }

        /// <summary>
        /// 把服务端下发的 MetaHuman 外观套到本 Actor：只认 Ada 身体（头部形状的承载骨在脸骨上），
        /// 人形占位身体直接跳过并留痕——硬套会让主姿态链跨骨架，引擎会断言。
        /// </summary>
        public bool ApplyMetaHumanLook(MetaHumanLook look, ulong characterId, int hairColorIndex = 0)
        {
            if (look == null || _metaHumanApplied)
            {
                return _metaHumanApplied;
            }

            AnimatedModel body = _animatedModel ?? Actor.FindActor<AnimatedModel>() ?? Actor.GetChild<AnimatedModel>();
            if (body?.SkinnedModel == null || !body.SkinnedModel.IsLoaded)
            {
                Debug.LogWarning($"[RemotePlayerActor] 远端外观跳过：角色 {characterId} 没有已加载的身体模型");
                return false;
            }

            if (body.SkinnedModel.Path.IndexOf("Ada_BodyPreview", StringComparison.OrdinalIgnoreCase) < 0)
            {
                Debug.LogWarning($"[RemotePlayerActor] 远端外观跳过：角色 {characterId} 的身体不是 MetaHuman 承载模型（{body.SkinnedModel.Path}）");
                return false;
            }

            // 远端身体同样吃空动画图：用同一套程序化待机（有真实动画时自动让位）。
            if (body.GetScript<Character.ProceduralIdleDriver>() == null)
            {
                body.AddScript<Character.ProceduralIdleDriver>();
            }

            AnimatedModel head = null;
            try
            {
                var headModel = Content.Load<SkinnedModel>(MetaHuman.Runtime.MetaHumanAssetUtil.ResolveProjectPath(Equipment.EquipmentDatabase.HeadModelPath), 10000.0);
                if (headModel != null && headModel.IsLoaded)
                {
                    head = Actor.AddChild<AnimatedModel>();
                    head.Name = $"RemoteHead_{characterId}";
                    head.SkinnedModel = headModel;
                    head.LocalPosition = Vector3.Zero;
                    head.LocalScale = Vector3.One;
                    // 490 骨头资产同理：关掉逐骨运动模糊，避免首次蒙皮时撞 AnimatedModel.cpp:82（#76）。
                    head.PerBoneMotionBlur = false;
                    head.AddScript<Character.WorldHeadMasterPose>();
                }
                else
                {
                    Debug.LogWarning($"[RemotePlayerActor] 远端头模型未就绪：角色 {characterId}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RemotePlayerActor] 远端头挂载失败（角色 {characterId}）：{ex.Message}");
            }

            if (head != null)
            {
                try
                {
                    var hairModel = Content.Load<Model>(MetaHuman.Runtime.MetaHumanAssetUtil.ResolveProjectPath(Equipment.EquipmentDatabase.HairCardsModelPath), 10000.0);
                    if (hairModel != null && hairModel.IsLoaded)
                    {
                        var hair = head.AddChild<StaticModel>();
                        hair.Name = $"RemoteHair_{characterId}";
                        hair.Model = hairModel;

                        var palette = UI.Ink.Pages.CharacterCreation.CcFacePresets.HairColors;
                        var hairColor = palette.Length > 0
                            ? palette[Mathf.Clamp(hairColorIndex, 0, palette.Length - 1)]
                            : new Color(0.10f, 0.06f, 0.035f);
                        var hairBase = hair.GetMaterial(0);
                        if (hairBase != null && hairBase.CreateVirtualInstance() is MaterialInstance hairInstance)
                        {
                            hairInstance.SetParameterValue("HairColor", hairColor);
                            hair.SetMaterial(0, hairInstance);
                        }

                        // 发型随 look 值走；Owner 在下方才挂上 MetaHumanCharacter，绑定器会懒解析。
                        hair.AddScript<Character.MetaHumanHairBinder>();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[RemotePlayerActor] 远端头发挂载失败（角色 {characterId}）：{ex.Message}");
                }
            }

            var mh = Actor.GetScript<MetaHumanCharacter>() ?? Actor.AddScript<MetaHumanCharacter>();
            if (mh == null)
            {
                Debug.LogWarning($"[RemotePlayerActor] 无法添加 MetaHumanCharacter：角色 {characterId}");
                return false;
            }

            mh.CharacterRole = "remote";
            mh.ApplyOnStart = false;
            mh.TemplateIds = new[] { "metahuman.face_archetype", "metahuman.body_base" };
            mh.TargetModels = head != null ? new[] { body, head } : new[] { body };
            mh.EnableBoneScaleChannel = false;
            if (!mh.IsLoaded)
            {
                mh.Load();
            }

            bool applied = mh.LoadLook(look);
            _metaHumanApplied = applied;

            // 远端也要眨眼（与本地玩家同一套面部空闲动画）；张嘴 demo 关掉，避免隔着距离看到嘴一直动。
            if (applied && head != null)
            {
                var face = head.GetScript<MetaHumanFaceAnimator>() ?? head.AddScript<MetaHumanFaceAnimator>();
                if (face != null)
                {
                    face.Owner = mh;
                    face.AutoBlink = true;
                    face.AutoPlayDemo = false;
                    face.LinkMirrors = true;
                }

                // 外部追踪入口与本地玩家同款：默认关闭。
                var tracking = head.GetScript<MetaHumanFaceTrackingReceiver>()
                               ?? head.AddScript<MetaHumanFaceTrackingReceiver>();
                if (tracking != null)
                {
                    tracking.Animator = face;
                }
            }
            return applied;
        }

        public override void OnDisable()
        {
        }

        /// <summary>
        /// 设置角色位置（由 FlaxActorSyncSystem 每帧调用）。
        /// </summary>
        public void SetWorldPosition(float x, float y, float z)
        {
            Actor.Position = new Vector3(x, y, z);
        }

        /// <summary>
        /// 设置角色旋转（由 FlaxActorSyncSystem 调用）。
        /// </summary>
        public void SetWorldRotation(float pitch, float yaw, float roll)
        {
            Actor.Orientation = Quaternion.Euler(pitch, yaw, roll);
        }

        /// <summary>
        /// 获取角色当前位置。
        /// </summary>
        public Vector3 GetWorldPosition()
        {
            return Actor.Position;
        }

        /// <summary>
        /// 检查 AnimatedModel 资源是否已就绪，避免在 SkinnedModel 或 AnimationGraph 未加载时访问参数。
        /// </summary>
        private bool CanAccessAnimationParameters()
        {
            if (_animatedModel == null) return false;
            if (_animatedModel.SkinnedModel == null || !_animatedModel.SkinnedModel.IsLoaded) return false;
            if (_animatedModel.AnimationGraph == null || !_animatedModel.AnimationGraph.IsLoaded) return false;
            return true;
        }

        /// <summary>
        /// 设置动画参数（IsWalking 等）。
        /// </summary>
        public void SetAnimationBool(string paramName, bool value)
        {
            if (!CanAccessAnimationParameters()) return;
            try
            {
                var param = _animatedModel.GetParameter(paramName);
                if (param != null)
                {
                    param.Value = value;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RemotePlayerActor] 设置动画参数 {paramName} 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置动画浮点参数。
        /// </summary>
        public void SetAnimationFloat(string paramName, float value)
        {
            if (!CanAccessAnimationParameters()) return;
            try
            {
                var param = _animatedModel.GetParameter(paramName);
                if (param != null)
                {
                    param.Value = value;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RemotePlayerActor] 设置动画参数 {paramName} 失败: {ex.Message}");
            }
        }
    }
}
