using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using TMPro;

namespace Forage
{
    /// <summary>
    /// A low-poly wooden bucket that carries water as a percentage (0–100%).
    ///
    /// Fill:   dip the rim under the pond surface and it fills (murky water).
    /// Spill:  the water surface stays level with gravity; tilt the bucket past
    ///         the angle where the water reaches the rim and it pours out —
    ///         the fuller it is, the less tilt that takes. Jerky movement
    ///         (sudden starts/stops) sloshes it the same way, so carry it
    ///         smoothly and don't fill it to the brim.
    /// Boil:   seat it on the bucket stand beside the burning campfire.
    /// Drink:  raise the rim to your mouth, or hold it near your face and
    ///         press the drink button (B / Y). Untreated water hydrates you
    ///         but makes you sick and lose fluid faster.
    /// Pour:   tip it over the camp pot to fill the pot.
    /// </summary>
    public class Bucket : MonoBehaviour
    {
        // interior geometry (local space, metres)
        public const float FloorY = 0.025f;
        public const float RimY = 0.255f;
        public const float InnerBottomR = 0.092f;
        public const float InnerTopR = 0.122f;
        const float InnerH = RimY - FloorY;
        const float MeanR = (InnerBottomR + InnerTopR) * 0.5f;

        public static readonly List<Bucket> All = new List<Bucket>();

        [Header("Water (0..1 of capacity)")]
        [Range(0, 1)] public float fill;
        public bool contaminated;
        public bool boiled;
        [Range(0, 1)] public float boilProgress;

        [Header("Tuning")]
        public float scoopRate = 0.65f;          // fraction per second while the rim is under water
        public float spillStrength = 3.2f;       // flow-rate scale once water crests the rim
        public float maxSloshDegrees = 35f;      // cap on how far acceleration tilts the water surface
        public float sloshResponse = 5f;         // how quickly the water reacts to acceleration
        public float boilSecondsEmpty = 8f;
        public float boilSecondsFull = 26f;
        public float servingFraction = 0.25f;    // one drink = a quarter bucket
        public float hydrationPerServing = 30f;
        public float drinkDistance = 0.32f;
        public float drinkHoldSeconds = 0.9f;

        [Header("Read-only")]
        public float spillRate;                  // fraction per second leaving right now
        public float effectiveTilt;              // degrees between bucket axis and water normal
        public float totalSpilled;

        public float Fill => fill;
        public int Percent => Mathf.RoundToInt(fill * 100f);
        public bool IsEmpty => fill < 0.02f;
        public bool HeldByHand => PlayerHands.IsHandHeld(_grab);
        public bool OnStand => BucketStand.Instance != null && BucketStand.Instance.Holds(this);
        public bool IsBoiling => OnStand && FirePit.Instance != null && FirePit.Instance.IsLit && !IsEmpty && !boiled;
        public Vector3 RimCenter => transform.TransformPoint(0f, RimY, 0f);

        XRGrabInteractable _grab;
        Rigidbody _rb;
        Transform _water;
        Material _waterMat;
        readonly List<Transform> _specks = new List<Transform>();
        Vector2[] _speckOffsets;
        ParticleSystem _spill, _steam;
        AudioSource _pourAudio, _boilAudio;
        Canvas _label;
        TextMeshProUGUI _labelText;

        Vector3 _lastPos, _lastVel, _accel;
        bool _haveLast;
        float _drinkTimer, _hintCooldown, _spillWindow, _spillWindowTimer, _pourIntoPotAccum;
        bool _warnedDirty, _announcedFill;

        static readonly Color MurkyCol = new Color(0.42f, 0.36f, 0.22f, 1f);
        static readonly Color CleanCol = new Color(0.32f, 0.52f, 0.68f, 1f);

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Awake()
        {
            _grab = GetComponent<XRGrabInteractable>();
            _rb = GetComponent<Rigidbody>();
        }

