# Forage (Leafwise) — Technical Documentation
**Educational VR Survival Simulator for Meta Quest 3**
RMIT Mixed Reality · Development log & system reference · September 2–3, 2026
Branch: `forage-prototype` · Unity **6000.3.21f1** · URP · OpenXR + Meta Quest Support

---

## 1. Project Overview

Forage teaches real wilderness-survival decision-making inside a safe, low-poly VR forest: starting fire by hand-drill or flint, purifying water, identifying edible vs. deadly mushrooms, and reading wildlife behavior. Wrong choices hurt but never hard-kill (health floors at 5) — every mistake becomes a lesson delivered by fact cards, per the project pitch.

**Design pillars**
1. Education first — every mechanic mirrors a real survival protocol.
2. Short sessions (~10–15 min), small world (180 m × 180 m), no crafting sprawl.
3. Quest-3-first performance: everything is built to render standalone.

---

## 2. Architecture at a Glance

```
Assets/
├── Scenes/Forage.unity          ← generated, never hand-edited
├── Editor/Forage/
│   ├── ForageSceneBuilder.cs    ← builds the entire scene from code (menu: Forage ▸ Build Forage Scene)
│   └── SkillExporter.cs         ← MCP tooling bridge (skills export, plugin controls)
├── Scripts/Forage/
│   ├── Core/        GameManager, PlayerVitals, SpawnGuard, SprintController,
│   │                ScreenFeedback, Haptics, ProceduralAudio, AmbienceAndFeedback,
│   │                ForageAssets, ForageEvents
│   ├── Environment/ ForestGenerator, NatureFactory, LowPolyFactory,
│   │                ProceduralTextures, AmbientWindFx, PondLife
│   ├── Fire/        FirePit, FireDrill, FlintStone, FireVfx
│   ├── Water/       CookingPot
│   ├── Items/       SurvivalItem, ItemFactory, Mushroom
│   ├── Animals/     Animal (base), Rabbit, Snake (+SnakeSpecies), Squirrel,
│   │                AnimalFactory, AnimalManager (+ tester panel)
│   └── UI/          WristHud, GlanceHud, FactCard
├── Forage/Materials (45 generated .mat) · Forage/Textures (13 baked .png) · Forage/Shaders (FoliageWind)
└── FurrySquirrel/   ← the only external art asset (model, animator, textures)
```

**Key principle:** the scene is *generated*, not hand-authored. `ForageSceneBuilder` creates lighting, the XR rig, all systems and materials; `ForestGenerator` + `CampsiteBuilder` + `AnimalManager` build the world at runtime from a fixed seed (**20260902**), so the editor, the simulator and the Quest build always produce the identical forest.

---

## 3. Assets & Modules Used

### 3.1 External art assets
| Asset | Source | Used for | Notes |
|---|---|---|---|
| **Furry Squirrel** (Squirrel.fbx, Animations_Squirrel.controller, albedo/normal/mask textures) | User-provided Asset Store package | The two wandering/climbing squirrels | Its fur-shell shader is Quest-hostile, so a plain URP Lit material is rebuilt from its textures at spawn. Only the FBX, animator controller, URP material and 3 textures were copied in. |

Everything else — terrain, trees, rocks, mushrooms, items, snakes, rabbits, fish, crabs, all textures, all audio — is **generated procedurally in code**. No other Asset Store content.

### 3.2 Unity packages (project manifest)
| Package | Version | Role |
|---|---|---|
| com.unity.xr.interaction.toolkit | 3.5.1 | Grabbing, sockets, teleport, locomotion, XR Interaction Simulator |
| com.unity.xr.openxr (+ meta-openxr 2.5.1) | 1.17.1 | Quest 3 runtime; “Meta Quest Support” feature enabled for Android |
| com.unity.xr.hands / arfoundation / compositionlayers | 1.8.1 / 6.5.0 / 2.5.0 | Template stack (hands, passthrough-capable) |
| com.unity.render-pipelines.universal | 17.3.0 | URP rendering |
| com.unity.ai.navigation | (via ext.) | **NavMeshSurface runtime baking for all animal AI** |
| com.unity.probuilder | (via ext.) | Reserved for C7 shelter construction geometry |
| com.unity.ai.assistant | 2.19.0-pre.2 | Hosts the Unity↔Claude MCP relay used for all automated testing |
| com.ivanmurzak.unity.mcp + probuilder/navigation/animation/terrain extensions | 0.90 / 1.x | 108 Unity tool skills exported globally to `C:\Users\JAY\.claude\skills`; plugin auto-connect disabled (cloud login not used) |

