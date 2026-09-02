using System.Collections.Generic;
using UnityEngine;
using Unity.AI.Navigation;

namespace Forage
{
    /// <summary>
    /// Bakes the NavMesh over the generated forest at startup, spawns the
    /// wildlife (rabbits, snakes, squirrels), keeps a registry of climbable
    /// trees, and hosts the editor-only Animal Tester panel.
    /// </summary>
    public class AnimalManager : MonoBehaviour
    {
        [Header("Population")]
        public int rabbits = 3;
        public int squirrels = 2;
        public int snakeZones = 7;      // ambush zones; snakes materialize when you wander near
        public float snakeZoneTriggerDist = 16f;
        public float snakeDespawnDist = 38f;

        [Header("Squirrel visual (assigned by scene builder)")]
        public GameObject squirrelModel;
        public RuntimeAnimatorController squirrelController;
        public Texture2D squirrelAlbedo;
        public Texture2D squirrelNormal;
        public float squirrelScale = 0.5f;

        static AnimalManager _instance;
        public static AnimalManager Instance
        {
            get
            {
                if (_instance == null) _instance = FindFirstObjectByType<AnimalManager>();
                return _instance;
            }
        }

        public bool NavMeshReady { get; private set; }

        readonly List<(Vector3 basePos, float height)> _trees = new List<(Vector3, float)>();

        void Awake() => _instance = this;

        void Start()
        {
            // ForestGenerator ran in Awake; geometry exists now
            BakeNavMesh();
            CollectTrees();
            SpawnAll();
        }

        void BakeNavMesh()
        {
            var go = new GameObject("NavMesh");
            var surface = go.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
            NavMeshReady = surface.navMeshData != null;
            var tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
            Debug.Log($"[Forage] NavMesh baked: {tri.vertices.Length} verts, {tri.indices.Length / 3} tris");
        }

        void CollectTrees()
        {
            var forest = ForestGenerator.Instance;
            if (forest == null) return;
            foreach (var col in forest.GetComponentsInChildren<CapsuleCollider>())
            {
                if (!col.name.Contains("Tree")) continue;
                _trees.Add((col.transform.position, Mathf.Min(col.height * 0.75f, 3.2f)));
            }
            Debug.Log($"[Forage] Climbable trees registered: {_trees.Count}");
        }

        public bool TryGetNearbyTree(Vector3 from, float maxDist, out Vector3 basePos, out float height)
        {
            basePos = Vector3.zero;
            height = 0f;
            float best = maxDist * maxDist;
            bool found = false;
            foreach (var (pos, h) in _trees)
            {
                float d = (pos - from).sqrMagnitude;
                if (d < best) { best = d; basePos = pos; height = h; found = true; }
            }
            return found;
        }

        class SnakeZone
        {
            public Vector3 center;
            public SnakeSpecies species;
            public Snake alive;
            public float cooldownUntil;
        }
        readonly List<SnakeZone> _snakeZones = new List<SnakeZone>();

        void SpawnAll()
        {
            if (!NavMeshReady)
            {
                Debug.LogWarning("[Forage] NavMesh missing; animals not spawned.");
                return;
            }
            for (int i = 0; i < rabbits; i++) SpawnRabbit(RandomSpawn(10f, 35f));
            for (int i = 0; i < squirrels; i++) SpawnSquirrel(RandomSpawn(8f, 30f));

            // snake ambush zones spread across the map, species assigned per zone
            var forest = ForestGenerator.Instance;
            var rand = new System.Random(forest != null ? forest.seed + 31337 : 1);
            for (int i = 0; i < snakeZones; i++)
            {
                float a = (float)rand.NextDouble() * Mathf.PI * 2f;
                float r = 12f + (float)rand.NextDouble() * 55f;
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                float y = forest != null ? forest.HeightAt(x, z) : 0f;
                _snakeZones.Add(new SnakeZone
                {
                    center = new Vector3(x, y, z),
                    species = SnakeSpecies.All[i % SnakeSpecies.All.Length]
                });
            }
        }

        void Update()
        {
            var gm = GameManager.Instance;
            if (gm == null || !NavMeshReady) return;
            Vector3 player = gm.PlayerPosition;

            foreach (var zone in _snakeZones)
            {
                float dist = Vector3.Distance(new Vector3(player.x, zone.center.y, player.z), zone.center);

                if (zone.alive == null)
                {
                    if (dist < snakeZoneTriggerDist && Time.time > zone.cooldownUntil)
                    {
                        // materialize away from the player's eyes, between them and the zone heart
                        Vector3 dir = (zone.center - player).normalized;
                        Vector3 spawnAt = zone.center + dir * 2f;
                        if (UnityEngine.AI.NavMesh.SamplePosition(spawnAt, out var hit, 5f, UnityEngine.AI.NavMesh.AllAreas))
                        {
                            zone.alive = SpawnSnake(hit.position, zone.species);
                        }
                        else zone.cooldownUntil = Time.time + 10f;
                    }
                }
                else if (dist > snakeDespawnDist &&
                         (zone.alive.state == Snake.State.Patrol || zone.alive.state == Snake.State.Leave))
                {
                    Destroy(zone.alive.gameObject);
                    zone.alive = null;
                    zone.cooldownUntil = Time.time + 20f;
                }
            }
        }

