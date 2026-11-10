# Security policy

## Reporting a vulnerability

Please do not open a public issue for security problems.

Report them privately through [GitHub private vulnerability reporting](https://github.com/Milvasoft/ooshak/security/advisories/new). Include the version, the steps to reproduce, and the impact you expect. We aim to reply within a week.

## Supported versions

Ooshak is in early development. Only the latest release receives fixes.

## Areas that matter most

- **Permission popup.** Anything that could approve a Claude Code permission without a deliberate mouse click on the popup.
- **Local hook endpoint** (`localhost:47811`). Anything that lets a web page or another user's process send events or receive answers.
- **Message delivery.** Anything that types text into the wrong session or into a field the user is editing.
- **Model downloads.** Anything that could make Ooshak run a file whose SHA-256 does not match the pinned value.
