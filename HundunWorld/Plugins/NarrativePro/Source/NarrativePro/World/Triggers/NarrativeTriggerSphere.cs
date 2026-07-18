using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.World.Triggers
{
    /// <summary>
    /// Narrative 触发球。移植自 UE5 NarrativeArsenal: World/Triggers/NarrativeTriggerSphere.h（UNarrativeTriggerSphere : USphereComponent）。
    /// 用作世界中的球形触发区域。
    ///
    /// 实现说明：
    /// - Flax 无 USphereComponent 直接对应物（最接近的是 SphereCollider，但不能安全子类化），
    ///   改为 Script 占位挂载到带碰撞体的 Actor 上，运行时自动查找或创建 SphereCollider 作为触发体。
    /// - 源类无反射 UPROPERTY / UFUNCTION，Radius 字段对应 USphereComponent::SphereRadius 基类属性。
    /// - 重叠开始/结束事件通过 Flax 的 Collider.TriggerEnter/TriggerExit 实现，转发到 OnBeginOverlap/OnEndOverlap。
    /// </summary>
    public class NarrativeTriggerSphere : Script
    {
        /// <summary>触发球半径（对应 USphereComponent::SphereRadius）</summary>
        public float Radius { get; set; } = 100f;

        /// <summary>触发碰撞体引用（未指定时将在 OnEnable 中自动查找或创建）。</summary>
        public SphereCollider TriggerCollider { get; set; }

        /// <summary>当有 Actor 进入触发球时调用。</summary>
        public event Action<Actor> OnBeginOverlap;

        /// <summary>当有 Actor 离开触发球时调用。</summary>
        public event Action<Actor> OnEndOverlap;

        /// <summary>是否由本组件自动创建触发碰撞体（未指定 TriggerCollider 时生效）。</summary>
        public bool bAutoCreateCollider { get; set; } = true;

        private bool _bCreatedColliderInternally;

        public override void OnEnable()
        {
            base.OnEnable();
            EnsureCollider();
            SubscribeCollider();
        }

        public override void OnDisable()
        {
            UnsubscribeCollider();
            DestroyInternalCollider();
            base.OnDisable();
        }

        /// <summary>确保触发碰撞体存在（自动查找或创建）。</summary>
        private void EnsureCollider()
        {
            if (TriggerCollider != null) return;

            // 优先查找 Actor 上已挂载的 SphereCollider
            TriggerCollider = Actor.GetScript<SphereCollider>();
            if (TriggerCollider != null) return;

            if (!bAutoCreateCollider) return;

            // 自动创建一个 SphereCollider 作为 Actor 子物体
            TriggerCollider = Actor.AddChild<SphereCollider>();
            TriggerCollider.Name = "NarrativeTriggerSphere_Collider";
            _bCreatedColliderInternally = true;

            NarrativeLog.Log($"[NarrativeTriggerSphere] 自动创建 SphereCollider：{Actor.Name}/{TriggerCollider.Name}");
        }

        /// <summary>订阅碰撞体触发事件。</summary>
        private void SubscribeCollider()
        {
            if (TriggerCollider == null) return;
            TriggerCollider.IsTrigger = true;
            TriggerCollider.Radius = Radius;
            TriggerCollider.TriggerEnter += OnTriggerEnterInternal;
            TriggerCollider.TriggerExit += OnTriggerExitInternal;
        }

        /// <summary>取消订阅碰撞体触发事件。</summary>
        private void UnsubscribeCollider()
        {
            if (TriggerCollider == null) return;
            TriggerCollider.TriggerEnter -= OnTriggerEnterInternal;
            TriggerCollider.TriggerExit -= OnTriggerExitInternal;
        }

        /// <summary>销毁本组件内部创建的碰撞体。</summary>
        private void DestroyInternalCollider()
        {
            if (_bCreatedColliderInternally && TriggerCollider != null)
            {
                Actor.Destroy(TriggerCollider);
            }
            TriggerCollider = null;
            _bCreatedColliderInternally = false;
        }

        /// <summary>触发进入事件回调。</summary>
        private void OnTriggerEnterInternal(PhysicsColliderActor other)
        {
            if (other == null) return;
            // PhysicsColliderActor 自身就是 Actor
            OnBeginOverlap?.Invoke(other);
        }

        /// <summary>触发离开事件回调。</summary>
        private void OnTriggerExitInternal(PhysicsColliderActor other)
        {
            if (other == null) return;
            OnEndOverlap?.Invoke(other);
        }

        /// <summary>运行时修改半径后需调用此方法同步到碰撞体。</summary>
        public void ApplyRadius()
        {
            if (TriggerCollider != null)
            {
                TriggerCollider.Radius = Radius;
            }
        }
    }
}
