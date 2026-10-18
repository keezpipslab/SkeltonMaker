using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SkeletonMaker
{
    /// <summary>
    /// Lets either hand's grip pick up and move the designer panel, with
    /// everything on it: squeeze the grip while that hand is at the panel's
    /// handle. A plain reparent with worldPositionStays=true, as
    /// RaymarchQuadGrab does for the quad, and kept apart from
    /// Grabbable/HandGrabber for the same reason: the panel is not something
    /// to place, and its primitives must not count as held along with it.
    /// </summary>
    public class PanelGrab : MonoBehaviour
    {
        [SerializeField] private Knob.Hand[] hands;
        [Tooltip("The bar the panel is picked up by.")]
        [SerializeField] private Collider handle;
        [Tooltip("How close (meters) the hand must be to the handle for the grip to take it.")]
        [SerializeField] private float grabRadius = 0.06f;

        /// <summary>The controllers whose grip is holding a panel just now.</summary>
        public static readonly HashSet<Transform> Holding = new HashSet<Transform>();

        private int holder = -1;
        private Action<InputAction.CallbackContext>[] performed;
        private Action<InputAction.CallbackContext>[] canceled;

        private void OnEnable()
        {
            if (hands == null) return;
            performed = new Action<InputAction.CallbackContext>[hands.Length];
            canceled = new Action<InputAction.CallbackContext>[hands.Length];
            for (int i = 0; i < hands.Length; i++)
            {
                if (hands[i].grip == null) continue;
                int index = i;
                performed[i] = _ => OnGrip(index);
                canceled[i] = _ => OnLetGo(index);
                hands[i].grip.action.Enable();
                hands[i].grip.action.performed += performed[i];
                hands[i].grip.action.canceled += canceled[i];
            }
        }

        private void OnDisable()
        {
            if (hands != null)
            {
                for (int i = 0; i < hands.Length && performed != null; i++)
                {
                    if (hands[i].grip == null || performed[i] == null) continue;
                    hands[i].grip.action.performed -= performed[i];
                    hands[i].grip.action.canceled -= canceled[i];
                }
            }
            if (holder >= 0) OnLetGo(holder);
        }

        private void OnGrip(int index)
        {
            if (holder >= 0 || handle == null || hands[index].controller == null) return;

            Vector3 hand = hands[index].controller.position;
            if (SizeHandles.Claims(hand)) return; // the grip is pulling a size cube instead
            if (Vector3.Distance(hand, handle.ClosestPoint(hand)) > grabRadius) return;

            holder = index;
            Holding.Add(hands[index].controller);
            transform.SetParent(hands[index].controller, true);
        }

        private void OnLetGo(int index)
        {
            if (holder != index) return;
            Holding.Remove(hands[index].controller);
            holder = -1;
            transform.SetParent(null, true);
        }
    }
}
