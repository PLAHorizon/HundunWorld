---
Title: Fix interpolation edge cases in NetworkSyncManager and improve prediction-match robustness
---

This PR contains the interpolation fixes already committed to main and adds the follow-up change to align prediction identifiers using ClientTick. It includes implementation, tests, and updates to NetworkSyncManager so PredictedState carries ClientTick and CorrectPrediction uses ClientTick matching.

Files changed (summary):
- HundunWorld/Source/Game/Network/NetworkSyncManager.cs
  - Add ClientTick to PredictedState
  - Populate PredictedState.ClientTick from ECS LocalSimulationSystem via HundunWorldGame.Instance.CurrentClientTick if available, else use predictedFrameCount cast
  - CorrectPrediction and ReplayPredictions updated to match on ClientTick (long) instead of SequenceNumber (int)
  - Added tests under HundunWorld/Tests/ to validate matching and replay behavior

Details:
- PredictedState now has `public long ClientTick;`
- PredictMovement() populates ClientTick when enqueuing predicted states.
- CorrectPrediction(long serverClientTick) now searches predictionBuffer by ClientTick.
- ReplayPredictions(long fromClientTick) replays states with ClientTick > fromClientTick.

Tests added:
- HundunWorld/Tests/NetworkSync/PredictionSequenceAlignmentTests.cs
  - Verifies PredictedState.ClientTick is set and that CorrectPrediction matches by ClientTick.
  - Tests ReplayPredictions replays only inputs after acknowledged tick.

Notes on compatibility:
- This change prefers ClientTick (long) as the canonical identifier and is compatible with InputSendSystem and InputHistoryBuffer which already use ClientTick. If the server echoes AcknowledgedSequence instead of ClientTick you'll need to map or ensure the server echoes ClientTick.

Risk:
- Moderate: touches prediction / reconciliation path and tests are added to cover the behavior. End-to-end tests recommended on staging.

Checklist:
- [x] Add PredictedState.ClientTick
- [x] Populate ClientTick in PredictMovement
- [x] Update CorrectPrediction/ReplayPredictions to use ClientTick
- [x] Add unit tests

