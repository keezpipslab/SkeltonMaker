using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SkeletonMaker
{
    /// <summary>
    /// SkeletonMaker > Add Smoothing Knob: builds the grab-and-twist dial that
    /// drives the scene's RaymarchQuad smoothing (small disk + indicator
    /// sphere + value text), wired to both hands' grips. Replaces any knob
    /// already in the scene. Move/rotate the "Smoothing Knob" object afterwards
    /// to put it where you want - its up axis should point at the viewer.
    /// BuildKnob is the dial itself, shared with the math stage's dials.
    /// </summary>
    public static class SmoothingKnobMenu
    {
        private const string InputActionsPath = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/XRI Default Input Actions.inputactions";

        [MenuItem("SkeletonMaker/Add Smoothing Knob")]
        public static void AddSmoothingKnob()
        {
            var quad = Object.FindFirstObjectByType<RaymarchQuad>(FindObjectsInactive.Include);
            if (quad == null)
            {
                EditorUtility.DisplayDialog("Add Smoothing Knob", "No RaymarchQuad in the open scene - run 'Add Raymarch Quad' first.", "OK");
                return;
            }

            var existing = Object.FindFirstObjectByType<SmoothingKnob>(FindObjectsInactive.Include);
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

            // Hovering left of the table at chest height, facing the player.
            Vector3 position = new Vector3(-0.75f, 1.15f, 0.8f);
            var origin = GameObject.Find("XR Origin (XR Rig)"); // Camera.main is wherever the editor camera is, not where the player stands
            Vector3 toViewer = (origin != null ? origin.transform.position : Vector3.zero) - position;
            toViewer.y = 0f;
            toViewer = toViewer.sqrMagnitude > 1e-4f ? toViewer.normalized : Vector3.back;

            var knob = BuildKnob<SmoothingKnob>("Smoothing Knob", position, Quaternion.LookRotation(Vector3.up, toViewer), quad, 0f, 0.3f);

            Selection.activeGameObject = knob.gameObject;
            EditorSceneManager.MarkSceneDirty(knob.gameObject.scene);
        }

        /// <summary>Builds one dial of the given kind - disk, indicator, value text -
        /// wired to the quad and to both hands' grips, turning from min to max.</summary>
        internal static T BuildKnob<T>(string name, Vector3 position, Quaternion rotation, RaymarchQuad quad, float min, float max) where T : Knob
        {
            var root = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(root, "Add " + name);
            root.transform.SetPositionAndRotation(position, rotation);

            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(0.13f, 0.06f, 0.13f);
            box.center = new Vector3(0f, 0.02f, 0f);

            var spin = new GameObject("Spin").transform;
            spin.SetParent(root.transform, false);

            var disk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disk.name = "Disk";
            Object.DestroyImmediate(disk.GetComponent<Collider>());
            disk.transform.SetParent(spin, false);
            disk.transform.localScale = new Vector3(0.12f, 0.012f, 0.12f);
            disk.GetComponent<MeshRenderer>().sharedMaterial = GetOrCreateMaterial("KnobDisk", new Color(0.18f, 0.2f, 0.26f), false);

            var indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            indicator.name = "Indicator";
            Object.DestroyImmediate(indicator.GetComponent<Collider>());
            indicator.transform.SetParent(spin, false);
            indicator.transform.localPosition = new Vector3(0f, 0.012f, 0.042f);
            indicator.transform.localScale = Vector3.one * 0.02f;
            indicator.GetComponent<MeshRenderer>().sharedMaterial = GetOrCreateMaterial("KnobIndicator", new Color(1f, 0.75f, 0.2f), true);

            var labelGo = new GameObject("Value Label");
            labelGo.transform.SetParent(root.transform, false);
            var text = labelGo.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.font = font;
            labelGo.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            text.fontSize = 64;
            text.characterSize = 0.004f;
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            text.text = name;

            var knob = root.AddComponent<T>();
            var so = new SerializedObject(knob);
            so.FindProperty("quad").objectReferenceValue = quad;
            so.FindProperty("spin").objectReferenceValue = spin;
            so.FindProperty("label").objectReferenceValue = text;
            so.FindProperty("minValue").floatValue = min;
            so.FindProperty("maxValue").floatValue = max;

            SetHands(so.FindProperty("hands"), "grip", "Select");
            so.ApplyModifiedPropertiesWithoutUndo();

            return knob;
        }

        /// <summary>Fills a list of hands with both of them: each one's grab point
        /// as "controller", and its XRI interaction action (Select = the grip,
        /// Activate = the trigger) under the given field name.</summary>
        internal static void SetHands(SerializedProperty handsProp, string actionField, string action)
        {
            Transform left = null, right = null;
            foreach (var grabber in Object.FindObjectsByType<HandGrabber>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (grabber.name.Contains("Left")) left = grabber.transform;
                else if (grabber.name.Contains("Right")) right = grabber.transform;
            }

            handsProp.arraySize = 2;
            SetHand(handsProp.GetArrayElementAtIndex(0), actionField, "XRI Left Interaction/" + action, left);
            SetHand(handsProp.GetArrayElementAtIndex(1), actionField, "XRI Right Interaction/" + action, right);
        }

        private static void SetHand(SerializedProperty hand, string actionField, string action, Transform controller)
        {
            hand.FindPropertyRelative(actionField).objectReferenceValue = FindAction(action);
            hand.FindPropertyRelative("controller").objectReferenceValue = controller;
        }

        private static InputActionReference FindAction(string mapSlashActionName)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(InputActionsPath))
            {
                if (asset is InputActionReference iar && iar.name == mapSlashActionName) return iar;
            }
            Debug.LogWarning($"SmoothingKnobMenu: could not find the '{mapSlashActionName}' action.");
            return null;
        }

        internal static Material GetOrCreateMaterial(string name, Color color, bool unlit)
        {
            string path = "Assets/SkeletonMaker/Materials/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            mat = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}
