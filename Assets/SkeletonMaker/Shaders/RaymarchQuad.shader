Shader "SkeletonMaker/RaymarchQuad"
{
    // Raymarches the placed primitives (sent in every frame by RaymarchQuad)
    // inside whatever mesh this material is on - normally a quad. The quad
    // is a window: each pixel marches from the current eye through its own
    // surface point, so the shapes keep true per-eye stereo depth, and only
    // the pixels the quad covers pay for the raymarch.
    //
    // Every shape is evaluated in its "Visual" child's own mesh space
    // (_RMQ_ShapeWorldToLocal), with each SDF below matching the exact
    // native mesh RaymarchableElement / ProceduralMeshFactory builds - so
    // Size, orientation and non-uniform scale all come for free from that
    // transform, and a shape always lines up with its mesh.

    Properties
    {
        [Toggle] _ClipBackground ("Clip Background (cutout: only the shapes are drawn)", Float) = 0
        [Toggle] _StartAtSurface ("Start Rays At Quad Surface (hide anything in front of the quad)", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" "PreviewType" = "Plane" }
        Cull [_Cull]
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "RaymarchQuad"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_instancing // required for single-pass instanced stereo (VR)

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Must match RaymarchQuad.MaxShapes.
            #define MAX_SHAPES 64
            #define EPSILON 0.0005

            // Kind order matches SkeletonMaker.PrimitiveKind.
            #define KIND_SPHERE 0
            #define KIND_BOX 1
            #define KIND_CAPSULE 2
            #define KIND_PYRAMID 3
            #define KIND_TORUS 4
            #define KIND_ROUNDBOX 5
            #define KIND_CONE 6
            #define KIND_OCTAHEDRON 7
            #define KIND_HEXPRISM 8
            #define KIND_CYLINDER 9
            #define KIND_TRIPRISM 10
            #define KIND_LINK 11

            float _ClipBackground;
            float _StartAtSurface;

            // --- per-frame data from RaymarchQuad ---
            int _RMQ_ShapeCount;
            float4x4 _RMQ_ShapeWorldToLocal[MAX_SHAPES]; // world -> the shape's native mesh space
            float4 _RMQ_ShapeParams[MAX_SHAPES];         // x = kind, y = mesh->world distance scale
            float4 _RMQ_ShapeBounds[MAX_SHAPES];         // xyz = world center, w = world radius
            float4 _RMQ_ShapeColors[MAX_SHAPES];
            float4 _RMQ_SceneBounds;                     // bounding sphere around every shape

            int _RMQ_MaxSteps;
            float _RMQ_Smoothing;
            float3 _RMQ_LightDir;                        // world, pointing toward the light
            float3 _RMQ_LightColor;
            float _RMQ_Ambient;
            float _RMQ_Specular;
            float _RMQ_SpecularPow;
            float _RMQ_ShadowStrength;
            float _RMQ_OcclusionStrength;
            float3 _RMQ_BackgroundColor;

            // -----------------------------------------------------------------
            // Shape SDFs, each in its own native mesh space (see the matching
            // mesh in RaymarchableElement / ProceduralMeshFactory).
            // -----------------------------------------------------------------

            // Built-in Unity sphere: radius 0.5.
            float SphereSDF(float3 p) { return length(p) - 0.5; }

            // Built-in Unity cube: 1 x 1 x 1.
            float BoxSDF(float3 p)
            {
                float3 q = abs(p) - 0.5;
                return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
            }

            // Built-in Unity capsule: radius 0.5, total height 2 along Y.
            float CapsuleSDF(float3 p)
            {
                p.y -= clamp(p.y, -0.5, 0.5);
                return length(p) - 0.5;
            }

            // Built-in Unity cylinder: radius 0.5, total height 2 along Y.
            float CylinderSDF(float3 p)
            {
                float2 d = abs(float2(length(p.xz), p.y)) - float2(0.5, 1.0);
                return min(max(d.x, d.y), 0.0) + length(max(d, 0.0));
            }

            // Square base 1 x 1 at y = -0.5, apex at y = +0.5 (after iq).
            float PyramidSDF(float3 p)
            {
                const float h = 1.0;
                const float m2 = h * h + 0.25;
                p.y += 0.5;
                p.xz = abs(p.xz);
                p.xz = (p.z > p.x) ? p.zx : p.xz;
                p.xz -= 0.5;
                float3 q = float3(p.z, h * p.y - 0.5 * p.x, h * p.x + 0.5 * p.y);
                float s = max(-q.x, 0.0);
                float t = clamp((q.y - 0.5 * p.z) / (m2 + 0.25), 0.0, 1.0);
                float a = m2 * (q.x + s) * (q.x + s) + q.y * q.y;
                float b = m2 * (q.x + 0.5 * t) * (q.x + 0.5 * t) + (q.y - m2 * t) * (q.y - m2 * t);
                float d2 = min(q.y, -q.x * m2 - q.y * 0.5) > 0.0 ? 0.0 : min(a, b);
                return sqrt((d2 + q.z * q.z) / m2) * sign(max(q.z, -p.y));
            }

            // Ring radius 0.35 in the XY plane (axis Z), tube radius 0.15.
            float TorusSDF(float3 p)
            {
                float2 q = float2(length(p.xy) - 0.35, p.z);
                return length(q) - 0.15;
            }

            // Superellipsoid |x|^n + |y|^n + |z|^n = 0.5^n, n = 2 / 0.3
            // (ProceduralMeshFactory's roundedness). An L-n norm has gradient
            // length <= 1 for n >= 2, so this stays a safe distance bound.
            float RoundBoxSDF(float3 p)
            {
                const float n = 2.0 / 0.3;
                float3 a = pow(max(abs(p), 1e-5), n); // avoid pow(0, n) edge cases on some GPUs
                return pow(a.x + a.y + a.z, 1.0 / n) - 0.5;
            }

            // Base radius 0.5 at y = -0.5, apex at y = +0.5 (after iq).
            float ConeSDF(float3 p)
            {
                const float2 q = float2(0.5, -1.0); // (base radius, -height), apex at origin
                p.y -= 0.5;
                float2 w = float2(length(p.xz), p.y);
                float2 a = w - q * clamp(dot(w, q) / dot(q, q), 0.0, 1.0);
                float2 b = w - q * float2(clamp(w.x / q.x, 0.0, 1.0), 1.0);
                float d = min(dot(a, a), dot(b, b));
                float s = max(-(w.x * q.y - w.y * q.x), -(w.y - q.y));
                return sqrt(d) * sign(s);
            }

            // Vertices at +-0.5 on each axis (after iq).
            float OctahedronSDF(float3 p)
            {
                const float s = 0.5;
                p = abs(p);
                float m = p.x + p.y + p.z - s;
                float3 q;
                if (3.0 * p.x < m) q = p.xyz;
                else if (3.0 * p.y < m) q = p.yzx;
                else if (3.0 * p.z < m) q = p.zxy;
                else return m * 0.57735027;
                float k = clamp(0.5 * (q.z - q.y + s), 0.0, s);
                return length(float3(q.x, q.y - s + k, q.z - k));
            }

            // Hexagon in XZ with a vertex on +X, circumradius 0.5, height 1 along Y.
            float HexPrismSDF(float3 p)
            {
                float2 q = abs(p.xz);
                float d2 = max(q.x * 0.8660254 + q.y * 0.5, q.y) - 0.4330127; // apothem
                float2 d = float2(d2, abs(p.y) - 0.5);
                return min(max(d.x, d.y), 0.0) + length(max(d, 0.0));
            }

            // Triangle in XZ with a vertex on +X, circumradius 0.5, height 1 along Y.
            float TriPrismSDF(float3 p)
            {
                float d2 = max(abs(p.z) * 0.8660254 + p.x * 0.5, -p.x) - 0.25; // inradius
                float2 d = float2(d2, abs(p.y) - 0.5);
                return min(max(d.x, d.y), 0.0) + length(max(d, 0.0));
            }

            // Stadium path in the XY plane (straight half-length 0.15 along X,
            // end radius 0.18) swept with a 0.12 tube.
            float LinkSDF(float3 p)
            {
                float3 q = float3(p.x - clamp(p.x, -0.15, 0.15), p.y, p.z);
                return length(float2(length(q.xy) - 0.18, q.z)) - 0.12;
            }

            float ShapeSDF(float3 p, int kind)
            {
                switch (kind)
                {
                    case KIND_SPHERE: return SphereSDF(p);
                    case KIND_BOX: return BoxSDF(p);
                    case KIND_CAPSULE: return CapsuleSDF(p);
                    case KIND_PYRAMID: return PyramidSDF(p);
                    case KIND_TORUS: return TorusSDF(p);
                    case KIND_ROUNDBOX: return RoundBoxSDF(p);
                    case KIND_CONE: return ConeSDF(p);
                    case KIND_OCTAHEDRON: return OctahedronSDF(p);
                    case KIND_HEXPRISM: return HexPrismSDF(p);
                    case KIND_CYLINDER: return CylinderSDF(p);
                    case KIND_TRIPRISM: return TriPrismSDF(p);
                    default: return LinkSDF(p);
                }
            }

            // -----------------------------------------------------------------
            // Scene: smooth union of every shape, blending color alongside.
            // -----------------------------------------------------------------

            // Polynomial smooth min; returns (distance, blend factor toward b).
            float2 SmoothMin(float a, float b, float k)
            {
                if (k <= 0.0) return a < b ? float2(a, 0.0) : float2(b, 1.0);
                float h = saturate(0.5 + 0.5 * (a - b) / k);
                return float2(lerp(a, b, h) - k * h * (1.0 - h), h);
            }

            float SceneSDF(float3 p, out float3 color)
            {
                float best = 1e5;
                color = float3(0, 0, 0);
                float k = _RMQ_Smoothing;

                [loop]
                for (int i = 0; i < _RMQ_ShapeCount; i++)
                {
                    // Cheap reject: if even this shape's bounding sphere is
                    // further than the blend radius beyond the current best,
                    // it can't change the result.
                    float4 bounds = _RMQ_ShapeBounds[i];
                    if (length(p - bounds.xyz) - bounds.w > best + k) continue;

                    float3 local = mul(_RMQ_ShapeWorldToLocal[i], float4(p, 1.0)).xyz;
                    float d = ShapeSDF(local, (int)_RMQ_ShapeParams[i].x) * _RMQ_ShapeParams[i].y;

                    float2 m = SmoothMin(best, d, k);
                    color = lerp(color, _RMQ_ShapeColors[i].rgb, m.y);
                    best = m.x;
                }
                return best;
            }

            float SceneDist(float3 p)
            {
                float3 unused;
                return SceneSDF(p, unused);
            }

            // Tetrahedral normal: 4 scene evaluations instead of 6.
            float3 EstimateNormal(float3 p)
            {
                const float2 e = float2(1.0, -1.0) * 0.5773 * EPSILON * 2.0;
                return normalize(
                    e.xyy * SceneDist(p + e.xyy) +
                    e.yyx * SceneDist(p + e.yyx) +
                    e.yxy * SceneDist(p + e.yxy) +
                    e.xxx * SceneDist(p + e.xxx));
            }

            float SoftShadow(float3 p, float3 dir, float maxT)
            {
                float res = 1.0;
                float t = 0.02;
                [loop]
                for (int i = 0; i < 24 && t < maxT; i++)
                {
                    float h = SceneDist(p + dir * t);
                    if (h < EPSILON) return 0.0;
                    res = min(res, 8.0 * h / t);
                    t += clamp(h, 0.01, 0.2);
                }
                return saturate(res);
            }

            float AmbientOcclusion(float3 p, float3 n)
            {
                float occ = 0.0;
                float w = 1.0;
                [unroll]
                for (int i = 1; i <= 4; i++)
                {
                    float h = 0.03 * i;
                    occ += (h - SceneDist(p + n * h)) * w;
                    w *= 0.6;
                }
                return saturate(1.0 - 4.0 * occ);
            }

            // Ray vs sphere; returns (tEnter, tExit), or tEnter > tExit on a miss.
            float2 RaySphere(float3 ro, float3 rd, float4 sphere)
            {
                float3 oc = ro - sphere.xyz;
                float b = dot(oc, rd);
                float c = dot(oc, oc) - sphere.w * sphere.w;
                float h = b * b - c;
                if (h < 0.0) return float2(1.0, -1.0);
                h = sqrt(h);
                return float2(-b - h, -b + h);
            }

            // -----------------------------------------------------------------

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                return OUT;
            }

            float4 Background()
            {
                if (_ClipBackground > 0.5) discard;
                return float4(_RMQ_BackgroundColor, 1.0);
            }

            float4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                if (_RMQ_ShapeCount <= 0) return Background();

                // _WorldSpaceCameraPos is the current eye's position under
                // single-pass instanced stereo.
                float3 eye = _WorldSpaceCameraPos.xyz;
                float3 toSurface = IN.positionWS - eye;
                float surfaceDist = length(toSurface);
                float3 rd = toSurface / surfaceDist;

                // Only march the stretch of ray inside the shapes' bounding
                // sphere - pixels that miss it cost nothing beyond this test.
                float2 range = RaySphere(eye, rd, _RMQ_SceneBounds);
                float tStart = max(range.x, _StartAtSurface > 0.5 ? surfaceDist : 0.0);
                if (range.y < tStart) return Background();

                float t = tStart;
                bool hit = false;
                [loop]
                for (int i = 0; i < _RMQ_MaxSteps; i++)
                {
                    float d = SceneDist(eye + rd * t);
                    if (d < EPSILON * t) { hit = true; break; }
                    t += d;
                    if (t > range.y) break;
                }
                if (!hit) return Background();

                float3 p = eye + rd * t;
                float3 albedo;
                SceneSDF(p, albedo);
                float3 n = EstimateNormal(p);
                float3 l = normalize(_RMQ_LightDir);

                float diffuse = saturate(dot(n, l));
                if (_RMQ_ShadowStrength > 0.0 && diffuse > 0.0)
                    diffuse *= lerp(1.0, SoftShadow(p + n * 0.002, l, _RMQ_SceneBounds.w * 2.0), _RMQ_ShadowStrength);

                float occlusion = _RMQ_OcclusionStrength > 0.0 ? lerp(1.0, AmbientOcclusion(p, n), _RMQ_OcclusionStrength) : 1.0;

                float3 h = normalize(l - rd);
                float spec = pow(saturate(dot(n, h)), _RMQ_SpecularPow) * _RMQ_Specular * (diffuse > 0.0 ? 1.0 : 0.0);

                float3 color = albedo * (_RMQ_Ambient * occlusion + diffuse * _RMQ_LightColor) + spec * _RMQ_LightColor;
                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
