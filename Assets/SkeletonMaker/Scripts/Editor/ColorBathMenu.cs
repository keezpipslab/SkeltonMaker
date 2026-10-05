using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// SkeletonMaker > Add Color Baths: builds a small grid of colored discs
    /// hovering just right of the table, each a ColorBath that paints a held
    /// element dipped into it. Replaces any baths already in the scene.
    /// Move the "Color Baths" object (or single baths) wherever you want
    /// afterwards, and change a bath's color on its ColorBath component.
    /// </summary>
    public static class ColorBathMenu
    {
        private const string RootName = "Color Baths";
        private const string MaterialPath = "Assets/SkeletonMaker/Materials/ColorBath.mat";

        private const float DiscRadius = 0.07f;
        private const float Spacing = 0.17f;
        private const int Columns = 2;

        private static readonly (string name, Color color)[] Baths =
        {
            ("Red", new Color(0.9f, 0.15f, 0.15f)),
            ("Orange", new Color(1f, 0.55f, 0.1f)),
            ("Yellow", new Color(1f, 0.85f, 0.15f)),
            ("Green", new Color(0.2f, 0.75f, 0.25f)),
            ("Blue", new Color(0.15f, 0.4f, 0.95f)),
            ("Purple", new Color(0.6f, 0.25f, 0.85f)),
        };

        [MenuItem("SkeletonMaker/Add Color Baths")]
        public static void AddColorBaths()
        {
            var existing = GameObject.Find(RootName);
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            // Beside the table's right edge, a little above its top, starting
            // at its near edge - mirroring the smoothing knob on the left.
            Vector3 origin = new Vector3(0.9f, 0.9f, 0.7f);
            var table = GameObject.Find("Table");
            var tableRenderer = table != null ? table.GetComponent<Renderer>() : null;
            if (tableRenderer != null)
            {
                Bounds b = tableRenderer.bounds;
                origin = new Vector3(b.max.x + 0.15f, b.max.y + 0.1f, b.min.z + 0.1f);
            }

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Add Color Baths");
            root.transform.position = origin;

            var material = GetOrCreateMaterial();
            for (int i = 0; i < Baths.Length; i++)
            {
                var bath = new GameObject(Baths[i].name + " Bath");
                bath.transform.SetParent(root.transform, false);
                bath.transform.localPosition = new Vector3((i % Columns) * Spacing, 0f, (i / Columns) * Spacing);

                var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                disc.name = "Disc";
                Object.DestroyImmediate(disc.GetComponent<Collider>());
                disc.transform.SetParent(bath.transform, false);
                disc.transform.localScale = new Vector3(DiscRadius * 2f, 0.004f, DiscRadius * 2f);
                var renderer = disc.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;

                var so = new SerializedObject(bath.AddComponent<ColorBath>());
                so.FindProperty("color").colorValue = Baths[i].color;
                so.FindProperty("surface").objectReferenceValue = renderer;
                so.ApplyModifiedPropertiesWithoutUndo(); // also runs OnValidate, which tints the disc
            }

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);
        }

        private static Material GetOrCreateMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat != null) return mat;

            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", Color.white);
            AssetDatabase.CreateAsset(mat, MaterialPath);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}
