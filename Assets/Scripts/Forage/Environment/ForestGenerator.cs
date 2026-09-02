using System.Collections.Generic;
using UnityEngine;

namespace Forage
{
    /// <summary>
    /// Generates the forest at runtime from a fixed seed: smooth rolling
    /// terrain with a baked albedo texture, a camp clearing and a pond, and
    /// dense clustered vegetation (broadleaf/pine/birch trees with foliage
    /// cards, bushes, ferns, grass, flowers, rocks, stumps, fallen logs).
    /// Deterministic, so editor tests and the Quest build match.
    /// </summary>
    public class ForestGenerator : MonoBehaviour
    {
        [Header("Seed & size")]
        public int seed = 20260902;
        public float worldSize = 180f;
        public int gridResolution = 110;

        [Header("Camp clearing")]
        public float campRadius = 9f;

        [Header("Pond")]
        public Vector2 pondCenter = new Vector2(24f, 16f);
        public float pondRadius = 8f;
        public float pondDepth = 1.4f;

        [Header("Scatter attempts (yield depends on density mask)")]
        public int treeAttempts = 1000;
        public int rockAttempts = 60;
        public int bushAttempts = 150;
        public int fernAttempts = 320;
        public int grassAttempts = 900;
        public int flowerAttempts = 90;
        public int stumpAttempts = 22;
        public int logAttempts = 26;

        [Header("Materials (assigned by scene builder)")]
        public Material terrainMat;
        public Material barkMat;
        public Material birchBarkMat;
        public Material[] broadleafCards;
        public Material[] pineCards;
        public Material[] birchCards;
        public Material grassCard;
        public Material fernCard;
        public Material rockMat;
        public Material flowerStemMat;
        public Material flowerPetalMat;
        public Material waterMat;
        public Material fishMat;
        public Material crabMat;

        static ForestGenerator _instance;
        /// <summary>Survives mid-play domain reloads (recompile while playing) by re-finding itself.</summary>
        public static ForestGenerator Instance
        {
            get
            {
                if (_instance == null) _instance = FindFirstObjectByType<ForestGenerator>();
                return _instance;
            }
        }

        Transform _propsRoot;

        void Awake()
        {
            _instance = this;
            Generate();
        }

        // ---- static field functions so the editor scene-builder can bake matching textures ----

        public static float HeightField(int seed, float x, float z, float campRadius, Vector2 pondCenter,
            float pondRadius, float pondDepth)
        {
            float ox = seed * 0.137f % 1000f;
            float h = Mathf.PerlinNoise(x * 0.014f + ox, z * 0.014f + ox) * 3.2f
                    + Mathf.PerlinNoise(x * 0.055f + ox, z * 0.055f + ox) * 0.8f
                    + Mathf.PerlinNoise(x * 0.006f + ox * 2f, z * 0.006f + ox * 2f) * 2f;

            float campT = Mathf.Clamp01(new Vector2(x, z).magnitude / campRadius);
            h *= Mathf.SmoothStep(0f, 1f, campT);

            float pondT = Mathf.Clamp01(Vector2.Distance(new Vector2(x, z), pondCenter) / pondRadius);
            h -= (1f - Mathf.SmoothStep(0f, 1f, pondT)) * (pondDepth + 1.5f);
            return h;
        }

        public static float DensityField(int seed, float x, float z)
        {
            float ox = seed * 0.271f % 1000f;
            float d = Mathf.PerlinNoise(x * 0.02f + ox, z * 0.02f + ox);
            return Mathf.Clamp01(d * 1.25f - 0.1f);
        }

        public float HeightAt(float x, float z) => HeightField(seed, x, z, campRadius, pondCenter, pondRadius, pondDepth);
        public float DensityAt(float x, float z) => DensityField(seed, x, z);

        public bool IsInPond(Vector3 worldPos) =>
            Vector2.Distance(new Vector2(worldPos.x, worldPos.z), pondCenter) < pondRadius;

