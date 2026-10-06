using UnityEngine;

namespace Forage
{
    /// <summary>
    /// Life inside the pond: fish (each driven by its own <see cref="FishAI"/>:
    /// swimming, struggling when caught, flopping on land) and crabs (each
    /// driven by a <see cref="CrabAI"/>: walking sideways on the pond bed,
    /// fleeing, burying, pinching) — visible through the transparent water.
    /// </summary>
    public class PondLife : MonoBehaviour
    {
        public Material fishMat;
        public Material crabMat;
        public Vector2 pondCenter;
        public float pondRadius = 8f;
        public int fishCount = 7;
        public int crabCount = 3;

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

            var eyes = ForageAssets.Instance != null ? ForageAssets.Instance.charredWood : null;
            for (int i = 0; i < crabCount; i++)
            {
                // crabs simulate themselves too (CrabAI): walk on the real pond
                // floor, flee from hands, bury, pinch when picked up
                float s = 0.07f + (float)rand.NextDouble() * 0.04f;
                var crab = CrabAI.Build(transform, i, s, crabMat, eyes);
                crab.pondCenter = pondCenter;
                crab.pondRadius = pondRadius;
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
            rb.maxLinearVelocity = GroundSettle.MaxSpeed;   // a far grab must not fire it across the map
            rb.maxDepenetrationVelocity = 2f;
            var grab = go.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            grab.movementType = UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable.MovementType.VelocityTracking;
            grab.throwOnDetach = true;
            grab.useDynamicAttach = true;
            var item = go.AddComponent<SurvivalItem>();
            item.kind = ItemKind.Fish;
            go.AddComponent<FishItem>();
            var cook = go.AddComponent<Cookable>();
            cook.cookSeconds = 12f;
            cook.requiresSkewer = true;   // roast it on a skewer over the fire, not in the ashes

            var ai = go.AddComponent<FishAI>();
            ai.pondCenter = pondCenter;
            ai.pondRadius = pondRadius;
            ai.cruiseSpeed = 0.35f + (float)rand.NextDouble() * 0.35f;
            return go;
        }

        /// <summary>Kept for API compatibility: fish (FishAI) and crabs (CrabAI) simulate themselves.</summary>
        public void OnFishCaught(Transform fish) { }
    }
}
