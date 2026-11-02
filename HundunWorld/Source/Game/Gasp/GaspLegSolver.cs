using FlaxEngine;

namespace HundunWorld.Game.Gasp
{
    /// <summary>
    /// 组件空间（模型空间）双骨解析腿 IK。
    /// 引擎的 NodesPose 是组件空间矩阵；这里直接在组件矩阵上旋转大腿/小腿，
    /// 再用相对矩阵把子树重新铺开，因此骨长/层级不会被破坏。
    /// </summary>
    public static class GaspLegSolver
    {
        /// <summary>从组件矩阵提取“相对父节点”的矩阵，便于改一个节点后重铺子树。</summary>
        public static void BuildRelative(Matrix[] comp, GaspSkeleton skel, Matrix[] rel)
        {
            for (int i = 0; i < skel.Count; i++)
            {
                int p = skel.Parents[i];
                rel[i] = p >= 0 ? comp[i] * Matrix.Invert(comp[p]) : comp[i];
            }
        }

        /// <summary>用相对矩阵把整棵骨架重铺成组件空间（父在前）。</summary>
        public static void Propagate(Matrix[] comp, GaspSkeleton skel, Matrix[] rel)
        {
            for (int i = 0; i < skel.Count; i++)
            {
                int p = skel.Parents[i];
                comp[i] = p >= 0 ? rel[i] * comp[p] : rel[i];
            }
        }

        /// <summary>
        /// 绕 node 自身原点旋转，使 child 关节指向 target。mode 0..3 用于实测乘法顺序：
        /// bit0=旋转右乘(rot*rDelta)，否则左乘；bit1=相对矩阵右乘(new*invParent)，否则左乘。
        /// </summary>
        public static bool RotateComponentNode(Matrix[] comp, GaspSkeleton skel, Matrix[] rel,
            int node, int child, Vector3 target, int mode, out float error)
        {
            error = float.MaxValue;
            Vector3 origin = comp[node].TranslationVector;
            Vector3 cur = comp[child].TranslationVector - origin;
            Vector3 des = target - origin;
            if (cur.LengthSquared < 1e-6f || des.LengthSquared < 1e-6f)
                return false;

            Vector3 c = Vector3.Normalize(cur);
            Vector3 d = Vector3.Normalize(des);
            Vector3 axis = Vector3.Cross(c, d);
            float sin = axis.Length;
            float cos = Mathf.Clamp(Vector3.Dot(c, d), -1f, 1f);
            Matrix rDelta = sin < 1e-5f
                ? Matrix.Identity
                : Matrix.RotationAxis(axis / sin, Mathf.Atan2(sin, cos));

            Matrix rot = comp[node];
            rot.M41 = 0f;
            rot.M42 = 0f;
            rot.M43 = 0f;
            Matrix rotNew = (mode & 1) != 0 ? rot * rDelta : rDelta * rot;
            Matrix newComp = rotNew;
            newComp.M41 = origin.X;
            newComp.M42 = origin.Y;
            newComp.M43 = origin.Z;
            comp[node] = newComp;

            int parent = skel.Parents[node];
            Matrix parentInv = parent >= 0 ? Matrix.Invert(comp[parent]) : Matrix.Identity;
            rel[node] = (mode & 2) != 0 ? newComp * parentInv : parentInv * newComp;
            Propagate(comp, skel, rel);
            error = (comp[child].TranslationVector - target).Length;
            return true;
        }

        /// <summary>
        /// 解一条腿：保持大腿/小腿骨长，让脚踝组件位置到达 target。
        /// 膝盖弯曲方向沿用当前膝盖相对髋-目标线的偏移；退化时朝模型前方（-Z）。
        /// </summary>
        public static bool SolveLegComponent(Matrix[] comp, GaspSkeleton skel, int thigh, int calf, int foot,
            Vector3 targetAnkle, int mode, out float kneeError, out float ankleError)
        {
            kneeError = ankleError = float.MaxValue;
            if (thigh < 0 || calf < 0 || foot < 0)
                return false;

            var rel = new Matrix[skel.Count];
            BuildRelative(comp, skel, rel);

            Vector3 hip = comp[thigh].TranslationVector;
            Vector3 knee = comp[calf].TranslationVector;
            Vector3 ankle = comp[foot].TranslationVector;
            float l1 = (knee - hip).Length;
            float l2 = (ankle - knee).Length;
            if (l1 < 1e-3f || l2 < 1e-3f)
                return false;

            Vector3 toTarget = targetAnkle - hip;
            float dist = toTarget.Length;
            if (dist < 1e-4f)
                return false;
            Vector3 dir = toTarget / dist;
            float d = Mathf.Clamp(dist, Mathf.Abs(l1 - l2) + 1e-3f, (l1 + l2) * 0.999f);

            Vector3 pole = (knee - hip) - dir * Vector3.Dot(knee - hip, dir);
            if (pole.LengthSquared < 1e-4f)
            {
                pole = Vector3.Cross(dir, Vector3.Up);
                if (pole.LengthSquared < 1e-4f)
                    pole = Vector3.Backward;
                if (Vector3.Dot(pole, Vector3.Forward) < 0f)
                    pole = -pole;
            }
            pole.Normalize();

            float a = (l1 * l1 - l2 * l2 + d * d) / (2f * d);
            float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - a * a));
            Vector3 kneeTarget = hip + dir * a + pole * h;