        void Start()
        {
            // adopt wherever the spawner put us, then smooth motion from here on
            if (_rb != null)
            {
                _rb.position = transform.position;
                _rb.rotation = transform.rotation;
                _rb.interpolation = RigidbodyInterpolation.Interpolate;
            }
            BuildWaterVisual();
            BuildEffects();
            BuildLabel();
            RefreshVisuals();
        }

        // ------------------------------------------------------------ build

        void BuildWaterVisual()
        {
            _waterMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _waterMat.SetColor("_BaseColor", MurkyCol);
            _waterMat.SetFloat("_Smoothness", 0.85f);
            var disc = new GameObject("BucketWater");
            disc.transform.SetParent(transform, false);
            disc.AddComponent<MeshFilter>().sharedMesh = LowPolyFactory.Cone(1f, 0.004f, 14);
            var mr = disc.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _waterMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _water = disc.transform;

            // floating dirt specks: the visible sign the water is not safe yet
            var speckMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            speckMat.SetColor("_BaseColor", new Color(0.18f, 0.13f, 0.07f));
            var mesh = NatureFactory.SmoothBlob(0.006f, 0, 0.3f, 31, new Vector3(1.3f, 0.35f, 1f));
            var rand = new System.Random(7);
            _speckOffsets = new Vector2[9];
            for (int i = 0; i < _speckOffsets.Length; i++)
            {
                float a = (float)rand.NextDouble() * Mathf.PI * 2f, r = 0.15f + (float)rand.NextDouble() * 0.6f;
                _speckOffsets[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
                var s = new GameObject("DirtSpeck");
                s.transform.SetParent(transform, false);
                s.AddComponent<MeshFilter>().sharedMesh = mesh;
                var sr = s.AddComponent<MeshRenderer>();
                sr.sharedMaterial = speckMat;
                sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _specks.Add(s.transform);
            }
        }

        void BuildEffects()
        {
            var assets = ForageAssets.Instance;
            var go = new GameObject("SpillStream", typeof(ParticleSystem));
            go.transform.SetParent(transform, false);
            _spill = go.GetComponent<ParticleSystem>();
            _spill.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _spill.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
            main.gravityModifier = 1.3f;
            main.maxParticles = 400;
            var shape = _spill.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.025f;
            var em = _spill.emission;
            em.rateOverTime = 0f;
            var rend = go.GetComponent<ParticleSystemRenderer>();
            if (assets != null) rend.material = assets.smoke;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _spill.Play();

            _pourAudio = ProceduralAudio.Loop(transform, ProceduralAudio.Pour(), 0f, 1f, 8f);
            _boilAudio = ProceduralAudio.Loop(transform, ProceduralAudio.Bubbles(), 0f, 1f, 8f);
        }

        void BuildLabel()
        {
            var go = new GameObject("BucketLabel", typeof(Canvas));
            go.transform.SetParent(transform, false);
            _label = go.GetComponent<Canvas>();
            _label.renderMode = RenderMode.WorldSpace;
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(520, 130);
            rect.localScale = Vector3.one * 0.00034f;
            FactCard.MakeRect(rect, "Bg", new Color(0.07f, 0.09f, 0.07f, 0.82f));
            _labelText = FactCard.MakeText(rect, "Text", "", 40, new Vector2(0.04f, 0.06f), new Vector2(0.96f, 0.94f));
            _labelText.alignment = TextAlignmentOptions.Center;
            _labelText.fontStyle = FontStyles.Bold;
            _label.enabled = false;
        }

        // ------------------------------------------------------------ update

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _hintCooldown -= dt;

            TrackAcceleration(dt);
            Scoop(dt);
            Spill(dt);
            Boil(dt);
            MouthDrink(dt);

            if (_rb != null && !_rb.isKinematic) _rb.mass = 1.2f + 3.5f * fill;
            RefreshVisuals();
        }

