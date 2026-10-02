using System.Collections.Generic;
using UnityEngine;

namespace Forage
{
    /// <summary>
    /// Life inside the pond: fish (each driven by its own <see cref="FishAI"/>:
    /// swimming, struggling when caught, flopping on land) and crabs
    /// scuttling on the bottom — visible through the transparent water.
    /// </summary>
    public class PondLife : MonoBehaviour
    {
        public Material fishMat;
        public Material crabMat;
        public Vector2 pondCenter;
        public float pondRadius = 8f;
        public int fishCount = 7;
        public int crabCount = 3;

        class Swimmer
        {
            public Transform tf;
            public Vector3 target;
            public float speed;
            public float wigglePhase;
            public bool onFloor; // crab
        }

        readonly List<Swimmer> _critters = new List<Swimmer>();

        /// <summary>Follows the generated pond rather than a hard-coded height.</summary>
        float SurfaceY => ForestGenerator.Instance != null
            ? ForestGenerator.Instance.WaterLevel - 0.05f
            : -0.5f;

        void Start()
        {
            var rand = new System.Random(12345);

            for (int i = 0; i < fishCount; i++)
            {
                // fish simulate themselves (FishAI) — PondLife only spawns them
                var fish = BuildFish(i, rand);
                fish.transform.position = RandomPoint(rand, false);
                fish.GetComponent<Rigidbody>().position = fish.transform.position;
            }

            for (int i = 0; i < crabCount; i++)
            {
                var crab = BuildCrab(i, rand);
                _critters.Add(new Swimmer
                {
                    tf = crab.transform,
                    target = RandomPoint(rand, true),
                    speed = 0.1f + (float)rand.NextDouble() * 0.08f,
                    wigglePhase = (float)rand.NextDouble() * 10f,
                    onFloor = true
                });
                crab.transform.position = RandomPoint(rand, true);
            }
        }

        Vector3 RandomPoint(System.Random rand, bool onFloor)
        {
            float a = (float)rand.NextDouble() * Mathf.PI * 2f;
            float r = (float)rand.NextDouble() * pondRadius * 0.7f;
            float x = pondCenter.x + Mathf.Cos(a) * r;
            float z = pondCenter.y + Mathf.Sin(a) * r;
            float floor = ForestGenerator.Instance != null ? ForestGenerator.Instance.HeightAt(x, z) : -2f;
            float y = onFloor
                ? floor + 0.05f
                : Mathf.Lerp(floor + 0.25f, SurfaceY - 0.15f, (float)rand.NextDouble());
            return new Vector3(x, y, z);
        }

        void Update()
        {
            var rand = new System.Random((int)(Time.time * 1000) + 7);
            foreach (var c in _critters)
            {
                Vector3 to = c.target - c.tf.position;
                if (to.magnitude < 0.3f)
                {
                    c.target = RandomPoint(rand, c.onFloor);
                    continue;
                }

                Vector3 dir = to.normalized;
                c.tf.position += dir * c.speed * Time.deltaTime;

                if (c.onFloor)
                {
                    // crabs hug the floor and shuffle sideways
                    float floor = ForestGenerator.Instance.HeightAt(c.tf.position.x, c.tf.position.z);
                    c.tf.position = new Vector3(c.tf.position.x, floor + 0.05f, c.tf.position.z);
                    c.tf.rotation = Quaternion.LookRotation(new Vector3(dir.z, 0, -dir.x));
                }
                else
                {
                    // fish: face travel direction with a tail-wiggle yaw
                    float wiggle = Mathf.Sin(Time.time * 7f + c.wigglePhase) * 9f;
                    c.tf.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0, wiggle, 0);
                    // keep under the surface
                    if (c.tf.position.y > SurfaceY - 0.1f)
                        c.tf.position += Vector3.down * 0.2f * Time.deltaTime;
                }
            }
        }

        GameObject BuildFish(int i, System.Random rand)
        {
            var go = new GameObject("Fish" + i);
            go.transform.SetParent(transform, false);
            float s = 0.1f + (float)rand.NextDouble() * 0.1f;

            var body = new GameObject("Body");
            body.transform.SetParent(go.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh =
                NatureFactory.SmoothBlob(s, 1, 0.05f, 500 + i, new Vector3(0.45f, 0.6f, 1.6f));
            body.AddComponent<MeshRenderer>().sharedMaterial = fishMat;

            var tail = new GameObject("Tail");
            tail.transform.SetParent(go.transform, false);
            tail.transform.localPosition = new Vector3(0, 0, -s * 1.6f);
            tail.transform.localRotation = Quaternion.Euler(0, 90, 0);
            tail.AddComponent<MeshFilter>().sharedMesh = LowPolyFactory.Cone(s * 0.45f, s * 0.7f, 4);
            tail.AddComponent<MeshRenderer>().sharedMaterial = fishMat;

            // fishing: reach into the water and grab it! A body-shaped capsule
            // (the old sphere was ~2x the fish and snagged on the pond floor)
            var col = go.AddComponent<CapsuleCollider>();
            col.direction = 2;
            col.radius = s * 0.75f;
            col.height = s * 3.6f;
            col.center = new Vector3(0f, 0f, -s * 0.3f);
            // a real dynamic body at all times — never kinematic. XRI restores the
            // pre-grab kinematic flag on release, which is what used to leave a
            // dropped fish frozen in mid-air.
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.useGravity = false;
            rb.mass = 0.4f + s * 2f;
            var grab = go.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            grab.movementType = UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable.MovementType.VelocityTracking;
            grab.throwOnDetach = true;
            grab.useDynamicAttach = true;
            var item = go.AddComponent<SurvivalItem>();
            item.kind = ItemKind.Fish;
            go.AddComponent<FishItem>();
            var cook = go.AddComponent<Cookable>();
            cook.cookSeconds = 12f;

            var ai = go.AddComponent<FishAI>();
            ai.pondCenter = pondCenter;
            ai.pondRadius = pondRadius;
            ai.cruiseSpeed = 0.35f + (float)rand.NextDouble() * 0.35f;
            return go;
        }

        /// <summary>Kept for API compatibility: fish are simulated by FishAI, not by PondLife.</summary>
        public void OnFishCaught(Transform fish)
        {
            _critters.RemoveAll(c => c.tf == fish);
        }

        GameObject BuildCrab(int i, System.Random rand)
        {
            var go = new GameObject("Crab" + i);
            go.transform.SetParent(transform, false);
            float s = 0.08f + (float)rand.NextDouble() * 0.05f;

            var body = new GameObject("Body");
            body.transform.SetParent(go.transform, false);
            body.transform.localPosition = Vector3.up * s * 0.3f;
            body.AddComponent<MeshFilter>().sharedMesh =
                NatureFactory.SmoothBlob(s, 1, 0.08f, 700 + i, new Vector3(1.5f, 0.45f, 1f));
            body.AddComponent<MeshRenderer>().sharedMaterial = crabMat;

            // stubby legs: three small cones per side
            for (int leg = 0; leg < 6; leg++)
            {
                float side = leg < 3 ? 1f : -1f;
                var l = new GameObject("Leg");
                l.transform.SetParent(go.transform, false);
                l.transform.localPosition = new Vector3(side * s * 1.2f, s * 0.25f, (leg % 3 - 1) * s * 0.7f);
                l.transform.localRotation = Quaternion.Euler(0, 0, side * 125f);
                l.AddComponent<MeshFilter>().sharedMesh = LowPolyFactory.Cone(s * 0.12f, s * 0.9f, 4);
                l.AddComponent<MeshRenderer>().sharedMaterial = crabMat;
            }
            return go;
        }
    }
}
