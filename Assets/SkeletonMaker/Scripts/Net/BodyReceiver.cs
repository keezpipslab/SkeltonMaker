using System.Collections.Generic;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// Turns the body messages coming in over the OscLink into skeletons on
    /// the stage: one RemoteSkeleton per performer id, made when the first
    /// message from them arrives and removed again once they have been silent
    /// for a while. Only in the stages listed here: in the others nobody else
    /// is on the stage, whatever comes in.
    /// </summary>
    public class BodyReceiver : MonoBehaviour
    {
        [SerializeField] private OscLink link;

        [Tooltip("The quad the remote skeletons' shapes are drawn in. Found in the scene if left empty.")]
        [SerializeField] private RaymarchQuad quad;

        [Tooltip("One per remote performer, in the order they show up.")]
        [SerializeField] private Color[] colors = { new Color(0.45f, 0.75f, 1f), new Color(1f, 0.6f, 0.45f) };

        [Min(1f)]
        [Tooltip("A performer nothing has been heard from for this many seconds is taken off the stage.")]
        [SerializeField] private float forgetAfter = 5f;

        [Tooltip("Where your own body is shown when it comes back in (the link's Accept Own Id), in stage meters from where you really are.")]
        [SerializeField] private Vector3 echoShift = new Vector3(0f, 0f, 1.5f);

        [Tooltip("The stages other performers are shown in. Everywhere, if there is no StageController.")]
        [SerializeField] private Stage[] stagesShown = { Stage.Join, Stage.TogetherMath, Stage.Record };

        private readonly Dictionary<int, RemoteSkeleton> skeletons = new Dictionary<int, RemoteSkeleton>();
        private readonly List<int> gone = new List<int>();
        private int made;
        private int shiftedFromId = int.MaxValue;
        private Vector3 shiftedBy;

        /// <summary>Everyone on the stage right now, by performer id.</summary>
        public IReadOnlyDictionary<int, RemoteSkeleton> Skeletons => skeletons;

        private void OnEnable()
        {
            if (link != null) link.Received += OnReceived;
        }

        private void OnDisable()
        {
            if (link != null) link.Received -= OnReceived;
        }

        /// <summary>Shows every performer with this id or a higher one moved by
        /// shift, in stage meters (a recording, which would otherwise stand
        /// exactly where it was recorded).</summary>
        public void ShiftFrom(int id, Vector3 shift)
        {
            shiftedFromId = id;
            shiftedBy = shift;
        }

        private bool Shown()
        {
            var stages = StageController.Instance;
            return stages == null || System.Array.IndexOf(stagesShown, stages.Current) >= 0;
        }

        private void OnReceived(OscMessage message)
        {
            if (!Shown()) return;
            if (!OscBody.TryParseAddress(message.Address, out int id, out string leaf)) return;
            bool own = id == link.PerformerId;
            if (own && !link.AcceptOwnId) return;

            if (!skeletons.TryGetValue(id, out RemoteSkeleton skeleton))
            {
                skeleton = Make(id);
                skeletons[id] = skeleton;
            }
            skeleton.ExtraShift = own ? echoShift : id >= shiftedFromId ? shiftedBy : Vector3.zero;
            skeleton.InTheRoom = !own && !link.RemoteIsThisPc;
            skeleton.Handle(leaf, message);
        }

        private RemoteSkeleton Make(int id)
        {
            var go = new GameObject($"Remote Skeleton {id}");
            go.transform.SetParent(transform, false);
            var skeleton = go.AddComponent<RemoteSkeleton>();

            Color color = colors != null && colors.Length > 0 ? colors[made++ % colors.Length] : Color.white;
            var rig = MainRig();
            skeleton.Build(rig != null ? rig.LineWidth : 0.012f, rig != null ? rig.LineMaterial : null, color);

            if (quad == null) quad = FindFirstObjectByType<RaymarchQuad>(FindObjectsInactive.Include);
            if (quad != null) quad.AddSource($"Remote {id}", skeleton.Body, color, ShapeGroup.B);

            Debug.Log($"BodyReceiver: performer {id} is on the stage.", this);
            return skeleton;
        }

        /// <summary>The scene's real skeleton (not the tutorial's practice stick), switched on or not.</summary>
        public static SkeletonRig MainRig()
        {
            foreach (var rig in FindObjectsByType<SkeletonRig>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!rig.IsStick) return rig;
            return null;
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            bool shown = Shown();
            gone.Clear();
            foreach (var pair in skeletons)
                if (!shown || now - pair.Value.LastHeard > forgetAfter) gone.Add(pair.Key);

            foreach (int id in gone)
            {
                var skeleton = skeletons[id];
                skeletons.Remove(id);
                if (quad != null) quad.RemoveSource(skeleton.Body);
                Destroy(skeleton.gameObject);
                Debug.Log($"BodyReceiver: performer {id} has left the stage.", this);
            }
        }
    }
}
