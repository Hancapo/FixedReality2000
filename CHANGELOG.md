# Changelog

All notable changes to Fixed Reality 2000 are documented in this file.

## [Unreleased]

### Removed

- Removed the `player_storecamera` optimization and its F8 toggle. The mod no
  longer disables or removes the secondary camera from the URP camera stack.
  Obsolete configuration entries are deleted automatically.

## [0.2.0] - 2026-07-29

### Added

- A native-styled Controls tab with separate Mouse, Keyboard, and Gamepad
  subpages.
- In-game keyboard rebinding for movement, sprint, previous/next tool, utility,
  and toolbar visibility. Duplicate assignments swap bindings, Delete unbinds,
  Backspace cancels capture, and Reset Defaults restores the original layout.
- A Gamepad section organized into Tuning and Preferences pages.
- Controller settings for independent look sensitivity, radial movement and
  look deadzones, response curve, trigger threshold, menu cursor speed,
  horizontal and vertical inversion, Standard/Southpaw stick layouts, and
  Hold/Toggle sprint behavior.
- Controller rebinding for primary and secondary actions, utility,
  previous/next tool, toolbar visibility, and sprint. Binding names adapt to
  Xbox, PlayStation, and Nintendo controllers.
- Controller vibration with an enable switch and adjustable intensity.
- Conventional D-pad and analog-stick focus navigation for the options menu.
  Directional input moves between controls, sliders require A to enter an
  explicit edit mode before consuming left/right, A submits once through
  Unity's native UI path, and moving the physical mouse restores cursor
  navigation.
- Native-styled Display and Graphics subpages under Video.
- A monitor selector that moves the game window to the selected named display
  before reapplying its resolution and display mode.
- A V-Sync option under Display. Enabling it uses Unity's native display
  synchronization and temporarily disables the FPS Limit control; disabling it
  restores the previously selected frame-rate limit.
- A post-process anti-aliasing selector with Off, FXAA, and SMAA modes.

### Fixed

- Controller movement now uses radial deadzones and preserves analog stick
  magnitude instead of jumping to full walking speed immediately outside the
  retail game's extremely small deadzone.
- Controller camera sensitivity is independent from mouse sensitivity and
  remains consistent between `00_room` and the rest of the game.
- Controller gameplay actions now use the selected bindings instead of the
  retail hard-coded button checks.
- Controller look and the pause-menu virtual cursor now reject stick drift.
  The cursor also uses unscaled time and remains responsive while paused.
- Looking or pressing a controller button now switches the active input method;
  moving the left stick is no longer required.
- Moving or clicking the physical mouse immediately restores the native cursor
  after opening the pause menu with a controller. Mouse and controller
  navigation can be alternated without reopening the menu.
- Pause-menu focus is now recovered after page changes and modal transitions
  instead of leaving the controller unable to select anything.
- The pause-menu Home navigation uses stable explicit neighbours, preventing
  Items from intermittently being skipped when moving between Tasks and Skins.
- Expanded dropdowns consume B before the pause menu handles Back. B can also
  close slider edit mode, return through menu pages consistently, and unpause
  directly from the Home screen.
- The exit confirmation can be navigated and activated with a controller. B
  closes only the confirmation and returns focus to Exit instead of leaving the
  pause menu.
- Controller input can advance the computer examination dialogue in `00_room`,
  navigate its answer list with the stick or D-pad, and confirm a choice
  without requiring a mouse click.
- Hold sprint stops when keyboard or mouse input takes over. Toggle sprint now
  resets when the movement stick returns to neutral instead of resuming when
  the player moves again.
- Keyboard control prompts, tool-change hints, and toolbar labels now display
  the current bindings instead of the retail hard-coded keys.
- Unbound keyboard actions no longer interrupt the game's complete keyboard
  input loop or leave the player unable to move.
- The first-person camera pitch is limited before the camera can intersect the
  player's body when looking straight up or down.
- Graphics overrides wait until the animated DMT splash has finished,
  preventing its mesh trails from becoming large overexposed polygons.
- Original texture filtering now enables anisotropic filtering and applies 16x
  anisotropy to filtered, mipmapped environment textures. It remains limited
  to `ENVIRONMENT` outside `00_room`.
