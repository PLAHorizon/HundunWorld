using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.AI;
using NarrativePro.Character;
using NarrativePro.Core;
using NarrativePro.Interaction;
using NarrativePro.UnrealFramework;
using NarrativePro.Vehicles.Mass;

namespace NarrativePro.Vehicles
{
    /// <summary>
    /// 运动学交通载具 Actor。对应 UE5 由 <c>UVehicleSpawnerSubsystem::SpawnActor</c>
    /// 生成的 <c>ANarrativeWheeledVehicle</c> 蓝图实例（VehicleSpawnerSubsystem.cpp:22-77）。
    /// <para>
    /// UE5 侧该 Actor 的差异化能力来自 Chaos 轮式移动组件（SetBaseSnapshot / 车轮转向），
    /// Flax 1.12 无该 API（已核对 FlaxEngine.CSharp.xml，Vehicle/WheeledVehicle 0 命中），
    /// 因此本类是「车道运动学的 Actor 门面」：位置/朝向/速度由
    /// <see cref="Mass.VehicleMovementProcessor"/> 算出后写进来，
    /// 同时补齐 UE5 在该 Actor 上做的三件可移植的事——
    /// ① <see cref="NarrativeVehicleBase.SetRandomSeed"/>（cpp:56）；
    /// ② <see cref="NarrativeVehicleBase.SetManagedByMass"/>（cpp:58，此处为真实字段）；
    /// ③ <see cref="MountComponent"/> 座位与 <see cref="Mass.VehicleSpawnerSubsystem"/> 的
    /// <c>AddOccupants</c>（cpp:60-71）。
    /// </para>
    /// <para>
    /// 另外它是行人与玩家认识「有一辆车在这儿」的唯一入口：
    /// <c>MassPedSimulation.SyncVehicleObstacles</c> 按 Actor 上的 <see cref="NarrativeVehicleBase"/>
    /// 把车登记进行人避让障碍表，因此载具一旦生成，行人避让立刻生效。
    /// </para>
    /// </summary>
    public class TrafficVehicleActor : NarrativeWheeledVehicle
    {
        /// <summary>本 Actor 对应的模拟实体。由 <see cref="VehicleSpawnerSubsystem"/> 写入。</summary>
        [NonSerialized]
        public TrafficVehicle Vehicle;

        /// <summary>车身速度（世界单位/秒）。对应 UE5 Chaos 组件的 GetVelocity 读值。</summary>
        public Vector3 BodyVelocity { get; private set; }

        /// <summary>标量速度。</summary>
        public float BodySpeed { get; private set; }

        /// <summary>当前档位（表现/同步用）。</summary>
        public EVehicleRepresentationLOD RepresentationLOD { get; private set; } = EVehicleRepresentationLOD.High;

        /// <summary>材质泥污系数（0-1）。对应 UE5 FVehicleVisualPackedData::Dirt。</summary>
        public float VisualDirt { get; private set; }

        /// <summary>颜色索引。对应 UE5 FVehicleVisualPackedData::Color。</summary>
        public float VisualColor { get; private set; }

        /// <summary>是否正在刹车（供音效/灯光表现读取）。</summary>
        public bool bBraking { get; private set; }

        /// <summary>座位数（交互槽位）。UE5 配在载具蓝图的 Interactable 组件上。</summary>
        public int SeatCount = 1;

        /// <summary>是否由模拟（Mass 等价层）托管。对应 UE5 bManagedByMass。</summary>
        private bool bManagedByMass;

        /// <summary>本载具上已上车的角色（座位索引 → 角色）。对应 UE5 ActorToSeatIndexMap 的 Actor 侧视图。</summary>
        [NonSerialized]
        private readonly List<NarrativeCharacter> _occupants = new List<NarrativeCharacter>();

        /// <summary>运行期登记表（供玩家交互/任务查询最近的可用载具）。</summary>
        public static readonly List<TrafficVehicleActor> ActiveVehicles = new List<TrafficVehicleActor>();

        public override void OnEnable()
        {
            base.OnEnable();
            if (!ActiveVehicles.Contains(this)) ActiveVehicles.Add(this);
            EnsureMountComponent();
        }

