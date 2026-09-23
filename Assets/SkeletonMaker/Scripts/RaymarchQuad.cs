using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkeletonMaker
{
    /// <summary>
    /// Shows the primitives placed under one or more skeleton roots as a
    /// single smooth raymarched (SDF) surface, drawn only inside this quad -
    /// the cheap way to raymarch in VR, since only the pixels the quad covers
    /// are raymarched. Needs a MeshRenderer using the SkeletonMaker/RaymarchQuad
    /// shader; no URP renderer feature involved.
    ///
    /// The quad is a window, not a screen: rays go from each eye through the
    /// quad into the world, so the shapes appear exactly where their meshes
    /// are (with stereo depth), and the quad should stand between the viewer
    /// and the skeleton it shows.
    ///
    /// Each frame (after every LateUpdate, so after AvatarDanceSource has
    /// posed the stand-in) every active "Visual" child under each visible
    /// source root is gathered. Kind comes from its parent's RaymarchShape
    /// (stand-in duplicates) or RaymarchableElement (the main skeleton), with
    /// the mesh name as a fallback; size/orientation come straight from the
    /// Visual's transform, which the shader evaluates in that mesh's own
    /// space - so the raymarched shape always matches its mesh.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshRenderer))]
    public class RaymarchQuad : MonoBehaviour
    {
        /// <summary>Must match MAX_SHAPES in RaymarchQuad.shader.</summary>
        public const int MaxShapes = 64;

        [Serializable]
        public class ShapeSource
        {
            public string label;
            [Tooltip("Every placed primitive under this Transform is drawn (e.g. 'Avatar Stand-In (Preview)' or the main 'Skeleton').")]
            public Transform root;
            public bool visible = true;
            public Color color = new Color(0.85f, 0.85f, 0.9f);
        }

        [Tooltip("Where shapes are gathered from. Toggle 'visible' at runtime (Inspector, or ToggleSource()/SetSourceVisible() from code) to show/hide a whole skeleton's shapes. Shared cap of MaxShapes across all sources, first come first served.")]
        public List<ShapeSource> sources = new List<ShapeSource>();

        [Header("Quality")]
        [Range(8, 128)]
        [Tooltip("Max sphere-tracing steps per ray. Lower = cheaper, but grazing edges and thin gaps may break up.")]
        public int maxSteps = 64;

        [Header("Look")]
        [Range(0f, 0.3f)]
        [Tooltip("Blend radius between shapes (meters). 0 = hard union.")]
        public float smoothing = 0.03f;

        [Tooltip("Light direction/color source. Falls back to RenderSettings.sun, then to a fixed overhead direction.")]
        public Light sun;
        public Color lightColor = Color.white;
        [Range(0f, 1f)] public float ambient = 0.25f;
        [Range(0f, 2f)] public float specular = 0.4f;
        [Range(1f, 128f)] public float specularPower = 24f;

        [Range(0f, 1f)]
        [Tooltip("0 skips the shadow ray entirely (the most expensive part of shading).")]
        public float shadowStrength = 0.5f;

        [Range(0f, 1f)]
        [Tooltip("0 skips ambient occlusion entirely.")]
        public float occlusionStrength = 0.5f;

        [Tooltip("Color where a ray misses every shape (unless the material's Clip Background is on).")]
        public Color backgroundColor = new Color(0.08f, 0.08f, 0.1f);

        /// <summary>How many shapes were sent to the shader last frame.</summary>
        public int ShapeCount { get; private set; }

        private static readonly int ShapeCountId = Shader.PropertyToID("_RMQ_ShapeCount");
        private static readonly int ShapeWorldToLocalId = Shader.PropertyToID("_RMQ_ShapeWorldToLocal");
        private static readonly int ShapeParamsId = Shader.PropertyToID("_RMQ_ShapeParams");
        private static readonly int ShapeBoundsId = Shader.PropertyToID("_RMQ_ShapeBounds");
        private static readonly int ShapeColorsId = Shader.PropertyToID("_RMQ_ShapeColors");
        private static readonly int SceneBoundsId = Shader.PropertyToID("_RMQ_SceneBounds");
        private static readonly int MaxStepsId = Shader.PropertyToID("_RMQ_MaxSteps");
        private static readonly int SmoothingId = Shader.PropertyToID("_RMQ_Smoothing");
        private static readonly int LightDirId = Shader.PropertyToID("_RMQ_LightDir");
        private static readonly int LightColorId = Shader.PropertyToID("_RMQ_LightColor");
        private static readonly int AmbientId = Shader.PropertyToID("_RMQ_Ambient");
        private static readonly int SpecularId = Shader.PropertyToID("_RMQ_Specular");
        private static readonly int SpecularPowId = Shader.PropertyToID("_RMQ_SpecularPow");
        private static readonly int ShadowStrengthId = Shader.PropertyToID("_RMQ_ShadowStrength");
        private static readonly int OcclusionStrengthId = Shader.PropertyToID("_RMQ_OcclusionStrength");
        private static readonly int BackgroundColorId = Shader.PropertyToID("_RMQ_BackgroundColor");

        // Bounding-sphere radius of each kind's native mesh (see the matching
        // SDFs in RaymarchQuad.shader), indexed by PrimitiveKind.
        private static readonly float[] NativeBoundRadius =
        {
            0.5f,    // Sphere
            0.8661f, // Box
            1.0f,    // Capsule (radius 0.5, height 2)
            0.8661f, // Pyramid
            0.5f,    // Torus
            0.8661f, // RoundBox
            0.7072f, // Cone
            0.5f,    // Octahedron
            0.7072f, // HexagonalPrism
            1.1181f, // Cylinder (radius 0.5, height 2)
            0.7072f, // TriangularPrism
            0.45f,   // Link
        };

        private readonly Matrix4x4[] _worldToLocal = new Matrix4x4[MaxShapes];
        private readonly Vector4[] _params = new Vector4[MaxShapes];
        private readonly Vector4[] _bounds = new Vector4[MaxShapes];
        private readonly Vector4[] _colors = new Vector4[MaxShapes];
        private readonly List<MeshFilter> _filters = new List<MeshFilter>();

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _block;

        public void SetSourceVisible(int index, bool visible)
        {
            if (index >= 0 && index < sources.Count) sources[index].visible = visible;
        }

        public void ToggleSource(int index)
        {
            if (index >= 0 && index < sources.Count) sources[index].visible = !sources[index].visible;
        }

        private void OnEnable()
        {
            _renderer = GetComponent<MeshRenderer>();
            _block ??= new MaterialPropertyBlock();
            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
        }

        private void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (_renderer == null) return;

            int count = GatherShapes(out Vector4 sceneBounds);
            ShapeCount = count;

            // Unused slots: identity + a far-away zero-radius bound, never reached
            // since the shader loops to _RMQ_ShapeCount only, but keeps arrays sane.
            for (int i = count; i < MaxShapes; i++)
            {
                _worldToLocal[i] = Matrix4x4.identity;
                _params[i] = Vector4.zero;
                _bounds[i] = new Vector4(0f, -1000f, 0f, 0f);
                _colors[i] = Vector4.zero;
            }

            _renderer.GetPropertyBlock(_block);
            _block.SetInteger(ShapeCountId, count);
            _block.SetMatrixArray(ShapeWorldToLocalId, _worldToLocal);
            _block.SetVectorArray(ShapeParamsId, _params);
            _block.SetVectorArray(ShapeBoundsId, _bounds);
            _block.SetVectorArray(ShapeColorsId, _colors);
            _block.SetVector(SceneBoundsId, sceneBounds);

            _block.SetInteger(MaxStepsId, maxSteps);
            _block.SetFloat(SmoothingId, smoothing);

            Light light = sun != null ? sun : RenderSettings.sun;
            Vector3 toLight = light != null ? -light.transform.forward : new Vector3(0.3f, 1f, -0.4f).normalized;
            Color lc = light != null ? lightColor * light.color : lightColor;
            _block.SetVector(LightDirId, toLight);
            _block.SetColor(LightColorId, lc);

            _block.SetFloat(AmbientId, ambient);
            _block.SetFloat(SpecularId, specular);
            _block.SetFloat(SpecularPowId, specularPower);
            _block.SetFloat(ShadowStrengthId, shadowStrength);
            _block.SetFloat(OcclusionStrengthId, occlusionStrength);
            _block.SetColor(BackgroundColorId, backgroundColor);
            _renderer.SetPropertyBlock(_block);
        }

        private int GatherShapes(out Vector4 sceneBounds)
        {
            int count = 0;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;

            foreach (var source in sources)
            {
                if (source == null || !source.visible || source.root == null) continue;

                source.root.GetComponentsInChildren(false, _filters);
                foreach (var filter in _filters)
                {
                    if (count >= MaxShapes) break;
                    if (filter.name != "Visual" || !TryGetKind(filter, out PrimitiveKind kind)) continue;

                    Transform visual = filter.transform;
                    Vector3 scale = visual.lossyScale;
                    float sx = Mathf.Abs(scale.x), sy = Mathf.Abs(scale.y), sz = Mathf.Abs(scale.z);
                    float minScale = Mathf.Min(sx, Mathf.Min(sy, sz));
                    if (minScale < 1e-6f) continue;

                    Vector3 center = visual.position;
                    float radius = NativeBoundRadius[(int)kind] * Mathf.Max(sx, Mathf.Max(sy, sz));

                    _worldToLocal[count] = visual.worldToLocalMatrix;
                    _params[count] = new Vector4((int)kind, minScale, 0f, 0f);
                    _bounds[count] = new Vector4(center.x, center.y, center.z, radius);
                    _colors[count] = source.color.linear;

                    min = Vector3.Min(min, center - Vector3.one * radius);
                    max = Vector3.Max(max, center + Vector3.one * radius);
                    count++;
                }
            }

            if (count == 0)
            {
                sceneBounds = Vector4.zero;
                return 0;
            }

            Vector3 sceneCenter = (min + max) * 0.5f;
            float sceneRadius = 0f;
            for (int i = 0; i < count; i++)
            {
                Vector3 c = _bounds[i];
                sceneRadius = Mathf.Max(sceneRadius, Vector3.Distance(c, sceneCenter) + _bounds[i].w);
            }
            sceneBounds = new Vector4(sceneCenter.x, sceneCenter.y, sceneCenter.z, sceneRadius + smoothing);
            return count;
        }

        private static bool TryGetKind(MeshFilter visual, out PrimitiveKind kind)
        {
            Transform element = visual.transform.parent;
            if (element != null)
            {
                if (element.TryGetComponent(out RaymarchShape shape)) { kind = shape.kind; return true; }
                if (element.TryGetComponent(out RaymarchableElement source)) { kind = source.Kind; return true; }
            }

            // Fallback for copies made before RaymarchShape existed: built-in
            // meshes are named Sphere/Cube/Capsule/Cylinder, procedural ones
            // are saved under their PrimitiveKind name.
            Mesh mesh = visual.sharedMesh;
            if (mesh != null)
            {
                string meshName = mesh.name.Replace(" Instance", "");
                if (meshName == "Cube") { kind = PrimitiveKind.Box; return true; }
                if (Enum.TryParse(meshName, out kind)) return true;
            }

            kind = default;
            return false;
        }
    }
}
