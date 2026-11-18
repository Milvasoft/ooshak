using System.ComponentModel;
using System.Diagnostics;

namespace Ooshak.Core.Mac;

/// <summary>
/// Drives the Claude desktop app on macOS through the Accessibility API, mirroring the Windows
/// version: the same names and editor classes, read from the Chromium tree Electron exposes.
/// </summary>
public sealed class MacClaudeWindow : IClaudeDesktop
{
    private const string DocumentSuffix = " - Claude Code";
    private const string PromptName = "Prompt";
    private const string PromptClass = "ProseMirror";
    private const string FocusedClass = "ProseMirror-focused";
    private const string ProcessName = "Claude";
    private const int MaxElements = 20_000;
    private const ushort KeyV = 9;
    private const ulong CommandFlag = 0x00100000;

    private static readonly TimeSpan SwitchTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ElementTimeout = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan PasteSettle = TimeSpan.FromMilliseconds(150);

    public DeliveryStatus Deliver(string title, string text, bool restoreView, string? localId = null)
    {
        using var app = OpenApp(out var pid);
        if (app is null)
            return DeliveryStatus.WindowNotFound;

        using (var tree = Scan(app))
        {
            var previous = ShownTitle(tree);
            if (previous != title)
            {
                if (IsUserTyping(tree))
                    return DeliveryStatus.UserIsTyping;

                var switched = Show(app, tree, title, localId);
                if (switched != DeliveryStatus.Sent)
                    return switched;
            }

            try
            {
                return Send(app, pid, text);
            }
            finally
            {
                if (restoreView && previous is not null && previous != title)
                {
                    using var now = Scan(app);
                    Show(app, now, previous);
                }
            }
        }
    }

    public (string? Shown, string? Prompt, bool HasSend) ReadState()
    {
        using var app = OpenApp(out _);
        if (app is null)
            return (null, null, false);

        using var tree = Scan(app);
        var prompt = FindPrompt(tree);
        return (ShownTitle(tree), prompt is null ? null : ReadValue(prompt), FindButton(tree, "Send") is not null);
    }

    public bool ClearIfEquals(string text)
    {
        using var app = OpenApp(out _);
        if (app is null)
            return false;

        using var tree = Scan(app);
        var prompt = FindPrompt(tree);
        return prompt is not null && ReadValue(prompt) == text.Trim() && prompt.SetString("AXValue", "");
    }

    private static DeliveryStatus Send(AxElement app, int pid, string text)
    {
        using (var tree = WaitForTree(app, t => FindPrompt(t) is not null, ElementTimeout))
        {
            var prompt = FindPrompt(tree);
            if (prompt is null)
                return DeliveryStatus.PromptNotFound;

            // A draft belongs to the user: merging speech into it would send their half-written text.
            var draft = ReadValue(prompt);
            if (draft.Length > 0 && draft != text.Trim())
                return DeliveryStatus.TargetHasDraft;

            if (draft.Length == 0)
                Type(prompt, pid, text);
        }

        using (var tree = WaitForTree(app, t => FindButton(t, "Send") is not null, ElementTimeout))
        {
            var send = FindButton(tree, "Send");
            if (send is null)
            {
                // Never leave our text behind where the user could send it by accident.
                FindPrompt(tree)?.SetString("AXValue", "");
                return DeliveryStatus.NoSendButton;
            }

            send.Perform("AXPress");
        }

        var cleared = WaitUntil(() =>
        {
            using var tree = Scan(app);
            return FindPrompt(tree) is { } p && ReadValue(p).Length == 0;
        }, ElementTimeout);

        return cleared ? DeliveryStatus.Sent : DeliveryStatus.NotConfirmed;
    }

    // Chromium accepts a new value for some editors and ignores it for others, so when the value does
    // not take, the text goes in through the clipboard the way a user would paste it.
    private static void Type(AxElement prompt, int pid, string text)
    {
        if (prompt.SetString("AXValue", text) && ReadValue(prompt) == text.Trim())
            return;

        prompt.SetTrue("AXFocused");
        var saved = Clipboard.Read();
        Clipboard.Write(text);
        PressPaste(pid);
        Thread.Sleep(PasteSettle);
        Clipboard.Write(saved);
    }

    private static void PressPaste(int pid)
    {
        foreach (var down in new[] { true, false })
        {
            var key = MacAccessibility.CGEventCreateKeyboardEvent(IntPtr.Zero, KeyV, down);
            MacAccessibility.CGEventSetFlags(key, CommandFlag);
            MacAccessibility.CGEventPostToPid(pid, key);
            MacAccessibility.CFRelease(key);
        }
    }

