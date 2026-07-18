using FlaxEngine;
using HundunWorld.Game.Equipment;

namespace HundunWorld.Game.Character
{
    /// <summary>
    /// 角色挂载槽位，保存已挂载的 Actor 及其局部偏移，并在每帧同步到目标骨骼。
    /// </summary>
    public class CharacterAttachmentSlot
    {
        public EquipmentSlot Slot;
        public Actor AttachedActor;
        public string BoneName;
        public Vector3 LocalOffset;
        public Quaternion LocalRotation;
        public Vector3 LocalScale;

        /// <summary>
        /// 装备类型，用于管理器按类型卸载。
        /// </summary>
        public EquipmentType Type;

        public CharacterAttachmentSlot(EquipmentSlot slot, Actor attachedActor, string boneName, Vector3 offset, Quaternion rotation, Vector3 scale)
        {
            Slot = slot;
            AttachedActor = attachedActor;
            BoneName = boneName;
            LocalOffset = offset;
            LocalRotation = rotation;
            LocalScale = scale;
        }

        /// <summary>
        /// 将挂载 Actor 同步到指定 AnimatedModel 的骨骼上。
        /// </summary>
        public void SyncToBone(AnimatedModel animatedModel)
        {
            if (AttachedActor == null) return;

            // 安全校验：避免在 SkinnedModel 未加载时调用 GetNodeTransformation 触发断言
            if (animatedModel == null || animatedModel.SkinnedModel == null || !animatedModel.SkinnedModel.IsLoaded)
            {
                // 如果骨骼模型未就绪，至少保持子 Actor 的本地变换，避免异常
                if (string.IsNullOrEmpty(BoneName))
                {
                    AttachedActor.LocalPosition = LocalOffset;
                    AttachedActor.LocalOrientation = LocalRotation;
                    AttachedActor.LocalScale = LocalScale;
                }
                return;
            }

            if (string.IsNullOrEmpty(BoneName))
            {
                AttachedActor.LocalPosition = LocalOffset;
                AttachedActor.LocalOrientation = LocalRotation;
                AttachedActor.LocalScale = LocalScale;
                return;
            }

            animatedModel.GetNodeTransformation(BoneName, out Matrix worldMatrix, true);
            var worldPosition = worldMatrix.TranslationVector;
            var worldRot = Quaternion.RotationMatrix(worldMatrix);
            var worldScale = worldMatrix.ScaleVector;

            AttachedActor.Position = worldPosition + LocalOffset * worldRot;
            AttachedActor.Orientation = worldRot * LocalRotation;
            AttachedActor.Scale = worldScale * LocalScale;
        }
    }
}
