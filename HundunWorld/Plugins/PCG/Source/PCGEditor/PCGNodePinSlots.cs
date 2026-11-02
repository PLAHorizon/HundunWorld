// PCG Plugin - Canvas box ids and graph pin indices
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §6.1.

namespace PCGEditor
{
    /// <summary>
    /// Converts between a Visject box's id and the pin index a saved graph stores.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A saved edge names its endpoints as a node index plus a pin index, while the canvas identifies a pin by
    /// the box id inside a node. The two are related by the layout <see cref="PCGNodeArchetypes"/> builds: boxes
    /// are added input-first, so an input's pin index is its box id and an output's is its box id minus the number
    /// of inputs on that node.
    /// </para>
    /// <para>
    /// Nothing in the engine checks that this conversion happened, so getting it wrong does not throw - it wires
    /// the graph to a different pin and every later run reads from the wrong one. That is why the arithmetic lives
    /// in one pure function with its own tests instead of being inlined twice into the canvas event handlers, and
    /// why the tests assert the dependency that is easiest to drop by accident (an output's index depends on the
    /// input count, so removing that term must not go unnoticed).
    /// </para>
    /// </remarks>
    internal static class PCGNodePinSlots
    {
        /// <summary>Box id a node's pin has, following the input-first layout.</summary>
        public static int BoxId(bool isOutput, int pinIndex, int inputPinCount) =>
            isOutput ? pinIndex + inputPinCount : pinIndex;

        /// <summary>Pin index one of a node's boxes carries in a saved graph.</summary>
        public static int PinIndex(bool isOutput, int boxId, int inputPinCount) =>
            isOutput ? boxId - inputPinCount : boxId;
    }
}
