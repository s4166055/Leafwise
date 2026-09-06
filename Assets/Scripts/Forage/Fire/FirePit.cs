using UnityEngine;

namespace Forage
{
    /// <summary>
    /// The campfire. Needs tinder + at least one stick placed inside the stone
    /// ring, then heat from the hand drill. Ember at 60 heat, flames at 100.
    /// While burning it warms the player, cooks the pot (C3) and scares off
    /// dangerous animals (C6).
    /// </summary>
    public class FirePit : MonoBehaviour
    {
        public enum FireState { Unlit, Ember, Burning }

        [Header("Tuning")]
        public float heatToEmber = 60f;
        public float heatToFlame = 100f;
        public float heatDecayPerSecond = 7f;
        public float decayGraceSeconds = 1.2f;
        public float warmthRadius = 3.5f;
        public float burnSecondsPerStick = 150f;

        [Header("State (read-only)")]
        public FireState state = FireState.Unlit;
        public float heat;
        public int tinderCount;
        public int stickCount;
        public int wetStickCount;
        public bool hasWetWood => wetStickCount > 0;

        /// <summary>
        /// Heat multiplier from fuel dampness. All dry = 1.0, all damp = 0.25.
        /// Scaling by ratio (rather than a sticky flag) means piling on dry
        /// wood recovers a damp pit — the lesson stays, the dead end doesn't.
        /// </summary>
        public float DryFactor => stickCount == 0
            ? 1f
            : Mathf.Lerp(1f, 0.25f, (float)wetStickCount / stickCount);

        public bool IsLit => state == FireState.Burning;

        static FirePit _instance;
        /// <summary>Survives mid-play domain reloads by re-finding itself.</summary>
        public static FirePit Instance
        {
            get
            {
                if (_instance == null) _instance = FindFirstObjectByType<FirePit>();
                return _instance;
            }
        }

        float _lastHeatTime;
        float _fuelSeconds;
        ParticleSystem _flame, _smoke, _embers;
        Light _light;
        float _hintCooldown;

        void Awake()
        {
            _instance = this;
            _light = new GameObject("FireLight").AddComponent<Light>();
            _light.transform.SetParent(transform, false);
            _light.transform.localPosition = Vector3.up * 0.5f;
            _light.type = LightType.Point;
            _light.color = new Color(1f, 0.72f, 0.34f); // warm firelight, not red
            _light.range = 7f;
            _light.intensity = 0f;
            _light.shadows = LightShadows.None;
        }

        void Update()
        {
            switch (state)
            {
                case FireState.Unlit:
                case FireState.Ember:
                    if (heat > 0 && Time.time - _lastHeatTime > decayGraceSeconds)
                        heat = Mathf.Max(0, heat - heatDecayPerSecond * Time.deltaTime);
                    if (state == FireState.Ember && heat < heatToEmber * 0.5f)
                        SetState(FireState.Unlit);
                    break;

                case FireState.Burning:
                    _fuelSeconds -= Time.deltaTime;
                    _light.intensity = (1.6f + Mathf.PerlinNoise(Time.time * 6f, 0.3f) * 0.8f) * _fireScale;
                    _light.range = 7f * _fireScale;
                    if (_fuelSeconds <= 0)
                    {
                        SetState(FireState.Unlit);
                        heat = 0;
                        stickCount = 0;
                        tinderCount = 0;
                        wetStickCount = 0;
                        ForageEvents.RaiseSignal("fire-out");
                    }
                    break;
            }

            // warm the player when near a burning fire
            var gm = GameManager.Instance;
            if (gm != null && gm.vitals != null)
                gm.vitals.nearFire = IsLit &&
                    Vector3.Distance(gm.PlayerPosition, transform.position) < warmthRadius;

            _hintCooldown -= Time.deltaTime;
        }

        /// <summary>Called by the drill while spinning against the fireboard. Returns true if heat was applied.</summary>
        public bool AddHeat(float amount)
        {
            if (state == FireState.Burning) return false;

            if (tinderCount == 0)
            {
                TryHint("fire-no-tinder");
                return false;
            }
            if (stickCount == 0)
            {
                TryHint("fire-no-wood");
                return false;
            }
            if (hasWetWood)
            {
                amount *= DryFactor;
                if (heat > 15f) TryHint("wood-damp");
            }

            heat = Mathf.Min(heatToFlame, heat + amount);
            _lastHeatTime = Time.time;

            if (heat >= heatToFlame)
                Ignite();
            else if (heat >= heatToEmber && state == FireState.Unlit)
                SetState(FireState.Ember);
            return true;
        }

