using System;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;

namespace Horizon.Game.GengDi.Core.Animations;

public static class ImplicitContentAnimationHelper
{
    public static void AttachSlideAndScale(Control control)
    {
        if (control == null)
        {
            return;
        }

        var visual = ElementComposition.GetElementVisual(control);
        var compositor = visual.Compositor;

        var offsetAnimation = compositor.CreateVector3KeyFrameAnimation();
        offsetAnimation.Target = "Offset";
        offsetAnimation.InsertExpressionKeyFrame(1.0f, "this.FinalValue");
        offsetAnimation.Duration = TimeSpan.FromMilliseconds(250);

        var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
        scaleAnimation.Target = "Scale";
        scaleAnimation.InsertExpressionKeyFrame(1.0f, "this.FinalValue");
        scaleAnimation.Duration = offsetAnimation.Duration;

        var animationGroup = compositor.CreateAnimationGroup();
        animationGroup.Add(offsetAnimation);
        animationGroup.Add(scaleAnimation);

        var implicitAnimations = compositor.CreateImplicitAnimationCollection();
        implicitAnimations["Offset"] = animationGroup;
        visual.ImplicitAnimations = implicitAnimations;
    }
}