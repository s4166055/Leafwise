using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Forage
{
    /// <summary>
    /// A forageable mushroom. Pick it up and bring it to your mouth to eat.
    /// Safe species feed you; poisonous ones make you sick — either way a
    /// fact card teaches what you just ate and how to recognise it.
    /// </summary>
    public class Mushroom : MonoBehaviour
    {
        public string speciesName;
        [TextArea] public string fact;
        public bool poisonous;
        [Tooltip("Where this species grows — shown when the player notices it.")]
        public string habitatNote = "the forest";

        public float eatDistance = 0.30f;
        public float eatHoldSeconds = 0.6f;

        [Header("Proximity reaction")]
        public float noticeDistance = 2.6f;

        XRGrabInteractable _grab;
        float _eatTimer;
        static float _lastWarnTime = -99f;
        GameObject _tag;

        void Awake()
        {
            _grab = GetComponent<XRGrabInteractable>();
            if (_grab != null)
            {
                _grab.selectEntered.AddListener(_ => ShowTag());
                _grab.selectExited.AddListener(_ => HideTag());
            }
        }

        bool _noticed;
        Vector3 _restScale = Vector3.one;

        /// <summary>
        /// Foraging is about noticing. Walk close and the mushroom gently
        /// "presents" itself — a small rise and a one-time Scout note naming
        /// the species and where it grows — so players learn to read habitat,
        /// not just colour.
        /// </summary>
        void ReactToProximity(GameManager gm)
        {
            bool held = _grab != null && _grab.isSelected;
            if (held) return;

            Vector3 p = gm.PlayerPosition;
            float dist = Vector2.Distance(new Vector2(transform.position.x, transform.position.z),
                                          new Vector2(p.x, p.z));

            if (dist < noticeDistance)
            {
                // subtle lift + sway so it catches the eye in undergrowth
                float t = 1f - Mathf.Clamp01(dist / noticeDistance);
                float sway = Mathf.Sin(Time.time * 2.4f + transform.position.x) * 2.5f * t;
                transform.localScale = Vector3.Lerp(transform.localScale, _restScale * (1f + t * 0.18f),
                    Time.deltaTime * 5f);
                transform.localRotation = Quaternion.Euler(sway, transform.localRotation.eulerAngles.y, 0f);

                if (!_noticed && dist < noticeDistance * 0.65f)
                {
                    _noticed = true;
                    ForageEvents.RaiseSignal("mushroom-noticed");
                    var scout = FindFirstObjectByType<ScoutCompanion>();
                    if (scout != null)
                        scout.Say(poisonous
                            ? $"Careful — that looks like {speciesName}. They grow in {habitatNote}. Read the tag before you touch your mouth."
                            : $"That's {speciesName} — they grow in {habitatNote}. Looks like a safe find.");
                }
            }
            else if (transform.localScale != _restScale)
            {
                transform.localScale = Vector3.Lerp(transform.localScale, _restScale, Time.deltaTime * 4f);
            }
        }

        /// <summary>Small color-coded name tag floating above the mushroom while held.</summary>
        void ShowTag()
        {
            if (_tag != null) return;
            _tag = new GameObject("MushroomTag", typeof(Canvas));
            _tag.transform.SetParent(transform, false);
            _tag.transform.localPosition = Vector3.up * 0.16f;
            var rect = _tag.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(560, 110);
            rect.localScale = Vector3.one * 0.00032f; // ~18cm x 3.5cm

            var accent = poisonous ? new Color(0.88f, 0.34f, 0.28f) : new Color(0.45f, 0.72f, 0.35f);
            FactCard.MakeRect(rect, "Bg", new Color(0.08f, 0.1f, 0.07f, 0.82f));
            var text = FactCard.MakeText(rect, "Name",
                speciesName + (poisonous ? "  —  DO NOT EAT" : "  —  edible"), 44,
                new Vector2(0.04f, 0.08f), new Vector2(0.96f, 0.92f));
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.color = accent * 1.3f;
            text.fontStyle = TMPro.FontStyles.Bold;
        }

        void HideTag()
        {
            if (_tag != null) Destroy(_tag);
        }

        void LateUpdate()
        {
            if (_tag == null) return;
            var gm = GameManager.Instance;
            if (gm == null || gm.playerHead == null) return;
            Vector3 to = _tag.transform.position - gm.playerHead.position;
            if (to.sqrMagnitude > 0.001f)
                _tag.transform.rotation = Quaternion.LookRotation(to);
        }

        void Update()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.playerHead == null) return;

            ReactToProximity(gm);

            bool held = _grab != null && _grab.isSelected;
            bool atMouth = Vector3.Distance(transform.position, gm.playerHead.position) < eatDistance;

            if (held && atMouth)
            {
                if (poisonous && _eatTimer == 0f && Time.time - _lastWarnTime > 12f)
                {
                    _lastWarnTime = Time.time;
                    ForageEvents.RaiseHint("about-to-eat-suspicious-mushroom");
                }
                _eatTimer += Time.deltaTime;
                // bite animation: the mushroom visibly shrinks as you eat
                transform.localScale = Vector3.one * (1f - Mathf.Clamp01(_eatTimer / eatHoldSeconds) * 0.45f);
                if (_eatTimer >= eatHoldSeconds)
                    Eat(gm);
            }
            else
            {
                if (_eatTimer > 0f) transform.localScale = Vector3.one;
                _eatTimer = 0f;
            }
        }

        public void Eat(GameManager gm)
        {
            var cookable = GetComponent<Cookable>();
            bool cooked = cookable != null && cookable.cooked;
            // cooking makes safe food more nourishing — but NEVER detoxifies deadly species
            gm.vitals.Eat(cooked ? 45f : 30f, poisonous);
            if (cooked && poisonous)
                FactCard.Show("Cooking does NOT make it safe!",
                    "Heat does not destroy the toxins in deadly mushrooms like the Death Cap. " +
                    "If a mushroom is poisonous raw, it is poisonous cooked.", good: false);
            ProceduralAudio.PlayAt(transform.position, ProceduralAudio.Crunch(), 0.9f);
            Haptics.Pulse(0.25f, 0.1f);
            if (!poisonous) ScreenFeedback.Eat(); // poison path flashes red via vitals.Harmed

            if (poisonous)
            {
                ForageEvents.RaiseSignal("ate-poisonous-mushroom");
                FactCard.Show("Oh no — " + speciesName + "!", fact, good: false);
            }
            else
            {
                ForageEvents.RaiseSignal("ate-safe-mushroom");
                FactCard.Show(speciesName + " — good choice!", fact, good: true);
                gm.CompleteObjective("forage");
            }

            Destroy(gameObject);
        }
    }
}