        public override void OnDisable()
        {
            ActiveVehicles.Remove(this);
            DetachAllOccupants();
            base.OnDisable();
        }

        // =====================================================================
        // 模拟侧写入
        // =====================================================================

        /// <summary>
        /// 由生成子系统在取得 Actor 后调用。对应 UE5 cpp:46-53 的 SetBaseSnapshot
        /// （Chaos 缺失 → 改写运动学初值）。
        /// </summary>
        public void ConfigureFromEntity(TrafficVehicle vehicle, bool bReused, Transform spawnTransform,
                                         Vector3 velocity, float speed)
        {
            Vehicle = vehicle;
            if (Actor != null)
            {
                Actor.Position = spawnTransform.Translation;
                Actor.Orientation = spawnTransform.Orientation;
                Actor.IsActive = true;
            }
            BodyVelocity = velocity;
            BodySpeed = speed;
            RepresentationLOD = vehicle != null ? vehicle.LOD : EVehicleRepresentationLOD.High;
            SeatCount = Mathf.Max(SeatCount, vehicle != null ? 1 : 1);
        }

        /// <summary>
        /// 可视化数据同步。对应 UE5 <c>UVehicleVisualizationProcessor</c> 下发材质参数 +
        /// <c>SyncVelocityQuery</c> 写速度。
        /// </summary>
        public void ApplyVisualPackedData(Mass.VehicleVisualPackedData packed, Vector3 velocity,
                                           EVehicleRepresentationLOD lod)
        {
            VisualDirt = packed.Dirt;
            VisualColor = packed.Color;
            bBraking = packed.bBraking;
            BodyVelocity = velocity;
            BodySpeed = packed.Speed;
            RepresentationLOD = lod;
        }

        /// <summary>
        /// 载具速度读值。行人侧目前用位置差分推导速度（<c>MassPedSimulation</c> 的注释），
        /// 本方法是显式的速度接口：任何需要「车速」的代码可直接读，不再需要差分。
        /// </summary>
        public float GetSpeed() => BodySpeed;

        /// <summary>Actor 被回收进池时调用：清除实体绑定。</summary>
        public void NotifyReleased()
        {
            Vehicle = null;
            BodyVelocity = Vector3.Zero;
            BodySpeed = 0f;
            bBraking = false;
            RepresentationLOD = EVehicleRepresentationLOD.Low;
            DetachAllOccupants();
        }

        // =====================================================================
        // Mass 托管标记（NarrativeVehicleBase 的占位实现落地）
        // =====================================================================

        /// <summary>
        /// 移动组件读取。返回实体自己的运动学模型，
        /// 这样 gameplay 侧（伤害归因/倒车刹车/UI 车速表）与交通模拟读的是同一个对象
        /// ——对应 UE5 侧 gameplay 直接 <c>FindComponentByClass&lt;UChaosWheeledVehicleMovementComponent&gt;</c>。
        /// </summary>
        public override KinematicVehicleMovement GetVehicleMovementComponent()
        {
            if (Vehicle != null && Vehicle.Movement != null) return Vehicle.Movement;
            return base.GetVehicleMovementComponent();
        }

        // =====================================================================
        // 挂载（乘员/玩家上车）
        // =====================================================================

        /// <summary>取挂载组件（不存在则挂上并配好座位槽位）。</summary>
        public MountComponent GetMountComponent()
        {
            return EnsureMountComponent();
        }

        private MountComponent _mount;

        private MountComponent EnsureMountComponent()
        {
            if (_mount != null && _mount.Actor != null) return _mount;
            if (Actor == null) return null;

            _mount = Actor.GetScript<MountComponent>();
            if (_mount == null)
            {
                _mount = Actor.AddScript<MountComponent>();
            }
            if (_mount == null) return null;

            ApplySeatConfiguration(Vehicle);
            return _mount;
        }

