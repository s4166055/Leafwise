using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Forage
{
    /// <summary>
    /// Physical water for the pond.
    ///
    /// * A live surface mesh: a slow swell plus expanding ripple rings from
    ///   anything that breaks the surface (dropped items, your hands, fish).
    /// * Buoyancy per Rigidbody: wood floats, flint and the metal pot sink,
    ///   the bucket floats less the fuller it is. Bodies bob on the waves.
    /// * Water drag and a slow circulating current, so floating things drift.
    /// * Splash droplets + a splash sound on entry.
    ///
    /// The original PondWater mesh is static-batched with the forest (so it
    /// cannot be deformed); this component hides its renderer and draws an
    /// animated copy instead. The PondWater collider and "Water" tag stay, so
    /// the pot's scooping trigger works exactly as before.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class WaterBody : MonoBehaviour
    {
        static WaterBody _instance;
        public static WaterBody Instance
        {
            get
            {
                if (_instance == null) _instance = FindFirstObjectByType<WaterBody>();
                return _instance;
            }
        }

        [Header("Shape (set from the generated pond)")]
        public Vector2 center;
        public float radius = 7.4f;
        public float surfaceY;
        public Material waterMat;

        [Header("Waves")]
        public float swellAmplitude = 0.012f;
        public int rings = 30;
        public int segments = 64;

        [Header("Physics")]
        public float density = 1.6f;          // default buoyancy multiplier (light wood floats)
        public float waterDrag = 2.2f;
        public float angularDrag = 1.5f;
        public float currentSpeed = 0.22f;    // slow circulation around the pond

        class RippleWave { public Vector2 p; public float t0; public float amp; }
        readonly List<RippleWave> _ripples = new List<RippleWave>();
        readonly HashSet<Rigidbody> _bodies = new HashSet<Rigidbody>();
        readonly List<Rigidbody> _scratch = new List<Rigidbody>();
        readonly Dictionary<Rigidbody, bool> _wasUnder = new Dictionary<Rigidbody, bool>();

        Mesh _mesh;
        Vector3[] _base, _verts;
        Transform _surface;
        Vector3 _lastLeft, _lastRight;
        bool _leftWasIn, _rightWasIn;
        float _splashCooldown;

        void Awake() => _instance = this;

        void Start()
        {
            var r = GetComponent<MeshRenderer>();
            if (r != null)
            {
                if (waterMat == null) waterMat = r.sharedMaterial;
                r.enabled = false; // replaced by the animated surface below
            }
            BuildSurface();
        }

        // ---------------------------------------------------------- queries

        /// <summary>Animated surface height at a world XZ (includes swell + ripples).</summary>
        public float SurfaceHeightAt(float x, float z) => surfaceY + WaveHeight(x - center.x, z - center.y, Time.time);

        public bool InsideFootprint(Vector3 p, float margin = 0f) =>
            Vector2.Distance(new Vector2(p.x, p.z), center) < radius - margin;

        /// <summary>True when a world point is inside the pond and below its surface.</summary>
        public bool IsUnderwater(Vector3 p, float above = 0f) =>
            InsideFootprint(p) && p.y < SurfaceHeightAt(p.x, p.z) + above;

        /// <summary>Slow circulation: tangential flow around the pond centre (strongest mid-radius).</summary>
        public Vector3 CurrentAt(Vector3 p)
        {
            Vector2 d = new Vector2(p.x - center.x, p.z - center.y);
            float r01 = Mathf.Clamp01(d.magnitude / radius);
            if (d.sqrMagnitude < 0.0001f) return Vector3.zero;
            Vector2 tangent = new Vector2(-d.y, d.x).normalized;
            float strength = Mathf.Sin(r01 * Mathf.PI) * currentSpeed;
            return new Vector3(tangent.x, 0f, tangent.y) * strength;
        }

        /// <summary>Start an expanding ripple ring at a world point.</summary>
        public void Ripple(Vector3 worldPos, float amplitude)
        {
            if (!InsideFootprint(worldPos)) return;
            if (_ripples.Count >= 12) _ripples.RemoveAt(0);
            _ripples.Add(new RippleWave
            {
                p = new Vector2(worldPos.x - center.x, worldPos.z - center.y),
                t0 = Time.time,
                amp = Mathf.Clamp(amplitude, 0.004f, 0.06f)
            });
        }

        /// <summary>Ripple + droplets + sound.</summary>
        public void Splash(Vector3 worldPos, float strength)
        {
            strength = Mathf.Clamp01(strength);
            var at = new Vector3(worldPos.x, SurfaceHeightAt(worldPos.x, worldPos.z), worldPos.z);
            Ripple(at, 0.01f + strength * 0.04f);
            Droplets(at, (int)Mathf.Lerp(6, 40, strength), new Color(0.75f, 0.85f, 0.95f, 0.7f), Vector3.up);
            if (_splashCooldown <= 0f)
            {
                _splashCooldown = 0.12f;
                ProceduralAudio.PlayAt(at, ProceduralAudio.Splash(), Mathf.Lerp(0.25f, 0.9f, strength));
            }
        }

        /// <summary>One-shot burst of water droplets (shared with the bucket).</summary>
        public static void Droplets(Vector3 pos, int count, Color color, Vector3 dir)
        {
            var assets = ForageAssets.Instance;
            if (assets == null || assets.smoke == null) return;
            var go = new GameObject("WaterDroplets", typeof(ParticleSystem));
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(dir.sqrMagnitude > 0.001f ? dir : Vector3.up);
            var ps = go.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
            main.startColor = color;
            main.gravityModifier = 1.4f;
            main.playOnAwake = false;
            main.duration = 0.2f;
            main.loop = false;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Max(1, count)) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 28f;
            shape.radius = 0.04f;
            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.material = assets.smoke;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            Destroy(go, 1.5f);
        }

        // ---------------------------------------------------------- surface

        void BuildSurface()
        {
            var go = new GameObject("PondSurface (animated)");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(center.x, surfaceY, center.y);
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            _surface = go.transform;

            int vCount = 1 + rings * segments;
            _base = new Vector3[vCount];
            var uvs = new Vector2[vCount];
            _base[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);
            for (int ri = 1; ri <= rings; ri++)
            {
                float rr = radius * ri / rings;
                for (int s = 0; s < segments; s++)
                {
                    float a = s * Mathf.PI * 2f / segments;
                    int idx = 1 + (ri - 1) * segments + s;
                    _base[idx] = new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
                    uvs[idx] = new Vector2(0.5f + _base[idx].x / (radius * 2f), 0.5f + _base[idx].z / (radius * 2f));
                }
            }

            var tris = new List<int>();
            for (int s = 0; s < segments; s++)
            {
                int a = 1 + s, b = 1 + (s + 1) % segments;
                tris.Add(0); tris.Add(b); tris.Add(a);
            }
            for (int ri = 1; ri < rings; ri++)
                for (int s = 0; s < segments; s++)
                {
                    int i0 = 1 + (ri - 1) * segments + s;
                    int i1 = 1 + (ri - 1) * segments + (s + 1) % segments;
                    int o0 = 1 + ri * segments + s;
                    int o1 = 1 + ri * segments + (s + 1) % segments;
                    tris.Add(i0); tris.Add(i1); tris.Add(o0);
                    tris.Add(i1); tris.Add(o1); tris.Add(o0);
                }

            _verts = (Vector3[])_base.Clone();
            _mesh = new Mesh { name = "PondSurface" };
            _mesh.MarkDynamic();
            _mesh.vertices = _verts;
            _mesh.uv = uvs;
            _mesh.SetTriangles(tris, 0);
            _mesh.RecalculateNormals();
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(radius * 2f, 0.5f, radius * 2f));

            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = waterMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        float WaveHeight(float lx, float lz, float t)
        {
            float h = swellAmplitude * (Mathf.Sin(lx * 0.9f + t * 1.1f) * 0.6f +
                                        Mathf.Sin(lz * 1.3f - t * 0.8f) * 0.4f);
            for (int i = 0; i < _ripples.Count; i++)
            {
                var rp = _ripples[i];
                float age = t - rp.t0;
                float d = Mathf.Sqrt((lx - rp.p.x) * (lx - rp.p.x) + (lz - rp.p.y) * (lz - rp.p.y));
                float front = age * 1.4f;               // ring expands at 1.4 m/s
                if (d > front) continue;
                float k = 10f;                           // ~0.6 m wavelength
                h += rp.amp * Mathf.Sin(k * (d - front)) * Mathf.Exp(-age * 1.3f) / (1f + d * 1.5f);
            }
            return h;
        }

        void Update()
        {
            float t = Time.time;
            _splashCooldown -= Time.deltaTime;
            _ripples.RemoveAll(r => t - r.t0 > 4f);

            if (_mesh != null)
            {
                for (int i = 0; i < _base.Length; i++)
                {
                    var b = _base[i];
                    // fade waves to zero at the very edge so the rim meets the bank cleanly
                    float edge = 1f - Mathf.Clamp01((new Vector2(b.x, b.z).magnitude - radius * 0.85f) / (radius * 0.15f));
                    _verts[i] = new Vector3(b.x, WaveHeight(b.x, b.z, t) * edge, b.z);
                }
                _mesh.vertices = _verts;
                _mesh.RecalculateNormals();
            }

            HandRipples(PlayerHands.Left, ref _lastLeft, ref _leftWasIn);
            HandRipples(PlayerHands.Right, ref _lastRight, ref _rightWasIn);
        }

        void HandRipples(Transform hand, ref Vector3 last, ref bool wasIn)
        {
            if (hand == null) return;
            Vector3 p = hand.position;
            bool inWater = IsUnderwater(p, 0.02f);
            float speed = (p - last).magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
            if (inWater != wasIn && InsideFootprint(p))
                Splash(p, Mathf.Clamp01(speed / 3f) * 0.6f + 0.1f);
            else if (inWater && speed > 0.6f && Random.value < Time.deltaTime * 6f)
                Ripple(p, 0.008f + speed * 0.004f);
            wasIn = inWater;
            last = p;
        }

        // ---------------------------------------------------------- physics

        void OnTriggerEnter(Collider other)
        {
            var rb = other.attachedRigidbody;
            if (rb != null) _bodies.Add(rb);
        }

        void OnTriggerExit(Collider other)
        {
            var rb = other.attachedRigidbody;
            if (rb != null) { _bodies.Remove(rb); _wasUnder.Remove(rb); }
        }

        /// <summary>How strongly an object floats (1 = neutral). Below 1 it sinks.</summary>
        float BuoyancyFor(Rigidbody rb)
        {
            if (rb.GetComponent<CrabAI>() != null) return 0.5f;   // crabs sink back to the bed
            var bucket = rb.GetComponent<Bucket>();
            if (bucket != null) return Mathf.Lerp(1.5f, 0.55f, bucket.Fill);
            var item = rb.GetComponent<SurvivalItem>();
            if (item != null)
            {
                switch (item.kind)
                {
                    case ItemKind.Flint: return 0.35f;
                    case ItemKind.Pot: return 0.6f;
                    case ItemKind.Mushroom: return 1.2f;
                    case ItemKind.Fish: return 1.05f; // dead fish drift near the surface
                }
            }
            return density;
        }

        void FixedUpdate()
        {
            _scratch.Clear();
            foreach (var rb in _bodies) _scratch.Add(rb);

            foreach (var rb in _scratch)
            {
                if (rb == null) { _bodies.Remove(rb); continue; }
                if (rb.isKinematic) continue;
                var fish = rb.GetComponent<FishAI>();
                if (fish != null && fish.IsAlive) continue;           // live fish swim on their own
                var grab = rb.GetComponent<XRGrabInteractable>();
                if (grab != null && grab.isSelected) continue;        // held or socketed

                // submerged fraction from the collider bounds against the local wave height
                Bounds b = default;
                bool any = false;
                foreach (var c in rb.GetComponentsInChildren<Collider>())
                {
                    if (c.isTrigger || !c.enabled) continue;
                    if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
                }
                if (!any) continue;
                Vector3 com = rb.worldCenterOfMass;
                if (!InsideFootprint(com)) continue;

                float surf = SurfaceHeightAt(com.x, com.z);
                float height = Mathf.Max(0.02f, b.size.y);
                float frac = Mathf.Clamp01((surf - b.min.y) / height);
                bool under = frac > 0.05f;

                _wasUnder.TryGetValue(rb, out bool wasUnder);
                if (under && !wasUnder && rb.linearVelocity.y < -0.8f)
                    Splash(com, Mathf.Clamp01(-rb.linearVelocity.y / 6f) * Mathf.Clamp(rb.mass, 0.3f, 2f));
                _wasUnder[rb] = under;
                if (!under) continue;

                // Archimedes: lift proportional to the submerged share, scaled per material
                rb.AddForce(-Physics.gravity * rb.mass * frac * BuoyancyFor(rb), ForceMode.Force);

                // water resistance (velocity relative to the current) and spin damping
                Vector3 rel = rb.linearVelocity - CurrentAt(com);
                rb.AddForce(-rel * waterDrag * frac * rb.mass, ForceMode.Force);
                rb.AddTorque(-rb.angularVelocity * angularDrag * frac * rb.mass, ForceMode.Force);

                // floating bodies leave the occasional ripple as they bob
                if (frac < 0.95f && Random.value < Time.fixedDeltaTime * 0.6f)
                    Ripple(com, 0.006f);
            }
        }
    }
}