        Vector3 RandomSpawn(float minR, float maxR)
        {
            var forest = ForestGenerator.Instance;
            for (int tries = 0; tries < 20; tries++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                float r = Random.Range(minR, maxR);
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                if (forest != null && Vector2.Distance(new Vector2(x, z), forest.pondCenter) < forest.pondRadius + 2f)
                    continue;
                float y = forest != null ? forest.HeightAt(x, z) : 0f;
                if (UnityEngine.AI.NavMesh.SamplePosition(new Vector3(x, y, z), out var hit, 4f, UnityEngine.AI.NavMesh.AllAreas))
                    return hit.position;
            }
            return Vector3.zero;
        }

        public Rabbit SpawnRabbit(Vector3 pos)
        {
            var go = new GameObject("Rabbit");
            go.transform.position = pos;
            return go.AddComponent<Rabbit>();
        }

        public Snake SpawnSnake(Vector3 pos, SnakeSpecies species = null)
        {
            var go = new GameObject("Snake");
            go.transform.position = pos;
            var snake = go.AddComponent<Snake>();
            snake.Configure(species ?? SnakeSpecies.All[Random.Range(0, SnakeSpecies.All.Length)]);
            return snake;
        }

        public Squirrel SpawnSquirrel(Vector3 pos)
        {
            var go = new GameObject("Squirrel");
            go.transform.position = pos;
            var squirrel = go.AddComponent<Squirrel>();
            if (squirrelModel != null)
            {
                var model = Instantiate(squirrelModel);
                squirrel.SetVisual(model, squirrelController, squirrelScale, squirrelAlbedo, squirrelNormal);
            }
            else
            {
                // fallback: rabbit-ish body so it never spawns invisible
                squirrel.SetVisualFallback();
            }
            return squirrel;
        }

        // ------------------------------------------------------------------
        // Animal Tester panel (editor / simulator only)
        // ------------------------------------------------------------------

        bool _panelOpen = true;

        void OnGUI()
        {
            if (!Application.isEditor) return;
            GUILayout.BeginArea(new Rect(10, 200, 210, 420), GUI.skin.box);
            _panelOpen = GUILayout.Toggle(_panelOpen, "ANIMAL TESTER");
            if (_panelOpen)
            {
                var gm = GameManager.Instance;
                Vector3 near = gm != null ? gm.PlayerPosition + gm.playerHead.forward * 4f : Vector3.zero;
                near.y = ForestGenerator.Instance != null ? ForestGenerator.Instance.HeightAt(near.x, near.z) : 0f;

                GUILayout.Label("— Spawn near player —");
                if (GUILayout.Button("Spawn Rabbit")) SpawnRabbit(near);
                if (GUILayout.Button("Spawn Snake")) SpawnSnake(near);
                if (GUILayout.Button("Spawn Squirrel")) SpawnSquirrel(near);

                GUILayout.Label("— Force states (nearest) —");
                if (GUILayout.Button("Rabbit: Approach")) Nearest<Rabbit>()?.ForceState(Rabbit.State.Approach);
                if (GUILayout.Button("Rabbit: Flee")) Nearest<Rabbit>()?.ForceState(Rabbit.State.Flee);
                if (GUILayout.Button("Snake: Alert")) Nearest<Snake>()?.ForceState(Snake.State.Alert);
                if (GUILayout.Button("Snake: Strike")) Nearest<Snake>()?.ForceState(Snake.State.Strike);
                if (GUILayout.Button("Snake: Leave")) Nearest<Snake>()?.ForceState(Snake.State.Leave);
                if (GUILayout.Button("Squirrel: Climb tree")) Nearest<Squirrel>()?.ForceClimb();
            }
            GUILayout.EndArea();
        }

        T Nearest<T>() where T : Animal
        {
            var gm = GameManager.Instance;
            T best = null;
            float bestD = float.MaxValue;
            foreach (var a in FindObjectsByType<T>(FindObjectsSortMode.None))
            {
                float d = (a.transform.position - gm.PlayerPosition).sqrMagnitude;
                if (d < bestD) { bestD = d; best = a; }
            }
            return best;
        }
    }
}
