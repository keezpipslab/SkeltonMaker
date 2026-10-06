using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SkeletonMaker
{
    /// <summary>
    /// Decides whether the headset shows the real room (Meta passthrough)
    /// behind the scene, or the skybox. Three things have a say:
    ///
    /// - the app's stage: the room is shown in the stages listed here (none
    ///   for now), the skybox in the others;
    /// - T on the keyboard flips that until the next stage change;
    /// - Require(): anything that makes not seeing the room unsafe - a remote
    ///   performer drawn right where a real person stands - holds it on, and
    ///   T can't switch it off for as long as that lasts.
    ///
    /// Showing the room means three things at once: the passthrough layer is
    /// unhidden, the camera clears to transparent instead of to the skybox
    /// (passthrough is composited underneath the scene, so it shows wherever
    /// nothing was drawn), and the raymarch quad stops filling in its
    /// background. If passthrough isn't running - no headset, or Link without
    /// its passthrough option - the skybox simply stays.
    ///
    /// Needs an OVRManager with Enable Passthrough in the scene; see
    /// SkeletonMaker > Add Passthrough.
    /// </summary>
    public class PassthroughView : MonoBehaviour
    {
        private static readonly HashSet<object> required = new HashSet<object>();

        [SerializeField] private OVRPassthroughLayer layer;

        [Tooltip("The headset camera. Camera.main if left empty.")]
        [SerializeField] private Camera viewCamera;

        [Tooltip("Stops drawing its dark background while the room is shown, so only the shapes are left.")]
        [SerializeField] private RaymarchQuad quad;

        [Tooltip("The stages that start out showing the room.")]
        [SerializeField] private Stage[] stagesShown = { };

        private bool wanted;
        private bool applied;
        private bool cameraSaved;
        private CameraClearFlags savedClearFlags;
        private Color savedBackground;
        private StageController stages;

        /// <summary>True while the room is what's behind the scene.</summary>
        public static bool Showing { get; private set; }

        /// <summary>Holds the room on for as long as who needs it. Call again with false (or when who goes away) to let go.</summary>
        public static void Require(object who, bool needed)
        {
            if (needed) required.Add(who);
            else required.Remove(who);
        }

        private void Start()
        {
            stages = StageController.Instance;
            if (stages != null)
            {
                stages.Changed += OnStageChanged;
                OnStageChanged(stages.Current);
            }
            if (layer != null) layer.hidden = true;
        }

        private void OnDestroy()
        {
            if (stages != null) stages.Changed -= OnStageChanged;
        }

        private void OnDisable() => Apply(false);

        private void OnStageChanged(Stage stage) => wanted = Array.IndexOf(stagesShown, stage) >= 0;

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.tKey.wasPressedThisFrame)
            {
                wanted = !wanted;
                if (!wanted && required.Count > 0)
                    Debug.Log("PassthroughView: the room stays on while another performer is drawn where they really stand.", this);
            }

            Apply((wanted || required.Count > 0) && layer != null && OVRManager.IsInsightPassthroughInitialized());
        }

        private void Apply(bool show)
        {
            if (show == applied) return;
            applied = show;
            Showing = show;

            if (layer != null) layer.hidden = !show;
            if (quad != null) quad.clipBackground = show;

            if (viewCamera == null) viewCamera = Camera.main;
            if (viewCamera == null) return;
            if (show)
            {
                savedClearFlags = viewCamera.clearFlags;
                savedBackground = viewCamera.backgroundColor;
                cameraSaved = true;
                viewCamera.clearFlags = CameraClearFlags.SolidColor;
                viewCamera.backgroundColor = Color.clear;
            }
            else if (cameraSaved)
            {
                viewCamera.clearFlags = savedClearFlags;
                viewCamera.backgroundColor = savedBackground;
            }
        }
    }
}
