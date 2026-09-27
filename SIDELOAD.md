# Installing Forage on a Meta Quest 3

This is the headset-day checklist. Everything here happens **after** the APK is
built — if `Builds/Leafwise.apk` does not exist yet, build it first (see
[Rebuilding the APK](#rebuilding-the-apk)).

You do **not** need Unity installed to sideload. You need the APK and `adb`.

---

## 1. One-time: turn on Developer Mode

Developer Mode is what allows a headset to install apps that did not come from
the Meta Store. It is free, but it requires a developer account tied to an
"organization" (a formality — you can be the only member).

1. On a computer, go to <https://developer.meta.com/manage/organizations/> and
   sign in with the **same Meta account that is logged into the headset**.
2. Create an organization. Any name works, e.g. `RMIT Mixed Reality`.
3. Accept the developer agreement. You may be asked to add a payment method or
   enable two-factor authentication — 2FA is the quicker of the two and is
   enough to unlock Developer Mode.
4. On your phone, open the **Meta Horizon** app (formerly Oculus).
5. `Menu` → `Devices` → select your Quest 3 → `Headset settings` →
   **Developer Mode** → toggle **on**.
6. Reboot the headset (hold the power button → Restart). Developer Mode does not
   reliably take effect until it restarts.

> If the Developer Mode toggle is missing or greyed out, the account in the phone
> app is not the one that owns the organization from step 2. That mismatch is the
> single most common cause.

---

## 2. Connect the headset over USB

1. Plug the Quest 3 into the computer with a USB-C cable. Use a cable that does
   data, not a charge-only cable — this is the second most common cause of
   trouble.
2. Put the headset on. You will see **"Allow USB debugging?"** — check
   *Always allow from this computer* and choose **Allow**.
   - If no prompt appears, unplug and replug while wearing the headset.

Confirm the computer can see it. `adb` ships with the Unity Android module, so
you already have it:

```powershell
$adb = "C:\Program Files\Unity\Hub\Editor\6000.3.21f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
& $adb devices
```

Expected:

```
List of devices attached
1WMHHXXXXXXXXX  device
```

| What you see | What it means |
| --- | --- |
| `device` | Ready. Continue to step 3. |
| `unauthorized` | The "Allow USB debugging?" prompt has not been accepted. Put the headset on. |
| *(empty list)* | Cable is charge-only, Developer Mode is off, or the headset needs a reboot. |
| `offline` | Unplug, replug, and re-accept the prompt. |

---

## 3. Install the APK

```powershell
$adb = "C:\Program Files\Unity\Hub\Editor\6000.3.21f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
& $adb install -r E:\unity\Leafwise\Builds\Leafwise.apk
```

`-r` reinstalls over an existing copy, keeping the same app identity — use it for
every update after the first install. A successful run ends with `Success`.

If you get `INSTALL_FAILED_UPDATE_INCOMPATIBLE`, a copy signed with a different
key is already on the headset. Remove it, then install again:

```powershell
& $adb uninstall com.rmit.forage
& $adb install E:\unity\Leafwise\Builds\Leafwise.apk
```

---

## 4. Launch it

In the headset: **Library** → the **Unknown Sources** filter (top-right dropdown
in the app grid) → **Forage**.

Sideloaded apps never appear in the main store-apps list. If you cannot find it,
you are almost certainly not looking at Unknown Sources.

To launch it from the computer instead:

```powershell
& $adb shell am start -n com.rmit.forage/com.unity3d.player.UnityPlayerActivity
```

---

## 5. Watch the logs while it runs

This is the fastest way to diagnose anything that behaves differently on device
than it did in the editor:

```powershell
& $adb logcat -s Unity:V
```

Leave that running, put the headset on, and the game's `Debug.Log` output streams
to your terminal. Every message the simulation prints is prefixed `[Forage]`, so
to see only our own logs:

```powershell
& $adb logcat -s Unity:V | Select-String "\[Forage\]"
```

On a healthy launch you should see the world build itself:

```
[Forage] NavMesh baked: 9331 verts, 4099 tris
[Forage] Climbable trees registered: 438
[Forage] Field Mushroom: 8 placed in open meadows
[Forage] Chanterelle: 5 placed in damp ground near the pond
[Forage] Fly Agaric: 6 placed in under woodland trees
[Forage] Death Cap: 7 placed in deep shaded woods
```

---

## Rebuilding the APK

Two ways. Both produce `Builds/Leafwise.apk`.

**From the Unity editor** — menu **Forage → Quest**:

| Menu item | What it does |
| --- | --- |
| `1 - Configure XR and Player Settings` | Applies every Quest setting in code: ARM64, IL2CPP, Vulkan, multi-view, min SDK 32, linear colour, OpenXR loader + Meta Quest feature + controller profiles. |
| `2 - Validate Build Readiness` | Runs 12 pre-flight checks and prints a PASS/FAIL table. Run this before building. |
| `3 - Build APK` | Validates, then builds to `Builds/Leafwise.apk`. |
| `4 - Build and Run on Headset` | Same, then installs and launches on the connected headset. |

**Headless**, which is what CI would use:

```powershell
$unity = "C:\Program Files\Unity\Hub\Editor\6000.3.21f1\Editor\Unity.exe"
& $unity -batchmode -quit -projectPath E:\unity\Leafwise -buildTarget Android `
         -executeMethod Forage.EditorTools.QuestBuild.BuildApk `
         -logFile E:\unity\Leafwise\Logs\c8-build.log
```

Check `$LASTEXITCODE` — `0` means success. The settings are applied from code
rather than clicked in the inspector on purpose: they are versioned in
[QuestBuild.cs](Assets/Editor/Forage/QuestBuild.cs), reviewable in a diff, and
reproduce identically on a teammate's machine.

### If the build fails on Burst

This machine has previously hit Burst AOT linker failures
(`burst-lld-21-hostwin` exiting non-zero), most often caused by antivirus
locking the linker's temp files. Burst only accelerates the NavMesh bake here,
so disabling its compilation is a safe fallback that costs a little animal
pathfinding performance:

```powershell
& $unity -batchmode -quit -projectPath E:\unity\Leafwise -buildTarget Android `
         -burst-disable-compilation `
         -executeMethod Forage.EditorTools.QuestBuild.BuildApk `
         -logFile E:\unity\Leafwise\Logs\c8-build.log
```

The real fix is to whitelist `E:\unity\Leafwise` in your antivirus.

---

## Performance expectations on device

The forest generates **~10,400 mesh renderers**, of which ~9,300 sway on the
wind shader. That is comfortable on a desktop GPU but is the most likely thing
to need tuning on Quest 3's mobile chip. If the frame rate is poor on device,
tune these first, in this order:

1. **Reduce prop counts** — `ForestGenerator` exposes `treeAttempts` (1000),
   `grassAttempts` (900), `fernAttempts` (320), `bushAttempts` (150) on the
   `Forest` object in the scene. Halving grass and ferns costs the least
   visually.
2. **Shorten the camera far plane / add fog** so distant trees cull earlier.
3. **Drop `_WindStrength` to 0** on the grass materials — vertex wind on 9,300
   renderers is pure vertex-shader cost.

Profile before tuning: `adb logcat` plus the Unity Profiler attached over
network (`Window → Analysis → Profiler`, set target to the device) will tell you
whether you are CPU- or GPU-bound rather than guessing.