---

## 4. World Generation (the “realistic dense forest” pass)

### 4.1 Terrain
- 180 m × 180 m smooth-shaded mesh, 110×110 grid, built from three octaves of Perlin noise (analytic `HeightField` — animals, props and the fall-guard all query the same function, no raycasts).
- Flat **camp clearing** (radius 9 m) at origin; **pond** (radius 8 m, ~1.4 m deep) at (24, 16).
- **Baked albedo** (1024²): moss under dense groves, dry grass in meadows, trampled dirt around camp, sand ring at the pond — driven by the same noise fields.
- **Ground detail layer**: a tiling 256² grass-blade texture in URP Lit’s detail slot (~2 m repeat) so the ground holds up close-up.
- Whole terrain is a `TeleportationArea` (Teleport interaction layer).

### 4.2 Vegetation & props (~1,900 placed, static-batched)
| Type | How it’s built | Count (attempt-based) |
|---|---|---|
| Broadleaf trees | Smooth bent trunk tube + branch stubs + 9–14 alpha-cutout leaf-cards in an ellipsoid crown | ~45% of ~350 trees |
| Pines | Tall trunk + 5–8 tiers of drooping needle-cards | ~35% |
| Birches | White lenticel-textured trunk + loose yellow-green cards | ~20% |
| Bushes / ferns / grass / flowers | Card clusters / frond cards / crossed blade-cards / stem+petal | 150 / 320 / 900 / 90 attempts |
| Rocks, stumps, fallen logs | Smooth jittered blobs & tubes with baked rock/bark textures | 60 / 22 / 26 attempts |

- Tree density follows a Perlin **density mask** → thick groves with natural clearings; ferns follow density, flowers prefer clearings.
- Trees sink 0.35 m so trunks never float on slopes.
- **13 procedural textures** are baked to PNG assets at scene-build time (bark, birch bark, 2 leaf clusters, birch leaves, 2 pine branches, grass blades, fern, rock, terrain albedo, ground detail, fly-agaric cap, single leaf).

### 4.3 Motion & atmosphere
- **Custom `Forage/FoliageWind` URP shader** (hand-written HLSL, SRP-batcher compatible, with shadow-caster pass): vertex wind sway + high-frequency flutter, masked by UV so bases stay anchored. Runs on GPU → free on Quest and survives static batching. Per-type tuning (grass sways hardest, pines subtlest).
- `AmbientWindFx`: drifting leaf particles + sunlit dust motes in a volume that follows the player.
- `PondLife`: **7 fish** cruising below the surface (tail-wag yaw, depth-clamped) and **3 crabs** scuttling sideways on the pond floor — visible through water made properly transparent (α 0.45).
- Late-afternoon directional light, trilight ambient, exponential fog (0.011).

**Deliberate engineering choice:** Unity’s Terrain system was *not* used — it is a well-known GPU cost on standalone Quest. The custom mesh + baked-texture approach is the standard mobile-VR practice and keeps identical visuals across editor and device.

---

## 5. Survival Mechanics

### 5.1 Player vitals (`PlayerVitals`) — the health-bar rules
| Stat | Decreases | Increases |
|---|---|---|
| **Hydration** | 4.5/min passively; ×2.5 while sick | Drinking from the pot: +45 |
| **Food (energy)** | 3/min passively | Safe mushroom: +30 |
| **Warmth** | 5/min at night away from fire | +25/min near a lit fire (radius 3.5→5.5 m with fire size) |
| **Health** | Drains while any stat is 0 or while sick; instant hits: poison −15, Eastern Brown bite −16, Python bite −6 | Recovers ~2.5/min when everything is fine. **Floor = 5: weakness, never death** (education-first) |
| **Sickness** | — | Dirty water (90 s), poisonous mushroom (120 s), venomous bite (75 s) |

