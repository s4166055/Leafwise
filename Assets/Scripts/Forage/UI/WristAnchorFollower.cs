using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace Forage
{
    /// <summary>
    /// Keeps the wrist HUD on the left wrist whichever input is in use.
    ///
    /// The HUD used to be a child of "Left Controller". With hand tracking on,
    /// XRInputModalityManager deactivates that object the moment you put the
    /// controllers down, and the HUD vanished with it. So the anchor now lives
    /// in tracking space and follows the controller while it is active, or the
    /// tracked left wrist joint when it is not.
    ///
    /// In hand mode the panel turns to face the eyes rather than lying flat on
    /// the wrist: a hand can be at any angle, and a watch face you have to
    /// twist your arm to read is worse than one that is always readable.
    /// </summary>
    public class WristAnchorFollower : MonoBehaviour
    {
        [Header("Wiring (set by ForageSceneBuilder)")]
        public Transform leftController;
        public Transform trackingSpace;     // XROrigin's camera floor offset: hand joints are relative to it

        [Header("Controller mode: watch position on the forearm")]
        public Vector3 controllerOffset = new Vector3(0f, 0.035f, -0.14f);
        public Vector3 controllerEuler = new Vector3(55f, 0f, 0f);

        [Header("Hand mode")]
        public float backOfWristLift = 0.035f;   // out of the back of the hand
        public float towardElbow = 0.04f;        // up the forearm, off the hand itself

        static readonly List<XRHandSubsystem> _hands = new List<XRHandSubsystem>();
        bool _shown = true;

        void LateUpdate()
        {
            if (leftController != null && leftController.gameObject.activeInHierarchy)
            {
                transform.SetPositionAndRotation(
                    leftController.TransformPoint(controllerOffset),
                    leftController.rotation * Quaternion.Euler(controllerEuler));
                Show(true);
                return;
            }

            if (TryGetLeftWrist(out var pos, out var rot))
            {
                // XR Hands joint axes: forward runs to the fingers, up out of the back of the hand
                Vector3 p = pos + rot * Vector3.up * backOfWristLift - rot * Vector3.forward * towardElbow;
                var cam = Camera.main;
                Quaternion face = cam != null
                    ? Quaternion.LookRotation(p - cam.transform.position, cam.transform.up)
                    : rot;
                transform.SetPositionAndRotation(p, face);
                Show(true);
                return;
            }

            // neither controller nor hand tracked: hide rather than float at the last pose
            Show(false);
        }

        bool TryGetLeftWrist(out Vector3 pos, out Quaternion rot)
        {
            pos = default;
            rot = Quaternion.identity;
            _hands.Clear();
            SubsystemManager.GetSubsystems(_hands);
            foreach (var s in _hands)
            {
                if (!s.running) continue;
                var hand = s.leftHand;
                if (!hand.isTracked) continue;
                if (!hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out Pose pose)) continue;

                var space = trackingSpace != null ? trackingSpace : transform.parent;
                pos = space != null ? space.TransformPoint(pose.position) : pose.position;
                rot = space != null ? space.rotation * pose.rotation : pose.rotation;
                return true;
            }
            return false;
        }

        void Show(bool show)
        {
            if (_shown == show) return;
            _shown = show;
            for (int i = 0; i < transform.childCount; i++)
                transform.GetChild(i).gameObject.SetActive(show);
        }
    }
}