        void TrackAcceleration(float dt)
        {
            Vector3 p = transform.position;
            if (!_haveLast) { _lastPos = p; _lastVel = Vector3.zero; _haveLast = true; return; }
            Vector3 v = (p - _lastPos) / dt;
            _lastPos = p;
            if (v.magnitude > 40f) { _lastVel = Vector3.zero; return; } // teleport, not a slosh
            Vector3 a = (v - _lastVel) / dt;
            _lastVel = v;
            a.y = 0f; // vertical bobbing barely moves the surface; horizontal jerks slosh it
            // the water has inertia: it responds to sustained acceleration, not single-frame noise
            _accel = Vector3.Lerp(_accel, a, 1f - Mathf.Exp(-sloshResponse * dt));
        }

        /// <summary>Water normal in the bucket's frame: gravity minus the bucket's horizontal acceleration.</summary>
        Vector3 EffectiveDown()
        {
            float maxA = 9.81f * Mathf.Tan(maxSloshDegrees * Mathf.Deg2Rad);
            Vector3 a = Vector3.ClampMagnitude(_accel, maxA);
            return (Physics.gravity - a).normalized;
        }

        void Scoop(float dt)
        {
            var water = WaterBody.Instance;
            bool underwater = water != null
                ? water.IsUnderwater(RimCenter, 0.01f)
                : ForestGenerator.Instance != null && ForestGenerator.Instance.IsInPond(RimCenter) &&
                  RimCenter.y < ForestGenerator.Instance.WaterLevel + 0.01f;
            if (!underwater || fill >= 1f) return;
            if (Vector3.Dot(transform.up, Vector3.up) < -0.3f) return; // upside down traps air, no water in

            float before = fill;
            fill = Mathf.Min(1f, fill + scoopRate * dt);
            if (fill - before > 0f)
            {
                // any pond water makes the whole bucket untreated again
                contaminated = true;
                boiled = false;
                boilProgress = 0f;
                if (water != null && Random.value < dt * 8f) water.Ripple(RimCenter, 0.015f);
            }
            if (!_announcedFill && fill > 0.5f)
            {
                _announcedFill = true;
                ForageEvents.RaiseSignal("bucket-filled");
                ForageEvents.RaiseHint("bucket-filled");
                ProceduralAudio.PlayAt(RimCenter, ProceduralAudio.Splash(), 0.5f);
            }
        }

        void Spill(float dt)
        {
            spillRate = 0f;
            Vector3 down = EffectiveDown();
            effectiveTilt = Vector3.Angle(transform.up, -down);

            bool rimUnderwater = WaterBody.Instance != null && WaterBody.Instance.IsUnderwater(RimCenter, 0.01f);
            if (!IsEmpty && !rimUnderwater)
            {
                // height the level surface reaches on the low side of the rim
                float tilt = Mathf.Min(effectiveTilt, 89f) * Mathf.Deg2Rad;
                float level = fill * InnerH + MeanR * Mathf.Tan(tilt);
                float excess = (level - InnerH) / InnerH;
                if (excess > 0f) spillRate = spillStrength * Mathf.Pow(excess, 1.5f);
                if (effectiveTilt > 95f) spillRate = Mathf.Max(spillRate, 1.4f); // tipped over: it all comes out
                spillRate = Mathf.Min(spillRate, 3f);
            }

            float lost = Mathf.Min(fill, spillRate * dt);
            if (lost > 0f)
            {
                fill -= lost;
                totalSpilled += lost;
                _spillWindow += lost;
                PourInto(lost, down);
            }

            // stream particles leave from the lowest point of the rim
            Vector3 lowDir = Vector3.ProjectOnPlane(down, transform.up);
            if (lowDir.sqrMagnitude < 1e-4f) lowDir = transform.forward;
            lowDir.Normalize();
            Vector3 lip = RimCenter + lowDir * InnerTopR;
            _spill.transform.position = lip;
            _spill.transform.rotation = Quaternion.LookRotation((lowDir * 0.7f + Vector3.down * 0.3f).normalized);
            var em = _spill.emission;
            em.rateOverTime = Mathf.Min(spillRate * 700f, 160f);
            var main = _spill.main;
            main.startColor = (contaminated ? MurkyCol : CleanCol) * new Color(1, 1, 1, 0.8f);
            if (_pourAudio != null)
                _pourAudio.volume = Mathf.MoveTowards(_pourAudio.volume, Mathf.Clamp01(spillRate * 1.6f) * 0.8f, dt * 4f);

            if (spillRate > 0f && WaterBody.Instance != null)
            {
                Vector3 below = new Vector3(lip.x, WaterBody.Instance.surfaceY, lip.z);
                if (WaterBody.Instance.InsideFootprint(below) && Random.value < dt * 10f)
                    WaterBody.Instance.Ripple(below, 0.01f);
            }

            // Scout notices if you're losing a lot on the walk back
            _spillWindowTimer += dt;
            if (_spillWindowTimer > 2f)
            {
                if (_spillWindow > 0.08f && HeldByHand && _hintCooldown <= 0f)
                {
                    _hintCooldown = 25f;
                    ForageEvents.RaiseHint("bucket-spilling");
                }
                _spillWindow = 0f;
                _spillWindowTimer = 0f;
            }

            // Only snap a near-empty bucket to zero while it is actually pouring out.
            // (Doing it unconditionally zeroed the first few frames of scooping at
            // headset frame rates, where each frame adds less than 2%.)
            if (lost > 0f && fill < 0.02f) fill = 0f;
            if (fill <= 0f) { boiled = false; boilProgress = 0f; contaminated = false; _announcedFill = false; }
        }

