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
            Assert.That(BucketStand.Instance, Is.Not.Null);
            Assert.That(Vector3.Distance(BucketStand.Instance.transform.position, FirePit.Instance.transform.position),
                Is.LessThan(1.3f), "stand must be inside the fire's heat");
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
        public IEnumerator Bucket_BoilsOnTheStandOnlyWhileTheFireBurns()
        {
            var stand = BucketStand.Instance;
            var pit = FirePit.Instance;
            var b = FreshBucket(stand.Seat.position + Vector3.up * 0.02f);
            yield return null;
            b.SetContents(0.3f, false);
            var grab = b.GetComponent<XRGrabInteractable>();
            stand.Socket.interactionManager.SelectEnter((IXRSelectInteractor)stand.Socket, (IXRSelectInteractable)grab);
            yield return Wait(1f);
            Assert.That(b.OnStand, Is.True);
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
            stand.Socket.interactionManager.SelectExit((IXRSelectInteractor)stand.Socket, (IXRSelectInteractable)grab);
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
            var hand = Object.FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None)
                .First(i => i.handedness == InteractorHandedness.Right);

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
    }
}
