using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Character;
using NarrativePro.Core;
using NarrativePro.Items;
using NarrativePro.UnrealFramework;

namespace NarrativePro.World
{
    /// <summary>
    /// 可攀爬契约。对应 UE5 IClimbableActor 接口（NarrativeCharacterMovement.h:75-97）：
    /// 攀爬物可覆盖攀爬动画 Override 层标签、在初次跳抓时校准 warp 属性、
    /// 并在攀爬过程中持续给出下一段攀爬变换。
    /// UE5 的 Execute_AdjustInitialWarpProps / Execute_TryFindClimbTransform 为 BlueprintNativeEvent，
    /// 端口改为 C# 接口方法；语义保持一致（返回 false 表示中止攀爬）。
    /// </summary>
    public interface IClimbable
    {
        /// <summary>进入攀爬时应施加的动画 Override 层标签。对应 UE5 GetClimbOverrideTag。</summary>
        GameplayTag GetClimbOverrideTag();

        /// <summary>
        /// 从给定位置/朝向判断角色此刻可以抓住本攀爬物（侧面/高度/距离判定）。
        /// UE5 用 TraceChannel_NarrativeClimbable（NarrativeArsenal.h:15）前向扫描达成同样门禁。
        /// </summary>
        bool CanClimbFrom(Vector3 characterLocation, Vector3 facing, float maxDistance);

        /// <summary>
        /// 初次跳抓时把「起始 ledge」warp 属性对准攀爬物（如梯子按抓握高度吸附横档）。
        /// 对应 UE5 AdjustInitialWarpProps；返回 false 中止本次攀爬。
        /// </summary>
        bool AdjustInitialWarpProps(
            AttachWarpProps props,
            Vector3 initialHitPoint,
            Vector3 initialHitNormal,
            NarrativeCharacter character,
            bool pressedJump,
            Float2 inputVector,
            float optionalBlendInTime);

        /// <summary>
        /// 攀爬期间按输入推进目标变换。对应 UE5 TryFindClimbTransform（NarrativeCharacterMovement.cpp:1527）。
        /// UE5 里该调用由蒙太奇/PoseSearch 特征通道逐帧驱动；Flax 1.12 无 PoseSearch
        /// （已核查 FlaxEngine.CSharp.xml：0 个 PoseSearch 类型），改由
        /// <see cref="NarrativeCharacterMovement"/> 的攀爬积分每帧以 deltaTime 调用。
        /// 返回 false 表示无可用支撑点（松手下坠）。
        /// </summary>
        bool TryFindClimbTransform(
            AttachWarpProps props,
            AttachWarpProps oldProps,
            NarrativeCharacter character,
            bool pressedJump,
            Float2 inputVector,
            float deltaTime);
    }

    /// <summary>
    /// 具体可攀爬物（梯子/墙面抓握带）。挂载在带碰撞的 Actor 上的 Script，
    /// 场景摆放约定：Actor 局部 Forward 为「背离墙面的外法线」，局部 Right 为横移轴，
    /// 局部 Up 为攀爬轴；EntryHeight/TopHeight 为局部 Z 高度。
    /// UE5 中同类数据来自扫描 TraceChannel_NarrativeClimbable 的几何推导
    /// （前向扫面 :1613 + 顶部下行扫 :1645，常数 15/80/120 端口保留同名取值），
    /// 本类把这些量做成显式可编辑字段。
    /// <para>
    /// 摆放有两条通路（缺陷5）：① 场景里给带碰撞的 Actor 挂本脚本（UE5 同款做法）；
    /// ② 无场景摆放时由 <c>Content/NarrativePro/Climbables/*.json</c> 数据驱动生成
    /// （<see cref="NarrativeClimbableSpawner"/>，与 <c>Spawners/NPCCrowdSpawner.cs</c> +
    /// <c>Content/NarrativePro/Crowd/*.json</c> 代码生成 NPC 同一套路，首次攀爬发现查询时幂等落地）。
    /// </para>
    /// </summary>
    public class ClimbableActor : Script, IClimbable
    {
        // ===== 几何（局部坐标，单位与角色移动一致：1 单位 = 1 cm） =====
        /// <summary>底部起始抓握高度（局部 Z）。低于此高度不再提供支撑（松手下坠）。</summary>
        public float EntryHeight = 0f;

        /// <summary>顶部翻越高度（局部 Z）。在顶部继续向上输入 → 站立离场。</summary>
        public float TopHeight = 300f;

        /// <summary>攀爬面横向半宽（局部 X），超出即不可抓。</summary>
        public float HalfWidth = 120f;