        /// <summary>Spilled water that lands in the camp pot fills the pot.</summary>
        void PourInto(float amount, Vector3 down)
        {
            Vector3 lowDir = Vector3.ProjectOnPlane(down, transform.up).normalized;
            Vector3 lip = RimCenter + lowDir * InnerTopR;
            foreach (var pot in FindObjectsByType<CookingPot>(FindObjectsSortMode.None))
            {
                Vector3 pp = pot.transform.position;
                float horiz = Vector2.Distance(new Vector2(lip.x, lip.z), new Vector2(pp.x, pp.z));
                if (horiz < 0.2f && lip.y > pp.y && lip.y - pp.y < 0.9f)
                {
                    pot.ReceiveWater(amount, clean: !contaminated && boiled);
                    break;
                }
            }
        }

        void Boil(float dt)
        {
            bool onStand = OnStand;
            var fire = FirePit.Instance;
            bool fireLit = fire != null && fire.IsLit;

            if (onStand && !IsEmpty && !boiled)
            {
                if (fireLit)
                {
                    float seconds = Mathf.Lerp(boilSecondsEmpty, boilSecondsFull, fill);
                    boilProgress = Mathf.Min(1f, boilProgress + dt / seconds);
                    if (boilProgress >= 1f)
                    {
                        boiled = true;
                        contaminated = false;
                        ForageEvents.RaiseSignal("bucket-boiled");
                        ProceduralAudio.PlayAt(transform.position, ProceduralAudio.Chime(), 0.5f);
                        FactCard.Show("Water boiled — safe to drink",
                            "A rolling boil kills the bacteria, viruses and parasites in pond water. " +
                            "The dirt settles to the bottom; the water above it is now safe.", good: true);
                    }
                }
                else if (_hintCooldown <= 0f)
                {
                    _hintCooldown = 20f;
                    ForageEvents.RaiseHint("bucket-needs-fire");
                }
            }
            else if (!onStand && boilProgress > 0f && !boiled)
            {
                // taken off the heat part-way: it cools and is NOT safe yet
                if (boilProgress > 0.15f && _hintCooldown <= 0f)
                {
                    _hintCooldown = 10f;
                    ForageEvents.RaiseHint("boil-interrupted");
                }
                boilProgress = Mathf.Max(0f, boilProgress - dt / 6f);
            }

            bool steaming = onStand && fireLit && !IsEmpty && boilProgress > 0.25f;
            if (steaming && _steam == null)
            {
                _steam = FireVfx.Smoke(transform, 0f);
                _steam.transform.localPosition = new Vector3(0f, RimY, 0f);
            }
            if (_steam != null)
            {
                var em = _steam.emission;
                em.rateOverTime = steaming ? Mathf.Lerp(3f, 14f, boilProgress) : 0f;
            }
            if (_boilAudio != null)
                _boilAudio.volume = Mathf.MoveTowards(_boilAudio.volume,
                    steaming && !boiled ? 0.45f * boilProgress : (steaming ? 0.12f : 0f), dt);
        }