- Nearest filtering now affects only albedo and base-color textures. Normal
  maps, masks, lightmaps, and other data textures keep their authored
  filtering, preventing stippled and noisy material artifacts.
- Returning from Nearest to Original restores both the authored filter mode
  and the original per-texture anisotropy level.
- Borderless mode now remains visibly selected after restarting the game; the
  dropdown is synchronized with the saved display mode rather than its
  serialized Fullscreen default.

### Changed

- Mouse sensitivity and invert mouse moved from Game to the Mouse controls
  subpage. Game now contains only gameplay-facing options such as FOV and
  toolbar visibility.
- The former Anti-Aliasing option is now named MSAA to distinguish it from
  post-process anti-aliasing.
- FPS Limit moved to the Display subpage.
- Monitor names are shortened when necessary so they cannot overlap the
  dropdown arrow.
- Keyboard bindings are stored in the readable
  `FixedReality2000.keybindings.cfg` file. Valid legacy bindings are migrated
  automatically and their old PlayerPrefs entries are removed.
- Controller settings and bindings are stored in the readable
  `FixedReality2000.controller.cfg` file.
- Real-time shadows are approximately 30% darker while preserving direct-light
  brightness as much as possible.

## [0.1.0] - 2026-07-28

### Added

- Expanded Video settings with an FPS limiter, texture filtering options, and
  MSAA selection.
- Automatic 21:9 and 32:9 support for resolutions reported by the display.
- An Aspect Ratio selector below Resolution with Auto, 4:3, 16:9, 16:10, 21:9,
  and 32:9. Fullscreen modes only expose display-reported resolutions, while
  Windowed can also use sizes calculated from the monitor dimensions.
- Changing Aspect Ratio refreshes the list without resizing the window; the
  selected Resolution remains the explicit action that applies a new size.
- Window-sized aspect-ratio options are calculated from the display dimensions
  and reported modes instead of relying on a fixed resolution table.
- Resolution, aspect ratio, and display-mode changes are now applied through a
  coordinated, verified path with short retries when Unity defers a window update.
- Screen cameras now use the actual display aspect ratio, while screen-space UI
  expands instead of being cropped or stretched on ultrawide displays.
- A 50–120 degree FOV slider under the Game settings.
- A Very High graphics preset.
- Numeric values below slider handles, including the audio sliders.
- Sprinting while holding `Shift` and configurable head bobbing.
- Viewmodel compensation that keeps held items at a stable size and position
  across different FOV values.
- In-game CFG reloading with `F5`.
- An optional local bridge for real-time inspection through UnityExplorer.

### Fixed

- Removed the 60 FPS cap forced by `BrokenPlayer.Prepare`, which was especially
  noticeable when using the Low preset.
- The in-game FPS limiter now applies and persists correctly.
- Resolutions are populated from the display's actual supported modes;
  2560×1440 now appears when available.
- Fullscreen, Borderless, and Windowed modes now apply during gameplay.
- FOV no longer resets to 60 when reopening the menu or after certain player
  animations.
- Slider values now appear below their handles instead of replacing the option
  labels.
- Medium, High, and Very High now enable shadows correctly.
- High now uses a higher shadow resolution than Medium.
- Shadows are applied when loading the game without requiring the options menu
  to be opened first.

### Changed

- Nearest filtering is limited to environment materials under `ENVIRONMENT`
  outside the `00_room` scene, while preserving anisotropic filtering.
- FOV, FPS limit, texture filtering, and MSAA are stored through `PlayerPrefs`
  instead of being duplicated in `FixedReality2000.cfg`.
- The remaining rendering optimizations are limited to the SRP Batcher, dynamic
  batching, and caches for repeated object lookups.
- The `player_storecamera` optimization retains an `F8` manual restore for its
  unlockable gameplay mechanic.
- Source comments and CFG descriptions are written in English.

### Removed

- Brutal Performance Mode.
- Runtime distance culling.
- Aggressive spatial instancing that replaced renderers.
- Forced GPU instancing on every loaded material.
- Runtime static batching.
- The centralized script scheduler and NPC navigation throttling.
- Experimental replacements for scene-script update loops.
- Toggles that removed URP decals and RenderObjects features.
- The performance diagnostics overlay and its testing hotkeys.
- The example patch that logged money changes.

These features were removed because they altered visuals or behavior, produced
inconsistent performance gains, or existed solely for A/B testing.
