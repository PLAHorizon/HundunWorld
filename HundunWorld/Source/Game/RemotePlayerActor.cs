using System;
using FlaxEngine;

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
