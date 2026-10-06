using System.Collections.Generic;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// The button that hides what the avatars are drawn with besides the
    /// raymarch - the lines of their skeletons and the see-through copies of
    /// the primitives on them - on your own avatar and on every other
    /// performer's: lit while they are hidden. Only the renderers are
    /// switched off, so the raymarch quad, which reads the objects, still
    /// shows the shapes. Everything is shown again when the button is
    /// switched off with its stage. See PushButton for how it's pressed.
    /// </summary>
    public class HideToggle : PushButton
    {
        private readonly List<Renderer> renderers = new List<Renderer>();

        private bool hidden;
        private AvatarDanceSource avatar;
        private BodyReceiver receiver;

        protected override bool Lit => hidden;

        protected override string Caption => hidden ? "Hide\non" : "Hide\noff";

        protected override void Press()
        {
            hidden = !hidden;
            if (!hidden) Show(true);
        }

        protected override void Update()
        {
            base.Update();

            // Every frame: primitives are placed, and performers arrive, while it is on.
            if (hidden) Show(false);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (!hidden) return;
            hidden = false;
            Show(true);
        }

        private void Show(bool show)
        {
            // Either may be switched off, or not there yet.
            if (avatar == null) avatar = FindFirstObjectByType<AvatarDanceSource>(FindObjectsInactive.Include);
            if (receiver == null) receiver = FindFirstObjectByType<BodyReceiver>(FindObjectsInactive.Include);

            if (avatar != null) Show(avatar.Root, show);
            if (receiver == null) return;
            foreach (var skeleton in receiver.Skeletons.Values)
                if (skeleton != null) Show(skeleton.Body, show);
        }

        private void Show(Transform root, bool show)
        {
            if (root == null) return;
            root.GetComponentsInChildren(true, renderers);
            foreach (var renderer in renderers)
                if (renderer.enabled != show) renderer.enabled = show;
        }
    }
}
