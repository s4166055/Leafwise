using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR;
#if UNITY_EDITOR
// XRI guards this namespace with `#if ENABLE_VR || UNITY_GAMECORE`, so it is not
// unconditionally present in a player build. Everything that uses it here is
// editor-only, so the import is too - otherwise a build configuration without
// ENABLE_VR fails to compile on a namespace the shipped game never needs.
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
#endif

namespace Forage
{
    /// <summary>
    /// Spawns the XR Interaction Simulator only when there is no real headset.
    ///
    /// XRI's own loader (<c>XRInteractionSimulatorLoader.Initialize</c>) runs
    /// after every scene load and instantiates the simulator whenever the
    /// project setting says so. It has no notion of a connected HMD. The
    /// simulator then registers an <c>XRSimulatedHMD</c>, the camera's
    /// TrackedPoseDriver binds to <c>/XRSimulatedHMD/centerEyePosition</c>,
    /// and the real Quest, tracking perfectly over Link, is ignored. That was
    /// the whole "I have to use the joystick to look around" experience: every
    /// headset test had gone through the editor, and the simulator owned the
    /// camera each time.
    ///
    /// So the project setting is off, and this component decides at runtime.
    /// Real headset active: do nothing, the OpenXR HMD drives the camera.
    /// No headset: instantiate the simulator so desk testing still works.
    /// The whole body is editor-only; on device this component is inert.
    /// </summary>
    public class SimulatorGuard : MonoBehaviour
    {
        /// <summary>How long to give the XR display to come up over Link before deciding.</summary>
        public float detectWindowSeconds = 1.5f;

#if UNITY_EDITOR
        const string SettingsResource = "XRDeviceSimulatorSettings";

        void Start() => StartCoroutine(Decide());

        IEnumerator Decide()
        {
            // XR initialises before the scene loads, but a Link session can take a
            // few frames to report an active display. Poll briefly, bound by
            // wall-clock so an unfocused editor cannot stall it.
            float deadline = Time.realtimeSinceStartup + detectWindowSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (RealHeadsetPresent()) break;
                yield return null;
            }

            if (RealHeadsetPresent())
            {
                var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
                Debug.Log($"[Forage] Simulator: real headset detected ('{head.name}' via {XRSettings.loadedDeviceName}). " +
                          "XR Interaction Simulator NOT spawned - your head drives the camera.");
                yield break;
            }

            // Say why there is no headset. A loader that is active with no display
            // is XR stuck from an interrupted previous Play (the OpenXR loader can
            // throw inside Deinitialize and never release); that needs an editor
            // restart and is a different problem from an unplugged Quest.
            string loaderState = DescribeLoaderState();
            Debug.Log($"[Forage] Simulator: no headset detected ({loaderState}).");

            if (FindFirstObjectByType<XRInteractionSimulator>() != null)
            {
                Debug.Log("[Forage] Simulator: already present, leaving it alone.");
                yield break;
            }

            var prefab = LoadSimulatorPrefab();
            if (prefab == null)
            {
                Debug.LogWarning("[Forage] Simulator: no headset and no simulator prefab assigned in " +
                                 "XRDeviceSimulatorSettings - desk testing will have no head/controller input.");
                yield break;
            }

            var sim = Instantiate(prefab);
            sim.name = prefab.name;
            DontDestroyOnLoad(sim);
            Debug.Log("[Forage] Simulator: no headset detected - XR Interaction Simulator spawned for desk testing " +
                      "(WASD move, mouse look, Left Shift sprint).");

            // The sample's feedback UI throws every frame; keep the console usable.
            InvokeRepeating(nameof(SweepBuggyUi), 0.5f, 2f);
        }

        static bool RealHeadsetPresent()
        {
            if (!XRSettings.isDeviceActive) return false;
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            // Before the simulator exists, the only way this is valid is a real device.
            return head.isValid && !head.name.Contains("Simulated");
        }

        static string DescribeLoaderState()
        {
            var settingsType = Type.GetType("UnityEngine.XR.Management.XRGeneralSettings, UnityEngine.XR.Management");
            if (settingsType == null)
                return "no XR manager assembly loaded";

            var instance = settingsType.GetProperty("Instance")?.GetValue(null);
            if (instance == null)
                return "no XR manager";

            var manager = settingsType.GetProperty("Manager")?.GetValue(instance);
            if (manager == null)
                return "no XR manager";

            var activeLoaderProp = manager.GetType().GetProperty("activeLoader");
            var activeLoader = activeLoaderProp?.GetValue(manager);
            if (activeLoader == null)
                return "no active loader (XR init failed or no runtime)";

            var nameProp = activeLoader.GetType().GetProperty("name");
            var loaderName = nameProp?.GetValue(activeLoader) as string ?? activeLoader.GetType().Name;
            return $"loader {loaderName} active but no display - XR is stuck, restart the editor";
        }

        /// <summary>
        /// The settings class is internal to XRI, so read the prefab reference
        /// straight off the serialised asset that ships in Resources.
        /// </summary>
        static GameObject LoadSimulatorPrefab()
        {
            var settings = Resources.Load<ScriptableObject>(SettingsResource);
            if (settings == null) return null;
            var so = new UnityEditor.SerializedObject(settings);
            return so.FindProperty("m_SimulatorPrefab")?.objectReferenceValue as GameObject;
        }

        void SweepBuggyUi()
        {
            int disabled = 0;
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb == null || !mb.enabled) continue;
                if (mb.GetType().Name == "XRInteractionSimulatorInputFeedbackUI") { mb.enabled = false; disabled++; }
            }
            if (disabled > 0)
                Debug.Log($"[Forage] Disabled {disabled} buggy simulator feedback UI component(s) (Unity sample bug).");
        }
#endif
    }
}
