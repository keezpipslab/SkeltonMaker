using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// Saves the skeleton being built to a JSON file on the device and builds
    /// it back from one (see SkeletonComposition for what's in it). F5 saves
    /// and F9 loads for now; anything else - a button in VR, a scene change -
    /// can call Save()/Load() directly. Loading replaces whatever is placed
    /// on the skeleton; elements on the table or in a hand are left alone.
    /// </summary>
    public class CompositionStore : MonoBehaviour
    {
        public static CompositionStore Instance { get; private set; }

        [Tooltip("One element prefab per PrimitiveKind, used to rebuild a saved skeleton.")]
        [SerializeField] private GameObject[] elementPrefabs;

        [Tooltip("Whose smoothing is saved and restored. Optional.")]
        [SerializeField] private RaymarchQuad quad;

        [Tooltip("File name (without extension) under Application.persistentDataPath.")]
        [SerializeField] private string slot = "skeleton";

        public string Slot
        {
            get => slot;
            set => slot = value;
        }

        public string FilePath => Path.Combine(Application.persistentDataPath, slot + ".json");

        public bool HasSave => File.Exists(FilePath);

        private Dictionary<PrimitiveKind, GameObject> prefabsByKind;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.f5Key.wasPressedThisFrame) Save();
            if (keyboard.f9Key.wasPressedThisFrame) Load();
        }

        /// <summary>Writes what's currently placed on the skeleton to this slot's
        /// file, replacing what was saved there before.</summary>
        public bool Save()
        {
            var rig = SkeletonRig.Instance;
            if (rig == null || rig.IsStick) // the tutorial's practice stick isn't a skeleton to keep
            {
                Debug.LogWarning("CompositionStore: no SkeletonRig to save.");
                return false;
            }

            var composition = SkeletonComposition.Capture(rig, quad);
            try
            {
                File.WriteAllText(FilePath, composition.ToJson());
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"CompositionStore: couldn't write {FilePath}: {e.Message}");
                return false;
            }

            Debug.Log($"CompositionStore: saved {composition.elements.Count} element(s) to {FilePath}");
            return true;
        }

        /// <summary>Rebuilds the skeleton from this slot's file. Does nothing
        /// (and keeps the current skeleton) if there is no readable save.</summary>
        public bool Load()
        {
            var rig = SkeletonRig.Instance;
            if (rig == null || rig.IsStick)
            {
                Debug.LogWarning("CompositionStore: no SkeletonRig to load onto.");
                return false;
            }

            if (!HasSave)
            {
                Debug.Log($"CompositionStore: nothing saved yet at {FilePath}");
                return false;
            }

            SkeletonComposition composition;
            try
            {
                composition = SkeletonComposition.FromJson(File.ReadAllText(FilePath));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"CompositionStore: couldn't read {FilePath}: {e.Message}");
                return false;
            }

            if (composition == null)
            {
                Debug.LogError($"CompositionStore: {FilePath} isn't a composition this version can read.");
                return false;
            }

            Apply(composition, rig);
            return true;
        }

        private void Apply(SkeletonComposition composition, SkeletonRig rig)
        {
            rig.RemovePlacedElements();

            int built = 0;
            foreach (var saved in composition.elements)
            {
                if (!Enum.TryParse(saved.kind, out PrimitiveKind kind) || !TryGetPrefab(kind, out GameObject prefab))
                {
                    Debug.LogWarning($"CompositionStore: no element prefab for kind '{saved.kind}', skipped.");
                    continue;
                }

                if (!rig.TryFindAnchor(saved.anchor, out Transform anchor, out string jointName))
                {
                    Debug.LogWarning($"CompositionStore: the skeleton has no anchor '{saved.anchor}', skipped.");
                    continue;
                }

                var instance = Instantiate(prefab);
                instance.GetComponent<RaymarchableElement>().Size = saved.size;
                if (saved.painted) ElementColor.Paint(instance, saved.color);
                instance.GetComponent<SkeletonPlacement>().PlaceLoaded(anchor, jointName, saved.localPosition, saved.localRotation);
                built++;
            }

            if (quad != null) quad.smoothing = composition.smoothing;

            Debug.Log($"CompositionStore: loaded {built} of {composition.elements.Count} element(s) from {FilePath}");
        }

        private bool TryGetPrefab(PrimitiveKind kind, out GameObject prefab)
        {
            if (prefabsByKind == null)
            {
                prefabsByKind = new Dictionary<PrimitiveKind, GameObject>();
                if (elementPrefabs != null)
                {
                    foreach (var candidate in elementPrefabs)
                    {
                        // Needs both: the kind to match on, and the placement to put it on the skeleton with.
                        if (candidate == null || !candidate.TryGetComponent(out RaymarchableElement element)) continue;
                        if (!candidate.TryGetComponent(out SkeletonPlacement _)) continue;
                        prefabsByKind[element.Kind] = candidate;
                    }
                }
            }

            return prefabsByKind.TryGetValue(kind, out prefab);
        }
    }
}
