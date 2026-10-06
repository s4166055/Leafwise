using System;
using UnityEngine;

namespace Forage
{
    /// <summary>
    /// Survival stats for the player. Values are 0..100.
    /// Education-focused: nothing here hard-kills the player — bottoming out
    /// a stat causes weakness/sickness feedback and a Scout lesson instead.
    /// </summary>
    public class PlayerVitals : MonoBehaviour
    {
        [Header("Current values")]
        [Range(0, 100)] public float health = 100f;
        [Range(0, 100)] public float hydration = 80f;
        [Range(0, 100)] public float energy = 75f;   // food
        [Range(0, 100)] public float warmth = 70f;

        [Header("Decay per minute")]
        public float hydrationDecay = 4.5f;
        public float energyDecay = 3.0f;
        public float warmthDecayCold = 5.0f;   // when cold (night / no fire nearby)
        public float warmthRecoverFire = 25f;  // when near a lit fire

        [Header("Untreated water")]
        [Tooltip("Hydration drains this many times faster while untreated water is in your gut.")]
        public float untreatedDrainMultiplier = 3f;
        [Tooltip("Seconds of fast drain added per raw sip from the pond.")]
        public float untreatedSecondsPerSip = 40f;
        [Tooltip("If the fast-drain window stacks past this, you also get sick.")]
        public float untreatedSickThreshold = 150f;

        [Header("State (read-only)")]
        public bool isSick;
        public float untreatedUntil;
        public bool nearFire;
        public bool isNight;

        public event Action<PlayerVitals> Changed;
        /// <summary>Raised with a short reason string when something bad happens (drives Scout + vignette).</summary>
        public event Action<string> Harmed;

        float _sickUntil;
        float _foodPoisonUntil;

        [Header("Food poisoning (raw fish)")]
        [Tooltip("Energy drains this many times faster while food poisoning lasts: you're emptied out.")]
        public float poisonEnergyDrainMultiplier = 3f;

        /// <summary>True while raw fish (or similar) is making you ill and hungrier.</summary>
        public bool HasFoodPoisoning => Time.time < _foodPoisonUntil;
        public float FoodPoisoningSecondsLeft => Mathf.Max(0f, _foodPoisonUntil - Time.time);

        /// <summary>Current energy (food) drain multiplier.</summary>
        public float EnergyDrainMultiplier => HasFoodPoisoning ? poisonEnergyDrainMultiplier : 1f;

        /// <summary>True while untreated water is making you lose fluid faster.</summary>
        public bool HasUntreatedWater => Time.time < untreatedUntil;
        public float UntreatedSecondsLeft => Mathf.Max(0f, untreatedUntil - Time.time);

        /// <summary>Current hydration drain multiplier (sickness and untreated water stack).</summary>
        public float HydrationDrainMultiplier =>
            (isSick ? 2.5f : 1f) * (HasUntreatedWater ? untreatedDrainMultiplier : 1f);

        void Update()
        {
            float dtMin = Time.deltaTime / 60f;

            hydration = Mathf.Max(0, hydration - hydrationDecay * dtMin * HydrationDrainMultiplier);
            energy = Mathf.Max(0, energy - energyDecay * dtMin * EnergyDrainMultiplier);

            if (nearFire)
                warmth = Mathf.Min(100, warmth + warmthRecoverFire * dtMin);
            else if (isNight)
                warmth = Mathf.Max(0, warmth - warmthDecayCold * dtMin);

            // health responds to neglected stats, and slowly recovers when all is well
            float strain = 0f;
            if (hydration <= 0) strain += 4f;
            if (energy <= 0) strain += 2f;
            if (warmth <= 0) strain += 3f;
            if (isSick) strain += 3f;

            if (strain > 0f)
                health = Mathf.Max(5f, health - strain * dtMin * 10f); // floor at 5: weak, never dead
            else
                health = Mathf.Min(100f, health + 2.5f * dtMin * 10f);

            if (isSick && Time.time > _sickUntil)
                isSick = false;

            Changed?.Invoke(this);
        }

        public void Drink(float amount, bool contaminated)
        {
            hydration = Mathf.Min(100, hydration + amount);
            if (contaminated)
            {
                MakeSick(90f);
                AddUntreated(60f);
                Harmed?.Invoke("drank-dirty-water");
            }
            Changed?.Invoke(this);
        }

        /// <summary>
        /// A cupped-hand sip straight from the pond. It DOES refill hydration
        /// right now, but untreated water makes you lose fluid faster for a
        /// while afterwards — and stacking too many sips makes you sick.
        /// </summary>
        public void DrinkRaw(float amount)
        {
            hydration = Mathf.Min(100, hydration + amount);
            AddUntreated(untreatedSecondsPerSip);
            if (UntreatedSecondsLeft > untreatedSickThreshold && !isSick)
            {
                MakeSick(60f);
                Harmed?.Invoke("drank-too-much-untreated-water");
            }
            Changed?.Invoke(this);
        }

        void AddUntreated(float seconds)
        {
            untreatedUntil = Mathf.Min(Mathf.Max(Time.time, untreatedUntil) + seconds, Time.time + 240f);
        }

        public void Eat(float amount, bool poisonous)
        {
            if (poisonous)
            {
                MakeSick(120f);
                health = Mathf.Max(5f, health - 15f);
                Harmed?.Invoke("ate-poisonous-mushroom");
            }
            else
            {
                energy = Mathf.Min(100, energy + amount);
            }
            Changed?.Invoke(this);
        }

        public void Damage(float amount, string reason)
        {
            health = Mathf.Max(5f, health - amount);
            Harmed?.Invoke(reason);
            Changed?.Invoke(this);
        }

        /// <summary>
        /// Raw or spoiled food: an immediate hit to health, then a spell of
        /// sickness during which you lose fluid (thirstier) AND food (hungrier)
        /// much faster, and health keeps draining until it passes.
        /// </summary>
        public void ApplyFoodPoisoning(float seconds, float healthHit, string reason)
        {
            _foodPoisonUntil = Mathf.Max(Time.time, _foodPoisonUntil) + seconds;
            MakeSick(seconds);
            health = Mathf.Max(5f, health - healthHit);
            Harmed?.Invoke(reason);
            Changed?.Invoke(this);
        }

        /// <summary>
        /// Time passes while the player sleeps (or skips the night): the body
        /// still uses up water and food. Sleeping in a shelter also warms and
        /// heals; skipping from the watch only passes the hours.
        /// </summary>
        public void PassTheNight(bool sleeping)
        {
            hydration = Mathf.Max(0f, hydration - 12f);
            energy = Mathf.Max(0f, energy - 12f);
            if (sleeping)
            {
                warmth = Mathf.Min(100f, warmth + 45f);
                health = Mathf.Min(100f, health + 25f);
            }
            Changed?.Invoke(this);
        }

        public void MakeSick(float seconds)
        {
            isSick = true;
            _sickUntil = Mathf.Max(_sickUntil, Time.time + seconds);
        }
    }
}
