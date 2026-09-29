using UnityEngine;

namespace Forage
{
    /// <summary>
    /// Wires the sensory layer together: forest ambience + pond audio loops,
    /// objective chime + haptic on completion, damage vignette + rumble on harm.
    /// </summary>
    public class AmbienceAndFeedback : MonoBehaviour
    {
        GameManager _gm;

        void Start()
        {
            _gm = GameManager.Instance;

            // 2D forest bed
            ProceduralAudio.Loop(transform, ProceduralAudio.ForestAmbience(), 0.32f, spatial: 0f);

            // water lapping at the pond
            var forest = ForestGenerator.Instance;
            if (forest != null)
            {
                var waterGo = new GameObject("PondAudioAnchor");
                waterGo.transform.position = new Vector3(forest.pondCenter.x, -0.3f, forest.pondCenter.y);
                ProceduralAudio.Loop(waterGo.transform, ProceduralAudio.WaterLap(), 0.7f, spatial: 1f, range: 16f);
            }

            // occasional bird chirps from random directions (daytime feel)
            InvokeRepeating(nameof(RandomChirp), 4f, 7f);

            if (_gm != null)
            {
                _gm.ObjectiveCompleted += OnObjectiveCompleted;
                if (_gm.vitals != null) _gm.vitals.Harmed += OnHarmed;
            }
        }

        void OnDestroy()
        {
            if (_gm != null)
            {
                _gm.ObjectiveCompleted -= OnObjectiveCompleted;
                if (_gm.vitals != null) _gm.vitals.Harmed -= OnHarmed;
            }
        }

        void RandomChirp()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            var offset = Random.onUnitSphere * 12f;
            offset.y = Mathf.Abs(offset.y) * 0.5f + 3f;
            ProceduralAudio.PlayAt(gm.PlayerPosition + offset, ProceduralAudio.Chirp(Random.Range(0, 4)), 0.5f);
        }

        void OnObjectiveCompleted(Objective obj)
        {
            var gm = GameManager.Instance;
            ProceduralAudio.PlayAt(gm.PlayerPosition, ProceduralAudio.Chime(), 0.8f);
            Haptics.Pulse(0.4f, 0.15f);
            FactCard.Show("Objective complete!", obj.title, good: true);
        }

        void OnHarmed(string reason)
        {
            ScreenFeedback.Damage();
            Haptics.Pulse(0.8f, 0.25f);
        }
    }
}
