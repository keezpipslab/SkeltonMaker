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
    /// Math is Build plus the math dials. Join is the avatar, the quad, the
    /// smoothing knob and the mode buttons: no table, no shapes to build with
    /// and no skeleton to hang them on (LooseShapes, added here to the Stages
    /// object, takes the shapes lying on the table away). The hidden Record
    /// stage is the skeleton, the avatar, the quad and the "Record Guide" text.
    /// Replaces what a previous run added; run it again after adding
    /// something to the scene that the tutorial shouldn't show, or edit the
    /// lists on the Stages object by hand. WireStages is the sorting alone,
    /// which the other menus run after adding something of their own.
    /// </summary>
    public static class StagesMenu
    {
        private const string StagesName = "Stages";
        private const string GuideName = "Tutorial Guide";
        private const string StickName = "Practice Stick";
        private const string StickSourceLabel = "Tutorial stick";
        internal const string RecordGuideName = "Record Guide";

        // What the hidden Record stage shows, besides the quad and its text.
        private static readonly string[] RecordObjectNames = { "Table", "Skeleton", "Avatar Stand-In (Preview)" };

        // What is left of the builder in Join, besides the quad, the smoothing knob and the mode buttons.
        private static readonly string[] JoinObjectNames = { "Avatar Stand-In (Preview)" };

        // There in every stage but Join, so it has to be listed in all the others.
        private const string TableName = "Table";

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
            if (controller.GetComponent<LooseShapes>() == null) Undo.AddComponent<LooseShapes>(controller.gameObject);

            var quad = Object.FindFirstObjectByType<RaymarchQuad>(FindObjectsInactive.Include);

            var stick = BuildStick(mainRig);
            ShowStickInQuad(quad, stick);

            var oldRecordGuide = FindByName(RecordGuideName);
            if (oldRecordGuide != null) Undo.DestroyObjectImmediate(oldRecordGuide);
            var recordGuide = BuildText(RecordGuideName, "Record yourself");
            recordGuide.SetActive(false);

            var guide = BuildText(GuideName, "Tutorial");
            var soTutorial = new SerializedObject(guide.AddComponent<TutorialGuide>());
            soTutorial.FindProperty("label").objectReferenceValue = guide.GetComponent<TextMesh>();
            soTutorial.ApplyModifiedPropertiesWithoutUndo();
            var soGuide = new SerializedObject(guide.GetComponent<TutorialGuide>());
            soGuide.FindProperty("stick").objectReferenceValue = stick;
            var baths = GameObject.Find("Color Baths");
            SetObjects(soGuide.FindProperty("colorObjects"), baths != null ? new List<GameObject> { baths } : new List<GameObject>());
            SetObjects(soGuide.FindProperty("windowObjects"), quad != null ? new List<GameObject> { quad.gameObject } : new List<GameObject>());
            soGuide.ApplyModifiedPropertiesWithoutUndo();

            WireStages(controller);

            Selection.activeGameObject = controller.gameObject;
        }

        /// <summary>Sorts what is in the scene into the stages' lists, and hands
        /// the Record stage's text to the TestPartner that writes it.</summary>
        public static void WireStages(StageController controller)
        {
            var tutorialObjects = new List<GameObject>();
            var guide = Object.FindFirstObjectByType<TutorialGuide>(FindObjectsInactive.Include);
            if (guide != null) tutorialObjects.Add(guide.gameObject);
            foreach (var rig in Object.FindObjectsByType<SkeletonRig>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (rig.IsStick) tutorialObjects.Add(rig.gameObject);

            var table = FindByName(TableName);
            if (table != null) tutorialObjects.Add(table);

            var buildObjects = new List<GameObject>();
            if (table != null) buildObjects.Add(table);
            foreach (string name in BuildObjectNames)
            {
                var go = FindByName(name);
                if (go != null) buildObjects.Add(go);
                else Debug.LogWarning($"StagesMenu: no '{name}' in the open scene, left out of the Build stage.");
            }

            var quad = Object.FindFirstObjectByType<RaymarchQuad>(FindObjectsInactive.Include);
            if (quad != null) buildObjects.Add(quad.gameObject);
            var knob = Object.FindFirstObjectByType<SmoothingKnob>(FindObjectsInactive.Include);
            if (knob != null) buildObjects.Add(knob.gameObject);
            foreach (var button in Object.FindObjectsByType<PushButton>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                buildObjects.Add(button.gameObject);

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

            // Math: everything in Build plus the dials. None yet: no Math stage, and "next" skips it.
            var mathObjects = new List<GameObject>();
            var dials = MathStageMenu.Dials();
            if (dials.Count > 0)
            {
                mathObjects.AddRange(buildObjects);
                mathObjects.AddRange(dials);
            }

            // Join: nothing left to build with - the avatar, the view and what sets them.
            var joinObjects = new List<GameObject>();
            foreach (string name in JoinObjectNames)
            {
                var go = FindByName(name);
                if (go != null) joinObjects.Add(go);
            }
            if (quad != null) joinObjects.Add(quad.gameObject);
            if (knob != null) joinObjects.Add(knob.gameObject);
            foreach (var button in Object.FindObjectsByType<ModeButton>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                joinObjects.Add(button.gameObject);

            var recordObjects = new List<GameObject>();
            foreach (string name in RecordObjectNames)
            {
                var go = FindByName(name);
                if (go != null) recordObjects.Add(go);
            }
            if (quad != null) recordObjects.Add(quad.gameObject);
            var recordGuide = FindByName(RecordGuideName);
            if (recordGuide != null) recordObjects.Add(recordGuide);

            var so = new SerializedObject(controller);
            SetObjects(so.FindProperty("tutorialObjects"), tutorialObjects);
            SetObjects(so.FindProperty("buildObjects"), buildObjects);
            SetObjects(so.FindProperty("mathObjects"), mathObjects);
            SetObjects(so.FindProperty("joinObjects"), joinObjects);
            SetObjects(so.FindProperty("recordObjects"), recordObjects);
            so.ApplyModifiedProperties();

            var partner = Object.FindFirstObjectByType<TestPartner>(FindObjectsInactive.Include);
            if (partner != null)
            {
                var soPartner = new SerializedObject(partner);
                soPartner.FindProperty("label").objectReferenceValue = recordGuide != null ? recordGuide.GetComponent<TextMesh>() : null;
                soPartner.ApplyModifiedProperties();
            }

            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            Debug.Log($"Stages: the tutorial keeps {tutorialSlots} table slot(s); {buildObjects.Count} object(s) wait for the Build stage, " +
                      $"{mathObjects.Count} for Math, {joinObjects.Count} for Join, {recordObjects.Count} for Record.");
        }

        // GameObject.Find only sees what is switched on.
        internal static GameObject FindByName(string name)
        {
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == name) return root;
            return null;
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
                color = new Color(0.85f, 0.88f, 0.95f),
            });
            EditorUtility.SetDirty(quad);
        }

        // A text floating above the table (the tutorial's, the Record stage's).
        private static GameObject BuildText(string name, string placeholder)
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

            var root = new GameObject(name);
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
            text.text = placeholder;
            return root;
        }

        private static void SetObjects(SerializedProperty list, List<GameObject> objects)
        {
            list.arraySize = objects.Count;
            for (int i = 0; i < objects.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = objects[i];
        }
    }
}