        /// <summary>角色身体中心相对攀爬面沿 -Forward 的嵌入深度（UE5 TargetLocationOffset=15 语义，加胶囊半径余量）。</summary>
        public float GrabDepth = 30f;

        /// <summary>顶部站立时相对 TopHeight 再升的高度（站直余量，UE5 下行扫描后 ledge+80 口径）。</summary>
        public float StandUpStep = 80f;

        /// <summary>顶部站立时相对攀爬面的外向深度（翻身后站姿略靠前）。</summary>
        public float ExitDepth = 10f;

        // ===== 时序 =====
        /// <summary>攀爬竖直速度（单位/秒）。对应 AttachWarpProps.Speed 的消费值。</summary>
        public float ClimbVerticalSpeed = 100f;

        /// <summary>沿 ledge 横移速度（单位/秒）。对应 UE5 前向扫面横移段（:1688 的 -InputVector.X 分支）。</summary>
        public float ClimbStrafeSpeed = 60f;

        /// <summary>跳抓初始吸附点在角色中心之上的高度偏移。</summary>
        public float InitialGrabZOffset = 30f;

        /// <summary>Blend-in 时间（对应 UE5 OptionalBlendInTime / bShortClimbStrafe=.3f，:1738）。</summary>
        public float EntryBlendInTime = 0.3f;

        // ===== 门禁 =====
        /// <summary>起跳/走近时允许判定可抓的最大水平距离。</summary>
        public float MaxClimbDistance = 140f;

        /// <summary>横向可抓容差（超出 HalfWidth 后仍可伸手）。</summary>
        public float LateralTolerance = 60f;

        /// <summary>角色低于底部多少之内仍可入抓（太低不触发）。</summary>
        public float BottomSlack = 250f;

        /// <summary>角色高于顶部多少之外不再触发攀爬。</summary>
        public float TopSlack = 100f;

        /// <summary>实例登记（对应交互系统在无碰撞 Actor 上的邻近回退套路：射线打不中的代码生成物仍可被发现）。</summary>
        private static readonly List<ClimbableActor> _active = new List<ClimbableActor>();

        public override void OnEnable()
        {
            base.OnEnable();
            if (!_active.Contains(this)) _active.Add(this);
        }

        public override void OnDisable()
        {
            _active.Remove(this);
            base.OnDisable();
        }

        // ===== IClimbable =====

        /// <summary>对应 UE5 GetClimbOverrideTag_Implementation 的默认值（NarrativeCharacterMovement.cpp:1504）。</summary>
        public GameplayTag GetClimbOverrideTag()
        {
            // Core 与 Items 各有一个同名静态标签表，运行期持有真实标签的是 Core 版（见 NarrativeGameplayTags.cs 注册函数）。
            return NarrativePro.Core.NarrativeGameplayTags.Narrative_Anim_OverrideLayer_Climbing;
        }

        public bool CanClimbFrom(Vector3 characterLocation, Vector3 facing, float maxDistance)
        {
            if (Actor == null) return false;
            WorldToClimbParams(characterLocation, out float x, out float z);
            if (x < -HalfWidth - LateralTolerance || x > HalfWidth + LateralTolerance) return false;
            if (z < EntryHeight - BottomSlack || z > TopHeight + TopSlack) return false;

            Vector3 facePoint = ClimbPointToWorld(Mathf.Clamp(x, -HalfWidth, HalfWidth), z);
            Vector3 toFace = facePoint - characterLocation;
            toFace.Z = 0f;
            float dist = toFace.Length;
            float limit = maxDistance > 0f ? maxDistance : MaxClimbDistance;
            if (dist > limit) return false;

            // 要求大致面向攀爬面（背面接近不算），与 UE5 前向扫描的方向语义一致。
            if (dist > 1f && facing.LengthSquared > 0.0001f)
            {
                float dot = Vector3.Dot(Vector3.Normalize(toFace), Vector3.Normalize(facing));
                if (dot < -0.1f) return false;
            }
            return true;
        }

