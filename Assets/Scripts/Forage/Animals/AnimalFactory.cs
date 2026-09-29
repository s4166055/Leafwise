using System.Collections.Generic;
using UnityEngine;

namespace Forage
{
    /// <summary>Procedural animal bodies (rabbit, snake). Squirrel uses the Furry Squirrel FBX.</summary>
    public static class AnimalFactory
    {
        // One keyed cache for every generated material - fur coats, snake skins,
        // the eye. A single owner means a single lifetime: a fake-null (destroyed)
        // entry is simply regenerated, and the cache is cleared explicitly on
        // subsystem registration so it is correct whether or not Domain Reload
        // is enabled in the editor.
        static readonly Dictionary<string, Material> _cache = new Dictionary<string, Material>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() => _cache.Clear();

        static Material Cached(string key, System.Func<Material> make)
        {
            if (_cache.TryGetValue(key, out var m) && m != null) return m;
            m = make();
            _cache[key] = m;
            return m;
        }

        static Material Lit() => new Material(Shader.Find("Universal Render Pipeline/Lit"));

        /// <summary>
        /// A wet, glossy eye. Flat matte spheres are one of the biggest reasons
        /// a procedural animal reads as a toy, so eyes get a real specular
        /// highlight instead of the default plastic response.
        /// </summary>
        public static Material EyeMaterial(Color col)
        {
            var mat = Lit();
            mat.SetColor("_BaseColor", col);
            mat.SetFloat("_Smoothness", 0.95f);
            return mat;
        }

        /// <summary>The shared dark eye every species uses.</summary>
        public static Material Eye => Cached("eye", () => EyeMaterial(new Color(0.05f, 0.04f, 0.035f)));