### 5.2 Fire — two authentic ignition methods
**Shared state machine** (`FirePit`): Unlit → Ember (heat 60) → Burning (heat 100). Heat decays 7/s after a 1.2 s grace when you stop. Requires **tinder + at least one stick** dropped into the stone ring (trigger detects released items only).
- **Hand drill**: grab the drill stick, press its tip on the fireboard, scrub fast. Tip speed → heat (16/s at full speed); friction haptics scale with speed; smoke appears past 20% heat; too slow → Scout hint.
- **Flint & stone** (2 stones by the fireboard): strike them together **hard** (≥2.4 m/s relative impact) near the pit — spark burst + click + haptic; each good strike ≈ +40 heat, so **2–3 solid strikes** ignite. Weak strikes (≥1.3 m/s) spark faintly and hint “strike harder”.
- **Wet-wood lesson**: sticks near the pond are damp — heat ×0.25 and a Scout hint. Find dry wood.
- **Fuel scaling** (user request): each stick adds 150 s of burn; feeding a burning fire enlarges it — flame emission 35→110, size and speed up, light range and warmth radius grow (1→2.1× at 7+ sticks).

### 5.3 Water
Pot (grabbable, by the fire pit): dip **below the pond surface** to scoop murky water → hold within 1.3 m of the lit fire for **18 s** to boil (steam VFX, water disc turns clear) → raise to your mouth (< 0.3 m, hold 0.9 s) to drink. Boiled = +45 hydration, gulp sound, blue vignette flash. **Unboiled = sickness**, with a one-time Scout warning before you commit. Removing the pot mid-boil resets it to dirty.

### 5.4 Foraging — 26 mushrooms, 4 species
| Species | Count | Safe? | Teaching point |
|---|---|---|---|
| Field Mushroom | 8 | ✅ | brown gills, pleasant smell — but check for pale lookalikes |
| Chanterelle | 5 | ✅ | golden funnel, ridges not gills, apricot smell |
| Fly Agaric | 6 | ☠ | classic red-with-white-warts warning (textured cap) |
| **Death Cap** | 7 | ☠☠ | *deliberately innocent-looking pale cap* — the core lesson |

Pick one up → a **color-coded name tag** appears on it (red “DO NOT EAT” / green “edible”). Eat by bringing to your mouth: crunch sound + haptic; safe → +30 food, green flash, objective complete; poisonous → −15 HP, 120 s sickness, red flash, and a **fact card** explaining identification. Cards are compact, crisp (high pixel density), placed low-right of gaze, fade in/out — never a full-screen overlay.

---

## 6. Wildlife — species, counts, and behavioral conditions

All animals derive from `Animal`: NavMeshAgent movement over a **runtime-baked NavMeshSurface** (physics-collider geometry — tree trunks and rocks carve obstacles), horizontal-only player distance (head height ignored), awareness of **player speed** (smoothed head velocity from `GameManager.PlayerSpeed`), and a speed-synced procedural gait.

### 6.1 Rabbit — the trust teacher (3 spawned)
| Condition | Transition |
|---|---|
| Player within 7 m | Wander → **Watch** (freezes, faces you) |
| Player speed > 1.5 m/s at any point | → **Flee** (3.6 m/s, 12 m away) + hint |
| Player < 5 m and < 0.7 m/s for 3 s | → **Approach** (walks to you) |
| Reaches 1.6 m | → **Gift**: drops a dry stick, chirp, completes *wildlife* objective, fact card |
| After gifting | shy **Flee**, 90 s gift cooldown |

### 6.2 Snakes — three species, proximity-spawned
**7 hidden ambush zones** are seeded across the map. A snake **materializes when you come within 16 m** of its zone and despawns (20 s cooldown) when you leave beyond 38 m and it has calmed — encounters feel discovered, not staged.

| Species | Size | Venom | Temperament | Bite |
|---|---|---|---|---|
| Eastern Brown Snake | 1.35× | **Yes** | Aggressive | −16 HP + 75 s sickness |
| Carpet Python | 1.7× | No | Aggressive (defensive) | −6 HP |
| Green Tree Snake | 0.7× | No | **Shy — flees from you** | — |

**Aggressive encounter logic:** notice at 2.6 + 0.8×size m → **Alert**: rears its head, hisses (synthesized), controller rumble, Scout hint “freeze”.
- Move faster than 1.1 m/s for a cumulative 0.9 s → **Strike** (chases at 4.5–6.2 m/s, bites within 0.9 + 0.4×size m) → Leave.
- Hold still 4.5 s → it de-escalates, *wildlife* objective completes, and it leaves. Lesson delivered either way via fact card.

