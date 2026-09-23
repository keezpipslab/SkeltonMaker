using System;
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
        public bool IsHeld { get; private set; }
        public Transform HoldingHand { get; private set; }

        public event Action Grabbed;
        public event Action Released;

        public void Grab(Transform hand)
        {
            if (IsHeld || hand == null) return;
            IsHeld = true;
            HoldingHand = hand;
            transform.SetParent(hand, true);
            Grabbed?.Invoke();
        }

        public void Release()
        {
            if (!IsHeld) return;
            IsHeld = false;
            HoldingHand = null;
            transform.SetParent(null, true);
            Released?.Invoke();
        }
    }
}
