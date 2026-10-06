using System.Collections.Generic;
using UnityEngine;

namespace Forage
{
    /// <summary>
    /// A living pond surface. Gentle waves move across it, ripples spread from
    /// wherever a hand, controller or the pot touches the water, and now and
    /// then a fish rises. Headset testers described the old pond as "a blue
    /// transparent solid body": a flat disc with nothing moving on it.
    ///
    /// The surface is a ~700-vertex disc animated on the CPU each frame. No
    /// custom shader, so nothing can be stripped from the Quest build, and the
    /// glossy water material picks up moving highlights from the normals.
    /// </summary>
    public class PondWater : MonoBehaviour
    {
        [Header("Shape")]
        public int rings = 14;
        public int segments = 48;

        [Header("Motion")]
        public float waveHeight = 0.02f;
        public float rippleSpeed = 0.9f;        // m/s the ring travels outward
        // The surface has a vertex every ~0.5 m, so the swell must be longer than
        // that to show at all. The fine ring you actually see is a particle.
        public float rippleWavelength = 1.1f;
        public float rippleLife = 3.5f;
        public Vector2 fishRiseEverySeconds = new Vector2(4f, 9f);

        public static PondWater Instance { get; private set; }

        float _radius;
        Mesh _mesh;
        Vector3[] _base, _verts;

        struct RippleRing { public Vector2 centre; public float start, strength; }
        readonly List<RippleRing> _ripples = new List<RippleRing>();

        // things that can touch the water: controllers and tracked palms (PlayerHands)
        readonly Dictionary<int, float> _lastTouchRipple = new Dictionary<int, float>();
        readonly Dictionary<int, bool> _wasIn = new Dictionary<int, bool>();
        float _nextFish;

        /// <summary>World height of the still surface.</summary>
        public float SurfaceY => transform.position.y;

        public void Init(float radius)
        {
            Instance = this;
            _radius = radius;
            BuildMesh();
            _nextFish = Time.time + Random.Range(fishRiseEverySeconds.x, fishRiseEverySeconds.y);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>True when a world point is over the visible water, horizontally.</summary>
        public bool IsOverWater(Vector3 world)
        {
            Vector3 l = transform.InverseTransformPoint(world);
            return new Vector2(l.x, l.z).magnitude < _radius;
        }

        public void Ripple(Vector3 world, float strength)
        {
            Vector3 l = transform.InverseTransformPoint(world);
            if (_ripples.Count >= 12) _ripples.RemoveAt(0);
            _ripples.Add(new RippleRing { centre = new Vector2(l.x, l.z), start = Time.time, strength = strength });
            RingParticles(new Vector3(world.x, SurfaceY + 0.01f, world.z), strength);
        }

        static Material _ringMat;

        /// <summary>A soft white ring on a transparent texture, made once in code.</summary>
        static Material RingMaterial()
        {
            if (_ringMat != null || ForageAssets.Instance == null) return _ringMat;
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
            _ringMat.SetFloat("_Cull", 0f);   // draw both faces: seen from above at any angle
            return _ringMat;
        }

        /// <summary>Two or three flat rings that grow and fade, lying on the surface.</summary>
        static void RingParticles(Vector3 position, float strength)
        {
            var mat = RingMaterial();
            if (mat == null) return;
            var go = new GameObject("RippleRings", typeof(ParticleSystem));
            go.transform.position = position;
            var ps = go.GetComponent<ParticleSystem>();

            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 1.8f + strength * 0.8f;
            main.startSpeed = 0f;
            main.startSize = 0.8f + strength * 1.6f;
            main.startRotation3D = true;
            main.startRotationX = -Mathf.PI / 2f;  // lie flat on the water, face up
            main.startColor = new Color(1f, 1f, 1f, 0.55f + 0.3f * Mathf.Clamp01(strength));
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            int rings = strength > 0.7f ? 3 : 2;
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
            Destroy(go, 4f);
        }

        /// <summary>Ripple, droplets and a splash sound. Strength 1 = a pot dipped in.</summary>
        public static void Splash(Vector3 world, float strength)
        {
            var w = Instance;
            if (w != null)
            {
                w.Ripple(world, strength);
                world.y = w.SurfaceY;
            }
            Droplets(world, Mathf.RoundToInt(10 + 16 * strength));
            ProceduralAudio.PlayAt(world, ProceduralAudio.Splash(), Mathf.Clamp01(0.35f + 0.5f * strength));
        }

        static void Droplets(Vector3 position, int count)
        {
            if (ForageAssets.Instance == null) return;
            var go = new GameObject("Splash", typeof(ParticleSystem));
            go.transform.position = position;
            go.transform.rotation = Quaternion.LookRotation(Vector3.up);
            var ps = go.GetComponent<ParticleSystem>();

            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 1f, 1f, 1f), new Color(0.8f, 0.92f, 1f, 0.9f));
            main.gravityModifier = 1.4f;
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 28f;
            shape.radius = 0.05f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = DropletMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            ps.Play();
            Destroy(go, 1.2f);
        }

        static Material _dropletMat;

        /// <summary>The smoke particle material is mid-grey; droplets need to read as bright water.</summary>
        static Material DropletMaterial()
        {
            if (_dropletMat == null)
            {
                _dropletMat = new Material(ForageAssets.Instance.smoke) { name = "Droplet (runtime)" };
                _dropletMat.SetColor("_BaseColor", new Color(0.95f, 0.98f, 1f, 0.95f));
            }
            return _dropletMat;
        }

