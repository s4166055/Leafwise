using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Forage
{
    /// <summary>
    /// Traditional campfire cooking gear built over the fire pit:
    ///
    /// * A lashed three-pole **tripod** with a chain and **hook** hanging over
    ///   the flames. Hang the bucket on it by its handle; while the fire is
    ///   burning the water heats up and boils (see <see cref="Bucket"/>).
    /// * A **spit**: two forked uprights either side of the ring. Rest a
    ///   skewer of fish across the forks and it roasts over the flames
    ///   (see <see cref="Skewer"/> and <see cref="Cookable.requiresSkewer"/>).
    ///
    /// Built in pit-local space. Everything is placed so the hanging bucket and
    /// the spit never overlap, and both sit inside the flames' heat.
    /// </summary>
    public class CampfireRig : MonoBehaviour
    {
        static CampfireRig _instance;
        public static CampfireRig Instance
        {
            get
            {
                if (_instance == null) _instance = FindFirstObjectByType<CampfireRig>();
                return _instance;
            }
        }

        // layout (metres, pit-local)
        public static readonly Vector3 Apex = new Vector3(0f, 1.25f, -0.1f);
        public static readonly Vector3 HookPoint = new Vector3(0f, 0.88f, -0.1f);
        public static readonly Vector3 SpitPoint = new Vector3(0f, 0.5f, 0.2f);
        const float TripodRadius = 0.68f;
        const float ForkX = 0.6f;

        public BucketSocket Hook { get; private set; }
        public SkewerSocket Spit { get; private set; }
        public Transform HookAttach { get; private set; }
        public Transform SpitAttach { get; private set; }

        Canvas _signCanvas;

        void Awake() => _instance = this;

        public bool HoldsBucket(Bucket bucket) => Holds(Hook, bucket != null ? bucket.transform : null);
        public bool HoldsSkewer(Skewer skewer) => Holds(Spit, skewer != null ? skewer.transform : null);

        static bool Holds(XRSocketInteractor socket, Transform t)
        {
            if (socket == null || t == null || !socket.hasSelection) return false;
            foreach (var i in socket.interactablesSelected)
                if (i.transform == t) return true;
            return false;
        }

        /// <summary>Build the tripod, hook, spit and sign. Call once after placing at the pit.</summary>
        public void Build()
        {
            var assets = ForageAssets.Instance;
            var wood = assets.stickWood;
            var metal = assets.potMetal;

            // --- tripod: three poles lashed together above the fire
            for (int i = 0; i < 3; i++)
            {
                float a = (90f + i * 120f) * Mathf.Deg2Rad;
                var foot = new Vector3(Mathf.Cos(a) * TripodRadius, -0.05f, Mathf.Sin(a) * TripodRadius);
                Vector3 dir = (Apex - foot).normalized;
                Pole(foot, Apex + dir * 0.14f, 0.024f, wood, "TripodPole" + i);
            }
            var lashing = LowPolyFactory.AddMeshChild(gameObject,
                NatureFactory.SmoothBlob(0.045f, 1, 0.2f, 911, new Vector3(1f, 0.8f, 1f)), assets.tinderStraw, Apex);
            lashing.name = "Lashing";

            // chain and hook
            Pole(Apex, HookPoint + Vector3.up * 0.03f, 0.006f, metal, "Chain");
            var hookVis = LowPolyFactory.AddMeshChild(gameObject, LowPolyFactory.Cone(0.012f, 0.05f, 6, 0.004f), metal,
                HookPoint + new Vector3(0f, 0.03f, 0f));
            hookVis.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
            hookVis.name = "Hook";

            var hookGo = new GameObject("BucketHook");
            hookGo.transform.SetParent(transform, false);
            hookGo.transform.localPosition = HookPoint;
            HookAttach = hookGo.transform;
            var hookTrigger = hookGo.AddComponent<SphereCollider>();
            hookTrigger.isTrigger = true;
            hookTrigger.radius = 0.26f;
            hookTrigger.center = new Vector3(0f, -0.18f, 0f);   // reaches down over the bucket body too
            Hook = hookGo.AddComponent<BucketSocket>();
            Hook.attachTransform = HookAttach;
            Hook.recycleDelayTime = 0.6f;
            Hook.selectEntered.AddListener(_ =>
            {
                ProceduralAudio.PlayAt(HookAttach.position, ProceduralAudio.FlintClick(), 0.35f);
                ForageEvents.RaiseSignal("bucket-hung");
            });

            // --- spit: forked uprights either side, skewer rests across the fire
            foreach (float sx in new[] { -ForkX, ForkX })
            {
                var basePt = new Vector3(sx, -0.05f, SpitPoint.z);
                var top = new Vector3(sx, SpitPoint.y - 0.02f, SpitPoint.z);
                Pole(basePt, top, 0.02f, wood, "SpitUpright");
                Pole(top, top + new Vector3(0f, 0.09f, 0.05f), 0.012f, wood, "SpitFork");
                Pole(top, top + new Vector3(0f, 0.09f, -0.05f), 0.012f, wood, "SpitFork");
            }

            var spitGo = new GameObject("SpitRest");
            spitGo.transform.SetParent(transform, false);
            spitGo.transform.localPosition = SpitPoint;
            SpitAttach = spitGo.transform;
            var spitTrigger = spitGo.AddComponent<SphereCollider>();
            spitTrigger.isTrigger = true;
            spitTrigger.radius = 0.32f;
            Spit = spitGo.AddComponent<SkewerSocket>();
            Spit.attachTransform = SpitAttach;
            Spit.recycleDelayTime = 0.6f;
            Spit.selectEntered.AddListener(_ =>
            {
                ProceduralAudio.PlayAt(SpitAttach.position, ProceduralAudio.FlintClick(), 0.3f);
                ForageEvents.RaiseSignal("skewer-on-spit");
            });

            BuildSign();
        }

        GameObject Pole(Vector3 from, Vector3 to, float radius, Material mat, string name)
        {
            Vector3 d = to - from;
            var go = LowPolyFactory.AddMeshChild(gameObject, LowPolyFactory.Cone(radius, d.magnitude, 6, radius * 0.75f), mat, from);
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
            go.name = name;
            return go;
        }

        void BuildSign()
        {
            var signGo = new GameObject("RigSign", typeof(Canvas));
            signGo.transform.SetParent(transform, false);
            signGo.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            _signCanvas = signGo.GetComponent<Canvas>();
            _signCanvas.renderMode = RenderMode.WorldSpace;
            var rect = signGo.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(620, 120);
            rect.localScale = Vector3.one * 0.0004f;
            FactCard.MakeRect(rect, "Bg", new Color(0.08f, 0.1f, 0.07f, 0.7f));
            var text = FactCard.MakeText(rect, "Text",
                "Hook: hang the bucket to boil water\nForks: rest a skewer of fish to roast it", 30,
                new Vector2(0.04f, 0.06f), new Vector2(0.96f, 0.94f));
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.color = new Color(0.9f, 0.85f, 0.7f);
        }

        void LateUpdate()
        {
            if (_signCanvas == null) return;
            var gm = GameManager.Instance;
            if (gm == null || gm.playerHead == null) return;
            float dist = Vector3.Distance(gm.playerHead.position, transform.position);
            bool bothUsed = Hook != null && Hook.hasSelection && Spit != null && Spit.hasSelection;
            _signCanvas.enabled = dist < 4f && !bothUsed;
            var t = _signCanvas.transform;
            Vector3 to = t.position - gm.playerHead.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.001f) t.rotation = Quaternion.LookRotation(to);
        }
    }
}
