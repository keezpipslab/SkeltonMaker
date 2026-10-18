using UnityEngine;
using UnityEngine.InputSystem;

namespace SkeletonMaker
{
    /// <summary>
    /// On the table: the G key takes the table away and brings it back. Only
    /// the table's own renderers and colliders (and those of its children)
    /// are switched off, so its spawn points keep working and the primitives
    /// on it - which are not its children - stay where they are, floating
    /// at their slots.
    /// </summary>
    public class TableHideToggle : MonoBehaviour
    {
        [SerializeField] private Key key = Key.G;

        private bool hidden;

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[key].wasPressedThisFrame) SetHidden(!hidden);
        }

        public void SetHidden(bool hide)
        {
            hidden = hide;
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = !hide;
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = !hide;
        }
    }
}