            Matrix footRot = comp[foot];
            footRot.M41 = 0f;
            footRot.M42 = 0f;
            footRot.M43 = 0f;

            RotateComponentNode(comp, skel, rel, thigh, calf, kneeTarget, mode, out kneeError);
            RotateComponentNode(comp, skel, rel, calf, foot, targetAnkle, mode, out ankleError);

            // 脚跟随小腿移动，但世界朝向保持（脚不跟着小腿乱转）。
            Vector3 footPos = comp[foot].TranslationVector;
            Matrix footNew = footRot;
            footNew.M41 = footPos.X;
            footNew.M42 = footPos.Y;
            footNew.M43 = footPos.Z;
            comp[foot] = footNew;
            int footParent = skel.Parents[foot];
            Matrix parentInv = footParent >= 0 ? Matrix.Invert(comp[footParent]) : Matrix.Identity;
            rel[foot] = footNew * parentInv;
            Propagate(comp, skel, rel);

            return true;
        }

        /// <summary>根据双脚目标与当前脚高，返回骨盆应下沉的量（≤0，厘米）。</summary>
        public static float ComputePelvisDrop(Vector3 ankleL, Vector3 targetL, Vector3 ankleR, Vector3 targetR, float maxDrop)
        {
            float drop = 0f;
            drop = Mathf.Min(drop, targetL.Y - ankleL.Y);
            drop = Mathf.Min(drop, targetR.Y - ankleR.Y);
            return Mathf.Clamp(drop, -maxDrop, 0f);
        }

        /// <summary>
        /// 让脚掌“上方向”对齐地面法线（保留 yaw），最大倾角受限；绕脚自身原点旋转并重铺子树。
        /// Ada 脚骨局部 X 轴在组件空间里朝上（局部 Y 是横向、局部 Z 是脚背朝向），
        /// 用错轴会让脚被拧到侧向 90°。使用与 SolveLegComponent 相同的矩阵乘法顺序（mode 3）。
        /// </summary>
        public static float AlignFootToGround(Matrix[] comp, GaspSkeleton skel, int foot, Vector3 groundNormal, float maxAngleDeg)
        {
            if (foot < 0 || groundNormal.LengthSquared < 1e-6f)
                return 0f;
            groundNormal.Normalize();

            Matrix rot = comp[foot];
            rot.M41 = 0f;
            rot.M42 = 0f;
            rot.M43 = 0f;
            Vector3 up = Vector3.Transform(Vector3.Right, rot);
            if (up.LengthSquared < 1e-6f)
                return 0f;
            up.Normalize();

            Vector3 axis = Vector3.Cross(up, groundNormal);
            float sin = axis.Length;
            float cos = Mathf.Clamp(Vector3.Dot(up, groundNormal), -1f, 1f);
            float angle = Mathf.Atan2(sin, cos);
            if (angle < 1e-4f)
                return 0f;

            float maxAngle = Mathf.Max(0f, maxAngleDeg) * Mathf.DegreesToRadians;
            float applied = Mathf.Min(angle, maxAngle);
            if (applied <= 1e-4f)
                return 0f;

            Quaternion qDelta = Quaternion.RotationAxis(axis / sin, applied);
            Matrix rDelta = Matrix.RotationQuaternion(qDelta);
            Matrix rotNew = rot * rDelta;
            Vector3 origin = comp[foot].TranslationVector;
            Matrix newComp = rotNew;
            newComp.M41 = origin.X;
            newComp.M42 = origin.Y;
            newComp.M43 = origin.Z;

            var rel = new Matrix[skel.Count];
            BuildRelative(comp, skel, rel);
            comp[foot] = newComp;
            int parent = skel.Parents[foot];
            Matrix parentInv = parent >= 0 ? Matrix.Invert(comp[parent]) : Matrix.Identity;
            rel[foot] = newComp * parentInv;
            Propagate(comp, skel, rel);
            return applied * Mathf.RadiansToDegrees;
        }

        /// <summary>在组件空间平移骨盆并重铺整副骨架（用于脚够不到地面时下沉）。</summary>
        public static void OffsetPelvis(Matrix[] comp, GaspSkeleton skel, float dy)
        {
            if (skel.Pelvis < 0 || Mathf.Abs(dy) < 1e-4f)
                return;
            var rel = new Matrix[skel.Count];
            BuildRelative(comp, skel, rel);
            Matrix pelvis = comp[skel.Pelvis];
            pelvis.M42 += dy;
            comp[skel.Pelvis] = pelvis;
            int parent = skel.Parents[skel.Pelvis];
            Matrix parentInv = parent >= 0 ? Matrix.Invert(comp[parent]) : Matrix.Identity;
            rel[skel.Pelvis] = pelvis * parentInv;
            Propagate(comp, skel, rel);
        }
    }
}
