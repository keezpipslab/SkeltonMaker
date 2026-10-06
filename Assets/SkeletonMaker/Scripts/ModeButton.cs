using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// One of the three buttons that choose what the avatar follows - Still
    /// (the A-pose), Animation (the dance) or You (your tracked body): lit
    /// while its mode is the current one. See PushButton for how it's pressed.
    /// </summary>
    public class ModeButton : PushButton
    {
        [SerializeField] private AvatarMode mode;

        private AvatarDanceSource avatar;

        // The avatar is switched off in some stages, so it can't be found once and for all at startup.
        private AvatarDanceSource Avatar
        {
            get
            {
                if (avatar == null) avatar = FindFirstObjectByType<AvatarDanceSource>(FindObjectsInactive.Include);
                return avatar;
            }
        }

        protected override bool Lit => Avatar != null && Avatar.Mode == mode;

        protected override string Caption => mode == AvatarMode.Tracking ? "You" : mode.ToString();

        protected override void Press()
        {
            if (Avatar == null) return;
            if (mode == AvatarMode.Tracking && !Avatar.CanTrack) return; // no tracked body to follow
            Avatar.Mode = mode;
        }
    }
}
