using UnityEngine;

namespace Forage
{
    /// <summary>
    /// A session-length day: the sun arcs down over ~10 minutes into dusk and
    /// night. Night makes Warmth matter (fire/shelter), swaps birdsong for
    /// crickets and owl calls, completes the survive objective and shows the
    /// end-of-session lessons summary.
    /// </summary>
    public class DayNightCycle : MonoBehaviour
    {
        public Light sun;
        public float dayLengthSeconds = 600f; // sunset arc; night continues after

        [Header("State (read-only)")]
        public bool isNight;

        float _t;
        bool _nightfallHandled;
        AudioSource _crickets;
        float _owlTimer = 20f;

        static readonly Color DaySun = new Color(1f, 0.93f, 0.78f);
        static readonly Color DuskSun = new Color(1f, 0.55f, 0.3f);
        static readonly Color DayFog = new Color(0.6f, 0.7f, 0.64f);
        static readonly Color NightFog = new Color(0.07f, 0.1f, 0.14f);

        void Update()
        {
            if (sun == null) return;
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / dayLengthSeconds);

            // sun elevation 38° -> -14° (below horizon)
            float elevation = Mathf.Lerp(38f, -14f, k);
            sun.transform.rotation = Quaternion.Euler(elevation, -38f + k * 30f, 0f);

            // light: warm day -> ember dusk -> off
            float duskT = Mathf.InverseLerp(14f, 2f, elevation);   // 0 day .. 1 deep dusk
            float nightT = Mathf.InverseLerp(4f, -8f, elevation);  // 0 .. 1 night
            sun.color = Color.Lerp(DaySun, DuskSun, duskT);
            sun.intensity = Mathf.Lerp(1.25f, 0f, nightT);

            RenderSettings.fogColor = Color.Lerp(DayFog, NightFog, nightT);
            RenderSettings.ambientSkyColor = Color.Lerp(new Color(0.6f, 0.7f, 0.82f), new Color(0.06f, 0.09f, 0.16f), nightT);
            RenderSettings.ambientEquatorColor = Color.Lerp(new Color(0.42f, 0.5f, 0.4f), new Color(0.05f, 0.07f, 0.1f), nightT);
            RenderSettings.ambientGroundColor = Color.Lerp(new Color(0.2f, 0.24f, 0.16f), new Color(0.03f, 0.04f, 0.05f), nightT);

            bool nightNow = elevation < 4f;
            if (nightNow != isNight)
            {
                isNight = nightNow;
                var gm = GameManager.Instance;
                if (gm != null && gm.vitals != null) gm.vitals.isNight = isNight;
            }

            if (isNight)
            {
                if (!_nightfallHandled) OnNightfall();
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

        void OnNightfall()
        {
            _nightfallHandled = true;
            var gm = GameManager.Instance;
            if (gm == null) return;

            _crickets = ProceduralAudio.Loop(transform, ProceduralAudio.Crickets(), 0.25f, spatial: 0f);
            ProceduralAudio.PlayAt(gm.PlayerPosition + new Vector3(8f, 5f, 3f), ProceduralAudio.Hoot(), 0.8f);

            gm.CompleteObjective("survive");
            ShowSummary(gm);
            ForageEvents.RaiseSignal("nightfall");
        }

        void ShowSummary(GameManager gm)
        {
            int done = 0;
            var lines = new System.Text.StringBuilder();
            foreach (var o in gm.Objectives)
            {
                if (o.done) done++;
                lines.Append(o.done ? "[x] " : "[ ] ").Append(o.title).Append('\n');
            }
            FactCard.Show(
                "Nightfall — you survived the day! (" + done + "/" + gm.Objectives.Count + ")",
                lines + "\nStay warm: keep the fire fed, or rest in your shelter.",
                good: true);
        }
    }
}
