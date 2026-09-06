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

        public float eatDistance = 0.30f;
        public float eatHoldSeconds = 0.6f;

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
