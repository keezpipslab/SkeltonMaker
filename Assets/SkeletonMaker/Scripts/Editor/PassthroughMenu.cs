using Meta.XR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// SkeletonMaker > Add Passthrough: adds what showing the real room takes -
    /// an "OVR Manager" (Meta's passthrough only runs with one in the scene),
    /// the "Passthrough" object (OVRPassthroughLayer + PassthroughView) and
    /// the "Passthrough Camera" (PassthroughCameraAccess + PassthroughCameraFeed
    /// with its preview panel) - and switches passthrough and passthrough
    /// camera access on in the Meta project config, which is what puts them
    /// in the Android manifest. Replaces what a previous run added.
    /// </summary>
    public static class PassthroughMenu
    {
        private const string ManagerName = "OVR Manager";
        private const string ViewName = "Passthrough";
        private const string CameraName = "Passthrough Camera";

        [MenuItem("SkeletonMaker/Add Passthrough")]
        public static void AddPassthrough()
        {
            foreach (var old in Object.FindObjectsByType<PassthroughView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Undo.DestroyObjectImmediate(old.gameObject);
            foreach (var old in Object.FindObjectsByType<PassthroughCameraFeed>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Undo.DestroyObjectImmediate(old.gameObject);
            foreach (var old in Object.FindObjectsByType<OVRManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Undo.DestroyObjectImmediate(old.gameObject);

            var config = OVRProjectConfig.CachedProjectConfig;
            if (config != null)
            {
                if (config.insightPassthroughSupport == OVRProjectConfig.FeatureSupport.None)
                    config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;
                config.isPassthroughCameraAccessEnabled = true;
                OVRProjectConfig.CommitProjectConfig(config);
            }
            else
            {
                Debug.LogWarning("PassthroughMenu: no Meta project config yet - run this again once the editor has finished loading.");
            }

            var managerObject = new GameObject(ManagerName);
            Undo.RegisterCreatedObjectUndo(managerObject, "Add Passthrough");
            var manager = managerObject.AddComponent<OVRManager>();
            manager.isInsightPassthroughEnabled = true;
            // Stage = the floor, and not moved by a recenter: a recenter would
            // otherwise throw the stage calibration off.
            var soManager = new SerializedObject(manager);
            soManager.FindProperty("_trackingOriginType").intValue = (int)OVRManager.TrackingOrigin.Stage;
            soManager.ApplyModifiedPropertiesWithoutUndo();

            var viewCamera = Camera.main;
            var quad = Object.FindFirstObjectByType<RaymarchQuad>(FindObjectsInactive.Include);

            var viewObject = new GameObject(ViewName);
            Undo.RegisterCreatedObjectUndo(viewObject, "Add Passthrough");
            var layer = viewObject.AddComponent<OVRPassthroughLayer>();
            layer.hidden = true; // PassthroughView unhides it
            Wire(viewObject.AddComponent<PassthroughView>(), ("layer", layer), ("viewCamera", viewCamera), ("quad", quad));

            var cameraObject = new GameObject(CameraName);
            Undo.RegisterCreatedObjectUndo(cameraObject, "Add Passthrough");
            var access = cameraObject.AddComponent<PassthroughCameraAccess>();
            access.CameraPosition = PassthroughCameraAccess.CameraPositionType.Left;
            access.enabled = false; // PassthroughCameraFeed.Begin() starts it
            Wire(cameraObject.AddComponent<PassthroughCameraFeed>(),
                ("trackingSpace", Object.FindFirstObjectByType<AvatarBodyTrackingSource>(FindObjectsInactive.Include)),
                ("preview", BuildPreview(cameraObject.transform)));

            Selection.activeGameObject = viewObject;
            EditorSceneManager.MarkSceneDirty(viewObject.scene);
            Debug.Log("Passthrough added. Over Link it also needs 'Passthrough over Meta Horizon Link' and 'Passthrough Camera API permissions' switched on in the Link app (Settings > Developer), and it only shows in the headset, never in the Game view.");
        }

        // A 4:3 panel floating above the table, facing the player.
        private static Renderer BuildPreview(Transform parent)
        {
            Vector3 position = new Vector3(-0.6f, 1.5f, 1.2f);
            var table = GameObject.Find("Table");
            var tableRenderer = table != null ? table.GetComponent<Renderer>() : null;
            if (tableRenderer != null)
            {
                Bounds b = tableRenderer.bounds;
                position = new Vector3(b.min.x, b.max.y + 0.6f, b.center.z);
            }

            var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
            panel.name = "Preview";
            Object.DestroyImmediate(panel.GetComponent<Collider>());
            panel.transform.SetParent(parent, false);
            panel.transform.position = position;
            panel.transform.localScale = new Vector3(0.4f, 0.3f, 1f);

            var renderer = panel.GetComponent<Renderer>();
            renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Passthrough Preview" };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            panel.SetActive(false);
            return renderer;
        }

        private static void Wire(Object component, params (string property, Object value)[] references)
        {
            var so = new SerializedObject(component);
            foreach (var (property, value) in references) so.FindProperty(property).objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }
    }
}
