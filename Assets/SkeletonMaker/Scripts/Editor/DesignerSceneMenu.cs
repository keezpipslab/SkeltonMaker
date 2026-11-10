using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// SkeletonMaker > Create Designer Scene: makes Scenes/Designer.unity, the
    /// scene for designing a raymarched world and exporting it. It starts as
    /// a copy of SkeletonBuilder.unity, so the XR rig and the raymarch quad
    /// are the ones that are known to work, with everything else taken out:
    /// no table, skeleton, avatar, stages or network.
    ///
    /// What it adds: "World", which the placed primitives become children of,
    /// and "Designer Panel", holding one slot per primitive kind, the
    /// smoothing and inflate dials, the Hide, Export, Save and Load buttons and the handle
    /// the whole panel is moved by. The primitives are the table's prefabs
    /// with SkeletonPlacement swapped for WorldElement, saved under
    /// Prefabs/Designer. Running it again builds the scene afresh.
    /// </summary>
    public static class DesignerSceneMenu
    {
        private const string SourceScene = "Assets/SkeletonMaker/Scenes/SkeletonBuilder.unity";
        private const string ScenePath = "Assets/SkeletonMaker/Scenes/Designer.unity";
        private const string PrefabFolder = "Assets/SkeletonMaker/Prefabs";
        private const string DesignerFolder = "Designer";

        // What is kept of the source scene.
        private static readonly string[] Kept = { "XR Origin (XR Rig)", "OVR Manager", "Raymarch Quad", "Size Handles" };

        // In the table's order, two rows of six.
        private static readonly PrimitiveKind[] Palette =
        {
            PrimitiveKind.Sphere, PrimitiveKind.Box, PrimitiveKind.Capsule, PrimitiveKind.Cylinder, PrimitiveKind.Pyramid, PrimitiveKind.Cone,
            PrimitiveKind.Octahedron, PrimitiveKind.HexagonalPrism, PrimitiveKind.TriangularPrism, PrimitiveKind.Torus, PrimitiveKind.RoundBox, PrimitiveKind.Link,
        };

        // The panel, in its own axes: X to the viewer's right, Z up the panel, Y at the viewer.
        private const int Columns = 6;
        private const float SlotSpacing = 0.2f;
        private const float TopRow = 0.34f;
        private const float ControlRow = -0.2f;
        private const float ControlSpacing = 0.2f;
        private const float HandleRow = -0.47f;

        // A world is bigger than what goes on a skeleton.
        private static readonly Vector3 MaxSize = new Vector3(3f, 3f, 3f);

        private static readonly Color WorldColor = new Color(0.85f, 0.88f, 0.95f);

        [MenuItem("SkeletonMaker/Create Designer Scene")]
        private static void CreateDesignerScene()
        {
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Create Designer Scene", ScenePath + " is already there. Build it afresh? Whatever was changed in it by hand is lost.", "Build afresh", "Cancel"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Build();
        }

        /// <summary>Builds the scene and leaves it open. Replaces the one that's
        /// there, and whatever scene is open, without asking.</summary>
        public static void Build()
        {
            AssetDatabase.DeleteAsset(ScenePath);
            if (!AssetDatabase.CopyAsset(SourceScene, ScenePath))
            {
                Debug.LogError("DesignerSceneMenu: could not copy " + SourceScene);
                return;
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects())
                if (System.Array.IndexOf(Kept, root.name) < 0) Object.DestroyImmediate(root);

            var quad = Object.FindFirstObjectByType<RaymarchQuad>(FindObjectsInactive.Include);
            if (quad == null)
            {
                Debug.LogError("DesignerSceneMenu: no RaymarchQuad in " + SourceScene);
                return;
            }

            var prefabs = new GameObject[Palette.Length];
            for (int i = 0; i < Palette.Length; i++) prefabs[i] = WorldElementPrefab(Palette[i]);

            var world = new GameObject("World").AddComponent<DesignWorld>();
            world.gameObject.AddComponent<WorldGrab>();
            new GameObject("Controller Button Labels").AddComponent<ControllerButtonLabels>();
            var soWorld = new SerializedObject(world);
            soWorld.FindProperty("quad").objectReferenceValue = quad;
            var soPrefabs = soWorld.FindProperty("elementPrefabs"); // what Load rebuilds a saved world from
            soPrefabs.arraySize = prefabs.Length;
            for (int i = 0; i < prefabs.Length; i++) soPrefabs.GetArrayElementAtIndex(i).objectReferenceValue = prefabs[i];
            soWorld.ApplyModifiedPropertiesWithoutUndo();

            SetUpQuad(quad, world.transform);
            BuildPanel(quad, prefabs);

            EditorSceneManager.SaveScene(scene);
            Debug.Log("DesignerSceneMenu: built " + ScenePath);
        }

        // Shows the world and what is in a hand, and nothing else: not the
        // primitives waiting on the panel, which Show Table would draw.
        private static void SetUpQuad(RaymarchQuad quad, Transform world)
        {
            quad.sources.Clear();
            quad.sources.Add(new RaymarchQuad.ShapeSource { label = "World", root = world, visible = true, color = WorldColor });
            quad.showTable = false;
            quad.showHeld = true;
            quad.heldColor = WorldColor;
            quad.combine = CombineMode.SubtractB; // the primitives switched to subtract are group B
            quad.inflate = 0f;
            quad.repeat = 0f;

            // The left X button would bring Show Table back.
            var soQuad = new SerializedObject(quad);
            soQuad.FindProperty("contextToggleAction").objectReferenceValue = null;
            soQuad.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(quad);

            // A primitive within reach is in front of a standing quad, where
            // the quad shows nothing; on the head, everything is behind it.
            if (quad.TryGetComponent(out RaymarchQuadGrab grab))
            {
                var soGrab = new SerializedObject(grab);
                soGrab.FindProperty("startOnHead").boolValue = true;
                soGrab.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void BuildPanel(RaymarchQuad quad, GameObject[] prefabs)
        {
            // Upright in front of where the player stands, facing them. Its
            // axes are a dial's: up points at the viewer, forward up the panel.
            var panel = new GameObject("Designer Panel").transform;
            panel.SetPositionAndRotation(new Vector3(0f, 1.15f, 0.6f), Quaternion.LookRotation(Vector3.up, Vector3.back));

            // A primitive stands as it does on the table: its own up along the panel's.
            Quaternion upright = Quaternion.Euler(90f, 0f, 0f);
            for (int i = 0; i < Palette.Length; i++)
            {
                var slot = new GameObject(Palette[i] + " Slot");
                slot.transform.SetParent(panel, false);
                float x = (i % Columns - (Columns - 1) * 0.5f) * SlotSpacing;
                float z = TopRow - (i / Columns) * SlotSpacing;
                slot.transform.SetLocalPositionAndRotation(new Vector3(x, 0f, z), upright);

                var so = new SerializedObject(slot.AddComponent<PaletteSlot>());
                so.FindProperty("primitivePrefab").objectReferenceValue = prefabs[i];
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            Vector3 Control(int index) => panel.TransformPoint(new Vector3((index - 2.5f) * ControlSpacing, 0f, ControlRow));

            SmoothingKnobMenu.BuildKnob<SmoothingKnob>("Smoothing Knob", Control(0), panel.rotation, quad, 0f, 0.3f).transform.SetParent(panel, true);
            SmoothingKnobMenu.BuildKnob<InflateKnob>("Inflate Knob", Control(1), panel.rotation, quad, -0.15f, 0.15f).transform.SetParent(panel, true);
            AddButton<PreviewHideToggle>("Hide Button", Control(2), panel);
            AddButton<ExportButton>("Export Button", Control(3), panel);
            AddButton<WorldStoreButton>("Save Button", Control(4), panel);
            var soLoad = new SerializedObject(AddButton<WorldStoreButton>("Load Button", Control(5), panel));
            soLoad.FindProperty("load").boolValue = true;
            soLoad.ApplyModifiedPropertiesWithoutUndo();

            var handle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            handle.name = "Handle";
            handle.transform.SetParent(panel, false);
            handle.transform.localPosition = new Vector3(0f, 0f, HandleRow);
            handle.transform.localScale = new Vector3(0.5f, 0.03f, 0.03f);
            handle.GetComponent<BoxCollider>().isTrigger = true;
            handle.GetComponent<MeshRenderer>().sharedMaterial = SmoothingKnobMenu.GetOrCreateMaterial("KnobDisk", new Color(0.18f, 0.2f, 0.26f), false);

            var soGrab = new SerializedObject(panel.gameObject.AddComponent<PanelGrab>());
            soGrab.FindProperty("handle").objectReferenceValue = handle.GetComponent<BoxCollider>();
            SmoothingKnobMenu.SetHands(soGrab.FindProperty("hands"), "grip", "Select");
            soGrab.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T AddButton<T>(string name, Vector3 position, Transform panel) where T : PushButton
        {
            var button = ButtonsMenu.BuildButton<T>(name, position, panel.rotation);
            button.transform.SetParent(panel, true);

            var so = new SerializedObject(button);
            SmoothingKnobMenu.SetHands(so.FindProperty("hands"), "trigger", "Activate");
            so.ApplyModifiedPropertiesWithoutUndo();
            return button;
        }

        /// <summary>The table's prefab of this kind, as a designer primitive:
        /// placed in the world instead of on a skeleton, and allowed to grow
        /// much bigger.</summary>
        private static GameObject WorldElementPrefab(PrimitiveKind kind)
        {
            if (!AssetDatabase.IsValidFolder(PrefabFolder + "/" + DesignerFolder)) AssetDatabase.CreateFolder(PrefabFolder, DesignerFolder);

            string source = $"{PrefabFolder}/{kind} Element.prefab";
            string path = $"{PrefabFolder}/{DesignerFolder}/{kind} World Element.prefab";

            var contents = PrefabUtility.LoadPrefabContents(source);
            try
            {
                if (contents.TryGetComponent(out SkeletonPlacement placement)) Object.DestroyImmediate(placement);
                if (!contents.TryGetComponent(out WorldElement _)) contents.AddComponent<WorldElement>();

                var so = new SerializedObject(contents.GetComponent<RaymarchableElement>());
                so.FindProperty("maxSize").vector3Value = MaxSize;
                so.ApplyModifiedPropertiesWithoutUndo();

                return PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
    }
}
