# RimTouch – Windows 1.7 continuation

A Windows touch continuation of Tatsuki Tanaka’s RimTouch. Provides touch selection, UI repair, pinch zoom, panning, world-map controls and input cleanup for RimWorld 1.7.

Upstream: https://github.com/iLtanaka/RimTouch. Original author: Tatsuki Tanaka. Windows continuation by Sweet Briana and Cinder. GPL-3.0-or-later is retained for the continuation. The source snapshot is based on local commit c7bfa3adc60bc23ee5ea101f23320db934ef22b1, including main-menu cleanup guards. Metadata retains candidate wording; compilation is not certification for all touchscreens, display scaling, twist gestures, Linux or arbitrary mod lists.

- Package ID: `cinder.rimtouch17.private`
- Code license: GPL-3.0-or-later. See LICENSE.
- Steam Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id=3827698857

## Source release

This is the selected production source and metadata snapshot, with game, Harmony and provider assemblies omitted. Original workspaces, installed mods, active lists and player saves are unchanged. This GitHub publication does not update Steam Workshop. See BUILD-STATUS.md for fresh compilation evidence and its limits.

## Build for 1.7

Use PowerShell 7 and an installed .NET SDK. Supply your licensed game and matching Harmony assembly:

```powershell
./Build17.ps1 -GameDir 'PATH/TO/RIMWORLD' -HarmonyDll 'PATH/TO/0Harmony.dll'
```

The portable helper compiles only Source/ into .build/1.7, without downloading dependencies, starting a game, copying a runtime or changing installed mods. Archived IDE project files may retain legacy references; the 1.7 helper is the supported build path for this snapshot. Legacy source, where included, is separate and has not been newly tested. Assets have separate terms in ASSETS.md.
