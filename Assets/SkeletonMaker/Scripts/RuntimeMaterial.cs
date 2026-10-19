using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// A material for things made in code (GameObject.CreatePrimitive cubes,
    /// spheres, ...). The one CreatePrimitive gives them is the pipeline's
    /// default Lit, which is fine in the editor but fuchsia in a player build
    /// (the standalone Quest): no material in the build uses that variant, so
    /// it is stripped. Resources/RuntimeOpaque is a copy of PrimitiveMat - opaque
    /// Lit, no keywords - so its variant is always built, and copies of it
    /// only differ in color.
    /// </summary>
    public static class RuntimeMaterial
    {
        private static Material template;

        /// <summary>A new opaque Lit material of this color.</summary>
        public static Material Opaque(Color color)
        {
            if (template == null) template = Resources.Load<Material>("RuntimeOpaque");
            var material = template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = "RuntimeOpaque";
            material.color = color;
            return material;
        }

        /// <summary>Gives a renderer its own opaque material of this color.</summary>
        public static void Apply(Renderer renderer, Color color)
        {
            if (renderer.sharedMaterial != null && renderer.sharedMaterial.name == "RuntimeOpaque")
                renderer.sharedMaterial.color = color; // already one of ours
            else
                renderer.sharedMaterial = Opaque(color);
        }
    }
}
