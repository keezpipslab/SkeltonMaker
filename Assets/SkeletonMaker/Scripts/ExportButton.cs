namespace SkeletonMaker
{
    /// <summary>
    /// The designer's Export button: bakes the world into a mesh and writes
    /// it to an .obj file (DesignWorld.Export). Lit while that is going on,
    /// and its text says how it went. See PushButton for how it's pressed.
    /// </summary>
    public class ExportButton : PushButton
    {
        protected override bool Lit => DesignWorld.Instance != null && DesignWorld.Instance.Exporting;

        protected override string Caption
        {
            get
            {
                var world = DesignWorld.Instance;
                return world == null || string.IsNullOrEmpty(world.Status) ? "Export" : "Export\n" + world.Status;
            }
        }

        protected override void Press()
        {
            if (DesignWorld.Instance != null) DesignWorld.Instance.Export();
        }
    }
}
