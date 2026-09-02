using UnityEngine;

namespace Forage
{
    /// <summary>
    /// Builds the camp at startup: stone-ring fire pit, fireboard + drill,
    /// and gatherable sticks / tinder scattered around the clearing (a few
    /// damp sticks near the pond teach the dry-wood lesson).
    /// Runs in Start so the ForestGenerator (Awake) has already produced terrain.
    /// </summary>
    public class CampsiteBuilder : MonoBehaviour
    {
        [Header("Counts")]
        public int drySticks = 8;
        public int wetSticks = 4;
        public int tinderBundles = 4;

        public FirePit FirePit { get; private set; }

        void Start()
        {
            var forest = ForestGenerator.Instance;
            if (forest == null)
            {
                Debug.LogError("[Forage] CampsiteBuilder needs a ForestGenerator in the scene.");
                return;
            }

            BuildFirePit(forest);
            ScatterGatherables(forest);
            ScatterMushrooms(forest);
        }

        void ScatterMushrooms(ForestGenerator forest)
        {
            var assets = ForageAssets.Instance;
            var rand = new System.Random(forest.seed + 6060);

            // (species, fact, poisonous, cap material, funnel shape)
            var species = new (string name, string fact, bool poison, Material cap, bool funnel)[]
            {
                ("Field Mushroom",
                 "Brown gills under a smooth cream cap and a pleasant mushroomy smell. A classic safe find — but always check for pale deadly lookalikes before eating.",
                 false, assets.capBrown, false),
                ("Chanterelle",
                 "Golden and funnel-shaped, with blunt ridges (not true gills) running down the stem, and a faint apricot smell. A prized safe forage.",
                 false, assets.capYellow, true),
                ("Fly Agaric",
                 "The bright red warning cap. Eating it causes sweating, nausea and delirium. In the wild, bright colors often mean 'leave me alone'.",
                 true, assets.capRed, false),
                ("Death Cap",
                 "Pale, greenish-white and innocent-looking — this one causes most fatal mushroom poisonings in the world. If you are not 100% certain, never eat a pale wild mushroom.",
                 true, assets.capPale, false),
            };
            // weighted: more field mushrooms, and the deceptive death cap is common enough to matter
            int[] weights = { 8, 5, 6, 7 };

            for (int s = 0; s < species.Length; s++)
            {
                for (int i = 0; i < weights[s]; i++)
                {
                    // mushrooms grow in the woods: bias toward denser forest
                    for (int tries = 0; tries < 15; tries++)
                    {
                        float a = (float)rand.NextDouble() * Mathf.PI * 2f;
                        float r = 6f + (float)rand.NextDouble() * 55f;
                        float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                        if (UnityEngine.Vector2.Distance(new Vector2(x, z), forest.pondCenter) < forest.pondRadius + 1f) continue;
                        if ((float)rand.NextDouble() > forest.DensityAt(x, z) + 0.35f) continue;

                        var sp = species[s];
                        var shroom = ItemFactory.Mushroom(forest.seed + 600 + s * 100 + i,
                            sp.name, sp.fact, sp.poison, sp.cap, sp.funnel);
                        shroom.transform.position = new Vector3(x, forest.HeightAt(x, z) + 0.05f, z);
                        shroom.transform.rotation = Quaternion.Euler(0, (float)rand.NextDouble() * 360f, 0);
                        break;
                    }
                }
            }
        }

        void BuildFirePit(ForestGenerator forest)
        {
            var assets = ForageAssets.Instance;
            var pitPos = new Vector3(1.8f, forest.HeightAt(1.8f, -2.2f), -2.2f);

            var pit = new GameObject("FirePit");
            pit.transform.position = pitPos;

            // stone ring
            var rand = new System.Random(forest.seed + 777);
            int stones = 9;
            for (int i = 0; i < stones; i++)
            {
                float a = i * Mathf.PI * 2f / stones;
                var stone = NatureFactory.Rock(forest.seed + 700 + i, assets.stone, 0.13f + (float)rand.NextDouble() * 0.05f);
                stone.transform.SetParent(pit.transform, false);
                stone.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.45f, 0.05f, Mathf.Sin(a) * 0.45f);
                Object.Destroy(stone.GetComponent<SphereCollider>()); // ring itself doesn't need physics
            }

