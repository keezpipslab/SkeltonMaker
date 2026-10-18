using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// The designer's Save and Load buttons (DesignWorld.Save / Load); which of
    /// the two one is, is its Load tick. Its text says how its last press
    /// went. See PushButton for how it's pressed.
    /// </summary>
    public class WorldStoreButton : PushButton
    {
        [Tooltip("Ticked: loads the saved world. Unticked: saves this one.")]
        [SerializeField] private bool load;

        protected override string Caption
        {
            get
            {
                string name = load ? "Load" : "Save";
                var world = DesignWorld.Instance;
                string status = world == null ? "" : load ? world.LoadStatus : world.SaveStatus;
                return string.IsNullOrEmpty(status) ? name : name + "\n" + status;
            }
        }

        protected override void Press()
        {
            var world = DesignWorld.Instance;
            if (world == null) return;

            if (load) world.Load();
            else world.Save();
        }
    }
}
