using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

namespace Forage
{
    /// <summary>
    /// Makes sure the headset is actually running room-scale, and says so out
    /// loud in the device log.
    ///
    /// Requesting Floor (stage space) is not a guarantee: a headset with no
    /// room boundary set up, or one in a seated/stationary profile, can hand
    /// back Device (local space) instead. In that case the camera reports a
    /// height near zero and the player ends up with their eyes on the ground,
    /// so a fallback offset is needed — but only then, because applying it when
    /// Floor *was* granted would double-count the real head height.
    ///
    /// Every branch logs with the [Forage] prefix, so `adb logcat -s Unity:V`
    /// tells you exactly what the device did rather than leaving it to feel.
    /// </summary>
    [DisallowMultipleComponent]
    public class VrTrackingSetup : MonoBehaviour
    {
        /// <summary>Eye height applied only if the runtime refuses Floor mode.</summary>
        public float seatedFallbackEyeHeight = 1.7f;

        XROrigin _origin;
        static readonly List<XRInputSubsystem> _subsystems = new List<XRInputSubsystem>();

        void Awake() => _origin = GetComponentInChildren<XROrigin>();

        void Start()
        {
            Debug.Log($"[Forage] VR: tracking setup starting on '{gameObject.name}' " +
                      $"(origin={(_origin == null ? "null" : _origin.gameObject.name)}).");
            StartCoroutine(ConfigureWhenReady());
        }

        IEnumerator ConfigureWhenReady()
        {
            if (_origin == null)
            {
                Debug.LogWarning("[Forage] VrTrackingSetup: no XROrigin found.");
                yield break;
            }

            // The input subsystem is not necessarily up on the first frame.
            // Bound this by wall-clock time, not frame count: the editor
            // throttles its player loop to a few fps when the window is not
            // focused, so a frame-counted wait silently outlives the caller.
            float deadline = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < deadline)
            {
                SubsystemManager.GetSubsystems(_subsystems);
                if (_subsystems.Count > 0) break;
                yield return null;
            }

            if (_subsystems.Count == 0)
            {
                // No XR runtime at all: editor play without a headset, or XR
                // failed to initialise. Give the flat view a sane eye height.
                Debug.Log("[Forage] VR: no XR input subsystem (running flat). " +
                          "Applying " + seatedFallbackEyeHeight + " m eye height.");
                ApplyFallbackHeight();
                yield break;
            }

            var subsystem = _subsystems[0];

            // These are native calls into the XR runtime. A runtime that came up
            // half-initialised can throw here, and an exception inside a
            // coroutine aborts it silently — which would leave the player at the
            // wrong height with nothing in the log to explain why.
            TrackingOriginModeFlags supported;
            try
            {
                supported = subsystem.GetSupportedTrackingOriginModes();
                Debug.Log($"[Forage] VR: supported tracking origin modes = {supported}");

                // Ask for Floor explicitly; XROrigin also requests it, but doing
                // it here means we can see the result rather than assume it.
                if ((supported & TrackingOriginModeFlags.Floor) != 0)
                    subsystem.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Forage] VR: XR runtime rejected the tracking-origin query (" +
                                 e.GetType().Name + ": " + e.Message + "). Using fallback height.");
                ApplyFallbackHeight();
                yield break;
            }

            yield return null;

            TrackingOriginModeFlags actual;
            try { actual = subsystem.GetTrackingOriginMode(); }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Forage] VR: could not read the tracking origin mode (" +
                                 e.Message + "). Using fallback height.");
                ApplyFallbackHeight();
                yield break;
            }
            Debug.Log($"[Forage] VR: tracking origin mode in use = {actual}");

            if (actual == TrackingOriginModeFlags.Floor)
            {
                // Room-scale is live. The headset reports true head height, so
                // any offset of ours would stack on top of it.
                _origin.CameraYOffset = 0f;
                if (_origin.CameraFloorOffsetObject != null)
                    _origin.CameraFloorOffsetObject.transform.localPosition = Vector3.zero;

                subsystem.TryRecenter();
                Debug.Log("[Forage] VR: ROOM-SCALE ACTIVE — physically walking, " +
                          "leaning and crouching move the view. Head height comes " +
                          "from the headset.");
            }
            else
            {
                Debug.LogWarning($"[Forage] VR: headset would not grant Floor mode (got {actual}). " +
                                 "Falling back to a fixed eye height. Set up the room boundary / " +
                                 "switch off stationary mode on the headset for true room-scale.");
                ApplyFallbackHeight();
            }

            ReportHeadTracking();
        }

        void ApplyFallbackHeight()
        {
            if (_origin == null) return;
            _origin.CameraYOffset = seatedFallbackEyeHeight;
            if (_origin.CameraFloorOffsetObject != null)
                _origin.CameraFloorOffsetObject.transform.localPosition =
                    new Vector3(0f, seatedFallbackEyeHeight, 0f);
        }

        /// <summary>
        /// Logs whether the head pose is genuinely moving, so a "head tracking
        /// does nothing" report can be confirmed or ruled out from the log alone.
        /// </summary>
        void ReportHeadTracking()
        {
            var head = _origin != null && _origin.Camera != null
                ? _origin.Camera.transform : Camera.main?.transform;
            if (head == null) return;
            StartCoroutine(SampleHead(head));
        }

        IEnumerator SampleHead(Transform head)
        {
            var startPos = head.localPosition;
            var startRot = head.localRotation.eulerAngles;
            yield return new WaitForSeconds(2f);

            float moved = Vector3.Distance(head.localPosition, startPos);
            float turned = Quaternion.Angle(Quaternion.Euler(startRot), head.localRotation);

            var device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            bool tracked = device.isValid &&
                           device.TryGetFeatureValue(CommonUsages.isTracked, out bool t) && t;

            Debug.Log($"[Forage] VR head check: device valid={device.isValid} tracked={tracked} " +
                      $"name='{device.name}' | moved {moved:F3} m, turned {turned:F1} deg over 2 s | " +
                      $"local head pos {head.localPosition}");

            if (!tracked)
                Debug.LogError("[Forage] VR: the head device reports NOT TRACKED — " +
                               "the view will not follow your head. Check that the headset " +
                               "is on your face and the guardian is set up.");
            else if (moved < 0.001f && turned < 0.5f)
                Debug.LogWarning("[Forage] VR: head pose did not change in 2 s. If you were " +
                                 "moving, something is overriding the camera transform.");
        }
    }
}
