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

        [Header("State (read-only)")]
        public bool isSick;
        public bool nearFire;
        public bool isNight;

        public event Action<PlayerVitals> Changed;
        /// <summary>Raised with a short reason string when something bad happens (drives Scout + vignette).</summary>
        public event Action<string> Harmed;

        float _sickUntil;

        void Update()
        {
            float dtMin = Time.deltaTime / 60f;

            hydration = Mathf.Max(0, hydration - hydrationDecay * dtMin * (isSick ? 2.5f : 1f));
            energy = Mathf.Max(0, energy - energyDecay * dtMin);

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
                Harmed?.Invoke("drank-dirty-water");
            }
            Changed?.Invoke(this);
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

        public void MakeSick(float seconds)
        {
            isSick = true;
            _sickUntil = Time.time + seconds;
        }
    }
}
