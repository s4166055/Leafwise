# Forage (Leafwise) — Technical Documentation
**Educational VR Survival Simulator for Meta Quest 3**
RMIT Mixed Reality · Development log & system reference · September 2–6, 2026
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
- **144 m × 144 m** smooth-shaded mesh, 110×110 grid, built from three octaves of Perlin noise (analytic `HeightField` — animals, props and the fall-guard all query the same function, no raycasts).
- Flat **camp clearing** (radius 9 m) at origin; **pond** (radius 8 m, ~1.4 m deep) at (24, 16).
- **The clearing is a plateau, not a pit.** The camp blends to `CampLevel` — the terrain's own height at the origin (≈3.15 m) — rather than being multiplied toward y=0, which previously sank the whole camp ~3 m below the surrounding forest. Pond water height and the pot's scooping threshold are both derived from the generated pond, so nothing is pinned to a hard-coded height.
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

### 4.4 Habitat zones — where things are found

The world is divided into five zones derived from the terrain's density field, so the forest reads as an ecosystem and *learning where to look* becomes part of the lesson (`Habitat.cs`).

| Zone | Where it is | What lives there |
|---|---|---|
| **Camp** | Inside the 9 m clearing | Kept clear of wildlife |
| **Meadow** | Open, low tree density | Field Mushrooms · deer graze · brown snakes bask on the edges · flowers |
| **Woodland** | Ordinary forest | Fly Agaric · squirrels · foxes · pythons |
| **Deep Woods** | Dense canopy, >28 m from camp | Death Caps in the shade · the bear roams here |
| **Waterside** | Within ~6 m of the pond | Chanterelles on damp margins · damp firewood · green tree snakes |

Species are placed by searching only the zones that suit them; waterside species are searched around the pond rather than radially from camp.

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

### 5.2 HOW TO START A FIRE — step by step

**1 · Gather tinder.** Straw-coloured **tinder bundles** lie around the camp clearing (4 of them). Point at one, squeeze **grip** to pick it up, and drop it inside the stone ring. *Without tinder nothing will ever catch — Scout will tell you so.*

**2 · Gather dry wood.** Pick up **1–2 sticks** and drop them in the ring too. ⚠️ **Sticks lying near the pond are damp** — they only deliver a quarter of the heat. Damp fuel no longer ruins the fire permanently: the penalty scales with the wet-to-dry ratio, so piling on dry wood rescues it.

**3 · Make a spark — pick either method.**

| Method | What to do |
|---|---|
| **Flint & stone** *(fastest)* | Grab the **two flint stones** beside the fireboard, one in each hand, and strike them together **hard** directly over the pit. A solid hit (≥ 2.4 m/s) throws real sparks; **2–3 good strikes** ignite the tinder. Weak taps only spark faintly and Scout says "strike harder". |
| **Hand drill** *(traditional)* | Grab the **drill stick**, press its tip onto the **fireboard**, and scrub back and forth **fast**. Heat builds with tip speed (up to 16/s) and the controller rumbles with the friction. Stop and it cools. |

**4 · Watch the stages.** Heat 60 → **embers** (glow, heavy smoke). Heat 100 → **flames**, and the *Start a campfire* objective completes with a chime.

**5 · Keep it alive.** Each stick burns ~150 s. **Add more wood while it's burning** and the fire physically grows — flame density rises from 95 to 260 particles, the light reaches 8 → 13 m, and the warmth radius widens 3.8 → 5.2 m. Let the fuel run out and it dies.

**What the fire gives you:** warmth at night, boiling water safe to drink, cooking for mushrooms and fish, and it keeps bears away from camp.

### 5.3 Fire — two authentic ignition methods
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

## 6. HOW TO REACT TO EACH ANIMAL — the survival rules

Every creature reacts to **how fast you move** and **how close you get**. The single rule underneath all of them: *calm, slow movement is safe; running is what causes trouble.* Running (Left Shift / thumbstick click) scares wildlife on purpose.

