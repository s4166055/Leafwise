using UnityEngine;

namespace Forage
{
    /// <summary>Code-built particle systems for the campfire (flame, smoke, embers, sparks).</summary>
    public static class FireVfx
    {
        /// <summary>One-shot spark burst at a world point (flint strikes).</summary>
        public static void SparkBurst(Vector3 position, int count)
        {
            var go = new GameObject("SparkBurst", typeof(ParticleSystem));
            go.transform.position = position;
            var ps = go.GetComponent<ParticleSystem>();

            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.02f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.85f, 0.4f), new Color(1f, 0.55f, 0.15f));
            main.gravityModifier = 1.2f;
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.02f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = ForageAssets.Instance.ember;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            ps.Play();
            Object.Destroy(go, 1f);
        }

        public static ParticleSystem Flame(Transform parent)
        {
            var ps = NewSystem(parent, "Flame", ForageAssets.Instance.flame);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.75f, 0.15f), new Color(1f, 0.35f, 0.05f));

            var emission = ps.emission;
            emission.rateOverTime = 55f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.16f;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0, 0.7f), new Keyframe(0.35f, 1f), new Keyframe(1, 0.05f)));

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.4f, 0.05f), 0.6f),
                        new GradientColorKey(new Color(0.6f, 0.1f, 0.02f), 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            return ps;
        }

        public static ParticleSystem Smoke(Transform parent, float rate = 12f)
        {
            var ps = NewSystem(parent, "Smoke", ForageAssets.Instance.smoke);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            main.startColor = new Color(0.35f, 0.35f, 0.35f, 0.5f);

            var emission = ps.emission;
            emission.rateOverTime = rate;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.1f;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(1, 1.6f)));

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.4f, 0.4f, 0.4f), 0f), new GradientColorKey(new Color(0.55f, 0.55f, 0.55f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.45f, 0.2f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            return ps;
        }

        public static ParticleSystem Embers(Transform parent)
        {
            var ps = NewSystem(parent, "Embers", ForageAssets.Instance.ember);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.6f, 0.1f), new Color(1f, 0.3f, 0.05f));
            main.gravityModifier = -0.05f;

            var emission = ps.emission;
            emission.rateOverTime = 10f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 20f;
            shape.radius = 0.14f;
            return ps;
        }

        static ParticleSystem NewSystem(Transform parent, string name, Material mat)
        {
            var go = new GameObject(name, typeof(ParticleSystem));
            go.transform.SetParent(parent, false);
            var ps = go.GetComponent<ParticleSystem>();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }
    }
}
