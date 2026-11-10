using FlaUI.Core.Definitions;
using Ooshak.Core;

Console.OutputEncoding = System.Text.Encoding.UTF8;

switch (args.FirstOrDefault())
{
    case "dump":
        return Dump();
    case "scan":
        return Scan();
    case "state":
    {
        using var claude = new ClaudeWindow();
        var (shown, prompt, hasSend) = claude.ReadState();
        Console.WriteLine($"shown='{shown}' prompt='{prompt}' send={hasSend}");
        return 0;
    }
    case "clear" when args.Length == 2:
    {
        using var claude = new ClaudeWindow();
        Console.WriteLine($"cleared={claude.ClearIfEquals(args[1])}");
        return 0;
    }
    case "download-models":
    {
        var missing = ModelCatalog.Missing(OoshakConfig.ModelsDir);
        Console.WriteLine($"{missing.Count} files, {ModelCatalog.TotalBytes(missing) >> 20} MB");
        await ModelDownloader.DownloadAsync(missing, OoshakConfig.ModelsDir,
            new Progress<ModelProgress>(p => Console.Write($"\r{p.File} {p.Done >> 20}/{p.Total >> 20} MB   ")), CancellationToken.None);
        Console.WriteLine();
        return 0;
    }
    case "make-icon" when args.Length == 2:
    {
        var brass = System.Drawing.Color.FromArgb(0xE9, 0xB4, 0x4C);
        var paper = System.Drawing.Color.FromArgb(0xF4, 0xEE, 0xE3);
        Directory.CreateDirectory(args[1]);
        LogoPainter.WriteIcon(Path.Combine(args[1], "ooshak.ico"), [16, 20, 24, 32, 40, 48, 64, 128, 256], brass, paper);
        using var preview = LogoPainter.Render(512, brass, paper);
        preview.Save(Path.Combine(args[1], "logo-512.png"), System.Drawing.Imaging.ImageFormat.Png);
        using var small = LogoPainter.Render(32, brass, paper);
        small.Save(Path.Combine(args[1], "logo-32.png"), System.Drawing.Imaging.ImageFormat.Png);
        return 0;
    }
    case "pause-test":
    {
        var config = OoshakConfig.Load();
        using var speaker = new Speaker(config.ModelPath(config.PiperExe), config.ModelPath(config.PiperVoice));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        speaker.Say("Bu kısa bir duraklatma denemesidir, birazdan duracağım ve sonra kaldığım yerden devam edeceğim.");
        Thread.Sleep(1500);
        speaker.Pause();
        Console.WriteLine($"paused={speaker.IsPaused} speaking={speaker.IsSpeaking}");
        Thread.Sleep(1500);
        speaker.Resume();
        Console.WriteLine($"paused={speaker.IsPaused} speaking={speaker.IsSpeaking}");
        while (speaker.IsSpeaking)
            Thread.Sleep(50);
        Console.WriteLine($"finished in {watch.ElapsedMilliseconds} ms");

        speaker.Say("Bu cümle hemen kesilecek ve bir daha duyulmayacak, çünkü durdurma denemesi yapıyorum.");
        Thread.Sleep(1200);
        speaker.Pause();
        speaker.Stop();
        Thread.Sleep(300);
        Console.WriteLine($"after stop paused={speaker.IsPaused} speaking={speaker.IsSpeaking}");
        return 0;
    }
    case "speak-text" when args.Length == 2:
        Console.WriteLine(SpeechLexicon.Apply(args[1], OoshakConfig.Load().Pronunciations));
        return 0;
    case "say" when args.Length is 2 or 3:
    {
        var config = OoshakConfig.Load();
        using var speaker = new Speaker(config.ModelPath(config.PiperExe), config.ModelPath(config.PiperVoice));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        speaker.Say(args[1], args.Length == 3 ? double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 1.0);
        Thread.Sleep(300);
        while (speaker.IsSpeaking)
            Thread.Sleep(50);
        Console.WriteLine($"spoken in {watch.ElapsedMilliseconds} ms");
        return 0;
    }
    case "pipeline" when args.Length == 2:
        return await Pipeline(args[1]);
    case "enroll" when args.Length == 2:
        return await Pipeline(args[1], enroll: true);
    case "listen":
    {
        using var server = new HookServer(47811, (route, payload) =>
        {
            var keys = string.Join(",", payload.EnumerateObject().Select(p => p.Name));
            Console.WriteLine($"HOOK {route}: {keys}");
            Console.WriteLine($"SPOKEN: {ReplyExtractor.FromStopPayload(payload)}");
        });
        server.Start();
        Console.WriteLine("listening");
        Thread.Sleep(TimeSpan.FromSeconds(args.Length > 1 ? int.Parse(args[1]) : 60));
        return 0;
    }
    case "recap" when args.Length == 2:
        Console.WriteLine(ReplyExtractor.RecapFromTranscript(args[1]) ?? "(none)");
        return 0;
    case "install-hooks":
        Console.WriteLine($"backup: {HookInstaller.Install(OoshakConfig.Load().Port)}");
        return 0;
    case "remove-hooks":
        Console.WriteLine($"backup: {HookInstaller.Remove(OoshakConfig.Load().Port)}");
        return 0;
    case "sessions":
        foreach (var s in SessionCatalog.Load().Take(15))
            Console.WriteLine($"{s.LastActivity:MM-dd HH:mm}  {s.Title}");
        return 0;
    case "send" when args.Length == 3:
        return Send(args[1], args[2]);
    default:
        Console.WriteLine("usage: ooshak-cli dump|scan|sessions|send <title> <text>");
        return 1;
}

