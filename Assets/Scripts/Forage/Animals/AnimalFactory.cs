using UnityEngine;

namespace Forage
{
    /// <summary>Procedural animal bodies (rabbit, snake). Squirrel uses the Furry Squirrel FBX.</summary>
    public static class AnimalFactory
    {
        static Material _fur;
        static Material _furLight;
        static Material _snakeSkin;
        static Material _eye;

        static void EnsureMats()
        {
            if (_fur != null) return;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            _fur = new Material(lit); _fur.SetColor("_BaseColor", new Color(0.5f, 0.4f, 0.3f));
            _furLight = new Material(lit); _furLight.SetColor("_BaseColor", new Color(0.75f, 0.68f, 0.58f));
            _snakeSkin = new Material(lit); _snakeSkin.SetColor("_BaseColor", new Color(0.35f, 0.42f, 0.2f));
            _eye = new Material(lit); _eye.SetColor("_BaseColor", new Color(0.08f, 0.06f, 0.05f));
        }

        static GameObject Blob(Transform parent, string name, float r, Vector3 squash, Material mat, Vector3 pos, int seed)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothBlob(r, 1, 0.06f, seed, squash);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>Rabbit visual: body + head + ears + tail + feet. Returns the visual root.</summary>
        public static Transform RabbitBody(Transform parent, int seed)
        {
            EnsureMats();
            var root = new GameObject("Body").transform;
            root.SetParent(parent, false);

            Blob(root, "Torso", 0.16f, new Vector3(0.8f, 0.85f, 1.25f), _fur, new Vector3(0, 0.17f, 0), seed);
            Blob(root, "Head", 0.09f, Vector3.one, _fur, new Vector3(0, 0.3f, 0.16f), seed + 1);
            Blob(root, "Tail", 0.05f, Vector3.one, _furLight, new Vector3(0, 0.2f, -0.19f), seed + 2);
            Blob(root, "Belly", 0.1f, new Vector3(0.75f, 0.6f, 1f), _furLight, new Vector3(0, 0.1f, 0.02f), seed + 3);

            for (int i = 0; i < 2; i++)
            {
                var ear = new GameObject("Ear");
                ear.transform.SetParent(root, false);
                ear.transform.localPosition = new Vector3(i == 0 ? -0.035f : 0.035f, 0.38f, 0.13f);
                ear.transform.localRotation = Quaternion.Euler(-12f, 0, i == 0 ? -8f : 8f);
                ear.AddComponent<MeshFilter>().sharedMesh = LowPolyFactory.Cone(0.022f, 0.14f, 6, 0.012f);
                ear.AddComponent<MeshRenderer>().sharedMaterial = _fur;

                Blob(root, "Eye", 0.014f, Vector3.one, _eye,
                    new Vector3(i == 0 ? -0.05f : 0.05f, 0.315f, 0.225f), seed + 4 + i);
            }
            return root;
        }

        public static float SegmentSpacing(float scale) => 0.075f * scale;

        /// <summary>Banded snake-skin texture generated at runtime.</summary>
        public static Material SnakeSkin(Color baseCol, Color bandCol, int seed)
        {
            int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, true);
            var rand = new System.Random(seed);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size, v = (float)y / size;
                    // diagonal bands + scale speckle
                    float band = Mathf.PerlinNoise(u * 2f + seed, v * 14f) > 0.5f ? 1f : 0f;
                    float speckle = Mathf.PerlinNoise(u * 30f + seed * 2, v * 30f) * 0.25f;
                    var c = Color.Lerp(baseCol, bandCol, band * 0.8f);
                    c *= 0.85f + speckle;
                    px[y * size + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply(true);

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetTexture("_BaseMap", tex);
            mat.SetFloat("_Smoothness", 0.45f); // scaly sheen
            return mat;
        }

        /// <summary>
        /// Snake body: tapered segment chain with banded skin, a broad flat head
        /// with eyes and a flicking tongue. Returns segments; tongue is child
        /// "Tongue" of segment 0.
        /// </summary>
        public static Transform[] SnakeBody(Transform parent, int seed, float scale, Material skin, int segments = 16)
        {
            EnsureMats();
            var list = new Transform[segments];
            for (int i = 0; i < segments; i++)
            {
                float t = (float)i / (segments - 1);
                // neck dips then body swells then tapers to a fine tail
                float profile = i == 0 ? 0.95f : Mathf.Sin(Mathf.PI * Mathf.Pow(1f - t, 0.72f));
                float r = (0.018f + 0.042f * profile) * scale;
                var squash = i == 0 ? new Vector3(1.3f, 0.7f, 1.45f) : new Vector3(1.1f, 0.85f, 1.35f);
                // tight spacing so segments overlap into one continuous body
                var seg = Blob(parent, "Seg" + i, r, squash, skin,
                    new Vector3(0, r * 0.85f, -i * SegmentSpacing(scale)), seed + i);
                list[i] = seg.transform;

                if (i == 0)
                {
                    float er = 0.012f * scale;
                    Blob(seg.transform, "EyeL", er, Vector3.one, _eye, new Vector3(-0.035f * scale, 0.02f * scale, 0.04f * scale), seed + 90);
                    Blob(seg.transform, "EyeR", er, Vector3.one, _eye, new Vector3(0.035f * scale, 0.02f * scale, 0.04f * scale), seed + 91);

                    // forked tongue: thin red cone, animated by the Snake script
                    var tongueMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    tongueMat.SetColor("_BaseColor", new Color(0.75f, 0.1f, 0.12f));
                    var tongue = new GameObject("Tongue");
                    tongue.transform.SetParent(seg.transform, false);
                    tongue.transform.localPosition = new Vector3(0, 0, 0.085f * scale);
                    tongue.transform.localRotation = Quaternion.Euler(90f, 0, 0);
                    tongue.AddComponent<MeshFilter>().sharedMesh = LowPolyFactory.Cone(0.006f * scale, 0.09f * scale, 4);
                    tongue.AddComponent<MeshRenderer>().sharedMaterial = tongueMat;
                }
            }
            return list;
        }
    }
}
