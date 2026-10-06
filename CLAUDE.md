# SkeletonMaker

## This is a Link project, always

The app runs from the Unity editor in Play mode, streamed to a Quest 3 over Meta Quest Link (USB).
That is how it is developed and how it is performed. There are no headset (Android) builds in the
workflow.

- Never suggest a headset build: not as a fix, not as a workaround, not as a diagnostic or
  "decisive check", not as a verification step.
- A problem that only shows over Link is a real problem to solve over Link. "It would be fine on the
  headset" is not an answer.
- Editor Play uses the Standalone (PC) settings: OpenXR features, render mode and quality level for
  Standalone are the ones that matter. Android-side settings only need to stay valid.
- Link must be fully up (headset on, inside the Link home) before Play is pressed; pressing Play
  while Link is half connected has frozen the editor.
- Keyboard shortcuts in Play only work while the Game view has focus.

See README.md for controls, stages, scripts and setup.
