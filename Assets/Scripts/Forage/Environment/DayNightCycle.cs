using System.Collections;
using UnityEngine;

namespace Forage
{
    /// <summary>
    /// A looping day. One full cycle lasts <see cref="cycleSeconds"/> (20 minutes
    /// by default) and the session starts in the morning:
    ///
    ///   morning → long bright day → a slow golden dusk → a short moonlit night
    ///   → dawn → next day …
    ///
    /// Roughly 72% of the cycle is daylight and 27% is night, so a night lasts
    /// about five and a half minutes instead of being "forever".
    ///
    /// The scene only has ONE realtime light (the URP asset disables additional
    /// lights), so at night that same directional light is re-used as the moon:
    /// cool blue, dim, high in the sky. The procedural skybox is dimmed so a
    /// light pointing down from "the moon" doesn't paint a daytime sky.
    ///
    /// Night makes Warmth matter (fire / shelter), brings crickets and owls,
    /// completes the "survive" objective at nightfall and "dawn" at sunrise.
    /// The player can skip the night — <see cref="SkipToMorning"/> — by
    /// sleeping in a finished shelter (<see cref="ShelterRest"/>) or by holding
    /// a hand over the wrist watch (<see cref="WristHud"/>).
    /// </summary>
    public class DayNightCycle : MonoBehaviour
    {
        public Light sun;
        [Tooltip("Real seconds for one full day + night loop.")]
        public float cycleSeconds = 1200f;

        [Header("Skipping the night")]
        public float skipDuration = 3.5f;
        public float fadeDuration = 1.2f;

        [Header("State (read-only)")]
        public bool isNight;

        // --- cycle layout (fractions of one loop) -------------------------------
        /// <summary>Where the session begins: mid-morning, sun about 30° up.</summary>
        public const float StartPhase = 0.07f;
        /// <summary>Where a skipped night lands: early morning, sun climbing.</summary>
        public const float MorningPhase = 0.03f;
        public const float SunsetPhase = 0.66f;
        public const float SunrisePhase = 0.935f;
        /// <summary>Below this sun elevation the light switches to moonlight.</summary>
        public const float NightElevation = -3f;
        const float DayFraction = 1f - SunrisePhase + SunsetPhase;   // sunrise → sunset

        static readonly float[] KeyPhase = { 0f, 0.07f, 0.25f, 0.44f, 0.54f, 0.60f, 0.66f, 0.72f, 0.80f, 0.88f, 0.935f };
        static readonly float[] KeyElev = { 6f, 30f, 58f, 40f, 22f, 10f, 0f, -9f, -18f, -9f, 0f };

        static AnimationCurve _elevationCurve;
        static AnimationCurve ElevationCurve
        {
            get
            {
                if (_elevationCurve == null)
                {
                    var c = new AnimationCurve();
                    for (int w = -1; w <= 1; w++)
                        for (int i = 0; i < KeyPhase.Length; i++)
                            c.AddKey(new Keyframe(KeyPhase[i] + w, KeyElev[i]));
                    for (int i = 0; i < c.length; i++) c.SmoothTangents(i, 0f);
                    _elevationCurve = c;
                }
                return _elevationCurve;
            }
        }

        static float Wrap01(float p) => p - Mathf.Floor(p);

        /// <summary>Sun elevation in degrees at a point of the (periodic) cycle.</summary>
        public static float SunElevation(float phase) => ElevationCurve.Evaluate(Wrap01(phase));

        /// <summary>Clock time 0..24 at a point of the cycle (sunrise = 06:00, sunset = 18:00).</summary>
        public static float ClockHours(float phase)
        {
            float q = Wrap01(phase - SunrisePhase);
            float h = q < DayFraction
                ? 6f + 12f * q / DayFraction
                : 18f + 12f * (q - DayFraction) / (1f - DayFraction);
            return h % 24f;
        }

        public static string FormatClock(float hours)
        {
            int total = Mathf.FloorToInt(hours * 60f + 0.5f) % (24 * 60);
            return (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
        }

        // --- runtime state -------------------------------------------------------
        float _phase = StartPhase;     // unwrapped: whole numbers count days
        bool _sawNight;
        bool _summaryPending;
        bool _nightCardShown;
        AudioSource _crickets;
        float _owlTimer = 20f;
        Material _sky;
        float _skyExposure = 1.3f;
        float _skySunSize = 0.04f;

        public float Phase01 => Wrap01(_phase);
        public float Elevation => SunElevation(_phase);
        public float ClockNow => ClockHours(_phase);
        public string ClockString => FormatClock(ClockNow);
        public int Day => Mathf.FloorToInt(_phase) + 1;
        public bool IsSkipping { get; private set; }
        /// <summary>The night can be skipped right now.</summary>
        public bool CanSkipNight => isNight && !IsSkipping;
        /// <summary>1 in the dead of night, 0 in daylight. Handy for tests and the HUD.</summary>
        public float NightAmount => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(10f, -12f, Elevation));

