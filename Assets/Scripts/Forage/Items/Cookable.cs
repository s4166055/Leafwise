using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Forage
{
    /// <summary>
    /// Food that can be cooked: leave it on the ground within a meter of the
    /// burning fire for ~10 s and it roasts (darkens, gains food value).
    /// Fish are stricter (<see cref="requiresSkewer"/>): they only roast on a
    /// skewer held or rested over the flames, the way you'd really cook them.
    /// Important lesson: cooking does NOT neutralize deadly mushroom toxins.
    /// </summary>
    public class Cookable : MonoBehaviour
    {
        public float cookSeconds = 10f;
        public float cookRadius = 1.1f;
        [Tooltip("Only cooks when speared on a skewer over the flames (fish).")]
        public bool requiresSkewer;
        [Tooltip("For skewer cooking: how far from the fire's centre (horizontally) still counts as over the flames.")]
        public float overFlameRadius = 0.55f;

        float _hintCooldown;

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
            bool held = _grab != null && _grab.enabled && _grab.isSelected;
            bool overFire;
            if (requiresSkewer)
            {
                var fish = GetComponent<FishItem>();
                bool onStick = fish != null && fish.IsSkewered;
                overFire = false;
                if (fire != null && fire.IsLit)
                {
                    Vector3 d = transform.position - fire.transform.position;
                    float horiz = new Vector2(d.x, d.z).magnitude;
                    overFire = onStick && horiz < overFlameRadius && d.y > 0.1f && d.y < 1.1f;

                    // left lying by (or in) the fire: tell them how it's done
                    _hintCooldown -= Time.deltaTime;
                    if (!onStick && !held && d.magnitude < cookRadius && _hintCooldown <= 0f)
                    {
                        _hintCooldown = 20f;
                        ForageEvents.RaiseHint("fish-needs-skewer");
                    }
                }
            }
            else
            {
                overFire = fire != null && fire.IsLit && !held &&
                    Vector3.Distance(transform.position, fire.transform.position) < cookRadius;
            }

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