        /// <summary>
        /// Procedural fur: an albedo of directional strands over a darker
        /// undercoat, plus a matching normal map derived from the same strand
        /// height field.
        ///
        /// The normal map is the part that matters. Every mammal here used to be
        /// a flat <c>_BaseColor</c> on a smooth blob, which is exactly why they
        /// read as plasticine beside the textured squirrel: one evenly lit
        /// surface with no high-frequency detail gives the eye nothing to
        /// resolve. Real per-pixel relief makes the same silhouette look like an
        /// animal, and costs only one extra texture fetch on device.
        ///
        /// Strands are stretched along V because SmoothBlob lays out spherical
        /// UVs (U wraps around the body, V runs pole to pole), so the fur lies
        /// along the animal instead of swirling around it.
        ///
        /// Bakes happen on the main thread at spawn, so they are kept cheap:
        /// 128 px by default (fur tiles anyway), Color32 staging, and the CPU
        /// copy is released after upload. All ten species coats together cost
        /// well under a frame on Quest hardware.
        ///
        /// NOTE: <c>_NORMALMAP</c> is a shader_feature. Materials created at
        /// runtime are invisible to URP's build-time variant collection, so the
        /// scene must reference at least one material asset with the keyword
        /// enabled or the variant is stripped from the APK and this silently
        /// renders flat on device. ForageSceneBuilder assigns that anchor
        /// material to AnimalManager.shaderVariantAnchor.
        /// </summary>
        public static Material FurMaterial(string key, Color baseCol, Color underCol, int seed,
                                           float strandDensity = 52f, float smoothness = 0.18f, int size = 128)
        {
            return Cached(key, () =>
            {
                float ox = seed * 7.31f % 500f;

                // shared height field: fine strands across U, stretched along V.
                // Weighted toward the finer octaves - a heavy low-frequency clump
                // term made the coat look lumpy, like wet clay, rather than hair.
                var height = new float[size, size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u = (float)x / size, v = (float)y / size;
                        float strand = Mathf.PerlinNoise(u * strandDensity + ox, v * strandDensity * 0.15f + ox);
                        float fine = Mathf.PerlinNoise(u * strandDensity * 3.2f + ox * 2f, v * strandDensity * 0.5f);
                        float finer = Mathf.PerlinNoise(u * strandDensity * 6.5f + ox * 4f, v * strandDensity * 0.9f);
                        float clump = Mathf.PerlinNoise(u * 5f + ox * 3f, v * 4f + ox);
                        height[x, y] = Mathf.Clamp01(strand * 0.44f + fine * 0.30f + finer * 0.14f + clump * 0.16f);
                    }

                var albedo = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float h = height[x, y];
                        // tips catch the light, roots stay in the undercoat
                        var c = Color.Lerp(underCol, baseCol, Mathf.SmoothStep(0.15f, 0.85f, h));
                        c *= 0.9f + h * 0.22f;
                        c.a = 1f;
                        px[y * size + x] = c;
                    }
                albedo.SetPixels32(px);
                albedo.Apply(true, true);

                // normal map from the same field (sobel); linear so it is not gamma-corrected
                var normal = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Repeat };
                var npx = new Color32[size * size];
                const float relief = 3.2f;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        int xm = (x - 1 + size) % size, xp = (x + 1) % size;
                        int ym = (y - 1 + size) % size, yp = (y + 1) % size;
                        float dx = (height[xp, y] - height[xm, y]) * relief;
                        float dy = (height[x, yp] - height[x, ym]) * relief;
                        var n = new Vector3(-dx, -dy, 1f).normalized;
                        npx[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                    }
                normal.SetPixels32(npx);
                normal.Apply(true, true);

                var mat = Lit();
                mat.SetTexture("_BaseMap", albedo);
                mat.SetColor("_BaseColor", Color.white);
                mat.SetTexture("_BumpMap", normal);
                mat.SetFloat("_BumpScale", 1.0f);
                mat.EnableKeyword("_NORMALMAP");
                mat.SetFloat("_Smoothness", smoothness);   // fur is matte, not plastic
                mat.SetFloat("_Metallic", 0f);
                return mat;
            });
        }

        /// <summary>
        /// A jittered sphere part. <paramref name="subdivisions"/> defaults to 1
        /// (32 tris): eyes, noses and feet cover a few pixels at VR distance and
        /// gain nothing from more. Pass 2 for the large body masses whose
        /// silhouette the player actually reads.
        /// </summary>
        public static GameObject Blob(Transform parent, string name, float r, Vector3 squash, Material mat,
                                      Vector3 pos, int seed, int subdivisions = 1)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothBlob(r, subdivisions, 0.06f, seed, squash);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>
        /// Rabbit: crouched hare silhouette — long haunches, tucked forelegs,
        /// rounded rump, tapered muzzle, long ears with pink inner lining and a
        /// white scut. Returns the visual root.
        /// </summary>
        public static Transform RabbitBody(Transform parent, int seed)
        {
            var fur = FurMaterial("fur-mid", new Color(0.5f, 0.4f, 0.3f), new Color(0.3f, 0.23f, 0.16f), 11);
            var furLight = FurMaterial("fur-light", new Color(0.8f, 0.74f, 0.64f), new Color(0.56f, 0.5f, 0.42f), 12);
            var innerEar = Cached("rabbit-inner-ear", () => { var m = Lit(); m.SetColor("_BaseColor", new Color(0.85f, 0.6f, 0.6f)); return m; });
            var nose = Cached("rabbit-nose", () => { var m = Lit(); m.SetColor("_BaseColor", new Color(0.72f, 0.45f, 0.45f)); return m; });

            var root = new GameObject("Body").transform;
            root.SetParent(parent, false);

            // haunches highest, chest lower and forward — the classic crouch
            Blob(root, "Rump", 0.135f, new Vector3(0.95f, 0.95f, 1.0f), fur, new Vector3(0, 0.155f, -0.075f), seed, 2);
            Blob(root, "Chest", 0.105f, new Vector3(0.9f, 0.85f, 1.15f), fur, new Vector3(0, 0.115f, 0.09f), seed + 1, 2);
            Blob(root, "Belly", 0.085f, new Vector3(0.8f, 0.55f, 1.3f), furLight, new Vector3(0, 0.075f, 0.01f), seed + 2, 2);

            // head sits forward and low, with a tapered muzzle
            var head = Blob(root, "Head", 0.072f, new Vector3(0.95f, 0.95f, 1.1f), fur, new Vector3(0, 0.215f, 0.185f), seed + 3, 2);
            Blob(head.transform, "Muzzle", 0.042f, new Vector3(0.85f, 0.75f, 1.25f), fur, new Vector3(0, -0.022f, 0.055f), seed + 4);
            Blob(head.transform, "Nose", 0.014f, new Vector3(1f, 0.8f, 1f), nose, new Vector3(0, -0.022f, 0.093f), seed + 5);

            // powerful hind legs folded alongside the body
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                Blob(root, "Haunch", 0.072f, new Vector3(0.55f, 0.95f, 1.25f), fur,
                    new Vector3(side * 0.085f, 0.115f, -0.06f), seed + 6 + i, 2);
                var foot = Blob(root, "HindFoot", 0.032f, new Vector3(0.7f, 0.5f, 2.1f), fur,
                    new Vector3(side * 0.075f, 0.03f, -0.015f), seed + 8 + i);
                foot.transform.localRotation = Quaternion.Euler(0, side * 4f, 0);

                // tucked forelegs
                Blob(root, "Foreleg", 0.026f, new Vector3(0.8f, 1.5f, 0.9f), fur,
                    new Vector3(side * 0.05f, 0.055f, 0.13f), seed + 10 + i);

                // ears: long, swept back, set well apart with a pink inner face
                var ear = new GameObject("Ear");
                ear.transform.SetParent(head.transform, false);
                ear.transform.localPosition = new Vector3(side * 0.052f, 0.05f, -0.022f);
                ear.transform.localRotation = Quaternion.Euler(-16f, side * 10f, side * 24f);
                ear.AddComponent<MeshFilter>().sharedMesh = LowPolyFactory.Cone(0.026f, 0.155f, 7, 0.011f);
                ear.AddComponent<MeshRenderer>().sharedMaterial = fur;
                var inner = new GameObject("EarInner");
                inner.transform.SetParent(ear.transform, false);
                inner.transform.localPosition = new Vector3(0, 0.005f, 0.011f);
                inner.transform.localScale = new Vector3(0.62f, 0.92f, 0.62f);
                inner.AddComponent<MeshFilter>().sharedMesh = LowPolyFactory.Cone(0.026f, 0.155f, 7, 0.011f);
                inner.AddComponent<MeshRenderer>().sharedMaterial = innerEar;

                // eyes on the sides of the skull, as prey animals have
                Blob(head.transform, "Eye", 0.0135f, Vector3.one, Eye,
                    new Vector3(side * 0.056f, 0.012f, 0.026f), seed + 12 + i);
            }

            // white scut
            Blob(root, "Tail", 0.043f, new Vector3(1f, 0.9f, 0.85f), furLight, new Vector3(0, 0.175f, -0.185f), seed + 14);
            return root;
        }

        public static float SegmentSpacing(float scale) => 0.075f * scale;

        /// <summary>
        /// One skin per species, not per snake. Zone snakes are destroyed and
        /// respawned as the player wanders, and Destroy() on the GameObject does
        /// not free a code-created Texture2D - so an uncached bake here leaked
        /// ~350 KB and a main-thread hitch every time a snake materialised,
        /// which is precisely the moment the freeze encounter begins.
        /// </summary>
        public static Material SnakeSkinFor(SnakeSpecies species) =>
            Cached("snake-" + species.name, () =>
                SnakeSkin(species.baseColor, species.bandColor, species.name.GetHashCode() & 0xFFFF));

        /// <summary>Snake skin: overlapping scale pattern + species banding, generated at runtime.</summary>
        public static Material SnakeSkin(Color baseCol, Color bandCol, int seed, int size = 128)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size, v = (float)y / size;

                    // banding along the body (v axis wraps the length)
                    float band = Mathf.PerlinNoise(u * 1.5f + seed, v * 10f) > 0.52f ? 1f : 0f;
                    var c = Color.Lerp(baseCol, bandCol, band * 0.8f);

                    // overlapping scales: offset diamond grid with a bright rim on each scale
                    float row = v * 26f;
                    float col = u * 13f + (Mathf.Floor(row) % 2f) * 0.5f;
                    float fx = Mathf.Abs(col - Mathf.Floor(col) - 0.5f) * 2f;
                    float fy = Mathf.Abs(row - Mathf.Floor(row) - 0.5f) * 2f;
                    float diamond = fx + fy; // 0 center .. 2 corner
                    float rim = Mathf.SmoothStep(0.75f, 1.0f, diamond);   // bright edge
                    float shade = Mathf.SmoothStep(1.0f, 1.6f, diamond);  // dark groove
                    c *= 0.9f + rim * 0.25f - shade * 0.35f;

                    // subtle organic mottle
                    c *= 0.92f + Mathf.PerlinNoise(u * 24f + seed * 2, v * 24f) * 0.16f;
                    c.a = 1f;
                    px[y * size + x] = c;
                }
            tex.SetPixels32(px);
            tex.Apply(true, true);

            var mat = Lit();
            mat.SetTexture("_BaseMap", tex);
            mat.SetFloat("_Smoothness", 0.5f); // scaly sheen
            return mat;
        }

        /// <summary>
        /// Snake body: an invisible spine of transforms driven by the Snake
        /// script, rendered as ONE continuous scaled tube (SnakeTube). The
        /// head is a flattened wedge with eyes and a flicking tongue, blended
        /// into the tube at the neck.
        /// </summary>
        public static Transform[] SnakeBody(Transform parent, int seed, float scale, Material skin, int segments = 16)
        {
            var list = new Transform[segments];
            for (int i = 0; i < segments; i++)
            {
                // spine points only — no per-segment renderers
                var seg = new GameObject("Spine" + i);
                seg.transform.SetParent(parent, false);
                seg.transform.localPosition = new Vector3(0, 0.05f * scale, -i * SegmentSpacing(scale));
                list[i] = seg.transform;
            }

            // head: flattened wedge that caps the tube
            var head = new GameObject("HeadMesh");
            head.transform.SetParent(list[0], false);
            head.transform.localPosition = new Vector3(0, 0, 0.03f * scale);
            head.AddComponent<MeshFilter>().sharedMesh =
                NatureFactory.SmoothBlob(0.062f * scale, 1, 0.05f, seed, new Vector3(1.15f, 0.62f, 1.6f));
            head.AddComponent<MeshRenderer>().sharedMaterial = skin;

            float er = 0.012f * scale;
            Blob(head.transform, "EyeL", er, Vector3.one, Eye, new Vector3(-0.036f * scale, 0.022f * scale, 0.045f * scale), seed + 90);
            Blob(head.transform, "EyeR", er, Vector3.one, Eye, new Vector3(0.036f * scale, 0.022f * scale, 0.045f * scale), seed + 91);

            var tongueMat = Cached("snake-tongue", () => { var m = Lit(); m.SetColor("_BaseColor", new Color(0.75f, 0.1f, 0.12f)); return m; });
            var tongue = new GameObject("Tongue");
            tongue.transform.SetParent(head.transform, false);
            tongue.transform.localPosition = new Vector3(0, 0, 0.1f * scale);
            tongue.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            tongue.AddComponent<MeshFilter>().sharedMesh = LowPolyFactory.Cone(0.006f * scale, 0.09f * scale, 4);
            tongue.AddComponent<MeshRenderer>().sharedMaterial = tongueMat;

            // the continuous body tube
            var tubeGo = new GameObject("BodyTube");
            tubeGo.transform.SetParent(parent, false);
            tubeGo.AddComponent<SnakeTube>().Init(list, scale, skin);
            return list;
        }
    }
}