        void Start()
        {
            var sky = RenderSettings.skybox;
            if (sky != null)
            {
                _sky = new Material(sky) { name = "Skybox (runtime)" };
                if (_sky.HasProperty("_Exposure")) _skyExposure = _sky.GetFloat("_Exposure");
                if (_sky.HasProperty("_SunSize")) _skySunSize = _sky.GetFloat("_SunSize");
                RenderSettings.skybox = _sky;
            }
            Refresh();
        }

        void OnDestroy()
        {
            if (_sky != null) Destroy(_sky);
        }

        void Update()
        {
            if (sun == null) return;
            if (!IsSkipping) _phase += Time.deltaTime / Mathf.Max(1f, cycleSeconds);
            Refresh();

            if (_crickets != null)
            {
                float target = isNight ? 0.25f : 0f;
                _crickets.volume = Mathf.MoveTowards(_crickets.volume, target, Time.deltaTime * 0.15f);
            }

            if (isNight)
            {
                _owlTimer -= Time.deltaTime;
                if (_owlTimer <= 0f)
                {
                    _owlTimer = Random.Range(18f, 40f);
                    var gm = GameManager.Instance;
                    if (gm != null)
                        ProceduralAudio.PlayAt(gm.PlayerPosition + Random.onUnitSphere * 15f + Vector3.up * 6f,
                            ProceduralAudio.Hoot(), 0.6f);
                }
            }
        }

        /// <summary>Jump straight to a point of the cycle (tests, debugging).</summary>
        public void SetPhase(float phase)
        {
            _phase = phase;
            Refresh();
        }

        // --- lighting ------------------------------------------------------------

        static readonly Color DaySun = new Color(1f, 0.93f, 0.78f);
        static readonly Color DuskSun = new Color(1f, 0.55f, 0.3f);
        static readonly Color MoonLight = new Color(0.55f, 0.66f, 0.95f);
        static readonly Color DayFog = new Color(0.6f, 0.7f, 0.64f);
        static readonly Color NightFog = new Color(0.05f, 0.08f, 0.14f);
        static readonly Color DaySky = new Color(0.6f, 0.7f, 0.82f);
        static readonly Color NightSky = new Color(0.09f, 0.13f, 0.24f);
        static readonly Color DayEquator = new Color(0.42f, 0.5f, 0.4f);
        static readonly Color NightEquator = new Color(0.07f, 0.10f, 0.17f);
        static readonly Color DayGround = new Color(0.2f, 0.24f, 0.16f);
        static readonly Color NightGround = new Color(0.03f, 0.04f, 0.07f);

        void Refresh()
        {
            if (sun == null) return;
            float elev = Elevation;
            ApplyLighting(elev);

            bool nightNow = elev < 0f;
            if (nightNow != isNight)
            {
                isNight = nightNow;
                var gm = GameManager.Instance;
                if (gm != null && gm.vitals != null) gm.vitals.isNight = isNight;
                if (isNight) OnNightfall(); else OnDawn();
            }
        }

