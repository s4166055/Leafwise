using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Forage
{
    /// <summary>
    /// Food that can be cooked: leave it on the ground within a meter of the
    /// burning fire for ~10 s and it roasts (darkens, gains food value).
    /// Important lesson: cooking does NOT neutralize deadly mushroom toxins.
    /// </summary>
    public class Cookable : MonoBehaviour
    {
        public float cookSeconds = 10f;
        public float cookRadius = 1.1f;

        [Header("State (read-only)")]
        public bool cooked;
        public float progress;

        XRGrabInteractable _grab;
        Renderer[] _renderers;
        Color[] _originals;
        ParticleSystem _smoke;

        void Awake()
        {
            _grab = GetComponent<XRGrabInteractable>();
        }

        void Start()
        {
            _renderers = GetComponentsInChildren<Renderer>();
            _originals = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
                _originals[i] = _renderers[i].material.GetColor("_BaseColor");
        }

        void Update()
        {
            if (cooked) return;
            var fire = FirePit.Instance;
            bool held = _grab != null && _grab.isSelected;
            bool overFire = fire != null && fire.IsLit && !held &&
                Vector3.Distance(transform.position, fire.transform.position) < cookRadius;

            if (!overFire)
            {
                if (_smoke != null) { var em = _smoke.emission; em.rateOverTime = 0f; }
                return;
            }

            if (_smoke == null) _smoke = FireVfx.Smoke(transform, 0f);
            var e = _smoke.emission;
            e.rateOverTime = 6f;

            progress += Time.deltaTime / cookSeconds;
            float darken = Mathf.Lerp(1f, 0.45f, progress);
            for (int i = 0; i < _renderers.Length; i++)
                _renderers[i].material.SetColor("_BaseColor", _originals[i] * darken);

            if (progress >= 1f)
            {
                cooked = true;
                e.rateOverTime = 0f;
                gameObject.name = "Cooked " + gameObject.name;
                ProceduralAudio.PlayAt(transform.position, ProceduralAudio.Crunch(), 0.5f);
                ForageEvents.RaiseSignal("food-cooked");
            }
        }
    }
}