    private static DeliveryStatus Show(AxElement app, AxTree tree, string title, string? localId = null)
    {
        var entry = FindButton(tree, title);
        if (entry is not null)
            entry.Perform("AXPress");
        else if (localId is null || !OpenByLink(localId))
            return DeliveryStatus.SessionNotInSidebar;

        var shown = WaitUntil(() =>
        {
            using var now = Scan(app);
            return ShownTitle(now) == title;
        }, SwitchTimeout);

        return shown ? DeliveryStatus.Sent : DeliveryStatus.SwitchTimedOut;
    }

    private static bool OpenByLink(string localId)
    {
        try
        {
            using var open = Process.Start(new ProcessStartInfo("/usr/bin/open") { ArgumentList = { ClaudeDesktop.SessionLink(localId) } });
            return open is not null;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static bool IsUserTyping(AxTree tree)
    {
        var prompt = FindPrompt(tree);
        return prompt is not null
            && prompt.Strings("AXDOMClassList").Contains(FocusedClass)
            && ReadValue(prompt).Length > 0;
    }

    private static string? ShownTitle(AxTree tree)
    {
        foreach (var element in tree.Elements)
        {
            if (element.Role != "AXWebArea")
                continue;

            var name = NameOf(element);
            if (name.EndsWith(DocumentSuffix, StringComparison.Ordinal))
                return name[..^DocumentSuffix.Length];
        }

        return null;
    }

    private static AxElement? FindPrompt(AxTree tree)
    {
        AxElement? anyEditor = null;
        foreach (var element in tree.Elements)
        {
            if (element.Role is not ("AXTextArea" or "AXTextField"))
                continue;
            if (!element.Strings("AXDOMClassList").Contains(PromptClass))
                continue;
            if (NameOf(element) == PromptName)
                return element;
            anyEditor ??= element;
        }

        return anyEditor;
    }

    private static AxElement? FindButton(AxTree tree, string name) =>
        tree.Elements.FirstOrDefault(e => e.Role == "AXButton" && NameOf(e) == name);

    // Chromium puts an aria-label in the description and visible text in the title.
    private static string NameOf(AxElement element) =>
        ClaudeText.Normalize(NonEmpty(element.String("AXTitle")) ?? NonEmpty(element.String("AXDescription")) ?? "");

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string ReadValue(AxElement element) => (element.String("AXValue") ?? "").Trim();

    private static AxElement? OpenApp(out int pid)
    {
        pid = 0;
        var process = Process.GetProcessesByName(ProcessName).OrderBy(p => p.Id).FirstOrDefault();
        if (process is null)
            return null;

        pid = process.Id;
        var app = AxElement.Application(pid);

        // Electron builds its accessibility tree only for assistive apps that ask for it.
        app.SetTrue("AXManualAccessibility");
        return app;
    }

    private static AxTree Scan(AxElement app)
    {
        var tree = new AxTree();
        var window = app.Element("AXMainWindow") ?? app.Element("AXFocusedWindow") ?? app.Elements("AXWindows").FirstOrDefault();
        if (window is null)
            return tree;

        var pending = new Stack<AxElement>();
        pending.Push(window);
        while (pending.Count > 0 && tree.Elements.Count < MaxElements)
        {
            var element = pending.Pop();
            tree.Elements.Add(element);
            var children = element.Elements("AXChildren");
            for (var i = children.Count - 1; i >= 0; i--)
                pending.Push(children[i]);
        }

        foreach (var left in pending)
            left.Dispose();

        return tree;
    }

    private static AxTree WaitForTree(AxElement app, Func<AxTree, bool> ready, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            var tree = Scan(app);
            if (ready(tree) || watch.Elapsed >= timeout)
                return tree;

            tree.Dispose();
            Thread.Sleep(PollInterval);
        }
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            if (condition())
                return true;
            Thread.Sleep(PollInterval);
        }

        return condition();
    }

    public void Dispose()
    {
    }

    private sealed class AxTree : IDisposable
    {
        public List<AxElement> Elements { get; } = [];

        public void Dispose()
        {
            foreach (var element in Elements)
                element.Dispose();
        }
    }
}

/// <summary>The general pasteboard, through pbcopy and pbpaste.</summary>
public static class Clipboard
{
    public static string Read() => Run("/usr/bin/pbpaste", null);

    public static void Write(string text) => Run("/usr/bin/pbcopy", text);

    private static string Run(string tool, string? input)
    {
        var start = new ProcessStartInfo(tool)
        {
            RedirectStandardInput = input is not null,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            StandardInputEncoding = input is null ? null : new System.Text.UTF8Encoding(false),
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        start.Environment["LANG"] = "en_US.UTF-8";

        using var process = Process.Start(start)!;
        if (input is not null)
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }
}
