using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Forage.Tests
{
    /// <summary>
    /// Behaviour tests for the water update: bucket fill / spill / pour / boil /
    /// drink, raw-water drinking and its faster thirst drain, pond buoyancy,
    /// and the living fish (struggle, flop on land, swim again in water).
    /// Each test drives the real game objects in the real generated scene.
    /// </summary>
    public class WaterAndFishPlayModeTests
    {
        static bool _loaded;

        [UnitySetUp]
        public IEnumerator LoadWorldOnce()
        {
            if (_loaded) yield break;
            yield return SceneManager.LoadSceneAsync("Forage", LoadSceneMode.Single);
            float until = Time.time + 4f;
            while (Time.time < until) yield return null;
            _loaded = true;
        }

        /// <summary>
        /// The right hand's grab interactor. The rig is hands-first: with no
        /// headset tracked (a test run) XRInputModalityManager leaves both hands
        /// and both controllers switched off, so switch the right controller on
        /// to grab with, as picking it up would in the headset.
        /// </summary>
        static NearFarInteractor RightHandInteractor()
        {
            var right = Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(i => i.handedness == InteractorHandedness.Right).ToList();
            var active = right.FirstOrDefault(i => i.isActiveAndEnabled);
            if (active != null) return active;

            var modality = Object.FindFirstObjectByType<UnityEngine.XR.Interaction.Toolkit.Inputs.XRInputModalityManager>(
                FindObjectsInactive.Include);
            if (modality != null && modality.rightController != null)
                modality.rightController.SetActive(true);
            return right.First(i => i.isActiveAndEnabled);
        }

        static IEnumerator Wait(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until) yield return null;
        }

        static Bucket FreshBucket(Vector3 at)
        {
            var go = ItemFactory.Bucket();
            go.transform.position = at;
            return go.GetComponent<Bucket>();
        }

        static void Hold(Bucket b, Vector3 pos, Quaternion rot)
        {
            // kinematic stand-in for a hand holding it still
            var rb = b.GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.position = pos; rb.rotation = rot;
            b.transform.SetPositionAndRotation(pos, rot);
        }

        [UnityTest, Order(0)] // first: before other tests start moving the player and props around
        public IEnumerator Items_ComeToRestInsteadOfRollingAway()
        {
            // Regression: round colliders + no rolling resistance used to carry the tinder,
            // flint and pot 20-60 m out of camp within half a minute.
            var forest = ForestGenerator.Instance;
            var loose = Object.FindObjectsByType<SurvivalItem>(FindObjectsSortMode.None)
                .Where(i => i.GetComponent<Rigidbody>() != null && !i.GetComponent<Rigidbody>().isKinematic
                            && i.GetComponent<FishAI>() == null
                            && (i.kind == ItemKind.Tinder || i.kind == ItemKind.Flint || i.kind == ItemKind.Pot ||
                                i.kind == ItemKind.Stick || i.kind == ItemKind.LeafBundle || i.kind == ItemKind.Branch))
                .ToList();
            Assert.That(loose.Count, Is.GreaterThan(10));
            var start = loose.ToDictionary(i => i, i => i.transform.position);
            yield return Wait(15f);

            var wandered = loose.Where(i => i != null)
                .Select(i => (i, d: Vector3.Distance(i.transform.position, start[i])))
                .Where(t => t.d > 0.6f)
                .Select(t => $"{t.i.kind} {t.i.name} (wet {t.i.isWet}) moved {t.d:F1} m: {start[t.i]} -> {t.i.transform.position}, " +
                             $"ground at start {forest.HeightAt(start[t.i].x, start[t.i].z):F2}, parent {(t.i.transform.parent ? t.i.transform.parent.name : "-")}")
                .ToList();
            Assert.That(wandered, Is.Empty, "items kept rolling:\n" + string.Join("\n", wandered));

            var buried = Object.FindObjectsByType<SurvivalItem>(FindObjectsSortMode.None)
                .Where(i => i.transform.position.y < forest.HeightAt(i.transform.position.x, i.transform.position.z) - 2f)
                .Select(i => $"{i.kind} {i.name} at {i.transform.position}")
                .ToList();
            Assert.That(buried, Is.Empty, "items fell through the world:\n" + string.Join("\n", buried));
        }

        [UnityTest]
        public IEnumerator Bucket_SpawnsAtCampAndRestsOnGround()
        {
            yield return null;
            var forest = ForestGenerator.Instance;
            var b = Object.FindObjectsByType<Bucket>(FindObjectsSortMode.None)
                .OrderBy(x => x.transform.position.magnitude).First();
            var p = b.transform.position;
            Assert.That(Mathf.Abs(p.y - forest.HeightAt(p.x, p.z)), Is.LessThan(0.3f), "bucket is not on the ground");
            Assert.That(new Vector2(p.x, p.z).magnitude, Is.LessThan(forest.campRadius));
            Assert.That(Vector3.Distance(p, FirePit.Instance.transform.position), Is.LessThan(2f),
                "the bucket should start beside the fire pit");
            Assert.That(CampfireRig.Instance, Is.Not.Null, "campfire rig (hook + spit) missing");
        }

        [UnityTest]
        public IEnumerator Bucket_ScoopsMurkyWaterFromThePond()
        {
            var w = WaterBody.Instance;
            var dip = new Vector3(w.center.x + 2f, w.surfaceY - 0.32f, w.center.y - 1.5f);
            var b = FreshBucket(dip);
            Hold(b, dip, Quaternion.identity);
            yield return Wait(2.5f);
            Assert.That(b.fill, Is.GreaterThan(0.95f),
                $"bucket at {b.transform.position}, rim {b.RimCenter}, surface {w.SurfaceHeightAt(b.RimCenter.x, b.RimCenter.z):F2}, " +
                $"rim underwater {w.IsUnderwater(b.RimCenter, 0.01f)}, spilled {b.totalSpilled:F2}");
            Assert.That(b.contaminated, Is.True, "pond water must start untreated");
            Assert.That(b.boiled, Is.False);
            Object.Destroy(b.gameObject);
        }

        [UnityTest]
        public IEnumerator Bucket_SpillsDownToTheRimLevelWhenTilted()
        {
            var b = FreshBucket(Vector3.zero);
            yield return null;
            Hold(b, new Vector3(3f, ForestGenerator.Instance.HeightAt(3f, 1f) + 1f, 1f), Quaternion.Euler(30f, 0f, 0f));
            b.SetContents(1f, false);
            yield return Wait(3f);
            // analytic: level surface touches the rim when fill = 1 - r*tan(30)/H
            float expected = 1f - 0.107f * Mathf.Tan(30f * Mathf.Deg2Rad) / 0.23f;
            Assert.That(b.fill, Is.EqualTo(expected).Within(0.03f));

            Hold(b, b.transform.position, Quaternion.Euler(8f, 0f, 0f));
            float kept = b.fill;
            yield return Wait(1f);
            Assert.That(b.fill, Is.EqualTo(kept).Within(0.001f), "a gently tilted bucket must not keep spilling");

            Hold(b, b.transform.position, Quaternion.Euler(125f, 0f, 0f));
            yield return Wait(2f);
            Assert.That(b.IsEmpty, Is.True, "an upturned bucket empties");
            Object.Destroy(b.gameObject);
        }

        [UnityTest]
        public IEnumerator Bucket_PouredOverThePotFillsIt()
        {
            var forest = ForestGenerator.Instance;
            var pot = Object.FindFirstObjectByType<CookingPot>();
            var prb = pot.GetComponent<Rigidbody>();
            var potPos = new Vector3(2.5f, forest.HeightAt(2.5f, 0.5f) + 0.02f, 0.5f);
            prb.isKinematic = true; prb.position = potPos; pot.transform.SetPositionAndRotation(potPos, Quaternion.identity);
            pot.SetState(CookingPot.PotState.Empty);

            var b = FreshBucket(Vector3.zero);
            yield return null;
            var rot = Quaternion.Euler(0f, 0f, 75f);
            Hold(b, Vector3.zero, rot);
            Vector3 lowDir = Vector3.ProjectOnPlane(Vector3.down, b.transform.up).normalized;
            Vector3 lip = b.transform.TransformPoint(0f, Bucket.RimY, 0f) + lowDir * Bucket.InnerTopR;
            Hold(b, potPos + Vector3.up * 0.45f - lip, rot);
            b.SetContents(0.8f, false);
            yield return Wait(2f);
            Assert.That(pot.state, Is.EqualTo(CookingPot.PotState.DirtyWater));
            prb.isKinematic = false;
            Object.Destroy(b.gameObject);
        }

        [UnityTest]
        public IEnumerator Bucket_HangsOnTheHookAndBoilsOnlyWhileTheFireBurns()
        {
            var rig = CampfireRig.Instance;
            var pit = FirePit.Instance;
            var b = FreshBucket(rig.HookAttach.position + Vector3.down * 0.4f);
            yield return null;
            b.SetContents(0.3f, false);
            var grab = b.GetComponent<XRGrabInteractable>();
            rig.Hook.interactionManager.SelectEnter((IXRSelectInteractor)rig.Hook, (IXRSelectInteractable)grab);
            yield return Wait(1f);
            Assert.That(b.OnHook, Is.True);
            // hung by its handle: the bucket sits below the hook, upright, over the flames
            Assert.That(b.transform.position.y, Is.LessThan(rig.HookAttach.position.y - 0.25f));
            Assert.That(Vector3.Angle(b.transform.up, Vector3.up), Is.LessThan(5f));
            Assert.That(b.spillRate, Is.EqualTo(0f), "the hook's snap must not slosh water out");
            if (!pit.IsLit)
            {
                Assert.That(b.boilProgress, Is.EqualTo(0f), "must not boil without fire");
                pit.AddItem(ItemFactory.TinderBundle(1).GetComponent<SurvivalItem>());
                pit.AddItem(ItemFactory.Stick(2, false).GetComponent<SurvivalItem>());
                pit.AddHeat(60f); pit.AddHeat(60f);
            }
            Assert.That(pit.IsLit, Is.True);
            yield return Wait(15f); // 0.3 fill boils in ~13.4 s
            Assert.That(b.boiled, Is.True);
            Assert.That(b.contaminated, Is.False);
            rig.Hook.interactionManager.SelectExit((IXRSelectInteractor)rig.Hook, (IXRSelectInteractable)grab);
            Object.Destroy(b.gameObject);
        }

        [UnityTest]
        public IEnumerator Drinking_BoiledIsSafe_RawHydratesButDrainsFaster()
        {
            var gm = GameManager.Instance;
            var v = gm.vitals;
            var b = FreshBucket(new Vector3(5f, 50f, 5f));
            yield return null;
            b.GetComponent<Rigidbody>().isKinematic = true;

            v.hydration = 40f; v.isSick = false; v.untreatedUntil = 0f;
            b.SetContents(0.5f, true);
            b.DrinkServing();
            Assert.That(v.hydration, Is.EqualTo(70f).Within(0.5f));
            Assert.That(v.isSick, Is.False);
            Assert.That(v.HasUntreatedWater, Is.False);
            Assert.That(gm.IsComplete("water"), Is.True);

            // a raw sip from the pond: +H2O now, x3 drain afterwards
            v.hydration = 40f; v.isSick = false; v.untreatedUntil = 0f;
            var hd = Object.FindFirstObjectByType<HandDrinking>();
            Assert.That(hd, Is.Not.Null, "drink-button handler missing");
            hd.Sip(new Vector3(WaterBody.Instance.center.x, WaterBody.Instance.surfaceY, WaterBody.Instance.center.y), false);
            Assert.That(v.hydration, Is.GreaterThan(40f));
            Assert.That(v.HasUntreatedWater, Is.True);
            Assert.That(v.HydrationDrainMultiplier, Is.EqualTo(v.untreatedDrainMultiplier).Within(0.01f));

            float h0 = v.hydration, t0 = Time.time;
            yield return Wait(4f);
            float perMin = (h0 - v.hydration) / (Time.time - t0) * 60f;
            Assert.That(perMin, Is.EqualTo(v.hydrationDecay * v.untreatedDrainMultiplier).Within(0.6f));
            v.untreatedUntil = 0f;
            Object.Destroy(b.gameObject);
        }

        [UnityTest]
        public IEnumerator DrinkButton_WithHandInTheWater_TakesARawSip()
        {
            var w = WaterBody.Instance;
            var forest = ForestGenerator.Instance;
            var rig = Object.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            var cc = rig.GetComponent<CharacterController>();
            // stand in the shallows so the (simulated) hands are at the waterline
            Vector3 spot = Vector3.zero; float best = 99f;
            for (float r = 0f; r < 7f; r += 0.1f)
            {
                float x = w.center.x - r, z = w.center.y;
                float d = Mathf.Abs((w.surfaceY - forest.HeightAt(x, z)) - 1.6f);
                if (d < best) { best = d; spot = new Vector3(x, forest.HeightAt(x, z), z); }
            }
            Vector3 home = rig.transform.position;
            cc.enabled = false; rig.transform.position = spot; cc.enabled = true;
            yield return Wait(0.5f);

            var hd = Object.FindFirstObjectByType<HandDrinking>();
            int sips = hd.SipsTaken;
            string what = hd.PressDrink(false, true);
            Assert.That(what, Is.EqualTo("sip"), "the button did not drink with the hand at the waterline");
            Assert.That(hd.SipsTaken, Is.EqualTo(sips + 1));

            cc.enabled = false; rig.transform.position = home; cc.enabled = true;
            GameManager.Instance.vitals.untreatedUntil = 0f;
        }

        [UnityTest]
        public IEnumerator Skewer_SpearsFish_RoastsOnTheSpit_ButNotInTheAshes()
        {
            var rig = CampfireRig.Instance;
            var pit = FirePit.Instance;
            var fishes = Object.FindObjectsByType<FishItem>(FindObjectsSortMode.None).Where(f => !f.IsSkewered).Take(2).ToList();
            Assert.That(fishes.Count, Is.EqualTo(2));
            var skewer = Object.FindObjectsByType<Skewer>(FindObjectsSortMode.None).First(s => s.FishCount == 0);

            // spear one fish: it stops being an animal and rides the stick
            Assert.That(skewer.Spear(fishes[0]), Is.True);
            yield return null;
            Assert.That(fishes[0].IsSkewered, Is.True);
            Assert.That(fishes[0].transform.parent, Is.EqualTo(skewer.transform));
            Assert.That(fishes[0].GetComponent<Rigidbody>().isKinematic, Is.True);
            var ai = fishes[0].GetComponent<FishAI>();
            if (ai != null) Assert.That(ai.enabled, Is.False);

            // the other fish is left lying right next to the fire
            var loose = fishes[1];
            var lr = loose.GetComponent<Rigidbody>();
            var la = loose.GetComponent<FishAI>();
            if (la != null) la.enabled = false;
            lr.isKinematic = true;
            var nearFire = pit.transform.position + new Vector3(0.3f, 0.05f, -0.3f);
            lr.position = nearFire; loose.transform.position = nearFire;

            // rest the skewer on the spit and light the fire
            var sg = skewer.GetComponent<XRGrabInteractable>();
            rig.Spit.interactionManager.SelectEnter((IXRSelectInteractor)rig.Spit, (IXRSelectInteractable)sg);
            yield return Wait(0.5f);
            Assert.That(skewer.OnSpit, Is.True);
            Assert.That(Vector3.Angle(skewer.transform.right, rig.SpitAttach.right), Is.LessThan(5f), "skewer should lie across the forks");
            if (!pit.IsLit)
            {
                pit.AddItem(ItemFactory.TinderBundle(3).GetComponent<SurvivalItem>());
                pit.AddItem(ItemFactory.Stick(4, false).GetComponent<SurvivalItem>());
                pit.AddHeat(60f); pit.AddHeat(60f);
            }
            Assert.That(pit.IsLit, Is.True);

            var onStick = fishes[0].GetComponent<Cookable>();
            yield return Wait(onStick.cookSeconds + 1.5f);
            Assert.That(onStick.cooked, Is.True, $"skewered fish over the fire did not roast (progress {onStick.progress:F2})");
            Assert.That(loose.GetComponent<Cookable>().progress, Is.EqualTo(0f), "a fish lying in the ashes must not cook");

            rig.Spit.interactionManager.SelectExit((IXRSelectInteractor)rig.Spit, (IXRSelectInteractable)sg);
        }

        [UnityTest]
        public IEnumerator Pond_WoodFloatsAndDrifts_FlintSinks()
        {
            var w = WaterBody.Instance;
            var stick = ItemFactory.Stick(77, false);
            var flint = ItemFactory.FlintStone(78);
            var s0 = new Vector3(w.center.x + 1f, w.surfaceY + 0.8f, w.center.y - 1f);
            stick.transform.position = s0; stick.GetComponent<Rigidbody>().position = s0;
            var f0 = new Vector3(w.center.x + 1.5f, w.surfaceY + 0.8f, w.center.y - 1f);
            flint.transform.position = f0; flint.GetComponent<Rigidbody>().position = f0;
            yield return Wait(6f);
            Assert.That(stick.transform.position.y, Is.EqualTo(w.surfaceY).Within(0.2f), "wood should float at the surface");
            Assert.That(Vector2.Distance(new Vector2(stick.transform.position.x, stick.transform.position.z), new Vector2(s0.x, s0.z)),
                Is.GreaterThan(0.2f), "floating wood should drift with the current");
            float floor = ForestGenerator.Instance.HeightAt(flint.transform.position.x, flint.transform.position.z);
            Assert.That(flint.transform.position.y - floor, Is.LessThan(0.3f), "flint should sink to the bottom");
            Object.Destroy(stick); Object.Destroy(flint);
        }

        [UnityTest]
        public IEnumerator Fish_StrugglesWhenHeld_FlopsOnLand_SwimsWhenBackInWater()
        {
            var fish = Object.FindObjectsByType<FishAI>(FindObjectsSortMode.None).First(f => f.IsAlive);
            fish.slipChance = 0f; // deterministic: no wriggling free during the test
            var rb = fish.GetComponent<Rigidbody>();
            var grab = fish.GetComponent<XRGrabInteractable>();
            var hand = RightHandInteractor();

            // bring it to the hand first, as a real grab would: selecting it from the pond 30 m
            // away made velocity tracking fire it through camp, bowling the skewers and bucket
            // off the map
            rb.position = hand.transform.position; fish.transform.position = hand.transform.position;
            rb.linearVelocity = Vector3.zero;
            hand.interactionManager.SelectEnter((IXRSelectInteractor)hand, (IXRSelectInteractable)grab);
            yield return null;
            yield return null;
            Assert.That(fish.state, Is.EqualTo(FishAI.State.Held));
            float tail0 = fish.transform.Find("Tail").localEulerAngles.y;
            yield return Wait(0.13f);
            Assert.That(fish.transform.Find("Tail").localEulerAngles.y, Is.Not.EqualTo(tail0).Within(0.5f),
                "the tail should thrash while held");

            if (grab.isSelected)
                hand.interactionManager.SelectExit((IXRSelectInteractor)hand, (IXRSelectInteractable)grab);
            // put it on dry ground at camp, a metre up, as if dropped there
            var forest = ForestGenerator.Instance;
            var dropAt = new Vector3(3f, forest.HeightAt(3f, 2f) + 1f, 2f);
            rb.position = dropAt; fish.transform.position = dropAt;
            rb.linearVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.That(rb.isKinematic, Is.False, "a released fish must never freeze in mid-air");
            Assert.That(rb.useGravity, Is.True);
            float y0 = fish.transform.position.y;
            yield return Wait(4f);
            Assert.That(fish.state, Is.EqualTo(FishAI.State.Flopping));
            Assert.That(fish.transform.position.y, Is.LessThan(y0 - 0.5f), "it should have fallen from the hand");
            Assert.That(fish.flops, Is.GreaterThan(1), "it should flop on the ground");

            var w = WaterBody.Instance;
            var drop = new Vector3(w.center.x - 2f, w.surfaceY + 0.6f, w.center.y + 1f);
            rb.position = drop; fish.transform.position = drop;
            yield return Wait(3f);
            Assert.That(fish.state, Is.EqualTo(FishAI.State.Swimming), "back in the water it should swim again");
            Assert.That(rb.useGravity, Is.False);
        }

        static float RealFloorBelow(Vector3 p)
        {
            float best = float.NegativeInfinity;
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 0.6f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
                if (h.rigidbody == null && h.point.y > best) best = h.point.y;
            return best;
        }

        [UnityTest]
        public IEnumerator Crabs_WalkOnThePondFloor_Flee_CanBePickedUp_AndReturnToWater()
        {
            var crabs = Object.FindObjectsByType<CrabAI>(FindObjectsSortMode.None);
            Assert.That(crabs.Length, Is.GreaterThanOrEqualTo(2), "the pond should have crabs");

            // standing ON the real terrain mesh, not inside it or floating above it
            foreach (var c in crabs)
            {
                float floor = RealFloorBelow(c.transform.position);
                Assert.That(float.IsNegativeInfinity(floor), Is.False, c.name + " has no floor below it");
                Assert.That(c.transform.position.y - floor, Is.InRange(-0.04f, 0.08f), c.name + " is not standing on the pond floor");
                Assert.That(c.GetComponent<Collider>(), Is.Not.Null);
            }

            // they walk around by themselves (and their legs move while they do)
            var start = crabs.ToDictionary(c => c, c => c.transform.position);
            yield return Wait(6f);
            Assert.That(crabs.Any(c => Vector3.Distance(c.transform.position, start[c]) > 0.1f), Is.True, "no crab walked anywhere in 6 s");

            // a hand reaching close makes it scuttle away
            var crab = crabs.First(c => c.state != CrabAI.State.Buried);
            float r0 = crab.fleeRadius;
            crab.fleeRadius = 1000f;           // "a hand is right next to it"
            yield return null; yield return null;
            Assert.That(crab.state, Is.EqualTo(CrabAI.State.Fleeing), "a crab should flee from a nearby hand");
            crab.fleeRadius = r0;
            crab.startles = 0;

            // pick it up: it struggles in the hand
            var grab = crab.GetComponent<XRGrabInteractable>();
            var hand = RightHandInteractor();
            crab.transform.position = hand.transform.position;   // reach it first, as a real grab would
            hand.interactionManager.SelectEnter((IXRSelectInteractor)hand, (IXRSelectInteractable)grab);
            yield return null; yield return null;
            Assert.That(crab.state, Is.EqualTo(CrabAI.State.Held));
            var leg = crab.transform.Find("Leg0");
            var leg0 = leg.localRotation;
            yield return Wait(0.2f);
            Assert.That(Quaternion.Angle(leg.localRotation, leg0), Is.GreaterThan(1f), "its legs should wave while held");

            // drop it on dry land at camp, a metre up
            hand.interactionManager.SelectExit((IXRSelectInteractor)hand, (IXRSelectInteractable)grab);
            var forest = ForestGenerator.Instance;
            var dropAt = new Vector3(3f, forest.HeightAt(3f, 2f) + 1f, 2f);
            var rb = crab.GetComponent<Rigidbody>();
            rb.position = dropAt; crab.transform.position = dropAt;
            rb.linearVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.That(crab.state, Is.EqualTo(CrabAI.State.Falling));
            Assert.That(rb.isKinematic, Is.False, "a dropped crab must fall, not freeze in mid-air");
            yield return Wait(4f);
            Assert.That(crab.state, Is.Not.EqualTo(CrabAI.State.Falling), "it should land and recover");
            Assert.That(crab.transform.position.y, Is.LessThan(dropAt.y - 0.5f), "it should have fallen to the ground");

            // ...and head back toward the pond
            Vector2 pc = crab.pondCenter;
            float d0 = Vector2.Distance(new Vector2(crab.transform.position.x, crab.transform.position.z), pc);
            yield return Wait(4f);
            float d1 = Vector2.Distance(new Vector2(crab.transform.position.x, crab.transform.position.z), pc);
            Assert.That(d1, Is.LessThan(d0 - 0.3f), $"a crab on land should walk back toward the water ({d0:F1} -> {d1:F1} m)");
        }

        [UnityTest]
        public IEnumerator Items_CannotBeFlungOffTheMap_AndAreRecoveredIfLost()
        {
            // Regression: a velocity-tracked item whose hand target jumped got hundreds of m/s,
            // left the 144 m map, and the rescue net then "recovered" it to a point in the air over
            // the void, so it fell and was recovered forever (both skewers, the bucket, a branch).
            var forest = ForestGenerator.Instance;
            float edge = forest.worldSize * 0.5f;
            var stick = Object.FindObjectsByType<SurvivalItem>(FindObjectsSortMode.None)
                .First(i => i.kind == ItemKind.Stick && i.transform.parent == null
                            && !i.GetComponent<Rigidbody>().isKinematic
                            && new Vector2(i.transform.position.x, i.transform.position.z).magnitude < forest.campRadius + 6f);
            var rb = stick.GetComponent<Rigidbody>();
            Vector3 home = stick.transform.position;

            rb.WakeUp();
            rb.linearVelocity = new Vector3(300f, 60f, 0f);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(rb.linearVelocity.magnitude, Is.LessThanOrEqualTo(GroundSettle.MaxSpeed + 0.5f), "item speed must be capped");

            // now lose it off the edge of the map entirely
            int rescues0 = SurvivalItem.RescueCount;
            var lost = new Vector3(edge + 15f, 8f, 0f);
            rb.linearVelocity = Vector3.zero;
            rb.position = lost; stick.transform.position = lost;
            yield return Wait(4f);
            Vector3 p = stick.transform.position;
            Assert.That(Mathf.Abs(p.x) < edge && Mathf.Abs(p.z) < edge, Is.True, $"not brought back onto the map: {p}");
            float ground = forest.HeightAt(p.x, p.z);
            Assert.That(p.y, Is.InRange(ground - 0.3f, ground + 1.5f), "recovered onto the ground");
            Assert.That(SurvivalItem.RescueCount - rescues0, Is.EqualTo(1), "recovered once, not in a loop");
            Assert.That(Vector3.Distance(p, home), Is.LessThan(25f), "returned to where it last rested");
        }
    }
}