        public bool AdjustInitialWarpProps(
            AttachWarpProps props,
            Vector3 initialHitPoint,
            Vector3 initialHitNormal,
            NarrativeCharacter character,
            bool pressedJump,
            Float2 inputVector,
            float optionalBlendInTime)
        {
            if (props == null || Actor == null || character == null || character.Actor == null) return false;

            Vector3 charPos = character.Actor.Position;
            WorldToClimbParams(charPos, out float rawX, out float rawZ);

            // 角色在攀爬带的纵向范围外则放弃（UE5：下行扫描无顶面即 early out，:1653）。
            if (rawZ < EntryHeight - BottomSlack || rawZ > TopHeight + TopSlack) return false;

            float x = Mathf.Clamp(rawX, -HalfWidth, HalfWidth);
            // 跳抓吸附高度：角色中心略上（手部高度），夹到可爬区间（UE5 横档吸附语义的最简等价）。
            float z = Mathf.Clamp(rawZ + InitialGrabZOffset, EntryHeight, TopHeight);
            // 向上输入时先递进一格，避免入场即判「到底」。
            if (inputVector.Y > 0.1f) z = Mathf.Min(z + ClimbVerticalSpeed * 0.1f, TopHeight);

            props.ClimbableActor = this;
            Vector3 entry = ClimbPointToWorld(x, z);
            Quaternion faceWall = FaceWallRotation();
            props.LedgeTransform = new Transform(entry, faceWall, Vector3.One);
            props.CurrentLedgeTransform = props.LedgeTransform;
            props.HasFrontLedge = true;
            props.ActionType = ETraversalActionType.Climb;
            props.CurrentMovementMode = character.GetNarrativeCharacterMovement()?.MovementMode
                                        ?? NarrativeMovementMode.Walking;
            props.NewMovementMode = NarrativeMovementMode.Custom; // 入场后保持攀爬态
            props.Speed = ClimbVerticalSpeed;
            props.OverrideLayerBlendIn = EntryBlendInTime;
            props.OptionalBlendInTime = optionalBlendInTime >= 0f ? optionalBlendInTime : EntryBlendInTime;
            return true;
        }

        public bool TryFindClimbTransform(
            AttachWarpProps props,
            AttachWarpProps oldProps,
            NarrativeCharacter character,
            bool pressedJump,
            Float2 inputVector,
            float deltaTime)
        {
            if (props == null || oldProps == null || Actor == null) return false;

            Vector3 current = oldProps.CurrentLedgeTransform.Translation;
            WorldToClimbParams(current, out float x, out float z);

            float nz = z + inputVector.Y * ClimbVerticalSpeed * deltaTime;
            // 横移方向与 UE5 :1688 的 * -InputVector.X 一致。
            float nx = x - inputVector.X * ClimbStrafeSpeed * deltaTime;

            bool atTop = nz >= TopHeight - 0.01f;
            bool atBottom = nz <= EntryHeight + 0.01f;

            // 顶部 + 向上输入/跳跃 → 翻越站立离场（UE5 climb-to-standing，:1770-1785：NewMovementMode=MOVE_Walking）。
            if (atTop && (pressedJump || inputVector.Y >= 0.9f))
            {
                float exitX = Mathf.Clamp(nx, -HalfWidth, HalfWidth);
                Vector3 stand = ClimbPointToWorld(exitX, TopHeight + StandUpStep, ExitDepth);
                props.LedgeTransform = new Transform(stand, Actor.Orientation, Vector3.One);
                props.CurrentLedgeTransform = props.LedgeTransform;
                props.NewMovementMode = NarrativeMovementMode.Walking;
                props.ActionType = ETraversalActionType.ExitClimb;
                props.HasFrontLedge = true;
                props.ObstacleDepth = 100f; // 对齐 UE5 :1774
                return true;
            }

            // 底部 + 向下拉到底且未按跳 → 松手（返回 false，移动层落为 Falling，UE5 :577）。
            if (atBottom && inputVector.Y <= -0.9f && !pressedJump) return false;

            float cz = Mathf.Clamp(nz, EntryHeight, TopHeight);
            float cx = Mathf.Clamp(nx, -HalfWidth, HalfWidth);
            Vector3 next = ClimbPointToWorld(cx, cz);
            props.LedgeTransform = new Transform(next, FaceWallRotation(), Vector3.One);
            props.CurrentLedgeTransform = props.LedgeTransform;
            props.NewMovementMode = NarrativeMovementMode.Custom; // 继续攀爬
            props.ActionType = ETraversalActionType.Climb;
            props.Speed = ClimbVerticalSpeed;
            return true;
        }

        // ===== 几何工具 =====

        /// <summary>攀爬参数 → 世界点（角色身体中心：贴面并嵌入 GrabDepth）。</summary>
        private Vector3 ClimbPointToWorld(float x, float z) => ClimbPointToWorld(x, z, GrabDepth);

        private Vector3 ClimbPointToWorld(float x, float z, float depth)
        {
            Vector3 origin = Actor.Position;
            Quaternion q = Actor.Orientation;
            Vector3 right = Vector3.Transform(Vector3.Right, q);
            Vector3 up = Vector3.Transform(Vector3.Up, q);
            Vector3 fwd = Vector3.Transform(Vector3.Forward, q);
            return origin + right * x + up * z - fwd * depth;
        }