        void ApplyLighting(float elev)
        {
            // where along the day / night arc are we (drives the sun's compass swing)
            float q = Wrap01(_phase - SunrisePhase);
            bool dayArc = q < DayFraction;
            float u = dayArc ? q / DayFraction : (q - DayFraction) / (1f - DayFraction);

            if (elev > NightElevation)
            {
                // sun: warm by day, ember red at dusk and dawn
                float az = Mathf.Lerp(-75f, 75f, dayArc ? u : 1f);
                sun.transform.rotation = Quaternion.Euler(elev, az, 0f);
                sun.color = Color.Lerp(DaySun, DuskSun, Mathf.InverseLerp(26f, 3f, elev));
                sun.intensity = 1.25f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(NightElevation, 8f, elev));
            }
            else
            {
                // moon: the same light, cool and dim, arcing high across the night
                float moonElev = 25f + 25f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(u));
                float az = Mathf.Lerp(-75f, 75f, Mathf.Clamp01(u)) + 180f;
                sun.transform.rotation = Quaternion.Euler(moonElev, az, 0f);
                sun.color = MoonLight;
                sun.intensity = 0.3f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(NightElevation, -10f, elev));
            }

            float nightT = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(10f, -12f, elev));
            RenderSettings.fogColor = Color.Lerp(DayFog, NightFog, nightT);
            RenderSettings.ambientSkyColor = Color.Lerp(DaySky, NightSky, nightT);
            RenderSettings.ambientEquatorColor = Color.Lerp(DayEquator, NightEquator, nightT);
            RenderSettings.ambientGroundColor = Color.Lerp(DayGround, NightGround, nightT);

            if (_sky != null)
            {
                // the procedural sky is drawn from the light's direction, so dim it
                // (and drop the sun disc) before the light turns into the moon
                float skyNight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(5f, -2.5f, elev));
                if (_sky.HasProperty("_Exposure")) _sky.SetFloat("_Exposure", Mathf.Lerp(_skyExposure, 0.07f, skyNight));
                if (_sky.HasProperty("_SunSize")) _sky.SetFloat("_SunSize", Mathf.Lerp(_skySunSize, 0f, skyNight));
            }
        }

        // --- events --------------------------------------------------------------

        void OnNightfall()
        {
            _sawNight = true;
            var gm = GameManager.Instance;
            if (gm == null) return;

            if (_crickets == null)
                _crickets = ProceduralAudio.Loop(transform, ProceduralAudio.Crickets(), 0f, spatial: 0f);
            ProceduralAudio.PlayAt(gm.PlayerPosition + new Vector3(8f, 5f, 3f), ProceduralAudio.Hoot(), 0.8f);

            gm.CompleteObjective("survive");
            ForageEvents.RaiseSignal("nightfall");
            ForageEvents.RaiseHint("night-falls");

            if (!_nightCardShown)
            {
                _nightCardShown = true;
                FactCard.Show(
                    "Night falls",
                    "It gets cold in the dark: Warmth drains unless you stay by the fire or rest in your shelter.\n" +
                    "Crouch down inside the finished shelter to sleep until morning (at a desk: hold C), or hold your free hand over your wrist watch (N at a desk).",
                    good: true);
            }
        }

        void OnDawn()
        {
            var gm = GameManager.Instance;
            ForageEvents.RaiseSignal("dawn");
            if (!_sawNight || gm == null) return;
            _sawNight = false;
            gm.CompleteObjective("dawn");
            _summaryPending = true;
            if (!IsSkipping) ShowSummary();
        }

        void ShowSummary()
        {
            _summaryPending = false;
            var gm = GameManager.Instance;
            if (gm == null) return;
            int done = 0;
            var lines = new System.Text.StringBuilder();
            foreach (var o in gm.Objectives)
            {
                if (o.done) done++;
                lines.Append(o.done ? "[x] " : "[ ] ").Append(o.title).Append('\n');
            }
            FactCard.Show(
                "Sunrise — you made it through the night! (" + done + "/" + gm.Objectives.Count + ")",
                lines + "\nA new day begins. Keep exploring, or finish what is still unchecked.",
                good: true);
        }

        // --- skipping the night --------------------------------------------------

        /// <summary>
        /// Fade out, spin the clock to the next morning, fade back in. Sleeping in
        /// the shelter restores warmth and some health; skipping from the watch
        /// only passes the time. Returns false if it isn't night or a skip is
        /// already running.
        /// </summary>
        public bool SkipToMorning(bool sleeping)
        {
            if (!CanSkipNight) return false;
            StartCoroutine(SkipRoutine(sleeping));
            return true;
        }

        IEnumerator SkipRoutine(bool sleeping)
        {
            IsSkipping = true;
            ForageEvents.RaiseSignal(sleeping ? "sleeping" : "skipping-night");

            ScreenFeedback.FadeTo(1f, fadeDuration);
            yield return new WaitForSeconds(fadeDuration + 0.1f);

            float start = _phase;
            float target = Mathf.Floor(start - MorningPhase) + 1f + MorningPhase;
            float dur = Mathf.Max(0.05f, skipDuration);
            for (float t = 0f; t < dur; t += Time.deltaTime)
            {
                _phase = Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t / dur));
                Refresh();
                yield return null;
            }
            _phase = target;
            Refresh();

            var gm = GameManager.Instance;
            if (gm != null && gm.vitals != null) gm.vitals.PassTheNight(sleeping);
            ForageEvents.RaiseSignal(sleeping ? "slept" : "skipped-night");

            yield return new WaitForSeconds(0.5f);
            ScreenFeedback.FadeTo(0f, fadeDuration);
            yield return new WaitForSeconds(fadeDuration);

            IsSkipping = false;
            if (_summaryPending) ShowSummary();
        }
    }
}
