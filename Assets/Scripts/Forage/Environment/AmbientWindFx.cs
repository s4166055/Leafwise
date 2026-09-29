using UnityEngine;

namespace Forage
{
    /// <summary>
    /// Ambient life for the forest: leaves drifting down through the air and
    /// sunlit dust motes, emitted in a volume that follows the player.
    /// </summary>
    public class AmbientWindFx : MonoBehaviour
    {
        public Material leafParticleMat;
        public Material moteParticleMat;

        ParticleSystem _leaves;
        ParticleSystem _motes;
        Transform _head;

        void Start()
        {
            _leaves = BuildLeaves();
            _motes = BuildMotes();
        }

        void LateUpdate()
        {
            if (_head == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                _head = cam.transform;
            }
            // keep the emission volume centered on the player
            var p = _head.position;
            transform.position = new Vector3(p.x, p.y + 6f, p.z);
        }

        ParticleSystem BuildLeaves()
        {
            var go = new GameObject("DriftingLeaves", typeof(ParticleSystem));
            go.transform.SetParent(transform, false);
            var ps = go.GetComponent<ParticleSystem>();

            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 11f);
            main.startSpeed = 0.1f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0.02f;

            var emission = ps.emission;
            emission.rateOverTime = 9f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(26f, 1f, 26f);

            // sideways wind drift + tumbling
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.15f, 0.5f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.55f, -0.3f);
            vel.z = new ParticleSystem.MinMaxCurve(0.05f, 0.3f);

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.3f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = leafParticleMat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }

        ParticleSystem BuildMotes()
        {
            var go = new GameObject("DustMotes", typeof(ParticleSystem));
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.down * 4f;
            var ps = go.GetComponent<ParticleSystem>();

            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startSpeed = 0.02f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.004f, 0.012f);
            main.startColor = new Color(1f, 0.95f, 0.8f, 0.35f);

            var emission = ps.emission;
            emission.rateOverTime = 10f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(10f, 4f, 10f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.08f;
            noise.frequency = 0.5f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = moteParticleMat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }
    }
}
