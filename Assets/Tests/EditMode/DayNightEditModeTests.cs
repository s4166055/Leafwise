using NUnit.Framework;
using UnityEngine;

namespace Forage.Tests
{
    /// <summary>
    /// Pure-function checks for the looping day/night cycle, the sleep rule and
    /// the watch gesture. No scene or headset needed.
    /// </summary>
    public class DayNightEditModeTests
    {
        const int Steps = 2000;

        [Test]
        public void Cycle_StartsInTheMorning_SunWellAboveTheHorizon()
        {
            float e = DayNightCycle.SunElevation(DayNightCycle.StartPhase);
            Assert.That(e, Is.InRange(25f, 35f), "session should start in bright morning");
            Assert.That(DayNightCycle.ClockHours(DayNightCycle.StartPhase), Is.InRange(7f, 10f));
        }

        [Test]
        public void Cycle_IsPeriodic()
        {
            for (int i = 0; i < 50; i++)
            {
                float p = i / 50f;
                Assert.That(DayNightCycle.SunElevation(p + 1f), Is.EqualTo(DayNightCycle.SunElevation(p)).Within(0.001f));
                Assert.That(DayNightCycle.SunElevation(p + 3f), Is.EqualTo(DayNightCycle.SunElevation(p)).Within(0.001f));
            }
            // and the seam at phase 0 / 1 is continuous
            Assert.That(DayNightCycle.SunElevation(0.9999f), Is.EqualTo(DayNightCycle.SunElevation(0.0001f)).Within(0.1f));
        }

        [Test]
        public void Night_IsAMinorityOfTheLoop_AndMorningIsNotCramped()
        {
            int night = 0, bright = 0;
            for (int i = 0; i < Steps; i++)
            {
                float e = DayNightCycle.SunElevation(i / (float)Steps);
                if (e < 0f) night++;
                if (e > 15f) bright++;
            }
            float nightFrac = night / (float)Steps, brightFrac = bright / (float)Steps;
            Assert.That(nightFrac, Is.InRange(0.15f, 0.35f), "night should be about a quarter of the loop");
            Assert.That(brightFrac, Is.GreaterThan(0.4f), "most of the loop should be properly bright daylight");
        }

        [Test]
        public void Dusk_IsSlow()
        {
            // 15° above the horizon down to sunset used to take a couple of minutes at most
            // of a 10 minute day; it should now take a decent slice of the 20 minute loop.
            float a = Find(0.45f, DayNightCycle.SunsetPhase, 15f);
            float dusk = (DayNightCycle.SunsetPhase - a) * 1200f;
            Assert.That(dusk, Is.GreaterThan(90f), "dusk (15° -> horizon) lasts " + dusk + " s");
        }

        static float Find(float from, float to, float elev)
        {
            for (int i = 0; i <= 1000; i++)
            {
                float p = Mathf.Lerp(from, to, i / 1000f);
                if (DayNightCycle.SunElevation(p) <= elev) return p;
            }
            return to;
        }

        [Test]
        public void SunMovesSmoothly()
        {
            float prev = DayNightCycle.SunElevation(0f);
            for (int i = 1; i <= Steps; i++)
            {
                float e = DayNightCycle.SunElevation(i / (float)Steps);
                Assert.That(Mathf.Abs(e - prev), Is.LessThan(0.5f), $"sun jumped at phase {i / (float)Steps:F3}");
                prev = e;
            }
        }

        [Test]
        public void SunriseAndSunset_AreOnTheHorizon()
        {
            Assert.That(DayNightCycle.SunElevation(DayNightCycle.SunsetPhase), Is.EqualTo(0f).Within(0.6f));
            Assert.That(DayNightCycle.SunElevation(DayNightCycle.SunrisePhase), Is.EqualTo(0f).Within(0.6f));
            Assert.That(DayNightCycle.SunElevation(0.8f), Is.LessThan(-10f), "deep night has the sun well below the horizon");
        }

        [Test]
        public void SkippedNight_LandsInEarlyMorning()
        {
            float e = DayNightCycle.SunElevation(DayNightCycle.MorningPhase);
            Assert.That(e, Is.InRange(8f, 25f));
            Assert.That(DayNightCycle.ClockHours(DayNightCycle.MorningPhase), Is.InRange(6f, 8f));
        }

