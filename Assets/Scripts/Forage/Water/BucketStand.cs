using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Forage
{
    /// <summary>Socket that only accepts the bucket.</summary>
    public class BucketSocket : XRSocketInteractor
    {
        public override bool CanHover(UnityEngine.XR.Interaction.Toolkit.Interactables.IXRHoverInteractable interactable) =>
            base.CanHover(interactable) && interactable.transform.GetComponent<Bucket>() != null;

        public override bool CanSelect(IXRSelectInteractable interactable) =>
            base.CanSelect(interactable) && interactable.transform.GetComponent<Bucket>() != null;
    }

    /// <summary>
    /// The place by the campfire for the bucket: a flat hearth stone right
    /// at the edge of the stone ring, with a socket that snaps the bucket
    /// upright onto it. While the fire is burning, a seated bucket heats and
    /// boils (see <see cref="Bucket"/>).
    /// </summary>
    public class BucketStand : MonoBehaviour
    {
        static BucketStand _instance;
        public static BucketStand Instance
        {
            get
            {
                if (_instance == null) _instance = FindFirstObjectByType<BucketStand>();
                return _instance;
            }
        }

        public BucketSocket Socket { get; private set; }
        public Transform Seat { get; private set; }
        TMPro.TextMeshProUGUI _sign;
        Canvas _signCanvas;

        void Awake() => _instance = this;

        public bool Holds(Bucket bucket)
        {
            if (Socket == null || !Socket.hasSelection || bucket == null) return false;
            foreach (var i in Socket.interactablesSelected)
                if (i.transform == bucket.transform) return true;
            return false;
        }

        /// <summary>Builds the hearth stone, socket and a small sign. Call once after placing.</summary>
        public void Build()
        {
            var assets = ForageAssets.Instance;

            // flat hearth slab
            var slab = new GameObject("HearthStone");
            slab.transform.SetParent(transform, false);
            slab.AddComponent<MeshFilter>().sharedMesh =
                NatureFactory.SmoothBlob(0.2f, 1, 0.06f, 4711, new Vector3(1.15f, 0.32f, 1f));
            slab.AddComponent<MeshRenderer>().sharedMaterial = assets.stone;
            slab.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            var slabCol = slab.AddComponent<BoxCollider>();
            slabCol.size = new Vector3(0.42f, 0.1f, 0.38f);

            // two small wedge stones so it reads as a built spot, not a random rock
            for (int i = 0; i < 2; i++)
            {
                var w = NatureFactory.Rock(4800 + i, assets.stone, 0.07f);
                w.transform.SetParent(transform, false);
                w.transform.localPosition = new Vector3(i == 0 ? -0.24f : 0.24f, 0.03f, -0.05f);
                var c = w.GetComponent<Collider>();
                if (c != null) Destroy(c);
            }

            // where the bucket sits (bucket pivot is its base)
            Seat = new GameObject("BucketSeat").transform;
            Seat.SetParent(transform, false);
            Seat.localPosition = new Vector3(0f, 0.105f, 0f);

            // socket: trigger volume above the slab, snapping the bucket upright
            var sockGo = new GameObject("BucketSocket");
            sockGo.transform.SetParent(transform, false);
            sockGo.transform.localPosition = new Vector3(0f, 0.22f, 0f);
            var trigger = sockGo.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.24f;
            Socket = sockGo.AddComponent<BucketSocket>();
            Socket.attachTransform = Seat;
            Socket.recycleDelayTime = 0.6f;
            Socket.selectEntered.AddListener(_ =>
            {
                ProceduralAudio.PlayAt(transform.position, ProceduralAudio.FlintClick(), 0.35f);
                ForageEvents.RaiseSignal("bucket-on-stand");
            });

            // small sign so players know what the spot is for
            var signGo = new GameObject("StandSign", typeof(Canvas));
            signGo.transform.SetParent(transform, false);
            signGo.transform.localPosition = new Vector3(0f, 0.62f, 0f);
            _signCanvas = signGo.GetComponent<Canvas>();
            _signCanvas.renderMode = RenderMode.WorldSpace;
            var rect = signGo.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(480, 90);
            rect.localScale = Vector3.one * 0.00045f;
            FactCard.MakeRect(rect, "Bg", new Color(0.08f, 0.1f, 0.07f, 0.7f));
            _sign = FactCard.MakeText(rect, "Text", "Bucket stand — boils water while the fire burns", 30,
                new Vector2(0.04f, 0.08f), new Vector2(0.96f, 0.92f));
            _sign.alignment = TMPro.TextAlignmentOptions.Center;
            _sign.color = new Color(0.9f, 0.85f, 0.7f);
        }

        void LateUpdate()
        {
            // keep our own Canvas reference: Graphic.canvas returns null once the canvas is disabled
            if (_signCanvas == null) return;
            var gm = GameManager.Instance;
            if (gm == null || gm.playerHead == null) return;
            var t = _signCanvas.transform;
            float dist = Vector3.Distance(gm.playerHead.position, transform.position);
            _signCanvas.enabled = dist < 4f && (Socket == null || !Socket.hasSelection);
            Vector3 to = t.position - gm.playerHead.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.001f) t.rotation = Quaternion.LookRotation(to);
        }
    }
}
