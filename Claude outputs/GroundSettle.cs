using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Forage
{
    /// <summary>
    /// Makes dropped items come to rest instead of rolling away forever.
    ///
    /// PhysX has friction (which stops sliding) but no ROLLING resistance, so
    /// any round collider on even a gentle slope keeps rolling — on this
    /// terrain the tinder, flint and the pot used to end up 20–60 m from camp.
    /// While an item is resting on static ground (terrain, rocks) and nobody is
    /// holding it, this applies rolling resistance and, once it is nearly still,
    /// puts it to sleep. In the air (thrown) or in water it does nothing, so
    /// throws and floating behave exactly as before.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class GroundSettle : MonoBehaviour
    {
        [Tooltip("How quickly spin dies away while touching the ground (per second).")]
        public float rollingResistance = 7f;
        [Tooltip("Extra slowing of travel while touching the ground (per second).")]
        public float groundDrag = 1.5f;
        public float settleSpeed = 0.15f;
        public float settleSpin = 1.5f;
        public float settleSeconds = 0.25f;

        static PhysicsMaterial _grip;
        /// <summary>Grippy, barely-bouncy material for hand-held props.</summary>
        public static PhysicsMaterial Grip
        {
            get
            {
                if (_grip == null)
                    _grip = new PhysicsMaterial("ItemGrip")
                    {
                        dynamicFriction = 0.8f,
                        staticFriction = 0.95f,
                        bounciness = 0.05f,
                        frictionCombine = PhysicsMaterialCombine.Maximum,
                        bounceCombine = PhysicsMaterialCombine.Minimum
                    };
                return _grip;
            }
        }

        Rigidbody _rb;
        XRGrabInteractable _grab;
        float _lastGroundContact = -1f;
        float _slowFor;

        public bool Grounded => Time.fixedTime - _lastGroundContact < 0.06f;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _grab = GetComponent<XRGrabInteractable>();
            // Overlap correction (e.g. the player's body teleporting onto a stick) used to fling
            // items tens of metres into the air; push them out gently instead, and cap speed
            // well above any real throw.
            _rb.maxDepenetrationVelocity = 2f;
            _rb.maxLinearVelocity = 25f;
            foreach (var c in GetComponents<Collider>())
                if (!c.isTrigger && c.sharedMaterial == null) c.sharedMaterial = Grip;
        }

        void OnCollisionStay(Collision c)
        {
            // static scenery only (terrain, rocks, logs) — not other loose items
            if (c.rigidbody == null && !c.collider.isTrigger) _lastGroundContact = Time.fixedTime;
        }

        void OnCollisionEnter(Collision c) => OnCollisionStay(c);

        void FixedUpdate()
        {
            if (_rb.isKinematic || (_grab != null && _grab.isSelected) || !Grounded)
            {
                _slowFor = 0f;
                return;
            }

            float dt = Time.fixedDeltaTime;
            _rb.angularVelocity *= Mathf.Exp(-rollingResistance * dt);
            Vector3 v = _rb.linearVelocity;
            Vector3 flat = new Vector3(v.x, 0f, v.z) * Mathf.Exp(-groundDrag * dt);
            _rb.linearVelocity = new Vector3(flat.x, v.y, flat.z);

            if (_rb.linearVelocity.magnitude < settleSpeed && _rb.angularVelocity.magnitude < settleSpin)
            {
                _slowFor += dt;
                if (_slowFor >= settleSeconds)
                {
                    _rb.linearVelocity = Vector3.zero;
                    _rb.angularVelocity = Vector3.zero;
                    _rb.Sleep();
                }
            }
            else _slowFor = 0f;
        }
    }
}
