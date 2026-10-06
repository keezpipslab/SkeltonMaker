# SkeletonMaker

VR skeleton-building toy for Meta Quest 3 (Unity 6000.3.22f1, URP, XR Interaction Toolkit 3.5.1).

A life-size minimal line skeleton stands next to a 1.5 x 0.8 x 0.8m table holding 12 kinds of
primitive shapes, arranged in two rows of six: Sphere, Box, Capsule, Cylinder, Pyramid, Cone,
Octahedron, Hexagonal Prism, Triangular Prism, Torus, Round Box, Link. Grab a primitive with the
controller grip button, resize it with the thumbsticks, and place it onto the skeleton to build it
up. Primitives use plain meshes for now - `RaymarchableElement` exposes `Kind` + `Size` as the
stable data a future raymarch/SDF material can read directly (every kind here maps onto a
well-known SDF primitive), so swapping the visual later shouldn't touch any of the pickup/placement
logic.

Open scene: `Assets/SkeletonMaker/Scenes/SkeletonBuilder.unity`

## Controls

- **Grab**: grip button on either controller (XRI's "Select" action). Picks up the nearest free
  element within reach and parents it to the hand, keeping the exact orientation/offset it was
  grabbed in (no snapping).
- **Resize while held** (uses both thumbsticks regardless of which hand is holding):
  - Right stick **up/down**: uniform scale - multiplies all 3 axes by the same factor, so it preserves
    whatever shape the other 3 controls already gave it instead of pulling it back toward a cube
  - Left stick **up/down**: height (local Y)
  - Left stick **left/right**: width (local X)
  - Right stick **left/right**: depth (local Z)
- **Hold near the skeleton**: the nearest bone line brightens (yellow) as the held element gets
  within 35cm, turning green once within the 15cm placement radius - previewing which bone it'll
  land on before you let go. Near a hinge joint (shoulder, elbow, wrist, hip, knee, ankle), both
  bones meeting there brighten together instead of just one, signaling you're aiming at the joint
  itself rather than partway along a limb - joints use a tighter 6cm radius than bones do (most
  limb bones here are only 30-40cm long, so a joint had to stay a small, deliberate target rather
  than swallowing most of the bone's own length).
- **Release near/on a hinge joint** (within 6cm of a shoulder/elbow/wrist/hip/knee/ankle): the
  element stays, parented under that joint specifically (e.g. `Joint_LeftLowerArm` for the elbow) -
  takes priority over the bone check below even if a bone segment is technically a hair closer,
  since aiming at a joint is the more deliberate target.
- **Release near/on a bone elsewhere** (within 15cm of a bone line, away from a hinge joint): the
  element stays, parented under that specific bone (e.g. `Bone_LeftUpperArm_LeftLowerArm`) rather
  than the skeleton root, and a fresh copy appears back at its table slot.
- **Release far away**: the element waits 4s (grace period to re-grab it), then teleports back to
  its home slot on the table (the same instance - re-grabbing during the 4s cancels this).
- **Duplicate**: hold **A** (right controller) while squeezing the grip of either hand at a shape,
  and the hand takes a copy, leaving the original where it is - on the table or on the skeleton.
- **Mirror**: the **Mirror** button beside the dials (or the **M** key) switches mirror placement
  on and off; while it's lit, a shape placed on one side of the body is also placed on the other.

## Stages (tutorial, build, math, join - and a hidden one to record in)

The scene is one Unity scene split into stages by `StageController` (on the `Stages` object), which
switches whole objects on and off: an object listed under a stage is active only while a stage
listing it is current, and anything listed nowhere (XR rig, table, managers) is always there.
`SkeletonMaker > Add Stages` (Editor menu, once per scene) adds it and sorts the scene:

- **Tutorial** - starts as the table with only the Sphere and Box slots, and the `Tutorial Guide`
  text above it. `TutorialGuide` walks through seven steps and moves on as soon as each has actually
  been done, switching each new thing on when its step starts:
  1. pick a shape up;
  2. scale it (right stick up/down);
  3. reshape it (the other three stick axes; it's changing the width that counts);
  4. color it - the color baths appear;
  5. place it - the `Practice Stick` appears where the skeleton's spine will be: a `SkeletonRig`
     with **Stick** ticked, i.e. one upright bone and no joints, so previewing and placing work
     exactly as on the skeleton (nothing placed on it is copied to the avatar);
  6. look at it through the raymarch quad, which appears now - done once a second shape is on the
     stick, so the two can be seen merging;
  7. pick the quad up with the left grip - done once it's in the hand (or has moved 5cm).

  The guide only watches outcomes (what's in a hand, its `Size` and color, how many shapes are on
  the stick) and never talks to the baths or the quad - it just switches on whatever objects are
  listed for a step - so those can be restyled without touching it. The one link to the quad is a
  "Tutorial stick" entry the menu adds to its **Sources**, without which shapes on the stick
  wouldn't be drawn. **B** on the right controller (or Enter) skips the tutorial. The stick and
  everything on it disappear when Build begins.
- **Build** - everything else: the skeleton, the avatar stand-in, the raymarch quad, the smoothing
  knob, the color baths and the other ten table slots (a slot that's switched off hasn't spawned
  its shape yet, so the table fills up when Build begins).
- **Math** - everything in Build, plus one more dial beside the smoothing knob, built by
  `SkeletonMaker > Add Math Stage` (run it after Add Stages). It changes one thing about how the
  raymarch quad computes the surface, so only the raymarched view changes, not the meshes:
  - **Inflate** (`InflateKnob`, `RaymarchQuad.inflate`): `d - c`. The surface is wherever the
    distance is 0, so subtracting `c` everywhere moves it `c` meters outward - or inward below 0,
    where thin shapes disappear. 12 o'clock is 0; the dial runs from -0.15 to +0.15 m.
  - **Repeat** (`RepeatKnob`, `RaymarchQuad.repeat`) - *not in the scene for now*: the menu builds
    no dial for it and removes one that's there. The math is still in the shader and costs nothing
    while `repeat` is 0 (set it in the Inspector to try it). `mod(p)`: space is wrapped around every so
    many meters across the floor, so the same shapes are met again in every cell, out to
    `repeatDistance` (20 m, fading into the background color). All the way down is off; turning it
    up brings the copies in from 4 m apart to 1.2 m. The cells are centred on the middle of
    everything the quad shows, and whatever sticks out of a cell is cut off at its edge - so with
    the table, main skeleton and dancer all showing, use the left **X** button to declutter down to
    the dancer first. This is the expensive one on a Quest: every pixel of the quad inside the
    layer of copies is marched, not just those near a shape.

  The dial goes back to neutral when the stage is left, so Build always shows the skeleton as built.
- **Join** - the building is over: the table, the shapes on it (`LooseShapes`, on the `Stages`
  object), the color baths and the skeleton they were hung on are gone, leaving the avatar wearing
  what was built, the smoothing knob and the mode buttons. The quad starts on your head, so
  everything is seen raymarched (see Head view; take it off as usual, and it goes back to where it
  stood when the stage ends). And the other performer appears: this is the only stage, apart from
  Record, in which anyone else is on the stage - `BodyReceiver` drops whatever comes in during the
  others (its **Stages Shown**). The avatar is put in Tracking mode
  for as long as the stage lasts, since your body is only sent while it is tracked, and gets its
  old mode back afterwards. With nobody on the other end (the `OscLink`'s Remote Host is this PC)
  the other performer is your own recording, or the dancing fake peer until you have made one -
  see `TestPartner` below.
- **Record** (hidden) - **H** goes there from any stage, and **H** or **B** goes back to where you
  were. It shows the skeleton, the avatar, the quad and a text, puts the avatar in Tracking mode,
  and plays the saved recording so you can check it. **A** on the right controller (or **R**)
  starts a new one after a 3 s countdown, and stops and saves it. It is kept in
  `body-recording.oscrec` under `Application.persistentDataPath`, so it is there in every later
  session. A recording with less than 2 s of body in it (tracking wasn't running, or A was pressed
  twice) is thrown away and the saved one kept.

**B** on the right controller (or Enter) is "next stage": out of the tutorial into Build, then
round Build, Math and Join. The **Finished** button goes straight to Join.
`StageController.Instance.Go(stage)` / `Next()` / `ToggleRecord()` do it from code. Set **Start
Stage** on the `Stages` object to skip ahead while working on one stage. Run Add Stages again after
adding something to the scene the tutorial shouldn't show (or edit the lists by hand).

### Buttons

`SkeletonMaker > Add Buttons` builds five flat cubes the size of a dial, around the dials:
**Mirror** (`MirrorToggle`, lit while mirror placement is on) under the smoothing knob, **Finished**
(`FinishedButton`, on to Join) beside it, and in a row above the dials **Still**, **Animation** and
**You** (`ModeButton`), which choose what the avatar follows; the current one is lit. Buttons are
pressed with the **trigger**: pull either trigger while that hand is at the cube (`PushButton`;
within 8 cm). The grip stays for picking things up and turning the dials. Move the objects to put
them elsewhere; the menu leaves a button that's already there where it is.

## Saving and loading a skeleton

`SkeletonComposition` is one skeleton's configuration as plain data: every primitive placed on the
main skeleton - its `kind`, `size`, color (`painted` + `color`; unpainted elements keep their
material's color), which `Bone_`/`Joint_` anchor it's parented under, and its local
position/rotation offset from that anchor (the same offset `AvatarDuplicateManager` relies on to
mirror a duplicate correctly) - plus the `smoothing` it was built with and a format `version`. Only
placed elements are included - not anything still on the table, currently held, or a stand-in
avatar duplicate.

- **At runtime**: `SkeletonMaker > Add Composition Store` (Editor menu, once per scene) adds a
  `Composition Store` object wired to every element prefab and the Raymarch Quad. **F5** saves,
  **F9** loads; `CompositionStore.Instance.Save()` / `Load()` do the same from code. The file is
  `<slot>.json` (slot `skeleton` by default) under `Application.persistentDataPath`. Loading
  replaces whatever is placed on the skeleton (and its copies on the stand-in) and restores the
  smoothing; elements on the table or in a hand are left alone. Loaded elements behave like any
  placed one - grab, resize, re-place, recolor - but mirror pairs come back as two independent
  elements.
- **From the editor**: `SkeletonMaker > Export Composition...` writes the same JSON to a file you
  choose the location for. `CompositionExporter.BuildCompositionJson()` is the gathering logic,
  kept separate from the (blocking, interactive) save dialog so it stays testable/scriptable on its
  own.

## Script architecture (`Assets/SkeletonMaker/Scripts`)

- `PrimitiveKind` - the 12 shapes listed above.
- `RaymarchableElement` - owns Kind + Size, builds/rescales the visual mesh + a trigger
  `BoxCollider` used for grab detection. Sphere/Box/Capsule/Cylinder use
  `GameObject.CreatePrimitive`; every other kind comes from `ProceduralMeshFactory`. Size maps to
  `localScale` by dividing out the visual's own mesh bounds, so this works for any kind's native
  dimensions without a hand-picked factor per shape.
- `ProceduralMeshFactory` - builds the shapes with no Unity built-in equivalent (Pyramid,
  Octahedron, Cone, the two prisms, Torus, Link, Round Box). Each is saved as a real asset under
  `Assets/SkeletonMaker/Meshes/` the first time it's built in the Editor, since a MeshFilter
  reference to a bare `new Mesh()` doesn't survive being baked into a prefab. Its `AddTri`/
  `AddTriIndices` helpers take a rough "which way is outside" hint and pick whichever vertex order
  agrees with it, so a new shape's winding self-corrects instead of silently baking an inside-out
  face (`Torus` and `Link` reuse one general "sweep a tube around a closed planar path" helper).
- `Grabbable` - generic pick-up-and-hold mechanics (reparent with `worldPositionStays: true`).
- `HandGrabber` - lives on each controller; watches the grip button, finds/grabs/releases the
  nearest `Grabbable`.
- `HeldElementScaler` - the thumbstick-to-size mapping described above.
- `SkeletonRig` - the line skeleton itself (`LineRenderer` per bone) and
  `NearestBoneIndex(worldPoint)` used both for the placement check and to estimate which bone a
  primitive is being placed on. Each arm ends in a 10 cm hand bone past the wrist
  (`Bone_LeftHand_LeftHandTip`), to build a hand on; its tip is not a joint any body source
  reports, so on the avatar and on a remote skeleton the hand bone simply turns with the wrist.
  Otherwise 21 joints matching Unity's HumanBodyBones chain (hips/spine/
  chest/neck/head, shoulder-upperarm-lowerarm-hand per arm, upperleg-lowerleg-foot-toes per leg) -
  the same joint set/connectivity the rayMarchVR project's `RaymarchAvatarSource` drives live off a
  Humanoid Animator, but frozen here into a fixed A-pose (arms angled ~35 degrees down and out from
  the shoulders, knees kicked slightly forward) instead of being posed at runtime. Exposes each
  bone's own GameObject as `BoneAnchor(index)` - positioned at that bone's own ("from") joint, so an
  element parented under it gets a small, meaningful local offset from the joint it landed on rather
  than from the whole rig's origin - and its `BoneJointName(index)`, the joint-name key shared with
  `AvatarBodyTarget`.
  Separately, the six limb hinges per side - shoulder, elbow, wrist, hip, knee, ankle, 12 total -
  each get their own dedicated `Joint_<name>` GameObject (`NearestHingeJointIndex`/
  `HingeJointAnchor`/`HingeJointName`), distinct from the bone-segment anchors above: a hinge joint
  is the point *shared* by two bones, and picking whichever of the two adjacent segments is a hair
  closer numerically doesn't read as "you're at the joint." `Joint_<name>` is one unambiguous node
  per hinge instead.
  Also owns the hold-proximity color preview (`UpdateHeldPreview`/`ClearHeldPreview`, via a
  `MaterialPropertyBlock` override per bone so it works regardless of whether the assigned line
  material honors per-vertex colors) - checks the nearest hinge joint first (highlighting every bone
  touching it together) before falling back to the single nearest bone segment.
- `SkeletonPlacement` - the keep-vs-discard rule on release. A hinge joint within range takes
  priority over a bone segment (`PlaceOnJoint` vs `PlaceOnSkeleton`) - either way it parents under
  the resolved anchor (not the rig root) and triggers `AvatarDuplicateManager`.
- `TableSpawnPoint` - one table slot; spawns a replacement when notified.
- `SkeletonComposition` / `CompositionStore` - the saved-skeleton data and the runtime save/load of
  it (see above). Loading goes through `SkeletonRig.TryFindAnchor` and
  `SkeletonPlacement.PlaceLoaded`, so a loaded element ends up in the same state as a hand-placed one.
- `AvatarBodyTarget` - joint-name -> Transform map for the avatar body that placed primitives get
  duplicated onto. Its joint slot names are auto-populated from `SkeletonRig.JointNames` so they can
  never drift out of sync; in the scene the slots point at the stand-in's `Bone_`/`Joint_` children.
- `AvatarDanceSource` - poses the stand-in each frame from the current `AvatarMode`'s source (still
  A-pose, dance Animator, or tracked body); `AvatarBodyTrackingSource` is the tracked-body source.
- `AvatarDuplicateManager` - on each placement, mirrors a transparent, non-interactive duplicate of
  the placed element onto the matching `AvatarBodyTarget` joint (see below). No-ops safely - no
  `AvatarBodyTarget` in the scene, or that joint's slot unassigned.

## Raymarched view (Raymarch Quad)

`SkeletonMaker > Add Raymarch Quad` (Editor menu) adds a `Raymarch Quad` to the open scene: a
1.6 x 2m quad standing 1m in front of `Avatar Stand-In (Preview)`, facing the camera, that shows the
placed primitives as one smooth raymarched (SDF) surface instead of meshes. It is a *window*, not a
screen: each eye's rays go through the quad into the world, so the shapes appear exactly where their
meshes are, with stereo depth, and only the pixels the quad covers are raymarched (the cheap way to
raymarch in VR - no URP renderer feature involved). Ported from the sibling rayMarchVR project's
quad path, but primitives-only and rewritten to this project's conventions.

- `RaymarchQuad` (on the quad) gathers every active `Visual` child under each entry in its
  **Sources** list every frame (after all `LateUpdate`s, so after `AvatarDanceSource` has posed the
  stand-in) and sends up to 64 shapes to the material via a `MaterialPropertyBlock`. The menu wires
  two sources, both visible from the start: the stand-in and the main `Skeleton` - tick
  **Visible** per source to toggle, at runtime too, or call `ToggleSource(index)` /
  `SetSourceVisible(index, bool)`. Look settings (per-source color, blend radius, light, ambient,
  specular, shadow/AO strength, max steps, background) live on the component.
  **Show Held** (on by default, grey **Held Color**) also draws the primitive currently held in a hand,
  which is not under any source root (`Grabbable.Held` tracks what is in a hand).
  **Show Table** (on by default, green **Table Color**) draws every `RaymarchableElement` in the scene
  that is neither placed on a skeleton (its anchor's parent isn't the rig) nor currently held - i.e.
  whatever's still sitting loose on the table.
- The quad itself can be moved: squeeze the **left** controller's grip while your hand is at the quad
  (`RaymarchQuadGrab`, kept separate from the `Grabbable`/`HandGrabber` system used by placeable primitives
  so the right hand - which shares that system's layer mask with every primitive - never competes for it).
- **Head view**: carry the quad to your head and let go there (hand within 25 cm of the headset) and
  it sticks: it rides 15 cm in front of the eyes, 1.2 m wide, so everything you look at is
  raymarched. It is the same window, worn like glasses - no extra render pass, and a pixel that
  looks at no shape costs a few dot products per shape and nothing more. While it is on, the quad
  draws only the shapes (so the table, the dials and the line skeletons stay visible around them),
  and shadows and occlusion are off and rays stop at 48 steps, since every pixel on screen is now
  a raymarched one; all of that is on `RaymarchQuadGrab` (**Head Clips Background**, **Head Cheap
  Shading**, **Head Max Steps**) and is undone when it comes off. Squeeze the left grip next to
  your head to take it off: it is back in your hand, as it was held when it went on. Raymarched
  shapes are drawn over everything further away than the quad, without depth of their own.
- The left controller's primary button (X) calls `ToggleContext()`, which flips **Show Table** together
  with every source whose **Include In Context Toggle** is ticked (the menu ticks it for the main
  skeleton) - everything is shown to begin with; one press declutters down to just the dancer,
  another press brings the table and reference pose back.
- **Smoothing** is set with the **Smoothing Knob** (`SmoothingKnob`, a `Knob` like the math stage's dials, built by `SkeletonMaker > Add Smoothing Knob`):
  a small disk with a gold indicator sphere floating left of the table, with the value in a text above it.
  Squeeze either hand's grip while that hand is at the disk and twist your wrist about the disk's axis like
  turning a real knob - the disk never moves, only the indicator turns (clockwise = more blending, 270 degrees
  of travel for 0 to 0.3 m). Twist is read from the controller's rotation (swing-twist about the knob's
  axis), so it works however you hold the controller. Move/rotate the `Smoothing Knob` object to relocate it
  (its up axis points at the viewer, forward is 12 o'clock). The thumbsticks are left to `HeldElementScaler`.
- Shape kind: the parent's `RaymarchShape` (stand-in duplicates - `AvatarDuplicateManager` now adds
  one, since it strips `RaymarchableElement`), else its `RaymarchableElement` (main skeleton), else
  parsed from the mesh name (covers duplicates made before `RaymarchShape` existed).
- Size/orientation are *not* sent separately: `Shaders/RaymarchQuad.shader` evaluates each shape in
  its `Visual`'s own mesh space (`worldToLocalMatrix`), with one SDF per kind matching the native
  mesh from `RaymarchableElement` / `ProceduralMeshFactory` exactly (built-in capsule/cylinder are
  radius 0.5 / height 2 along Y; procedural shapes fill a unit box, Y up; torus and link lie in
  the XY plane). **If you change a mesh in `ProceduralMeshFactory`, update its SDF in the shader and
  its bounding radius in `RaymarchQuad.NativeBoundRadius`.** Kind indices in the shader follow
  `PrimitiveKind`'s order.
- Cost controls for Quest: rays that miss the shapes' overall bounding sphere cost almost nothing,
  far shapes are skipped per step, and **Max Steps**, **Shadow Strength = 0** and
  **Occlusion Strength = 0** are the main knobs. The material's **Start At Surface** (on by default)
  hides anything between the viewer and the quad; **Clip Background** draws only the shapes.

## Avatar / Meta Body package

The stand-in avatar can follow the player's own body through Meta's **Movement SDK** (body
tracking) - see "Tracking mode" below. Grabbing is independent of it: it reads the standard XRI
controller transforms (`Left Controller` / `Right Controller` under `XR Origin (XR Rig)/Camera
Offset`). The sample's hand-tracking, poke/near-far interactors, and locomotion (teleport/move/turn)
were disabled in the scene since this app doesn't need them, and a live thumbstick otherwise doubles
as "walk around" input, which would fight the resize controls. Switching the Teleport Interactor
objects off isn't enough on its own: XRI's `ControllerInputActionManager` switches them back on
whenever a thumbstick is pushed forward (the red arc). `SkeletonMaker > Remove Teleport` unhooks the
manager from the teleport interactor and its two actions on both controllers.

The scene has a visible `Avatar Stand-In (Preview)` GameObject - a second copy of
the line-skeleton visual (both its bone segments and its 12 hinge-joint anchors) standing a couple
meters beside the table - so the duplicate feature can actually be seen working today.
`AvatarBodyTarget (Placeholder)`'s joint slots are wired to this stand-in's matching bones/joints
(by name), so placing a primitive on the real skeleton - on a bone or right at a hinge joint -
mirrors a faint transparent copy onto the corresponding spot on the stand-in. The stand-in isn't
frozen: `Assets/Animations/Dancing.fbx` (a Mixamo mocap clip, Humanoid, bone-only/no mesh so it's
naturally invisible) plays on a loop via a small hidden "Dance Motion Source" child (its own
Animator + `Assets/Animations/DancingLoop.controller`), and `AvatarDanceSource` reads that Animator's
live Humanoid bone transforms every `LateUpdate` to reposition *and reorient* the stand-in's
`Bone_`/`Joint_` children - rotation matters here even though a bare joint has no line of its own to
orient, since anything placed there is a child of it: `AvatarDuplicateManager` only ever captured a
*local* offset/rotation relative to the joint, so without also updating the joint's own rotation
every frame, a placed decoration would stay pointed the same fixed way in world space while the limb
danced around it instead of swinging with it. Same pattern the sibling rayMarchVR project's
`RaymarchAvatarSource` uses to drive a raymarched skeleton from mocap. It matches each stand-in
child's name (`Bone_{from}_{to}` / `Joint_{name}`) straight to a `HumanBodyBones` enum value via
`Enum.TryParse`, so it needs no
separate joint list of its own. Swap in any other Humanoid clip by pointing the controller's "Dance"
state at a different `AnimationClip`, or drop a different Humanoid-rigged FBX in and repoint
`AvatarDanceSource.sourceAnimator` at its Animator.

The rotation it reads straight off the Animator isn't usable as-is, though: a source rig's bones
aren't oriented the same way our own bone/joint anchors are (always identity - "no rotation
relative to setup"). Mixamo's own rest/bind orientation per bone is arbitrary relative to ours, so
driving an anchor's rotation directly from it baked that mismatch into every placed duplicate's
position *and* rotation (`Instantiate` rotates a child's local offset by its parent's rotation) -
correct-looking in the frozen A-pose (which never touches this rotation at all) but visibly wrong
once dancing. `AvatarDanceSource` fixes this in two parts:

- **Rest reference.** It captures the humanoid's zero-muscle T-pose once (via `HumanPoseHandler`),
  swings each limb from its T-pose direction onto the main skeleton's A-pose direction, and reports
  every bone's rotation *relative to that A-pose orientation*. So the anchors are exactly identity
  whenever a limb is in the A-pose, and a primitive keeps its place and orientation on the limb
  through any dance move. (An earlier version calibrated against whatever pose the dance happened to
  be in when it started, which was wrong whenever that wasn't an A-pose.)
- **Left/Right.** The main skeleton's "Left" joints sit on +X, which is the humanoid's *Right* side
  when facing +Z, so each joint name is looked up on the opposite side of the dancer. Without this,
  a primitive placed on the left arm followed the arm on the other side.

Verified live: every arm/leg segment's predicted limb direction matched the dancer's real limb
direction to 0.0 degrees at different moments of the dance, and the stand-in's left/right sides match
the main skeleton's. Torso joints that touch several bones use one of them as reference, so they are
only approximate.

**Still / Animation / You**: the three mode buttons above the dials put the stand-in in one of
three modes (`AvatarMode`, `AvatarDanceSource.Mode`):

- **Still** - the frozen A-pose. `AvatarDanceSource` copies the main `SkeletonRig`'s own
  `Bone_`/`Joint_` local transforms straight onto the stand-in's matching children every frame - the
  main rig's pose never changes, so it's always the correct, authoritative "standing still" reference
  rather than a separately cached snapshot that could go stale.
- **Animation** - the dance, as described above.
- **Tracking** (the **You** button) - the player's own body, from Meta's Movement SDK (see below).

Whenever a mode starts, the avatar is put where the still avatar stands: the source's hips are moved
over the still pose's hips once, and that offset is then kept, so from there on the avatar moves
freely, exactly as its source does (the dance's root motion, the player walking around). The dance
keeps its own heading and height. The tracked body is additionally turned to face the way the still
avatar faces and stood on the stand-in's floor (whichever foot is lowest at that moment counts as on
the ground), because the player is somewhere else, facing anywhere, in a tracking space whose floor
needn't be the scene's. If the mode's source has nothing to show yet (body tracking still starting,
permission not granted, no headset), the avatar stays in the still pose until it does; if tracking
drops out later, it holds its last pose.

`AvatarDanceSource.mode` is a plain serialized enum, so it can also be set directly in the Inspector
at runtime for quick testing without touching a controller.

**Distant / Embodied** (Tracking mode only): **Y** on the left controller (or **E**) flips
`AvatarDanceSource.embody`. Off, the tracked avatar stands at a distance as described above. On, it
is worn: every joint is put where the player's own joint is in the scene, so whatever was built on
the skeleton sits on the player's body. Still and Animation ignore the switch, and it is remembered
across mode changes. While worn, the copies on the head bone (`Bone_Neck_Head`) are switched off, as
they would sit around the player's eyes; they come back when it is taken off. The tracked joints are brought into the scene through the headset: Meta's
plugin reports the head in the same tracking space as the body joints, and the scene has it as the
main camera, so the two together give that space's place in the world
(`AvatarBodyTrackingSource.TryGetTrackingToWorld`) without assuming the XR Origin's tracking origin
is the one the plugin uses. It is redone every frame, so it follows the rig if that moves.

### Tracking mode (Meta Movement SDK)

Packages (added to `Packages/manifest.json`, with Meta's scoped registry `npm.developer.oculus.com`):
`com.meta.xr.sdk.core` 207.0.0 and `com.meta.xr.sdk.movement` (git, pinned to the v207 commit). The
project stays on Unity's OpenXR plugin + XRI's XR Origin - there is no `OVRCameraRig` in the scene
(an `OVRManager` only came in with passthrough, see below). The Core SDK's **Meta XR Feature** (OpenXR feature) is what makes body tracking available
to it, on Android (headset builds) and on Standalone (editor Play over Quest Link).

**Editor Play over Link**: Link needs **Developer Runtime Features** on (Meta Quest Link app >
Settings > Beta) and must be fully up - headset on, inside the Link home - *before* pressing Play.
The Meta plugin blocks the editor while it waits for a Link session that is only half connected
(once for several minutes, ending in a failed XR session). `SkeletonMaker > Body Tracking In Editor
(Link)` toggles the feature for Standalone: untick it to take the Meta plugin out of editor Play
when Link is misbehaving (Tracking mode then just shows the still pose in the editor).

Project settings that matter:

- `Assets/Oculus/OculusProjectConfig.asset`: **Body Tracking Support = Supported** - adds the
  `com.oculus.permission.BODY_TRACKING` permission and feature to the Android manifest at build time.
- `Assets/Resources/OculusRuntimeSettings.asset`: **Body Tracking Joint Set = Full Body** (legs are
  needed) and **Fidelity = High**.

Scene: `SkeletonMaker > Add Body Tracking Source` adds a `Body Tracking Source` object and wires it
into `AvatarDanceSource.bodySource` (the You button does nothing while that is empty). It
holds:

- `MetaSourceDataProvider` (Movement SDK; an `OVRBody`) - kept disabled until Tracking mode starts.
- `AvatarBodyTrackingSource` - enables it on `Begin()` (asking for the body tracking permission first
  if needed) and disables it again on `End()`, and exposes the tracked joints and the provider's
  T-pose by `HumanBodyBones` name (`IAvatarPoseSource`, the same interface the dance Animator is read
  through). It reads the provider's raw tracking-space joints rather than
  `MetaSourceDataProvider.GetSkeletonPose()`, which insists on an `OVRCameraRig` for its tracking
  space; the tracking space doesn't matter for the distant avatar anyway, since it is placed on the
  stand-in as described above, and the embodied one works it out from the headset.

`AvatarDanceSource` treats both sources identically: the rest-pose calibration described above uses
the source's T-pose (the Animator's zero-muscle pose, or the tracked skeleton's bind pose) and is done
in the body's own frame, so it holds whichever way the source happens to be facing.

## Passthrough (the real room, and its camera)

`SkeletonMaker > Add Passthrough` adds three objects and switches passthrough and passthrough camera
access on in `Assets/Oculus/OculusProjectConfig.asset` (which puts `com.oculus.feature.PASSTHROUGH`
and `horizonos.permission.HEADSET_CAMERA` in the Android manifest at build time). Package:
`com.meta.xr.mrutilitykit` 207.0.0, for its `PassthroughCameraAccess`.

Passthrough goes through Meta's Core SDK (the **Meta XR Feature** that body tracking already uses),
not through Unity's *Meta Quest: Camera (Passthrough)* OpenXR feature, which stays off: Unity's
route gives no camera pictures in the editor, and two owners of the passthrough layer is one too many.

- **`OVR Manager`** (`OVRManager`, Enable Passthrough on, Tracking Origin **Stage**). Meta's
  passthrough only runs with one in the scene. It also sets the tracking origin, which the XR Origin
  follows: the floor, with recentering off.
- **`Passthrough`** (`OVRPassthroughLayer` + `PassthroughView`). **Off in every stage for now**:
  the room is shown in the stages listed under **Stages Shown**, and that list is empty. **T**
  still flips it until the next stage change; and a
  remote performer drawn on top of the real one holds it on (`PassthroughView.Require`). Showing the
  room = the layer unhidden, the camera clearing to transparent instead of to the skybox (passthrough
  is composited *under* the scene, so it shows wherever nothing opaque was drawn) and
  `RaymarchQuad.clipBackground` on, so the quad draws only its shapes. If passthrough isn't running
  the skybox simply stays.
- **`Passthrough Camera`** (`PassthroughCameraAccess` + `PassthroughCameraFeed`). The left camera,
  1280 x 960, running only between `Begin()` and `End()`. `TryGetFrame` gives the pixels (bottom row
  first), focal length and principal point in pixels of that picture, the camera's pose in the scene
  when it was taken, and the timestamp. Meta reports the pose in the headset's tracking space; it is
  brought into the scene with `AvatarBodyTrackingSource.TryGetTrackingToWorld`, so it stays right
  after `StageCalibrator` has moved the XR Origin. **V** shows the live picture on a small panel by
  the table and logs resolution, lens numbers and pose once. Quest 3 / 3S only.

This is a Link project: it is run from the editor over Link, not as a headset build.

**Over Link** (editor Play): in the Link app, Settings > Developer (Settings > Beta in older
versions), next to Developer Runtime Features, switch on **Passthrough over Meta Horizon Link** and
**Passthrough Camera API permissions**, and restart Unity. Camera pictures need Link v85 or newer
and a USB cable. Passthrough only shows in the headset - the Game view stays black where the room
would be.

## Two performers on one stage (OSC)

Two instances, each an editor on Link with its own headset, send each other their tracked body and
the skeleton built on it, and show the other one as a line skeleton wearing ghosts of its primitives.
`SkeletonMaker > Add Shared Stage` adds everything below; `SkeletonMaker > Check OSC Codec` writes
and reads back a test message.

**Setting up two PCs**: on the `Shared Stage` object's `OscLink`, give each PC its own
**Performer Id** and the other PC's address as **Remote Host** (same port on both, UDP 9000 by
default, which the firewall has to let in). Your body is sent while the avatar is in Tracking mode;
the built skeleton is sent all the time.

### The stage frame

"Stage" here is the place, not the app's Tutorial/Build/Math phases. The `Stage Origin` object
(`StageFrame`) marks the spot on the floor both performers agree on; every body is sent relative to
it, in meters, Unity's left-handed axes, Y up. Both PCs must have it at the same place in the scene.

- **Stage Shift** is the safe offset. Each side draws the other moved by *(their shift - its own)*:
  (-1,0,0) on one PC and (1,0,0) on the other keeps the bodies 2 m further apart than the performers
  really are, zero on both puts every skeleton on its real performer. Whenever a remote skeleton is
  drawn less than 0.5 m from where its performer really stands, the real room is switched on and
  held on (see Passthrough; only for performers coming from another PC, not for the fake peer, an
  echo or a recording while Remote Host is this PC) - Quest's legs are estimated, so the skeleton is not where the feet are.
- **Calibration** (`StageCalibrator`) lines the headset up with the real floor by moving the XR
  Origin, never the scene. Put two marks on the floor, **Mark Distance** apart: A is the stage
  origin, B is straight ahead of it. Press **C**, rest the right controller on A and pull the
  trigger (or Space), then the same on B; the controller buzzes each time. The console reports how
  far apart it measured the marks - both PCs should agree to within a couple of centimeters. The
  result is saved to `stage-calibration.json` under `Application.persistentDataPath` and put back on
  the next start; it is flagged stale when the headset reports its tracking origin moved, and then
  needs doing again. With the `OVR Manager` that passthrough added, the tracking origin is Meta's
  **Stage** (the floor, not moved by a recenter), so a recenter no longer does that.
- `StageCalibrator.Calibrate(worldA, worldB)` is the entry point for the ArUco marker input that
  will replace the controller (markers lie flat on the floor, so that input also sets **Use Mark
  Height**). The frames for it come from `PassthroughCameraFeed.TryGetFrame` (see Passthrough); the
  detector itself is not written yet.

### Messages

All to `/skm/<performer id>/...`, one UDP datagram each, nothing acknowledged: state is simply
repeated, so a late or lossy peer is up to date within a second.

| Address | Arguments | Sent |
|---|---|---|
| `pose` | int sequence, then 21 joints x (float px py pz, qx qy qz qw), stage frame | every frame |
| `rest` | int version, then 21 joints x 7 floats: the T-pose, in the sender's own space | every second, and when it changes |
| `state` | int revision, int element count, float smoothing, float shift x y z, int calibrated | every second, and when it changes |
| `elem` | int revision, int index, string kind, string anchor, float size xyz, local position xyz, local rotation xyzw, int painted, float rgba | all of them every second, and when one changes |

Joint order (`OscBody.Joints`, `HumanBodyBones` names, anatomical left/right): Hips, Spine, Chest,
Neck, Head, Left Shoulder / UpperArm / LowerArm / Hand, the same four on the right, Left UpperLeg /
LowerLeg / Foot / Toes, the same four on the right. A joint a body lacks has NaN as its position.
An `elem` is one `SkeletonComposition.Element`; a skeleton is complete once `element count`
elements of the `state`'s revision have arrived.

### Scripts (`Scripts/Net`, plus `StageFrame` and `StageCalibrator`)

- `OscCodec` - `OscWriter` / `OscMessage`: int, float and string arguments, bundles read.
- `OscLink` - the UDP socket; `Received` hands messages out on the main thread. Has a simulated
  **Delay / Jitter / Loss** for what comes in.
- `BodySender` / `BodyPublisher` - the local body and skeleton, out.
- `BodyReceiver` / `RemoteSkeleton` / `NetworkPoseSource` - one remote skeleton per performer id,
  made on the first message and removed after 5 s of silence; hidden when no pose has arrived for 1 s.
  Its ghosts come from `AvatarDuplicateManager.BuildGhost` and it is added to the Raymarch Quad's
  sources (the 64 shapes are shared with everything else; the console warns when they run out).
- `AvatarDriver` - the calibrate-and-pose part that used to live in `AvatarDanceSource`, now shared
  by the stand-in and every remote skeleton.
- `BodyRecorder` / `BodyPlayer` - record what is sent (or received) to `body-recording.oscrec`
  and play it back in as performer 10 (further recorded performers as 11, 12, ...), looping.
- `FakePeer` - sends the dance as performer 2, wearing the local skeleton.
- `TestPartner` - the other performer when there is none: runs the recorder and player in the
  hidden Record stage, and in Join plays the recording (or switches the fake peer on while there is
  no recording yet) whenever Remote Host is this PC. The recording would stand exactly where you
  stood while making it, so it is shown moved by **Recording Shift** (stage meters).

### Testing with one PC

Leave **Remote Host** on 127.0.0.1 so everything sent comes straight back in.

Other performers only show in the Join and Record stages.

1. **Record and replay**: H, then A (or R) to record yourself and A again to save. From then on
   that recording is who you meet in Join.
2. **Fake peer**: with nothing recorded yet, Join shows a second skeleton dancing at the fake
   peer's Stage Position; no body tracking needed. `TestPartner` switches `Shared Stage > Fake
   Peer` on and off itself.
3. **Meet yourself**: tick **Accept Own Id** on the `OscLink` and go to Join. Your own body comes
   back as a remote skeleton, moved by the `BodyReceiver`'s **Echo Shift**. Give the link a
   **Delay** of a few seconds and it follows you around.
4. **A second PC without a headset** can run the same scene with its Remote Host set to the first
   PC, to try the real network (switch its `Fake Peer` on by hand: with a remote host that isn't
   this PC, `TestPartner` brings nobody on).