        void BuildMesh()
        {
            int vCount = 1 + rings * segments;
            _base = new Vector3[vCount];
            var uv = new Vector2[vCount];
            _base[0] = Vector3.zero;
            uv[0] = new Vector2(0.5f, 0.5f);
            for (int r = 1; r <= rings; r++)
            {
                float rad = _radius * r / rings;
                for (int s = 0; s < segments; s++)
                {
                    float a = s * Mathf.PI * 2f / segments;
                    int i = 1 + (r - 1) * segments + s;
                    _base[i] = new Vector3(Mathf.Cos(a) * rad, 0f, Mathf.Sin(a) * rad);
                    uv[i] = new Vector2(0.5f + Mathf.Cos(a) * 0.5f * r / rings, 0.5f + Mathf.Sin(a) * 0.5f * r / rings);
                }
            }

            var tris = new List<int>(segments * (rings * 2 - 1) * 3);
            for (int s = 0; s < segments; s++)
            {
                int s1 = (s + 1) % segments;
                tris.Add(0); tris.Add(1 + s1); tris.Add(1 + s);
            }
            for (int r = 1; r < rings; r++)
            {
                int inner = 1 + (r - 1) * segments, outer = 1 + r * segments;
                for (int s = 0; s < segments; s++)
                {
                    int s1 = (s + 1) % segments;
                    tris.Add(inner + s); tris.Add(inner + s1); tris.Add(outer + s);
                    tris.Add(inner + s1); tris.Add(outer + s1); tris.Add(outer + s);
                }
            }

            _verts = (Vector3[])_base.Clone();
            _mesh = new Mesh { name = "PondSurface" };
            _mesh.MarkDynamic();
            _mesh.vertices = _verts;
            _mesh.uv = uv;
            _mesh.SetTriangles(tris, 0);
            _mesh.RecalculateNormals();
            // fixed bounds: the animation never moves a vertex more than a few cm
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(_radius * 2f, 0.5f, _radius * 2f));

            var mf = GetComponent<MeshFilter>();
            if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = _mesh;
        }

        void Update()
        {
            if (_mesh == null) return;
            DetectTouches();
            FishRise();
            Animate();
        }

        void Animate()
        {
            float t = Time.time;
            for (int i = _ripples.Count - 1; i >= 0; i--)
                if (t - _ripples[i].start > rippleLife) _ripples.RemoveAt(i);

            for (int i = 0; i < _base.Length; i++)
            {
                float x = _base[i].x, z = _base[i].z;
                float edge = 1f - Mathf.Pow(Mathf.Sqrt(x * x + z * z) / _radius, 6f); // rim stays put against the bank

                float y = waveHeight * 0.5f * (
                    Mathf.Sin(x * 1.3f + t * 1.1f) +
                    0.6f * Mathf.Sin(z * 1.9f - t * 1.4f) +
                    0.4f * Mathf.Sin((x + z) * 2.7f + t * 2.0f));

                for (int k = 0; k < _ripples.Count; k++)
                {
                    var rp = _ripples[k];
                    float age = t - rp.start;
                    float d = Vector2.Distance(new Vector2(x, z), rp.centre);
                    float front = age * rippleSpeed;
                    float band = d - front;
                    if (band > 0.05f || band < -2.2f) continue;   // only the travelling swell
                    float fade = Mathf.Exp(-age * 1.1f) / (1f + d * 0.6f);
                    float shape = Mathf.Clamp01(1f + band / 2.2f);
                    y += rp.strength * 0.05f * fade * shape * Mathf.Sin(2f * Mathf.PI * band / rippleWavelength);
                }

                _verts[i].y = y * Mathf.Max(0f, edge);
            }
            _mesh.vertices = _verts;
            _mesh.RecalculateNormals();
        }

        void FishRise()
        {
            if (Time.time < _nextFish) return;
            _nextFish = Time.time + Random.Range(fishRiseEverySeconds.x, fishRiseEverySeconds.y);
            Vector2 p = Random.insideUnitCircle * _radius * 0.7f;
            Ripple(transform.TransformPoint(new Vector3(p.x, 0f, p.y)), 0.45f);
        }

        readonly List<PlayerHands.Point> _points = new List<PlayerHands.Point>();
        readonly HashSet<int> _seen = new HashSet<int>();

        void DetectTouches()
        {
            PlayerHands.Get(_points);
            _seen.Clear();
            foreach (var pt in _points)
            {
                _seen.Add(pt.id);
                Touch(pt.id, pt.position);
            }
            // a hand that stopped tracking has left the water
            foreach (int id in new[] { 1, 2, 3, 4 })
                if (!_seen.Contains(id)) _wasIn[id] = false;
        }

        void Touch(int id, Vector3 world)
        {
            bool inWater = IsOverWater(world) && world.y < SurfaceY + 0.03f;
            _wasIn.TryGetValue(id, out bool was);
            _wasIn[id] = inWater;
            if (!inWater) return;

            if (!was)
            {
                Splash(world, 0.6f);   // hand goes in
                _lastTouchRipple[id] = Time.time;
            }
            else if (!_lastTouchRipple.TryGetValue(id, out float last) || Time.time - last > 0.3f)
            {
                Ripple(world, 0.35f);  // trailing ripples while the hand moves through
                _lastTouchRipple[id] = Time.time;
            }
        }
    }
}
