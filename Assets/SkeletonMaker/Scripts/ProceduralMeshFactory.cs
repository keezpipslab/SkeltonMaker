using System.Collections.Generic;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// Procedural placeholder meshes for primitive kinds with no Unity
    /// built-in equivalent (Sphere/Box/Capsule/Cylinder use
    /// GameObject.CreatePrimitive instead - see RaymarchableElement). Every
    /// mesh here is built to roughly fill a 1-unit bounding box per axis so
    /// Size maps onto localScale the same way the built-ins do (see
    /// RaymarchableElement.LocalScaleFor, which normalizes by each mesh's
    /// actual bounds rather than assuming this exactly).
    ///
    /// Meshes are saved as real project assets the first time they're built
    /// in the Editor: a MeshFilter reference to a bare `new Mesh()` doesn't
    /// survive being baked into a prefab, only a reference to an actual
    /// asset does (same reason GameObject.CreatePrimitive's built-in meshes
    /// survive it fine).
    /// </summary>
    public static class ProceduralMeshFactory
    {
        private static readonly Dictionary<PrimitiveKind, Mesh> Cache = new Dictionary<PrimitiveKind, Mesh>();

        public static Mesh Get(PrimitiveKind kind)
        {
            if (Cache.TryGetValue(kind, out var cached) && cached != null) return cached;

            string assetPath = "Assets/SkeletonMaker/Meshes/" + kind + ".asset";

#if UNITY_EDITOR
            var existing = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing != null)
            {
                Cache[kind] = existing;
                return existing;
            }
#endif

            var mesh = Generate(kind);

#if UNITY_EDITOR
            if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/SkeletonMaker/Meshes"))
                UnityEditor.AssetDatabase.CreateFolder("Assets/SkeletonMaker", "Meshes");
            UnityEditor.AssetDatabase.CreateAsset(mesh, assetPath);
#endif

            Cache[kind] = mesh;
            return mesh;
        }

        private static Mesh Generate(PrimitiveKind kind)
        {
            switch (kind)
            {
                case PrimitiveKind.Pyramid: return BuildPyramid();
                case PrimitiveKind.Octahedron: return BuildOctahedron();
                case PrimitiveKind.Cone: return BuildCone();
                case PrimitiveKind.HexagonalPrism: return BuildPrism(6, "HexagonalPrism");
                case PrimitiveKind.TriangularPrism: return BuildPrism(3, "TriangularPrism");
                case PrimitiveKind.Torus: return BuildTorus();
                case PrimitiveKind.Link: return BuildLink();
                case PrimitiveKind.RoundBox: return BuildRoundBox();
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(kind), kind, "no procedural mesh for " + kind);
            }
        }

        // -----------------------------------------------------------------
        // Shared helpers. AddTri takes a rough "which way is outside" hint
        // and picks whichever vertex order makes the face's normal agree
        // with it, instead of relying on getting the order right by hand -
        // any wrong guess self-corrects instead of silently baking an
        // inside-out face.
        // -----------------------------------------------------------------

        private static void AddTri(List<Vector3> verts, List<int> tris, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 outwardHint)
        {
            int i0 = verts.Count; verts.Add(v0);
            int i1 = verts.Count; verts.Add(v1);
            int i2 = verts.Count; verts.Add(v2);
            AddTriIndices(tris, i0, i1, i2, v0, v1, v2, outwardHint);
        }

        private static void AddTriIndices(List<int> tris, int i0, int i1, int i2, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 outwardHint)
        {
            Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0);
            if (Vector3.Dot(normal, outwardHint) < 0f)
            {
                tris.Add(i0); tris.Add(i2); tris.Add(i1);
            }
            else
            {
                tris.Add(i0); tris.Add(i1); tris.Add(i2);
            }
        }

        private static Mesh Finish(string name, List<Vector3> verts, List<int> tris)
        {
            var mesh = new Mesh { name = name };
            if (verts.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        // -----------------------------------------------------------------
        // Pyramid - 4-sided, base + apex.
        // -----------------------------------------------------------------
        private static Mesh BuildPyramid()
        {
            Vector3 apex = new Vector3(0f, 0.5f, 0f);
            Vector3 a = new Vector3(-0.5f, -0.5f, -0.5f);
            Vector3 b = new Vector3(0.5f, -0.5f, -0.5f);
            Vector3 c = new Vector3(0.5f, -0.5f, 0.5f);
            Vector3 d = new Vector3(-0.5f, -0.5f, 0.5f);

            var verts = new List<Vector3>();
            var tris = new List<int>();

            AddTri(verts, tris, a, b, c, Vector3.down);
            AddTri(verts, tris, a, c, d, Vector3.down);
            AddTri(verts, tris, a, b, apex, (a + b + apex) / 3f);
            AddTri(verts, tris, b, c, apex, (b + c + apex) / 3f);
            AddTri(verts, tris, c, d, apex, (c + d + apex) / 3f);
            AddTri(verts, tris, d, a, apex, (d + a + apex) / 3f);

            return Finish("Pyramid", verts, tris);
        }

        // -----------------------------------------------------------------
        // Octahedron - two square pyramids glued base-to-base.
        // -----------------------------------------------------------------
        private static Mesh BuildOctahedron()
        {
            Vector3 px = new Vector3(0.5f, 0f, 0f), nx = new Vector3(-0.5f, 0f, 0f);
            Vector3 py = new Vector3(0f, 0.5f, 0f), ny = new Vector3(0f, -0.5f, 0f);
            Vector3 pz = new Vector3(0f, 0f, 0.5f), nz = new Vector3(0f, 0f, -0.5f);

            var verts = new List<Vector3>();
            var tris = new List<int>();

            void Face(Vector3 v0, Vector3 v1, Vector3 v2) => AddTri(verts, tris, v0, v1, v2, (v0 + v1 + v2) / 3f);

            Face(px, py, pz); Face(pz, py, nx); Face(nx, py, nz); Face(nz, py, px);
            Face(px, pz, ny); Face(pz, nx, ny); Face(nx, nz, ny); Face(nz, px, ny);

            return Finish("Octahedron", verts, tris);
        }

        // -----------------------------------------------------------------
        // Cone - circular base + apex.
        // -----------------------------------------------------------------
        private static Mesh BuildCone()
        {
            const int segments = 24;
            Vector3 apex = new Vector3(0f, 0.5f, 0f);
            Vector3 baseCenter = new Vector3(0f, -0.5f, 0f);

            Vector3 Rim(int i)
            {
                float t = i / (float)segments * Mathf.PI * 2f;
                return new Vector3(Mathf.Cos(t) * 0.5f, -0.5f, Mathf.Sin(t) * 0.5f);
            }

            var verts = new List<Vector3>();
            var tris = new List<int>();

            for (int i = 0; i < segments; i++)
            {
                int j = (i + 1) % segments;
                Vector3 r0 = Rim(i), r1 = Rim(j);
                AddTri(verts, tris, r0, r1, apex, (r0 + r1 + apex) / 3f);
                AddTri(verts, tris, baseCenter, r1, r0, Vector3.down);
            }

            return Finish("Cone", verts, tris);
        }

        // -----------------------------------------------------------------
        // N-sided prism - used for both the hexagonal and triangular prism.
        // -----------------------------------------------------------------
        private static Mesh BuildPrism(int sides, string name)
        {
            const float radius = 0.5f;

            Vector3 Top(int i) { float t = i / (float)sides * Mathf.PI * 2f; return new Vector3(Mathf.Cos(t) * radius, 0.5f, Mathf.Sin(t) * radius); }
            Vector3 Bot(int i) { float t = i / (float)sides * Mathf.PI * 2f; return new Vector3(Mathf.Cos(t) * radius, -0.5f, Mathf.Sin(t) * radius); }

            Vector3 topCenter = new Vector3(0f, 0.5f, 0f);
            Vector3 botCenter = new Vector3(0f, -0.5f, 0f);

            var verts = new List<Vector3>();
            var tris = new List<int>();

            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                Vector3 t0 = Top(i), t1 = Top(j), b0 = Bot(i), b1 = Bot(j);

                AddTri(verts, tris, topCenter, t0, t1, Vector3.up);
                AddTri(verts, tris, botCenter, b1, b0, Vector3.down);

                Vector3 sideOutward = new Vector3(t0.x + t1.x, 0f, t0.z + t1.z);
                AddTri(verts, tris, t0, b0, b1, sideOutward);
                AddTri(verts, tris, t0, b1, t1, sideOutward);
            }

            return Finish(name, verts, tris);
        }

        // -----------------------------------------------------------------
        // Torus and link both sweep a circular tube cross-section around a
        // closed planar path - a circle for the torus, a stadium/racetrack
        // shape (two straight sides + two semicircle ends) for the link.
        // -----------------------------------------------------------------
        private static Mesh BuildTorus()
        {
            const int ringSegments = 32, tubeSegments = 16;
            const float ringRadius = 0.35f, tubeRadius = 0.15f;

            var path = new List<Vector3>();
            for (int i = 0; i < ringSegments; i++)
            {
                float t = i / (float)ringSegments * Mathf.PI * 2f;
                path.Add(new Vector3(Mathf.Cos(t) * ringRadius, Mathf.Sin(t) * ringRadius, 0f));
            }

            return SweepClosedTube(path, tubeRadius, tubeSegments, "Torus");
        }

        private static Mesh BuildLink()
        {
            const int tubeSegments = 16;
            const int capSegments = 12;
            const float tubeRadius = 0.12f;
            const float capRadius = 0.18f;
            const float straightHalfLength = 0.15f;

            var path = new List<Vector3>();

            for (int i = 0; i <= capSegments; i++)
            {
                float t = -Mathf.PI / 2f + (i / (float)capSegments) * Mathf.PI;
                path.Add(new Vector3(straightHalfLength + Mathf.Cos(t) * capRadius, Mathf.Sin(t) * capRadius, 0f));
            }
            for (int i = 0; i <= capSegments; i++)
            {
                float t = Mathf.PI / 2f + (i / (float)capSegments) * Mathf.PI;
                path.Add(new Vector3(-straightHalfLength + Mathf.Cos(t) * capRadius, Mathf.Sin(t) * capRadius, 0f));
            }

            return SweepClosedTube(path, tubeRadius, tubeSegments, "Link");
        }

        private static Mesh SweepClosedTube(List<Vector3> path, float tubeRadius, int tubeSegments, string name)
        {
            int n = path.Count;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var ring = new int[n, tubeSegments];

            Vector3 Tangent(int i)
            {
                Vector3 prev = path[(i - 1 + n) % n];
                Vector3 next = path[(i + 1) % n];
                return (next - prev).normalized;
            }

            for (int i = 0; i < n; i++)
            {
                Vector3 center = path[i];
                Vector3 tangent = Tangent(i);
                Vector3 radial = new Vector3(tangent.y, -tangent.x, 0f);
                if (radial.sqrMagnitude < 1e-8f) radial = Vector3.right;
                radial.Normalize();
                Vector3 z = Vector3.forward;

                for (int j = 0; j < tubeSegments; j++)
                {
                    float a = j / (float)tubeSegments * Mathf.PI * 2f;
                    Vector3 offset = radial * Mathf.Cos(a) * tubeRadius + z * Mathf.Sin(a) * tubeRadius;
                    ring[i, j] = verts.Count;
                    verts.Add(center + offset);
                }
            }

            for (int i = 0; i < n; i++)
            {
                int iN = (i + 1) % n;
                for (int j = 0; j < tubeSegments; j++)
                {
                    int jN = (j + 1) % tubeSegments;
                    int i00 = ring[i, j], i01 = ring[i, jN], i10 = ring[iN, j], i11 = ring[iN, jN];
                    Vector3 v00 = verts[i00], v01 = verts[i01], v10 = verts[i10], v11 = verts[i11];
                    Vector3 outward = v00 - path[i];

                    AddTriIndices(tris, i00, i10, i11, v00, v10, v11, outward);
                    AddTriIndices(tris, i00, i11, i01, v00, v11, v01, outward);
                }
            }

            return Finish(name, verts, tris);
        }

        // -----------------------------------------------------------------
        // Round box - a superellipsoid (a "squircle" sphere): same UV-sphere
        // topology as a normal sphere, but each point's sin/cos components
        // are raised to a power < 1 before being placed, which pushes the
        // surface out toward flat faces with softened edges instead of a
        // round profile.
        // -----------------------------------------------------------------
        private static Mesh BuildRoundBox()
        {
            const int latSegments = 16, lonSegments = 24;
            const float roundedness = 0.3f; // lower = boxier/sharper edges, 1 = sphere

            float SignedPow(float v, float p) => Mathf.Sign(v) * Mathf.Pow(Mathf.Abs(v), p);

            var verts = new List<Vector3>();
            var tris = new List<int>();
            var indices = new int[latSegments + 1, lonSegments + 1];

            for (int i = 0; i <= latSegments; i++)
            {
                float theta = i / (float)latSegments * Mathf.PI;
                for (int j = 0; j <= lonSegments; j++)
                {
                    float phi = j / (float)lonSegments * Mathf.PI * 2f;
                    float sx = Mathf.Sin(theta) * Mathf.Cos(phi);
                    float sy = Mathf.Cos(theta);
                    float sz = Mathf.Sin(theta) * Mathf.Sin(phi);
                    Vector3 pos = new Vector3(SignedPow(sx, roundedness), SignedPow(sy, roundedness), SignedPow(sz, roundedness)) * 0.5f;
                    indices[i, j] = verts.Count;
                    verts.Add(pos);
                }
            }

            for (int i = 0; i < latSegments; i++)
            {
                for (int j = 0; j < lonSegments; j++)
                {
                    int i00 = indices[i, j], i01 = indices[i, j + 1], i10 = indices[i + 1, j], i11 = indices[i + 1, j + 1];
                    Vector3 v00 = verts[i00], v01 = verts[i01], v10 = verts[i10], v11 = verts[i11];
                    Vector3 outward = (v00 + v01 + v10 + v11) * 0.25f;

                    AddTriIndices(tris, i00, i10, i11, v00, v10, v11, outward);
                    AddTriIndices(tris, i00, i11, i01, v00, v11, v01, outward);
                }
            }

            return Finish("RoundBox", verts, tris);
        }
    }
}
