namespace SkeletonMaker
{
    /// <summary>
    /// The designer's Hide button: lit while the meshes of the primitives
    /// placed in the world are not drawn, leaving only the raymarched
    /// surface. Only their renderers are switched off (WorldElement does it),
    /// so the raymarch quad still shows them and they can still be picked up;
    /// one that is in a hand, and the ones on the panel, stay visible. See
    /// PushButton for how it's pressed.
    /// </summary>
    public class PreviewHideToggle : PushButton
    {
        protected override bool Lit => WorldElement.PreviewHidden;

        protected override string Caption => WorldElement.PreviewHidden ? "Hide\non" : "Hide\noff";

        protected override void Press() => WorldElement.PreviewHidden = !WorldElement.PreviewHidden;

        protected override void OnDisable()
        {
            base.OnDisable();
            WorldElement.PreviewHidden = false;
        }
    }
}