        /// <summary>世界点 → 攀爬参数（沿正交轴投影，与 ClimbPointToWorld 互逆）。</summary>
        private void WorldToClimbParams(Vector3 world, out float x, out float z)
        {
            Vector3 rel = world - Actor.Position;
            Quaternion q = Actor.Orientation;
            Vector3 right = Vector3.Transform(Vector3.Right, q);
            Vector3 up = Vector3.Transform(Vector3.Up, q);
            x = Vector3.Dot(rel, right);
            z = Vector3.Dot(rel, up);
        }

        /// <summary>攀爬中角色面朝墙（-外法线）的旋转，只取偏航。</summary>
        private Quaternion FaceWallRotation()
        {
            Vector3 fwd = Vector3.Transform(Vector3.Forward, Actor.Orientation);
            Vector3 toWall = new Vector3(-fwd.X, 0f, -fwd.Z);
            if (toWall.LengthSquared < 0.0001f) return Actor.Orientation;
            return Quaternion.LookRotation(Vector3.Normalize(toWall));
        }

        /// <summary>
        /// 邻近回退查询：无碰撞体的代码生成攀爬物射线打不中
        /// （与 PlayerInteractionComponent 的邻近回退同一理由），用登记表找最近可抓者。
        /// <para>
        /// 缺陷5 的健壮性收口：
        /// ① 首次查询先按内容生成攀爬物（<see cref="NarrativeClimbableSpawner"/>，一次性幂等，
        ///    目录缺失/无条目时只是不生成，不抛异常也不重复扫盘）；
        /// ② 登记表为空时立即返回 null，不做任何逐帧扫描；
        /// ③ 失效条目（Actor 被销毁）按固定节奏批量清理，而不是每次查询都遍历删除；
        /// ④ <paramref name="excludeOwner"/> 传入攀爬者时，跳过属于它自己那棵 Actor 子树的攀爬物
        ///    （与移动组件的自 Hit 过滤同一口径，防止代码生成的攀爬物被挂到角色身上后自抓）。
        /// </para>
        /// </summary>
        /// <param name="location">角色当前位置。</param>
        /// <param name="facing">角色水平朝向。</param>
        /// <param name="maxDistance">最大可抓水平距离。</param>
        /// <param name="excludeOwner">需要排除归属子树的角色 Actor（可为 null）。</param>
        public static ClimbableActor FindNearestClimbable(Vector3 location, Vector3 facing, float maxDistance, Actor excludeOwner = null)
        {
            // ①：内容驱动的攀爬物在这里落地一次（主线程：调用点是移动组件 OnUpdate 里的发现扫描）。
            NarrativeClimbableSpawner.EnsureSpawnedFromContent();

            // ②：空表直接返回 —— 一个攀爬物都没有时也必须安全（这是最常见的场景）。
            if (_active.Count == 0) return null;

            // ③：按节奏批量剔除失效项，避免销毁高峰里每次查询都全表清理。
            if ((_queryCounter = (_queryCounter + 1) % DeadSweepInterval) == 0)
            {
                for (int i = _active.Count - 1; i >= 0; i--)
                {
                    var entry = _active[i];
                    if (entry == null || entry.Actor == null) _active.RemoveAt(i);
                }
                if (_active.Count == 0) return null;
            }

            ClimbableActor best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < _active.Count; i++)
            {
                var c = _active[i];
                if (c == null || c.Actor == null) continue;                 // ②/③：失效项跳过，不索引越界
                if (c.Actor == excludeOwner || IsInOwnerChain(c.Actor, excludeOwner)) continue; // ④
                if (!c.CanClimbFrom(location, facing, maxDistance)) continue;
                Vector3 delta = c.Actor.Position - location;
                delta.Z = 0f;
                float d = delta.Length;
                if (d < bestDist) { bestDist = d; best = c; }
            }
            return best;
        }

        private static int _queryCounter;

        /// <summary>失效项批量清理节奏（次查询）。</summary>
        private const int DeadSweepInterval = 64;

        /// <summary>候选 Actor 是否挂在 <paramref name="owner"/> 之下（沿父链回溯，与近战/攀爬的自 Hit 过滤同一口径）。</summary>
        private static bool IsInOwnerChain(Actor candidate, Actor owner)
        {
            if (candidate == null || owner == null) return false;
            var current = candidate.Parent;
            int guard = 0;
            while (current != null && guard++ < 32)
            {
                if (ReferenceEquals(current, owner)) return true;
                current = current.Parent;
            }
            return false;
        }
    }
}
