# Changelog

All notable changes to Ooshak are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

## [0.2.0] - 2026-11-19

### Added

- A macOS preview for Apple silicon: a menu bar app with the same voice loop, delivered as a DMG. It uses the Accessibility API to type into the Claude app, PortAudio for sound, Whisper on Metal and the Mac's own voices for replies. It is signed ad hoc, not notarized, and has not yet been tried on a real Claude session; see the README for what is missing.
- Windows and macOS setup files are built and tested by an Azure pipeline (`azure-pipelines.yml`).

### Fixed

- Ooshak no longer pauses itself all the time when the taskbar auto-hides: a maximized window covering the whole screen is not taken for a full-screen game or video any more.
- Connecting to Claude Code works when Claude Code has not created its settings file yet.

## [0.1.2] - 2026-11-11

### Fixed

- A reply could start while you were still talking when the system was busy. The wait now checks for speech after every pause.
- Release checksums use LF line endings, so `sha256sum -c SHA256SUMS.txt` works.

## [0.1.1] - 2026-11-11

### Fixed

- The permission popup now appears while a game or video is full screen. Only listening and reading pause.
- The permission popup opens on the screen with the Claude window, and moves to another screen when that one is taken by a full-screen app.
- Failed message deliveries now log the exact reason.
- Pronunciation data is read correctly when the repository is checked out with CRLF line endings.

## [0.1.0] - 2026-11-10

First public preview.

### Added

- Always-on listening with a wake name you teach with your own voice.
- On-device speech recognition with Silero VAD and Whisper large-v3-turbo on Vulkan.
- Sending spoken messages to Claude Code sessions in the Claude desktop app by name, alias, last session, a general session or a single go-between session.
- Spoken replies through local Piper voices, with assistant-style answers, reading speed, pronunciation dictionary and English pronunciation for the Turkish voice.
- Permission popup for Claude Code permission requests.
- Pause, resume and stop reading by voice or hotkey, replies held while you talk, echo filtering, and automatic pause in full-screen apps.
- Turkish and English interface, commands and voices.
- Floating island, tray icon and main window with status, settings and log.
- First-run model download with pinned revisions and SHA-256 checks.
- Per-user Windows installer.

[0.2.0]: https://github.com/Milvasoft/ooshak/releases/tag/v0.2.0
[0.1.2]: https://github.com/Milvasoft/ooshak/releases/tag/v0.1.2
[0.1.1]: https://github.com/Milvasoft/ooshak/releases/tag/v0.1.1
[0.1.0]: https://github.com/Milvasoft/ooshak/releases/tag/v0.1.0
