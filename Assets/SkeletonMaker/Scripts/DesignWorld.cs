using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// The world being designed: every primitive that has been let go of is a
    /// child of this object (WorldElement puts it there), and the raymarch
    /// quad shows them as one surface.
    ///
    /// Export() bakes that surface into a mesh and writes it to an .obj file
    /// in the Exports folder next to Assets, named after the time. The baking
    /// (WorldMesher) runs off the main thread, so the headset keeps drawing
    /// while it works.
    ///
    /// The object can be moved, turned and made bigger or smaller as a whole
    /// (WorldGrab). That is only how the world is looked at: smoothing and
    /// inflate are in the world's own meters, and it is saved and exported
    /// at the size it was built, wherever it stands.
    ///
    /// Save() writes the world to a JSON file on this PC (see WorldDesign for
    /// what's in it) and Load() builds it back from that file, replacing what
    /// is placed just now; the primitives on the panel and in a hand are left
    /// alone. F5 and F9 do the same as the panel's Save and Load buttons.
    /// Each save also leaves a copy named after the time.
    /// </summary>
    public class DesignWorld : MonoBehaviour
    {
        public static DesignWorld Instance { get; private set; }

        [Tooltip("Whose smoothing and inflate the export is baked with.")]
        [SerializeField] private RaymarchQuad quad;

        [Header("Export")]
        [Min(0.002f)]
        [Tooltip("Size of a cell of the grid the surface is taken from (meters). Smaller = more detail, a bigger file and a longer wait.")]
        [SerializeField] private float voxelSize = 0.01f;
        [Min(1000)]
        [Tooltip("The most cells the grid may have. A world too big for that at Voxel Size is baked with bigger cells.")]
        [SerializeField] private int maxCells = 8000000;
        [Tooltip("Where the .obj files go: a folder next to Assets, or a full path.")]
        [SerializeField] private string folder = "Exports";

        [Header("Save")]
        [Tooltip("One designer primitive prefab per PrimitiveKind, used to rebuild a saved world.")]
        [SerializeField] private GameObject[] elementPrefabs;
        [Tooltip("File name (without extension) under Application.persistentDataPath.")]
        [SerializeField] private string slot = "world";

        public string FilePath => Path.Combine(Application.persistentDataPath, slot + ".json");

        public bool HasSave => File.Exists(FilePath);

        /// <summary>How the last save and the last load went, in a few words. Empty before the first.</summary>
        public string SaveStatus { get; private set; } = "";
        public string LoadStatus { get; private set; } = "";

        private Dictionary<PrimitiveKind, GameObject> prefabsByKind;

        /// <summary>True while an export is being baked.</summary>
        public bool Exporting { get; private set; }

        /// <summary>How the last export went, in a few words. Empty before the first.</summary>
        public string Status { get; private set; } = "";

        private void Awake()
        {
            // A scene built before there was one.
            if (!TryGetComponent(out WorldGrab _)) gameObject.AddComponent<WorldGrab>();
        }

        private void OnEnable() => Instance = this;

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
            if (quad != null) quad.unitScale = 1f;
        }

        // The quad's smoothing and inflate are in this world's meters.
        private void LateUpdate()
        {
            if (quad != null) quad.unitScale = transform.lossyScale.x;
        }

        private void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.f5Key.wasPressedThisFrame) Save();
            if (keyboard.f9Key.wasPressedThisFrame) Load();
        }

        /// <summary>Writes what's placed in the world to this slot's file,
        /// replacing what was saved there before, and once more to
        /// "<slot>-<date>-<time>.json" beside it, which is never overwritten:
        /// every save stays around.</summary>
        public bool Save()
        {
            bool written = Write(FilePath, out string status);
            if (written) Write(Path.Combine(Application.persistentDataPath, slot + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), out _);
            SaveStatus = status;
            return written;
        }

        /// <summary>Rebuilds the world from this slot's file. Does nothing (and
        /// keeps the current world) if there is no readable save. The world
        /// it replaces is kept in "<slot>-before-load.json", in case the
        /// button was pressed by mistake.</summary>
        public bool Load()
        {
            if (!HasSave)
            {
                LoadStatus = "nothing saved";
                Debug.Log($"DesignWorld: nothing saved yet at {FilePath}");
                return false;
            }

            WorldDesign design;
            try
            {
                design = WorldDesign.FromJson(File.ReadAllText(FilePath));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                LoadStatus = "failed";
                Debug.LogError($"DesignWorld: couldn't read {FilePath}: {e.Message}");
                return false;
            }

            if (design == null)
            {
                LoadStatus = "failed";
                Debug.LogError($"DesignWorld: {FilePath} isn't a world this version can read.");
                return false;
            }

            Write(Path.Combine(Application.persistentDataPath, slot + "-before-load.json"), out _);
            Apply(design);
            return true;
        }

        private bool Write(string path, out string status)
        {
            var design = WorldDesign.Capture(transform, quad);
            try
            {
                File.WriteAllText(path, design.ToJson());
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                status = "failed";
                Debug.LogError($"DesignWorld: couldn't write {path}: {e.Message}");
                return false;
            }

            status = $"{design.elements.Count} saved";
            Debug.Log($"DesignWorld: saved {design.elements.Count} primitive(s) to {path}");
            return true;
        }

        private void Apply(WorldDesign design)
        {
            // Switched off first, so they are out of WorldElement.All at once
            // rather than at the end of the frame.
            foreach (var placed in WorldElement.All.ToArray())
            {
                if (placed == null || !placed.Placed) continue;
                placed.gameObject.SetActive(false);
                Destroy(placed.gameObject);
            }

            int built = 0;
            foreach (var saved in design.elements)
            {
                if (!Enum.TryParse(saved.kind, out PrimitiveKind kind) || !TryGetPrefab(kind, out GameObject prefab))
                {
                    Debug.LogWarning($"DesignWorld: no primitive prefab for kind '{saved.kind}', skipped.");
                    continue;
                }

                var instance = Instantiate(prefab, transform);
                instance.transform.SetLocalPositionAndRotation(saved.position, saved.rotation);
                if (saved.scale > 0f) instance.transform.localScale = Vector3.one * saved.scale; // 0: a file from before it was saved
                instance.GetComponent<RaymarchableElement>().Size = saved.size;
                var placed = instance.GetComponent<WorldElement>();
                placed.Subtract = saved.subtract;
                placed.Smoothing = saved.smoothing;
                built++;
            }

            if (quad != null)
            {
                quad.smoothing = design.smoothing;
                quad.inflate = design.inflate;
            }

            LoadStatus = $"{built} loaded";
            Debug.Log($"DesignWorld: loaded {built} of {design.elements.Count} primitive(s) from {FilePath}");
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
                        // Needs both: the kind to match on, and the WorldElement that makes it part of the world.
                        if (candidate == null || !candidate.TryGetComponent(out RaymarchableElement element)) continue;
                        if (!candidate.TryGetComponent(out WorldElement _)) continue;
                        prefabsByKind[element.Kind] = candidate;
                    }
                }
            }

            return prefabsByKind.TryGetValue(kind, out prefab);
        }

        public async void Export()
        {
            if (Exporting) return;

            WorldShape[] shapes = GatherShapes(out int added);
            if (added == 0)
            {
                Status = "nothing placed";
                return;
            }

            string directory = Path.IsPathRooted(folder) ? folder : Path.Combine(Directory.GetParent(Application.dataPath).FullName, folder);
            string path = Path.Combine(directory, "world-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".obj");
            float smoothing = quad != null ? quad.smoothing : 0f;
            float inflate = quad != null ? quad.inflate : 0f;
            float voxel = voxelSize;
            int cells = maxCells;

            Exporting = true;
            Status = "working...";
            try
            {
                WorldMesher.Result mesh = await Task.Run(() =>
                {
                    var result = WorldMesher.Build(shapes, smoothing, inflate, voxel, cells);
                    if (result.triangles.Count > 0)
                    {
                        Directory.CreateDirectory(directory);
                        WorldMesher.WriteObj(result, path);
                    }
                    return result;
                });

                if (mesh.triangles.Count == 0)
                {
                    Status = "no surface";
                    Debug.LogWarning("DesignWorld: the export found no surface - everything may be carved away or inflated to nothing.", this);
                }
                else
                {
                    Status = $"{mesh.triangles.Count / 3:N0} tris";
                    Debug.Log($"DesignWorld: exported {mesh.vertices.Length:N0} vertices / {mesh.triangles.Count / 3:N0} triangles ({mesh.voxel * 100f:F1} cm cells) to {path}");
                }
            }
            catch (Exception e)
            {
                Status = "failed";
                Debug.LogException(e);
            }
            finally
            {
                Exporting = false;
            }
        }

        /// <summary>The placed primitives as the raymarch quad sends them to its
        /// shader: each in its Visual's own mesh space, and in the same order -
        /// but seen from this object rather than from the scene, so the mesh
        /// comes out the size the world was built, wherever WorldGrab has put it.</summary>
        private WorldShape[] GatherShapes(out int added)
        {
            added = 0;
            var shapes = new List<WorldShape>();
            Matrix4x4 toScene = transform.localToWorldMatrix;
            float shown = transform.lossyScale.x;
            foreach (Transform child in transform)
            {
                if (!child.gameObject.activeInHierarchy) continue;
                if (!child.TryGetComponent(out WorldElement placed) || !child.TryGetComponent(out RaymarchableElement element)) continue;

                Transform visual = placed.transform.Find("Visual");
                if (visual == null) continue;

                Vector3 scale = visual.lossyScale;
                float sx = Mathf.Abs(scale.x), sy = Mathf.Abs(scale.y), sz = Mathf.Abs(scale.z);
                float minScale = Mathf.Min(sx, Mathf.Min(sy, sz));
                if (minScale < 1e-6f) continue;

                shapes.Add(new WorldShape
                {
                    worldToLocal = visual.worldToLocalMatrix * toScene,
                    kind = element.Kind,
                    scale = minScale / shown,
                    center = transform.InverseTransformPoint(visual.position),
                    radius = RaymarchQuad.BoundRadius(element.Kind) * Mathf.Max(sx, Mathf.Max(sy, sz)) / shown,
                    subtract = placed.Subtract,
                    smoothing = placed.Smoothing,
                });
                if (!placed.Subtract) added++;
            }
            return shapes.ToArray();
        }
    }
}