| Animal | Found in | It notices you at | ✅ Do this | ❌ Not this |
|---|---|---|---|---|
| **Rabbit** ×3 | Meadow, woodland | 7 m | Stand still or creep in **under 0.7 m/s**. Hold that for 3 s and it approaches and **drops you dry firewood** | Move faster than 1.5 m/s — it bolts 12 m away |
| **Squirrel** ×2 | Woodland, deep woods | 6 m | Watch from a distance — it bounds between trees | Walk within 6 m: it sprints for the nearest trunk and runs up it |
| **Deer** ×2 | Meadows | 10 m | Watch quietly from **4–9 m** for 6 s, moving under 0.8 m/s → wildlife lesson complete | Rush it or come inside 3.5 m — it bolts at 7 m/s |
| **Snake** (3 species, 7 ambush zones) | By species — see below | 2.6–4 m | **FREEZE.** It rears and hisses; hold still 4.5 s and it calms and leaves | Keep moving > 1.1 m/s for ~1 s and it **strikes** |
| **Fox** ×1 | Woodland, meadow | Hunts food, not you | Keep mushrooms and fish **near the lit fire or inside the shelter** | Leave food lying on open ground — it's stolen |
| **Bear** ×1 | Deep woods | 13 m | **Back away slowly** while facing it (under 1.2 m/s) for 4 s. A **burning campfire keeps it out of camp entirely** | **Never run.** Running within 11 m triggers a charge: −20 HP |

**The three snakes** — spawn from hidden zones as you wander near (16 m), despawn when you leave (38 m):

| Species | Habitat | Venom | Temperament | Bite |
|---|---|---|---|---|
| Eastern Brown Snake | meadow edges | **Yes** | Aggressive | −16 HP + 75 s sickness |
| Carpet Python | deep woods | No | Defensive | −6 HP |
| Green Tree Snake | pond margins | No | **Shy — flees from you** | — |

**Mushrooms react too.** Walk within ~2.6 m of any mushroom and it gently lifts and sways to catch your eye, and Scout names the species *and the habitat it grows in* — so you learn to read the ground, not just the colour. Picking one up shows a red **DO NOT EAT** or green **edible** tag.

---

## 7. Wildlife — species, counts, and behavioral conditions

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

- **Sprint:** hold **Left Shift** (simulator) or **click either thumbstick** (Quest). **Walk 12 m/s, run 22 m/s** — tripled from the original pace, and with the world 20% smaller a crossing takes ~12 s rather than ~45 s. Running still scares wildlife, tying movement into the education loop.
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
| **Scout companion** | A small firefly that stays **completely hidden while you explore** and only materialises beside a speech bubble when it has something to say — it was previously a large glowing orb parked in the middle of the view. Press **H** (or a controller face button) to ask about the current objective. |
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
| *(history rewrite)* | Split into one commit per feature (23 `feat:` commits); the previous checkpoint history is preserved on `backup/checkpoint-history` |
| `326d12c` | Fix — damp-wood penalty scales with wet/dry ratio instead of latching forever |
| `1532e1b` | **Fix — camp and fire pit sat in a crater**; the clearing now flattens to the natural ground level. World 20% smaller (180 → 144 m) |
| `1eacb19` | Movement 3× faster (walk 12 m/s, run 22 m/s) |
| `5305ea6` | Warmer, denser fire — yellow-white core through orange |
| `3776917` | Realistic rabbit — crouched hare build, haunches, lined ears, scut |
| `31c7eaf` | **Fix — deer head was buried in the ground**; neck rig rebuilt on a shoulder pivot |
| `b901bf8` | Bear anatomy — shoulder hump, jointed limbs, broad clawed paws |
| `6d7f596` | Squirrel bounding scamper gait + startle-and-climb flee |
| `fbd62ef` | Habitat zones — each species lives where it really would |
| `8aebb48` | Mushrooms react as you approach; Scout names the species and its habitat |
| `a057fab` | **Fix — Scout orb no longer floats in view as a white ball** |
| `1cbd852` | **Fix — repaired the manifest so the project can open at all** (duplicate JSON keys; a module that only exists in Unity 6000.5) |
| `6edad70` | **C8 — one-command Quest 3 XR setup, validation and APK build** |
| `a481fa4` | C8 — automated forest self-test (52 assertions, runs itself in play mode) |
| `fb15fa5` | C8 — [SIDELOAD.md](SIDELOAD.md), the headset-day install guide |
| `c2582df` | Ignore a blank project Unity Hub scaffolded inside the repo |
| `14ce337` | C8 — serialised Quest player + OpenXR settings |

---

## 11. Known Limitations & Next Steps

