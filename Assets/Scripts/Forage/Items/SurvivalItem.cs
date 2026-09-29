using UnityEngine;

namespace Forage
{
    public enum ItemKind
    {
        Stick,
        Tinder,
        DrillStick,
        Pot,
        Mushroom,
        Branch,      // shelter building
        LeafBundle,  // shelter building
        Flint,       // strike two together for sparks
        Fish         // caught from the pond; cook it!
    }

    /// <summary>Marks a grabbable survival item and its properties.</summary>
    public class SurvivalItem : MonoBehaviour
    {
        public ItemKind kind;
        public bool isWet;

        /// <summary>How far below the ground counts as "lost out of the world".</summary>
        const float LostBelowGround = 10f;

        /// <summary>
        /// How many items have had to be rescued this session. The net keeps the
        /// game playable, but a rescue always means a placement or physics bug,
        /// and a silent fix would hide it - so this is exposed for the tests,
        /// which assert it stays at zero.
        /// </summary>
        public static int RescueCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRescueCount() => RescueCount = 0;

        Vector3 _lastGoodPosition;
        float _nextCheck;

        /// <summary>
        /// Recover an item that has fallen out of the world.
        ///
        /// Placement now raycasts onto the real terrain collider, so items
        /// should not start inside the mesh - but a knock down a steep seam, or
        /// a physics tunnelling glitch, can still lose one. That matters
        /// because several items are unique: lose the pot and boiling water
        /// becomes impossible, so the session is quietly unwinnable with no
        /// feedback. Rather than let that happen, put it back where it last sat
        /// safely on the ground.
        /// </summary>
        void FixedUpdate()
        {
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + 0.5f;

            var forest = ForestGenerator.Instance;
            if (forest == null) return;

            var p = transform.position;
            float ground = forest.HeightAt(p.x, p.z);

            if (p.y > ground - 0.5f)
            {
                _lastGoodPosition = p;   // resting on or above the surface
                return;
            }

            if (p.y > ground - LostBelowGround) return;   // sunk a little; let physics sort it

            var body = GetComponent<Rigidbody>();
            if (body != null) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }

            transform.position = _lastGoodPosition != Vector3.zero
                ? _lastGoodPosition
                : new Vector3(p.x, ground + 0.3f, p.z);

            RescueCount++;
            Debug.LogWarning($"[Forage] {kind} '{name}' fell out of the world and was recovered to " +
                             $"{transform.position}. This is a placement or physics bug - see RescueCount.");
        }
    }
}
