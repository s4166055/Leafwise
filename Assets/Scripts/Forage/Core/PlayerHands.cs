using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Forage
{
    /// <summary>
    /// Finds the two controller transforms on the XR rig and reads the
    /// "drink" button (B / Y — the secondary face button) per hand.
    ///
    /// Input is read two ways so it works in every setup we use:
    /// the Input System XR controller (Quest via OpenXR and the XR Interaction
    /// Simulator, whose secondary button is bound to a key), and the legacy
    /// UnityEngine.XR device API as a fallback. Keyboard J is a desk fallback.
    /// </summary>
    public static class PlayerHands
    {
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
    }
}
