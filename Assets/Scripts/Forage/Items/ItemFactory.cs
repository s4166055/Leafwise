using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Forage
{
    /// <summary>Builds grabbable survival items from procedural meshes.</summary>
    public static class ItemFactory
    {
        static PhysicsMaterial _grippyGround;

        /// <summary>
        /// High-friction, zero-bounce surface for loose items.
        ///
        /// A stick is a capsule collider lying on its side, which to PhysX is a
        /// log. With the default material and no damping it rolls down the
        /// terrain's slopes and never stops, so gathered firewood wanders off
        /// across the forest before you can drop it in the pit.
        /// </summary>
        static PhysicsMaterial GrippyGround =>
            _grippyGround != null ? _grippyGround : _grippyGround = new PhysicsMaterial("ForageItem")
            {
                dynamicFriction = 0.85f,
                staticFriction = 0.95f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Maximum,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };

        /// <summary>Adds Rigidbody + XRGrabInteractable + SurvivalItem to a prop root.</summary>
        public static SurvivalItem MakeGrabbable(GameObject go, ItemKind kind, float mass = 0.5f)
        {
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // Damping is what actually stops a dropped item wandering. The
            // defaults are 0 linear and 0.05 angular: almost nothing, so an
            // elongated collider keeps rolling on any slope. These values still
            // let a thrown item fly properly; they just bleed off the roll once
            // it lands.
            rb.linearDamping = 0.6f;
            rb.angularDamping = 4f;

            // Let physics put the item to sleep quickly once it has settled.
            rb.sleepThreshold = 0.05f;

            foreach (var c in go.GetComponentsInChildren<Collider>())
                if (!c.isTrigger) c.sharedMaterial = GrippyGround;

            // Damping alone still lets round colliders creep downhill (no rolling
            // resistance in PhysX); GroundSettle stops that and caps fling speed.
            go.AddComponent<GroundSettle>();

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

            // A flat-bottomed box, not a capsule: a capsule on its side is a log
            // to PhysX and, even with damping and GroundSettle, sticks on the
            // slope north of camp kept creeping 1-1.6 m downhill. A box has a
            // face on the ground and stays put. Slightly wider than tall so it
            // lands flat.
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(len, r * 2f, r * 2.8f);
            col.center = new Vector3(-len * 0.5f, 0, 0);

            var item = MakeGrabbable(go, ItemKind.Stick, 0.4f);
            item.isWet = wet;
            return go;
        }

        public static GameObject TinderBundle(int seed)
        {
            var go = new GameObject("Tinder");
            // Bigger (was 0.09) and a brighter golden straw: the old pale clump
            // matched the dry-grass ground and testers could not find any.
            LowPolyFactory.AddMeshChild(go,
                LowPolyFactory.Blob(0.12f, 1, 0.45f, seed, new Vector3(1.2f, 0.6f, 1f)),
                TinderMaterial, Vector3.up * 0.06f);
            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.13f;
            col.center = Vector3.up * 0.06f;
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

            var col = go.AddComponent<BoxCollider>();   // flat-bottomed, like the stick (see above)
            col.size = new Vector3(len, r * 2f, r * 3f);
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

        static Material _tinderMaterial;

        /// <summary>Golden straw that reads against green and brown ground. Same shader as the scene material.</summary>
        static Material TinderMaterial
        {
            get
            {
                if (_tinderMaterial == null)
                {
                    _tinderMaterial = new Material(ForageAssets.Instance.tinderStraw) { name = "Tinder (runtime)" };
                    _tinderMaterial.SetColor("_BaseColor", new Color(0.98f, 0.82f, 0.36f));
                }
                return _tinderMaterial;
            }
        }

        static Material _flintMaterial;

        /// <summary>
        /// Real flint is dark and glassy, which also sets it apart from the pale
        /// ring stones it sits beside. Same shader as the rock material, so no
        /// new variant has to survive shader stripping on the Quest build.
        /// </summary>
        static Material FlintMaterial
        {
            get
            {
                if (_flintMaterial == null)
                {
                    _flintMaterial = new Material(ForageAssets.Instance.stone) { name = "Flint (runtime)" };
                    _flintMaterial.SetColor("_BaseColor", new Color(0.16f, 0.17f, 0.21f));
                    _flintMaterial.SetFloat("_Smoothness", 0.82f);
                }
                return _flintMaterial;
            }
        }

        public static GameObject FlintStone(int seed)
        {
            var go = new GameObject("FlintStone");
            // Bigger than it was (0.055): at arm's length an 11 cm grey ball on
            // grey ground next to a ring of grey rocks was simply not findable.
            const float radius = 0.07f;
            var mesh = NatureFactory.SmoothBlob(radius, 1, 0.18f, seed, new Vector3(1.1f, 0.75f, 0.9f));
            var vis = new GameObject("Stone");
            vis.transform.SetParent(go.transform, false);
            vis.AddComponent<MeshFilter>().sharedMesh = mesh;
            vis.AddComponent<MeshRenderer>().sharedMaterial = FlintMaterial;

            var col = go.AddComponent<SphereCollider>();
            col.radius = radius;

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
            go.GetComponent<Rigidbody>().isKinematic = true;
            go.GetComponent<XRGrabInteractable>().movementType = XRBaseInteractable.MovementType.Kinematic;

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

        /// <summary>
        /// Low-poly wooden bucket with two metal bands and an arched handle.
        /// Held by the handle (it hangs and tilts with your wrist), seated on
        /// the bucket stand by its base. See <see cref="Forage.Bucket"/>.
        /// </summary>
        public static GameObject Bucket()
        {
            var go = new GameObject("Bucket");
            var assets = ForageAssets.Instance;

            const int N = 12;
            const float outerBottom = 0.105f, outerTop = 0.136f, height = Forage.Bucket.RimY;
            float innerBottom = Forage.Bucket.InnerBottomR, innerTop = Forage.Bucket.InnerTopR, floorY = Forage.Bucket.FloorY;

            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            }
            Vector3 P(float r, float y, int k)
            {
                float a = k * Mathf.PI * 2f / N;
                return new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            }
            for (int k = 0; k < N; k++)
            {
                // outer staves (face outward)
                Vector3 b0 = P(outerBottom, 0f, k), b1 = P(outerBottom, 0f, k + 1);
                Vector3 t0 = P(outerTop, height, k), t1 = P(outerTop, height, k + 1);
                Tri(b0, t0, b1); Tri(b1, t0, t1);
                // inner wall (faces inward)
                Vector3 ib0 = P(innerBottom, floorY, k), ib1 = P(innerBottom, floorY, k + 1);
                Vector3 it0 = P(innerTop, height, k), it1 = P(innerTop, height, k + 1);
                Tri(ib0, ib1, it0); Tri(ib1, it1, it0);
                // rim (faces up)
                Tri(it0, t1, t0); Tri(it0, it1, t1);
                // outside base (faces down) and inner floor (faces up)
                Tri(Vector3.zero, b0, b1);
                Vector3 fc = new Vector3(0f, floorY, 0f);
                Tri(fc, ib1, ib0);
            }
            var body = LowPolyFactory.AddMeshChild(go, LowPolyFactory.Faceted(verts, tris, "BucketBody"),
                assets.stickWood, Vector3.zero);
            body.name = "BucketBody";

            // two metal hoops
            foreach (float y in new[] { 0.035f, 0.2f })
            {
                float r0 = Mathf.Lerp(outerBottom, outerTop, y / height) + 0.004f;
                float r1 = Mathf.Lerp(outerBottom, outerTop, (y + 0.028f) / height) + 0.004f;
                var hoop = LowPolyFactory.AddMeshChild(go, LowPolyFactory.Cone(r0, 0.028f, N, r1), assets.potMetal,
                    new Vector3(0f, y, 0f));
                hoop.name = "Hoop";
            }

            // arched wire handle (double-sided tube along a half ellipse)
            var hv = new System.Collections.Generic.List<Vector3>();
            var ht = new System.Collections.Generic.List<int>();
            const int seg = 12, sides = 5;
            const float tubeR = 0.007f, spanX = outerTop + 0.006f, rise = 0.12f;
            Vector3 Arc(float u) => new Vector3(Mathf.Cos(Mathf.PI * u) * spanX, height - 0.03f + Mathf.Sin(Mathf.PI * u) * (rise + 0.03f), 0f);
            for (int i = 0; i < seg; i++)
            {
                Vector3 p0 = Arc((float)i / seg), p1 = Arc((float)(i + 1) / seg);
                Vector3 dir = (p1 - p0).normalized;
                Vector3 n1 = Vector3.forward, n2 = Vector3.Cross(dir, n1).normalized;
                for (int j = 0; j < sides; j++)
                {
                    float a0 = j * Mathf.PI * 2f / sides, a1 = (j + 1) * Mathf.PI * 2f / sides;
                    Vector3 o0 = (n1 * Mathf.Cos(a0) + n2 * Mathf.Sin(a0)) * tubeR;
                    Vector3 o1 = (n1 * Mathf.Cos(a1) + n2 * Mathf.Sin(a1)) * tubeR;
                    Vector3 a = p0 + o0, b = p0 + o1, c = p1 + o0, d = p1 + o1;
                    int s0 = hv.Count;
                    hv.Add(a); hv.Add(b); hv.Add(c); hv.Add(b); hv.Add(d); hv.Add(c);
                    ht.Add(s0); ht.Add(s0 + 1); ht.Add(s0 + 2); ht.Add(s0 + 3); ht.Add(s0 + 4); ht.Add(s0 + 5);
                    // back faces too, so the wire never vanishes at any angle
                    ht.Add(s0); ht.Add(s0 + 2); ht.Add(s0 + 1); ht.Add(s0 + 3); ht.Add(s0 + 5); ht.Add(s0 + 4);
                }
            }
            var handle = LowPolyFactory.AddMeshChild(go, LowPolyFactory.Faceted(hv, ht, "BucketHandle"),
                assets.potMetal, Vector3.zero);
            handle.name = "BucketHandle";

            // physics: convex frustum for the body, a small box on the handle grip
            var colVerts = new System.Collections.Generic.List<Vector3>();
            for (int k = 0; k < N; k++) { colVerts.Add(P(outerBottom, 0f, k)); colVerts.Add(P(outerTop, height, k)); }
            var colMesh = new Mesh { name = "BucketHull" };
            colMesh.SetVertices(colVerts);
            var colTris = new System.Collections.Generic.List<int>();
            for (int k = 0; k < N; k++)
            {
                int b0 = k * 2, t0 = k * 2 + 1, b1 = ((k + 1) % N) * 2, t1 = ((k + 1) % N) * 2 + 1;
                colTris.Add(b0); colTris.Add(t0); colTris.Add(b1);
                colTris.Add(b1); colTris.Add(t0); colTris.Add(t1);
            }
            colMesh.SetTriangles(colTris, 0);
            var hull = go.AddComponent<MeshCollider>();
            hull.sharedMesh = colMesh;
            hull.convex = true;

            var gripCol = go.AddComponent<BoxCollider>();
            gripCol.center = new Vector3(0f, height + rise - 0.005f, 0f);
            gripCol.size = new Vector3(0.12f, 0.035f, 0.035f);

            var grip = new GameObject("HandleGrip").transform;
            grip.SetParent(go.transform, false);
            grip.localPosition = new Vector3(0f, height + rise, 0f);
            var baseAttach = new GameObject("BaseAttach").transform;
            baseAttach.SetParent(go.transform, false);

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 1.2f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            // same settling rules as every other loose item (see MakeGrabbable)
            rb.linearDamping = 0.6f;
            rb.angularDamping = 4f;
            rb.sleepThreshold = 0.05f;
            hull.sharedMaterial = GrippyGround;
            gripCol.sharedMaterial = GrippyGround;
            go.AddComponent<GroundSettle>();
            // interpolation is switched on in Bucket.Start, AFTER the caller has placed it:
            // an interpolated body ignores a transform move made in its creation frame and
            // would stay at the world origin (underground) and fall forever.

            var grab = go.AddComponent<BucketGrabInteractable>();
            grab.handleAttach = grip;
            grab.baseAttach = baseAttach;
            grab.attachTransform = grip;
            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            grab.useDynamicAttach = false;
            grab.trackRotation = true;
            grab.throwOnDetach = true;

            var item = go.AddComponent<SurvivalItem>();
            item.kind = ItemKind.Bucket;
            go.AddComponent<Forage.Bucket>();
            return go;
        }

        /// <summary>
        /// A long green stick, whittled to a point: spear a fish with the tip
        /// (hold the skewer and push it into the fish), then rest the skewer
        /// across the forked spit over the campfire to roast it. See
        /// <see cref="Forage.Skewer"/>. Lies along local X; the point is at +X.
        /// </summary>
        public static GameObject Skewer(int seed)
        {
            var go = new GameObject("Skewer");
            var assets = ForageAssets.Instance;
            float half = Forage.Skewer.Length * 0.5f;
            const float r = 0.011f;

            // shaft (butt at -X) and a sharpened point (+X)
            var shaft = LowPolyFactory.AddMeshChild(go, LowPolyFactory.Cone(r, Forage.Skewer.Length - 0.08f, 6, r * 0.9f),
                assets.stickWood, new Vector3(-half, 0f, 0f));
            shaft.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);
            var point = LowPolyFactory.AddMeshChild(go, LowPolyFactory.Cone(r * 0.9f, 0.08f, 6, 0f),
                assets.tinderStraw, new Vector3(half - 0.08f, 0f, 0f));
            point.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);

            var col = go.AddComponent<BoxCollider>();
            // a little thicker than the stick itself: a 3 cm sliver is easy for PhysX to lose
            col.size = new Vector3(Forage.Skewer.Length, 0.04f, 0.04f);

            var tip = new GameObject("SkewerTip").transform;
            tip.SetParent(go.transform, false);
            tip.localPosition = new Vector3(half - 0.02f, 0f, 0f);
            var rest = new GameObject("RestAttach").transform;
            rest.SetParent(go.transform, false);

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.3f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearDamping = 0.6f;
            rb.angularDamping = 4f;
            rb.sleepThreshold = 0.05f;
            col.sharedMaterial = GrippyGround;
            go.AddComponent<GroundSettle>();

            var grab = go.AddComponent<SkewerGrabInteractable>();
            grab.restAttach = rest;
            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            grab.useDynamicAttach = true;
            grab.throwOnDetach = true;

            var item = go.AddComponent<SurvivalItem>();
            item.kind = ItemKind.Skewer;
            var sk = go.AddComponent<Forage.Skewer>();
            sk.tip = tip;
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
