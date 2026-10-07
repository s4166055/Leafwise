using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Forage
{
    /// <summary>
    /// A caught fish. Eat it raw and risk your stomach — or spear it on a
    /// skewer and roast it over the fire (Cookable) for a proper meal. It can
    /// be eaten straight off the skewer.
    /// </summary>
    public class FishItem : MonoBehaviour
    {
        public float eatDistance = 0.30f;
        public float eatHoldSeconds = 0.6f;

        /// <summary>Raw-fish illness (tunable): food gained, sickness length, instant health hit.</summary>
        public const float RawFoodValue = 12f;
        public const float PoisoningSeconds = 75f;
        public const float PoisoningHealthHit = 8f;

        XRGrabInteractable _grab;
        Cookable _cookable;
        float _eatTimer;

        /// <summary>The skewer this fish is on, if any.</summary>
        public Skewer OnSkewer { get; private set; }
        public bool IsSkewered => OnSkewer != null;
        public void MarkSkewered(Skewer s) => OnSkewer = s;

        void Awake()
        {
            _grab = GetComponent<XRGrabInteractable>();
            _cookable = GetComponent<Cookable>();
        }

        void Update()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.playerHead == null) return;

            // held directly, or held by its skewer
            bool held = (_grab != null && _grab.enabled && _grab.isSelected) ||
                        (OnSkewer != null && OnSkewer.HeldByHand);
            bool atMouth = Vector3.Distance(transform.position, gm.playerHead.position) < eatDistance;

            if (held && atMouth)
            {
                _eatTimer += Time.deltaTime;
                float bite = Mathf.Clamp01(_eatTimer / eatHoldSeconds);
                transform.localScale = Vector3.one * (1f - bite * 0.4f);
                if (_eatTimer >= eatHoldSeconds) Eat(gm);
            }
            else
            {
                _eatTimer = 0f;
            }
        }

        public void Eat(GameManager gm)
        {
            if (_cookable == null) _cookable = GetComponent<Cookable>(); // added after Awake at build time
            bool cooked = _cookable != null && _cookable.cooked;
            ProceduralAudio.PlayAt(transform.position, ProceduralAudio.Crunch(), 0.9f);
            Haptics.Pulse(0.25f, 0.1f);

            if (cooked)
            {
                gm.vitals.Eat(50f, poisonous: false);
                ScreenFeedback.Eat();
                FactCard.Show("Roasted fish — a real meal!",
                    "Cooking fish kills parasites and bacteria and makes protein easier to digest. " +
                    "In survival, always cook your catch when you can.", good: true);
                ForageEvents.RaiseSignal("ate-cooked-fish");
                gm.CompleteObjective("fish");
            }
            else
            {
                // raw fish: a little food, but parasites and bacteria make you properly ill —
                // an immediate hit to health, then a spell of vomiting and cramps that drains
                // water, food AND health until it passes
                gm.vitals.Eat(RawFoodValue, poisonous: false);
                gm.vitals.ApplyFoodPoisoning(PoisoningSeconds, PoisoningHealthHit, "ate-raw-fish");
                FactCard.Show("Raw fish — you feel sick!",
                    "Raw freshwater fish carries parasites and bacteria. Your stomach cramps: you'll lose health, " +
                    "and burn through water and food much faster for a while. Roast your catch on a skewer first.",
                    good: false);
                ForageEvents.RaiseSignal("ate-raw-fish");
                ForageEvents.RaiseHint("ate-raw-fish");
            }
            Destroy(gameObject);
        }
    }
}