        /// <summary>
        /// 按座位数补齐交互槽位（UE5 在载具蓝图上手工摆放座位槽位，此处等价生成）。
        /// 槽位沿车身 X 轴排开，作为 NPC/玩家上车的目标点。
        /// </summary>
        public void ApplySeatConfiguration(TrafficVehicle vehicle)
        {
            var mount = _mount ?? EnsureMountComponent();
            if (mount == null) return;

            if (vehicle != null && vehicle.Passengers != null && vehicle.Passengers.Passengers.Count > 0)
            {
                SeatCount = Mathf.Max(SeatCount, vehicle.Passengers.Passengers.Count);
            }
            SeatCount = Mathf.Clamp(SeatCount, 1, 8);

            if (mount.InteractionSlots == null)
            {
                mount.InteractionSlots = new List<InteractionSlotConfig>();
            }
            while (mount.InteractionSlots.Count < SeatCount)
            {
                int index = mount.InteractionSlots.Count;
                // 对应 UE5 蓝图上的座位 SlotTransform：沿车身右侧排开，面朝车头
                var slotTransform = new Transform(
                    new Vector3(60f + index * 55f, 40f, 0f),
                    Quaternion.Identity);
                mount.InteractionSlots.Add(new InteractionSlotConfig { SlotTransform = slotTransform });
            }

            mount.InteractableNameText = string.IsNullOrEmpty(VehicleSaveGUID.ToString()) && Actor != null
                ? Actor.Name
                : Actor?.Name ?? "Vehicle";
            if (string.IsNullOrEmpty(mount.InteractableActionText))
            {
                mount.InteractableActionText = "上车";
            }
        }

        /// <summary>还有空位吗（对应 UE5 GetAvailableSlots 的最小可用判定）。</summary>
        public bool HasFreeSeat()
        {
            return _occupants.Count < Mathf.Max(1, SeatCount);
        }

        /// <summary>
        /// 让一个已有角色上车。对应 UE5 <c>UMountComponent</c> 的占乘流程
        /// （MountComponent.cpp:72-99：TargetInteractionSlot + RunInteractBehavior），
        /// 区别是不再生成 NPC，而是接管现成角色（玩家/任务 NPC）。
        /// </summary>
        /// <param name="character">要上车的角色。</param>
        /// <param name="seatIndex">座位号，-1 表示取第一个空位。</param>
        /// <returns>是否成功上车。</returns>
        public virtual bool TryMountCharacter(NarrativeCharacter character, int seatIndex = -1)
        {
            if (character == null || Actor == null) return false;
            var mount = EnsureMountComponent();
            if (mount == null) return false;

            if (seatIndex < 0)
            {
                seatIndex = _occupants.Count;
            }
            if (seatIndex >= Mathf.Max(1, SeatCount))
            {
                NarrativeLog.LogWarning($"[TrafficVehicleActor] 座位 {seatIndex} 超出 {SeatCount} 个座位，{character.Actor?.Name} 未上车。");
                return false;
            }

            var hostActor = character.Actor;
            if (hostActor == null) return false;

            // 关碰撞（对应 UE5 Capsule->SetCollisionEnabled(NoCollision)；
            // Flax 1.12 Collider 无禁用开关，触发化即可移出碰撞响应——同 MountComponent.cs 的处理）
            DisableColliders(hostActor);

            // 移动模式置 None（对应 UE5 SetMovementMode(MOVE_None)）
            var movement = hostActor.GetScript<NarrativeCharacterMovement>();
            movement?.SetMovementMode(NarrativeMovementMode.None);

            // 挂到车身（对应 UE5 AttachToActor(SnapToTargetNotIncludingScale)）
            hostActor.Parent = Actor;
            hostActor.LocalPosition = Vector3.Zero;

            // 走交互槽位握手（对应 MountComponent.cs:403-409 的同一通路）
            var interaction = hostActor.GetScript<NPCInteractionComponent>();
            if (interaction != null && interaction.TargetInteractionSlot(mount, seatIndex, false))
            {
                interaction.RunInteractBehavior(false);
            }

            if (!_occupants.Contains(character)) _occupants.Add(character);
            mount.ActorToSeatIndexMap[hostActor] = seatIndex;

            // 第一个上车的角色成为「拥有者」（对应 UE5 玩家上车即 Possess 载具），
            // 于是 DealVehicleDamage / HandleDeath 的伤害归因走驾驶员的 ASC。
            if (_occupants.Count == 1)
            {
                PossessedBy(hostActor);
            }
            return true;
        }

