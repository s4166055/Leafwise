using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Forage.Testing
{
    /// <summary>One line of a report: a check, a section header, or a note.</summary>
    public sealed class InvariantEntry
    {
        public enum Kind { Section, Check, Note }

        public Kind Type;
        public bool Ok;
        public string Label;
        public string Detail;

        public override string ToString()
        {
            switch (Type)
            {
                case Kind.Section: return "\n== " + Label + " ==";
                case Kind.Note: return "      (" + Label + ")";
                default:
                    return (Ok ? "PASS  " : "FAIL  ") + Label +
                           (string.IsNullOrEmpty(Detail) ? "" : "   [" + Detail + "]");
            }
        }
    }

    public sealed class InvariantReport
    {
        public readonly List<InvariantEntry> Entries = new List<InvariantEntry>();

        public IEnumerable<InvariantEntry> Checks => Entries.Where(e => e.Type == InvariantEntry.Kind.Check);
        public int Passed => Checks.Count(c => c.Ok);
        public int Failed => Checks.Count(c => !c.Ok);
        public IEnumerable<InvariantEntry> Failures => Checks.Where(c => !c.Ok);

        internal void Section(string title) =>
            Entries.Add(new InvariantEntry { Type = InvariantEntry.Kind.Section, Label = title });

        internal void Note(string text) =>
            Entries.Add(new InvariantEntry { Type = InvariantEntry.Kind.Note, Label = text });

        internal void Check(bool ok, string label, string detail = "") =>
            Entries.Add(new InvariantEntry { Type = InvariantEntry.Kind.Check, Ok = ok, Label = label, Detail = detail });

        public string ToText(string title)
        {
            var sb = new StringBuilder();
            sb.AppendLine(title);
            sb.AppendLine("run: " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            foreach (var e in Entries) sb.AppendLine(e.ToString());
            sb.AppendLine();
            sb.AppendLine($"RESULT: {Passed} passed, {Failed} failed");
            return sb.ToString();
        }
    }

    /// <summary>
    /// The one list of things that must be true of a running Forage world.
    ///
    /// Both harnesses call this - the editor self-test that writes a report to
    /// Temp/, and the Unity Test Framework PlayMode test - so there is a single
    /// definition of "the world is healthy" rather than two hand-maintained
    /// copies that drift apart. It is runtime code on purpose: everything it
    /// inspects exists only after ForestGenerator.Awake() has run.
    ///
    /// Each check pins either a bug we have actually shipped (camp in a crater,
    /// fire pit in a hole, deer buried in the ground, magenta materials, player
    /// falling through the terrain) or a value we deliberately chose (world
    /// size, speeds, room-scale tracking), so a change that regresses one fails
    /// a test instead of being discovered by eye.
    /// </summary>
    public static class WorldInvariants
    {
        public static InvariantReport Run()
        {
            var r = new InvariantReport();
            var forest = Object.FindFirstObjectByType<ForestGenerator>();

            r.Section("Terrain and camp clearing");
            r.Check(forest != null, "ForestGenerator present in scene");
            if (forest == null) return r;

            r.Check(Mathf.Abs(forest.worldSize - 144f) < 0.01f, "World is 20% smaller (worldSize 144)",
                    "actual " + forest.worldSize);

            float campH = forest.HeightAt(0f, 0f);
            float campLevel = ForestGenerator.CampLevel(forest.seed);
            r.Check(Mathf.Abs(campH - campLevel) < 0.05f, "Camp centre sits at natural ground level (not a crater)",
                    $"height {campH:F2} vs CampLevel {campLevel:F2}");

            // the clearing must not be a bowl: compare with a ring of forest around it
            float ring = 0f;
            for (int i = 0; i < 24; i++)
            {
                float a = i / 24f * Mathf.PI * 2f;
                ring += forest.HeightAt(Mathf.Cos(a) * 20f, Mathf.Sin(a) * 20f);
            }
            ring /= 24f;
            r.Check(Mathf.Abs(campH - ring) < 2.5f, "Camp is level with the surrounding forest",
                    $"camp {campH:F2} vs ring@20m {ring:F2} (delta {campH - ring:F2} m)");

            var terrain = GameObject.Find("Terrain");
            r.Check(terrain != null && terrain.GetComponent<MeshCollider>() != null,
                    "Terrain mesh has a collider (walkable)");

            bool hitOk = Physics.Raycast(new Vector3(0f, campH + 60f, 0f), Vector3.down, out var hit, 200f);
            r.Check(hitOk && Mathf.Abs(hit.point.y - campH) < 0.3f, "Ground raycast at camp matches the height field",
                    hitOk ? $"hit y {hit.point.y:F2}, expected {campH:F2}" : "no hit");

            r.Section("Camp fire pit");
            var pit = Object.FindFirstObjectByType<FirePit>();
            r.Check(pit != null, "Fire pit present");
            if (pit != null)
            {
                float pitGround = forest.HeightAt(pit.transform.position.x, pit.transform.position.z);
                float drop = pit.transform.position.y - pitGround;
                r.Check(Mathf.Abs(drop) < 0.6f, "Fire pit rests on the ground (not sunk in a hole)",
                        $"pit y {pit.transform.position.y:F2}, ground {pitGround:F2}, delta {drop:F2} m");
            }

            r.Section("Forest density");
            var propsRoot = GameObject.Find("GeneratedForest");
            r.Check(propsRoot != null, "GeneratedForest root exists");
            var renderers = propsRoot != null
                ? propsRoot.GetComponentsInChildren<MeshRenderer>(true)
                : new MeshRenderer[0];
            r.Note($"total mesh renderers under GeneratedForest: {renderers.Length}");
            r.Check(renderers.Length >= 1500, "Dense forest: 1500+ props generated", renderers.Length.ToString());

            int Count(string prefix) => renderers.Count(x => x.transform.parent != null &&
                (x.name.StartsWith(prefix) || x.transform.parent.name.StartsWith(prefix)));
            int trees = Count("Tree") + Count("Pine") + Count("Birch") + Count("Broadleaf");
            r.Note($"tree-ish renderers: {trees}");
            r.Check(trees >= 200, "Tree cover is substantial", trees.ToString());

            r.Section("Wind / moving foliage");
            var windShader = Shader.Find("Forage/FoliageWind");
            r.Check(windShader != null, "Forage/FoliageWind shader compiles and is findable");
            var windMats = renderers.SelectMany(x => x.sharedMaterials)
                                    .Where(m => m != null && m.shader == windShader)
                                    .Distinct().ToList();
            r.Check(windMats.Count >= 3, "Multiple foliage materials use the wind shader", windMats.Count + " materials");
            r.Check(windMats.Any(m => m.HasProperty("_WindStrength") && m.GetFloat("_WindStrength") > 0f),
                    "Wind strength is non-zero (leaves actually move)");
            int windRenderers = renderers.Count(x => x.sharedMaterials.Any(m => m != null && m.shader == windShader));
            r.Note($"renderers swaying in the wind: {windRenderers}");
            r.Check(windRenderers >= 500, "Wind applies to a large share of the foliage", windRenderers.ToString());

            r.Section("Pond");
            var water = GameObject.Find("PondWater");
            r.Check(water != null, "Pond water surface exists");
            if (water != null)
            {
                var wm = water.GetComponent<MeshRenderer>()?.sharedMaterial;
                bool transparent = wm != null &&
                    ((wm.HasProperty("_Surface") && wm.GetFloat("_Surface") > 0.5f) ||
                     (wm.HasProperty("_BaseColor") && wm.GetColor("_BaseColor").a < 0.95f));
                r.Check(transparent, "Water is transparent (fish are visible through it)",
                        wm == null ? "no material" : $"alpha {(wm.HasProperty("_BaseColor") ? wm.GetColor("_BaseColor").a : -1f):F2}");
                r.Check(water.CompareTag("Water"), "Water is tagged for the scooping interaction");
            }
            var life = Object.FindFirstObjectByType<PondLife>();
            r.Check(life != null && life.GetComponentsInChildren<MeshRenderer>(true).Length >= 5,
                    "Fish and crabs populate the pond",
                    life == null ? "no PondLife" : life.GetComponentsInChildren<MeshRenderer>(true).Length + " creatures");

            r.Section("Habitat zones");
            r.Check(Habitat.ZoneAt(0f, 0f) == Habitat.Zone.Camp, "Origin classifies as the Camp zone",
                    Habitat.ZoneAt(0f, 0f).ToString());
            var seen = new HashSet<Habitat.Zone>();
            for (float x = -70f; x <= 70f; x += 5f)
                for (float z = -70f; z <= 70f; z += 5f)
                    seen.Add(Habitat.ZoneAt(x, z));
            foreach (Habitat.Zone z in System.Enum.GetValues(typeof(Habitat.Zone)))
                r.Check(seen.Contains(z), "Zone present in the world: " + z);

            r.Section("Animals and habitats");
            var mgr = Object.FindFirstObjectByType<AnimalManager>();
            r.Check(mgr != null, "AnimalManager present");
            if (mgr != null)
            {
                r.Check(mgr.NavMeshReady, "NavMesh baked at runtime (animals can path)");
                r.Check(mgr.squirrelScale > 1.5f, "Squirrel scaled up 3-4x as requested", "scale " + mgr.squirrelScale);
            }

            int rabbits = Object.FindObjectsByType<Rabbit>(FindObjectsSortMode.None).Length;
            int deer = Object.FindObjectsByType<Deer>(FindObjectsSortMode.None).Length;
            int squirrels = Object.FindObjectsByType<Squirrel>(FindObjectsSortMode.None).Length;
            int snakes = Object.FindObjectsByType<Snake>(FindObjectsSortMode.None).Length;
            r.Note($"rabbits {rabbits}, deer {deer}, squirrels {squirrels}, snakes {snakes}");
            r.Check(rabbits >= 1, "Rabbits spawned", rabbits.ToString());
            r.Check(deer >= 1, "Deer spawned", deer.ToString());
            r.Check(squirrels >= 1, "Squirrels spawned", squirrels.ToString());

            // Proximity gating, not a frozen first frame: once the simulation is
            // running, a zone near camp legitimately materialises its snake.
            // What must never happen is every zone being pre-spawned - measured
            // against the zones that actually exist, not the number requested,
            // since TryFindSpot can fail and place fewer.
            if (mgr != null)
            {
                int zones = mgr.ActiveSnakeZoneCount;
                r.Note($"{zones} of {mgr.snakeZones} requested ambush zones found habitat");
                r.Check(zones >= 3, "Enough snake ambush zones found habitat", zones.ToString());
                r.Check(zones == 0 || snakes < zones, "Snakes are proximity-spawned, not all pre-placed",
                        $"{snakes} alive of {zones} zones");
            }

            // Assert the spawner's own record, not a zone re-derived from the
            // position. Animals wander, so live position says nothing about
            // placement; and re-deriving would fail a legitimate fallback (six
            // habitat attempts missed, placed anywhere walkable) as though the
            // placement rule were broken. AnimalManager knows which it was.
            foreach (var a in Object.FindObjectsByType<Animal>(FindObjectsSortMode.None))
                if (a is Rabbit || a is Deer || a is Squirrel || a is Fox || a is Bear)
                    r.Check(a.PlacementSatisfied, a.GetType().Name + " placed in its habitat",
                            "spawn zone " + a.SpawnZone);

            r.Section("Animals sit on the ground");
            foreach (var a in Object.FindObjectsByType<Animal>(FindObjectsSortMode.None))
            {
                float g = forest.HeightAt(a.transform.position.x, a.transform.position.z);
                float dy = a.transform.position.y - g;
                r.Check(dy > -0.4f && dy < 1.6f, a.GetType().Name + " grounded", $"{dy:F2} m above terrain");
            }

            r.Section("Player rig");
            var sprint = Object.FindFirstObjectByType<SprintController>();
            r.Check(sprint != null, "SprintController present");
            if (sprint != null)
            {
                r.Check(sprint.walkSpeed >= 11f, "Walk speed raised for a fast traverse", "walk " + sprint.walkSpeed);
                r.Check(sprint.runSpeed > sprint.walkSpeed, "Run is faster than walk", "run " + sprint.runSpeed);
            }

            var origin = Object.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            r.Check(origin != null, "XR Origin present");
            if (origin != null)
            {
                // The VR-correctness check that matters: room-scale, not seated.
                // Device mode pins the origin to the startup head pose and
                // ignores the real floor, so physical movement never maps 1:1.
                r.Check(origin.RequestedTrackingOriginMode ==
                            Unity.XR.CoreUtils.XROrigin.TrackingOriginMode.Floor,
                        "Room-scale: tracking origin is Floor, not seated Device",
                        origin.RequestedTrackingOriginMode.ToString());

                r.Check(origin.GetComponent<VrTrackingSetup>() != null,
                        "VrTrackingSetup verifies the granted origin mode at runtime");

                var head = origin.Camera != null ? origin.Camera.transform : origin.transform;
                float pg = forest.HeightAt(head.position.x, head.position.z);
                float eye = head.position.y - pg;
                r.Check(eye > 0.3f, "Player head is above the terrain (no fall-through)",
                        $"head y {head.position.y:F2}, ground {pg:F2}, eye height {eye:F2} m");

                // Eye height is deterministic exactly when NO head device is
                // attached: the editor's OpenXR runtime fails with
                // FORM_FACTOR_UNAVAILABLE, XROrigin never moves the offset, and
                // the rig's serialised 1.7 m is what the camera stands on - so
                // assert it there. With a real headset the value is whatever the
                // person's height is, so it is reported, not asserted.
                var headDevice = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head);
                if (!headDevice.isValid)
                    r.Check(eye > 1.4f && eye < 2.1f, "Flat-mode eye height is a standing 5-6 ft (serialised offset applied)",
                            $"{eye:F2} m ({eye * 3.281f:F1} ft)");
                else
                    r.Note($"head device present - eye height {eye:F2} m is the wearer's real height");

                float rigDrop = origin.transform.position.y -
                                forest.HeightAt(origin.transform.position.x, origin.transform.position.z);
                r.Check(rigDrop > -0.5f && rigDrop < 2.5f, "Rig floor sits on the terrain", $"{rigDrop:F2} m above ground");
            }

            var scout = Object.FindFirstObjectByType<ScoutCompanion>();
            r.Check(scout != null, "Scout companion present");
            if (scout != null)
            {
                // The complaint was a white ball permanently in view. A snapshot
                // cannot distinguish "stuck on" from the ~0.25 s fade-out after
                // the speech timer expires (the renderer follows alpha, the
                // timer does not), so this is reported rather than asserted.
                // The real check is behavioural and lives in the PlayMode suite:
                // Silence(), one LateUpdate, renderer must be off.
                var mr = scout.GetComponentsInChildren<MeshRenderer>(true);
                int visible = mr.Count(x => x.enabled);
                r.Note($"Scout: speaking={scout.IsSpeaking}, {visible} visible renderer(s) " +
                       "(behavioural hide test in ForagePlayModeTests)");
            }

            r.Section("Materials");
            var bad = renderers.Where(x => x.sharedMaterials.Any(m => m == null || m.shader == null ||
                                      m.shader.name == "Hidden/InternalErrorShader")).ToList();
            r.Check(bad.Count == 0, "No missing/error (magenta) materials in the forest",
                    bad.Count == 0 ? "" : string.Join(", ", bad.Take(5).Select(b => b.name)));

            return r;
        }
    }
}
