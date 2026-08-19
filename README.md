# Lingshan Swift Ride / 灵山飞驰

A first-person PICO XR roller-coaster experience built with Unity 6 and URP. The ride travels through mountains, mist and fantasy environments, with a start page, an `AGAIN / EXIT` finish page, head-tracked viewing and layered mine-cart audio.

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

## APK

A PICO-ready APK is attached to the GitHub release. Build output is intentionally excluded from source control.

## License

Original source code in this repository is available under the MIT License. PICO SDK, Unity Asset Store content and any other third-party material remain subject to their own licenses and are not relicensed here.
