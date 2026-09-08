using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Forage
{
    /// <summary>Builds grabbable survival items from procedural meshes.</summary>
    public static class ItemFactory
    {
        /// <summary>Adds Rigidbody + XRGrabInteractable + SurvivalItem to a prop root.</summary>
        public static SurvivalItem MakeGrabbable(GameObject go, ItemKind kind, float mass = 0.5f)
        {
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var grab = go.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            grab.useDynamicAttach = true;
            grab.throwOnDetach = true;

            var item = go.AddComponent<SurvivalItem>();
            item.kind = kind;
            return item;
        }

        public static GameObject Stick(int seed, bool wet = false)
        {
            var rand = new System.Random(seed);
            var go = new GameObject("Stick");
            float len = 0.45f + (float)rand.NextDouble() * 0.25f;
            float r = 0.022f + (float)rand.NextDouble() * 0.012f;
            var mesh = LowPolyFactory.Cone(r, len, 5, r * 0.8f);
            var vis = LowPolyFactory.AddMeshChild(go, mesh, ForageAssets.Instance.stickWood, Vector3.zero);
            vis.transform.localRotation = Quaternion.Euler(0, 0, 90); // lie along X

            var col = go.AddComponent<CapsuleCollider>();
            col.direction = 0;
            col.radius = r * 1.4f;
            col.height = len;
            col.center = new Vector3(-len * 0.5f, 0, 0);

            var item = MakeGrabbable(go, ItemKind.Stick, 0.4f);
            item.isWet = wet;
            return go;
        }

        public static GameObject TinderBundle(int seed)
        {
            var go = new GameObject("Tinder");
            LowPolyFactory.AddMeshChild(go,
                LowPolyFactory.Blob(0.09f, 1, 0.45f, seed, new Vector3(1.2f, 0.6f, 1f)),
                ForageAssets.Instance.tinderStraw, Vector3.up * 0.05f);
            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.1f;
            col.center = Vector3.up * 0.05f;
            MakeGrabbable(go, ItemKind.Tinder, 0.1f);
            return go;
        }

        public static GameObject Branch(int seed)
        {
            var rand = new System.Random(seed);
            var go = new GameObject("Branch");
            float len = 1.6f + (float)rand.NextDouble() * 0.5f;
            float r = 0.032f;
            var vis = new GameObject("BranchMesh");
            vis.transform.SetParent(go.transform, false);
            vis.transform.localRotation = Quaternion.Euler(0, 0, 90);
            vis.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothTube(r, r * 0.7f, len, 5, 2, 0.06f, seed, 2f);
            vis.AddComponent<MeshRenderer>().sharedMaterial = ForageAssets.Instance.stickWood;

            var col = go.AddComponent<CapsuleCollider>();
            col.direction = 0;
            col.radius = r * 1.5f;
            col.height = len;
            col.center = new Vector3(-len * 0.5f, 0, 0);
            MakeGrabbable(go, ItemKind.Branch, 0.9f);
            return go;
        }

        public static GameObject LeafBundle(int seed)
        {
            var go = new GameObject("LeafBundle");
            var rand = new System.Random(seed);
            var mat = ForageAssets.Instance.tinderStraw;
            var leafGreen = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            leafGreen.SetColor("_BaseColor", new Color(0.3f, 0.5f, 0.22f));
            for (int i = 0; i < 3; i++)
            {
                var blob = new GameObject("Leaves" + i);
                blob.transform.SetParent(go.transform, false);
                blob.transform.localPosition = new Vector3(((float)rand.NextDouble() - 0.5f) * 0.12f, 0.05f + i * 0.03f,
                    ((float)rand.NextDouble() - 0.5f) * 0.12f);
                blob.AddComponent<MeshFilter>().sharedMesh =
                    NatureFactory.SmoothBlob(0.11f, 1, 0.25f, seed + i, new Vector3(1.3f, 0.5f, 1.1f));
                blob.AddComponent<MeshRenderer>().sharedMaterial = leafGreen;
            }
            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.15f;
            col.center = Vector3.up * 0.08f;
            MakeGrabbable(go, ItemKind.LeafBundle, 0.25f);
            return go;
        }

        /// <summary>Berry cluster: red = safe, white = toxic (a real-world rule of thumb).</summary>
        public static GameObject BerryCluster(int seed, bool safe)
        {
            var go = new GameObject(safe ? "RedBerries" : "WhiteBerries");
            var rand = new System.Random(seed);
            var assets = ForageAssets.Instance;
            var berryMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            berryMat.SetColor("_BaseColor", safe ? new Color(0.75f, 0.12f, 0.18f) : new Color(0.92f, 0.92f, 0.85f));
            berryMat.SetFloat("_Smoothness", 0.6f);

            LowPolyFactory.AddMeshChild(go, LowPolyFactory.Cone(0.008f, 0.09f, 4), assets.mushroomStem, Vector3.zero);
            for (int i = 0; i < 6; i++)
            {
                var berry = new GameObject("Berry");
                berry.transform.SetParent(go.transform, false);
                berry.transform.localPosition = new Vector3(
                    ((float)rand.NextDouble() - 0.5f) * 0.07f,
                    0.08f + ((float)rand.NextDouble() - 0.3f) * 0.05f,
                    ((float)rand.NextDouble() - 0.5f) * 0.07f);
                berry.AddComponent<MeshFilter>().sharedMesh = NatureFactory.SmoothBlob(0.016f, 1, 0.03f, seed + i, Vector3.one);
                berry.AddComponent<MeshRenderer>().sharedMaterial = berryMat;
            }

            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.08f;
            col.center = Vector3.up * 0.08f;
            MakeGrabbable(go, ItemKind.Mushroom, 0.05f); // behaves like forage food (fox will steal it too)

            var shroom = go.AddComponent<Mushroom>();
            shroom.speciesName = safe ? "Wild Raspberries" : "White Baneberry";
            shroom.poisonous = !safe;
            shroom.fact = safe
                ? "Red aggregate berries like raspberries are among the safest wild foods. Sweet smell, familiar shape — a forager's friend."
                : "WHITE berries are almost always toxic — baneberry can stop a heart. Rule of thumb: white and yellow berries, leave them be.";
            return go;
        }

        public static GameObject FlintStone(int seed)
        {
            var go = new GameObject("FlintStone");
            var mesh = NatureFactory.SmoothBlob(0.055f, 1, 0.18f, seed, new Vector3(1.1f, 0.75f, 0.9f));
            var vis = new GameObject("Stone");
            vis.transform.SetParent(go.transform, false);
            vis.AddComponent<MeshFilter>().sharedMesh = mesh;
            vis.AddComponent<MeshRenderer>().sharedMaterial = ForageAssets.Instance.stone;

            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.055f;

            MakeGrabbable(go, ItemKind.Flint, 0.5f);
            go.AddComponent<FlintStone>();
            return go;
        }

        public static GameObject Mushroom(int seed, string species, string fact, bool poisonous,
            Material cap, bool funnelShape = false)
        {
            var rand = new System.Random(seed);
            float scale = 0.8f + (float)rand.NextDouble() * 0.6f;
            var go = new GameObject("Mushroom_" + species.Replace(" ", ""));
            var assets = ForageAssets.Instance;

            float stemH = 0.07f * scale;
            LowPolyFactory.AddMeshChild(go, LowPolyFactory.Cone(0.013f * scale, stemH, 6, 0.011f * scale),
                assets.mushroomStem, Vector3.zero);

            if (funnelShape)
            {
                // chanterelle-style funnel: narrow at the stem, wide at the top
                LowPolyFactory.AddMeshChild(go, LowPolyFactory.Cone(0.012f * scale, 0.045f * scale, 8, 0.055f * scale),
                    cap, new Vector3(0, stemH * 0.9f, 0));
            }
            else
            {
                // smooth UV-mapped cap so textured caps (fly agaric spots) render correctly
                var capGo = new GameObject("Cap");
                capGo.transform.SetParent(go.transform, false);
                capGo.transform.localPosition = new Vector3(0, stemH, 0);
                capGo.AddComponent<MeshFilter>().sharedMesh =
                    NatureFactory.SmoothBlob(0.05f * scale, 1, 0.1f, seed, new Vector3(1f, 0.55f, 1f));
                capGo.AddComponent<MeshRenderer>().sharedMaterial = cap;
            }

            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.06f * scale;
            col.center = new Vector3(0, stemH * 0.7f, 0);

            MakeGrabbable(go, ItemKind.Mushroom, 0.05f);

            var shroom = go.AddComponent<Mushroom>();
            shroom.speciesName = species;
            shroom.fact = fact;
            shroom.poisonous = poisonous;
            return go;
        }

        public static GameObject Pot()
        {
            var go = new GameObject("Pot");
            var assets = ForageAssets.Instance;
            // open-top pot: walls as a truncated cone, dark base disc
            LowPolyFactory.AddMeshChild(go, LowPolyFactory.Cone(0.085f, 0.1f, 10, 0.095f), assets.potMetal, Vector3.zero);
            LowPolyFactory.AddMeshChild(go, LowPolyFactory.Cone(0.083f, 0.008f, 10), assets.charredWood, new Vector3(0, 0.008f, 0));
            // simple handle arch
            var handle = LowPolyFactory.AddMeshChild(go, LowPolyFactory.Cone(0.008f, 0.22f, 5), assets.potMetal, new Vector3(-0.11f, 0.1f, 0));
            handle.transform.localRotation = Quaternion.Euler(0, 0, -90f);

            var col = go.AddComponent<CapsuleCollider>();
            col.direction = 1;
            col.radius = 0.1f;
            col.height = 0.14f;
            col.center = new Vector3(0, 0.06f, 0);

            MakeGrabbable(go, ItemKind.Pot, 0.8f);
            go.AddComponent<CookingPot>();
            return go;
        }

        public static GameObject DrillStick()
        {
            var go = new GameObject("DrillStick");
            var mesh = LowPolyFactory.Cone(0.02f, 0.5f, 6, 0.008f);
            LowPolyFactory.AddMeshChild(go, mesh, ForageAssets.Instance.stickWood, Vector3.zero);

            // tip marker at the pointed end (top of cone -> we drill tip-down)
            var tip = new GameObject("DrillTip");
            tip.transform.SetParent(go.transform, false);
            tip.transform.localPosition = new Vector3(0, 0.5f, 0);

            var col = go.AddComponent<CapsuleCollider>();
            col.direction = 1;
            col.radius = 0.03f;
            col.height = 0.52f;
            col.center = new Vector3(0, 0.25f, 0);

            MakeGrabbable(go, ItemKind.DrillStick, 0.3f);
            return go;
        }
    }
}
