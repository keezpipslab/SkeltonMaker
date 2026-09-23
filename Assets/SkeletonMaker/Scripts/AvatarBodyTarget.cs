using System.Collections.Generic;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// Placeholder joint-name -> Transform map for the player's own avatar body,
    /// meant to eventually sit on whatever Meta's Movement SDK (Body Tracking)
    /// drives. Slot names mirror SkeletonRig's joints so AvatarDuplicateManager
    /// can look up "where on the player's body does this skeleton bone
    /// correspond to" by the same string. Until the Movement SDK is installed
    /// and its bone transforms are assigned into these slots (e.g. by an adapter
    /// mapping OVRSkeleton.BoneId to these names), every slot's transform stays
    /// null and AvatarDuplicateManager simply no-ops.
    /// </summary>
    [ExecuteAlways]
    public class AvatarBodyTarget : MonoBehaviour
    {
        [System.Serializable]
        public class JointSlot
        {
            public string name;
            public Transform transform;
        }

        [SerializeField] private List<JointSlot> joints = new List<JointSlot>();

        public static AvatarBodyTarget Instance { get; private set; }

        private Dictionary<string, Transform> lookup;

        private void Awake()
        {
            Instance = this;
            BuildLookup();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>The avatar transform corresponding to a SkeletonRig joint
        /// name, or null if that slot hasn't been assigned yet (the normal state
        /// before a real avatar is wired in).</summary>
        public Transform GetJoint(string jointName)
        {
            if (lookup == null) BuildLookup();
            return jointName != null && lookup.TryGetValue(jointName, out var t) ? t : null;
        }

        private void BuildLookup()
        {
            lookup = new Dictionary<string, Transform>();
            foreach (var slot in joints)
            {
                if (!string.IsNullOrEmpty(slot.name)) lookup[slot.name] = slot.transform;
            }
        }

        private void OnValidate()
        {
            // Keep the slot list in sync with SkeletonRig's joint names (only
            // adding missing ones, so manually-assigned transforms survive),
            // so this never has to be hand-authored or drift out of sync.
            var existing = new HashSet<string>();
            foreach (var slot in joints)
            {
                if (!string.IsNullOrEmpty(slot.name)) existing.Add(slot.name);
            }

            foreach (var name in SkeletonRig.JointNames)
            {
                if (!existing.Contains(name)) joints.Add(new JointSlot { name = name, transform = null });
            }

            lookup = null; // rebuild lazily; Awake/GetJoint will refresh it
        }
    }
}
