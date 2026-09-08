using System.Collections.Generic;
using UnityEngine;

namespace Forage
{
    /// <summary>
    /// Natural-looking procedural props: smooth bent tree trunks with bark UVs,
    /// alpha-cutout foliage card canopies, layered pines, grass/fern cards and
    /// rounded rocks. Complements LowPolyFactory (still used for small items).
    /// </summary>
    public static class NatureFactory
    {
        static Mesh _quadCentered;
        static Mesh _quadBottom;

        /// <summary>1x1 quad, pivot at center, full UV.</summary>
        public static Mesh QuadCentered()
        {
            if (_quadCentered != null) return _quadCentered;
            _quadCentered = BuildQuad(-0.5f);
            return _quadCentered;
        }

        /// <summary>1x1 quad, pivot at bottom edge center.</summary>
        public static Mesh QuadBottom()
        {
            if (_quadBottom != null) return _quadBottom;
            _quadBottom = BuildQuad(0f);
            return _quadBottom;
        }

        static Mesh BuildQuad(float yMin)
        {
            var m = new Mesh { name = "FoliageQuad" };
            m.vertices = new[]
            {
                new Vector3(-0.5f, yMin, 0), new Vector3(0.5f, yMin, 0),
                new Vector3(-0.5f, yMin + 1f, 0), new Vector3(0.5f, yMin + 1f, 0)
            };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            m.RecalculateNormals();
            return m;
        }

        /// <summary>Smooth-shaded tapered tube with a gentle bend and organic ring jitter. Cylindrical UVs.</summary>
        public static Mesh SmoothTube(float bottomR, float topR, float height, int radialSegs, int heightSegs,
            float bend, int seed, float uvTilesV = 2f)
        {
            var rand = new System.Random(seed);
            float bendAngle = (float)rand.NextDouble() * Mathf.PI * 2f;
            var bendDir = new Vector3(Mathf.Cos(bendAngle), 0, Mathf.Sin(bendAngle));

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            for (int h = 0; h <= heightSegs; h++)
            {
                float t = (float)h / heightSegs;
                float r = Mathf.Lerp(bottomR, topR, t) * (0.96f + (float)rand.NextDouble() * 0.08f);
                Vector3 center = Vector3.up * (height * t) + bendDir * (bend * t * t);
                for (int s = 0; s <= radialSegs; s++)
                {
                    float a = s * Mathf.PI * 2f / radialSegs;
                    verts.Add(center + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r));
                    uvs.Add(new Vector2((float)s / radialSegs, t * uvTilesV));
                }
            }

            int ring = radialSegs + 1;
            for (int h = 0; h < heightSegs; h++)
                for (int s = 0; s < radialSegs; s++)
                {
                    int i = h * ring + s;
                    tris.Add(i); tris.Add(i + ring); tris.Add(i + 1);
                    tris.Add(i + 1); tris.Add(i + ring); tris.Add(i + ring + 1);
                }

            // top cap
            int top = verts.Count;
            verts.Add(Vector3.up * height + bendDir * bend);
            uvs.Add(new Vector2(0.5f, uvTilesV));
            for (int s = 0; s < radialSegs; s++)
            {
                int i = heightSegs * ring + s;
                tris.Add(i); tris.Add(top); tris.Add(i + 1);
            }

