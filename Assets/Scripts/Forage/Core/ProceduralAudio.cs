using System.Collections.Generic;
using UnityEngine;

namespace Forage
{
    /// <summary>
    /// All game audio is synthesized in code (no imported assets):
    /// forest ambience, fire crackle, water, hiss, chirps, chimes, gulps,
    /// crunches. Clips are cached; play helpers attach 3D sources.
    /// </summary>
    public static class ProceduralAudio
    {
        const int SampleRate = 22050;
        static readonly Dictionary<string, AudioClip> _cache = new Dictionary<string, AudioClip>();
        static System.Random _rand = new System.Random(777);

        static float Noise() => (float)(_rand.NextDouble() * 2.0 - 1.0);

        static AudioClip Bake(string name, float seconds, System.Func<int, float, float> sampleAt, bool loop = false)
        {
            if (_cache.TryGetValue(name, out var cached) && cached != null) return cached;
            int n = (int)(seconds * SampleRate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
                data[i] = Mathf.Clamp(sampleAt(i, (float)i / SampleRate), -1f, 1f);
            // soft fade edges to avoid clicks (skip fade-out for loops)
            int fade = SampleRate / 50;
            for (int i = 0; i < fade && i < n; i++)
            {
                data[i] *= (float)i / fade;
                if (!loop) data[n - 1 - i] *= (float)i / fade;
            }
            var clip = AudioClip.Create(name, n, 1, SampleRate, false);
            clip.SetData(data, 0);
            _cache[name] = clip;
            return clip;
        }

        // ---------- clips ----------

        /// <summary>Gentle wind + leaf rustle bed (looping).</summary>
        public static AudioClip ForestAmbience()
        {
            float lp = 0f, lp2 = 0f;
            return Bake("ambience", 12f, (i, t) =>
            {
                float raw = Noise();
                lp = Mathf.Lerp(lp, raw, 0.02f);          // deep wind
                lp2 = Mathf.Lerp(lp2, raw, 0.12f);         // leafy hiss
                float swell = 0.6f + 0.4f * Mathf.Sin(t * 0.35f) * Mathf.Sin(t * 0.13f + 1f);
                return (lp * 0.55f + lp2 * 0.12f) * swell * 0.55f;
            }, loop: true);
        }

        /// <summary>Crackling fire (looping).</summary>
        public static AudioClip FireCrackle()
        {
            float lp = 0f;
            float pop = 0f;
            return Bake("crackle", 6f, (i, t) =>
            {
                float raw = Noise();
                lp = Mathf.Lerp(lp, raw, 0.25f);
                if (_rand.NextDouble() < 0.0012) pop = 0.9f + Noise() * 0.1f; // random pops
                pop *= 0.988f;
                return lp * 0.18f + pop * Noise() * 0.8f;
            }, loop: true);
        }

        /// <summary>Soft water lapping (looping).</summary>
        public static AudioClip WaterLap()
        {
            float lp = 0f;
            return Bake("waterlap", 8f, (i, t) =>
            {
                lp = Mathf.Lerp(lp, Noise(), 0.06f);
                float wave = Mathf.Max(0f, Mathf.Sin(t * 0.9f)) * Mathf.Max(0f, Mathf.Sin(t * 1.7f + 0.6f));
                return lp * 0.5f * (0.15f + wave * 0.85f) * 0.5f;
            }, loop: true);
        }

        /// <summary>Snake hiss (one-shot ~1.2s).</summary>
        public static AudioClip Hiss()
        {
            float hp = 0f, prev = 0f;
            return Bake("hiss", 1.2f, (i, t) =>
            {
                float raw = Noise();
                hp = raw - prev; prev = raw; // crude high-pass
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 1.2f));
                return hp * 0.5f * env;
            });
        }

        /// <summary>Bird chirp (one-shot, seeded variant).</summary>
        public static AudioClip Chirp(int variant)
        {
            return Bake("chirp" + variant, 0.5f, (i, t) =>
            {
                float f = 2600f + variant * 300f;
                float sweep = Mathf.Sin(t * 18f + variant) * 500f;
                float env = Mathf.Exp(-t * 6f) * (Mathf.Sin(t * 40f + variant) > -0.2f ? 1f : 0f);
                return Mathf.Sin(2f * Mathf.PI * (f + sweep) * t) * env * 0.4f;
            });
        }

