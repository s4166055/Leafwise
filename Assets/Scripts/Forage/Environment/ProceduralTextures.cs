using UnityEngine;

namespace Forage
{
    /// <summary>
    /// Deterministic procedural textures for the forest: bark, leaf-cluster
    /// cards, pine branches, grass blades, terrain albedo, rock. Pure code —
    /// usable from the editor (baked to PNG assets) and at runtime.
    /// </summary>
    public static class ProceduralTextures
    {
        // ---------- helpers ----------

        static Texture2D NewTex(int w, int h, bool alpha)
        {
            var tex = new Texture2D(w, h, alpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, true);
            tex.wrapMode = TextureWrapMode.Repeat;
            return tex;
        }

        static float Fbm(float x, float y, int octaves, float lacunarity = 2f, float gain = 0.5f)
        {
            float sum = 0f, amp = 1f, freq = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Mathf.PerlinNoise(x * freq, y * freq) * amp;
                norm += amp;
                amp *= gain;
                freq *= lacunarity;
            }
            return sum / norm;
        }

        // ---------- bark ----------

        public static Texture2D Bark(int seed, Color baseCol, Color crackCol, int size = 256)
        {
            var tex = NewTex(size, size, false);
            float ox = seed * 0.173f % 100f;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size, v = (float)y / size;
                    // vertical striations: stretch noise along Y
                    float stria = Fbm(u * 14f + ox, v * 2.2f + ox, 3);
                    float cracks = Mathf.Pow(Fbm(u * 28f + ox * 2f, v * 5f + ox, 2), 2.2f);
                    float grain = Fbm(u * 60f + ox, v * 60f + ox, 2) * 0.12f;
                    var c = Color.Lerp(crackCol, baseCol, Mathf.Clamp01(stria * 1.35f - cracks * 0.55f + grain));
                    pixels[y * size + x] = c;
                }
            tex.SetPixels(pixels);
            tex.Apply(true);
            return tex;
        }

        public static Texture2D BirchBark(int seed, int size = 256)
        {
            var tex = NewTex(size, size, false);
            float ox = seed * 0.211f % 100f;
            var rand = new System.Random(seed);
            var pixels = new Color[size * size];
            var white = new Color(0.92f, 0.9f, 0.86f);
            var grey = new Color(0.75f, 0.74f, 0.7f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size, v = (float)y / size;
                    float mottle = Fbm(u * 6f + ox, v * 6f + ox, 3);
                    pixels[y * size + x] = Color.Lerp(grey, white, 0.6f + mottle * 0.4f);
                }
            // horizontal dark lenticel strokes
            for (int i = 0; i < 90; i++)
            {
                int ly = rand.Next(size);
                int lx = rand.Next(size);
                int len = 4 + rand.Next(18);
                float dark = 0.15f + (float)rand.NextDouble() * 0.25f;
                for (int k = 0; k < len; k++)
                {
                    int px = (lx + k) % size;
                    var c = new Color(dark, dark * 0.95f, dark * 0.9f);
                    pixels[ly * size + px] = c;
                    if (ly + 1 < size && rand.NextDouble() < 0.5) pixels[(ly + 1) * size + px] = c;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply(true);
            return tex;
        }

        // ---------- foliage cards (RGBA cutout) ----------

        public static Texture2D LeafCluster(int seed, Color leafA, Color leafB, int size = 256)
        {
            var tex = NewTex(size, size, true);
            var rand = new System.Random(seed);
            var pixels = new Color[size * size]; // starts fully transparent black

            int leaves = 220;
            for (int i = 0; i < leaves; i++)
            {
                // cluster leaves toward the center
                float ang = (float)rand.NextDouble() * Mathf.PI * 2f;
                float rad = Mathf.Pow((float)rand.NextDouble(), 0.6f) * size * 0.42f;
                int cx = (int)(size * 0.5f + Mathf.Cos(ang) * rad);
                int cy = (int)(size * 0.5f + Mathf.Sin(ang) * rad);
                float lw = size * (0.03f + (float)rand.NextDouble() * 0.045f);
                float lh = lw * (1.5f + (float)rand.NextDouble());
                float rot = (float)rand.NextDouble() * Mathf.PI;
                var col = Color.Lerp(leafA, leafB, (float)rand.NextDouble());
                col *= 0.75f + (float)rand.NextDouble() * 0.45f;
                col.a = 1f;

                float cos = Mathf.Cos(rot), sin = Mathf.Sin(rot);
                int r = (int)Mathf.Max(lw, lh) + 1;
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int px = cx + dx, py = cy + dy;
                        if (px < 0 || px >= size || py < 0 || py >= size) continue;
                        float ex = (dx * cos + dy * sin) / lw;
                        float ey = (-dx * sin + dy * cos) / lh;
                        if (ex * ex + ey * ey <= 1f)
                            pixels[py * size + px] = col;
                    }
            }
            tex.SetPixels(pixels);
            tex.Apply(true);
            return tex;
        }

        public static Texture2D PineBranch(int seed, Color needleCol, int size = 256)
        {
            var tex = NewTex(size, size, true);
            var rand = new System.Random(seed);
            var pixels = new Color[size * size];

            // central stem rising from the bottom
            void Stroke(Vector2 a, Vector2 b, float width, Color col)
            {
                int steps = (int)((b - a).magnitude) + 1;
                for (int s = 0; s <= steps; s++)
                {
                    Vector2 p = Vector2.Lerp(a, b, (float)s / steps);
                    int w = Mathf.CeilToInt(width);
                    for (int dy = -w; dy <= w; dy++)
                        for (int dx = -w; dx <= w; dx++)
                        {
                            if (dx * dx + dy * dy > width * width) continue;
                            int px = (int)p.x + dx, py = (int)p.y + dy;
                            if (px < 0 || px >= size || py < 0 || py >= size) continue;
                            pixels[py * size + px] = col;
                        }
                }
            }

            var stemCol = new Color(0.32f, 0.22f, 0.14f, 1f);
            Stroke(new Vector2(size * 0.5f, 0), new Vector2(size * 0.5f, size * 0.95f), size * 0.008f, stemCol);

            int needles = 150;
            for (int i = 0; i < needles; i++)
            {
                float t = (float)rand.NextDouble();               // position along stem
                float sy = t * size * 0.92f;
                float reach = (1f - t * 0.8f) * size * 0.4f;       // shorter near the top
                float side = rand.NextDouble() < 0.5 ? -1f : 1f;
                float droop = reach * (0.15f + (float)rand.NextDouble() * 0.25f);
                var a = new Vector2(size * 0.5f, sy);
                var b = new Vector2(size * 0.5f + side * reach, sy + droop * 0.4f + reach * 0.35f);
                var col = needleCol * (0.7f + (float)rand.NextDouble() * 0.5f);
                col.a = 1f;
                Stroke(a, b, size * 0.006f, col);
            }
            tex.SetPixels(pixels);
            tex.Apply(true);
            return tex;
        }

        public static Texture2D GrassBlades(int seed, Color grassCol, int size = 256)
        {
            var tex = NewTex(size, size, true);
            var rand = new System.Random(seed);
            var pixels = new Color[size * size];

            int blades = 22;
            for (int i = 0; i < blades; i++)
            {
                float baseX = (float)rand.NextDouble() * size;
                float height = size * (0.5f + (float)rand.NextDouble() * 0.48f);
                float lean = ((float)rand.NextDouble() - 0.5f) * size * 0.35f;
                float width = size * (0.012f + (float)rand.NextDouble() * 0.014f);
                var col = grassCol * (0.65f + (float)rand.NextDouble() * 0.6f);
                col.a = 1f;

                int steps = (int)height;
                for (int s = 0; s < steps; s++)
                {
                    float t = (float)s / steps;
                    float px = baseX + lean * t * t;
                    float w = width * (1f - t * 0.9f);
                    int y = s;
                    for (int dx = -(int)w - 1; dx <= (int)w + 1; dx++)
                    {
                        int x = (int)px + dx;
                        if (x < 0 || x >= size || y < 0 || y >= size) continue;
                        if (Mathf.Abs(dx) <= w)
                            pixels[y * size + x] = col;
                    }
                }
            }
            tex.SetPixels(pixels);
            tex.Apply(true);
            return tex;
        }

        // ---------- terrain & rock ----------

        /// <summary>World-mapped terrain albedo using the same height/density fields the generator uses.</summary>
        public static Texture2D TerrainAlbedo(System.Func<float, float, float> heightAt,
            System.Func<float, float, float> densityAt, float worldSize, Vector2 pondCenter, float pondRadius,
            float campRadius, int seed, int size = 1024)
        {
            var tex = NewTex(size, size, false);
            float half = worldSize * 0.5f;
            float ox = seed * 0.31f % 100f;
            var pixels = new Color[size * size];

            var grassBright = new Color(0.4f, 0.52f, 0.24f);
            var grassDry = new Color(0.56f, 0.55f, 0.28f);
            var moss = new Color(0.22f, 0.34f, 0.16f);
            var dirt = new Color(0.42f, 0.33f, 0.22f);
            var sand = new Color(0.62f, 0.56f, 0.4f);

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float wx = -half + worldSize * x / (size - 1f);
                    float wz = -half + worldSize * y / (size - 1f);

                    float density = densityAt(wx, wz);
                    float dry = Fbm(wx * 0.03f + ox, wz * 0.03f + ox, 3);
                    float grain = Fbm(wx * 0.6f + ox * 2f, wz * 0.6f + ox * 2f, 2);

                    // open meadow: green-dry mix; under dense canopy: mossy and darker
                    var c = Color.Lerp(Color.Lerp(grassBright, grassDry, dry), moss, Mathf.SmoothStep(0f, 1f, density * 1.15f));

                    // trampled dirt around camp
                    float campT = new Vector2(wx, wz).magnitude / campRadius;
                    if (campT < 1.3f)
                        c = Color.Lerp(dirt, c, Mathf.SmoothStep(0.35f, 1.3f, campT));

                    // sandy rim then dirt around/under the pond
                    float pondD = Vector2.Distance(new Vector2(wx, wz), pondCenter);
                    if (pondD < pondRadius + 2.5f)
                        c = Color.Lerp(sand, c, Mathf.SmoothStep(pondRadius * 0.55f, pondRadius + 2.5f, pondD));

                    // fine grain breakup
                    c *= 0.92f + grain * 0.16f;
                    pixels[y * size + x] = c;
                }
            tex.SetPixels(pixels);
            tex.Apply(true);
            return tex;
        }

        public static Texture2D Rock(int seed, int size = 256)
        {
            var tex = NewTex(size, size, false);
            float ox = seed * 0.41f % 100f;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size, v = (float)y / size;
                    float n = Fbm(u * 7f + ox, v * 7f + ox, 4);
                    float speck = Fbm(u * 40f + ox * 3f, v * 40f + ox * 3f, 2);
                    float g = 0.34f + n * 0.3f + (speck - 0.5f) * 0.12f;
                    pixels[y * size + x] = new Color(g, g * 1.0f, g * 0.97f);
                }
            tex.SetPixels(pixels);
            tex.Apply(true);
            return tex;
        }

        /// <summary>Neutral-gray tiling grass detail (for URP Lit detail slot, MULX2 blending).</summary>
        public static Texture2D GroundDetail(int seed, int size = 256)
        {
            var tex = NewTex(size, size, false);
            float ox = seed * 0.53f % 100f;
            var rand = new System.Random(seed);
            var pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size, v = (float)y / size;
                    // tileable-ish mottle: sample noise on a torus via two offset lookups
                    float n = (Fbm(u * 9f + ox, v * 9f + ox, 3) + Fbm((1f - u) * 9f + ox * 2f, (1f - v) * 9f + ox * 2f, 3)) * 0.5f;
                    float g = 0.42f + n * 0.2f;
                    pixels[y * size + x] = new Color(g, g + 0.015f, g - 0.01f);
                }

            // short blade strokes for texture grain
            for (int i = 0; i < 900; i++)
            {
                int bx = rand.Next(size), by = rand.Next(size);
                int len = 3 + rand.Next(6);
                float shade = 0.35f + (float)rand.NextDouble() * 0.35f;
                int lean = rand.Next(-1, 2);
                for (int k = 0; k < len; k++)
                {
                    int px = (bx + k * lean + size) % size;
                    int py = (by + k) % size;
                    pixels[py * size + px] = new Color(shade * 0.95f, shade + 0.03f, shade * 0.85f);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply(true);
            return tex;
        }

        /// <summary>A single leaf silhouette for drifting-leaf particles.</summary>
        public static Texture2D SingleLeaf(int seed, Color leafCol, int size = 64)
        {
            var tex = NewTex(size, size, true);
            var pixels = new Color[size * size];
            float cx = size * 0.5f, cy = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // pointed ellipse leaf with a darker midrib
                    float ex = (x - cx) / (size * 0.28f);
                    float ey = (y - cy) / (size * 0.45f);
                    float d = ex * ex + Mathf.Abs(ey) * Mathf.Abs(ey) * (1f + Mathf.Abs(ey) * 0.6f);
                    if (d <= 1f)
                    {
                        var c = leafCol * (0.8f + 0.3f * (1f - d));
                        if (Mathf.Abs(ex) < 0.09f) c *= 0.75f; // midrib
                        c.a = 1f;
                        pixels[y * size + x] = c;
                    }
                }
            tex.SetPixels(pixels);
            tex.Apply(true);
            return tex;
        }

        /// <summary>Red cap with white warts for the fly agaric.</summary>
        public static Texture2D SpottedCap(int seed, Color capCol, int size = 128)
        {
            var tex = NewTex(size, size, false);
            var rand = new System.Random(seed);
            var pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = capCol * (0.9f + 0.2f * (i % 7) / 7f);

            for (int s = 0; s < 26; s++)
            {
                int cx = rand.Next(size), cy = rand.Next(size);
                int r = 2 + rand.Next(4);
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (dx * dx + dy * dy > r * r) continue;
                        int px = (cx + dx + size) % size, py = (cy + dy + size) % size;
                        pixels[py * size + px] = new Color(0.93f, 0.9f, 0.85f);
                    }
            }
            tex.SetPixels(pixels);
            tex.Apply(true);
            return tex;
        }
    }
}