        public void AddItem(SurvivalItem item)
        {
            if (item.kind == ItemKind.Tinder) tinderCount++;
            else if (item.kind == ItemKind.Stick)
            {
                stickCount++;
                if (item.isWet) wetStickCount++;
                // feeding a burning fire extends and enlarges it
                if (state == FireState.Burning)
                {
                    _fuelSeconds += burnSecondsPerStick;
                    ScaleFireToFuel();
                }
            }
            else return;

            // consume the item visually into the pit
            item.transform.SetParent(transform, true);
            var rb = item.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
            foreach (var c in item.GetComponentsInChildren<Collider>()) c.enabled = false;
            var grab = item.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            if (grab != null) grab.enabled = false;

            // pile items loosely in the ring
            item.transform.position = transform.position + new Vector3(
                Random.Range(-0.12f, 0.12f), 0.08f + stickCount * 0.03f, Random.Range(-0.12f, 0.12f));
            item.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360f), Random.Range(-10f, 10f));

            ForageEvents.RaiseSignal("fuel-added");
        }

        void Ignite()
        {
            SetState(FireState.Burning);
            _fuelSeconds = Mathf.Max(1, stickCount) * burnSecondsPerStick;
            ScaleFireToFuel();
            if (GameManager.Instance != null)
                GameManager.Instance.CompleteObjective("fire");
            ForageEvents.RaiseSignal("fire-lit");
        }

        float _fireScale = 1f;

        /// <summary>More wood = a bigger, brighter, hotter fire.</summary>
        void ScaleFireToFuel()
        {
            if (state != FireState.Burning || _flame == null) return;
            float t = Mathf.Clamp01((stickCount - 1) / 6f); // 1 stick = small, 7+ = roaring

            var flameEmission = _flame.emission;
            flameEmission.rateOverTime = Mathf.Lerp(95f, 260f, t);
            var flameMain = _flame.main;
            flameMain.startSize = new ParticleSystem.MinMaxCurve(
                Mathf.Lerp(0.14f, 0.24f, t), Mathf.Lerp(0.32f, 0.6f, t));
            flameMain.startSpeed = new ParticleSystem.MinMaxCurve(
                Mathf.Lerp(0.6f, 1.0f, t), Mathf.Lerp(1.4f, 2.4f, t));

            var emberEmission = _embers.emission;
            emberEmission.rateOverTime = Mathf.Lerp(8f, 24f, t);

            _fireScale = Mathf.Lerp(1f, 2.1f, t);
            warmthRadius = Mathf.Lerp(3.5f, 5.5f, t);
        }

        AudioSource _crackle;

        void SetState(FireState newState)
        {
            state = newState;

            // fire audio follows the state
            if (newState == FireState.Burning)
            {
                if (_crackle == null)
                    _crackle = ProceduralAudio.Loop(transform, ProceduralAudio.FireCrackle(), 0.85f, 1f, 14f);
                _crackle.volume = 0.85f;
            }
            else if (_crackle != null)
            {
                _crackle.volume = newState == FireState.Ember ? 0.25f : 0f;
            }

            if (_flame == null && (newState == FireState.Ember || newState == FireState.Burning))
            {
                var vfxRoot = new GameObject("FireVfx").transform;
                vfxRoot.SetParent(transform, false);
                vfxRoot.localPosition = Vector3.up * 0.12f;
                _flame = FireVfx.Flame(vfxRoot);
                _smoke = FireVfx.Smoke(vfxRoot);
                _embers = FireVfx.Embers(vfxRoot);
            }

            if (_flame != null)
            {
                var flameEmission = _flame.emission;
                var emberEmission = _embers.emission;
                var smokeEmission = _smoke.emission;
                switch (newState)
                {
                    case FireState.Unlit:
                        flameEmission.rateOverTime = 0;
                        emberEmission.rateOverTime = 0;
                        smokeEmission.rateOverTime = 2;
                        _light.intensity = 0;
                        break;
                    case FireState.Ember:
                        flameEmission.rateOverTime = 4;
                        emberEmission.rateOverTime = 6;
                        smokeEmission.rateOverTime = 18;
                        _light.intensity = 0.35f;
                        break;
                    case FireState.Burning:
                        flameEmission.rateOverTime = 130;
                        emberEmission.rateOverTime = 14;
                        smokeEmission.rateOverTime = 12;
                        break;
                }
            }
        }

        void TryHint(string id)
        {
            if (_hintCooldown > 0) return;
            _hintCooldown = 6f;
            ForageEvents.RaiseHint(id);
        }

        void OnTriggerEnter(Collider other)
        {
            var item = other.GetComponentInParent<SurvivalItem>();
            if (item == null) return;
            if (item.kind != ItemKind.Tinder && item.kind != ItemKind.Stick) return;

            // only accept items the player deliberately dropped in (not held)
            var grab = item.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            if (grab != null && grab.isSelected) return;

            AddItem(item);
        }
    }
}
