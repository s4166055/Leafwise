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
        Fish,        // caught from the pond; cook it!
        Bucket,      // carries pond water back to camp
        Skewer       // long green stick: spear a fish and roast it over the fire
    }

    /// <summary>Marks a grabbable survival item and its properties.</summary>
    public class SurvivalItem : MonoBehaviour
    {
        public ItemKind kind;
        public bool isWet;

        /// <summary>
        /// How far below the ground counts as "lost out of the world". Was 10 m:
        /// by then the item had visibly dropped through the floor and vanished.
        /// The real terrain mesh and the analytic height differ by a few cm, so
        /// anything 2 m under it has definitely gone through.
        /// </summary>
        const float LostBelowGround = 2f;

        /// <summary>Keep this far inside the terrain's edge (the map is a square, worldSize wide).</summary>
        const float EdgeMargin = 3f;

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
        Rigidbody _body;
        UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable _grab;

        /// <summary>
        /// Recover an item that has fallen out of the world.
        ///
        /// Placement raycasts onto the real terrain collider, so items should
        /// not start inside the mesh, but a knock down a steep seam, a physics
        /// tunnelling glitch or a fling off the edge of the map can still lose
        /// one. Several items are unique: lose the pot and boiling water becomes
        /// impossible, so the session is quietly unwinnable with no feedback.
        /// So put it back where it last sat safely on the ground.
        ///
        /// The first version recorded the "last good" position whenever the
        /// item was anywhere above the ground, including in mid-air and past the
        /// edge of the map. An item flung off the map was "recovered" to a point
        /// in the air over the void, fell again, and looped forever (the bucket,
        /// both skewers and a branch did exactly that). A position now counts as
        /// good only if the item is inside the map, near the ground, nearly still
        /// and not in a hand.
        /// </summary>
        void FixedUpdate()
        {
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + 0.25f;

            var forest = ForestGenerator.Instance;
            if (forest == null) return;
            // fetched lazily: builders add SurvivalItem before or after the body depending on the item
            if (_body == null) _body = GetComponent<Rigidbody>();
            if (_grab == null) _grab = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            if (transform.parent != null && _body != null && _body.isKinematic) return;   // on a skewer, hook, etc.

            var p = transform.position;
            float ground = forest.HeightAt(p.x, p.z);
            float edge = forest.worldSize * 0.5f - EdgeMargin;
            bool insideMap = Mathf.Abs(p.x) < edge && Mathf.Abs(p.z) < edge;

            if (p.y > ground - 0.5f)
            {
                bool held = _grab != null && _grab.isSelected;
                bool still = _body == null || _body.isKinematic || _body.linearVelocity.sqrMagnitude < 0.25f;
                if (insideMap && !held && still && p.y < ground + 1.5f)
                    _lastGoodPosition = p;   // resting on or just above the surface
                return;
            }

            // Inside the map, a little under the analytic surface is normal (it is a few cm off
            // the real mesh, and the pond bed dips). Past the edge there is no floor at all.
            if (insideMap && p.y > ground - LostBelowGround) return;

            if (_body != null && !_body.isKinematic)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }

            Vector3 to;
            if (_lastGoodPosition != Vector3.zero) to = _lastGoodPosition + Vector3.up * 0.1f;
            else
            {
                float x = Mathf.Clamp(p.x, -edge, edge), z = Mathf.Clamp(p.z, -edge, edge);
                to = new Vector3(x, forest.HeightAt(x, z) + 0.3f, z);
            }
            if (_body != null) _body.position = to;
            transform.position = to;

            RescueCount++;
            Debug.LogWarning($"[Forage] {kind} '{name}' fell out of the world and was recovered to " +
                             $"{transform.position}. This is a placement or physics bug - see RescueCount.");
        }
    }
}