| Area | Status |
|---|---|
| C6 | Deer (quiet approach), fox (steals unsecured food), bear (never run), ambient birds/owl, **day→night cycle** (gives Warmth real stakes, completes “survive to nightfall”) |
| C7 | Shelter building (**ProBuilder** geometry), visible **Scout companion** voicing the existing hint bus, cooking mushrooms **and fish** on the fire, fishing, berry bushes, session summary |
| C8 | ✅ **Complete** — APK built and verified (§12). Outstanding: **on-device profiling**, which genuinely cannot be done until the headset arrives. ~10.4 k renderers is the likely bottleneck; the tuning order and the Burst `-burst-disable-compilation` fallback are written up in [SIDELOAD.md](SIDELOAD.md) |
| Audio | Structurally verified; audible pass needs a focused editor / headset |
| ai-game.dev skills | 108 skill files exported globally; their CLI execution requires an interactive `unity-mcp-cli login` (cloud account) — all equivalent operations run through the Unity relay instead |
| Editor stability | The MCP plugin’s cloud reconnect could deadlock domain reloads — auto-connect disabled (`Forage ▸ Disable MCP Plugin Auto-Connect`) |

---

## 12. C8 — Quest 3 build pipeline

### 12.1 Settings as code, not as clicks

Every Quest setting is applied by [QuestBuild.cs](Assets/Editor/Forage/QuestBuild.cs) rather than checked into the inspector by hand. The reason is practical: inspector state lives in binary-ish project assets that nobody reviews, and it silently differs between machines. In code it appears in a diff, and any teammate reproduces it by running one menu item.

**Menu: Forage ▸ Quest**

| Item | Does |
|---|---|
| `1 - Configure XR and Player Settings` | Applies the full Quest configuration below |
| `2 - Validate Build Readiness` | 12 pre-flight checks, prints a PASS/FAIL table |
| `3 - Build APK` | Validates, then builds `Builds/Leafwise.apk` |
| `4 - Build and Run on Headset` | Same, then installs and launches over USB |

| Setting | Value | Why |
|---|---|---|
| Scripting backend | IL2CPP | Mono is not supported on Android/Quest |
| Architecture | **ARM64 only** | Quest 3 is 64-bit; shipping armeabi-v7a just inflates the APK |
| Graphics API | **Vulkan** | Meta's recommended path on Quest 3 |
| Stereo rendering | **Single-pass instanced** (multi-view) | Renders both eyes in one pass — the single largest VR GPU saving |
| Colour space | Linear | Correct lighting; gamma looks washed out |
| Min SDK | 32 | Meta store floor for Quest 3 |
| Bundle id | `com.rmit.forage` | Stable app identity so `adb install -r` updates in place |
| XR loader | OpenXR (Android), assigned via `XRPackageMetadataStore` | The modern path; the legacy Oculus plugin is deprecated |
| OpenXR features | Meta Quest Support + Touch Plus / Touch Pro / Oculus Touch profiles | **Without an interaction profile the controllers report no input on device** — a silent failure that only shows up in the headset |

### 12.2 Validation gate

Step 2 exists so a misconfiguration fails in seconds instead of thirty minutes into IL2CPP. Result:

```
PASS  Forage scene exists                     PASS  Vulkan is the primary graphics API
PASS  Forage scene is in build settings       PASS  OpenXR loader active for Android
PASS  IL2CPP scripting backend                PASS  Meta Quest OpenXR feature enabled
PASS  ARM64 only                              PASS  A controller interaction profile is enabled
PASS  minSdkVersion 32 or higher              PASS  Android Build Support installed
PASS  Linear colour space
PASS  Multi-view (single pass instanced)      READY TO BUILD
```

### 12.3 Build result

```
[Forage] BUILD SUCCEEDED -> Builds\Leafwise.apk  (1554.5 MB in 29.6 min)
```

