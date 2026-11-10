# Contributing to Ooshak

Thanks for helping. Issues and pull requests are welcome in English or Turkish.

## Before you start

- For bugs, open an issue with the bug template. Attach the relevant lines from `%USERPROFILE%\.ooshak\ooshak.log`, with any private session names removed.
- For larger changes, open an issue or a discussion first so we can agree on the approach.
- Look for issues labeled `good first issue` if you are new to the code.

## Development setup

1. Install the .NET 10 SDK and Windows 10 or 11.
2. Clone the repository and run the tests:

   ```powershell
   dotnet test
   ```

3. Download the speech models once (about 1.8 GB):

   ```powershell
   dotnet run --project src/Ooshak.Cli -- download-models
   ```

4. Run the app:

   ```powershell
   dotnet run --project src/Ooshak
   ```

`src/Ooshak.Cli` has commands for working on one piece at a time: `pipeline <file.raw>` runs 16 kHz PCM audio through detection, transcription and routing without sending anything, `speak-text` shows how a sentence will be pronounced, and `scan` lists what the Claude window exposes to UI Automation.

## Guidelines

- Keep pull requests focused on one change and explain why it is needed.
- Add or update tests in `tests/Ooshak.Tests` for behavior you change. Routing, text handling and anything without a window should be covered.
- Every text shown to users goes through `L.T("Turkish source")` in code or `{local:T 'Turkish source'}` in XAML, with an English entry in `Translations.cs`. A test fails when a translation is missing.
- Name things after what they mean, and write comments only for the reasons the code cannot show.
- Ooshak must never approve permissions, send messages or speak on its own initiative. Changes that touch these paths need a clear explanation of how they stay safe.
- Do not add telemetry or network calls beyond the pinned model downloads.

## Pull requests

1. Fork the repository and create a branch from `main`.
2. Make your change, run `dotnet test`, and try it in the app if it affects behavior.
3. Open a pull request and fill in the template. CI must pass before merging.

By contributing you agree that your contributions are licensed under the MIT License.
