# Forage (Leafwise) — Setup Guide
**Getting from a fresh Unity install to playing the game, with nothing broken.**

Follow these in order. Steps 1–3 are the ones people skip and then hit problems.

---

## 1. Install the exact Unity version

The project is pinned to **Unity 6000.3.21f1**. A different version will silently
re-import and re-serialize assets, which causes broken references and merge noise
for everyone else.

1. Install **Unity Hub** → https://unity.com/download
2. In Hub: **Installs ▸ Install Editor ▸ Archive ▸ download archive**, pick
   **6000.3.21f1**. (Direct: `unityhub://6000.3.21f1/c02631ffc030`)
3. Tick these modules during install:
   - **Android Build Support** — *only if you will build the Quest APK*
     - plus its sub-items **Android SDK & NDK Tools** and **OpenJDK**
   - Nothing else is required to play and test in the editor.

> Testing in the editor needs **no headset**. The XR Interaction Simulator is
> already enabled in the project.

---

## 2. Install Git LFS — do this BEFORE cloning

The repo stores **342 binary assets** (models, textures, audio, the squirrel FBX)
in Git LFS. Clone without LFS and those files arrive as 130-byte text stubs;
Unity then shows pink materials, missing meshes and import errors that look like
project corruption but are just missing files.

```bash
git lfs install
```

Verify: `git lfs version` should print a version.

---

## 3. Clone the repository

```bash
git clone https://github.com/s4166055/Leafwise.git
cd Leafwise
git checkout <branch your team is using>
git lfs pull        # ensures every binary is fetched
```

Sanity check — this must print a real size, not ~130 bytes:

```bash
ls -l Assets/FurrySquirrel/Meshes/Squirrel.fbx
```

Expect roughly **1.7 MB**. If it is 130 bytes, LFS did not run: go back to step 2,
then `git lfs pull`.

---

## 4. Open the project — the two most common mistakes

1. In Unity Hub: **Add ▸ Add project from disk**, and select the folder you just
   cloned. **Make sure the path is the clone, not any other Unity project on your
   machine.** Opening the wrong project is the single most common cause of
   "where is the forest?"
2. The editor version shown next to the project must read **6000.3.21f1**.

**First open takes 10–25 minutes.** Unity is importing every asset and building
the `Library/` folder from scratch (`Library/` is correctly excluded from git, so
it is never shared). The window may say *"Not Responding"* — that is normal for a
large import. **Do not force-quit it**; killing Unity mid-import corrupts the
Library and you start over.

---

## 5. Open the right scene

Unity does not remember which scene to open on a fresh clone (that record lives in
the ignored `Library/` folder), so it usually opens the wrong one.

**Open `Assets/Scenes/Forage.unity`.**

- `SampleScene.unity` = the blue Unity VR template. Not our game.
- `BasicScene.unity` = template leftover. Not our game.

Press **Play**. You should be standing in a forest clearing at eye height with a
stone fire ring, a fireboard, a pot and flint stones nearby.

---

## 6. If the scene ever looks wrong

The scene is **generated from code**, so you can always rebuild it from scratch:

> **Forage ▸ Build Forage Scene**

This recreates lighting, the XR rig, all systems and materials deterministically.
Code is the single source of truth — never hand-edit the scene and commit it.

---

## Controls (editor / simulator)

| Action | Input |
|---|---|
| Look | hold **right mouse** + move |
| Move | **W A S D** |
| Run | hold **Left Shift** |
| Switch hand control | **T** / **Y** (head / left / right) |
| Grab | trigger/grip on the simulated controller (see the on-screen simulator menu) |
| Ask Scout for a hint | **H** |

On a Quest: thumbstick to move, **click a thumbstick** to run, grip to grab.

---

## Known console messages you can safely ignore

The project currently includes developer tooling (the Unity AI Assistant package
and four `com.ivanmurzak.unity.mcp.*` packages) used to drive the editor
programmatically during development. On a machine without those cloud accounts
they log red errors on startup:

- `Authorization failed. Token may be missing, invalid, or revoked`
- `Version handshake failed: No response from server`
- `Error reason is 'NoSubscription'`
- `Could not resolve entitlement…`

**These do not affect the game.** To silence them:

> **Forage ▸ Disable MCP Plugin Auto-Connect**

If your team does not need that tooling, it is cleaner to remove the five packages
from `Packages/manifest.json` along with the `openupm` scoped registry — see the
team lead before doing so.

Also harmless:
- `Could not find a device that supports eye tracking` — expected without a headset.
- `Account API did not become accessible` — Unity cloud services, unused here.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Pink/magenta materials, missing meshes | Git LFS not installed before cloning | `git lfs install` then `git lfs pull` |
| Opens a blue empty VR room | Wrong scene or wrong project | Open `Assets/Scenes/Forage.unity` in the cloned folder |
| Unity opens in **Safe Mode** | Compile errors, usually a half-finished package resolve | Let package resolution finish, then **Assets ▸ Reimport All** |
| No forest, no animals in the Scene view | Normal — the world is generated at runtime | Press **Play** |
| Editor frozen on "Opening project…" | First-time asset import | Wait; check CPU usage is non-zero before assuming a hang |
| Falling through the ground | Stale scene from an older commit | **Forage ▸ Build Forage Scene** |

---

## Building the Quest 3 APK

Only needed for on-device testing.

1. Ensure **Android Build Support + SDK/NDK + OpenJDK** are installed for 6000.3.21f1.
2. **File ▸ Build Settings ▸ Android ▸ Switch Platform** (first switch re-imports
   textures; allow 10+ minutes).
3. XR Plug-in Management is already configured: **OpenXR** with **Meta Quest
   Support**, ARM64, IL2CPP.
4. Enable Developer Mode on the headset (Meta Horizon app), connect by USB, allow
   USB debugging, then **Build and Run**.
