using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

namespace Forage
{
    /// <summary>
    /// Reports, in the device log, whether the headset is actually running
    /// room-scale. It reads; it never writes.
    ///
    /// XROrigin already owns the height logic: it requests Floor, retries while
    /// the runtime reports Unknown, applies <c>CameraYOffset</c> only in
    /// Device/Unbounded mode, zeroes the floor offset in Floor mode, and
    /// re-applies all of that whenever the runtime changes origin mid-session
    /// (boundary lost, stationary mode toggled). An earlier version of this
    /// component duplicated that with its own writes - zeroing CameraYOffset
    /// when Floor was granted, which threw away XROrigin's fallback for a later
    /// drop to Device, and deciding from a one-frame poll, which misclassified
    /// a healthy headset while the reference-space change was still pending.
    ///
    /// So the configuration lives on the XROrigin (RequestedTrackingOriginMode
    /// = Floor, CameraYOffset = 1.7 m as the seated fallback) and this class
    /// only tells you what happened, so `adb logcat -s Unity:V` answers "did
    /// room-scale come up?" without guessing from feel.
    /// </summary>
    [DisallowMultipleComponent]
    public class VrTrackingSetup : MonoBehaviour
    {
        /// <summary>How long to wait for the runtime to settle before reporting.</summary>
        public float reportAfterSeconds = 3f;

        XROrigin _origin;
        static readonly List<XRInputSubsystem> _subsystems = new List<XRInputSubsystem>();

        void Awake() => _origin = GetComponentInChildren<XROrigin>();

        void Start()
        {
            if (_origin == null)
            {
                Debug.LogWarning("[Forage] VR: no XROrigin under '" + gameObject.name + "'.");
                return;
            }
            Debug.Log($"[Forage] VR: rig requests {_origin.RequestedTrackingOriginMode}, " +
                      $"seated fallback offset {_origin.CameraYOffset:F2} m.");
            StartCoroutine(Report());
        }

        IEnumerator Report()
        {
            // Bound by wall-clock, not frames: an unfocused editor throttles the
            // player loop, and a frame-counted wait silently outlives everything.
            float deadline = Time.realtimeSinceStartup + reportAfterSeconds;
            XRInputSubsystem subsystem = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                SubsystemManager.GetSubsystems(_subsystems);
                if (_subsystems.Count > 0) subsystem = _subsystems[0];

                // XROrigin exposes the mode it has actually confirmed. Unknown /
                // NotSpecified means the session is still coming up - keep waiting.
                var mode = _origin.CurrentTrackingOriginMode;
                if (subsystem != null && mode != TrackingOriginModeFlags.Unknown) break;
                yield return null;
            }

            if (subsystem == null)
            {
                // Editor without a headset, or XR failed to initialise. XROrigin
                // never moves the offset in that case, so the serialised 1.7 m on
                // the Camera Offset object is what the player stands on.
                Debug.Log("[Forage] VR: no XR input subsystem - running flat. Eye height comes from " +
                          $"the rig's serialised offset ({_origin.CameraYOffset:F2} m).");
                yield break;
            }

            TrackingOriginModeFlags supported;
            try { supported = subsystem.GetSupportedTrackingOriginModes(); }
            catch (System.Exception e) { supported = TrackingOriginModeFlags.Unknown; Debug.LogWarning("[Forage] VR: " + e.Message); }

            var actual = _origin.CurrentTrackingOriginMode;
            Debug.Log($"[Forage] VR: supported tracking origin modes = {supported}; in use = {actual}");

            if (actual == TrackingOriginModeFlags.Floor)
                Debug.Log("[Forage] VR: ROOM-SCALE ACTIVE - physically walking, leaning and crouching move " +
                          "the view; head height comes from the headset.");
            else if (actual == TrackingOriginModeFlags.Unknown)
                Debug.LogWarning($"[Forage] VR: origin mode still Unknown after {reportAfterSeconds:F0} s - " +
                                 "the OpenXR session may not have synchronised. XROrigin keeps retrying.");
            else
                Debug.LogWarning($"[Forage] VR: headset is in {actual} (seated) mode, not Floor. XROrigin has " +
                                 $"applied the {_origin.CameraYOffset:F2} m fallback. For true room-scale, set up " +
                                 "the room boundary / switch off stationary mode on the headset.");

            yield return SampleHead();
        }

        /// <summary>
        /// Logs whether the head pose is genuinely moving, so a "head tracking
        /// does nothing" report can be confirmed or ruled out from the log alone.
        /// </summary>
        IEnumerator SampleHead()
        {
            var head = _origin.Camera != null ? _origin.Camera.transform : null;
            if (head == null) yield break;

            var startPos = head.localPosition;
            var startRot = head.localRotation;
            yield return new WaitForSeconds(2f);

            float moved = Vector3.Distance(head.localPosition, startPos);
            float turned = Quaternion.Angle(startRot, head.localRotation);

            var device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            bool tracked = device.isValid &&
                           device.TryGetFeatureValue(CommonUsages.isTracked, out bool t) && t;

            Debug.Log($"[Forage] VR head check: device valid={device.isValid} tracked={tracked} " +
                      $"name='{device.name}' | moved {moved:F3} m, turned {turned:F1} deg over 2 s | " +
                      $"local head pos {head.localPosition}");

            if (device.isValid && !tracked)
                Debug.LogError("[Forage] VR: the head device reports NOT TRACKED - the view will not follow " +
                               "your head. Check the headset is on your face and the guardian is set up.");
            else if (tracked && moved < 0.001f && turned < 0.5f)
                Debug.LogWarning("[Forage] VR: head pose did not change in 2 s. If you were moving, " +
                                 "something is overriding the camera transform.");
        }
    }
}