        // ------------------------------------------------------------ drinking

        void MouthDrink(float dt)
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.playerHead == null || IsEmpty || !HeldByHand)
            {
                _drinkTimer = 0f;
                return;
            }
            bool atMouth = Vector3.Distance(RimCenter, gm.playerHead.position) < drinkDistance;
            if (!atMouth) { _drinkTimer = 0f; _warnedDirty = false; return; }

            if (contaminated && !_warnedDirty)
            {
                _warnedDirty = true;
                ForageEvents.RaiseHint("about-to-drink-dirty");
            }
            _drinkTimer += dt;
            if (_drinkTimer >= drinkHoldSeconds)
            {
                _drinkTimer = 0f;
                DrinkServing();
            }
        }

        /// <summary>Drink-button path: works when held close to the face.</summary>
        public bool TryDrinkNow(float maxDistance = 0.55f)
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.playerHead == null || IsEmpty || !HeldByHand) return false;
            if (Vector3.Distance(RimCenter, gm.playerHead.position) > maxDistance) return false;
            DrinkServing();
            return true;
        }

        /// <summary>Drink one serving (a quarter bucket, or whatever is left).</summary>
        public void DrinkServing()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.vitals == null || IsEmpty) return;

            float amount = Mathf.Min(servingFraction, fill);
            float hydration = hydrationPerServing * amount / servingFraction;
            bool dirty = contaminated || !boiled;
            fill -= amount;

            gm.vitals.Drink(hydration, dirty);
            ProceduralAudio.PlayAt(transform.position, ProceduralAudio.Gulp(), 0.9f);
            PlayerHands.HoldingHand(_grab, out bool l, out bool r);
            Haptics.Pulse(0.25f, 0.12f, l, r);

            if (dirty)
            {
                ScreenFeedback.DrinkUntreated();
                ForageEvents.RaiseSignal("drank-dirty-water");
                FactCard.Show("Untreated water…",
                    "Pond water carries bacteria and parasites. It wets your mouth, but the stomach " +
                    "upset makes you lose fluid FASTER. Boil it on the stand by the fire first.", good: false);
            }
            else
            {
                ScreenFeedback.Drink();
                gm.CompleteObjective("water");
                ForageEvents.RaiseSignal("drank-clean-water");
            }
            if (IsEmpty) { fill = 0f; boiled = false; boilProgress = 0f; contaminated = false; _announcedFill = false; }
        }

        // ------------------------------------------------------------ visuals

        void RefreshVisuals()
        {
            if (_water == null) return;
            bool show = !IsEmpty;
            _water.gameObject.SetActive(show);

            Color col = boiled ? CleanCol : Color.Lerp(MurkyCol, CleanCol, boilProgress * 0.5f);
            _waterMat.SetColor("_BaseColor", col);

            if (show)
            {
                // the surface stays level with (effective) gravity, pivoting about the axis point at fill height
                Vector3 down = EffectiveDown();
                float h = FloorY + fill * InnerH;
                Vector3 axisPoint = transform.TransformPoint(0f, h, 0f);
                float r = Mathf.Lerp(InnerBottomR, InnerTopR, fill) * 0.97f;
                float tilt = Vector3.Angle(transform.up, -down);
                float stretch = 1f / Mathf.Max(0.35f, Mathf.Cos(Mathf.Min(tilt, 70f) * Mathf.Deg2Rad));
                Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, -down);
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(transform.right, -down);
                _water.position = axisPoint;
                _water.rotation = Quaternion.LookRotation(fwd.normalized, -down);
                // stretch along the tilt direction so the oval meets the walls
                Vector3 lowLocal = Quaternion.Inverse(_water.rotation) * Vector3.ProjectOnPlane(transform.up, -down);
                float sx = Mathf.Abs(lowLocal.x) > Mathf.Abs(lowLocal.z) ? stretch : 1f;
                float sz = sx > 1f ? 1f : stretch;
                // world scale (bucket is unscaled), boiling makes the surface shimmer
                float shimmer = IsBoiling ? 1f + Mathf.Sin(Time.time * 30f) * 0.01f : 1f;
                _water.localScale = new Vector3(r * sx * shimmer, 1f, r * sz * shimmer);
            }

            // dirt specks drift on murky water and settle out once boiled
            bool dirtVisible = show && !boiled;
            for (int i = 0; i < _specks.Count; i++)
            {
                var s = _specks[i];
                s.gameObject.SetActive(dirtVisible);
                if (!dirtVisible) continue;
                float ang = Time.time * (0.15f + i * 0.03f) + i;
                Vector2 o = _speckOffsets[i];
                Vector2 rot = new Vector2(o.x * Mathf.Cos(ang) - o.y * Mathf.Sin(ang), o.x * Mathf.Sin(ang) + o.y * Mathf.Cos(ang));
                float r = Mathf.Lerp(InnerBottomR, InnerTopR, fill) * 0.85f;
                s.position = _water.position + _water.rotation * new Vector3(rot.x * r, 0.003f, rot.y * r);
                s.rotation = _water.rotation;
            }

            UpdateLabel();
        }

        void UpdateLabel()
        {
            if (_label == null) return;
            var gm = GameManager.Instance;
            bool near = gm != null && gm.playerHead != null &&
                        Vector3.Distance(gm.playerHead.position, transform.position) < 1.6f;
            bool show = HeldByHand || (near && (OnStand || !IsEmpty));
            _label.enabled = show;
            if (!show) return;

            _label.transform.position = transform.position + Vector3.up * 0.5f;
            Vector3 to = _label.transform.position - gm.playerHead.position;
            if (to.sqrMagnitude > 0.001f) _label.transform.rotation = Quaternion.LookRotation(to);

            string status;
            Color c;
            if (IsEmpty) { status = "empty — scoop from the pond"; c = new Color(0.85f, 0.85f, 0.8f); }
            else if (boiled) { status = "boiled — safe to drink"; c = new Color(0.55f, 0.85f, 1f); }
            else if (IsBoiling) { status = $"boiling {Mathf.RoundToInt(boilProgress * 100f)}%"; c = new Color(1f, 0.8f, 0.45f); }
            else { status = "murky — boil before drinking"; c = new Color(0.95f, 0.65f, 0.4f); }
            string spill = spillRate > 0.02f ? "  <color=#ff8a6a>spilling!</color>" : "";
            _labelText.text = $"Water {Percent}%{spill}\n<size=30>{status}</size>";
            _labelText.color = c;
        }

        /// <summary>Test/debug helper: set contents directly.</summary>
        public void SetContents(float newFill, bool isBoiled)
        {
            fill = Mathf.Clamp01(newFill);
            boiled = isBoiled && fill > 0f;
            contaminated = !boiled && fill > 0f;
            boilProgress = boiled ? 1f : 0f;
        }
    }

    /// <summary>
    /// Grab interactable for the bucket: hands hold it by the handle (so it
    /// hangs naturally and tilts with your wrist), while the stand's socket
    /// seats it by its base.
    /// </summary>
    public class BucketGrabInteractable : XRGrabInteractable
    {
        public Transform handleAttach;
        public Transform baseAttach;

        public override Transform GetAttachTransform(UnityEngine.XR.Interaction.Toolkit.Interactors.IXRInteractor interactor)
        {
            if (interactor is UnityEngine.XR.Interaction.Toolkit.Interactors.XRSocketInteractor && baseAttach != null)
                return baseAttach;
            if (handleAttach != null) return handleAttach;
            return base.GetAttachTransform(interactor);
        }
    }
}