        /// <summary>Owl hoot for nightfall.</summary>
        public static AudioClip Hoot()
        {
            return Bake("hoot", 1.4f, (i, t) =>
            {
                float phase = t < 0.5f ? t / 0.5f : Mathf.Clamp01((t - 0.65f) / 0.6f);
                float on = (t < 0.5f || t > 0.65f) ? 1f : 0f;
                float env = Mathf.Sin(Mathf.PI * phase) * on;
                return Mathf.Sin(2f * Mathf.PI * 340f * t + Mathf.Sin(t * 30f) * 0.4f) * env * 0.5f;
            });
        }

        /// <summary>Objective-complete chime: little rising arpeggio.</summary>
        public static AudioClip Chime()
        {
            return Bake("chime", 0.9f, (i, t) =>
            {
                float[] notes = { 523.25f, 659.25f, 783.99f }; // C5 E5 G5
                float s = 0f;
                for (int k = 0; k < 3; k++)
                {
                    float start = k * 0.12f;
                    if (t >= start)
                        s += Mathf.Sin(2f * Mathf.PI * notes[k] * (t - start)) * Mathf.Exp(-(t - start) * 5f) * 0.3f;
                }
                return s;
            });
        }

        /// <summary>Gulp for drinking.</summary>
        public static AudioClip Gulp()
        {
            return Bake("gulp", 0.45f, (i, t) =>
            {
                float f = 220f - t * 260f;
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.45f));
                return Mathf.Sin(2f * Mathf.PI * f * t) * env * 0.55f;
            });
        }

        /// <summary>Crunch for eating.</summary>
        public static AudioClip Crunch()
        {
            float lp = 0f;
            return Bake("crunch", 0.35f, (i, t) =>
            {
                lp = Mathf.Lerp(lp, Noise(), 0.6f);
                float env = Mathf.Exp(-t * 12f);
                return lp * env * 0.8f;
            });
        }

        /// <summary>Night crickets bed (looping).</summary>
        public static AudioClip Crickets()
        {
            return Bake("crickets", 6f, (i, t) =>
            {
                // two interleaved chirp trains
                float s = 0f;
                float c1 = Mathf.Repeat(t, 0.7f);
                if (c1 < 0.18f && Mathf.Sin(t * 0.9f) > -0.3f)
                    s += Mathf.Sin(2f * Mathf.PI * 4200f * t) * Mathf.Sin(Mathf.PI * c1 / 0.18f) *
                         (Mathf.Sin(2f * Mathf.PI * 38f * t) > 0 ? 1f : 0f) * 0.22f;
                float c2 = Mathf.Repeat(t + 0.31f, 1.1f);
                if (c2 < 0.14f && Mathf.Sin(t * 0.53f + 2f) > 0f)
                    s += Mathf.Sin(2f * Mathf.PI * 3400f * t) * Mathf.Sin(Mathf.PI * c2 / 0.14f) *
                         (Mathf.Sin(2f * Mathf.PI * 31f * t) > 0 ? 1f : 0f) * 0.18f;
                return s;
            }, loop: true);
        }

        /// <summary>Low bear growl (one-shot ~1.6s).</summary>
        public static AudioClip Growl()
        {
            float lp = 0f;
            return Bake("growl", 1.6f, (i, t) =>
            {
                lp = Mathf.Lerp(lp, Noise(), 0.04f);
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 1.6f));
                float rumble = Mathf.Sin(2f * Mathf.PI * (52f + Mathf.Sin(t * 9f) * 8f) * t);
                return (rumble * 0.5f + lp * 1.6f) * env * 0.8f;
            });
        }

        /// <summary>Flint click/strike.</summary>
        public static AudioClip FlintClick()
        {
            return Bake("flint", 0.12f, (i, t) =>
            {
                float env = Mathf.Exp(-t * 60f);
                return Noise() * env * 0.9f;
            });
        }

        // ---------- play helpers ----------

        public static AudioSource Loop(Transform parent, AudioClip clip, float volume, float spatial = 1f, float range = 12f)
        {
            var go = new GameObject("Audio_" + clip.name);
            go.transform.SetParent(parent, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.volume = volume;
            src.spatialBlend = spatial;
            src.maxDistance = range;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.Play();
            return src;
        }

        public static void PlayAt(Vector3 position, AudioClip clip, float volume = 1f)
        {
            AudioSource.PlayClipAtPoint(clip, position, volume);
        }
    }
}
