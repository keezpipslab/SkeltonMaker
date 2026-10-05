using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// SkeletonMaker > Add Stages: adds the "Stages" object (StageController),
    /// the "Tutorial Guide" text above the table and the "Practice Stick"
    /// where the skeleton will stand, and sorts the scene into stages - the
    /// tutorial starts with only the table and its Sphere and Box slots; the
    /// skeleton, the avatar stand-in, the raymarch quad, the smoothing knob,
    /// the color baths and the other ten slots belong to the Build stage (the
    /// guide switches the color baths and the quad on early, at their steps).
    /// Replaces what a previous run added; run it again after adding
    /// something to the scene that the tutorial shouldn't show, or edit the
    /// lists on the Stages object by hand.
    /// </summary>
    public static class StagesMenu
    {
        private const string StagesName = "Stages";
        private const string GuideName = "Tutorial Guide";
        private const string StickName = "Practice Stick";
        private const string StickSourceLabel = "Tutorial stick";

        private static readonly PrimitiveKind[] TutorialKinds = { PrimitiveKind.Sphere, PrimitiveKind.Box };

        // Scene objects that only make sense once there's a skeleton to build.
        private static readonly string[] BuildObjectNames = { "Skeleton", "Avatar Stand-In (Preview)", "Color Baths" };

        [MenuItem("SkeletonMaker/Add Stages")]
        public static void AddStages()
        {
            var existingGuide = Object.FindFirstObjectByType<TutorialGuide>(FindObjectsInactive.Include);
            if (existingGuide != null) Undo.DestroyObjectImmediate(existingGuide.gameObject);

            SkeletonRig mainRig = null;
            foreach (var rig in Object.FindObjectsByType<SkeletonRig>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (rig.IsStick) Undo.DestroyObjectImmediate(rig.gameObject);
                else mainRig = rig;
            }

            var controller = Object.FindFirstObjectByType<StageController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                var go = new GameObject(StagesName);
                Undo.RegisterCreatedObjectUndo(go, "Add Stages");
                controller = go.AddComponent<StageController>();
            }

            var buildObjects = new List<GameObject>();
            foreach (string name in BuildObjectNames)
            {
                var go = GameObject.Find(name);
                if (go != null) buildObjects.Add(go);
                else Debug.LogWarning($"StagesMenu: no '{name}' in the open scene, left out of the Build stage.");
            }

            var quad = Object.FindFirstObjectByType<RaymarchQuad>(FindObjectsInactive.Include);
            if (quad != null) buildObjects.Add(quad.gameObject);
            var knob = Object.FindFirstObjectByType<SmoothingKnob>(FindObjectsInactive.Include);
            if (knob != null) buildObjects.Add(knob.gameObject);

            // A slot that's switched off never spawns its shape, so the table
            // starts with just the tutorial's two and fills up when Build begins.
            int tutorialSlots = 0;
            foreach (var slot in Object.FindObjectsByType<TableSpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var prefab = new SerializedObject(slot).FindProperty("primitivePrefab").objectReferenceValue as GameObject;
                var element = prefab != null ? prefab.GetComponent<RaymarchableElement>() : null;
                if (element != null && System.Array.IndexOf(TutorialKinds, element.Kind) >= 0) tutorialSlots++;
                else buildObjects.Add(slot.gameObject);
            }

            var stick = BuildStick(mainRig);
            ShowStickInQuad(quad, stick);

            var guide = BuildGuide();
            var soGuide = new SerializedObject(guide.GetComponent<TutorialGuide>());
            soGuide.FindProperty("stick").objectReferenceValue = stick;
            var baths = GameObject.Find("Color Baths");
            SetObjects(soGuide.FindProperty("colorObjects"), baths != null ? new List<GameObject> { baths } : new List<GameObject>());
            SetObjects(soGuide.FindProperty("windowObjects"), quad != null ? new List<GameObject> { quad.gameObject } : new List<GameObject>());
            soGuide.ApplyModifiedPropertiesWithoutUndo();

            var so = new SerializedObject(controller);
            SetObjects(so.FindProperty("tutorialObjects"), new List<GameObject> { guide, stick.gameObject });
            SetObjects(so.FindProperty("buildObjects"), buildObjects);
            so.ApplyModifiedProperties();
            MathStageMenu.WireStage(controller); // the Math stage follows the Build stage's list

            Selection.activeGameObject = controller.gameObject;
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            Debug.Log($"Stages: the tutorial keeps {tutorialSlots} table slot(s); {buildObjects.Count} object(s) wait for the Build stage.");
        }

        // One upright bone standing where the skeleton's spine will be, so the
        // raymarch quad frames it just as it frames the skeleton later.
        private static SkeletonRig BuildStick(SkeletonRig mainRig)
        {
            var go = new GameObject(StickName);
            Undo.RegisterCreatedObjectUndo(go, "Add Stages");
            go.SetActive(false); // so the rig is first built as a stick, not as a whole skeleton
            var stick = go.AddComponent<SkeletonRig>();

            var so = new SerializedObject(stick);
            so.FindProperty("stick").boolValue = true;
            if (mainRig != null)
            {
                go.transform.SetPositionAndRotation(mainRig.transform.TransformPoint(new Vector3(0f, 0.95f, 0f)), mainRig.transform.rotation);
                var soMain = new SerializedObject(mainRig);
                foreach (string look in new[] { "lineWidth", "lineMaterial", "lineColor" })
                    so.CopyFromSerializedProperty(soMain.FindProperty(look));
            }
            else
            {
                go.transform.position = new Vector3(0f, 0.95f, 1.9f);
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            go.SetActive(true);
            return stick;
        }

        // The quad only draws what's under one of its sources, so the stick
        // has to be one for the shapes placed on it to show up.
        private static void ShowStickInQuad(RaymarchQuad quad, SkeletonRig stick)
        {
            if (quad == null) return;

            Undo.RecordObject(quad, "Add Stages");
            quad.sources.RemoveAll(source => source == null || source.root == null || source.label == StickSourceLabel);
            quad.sources.Add(new RaymarchQuad.ShapeSource
            {
                label = StickSourceLabel,
                root = stick.transform,
                visible = true,
                color = new Color(0.95f, 0.7f, 0.45f),
            });
            EditorUtility.SetDirty(quad);
        }

        private static GameObject BuildGuide()
        {
            // Floating above the table's far edge, high enough not to hide the stick behind it.
            Vector3 position = new Vector3(0f, 1.8f, 1.4f);
            var table = GameObject.Find("Table");
            var tableRenderer = table != null ? table.GetComponent<Renderer>() : null;
            if (tableRenderer != null)
            {
                Bounds b = tableRenderer.bounds;
                position = new Vector3(b.center.x, b.max.y + 1.0f, b.max.z);
            }

            var root = new GameObject(GuideName);
            Undo.RegisterCreatedObjectUndo(root, "Add Stages");
            root.transform.position = position;

            var text = root.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.font = font;
            root.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            text.fontSize = 64;
            text.characterSize = 0.005f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            text.text = "Tutorial";

            var so = new SerializedObject(root.AddComponent<TutorialGuide>());
            so.FindProperty("label").objectReferenceValue = text;
            so.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }

        private static void SetObjects(SerializedProperty list, List<GameObject> objects)
        {
            list.arraySize = objects.Count;
            for (int i = 0; i < objects.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = objects[i];
        }
    }
}
