using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Forage
{
    /// <summary>
    /// The camp pot. Dip it into the pond to scoop murky water, hold it over
    /// the lit campfire until it boils, then raise it to your mouth to drink.
    /// Drinking unboiled water causes sickness (and a Scout lesson).
    /// </summary>
    public class CookingPot : MonoBehaviour
    {
        public enum PotState { Empty, DirtyWater, Boiling, CleanWater }

        [Header("Tuning")]
        public float boilSeconds = 18f;
        public float drinkDistance = 0.30f;   // pot-to-head distance that counts as drinking
        public float drinkHoldSeconds = 0.9f;
        public float fireRadius = 1.3f;       // how close to the fire pit to boil
        public float scoopMaxY = 0.0f;        // pot must reach below this height to scoop

        [Header("State (read-only)")]
        public PotState state = PotState.Empty;
        public float boilProgress;

        static readonly Color DirtyCol = new Color(0.45f, 0.4f, 0.25f);
        static readonly Color CleanCol = new Color(0.35f, 0.55f, 0.7f);

        XRGrabInteractable _grab;
        MeshRenderer _waterSurface;
        Material _waterMat;
        ParticleSystem _steam;
        float _drinkTimer;
        float _hintCooldown;

        void Awake()
        {
            _grab = GetComponent<XRGrabInteractable>();
        }

        void Start()
        {
            // water surface disc inside the pot rim
            _waterMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var surf = LowPolyFactory.AddMeshChild(gameObject,
                LowPolyFactory.Cone(0.075f, 0.004f, 10), _waterMat, new Vector3(0, 0.09f, 0));
            _waterSurface = surf.GetComponent<MeshRenderer>();
            _waterSurface.enabled = false;
        }

        void Update()
        {
            _hintCooldown -= Time.deltaTime;

            switch (state)
            {
                case PotState.DirtyWater:
                    TryBoil();
                    TryDrink(contaminated: true);
                    break;
                case PotState.Boiling:
                    Boil();
                    break;
                case PotState.CleanWater:
                    TryDrink(contaminated: false);
                    break;
            }
        }

        void TryBoil()
        {
            var fire = FirePit.Instance;
            if (fire == null || !fire.IsLit) return;
            if (Vector3.Distance(transform.position, fire.transform.position) > fireRadius) return;
            SetState(PotState.Boiling);
        }

        void Boil()
        {
            var fire = FirePit.Instance;
            bool overFire = fire != null && fire.IsLit &&
                Vector3.Distance(transform.position, fire.transform.position) <= fireRadius;

            if (!overFire)
            {
                // taken off the fire before finishing — back to dirty
                SetState(PotState.DirtyWater);
                if (_hintCooldown <= 0f)
                {
                    _hintCooldown = 8f;
                    ForageEvents.RaiseHint("boil-interrupted");
                }
                return;
            }

            boilProgress += Time.deltaTime / boilSeconds;
            if (boilProgress >= 1f)
            {
                SetState(PotState.CleanWater);
                ForageEvents.RaiseSignal("water-boiled");
            }
        }

        void TryDrink(bool contaminated)
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.playerHead == null) return;
            bool held = _grab != null && _grab.isSelected;
            bool atMouth = Vector3.Distance(transform.position, gm.playerHead.position) < drinkDistance;

            if (held && atMouth)
            {
                if (contaminated && _drinkTimer == 0f && _hintCooldown <= 0f)
                {
                    // one warning before they commit
                    _hintCooldown = 10f;
                    ForageEvents.RaiseHint("about-to-drink-dirty");
                }
                _drinkTimer += Time.deltaTime;

                // drinking animation: water level sinks and drops trickle toward you
                float sip = Mathf.Clamp01(_drinkTimer / drinkHoldSeconds);
                if (_waterSurface != null)
                {
                    _waterSurface.transform.localPosition = new Vector3(0, Mathf.Lerp(0.09f, 0.02f, sip), 0);
                    _waterSurface.transform.localScale = Vector3.one * Mathf.Lerp(1f, 0.55f, sip);
                }
                if (_drinkTimer >= drinkHoldSeconds)
                {
                    gm.vitals.Drink(45f, contaminated);
                    ProceduralAudio.PlayAt(transform.position, ProceduralAudio.Gulp(), 0.9f);
                    ScreenFeedback.Drink();
                    Haptics.Pulse(0.25f, 0.12f);
                    if (!contaminated)
                    {
                        gm.CompleteObjective("water");
                        ForageEvents.RaiseSignal("drank-clean-water");
                    }
                    SetState(PotState.Empty);
                }
            }
            else
            {
                if (_drinkTimer > 0f && _waterSurface != null)
                {
                    _waterSurface.transform.localPosition = new Vector3(0, 0.09f, 0);
                    _waterSurface.transform.localScale = Vector3.one;
                }
                _drinkTimer = 0f;
            }
        }

        public void SetState(PotState newState)
        {
            state = newState;
            _drinkTimer = 0f;
            if (_waterSurface != null)
            {
                _waterSurface.transform.localPosition = new Vector3(0, 0.09f, 0);
                _waterSurface.transform.localScale = Vector3.one;
            }
            if (newState != PotState.Boiling && newState != PotState.CleanWater) boilProgress = 0f;

            if (_waterSurface != null)
            {
                _waterSurface.enabled = newState != PotState.Empty;
                if (newState == PotState.DirtyWater || newState == PotState.Boiling)
                    _waterMat.SetColor("_BaseColor", DirtyCol);
                else if (newState == PotState.CleanWater)
                    _waterMat.SetColor("_BaseColor", CleanCol);
            }

            if (newState == PotState.Boiling)
            {
                if (_steam == null)
                    _steam = FireVfx.Smoke(transform, 0f);
                var em = _steam.emission;
                em.rateOverTime = 14f;
            }
            else if (_steam != null)
            {
                var em = _steam.emission;
                em.rateOverTime = 0f;
            }
        }

        void OnTriggerStay(Collider other)
        {
            if (state != PotState.Empty) return;
            if (!other.CompareTag("Water")) return;
            if (transform.position.y > scoopMaxY) return; // must actually dip it down to the water
            SetState(PotState.DirtyWater);
            ForageEvents.RaiseSignal("water-scooped");
        }
    }
}
