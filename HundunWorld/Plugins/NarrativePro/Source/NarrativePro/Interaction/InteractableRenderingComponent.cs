using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Interaction
{
    /// <summary>
    /// 可交互渲染组件。挂到可交互 Actor 上，用于在玩家聚焦/交互时绘制特殊视觉效果（高亮、轮廓等）。
    /// 适配 UE5 UInteractableRenderingComponent（继承 UPrimitiveComponent，Flax 中改为 Script）。
    /// UE5 中的 FPrimitiveSceneProxy 在 Flax 无直接等价物，渲染逻辑通过 OnDebugDraw 提供可视化辅助，
    /// 实际的高亮材质叠加应由子类或 UI 层实现。
    /// </summary>
    public class InteractableRenderingComponent : Script
    {
        /// <summary>所属的可交互组件（自动查找，亦可手动指定）</summary>
        public NarrativeInteractableComponent OwningInteractable { get; set; }

        /// <summary>聚焦时是否启用渲染</summary>
        public bool bRenderWhenFocused = true;

        /// <summary>调试绘制颜色（高亮包围盒）</summary>
        public Color HighlightColor = new Color(1f, 0.85f, 0f, 1f);

        /// <summary>调试绘制线宽（Flax DebugDraw 通常忽略，保留以备子类使用）</summary>
        public float HighlightThickness = 2f;

        /// <summary>调试绘制持续时间（秒，&lt;=0 表示每帧重绘）</summary>
        public float DebugDrawDuration = 0f;

        public override void OnEnable()
        {
            base.OnEnable();
            // 自动查找所属可交互组件
            if (OwningInteractable == null)
            {
                OwningInteractable = Actor != null ? Actor.GetScript<NarrativeInteractableComponent>() : null;
            }
        }

        public override void OnDisable()
        {
            OwningInteractable = null;
            base.OnDisable();
        }

        /// <summary>
        /// 调试绘制。对应 UE5 CreateSceneProxy/CalcBounds 的可视化功能。
        /// 在编辑器与运行时绘制可交互对象包围盒，便于直观查看交互范围。
        /// </summary>
        public override void OnDebugDraw()
        {
            base.OnDebugDraw();

            if (!bRenderWhenFocused) return;
            if (OwningInteractable == null) return;

            BoundingBox bounds = CalcBounds();
            DrawBoundingBox(bounds, HighlightColor, DebugDrawDuration);
        }

        /// <summary>使用 DebugDraw.DrawLine 绘制包围盒的 12 条边。</summary>
        protected void DrawBoundingBox(BoundingBox bounds, Color color, float duration)
        {
            Vector3 min = bounds.Minimum;
            Vector3 max = bounds.Maximum;
            Vector3 a = new Vector3(min.X, min.Y, min.Z);
            Vector3 b = new Vector3(max.X, min.Y, min.Z);
            Vector3 c = new Vector3(max.X, min.Y, max.Z);
            Vector3 d = new Vector3(min.X, min.Y, max.Z);
            Vector3 e = new Vector3(min.X, max.Y, min.Z);
            Vector3 f = new Vector3(max.X, max.Y, min.Z);
            Vector3 g = new Vector3(max.X, max.Y, max.Z);
            Vector3 h = new Vector3(min.X, max.Y, max.Z);

            // 底面 4 条边
            DebugDraw.DrawLine(a, b, color, duration, false);
            DebugDraw.DrawLine(b, c, color, duration, false);
            DebugDraw.DrawLine(c, d, color, duration, false);
            DebugDraw.DrawLine(d, a, color, duration, false);
            // 顶面 4 条边
            DebugDraw.DrawLine(e, f, color, duration, false);
            DebugDraw.DrawLine(f, g, color, duration, false);
            DebugDraw.DrawLine(g, h, color, duration, false);
            DebugDraw.DrawLine(h, e, color, duration, false);
            // 垂直 4 条边
            DebugDraw.DrawLine(a, e, color, duration, false);
            DebugDraw.DrawLine(b, f, color, duration, false);
            DebugDraw.DrawLine(c, g, color, duration, false);
            DebugDraw.DrawLine(d, h, color, duration, false);
        }

        /// <summary>变换更新时是否需要重建渲染代理。
        /// 适配 UE5 ShouldRecreateProxyOnUpdateTransform，Flax 中默认返回 true。</summary>
        public virtual bool ShouldRecreateProxyOnUpdateTransform() => true;

        /// <summary>计算组件包围盒。适配 UE5 CalcBounds。</summary>
        /// <returns>世界空间包围盒</returns>
        public virtual BoundingBox CalcBounds()
        {
            if (OwningInteractable != null)
            {
                return OwningInteractable.GetInteractableBounds();
            }
            if (Actor != null)
            {
                return Actor.Box;
            }
            return new BoundingBox(Vector3.Zero, Vector3.Zero);
        }

#if FLAX_EDITOR
        /// <summary>编辑器属性变更回调。适配 UE5 PostEditChangeProperty。
        /// 子类可覆盖以响应编辑器中的属性修改。</summary>
        public virtual void OnEditorPropertyChanged()
        {
        }
#endif
    }
}
