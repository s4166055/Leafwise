using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Forage
{
    /// <summary>
    /// A roasting skewer.
    ///
    /// Hold it and push the point into a fish (on the ground, in your other
    /// hand, flopping — anywhere) and the fish slides onto the stick. Up to two
    /// fish fit. Then either hold it over the burning fire, or rest it across
    /// the forked spit on the campfire rig and leave it to roast. Eat straight
    /// off the skewer by bringing the fish to your mouth.
    /// </summary>
    public class Skewer : MonoBehaviour
    {
        public const float Length = 1.25f;
        public static readonly List<Skewer> All = new List<Skewer>();

        public Transform tip;
        public int capacity = 2;
        public float spearRadius = 0.07f;

        static readonly float[] SlotX = { 0.10f, -0.14f };   // along the stick, about its middle

        XRGrabInteractable _grab;
        readonly Collider[] _hits = new Collider[16];

        public bool HeldByHand => PlayerHands.IsHandHeld(_grab);
        public bool OnSpit => CampfireRig.Instance != null && CampfireRig.Instance.HoldsSkewer(this);
        public int FishCount => GetComponentsInChildren<FishItem>().Length;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Awake() => _grab = GetComponent<XRGrabInteractable>();

        void Update()
        {
            if (tip == null || !HeldByHand || FishCount >= capacity) return;
            int n = Physics.OverlapSphereNonAlloc(tip.position, spearRadius, _hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var fish = _hits[i].GetComponentInParent<FishItem>();
                if (fish == null || fish.IsSkewered) continue;
                Spear(fish);
                break;
            }
        }

        /// <summary>Slide a fish onto the skewer. Public so tests can drive it directly.</summary>
        public bool Spear(FishItem fish)
        {
            if (fish == null || fish.IsSkewered || FishCount >= capacity) return false;
            int slot = FishCount;

            // take it out of whichever hand is holding it first (XRI restores its
            // physics flags on release, so do this BEFORE making it kinematic)
            var grab = fish.GetComponent<XRGrabInteractable>();
            if (grab != null && grab.isSelected && grab.interactionManager != null)
                grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grab);

            var ai = fish.GetComponent<FishAI>();
            if (ai != null) ai.Impale();

            var rb = fish.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.None;
            }
            foreach (var c in fish.GetComponentsInChildren<Collider>()) c.enabled = false;
            if (grab != null) grab.enabled = false;

            fish.transform.SetParent(transform, false);
            fish.transform.localPosition = new Vector3(SlotX[Mathf.Min(slot, SlotX.Length - 1)], 0f, 0f);
            fish.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);   // nose toward the point
            fish.transform.localScale = Vector3.one;
            fish.MarkSkewered(this);

            ProceduralAudio.PlayAt(fish.transform.position, ProceduralAudio.Crunch(), 0.5f);
            PlayerHands.HoldingHand(_grab, out bool l, out bool r);
            Haptics.Pulse(0.35f, 0.08f, l, r);
            ForageEvents.RaiseSignal("fish-skewered");
            ForageEvents.RaiseHint("fish-skewered");
            return true;
        }
    }

    /// <summary>
    /// Grab interactable for the skewer: hands hold it wherever they grab it,
    /// while the spit's socket seats it by its middle so it lies across the fire.
    /// </summary>
    public class SkewerGrabInteractable : XRGrabInteractable
    {
        public Transform restAttach;

        public override Transform GetAttachTransform(IXRInteractor interactor)
        {
            if (interactor is XRSocketInteractor && restAttach != null) return restAttach;
            return base.GetAttachTransform(interactor);
        }
    }
}
