using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// SkeletonMaker > Add Buttons: builds the flat-cube buttons that go with
    /// the dials - "Mirror Button" (mirror placement on/off) under the
    /// smoothing knob, "Finished Button" (on to the Join stage) beside it,
    /// under where the math stage's dial is, and in a row above the dials the
    /// three that choose what the avatar follows: "Still Button", "Animation
    /// Button" and "You Button". Each is the size of a dial and wired to both
    /// hands' triggers, and all are put in the stages. A button that's
    /// already there is left where it is, and only wired again. Needs the
    /// Smoothing Knob.
    /// </summary>
    public static class ButtonsMenu
    {
        private const float Below = 0.2f;   // under the dial, clear of its text
        private const float Beside = 0.2f;  // the dials' own spacing
        private const float Above = 0.36f;  // over the dial, clear of its text and of the button's own

        private static readonly (string name, AvatarMode mode)[] Modes =
        {
            ("Still Button", AvatarMode.Still), ("Animation Button", AvatarMode.Animation), ("You Button", AvatarMode.Tracking),
        };

        [MenuItem("SkeletonMaker/Add Buttons")]
        public static void AddButtons()
        {
            var smoothing = Object.FindFirstObjectByType<SmoothingKnob>(FindObjectsInactive.Include);
            if (smoothing == null)
            {
                EditorUtility.DisplayDialog("Add Buttons", "No Smoothing Knob in the open scene - run 'Add Smoothing Knob' first.", "OK");
                return;
            }

            // The dials stand upright facing the viewer: their forward axis is "up the panel".
            Transform dial = smoothing.transform;
            Vector3 under = dial.position - dial.forward * Below;
            Vector3 side = dial.right;
            var table = GameObject.Find("Table");
            if (table != null && Vector3.Dot(side, dial.position - table.transform.position) < 0f) side = -side;

            var mirror = Object.FindFirstObjectByType<MirrorToggle>(FindObjectsInactive.Include);
            if (mirror == null) mirror = BuildButton<MirrorToggle>("Mirror Button", under, dial.rotation);

            var finished = Object.FindFirstObjectByType<FinishedButton>(FindObjectsInactive.Include);
            if (finished == null) finished = BuildButton<FinishedButton>("Finished Button", under + side * Beside, dial.rotation);

            var modeButtons = Object.FindObjectsByType<ModeButton>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < Modes.Length; i++)
            {
                if (System.Array.Exists(modeButtons, b => ModeOf(b) == Modes[i].mode)) continue;
                var button = BuildButton<ModeButton>(Modes[i].name, dial.position + dial.forward * Above + side * (Beside * i), dial.rotation);
                var soMode = new SerializedObject(button);
                soMode.FindProperty("mode").enumValueIndex = (int)Modes[i].mode;
                soMode.ApplyModifiedPropertiesWithoutUndo();
            }

            // Also for the ones that were there already: which hand control presses them may have changed.
            foreach (var button in Object.FindObjectsByType<PushButton>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var soButton = new SerializedObject(button);
                SmoothingKnobMenu.SetHands(soButton.FindProperty("hands"), "trigger", "Activate");
                soButton.ApplyModifiedProperties();
            }

            var controller = Object.FindFirstObjectByType<StageController>(FindObjectsInactive.Include);
            if (controller != null) StagesMenu.WireStages(controller);
            else Debug.LogWarning("ButtonsMenu: no Stages object in the open scene, so the buttons are simply always there - run 'Add Stages'.");

            Selection.activeGameObject = mirror.gameObject;
            EditorSceneManager.MarkSceneDirty(mirror.gameObject.scene);
        }

        private static AvatarMode ModeOf(ModeButton button) =>
            (AvatarMode)new SerializedObject(button).FindProperty("mode").enumValueIndex;

        private static T BuildButton<T>(string name, Vector3 position, Quaternion rotation) where T : PushButton
        {
            var root = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(root, "Add " + name);
            root.transform.SetPositionAndRotation(position, rotation);

            // Same footprint as a dial's.
            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(0.13f, 0.06f, 0.13f);
            box.center = new Vector3(0f, 0.02f, 0f);

            var cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cap.name = "Cap";
            Object.DestroyImmediate(cap.GetComponent<Collider>());
            cap.transform.SetParent(root.transform, false);
            cap.transform.localScale = new Vector3(0.12f, 0.012f, 0.12f);
            cap.GetComponent<MeshRenderer>().sharedMaterial = SmoothingKnobMenu.GetOrCreateMaterial("KnobDisk", new Color(0.18f, 0.2f, 0.26f), false);

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(root.transform, false);
            var text = labelGo.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.font = font;
            labelGo.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            text.fontSize = 64;
            text.characterSize = 0.004f;
            text.anchor = TextAnchor.UpperCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            text.text = name;

            var button = root.AddComponent<T>();
            var so = new SerializedObject(button);
            so.FindProperty("cap").objectReferenceValue = cap.transform;
            so.FindProperty("label").objectReferenceValue = text;
            so.ApplyModifiedPropertiesWithoutUndo();

            return button;
        }
    }
}
