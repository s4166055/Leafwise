using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Forage.EditorTools
{
    /// <summary>
    /// Builds (or rebuilds) Assets/Scenes/Forage.unity from scratch:
    /// procedural textures, materials, XR rig, game systems, wrist HUD and
    /// build settings. Idempotent — safe to run repeatedly.
    /// </summary>
    public static class ForageSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Forage.unity";
        const string MaterialDir = "Assets/Forage/Materials";
        const string TextureDir = "Assets/Forage/Textures";
        // Hands variant: tracked hands (pinch to grab, poke) are the default, and
        // XRInputModalityManager switches to the controllers when they are picked up.
        const string RigPrefabPath = "Assets/VRTemplateAssets/Prefabs/Setup/Complete XR Origin Set Up Hands Variant.prefab";

        [MenuItem("Forage/Build Forage Scene")]
        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- lighting & atmosphere: late-afternoon forest ---
            var sun = new GameObject("Sun", typeof(Light));
            var light = sun.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.93f, 0.78f);
            light.intensity = 1.25f;
            light.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(38f, -38f, 0f);

            var dayNight = sun.AddComponent<DayNightCycle>();
            dayNight.sun = light;

            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.sun = light;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.6f, 0.7f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.5f, 0.4f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.24f, 0.16f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.011f;
            RenderSettings.fogColor = new Color(0.6f, 0.7f, 0.64f);

            // --- forest first: its height field decides where everything else sits ---
            var forestGo = new GameObject("Forest");
            var forest = forestGo.AddComponent<ForestGenerator>();

            // --- XR rig from the VR template ---
            // spawn on the camp plateau (the clearing is no longer flattened to y=0),
            // 1 m up so the character controller settles onto the ground
            var rig = CreateRig(new Vector3(0f, ForestGenerator.CampLevel(forest.seed) + 1f, 0f));
            if (rig == null) return;

            // --- systems ---
            var systems = new GameObject("Forage Systems");
            var manager = systems.AddComponent<GameManager>();
            var vitals = systems.AddComponent<PlayerVitals>();

            var mats = EnsureMaterials(forest);

            forest.terrainMat = mats.terrain;
            forest.barkMat = mats.bark;
            forest.birchBarkMat = mats.birchBark;
            forest.broadleafCards = new[] { mats.leafCardA, mats.leafCardB };
            forest.pineCards = new[] { mats.pineCardA, mats.pineCardB };
            forest.birchCards = new[] { mats.birchCard };
            forest.grassCard = mats.grassCard;
            forest.fernCard = mats.fernCard;
            forest.rockMat = mats.rock;
            forest.flowerStemMat = mats.flowerStem;
            forest.flowerPetalMat = mats.flowerPetal;
            forest.waterMat = mats.water;
            forest.fishMat = mats.fish;
            forest.crabMat = mats.crab;

            // ambient motion: drifting leaves + dust motes around the player
            // (own GameObject: it follows the player, so it must never parent scene content)
            var windFx = new GameObject("AmbientWind").AddComponent<AmbientWindFx>();
            windFx.leafParticleMat = mats.leafParticle;
            windFx.moteParticleMat = mats.moteParticle;

            var head = FindDeep(rig.transform, t => t.GetComponent<Camera>() != null);
            manager.vitals = vitals;
            manager.playerHead = head;

            var guard = systems.AddComponent<SpawnGuard>();
            guard.rig = rig.transform;

            // shared runtime assets + the camp (fire pit, drill, gatherables)
            var assets = systems.AddComponent<ForageAssets>();
            assets.stickWood = mats.stickWood;
            assets.tinderStraw = mats.tinderStraw;
            assets.stone = mats.rock;
            assets.potMetal = mats.potMetal;
            assets.charredWood = mats.charredWood;
            assets.flame = mats.flame;
            assets.smoke = mats.smoke;
            assets.ember = mats.ember;
            assets.mushroomStem = mats.mushroomStem;
            assets.capBrown = mats.capBrown;
            assets.capYellow = mats.capYellow;
            assets.capRed = mats.capRed;
            assets.capPale = mats.capPale;
            systems.AddComponent<CampsiteBuilder>();

            // sensory layer: audio ambience, feedback vignette, haptic hooks
            systems.AddComponent<AmbienceAndFeedback>();
            systems.AddComponent<SprintController>();
            systems.AddComponent<SimulatorGuard>();   // spawns the simulator only when no real headset is present
            new GameObject("ScreenFeedback").AddComponent<ScreenFeedback>();

            // wildlife: NavMesh bake + rabbits, snakes, squirrels
            var animals = systems.AddComponent<AnimalManager>();
            animals.shaderVariantAnchor = ShaderVariantAnchor();
            animals.squirrelModel = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/FurrySquirrel/Meshes/Squirrel.fbx");
            animals.squirrelController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/FurrySquirrel/Animations/Animations_Squirrel.controller");
            animals.squirrelAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/FurrySquirrel/Textures/Squirrel_AlbedoTransparency.png");
            animals.squirrelNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/FurrySquirrel/Textures/Squirrel_Normal.png");

            // --- wrist HUD on the left wrist (controller or tracked hand) ---
            var hudAnchor = CreateWristHudAnchor(rig);
            var hud = hudAnchor.AddComponent<WristHud>();
            hud.vitals = vitals;

            // lazy-follow glance strip: shows itself when vitals are low or changing
            var glance = new GameObject("GlanceHud").AddComponent<GlanceHud>();
            glance.vitals = vitals;

            // Scout: the visible AI companion voicing every hint
            new GameObject("Scout").AddComponent<ScoutCompanion>();

            EnsureWaterTag();
            EnableXrSimulator();

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[Forage] Forage scene built and saved to " + ScenePath);
        }

        // ------------------------------------------------------------------
        // XR rig
        // ------------------------------------------------------------------

        /// <summary>Instantiates and configures the player rig. Shared by the full build and the rig swap.</summary>
        static GameObject CreateRig(Vector3 position)
        {
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
            if (rigPrefab == null)
            {
                Debug.LogError($"[Forage] Rig prefab not found at {RigPrefabPath}");
                return null;
            }
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
            rig.name = "XR Origin Rig";
            rig.transform.position = position;

            // Room-scale (Floor / stage space). Device mode is the seated 3DOF
            // origin: it pins the origin to wherever the head happened to be at
            // startup, ignores the real floor, and therefore needs a faked
            // eye-height offset. With Floor the headset reports true head height
            // and position, so physically stepping, leaning, crouching and
            // turning all move the view 1:1 — the actual point of playing in VR.
            var origin = rig.GetComponentInChildren<Unity.XR.CoreUtils.XROrigin>();
            if (origin != null)
            {
                origin.RequestedTrackingOriginMode = Unity.XR.CoreUtils.XROrigin.TrackingOriginMode.Floor;

                // XROrigin applies CameraYOffset ONLY when the runtime is in
                // Device/Unbounded mode - including a mid-session drop from
                // Floor when the boundary is lost - and zeroes the floor offset
                // itself in Floor mode. So this is the seated fallback, not a
                // fixed height, and it must not be zero or that fallback is gone.
                // It is also the serialised height for flat play in the editor,
                // where no XR subsystem exists and XROrigin never moves the offset.
                origin.CameraYOffset = 1.7f;
                if (origin.CameraFloorOffsetObject != null)
                    origin.CameraFloorOffsetObject.transform.localPosition = new Vector3(0f, 1.7f, 0f);
            }

            // Log-only: reports which origin mode the headset actually granted.
            rig.AddComponent<VrTrackingSetup>();

            var characterController = rig.GetComponentInChildren<CharacterController>();
            if (characterController != null)
            {
                characterController.height = 1.75f;
                characterController.center = new Vector3(0f, 0.875f, 0f);
                // The forest is hilly and littered with roots and rocks. The
                // defaults (45 deg slope, 0.3 m step) snag constantly, which
                // reads as "movement is slow" even at a high move speed.
                characterController.slopeLimit = 60f;
                characterController.stepOffset = 0.6f;
                characterController.skinWidth = 0.03f;
            }

            // The hands rig (XRI Hands Interaction Demo base) switches gravity
            // off for its tabletop demo. Forage spawns 1 m up and walks hills,
            // so without gravity the player floats at spawn height forever.
            foreach (var gravity in rig.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity.GravityProvider>(true))
                gravity.useGravity = true;

            // The VR template's controller callouts pop labels up whenever you
            // glance at a controller: clutter in the middle of the view.
            foreach (var t in rig.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Affordance Callouts"))
                    t.gameObject.SetActive(false);

            LightenComfortVignette(rig);
            GrabsComeToHand(rig);
            return rig;
        }

        /// <summary>
        /// Far grabs (ray or pinch-at-a-distance) pull the object into your hand
        /// instead of leaving it hanging at the end of the ray, so a mushroom or
        /// fish picked up from afar can be brought to your mouth. Carried over
        /// from the environment branch, where it was set on the old rig's
        /// controller interactors only; this applies it to hands as well.
        /// </summary>
        public static void GrabsComeToHand(GameObject rig)
        {
            foreach (var nf in rig.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.NearFarInteractor>(true))
            {
                nf.farAttachMode = UnityEngine.XR.Interaction.Toolkit.Attachment.InteractorFarAttachMode.Near;
                PrefabUtility.RecordPrefabInstancePropertyModifications(nf);
            }
        }

        /// <summary>
        /// Re-applies the rig settings above to the saved Forage scene without
        /// rebuilding it. Also runs headless:
        /// Unity.exe -batchmode -projectPath . -executeMethod
        ///   Forage.EditorTools.ForageSceneBuilder.ApplyRigSettingsToScene -quit
        /// </summary>
        [MenuItem("Forage/Apply Rig Settings To Scene")]
        public static void ApplyRigSettingsToScene()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var rig = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "XR Origin Rig");
            if (rig == null)
            {
                Debug.LogError("[Forage] Apply rig settings: no 'XR Origin Rig' in " + ScenePath);
                return;
            }
            LightenComfortVignette(rig);
            GrabsComeToHand(rig);
            int interactors = rig.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.NearFarInteractor>(true).Length;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Forage] Rig settings applied: vignette 0.9, far grabs come to hand on {interactors} interactor(s). Scene saved.");
        }

        /// <summary>
        /// The template's tunneling vignette closes the view to 70% whenever you
        /// move or turn. At Forage's pace you are almost always moving, so the
        /// edges of the view were dark most of the time - the "it takes our
        /// vision" complaint. 90% keeps a hint of the comfort effect.
        /// </summary>
        public static void LightenComfortVignette(GameObject rig)
        {
            foreach (var v in rig.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort.TunnelingVignetteController>(true))
            {
                v.defaultParameters.apertureSize = 0.9f;
                v.defaultParameters.featheringEffect = 0.15f;
                PrefabUtility.RecordPrefabInstancePropertyModifications(v);
            }
        }

        /// <summary>
        /// The wrist HUD anchor lives in tracking space and follows the left
        /// controller or the tracked left wrist (see WristAnchorFollower). It
        /// must not be a child of "Left Controller": hand mode deactivates it.
        /// </summary>
        static GameObject CreateWristHudAnchor(GameObject rig)
        {
            var origin = rig.GetComponentInChildren<Unity.XR.CoreUtils.XROrigin>();
            var modality = rig.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Inputs.XRInputModalityManager>(true);
            var trackingSpace = origin != null && origin.CameraFloorOffsetObject != null
                ? origin.CameraFloorOffsetObject.transform
                : rig.transform;

            var hudAnchor = new GameObject("WristHudAnchor");
            hudAnchor.transform.SetParent(trackingSpace, false);
            var follow = hudAnchor.AddComponent<WristAnchorFollower>();
            follow.trackingSpace = trackingSpace;
            follow.leftController = modality != null && modality.leftController != null
                ? modality.leftController.transform
                : FindDeep(rig.transform, t =>
                    t.name.ToLowerInvariant().Contains("left") && t.name.ToLowerInvariant().Contains("controller"));
            return hudAnchor;
        }

        /// <summary>
        /// Replaces the rig in the open Forage scene with the current rig
        /// prefab, without rebuilding anything else. BuildScene regenerates the
        /// whole scene from scratch; this keeps every other object as it is.
        /// Scene references into the old rig are re-pointed by hierarchy path.
        /// </summary>
        [MenuItem("Forage/Swap Player Rig (keep scene)")]
        public static void SwapRig()
        {
            var scene = SceneManager.GetActiveScene();
            var oldRig = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "XR Origin Rig");
            if (oldRig == null)
            {
                Debug.LogError("[Forage] Rig swap: no root object named 'XR Origin Rig' in the open scene.");
                return;
            }

            var rig = CreateRig(oldRig.transform.position);
            if (rig == null) return;
            rig.transform.rotation = oldRig.transform.rotation;
            SceneManager.MoveGameObjectToScene(rig, scene);

            // carry the wrist HUD over (it was added to the old rig in the scene)
            var oldHud = oldRig.GetComponentInChildren<WristHud>(true);
            if (oldHud != null)
            {
                var anchor = CreateWristHudAnchor(rig);
                var hud = anchor.AddComponent<WristHud>();
                hud.vitals = oldHud.vitals;
            }

            // re-point every scene reference that targeted the old rig
            int repointed = 0, missed = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == oldRig || root == rig) continue;
                foreach (var comp in root.GetComponentsInChildren<Component>(true))
                {
                    if (comp == null) continue;
                    var so = new SerializedObject(comp);
                    var it = so.GetIterator();
                    bool changed = false;
                    while (it.Next(true))
                    {
                        if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                        var target = it.objectReferenceValue;
                        Transform targetT = target is GameObject go ? go.transform
                                          : target is Component c ? c.transform : null;
                        if (targetT == null || !targetT.IsChildOf(oldRig.transform)) continue;

                        var mapped = MapIntoNewRig(targetT, oldRig.transform, rig.transform);
                        Object replacement = mapped == null ? null
                            : target is GameObject ? mapped.gameObject
                            : (Object)mapped.GetComponent(target.GetType());
                        if (replacement == null)
                        {
                            missed++;
                            Debug.LogWarning($"[Forage] Rig swap: could not map {comp.GetType().Name}.{it.propertyPath} ({targetT.name})");
                            continue;
                        }
                        it.objectReferenceValue = replacement;
                        changed = true;
                        repointed++;
                    }
                    if (changed) so.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            Object.DestroyImmediate(oldRig);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Forage] Rig swapped to {RigPrefabPath}: {repointed} reference(s) re-pointed, {missed} unmapped. Scene saved.");
        }

        /// <summary>Finds the same object in the new rig: the camera by component, anything else by path.</summary>
        static Transform MapIntoNewRig(Transform target, Transform oldRoot, Transform newRoot)
        {
            if (target == oldRoot) return newRoot;
            if (target.GetComponent<Camera>() != null)
                return FindDeep(newRoot, t => t.GetComponent<Camera>() != null);

            var path = new System.Collections.Generic.List<string>();
            for (var t = target; t != oldRoot; t = t.parent) path.Insert(0, t.name);
            var found = newRoot.Find(string.Join("/", path));
            return found != null ? found : FindDeep(newRoot, t => t.name == target.name);
        }

        // ------------------------------------------------------------------
        // textures
        // ------------------------------------------------------------------

        static Texture2D SaveTexture(string name, Texture2D generated, bool cutout)
        {
            System.IO.Directory.CreateDirectory(TextureDir);
            string path = $"{TextureDir}/{name}.png";
            System.IO.File.WriteAllBytes(path, generated.EncodeToPNG());
            Object.DestroyImmediate(generated);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = cutout;
            importer.mipmapEnabled = true;
            importer.wrapMode = cutout ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------
        // materials
        // ------------------------------------------------------------------

        public struct Palette
        {
            public Material terrain, bark, birchBark, leafCardA, leafCardB, birchCard,
                pineCardA, pineCardB, grassCard, fernCard, rock, flowerStem, flowerPetal, water, fish, crab;
            public Material stickWood, tinderStraw, potMetal, charredWood, flame, smoke, ember;
            public Material mushroomStem, capBrown, capYellow, capRed, capPale;
            public Material leafParticle, moteParticle;
        }

        static Palette EnsureMaterials(ForestGenerator forest)
        {
            int seed = forest.seed;

            // ---- bake textures ----
            var terrainTex = SaveTexture("TerrainAlbedo",
                ProceduralTextures.TerrainAlbedo(
                    (x, z) => forest.HeightAt(x, z), (x, z) => forest.DensityAt(x, z),
                    forest.worldSize, forest.pondCenter, forest.pondRadius, forest.campRadius, seed),
                cutout: false);
            var barkTex = SaveTexture("Bark",
                ProceduralTextures.Bark(seed, new Color(0.42f, 0.3f, 0.2f), new Color(0.2f, 0.13f, 0.08f)), false);
            var birchTex = SaveTexture("BirchBark", ProceduralTextures.BirchBark(seed + 1), false);
            var leafATex = SaveTexture("LeafClusterA",
                ProceduralTextures.LeafCluster(seed + 2, new Color(0.2f, 0.42f, 0.16f), new Color(0.36f, 0.55f, 0.2f)), true);
            var leafBTex = SaveTexture("LeafClusterB",
                ProceduralTextures.LeafCluster(seed + 3, new Color(0.16f, 0.36f, 0.14f), new Color(0.3f, 0.48f, 0.18f)), true);
            var birchLeafTex = SaveTexture("BirchLeaves",
                ProceduralTextures.LeafCluster(seed + 4, new Color(0.42f, 0.52f, 0.18f), new Color(0.6f, 0.64f, 0.25f)), true);
            var pineATex = SaveTexture("PineBranchA",
                ProceduralTextures.PineBranch(seed + 5, new Color(0.14f, 0.32f, 0.18f)), true);
            var pineBTex = SaveTexture("PineBranchB",
                ProceduralTextures.PineBranch(seed + 6, new Color(0.19f, 0.4f, 0.22f)), true);
            var grassTex = SaveTexture("GrassBlades",
                ProceduralTextures.GrassBlades(seed + 7, new Color(0.35f, 0.52f, 0.2f)), true);
            var fernTex = SaveTexture("FernFronds",
                ProceduralTextures.PineBranch(seed + 8, new Color(0.16f, 0.42f, 0.2f)), true);
            var rockTex = SaveTexture("Rock", ProceduralTextures.Rock(seed + 9), false);
            var flyAgaricTex = SaveTexture("FlyAgaricCap",
                ProceduralTextures.SpottedCap(seed + 10, new Color(0.8f, 0.15f, 0.1f)), false);
            var singleLeafTex = SaveTexture("SingleLeaf",
                ProceduralTextures.SingleLeaf(seed + 11, new Color(0.5f, 0.55f, 0.22f)), true);
            var groundDetailTex = SaveTexture("GroundDetail", ProceduralTextures.GroundDetail(seed + 12), false);

            // ---- materials ----
            var terrainMat = Make("Terrain", Color.white, 0.02f, tex: terrainTex);
            terrainMat.SetTexture("_DetailAlbedoMap", groundDetailTex);
            terrainMat.SetTextureScale("_DetailAlbedoMap", new Vector2(90f, 90f));
            terrainMat.SetFloat("_DetailAlbedoMapScale", 1.0f);
            terrainMat.EnableKeyword("_DETAIL_MULX2");
            EditorUtility.SetDirty(terrainMat);

            return new Palette
            {
                terrain = terrainMat,
                bark = Make("Bark", Color.white, 0.05f, tex: barkTex),
                birchBark = Make("BirchBark", Color.white, 0.05f, tex: birchTex),
                leafCardA = MakeCutout("LeafCardA", leafATex, Color.white, 0.10f, 0.06f, 1.5f),
                leafCardB = MakeCutout("LeafCardB", leafBTex, new Color(0.92f, 0.96f, 0.88f), 0.11f, 0.07f, 1.7f),
                birchCard = MakeCutout("BirchCard", birchLeafTex, Color.white, 0.13f, 0.09f, 2.0f),
                pineCardA = MakeCutout("PineCardA", pineATex, Color.white, 0.05f, 0.025f, 1.2f),
                pineCardB = MakeCutout("PineCardB", pineBTex, Color.white, 0.06f, 0.03f, 1.3f),
                grassCard = MakeCutout("GrassCard", grassTex, Color.white, 0.16f, 0.08f, 2.2f),
                fernCard = MakeCutout("FernCard", fernTex, Color.white, 0.09f, 0.06f, 1.8f),
                rock = Make("Rock", Color.white, 0.08f, tex: rockTex),
                flowerStem = Make("FlowerStem", new Color(0.3f, 0.5f, 0.25f)),
                flowerPetal = Make("FlowerPetal", new Color(0.9f, 0.75f, 0.35f)),
                water = Make("Water", new Color(0.22f, 0.4f, 0.42f, 0.45f), 0.92f, transparent: true),
                fish = Make("Fish", new Color(0.6f, 0.65f, 0.7f), 0.5f),
                crab = Make("Crab", new Color(0.6f, 0.28f, 0.18f), 0.25f),

                stickWood = Make("StickWood", new Color(0.4f, 0.29f, 0.18f)),
                tinderStraw = Make("TinderStraw", new Color(0.78f, 0.68f, 0.4f)),
                potMetal = Make("PotMetal", new Color(0.35f, 0.36f, 0.4f), 0.6f),
                charredWood = Make("CharredWood", new Color(0.12f, 0.1f, 0.09f)),
                flame = MakeParticle("FlameParticle", new Color(1f, 0.55f, 0.1f, 0.9f), additive: true),
                smoke = MakeParticle("SmokeParticle", new Color(0.5f, 0.5f, 0.5f, 0.45f), additive: false),
                ember = MakeParticle("EmberParticle", new Color(1f, 0.45f, 0.1f, 1f), additive: true),

                mushroomStem = Make("MushroomStem", new Color(0.92f, 0.89f, 0.8f)),
                capBrown = Make("CapBrown", new Color(0.62f, 0.45f, 0.3f)),
                capYellow = Make("CapYellow", new Color(0.92f, 0.72f, 0.25f)),
                capRed = Make("CapRed", Color.white, 0.15f, tex: flyAgaricTex),
                capPale = Make("CapPale", new Color(0.85f, 0.88f, 0.78f)),
                leafParticle = MakeParticle("LeafParticle", Color.white, additive: false, tex: singleLeafTex),
                moteParticle = MakeParticle("MoteParticle", new Color(1f, 0.95f, 0.8f, 0.4f), additive: true),
            };
        }

        static Material Make(string name, Color color, float smoothness = 0.05f, bool transparent = false,
            Texture2D tex = null)
        {
            System.IO.Directory.CreateDirectory(MaterialDir);
            string path = $"{MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetTexture("_BaseMap", tex);
            if (transparent)
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON"); // stale keyword forces wrong blending
                mat.DisableKeyword("_ALPHAMODULATE_ON");
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Double-sided alpha-cutout foliage material with vertex wind sway.</summary>
        static Material MakeCutout(string name, Texture2D tex, Color tint,
            float windStrength = 0.1f, float flutter = 0.05f, float windSpeed = 1.6f)
        {
            System.IO.Directory.CreateDirectory(MaterialDir);
            string path = $"{MaterialDir}/{name}.mat";
            var shader = Shader.Find("Forage/FoliageWind");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", tint);
            mat.SetFloat("_Cutoff", 0.42f);
            mat.SetFloat("_WindStrength", windStrength);
            mat.SetFloat("_FlutterStrength", flutter);
            mat.SetFloat("_WindSpeed", windSpeed);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material MakeParticle(string name, Color color, bool additive, Texture2D tex = null)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            if (tex == null)
                tex = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
            if (tex != null) mat.SetTexture("_BaseMap", tex);

            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", additive ? 2f : 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", additive
                ? (int)UnityEngine.Rendering.BlendMode.One
                : (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------

        static void EnsureWaterTag()
        {
            var tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var tags = tagManager.FindProperty("tags");
            for (int i = 0; i < tags.arraySize; i++)
                if (tags.GetArrayElementAtIndex(i).stringValue == "Water") return;
            tags.InsertArrayElementAtIndex(tags.arraySize);
            tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = "Water";
            tagManager.ApplyModifiedProperties();
        }

        static void EnableXrSimulator()
        {
            const string settingsPath = "Assets/XRI/Settings/Resources/XRDeviceSimulatorSettings.asset";
            var settings = AssetDatabase.LoadMainAssetAtPath(settingsPath);
            if (settings == null)
            {
                Debug.LogWarning("[Forage] XRDeviceSimulatorSettings not found; simulator not enabled.");
                return;
            }

            var so = new SerializedObject(settings);
            var prefabProp = so.FindProperty("m_SimulatorPrefab");
            if (prefabProp.objectReferenceValue == null)
            {
                var prefab = FindSimulatorPrefab();
                if (prefab == null)
                {
                    ImportSimulatorSample();
                    prefab = FindSimulatorPrefab();
                }
                prefabProp.objectReferenceValue = prefab;
                so.FindProperty("m_UseClassic").boolValue =
                    prefab != null && prefab.name.Contains("Device");
            }

            // Off on purpose: XRI would spawn the simulator on every editor Play, even
            // with a real Quest connected over Link, and the camera then follows the
            // simulated HMD instead of the player's head. SimulatorGuard spawns it at
            // runtime only when no headset is active.
            so.FindProperty("m_AutomaticallyInstantiateSimulatorPrefab").boolValue = false;
            so.FindProperty("m_AutomaticallyInstantiateInEditorOnly").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
        }

        static GameObject FindSimulatorPrefab()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab Simulator"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (name == "XR Interaction Simulator" || name == "XR Device Simulator")
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            return null;
        }

        static void ImportSimulatorSample()
        {
            var samples = UnityEditor.PackageManager.UI.Sample.FindByPackage("com.unity.xr.interaction.toolkit", null);
            var sim = samples.FirstOrDefault(s => s.displayName.Contains("Simulator"));
            if (string.IsNullOrEmpty(sim.displayName))
            {
                Debug.LogWarning("[Forage] No simulator sample found in XRI package.");
                return;
            }
            sim.Import(UnityEditor.PackageManager.UI.Sample.ImportOptions.OverridePreviousImports);
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// A material asset whose only job is to be referenced by the built scene
        /// with <c>_NORMALMAP</c> enabled.
        ///
        /// URP strips shader_feature variants that no material in the build uses
        /// (UniversalRenderPipelineGlobalSettings.m_StripUnusedVariants). Every
        /// fur coat and the squirrel's rebuilt material are created at runtime
        /// with EnableKeyword("_NORMALMAP"), which the build-time variant
        /// collection cannot see; without this anchor the variant is stripped
        /// from the APK, the keyword selects a variant that does not exist, and
        /// the whole animal fix renders flat on device while looking right in
        /// the editor, where variants compile on demand.
        /// </summary>
        static Material ShaderVariantAnchor()
        {
            System.IO.Directory.CreateDirectory(MaterialDir);
            string path = $"{MaterialDir}/RuntimeVariantAnchor.mat";
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }
            // Texture2D.normalTexture is a built-in flat normal map, so the asset
            // has no dependency of its own.
            mat.SetTexture("_BumpMap", Texture2D.normalTexture);
            mat.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Transform FindDeep(Transform root, System.Func<Transform, bool> predicate)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (predicate(t)) return t;
            return null;
        }
    }
}
