# Fixed Reality 2000

Fixed Reality 2000 is a BepInEx mod for **Broken Reality 2000**. It fixes
broken PC settings, expands the available video and control options, and
improves keyboard and controller support.

## Features

- Reliable resolution, monitor, display mode, aspect ratio, V-Sync, and FPS
  limit settings.
- Ultrawide support for 21:9 and 32:9 displays.
- Adjustable FOV with first-person viewmodel compensation.
- Very High graphics quality, improved shadows, MSAA, post-process
  anti-aliasing, and texture filtering options.
- Numeric values for the game's sliders.
- Keyboard rebinding and improved controller input and menu navigation.
- Configurable controller sensitivity, deadzones, response curve, stick
  layout, vibration, and sprint behavior.
- Sprinting and subtle head bobbing.

## Requirements

- Broken Reality 2000 for Windows.
- BepInEx 5.4.23.5 Mono.

## Installation

Extract the release archive into the Broken Reality 2000 installation folder.
The DLL should end up at:

```text
Broken Reality 2000\BepInEx\plugins\FixedReality2000\FixedReality2000.dll
```

Most settings are available directly from the in-game Options menu. Additional
configuration files are generated under `BepInEx\config`.

## Building

```powershell
dotnet build .\FixedReality2000.sln -c Release
```

Use `-p:DeployOnBuild=true` to copy the compiled DLL to the configured game
installation.

See [CHANGELOG.md](CHANGELOG.md) for version history.
