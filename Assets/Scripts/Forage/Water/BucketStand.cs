using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Forage
{
    // The flat hearth-stone "bucket stand" was replaced by the campfire rig
    // (Camp/CampfireRig.cs): the bucket now hangs from a hook on a tripod over
    // the fire, and fish roast on a skewer resting across a forked spit.
    // These socket filters are what the rig uses.

    /// <summary>Socket that only accepts the bucket (the tripod's hook).</summary>
    public class BucketSocket : XRSocketInteractor
    {
        public override bool CanHover(IXRHoverInteractable interactable) =>
            base.CanHover(interactable) && interactable.transform.GetComponent<Bucket>() != null;

        public override bool CanSelect(IXRSelectInteractable interactable) =>
            base.CanSelect(interactable) && interactable.transform.GetComponent<Bucket>() != null;
    }

    /// <summary>Socket that only accepts a skewer (the spit's forked rests).</summary>
    public class SkewerSocket : XRSocketInteractor
    {
        public override bool CanHover(IXRHoverInteractable interactable) =>
            base.CanHover(interactable) && interactable.transform.GetComponent<Skewer>() != null;

        public override bool CanSelect(IXRSelectInteractable interactable) =>
            base.CanSelect(interactable) && interactable.transform.GetComponent<Skewer>() != null;
    }
}
