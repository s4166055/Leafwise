using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Forage
{
    /// <summary>
    /// A living fish. It is a real dynamic Rigidbody the whole time, so it
    /// never freezes where it was grabbed:
    ///
    /// * Swimming — wanders the pond below the surface, tail beating with
    ///   speed, darts away from hands and splashes, nudged by the current.
    /// * Held     — struggles: the body thrashes, the tail whips, the holding
    ///   controller buzzes; held over the water it can wriggle free.
    /// * Flopping — out of the water it flops: hops, twists and slaps the
    ///   ground, tending back toward the pond when it is close. It tires and,
    ///   after a minute or so out of water, dies.
    /// * Back in water (dropped, thrown or flopped in) — it swims away again.
    /// * Dead / cooked — an ordinary food item (FishItem / Cookable).
    ///
    /// Root cause of the old "fish stays where it was grabbed" bug: the fish
    /// was kinematic while swimming, and XRGrabInteractable restores the
    /// pre-grab kinematic and gravity flags on release — so a dropped fish
    /// hung in mid-air. Now nothing is ever kinematic, and this component
    /// re-asserts gravity / kinematic state every physics step it isn't held.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class FishAI : MonoBehaviour
    {
        public enum State { Swimming, Held, Flopping, Dead }

        [Header("Pond (set by PondLife)")]
        public Vector2 pondCenter;
        public float pondRadius = 8f;

        [Header("Swimming")]
        public float cruiseSpeed = 0.5f;
        public float dartSpeed = 2.2f;
        public float fleeRadius = 0.9f;

        [Header("Held")]
        [Tooltip("Chance per thrash, while held low over the water, that it slips out of your grip.")]
        [Range(0, 1)] public float slipChance = 0.07f;
        [Tooltip("Seconds after being grabbed before it can slip free.")]
        public float slipGraceSeconds = 1.5f;

        [Header("Out of water")]
        public float secondsToDie = 75f;
        public float flopImpulse = 2.1f;

        [Header("Read-only")]
        public State state = State.Swimming;
        [Range(0, 1)] public float stamina = 1f;
        public float secondsOutOfWater;
        public int flops;

        public bool IsAlive => state != State.Dead;

        Rigidbody _rb;
        XRGrabInteractable _grab;
        Cookable _cook;
        Transform _body, _tail;
        Vector3 _target;
        float _tailPhase, _nextFlop, _dartUntil, _nextStruggle, _retarget, _grabbedAt;
        Vector3 _dartDir;
        bool _leftHand, _rightHand;
        System.Random _rand;

        WaterBody Water => WaterBody.Instance;

        float SurfaceY(Vector3 p)
        {
            if (Water != null) return Water.SurfaceHeightAt(p.x, p.z);
            return ForestGenerator.Instance != null ? ForestGenerator.Instance.WaterLevel : 0f;
        }

        float FloorY(Vector3 p) => ForestGenerator.Instance != null ? ForestGenerator.Instance.HeightAt(p.x, p.z) : p.y - 1f;

        bool InWater(Vector3 p)
        {
            bool inside = Vector2.Distance(new Vector2(p.x, p.z), pondCenter) < pondRadius * 0.92f;
            return inside && p.y < SurfaceY(p) + 0.02f;
        }

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _grab = GetComponent<XRGrabInteractable>();
            _cook = GetComponent<Cookable>();
            _body = transform.Find("Body");
            _tail = transform.Find("Tail");
            _rand = new System.Random(GetInstanceID());

            _rb.isKinematic = false;
            _rb.useGravity = false;
            _rb.linearDamping = 1.5f;
            _rb.angularDamping = 2f;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            if (_grab != null)
            {
                _grab.selectEntered.AddListener(OnGrabbed);
                _grab.selectExited.AddListener(OnReleased);
            }
        }

        void Start()
        {
            _rb.position = transform.position; // spawner placed us after Awake
            _rb.rotation = transform.rotation;
            _target = RandomSwimPoint();
            _tailPhase = (float)_rand.NextDouble() * 10f;
        }

        float R(float a, float b) => a + (float)_rand.NextDouble() * (b - a);

        Vector3 RandomSwimPoint()
        {
            float a = R(0f, Mathf.PI * 2f), r = R(0f, pondRadius * 0.7f);
            float x = pondCenter.x + Mathf.Cos(a) * r, z = pondCenter.y + Mathf.Sin(a) * r;
            var p = new Vector3(x, 0f, z);
            float floor = FloorY(p), surf = SurfaceY(p);
            p.y = Mathf.Lerp(floor + 0.25f, surf - 0.15f, R(0.1f, 0.9f));
            if (p.y > surf - 0.12f) p.y = surf - 0.12f;
            return p;
        }

        // ------------------------------------------------------------ grab events

        void OnGrabbed(SelectEnterEventArgs args)
        {
            if (state == State.Dead) return;
            var it = args.interactorObject;
            _leftHand = it.handedness == UnityEngine.XR.Interaction.Toolkit.Interactors.InteractorHandedness.Left;
            _rightHand = it.handedness == UnityEngine.XR.Interaction.Toolkit.Interactors.InteractorHandedness.Right;
            if (!_leftHand && !_rightHand) _leftHand = _rightHand = true;

            bool fromWater = state == State.Swimming;
            state = State.Held;
            _grabbedAt = Time.time;
            _nextStruggle = Time.time + 0.15f;
            if (fromWater)
            {
                if (Water != null) Water.Splash(transform.position, 0.45f);
                ForageEvents.RaiseSignal("fish-caught");
            }
            Haptics.Pulse(0.6f, 0.2f, _leftHand, _rightHand);
        }

        void OnReleased(SelectExitEventArgs args)
        {
            if (state == State.Dead) { ApplyPhysicsFlags(); return; }
            if (_grab != null && _grab.isSelected) return; // passed to the other hand
            state = InWater(transform.position) ? State.Swimming : State.Flopping;
            _nextFlop = Time.time + R(0.25f, 0.6f);
            ApplyPhysicsFlags();
        }

        /// <summary>XRI restores pre-grab flags on release; make sure physics is what this state needs.</summary>
        void ApplyPhysicsFlags()
        {
            if (_grab != null && _grab.isSelected) return;
            _rb.isKinematic = false;
            bool water = InWater(transform.position);
            _rb.useGravity = !(water && state == State.Swimming);
            _rb.linearDamping = water ? 1.5f : 0.05f;
            _rb.angularDamping = water ? 2f : 0.3f;
        }

        // ------------------------------------------------------------ simulation

        void FixedUpdate()
        {
            bool held = _grab != null && _grab.isSelected;
            if (held)
            {
                if (state != State.Dead) state = State.Held;
                return;
            }

            // cooking or a long time out of the water ends it
            if (state != State.Dead && ((_cook != null && (_cook.cooked || _cook.progress > 0.05f)) || stamina <= 0f))
                Die();

            ApplyPhysicsFlags();
            if (state == State.Dead) return;

            bool water = InWater(transform.position);
            if (state == State.Flopping && water)
            {
                state = State.Swimming;
                _target = RandomSwimPoint();
                _dartDir = (new Vector3(_target.x, transform.position.y, _target.z) - transform.position).normalized;
                _dartUntil = Time.time + 1.2f;
                if (Water != null) Water.Splash(transform.position, 0.5f);
                ForageEvents.RaiseSignal("fish-escaped");
                ForageEvents.RaiseHint("fish-escaped");
                ApplyPhysicsFlags();
            }
            else if (state == State.Swimming && !water)
            {
                // pushed or leapt out of the water
                state = State.Flopping;
                _nextFlop = Time.time + 0.3f;
                ApplyPhysicsFlags();
            }

            if (state == State.Swimming) Swim();
            else if (state == State.Flopping) Flop();
        }

        void Swim()
        {
            Vector3 p = transform.position;
            stamina = Mathf.MoveTowards(stamina, 1f, Time.fixedDeltaTime * 0.05f); // recovers in water
            secondsOutOfWater = 0f;

            // flee hands that reach in, and splashes
            Vector3 flee = Vector3.zero;
            foreach (var hand in new[] { PlayerHands.Left, PlayerHands.Right })
            {
                if (hand == null) continue;
                Vector3 d = p - hand.position;
                if (d.magnitude < fleeRadius && InWater(hand.position + Vector3.down * 0.05f))
                    flee += d.normalized * (fleeRadius - d.magnitude);
            }
            if (flee.sqrMagnitude > 0.0001f && Time.time > _dartUntil)
            {
                flee.y *= 0.3f;
                _dartDir = flee.normalized;
                _dartUntil = Time.time + R(0.4f, 0.8f);
                _retarget = 0f;
            }

            _retarget -= Time.fixedDeltaTime;
            if ((p - _target).magnitude < 0.35f || _retarget <= 0f)
            {
                _target = RandomSwimPoint();
                _retarget = R(4f, 9f);
            }

            Vector3 desired;
            if (Time.time < _dartUntil) desired = _dartDir * dartSpeed;
            else
            {
                desired = (_target - p).normalized * cruiseSpeed;
                // idle pauses and bursts read as alive rather than a conveyor belt
                desired *= 0.65f + 0.35f * Mathf.Sin(Time.time * 0.7f + _tailPhase);
            }
            if (Water != null) desired += Water.CurrentAt(p) * 0.5f;

            // stay below the surface and above the floor
            float surf = SurfaceY(p), floor = FloorY(p);
            if (p.y > surf - 0.1f) desired.y = Mathf.Min(desired.y, -0.3f);
            if (p.y < floor + 0.15f) desired.y = Mathf.Max(desired.y, 0.3f);
            // turn back from the bank
            Vector2 fromC = new Vector2(p.x - pondCenter.x, p.z - pondCenter.y);
            if (fromC.magnitude > pondRadius * 0.78f)
                desired += -new Vector3(fromC.x, 0f, fromC.y).normalized * 0.6f;

            Vector3 v = _rb.linearVelocity;
            _rb.AddForce((desired - v) * 4f * _rb.mass, ForceMode.Force);

            // face where it's going, with a body-wide wag
            Vector3 flat = v.sqrMagnitude > 0.0025f ? v : transform.forward;
            float wag = Mathf.Sin(_tailPhase) * (6f + v.magnitude * 6f);
            var look = Quaternion.LookRotation(Vector3.Lerp(flat.normalized, new Vector3(flat.x, 0f, flat.z).normalized, 0.6f));
            _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, look * Quaternion.Euler(0f, wag, 0f), Time.fixedDeltaTime * 6f));
            _rb.angularVelocity = Vector3.zero;
        }

        void Flop()
        {
            Vector3 p = transform.position;
            secondsOutOfWater += Time.fixedDeltaTime;
            stamina = Mathf.Clamp01(1f - secondsOutOfWater / secondsToDie);

            // resting on the terrain, or settled on a rock/log/anything else
            bool grounded = (p.y - FloorY(p) < 0.14f && Mathf.Abs(_rb.linearVelocity.y) < 0.6f) ||
                            _rb.linearVelocity.magnitude < 0.05f;
            if (!grounded || Time.time < _nextFlop) return;

            // weaker and less frequent as it tires
            float vigour = Mathf.Lerp(0.35f, 1f, stamina);
            _nextFlop = Time.time + Mathf.Lerp(2.4f, 0.45f, stamina) * R(0.7f, 1.3f);
            flops++;

            Vector3 side = Random.insideUnitSphere; side.y = 0f;
            // real fish flop mostly UP with a little sideways skip — not a sprint
            Vector3 dir = side.normalized * R(0.08f, 0.25f);
            // close to the pond, flops tend to carry it toward the water (fish "know" the slope)
            Vector2 toPond = pondCenter - new Vector2(p.x, p.z);
            float distToEdge = toPond.magnitude - pondRadius * 0.92f;
            if (distToEdge < 3f && Random.value < 0.6f)
                dir += new Vector3(toPond.x, 0f, toPond.y).normalized * 0.3f;

            _rb.AddForce((Vector3.up * R(0.8f, 1.2f) + dir) * flopImpulse * vigour * _rb.mass, ForceMode.Impulse);
            _rb.AddTorque(new Vector3(R(-1f, 1f), R(-1f, 1f), R(-1f, 1f)) * 0.06f * vigour * _rb.mass, ForceMode.Impulse);
            ProceduralAudio.PlayAt(p, ProceduralAudio.Crunch(), 0.18f * vigour); // wet slap
            if (Water != null && Water.InsideFootprint(p, -0.6f)) Water.Ripple(p, 0.01f);
        }

        void Die()
        {
            state = State.Dead;
            stamina = 0f;
            if (_body != null) _body.localRotation = Quaternion.identity;
            if (_tail != null) _tail.localRotation = Quaternion.Euler(0, 90, 0);
            ForageEvents.RaiseSignal("fish-died");
        }

        // ------------------------------------------------------------ animation

        void Update()
        {
            if (state == State.Dead) return;
            float speed = _rb != null ? _rb.linearVelocity.magnitude : 0f;
            float rate;
            float amp;
            switch (state)
            {
                case State.Held:
                    // thrashing in the hand, in bursts
                    float burst = Mathf.PerlinNoise(Time.time * 1.7f, _tailPhase) > 0.45f ? 1f : 0.3f;
                    rate = 26f * burst * Mathf.Lerp(0.5f, 1f, stamina);
                    amp = 35f * burst * Mathf.Lerp(0.4f, 1f, stamina);
                    Struggle(burst);
                    break;
                case State.Flopping:
                    rate = 14f * Mathf.Lerp(0.3f, 1f, stamina);
                    amp = 28f * Mathf.Lerp(0.2f, 1f, stamina);
                    break;
                default:
                    rate = 6f + speed * 10f;
                    amp = 14f + speed * 8f;
                    break;
            }
            _tailPhase += Time.deltaTime * rate;
            float s = Mathf.Sin(_tailPhase);
            if (_tail != null) _tail.localRotation = Quaternion.Euler(0f, 90f + s * amp, 0f);
            if (_body != null) _body.localRotation = Quaternion.Euler(0f, -s * amp * 0.35f,
                state == State.Swimming ? 0f : s * amp * 0.25f);
        }

        void Struggle(float burst)
        {
            if (Time.time < _nextStruggle) return;
            _nextStruggle = Time.time + R(0.35f, 0.9f);
            if (burst < 1f) return;
            Haptics.Pulse(Mathf.Lerp(0.15f, 0.45f, stamina), 0.08f, _leftHand, _rightHand);

            // held low over the water while still strong, it can slip out of your grip
            Vector3 p = transform.position;
            bool overWater = Vector2.Distance(new Vector2(p.x, p.z), pondCenter) < pondRadius * 0.9f &&
                             p.y < SurfaceY(p) + 0.6f;
            bool graceOver = Time.time - _grabbedAt > slipGraceSeconds;
            if (overWater && graceOver && stamina > 0.6f && Random.value < slipChance &&
                _grab != null && _grab.interactionManager != null)
            {
                _grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)_grab);
                if (Water != null) Water.Splash(p, 0.4f);
            }
            // drains a little stamina; held in air it is out of water too
            secondsOutOfWater += 0.4f;
            stamina = Mathf.Clamp01(1f - secondsOutOfWater / secondsToDie);
        }
    }
}
