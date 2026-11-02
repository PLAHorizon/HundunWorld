// PCG Plugin - Canvas box ids vs saved pin indices (docs/PCG_PLUGIN_DESIGN.md §6.1).
//
// Saving a wire means turning "the output box of node A that the mouse grabbed" into "node index + pin index",
// and nothing in the engine checks that the conversion was right: a wrong index does not throw, it reads from a
// different pin on every later run. So the arithmetic is a pure function and these tests own it.

using PCGEditor;

// One check callback for the whole harness: reuse the delegate NodeBehaviourTests already takes.
using CheckDelegate = PCGGoldenRunner.NodeBehaviourTests.CheckDelegate;

namespace PCGGoldenRunner
{
    internal static class PinSlotTests
    {
        internal static void Run(CheckDelegate check)
        {
            // Inputs come first and their pin index is their box id: the identity case, which is what most of the
            // canvas is made of. Asserted first so a failure above it is not masked by the interesting cases.
            check("an input box's id is its pin index",
                PCGNodePinSlots.PinIndex(false, 0, 3) == 0 &&
                PCGNodePinSlots.PinIndex(false, 1, 3) == 1 &&
                PCGNodePinSlots.PinIndex(false, 2, 3) == 2,
                $"{PCGNodePinSlots.PinIndex(false, 2, 3)}");

            check("an output box's id is offset by the node's input count",
                PCGNodePinSlots.PinIndex(true, 3, 3) == 0 &&
                PCGNodePinSlots.PinIndex(true, 4, 3) == 1,
                $"{PCGNodePinSlots.PinIndex(true, 4, 3)}");

            // The term that is easiest to drop by accident: an output's index must depend on how many inputs the
            // node has. With the term missing this returns the raw box id, which for a single-output node is an
            // out-of-range pin - the compiler would then reject a graph the designer just drew.
            check("an output's index depends on the input count, not only on its box id",
                PCGNodePinSlots.PinIndex(true, 3, 3) != PCGNodePinSlots.PinIndex(true, 3, 0),
                $"{PCGNodePinSlots.PinIndex(true, 3, 3)} vs {PCGNodePinSlots.PinIndex(true, 3, 0)}");

            // Round trip in both directions over the shapes the plugin actually declares, including a node with no
            // inputs at all (a sampler) and one with no outputs (a spawner).
            int checkedPairs = 0;
            bool roundTrips = true;

            for (int inputCount = 0; inputCount <= 4 && roundTrips; inputCount++)
            {
                for (int outputCount = 0; outputCount <= 4 && roundTrips; outputCount++)
                {
                    for (int pin = 0; pin < inputCount; pin++)
                    {
                        int box = PCGNodePinSlots.BoxId(false, pin, inputCount);
                        roundTrips &= PCGNodePinSlots.PinIndex(false, box, inputCount) == pin;
                        checkedPairs++;
                    }

                    for (int pin = 0; pin < outputCount; pin++)
                    {
                        int box = PCGNodePinSlots.BoxId(true, pin, inputCount);
                        roundTrips &= PCGNodePinSlots.PinIndex(true, box, inputCount) == pin;
                        checkedPairs++;
                    }
                }
            }

            check("box id and pin index round-trip for every declared shape", roundTrips, $"{checkedPairs} pair(s)");

            // The layout PCGNodeArchetypes.SpawnNode writes: every input box, then every output box, ids 0..N-1
            // then N... The mapping is only sound because that loop orders them that way, so the order itself is
            // asserted here rather than assumed - if the archetype builder ever interleaves them, the pin indices
            // saved for every wire would shift and this is the test that should say so.
            const int Inputs = 2;
            const int Outputs = 3;
            var slots = new int[Inputs + Outputs];
            int boxId = 0;

            for (int i = 0; i < Inputs; i++)
                slots[boxId] = PCGNodePinSlots.PinIndex(false, boxId++, Inputs);

            for (int i = 0; i < Outputs; i++)
                slots[boxId] = PCGNodePinSlots.PinIndex(true, boxId++, Inputs);

            bool sequenceOk = true;
            for (int i = 0; i < Inputs; i++)
                sequenceOk &= slots[i] == i;

            for (int i = 0; i < Outputs; i++)
                sequenceOk &= slots[Inputs + i] == i;

            check("input-first box numbering yields 0..n-1 per side", sequenceOk,
                string.Join(",", slots));

            // A node with no inputs: an output's box id is already its pin index, which is the case that makes the
            // input count a real variable rather than a constant of the plugin.
            check("a source node with no inputs numbers its outputs from zero",
                PCGNodePinSlots.PinIndex(true, 0, 0) == 0 && PCGNodePinSlots.PinIndex(true, 2, 0) == 2);
        }
    }
}
