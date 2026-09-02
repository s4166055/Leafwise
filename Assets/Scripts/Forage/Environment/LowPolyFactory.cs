using System.Collections.Generic;
using UnityEngine;

namespace Forage
{
    /// <summary>
    /// Builds simple low-poly meshes at runtime (faceted shading via unshared vertices).
    /// All meshes are generated deterministically from the parameters passed in.
    /// </summary>
    public static class LowPolyFactory
    {
        /// <summary>Builds a mesh with flat (faceted) shading by giving every triangle its own vertices.</summary>
        public static Mesh Faceted(List<Vector3> verts, List<int> tris, string name)
        {
            var outVerts = new Vector3[tris.Count];
            var outTris = new int[tris.Count];
            for (int i = 0; i < tris.Count; i++)
            {
                outVerts[i] = verts[tris[i]];
                outTris[i] = i;
            }

            var mesh = new Mesh { name = name };
            if (outVerts.Length > 65000)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = outVerts;
            mesh.triangles = outTris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh Cone(float radius, float height, int segments, float topRadius = 0f)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments;
                float a1 = (i + 1) * Mathf.PI * 2f / segments;
                Vector3 b0 = new Vector3(Mathf.Cos(a0) * radius, 0, Mathf.Sin(a0) * radius);
                Vector3 b1 = new Vector3(Mathf.Cos(a1) * radius, 0, Mathf.Sin(a1) * radius);
                Vector3 t0 = new Vector3(Mathf.Cos(a0) * topRadius, height, Mathf.Sin(a0) * topRadius);
                Vector3 t1 = new Vector3(Mathf.Cos(a1) * topRadius, height, Mathf.Sin(a1) * topRadius);

                int s = verts.Count;
                verts.Add(b0); verts.Add(t0); verts.Add(b1);
                tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
                if (topRadius > 0.001f)
                {
                    s = verts.Count;
                    verts.Add(b1); verts.Add(t0); verts.Add(t1);
                    tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
                }
                // bottom cap
                s = verts.Count;
                verts.Add(Vector3.zero); verts.Add(b1); verts.Add(b0);
                tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
            }
            return Faceted(verts, tris, "Cone");
        }

        /// <summary>Irregular blob built from a jittered octahedron subdivision — used for rocks and leaf canopies.</summary>
        public static Mesh Blob(float radius, int subdivisions, float jitter, int seed, Vector3 scale)
        {
            var rand = new System.Random(seed);
            // octahedron base
            var verts = new List<Vector3>
            {
                Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back
            };
            var tris = new List<int>
            {
                0,4,3, 0,3,5, 0,5,2, 0,2,4,
                1,3,4, 1,5,3, 1,2,5, 1,4,2
            };

            for (int s = 0; s < subdivisions; s++)
            {
                var newTris = new List<int>();
                var midCache = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
                    if (midCache.TryGetValue(key, out int idx)) return idx;
                    verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
                    midCache[key] = verts.Count - 1;
                    return verts.Count - 1;
                }
                for (int i = 0; i < tris.Count; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    newTris.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                tris = newTris;
            }

            for (int i = 0; i < verts.Count; i++)
            {
                float j = 1f + ((float)rand.NextDouble() * 2f - 1f) * jitter;
                var v = verts[i] * radius * j;
                verts[i] = Vector3.Scale(v, scale);
            }
            return Faceted(verts, tris, "Blob");
        }

        // ---------- assembled props (each returns a parent GameObject) ----------

        public static GameObject PineTree(int seed, Material bark, Material leaves, float scale)
        {
            var rand = new System.Random(seed);
            var root = new GameObject("PineTree");
            float trunkH = (1.4f + (float)rand.NextDouble() * 0.8f) * scale;
            float trunkR = 0.14f * scale;

            AddMeshChild(root, Cone(trunkR, trunkH * 1.1f, 6, trunkR * 0.7f), bark, Vector3.zero);

            int tiers = 3;
            float tierR = (1.1f + (float)rand.NextDouble() * 0.4f) * scale;
            float y = trunkH * 0.55f;
            for (int t = 0; t < tiers; t++)
            {
                float r = tierR * (1f - t * 0.28f);
                float h = 1.5f * scale * (1f - t * 0.15f);
                AddMeshChild(root, Cone(r, h, 7), leaves, new Vector3(0, y, 0));
                y += h * 0.55f;
            }

            var col = root.AddComponent<CapsuleCollider>();
            col.radius = trunkR * 1.6f;
            col.height = trunkH + 2.5f * scale;
            col.center = new Vector3(0, col.height * 0.5f, 0);
            return root;
        }

        public static GameObject OakTree(int seed, Material bark, Material leaves, float scale)
        {
            var rand = new System.Random(seed);
            var root = new GameObject("OakTree");
            float trunkH = (1.6f + (float)rand.NextDouble() * 0.9f) * scale;
            float trunkR = 0.18f * scale;

            AddMeshChild(root, Cone(trunkR, trunkH, 6, trunkR * 0.55f), bark, Vector3.zero);

            int clumps = 2 + rand.Next(3);
            for (int c = 0; c < clumps; c++)
            {
                float r = (0.9f + (float)rand.NextDouble() * 0.6f) * scale;
                var offset = new Vector3(
                    ((float)rand.NextDouble() - 0.5f) * 1.2f * scale,
                    trunkH + ((float)rand.NextDouble() - 0.1f) * 0.8f * scale,
                    ((float)rand.NextDouble() - 0.5f) * 1.2f * scale);
                AddMeshChild(root, Blob(r, 1, 0.25f, seed * 31 + c, new Vector3(1f, 0.8f, 1f)), leaves, offset);
            }

            var col = root.AddComponent<CapsuleCollider>();
            col.radius = trunkR * 1.6f;
            col.height = trunkH + 1.5f * scale;
            col.center = new Vector3(0, col.height * 0.5f, 0);
            return root;
        }

