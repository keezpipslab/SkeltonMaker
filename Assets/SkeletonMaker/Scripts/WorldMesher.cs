using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>One primitive of a world to bake, as RaymarchQuad sends it to its shader.</summary>
    public struct WorldShape
    {
        public Matrix4x4 worldToLocal; // world -> the shape's native mesh space
        public PrimitiveKind kind;
        public float scale;            // mesh -> world distance scale
        public Vector3 center;         // bounding sphere, world
        public float radius;
        public bool subtract;          // carved out of the others instead of added
        public float smoothing;        // its own, on top of everyone's
    }

    /// <summary>
    /// Pulls a mesh out of the raymarched surface: the rayMarchVR project's
    /// RaymarchSkeletonMeshBaker (marching tetrahedra over a grid, then an
    /// .obj), ported from an editor window to something that runs in Play,
    /// with this project's shapes in place of that one's skeleton.
    ///
    /// The distance math is a C# copy of Shaders/RaymarchQuad.shader - the
    /// twelve shape SDFs, SmoothMin/SmoothMax and CombinedSDF's "A with B
    /// carved out" - so a change there has to be made here too.
    ///
    /// Marching tetrahedra (6 per grid cell, sharing the cell's main
    /// diagonal) rather than marching cubes, as in the original: a
    /// tetrahedron has only 16 sign cases, so no lookup table is needed.
    /// Unlike the original, vertices are shared between triangles (one per
    /// grid edge the surface crosses), which keeps the file a sixth the size.
    ///
    /// Nothing here touches a Unity object, so Build and WriteObj may run on
    /// any thread.
    /// </summary>
    public static class WorldMesher
    {
        public class Result
        {
            public Vector3[] vertices;
            public Vector3[] normals;
            public List<int> triangles;
            public float voxel; // the cell size it was baked with
        }

        // -----------------------------------------------------------------
        // Shape SDFs, each in its own native mesh space. See the shader for
        // what each one is.
        // -----------------------------------------------------------------

        private static float Sign(float v) => v > 0f ? 1f : v < 0f ? -1f : 0f;

        private static float Length(float x, float y) => Mathf.Sqrt(x * x + y * y);

        private static float SphereSDF(Vector3 p) => p.magnitude - 0.5f;

        private static float BoxSDF(Vector3 p)
        {
            float qx = Mathf.Abs(p.x) - 0.5f, qy = Mathf.Abs(p.y) - 0.5f, qz = Mathf.Abs(p.z) - 0.5f;
            var outside = new Vector3(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f), Mathf.Max(qz, 0f));
            return outside.magnitude + Mathf.Min(Mathf.Max(qx, Mathf.Max(qy, qz)), 0f);
        }

        private static float CapsuleSDF(Vector3 p)
        {
            p.y -= Mathf.Clamp(p.y, -0.5f, 0.5f);
            return p.magnitude - 0.5f;
        }

        // Distance to a prism: dx from its outline, dy from its two flat ends.
        private static float Extrude(float dx, float dy) =>
            Mathf.Min(Mathf.Max(dx, dy), 0f) + Length(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f));

        private static float CylinderSDF(Vector3 p) => Extrude(Length(p.x, p.z) - 0.5f, Mathf.Abs(p.y) - 1f);

        private static float PyramidSDF(Vector3 p)
        {
            const float h = 1f;
            const float m2 = h * h + 0.25f;
            p.y += 0.5f;
            float px = Mathf.Abs(p.x), pz = Mathf.Abs(p.z);
            if (pz > px) (px, pz) = (pz, px);
            px -= 0.5f;
            pz -= 0.5f;

            float qx = pz, qy = h * p.y - 0.5f * px, qz = h * px + 0.5f * p.y;
            float s = Mathf.Max(-qx, 0f);
            float t = Mathf.Clamp01((qy - 0.5f * pz) / (m2 + 0.25f));
            float a = m2 * (qx + s) * (qx + s) + qy * qy;
            float b = m2 * (qx + 0.5f * t) * (qx + 0.5f * t) + (qy - m2 * t) * (qy - m2 * t);
            float d2 = Mathf.Min(qy, -qx * m2 - qy * 0.5f) > 0f ? 0f : Mathf.Min(a, b);
            return Mathf.Sqrt((d2 + qz * qz) / m2) * Sign(Mathf.Max(qz, -p.y));
        }

        private static float TorusSDF(Vector3 p) => Length(Length(p.x, p.y) - 0.35f, p.z) - 0.15f;

        private static float RoundBoxSDF(Vector3 p)
        {
            const float n = 2f / 0.3f;
            float a = Mathf.Pow(Mathf.Max(Mathf.Abs(p.x), 1e-5f), n)
                    + Mathf.Pow(Mathf.Max(Mathf.Abs(p.y), 1e-5f), n)
                    + Mathf.Pow(Mathf.Max(Mathf.Abs(p.z), 1e-5f), n);
            return Mathf.Pow(a, 1f / n) - 0.5f;
        }

        private static float ConeSDF(Vector3 p)
        {
            const float qx = 0.5f, qy = -1f; // (base radius, -height), apex at origin
            float wx = Length(p.x, p.z), wy = p.y - 0.5f;

            float ta = Mathf.Clamp01((wx * qx + wy * qy) / (qx * qx + qy * qy));
            float ax = wx - qx * ta, ay = wy - qy * ta;
            float bx = wx - qx * Mathf.Clamp01(wx / qx), by = wy - qy;
            float d = Mathf.Min(ax * ax + ay * ay, bx * bx + by * by);
            float s = Mathf.Max(-(wx * qy - wy * qx), -(wy - qy));
            return Mathf.Sqrt(d) * Sign(s);
        }

        private static float OctahedronSDF(Vector3 p)
        {
            const float s = 0.5f;
            float x = Mathf.Abs(p.x), y = Mathf.Abs(p.y), z = Mathf.Abs(p.z);
            float m = x + y + z - s;

            float qx, qy, qz;
            if (3f * x < m) { qx = x; qy = y; qz = z; }
            else if (3f * y < m) { qx = y; qy = z; qz = x; }
            else if (3f * z < m) { qx = z; qy = x; qz = y; }
            else return m * 0.57735027f;

            float k = Mathf.Clamp(0.5f * (qz - qy + s), 0f, s);
            return new Vector3(qx, qy - s + k, qz - k).magnitude;
        }

        private static float HexPrismSDF(Vector3 p)
        {
            float qx = Mathf.Abs(p.x), qz = Mathf.Abs(p.z);
            return Extrude(Mathf.Max(qx * 0.8660254f + qz * 0.5f, qz) - 0.4330127f, Mathf.Abs(p.y) - 0.5f);
        }

        private static float TriPrismSDF(Vector3 p) =>
            Extrude(Mathf.Max(Mathf.Abs(p.z) * 0.8660254f + p.x * 0.5f, -p.x) - 0.25f, Mathf.Abs(p.y) - 0.5f);

        private static float LinkSDF(Vector3 p)
        {
            float qx = p.x - Mathf.Clamp(p.x, -0.15f, 0.15f);
            return Length(Length(qx, p.y) - 0.18f, p.z) - 0.12f;
        }

        private static float ShapeSDF(Vector3 p, PrimitiveKind kind)
        {
            switch (kind)
            {
                case PrimitiveKind.Sphere: return SphereSDF(p);
                case PrimitiveKind.Box: return BoxSDF(p);
                case PrimitiveKind.Capsule: return CapsuleSDF(p);
                case PrimitiveKind.Pyramid: return PyramidSDF(p);
                case PrimitiveKind.Torus: return TorusSDF(p);
                case PrimitiveKind.RoundBox: return RoundBoxSDF(p);
                case PrimitiveKind.Cone: return ConeSDF(p);
                case PrimitiveKind.Octahedron: return OctahedronSDF(p);
                case PrimitiveKind.HexagonalPrism: return HexPrismSDF(p);
                case PrimitiveKind.Cylinder: return CylinderSDF(p);
                case PrimitiveKind.TriangularPrism: return TriPrismSDF(p);
                default: return LinkSDF(p);
            }
        }

        // -----------------------------------------------------------------
        // The world: the smooth union of what adds, with the smooth union of
        // what carves taken out of it, then inflated.
        // -----------------------------------------------------------------

        private const float Nothing = 1e5f; // "no shape yet", as in the shader

        // h is how much of the result is b's, as the shader's second component.
        private static float SmoothMin(float a, float b, float k, out float h)
        {
            if (k <= 0f)
            {
                h = a < b ? 0f : 1f;
                return Mathf.Min(a, b);
            }
            if (a >= 1e4f)
            {
                h = 1f;
                return b;
            }
            h = Mathf.Clamp01(0.5f + 0.5f * (a - b) / k);
            return Mathf.Lerp(a, b, h) - k * h * (1f - h);
        }

        private static float SmoothMax(float a, float b, float k)
        {
            if (k <= 0f || Mathf.Abs(a) >= 1e4f || Mathf.Abs(b) >= 1e4f) return Mathf.Max(a, b);
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(a, b, h) + k * h * (1f - h);
        }

        private static float Distance(WorldShape[] shapes, float smoothing, float inflate, Vector3 p)
        {
            float added = Nothing, carved = Nothing;
            float addOwn = 0f, carveOwn = 0f; // the shader's ownA and ownB: the own smoothing of each body, where p is
            for (int i = 0; i < shapes.Length; i++)
            {
                float best = shapes[i].subtract ? carved : added;
                float own = shapes[i].smoothing;
                float k = Mathf.Max(0f, smoothing + (shapes[i].subtract ? carveOwn : addOwn) + own); // everyone's, plus what each of the two adds

                // The shader's cheap reject, kept so the two agree exactly.
                if ((p - shapes[i].center).magnitude - shapes[i].radius > best + k) continue;

                float d = ShapeSDF(shapes[i].worldToLocal.MultiplyPoint3x4(p), shapes[i].kind) * shapes[i].scale;
                if (shapes[i].subtract)
                {
                    carved = SmoothMin(carved, d, k, out float h);
                    carveOwn = Mathf.Lerp(carveOwn, own, h);
                }
                else
                {
                    added = SmoothMin(added, d, k, out float h);
                    addOwn = Mathf.Lerp(addOwn, own, h);
                }
            }
            return SmoothMax(added, -carved, Mathf.Max(0f, smoothing + addOwn + carveOwn)) - inflate;
        }

        private static Vector3 Normal(WorldShape[] shapes, float smoothing, float inflate, Vector3 p, float eps)
        {
            // Tetrahedral, as the shader's EstimateNormal.
            float e = 0.5773f * eps;
            var a = new Vector3(e, -e, -e);
            var b = new Vector3(-e, -e, e);
            var c = new Vector3(-e, e, -e);
            var d = new Vector3(e, e, e);
            Vector3 n = a * Distance(shapes, smoothing, inflate, p + a) + b * Distance(shapes, smoothing, inflate, p + b)
                      + c * Distance(shapes, smoothing, inflate, p + c) + d * Distance(shapes, smoothing, inflate, p + d);
            return n.sqrMagnitude > 1e-20f ? n.normalized : Vector3.up;
        }

        // -----------------------------------------------------------------
        // Marching tetrahedra.
        // -----------------------------------------------------------------

        private static readonly int[] CornerX = { 0, 1, 1, 0, 0, 1, 1, 0 };
        private static readonly int[] CornerY = { 0, 0, 1, 1, 0, 0, 1, 1 };
        private static readonly int[] CornerZ = { 0, 0, 0, 0, 1, 1, 1, 1 };

        // Six tetrahedra sharing the cell's main diagonal 0-6.
        private static readonly int[,] Tets =
        {
            { 0, 1, 2, 6 }, { 0, 2, 3, 6 }, { 0, 3, 7, 6 },
            { 0, 7, 4, 6 }, { 0, 4, 5, 6 }, { 0, 5, 1, 6 },
        };

        public static Result Build(WorldShape[] shapes, float smoothing, float inflate, float voxelSize, int maxCells)
        {
            var result = new Result { vertices = new Vector3[0], normals = new Vector3[0], triangles = new List<int>(), voxel = voxelSize };

            // Only what adds can have a surface; what carves just takes away from it.
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            float widestOwn = 0f;
            foreach (var shape in shapes)
            {
                if (shape.subtract) continue;
                widestOwn = Mathf.Max(widestOwn, shape.smoothing);
                min = Vector3.Min(min, shape.center - Vector3.one * shape.radius);
                max = Vector3.Max(max, shape.center + Vector3.one * shape.radius);
            }
            if (min.x > max.x) return result;

            Vector3 size = max - min + Vector3.one * (2f * (smoothing + 2f * widestOwn + Mathf.Max(inflate, 0f)));
            float voxel = Mathf.Max(voxelSize, Mathf.Pow(size.x * size.y * size.z / maxCells, 1f / 3f));
            size += Vector3.one * (4f * voxel); // so the surface is never cut off at the edge of the grid
            Vector3 origin = (min + max) * 0.5f - size * 0.5f;
            result.voxel = voxel;

            int nx = Mathf.Max(2, Mathf.CeilToInt(size.x / voxel));
            int ny = Mathf.Max(2, Mathf.CeilToInt(size.y / voxel));
            int nz = Mathf.Max(2, Mathf.CeilToInt(size.z / voxel));
            int gx = nx + 1, gy = ny + 1, gz = nz + 1;
            int slice = gx * gy;
            long total = (long)slice * gz;

            var dist = new float[total];
            Parallel.For(0, gz, zi =>
            {
                for (int yi = 0; yi < gy; yi++)
                    for (int xi = 0; xi < gx; xi++)
                        dist[xi + gx * yi + slice * zi] = Distance(shapes, smoothing, inflate, origin + new Vector3(xi, yi, zi) * voxel);
            });

            Vector3 Point(int index) => origin + new Vector3(index % gx, index / gx % gy, index / slice) * voxel;

            var vertices = new List<Vector3>();
            var triangles = result.triangles;
            var onEdge = new Dictionary<long, int>(); // grid edge -> the vertex where the surface crosses it

            int Vertex(int from, int to)
            {
                if (from > to) (from, to) = (to, from);
                long key = from * total + to;
                if (onEdge.TryGetValue(key, out int index)) return index;

                float t = Mathf.Clamp01(dist[from] / (dist[from] - dist[to])); // the two differ in sign
                index = vertices.Count;
                vertices.Add(Vector3.Lerp(Point(from), Point(to), t));
                onEdge.Add(key, index);
                return index;
            }

            void Triangle(int a, int b, int c, bool flip)
            {
                if (a == b || b == c || a == c) return;
                if (flip) (b, c) = (c, b);
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }

            var corner = new int[8];
            var inside = new List<int>(4);
            var outside = new List<int>(4);

            for (int zi = 0; zi < nz; zi++)
            {
                for (int yi = 0; yi < ny; yi++)
                {
                    for (int xi = 0; xi < nx; xi++)
                    {
                        int negative = 0;
                        for (int i = 0; i < 8; i++)
                        {
                            corner[i] = xi + CornerX[i] + gx * (yi + CornerY[i]) + slice * (zi + CornerZ[i]);
                            if (dist[corner[i]] < 0f) negative++;
                        }
                        if (negative == 0 || negative == 8) continue;

                        for (int t = 0; t < 6; t++)
                        {
                            inside.Clear();
                            outside.Clear();
                            for (int i = 0; i < 4; i++)
                            {
                                int index = corner[Tets[t, i]];
                                if (dist[index] < 0f) inside.Add(index);
                                else outside.Add(index);
                            }
                            if (inside.Count == 0 || outside.Count == 0) continue;

                            // Which way round a face goes is worked out from the
                            // tetrahedron's own corners rather than from the face:
                            // a face can be a sliver with no direction to speak
                            // of, but it always faces the way the cut does. Both
                            // products below are the tetrahedron's volume, never 0.
                            Vector3 outward = Point(outside[0]) - Point(inside[0]);
                            if (inside.Count == 1 || outside.Count == 1)
                            {
                                // One corner cut off: the face is the opposite face, seen from it.
                                bool insideAlone = inside.Count == 1;
                                int alone = insideAlone ? inside[0] : outside[0];
                                List<int> rest = insideAlone ? outside : inside;
                                Vector3 face = Vector3.Cross(Point(rest[1]) - Point(rest[0]), Point(rest[2]) - Point(rest[0]));
                                Triangle(Vertex(alone, rest[0]), Vertex(alone, rest[1]), Vertex(alone, rest[2]), Vector3.Dot(face, outward) < 0f);
                            }
                            else
                            {
                                // The cut is a quad, in the order 00, 01, 11, 10.
                                int q00 = Vertex(inside[0], outside[0]), q01 = Vertex(inside[0], outside[1]);
                                int q10 = Vertex(inside[1], outside[0]), q11 = Vertex(inside[1], outside[1]);
                                Vector3 face = Vector3.Cross(Point(outside[1]) - Point(outside[0]), Point(inside[1]) - Point(inside[0]));
                                bool flip = Vector3.Dot(face, outward) < 0f;
                                Triangle(q00, q01, q11, flip);
                                Triangle(q00, q11, q10, flip);
                            }
                        }
                    }
                }
            }

            result.vertices = vertices.ToArray();
            result.normals = new Vector3[result.vertices.Length];
            float eps = voxel * 0.5f;
            Parallel.For(0, result.vertices.Length, i =>
            {
                result.normals[i] = Normal(shapes, smoothing, inflate, result.vertices[i], eps);
            });
            return result;
        }

        /// <summary>Writes positions, normals and faces. Unity is left-handed and
        /// .obj is not, so X is negated and each face turned around, as in the
        /// original, rather than writing a mirrored mesh.</summary>
        public static void WriteObj(Result mesh, string path)
        {
            var culture = CultureInfo.InvariantCulture; // a decimal point, whatever this PC's language is
            using (var writer = new StreamWriter(path))
            {
                writer.WriteLine("# Baked by SkeletonMaker's designer scene");
                foreach (var v in mesh.vertices)
                    writer.WriteLine(string.Format(culture, "v {0:G7} {1:G7} {2:G7}", -v.x, v.y, v.z));
                foreach (var n in mesh.normals)
                    writer.WriteLine(string.Format(culture, "vn {0:G4} {1:G4} {2:G4}", -n.x, n.y, n.z));

                var t = mesh.triangles;
                for (int i = 0; i < t.Count; i += 3)
                {
                    int a = t[i] + 1, b = t[i + 1] + 1, c = t[i + 2] + 1;
                    writer.WriteLine(string.Format(culture, "f {0}//{0} {1}//{1} {2}//{2}", c, b, a));
                }
            }
        }
    }
}
