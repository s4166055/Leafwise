using UnityEngine;

namespace Forage
{
    /// <summary>
    /// Flat white rings that grow and fade on the water surface. The pond mesh
    /// can only show ripples down to the spacing of its vertices, so a hand
    /// dipped in or a dropped item made a swell you could barely see from
    /// standing height. These rings are what reads as "I touched the water".
    /// Code-made texture and particle system: no assets, nothing to strip.
    /// </summary>
    public static class RippleRingFx
    {
        static Material _ringMat;
        static float _lastPlay = -1f;

        /// <summary>Strength 0..1: 1 is a pot or bucket dunked in, ~0.3 a fingertip.</summary>
        public static void Play(Vector3 surfacePoint, float strength)
        {
            // many small ripples can arrive in one frame (bobbing items, fish); one ring is enough
            if (Time.time - _lastPlay < 0.08f) return;
            var mat = RingMaterial();
            if (mat == null) return;
            _lastPlay = Time.time;
            strength = Mathf.Clamp01(strength);

            var go = new GameObject("RippleRings", typeof(ParticleSystem));
            go.transform.position = surfacePoint + Vector3.up * 0.01f;
            var ps = go.GetComponent<ParticleSystem>();

            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 1.6f + strength * 0.9f;
            main.startSpeed = 0f;
            main.startSize = 0.7f + strength * 1.7f;
            main.startRotation3D = true;
            main.startRotationX = -Mathf.PI / 2f;   // lie flat on the water, face up
            main.startColor = new Color(1f, 1f, 1f, 0.5f + 0.35f * strength);
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            int rings = strength > 0.6f ? 3 : 2;
            var bursts = new ParticleSystem.Burst[rings];
            for (int i = 0; i < rings; i++) bursts[i] = new ParticleSystem.Burst(i * 0.22f, (short)1);
            emission.SetBursts(bursts);

            var shape = ps.shape;
            shape.enabled = false;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.08f, 1f, 1f));

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
            colour.color = g;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = mat;
            renderer.alignment = ParticleSystemRenderSpace.World;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            ps.Play();
            Object.Destroy(go, 4f);
        }

        /// <summary>A soft white ring on a transparent texture, made once in code.</summary>
        static Material RingMaterial()
        {
            if (_ringMat != null || ForageAssets.Instance == null || ForageAssets.Instance.smoke == null) return _ringMat;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "RippleRing" };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float r = new Vector2(x - n / 2 + 0.5f, y - n / 2 + 0.5f).magnitude / (n / 2f);
                    float a = Mathf.Clamp01(1f - Mathf.Abs(r - 0.82f) / 0.1f);
                    px[y * n + x] = new Color(1f, 1f, 1f, a * a);
                }
            tex.SetPixels(px);
            tex.Apply();
            _ringMat = new Material(ForageAssets.Instance.smoke) { name = "RippleRing (runtime)" };
            _ringMat.SetTexture("_BaseMap", tex);
            _ringMat.SetTexture("_MainTex", tex);
            _ringMat.SetColor("_BaseColor", new Color(0.9f, 0.97f, 1f, 0.75f));
            _ringMat.SetFloat("_Cull", 0f);   // both faces: seen from above at any angle
            return _ringMat;
        }
    }
}