// Runs a 16 kHz signed 16-bit mono file through detection, transcription and routing. Nothing is delivered.
static async Task<int> Pipeline(string path, bool enroll = false)
{
    var config = OoshakConfig.Load();
    using var vad = new SileroVad(config.ModelPath(config.VadModel));
    using var transcriber = new Transcriber(config.ModelPath(config.WhisperModel), config.Language);
    var segmenter = new UtteranceSegmenter(config.SilenceMs);
    var titles = SessionCatalog.LoadRecent().Select(s => s.Title).ToList();
    L.Language = config.Language;
    var router = new VoiceRouter(config.WakeNames, TimeSpan.Zero);
    router.SetEnglish(config.Language == L.English);
    router.SetTargets(titles, config.Aliases);
    var vocabulary = VoicePrompt.Build(config, titles);

    var bytes = File.ReadAllBytes(path);
    var utterances = 0;

    for (var offset = 0; offset + SileroVad.FrameSamples * 2 <= bytes.Length; offset += SileroVad.FrameSamples * 2)
    {
        var frame = new float[SileroVad.FrameSamples];
        for (var i = 0; i < frame.Length; i++)
            frame[i] = BitConverter.ToInt16(bytes, offset + i * 2) / 32768f;

        var utterance = segmenter.Feed(frame, vad.Probability(frame));
        if (utterance is null)
            continue;

        utterances++;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var text = await transcriber.TranscribeAsync(utterance, enroll ? "" : vocabulary);
        Console.WriteLine($"[{utterance.Length / 16000.0:F1}s audio, {watch.ElapsedMilliseconds} ms] {text}");
        Console.WriteLine(enroll ? $"  => name: {WakeEnrollment.Extract(text) ?? "(rejected)"}" : $"  => {router.Route(text)}");
    }

    Console.WriteLine($"utterances: {utterances}, backend: {Transcriber.LoadedBackend}");
    return 0;
}

static int Send(string title, string text)
{
    var session = SessionCatalog.Load().FirstOrDefault(s => s.Title == title);
    if (session is null)
    {
        Console.WriteLine("session not found in catalog");
        return 3;
    }

    using var claude = new ClaudeWindow();
    var watch = System.Diagnostics.Stopwatch.StartNew();
    var status = claude.Deliver(title, text, restoreView: true, session.LocalId);
    Console.WriteLine($"{status} in {watch.ElapsedMilliseconds} ms");
    return status == DeliveryStatus.Sent ? 0 : 4;
}

static int Scan()
{
    using var claude = new ClaudeWindow();
    var window = claude.Find();
    if (window is null)
        return 2;

    var pattern = new System.Text.RegularExpressions.Regex(
        "message|reply|prompt|send|stop|queue|interrupt|proseMirror|tiptap|editor|textbox|composer",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    var all = window.FindAllDescendants();
    Console.WriteLine($"total: {all.Length}");
    Console.WriteLine("types: " + string.Join(", ", all
        .GroupBy(e => e.Properties.ControlType.ValueOrDefault)
        .Select(g => $"{g.Key}={g.Count()}")));

    foreach (var element in all)
    {
        var name = ClaudeWindow.Normalize(element.Properties.Name.ValueOrDefault);
        var cls = element.Properties.ClassName.ValueOrDefault ?? "";
        var id = element.Properties.AutomationId.ValueOrDefault ?? "";
        var type = element.Properties.ControlType.ValueOrDefault;
        var hasValue = element.Patterns.Value.IsSupported;

        if (name.StartsWith("More options for", StringComparison.Ordinal))
            continue;

        var interesting = type is ControlType.Edit or ControlType.Document
            || (hasValue && type != ControlType.Hyperlink)
            || pattern.IsMatch(cls) || pattern.IsMatch(id)
            || (type == ControlType.Button && pattern.IsMatch(name));

        if (!interesting)
            continue;

        var shortName = name.Length > 70 ? name[..70] : name;
        Console.WriteLine($"{type} name='{shortName}' class='{(cls.Length > 60 ? cls[..60] : cls)}' id='{id}' value={hasValue}");
    }

    return 0;
}

static int Dump()
{
    using var claude = new ClaudeWindow();
    var window = claude.Find();
    if (window is null)
    {
        Console.WriteLine("Claude window not found");
        return 2;
    }

    Console.WriteLine($"shown: {ClaudeWindow.ShownTitle(window) ?? "(none)"}");

    foreach (var type in new[] { ControlType.RadioButton, ControlType.Edit, ControlType.Document })
    {
        var items = window.FindAllDescendants(cf => cf.ByControlType(type));
        Console.WriteLine($"--- {type}: {items.Length}");
        foreach (var item in items.Take(15))
            Console.WriteLine($"  '{ClaudeWindow.Normalize(item.Properties.Name.ValueOrDefault)}'");
    }

    var buttons = window.FindAllDescendants(cf => cf.ByControlType(ControlType.Button));
    Console.WriteLine($"--- Button: {buttons.Length}");
    foreach (var button in buttons.Take(60))
        Console.WriteLine($"  '{ClaudeWindow.Normalize(button.Properties.Name.ValueOrDefault)}'");

    return 0;
}
