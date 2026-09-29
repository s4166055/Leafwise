using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Forage
{
    /// <summary>Simple controller haptics: rumble one hand or both.</summary>
    public static class Haptics
    {
        static readonly List<InputDevice> _devices = new List<InputDevice>();

        public static void Pulse(float amplitude, float seconds, bool left = true, bool right = true)
        {
            amplitude = Mathf.Clamp01(amplitude);
            if (left) PulseHand(XRNode.LeftHand, amplitude, seconds);
            if (right) PulseHand(XRNode.RightHand, amplitude, seconds);
        }

        static void PulseHand(XRNode node, float amplitude, float seconds)
        {
            _devices.Clear();
            InputDevices.GetDevicesAtXRNode(node, _devices);
            foreach (var device in _devices)
            {
                if (device.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse)
                    device.SendHapticImpulse(0u, amplitude, seconds);
            }
        }
    }
}
