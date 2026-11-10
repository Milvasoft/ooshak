# Third-party notices

Ooshak is MIT licensed. It uses the components below, each under its own license.

## Bundled with the installer (NuGet packages)

| Component | License |
|---|---|
| [Whisper.net](https://github.com/sandrohanea/whisper.net) and its native whisper.cpp runtimes | MIT |
| [ONNX Runtime](https://github.com/microsoft/onnxruntime) | MIT |
| [NAudio](https://github.com/naudio/NAudio) | MIT |
| [FlaUI](https://github.com/FlaUI/FlaUI) | MIT |
| [.NET](https://github.com/dotnet/runtime) and System.Drawing.Common | MIT |

## Included data

`src/Ooshak.Core/Resources/EnglishPronunciations.txt` is derived from the [CMU Pronouncing Dictionary](https://github.com/cmusphinx/cmudict), Copyright (C) 1993-2015 Carnegie Mellon University, used under its BSD-style license. The full license text is kept at the top of the file.

## Downloaded on first run (not distributed with Ooshak)

Each user downloads these directly from their publishers. Ooshak pins the exact revision and checks the SHA-256 of every file.

| Model | Source | License |
|---|---|---|
| Whisper large-v3-turbo (GGML) | [ggerganov/whisper.cpp](https://huggingface.co/ggerganov/whisper.cpp) | MIT |
| Silero VAD | [snakers4/silero-vad](https://github.com/snakers4/silero-vad) | MIT |
| Piper (Windows build 2023.11.14-2) | [rhasspy/piper](https://github.com/rhasspy/piper) | MIT |
| Turkish voice `tr_TR-dfki-medium` | [rhasspy/piper-voices](https://huggingface.co/rhasspy/piper-voices), trained on the [DFKI MaryTTS data](https://github.com/marytts/dfki-ot-data/) | CC BY-NC-SA 4.0, **non-commercial use only** |
| English voice `en_US-ljspeech-medium` | [rhasspy/piper-voices](https://huggingface.co/rhasspy/piper-voices), trained on [LJ Speech](https://keithito.com/LJ-Speech-Dataset/) | Public domain dataset |

If you use Ooshak commercially, use the English voice or replace the Turkish voice with one whose license allows it.

Claude and Claude Code are trademarks of Anthropic, PBC. Ooshak is not affiliated with Anthropic.
