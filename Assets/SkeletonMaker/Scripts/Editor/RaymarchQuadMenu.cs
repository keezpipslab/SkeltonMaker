using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace SkeletonMaker
{
    /// <summary>
    /// SkeletonMaker > Add Raymarch Quad: creates a RaymarchQuad in the open
    /// scene, standing between the viewer and the avatar stand-in, showing
    /// the shapes on the stand-in and on the main skeleton (and, as always,
    /// those on the table and in a hand). Also creates the quad material on
    /// first use.
    /// </summary>
    public static class RaymarchQuadMenu
    {
        private const string MaterialPath = "Assets/SkeletonMaker/Materials/RaymarchQuadMat.mat";
        private const string StandInName = "Avatar Stand-In (Preview)";
        private const string SkeletonName = "Skeleton";

        [MenuItem("SkeletonMaker/Add Raymarch Quad")]
        private static void AddRaymarchQuad()
        {
            var standIn = GameObject.Find(StandInName);
            var skeleton = GameObject.Find(SkeletonName);
            if (standIn == null)
            {
                EditorUtility.DisplayDialog("Add Raymarch Quad", $"No '{StandInName}' GameObject found in the open scene.", "OK");
                return;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Raymarch Quad";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            Undo.RegisterCreatedObjectUndo(go, "Add Raymarch Quad");

            // Thin box over the quad's local 1x1 face, just for the left-hand grab check below.
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(1f, 1f, 0.05f);
            box.isTrigger = true;

            // Stand 1m in front of the stand-in, facing whoever is looking at
            // it (the XR camera, or the world origin as a fallback), tall
            // enough to frame a whole dancing figure.
            Vector3 viewer = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            Vector3 toStandIn = standIn.transform.position - viewer;
            toStandIn.y = 0f;
            Vector3 dir = toStandIn.sqrMagnitude > 1e-4f ? toStandIn.normalized : Vector3.forward;
            go.transform.position = standIn.transform.position - dir * 1.0f + Vector3.up * 1.0f;
            go.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            go.transform.localScale = new Vector3(1.6f, 2.0f, 1f);

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = GetOrCreateMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var quad = go.AddComponent<RaymarchQuad>();
            quad.sources.Add(new RaymarchQuad.ShapeSource
            {
                label = "Stand-in",
                root = standIn.transform,
                visible = true,
                color = new Color(0.85f, 0.88f, 0.95f),
            });
            if (skeleton != null)
            {
                quad.sources.Add(new RaymarchQuad.ShapeSource
                {
                    label = "Main skeleton",
                    root = skeleton.transform,
                    visible = true,
                    color = new Color(0.85f, 0.88f, 0.95f),
                    includeInContextToggle = true,
                });
            }

            var soQuad = new SerializedObject(quad);
            soQuad.FindProperty("contextToggleAction").objectReferenceValue = FindAction("XRI Left Interaction/Toggle Context");
            soQuad.ApplyModifiedPropertiesWithoutUndo();

            var grab = go.AddComponent<RaymarchQuadGrab>();
            var leftController = GameObject.Find("Left Controller");
            if (leftController != null)
            {
                var so = new SerializedObject(grab);
                so.FindProperty("leftController").objectReferenceValue = leftController.transform;
                so.FindProperty("head").objectReferenceValue = Camera.main != null ? Camera.main.transform : null;
                so.FindProperty("leftGripAction").objectReferenceValue = FindAction("XRI Left Interaction/Select");
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
        }

        private static InputActionReference FindAction(string mapSlashActionName)
        {
            const string path = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/XRI Default Input Actions.inputactions";
            foreach (var asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
            {
                if (asset is InputActionReference iar && iar.name == mapSlashActionName) return iar;
            }
            Debug.LogWarning($"RaymarchQuadMenu: could not find the '{mapSlashActionName}' action.");
            return null;
        }

        private static Material GetOrCreateMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat != null) return mat;

            var shader = Shader.Find("SkeletonMaker/RaymarchQuad");
            if (shader == null)
            {
                Debug.LogError("RaymarchQuadMenu: shader 'SkeletonMaker/RaymarchQuad' not found.");
                return null;
            }

            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, MaterialPath);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}
