using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// SkeletonMaker > Add Xsens Input: makes an Xsens suit an alternative to
    /// Meta's body tracking. Adds an XsensBodySource and a BodyInput (the
    /// switch between the two) to the "Body Tracking Source" object, and
    /// points AvatarDanceSource and BodySender at the switch. Safe to run
    /// again; needs the Body Tracking Source to be there already.
    /// </summary>
    public static class XsensMenu
    {
        [MenuItem("SkeletonMaker/Add Xsens Input")]
        public static void AddXsensInput()
        {
            var meta = Object.FindFirstObjectByType<AvatarBodyTrackingSource>(FindObjectsInactive.Include);
            if (meta == null)
            {
                EditorUtility.DisplayDialog("Add Xsens Input", "No Body Tracking Source in the open scene - run 'Add Body Tracking Source' first.", "OK");
                return;
            }

            var xsens = meta.GetComponent<XsensBodySource>();
            if (xsens == null) xsens = Undo.AddComponent<XsensBodySource>(meta.gameObject);
            var input = meta.GetComponent<BodyInput>();
            if (input == null) input = Undo.AddComponent<BodyInput>(meta.gameObject);

            var so = new SerializedObject(input);
            so.FindProperty("meta").objectReferenceValue = meta;
            so.FindProperty("xsens").objectReferenceValue = xsens;
            so.ApplyModifiedProperties();

            foreach (var avatar in Object.FindObjectsByType<AvatarDanceSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Point(avatar, input);
            foreach (var sender in Object.FindObjectsByType<BodySender>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Point(sender, input);

            Selection.activeGameObject = input.gameObject;
            EditorSceneManager.MarkSceneDirty(input.gameObject.scene);
            Debug.Log("Xsens input added. In MVN: Options > Network Streamer, this PC's address, port 9763, 'Position + Orientation (Quaternion)'. X on the keyboard (or Use on the BodyInput) switches between Meta and Xsens.");
        }

        /// <summary>The body the scene's Tracking mode should follow: the switch
        /// if there is one, else Meta's body tracking.</summary>
        internal static BodySource SceneBodySource()
        {
            var input = Object.FindFirstObjectByType<BodyInput>(FindObjectsInactive.Include);
            if (input != null) return input;
            return Object.FindFirstObjectByType<AvatarBodyTrackingSource>(FindObjectsInactive.Include);
        }

        private static void Point(Object user, BodySource source)
        {
            var so = new SerializedObject(user);
            so.FindProperty("bodySource").objectReferenceValue = source;
            so.ApplyModifiedProperties();
        }
    }
}