        [Test]
        public void Clock_RunsForwardAndHitsSunriseAndSunset()
        {
            Assert.That(DayNightCycle.ClockHours(DayNightCycle.SunrisePhase), Is.EqualTo(6f).Within(0.05f));
            Assert.That(DayNightCycle.ClockHours(DayNightCycle.SunsetPhase), Is.EqualTo(18f).Within(0.05f));

            float prev = DayNightCycle.ClockHours(DayNightCycle.SunrisePhase);
            float unwrapped = prev;
            for (int i = 1; i <= Steps; i++)
            {
                float h = DayNightCycle.ClockHours(DayNightCycle.SunrisePhase + i / (float)Steps);
                float d = h - prev;
                if (d < -12f) d += 24f;   // midnight wrap
                Assert.That(d, Is.GreaterThan(0f), "clock ran backwards at step " + i);
                unwrapped += d;
                prev = h;
            }
            Assert.That(unwrapped - 6f, Is.EqualTo(24f).Within(0.1f), "one loop = 24 clock hours");
        }

        [Test]
        public void FormatClock_IsHHMM()
        {
            Assert.That(DayNightCycle.FormatClock(6.5f), Is.EqualTo("06:30"));
            Assert.That(DayNightCycle.FormatClock(18f), Is.EqualTo("18:00"));
            Assert.That(DayNightCycle.FormatClock(23.999f), Is.EqualTo("00:00"));
        }

        // --- sleeping in the shelter -----------------------------------------------

        static readonly Vector3 Rest = new Vector3(2f, 0.5f, -3f);
        const float Ground = 0.5f;

        static ShelterRest.Reason Eval(Vector3 head, bool complete = true, bool night = true, bool busy = false) =>
            ShelterRest.Evaluate(head, Rest, Ground, complete, night, busy, 1.3f, 1.15f);

        [Test]
        public void Sleep_NeedsCrouchingInsideACompleteShelterAtNight()
        {
            var crouched = new Vector3(Rest.x + 0.2f, Ground + 0.9f, Rest.z);
            var standing = new Vector3(Rest.x + 0.2f, Ground + 1.65f, Rest.z);
            var outside = new Vector3(Rest.x + 4f, Ground + 0.9f, Rest.z);

            Assert.That(Eval(crouched), Is.EqualTo(ShelterRest.Reason.Ok));
            Assert.That(Eval(standing), Is.EqualTo(ShelterRest.Reason.StandingUp));
            Assert.That(Eval(outside), Is.EqualTo(ShelterRest.Reason.TooFar));
            Assert.That(Eval(crouched, complete: false), Is.EqualTo(ShelterRest.Reason.ShelterIncomplete));
            Assert.That(Eval(crouched, night: false), Is.EqualTo(ShelterRest.Reason.NotNight));
            Assert.That(Eval(crouched, busy: true), Is.EqualTo(ShelterRest.Reason.Busy));
        }

        [Test]
        public void Sleep_DesktopCrouchKeyCountsAsCrouching()
        {
            var standing = new Vector3(Rest.x + 0.2f, Ground + 1.65f, Rest.z);
            var far = new Vector3(Rest.x + 4f, Ground + 1.65f, Rest.z);
            Assert.That(ShelterRest.Evaluate(standing, Rest, Ground, true, true, false, 1.3f, 1.15f, crouchKeyHeld: false),
                Is.EqualTo(ShelterRest.Reason.StandingUp));
            Assert.That(ShelterRest.Evaluate(standing, Rest, Ground, true, true, false, 1.3f, 1.15f, crouchKeyHeld: true),
                Is.EqualTo(ShelterRest.Reason.Ok));
            // the key does not let you sleep outside, in daylight, or in an unfinished shelter
            Assert.That(ShelterRest.Evaluate(far, Rest, Ground, true, true, false, 1.3f, 1.15f, true), Is.EqualTo(ShelterRest.Reason.TooFar));
            Assert.That(ShelterRest.Evaluate(standing, Rest, Ground, true, false, false, 1.3f, 1.15f, true), Is.EqualTo(ShelterRest.Reason.NotNight));
            Assert.That(ShelterRest.Evaluate(standing, Rest, Ground, false, true, false, 1.3f, 1.15f, true), Is.EqualTo(ShelterRest.Reason.ShelterIncomplete));
        }

        // --- the watch gesture -----------------------------------------------------

        [Test]
        public void Watch_HandMustBeClose()
        {
            var watch = new Vector3(0f, 1.2f, 0.3f);
            Assert.That(WristHud.HandNearWatch(watch + new Vector3(0.05f, 0f, 0f), watch), Is.True);
            Assert.That(WristHud.HandNearWatch(watch + new Vector3(0.5f, 0f, 0f), watch), Is.False);
        }
    }
}