**Body/visuals:** 16 tapered segments in a distance-constrained follow chain (continuous through the rearing pose), banded procedural skin per species, eyes, flicking forked tongue (rate rises when alert), serpentine sway scaled by speed.

### 6.3 Squirrel — ambient life (2 spawned, Furry Squirrel model @ 0.5×)
Wanders between trees (2.2 m/s); every 15–35 s picks a registered tree within 14 m → runs to it → **climbs 2–3 m up the trunk** (nose-up against bark) → idles 4–8 s → climbs down → wanders on. Drives the asset’s own animator via its float speed parameter. ~350 trees are registered as climbable at startup.

### 6.4 Pond life
Fish (7) and crabs (3) as described in §4.3 — ambient, and groundwork for the fishing mechanic (C7).

---

## 7. Locomotion, Comfort & Safety

- **Sprint** (user request): hold **Left Shift** (simulator) or **click either thumbstick** (Quest). Walk 3 m/s, run 6 m/s — and running scares wildlife, tying movement into the education loop.
- **SpawnGuard** — three-layer anti-fall system born from a real playtest bug:
  1. Rig spawns 1 m above ground and settles (never starts inside the terrain collider).
  2. **Head-ground clamp**: head tracking has no collision, so if the camera would sink into a hillside (simulator translate *or* physically walking into a slope on Quest) the whole rig rides up smoothly.
  3. Fall catch: > 6 m below terrain, or stuck submerged > 2 s → reset to the last safe standing spot.
- Teleport across the entire terrain; XR Interaction Simulator auto-enabled in the editor for headset-free play.

---

## 8. UI / Feedback Systems

| System | Behavior |
|---|---|
| **Wrist watch** (`WristHud`) | ~10 cm translucent panel on the left forearm: 4 labeled bars + current objective. Detailed readout on demand. |
| **Glance strip** (`GlanceHud`) | Slim 4-bar strip floating low in view with **lazy follow** (VR-comfort safe, never head-locked). Auto-shows only when: any vital < 35, a vital just changed, you’re sick, or during the first 15 s. Fades away otherwise. |
| **Fact cards** (`FactCard`) | Compact educational cards (species facts, encounter lessons, objective completions), accent-colored good/bad, gaze-offset placement, fade in/out, one at a time. |
| **Mushroom tags** | World-space label on a held mushroom: red “DO NOT EAT” / green “edible”. |
| **ScreenFeedback vignette** | Radial edge tint: red flash on damage, green on eating, blue on drinking, sickly pulse while poisoned. |
| **Haptics** (`Haptics`) | Drill friction (speed-scaled), flint strikes, snake warning + bite (strong), consumption ticks, objective pulses. |
| **Audio** (`ProceduralAudio`) | **All clips synthesized in code** — no audio files: wind/leaf ambience bed, pond lapping (3D at pond), random bird chirps, state-driven fire crackle, snake hiss, flint click, objective chime (C-E-G arpeggio), drinking gulp, eating crunch, owl hoot (ready for night). |

---

## 9. Objectives & Game Loop (`GameManager`)

1. Explore the forest (auto-completes 14 m from camp)
2. Start a campfire (drill or flint)
3. Boil pond water and drink it safely
4. Eat a safe mushroom
5. Build the shelter *(C7)*
6. Observe wildlife without scaring it (rabbit gift **or** snake calm-freeze)
7. Survive until nightfall *(C6 day-night)*

Completion → chime + haptic + toast card; the wrist watch always shows the current objective. `ForageEvents` is the decoupled hint/signal bus that the Scout companion (C7) will voice — hints already fire everywhere (`fire-no-tinder`, `wood-damp`, `strike-harder`, `about-to-drink-dirty`, `snake-freeze`, `rabbit-scared`, …).

---

## 10. Development & Testing Methodology

All development is driven and verified through the **Unity MCP relay** (Unity AI Assistant package) from Claude Code — the editor is operated programmatically:

