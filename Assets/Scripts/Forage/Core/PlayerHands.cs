using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Forage
{
    /// <summary>
    /// Everything about the player's hands in one place.
    ///
    /// Controllers and buttons: finds the two controller transforms on the XR
    /// rig and reads the "drink" button (B / Y — the secondary face button) per
    /// hand. Input is read two ways so it works in every setup we use: the Input
    /// System XR controller (Quest via OpenXR and the XR Interaction Simulator,
    /// whose secondary button is bound to a key), and the legacy UnityEngine.XR
    /// device API as a fallback. Keyboard J is a desk fallback.
    ///
    /// Hand positions: <see cref="Get"/> lists where the hands are right now,
    /// whichever input is in use: the held controllers, or tracked palms when
    /// the controllers are put down. Used by anything that reacts to a hand
    /// touching the world (burns from the fire).
    /// </summary>
    public static class PlayerHands
    {
        // ------------------------------------------------------------------
        // controllers and the drink button
        // ------------------------------------------------------------------

        static Transform _left, _right;
        static int _lastFrame = -1;
        static bool _leftDown, _rightDown, _leftHeld, _rightHeld;
        static readonly List<UnityEngine.XR.InputDevice> _devices = new List<UnityEngine.XR.InputDevice>();

        public static Transform Left { get { Resolve(); return _left; } }
        public static Transform Right { get { Resolve(); return _right; } }

        static void Resolve()
        {
            if (_left != null && _right != null) return;
            var origin = Object.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin == null) return;
            foreach (var t in origin.GetComponentsInChildren<Transform>(true))
            {
                if (_left == null && t.name == "Left Controller") _left = t;
                else if (_right == null && t.name == "Right Controller") _right = t;
            }
        }

        /// <summary>True on the frame the drink button went down on that hand.</summary>
        public static bool DrinkPressed(bool left) { Poll(); return left ? _leftDown : _rightDown; }

        /// <summary>True on the frame the drink button went down on either hand (or J).</summary>
        public static bool DrinkPressedAny() { Poll(); return _leftDown || _rightDown; }

        static void Poll()
        {
            if (_lastFrame == Time.frameCount) return;
            _lastFrame = Time.frameCount;

            bool l = ReadButton(true), r = ReadButton(false);
            var kb = UnityEngine.InputSystem.Keyboard.current;
            bool key = kb != null && kb.jKey.isPressed;

            _leftDown = l && !_leftHeld;
            _rightDown = (r || key) && !_rightHeld;
            _leftHeld = l;
            _rightHeld = r || key;
        }

        static bool ReadButton(bool left)
        {
            // Input System (OpenXR controllers and the XR Interaction Simulator)
            var ctrl = left ? UnityEngine.InputSystem.XR.XRController.leftHand
                            : UnityEngine.InputSystem.XR.XRController.rightHand;
            if (ctrl != null)
            {
                var b = ctrl.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>("secondaryButton");
                if (b != null && b.isPressed) return true;
            }

            // legacy XR device API fallback
            _devices.Clear();
            UnityEngine.XR.InputDevices.GetDevicesAtXRNode(
                left ? UnityEngine.XR.XRNode.LeftHand : UnityEngine.XR.XRNode.RightHand, _devices);
            foreach (var d in _devices)
                if (d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool p) && p)
                    return true;
            return false;
        }

        /// <summary>
        /// Held by a hand (not merely seated in a socket such as the bucket stand).
        /// </summary>
        public static bool IsHandHeld(XRGrabInteractable grab)
        {
            if (grab == null || !grab.isSelected) return false;
            foreach (var i in grab.interactorsSelecting)
                if (!(i is XRSocketInteractor)) return true;
            return false;
        }

        /// <summary>Which hand is holding it (for per-hand haptics). Defaults to both.</summary>
        public static void HoldingHand(XRGrabInteractable grab, out bool left, out bool right)
        {
            left = right = false;
            if (grab != null)
                foreach (var i in grab.interactorsSelecting)
                {
                    if (i is XRSocketInteractor) continue;
                    if (i.handedness == InteractorHandedness.Left) left = true;
                    else if (i.handedness == InteractorHandedness.Right) right = true;
                }
            if (!left && !right) left = right = true;
        }

        // ------------------------------------------------------------------
        // where the hands are right now (controllers or tracked palms)
        // ------------------------------------------------------------------

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
                var origin = Object.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
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
