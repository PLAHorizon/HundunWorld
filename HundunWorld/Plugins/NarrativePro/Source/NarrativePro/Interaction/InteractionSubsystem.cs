using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Interaction
{
    /// <summary>
    /// 交互子系统。缓存场景中所有 InteractableComponent，便于全局查询。
    /// 适配 UE5 UInteractionSubsystem（Flax 无 WorldSubsystem 等价物，使用 Singleton 模式）。
    /// </summary>
    public class InteractionSubsystem : Script
    {
        private static InteractionSubsystem _instance;
        private readonly HashSet<NarrativeInteractableComponent> _interactableActors = new HashSet<NarrativeInteractableComponent>();
        private readonly object _lock = new object();

        /// <summary>获取当前场景的交互子系统实例（可能为空）。</summary>
        public static InteractionSubsystem Instance => _instance;

        /// <summary>所有缓存的可交互组件。</summary>
        public HashSet<NarrativeInteractableComponent> GetInteractableActors()
        {
            lock (_lock)
            {
                return new HashSet<NarrativeInteractableComponent>(_interactableActors);
            }
        }

        /// <summary>缓存一个可交互组件。</summary>
        public void CacheInteractable(NarrativeInteractableComponent interactable)
        {
            if (interactable == null) return;
            lock (_lock)
            {
                _interactableActors.Add(interactable);
            }
        }

        /// <summary>取消缓存一个可交互组件。</summary>
        public void UncacheInteractable(NarrativeInteractableComponent interactable)
        {
            if (interactable == null) return;
            lock (_lock)
            {
                _interactableActors.Remove(interactable);
            }
        }

        /// <summary>查找距离指定位置最近的可交互组件。</summary>
        public NarrativeInteractableComponent FindNearestInteractable(Vector3 position, float maxDistance = float.MaxValue)
        {
            NarrativeInteractableComponent best = null;
            float bestDist = maxDistance;
            lock (_lock)
            {
                foreach (var interactable in _interactableActors)
                {
                    if (interactable == null) continue;
                    var actor = interactable.Actor;
                    if (actor == null) continue;
                    float d = Vector3.Distance(position, actor.Position);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = interactable;
                    }
                }
            }
            return best;
        }

        /// <summary>查找指定位置半径内的所有可交互组件。</summary>
        public List<NarrativeInteractableComponent> FindInteractablesInRange(Vector3 center, float radius)
        {
            var result = new List<NarrativeInteractableComponent>();
            float r2 = radius * radius;
            lock (_lock)
            {
                foreach (var interactable in _interactableActors)
                {
                    if (interactable == null) continue;
                    var actor = interactable.Actor;
                    if (actor == null) continue;
                    if (Vector3.DistanceSquared(center, actor.Position) <= r2)
                    {
                        result.Add(interactable);
                    }
                }
            }
            return result;
        }

        public override void OnEnable()
        {
            base.OnEnable();
            _instance = this;
        }

        public override void OnDisable()
        {
            lock (_lock)
            {
                _interactableActors.Clear();
            }
            if (_instance == this) _instance = null;
            base.OnDisable();
        }
    }
}
