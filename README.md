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
  - Left stick **Y**: uniform scale - multiplies all 3 axes by the same factor, so it preserves
    whatever shape the other 3 controls already gave it instead of pulling it back toward a cube
  - Left stick **X**: height (local Y)
  - Right stick **X**: width (local X)
  - Right stick **Y**: depth (local Z)
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

## Exporting a composition

`SkeletonMaker > Export Composition...` (Editor menu, not an in-VR control) writes every primitive
currently placed on the main skeleton to a JSON file you choose the location for - each one's
`kind`, `size`, which `Bone_`/`Joint_` anchor it's parented under, and its local position/rotation
offset from that anchor (the same offset `AvatarDuplicateManager` already relies on to mirror a
duplicate correctly, so it's enough to reconstruct the composition later or feed it to another
tool - `RaymarchableElement` already exposes `Kind` + `Size` as exactly the data a future SDF
material would need). Only placed elements are included - not anything still on the table,
currently held, or a stand-in avatar duplicate. `CompositionExporter.BuildCompositionJson()` is the
actual gathering logic, kept separate from the (blocking, interactive) save dialog so it stays
testable/scriptable on its own.

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
  primitive is being placed on. 21 joints matching Unity's HumanBodyBones chain (hips/spine/
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
- `AvatarBodyTarget` - placeholder joint-name -> Transform map for the player's own avatar body,
  meant to eventually sit on whatever Meta's Movement SDK (Body Tracking) drives. Its joint slot
  names are auto-populated from `SkeletonRig.JointNames` so they can never drift out of sync. Every
  slot's transform is null until the Movement SDK is installed and its bone transforms are mapped
  in (e.g. by an adapter reading `OVRSkeleton.BoneId` and assigning by name) - until then this is
  inert plumbing.
- `AvatarDuplicateManager` - on each placement, mirrors a transparent, non-interactive duplicate of
  the placed element onto the matching `AvatarBodyTarget` joint (see below). No-ops safely - no
  `AvatarBodyTarget` in the scene, or that joint's slot unassigned - which is the expected state
  until a real avatar is wired in.

## Avatar / Meta Body package

The player's own visible body is expected to use Meta's **Movement SDK** (Body Tracking) for a
humanoid avatar. That package isn't in Unity's package registry - install it from the Asset Store
or Meta's own scoped registry (`Movement` / `Meta XR All-in-One SDK`) when you're ready to add the
avatar. It wasn't installed here to keep the project buildable without an external download.

Nothing in this project depends on it: grabbing reads the standard XRI controller transforms
(`Left Controller` / `Right Controller` under `XR Origin (XR Rig)/Camera Offset`), which stays
correct regardless of which avatar/body solution is driving the player's visible hands. The
sample's hand-tracking, poke/near-far interactors, and locomotion (teleport/move/turn) were
disabled in the scene since this app doesn't need them - the player is expected to stand still at
the table - and a live thumbstick otherwise doubles as "walk around" input, which would fight the
resize controls.

The "duplicate each placed primitive onto the player's own body" feature is already wired up (see
`AvatarBodyTarget`/`AvatarDuplicateManager` above) and just needs the Movement SDK's actual bone
transforms plugged in once it's installed. Remaining step: write a small adapter that reads the
Movement SDK's body-tracking bone transforms (e.g. `OVRSkeleton`'s bones, matched by
`OVRSkeleton.BoneId`) and assigns them into `AvatarBodyTarget`'s joint slots by name every frame (or
once, if the avatar rig is itself driven by an Animator). Everything downstream - spawning a
transparent, non-interactive duplicate at the matching joint whenever a primitive is placed -
already works without further changes.

**Until then**, the scene has a visible `Avatar Stand-In (Preview)` GameObject - a second copy of
the line-skeleton visual (both its bone segments and its 12 hinge-joint anchors) standing a couple
meters beside the table - so the duplicate feature can actually be seen working today.
`AvatarBodyTarget (Placeholder)`'s joint slots are wired to this stand-in's matching bones/joints
(by name), so placing a primitive on the real skeleton - on a bone or right at a hinge joint -
mirrors a faint transparent copy onto the corresponding spot on the stand-in. This is still **not**
real body tracking (nothing here reads the player's actual body), but the stand-in isn't frozen
either: `Assets/Animations/Dancing.fbx` (a Mixamo mocap clip, Humanoid, bone-only/no mesh so it's
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

**Dancing vs. a pose**: either controller's trigger (XRI's "Activate" action - unused elsewhere in
this project, since only grip and the thumbsticks are already taken) toggles the stand-in between
dancing and standing still in the frozen A-pose. When not dancing, `AvatarDanceSource` copies the
main `SkeletonRig`'s own `Bone_`/`Joint_` local transforms straight onto the stand-in's matching
children every frame instead of reading the dance Animator - the main rig's pose never changes, so
it's always the correct, authoritative "standing still" reference rather than a separately cached
snapshot that could go stale. `AvatarDanceSource.isDancing` is also just a plain serialized bool, so
it can be flipped directly in the Inspector at runtime for quick testing without touching a
controller at all.
When the Movement SDK is installed, repoint `AvatarBodyTarget`'s joint slots at the real tracked
bones (or just delete the stand-in and its wiring) to switch over to the real thing.
