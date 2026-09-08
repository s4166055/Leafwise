using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Forage
{
    /// <summary>
    /// A caught fish. Eat it raw and risk your stomach — or roast it by the
    /// fire (Cookable) for a proper meal.
    /// </summary>
    public class FishItem : MonoBehaviour
    {
        public float eatDistance = 0.30f;
        public float eatHoldSeconds = 0.6f;

        XRGrabInteractable _grab;
        Cookable _cookable;
        float _eatTimer;

        void Awake()
        {
            _grab = GetComponent<XRGrabInteractable>();
            _cookable = GetComponent<Cookable>();
        }

        void Update()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.playerHead == null) return;

            bool held = _grab != null && _grab.isSelected;
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
            }
            else
            {
                gm.vitals.Eat(15f, poisonous: false);
                gm.vitals.MakeSick(45f);
                gm.vitals.Damage(4f, "ate-raw-fish");
                FactCard.Show("Raw fish… risky.",
                    "Freshwater fish often carry parasites — eating them raw can make you sick. " +
                    "Roast your catch by the fire first.", good: false);
                ForageEvents.RaiseSignal("ate-raw-fish");
            }
            Destroy(gameObject);
        }
    }
}
