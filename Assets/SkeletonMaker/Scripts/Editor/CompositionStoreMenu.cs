using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// SkeletonMaker > Add Composition Store: adds the object that saves and
    /// loads the skeleton at runtime (F5 / F9), wired to every element prefab
    /// in the Prefabs folder and to the scene's RaymarchQuad. Run it again
    /// after adding a new kind of element prefab to rewire the existing one.
    /// </summary>
    public static class CompositionStoreMenu
    {
        private const string ObjectName = "Composition Store";
        private const string PrefabFolder = "Assets/SkeletonMaker/Prefabs";

        [MenuItem("SkeletonMaker/Add Composition Store")]
        public static void AddCompositionStore()
        {
            var store = Object.FindFirstObjectByType<CompositionStore>();
            if (store == null)
            {
                var go = new GameObject(ObjectName);
                Undo.RegisterCreatedObjectUndo(go, "Add Composition Store");
                store = go.AddComponent<CompositionStore>();
            }

            var prefabs = new List<GameObject>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab != null && prefab.GetComponent<RaymarchableElement>() != null) prefabs.Add(prefab);
            }

            var so = new SerializedObject(store);
            var list = so.FindProperty("elementPrefabs");
            list.arraySize = prefabs.Count;
            for (int i = 0; i < prefabs.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = prefabs[i];
            so.FindProperty("quad").objectReferenceValue = Object.FindFirstObjectByType<RaymarchQuad>();
            so.ApplyModifiedProperties();

            Selection.activeGameObject = store.gameObject;
            EditorSceneManager.MarkSceneDirty(store.gameObject.scene);
            Debug.Log($"Composition Store wired to {prefabs.Count} element prefab(s). Saves go to {store.FilePath}");
        }
    }
}