        public void Generate()
        {
            if (_propsRoot != null) Destroy(_propsRoot.gameObject);
            _propsRoot = new GameObject("GeneratedForest").transform;
            _propsRoot.SetParent(transform, false);

            BuildTerrain();
            BuildPondWater();
            ScatterProps();

            StaticBatchingUtility.Combine(_propsRoot.gameObject);
        }

        void BuildTerrain()
        {
            int res = gridResolution;
            float half = worldSize * 0.5f;
            float step = worldSize / res;

            var verts = new List<Vector3>((res + 1) * (res + 1));
            var uvs = new List<Vector2>((res + 1) * (res + 1));
            for (int z = 0; z <= res; z++)
                for (int x = 0; x <= res; x++)
                {
                    float wx = -half + x * step;
                    float wz = -half + z * step;
                    verts.Add(new Vector3(wx, HeightAt(wx, wz), wz));
                    uvs.Add(new Vector2((float)x / res, (float)z / res));
                }

            var tris = new List<int>(res * res * 6);
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    int i = z * (res + 1) + x;
                    tris.Add(i); tris.Add(i + res + 1); tris.Add(i + 1);
                    tris.Add(i + 1); tris.Add(i + res + 1); tris.Add(i + res + 2);
                }

            // smooth-shaded terrain with a world-mapped albedo texture
            var mesh = new Mesh { name = "Terrain" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Terrain");
            go.transform.SetParent(_propsRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = terrainMat;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.isStatic = true;

            var teleportArea = go.AddComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea>();
            var teleportMask = UnityEngine.XR.Interaction.Toolkit.InteractionLayerMask.GetMask("Teleport");
            if (teleportMask != 0)
                teleportArea.interactionLayers = teleportMask;
        }

        void BuildPondWater()
        {
            var mesh = LowPolyFactory.Cone(pondRadius * 0.92f, 0.02f, 24);
            var go = LowPolyFactory.AddMeshChild(_propsRoot.gameObject, mesh, waterMat,
                new Vector3(pondCenter.x, -0.45f, pondCenter.y));
            go.name = "PondWater";
            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = pondRadius * 0.9f;
            col.center = Vector3.up * 0.3f;
            go.tag = "Water";

            // life under the surface, visible through the transparent water
            var life = new GameObject("PondLife").AddComponent<PondLife>();
            life.transform.SetParent(_propsRoot, false);
            life.fishMat = fishMat;
            life.crabMat = crabMat;
            life.pondCenter = pondCenter;
            life.pondRadius = pondRadius;
        }

        void ScatterProps()
        {
            var rand = new System.Random(seed);
            float half = worldSize * 0.5f - 4f;

            Vector3? Spot(float minCenterDist, float minPondMargin)
            {
                for (int tries = 0; tries < 12; tries++)
                {
                    float x = ((float)rand.NextDouble() * 2f - 1f) * half;
                    float z = ((float)rand.NextDouble() * 2f - 1f) * half;
                    var p2 = new Vector2(x, z);
                    if (p2.magnitude < minCenterDist) continue;
                    if (Vector2.Distance(p2, pondCenter) < pondRadius + minPondMargin) continue;
                    return new Vector3(x, HeightAt(x, z), z);
                }
                return null;
            }

            // trees follow the density mask -> dense groves with meadow gaps
            for (int i = 0; i < treeAttempts; i++)
            {
                var pos = Spot(campRadius + 1.5f, 1.8f);
                if (pos == null) continue;
                float density = DensityAt(pos.Value.x, pos.Value.z);
                if ((float)rand.NextDouble() > density * density * 2.0f) continue;

                float scale = 0.85f + (float)rand.NextDouble() * 0.8f;
                GameObject tree;
                double species = rand.NextDouble();
                if (species < 0.42)
                    tree = NatureFactory.BroadleafTree(seed + i, barkMat, broadleafCards, scale);
                else if (species < 0.78)
                    tree = NatureFactory.PineTree(seed + i, barkMat, pineCards, scale);
                else
                    tree = NatureFactory.BirchTree(seed + i, birchBarkMat, birchCards, scale);
                // sink slightly so trunks never float on slopes
                Place(tree, pos.Value + Vector3.down * 0.35f, rand);
            }

            void ScatterSimple(int attempts, float minCenter, float pondMargin,
                System.Func<int, GameObject> make, bool followDensity = false, bool invertDensity = false)
            {
                for (int i = 0; i < attempts; i++)
                {
                    var pos = Spot(minCenter, pondMargin);
                    if (pos == null) continue;
                    if (followDensity)
                    {
                        float d = DensityAt(pos.Value.x, pos.Value.z);
                        if (invertDensity) d = 1f - d;
                        if ((float)rand.NextDouble() > d * 1.3f) continue;
                    }
                    Place(make(i), pos.Value, rand);
                }
            }

            ScatterSimple(rockAttempts, campRadius * 0.7f, 0.5f,
                i => NatureFactory.Rock(seed + 5000 + i, rockMat, 0.35f + (float)rand.NextDouble() * 0.9f));
            ScatterSimple(bushAttempts, campRadius * 0.8f, 0.8f,
                i => NatureFactory.Bush(seed + 9000 + i, broadleafCards, 0.6f + (float)rand.NextDouble() * 0.7f));
            ScatterSimple(fernAttempts, campRadius * 0.8f, 0.6f,
                i => NatureFactory.FernPlant(seed + 11000 + i, fernCard, 0.7f + (float)rand.NextDouble() * 0.7f),
                followDensity: true);
            ScatterSimple(grassAttempts, campRadius * 0.35f, 0.4f,
                i => NatureFactory.GrassPatch(seed + 13000 + i, grassCard, 0.8f + (float)rand.NextDouble() * 0.8f));
            ScatterSimple(flowerAttempts, campRadius * 0.5f, 0.5f,
                i => LowPolyFactory.Flower(seed + 15000 + i, flowerStemMat, flowerPetalMat,
                    0.8f + (float)rand.NextDouble() * 0.6f),
                followDensity: true, invertDensity: true);
            ScatterSimple(stumpAttempts, campRadius + 1f, 1f, i => Stump(seed + 17000 + i, rand));
            ScatterSimple(logAttempts, campRadius + 1f, 1f, i => FallenLog(seed + 19000 + i, rand));
        }

        GameObject Stump(int s, System.Random rand)
        {
            var root = new GameObject("Stump");
            float sc = 0.8f + (float)rand.NextDouble() * 0.5f;
            var go = new GameObject("StumpMesh");
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothTube(0.3f * sc, 0.26f * sc, 0.45f * sc, 7, 2, 0.02f, s, 1f);
            go.AddComponent<MeshRenderer>().sharedMaterial = barkMat;
            var col = root.AddComponent<CapsuleCollider>();
            col.radius = 0.3f * sc;
            col.height = 0.5f * sc;
            col.center = new Vector3(0, 0.25f * sc, 0);
            return root;
        }

        GameObject FallenLog(int s, System.Random rand)
        {
            var root = new GameObject("FallenLog");
            float sc = 0.8f + (float)rand.NextDouble() * 0.6f;
            float len = (2.0f + (float)rand.NextDouble() * 1.6f) * sc;
            float r = 0.17f * sc;
            var go = new GameObject("LogMesh");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = Vector3.up * r * 0.8f;
            go.transform.localRotation = Quaternion.Euler(0, 0, 90f);
            go.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothTube(r, r * 0.85f, len, 7, 3, 0.05f, s, len);
            go.AddComponent<MeshRenderer>().sharedMaterial = barkMat;
            var col = root.AddComponent<CapsuleCollider>();
            col.direction = 0;
            col.radius = r;
            col.height = len;
            col.center = new Vector3(-len * 0.5f, r * 0.8f, 0);
            return root;
        }

        void Place(GameObject go, Vector3 pos, System.Random rand)
        {
            go.transform.SetParent(_propsRoot, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0, (float)rand.NextDouble() * 360f, 0);
            go.isStatic = true;
            foreach (var child in go.GetComponentsInChildren<Transform>(true))
                child.gameObject.isStatic = true;
        }
    }
}
