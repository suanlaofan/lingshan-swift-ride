# Lingshan Swift Ride / 灵山飞驰

A first-person PICO XR roller-coaster game built with Unity 6 and URP. Ride through mountains, valley mist and fantasy environments while actively aiming at and collecting 12 charms with your controller.

Version **0.2.1** adds the 灵山飞驰 app name and game icon, optimized architecture LODs and PICO rendering settings, black surroundings outside the start/finish panels, and immediate START / AGAIN transitions. The valley fog objects and original fog materials are preserved.

## Requirements

- Unity `6000.5.4f1`
- Android Build Support (`SDK`, `NDK` and OpenJDK installed by Unity Hub)
- PICO Emulator `0.13.0` or a compatible PICO headset
- PICO XR Unity package `com.bytedance.pico.xr` version `0.13.1`

## Third-party packages

This public repository contains the original game code, project settings, scenes and redistributable project assets. The following licensed packages are intentionally excluded from Git history:

- `Packages/com.bytedance.pico.xr`
- `Assets/Allsky`
- `Assets/PolygonNatureBiomes`

Obtain those packages from their official sources under their respective licenses, then restore them at the paths above. The package manifest expects the PICO package at `Packages/com.bytedance.pico.xr`.

## Run

1. Open the repository with Unity `6000.5.4f1`.
2. Restore the licensed packages listed above.
3. Open `Assets/Scenes/EnvironmentScene.unity`.
4. Enter Play Mode, or use `Tools > Lingshan > Build PICO APK` for an Android build.

The PICO package identifier is `com.newgamestudio.picoenvironmentstarter`.

## Controls

In PICO Emulator, enable `Controller Button Mode` from the left toolbar. Aim a controller ray at `START`, `AGAIN` or `EXIT`, then press the corresponding trigger key. On a headset, point with either controller and pull its trigger.

## Validation and performance

- Unity logic validation: 11 cases passed. Editor integration: 3 rounds, 36 active collections; wrong-hand, held-trigger and non-aimed inputs rejected. Start/finish black backgrounds and immediate START / AGAIN transitions are asserted. Editor tests use temporary tracked-controller input and route checkpoints.
- PICO Emulator 0.13: the final APK installed and ran a complete natural route, with 5 charms collected and a 500/1200 result; the finish panel surroundings were visually black. A simulator-only presence workaround prevents its false “not worn” state from immediately pausing the ride. Real headsets retain wearer checks.
- Architecture LODs are 60,000 / 15,000 / 3,500 triangles per instance. Counting one active LOD per instance reduces the scene mesh budget from 4,686,719 to 905,927 triangles (80.7%). This is not a measured FPS improvement.
- Actual PICO headset FPS and sustained thermal performance remain unverified. Emulator application Update intervals are not headset presentation FPS.

The optional vendored `com.coplaydev.unity-mcp` 10.1.0 Editor tool retains its upstream MIT license. The project-local connection helper uses localhost port 8098; it is not an APK runtime requirement.

## APK

A PICO-ready APK is attached to the GitHub release. Build output is intentionally excluded from source control.

## License

Original source code in this repository is available under the MIT License. PICO SDK, Unity Asset Store content and any other third-party material remain subject to their own licenses and are not relicensed here.