            // charred base disc
            LowPolyFactory.AddMeshChild(pit, LowPolyFactory.Cone(0.4f, 0.03f, 12), assets.charredWood, Vector3.up * 0.01f);

            // drop-in trigger for fuel
            var trigger = pit.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.42f;
            trigger.center = Vector3.up * 0.25f;

            FirePit = pit.AddComponent<FirePit>();

            // fireboard next to the pit
            var board = new GameObject("Fireboard");
            board.transform.position = pitPos + new Vector3(0.85f, 0f, 0.15f);
            LowPolyFactory.AddMeshChild(board, LowPolyFactory.Cone(0.16f, 0.05f, 8, 0.14f), assets.stickWood, Vector3.zero);
            var boardCol = board.AddComponent<BoxCollider>();
            boardCol.size = new Vector3(0.34f, 0.06f, 0.34f);
            boardCol.center = Vector3.up * 0.03f;

            var boardCenter = new GameObject("BoardCenter").transform;
            boardCenter.SetParent(board.transform, false);
            boardCenter.localPosition = Vector3.up * 0.06f;

            // drill stick resting on the board
            var drillGo = ItemFactory.DrillStick();
            drillGo.transform.position = board.transform.position + new Vector3(-0.1f, 0.15f, 0.1f);
            drillGo.transform.rotation = Quaternion.Euler(0, 0, 80f);
            var drill = drillGo.AddComponent<FireDrill>();
            drill.firePit = FirePit;
            drill.boardCenter = boardCenter;

            // camp pot for the water mechanic
            var pot = ItemFactory.Pot();
            pot.transform.position = pitPos + new Vector3(-0.9f, 0.25f, 0.3f);

            // two flint stones: strike them together hard near the pit for sparks
            for (int i = 0; i < 2; i++)
            {
                var flint = ItemFactory.FlintStone(forest.seed + 950 + i);
                flint.transform.position = pitPos + new Vector3(0.55f + i * 0.18f, 0.2f, -0.5f);
            }
        }

        void ScatterGatherables(ForestGenerator forest)
        {
            var rand = new System.Random(forest.seed + 4242);

            Vector3 CampSpot(float minR, float maxR)
            {
                float a = (float)rand.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Lerp(minR, maxR, (float)rand.NextDouble());
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                return new Vector3(x, forest.HeightAt(x, z) + 0.1f, z);
            }

            for (int i = 0; i < drySticks; i++)
            {
                var stick = ItemFactory.Stick(forest.seed + 100 + i, wet: false);
                stick.transform.position = CampSpot(2.5f, forest.campRadius + 6f);
                stick.transform.rotation = Quaternion.Euler(0, (float)rand.NextDouble() * 360f, 0);
            }

            // damp sticks near the pond — a trap that teaches "find dry wood"
            for (int i = 0; i < wetSticks; i++)
            {
                var stick = ItemFactory.Stick(forest.seed + 200 + i, wet: true);
                float a = (float)rand.NextDouble() * Mathf.PI * 2f;
                float r = forest.pondRadius + 1.2f + (float)rand.NextDouble() * 2f;
                float x = forest.pondCenter.x + Mathf.Cos(a) * r;
                float z = forest.pondCenter.y + Mathf.Sin(a) * r;
                stick.transform.position = new Vector3(x, forest.HeightAt(x, z) + 0.1f, z);
            }

            for (int i = 0; i < tinderBundles; i++)
            {
                var tinder = ItemFactory.TinderBundle(forest.seed + 300 + i);
                tinder.transform.position = CampSpot(2f, forest.campRadius + 4f);
            }
        }
    }
}