        /// <summary>
        /// 让一个角色下车（交互动词「下车」/任务脚本/下车键调用）。对应 UE5 UMountComponent 卸载流：
        /// 座位映射释放、碰撞恢复、移动模式回 Walking，并按座位（车门位）世界变换落位，
        /// 不是把角色留在车体中心。
        /// </summary>
        public virtual bool TryDismountCharacter(NarrativeCharacter character)
        {
            if (character?.Actor == null) return false;
            var hostActor = character.Actor;

            bool bWasDriver = _occupants.Count > 0 && _occupants[0] == character;
            int seatIndex = -1;
            if (_mount != null) _mount.ActorToSeatIndexMap.TryGetValue(hostActor, out seatIndex);
            _occupants.Remove(character);
            if (_mount != null) _mount.ActorToSeatIndexMap.Remove(hostActor);

            hostActor.Parent = null;

            // 下车落位（缺陷2 对称出口的落点部分）：座位世界变换经 InteractionFunctionLibrary
            // （TryGetSlotWorldTransform 的实际调用方）；无座位映射时退回车身右侧外扩点。
            if (_mount != null &&
                NarrativePro.Interaction.InteractionFunctionLibrary.TryGetSlotWorldTransform(
                    _mount, seatIndex >= 0 ? seatIndex : 0, out Transform seatWorld))
            {
                hostActor.Position = seatWorld.Translation;
                hostActor.Orientation = seatWorld.Orientation;
            }
            else if (Actor != null)
            {
                Vector3 exitRight = Vector3.Transform(Vector3.Right, Actor.Orientation);
                exitRight.Y = 0f;
                hostActor.Position = Actor.Position +
                    (exitRight.LengthSquared > 0.0001f ? Vector3.Normalize(exitRight) * 120f : new Vector3(120f, 0f, 0f));
            }

            var movement = hostActor.GetScript<NarrativeCharacterMovement>();
            movement?.SetMovementMode(NarrativeMovementMode.Walking);

            EnableColliders(hostActor);

            // 驾驶员下车：把拥有者让给下一个在座角色（无人则解除占有）
            if (bWasDriver)
            {
                UnPossessed();
                if (_occupants.Count > 0 && _occupants[0].Actor != null)
                {
                    PossessedBy(_occupants[0].Actor);
                }
            }
            return true;
        }

        private void DetachAllOccupants()
        {
            for (int i = _occupants.Count - 1; i >= 0; i--)
            {
                TryDismountCharacter(_occupants[i]);
            }
            _occupants.Clear();
        }

        /// <summary>
        /// 返回车上的驾驶员角色。对应 UE5 <c>GetNarrativeCharacter()</c>（走控制器占有），
        /// Flax 无 Controller，故取第一个在座角色 —— 这让
        /// <see cref="NarrativeVehicleBase.DealVehicleDamage"/> 的伤害归属真正生效。
        /// </summary>
        public override NarrativeCharacter GetNarrativeCharacter()
        {
            NarrativeCharacter driver = _occupants.Count > 0 ? _occupants[0] : null;
            return driver ?? base.GetNarrativeCharacter();
        }

        /// <summary>车上角色快照（供任务/交互读取）。</summary>
        public IReadOnlyList<NarrativeCharacter> Occupants => _occupants;

        private static void DisableColliders(Actor root)
        {
            if (root == null) return;
            foreach (var child in root.Children)
            {
                if (child is Collider collider)
                {
                    collider.IsTrigger = true;
                }
                DisableColliders(child);
            }
        }

        private static void EnableColliders(Actor root)
        {
            if (root == null) return;
            foreach (var child in root.Children)
            {
                if (child is Collider collider)
                {
                    collider.IsTrigger = false;
                }
                EnableColliders(child);
            }
        }
    }
}
