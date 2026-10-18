# SkeletonMaker

## This is a Link project, always

The app runs from the Unity editor in Play mode, streamed to a Quest 3 over Meta Quest Link (USB).
That is how it is developed and how it is performed. The only headset (Android) build is the
standalone second performer below.

- Never suggest a headset build of the main (Link) app: not as a fix, not as a workaround, not as a
  diagnostic or "decisive check", not as a verification step.
- The one headset build is the second performer: the No Raymarch scene as a standalone Quest app
  (`SkeletonMaker > Build Quest Standalone`), connecting to the Link PC over OSC. It is built from a
  separate git worktree kept on Android, never by switching this project's build target - that would
  make Editor Play over Link run with the Android settings.
- A problem that only shows over Link is a real problem to solve over Link. "It would be fine on the
  headset" is not an answer.
- Editor Play uses the Standalone (PC) settings: OpenXR features, render mode and quality level for
  Standalone are the ones that matter. Android-side settings only need to stay valid.
- Link must be fully up (headset on, inside the Link home) before Play is pressed; pressing Play
  while Link is half connected has frozen the editor.
- Keyboard shortcuts in Play only work while the Game view has focus.

See README.md for controls, stages, scripts and setup.