            var mesh = new Mesh { name = "SmoothTube" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Smooth-shaded jittered sphere with spherical UVs (rocks, mushroom caps).</summary>
        public static Mesh SmoothBlob(float radius, int subdivisions, float jitter, int seed, Vector3 scale)
        {
            var rand = new System.Random(seed);
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

            var uvs = new List<Vector2>(verts.Count);
            for (int i = 0; i < verts.Count; i++)
            {
                var n = verts[i];
                uvs.Add(new Vector2(Mathf.Atan2(n.z, n.x) / (2f * Mathf.PI) + 0.5f, n.y * 0.5f + 0.5f));
                float j = 1f + ((float)rand.NextDouble() * 2f - 1f) * jitter;
                verts[i] = Vector3.Scale(n * radius * j, scale);
            }

            var mesh = new Mesh { name = "SmoothBlob" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static GameObject Card(Transform parent, Mesh quad, Material mat, Vector3 localPos, Quaternion rot, Vector2 size)
        {
            var go = new GameObject("Card");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = rot;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        // ------------------------------------------------------------------
        // trees
        // ------------------------------------------------------------------

        public static GameObject BroadleafTree(int seed, Material bark, Material[] leafCards, float scale)
        {
            var rand = new System.Random(seed);
            var root = new GameObject("BroadleafTree");

            float trunkH = (3.0f + (float)rand.NextDouble() * 2.2f) * scale;
            float trunkR = (0.16f + (float)rand.NextDouble() * 0.08f) * scale;
            float bend = trunkH * (0.04f + (float)rand.NextDouble() * 0.1f);

            var trunk = new GameObject("Trunk");
            trunk.transform.SetParent(root.transform, false);
            trunk.AddComponent<MeshFilter>().sharedMesh =
                SmoothTube(trunkR, trunkR * 0.45f, trunkH, 7, 4, bend, seed, trunkH * 0.8f);
            trunk.AddComponent<MeshRenderer>().sharedMaterial = bark;

            // a couple of visible branch stubs reaching into the canopy
            int branches = 2 + rand.Next(2);
            for (int b = 0; b < branches; b++)
            {
                var branch = new GameObject("Branch");
                branch.transform.SetParent(root.transform, false);
                float bh = trunkH * (0.55f + 0.15f * b);
                branch.transform.localPosition = new Vector3(0, bh, 0);
                branch.transform.localRotation = Quaternion.Euler(
                    40f + (float)rand.NextDouble() * 30f, (float)rand.NextDouble() * 360f, 0);
                branch.AddComponent<MeshFilter>().sharedMesh =
                    SmoothTube(trunkR * 0.35f, trunkR * 0.12f, trunkH * 0.35f, 5, 2, 0.1f, seed + b, 1f);
                branch.AddComponent<MeshRenderer>().sharedMaterial = bark;
            }

            // canopy: overlapping foliage cards in an ellipsoid crown
            float crownR = (1.6f + (float)rand.NextDouble() * 0.9f) * scale;
            float crownY = trunkH * 0.92f;
            int cards = 9 + rand.Next(5);
            var quad = QuadCentered();
            for (int c = 0; c < cards; c++)
            {
                var dir = new Vector3(
                    ((float)rand.NextDouble() * 2f - 1f),
                    ((float)rand.NextDouble() * 2f - 1f) * 0.55f,
                    ((float)rand.NextDouble() * 2f - 1f));
                var pos = new Vector3(0, crownY, 0) + Vector3.Scale(dir, new Vector3(crownR, crownR * 0.6f, crownR));
                var rot = Quaternion.Euler(
                    ((float)rand.NextDouble() - 0.5f) * 50f,
                    (float)rand.NextDouble() * 360f,
                    ((float)rand.NextDouble() - 0.5f) * 50f);
                float s = (crownR * 1.5f) * (0.8f + (float)rand.NextDouble() * 0.6f);
                Card(root.transform, quad, leafCards[rand.Next(leafCards.Length)], pos, rot, new Vector2(s, s));
            }

            var col = root.AddComponent<CapsuleCollider>();
            col.radius = trunkR * 1.5f;
            col.height = trunkH;
            col.center = new Vector3(0, trunkH * 0.5f, 0);
            return root;
        }

        public static GameObject PineTree(int seed, Material bark, Material[] pineCards, float scale)
        {
            var rand = new System.Random(seed);
            var root = new GameObject("PineTree");

            float trunkH = (4.0f + (float)rand.NextDouble() * 2.5f) * scale;
            float trunkR = (0.13f + (float)rand.NextDouble() * 0.05f) * scale;

            var trunk = new GameObject("Trunk");
            trunk.transform.SetParent(root.transform, false);
            trunk.AddComponent<MeshFilter>().sharedMesh =
                SmoothTube(trunkR, trunkR * 0.25f, trunkH, 6, 3, trunkH * 0.02f, seed, trunkH * 0.8f);
            trunk.AddComponent<MeshRenderer>().sharedMaterial = bark;

            // tiers of drooping branch cards, wider at the bottom
            int tiers = 5 + rand.Next(3);
            var quad = QuadBottom();
            for (int t = 0; t < tiers; t++)
            {
                float tt = (float)t / (tiers - 1);
                float y = trunkH * (0.3f + 0.65f * tt);
                float reach = Mathf.Lerp(2.2f, 0.7f, tt) * scale;
                int cardsInTier = 3;
                for (int c = 0; c < cardsInTier; c++)
                {
                    float yaw = (c * 120f) + (float)rand.NextDouble() * 60f + t * 35f;
                    var rot = Quaternion.Euler(105f + (float)rand.NextDouble() * 10f, yaw, 0);
                    Card(root.transform, quad, pineCards[rand.Next(pineCards.Length)],
                        new Vector3(0, y, 0), rot, new Vector2(reach * 1.2f, reach));
                }
            }
            // leader tip
            Card(root.transform, quad, pineCards[0], new Vector3(0, trunkH * 0.96f, 0),
                Quaternion.Euler(10f, (float)rand.NextDouble() * 360f, 0), new Vector2(0.7f * scale, 1.0f * scale));

            var col = root.AddComponent<CapsuleCollider>();
            col.radius = trunkR * 1.6f;
            col.height = trunkH;
            col.center = new Vector3(0, trunkH * 0.5f, 0);
            return root;
        }

        public static GameObject BirchTree(int seed, Material birchBark, Material[] leafCards, float scale)
        {
            var rand = new System.Random(seed);
            var root = new GameObject("BirchTree");

            float trunkH = (3.5f + (float)rand.NextDouble() * 2f) * scale;
            float trunkR = 0.09f * scale;

            var trunk = new GameObject("Trunk");
            trunk.transform.SetParent(root.transform, false);
            trunk.AddComponent<MeshFilter>().sharedMesh =
                SmoothTube(trunkR, trunkR * 0.4f, trunkH, 6, 4, trunkH * 0.08f, seed, trunkH);
            trunk.AddComponent<MeshRenderer>().sharedMaterial = birchBark;

            float crownR = (1.0f + (float)rand.NextDouble() * 0.5f) * scale;
            int cards = 6 + rand.Next(4);
            var quad = QuadCentered();
            for (int c = 0; c < cards; c++)
            {
                var pos = new Vector3(
                    ((float)rand.NextDouble() * 2f - 1f) * crownR * 0.8f,
                    trunkH * (0.75f + (float)rand.NextDouble() * 0.3f),
                    ((float)rand.NextDouble() * 2f - 1f) * crownR * 0.8f);
                var rot = Quaternion.Euler(
                    ((float)rand.NextDouble() - 0.5f) * 60f,
                    (float)rand.NextDouble() * 360f, 0);
                float s = crownR * (1.1f + (float)rand.NextDouble() * 0.7f);
                Card(root.transform, quad, leafCards[rand.Next(leafCards.Length)], pos, rot, new Vector2(s, s));
            }

            var col = root.AddComponent<CapsuleCollider>();
            col.radius = trunkR * 1.8f;
            col.height = trunkH;
            col.center = new Vector3(0, trunkH * 0.5f, 0);
            return root;
        }

        // ------------------------------------------------------------------
        // undergrowth & rocks
        // ------------------------------------------------------------------

        public static GameObject GrassPatch(int seed, Material grassCard, float scale)
        {
            var rand = new System.Random(seed);
            var root = new GameObject("GrassPatch");
            var quad = QuadBottom();
            int cards = 2 + rand.Next(2);
            for (int c = 0; c < cards; c++)
            {
                var rot = Quaternion.Euler(0, c * (180f / cards) + (float)rand.NextDouble() * 30f, 0);
                float s = (0.5f + (float)rand.NextDouble() * 0.4f) * scale;
                Card(root.transform, quad, grassCard, Vector3.zero, rot, new Vector2(s * 1.4f, s));
            }
            return root;
        }

        public static GameObject FernPlant(int seed, Material fernCard, float scale)
        {
            var rand = new System.Random(seed);
            var root = new GameObject("Fern");
            var quad = QuadBottom();
            int fronds = 3;
            for (int c = 0; c < fronds; c++)
            {
                var rot = Quaternion.Euler(-25f - (float)rand.NextDouble() * 15f,
                    c * 120f + (float)rand.NextDouble() * 40f, 0);
                float s = (0.7f + (float)rand.NextDouble() * 0.5f) * scale;
                Card(root.transform, quad, fernCard, Vector3.up * 0.02f, rot, new Vector2(s, s * 0.8f));
            }
            return root;
        }

        public static GameObject Rock(int seed, Material rockMat, float scale)
        {
            var rand = new System.Random(seed);
            var root = new GameObject("Rock");
            var squash = new Vector3(1f, 0.5f + (float)rand.NextDouble() * 0.35f, 0.75f + (float)rand.NextDouble() * 0.4f);
            var go = new GameObject("RockMesh");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = Vector3.down * scale * 0.15f; // sit into the ground
            go.AddComponent<MeshFilter>().sharedMesh = SmoothBlob(scale, 2, 0.22f, seed, squash);
            go.AddComponent<MeshRenderer>().sharedMaterial = rockMat;
            var col = root.AddComponent<SphereCollider>();
            col.radius = scale * 0.75f;
            return root;
        }

        public static GameObject Bush(int seed, Material[] leafCards, float scale)
        {
            var rand = new System.Random(seed);
            var root = new GameObject("Bush");
            var quad = QuadCentered();
            int cards = 4 + rand.Next(3);
            for (int c = 0; c < cards; c++)
            {
                var pos = new Vector3(
                    ((float)rand.NextDouble() - 0.5f) * scale * 0.7f,
                    scale * (0.3f + (float)rand.NextDouble() * 0.25f),
                    ((float)rand.NextDouble() - 0.5f) * scale * 0.7f);
                var rot = Quaternion.Euler(((float)rand.NextDouble() - 0.5f) * 40f,
                    (float)rand.NextDouble() * 360f, 0);
                float s = scale * (0.7f + (float)rand.NextDouble() * 0.5f);
                Card(root.transform, quad, leafCards[rand.Next(leafCards.Length)], pos, rot, new Vector2(s, s * 0.8f));
            }
            return root;
        }
    }
}
