using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace SkeletonMaker
{
    /// <summary>
    /// Lines this headset's world up with the real floor, so that two
    /// instances that both calibrate on the same two floor marks share one
    /// stage. Mark A is the Stage Origin, mark B lies straight ahead of it
    /// (the Stage Origin's forward). The XR Origin is moved, not the scene.
    ///
    /// With the controller: press C, rest the right controller on mark A and
    /// pull the trigger (or press Space), then the same on mark B. Anything
    /// else that can find the two marks - a marker detector - calls
    /// Calibrate() with them directly.
    ///
    /// The result is kept in a file and put back on the next start. It only
    /// holds for as long as the headset's own tracking origin stays where it
    /// was, so it is flagged stale when the runtime reports that moved.
    /// </summary>
    public class StageCalibrator : MonoBehaviour
    {
        [Serializable]
        private class Saved
        {
            public Vector3 originPosition;
            public Quaternion originRotation;
            public float measuredDistance;
            public string savedAtUtc;
        }

        [Tooltip("The XR Origin: what gets moved. Found in the scene if left empty.")]
        [SerializeField] private Transform xrOrigin;

        [Tooltip("What is held on the floor marks: the right controller. Looked up under the XR Origin by name if left empty.")]
        [SerializeField] private Transform pointer;

        [Tooltip("Off: only position across the floor and heading come from the marks, and the floor stays where the headset already has it. On: mark A's height becomes the stage floor too - right for a marker lying flat on the floor, wrong for a controller, whose tracked point is a few centimeters above what it rests on.")]
        [SerializeField] private bool useMarkHeight;

        [Tooltip("File name (without extension) under Application.persistentDataPath.")]
        [SerializeField] private string slot = "stage-calibration";

        private enum Step { Idle, MarkA, MarkB }

        private static StageCalibrator active;

        private Step step;
        private Vector3 markA;
        private InputAction captureAction;
        private bool calibrated;
        private bool stale;
        private float listenFrom;
        private readonly List<XRInputSubsystem> subsystems = new List<XRInputSubsystem>();
        private readonly List<XRInputSubsystem> watched = new List<XRInputSubsystem>();

        /// <summary>True while a trigger pull marks a floor point instead of doing its usual job.</summary>
        public static bool IsCapturing => active != null && active.step != Step.Idle;

        /// <summary>True once this instance's stage has been lined up with the floor and nothing has moved since.</summary>
        public static bool IsCalibrated => active != null && active.calibrated && !active.stale;

        public string FilePath => Path.Combine(Application.persistentDataPath, slot + ".json");

        private void OnEnable()
        {
            active = this;
            listenFrom = Time.unscaledTime + 5f;
            if (captureAction == null)
            {
                captureAction = new InputAction("Mark Floor Point", InputActionType.Button);
                captureAction.AddBinding("<XRController>{RightHand}/triggerPressed");
                captureAction.AddBinding("<Keyboard>/space");
            }
            captureAction.Enable();
        }

        private void OnDisable()
        {
            captureAction.Disable();
            foreach (var subsystem in watched) subsystem.trackingOriginUpdated -= OnTrackingOriginUpdated;
            watched.Clear();
            step = Step.Idle;
            if (active == this) active = null;
        }

        private void Start() => LoadSaved();

        private void Update()
        {
            WatchTrackingOrigin();

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.cKey.wasPressedThisFrame)
            {
                if (step != Step.Idle)
                {
                    step = Step.Idle;
                    Debug.Log("StageCalibrator: cancelled.", this);
                }
                else if (Pointer() == null)
                {
                    Debug.LogWarning("StageCalibrator: no controller to mark the floor with.", this);
                }
                else
                {
                    step = Step.MarkA;
                    Debug.Log("StageCalibrator: rest the right controller on mark A (the stage origin) and pull the trigger.", this);
                }
                return;
            }

            if (step == Step.Idle || !captureAction.WasPressedThisFrame()) return;
            var held = Pointer();
            if (held == null) return;

            Buzz();
            if (step == Step.MarkA)
            {
                markA = held.position;
                step = Step.MarkB;
                Debug.Log("StageCalibrator: mark A taken. Now mark B.", this);
            }
            else
            {
                step = Step.Idle;
                Calibrate(markA, held.position);
            }
        }

        /// <summary>Moves the XR Origin so that worldA - a real floor point, given
        /// where the scene has it right now - lands on the Stage Origin, and worldB
        /// straight ahead of it. False if the two are too close together to give a heading.</summary>
        public bool Calibrate(Vector3 worldA, Vector3 worldB)
        {
            var origin = XrOrigin();
            if (origin == null)
            {
                Debug.LogWarning("StageCalibrator: no XR Origin to move.", this);
                return false;
            }

            StageFrame.StageToWorld(out Vector3 stagePosition, out Quaternion stageRotation);
            Vector3 up = stageRotation * Vector3.up;
            Vector3 ahead = Vector3.ProjectOnPlane(worldB - worldA, up);
            if (ahead.magnitude < 0.2f)
            {
                Debug.LogWarning("StageCalibrator: the two marks are too close together to tell which way the stage faces.", this);
                return false;
            }

            float turn = Vector3.SignedAngle(ahead, stageRotation * Vector3.forward, up);
            origin.RotateAround(worldA, up, turn);
            Vector3 move = stagePosition - worldA;
            if (!useMarkHeight) move -= up * Vector3.Dot(move, up);
            origin.position += move;

            calibrated = true;
            stale = false;

            float measured = Vector3.Distance(worldA, worldB);
            float expected = StageFrame.Instance != null ? StageFrame.Instance.MarkDistance : 0f;
            Debug.Log(expected > 0f
                ? $"StageCalibrator: calibrated. Marks measured {measured:0.000} m apart, {(measured - expected) * 100f:+0.0;-0.0} cm off the {expected:0.000} m they should be."
                : $"StageCalibrator: calibrated. Marks measured {measured:0.000} m apart.", this);

            Save(origin, measured);
            return true;
        }

        private void Save(Transform origin, float measured)
        {
            var saved = new Saved
            {
                originPosition = origin.position,
                originRotation = origin.rotation,
                measuredDistance = measured,
                savedAtUtc = DateTime.UtcNow.ToString("o"),
            };
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(saved, prettyPrint: true));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"StageCalibrator: couldn't write {FilePath}: {e.Message}", this);
            }
        }

        private void LoadSaved()
        {
            if (!File.Exists(FilePath)) return;
            var origin = XrOrigin();
            if (origin == null) return;

            Saved saved;
            try
            {
                saved = JsonUtility.FromJson<Saved>(File.ReadAllText(FilePath));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                Debug.LogError($"StageCalibrator: couldn't read {FilePath}: {e.Message}", this);
                return;
            }
            if (saved == null || saved.originRotation.Equals(default(Quaternion))) return; // an empty or foreign file

            origin.SetPositionAndRotation(saved.originPosition, saved.originRotation);
            calibrated = true;
            Debug.Log($"StageCalibrator: put back the calibration saved at {saved.savedAtUtc}. Calibrate again (C) if the headset has been recentered or the room set up anew since.", this);
        }

        // The subsystem isn't there yet at Start, and can come and go with the headset.
        private void WatchTrackingOrigin()
        {
            SubsystemManager.GetSubsystems(subsystems);
            foreach (var subsystem in subsystems)
            {
                if (watched.Contains(subsystem)) continue;
                subsystem.trackingOriginUpdated += OnTrackingOriginUpdated;
                watched.Add(subsystem);
            }
        }

        private void OnTrackingOriginUpdated(XRInputSubsystem subsystem)
        {
            // The runtime also reports its origin once as the session comes up, which is not a move.
            if (!calibrated || stale || Time.unscaledTime < listenFrom) return;
            stale = true;
            Debug.LogWarning("StageCalibrator: the headset's tracking origin moved (recentered?). The stage is no longer lined up - calibrate again (C).", this);
        }

        private Transform XrOrigin()
        {
            if (xrOrigin == null)
            {
                var found = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
                if (found != null) xrOrigin = found.transform;
            }
            return xrOrigin;
        }

        private Transform Pointer()
        {
            if (pointer == null)
            {
                var origin = XrOrigin();
                if (origin != null)
                {
                    foreach (var child in origin.GetComponentsInChildren<Transform>())
                        if (child.name == "Right Controller") { pointer = child; break; }
                }
            }
            return pointer;
        }

        // So the performer, who can't see the console, knows the point was taken.
        private static void Buzz()
        {
            var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (device.isValid) device.SendHapticImpulse(0, 0.7f, 0.15f);
        }
    }
}
