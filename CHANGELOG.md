# Changelog

All notable changes to Ooshak are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

## [0.3.0] - 2026-11-26

### Fixed

- Ooshak paused itself almost all the time: Windows' everyday "accepts notifications" state was taken for presentation mode, so listening and reading stopped as if a full-screen game were running. Only full-screen Direct3D apps and real presentation mode pause it now.

### Added

- A **Waiting** page lists every Claude Code session that needs you, grouped by project: permission requests with Allow and Deny right on the card, questions a session asked, and sessions that finished and wait for your next message. Cards show how long they have waited, open the session in the Claude app (a terminal session offers its `claude --resume` command instead), and go away by themselves when the session gets back to work. Terminal sessions are listed too. The tray and menu bar menus open the page directly.
- Questions a session asks (AskUserQuestion) show their options as buttons, in the popup and on the card. One click answers a single question; several questions or multiple choice are picked and sent with Answer. "Answer in Claude" leaves the question to Claude's own window.
- A message for a busy session is no longer dropped with "I couldn't send the message". If the session is working, waiting for a permission or a question, or you are typing, Ooshak says so in one sentence, keeps the message and delivers it as soon as the session is free, for up to ten minutes.
- Ooshak reads permission requests and questions out loud ("Atlas wants permission to run a command. Approve?"). Answer "yes", "no" or an option's name within 15 seconds without the wake name, or later with it. Speech that starts while Ooshak talks needs the wake name, so its own voice can never approve anything. Every voice decision is logged with what was heard.
- "Ooshak, what's the status?" ("Uşak, ne durumdayız?") sums up who needs you and who is still working.
- A delivered message gets a short spoken "On it, Atlas has it" instead of a beep; it names the session only when it changed. Both this and the read-out requests can be turned off in Settings.
- Sessions are asked to end voice replies like a personal assistant: the outcome first, two short sentences, no code, paths or ids. A saved older default instruction picks up the new one.
- Ooshak now also listens to the `UserPromptSubmit`, `PostToolUse` and `SessionEnd` hooks to know when a session is working again. An existing connection is updated on the next start.

## [0.2.0] - 2026-11-19

### Added

- A macOS preview for Apple silicon: a menu bar app with the same voice loop, delivered as a DMG. It uses the Accessibility API to type into the Claude app, PortAudio for sound, Whisper on Metal and the Mac's own voices for replies. It is signed ad hoc, not notarized, and has not yet been tried on a real Claude session; see the README for what is missing.
- The release workflow builds and tests both platforms on GitHub Actions; on the Mac runner `Ooshak --self-test` runs a spoken command through Silero, Whisper and the router.

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

[0.3.0]: https://github.com/Milvasoft/ooshak/releases/tag/v0.3.0
[0.2.0]: https://github.com/Milvasoft/ooshak/releases/tag/v0.2.0
[0.1.2]: https://github.com/Milvasoft/ooshak/releases/tag/v0.1.2
[0.1.1]: https://github.com/Milvasoft/ooshak/releases/tag/v0.1.1
[0.1.0]: https://github.com/Milvasoft/ooshak/releases/tag/v0.1.0
