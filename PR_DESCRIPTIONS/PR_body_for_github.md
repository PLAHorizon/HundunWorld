PR Title: Fix interpolation edge cases in NetworkSyncManager and align prediction id to ClientTick

PR Description:

In a nutshell
This PR fixes two interpolation edge cases in NetworkSyncManager that caused remote entity jitter and incorrect smoothing under certain buffering and timing conditions. It also aligns client-side prediction identifiers with the network protocol by adding ClientTick to PredictedState and matching server acknowledgements by ClientTick (with a fallback to the old SequenceNumber for compatibility).

What changed
1) Interpolation "latest" selection
- When interpolationBuffer.Count < 2, the code now picks the last enqueued ServerState as the latest instead of using Queue.Peek(). This ensures smoothing is applied towards the most-recent authoritative sample.

2) targetTime earlier than earliest buffered state
- Added handling for targetTime < earliest buffer timestamp (previous == null && next != null). The client now smoothly Lerp/Slerp toward the earliest server state instead of stuttering.

3) Prediction sequence alignment
- PredictedState now contains a long ClientTick field.
- PredictMovement fills PredictedState.ClientTick (fallback to local predictedFrameCount when ECS ClientTick unavailable).
- CorrectPrediction now attempts to match server-provided ClientTick first; if not found, it falls back to matching SequenceNumber (int) and logs a warning.
- ReplayPredictions replays predictions by ClientTick (long).

Files changed
- HundunWorld/Source/Game/Network/NetworkSyncManager.cs
- PR_DESCRIPTIONS/2026-07-24_fix-networksync-interpolation.md (PR description / guide)
- PR_DESCRIPTIONS/2026-07-24_fix-networksync-interpolation-and-align-clienttick.md

Testing and validation
- Inserted tests are recommended (not committed here) to validate:
  * Single-element interpolation smoothing
  * previous == null && next != null behavior
  * ClientTick matching and ReplayPredictions behavior under ack/late/packet-loss scenarios

Backward compatibility
- The change prefers ClientTick (long) as canonical ID (consistent with ECS InputPacket), but falls back to the existing SequenceNumber (int) for compatibility.

Follow-up tasks
- Expose current ECS LocalSimulationSystem CurrentClientTick to populate PredictedState with authoritative ClientTick at prediction time (recommended next PR).

Reviewer: @Long
Labels: 网络同步

