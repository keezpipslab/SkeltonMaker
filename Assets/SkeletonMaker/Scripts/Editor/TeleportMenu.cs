using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// SkeletonMaker > Remove Teleport: stops the XR rig's controllers from
    /// showing the teleport arc when a thumbstick is pushed forward. XRI's
    /// ControllerInputActionManager (on each controller) switches its Teleport
    /// Interactor on from the "Teleport Mode" action no matter whether that
    /// object was switched off in the scene, so this unhooks the manager from
    /// both - the thumbsticks are for resizing here, and there's nothing to
    /// teleport to.
    /// </summary>
    public static class TeleportMenu
    {
        private static readonly string[] Unhooked = { "m_TeleportInteractor", "m_TeleportMode", "m_TeleportModeCancel" };

        [MenuItem("SkeletonMaker/Remove Teleport")]
        public static void RemoveTeleport()
        {
            int managers = 0;
            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                // By name: the manager lives in the XRI Starter Assets sample, not in a package.
                if (behaviour == null || behaviour.GetType().Name != "ControllerInputActionManager") continue;

                var so = new SerializedObject(behaviour);
                var interactor = so.FindProperty("m_TeleportInteractor");
                if (interactor != null && interactor.objectReferenceValue is Component teleport)
                {
                    Undo.RecordObject(teleport.gameObject, "Remove Teleport");
                    teleport.gameObject.SetActive(false);
                }

                foreach (string name in Unhooked)
                {
                    var property = so.FindProperty(name);
                    if (property != null) property.objectReferenceValue = null;
                }
                so.ApplyModifiedProperties();

                managers++;
                EditorSceneManager.MarkSceneDirty(behaviour.gameObject.scene);
            }

            Debug.Log(managers > 0
                ? $"Remove Teleport: unhooked the teleport ray on {managers} controller(s). Save the scene to keep it."
                : "Remove Teleport: no ControllerInputActionManager in the open scene.");
        }
    }
}
