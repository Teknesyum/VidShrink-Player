# Changelog

All notable changes to VidShrink Player are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow
[Semantic Versioning](https://semver.org/).

The history of the program under its old name, VidShrink, up to 1.2.24 and the changes
made after it, is in [`docs/CHANGELOG-VidShrink.md`](docs/CHANGELOG-VidShrink.md).

## [Unreleased]

### Added

- The player shortcuts list shows double-click (full screen) and hold left click (2× until released) under the left-click row.
- Holding the left button shows the 2× speed badge for as long as the button is held; releasing shows the restored speed.
- The comparison panel takes the player's left-click behaviour: double-click toggles the panel's full screen and holding plays both sides and the audio at 2× until released.

## [0.0.3] - 2026-10-22

### Added

- The title bar shows the app version to the right of the name; clicking it checks for updates and reports whether the app is up to date, an update is ready, or the check failed.

### Changed

- The recorder microphone and system audio level bars now redraw on every display frame (about 60 updates per second instead of 20), with the peak rising fast and easing down slowly. The level source reports 25 ms windows instead of 50 ms, and the frame loop runs only while a bar is visible and moving.
- The recorder result panel no longer lists "Save as MP4" and "Convert to GIF" as separate
  buttons; they now sit in a "More" menu at the end of the action row, which is hidden when
  neither conversion applies.
- Recorder mini strip: the start/pause button is now a filled primary button and the stop button an accent one, so both stand out; the strip can be moved by pressing anywhere outside its buttons; its tooltips now wait 1 second longer (1.4 s in total) before appearing.

### Fixed

- Editor: dragging the playhead along the ruler no longer leaves it sliding backward on its own after you stop or release; queued preview seeks are coalesced so only the latest one runs, and the playhead reports the requested position while a seek is in flight.
- The default-app suggestion banner no longer appears twice after the window is restored from the tray; restoring no longer reruns the startup path.

## [0.0.2] - 2026-10-21

### Added

### Changed

- Every labelled field is now a single Tab stop: the "?" badge beside a field label no
  longer takes focus from the keyboard. Its help is read by screen readers as the field's
  description, and F1 on the field opens it.
- Screen readers read the target size chips with their unit ("8 MB") and the share link
  boxes by name.

### Fixed

- Changing the colour matrix (Convert's BT.601/709/2020 choice and Shrink's colour space
  filter) now resamples colour with Lanczos instead of bilinear, so sharp colour edges stay
  cleaner.
- Recorder: on several monitors the drawing layer opens on the right screen. Without a capture area it covers the
  screen under the pointer instead of always the primary one; an area spanning monitors with different scaling is
  trimmed to one monitor so the layer is not sized with the wrong scale.
- The installers (setup program, silent install and `Install-VidShrink.ps1`) now write the
  Windows 11 context menu labels in the install language, so the first menu no longer shows the
  built-in English names until the app is opened once. Uninstall removes them.
- Closing the player ends its Windows media controls session; pressing a stale one could crash the app.

## [0.0.1]

### Changed

- VidShrink is now VidShrink Player. The program window and the documents use the new
  name.
- The repository is now [Teknesyum/VidShrink-Player](https://github.com/Teknesyum/VidShrink-Player).
- Version numbers start again at `0.0.1`. The last release under the old name was 1.2.24;
  everything listed under its Unreleased section in the old changelog ships in this release.

### Moving From VidShrink

- One manual step, once. If your VidShrink install does not offer VidShrink Player as an
  update, install it once with any of the ways in the [README](README.md#install). Updates
  after that arrive as before.
