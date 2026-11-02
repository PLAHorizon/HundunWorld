using System;
using System.Collections.Generic;
using FlaxEngine;

namespace HundunWorld.Game.Character
{
    /// <summary>
    /// ALS 剪辑里的 MetaHuman 通道按 UE Z-up 烘，Ada 骨架导入 Flax 后是 Y-up（厘米）。
    /// 直接播放会让整副骨架多转 −90°X：实测 pelvis 局部平移 (0, 2.94, −95.74)，而绑定姿态是 (0, 87.07, −2.09)。
    /// 动画更新后把每个节点的姿态矩阵换成 R_x(+90°)·M·R_x(−90°)，即绕 Actor 原点把姿态转正；
    /// 只对「挂了动画图的 Ada 身体」生效，不改资产、对无图状态零影响。
    /// </summary>
    public static class MetaHumanClipFrameFix
    {
        private static readonly Matrix ToModel = Matrix.RotationX(-Mathf.PiOverTwo);
        private static readonly Matrix ToClip = Matrix.RotationX(Mathf.PiOverTwo);
        private static readonly Dictionary<AnimatedModel, Action> Handlers = new Dictionary<AnimatedModel, Action>();
        private static readonly Dictionary<AnimatedModel, Matrix[]> Buffers = new Dictionary<AnimatedModel, Matrix[]>();
        private static readonly HashSet<AnimatedModel> Inside = new HashSet<AnimatedModel>();
        private static int _logged;

        public static void Attach(AnimatedModel model)
        {
            if (model == null || Handlers.ContainsKey(model))
            {
                return;
            }
            Action handler = () => Apply(model);
            Handlers[model] = handler;
            model.AnimationUpdated += handler;
        }

        public static void Detach(AnimatedModel model)
        {
            if (model == null)
            {
                return;
            }
            if (Handlers.TryGetValue(model, out Action handler))
            {
                model.AnimationUpdated -= handler;
                Handlers.Remove(model);
            }
            Buffers.Remove(model);
        }

        private static void Apply(AnimatedModel model)
        {
            if (model == null || model.SkinnedModel == null || model.AnimationGraph == null)
            {
                return;
            }
            if (!Inside.Add(model))
            {
                return;
            }
            try
            {
                int count = model.SkinnedModel.Nodes.Length;
                if (count <= 0)
                {
                    return;
                }
                if (!Buffers.TryGetValue(model, out Matrix[] pose) || pose.Length != count)
                {
                    pose = new Matrix[count];
                    Buffers[model] = pose;
                }
                model.GetCurrentPose(out pose, false);
                if (pose == null || pose.Length == 0)
                {
                    return;
                }
                for (int i = 0; i < pose.Length; i++)
                {
                    pose[i] = ToModel * pose[i] * ToClip;
                }
                model.SetCurrentPose(pose, false);
                if (_logged < 3)
                {
                    _logged++;
                    Debug.Log("[MetaHumanClipFrameFix] 姿态已按 Z-up→Y-up 转正: model="
                              + (model.Parent != null ? model.Parent.Name : model.Name) + ", nodes=" + count);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MetaHumanClipFrameFix] 姿态转正失败: " + ex.Message);
            }
            finally
            {
                Inside.Remove(model);
            }
        }
    }
}
