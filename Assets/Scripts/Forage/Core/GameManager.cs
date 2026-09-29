using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Forage
{
    [Serializable]
    public class Objective
    {
        public string id;
        public string title;
        public bool done;

        public Objective(string id, string title)
        {
            this.id = id;
            this.title = title;
        }
    }

    /// <summary>
    /// Central game state: the ordered objective checklist and shared references.
    /// Mechanics call <see cref="CompleteObjective"/> when the player succeeds.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        static GameManager _instance;
        /// <summary>Survives mid-play domain reloads by re-finding itself.</summary>
        public static GameManager Instance
        {
            get
            {
                if (_instance == null) _instance = FindFirstObjectByType<GameManager>();
                return _instance;
            }
        }

        [Header("References (assigned by scene builder)")]
        public PlayerVitals vitals;
        public Transform playerHead;   // main camera transform inside the XR rig

        public readonly List<Objective> Objectives = new List<Objective>();

        public event Action<Objective> ObjectiveCompleted;
        public event Action ObjectivesChanged;

        void Awake()
        {
            _instance = this;
            SeedObjectives();
        }

        void SeedObjectives()
        {
            Objectives.Clear();
            Objectives.Add(new Objective("explore", "Explore the forest around the camp"));
            Objectives.Add(new Objective("fire", "Start a campfire with the hand drill"));
            Objectives.Add(new Objective("water", "Boil pond water and drink it safely"));
            Objectives.Add(new Objective("forage", "Eat a safe mushroom"));
            Objectives.Add(new Objective("shelter", "Build the shelter"));
            Objectives.Add(new Objective("wildlife", "Observe wildlife without scaring it"));
            Objectives.Add(new Objective("survive", "Survive until nightfall"));
            ObjectivesChanged?.Invoke();
        }

        public Objective CurrentObjective => Objectives.FirstOrDefault(o => !o.done);

        public bool IsComplete(string id) => Objectives.Any(o => o.id == id && o.done);

        public void CompleteObjective(string id)
        {
            var obj = Objectives.FirstOrDefault(o => o.id == id);
            if (obj == null || obj.done) return;
            obj.done = true;
            ObjectiveCompleted?.Invoke(obj);
            ObjectivesChanged?.Invoke();
            Debug.Log($"[Forage] Objective complete: {obj.title}");
        }

        /// <summary>Player head position — used by animals, Scout and interaction checks.</summary>
        public Vector3 PlayerPosition => playerHead != null ? playerHead.position : Vector3.zero;

        /// <summary>Smoothed player movement speed (m/s) — animals react to fast motion.</summary>
        public float PlayerSpeed { get; private set; }

        Vector3 _lastHeadPos;

        void Update()
        {
            if (playerHead != null)
            {
                float rawSpeed = (playerHead.position - _lastHeadPos).magnitude / Mathf.Max(Time.deltaTime, 1e-5f);
                _lastHeadPos = playerHead.position;
                if (rawSpeed < 30f) // ignore teleport jumps
                    PlayerSpeed = Mathf.Lerp(PlayerSpeed, rawSpeed, 0.1f);
            }

            // first objective: wander a little way out of the camp clearing
            if (!IsComplete("explore"))
            {
                var p = PlayerPosition;
                if (new Vector2(p.x, p.z).magnitude > 14f)
                    CompleteObjective("explore");
            }
        }
    }
}
