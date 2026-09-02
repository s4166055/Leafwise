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
        const string RigPrefabPath = "Assets/VRTemplateAssets/Prefabs/Setup/Complete XR Origin Set Up Variant.prefab";

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

            // --- XR rig from the VR template ---
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
            if (rigPrefab == null)
            {
                Debug.LogError($"[Forage] Rig prefab not found at {RigPrefabPath}");
                return;
            }
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
            rig.name = "XR Origin Rig";
            rig.transform.position = new Vector3(0f, 1f, 0f);

            // --- systems ---
            var systems = new GameObject("Forage Systems");
            var manager = systems.AddComponent<GameManager>();
            var vitals = systems.AddComponent<PlayerVitals>();

            // forest first (its field functions drive the terrain texture bake)
            var forestGo = new GameObject("Forest");
            var forest = forestGo.AddComponent<ForestGenerator>();

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
            new GameObject("ScreenFeedback").AddComponent<ScreenFeedback>();

            // wildlife: NavMesh bake + rabbits, snakes, squirrels
            var animals = systems.AddComponent<AnimalManager>();
            animals.squirrelModel = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/FurrySquirrel/Meshes/Squirrel.fbx");
            animals.squirrelController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/FurrySquirrel/Animations/Animations_Squirrel.controller");
            animals.squirrelAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/FurrySquirrel/Textures/Squirrel_AlbedoTransparency.png");
            animals.squirrelNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/FurrySquirrel/Textures/Squirrel_Normal.png");

            // --- wrist HUD on the left controller ---
            var leftHand = FindDeep(rig.transform, t =>
                t.name.ToLowerInvariant().Contains("left") && t.name.ToLowerInvariant().Contains("controller"));
            var hudAnchor = new GameObject("WristHudAnchor");
            hudAnchor.transform.SetParent(leftHand != null ? leftHand : rig.transform, false);
            hudAnchor.transform.localPosition = new Vector3(0f, 0.035f, -0.14f); // watch position on the forearm
            hudAnchor.transform.localRotation = Quaternion.Euler(55f, 0f, 0f);
            var hud = hudAnchor.AddComponent<WristHud>();
            hud.vitals = vitals;

            // lazy-follow glance strip: shows itself when vitals are low or changing
            var glance = new GameObject("GlanceHud").AddComponent<GlanceHud>();
            glance.vitals = vitals;

            EnsureWaterTag();
            EnableXrSimulator();

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[Forage] Forage scene built and saved to " + ScenePath);
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

            so.FindProperty("m_AutomaticallyInstantiateSimulatorPrefab").boolValue = true;
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

        static Transform FindDeep(Transform root, System.Func<Transform, bool> predicate)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (predicate(t)) return t;
            return null;
        }
    }
}