86 MB on disk (1554.5 MB is Unity's uncompressed total). Verified by inspecting the APK as a zip archive — the build "succeeding" is not by itself proof it will run on a Quest:

| Check | Result |
|---|---|
| Native ABIs present | **`arm64-v8a` only** — no wasted 32-bit slice |
| `libil2cpp.so` | present — IL2CPP genuinely used |
| `libopenxr_loader.so`, `libUnityOpenXR.so`, `libUnityOpenXRHands.so` | present — XR runtime shipped |
| Package id in manifest | `com.rmit.forage` |
| Manifest targets | Oculus / HorizonOS |
| Entries / arm64 libs | 856 / 22 |

Install instructions: [SIDELOAD.md](SIDELOAD.md).

### 12.4 Automated forest verification

The world is generated in `ForestGenerator.Awake()`, so it exists only in play mode and cannot be checked from the saved scene. [ForageSelfTest.cs](Assets/Editor/Forage/ForageSelfTest.cs) drives the editor through *open scene → enter play → settle → assert → exit play* and writes a report. Its phase lives in `SessionState` so it survives the domain reloads play mode causes, and dropping a `Temp/forage-selftest.request` file starts it from outside the editor.

**Result: 51 of 52 assertions passed on the first run.** The single failure was a defect in the test, not the game — it measured `SprintController`'s transform, which sits on the systems container at the origin, rather than the XR rig. Corrected to measure the rig and camera.

Representative measured values:

| Assertion | Measured |
|---|---|
| Camp is not a crater | camp 3.15 m vs forest ring at 20 m 2.95 m — **delta 0.20 m** |
| Fire pit rests on ground | pit y 3.17, ground 3.17 — **delta 0.00 m** |
| Forest density | **10,426** mesh renderers, 6,638 tree-ish |
| Wind coverage | **9,269** renderers swaying, 7 materials on `Forage/FoliageWind` |
| Water transparency | alpha **0.45**, 35 fish/crabs below the surface |
| Habitat zones | all five present; origin classifies as `Camp` |
| Animal placement | deer → `Meadow`, squirrels → `Woodland`; snakes **0 alive at start** (proximity-spawned) |
| Animals grounded | every animal within **0.02–0.15 m** of the terrain |
| Materials | **no** missing or error (magenta) materials |

The assertions deliberately pin the bugs we have actually shipped before — camp in a crater, fire pit in a hole, deer buried in the ground, magenta materials, player falling through terrain — so a future change that reintroduces one is caught by a test rather than by eye.

### 12.5 Three blockers fixed to get here

Worth recording, because they were all environmental rather than gameplay bugs:

1. **`packages-lock.json` was not valid JSON** — duplicate keys (`com.unity.test-framework`, `com.unity.nuget.newtonsoft-json`). Unity exited with code 1 and loaded no packages at all.
2. **`com.unity.modules.physicscore2d` does not exist in 6000.3.21f1** — it ships with Unity 6000.5 and arrived with the merged `environment` branch. The GUI tolerated it; batch mode called it fatal.
3. **Unity Hub had never been told about Leafwise.** It only knew `My project`, which is why the editor kept opening the blank blue sample scene. Fixed by registering `E:\unity\Leafwise` in the Hub — use **Add ▸ Add project from disk**, never *New project* (which scaffolds a fresh empty project, as it did once into this very folder).

---

## 13. Headset findings and fixes (first on-device test)

The first real Quest 3 session produced three reports. All three were genuine, and the first was the most serious: the game was only playable with the joystick, and moving your head did not give the VR experience it should.

### 13.1 The head-tracking report — root cause

The rig was saved with a **seated** tracking origin:

```
m_RequestedTrackingOriginMode: 1   ← Device
m_CameraYOffset: 1.7
```

`Device` is the 3DOF origin. It pins the tracking origin to wherever the head happened to be at app start, ignores the real floor, and therefore needs a *faked* eye height — which is exactly what that 1.7 m offset was doing. Physical movement is measured from an arbitrary point rather than from the room, so stepping, leaning and crouching never map 1:1 to the view, and locomotion collapses onto the joystick.

The rig now requests **Floor** (stage space), the correct origin for a standing room-scale title: the headset reports true head height and position, so walking, leaning, crouching and turning drive the view directly. The faked offset is removed, since the runtime supplies real height and any offset of ours would stack on top of it.

Requesting Floor is not a guarantee — a headset with no room boundary, or one in a stationary profile, can hand back Device. [VrTrackingSetup.cs](Assets/Scripts/Forage/Core/VrTrackingSetup.cs) checks what was actually granted and applies a seated eye height *only* if Floor was refused, logging every branch:

```
[Forage] VR: supported tracking origin modes = Device, Floor
[Forage] VR: tracking origin mode in use = Floor
[Forage] VR: ROOM-SCALE ACTIVE — physically walking, leaning and crouching move the view.
[Forage] VR head check: device valid=True tracked=True | moved 0.184 m, turned 27.3 deg over 2 s
```

That last line exists so "head tracking does nothing" can be confirmed or ruled out from `adb logcat -s Unity:V` rather than from feel.

**Ruled out with evidence** before changing anything, so the record is clear: the XR Interaction Simulator was *not* shipping in the build (`m_AutomaticallyInstantiateInEditorOnly: 1`); the Android manifest is correctly an immersive VR app (`com.oculus.intent.category.VR`, `supportedDevices: quest2|questpro|quest3|quest3s`, `vr.headtracking` — the absent `vr_only` key is the obsolete Gear-VR-era declaration); `TrackedPoseDriver` is present on the rig camera; movement was already head-relative (`forwardSource = Main Camera`); and no second camera was rendering over the XR one.

`SpawnGuard`'s head clearance also had to drop from **1.5 m to 0.25 m**. At eye height it would shove the rig upward every frame the player crouched — fighting them and drifting the rig skyward — because under real tracking a low head is a legitimate pose, not a fall-through.

### 13.2 The movement-speed report — the setting was never the limiter

Travel felt slow despite a 12 m/s setting, because the `CharacterController` kept its defaults: **45° slope limit and a 0.3 m step offset**. On hilly ground littered with roots, rocks and fallen logs the player snagged constantly, so real travel was a fraction of the configured speed. Widened to **60° and 0.6 m** — that is the fix that makes the forest feel crossable.

Speeds raised on top of it: **walk 16 m/s, run 30 m/s**. `SprintController` now drives *every* `ContinuousMoveProvider` on the rig instead of the first one `FindFirstObjectByType` happened to return, and logs what it applied — a rig carrying two providers would otherwise leave one at its prefab default of 2.5 m/s, making the felt pace depend on which one drove the player.

### 13.3 The animal-fidelity report

The squirrel looked well defined because it is a real model with albedo and normal maps. Every procedural mammal was a flat `_BaseColor` on a smooth blob: one evenly lit surface with no high-frequency detail, which is precisely why they read as plasticine beside it.

`AnimalFactory.FurMaterial` now bakes a coat per species — an albedo of directional strands over a darker undercoat, plus a **normal map** derived from the same strand height field. The normal map is the part that matters; per-pixel relief is what lets the eye resolve a surface as hair. Strands run along V because `SmoothBlob` lays out spherical UVs, so fur lies along the body instead of swirling around it.

| Species | Strand density | Character |
|---|---|---|
| Bear | 34 | long, shaggy, strong relief — seen closest, so it gains the most |
| Fox | 46 | medium length, slightly glossy |
| Deer | 68 | short, dense, lies flat |
| Rabbit | 52 | the shared mid/light coat |

Eyes gained real specular response; matte spheres for eyes are one of the strongest toy signals on an otherwise decent model. Body and head blobs on the bear, fox and rabbit went from subdivision 1 to 2 so silhouettes read smoothly.

Checked by rendering the rabbit and coat swatches through an offscreen camera and looking at the image, not by assuming. The first attempt leaned on a low-frequency clump term and looked like wet clay; the noise is now weighted toward its finer octaves.

### 13.4 The test harness was reading a frozen frame

Worth recording as a methodology fix. An unfocused editor throttles — often halts — the player loop, so `Awake` and `Start` ran but nothing after the first `yield` did: coroutines, `Update` and NavMesh settling all stalled. That is how `VrTrackingSetup` could log its first line and then appear to do nothing, with no hint why. The settle phase now drives `EditorApplication.Step()`, making the wait real whether or not the window has focus.

Stepping immediately exposed three assertions that were testing the frozen frame rather than the design, all of which had been passing for the wrong reason:

| Assertion | Why it was wrong | Now |
|---|---|---|
| `snakes == 0` at start | proximity spawning genuinely fires for a zone near camp | `alive < zones` — not all pre-placed |
| no visible Scout renderers | Scout actually speaks its opening hint | tied to `ScoutCompanion.IsSpeaking` |
| habitat by live position | animals wander; a squirrel had walked to the pond | `Animal.SpawnZone`, recorded at placement |

**56 assertions, all passing** against a stepping simulation.

### 13.5 Build note

The APK build failed four times before succeeding, and none of it was code: a security product on this machine (`Reason Cybersecurity`, which is why Defender reports itself disabled) intermittently denies *execution* of the NDK linker `ld.lld.exe` under build load. Run manually it works fine (`LLD 18.0.3`), so the error never means the NDK is broken. Retrying is the fix — IL2CPP output is cached, so a retry links in about a minute. Clearing `Library/Bee` and `-burst-disable-compilation` both did not help. The permanent fix is an antivirus exclusion for `C:\Program Files\Unity` and `E:\unity\Leafwise`.

---

*Generated as part of the checkpoint-driven build. The scene can always be rebuilt from scratch via **Forage ▸ Build Forage Scene** — code is the single source of truth.*
