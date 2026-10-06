using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

namespace Forage
{
    /// <summary>
    /// Where the player's hands are right now, whichever input is in use: the
    /// held controllers, or tracked palms when the controllers are put down.
    /// Used by anything that reacts to a hand touching the world (pond ripples,
    /// burns from the fire).
    /// </summary>
    public static class PlayerHands
    {
        public struct Point
        {
            public int id;          // 1/2 left/right controller, 3/4 left/right palm
            public Vector3 position;
        }

        static XRInputModalityManager _modality;
        static Transform _trackingSpace;
        static readonly List<XRHandSubsystem> _subsystems = new List<XRHandSubsystem>();

        /// <summary>Fills <paramref name="into"/> with every hand point currently tracked.</summary>
        public static void Get(List<Point> into)
        {
            into.Clear();
            if (_modality == null)
                _modality = Object.FindFirstObjectByType<XRInputModalityManager>(FindObjectsInactive.Include);
            if (_trackingSpace == null)
            {
                var origin = Object.FindFirstObjectByType<XROrigin>();
                if (origin != null && origin.CameraFloorOffsetObject != null)
                    _trackingSpace = origin.CameraFloorOffsetObject.transform;
            }

            if (_modality != null)
            {
                AddController(into, 1, _modality.leftController);
                AddController(into, 2, _modality.rightController);
            }

            _subsystems.Clear();
            SubsystemManager.GetSubsystems(_subsystems);
            foreach (var s in _subsystems)
            {
                if (!s.running) continue;
                AddPalm(into, 3, s.leftHand);
                AddPalm(into, 4, s.rightHand);
                break;
            }
        }

        static void AddController(List<Point> into, int id, GameObject go)
        {
            if (go != null && go.activeInHierarchy)
                into.Add(new Point { id = id, position = go.transform.position });
        }

        static void AddPalm(List<Point> into, int id, XRHand hand)
        {
            if (!hand.isTracked || !hand.GetJoint(XRHandJointID.Palm).TryGetPose(out Pose pose)) return;
            Vector3 world = _trackingSpace != null ? _trackingSpace.TransformPoint(pose.position) : pose.position;
            into.Add(new Point { id = id, position = world });
        }
    }
}
