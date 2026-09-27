using UnityEngine;
using UnityEngine.AI;

namespace Forage
{
    /// <summary>
    /// Base for all wildlife: NavMesh movement, player awareness (distance and
    /// how fast the player is moving), and a simple gait bob so bodies feel alive.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public abstract class Animal : MonoBehaviour
    {
        [Header("Awareness")]
        public float noticeDistance = 7f;
        public float scarySpeed = 1.5f;   // player m/s that counts as "fast"

        protected NavMeshAgent agent;
        protected Transform body;         // visual root (bobbed by gait animation)
        float _gaitPhase;

        /// <summary>Horizontal (XZ) distance to the player — ground animals ignore head height.</summary>
        protected float PlayerDistance
        {
            get
            {
                if (GameManager.Instance == null) return 999f;
                Vector3 p = GameManager.Instance.PlayerPosition;
                return Vector2.Distance(
                    new Vector2(transform.position.x, transform.position.z),
                    new Vector2(p.x, p.z));
            }
        }

        protected float PlayerSpeed => GameManager.Instance != null ? GameManager.Instance.PlayerSpeed : 0f;

        /// <summary>
        /// The habitat zone this animal was placed in. Recorded because animals
        /// wander: once the simulation has run for a few seconds a squirrel may
        /// legitimately be standing at the pond, so its current position says
        /// nothing about whether habitat-based placement worked. This does.
        /// </summary>
        public Habitat.Zone SpawnZone { get; private set; }

        /// <summary>
        /// Whether the spawner actually found a spot in this species' habitat.
        /// AnimalManager.ZoneSpawn falls back to "anywhere walkable" after six
        /// failed attempts; that fallback is legitimate for gameplay but it is
        /// the spawner's decision, and the spawner is the only thing that knows
        /// it happened. Tests assert this flag rather than re-deriving a zone
        /// from the position, which would fail a correct fallback as if the
        /// placement rule were broken.
        /// </summary>
        public bool PlacementSatisfied { get; internal set; } = true;

        protected virtual void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            SpawnZone = Habitat.ZoneAt(transform.position);
        }

        protected void SetBody(Transform visualRoot) => body = visualRoot;

        /// <summary>Random reachable point within radius of a center.</summary>
        protected Vector3 RandomPoint(Vector3 center, float radius)
        {
            for (int i = 0; i < 8; i++)
            {
                var p = center + new Vector3(Random.Range(-radius, radius), 0, Random.Range(-radius, radius));
                if (NavMesh.SamplePosition(p, out var hit, 3f, NavMesh.AllAreas))
                    return hit.position;
            }
            return transform.position;
        }

        protected void FacePlayer()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            Vector3 to = gm.PlayerPosition - transform.position;
            to.y = 0;
            if (to.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(to), Time.deltaTime * 5f);
        }

        /// <summary>Speed-synced vertical bob + slight pitch — cheap but lively gait.</summary>
        protected void AnimateGait(float bobHeight, float frequency)
        {
            if (body == null) return;
            float speed01 = agent.enabled ? Mathf.Clamp01(agent.velocity.magnitude / Mathf.Max(agent.speed, 0.1f)) : 0f;
            _gaitPhase += Time.deltaTime * frequency * (0.3f + speed01);
            float bob = Mathf.Abs(Mathf.Sin(_gaitPhase)) * bobHeight * speed01;
            body.localPosition = new Vector3(0, bob, 0);
            body.localRotation = Quaternion.Euler(Mathf.Sin(_gaitPhase * 2f) * 4f * speed01, 0, 0);
        }
    }
}