- **Compile/console gate**: every code batch is refreshed via `AssetDatabase.Refresh()` and checked for zero errors before proceeding.
- **Deterministic frame-stepping**: the editor throttles its player loop when unfocused, so behavior tests advance the simulation with `EditorApplication.Step()` — e.g. “step 60 frames near the snake, then jiggle the rig 0.04 m/frame for 300 frames” — giving exactly reproducible encounter tests.
- **Automated behavior assertions** (all passing):
  - 980 m full-map traversal — zero terrain fall-throughs; head buried 2 m in a hilltop surfaces automatically.
  - Fire: fuel acceptance, 3×40-heat flint path → Burning; wet-wood damping; wood-feeding growth (1→3 sticks).
  - Water: scoop → boil → clean → +hydration; boil interruption resets.
  - Mushrooms: species distribution (8/5/6/7), safe eat (+food, objective, card), poison eat (−HP, sickness).
  - Snake: Alert on approach → bite lands (HP 100→88) when moving; calm-freeze → leaves. Proximity spawn/despawn lifecycle observed live.
  - Rabbit: settle → Watch → Approach → **gift stick spawned** → Flee; wildlife objective set.
  - Squirrel: full ToTree → ClimbUp (+2.8 m) → TreeIdle → ClimbDown → Wander cycle.
  - Sprint: provider speed 3 → 6 under the sprint flag.
- **Audio verification**: every AudioSource checked for `isPlaying`/time advance and **non-silent waveform RMS** sampled from the generated clips (editor mutes audio while unfocused; audible confirmation is the in-headset/user test).
- **Visual verification**: RenderTexture screenshots from scripted cameras at every milestone (forest, pond, campfire, animals) reviewed frame by frame; several bugs (floating trees, disconnected snake body, magenta squirrel material, forest-dragging bug) were caught *from the screenshots*.
- **Animal Tester panel** (editor-only, never ships): spawn any animal near the player and force any state — Rabbit Approach/Flee, Snake Alert/Strike/Leave, Squirrel Climb.
- **Checkpoint workflow**: every feature lands as a tested commit on `forage-prototype`; the team merges each into `main` after human testing. To merge: `git checkout main && git merge forage-prototype && git push`.

### Commit history (this effort)
| Commit | Content |
|---|---|
| `b4ee9a3` | C1 — generated world, XR rig, vitals + HUD, objectives |
| `1c391ef`, `e051650` | C1 fixes — spawn settle, teleport area, head-ground clamp, gentler hills |
| `e248d4e` | C2 — fire: gatherables, pit, hand-drill, VFX, wet-wood lesson |
| `2c73036` | C3 — water: scoop/boil/drink, sickness path |
| `9144344` | C4 — mushroom foraging, fact cards |
| `cbad98a` | Environment overhaul — textures, wind shader, pond life, flint ignition, fire scaling, subtle HUD, crisp cards |
| `01d2423` | Tooling — MCP extensions (ProBuilder/Navigation/Animation/Terrain), 108 skills exported globally |
| `7abdd24` | C5 — rabbits, 3 snake species + proximity zones, squirrels, full audio/haptics/vignette layer, sprint |
| `5579a72` | HUD — lazy-follow glance strip + wrist watch placement |

---

## 11. Known Limitations & Next Steps

| Area | Status |
|---|---|
| C6 | Deer (quiet approach), fox (steals unsecured food), bear (never run), ambient birds/owl, **day→night cycle** (gives Warmth real stakes, completes “survive to nightfall”) |
| C7 | Shelter building (**ProBuilder** geometry), visible **Scout companion** voicing the existing hint bus, cooking mushrooms **and fish** on the fire, fishing, berry bushes, session summary |
| C8 | Quest 3 APK (toolchain installed: Android SDK/NDK/OpenJDK for 6000.3.21f1), on-device profiling — ~11 k renderers may need foliage-card trimming/culling; Burst fallback `-burst-disable-compilation` prepared |
| Audio | Structurally verified; audible pass needs a focused editor / headset |
| ai-game.dev skills | 108 skill files exported globally; their CLI execution requires an interactive `unity-mcp-cli login` (cloud account) — all equivalent operations run through the Unity relay instead |
| Editor stability | The MCP plugin’s cloud reconnect could deadlock domain reloads — auto-connect disabled (`Forage ▸ Disable MCP Plugin Auto-Connect`) |

---

*Generated as part of the checkpoint-driven build. The scene can always be rebuilt from scratch via **Forage ▸ Build Forage Scene** — code is the single source of truth.*
