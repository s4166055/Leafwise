using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Forage.Tests
{
    /// <summary>
    /// The looping day, moonlight, sleeping/skipping the night, raw and cooked
    /// fish, and the fishing / sunrise objectives — all driven on the real scene.
    /// </summary>
    public class DayNightPlayModeTests
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

        static DayNightCycle Cycle => Object.FindFirstObjectByType<DayNightCycle>();

        static IEnumerator WaitWhileSkipping(DayNightCycle dn, float timeout = 15f)
        {
            float until = Time.time + timeout;
            while (dn.IsSkipping && Time.time < until) yield return null;
            Assert.That(dn.IsSkipping, Is.False, "skip never finished");
        }

        [UnityTest, Order(0)]
        public IEnumerator Day_StartsBrightAndTheCycleLoops()
        {
            yield return null;
            var dn = Cycle;
            Assert.That(dn, Is.Not.Null);
            Assert.That(dn.isNight, Is.False);
            Assert.That(dn.sun.intensity, Is.GreaterThan(1f), "morning light should be bright");
            Assert.That(dn.sun.transform.forward.y, Is.LessThan(-0.3f), "the sun should shine downwards by day");
            Assert.That(dn.cycleSeconds, Is.EqualTo(1200f));

            // night: the same light becomes cool, dim moonlight
            dn.SetPhase(0.75f);
            yield return null;
            Assert.That(dn.isNight, Is.True);
            Assert.That(GameManager.Instance.vitals.isNight, Is.True, "night must make warmth matter");
            Assert.That(dn.sun.intensity, Is.InRange(0.15f, 0.4f), "moonlight is dim but not black");
            Assert.That(dn.sun.color.b, Is.GreaterThan(dn.sun.color.r), "moonlight is cool blue");
            Assert.That(dn.sun.transform.forward.y, Is.LessThan(-0.3f), "the moon must light the ground, not the sky");
            Assert.That(RenderSettings.ambientSkyColor.maxColorComponent, Is.GreaterThan(0.1f), "night ambient must not be pitch black");
            Assert.That(GameManager.Instance.IsComplete("survive"), Is.True, "reaching nightfall completes 'survive'");

            // the sky is dimmed so the 'moon' light does not paint a daytime sky
            var sky = RenderSettings.skybox;
            if (sky != null && sky.HasProperty("_Exposure"))
                Assert.That(sky.GetFloat("_Exposure"), Is.LessThan(0.2f), "night sky exposure");

            // and next morning the loop starts again: day two, bright, no longer night
            dn.SetPhase(1.2f);
            yield return null;
            Assert.That(dn.isNight, Is.False);
            Assert.That(dn.Day, Is.EqualTo(2));
            Assert.That(dn.sun.intensity, Is.GreaterThan(1f));
            Assert.That(GameManager.Instance.vitals.isNight, Is.False);
            Assert.That(GameManager.Instance.IsComplete("dawn"), Is.True, "sunrise after a night completes 'dawn'");
            if (sky != null && sky.HasProperty("_Exposure"))
                Assert.That(sky.GetFloat("_Exposure"), Is.GreaterThan(0.8f), "day sky exposure restored");
        }

        [UnityTest]
        public IEnumerator Light_ChangesGraduallyThroughTheWholeLoop()
        {
            var dn = Cycle;
            float prev = -1f, worst = 0f, worstAt = 0f;
            for (int i = 0; i <= 500; i++)
            {
                float p = 2f + i / 500f;
                dn.SetPhase(p);
                float inten = dn.sun.intensity;
                Assert.That(float.IsNaN(inten) || inten < 0f, Is.False);
                if (prev >= 0f && Mathf.Abs(inten - prev) > worst) { worst = Mathf.Abs(inten - prev); worstAt = p; }
                prev = inten;
            }
            Assert.That(worst, Is.LessThan(0.12f), $"light jumped by {worst:F3} near phase {worstAt:F3}");
            dn.SetPhase(2.2f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SkipToMorning_ByWatch_PassesTimeButDoesNotHeal()
        {
            var dn = Cycle;
            var v = GameManager.Instance.vitals;
            dn.skipDuration = 0.4f; dn.fadeDuration = 0.2f;

            Assert.That(dn.SkipToMorning(false), Is.False, "cannot skip in daylight");

            dn.SetPhase(3.75f);
            yield return null;
            v.hydration = 80f; v.energy = 80f; v.warmth = 10f; v.health = 50f; v.nearFire = false;
            Assert.That(dn.SkipToMorning(false), Is.True);
            Assert.That(dn.SkipToMorning(false), Is.False, "a skip already running cannot be restarted");

            yield return Wait(0.6f);
            Assert.That(ScreenFeedback.FadeLevel, Is.GreaterThan(0.9f), "the view should be black while time passes");
            yield return WaitWhileSkipping(dn);
            yield return Wait(0.3f);

            Assert.That(dn.isNight, Is.False, "should wake in the morning");
            Assert.That(dn.Phase01, Is.EqualTo(DayNightCycle.MorningPhase).Within(0.01f));
            Assert.That(dn.Day, Is.EqualTo(5));
            Assert.That(ScreenFeedback.FadeLevel, Is.LessThan(0.05f), "screen fades back in");
            Assert.That(v.hydration, Is.InRange(66f, 69f), "a night costs water");
            Assert.That(v.energy, Is.InRange(66f, 69f), "a night costs food");
            Assert.That(v.warmth, Is.LessThan(40f), "skipping from the watch does not warm you");
            Assert.That(v.health, Is.LessThan(60f), "skipping from the watch does not heal you");
        }

        [UnityTest]
        public IEnumerator SleepingInTheShelter_WarmsAndHeals()
        {
            var dn = Cycle;
            var v = GameManager.Instance.vitals;
            dn.skipDuration = 0.4f; dn.fadeDuration = 0.2f;
            dn.SetPhase(4.75f);
            yield return null;
            v.warmth = 10f; v.health = 50f; v.hydration = 80f; v.energy = 80f;

            Assert.That(dn.SkipToMorning(true), Is.True);
            yield return WaitWhileSkipping(dn);
            Assert.That(dn.isNight, Is.False);
            Assert.That(v.warmth, Is.GreaterThan(50f), "sleeping in a shelter warms you");
            Assert.That(v.health, Is.GreaterThan(70f), "sleeping heals");
            Assert.That(v.hydration, Is.LessThan(70f), "...but you still wake thirsty");
        }

        [UnityTest]
        public IEnumerator Shelter_LetsYouSleepOnlyWhenFinishedAndCrouchedAtNight()
        {
            var dn = Cycle;
            var shelter = Shelter.Instance;
            var rest = shelter.GetComponent<ShelterRest>();
            Assert.That(rest, Is.Not.Null, "shelter has no ShelterRest");
            var forest = ForestGenerator.Instance;
            Vector3 spot = rest.RestPoint;
            float ground = forest.HeightAt(spot.x, spot.z);
            Vector3 Head(float h, float dx = 0f) => new Vector3(spot.x + dx, ground + h, spot.z);

            dn.SetPhase(6.75f);
            yield return null;

            int b = shelter.branches, l = shelter.leaves;
            shelter.branches = 0; shelter.leaves = 0;
            Assert.That(rest.Check(Head(0.9f)), Is.EqualTo(ShelterRest.Reason.ShelterIncomplete));

            shelter.branches = shelter.branchesNeeded; shelter.leaves = shelter.leavesNeeded;
            Assert.That(rest.Check(Head(1.65f)), Is.EqualTo(ShelterRest.Reason.StandingUp));
            Assert.That(rest.Check(Head(0.9f)), Is.EqualTo(ShelterRest.Reason.Ok));
            Assert.That(rest.Check(Head(0.9f, 5f)), Is.EqualTo(ShelterRest.Reason.TooFar));

            dn.SetPhase(7.3f);
            yield return null;
            Assert.That(rest.Check(Head(0.9f)), Is.EqualTo(ShelterRest.Reason.NotNight));

            shelter.branches = b; shelter.leaves = l;
        }

        [UnityTest]
        public IEnumerator RawFish_MakesYouSick_HungrierAndThirstier_AndDrainsHealth()
        {
            var gm = GameManager.Instance;
            var v = gm.vitals;
            var fish = Object.FindObjectsByType<FishItem>(FindObjectsSortMode.None).First(f => !f.IsSkewered);
            string hint = null;
            System.Action<string> onHint = id => { if (id == "ate-raw-fish") hint = id; };
            ForageEvents.Hint += onHint;

            gm.Objectives.First(o => o.id == "fish").done = false;   // tests share one scene: start clean
            v.health = 100f; v.hydration = 80f; v.energy = 60f; v.isSick = false;
            Assert.That(v.HasFoodPoisoning, Is.False);
            float baseHydroMul = v.HydrationDrainMultiplier, baseEnergyMul = v.EnergyDrainMultiplier;

            fish.Eat(gm);
            yield return null;
            ForageEvents.Hint -= onHint;

            Assert.That(fish == null, Is.True, "the fish is eaten");
            Assert.That(v.HasFoodPoisoning, Is.True);
            Assert.That(v.isSick, Is.True);
            Assert.That(v.FoodPoisoningSecondsLeft, Is.InRange(60f, 76f));
            Assert.That(v.health, Is.LessThanOrEqualTo(100f - FishItem.PoisoningHealthHit + 0.5f), "raw fish hurts immediately");
            Assert.That(v.energy, Is.InRange(60f + FishItem.RawFoodValue - 1f, 60f + FishItem.RawFoodValue + 0.5f), "a little food");
            Assert.That(v.EnergyDrainMultiplier, Is.GreaterThanOrEqualTo(baseEnergyMul * 2.9f), "hungrier");
            Assert.That(v.HydrationDrainMultiplier, Is.GreaterThanOrEqualTo(baseHydroMul * 2.4f), "thirstier");
            Assert.That(hint, Is.EqualTo("ate-raw-fish"), "Scout is told");
            Assert.That(gm.IsComplete("fish"), Is.False, "eating it raw is not the fishing objective");

            // health keeps draining while it lasts
            float h0 = v.health;
            yield return Wait(5f);
            Assert.That(v.health, Is.LessThan(h0 - 1f), "illness keeps draining health");
        }

        [UnityTest]
        public IEnumerator FoodPoisoning_Passes()
        {
            var v = GameManager.Instance.vitals;
            v.isSick = false;
            v.ApplyFoodPoisoning(0.6f, 3f, "test-poison");
            Assert.That(v.HasFoodPoisoning, Is.True);
            yield return Wait(1.2f);
            Assert.That(v.HasFoodPoisoning, Is.False);
            Assert.That(v.isSick, Is.False, "sickness ends with the poisoning");
            Assert.That(v.EnergyDrainMultiplier, Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator CookedFish_IsAMealAndCompletesTheFishingObjective()
        {
            var gm = GameManager.Instance;
            var v = gm.vitals;
            var fish = Object.FindObjectsByType<FishItem>(FindObjectsSortMode.None).First(f => !f.IsSkewered);
            fish.GetComponent<Cookable>().cooked = true;
            gm.Objectives.First(o => o.id == "fish").done = false;
            v.isSick = false; v.energy = 30f; v.health = 100f;
            float hurtBefore = v.health;

            Assert.That(gm.IsComplete("fish"), Is.False);
            fish.Eat(gm);
            yield return null;

            Assert.That(gm.IsComplete("fish"), Is.True, "roasting and eating a fish completes the objective");
            Assert.That(v.energy, Is.GreaterThan(70f));
            Assert.That(v.HasFoodPoisoning, Is.False);
            Assert.That(v.health, Is.GreaterThanOrEqualTo(hurtBefore - 0.1f), "cooked fish does not hurt");
        }

        [UnityTest]
        public IEnumerator Objectives_IncludeFishingAndSunrise_InSensibleOrder()
        {
            yield return null;
            var ids = GameManager.Instance.Objectives.Select(o => o.id).ToList();
            Assert.That(ids, Does.Contain("fish"));
            Assert.That(ids, Does.Contain("dawn"));
            Assert.That(ids.IndexOf("fish"), Is.LessThan(ids.IndexOf("shelter")));
            Assert.That(ids.IndexOf("survive"), Is.LessThan(ids.IndexOf("dawn")));
            Assert.That(ids.Last(), Is.EqualTo("dawn"));
        }

        [UnityTest]
        public IEnumerator Visual_NightIsDarkerThanDay_ButNotBlack()
        {
            // Renders the camp at several times of day to PNGs (Temp/claude-shots) and checks the
            // brightness ordering: noon > dusk > moonlit night, with the night clearly not black.
            var dn = Cycle;
            var pit = FirePit.Instance.transform.position;
            var camGo = new GameObject("DayNightTestCam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 70f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.Skybox;
            camGo.transform.position = pit + new Vector3(4f, 1.7f, -5f);
            camGo.transform.rotation = Quaternion.LookRotation(pit + Vector3.up * 1.2f - camGo.transform.position);
            var rt = new RenderTexture(960, 540, 24);
            cam.targetTexture = rt;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Temp", "claude-shots"));
            System.IO.Directory.CreateDirectory(dir);

            var lum = new System.Collections.Generic.Dictionary<string, float>();
            var shots = new (string name, float phase)[]
            {
                ("1-morning", 0.07f), ("2-noon", 0.25f), ("3-dusk", 0.60f), ("4-sunset", 0.66f),
                ("5-night", 0.75f), ("6-deepnight", 0.80f), ("7-predawn", 0.92f), ("8-dawn", 0.97f),
            };
            foreach (var (name, phase) in shots)
            {
                dn.SetPhase(10f + phase);
                yield return null; yield return null;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                var px = tex.GetPixels32();
                double sum = 0;
                foreach (var c in px) sum += 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
                lum[name] = (float)(sum / px.Length / 255.0);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "daynight-" + name + ".png"), tex.EncodeToPNG());
            }
            Object.Destroy(camGo); Object.Destroy(rt); Object.Destroy(tex);
            dn.SetPhase(10f + DayNightCycle.StartPhase);

            string report = string.Join(", ", lum.Select(kv => $"{kv.Key} {kv.Value:F3}"));
            Debug.Log("[DayNight] mean luminance: " + report);
            Assert.That(lum["2-noon"], Is.GreaterThan(lum["3-dusk"]), report);
            Assert.That(lum["3-dusk"], Is.GreaterThan(lum["6-deepnight"]), report);
            Assert.That(lum["6-deepnight"], Is.LessThan(lum["2-noon"] * 0.5f), "night should be clearly darker: " + report);
            Assert.That(lum["6-deepnight"], Is.GreaterThan(0.015f), "moonlit night must not be pitch black: " + report);
            Assert.That(lum["8-dawn"], Is.GreaterThan(lum["6-deepnight"]), report);
        }

        [UnityTest]
        public IEnumerator WristHud_ShowsTheClock()
        {
            var dn = Cycle;
            dn.SetPhase(6.07f);
            yield return null;
            yield return null;
            var hud = Object.FindFirstObjectByType<WristHud>();
            Assert.That(hud, Is.Not.Null);
            Assert.That(hud.ClockLabel, Is.Not.Null, "wrist HUD has no clock label");
            Assert.That(hud.ClockLabel, Does.Match(@"^\d\d:\d\d$"));
            Assert.That(hud.ClockLabel, Is.EqualTo(dn.ClockString).Or.EqualTo(DayNightCycle.FormatClock(DayNightCycle.ClockHours(dn.Phase01 - 0.001f))));
        }
    }
}
