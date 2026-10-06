using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Forage
{
    /// <summary>
    /// A pond crab that behaves like one.
    ///
    /// The old crabs were a blob with three spikes that glided over the pond
    /// bed with no legs moving, could not be touched (no collider), and sat
    /// partly inside the terrain because they followed the analytic height
    /// field instead of the real mesh. Now:
    ///
    /// * Built with a shell, eight jointed legs, two claws and eye stalks.
    /// * Walks SIDEWAYS on the real pond floor (raycast), tilting with the slope,
    ///   legs stepping in alternating pairs; pauses and waves its claws.
    /// * Scuttles away from a hand that reaches near it; startle it a few
    ///   times and it buries itself in the silt until it feels safe.
    /// * Can be picked up — it struggles and may pinch (rumble + a sting).
    ///   Dropped, it falls, lands and walks back to the water.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CrabAI : MonoBehaviour
    {
        public enum State { Walking, Idle, Fleeing, Buried, Held, Falling }

        [Header("Pond (set by PondLife)")]
        public Vector2 pondCenter;
        public float pondRadius = 8f;
        public float size = 0.1f;

        [Header("Behaviour")]
        public float walkSpeed = 0.12f;
        public float fleeSpeed = 0.55f;
        public float fleeRadius = 0.6f;

        [Header("Read-only")]
        public State state = State.Walking;
        public int startles;
        public int pinches;

        Rigidbody _rb;
        XRGrabInteractable _grab;
        Transform[] _legs;
        float[] _legSide;
        Transform[] _claws;
        Vector3 _target;
        Vector3 _moveDir = Vector3.right;
        float _stateUntil, _legPhase, _bury, _nextPinch, _fallStart, _threatCooldown;
        bool _leftHand, _rightHand;
        System.Random _rand;
        readonly RaycastHit[] _hits = new RaycastHit[8];

        float R(float a, float b) => a + (float)_rand.NextDouble() * (b - a);
        public bool InPond => Vector2.Distance(new Vector2(transform.position.x, transform.position.z), pondCenter) < pondRadius * 0.9f;

        // ------------------------------------------------------------ build

        /// <summary>Builds a crab under <paramref name="parent"/>. Shell, legs, claws, eyes, collider, physics.</summary>
        public static CrabAI Build(Transform parent, int index, float s, Material shell, Material eyes)
        {
            var go = new GameObject("Crab" + index);
            go.transform.SetParent(parent, false);

            var body = new GameObject("Shell");
            body.transform.SetParent(go.transform, false);
            body.transform.localPosition = new Vector3(0f, s * 0.42f, 0f);
            body.AddComponent<MeshFilter>().sharedMesh =
                NatureFactory.SmoothBlob(s, 1, 0.06f, 700 + index, new Vector3(1.5f, 0.42f, 1.05f));
            body.AddComponent<MeshRenderer>().sharedMaterial = shell;

            var legs = new Transform[8];
            var sides = new float[8];
            for (int i = 0; i < 8; i++)
            {
                float side = i < 4 ? 1f : -1f;
                float z = (-0.45f + (i % 4) * 0.3f) * s;
                var pivot = new GameObject("Leg" + i).transform;
                pivot.SetParent(go.transform, false);
                pivot.localPosition = new Vector3(side * s * 1.05f, s * 0.42f, z);
                Vector3 upperDir = new Vector3(side, 0.55f, 0f).normalized;
                var upper = LowPolyFactory.AddMeshChild(pivot.gameObject,
                    LowPolyFactory.Cone(s * 0.07f, s * 0.55f, 4, s * 0.05f), shell, Vector3.zero);
                upper.transform.localRotation = Quaternion.FromToRotation(Vector3.up, upperDir);
                Vector3 knee = upperDir * s * 0.55f;
                Vector3 lowerDir = new Vector3(side * 0.35f, -1f, 0f).normalized;
                var lower = LowPolyFactory.AddMeshChild(pivot.gameObject,
                    LowPolyFactory.Cone(s * 0.05f, s * 0.78f, 4, 0f), shell, knee);
                lower.transform.localRotation = Quaternion.FromToRotation(Vector3.up, lowerDir);
                legs[i] = pivot;
                sides[i] = side;
            }

            var claws = new Transform[2];
            for (int c = 0; c < 2; c++)
            {
                float side = c == 0 ? 1f : -1f;
                var pivot = new GameObject("Claw" + c).transform;
                pivot.SetParent(go.transform, false);
                pivot.localPosition = new Vector3(side * s * 0.55f, s * 0.42f, s * 0.85f);
                Vector3 armDir = new Vector3(side * 0.45f, 0.25f, 1f).normalized;
                var arm = LowPolyFactory.AddMeshChild(pivot.gameObject,
                    LowPolyFactory.Cone(s * 0.09f, s * 0.6f, 5, s * 0.07f), shell, Vector3.zero);
                arm.transform.localRotation = Quaternion.FromToRotation(Vector3.up, armDir);
                var pincer = LowPolyFactory.AddMeshChild(pivot.gameObject,
                    NatureFactory.SmoothBlob(s * 0.2f, 1, 0.1f, 720 + index * 2 + c, new Vector3(0.8f, 0.55f, 1.5f)),
                    shell, armDir * s * 0.7f);
                pincer.transform.localRotation = Quaternion.LookRotation(armDir);
                claws[c] = pivot;
            }

            for (int e = 0; e < 2; e++)
            {
                float side = e == 0 ? 1f : -1f;
                var stalkPos = new Vector3(side * s * 0.25f, s * 0.6f, s * 0.7f);
                LowPolyFactory.AddMeshChild(go, LowPolyFactory.Cone(s * 0.03f, s * 0.22f, 4, s * 0.025f), shell, stalkPos);
                LowPolyFactory.AddMeshChild(go, NatureFactory.SmoothBlob(s * 0.07f, 1, 0f, 740 + e, Vector3.one),
                    eyes != null ? eyes : shell, stalkPos + Vector3.up * s * 0.24f);
            }

            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(s * 2.6f, s * 0.7f, s * 2.2f);
            col.center = new Vector3(0f, s * 0.42f, s * 0.15f);

            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;          // walks by itself; dynamic only while falling after a drop
            rb.useGravity = true;
            rb.mass = 0.3f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.maxLinearVelocity = GroundSettle.MaxSpeed;
            rb.maxDepenetrationVelocity = 2f;

            var grab = go.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.Kinematic;
            grab.useDynamicAttach = true;
            grab.throwOnDetach = true;

            var ai = go.AddComponent<CrabAI>();
            ai.size = s;
            ai._legs = legs;
            ai._legSide = sides;
            ai._claws = claws;
            return ai;
        }

        // ------------------------------------------------------------ lifecycle

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _grab = GetComponent<XRGrabInteractable>();
            _rand = new System.Random(GetInstanceID());
            if (_grab != null)
            {
                _grab.selectEntered.AddListener(OnGrabbed);
                _grab.selectExited.AddListener(OnReleased);
            }
        }

        void Start()
        {
            _target = RandomFloorPoint();
            _legPhase = R(0f, 10f);
            SnapToGround(1f);
        }

        Vector3 RandomFloorPoint()
        {
            float a = R(0f, Mathf.PI * 2f), r = R(0f, pondRadius * 0.7f);
            return new Vector3(pondCenter.x + Mathf.Cos(a) * r, transform.position.y, pondCenter.y + Mathf.Sin(a) * r);
        }

        void OnGrabbed(SelectEnterEventArgs args)
        {
            state = State.Held;
            var it = args.interactorObject;
            _leftHand = it.handedness == UnityEngine.XR.Interaction.Toolkit.Interactors.InteractorHandedness.Left;
            _rightHand = it.handedness == UnityEngine.XR.Interaction.Toolkit.Interactors.InteractorHandedness.Right;
            if (!_leftHand && !_rightHand) _leftHand = _rightHand = true;
            _nextPinch = Time.time + R(0.4f, 1.6f);
            if (WaterBody.Instance != null && WaterBody.Instance.IsUnderwater(transform.position))
                WaterBody.Instance.Splash(transform.position, 0.25f);
        }

        void OnReleased(SelectExitEventArgs args)
        {
            if (_grab != null && _grab.isSelected) return; // passed to the other hand
            state = State.Falling;
            _fallStart = Time.time;
            _rb.isKinematic = false;
            _rb.useGravity = true;
        }

        // ------------------------------------------------------------ simulation

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            switch (state)
            {
                case State.Held: Held(); Animate(2.6f, 1f); return;
                case State.Falling: Falling(); Animate(1.8f, 0.6f); return;
            }

            _threatCooldown -= dt;
            if (state != State.Buried && _threatCooldown <= 0f && HandNearby())
            {
                _threatCooldown = 0.8f;
                startles++;
                if (startles >= 3 && InPond)
                {
                    state = State.Buried;
                    _stateUntil = Time.time + R(6f, 10f);
                    startles = 0;
                    if (WaterBody.Instance != null) WaterBody.Instance.Ripple(transform.position, 0.008f);
                }
                else
                {
                    state = State.Fleeing;
                    _stateUntil = Time.time + R(1.2f, 2f);
                    Vector3 away = transform.position - NearestHand();
                    away.y = 0f;
                    if (away.sqrMagnitude < 1e-4f) away = transform.right;
                    _target = ClampToPond(transform.position + away.normalized * 1.3f);
                }
            }

            float speed = 0f;
            switch (state)
            {
                case State.Walking:
                    if (!InPond) _target = ClampToPond(transform.position);   // on land: head for the water
                    speed = InPond ? walkSpeed : walkSpeed * 1.6f;
                    if (Flat(_target - transform.position).magnitude < 0.12f)
                    {
                        state = State.Idle;
                        _stateUntil = Time.time + R(1f, 3.5f);
                    }
                    break;
                case State.Idle:
                    if (Time.time > _stateUntil) { state = State.Walking; _target = RandomFloorPoint(); }
                    break;
                case State.Fleeing:
                    speed = fleeSpeed;
                    if (Time.time > _stateUntil || Flat(_target - transform.position).magnitude < 0.1f)
                    {
                        state = State.Walking;
                        _target = RandomFloorPoint();
                    }
                    break;
                case State.Buried:
                    if (Time.time > _stateUntil && !HandNearby()) state = State.Walking;
                    break;
            }

            if (speed > 0f)
            {
                Vector3 to = Flat(_target - transform.position);
                if (to.sqrMagnitude > 1e-4f) _moveDir = Vector3.Slerp(_moveDir, to.normalized, dt * 4f).normalized;
                transform.position += _moveDir * speed * dt;
            }

            _bury = Mathf.MoveTowards(_bury, state == State.Buried ? 1f : 0f, dt * 1.5f);
            SnapToGround(dt * 8f);
            Animate(speed > 0f ? speed * 40f : 0.6f, state == State.Idle ? 0.35f : (speed > 0f ? 1f : 0.15f));
        }

        void Held()
        {
            if (Time.time < _nextPinch) return;
            _nextPinch = Time.time + R(1.5f, 3.5f);
            if (_rand.NextDouble() > 0.55) return;
            pinches++;
            Haptics.Pulse(0.9f, 0.18f, _leftHand, _rightHand);
            ProceduralAudio.PlayAt(transform.position, ProceduralAudio.FlintClick(), 0.4f);
            var gm = GameManager.Instance;
            if (gm != null && gm.vitals != null) gm.vitals.Damage(1f, "crab-pinch");
            ForageEvents.RaiseHint("crab-pinch");
        }

        void Falling()
        {
            // XRI restores the grab-time kinematic flag on release; keep it dynamic while it falls
            if (_rb.isKinematic && Time.time - _fallStart < 0.3f) _rb.isKinematic = false;
            bool settled = _rb.linearVelocity.magnitude < 0.08f && Time.time - _fallStart > 0.25f;
            if (settled || Time.time - _fallStart > 6f)
            {
                _rb.isKinematic = true;
                state = State.Walking;
                _target = InPond ? RandomFloorPoint() : ClampToPond(transform.position);
                if (WaterBody.Instance != null && InPond) WaterBody.Instance.Ripple(transform.position, 0.006f);
            }
        }

        // ------------------------------------------------------------ helpers

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        Vector3 ClampToPond(Vector3 p)
        {
            Vector2 d = new Vector2(p.x, p.z) - pondCenter;
            float max = pondRadius * 0.72f;
            if (d.magnitude > max) d = d.normalized * max;
            return new Vector3(pondCenter.x + d.x, p.y, pondCenter.y + d.y);
        }

        Vector3 NearestHand()
        {
            Vector3 best = transform.position + Vector3.one * 99f;
            foreach (var h in new[] { PlayerHands.Left, PlayerHands.Right })
                if (h != null && (h.position - transform.position).sqrMagnitude < (best - transform.position).sqrMagnitude)
                    best = h.position;
            return best;
        }

        bool HandNearby()
        {
            Vector3 h = NearestHand();
            return (h - transform.position).magnitude < fleeRadius;
        }

        /// <summary>Stand on the REAL pond floor (terrain collider), tilted to its slope.</summary>
        void SnapToGround(float blend)
        {
            Vector3 p = transform.position;
            int n = Physics.RaycastNonAlloc(p + Vector3.up * 0.6f, Vector3.down, _hits, 3f, ~0, QueryTriggerInteraction.Ignore);
            float bestY = float.NegativeInfinity;
            Vector3 normal = Vector3.up;
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (h.rigidbody != null || h.collider.isTrigger) continue;   // scenery only, never ourselves or loose items
                if (h.point.y > bestY) { bestY = h.point.y; normal = h.normal; }
            }
            if (float.IsNegativeInfinity(bestY))
                bestY = ForestGenerator.Instance != null ? ForestGenerator.Instance.HeightAt(p.x, p.z) : p.y;

            float y = bestY - _bury * size * 0.75f;   // buried: only the eyes peek out
            transform.position = new Vector3(p.x, Mathf.Lerp(p.y, y, Mathf.Clamp01(blend)), p.z);

            // walks sideways: its local X follows the travel direction
            Vector3 fwd = Vector3.Cross(_moveDir, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = transform.forward;
            var look = Quaternion.LookRotation(Vector3.ProjectOnPlane(-fwd, normal).normalized, normal);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, Mathf.Clamp01(blend));
        }

        void Animate(float rate, float amp)
        {
            if (_legs == null) return;
            _legPhase += Time.deltaTime * rate;
            for (int i = 0; i < _legs.Length; i++)
            {
                // alternating pairs, the way crabs actually step
                float phase = _legPhase + (i % 2 == 0 ? 0f : Mathf.PI) + (i % 4) * 0.4f;
                float lift = Mathf.Sin(phase) * 22f * amp;
                float swing = Mathf.Cos(phase) * 14f * amp;
                _legs[i].localRotation = Quaternion.Euler(swing, 0f, _legSide[i] * lift);
            }
            if (_claws != null)
            {
                float open = state == State.Held ? Mathf.Abs(Mathf.Sin(Time.time * 9f)) * 30f
                           : state == State.Idle ? Mathf.Sin(Time.time * 2.5f) * 18f : 4f;
                for (int c = 0; c < _claws.Length; c++)
                    _claws[c].localRotation = Quaternion.Euler(-open * 0.5f, (c == 0 ? -1f : 1f) * open, 0f);
            }
        }
    }
}
