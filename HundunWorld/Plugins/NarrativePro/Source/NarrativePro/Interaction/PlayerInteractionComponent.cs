using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Interaction
{
    /// <summary>
    /// 玩家交互组件。包含 NPC 不需要的交互追踪（trace）逻辑。
    /// 适配 UE5 UPlayerInteractionComponent，移除复制/RPC，改为本地逻辑 + 事件回调。
    /// </summary>
    public class PlayerInteractionComponent : NarrativeInteractionComponent
    {
        /// <summary>发现新的可交互对象时触发</summary>
        public event Action<NarrativeInteractableComponent> OnFoundInteractable;

        /// <summary>失去当前可交互对象时触发</summary>
        public event Action<NarrativeInteractableComponent> OnLostInteractable;

        /// <summary>按下交互键时触发</summary>
        public event Action<NarrativeInteractionComponent> OnInteractPressed;

        /// <summary>松开交互键时触发</summary>
        public event Action<NarrativeInteractionComponent> OnInteractReleased;

        /// <summary>当前正注视的可交互对象</summary>
        public NarrativeInteractableComponent ViewedInteractable { get; protected set; }

        /// <summary>上次交互检查时间</summary>
        public float LastInteractionCheckTime { get; set; } = 0f;

        /// <summary>本地玩家是否按住交互键</summary>
        public bool bInteractHeld { get; set; } = false;

        /// <summary>触发交互的输入动作名称列表</summary>
        public List<string> InteractionInputs { get; set; } = new List<string>();

        /// <summary>当前交互剩余时间（按住时间）</summary>
        public float RemainingInteractTime { get; set; } = 0f;

        /// <summary>交互检查频率（秒）。0 = 每帧检查</summary>
        public float InteractionCheckFrequency { get; set; } = 0f;

        /// <summary>交互检查的最大距离</summary>
        public float InteractionCheckDistance { get; set; } = 500f;

        /// <summary>若大于 0，使用球体追踪而非射线</summary>
        public float InteractionCheckSphereRadius { get; set; } = 0f;

        /// <summary>摄像机引用（用于交互追踪的起点和方向）</summary>
        public FlaxEngine.Camera InteractionCamera { get; set; }

        private float _nextCheckTime = 0f;

        public override void OnEnable()
        {
            base.OnEnable();
            _nextCheckTime = 0f;
        }

        /// <summary>每帧执行交互检查。</summary>
        public override void OnUpdate()
        {
            float dt = Time.DeltaTime;
            PerformInteractionCheck(dt);

            // 处理按住交互键的进度
            if (bInteractHeld && ViewedInteractable != null)
            {
                if (ViewedInteractable.InteractionTime > 0f)
                {
                    RemainingInteractTime -= dt;
                    if (RemainingInteractTime <= 0f)
                    {
                        // 交互完成
                        BeginInteract();
                    }
                }
            }
        }

        /// <summary>执行交互检查（射线/球体追踪）。</summary>
        public virtual void PerformInteractionCheck(float deltaTime)
        {
            // 检查频率
            if (InteractionCheckFrequency > 0f && Time.GameTime < _nextCheckTime) return;
            _nextCheckTime = Time.GameTime + InteractionCheckFrequency;
            LastInteractionCheckTime = Time.GameTime;

            Vector3 startPos;
            Vector3 dir;
            FlaxEngine.Camera cam = InteractionCamera ?? FlaxEngine.Camera.MainCamera;
            if (cam != null)
            {
                startPos = cam.Position;
                dir = cam.Direction;
            }
            else if (OwningPawn != null)
            {
                startPos = OwningPawn.Position;
                dir = OwningPawn.Direction;
            }
            else
            {
                return;
            }

            NarrativeInteractableComponent newInteractable = null;
            float maxDist = InteractionCheckDistance;

            // 使用球体或射线追踪
            if (InteractionCheckSphereRadius > 0f)
            {
                // 球体追踪：获取半径内所有碰撞体
                if (Physics.SphereCastAll(startPos, InteractionCheckSphereRadius, dir, out var hits, maxDist))
                {
                    foreach (var hit in hits)
                    {
                        var interactable = FindInteractableOnActor(hit.Collider);
                        if (interactable != null)
                        {
                            newInteractable = interactable;
                            break;
                        }
                    }
                }
            }
            else
            {
                // 射线追踪
                RayCastHit hit;
                if (Physics.RayCast(startPos, dir, out hit, maxDist))
                {
                    newInteractable = FindInteractableOnActor(hit.Collider);
                }
            }

            // 检查交互距离
            if (newInteractable != null && OwningPawn != null)
            {
                float dist = Vector3.Distance(OwningPawn.Position, newInteractable.Actor.Position);
                if (dist > newInteractable.InteractionDistance)
                {
                    newInteractable = null;
                }
            }

            // 更新当前可交互对象
            if (newInteractable != ViewedInteractable)
            {
                if (ViewedInteractable != null)
                {
                    ViewedInteractable.EndFocus(OwningPawn, this);
                    OnLostInteractable?.Invoke(ViewedInteractable);
                }
                ViewedInteractable = newInteractable;
                if (ViewedInteractable != null)
                {
                    ViewedInteractable.BeginFocus(OwningPawn, this);
                    OnFoundInteractable?.Invoke(ViewedInteractable);
                    RemainingInteractTime = ViewedInteractable.InteractionTime;
                }
            }
        }

        /// <summary>查找 Actor 上的 NarrativeInteractableComponent。</summary>
        private NarrativeInteractableComponent FindInteractableOnActor(Actor hitActor)
        {
            if (hitActor == null) return null;
            return hitActor.GetScript<NarrativeInteractableComponent>();
        }

        /// <summary>清除当前注视的可交互对象。</summary>
        public void ClearViewedInteractable()
        {
            if (ViewedInteractable != null)
            {
                ViewedInteractable.EndFocus(OwningPawn, this);
                OnLostInteractable?.Invoke(ViewedInteractable);
                ViewedInteractable = null;
            }
        }

        /// <summary>设置当前注视的可交互对象。</summary>
        public void SetViewedInteractable(NarrativeInteractableComponent interactable)
        {
            if (ViewedInteractable == interactable) return;
            ClearViewedInteractable();
            ViewedInteractable = interactable;
            if (ViewedInteractable != null)
            {
                ViewedInteractable.BeginFocus(OwningPawn, this);
                OnFoundInteractable?.Invoke(ViewedInteractable);
                RemainingInteractTime = ViewedInteractable.InteractionTime;
            }
        }

        /// <summary>开始交互（按下交互键）。</summary>
        public virtual void BeginInteract()
        {
            if (ViewedInteractable == null) return;

            bInteractHeld = true;
            string errorText;
            if (!ViewedInteractable.CanInteract(OwningPawn, this, out errorText))
            {
                NarrativeLog.Log($"[Interaction] 不可交互: {errorText}");
                return;
            }

            ViewedInteractable.BeginInteract(OwningPawn, this);
            OnInteractPressed?.Invoke(this);

            // 无需按住时直接完成交互
            if (ViewedInteractable.InteractionTime <= 0f)
            {
                if (ViewedInteractable.Interact(OwningPawn, this))
                {
                    EndInteract();
                }
            }
            else
            {
                RemainingInteractTime = ViewedInteractable.InteractionTime;
            }
        }

        /// <summary>结束交互（松开交互键）。</summary>
        public virtual void EndInteract()
        {
            if (ViewedInteractable == null) return;
            bInteractHeld = false;
            ViewedInteractable.EndInteract(OwningPawn, this);
            OnInteractReleased?.Invoke(this);
            RemainingInteractTime = 0f;
        }

        public override void Load()
        {
            base.Load();
            // 玩家不恢复占用状态
            OccupiedInteractable = null;
            OccupiedInteractableSlotIdx = -1;
        }
    }
}
