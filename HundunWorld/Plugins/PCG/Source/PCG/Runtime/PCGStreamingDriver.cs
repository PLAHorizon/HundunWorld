// PCG Plugin - Streaming driver component
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §7.3.

using FlaxEngine;

namespace PCG.Runtime
{
    /// <summary>
    /// Gives a streaming volume the heartbeat it cannot have on its own: Flax exposes an update hook on
    /// <see cref="Script"/>, while a <see cref="PCGVolume"/> is an <see cref="Actor"/>.
    /// </summary>
    /// <remarks>
    /// Dropping this on any actor in the volume makes the window follow, with no game code. It deliberately does
    /// nothing but forward the call - the decision about whether a frame is worth a generation lives on the
    /// volume (<see cref="PCGVolume.UpdateStreaming"/>), because that decision needs the cell plan, the layer
    /// backlog and the cache, all of which belong to the generation, not to whoever happens to be ticking.
    /// A game that already has a per-frame hook on the player is free to call that method itself and leave this
    /// component off.
    /// </remarks>
    public class PCGStreamingDriver : Script
    {
        /// <summary>The streaming volume to keep up to date.</summary>
        [Tooltip("The streaming volume whose window follows its own StreamFollowTarget.")]
        public PCGVolume Target;

        /// <summary>
        /// Forwards one frame to the volume. Cheap when nothing has to happen: the volume's own check is a
        /// position read plus two comparisons.
        /// </summary>
        public override void OnUpdate()
        {
            base.OnUpdate();

            if (Target == null)
                return;

            Target.UpdateStreaming();
        }
    }
}
