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

        /// <summary>Snake skin: overlapping scale pattern + species banding, generated at runtime.</summary>
        public static Material SnakeSkin(Color baseCol, Color bandCol, int seed)
        {
            int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, true);
            tex.wrapMode = TextureWrapMode.Repeat;
            var px = new Color[size * size];
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
                    px[y * size + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply(true);

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
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
            EnsureMats();
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
            Blob(head.transform, "EyeL", er, Vector3.one, _eye, new Vector3(-0.036f * scale, 0.022f * scale, 0.045f * scale), seed + 90);
            Blob(head.transform, "EyeR", er, Vector3.one, _eye, new Vector3(0.036f * scale, 0.022f * scale, 0.045f * scale), seed + 91);

            var tongueMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            tongueMat.SetColor("_BaseColor", new Color(0.75f, 0.1f, 0.12f));
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
