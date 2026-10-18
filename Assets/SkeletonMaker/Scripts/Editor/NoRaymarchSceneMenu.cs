using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// SkeletonMaker > Create No Raymarch Scene: makes Scenes/SkeletonBuilder
    /// No Raymarch.unity, a copy of SkeletonBuilder.unity with the raymarching
    /// taken out, for prototyping without it. Gone are the raymarch quad and
    /// everything that only acts on it: the dials (smoothing, inflate), the
    /// Together Math stage's combine buttons and the Hide button (which hides
    /// the meshes so only the raymarch shows). Everything else - table,
    /// skeleton, avatar, stages, network, passthrough - is as it is in the
    /// source scene; what pointed at the quad copes with it being missing.
    /// The tutorial ends after the place step, since the rest is about the
    /// window. It is the scene the standalone Quest runs (QuestBuildMenu), so
    /// its OscLink is performer 2 with Remote Host "auto". Running it again
    /// builds the scene afresh from SkeletonBuilder.
    /// </summary>
    public static class NoRaymarchSceneMenu
    {
        private const string SourceScene = "Assets/SkeletonMaker/Scenes/SkeletonBuilder.unity";
        private const string ScenePath = "Assets/SkeletonMaker/Scenes/SkeletonBuilder No Raymarch.unity";

        private static readonly string[] StageLists =
            { "tutorialObjects", "buildObjects", "mathObjects", "joinObjects", "togetherMathObjects", "recordObjects" };

        [MenuItem("SkeletonMaker/Create No Raymarch Scene")]
        private static void CreateNoRaymarchScene()
        {
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Create No Raymarch Scene", ScenePath + " is already there. Build it afresh? Whatever was changed in it by hand is lost.", "Build afresh", "Cancel"))
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
                Debug.LogError("NoRaymarchSceneMenu: could not copy " + SourceScene);
                return;
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            int removed = 0;
            removed += RemoveAll<RaymarchQuad>();
            removed += RemoveAll<Knob>();         // smoothing, inflate (and repeat): all dials on the quad
            removed += RemoveAll<CombineButton>();
            removed += RemoveAll<HideToggle>();

            // The stage lists and the tutorial's window would otherwise keep
            // missing entries where those objects were.
            var stages = Object.FindFirstObjectByType<StageController>(FindObjectsInactive.Include);
            if (stages != null)
            {
                var so = new SerializedObject(stages);
                foreach (string list in StageLists) DropMissing(so.FindProperty(list));
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            var guide = Object.FindFirstObjectByType<TutorialGuide>(FindObjectsInactive.Include);
            if (guide != null)
            {
                var so = new SerializedObject(guide);
                so.FindProperty("windowObjects").arraySize = 0;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // The second performer: on a standalone Quest, which answers the
            // PC on Link (performer 1) at whatever address it hears it from.
            var link = Object.FindFirstObjectByType<OscLink>(FindObjectsInactive.Include);
            if (link != null)
            {
                var so = new SerializedObject(link);
                so.FindProperty("performerId").intValue = 2;
                so.FindProperty("remoteHost").stringValue = OscLink.Auto;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"NoRaymarchSceneMenu: built {ScenePath} ({removed} raymarch objects taken out).");
        }

        private static int RemoveAll<T>() where T : Component
        {
            int count = 0;
            foreach (var component in Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (component == null) continue; // already gone with an earlier one
                Object.DestroyImmediate(component.gameObject);
                count++;
            }
            return count;
        }

        private static void DropMissing(SerializedProperty array)
        {
            if (array == null) return;
            for (int i = array.arraySize - 1; i >= 0; i--)
                if (array.GetArrayElementAtIndex(i).objectReferenceValue == null) array.DeleteArrayElementAtIndex(i);
        }
    }
}
