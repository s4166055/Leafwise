# LeafWise - Educational Survival VR Project
​
**LeafWise** is a **survival simulator** where you learn how to build a campfire, get water, forage safely, and hide from curious animals in the **safety of virtual reality** with a helpful guide. 
It teaches real wilderness-survival decision-making inside a safe, low-poly VR forest: starting fire by hand-drill or flint, purifying water, identifying edible vs. deadly mushrooms, and reading wildlife behavior. Wrong choices hurt but never hard-kill — every mistake becomes a lesson delivered by fact cards, per the project pitch.

## Features
- Fire Starting Mechanic
- Cleaning Water Mechanic 
- Mushroom Foraging 
- Wandering Animals 
- Dangerous Snake 

## Project Setup (TBC)
- Unity Version: 6000.3.21f1
- Target XR device: Meta Quest 3
- SDK and package names:
  - Android SDK​
  - Meta XR All-in-One SDK​
- AI Packages:
  - Unity-MCP by Ivan Murzuk​
- Required modules and build platform;
- Installation and project-opening steps:
  1. Clone the Leafwise repository into Unity
- How to build, deploy, and run on the device;
- Known issues and troubleshooting notes.

## Architecture at a Glance
 
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
 
**Key principle:** the scene is *generated*, not hand-authored. `ForageSceneBuilder` creates lighting, the XR rig, all systems and materials; `ForestGenerator` + `CampsiteBuilder` + `AnimalManager` build the world at runtime from a fixed seed, so the editor, the simulator and the Quest build always produce the identical forest.


 
## Assets & Modules Used
 
### Unity packages (project manifest)
| Package | Version | Role |
|---|---|---|
| com.unity.xr.interaction.toolkit | 3.5.1 | Grabbing, sockets, teleport, locomotion, XR Interaction Simulator |
| com.unity.xr.openxr (+ meta-openxr 2.5.1) | 1.17.1 | Quest 3 runtime; "Meta Quest Support" feature enabled for Android |
| com.unity.xr.hands / arfoundation / compositionlayers | 1.8.1 / 6.5.0 / 2.5.0 | Template stack (hands, passthrough-capable) |
| com.unity.render-pipelines.universal | 17.3.0 | URP rendering |
| com.unity.ai.navigation | (via ext.) | NavMeshSurface runtime baking for animal AI |
| com.unity.probuilder | (via ext.) | Reserved for shelter construction geometry |
| com.unity.ai.assistant | 2.19.0-pre.2 | Hosts the Unity↔Claude MCP relay used for automated testing |
| com.ivanmurzak.unity.mcp + probuilder/navigation/animation/terrain extensions | 0.90 / 1.x | Unity tool skills for MCP-driven development |
 
---
 
## Current Status
 
The project is in its **prototyping / sandbox phase**. Core systems exist but are not yet reliable or complete.
 
| Feature | Status |
|---|---|
| **Terrain / Environment** | Forest environment built from free Unity assets, with campfire area, pond, and basic animal interactions |
| **Fire Starting** | Campfire object exists; player can hold and throw two sticks into it, but fire does **not** ignite yet. Drill-motion mechanic not implemented |
| **Clean Water** | Pond exists in the environment; no clean/dirty water logic implemented yet |
| **Mushroom Foraging** | Safe/dangerous mushrooms generate with a colour indicator (green = safe, red = poisonous) and can be picked up, but not eaten yet |
| **Animals** | Rabbit, snake, squirrel, deer, and fox assets are implemented and spawnable via the sandbox "Animal Tester" panel — but they all currently share the same generic behaviour (approach the player, then flee) rather than species-specific logic |
| **AI Helper ("Scout")** | Basic implementation — responds when animals retreat from or attack the player |

### Known Issues
- Project is currently in a buggy state
- Species-specific animal behaviour (e.g. different reactions per animal, snake bite/venom, rabbit gift mechanic) not yet implemented — all animals currently run the same approach/flee pattern
- Animal testing is done via a temporary sandbox panel, intended to be removed for the final prototype
- Meta Quest 3 headset tracking is not working; controller input works as a fallback
- Deployment requires manually pointing to the correct project directory rather than the test one
