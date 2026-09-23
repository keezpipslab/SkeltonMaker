using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// Generic pick-up-and-hold mechanics. Grabbing is a plain reparent onto
    /// the hand with worldPositionStays=true, so the element keeps exactly
    /// the orientation and offset it was grabbed in - no snapping.
    /// </summary>
    public class Grabbable : MonoBehaviour
    {
        /// <summary>Every Grabbable currently in a hand (e.g. so the raymarch quad can show it).</summary>
        public static readonly List<Grabbable> Held = new List<Grabbable>();

        public bool IsHeld { get; private set; }
        public Transform HoldingHand { get; private set; }

        public event Action Grabbed;
        public event Action Released;

        public void Grab(Transform hand)
        {
            if (IsHeld || hand == null) return;
            IsHeld = true;
            HoldingHand = hand;
            Held.Add(this);
            transform.SetParent(hand, true);
            Grabbed?.Invoke();
        }

        private void OnDisable() => Held.Remove(this);

        public void Release()
        {
            if (!IsHeld) return;
            IsHeld = false;
            HoldingHand = null;
            Held.Remove(this);
            transform.SetParent(null, true);
            Released?.Invoke();
        }
    }
}
