using UnityEngine;

namespace Forage
{
    /// <summary>
    /// The drink button (B / Y on Quest; the simulator's secondary button; J at a desk).
    ///
    /// 1. Holding the bucket or pot near your face → drink a serving from it.
    /// 2. Otherwise, with that hand in the pond → a cupped-hand sip of raw
    ///    water: it refills H2O now, but untreated water makes the H2O bar
    ///    drain faster afterwards (and too many sips make you sick).
    /// </summary>
    public class HandDrinking : MonoBehaviour
    {
        public float sipHydration = 9f;
        public float sipCooldown = 0.8f;
        [Tooltip("How far above the surface a hand still counts as 'in the water'.")]
        public float surfaceSlack = 0.1f;

        float _cooldown;
        int _sips;

        public int SipsTaken => _sips;

        void Update()
        {
            _cooldown -= Time.deltaTime;
            if (_cooldown > 0f) return;

            bool l = PlayerHands.DrinkPressed(true);
            bool r = PlayerHands.DrinkPressed(false);
            if (!l && !r) return;
            PressDrink(l, r);
        }

        /// <summary>
        /// Everything the drink button does, given which hand(s) pressed it.
        /// Public so automated tests can drive the exact same path.
        /// Returns what happened: "bucket", "pot", "sip" or "" (nothing to drink).
        /// </summary>
        public string PressDrink(bool l, bool r)
        {
            _cooldown = sipCooldown;

            // a container held near the face takes priority
            foreach (var b in Bucket.All)
                if (b.TryDrinkNow()) return "bucket";
            foreach (var pot in FindObjectsByType<CookingPot>(FindObjectsSortMode.None))
                if (pot.TryDrinkNow()) return "pot";

            if (r && TrySip(PlayerHands.Right, false)) return "sip";
            if (l && TrySip(PlayerHands.Left, true)) return "sip";

            // keyboard / simulator fallback: if the player is standing at the water's edge,
            // drink with whichever hand is lower
            var gm = GameManager.Instance;
            var water = WaterBody.Instance;
            if (gm != null && water != null && water.InsideFootprint(gm.PlayerPosition, -1.5f))
            {
                var lower = PlayerHands.Left != null && PlayerHands.Right != null &&
                            PlayerHands.Left.position.y < PlayerHands.Right.position.y
                    ? PlayerHands.Left : PlayerHands.Right;
                if (lower != null && lower.position.y < water.surfaceY + 0.35f)
                {
                    Sip(lower.position, lower == PlayerHands.Left);
                    return "sip";
                }
            }
            return "";
        }

        bool TrySip(Transform hand, bool left)
        {
            var water = WaterBody.Instance;
            if (hand == null || water == null) return false;
            if (!water.IsUnderwater(hand.position, surfaceSlack)) return false;
            Sip(hand.position, left);
            return true;
        }

        /// <summary>Public so tests can drive it directly.</summary>
        public void Sip(Vector3 at, bool left)
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.vitals == null) return;
            _sips++;
            gm.vitals.DrinkRaw(sipHydration);
            ProceduralAudio.PlayAt(at, ProceduralAudio.Gulp(), 0.7f);
            ScreenFeedback.DrinkUntreated();
            Haptics.Pulse(0.3f, 0.1f, left, !left);
            if (WaterBody.Instance != null) WaterBody.Instance.Splash(at, 0.25f);
            ForageEvents.RaiseSignal("drank-raw-water");
            ForageEvents.RaiseHint("drank-raw-water");
            if (_sips == 1)
                FactCard.Show("Straight from the pond",
                    "It quenches you now — but untreated water upsets your gut and you lose fluid FASTER " +
                    "(watch the H2O bar). Fill the bucket and boil it on the stand by the fire instead.",
                    good: false);
        }
    }
}
