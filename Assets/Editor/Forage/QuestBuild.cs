using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace Forage.EditorTools
{
    /// <summary>
    /// One-command Quest 3 setup and APK build.
    ///
    /// Everything the headset needs is configured in code rather than clicked
    /// in the inspector, so the settings are versioned, reviewable and
    /// reproducible on any teammate's machine: the OpenXR loader for Android,
    /// the Meta Quest feature, controller interaction profiles, ARM64 + IL2CPP,
    /// Vulkan and multi-view rendering.
    /// </summary>
    public static class QuestBuild
    {
        const string ScenePath = "Assets/Scenes/Forage.unity";
        const string OutputDir = "Builds";
        const string ApkName = "Leafwise.apk";

        // ------------------------------------------------------------------
        // 1. XR + player configuration
        // ------------------------------------------------------------------

        [MenuItem("Forage/Quest/1 - Configure XR and Player Settings", priority = 100)]
        public static void ConfigureForQuest()
        {
            var named = NamedBuildTarget.Android;

            // ---- identity ----
            PlayerSettings.companyName = "RMIT Mixed Reality";
            PlayerSettings.productName = "Forage";
            PlayerSettings.SetApplicationIdentifier(named, "com.rmit.forage");
            PlayerSettings.bundleVersion = "0.8.0";
            PlayerSettings.Android.bundleVersionCode = 8;

            // ---- architecture: Quest 3 is ARM64 only, and requires IL2CPP ----
            PlayerSettings.SetScriptingBackend(named, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetIl2CppCompilerConfiguration(named, Il2CppCompilerConfiguration.Release);
            PlayerSettings.SetApiCompatibilityLevel(named, ApiCompatibilityLevel.NET_Standard);

            // ---- Android API levels required by the Meta store ----
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

            // ---- graphics ----
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan });
            PlayerSettings.colorSpace = ColorSpace.Linear;

            // multi-view renders both eyes in one pass: the single biggest Quest win
            PlayerSettings.stereoRenderingPath = StereoRenderingPath.Instancing;

            PlayerSettings.gpuSkinning = true;
            PlayerSettings.MTRendering = true;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.Android.startInFullscreen = true;
            PlayerSettings.Android.renderOutsideSafeArea = true;
            PlayerSettings.Android.forceInternetPermission = false;
            PlayerSettings.Android.forceSDCardPermission = false;

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            EnableOpenXr(BuildTargetGroup.Android);     // the shipped Quest build
            EnableOpenXr(BuildTargetGroup.Standalone);  // so editor Play uses a headset over Link

            AssetDatabase.SaveAssets();
            Debug.Log("[Forage] Quest configuration applied: ARM64, IL2CPP, Vulkan, " +
                      "multi-view, minSdk 32, OpenXR + Meta Quest.");
        }

        /// <summary>
        /// Turn on the OpenXR loader and the Meta Quest features for a build target.
        ///
        /// Android is the shipped build. Standalone matters just as much in
        /// practice: it is what the editor's Play button uses, so without it a
        /// Quest connected over Link is invisible to Play mode and you end up
        /// testing the simulator while believing you are testing the headset.
        /// Configuring both here keeps that in a reviewable diff instead of
        /// depending on whoever last opened the XR Plug-in Management window.
        /// </summary>
        static void EnableOpenXr(BuildTargetGroup group)
        {
            // Direct, compile-checked calls on purpose. An earlier version looked
            // these types up with Type.GetType(); that returned null in batch mode
            // and the whole XR setup silently did nothing, which is how the Android
            // OpenXR loader went missing while every log line still said success.
            // Forage.Editor.asmdef references Unity.XR.Management(.Editor), so a
            // missing package is now a build error rather than a silent skip.
            var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
            if (settings == null)
            {
                Debug.LogError($"[Forage] No XR settings for {group}. Open Project Settings > " +
                               "XR Plug-in Management once, then re-run.");
                return;
            }

            settings.InitManagerOnStart = true;

            var manager = settings.AssignedSettings;
            if (manager == null)
            {
                Debug.LogError($"[Forage] XR manager settings missing for {group}.");
                return;
            }

            bool assigned = XRPackageMetadataStore.AssignLoader(manager, typeof(OpenXRLoader).FullName, group);
            Debug.Log($"[Forage] OpenXR loader assigned for {group}: {assigned}");

            var openXr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (openXr == null)
            {
                Debug.LogWarning($"[Forage] OpenXR settings asset not found for {group}.");
                return;
            }

            openXr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;

            // Without an interaction profile the controllers report no input on device.
            ApplyFeatures(openXr);
            EditorUtility.SetDirty(openXr);
        }

        /// <summary>The interaction profiles the Quest actually needs.</summary>
        static readonly string[] WantedFeatures =
        {
            "MetaQuestTouchPlusControllerProfile",   // Quest 3 / 3S controllers
            "MetaQuestTouchProControllerProfile",    // Quest Pro controllers
            "OculusTouchControllerProfile",          // Quest 2 and older
            "MetaQuestFeature",

            // Tracked hands. The rig (Complete XR Origin Set Up Hands Variant) has
            // hand objects wired into XRInputModalityManager, so putting the
            // controllers down switches to hands instead of stranding the player.
            "HandTracking",            // XR Hands joint data: hand visuals and the wrist HUD
            "MetaHandTrackingAim",     // pinch strength and aim pose on Quest
            "HandInteractionProfile",  // pinch pose / pinch value the grab interactors read
        };

        /// <summary>
        /// Features that must stay off, each with the reason it is off.
        /// </summary>
        static readonly (string Name, string Reason)[] UnwantedFeatures =
        {
            // Meta's detached-controller profiles (XR_META_detached_controllers)
            // suggest binding paths this runtime rejects, e.g.
            // /user/detached_controller_meta/left/input/thumbrest/force.
            // xrSuggestInteractionProfileBindings then fails with
            // XR_ERROR_PATH_UNSUPPORTED, and because that call registers the whole
            // combined binding set atomically, ONE bad path discards every
            // controller binding: controllers still track, but nothing is bound to
            // grab or trigger, so the game looks like it ignores your hands.
            ("DetachedMetaQuestTouchPlusControllerProfile", "its binding paths discard all controller bindings"),
            ("DetachedMetaQuestTouchProControllerProfile",  "its binding paths discard all controller bindings"),
            ("DetachedOculusTouchControllerProfile",        "its binding paths discard all controller bindings"),

            // Hand tracking used to be listed here: the old controller-only rig had
            // no hand objects, so hand mode left the player with no input at all.
            // The hands rig fixed that, and it is now in WantedFeatures.
        };

        /// <summary>
        /// Match feature type names EXACTLY. An earlier version matched with
        /// Contains(), and "MetaQuestTouchPlusControllerProfile" is a substring
        /// of "DetachedMetaQuestTouchPlusControllerProfile" - so asking for the
        /// Touch Plus profile silently switched on the detached one beside it.
        ///
        /// Each feature is its own sub-asset, so it must be marked dirty itself.
        /// Dirtying only the parent OpenXRSettings let the editor build with the
        /// features on while SaveAssets never wrote them to disk, so a teammate
        /// who pulled the repo got every interaction profile and hand feature off.
        /// </summary>
        static void ApplyFeatures(OpenXRSettings settings)
        {
            foreach (var f in settings.GetFeatures<OpenXRFeature>())
            {
                if (f == null) continue;
                string name = f.GetType().Name;

                if (System.Array.IndexOf(WantedFeatures, name) >= 0)
                {
                    if (!f.enabled) { f.enabled = true; EditorUtility.SetDirty(f); Debug.Log("[Forage]   enabled  " + name); }
                }
                else
                {
                    var unwanted = System.Array.Find(UnwantedFeatures, u => u.Name == name);
                    if (unwanted.Name != null && f.enabled)
                    {
                        f.enabled = false;
                        EditorUtility.SetDirty(f);
                        Debug.Log($"[Forage]   DISABLED {name} - {unwanted.Reason}");
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // 2. Pre-flight validation
        // ------------------------------------------------------------------

        [MenuItem("Forage/Quest/2 - Validate Build Readiness", priority = 101)]
        public static bool Validate()
        {
            bool ok = true;
            void Check(bool condition, string label, string fix)
            {
                if (condition) Debug.Log("[Forage] PASS  " + label);
                else { Debug.LogError("[Forage] FAIL  " + label + "  -> " + fix); ok = false; }
            }

            var named = NamedBuildTarget.Android;

            Check(File.Exists(ScenePath), "Forage scene exists", "Run Forage > Build Forage Scene");
            Check(EditorBuildSettings.scenes.Any(s => s.path == ScenePath && s.enabled),
                  "Forage scene is in build settings", "Run step 1");
            Check(PlayerSettings.GetScriptingBackend(named) == ScriptingImplementation.IL2CPP,
                  "IL2CPP scripting backend", "Run step 1");
            Check(PlayerSettings.Android.targetArchitectures == AndroidArchitecture.ARM64,
                  "ARM64 only", "Run step 1");
            Check((int)PlayerSettings.Android.minSdkVersion >= 32, "minSdkVersion 32 or higher", "Run step 1");
            Check(PlayerSettings.colorSpace == ColorSpace.Linear, "Linear colour space", "Run step 1");
            Check(PlayerSettings.stereoRenderingPath == StereoRenderingPath.Instancing,
                  "Multi-view (single pass instanced)", "Run step 1");

            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            Check(apis.Length > 0 && apis[0] == UnityEngine.Rendering.GraphicsDeviceType.Vulkan,
                  "Vulkan is the primary graphics API", "Run step 1");

            var xrSettings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            bool loaderOk = xrSettings != null && xrSettings.AssignedSettings != null &&
                            xrSettings.AssignedSettings.activeLoaders.Any(l => l is OpenXRLoader);
            Check(loaderOk, "OpenXR loader active for Android", "Run step 1");

            var openXr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            bool questOk = openXr != null && openXr.GetFeatures<OpenXRFeature>()
                .Any(f => f != null && f.enabled && f.GetType().Name.Contains("MetaQuest"));
            Check(questOk, "Meta Quest OpenXR feature enabled", "Run step 1");

            bool profileOk = openXr != null && openXr.GetFeatures<OpenXRFeature>()
                .Any(f => f != null && f.enabled && f.GetType().Name.Contains("ControllerProfile"));
            Check(profileOk, "A controller interaction profile is enabled", "Run step 1");

            bool handsOk = openXr != null && new[] { "HandTracking", "MetaHandTrackingAim" }.All(n =>
                openXr.GetFeatures<OpenXRFeature>().Any(f => f != null && f.enabled && f.GetType().Name == n));
            Check(handsOk, "Hand tracking features enabled (hands-first rig)", "Run step 1");

            // One detached profile left on kills every controller binding, so it
            // is a build blocker rather than a warning.
            var detached = openXr == null ? null : openXr.GetFeatures<OpenXRFeature>()
                .FirstOrDefault(f => f != null && f.enabled &&
                                     UnwantedFeatures.Any(u => u.Name == f.GetType().Name));
            Check(detached == null, "No input-breaking OpenXR feature enabled",
                  detached == null ? "Run step 1" : $"{detached.GetType().Name} is on - run step 1");

            string androidPlayer = Path.Combine(EditorApplication.applicationContentsPath,
                                                "PlaybackEngines", "AndroidPlayer");
            Check(Directory.Exists(androidPlayer), "Android Build Support installed",
                  "Unity Hub > Installs > Add Modules > Android Build Support");

            Debug.Log(ok ? "[Forage] READY TO BUILD" : "[Forage] NOT READY - fix the failures above");
            return ok;
        }

        // ------------------------------------------------------------------
        // 3. Build
        // ------------------------------------------------------------------

        [MenuItem("Forage/Quest/3 - Build APK", priority = 102)]
        public static void BuildApk() => Build(andRun: false);

        [MenuItem("Forage/Quest/4 - Build and Run on Headset", priority = 103)]
        public static void BuildAndRun() => Build(andRun: true);

        static void Build(bool andRun)
        {
            if (!Validate())
            {
                Debug.LogError("[Forage] Build aborted - validation failed.");
                return;
            }

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                Debug.Log("[Forage] Switching platform to Android (this can take several minutes)...");
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            }

            Directory.CreateDirectory(OutputDir);
            string apk = Path.Combine(OutputDir, ApkName);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = apk,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = andRun ? BuildOptions.AutoRunPlayer : BuildOptions.None
            };

            Debug.Log("[Forage] Building " + apk + " ...");
            var report = UnityEditor.BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.Log($"[Forage] BUILD SUCCEEDED -> {apk}  " +
                          $"({summary.totalSize / (1024f * 1024f):F1} MB in {summary.totalTime.TotalMinutes:F1} min)");
            }
            else
            {
                Debug.LogError($"[Forage] BUILD {summary.result}: {summary.totalErrors} error(s). " +
                               "See the Console and Editor.log for detail.");
            }
        }
    }
}