        public static GameObject Rock(int seed, Material rockMat, float scale)
        {
            var rand = new System.Random(seed);
            var root = new GameObject("Rock");
            var squash = new Vector3(1f, 0.55f + (float)rand.NextDouble() * 0.35f, 0.8f + (float)rand.NextDouble() * 0.4f);
            var mesh = Blob(scale, 1, 0.3f, seed, squash);
            AddMeshChild(root, mesh, rockMat, Vector3.zero);
            var col = root.AddComponent<SphereCollider>();
            col.radius = scale * 0.8f;
            col.center = new Vector3(0, scale * 0.1f, 0);
            return root;
        }

        public static GameObject Bush(int seed, Material leaves, float scale)
        {
            var root = new GameObject("Bush");
            AddMeshChild(root, Blob(scale, 1, 0.35f, seed, new Vector3(1f, 0.6f, 1f)), leaves,
                new Vector3(0, scale * 0.35f, 0));
            return root;
        }

        public static GameObject BirchTree(int seed, Material whiteBark, Material leaves, float scale)
        {
            var rand = new System.Random(seed);
            var root = new GameObject("BirchTree");
            float trunkH = (2.2f + (float)rand.NextDouble() * 1.2f) * scale;
            float trunkR = 0.09f * scale;

            AddMeshChild(root, Cone(trunkR, trunkH, 5, trunkR * 0.5f), whiteBark, Vector3.zero);

            int clumps = 3 + rand.Next(2);
            for (int c = 0; c < clumps; c++)
            {
                float r = (0.55f + (float)rand.NextDouble() * 0.35f) * scale;
                var offset = new Vector3(
                    ((float)rand.NextDouble() - 0.5f) * 0.8f * scale,
                    trunkH * (0.7f + (float)rand.NextDouble() * 0.35f),
                    ((float)rand.NextDouble() - 0.5f) * 0.8f * scale);
                AddMeshChild(root, Blob(r, 1, 0.3f, seed * 17 + c, new Vector3(0.9f, 1.15f, 0.9f)), leaves, offset);
            }

            var col = root.AddComponent<CapsuleCollider>();
            col.radius = trunkR * 1.6f;
            col.height = trunkH + scale;
            col.center = new Vector3(0, col.height * 0.5f, 0);
            return root;
        }

        public static GameObject Stump(int seed, Material bark, float scale)
        {
            var root = new GameObject("Stump");
            AddMeshChild(root, Cone(0.28f * scale, 0.45f * scale, 7, 0.24f * scale), bark, Vector3.zero);
            var col = root.AddComponent<CapsuleCollider>();
            col.radius = 0.28f * scale;
            col.height = 0.5f * scale;
            col.center = new Vector3(0, 0.25f * scale, 0);
            return root;
        }

        public static GameObject FallenLog(int seed, Material bark, float scale)
        {
            var rand = new System.Random(seed);
            var root = new GameObject("FallenLog");
            float len = (1.8f + (float)rand.NextDouble() * 1.6f) * scale;
            float r = 0.16f * scale;
            var log = AddMeshChild(root, Cone(r, len, 7, r * 0.85f), bark, Vector3.up * r);
            log.transform.localRotation = Quaternion.Euler(0f, 0f, 90f); // lay it down along X
            var col = root.AddComponent<CapsuleCollider>();
            col.direction = 0; // X axis
            col.radius = r;
            col.height = len;
            col.center = new Vector3(-len * 0.5f, r, 0);
            return root;
        }

        public static GameObject Fern(int seed, Material leaves, float scale)
        {
            var rand = new System.Random(seed);
            var root = new GameObject("Fern");
            int fronds = 5 + rand.Next(3);
            for (int i = 0; i < fronds; i++)
            {
                var frond = AddMeshChild(root, Cone(0.045f * scale, 0.7f * scale, 3), leaves, Vector3.zero);
                float yaw = i * (360f / fronds) + (float)rand.NextDouble() * 20f;
                frond.transform.localRotation = Quaternion.Euler(55f + (float)rand.NextDouble() * 15f, yaw, 0f);
            }
            return root;
        }

        public static GameObject GrassTuft(int seed, Material grass, float scale)
        {
            var rand = new System.Random(seed);
            var root = new GameObject("GrassTuft");
            int blades = 3 + rand.Next(3);
            for (int i = 0; i < blades; i++)
            {
                var blade = AddMeshChild(root, Cone(0.02f * scale, (0.25f + (float)rand.NextDouble() * 0.2f) * scale, 3),
                    grass, new Vector3(((float)rand.NextDouble() - 0.5f) * 0.15f * scale, 0,
                                       ((float)rand.NextDouble() - 0.5f) * 0.15f * scale));
                blade.transform.localRotation = Quaternion.Euler(
                    ((float)rand.NextDouble() - 0.5f) * 30f, (float)rand.NextDouble() * 360f, 0);
            }
            return root;
        }

        public static GameObject Flower(int seed, Material stem, Material petal, float scale)
        {
            var root = new GameObject("Flower");
            AddMeshChild(root, Cone(0.015f * scale, 0.3f * scale, 3), stem, Vector3.zero);
            AddMeshChild(root, Blob(0.06f * scale, 0, 0.15f, seed, Vector3.one), petal,
                new Vector3(0, 0.32f * scale, 0));
            return root;
        }

        public static GameObject AddMeshChild(GameObject parent, Mesh mesh, Material mat, Vector3 localPos)
        {
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }
    }
}
