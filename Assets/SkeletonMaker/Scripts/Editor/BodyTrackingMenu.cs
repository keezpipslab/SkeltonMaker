using Meta.XR.Movement.Retargeting;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// SkeletonMaker > Add Body Tracking Source: adds the "Body Tracking Source"
    /// object (Movement SDK's MetaSourceDataProvider + AvatarBodyTrackingSource)
    /// and wires it into the scene's AvatarDanceSource, which is what makes its
    /// Tracking mode available. Replaces any source already in the scene.
    /// </summary>
    public static class BodyTrackingMenu
    {
        [MenuItem("SkeletonMaker/Add Body Tracking Source")]
        public static void AddBodyTrackingSource()
        {
            var avatar = Object.FindFirstObjectByType<AvatarDanceSource>(FindObjectsInactive.Include);
            if (avatar == null)
            {
                EditorUtility.DisplayDialog("Add Body Tracking Source", "No AvatarDanceSource in the open scene.", "OK");
                return;
            }

            var existing = Object.FindFirstObjectByType<AvatarBodyTrackingSource>(FindObjectsInactive.Include);
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

            var root = new GameObject("Body Tracking Source");
            Undo.RegisterCreatedObjectUndo(root, "Add Body Tracking Source");

            // Off until AvatarBodyTrackingSource.Begin() - tracking only runs in Tracking mode.
            var provider = root.AddComponent<MetaSourceDataProvider>();
            provider.ProvidedSkeletonType = OVRPlugin.BodyJointSet.FullBody;
            provider.enabled = false;
            var source = root.AddComponent<AvatarBodyTrackingSource>();

            var so = new SerializedObject(avatar);
            so.FindProperty("bodySource").objectReferenceValue = source;
            so.ApplyModifiedProperties();

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);
        }
    }
}
